#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 시작 위치 마커(CranePlayerStartPoint)를 만들거나 선택해 주는 메뉴.
    /// 선택한 오브젝트(예: 레인)가 있으면 그 위치로 마커를 옮겨 생성 → 끌어다 미세조정 후 파란 Z축을 항구쪽으로.
    /// </summary>
    public static class CranePlayerStartPointMenu
    {
        const string MarkerName = "PlayerStartPoint";

        // 보류(2026-06-04): 플레이어 리그 스케일 문제(1/24 모델 vs 실척 리그)로 시작 위치 기능 전체 보류 중 —
        //   메뉴를 숨긴다. 재개 시 아래 [MenuItem] 주석만 풀면 됨.
        // [MenuItem("Container/플레이어 시작 지점 생성", false, 3)]
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
