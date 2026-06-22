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
        const float MarginX      = 2.0f;    // X(붐 방향) 여유 — 0.8→2.0 (실척 ≈48m 사방 추가, 사용자 요청으로 X만 확대)
        const float MarginZ      = 0.8f;    // Z(레일 방향) 여유 — 원래대로
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

        // ── item 2: 계선주·연석·배수(바다측 안벽) ──
        //   WaterSideX = 바다측 부호(+X=트롤리 Max 관례). 렌더 확인 후 어긋나면 -1로 뒤집는다.
        const float WaterSideX = 1f;
        // [선박 2026-06-19] 바다 폭 — 안벽 가장자리에서 바다측으로 확장(배가 뜰 개방 수면). 8u≈192m: 선폭 39.5m + 여유.
        const float SeaExtent  = 8f;
        static readonly Color CConcrete = new Color(0.60f, 0.59f, 0.56f);   // 연석 콘크리트
        static readonly Color CBollard  = new Color(0.16f, 0.17f, 0.19f);   // 계선주 캐스트강(차콜)
        static readonly Color CDrain    = new Color(0.10f, 0.10f, 0.11f);   // 배수 채널(어둠)
        static readonly Color CSea      = new Color(0.07f, 0.20f, 0.30f);   // 바다(짙은 청록, 반사형)

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
                    // Z 중심: 갠트리가 있으면 주행 범위 중간(=초기 위치), 없으면 현재 바운즈 중심
                    float centerZ = gantry != null ? (gantry.Min + gantry.Max) * 0.5f : b.center.z;
                    center = new Vector3(b.center.x, 0f, centerZ);
                    sizeX = Mathf.Max(DefaultSizeX, b.size.x + MarginX * 2f);
                    // 바닥 Z 길이는 갠트리 주행거리(gantryRange)와 분리된 고정값(QuayZSpan) — 주행범위를 키워도 바닥은 안 커진다.
                    sizeZ = Mathf.Max(DefaultSizeZ, b.size.z + MarginZ * 2f + QuayZSpan);
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
            var lanesX = new List<float>();
            if (crane != null)
            {
                foreach (var t in crane.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(StsPartNames.RailPrefix)) lanesX.Add(t.position.x - center.x);
            }
            if (lanesX.Count == 0) { lanesX.Add(-RailGauge * 0.5f); lanesX.Add(RailGauge * 0.5f); }

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

            // 트롤리 백리치 끝(육지측) — 트럭이 컨테이너 받는 가장 안쪽. 기즈모식 변환(무버 로컬끝점→월드→부두로컬).
            float backLimit = -sgn * (hx - 0.12f);
            var trolley = crane != null ? crane.GetComponentInChildren<TrolleyMover>() : null;
            if (trolley != null)
            {
                var tr = trolley.transform; var tp = tr.parent != null ? tr.parent : tr;
                Vector3 aL = tr.localPosition, bL = tr.localPosition; aL.x = trolley.Min; bL.x = trolley.Max;
                float a = tp.TransformPoint(aL).x - center.x, b = tp.TransformPoint(bL).x - center.x;
                backLimit = waterPos ? Mathf.Min(a, b) : Mathf.Max(a, b);
            }

            // 도로 길이(Z) = QuayRail 길이로 통일(sizeZ*0.98), Z중심 정렬. (부두 바닥 전 요소 길이 일치)
            float zHalf = sizeZ * 0.98f * 0.5f;
            float zLo = -zHalf, zHi = zHalf;

            // 슬래브 안으로 클램프한 백리치 끝
            backLimit = Mathf.Clamp(backLimit, -(hx - 0.05f), hx - 0.05f);

            // ── 차선은 '황색 안전선 안쪽'에만 (황색선과 겹치지 않게). 개수 고정: 안쪽 3차로 · 백리치 2차로 ──
            //   황색선은 레일 중심 ±LaneOffset에 깔린다. 차로 영역을 그보다 더 안쪽으로 띄워 침범을 막는다.
            float yClear = LaneOffset + 0.02f;                         // 황색선에서 더 안쪽으로 띄울 거리
            float innerLand = landRailX  + sgn * yClear;               // 육지측 레일의 내측(바다쪽) 황색선 안쪽
            float innerSea  = waterRailX - sgn * yClear;               // 바다측 레일의 내측(육지쪽) 황색선 안쪽
            DrawRoadSegment(root, white, innerLand, innerSea, zLo, zHi, 3, "크레인 안쪽(포털)");

            float backSea = landRailX - sgn * yClear;                  // 육지측 레일의 외측(육지쪽) 황색선 바깥
            DrawRoadSegment(root, white, backSea, backLimit, zLo, zHi, 2, "백리치(바깥)");

            // 배수 트렌치 — '차로 바깥'(백리치 차로의 육지측 끝 너머)으로. 노면 집수 + 트럭 통과 그레이트.
            float drainX = Mathf.Clamp(backLimit - sgn * 0.035f, -(hx - 0.03f), hx - 0.03f);
            float zMid = (zLo + zHi) * 0.5f, zLen = zHi - zLo;
            var drainMat = MakeMat(CDrain, 0.2f, 0.10f, null);
            var steel = MakeMat(CRail, 0.7f, 0.40f, null);
            PbBox(root, "Quay_DrainChannel", new Vector3(drainX, 0.003f, zMid), new Vector3(0.03f, 0.008f, zLen), drainMat);
            int gn = Mathf.Max(4, Mathf.RoundToInt(zLen / 0.08f));
            for (int i = 0; i <= gn; i++)
            {
                float z = zLo + zLen * i / gn;
                QuayBox(root, "Quay_DrainGrate", new Vector3(drainX, 0.006f, z), new Vector3(0.034f, 0.003f, 0.01f), steel);
            }
        }

        // 한 도로 구간(x0~x1)을 차로로 그린다. lanes>0=정확히 그 수(균등 분할·증축 안 함), ≤0=4m기준 자동.
        //   구간 경계(x0~x1)는 호출부가 '황색선 안쪽'으로 넘겨준다. 점선·화살표는 길이에 정수개로 맞춰 균등 배치.
        static void DrawRoadSegment(Transform root, Material white, float x0, float x1, float zLo, float zHi, int lanes, string label)
        {
            float xa = Mathf.Min(x0, x1), xb = Mathf.Max(x0, x1), w = xb - xa;
            float zMid = (zLo + zHi) * 0.5f, zLen = zHi - zLo;
            if (w < 0.04f || zLen < 0.2f) return;

            if (lanes <= 0) lanes = Mathf.Max(1, Mathf.RoundToInt(w / (4.0f * StsConfig.ModelScale)));
            float laneW = w / lanes;                                    // 구간을 정확히 lanes로 균등 분할

            // 양끝 경계 실선(Z방향)
            Stripe(root, "Road_Edge", new Vector3(xa, MarkY, zMid), MarkW, zLen, white);
            Stripe(root, "Road_Edge", new Vector3(xb, MarkY, zMid), MarkW, zLen, white);

            // 점선 주기 산식 — 목표 12m(점선3+간격9)를 도로 길이에 정수개로 맞춤. 점선은 주기의 25%.
            int nPer = Mathf.Max(1, Mathf.RoundToInt(zLen / (12.0f * StsConfig.ModelScale)));
            float period = zLen / nPer;
            float dashLen = period * 0.25f;
            for (int k = 1; k < lanes; k++)
            {
                float x = xa + laneW * k;
                for (int p = 0; p < nPer; p++)
                    Stripe(root, "Road_LaneDash", new Vector3(x, MarkY, zLo + period * (p + 0.5f)), MarkW, dashLen, white);
            }

            // 화살표 — 차로당 정수개 균등 배치(점선 절반 빈도, 1~4개). 주기 안에 들어가게 길이 제한.
            int nArrow = Mathf.Clamp(nPer / 2, 1, 4);
            float arrowW = Mathf.Min(laneW * 0.30f, 2.0f * StsConfig.ModelScale);
            float arrowL = Mathf.Min(arrowW * 2.6f, (zLen / nArrow) * 0.7f);
            for (int k = 0; k < lanes; k++)
            {
                float cx = xa + laneW * (k + 0.5f);
                for (int a = 0; a < nArrow; a++)
                    Arrow(root, "Road_Arrow", new Vector3(cx, MarkY, zLo + zLen * (a + 0.5f) / nArrow), arrowW, arrowL, white);
            }
            Debug.Log($"[QuayGround] {label} — 폭 {w * StsConfig.InvModelScale:F1}m · {lanes}차로(각 {laneW * StsConfig.InvModelScale:F1}m)");
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
            backLimit = Mathf.Clamp(backLimit, -(hx - 0.05f), hx - 0.05f);

            float Wc  = 2.438f * StsConfig.ModelScale;          // 컨테이너 폭 = 1줄 폭(X)
            float L40 = 12.192f * StsConfig.ModelScale;         // 40ft 길이 → 슬롯 피치(Z)

            // 보관소 — 백리치 도로/배수 너머(육지측)에서 시작. ★ 줄 수 = 최대 10(오너 지시). 1줄 = 컨테이너 폭.
            float yardEdge = backLimit - sgn * 0.10f;           // 도로+배수 뒤로 띄운 시작(seaward)
            float slabEnd  = -sgn * (hx - 0.06f);               // 슬래브 육지 끝
            float avail = Mathf.Abs(slabEnd - yardEdge);        // 가용 폭
            int rows = Mathf.Clamp(Mathf.FloorToInt(avail / Wc), 1, 10);   // 최대 10줄
            float W = rows * Wc;                                // 10줄 폭(딱 줄 수만큼)
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

            // 바다측 레일 → 안벽 가장자리(계선/연석과 동일 기준)
            float waterRailX = sgn * RailGauge * 0.5f;
            if (lanesX != null && lanesX.Count > 0)
            {
                waterRailX = lanesX[0];
                for (int i = 1; i < lanesX.Count; i++)
                    waterRailX = waterPos ? Mathf.Max(waterRailX, lanesX[i]) : Mathf.Min(waterRailX, lanesX[i]);
            }
            float quayEdgeX = waterRailX + sgn * 4f * StsConfig.ModelScale;   // 안벽 가장자리

            // [선박] 바다를 슬래브 밖 '개방 수면'으로 확장 — 안벽 가장자리에서 바다측 SeaExtent까지(배 접안 수면).
            const float seaY = 0.004f;                         // 노면 마킹 위·연석(0.022) 아래 → 안벽 너머 아스팔트를 덮음
            float seaInner = quayEdgeX + sgn * 0.005f;         // 연석 바로 바깥에서 시작
            float seaOuter = quayEdgeX + sgn * SeaExtent;      // 개방 수면 바다측 끝
            float xa = Mathf.Min(seaInner, seaOuter), xb = Mathf.Max(seaInner, seaOuter);
            float seaWidth = xb - xa;
            float seaLen = sizeZ;                              // 안벽 길이와 일치(아스팔트=바다 길이)
            float seaCx = (xa + xb) * 0.5f;
            if (seaWidth < 0.02f) return;                      // 안벽이 슬래브 끝에 붙어 있으면 생략

            var sea = new GameObject("Sea");
            sea.transform.SetParent(root, worldPositionStays: false);
            sea.transform.localPosition = new Vector3(seaCx, seaY, 0f);
            sea.transform.localScale = new Vector3(seaWidth, 1f, seaLen);
            sea.AddComponent<MeshFilter>().sharedMesh = UnitQuad();
            sea.AddComponent<MeshRenderer>().sharedMaterial = MakeMat(CSea, 0.0f, 0.92f, null);   // 반사형 수면

            Debug.Log($"[QuayGround] 바다 — 안벽 가장자리({quayEdgeX * StsConfig.InvModelScale:F1}m)부터 아스팔트 끝까지 {seaWidth * StsConfig.InvModelScale:F1}m × 길이 {seaLen * StsConfig.InvModelScale:F1}m");
        }

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
            float edgeMargin = 4f * StsConfig.ModelScale;  // 바다측 레일 → 안벽(quay) 가장자리 ≈4m
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
        static Texture2D BuildAsphaltTexture()
        {
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
            return tex;
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
