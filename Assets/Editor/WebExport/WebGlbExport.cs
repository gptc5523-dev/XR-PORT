using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using GLTFast;
using GLTFast.Export;

/// <summary>
/// 웹 전시용 GLB 내보내기 (glTFast).
/// 배치: Unity -batchmode -executeMethod WebGlbExport.ExportRtgBatch
/// </summary>
public static class WebGlbExport
{
    const string RtgFbxPath = "Assets/Crane/Models/RTG_Crane.fbx";
    const string OutRelPath = "_incoming/port-vr/rtg-crane.glb"; // 프로젝트 루트 기준(Assets 밖)

    // 실척 게이트 — 근거: 전고 ≈25.0m(호이스트 드럼 상단 z24.455 + 플랜지, 동적데이터 문서),
    // 최대폭 ≈27.25m(스팬 23.6 + 다리 + 케이블릴 헤드 x13.13, 실물 레퍼런스·실측 교차)
    const float MinHeight = 23.5f, MaxHeight = 26.5f;
    const float MinWidth = 26f, MaxWidth = 29f;

    [MenuItem("Tool/웹 내보내기/RTG GLB")]
    public static void ExportRtgMenu() => ExportRtg(false);

    public static void ExportRtgBatch() => ExportRtg(true);

    /// <summary>마커 파일이 있으면 도메인 리로드 직후 1회 자동 내보내기 (열린 에디터 원격 트리거용).</summary>
    [InitializeOnLoadMethod]
    static void AutoRunIfRequested()
    {
        EditorApplication.delayCall += () =>
        {
            string marker = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "_incoming/port-vr/.rtg_export_request");
            if (!File.Exists(marker)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(marker); // 실패해도 재시도 루프에 빠지지 않도록 먼저 제거
            Debug.Log("[WebGlbExport] 마커 감지 — RTG GLB 자동 내보내기 시작");
            ExportRtg(false);
        };
    }

    static async void ExportRtg(bool batch)
    {
        int code = 1;
        GameObject inst = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RtgFbxPath);
            if (prefab == null) throw new Exception("FBX 로드 실패: " + RtgFbxPath);

            inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = Vector3.zero;

            var rends = inst.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) throw new Exception("렌더러 없음");

            Bounds Measure()
            {
                var bb = rends[0].bounds;
                foreach (var r in rends) bb.Encapsulate(r.bounds);
                return bb;
            }

            // 업축 후보: 프리팹 기본 회전 → Z-up 보정(±90°X). 게이트 통과하는 첫 후보 채택.
            // (FBX 원본이 Z-up이라 identity로 세우면 눕는다 — 2026-08-27 실측 27.25 x 12.43 x 25.04)
            var candidates = new (string label, Quaternion rot)[]
            {
                ("프리팹 기본", prefab.transform.rotation),
                ("Z-up 보정 -90X", Quaternion.Euler(-90f, 0f, 0f)),
                ("Z-up 보정 +90X", Quaternion.Euler(90f, 0f, 0f)),
            };
            Bounds b = default;
            string picked = null;
            foreach (var c in candidates)
            {
                inst.transform.rotation = c.rot;
                var bb = Measure();
                float h = bb.size.y;
                float w = Mathf.Max(bb.size.x, bb.size.z);
                Debug.Log($"[WebGlbExport] 후보 '{c.label}': {bb.size.x:F3} x {bb.size.y:F3} x {bb.size.z:F3} m");
                if (h >= MinHeight && h <= MaxHeight && w >= MinWidth && w <= MaxWidth)
                {
                    b = bb;
                    picked = c.label;
                    break;
                }
            }
            if (picked == null)
                throw new Exception("실척 게이트 FAIL: 모든 업축 후보 불통과 " +
                                    $"(전고 허용 {MinHeight}~{MaxHeight}, 최대폭 {MinWidth}~{MaxWidth} — 로그의 후보 치수 참조)");
            Debug.Log($"[WebGlbExport] 업축 채택 '{picked}' — 전고 {b.size.y:F2}m, 최대폭 {Mathf.Max(b.size.x, b.size.z):F2}m PASS");

            int tris = 0, meshCount = 0;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                meshCount++;
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                    tris += (int)mf.sharedMesh.GetIndexCount(i) / 3;
            }
            var mats = new HashSet<Material>();
            foreach (var r in rends)
                foreach (var m in r.sharedMaterials)
                    if (m != null) mats.Add(m);
            Debug.Log($"[WebGlbExport] meshes={meshCount}, tris={tris}, mats={mats.Count}");

            // 원점 정렬: 바닥 중심 (요청 스펙: 원점은 모델 중심·바닥 기준)
            inst.transform.position = new Vector3(-b.center.x, -b.min.y, -b.center.z);

            string outAbs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutRelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outAbs));
            if (File.Exists(outAbs)) File.Delete(outAbs);

            var export = new GameObjectExport(new ExportSettings { Format = GltfFormat.Binary });
            export.AddScene(new[] { inst }, "RTG_Crane");
            bool ok = await export.SaveToFileAndDispose(outAbs);

            float mb = File.Exists(outAbs) ? new FileInfo(outAbs).Length / 1024f / 1024f : 0f;
            Debug.Log($"[WebGlbExport] 저장 {(ok ? "성공" : "실패")}: {outAbs} ({mb:F2} MB)");
            code = ok ? 0 : 1;
        }
        catch (Exception e)
        {
            Debug.LogError("[WebGlbExport] 예외: " + e);
            code = 2;
        }
        finally
        {
            if (inst != null) UnityEngine.Object.DestroyImmediate(inst);
            if (batch) EditorApplication.Exit(code);
        }
    }
}
