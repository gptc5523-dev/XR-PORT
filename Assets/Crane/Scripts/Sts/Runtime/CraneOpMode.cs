using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>크레인 운영상태 — 계획서/PLC 문서의 '운전 모드(운전·정지·이상)' 항목.
    /// PLC 실값 매핑은 ㈜엠비이 데이터 매핑 정의서에서 확정.</summary>
    public enum OpMode { Stopped = 0, Running = 1, Fault = 2 }

    /// <summary>
    /// STS 크레인 운영상태(O&amp;M 상태표시) 데이터 모델 — 1차년도 지표4(상태표시 정확도)의 입력.
    ///
    /// 설계:
    ///   - 현재는 우리 데이터(축 움직임 + 알람)로 운영상태를 '자체 판정'한다.
    ///   - PLC가 붙으면 Current 판정만 PLC가 내려주는 운전모드 값으로 교체하면 된다(IAxisMover와 동일 패턴).
    ///   - 이 컴포넌트는 '상태 표시'만 담당한다. 조작 주체(자동 사이클/수동 VR) 전환은 별개 축이며 여기서 다루지 않는다.
    ///
    /// 판정 우선순위(코드북 §7 안전 우선과 정합):
    ///   ① 알람 유효 → 이상(Fault)   ② 축 이동 중 → 운전(Running)   ③ 그 외 → 정지(Stopped)
    /// </summary>
    // H4: 가속 측정은 축 구동(PlcBridge, order -100)이 끝난 뒤 — 같은 물리틱에서 최종 위치를 읽도록 늦게 실행.
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Container/STS Crane/Crane Op Mode (운영상태)")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class CraneOpMode : MonoBehaviour
    {
        [Tooltip("이 프레임 위치차 합이 이 값을 넘으면 '움직이는 중'으로 본다(모델 단위). 부동소수 노이즈 무시용.")]
        [SerializeField] float moveEpsilon = 1e-5f;
        [Tooltip("마지막 움직임 후 이 시간(초) 동안은 '운전' 유지 — 간헐 입력에 상태가 깜빡이지 않게.")]
        [SerializeField] float runCoast = 0.25f;

        // ── 가속도 한계 알람(코드북 1021/2021/3021) ──
        // 미사용이던 '속도' 데이터를 한 번 더 미분해 '가속도(실척 m/s²)'를 얻고, 정격 초과 급조작을 자연발생 알람으로.
        // 실척 환산: 모델 units/s ÷ ModelScale(=1/24) = 실척 m/s (CraneStatusHUD.UpdateSpeeds와 동일 규약).
        // 임계 기본값은 STS 통상 정격 가속도(주행 느림·트롤리/권상 빠름)에 급조작 여유를 더한 보수값 — 실기 튜닝용 SerializeField.
        // H5 SSOT: 기본값은 CraneAxisProfile(모션생성용 정격과 한 출처)에서 가져온다. 트립 = 정격 × k(단일 안전계수).
        //   k = 1.667(=5/3) 세 축 공통 — 정상헤드룸1.3×노이즈마진1.25×안전1.025. GT 2.67× 불일치 해소(감사 H5).
        [Header("가속도 한계 (실척 m/s²) — 코드북 1021/2021/3021 (SSOT=CraneAxisProfile, 트립=정격×k)")]
        [Tooltip("갠트리(주행) 가속 트립 한계. SSOT=CraneAxisProfile.GantryAccelTrip (= 정격 0.15 × k 1.667 = 0.250). STS 주행 정격 가속 ~0.15 m/s².")]
        [SerializeField] float gantryAccelLimit = CraneAxisProfile.GantryAccelTrip;
        [Tooltip("트롤리 가속 트립 한계. SSOT=CraneAxisProfile.TrolleyAccelTrip (= 정격 0.60 × k 1.667 = 1.000). 정격 가속 ~0.6 m/s².")]
        [SerializeField] float trolleyAccelLimit = CraneAxisProfile.TrolleyAccelTrip;
        [Tooltip("권상(호이스트) 가속 트립 한계. SSOT=CraneAxisProfile.HoistAccelTrip (= 정격 0.50 × k 1.667 = 0.833). 정격 가속 ~0.5 m/s².")]
        [SerializeField] float hoistAccelLimit = CraneAxisProfile.HoistAccelTrip;
        [Tooltip("가속도 EMA 평활 계수(0~1). 위치 2차 미분의 프레임 노이즈를 누르려 작게. 클수록 즉답·노이즈↑. ※표시(HUD)용만 — 트립 판정엔 raw값 사용.")]
        [SerializeField, Range(0.05f, 1f)] float accelSmoothing = 0.2f;

        // H3 트립 판정 분리 — EMA는 피크를 깎아 트립을 누락시키므로 트립은 raw 가속도로 보고, 히스테리시스+디바운스로 채터링만 차단.
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

        // 가속도 추적 — 물리틱(FixedUpdate)에서 측정(고정 dt라 2차 미분 노이즈가 가변 프레임보다 작다).
        float fpG, fpT, fpH;     // 직전 FixedUpdate 축 위치(모델 units)
        float vG, vT, vH;        // 직전 실척 속도(m/s)
        float aG, aT, aH;        // 평활된 실척 가속도 크기(m/s²) — 표시용(HUD)
        bool fPrimed;            // 가속 추적 첫 틱 완료

        // 축별 트립 상태머신(raw 가속도 기준). trip=현재 트립, over=연속 초과 틱, under=연속 복귀 틱.
        bool tripG, tripT, tripH;
        int overG, overT, overH, underG, underT, underH;

        // H5 되감기 마스킹 — 위치 불연속(CSV loop 등) 직후 이 틱 수만큼 가속 측정을 건너뛰고 속도 baseline만 재구축.
        // 2틱 필요: 불연속 틱의 점프 속도가 다음 가속 계산에 새지 않도록 깨끗한 속도 표본 1개를 먼저 확보.
        int accelWarmup;

        /// <summary>
        /// 가속도 한계 알람(1021/2021/3021)을 평가할지 — PLC 실데이터일 때만 true.
        /// 직접조종(임시입력)은 가감속 램프가 없어 출발/정지마다 0↔정격을 한 틱에 점프(≈37 m/s²)하므로
        /// 가속도가 항상 비현실적 → 오경보. PLC는 인버터 가감속 프로파일이 있어 정상 운전이 정격 이내(≈0.17 m/s²).
        /// PlcBridge가 PLC 연결 시 true로 세팅. 기본 false(직접조종) = 가속 알람 비활성.
        /// </summary>
        public bool PlcDriven { get; set; }

        /// <summary>축별 현재 가속도 크기(실척 m/s², EMA 평활) — HUD 표시용.</summary>
        public float GantryAccel  => aG;
        public float TrolleyAccel => aT;
        public float HoistAccel   => aH;
        public float GantryAccelLimit  => gantryAccelLimit;
        public float TrolleyAccelLimit => trolleyAccelLimit;
        public float HoistAccelLimit   => hoistAccelLimit;

        /// <summary>축별 가속도 트립(디바운스·히스테리시스 적용) — CraneFault 가속 알람(1021/2021/3021) 입력.</summary>
        public bool GantryAccelTripped  => tripG;
        public bool TrolleyAccelTripped => tripT;
        public bool HoistAccelTripped   => tripH;

        /// <summary>축 3개 중 하나라도 runCoast 이내에 움직였는지(=운전 중).</summary>
        public bool IsMoving => Time.unscaledTime - lastMoveTime < runCoast;

        /// <summary>
        /// 알람 시스템(코드북 SSOT) 오프라인 여부 — fail-to-safe 게이트(재감사 신규결함 #1).
        /// true면 알람 정의를 신뢰할 수 없으므로 운영상태를 Fault로 강제하고 autoStop 해야 한다.
        /// HUD/로그가 '알람 시스템 오프라인 — 안전정지'를 영구 표시하는 데 쓴다. 정상 시 false(거동 불변).
        /// </summary>
        public bool AlarmSystemOffline => !AlarmCodebook.IsLoaded;

        /// <summary>
        /// 현재 운영상태에서 PLC 자동정지(autoStop)를 걸어야 하는가 — 현재 활성 알람의 AutoStop 플래그.
        /// 코드북 미로드 시엔 비상 FaultDef(AutoStop=true)가 반환되므로 자동 true(안전측 실패).
        /// </summary>
        public bool ShouldAutoStop => CraneFault.Evaluate(crane).AutoStop;

        /// <summary>현재 운영상태(자체 판정). PLC 연동 시 이 getter만 교체.</summary>
        public OpMode Current
        {
            get
            {
                // ⓪ fail-to-safe(최우선): 알람 시스템(코드북) 오프라인이면 무조건 Fault로 강제 + autoStop.
                //    조용히 '알람 0'으로 정상 운전하는 단일 실패점 차단(재감사 신규결함 #1). 정상 시엔 통과 → 거동 불변.
                //    ※ 아래 CraneFault.Evaluate도 같은 경우 비상 FaultDef를 내므로 ①에서도 Fault가 되지만,
                //      여기서 명시 분기를 둬 의도(안전상태 전이)를 코드로 드러낸다.
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

        // 가속도 측정은 고정 dt(FixedUpdate)에서 — 위치를 두 번 미분하므로 dt 흔들림이 큰 Update보다 노이즈가 작다.
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 1e-6f || crane == null) return;
            float toReal = crane.ModelScale > 1e-9f ? 1f / crane.ModelScale : StsConfig.InvModelScale;   // 모델 units → 실척 m (=×24). 폴백 24f=1/ModelScale, SSOT.

            float g = crane.Gantry  != null ? crane.Gantry.Current  : 0f;
            float t = crane.Trolley != null ? crane.Trolley.Current : 0f;
            float h = crane.Spreader != null ? crane.Spreader.Current : 0f;

            if (fPrimed && accelWarmup == 0)
            {
                StepAccel(g, fpG, ref vG, ref aG, ref tripG, ref overG, ref underG, gantryAccelLimit,  dt, toReal);
                StepAccel(t, fpT, ref vT, ref aT, ref tripT, ref overT, ref underT, trolleyAccelLimit, dt, toReal);
                StepAccel(h, fpH, ref vH, ref aH, ref tripH, ref overH, ref underH, hoistAccelLimit,   dt, toReal);
            }
            else if (fPrimed)
            {
                // 워밍업: 위치 불연속 직후 — 속도 baseline만 재구축하고 가속/트립 판정은 건너뜀(H5 인공 스파이크 차단).
                vG = (g - fpG) * toReal / dt;
                vT = (t - fpT) * toReal / dt;
                vH = (h - fpH) * toReal / dt;
                accelWarmup--;
            }
            // 위치(직전 틱) 갱신은 여기 한 곳이 단독 소유 — StepAccel은 prev를 읽기만 한다(H3 이중 갱신 제거).
            fpG = g; fpT = t; fpH = h; fPrimed = true;
        }

        /// <summary>가속 추적 재프라임 — CSV 되감기 등 위치 불연속 구간의 인공 가속 스파이크(H5)를 막기 위해 PlcBridge가 호출.</summary>
        public void ResetAccelTracking() => accelWarmup = 2;

        // 위치(모델 units) → 실척 속도(m/s) → 가속도 크기(m/s²). accel은 절대값(증·감속 둘 다 한계 대상).
        // prev(직전 위치)는 읽기 전용 — 위치 전진은 호출부(FixedUpdate) 단독 책임.
        // 표시값(aSmooth)은 EMA로 평활하되, 트립 판정(trip)은 raw aNow로 — EMA가 짧은 피크를 깎아 트립을 누락(H3)하지 않도록.
        void StepAccel(float cur, float prev, ref float vPrev, ref float aSmooth,
                       ref bool trip, ref int over, ref int under, float limit, float dt, float toReal)
        {
            float vNow = (cur - prev) * toReal / dt;          // 실척 m/s (부호 유지 — 가속/감속 방향)
            float aNow = Mathf.Abs(vNow - vPrev) / dt;        // 실척 m/s² (raw, 피크 보존)
            aSmooth += (aNow - aSmooth) * accelSmoothing;     // 표시용 EMA
            vPrev = vNow;

            // 트립 디바운스 + 히스테리시스: 한계 초과 N틱 연속 → set / 해제임계(한계×frac) 이하 M틱 연속 → clear.
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
