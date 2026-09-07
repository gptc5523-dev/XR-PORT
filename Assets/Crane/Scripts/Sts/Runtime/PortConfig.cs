using UnityEngine;
using Container.Ship;
using ContainerProject;

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

        /// <summary>바다 폭 배수 — 바다 폭 = 에이프런 폭 × 이 값. 오너 지시 2026-09-07
        /// "부두 기준으로 바다 넓이를 줄여줘". 부두를 앵커로 쓰므로 에이프런이 넓어지면 바다도 넓어진다.</summary>
        public const float SeaApronRatio = 2f;

        /// <summary>바다 폭(안벽 전면 → 바다쪽) — 실척 m. Ceil(30 × 2 / 10) × 10 = 60m.
        /// ★ 하한 — 접안한 배(선폭 39.53m)가 물 밖으로 나가면 안 된다. SeaFitsShip 이 검사한다.</summary>
        public static float SeaWidthMeters =>
            Mathf.Ceil(ApronWidthMeters * SeaApronRatio / 10f) * 10f;

        /// <summary>바다 길이(안벽 방향) — 실척 m. 선석 길이와 같다. 340m.
        /// 오너 지시 2026-09-07 "바다 길이를 부두랑 똑같이".
        /// 케이슨 실제 끝은 줄눈 때문에 ±169.985m 라, 바다 끝(±170)이 15mm 앞서 나가
        /// 두 끝면이 겹치지 않는다 → Z-fighting 없음.</summary>
        public static float SeaLengthMeters => BerthLengthMeters;

        // ── 비활성 2026-09-07 (길이를 선석과 동일하게) ──
        //   선석 양끝으로도 바다폭만큼 더 뻗던 산식. 340 + 60×2 = 460m.
        // public static float SeaLengthMeters => BerthLengthMeters + 2f * SeaWidthMeters;

        /// <summary>접안한 배가 수면 안에 들어오나 — 바다 폭 &gt; 선폭, 바다 길이 &gt; 설계선 LOA.
        /// 바다를 줄이다 보면 배가 물 밖으로 삐져나간다. 줄이기 전에 이걸 본다.</summary>
        public static bool SeaFitsShip =>
            SeaWidthMeters > ShipConfig.BeamMeters && SeaLengthMeters > ShipConfig.LoaMeters;

        // ── 비활성 2026-09-07 (앵커를 선폭 → 부두로 교체) ──
        // /// <summary>바다 폭 = 설계선 선폭 × 이 값. 39.53×3 → 120m.</summary>
        // public const float SeaMarginRatio = 3f;

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

        // ═══════════ 에이프런 노면 표시 ═══════════
        /// <summary>안전 차선 — 레일 중심에서 차선 중심까지(양옆) 실척 m. 갠트리 접근 금지선.</summary>
        public const float LaneOffsetM = 1.08f;
        /// <summary>안전 차선 폭 — 실척 m.</summary>
        public const float LaneWidthM = 0.34f;
        //   레일 사이(트럭 주행 구역)에는 차선을 넣지 않는다 — 오너 지시 2026-09-07
        //   "레인 안쪽은 차선이 없어도 됨".

        // ═══════════ 야드 (오너 확정 2026-09-07: 2레인 × 2블록 = 4블록) ═══════════
        // ① 컨테이너 앵커 — ISO 1AA 40ft. 폭은 ProceduralContainerMesh.StdWidth SSOT 추종.
        /// <summary>40ft 컨테이너 길이 — 실척 m.</summary>
        public const float ContainerLenM = 12.192f;
        /// <summary>열 간 간격 — 실척 m. RTG 야드 표준.</summary>
        public const float RowGapM = 0.4f;
        /// <summary>베이 간 간격 — 실척 m.</summary>
        public const float BayGapM = 0.6f;
        /// <summary>열 피치 = 컨테이너 폭 + 열간격. 2.438 + 0.4 = 2.838m.</summary>
        public static float RowPitchM => ProceduralContainerMesh.StdWidth + RowGapM;
        /// <summary>베이 피치 = 컨테이너 길이 + 베이간격. 12.192 + 0.6 = 12.792m.</summary>
        public static float BayPitchM => ContainerLenM + BayGapM;

        // ② RTG 앵커 — RtgCraneCreator.SpanX 미러. RtgCraneCreator 는 Editor 라 Runtime 에서
        //    참조할 수 없어 값을 복사한다. 바꿀 땐 양쪽을 같이 고칠 것.
        /// <summary>RTG 다리 중심 간격 — 실척 m.</summary>
        public const float RtgSpanM = 23.6f;
        /// <summary>RTG 다리 박스 단면 한 변 — 실척 m. RtgCraneCreator.LegSec 미러.</summary>
        public const float RtgLegSectionM = 0.9f;
        /// <summary>다리 '안쪽면' 사이 순간격 — 실척 m. 23.6 − 0.9 = 22.7m.
        /// ★ 스팬은 다리 중심 간격이라 그대로 쓰면 블록·차선이 다리 밑으로 들어간다
        /// (오너 지적 2026-09-07 "트럭 라인 배치가 오류"). 실제 쓸 수 있는 폭은 이것이다.</summary>
        public static float RtgClearSpanM => RtgSpanM - RtgLegSectionM;

        /// <summary>RTG 구조물이 다리 중심선 '바깥'으로 튀어나오는 폭 — 실척 m.
        /// 주거더 오버행(0.75)·실빔·보기가 다리보다 바깥에 있다.
        /// 씬 실측: 전폭 27.25m → 편측 13.83m, 스팬 반(11.8) 을 빼면 2.03m. 여유 포함 2.5.
        /// ★ 스팬(다리 중심 간격)만 보고 야드를 재면 이만큼이 포장 밖으로 나간다
        /// (오너 지적 2026-09-07: RTG 다리가 포장을 0.53m 벗어나 허공에 떴다).</summary>
        public const float RtgOverhangM = 2.5f;

        // ③ 블록 구성
        /// <summary>블록 1개의 컨테이너 열수 — RTG 스팬 방향.</summary>
        public const int YardRows = 6;
        /// <summary>적재 단수.</summary>
        public const int YardTiers = 4;
        /// <summary>X(안벽 수직) 방향 레인 수 — 레인 1개 = RTG 주행로 1줄.
        /// 이 값은 '야드 밴드 깊이'를 정한다(포장 크기). 실제로 블록을 까는 수는 YardActiveLanes.</summary>
        public const int YardLanes = 2;

        /// <summary>실제로 블록·트럭레인을 까는 레인 수. 오너 지시 2026-09-07
        /// "크레인 없는 쪽 야드 부분 지우고 크레인 있는 쪽을 안쪽으로 이동".
        /// 깊이는 YardLanes 로 유지하고 사용만 줄인다 — 에이프런 뒤가 빈 공간으로 남아
        /// 나중에 레인을 되살릴 자리가 보존된다. YardLanes 로 올리면 원상 복구.</summary>
        public const int YardActiveLanes = 1;

        /// <summary>블록을 깔기 시작할 레인 인덱스 — 육지쪽부터 채운다. 2 − 1 = 1.</summary>
        public static int YardLaneStart => YardLanes - YardActiveLanes;
        /// <summary>레인당 블록 수 — Z(안벽 평행) 방향.</summary>
        public const int YardBlocksPerLane = 2;

        // ④ 통로
        /// <summary>레인 간 X 통로 — 실척 m.
        /// ponytail: 인접 레인을 둘 다 쓰면 RTG 오버행(2.5m)끼리 부딪힌다(필요 5m+).
        ///   지금은 YardActiveLanes=1 이라 안 물린다. 두 레인을 동시에 쓸 때 이 값을 키울 것.</summary>
        public const float YardAisleXM = 1.5f;

        /// <summary>야드 밴드 양 끝 여유 — 실척 m. RTG 가 포장 밖으로 나가면 안 되므로
        /// 통로와 오버행 중 큰 값. 종전엔 통로(1.5m)만 써서 0.53m 가 모자랐다.</summary>
        public static float YardEndMarginM => Mathf.Max(YardAisleXM, RtgOverhangM);
        /// <summary>블록 간 Z 횡단로 — 실척 m. 소방·정비 통로.</summary>
        public const float YardCrossAisleM = 16f;
        /// <summary>선석 끝 ↔ 블록 끝 최소 여유 — 실척 m.</summary>
        public const float YardEndM = 8f;

        // ⑤ 유도값 — 손대지 말 것
        /// <summary>블록 폭(열 방향) — 실척 m. 6 × 2.838 = 17.03m.</summary>
        public static float YardBlockWidthM => YardRows * RowPitchM;

        /// <summary>블록 1개의 베이 수 — 안벽 길이에서 양끝 여유와 횡단로를 빼고 베이 피치로 나눈 내림.
        /// (340 − 8×2 − 16) / 2 / 12.792 = 12베이.</summary>
        public static int YardBays => Mathf.FloorToInt(
            (BerthLengthMeters - 2f * YardEndM - (YardBlocksPerLane - 1) * YardCrossAisleM)
            / YardBlocksPerLane / BayPitchM);

        /// <summary>블록 길이(베이 방향) — 실척 m. 12 × 12.792 = 153.5m.</summary>
        public static float YardBlockLengthM => YardBays * BayPitchM;

        /// <summary>야드 밴드 깊이 — 실척 m. 레인 n개 + 레인간 통로 (n−1)개 + 양 끝 여유 2개.
        /// 2×23.6 + 1×1.5 + 2×2.5 = 53.7m. 끝 여유는 RTG 오버행을 덮어야 한다.</summary>
        public static float YardDepthM =>
            YardLanes * RtgSpanM + (YardLanes - 1) * YardAisleXM + 2f * YardEndMarginM;

        /// <summary>야드 장치능력 — TEU. 40ft = 2 TEU. 사용 레인만 센다.
        /// 6열 × 12베이 × 4단 × (1레인 × 2블록) = 576개 = 1,152 TEU.</summary>
        public static int YardCapacityTeu =>
            YardRows * YardBays * YardTiers * YardActiveLanes * YardBlocksPerLane * 2;

        /// <summary>블록 ↔ RTG 다리 안쪽면 여유(편측) — 실척 m. (22.7 − 17.03)/2 = 2.84m.
        /// 블록이 스팬 중앙이므로 양쪽에 이만큼씩 남는다.</summary>
        public static float YardBlockLegClearanceM => (RtgClearSpanM - YardBlockWidthM) * 0.5f;

        // ── 비활성 2026-09-07 (오너 선택: RTG 를 블록 중앙으로 되돌림) ──────────────
        //   블록을 스팬 한쪽으로 몰고 반대쪽에 5.67m 트럭레인을 두는 실물 RTG 방식이었으나,
        //   RTG 가 블록을 비대칭으로 감싸 어색해 보인다는 판단. 블록은 스팬 중앙으로.
        //   되살리려면 아래 주석을 풀고 YardBlockCenterX 를 한쪽으로 미는 식으로 되돌린다.
        //
        // /// <summary>트럭 주행레인 폭 = 다리 순간격 − 블록 폭 = 22.7 − 17.03 = 5.67m.</summary>
        // public static float YardTruckLaneWidthM => RtgClearSpanM - YardBlockWidthM;
        //
        // /// <summary>트럭 주행레인 중심 X — 스팬 안에서 해측.</summary>
        // public static float YardTruckLaneCenterX(int i) =>
        //     YardSpanCenterX(i) + RtgClearSpanM * 0.5f - YardTruckLaneWidthM * 0.5f;
        //
        // /// <summary>블록 중심 → RTG 스팬 중심 오프셋. 블록이 중앙이면 0 이라 불필요.</summary>
        // public static float YardRtgOffsetFromBlockM => (RtgClearSpanM - YardBlockWidthM) * 0.5f;
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>레인 i(0부터, 안벽에서 먼 순) 의 RTG 스팬 중심 X — 실척 m(음수, 육지쪽).
        /// 에이프런 끝에서 통로 하나 지나고 스팬 반개. RTG 는 여기에 선다.</summary>
        public static float YardSpanCenterX(int i) =>
            -(ApronWidthMeters + YardEndMarginM + RtgSpanM * (i + 0.5f) + YardAisleXM * i);

        /// <summary>블록 중심 X = RTG 스팬 중심. 오너 선택 2026-09-07 —
        /// RTG 가 블록을 대칭으로 감싸는 쪽을 택했다(트럭레인은 포기).
        /// 양옆에 YardBlockLegClearanceM(2.84m) 씩 남는다.</summary>
        public static float YardBlockCenterX(int i) => YardSpanCenterX(i);

        /// <summary>블록 j(0부터) 의 중심 Z — 실척 m. 안벽 중앙 기준 대칭.</summary>
        public static float YardBlockCenterZ(int j)
        {
            float run = YardBlocksPerLane * YardBlockLengthM
                      + (YardBlocksPerLane - 1) * YardCrossAisleM;
            return -run * 0.5f + YardBlockLengthM * 0.5f
                   + j * (YardBlockLengthM + YardCrossAisleM);
        }

        /// <summary>안벽 전면고(데크 → 해저) = 코핑고 + 계획수심 — 실척 m. 4 + 15 = 19m.
        /// 코핑고는 StsConfig.QuayDeckAboveSeaMeters SSOT 를 추종한다(수면 Y 와 같은 출처).</summary>
        public static float QuayWallHeightMeters =>
            StsConfig.QuayDeckAboveSeaMeters + WaterDepthMeters;
    }
}
