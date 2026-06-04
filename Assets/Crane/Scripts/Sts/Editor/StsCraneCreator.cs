#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 메뉴에서 STS(Ship-To-Shore) Crane GameObject 계층을 자동 생성.
    ///
    /// 생성되는 hierarchy:
    ///     STS_Crane                       (StsCrane) — 다리·포털·A프레임·스테이 케이블(정적)
    ///       └─ Boom                       (붐 거더 + 기계실, 정적)
    ///           └─ Trolley                (TrolleyMover, X 슬라이딩)
    ///           └─ SpreaderRoot           (트롤리 X를 따라감)
    ///               └─ Spreader           (SpreaderHoist, Y 승강)
    ///                   └─ AttachPoint     (SpreaderAttach, 컨테이너 부착)
    ///
    /// 형상은 박스/스트럿 primitive 조합으로 실제 STS 크레인 실루엣(포털 다리 4개,
    /// 격자 붐, A-프레임 정상, 포어/백 스테이, 기계실, 트롤리·스프레더)을 흉내낸다.
    /// 치수는 1/24 미니어처(컨테이너와 비례)에 맞춘 기본값.
    ///
    /// 트롤리/스프레더는 무버 컴포넌트로 분리돼 있고, 루트에 자동 사이클(StsCraneOperator)·
    /// VR 수동 조종(StsCraneVRController)·컨테이너 집기(SpreaderGrabber) 드라이버가 함께 붙는다.
    ///
    /// ※ 보류(미사용)된 디테일 형상 블록은 같은 폴더의 DEFERRED_DETAILS.md 에 백업돼 있다.
    /// </summary>
    public static class StsCraneCreator
    {
        const float Scale = 1f / 24f;

        // 트롤리 가동 (붐 로컬 X) — 음수=육지쪽 backreach, 양수=바다쪽 outreach
        const float TrolleyMinX  = -4f  * Scale - BoomBackExtra;   // 백트래블 한계 — 거더 백리치 연장(BoomBackExtra)만큼 트롤리도 더 뒤로
        const float TrolleyMaxX  =  42f * Scale;   // 앞(바다쪽) 약간 연장 38→42 — 붐 끝도 같이 나가 아웃리치 25→약29m
        const float TrolleyRestX =  8f  * Scale;   // ≈  0.333m

        // 높이/치수
        const float RailH    = 32f  * Scale;       // 붐(트롤리 레일) 높이 = 다리 높이 ≈ 실척 32m. STS 비율(인양고≈아웃리치31m, 붐길이54m×0.6, 게이지16×2.0)에 맞춰 23→32
        const float ApexH    = 20f  * Scale;       // A-프레임 정상 높이 — 16→20. 게이지 확장으로 정상(WaterLegX)이 바다쪽 이동 → 백스테이 클리어런스 유지 위해 더 높임
        const float GaugeZ   = 16f  * Scale;       // 레일 게이지(좌우 다리 간격, Z) — 40ft(12.192/24=0.508m) 길이 + 스프레더 양끝 클리어런스 수용 (12→16, 0.667m)
        const float LegSpanX = 15f  * Scale;       // 육지/바다 다리 간격(X=레일 게이지) ≈ 실척 15m — 비율(게이지≈0.6×아웃리치) 맞춰 9→15. 바다다리 이동으로 아웃리치≈25m
        const float LegSec   = 0.6f * Scale;       // 다리 단면 한 변
        const float LegTopY  = RailH + 0.088f;     // 포털 다리/상부 크로스빔/A프레임 베이스 공통 상단. 붐 거더 윗면(≈RailH+0.065)보다 살짝 위 → 거더가 크로스빔에 붙고 트롤리·거더가 그 아래
        // 트윈(더블 박스) 거더 — 두 박스 거더를 z=±GirderGapZ에 두고 사이를 횡프레임·평면 대각으로 결속.
        const float GirderGapZ   = 0.16f;                          // 각 거더 중심 Z — 붐 바깥 끝쪽까지 넓게(다리 게이지 ±0.333 안쪽)
        const float GirderWidthZ = 0.045f;                         // 각 박스 거더 단면 폭(Z)
        const float GirderOuterZ = GirderGapZ + GirderWidthZ * 0.5f;// 트윈 거더 바깥 가장자리(캣워크 난간 위치)

        // 붐 거더 X 끝점 — 거더/레일/격자/스테이가 공유(한 군데서 길이 관리)
        const float BoomBackExtra = 0.12f;             // 트롤리 백트래블 + 거더 백리치를 함께 뒤로 빼는 양(기계실은 고정)
        const float GantryRange   = 1.5f;              // 갠트리 주행 범위(±, 모델 단위) ≈ 실척 ±36m (~15 베이). 부두 sizeZ·QuayRail 길이도 연동 확장
        const float BoomBackX = TrolleyMinX - 0.27f;   // 백리치(육지쪽) 끝 — 트롤리 뒤 0.27 여유(TrolleyMinX가 이미 BoomBackExtra만큼 뒤로 감)
        const float BoomTipX  = TrolleyMaxX + 0.1f;    // 아웃리치 끝 — 트롤리 끝 + 팁 구조 여유(트롤리가 거의 끝까지)

        // 기계실 — 거더 백리치 연장과 무관하게 고정 위치. 백스테이가 기계실을 안 뚫게 앞(바다쪽)으로 MHForward만큼 당김.
        const float MHForward        = 0.12f;
        const float MachineryHouseHX = 0.085f;                              // 기계실 X 반폭(0.17의 절반)
        const float MachineryHouseX  = (-4f * Scale - 0.27f) + 0.11f + MHForward;  // 기계실 중심 X — 연장 전 백리치 기준에 고정(TrolleyMinX 변화에 안 휩쓸림) + 앞당김

        // 다리 X 위치(붐 로컬 = 루트 로컬, 붐이 루트 x=0에 있으므로 동일)
        const float LandLegX  = 0f;
        const float WaterLegX = LegSpanX;

        // 스프레더 승강 (spreaderRoot=붐 레벨 기준 로컬 Y, 음수=아래)
        const float SpreaderMaxY  = -4f  * Scale;           // 완전 상승 = 헤드블록이 트롤리 헤드 바로 아래 도킹. -3→-4: 헤드 치크(상단 hbY+0.0265)가 트롤리 헤드 하단(-0.0725)을 파고들어 -1*Scale 더 내려 ~9mm 여유 확보
        const float SpreaderMinY  = -(RailH - 0.8f * Scale); // 지면 직전(붐 높이에 연동)
        const float SpreaderRestY = -10f * Scale;

        // 스프레더 텔레스코픽 반길이(로컬 X, m). 중앙부는 항상 20ft, 좌/우 암이 사이즈별로 슬라이드.
        const float SpreaderHalf20 = 0.126f;   // 20ft (6.058/24/2 ≈ 0.1262)
        const float SpreaderHalf40 = 0.254f;   // 40ft (12.192/24/2 = 0.254)

        // 색
        static readonly Color CStruct  = new Color(0.82f, 0.83f, 0.85f); // 다리/포털/거더
        static readonly Color CBoom    = new Color(0.70f, 0.74f, 0.80f); // 붐 거더 본체
        static readonly Color CRail     = new Color(0.55f, 0.57f, 0.62f); // 트롤리 레일
        static readonly Color CMachine = new Color(0.30f, 0.33f, 0.38f); // 기계실
        static readonly Color CTrolley = new Color(0.95f, 0.45f, 0.10f); // 트롤리(안전 주황)
        static readonly Color CSpread  = new Color(0.98f, 0.80f, 0.10f); // 스프레더(안전 노랑)
        static readonly Color CDark    = new Color(0.13f, 0.13f, 0.15f); // 트위스트락/헤드블록
        static readonly Color CCable   = new Color(0.10f, 0.10f, 0.11f); // 케이블/로프
        static readonly Color CGlass   = new Color(0.25f, 0.55f, 0.70f); // 운전실 창
        static readonly Color CLight   = new Color(1.00f, 0.95f, 0.70f); // 작업등 렌즈
        static readonly Color CWarn    = new Color(0.90f, 0.10f, 0.10f); // 항공장애등(적색)
        // ⚠️ 임시 테스트색 — 4단계(와이어 시스템) 수정부 식별용(발광 시안그린). 확인 후 원래 색으로 되돌릴 것.
        static readonly Color CWireTest = new Color(0.00f, 1.00f, 0.55f);

        const string RootName = "STS_Crane";

        // 같은 색은 머티리얼 1개를 재사용(빌드 1회 한정)
        static Dictionary<Color, Material> _matCache;
        // 모든 머티리얼이 공유하는 절차 생성 강철 디테일 텍스처(_BaseColor로 틴트)
        static Texture2D _steelTex;
        // 모든 Box/Strut가 공유하는 모서리 베벨(챔퍼) 큐브 메시
        static Mesh _beveledCube;
        // 트러스 절점 연결판(거싯) — 같은 치수끼리 메시 1개 재사용
        static Dictionary<string, Mesh> _plateCache;
        // 접합부 볼트 패턴 — 한 메시에 여러 볼트 헤드. 같은 패턴끼리 메시 1개 재사용(드로콜·정점 절약)
        static Dictionary<string, Mesh> _boltCache;

        [MenuItem("Container/STS 크레인 생성", false, 0)]
        public static void CreateFromMenu40ft() => CreateAtContainer(SpreaderHalf40);

        // [MenuItem("Container/Create STS Crane 20ft")]   // 메뉴 숨김(사용자 요청, 일단) — 40ft 크레인이 텔레스코픽으로 20ft 커버
        public static void CreateFromMenu20ft() => CreateAtContainer(SpreaderHalf20);

        // 컨테이너 위치에 지정 스프레더 사이즈(반길이)로 생성 — 기존 인스턴스는 교체.
        static void CreateAtContainer(float spreaderHalf)
        {
            // 중복 방지 — 기존 인스턴스 제거(Undo 가능)
            var prev = GameObject.Find(RootName);
            if (prev != null) Undo.DestroyObjectImmediate(prev);

            // 컨테이너가 스프레더 정지 위치 아래에 오도록 배치
            Vector3 anchor = FindContainerAnchor();
            Vector3 pos = anchor - new Vector3(TrolleyRestX, 0f, 0f);

            var root = Create(pos, spreaderHalf);
            Selection.activeGameObject = root;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
        }

        /// <summary>
        /// hierarchy를 생성해서 root GameObject를 반환. 다른 에디터/런타임 코드에서도 호출 가능.
        /// Undo 시스템에 등록 → Ctrl+Z 한 번으로 되돌릴 수 있음.
        /// </summary>
        public static GameObject Create(Vector3 worldPosition, float spreaderHalf = SpreaderHalf40)
        {
            _matCache = new Dictionary<Color, Material>();
            _steelTex = null;
            _plateCache = new Dictionary<string, Mesh>();
            _boltCache = new Dictionary<string, Mesh>();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create STS Crane");
            root.transform.position = worldPosition;

            // 정적 구조
            BuildGroundRails(root.transform);
            BuildPortal(root.transform);
            BuildAccessDetails(root.transform);   // 사다리 + 점검 플랫폼

            // 붐 (루트 x=0, y=RailH) — 자식 트롤리/스프레더가 붐 로컬 좌표로 동작
            var boom = new GameObject("Boom");
            boom.transform.SetParent(root.transform, worldPositionStays: false);
            boom.transform.localPosition = new Vector3(0f, RailH, 0f);
            BuildBoomStructure(boom.transform);
            BuildBoomDetails(boom.transform);     // 보도·시브·작업등
            BuildBoomTrussDepth(boom.transform);  // 측면 워런 트러스(깊이감) + 측면 케이블 트레이
            BuildBoomSplices(boom.transform);     // 거더 스플라이스 연결판 + 볼트열
            BuildMachineryHouseAccess(boom.transform);  // 사다리 정상 ↔ 기계실 접근 캣워크 + 사다리쪽 출입문

            // A-프레임 + 스테이 케이블 (루트 레벨)
            BuildApexAndStays(root.transform);
            BuildBoltedJoints(root.transform);    // 주요 구조 접합부 연결판 + 볼트(다리/포털/실빔/브레이스)

            // 트롤리 (붐 직속 자식)
            var trolley = new GameObject("Trolley");
            trolley.transform.SetParent(boom.transform, worldPositionStays: false);
            BuildTrolleyVisual(trolley.transform);

            // 스프레더 루트 (트롤리의 형제 — TrolleyMover가 X 동기)
            var spreaderRoot = new GameObject("SpreaderRoot");
            spreaderRoot.transform.SetParent(boom.transform, worldPositionStays: false);

            var spreader = new GameObject("Spreader");
            spreader.transform.SetParent(spreaderRoot.transform, worldPositionStays: false);
            BuildSpreaderVisual(spreader.transform, spreaderHalf);
            spreader.AddComponent<SpreaderLockAnimator>();   // 트위스트락 잠금 모션

            var attachPoint = new GameObject("AttachPoint");
            attachPoint.transform.SetParent(spreader.transform, worldPositionStays: false);
            // 컨테이너 윗면이 여기에 정렬됨(SpreaderGrabber.Grab). 스프레더 본체 최하단(하단 Beam_Flange 밑면 ≈ -0.019)에
            // 맞춰야 빔·플랜지가 컨테이너 위에 얹히고 트위스트락만 코너 캐스팅으로 삽입된다. (-0.01이면 플랜지가 컨테이너에 묻힘)
            attachPoint.transform.localPosition = new Vector3(0f, -0.019f, 0f);

            // 호이스트 로프 — HoistRopeRig가 매 프레임 스프레더 Y에 맞춰 신축
            BuildHoistRopes(spreaderRoot.transform, spreader.transform);

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
            spreader.transform.localPosition = new Vector3(0f, SpreaderRestY, 0f);

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

            // 자동 구동 드라이버(데모) — Play 시 트롤리 왕복 + 스프레더 승강 사이클 반복
            root.AddComponent<StsCraneOperator>();
            // VR 컨트롤러 수동 조종 — 켜지면 자동 사이클을 끄고 스틱/트리거로 직접 조종
            root.AddComponent<StsCraneVRController>();
            // 컨테이너 집기/놓기 + 통과 방지(콜라이더 없는 컨테이너 대응)
            root.AddComponent<SpreaderGrabber>();

            _matCache = null;
            _steelTex = null;   // 텍스처는 머티리얼이 참조 유지 → 캐시 핸들만 해제
            _plateCache = null; // 거싯 메시도 GameObject가 참조 유지 → 핸들만 해제
            _boltCache = null;  // 볼트 메시도 GameObject가 참조 유지 → 핸들만 해제
            return root;
        }

        // ───────────────────────── 정적 구조 ─────────────────────────

        // 부두 위 주행 레일 — 안벽(quay)을 따라 Z축으로 깔린다(붐이 뻗는 X와 수직).
        // 육지측·바다측 다리행 아래에 1줄씩(두 레일 간격 = 레일 게이지 = LegSpanX).
        static void BuildGroundRails(Transform root)
        {
            // 크레인 측 레일은 '위치 마커'만 남기고 렌더러 OFF — 시각 레일은 부두(Quay_Ground)에 고정으로 그려진다.
            //   Quay는 이 Rail_ Transform의 X를 읽어 같은 X로 긴 고정 레일을 깐다(크레인과 같이 움직이지 않음).
            float railLen = GaugeZ + 0.5f;
            foreach (float x in new[] { LandLegX, WaterLegX })
            {
                var rail = Box(root, "Rail_" + (x == LandLegX ? "Land" : "Water"),
                    new Vector3(x, 0.004f, 0f),
                    new Vector3(0.02f, 0.008f, railLen), CRail);
                var mr = rail.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
        }

        // 포털 게이트: 다리 4개 + 실 빔 + 보기 + 상부 크로스 빔 + 대각 브레이스
        static void BuildPortal(Transform root)
        {
            float halfZ = GaugeZ * 0.5f;
            float[] legX = { LandLegX, WaterLegX };
            // 다리 상단 = LegTopY(공통 상수). 붐 거더 윗면에 크로스빔이 붙고, A프레임 베이스도 같은 높이로 정합.
            float legTopY = LegTopY;

            foreach (float x in legX)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * halfZ;
                    // 다리 — 격자 트러스(코너 포스트 + 가로 rung + 대각 lacing)
                    BuildLatticeLeg(root, x, z, 0f, legTopY, LegSec * 1.7f);
                    // 보기(주행 대차) — 이퀄라이저 빔 + 바퀴 4개. Z축으로 주행하므로 보기는 Z로 길다.
                    Box(root, "Bogie", new Vector3(x, 0.05f, z),
                        new Vector3(LegSec * 1.0f, 0.026f, LegSec * 2.6f), CDark);
                    Box(root, "Bogie_Beam", new Vector3(x, 0.032f, z),
                        new Vector3(LegSec * 0.45f, 0.016f, LegSec * 2.2f), CStruct);
                    for (int w = 0; w < 4; w++)
                    {
                        // 바퀴 — 원통(축은 주행레일 직각 = X), Z방향으로 굴러감
                        float wz = z + (w - 1.5f) * LegSec * 0.55f;
                        Rod(root, "Wheel",
                            new Vector3(x - LegSec * 0.65f, 0.014f, wz),
                            new Vector3(x + LegSec * 0.65f, 0.014f, wz),
                            0.013f, new Color(0.05f, 0.05f, 0.06f));
                    }
                }
            }

            // 좌우 다리를 잇는 상부 크로스 빔(포털 상단) — X 위치마다 1개.
            //   붐 상부 보도(Boom_Toe/Mid-rail/Walkway_Deck)가 이 빔과 같은 높이대라, 보도 부재 쪽을
            //   포털 통과 지점(육지/바다 다리 X)에서 끊어 개구부로 지나가게 한다(BoomTopWalkwayGaps 참조).
            foreach (float x in legX)
            {
                Box(root, "Portal_Cross", new Vector3(x, legTopY - LegSec * 0.5f, 0f),
                    new Vector3(LegSec * 0.8f, LegSec * 0.8f, GaugeZ + LegSec), CStruct);
            }

            // 측면 대각 브레이스(앞/뒤 다리 사이) — Sill_Beam(RailH*0.4)을 하현재로 그 위에 X 패턴
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * halfZ;
                Strut(root, "Brace",
                    new Vector3(LandLegX, RailH * 0.4f, z),
                    new Vector3(WaterLegX, RailH * 0.92f, z), 0.010f, CStruct);
                Strut(root, "Brace",
                    new Vector3(WaterLegX, RailH * 0.4f, z),
                    new Vector3(LandLegX, RailH * 0.92f, z), 0.010f, CStruct);
            }

            // 실 빔 — 같은 쪽 두 다리(육지·바다)를 잇는 종방향 빔(다리 중간보다 약간 아래 높이)
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, "Sill_Beam",
                    new Vector3((LandLegX + WaterLegX) * 0.5f, RailH * 0.4f, s * halfZ),
                    new Vector3(LegSpanX + LegSec, 0.02f, LegSec * 0.9f), CStruct);
            }

            // ── 전체 디테일 보강 (베이스/다리) ──
            foreach (float x in legX)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float lz = s * halfZ;
                    // 다리 베이스 받침 플레이트
                    Box(root, "Leg_BasePlate", new Vector3(x, 0.066f, lz),
                        new Vector3(LegSec * 2.4f, 0.012f, LegSec * 2.0f), CStruct);
                }
            }
            // 도관/케이블 번들 — 육지쪽 다리 +Z 코너를 따라 '베이스 플레이트(아래) → 붐 거더 밑(위)'까지 연속.
            //   양 끝을 정션박스로 구조물에 접속(아래=베이스, 위=붐 밑) → 위가 허공에 끊겨 떠 보이던 것 해결(계산: 끝점을 구조물에 맞춤).
            float condX = LandLegX - 0.024f;
            float condBotY = 0.072f;          // 베이스 플레이트 윗면(0.066 + 0.012/2) — 바닥 정션박스 접속
            float condTopY = RailH - 0.005f;  // 붐 거더 바로 아래(다리 상단) — 상단 정션박스로 접속
            for (int c = -1; c <= 1; c++)   // 3줄 번들
                Rod(root, "Leg_Conduit",
                    new Vector3(condX, condBotY, halfZ + c * 0.006f),
                    new Vector3(condX, condTopY, halfZ + c * 0.006f), 0.0035f, CDark);
            // 클램프(고정 브래킷) 다단 — 전 구간을 다리에 붙임
            for (int i = 0; i <= 8; i++)
            {
                float cy = Mathf.Lerp(condBotY + 0.02f, condTopY - 0.02f, i / 8f);
                Box(root, "Conduit_Clamp", new Vector3(condX + 0.008f, cy, halfZ),
                    new Vector3(0.018f, 0.005f, 0.026f), CStruct);
            }
            // 정션 박스(상·하) — 도관 양 끝에 정확히 얹어 구조물 접속부 표현(끝이 안 뜨게)
            Box(root, "Junction_Box", new Vector3(condX, condBotY + 0.016f, halfZ),
                new Vector3(0.02f, 0.032f, 0.026f), CMachine);
            Box(root, "Junction_Box", new Vector3(condX, condTopY - 0.016f, halfZ),
                new Vector3(0.02f, 0.032f, 0.026f), CMachine);

            // ── 베이스/부두 인터페이스 디테일 ──
            foreach (float x in legX)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float lz = s * halfZ;
                    // 폭풍 계류 스토우 핀(부두에 박는 핀) + 하우징
                    Box(root, "Stow_Pin_Housing", new Vector3(x, 0.045f, lz + s * LegSec * 1.5f),
                        new Vector3(0.022f, 0.05f, 0.022f), CMachine);
                    Rod(root, "Stow_Pin", new Vector3(x, 0f, lz + s * LegSec * 1.5f),
                        new Vector3(x, 0.05f, lz + s * LegSec * 1.5f), 0.006f, CDark);
                    // 타이다운 러그(부두 고정 고리)
                    Box(root, "Tiedown_Lug", new Vector3(x + 0.03f, 0.012f, lz),
                        new Vector3(0.012f, 0.018f, 0.01f), CStruct);
                    // 휠 레일 스위퍼(주행방향 Z 앞뒤 청소판 — 판은 레일을 X로 가로지름)
                    for (int e = -1; e <= 1; e += 2)
                        Box(root, "Rail_Sweeper", new Vector3(x, 0.006f, lz + e * LegSec * 1.4f),
                            new Vector3(LegSec * 1.1f, 0.01f, 0.006f), CDark);
                }
            }
        }

        // 붐: 메인 거더 + 주행 레일 + 격자 대각 + 기계실 (붐 로컬 좌표)
        static void BuildBoomStructure(Transform boom)
        {
            float x0 = BoomBackX;   // 백리치 끝
            float x1 = BoomTipX;    // 아웃리치 끝(바다쪽)
            float len = x1 - x0;
            float mid = (x0 + x1) * 0.5f;

            // ── 트윈(더블 박스) 거더 — 두 박스 거더(z=±GirderGapZ) + 거더별 하단 주행 레일 ──
            const float gY = 0.04f, gH = 0.05f;        // 거더 중심 높이 / 단면 높이
            float gTop = gY + gH * 0.5f;               // 거더 윗면(=0.065)
            float gBot = gY - gH * 0.5f;               // 거더 밑면(=0.015)
            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * GirderGapZ;
                Box(boom, "Boom_Girder", new Vector3(mid, gY, gz),
                    new Vector3(len, gH, GirderWidthZ), CBoom);
                // 트롤리 주행 레일(거더 하단)
                Box(boom, "Boom_Rail", new Vector3(mid, 0.006f, gz),
                    new Vector3(len, 0.012f, GirderWidthZ * 0.8f), CRail);
            }

            // ── 두 거더 결속: 횡프레임(상·하 횡재 + 거더별 수직재) ──
            int frames = 11;
            float crossBot = gBot + 0.004f, crossTop = gTop - 0.004f;
            for (int i = 0; i <= frames; i++)
            {
                float fx = Mathf.Lerp(x0, x1, i / (float)frames);
                Box(boom, "Boom_Cross", new Vector3(fx, crossBot, 0f),
                    new Vector3(0.006f, 0.006f, 2f * GirderGapZ), CStruct);
                Box(boom, "Boom_Cross", new Vector3(fx, crossTop, 0f),
                    new Vector3(0.006f, 0.006f, 2f * GirderGapZ), CStruct);
                for (int s = -1; s <= 1; s += 2)
                    Box(boom, "Boom_Vertical", new Vector3(fx, gY, s * GirderGapZ),
                        new Vector3(0.006f, gH, 0.006f), CStruct);
            }

            // ── 두 거더 사이 평면 대각 브레이스(바닥면 X 지그재그) — 비틀림 강성 표현 ──
            int dseg = 11;
            float dstep = len / dseg;
            for (int i = 0; i < dseg; i++)
            {
                float xa = x0 + dstep * i;
                float xb = x0 + dstep * (i + 1);
                bool up = (i % 2) == 0;
                Strut(boom, "Boom_Plan_Brace",
                    new Vector3(xa, crossBot, up ? -GirderGapZ : GirderGapZ),
                    new Vector3(xb, crossBot, up ?  GirderGapZ : -GirderGapZ),
                    0.004f, CStruct);
            }

            // 기계실(육지쪽 위) + 디테일 — 붐 가로(Z)로 넓혀 육중하게(거더보다 양옆 돌출)
            // ※ 기계실은 고정(거더만 뒤로 연장) + 앞으로 MHForward만큼 당김 — 백스테이가 기계실을 안 뚫게
            float mhx = MachineryHouseX;
            float mhZ = 2f * GirderGapZ + 0.08f;   // Z 폭 — 트윈 거더(±GirderGapZ)를 가로질러 얹히게 넓힘(거더 바깥으로 약간 오버행)
            float mhHZ = mhZ * 0.5f;       // Z 반폭
            float mhHX = MachineryHouseHX; // X 반폭(0.17의 절반)
            Box(boom, "Machinery_House", new Vector3(mhx, 0.105f, 0f),
                new Vector3(0.17f, 0.11f, mhZ), CMachine);
            Box(boom, "MH_Roof", new Vector3(mhx, 0.165f, 0f),
                new Vector3(0.185f, 0.012f, mhZ + 0.01f), CStruct);
            // 출입문 세트 숨김(사용자 요청) — 손잡이는 문이 없으면 떠서 함께 숨김. 복구하려면 주석 해제
            // Box(boom, "MH_DoorFrame", new Vector3(mhx + 0.085f, 0.086f, 0f),
            //     new Vector3(0.004f, 0.066f, 0.05f), CStruct);
            // Box(boom, "MH_Door", new Vector3(mhx + 0.087f, 0.085f, 0f),
            //     new Vector3(0.005f, 0.058f, 0.042f), CDark);
            // Box(boom, "MH_DoorHandle", new Vector3(mhx + 0.0905f, 0.085f, 0.014f),
            //     new Vector3(0.004f, 0.012f, 0.004f), CStruct);
            // Box(boom, "MH_DoorLanding", new Vector3(mhx + 0.1f, 0.056f, 0f),
            //     new Vector3(0.03f, 0.004f, 0.05f), CMachine);
            // 루버 환기 패널(양 ±Z면 육지쪽) — 프레임 + 가로 슬랫 다단, 상·하 2뱅크
            for (int s = -1; s <= 1; s += 2)
            {
                float fz = s * mhHZ;
                foreach (float vy in new[] { 0.118f, 0.078f })
                {
                    Box(boom, "MH_VentFrame", new Vector3(mhx - 0.05f, vy, fz - s * 0.001f),
                        new Vector3(0.042f, 0.034f, 0.005f), CDark);
                    for (int l = -2; l <= 2; l++)
                        Box(boom, "MH_Louver", new Vector3(mhx - 0.05f, vy + l * 0.007f, fz),
                            new Vector3(0.038f, 0.004f, 0.004f), CStruct);
                }
            }
            EquipmentUnit(boom, "MH_AC_Unit", new Vector3(mhx - 0.055f, 0.18f, 0f),
                new Vector3(0.05f, 0.028f, 0.05f), CDark);
            // 기계실 추가 디테일 — 배기구·창·보조 E-house·케이블 트레이·붐호이스트 윈치
            Rod(boom, "MH_Exhaust", new Vector3(mhx + 0.035f, 0.175f, 0.02f),
                new Vector3(mhx + 0.035f, 0.215f, 0.02f), 0.006f, CDark);
            // 창 — 큰 단일 유리 → 리세스 프레임 + 분할 유리 3칸 + 세로 멀리언.
            //   +Z(사다리)면은 출입문으로 대체(BuildMachineryHouseAccess)하므로 창은 -Z면에만.
            for (int s = -1; s <= 1; s += 2)
            {
                if (s > 0) continue;   // +Z = 사다리 접근측 → 창 대신 문
                float fz = s * mhHZ;
                Box(boom, "MH_WinFrame", new Vector3(mhx + 0.01f, 0.118f, fz - s * 0.002f),
                    new Vector3(0.094f, 0.032f, 0.005f), CDark);
                for (int g = -1; g <= 1; g++)
                    Box(boom, "MH_Window", new Vector3(mhx + 0.01f + g * 0.03f, 0.118f, fz),
                        new Vector3(0.024f, 0.024f, 0.004f), CGlass);
                for (int m = -1; m <= 1; m += 2)
                    Box(boom, "MH_Mullion", new Vector3(mhx + 0.01f + m * 0.015f, 0.118f, fz),
                        new Vector3(0.004f, 0.03f, 0.004f), CStruct);
            }
            Box(boom, "Cable_Tray", new Vector3(mhx + 0.18f, 0.05f, GirderGapZ),
                new Vector3(0.35f, 0.006f, 0.008f), CDark);
            Rod(boom, "Boom_Hoist_Drum",
                new Vector3(mhx - 0.06f, 0.04f, -0.03f),
                new Vector3(mhx - 0.06f, 0.04f,  0.03f), 0.024f, CDark);
            // 기계실 지붕 난간 — 4변 둘레 레일 + 토보드(킥플레이트) + 둘레 기둥
            float mhRoofY = 0.171f;
            float rlZ = mhHZ + 0.003f, rlX = mhHX + 0.003f, rlH = 0.026f;
            for (int s = -1; s <= 1; s += 2)
            {
                // ±Z 긴 변
                Box(boom, "MH_Roof_Rail", new Vector3(mhx, mhRoofY + rlH, s * rlZ),
                    new Vector3(rlX * 2f, 0.004f, 0.004f), CStruct);
                Box(boom, "MH_Roof_Toe", new Vector3(mhx, mhRoofY + 0.006f, s * rlZ),
                    new Vector3(rlX * 2f, 0.009f, 0.003f), CStruct);
                // ±X 끝 변
                Box(boom, "MH_Roof_Rail", new Vector3(mhx + s * rlX, mhRoofY + rlH, 0f),
                    new Vector3(0.004f, 0.004f, rlZ * 2f), CStruct);
                Box(boom, "MH_Roof_Toe", new Vector3(mhx + s * rlX, mhRoofY + 0.006f, 0f),
                    new Vector3(0.003f, 0.009f, rlZ * 2f), CStruct);
            }
            // 둘레 기둥(코너 4 + 각 변 중간 4) — 3×3 격자에서 내부 1칸만 제외
            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                if (ix == 0 && iz == 0) continue;
                Box(boom, "MH_Roof_Post", new Vector3(mhx + ix * rlX, mhRoofY + rlH * 0.5f, iz * rlZ),
                    new Vector3(0.004f, rlH, 0.004f), CStruct);
            }

            // ── 기계실 표면/장비 디테일(이 박스만 고도화) ──
            float mhFZ = mhHZ;
            // 수직 코너 엣지 포스트 4(상자 모서리 트림)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Box(boom, "MH_Corner", new Vector3(mhx + sx * mhHX, 0.105f, sz * mhFZ),
                    new Vector3(0.006f, 0.112f, 0.006f), CStruct);
            // 패널 이음매(가로 2단 + 세로 2줄, 창 비껴서) — 큰 ±Z면 평탄함 제거
            for (int s = -1; s <= 1; s += 2)
            {
                float fz = s * (mhFZ + 0.001f);
                foreach (float ry in new[] { 0.062f, 0.148f })
                    Box(boom, "MH_PanelSeam", new Vector3(mhx, ry, fz),
                        new Vector3(0.166f, 0.0035f, 0.003f), CStruct);
                for (int c = -1; c <= 1; c += 2)
                    Box(boom, "MH_PanelSeam", new Vector3(mhx + c * 0.055f, 0.105f, fz),
                        new Vector3(0.003f, 0.106f, 0.003f), CStruct);
            }
            // 벽면 정션박스 2 + 수직 도관(육지쪽 +Z면)
            JunctionBox(boom, "MH_JBox", new Vector3(mhx - 0.07f, 0.09f, mhFZ),
                new Vector3(0.016f, 0.022f, 0.008f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f), CDark);
            JunctionBox(boom, "MH_JBox", new Vector3(mhx - 0.07f, 0.06f, mhFZ),
                new Vector3(0.012f, 0.016f, 0.007f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f), CStruct);
            Rod(boom, "MH_Conduit", new Vector3(mhx - 0.07f, 0.055f, mhFZ),
                new Vector3(mhx - 0.07f, 0.155f, mhFZ), 0.003f, CDark);

            // 다리 도관 → 기계실 정션박스(MH_JBox) 연결 — '데크/난간 밑(runY)'으로 라우팅(붐 로컬 좌표).
            //   다리 도관 상단(붐로컬 y≈-0.005, x=condX, z=legZ)에서 데크(0.067) 아래를 따라 X→Z로 와서 JBox(y=0.06)에 종단.
            //   엔드포인트는 실제 부품(다리 도관 끝 ↔ MH_JBox)에 맞춰 계산 — 허공/이상한 데로 안 감.
            {
                float condX2 = LandLegX - 0.024f;     // 다리 도관 x (root/boom 공통)
                float legZ   = GaugeZ * 0.5f;          // 다리 z (= halfZ)
                float runY   = 0.05f;                  // 데크(0.067) 밑 — 난간 아래로 통과
                float jbX = mhx - 0.07f, jbY = 0.06f, jbZ = mhFZ;   // MH_JBox 위치
                float r   = 0.006f;                    // 3줄 번들을 한 다발로
                Rod(boom, "Leg_Conduit_Link", new Vector3(condX2, -0.005f, legZ), new Vector3(condX2, runY, legZ), r, CDark); // 상승(데크 밑까지)
                Rod(boom, "Leg_Conduit_Link", new Vector3(condX2, runY, legZ),    new Vector3(jbX,   runY, legZ),   r, CDark); // X 수평(데크 밑)
                Rod(boom, "Leg_Conduit_Link", new Vector3(jbX, runY, legZ),       new Vector3(jbX,   runY, jbZ),    r, CDark); // Z 수평(기계실 벽쪽)
                Rod(boom, "Leg_Conduit_Link", new Vector3(jbX, runY, jbZ),        new Vector3(jbX,   jbY,  jbZ),    r, CDark); // JBox로 상승
                Box(boom, "Conduit_Elbow_JBox", new Vector3(jbX, runY, legZ),
                    new Vector3(0.022f, 0.026f, 0.026f), CMachine);   // 꺾이는 모서리 엘보 정션
            }
            // 벽면 작업등 2(아래 향함, 양 ±Z면 바다쪽 상단)
            for (int s = -1; s <= 1; s += 2)
            {
                Box(boom, "MH_WallLight_Housing", new Vector3(mhx + 0.06f, 0.152f, s * mhFZ),
                    new Vector3(0.012f, 0.01f, 0.012f), CDark);
                Ball(boom, "MH_WallLight", new Vector3(mhx + 0.06f, 0.146f, s * mhFZ),
                    new Vector3(0.009f, 0.006f, 0.009f), CLight);
            }
            // 지붕 디테일 — 점검 해치 + 보조 HVAC + 배기 캡
            Box(boom, "MH_RoofHatch", new Vector3(mhx + 0.06f, mhRoofY + 0.006f, -0.012f),
                new Vector3(0.03f, 0.008f, 0.03f), CDark);
            EquipmentUnit(boom, "MH_HVAC2", new Vector3(mhx + 0.03f, mhRoofY + 0.014f, 0.025f),
                new Vector3(0.04f, 0.022f, 0.03f), CDark);
            Ball(boom, "MH_ExhaustCap", new Vector3(mhx + 0.035f, 0.218f, 0.02f),
                new Vector3(0.012f, 0.008f, 0.012f), CDark);
            // 지붕 접근 수직 사다리(바다쪽 +Z면 → 지붕)
            BuildLadder(boom, mhx + 0.07f, mhFZ + 0.008f, 0.06f, mhRoofY, 0.02f);

            // ════════ 기계실 전체 마감/디테일 (넓힌 게이지 폭에 맞춤) ════════
            float mhTopY = 0.16f, mhBotY = 0.05f;

            // 마감: 상부 처마(eave) + 하부 실(sill) 둘레 띠 — 박스 4변 테두리 정리
            for (int s = -1; s <= 1; s += 2)
            {
                Box(boom, "MH_Eave", new Vector3(mhx, mhTopY + 0.003f, s * (mhHZ + 0.004f)),
                    new Vector3(0.176f, 0.006f, 0.006f), CStruct);
                Box(boom, "MH_Sill", new Vector3(mhx, mhBotY + 0.001f, s * (mhHZ + 0.003f)),
                    new Vector3(0.176f, 0.008f, 0.007f), CStruct);
                Box(boom, "MH_Eave", new Vector3(mhx + s * (mhHX + 0.004f), mhTopY + 0.003f, 0f),
                    new Vector3(0.006f, 0.006f, mhZ + 0.008f), CStruct);
                Box(boom, "MH_Sill", new Vector3(mhx + s * (mhHX + 0.003f), mhBotY + 0.001f, 0f),
                    new Vector3(0.007f, 0.008f, mhZ + 0.006f), CStruct);
            }

            // 육지쪽(-X) 끝면: 대형 루버 흡기 뱅크 3(프레임 + 가로 슬랫)
            float mhEndX = mhx - mhHX - 0.001f;
            for (int iz = -1; iz <= 1; iz++)
            {
                float lz = iz * 0.072f;
                Box(boom, "MH_EndLouverFrame", new Vector3(mhEndX, 0.103f, lz),
                    new Vector3(0.005f, 0.062f, 0.052f), CDark);
                for (int l = -3; l <= 3; l++)
                    Box(boom, "MH_EndLouver", new Vector3(mhEndX - 0.001f, 0.103f + l * 0.008f, lz),
                        new Vector3(0.004f, 0.004f, 0.048f), CStruct);
            }

            // 바다쪽(+X) 끝면 창 숨김(사용자 요청) — 복구하려면 주석 해제
            // float mhEndX2 = mhx + mhHX + 0.001f;
            // for (int s = -1; s <= 1; s += 2)
            // {
            //     Box(boom, "MH_EndWinFrame", new Vector3(mhEndX2, 0.114f, s * 0.078f),
            //         new Vector3(0.005f, 0.03f, 0.03f), CDark);
            //     Box(boom, "MH_EndWindow", new Vector3(mhEndX2 + 0.001f, 0.114f, s * 0.078f),
            //         new Vector3(0.004f, 0.024f, 0.024f), CGlass);
            // }

            // 넓어진 지붕 양 끝(Z flank) 장비 — 콘덴서 2 + 팬그릴, 버섯 벤트 2, 케이블 트레이 횡단
            for (int s = -1; s <= 1; s += 2)
            {
                EquipmentUnit(boom, "MH_Condenser", new Vector3(mhx - 0.02f, mhRoofY + 0.016f, s * 0.085f),
                    new Vector3(0.06f, 0.03f, 0.05f), CDark);
                Rod(boom, "MH_RoofVent", new Vector3(mhx + 0.055f, mhRoofY + 0.004f, s * 0.08f),
                    new Vector3(mhx + 0.055f, mhRoofY + 0.022f, s * 0.08f), 0.005f, CStruct);
                Ball(boom, "MH_RoofVent_Cap", new Vector3(mhx + 0.055f, mhRoofY + 0.024f, s * 0.08f),
                    new Vector3(0.014f, 0.009f, 0.014f), CStruct);
            }
            Box(boom, "MH_RoofTray", new Vector3(mhx + 0.02f, mhRoofY + 0.008f, 0f),
                new Vector3(0.012f, 0.005f, mhZ * 0.85f), CDark);

            // 지붕 네 모서리 적색 마커등(항공/안전)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Ball(boom, "MH_CornerLight", new Vector3(mhx + sx * mhHX, mhRoofY + 0.012f, sz * mhHZ),
                    new Vector3(0.007f, 0.007f, 0.007f), CWarn);

            // 넓은 ±Z면 보강 — 수직 도관 + 벽 작업등(육지쪽 분산)
            for (int s = -1; s <= 1; s += 2)
            {
                Rod(boom, "MH_WallConduit", new Vector3(mhx + 0.05f, 0.055f, s * (mhHZ + 0.0015f)),
                    new Vector3(mhx + 0.05f, 0.15f, s * (mhHZ + 0.0015f)), 0.003f, CDark);
                Box(boom, "MH_WallLight_Housing", new Vector3(mhx - 0.02f, 0.152f, s * (mhHZ + 0.001f)),
                    new Vector3(0.012f, 0.01f, 0.012f), CDark);
                Ball(boom, "MH_WallLight", new Vector3(mhx - 0.02f, 0.146f, s * (mhHZ + 0.001f)),
                    new Vector3(0.009f, 0.006f, 0.009f), CLight);
            }

            // 붐 상단 양옆 난간(walkway railing) — 트윈 거더 바깥 가장자리
            float girderTop = 0.065f;   // 거더 윗면
            float railTop   = 0.105f;   // 상단 가로 레일 높이
            int posts = 7;
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * GirderOuterZ;
                Box(boom, "Boom_Railing", new Vector3(mid, railTop, z),
                    new Vector3(len, 0.006f, 0.006f), CStruct);
                // 중간 가로대 — 포털 빔 통과 지점에서 끊음
                BoxGappedX(boom, "Boom_Railing_Mid", x0, x1, (railTop + girderTop) * 0.5f, z,
                    0.004f, 0.004f, CStruct, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
                // 토보드(킥플레이트) — 보도 가장자리. 포털 빔 통과 지점에서 끊음(Portal_Cross 관통 방지)
                BoxGappedX(boom, "Boom_Toe", x0, x1, girderTop + 0.009f, z,
                    0.012f, 0.003f, CStruct, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
                for (int i = 0; i <= posts; i++)
                {
                    float px = Mathf.Lerp(x0, x1, i / (float)posts);
                    Box(boom, "Railing_Post",
                        new Vector3(px, (railTop + girderTop) * 0.5f, z),
                        new Vector3(0.005f, railTop - girderTop, 0.005f), CStruct);
                }
            }
        }

        // A-프레임 정상 + 포어/백 스테이 케이블 (루트 로컬)
        static void BuildApexAndStays(Transform root)
        {
            float halfZ = GaugeZ * 0.5f;
            float apexHalfZ = GaugeZ * 0.35f;   // 정상 가로보(Apex, 폭 GaugeZ*0.7) 끝=A-프레임 다리가 물리는 코너
            Vector3 apex = new Vector3(WaterLegX, RailH + ApexH, 0f);

            // 정상 가로보
            Box(root, "Apex", new Vector3(apex.x, apex.y, 0f),
                new Vector3(0.04f, 0.04f, GaugeZ * 0.7f), CStruct);

            // 정상 점검 플랫폼(데크 + 난간 기둥/상·중단 가로대 + 제어 캐비닛)
            float platY = apex.y + 0.03f;
            float halfX = 0.035f;
            float halfPZ = GaugeZ * 0.275f;
            float railH2 = 0.032f;

            Box(root, "Apex_Platform", new Vector3(apex.x, platY, 0f),
                new Vector3(halfX * 2f, 0.008f, halfPZ * 2f), CStruct);

            // 난간 기둥(둘레 8개)
            float[] gx = { -halfX, 0f, halfX };
            float[] gz = { -halfPZ, 0f, halfPZ };
            foreach (float fx in gx)
            foreach (float fz in gz)
            {
                if (Mathf.Abs(fx) >= halfX - 1e-4f || Mathf.Abs(fz) >= halfPZ - 1e-4f)
                    Box(root, "Apex_Post", new Vector3(apex.x + fx, platY + railH2 * 0.5f, fz),
                        new Vector3(0.004f, railH2, 0.004f), CStruct);
            }
            // 상단·중간 가로 레일(4변 × 2단)
            foreach (float h in new[] { railH2, railH2 * 0.55f })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(root, "Apex_Rail", new Vector3(apex.x, platY + h, s * halfPZ),
                        new Vector3(halfX * 2f, 0.004f, 0.004f), CStruct);
                    Box(root, "Apex_Rail", new Vector3(apex.x + s * halfX, platY + h, 0f),
                        new Vector3(0.004f, 0.004f, halfPZ * 2f), CStruct);
                }
            }
            // 제어 캐비닛 — 직립 인클로저(도어/루버/지붕/공조)
            ControlCabinet(root, "Apex_Cabinet", new Vector3(apex.x - halfX * 0.4f, platY + 0.02f, halfPZ * 0.45f),
                new Vector3(0.025f, 0.03f, 0.02f), new Vector3(1f, 0f, 0f), CMachine);
            // 토보드(난간 하부 킥플레이트, 4변) — 공구 낙하 방지 + 디테일
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, "Apex_ToeBoard", new Vector3(apex.x, platY + 0.007f, s * (halfPZ - 0.001f)),
                    new Vector3(halfX * 2f, 0.01f, 0.003f), CStruct);
                Box(root, "Apex_ToeBoard", new Vector3(apex.x + s * (halfX - 0.001f), platY + 0.007f, 0f),
                    new Vector3(0.003f, 0.01f, halfPZ * 2f), CStruct);
            }
            // 정션 박스 2(반대편 데크) — 데크 볼트 고정형(덮개는 빔 방향, 글랜드는 데크 아래로)
            JunctionBox(root, "Apex_JBox", new Vector3(apex.x + halfX * 0.45f, platY + 0.016f, -halfPZ * 0.5f),
                new Vector3(0.016f, 0.022f, 0.014f), new Vector3(1f, 0f, 0f), Vector3.down, CDark);
            JunctionBox(root, "Apex_JBox", new Vector3(apex.x - halfX * 0.5f, platY + 0.012f, -halfPZ * 0.2f),
                new Vector3(0.012f, 0.016f, 0.012f), new Vector3(-1f, 0f, 0f), Vector3.down, CMachine);

            // 비콘 마스트 — 단단한 마스트 + 하우징 달린 적색 항공장애등 2단 + 풍속계 + 피뢰침
            float mb = platY + 0.012f;
            float mastTop = mb + 0.10f;
            // 받침 플랜지 2단(원형)
            Rod(root, "Mast_Base", new Vector3(apex.x, mb - 0.006f, 0f),
                new Vector3(apex.x, mb + 0.004f, 0f), 0.012f, CMachine);
            Rod(root, "Mast_Base", new Vector3(apex.x, mb + 0.004f, 0f),
                new Vector3(apex.x, mb + 0.012f, 0f), 0.0075f, CStruct);
            // 마스트 기둥(약간 굵게)
            Rod(root, "Apex_Mast", new Vector3(apex.x, mb + 0.012f, 0f),
                new Vector3(apex.x, mastTop, 0f), 0.0035f, CStruct);
            // 적색 항공장애등 2단 — 검은 하우징(짧은 원통) + 적색 돔(작게)
            foreach (float ly in new[] { mb + 0.045f, mb + 0.085f })
            {
                Rod(root, "AviLight_Housing", new Vector3(apex.x, ly - 0.005f, 0f),
                    new Vector3(apex.x, ly + 0.003f, 0f), 0.0085f, CDark);
                Ball(root, "Aviation_Light", new Vector3(apex.x, ly + 0.008f, 0f),
                    new Vector3(0.011f, 0.008f, 0.011f), CWarn);
            }
            // 피뢰침(마스트 꼭대기 → 뾰족)
            Cone(root, "Lightning_Rod", new Vector3(apex.x, mastTop, 0f),
                new Vector3(apex.x, mastTop + 0.028f, 0f), 0.002f, 0f, CStruct, 12);
            // 풍속계 — 측면 브래킷 + 수직 스핀들 + 수평 3컵(맞바람에 도는 컵)
            Vector3 anBrkt = new Vector3(apex.x, mb + 0.07f, 0.022f);
            Rod(root, "Anemo_Arm", new Vector3(apex.x, mb + 0.07f, 0.004f), anBrkt, 0.0015f, CStruct);
            Vector3 anHub = anBrkt + new Vector3(0f, 0.012f, 0f);
            Rod(root, "Anemo_Spindle", anBrkt, anHub, 0.0014f, CDark);
            Ball(root, "Anemo_Hub", anHub, new Vector3(0.006f, 0.006f, 0.006f), CDark);
            for (int k = 0; k < 3; k++)
            {
                float a3 = k * Mathf.PI * 2f / 3f;
                Vector3 cupP = anHub + new Vector3(Mathf.Cos(a3) * 0.011f, 0.001f, Mathf.Sin(a3) * 0.011f);
                Rod(root, "Anemo_CupArm", anHub, cupP, 0.0008f, CStruct);
                Ball(root, "Anemo_Cup", cupP, new Vector3(0.005f, 0.005f, 0.005f), CStruct);
            }

            // A-프레임 다리 4개 — 같은 쪽 정상 코너(±apexHalfZ)에서 같은 쪽 다리 상단으로 물림
            // → port/star 두 개의 A가 정상 가로보로 묶이는 형상(한 점 수렴 아님)
            foreach (float lx in new[] { LandLegX, WaterLegX })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Strut(root, "Aframe",
                        new Vector3(apex.x, apex.y, s * apexHalfZ),
                        new Vector3(lx, LegTopY, s * halfZ), 0.012f, CStruct);
                }
            }

            // A-프레임 면을 격자 트러스로 — 다단 가로 브레이스 + 대각 지그재그(앞·뒤 면)
            float[] lv = { 0.20f, 0.40f, 0.60f, 0.80f };
            foreach (float legX in new[] { LandLegX, WaterLegX })
            {
                foreach (float t in lv)
                {
                    float bx = Mathf.Lerp(WaterLegX, legX, t);
                    float by = Mathf.Lerp(apex.y, LegTopY, t);
                    // 가로 브레이스 폭 — 꼭대기(코너 ±apexHalfZ)에서 다리 베이스(±halfZ)로
                    Box(root, "Aframe_Brace", new Vector3(bx, by, 0f),
                        new Vector3(0.007f, 0.007f, 2f * Mathf.Lerp(apexHalfZ, halfZ, t)), CStruct);
                }
                // 큰 X 하나 — 면의 꼭대기 코너(±apexHalfZ)에서 다리 베이스 반대편(±halfZ)으로 교차(지그재그 제거)
                Vector3 tN = new Vector3(apex.x, apex.y, -apexHalfZ);
                Vector3 tP = new Vector3(apex.x, apex.y,  apexHalfZ);
                Vector3 bN = new Vector3(legX, LegTopY, -halfZ);
                Vector3 bP = new Vector3(legX, LegTopY,  halfZ);
                Strut(root, "Aframe_Lace", tN, bP, 0.008f, CStruct);
                Strut(root, "Aframe_Lace", tP, bN, 0.008f, CStruct);
            }

            // A-프레임 측면(port/star) 가로 브레이스 — 바다·육지 다리쌍 연결
            foreach (float t in new[] { 0.38f, 0.66f })
            {
                float lx = Mathf.Lerp(WaterLegX, LandLegX, t);
                float yy = Mathf.Lerp(apex.y, LegTopY, t);
                float sz = Mathf.Lerp(apexHalfZ, halfZ, t);   // 다리의 안쪽 기울기에 맞춤(코너→베이스)
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(root, "Aframe_SideBrace",
                        new Vector3((WaterLegX + lx) * 0.5f, yy, s * sz),
                        new Vector3(Mathf.Abs(WaterLegX - lx), 0.006f, 0.006f), CStruct);
                }
            }

            // A-프레임 절점 거싯 — 정상 코너 결합부 + 다리 상단 결합부(측면 ±Z 면에 연결판).
            //   A-프레임 다리(Strut)·라싱이 한 점에 모이는 곳에 용접 거싯을 덧대 절점 표현.
            float afG = 0.013f, afT = 0.004f, afOff = 0.003f;
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion fr = s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                // 정상 코너(가로보 끝, A-프레임 다리가 물리는 곳)
                Gusset(root, new Vector3(apex.x, apex.y, s * apexHalfZ + s * afOff), fr, afG, afT, CStruct);
                // 다리 상단 결합부(육지/바다 다리)
                foreach (float lx in new[] { LandLegX, WaterLegX })
                    Gusset(root, new Vector3(lx, LegTopY + 0.012f, s * halfZ + s * afOff), fr, afG, afT, CStruct);
            }

            // 정상 시브 네스트(스테이 도르래) + 장비 하우징 — 시브를 감싸게 거더 폭에 맞춤
            Box(root, "Apex_SheaveHouse", new Vector3(apex.x, apex.y - 0.025f, 0f),
                new Vector3(0.03f, 0.05f, 2f * GirderGapZ + 0.08f), CMachine);
            // 정상 시브 — 양쪽(z=±GirderGapZ)에 1개씩, 각 거더 포어스테이를 받음(축은 Z)
            for (int s = -1; s <= 1; s += 2)
            {
                float sz = s * GirderGapZ;
                Rod(root, "Apex_Sheave",
                    new Vector3(apex.x, apex.y - 0.01f, sz - 0.016f),
                    new Vector3(apex.x, apex.y - 0.01f, sz + 0.016f), 0.016f, CDark);
                // 정상 시브 네스트(치크+핀 보스)
                SheaveNest(root, new Vector3(apex.x, apex.y - 0.01f, sz), 0.016f, 0.016f, CStruct);
            }
            // 시브 하우스 디테일 — 측면 리브 + 점검 해치 + 리프팅 러그
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, "SheaveHouse_Rib", new Vector3(apex.x, apex.y - 0.025f, s * GaugeZ * 0.24f),
                    new Vector3(0.034f, 0.05f, 0.004f), CStruct);
            }
            Box(root, "SheaveHouse_Hatch", new Vector3(apex.x + 0.016f, apex.y - 0.015f, 0f),
                new Vector3(0.004f, 0.02f, 0.02f), CDark);
            Box(root, "Lifting_Lug", new Vector3(apex.x, apex.y + 0.008f, 0f),
                new Vector3(0.006f, 0.014f, 0.006f), CStruct);

            // 정상 작업조명 갤러리 — 플랫폼 바다쪽 가장자리에서 받침대로 뻗은 프레임 +
            // 하우징 달린 플러드라이트(아래·바다쪽 작업면을 비춤). 떠 있지 않게 받침으로 고정.
            float galX = apex.x + 0.042f;
            float galY = platY - 0.004f;
            Box(root, "FloodBar", new Vector3(galX, galY, 0f),
                new Vector3(0.006f, 0.008f, halfPZ * 1.7f), CStruct);
            for (int s = -1; s <= 1; s += 2)
                Strut(root, "FloodBar_Brace",
                    new Vector3(apex.x, platY + 0.004f, s * halfPZ * 0.7f),
                    new Vector3(galX, galY, s * halfPZ * 0.7f), 0.004f, CStruct);
            for (int i = -2; i <= 2; i++)
            {
                float fz = i * halfPZ * 0.4f;
                Box(root, "Floodlight_Housing", new Vector3(galX + 0.006f, galY, fz),
                    new Vector3(0.012f, 0.014f, 0.014f), CDark);
                Ball(root, "Apex_Floodlight", new Vector3(galX + 0.013f, galY - 0.006f, fz),
                    new Vector3(0.011f, 0.009f, 0.011f), CLight);
            }

            // 정상 접근 사다리 숨김(사용자 요청) — 복구하려면 주석 해제
            // Vector3 aTop = Vector3.Lerp(new Vector3(LandLegX, RailH, halfZ), apex, 0.9f);
            // Vector3 aBot = new Vector3(LandLegX, RailH, halfZ);
            // BuildInclinedLadder(root, aTop, aBot, 0.028f, new Vector3(0f, 0f, 0.022f));

            // 스테이 케이블 — 부채꼴 + 앵커 플레이트·턴버클. 측면 시브(z=±GirderGapZ)에서 같은 쪽 거더로 내림(측면별 수직면).
            float boomTopY = RailH + 0.07f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 sheave = new Vector3(apex.x, apex.y - 0.01f, s * GirderGapZ);
                foreach (float f in new[] { 0.35f, 0.55f, 0.78f, 1.0f })   // 바다쪽 4줄
                    BuildStay(root, sheave,
                        new Vector3(Mathf.Lerp(WaterLegX, BoomTipX, f), boomTopY, s * GirderGapZ), "Forestay");
            }
            // 정상 후방 연장(아웃리거) 보류(사용자 요청) — A-프레임을 높여(ApexH↑) 정상 시브 하우스에서 바로 내려도 기계실 위를 넘으므로 불필요. 복구하려면 블록주석 제거
            /*
            float backMastX = apex.x - 0.22f;
            float bmY = apex.y - 0.005f;
            Box(root, "Apex_BackBeam", new Vector3(backMastX, bmY, 0f),
                new Vector3(0.016f, 0.016f, 2f * GirderGapZ), CStruct);
            for (int s = -1; s <= 1; s += 2)
            {
                float armZ = s * GirderGapZ;
                Strut(root, "Apex_BackArm", new Vector3(apex.x, apex.y, armZ),
                    new Vector3(backMastX, bmY, armZ), 0.009f, CStruct);
                Strut(root, "Apex_BackBrace", new Vector3(backMastX, bmY, armZ),
                    new Vector3(apex.x, apex.y - 0.09f, armZ), 0.006f, CStruct);
                Rod(root, "Apex_BackSheave",
                    new Vector3(backMastX, bmY, armZ - 0.014f),
                    new Vector3(backMastX, bmY, armZ + 0.014f), 0.013f, CDark);
            }
            */

            // 백스테이 — 정상 시브 하우스(Apex_SheaveHouse, z=±GirderGapZ)에서 앞(기계실 앞쪽) + 뒤(거더 맨뒤) 2점으로.
            //   A-프레임을 높여 케이블 각도를 세워 기계실 지붕 위를 넘김.
            float bsFrontX = MachineryHouseX + MachineryHouseHX + 0.03f;   // 기계실 앞쪽(바다쪽) 한 줄
            float bsBackX  = BoomBackX + 0.02f;                            // 거더 맨뒤(이퀄라이저 빔) 한 줄
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 shTop = new Vector3(apex.x, apex.y - 0.01f, s * GirderGapZ);   // 시브 하우스 시브 위치
                foreach (float bx in new[] { bsFrontX, bsBackX })
                    BuildStay(root, shTop,
                        new Vector3(bx, boomTopY, s * GirderGapZ), "Backstay");
            }
        }

        // ───────────────────────── 가동부 시각화 ─────────────────────────

        static void BuildTrolleyVisual(Transform trolley)
        {
            // 트롤리는 붐 레일(붐 로컬 y=0)에 위치. 본체는 그 아래로. 트윈 거더 두 레일에 걸침.
            float tZ = 2f * GirderGapZ + 0.05f;   // 두 레일에 걸치는 폭(≈0.19)
            PbBox(trolley, "Trolley_Body", new Vector3(0f, -0.025f, 0f),
                new Vector3(0.11f, 0.05f, tZ), CTrolley, bevel: 0.12f);
            BuildTrolleyBodyFrame(trolley);   // 1) 본체 프레임화 — 코너 포스트/둘레 종재/리브/이음매
            PbBox(trolley, "Trolley_Head", new Vector3(0f, -0.06f, 0f),
                new Vector3(0.07f, 0.025f, tZ * 0.85f), CDark, bevel: 0.2f);
            // 두 레일 위 주행 대차(보기) + 바퀴 — 각 거더 레일(z=±GirderGapZ)에 올라탐
            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * GirderGapZ;
                PbBox(trolley, "Trolley_Bogie", new Vector3(0f, -0.006f, gz),
                    new Vector3(0.13f, 0.016f, 0.03f), CDark, bevel: 0.25f);
                foreach (float wx in new[] { -0.05f, -0.018f, 0.018f, 0.05f })   // 보기당 4륜(디테일)
                    Rod(trolley, "Trolley_Wheel",
                        new Vector3(wx, 0.001f, gz - 0.013f),
                        new Vector3(wx, 0.001f, gz + 0.013f), 0.011f, CRail);
            }
            // 호이스트 시브(도르래) — 로프가 도는 큰 휠(축 Z, 로프 z±0.05 reeve). 더 크게(실제감) + 강철색.
            for (int w = -1; w <= 1; w += 2)
                Rod(trolley, "Trolley_Sheave",
                    new Vector3(w * 0.03f, -0.005f, -0.056f),
                    new Vector3(w * 0.03f, -0.005f,  0.056f), 0.016f, CRail);
            // ── 시브 블록 치크/핀 + 로프 데드엔드 소켓 (와이어 연결부) ──
            for (int w = -1; w <= 1; w += 2)
                SheaveNest(trolley, new Vector3(w * 0.03f, -0.005f, 0f), 0.013f, 0.05f, CStruct);
            // 데드엔드 소켓 — 육지쪽(x=-0.03) 로프 falls 2개를 트롤리에 정착(스펠터 소켓 몸체 + 클레비스 핀 + 바스켓)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 sk = new Vector3(-0.03f, -0.052f, sz * 0.05f);
                Box(trolley, "Rope_Socket_Body", sk, new Vector3(0.012f, 0.02f, 0.012f), CStruct);
                Rod(trolley, "Rope_Socket_Pin",
                    sk + new Vector3(-0.008f, 0.011f, 0f), sk + new Vector3(0.008f, 0.011f, 0f), 0.0025f, CDark);
                Cone(trolley, "Rope_Socket_Basket",
                    sk + new Vector3(0f, -0.016f, 0f), sk + new Vector3(0f, -0.004f, 0f), 0.003f, 0.007f, CDark);
            }
            // 운전실(운전석) — 재설계. 스프레더 뒤(-X)에 매달려 전면(+X) 경사창으로 화물을 내려다본다.
            BuildOperatorCab(trolley);

            // (내가 추가했던 Trolley_Gearbox·Trolley_Cabinet·Trolley_DriveShaft·모터 부속(핀/팬커버/플랜지/감속기) 전부 제거
            //  — 사용자가 요청 안 한 부품 추가였음. 기존 부품만 남긴다.)
            // 트롤리 주행 모터 — 숨김(사용자 요청). 복구하려면 주석 해제.
            // for (int s = -1; s <= 1; s += 2)
            //     Rod(trolley, "Trolley_Motor", new Vector3(-0.085f, -0.062f, s * 0.075f),
            //         new Vector3(-0.055f, -0.062f, s * 0.075f), 0.013f, CMachine);
            PbBox(trolley, "Trolley_FestoonBox", new Vector3(-0.06f, -0.06f, 0f),
                new Vector3(0.018f, 0.025f, 0.03f), CDark);
            // 트롤리 양끝 완충 버퍼(적색)
            for (int sx = -1; sx <= 1; sx += 2)
                PbBox(trolley, "Trolley_Bumper", new Vector3(sx * 0.058f, -0.01f, 0f),
                    new Vector3(0.008f, 0.014f, 0.04f), CWarn, bevel: 0.3f);
        }

        // 트롤리 본체 프레임화 (Trolley 디테일 1) — 민짜 박스를 용접 프레임으로 분절.
        //   코너 포스트 4 + 상·하 둘레 종재 + 측면(±X) 수직 리브 + 단부(±Z) 리브 + 패널 이음매.
        //   본체 박스(0.11×0.05×0.37) 좌표 불변, 표면에 proud 부재만 추가(폴리: 가는 박스 ~36개).
        static void BuildTrolleyBodyFrame(Transform trolley)
        {
            float hx = 0.055f, hz = (2f * GirderGapZ + 0.05f) * 0.5f;   // 본체 반치수(X,Z)
            float yTop = 0f, yBot = -0.05f, yMid = -0.025f, h = 0.05f;

            // 코너 포스트 4 — 본체 모서리 수직 트림
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Box(trolley, "TB_Corner", new Vector3(sx * hx, yMid, sz * hz),
                    new Vector3(0.009f, h + 0.006f, 0.009f), CStruct);

            // 상·하 둘레 종재(긴 변 Z + 짧은 변 X)
            foreach (float y in new[] { yTop, yBot })
            {
                for (int sx = -1; sx <= 1; sx += 2)
                    Box(trolley, "TB_Chord", new Vector3(sx * hx, y, 0f),
                        new Vector3(0.008f, 0.007f, 2f * hz), CStruct);
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(trolley, "TB_Chord", new Vector3(0f, y, sz * hz),
                        new Vector3(2f * hx, 0.007f, 0.008f), CStruct);
            }

            // 측면(±X) 수직 리브 — Z 따라 다단(평탄한 긴 면 분절)
            int ribs = 6;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int i = 1; i < ribs; i++)
            {
                float z = Mathf.Lerp(-hz, hz, i / (float)ribs);
                Box(trolley, "TB_Rib", new Vector3(sx * (hx + 0.001f), yMid, z),
                    new Vector3(0.005f, h, 0.006f), CTrolley);
            }

            // 단부(±Z) 수직 리브 3 — 끝면 분절
            for (int sz = -1; sz <= 1; sz += 2)
            for (int i = -1; i <= 1; i++)
                Box(trolley, "TB_EndRib", new Vector3(i * hx * 0.55f, yMid, sz * (hz + 0.001f)),
                    new Vector3(0.006f, h, 0.005f), CTrolley);

            // 패널 이음매(가로) — 큰 ±X 면 평탄함 제거
            for (int sx = -1; sx <= 1; sx += 2)
                Box(trolley, "TB_Seam", new Vector3(sx * (hx + 0.0005f), yMid, 0f),
                    new Vector3(0.003f, 0.003f, 2f * hz), CDark);
        }

        // 운전실(운전석) 재설계 — 트롤리 하부, 스프레더 뒤(-X)에 매달려 전면(+X)으로 화물을 내려다본다.
        //   실제 STS 운전실: 전면 하부 '경사창'(내려다보기) + 좌석/콘솔 + 측·후면 도어 + 지붕 + 하부 작업등 + 후면 접근 플랫폼.
        //   좌표는 트롤리 로컬. 본체(y≥-0.05)·헤드(x∈±0.035)·로프소켓(x≈-0.03)과 안 겹치게: 운전실은 x≤-0.042, y≤-0.052.
        static void BuildOperatorCab(Transform trolley)
        {
            // ProBuilder 편집형(PbBox). 전면 글라스 노즈로 화물 내려다보기.
            //   ★ 막힘 해결(계산): 운전실 지붕을 Trolley_Head 하단(-0.0725)보다 아래(-0.074)로 내려 헤드와 y겹침 0 →
            //     전면창이 헤드에 안 막힘(이전 지붕 -0.052는 헤드와 같은 높이라 정면으로 막혔음).
            //     헤드 아래라 x 위치 무관하게 안 겹쳐, 중심을 스프레더(x=0) 근처로 둬 바로 아래 화물을 내려다봄.
            float cx = -0.075f, hx = 0.030f, hz = 0.036f;   // 중심 — 뒤로 더 뺌(전면 x=-0.045, 헤드 -0.035 밖·본체 아래라 막힘·겹침 없음)
            float roofY = -0.074f, floorY = -0.134f;        // 지붕 < 헤드 하단(-0.0725) → 막힘 제거, 높이 0.06
            float h = roofY - floorY, midY = (floorY + roofY) * 0.5f;
            float frontX = cx + hx, backX = cx - hx;

            // 바닥·지붕(오버행) — 베벨로 모서리 깎아 큐브 티 제거
            PbBox(trolley, "Cab_Floor", new Vector3(cx, floorY, 0f), new Vector3(2f * hx, 0.006f, 2f * hz), CMachine, bevel: 0.2f);
            PbBox(trolley, "Cab_Roof",  new Vector3(cx - 0.002f, roofY, 0f), new Vector3(2f * hx + 0.01f, 0.006f, 2f * hz + 0.01f), CTrolley, bevel: 0.3f);

            // 현수 — 운전실(본체 뒤·아래)을 본체 후면 하단 모서리에 매단다. 헤드(x±0.035) 밖이라 안 겹침.
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Strut(trolley, "Cab_Hanger",                       // 전면-상단 → 본체 하단(수직 기둥)
                    new Vector3(frontX, roofY, sz * hz * 0.7f),
                    new Vector3(frontX, -0.05f, sz * hz * 0.7f), 0.004f, CStruct);
                Strut(trolley, "Cab_HangerBrace",                 // 후면-상단 → 본체 하단(대각 버팀)
                    new Vector3(backX, roofY, sz * hz * 0.7f),
                    new Vector3(frontX, -0.05f, sz * hz * 0.7f), 0.004f, CStruct);
            }

            // 코너 포스트 4
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(trolley, "Cab_Post", new Vector3(cx + sx * hx, midY, sz * hz), new Vector3(0.005f, h, 0.005f), CDark);

            // 후면 벽 + 도어 + 도어창 + 손잡이
            PbBox(trolley, "Cab_BackWall", new Vector3(backX, midY, 0f), new Vector3(0.004f, h, 2f * hz), CTrolley);
            // Cab_Door 일체(문짝+도어창+손잡이) 숨김(사용자 요청). 복구하려면 주석 해제.
            // PbBox(trolley, "Cab_Door", new Vector3(backX - 0.002f, floorY + h * 0.42f, hz * 0.45f), new Vector3(0.003f, h * 0.78f, hz * 0.8f), CDark);
            // PbBox(trolley, "Cab_DoorWindow", new Vector3(backX - 0.003f, floorY + h * 0.62f, hz * 0.45f), new Vector3(0.002f, h * 0.26f, hz * 0.6f), CGlass);
            // PbBox(trolley, "Cab_DoorHandle", new Vector3(backX - 0.005f, floorY + h * 0.42f, hz * 0.08f), new Vector3(0.003f, 0.008f, 0.003f), CStruct);

            // 측면(±Z): 하부 패널 + 큰 측창 + 멀리언
            for (int sz = -1; sz <= 1; sz += 2)
            {
                PbBox(trolley, "Cab_SidePanel", new Vector3(cx, floorY + h * 0.22f, sz * hz), new Vector3(2f * hx, h * 0.44f, 0.003f), CTrolley);
                PbBox(trolley, "Cab_SideGlass", new Vector3(cx + 0.004f, floorY + h * 0.72f, sz * (hz - 0.001f)), new Vector3(2f * hx * 0.82f, h * 0.5f, 0.002f), CGlass);
            }

            // 전면 글라스 노즈(핵심): 상부 수직창 + 하부 경사창(바닥이 +X로 돌출 → 내려다보기) + 가로 멀리언 + 와이퍼
            PbBox(trolley, "Cab_FrontGlassUpper", new Vector3(frontX, floorY + h * 0.74f, 0f), new Vector3(0.002f, h * 0.46f, 2f * hz * 0.92f), CGlass);
            PbBox(trolley, "Cab_FrontGlassSlope", new Vector3(frontX + 0.014f, floorY + h * 0.26f, 0f), new Vector3(0.002f, h * 0.54f, 2f * hz * 0.92f), CGlass,
                  new Vector3(0f, 0f, 34f));   // 하단 +X 돌출 경사창
            PbBox(trolley, "Cab_FrontMullionH", new Vector3(frontX + 0.003f, floorY + h * 0.5f, 0f), new Vector3(0.005f, 0.003f, 2f * hz * 0.92f), CDark);
            Rod(trolley, "Cab_Wiper", new Vector3(frontX + 0.012f, floorY + h * 0.34f, -0.012f), new Vector3(frontX + 0.004f, floorY + h * 0.52f, 0.012f), 0.0012f, CDark);

            // 내부: 좌석 + 등받이 + 헤드레스트 + 좌우 콘솔/조이스틱
            float seatX = cx + 0.004f, seatY = floorY + 0.012f;
            PbBox(trolley, "Cab_Seat", new Vector3(seatX, seatY, 0f), new Vector3(0.016f, 0.006f, 0.018f), CDark, bevel: 0.3f);
            PbBox(trolley, "Cab_SeatBack", new Vector3(seatX - 0.01f, seatY + 0.016f, 0f), new Vector3(0.005f, 0.026f, 0.018f), CDark, bevel: 0.3f);
            PbBox(trolley, "Cab_HeadRest", new Vector3(seatX - 0.009f, seatY + 0.032f, 0f), new Vector3(0.005f, 0.008f, 0.012f), CDark, bevel: 0.35f);
            for (int sz = -1; sz <= 1; sz += 2)
            {
                PbBox(trolley, "Cab_Console", new Vector3(seatX + 0.008f, seatY + 0.006f, sz * 0.015f), new Vector3(0.016f, 0.006f, 0.007f), CMachine, bevel: 0.3f);
                Rod(trolley, "Cab_Joystick", new Vector3(seatX + 0.01f, seatY + 0.009f, sz * 0.015f), new Vector3(seatX + 0.012f, seatY + 0.02f, sz * 0.015f), 0.0015f, CDark);
            }

            // 외부 장비 + 작업등 (후면 접근 플랫폼/난간/AC는 숨김 — 사용자 요청)
            // PbBox(trolley, "Cab_AC", new Vector3(backX - 0.008f, midY + 0.01f, 0f), new Vector3(0.012f, 0.018f, 0.028f), CMachine);
            // 통신 안테나 — 운전실 후면 모서리(트롤리 본체 뒤 = 윗공간 열림)에. 베이스 마운트 + 2단 테이퍼 휩 + 끝 캡.
            //   (이전엔 얇은 막대가 본체에 박혀 허접했음 → 위치·형태 수정)
            float antX = backX, antZ = 0.018f, antY = roofY;
            PbBox(trolley, "Cab_AntennaBase", new Vector3(antX, antY + 0.003f, antZ), new Vector3(0.008f, 0.005f, 0.008f), CDark);     // 베이스 마운트
            Rod(trolley, "Cab_Antenna", new Vector3(antX, antY + 0.005f, antZ), new Vector3(antX, antY + 0.024f, antZ), 0.0024f, CStruct);  // 하부 휩(굵게)
            Rod(trolley, "Cab_Antenna", new Vector3(antX, antY + 0.024f, antZ), new Vector3(antX, antY + 0.046f, antZ), 0.0011f, CStruct);  // 상부 휩(얇게·테이퍼)
            Ball(trolley, "Cab_AntennaTip", new Vector3(antX, antY + 0.047f, antZ), new Vector3(0.0035f, 0.0035f, 0.0035f), CWarn);   // 끝 캡(주황)
            for (int i = -1; i <= 1; i++)
                Ball(trolley, "Cab_Floodlight", new Vector3(frontX - 0.004f, floorY - 0.004f, i * 0.02f), new Vector3(0.01f, 0.006f, 0.01f), CLight);
            // PbBox(trolley, "Cab_Platform", new Vector3(backX - 0.018f, floorY + 0.002f, 0f), new Vector3(0.03f, 0.004f, 2f * hz + 0.014f), CMachine);
            // for (int sz = -1; sz <= 1; sz += 2)
            //     PbBox(trolley, "Cab_PlatRail", new Vector3(backX - 0.018f, floorY + 0.022f, sz * (hz + 0.006f)), new Vector3(0.03f, 0.003f, 0.003f), CStruct);
        }

        // 스프레더 — 중앙 고정부(항상 20ft) + 좌/우 텔레스코픽 암(끝빔 + 트위스트락).
        // spreaderHalf(반길이)로 암 위치를 정함: 20ft=중앙 끝에 밀착, 40ft=바깥으로 신장(텔레스코핑 빔이 연결).
        static void BuildSpreaderVisual(Transform spreader, float spreaderHalf)
        {
            // 컨테이너 긴 축이 안벽/주행 방향(Z)을 향하도록 스프레더 전체를 90° 회전
            // (부속은 긴 축=로컬 X로 배치 → Y축 90° 회전으로 월드 Z가 긴 축이 됨)
            spreader.localRotation = Quaternion.Euler(0f, 90f, 0f);

            float hw = 0.05f;                  // 반폭(로컬 Z) — 컨테이너 폭 비례(20·40ft 동일)
            const float hl0 = SpreaderHalf20;  // 중앙 고정부 반길이(항상 20ft)
            Color CMetal = CRail;              // 트위스트락 회색

            // ── 중앙 고정부(항상 20ft) — 메인 빔 + 상·하 플랜지 ──
            Box(spreader, "Spreader_Bar", Vector3.zero,
                new Vector3(hl0 * 2f, 0.028f, hw * 2f), CSpread);
            for (int sy = -1; sy <= 1; sy += 2)
                Box(spreader, "Beam_Flange", new Vector3(0f, sy * 0.016f, 0f),
                    new Vector3(hl0 * 2f, 0.006f, hw * 2f + 0.008f), CSpread);

            // ── 헤드블록(중앙) — 크로스 프레임 + 시브 4개(2쌍) + 치크 플레이트 ──
            float hbY = 0.058f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Strut(spreader, "Head_Frame",
                    new Vector3(sx * 0.055f, 0.016f, sz * 0.028f),
                    new Vector3(sx * 0.035f, hbY - 0.012f, sz * 0.02f), 0.006f, CSpread);
            Box(spreader, "Spreader_Head", new Vector3(0f, hbY, 0f),
                new Vector3(0.11f, 0.026f, hw * 1.5f), CSpread);
            foreach (float hx in new[] { -0.042f, -0.018f, 0.018f, 0.042f })
                Rod(spreader, "Head_Sheave",
                    new Vector3(hx, hbY + 0.013f, -0.026f),
                    new Vector3(hx, hbY + 0.013f,  0.026f), 0.013f, CDark);
            for (int sz = -1; sz <= 1; sz += 2)
                Box(spreader, "Head_Cheek", new Vector3(0f, hbY + 0.013f, sz * 0.03f),
                    new Vector3(0.1f, 0.028f, 0.004f), CSpread);
            // 헤드 시브 관통 핀 보스(축단) — 치크 밖으로 살짝 돌출
            foreach (float hx in new[] { -0.042f, -0.018f, 0.018f, 0.042f })
                Rod(spreader, "Head_Sheave_Pin",
                    new Vector3(hx, hbY + 0.013f, -0.034f),
                    new Vector3(hx, hbY + 0.013f,  0.034f), 0.004f, CDark);

            // ── 부속(중앙) — 파워팩/정션박스/작업등 ──
            Vector3[] ppTips = PowerPack(spreader, "Spreader_PowerPack", new Vector3(0.07f, 0.022f, 0f),
                new Vector3(0.04f, 0.022f, hw * 1.2f), CMachine);
            JunctionBox(spreader, "Spreader_JBox", new Vector3(-0.07f, 0.02f, 0f),
                new Vector3(0.03f, 0.018f, 0.03f), Vector3.up, Vector3.down, CDark);
            for (int sx = -1; sx <= 1; sx += 2)
                Ball(spreader, "Spreader_Floodlight", new Vector3(sx * 0.085f, 0.005f, 0f),
                    new Vector3(0.013f, 0.008f, 0.013f), CLight);

            // ── 유압/제어 호스 — 파워팩 포트 → J박스 → 헤드블록. 끝점을 부품 '안으로' 묻고 접합부마다 클램프로 봉합(틈 제거) ──
            //   J박스 본체: 중심(-0.07,0.02,0), 치수(0.03,0.018,0.03) → x:-0.085~-0.055, y:0.011~0.029, z:±0.015
            //   헤드블록 본체: 중심(0,hbY=0.058,0), 치수(0.11,0.026,..) → 밑면 y=0.045
            for (int i = 0; i < 2; i++)
            {
                int sz = i == 0 ? -1 : 1;
                float cz = sz * 0.008f;
                Vector3 jbIn = new Vector3(-0.062f, 0.022f, cz);          // J박스 내부로 묻은 끝점
                Vector3 hbIn = new Vector3(-0.038f, 0.05f,  sz * 0.012f); // 헤드블록 내부로 묻은 끝점

                // 파워팩 포트(ppTips) → J박스 (포트 끝에서 시작, J박스 안으로 종단)
                CableCatenary(spreader, "Spreader_Hose", ppTips[i], jbIn, 0.005f, 0.0014f, CCable);
                Ball(spreader, "Hose_Clamp", new Vector3(-0.056f, 0.024f, cz), Vector3.one * 0.005f, CStruct);

                // J박스(내부) → 헤드블록 밑면(안으로 묻음) : 제어 커넥터
                CableCatenary(spreader, "Spreader_Hose",
                    new Vector3(-0.063f, 0.026f, cz), hbIn, 0.004f, 0.0013f, CCable);
                Ball(spreader, "Hose_Clamp", new Vector3(-0.04f, 0.046f, sz * 0.012f), Vector3.one * 0.005f, CStruct);
            }

            // ── 좌/우 텔레스코픽 암(끝빔 + 트위스트락) — 런타임에 20↔40ft 슬라이드 ──
            // 각 암을 Transform으로 묶고 자식은 '암 로컬' 좌표(암 원점 = 끝빔 위치)로 배치 →
            // SpreaderTelescope가 암의 로컬 X만 옮기면 끝빔·트위스트락이 통째로 슬라이드한다.
            Transform armL = null, armR = null;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var arm = new GameObject(sx < 0 ? "TeleArm_L" : "TeleArm_R");
                arm.transform.SetParent(spreader, worldPositionStays: false);
                arm.transform.localPosition = new Vector3(sx * spreaderHalf, 0f, 0f);
                Transform a = arm.transform;
                if (sx < 0) armL = a; else armR = a;

                // 끝단 크로스 빔(컨테이너 단부) — 암 원점
                Box(a, "End_Beam", new Vector3(-sx * 0.008f, 0f, 0f),
                    new Vector3(0.016f, 0.03f, hw * 2f + 0.012f), CSpread);
                // 트위스트락 2(앞/뒤 코너) — 회색 락헤드 + 락 콘(아래로 뾰족)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 c = new Vector3(-sx * 0.006f, 0f, sz * (hw - 0.006f));
                    Box(a, "Twistlock_Head", c + new Vector3(0f, -0.014f, 0f),
                        new Vector3(0.02f, 0.024f, 0.02f), CMetal);
                    Cone(a, "Twistlock_Cone",
                        c + new Vector3(0f, -0.036f, 0f), c + new Vector3(0f, -0.024f, 0f),
                        0.004f, 0.009f, CMetal, 16);
                }
            }

            // 텔레스코핑 빔(스프레더 직속) — 중앙 끝(hl0)↔암 사이를 SpreaderTelescope가 X 스케일로 신축.
            // 20ft면 수축(거의 사라짐), 40ft면 신장. 초기 크기는 생성 사이즈 기준(에디터 표시용).
            float initLen = Mathf.Max(0.0001f, (spreaderHalf - hl0) + 0.02f);
            float initMid = (hl0 + spreaderHalf) * 0.5f;
            Transform teleL = Box(spreader, "Tele_Beam", new Vector3(-initMid, 0f, 0f),
                new Vector3(initLen, 0.018f, hw * 1.2f), CDark).transform;
            Transform teleR = Box(spreader, "Tele_Beam", new Vector3(initMid, 0f, 0f),
                new Vector3(initLen, 0.018f, hw * 1.2f), CDark).transform;

            // 런타임 텔레스코픽 전환(시작 사이즈 = 생성 시 spreaderHalf)
            var tele = spreader.gameObject.AddComponent<SpreaderTelescope>();
            tele.Configure(armL, armR, teleL, teleR, hl0, SpreaderHalf20, SpreaderHalf40,
                           spreaderHalf > hl0 + 1e-4f);
        }

        // 호이스트 로프 4줄 — spreaderRoot(붐 레벨 y=0) → 스프레더 헤드.
        // 정적 생성 후 HoistRopeRig가 매 프레임 스프레더 Y에 맞춰 신축(게임 런타임).
        static void BuildHoistRopes(Transform spreaderRoot, Transform spreader)
        {
            float topY = -0.02f;               // 로프 상단(트롤리 헤드 아래)
            float attachOffsetY = 0.05f;       // 스프레더 원점 → 헤드블록 상단(로프 하단)
            float radius = 0.0035f;
            float restBotY = SpreaderRestY + attachOffsetY;

            var ropes = new List<Transform>();
            var topAnchors = new List<Vector2>();
            var botAnchors = new List<Vector2>();
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float x = sx * 0.03f;
                float z = sz * 0.05f;   // 스프레더 코너에 맞춘 수직 로프(부채꼴 취소 — 사용자 요청, 예전 방식)
                var rope = Rod(spreaderRoot, "Hoist_Rope",
                    new Vector3(x, topY, z), new Vector3(x, restBotY, z), radius, CCable);
                ropes.Add(rope.transform);
                topAnchors.Add(new Vector2(x, z));
                botAnchors.Add(new Vector2(x, z));
            }
            // 매 프레임 트롤리↔스프레더 사이로 로프 신축(상≠하 각진 리빙)
            var rig = spreaderRoot.gameObject.AddComponent<HoistRopeRig>();
            rig.Configure(spreader, topY, attachOffsetY, ropes.ToArray(),
                          topAnchors.ToArray(), botAnchors.ToArray(), radius);
        }

        // ───────────────────────── 디테일 지오메트리 (D) ─────────────────────────

        // 격자 트러스 다리 — 4 코너 포스트 + 가로 rung + 4면 대각 lacing
        static void BuildLatticeLeg(Transform root, float cx, float cz, float y0, float y1, float foot)
        {
            float h = y1 - y0;
            float half = foot * 0.5f;
            float postT = foot * 0.28f;

            // 4 코너 포스트
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Box(root, "Leg_Post", new Vector3(cx + sx * half, (y0 + y1) * 0.5f, cz + sz * half),
                    new Vector3(postT, h, postT), CStruct);
            }

            int segs = Mathf.Max(4, Mathf.RoundToInt(h / 0.10f));
            float step = h / segs;

            // 가로 rung — 매 노드, 4면
            for (int i = 0; i <= segs; i++)
            {
                float y = y0 + step * i;
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(root, "Leg_Rung", new Vector3(cx, y, cz + sz * half),
                        new Vector3(foot, postT * 0.7f, postT * 0.7f), CStruct);
                for (int sx = -1; sx <= 1; sx += 2)
                    Box(root, "Leg_Rung", new Vector3(cx + sx * half, y, cz),
                        new Vector3(postT * 0.7f, postT * 0.7f, foot), CStruct);
            }

            // 대각 lacing — 4면 지그재그
            for (int i = 0; i < segs; i++)
            {
                float ya = y0 + step * i;
                float yb = y0 + step * (i + 1);
                bool up = (i % 2) == 0;
                for (int sz = -1; sz <= 1; sz += 2)
                    Strut(root, "Leg_Lace",
                        new Vector3(cx - half, up ? ya : yb, cz + sz * half),
                        new Vector3(cx + half, up ? yb : ya, cz + sz * half), postT * 0.55f, CStruct);
                for (int sx = -1; sx <= 1; sx += 2)
                    Strut(root, "Leg_Lace",
                        new Vector3(cx + sx * half, up ? ya : yb, cz - half),
                        new Vector3(cx + sx * half, up ? yb : ya, cz + half), postT * 0.55f, CStruct);
            }

            // 절점 연결판(거싯) — 라싱/포스트 교차 노드에 면마다 챔퍼 8각 판(용접 연결 표현).
            //   순수 추가(기존 포스트/라싱 좌표는 그대로) — 4 코너 포스트가 만나는 ±X·±Z 면에 부착.
            float gSize = postT * 1.15f;
            float gThick = postT * 0.22f;
            float gOff = gThick * 0.55f;          // 면에서 살짝 띄워 z-fighting 방지
            for (int i = 2; i < segs; i += 3)     // 내부 노드 일부(과밀 방지)
            {
                float y = y0 + step * i;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    // ±Z 면(코너 x=cx±half)
                    Gusset(root, new Vector3(cx + sx * half, y, cz + half + gOff),
                        Quaternion.identity,        gSize, gThick, CStruct);
                    Gusset(root, new Vector3(cx + sx * half, y, cz - half - gOff),
                        Quaternion.Euler(0, 180, 0), gSize, gThick, CStruct);
                    // ±X 면(코너 z=cz±half)
                    Gusset(root, new Vector3(cx + half + gOff, y, cz + sx * half),
                        Quaternion.Euler(0, 90, 0),  gSize, gThick, CStruct);
                    Gusset(root, new Vector3(cx - half - gOff, y, cz + sx * half),
                        Quaternion.Euler(0, -90, 0), gSize, gThick, CStruct);
                }
            }

            // 갠트리 주행 시 레일 위 장애물(컨테이너 등)과 충돌하도록 다리 하나를 감싸는 BoxCollider 1개.
            //   격자 부재(Leg_Post/Rung/Lace)는 전부 시각용이라 콜라이더가 없다 → 다리를 통째로 물리화.
            //   (크레인 루트의 kinematic Rigidbody가 이 콜라이더로 dynamic 컨테이너를 밀어낸다.)
            var legCol = new GameObject("Leg_Collider");
            legCol.transform.SetParent(root, worldPositionStays: false);
            legCol.transform.localPosition = new Vector3(cx, (y0 + y1) * 0.5f, cz);
            legCol.AddComponent<BoxCollider>().size = new Vector3(foot, h, foot);
        }

        // 붐 디테일: 보도 그레이팅 + 끝단 시브(도르래) + 작업등
        static void BuildBoomDetails(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            float len = x1 - x0, mid = (x0 + x1) * 0.5f;

            // 보도 그레이팅 — 거더 위 2장 + 가운데(Boom_Vertical/브레이스 위) 1장으로 상단 전체를 막음(연속 보도, 패널 이음새 유지)
            for (int s = -1; s <= 1; s += 2)
                BoxGappedX(boom, "Walkway_Deck", x0, x1, 0.067f, s * GirderGapZ,
                    0.004f, 0.05f, CMachine, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
            // 중앙 보도 숨김(사용자 요청) — 복구하려면 주석 해제
            // Box(boom, "Walkway_Deck_Mid", new Vector3(mid, 0.067f, 0f),
            //     new Vector3(len, 0.004f, 2f * (GirderGapZ - 0.025f)), CMachine);

            // 끝단 시브(도르래) — 로프가 도는 휠, 축은 Z
            Rod(boom, "Sheave_Tip",
                new Vector3(x1 - 0.02f, 0.03f, -0.04f),
                new Vector3(x1 - 0.02f, 0.03f,  0.04f), 0.02f, CDark);

            // 작업등(floodlight) — 붐 하부, 아래를 비춤
            foreach (float lx in new[] { mid + 0.10f, x1 - 0.12f, x1 - 0.30f })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * GirderGapZ;
                    Box(boom, "Floodlight_Housing", new Vector3(lx, -0.008f, z),
                        new Vector3(0.018f, 0.014f, 0.018f), CDark);
                    Ball(boom, "Floodlight_Lens", new Vector3(lx, -0.018f, z),
                        new Vector3(0.015f, 0.007f, 0.015f), CLight);
                }
            }

            // 끝단 플랫폼 + 추가 시브 + 항해등
            // 끝단 플랫폼 — 트윈 거더 전폭으로(좁던 것 수정) + 둘레 안전 난간
            Box(boom, "Tip_Platform", new Vector3(x1 - 0.04f, 0.066f, 0f),
                new Vector3(0.08f, 0.004f, 2f * GirderOuterZ), CMachine);
            {
                float tpY0 = 0.068f, tpRailY = 0.102f;
                float tpX0 = x1 - 0.08f, tpX1 = x1, tpHZ = GirderOuterZ;
                for (int s = -1; s <= 1; s += 2)   // ±Z 옆 난간
                    Box(boom, "Tip_Rail", new Vector3((tpX0 + tpX1) * 0.5f, tpRailY, s * tpHZ),
                        new Vector3(tpX1 - tpX0, 0.005f, 0.005f), CStruct);
                Box(boom, "Tip_Rail", new Vector3(tpX1, tpRailY, 0f),   // 바다쪽 끝 난간
                    new Vector3(0.005f, 0.005f, tpHZ * 2f), CStruct);
                foreach (float pz in new[] { -tpHZ, 0f, tpHZ })        // 기둥(끝 코너+중앙)
                    Box(boom, "Tip_Rail_Post", new Vector3(tpX1, (tpY0 + tpRailY) * 0.5f, pz),
                        new Vector3(0.005f, tpRailY - tpY0, 0.005f), CStruct);
            }
            Rod(boom, "Sheave_Tip2",
                new Vector3(x1 - 0.05f, 0.03f, -0.04f),
                new Vector3(x1 - 0.05f, 0.03f,  0.04f), 0.018f, CDark);
            // 끝단 시브 네스트(치크+핀 보스) — 시브 블록 표현
            SheaveNest(boom, new Vector3(x1 - 0.02f, 0.03f, 0f), 0.02f,  0.04f, CStruct);
            SheaveNest(boom, new Vector3(x1 - 0.05f, 0.03f, 0f), 0.018f, 0.04f, CStruct);
            // 항해등 숨김(사용자 요청) — 복구하려면 주석 해제
            // Ball(boom, "Nav_Light", new Vector3(x1 - 0.003f, 0.05f, 0f),
            //     new Vector3(0.012f, 0.016f, 0.012f), CWarn);

            // 하부 점검 캣워크(양옆) + 난간(상단·중간대 + 기둥)
            for (int s = -1; s <= 1; s += 2)
            {
                float cz = s * (GirderOuterZ + 0.012f);
                float railZ = cz + s * 0.009f;
                Box(boom, "Catwalk", new Vector3(mid, 0f, cz),
                    new Vector3(len, 0.004f, 0.022f), CMachine);
                Box(boom, "Catwalk_Rail", new Vector3(mid, 0.026f, railZ),
                    new Vector3(len, 0.004f, 0.004f), CStruct);
                Box(boom, "Catwalk_RailMid", new Vector3(mid, 0.013f, railZ),
                    new Vector3(len, 0.003f, 0.003f), CStruct);
                int cposts = 14;
                for (int i = 0; i <= cposts; i++)
                {
                    float px = Mathf.Lerp(x0, x1, i / (float)cposts);
                    Box(boom, "Catwalk_Post", new Vector3(px, 0.013f, railZ),
                        new Vector3(0.003f, 0.026f, 0.003f), CStruct);
                }
            }

            // 페스툰(전력·제어 케이블) — 붐 하부 트랙 + 늘어진 케이블 다발
            float festZ = GirderOuterZ + 0.008f;
            Box(boom, "Festoon_Track", new Vector3(mid, -0.006f, festZ),
                new Vector3(len, 0.004f, 0.004f), CDark);
            int loops = 12;
            var festX = new float[loops];
            for (int i = 0; i < loops; i++)
            {
                float fx = Mathf.Lerp(x0 + 0.05f, x1 - 0.05f, i / (float)(loops - 1));
                festX[i] = fx;
                // 페스툰 새들(트랙에 매달린 케이블 행거)
                Box(boom, "Festoon_Saddle", new Vector3(fx, -0.013f, festZ),
                    new Vector3(0.005f, 0.014f, 0.005f), CDark);
            }
            // 늘어진 케이블 루프 — 인접 새들 사이가 아래로 처지는 카테너리(Unity Splines로 곡선 튜브 베이크).
            //   새들 하단(festBotY)에서 매달려 스팬 중앙이 festSag만큼 처짐(직선 막대 → 실제 페스툰 곡선).
            float festBotY = -0.020f, festSag = 0.012f;
            for (int i = 0; i < loops - 1; i++)
                CableCatenary(boom, "Festoon_Cable",
                    new Vector3(festX[i], festBotY, festZ),
                    new Vector3(festX[i + 1], festBotY, festZ),
                    festSag, 0.0016f, CCable);

            // ── 뒷부분(육지측 백리치) 디테일 ──
            // 평형추(counterweight) — 적층 무게 블록(긴 아웃리치 균형용)
            for (int i = 0; i < 3; i++)
            {
                Box(boom, "Counterweight", new Vector3(x0 + 0.05f, -0.05f + i * 0.022f, 0f),
                    new Vector3(0.09f, 0.02f, 2f * GirderOuterZ), CDark);
            }
            // 평형추 하부 받침 격자
            for (int s = -1; s <= 1; s += 2)
            {
                Strut(boom, "CW_Brace",
                    new Vector3(x0 + 0.01f, 0.0f, s * GirderGapZ),
                    new Vector3(x0 + 0.09f, -0.04f, s * GirderGapZ), 0.006f, CStruct);
            }
            // 백스테이 앵커 브래킷
            // 백스테이 이퀄라이저 빔 — 두 거더 뒤끝(±GirderGapZ)을 잇고 백스테이가 양 끝에 물림
            Box(boom, "Stay_Anchor", new Vector3(x0 + 0.02f, 0.075f, 0f),
                new Vector3(0.02f, 0.035f, 2f * GirderGapZ), CMachine);
            // 백리치 끝 플랫폼 + 경고등
            Box(boom, "Back_Platform", new Vector3(x0 + 0.02f, 0.066f, 0f),
                new Vector3(0.06f, 0.004f, 2f * GirderOuterZ), CMachine);
            // 백리치 경고등 숨김(사용자 요청) — 복구하려면 주석 해제
            // Ball(boom, "Back_Light", new Vector3(x0 + 0.004f, 0.05f, 0f),
            //     new Vector3(0.012f, 0.016f, 0.012f), CWarn);
            // 변압기 + 냉각 유닛 숨김(사용자 요청) — 복구하려면 주석 해제
            // Box(boom, "Transformer", new Vector3(x0 + 0.15f, 0.085f, GirderGapZ),
            //     new Vector3(0.05f, 0.05f, 0.04f), CMachine);
            // Box(boom, "Cooling_Unit", new Vector3(x0 + 0.15f, 0.082f, -GirderGapZ),
            //     new Vector3(0.05f, 0.044f, 0.04f), CDark);

            // ── 전체 디테일 보강 (붐) ──
            // 전력/제어 도관(conduit) — 붐 하부 전장 양옆
            for (int s = -1; s <= 1; s += 2)
            {
                Rod(boom, "Conduit",
                    new Vector3(x0, -0.012f, s * GirderGapZ),
                    new Vector3(x1, -0.012f, s * GirderGapZ), 0.004f, CDark);
            }
            // 붐 코너 작업등 추가
            foreach (float lx in new[] { x0 + 0.06f, x1 - 0.05f })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(boom, "Floodlight_Housing", new Vector3(lx, -0.006f, s * GirderGapZ),
                        new Vector3(0.014f, 0.012f, 0.014f), CDark);
                    Ball(boom, "Floodlight_Lens", new Vector3(lx, -0.014f, s * GirderGapZ),
                        new Vector3(0.011f, 0.006f, 0.011f), CLight);
                }
            }
        }

        // ───────── 붐 측면 트러스 입체화 + 측면 케이블 트레이 (1단계) ─────────
        // 트윈 박스 거더가 옆에서 보면 납작한 띠 두 줄 → '장난감 같다'의 핵심 원인.
        //   각 거더 바깥/안쪽 면에 상·하현재(chord)를, 바깥 면엔 워런 빗재(지그재그)를 둘러
        //   깊이감 있는 트러스 거더 실루엣으로. 거더 본체/레일/트롤리 좌표는 건드리지 않는 순수 추가.
        // 폴리 통제: 워런(빗재 위주) + 수직재 격번(2칸마다), 현재 캡은 가는 박스.
        static void BuildBoomTrussDepth(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            float len = x1 - x0, mid = (x0 + x1) * 0.5f;

            const float gY = 0.04f, gH = 0.05f;
            float gTop = gY + gH * 0.5f;   // 거더 윗면 0.065 (보도 그레이팅 밑면과 같은 레벨)
            float gBot = gY - gH * 0.5f;   // 거더 밑면 0.015

            // 트러스 현재 — 위는 보도 밑면(0.065)에 맞춤. 아래는 주행 트롤리 상단(y=0, Z 반폭 0.185로
            //   거더 바깥 면까지 옴)과 겹치지 않게 그 바로 위(0.003)까지만 내려 깊이 확장.
            float chTop = gTop;            // 상현재 0.065
            float chBot = 0.003f;          // 하현재(트롤리 상단 위 — 주행 중 관통 방지)
            float chMidY = (chTop + chBot) * 0.5f;
            float chH = chTop - chBot;

            int web = 16;                  // 빗재 베이 수
            float wstep = len / web;

            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * GirderGapZ;
                float zo = gz + s * (GirderWidthZ * 0.5f + 0.0015f);  // 바깥 면(웹 패널 + 트레이)
                float zi = gz - s * (GirderWidthZ * 0.5f + 0.0015f);  // 안쪽 면(현재만)

                // 상·하현재 캡 — 바깥/안쪽 양면(거더를 ㅁ자 프레임으로 감쌈)
                foreach (float zf in new[] { zo, zi })
                {
                    Box(boom, "Boom_Chord_Top", new Vector3(mid, chTop, zf),
                        new Vector3(len, 0.007f, 0.007f), CStruct);
                    Box(boom, "Boom_Chord_Bot", new Vector3(mid, chBot, zf),
                        new Vector3(len, 0.007f, 0.007f), CStruct);
                }

                // 바깥 면 워런 빗재 — 상↔하현재 지그재그(삼각 분할 → 트러스 느낌)
                for (int i = 0; i < web; i++)
                {
                    float xa = x0 + wstep * i;
                    float xb = x0 + wstep * (i + 1);
                    bool up = (i % 2) == 0;
                    Strut(boom, "Boom_Web",
                        new Vector3(xa, up ? chBot : chTop, zo),
                        new Vector3(xb, up ? chTop : chBot, zo), 0.004f, CStruct);
                }
                // 패널 포인트 수직재 — 폴리 절약 위해 2칸마다만
                for (int i = 0; i <= web; i += 2)
                {
                    float px = x0 + wstep * i;
                    Box(boom, "Boom_WebPost", new Vector3(px, chMidY, zo),
                        new Vector3(0.005f, chH, 0.005f), CStruct);
                }

                // ── 연속 측면 케이블 트레이(바깥) + 클램프 + 케이블 다발 ──
                float trayZ = zo + s * 0.007f;
                float trayY = 0.03f;
                Box(boom, "Boom_CableTray", new Vector3(mid, trayY, trayZ),
                    new Vector3(len, 0.004f, 0.013f), CDark);                       // 트레이 바닥
                Box(boom, "Boom_CableTray_Lip", new Vector3(mid, trayY + 0.006f, trayZ + s * 0.0055f),
                    new Vector3(len, 0.009f, 0.003f), CStruct);                     // 바깥 립
                int clamps = 13;
                for (int i = 0; i <= clamps; i++)
                {
                    float cx = Mathf.Lerp(x0, x1, i / (float)clamps);
                    Box(boom, "Boom_TrayClamp", new Vector3(cx, trayY - 0.004f, trayZ),
                        new Vector3(0.005f, 0.011f, 0.02f), CStruct);               // 트레이 행거 클램프
                }
                for (int c = -1; c <= 1; c += 2)                                    // 케이블 다발 2줄
                    Rod(boom, "Boom_TrayCable",
                        new Vector3(x0, trayY + 0.001f, trayZ + c * 0.0028f),
                        new Vector3(x1, trayY + 0.001f, trayZ + c * 0.0028f), 0.0026f, CCable);
                foreach (float jx in new[] { mid - 0.45f, mid + 0.55f })           // 트레이 정션 박스 2(붐 측면 부착)
                    JunctionBox(boom, "Boom_TrayJBox", new Vector3(jx, trayY + 0.005f, trayZ + s * 0.006f),
                        new Vector3(0.03f, 0.024f, 0.013f), new Vector3(0f, 0f, s), new Vector3(0f, 0f, -s), CMachine);
            }
        }

        // 접근 디테일: 다리 수직 사다리 + 포털 상단 점검 플랫폼 + 난간
        static void BuildAccessDetails(Transform root)
        {
            // 다리 오른쪽(star측) 케이지 수직 사다리 — 격자 다리 외곽 밖으로 띄워 겹침 방지
            float legOuter = GaugeZ * 0.5f + LegSec * 1.7f * 0.5f;   // 격자 다리 외곽 z
            float ladderZ  = legOuter + 0.022f;                     // 사다리 위치
            // 붐 데크(붐 로컬 0.067 → 루트 RailH+0.067)까지 올려 기계실 접근 캣워크와 연결
            float ly0 = 0.05f, ly1 = RailH + 0.067f;
            float ladW = 0.03f;                       // 사다리 폭(stile 간격) — 그립 연장과 공유
            BuildLadder(root, LandLegX, ladderZ, ly0, ly1, ladW);

            // 사다리 상단 그립 연장 — 데크(ly1) 위로 stile을 더 올려 잡고 내려서는 손잡이 + 상단 가로대.
            //   데크가 사다리 가장자리에서 끝나(MHAccess_Deck step-off) 머리 위가 비므로, 이 연장으로 안전 승강 마감.
            float grabH = 0.04f;
            for (int s = -1; s <= 1; s += 2)
                Box(root, "Ladder_Grab", new Vector3(LandLegX + s * ladW * 0.5f, ly1 + grabH * 0.5f, ladderZ),
                    new Vector3(0.004f, grabH, 0.004f), CStruct);
            Rod(root, "Ladder_Grab_Top",
                new Vector3(LandLegX - ladW * 0.5f, ly1 + grabH, ladderZ),
                new Vector3(LandLegX + ladW * 0.5f, ly1 + grabH, ladderZ), 0.0022f, CStruct);

            // 다리에 고정하는 standoff 브래킷
            for (int i = 0; i <= 5; i++)
            {
                float by = Mathf.Lerp(ly0, ly1, i / 5f);
                Box(root, "Ladder_Bracket",
                    new Vector3(LandLegX, by, (legOuter + ladderZ) * 0.5f),
                    new Vector3(0.005f, 0.005f, ladderZ - legOuter), CStruct);
            }

            // 안전 케이지 — 외측 세로 가드바 3 + 후프(ㄷ자) 다단 (하부는 승하강 위해 생략)
            float cageZ = ladderZ + 0.03f;
            float cy0 = ly0 + 0.12f;
            foreach (float gx2 in new[] { -0.022f, 0f, 0.022f })
            {
                Box(root, "Cage_Bar", new Vector3(LandLegX + gx2, (cy0 + ly1) * 0.5f, cageZ),
                    new Vector3(0.004f, ly1 - cy0, 0.004f), CStruct);
            }
            int hoops = 6;
            for (int i = 0; i <= hoops; i++)
            {
                float hy = Mathf.Lerp(cy0, ly1, i / (float)hoops);
                Box(root, "Cage_Hoop", new Vector3(LandLegX, hy, cageZ),
                    new Vector3(0.05f, 0.004f, 0.004f), CStruct);
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(root, "Cage_Hoop", new Vector3(LandLegX + s * 0.025f, hy, (cageZ + ladderZ) * 0.5f),
                        new Vector3(0.004f, 0.004f, cageZ - ladderZ), CStruct);
                }
            }

            // 지그재그 계단탑 숨김(사용자 요청 — 사다리가 이미 있어 계단 불필요). 복구하려면 아래 한 줄 주석 해제.
            // 메서드 BuildStairTower(root) 본체는 그대로 남겨둠.
            // BuildStairTower(root);

            // 갠트리 페스툰 트랙 숨김(사용자 요청) — 복구하려면 주석 해제
            // float gfZ = -(GaugeZ * 0.5f);
            // Box(root, "Gantry_Festoon_Track",
            //     new Vector3((LandLegX + WaterLegX) * 0.5f, 0.088f, gfZ),
            //     new Vector3(LegSpanX, 0.004f, 0.004f), CDark);

            // 포털 상단 점검 캣워크 — 사용자 요청으로 제거(Access_Platform / Platform_Rail / RailMid / Toe / Post
            // + 받침 브래킷 Platform_Bracket). 복구하려면 아래 /* */ 만 지우면 됨.
            /*
            float apY = RailH - 0.09f;          // 데크 높이
            float apHZ = GaugeZ * 0.5f;         // 다리 위치(±)까지
            float apW = 0.05f;                  // 통로 폭(X)
            Box(root, "Access_Platform", new Vector3(LandLegX, apY, 0f),
                new Vector3(apW, 0.004f, apHZ * 2f), CMachine);
            // 다리에 받침 브래킷(떠 있지 않게) — 양 끝
            for (int s = -1; s <= 1; s += 2)
                Strut(root, "Platform_Bracket",
                    new Vector3(LandLegX, apY - 0.045f, s * apHZ),
                    new Vector3(LandLegX, apY - 0.002f, s * apHZ * 0.55f), 0.006f, CStruct);
            // 난간 — 긴 옆면(±X) 양쪽: 상단+중간 레일 + 토보드 + 기둥
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float rx = LandLegX + sx * apW * 0.5f;
                Box(root, "Platform_Rail", new Vector3(rx, apY + 0.032f, 0f),
                    new Vector3(0.004f, 0.004f, apHZ * 2f), CStruct);
                Box(root, "Platform_RailMid", new Vector3(rx, apY + 0.017f, 0f),
                    new Vector3(0.003f, 0.003f, apHZ * 2f), CStruct);
                Box(root, "Platform_Toe", new Vector3(rx, apY + 0.006f, 0f),
                    new Vector3(0.003f, 0.008f, apHZ * 2f), CStruct);
                int np = 6;
                for (int i = 0; i <= np; i++)
                {
                    float pz = Mathf.Lerp(-apHZ, apHZ, i / (float)np);
                    Box(root, "Platform_Post", new Vector3(rx, apY + 0.017f, pz),
                        new Vector3(0.004f, 0.034f, 0.004f), CStruct);
                }
            }
            */
        }

        // 지그재그(switchback) 계단탑 — land leg(-Z 측) 옆을 따라 지면→붐 레벨까지.
        //   비행(flight)마다 경사 스트링어 2 + 계단판 + 경사 핸드레일/기둥, 턴마다 중간 랜딩 + 난간.
        //   사다리(+Z)와 반대편이라 겹침 없음. 폴리 통제: 비행당 계단판 4, 난간 기둥 격번.
        static void BuildStairTower(Transform root)
        {
            float halfZ   = GaugeZ * 0.5f;
            float legHalf = LegSec * 1.7f * 0.5f;
            float zc      = -(halfZ + legHalf + 0.06f);   // 다리 -Z 면 바깥 계단 중심
            float stairW  = 0.05f;                         // 계단 폭(Z)
            float hzW     = stairW * 0.5f;

            float xL = LandLegX - 0.03f;
            float run = 0.15f;
            float xR = xL + run;

            float y0 = 0.06f, y1 = RailH - 0.06f;
            float flightRise = 0.19f;
            int   treads = 4;
            float railH = 0.05f;

            int flights = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / flightRise));

            // 타워 4코너 수직 프레임 포스트(떠 있지 않게 지면→상단) + 상·하 보 정리
            foreach (float fx in new[] { xL, xR })
            foreach (float fz in new[] { zc - hzW, zc + hzW })
                Box(root, "Stair_Frame", new Vector3(fx, (y0 + y1) * 0.5f, fz),
                    new Vector3(0.006f, y1 - y0, 0.006f), CStruct);

            bool dir = true;   // true: xL→xR 로 상승
            float y = y0;
            for (int f = 0; f < flights; f++)
            {
                float yTop = Mathf.Min(y + flightRise, y1);
                float xa = dir ? xL : xR;
                float xb = dir ? xR : xL;

                // 경사 스트링어 2(양옆 Z)
                for (int s = -1; s <= 1; s += 2)
                    Strut(root, "Stair_Stringer",
                        new Vector3(xa, y, zc + s * hzW), new Vector3(xb, yTop, zc + s * hzW), 0.006f, CStruct);

                // 계단판(트레드)
                for (int i = 1; i <= treads; i++)
                {
                    float ti = i / (float)(treads + 1);
                    Box(root, "Stair_Tread",
                        new Vector3(Mathf.Lerp(xa, xb, ti), Mathf.Lerp(y, yTop, ti), zc),
                        new Vector3(run / (treads + 1) * 0.9f, 0.004f, stairW), CMachine);
                }

                // 경사 핸드레일 + 기둥(양옆) — 안전 난간
                for (int s = -1; s <= 1; s += 2)
                {
                    float rz = zc + s * (hzW + 0.002f);
                    Strut(root, "Stair_Rail",
                        new Vector3(xa, y + railH, rz), new Vector3(xb, yTop + railH, rz), 0.004f, CStruct);
                    for (int i = 0; i <= 1; i++)   // 기둥 2(시작/끝) — 폴리 절약
                    {
                        float ti = i;
                        Box(root, "Stair_RailPost",
                            new Vector3(Mathf.Lerp(xa, xb, ti), Mathf.Lerp(y, yTop, ti) + railH * 0.5f, rz),
                            new Vector3(0.004f, railH, 0.004f), CStruct);
                    }
                }

                // 중간 랜딩(턴 플랫폼) + 둘레 난간 — 최상단 비행 제외하고 매 턴
                if (yTop < y1 - 1e-4f)
                {
                    Box(root, "Stair_Landing", new Vector3(xb, yTop, zc),
                        new Vector3(0.055f, 0.005f, stairW + 0.012f), CMachine);
                    for (int s = -1; s <= 1; s += 2)   // 랜딩 바깥 난간(상단 가로 + 기둥 2)
                    {
                        float rz = zc + s * (hzW + 0.006f);
                        Box(root, "Landing_Rail", new Vector3(xb, yTop + railH, rz),
                            new Vector3(0.055f, 0.004f, 0.004f), CStruct);
                        for (int e = -1; e <= 1; e += 2)
                            Box(root, "Landing_Post", new Vector3(xb + e * 0.024f, yTop + railH * 0.5f, rz),
                                new Vector3(0.004f, railH, 0.004f), CStruct);
                    }
                    // 다리에 묶는 수평 타이 브래킷(떠 있지 않게)
                    Strut(root, "Stair_Tie",
                        new Vector3(xb, yTop, zc + hzW),
                        new Vector3(LandLegX, yTop, -(halfZ + legHalf)), 0.004f, CStruct);
                }

                y = yTop;
                dir = !dir;
            }

            // 최상단 도착 랜딩(붐/포털 레벨 접속) + 난간
            Box(root, "Stair_TopLanding", new Vector3((xL + xR) * 0.5f, y1, zc),
                new Vector3(run + 0.04f, 0.005f, stairW + 0.012f), CMachine);
            for (int s = -1; s <= 1; s += 2)
            {
                float rz = zc + s * (hzW + 0.006f);
                Box(root, "TopLanding_Rail", new Vector3((xL + xR) * 0.5f, y1 + railH, rz),
                    new Vector3(run + 0.04f, 0.004f, 0.004f), CStruct);
                for (int i = 0; i <= 3; i++)
                    Box(root, "TopLanding_Post",
                        new Vector3(Mathf.Lerp(xL - 0.02f, xR + 0.02f, i / 3f), y1 + railH * 0.5f, rz),
                        new Vector3(0.004f, railH, 0.004f), CStruct);
            }
        }

        // 사다리 정상 ↔ 기계실 접근 캣워크 + 기계실 +Z(사다리쪽) 출입문 (붐 로컬).
        //   붐은 루트에 (0,RailH,0)로 고정 → 루트 사다리의 z는 그대로, y만 RailH 오프셋으로 정합.
        //   캣워크: 사다리(z≈0.377) → 코너(x=LandLegX) → 기계실 문(x=MachineryHouseX), 데크 레벨(붐 로컬 0.067).
        static void BuildMachineryHouseAccess(Transform boom)
        {
            float deckY   = 0.067f;
            float mhx     = MachineryHouseX;                 // ≈ -0.207
            float mhZ     = 2f * GirderGapZ + 0.08f;         // 0.40
            float mhHZ    = mhZ * 0.5f;                      // +Z 면 z = 0.20
            float ladderZ = GaugeZ * 0.5f + LegSec * 1.7f * 0.5f + 0.022f;  // 사다리 z ≈ 0.377
            float walkW   = 0.055f;                          // 캣워크 폭
            float railH   = 0.035f;
            float corZ    = mhHZ + 0.035f;                   // 캣워크 코너 z(=0.235, 기계실 벽 바깥)

            // 경로(우회): 사다리(z≈ladderZ) 정상에서 다리·포털 바깥(+Z)으로 빠져나간 뒤,
            //   기계실 X(x=mhx)에서 안쪽(−Z)으로 꺾어 문 앞(corZ)으로 진입. → 다리 기둥/포털 크로스빔을 통과하지 않음.
            // ── 세그먼트1: 사다리(x=LandLegX) → 기계실 X(x=mhx), z=ladderZ (다리 바깥 우회) ──
            //   사다리(x=LandLegX)는 데크로 덮지 않는다. 덮으면 사다리 위가 철판에 막힘(기존 +X 오버행 walkW/2가 원인).
            //   데크를 사다리 가장자리에서 끝내 step-off 랜딩으로 만들고, 머리 위는 개구부로 비운다.
            float deckFarX  = mhx - walkW * 0.5f;   // 기계실 코너쪽 끝 — 세그2와 정합 위해 walkW/2 오버행 유지
            float ladHalf   = 0.015f;               // 사다리 반폭(BuildAccessDetails ladW=0.03과 정합)
            float deckNearX = LandLegX - ladHalf;   // 사다리 '안쪽 가장자리'에서 끝냄 — 사다리 폭(±ladHalf)만큼 위를 비워 개구부 확보.
                                                    //   LandLegX(사다리 중심)로 끝내면 사다리 안쪽 절반이 데크에 덮여 머리가 막힌다(이번 버그).
            float s1x   = (deckFarX + deckNearX) * 0.5f;
            float s1len = deckNearX - deckFarX;
            Box(boom, "MHAccess_Deck", new Vector3(s1x, deckY, ladderZ),
                new Vector3(s1len, 0.004f, walkW), CMachine);

            // ── 세그먼트2: 코너(x=mhx) → 문 앞(z=corZ), x=mhx ──
            float s2z = (ladderZ + corZ) * 0.5f;
            float s2len = ladderZ - corZ + walkW;
            Box(boom, "MHAccess_Deck", new Vector3(mhx, deckY, s2z),
                new Vector3(walkW, 0.004f, s2len), CMachine);

            // ── 난간: 다리 반대편(바깥)에만 — 세그1 +Z, 세그2 −X(기계실 쪽). 다리에 안 닿게 통로 확보. ──
            float r1z = ladderZ + walkW * 0.5f;
            Box(boom, "MHAccess_Rail", new Vector3(s1x, deckY + railH, r1z),
                new Vector3(s1len, 0.004f, 0.004f), CStruct);
            for (int i = 0; i <= 4; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(Mathf.Lerp(LandLegX, mhx, i / 4f), deckY + railH * 0.5f, r1z),
                    new Vector3(0.004f, railH, 0.004f), CStruct);
            float r2x = mhx - walkW * 0.5f;
            Box(boom, "MHAccess_Rail", new Vector3(r2x, deckY + railH, s2z),
                new Vector3(0.004f, 0.004f, s2len), CStruct);
            for (int i = 0; i <= 2; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(r2x, deckY + railH * 0.5f, Mathf.Lerp(corZ, ladderZ, i / 2f)),
                    new Vector3(0.004f, railH, 0.004f), CStruct);
            // 바깥쪽 볼록 꼭지점 기둥 — 두 바깥 난간(r1z·r2x)이 만나는 코너. 없으면 코너가 ㄴ처럼 비어 보임.
            Box(boom, "MHAccess_Post",
                new Vector3(mhx - walkW * 0.5f, deckY + railH * 0.5f, ladderZ + walkW * 0.5f),
                new Vector3(0.004f, railH, 0.004f), CStruct);

            // ── 안쪽 난간(다리 반대 면) — 세그1 −Z, 세그2 +X. 양측 난간으로 통로 완성. ──
            // 세그1 안쪽: 다리 기둥/사다리 승강 갭 확보 위해 다리쪽을 살짝 띄워 시작.
            float in1Start = LandLegX - 0.025f;
            float in1End   = mhx + walkW * 0.5f;   // 안쪽 코너에서 끝냄 — 세그2(직각 통로) 입구를 안 막게(길이 계산)
            float in1cx = (in1Start + in1End) * 0.5f;
            float in1len = Mathf.Abs(in1Start - in1End);
            float in1z = ladderZ - walkW * 0.5f;
            Box(boom, "MHAccess_Rail", new Vector3(in1cx, deckY + railH, in1z),
                new Vector3(in1len, 0.004f, 0.004f), CStruct);
            for (int i = 0; i <= 3; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(Mathf.Lerp(in1Start, in1End, i / 3f), deckY + railH * 0.5f, in1z),
                    new Vector3(0.004f, railH, 0.004f), CStruct);
            // 세그2 안쪽: 문 진입(낮은 z)은 열어두고, 위로는 안쪽 코너에서 끝냄(세그1 통로를 안 막게).
            float in2x = mhx + walkW * 0.5f;
            float in2Top = ladderZ - walkW * 0.5f;   // 안쪽 코너 z — 세그1 통로 침범 방지(길이 계산)
            float in2cz  = (corZ + in2Top) * 0.5f;
            float in2len = Mathf.Abs(in2Top - corZ);
            Box(boom, "MHAccess_Rail", new Vector3(in2x, deckY + railH, in2cz),
                new Vector3(0.004f, 0.004f, in2len), CStruct);
            for (int i = 0; i <= 2; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(in2x, deckY + railH * 0.5f, Mathf.Lerp(corZ, in2Top, i / 2f)),
                    new Vector3(0.004f, railH, 0.004f), CStruct);

            // ── 받침 아웃리거 브래킷(떠 있지 않게) — 캣워크 → 거더 상단. 다리선(x=LandLegX)은 피함. ──
            foreach (float bx in new[] { LandLegX - 0.06f, mhx })
                Strut(boom, "MHAccess_Bracket",
                    new Vector3(bx, deckY, ladderZ), new Vector3(bx, 0.065f, GirderOuterZ), 0.004f, CStruct);
            Strut(boom, "MHAccess_Bracket",
                new Vector3(mhx, deckY, corZ), new Vector3(mhx, 0.065f, GirderOuterZ), 0.004f, CStruct);

            // ── 기계실 +Z(사다리쪽) 출입문 한 짝: 문틀 + 문짝 + 손잡이 + 킥플레이트 + 문턱 ──
            float dz = mhHZ;          // +Z 면
            float doorY = 0.103f;     // 문 중심(밑은 데크 0.067, 위는 -Z면 창 영역 아래)
            Box(boom, "MH_DoorFrame", new Vector3(mhx, doorY, dz - 0.001f),
                new Vector3(0.052f, 0.078f, 0.005f), CStruct);
            Box(boom, "MH_Door", new Vector3(mhx, doorY - 0.002f, dz + 0.002f),
                new Vector3(0.045f, 0.068f, 0.005f), CDark);
            Box(boom, "MH_DoorHandle", new Vector3(mhx + 0.016f, doorY, dz + 0.005f),
                new Vector3(0.004f, 0.012f, 0.004f), CStruct);
            Box(boom, "MH_DoorKick", new Vector3(mhx, doorY - 0.03f, dz + 0.003f),
                new Vector3(0.045f, 0.016f, 0.003f), CStruct);
            Box(boom, "MH_DoorThreshold", new Vector3(mhx, deckY + 0.001f, dz + 0.016f),
                new Vector3(0.052f, 0.004f, 0.032f), CMachine);
        }

        // 시브 네스트 (4단계) — 시브(축 Z) 양옆 치크 플레이트 + 관통 핀 보스(축단 캡 포함).
        //   납작한 실린더로 치크 원판을, 가는 실린더로 축을 표현해 '시브 블록' 느낌으로.
        static void SheaveNest(Transform parent, Vector3 center, float sheaveR, float sheaveHalfZ, Color cheekC)
        {
            float chZ = sheaveHalfZ + 0.003f;
            for (int s = -1; s <= 1; s += 2)
                Rod(parent, "Sheave_Cheek",
                    center + new Vector3(0f, 0f, s * chZ),
                    center + new Vector3(0f, 0f, s * (chZ + 0.004f)), sheaveR * 1.18f, cheekC);
            Rod(parent, "Sheave_Pin",
                center + new Vector3(0f, 0f, -(chZ + 0.01f)),
                center + new Vector3(0f, 0f,  (chZ + 0.01f)), sheaveR * 0.26f, CDark);
            for (int s = -1; s <= 1; s += 2)
                Rod(parent, "Sheave_PinCap",
                    center + new Vector3(0f, 0f, s * (chZ + 0.008f)),
                    center + new Vector3(0f, 0f, s * (chZ + 0.012f)), sheaveR * 0.45f, CStruct);
        }

        // 스테이 케이블 + 앵커 플레이트(양끝) + 턴버클(붐 쪽 하단)
        static void BuildStay(Transform root, Vector3 a, Vector3 b, string name)
        {
            Rod(root, name, a, b, 0.004f, CCable);
            Box(root, "Stay_Plate", a, new Vector3(0.012f, 0.012f, 0.012f), CMachine);
            Box(root, "Stay_Plate", b, new Vector3(0.012f, 0.012f, 0.012f), CMachine);
            Vector3 dir = a - b;
            float len = dir.magnitude;
            if (len > 1e-4f)
            {
                dir /= len;
                Rod(root, "Turnbuckle", b + dir * 0.03f, b + dir * 0.06f, 0.008f, CDark);
            }
        }

        // 경사 사다리: a→b 축을 따라 2 stile + 가로 rung. offset 만큼 바깥으로 띄워 구조물 박힘 방지.
        static void BuildInclinedLadder(Transform root, Vector3 a, Vector3 b, float width, Vector3 offset)
        {
            a += offset; b += offset;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            d /= len;
            Vector3 side = Vector3.Cross(d, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side = side.normalized;
            for (int s = -1; s <= 1; s += 2)
            {
                Strut(root, "Ladder_Stile",
                    a + side * (s * width * 0.5f), b + side * (s * width * 0.5f), 0.004f, CStruct);
            }
            int rungs = Mathf.Max(3, Mathf.RoundToInt(len / 0.04f));
            for (int i = 0; i <= rungs; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)rungs);
                Rod(root, "Ladder_Rung",
                    p - side * (width * 0.5f), p + side * (width * 0.5f), 0.0025f, CStruct);
            }
        }

        // 수직 사다리: 양옆 stile + 가로 rung(원통)
        static void BuildLadder(Transform parent, float x, float z, float y0, float y1, float widthX)
        {
            float midY = (y0 + y1) * 0.5f;
            float h = y1 - y0;
            for (int s = -1; s <= 1; s += 2)
            {
                Box(parent, "Ladder_Stile", new Vector3(x + s * widthX * 0.5f, midY, z),
                    new Vector3(0.004f, h, 0.004f), CStruct);
            }
            int rungs = Mathf.Max(2, Mathf.RoundToInt(h / 0.04f));
            for (int i = 0; i <= rungs; i++)
            {
                float ry = Mathf.Lerp(y0, y1, i / (float)rungs);
                Rod(parent, "Ladder_Rung",
                    new Vector3(x - widthX * 0.5f, ry, z),
                    new Vector3(x + widthX * 0.5f, ry, z), 0.0022f, CStruct);
            }
        }

        // ───────────────────────── primitive 헬퍼 ─────────────────────────

        static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = NewCube(name, parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Colorize(go, color);
            return go;
        }

        // ───────── 장비 인클로저 라이브러리 (민짜 큐브 → 실제 형상) ─────────
        // 정션박스/캐비닛/공조유닛/파워팩을 '함체+부속'으로 조형하는 재사용 헬퍼.
        // 면 법선에서 면내 축을 산식으로 도출 → 어느 방향으로 두든 부속이 면에 맞물린다.

        static Vector3 AbsV(Vector3 a) => new Vector3(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z));

        // 정션 박스(전기 결선함) — 함체 + 볼트 덮개 + 케이블 글랜드 + 베이스 플랜지.
        //   center=함체 중심, body=함체 치수, coverFace=점검 도어 향하는 면 법선(축정렬 단위),
        //   glandDir=케이블 인입/부착 방향(데크형=Vector3.down, 벽부착형=벽 안쪽).
        static void JunctionBox(Transform parent, string name, Vector3 center, Vector3 body,
                                Vector3 coverFace, Vector3 glandDir, Color bodyC)
        {
            Vector3 fAbs = AbsV(coverFace);
            Vector3 u, v;   // 덮개 면내 두 축
            if (fAbs.x > 0.5f) { u = Vector3.up;    v = Vector3.forward; }
            else if (fAbs.y > 0.5f) { u = Vector3.right; v = Vector3.forward; }
            else { u = Vector3.right; v = Vector3.up; }
            float eF = Vector3.Dot(fAbs, body) * 0.5f;
            float eU = Vector3.Dot(AbsV(u), body) * 0.5f;
            float eV = Vector3.Dot(AbsV(v), body) * 0.5f;

            // 1) 함체
            Box(parent, name + "_Body", center, body, bodyC);

            // 2) 볼트 덮개(면내 86% + 모서리 볼트 4)
            float coverT = Mathf.Max(0.0022f, eF * 0.18f);
            Vector3 coverSize = fAbs * coverT + AbsV(u) * (2f * eU * 0.86f) + AbsV(v) * (2f * eV * 0.86f);
            Box(parent, name + "_Cover", center + coverFace * (eF + coverT * 0.5f), coverSize, CStruct);
            float boltR = Mathf.Max(0.0011f, Mathf.Min(eU, eV) * 0.14f);
            Vector3 boltScale = fAbs * (boltR * 1.4f) + (AbsV(u) + AbsV(v)) * (boltR * 2f);
            Vector3 boltBase = center + coverFace * (eF + coverT);
            for (int su = -1; su <= 1; su += 2)
            for (int sv = -1; sv <= 1; sv += 2)
                Ball(parent, name + "_Bolt",
                    boltBase + u * (su * eU * 0.66f) + v * (sv * eV * 0.66f), boltScale, CDark);

            // 3) 케이블 글랜드 2(너트 + 케이블 스텁)
            Vector3 gAbs = AbsV(glandDir);
            Vector3 gu = (gAbs.y > 0.5f) ? Vector3.right : Vector3.up;
            float eG  = Vector3.Dot(gAbs, body) * 0.5f;
            float eGu = Vector3.Dot(AbsV(gu), body) * 0.5f;
            float glandR = Mathf.Max(0.0012f, eGu * 0.18f);
            float glandLen = Mathf.Max(0.004f, eG * 0.8f);
            Vector3 gNutScale = gAbs * (glandR * 1.2f) + (Vector3.one - gAbs) * (glandR * 2.2f);
            Vector3 gBase = center + glandDir * eG;
            for (int g = -1; g <= 1; g += 2)
            {
                Vector3 gp = gBase + gu * (g * eGu * 0.45f);
                Ball(parent, name + "_GlandNut", gp, gNutScale, CStruct);
                Rod(parent, name + "_Cable", gp, gp + glandDir * glandLen, glandR * 0.7f, CCable);
            }

            // 4) 베이스 플랜지(부착면) + 앵커 볼트 2 — 떠 보이지 않게 접지
            Vector3 bodyPerpG = Vector3.Scale(body, Vector3.one - gAbs);
            Box(parent, name + "_Base", center + glandDir * (eG + 0.0015f),
                gAbs * 0.003f + bodyPerpG * 1.35f, CStruct);
            Vector3 abAxis = (gAbs.y > 0.5f) ? Vector3.forward : Vector3.up;
            float eAb = Vector3.Dot(AbsV(abAxis), body) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
                Ball(parent, name + "_AnchorBolt",
                    center + glandDir * (eG + 0.003f) + abAxis * (s * eAb * 1.2f),
                    gAbs * (boltR * 1.2f) + (Vector3.one - gAbs) * (boltR * 1.8f), CDark);
        }

        // 제어 캐비닛(직립) — 플린스 + 본체 + 더블도어/손잡이 + 측면 루버 + 지붕 오버행 + 상부 공조 + 인양러그.
        //   frontFace=도어 향하는 수평 면 법선(±X/±Z). 높이는 항상 +Y.
        static void ControlCabinet(Transform parent, string name, Vector3 center, Vector3 body,
                                   Vector3 frontFace, Color bodyC)
        {
            Vector3 up = Vector3.up;
            Vector3 fAbs = AbsV(frontFace);
            Vector3 wide = Vector3.Cross(up, frontFace);
            if (wide.sqrMagnitude < 1e-6f) wide = Vector3.right;
            wide = wide.normalized;
            Vector3 wAbs = AbsV(wide);
            float eF = Vector3.Dot(fAbs, body) * 0.5f;
            float eW = Vector3.Dot(wAbs, body) * 0.5f;
            float eH = body.y * 0.5f;
            float topY = center.y + eH, botY = center.y - eH;

            Box(parent, name + "_Plinth", new Vector3(center.x, botY + 0.004f, center.z),
                fAbs * (2f * eF * 0.9f) + wAbs * (2f * eW * 0.96f) + up * 0.008f, CDark);
            Box(parent, name + "_Body", center + up * 0.004f, body, bodyC);
            Box(parent, name + "_Roof", new Vector3(center.x, topY + 0.003f, center.z),
                fAbs * (2f * eF + 0.006f) + wAbs * (2f * eW + 0.006f) + up * 0.005f, CStruct);

            // 더블 도어(중앙 이음매 분할) + 손잡이
            float doorT = 0.0022f;
            Vector3 doorFacePos = center + frontFace * (eF + doorT * 0.5f) + up * 0.004f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 leaf = doorFacePos + wide * (s * eW * 0.49f);
                Box(parent, name + "_Door", leaf,
                    fAbs * doorT + wAbs * (eW * 0.9f) + up * (2f * eH * 0.82f), CStruct);
                Box(parent, name + "_DoorHandle", leaf - wide * (s * eW * 0.42f) + frontFace * doorT,
                    fAbs * 0.004f + wAbs * 0.004f + up * (eH * 0.5f), CDark);
            }
            // 측면(±wide) 가로 루버 3
            for (int s = -1; s <= 1; s += 2)
            for (int l = -1; l <= 1; l++)
                Box(parent, name + "_Louver",
                    center + wide * (s * (eW + 0.0015f)) + up * (l * eH * 0.4f),
                    wAbs * 0.003f + fAbs * (2f * eF * 0.7f) + up * 0.004f, CDark);
            // 지붕 공조 박스 + 인양 러그 2
            Box(parent, name + "_TopAC",
                new Vector3(center.x, topY + 0.012f, center.z) - frontFace * (eF * 0.3f),
                fAbs * (2f * eF * 0.5f) + wAbs * (2f * eW * 0.5f) + up * 0.014f, CMachine);
            for (int s = -1; s <= 1; s += 2)
                Box(parent, name + "_LiftLug",
                    new Vector3(center.x, topY + 0.008f, center.z) + wide * (s * eW * 0.8f),
                    wAbs * 0.004f + fAbs * 0.006f + up * 0.008f, CStruct);
        }

        // 공조/냉각 유닛(상부 팬) — 스키드 + 본체 + 팬가드(링+십자스포크+허브) + 측면 루버 + 전면 파이프/패널 + 인양러그.
        static void EquipmentUnit(Transform parent, string name, Vector3 center, Vector3 body, Color bodyC)
        {
            float ex = body.x * 0.5f, ey = body.y * 0.5f, ez = body.z * 0.5f;
            float topY = center.y + ey, botY = center.y - ey;

            Box(parent, name + "_Skid", new Vector3(center.x, botY + 0.002f, center.z),
                new Vector3(body.x * 1.1f, 0.005f, body.z * 1.1f), CStruct);
            Box(parent, name + "_Body", center, body, bodyC);

            float fanR = Mathf.Min(ex, ez) * 0.82f;
            Vector3 fanCtr = new Vector3(center.x, topY + 0.003f, center.z);
            Rod(parent, name + "_FanRing",
                fanCtr - Vector3.up * 0.0015f, fanCtr + Vector3.up * 0.0015f, fanR, CDark);
            Box(parent, name + "_FanSpoke", fanCtr, new Vector3(2f * fanR, 0.0015f, 0.0025f), CStruct);
            Box(parent, name + "_FanSpoke", fanCtr, new Vector3(0.0025f, 0.0015f, 2f * fanR), CStruct);
            Ball(parent, name + "_FanHub", fanCtr, Vector3.one * (fanR * 0.4f), CMachine);

            for (int s = -1; s <= 1; s += 2)
            for (int l = -1; l <= 1; l++)
                Box(parent, name + "_Louver",
                    new Vector3(center.x, center.y + l * ey * 0.45f, center.z + s * (ez + 0.0015f)),
                    new Vector3(2f * ex * 0.8f, 0.004f, 0.003f), CDark);

            for (int s = -1; s <= 1; s += 2)
                Rod(parent, name + "_Pipe",
                    new Vector3(center.x - ex, center.y + s * ey * 0.35f, center.z + s * ez * 0.3f),
                    new Vector3(center.x - ex - 0.008f, center.y + s * ey * 0.35f, center.z + s * ez * 0.3f),
                    0.0022f, CMachine);
            Box(parent, name + "_AccessPanel", new Vector3(center.x - ex - 0.0011f, center.y, center.z),
                new Vector3(0.0022f, 2f * ey * 0.7f, 2f * ez * 0.7f), CStruct);
            for (int s = -1; s <= 1; s += 2)
                Box(parent, name + "_LiftLug",
                    new Vector3(center.x + s * ex * 0.8f, topY + 0.006f, center.z),
                    new Vector3(0.004f, 0.008f, 0.004f), CStruct);
        }

        // 유압 파워팩 — 오일탱크 + 상부 전동기/펌프(가로 실린더) + 매니폴드/유압포트 + 필러캡 + 유면계 + 스키드.
        //   매니폴드/포트는 중앙 방향(-X)으로 인출하고, 포트 끝 좌표 2개를 반환 → 호스가 그 끝에 물려 허공에 안 뜬다.
        static Vector3[] PowerPack(Transform parent, string name, Vector3 center, Vector3 body, Color bodyC)
        {
            float ex = body.x * 0.5f, ey = body.y * 0.5f, ez = body.z * 0.5f;
            float mY = center.y + ey + 0.006f;

            Box(parent, name + "_Tank", center, body, bodyC);
            Box(parent, name + "_Skid", new Vector3(center.x, center.y - ey + 0.002f, center.z),
                new Vector3(body.x * 1.1f, 0.004f, body.z * 1.1f), CStruct);
            // 전동기(축 Z) + 펌프(끝단 작은 실린더)
            Rod(parent, name + "_Motor",
                new Vector3(center.x, mY, center.z - ez * 0.6f),
                new Vector3(center.x, mY, center.z + ez * 0.6f), Mathf.Min(ex, ey) * 0.45f, CMachine);
            Rod(parent, name + "_Pump",
                new Vector3(center.x, mY, center.z + ez * 0.6f),
                new Vector3(center.x, mY, center.z + ez * 0.92f), Mathf.Min(ex, ey) * 0.28f, CDark);
            // 매니폴드(중앙 방향 -X 끝 상부) + 유압 포트 2(-X로 인출, 끝에 커플링)
            float portY = center.y + ey + 0.003f;
            float manX = center.x - ex * 0.35f;
            Box(parent, name + "_Manifold", new Vector3(manX, portY, center.z),
                new Vector3(ex * 0.5f, 0.01f, ez * 0.7f), CDark);
            var tips = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                int s = i == 0 ? -1 : 1;
                Vector3 a = new Vector3(manX - ex * 0.2f, portY, center.z + s * ez * 0.35f);
                Vector3 b = new Vector3(center.x - ex - 0.01f, portY, center.z + s * ez * 0.35f);
                Rod(parent, name + "_Port", a, b, 0.0018f, CMachine);
                Ball(parent, name + "_PortCoupling", b, Vector3.one * 0.004f, CStruct);
                tips[i] = b;
            }
            // 주유/브리더 캡 — 떠 보이지 않게 탱크 윗면에 '묻은' 짧은 원통 넥 + 윗 캡(구 아님). +X쪽 배치.
            Vector3 fcBase = new Vector3(center.x + ex * 0.4f, center.y + ey - 0.002f, center.z); // 탱크 안에서 시작
            Rod(parent, name + "_FillerNeck", fcBase, fcBase + Vector3.up * 0.008f, 0.0035f, CMachine);
            Rod(parent, name + "_FillerCap", fcBase + Vector3.up * 0.007f, fcBase + Vector3.up * 0.011f, 0.0042f, CStruct);
            Box(parent, name + "_SightGauge", new Vector3(center.x + ex + 0.0011f, center.y, center.z + ez * 0.4f),
                new Vector3(0.0022f, 2f * ey * 0.5f, 0.004f), CGlass);
            return tips;
        }

        // 붐 상부 보도 부재가 포털 빔과 만나는 X 구간 — 그 폭만큼 보도를 끊는다(다리 X마다).
        // Portal_Cross X 반폭(LegSec*0.4) + 여유. BuildBoomStructure/Details(붐 로컬)에서 공통 사용.
        static readonly float[] BoomTopWalkwayGapX = { LandLegX, WaterLegX };
        const float BoomTopWalkwayGapHalf = LegSec * 0.4f + 0.006f;

        // X축으로 긴 보도 박스를 gapCenters±gapHalf 구간에서 끊어 여러 토막으로 생성(포털 빔 관통 방지).
        static void BoxGappedX(Transform parent, string name, float x0, float x1, float y, float z,
                               float thickY, float thickZ, Color color, float[] gapCenters, float gapHalf)
        {
            var cuts = new List<float> { x0 };
            foreach (float gc in gapCenters)
                if (gc - gapHalf > x0 && gc + gapHalf < x1) { cuts.Add(gc - gapHalf); cuts.Add(gc + gapHalf); }
            cuts.Add(x1);
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i += 2)   // 유지 구간 = (cuts[0],cuts[1]),(cuts[2],cuts[3])...
            {
                float a = cuts[i], b = cuts[i + 1];
                if (b - a > 0.004f)
                    Box(parent, name, new Vector3((a + b) * 0.5f, y, z),
                        new Vector3(b - a, thickY, thickZ), color);
            }
        }

        // a→b 를 잇는 가는 막대(다리/케이블/대각). 부모는 회전·스케일 없는 노드여야 정확.
        static GameObject Strut(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Color color)
        {
            var go = NewCube(name, parent);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(thickness, len, thickness);
            Colorize(go, color);
            return go;
        }

        // a→b 를 잇는 원통(케이블/로프/바퀴 등 둥근 부재). 기본 실린더 높이 2 기준.
        static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Color color)
        {
            var go = NewPrimitive(PrimitiveType.Cylinder, name, parent);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
            Colorize(go, color);
            return go;
        }

        // 구체(램프·돔·풍속계 컵 등 둥근 부재). scale로 눌러 돔/타원도 표현.
        static GameObject Ball(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = NewPrimitive(PrimitiveType.Sphere, name, parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Colorize(go, color);
            return go;
        }

        // 원뿔/절두원뿔 — Unity 기본 프리미티브에 없어 메시를 절차 생성.
        // a=밑면 중심, b=꼭대기 중심. rBottom=밑면 반지름, rTop=윗면 반지름(0이면 뾰족한 콘).
        static GameObject Cone(Transform parent, string name, Vector3 a, Vector3 b,
                               float rBottom, float rTop, Color color, int seg = 24)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildFrustumMesh(rBottom, rTop, seg);   // 단위 높이(y=0→1), 반지름은 메시에 반영

            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = a;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(1f, len, 1f);     // 높이만 늘림(반지름 유지)
            mr.sharedMaterial = GetMaterial(color);
            return go;
        }

        // 단위 높이(y=0→1) 절두원뿔 메시 — 옆면 + 위/아래 캡.
        static Mesh BuildFrustumMesh(float rBottom, float rTop, int seg)
        {
            seg = Mathf.Max(8, seg);
            var verts = new List<Vector3>(seg * 2 + 2);
            var tris  = new List<int>(seg * 12);
            for (int i = 0; i < seg; i++)
            {
                float ang = (i / (float)seg) * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                verts.Add(new Vector3(cx * rBottom, 0f, cz * rBottom));  // 밑면 i → 2i
                verts.Add(new Vector3(cx * rTop,    1f, cz * rTop));     // 윗면 i → 2i+1
            }
            for (int i = 0; i < seg; i++)
            {
                int b0 = 2 * i, t0 = 2 * i + 1;
                int j = (i + 1) % seg;
                int b1 = 2 * j, t1 = 2 * j + 1;
                tris.Add(b0); tris.Add(t0); tris.Add(b1);   // 옆면(바깥 향함)
                tris.Add(b1); tris.Add(t0); tris.Add(t1);
            }
            int cBot = verts.Count; verts.Add(new Vector3(0f, 0f, 0f));
            int cTop = verts.Count; verts.Add(new Vector3(0f, 1f, 0f));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                tris.Add(cBot); tris.Add(2 * i);     tris.Add(2 * j);       // 아래 캡(-Y)
                tris.Add(cTop); tris.Add(2 * j + 1); tris.Add(2 * i + 1);   // 위 캡(+Y)
            }
            var m = new Mesh { name = "STS_Cone" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ───────── 트러스 절점 연결판(거싯) ─────────

        // 트러스 절점에 면과 평평하게 붙는 모서리 챔퍼 8각 판(용접 거싯 표현).
        // faceRot: 판 로컬 +Z를 면 바깥 노멀로 향하게 하는 회전(+Z면=identity, -Z면=Euler(0,180,0),
        //          +X면=Euler(0,90,0), -X면=Euler(0,-90,0)). 8각이라 직사각 박스보다 곡면 느낌.
        static GameObject Gusset(Transform parent, Vector3 localPos, Quaternion faceRot,
                                 float size, float thick, Color color)
        {
            string key = $"g{size:F4}_{thick:F4}";
            if (_plateCache == null) _plateCache = new Dictionary<string, Mesh>();
            if (!_plateCache.TryGetValue(key, out var mesh))
            {
                float s = size, c = size * 0.22f;   // c=모서리 챔퍼 폭(과하면 알약처럼 보임)
                var poly = new[]
                {
                    new Vector2(-s + c, -s), new Vector2(s - c, -s),
                    new Vector2(s, -s + c),  new Vector2(s,  s - c),
                    new Vector2(s - c,  s),  new Vector2(-s + c,  s),
                    new Vector2(-s,  s - c), new Vector2(-s, -s + c),
                };
                mesh = BuildPlateMesh(poly, thick);
                _plateCache[key] = mesh;
            }
            var go = new GameObject("Truss_Gusset");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = faceRot;
            Colorize(go, color);
            return go;
        }

        // 평면 볼록 다각형(로컬 XY 점들)을 thickness(로컬 Z)로 압출한 판재 — 앞/뒤 캡 + 옆면.
        // 플랫 셰이딩: 캡·옆면이 정점을 공유하지 않게 면마다 별도 정점을 둬 모서리가 날카롭게(강철판 느낌).
        static Mesh BuildPlateMesh(Vector2[] poly, float thickness)
        {
            int n = poly.Length;
            // CCW 보정 — 앞면(+Z) 캡 노멀이 바깥(+Z)을 향하게.
            float area = 0f;
            for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            var p = poly;
            if (area < 0f) { p = new Vector2[n]; for (int i = 0; i < n; i++) p[i] = poly[n - 1 - i]; }

            float hz = thickness * 0.5f;
            var v = new List<Vector3>();
            var t = new List<int>();

            // 앞 캡(+Z) — 자체 정점
            int f0 = v.Count;
            for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, hz));
            for (int i = 1; i < n - 1; i++) { t.Add(f0); t.Add(f0 + i); t.Add(f0 + i + 1); }
            // 뒤 캡(-Z) — 자체 정점(역와인딩)
            int b0 = v.Count;
            for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, -hz));
            for (int i = 1; i < n - 1; i++) { t.Add(b0); t.Add(b0 + i + 1); t.Add(b0 + i); }
            // 옆면 — 변마다 4정점(공유 안 함)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int q = v.Count;
                v.Add(new Vector3(p[i].x, p[i].y,  hz));   // fi
                v.Add(new Vector3(p[i].x, p[i].y, -hz));   // bi
                v.Add(new Vector3(p[j].x, p[j].y,  hz));   // fj
                v.Add(new Vector3(p[j].x, p[j].y, -hz));   // bj
                t.Add(q); t.Add(q + 1); t.Add(q + 2);
                t.Add(q + 2); t.Add(q + 1); t.Add(q + 3);
            }
            var m = new Mesh { name = "STS_Plate" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ───────── 접합부 볼트 (2단계) ─────────

        // 볼트 패턴 — 원형 링(앵커/플랜지 볼트 서클)
        static Vector2[] BoltRing(int n, float r)
        {
            var a = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float t = (i / (float)n) * Mathf.PI * 2f;
                a[i] = new Vector2(Mathf.Cos(t) * r, Mathf.Sin(t) * r);
            }
            return a;
        }

        // 볼트 패턴 — 격자(스플라이스 플레이트 볼트 그리드)
        static Vector2[] BoltGrid(int nx, int ny, float dx, float dy)
        {
            var list = new List<Vector2>(nx * ny);
            for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
                list.Add(new Vector2((ix - (nx - 1) * 0.5f) * dx, (iy - (ny - 1) * 0.5f) * dy));
            return list.ToArray();
        }

        // 여러 볼트 헤드를 담은 공유 메시 — 접합부당 GameObject 1개로 N개 볼트 표현.
        // 평면(로컬 z=0)에서 +Z로 돌출. faceRot로 면 노멀에 맞춰 배치.
        static Mesh GetBoltMesh(string key, Vector2[] pos, float boltR, float boltH, int seg = 6)
        {
            if (_boltCache == null) _boltCache = new Dictionary<string, Mesh>();
            if (_boltCache.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var p in pos) AddBoltPrism(v, t, p, boltR, boltH, seg);
            m = new Mesh { name = "STS_Bolts" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            _boltCache[key] = m;
            return m;
        }

        // 단일 볼트 헤드(seg각 저폴리 프리즘) — 면(z=0)에서 +Z로 h 돌출. 옆면 + 윗 캡(아랫면 생략, 면에 묻힘).
        static void AddBoltPrism(List<Vector3> v, List<int> t, Vector2 c, float r, float h, int seg)
        {
            int b = v.Count;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f + Mathf.PI / seg;
                float cx = Mathf.Cos(a) * r, cy = Mathf.Sin(a) * r;
                v.Add(new Vector3(c.x + cx, c.y + cy, 0f));   // 밑(면) → b+2i
                v.Add(new Vector3(c.x + cx, c.y + cy, h));    // 위(헤드) → b+2i+1
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                int b0 = b + 2 * i, t0 = b + 2 * i + 1, b1 = b + 2 * j, t1 = b + 2 * j + 1;
                // +Z 압출이라 바깥(반경) 노멀을 위해 b0→b1→t1→t0 와인딩
                t.Add(b0); t.Add(b1); t.Add(t1);
                t.Add(b0); t.Add(t1); t.Add(t0);
            }
            int cap = v.Count; v.Add(new Vector3(c.x, c.y, h));   // 윗 캡 중심(+Z 향함)
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                t.Add(cap); t.Add(b + 2 * i + 1); t.Add(b + 2 * j + 1);
            }
        }

        // 볼트 패턴 메시를 면(faceRot)에 부착 — 다크 강철. plateOffset만큼 면 바깥으로 띄움.
        static void Bolts(Transform parent, Vector3 pos, Quaternion faceRot, string key,
                          Vector2[] patt, float boltR, float plateOffset)
        {
            var mesh = GetBoltMesh(key, patt, boltR, boltR * 0.85f, 6);
            var go = new GameObject("Joint_Bolts");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = pos + faceRot * new Vector3(0f, 0f, plateOffset);
            go.transform.localRotation = faceRot;
            Colorize(go, CDark);
        }

        // 연결 플레이트(거싯) + 볼트 링 한 세트 — 면 노멀(faceRot)에 정렬.
        static void BoltedPlate(Transform parent, Vector3 pos, Quaternion faceRot,
                                float size, int nBolts, float ringR, float boltR)
        {
            float thick = 0.005f;
            Gusset(parent, pos, faceRot, size, thick, CStruct);
            Bolts(parent, pos, faceRot, $"ring{nBolts}_{ringR:F4}_{boltR:F4}",
                  BoltRing(nBolts, ringR), boltR, thick * 0.5f + 0.0004f);
        }

        // 주요 구조 접합부 연결판 + 볼트 — 다리 상·하단, 포털, 실빔/브레이스 절점(루트 로컬).
        static void BuildBoltedJoints(Transform root)
        {
            float halfZ = GaugeZ * 0.5f;
            float legHalf = LegSec * 1.7f * 0.5f;   // 격자 다리 외곽 반폭
            float[] legX = { LandLegX, WaterLegX };

            foreach (float x in legX)
            for (int s = -1; s <= 1; s += 2)
            {
                // 면 회전: +Z 외측 면=identity, -Z 외측 면=180°
                Quaternion fr = s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                float zOuter = s * (halfZ + legHalf + 0.0006f);   // 다리 바깥 ±Z 면

                // 1) 다리 상단(포털 크로스/A프레임/거더 수렴) — 큰 볼트 링 연결판
                BoltedPlate(root, new Vector3(x, LegTopY - 0.02f, zOuter),
                    fr, 0.026f, 8, 0.018f, 0.0024f);

                // 2) 실빔(Sill_Beam) ↔ 다리 절점(RailH*0.4)
                BoltedPlate(root, new Vector3(x, RailH * 0.4f, zOuter),
                    fr, 0.02f, 6, 0.013f, 0.0021f);

                // 3) 측면 대각 브레이스 상단 절점(RailH*0.92) — 두 X-브레이스가 각 다리 상단에서 만남
                BoltedPlate(root, new Vector3(x, RailH * 0.92f, zOuter),
                    fr, 0.017f, 6, 0.011f, 0.002f);

                // 4) 다리 베이스 받침판 앵커 볼트 그리드(위로 향함)
                Bolts(root, new Vector3(x, 0.073f, s * halfZ), Quaternion.Euler(-90, 0, 0),
                    "grid3x2", BoltGrid(3, 2, 0.016f, 0.012f), 0.0022f, 0.0004f);
            }
        }

        // 붐 거더 스플라이스 연결판 + 볼트열 — 박스 거더를 분절된 볼트 접합 세그먼트로(붐 로컬).
        static void BuildBoomSplices(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            const float gY = 0.04f, gH = 0.05f;
            float gTop = gY + gH * 0.5f;

            // 거더 길이를 3등분한 내부 스테이션 2곳에 스플라이스(과밀 방지)
            foreach (float f in new[] { 0.34f, 0.67f })
            {
                float sx = Mathf.Lerp(x0, x1, f);
                for (int s = -1; s <= 1; s += 2)
                {
                    float gz = s * GirderGapZ;
                    float zo = gz + s * (GirderWidthZ * 0.5f + 0.0015f);
                    // 측면 스플라이스 플레이트(거더 면을 덮는 가는 띠) + 세로 볼트열
                    Box(boom, "Girder_Splice", new Vector3(sx, gY, zo),
                        new Vector3(0.012f, gH + 0.006f, 0.004f), CStruct);
                    Bolts(boom, new Vector3(sx, gY, zo), Quaternion.identity,
                        "splice_col", BoltGrid(1, 4, 0f, 0.013f), 0.0016f, 0.0026f);
                }
                // 상부 플랜지 가로 스플라이스(두 거더를 잇는 덮개판) + 가로 볼트열
                Box(boom, "Girder_FlangeSplice", new Vector3(sx, gTop + 0.004f, 0f),
                    new Vector3(0.016f, 0.004f, 2f * GirderGapZ), CStruct);
                Bolts(boom, new Vector3(sx, gTop + 0.008f, 0f), Quaternion.Euler(-90, 0, 0),
                    "splice_row", BoltGrid(2, 5, 0.007f, 0.05f), 0.0015f, 0.0004f);
            }
        }

        // 모서리 베벨(챔퍼) 큐브를 사용 — 모든 Box/Strut가 공유 메시로 "마감된 느낌"
        static GameObject NewCube(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = GetBeveledCube();
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        // 12 모서리를 살짝 깎은 단위 큐브(챔퍼) 메시. 면 방향은 outward로 자동 보정, 면당 UV 0~1 유지.
        static Mesh GetBeveledCube()
        {
            if (_beveledCube != null) return _beveledCube;
            const float h = 0.5f, c = 0.06f;   // c=베벨 폭(살짝)
            float b = h - c;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };

            // 6 메인 면(축소된 사각형)
            for (int a = 0; a < 3; a++)
            for (int s = -1; s <= 1; s += 2)
            {
                int a1 = (a + 1) % 3, a2 = (a + 2) % 3;
                AddBevQuad(v, t, uv,
                    AxV(a, s * h, a1, -b, a2, -b), AxV(a, s * h, a1, b, a2, -b),
                    AxV(a, s * h, a1, b, a2, b),   AxV(a, s * h, a1, -b, a2, b), ax[a] * s);
            }
            // 12 모서리 챔퍼 면
            for (int a = 0; a < 3; a++)
            for (int s1 = -1; s1 <= 1; s1 += 2)
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                int a1 = (a + 1) % 3, a2 = (a + 2) % 3;
                AddBevQuad(v, t, uv,
                    AxV(a1, s1 * h, a2, s2 * b, a, -b), AxV(a1, s1 * h, a2, s2 * b, a, b),
                    AxV(a1, s1 * b, a2, s2 * h, a, b),  AxV(a1, s1 * b, a2, s2 * h, a, -b),
                    ax[a1] * s1 + ax[a2] * s2);
            }
            // 8 코너 삼각형
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                AddBevTri(v, t, uv,
                    new Vector3(sx * h, sy * b, sz * b), new Vector3(sx * b, sy * h, sz * b),
                    new Vector3(sx * b, sy * b, sz * h), new Vector3(sx, sy, sz));

            var m = new Mesh { name = "STS_BeveledCube" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return _beveledCube = m;
        }

        // 축 인덱스로 Vector3 구성
        static Vector3 AxV(int a, float va, int a1, float va1, int a2, float va2)
        { var p = Vector3.zero; p[a] = va; p[a1] = va1; p[a2] = va2; return p; }

        // 사각/삼각 추가 — outward 기준으로 와인딩 자동 보정(뒤집힘 방지) + UV 0~1
        static void AddBevQuad(List<Vector3> v, List<int> t, List<Vector2> uv,
                               Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            else
            { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        }

        static void AddBevTri(List<Vector3> v, List<int> t, List<Vector2> uv,
                              Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0.5f, 1));
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            { t.Add(i); t.Add(i + 2); t.Add(i + 1); }
            else
            { t.Add(i); t.Add(i + 1); t.Add(i + 2); }
        }

        // 늘어진 케이블(카테너리) — Unity Splines로 a→b 사이에 sag만큼 처지는 곡선 튜브 메시를 만들어 붙인다.
        //   knot을 포물선 근사(중앙 최대 처짐)로 깔고 SplineMesh.Extrude로 생성 시점에 메시를 굽는다(런타임 컴포넌트 불필요).
        //   a/b/sag는 parent 로컬 좌표·길이. 호이스트 로프(팽팽=직선)엔 안 쓰고, 처지는 전력/제어 케이블에만.
        static GameObject CableCatenary(Transform parent, string name, Vector3 a, Vector3 b,
                                        float sag, float radius, Color color,
                                        int knots = 5, int sides = 5, int segments = 18)
        {
            var spline = new UnityEngine.Splines.Spline();
            for (int i = 0; i < knots; i++)
            {
                float t = i / (float)(knots - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= 4f * sag * t * (1f - t);   // 포물선 처짐(중앙 최대) — 카테너리 근사
                spline.Add(new Unity.Mathematics.float3(p.x, p.y, p.z),
                           UnityEngine.Splines.TangentMode.AutoSmooth);
            }
            var mesh = new Mesh { name = name + "_Mesh" };
            UnityEngine.Splines.SplineMesh.Extrude(spline, mesh, radius, sides, segments);

            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color);
            return go;
        }

        // ProBuilder 편집형 박스 — CreatePrimitive 대신 ProBuilder 메시로 생성. 디자이너가 ProBuilderize 없이
        //   바로 ProBuilder(베벨·면 분할)·Polybrush(스컬프팅)로 다듬을 수 있다. (디자인 워크플로용 — 신규/재설계 부품에 사용)
        //   euler 주면 회전. 콜라이더는 시각 전용으로 제거(기존 부품과 통일).
        static GameObject PbBox(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, Vector3 euler = default, float bevel = 0f)
        {
            var pb = UnityEngine.ProBuilder.ShapeGenerator.GenerateCube(UnityEngine.ProBuilder.PivotLocation.Center, size);
            pb.name = name;
            pb.transform.SetParent(parent, worldPositionStays: false);
            pb.transform.localPosition = localPos;
            if (euler != Vector3.zero) pb.transform.localRotation = Quaternion.Euler(euler);
            // 베벨 비활성 — 전 모서리 일괄 베벨은 blind로 자기교차해 메시가 산산조각 남(Trolley_Gearbox 등 파편).
            //   깨끗한 ProBuilder 박스로 생성만 하고, 베벨/면다듬기는 디자이너가 ProBuilder 에디터에서 시각적으로 한다.
            //   (bevel 인자는 호출부 호환 위해 유지하되 무시. 안전한 자동 베벨이 필요하면 부품별 소량으로 재도입.)
            _ = bevel;
            pb.ToMesh();
            pb.Refresh();
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = GetMaterial(color);
            var col = pb.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            return pb.gameObject;
        }

        static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // 에디터 클릭 방해 줄이려 collider 제거(시각화 전용).
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        static void Colorize(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial = GetMaterial(c);
        }

        // 같은 색 머티리얼 재사용 — 프로젝트 머티리얼 오염 방지 위해 인스턴스(에셋 미저장).
        // 단색 평면 → 절차 강철 텍스처(albedo) + 카테고리별 PBR + 일부 에미시브.
        static Material GetMaterial(Color c)
        {
            if (_matCache != null && _matCache.TryGetValue(c, out var cached)) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "STS_Mat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);

            // 카테고리별 PBR + 텍스처 적용 여부
            float metallic = 0.30f, smooth = 0.35f;
            bool  useTex = true, emissive = false;
            float emi = 0f;

            if (Same(c, CWireTest))    { metallic = 0.0f;  smooth = 0.5f;  useTex = false; emissive = true; emi = 2.0f; } // ⚠️ 임시: 4단계 수정부 식별용 발광
            else if (Same(c, CGlass))  { metallic = 0.0f;  smooth = 0.92f; useTex = false; }              // 유리: 매끈
            else if (Same(c, CCable))  { metallic = 0.0f;  smooth = 0.15f; useTex = false; }              // 로프: 매트
            else if (Same(c, CLight))  { metallic = 0.0f;  smooth = 0.60f; useTex = false; emissive = true; emi = 1.8f; } // 작업등 렌즈: 발광
            else if (Same(c, CWarn))   { metallic = 0.0f;  smooth = 0.55f; emissive = true; emi = 1.1f; } // 경고/항공등: 약발광
            else if (Same(c, CDark))   { metallic = 0.20f; smooth = 0.25f; }                              // 다크 강철/고무
            else if (Same(c, CRail))   { metallic = 0.65f; smooth = 0.55f; }                              // 마모된 레일: 금속 광택
            else if (Same(c, CMachine)){ metallic = 0.35f; smooth = 0.30f; }                              // 기계실 도장
            // 그 외(CStruct/CBoom/CTrolley/CSpread): 칠한 구조 강철 기본값

            if (useTex)
            {
                var tex = GetSteelTexture();
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * emi);
            }

            _matCache?.Add(c, mat);
            return mat;
        }

        // 색 근사 비교(머티리얼 카테고리 분류용)
        static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        // 절차 생성 강철 디테일 텍스처 — 그레이스케일(평균≈0.9), 다중 옥타브 노이즈 + 세로 때 스트릭 + 미세 그레인.
        // _BaseColor가 곱해져 각 색의 "칠한 강철" 질감이 됨. 에셋으로 저장하지 않음.
        static Texture2D GetSteelTexture()
        {
            if (_steelTex != null) return _steelTex;

            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "STS_SteelDetail", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 다중 옥타브 노이즈(얼룩/때)
                float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.55f
                        + Mathf.PerlinNoise(x * 0.13f + 100f, y * 0.13f) * 0.30f
                        + Mathf.PerlinNoise(x * 0.40f + 50f,  y * 0.40f) * 0.15f;
                // 세로로 흘러내린 때 스트릭(상단일수록 옅게)
                float streak = Mathf.PerlinNoise(x * 0.6f, y * 0.015f);
                streak = Mathf.SmoothStep(0.62f, 1f, streak) * (y / (float)N) * 0.18f;
                // 미세 그레인
                float grain = (Hash(x, y) - 0.5f) * 0.05f;

                float v = Mathf.Clamp01(0.93f + (n - 0.5f) * 0.22f + grain - streak);
                byte b = (byte)(v * 255f);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _steelTex = tex;
        }

        // 결정적 정수 해시 → [0,1) (텍스처 그레인용)
        static float Hash(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
        }

        static Vector3 FindContainerAnchor()
        {
            // 1) 실제 컨테이너 인스턴스 우선(가장 정확)
            var instType = System.Type.GetType("ContainerProject.ContainerInstance, Assembly-CSharp");
            if (instType != null)
            {
                var inst = Object.FindFirstObjectByType(instType) as Component;
                if (inst != null) return inst.transform.position;
            }
            // 2) 스포너(보통 매니저 — 원점일 수 있음)
            var spawnerType = System.Type.GetType("ContainerProject.ContainerSpawner, Assembly-CSharp");
            if (spawnerType != null)
            {
                var spawner = Object.FindFirstObjectByType(spawnerType) as Component;
                if (spawner != null) return spawner.transform.position;
            }
            // 3) 이름으로
            var named = GameObject.Find("Container_Procedural");
            if (named != null) return named.transform.position;
            return Vector3.zero;
        }
    }
}
#endif
