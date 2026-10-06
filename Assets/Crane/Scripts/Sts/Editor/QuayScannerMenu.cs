#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>부두 컨테이너 인식을 에디트 모드에서 즉시 실행(Play 불필요). 스캐너가 없으면 만든다.</summary>
    public static class QuayScannerMenu
    {
        /// <summary>스캔 실행 후 인식 개수 반환(스캐너 없으면 생성). 크레인 생성 시 자동 호출되므로 메뉴엔 없다.</summary>
        public static int ScanNow(bool select = true)
        {
            var scanner = Object.FindAnyObjectByType<QuayContainerScanner>();
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
