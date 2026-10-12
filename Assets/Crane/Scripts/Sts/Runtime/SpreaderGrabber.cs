using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>외부 Rigidbody를 트위스트락 콘 기준으로 잡고/놓고, 빈 스프레더의 컨테이너 통과를 클램프로 막는다.
    /// order 50: 클램프가 VRController 축 이동(order 0) 뒤에 돌아야 떨림 없이 침투를 복원한다.</summary>
    [DefaultExecutionOrder(50)]
    [AddComponentMenu("AI-XR Crane/STS Crane/Spreader Grabber")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed partial class SpreaderGrabber : MonoBehaviour
    {
        [Header("잡기")]
        [Tooltip("트위스트락(콘)에서 이 거리 안의 가장 가까운 컨테이너를 잡는다(m). 단 maxGrabRange로 상한이 걸린다.")]
        [SerializeField] float grabRange = 0.35f;
        [Tooltip("잡기 인식 반경의 상한(m). 1/24 미니어처라 grabRange가 커도 실제 반경은 이 값으로 제한된다. " +
                 "잡기 범위를 실제로 넓히려면 grabRange가 아니라 이 값을 올릴 것(기존 코드 상한 0.1을 인스펙터로 노출).")]
        [SerializeField] float maxGrabRange = 0.1f;
        [Tooltip("잡은 컨테이너 긴 축이 이 길이를 넘으면 40ft로 자동 신축(20ft≈0.25 / 40ft≈0.51, m)")]
        [SerializeField] float sizeThreshold = YardGrid.Is40ThresholdU;
        [Tooltip("Console에 집기 진단 로그 출력")]
        [SerializeField] bool debugLog = true;

        [Header("코너 안착(체결 조건)")]
        [Tooltip("트위스트락이 컨테이너 상단 코너캐스팅 위에 '동심 정렬 + 윗면 안착'됐을 때만 체결한다. " +
                 "켜면 '근처에만 있어도 잠김'을 차단(근접만으로는 안 잠금). 끄면 종전처럼 근접 잠금.")]
        [SerializeField] bool requireSeatedRegistration = true;
        [Tooltip("동심 정렬 수평 허용오차(m) — 스프레더(트위스트락 평균) 중심이 컨테이너 중심에서 이 거리 안일 때만. " +
                 "사이즈가 자동 정합되므로 이 값 이내면 콘 4개가 코너캐스팅 위에 놓인다. 0.015≈가이드 정합(실척 ~36cm).")]
        [SerializeField] float registerTolXZ = 0.015f;
        // 높이 밴드는 InsertU × [InsertMinFrac, InsertMaxFrac] 하나로 정한다(ConeBottomY 실측 기준).

        [Tooltip("콘 돌출량을 못 재는 크레인(콘 미탐색)에서만 쓰는 폴백 삽입 깊이(실척 m). " +
                 "정상 크레인은 이 값을 무시하고 씬 기하에서 유도한다 — InsertDepthMeters 참고.")]
        [SerializeField] float insertFallbackMeters = 0.04f;
        // ★ 상판 두께(ISO 1161 ≈16mm)는 1차 출처 없음 — 2차 출처로 삽입 상한을 만들지 말 것.

        float protrusionM = -1f;   // 콘 돌출량 캐시(실척 m, 음수 = 미측정)

        /// <summary>콘 삽입 깊이(실척 m) = 씬 기하의 콘 돌출량(실측 STS 24mm · RTG 56mm), 못 재면 insertFallbackMeters.
        /// ★ 호출측 캐시 금지 — 매번 프로퍼티로 읽는다.</summary>
        public float InsertDepthMeters
        {
            get
            {
                if (protrusionM < 0f) protrusionM = MeasureProtrusionM();
                return protrusionM > 0f ? protrusionM : insertFallbackMeters;
            }
        }

        // 콘 돌출량(실척 m) = 콘 아닌 스프레더 최저 Y − 콘 바닥 Y. StsGrabProbe.ProtrusionM 과 같은 식 유지.
        //   0 이하면 안착 깊이 정의 불가 → 폴백.
        float MeasureProtrusionM()
        {
            var sp = crane != null ? crane.Spreader as Component : null;
            if (sp == null || twistlocks == null || twistlocks.Length == 0) return 0f;
            Transform held = crane.Attach != null ? crane.Attach.AttachedContainer : null;
            float coneB = ConeBottomY(), bodyB = float.MaxValue;
            foreach (var r in sp.GetComponentsInChildren<Renderer>())
            {
                if (held != null && r.transform.IsChildOf(held)) continue;
                bool inCone = false;
                foreach (var t in twistlocks)
                    if (t != null && (r.transform == t || r.transform.IsChildOf(t))) { inCone = true; break; }
                if (!inCone) bodyB = Mathf.Min(bodyB, r.bounds.min.y);
            }
            return bodyB < float.MaxValue ? (bodyB - coneB) / StsConfig.ModelScale : 0f;
        }

        [Header("통과 방지")]
        [SerializeField] bool blockPassThrough = true;
        [Tooltip("컨테이너 윗면 위로 둘 여유(m)")]
        [SerializeField] float topClearance = 0f;
        [Tooltip("footprint 바깥으로 이 수평 거리까지는 '위'로 보고 멈춤(m)")]
        [SerializeField] float passXZmargin = 0.1f;
        [Tooltip("Landing 센서 — 든 컨테이너가 아래 컨테이너 footprint와 이 비율 이상 겹치면 '적층'으로 보고 윗면에서 하강 정지. " +
                 "중심점 일치가 아니라 겹침 비율이라 살짝 어긋나게 내려놔도 멈춤(아래 것을 바닥으로 밀어넣지 않음). 옆 내려놓기(겹침~0)와 구분.")]
        [SerializeField] float landingOverlapFrac = 0.4f;

        [Header("물리 충돌(컨테이너 밀림/토플)")]
        [Tooltip("스프레더에 kinematic 콜라이더를 달아 컨테이너(동적 강체)를 물리적으로 밀어/넘어뜨린다. " +
                 "빈 스프레더가 옆에서 치면 컨테이너가 밀린다. 잡기와 충돌나면 끄고 'sideHit 타넘기 방지'만 유지.")]
        [SerializeField] bool spreaderCollider = true;

        [Header("Anti-Collision 경보(3013 HO Snag)")]
        [Tooltip("충돌 해제 후 경보를 잠깐 더 유지하는 시간(s) — 경계 깜빡임 방지.")]
        [SerializeField] float loadCollisionHold = 0.3f;

        StsCrane crane;
        SpreaderLockAnimator lockAnim;
        SpreaderTelescope telescope;      // (절차 STS) 잡은 컨테이너 크기에 맞춰 20/40ft 신축(스케일)
        RtgSpreaderTelescope rtgTele;     // (FBX RTG) 슬라이드 방식 신축
        SpreaderHoist spreaderHoist;   // 잡은 컨테이너 밑면 기준으로 하강 바닥 한계 설정
        Transform[] twistlocks;   // Twistlock_Cone들 — 잡기 기준점
        readonly List<Rigidbody> bodies = new List<Rigidbody>();   // 씬 강체 목록(재사용 버퍼)
        readonly List<Renderer[]> bodyRends = new List<Renderer[]>();   // bodies[i] 의 렌더러 — 틱마다 GetComponentsInChildren 하면 7.8ms·205KB GC 였다
        float nextRefresh;

        float floorTopY;             // 바닥 윗면 월드 Y(통과방지 기본 받침). SSOT = ContainerPhysicsStabilizer.FindFloorTopY
        BoxCollider pusherBox;       // SpreaderPusher 콜라이더 — X를 텔레스코픽 길이에 맞춰 갱신
        float lastAntiCollTime = -999f;   // 마지막 Anti-Collision 겹침 시각(hold 동안 경보 유지)

        // QA 판정용 이전 상태 — 엣지와 보정량 변화만 찍어 매 틱 로그 폭주 방지.
        bool qaPrevOver, qaPrevSideHit, qaPrevLoadColl, qaPrevLanded, qaPrevClampEmpty;
        float qaCorrPrev = -1f;

        // 전수 스캔(FindObjectsByType) 최소 간격 — 화물은 거의 안 변하고 잡힘 여부는 매 틱 IsChildOf로 거른다.
        const float MinRescanInterval = 3f;

        // 빈 스프레더 통과방지 여유 상한 / 측면충돌 판정 깊이 비율.
        const float EmptyPassMarginCap = 0.03f;
        const float SideHitDepthFrac = 0.25f;

        Transform AttachPoint => (crane != null && crane.Attach != null) ? crane.Attach.transform : null;

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
            telescope = GetComponentInChildren<SpreaderTelescope>(true);
            rtgTele = GetComponentInChildren<RtgSpreaderTelescope>(true);
            spreaderHoist = crane != null ? crane.Spreader as SpreaderHoist : null;

            var list = new List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                // 절차 크레인: 'Twistlock_Cone'(BaseName). FBX 크레인: 'Spreader_Twistlock_F_L' 등(콘 4개).
                if (CraneHud.BaseName(t.name) == StsPartNames.TwistlockCone
                    || t.name.StartsWith(StsPartNames.SpreaderTwistlockPrefix))
                    list.Add(t);
            }
            twistlocks = list.ToArray();

            if (passXZmargin > EmptyPassMarginCap)
                Debug.LogWarning($"[Crane] SpreaderGrabber: 인스펙터 passXZmargin {passXZmargin} 가 상한 {EmptyPassMarginCap}(으)로 클램프됨(빈 스프레더 통과방지) — 더 크게 쓰려면 코드의 EmptyPassMarginCap을 올릴 것.");

            Refresh();
        }

        void Start()
        {
            floorTopY = AIXRCrane.ContainerPhysicsStabilizer.FindFloorTopY(out _);
            CreateSpreaderPusher();
            // Play 시 이 로그가 없으면 SpreaderGrabber가 안 도는 것(컴파일/재생성 문제)
            if (debugLog)
                Debug.Log($"[Crane] SpreaderGrabber 활성 — 트위스트락 {twistlocks.Length}개, " +
                          $"집을수있는강체 {bodies.Count}개, 통과방지 {blockPassThrough}, 물리콜라이더 {spreaderCollider}");
        }

        // 트위스트락 콘들의 중심(없으면 부착점) — 잡기 기준점
        public Vector3 GrabPoint()
        {
            if (twistlocks != null && twistlocks.Length > 0)
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var t in twistlocks) if (t != null) { sum += t.position; n++; }
                if (n > 0) return sum / n;
            }
            return AttachPoint != null ? AttachPoint.position : transform.position;
        }

        /// <summary>콘 바닥 월드 Y(매단 컨테이너 제외). 원점≠콘끝(RTG 는 137mm 위) — CraneDemoRunner.SpreaderBottomY 와 같은 식.</summary>
        public float ConeBottomY()
        {
            Transform held = crane != null && crane.Attach != null ? crane.Attach.AttachedContainer : null;
            float y = float.MaxValue;
            if (twistlocks != null)
                foreach (var t in twistlocks)
                    if (t != null)
                        foreach (var r in t.GetComponentsInChildren<Renderer>())
                            if (held == null || !r.transform.IsChildOf(held)) y = Mathf.Min(y, r.bounds.min.y);
            if (y < float.MaxValue) return y;

            var sp = crane != null ? (crane.Spreader as Component) : null;   // 콘 미탐색 폴백 — 스프레더 최저 렌더러
            if (sp == null) return AttachPoint != null ? AttachPoint.position.y : transform.position.y;
            y = sp.transform.position.y;
            foreach (var r in sp.GetComponentsInChildren<Renderer>())
                if (held == null || !r.transform.IsChildOf(held)) y = Mathf.Min(y, r.bounds.min.y);
            return y;
        }

        /// <summary>SpreaderPusher 콜라이더 on/off — 자동 시나리오 하강 시 대상을 밀어 넘어뜨리지 않게 끈다.</summary>
        public void SetPusherActive(bool on)
        {
            if (pusherBox != null) pusherBox.enabled = on;
        }
        /// <summary>현재 푸셔 콜라이더 활성 여부(없으면 false).</summary>
        public bool IsPusherActive => pusherBox != null && pusherBox.enabled;

        /// <summary>스프레더(또는 잡은 컨테이너)가 받침(컨테이너/바닥) 위에 얹혀 멈췄는지 — 안착 표시용.</summary>
        public bool IsLanded { get; private set; }

        /// <summary>스프레더(빈 프레임/든 화물)가 컨테이너를 옆에서 친 충돌(Anti-Collision) — 경보용(3013 HO Snag).</summary>
        public bool LoadCollision { get; private set; }

        /// <summary>PLC 구동 중 받침을 뚫고 내려감 — 클램프 대신 경고만(HUD).</summary>
        public bool PassThrough { get; private set; }

        /// <summary>빈 스프레더가 후보 컨테이너 상단 코너캐스팅 위에 안착 정렬됨 — 지금 Y로 체결 가능(HUD 표시용).</summary>
        public bool ReadyToLock { get; private set; }

        /// <summary>빈 스프레더가 후보 근처지만 아직 안착 정렬 안 됨 — '모서리 정렬 필요'(HUD 표시용).</summary>
        public bool NearButUnseated { get; private set; }

        /// <summary>LoadCollisionRelay가 옆/아래 충돌 보고 — hold 동안 경보 유지.</summary>
        public void NotifyContact() => lastAntiCollTime = Time.time;
    }
}
