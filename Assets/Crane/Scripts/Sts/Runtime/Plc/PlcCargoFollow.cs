using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>라이브 PLC 화물 — PLC 잠금 신호가 오르면 스프레더 밑 컨테이너를 매달고 내리면 놓는다. PLC 가 진실이라 콘 안착 게이트는 없다.</summary>
    [RequireComponent(typeof(PlcBridge))]
    [DisallowMultipleComponent]
    public sealed class PlcCargoFollow : MonoBehaviour
    {
        [Tooltip("잠금 순간 스프레더 기준점과 컨테이너 윗면 중심의 허용 거리(실척 m) — 수평·수직.")]
        [SerializeField] float pickRadiusM = 0.5f, pickHeightM = 0.15f;   // PLC 위치결정 ±3cm — 높이는 하이큐브 차(0.305m)보다 작게 해 일반·하이큐브를 헷갈리지 않는다

        StsCrane crane;
        PlcBridge bridge;
        SpreaderGrabber grabber;
        SpreaderLockAnimator lockAnim;
        SpreaderTelescope telescope;      // STS 20/40ft
        RtgSpreaderTelescope rtgTele;     // RTG 20/40/45ft
        bool was;
        Transform heldYardRoot;   // 야드에서 집었으면 놓을 때 Yard_Containers 밑으로 되돌린다(다음 집기 후보에 남게)
        System.Threading.Tasks.Task<string> pending;
        bool needSync = true;   // 접속 직후·놓을 때마다·30초마다 서버 화물 상태(/cargo)에 씬을 맞춘다 — 늦게 뜬 XR 도 같은 배치
        float nextSync;

        [System.Serializable] class Moved { public float[] o, c; }
        [System.Serializable] class Cargo { public Moved[] boxes; public float[] held; }
        bool primed;   // 접속 순간의 잠금 상태는 기준일 뿐 — 이미 매단 채 접속하면 상승 엣지로 오인해 헛집기를 했다
        readonly Dictionary<Transform, Vector3> origin = new();   // 컨테이너마다 씬 시작 때 윗면 중심 = 서버 o — 자리로만 짝지으면 같은 칸에 놓인 다른 컨테이너를 헷갈렸다

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            bridge = GetComponent<PlcBridge>();
            grabber = GetComponent<SpreaderGrabber>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
            telescope = GetComponentInChildren<SpreaderTelescope>(true);
            rtgTele = GetComponentInChildren<RtgSpreaderTelescope>(true);
        }

        void LateUpdate()
        {
            if (crane == null || bridge == null || !bridge.isActiveAndEnabled || !bridge.Active || bridge.Measuring || (PortDemoDirector.Spectator && CraneLink.Syncs(crane))) return;   // 관전자는 호스트가 보내는 크레인이면 그 화물을 따른다
            bool locked = bridge.Latest.TwistLockLocked;
            if (!primed)
            {
                if (bridge.Source == null || !bridge.Source.TryRead(out _)) return;
                was = locked; primed = true; needSync = true;
                foreach (var t in Candidates())
                {
                    if (t.TryGetComponent<Rigidbody>(out var rb)) { rb.isKinematic = true; rb.useGravity = false; }
                    if (!t.IsChildOf(crane.transform) && SceneUtil.TryBounds(t, out var b)) origin[t] = new Vector3(b.center.x, b.max.y, b.center.z);
                }   // PLC 가 진실 — 배 화물도 놓은 자리 그대로(중력으로 굴러 PLC 자세와 어긋나지 않게)
            }
            if (locked && !was) Pick();
            else if (!locked && was) Place();
            was = locked;
            Sync();
        }

        void Sync()
        {
            if (!(bridge.Source is ServerPlcSource src) || !src.IsConnected || !bridge.RangesResolved) return;
            if (pending == null && (needSync || Time.unscaledTime >= nextSync))
            {
                pending = src.GetText("/cargo?crane=" + System.Uri.EscapeDataString(crane.name));
                needSync = false;
                nextSync = Time.unscaledTime + 30f;
            }
            if (pending == null || !pending.IsCompleted) return;
            if (pending.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) Apply(pending.Result);
            pending = null;
        }

        // 원래 자리(o)가 신원 — 씬 시작 때 그 자리에 있던 컨테이너를 지금 자리(c)로 옮긴다.
        void Apply(string json)
        {
            var data = JsonUtility.FromJson<Cargo>(json);
            if (data == null) return;
            data.boxes ??= new Moved[0];
            var sway = CraneSway.Of(crane);
            Vector3 gp = (grabber != null ? grabber.GrabPoint() : ((Component)crane.Spreader).transform.position) - (sway != null ? sway.Offset : Vector3.zero);
            float tol = pickRadiusM * crane.ModelScale, tolY = pickHeightM * crane.ModelScale;
            int moved = 0;
            foreach (var m in data.boxes)
            {
                if (m.o == null || m.c == null || m.o.Length < 3 || m.c.Length < 3) continue;
                var t = Origin(Pose(m.o, gp), tol, tolY);
                if (t == null || t.IsChildOf(crane.transform) || !SceneUtil.TryBounds(t, out var b)) continue;
                var d = Pose(m.c, gp) - new Vector3(b.center.x, b.max.y, b.center.z);
                if (d.magnitude < 0.02f * crane.ModelScale) continue;   // 이미 그 자리(PLC ±3cm)
                t.position += d;
                if (t.TryGetComponent<Rigidbody>(out var rb)) { rb.position = t.position; if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; }
                moved++;
            }
            var all = new List<Transform>();
            foreach (var t in Candidates()) if (!t.IsChildOf(crane.transform)) all.Add(t);
            if (moved > 0) Debug.Log($"[PlcCargo] {crane.name} 화물 상태 맞춤 — {moved}개를 라이브 PLC 의 지금 자리로");
            // 운반 중에 접속했으면 그 컨테이너를 스프레더 밑에 매단다(집기 전 자리에서 찾아서).
            if (data.held != null && data.held.Length >= 3 && was && crane.Attach != null && !crane.Attach.HasContainer)
            {
                var h = Find(all, Pose(data.held, gp), tol, tolY, new HashSet<Transform>());
                if (h != null && SceneUtil.TryBounds(h, out var hb))
                {
                    heldYardRoot = h.parent != null && h.parent.name == "Yard_Containers" ? h.parent : null;
                    h.position += gp - new Vector3(hb.center.x, hb.max.y, hb.center.z);
                    Lock(h, gp);
                    Debug.Log($"[PlcCargo] {crane.name} 운반 중 접속 — {h.name} 을 스프레더에 매닮");
                }
            }
        }

        Vector3 Pose(float[] v, Vector3 gp) => bridge.WorldAtPose(new PlcSnapshot { GtPosition = v[0], TrPosition = v[1], HoPosition = v[2] }, gp);

        Transform Origin(Vector3 top, float tol, float tolY)
        {
            Transform best = null;
            float bd = float.MaxValue;
            foreach (var kv in origin)
            {
                if (kv.Key == null) continue;
                float d = new Vector2(kv.Value.x - top.x, kv.Value.z - top.z).magnitude;
                if (d <= tol && Mathf.Abs(kv.Value.y - top.y) <= tolY && d < bd) { bd = d; best = kv.Key; }
            }
            return best;
        }

        static Transform Find(List<Transform> all, Vector3 top, float tol, float tolY, HashSet<Transform> used)
        {
            Transform best = null;
            float bd = float.MaxValue;
            foreach (var t in all)
            {
                if (used.Contains(t) || !SceneUtil.TryBounds(t, out var b)) continue;
                float d = new Vector2(b.center.x - top.x, b.center.z - top.z).magnitude;
                if (d <= tol && Mathf.Abs(b.max.y - top.y) <= tolY && d < bd) { bd = d; best = t; }
            }
            return best;
        }

        void Pick()
        {
            if (crane.Attach == null || crane.Attach.HasContainer) return;
            Vector3 gp = grabber != null ? grabber.GrabPoint() : ((Component)crane.Spreader).transform.position;
            float r = pickRadiusM * crane.ModelScale, h = pickHeightM * crane.ModelScale, best = float.MaxValue;
            Transform c = null;
            foreach (var t in Candidates())
            {
                if (t.IsChildOf(crane.transform) || !SceneUtil.TryBounds(t, out var b)) continue;
                float dxz = new Vector2(b.center.x - gp.x, b.center.z - gp.z).magnitude;
                if (dxz <= r && Mathf.Abs(b.max.y - gp.y) <= h && dxz < best) { best = dxz; c = t; }
            }
            if (c == null)
            {
                Debug.LogWarning($"[PlcCargo] {crane.name} 잠금 — 스프레더 밑에 컨테이너가 없다(PLC 자세와 씬 배치가 어긋남). gp={gp}");
                return;
            }
            heldYardRoot = c.parent != null && c.parent.name == "Yard_Containers" ? c.parent : null;
            Lock(c, gp);
        }

        // 잠금 = 코너캐스팅에 콘이 물린 상태 — 스프레더를 컨테이너 길이로 맞추고, 컨테이너를 스프레더 중심·방향에 맞춘 뒤 매단다.
        void Lock(Transform c, Vector3 gp)
        {
            if (!SceneUtil.TryBounds(c, out var b)) return;
            float lenM = Mathf.Max(b.size.x, b.size.z) / crane.ModelScale;
            float offXZ = new Vector2(b.center.x - gp.x, b.center.z - gp.z).magnitude / crane.ModelScale, gapY = (b.max.y - gp.y) / crane.ModelScale;
            if (telescope != null) telescope.Set40(lenM > 9f);
            if (rtgTele != null) rtgTele.SetSize(lenM > 13f ? RtgSpreaderTelescope.Size.Ft45 : lenM > 9f ? RtgSpreaderTelescope.Size.Ft40 : RtgSpreaderTelescope.Size.Ft20);
            // 긴 축끼리 맞춘다 — STS 스프레더는 자기 축이 90° 돌아 있어 오브젝트 회전값으로 맞추면 컨테이너를 90° 돌려 잡았다.
            float d = Mathf.DeltaAngle(LongYaw(c), LongYaw(((Component)crane.Spreader).transform));
            if (Mathf.Abs(d) > 90f) d -= Mathf.Sign(d) * 180f;   // 컨테이너는 앞뒤 대칭 — 가까운 쪽으로만 돌린다
            c.RotateAround(new Vector3(b.center.x, b.max.y, b.center.z), Vector3.up, d);
            if (SceneUtil.TryBounds(c, out b)) c.position += new Vector3(gp.x - b.center.x, 0f, gp.z - b.center.z);
            if (!crane.Attach.Attach(c)) return;
            if (lockAnim != null) lockAnim.SetLocked(true);
            QaLog.Info("PLCCARGO", "lock", $"crane={crane.name} box={c.name} len={lenM:F1}m tele={(rtgTele != null ? (lenM > 13f ? "45" : lenM > 9f ? "40" : "20") : telescope != null ? (lenM > 9f ? "40" : "20") : "-")} offXZ={offXZ * 1000f:F0}mm gapY={gapY * 1000f:F0}mm");
        }

        // 수평 긴 축의 방향(도) — 메시를 이 물체 기준으로 잰 크기에서 가장 긴 수평 축.
        static float LongYaw(Transform t)
        {
            var size = Vector3.zero;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var w = mf.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)));
                    var l = t.InverseTransformPoint(w);
                    size = Vector3.Max(size, new Vector3(Mathf.Abs(l.x), Mathf.Abs(l.y), Mathf.Abs(l.z)));
                }
            }
            Vector3 best = t.forward; float bestLen = -1f;
            Vector3[] axes = { t.right, t.up, t.forward };
            for (int i = 0; i < 3; i++)
            {
                float len = size[i] * t.lossyScale[i];
                if (Mathf.Abs(axes[i].y) < 0.7f && len > bestLen) { bestLen = len; best = axes[i]; }
            }
            return Mathf.Atan2(best.x, best.z) * Mathf.Rad2Deg;
        }

        /// <summary>집을 수 있는 컨테이너 — 물리 화물(배, 이름에 Container) + 야드 정적 배치(Yard_Containers 자식, Rigidbody 없음). 배치 내보내기와 같은 목록.</summary>
        public static IEnumerable<Transform> Candidates()
        {
            foreach (var rb in FindObjectsByType<Rigidbody>())
                if (rb.name.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) >= 0) yield return rb.transform;
            var yard = GameObject.Find("Yard_Containers");
            if (yard != null) foreach (Transform t in yard.transform) yield return t;
        }

        void Place()
        {
            if (crane.Attach == null || !crane.Attach.HasContainer) return;
            var held = crane.Attach.AttachedContainer;
            crane.Attach.Detach();   // grabber.Release 는 야드 칸 중앙으로 몰래 정렬한다 — PLC 가 진실이니 놓은 자리 그대로
            if (lockAnim != null) lockAnim.SetLocked(false);
            if (rtgTele != null) rtgTele.SetSize(RtgSpreaderTelescope.Size.Ft40);   // 빈 스프레더 기준자세(SpreaderGrabber.Release 와 같다)
            if (held != null && heldYardRoot != null) held.SetParent(heldYardRoot, true);
            heldYardRoot = null;
            needSync = true;
        }
    }
}
