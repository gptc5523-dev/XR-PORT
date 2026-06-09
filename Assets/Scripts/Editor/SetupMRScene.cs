using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public static class SetupMRScene
{
    [MenuItem("Container/MR 씬 셋업")]
    public static void Setup()
    {
        int added = 0;

        var existingSession = Object.FindFirstObjectByType<ARSession>();
        if (existingSession == null)
        {
            var sessionGO = new GameObject("AR Session");
            sessionGO.AddComponent<ARSession>();
            Undo.RegisterCreatedObjectUndo(sessionGO, "Setup MR Scene");
            Debug.Log("[SetupMRScene] AR Session 생성");
            added++;
        }

        var xrOrigin = Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null)
        {
            var planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            if (planeManager == null)
            {
                Undo.AddComponent<ARPlaneManager>(xrOrigin.gameObject);
                Debug.Log("[SetupMRScene] ARPlaneManager 추가 to " + xrOrigin.name);
                added++;
            }

            var cam = xrOrigin.Camera != null ? xrOrigin.Camera : xrOrigin.GetComponentInChildren<Camera>();
            if (cam != null)
            {
                Undo.RecordObject(cam, "Setup MR Scene Camera");
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                EditorUtility.SetDirty(cam);
                Debug.Log("[SetupMRScene] Main Camera 배경 -> 투명 (패스스루용)");

                if (cam.GetComponent<ARCameraManager>() == null)
                {
                    Undo.AddComponent<ARCameraManager>(cam.gameObject);
                    Debug.Log("[SetupMRScene] ARCameraManager 추가 to Main Camera");
                    added++;
                }
                var existingBg = cam.GetComponent<ARCameraBackground>();
                if (existingBg != null)
                {
                    Undo.DestroyObjectImmediate(existingBg);
                    Debug.Log("[SetupMRScene] ARCameraBackground 제거 (Quest 패스스루엔 불필요, 노란 사각형 원인)");
                }
            }
        }
        else
        {
            Debug.LogWarning("[SetupMRScene] XR Origin 못 찾음. SampleScene에 'XR Origin Hands (XR Rig)' prefab 배치되어 있는지 확인");
        }

        var existingMgr = Object.FindFirstObjectByType<TablePlacementManager>();
        if (existingMgr == null)
        {
            var mgrGO = new GameObject("TablePlacement");
            mgrGO.AddComponent<TablePlacementManager>();
            Undo.RegisterCreatedObjectUndo(mgrGO, "Setup MR Scene");
            Debug.Log("[SetupMRScene] TablePlacementManager 추가");
            added++;
        }

        var existingFloor = GameObject.Find("VirtualFloor");
        if (existingFloor == null)
        {
            var floor = new GameObject("VirtualFloor");
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            var col = floor.AddComponent<BoxCollider>();
            col.size = new Vector3(100f, 0.1f, 100f);
            Undo.RegisterCreatedObjectUndo(floor, "Setup MR Scene");
            Debug.Log("[SetupMRScene] VirtualFloor 추가 (Y=-0.05, 100x100m invisible collider)");
            added++;
        }

        Debug.Log($"[SetupMRScene] 완료. {added}개 항목 추가됨. 남은 단계: 1) OpenXR Passthrough feature 체크  2) Quest 3 Space Setup");
    }
}
