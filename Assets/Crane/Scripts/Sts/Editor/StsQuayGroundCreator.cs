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
    /// - 시각 전용(콜라이더 없음) — 물리 바닥은 기존 VirtualFloor 가 담당.
    ///
    /// 생성 머티리얼/텍스처는 인스턴스(에셋 미저장)라 프로젝트 머티리얼을 오염시키지 않는다.
    /// </summary>
    public static class StsQuayGroundCreator
    {
        const string RootName  = StsPartNames.QuayGround;
        const string CraneName = "STS_Crane";

        // 기본 치수(크레인을 못 찾을 때) — X=apron(붐 방향), Z=안벽 길이(레일 방향), m
        const float DefaultSizeX = 12.0f;   // [선박] 5→12 (실척 ≈288m) — 360m 안벽에 맞춰 apron(육지) 폭 확대
        const float DefaultSizeZ = 20.0f;   // [선박] 풀사이즈 컨테이너선 접안 + 크레인 3~5대 → 480m급 안벽(바다 길이와 일치)
        const float MarginX      = 2.0f;    // X(붐 방향) 여유 — 0.8→2.0 (실척 ≈48m 사방 추가). ※크레인 있으면 미사용(아래 에이프런 기준으로 대체), 크레인 없을 때 폴백용만
        const float MarginZ      = 0.8f;    // Z(레일 방향) 여유 — 원래대로
        // [배치 2026-07-02] 슬래브 X는 '크레인 바운즈 중심'이 아니라 '안벽 가장자리' 기준으로 잡는다.
        //   종전엔 붐 아웃리치가 바운즈 중심을 바다쪽으로 끌어 슬래브 절반(≈143m)이 바다 밑에 깔려 낭비됐음(사용자 지시로 제거).
        //   → 안벽 가장자리에서 육지측으로 ApronDepth 만큼만 슬래브를 깔고, 바다측은 바다 시작선까지만 딱 맞춘다.
        const float ApronDepth        = 6.0f;  // 안벽 가장자리→육지측 에이프런 깊이(≈144m: 백리치+트럭도로+야드 10줄 수용)
        // [겹침수정 2026-07-02] 슬래브 바다측 끝 = 바다 시작선(seaInner)과 일치. 종전 0.25u(≈6m)는 슬래브가 바다면(y≈0)
        //   밑으로 그만큼 파고들어 z-파이팅 겹침을 냈음(사용자 지적) → 바다 안쪽 시작 오프셋(≈0.12m)만 남겨 겹침 0.
        //   BuildSea 의 seaInner 도 이 상수를 써 슬래브 끝 = 바다 시작이 항상 붙어 있게 한다(SSOT).
        const float SlabSeawardMargin = 0.005f; // 안벽 가장자리에서 바다측 시작선까지의 오프셋(=slab 끝=sea 시작, 겹침 없음)
        // 바다측 레일 → 안벽(quay) 가장자리 ≈4m — CreateFromMenu·BuildSea·BuildMooringEdge 공유 SSOT.
        const float QuayEdgeMargin    = 4f * StsConfig.ModelScale;
        // 부두 Z 길이 기여분 — '고정'. [선박 2026-06-19] 294m 배 + 크레인 3~5대가 길이방향으로 늘어서야 하므로
        //   기존 3.0u(72m)→13u(312m)로 확대. 크레인 있으면 sizeZ = 크레인Z + 여유 + QuayZSpan ≈ 15u(360m).
        const float QuayZSpan    = 18.0f;
        const float Thickness    = 0.08f;   // 슬래브 두께(가장자리 단차)
        const float TileMeters   = 0.5f;    // 아스팔트 텍스처 1타일 = 0.5m

        const float RailGauge    = StsConfig.LegGaugeXMeters * StsConfig.ModelScale; // [H2] 크레인 레일 게이지와 동일 SSOT(=StsCraneCreator.LegSpanX) — 크레인 없을 때 폴백 차선용
        const float LaneOffset   = 0.045f;   // 레일 중심에서 차선까지(양옆)
        const float LaneWidth    = 0.014f;   // 차선 폭
        const float LaneY        = 0.0016f;  // 아스팔트 윗면 바로 위(z-fighting 방지)

        static readonly Color CAsphalt = new Color(0.205f, 0.20f, 0.215f); // 어두운 중성 회색
        static readonly Color CPaint   = new Color(0.88f, 0.74f, 0.10f);   // 안전 노랑 차선
        static readonly Color CRail    = new Color(0.55f, 0.58f, 0.62f);   // 강철 레일(크레인 Rail 색과 동일)

        const float RailY      = StsConfig.RailSectionH * 0.5f;  // [H2] 레일 중심 Y = 단면높이/2 (크레인 짧은 레일과 일치, =0.004)
        const float RailH      = StsConfig.RailSectionH;         // [H2] 레일 단면 높이 SSOT
        const float RailW      = StsConfig.RailSectionW;         // [H2] 레일 단면 폭(X) SSOT

        // ── item 1: 노면 마킹(컨테이너 야드 그리드·트럭 차로·화살표) ──
        const float MarkY = 0.0018f;   // 마킹 Y(아스팔트 위; 노란 차선 LaneY 0.0016보다 약간 위로 분리)
        const float MarkW = 0.006f;    // 그리드/차로 선폭(≈0.14m 실척)
        static readonly Color CMarkWhite = new Color(0.85f, 0.85f, 0.82f);  // 흰색 노면 페인트
        // 차선 점선·진행 화살표 간격 — '실척 고정'(도로 길이와 무관). 종전엔 화살표가 개수 상한(4개)이라 긴 도로에서 벌어졌다.
        const float RoadDashPeriodM = 8f;    // 점선 주기(실척 m) — 정수개로 맞추되 이 값에 근접 고정
        const float RoadDashFill    = 0.5f;  // 점선 길이 비율(주기의 50%) → 점선 4m + 간격 4m(종전 3:9보다 촘촘)
        const float RoadArrowGapM   = 30f;   // 진행 화살표 간격(실척 m) — 상한 없이 고정 간격
        const float RoadLaneWidthM  = 4f;    // 차로 1개 폭(실척 m) — '고정'(auto 분할용). 백리치는 포털차로폭 종속이라 미사용이지만 야드 등 auto 대비 유지

        // ── item 2: 계선주·연석·배수(바다측 안벽) ──
        //   WaterSideX = 바다측 부호(+X=트롤리 Max 관례). 렌더 확인 후 어긋나면 -1로 뒤집는다.
        const float WaterSideX = 1f;
        // [선박 2026-06-19] 바다 폭 — 안벽 가장자리에서 바다측으로 확장(배가 뜰 개방 수면). 8u≈192m: 선폭 39.5m + 여유.
        const float SeaExtent  = 8f;
        static readonly Color CConcrete = new Color(0.60f, 0.59f, 0.56f);   // 연석 콘크리트
        static readonly Color CBollard  = new Color(0.16f, 0.17f, 0.19f);   // 계선주 캐스트강(차콜)
        static readonly Color CDrain    = new Color(0.10f, 0.10f, 0.11f);   // 배수 채널(어둠)
        static readonly Color CFoam     = new Color(0.74f, 0.80f, 0.82f);   // 안벽 접수선 포말(whitewater)
        const float SeaTile  = 0.25f;   // 수면 파형 격자·색 텍스처 1타일 = 0.25u(≈6m)
        const float SeaDamp  = 0.4f;    // 안벽에서 이 거리(≈10m) 안은 파고를 죽여 잔잔(포말 평탄 안착)
        const float SeaLift  = 0.0006f; // 지면(y=0) 위 여유(≈1.4cm) — 겹친 아스팔트와 z-fighting 방지
        const float FoamW    = 0.05f;   // 접수선 포말 폭(≈1.2m)
        // [겹침제거 #4] 바다는 슬래브 바다측 끝과 '정확히 맞닿게'(면 겹침 0·틈 0, 같은 선). 밑으로 밀어넣지 않는다(tuck-under 폐기).
        //   접수선 sliver(솟음)는 SeaBaseDrop로만 방지 — 벽면 수면을 데크(y=0) 아래로 내려 물이 안벽 코핑 살짝 아래에서 닿게 한다(현실적).
        const float SeaBaseDrop = -0.003f;  // 접수선(잔잔대) 수면을 데크 아래로(≈−7cm) — 물이 벽면에 닿는 선, 겹침 아님

        static Mesh _unitQuad;

        // ── Ground 메뉴 선택 항목(토글) — EditorPrefs로 보존. '부두 바닥 생성' 시 켜진 것만 만든다. ──
        const string MenuSea     = "Ground/바다";
        const string MenuRoad    = "Ground/도로";
        const string MenuYard    = "Ground/컨테이너 주차장";
        const string MenuCurb    = "Ground/연석 (Curb)";
        const string MenuBollard = "Ground/계선주 (Bollard)";
        const string MenuDecor   = "Ground/항구 꾸미기 (육지측: 야적·조명탑·펜스)";
        static bool IncludeSea     { get => EditorPrefs.GetBool("QuayGround.sea",     true); set => EditorPrefs.SetBool("QuayGround.sea",     value); }
        static bool IncludeRoad    { get => EditorPrefs.GetBool("QuayGround.road",    true); set => EditorPrefs.SetBool("QuayGround.road",    value); }
        static bool IncludeYard    { get => EditorPrefs.GetBool("QuayGround.yard",    true); set => EditorPrefs.SetBool("QuayGround.yard",    value); }
        static bool IncludeCurb    { get => EditorPrefs.GetBool("QuayGround.curb",    true); set => EditorPrefs.SetBool("QuayGround.curb",    value); }
        static bool IncludeBollard { get => EditorPrefs.GetBool("QuayGround.bollard", true); set => EditorPrefs.SetBool("QuayGround.bollard", value); }
        static bool IncludeDecor   { get => EditorPrefs.GetBool("QuayGround.decor",   true); set => EditorPrefs.SetBool("QuayGround.decor",   value); }

        [MenuItem(MenuSea, false, 20)]     static void ToggleSea()     => IncludeSea     = !IncludeSea;
        [MenuItem(MenuSea, true)]          static bool ToggleSeaV()    { Menu.SetChecked(MenuSea, IncludeSea); return true; }
        [MenuItem(MenuRoad, false, 21)]    static void ToggleRoad()    => IncludeRoad    = !IncludeRoad;
        [MenuItem(MenuRoad, true)]         static bool ToggleRoadV()   { Menu.SetChecked(MenuRoad, IncludeRoad); return true; }
        [MenuItem(MenuYard, false, 22)]    static void ToggleYard()    => IncludeYard    = !IncludeYard;
        [MenuItem(MenuYard, true)]         static bool ToggleYardV()   { Menu.SetChecked(MenuYard, IncludeYard); return true; }
        [MenuItem(MenuCurb, false, 23)]    static void ToggleCurb()    => IncludeCurb    = !IncludeCurb;
        [MenuItem(MenuCurb, true)]         static bool ToggleCurbV()   { Menu.SetChecked(MenuCurb, IncludeCurb); return true; }
        [MenuItem(MenuBollard, false, 24)] static void ToggleBollard() => IncludeBollard = !IncludeBollard;
        [MenuItem(MenuBollard, true)]      static bool ToggleBollardV(){ Menu.SetChecked(MenuBollard, IncludeBollard); return true; }
        [MenuItem(MenuDecor, false, 25)]   static void ToggleDecor()   => IncludeDecor   = !IncludeDecor;
        [MenuItem(MenuDecor, true)]        static bool ToggleDecorV()  { Menu.SetChecked(MenuDecor, IncludeDecor); return true; }

        [MenuItem("Ground/부두 바닥 생성 (선택 항목)", false, 1)]
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
                    //   [E 수정] gantry.Min/Max는 크레인-로컬 Z(localPosition 기준)다. 종전엔 이 로컬값을 월드 center.z로
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
                    // 바닥 Z 길이는 갠트리 주행거리(gantryRange)와 분리된 고정값(QuayZSpan) — 주행범위를 키워도 바닥은 안 커진다.
                    sizeZ = Mathf.Max(DefaultSizeZ, b.size.z + MarginZ * 2f + QuayZSpan);

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

            Selection.activeGameObject = root;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
            Debug.Log($"[QuayGround] 아스팔트 부두 바닥 생성 — {sizeX:F2}m × {sizeZ:F2}m, 윗면 y=0, 중심 {center}. " +
                      (crane != null ? $"크레인 레일에 맞춰 긴 트랙 + 안전 차선 표시. 갠트리 주행범위를 레일 끝까지 자동 확장(총 {gantryRange:F2}m, 양쪽 대칭)." : "크레인 없음 → 기본 차선."));
        }

        static GameObject Build(Vector3 center, float sizeX, float sizeZ, GameObject crane)
        {
            var root = new GameObject(RootName);
            root.transform.position = center;

            // 1) 아스팔트 슬래브 — 윗면 y=0, 아래로 Thickness
            var slab = new GameObject("Asphalt");
            slab.transform.SetParent(root.transform, worldPositionStays: false);
            slab.AddComponent<MeshFilter>().sharedMesh = BuildSlab(sizeX, sizeZ, Thickness, TileMeters);
            slab.AddComponent<MeshRenderer>().sharedMaterial =
                MakeMat(CAsphalt, metallic: 0.0f, smooth: 0.12f, tex: BuildAsphaltTexture());

            // 2) 레일 안전 차선(노랑) — 크레인 레일 X를 찾아 양옆에, 없으면 기본 게이지
            //   ※ "Rail_" 접두사는 러닝레일 마커(Rail_Land/Rail_Water)만이 아니라 보기의 레일 스위퍼(Rail_Sweeper,
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
                // [배치 재정의] 크레인이 없으면 레일을 슬래브 한가운데(±게이지/2)가 아니라 '바다측 끝 기준' 안벽 위치에 둔다.
                //   → Lane/QuayRail/Quay_Curb/Quay_Bollard(모두 lanesX 파생)가 물가에 정렬. 크레인 있을 땐 apron 사이징으로
                //   실제 레일이 바로 이 자리에 오므로 두 경우가 일치한다.
                bool wp = WaterSideX >= 0f; float sg = wp ? 1f : -1f;
                float hxL = sizeX * 0.5f;
                float waterRailLocal = sg * (hxL - SlabSeawardMargin - QuayEdgeMargin);  // 바다측 레일 = 안벽 끝에서 4m 안쪽
                float landRailLocal  = waterRailLocal - sg * RailGauge;                   // 육지측 레일 = 게이지만큼 육지측
                lanesX.Add(landRailLocal);
                lanesX.Add(waterRailLocal);
            }

            var paint = MakeMat(CPaint, metallic: 0.0f, smooth: 0.25f, tex: null);
            foreach (float lx in lanesX)
                foreach (float off in new[] { -LaneOffset, LaneOffset })
                    Stripe(root.transform, "Lane", new Vector3(lx + off, LaneY, 0f),
                           LaneWidth, sizeZ * 0.98f, paint);

            // 3) 고정 긴 레일(트랙) — 부두에 고정되어 있고 크레인이 그 위를 굴러간다(크레인 측 Rail_*는 렌더러 OFF).
            //    크레인의 Rail_ Transform이 정해준 X를 그대로 사용해 정렬.
            if (lanesX.Count > 0)
            {
                var railMat = MakeMat(CRail, metallic: 0.7f, smooth: 0.35f, tex: null);
                float trackLen = sizeZ * 0.98f;
                foreach (float lx in lanesX)
                    QuayBox(root.transform, "QuayRail", new Vector3(lx, RailY, 0f),
                            new Vector3(RailW, RailH, trackLen), railMat);
            }

            // 5) 계선주·연석 — 바다측 안벽(항상 생성). 크레인 레일 기준 상대 배치.
            BuildMooringEdge(root.transform, sizeX, sizeZ, lanesX);

            // 4·6·7) 도로·보관소·바다 — Ground 메뉴 토글로 선택 생성
            if (IncludeRoad) BuildTruckRoad(root.transform, sizeX, sizeZ, lanesX, crane, center);   // 트럭 주행 도로
            if (IncludeYard) BuildContainerYard(root.transform, sizeX, sizeZ, lanesX, crane, center); // 컨테이너 주차장
            if (IncludeSea)  BuildSea(root.transform, sizeX, sizeZ, lanesX);                         // 바다
            if (IncludeDecor) BuildPortDecor(root.transform, sizeX, sizeZ, lanesX, crane);          // 항구 꾸미기(육지측)

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
        static Mesh BuildSlab(float sx, float sz, float th, float tile)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            float ux = sx / tile, uz = sz / tile, ut = th / tile;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();

            // Top (+Y) — 보이는 면, 타일 UV
            AddQuad(v, uv, t,
                new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, uz), new Vector2(0, uz), Vector3.up);
            // Bottom (-Y)
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, -hz), new Vector3(hx, -th, -hz), new Vector3(hx, -th, hz), new Vector3(-hx, -th, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, uz), new Vector2(0, uz), Vector3.down);
            // 4 측면(가장자리 단차)
            AddQuad(v, uv, t,
                new Vector3(hx, -th, -hz), new Vector3(hx, -th, hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(uz, 0), new Vector2(uz, ut), new Vector2(0, ut), Vector3.right);
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, -hz), new Vector3(-hx, -th, hz), new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(uz, 0), new Vector2(uz, ut), new Vector2(0, ut), Vector3.left);
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, hz), new Vector3(hx, -th, hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, ut), new Vector2(0, ut), Vector3.forward);
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, -hz), new Vector3(hx, -th, -hz), new Vector3(hx, 0f, -hz), new Vector3(-hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, ut), new Vector2(0, ut), Vector3.back);

            var m = new Mesh { name = "Quay_Slab" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
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

        // ── item 1: 노면 마킹 — 트럭 주행 도로(크레인 밑 포털 → 백리치) ──────────────────────
        //   크레인은 백리치에서 컨테이너를 트럭(섀시) 위로 내린다. 트럭은 크레인 다리 사이(포털)를 지나
        //   백리치로 들어오므로, 그 구간에 Z방향 주행 도로(차선 경계·레인 점선·진행 화살표)를 깐다.
        //   전부 아스팔트 윗면 바로 위(MarkY)의 평면 페인트(쿼드). 실척↔모델 환산 = StsConfig.ModelScale(1/24).
        static void BuildTruckRoad(Transform root, float sizeX, float sizeZ, List<float> lanesX, GameObject crane, Vector3 center)
        {
            float hx = sizeX * 0.5f;
            var white = MakeMat(CMarkWhite, 0.0f, 0.20f, null);
            bool waterPos = WaterSideX >= 0f;
            float sgn = waterPos ? 1f : -1f;

            // 바다측·육지측 레일 X(크레인 밑 = 두 레일 사이)
            float waterRailX = sgn * RailGauge * 0.5f, landRailX = -sgn * RailGauge * 0.5f;
            if (lanesX != null && lanesX.Count > 0)
            {
                waterRailX = lanesX[0]; landRailX = lanesX[0];
                for (int i = 1; i < lanesX.Count; i++)
                {
                    waterRailX = waterPos ? Mathf.Max(waterRailX, lanesX[i]) : Mathf.Min(waterRailX, lanesX[i]);
                    landRailX  = waterPos ? Mathf.Min(landRailX,  lanesX[i]) : Mathf.Max(landRailX,  lanesX[i]);
                }
            }

            // 도로 길이(Z) = QuayRail 길이로 통일(sizeZ*0.98), Z중심 정렬. (부두 바닥 전 요소 길이 일치)
            float zHalf = sizeZ * 0.98f * 0.5f;
            float zLo = -zHalf, zHi = zHalf;

            // ── 차선은 '황색 안전선 안쪽'에만 (황색선과 겹치지 않게). 포털=3차로(원래대로), 백리치=2차로. ──
            float yClear = LaneOffset + 0.02f;                         // 황색선에서 더 안쪽으로 띄울 거리
            float innerLand = landRailX  + sgn * yClear;               // 육지측 레일의 내측(바다쪽) 황색선 안쪽
            float innerSea  = waterRailX - sgn * yClear;               // 바다측 레일의 내측(육지쪽) 황색선 안쪽
            DrawRoadSegment(root, white, innerLand, innerSea, zLo, zHi, 3, "크레인 안쪽(포털)");

            // [차로당 폭 일치] 백리치 2차로의 '차로당 폭' = 포털 3차로의 차로당 폭과 '똑같게'. → 백리치 폭 = 2 × 포털차로폭.
            //   (2차로를 3차로처럼: 차로 1개 폭을 포털과 동일한 4.96m로. 2차로라 전체 폭은 9.9m로 포털 14.9m보단 좁다
            //    — 2차로로 전체 14.9m를 채우면 차로가 7.44m가 되어 포털 차로폭과 어긋나므로, 차로폭 일치를 우선.)
            float portalLaneW = Mathf.Abs(innerSea - innerLand) / 3f;  // 포털 한 차로 폭(=4.96m)
            float backSea = landRailX - sgn * yClear;                  // 육지측 레일의 외측(육지쪽) 황색선 바깥
            float backRoadEnd = Mathf.Clamp(backSea - sgn * (2f * portalLaneW), -(hx - 0.05f), hx - 0.05f);
            DrawRoadSegment(root, white, backSea, backRoadEnd, zLo, zHi, 2, "백리치(바깥)");

            // 배수 트렌치 — 백리치 차로 바로 뒤(육지측). 노면 집수 + 트럭 통과 그레이트.
            float drainX = Mathf.Clamp(backRoadEnd - sgn * 0.035f, -(hx - 0.03f), hx - 0.03f);
            float zMid = (zLo + zHi) * 0.5f, zLen = zHi - zLo;
            var drainMat = MakeMat(CDrain, 0.2f, 0.10f, null);
            var steel = MakeMat(CRail, 0.7f, 0.40f, null);
            PbBox(root, "Quay_DrainChannel", new Vector3(drainX, 0.003f, zMid), new Vector3(0.03f, 0.008f, zLen), drainMat);
            // [A·C 수정] 종전엔 그레이트를 zLen/0.08 간격의 개별 큐브(≈250개)로 깔아 VR 드로우콜을 폭증시켰다(500m 안벽에서 253개).
            //   → 채널 전체를 덮는 '단일' 강재 그레이트 스트립 1개로 대체(드로우콜 1). 살대 무늬는 머티리얼/텍스처로 표현.
            QuayBox(root, "Quay_DrainGrate", new Vector3(drainX, 0.006f, zMid), new Vector3(0.034f, 0.003f, zLen), steel);
        }

        // 한 도로 구간(x0~x1)을 차로로 그린다. lanes>0=정확히 그 수(균등 분할·증축 안 함), ≤0=4m기준 자동.
        //   구간 경계(x0~x1)는 호출부가 '황색선 안쪽'으로 넘겨준다. 점선·화살표는 길이에 정수개로 맞춰 균등 배치.
        static void DrawRoadSegment(Transform root, Material white, float x0, float x1, float zLo, float zHi, int lanes, string label)
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

            // 화살표 — '실척 고정' 간격(RoadArrowGapM ≈30m, 개수 상한 없음). 한 차로의 모든 화살표를 '단일 메시'로 합친다.
            int nArrow = Mathf.Max(1, Mathf.RoundToInt(zLen / (RoadArrowGapM * StsConfig.ModelScale)));
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
            Debug.Log($"[QuayGround] {label} — 폭 {w * StsConfig.InvModelScale:F1}m · {lanes}차로(각 {laneW * StsConfig.InvModelScale:F1}m), 점선 {nPer}·화살표 {nArrow}/차로 (각 단일 메시)");
        }

        // 길쭉한 노면 방향 화살표(+Z): 꼬리 60% + 삼각 머리 40%, 전체 길이 len(중심=center). 잘려 보이지 않게 연결.
        static void Arrow(Transform parent, string name, Vector3 center, float w, float len, Material mat)
        {
            float hl = len * 0.4f, tl = len * 0.6f;
            // 꼬리(중심 뒤쪽: z = center-len/2 ~ center-len/2+tl)
            Stripe(parent, name + "_Tail", center + new Vector3(0f, 0f, -len * 0.5f + tl * 0.5f), w * 0.34f, tl, mat);
            // 머리(삼각: 베이스 z = center-len/2+tl, 팁 z = center+len/2)
            var head = new GameObject(name);
            head.transform.SetParent(parent, worldPositionStays: false);
            head.transform.localPosition = center + new Vector3(0f, 0f, -len * 0.5f + tl + hl * 0.5f);
            head.transform.localScale = new Vector3(w, 1f, hl);
            head.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
            head.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // 윗면을 향한 삼각 화살촉(중심 원점, +Z 끝).
        static Mesh _arrow;
        static Mesh ArrowMesh()
        {
            if (_arrow != null) return _arrow;
            var m = new Mesh { name = "Quay_Arrow" };
            m.SetVertices(new List<Vector3> {
                new Vector3(0f, 0f, 0.5f), new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) });
            m.SetTriangles(new[] { 0, 2, 1 }, 0);   // +Y(윗면) 와인딩
            m.RecalculateNormals(); m.RecalculateBounds();
            return _arrow = m;
        }

        // ── 노면 마킹 합치기(단일 메시) — 점선·화살표를 GameObject 폭증 없이 촘촘히 그리기 위한 누적 헬퍼(로컬 y=0 평면, +Y 향) ──
        static void AddMarkQuad(List<Vector3> v, List<int> t, float xc, float zc, float w, float len)
        {
            int i = v.Count; float hw = w * 0.5f, hl = len * 0.5f;
            v.Add(new Vector3(xc - hw, 0f, zc - hl)); v.Add(new Vector3(xc + hw, 0f, zc - hl));
            v.Add(new Vector3(xc + hw, 0f, zc + hl)); v.Add(new Vector3(xc - hw, 0f, zc + hl));
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
        // 삼각 화살촉 — 베이스 z=zBase(폭 w), 팁 z=zBase+len. +Y 와인딩(ArrowMesh와 동일 순서).
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

        // ── item 3: 컨테이너 보관소(적치장) — 백리치 도로보다 더 육지측(크레인 사거리 밖). ────────
        //   바닥에 컨테이너 슬롯 그리드(행=컨테이너 폭 2.438m, 슬롯=40ft 12.192m)를 그린다. 실척 ÷24.
        static void BuildContainerYard(Transform root, float sizeX, float sizeZ, List<float> lanesX, GameObject crane, Vector3 center)
        {
            float hx = sizeX * 0.5f;
            bool waterPos = WaterSideX >= 0f;
            float sgn = waterPos ? 1f : -1f;
            var line = MakeMat(CMarkWhite, 0.0f, 0.20f, null);

            // 트롤리 백리치 끝(도로 바깥 경계) — 보관소는 이보다 더 육지측. (기즈모식 변환)
            float backLimit = -sgn * (hx - 0.12f);
            var trolley = crane != null ? crane.GetComponentInChildren<TrolleyMover>() : null;
            if (trolley != null)
            {
                var tr = trolley.transform; var tp = tr.parent != null ? tr.parent : tr;
                Vector3 aL = tr.localPosition, bL = tr.localPosition; aL.x = trolley.Min; bL.x = trolley.Max;
                float a = tp.TransformPoint(aL).x - center.x, b = tp.TransformPoint(bL).x - center.x;
                backLimit = waterPos ? Mathf.Min(a, b) : Mathf.Max(a, b);
            }
            // [야드 수정 #2] 육지측 레일 X(도로와 동일 산식) — 백리치 클램프 기준.
            float landRailX = -sgn * RailGauge * 0.5f;
            if (lanesX != null && lanesX.Count > 0)
            {
                landRailX = lanesX[0];
                for (int i = 1; i < lanesX.Count; i++)
                    landRailX = waterPos ? Mathf.Min(landRailX, lanesX[i]) : Mathf.Max(landRailX, lanesX[i]);
            }
            // 백리치 끝 클램프 — 도로(BuildTruckRoad)와 동일 규칙으로 통일(슬래브 안 + 육지측 레일 안쪽선보다 더 육지측).
            float backSeaClampY = landRailX - sgn * (LaneOffset + 0.02f);
            float landEndClampY = -sgn * (hx - 0.05f);
            float limSeaY = backSeaClampY - sgn * 0.06f;
            backLimit = Mathf.Clamp(backLimit, Mathf.Min(landEndClampY, limSeaY), Mathf.Max(landEndClampY, limSeaY));

            float Wc  = 2.438f * StsConfig.ModelScale;          // 컨테이너 폭 = 1줄 폭(X)
            float L40 = 12.192f * StsConfig.ModelScale;         // 40ft 길이 → 슬롯 피치(Z)

            // 보관소 — 백리치 도로/배수 너머(육지측)에서 시작. ★ 줄 수 = 최대 10(오너 지시). 1줄 = 컨테이너 폭.
            float slabEnd  = -sgn * (hx - 0.06f);               // 슬래브 육지 끝
            float yardEdge = backLimit - sgn * 0.10f;           // 도로+배수 뒤로 띄운 시작(seaward)
            // [F 수정] yardEdge가 슬래브 육지끝보다 더 육지측이면(크레인/트롤리 없을 때 발생) 슬래브 밖에 그려지므로 생략.
            if ((slabEnd - yardEdge) * sgn >= 0f) return;       // 가용 공간이 육지측에 없음 → 야드 생략
            float avail = Mathf.Abs(slabEnd - yardEdge);        // 가용 폭
            if (avail < Wc) return;                             // 한 줄도 못 넣으면 생략
            int rows = Mathf.Clamp(Mathf.FloorToInt(avail / Wc), 1, 10);   // 최대 10줄
            float W = rows * Wc;                                // 줄 수만큼 폭
            float landEnd = yardEdge - sgn * W;                 // 육지측으로 W만큼
            float xa = Mathf.Min(yardEdge, landEnd), xb = Mathf.Max(yardEdge, landEnd);
            float zHalf = sizeZ * 0.98f * 0.5f, zLen = zHalf * 2f;   // QuayRail 길이로 통일
            float xMid = (xa + xb) * 0.5f;
            if (W < Wc || zLen < L40) return;                   // 공간 부족하면 생략

            // 길이방향으로 3개 블록으로 분할(사이 이송 통로) — 일자 한 줄 대신 실제 터미널처럼.
            const int nBlk = 3;
            float aisle  = Mathf.Min(0.5f, zLen * 0.06f);          // 블록 사이 통로(≈12m)
            float blkLen = (zLen - (nBlk - 1) * aisle) / nBlk;
            int slots = Mathf.Max(1, Mathf.RoundToInt(blkLen / L40));   // 블록당 슬롯 수
            float slotL = blkLen / slots;
            for (int blk = 0; blk < nBlk; blk++)
            {
                float zc = -zHalf + blkLen * 0.5f + blk * (blkLen + aisle);
                float zh = blkLen * 0.5f;
                // 블록 외곽(4변)
                Stripe(root, "Yard_Edge", new Vector3(xa,   MarkY, zc), MarkW, blkLen, line);
                Stripe(root, "Yard_Edge", new Vector3(xb,   MarkY, zc), MarkW, blkLen, line);
                Stripe(root, "Yard_Edge", new Vector3(xMid, MarkY, zc - zh), W, MarkW, line);
                Stripe(root, "Yard_Edge", new Vector3(xMid, MarkY, zc + zh), W, MarkW, line);
                // 행 경계(블록 길이만큼만)
                for (int k = 1; k < rows; k++)
                    Stripe(root, "Yard_Row", new Vector3(xa + Wc * k, MarkY, zc), MarkW, blkLen, line);
                // 슬롯 경계(블록 내 40ft 간격)
                for (int k = 1; k < slots; k++)
                    Stripe(root, "Yard_Slot", new Vector3(xMid, MarkY, zc - zh + slotL * k), W, MarkW, line);
            }

            Debug.Log($"[QuayGround] 컨테이너 보관소 — {W * StsConfig.InvModelScale:F1}m × {rows}행, {nBlk}블록 × {slots}슬롯(40ft), 블록길이 {blkLen * StsConfig.InvModelScale:F1}m");
        }

        // ── item 4: 바다 — 안벽 가장자리 바깥(바다측) 수면. 안벽 너머 남는 아스팔트를 덮고 외해로 확장. ──
        static void BuildSea(Transform root, float sizeX, float sizeZ, List<float> lanesX)
        {
            bool waterPos = WaterSideX >= 0f;
            float sgn = waterPos ? 1f : -1f;

            // [겹침제거 #4 — 결정론] 바다 안쪽 = '슬래브의 실제 바다측 끝'(로컬 +hx·−hx)과 직접 일치시킨다.
            //   종전엔 레일 X(quayEdge)로 유도했는데, 크레인 미발견/폴백 시 슬래브가 대칭(±hx)으로 커지면
            //   레일 기준 바다(≈0.5u 시작)가 슬래브(±6u) 위로 겹쳐 z-파이팅했다(사용자 렌더 확인). → 슬래브 끝을 직접 기준.
            float hx = sizeX * 0.5f;
            const float seaY = 0f;                             // 그라운드(슬래브 윗면 y=0) 기준 — 세부 높이는 메시가 담당(SeaBaseDrop로 접수선은 데크 아래).
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
            var sea = new GameObject("Sea");
            sea.transform.SetParent(root, worldPositionStays: false);
            sea.transform.localPosition = new Vector3(seaCx, seaY, 0f);
            sea.AddComponent<MeshFilter>().sharedMesh = BuildSeaSurfaceMesh(seaWidth, seaLen, SeaTile, innerLocalX);

            // 머티리얼: 흰 베이스 × 색맵(수심 그라디언트·화이트캡, 전체 1회 매핑). 잔물결 글린트는 '메시 노멀'이 담당.
            //   [B 수정] 종전 _BumpMap 타일링(×2)은 URP Lit이 _BumpMap_ST를 무시(LitInput.hlsl: 모든 맵이 _BaseMap_ST 공유 UV)해
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

            // 안벽 접수선 포말 — 슬래브 끝 바로 바깥(over water)에 데크 살짝 위로 얇게. 슬래브와 겹치지 않는다.
            var foam = MakeMat(CFoam, 0.0f, 0.12f, null);
            float foamX = seaInner + sgn * 0.015f;   // 슬래브 바다측 끝 바로 바깥
            Stripe(root, "Sea_Foam", new Vector3(foamX, 0.0012f, 0f), FoamW, seaLen, foam);

            Debug.Log($"[QuayGround] 바다 — 슬래브 바다측 끝부터 {seaWidth * StsConfig.InvModelScale:F1}m × 길이 {seaLen * StsConfig.InvModelScale:F1}m(슬래브와 정확히 맞닿음), 스웰+수심색+포말");
        }

        // 세분·변위된 수면 메시 — 다방향 사인 스웰(모델 단위), 안벽 접수선은 파고를 죽여 잔잔.
        //   UV는 타일(SeaTile) 단위로 매겨 색 텍스처가 실척으로 반복되게 한다.
        static Mesh BuildSeaSurfaceMesh(float w, float len, float tile, float innerLocalX)
        {
            // [B 수정] 격자 밀도는 UV 타일과 분리 — 잔물결 글린트를 메시 노멀로 내려면 촘촘해야 한다(≈2.4m 격자).
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
                // [겹침제거 #4] 접수선(k→0)은 SeaBaseDrop만큼 데크 아래로 내려 벽면에 잔잔히 닿고, 외해(k→1)로 갈수록 스웰이 살아난다.
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
            // [B 수정] 잔물결(글린트) — 노멀맵(URP _BumpMap_ST 무시됨) 대신 촘촘한 메시 노멀로 태양광 반짝임을 만든다.
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

        // (잔물결 노멀맵 생성기 제거 — URP Lit이 _BumpMap_ST를 무시해 타일링이 무효였음. 잔물결은 메시 노멀(SeaHeight ripple)로 대체.)

        // ── 항구 꾸미기(육지측, 백리치 뒤) — 크레인은 건드리지 않음. 야적 스택 + 조명탑 + 경계 펜스 ──
        static void BuildPortDecor(Transform root, float sizeX, float sizeZ, List<float> lanesX, GameObject crane)
        {
            float land = WaterSideX >= 0f ? -1f : 1f;     // 육지측 부호(바다 반대)
            float hx = sizeX * 0.5f;
            float len = sizeZ * 0.98f, hz = len * 0.5f;
            float s = StsConfig.ModelScale;

            var decor = new GameObject("PortDecor");
            decor.transform.SetParent(root, false);
            var T = decor.transform;

            // (야적 스택은 오너 지시로 제거 — 바닥 야드 마킹은 BuildContainerYard가 그대로 유지)

            // 2. 고소 조명탑(육지측 가장자리 따라)
            var steel = MakeMat(new Color(0.52f,0.53f,0.55f), 0.4f, 0.4f, null);
            var lamp  = MakeMat(new Color(0.96f,0.93f,0.72f), 0.0f, 0.6f, null);
            float towerX = land * (hx - 0.35f);
            const float towerH = 1.3f;     // ≈31m
            int nT = 4;
            for (int i = 0; i < nT; i++)
            {
                float z = -hz + len * (i + 0.5f) / nT;
                Cyl(T, "Light_Pole", new Vector3(towerX, towerH * 0.5f, z), 0.016f, towerH, steel);
                PbBox(T, "Light_Head", new Vector3(towerX, towerH + 0.02f, z), new Vector3(0.22f, 0.035f, 0.06f), steel);
                for (int l = -1; l <= 1; l++)
                    PbBox(T, "Light_Lamp", new Vector3(towerX + l * 0.07f, towerH + 0.04f, z), new Vector3(0.04f, 0.02f, 0.04f), lamp);
            }

            // 3. 육지측 경계 펜스(포스트 + 상·중 레일 + 망)
            var fence = MakeMat(new Color(0.48f,0.49f,0.51f), 0.3f, 0.3f, null);
            float fenceX = land * (hx - 0.06f);
            const float fenceH = 0.1f;     // ≈2.4m
            int nP = Mathf.Max(4, Mathf.RoundToInt(len / 0.9f));
            for (int i = 0; i <= nP; i++)
            {
                float z = -hz + len * i / nP;
                Cyl(T, "Fence_Post", new Vector3(fenceX, fenceH * 0.5f, z), 0.006f, fenceH, fence);
            }
            PbBox(T, "Fence_Rail", new Vector3(fenceX, fenceH * 0.92f, 0f), new Vector3(0.008f, 0.008f, len), fence);
            PbBox(T, "Fence_Rail", new Vector3(fenceX, fenceH * 0.45f, 0f), new Vector3(0.008f, 0.008f, len), fence);
            PbBox(T, "Fence_Mesh", new Vector3(fenceX, fenceH * 0.5f, 0f), new Vector3(0.002f, fenceH * 0.9f, len),
                  MakeMat(new Color(0.4f,0.42f,0.44f), 0.2f, 0.2f, null));

            Debug.Log($"[QuayGround] 항구 꾸미기(육지측) — 조명탑 {nT}, 펜스 포스트 {nP+1}");
        }

        // ── item 2: 계선주·연석 — 바다측 안벽 ──────────────────────────────────────────
        //   ※ WaterSideX = 바다측 부호(+X 가정=트롤리 Max 관례). 렌더 확인 후 어긋나면 -1로 뒤집는다.
        //   (배수 트렌치는 트럭 도로 바깥쪽으로 옮겨 BuildTruckRoad에서 그린다.)
        static void BuildMooringEdge(Transform root, float sizeX, float sizeZ, List<float> lanesX)
        {
            float hx = sizeX * 0.5f;
            bool waterPos = WaterSideX >= 0f;
            float sgn = waterPos ? 1f : -1f;
            float len = sizeZ * 0.98f;   // QuayRail 길이로 통일

            // ── 크레인 위치 기준 ── 슬래브 끝이 아니라 바다측 크레인 레일(=다리 접지선) X에 상대 배치한다.
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

            // 연석(緣石) — 안벽 가장자리 따라 낮은 콘크리트 턱 (ProBuilder). Ground 메뉴 토글로 선택.
            if (IncludeCurb)
            {
                var concrete = MakeMat(CConcrete, 0.0f, 0.15f, null);
                const float curbW = 0.03f, curbH = 0.022f;
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
                for (int i = 0; i <= n; i++)
                {
                    float z = -len * 0.5f + len * i / n;
                    Cyl(root, "Quay_Bollard", new Vector3(bx, postH * 0.5f, z), postR, postH, bollMat);
                    Cyl(root, "Quay_BollardCap", new Vector3(bx, postH + capH * 0.5f, z), capR, capH, bollMat);
                }
            }
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
        //   [D 수정] 정적 캐시 — 종전엔 재생성마다 256² 텍스처를 새로 구워 구 텍스처를 누수했다(Sea 텍스처는 캐시됨).
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
