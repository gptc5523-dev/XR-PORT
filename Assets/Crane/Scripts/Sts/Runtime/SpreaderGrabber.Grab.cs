using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class SpreaderGrabber
    {
        void Refresh()
        {
            // 강체 전부를 담고 크레인 자식 제외는 쓰는 쪽에서 매 틱 건다.
            //   ★ 스캔 때 거르면 재스캔 전까지 통과방지가 그 컨테이너를 못 보고 관통한다.
            var all = FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude);
            bodies.Clear(); bodyRends.Clear();
            foreach (var rb in all)
                if (rb != null) { bodies.Add(rb); bodyRends.Add(rb.GetComponentsInChildren<Renderer>()); }
            nextRefresh = Time.time + MinRescanInterval;
        }

        // bodies[i] 의 렌더러 바운즈 합 — SceneUtil.TryBounds 와 같은 기준(꺼진 오브젝트만 뺀다), 다만 렌더러는 Refresh 때 모아 둔 것.
        bool BodyBounds(int i, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var rd in bodyRends[i])
            {
                if (rd == null || !rd.gameObject.activeInHierarchy) continue;
                if (any) b.Encapsulate(rd.bounds);
                else { b = rd.bounds; any = true; }
            }
            return any;
        }

        /// <summary>콘 삽입 깊이(모델 단위).</summary>
        float InsertU => InsertDepthMeters * StsConfig.ModelScale;

        // 체결 인정 삽입 밴드 = InsertU × [최소, 최대] — 공중 체결과 옆면으로 파고든 상태를 거른다.
        const float InsertMinFrac = 0.25f, InsertMaxFrac = 2f;

        // 빈 스프레더가 c 의 코너캐스팅 위에 안착 정렬됐는지(gp=콘 평균): registerTolXZ 이내 동심 + gap 이 삽입 밴드 안.
        bool IsSeatedOver(Transform c, Vector3 gp, out float dXZ, out float gap)
        {
            dXZ = float.MaxValue; gap = float.MaxValue;
            if (!SceneUtil.TryBounds(c, out Bounds b)) return false;
            float dx = gp.x - b.center.x, dz = gp.z - b.center.z;
            dXZ = Mathf.Sqrt(dx * dx + dz * dz);
            // gap = 콘 바닥 − 윗면(음수 = 박힘). 원점이 콘 끝이 아니므로 실측 기하로 잰다.
            gap = ConeBottomY() - b.max.y;
            bool centered = dXZ <= registerTolXZ;
            bool inserted = gap <= -InsertU * InsertMinFrac && gap >= -InsertU * InsertMaxFrac;
            return centered && inserted;
        }

        /// <summary>VR 컨트롤러 Y 버튼 — 비어 있으면 트위스트락 근처 컨테이너를 잡는다(이미 잡았으면 무시).</summary>
        public void Grab()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || attach.HasContainer) return;

            Vector3 gp = GrabPoint();
            var c = FindNearest(gp, out float dist);
            // QA S-PASS-6: 유효 반경 = min(grabRange, maxGrabRange).
            float effRange = Mathf.Min(grabRange, maxGrabRange);
            bool inRange = c != null && dist <= effRange;
            // 안착 평가 1회 — 게이트와 try 로그가 같은 값을 쓰게.
            bool seated = false; float segXZ = float.MaxValue, segGap = float.MaxValue;
            if (c != null) seated = IsSeatedOver(c, gp, out segXZ, out segGap);
            bool willGrab = inRange && (!requireSeatedRegistration || seated);
            if (debugLog)
                Debug.Log($"[Crane] 집기 시도 — 기준점(트위스트락) {gp}, 후보 {bodies.Count}개, " +
                          $"최근접 {(c != null ? $"{c.name} (거리 {dist:F3} / 유효범위 {effRange:F3})" : "없음")}");
            QaLog.Info("GRAB", "try",
                $"gp={QaLog.V(gp)} candidates={bodies.Count} nearest={(c != null ? c.name : "none")} " +
                $"dist={(c != null ? QaLog.F(dist) : "inf")} effRange={QaLog.F(effRange)} " +
                $"seated={seated} dXZ={QaLog.F(segXZ)} gap={QaLog.F(segGap)} grabbed={willGrab}");
            if (c == null) return;

            // 코너 안착 게이트 — 근접만으론 안 잠근다(옆면·공중·측면진입 거부).
            if (requireSeatedRegistration && !seated)
            {
                if (debugLog)
                    Debug.Log($"[Crane] 집기 거부 — 코너 미정렬: 중심오차 {segXZ:F3}m(허용 {registerTolXZ:F3}), " +
                              $"콘 바닥−윗면 {segGap:F4}u(체결 밴드 {-InsertU * InsertMaxFrac:F4}~{-InsertU * InsertMinFrac:F4}u). " +
                              $"스프레더를 컨테이너 중심 위에서 콘이 박힐 때까지 내리세요(통과방지가 삽입 {InsertDepthMeters * 1000f:F0}mm 에서 멈춥니다).");
                QaLog.Info("GRAB", "reject",
                    $"reason=unseated dXZ={QaLog.F(segXZ)} tolXZ={QaLog.F(registerTolXZ)} gap={QaLog.F(segGap)}");
                return;
            }

            bool hasBounds = SceneUtil.TryBounds(c, out Bounds b);

            // 긴 축 길이로 텔레스코픽 20/40ft 자동 신축. 놓아도 유지.
            if ((telescope != null || rtgTele != null) && hasBounds)
            {
                float longSide = Mathf.Max(b.size.x, b.size.z);
                bool is40 = longSide > sizeThreshold;
                if (telescope != null) telescope.Set40(is40);
                if (rtgTele != null) rtgTele.SetSize(is40 ? RtgSpreaderTelescope.Size.Ft40 : RtgSpreaderTelescope.Size.Ft20);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 긴축 {longSide:F3}m → {(is40 ? "40ft" : "20ft")} 신축");
            }

            attach.Attach(c);   // 월드 자세 그대로 자식이 된다
            // 수평만 콘 중심에 맞춘다. 높이는 게이트가 이미 박힌 상태만 통과시키므로 건드리면 순간이동한다.
            if (hasBounds) c.position += new Vector3(gp.x - b.center.x, 0f, gp.z - b.center.z);

            // 하강 바닥 한계를 컨테이너 밑면 기준으로 — 컨테이너가 바닥에 닿고 멈추게.
            if (spreaderHoist != null && SceneUtil.TryBounds(c, out Bounds held))
            {
                // 축 목표 = Current + (floorTopY − 밑면Y) / WorldPerUnit — 축 해석(로컬/월드)과 무관.
                //   ★ 부모 position.y 기반 식은 worldVertical 크레인에서 바닥 아래까지 내려갔다.
                float floorMinY = spreaderHoist.Current + (floorTopY - held.min.y) / spreaderHoist.WorldPerUnit;
                spreaderHoist.SetFloorOffset(floorMinY - spreaderHoist.Min);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 밑면 기준 바닥 — 하강한계 +{floorMinY - spreaderHoist.Min:F3} (높이 {held.size.y:F3})");
                // QA S-PHYS-6: LowerLimit≤Max 유지(역전 시 호이스트 먹통).
                float lowerLimit = spreaderHoist.Min + spreaderHoist.FloorOffset;
                QaLog.Check("HOIST", "floorlimit", lowerLimit <= spreaderHoist.Max + 1e-4f,
                    $"heldHeight={QaLog.F(held.size.y)} floorMinY={QaLog.F(floorMinY)} offset={QaLog.F(spreaderHoist.FloorOffset)} " +
                    $"lowerLimit={QaLog.F(lowerLimit)} max={QaLog.F(spreaderHoist.Max)}");
            }

            if (lockAnim != null) lockAnim.SetLocked(true);

            // 운반 중 충돌 감지용 접점 릴레이 — 놓을 때 제거.
            if (c.GetComponent<LoadCollisionRelay>() == null)
                c.gameObject.AddComponent<LoadCollisionRelay>().owner = this;
        }

        /// <summary>VR 컨트롤러 X 버튼 — 잡고 있으면 놓는다(비어 있으면 무시).</summary>
        public void Release()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || !attach.HasContainer) return;

            var held = attach.AttachedContainer;
            if (held != null) SnapToYardCell(held);   // kinematic 인 동안 칸 정렬 — 물리와 싸우지 않게
            attach.Detach();
            if (held != null)
            {
                var relay = held.GetComponent<LoadCollisionRelay>();
                if (relay != null) Destroy(relay);
            }
            if (spreaderHoist != null) spreaderHoist.SetFloorOffset(0f);   // 빈 스프레더 바닥 한계 복원
            if (lockAnim != null) lockAnim.SetLocked(false);
            if (rtgTele != null) rtgTele.SetSize(RtgSpreaderTelescope.Size.Ft40);   // 빈 스프레더 = 40ft 기준자세
            if (debugLog) Debug.Log("[Crane] 놓기(Detach)");
        }

        // 놓을 때 야드 칸 중심·격자 축으로 정렬(SSOT = YardGrid). 높이 불변, 야드 밖·목표 칸 점유 시 건너뜀.
        void SnapToYardCell(Transform c)
        {
            if (!SceneUtil.TryBounds(c, out Bounds b)) return;
            float longSide = Mathf.Max(b.size.x, b.size.z);
            if (!YardGrid.TrySnapXZ(b.center, longSide, out Vector3 cell)) return;

            Quaternion rotWas = c.rotation;
            float yaw = YardGrid.SnapYawDeg(c.eulerAngles.y);
            c.rotation = Quaternion.Euler(c.eulerAngles.x, yaw, c.eulerAngles.z);
            // 회전 뒤 바운즈를 다시 잰다(원점 ≠ 바운즈 중심인 화물 대비).
            if (!SceneUtil.TryBounds(c, out Bounds nb)) { c.rotation = rotWas; return; }
            Vector3 delta = new Vector3(cell.x - nb.center.x, 0f, cell.z - nb.center.z);

            // 옮길 자리가 다른 컨테이너와 '교차'하면 원복. 0.98 은 닿음(적층)과 파고듦을 가르는 여유.
            var moved = new Bounds(nb.center + delta, nb.size * 0.98f);
            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                if (rb == null) continue;
                Transform t = rb.transform;
                if (t == c || t.IsChildOf(c) || c.IsChildOf(t) || t.IsChildOf(transform)) continue;
                if (BodyBounds(i, out Bounds ob) && moved.Intersects(ob))
                {
                    c.rotation = rotWas;
                    if (debugLog) Debug.Log($"[Crane] 야드 칸 정렬 건너뜀 — 목표 칸이 {t.name} 와(과) 겹침. 놓은 자리 그대로 둠.");
                    return;
                }
            }
            c.position += delta;
            if (debugLog)
                Debug.Log($"[Crane] 야드 칸 정렬 — 중심 x {nb.center.x:F4}→{cell.x:F4}, z {nb.center.z:F4}→{cell.z:F4}, yaw {yaw:F0}°");
        }

        // 기준점에서 (상한 적용된) grabRange 안의 가장 가까운 컨테이너(콜라이더 있으면 그것 우선)
        Transform FindNearest(Vector3 gp, out float dist)
        {
            dist = float.MaxValue;
            float range = Mathf.Min(grabRange, maxGrabRange);

            // 콜라이더 우선 — range 안 크레인 외부 Rigidbody 중 최근접.
            Transform nearestCol = null;
            float nearestColD = float.MaxValue;
            var hits = Physics.OverlapSphere(gp, range);
            foreach (var h in hits)
            {
                var rb = h.attachedRigidbody;
                if (rb == null || rb.transform.IsChildOf(transform)) continue;
                float d = Vector3.Distance(gp, h.ClosestPoint(gp));   // 콜라이더 표면까지 거리
                if (d < nearestColD) { nearestColD = d; nearestCol = rb.transform; }
            }
            if (nearestCol != null) { dist = nearestColD; return nearestCol; }

            // 렌더러 바운즈 최근접(콜라이더를 못 맞춘 경우)
            Transform best = null;
            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                if (rb == null || rb.transform.IsChildOf(transform)) continue;
                if (!BodyBounds(i, out Bounds b)) continue;
                float d = Vector3.Distance(gp, b.ClosestPoint(gp));   // 바운즈 표면까지 거리
                if (d <= range && d < dist) { dist = d; best = rb.transform; }
            }
            return best;
        }
    }
}
