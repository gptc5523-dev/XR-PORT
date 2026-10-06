using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>크레인 운영상태 — 계획서/PLC 문서의 '운전 모드(운전·정지·이상)' 항목.
    /// PLC 실값 매핑은 ㈜엠비이 데이터 매핑 정의서에서 확정.</summary>
    public enum OpMode { Stopped = 0, Running = 1, Fault = 2 }

    /// <summary>STS 운영상태 모델 — 알람 유효→Fault, 축 이동→Running, 그 외 Stopped(코드북 §7 안전 우선). PLC 연동 시 Current만 교체.
    /// 축 구동(PlcBridge, order -100) 이후 실행 — 같은 물리틱의 최종 위치를 읽는다.</summary>
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("AI-XR Crane/STS Crane/Crane Op Mode (운영상태)")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class CraneOpMode : MonoBehaviour
    {
        [Tooltip("이 프레임 위치차 합이 이 값을 넘으면 '움직이는 중'으로 본다(모델 단위). 부동소수 노이즈 무시용.")]
        [SerializeField] float moveEpsilon = 1e-5f;
        [Tooltip("마지막 움직임 후 이 시간(초) 동안은 '운전' 유지 — 간헐 입력에 상태가 깜빡이지 않게.")]
        [SerializeField] float runCoast = 0.25f;

        // 가속도 한계 알람(1021/2021/3021) — 속도를 한 번 더 미분한 실척 m/s²로 급조작 검출.
        // 기본값 SSOT=CraneAxisProfile, 트립=정격×k(=5/3).
        [Header("가속도 한계 (실척 m/s²) — 코드북 1021/2021/3021 (SSOT=CraneAxisProfile, 트립=정격×k)")]
        [Tooltip("갠트리(주행) 가속 트립 한계. SSOT=CraneAxisProfile.GantryAccelTrip (= 정격 0.15 × k 1.667 = 0.250). STS 주행 정격 가속 ~0.15 m/s².")]
        [SerializeField] float gantryAccelLimit = CraneAxisProfile.GantryAccelTrip;
        [Tooltip("트롤리 가속 트립 한계. SSOT=CraneAxisProfile.TrolleyAccelTrip (= 정격 0.60 × k 1.667 = 1.000). 정격 가속 ~0.6 m/s².")]
        [SerializeField] float trolleyAccelLimit = CraneAxisProfile.TrolleyAccelTrip;
        [Tooltip("권상(호이스트) 가속 트립 한계. SSOT=CraneAxisProfile.HoistAccelTrip (= 정격 0.50 × k 1.667 = 0.833). 정격 가속 ~0.5 m/s².")]
        [SerializeField] float hoistAccelLimit = CraneAxisProfile.HoistAccelTrip;

        // 트립은 raw 가속도로 판정(EMA는 피크를 깎아 누락), 히스테리시스+디바운스로 채터링 차단.
        [Tooltip("트립 발동 디바운스 — 한계 초과가 이 틱 수만큼 '연속'돼야 알람(짧은 노이즈 1틱 스파이크 무시). 물리틱(기본 50Hz) 기준. SSOT=CraneAxisProfile.AccelTripSetN.")]
        [SerializeField, Range(1, 20)] int accelTripSetN = CraneAxisProfile.AccelTripSetN;
        [Tooltip("트립 해제 디바운스 — 복귀 임계 이하가 이 틱 수만큼 연속돼야 해제(경계 채터링 방지). SSOT=CraneAxisProfile.AccelTripClearN.")]
        [SerializeField, Range(1, 30)] int accelTripClearN = CraneAxisProfile.AccelTripClearN;
        [Tooltip("트립 해제 데드밴드 — 해제 임계 = 한계 × 이 값. set(한계) > clear(한계×frac) 히스테리시스로 경계 떨림 차단. SSOT=CraneAxisProfile.AccelClearFrac.")]
        [SerializeField, Range(0.3f, 0.99f)] float accelClearFrac = CraneAxisProfile.AccelClearFrac;

        StsCrane crane;
        float prevG, prevT, prevH;
        float lastMoveTime = -999f;
        bool primed;   // 첫 프레임 위치 캡처 완료(초기 0→실제값 점프를 이동으로 오인하지 않게)

        // 가속도 추적은 FixedUpdate에서 — 고정 dt라 2차 미분 노이즈가 작다.
        float fpG, fpT, fpH;     // 직전 FixedUpdate 축 위치(모델 units)
        float vG, vT, vH;        // 직전 실척 속도(m/s)
        bool fPrimed;            // 가속 추적 첫 틱 완료

        // 축별 트립 상태머신(raw 가속도 기준). trip=현재 트립, over=연속 초과 틱, under=연속 복귀 틱.
        bool tripG, tripT, tripH;
        int overG, overT, overH, underG, underT, underH;

        // 되감기 마스킹 — 위치 불연속 직후 이 틱 수만큼 가속 측정을 건너뛰고 속도 baseline만 재구축.
        // 2틱: 점프 속도가 다음 가속 계산에 새지 않게 깨끗한 속도 표본 1개를 먼저 확보.
        int accelWarmup;

        /// <summary>가속도 한계 알람 평가 여부 — PLC 실데이터일 때만 true.
        /// 직접조종은 가감속 램프가 없어 오경보라 기본 false.</summary>
        public bool PlcDriven { get; set; }

        /// <summary>축별 가속도 트립(디바운스·히스테리시스 적용) — CraneFault 가속 알람(1021/2021/3021) 입력.</summary>
        public bool GantryAccelTripped  => tripG;
        public bool TrolleyAccelTripped => tripT;
        public bool HoistAccelTripped   => tripH;

        /// <summary>축 3개 중 하나라도 runCoast 이내에 움직였는지(=운전 중).</summary>
        bool IsMoving => Time.unscaledTime - lastMoveTime < runCoast;

        /// <summary>알람 시스템(코드북) 오프라인 — fail-to-safe: true면 운영상태 Fault 강제 + autoStop.</summary>
        bool AlarmSystemOffline => !AlarmCodebook.IsLoaded;

        /// <summary>현재 운영상태(자체 판정). PLC 연동 시 이 getter만 교체.</summary>
        public OpMode Current
        {
            get
            {
                // ⓪ fail-to-safe(최우선): 알람 시스템 오프라인이면 무조건 Fault — '알람 0'으로 조용히 정상 운전하는 단일 실패점 차단.
                if (AlarmSystemOffline)                 return OpMode.Fault;    // ⓪ 알람 시스템 오프라인 — 안전정지
                if (CraneFault.Evaluate(crane).IsValid) return OpMode.Fault;    // ① 이상 — 안전 최우선
                if (IsMoving)                           return OpMode.Running;  // ② 축 이동 중
                return OpMode.Stopped;                                          // ③ 정지(미가동)
            }
        }

        void Awake() => crane = GetComponent<StsCrane>();

        void Update()
        {
            float g = crane.Gantry  != null ? crane.Gantry.Current  : 0f;
            float t = crane.Trolley != null ? crane.Trolley.Current : 0f;
            float h = crane.Spreader != null ? crane.Spreader.Current : 0f;
            if (primed)
            {
                float d = Mathf.Abs(g - prevG) + Mathf.Abs(t - prevT) + Mathf.Abs(h - prevH);
                if (d > moveEpsilon) lastMoveTime = Time.unscaledTime;
            }
            prevG = g; prevT = t; prevH = h; primed = true;
        }

        // 가속도 측정은 고정 dt(FixedUpdate)에서 — 위치 2차 미분이라 dt 흔들림에 민감.
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 1e-6f || crane == null) return;
            float toReal = crane.ModelScale > 1e-9f ? 1f / crane.ModelScale : StsConfig.InvModelScale;   // 모델 units → 실척 m

            float g = crane.Gantry  != null ? crane.Gantry.Current  : 0f;
            float t = crane.Trolley != null ? crane.Trolley.Current : 0f;
            float h = crane.Spreader != null ? crane.Spreader.Current : 0f;
            // 축마다 월드/축 배율이 다르다(FBX RTG 트롤리 4.17) — 공용 배율이면 RTG 트롤리 알람이 침묵한다.
            float rG = toReal * (crane.Gantry   != null ? crane.Gantry.WorldPerUnit   : 1f);
            float rT = toReal * (crane.Trolley  != null ? crane.Trolley.WorldPerUnit  : 1f);
            float rH = toReal * (crane.Spreader != null ? crane.Spreader.WorldPerUnit : 1f);

            if (fPrimed && accelWarmup == 0)
            {
                StepAccel(g, fpG, ref vG, ref tripG, ref overG, ref underG, gantryAccelLimit,  dt, rG);
                StepAccel(t, fpT, ref vT, ref tripT, ref overT, ref underT, trolleyAccelLimit, dt, rT);
                StepAccel(h, fpH, ref vH, ref tripH, ref overH, ref underH, hoistAccelLimit,   dt, rH);
            }
            else if (fPrimed)
            {
                // 워밍업: 불연속 직후 — 속도 baseline만 재구축, 가속/트립 판정 건너뜀.
                vG = (g - fpG) * rG / dt;
                vT = (t - fpT) * rT / dt;
                vH = (h - fpH) * rH / dt;
                accelWarmup--;
            }
            // 직전 위치 갱신은 여기 한 곳이 소유 — StepAccel은 읽기만.
            fpG = g; fpT = t; fpH = h; fPrimed = true;
        }

        /// <summary>가속 추적 재프라임 — CSV 되감기 등 위치 불연속의 인공 스파이크 방지(PlcBridge 호출).</summary>
        public void ResetAccelTracking() => accelWarmup = 2;

        // 위치(모델 units) → 실척 속도(m/s) → |가속도|(m/s²). prev는 읽기 전용.
        // 트립 판정은 raw aNow — 평활하면 피크가 깎여 트립을 놓친다.
        void StepAccel(float cur, float prev, ref float vPrev,
                       ref bool trip, ref int over, ref int under, float limit, float dt, float toReal)
        {
            float vNow = (cur - prev) * toReal / dt;          // 실척 m/s (부호 유지 — 가속/감속 방향)
            float aNow = Mathf.Abs(vNow - vPrev) / dt;        // 실척 m/s² (raw, 피크 보존)
            vPrev = vNow;

            // 트립 디바운스+히스테리시스: 한계 초과 N틱 연속 → set / 한계×frac 이하 M틱 연속 → clear.
            float clear = limit * accelClearFrac;
            if (!trip)
            {
                if (aNow >= limit) { if (++over >= accelTripSetN) { trip = true; under = 0; } }
                else over = 0;
            }
            else
            {
                if (aNow <= clear) { if (++under >= accelTripClearN) { trip = false; over = 0; } }
                else under = 0;
            }
        }

        /// <summary>운영상태 한글 표기(HUD).</summary>
        public static string Label(OpMode m) => m switch
        {
            OpMode.Running => "운전",
            OpMode.Stopped => "정지",
            _              => "이상",
        };

        /// <summary>운영상태 표시색 — 운전=녹 / 정지=회 / 이상=적(코드북 §7 색 규약과 정합).</summary>
        public static Color ModeColor(OpMode m) => m switch
        {
            OpMode.Running => new Color(0.30f, 0.80f, 0.40f),
            OpMode.Stopped => new Color(0.65f, 0.65f, 0.68f),
            _              => new Color(0.92f, 0.20f, 0.18f),
        };
    }
}
