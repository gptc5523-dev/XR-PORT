using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
        /// <summary>크레인 한 대의 시연 — 컨테이너 5개를 옮기고 역순으로 되돌리며 반복(STS: 배→안벽, RTG: 야드→빈 열).
        /// 못 닿거나 막히면 그 작업만 건너뛴다. 가설 구현, Quest 미검증.</summary>
    [DisallowMultipleComponent]
    public sealed partial class CraneDemoRunner : MonoBehaviour
    {
        /// <summary>크레인당 옮기는 컨테이너 수.</summary>
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

        // RTG 쌓기 더미 — 한쪽 끝 열에 같은 길이 계열(20ft / 40ft·40HC·45ft)끼리 StackTiers 단까지.
        sealed class Stack { public Vector3 bottom; public float topY; public int tiers; public bool ft40; }
        readonly List<Stack> stacks = new List<Stack>();
        const int StackTiers = 3;
        readonly List<Collider> hidden = new List<Collider>();   // 옮기는 동안 끈 콜라이더 — 이웃 컨테이너를 밀지 않게
        float drop;           // 부착점 → 스프레더 최저점(콘 바닥), 월드 Y
        float clearTopY;      // 옮길 때 컨테이너 밑면이 넘어야 할 높이
        float vG, vT, vH;     // 축 속도(실척 m/s, 부호 포함)
        Result result;
        bool stalled, auto, started, pusherWas, stopWas;
        int placed;

        static float Lift => SeatLiftMeters * StsConfig.ModelScale;   // 놓을 때만 쓰는 여유(받침 윗면 위)

        // 집는 자세의 '콘 바닥 − 컨테이너 윗면' — 그랩버 삽입 깊이(InsertDepthMeters)만큼 콘을 박는다(수동 집기와 같은 자세).
        //   빈 스프레더 통과방지 한계(받침 윗면 − InsertU)에 정확히 얹히므로 클램프 기준이 바뀌면 같이 바뀌어야 한다.
        float SeatGapM => grabber != null ? -grabber.InsertDepthMeters : SeatLiftMeters;
        float SeatGapU => SeatGapM * StsConfig.ModelScale;

        // 검증 — 매 작업마다 잰다(위반은 "[PortDemo] 검증 실패" 경고). ① 집기 정렬 ≤0.36m·콘−윗면=SeatGap±1cm
        //   ② 안착 = 받침 윗면 ±2cm ③ 겹침·경로 1cm 이상 파고들지 않음 ④ 축 |v|≤정격, |a|≤2·정격가속(트립비 ≤1.2×1.05)
        const float PickTolM = 0.36f, SupportTolM = 0.02f, OverlapSkinM = 0.01f;

        // 집기 안착 허용오차 — 삽입 깊이에서 유도(StsGrabProbe 와 같은 식): min(5mm, 깊이×25%).
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

        // 끄면 진행 중인 작업도 멈춘다 — 코루틴은 컴포넌트를 꺼도 계속 돌아, 남은 작업이 컨테이너를 공중에 놓을 수 있었다.
        //   들고 있던 컨테이너는 스프레더에 매달린 채 남는다. 다시 켜도 재시작하지 않는다(PortDemoDirector 가 Go 를 한 번만 부른다).
        void OnDisable() { StopAllCoroutines(); Suspend(); }

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

            var cands = rtg ? YardCandidates(site) : ShipCandidates(site);
            if (rtg) cands = FortyFirst(cands);   // 한 더미를 채우기 쉽게 40ft 계열부터
            foreach (var t in cands)
            {
                if (jobs.Count == Count) break;
                if (!SceneUtil.TryBounds(t, out var b)) continue;
                var rb = t.GetComponent<Rigidbody>();
                var j = new Job
                {
                    box = t, parent = t.parent, rot = t.rotation, size = b.size,
                    home = new Vector3(b.center.x, b.min.y, b.center.z),
                    ft40 = IsFt40(b),
                    kinematic = rb == null || rb.isKinematic,
                };
                j.pivot = t.position - j.home;
                if (!(rtg ? StackSlot(site, j) : FindSlot(site, j, site.land.max.y))) continue;
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
            Debug.Log($"[PortDemo] {name}: 계획 {jobs.Count}개 — {(rtg ? $"야드 → 한쪽 {StackTiers}단 쌓기(더미 {stacks.Count})" : "배 위 단 → 안벽")}, " +
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
    }
}
