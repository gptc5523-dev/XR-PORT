using AIXRCrane.Crane.Sts;   // StsConfig.ModelScale (미니어처 환산비 SSOT 공유)

namespace AIXRCrane.Ship
{
    /// <summary>컨테이너선(Post-Panamax급) 전역 치수 SSOT. 값은 전부 실척(m), 절차 메시는 실척으로 빌드 후
    /// ModelScale(1/24)로 축소(크레인·컨테이너와 공유). 흘수선 y=0, 킬=−Draft, 주갑판=+Freeboard, 선수=+Z, 선폭=±X, 위=+Y.</summary>
    public static class ShipConfig
    {
        /// <summary>컨테이너선 루트 이름 — 생성부(ShipCreator)·접안부(ShipBerthMenu)가 공유한다.</summary>
        public const string ShipRootName = "ContainerShip";

        /// <summary>실척 m × ModelScale = 모델 단위. 크레인·컨테이너와 동일한 1/24 미니어처 환산비.</summary>
        public const float ModelScale = StsConfig.ModelScale;

        /// <summary>전장 LOA(길이) — 실척 m.</summary>
        public const float LoaMeters = 294f;

        // 선폭(Beam)은 임의값이 아니라 컨테이너 적재 열수에서 역산 — 크레인의 그 컨테이너 폭을 그대로 적층.
        //   선폭 = 열수×컨테이너폭 + (열수−1)×라싱간격 + 양현 사이드데크. 아웃리치 45m 고정 → 15열(39.53m)이 전폭 커버.

        /// <summary>갑판 컨테이너 적재 열수(횡방향). 선폭 역산의 입력.</summary>
        public const int DeckRows = 15;

        /// <summary>컨테이너 폭 — 크레인/야드와 동일 SSOT(ISO 668, 2.438m) 직접 참조.</summary>
        public const float ContainerWidthM = ProceduralContainerMesh.StdWidth;

        /// <summary>갑판 열간 라싱 간격 — 실척 m.</summary>
        public const float RowGapM = 0.04f;

        /// <summary>양현 사이드데크(라싱 통로) 폭 — 실척 m.</summary>
        public const float SideDeckM = 1.2f;

        /// <summary>형폭 Beam(선폭) — 실척 m. 컨테이너 15열에서 역산 = 39.53m. 아웃리치 45m가 전폭을 덮음.</summary>
        public const float BeamMeters =
            DeckRows * ContainerWidthM + (DeckRows - 1) * RowGapM + 2f * SideDeckM;   // = 39.53

        /// <summary>갑판 적재 최대 단수 — 선미→선수 계단 램프의 최고단(1이면 전 구간 평탄 1단).
        /// 윗단은 DeckSecondTierRatio 확률로 얹는다. 선수가 낮은 건 SOLAS V/22 선교 시야 요건 반영.</summary>
        public const int DeckMaxTiers = 2;

        /// <summary>윗단(2단째)을 얹을 확률 0~1. 아래 단은 항상 전부 채운다.
        /// 실제 배도 부분 적재 시 스택 높이가 들쭉날쭉하다. 1 이면 전부 2단, 0 이면 전부 1단.</summary>
        public const float DeckSecondTierRatio = 0.5f;

        /// <summary>윗단 무늬 시드 — 같은 값이면 같은 무늬가 재현된다. 0 이면 매번 다르다.</summary>
        public const int DeckStackSeed = 20260907;

        /// <summary>갑판에 실을 컨테이너 수. 0이면 슬롯 전부(슬롯=열=193개).
        /// 정밀 FBX 1개 110,134 삼각형이라 개수가 곧 렌더 예산(193개≈2,130만 삼각형) — 줄이려면 개수 지정.</summary>
        public const int DeckCargoCount = 0;

        /// <summary>형심 Depth(킬 바닥→주갑판) — 실척 m.</summary>
        public const float DepthMeters = 24f;

        /// <summary>설계 흘수 Draft(흘수선→킬 바닥) — 실척 m.</summary>
        public const float DraftMeters = 13f;

        /// <summary>건현 Freeboard(흘수선→주갑판) = Depth − Draft — 실척 m.</summary>
        public const float FreeboardMeters = DepthMeters - DraftMeters;   // 11

        // ── 종방향 마스터 레이아웃 (상부구조 전부가 공유하는 SSOT) — 실척 m, 미드십 z=0, 선수 +Z ──
        //   기관실 아프트(engine-aft) 배치 표준: 거주구·펀넬을 선미쪽, 화물구는 그 앞~선수루 뒤.

        /// <summary>전장 절반(미드십→선수/선미) — 실척 m.</summary>
        public const float HalfLoa = LoaMeters * 0.5f;   // 147

        /// <summary>거주구 전면(화물구 쪽) z — 실척 m.</summary>
        public const float AccomFrontZ = -92f;
        /// <summary>거주구 후면 z — 실척 m. 전후 길이 22m(폭28>길이22, 타워형).</summary>
        public const float AccomAftZ = -114f;
        /// <summary>거주구 갑판 층수. SOLAS 전방시야(브리지 눈높이 ≥ 주갑판+20m)에서 역산 — 9층(27m)+휠하우스=30m.</summary>
        public const int AccomDecks = 9;
        /// <summary>상부구조 한 층 높이 — 실척 m.</summary>
        public const float DeckHeightM = 3.0f;

        /// <summary>펀넬(거주구 후방, 근접) 중심 z — 실척 m.</summary>
        public const float FunnelZ = -118f;

        /// <summary>화물 구역(해치) 후단 z(거주구 전면 앞) — 실척 m.</summary>
        public const float CargoAftZ = -82f;
        /// <summary>화물 구역(해치) 전단 z(선수루 뒤) — 실척 m.</summary>
        public const float CargoFwdZ = 112f;

        /// <summary>선수루(forecastle) 후단 z — 실척 m. 길이=146-121.5≈24.5m=0.083·LOA(표준 0.06~0.10).</summary>
        public const float ForecastleAftZ = 121.5f;
        /// <summary>선수루 상승 높이(주갑판 위) — 실척 m.</summary>
        public const float ForecastleRaiseM = 2.4f;
        /// <summary>불워크/방파판 높이 — 실척 m.</summary>
        public const float BulwarkHeightM = 2.0f;
    }
}
