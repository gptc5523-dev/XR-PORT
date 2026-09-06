#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// STS 크레인이 서는 부두(quay) 아스팔트 바닥을 절차적으로 생성.
    ///
    /// - 슬래브 윗면 = y 0 (VirtualFloor 윗면·크레인 접지면과 동일 평면).
    /// - 아스팔트 텍스처는 절차 생성: 저주파 얼룩(시일코트) + 골재 그레인 + 드문드문 밝은 골재.
    ///   매트(거친) PBR로 빛 반사 최소.
    /// - 씬에 STS_Crane 이 있으면 그 XZ 바운즈에 여유를 둬 자동으로 크기·위치를 맞추고,
    ///   레일(Rail_*)을 찾아 양옆에 노란 안전 차선을 그린다. 없으면 원점에 기본 크기로 생성.
    /// - 슬래브가 바닥 콜라이더(BoxCollider 1개)를 직접 갖는다 = 걷는 면·컨테이너 착지면.
    ///   안벽 밖에는 바닥이 없다. 종전 VirtualFloor(100×100u ≈ 2,400×2,400m)는 보이는 아스팔트의
    ///   153배를 밟히게 해서 바다 위·허공을 걸을 수 있었다 → CreateFromMenu 가 제거한다.
    ///
    /// 생성 머티리얼/텍스처는 인스턴스(에셋 미저장)라 프로젝트 머티리얼을 오염시키지 않는다.
    /// </summary>
    public static class StsQuayGroundCreator
    {
        const string RootName  = StsPartNames.QuayGround;
        const string CraneName = "STS_Crane";
        // 계선주 — 블렌더 제작 FBX. RTG_Crane.fbx 와 같은 관례: 실척 m 로 내보내고 유니티에서 측정·보정한다.
        //   없으면 아래 Cyl() 절차생성으로 조용히 폴백하므로, FBX 임포트 전에도 부두 생성이 깨지지 않는다.
        const string BollardFbx        = "Assets/Crane/Models/Quay_Bollard.fbx";
        const float  RealBollardHeightM = 1.368f;   // Blender 실측 총높이(기둥 1.08 + 갓 0.288)
        // 연석 — 블렌더 제작 4m 프리캐스트 유닛. 통짜 압출이 아니라 유닛을 깔아 줄눈이 보이게 한다.
        const string CurbFbx           = "Assets/Crane/Models/Quay_Curb.fbx";
        const float  RealCurbHeightM   = 0.528f;    // = curbH(0.022u) × InvModelScale. 단면 0.72W × 0.528H
        const float  CurbUnitPitchM    = 4.0f;      // 유닛 피치 = 유닛 실길이 3.985m + 줄눈 0.015m

        // 기본 치수(크레인을 못 찾을 때) — X=apron(붐 방향), Z=안벽 길이(레일 방향), 모델 단위
        //   ★ 폴백도 크레인이 있을 때와 '같은 산식'을 쓴다. 종전 X 12u(288m)·Z 20u(480m)는 별도 매직넘버라
        //     크레인 없이 생성하면 실제 배치(94m×384m)와 3배 어긋난 허허벌판이 나왔다.
        const float DefaultSizeX = ApronDepth + SlabSeawardMargin;   // = 3.905u(≈94m) — 크레인 있을 때와 동일
        const float DefaultSizeZ = QuaySlabZ;                        // = 16.0u(384m) — 선석에서 유도(아래)
        // X(붐 방향) 여유 — 레일(Rail_*)을 못 찾은 폴백에서만 쓴다. 2.0u(≈48m 사방)는 붐 아웃리치까지 감싸
        //   슬래브를 161m로 부풀렸다 → 0.5u(12m). 이러면 폴백도 DefaultSizeX(94m)로 수렴해 정상 경로와 같아진다.
        const float MarginX      = 0.5f;
        const float MarginZ      = 0.8f;    // Z(레일 방향) 여유 — 원래대로
        // ═══ 터미널 레이아웃 수식 (실척 m) — 모든 야드·에이프런 치수를 앵커에서 유도한다. 매직넘버 금지 ═══
        //   오너 지시 2026-08-10 "수식을 사용해서 항구를 만들어줘". 앵커는 ① 컨테이너 규격 ② RTG 리그 실치수
        //   ③ 배·선석. 아래 유도값만 바꾸면 도로·야드·에이프런·슬래브가 한꺼번에 따라온다.
        // ① 컨테이너 앵커 (ISO 1AA 40ft)
        const float ContLenM   = 12.192f;                 // 40ft 길이
        const float ContWidM   = 2.438f;                  // 폭(8ft)
        const float RowGapM    = 0.4f;                    // 열 간 간격(RTG 야드 표준)
        const float BayGapM    = 0.6f;                    // 베이 간 간격
        const float RowPitchM  = ContWidM + RowGapM;      // 2.838 — 열 피치
        const float BayPitchM  = ContLenM + BayGapM;      // 12.792 — 베이 피치
        // ② RTG 리그 앵커 (RtgCraneCreator.SpanX/BaseZ SSOT 미러)
        const float RtgSpanM   = 23.6f;                   // 다리 중심 간격 = 컨테이너 6열 + 트럭레인
        const int   YardRows   = 6;                       // 블록 1개의 컨테이너 열수(RTG 스팬 방향)
        const int   YardTiers  = 4;                       // 적재 단수(장치능력 산출용)
        // ③ 블록 배열 — RTG 주행축이 안벽과 '평행'(오너 지시 "RTG 90도 돌려줘"). 실물 RTG 터미널 배치.
        const int   YardColsX   = 2;                      // X(안벽 수직) 방향 블록 열수
        const int   YardColsZ   = 2;                      // Z(안벽 평행) 방향 블록 열수
        const float YardAisleXM = 1.5f;                   // 블록 간 X 통로(인접 RTG 스팬 간섭 방지)
        const float YardAisleZM = 16f;                    // 블록 간 Z 통로(소방·정비 횡단도로)
        const float YardEndZM   = 8f;                     // 선석 끝 ↔ 블록 끝 여유
        // ④ 유도값 — 야드 밴드 깊이 = 스팬 n개 + 통로 (n+1)개
        const float YardBandM   = YardColsX * RtgSpanM + (YardColsX + 1) * YardAisleXM;   // 51.7 m
        // 에이프런 = 고정 단면(코핑·게이지·간선·가로등) + 야드 밴드. 종전 3.9u(94m)는 매직넘버였다.
        const float QuayEdgeMarginM = 4f;                                   // 바다측 레일 → 안벽 가장자리
        const float ArterialM       = 2f * RoadLaneWidthM;                  // 간선 2차로 = 8 m
        const float RailClearM      = (LaneOffset + 0.02f) * StsConfig.InvModelScale;  // 레일 ↔ 간선 여유 1.56 m
        const float LandStripM      = 6.5f;                                 // 육지 끝 연석 0.5 + 가로등 스트립 6
        const float ApronFixedM     = QuayEdgeMarginM + StsConfig.LegGaugeXMeters + RailClearM
                                    + ArterialM + ArterialM + LandStripM;   // 46.06 m
        const float ApronDepthM     = ApronFixedM + YardBandM;              // 97.76 m
        const float ApronDepth      = ApronDepthM * StsConfig.ModelScale;   // 4.073u — 안벽 가장자리→육지측 깊이
        // 슬래브 바다측 끝 = 바다 시작선(seaInner)과 일치. 종전 0.25u(≈6m)는 슬래브가 바다면(y≈0)
        //   밑으로 그만큼 파고들어 z-파이팅 겹침을 냈음(사용자 지적) → 바다 안쪽 시작 오프셋(≈0.12m)만 남겨 겹침 0.
        //   BuildSea 의 seaInner 도 이 상수를 써 슬래브 끝 = 바다 시작이 항상 붙어 있게 한다(SSOT).
        const float SlabSeawardMargin = 0.005f; // 안벽 가장자리에서 바다측 시작선까지의 오프셋(=slab 끝=sea 시작, 겹침 없음)
        // 바다측 레일 → 안벽(quay) 가장자리 ≈4m — CreateFromMenu·BuildSea·BuildMooringEdge 공유 SSOT.
        const float QuayEdgeMargin    = QuayEdgeMarginM * StsConfig.ModelScale;
        // 레일(강재 주행트랙)·안전차선 길이 = 선석(berth) = 배 LOA + 양끝 계선여유. 배 1척·STS 2대 기준.
        //   슬래브(안벽)는 이보다 길고, 레일 바깥 양끝 안벽은 도로로 남는다.
        const float ShipLoaM     = 294f;   // 컨테이너선 LOA (ShipConfig.LoaMeters SSOT)
        const float BerthClearM  = 25f;    // 선수·선미 계선 여유(편측)
        // ★ 부두 Z 길이는 '선석에서 유도'한다 — 고정 가산값(구 QuayZSpan 18u) 금지.
        //   구 산식은 sizeZ = 크레인Z + 여유 + 18u ≈ 20.4u(490m)를 냈는데 레일·차선·야드는 선석 344m 안에만 깔려,
        //   양끝 각 ≈73m(전체 30%)가 빈 아스팔트로 남았다(오너 지적 2026-08-10 "Ground가 너무 넓다").
        //   → 슬래브 Z = 선석 344m + 양끝 여유 = 384m. 배·크레인 치수가 바뀌어도 빈 구간이 일정하게 따라온다.
        const float BerthLenM      = ShipLoaM + 2f * BerthClearM;   // 선석 전장 344m(레일·차선 길이와 동일 SSOT)
        const float QuayEndMarginM = 20f;                            // 선석 끝 → 슬래브 끝(편측). 계선·차량 회전 여유.
        const float QuaySlabZ      = (BerthLenM + 2f * QuayEndMarginM) * StsConfig.ModelScale;  // = 16.0u(384m)
        // 블록 베이수 = 선석에서 통로를 뺀 길이를 베이 피치로 나눈 '정수' 몫. 블록 길이는 그 정수배(끝이 반 베이로 잘리지 않게).
        //   (344 − 8×2 − 16×1) / (2 × 12.792) = 12.19 → 12베이 → 블록 길이 153.5 m.
        static readonly int   YardBays      = Mathf.Max(1, Mathf.FloorToInt(
            (BerthLenM - 2f * YardEndZM - (YardColsZ - 1) * YardAisleZM) / (YardColsZ * BayPitchM)));
        static readonly float YardBlockLenM = YardBays * BayPitchM;                       // 153.5 m
        /// <summary>야드 장치능력(TEU, 40ft 1개 = 2 TEU) — 블록수 × 베이 × 열 × 단.</summary>
        static int YardCapacityTeu => YardColsX * YardColsZ * YardBays * YardRows * YardTiers * 2;
        // 슬래브 두께 = 안벽 전면 높이(데크 y=0 → 해저) — StsConfig SSOT(코핑고 4m + 계획수심 17m = 21m ≈ 0.875u).
        //   종전 0.08u(≈1.9m)는 물에 뜬 얇은 판처럼 보였다(오너 지적 2026-08-10 "너무 얇다").
        //   데크 y=0 은 크레인 접지·VirtualFloor 기준이라 못 올리므로, 아래로 두껍게 + 수면을 내려 높이차를 만든다.
        const float Thickness    = StsConfig.QuayWallThickness;
        // 아스팔트 텍스처 1타일 = 0.5 모델단위 = 실척 12m (종전 주석 "0.5m" 은 오기 — sx 가 모델단위이므로).
        const float TileMeters   = 0.5f;

        const float RailGauge    = StsConfig.LegGaugeXMeters * StsConfig.ModelScale; // 크레인 레일 게이지와 동일 SSOT(=StsCraneCreator.LegSpanX) — 크레인 없을 때 폴백 차선용
        const float LaneOffset   = 0.045f;   // 레일 중심에서 차선까지(양옆)
        const float LaneWidth    = 0.014f;   // 차선 폭
        const float LaneY        = 0.0016f;  // 아스팔트 윗면 바로 위(z-fighting 방지)

        static readonly Color CAsphalt = new Color(0.205f, 0.20f, 0.215f); // 어두운 중성 회색
        static readonly Color CPaint   = new Color(0.88f, 0.74f, 0.10f);   // 안전 노랑 차선
        static readonly Color CRail    = new Color(0.55f, 0.58f, 0.62f);   // 강철 레일(크레인 Rail 색과 동일)
        // 안벽 전면(케이슨 콘크리트) — 연석(CConcrete)보다 살짝 어둡고 채도 낮게(해수 오염·물때 뉘앙스).
        static readonly Color CQuayWall = new Color(0.50f, 0.50f, 0.48f);

        const float RailY      = StsConfig.RailSectionH * 0.5f;  // 레일 중심 Y = 단면높이/2 (크레인 짧은 레일과 일치, =0.004)
        const float RailH      = StsConfig.RailSectionH;         // 레일 단면 높이 SSOT
        const float RailW      = StsConfig.RailSectionW;         // 레일 단면 폭(X) SSOT

        // item 1: 노면 마킹(컨테이너 야드 그리드·트럭 차로·화살표)
        const float MarkY = 0.0018f;   // 마킹 Y(아스팔트 위; 노란 차선 LaneY 0.0016보다 약간 위로 분리)
        const float MarkW = 0.006f;    // 그리드/차로 선폭(≈0.14m 실척)
        static readonly Color CMarkWhite = new Color(0.85f, 0.85f, 0.82f);  // 흰색 노면 페인트
        // 차선 점선·진행 화살표 간격 — '실척 고정'(도로 길이와 무관). 종전엔 화살표가 개수 상한(4개)이라 긴 도로에서 벌어졌다.
        const float RoadDashPeriodM = 8f;    // 점선 주기(실척 m) — 정수개로 맞추되 이 값에 근접 고정
        const float RoadDashFill    = 0.5f;  // 점선 길이 비율(주기의 50%) → 점선 4m + 간격 4m
        const float RoadArrowGapM   = 30f;   // 진행 화살표 간격(실척 m) — 상한 없이 고정 간격
        const float RoadLaneWidthM  = 4f;    // 차로 1개 폭(실척 m) — '고정'(auto 분할용). 백리치는 포털차로폭 종속이라 미사용이지만 야드 등 auto 대비 유지

        // item 2: 계선주·연석·배수(바다측 안벽)
        //   WaterSideX = 바다측 부호(+X=트롤리 Max 관례). 렌더 확인 후 어긋나면 -1로 뒤집는다.
        const float WaterSideX = 1f;
        // 바다 폭 — 안벽 가장자리에서 바다측으로 확장(배가 뜰 개방 수면). 8u≈192m: 선폭 39.5m + 여유.
        const float SeaExtent  = 6f;   // 8→6 (192→144m, X축 축소 -48m; 배 선폭 39.5m+여유 100m로 충분)
        static readonly Color CConcrete = new Color(0.60f, 0.59f, 0.56f);   // 연석 콘크리트
        static readonly Color CBollard  = new Color(0.16f, 0.17f, 0.19f);   // 계선주 캐스트강(차콜)
        static readonly Color CDrain    = new Color(0.10f, 0.10f, 0.11f);   // 배수 채널(어둠)
        static readonly Color CFoam     = new Color(0.74f, 0.80f, 0.82f);   // 안벽 접수선 포말(whitewater)
        // 수중 볼륨(수면 아래 물기둥 + 해저 바닥면). 수면 색(CSeaDeep)보다 더 어둡게 — 빛이 안 드는 깊이.
        static readonly Color CSeaBody  = new Color(0.020f, 0.065f, 0.105f);
        const float SeaTile  = 0.25f;   // 수면 파형 격자·색 텍스처 1타일 = 0.25u(≈6m)
        const float SeaDamp  = 0.4f;    // 안벽에서 이 거리(≈10m) 안은 파고를 죽여 잔잔(포말 평탄 안착)
        const float FoamW    = 0.05f;   // 접수선 포말 폭(≈1.2m)
        // 수면 높이 — 데크(y=0) 아래 StsConfig.SeaLevelY(코핑고 4m ⇒ −0.1667u). 배 흘수선과 동일 SSOT.
        //   종전엔 수면이 데크와 같은 높이(±수 cm)라 z-fighting을 SeaLift/SeaBaseDrop 같은 미세 오프셋으로
        //   달래야 했고, 결과적으로 '지면과 바다 높이가 안 맞는' 그림이 됐다(오너 지적). 이제 4m 건현으로
        //   물리적으로 분리되므로 미세 오프셋은 0(수면은 평면 그대로, 파고만 외해로 갈수록 살아난다).
        const float SeaLevelY   = StsConfig.SeaLevelY;
        const float SeaLift     = 0f;   // 데크 z-fighting 회피용 미세 들어올림 — 건현이 생겨 불필요
        const float SeaBaseDrop = 0f;   // 접수선 미세 하강 — 건현이 생겨 불필요(벽면에서 수면 그대로 맞닿음)

        static Mesh _unitQuad;

        // Ground 메뉴 선택 항목(토글) — EditorPrefs로 보존. '부두 바닥 생성' 시 켜진 것만 만든다.
        const string MenuSea     = "Ground/바다";
        const string MenuRoad    = "Ground/도로";
        const string MenuCurb    = "Ground/연석 (Curb)";
        const string MenuBollard = "Ground/계선주 (Bollard)";
        static bool IncludeSea     { get => EditorPrefs.GetBool("QuayGround.sea",     true); set => EditorPrefs.SetBool("QuayGround.sea",     value); }
        static bool IncludeRoad    { get => EditorPrefs.GetBool("QuayGround.road",    true); set => EditorPrefs.SetBool("QuayGround.road",    value); }
        static bool IncludeCurb    { get => EditorPrefs.GetBool("QuayGround.curb",    true); set => EditorPrefs.SetBool("QuayGround.curb",    value); }
        static bool IncludeBollard { get => EditorPrefs.GetBool("QuayGround.bollard", true); set => EditorPrefs.SetBool("QuayGround.bollard", value); }

        static void ToggleSea()     => IncludeSea     = !IncludeSea;
        static bool ToggleSeaV()    { Menu.SetChecked(MenuSea, IncludeSea); return true; }
        static void ToggleRoad()    => IncludeRoad    = !IncludeRoad;
        static bool ToggleRoadV()   { Menu.SetChecked(MenuRoad, IncludeRoad); return true; }
        static void ToggleCurb()    => IncludeCurb    = !IncludeCurb;
        static bool ToggleCurbV()   { Menu.SetChecked(MenuCurb, IncludeCurb); return true; }
        static void ToggleBollard() => IncludeBollard = !IncludeBollard;
        static bool ToggleBollardV(){ Menu.SetChecked(MenuBollard, IncludeBollard); return true; }

        /// <summary>
        /// 항구 한 번에 세팅 — 부두 → STS 2대 → RTG 2대 → 컨테이너선(자동 접안). 오너 지시 2026-08-10
        /// "STS 2대 RTG 2대 무조건 배치, 알아서 배치해".
        ///
        /// 순서가 중요하다: ① 부두가 안벽 위치(QuayRail)의 SSOT를 깔고 → ② STS가 그 레일에 스냅되고 →
        /// ③ RTG가 부두가 그린 야드 블록 존을 실측해 올라가고 → ④ 배가 바다측 QuayRail 기준으로 접안한다.
        /// 각 단계는 개별 메뉴로도 그대로 있다(이 메뉴는 순서만 보장).
        /// </summary>
        public static void SetupWholePort()
        {
            // 도로 토글이 꺼져 있으면 야드 블록(YardBlock_Zone)이 안 그려지고, 그러면 RTG가 앉을 자리가 없어
            //   ③에서 조용히 경고만 내고 끝난다. 이 메뉴는 '항구 완성'을 약속하므로 필요한 항목을 켜고 시작한다.
            bool roadWas = IncludeRoad, seaWas = IncludeSea;
            IncludeRoad = true; IncludeSea = true;
            if (!roadWas || !seaWas)
                Debug.Log($"[Port] 항구 세팅에 필요한 Ground 토글을 켰습니다 — 도로 {roadWas}→true, 바다 {seaWas}→true.");

            CreateFromMenu();                                    // ① 부두(수식 유도) + 도로 + 야드 블록 + 바다
            StsCraneCreator.PlaceTwoSts();                       // ② STS 2대 — 레일 공유, 선석 중앙 ±40m
            RtgCraneCreator.PlaceRtgsInYard();                   // ③ RTG 2대 — 야드 블록 존 실측 배치
            Container.Ship.EditorTools.ShipCreator.CreateShip();  // ④ 컨테이너선 — 생성 직후 자동 접안

            Debug.Log($"[Port] 항구 세팅 완료 — 안벽 {BerthLenM:F0}m(슬래브 {QuaySlabZ * StsConfig.InvModelScale:F0}m) × 에이프런 {ApronDepthM:F1}m, " +
                      $"야드 {YardColsX}×{YardColsZ}블록({YardBays}베이×{YardRows}열) 장치능력 {YardCapacityTeu:N0} TEU, " +
                      $"STS 2 + RTG {2} + 컨테이너선 1. 데크 y=0 · 수면 y={StsConfig.SeaLevelY:F4}u(−{StsConfig.QuayDeckAboveSeaMeters:F0}m) · 안벽 {StsConfig.QuayWallHeightMeters:F0}m.");
        }

        public static void CreateFromMenu()
        {
            var prev = GameObject.Find(RootName);
            if (prev != null) Undo.DestroyObjectImmediate(prev);

            // 크레인이 있으면 그 XZ 바운즈에 맞춰 크기·중심을 잡는다. 갠트리 주행 범위가 있으면 Z로 그만큼 더 길게.
            Vector3 center = Vector3.zero;
            float sizeX = DefaultSizeX, sizeZ = DefaultSizeZ;
            float gantryRange = 0f;
            var crane = GameObject.Find(CraneName);
            if (crane != null)
            {
                var gantry = crane.GetComponent<GantryMover>();
                if (gantry != null) gantryRange = gantry.Max - gantry.Min;

                if (TryWorldBounds(crane.transform, out Bounds b))
                {
                    // Z 중심: 갠트리가 있으면 주행 범위 중간(=초기 위치)을 '월드'로 변환, 없으면 바운즈 중심.
                    //   gantry.Min/Max는 크레인-로컬 Z(localPosition 기준)다. 종전엔 이 로컬값을 월드 center.z로
                    //   그대로 써서, 크레인이 부모 밑이거나 Z오프셋을 가지면 지면이 Z로 어긋났다. → 크레인 트랜스폼으로
                    //   로컬 mid를 월드로 투영(부모 스케일/오프셋까지 반영). 루트 크레인이면 종전과 비트 동일.
                    float centerZ;
                    if (gantry != null)
                    {
                        Vector3 lp = crane.transform.localPosition;
                        Vector3 midLocal = new Vector3(lp.x, lp.y, (gantry.Min + gantry.Max) * 0.5f);
                        centerZ = (crane.transform.parent != null
                                    ? crane.transform.parent.TransformPoint(midLocal)
                                    : midLocal).z;
                    }
                    else centerZ = b.center.z;
                    // 바닥 Z 길이는 '선석(배 LOA + 계선여유) + 양끝 여유'에서 유도 — 갠트리 주행거리와 무관하다.
                    //   크레인 자체가 그보다 길면(비정상) 크레인 바운즈로 넓혀 잘리지 않게만 보정.
                    sizeZ = Mathf.Max(QuaySlabZ, b.size.z + MarginZ * 2f);

                    // X: 슬래브를 '안벽 가장자리' 기준으로 재배치 — 바다측으로 안 삐져나오게(에이프런=육지측만).
                    bool waterPos = WaterSideX >= 0f;
                    float sgnW = waterPos ? 1f : -1f;
                    float waterRailX = WaterRailWorldX(crane, waterPos);
                    if (!float.IsNaN(waterRailX))
                    {
                        float quayEdge     = waterRailX + sgnW * QuayEdgeMargin;      // 안벽 가장자리(계선/연석/바다 시작선과 동일 기준)
                        float seawardEdge  = quayEdge   + sgnW * SlabSeawardMargin;   // 연석 착좌분만 바다측으로
                        float landwardEdge = quayEdge   - sgnW * ApronDepth;          // 에이프런(육지측)
                        sizeX  = Mathf.Abs(seawardEdge - landwardEdge);
                        center = new Vector3((seawardEdge + landwardEdge) * 0.5f, 0f, centerZ);
                    }
                    else   // 레일을 못 찾으면 종전 바운즈 기준으로 폴백
                    {
                        sizeX  = Mathf.Max(DefaultSizeX, b.size.x + MarginX * 2f);
                        center = new Vector3(b.center.x, 0f, centerZ);
                    }
                    // (갠트리 주행범위 맞춤은 레일을 실제로 깐 뒤 Build 다음에 한다 — GantryRangeFitMenu.ApplyFit)
                }
            }

            var root = Build(center, sizeX, sizeZ, crane);
            Undo.RegisterCreatedObjectUndo(root, "Create Quay Ground");

            // 레거시 'VirtualFloor' 회수 — 슬래브가 바닥 콜라이더를 직접 갖게 되면서 역할이 없어졌다.
            //   이 오브젝트는 MR 테이블 셋업(Scene/MR 씬 셋업)이 만든 100×100u 박스인데, 1u=24m 환산에서
            //   2,400×2,400m 라 보이는 아스팔트의 153배를 허공에서 밟게 했다(바다 위로도 계속 걸림).
            //   ContainerPhysics.FindFloorTopY 는 이 오브젝트가 없으면 데크 y=0 으로 폴백하고,
            //   그게 곧 슬래브 윗면이라 값은 그대로다. Undo 로 되돌릴 수 있다.
            var legacyFloor = GameObject.Find("VirtualFloor");
            if (legacyFloor != null)
            {
                Undo.DestroyObjectImmediate(legacyFloor);
                Debug.Log("[QuayGround] 레거시 'VirtualFloor'(100×100u ≈ 2,400×2,400m) 제거 — 바닥은 슬래브 콜라이더가 담당.");
            }

            // 레일을 깐 뒤 갠트리 주행범위를 '바퀴가 레일을 안 벗어나게' 자동 맞춤(메뉴와 동일 계산).
            if (crane != null)
            {
                var g = crane.GetComponent<GantryMover>();
                if (g != null && GantryRangeFitMenu.ApplyFit(crane, g, out string fitMsg))
                {
                    gantryRange = g.Max - g.Min;
                    Debug.Log("[QuayGround] 갠트리 주행범위 자동 맞춤 — " + fitMsg);
                }
            }

            // 씬에 이미 있는 컨테이너선 자동 재접안 — 부두를 다시 깔면 안벽 X(레일)·수면 Y가 바뀌므로,
            //   배를 손으로 다시 만들지 않아도 제자리를 찾게 한다(오너 지시 '생성/재생성 한 번으로 접안까지').
            //   배가 없으면 조용히 통과. 상세 로그는 ShipBerthMenu가 남긴다.
            Container.Ship.EditorTools.ShipBerthMenu.ReberthExistingShip();

            FloorGate();   // 밟히는 면 = 보이는 면 — 생성 직후 자동 검사

            Selection.activeGameObject = root;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
            Debug.Log($"[QuayGround] 아스팔트 부두 바닥 생성 — {sizeX:F2}u × {sizeZ:F2}u, 데크 윗면 y=0, 안벽 두께 {Thickness:F3}u" +
                      $"(={StsConfig.QuayWallHeightMeters:F0}m: 수면 위 {StsConfig.QuayDeckAboveSeaMeters:F0}m + 수심 {StsConfig.BerthWaterDepthMeters:F0}m), 중심 {center}. " +
                      (crane != null ? $"크레인 레일에 맞춰 긴 트랙 + 안전 차선 표시. 갠트리 주행범위를 레일 끝까지 자동 확장(총 {gantryRange:F2}m, 양쪽 대칭)." : "크레인 없음 → 기본 차선."));
        }

        public static void ClearGround()
        {
            var root = GameObject.Find(RootName);
            if (root == null) { Debug.Log($"[QuayGround] '{RootName}' 없음 — 지울 것 없음."); return; }

            int n = root.transform.childCount;
            int group = Undo.GetCurrentGroup();
            for (int i = n - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
            Undo.CollapseUndoOperations(group);   // 118개를 Cmd+Z 한 번으로 되돌린다
            Debug.Log($"[QuayGround] '{RootName}' 내용 {n}개 삭제 — 빈 루트만 남음. Cmd+Z 로 복구, 'Ground/부두 바닥 생성'으로 재생성.");
        }

        // ── 바닥 게이트 ──────────────────────────────────────────────────────────────
        //   '밟히는 면'과 '보이는 면'이 어긋나도 조용히 돌아가던 것을 막는다. 과거 VirtualFloor
        //   (100×100u = 2,400×2,400m)가 유일한 바닥이라 보이는 아스팔트(≈98×384m)의 153배를
        //   허공에서 밟을 수 있었고, 화면상 아무 표시가 없어 오래 발각되지 않았다.
        const float FloorGateTolU = 0.01f;   // 판정 허용오차 0.01u ≈ 0.24m

        /// <summary>바닥 콜라이더가 보이는 슬래브와 일치하는지 검사. PASS면 true.
        /// CreateFromMenu 끝에서 자동 실행되고, 메뉴로도 단독 실행된다.</summary>
        public static bool FloorGate()
        {
            var quay = GameObject.Find(RootName);
            if (quay == null) { Debug.LogWarning($"[QA] FLOOR gate: '{RootName}' 없음 => SKIP"); return true; }

            var slabT = quay.transform.Find(StsPartNames.QuayAsphalt);
            if (slabT == null || !slabT.TryGetComponent<Collider>(out var slabCol)
                              || !slabT.TryGetComponent<Renderer>(out var slabRen))
            {
                Debug.LogError($"[QA] FLOOR gate: '{StsPartNames.QuayAsphalt}'에 Collider/Renderer 가 없음 => FAIL");
                return false;
            }

            var fails = new List<string>();
            Bounds cb = slabCol.bounds, rb = slabRen.bounds;

            // ① 밟히는 면 = 보이는 면 (XZ 범위 일치)
            if (Mathf.Abs(cb.size.x - rb.size.x) > FloorGateTolU || Mathf.Abs(cb.size.z - rb.size.z) > FloorGateTolU)
                fails.Add($"슬래브 콜라이더 XZ {cb.size.x:F3}×{cb.size.z:F3}u ≠ 렌더러 {rb.size.x:F3}×{rb.size.z:F3}u");

            // ② 걷는 높이 = 데크 y=0 (StsConfig 의 못 움직이는 기준면)
            if (Mathf.Abs(cb.max.y) > FloorGateTolU)
                fails.Add($"바닥 윗면 y={cb.max.y:F4}u ≠ 데크 0");

            // ③ 데크 높이를 지나면서 슬래브보다 넓은 바닥이 또 있으면 안 된다(부활한 VirtualFloor 등)
            float slabArea = Mathf.Max(rb.size.x * rb.size.z, 1e-6f);
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
            {
                if (c == slabCol || c.isTrigger || c.transform.IsChildOf(quay.transform)) continue;
                Bounds ob = c.bounds;
                if (ob.min.y > 0f || ob.max.y < 0f) continue;                  // 데크 높이를 안 지나면 바닥이 아니다
                float area = ob.size.x * ob.size.z;
                if (area <= slabArea) continue;                                // 슬래브보다 좁으면 무해
                fails.Add($"슬래브보다 넓은 바닥 '{c.name}' {ob.size.x:F1}×{ob.size.z:F1}u "
                        + $"(슬래브의 {area / slabArea:F0}배) — 안벽 밖 허공을 밟게 된다");
            }

            if (fails.Count == 0)
            {
                Debug.Log($"[QA] FLOOR gate: 밟히는 면 {cb.size.x:F3}×{cb.size.z:F3}u "
                        + $"(≈{cb.size.x * StsConfig.InvModelScale:F0}×{cb.size.z * StsConfig.InvModelScale:F0}m), 윗면 y=0 => PASS");
                return true;
            }
            Debug.LogError("[QA] FLOOR gate => FAIL\n  · " + string.Join("\n  · ", fails));
            return false;
        }

        static GameObject Build(Vector3 center, float sizeX, float sizeZ, GameObject crane)
        {
            var root = new GameObject(RootName);
            root.transform.position = center;

            // 1) 부두 슬래브 — 윗면(데크) y=0, 아래로 Thickness(=안벽 전면 21m, 해저까지).
            //    서브메시 2개: 0=윗면 아스팔트 노면, 1=측면·저면 콘크리트 안벽(케이슨). 21m 벽면에 아스팔트를
            //    바르면 재질이 어긋나므로 분리한다(제12법칙 재질).
            var slab = new GameObject(StsPartNames.QuayAsphalt);
            slab.transform.SetParent(root.transform, worldPositionStays: false);
            slab.AddComponent<MeshFilter>().sharedMesh = BuildSlab(sizeX, sizeZ, Thickness, TileMeters);
            slab.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                MakeMat(CAsphalt,  metallic: 0.0f, smooth: 0.12f, tex: BuildAsphaltTexture()),
                MakeMat(CQuayWall, metallic: 0.0f, smooth: 0.10f, tex: null),
            };
            // 바닥 콜라이더 — 걷는 면이자 컨테이너 착지면. 슬래브 메시가 정확한 직육면체(윗면 y=0,
            //   아래로 Thickness)라 BoxCollider 하나로 빈틈없이 덮인다(MeshCollider 는 VR 모바일 물리 낭비).
            //   ★ 밟히는 범위 = 보이는 범위. 안벽 밖은 바닥이 없다.
            // ponytail: 바다로 떨어뜨린 컨테이너를 받아줄 콜라이더는 없다(계속 낙하).
            //   필요해지면 BuildSea 의 Sea_Body 박스에 BoxCollider 를 붙이면 해저가 받는다.
            var slabCol = slab.AddComponent<BoxCollider>();
            slabCol.center = new Vector3(0f, -Thickness * 0.5f, 0f);
            slabCol.size   = new Vector3(sizeX, Thickness, sizeZ);

            // 2) 레일 안전 차선(노랑) — 크레인 레일 X를 찾아 양옆에, 없으면 기본 게이지
            //   "Rail_" 접두사는 러닝레일 마커(Rail_Land/Rail_Water)만이 아니라 보기의 레일 스위퍼(Rail_Sweeper,
            //     2다리×Z2끝×스위퍼2끝=8개)에도 붙는다(StsPartNames.RailPrefix 주석 참조). 스위퍼는 다리 X와 같은 자리라
            //     그대로 두면 같은 X에 차선·QuayRail이 5중으로 겹쳐 그려진다(VR 모바일 드로우 낭비). → '서로 다른 X'만
            //     남겨 레일 1줄 = 차선 1개가 되게 정리. 허용오차 0.1u(≈2.4m): 두 레일 게이지 0.75u(18m)의 절반보다 훨씬 작고
            //     부동소수 잡음보다 커서, 실제 두 레일은 보존하고 동일 X 스위퍼만 접는다.
            var lanesX = new List<float>();
            if (crane != null)
            {
                const float laneMergeTol = 0.1f;
                foreach (var t in crane.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(StsPartNames.RailPrefix))
                    {
                        float lx = t.position.x - center.x;
                        bool dup = false;
                        foreach (float e in lanesX) if (Mathf.Abs(e - lx) < laneMergeTol) { dup = true; break; }
                        if (!dup) lanesX.Add(lx);
                    }
            }
            if (lanesX.Count == 0)
            {
                // 크레인이 없으면 레일을 슬래브 한가운데(±게이지/2)가 아니라 '바다측 끝 기준' 안벽 위치에 둔다.
                //   → Lane/QuayRail/Quay_Curb/Quay_Bollard(모두 lanesX 파생)가 물가에 정렬. 크레인 있을 땐 apron 사이징으로
                //   실제 레일이 바로 이 자리에 오므로 두 경우가 일치한다.
                bool wp = WaterSideX >= 0f; float sg = wp ? 1f : -1f;
                float hxL = sizeX * 0.5f;
                float waterRailLocal = sg * (hxL - SlabSeawardMargin - QuayEdgeMargin);  // 바다측 레일 = 안벽 끝에서 4m 안쪽
                float landRailLocal  = waterRailLocal - sg * RailGauge;                   // 육지측 레일 = 게이지만큼 육지측
                lanesX.Add(landRailLocal);
                lanesX.Add(waterRailLocal);
            }

            // 레일·안전차선 길이 = 선석(berth) = 배 LOA + 양끝 여유. 슬래브보다 짧게(양끝은 도로로 남김). 슬래브 안으로 클램프.
            float berthLen = Mathf.Min(sizeZ * 0.98f, BerthLenM * StsConfig.ModelScale);

            var paint = MakeMat(CPaint, metallic: 0.0f, smooth: 0.25f, tex: null);
            foreach (float lx in lanesX)
                foreach (float off in new[] { -LaneOffset, LaneOffset })
                    Stripe(root.transform, "Lane", new Vector3(lx + off, LaneY, 0f),
                           LaneWidth, berthLen, paint);

            // 3) 고정 주행레일(트랙) — 선석 길이만큼(배가 접안하는 구간)만. 크레인이 그 위를 굴러간다.
            //    크레인의 Rail_ Transform이 정해준 X로 정렬. 레일 바깥 양끝(Z)은 도로로 남는다.
            if (lanesX.Count > 0)
            {
                var railMat = MakeMat(CRail, metallic: 0.7f, smooth: 0.35f, tex: null);
                foreach (float lx in lanesX)
                    QuayBox(root.transform, StsPartNames.QuayRail, new Vector3(lx, RailY, 0f),
                            new Vector3(RailW, RailH, berthLen), railMat);
            }

            // 5) 계선주·연석 — 바다측 안벽(항상 생성). 크레인 레일 기준 상대 배치.
            BuildMooringEdge(root.transform, sizeX, sizeZ, lanesX);

            // 4·6·7) 도로·보관소·바다 — Ground 메뉴 토글로 선택 생성
            if (IncludeRoad)
                BuildRoadNetwork(root.transform, sizeX, sizeZ, lanesX);   // 통합 도로 그리드(종방향 3 + 횡방향, 완전 연결)
            if (IncludeSea)  BuildSea(root.transform, sizeX, sizeZ, lanesX);                         // 바다

            return root;
        }

        // 단순 박스(콜라이더 없음) — 고정 트랙 레일용
        static void QuayBox(Transform parent, string name, Vector3 localCenter, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // 윗면이 y=0, 아래로 th 만큼 두께를 가진 슬래브 박스 메시. 윗면 UV 는 타일 반복.
        //   서브메시 0 = 윗면(아스팔트 노면), 서브메시 1 = 측면 4 + 저면(콘크리트 안벽면).
        static Mesh BuildSlab(float sx, float sz, float th, float tile)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            float ux = sx / tile, uz = sz / tile, ut = th / tile;
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var t = new List<int>();      // 서브메시 0 — 윗면
            var tw = new List<int>();     // 서브메시 1 — 측면·저면(안벽)

            // Top (+Y) — 보이는 면, 타일 UV
            AddQuad(v, uv, t,
                new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, uz), new Vector2(0, uz), Vector3.up);
            // Bottom (-Y) — 해저면(안벽 재질)
            AddQuad(v, uv, tw,
                new Vector3(-hx, -th, -hz), new Vector3(hx, -th, -hz), new Vector3(hx, -th, hz), new Vector3(-hx, -th, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, uz), new Vector2(0, uz), Vector3.down);
            // 4 측면 = 안벽 전면·후면(데크에서 해저까지 21m)
            AddQuad(v, uv, tw,
                new Vector3(hx, -th, -hz), new Vector3(hx, -th, hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(uz, 0), new Vector2(uz, ut), new Vector2(0, ut), Vector3.right);
            AddQuad(v, uv, tw,
                new Vector3(-hx, -th, -hz), new Vector3(-hx, -th, hz), new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(uz, 0), new Vector2(uz, ut), new Vector2(0, ut), Vector3.left);
            AddQuad(v, uv, tw,
                new Vector3(-hx, -th, hz), new Vector3(hx, -th, hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, ut), new Vector2(0, ut), Vector3.forward);
            AddQuad(v, uv, tw,
                new Vector3(-hx, -th, -hz), new Vector3(hx, -th, -hz), new Vector3(hx, 0f, -hz), new Vector3(-hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, ut), new Vector2(0, ut), Vector3.back);

            var m = new Mesh { name = "Quay_Slab", subMeshCount = 2 };
            m.SetVertices(v); m.SetUVs(0, uv);
            m.SetTriangles(t, 0); m.SetTriangles(tw, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // 노란 차선 1줄 — 단위 쿼드를 눕혀 스케일.
        static void Stripe(Transform parent, string name, Vector3 localCenter, float w, float len, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = new Vector3(w, 1f, len);
            go.AddComponent<MeshFilter>().sharedMesh = UnitQuad();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // 윗면을 향한 1×1 단위 쿼드(중심 원점, y=0).
        static Mesh UnitQuad()
        {
            if (_unitQuad != null) return _unitQuad;
            var m = new Mesh { name = "Quay_UnitQuad" };
            m.SetVertices(new List<Vector3> {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),   new Vector3(-0.5f, 0f, 0.5f) });
            m.SetUVs(0, new List<Vector2> {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return _unitQuad = m;
        }

        // outward 기준 와인딩 자동 보정 + UV.
        static void AddQuad(List<Vector3> v, List<Vector2> uv, List<int> t,
                            Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 outward)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            else
            { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        }

        // item 1: 노면 마킹 — 트럭 주행 도로(크레인 밑 포털 → 백리치)
        //   크레인은 백리치에서 컨테이너를 트럭(섀시) 위로 내린다. 트럭은 크레인 다리 사이(포털)를 지나
        //   백리치로 들어오므로, 그 구간에 Z방향 주행 도로(차선 경계·레인 점선·진행 화살표)를 깐다.
        //   전부 아스팔트 윗면 바로 위(MarkY)의 평면 페인트(쿼드). 실척↔모델 환산 = StsConfig.ModelScale(1/24).
        // 통합 도로 그리드 — 종방향 3줄(크레인 하역차로·안벽 간선·육지 간선)을 횡방향 연결도로가 '전폭 관통'해 하나로 잇는다.
        //   전부 끊김 없이 연결(사다리형 격자). 크레인 하역차로 3차로 일방, 나머지 2차로 양방향. 교차부 정지선 + 게이트 + 배수.
        //   ※ 야드 블록 기하는 상단 §터미널 레이아웃 수식에서 유도한다(RTG 주행 = 블록 길이 = 베이수 × 베이피치).

        static void BuildRoadNetwork(Transform root, float sizeX, float sizeZ, List<float> lanesX)
        {
            var white = MakeMat(CMarkWhite, 0.0f, 0.20f, null);
            bool waterPos = WaterSideX >= 0f;
            float sgn = waterPos ? 1f : -1f;                 // 바다 부호
            float s = StsConfig.ModelScale;
            float hx = sizeX * 0.5f, hz = sizeZ * 0.98f * 0.5f;
            float zLo = -hz, zHi = hz;
            float lane = 4f * s, mainW = 2f * lane, crossW = mainW;   // 차로 4m, 간선 2차로=8m
            float yClear = LaneOffset + 0.02f;

            // 레일 X (바다측=최대 / 육지측=최소)
            float waterRail = sgn * RailGauge * 0.5f, landRail = -sgn * RailGauge * 0.5f;
            if (lanesX != null && lanesX.Count > 0)
            {
                waterRail = lanesX[0]; landRail = lanesX[0];
                for (int i = 1; i < lanesX.Count; i++)
                {
                    waterRail = waterPos ? Mathf.Max(waterRail, lanesX[i]) : Mathf.Min(waterRail, lanesX[i]);
                    landRail  = waterPos ? Mathf.Min(landRail,  lanesX[i]) : Mathf.Max(landRail,  lanesX[i]);
                }
            }

            // 종방향 도로 3줄만 두고, RTG 야드를 간선 2줄이 앞뒤로 감싼다.
            //   ① STS 인터체인지(레일 사이): 트럭이 포털 밑을 지나 컨테이너를 주고받는 4차로 일방.
            //   ② 안벽 간선 / ③ 육지 간선: 야드를 앞뒤로 감싸는 2차로 양방향 순환도로. 그 사이 밴드가 RTG 야드.
            //   횡단 연결도로는 '블록 사이 gap'에만 — 종전처럼 전폭을 관통하는 5줄 격자를 깔지 않는다.
            float clLand = landRail + sgn * yClear, clSea = waterRail - sgn * yClear;   // ① 인터체인지(레일 사이)
            float wsSea  = landRail - sgn * yClear, wsLand = wsSea - sgn * mainW;        // ② 안벽 간선(레일 뒤) — 야드 바다측 경계
            float lsLand = -sgn * (hx - (0.5f + 6f) * s), lsSea = lsLand + sgn * mainW;  // ③ 육지 간선(가로등 스트립 안쪽) — 야드 육지측 경계
            DrawRoadSegment(root, white, clLand, clSea, zLo, zHi, 4, "STS 인터체인지", arrows: true);
            DrawRoadSegment(root, white, wsLand, wsSea, zLo, zHi, 2, "안벽 간선도로", arrows: false);
            DrawRoadSegment(root, white, lsSea, lsLand, zLo, zHi, 2, "육지 간선도로", arrows: false);

            // ── RTG 야드 — 블록 장축이 안벽과 '평행'(RTG 주행축 = Z). 전부 §터미널 레이아웃 수식에서 유도.
            //    블록 = 스택 6열(X, 6×2.838=17.03m) × 12베이(Z, 12×12.792=153.5m).
            //    RTG 스팬 23.6m가 이 존을 감싸고 Z로 주행한다 — 남는 6.57m가 트럭레인·다리 여유.
            //    종전엔 블록 장축이 안벽에 '수직'이고 길이가 RTG 주행 40m에 묶여 3베이짜리 토막이었다.
            float aisleX  = YardAisleXM * s, spanW = RtgSpanM * s;
            float stackW  = YardRows * RowPitchM * s;                  // 6열 스택 폭(X)
            float blkLen  = YardBlockLenM * s;                         // 블록 길이(Z) = 베이수 × 베이피치
            float berthHalf = Mathf.Min(hz, BerthLenM * s * 0.5f);
            float yardSeaX = wsLand, yardLandX = lsSea;                // 야드 X 경계(두 간선 사이)
            // 블록 중심 X — 육지측 경계에서 [통로 → 스팬]을 번갈아 쌓는다. 합 = YardBandM.
            var blkX = new float[YardColsX];
            for (int k = 0; k < YardColsX; k++)
                blkX[k] = yardLandX + sgn * (aisleX + spanW * 0.5f + k * (spanW + aisleX));
            // 블록 중심 Z — 선석 중앙 대칭. 사이에 (n−1)개의 횡단 통로.
            var blkZ = new float[YardColsZ];
            float pitchZ = blkLen + YardAisleZM * s;
            for (int k = 0; k < YardColsZ; k++)
                blkZ[k] = (k - (YardColsZ - 1) * 0.5f) * pitchZ;

            var zoneFill = MakeMat(new Color(0.185f, 0.175f, 0.155f), 0.0f, 0.14f, null);  // 스택 존 바닥(아스팔트보다 살짝 어둡게)
            foreach (float xc in blkX)
            foreach (float zc in blkZ)
            {
                // 존 바닥(어두운 스택 구획)
                Stripe(root, StsPartNames.YardBlockZone, new Vector3(xc, LaneY, zc), stackW, blkLen, zoneFill);
                // 스택 6열 격자선 — 선은 Z를 따라 흐르고 X를 6등분(스팬 방향이 X). 단일 메시로 합쳐 드로우콜 절약.
                var rv = new List<Vector3>(); var rt = new List<int>();
                for (int r = 1; r < YardRows; r++)
                    AddMarkQuad(rv, rt, xc - stackW * 0.5f + stackW * r / YardRows, zc, MarkW, blkLen);
                MarkMeshGO(root, "YardBlock_Rows", rv, rt, MarkY, white);
                // 베이 구획선 — X를 따라 흐르고 Z를 베이수로 등분(40ft 베이 경계)
                var bv = new List<Vector3>(); var bt = new List<int>();
                for (int q = 1; q < YardBays; q++)
                    AddMarkQuad(bv, bt, xc, zc - blkLen * 0.5f + blkLen * q / YardBays, stackW, MarkW);
                MarkMeshGO(root, "YardBlock_Bays", bv, bt, MarkY, white);
                // 블록 경계(Z 2변 + X 2변)
                Stripe(root, "YardBlock_Edge", new Vector3(xc, MarkY, zc - blkLen * 0.5f), stackW, MarkW, white);
                Stripe(root, "YardBlock_Edge", new Vector3(xc, MarkY, zc + blkLen * 0.5f), stackW, MarkW, white);
                Stripe(root, "YardBlock_Edge", new Vector3(xc - stackW * 0.5f, MarkY, zc), MarkW, blkLen, white);
                Stripe(root, "YardBlock_Edge", new Vector3(xc + stackW * 0.5f, MarkY, zc), MarkW, blkLen, white);
            }

            // 횡단 연결도로 — 블록 사이 Z 통로(YardColsZ−1곳). 두 간선을 잇는 2차로(안벽↔육지).
            for (int k = 0; k < YardColsZ - 1; k++)
            {
                float zg = (blkZ[k] + blkZ[k + 1]) * 0.5f;
                float cxMid = (yardSeaX + yardLandX) * 0.5f, cxLen = Mathf.Abs(yardLandX - yardSeaX);
                Stripe(root, "Cross_Edge", new Vector3(cxMid, MarkY, zg - crossW * 0.5f), cxLen, MarkW, white);
                Stripe(root, "Cross_Edge", new Vector3(cxMid, MarkY, zg + crossW * 0.5f), cxLen, MarkW, white);
                XDash(root, zg, yardSeaX, yardLandX, null, 0f, white);   // 중앙 점선(gap 없음)
            }

            // 게이트 (육지 간선 위, 중앙 gap Z=0 부근)
            float gateZ = 0f;
            BuildGate(root, (lsSea + lsLand) * 0.5f, gateZ, mainW);

            // 배수 트렌치 (안벽 간선 바다측 가장자리, 레일과 야드 사이 저지대)
            float drainX = wsSea + sgn * 0.02f;
            var drainMat = MakeMat(CDrain, 0.2f, 0.10f, null);
            var steel = MakeMat(CRail, 0.7f, 0.40f, null);
            PbBox(root, "Quay_DrainChannel", new Vector3(drainX, 0.003f, 0f), new Vector3(0.03f, 0.008f, 2f * hz), drainMat);
            QuayBox(root, "Quay_DrainGrate",  new Vector3(drainX, 0.006f, 0f), new Vector3(0.034f, 0.003f, 2f * hz), steel);

            Debug.Log($"[QuayGround] 야드·도로 수식 유도 — 종방향 3(인터체인지4차로·안벽간선·육지간선) + " +
                      $"RTG 야드 {YardColsX}×{YardColsZ}={YardColsX * YardColsZ}블록 · 각 {stackW * StsConfig.InvModelScale:F1}m({YardRows}열) × " +
                      $"{blkLen * StsConfig.InvModelScale:F1}m({YardBays}베이) · 주행축=Z(안벽 평행) + 횡단 {YardColsZ - 1}. " +
                      $"야드밴드 실측 {Mathf.Abs(yardLandX - yardSeaX) * StsConfig.InvModelScale:F1}m vs 수식 {YardBandM:F1}m, " +
                      $"장치능력 {YardCapacityTeu:N0} TEU({YardTiers}단), 게이트 Z={gateZ:F1}u.");
        }

        // 한 도로 구간(x0~x1)을 차로로 그린다. lanes>0=정확히 그 수(균등 분할·증축 안 함), ≤0=4m기준 자동.
        //   구간 경계(x0~x1)는 호출부가 '황색선 안쪽'으로 넘겨준다. 점선·화살표는 길이에 정수개로 맞춰 균등 배치.
        static void DrawRoadSegment(Transform root, Material white, float x0, float x1, float zLo, float zHi, int lanes, string label, bool arrows = true)
        {
            float xa = Mathf.Min(x0, x1), xb = Mathf.Max(x0, x1), w = xb - xa;
            float zMid = (zLo + zHi) * 0.5f, zLen = zHi - zLo;
            if (w < 0.04f || zLen < 0.2f) return;

            // [차로 폭 고정] lanes<=0이면 폭÷RoadLaneWidthM(≈4m)로 개수를 정해 차로 폭을 항상 ≈4m 균일하게.
            if (lanes <= 0) lanes = Mathf.Max(1, Mathf.RoundToInt(w / (RoadLaneWidthM * StsConfig.ModelScale)));
            float laneW = w / lanes;                                    // 구간을 정확히 lanes로 균등 분할

            // 양끝 경계 실선(Z방향)
            Stripe(root, "Road_Edge", new Vector3(xa, MarkY, zMid), MarkW, zLen, white);
            Stripe(root, "Road_Edge", new Vector3(xb, MarkY, zMid), MarkW, zLen, white);
            // 끝단 캡(양 Z끝) — 개방 단면(대롱거리는 열린 선끝) 방지
            Stripe(root, "Road_EndCap", new Vector3((xa + xb) * 0.5f, MarkY, zLo), w, MarkW, white);
            Stripe(root, "Road_EndCap", new Vector3((xa + xb) * 0.5f, MarkY, zHi), w, MarkW, white);

            // 점선 — '실척 고정' 주기(RoadDashPeriodM ≈8m). 한 차선의 모든 점선을 '단일 메시'로 합쳐 드로우콜을 줄인다(VR).
            float periodTarget = RoadDashPeriodM * StsConfig.ModelScale;
            int nPer = Mathf.Max(1, Mathf.RoundToInt(zLen / periodTarget));
            float period = zLen / nPer;
            float dashLen = period * RoadDashFill;      // 점선 4m + 간격 4m
            for (int k = 1; k < lanes; k++)
            {
                float x = xa + laneW * k;
                var dv = new List<Vector3>(nPer * 4); var dt = new List<int>(nPer * 6);
                for (int p = 0; p < nPer; p++)
                    AddMarkQuad(dv, dt, x, zLo + period * (p + 0.5f), MarkW, dashLen);
                MarkMeshGO(root, "Road_LaneDash", dv, dt, MarkY, white);
            }

            // 화살표(일방통행 표시) — 양방향 도로는 arrows=false로 끔. '실척 고정' 간격(RoadArrowGapM ≈30m).
            int nArrow = 0;
            if (arrows)
            {
                nArrow = Mathf.Max(1, Mathf.RoundToInt(zLen / (RoadArrowGapM * StsConfig.ModelScale)));
                float arrowW = Mathf.Min(laneW * 0.30f, 2.0f * StsConfig.ModelScale);
                float arrowL = Mathf.Min(arrowW * 2.6f, (zLen / nArrow) * 0.7f);
                for (int k = 0; k < lanes; k++)
                {
                    float cx = xa + laneW * (k + 0.5f);
                    var av = new List<Vector3>(nArrow * 7); var at = new List<int>(nArrow * 9);
                    for (int a = 0; a < nArrow; a++)
                    {
                        float zc = zLo + zLen * (a + 0.5f) / nArrow;
                        float hl = arrowL * 0.4f, tl = arrowL * 0.6f;
                        AddMarkQuad(av, at, cx, zc - arrowL * 0.5f + tl * 0.5f, arrowW * 0.34f, tl);  // 꼬리
                        AddMarkTri (av, at, cx, zc - arrowL * 0.5f + tl, arrowW, hl);                 // 머리(삼각)
                    }
                    MarkMeshGO(root, "Road_Arrow", av, at, MarkY, white);
                }
            }
            Debug.Log($"[QuayGround] {label} — 폭 {w * StsConfig.InvModelScale:F1}m · {lanes}차로(각 {laneW * StsConfig.InvModelScale:F1}m), 점선 {nPer}·화살표 {nArrow}/차로 (각 단일 메시)");
        }

        // 노면 마킹 합치기(단일 메시) — 점선·화살표를 GameObject 폭증 없이 촘촘히 그리기 위한 누적 헬퍼(로컬 y=0 평면, +Y 향)
        static void AddMarkQuad(List<Vector3> v, List<int> t, float xc, float zc, float w, float len)
        {
            int i = v.Count; float hw = w * 0.5f, hl = len * 0.5f;
            v.Add(new Vector3(xc - hw, 0f, zc - hl)); v.Add(new Vector3(xc + hw, 0f, zc - hl));
            v.Add(new Vector3(xc + hw, 0f, zc + hl)); v.Add(new Vector3(xc - hw, 0f, zc + hl));
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
        // 삼각 화살촉 — 베이스 z=zBase(폭 w), 팁 z=zBase+len. +Y(윗면) 와인딩.
        static void AddMarkTri(List<Vector3> v, List<int> t, float xc, float zBase, float w, float len)
        {
            int i = v.Count; float hw = w * 0.5f;
            v.Add(new Vector3(xc, 0f, zBase + len));       // 팁(+Z)
            v.Add(new Vector3(xc - hw, 0f, zBase));        // 베이스 좌
            v.Add(new Vector3(xc + hw, 0f, zBase));        // 베이스 우
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
        }
        // 누적한 마킹 메시를 한 GameObject(콜라이더 없음, y 오프셋)로 생성.
        static void MarkMeshGO(Transform root, string name, List<Vector3> v, List<int> t, float y, Material mat)
        {
            if (v.Count == 0) return;
            var m = new Mesh { name = "Quay_Mark" };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // X방향 중앙 점선 — gaps(종방향 도로) 구간의 점선은 스킵(교차로 박스 비움).
        static void XDash(Transform root, float z, float x0, float x1, List<Vector2> gaps, float clr, Material mat)
        {
            float xmin = Mathf.Min(x0, x1), xmax = Mathf.Max(x0, x1), len = xmax - xmin;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / (RoadDashPeriodM * StsConfig.ModelScale)));
            float p = len / n, dl = p * RoadDashFill;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float xc = xmin + p * (i + 0.5f);
                bool ing = false;
                if (gaps != null) foreach (var g in gaps) if (xc > g.x - clr && xc < g.y + clr) { ing = true; break; }
                if (!ing) AddMarkQuad(v, t, xc, z, dl, MarkW);
            }
            MarkMeshGO(root, "Cross_LaneDash", v, t, MarkY, mat);
        }

        // 게이트(육지측 간선 위) — 캐노피 지붕 + 지지기둥 + 부스 + 정지선.
        static void BuildGate(Transform root, float xCenter, float zc, float roadW)
        {
            var grey  = MakeMat(new Color(0.62f, 0.63f, 0.66f), 0.2f, 0.30f, null);
            var booth = MakeMat(new Color(0.50f, 0.52f, 0.55f), 0.2f, 0.30f, null);
            float canopyX = roadW * 1.25f;   // 간선 폭보다 조금 넓게 덮음
            float postDx  = roadW * 0.55f;
            PbBox(root, "Gate_Canopy", new Vector3(xCenter, 0.11f, zc), new Vector3(canopyX, 0.012f, 0.14f), grey);
            foreach (float dx in new[] { -postDx, postDx })
                foreach (float dz in new[] { -0.06f, 0.06f })
                    Cyl(root, "Gate_Post", new Vector3(xCenter + dx, 0.055f, zc + dz), 0.006f, 0.11f, grey);
            PbBox(root, "Gate_Booth", new Vector3(xCenter, 0.05f, zc), new Vector3(0.04f, 0.09f, 0.04f), booth);
            var white = MakeMat(CMarkWhite, 0f, 0.20f, null);
            Stripe(root, "Gate_StopLine", new Vector3(xCenter, MarkY, zc - 0.09f), roadW, MarkW * 1.6f, white);
            // 횡단보도(zebra) — 게이트 반대편. 바는 통행방향(Z)과 평행, 도로폭(X)을 가로질러 배치.
            const int nBar = 6;
            for (int i = 0; i < nBar; i++)
            {
                float bx = xCenter - roadW * 0.5f + roadW * (i + 0.5f) / nBar;
                Stripe(root, "Gate_Crosswalk", new Vector3(bx, MarkY, zc + 0.12f), MarkW * 3f, 0.10f, white);
            }
        }

        // item 4: 바다 — 안벽 가장자리 바깥(바다측) 수면. 안벽 너머 남는 아스팔트를 덮고 외해로 확장.
        static void BuildSea(Transform root, float sizeX, float sizeZ, List<float> lanesX)
        {
            bool waterPos = WaterSideX >= 0f;
            float sgn = waterPos ? 1f : -1f;

            // 바다 안쪽 = '슬래브의 실제 바다측 끝'(로컬 +hx·−hx)과 직접 일치시킨다.
            //   종전엔 레일 X(quayEdge)로 유도했는데, 크레인 미발견/폴백 시 슬래브가 대칭(±hx)으로 커지면
            //   레일 기준 바다(≈0.5u 시작)가 슬래브(±6u) 위로 겹쳐 z-파이팅했다(사용자 렌더 확인). → 슬래브 끝을 직접 기준.
            float hx = sizeX * 0.5f;
            const float seaY = SeaLevelY;                      // 수면 = 데크 아래 코핑고(≈−4m). 안벽 전면이 그만큼 물 위로 드러난다.
            float seaInner = sgn * hx;                          // 슬래브 바다측 끝 = 바다 시작(크레인 유무 무관, 겹침 0·틈 0)
            float seaOuter = seaInner + sgn * SeaExtent;        // 개방 수면 바다측 끝(슬래브 끝에서 SeaExtent)
            float xa = Mathf.Min(seaInner, seaOuter), xb = Mathf.Max(seaInner, seaOuter);
            float seaWidth = xb - xa;
            float seaLen = sizeZ;                              // 안벽 길이와 일치(아스팔트=바다 길이)
            float seaCx = (xa + xb) * 0.5f;
            if (seaWidth < 0.02f) return;                      // 안벽이 슬래브 끝에 붙어 있으면 생략

            // 접수선(슬래브 맞닿는 선)의 메시-로컬 X — 이 선 부근은 파고를 죽여 벽면에 잔잔하게(데크 아래로) 안착.
            float innerLocalX = seaInner - seaCx;

            // 세분·변위된 수면 메시(다방향 스웰 + 잔물결). 메시가 실치수라 스케일=1.
            var sea = new GameObject(StsPartNames.QuaySea);
            sea.transform.SetParent(root, worldPositionStays: false);
            sea.transform.localPosition = new Vector3(seaCx, seaY, 0f);
            sea.AddComponent<MeshFilter>().sharedMesh = BuildSeaSurfaceMesh(seaWidth, seaLen, SeaTile, innerLocalX);

            // 머티리얼: 흰 베이스 × 색맵(수심 그라디언트·화이트캡, 전체 1회 매핑). 잔물결 글린트는 '메시 노멀'이 담당.
            //   종전 _BumpMap 타일링(×2)은 URP Lit이 _BumpMap_ST를 무시(LitInput.hlsl: 모든 맵이 _BaseMap_ST 공유 UV)해
            //   노멀맵이 바다 전체에 1회만 매핑돼 사실상 무효였다. → 노멀맵 제거하고 촘촘한 메시(≈2.4m 격자)+잔물결 높이로 글린트 표현.
            var seaMat = MakeMat(Color.white, 0.0f, 0.90f, BuildSeaColorTexture());
            // 색맵은 UV(타일 단위) 전체를 0..1로 눌러 바다 전체에 딱 한 번. sgn<0면 안벽쪽이 반대라 X를 뒤집는다.
            float bsx = SeaTile / seaWidth, bsy = SeaTile / seaLen;
            if (seaMat.HasProperty("_BaseMap"))
            {
                seaMat.SetTextureScale ("_BaseMap", new Vector2(sgn >= 0f ? bsx : -bsx, bsy));
                seaMat.SetTextureOffset("_BaseMap", new Vector2(sgn >= 0f ? 0f : 1f, 0f));
            }
            sea.AddComponent<MeshRenderer>().sharedMaterial = seaMat;

            // 수중 볼륨 — 수면 '아래'가 완전히 비어 있어, 낮은 시점·측면에서 보면 물 밑으로 세계가 뚫려 보였다
            //   (오너 지적 2026-08-10 "아래로 아무것도 없는데 채워"). 수면 바로 밑부터 해저(=안벽 저면 레벨)까지
            //   한 덩어리 박스로 채운다. 이 박스의 밑면이 곧 해저면이고, 옆면이 열린 단면을 막는다(박스 1개=드로우콜 1).
            //   ★ 상단은 파고 최저점(리플 −0.0018u)보다 아래로 내려 수면 메시를 뚫고 나오지 않게 한다.
            //   ★ X는 [슬래브 바다측 끝 → 외해 끝]이라 안벽 전면과 '면 접촉'만 하고 파고들지 않는다(겹침 0).
            float bedY    = -Thickness;                 // 해저 = 안벽 저면과 동일 레벨(케이슨 발치에서 맞물림)
            float bodyTop = seaY - 0.004f;              // 수면 아래 ≈10cm
            var bodyMat = MakeMat(CSeaBody, metallic: 0.0f, smooth: 0.05f, tex: null);
            QuayBox(root, "Sea_Body", new Vector3(seaCx, (bodyTop + bedY) * 0.5f, 0f),
                    new Vector3(seaWidth, bodyTop - bedY, seaLen), bodyMat);

            // 안벽 접수선 포말 — 슬래브 끝 바로 바깥(over water)에 '수면' 살짝 위로 얇게. 벽면과 겹치지 않는다.
            //   ★ 데크(y=0)가 아니라 수면(seaY) 기준 — 건현 4m가 생겼으므로 데크 높이에 두면 공중에 뜬다.
            //   ★ X는 '중심'이 아니라 '안쪽 변'을 벽면에 맞춘다 — 종전 0.015u 오프셋은 폭 0.05u(1.2m)의 절반보다
            //     작아서 0.010u(24cm)가 슬래브 안으로 물렸다(오너 지적: 포말이 육지에 걸쳐 있음). 벽이 얇을 땐
            //     데크 위 흰 줄로, 두꺼워진 지금은 콘크리트 속 매몰로 나타난다. 반폭만큼 밀어 [벽면, 벽면+폭]에 정확히 얹는다.
            var foam = MakeMat(CFoam, 0.0f, 0.12f, null);
            float foamX = seaInner + sgn * FoamW * 0.5f;   // 안쪽 변 = 슬래브 바다측 끝(겹침 0)
            Stripe(root, "Sea_Foam", new Vector3(foamX, seaY + 0.0012f, 0f), FoamW, seaLen, foam);

            Debug.Log($"[QuayGround] 바다 — 슬래브 바다측 끝부터 {seaWidth * StsConfig.InvModelScale:F1}m × 길이 {seaLen * StsConfig.InvModelScale:F1}m, " +
                      $"수면 y={seaY:F4}u(데크 아래 {StsConfig.QuayDeckAboveSeaMeters:F1}m), 안벽 노출고 {StsConfig.QuayDeckAboveSeaMeters:F1}m · 수심 {StsConfig.BerthWaterDepthMeters:F1}m. " +
                      $"수중 볼륨 Sea_Body y={bodyTop:F3}→{bedY:F3}u(두께 {(bodyTop - bedY) * StsConfig.InvModelScale:F1}m, 밑면=해저) 로 물 아래 공백 채움.");
        }

        // 세분·변위된 수면 메시 — 다방향 사인 스웰(모델 단위), 안벽 접수선은 파고를 죽여 잔잔.
        //   UV는 타일(SeaTile) 단위로 매겨 색 텍스처가 실척으로 반복되게 한다.
        static Mesh BuildSeaSurfaceMesh(float w, float len, float tile, float innerLocalX)
        {
            // 격자 밀도는 UV 타일과 분리 — 잔물결 글린트를 메시 노멀로 내려면 촘촘해야 한다(≈2.4m 격자).
            const float meshStep = 0.1f;
            int nx = Mathf.Max(2, Mathf.RoundToInt(w / meshStep));
            int nz = Mathf.Max(2, Mathf.RoundToInt(len / meshStep));
            float hw = w * 0.5f, hl = len * 0.5f;
            var v  = new List<Vector3>((nx + 1) * (nz + 1));
            var uv = new List<Vector2>((nx + 1) * (nz + 1));
            var t  = new List<int>(nx * nz * 6);
            for (int iz = 0; iz <= nz; iz++)
            for (int ix = 0; ix <= nx; ix++)
            {
                float x = -hw + w * ix / nx;
                float z = -hl + len * iz / nz;
                float k = Mathf.SmoothStep(0f, SeaDamp, Mathf.Abs(x - innerLocalX));  // 벽=0 → 외해=1
                // 접수선(k→0)은 SeaBaseDrop만큼 데크 아래로 내려 벽면에 잔잔히 닿고, 외해(k→1)로 갈수록 스웰이 살아난다.
                float y = SeaLift + SeaBaseDrop * (1f - k) + SeaHeight(x, z) * k;
                v.Add(new Vector3(x, y, z));

                uv.Add(new Vector2((x + hw) / tile, (z + hl) / tile));
            }
            int stride = nx + 1;
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                int a = iz * stride + ix, b = a + 1, c = a + stride, d = c + 1;
                t.Add(a); t.Add(c); t.Add(b);   // +Y(윗면) 와인딩
                t.Add(b); t.Add(c); t.Add(d);
            }
            // 촘촘한 격자라 긴 안벽(500m+)에서 정점>65535 가능 → 32bit 인덱스로 안전화.
            var m = new Mesh { name = "Quay_Sea", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // 잔잔한 항내 스웰 높이(모델 단위) — 다방향 사인 합을 지면 위로만 융기(≥0)시킨다.
        //   골이 지면 아래로 안 내려가 겹친 아스팔트가 삐져나오지 않음. 마루 0.012u(≈0.29m) ≤ 연석 여유.
        static float SeaHeight(float x, float z)
        {
            const float TwoPi = 6.2831853f;
            float s = 0f;
            s += 0.42f * Mathf.Sin(x * (TwoPi / 0.50f) + 0.0f);
            s += 0.25f * Mathf.Sin((x * 0.7f + z * 0.7f)  * (TwoPi / 0.33f) + 1.7f);
            s += 0.33f * Mathf.Sin((x * 0.3f - z * 0.95f) * (TwoPi / 0.70f) + 3.1f);
            float swell = 0.012f * (s * 0.5f + 0.5f);   // s∈[-1,1] → [0, 0.012] 스웰
            // 잔물결(글린트) — 노멀맵(URP _BumpMap_ST 무시됨) 대신 촘촘한 메시 노멀로 태양광 반짝임을 만든다.
            //   파장 ≈8.4·6.7m, 소진폭. 메시 step(0.1u≈2.4m)이 이 파장을 3~4샘플로 잡아 RecalculateNormals가 기울기를 반영.
            float ripple = 0.0018f * (Mathf.Sin(x * (TwoPi / 0.35f))
                                    + Mathf.Sin((x * 0.5f + z * 0.87f) * (TwoPi / 0.28f) + 0.9f));
            return swell + ripple;
        }

        // 수면 색 텍스처(컬러, 바다 전체 1회 매핑) — 수심 그라디언트(안벽쪽 얕은 청록 → 외해 짙은 남색)
        //   + 저주파 스웰 색 얼룩 + 외해쪽 드문 화이트캡(흰 물마루). x축=안벽→외해, 안벽은 x=0.
        static readonly Color CSeaShallow = new Color(0.10f, 0.32f, 0.34f);  // 얕은 물(안벽쪽) 청록
        static readonly Color CSeaDeep    = new Color(0.03f, 0.11f, 0.20f);  // 깊은 물(외해) 남색
        static Texture2D _seaTex;
        static Texture2D BuildSeaColorTexture()
        {
            if (_seaTex != null) return _seaTex;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Quay_SeaTex", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float t = x / (float)(N - 1);                                   // 0=안벽(얕음) → 1=외해(깊음)
                var c = Color.Lerp(CSeaShallow, CSeaDeep, Mathf.SmoothStep(0f, 1f, t));
                // 저주파 스웰 색 얼룩(값 ±)
                float swell = Mathf.PerlinNoise(x * 0.012f, y * 0.012f) * 0.6f
                            + Mathf.PerlinNoise(x * 0.035f + 40f, y * 0.035f) * 0.4f;
                float m = 1f + (swell - 0.5f) * 0.35f;
                c.r *= m; c.g *= m; c.b *= m;
                // 외해쪽 드문 화이트캡(흰 물마루) — 얕은 물엔 거의 없음
                float fleck = Mathf.PerlinNoise(x * 0.25f + 7f, y * 0.25f + 3f);
                float capThresh = Mathf.Lerp(1.02f, 0.80f, t);                  // 외해일수록 문턱 낮음(더 자주)
                if (fleck > capThresh)
                    c = Color.Lerp(c, new Color(0.82f, 0.88f, 0.90f), Mathf.InverseLerp(capThresh, 1f, fleck) * 0.9f);
                px[y * N + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _seaTex = tex;
        }

        // item 2: 계선주·연석 — 바다측 안벽
        //   WaterSideX = 바다측 부호(+X 가정=트롤리 Max 관례). 렌더 확인 후 어긋나면 -1로 뒤집는다.
        //   (배수 트렌치는 BuildRoadNetwork에서 안벽 간선 뒤에 그린다.)
        static void BuildMooringEdge(Transform root, float sizeX, float sizeZ, List<float> lanesX)
        {
            float hx = sizeX * 0.5f;
            bool waterPos = WaterSideX >= 0f;
            float sgn = waterPos ? 1f : -1f;
            float len = sizeZ * 0.98f;   // QuayRail 길이로 통일

            // 크레인 위치 기준 슬래브 끝이 아니라 바다측 크레인 레일(=다리 접지선) X에 상대 배치한다.
            //   붐이 바다쪽으로 뻗어 슬래브 중심이 치우치므로, 안벽 가장자리·계선주는 반드시 바다측 레일 기준으로 둔다.
            float waterRailX = sgn * RailGauge * 0.5f;     // 폴백(크레인 없을 때)
            if (lanesX != null && lanesX.Count > 0)
            {
                waterRailX = lanesX[0];
                for (int i = 1; i < lanesX.Count; i++)
                    waterRailX = waterPos ? Mathf.Max(waterRailX, lanesX[i]) : Mathf.Min(waterRailX, lanesX[i]);
            }
            float edgeMargin = QuayEdgeMargin;  // 바다측 레일 → 안벽(quay) 가장자리 ≈4m (SSOT)
            float quayEdgeX = Mathf.Clamp(waterRailX + sgn * edgeMargin, -(hx - 0.02f), hx - 0.02f);

            // 연석(緣石) — 안벽 가장자리 따라 낮은 콘크리트 턱. Ground 메뉴 토글로 선택.
            //   4m 프리캐스트 유닛 FBX 를 피치대로 깐다. 단면이 일정한 직선이라 통짜 압출로도 형상은
            //   같지만, 줄눈이 없으면 VR에서 안벽 376m의 길이감이 죽는다. FBX 없으면 종전 통짜로 폴백.
            if (IncludeCurb)
            {
                var concrete = MakeMat(CConcrete, 0.0f, 0.15f, null);
                const float curbW = 0.03f, curbH = 0.022f;
                var curbFbx = AssetDatabase.LoadAssetAtPath<GameObject>(CurbFbx);
                if (curbFbx != null)
                {
                    float pitch = CurbUnitPitchM * StsConfig.ModelScale;
                    // 내림 — 올림하면 마지막 유닛이 안벽 끝을 최대 반피치(2m) 넘어 바다로 튀어나온다.
                    int   units = Mathf.FloorToInt(len / pitch);   // 4m 유닛이 하나도 안 들어가면 연석 없음
                    float run   = pitch * units;                                // 실제 덮는 길이 — 안벽 중앙 정렬
                    float s     = FbxScaleByHeight(curbFbx, RealCurbHeightM);   // 루프 밖에서 1회 측정
                    var group   = new GameObject("Quay_Curb").transform;        // 유닛 수십 개가 루트에 흩어지지 않게
                    group.SetParent(root, worldPositionStays: false);
                    for (int i = 0; i < units; i++)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(curbFbx);
                        go.name = "Quay_CurbUnit";
                        go.transform.SetParent(group, worldPositionStays: false);
                        // FBX 원점 = 바닥면·유닛 길이 중앙
                        go.transform.localPosition = new Vector3(quayEdgeX, 0f, -run * 0.5f + pitch * (i + 0.5f));
                        go.transform.localScale    = Vector3.one * s;
                        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                            r.sharedMaterial = concrete;
                    }
                }
                else
                    PbBox(root, "Quay_Curb", new Vector3(quayEdgeX, curbH * 0.5f, 0f), new Vector3(curbW, curbH, len), concrete);
            }

            // 계선주(bollard) — 연석 살짝 안쪽(육지쪽)으로 줄지어. 실척 ~20m 간격. Ground 메뉴 토글로 선택.
            if (IncludeBollard)
            {
                var bollMat = MakeMat(CBollard, 0.5f, 0.30f, null);
                float gapZ = 20f * StsConfig.ModelScale;   // ≈0.83
                int n = Mathf.Max(2, Mathf.RoundToInt(len / gapZ));
                float bx = quayEdgeX - sgn * 0.045f;       // 가장자리에서 안쪽(육지)으로
                const float postH = 0.045f, postR = 0.009f, capH = 0.012f, capR = 0.014f;
                var bollFbx = AssetDatabase.LoadAssetAtPath<GameObject>(BollardFbx);
                float bollScale = FbxScaleByHeight(bollFbx, RealBollardHeightM);   // 루프 밖에서 1회 측정
                for (int i = 0; i <= n; i++)
                {
                    float z = -len * 0.5f + len * i / n;
                    if (bollFbx != null)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(bollFbx);
                        go.name = "Quay_Bollard";
                        go.transform.SetParent(root, worldPositionStays: false);
                        go.transform.localPosition = new Vector3(bx, 0f, z);   // FBX 원점 = 바닥
                        go.transform.localScale    = Vector3.one * bollScale;
                        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                            r.sharedMaterial = bollMat;                        // URP 머티리얼은 기존 것 재사용
                        continue;
                    }
                    Cyl(root, "Quay_Bollard", new Vector3(bx, postH * 0.5f, z), postR, postH, bollMat);
                    Cyl(root, "Quay_BollardCap", new Vector3(bx, postH + capH * 0.5f, z), capR, capH, bollMat);
                }
            }
        }

        /// <summary>파일럿 검증 — 씬의 기존 절차생성 계선주 옆에 블렌더 FBX 계선주를 1개 놓는다.
        /// 스케일·피벗·머티리얼·이름 4가지가 한 화면에서 비교된다. 확인 뒤 지우면 된다.</summary>
        [MenuItem("Model/FBX/부두/계선주 파일럿 비교 (기존 옆에 1개)", false, 20)]
        static void BollardPilot()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(BollardFbx);
            if (fbx == null)
            {
                EditorUtility.DisplayDialog("계선주 파일럿",
                    $"FBX를 찾을 수 없습니다:\n{BollardFbx}\n\n유니티 창을 한 번 포커스해 임포트되게 하세요.", "확인");
                return;
            }

            // 기존 절차생성 계선주 하나를 기준점으로 삼는다(없으면 원점).
            var refGo = GameObject.Find(RootName)?.transform.Find("Quay_Bollard");
            Transform parent = refGo != null ? refGo.parent : GameObject.Find(RootName)?.transform;
            Vector3 pos = refGo != null ? refGo.localPosition : Vector3.zero;
            pos.y = 0f;                       // FBX 원점 = 바닥
            pos.z += 20f * StsConfig.ModelScale;   // 실척 20m 옆(= 계선주 간격)에 나란히

            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            go.name = "Quay_Bollard_FBX_Pilot";
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localScale    = Vector3.one * FbxScaleByHeight(fbx, RealBollardHeightM);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = MakeMat(CBollard, 0.5f, 0.30f, null);

            Undo.RegisterCreatedObjectUndo(go, "Bollard FBX Pilot");
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();

            float hU = RtgCraneFbxPlacer.CombinedBounds(go).size.y;
            Debug.Log($"[계선주 파일럿] 배치 완료 — localScale {go.transform.localScale.x:F4}, " +
                      $"실측 높이 {hU:F4}u(={hU * StsConfig.InvModelScale:F3}m, 목표 {RealBollardHeightM:F3}m). " +
                      $"부모 '{(parent != null ? parent.name : "(없음)")}'. 기존 절차생성 계선주와 나란히 놓았습니다.");
        }

        /// <summary>FBX 인스턴스를 '실척 높이 × ModelScale' 로 맞추는 배율. FBX 단위계(m/cm)를 몰라도
        /// 측정으로 수렴하므로 RTG_Crane.fbx 와 동일하게 임포트 설정 변화에 영향받지 않는다.
        /// 계선주·연석 등 블렌더 제작 부재가 공통으로 쓴다.</summary>
        static float FbxScaleByHeight(GameObject fbx, float realHeightM)
        {
            if (fbx == null) return 1f;
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            probe.transform.localScale = Vector3.one;
            float h = RtgCraneFbxPlacer.CombinedBounds(probe).size.y;
            Object.DestroyImmediate(probe);
            float target = realHeightM * StsConfig.ModelScale;
            return h > 1e-5f ? target / h : 1f;
        }

        // 원시 실린더(콜라이더 없음) — 계선주 등 둥근 부재용. height=전체높이, center=수직 중심.
        static void Cyl(Transform parent, string name, Vector3 center, float radius, float height, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = center;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ProBuilder 박스(콜라이더 없음) — 신규 디자인 부재(연석·배수 채널)용. 디자이너가 ProBuilder로 바로 편집.
        //   StsCraneCreator.PbBox와 동일 패턴(ShapeGenerator.GenerateCube). 베벨/면다듬기는 디자이너가 시각 편집.
        static GameObject PbBox(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var pb = UnityEngine.ProBuilder.ShapeGenerator.GenerateCube(UnityEngine.ProBuilder.PivotLocation.Center, size);
            pb.name = name;
            pb.transform.SetParent(parent, worldPositionStays: false);
            pb.transform.localPosition = localPos;
            pb.ToMesh();
            pb.Refresh();
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
            var col = pb.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            return pb.gameObject;
        }

        static Material MakeMat(Color c, float metallic, float smooth, Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "Quay_Mat" };
            mat.enableInstancing = true;   // 연석 유닛·계선주가 같은 머티리얼 → 드로우콜 폭증 방지
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            return mat;
        }

        // 절차 아스팔트 텍스처(그레이스케일, _BaseColor 곱) — 저주파 얼룩 + 골재 그레인 + 드문 밝은 골재.
        //   정적 캐시 — 종전엔 재생성마다 256² 텍스처를 새로 구워 구 텍스처를 누수했다(Sea 텍스처는 캐시됨).
        static Texture2D _asphaltTex;
        static Texture2D BuildAsphaltTexture()
        {
            if (_asphaltTex != null) return _asphaltTex;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Quay_Asphalt", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 시일코트 불균일(저주파 얼룩)
                float patch = Mathf.PerlinNoise(x * 0.012f, y * 0.012f) * 0.5f
                            + Mathf.PerlinNoise(x * 0.030f + 50f, y * 0.030f) * 0.3f
                            + Mathf.PerlinNoise(x * 0.080f + 120f, y * 0.080f) * 0.2f;
                // 골재 그레인(미세 스페클)
                float grain = Hash(x, y);
                // 드문드문 밝은 골재(돌)
                float stone = grain > 0.94f ? 0.18f : 0f;

                float v = 0.86f + (patch - 0.5f) * 0.22f + (grain - 0.5f) * 0.26f + stone;
                byte bb = (byte)(Mathf.Clamp01(v) * 255f);
                px[y * N + x] = new Color32(bb, bb, bb, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _asphaltTex = tex;
        }

        /// <summary>
        /// 부두에서 '걷는 면'(아스팔트 슬래브) 렌더러를 반환. 부두를 못 찾으면 null.
        ///
        /// ★ 이 프로젝트의 반복 사고 지점이다. 종전엔 호출부마다 "수평 면적이 가장 넓은 렌더러 = 슬래브"라는
        ///   휴리스틱을 각자 복사해 썼는데, **바다(6u×16u ≈ 96u²)가 아스팔트(3.9u×16u ≈ 62u²)보다 넓다.**
        ///   수면이 데크와 같은 높이일 땐 우연히 무해했지만 수면을 −4m로 내린 뒤로는 크레인·RTG·플레이어가
        ///   바다 한가운데 수면 높이에 놓인다(오너 지적 2026-08-10 "RTG 크레인이 바다에 출력된다").
        ///   → 이름('Asphalt') 1순위 + 바다 계열 제외. 새 호출부는 반드시 이 헬퍼를 쓸 것(복사 금지).
        /// </summary>
        public static Renderer FindQuayDeckRenderer(GameObject quay = null)
        {
            if (quay == null) quay = GameObject.Find(RootName);
            if (quay == null) return null;
            Renderer best = null; float bestArea = 0f;
            foreach (var r in quay.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name == StsPartNames.QuayAsphalt) return r;   // 이름 1순위(확정)
                if (StsPartNames.IsSeaName(r.gameObject.name)) continue;        // 바다·포말 제외
                Vector3 e = r.bounds.size;
                float area = e.x * e.z;
                if (area > bestArea) { bestArea = area; best = r; }
            }
            return best;
        }

        /// <summary>
        /// 야드 블록 존(YardBlock_Zone) 렌더러를 '안벽에 가까운 순'(바다측 = 큰 X, 같은 열이면 Z 오름차순)으로 반환.
        /// 부두가 없거나 도로 토글이 꺼져 블록을 안 그렸으면 빈 리스트.
        ///
        /// ★ RTG 배치부(절차·FBX 양쪽)는 야드 좌표 '산식을 복사하지 말고' 이 실측 목록을 쓴다 — 종전엔 야드 X·블록 Z
        ///   산식을 RTG 파일에 다시 적어 두 곳이 갈라졌고, 부두 수식을 고칠 때마다 RTG만 엉뚱한 자리에 남았다.
        /// </summary>
        public static List<Renderer> FindYardBlockZones(GameObject quay = null)
        {
            var list = new List<Renderer>();
            if (quay == null) quay = GameObject.Find(RootName);
            if (quay == null) return list;
            foreach (var r in quay.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name == StsPartNames.YardBlockZone) list.Add(r);
            bool waterPos = WaterSideX >= 0f;
            list.Sort((a, b) =>
            {
                float ax = a.bounds.center.x, bx = b.bounds.center.x;
                int byX = waterPos ? bx.CompareTo(ax) : ax.CompareTo(bx);   // 안벽(바다측)에 가까운 블록 먼저
                return byX != 0 ? byX : a.bounds.center.z.CompareTo(b.bounds.center.z);
            });
            return list;
        }

        /// <summary>
        /// 씬에 이미 깔린 Quay_Ground 에서 '바다측' QuayRail 월드 X 와 안벽 중앙 Z 를 읽는다. 부두가 없거나
        /// QuayRail 이 없으면 false.
        ///
        /// ★ 안벽 위치의 SSOT 는 '부두'다 — 크레인이 아니다. 접안(ShipBerthMenu)이 크레인에만 의존하면,
        ///   STS 없이 부두만 있는 씬에서 배가 원점(=슬래브 한가운데 육지)에 떨어진다(오너 지적 2026-08-10
        ///   "컨테이너선 생성하면 육지로 출력된다"). 부두는 항상 있으므로 이걸 1순위 앵커로 쓴다.
        /// </summary>
        public static bool TryQuayBerthAnchor(out float waterRailX, out float centerZ)
        {
            waterRailX = 0f; centerZ = 0f;
            var quay = GameObject.Find(RootName);
            if (quay == null) return false;

            bool waterPos = WaterSideX >= 0f;
            bool found = false; float zSum = 0f; int zn = 0;
            foreach (var t in quay.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != StsPartNames.QuayRail) continue;
                float x = t.position.x;
                waterRailX = !found ? x : (waterPos ? Mathf.Max(waterRailX, x) : Mathf.Min(waterRailX, x));
                zSum += t.position.z; zn++;
                found = true;
            }
            if (!found) return false;
            centerZ = zSum / zn;    // 레일은 안벽 중앙(Z=0 로컬)에 놓이므로 평균 = 선석 중앙
            return true;
        }

        // 크레인 Rail_* 중 바다측(waterPos=+X, 아니면 -X) 주행레일의 월드 X. 못 찾으면 NaN.
        static float WaterRailWorldX(GameObject crane, bool waterPos)
        {
            float rx = float.NaN;
            foreach (var t in crane.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(StsPartNames.RailPrefix))
                    rx = float.IsNaN(rx) ? t.position.x
                                         : (waterPos ? Mathf.Max(rx, t.position.x) : Mathf.Min(rx, t.position.x));
            return rx;
        }

        // 자식 Renderer들의 월드 AABB.
        static bool TryWorldBounds(Transform t, out Bounds b)
        {
            b = default;
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }

        // 결정적 정수 해시 → [0,1) (그레인용)
        static float Hash(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
        }
    }
}
#endif
