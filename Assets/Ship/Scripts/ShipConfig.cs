using Container.Crane.Sts;   // StsConfig.ModelScale (미니어처 환산비 SSOT 공유)
using ContainerProject;      // ProceduralContainerMesh.StdWidth (컨테이너 폭 SSOT 공유)

namespace Container.Ship
{
    /// <summary>
    /// 컨테이너선(Post-Panamax급) 전역 치수의 단일 출처(SSOT).
    ///
    /// ★ 모든 값은 실척(미터). 절차 메시는 실척으로 빌드한 뒤 마지막에 ModelScale(=1/24)로 축소한다
    ///   (크레인·컨테이너와 동일 환산비 — StsConfig.ModelScale 공유, [[reference_unity_package_versions]]와 무관).
    ///
    /// ── 치수 근거 (수학팀 검산 · 추측 금지 방침) ─────────────────────────────────
    ///   Post-Panamax 컨테이너선 표준 비율로 역산. STS 아웃리치(45m) > 선폭(40m) 이라
    ///   크레인이 선박 전폭을 덮는다(실물 정합, [[feedback_match_real_world_reference]]).
    ///
    ///   | 비율        | 실제 정상범위 | 본 선박                    |
    ///   |-------------|--------------|---------------------------|
    ///   | L/B         | 6.5 ~ 8.0    | 294/39.53 = 7.44 ✓        |
    ///   | B/D         | 1.5 ~ 1.9    | 39.53/24  = 1.65 ✓        |
    ///   | L/D         | 11 ~ 13      | 294/24 = 12.25 ✓          |
    ///   | 건현/흘수    | —            | 11/13 (건현 11m)           |
    ///   ※ 선폭은 컨테이너 15열 역산값(39.53m, 아래 DeckRows 참조).
    ///
    ///   ※ 흘수선 y=0 기준: 킬 바닥 = −Draft, 주갑판 = +Freeboard. 선체 길이방향 = +Z(선수),
    ///     선폭 = ±X, 상방 = +Y. 미드십(z=0)·중심선(x=0)·흘수선(y=0)이 메시 피벗.
    /// </summary>
    public static class ShipConfig
    {
        /// <summary>컨테이너선 루트 오브젝트 이름 — 생성부(ShipCreator)·접안부(ShipBerthMenu)·
        /// 부두 재생성(StsQuayGroundCreator)이 공유하는 SSOT. 각자 문자열을 박아두면 재접안이 조용히 끊긴다.</summary>
        public const string ShipRootName = "ContainerShip";

        /// <summary>실척 m × ModelScale = 모델 단위. 크레인·컨테이너와 동일한 1/24 미니어처 환산비.</summary>
        public const float ModelScale = StsConfig.ModelScale;

        /// <summary>전장 LOA(길이) — 실척 m.</summary>
        public const float LoaMeters = 294f;

        // ── 선폭(Beam)은 임의값이 아니라 컨테이너 적재 열수에서 역산(오너 지시 2026-06-19) ──
        //   크레인에 있는 그 컨테이너(폭 = ProceduralContainerMesh.StdWidth)를 그대로 적층하므로,
        //   선폭 = 열수×컨테이너폭 + (열수−1)×라싱간격 + 양현 사이드데크.
        //   크레인 아웃리치 45m 고정 → 15열(39.53m)이 전폭을 안전하게 커버([[feedback_calculate_before_placing]]).

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

        /// <summary>갑판 적재 최대 단수. 오너 지시 2026-09-07 "배에 컨테이너 최대 2단으로" →
        /// 재차 "그냥 전부 한 줄로 깔자" → 1단(쌓지 않음).
        /// 선미→선수 계단 램프의 가장 높은 단이다. 1 이면 램프가 평탄해져 전 구간 1단.</summary>
        public const int DeckMaxTiers = 1;

        /// <summary>갑판에 실을 컨테이너 수. <b>0 이면 슬롯 전부</b>.
        /// 오너 지시 2026-09-07 "컨테이너선에 컨테이너 첫 줄 전부 채우라고" → 0(전부).
        /// 1단(DeckMaxTiers=1)이라 슬롯 = 열 = 193개.
        /// 정밀 FBX(1개 110,134 삼각형)를 쓰므로 대수가 곧 렌더 예산이다 — 193개 ≈ 2,130만 삼각형.
        /// 줄이려면 여기에 원하는 개수를 넣으면 갑판 전체에 고르게 분산된다.</summary>
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
