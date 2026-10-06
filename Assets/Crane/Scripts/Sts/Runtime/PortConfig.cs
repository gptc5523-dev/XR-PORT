using UnityEngine;
using AIXRCrane.Ship;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>항구 치수 SSOT. 앵커(설계선·여유율)만 바꾸면 안벽 길이·에이프런·수심·안벽고가 따라온다.
    /// 안벽 길이는 여기서만 정의 — StsConfig 와는 순환참조 회피로 분리.</summary>
    public static class PortConfig
    {
        // ═══ ① 앵커 ═══
        //   설계선 = ShipConfig.LoaMeters(294m) · ShipConfig.DraftMeters(13m)

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
        /// <summary>안벽(선석) 길이 — 실척 m. LOA × 여유율, 10m 올림(340m).</summary>
        public static float BerthLengthMeters =>
            Mathf.Ceil(ShipConfig.LoaMeters * BerthLengthRatio / 10f) * 10f;

        /// <summary>에이프런 폭 — 실척 m. 해측여유 + 레일게이지 + 육측여유(30m).</summary>
        public static float ApronWidthMeters =>
            ApronSeawardM + StsConfig.LegGaugeXMeters + ApronLandwardM;

        /// <summary>계획수심(수면 → 해저) — 실척 m. 흘수 × 여유, 올림(15m).</summary>
        public static float WaterDepthMeters =>
            Mathf.Ceil(ShipConfig.DraftMeters * DepthAllowanceRatio);

        /// <summary>바다 폭 배수 — 바다 폭 = 에이프런 폭 × 이 값.</summary>
        public const float SeaApronRatio = 2f;

        /// <summary>바다 폭(안벽 전면 → 바다쪽) — 실척 m(60m).
        /// ★ 하한 — 접안한 배가 물 밖으로 나가면 안 된다. SeaFitsShip 이 검사.</summary>
        public static float SeaWidthMeters =>
            Mathf.Ceil(ApronWidthMeters * SeaApronRatio / 10f) * 10f;

        /// <summary>바다 길이(안벽 방향) = 선석 길이. 바다 끝이 케이슨 끝보다 15mm 앞서 Z-fighting 없음.</summary>
        public static float SeaLengthMeters => BerthLengthMeters;

        // 비활성: 선석 양끝으로 바다폭만큼 더 뻗던 SeaLengthMeters 산식 — git 이력 참조

        /// <summary>접안한 배가 수면 안에 들어오나 — 바다 폭 &gt; 선폭, 바다 길이 &gt; LOA.
        /// 바다를 줄이기 전에 이걸 본다.</summary>
        public static bool SeaFitsShip =>
            SeaWidthMeters > ShipConfig.BeamMeters && SeaLengthMeters > ShipConfig.LoaMeters;

        // 비활성: 선폭 앵커 SeaMarginRatio, 선회장 직경 TurningBasinMeters — git 이력 참조

        /// <summary>바다가 안벽 안쪽으로 파고드는 깊이 — 실척 m. x=0 에서 수면·안벽이 딱 겹치면
        /// Z-fighting. 겹친 부분은 불투명 케이슨에 가려진다.</summary>
        public const float SeaOverlapM = 1f;

        // ═══════════ 에이프런 노면 표시 ═══════════
        /// <summary>안전 차선 — 레일 중심에서 차선 중심까지(양옆) 실척 m. 갠트리 접근 금지선.</summary>
        public const float LaneOffsetM = 1.08f;
        /// <summary>안전 차선 폭 — 실척 m.</summary>
        public const float LaneWidthM = 0.34f;
        //   레일 사이(트럭 주행 구역)에는 차선을 넣지 않는다.

        // ═══════════ 야드 (2레인 × 2블록) ═══ ① 컨테이너 앵커 — ISO 1AA 40ft
        /// <summary>40ft 컨테이너 길이 — 실척 m. 폭은 ProceduralContainerMesh.StdWidth SSOT.</summary>
        public const float ContainerLenM = ProceduralContainerMesh.Length40ft;
        /// <summary>열 간 간격 — 실척 m. RTG 야드 표준.</summary>
        public const float RowGapM = 0.4f;
        /// <summary>베이 간 간격 — 실척 m.</summary>
        public const float BayGapM = 0.6f;
        /// <summary>열 피치 = 컨테이너 폭 + 열간격.</summary>
        public static float RowPitchM => ProceduralContainerMesh.StdWidth + RowGapM;
        /// <summary>베이 피치 = 컨테이너 길이 + 베이간격.</summary>
        public static float BayPitchM => ContainerLenM + BayGapM;

        // ② RTG 앵커 — RtgCraneCreator(Editor) 미러. ★ 바꿀 땐 양쪽을 같이 고칠 것.
        /// <summary>RTG 다리 중심 간격 — 실척 m.</summary>
        public const float RtgSpanM = 23.6f;
        /// <summary>RTG 다리 박스 단면 한 변 — 실척 m. RtgCraneCreator.LegSec 미러.</summary>
        public const float RtgLegSectionM = 0.9f;
        /// <summary>다리 '안쪽면' 사이 순간격 — 실척 m.
        /// ★ 스팬(중심 간격)을 쓰면 블록·차선이 다리 밑으로 들어간다 — 쓸 수 있는 폭은 이것.</summary>
        public static float RtgClearSpanM => RtgSpanM - RtgLegSectionM;

        /// <summary>RTG 구조물이 다리 중심선 바깥으로 튀어나오는 폭 — 실척 m(씬 실측 2.03 + 여유).
        /// ★ 스팬만 보고 야드를 재면 이만큼 포장 밖으로 나간다.</summary>
        public const float RtgOverhangM = 2.5f;

        // ③ 블록 구성
        /// <summary>블록 1개의 컨테이너 열수 — RTG 스팬 방향.</summary>
        public const int YardRows = 6;
        /// <summary>적재 단수.</summary>
        public const int YardTiers = 4;
        /// <summary>X(안벽 수직) 방향 레인 수 — 레인 1개 = RTG 주행로 1줄.
        /// 야드 밴드 깊이(포장 크기)를 정한다. 실제 블록 수는 YardActiveLanes.</summary>
        public const int YardLanes = 2;

        /// <summary>실제로 블록·트럭레인을 까는 레인 수. 깊이는 YardLanes 로 유지,
        /// 남는 공간은 레인 복구 자리. YardLanes 로 올리면 원상 복구.</summary>
        public const int YardActiveLanes = 1;

        /// <summary>블록을 깔기 시작할 레인 인덱스 — 육지쪽부터 채운다.</summary>
        public static int YardLaneStart => YardLanes - YardActiveLanes;
        /// <summary>레인당 블록 수 — Z(안벽 평행) 방향.</summary>
        public const int YardBlocksPerLane = 2;

        // ④ 통로 — ponytail: 두 레인을 동시에 쓰면 RTG 오버행끼리 부딪힌다(필요 5m+). 그때 키울 것.
        /// <summary>레인 간 X 통로 — 실척 m.</summary>
        public const float YardAisleXM = 1.5f;

        /// <summary>야드 밴드 양 끝 여유 — 실척 m. RTG 가 포장 밖으로 안 나가게 통로·오버행 중 큰 값.</summary>
        public static float YardEndMarginM => Mathf.Max(YardAisleXM, RtgOverhangM);
        /// <summary>블록 간 Z 횡단로 — 실척 m. 소방·정비 통로.</summary>
        public const float YardCrossAisleM = 16f;
        /// <summary>선석 끝 ↔ 블록 끝 최소 여유 — 실척 m.</summary>
        public const float YardEndM = 8f;

        // ⑤ 유도값 — 손대지 말 것
        /// <summary>블록 폭(열 방향) — 실척 m.</summary>
        public static float YardBlockWidthM => YardRows * RowPitchM;

        /// <summary>블록 1개의 베이 수 — 안벽 길이에서 양끝 여유·횡단로를 빼고 베이 피치로 내림(12베이).</summary>
        public static int YardBays => Mathf.FloorToInt(
            (BerthLengthMeters - 2f * YardEndM - (YardBlocksPerLane - 1) * YardCrossAisleM)
            / YardBlocksPerLane / BayPitchM);

        /// <summary>블록 길이(베이 방향) — 실척 m.</summary>
        public static float YardBlockLengthM => YardBays * BayPitchM;

        /// <summary>야드 밴드 깊이 — 실척 m. 레인 n개 + 통로 (n−1)개 + 양 끝 여유 2개.
        /// 끝 여유는 RTG 오버행을 덮어야 한다.</summary>
        public static float YardDepthM =>
            YardLanes * RtgSpanM + (YardLanes - 1) * YardAisleXM + 2f * YardEndMarginM;

        /// <summary>야드 장치능력 — TEU(40ft = 2 TEU). 사용 레인만 센다.</summary>
        public static int YardCapacityTeu =>
            YardRows * YardBays * YardTiers * YardActiveLanes * YardBlocksPerLane * 2;

        /// <summary>블록 ↔ RTG 다리 안쪽면 여유(편측) — 실척 m. 블록이 스팬 중앙이라 양쪽 동일.</summary>
        public static float YardBlockLegClearanceM => (RtgClearSpanM - YardBlockWidthM) * 0.5f;

        // 비활성: 블록을 한쪽으로 몰고 트럭레인 두던 산식(YardTruckLane*, 비대칭이라 중단) — git 이력 참조

        /// <summary>레인 i(0부터, 안벽에서 먼 순) 의 RTG 스팬 중심 X — 실척 m(음수, 육지쪽).
        /// RTG 는 여기에 선다.</summary>
        public static float YardSpanCenterX(int i) =>
            -(ApronWidthMeters + YardEndMarginM + RtgSpanM * (i + 0.5f) + YardAisleXM * i);

        /// <summary>야드(블록·RTG)를 바다쪽으로 미는 양 — 실척 m. 포장은 그대로, RTG 는 블록 존을
        /// 읽어 자동 추종. 0 이면 레인 슬롯 그대로.</summary>
        public const float YardShiftSeawardM = 12f;

        /// <summary>바다쪽 이동 한계 — 실척 m. RTG 해측 끝(스팬반 + 오버행)이 에이프런 경계를 넘지 않는 값.</summary>
        public static float YardShiftMaxM =>
            -ApronWidthMeters - (YardSpanCenterX(YardLaneStart) + RtgSpanM * 0.5f + RtgOverhangM);

        /// <summary>실제 적용되는 이동량 — 한계로 클램프.</summary>
        public static float YardShiftAppliedM => Mathf.Clamp(YardShiftSeawardM, 0f, YardShiftMaxM);

        /// <summary>블록 중심 X = RTG 스팬 중심 + 바다쪽 이동. RTG 가 블록을 대칭으로 감싼다.</summary>
        public static float YardBlockCenterX(int i) => YardSpanCenterX(i) + YardShiftAppliedM;

        /// <summary>블록 j(0부터) 의 중심 Z — 실척 m. 안벽 중앙 기준 대칭.</summary>
        public static float YardBlockCenterZ(int j)
        {
            float run = YardBlocksPerLane * YardBlockLengthM
                      + (YardBlocksPerLane - 1) * YardCrossAisleM;
            return -run * 0.5f + YardBlockLengthM * 0.5f
                   + j * (YardBlockLengthM + YardCrossAisleM);
        }

        /// <summary>안벽 전면고(데크 → 해저) = 코핑고 + 계획수심 — 실척 m.
        /// 코핑고는 StsConfig.QuayDeckAboveSeaMeters SSOT(수면 Y 와 같은 출처).</summary>
        public static float QuayWallHeightMeters =>
            StsConfig.QuayDeckAboveSeaMeters + WaterDepthMeters;

        // ═══ 플레이어 시작점 ═══ 씬 PlayerStartPoint 마커가 없을 때 기본값 — 레일 사이·선석 중앙, +X 를 본다.
        /// <summary>시작점 X — 실척 m. −(해측여유 + 레일게이지 ÷ 2).</summary>
        public static float PlayerStartXMeters => -(ApronSeawardM + StsConfig.LegGaugeXMeters * 0.5f);
        /// <summary>시작점 Z — 실척 m. 선석(안벽) 중앙.</summary>
        public const float PlayerStartZMeters = 0f;
    }
}
