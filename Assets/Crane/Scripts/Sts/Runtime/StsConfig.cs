namespace AIXRCrane.Crane.Sts
{
    /// <summary>STS 크레인 전역 환산 상수의 단일 출처(SSOT). ModelScale = 실척(m) ↔ 모델 단위 환산 계수
    /// (모델 단위 = 실척 m × ModelScale). StsCraneCreator의 빌드 스케일과 반드시 동일해야 한다.</summary>
    public static class StsConfig
    {
        /// <summary>실척 m × ModelScale = 모델 단위. STS 56m급 → 미니어처 환산비(=1/24).</summary>
        public const float ModelScale = 1f / 24f;

        /// <summary>ModelScale의 역수 — 모델 단위 → 실척 m 환산(모델 단위 × InvModelScale = 실척 m).</summary>
        public const float InvModelScale = 24f;

        // 크레인/부두 공유 치수(SSOT) — StsCraneCreator·QuayPartsPlacer·GantryRangeFitMenu 가 읽는다.

        /// <summary>레일 게이지(X, 육지/바다 두 주행레일 간격) — 실척 m(모델 단위 = ×ModelScale). 옛값 15f.
        /// 부두 레일(QuayPartsPlacer)도 이 값을 따라 다리 접지가 유지된다.</summary>
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

        // ── 부두 수직 단면(데크 · 수면 · 안벽 깊이) SSOT ──────────────────────────────
        //   데크 y=0(크레인 접지·VirtualFloor·컨테이너 착지 공통 기준)은 고정, 수면을 데크 아래로 내려 안벽 단면을 만든다.
        //   실물 레퍼런스(포스트파나막스급): 코핑 +4.0m · 계획수심 −16~−17m · 안벽 높이 21m.
        //   소비부: QuayPartsPlacer(슬래브 두께·바다 높이), ShipBerthMenu(흘수선 = 수면).

        /// <summary>데크(안벽 윗면 y=0) ↔ 수면 높이차 = 건현/코핑고 — 실척 m.</summary>
        public const float QuayDeckAboveSeaMeters = 4f;

        // ── 비활성(항구 사이즈 재계산 대기 — 주석처리) ──────────────
        //   새 값이 정해지면 아래 주석을 풀고 숫자만 교체한다. 소비처는 0곳.
        //
        // /// <summary>선석 계획수심(수면 → 해저) — 실척 m. 배 흘수 13 m + 여유수심 4 m.</summary>
        // public const float BerthWaterDepthMeters = 17f;
        //
        // /// <summary>안벽 전면 높이(데크 → 해저) = 코핑고 + 계획수심 — 실척 m. 슬래브 두께 SSOT.</summary>
        // public const float QuayWallHeightMeters = QuayDeckAboveSeaMeters + BerthWaterDepthMeters;
        //
        // /// <summary>안벽 슬래브 두께(데크 윗면 y=0 → 해저) — 모델 단위.</summary>
        // public const float QuayWallThickness = QuayWallHeightMeters * ModelScale;
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>수면 월드 Y — 모델 단위(데크 y=0 기준 아래). 바다 메시·배 흘수선의 공통 기준.</summary>
        public const float SeaLevelY = -QuayDeckAboveSeaMeters * ModelScale;

    }
}
