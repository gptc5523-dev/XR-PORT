#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ContainerProject.EditorTools
{
    /// <summary>컨테이너 FBX 머티리얼 리맵 — 내장본 대신 Final4 의 .mat 을 쓰게 한다.
    ///
    /// 왜 필요한가 (2026-09-08 실측):
    ///   컨테이너 FBX 는 externalObjects 가 비어 있어 Unity 가 FBX 내장 머티리얼을 만든다.
    ///   그 내장본은 base 0.12 무텍스처라 근거리(LOD0)에서 컨테이너가 새까맣게 보였다.
    ///     LOD0 Body  base 0.12 · map 없음                     ← 내장본
    ///     LOD1 Body  base 1.00 · map Container_Body_BaseColor ← Final4/Body.mat
    ///   이름은 양쪽이 같으므로 이름으로 리맵하면 둘이 같은 외형이 된다.
    ///
    /// 오너 방침 — 메뉴는 무조건 Model > FBX 아래.</summary>
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
