#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>메뉴에서 STS(Ship-To-Shore) Crane GameObject 계층(다리·포털·붐·트롤리·스프레더)을 절차 생성.
    /// 치수는 1/24 미니어처 기준, 루트에 VR 조종(StsCraneVRController)·집기(SpreaderGrabber)가 붙는다.</summary>
    public static partial class StsCraneCreator
    {
        const float Scale = StsConfig.ModelScale;   // SSOT — 런타임 StsConfig.ModelScale(1/24)과 동일. const은 const 참조 가능.

        // 트롤리 가동(붐 로컬 X) — 음수=육지쪽 backreach, 양수=바다쪽 outreach.
        //   백리치 15.9m/아웃리치 45m = 0.35 ∈ Post-Panamax 0.35~0.45 범위.
        const float TrolleyMinX  = -13f * Scale - BoomBackExtra;   // 백트래블 한계(실척 ~16m 백리치) — 거더 백리치 연장(BoomBackExtra)만큼 더 뒤로
        const float TrolleyMaxX  =  63f * Scale;   // 트롤리 바다쪽 한계 = 실척 63m. 아웃리치(TrolleyMaxX−WaterLegX) = 45m
        const float TrolleyRestX =  8f  * Scale;   // ≈  0.333m

        // 높이/치수. 양정 = (RailH−4.8)×Scale → 실척 39.2m(Post-Panamax 36~40m).
        //   레그·브레이스·양정은 RailH 파생이라 자동 전파(ApexH도 비율 유지 위해 동반 상향).
        const float RailH    = 44f   * Scale;      // 붐(트롤리 레일) 높이 = 다리 높이 ≈ 실척 44m (Post-Panamax). 양정 39.2m
        const float ApexH    = 27.5f * Scale;      // A-프레임 정상 높이 — 레그 비율 0.625 유지 필요(백스테이 클리어런스·실루엣 비례).
        const float GaugeZ   = StsConfig.GantryBaseZMeters * Scale;   // 갠트리 베이스(Z, 레일 게이지 아님). SSOT=StsConfig.GantryBaseZMeters(16m).
        const float LegSpanX = StsConfig.LegGaugeXMeters   * Scale;   // 레일 게이지(X, 육지/바다 다리 간격). SSOT=StsConfig.LegGaugeXMeters(18m).
        const float LegSec   = 1.0f * Scale;       // 다리 단면 한 변. footprint 1.70m, 세장비 44/1.70=25.9:1(현장 22~28)
        const float LegTopY  = RailH + 0.088f;     // 포털 다리/상부 크로스빔/A프레임 베이스 공통 상단 — 붐 거더 윗면보다 살짝 위(거더가 크로스빔에 붙는다).
        // 트윈(더블 박스) 거더 — 두 박스 거더를 z=±GirderGapZ에 두고 사이를 횡프레임·평면 대각으로 결속.
        const float GirderGapZ   = 0.16f;                          // 각 거더 중심 Z — 붐 바깥 끝쪽까지 넓게(다리 게이지 ±0.333 안쪽)
        const float GirderWidthZ = 0.055f;                         // 각 박스 거더 단면 폭(Z) — 0.045→0.055 확대(깊이 0.075와 균형 1.36:1, 갭 0.265 유지)
        const float GirderOuterZ = GirderGapZ + GirderWidthZ * 0.5f;// 트윈 거더 바깥 가장자리(캣워크 난간 위치)
        // 박스 거더 단면(붐-로컬) 상수 — BuildBoomStructure/BuildBoomCatwalk/BuildBoomSplices가 공유하는 값(중복 금지).
        const float GirderCenterY = 0.020f;                         // 거더 단면 중심 y(붐-로컬) — 윗면 0.065 보존 위해 아래로만 확장.
        const float GirderDepthH  = 0.09f;                          // 거더 단면 깊이(y) = 2.16m(현장 2.0~2.5). 깊이/폭 1.64:1. 바닥만 하강(트롤리 z-분리, 충돌 0)
        const float GirderTopLocal = GirderCenterY + GirderDepthH * 0.5f; // 거더 윗면(=0.065 유지, 상부 시스템 정합 기준). 0.020+0.045=0.065
        const float GirderBotLocal = GirderCenterY - GirderDepthH * 0.5f; // 거더 밑면(=-0.025, 깊어지면 따라 내려감). 바닥 현수물(트레이·투광등·단부프레임) SSOT
        // 거더 단면 상수(GapZ/WidthZ·GirderCenterY/GirderDepthH)는 Scale 미곱 raw 값 — Scale 바뀌면 비율 깨짐(현재 미해당).
        // 트롤리 레일 윗면 = 바퀴 트레드 접지면 — 공유 상수라 desync 회귀 방지.
        const float BoomRailTopY = 0.026f;   // 트롤리 주행 레일 윗면 y(= 레일 중심 0.02 + 높이 0.012/2). 바퀴 트레드 하단이 여기에 접지.

        // 붐 거더 X 끝점 — 거더/레일/격자/스테이가 공유(한 군데서 길이 관리)
        const float BoomBackExtra = 0.12f;             // 트롤리 백트래블 + 거더 백리치를 함께 뒤로 빼는 양(기계실은 고정)
        const float GantryRange   = 2.2f;              // 갠트리 주행 범위(±, 모델 단위) ≈ 실척 ±53m, 총 106m.
                                                        //   부두 바닥 Z 길이(부두 FBX)는 분리돼 있어 이 값을 키워도 바닥은 안 커짐.
        const float BoomBackX = TrolleyMinX - 0.27f;   // 백리치(육지쪽) 끝 — 트롤리 뒤 0.27 여유(TrolleyMinX가 이미 BoomBackExtra만큼 뒤로 감)
        const float BoomTipX  = TrolleyMaxX + 0.1f;    // 아웃리치 끝 — 트롤리 끝 + 팁 구조 여유(트롤리가 거의 끝까지)

        // 백리치 끝 플랫폼 위 디플렉터 시브 — 권상 로프(트롤리→플랫폼)를 받아 U자로 되돌려 기계실 진입 포트로 보냄.
        //   동적 로프 뒤끝을 기계실 뒷벽(처짐이 기계실 밑을 뚫음) 대신 여기로 옮겨, 처짐이 뒷공간(백리치)에서 일어나게 함.
        const float BackSheaveX   = BoomBackX + 0.03f;   // 시브 중심 X — 백리치 플랫폼(중심 x0+0.02) 아래
        const float BackSheaveY   = 0.02f;               // 시브 중심 Y — 데크(0.068) 밑으로 내려 매닮(행어로 현수)
        const float BackSheaveR   = 0.011f;              // 시브 외경 — 축소(끝단 브레이스 관통 회피)
        const float BackSheaveHZ  = 0.016f;              // 시브 반폭(Z) — side당 2폴(z=sz±0.008) 더블그루브 수용

        // 기계실 — 거더 백리치 연장과 무관하게 고정 위치. 백스테이가 기계실을 안 뚫게 앞(바다쪽)으로 MHForward만큼 당김.
        const float MHForward        = 0.12f;
        const float MachineryHouseHX = 0.085f;                              // 기계실 X 반폭(0.17의 절반)
        // 기계실을 백리치 끝(-13)으로 재정착 — 실제 STS는 기계실/평형추가 백리치 끝에 위치.
        //   캣워크·드럼·진입구는 이 상수 파생이라 자동 추종.
        const float MachineryHouseX  = (-13f * Scale - 0.27f) + 0.11f + MHForward;  // 기계실 중심 X — 새 백리치(-13) 기준 + 앞당김
        // 기계실 안 드럼 — 로프 끝도 이 값으로 잡는다(따로 적었더니 로프가 붐호이스트 윈치 안에서 끝났다).
        const float HoistDrumX = MachineryHouseX - 0.065f, HoistDrumY = 0.085f, HoistDrumR = 0.014f;
        const float TravelDrumX = -0.535f, TravelDrumY = 0.095f, TravelDrumR = 0.016f;

        // 다리 X 위치(붐 로컬 = 루트 로컬, 붐이 루트 x=0에 있으므로 동일)
        const float LandLegX  = 0f;
        const float WaterLegX = LegSpanX;

        // 스프레더 승강 (spreaderRoot=붐 레벨 기준 로컬 Y, 음수=아래)
        const float SpreaderMaxY  = -4f  * Scale;           // 완전 상승 = 헤드블록이 트롤리 헤드 바로 아래 도킹.
                                                             //   헤드 로프소켓 콘 top이 트롤리 헤드 하단을 파고들지 않게 ~11mm 여유.
        const float SpreaderMinY  = -(RailH - 0.8f * Scale); // 지면 직전(붐 높이에 연동)
        const float SpreaderRestY = -10f * Scale;

        // 스프레더 텔레스코픽 반길이(로컬 X, m). 중앙부는 항상 20ft, 좌/우 암이 사이즈별로 슬라이드.
        const float SpreaderHalf20 = 0.126f;   // 20ft (6.058/24/2 ≈ 0.1262)
        const float SpreaderHalf40 = 0.254f;   // 40ft (12.192/24/2 = 0.254)

        // [인양점 SSOT] 호이스트(헤드/시브/데드엔드/로프/헤드블록 소켓)를 한 상수로 묶어 양끝 정렬(gap=0 by construction).
        //   +X=항구(바다). 트롤리를 실척 ≈7m로 키운 뒤(Option C) 스프레더가 운전실 앞 빈 베이 중앙에 오도록 +X 이동.
        const float HoistX    = 0.027f;   // 인양점 항구쪽 이동 = (운전실전면 −0.062 + 트롤리바다끝 +0.115)/2 ≈ +0.027(≈0.65m)
        const float HoistSprX = 0.05f;    // 호이스트 폴 X 반간격(트롤리/parent) — 4가닥 분리
        const float HoistSprZ = 0.075f;   // 호이스트 폴 Z 반간격(트롤리/parent)

        // [트롤리 본체 치수 SSOT] Option C 기계실 통합 박스. 여러 메서드(본체/프레임/붐로프 앵커)가 공유.
        const float TrolleyHX = 0.145f;   // 본체 반길이 X: full 0.29 = 실척 6.96m ≈7m
        const float TrolleyCX = -0.03f;   // 본체 X중심(육지 −X 이동) → 본체 X[−0.175,+0.115]
        const float TrolleyBackX = TrolleyCX - TrolleyHX;   // 본체 육지쪽(뒤) 끝면 = −0.175 (붐 호이스트 로프 트롤리측 앵커 기준)

        // 색
        static readonly Color CStruct  = new Color(0.82f, 0.83f, 0.85f); // 다리/포털/거더
        static readonly Color CBoom    = new Color(0.70f, 0.74f, 0.80f); // 붐 거더 본체
        static readonly Color CRail     = new Color(0.55f, 0.57f, 0.62f); // 트롤리 레일
        static readonly Color CMachine = new Color(0.30f, 0.33f, 0.38f); // 기계실
        static readonly Color CTrolley = new Color(0.95f, 0.45f, 0.10f); // 트롤리(안전 주황)
        static readonly Color CSpread  = new Color(0.98f, 0.80f, 0.10f); // 스프레더(안전 노랑)
        static readonly Color CSafety  = new Color(0.96f, 0.78f, 0.10f); // 난간·계단·케이지·토보드(안전 노랑 — 스프레더와 통일)
        static readonly Color CDark    = new Color(0.13f, 0.13f, 0.15f); // 트위스트락/헤드블록
        static readonly Color CCable   = new Color(0.22f, 0.23f, 0.25f); // 와이어 로프(오일드 강철 회색 — 순흑X)
        static readonly Color CGlass   = new Color(0.25f, 0.55f, 0.70f); // 운전실 창
        static readonly Color CLight   = new Color(1.00f, 0.95f, 0.70f); // 작업등 렌즈
        static readonly Color CWarn    = new Color(0.90f, 0.10f, 0.10f); // 항공장애등(적색)

        const string RootName = StsPartNames.StsCraneRoot;

        [MenuItem("Model/PG/크레인/STS 크레인 생성", false, 0)]
        public static void CreateFromMenu40ft() => CreateAtContainer(SpreaderHalf40);

        // STS 2대 배치 — 같은 레일 공유, 지면 중심 ±(배 LOA/4)로 대칭(1/4·3/4 위치). 지면은 그대로.
        [MenuItem("Model/PG/크레인/STS 2대 배치", false, 1)]
        public static void PlaceTwoSts()
        {
            // 기준 X/Y — 기존 STS(정확히 배치됨) 우선, 없으면 QuayRail
            var existing = GameObject.Find(RootName);
            float baseX, baseY;
            if (existing != null) { baseX = existing.transform.position.x; baseY = existing.transform.position.y; }
            else if (TryFindQuayRailLandX(out float qx)) { baseX = qx; baseY = 0f; }
            else { Debug.LogWarning("[STS] 기준 STS/QuayRail 없음 — 'STS 크레인 생성' 또는 'Model/FBX/항구/레일 배치' 먼저."); return; }

            // 대칭 중심 Z — 지면(슬래브) 중심. 없으면 기존 STS Z.
            float centerZ = existing != null ? existing.transform.position.z : 0f;
            var groundGo = GameObject.Find(StsPartNames.QuayGround);
            if (groundGo != null)
            {
                // 부두 절차 생성기가 없어 걷는 면 조회 헬퍼가 없다 — 부두 FBX 루트 바운즈 중심 Z를 쓴다.
                if (SceneUtil.TryBounds(groundGo.transform, out var b)) centerZ = b.center.z;
            }

            // 기존 STS 전부 제거(2대 새로 배치)
            foreach (var nm in new[] { RootName, RootName + "_2" })
            { var g = GameObject.Find(nm); if (g != null) Undo.DestroyObjectImmediate(g); }

            // 대칭 간격 — 갠트리 주행 ±53m가 '중앙에서 겹치게' ±40m로 좁힘(가운데 컨테이너도 두 크레인이 짚음).
            //   STS#1 [-93,+13], STS#2 [-13,+93] → 중앙 ±13m 겹침.
            float half = 40f * Scale;   // ±40m, 간격 80m
            var c1 = Create(new Vector3(baseX, baseY, centerZ - half));
            var c2 = Create(new Vector3(baseX, baseY, centerZ + half)); c2.name = RootName + "_2";

            // 갠트리 주행을 레일 전장으로 맞춤 — 두 대가 배 전구간(선수·선미 포함) 커버. 충돌방지 로직(GantryMover)이 안전 보장.
            foreach (var c in new[] { c1, c2 })
            {
                var gm = c.GetComponent<GantryMover>();
                if (gm != null) GantryRangeFitMenu.ApplyFit(c, gm, out _);
            }

            Selection.activeGameObject = c1;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[STS] 2대 배치 — X={baseX:F2}, Z={centerZ - half:F2}/{centerZ + half:F2}(±{half:F2}u), 레일 전장 주행+충돌방지 → 배 전구간 커버·중앙 겹침. 지면 유지.");
        }

        // 컨테이너 위치에 지정 스프레더 사이즈(반길이)로 생성 — 기존 인스턴스는 교체.
        static void CreateAtContainer(float spreaderHalf)
        {
            // 중복 방지 — 기존 인스턴스 제거(Undo 가능)
            var prev = GameObject.Find(RootName);
            if (prev != null) Undo.DestroyObjectImmediate(prev);

            // 컨테이너가 스프레더 정지 위치 아래에 오도록 배치
            Vector3 anchor = FindContainerAnchor();
            Vector3 pos = anchor - new Vector3(TrolleyRestX, 0f, 0f);

            // [부두 우선 정렬] Quay_Ground가 있으면 크레인 X를 육지측 QuayRail에 스냅한다.
            //   크레인 루트 X = 육지측 레일(LandLegX=0, 중심 아님) — 이래야 Rail_Land/Rail_Water가 부두 레일에 포개진다.
            if (TryFindQuayRailLandX(out float quayLandX))
            {
                Debug.Log($"[STS] 기존 Quay_Ground 발견 → 크레인 루트 X를 육지측 QuayRail {quayLandX:F3}u에 정렬(컨테이너 앵커 X={pos.x:F3}u 대신). Rail_Water는 {quayLandX + LegSpanX:F3}u.");
                pos.x = quayLandX;
            }

            var root = Create(pos, spreaderHalf);

            // 부두 컨테이너 인식(스캔) 자동 실행 — 별도 메뉴에서 빠지고 생성에 통합됨.
            // 선택은 크레인에 유지(select:false)하고, 스캔이 앵커 산정 이후라 배치엔 영향 없음.
            QuayScannerMenu.ScanNow(select: false);

            // 부두가 있으면 갠트리 주행범위를 레일에 자동 맞춤(생성 순서 무관).
            //   부두가 없으면 ApplyFit가 false 반환 → 조용히 패스(부두 생성 시 자동맞춤됨).
            var gantry = root.GetComponent<GantryMover>();
            if (gantry != null && GantryRangeFitMenu.ApplyFit(root, gantry, out string fitMsg))
                Debug.Log($"[STS] 갠트리 주행범위 레일 자동 맞춤 — {fitMsg}");

            Selection.activeGameObject = root;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
        }

        /// <summary>hierarchy를 생성해 root GameObject를 반환. Undo 등록 → Ctrl+Z로 되돌릴 수 있음.</summary>
        public static GameObject Create(Vector3 worldPosition, float spreaderHalf = SpreaderHalf40)
        {
            _matCache = new Dictionary<Color, Material>();
            _steelTex = null;
            _ropeTex = null;
            _pbrCache = new Dictionary<string, Texture2D[]>();
            _plateCache = new Dictionary<string, Mesh>();
            _boltCache = new Dictionary<string, Mesh>();
            _nameSeq.Clear();   // 파츠 번호 카운터 리셋(생성마다 _1부터)

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create STS Crane");
            root.transform.position = worldPosition;

            // 정적 구조
            BuildGroundRails(root.transform);
            BuildPortal(root.transform);
            BuildAccessDetails(root.transform);   // 사다리 + 점검 플랫폼

            // 붐 (루트 x=0, y=RailH) — 자식 트롤리/스프레더가 붐 로컬 좌표로 동작
            var boom = new GameObject(Numbered("Boom"));
            boom.transform.SetParent(root.transform, worldPositionStays: false);
            boom.transform.localPosition = new Vector3(0f, RailH, 0f);
            BuildBoomStructure(boom.transform);
            BuildBoomDetails(boom.transform);     // 보도·시브·작업등
            BuildBoomTrussDepth(boom.transform);  // 측면 워런 트러스(깊이감) + 측면 케이블 트레이
            BuildBoomSplices(boom.transform);     // 거더 스플라이스 연결판 + 볼트열
            BuildMachineryHouseAccess(boom.transform);  // 사다리 정상 ↔ 기계실 접근 캣워크 + 사다리쪽 출입문

            // [붐 러핑] 힌지 피벗 생성(빈 노드). 재부모화 스윕·BoomLuffMover는 붐·리빙·케이블·트롤리 전부 지은 '맨 끝'([러핑 최종 편입])에서 한 번에.
            //   후속 빌더(리빙·스테이)가 이 피벗을 참조하므로 생성만 먼저. 피벗 = 바다다리 상단 힌지(WaterLegX, 거더 중심선).
            var luffPivot = new GameObject("Boom_LuffPivot");
            luffPivot.transform.SetParent(boom.transform, worldPositionStays: false);
            luffPivot.transform.localPosition = new Vector3(WaterLegX, GirderCenterY, 0f);
            BuildBoomHinge(boom.transform, luffPivot.transform);          // 힌지 관절(클레비스+텅+핀) — 스윕 이후(직접 부모 지정)
            BuildBoomHoistReeving(root.transform, luffPivot.transform);   // 붐호이스트 리빙(정점 시브↔브라이들 동적)

            // A-프레임 + 스테이 케이블 (루트 레벨)
            BuildApexAndStays(root.transform, luffPivot.transform);
            // [삭제] 붐 래치(6) — 83° 완전 스토우에서만 작동하는 잠금장치인데, 우리 시뮬은 그렇게 안 세우므로 불필요(죽은 부품) → 제거.
            BuildBoltedJoints(root.transform);    // 주요 구조 접합부 연결판 + 볼트(다리/포털/타이빔/브레이스)

            // 트롤리 (붐 직속 자식)
            var trolley = new GameObject(Numbered("Trolley"));
            trolley.transform.SetParent(boom.transform, worldPositionStays: false);
            BuildTrolleyVisual(trolley.transform);

            // 스프레더 루트 (트롤리의 형제 — TrolleyMover가 X 동기)
            var spreaderRoot = new GameObject(Numbered("SpreaderRoot"));
            spreaderRoot.transform.SetParent(boom.transform, worldPositionStays: false);

            var spreader = new GameObject(Numbered("Spreader"));
            spreader.transform.SetParent(spreaderRoot.transform, worldPositionStays: false);
            BuildSpreaderVisual(spreader.transform, spreaderHalf);
            spreader.AddComponent<SpreaderLockAnimator>();   // 트위스트락 잠금 모션

            var attachPoint = new GameObject(Numbered("AttachPoint"));
            attachPoint.transform.SetParent(spreader.transform, worldPositionStays: false);
            // 컨테이너 윗면이 여기에 정렬됨(SpreaderGrabber.Grab). 스프레더 본체 최하단(하단 Beam_Flange 밑면 ≈ -0.019)에
            // 맞춰야 빔·플랜지가 컨테이너 위에 얹히고 트위스트락만 코너 캐스팅으로 삽입된다. (-0.01이면 플랜지가 컨테이너에 묻힘)
            attachPoint.transform.localPosition = new Vector3(0f, -0.019f, 0f);

            // 호이스트 로프 — HoistRopeRig가 매 프레임 스프레더 Y에 맞춰 신축
            BuildHoistRopes(spreaderRoot.transform, spreader.transform);
            // 권상 윗구간 — 트롤리↔시브 구간·시브 감김을 TrolleyReevingRig로 트롤리 추종(꺾임 0).
            BuildHoistUpper(boom.transform, trolley.transform);
            // 견인로프 — 동일하게 트롤리 추종 리빙.
            BuildTowReeving(boom.transform, trolley.transform, luffPivot.transform);

            // 컴포넌트 부착 + 설정
            var trolleyMover = trolley.AddComponent<TrolleyMover>();
            var spreaderHoist = spreader.AddComponent<SpreaderHoist>();
            var spreaderAttach = attachPoint.AddComponent<SpreaderAttach>();

            trolleyMover.Configure(TrolleyMinX, TrolleyMaxX, spreaderRoot.transform);
            spreaderHoist.Configure(SpreaderMinY, SpreaderMaxY);
            spreaderAttach.Configure(attachPoint.transform);

            // 초기 위치 (rest pose) — Boom 로컬 좌표계 기준
            trolley.transform.localPosition = new Vector3(TrolleyRestX, 0f, 0f);
            spreaderRoot.transform.localPosition = new Vector3(TrolleyRestX, 0f, 0f);
            spreader.transform.localPosition = new Vector3(HoistX, SpreaderRestY, 0f);   // 인양점 항구쪽 이동(SpreaderHoist는 Y만 갱신, X 보존)

            // [러핑 최종 편입] 붐·리빙·케이블·트롤리·스프레더를 전부 지은 뒤, 힌지 초과(바다측)인 모든 렌더 부재를
            //   한 번에 루핑 피벗으로 재부모화(전장 부재는 _Luff, 트롤리·스프레더는 육지측 rest라 제외).
            {
                float hingeLocalX = WaterLegX;
                var luffParts = new List<Transform>();
                foreach (Transform c in boom.transform)
                {
                    if (c == luffPivot.transform) continue;
                    if (c.name.Contains("TowFore_Rope") || c.name.Contains("HoistU_Rope")) continue;   // 리그가 매 프레임 배치 — 재부모화 금지(어긋나면 허공 수직선)
                    if (c.name.EndsWith("_Luff")) { luffParts.Add(c); continue; }             // 분할된 바다측 절반
                    if (TryBoomLocalMinX(c, boom.transform, out float minX) && minX >= hingeLocalX - 0.002f)
                        luffParts.Add(c);                                                      // 완전히 바다측인 부재(구조·리빙·케이블 전부)
                }
                foreach (var tt in luffParts) tt.SetParent(luffPivot.transform, worldPositionStays: true);  // 월드 보존 → 0°서 위치 불변
                var luffMover = luffPivot.AddComponent<BoomLuffMover>();
                luffMover.Configure(0f, 83f);   // rest 0°(수평)·max 83°(확정)
                luffMover.Current = 0f;         // rest 0°(수평 운용 자세)
            }


            // 갠트리 주행 — 크레인 루트 Z축, 초기 위치(worldPosition.z) 기준 ±GantryRange. VR controller A 토글로 활성.
            var gantryMover = root.AddComponent<GantryMover>();
            gantryMover.Configure(worldPosition.z - GantryRange, worldPosition.z + GantryRange);

            // 갠트리 주행부(루트)에 kinematic Rigidbody — 다리 콜라이더(Leg_Collider)가 dynamic 컨테이너를
            //   밀어내도록 물리 바디 부여. 중력/외력은 안 받고(kinematic), GantryMover의 위치 이동만 충돌로 전달.
            var craneBody = root.AddComponent<Rigidbody>();
            craneBody.isKinematic = true;
            craneBody.useGravity = false;
            craneBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;   // 느린 주행도 터널링 방지

            // 루트 컴포넌트 — Facade로 묶기
            var stsCrane = root.AddComponent<StsCrane>();
            stsCrane.Configure(boom.transform, trolleyMover, spreaderHoist, spreaderAttach, gantryMover);

            // (데모 자동사이클 StsCraneOperator 제거 — 새 크레인은 시나리오 부착 전엔 가만히 있는다)
            // 폭풍 계류 결박 봉 — 갠트리 주행 시 살짝 들렸다(분리) 정지 시 내려 박힘. Start에서 Tiedown_Rod 자동 수집.
            root.AddComponent<TiedownController>();
            // VR 컨트롤러 수동 조종 — 스틱/트리거로 직접 조종
            root.AddComponent<StsCraneVRController>();
            // 컨테이너 집기/놓기 + 통과 방지(콜라이더 없는 컨테이너 대응)
            root.AddComponent<SpreaderGrabber>();

            PruneDeletedParts(root.transform);   // 삭제 지정 파츠 일괄 제거(번호 유지)
            CranePartId.Stamp(root.transform);   // PLC ↔ 3D 부품 ID — 번호가 아니라 위치로 붙인다(생성 순서 무관)

            _matCache = null;
            _steelTex = null;   // 텍스처는 머티리얼이 참조 유지 → 캐시 핸들만 해제
            _ropeTex = null;    // 로프 텍스처도 머티리얼이 참조 유지 → 캐시 핸들만 해제
            _pbrCache = null;   // PBR 라이브러리 텍스처는 에셋이라 영속 → 캐시 핸들만 해제
            _plateCache = null; // 거싯 메시도 GameObject가 참조 유지 → 핸들만 해제
            _boltCache = null;  // 볼트 메시도 GameObject가 참조 유지 → 핸들만 해제
            return root;
        }

        static Vector3 FindContainerAnchor()
        {
            var inst = Object.FindAnyObjectByType<AIXRCrane.ContainerInstance>();
            if (inst != null) return inst.transform.position;
            var named = GameObject.Find("Container_Procedural");
            return named != null ? named.transform.position : Vector3.zero;
        }

        /// <summary>씬에 이미 있는 Quay_Ground의 '육지측' QuayRail 월드 X를 반환(크레인 루트 X = 육지측 레일, 중심 아님).
        /// 부두(또는 QuayRail)가 없으면 false → 호출부는 컨테이너 앵커 기준 유지.</summary>
        static bool TryFindQuayRailLandX(out float landX)
        {
            landX = 0f;
            var quay = GameObject.Find(StsPartNames.QuayGround);
            if (quay == null) return false;
            bool found = false;
            foreach (var t in quay.GetComponentsInChildren<Transform>(true))
                if (t.name == StsPartNames.QuayRail)
                {
                    if (!found || t.position.x < landX) landX = t.position.x;   // 육지측 = 물측(+X) 반대 = 최소 X
                    found = true;
                }
            return found;
        }
    }
}
#endif
