using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CraneDemoRunner
    {
        void Pick(Job j)
        {
            CheckPick(j.box);
            Vector3 pos = j.box.position; Quaternion rot = j.box.rotation;
            if (!crane.Attach.Attach(j.box)) return;
            j.box.SetPositionAndRotation(pos, rot);   // Attach 는 부착점 원점으로 스냅한다 — 잰 자세 그대로 둔다
            LiftToContact(j.box);
            HideColliders(j.box);
            if (lockAnim != null) lockAnim.SetLocked(true);
            if (tele != null) tele.Set40(j.ft40);
            if (rtgTele != null) rtgTele.SetSize(j.ft40 ? RtgSpreaderTelescope.Size.Ft40 : RtgSpreaderTelescope.Size.Ft20);
        }

        // 집은 컨테이너 원자세가 받침(발자국 40% 이상 겹친 아래 단)을 파고든 채면(적재 오차) 멈춘 채 접촉면까지 올린다.
        //   안 그러면 들기 시작할 때 통과방지 클램프가 한 번에 밀어 올려 권상 가속이 튄다.
        void LiftToContact(Transform box)
        {
            if (crane.Spreader == null || !SceneUtil.TryBounds(box, out var b)) return;
            float top = b.min.y, reach = b.min.y + SupportTolM * StsConfig.ModelScale, area = b.size.x * b.size.z;
            foreach (var o in OtherBounds(box))
            {
                float ox = Mathf.Min(b.max.x, o.max.x) - Mathf.Max(b.min.x, o.min.x);
                float oz = Mathf.Min(b.max.z, o.max.z) - Mathf.Max(b.min.z, o.min.z);
                if (ox > 0f && oz > 0f && ox * oz >= 0.4f * area && o.max.y <= reach) top = Mathf.Max(top, o.max.y);
            }
            float pen = top - b.min.y;
            if (pen <= 0f) return;
            crane.Spreader.MoveTo(crane.Spreader.Current + pen / crane.Spreader.WorldPerUnit);
            Debug.Log($"[PortDemo] {name}: {box.name} 원자세가 받침을 {pen / StsConfig.ModelScale * 1000f:F2}mm 파고듦 — 멈춘 채 접촉면까지 올림");
        }

        void Place(Job j, Vector3 bottom, bool toAway)
        {
            var c = crane.Attach.Detach(j.parent);
            if (c == null) return;
            c.SetPositionAndRotation(bottom + j.pivot, j.rot);   // SeatLift 만큼 띄워 내린 것을 정확한 자리로
            // 진단 로그 — 어느 상자를 어디에 놓았는지 실척으로 남긴다(모델 단위는 1/24 라 안 읽힌다).
            if (YardGrid.TrySnapXZ(bottom, Mathf.Max(j.size.x, j.size.z), out var cell))
                Debug.Log($"[PortDemo] {name}: {c.name} {(toAway ? "옮김" : "되돌림")} — 실척 " +
                          $"({bottom.x * StsConfig.InvModelScale:F2}, {bottom.z * StsConfig.InvModelScale:F2})m · " +
                          $"칸중심 ({cell.x * StsConfig.InvModelScale:F2}, {cell.z * StsConfig.InvModelScale:F2})m · " +
                          $"이탈 {Vector3.Distance(new Vector3(bottom.x, 0f, bottom.z), new Vector3(cell.x, 0f, cell.z)) * StsConfig.InvModelScale:F3}m");
            // 위 진단은 '명령한 자리'라 늘 0 — 결과는 렌더러 AABB 중심으로 따로 잰다(크기가 갈리면 다른 칸 기준).
            if (SceneUtil.TryBounds(c, out var got) && YardGrid.TrySnapXZ(got.center, Mathf.Max(got.size.x, got.size.z), out var gotCell))
            {
                float dr = new Vector2(got.center.x - gotCell.x, got.center.z - gotCell.z).magnitude * StsConfig.InvModelScale;
                if (dr > 0.05f)
                    Debug.LogWarning($"[PortDemo] {c.name} 결과 이탈 {dr:F3}m — AABB중심 실척 " +
                        $"({got.center.x * StsConfig.InvModelScale:F2}, {got.center.z * StsConfig.InvModelScale:F2})m · " +
                        $"칸중심 ({gotCell.x * StsConfig.InvModelScale:F2}, {gotCell.z * StsConfig.InvModelScale:F2})m · " +
                        $"크기 계획 {Mathf.Max(j.size.x, j.size.z) * StsConfig.InvModelScale:F2}m ↔ 측정 " +
                        $"{Mathf.Max(got.size.x, got.size.z) * StsConfig.InvModelScale:F2}m");
            }
            ShowColliders();
            var rb = c.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 안벽 땅엔 바닥 콜라이더가 없을 수 있다 — 옮겨 둔 동안은 kinematic, 제자리에 돌아가면 원래 물리로
                rb.isKinematic = toAway || j.kinematic;
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            if (lockAnim != null) lockAnim.SetLocked(false);
            if (rtgTele != null) rtgTele.SetSize(RtgSpreaderTelescope.Size.Ft40);   // 빈 스프레더 기준자세(SpreaderGrabber.Release 와 같음)
            CheckPlaced(c, toAway);
            Debug.Log($"[PortDemo] {name} 놓기 {++placed} — {c.name} {(toAway ? "옮김" : "되돌림")}");
        }

        // 사람에게 넘길 때 — 자동으로 바꿔 둔 것을 원래대로(수동 조종·물리는 원래 규칙).
        void Suspend()
        {
            if (!auto) return;
            auto = false;
            vG = vT = vH = 0f;
            if (grabber != null) grabber.SetPusherActive(pusherWas);
            if (gantryMover != null) gantryMover.StopOnObstacle = stopWas;
            if (crane.Trolley is TrolleyMover tm) tm.StopOnObstacle = trolleyStopWas;
            ShowColliders();
            Debug.Log($"[PortDemo] {name}: 멈춤 — 접근·조종");
        }

        void Resume()
        {
            if (auto) return;
            auto = true;
            if (grabber != null) { pusherWas = grabber.IsPusherActive; grabber.SetPusherActive(false); }   // 하강 중 푸셔가 대상·이웃을 민다
            if (gantryMover != null) { stopWas = gantryMover.StopOnObstacle; gantryMover.StopOnObstacle = false; }   // 컨테이너를 장애물로 오인해 서지 않게
            if (crane.Trolley is TrolleyMover tm) { trolleyStopWas = tm.StopOnObstacle; tm.StopOnObstacle = false; }   // 같은 이유 — 윗면 바로 위 Align 에서 막혔다
            var held = crane.Attach.AttachedContainer;
            if (held != null && jobs.Exists(x => x.box == held)) HideColliders(held);
            if (started) Debug.Log($"[PortDemo] {name}: 재개");
            started = true;
        }

        void HideColliders(Transform t)
        {
            ShowColliders();
            foreach (var c in t.GetComponentsInChildren<Collider>())
                if (c.enabled) { c.enabled = false; hidden.Add(c); }
        }

        void ShowColliders()
        {
            foreach (var c in hidden) if (c != null) c.enabled = true;
            hidden.Clear();
        }
    }
}
