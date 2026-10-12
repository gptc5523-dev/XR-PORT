#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class QuayPartsPlacer
    {
        // ── 공용 ──

        static GameObject Load(string path, string label)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (fbx == null)
            {
                EditorUtility.DisplayDialog(label + " 배치",
                    $"FBX를 찾을 수 없습니다:\n{path}\n\n유니티 창을 한 번 포커스해 임포트되게 하세요.", "확인");
                return null;
            }
            if (EnsureMaterials(path))
                fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);   // 리임포트 후 재로드
            return fbx;
        }

        /// <summary>FBX 의 Blender 머티리얼을 URP/Lit 에셋으로 리맵한다(idempotent).
        /// 이미 전부 걸려 있으면 아무것도 안 하고 false 를 돌려 불필요한 리임포트를 피한다.</summary>
        internal static bool EnsureMaterials(string fbxPath)
        {
            if (!FbxMats.TryGetValue(fbxPath, out var names)) return false;
            if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter mi) return false;

            var already = mi.GetExternalObjectMap()
                            .Where(kv => kv.Key.type == typeof(Material) && kv.Value != null)
                            .Select(kv => kv.Key.name).ToHashSet();
            if (names.All(already.Contains)) return false;

            if (!Directory.Exists(MatDir)) { Directory.CreateDirectory(MatDir); AssetDatabase.Refresh(); }
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var n in names)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), GetOrCreateMat(n));
            mi.SaveAndReimport();
            Debug.Log($"[항구] 머티리얼 리맵 — {Path.GetFileName(fbxPath)} ← {string.Join(", ", names)}");
            return true;
        }

        static Material GetOrCreateMat(string name)
        {
            string path = $"{MatDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var d = Mats.FirstOrDefault(m => m.n == name);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", new Color(d.r, d.g, d.b, 1f));
            mat.SetFloat("_Metallic", d.metal);
            mat.SetFloat("_Smoothness", d.smooth);
            mat.enableInstancing = true;   // 같은 메시+머티리얼 반복이라 인스턴싱이 그대로 먹는다
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>부두 루트 — 없으면 만든다. StsCraneCreator·RtgCraneFbxPlacer·GantryRangeFit·ShipBerthMenu
        /// 5곳이 GameObject.Find(QuayGround)로 부두를 찾으므로, 부재를 씬 루트에 흩어놓으면 아무도 못 찾는다.</summary>
        static Transform QuayRoot()
        {
            var go = GameObject.Find(StsPartNames.QuayGround);
            if (go == null)
            {
                go = new GameObject(StsPartNames.QuayGround);
                Undo.RegisterCreatedObjectUndo(go, "Create " + StsPartNames.QuayGround);
            }
            return go.transform;
        }

        /// <summary>같은 이름의 기존 그룹을 지우고 Quay_Ground 아래에 새로 만든다
        /// — 두 번 눌러도 겹쳐 쌓이지 않게.</summary>
        static Transform NewRoot(string name)
        {
            var prev = GameObject.Find(name);
            if (prev != null) Undo.DestroyObjectImmediate(prev);
            var root = new GameObject(name).transform;
            root.SetParent(QuayRoot(), worldPositionStays: false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Place " + name);
            return root;
        }

        static void Place(GameObject fbx, Transform parent, string name, Vector3 localPos, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;      // FBX 원점 = 바닥·길이 중앙
            go.transform.localScale    = Vector3.one * scale;
        }

        static void Done(Transform root, string msg)
        {
            Selection.activeGameObject = root.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[항구] {root.name} — {msg}");
        }

        /// <summary>FBX 인스턴스를 '실척 높이 × ModelScale' 로 맞추는 배율. FBX 단위계(m/cm)를 몰라도
        /// 측정으로 수렴하므로 임포트 설정이 바뀌어도 안 깨진다. 항구 부재 공통.</summary>
        static float FbxScaleByHeight(GameObject fbx, float realHeightM)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            probe.transform.localScale = Vector3.one;
            float h = SceneUtil.BoundsOrPoint(probe).size.y;
            Object.DestroyImmediate(probe);
            return h > 1e-5f ? realHeightM * StsConfig.ModelScale / h : 1f;
        }
    }
}
#endif
