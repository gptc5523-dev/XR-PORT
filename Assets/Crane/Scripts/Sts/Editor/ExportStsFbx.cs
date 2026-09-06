// STS 크레인 → FBX 익스포트 (배치모드 전용) — Blender 이관용.
// 실행: Unity -batchmode -nographics -projectPath <repo>
//       -executeMethod Container.Crane.Sts.EditorTools.ExportStsFbx.Run
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Formats.Fbx.Exporter;

namespace Container.Crane.Sts.EditorTools
{
    public static class ExportStsFbx
    {
        public static void Run()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = StsCraneCreator.Create(Vector3.zero);

                var rends = root.GetComponentsInChildren<Renderer>();
                var b = new Bounds(root.transform.position, Vector3.zero);
                bool first = true;
                foreach (var r in rends)
                {
                    if (first) { b = r.bounds; first = false; }
                    else b.Encapsulate(r.bounds);
                }
                // Blender 임포트 후 축·스케일 검증용 실측치 (Unity: Y-up, 1u = 1m)
                Debug.Log($"[ExportStsFbx] renderers={rends.Length} " +
                          $"size=({b.size.x:F3},{b.size.y:F3},{b.size.z:F3}) " +
                          $"center=({b.center.x:F3},{b.center.y:F3},{b.center.z:F3})");

                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Export");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "STS_Crane.fbx");
                var opts = new ExportModelOptions
                {
                    ExportFormat = ExportFormat.Binary,   // Blender는 ASCII FBX 거부 — 바이너리 필수
                    ModelAnimIncludeOption = Include.Model,
                };
                string res = ModelExporter.ExportObjects(path, new Object[] { root }, opts);
                Debug.Log("[ExportStsFbx] export=" + res);
                EditorApplication.Exit(string.IsNullOrEmpty(res) ? 1 : 0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ExportStsFbx] FAIL " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
