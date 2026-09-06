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

    }
}
#endif
