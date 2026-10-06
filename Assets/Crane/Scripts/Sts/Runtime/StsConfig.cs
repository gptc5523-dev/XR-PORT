namespace AIXRCrane.Crane.Sts
{
    /// <summary>STS 크레인 전역 환산 상수 SSOT. 모델 단위 = 실척 m × ModelScale.
    /// ★ StsCraneCreator 의 빌드 스케일과 반드시 동일해야 한다.</summary>
    public static class StsConfig
    {
        /// <summary>실척 m × ModelScale = 모델 단위. STS 56m급 미니어처 환산비(1/24).</summary>
        public const float ModelScale = 1f / 24f;

        /// <summary>ModelScale 의 역수 — 모델 단위 × InvModelScale = 실척 m.</summary>
        public const float InvModelScale = 24f;

        // 크레인/부두 공유 치수(SSOT) — StsCraneCreator·QuayPartsPlacer·GantryRangeFitMenu 가 읽는다.

        /// <summary>레일 게이지(X, 육지/바다 두 주행레일 간격) — 실척 m.
        /// 부두 레일(QuayPartsPlacer)도 이 값을 따라 다리 접지가 유지된다.</summary>
        public const float LegGaugeXMeters = 18f;

        /// <summary>갠트리 베이스(주행방향 Z, 다리행 간격) — 실척 m. ※레일 게이지 아님.</summary>
        public const float GantryBaseZMeters = 16f;

        /// <summary>보기(bogie) 4륜 균등 피치 p — 모델 단위.</summary>
        public const float BogieEqualizerPitch = 0.032f;

        /// <summary>보기 전장(Z) 산출 휠 스팬 배율 — 전장 = pitch × factor.</summary>
        public const float BogieWheelSpanFactor = 3.25f;

        /// <summary>보기 전장(Z) — 모델 단위. 4륜 스팬 + 마진.</summary>
        public const float BogieLengthZ = BogieEqualizerPitch * BogieWheelSpanFactor;

        /// <summary>주행 레일 단면 폭(X) — 모델 단위. 크레인 짧은 레일·부두 고정 레일 공통.</summary>
        public const float RailSectionW = 0.02f;

        /// <summary>주행 레일 단면 높이(Y) — 모델 단위. 크레인 짧은 레일·부두 고정 레일 공통.</summary>
        public const float RailSectionH = 0.008f;

        // ── 부두 수직 단면 SSOT ── 데크 y=0(크레인 접지·컨테이너 착지 기준) 고정, 수면을 그 아래로 내린다.
        //   소비부: QuayPartsPlacer(슬래브 두께·바다 높이), ShipBerthMenu(흘수선 = 수면).

        /// <summary>데크(안벽 윗면 y=0) ↔ 수면 높이차 = 건현/코핑고 — 실척 m.</summary>
        public const float QuayDeckAboveSeaMeters = 4f;

        // 비활성: BerthWaterDepthMeters·QuayWallHeightMeters·QuayWallThickness(→ PortConfig 로 이전) — git 이력 참조

        /// <summary>수면 월드 Y — 모델 단위(데크 y=0 기준 아래). 바다 메시·배 흘수선의 공통 기준.</summary>
        public const float SeaLevelY = -QuayDeckAboveSeaMeters * ModelScale;

    }
}
