using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 크레인 한 대의 시연 시나리오 — 컨테이너 5개를 옮기고, 다 옮기면 역순으로 제자리에 되돌리고, 반복한다.
    ///   STS: 작업 베이의 배 위 단(위가 빈 것) → 같은 주행 위치의 안벽(땅) 빈자리.
    ///   RTG: 다른 RTG 보다 나에게 가까운 야드 컨테이너 → 같은 주행 위치의 빈 열.
    ///
    /// 자리는 씬에서 잰다 — 컨테이너 렌더러 바운즈, 무버 WorldAxis(축 1 = 월드 몇), 부두 땅(TryGetLand), 콜라이더.
    /// 축은 CraneAxisProfile 정격 최고속·가속의 사다리꼴로 움직인다. 집을 때마다 컨테이너의 '지금' 자리에서 다시 잰다
    /// — 사람이 조종하다 옮겨 놓았어도 거기서 집는다. 닿지 않거나 StallSeconds 동안 목표에 못 다가가면 그 작업만 건너뛴다.
    ///
    /// 높이 기준은 부착점(SpreaderAttach.AttachAnchor = 컨테이너 윗면이 오는 곳, SpreaderGrabber.Grab 과 같은 규약).
    ///   SpreaderGrabber 통과방지 클램프가 '빈 부착점 ≥ 받침 윗면', '든 컨테이너 밑면 ≥ 받침 윗면'으로 스프레더를 되밀어서
    ///   목표를 SeatLift 만큼 띄워 잡고, 놓은 뒤 정확한 자리로 스냅한다. 안 띄우면 매 틱 서로 밀어 영영 안 끝난다.
    /// 잡기는 SpreaderGrabber.Grab 을 쓰지 않는다 — 계산한 자세 그대로라 코너 안착 게이트가 필요 없다(PlcCargoReplay 와 같음).
    ///
    /// 멈춤: <see cref="PortDemoDirector.Holds"/> 가 참이면 그 틱에 손을 떼고(푸셔·장애물정지·콜라이더 원복) 기다린다.
    /// 가설 구현·Quest 미검증.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CraneDemoRunner : MonoBehaviour
    {
        /// <summary>크레인당 옮기는 컨테이너 수 — 오너 지시 "각 5개".</summary>
        public const int Count = 5;
        const float SeatLiftMeters = 0.02f;   // 통과방지 클램프(topClearance 기본 0) 위로 확실히
        const float StallSeconds = 3f;

        [Tooltip("정격 속도·가속 배율. 1 = 실척 정격(CraneAxisProfile).")]
        [SerializeField] float speedScale = 1f;
        [Tooltip("내려놓는 자리 사이 여유(실척 m).")]
        [SerializeField] float slotGapMeters = 1f;
        [Tooltip("옮길 때 지나는 가장 높은 컨테이너 윗면 위로 더 띄우는 높이(실척 m).")]
        [SerializeField] float clearanceMeters = 2f;

        /// <summary>크레인들이 같이 쓰는 현장 — 서로 같은 컨테이너·자리를 잡지 않게 PortDemoDirector 가 한 벌 만든다.</summary>
        public sealed class Site
        {
            public readonly List<Transform> ship = new List<Transform>();
            public readonly List<Transform> yard = new List<Transform>();
            public readonly List<Bounds> occupied = new List<Bounds>();       // 컨테이너가 있거나 누가 내려놓기로 한 자리
            public readonly HashSet<Transform> claimed = new HashSet<Transform>();
            public readonly List<StsCrane> rtgs = new List<StsCrane>();
            public Bounds land;
            public bool haveLand;
        }

        sealed class Job
        {
            public Transform box, parent;
            public Quaternion rot;
            public Vector3 size, pivot;   // pivot = 원점 − 밑면 중심
            public Vector3 home, away;    // 밑면 중심(월드)
            public bool ft40, kinematic;
        }

        enum Result { Done, Skip, Abort }

        static readonly int[] Signs = { -1, 1 };

        StsCrane crane;
        SpreaderGrabber grabber;
        SpreaderLockAnimator lockAnim;
        SpreaderTelescope tele;
        RtgSpreaderTelescope rtgTele;
        RtgBogieSteering steer;
        GantryMover gantryMover;
        readonly List<Job> jobs = new List<Job>();
        readonly List<Collider> hidden = new List<Collider>();   // 옮기는 동안 끈 콜라이더 — 이웃 컨테이너를 밀지 않게
        float clearTopY;      // 옮길 때 컨테이너 밑면이 넘어야 할 높이
        float vG, vT, vH;     // 축 속도(실척 m/s, 부호 포함)
        Result result;
        bool stalled, auto, started, pusherWas, stopWas;
        int placed;

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            grabber = GetComponent<SpreaderGrabber>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
            tele = GetComponentInChildren<SpreaderTelescope>(true);
            rtgTele = GetComponentInChildren<RtgSpreaderTelescope>(true);
            steer = GetComponent<RtgBogieSteering>();
            gantryMover = crane.Gantry as GantryMover;
        }

        void OnDisable() => Suspend();

        /// <summary>옮길 컨테이너와 내려놓을 자리를 정한다 — 못 정하면 이 크레인은 서 있다(로그로 남긴다).</summary>
        public void Plan(Site site)
        {
            bool rtg = steer != null;
            if (crane.Trolley == null || crane.Spreader == null || crane.Attach == null || (!rtg && !site.haveLand))
            {
                Debug.LogWarning($"[PortDemo] {name}: 계획 0개 — 무버·부착 배선 또는 부두 땅 없음");
                return;
            }

            foreach (var t in rtg ? YardCandidates(site) : ShipCandidates(site))
            {
                if (jobs.Count == Count) break;
                if (!TryBounds(t, out var b)) continue;
                var rb = t.GetComponent<Rigidbody>();
                var j = new Job
                {
                    box = t, parent = t.parent, rot = t.rotation, size = b.size,
                    home = new Vector3(b.center.x, b.min.y, b.center.z),
                    ft40 = Mathf.Max(b.size.x, b.size.z) > 9f * StsConfig.ModelScale,   // 20ft 6.06m · 40ft 12.19m
                    kinematic = rb == null || rb.isKinematic,
                };
                j.pivot = t.position - j.home;
                if (!FindSlot(site, j, rtg ? j.home.y : site.land.max.y)) continue;
                site.claimed.Add(t);
                jobs.Add(j);
            }

            // 이동 높이 = 옮기는 띠(작업 주행 위치 ± 컨테이너 길이 × 트롤리 가동범위) 안 가장 높은 컨테이너 윗면 + 여유
            Vector3 gd = GantryDir(), td = TrolleyDir();
            float gLo = float.MaxValue, gHi = float.MinValue, len = 0f, top = float.MinValue;
            foreach (var j in jobs)
            {
                float g = Vector3.Dot(j.home, gd);
                gLo = Mathf.Min(gLo, g); gHi = Mathf.Max(gHi, g); len = Mathf.Max(len, Extent(j.size, gd));
                top = Mathf.Max(top, Mathf.Max(j.home.y, j.away.y) + j.size.y);
            }
            TrolleyReach(out float tLo, out float tHi);
            foreach (var o in site.occupied)
            {
                float og = Vector3.Dot(o.center, gd), ot = Vector3.Dot(o.center, td);
                if (og >= gLo - len && og <= gHi + len && ot >= tLo && ot <= tHi) top = Mathf.Max(top, o.max.y);
            }
            clearTopY = top + clearanceMeters * StsConfig.ModelScale;
            Debug.Log($"[PortDemo] {name}: 계획 {jobs.Count}개 — {(rtg ? "야드 → 빈 열" : "배 위 단 → 안벽")}, 이동 높이 y={clearTopY:F3}");
        }

        public void Go()
        {
            if (jobs.Count > 0) StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            bool away = true;
            while (true)
            {
                for (int k = 0; k < jobs.Count; k++)
                {
                    var j = jobs[away ? k : jobs.Count - 1 - k];   // 되돌릴 땐 역순 — 나중에 놓은 것부터
                    do
                    {
                        while (PortDemoDirector.Holds(crane)) { Suspend(); yield return null; }
                        Resume();
                        yield return Move(j, away ? j.away : j.home, away);
                    }
                    while (result == Result.Abort);
                    yield return null;   // 전부 건너뛰는 경우에도 한 프레임은 넘긴다
                }
                away = !away;
            }
        }

        IEnumerator Move(Job j, Vector3 dest, bool toAway)
        {
            float lift = SeatLiftMeters * StsConfig.ModelScale;
            var attach = crane.Attach;
            result = Result.Abort;
            if (attach.HasContainer && attach.AttachedContainer != j.box)
            {
                // 조종하던 사람이 딴 컨테이너를 매단 채 떠났거나 앞 작업이 막혔다 — 그 자리에 놓고 시작
                ShowColliders();
                if (grabber != null) grabber.Release(); else attach.Detach();
            }
            if (!attach.HasContainer)
            {
                if (!TryBounds(j.box, out var b)) { result = Result.Skip; yield break; }
                if ((new Vector3(b.center.x, b.min.y, b.center.z) - dest).sqrMagnitude < lift * lift) { result = Result.Done; yield break; }   // 이미 거기
                Vector3 seat = Top(b);
                if (!Reach(seat))
                {
                    Debug.LogWarning($"[PortDemo] {name}: {j.box.name} 이(가) 닿지 않는 자리에 있어 건너뜀");
                    result = Result.Skip;
                    yield break;
                }
                yield return Hoist(clearTopY);   if (Cut()) yield break;
                yield return Travel(seat);       if (Cut()) yield break;
                yield return Hoist(seat.y);      if (Cut()) yield break;
                Pick(j);
            }
            float hang = j.size.y + lift;   // 부착점 → 든 컨테이너 밑면
            yield return Hoist(clearTopY + hang);       if (Cut()) yield break;
            yield return Travel(dest);                  if (Cut()) yield break;
            yield return Hoist(dest.y + lift + hang);   if (Cut()) yield break;
            Place(j, dest, toAway);
            result = Result.Done;
        }

        // 이 단계에서 끊어야 하나 — 멈춤이면 Abort(다시 시도), 막힘이면 Skip(다음 작업).
        bool Cut()
        {
            if (PortDemoDirector.Holds(crane)) { result = Result.Abort; return true; }
            if (stalled) { result = Result.Skip; return true; }
            return false;
        }

        IEnumerator Hoist(float anchorY) =>
            Drive(float.NaN, float.NaN, AxisFor(crane.Spreader, Vector3.up * (anchorY - Anchor().y)));

        IEnumerator Travel(Vector3 p)
        {
            stalled = false;
            if (steer != null && steer.Current != RtgBogieSteering.Mode.Travel) steer.SetMode(RtgBogieSteering.Mode.Travel);
            while (steer != null && steer.IsTurning)   // 보기를 꺾는 동안은 GantryMover 가 주행을 잠근다
            {
                if (PortDemoDirector.Holds(crane)) yield break;
                yield return null;
            }
            Vector3 d = p - Anchor(); d.y = 0f;
            yield return Drive(crane.Gantry != null ? AxisFor(crane.Gantry, d) : float.NaN, AxisFor(crane.Trolley, d), float.NaN);
        }

        // 세 축을 목표로 — 멈춤이면 그 틱에 빠지고, 남은 거리가 StallSeconds 동안 안 줄면 stalled.
        //   '안 움직임'이 아니라 '안 다가감'으로 본다 — 클램프가 되밀면 매 틱 움직이긴 하는데 영영 안 닿는다.
        IEnumerator Drive(float g, float t, float h)
        {
            stalled = false;
            vG = vT = vH = 0f;
            float best = float.MaxValue, since = 0f;
            while (true)
            {
                if (PortDemoDirector.Holds(crane)) yield break;
                float dt = Time.fixedDeltaTime, left = 0f;
                bool done = Step(crane.Gantry, g, CraneAxisProfile.GantryMaxSpeed, CraneAxisProfile.GantryRatedAccel, ref vG, dt, ref left)
                          & Step(crane.Trolley, t, CraneAxisProfile.TrolleyMaxSpeed, CraneAxisProfile.TrolleyRatedAccel, ref vT, dt, ref left)
                          & Step(crane.Spreader, h, CraneAxisProfile.HoistMaxSpeed, CraneAxisProfile.HoistRatedAccel, ref vH, dt, ref left);
                if (done) yield break;
                if (left < best - 0.001f) { best = left; since = 0f; }
                else if ((since += dt) > StallSeconds)
                {
                    stalled = true;
                    Debug.LogWarning($"[PortDemo] {name}: {StallSeconds:F0}초 넘게 목표에 못 다가감(남은 {left:F2}m) — 막힘, 이번 작업 건너뜀");
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }
        }

        // 한 축 한 틱 — 정격 가속으로 붙고, 멈출 거리를 남겨 감속한다(사다리꼴). 목표에 닿았으면 true.
        bool Step(IAxisMover a, float target, float vMax, float acc, ref float v, float dt, ref float left)
        {
            if (a == null || float.IsNaN(target)) return true;
            float toM = a.WorldPerUnit / StsConfig.ModelScale;   // 축 1 = 실척 몇 m
            if (toM <= 0f) return true;
            float err = (Mathf.Clamp(target, a.Min, a.Max) - a.Current) * toM;
            left += Mathf.Abs(err);
            if (Mathf.Abs(err) < 0.001f) { v = 0f; return true; }
            vMax *= speedScale; acc *= speedScale;
            float speed = Mathf.Min(Mathf.Min(Mathf.Abs(v) + acc * dt, vMax), Mathf.Sqrt(2f * acc * Mathf.Abs(err)));
            v = speed * Mathf.Sign(err);
            a.MoveTo(a.Current + Mathf.Sign(err) * Mathf.Min(speed * dt, Mathf.Abs(err)) / toM);
            return false;
        }

        void Pick(Job j)
        {
            Vector3 pos = j.box.position; Quaternion rot = j.box.rotation;
            if (!crane.Attach.Attach(j.box)) return;
            j.box.SetPositionAndRotation(pos, rot);   // Attach 는 부착점 원점으로 스냅한다 — 잰 자세 그대로 둔다
            HideColliders(j.box);
            if (lockAnim != null) lockAnim.SetLocked(true);
            if (tele != null) tele.Set40(j.ft40);
            if (rtgTele != null) rtgTele.SetSize(j.ft40 ? RtgSpreaderTelescope.Size.Ft40 : RtgSpreaderTelescope.Size.Ft20);
        }

        void Place(Job j, Vector3 bottom, bool toAway)
        {
            var c = crane.Attach.Detach(j.parent);
            if (c == null) return;
            c.SetPositionAndRotation(bottom + j.pivot, j.rot);   // SeatLift 만큼 띄워 내린 것을 정확한 자리로
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
            ShowColliders();
            Debug.Log($"[PortDemo] {name}: 멈춤 — 접근·조종");
        }

        void Resume()
        {
            if (auto) return;
            auto = true;
            if (grabber != null) { pusherWas = grabber.IsPusherActive; grabber.SetPusherActive(false); }   // 하강 중 푸셔가 대상·이웃을 민다
            if (gantryMover != null) { stopWas = gantryMover.StopOnObstacle; gantryMover.StopOnObstacle = false; }   // 컨테이너를 장애물로 오인해 서지 않게
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

        // STS: 위가 빈 배 컨테이너 — 가까운 베이(주행 위치)부터, 한 베이 안에선 땅 쪽 열부터(트롤리를 덜 움직이게).
        List<Transform> ShipCandidates(Site site)
        {
            Vector3 gd = GantryDir(), td = TrolleyDir(), a = Anchor();
            float l0 = Vector3.Dot(site.land.min, td), l1 = Vector3.Dot(site.land.max, td);
            var list = new List<(Transform t, int bay, float toLand)>();
            foreach (var t in site.ship)
            {
                if (site.claimed.Contains(t) || !TryBounds(t, out var b) || !Uncovered(b, site.occupied) || !Reach(Top(b))) continue;
                int bay = Mathf.RoundToInt(Mathf.Abs(Vector3.Dot(b.center - a, gd)) / Mathf.Max(Extent(b.size, gd), 1e-5f));
                float c = Vector3.Dot(b.center, td);
                list.Add((t, bay, Mathf.Abs(c - Mathf.Clamp(c, Mathf.Min(l0, l1), Mathf.Max(l0, l1)))));
            }
            list.Sort((x, y) => x.bay != y.bay ? x.bay.CompareTo(y.bay) : x.toLand.CompareTo(y.toLand));
            return list.ConvertAll(x => x.t);
        }

        // RTG: 다른 RTG 보다 나에게 가까운 야드 컨테이너 — 가까운 것부터.
        List<Transform> YardCandidates(Site site)
        {
            Vector3 me = MoverPos(crane);
            var list = new List<(Transform t, float d)>();
            foreach (var t in site.yard)
            {
                if (site.claimed.Contains(t) || !TryBounds(t, out var b) || !Uncovered(b, site.occupied) || !Reach(Top(b))) continue;
                float d = HDist(b.center, me);
                bool mine = true;
                foreach (var o in site.rtgs) if (o != crane && HDist(b.center, MoverPos(o)) < d) { mine = false; break; }
                if (mine) list.Add((t, d));
            }
            list.Sort((x, y) => x.d.CompareTo(y.d));
            return list.ConvertAll(x => x.t);
        }

        // 내려놓을 자리 — 집는 자리와 같은 주행 위치에서 트롤리 방향으로 훑어 가까운 빈자리부터.
        //   빈자리 = 부두 땅 안 · 다른 컨테이너(바운즈)·예약 자리와 안 겹침 · 콜라이더(다리 등) 없음 · 세 축으로 닿음.
        bool FindSlot(Site site, Job j, float groundY)
        {
            Vector3 td = TrolleyDir();
            float step = Extent(j.size, td) + slotGapMeters * StsConfig.ModelScale;
            float skin = 0.01f * StsConfig.ModelScale, lift = SeatLiftMeters * StsConfig.ModelScale;
            TrolleyReach(out float lo, out float hi);
            float home = Vector3.Dot(j.home, td);
            int n = Mathf.CeilToInt((hi - lo) / step) + 1;
            for (int k = 1; k <= n; k++)
                foreach (int sgn in Signs)
                {
                    float s = home + sgn * k * step;
                    if (s < lo || s > hi) continue;
                    Vector3 bottom = j.home + td * (s - home);
                    bottom.y = groundY;
                    var slot = new Bounds(bottom + Vector3.up * (j.size.y * 0.5f), j.size);
                    if (site.haveLand && !InsideXZ(slot, site.land)) continue;
                    if (AnyOverlap(slot, site.occupied, skin)) continue;
                    if (Physics.CheckBox(slot.center + Vector3.up * (j.size.y * 0.05f), j.size * 0.45f,
                                         Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Reach(bottom + Vector3.up * (j.size.y + 2f * lift))) continue;
                    site.occupied.Add(slot);
                    j.away = bottom;
                    return true;
                }
            return false;
        }

        // 부착점을 p 로 보내는 세 축 값이 모두 가동범위 안인가
        bool Reach(Vector3 p)
        {
            Vector3 d = p - Anchor();
            return InRange(crane.Trolley, d) && InRange(crane.Spreader, d) && (crane.Gantry == null || InRange(crane.Gantry, d));
        }

        // 트롤리 가동범위의 월드 양 끝(트롤리 방향 좌표) — 지금 부착점 기준
        void TrolleyReach(out float lo, out float hi)
        {
            var a = crane.Trolley;
            float s = Vector3.Dot(Anchor(), TrolleyDir());
            float p = s + (a.Min - a.Current) * a.WorldPerUnit, q = s + (a.Max - a.Current) * a.WorldPerUnit;
            lo = Mathf.Min(p, q); hi = Mathf.Max(p, q);
        }

        Vector3 Anchor() => crane.Attach.AttachAnchor.position;
        Vector3 GantryDir() => crane.Gantry != null ? crane.Gantry.WorldAxis.normalized : Vector3.forward;
        Vector3 TrolleyDir() => crane.Trolley.WorldAxis.normalized;

        // 집을 때 부착점이 갈 곳 — 윗면 중심에서 SeatLift 위
        static Vector3 Top(Bounds b) => new Vector3(b.center.x, b.max.y + SeatLiftMeters * StsConfig.ModelScale, b.center.z);

        // 부착점을 d 만큼 옮기는 축 값 — 축은 전부 평행이동이라 WorldAxis 성분만 본다(PlcBridge.WorldAtPose 와 같은 식)
        static float AxisFor(IAxisMover a, Vector3 d)
        {
            Vector3 w = a.WorldAxis;
            return a.Current + Vector3.Dot(d, w) / w.sqrMagnitude;
        }

        static bool InRange(IAxisMover a, Vector3 d)
        {
            float v = AxisFor(a, d), eps = (a.Max - a.Min) * 1e-3f;
            return v >= a.Min - eps && v <= a.Max + eps;
        }

        static float Extent(Vector3 size, Vector3 dir) =>
            Mathf.Abs(dir.x) * size.x + Mathf.Abs(dir.y) * size.y + Mathf.Abs(dir.z) * size.z;

        static Vector3 MoverPos(StsCrane c) => c.Gantry is Component g ? g.transform.position : c.transform.position;

        static float HDist(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        // 위에 다른 컨테이너가 얹혀 있지 않은가 — 겹침은 폭의 1/4 안쪽만 친다(옆에 붙은 이웃은 제외)
        static bool Uncovered(Bounds b, List<Bounds> all)
        {
            float mx = b.extents.x * 0.5f, mz = b.extents.z * 0.5f;
            foreach (var o in all)
                if (o.min.y > b.center.y && o.min.x < b.max.x - mx && o.max.x > b.min.x + mx
                                         && o.min.z < b.max.z - mz && o.max.z > b.min.z + mz)
                    return false;
            return true;
        }

        static bool AnyOverlap(Bounds a, List<Bounds> all, float skin)
        {
            foreach (var o in all)
                if (a.min.x < o.max.x - skin && a.max.x > o.min.x + skin && a.min.y < o.max.y - skin
                 && a.max.y > o.min.y + skin && a.min.z < o.max.z - skin && a.max.z > o.min.z + skin)
                    return true;
            return false;
        }

        static bool InsideXZ(Bounds a, Bounds land) =>
            a.min.x >= land.min.x && a.max.x <= land.max.x && a.min.z >= land.min.z && a.max.z <= land.max.z;

        /// <summary>렌더러 바운즈 합(월드). 렌더러가 없으면 false.</summary>
        public static bool TryBounds(Transform t, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (any) b.Encapsulate(r.bounds);
                else { b = r.bounds; any = true; }
            }
            return any;
        }
    }
}
