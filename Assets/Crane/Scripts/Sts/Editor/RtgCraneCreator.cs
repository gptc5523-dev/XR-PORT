#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 메뉴에서 RTG(Rubber-Tyred Gantry) 크레인 GameObject 계층을 자동 생성.
    ///
    /// ── 1단계: 형태(매싱)만 ──────────────────────────────────────────────
    /// 이 스크립트는 RTG의 1차 실루엣(포털 프레임 테이블)만 세운다. 로프/시브/
    /// 캣워크/계단/기계실 디테일·무버·스프레더 트위스트락 등은 이후 단계에서 얹는다.
    ///
    /// 생성 hierarchy:
    ///     RTG_Crane                    (root, localScale = 1/24)
    ///       ├─ Bogies                  코너 4개(고무 타이어 보기 매싱)
    ///       ├─ SillBeams               좌/우 하부 종빔 2개(보기 위 결속)
    ///       ├─ Legs                    박스 다리 4개
    ///       ├─ SideBraces              측면 프레임 대각 브레이스 2개
    ///       ├─ TopFrame
    ///       │    ├─ MainGirder x2      상부 횡거더(트롤리 주행면)
    ///       │    └─ EndTie x2          거더 전/후 결속 종빔
    ///       ├─ Trolley                 두 거더 위 주행체(기계실 박스 + 운전실 캔틸레버)
    ///       └─ Spreader                40ft 스프레더(매싱, 중간높이 파킹)
    ///
    /// 치수는 실척(m)으로 코드에 적고, 루트 localScale = ModelScale(1/24)로 축소해
    /// 컨테이너·플레이어와 정합(=STS와 동일 스케일). 값은 6+1열·1-over-5/6급 표준 비율.
    /// 형상은 ProBuilder 박스(PbBox)로 생성 → 디자이너가 곧바로 편집 가능.
    /// </summary>
    public static class RtgCraneCreator
    {
        // 실척(m) 치수 — 6+1 wide, 1-over-5/6 RTG 표준 비율
        const float Scale       = StsConfig.ModelScale; // 1/24 — 컨테이너/STS와 동일(SSOT)

        // 6+1열·1-over-5 하이큐브 RTG 표준 + 물리 검산으로 직접 유도(이전 작업 기록 미참조).
        //   스팬: 6열×2.438 + 갭 + 트럭레인 → 다리중심 ≈ 23.6m (공개 6+1 RTG 23.47~23.6).
        //   순양정 = 다리상단 20.5 − 트롤리/헤드블록/리빙 여유 2.2 ≈ 18.3m (1-over-5 하이큐브 17.9~18.5 충족).
        const float SpanX       = 23.6f;   // 좌/우 다리 중심 간격(횡) = 컨테이너 6열 + 트럭레인
        const float BaseZ       = 7.5f;    // 전/후 포털 간격(주행방향) = 휠베이스
        const float LegSec      = 0.9f;    // 다리 박스 단면 한 변

        const float GirderTopY  = 22.5f;   // 주거더 윗면 높이
        const float GirderDepth = 2.0f;    // 주거더 깊이(Y) → 다리 상단(=거더 밑면) 20.5
        const float GirderWidthZ= 1.0f;    // 주거더 폭(Z)
        const float GirderOverX = 0.75f;   // 주거더 X 오버행(양측)
        const float RailH       = 0.10f;   // 트롤리 크레인레일 높이(A75급). 휠 시트 = GirderTopY+RailH (SSOT)

        const float SillTopY    = 2.87f;   // 실빔 윗면(= 다리 시작). 2.4→2.87: 실빔 밑면 2.07이 이퀄라이저 상단 1.97·셰브런가드 1.90 위로 0.1+ 클리어 → 조향 킹핀 회전 간극 확보(2.4에선 실빔이 이퀄라이저를 0.37m 관통했음). 순양정=거더밑면 20.5 불변
        const float SillDepth   = 0.8f;    // 실빔 깊이(Y) → 실빔 2.07~2.87
        const float SillWidthX  = 0.9f;    // 실빔 폭(X)
        const float SillOverZ   = 1.0f;    // 실빔 Z 오버행(양측)

        // 주행부(고무 타이어 보기) — 16륜(코너당 4륜: 2스테이션×트윈)
        const float TireOD      = 1.5f;    // 18.00-25 타이어 외경(반경 0.75, 접지 y=0)
        const float TireW       = 0.5f;    // 타이어 폭(축=X)
        const float TwinDX      = 0.34f;   // 트윈 타이어 중심 X 반오프셋(간격 0.68)
        const float StationDZ   = 1.05f;   // 코너 내 2스테이션 Z 반간격(휠베이스 2.1). 타이어 OD1.5 사이 틈 0.6 확보(중앙 프레임 수용)


        const float SprParkY    = 8.0f;    // 스프레더 파킹 높이(중간, 리빙 전 검토용)

        // VR 수동 조종 구동 범위 (실척 m, 수학 검산) — STS 무버·컨트롤러 재사용
        //   트롤리 X: 휠(트롤리중심±2.2)+휠어셈블리 반폭 0.175 이 레일 엔드스톱 안면(±12.25)에 물리지 않는 한계
        //     = 12.25 − 2.2 − 0.175 = ±9.875 → 여유 두어 ±9.5. (6+1열 대부분 커버, 최외곽열 ±10.1은 레일한계로 소폭 미달 — 추후 조정)
        const float TrolleyMinX = -9.5f, TrolleyMaxX = 9.5f;
        //   호이스트 Y(스프레더 홀더 로컬): 상한 = 헤드블록 소켓(홀더+1.392)이 트롤리 프레임 밑면(23.2) 아래 0.3 여유 → 홀더 21.5.
        //     행정 = RTG 1-over-5 정격 양정 18.1m(레퍼런스 §1) → 하한 = 21.5 − 18.1 = 3.4. (파킹 8.0은 범위 내, 로프행정=드럼24.25−소켓 검증 일치)
        const float HoistMinY   = 3.4f, HoistMaxY = 21.5f;
        //   갠트리 Z: 야드 블록 따라 실척 ±20m 주행 → 월드 = ±20×Scale(생성 위치 기준). STS(±53m)보다 짧은 스택 이동 스케일.
        const float GantryRangeM = 20f;

        /// <summary>야드 배치 yaw(°) — 0 = 주행축이 월드 Z(안벽 평행), 스팬이 월드 X.
        /// 오너 지시 2026-08-10 "RTG 90도 돌려줘"로 종전 90°(주행=X, 안벽 수직)에서 전환.
        /// 야드 블록도 같은 방향으로 재유도됨(StsQuayGroundCreator §터미널 레이아웃 수식).
        /// ★ 회전은 반드시 '_Bay 부모 피벗'에 준다 — 루트에 주면 GantryMover의 로컬 Z 주행이 깨진다.</summary>
        const float YardYawDeg = 0f;

        /// <summary>야드 자동 배치 RTG 대수 — 오너 지시 2026-08-10 "RTG 2대 무조건 배치".
        /// 블록이 더 많으면 안벽에 가까운 블록부터 채운다(블록 수 &gt; 장비 수는 실물도 정상).</summary>
        const int YardRtgCount = 2;

        // 트롤리 권상 (레퍼런스: US5,314,262 컨테이너 크레인 트롤리 권상장치)
        //   개방 프레임 위 더블스레드 드럼 2개를 깊이(Z)로 오프셋 → 로프 4가닥이 프레임 사이로 곧게 하강,
        //   데드엔드 스프레더 소켓(x=±TrRopeGrooveX, z=±TrRopeZ)에 수직 정착. 트롤리 밑 리드시브 불필요.
        const float TrHoistDrumR  = 0.45f;  // 권상 드럼 반경
        const float TrDrumY       = 24.25f; // 드럼 축 높이(= 프레임 윗면 23.8 + 드럼 반경 0.45)
        const float TrRopeGrooveX = 1.2f;   // 드럼 그루브/로프 X(= 스프레더 소켓 X). 더블스레드 2그루브/드럼
        const float TrRopeZ       = 1.8f;   // 로프 수직 하강 Z(= 스프레더 소켓 Z)
        static float TrDrumZc => TrRopeZ - TrHoistDrumR;  // ±1.35 — 로프가 드럼 측면 접선(z=Zc+R=1.8)에서 수직 이탈

        // 파생 좌표
        static float LegHalfX   => SpanX * 0.5f;                 // ±11.8
        static float LegHalfZ   => BaseZ * 0.5f;                 // ±3.75
        static float GirderUnderY => GirderTopY - GirderDepth;   // 20.5 = 다리 상단
        static float GirderCenterY => GirderTopY - GirderDepth * 0.5f; // 21.5
        static float GirderLenX => SpanX + 2f * GirderOverX;     // 25.1
        static float SillCenterY => SillTopY - SillDepth * 0.5f; // 2.47
        static float SillLenZ   => BaseZ + 2f * SillOverZ;       // 9.5
        static float SprHeadY   => SprParkY + 1.392f;            // 헤드블록/소켓 높이 = 파킹 + hbY0.058×24 (SSOT). 이전 +1.704는 STS 헤드'상단'(0.071)기준 → includeHead:false(헤드+대각스트럿 생략)인 RTG에선 프레임이 스프레더 위 1.1m 떠 보였음. 헤드'본체'중심(hbY)에 착좌

        // 색(파란 도장 강철 RTG)
        static readonly Color CBlue = new Color(0.10f, 0.30f, 0.62f);  // 구조 파랑(코발트)
        static readonly Color CDark = new Color(0.15f, 0.16f, 0.18f);  // 허브/기어박스/킹핀 다크메탈
        static readonly Color CTrolley = new Color(0.13f, 0.35f, 0.68f); // 트롤리(약간 밝은 파랑)
        static readonly Color CRubber = new Color(0.06f, 0.06f, 0.07f); // 고무 타이어(매트 블랙)
        static readonly Color CYellow = new Color(0.85f, 0.72f, 0.10f); // 셰브런 안전 경고
        static readonly Color CRim = new Color(0.34f, 0.36f, 0.39f);    // 휠 림(중간 강철)
        static readonly Color CLens = new Color(1.00f, 0.95f, 0.72f);   // 투광등 렌즈(웜 화이트 발광)

        [MenuItem("Model/PG/크레인/RTG 크레인 생성", false, 1)]
        public static void CreateFromMenu()
        {
            // 기본 스폰 = YardBlock_Zone(야드 블록) 위에 자동 접지 배치. 씬뷰에서 가장 가까운 존을 고른다.
            //   존이 없으면 야드 지면(Quay_Ground) 중심, 그마저 없으면 씬뷰 중심으로 폴백.
            var pos = TryNearestYardZonePosition(out var zp) ? zp
                    : TryYardGroundPosition(out var p) ? p : SceneViewPivot();
            // 주행방향 = 월드 Z(안벽 평행)·스팬 = 월드 X — 야드 블록 장축과 동일(오너 지시 2026-08-10 "RTG 90도 돌려줘").
            //   크레인 로컬 Z가 곧 타이어 구르는 축이므로 yaw=0이면 그대로 월드 Z 주행이 된다.
            var go = CreatePlaced(pos, YardYawDeg, "RTG_Crane");
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        // 지정 위치·주행방향(yaw)으로 RTG를 '부모 피벗' 아래 생성 — 갠트리(루트-로컬 Z=타이어 구르는 축)가
        //   피벗 회전으로 월드 주행축에 매핑되게 한다. yaw=90°면 주행=월드 X(육지 방향)·스팬=월드 Z.
        //   루트에 직접 회전을 주면 GantryMover의 localPosition.z가 회전 무관 월드 Z라 크레인이 옆(스팬)으로 기어가는
        //     버그가 남([[feedback_derive_motion_per_crane_not_copy_sts]]). 반드시 피벗에 회전을 줘야 주행축이 따라온다.
        static GameObject CreatePlaced(Vector3 pos, float yawDeg, string craneName)
        {
            var pivot = new GameObject(craneName + "_Bay").transform;
            pivot.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            Undo.RegisterCreatedObjectUndo(pivot.gameObject, "Place " + craneName);

            var go = Create(Vector3.zero);                 // 원점 생성 → 갠트리 Min/Max = 0±범위(루트-로컬 Z)
            go.name = craneName;
            go.transform.SetParent(pivot, worldPositionStays: false);   // 로컬0·스케일 1/24 유지, 피벗 yaw 상속
            return go;
        }

        /// <summary>
        /// 야드 블록에 RTG를 자동 배치(오너 지시: 2대). 주행축 = 월드 Z(안벽 평행), 스팬 = 월드 X.
        ///
        /// ★ 배치 좌표는 부두 생성기가 '실제로 그린' 블록 존(YardBlock_Zone) 바운즈에서 읽는다 — 산식을 복사하지 않는다.
        ///   종전엔 야드 X·블록 Z 산식을 이 파일에 다시 적어 두 곳이 갈라졌고(SSOT 미러), 부두 수식이 바뀔 때마다
        ///   RTG만 엉뚱한 자리에 남았다. 존을 직접 읽으면 부두 쪽 수식이 바뀌어도 자동으로 따라온다.
        /// </summary>
        [MenuItem("Model/PG/크레인/RTG 야드 배치 (2대)", false, 2)]
        public static void PlaceRtgsInYard()
        {
            var groundGo = GameObject.Find(StsPartNames.QuayGround);
            if (groundGo == null) { Debug.LogWarning("[RTG] Quay_Ground 없음 — 'Ground/부두 바닥 생성' 먼저."); return; }

            var deck = StsQuayGroundCreator.FindQuayDeckRenderer(groundGo);
            float groundY = deck != null ? deck.bounds.max.y : 0f;   // 걷는 면 윗면(=데크 y0). 바다 오선택은 헬퍼가 차단.

            // 블록 존 수집 — 부두 생성기가 그린 렌더러가 SSOT(안벽 가까운 순 정렬은 헬퍼가 담당).
            var zones = StsQuayGroundCreator.FindYardBlockZones(groundGo);
            if (zones.Count == 0)
            {
                Debug.LogWarning("[RTG] YardBlock_Zone이 없습니다 — 'Ground/도로'를 켠 뒤 '부두 바닥 생성'을 먼저 실행하세요.");
                return;
            }

            // 기존 RTG 제거(피벗째) — 대수를 줄여도 옛 개체가 남지 않게.
            foreach (var nm in new[] { "RTG_Crane", "RTG_Crane_1", "RTG_Crane_2", "RTG_Crane_3", "RTG_Crane_4" })
            {
                var bay = GameObject.Find(nm + "_Bay"); if (bay != null) Undo.DestroyObjectImmediate(bay);
                var g   = GameObject.Find(nm);          if (g   != null) Undo.DestroyObjectImmediate(g);
            }

            int n = Mathf.Min(YardRtgCount, zones.Count);
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                var b = zones[i].bounds;
                var go = CreatePlaced(new Vector3(b.center.x, groundY, b.center.z), YardYawDeg, "RTG_Crane_" + (i + 1));

                // 주행 범위 = (블록 길이 − 휠베이스)/2, 로컬 Z 대칭 → 크레인이 블록 밖으로 나가지 않는다.
                float half = Mathf.Max(0f, (b.size.z - BaseZ * Scale) * 0.5f);
                var gm = go.GetComponent<GantryMover>();
                if (gm != null) gm.Configure(-half, half);

                log.Append($"\n  #{i + 1} 블록중심=({b.center.x:F3},{b.center.z:F3})u · 블록 {b.size.x * StsConfig.InvModelScale:F1}×{b.size.z * StsConfig.InvModelScale:F1}m · 주행 ±{half * StsConfig.InvModelScale:F1}m");
            }
            Selection.activeGameObject = GameObject.Find("RTG_Crane_1");
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[RTG] {n}대 배치 완료(블록 {zones.Count}개 중 안벽 가까운 순) — 주행축=월드 Z(안벽 평행)·스팬=월드 X, 지면 y={groundY:F3}." + log);
        }

        public static GameObject Create(Vector3 worldPosition)
        {
            _nameSeq.Clear();

            var root = new GameObject("RTG_Crane").transform;
            root.position = worldPosition;
            root.localScale = Vector3.one * Scale;   // 실척 자식 → 1/24 렌더
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Create RTG Crane");

            BuildBogies(root);
            BuildSills(root);
            BuildLegs(root);
            BuildSideBraces(root);
            BuildPortalKnees(root);   // 스팬 포털 코너 니 브레이스(다리→거더 대각 보강)
            BuildTopFrame(root);
            BuildTrolleyRails(root);  // 두 거더 위 트롤리 주행 레일
            BuildCatwalks(root);      // 거더 바깥 정비 캣워크 + 난간
            BuildLegLadder(root);     // -x 앞다리 바깥 수직 사다리(지면→캣워크)
            BuildCableReel(root);     // 전동 급전 케이블 릴(+x 후방 다리 외측) — 실물 RTG 전원 모듈(택1: Cable Reel)
            BuildFloodlights(root);   // 상부 거더 바깥 저프로파일 투광등 분산 — 야간 하역 조사(실물 RTG 상부 모듈, 레퍼런스 §2·R5/R6 반영)
            var trolley  = BuildTrolley(root);   // RTG 크랩 트롤리(비주얼) — 두 거더 위 X 주행
            var spreader = BuildSpreader(root);  // 40ft 스프레더(STS 통째 복사 — 헤드블록 포함)
            BuildSpreaderReeving(root); // 트롤리→STS 헤드블록 소켓(x±1.2,z±1.8,y9.392) 4폴 로프(정적, 파킹)

            BuildDrives(root, trolley, spreader, worldPosition);  // VR 수동 조종(STS 무버·컨트롤러 재사용) — 트롤리 X·호이스트 Y·갠트리 Z

            return root.gameObject;
        }

        // 코너 4개 주행 보기 — 16륜(코너당 트윈타이어 2스테이션) + 이퀄라이저·킹핀·기어박스·셰브런가드
        static void BuildBogies(Transform root)
        {
            var g = new GameObject("Bogies").transform; g.SetParent(root, false);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                BuildBogie(g, sx, sz);
        }

        // 한 코너의 보기 조립 — cx/cz = 코너 중심, 접지 y=0
        static void BuildBogie(Transform parent, float sx, float sz)
        {
            float cx = sx * LegHalfX, cz = sz * LegHalfZ;
            float axleY = TireOD * 0.5f;                 // 0.75
            var b = new GameObject("Bogie").transform; b.SetParent(parent, false);
            b.localPosition = new Vector3(cx, 0f, cz);   // 이하 로컬은 코너 기준

            // 이퀄라이저 빔(타이어 위, Z 방향) — 타이어 상단(1.5)에 얹힘(y중심 1.72). 보기 양끝 엔드커버까지 연장해 접합
            float eqLen = 2f * StationDZ + TireOD + 0.5f; // 4.1 — z±2.05
            PbBox(b, "Bogie_Equalizer", new Vector3(0f, 1.72f, 0f),
                new Vector3(0.9f, 0.5f, eqLen), CBlue);

            // 중앙 보기 프레임(벨리) — 두 스테이션 사이 Z틈(0.6)을 메워 '속 빈 바퀴' 해소
            PbBox(b, "Bogie_Frame", new Vector3(0f, 0.95f, 0f),
                new Vector3(1.0f, 1.25f, 0.55f), CBlue);

            // 차축 하우징 — 각 스테이션 트윈 사이 틈(0.18)에서 축을 감쌈
            foreach (var dz in new[] { -StationDZ, StationDZ })
                PbBox(b, "Bogie_AxleHousing", new Vector3(0f, TireOD * 0.5f, dz),
                    new Vector3(0.16f, 0.55f, 0.55f), CDark);

            // 킹핀(90° 조향 축) — 이퀄라이저 ↔ 실빔. 중심 1.75→1.95(1.50~2.40): 실빔 상승(밑면 2.07) 후에도 이퀄라이저 0.47m·실빔 0.33m 솔리드 물림, 1.97~2.07 조향 간극을 가로질러 결합
            PbCyl(b, "Bogie_Kingpin", new Vector3(0f, 1.95f, 0f), 0.35f, 0.9f, Vector3.up, CDark);

            // 주행 기어박스(구동륜) — 벨리 프레임 바깥면에 밀착(중앙 z틈 z±0.25, 타이어 회피). x0.375~0.925
            PbBox(b, "Bogie_Gearbox", new Vector3(0.65f, 0.85f, 0f),
                new Vector3(0.55f, 0.65f, 0.5f), CDark);

            // 트윈타이어 2스테이션(Z±) × 2본(X±) = 4륜
            foreach (var dz in new[] { -StationDZ, StationDZ })
            {
                foreach (var dx in new[] { -TwinDX, TwinDX })
                    BuildWheel(b, dx, dz, axleY, Mathf.Sign(dx));
                // 셰브런 엔드커버 — 지면(y0)~프레임(1.9) 광폭 판. 바닥 접지 + 이퀄라이저 접합 → 공중부양 해소
                float guardZ = dz + Mathf.Sign(dz) * (TireOD * 0.5f + 0.1f);   // z±1.9 (타이어 외곽 1.80 밖)
                PbBox(b, "Bogie_ChevronGuard", new Vector3(0f, 0.95f, guardZ),
                    new Vector3(2f * TwinDX + TireW + 0.3f, 1.9f, 0.12f), CYellow);
            }
        }

        // 상세 휠 — 타이어(고무) + 림 디스크(금속, 외측면 돌출) + 돔 허브. outSign = 트윈 바깥 방향
        static void BuildWheel(Transform b, float dx, float dz, float axleY, float outSign)
        {
            PbCyl(b, "Tire", new Vector3(dx, axleY, dz), TireOD * 0.5f, TireW, Vector3.right, CRubber);
            float faceX = dx + outSign * TireW * 0.5f;                          // 타이어 외측면
            PbCyl(b, "Wheel_Rim", new Vector3(faceX, axleY, dz), 0.40f, 0.18f, Vector3.right, CRim);
            Ball(b, "Wheel_Hub", new Vector3(faceX + outSign * 0.06f, axleY, dz),
                new Vector3(0.16f, 0.28f, 0.28f), CDark);
        }

        // 좌/우 하부 종빔 — 각 측 전/후 보기를 종방향으로 잇고 다리 밑동을 받음
        static void BuildSills(Transform root)
        {
            var g = new GameObject("SillBeams").transform; g.SetParent(root, false);
            foreach (var sx in new[] { -1f, 1f })
                PbBox(g, "Sill",
                    new Vector3(sx * LegHalfX, SillCenterY, 0f),
                    new Vector3(SillWidthX, SillDepth, SillLenZ), CBlue);
        }

        // 박스 다리 4개
        static void BuildLegs(Transform root)
        {
            var g = new GameObject("Legs").transform; g.SetParent(root, false);
            float cy = (SillTopY + GirderUnderY) * 0.5f;
            float h  = GirderUnderY - SillTopY;
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                PbBox(g, "Leg",
                    new Vector3(sx * LegHalfX, cy, sz * LegHalfZ),
                    new Vector3(LegSec, h, LegSec), CBlue);
        }

        // 측면(주행방향) 프레임 대각 X-브레이스 — 각 측 2개(전-하↔후-상 + 후-하↔전-상)로 X 완성.
        //   실물 RTG 포털은 주행방향 프레임을 X/K로 보강. 두 대각은 중앙(y11.685)서 교차(현실 X-브레이스 교차).
        static void BuildSideBraces(Transform root)
        {
            var g = new GameObject("SideBraces").transform; g.SetParent(root, false);
            float yLo = SillTopY + 0.5f, yHi = GirderUnderY - 0.5f;   // 3.37 ~ 20.0
            foreach (var sx in new[] { -1f, 1f })
            {
                // 전-하 ↔ 후-상
                Strut(g, "SideBrace", new Vector3(sx * LegHalfX, yLo,  LegHalfZ),
                                      new Vector3(sx * LegHalfX, yHi, -LegHalfZ), 0.5f, CBlue);
                // 후-하 ↔ 전-상 (X 완성)
                Strut(g, "SideBrace", new Vector3(sx * LegHalfX, yLo, -LegHalfZ),
                                      new Vector3(sx * LegHalfX, yHi,  LegHalfZ), 0.5f, CBlue);
            }
        }

        // 스팬 포털 프레임(z=±3.75 X-Y평면) 다리-거더 코너 니 브레이스 4개 — 각 다리 상단서 거더 밑면 안쪽으로 대각.
        //   현재 스팬 프레임엔 대각 보강이 없어 박스만 맞물림 → 실물 포털 코너의 표준 니 브레이스로 보강.
        //   [간섭 검산] z=±3.75(거더/다리 중심선). bot(±11.35,18.0)=다리 내면(11.35)서 시작·두께 0.4로 x≤11.50 < 측면브레이스 내면 11.55 → 무간섭.
        //     top(±9.1,20.4)은 거더 밑면(20.5) 0.1 아래·두께로 20.54까지 관입 → 거더에 솔리드 결합. EndTie(x±11.55~12.05)와도 무간섭.
        static void BuildPortalKnees(Transform root)
        {
            var g = new GameObject("PortalKnees").transform; g.SetParent(root, false);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                var bot = new Vector3(sx * (LegHalfX - 0.45f), 18.0f, sz * LegHalfZ);   // 다리 내면서 시작
                var top = new Vector3(sx * (LegHalfX - 2.7f),  20.4f, sz * LegHalfZ);   // 거더 밑면 안쪽 정착
                Strut(g, "PortalKnee", bot, top, 0.4f, CBlue);
            }
        }

        // 상부 프레임 — 횡거더 2(트롤리 주행면) + 전/후 결속 종빔 2
        static void BuildTopFrame(Transform root)
        {
            var g = new GameObject("TopFrame").transform; g.SetParent(root, false);
            foreach (var sz in new[] { -1f, 1f })
                PbBox(g, "MainGirder",
                    new Vector3(0f, GirderCenterY, sz * LegHalfZ),
                    new Vector3(GirderLenX, GirderDepth, GirderWidthZ), CBlue);

            float tieLenZ = BaseZ + LegSec;         // 8.40 = ±4.20 — 다리 바깥면(z=±4.20)까지만 덮음(코너 솔리드, 초과 0). 이전 BaseZ+GirderWidthZ(8.5,±4.25)는 거더 기준이라 하강 후 Leg_3/4를 0.05 초과·돌출했음
            const float endTieY     = 19.738f;      // [사용자 씬 배치] 거더중심 21.5→19.738로 하강(다리 상단 바로 아래 횡결속)
            const float endTieThick = 0.5f;         // 단면 두께 = SideBrace(Strut thick 0.5)와 동일(슬림 결속바)
            foreach (var sx in new[] { -1f, 1f })
                PbBox(g, "EndTie",
                    new Vector3(sx * LegHalfX, endTieY, 0f),
                    new Vector3(endTieThick, endTieThick, tieLenZ), CBlue);
        }

        // 거더 상부 정비 캣워크 — 각 거더 바깥쪽(트롤리 반대편) 통로 + 난간. 통로 z4.0~4.8 → 트롤리(z±3.9)와 0.1 이격
        static void BuildCatwalks(Transform root)
        {
            var g = new GameObject("Catwalks").transform; g.SetParent(root, false);
            float deckY = GirderTopY;                          // 22.5 거더 윗면
            const float deckH = 0.05f, wlkW = 0.8f, postH = 1.1f, postSec = 0.05f;
            float railLen = GirderLenX - 0.6f;                 // 24.5 (포스트 배치선)
            foreach (var sz in new[] { -1f, 1f })
            {
                float wCenter = sz * (LegHalfZ + 0.25f + wlkW * 0.5f);   // ±4.4 통로 중심(거더 위 얹힘+캔틸레버)
                float outer   = sz * (LegHalfZ + 0.25f + wlkW);          // ±4.8 바깥 가장자리(난간선)
                PbBox(g, "Catwalk_Deck", new Vector3(0f, deckY + deckH * 0.5f, wCenter),
                    new Vector3(railLen, deckH, wlkW), CRim);
                PbBox(g, "Catwalk_Toe", new Vector3(0f, deckY + deckH + 0.075f, outer),
                    new Vector3(railLen + postSec, 0.15f, 0.03f), CYellow);
                // 코너 솔리드 결합: 레일이 양끝 포스트를 t/2씩 가로질러 덮게 railLen+=postSec (reference-corner-solid-joint)
                foreach (var rh in new[] { postH, postH * 0.5f })       // 상·중 난간
                    PbBox(g, "Catwalk_Rail", new Vector3(0f, deckY + deckH + rh, outer),
                        new Vector3(railLen + postSec, 0.04f, 0.04f), CRim);
                int n = Mathf.Max(1, Mathf.RoundToInt(railLen / 3f));    // 포스트 ~3m 간격
                for (int i = 0; i <= n; i++)
                {
                    float px = -railLen * 0.5f + railLen * i / n;
                    PbBox(g, "Catwalk_Post", new Vector3(px, deckY + deckH + postH * 0.5f, outer),
                        new Vector3(0.05f, postH, 0.05f), CRim);
                }
                // 캔틸레버 지지 니 브래킷(~6m 간격) — 삼각 거싯 웹 + 데크 시트 + 거더 백플랜지 + 대각 하변 보강.
                //   삼각형: 거더면 상단(내상)·데크 바깥 밑면(외상)·거더면 하부(내하). 직각=내상, 빗변=하변.
                float girderOuterZ = sz * (LegHalfZ + GirderWidthZ * 0.5f);   // ±4.25 거더 바깥면
                float brkBotY = deckY - 0.8f;                                 // 21.7 거더면 하부 정착
                const float gussetT = 0.02f, flangeF = 0.09f;                 // 거싯 두께 / 플랜지 단면
                float seatLenZ = Mathf.Abs(outer - girderOuterZ);            // 0.55 데크 캔틸레버 폭
                int nb = Mathf.Max(1, Mathf.RoundToInt(railLen / 6f));
                for (int i = 0; i <= nb; i++)
                {
                    float px = -railLen * 0.5f + railLen * i / nb;
                    var topIn  = new Vector3(px, deckY,   girderOuterZ);      // 내상(거더면 상단)
                    var topOut = new Vector3(px, deckY,   outer);            // 외상(데크 바깥 밑면)
                    var botIn  = new Vector3(px, brkBotY, girderOuterZ);      // 내하(거더면 하부)
                    TriGusset(g, "Catwalk_Bracket", topIn, topOut, botIn, gussetT, CBlue);   // 웹
                    // 데크 시트(상변 수평) — 윗면이 데크 밑면(deckY)에 접함
                    PbBox(g, "Catwalk_Bracket", new Vector3(px, deckY - flangeF * 0.5f, (girderOuterZ + outer) * 0.5f),
                        new Vector3(flangeF, flangeF, seatLenZ), CBlue);
                    // 거더 백플랜지(내변 수직) — 거더 바깥면(z=girderOuterZ)에 정착
                    PbBox(g, "Catwalk_Bracket", new Vector3(px, (deckY + brkBotY) * 0.5f, girderOuterZ),
                        new Vector3(flangeF, deckY - brkBotY, flangeF), CBlue);
                    // 대각 하변 보강 플랜지(빗변 botIn→topOut) — 거싯 자유변 스티프너
                    Strut(g, "Catwalk_Bracket", botIn, topOut, flangeF, CBlue);
                }
            }
        }

        // -x 앞다리 바깥 수직 사다리 — 지면→캣워크. 다리·보기·거더(모두 x≥-12.55) 바깥 x-12.7 에 세워 전부 비간섭
        static void BuildLegLadder(Transform root)
        {
            var g = new GameObject("LegLadder").transform; g.SetParent(root, false);
            float x = -(LegHalfX + LegSec * 0.5f + 0.45f);     // -12.7 — 다리(-12.25)·보기가드(-12.54)·거더끝(-12.55) 바깥
            float z = LegHalfZ;                                // +3.75 (−x 앞다리 축선, 이 z엔 셰브런가드 없음)
            float y0 = 0.2f, yTop = GirderTopY;                // 0.2 → 22.5
            const float railGapZ = 0.5f;

            // 레일 2 (수직)
            foreach (var sz in new[] { -1f, 1f })
                PbBox(g, "Ladder_Rail", new Vector3(x, (y0 + yTop) * 0.5f, z + sz * railGapZ * 0.5f),
                    new Vector3(0.06f, yTop - y0, 0.06f), CRim);
            // 발판(rung) — 0.3m 간격
            int n = Mathf.FloorToInt((yTop - y0 - 0.3f) / 0.3f);
            for (int i = 1; i <= n; i++)
                PbBox(g, "Ladder_Rung", new Vector3(x, y0 + i * 0.3f, z),
                    new Vector3(0.06f, 0.03f, railGapZ), CRim);
        }

        // 전동 급전 케이블 릴 — 실물 RTG 전원 모듈(택1: Cable Reel, 출처 R1/R2). +x 후방 다리 외측에 U자 크래들로 얹은 릴.
        //   [솔리드 결합] 릴을 캔틸레버로 띄우지 않고 베이스빔 + 양측 크래들 암 + 하부 대각 브레이스 + 다리 마운트판으로
        //     다리에 통짜 지지([[reference_corner_solid_joint]]). 낙하 케이블 스터브 없음(케이블은 릴에 감겨 있음).
        //   [간섭 검산] 다리면 x=LegHalfX+LegSec/2=12.25. 릴 축=X, 중심(13.075, 9.0, −3.75), 플랜지 R1.5.
        //     내측 암(12.26~12.58)↔내플랜지(12.625)·외측 암(13.57~13.89)↔외플랜지(13.525) 각 0.045 클리어.
        //     위 SideBrace(x=11.8면 y≈20)·아래 실빔(y≤2.87) 모두 무간섭. 감김R 1.25<플랜지R 1.5 → 림 노출.
        static void BuildCableReel(Transform root)
        {
            var g = new GameObject("CableReel").transform; g.SetParent(root, false);
            float legFaceX = LegHalfX + LegSec * 0.5f;      // 12.25 — +x 다리 외측면
            const float xc = 13.075f, yc = 9.0f, zc = -3.75f; // 릴 중심(축=X)
            const float flangeR = 1.5f, flangeW = 0.06f;    // 플랜지 외경/두께
            const float halfW   = 0.45f;                    // 릴 반폭(플랜지 x=xc±0.45 = 12.625/13.525)
            const float hubR    = 0.55f, hubW = 0.90f;      // 코어 드럼
            const float windR   = 1.25f, windW = 0.80f;     // 감긴 케이블 스택(플랜지 안쪽)
            const float armInX  = 12.42f, armOutX = 13.73f; // 크래들 암 X(플랜지 바깥, 각 0.045 클리어)
            const float baseY   = 6.9f;                     // 크래들 베이스 상면 근처(암 밑동)

            // 다리 마운트판 — 베이스/브레이스 정착부 커버(다리 외측면 밀착)
            PbBox(g, "Reel_MountPlate", new Vector3(legFaceX + 0.06f, 7.6f, zc),
                new Vector3(0.12f, 3.4f, 0.9f), CBlue);
            // 캔틸레버 베이스 빔(다리→릴 밑)
            PbBox(g, "Reel_Base", new Vector3(13.05f, baseY, zc),
                new Vector3(1.7f, 0.4f, 0.55f), CBlue);       // x 12.2~13.9 (다리면 12.25에 물림)
            // U자 크래들 암 2 (플랜지 양 바깥, 베이스서 축 위까지) — 축을 양단 지지
            foreach (var ax in new[] { armInX, armOutX })
                PbBox(g, "Reel_CradleArm", new Vector3(ax, 8.0f, zc),
                    new Vector3(0.32f, 2.6f, 0.55f), CBlue);   // y 6.7~9.3 (베이스 위·축 y9 관통)

            // 릴 축 — 내측 암(12.42)서 외측 암(13.73) 관통, 양단 크래들 지지
            PbCyl(g, "Reel_Axle", new Vector3(xc, yc, zc), 0.18f, armOutX - armInX, Vector3.right, CDark);
            // 코어 드럼 + 감긴 케이블 + 양 플랜지(축과 동심)
            PbCyl(g, "Reel_Hub", new Vector3(xc, yc, zc), hubR, hubW, Vector3.right, CDark);
            PbCyl(g, "Reel_Cable", new Vector3(xc, yc, zc), windR, windW, Vector3.right, CRubber); // 매트 흑
            foreach (var fx in new[] { -halfW, halfW })
                PbCyl(g, "Reel_Flange", new Vector3(xc + fx, yc, zc), flangeR, flangeW, Vector3.right, CRim);

            // 구동 기어모터 — 외측 암 축단에 결합(캔틸레버 아님, 암에 접촉)
            PbBox(g, "Reel_GearMotor", new Vector3(armOutX + 0.26f, yc, zc),
                new Vector3(0.4f, 0.6f, 0.6f), CDark);
        }

        // 상부 프레임 투광등 — 실물 RTG는 전용 고탑 마스트가 아니라(그건 STS/경기장식),
        //   상부 프레임이 이미 ~20m 고소라 각 상부 거더 바깥 가장자리(캣워크 난간 밖)에 저프로파일 아웃리거로 투광등을
        //   분산 장착하고 하향-내향 광각으로 아래 컨테이너 스택 전장을 조사한다.
        //   [출처 검증] Konecranes 모듈 다이어그램(코너 투광등 존재) + 항만조명 실무(Razorlux/AGC): "컨테이너가 거더 가까이
        //     적재→광각 하향, 고소·해양등급 장착". 정확한 개수·간격은 터미널별 DIALux 산정(표준 없음)이라 분산 4/측으로 근사.
        //   [간섭 검산] 아웃리거·투광등은 캣워크 바깥 난간선(z±4.8) 밖(z±5.25)·난간 상단(23.65) 위 → 통로 헤드룸 무간섭.
        //     z±5.25는 거더(z≤±4.25)·트롤리 프레임(z≤±4.0) 밖. 아웃리거 밑면(23.65)이 난간 상단에 얹혀 솔리드 결합.
        static void BuildFloodlights(Transform root)
        {
            var g = new GameObject("Floodlights").transform; g.SetParent(root, false);
            float deckY = GirderTopY;                          // 22.5 상부 프레임/캣워크 데크 높이
            const float floodW = 0.5f;
            const int perSide = 4;
            float span = GirderLenX - 4f;                      // 조명 X 배치 구간(끝에서 안쪽 2m씩)
            foreach (var sz in new[] { -1f, 1f })
            {
                float zRail  = sz * (LegHalfZ + 0.25f + 0.8f);   // ±4.80 캣워크 바깥 난간선
                float zFlood = sz * (LegHalfZ + 0.25f + 1.25f);  // ±5.25 난간 밖 아웃리거 끝(투광등 위치)
                var aim = Quaternion.Euler(sz * 30f, 0f, 0f);    // 하향-내향(스팬 중앙 스택 방향, X축 30° 틸트)
                for (int i = 0; i < perSide; i++)
                {
                    float px = perSide > 1 ? -span * 0.5f + span * i / (perSide - 1) : 0f;
                    // 아웃리거(난간 상단 밖으로 캔틸레버) — 난간선서 zFlood까지 수평 암, 밑면이 난간 상단(23.65)에 얹힘
                    PbBox(g, "Flood_Outrigger", new Vector3(px, deckY + 1.2f, (zRail + zFlood) * 0.5f),
                        new Vector3(0.12f, 0.1f, Mathf.Abs(zFlood - zRail) + 0.1f), CRim);
                    // 투광등(아웃리거 끝 밑, 하향-내향)
                    RtgFloodlight(g, new Vector3(px, deckY + 1.15f, zFlood), aim, floodW);
                }
            }
        }

        // 투광등 1기 — 하향 발광 어셈블리(요크 암 + 본체 + 베젤 림 + 발광 렌즈 디스크). at=상단 부착점, aim=조사 방향(identity=수직 하향).
        //   [구 금지] 렌즈는 납작 원통 디스크([[feedback_sphere_for_round_joints_not_flat_steel]]). CLens는 발광(GetMaterial 이미션).
        static void RtgFloodlight(Transform parent, Vector3 at, Quaternion aim, float w)
        {
            var p = new GameObject(Numbered("Floodlight")).transform;
            p.SetParent(parent, false);
            p.localPosition = at; p.localRotation = aim;
            float hBody = 0.55f * w, bezT = 0.12f * w, lensT = 0.08f * w, yokeT = 0.12f * w;
            float bodyTopY = -0.15f * w;                     // 요크 아래 본체 윗면
            float bodyCY   = bodyTopY - hBody * 0.5f;
            float bodyBotY = bodyTopY - hBody;               // -0.70w
            // 요크 암(±X) — 부착점(y0)서 본체 밑까지 본체를 감싸 받침
            for (int a = -1; a <= 1; a += 2)
                PbBox(p, "Floodlight_Yoke", new Vector3(a * (w * 0.5f + yokeT * 0.5f), bodyBotY * 0.5f, 0f),
                    new Vector3(yokeT, -bodyBotY, w * 0.6f), CDark);
            // 램프 본체
            PbBox(p, "Floodlight_Housing", new Vector3(0f, bodyCY, 0f), new Vector3(w, hBody, w), CDark);
            // 베젤 림(렌즈 둘레, 본체보다 살짝 넓게)
            PbBox(p, "Floodlight_Bezel", new Vector3(0f, bodyBotY - bezT * 0.5f, 0f),
                new Vector3(w * 1.06f, bezT, w * 1.06f), CRim);
            // 발광 렌즈 디스크(조사면)
            PbCyl(p, "Floodlight_Lens", new Vector3(0f, bodyBotY - bezT - lensT * 0.5f, 0f),
                w * 0.42f, lensT, Vector3.up, CLens);
        }

        // 트롤리 주행 레일 — 각 주거더(z=±3.75) 중심선 위, 거더 전장(25.1) 끝까지 연속.
        //   A75급 크레인레일 단면: 발(foot)+웨브(web)+머리(head) 3단(합=RailH 0.10, 휠시트=y0+RailH).
        //   + 솔플레이트(체결판)·클립(~2m 간격)·양끝 엔드스톱(버퍼)으로 마감.
        static void BuildTrolleyRails(Transform root)
        {
            var g = new GameObject("TrolleyRails").transform; g.SetParent(root, false);
            float y0 = GirderTopY;                             // 22.5 거더 윗면(= 발 밑면)
            float railLen = GirderLenX;                        // 25.1 — 거더 전장(끝까지 연속)
            float footH = 0.025f, webH = 0.045f, headH = RailH - footH - webH;  // 0.025+0.045+0.03
            foreach (var sz in new[] { -1f, 1f })
            {
                float z = sz * LegHalfZ;                       // ±3.75 거더 중심선
                // 솔플레이트(거더↔레일 체결판) — 발보다 넓게 전장
                PbBox(g, "Rail_Soleplate", new Vector3(0f, y0 - 0.015f, z),
                    new Vector3(railLen, 0.03f, 0.26f), CDark);
                // 발 → 웨브 → 머리(연속 I-단면)
                PbBox(g, "TrolleyRail_Foot", new Vector3(0f, y0 + footH * 0.5f, z),
                    new Vector3(railLen, footH, 0.16f), CRim);
                PbBox(g, "TrolleyRail_Web", new Vector3(0f, y0 + footH + webH * 0.5f, z),
                    new Vector3(railLen, webH, 0.05f), CRim);
                PbBox(g, "TrolleyRail_Head", new Vector3(0f, y0 + footH + webH + headH * 0.5f, z),
                    new Vector3(railLen, headH, 0.09f), CRim);
                // 체결 클립(발 양옆, ~2m 간격)
                int nc = Mathf.Max(2, Mathf.RoundToInt(railLen / 2f));
                for (int i = 0; i <= nc; i++)
                {
                    float px = -railLen * 0.5f + railLen * i / nc;
                    foreach (var cz in new[] { -1f, 1f })
                        PbBox(g, "Rail_Clip", new Vector3(px, y0 + 0.02f, z + cz * 0.095f),
                            new Vector3(0.08f, 0.04f, 0.05f), CDark);
                }
                // 양 끝 엔드스톱(버퍼) — 레일 끝까지 채우고 트롤리 이탈 방지
                foreach (var ex in new[] { -1f, 1f })
                    PbBox(g, "Rail_EndStop", new Vector3(ex * (railLen * 0.5f - 0.15f), y0 + 0.18f, z),
                        new Vector3(0.3f, 0.28f, 0.22f), CYellow);
            }
        }

        // 트롤리(크랩) — 두 주거더 위 레일을 X로 주행. 대칭 사각 프레임 + 주행휠4 + 막힌 기계실 + STS 운전실 재사용. x=0 파킹.
        //   로프 4가닥은 여전히 개방 프레임 사이(x±1.2,z±1.8)로 곧게 하강. 운전실은 +X 단부 아래에 캔틸레버로 매닮.
        static Transform BuildTrolley(Transform root)
        {
            var t = new GameObject("Trolley").transform; t.SetParent(root, false);

            float railTopY  = GirderTopY + RailH;             // 22.60 — 레일 윗면(휠 시트)
            const float wheelR = 0.3f;
            float wheelCY   = railTopY + wheelR;              // 22.90 — 휠이 레일에 얹힘
            float frameBotY = wheelCY + wheelR;               // 23.20 — 프레임 밑면(휠 위)
            const float frameH = 0.6f;
            float frameCY   = frameBotY + frameH * 0.5f;      // 23.50
            float frameTopY = frameBotY + frameH;             // 23.80 (= 드럼 밑면 TrDrumY-R)
            const float frameHX = 3.0f;                       // 프레임 X 반폭(대칭). 기계실(±2.3)·드럼모터(±2.2) 수용 + 여유
            float railZ   = LegHalfZ;                         // ±3.75 — 휠·종빔 레일선
            const float endBeamT = 0.4f;                      // 단부빔 X두께
            float sideLenX  = 2f * frameHX + endBeamT;        // 6.4 — 종빔이 양 단부빔 바깥면(x=±3.2)까지 덮음(코너 솔리드)
            float crossLenZ = 2f * (railZ + 0.25f);           // 8.0 — 단부/크로스빔이 종빔 바깥면(z=±4.0)까지 덮음(코너 솔리드)

            // 대칭 개방 사각 프레임: 종빔2 + 단부빔2 + 드럼크로스빔2
            foreach (var sz in new[] { -1f, 1f })             // 종빔 2 (X방향, 레일 위, z=±3.75) — 휠 지지 주부재
                PbBox(t, "Trolley_SideBeam", new Vector3(0f, frameCY, sz * railZ),
                    new Vector3(sideLenX, frameH, 0.5f), CTrolley);
            // 단부빔 -X (얇은 단부빔) = Trolley_EndBeam_1
            PbBox(t, "Trolley_EndBeam", new Vector3(-frameHX, frameCY, 0f),
                new Vector3(endBeamT, frameH, crossLenZ), CTrolley);
            // 단부빔 +X = Trolley_EndBeam_2 → 운전실 마운트 브래킷. 캐빈 풋프린트(x 2.34~3.97) 위를 덮게 X를 2.3~4.0으로 넓힘
            //   → 캐빈이 이 브래킷에 매달린 구조가 되어 '떠 보임' 해소(마운트 포스트 top 23.224가 브래킷 23.2~23.8에 물림). 종빔끝(3.2) 밖 0.8은 캔틸레버.
            const float cabMinX = 2.3f, cabMaxX = 4.0f;
            PbBox(t, "Trolley_EndBeam", new Vector3((cabMinX + cabMaxX) * 0.5f, frameCY, 0f),
                new Vector3(cabMaxX - cabMinX, frameH, crossLenZ), CTrolley);
            foreach (var sx in new[] { -1f, 1f })             // 드럼 크로스빔 2 (Z방향, x=±1.5) — 드럼 베어링 받침
                PbBox(t, "Trolley_CrossBeam", new Vector3(sx * 1.5f, frameCY, 0f),
                    new Vector3(0.3f, frameH, crossLenZ), CTrolley);

            // 주행 휠 4 (거더 위 레일을 X로 굴러 → 축 Z). 트레드+양측 플랜지+브래킷
            float treadW = 0.11f, flangeW = 0.02f, flangeR = wheelR + 0.04f;
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                var wc = new Vector3(sx * 2.2f, wheelCY, sz * railZ);   // 휠베이스 4.4(프레임 코너 근처) — 주행 안정
                PbCyl(t, "Trolley_Wheel", wc, wheelR, treadW, Vector3.forward, CDark);
                foreach (var fz in new[] { -1f, 1f })
                    PbCyl(t, "Trolley_WheelFlange",
                        wc + new Vector3(0f, 0f, fz * (treadW * 0.5f + flangeW * 0.5f)),
                        flangeR, flangeW, Vector3.forward, CDark);
                PbBox(t, "Trolley_WheelBracket", new Vector3(sx * 2.2f, frameBotY - 0.05f, sz * railZ),
                    new Vector3(0.35f, 0.4f, 0.6f), CBlue);
            }

            BuildTrolleyMachinery(t, frameTopY);
            BuildTrolleyCab(t);
            return t;
        }

        // 운전실 — STS 크레인 운전실을 그대로 복사 재사용. +X 확장 베이 아래에 매달아 하역측(−X·아래)을 조망.
        //   STS 운전실은 모델단위(실척×1/24)로 지어지고 홀더-로컬 자체 오프셋(본체중심 model x≈-0.096, y -0.077..-0.152)을 가짐 →
        //   holder.localScale=24 로 루트 1/24 상쇄, 180°Y 회전으로 전면을 −X(중앙/화물)로 돌리고, 산식 위치로 확장 베이 밑에 배치.
        static void BuildTrolleyCab(Transform t)
        {
            var holder = new GameObject("OperatorCabRig").transform;
            holder.SetParent(t, false);
            holder.localScale    = Vector3.one * 24f;                 // 루트 1/24 상쇄 → STS 모델단위 셸 정상크기
            holder.localRotation = Quaternion.Euler(0f, 180f, 0f);    // 전면(model +X)을 −X(중앙/화물)로
            // 배치 산식: 홀더 회전·스케일 반영 시 본체중심 → t-local (holderX+2.3, holderY-2.65). 지붕top=holderY-1.78, 바닥=holderY-3.65.
            //   목표: 본체중심 X≈3.15(+X 확장 베이 밑), 지붕top≈23.1(<프레임밑 23.2). ⇒ holderX=0.85, holderY=24.88.
            holder.localPosition = new Vector3(0.85f, 24.88f, 0f);
            // 통합 마운트 상단을 프레임 밑면(frameBotY=23.2)에 맞춘다. STS 기본(−0.05)은 STS 박스 하단 기준이라
            //   RTG에선 post top이 t-local 23.75까지 솟아 Trolley_EndBeam(23.2~23.8)을 관통했음.
            //   post top = mountTopY+0.003 → t-local = 24.88+24·(mountTopY+0.003). 23.224(2.4cm 임베드) 목표 ⇒ mountTopY=−0.072.
            //   이때 Cab_Mount_Tie top = 24.88+24·(−0.072) = 23.152 < 23.2 → 단부빔 관통 없음, 포스트만 밑면에 솔리드 결합.
            StsCraneCreator.BuildOperatorCabForReuse(holder, -0.072f);
        }

        // 트롤리 권상 기계 — 더블스레드 드럼 2개(깊이 Z 오프셋) + 베어링 + 구동(컴팩트 유성모터) + 막힌 기계실(클래드 박스·지붕·워크웨이).
        //   [레퍼런스 US5,314,262] 드럼 2a/2b를 축 평행·깊이 오프셋 → 로프 4가닥이 프레임 사이로 곧게 하강.
        static void BuildTrolleyMachinery(Transform t, float frameTopY)
        {
            float drumY   = TrDrumY;               // 24.25 (= frameTopY 23.8 + R 0.45)
            const float R = TrHoistDrumR;          // 0.45
            const float halfL = 1.5f;              // 드럼 반길이(베어링 x=±1.5, 그루브 x=±1.2 감쌈)
            float flangeR = R + 0.10f;             // 0.55

            foreach (var sz in new[] { -1f, 1f })  // front(z=+1.35) / back(z=-1.35) 드럼
            {
                float dz = sz * TrDrumZc;          // ±1.35 — 로프가 z=dz+sz·R=±1.8에서 수직 이탈
                PbCyl(t, "Hoist_Drum", new Vector3(0f, drumY, dz), R, 2f * halfL, Vector3.right, CDark);
                foreach (var fx in new[] { -halfL, 0f, halfL })   // 플랜지(양단+중앙) — 더블 그루브 구획
                    PbCyl(t, "Drum_Flange", new Vector3(fx, drumY, dz), flangeR, 0.05f, Vector3.right, CRim);
                foreach (var ex in new[] { -1f, 1f })             // 베어링 페디스털(양단, 크로스빔 위)
                    PbBox(t, "Drum_Bearing", new Vector3(ex * halfL, (frameTopY + drumY) * 0.5f, dz),
                        new Vector3(0.34f, (drumY - frameTopY) + 0.24f, 0.5f), CBlue);
                // 구동(컴팩트 유성기어 내장 모터) — front=+x단, back=-x단 스태거로 무게 균형
                float ds = sz;
                PbCyl(t, "Drum_Motor", new Vector3(ds * (halfL + 0.55f), drumY, dz), 0.35f, 0.9f, Vector3.right, CDark);
                foreach (var fx in new[] { 0.35f, 0.7f })         // 냉각핀
                    PbCyl(t, "Motor_Fin", new Vector3(ds * (halfL + fx), drumY, dz), 0.37f, 0.03f, Vector3.right, CDark);
                PbBox(t, "Motor_TermBox", new Vector3(ds * (halfL + 0.55f), drumY + 0.42f, dz),
                    new Vector3(0.26f, 0.24f, 0.32f), CDark);
            }

            // 기계실(Machinery House): 드럼·모터를 감싸는 막힌 클래드 박스 + 지붕 + 워크웨이 난간
            //   [실물 RTG] 드럼/구동은 밖에 노출되지 않고 판넬 기계실 안. 밑면은 로프 통과로 개방(프레임이 바닥 구조).
            float floorY   = frameTopY;                    // 23.8 — 기계실 바닥(=프레임 윗면)
            const float houseHX = 2.3f;                    // 기계실 X 반폭(모터단 ±2.05·핀 여유 감쌈)
            const float houseHZ = 2.05f;                   // 기계실 Z 반폭(드럼 z±1.35+R 감쌈, 로프 z±1.8은 안쪽)
            const float wallTopY = 25.65f;                 // 벽 상단(드럼top 24.8·텀박스 위 헤드룸 확보)
            float wallH    = wallTopY - floorY;            // 1.85
            float wallCY   = (floorY + wallTopY) * 0.5f;   // 24.725
            const float wallT = 0.08f;                     // 판넬 두께

            // 4벽(클래드 판넬) — 밑면 개방(로프가 프레임 사이로 하강)
            // [코너 솔리드 결합] WallZ가 WallX 바깥면까지 가로질러 덮도록 X를 wallT만큼 연장(양끝 코너 +t/2) → 수직 4코너 노치 0.
            foreach (var sz in new[] { -1f, 1f })          // 전/후 벽(Z끝, X로 김)
                PbBox(t, "House_WallZ", new Vector3(0f, wallCY, sz * houseHZ),
                    new Vector3(2f * houseHX + wallT, wallH, wallT), CTrolley);
            foreach (var sx in new[] { -1f, 1f })          // 좌/우 벽(X끝, Z로 김)
                PbBox(t, "House_WallX", new Vector3(sx * houseHX, wallCY, 0f),
                    new Vector3(wallT, wallH, 2f * houseHZ), CTrolley);
            // 환기 루버(긴 벽 3단, 살짝 돌출)
            foreach (var sz in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                    PbBox(t, "House_Louver", new Vector3(0f, floorY + 0.55f + i * 0.35f, sz * (houseHZ + 0.01f)),
                        new Vector3(2f * houseHX - 0.6f, 0.14f, 0.03f), CDark);
            // 정비 도어(+x 벽)
            PbBox(t, "House_Door", new Vector3(houseHX + 0.01f, floorY + 0.9f, houseHZ - 0.55f),
                new Vector3(0.03f, 1.7f, 0.85f), CDark);

            // 지붕(오버행) + 리프팅 러그 4점
            float roofY = wallTopY + 0.06f;                // 25.71
            PbBox(t, "House_Roof", new Vector3(0f, roofY, 0f),
                new Vector3(2f * houseHX + 0.3f, 0.12f, 2f * houseHZ + 0.3f), CBlue);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                PbBox(t, "House_Lug", new Vector3(sx * (houseHX - 0.3f), roofY + 0.18f, sz * (houseHZ - 0.3f)),
                    new Vector3(0.1f, 0.24f, 0.1f), CDark);

            // 워크웨이 데크(Z끝 여유공간, 프레임 위 그레이팅) + 발끝판 + 상·중 난간(안전 황색)
            float deckY = floorY + 0.02f;                  // 23.82
            const float deckZc = 2.9f;                     // 데크 Z중심(houseHZ 2.05 ~ 프레임끝 3.9 사이)
            foreach (var sz in new[] { -1f, 1f })          // 전/후 워크웨이(X로 김)
            {
                PbBox(t, "House_Walkway", new Vector3(0f, deckY, sz * deckZc),
                    new Vector3(2f * houseHX + 0.3f, 0.05f, 1.6f), CRim);
                float edgeZ = sz * 3.65f;                  // 바깥 난간선
                PbBox(t, "House_Toeboard", new Vector3(0f, deckY + 0.1f, edgeZ),
                    new Vector3(2f * houseHX + 0.3f, 0.14f, 0.03f), CYellow);
                foreach (var ry in new[] { deckY + 1.1f, deckY + 0.6f })
                    PbBox(t, "House_Rail", new Vector3(0f, ry, edgeZ),
                        new Vector3(2f * houseHX + 0.3f, 0.04f, 0.04f), CYellow);
                for (int i = -2; i <= 2; i++)              // 난간 지주(상단이 상부 난간선 deckY+1.1에 맞도록: 바닥 deckY~상단 deckY+1.1)
                    PbBox(t, "House_RailPost", new Vector3(i * (houseHX + 0.15f) / 2f, deckY + 0.55f, edgeZ),
                        new Vector3(0.04f, 1.1f, 0.04f), CYellow);
            }
        }

        // 트롤리→스프레더 4폴 호이스트 리빙 — 로프 4가닥이 드럼 측면 접선점(x=±TrRopeGrooveX, y=TrDrumY, z=±TrRopeZ)에서
        //   개방 프레임 사이로 곧게 하강해 데드엔드 소켓에 수직 정착(수직=드럼 접선 → 꺾임 0, '로프 허공 각짐 금지' 충족).
        //   [향후] SpreaderHoist 무버 도입 시 드럼 감김·하강 구간을 동적 리빙(TrolleyReevingRig)으로 확장.
        static void BuildSpreaderReeving(Transform root)
        {
            var g = new GameObject("Reeving").transform; g.SetParent(root, false);
            float sockY = SprHeadY;                   // 헤드블록 소켓 Y (SSOT)
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                var top = new Vector3(sx * TrRopeGrooveX, TrDrumY, sz * TrRopeZ);
                var bot = new Vector3(sx * TrRopeGrooveX, sockY,   sz * TrRopeZ);
                PbCyl(g, "Hoist_Rope", (top + bot) * 0.5f, 0.025f, top.y - bot.y, Vector3.up, CDark);
            }
        }

        // 스프레더 — STS 스프레더를 그대로 복사(재사용). STS는 모델단위(실척×1/24)로 짓고 스케일 1 가정이라,
        //   RTG(실척+루트1/24)에선 holder.localScale=24로 루트 1/24을 상쇄해야 정상 크기가 된다.
        static Transform BuildSpreader(Transform root)
        {
            var holder = new GameObject("Spreader").transform;
            holder.SetParent(root, false);
            holder.localPosition = new Vector3(0f, SprParkY, 0f);  // 실척 m(루트가 1/24 적용) — 파킹, 리빙은 이후
            holder.localScale = Vector3.one * 24f;                 // 루트 1/24 상쇄 → STS 모델단위 스프레더가 정상 크기
            StsCraneCreator.BuildSpreaderForReuse(holder);         // 90° 회전(장축=Z)·텔레스코픽·트위스트락·부속·헤드블록 (STS 통째 복사)
            return holder;
        }

        // VR 수동 조종 배선 — STS와 동일 무버·컨트롤러 재사용(축 관례 동일: 트롤리 X·호이스트 Y·갠트리 Z).
        //   StsCraneVRController[RequireComponent(StsCrane)]가 StsCrane 애그리게이터의 IAxisMover를 물리 고정틱(FixedUpdate)에
        //   구동하고, 운전실 시점(A)은 LateUpdate 추종([[project_physics_pipeline_fixedupdate]]). 붐/러핑·어태치는 RTG에 없어 null 허용
        //   (컨트롤러는 boom/attach를 안 씀, 애그리게이터 기즈모만 null-guard). 범위는 위 const의 실척 수학 검산값.
        //   [다음 단계] 컨테이너 집기(SpreaderAttach/SpreaderGrabber)·로프 동적 추종(HoistRopeRig/TrolleyReevingRig)은 별도 증분.
        static void BuildDrives(Transform root, Transform trolley, Transform spreaderHolder, Vector3 worldPosition)
        {
            // 트롤리(X) — 이동 시 스프레더 홀더 X 동기(TrolleyMover.OnMoved). 스프레더 홀더도 루트-로컬 실척 m라 동일 좌표계.
            var trolleyMover = trolley.gameObject.AddComponent<TrolleyMover>();
            trolleyMover.Configure(TrolleyMinX, TrolleyMaxX, spreaderHolder);
            // 호이스트(Y) — 스프레더 홀더 로컬 Y 승강.
            var hoist = spreaderHolder.gameObject.AddComponent<SpreaderHoist>();
            hoist.Configure(HoistMinY, HoistMaxY);
            // 갠트리(Z) — 크레인 루트 월드 Z 주행(생성 위치 ± 실척 범위×Scale).
            var gantry = root.gameObject.AddComponent<GantryMover>();
            gantry.Configure(worldPosition.z - GantryRangeM * Scale, worldPosition.z + GantryRangeM * Scale);

            // 갠트리 주행이 kinematic 바디로 충돌 전달(STS와 동일) — 중력/외력 미수용.
            var body = root.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // 애그리게이터 + VR 컨트롤러(붐=null, 어태치=null). 컨트롤러가 RequireComponent(StsCrane)라 먼저 추가.
            var crane = root.gameObject.AddComponent<StsCrane>();
            crane.Configure(null, trolleyMover, hoist, null, gantry);
            root.gameObject.AddComponent<StsCraneVRController>();
        }

        // 헬퍼

        // ProBuilder 박스 — 깨끗한 큐브만 생성(베벨/면다듬기는 디자이너가 ProBuilder로).
        static GameObject PbBox(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, Vector3 euler = default)
        {
            var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            pb.name = Numbered(name);
            pb.transform.SetParent(parent, worldPositionStays: false);
            pb.transform.localPosition = localPos;
            if (euler != Vector3.zero) pb.transform.localRotation = Quaternion.Euler(euler);
            pb.ToMesh();
            pb.Refresh();
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = GetMaterial(color);
            var col = pb.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);   // 형태 단계 — 콜라이더 불필요
            return pb.gameObject;
        }

        // 두 점 a→b를 잇는 각재(로컬 Y축이 길이) — 대각 브레이스용
        static GameObject Strut(Transform parent, string name, Vector3 a, Vector3 b, float thick, Color color)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            var go = PbBox(parent, name, (a + b) * 0.5f, new Vector3(thick, len, thick), color);
            if (len > 1e-4f) go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            return go;
        }

        // 삼각 거싯 플레이트 — 부모 로컬 3점(p0,p1,p2)의 평면 삼각형을 면 법선 방향으로 thick만큼 압출.
        // 정점을 면마다 따로 둬 플랫 셰이딩(강철판 느낌). 니 브래킷 웹 등에.
        static GameObject TriGusset(Transform parent, string name, Vector3 p0, Vector3 p1, Vector3 p2, float thick, Color color)
        {
            Vector3 nrm = Vector3.Cross(p1 - p0, p2 - p0);
            if (nrm.sqrMagnitude < 1e-9f) return null;
            nrm.Normalize();
            Vector3 h = nrm * (thick * 0.5f);
            Vector3 a0 = p0 + h, a1 = p1 + h, a2 = p2 + h;   // 앞면(+n)
            Vector3 c0 = p0 - h, c1 = p1 - h, c2 = p2 - h;   // 뒷면(-n)
            var v = new List<Vector3>();
            var t = new List<int>();
            int f = v.Count; v.Add(a0); v.Add(a1); v.Add(a2);   // 앞 캡(+n, CCW)
            t.Add(f); t.Add(f + 1); t.Add(f + 2);
            int b = v.Count; v.Add(c0); v.Add(c1); v.Add(c2);   // 뒤 캡(-n, 역와인딩)
            t.Add(b); t.Add(b + 2); t.Add(b + 1);
            AddQuad(v, t, a0, a1, c1, c0);                       // 옆면 3(변마다 별도 정점)
            AddQuad(v, t, a1, a2, c2, c1);
            AddQuad(v, t, a2, a0, c0, c2);
            var mesh = new Mesh { name = name + "_mesh" };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(Numbered(name));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color);
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        static void AddQuad(List<Vector3> v, List<int> t, Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3)
        {
            int i = v.Count;
            v.Add(q0); v.Add(q1); v.Add(q2); v.Add(q3);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        // ProBuilder 원통 — 타이어/허브/킹핀. axis = 원통 축 방향(기본 생성은 Y축).
        static GameObject PbCyl(Transform parent, string name, Vector3 localPos, float radius, float height, Vector3 axis, Color color)
        {
            var pb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 24, radius, height, 0);
            pb.name = Numbered(name);
            pb.transform.SetParent(parent, worldPositionStays: false);
            pb.transform.localPosition = localPos;
            pb.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            pb.ToMesh();
            pb.Refresh();
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = GetMaterial(color);
            var col = pb.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            return pb.gameObject;
        }

        // 구 프리미티브(스케일로 타원/돔 가능) — 허브 돔 등.
        static GameObject Ball(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = Numbered(name);
            var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = GetMaterial(color);
            return go;
        }

        // 같은 색 머티리얼 재사용(에셋 미저장 인스턴스) — 파란 도장 강철 기본, 고무는 매트.
        static Material GetMaterial(Color c)
        {
            if (_matCache.TryGetValue(c, out var cached)) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "RTG_Mat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);
            float metallic = 0.30f, smooth = 0.45f;
            if (Same(c, CRubber)) { metallic = 0.0f; smooth = 0.15f; }   // 고무: 매트·무반사
            if (Same(c, CLens))                                          // 투광등 렌즈: 발광(이미션)
            {
                metallic = 0.0f; smooth = 0.60f;
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", c * 2.2f);
                }
            }
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            _matCache[c] = mat;
            return mat;
        }

        static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        static readonly Dictionary<Color, Material> _matCache = new Dictionary<Color, Material>();
        static readonly Dictionary<string, int> _nameSeq = new Dictionary<string, int>();
        static string Numbered(string n)
        {
            int c = _nameSeq.TryGetValue(n, out var v) ? v + 1 : 1;
            _nameSeq[n] = c;
            return n + "_" + c;
        }

        static Vector3 SceneViewPivot()
        {
            var sv = SceneView.lastActiveSceneView;
            return sv != null ? sv.pivot : Vector3.zero;
        }

        // 야드 지면 배치 헬퍼

        // 씬뷰 피벗에 가장 가까운 YardBlock_Zone(도로 그리드가 만든 스택 존) 중심 XZ + 접지 Y(Quay_Ground 슬래브 윗면).
        //   존 실제 렌더러 위치를 직접 읽어 그 위에 얹으므로 존을 손으로 옮겨도 정합. CreateFromMenu 자동 배치용.
        static bool TryNearestYardZonePosition(out Vector3 pos)
        {
            pos = Vector3.zero;
            var pivot = SceneViewPivot();
            Renderer best = null; float bestSq = float.PositiveInfinity;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.gameObject.name != StsPartNames.YardBlockZone) continue;
                var c = r.bounds.center;
                float dx = c.x - pivot.x, dz = c.z - pivot.z;   // XZ 거리(높이 무시)로 최근접 존
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = r; }
            }
            if (best == null) return false;                     // 존 없음 → 폴백

            var ground = FindGroundRenderer();
            float groundY = ground != null ? ground.bounds.max.y : best.bounds.min.y;
            var bc = best.bounds.center;
            pos = new Vector3(bc.x, groundY, bc.z);
            return true;
        }

        // 컨테이너 야드(주차장) 중심 XZ + 지면(Quay_Ground) 윗면 Y.
        // RTG 로컬 y=0 이 보기(바퀴) 밑면이라, 루트를 이 위치에 두면 지면에 정확히 접지.
        // (생성 시 기본 스폰 위치 계산용 — CreateFromMenu에서 사용)
        static bool TryYardGroundPosition(out Vector3 pos)
        {
            pos = Vector3.zero;
            var ground = FindGroundRenderer();
            if (ground == null) return false;
            float groundY = ground.bounds.max.y;

            if (!TryYardCenterXZ(out float cx, out float cz))
            { cx = ground.bounds.center.x; cz = ground.bounds.center.z; }  // 야드 없으면 지면 중앙

            pos = new Vector3(cx, groundY, cz);
            return true;
        }

        // 걷는 면(Quay_Ground) 우선, 없으면 가장 넓은 수평 렌더러를 지면으로.
        static Renderer FindGroundRenderer()
        {
            // 부두가 있으면 그 걷는 면(Asphalt) — 바다 오선택 방지는 공용 헬퍼가 담당.
            var deck = StsQuayGroundCreator.FindQuayDeckRenderer();
            if (deck != null) return deck;

            // 부두가 아예 없는 씬 → 평평한 것 중 면적 최대(바다 계열은 제외).
            Renderer best = null; float bestArea = 0f;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (StsPartNames.IsSeaName(r.gameObject.name)) continue;
                var e = r.bounds.size;
                float area = e.x * e.z;
                if (e.y < e.x && e.y < e.z && area > bestArea) { bestArea = area; best = r; }
            }
            return best;
        }

        // Yard_* (Row/Slot/Edge) 전체 렌더러를 감싸는 바운즈 중심.
        static bool TryYardCenterXZ(out float cx, out float cz)
        {
            cx = cz = 0f;
            bool any = false; Bounds b = default;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!IsUnderYard(r.transform)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return false;
            cx = b.center.x; cz = b.center.z;
            return true;
        }

        static bool IsUnderYard(Transform t)
        {
            // ★ 접두는 "Yard"(밑줄 없음) — 현행 야드 마킹은 "YardBlock_Zone/Rows/Edge" 라 "Yard_" 로 비교하면
            //   하나도 안 걸린다. 그러면 야드 중심 탐색이 조용히 실패하고 '지면 바운즈 중심' 폴백으로 내려가,
            //   그 지면이 바다로 오선택되던 시절엔 RTG가 바다 한가운데 떨어졌다(오너 지적 2026-08-10).
            for (var p = t; p != null; p = p.parent)
                if (p.name.StartsWith(StsPartNames.YardPrefix)) return true;
            return false;
        }
    }
}
#endif
