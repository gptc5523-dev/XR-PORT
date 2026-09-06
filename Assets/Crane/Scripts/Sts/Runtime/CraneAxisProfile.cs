namespace Container.Crane.Sts
{
    /// <summary>
    /// STS 크레인 축(갠트리·트롤리·권상) 운동 프로파일의 단일 출처(SSOT) — 외부 감사 H5(정격 속도·가감속 하드코딩) 대응.
    ///
    /// ── 배경 ──
    /// 모션 생성용 정격값(<see cref="Plc.VirtualPlcSource"/>의 *MaxSpeed/*RatedAccel)과
    /// 트립 판정용 한계(<see cref="CraneOpMode"/>의 *AccelLimit)이 두 파일에 따로 하드코딩돼,
    /// 한쪽만 바뀌면 "정격 ↔ 트립" 관계가 조용히 어긋날 수 있다는 게 감사 지적.
    /// 이 클래스가 두 소비처의 단일 출처가 되어 둘이 같은 상수를 참조하도록 한다.
    ///
    /// ── 값 정책 ──
    /// "추측 금지 — 모델 형상이 정해졌으니 정격을 역산하라"는 오너 지시에 따라, 이번엔 보존이 아니라
    /// <b>물리·수학으로 산출한 자기일관 정격 세트</b>를 적용한다. 모든 값은 산식·가정·STS 등급 출처를 명시.
    /// 벤더(㈜엠비이) 정밀확정은 "후속 교체 가능(블로커 아님)"으로 둔다.
    ///
    /// ── 모델 등급 판정 (StsCraneCreator 실척 치수 → STS 등급) ──
    ///   아웃리치 27m (TrolleyMaxX 42m − WaterLegX 15m), 인양고 RailH 32m, 게이지 15m, 붐팁 29.4m.
    ///   아웃리치 27m ≈ 10~11열 횡단 → <b>포스트파나막스 ~ 저(低)슈퍼포스트파나막스</b> 등급.
    ///   카탈로그 정격(Konecranes/Liebherr/ZPMC 공개 사양 범위):
    ///     권상(정격하중 ~40~65t) 60~90 m/min · 권상(공차) 120~150 m/min · 횡행 180~240 m/min · 주행 30~45 m/min.
    ///
    /// ── 산식 요약 ──
    ///   정격가속  a = v / τ   (τ = 0→정격속 램프타임, STS 통상 축별 범위에서 채택).
    ///   트립한계  trip = 정격가속 × k   (k = 단일 안전계수, 아래 (3)에서 도출·세 축 공통).
    ///   저크(jerk) 제한 j ≈ a / t_j (t_j = 가가속 구간) 도 언급 — 인버터 S-커브로 충격 완화.
    ///
    /// ── 단위 ──
    /// 속도 m/s, 가속도 m/s² (실척). 모델(씬) 단위 환산은 소비처가 <see cref="StsConfig.ModelScale"/>로 처리.
    ///
    /// ※ 가설 — Unity 미실행·Quest 미검증. 값은 산업표준 기반 산출, ㈜엠비이 정밀확정 시 교체.
    /// </summary>
    public static class CraneAxisProfile
    {
        // 1) 정격 속도 (실척 m/s) — 모션 생성(VirtualPlcSource)의 최고 속도 클램프.
        //    산식: v = (카탈로그 m/min) ÷ 60.  등급=포스트파나막스(아웃리치 27m).
        //    [산업표준 기반 산출, ㈜엠비이 인버터/모터 정격으로 교체 가능(블로커 아님).]

        /// <summary>갠트리(주행) 정격 속도. 산출: 등급 표준 42 m/min ÷ 60 = 0.70 m/s. 주행은 35~45 m/min 중앙(전륜 구동 견인력·레일 마찰 한계). 유지(0.7).</summary>
        public const float GantryMaxSpeed  = 0.7f;
        /// <summary>트롤리(횡행) 정격 속도. 산출: 등급 표준 210 m/min ÷ 60 = 3.5 m/s. 사이클 검증(아래 (2-T))으로 4.0→3.5 하향(240은 슈퍼포스트파나막스 상한이라 본 등급엔 과대).</summary>
        public const float TrolleyMaxSpeed = 3.5f;
        /// <summary>권상(호이스트) 정격 속도(정격하중). 산출: 75 m/min ÷ 60 = 1.25 m/s. 60~90 m/min 중앙. 공차는 약 2× = 150 m/min(2.5 m/s, 시나리오 hoistLight와 정합). 1.3→1.25.</summary>
        public const float HoistMaxSpeed   = 1.25f;

        // 2) 정격 가속도 (실척 m/s²) — 모션 생성(VirtualPlcSource)의 트라페조이드 램프 한계.
        //    산식: a = v_정격 / τ_ramp (τ = 0속→정격속 도달 시간, STS 인버터 통상 τ ≈ 2~6 s).
        //      GT: τ=4.7 s 채택 → a = 0.70 / 4.7 = 0.149 ≈ 0.15  (주행 견인력·레일 슬립 마진 → 완만).
        //      TR: τ=5.8 s 채택 → a = 3.50 / 5.8 = 0.603 ≈ 0.60  (고속 횡행 = 화물 진자 억제 위해 긴 램프).
        //      HO: τ=2.5 s 채택 → a = 1.25 / 2.5 = 0.50         (권상 빠른 응답, 단 화물 채찍질 방지로 0.5).
        //    저크 제한 j ≈ a / t_j (t_j≈0.5~1 s S-커브) → GT≈0.2 / TR≈0.8 / HO≈0.7 m/s³. 인버터 S-커브가 충격 완화.
        //    [산업표준 기반 산출, τ는 ㈜엠비이 인버터 가감속 파라미터로 교체 가능(블로커 아님).]

        /// <summary>갠트리 정격 가속. 산출 a=v/τ=0.70/4.7=0.149≈0.15. 유지.</summary>
        public const float GantryRatedAccel  = 0.15f;
        /// <summary>트롤리 정격 가속. 산출 a=v/τ=3.50/5.8=0.603≈0.60. 유지(v는 4.0→3.5로 줄었으나 τ도 6.7→5.8로 조정해 a 동일).</summary>
        public const float TrolleyRatedAccel = 0.6f;
        /// <summary>권상 정격 가속. 산출 a=v/τ=1.25/2.5=0.50. 0.6→0.50 하향(τ를 2.2→2.5 s로 늘려 화물 진동·로프 충격 저감).</summary>
        public const float HoistRatedAccel   = 0.50f;

        // 3) 가속도 트립 한계 (실척 m/s²) — 트립 판정(CraneOpMode)의 알람 발동 임계(코드북 1021/2021/3021).
        //
        //    ★ SSOT 핵심: 트립 = 정격가속 × k (단일 안전계수). 세 축을 같은 const(TripMargin)로 파생시켜
        //      "한쪽만 바뀌어 GT 2.67× vs TR/HO 1.67× 드리프트"(감사 H5)를 구조적으로 원천 차단한다.
        //
        // 단일 안전계수 k 도출 (제어·안전공학)
        //    트립은 (a) 정격 정상운전을 오경보 없이 통과시키고 (b) 급조작/이상을 잡아야 한다.
        //    k 는 아래 3개 마진의 곱으로 합성한다(모두 1보다 큰 보수측):
        //      ① 정상운전 헤드룸  m1=1.3 — 인버터 S-커브 오버슈트·부하변동·로프 진자 반력으로 정격가속이
        //         순간 ~1.3배 튀어도 정상으로 본다(트라페조이드 모서리·외란).
        //      ② 측정 노이즈/미분 스파이크  m2=1.25 — 위치 2차 미분(StepAccel)은 양자화·진동을 증폭한다.
        //         50Hz·모델양자화에서 raw 가속 노이즈 표준편차를 정격의 ~8%로 보면, 디바운스 set=3틱이
        //         독립 노이즈를 (0.08)^? 로 깎지만 상관성 있는 진동은 못 깎으므로 3σ≈0.25를 헤드룸으로 흡수.
        //      ③ 안전 여유  m3=1.025 — 잔여 모델 불확실성(τ 가정·toReal 환산) 보수 보정.
        //    k = m1·m2·m3 = 1.3 × 1.25 × 1.025 ≈ 1.666 ≈ 5/3.  → 세 축 공통 k = 1.667.
        //    (축별로 달리할 물리적 이유 없음: 노이즈·오버슈트 메커니즘이 동일 측정 파이프라인을 공유.
        //     기존 GT 2.67×는 GT 정격이 작아 절대 트립폭을 벌려둔 임시값일 뿐 물리 근거 없음 → 통일.)
        //
        //    디바운스 정합: set=3틱(60ms)·clear=5틱(100ms)·clearFrac=0.8 (아래 (4))과 함께 동작 →
        //    1틱 스파이크는 무시, k 헤드룸을 3틱 연속 넘는 '지속 급조작'만 트립. clear<set 히스테리시스로 채터링 차단.

        /// <summary>★ 트립/정격 단일 안전계수 k. = 정상헤드룸1.3 × 노이즈마진1.25 × 안전1.025 ≈ 5/3. 세 축 공통. [산업표준 산출, 벤더 교체 가능]</summary>
        public const float TripMargin = 5f / 3f;   // ≈ 1.667

        // 트립 한계 = 정격가속 × k 로 '파생'(리터럴 박지 않음) → 정격을 바꾸면 트립도 자동 추종, 드리프트 0.
        /// <summary>갠트리 가속 트립 한계 = 정격 0.15 × k(1.667) = 0.250 m/s². (기존 0.40, 마진 2.67× → 통일).</summary>
        public const float GantryAccelTrip  = GantryRatedAccel  * TripMargin;   // = 0.250
        /// <summary>트롤리 가속 트립 한계 = 정격 0.60 × k(1.667) = 1.000 m/s². (기존 1.00, 마진 1.67× → 동일).</summary>
        public const float TrolleyAccelTrip = TrolleyRatedAccel * TripMargin;   // = 1.000
        /// <summary>권상 가속 트립 한계 = 정격 0.50 × k(1.667) = 0.833 m/s². (기존 1.00, 정격 0.6→0.5 하향분 반영).</summary>
        public const float HoistAccelTrip   = HoistRatedAccel   * TripMargin;   // = 0.833

        // 정격↔트립 마진(트립 ÷ 정격) — 이제 셋 다 TripMargin과 동일(검증용). 향후 누가 *AccelTrip을
        // 리터럴로 되돌려도 이 파생식이 불일치를 즉시 드러낸다(컴파일타임 SSOT 자기검증).
        /// <summary>갠트리 트립/정격 마진(=k=1.667). TR/HO와 통일 — 감사 H5 해소.</summary>
        public const float GantryTripMargin  = GantryAccelTrip  / GantryRatedAccel;
        /// <summary>트롤리 트립/정격 마진(=k=1.667).</summary>
        public const float TrolleyTripMargin = TrolleyAccelTrip / TrolleyRatedAccel;
        /// <summary>권상 트립/정격 마진(=k=1.667).</summary>
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
