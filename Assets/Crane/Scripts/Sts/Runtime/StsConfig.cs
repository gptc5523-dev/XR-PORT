namespace Container.Crane.Sts
{
    /// <summary>
    /// STS 크레인 전역 환산 상수의 단일 출처(SSOT).
    ///
    /// ★ ModelScale — 실척(미터) ↔ 모델 단위 환산 계수.
    ///   - 정의:  모델 단위 = 실척 m × ModelScale,   실척 m = 모델 단위 ÷ ModelScale (= 모델 단위 × InvModelScale).
    ///   - 값 1/24: STS 크레인(붐 높이/아웃리치 등 50~56m급)을 컨테이너와 비례하는 미니어처로 축소한 환산비.
    ///     StsCraneCreator의 빌드 스케일(const Scale)과 반드시 동일해야 하며, 둘 다 이 상수를 참조한다.
    ///   - 런타임 어셈블리(Editor 폴더 아님)에 두어 에디터 생성기(Assembly-CSharp-Editor)도 자동 참조 가능.
    ///     이 프로젝트엔 asmdef가 없어 전부 기본 어셈블리(Assembly-CSharp/-Editor)로 컴파일되므로 추가 설정 불필요.
    ///   - const 이므로 다른 const(StsCraneCreator.Scale 등)·컴파일 상수 컨텍스트에서 그대로 참조 가능.
    /// </summary>
    public static class StsConfig
    {
        /// <summary>실척 m × ModelScale = 모델 단위. STS 56m급 → 미니어처 환산비(=1/24).</summary>
        public const float ModelScale = 1f / 24f;

        /// <summary>
        /// ModelScale의 역수 — 모델 단위 → 실척 m 환산(모델 단위 × InvModelScale = 실척 m).
        /// 수학적으로 1f / ModelScale = 1 / (1/24) = 24 와 정확히 동일(부동소수점 24f로 표현되는 값).
        /// </summary>
        public const float InvModelScale = 24f;

        // ── 크레인/부두 공유 치수 (SSOT, 외부 감사 H2) ─────────────────────────────
        //   StsCraneCreator(크레인 생성)·StsQuayGroundCreator(부두 레일)·GantryRangeFitMenu
        //   (주행반경 폴백)가 같은 값을 각자 하드코딩하던 것을 여기로 일원화. 한 곳만 바꾸면
        //   부두/갠트리 기하가 크레인과 어긋나던 구조를 제거한다. 값은 종전과 비트 단위 동일.

        /// <summary>레일 게이지(X, 육지/바다 두 주행레일 간격) — 실척 m. 모델 단위 = ×ModelScale.
        /// [외부감사 S3 증분2 2026-06-17] 15→18: Post-Panamax 표준 게이지. 아웃리치45/게이지18=2.50 ∈ 2.3~2.6 ✓.
        /// 부두 레일(StsQuayGroundCreator)도 이 SSOT를 추종하므로 다리 접지 유지.</summary>
        public const float LegGaugeXMeters = 18f;

        /// <summary>갠트리 베이스(주행방향 Z, 다리행 간격) — 실척 m. ※레일 게이지 아님. 모델 단위 = ×ModelScale.</summary>
        public const float GantryBaseZMeters = 16f;

        /// <summary>보기(bogie) 4륜 균등 피치 p — 모델 단위.</summary>
        public const float BogieEqualizerPitch = 0.032f;

        /// <summary>보기 전장(Z) 산출 휠 스팬 배율 — 전장 = pitch × factor.</summary>
        public const float BogieWheelSpanFactor = 3.25f;

        /// <summary>보기 전장(Z) — 모델 단위 (= pitch × factor ≈ 0.104). 4륜 스팬 0.096 + 마진.</summary>
        public const float BogieLengthZ = BogieEqualizerPitch * BogieWheelSpanFactor;

        /// <summary>주행 레일 단면 폭(X) — 모델 단위. 크레인 짧은 레일·부두 고정 레일 공통.</summary>
        public const float RailSectionW = 0.02f;

        /// <summary>주행 레일 단면 높이(Y) — 모델 단위. 크레인 짧은 레일·부두 고정 레일 공통.</summary>
        public const float RailSectionH = 0.008f;
    }
}
