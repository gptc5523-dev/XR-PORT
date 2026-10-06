namespace AIXRCrane.Crane.Sts
{
    /// <summary>STS 축(갠트리·트롤리·권상) 운동 프로파일 SSOT — 정격·트립 한계.
    /// 실척 m/s·m/s². 모델 단위 환산은 소비처가 <see cref="StsConfig.ModelScale"/>로.</summary>
    public static class CraneAxisProfile
    {
        // 1) 정격 속도(실척 m/s) — VirtualPlcSource 최고 속도 클램프. 카탈로그 m/min ÷ 60.

        /// <summary>갠트리(주행) 정격 속도 42 m/min.</summary>
        public const float GantryMaxSpeed  = 0.7f;
        /// <summary>트롤리(횡행) 정격 속도 210 m/min.</summary>
        public const float TrolleyMaxSpeed = 3.5f;
        /// <summary>권상 정격 속도(정격하중) 75 m/min. 공차는 약 2×.</summary>
        public const float HoistMaxSpeed   = 1.25f;

        // 2) 정격 가속도(실척 m/s²) — VirtualPlcSource 램프 한계. a = v_정격 / τ_ramp(τ≈2~6s).

        /// <summary>갠트리 정격 가속(τ≈4.7s).</summary>
        public const float GantryRatedAccel  = 0.15f;
        /// <summary>트롤리 정격 가속(τ≈5.8s).</summary>
        public const float TrolleyRatedAccel = 0.6f;
        /// <summary>권상 정격 가속(τ≈2.5s).</summary>
        public const float HoistRatedAccel   = 0.50f;

        // 3) 가속도 트립 한계(실척 m/s², 코드북 1021/2021/3021) = 정격가속 × k — 한쪽만 바뀌는 드리프트 방지.

        /// <summary>트립/정격 안전계수 k = 헤드룸1.3 × 노이즈1.25 × 안전1.025 ≈ 5/3. 세 축 공통.</summary>
        public const float TripMargin = 5f / 3f;   // ≈ 1.667

        /// <summary>갠트리 가속 트립 한계(정격 × k).</summary>
        public const float GantryAccelTrip  = GantryRatedAccel  * TripMargin;   // = 0.250
        /// <summary>트롤리 가속 트립 한계(정격 × k).</summary>
        public const float TrolleyAccelTrip = TrolleyRatedAccel * TripMargin;   // = 1.000
        /// <summary>권상 가속 트립 한계(정격 × k).</summary>
        public const float HoistAccelTrip   = HoistRatedAccel   * TripMargin;   // = 0.833

        // 4) 트립 디바운스·히스테리시스(CraneOpMode 채터링 차단). 물리틱(50Hz) 기준.

        /// <summary>트립 발동 디바운스 — 한계 초과 연속 틱 수(3틱=60ms).</summary>
        public const int   AccelTripSetN   = 3;
        /// <summary>트립 해제 디바운스 — 해제 임계 이하 연속 틱 수(5틱=100ms).</summary>
        public const int   AccelTripClearN = 5;
        /// <summary>트립 해제 임계 = 한계 × 이 값(히스테리시스).</summary>
        public const float AccelClearFrac  = 0.8f;
        /// <summary>가속 측정 창(초) — 행 간격(100ms)보다 길어야 지속 가속이 보이고, 0.2s면 mm 반올림 잡음(100ms에서 ±0.2m/s²)이 GT 한계 아래로 준다.</summary>
        public const float AccelWindowS    = 0.2f;

        // 5) 급조작 주입(데모/검증) 및 시나리오 상수.

        /// <summary>급조작 주입 시 가속 한계 배수 — 가속알람 자연발생용.</summary>
        public const float AggressiveAccelMul = 3f;
        /// <summary>양하 표준 하중(t, 시나리오 S02). 과부하(3012)는 CraneFault 판정.</summary>
        public const float CarryLoadTon = 32f;
        /// <summary>안착 하강 높이(실척 m, 절대값).</summary>
        public const float HoLowM = 2f;
    }
}
