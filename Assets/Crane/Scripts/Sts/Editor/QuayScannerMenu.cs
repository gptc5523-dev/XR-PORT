#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>부두 컨테이너 인식을 에디트 모드에서 즉시 실행(Play 불필요). 스캐너가 없으면 만든다.</summary>
    public static class QuayScannerMenu
    {
        /// <summary>부두 컨테이너 인식(스캔)을 실행. 스캐너가 없으면 만든다. 인식된 개수를 반환.
        /// 메뉴에서 빠졌고(크레인 생성 시 자동 실행), 다른 에디터 코드에서 호출 가능하도록 public.</summary>
        public static int ScanNow(bool select = true)
        {
            var scanner = Object.FindFirstObjectByType<QuayContainerScanner>();
            if (scanner == null)
            {
                var go = new GameObject("QuayContainerScanner");
                Undo.RegisterCreatedObjectUndo(go, "부두 스캐너 생성");
                scanner = go.AddComponent<QuayContainerScanner>();
            }
            int n = scanner.Scan();
            if (select) Selection.activeGameObject = scanner.gameObject;
            SceneView.RepaintAll();
            Debug.Log($"[부두 인식] 스캔 완료 — {n}개. Scene 뷰에 등급색 박스+라벨이 표시됩니다(콘솔 표 참조).");
            return n;
        }

        // 크레인 찾기(선택 무관) — 없으면 null.
        static GameObject FindCrane()
        {
            var go = Selection.activeGameObject;
            var crane = go != null ? go.GetComponent<StsCrane>() : null;
            if (crane == null) crane = Object.FindFirstObjectByType<StsCrane>();
            return crane != null ? crane.gameObject : null;
        }

        // ───────── 양하 시나리오 부착 (원클릭 복원 2026-06-22) ─────────
        //   배→항구 양하 시나리오(CraneUnloadScenario)를 크레인에 한 번에 붙인다. Play하면 갠트리 주행범위를
        //   스스로 계산(GantryRangeFit)하고 윗단부터 최대 maxContainers개를 육지 야드에 적치한다.
        //   클래스명이 바뀌어(CraneYardScenario→CraneUnloadScenario) 크레인에 'Missing Script'가 남아 있으면 함께 정리한다.
        const string UnloadMenu = "Object/크레인/STS 크레인 양하 시나리오 부착";

        [MenuItem(UnloadMenu, false, 20)]
        static void AttachUnload()
        {
            var craneGo = FindCrane();
            if (craneGo == null) { EditorUtility.DisplayDialog("양하 시나리오", "씬에 StsCrane(크레인)이 없습니다.", "확인"); return; }

            // 옛 시나리오가 'Missing Script'(클래스 삭제됨)로 남아 있으면 제거 — Inspector에서 안 보이던 잔재 정리.
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(craneGo);

            if (craneGo.GetComponent<CraneUnloadScenario>() == null)
                Undo.AddComponent<CraneUnloadScenario>(craneGo);
            EditorUtility.SetDirty(craneGo);
            Selection.activeGameObject = craneGo;
            Debug.Log($"[양하] 부착 완료 — Play 하면 갠트리 주행범위를 레일에 맞춰 자동 계산한 뒤 윗단부터 적치합니다." +
                      (removed > 0 ? $" (옛 Missing 스크립트 {removed}개 정리)" : "") +
                      " 처리 개수는 CraneUnloadScenario의 maxContainers로 조절(기본 10, 0 이하=전량).");
        }

        [MenuItem(UnloadMenu, true)]
        static bool AttachUnloadValidate() => Object.FindFirstObjectByType<StsCrane>() != null;

        // [메뉴 삭제 2026-06-17] '직접조종 복귀 (자동 시나리오 분리)' 메뉴 제거(오너 지시).
        //   시나리오는 Play 종료 시 OnDisable에서 VR을 자동 복귀하고, Inspector에서 컴포넌트를 떼면 된다.
        //   PLC 분리는 Inspector에서 PlcBridge 제거(OnDisable이 PlcDriven 자동 복원).
    }
}
#endif
