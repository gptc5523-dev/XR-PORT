using UnityEngine;
using Container.Ship;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 항구 치수 SSOT — 오너 확정 2026-09-07 "파나마스급 1선석".
    ///
    /// 여기 앵커(설계선·여유율)만 바꾸면 안벽 길이·에이프런·수심·안벽고가 한꺼번에 따라온다.
    /// 안벽 길이를 다른 파일에 절대 박지 말 것 — 연석·계선주·레일·케이슨이 전부 이걸 읽는다.
    ///
    /// ※ StsConfig 가 아니라 별도 파일인 이유: StsConfig 에 두면 ShipConfig.LoaMeters 를 참조해야 하는데
    ///   ShipConfig 가 이미 StsConfig.ModelScale 을 참조하고 있어 const 순환 참조가 된다.
    ///   도메인도 다르다 — StsConfig=크레인 제원, ShipConfig=선박 제원, PortConfig=항구 치수.
    /// </summary>
    public static class PortConfig
    {
        // ═══ ① 앵커 ═══
        /// <summary>선석 수. 파나마스급 1선석.</summary>
        public const int BerthCount = 1;
        //   설계선 = ShipConfig.LoaMeters(294m 파나마스) · ShipConfig.DraftMeters(13m)

        // ═══ ② 여유율 (PIANC/항만설계기준 관행) ═══
        /// <summary>선석 길이 여유율 — 안벽 길이 = 설계선 LOA × 이 값. 통상 1.10~1.15.</summary>
        public const float BerthLengthRatio = 1.15f;
        /// <summary>여유수심 배수 — 계획수심 = 만재흘수 × 이 값. 통상 1.10~1.15.</summary>
        public const float DepthAllowanceRatio = 1.15f;

        // ═══ ③ 에이프런 구성 (레일 게이지는 StsConfig SSOT 추종) ═══
        /// <summary>안벽 가장자리 → 바다측 주행레일 중심 — 실척 m.</summary>
        public const float ApronSeawardM = 4f;
        /// <summary>육지측 주행레일 중심 → 야드 경계 — 실척 m. 트럭 레인 확보.</summary>
        public const float ApronLandwardM = 8f;

        // ═══ ④ 유도값 — 여기부터는 손대지 말 것 ═══
        /// <summary>안벽(선석) 길이 — 실척 m. 294 × 1.15 = 338.1 → 10m 올림 = 340m.</summary>
        public static float BerthLengthMeters =>
            Mathf.Ceil(ShipConfig.LoaMeters * BerthLengthRatio / 10f) * 10f;

        /// <summary>에이프런 폭 — 실척 m. 해측여유 4 + 레일게이지 18 + 육측 8 = 30m.</summary>
        public static float ApronWidthMeters =>
            ApronSeawardM + StsConfig.LegGaugeXMeters + ApronLandwardM;

        /// <summary>계획수심(수면 → 해저) — 실척 m. 13 × 1.15 = 14.95 → 올림 15m.</summary>
        public static float WaterDepthMeters =>
            Mathf.Ceil(ShipConfig.DraftMeters * DepthAllowanceRatio);

        /// <summary>바다 여유 배수 — 바다 폭 = 설계선 선폭 × 이 값. 오너 선택 2026-09-07.
        /// 접안한 배 바깥으로 선폭 2척분의 물이 더 보이는 정도.</summary>
        public const float SeaMarginRatio = 3f;

        /// <summary>바다 폭(안벽 전면 → 바다쪽) — 실척 m. Ceil(39.53 × 3 / 10) × 10 = 120m.</summary>
        public static float SeaWidthMeters =>
            Mathf.Ceil(ShipConfig.BeamMeters * SeaMarginRatio / 10f) * 10f;

        /// <summary>바다 길이(안벽 방향) — 실척 m. 선석 + 양끝 바다폭. 340 + 120×2 = 580m.</summary>
        public static float SeaLengthMeters => BerthLengthMeters + 2f * SeaWidthMeters;

        // ── 비활성 2026-09-07 (오너 선택 B: 바다 축소) ────────────────────────────
        //   선회장 직경을 바다 앵커로 쓰면 588 × 1,516m 가 나와 부두(30 × 340m)가
        //   전체 폭의 4.9% 짜리 실선이 된다. 실척으로는 맞지만 확인이 안 된다.
        //   선회장 자체가 필요해지면(선회 시뮬레이션 등) 주석을 푼다.
        //
        // /// <summary>선회장 직경 — 자력 선회 기준 2 × LOA (항만설계기준). 294×2 = 588m.</summary>
        // public static float TurningBasinMeters => 2f * ShipConfig.LoaMeters;
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>바다가 안벽 안쪽으로 파고드는 깊이 — 실척 m. 수면 끝면과 안벽 전면이 x=0 에서
        /// 정확히 겹치면 Z-fighting 이 난다. 케이슨이 불투명하니 겹친 부분은 안 보인다.</summary>
        public const float SeaOverlapM = 1f;

        /// <summary>안벽 전면고(데크 → 해저) = 코핑고 + 계획수심 — 실척 m. 4 + 15 = 19m.
        /// 코핑고는 StsConfig.QuayDeckAboveSeaMeters SSOT 를 추종한다(수면 Y 와 같은 출처).</summary>
        public static float QuayWallHeightMeters =>
            StsConfig.QuayDeckAboveSeaMeters + WaterDepthMeters;
    }
}
