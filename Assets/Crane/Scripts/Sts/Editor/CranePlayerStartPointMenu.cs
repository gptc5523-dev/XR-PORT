#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>시작 위치 마커(CranePlayerStartPoint) 생성/선택 메뉴. 선택 오브젝트가 있으면 그 위치에 생성
    /// → 끌어다 미세조정 후 파란 Z축을 항구쪽으로.</summary>
    public static class CranePlayerStartPointMenu
    {
        const string MarkerName = StsPartNames.PlayerStartPoint;

        // 보류(리그 스케일 문제: 1/24 모델 vs 실척 리그) — 메뉴 숨김. 재개 시 아래 주석만 풀 것.
        // [MenuItem("Scene/플레이어 시작 지점 생성", false, 3)]
        public static void Create()
        {
            var existing = Object.FindAnyObjectByType<CranePlayerStartPoint>();
            GameObject go = existing != null ? existing.gameObject : new GameObject(MarkerName);
            if (existing == null)
            {
                go.AddComponent<CranePlayerStartPoint>();
                Undo.RegisterCreatedObjectUndo(go, "Create Player Start Point");
            }

            // 선택한 오브젝트(레인 등)가 있으면 그 위치로 — 마커 자신을 고른 경우는 제외.
            var sel = Selection.activeTransform;
            if (sel != null && sel != go.transform)
            {
                Undo.RecordObject(go.transform, "Place Player Start Point");
                go.transform.position = sel.position;
            }

            Selection.activeGameObject = go;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
            Debug.Log("[PlayerStartPoint] 시작 위치 마커 준비됨 — 씬에서 원하는 레인 끝으로 끌어다 두고, " +
                      "파란 Z축(forward)을 항구(바다)쪽으로 돌리세요. 플레이 시 그 지점·방향에서 시작합니다.");
        }
    }
}
#endif
