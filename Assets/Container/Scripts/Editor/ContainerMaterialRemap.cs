#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.EditorTools
{
    /// <summary>컨테이너 FBX 머티리얼 리맵 — 내장 머티리얼 대신 Final4 의 .mat 을 쓰게 한다.
    /// FBX 내장본은 무텍스처라 근거리(LOD0)에서 컨테이너가 새까맣게 보이는 문제를 이름 매칭으로 막는다.</summary>
    internal static class ContainerMaterialRemap
    {
        const string ModelDir = "Assets/Container/Models";
        const string MatDir   = "Assets/Container/Materials/Final4";

        [MenuItem("Model/FBX/컨테이너/머티리얼 리맵 (Final4)", priority = 40)]
        static void Remap()
        {
            var log = new List<string>();
            int files = 0, mapped = 0, missing = 0;

            foreach (var path in Directory.GetFiles(ModelDir, "*.fbx").OrderBy(p => p))
            {
                if (AssetImporter.GetAtPath(path) is not ModelImporter imp) continue;

                // 내장 머티리얼은 FBX 의 서브에셋으로 들어온다 — 거기서 이름을 얻는다.
                var names = AssetDatabase.LoadAllAssetsAtPath(path)
                                         .OfType<Material>().Select(m => m.name)
                                         .Distinct().OrderBy(n => n).ToList();
                if (names.Count == 0) continue;

                files++;
                int hit = 0;
                var miss = new List<string>();
                foreach (var n in names)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (mat == null) { miss.Add(n); missing++; continue; }
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
                    hit++; mapped++;
                }
                imp.SaveAndReimport();

                log.Add($"  {Path.GetFileName(path),-26} 리맵 {hit}/{names.Count}" +
                        (miss.Count > 0 ? $"  · 없음: {string.Join(", ", miss)}" : ""));
            }

            Debug.Log($"[컨테이너] 머티리얼 리맵 — FBX {files}개 · 연결 {mapped}개 · 누락 {missing}개\n"
                      + string.Join("\n", log));
        }
    }
}
#endif
