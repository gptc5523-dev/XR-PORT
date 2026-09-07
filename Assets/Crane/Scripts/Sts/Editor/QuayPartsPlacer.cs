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

        // 부재 실척 높이 — 블렌더 빌드 스크립트와 쌍으로 유지한다(문서/스크립트/부두연석_유닛_빌드.py).
        const float CurbHeightM    = 0.528f;   // 단면 0.72W × 0.528H
        const float BollardHeightM = 1.368f;   // 기둥 1.08 + 갓 0.288
        const float RailHeightM    = 0.192f;   // DIN 536 A120 170 + 소플레이트 22
                                               //   = StsConfig.RailSectionH(0.008u) × 24. SSOT 일치

        // ═══ 항구 치수 SSOT — 오너가 새로 계산해 넣는다 (지시 2026-09-07 "기존 항구 사이즈가 있다면 삭제") ═══
        //   0 이면 배치를 거부한다. 여기 숫자만 채우면 연석·계선주가 한꺼번에 따라온다.
        //   다른 곳에 안벽 길이를 박지 말 것.
        const float BerthLenM = 0f;           // ← 안벽(선석) 길이, 실척 m. 미정.
        //
        // 종전 산식 — 비활성. 새 값이 정해지면 지우거나, 같은 방식이면 주석을 풀어 쓴다.
        //   (using Container.Ship; 도 같이 되살려야 함)
        // const float BerthClearanceM = 25f;                                     // 선수·선미 각 여유
        // static float BerthLenM => ShipConfig.LoaMeters + BerthClearanceM * 2f; // 294 + 50 = 344m

        // 부재 배치 간격 — 부재 자체 규격에서 나온 값이라 항구 사이즈와 무관하게 유지.
        const float CurbPitchM     = 4.0f;    // 프리캐스트 유닛 피치(유닛 3.985 + 줄눈 0.015) = FBX 규격
        const float BollardGapM    = 20f;     // 계선주 간격 — 미정이면 오너 값으로 교체
        const float BollardInsetM  = 1.08f;   // 안벽 가장자리 → 육지쪽 계선주 중심 — 미정이면 교체
        const float RailPitchM     = 12.0f;   // 레일 정척 12m + 신축이음 10mm = FBX 규격

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

            var root = NewRoot("Quay_Curb");
            for (int i = 0; i < units; i++)
                Place(fbx, root, "Quay_CurbUnit",
                      new Vector3(0f, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"연석 {units}유닛 · 피치 {CurbPitchM:F1}m · 총 {run * StsConfig.InvModelScale:F1}m " +
                       $"(안벽 {BerthLenM:F0}m) · scale {scale:F4}");
        }

        [MenuItem("Model/FBX/항구/계선주 배치 (Quay_Bollard)", false, 2)]
        static void PlaceBollard()
        {
            if (!BerthReady("계선주")) return;
            var fbx = Load(BollardFbx, "계선주"); if (fbx == null) return;

            float len = BerthLenM * StsConfig.ModelScale;
            int   n   = Mathf.Max(1, Mathf.FloorToInt(BerthLenM / BollardGapM));   // 구간 수 → 계선주 n+1개
            float scale = FbxScaleByHeight(fbx, BollardHeightM);
            float x   = -BollardInsetM * StsConfig.ModelScale;       // 육지쪽(−X)

            var root = NewRoot("Quay_Bollard");
            for (int i = 0; i <= n; i++)
                Place(fbx, root, "Quay_Bollard",
                      new Vector3(x, 0f, -len * 0.5f + len * i / n), scale);

            Done(root, $"계선주 {n + 1}개 · 간격 {len / n * StsConfig.InvModelScale:F1}m " +
                       $"(안벽 {BerthLenM:F0}m) · 안쪽 {BollardInsetM:F2}m · scale {scale:F4}");
        }

        /// <summary>주행 레일 2줄 — 게이지는 StsConfig.LegGaugeXMeters(18m, Post-Panamax 표준) SSOT 추종.
        /// 원점 대칭으로 깐다. 안벽 위치가 정해지면 루트를 통째로 옮기면 된다(에이프런 오프셋은 항구 치수).</summary>
        [MenuItem("Model/FBX/항구/레일 배치 (Quay_Rail)", false, 3)]
        static void PlaceRail()
        {
            if (!BerthReady("레일")) return;
            var fbx = Load(RailFbx, "레일"); if (fbx == null) return;

            float pitch = RailPitchM * StsConfig.ModelScale;
            int   units = Mathf.FloorToInt(BerthLenM / RailPitchM);   // 나눗셈은 실척 m 끼리
            float run   = pitch * units;
            float scale = FbxScaleByHeight(fbx, RailHeightM);
            float halfGauge = StsConfig.LegGaugeXMeters * 0.5f * StsConfig.ModelScale;

            var root = NewRoot(StsPartNames.QuayRail);
            foreach (float x in new[] { -halfGauge, halfGauge })
                for (int i = 0; i < units; i++)
                    Place(fbx, root, StsPartNames.QuayRail,
                          new Vector3(x, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"레일 2줄 × {units}유닛 · 게이지 {StsConfig.LegGaugeXMeters:F0}m · " +
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

        /// <summary>같은 이름의 기존 루트를 지우고 새로 만든다 — 두 번 눌러도 겹쳐 쌓이지 않게.</summary>
        static Transform NewRoot(string name)
        {
            var prev = GameObject.Find(name);
            if (prev != null) Undo.DestroyObjectImmediate(prev);
            var root = new GameObject(name).transform;
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
