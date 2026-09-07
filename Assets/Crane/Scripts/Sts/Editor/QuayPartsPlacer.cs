#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 항구 부재 FBX 배치 — 블렌더에서 만든 부재를 씬에 깐다.
    ///
    /// 오너 방침 2026-09-07 "모든 오브젝트는 blender 에서 만들고 유니티로 넘기자".
    ///   메시는 C#에서 절대 만들지 않는다. 이 파일이 하는 일은 '어디에 몇 개' 뿐이다.
    ///   메뉴는 무조건 Model > FBX 아래 둔다(오너 지시).
    ///
    /// 좌표 규약 — 부두 절차 생성기(StsQuayGroundCreator)가 삭제돼 안벽 SSOT가 없다.
    ///   그래서 이 배치기는 '안벽 가장자리 = 월드 원점 X0, 안벽 방향 = Z' 를 기준으로 깐다.
    ///   부두 FBX가 들어오면 그 실측 위치로 갈아끼운다.
    /// </summary>
    public static class QuayPartsPlacer
    {
        const string CurbFbx    = "Assets/Crane/Models/Quay_Curb.fbx";
        const string BollardFbx = "Assets/Crane/Models/Quay_Bollard.fbx";
        const string RailFbx    = "Assets/Crane/Models/Quay_Rail.fbx";
        const string CaissonFbx = "Assets/Crane/Models/Quay_Caisson.fbx";

        // 부재 실척 높이 — 블렌더 빌드 스크립트와 쌍으로 유지한다(문서/스크립트/부두연석_유닛_빌드.py).
        const float CurbHeightM    = 0.528f;   // 단면 0.72W × 0.528H
        const float BollardHeightM = 1.368f;   // 기둥 1.08 + 갓 0.288
        const float RailHeightM    = 0.192f;   // DIN 536 A120 170 + 소플레이트 22
                                               //   = StsConfig.RailSectionH(0.008u) × 24. SSOT 일치

        // ═══ 항구 치수 SSOT — 오너가 새로 계산해 넣는다 (지시 2026-09-07 "기존 항구 사이즈가 있다면 삭제") ═══
        //   PortConfig 가 설계선 LOA 에서 유도한다. 여기에 숫자를 박지 말 것.
        static float BerthLenM => PortConfig.BerthLengthMeters;   // 294 × 1.15 → 340m

        // 부재 배치 간격 — 부재 자체 규격에서 나온 값이라 항구 사이즈와 무관하게 유지.
        const float CurbPitchM     = 4.0f;    // 프리캐스트 유닛 피치(유닛 3.985 + 줄눈 0.015) = FBX 규격
        const float CurbWidthM     = 0.72f;   // 연석 단면 폭 = FBX 규격. 해측면을 안벽 가장자리에 맞추는 데 쓴다
        const float BollardGapM    = 20f;     // 계선주 간격 — 미정이면 오너 값으로 교체
        const float BollardInsetM  = 1.08f;   // 안벽 가장자리 → 육지쪽 계선주 중심 — 미정이면 교체
        const float RailPitchM     = 12.0f;   // 레일 정척 12m + 신축이음 10mm = FBX 규격
        const float CaissonPitchM  = 20.0f;   // 케이슨 1함 20m + 줄눈 30mm = FBX 규격. 340/20 = 17함

        [MenuItem("Model/FBX/항구/연석 배치 (Quay_Curb)", false, 1)]
        static void PlaceCurb()
        {
            if (!BerthReady("연석")) return;
            var fbx = Load(CurbFbx, "연석"); if (fbx == null) return;

            float pitch = CurbPitchM * StsConfig.ModelScale;
            // 내림 — 올림하면 마지막 유닛이 안벽 끝을 최대 반피치(2m) 넘어 바다로 튀어나온다.
            //   나눗셈은 반드시 실척 m 끼리. 모델 단위(×1/24)로 나누면 344/4 가 86.0000012 로 나와
            //   반대로 튀는 순간 유닛 1개가 조용히 사라진다.
            int   units = Mathf.FloorToInt(BerthLenM / CurbPitchM);
            float run   = pitch * units;
            float scale = FbxScaleByHeight(fbx, CurbHeightM);

            // X0 = 안벽 가장자리. 중심을 X0 에 두면 폭의 절반이 바다로 튀어나오므로 반폭만큼 육지쪽(−X).
            float x = -CurbWidthM * 0.5f * StsConfig.ModelScale;

            var root = NewRoot("Quay_Curb");
            for (int i = 0; i < units; i++)
                Place(fbx, root, "Quay_CurbUnit",
                      new Vector3(x, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"연석 {units}유닛 · 피치 {CurbPitchM:F1}m · 총 {run * StsConfig.InvModelScale:F1}m " +
                       $"(안벽 {BerthLenM:F0}m) · scale {scale:F4}");
        }

        [MenuItem("Model/FBX/항구/계선주 배치 (Quay_Bollard)", false, 2)]
        static void PlaceBollard()
        {
            if (!BerthReady("계선주")) return;
            var fbx = Load(BollardFbx, "계선주"); if (fbx == null) return;

            // 구간 '중앙'에 놓는다. 끝점 배치(z = ±안벽/2)는 계선주 반지름만큼 안벽 밖 허공으로 나간다.
            //   중앙 배치는 양끝에 반피치(10m) 여유가 생겨 원기둥이 통째로 데크 위에 올라온다.
            //   연석·레일·케이슨이 쓰는 식과 동일하다.
            float pitch = BollardGapM * StsConfig.ModelScale;
            int   n     = Mathf.FloorToInt(BerthLenM / BollardGapM);
            float run   = pitch * n;
            float scale = FbxScaleByHeight(fbx, BollardHeightM);
            float x     = -BollardInsetM * StsConfig.ModelScale;       // 육지쪽(−X)

            var root = NewRoot("Quay_Bollard");
            for (int i = 0; i < n; i++)
                Place(fbx, root, "Quay_Bollard",
                      new Vector3(x, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"계선주 {n}개 · 간격 {BollardGapM:F0}m · 끝여유 {BollardGapM * 0.5f:F0}m " +
                       $"(안벽 {BerthLenM:F0}m) · 안쪽 {BollardInsetM:F2}m · scale {scale:F4}");
        }

        /// <summary>안벽 케이슨 — 항구의 본체. 데크 윗면이 y=0(크레인 접지·컨테이너 착지면)에 오도록
        /// 안벽고만큼 내려 놓는다. 바다 +X · 육지 −X · 안벽 가장자리 X0 규약이라 케이슨은 가장자리에서
        /// 육지쪽으로 에이프런 폭만큼 뻗는다.</summary>
        [MenuItem("Model/FBX/항구/안벽 배치 (Quay_Caisson)", false, 0)]
        static void PlaceCaisson()
        {
            if (!BerthReady("안벽")) return;
            var fbx = Load(CaissonFbx, "안벽"); if (fbx == null) return;

            float pitch = CaissonPitchM * StsConfig.ModelScale;
            int   units = Mathf.FloorToInt(BerthLenM / CaissonPitchM);   // 나눗셈은 실척 m 끼리
            float run   = pitch * units;
            float wallH = PortConfig.QuayWallHeightMeters;
            float scale = FbxScaleByHeight(fbx, wallH);
            float x     = -PortConfig.ApronWidthMeters * 0.5f * StsConfig.ModelScale;  // 가장자리 X0 → 육지쪽
            float y     = -wallH * StsConfig.ModelScale;                               // 데크 윗면을 y=0 으로

            var root = NewRoot("Quay_Caisson");
            for (int i = 0; i < units; i++)
                Place(fbx, root, "Quay_CaissonUnit",
                      new Vector3(x, y, -run * 0.5f + pitch * (i + 0.5f)), scale);

            // 걷는 면·컨테이너 착지면 — 부두 전체를 감싸는 BoxCollider 1개.
            //   FBX 는 addColliders:0 로 임포트되므로 콜라이더가 하나도 안 생긴다. 없으면 플레이어가
            //   부두를 뚫고 떨어지고 컨테이너도 안 얹힌다. 유닛마다 MeshCollider 를 다는 대신
            //   직육면체 하나로 덮는다 — 케이슨이 실제로 직육면체라 형상 오차가 0이다.
            var col = Undo.AddComponent<BoxCollider>(root.gameObject);
            col.size   = new Vector3(PortConfig.ApronWidthMeters, wallH, BerthLenM) * StsConfig.ModelScale;
            col.center = new Vector3(x, y * 0.5f, 0f);

            Done(root, $"케이슨 {units}함 · 피치 {CaissonPitchM:F0}m · 총 {run * StsConfig.InvModelScale:F1}m · " +
                       $"에이프런 {PortConfig.ApronWidthMeters:F0}m · 안벽고 {wallH:F0}m" +
                       $"(코핑 {StsConfig.QuayDeckAboveSeaMeters:F0} + 수심 {PortConfig.WaterDepthMeters:F0}) · scale {scale:F4}");
        }

        /// <summary>주행 레일 2줄 — 게이지는 StsConfig.LegGaugeXMeters(18m, Post-Panamax 표준) SSOT,
        /// 안벽 가장자리로부터의 거리는 PortConfig.ApronSeawardM SSOT 를 따른다.
        /// 원점 대칭으로 깔면 바다 +X 규약 때문에 해측 레일이 물 위로 나간다.</summary>
        [MenuItem("Model/FBX/항구/레일 배치 (Quay_Rail)", false, 3)]
        static void PlaceRail()
        {
            if (!BerthReady("레일")) return;
            var fbx = Load(RailFbx, "레일"); if (fbx == null) return;

            float pitch = RailPitchM * StsConfig.ModelScale;
            int   units = Mathf.FloorToInt(BerthLenM / RailPitchM);   // 나눗셈은 실척 m 끼리
            float run   = pitch * units;
            float scale = FbxScaleByHeight(fbx, RailHeightM);
            // X0 = 안벽 가장자리, 바다 +X. 해측 레일은 가장자리에서 육지쪽으로 ApronSeawardM,
            //   육측 레일은 거기서 게이지만큼 더 육지쪽. 둘 다 −X 다.
            float water = -PortConfig.ApronSeawardM * StsConfig.ModelScale;
            float land  = water - StsConfig.LegGaugeXMeters * StsConfig.ModelScale;

            var root = NewRoot(StsPartNames.QuayRail);
            foreach (float x in new[] { water, land })
                for (int i = 0; i < units; i++)
                    Place(fbx, root, StsPartNames.QuayRail,
                          new Vector3(x, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"레일 2줄 × {units}유닛 · 게이지 {StsConfig.LegGaugeXMeters:F0}m · " +
                       $"해측 −{PortConfig.ApronSeawardM:F0}m/육측 −{PortConfig.ApronSeawardM + StsConfig.LegGaugeXMeters:F0}m · " +
                       $"피치 {RailPitchM:F0}m · 총 {run * StsConfig.InvModelScale:F1}m · scale {scale:F4}");
        }

        // ── 공용 ──

        /// <summary>안벽 길이가 아직 안 정해졌으면 배치를 막는다. 옛 값을 되살려 조용히 쓰는 것보다,
        /// 멈추고 새 숫자를 요구하는 편이 낫다(오너 지시 2026-09-07).</summary>
        static bool BerthReady(string label)
        {
            if (BerthLenM > 0f) return true;
            EditorUtility.DisplayDialog(label + " 배치",
                "항구 치수가 아직 정해지지 않았습니다.\n\n" +
                "QuayPartsPlacer.BerthLenM (안벽 길이, 실척 m) 에 값을 넣어주세요.\n" +
                "기존 값(344m)은 오너 지시로 삭제했습니다.", "확인");
            return false;
        }

        static GameObject Load(string path, string label)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (fbx == null)
                EditorUtility.DisplayDialog(label + " 배치",
                    $"FBX를 찾을 수 없습니다:\n{path}\n\n유니티 창을 한 번 포커스해 임포트되게 하세요.", "확인");
            return fbx;
        }

        /// <summary>부두 루트 — 없으면 만든다.
        /// StsCraneCreator(레일 정렬)·RtgCraneCreator(야드 배치)·RtgCraneFbxPlacer(지면)·
        /// GantryRangeFit(주행범위)·ShipBerthMenu(접안 앵커) 5곳이 전부
        /// GameObject.Find(StsPartNames.QuayGround) 로 부두를 찾는다. 부재를 씬 루트에
        /// 흩어놓으면 부두가 실제로 있어도 아무도 못 찾는다.</summary>
        static Transform QuayRoot()
        {
            var go = GameObject.Find(StsPartNames.QuayGround);
            if (go == null)
            {
                go = new GameObject(StsPartNames.QuayGround);
                Undo.RegisterCreatedObjectUndo(go, "Create " + StsPartNames.QuayGround);
            }
            return go.transform;
        }

        /// <summary>같은 이름의 기존 그룹을 지우고 Quay_Ground 아래에 새로 만든다
        /// — 두 번 눌러도 겹쳐 쌓이지 않게.</summary>
        static Transform NewRoot(string name)
        {
            var prev = GameObject.Find(name);
            if (prev != null) Undo.DestroyObjectImmediate(prev);
            var root = new GameObject(name).transform;
            root.SetParent(QuayRoot(), worldPositionStays: false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Place " + name);
            return root;
        }

        static void Place(GameObject fbx, Transform parent, string name, Vector3 localPos, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;      // FBX 원점 = 바닥·길이 중앙
            go.transform.localScale    = Vector3.one * scale;
        }

        static void Done(Transform root, string msg)
        {
            Selection.activeGameObject = root.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[항구] {root.name} — {msg}");
        }

        /// <summary>FBX 인스턴스를 '실척 높이 × ModelScale' 로 맞추는 배율. FBX 단위계(m/cm)를 몰라도
        /// 측정으로 수렴하므로 임포트 설정이 바뀌어도 안 깨진다. 항구 부재 공통.</summary>
        static float FbxScaleByHeight(GameObject fbx, float realHeightM)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            probe.transform.localScale = Vector3.one;
            float h = RtgCraneFbxPlacer.CombinedBounds(probe).size.y;   // 기존 헬퍼 재사용
            Object.DestroyImmediate(probe);
            return h > 1e-5f ? realHeightM * StsConfig.ModelScale / h : 1f;
        }
    }
}
#endif
