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
    /// 높이: 컨테이너 윗면은 스프레더 최저점(콘 바닥)에 온다(PlcCargoReplay 와 같은 규약). 축은 부착점으로 움직이므로
    ///   부착점 → 콘 바닥 거리(drop)를 더해 잡는다 — FBX RTG 는 부착점이 스프레더 원점이라 안 더하면 스프레더가 파묻힌다.
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
        float drop;           // 부착점 → 스프레더 최저점(콘 바닥), 월드 Y
        float clearTopY;      // 옮길 때 컨테이너 밑면이 넘어야 할 높이
        float vG, vT, vH;     // 축 속도(실척 m/s, 부호 포함)
        Result result;
        bool stalled, auto, started, pusherWas, stopWas;
        int placed;

        static float Lift => SeatLiftMeters * StsConfig.ModelScale;   // 놓을 때만 쓰는 여유(받침 윗면 위)

        // 집는 자세의 '콘 바닥 − 컨테이너 윗면' (+ 띄움 / − 박힘). 수동 집기와 같은 자세로 앉힌다 —
        //   그랩버가 기하에서 유도한 삽입 깊이(SpreaderGrabber.InsertDepthMeters, 크레인마다 다르다)만큼 콘을 박는다.
        //   ★ 종전엔 늘 +SeatLift(윗면 20mm 위)라, 수동 집기를 고쳐도 자동 시연에서는 락이 컨테이너에 안 들어갔다
        //     (오너 2026-09-16 "스프레더 락이 컨테이너 안으로 안 들어간다"). 상수를 박지 않으므로 그랩버 값이 바뀌면 따라간다.
        //   ★ 클램프와 결합: 이 목표는 빈 스프레더 통과방지 한계(받침 윗면 − InsertU, fdf56d4)에 '정확히' 얹힌다.
        //     옛 +20mm 는 그 한계(당시 받침 윗면)보다 위로 피하려던 값이었다. 클램프 기준이 부착점 쪽으로 되돌아가면
        //     이 목표가 한계 아래가 되어 러너는 앉지 못하고 매 틱 밀리다 '막힘'으로 빠진다 — 둘은 같이 바뀌어야 한다.
        float SeatGapM => grabber != null ? -grabber.InsertDepthMeters : SeatLiftMeters;
        float SeatGapU => SeatGapM * StsConfig.ModelScale;

        // ── 검증 — 수식으로 세운 불변식을 작업마다 잰다. 위반은 "[PortDemo] 검증 실패" 경고, 스모크(PortDemoMenu)가 센다.
        //   ① 집기 정렬: 트위스트락 중심(SpreaderGrabber.GrabPoint — 러너 식과 따로 잰다) ↔ 윗면 중심 수평거리 ≤ 0.36m
        //      (= 수동 잠금 허용 registerTolXZ 0.015u × 24), 콘 바닥 − 윗면 = SeatGap(= −삽입깊이) ± 1cm
        //   ② 안착: 옮긴 밑면 = max(땅 윗면, 발밑 컨테이너 윗면) ± 2cm — 공중에 뜨거나 파묻히지 않는다
        //   ③ 겹침·경로: 놓은 자리와 운반 경로가 다른 컨테이너를 1cm 넘게 파고들지 않는다.
        //      경로 = 시작·끝 바운즈의 합 — 주행·횡행이 축마다 단조(사다리꼴, 되돌아가지 않음)라 실제 궤적을 감싼다.
        //   ④ 축: 틱마다 |v| ≤ 정격속, |a| ≤ 2·정격가속 — 사다리꼴 가속 구간은 a, 도착 틱 감속은 ≤ 2a 로 유계(Step 주석의 유도).
        //      트립 한계(정격 × TripMargin 5/3)로 나누면 2 ÷ 5/3 = 1.2 — 부동소수 여유 5% 를 두고 넘으면 그 틱에서 실패.
        //      2026-09-15 스모크에서 옛 도착 규칙(1mm 만 보고 섬)이 트립의 3.9배 스파이크를 냈다 — 이 검사가 그걸 잡는다.
        const float PickTolM = 0.36f, SupportTolM = 0.02f, OverlapSkinM = 0.01f;

        // 집기 안착 허용오차 — 목표(= 삽입 깊이)에서 유도한다. 옛 고정 ±1cm 는 STS 유도깊이 24mm 의 ±42% 라
        //   사실상 아무것도 못 잡았다(82 지적 2026-09-16). StsGrabProbe 의 밴드(d937f71)와 같은 식: min(5mm, 깊이×25%).
        float SeatTolM => Mathf.Min(0.005f, Mathf.Abs(SeatGapM) * 0.25f);
        const float AccelTripRatioMax = 2f / CraneAxisProfile.TripMargin * 1.05f;
        public static int Violations;
        public static float MaxPickErrM, MaxSupportErrM, MaxSpeedRatio, MaxAccelRatio;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetChecks() { Violations = 0; MaxPickErrM = MaxSupportErrM = MaxSpeedRatio = MaxAccelRatio = 0f; }
        Site site;
        bool trolleyStopWas;

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
            drop = Mathf.Max(0f, Anchor().y - SpreaderBottomY());
            this.site = site;

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
            Debug.Log($"[PortDemo] {name}: 계획 {jobs.Count}개 — {(rtg ? "야드 → 빈 열" : "배 위 단 → 안벽")}, " +
                      $"이동 높이 y={clearTopY:F3}, 부착점→콘 바닥 {drop:F4}, " +
                      $"축 시작(PLC m) GT={PlcM(crane.Gantry):F2} TR={PlcM(crane.Trolley):F2} HO={PlcM(crane.Spreader):F2}");
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
                if ((new Vector3(b.center.x, b.min.y, b.center.z) - dest).sqrMagnitude < Lift * Lift) { result = Result.Done; yield break; }   // 이미 거기
                Vector3 seat = Seat(b);
                if (!Reach(seat))
                {
                    Debug.LogWarning($"[PortDemo] {name}: {j.box.name} 이(가) 닿지 않는 자리에 있어 건너뜀");
                    result = Result.Skip;
                    yield break;
                }
                yield return Hoist(clearTopY + drop);   if (Cut()) yield break;
                yield return Travel(seat);              if (Cut()) yield break;
                yield return Align(seat);               if (Cut()) yield break;   // 흔들림을 잡고 내린다 — 이웃을 치지 않게
                yield return Hoist(seat.y);             if (Cut()) yield break;
                yield return Align(seat);               if (Cut()) yield break;
                Pick(j);
            }
            float hang = drop + SeatGapU + j.size.y;   // 부착점 → 든 컨테이너 밑면(집은 자세 그대로 매달림)
            yield return Hoist(clearTopY + hang);       if (Cut()) yield break;
            bool haveFrom = TryBounds(j.box, out var path);
            yield return Travel(dest);                  if (Cut()) yield break;
            if (haveFrom && TryBounds(j.box, out var to)) { path.Encapsulate(to); CheckClear(path, j.box, "운반 경로"); }
            yield return Align(dest);                   if (Cut()) yield break;
            yield return Hoist(dest.y + Lift + hang);   if (Cut()) yield break;
            yield return Align(dest);                   if (Cut()) yield break;   // 내리며 로프가 길어진 만큼 바람 편향(∝L)도 커졌다
            Place(j, dest, toAway);
            result = Result.Done;
        }

        // ── 흔들림(CraneSway) 대응 ──
        //   Settle: 평형점 기준 진동 진폭이 SettleM 아래로 — ζ=0.7·L=25m 에서 1.5m → 0.1m 는 ln(15)/(ζ·√(g/L)) ≈ 6.2초.
        //     돌풍이 계속 흔들어 못 내려가면 SettleTimeoutS 뒤 그대로 진행한다(실제 운전도 약한 흔들림엔 내린다).
        //   Align: 잦아든 뒤 '실제' 부착점(흔들림·바람 편향 포함)을 목표 p 에 수평으로 맞춘다 — 운전자가 바람만큼 트롤리를 비켜 대는 것.
        //     빈 스프레더는 바람을 안 받아 편향 0 이라 거의 안 움직이고, 컨테이너를 들면 d = L·F/(m·g) 만큼 비켜 댄다.
        const float SettleM = 0.1f, SettleTimeoutS = 30f;

        IEnumerator Settle()
        {
            var s = CraneSway.Of(crane);
            float waited = 0f;
            while (s != null && s.isActiveAndEnabled && !s.Settled(SettleM) && waited < SettleTimeoutS)
            {
                if (PortDemoDirector.Holds(crane)) { interrupted = true; yield break; }
                waited += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }

        IEnumerator Align(Vector3 p)
        {
            yield return Settle();                      if (Cut()) yield break;
            Vector3 d = p - crane.Attach.AttachAnchor.position; d.y = 0f;
            if (d.magnitude / StsConfig.ModelScale < SettleM) yield break;
            yield return Drive(crane.Gantry != null ? AxisFor(crane.Gantry, d) : float.NaN, AxisFor(crane.Trolley, d), float.NaN);
            if (Cut()) yield break;
            yield return Settle();
        }

        // 이 단계에서 끊어야 하나 — 멈춤이면 Abort(다시 시도), 막힘이면 Skip(다음 작업).
        //   interrupted: 기다리던 단계가 멈춤으로 빠졌다 — 그 사이 사람이 떠나 Holds 가 풀렸어도 목표에 못 간 채라 처음부터 다시 한다.
        bool interrupted;
        bool Cut()
        {
            if (interrupted || PortDemoDirector.Holds(crane)) { interrupted = false; result = Result.Abort; return true; }
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
                if (PortDemoDirector.Holds(crane)) { interrupted = true; yield break; }
                yield return null;
            }
            Vector3 d = p - Anchor(); d.y = 0f;
            yield return Drive(crane.Gantry != null ? AxisFor(crane.Gantry, d) : float.NaN, AxisFor(crane.Trolley, d), float.NaN);
        }

        // 세 축을 목표로 — 남은 거리가 StallSeconds 동안 안 줄면 stalled.
        //   '안 움직임'이 아니라 '안 다가감'으로 본다 — 클램프가 되밀면 매 틱 움직이긴 하는데 영영 안 닿는다.
        // 멈춤(사람 접근·조종)에 그 틱 순간 정지하지 않는다 — 한 틱에 서면 감속이 v/dt(트롤리 3.5m/s → 175m/s², 정격의 290배)라
        //   화물이 튄다. 정지거리 s = v²/(2a) 앞을 새 목표로 두면 Step 의 √(2a·err) 곡선이 곧 정격 감속선이다 — 선 뒤에 빠진다.
        IEnumerator Drive(float g, float t, float h)
        {
            stalled = false;
            vG = vT = vH = 0f;
            float best = float.MaxValue, since = 0f;
            bool stopping = false;
            // 검증 ④ — 축 값을 틱마다 다시 읽어 속도·가속을 잰다(Step 이 믿는 v 가 아니라 실제로 옮겨진 양).
            float pG = Cur(crane.Gantry), pT = Cur(crane.Trolley), pH = Cur(crane.Spreader), mG = 0f, mT = 0f, mH = 0f;
            while (true)
            {
                if (!stopping && PortDemoDirector.Holds(crane))
                {
                    stopping = interrupted = true;
                    g = StopAt(crane.Gantry, g, vG, CraneAxisProfile.GantryRatedAccel);
                    t = StopAt(crane.Trolley, t, vT, CraneAxisProfile.TrolleyRatedAccel);
                    h = StopAt(crane.Spreader, h, vH, CraneAxisProfile.HoistRatedAccel);
                }
                float dt = Time.fixedDeltaTime, left = 0f;
                bool done = Step(crane.Gantry, g, CraneAxisProfile.GantryMaxSpeed, CraneAxisProfile.GantryRatedAccel, ref vG, dt, ref left)
                          & Step(crane.Trolley, t, CraneAxisProfile.TrolleyMaxSpeed, CraneAxisProfile.TrolleyRatedAccel, ref vT, dt, ref left)
                          & Step(crane.Spreader, h, CraneAxisProfile.HoistMaxSpeed, CraneAxisProfile.HoistRatedAccel, ref vH, dt, ref left);
                Kin(crane.Gantry, ref pG, ref mG, CraneAxisProfile.GantryMaxSpeed, CraneAxisProfile.GantryRatedAccel, dt, "갠트리");
                Kin(crane.Trolley, ref pT, ref mT, CraneAxisProfile.TrolleyMaxSpeed, CraneAxisProfile.TrolleyRatedAccel, dt, "트롤리");
                Kin(crane.Spreader, ref pH, ref mH, CraneAxisProfile.HoistMaxSpeed, CraneAxisProfile.HoistRatedAccel, dt, "권상");
                if (done) yield break;
                if (stopping) { yield return new WaitForFixedUpdate(); continue; }
                if (left < best - 0.001f) { best = left; since = 0f; }
                else if ((since += dt) > StallSeconds)
                {
                    stalled = true;
                    Debug.LogWarning($"[PortDemo] {name}: {StallSeconds:F0}초 넘게 목표에 못 다가감(남은 {left:F2}m — {Why(g, t, h)}) — 막힘, 이번 작업 건너뜀");
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }
        }

        // 막힘 진단 — 축마다 남은 거리(실척 m)와 무버의 장애물 정지(IsBlocked) 여부, 흔들림 진폭
        string Why(float g, float t, float h)
        {
            var s = CraneSway.Of(crane);
            return $"갠트리 {ErrM(crane.Gantry, g):F3}{Blk(crane.Gantry)} · 트롤리 {ErrM(crane.Trolley, t):F3}{Blk(crane.Trolley)} · " +
                   $"권상 {ErrM(crane.Spreader, h):F3}{Blk(crane.Spreader)} · 흔들림 {(s != null ? s.AmplitudeM : 0f):F2}m" +
                   $"{(steer != null && steer.IsTurning ? " · 보기 조향 중" : "")}";
        }
        static float ErrM(IAxisMover a, float target) =>
            a == null || float.IsNaN(target) ? 0f : (Mathf.Clamp(target, a.Min, a.Max) - a.Current) * a.WorldPerUnit / StsConfig.ModelScale;
        static string Blk(IAxisMover a) => a is AxisMoverBase m && m.IsBlocked ? "(장애물 정지)" : "";

        // 한 축 한 틱 — 정격 가속으로 붙고, 멈출 거리를 남겨 감속한다(사다리꼴). 목표에 닿았으면 true.
        bool Step(IAxisMover a, float target, float vMax, float acc, ref float v, float dt, ref float left)
        {
            if (a == null || float.IsNaN(target)) return true;
            float toM = a.WorldPerUnit / StsConfig.ModelScale;   // 축 1 = 실척 몇 m
            if (toM <= 0f) return true;
            float err = (Mathf.Clamp(target, a.Min, a.Max) - a.Current) * toM;
            left += Mathf.Abs(err);
            vMax *= speedScale; acc *= speedScale;
            // 도착 = 1mm 안 '그리고' 한 틱에 설 수 있는 속도(|v| ≤ a·dt) — PlcSim/generate.py Axis 도착 정착과 같은 규칙.
            //   위치만 보고 서면 남은 속도 √(2a·ε)(ε≈1mm)가 한 틱에 0 이 된다: 권상 0.039m/s → 1.95m/s² = 트립의 2.3배,
            //   갠트리 3.9배(2026-09-15 스모크 진단). 이 규칙이면 v 를 실제 이동량/dt 로 두어 마지막 감속이 ≤ 2a 다:
            //   다 먹는 틱은 err ≤ v·dt, v = √(2a·err) → err ≤ 2a·dt² → 남은 속도 err/dt ≤ 2a·dt.
            if (Mathf.Abs(err) < 0.001f && Mathf.Abs(v) <= acc * dt) { v = 0f; return true; }
            float speed = Mathf.Min(Mathf.Min(Mathf.Abs(v) + acc * dt, vMax), Mathf.Sqrt(2f * acc * Mathf.Abs(err)));
            float move = Mathf.Min(speed * dt, Mathf.Abs(err));
            v = Mathf.Sign(err) * move / dt;
            a.MoveTo(a.Current + Mathf.Sign(err) * move / toM);
            return false;
        }

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

        // 집은 컨테이너의 원자세가 받침(발자국 40% 이상 겹친 아래 단 — SpreaderGrabber landingOverlapFrac 과 같은 기준)을
        //   파고든 채면(씬 적재 오차) 멈춘 채 접촉면까지 올린다. 안 그러면 들기 시작하는 둘째 틱에 통과방지 클램프가 그만큼을
        //   한 번에 밀어 올려 권상 가속이 트립의 2.46배로 튄다(2026-09-15 스모크 4차 — 첫 틱 v = a·dt = 0.010 뒤 0.037~0.051m/s).
        void LiftToContact(Transform box)
        {
            if (crane.Spreader == null || !TryBounds(box, out var b)) return;
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
            // 진단 — 야드 칸 이탈이 어디서 생기는지 보려면 '어느 상자를 어디에 놓았는지' 가 있어야 한다.
            //   2026-09-17 스모크가 Cont20_02 에서 이탈 0.256m(≤0.2)로 FAIL 했는데, 자동 경로엔 로그가 없어
            //   옮긴 자리인지 되돌린 자리인지조차 못 갈랐다. 실척으로 남긴다(모델 단위는 1/24 라 안 읽힌다).
            if (YardGrid.TrySnapXZ(bottom, Mathf.Max(j.size.x, j.size.z), out var cell))
                Debug.Log($"[PortDemo] {name}: {c.name} {(toAway ? "옮김" : "되돌림")} — 실척 " +
                          $"({bottom.x * StsConfig.InvModelScale:F2}, {bottom.z * StsConfig.InvModelScale:F2})m · " +
                          $"칸중심 ({cell.x * StsConfig.InvModelScale:F2}, {cell.z * StsConfig.InvModelScale:F2})m · " +
                          $"이탈 {Vector3.Distance(new Vector3(bottom.x, 0f, bottom.z), new Vector3(cell.x, 0f, cell.z)) * StsConfig.InvModelScale:F3}m");
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

        // ── 검증 헬퍼(위 ①~④) ──
        static float Cur(IAxisMover a) => a != null ? a.Current : 0f;

        /// <summary>축의 지금 위치를 PLC 좌표(실척 m, 0 = 무버 Min)로 — PlcSim 시작 자세를 씬과 맞출 때 쓰는 측정값.
        ///   RTG 는 무버 위치가 FBX 기본값이라 씬 파일에 안 남는다(프리팹 오버라이드 없음) → 로그로만 잴 수 있다.</summary>
        static float PlcM(IAxisMover a) => a == null ? 0f : (a.Current - a.Min) * a.WorldPerUnit / StsConfig.ModelScale;

        void Fail(string what)
        {
            if (++Violations <= 40) Debug.LogWarning($"[PortDemo] 검증 실패 — {name}: {what}");
        }

        // ④ 이번 틱 실제 속도·가속(실척) — 한계를 넘으면 그 틱에서 실패
        void Kin(IAxisMover a, ref float prev, ref float vPrev, float vMax, float acc, float dt, string axis)
        {
            if (a == null || dt <= 0f) return;
            float toM = a.WorldPerUnit / StsConfig.ModelScale;
            float cur = a.Current, v = (cur - prev) * toM / dt, am = (v - vPrev) / dt;
            float sr = Mathf.Abs(v) / (vMax * speedScale), ar = Mathf.Abs(am) / (acc * speedScale * CraneAxisProfile.TripMargin);
            MaxSpeedRatio = Mathf.Max(MaxSpeedRatio, sr);
            MaxAccelRatio = Mathf.Max(MaxAccelRatio, ar);
            if (sr > 1.001f || ar > AccelTripRatioMax)
                Fail($"{axis} 한 틱 v {vPrev:F3}→{v:F3}m/s(정격비 {sr:F3}) a {am:F2}m/s²(트립비 {ar:F2} > {AccelTripRatioMax:F2}), 축 {prev:F6}→{cur:F6}");
            prev = cur; vPrev = v;
        }

        // 지금 속도 v(실척 m/s — Step 이 실제로 옮긴 양)로 정격 감속해 서는 축 값. 서 있으면 NaN(안 움직임).
        float StopAt(IAxisMover a, float target, float v, float acc)
        {
            if (a == null || float.IsNaN(target) || Mathf.Abs(v) < 1e-4f) return float.NaN;
            float toM = a.WorldPerUnit / StsConfig.ModelScale;
            return a.Current + Mathf.Sign(v) * v * v / (2f * acc * speedScale) / toM;
        }

        // ① 집기 직전 — 트위스트락 중심·콘 바닥을 러너 식과 따로 재서 컨테이너 윗면에 맞춘다.
        void CheckPick(Transform box)
        {
            if (grabber == null || !TryBounds(box, out var b)) return;
            Vector3 gp = grabber.GrabPoint();
            float dxz = new Vector2(gp.x - b.center.x, gp.z - b.center.z).magnitude / StsConfig.ModelScale;
            float gap = (SpreaderBottomY() - b.max.y) / StsConfig.ModelScale;
            MaxPickErrM = Mathf.Max(MaxPickErrM, dxz);
            if (dxz > PickTolM || Mathf.Abs(gap - SeatGapM) > SeatTolM)
                Fail($"집기 정렬 {box.name} — 트위스트락↔윗면 중심 {dxz:F3}m(≤{PickTolM}), 콘 바닥−윗면 {gap:F3}m(목표 {SeatGapM:F3}±{SeatTolM})");
        }

        // ②③ 놓은 직후 — 옮긴 자리는 받침(땅·발밑 컨테이너 윗면)에 닿았나, 어디서든 다른 컨테이너를 파고들지 않았나.
        //   되돌린 자리는 원래 자세(home)라 받침이 배 해치·아래 단이다 — 겹침만 본다.
        void CheckPlaced(Transform c, bool toAway)
        {
            if (!TryBounds(c, out var b)) return;
            var others = OtherBounds(c);
            if (toAway)
            {
                float support = site != null && site.haveLand ? site.land.max.y : 0f;
                float reach = b.min.y + SupportTolM * StsConfig.ModelScale;
                foreach (var o in others)
                    if (o.max.y <= reach && o.min.x < b.max.x && o.max.x > b.min.x && o.min.z < b.max.z && o.max.z > b.min.z)
                        support = Mathf.Max(support, o.max.y);
                float err = Mathf.Abs(b.min.y - support) / StsConfig.ModelScale;
                MaxSupportErrM = Mathf.Max(MaxSupportErrM, err);
                if (err > SupportTolM) Fail($"안착 {c.name} — 밑면 y={b.min.y:F4}u, 받침 y={support:F4}u, 오차 {err:F3}m(≤{SupportTolM})");
            }
            if (AnyOverlap(b, others, OverlapSkinM * StsConfig.ModelScale)) Fail($"놓은 자리 {c.name} 이(가) 다른 컨테이너를 파고듦");
        }

        void CheckClear(Bounds path, Transform self, string what)
        {
            if (AnyOverlap(path, OtherBounds(self), OverlapSkinM * StsConfig.ModelScale)) Fail($"{what} {self.name} 이(가) 다른 컨테이너를 파고듦");
        }

        List<Bounds> OtherBounds(Transform self)
        {
            var list = new List<Bounds>();
            if (site == null) return list;
            foreach (var t in site.ship) if (t != null && t != self && TryBounds(t, out var ob)) list.Add(ob);
            foreach (var t in site.yard) if (t != null && t != self && TryBounds(t, out var ob)) list.Add(ob);
            return list;
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

        // STS: 위가 빈 배 컨테이너 — 가까운 베이(주행 위치)부터, 한 베이 안에선 땅 쪽 열부터(트롤리를 덜 움직이게).
        List<Transform> ShipCandidates(Site site)
        {
            Vector3 gd = GantryDir(), td = TrolleyDir(), a = Anchor();
            float l0 = Vector3.Dot(site.land.min, td), l1 = Vector3.Dot(site.land.max, td);
            var list = new List<(Transform t, int bay, float toLand)>();
            foreach (var t in site.ship)
            {
                if (site.claimed.Contains(t) || !TryBounds(t, out var b) || !Uncovered(b, site.occupied) || !Reach(Seat(b))) continue;
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
                if (site.claimed.Contains(t) || !TryBounds(t, out var b) || !Uncovered(b, site.occupied) || !Reach(Seat(b))) continue;
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
            float skin = 0.01f * StsConfig.ModelScale;
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
                    // 야드 안이면 칸(라인) 중심으로 라운딩 — 수동 놓기(SpreaderGrabber.Release)와 '같은 식'(YardGrid)을 읽는다.
                    //   ★ 이게 없으면 자동 시연은 라인을 안 지킨다: 시나리오는 Place() → crane.Attach.Detach() 로 놓아
                    //     Release() 의 스냅을 통째로 우회하므로, 수동 경로만 고쳐선 오너가 보는 화면이 안 바뀐다(xr-port-82 지적).
                    //   야드 밖(에이프런·선박 하역)은 false 가 와서 자유 스텝 그대로 — 거기엔 맞출 격자가 없다.
                    //   두 후보가 같은 칸으로 라운딩되면 아래 site.occupied 겹침 게이트가 걸러, 이웃 칸으로 자연히 넘어간다.
                    if (YardGrid.TrySnapXZ(bottom, Mathf.Max(j.size.x, j.size.z), out var cell))
                    { bottom.x = cell.x; bottom.z = cell.z; }
                    var slot = new Bounds(bottom + Vector3.up * (j.size.y * 0.5f), j.size);
                    if (site.haveLand && !InsideXZ(slot, site.land)) continue;
                    if (AnyOverlap(slot, site.occupied, skin)) continue;
                    if (Physics.CheckBox(slot.center + Vector3.up * (j.size.y * 0.05f), j.size * 0.45f,
                                         Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Reach(bottom + Vector3.up * (j.size.y + Lift + drop + SeatGapU))) continue;   // 놓기 직전 부착점
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

        // 집을 때 부착점이 갈 곳 — 윗면 중심에서 콘 바닥이 SeatGap 만큼(= 삽입깊이만큼 아래로) 오게
        Vector3 Seat(Bounds b) => new Vector3(b.center.x, b.max.y + SeatGapU + drop, b.center.z);

        // 흔들림을 뺀 부착점 — 계획·주행 목표는 흔들리지 않은 크레인 기하로 잡는다(실제 화물 맞춤은 Align 이 한다).
        Vector3 Anchor()
        {
            var s = CraneSway.Of(crane);
            return crane.Attach.AttachAnchor.position - (s != null ? s.Offset : Vector3.zero);
        }
        Vector3 GantryDir() => crane.Gantry != null ? crane.Gantry.WorldAxis.normalized : Vector3.forward;
        Vector3 TrolleyDir() => crane.Trolley.WorldAxis.normalized;

        // 스프레더 최저점(콘 바닥) — 매단 컨테이너는 뺀다
        float SpreaderBottomY()
        {
            var sp = ((Component)crane.Spreader).transform;
            var held = crane.Attach.AttachedContainer;
            float y = sp.position.y;
            foreach (var r in sp.GetComponentsInChildren<Renderer>())
                if (held == null || !r.transform.IsChildOf(held)) y = Mathf.Min(y, r.bounds.min.y);
            return y;
        }

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
