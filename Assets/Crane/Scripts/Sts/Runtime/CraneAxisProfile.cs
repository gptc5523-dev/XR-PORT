namespace AIXRCrane.Crane.Sts
{
    /// <summary>STS 크레인 축(갠트리·트롤리·권상) 운동 프로파일 SSOT — 정격·트립 한계 상수를 한 곳에서 파생.
    /// 속도 m/s, 가속도 m/s²(실척). 모델 단위 환산은 소비처가 <see cref="StsConfig.ModelScale"/>로 처리.</summary>
    public static class CraneAxisProfile
    {
        // 1) 정격 속도 (실척 m/s) — VirtualPlcSource 최고 속도 클램프. v = 카탈로그 m/min ÷ 60(포스트파나막스급).

        /// <summary>갠트리(주행) 정격 속도. v = 42 m/min ÷ 60 = 0.70 m/s.</summary>
        public const float GantryMaxSpeed  = 0.7f;
        /// <summary>트롤리(횡행) 정격 속도. v = 210 m/min ÷ 60 = 3.5 m/s. (옛값 4.0f)</summary>
        public const float TrolleyMaxSpeed = 3.5f;
        /// <summary>권상(호이스트) 정격 속도(정격하중). v = 75 m/min ÷ 60 = 1.25 m/s. 공차는 약 2×.</summary>
        public const float HoistMaxSpeed   = 1.25f;

        // 2) 정격 가속도 (실척 m/s²) — VirtualPlcSource 트라페조이드 램프 한계. a = v_정격 / τ_ramp (τ≈2~6s).

        /// <summary>갠트리 정격 가속. a = v/τ = 0.70/4.7 = 0.15 m/s².</summary>
        public const float GantryRatedAccel  = 0.15f;
        /// <summary>트롤리 정격 가속. a = v/τ = 3.50/5.8 = 0.60 m/s².</summary>
        public const float TrolleyRatedAccel = 0.6f;
        /// <summary>권상 정격 가속. a = v/τ = 1.25/2.5 = 0.50 m/s². (옛값 0.6f)</summary>
        public const float HoistRatedAccel   = 0.50f;

        // 3) 가속도 트립 한계 (실척 m/s²) — CraneOpMode 트립 판정 임계(코드북 1021/2021/3021).
        //    트립 = 정격가속 × k(TripMargin, 세 축 공통) — 한쪽만 바뀌는 드리프트를 구조적으로 막는다.

        /// <summary>트립/정격 안전계수 k = 정상헤드룸1.3 × 노이즈마진1.25 × 안전1.025 ≈ 5/3. 세 축 공통.</summary>
        public const float TripMargin = 5f / 3f;   // ≈ 1.667

        // 트립 한계 = 정격가속 × k 로 파생(리터럴 박지 않음) → 정격을 바꾸면 트립도 자동 추종.
        /// <summary>갠트리 가속 트립 한계 = 정격 0.15 × k = 0.250 m/s². (옛값 0.40f)</summary>
        public const float GantryAccelTrip  = GantryRatedAccel  * TripMargin;   // = 0.250
        /// <summary>트롤리 가속 트립 한계 = 정격 0.60 × k = 1.000 m/s².</summary>
        public const float TrolleyAccelTrip = TrolleyRatedAccel * TripMargin;   // = 1.000
        /// <summary>권상 가속 트립 한계 = 정격 0.50 × k = 0.833 m/s². (옛값 1.00f)</summary>
        public const float HoistAccelTrip   = HoistRatedAccel   * TripMargin;   // = 0.833

        // 정격↔트립 마진(트립 ÷ 정격) — 이 파생식이 불일치를 컴파일타임에 드러낸다(SSOT 자기검증).
        /// <summary>갠트리 트립/정격 마진(=k). 세 축 통일.</summary>
        public const float GantryTripMargin  = GantryAccelTrip  / GantryRatedAccel;
        /// <summary>트롤리 트립/정격 마진(=k).</summary>
        public const float TrolleyTripMargin = TrolleyAccelTrip / TrolleyRatedAccel;
        /// <summary>권상 트립/정격 마진(=k).</summary>
        public const float HoistTripMargin   = HoistAccelTrip   / HoistRatedAccel;

        // 4) 트립 디바운스·히스테리시스 — 트립 판정(CraneOpMode)의 채터링 차단 파라미터.
        //    물리적 의미: 노이즈 1틱 스파이크/경계 떨림을 무시. 물리틱(기본 50Hz) 기준 틱 수·비율.

        /// <summary>트립 발동 디바운스 — 한계 초과가 이 틱 수 연속돼야 알람. 현재 3틱(50Hz=60ms).</summary>
        public const int   AccelTripSetN   = 3;
        /// <summary>트립 해제 디바운스 — 복귀 임계 이하가 이 틱 수 연속돼야 해제. 현재 5틱(50Hz=100ms).</summary>
        public const int   AccelTripClearN = 5;
        /// <summary>트립 해제 데드밴드 — 해제 임계 = 한계 × 이 값(히스테리시스). 현재 0.8.</summary>
        public const float AccelClearFrac  = 0.8f;

        // 5) 급조작 주입(데모/검증) 및 시나리오 상수.

        /// <summary>급조작 주입 시 가속 한계 배수 — 정격 초과 가속으로 가속알람 자연발생. 현재 3×.</summary>
        public const float AggressiveAccelMul = 3f;
        /// <summary>양하 표준 하중(t, 시나리오 S02). 과부하(3012)는 CraneFault가 판정. 현재 32 t.</summary>
        public const float CarryLoadTon = 32f;
        /// <summary>안착 하강 높이(실척 m, range 비례 아님 — 절대값). 현재 2 m.</summary>
        public const float HoLowM = 2f;
    }
}
