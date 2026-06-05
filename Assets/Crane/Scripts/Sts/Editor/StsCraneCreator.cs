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
        static readonly Color CCable   = new Color(0.22f, 0.23f, 0.25f); // 와이어 로프(오일드 강철 회색 — 순흑X)
        static readonly Color CGlass   = new Color(0.25f, 0.55f, 0.70f); // 운전실 창
        static readonly Color CLight   = new Color(1.00f, 0.95f, 0.70f); // 작업등 렌즈
        static readonly Color CWarn    = new Color(0.90f, 0.10f, 0.10f); // 항공장애등(적색)
        // [감사 #13] 임시 테스트색 CWireTest 제거 — 4단계 와이어 작업 종료 후 실제 사용처 0건이던 발광 디버그색.

        const string RootName = "STS_Crane";

        // 같은 색은 머티리얼 1개를 재사용(빌드 1회 한정)
        static Dictionary<Color, Material> _matCache;
        // 모든 머티리얼이 공유하는 절차 생성 강철 디테일 텍스처(_BaseColor로 틴트)
        static Texture2D _steelTex;
        // 와이어 로프 전용 절차 텍스처 — 나선 strand 밴드(헬리컬 레이)
        static Texture2D _ropeTex;
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
            _ropeTex = null;
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

            // A-프레임 + 스테이 케이블 (루트 레벨)
            BuildApexAndStays(root.transform);
            BuildBoltedJoints(root.transform);    // 주요 구조 접합부 연결판 + 볼트(다리/포털/실빔/브레이스)

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
            // 호이스트 윗구간(트롤리 뒷면→백리치 고정 앵커) — BoomRopeRig가 트롤리 주행 시 신축
            BuildBoomHoistRopes(boom.transform, trolley.transform);

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

            PruneDeletedParts(root.transform);   // 삭제 지정 파츠 일괄 제거(번호 유지)

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
                    // 보기(주행 대차) — 벽돌 → 굴절 이퀄라이저 트럭: 중앙 피벗 1 → 서브 이퀄라이저 2 → 바퀴 4(끝으로 분산).
                    {
                        float bz = LegSec * 2.8f;                 // 보기 전장(Z)
                        float by = 0.05f;                          // 메인 이퀄라이저 빔 높이
                        var wheelC = new Color(0.05f, 0.05f, 0.06f);
                        // 메인 이퀄라이저 빔(다리 하단 중앙 피벗으로 매달림) + 피벗 핀(축 X)
                        Box(root, "Bogie_Equalizer", new Vector3(x, by, z),
                            new Vector3(LegSec * 0.5f, 0.014f, bz), CStruct);
                        Rod(root, "Bogie_Pivot",
                            new Vector3(x - LegSec * 0.55f, by, z), new Vector3(x + LegSec * 0.55f, by, z), 0.005f, CDark);
                        for (int sb = -1; sb <= 1; sb += 2)
                        {
                            float sbz = z + sb * bz * 0.25f;       // 서브 이퀄라이저 중심
                            Box(root, "Bogie_SubEqualizer", new Vector3(x, by - 0.016f, sbz),
                                new Vector3(LegSec * 0.42f, 0.011f, bz * 0.42f), CStruct);
                            Rod(root, "Bogie_SubPivot",            // 메인↔서브 피벗(축 X)
                                new Vector3(x - LegSec * 0.46f, by - 0.008f, sbz),
                                new Vector3(x + LegSec * 0.46f, by - 0.008f, sbz), 0.004f, CDark);
                            // 측면 프레임(바퀴 가드)
                            for (int fz = -1; fz <= 1; fz += 2)
                                Box(root, "Bogie_SideFrame", new Vector3(x + fz * LegSec * 0.5f, 0.024f, sbz),
                                    new Vector3(0.005f, 0.03f, bz * 0.5f), CStruct);
                            // 바퀴 2(서브 빔 양 끝) + 허브 보스 + 저널 박스
                            for (int w = -1; w <= 1; w += 2)
                            {
                                float wz = sbz + w * bz * 0.18f;
                                Rod(root, "Wheel",
                                    new Vector3(x - LegSec * 0.62f, 0.014f, wz),
                                    new Vector3(x + LegSec * 0.62f, 0.014f, wz), 0.013f, wheelC);
                                for (int hs = -1; hs <= 1; hs += 2)
                                    Rod(root, "Wheel_Hub",
                                        new Vector3(x + hs * LegSec * 0.5f, 0.014f, wz),
                                        new Vector3(x + hs * LegSec * 0.66f, 0.014f, wz), 0.006f, CDark);
                                Box(root, "Bogie_AxleBox", new Vector3(x, 0.024f, wz),
                                    new Vector3(LegSec * 0.5f, 0.012f, 0.01f), CMachine);
                            }
                        }
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

            // 측면 대각 브레이스(앞/뒤 다리 사이) — Sill_Beam(RailH*0.4)을 하현재로 그 위에 X 패턴.
            //   [디자인] 민짜 막대 → 빌트업(웹+플랜지) 부재 + 절점 거싯판·볼트로 퀄리티 보강.
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * halfZ;
                BuiltUpBrace(root, "Brace",
                    new Vector3(LandLegX, RailH * 0.4f, z), new Vector3(WaterLegX, RailH * 0.92f, z), CStruct);
                BuiltUpBrace(root, "Brace",
                    new Vector3(WaterLegX, RailH * 0.4f, z), new Vector3(LandLegX, RailH * 0.92f, z), CStruct);
                // 중앙 X 교차 절점 거싯 + 볼트 링 (다리 단부 절점 RailH*0.4·0.92 는 BuildBoltedJoints가 이미 처리 → 중복 회피).
                Quaternion fr = s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                float midX = (LandLegX + WaterLegX) * 0.5f;
                BoltedPlate(root, new Vector3(midX, RailH * 0.66f, z), fr, 0.044f, 4, 4, 0.016f, 0.016f, 0.0022f);  // 포털 중간 4×4=16
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
            float condX = LandLegX - 0.034f;   // 다리 밖(-X)으로 — 다리 -X면 럼/래이싱(x[-0.025,-0.017])에 안 박히게(클램프로 다리에 부착)
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
            EquipmentUnit(boom, "MH_AC_Unit", new Vector3(mhx - 0.055f, 0.185f, 0f),   // 0.18→0.185: 지붕 윗면(0.171)에 안착(파묻힘 해소)
                new Vector3(0.05f, 0.028f, 0.05f), CDark);
            // 기계실 추가 디테일 — 배기구·창·보조 E-house·케이블 트레이·붐호이스트 윈치
            Rod(boom, "MH_Exhaust", new Vector3(mhx + 0.035f, 0.171f, -0.025f),       // z 0.02→-0.025: HVAC2(+Z) 관통 해소
                new Vector3(mhx + 0.035f, 0.215f, -0.025f), 0.006f, CDark);
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
            // 붐호이스트 윈치 — 기계실 내부 공동(바닥 0.05~지붕 0.16)에 완전 수납. 드럼+플랜지+감속기/모터+디스크브레이크+베드+베어링.
            {
                float wy = 0.095f, wx = mhx - 0.02f;     // 기계실 안쪽(노출 X)
                float wHalfZ = 0.09f;                     // 드럼 반폭
                Rod(boom, "Boom_Hoist_Drum",              // 로프 스풀 드럼
                    new Vector3(wx, wy, -wHalfZ), new Vector3(wx, wy, wHalfZ), 0.026f, CMachine);
                for (int s = -1; s <= 1; s += 2)          // 드럼 양끝 플랜지
                    Rod(boom, "Drum_Flange",
                        new Vector3(wx, wy, s * wHalfZ), new Vector3(wx, wy, s * (wHalfZ + 0.006f)), 0.032f, CDark);
                Rod(boom, "Hoist_Gearbox",                // 감속기(드럼 -Z 직결)
                    new Vector3(wx, wy, -wHalfZ - 0.006f), new Vector3(wx, wy, -wHalfZ - 0.016f), 0.03f, CMachine);
                Rod(boom, "Hoist_Motor",                  // 구동 모터
                    new Vector3(wx, wy, -wHalfZ - 0.016f), new Vector3(wx, wy, -wHalfZ - 0.055f), 0.02f, CDark);
                Rod(boom, "Hoist_Brake",                  // 디스크 브레이크(드럼 +Z)
                    new Vector3(wx, wy, wHalfZ + 0.008f), new Vector3(wx, wy, wHalfZ + 0.016f), 0.034f, CStruct);
                Box(boom, "Hoist_Bedplate", new Vector3(wx, 0.058f, 0f),   // 베드플레이트(바닥 위 받침)
                    new Vector3(0.07f, 0.01f, 0.24f), CStruct);
                for (int s = -1; s <= 1; s += 2)          // 베어링 페디스털(드럼축 받침)
                    Box(boom, "Drum_Pedestal", new Vector3(wx, 0.075f, s * (wHalfZ - 0.01f)),
                        new Vector3(0.03f, 0.04f, 0.014f), CMachine);
            }
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

            // 다리 도관 → 기계실 정션박스(MH_JBox) 연결 — '붐 거더 선'을 따라 라우팅해 거더가 밑을 받치게(안 뜸).
            //   이전엔 수평 구간이 z=다리(0.333)로 가서 붐에서 떨어진 허공에 떴음 → 거더 z(≈0.172, 거더 폭 안)로 옮김.
            //   엘보 박스도 거더 위에 마운트해 복원(삭제 아님 — 재설계).
            {
                float condX2 = LandLegX - 0.034f;          // 다리 도관 x (다리 밖 — 기둥/래이싱 관통 회피, condX와 정렬)
                float legZ   = GaugeZ * 0.5f;               // 0.333 (다리 z)
                float gz     = GirderOuterZ - 0.01f;        // ≈0.172 — 붐 거더 폭 안(케이블 트레이 z0.16 바깥), 거더가 받침
                float runY   = 0.05f;                       // 붐 상부 케이블 트레이 높이(MH 바닥 0.067 아래라 안 겹침)
                float jbX = mhx - 0.07f, jbY = 0.06f, jbZ = mhFZ;   // MH_JBox
                float r   = 0.005f;
                Rod(boom, "Leg_Conduit_Link", new Vector3(condX2, -0.005f, legZ), new Vector3(condX2, runY, legZ), r, CDark); // 다리 따라 붐 상면까지 상승
                Rod(boom, "Leg_Conduit_Link", new Vector3(condX2, runY, legZ),    new Vector3(condX2, runY, gz),   r, CDark); // 다리 z→거더 z 인입(짧은 횡단)
                Rod(boom, "Leg_Conduit_Link", new Vector3(condX2, runY, gz),      new Vector3(jbX,    runY, gz),   r, CDark); // 거더 선 따라 -X (거더가 밑을 받침 → 안 뜸)
                Rod(boom, "Leg_Conduit_Link", new Vector3(jbX,    runY, gz),      new Vector3(jbX,    runY, jbZ),  r, CDark); // 거더→기계실 벽
                Rod(boom, "Leg_Conduit_Link", new Vector3(jbX,    runY, jbZ),     new Vector3(jbX,    jbY,  jbZ),  r, CDark); // JBox로 상승
                Box(boom, "Conduit_Elbow_JBox", new Vector3(condX2, runY, legZ),  new Vector3(0.018f, 0.02f, 0.018f), CMachine);  // 수직→수평 굽힘부(다리쪽) 엘보 박스 ← 모서리 큐브
                Box(boom, "Conduit_Elbow_JBox", new Vector3(condX2, runY, gz),    new Vector3(0.022f, 0.022f, 0.022f), CMachine); // 거더쪽 엘보 정션박스
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
            EquipmentUnit(boom, "MH_HVAC2", new Vector3(mhx + 0.03f, mhRoofY + 0.011f, 0.025f),   // 지붕 윗면 안착
                new Vector3(0.04f, 0.022f, 0.03f), CDark);
            Ball(boom, "MH_ExhaustCap", new Vector3(mhx + 0.035f, 0.218f, -0.025f),              // 배기 스택 따라 -Z로
                new Vector3(0.012f, 0.008f, 0.012f), CDark);
            // 지붕 접근 수직 사다리(바다쪽 +Z면 → 지붕)
            BuildLadder(boom, mhx + 0.07f, mhFZ + 0.008f, 0.06f, mhRoofY, 0.02f);
            // 지붕 사다리 스탠드오프 브래킷 — 벽(z=mhFZ)↔사다리(z≈mhFZ+0.008) 고정(6mm 공중부양 제거).
            for (int li = 0; li < 3; li++)
                Box(boom, "Ladder_Bracket",
                    new Vector3(mhx + 0.07f, Mathf.Lerp(0.085f, mhRoofY - 0.02f, li / 2f), mhFZ + 0.004f),
                    new Vector3(0.02f, 0.006f, 0.012f), CStruct);

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
            // 데크 받침 스터브 — 데크 밑면(platY-0.004)을 정상 가로보 윗면(apex.y+0.02)에 받침(6mm 공중부양 제거).
            for (int psz = -1; psz <= 1; psz += 2)
                Box(root, "Apex_PlatformStub", new Vector3(apex.x, apex.y + 0.023f, psz * halfPZ * 0.6f),
                    new Vector3(halfX * 1.6f, 0.006f, 0.006f), CStruct);

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
                // 다리 상단 결합부(육지/바다 다리) — 다리 격자 외곽면(halfZ + legHalf)에 부착(파묻힘 방지)
                float legHalf = LegSec * 1.7f * 0.5f;
                foreach (float lx in new[] { LandLegX, WaterLegX })
                    Gusset(root, new Vector3(lx, LegTopY + 0.012f, s * (halfZ + legHalf) + s * afOff), fr, afG, afT, CStruct);
            }

            // 정상 시브 네스트(스테이 도르래) + 장비 하우징 — 시브를 감싸게 거더 폭에 맞춤
            Box(root, "Apex_SheaveHouse", new Vector3(apex.x, apex.y - 0.025f, 0f),
                new Vector3(0.03f, 0.05f, 2f * GirderGapZ + 0.08f), CMachine);
            // 정상 시브 — 양쪽(z=±GirderGapZ)에 1개씩, 각 거더 포어스테이를 받음(축은 Z)
            for (int s = -1; s <= 1; s += 2)
            {
                float sz = s * GirderGapZ;
                Sheave(root, "Apex_Sheave",   // 민짜 드럼 → V홈 시브(스테이 로프가 도는 도르래)
                    new Vector3(apex.x, apex.y - 0.01f, sz - 0.016f),
                    new Vector3(apex.x, apex.y - 0.01f, sz + 0.016f), 0.016f, 0.006f, 0.004f, CDark);
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

            // 백스테이 — 정상 시브 하우스(z=±GirderGapZ)에서 거더 맨뒤(이퀄라이저 빔)로 가는 '주 백스테이'.
            //   ※ 앞쪽 줄(bsFrontX, 기계실 앞)은 짧고 높아 육지측 A-프레임 X-브레이스(Aframe_Lace)를
            //     정상부 바로 아래(x≈0.54)에서 관통 → 제거. (백스테이는 시브 z=±0.16에 물려 z 회피 불가,
            //     lace 대각이 모든 z를 훑어 충돌 불가피 → 짧은 앞 줄을 뺌. 뒤 줄은 가팔라 통과함.)
            float bsBackX = BoomBackX + 0.02f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 shTop = new Vector3(apex.x, apex.y - 0.01f, s * GirderGapZ);   // 시브 하우스 시브 위치
                BuildStay(root, shTop, new Vector3(bsBackX, boomTopY, s * GirderGapZ), "Backstay");
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
            // 주행 대차(보기) + 바퀴 — 각 거더 레일(z=±GirderGapZ) 밑면에 트레드 접지.
            // [감사 #3] 거더 밑면(y=0.015)이 레일 윗면(0.012) 바로 위라 위로 올리면 거더와 충돌 → 언더러닝 구성.
            //   기존 바퀴 중심 0.001(반경 0.011)은 레일(y0~0.012) 속에 파묻혔음 → 어셈블리를 0.012 내려 바퀴 윗면이 레일 밑면(y=0)에 접하게(중심 -0.011).
            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * GirderGapZ;
                PbBox(trolley, "Trolley_Bogie", new Vector3(0f, -0.018f, gz),
                    new Vector3(0.13f, 0.016f, 0.03f), CDark, bevel: 0.25f);
                foreach (float wx in new[] { -0.05f, -0.018f, 0.018f, 0.05f })   // 보기당 4륜(디테일)
                    Rod(trolley, "Trolley_Wheel",
                        new Vector3(wx, -0.011f, gz - 0.013f),
                        new Vector3(wx, -0.011f, gz + 0.013f), 0.011f, CRail);
            }
            // [디자인] 트롤리 리빙 시브 4개(falls별) — 홈 파인 휠 + 사각 치크 측판(둥근 코인 글리치 → 측판이 본체 위로 솟아 시브 프레이밍) + 축 핀.
            //   각 로프가 이 시브를 감아 넘어: 윗구간은 기계실(-X)로, 아랫구간(Hoist_Rope)은 헤드블록으로 강하.
            foreach (float tx in new[] { -0.03f, 0.03f })
            foreach (float tz in new[] { -0.05f, 0.05f })
            {
                Vector3 c = new Vector3(tx, -0.005f, tz);
                for (int e = -1; e <= 1; e += 2)   // 사각 치크 측판(본체 위로 솟아 클리핑 가림)
                    Box(trolley, "Sheave_Cheek", new Vector3(tx, -0.003f, tz + e * 0.0095f),
                        new Vector3(0.032f, 0.032f, 0.003f), CStruct);
                Sheave(trolley, "Trolley_Sheave",
                    c + new Vector3(0f, 0f, -0.004f), c + new Vector3(0f, 0f, 0.004f), 0.014f, 0.005f, 0.004f, CRail);
                Rod(trolley, "Sheave_Pin",         // 관통 축 핀(치크 밖으로 돌출)
                    new Vector3(tx, -0.005f, tz - 0.013f), new Vector3(tx, -0.005f, tz + 0.013f), 0.0035f, CDark);
            }
            // ── 로프 데드엔드 소켓 (와이어 연결부, 육지쪽 인입 정착) ──
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

            // 호이스트 윗구간(뒷면→백리치 앵커)은 BuildBoomHoistRopes에서 동적(BoomRopeRig)으로 생성.
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
            // ============================================================================
            //  OPERATOR CAB — 처음부터 새로(기존 폐기). SOLID & SEALED by construction:
            //  닫힌 하부 박스 + 사방 솔리드 프레임(4기둥+레일)으로 둘러싸고 유리는 '채움'만 →
            //  뚫린 틈·떠다니는 부품이 구조적으로 불가능. 그레이 몸체 + 슬림 오렌지 액센트 + 전방 바이저.
            //  전부 x<0(스프레더 뒤), 지붕 top < 헤드 하단(-0.0725).
            // ============================================================================
            float hx = 0.034f, hz = 0.040f, cx = -0.078f;       // 반깊이/반폭, 중심 X
            float roofY = -0.077f, floorY = -0.152f;            // 지붕 top=-0.074<-0.0725; 높이 0.075(~1.8m)
            float h = roofY - floorY, midY = (floorY + roofY) * 0.5f;
            float frontX = cx + hx, backX = cx - hx;            // 전 -0.044, 후 -0.112 (둘 다 <0)
            float sillY = floorY + h * 0.42f;                   // 창 밴드 하단
            float hdrY  = roofY  - h * 0.12f;                   // 창 밴드 상단
            Color frame = CDark, bodyC = CStruct, accent = CTrolley;   // 몸체=밝은 강철그레이(검은 박스 탈피)

            // ---- 솔리드 하부 박스(floor->sill): 닫힌 박스 1개 = 밀폐 베이스 ----
            float lwY = (floorY + sillY) * 0.5f, lwH = sillY - floorY;
            PbBox(trolley, "Cab_LowerBox", new Vector3(cx, lwY, 0f), new Vector3(2f * hx, lwH, 2f * hz), bodyC);
            PbBox(trolley, "Cab_Kick", new Vector3(cx, floorY + lwH * 0.14f, 0f), new Vector3(2f * hx + 0.004f, lwH * 0.30f, 2f * hz + 0.004f), frame);  // 다크 토킥
            PbBox(trolley, "Cab_Waistline", new Vector3(cx, sillY - 0.003f, 0f), new Vector3(2f * hx + 0.004f, 0.005f, 2f * hz + 0.004f), accent);      // 오렌지 허리선
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(trolley, "Cab_SideAccent", new Vector3(cx, floorY + lwH * 0.62f, sz * (hz + 0.0015f)), new Vector3(2f * hx * 0.9f, lwH * 0.4f, 0.002f), accent);

            // ---- 창 밴드(sill->header): 솔리드 프레임(4기둥+상단레일) + 유리 채움 ----
            float ubY = (sillY + hdrY) * 0.5f, ubH = hdrY - sillY;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(trolley, "Cab_Post", new Vector3(cx + sx * hx, ubY, sz * hz), new Vector3(0.007f, ubH + 0.006f, 0.007f), frame);  // 4 솔리드 코너 기둥(수직 봉인)
            PbBox(trolley, "Cab_HeaderRail", new Vector3(cx, hdrY, 0f), new Vector3(2f * hx + 0.004f, 0.005f, 2f * hz + 0.004f), frame);  // 상단 레일(밴드 상단 봉인)
            // 유리(전 + 양측 + 후) — 거의 full폭이라 가장자리가 기둥 밑으로 들어가 봉인
            PbBox(trolley, "Cab_GlassFront", new Vector3(frontX, ubY, 0f), new Vector3(0.002f, ubH, 2f * hz * 0.94f), CGlass);
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(trolley, "Cab_GlassSide", new Vector3(cx, ubY, sz * hz), new Vector3(2f * hx * 0.94f, ubH, 0.002f), CGlass);
            // 솔리드 뒷벽 — 밴드 개구부를 꽉 채워(폭 2hz=기둥까지, 높이 sill~header) 봉인. 떠 보이던 작은 유리 교체.
            PbBox(trolley, "Cab_BackWall", new Vector3(backX, ubY, 0f), new Vector3(0.004f, ubH + 0.004f, 2f * hz), bodyC);
            PbBox(trolley, "Cab_BackWindow", new Vector3(backX - 0.0006f, ubY + ubH * 0.16f, 0f), new Vector3(0.002f, ubH * 0.42f, 2f * hz * 0.46f), CGlass);   // 뒷벽 소창(솔리드 벽에 인셋 → 안 떠다님)
            for (int m = -1; m <= 1; m += 2)                                                       // 슬림 멀리언(전면 2)
                PbBox(trolley, "Cab_MullF", new Vector3(frontX + 0.0006f, ubY, m * hz * 0.42f), new Vector3(0.003f, ubH, 0.003f), frame);
            for (int sz = -1; sz <= 1; sz += 2)                                                    // 측면 멀리언 1씩
                PbBox(trolley, "Cab_MullS", new Vector3(cx, ubY, sz * (hz + 0.0006f)), new Vector3(0.003f, ubH, 0.003f), frame);

            // ---- 솔리드 헤더 + 지붕(상단 봉인, 지붕 오버행 < 헤드) ----
            float hbY = (hdrY + roofY) * 0.5f, hbH = roofY - hdrY;
            PbBox(trolley, "Cab_HeaderBox", new Vector3(cx, hbY, 0f), new Vector3(2f * hx, hbH, 2f * hz), bodyC);
            PbBox(trolley, "Cab_Roof", new Vector3(cx, roofY, 0f), new Vector3(2f * hx + 0.014f, 0.006f, 2f * hz + 0.014f), frame);  // top -0.074
            PbBox(trolley, "Cab_Visor", new Vector3(frontX + 0.009f, roofY - 0.0015f, 0f), new Vector3(0.020f, 0.004f, 2f * hz + 0.008f), frame);  // 전방 바이저

            // ---- 현수: 지붕 모서리 -> 트롤리 본체 하단(y=-0.05, 본체 발자국 안) ----
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Strut(trolley, "Cab_Hanger", new Vector3(frontX, roofY, sz * hz * 0.6f), new Vector3(-0.045f, -0.05f, sz * hz * 0.45f), 0.004f, CStruct);
                Strut(trolley, "Cab_Hanger", new Vector3(backX,  roofY, sz * hz * 0.6f), new Vector3(-0.055f, -0.05f, sz * hz * 0.45f), 0.004f, CStruct);
            }

            // ---- 내부(좌석+콘솔+조이스틱+모니터) — 유리 너머로 보임 ----
            float seatX = cx + 0.004f, seatY = floorY + 0.014f;
            PbBox(trolley, "Cab_SeatPedestal", new Vector3(seatX - 0.002f, floorY + 0.007f, 0f), new Vector3(0.008f, 0.014f, 0.010f), bodyC);
            PbBox(trolley, "Cab_Seat",      new Vector3(seatX, seatY, 0f),                   new Vector3(0.016f, 0.006f, 0.018f), frame);
            PbBox(trolley, "Cab_SeatBack",  new Vector3(seatX - 0.010f, seatY + 0.016f, 0f), new Vector3(0.005f, 0.028f, 0.018f), frame);
            PbBox(trolley, "Cab_HeadRest",  new Vector3(seatX - 0.009f, seatY + 0.034f, 0f), new Vector3(0.005f, 0.008f, 0.012f), frame);
            for (int sz = -1; sz <= 1; sz += 2)
            {
                PbBox(trolley, "Cab_ArmRest", new Vector3(seatX + 0.002f, seatY + 0.010f, sz * 0.011f), new Vector3(0.014f, 0.003f, 0.004f), frame);
                PbBox(trolley, "Cab_Console", new Vector3(seatX + 0.012f, seatY + 0.006f, sz * 0.014f), new Vector3(0.014f, 0.007f, 0.007f), bodyC);
                Rod(trolley,  "Cab_Joystick", new Vector3(seatX + 0.014f, seatY + 0.010f, sz * 0.014f), new Vector3(seatX + 0.015f, seatY + 0.020f, sz * 0.014f), 0.0015f, frame);
                Ball(trolley, "Cab_JoystickKnob", new Vector3(seatX + 0.015f, seatY + 0.0205f, sz * 0.014f), new Vector3(0.0038f, 0.0038f, 0.0038f), frame);
            }
            PbBox(trolley, "Cab_Monitor",       new Vector3(seatX + 0.020f, seatY + 0.016f, 0f), new Vector3(0.004f, 0.010f, 0.013f), bodyC);
            PbBox(trolley, "Cab_MonitorScreen", new Vector3(seatX + 0.0222f, seatY + 0.016f, 0f), new Vector3(0.001f, 0.008f, 0.011f), CLight);

            // ---- 외부: 노즈 하부 작업등 + 후면 안테나 ----
            for (int i = -1; i <= 1; i++)
            {
                PbBox(trolley, "Cab_FloodHousing", new Vector3(frontX - 0.002f, floorY - 0.002f, i * 0.022f), new Vector3(0.008f, 0.006f, 0.012f), bodyC);
                Ball(trolley,  "Cab_Floodlight",   new Vector3(frontX - 0.003f, floorY - 0.005f, i * 0.022f), new Vector3(0.009f, 0.006f, 0.009f), CLight);
            }
            float antX = backX, antZ = 0.020f;
            PbBox(trolley, "Cab_AntennaBase", new Vector3(antX, roofY + 0.003f, antZ), new Vector3(0.008f, 0.005f, 0.008f), frame);
            Rod(trolley, "Cab_Antenna", new Vector3(antX, roofY + 0.005f, antZ), new Vector3(antX, roofY + 0.030f, antZ), 0.0022f, CStruct);
            Ball(trolley, "Cab_AntennaTip", new Vector3(antX, roofY + 0.031f, antZ), new Vector3(0.0035f, 0.0035f, 0.0035f), CWarn);
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

            // ── 헤드블록(중앙) — 크로스 프레임 + 데드엔드 로프 소켓 4 (시브 없는 dead-end형) ──
            float hbY = 0.058f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Strut(spreader, "Head_Frame",
                    new Vector3(sx * 0.06f, 0.016f, sz * 0.038f),
                    new Vector3(sx * 0.035f, hbY - 0.012f, sz * 0.02f), 0.006f, CSpread);
            // [디자인] 헤드블록을 키워 데드엔드 소켓 4(로컬 x±0.05, z±0.03 + 콘 베이스 0.0085)가 모서리 밖으로 안 삐지게.
            //   반폭 x 0.065(>0.0585), z 0.045(>0.0385) → 각 변 ~0.006 여유.
            Box(spreader, "Spreader_Head", new Vector3(0f, hbY, 0f),
                new Vector3(0.13f, 0.026f, 0.09f), CSpread);
            // [디자인 재설계] 헤드블록 시브/치크/핀 제거 → STS 데드엔드형.
            //   호이스트 로프 4가닥(x±0.03, z±0.05)이 시브를 안 거치고 곧장 내려와 헤드블록 상단에 정착(spelter socket dead-end).
            //   리빙/도르래는 트롤리 쪽에만 둠 — 로프가 안 감기는 헤드블록 시브는 비기능 장식이라 제거.
            // 스프레더가 Y축 90° 회전(line 1163)이라, 로프(부모공간 x±0.03,z±0.05)와 맞추려면 스프레더-로컬은 x±0.05,z±0.03 (xz 스왑 보정).
            foreach (float rx in new[] { -0.05f, 0.05f })
            foreach (float rz in new[] { -0.03f, 0.03f })
            {
                Vector3 sk = new Vector3(rx, hbY + 0.013f, rz);   // 헤드블록 상단면(0.071), 회전 보정해 로프 바로 아래
                // 개방형 스펠터 소켓 — 테이퍼 단조 바디(밑 넓고 위 좁아 로프 인입) + 칼라 밴드 + 베이스 클레비스 핀.
                Cone(spreader, "Head_Rope_Socket",
                    sk + new Vector3(0f, -0.002f, 0f), sk + new Vector3(0f, 0.012f, 0f), 0.0085f, 0.0045f, CStruct);
                Rod(spreader, "Head_Rope_Collar",                 // 넥 칼라 밴드(단조 디테일)
                    sk + new Vector3(0f, 0.008f, 0f), sk + new Vector3(0f, 0.011f, 0f), 0.0055f, CMachine);
                Rod(spreader, "Head_Rope_Pin",                    // 클레비스 핀(베이스 관통, 양옆 돌출)
                    sk + new Vector3(0f, 0.001f, -0.011f), sk + new Vector3(0f, 0.001f, 0.011f), 0.0022f, CDark);
            }

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
                var arm = new GameObject(Numbered(sx < 0 ? "TeleArm_L" : "TeleArm_R"));
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
                    // [감사 #10] 트위스트락 Z를 ISO 코너캐스팅 폭(2.438/24/2≈0.0508)에 정렬. 기존 0.044는 0.0068 안쪽으로 빗나감.
                    const float isoCornerHalfZ = 0.0508f;
                    Vector3 c = new Vector3(-sx * 0.006f, 0f, sz * isoCornerHalfZ);
                    TwistlockHead(a, c, CMetal);
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

        // 트위스트락 하우징 — 컨테이너 코너캐스팅에 안착하는 락 헤드.
        //   'Twistlock_Head'(빈 그룹)를 코너 수직축에 두고, 그 아래로 본체→숄더→가이드 플랜지를
        //   3단으로 넓혀 깔때기형 랜딩 가이드 실루엣을 만든다(콘이 플랜지 밑으로 돌출).
        //   ★ 전 부재가 코너 수직축 기준 4회대칭(정사각) → SpreaderLockAnimator의 90° 트위스트가
        //     자기복귀(시각 변화 없음)로 적용돼 하우징이 어색하게 돌지 않는다(콘과 동일 회전축).
        //   ★ 끝빔 밑면 y=-0.015 위는 빔에 묻혀 안 보이므로 디테일은 그 아래(y<-0.015)에 집중.
        static void TwistlockHead(Transform arm, Vector3 corner, Color metal)
        {
            var head = new GameObject(Numbered("Twistlock_Head"));
            head.transform.SetParent(arm, worldPositionStays: false);
            head.transform.localPosition = corner;       // 콘과 동일한 코너 수직축 = 트위스트 회전축
            Transform h = head.transform;

            // 단조 본체 — 끝빔에 묻혀 장착(상단은 빔 속, 하단만 노출)
            PbBox(h, "Twistlock_Body",     new Vector3(0f, -0.012f,  0f), new Vector3(0.018f, 0.026f, 0.018f), metal);
            // 머시닝 숄더 — 본체와 플랜지 사이 체결 밴드(스텝 디테일)
            PbBox(h, "Twistlock_Shoulder", new Vector3(0f, -0.0205f, 0f), new Vector3(0.021f, 0.004f, 0.021f), CMachine);
            // 랜딩 가이드 플랜지 — 코너캐스팅 상면에 안착하는 최하단 깔때기 립(가장 넓음)
            PbBox(h, "Twistlock_Guide",    new Vector3(0f, -0.0245f, 0f), new Vector3(0.024f, 0.005f, 0.024f), CStruct);
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

        // 호이스트 윗구간(트롤리 뒷면 → 백리치 고정 앵커) — 동적(BoomRopeRig).
        //   [재설계: 뒷면+백리치] 고정 앵커를 백리치 끝(Stay_Anchor 부근 x≈-0.537)에 둠 — 트롤리 backmost(-0.345)보다
        //   더 뒤라, 트롤리가 어디 있든 케이블이 항상 뒤로 향함(기계실 앞면이면 트롤리가 지나쳐 버려 NG).
        //   경로는 트롤리 뒷면(x-0.062)에서 곧장 -X, 붐 밑(y-0.02)으로 주행 → 거더/brace/cross(전부 y0.015 위)·본체 회피.
        //   시브↔뒷면 reeving은 트롤리 내부라 암시(페어리드까지만). z측당 1줄.
        static void BuildBoomHoistRopes(Transform boom, Transform trolley)
        {
            const int segPer = 6;
            const float radius = 0.0035f;
            const float sagFactor = 0.02f;
            const float laneY = -0.02f;    // 붐 밑(거더 밑면 0.015 아래)
            float backX = BoomBackX + 0.02f;   // 백리치 고정 앵커 X(Stay_Anchor 부근)

            var trolleyLocal = new List<Vector3>();
            var backAnchor   = new List<Vector3>();
            var segs         = new List<Transform>();

            foreach (float zs in new[] { -1f, 1f })
            {
                float z = zs * 0.05f;
                // 트롤리 뒷면 페어리드(접속 가이드, 트롤리 부착) — 동적 로프 트롤리 끝
                Box(trolley, "Hoist_BackFairlead", new Vector3(-0.058f, -0.025f, z),
                    new Vector3(0.012f, 0.014f, 0.012f), CStruct);
                trolleyLocal.Add(new Vector3(-0.062f, -0.025f, z));
                // 백리치 고정 앵커(boom 로컬) — 동적 로프 뒤끝
                backAnchor.Add(new Vector3(backX, laneY, z));

                for (int k = 0; k < segPer; k++)
                {
                    var seg = NewPrimitive(PrimitiveType.Cylinder, "Hoist_Rope_ToMH", boom);
                    Colorize(seg, CCable);
                    segs.Add(seg.transform);
                }
            }

            // 백리치 앵커 빔 — 두 케이블(z±0.05) 뒤끝이 정착하는 횡빔 + 거더(z±0.16)에 매다는 행어 → 케이블 공중부양 해소.
            Box(boom, "Hoist_BackAnchor", new Vector3(backX, laneY, 0f),
                new Vector3(0.016f, 0.014f, 0.13f), CStruct);
            for (int zs = -1; zs <= 1; zs += 2)
                Strut(boom, "Hoist_BackAnchor_Hanger",
                    new Vector3(backX, laneY, zs * 0.055f), new Vector3(backX + 0.01f, 0.018f, zs * GirderGapZ), 0.004f, CStruct);

            // [C] 백리치 앵커 → 기계실 드럼: 케이블이 기계실 뒷벽의 '로프 진입 포트'로 들어가 드럼에 감김.
            //   진입 y0.08 = MH_Sill(y0.047~0.055) 위 → sill 관통 회피. 그냥 벽 관통 아니라 보스+둘레판으로 디자인.
            float drumX = MachineryHouseX - 0.02f, drumY = 0.085f;
            float entryX = MachineryHouseX - MachineryHouseHX, entryY = 0.08f;   // 기계실 뒷벽
            foreach (float zs in new[] { -1f, 1f })
            {
                float z = zs * 0.05f;
                // 로프 진입 포트(디자인) — 뒷벽 둘레판 + 돌출 보스(로프가 통과하는 페어리드)
                Box(boom, "MH_RopeEntry_Plate", new Vector3(entryX - 0.002f, entryY, z), new Vector3(0.004f, 0.026f, 0.026f), CStruct);
                Box(boom, "MH_RopeEntry", new Vector3(entryX, entryY, z), new Vector3(0.014f, 0.016f, 0.016f), CMachine);
                // 백리치 앵커 → 진입 포트(상승, sill 위로)
                CableCatenary(boom, "Hoist_Rope_ToDrum", new Vector3(backX, laneY, z), new Vector3(entryX, entryY, z), 0.012f, radius, CCable);
                // 진입 포트 → 드럼(기계실 안, 벽에 가려짐)
                CableCatenary(boom, "Hoist_Rope_ToDrum", new Vector3(entryX, entryY, z), new Vector3(drumX, drumY, z), 0.004f, radius, CCable);
            }

            var rig = boom.gameObject.AddComponent<BoomRopeRig>();
            rig.Configure(trolley, trolleyLocal.ToArray(), backAnchor.ToArray(),
                          segs.ToArray(), segPer, radius, sagFactor);
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
            float gSize = postT * 0.85f;          // 코너 포스트보다 약간 큰 정도(삐져나옴 방지)
            float gThick = postT * 0.22f;
            float gOff = gThick * 0.55f;          // 면에서 살짝 띄워 z-fighting 방지
            // 거싯 중심을 모서리 안쪽으로 당겨, 바깥 모서리를 코너 포스트 외곽면(half+postT/2)에 맞춤 → 다리 envelope 밖으로 안 나감
            float gCorner = half + postT * 0.5f - gSize;
            for (int i = 2; i < segs; i += 3)     // 내부 노드 일부(과밀 방지)
            {
                float y = y0 + step * i;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    // ±Z 면(코너 x=cx±half) — X로 안쪽 당김
                    Gusset(root, new Vector3(cx + sx * gCorner, y, cz + half + gOff),
                        Quaternion.identity,        gSize, gThick, CStruct);
                    Gusset(root, new Vector3(cx + sx * gCorner, y, cz - half - gOff),
                        Quaternion.Euler(0, 180, 0), gSize, gThick, CStruct);
                    // ±X 면(코너 z=cz±half) — Z로 안쪽 당김
                    Gusset(root, new Vector3(cx + half + gOff, y, cz + sx * gCorner),
                        Quaternion.Euler(0, 90, 0),  gSize, gThick, CStruct);
                    Gusset(root, new Vector3(cx - half - gOff, y, cz + sx * gCorner),
                        Quaternion.Euler(0, -90, 0), gSize, gThick, CStruct);
                }
            }

            // 갠트리 주행 시 레일 위 장애물(컨테이너 등)과 충돌하도록 다리 하나를 감싸는 BoxCollider 1개.
            //   격자 부재(Leg_Post/Rung/Lace)는 전부 시각용이라 콜라이더가 없다 → 다리를 통째로 물리화.
            //   (크레인 루트의 kinematic Rigidbody가 이 콜라이더로 dynamic 컨테이너를 밀어낸다.)
            var legCol = new GameObject(Numbered("Leg_Collider"));
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

            // 끝단 시브(도르래)는 아래 팁 플랫폼 뒤에서 트윈 시브 블록으로 한 번에 생성(겹침 제거 재배치).

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
            // ── 끝단 트윈 시브 블록(도르래) — 호이스트 로프 2가닥이 붐 끝에서 되돌아 트롤리로 가는 되돌이 시브 ──
            //   휠 2개를 하나의 공통 핀(Z축)에 끼우고 z=±0.05로 벌려, 트롤리 호이스트 시브·로프 소켓(z±0.05)과 정렬.
            //   (기존: 두 휠을 X로 포개 0.008 관통 → 로프 선에 맞춰 한 축 재배치, 겹침 0.)
            //   X=x1-0.025(팁 플랫폼 x1-0.08..x1 발자국 안), Y=0.03(유지), 휠 반지름 0.02(≈실척 0.96m 유지).
            {
                float sx = x1 - 0.025f, sy = 0.03f;
                float sr = 0.02f;            // 휠 반지름(유지)
                float sHalfW = 0.014f;       // 휠 반폭(Z) → 폭 0.028
                float zFall = 0.05f;         // 호이스트 falls Z — 트롤리 시브/로프 소켓과 동일 선
                float chkR = sr * 1.18f;     // 치크 원판 반지름
                // 휠 한 쌍(±zFall) — 이름 유지
                Rod(boom, "Sheave_Tip",
                    new Vector3(sx, sy, -zFall - sHalfW), new Vector3(sx, sy, -zFall + sHalfW), sr, CDark);
                Rod(boom, "Sheave_Tip2",
                    new Vector3(sx, sy,  zFall - sHalfW), new Vector3(sx, sy,  zFall + sHalfW), sr, CDark);
                // 각 휠 양옆 치크 플레이트(안쪽+바깥쪽) — 휠을 사이에 끼워 로프 이탈 방지
                for (int s = -1; s <= 1; s += 2)
                {
                    float inZ  = zFall - sHalfW - 0.005f;   // 안쪽 치크 중심 |Z| = 0.031 (휠 끝 0.036 안쪽)
                    float outZ = zFall + sHalfW + 0.005f;   // 바깥 치크 중심 |Z| = 0.069 (휠 끝 0.064 바깥)
                    Rod(boom, "Sheave_Cheek",
                        new Vector3(sx, sy, s * (inZ - 0.002f)),  new Vector3(sx, sy, s * (inZ + 0.002f)),  chkR, CStruct);
                    Rod(boom, "Sheave_Cheek",
                        new Vector3(sx, sy, s * (outZ - 0.002f)), new Vector3(sx, sy, s * (outZ + 0.002f)), chkR, CStruct);
                }
                // 공통 관통 핀 + 축단 캡
                float pinZ = zFall + sHalfW + 0.02f;        // 관통 핀 끝 |Z| = 0.084
                Rod(boom, "Sheave_Pin",
                    new Vector3(sx, sy, -pinZ), new Vector3(sx, sy, pinZ), sr * 0.3f, CDark);
                for (int s = -1; s <= 1; s += 2)
                    Rod(boom, "Sheave_PinCap",
                        new Vector3(sx, sy, s * (pinZ - 0.004f)), new Vector3(sx, sy, s * pinZ), sr * 0.5f, CStruct);
                // 행어 플레이트 — 시브 핀(y=0.03)을 팁 플랫폼 밑면(y≈0.064)에 매달아 공중부양 제거.
                //   바깥 치크 선(z=±0.069)에서 데크까지 수직 플레이트로 올림 → 시브 블록이 플랫폼에 매달린 구조.
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(boom, "Sheave_Hanger", new Vector3(sx, 0.048f, s * (zFall + sHalfW + 0.005f)),
                        new Vector3(0.012f, 0.038f, 0.008f), CStruct);
                    Box(boom, "Sheave_HangerGusset", new Vector3(sx, 0.04f, s * (pinZ - 0.006f)),
                        new Vector3(0.01f, 0.022f, 0.006f), CStruct);
                }
            }
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
            // 페스툰 트랙 행어 브래킷 — 트랙(z≈0.1905)을 옆 캣워크(z≈0.1945)에 고정(2mm 공중부양 제거).
            foreach (float fbx in new[] { x0 + len * 0.25f, mid, x1 - len * 0.25f })
                Box(boom, "Festoon_Bracket", new Vector3(fbx, -0.003f, GirderOuterZ + 0.010f),
                    new Vector3(0.006f, 0.006f, 0.013f), CStruct);
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
            // 늘어진 케이블 루프 — 인접 새들 사이가 아래로 처지는 카테너리(1가닥).
            float festBotY = -0.020f, festSag = 0.012f;
            for (int i = 0; i < loops - 1; i++)
                CableCatenary(boom, "Festoon_Cable",
                    new Vector3(festX[i], festBotY, festZ),
                    new Vector3(festX[i + 1], festBotY, festZ),
                    festSag, 0.0016f, CCable);

            // ── 뒷부분(육지측 백리치) 디테일 ──
            // [감사 #1·#2 삭제] 적층 무게 블록 'Counterweight'와 받침 'CW_Brace' 제거.
            //   STS는 선회·기복 크레인이 아니라 적층 죽은무게 평형추가 없음 — 긴 아웃리치는
            //   A프레임 정점 + 포어/백스테이 텐션 + 기계실 질량 + 백리치 구조로 균형. 이 구조는
            //   아래 Stay_Anchor / BuildApexAndStays 에 이미 존재하므로 평형추는 중복이자 오류였다.
            // 백스테이 앵커 브래킷
            // 백스테이 이퀄라이저 빔 — 두 거더 뒤끝(±GirderGapZ)을 잇고 백스테이가 양 끝에 물림
            Box(boom, "Stay_Anchor", new Vector3(x0 + 0.02f, 0.075f, 0f),
                new Vector3(0.02f, 0.035f, 2f * GirderGapZ), CMachine);
            // 백리치 끝 플랫폼 + 경고등
            Box(boom, "Back_Platform", new Vector3(x0 + 0.02f, 0.066f, 0f),
                new Vector3(0.06f, 0.004f, 2f * GirderOuterZ), CMachine);

            // [A 보강] 백리치 끝 면(x0) 프레임 — 두 거더(z±GirderGapZ)를 잇는 X-브레이스 + 상·하 타이.
            //   평형추 제거로 휑해진 백리치 끝을 구조적으로 마감(현실 STS 백리치 단부 프레임).
            {
                float bgB = 0.015f, bgT = 0.065f;   // 거더 밑면/윗면
                Strut(boom, "Backreach_EndBrace", new Vector3(x0, bgB, -GirderGapZ), new Vector3(x0, bgT, GirderGapZ), 0.006f, CStruct);
                Strut(boom, "Backreach_EndBrace", new Vector3(x0, bgB, GirderGapZ), new Vector3(x0, bgT, -GirderGapZ), 0.006f, CStruct);
                Box(boom, "Backreach_EndTie", new Vector3(x0, bgB, 0f), new Vector3(0.008f, 0.008f, 2f * GirderGapZ), CStruct);
                Box(boom, "Backreach_EndTie", new Vector3(x0, bgT, 0f), new Vector3(0.008f, 0.008f, 2f * GirderGapZ), CStruct);
            }
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
                // 트레이 정션 박스 2 — 트레이 립 위에 얹고 글랜드를 아래로(케이블 위 y≈0.038로 올림) → 베이스가 케이블 관통하던 것 제거.
                foreach (float jx in new[] { mid - 0.45f, mid + 0.55f })
                    JunctionBox(boom, "Boom_TrayJBox", new Vector3(jx, 0.052f, trayZ + s * 0.006f),
                        new Vector3(0.03f, 0.024f, 0.013f), new Vector3(0f, 0f, s), new Vector3(0f, -1f, 0f), CMachine);
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
            float cageOuterX = 0.027f;              // 케이지 측면바 바깥면(side bar x=±0.025, 두께0.004 → 0.027) — 케이지가 사다리(±0.015)보다 넓다.
            float deckNearX = LandLegX - cageOuterX; // 케이지 '바깥면'에서 끝냄 — 데크가 케이지(Cage_Hoop 측면바)를 타고 넘지 않게.
                                                    //   사다리 가장자리(±0.015)로 끝내면 케이지(±0.025)를 0.01 덮어 Cage_Hoop을 침범한다(이번 버그).
                                                    //   머리 위 개구부: 사다리(±0.015)는 데크 끝(-0.027)보다 +X라 그대로 비어 있음.
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
            // i=0(x=LandLegX, 옛 MHAccess_Post_1)은 사용자 요청으로 제외 — i=1부터 생성.
            for (int i = 1; i <= 4; i++)
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

        // 빌트업 브레이스 — 상수 z 평면 위 대각 부재를 웹 플레이트 + 양 가장자리 플랜지 리브(I/채널 단면)로.
        //   민짜 정사각 Strut보다 단면감↑. a→b 는 같은 z 평면(면밖=±Z). LookRotation으로 평면에 평평히 눕혀 롤 고정.
        static void BuiltUpBrace(Transform parent, string name, Vector3 a, Vector3 b, Color color,
                                 float webWidth = 0.024f, float webThick = 0.007f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, d / len);  // 로컬 Y=축, Z=면밖(+Z), X=면내수직
            Vector3 mid = (a + b) * 0.5f;
            var web = NewCube(name, parent);
            web.transform.localPosition = mid;
            web.transform.localRotation = rot;
            web.transform.localScale = new Vector3(webWidth, len, webThick);
            Colorize(web, color);
            for (int s = -1; s <= 1; s += 2)   // 양 가장자리 플랜지 리브(면밖으로 도드라져 I 단면감)
            {
                var fl = NewCube(name + "_Flange", parent);
                fl.transform.localPosition = mid + rot * new Vector3(s * (webWidth * 0.5f - 0.002f), 0f, 0f);
                fl.transform.localRotation = rot;
                fl.transform.localScale = new Vector3(0.004f, len, webThick * 2.2f);
                Colorize(fl, color);
            }
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
            var go = new GameObject(Numbered(name));
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

        // 홈 파인 시브(도르래) 휠 — 두 플랜지 + 중앙 V홈(로프 시트) + 허브 보어. 회전체(lathe).
        //   a/b = 양 축단 중심(축 방향), rOuter=플랜지 외경, rHub=허브 보어 반경, grooveDepth=홈 깊이.
        //   민짜 원기둥(Rod)과 달리 폭을 좁히고 림에 홈을 파 '드럼통'이 아닌 도르래 실루엣을 만든다.
        static GameObject Sheave(Transform parent, string name, Vector3 a, Vector3 b,
                                 float rOuter, float rHub, float grooveDepth, Color color, int seg = 24)
        {
            var go = new GameObject(Numbered(name));
            go.transform.SetParent(parent, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildSheaveMesh(rOuter, rHub, grooveDepth, seg);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = a;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(1f, len, 1f);   // 폭(축 길이)만 늘림, 반경은 메시에 반영
            mr.sharedMaterial = GetMaterial(color);
            return go;
        }

        // 단위 폭(y=0→1) 홈 파인 시브 메시 — 닫힌 단면(좌면→림 플랜지+V홈→우면→보어)을 Y축으로 회전.
        static Mesh BuildSheaveMesh(float rOuter, float rHub, float grooveDepth, int seg)
        {
            seg = Mathf.Max(12, seg);
            float R  = rOuter;
            float rh = Mathf.Clamp(rHub, 0.0003f, rOuter * 0.6f);
            float rg = Mathf.Max(rh + 0.0005f, rOuter - grooveDepth);   // 홈 바닥(로프 시트) 반경
            // 단면 닫힌 루프 (축 t=0→1, 반경 r): 좌 허브→좌 플랜지(얇은 립)→V홈 바닥→우 플랜지 립→우 허브, 보어로 닫힘.
            //   플랜지를 얇은 립(0.15)으로, 홈을 넓게(중앙 70%) → '아령'이 아닌 납작한 도르래 림.
            (float t, float r)[] prof = {
                (0f, rh), (0f, R), (0.15f, R), (0.5f, rg), (0.85f, R), (1f, R), (1f, rh),
            };
            int n = prof.Length;
            var verts = new List<Vector3>(n * seg);
            var tris  = new List<int>(n * seg * 6);
            for (int p = 0; p < n; p++)
                for (int i = 0; i < seg; i++)
                {
                    float ang = (i / (float)seg) * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(ang) * prof[p].r, prof[p].t, Mathf.Sin(ang) * prof[p].r));
                }
            for (int p = 0; p < n; p++)
            {
                int pa = p * seg, pb = ((p + 1) % n) * seg;   // 마지막 p6→p0 = 내부 보어(닫힘)
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    int a0 = pa + i, a1 = pa + j, b0 = pb + i, b1 = pb + j;
                    tris.Add(a0); tris.Add(b0); tris.Add(a1);   // BuildFrustumMesh와 동일 와인딩(바깥 노멀)
                    tris.Add(a1); tris.Add(b0); tris.Add(b1);
                }
            }
            var m = new Mesh { name = "STS_Sheave" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
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
            string gnm = Numbered("Truss_Gusset");
            if (_deletedParts.Contains(gnm)) return null;   // 삭제 지정 제외
            var go = new GameObject(gnm);
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

        // 단일 고력볼트 헤드 — 면(z=0)에서 +Z로 h 돌출. 둥근 받침 와셔 + 육각 머리(곧은 벽 + 상단 모따기 + 윗 육각면).
        // 실제 ISO 육각볼트의 시그니처(머리 모서리 챔퍼 + 와셔)를 살려 납작한 프리즘 느낌을 제거. 아랫면은 면에 묻혀 생략.
        static void AddBoltPrism(List<Vector3> v, List<int> t, Vector2 c, float r, float h, int seg)
        {
            // 머리 높이 h 기준 비례(전체 돌출 = h 유지)
            float washerH  = h * 0.2f;                          // 받침 와셔 두께
            float wallTopZ = washerH + (h - washerH) * 0.55f;   // 육각 머리 곧은 벽 상단
            float topZ     = h;                                 // 모따기 끝 = 윗 육각면
            float topR     = r * 0.82f;                         // 모따기 후 윗 육각면 외접반경
            float washerR  = r * 1.12f;                         // 와셔 반경 — 머리 대각폭(r)보다 살짝만 크게(고리처럼 안 보이게)

            // 1) 둥근 받침 와셔(매끈) — 옆면 + 윗 환형 캡
            AddRoundCollar(v, t, c, washerR, 0f, washerH, 12);
            // 2) 육각 머리 곧은 벽(평면 패싯)
            AddHexBand(v, t, c, r, r, washerH, wallTopZ, seg);
            // 3) 상단 모따기 밴드(벽 → 좁아진 윗면)
            AddHexBand(v, t, c, r, topR, wallTopZ, topZ, seg);
            // 4) 윗 육각 캡
            AddHexCap(v, t, c, topR, topZ, seg);
        }

        // 육각 단면 코너 좌표(밴드/캡 정렬용). 원래 프리즘과 동일하게 코너를 step/2만큼 회전.
        static Vector3 HexCorner(Vector2 c, float r, float ang, float z)
            => new Vector3(c.x + Mathf.Cos(ang) * r, c.y + Mathf.Sin(ang) * r, z);

        // 육각 밴드(하단링 rLow@zLow → 상단링 rHigh@zHigh) — 면마다 독립 정점으로 평면 패싯(각진 육각 유지).
        static void AddHexBand(List<Vector3> v, List<int> t, Vector2 c,
                               float rLow, float rHigh, float zLow, float zHigh, int seg)
        {
            float step = Mathf.PI * 2f / seg;
            for (int f = 0; f < seg; f++)
            {
                float a0 = f * step + step * 0.5f, a1 = (f + 1) * step + step * 0.5f;
                int b = v.Count;
                v.Add(HexCorner(c, rLow,  a0, zLow));    // b  : low A
                v.Add(HexCorner(c, rLow,  a1, zLow));    // b+1: low B
                v.Add(HexCorner(c, rHigh, a1, zHigh));   // b+2: high B
                v.Add(HexCorner(c, rHigh, a0, zHigh));   // b+3: high A
                t.Add(b); t.Add(b + 1); t.Add(b + 2);    // 바깥(반경) 노멀
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }

        // 윗 육각 캡(중심 팬, +Z 향함)
        static void AddHexCap(List<Vector3> v, List<int> t, Vector2 c, float r, float z, int seg)
        {
            float step = Mathf.PI * 2f / seg;
            int cc = v.Count; v.Add(new Vector3(c.x, c.y, z));   // 중심
            int rb = v.Count;
            for (int i = 0; i < seg; i++) v.Add(HexCorner(c, r, i * step + step * 0.5f, z));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                t.Add(cc); t.Add(rb + i); t.Add(rb + j);
            }
        }

        // 둥근 와셔 칼라 — 매끈한 옆면(정점 공유 → RecalculateNormals로 원통 음영) + 윗 환형 디스크(육각 머리 밑면 덮음).
        static void AddRoundCollar(List<Vector3> v, List<int> t, Vector2 c, float r, float zLo, float zHi, int seg)
        {
            int b = v.Count;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a) * r, cy = Mathf.Sin(a) * r;
                v.Add(new Vector3(c.x + cx, c.y + cy, zLo));   // b+2i  : 밑
                v.Add(new Vector3(c.x + cx, c.y + cy, zHi));   // b+2i+1: 위
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                int b0 = b + 2 * i, t0 = b + 2 * i + 1, b1 = b + 2 * j, t1 = b + 2 * j + 1;
                t.Add(b0); t.Add(b1); t.Add(t1);   // 옆면(바깥 노멀)
                t.Add(b0); t.Add(t1); t.Add(t0);
            }
            int cap = v.Count; v.Add(new Vector3(c.x, c.y, zHi));   // 윗 디스크 중심(+Z)
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
            string bnm = Numbered("Joint_Bolts");
            if (_deletedParts.Contains(bnm)) return;        // 삭제 지정 제외
            var go = new GameObject(bnm);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = pos + faceRot * new Vector3(0f, 0f, plateOffset);
            go.transform.localRotation = faceRot;
            Colorize(go, CDark);
        }

        // 연결 플레이트(거싯) + 볼트 사각 격자 한 세트 — 면 노멀(faceRot)에 정렬.
        //   실제 구조 접합은 원형 링이 아니라 nx×ny 사각 그리드(행×열) — 그에 맞춰 격자로 박는다.
        static void BoltedPlate(Transform parent, Vector3 pos, Quaternion faceRot,
                                float size, int nx, int ny, float dx, float dy, float boltR)
        {
            float thick = 0.005f;
            Gusset(parent, pos, faceRot, size, thick, CStruct);
            Bolts(parent, pos, faceRot, $"grid{nx}x{ny}_{dx:F4}_{dy:F4}_{boltR:F4}",
                  BoltGrid(nx, ny, dx, dy), boltR, thick * 0.5f + 0.0004f);
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

                // 1) 다리 상단(포털 크로스/A프레임/거더 수렴) — 볼트 링 연결판.
                //    판 폭(2*0.02=0.04)을 다리 외곽폭(LegSec*1.7=0.0425) 안으로 유지 → 가장자리 삐져나옴 방지.
                BoltedPlate(root, new Vector3(x, LegTopY - 0.02f, zOuter),
                    fr, 0.02f, 3, 4, 0.010f, 0.0085f, 0.0019f);   // 다리상단 주절점 3×4=12

                // 2) 실빔(Sill_Beam) ↔ 다리 절점(RailH*0.4)
                BoltedPlate(root, new Vector3(x, RailH * 0.4f, zOuter),
                    fr, 0.02f, 3, 3, 0.011f, 0.011f, 0.0019f);    // 실빔 절점 3×3=9

                // 3) 측면 대각 브레이스 상단 절점(RailH*0.92) — 두 X-브레이스가 각 다리 상단에서 만남
                BoltedPlate(root, new Vector3(x, RailH * 0.92f, zOuter),
                    fr, 0.017f, 3, 2, 0.009f, 0.008f, 0.0018f);   // 브레이스 절점 3×2=6

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
                    // [감사 #5] -Z 거더(s=-1)는 볼트가 +Z(거더 안쪽)로 박혀 파묻혔음 → 면 노멀에 맞춰 180° 회전(바깥 돌출).
                    Bolts(boom, new Vector3(sx, gY, zo), s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0),
                        "splice_col", BoltGrid(1, 4, 0f, 0.013f), 0.0016f, 0.0026f);
                }
                // 상부 플랜지 가로 스플라이스(두 거더를 잇는 덮개판) + 가로 볼트열
                // [감사 #6b] 덮개판 폭을 플랜지 바깥 가장자리(±GirderOuterZ)까지 넓힘(기존 ±GirderGapZ는 플랜지 안쪽만 덮음).
                Box(boom, "Girder_FlangeSplice", new Vector3(sx, gTop + 0.004f, 0f),
                    new Vector3(0.016f, 0.004f, 2f * GirderOuterZ), CStruct);
                // [감사 #6b] 볼트열이 두 거더 사이 갭(±0.1)에 떨어져 플랜지(|z|≈0.16)를 한 줄도 안 물었음
                //   → 거더별 2x2 클러스터를 z=±GirderGapZ 플랜지 위에 배치.
                for (int gs = -1; gs <= 1; gs += 2)
                    Bolts(boom, new Vector3(sx, gTop + 0.008f, gs * GirderGapZ), Quaternion.Euler(-90, 0, 0),
                        "splice_flange", BoltGrid(2, 2, 0.007f, 0.018f), 0.0015f, 0.0004f);
            }
        }

        // 모서리 베벨(챔퍼) 큐브를 사용 — 모든 Box/Strut가 공유 메시로 "마감된 느낌"
        static GameObject NewCube(string name, Transform parent)
        {
            var go = new GameObject(Numbered(name));
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

            var go = new GameObject(Numbered(name));
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
            pb.name = Numbered(name);
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

        // 파츠 번호 — 같은 이름마다 _1,_2…를 붙여 하이어라키에서 인스턴스를 구분(작업 지정 편의). 생성마다 _nameSeq 리셋.
        static readonly Dictionary<string, int> _nameSeq = new Dictionary<string, int>();
        // 삭제 지정 인스턴스 — 절차 생성이 결정적이라 이름(번호)이 고정됨. 여기 적힌 이름만 생성 제외(번호는 소비돼 이후 번호 안 밀림).
        //   하이어라키에서 지우고 싶은 객체 이름을 추가하면 재생성해도 안 나옴.
        static readonly HashSet<string> _deletedParts = new HashSet<string>
        {
            "Truss_Gusset_129", "Truss_Gusset_130", "Joint_Bolts_1", "Joint_Bolts_2",
            "MH_DoorKick_1", "MHAccess_Bracket_2", "Ladder_Bracket_5",
        };

        // 삭제 지정 일괄 제거 — Gusset/Joint_Bolts 외 모든 파츠(Box/Strut/Cone/PbBox…)에 대응.
        //   생성은 정상 진행돼 Numbered 번호가 소비되므로 이후 인스턴스 번호가 밀리지 않고, 생성 후 이름으로 제거한다.
        static void PruneDeletedParts(Transform root)
        {
            if (_deletedParts.Count == 0) return;
            var doomed = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && _deletedParts.Contains(t.name))
                    doomed.Add(t.gameObject);
            foreach (var go in doomed)
                if (go != null) Object.DestroyImmediate(go);
        }
        static string Numbered(string n)
        {
            int c = _nameSeq.TryGetValue(n, out var v) ? v + 1 : 1;
            _nameSeq[n] = c;
            return n + "_" + c;
        }

        static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = Numbered(name);
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
            float metallic = 0.38f, smooth = 0.45f;   // 구조 강철 기본 — 광택 약간↑(매트 플라스틱 '토이' 느낌 완화)
            bool  useTex = true, emissive = false, useRopeTex = false;
            float emi = 0f;

            if (Same(c, CGlass))       { metallic = 0.0f;  smooth = 0.92f; useTex = false; }              // 유리: 매끈
            else if (Same(c, CCable))  { metallic = 0.80f; smooth = 0.34f; useTex = false; useRopeTex = true; } // 와이어 로프: 강철 광택 + 나선 strand
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
            else if (useRopeTex)
            {
                var rt = GetRopeTexture();
                var tile = new Vector2(1f, 14f);   // 둘레 1회(6 strand) × 길이방향 촘촘히 → 나선 결
                if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", rt); mat.SetTextureScale("_BaseMap", tile); }
                if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", rt); mat.SetTextureScale("_MainTex", tile); }
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

        // 와이어 로프 텍스처 — 헬리컬 6-strand 대각 밴드 + strand 내 미세 와이어 + 그레인.
        //   그레이스케일(평균≈0.8)이라 _BaseColor(CCable 강철 회색)에 곱해져 꼬인 로프 결을 만든다.
        static Texture2D GetRopeTexture()
        {
            if (_ropeTex != null) return _ropeTex;
            const int N = 128;
            const float TAU = Mathf.PI * 2f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "STS_RopeStrand", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                // 외측 6 strand 나선 — u(둘레) 6회, v(길이) 따라 감김 → 대각 밴드
                float strand = Mathf.Cos((6f * u + 3f * v) * TAU);      // 능선(+)=밝게, 골(-)=어둡게
                float shade  = 0.80f + 0.20f * strand;
                // strand 골 음영 강조(둥근 단면 느낌)
                shade -= Mathf.SmoothStep(0.0f, 1.0f, -strand) * 0.12f;
                // strand 안 미세 와이어(촘촘한 결)
                float wire = Mathf.Cos((36f * u + 18f * v) * TAU);
                shade *= 0.95f + 0.05f * wire;
                float grain = (Hash(x, y) - 0.5f) * 0.05f;
                float val = Mathf.Clamp01(shade + grain);
                byte b = (byte)(val * 255f);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _ropeTex = tex;
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
