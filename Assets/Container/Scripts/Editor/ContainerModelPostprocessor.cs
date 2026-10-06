#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.EditorTools
{
    /// <summary>final_4 컨테이너 FBX·텍스처 임포트 규약. FBX 는 Blender 에서 이미 1/24 로 구워지므로 globalScale = 1(또 걸면 1/576).
    /// 축: X = 길이(도어 +X) · Y = 높이 · Z = 폭, 피봇 = 바닥면 중앙(ProceduralContainerMesh 와 같다).</summary>
    public sealed class ContainerModelPostprocessor : AssetPostprocessor
    {
        public const string ModelDir   = "Assets/Container/Models/";
        public const string TextureDir = "Assets/Container/Textures/";
        public const string MaterialDir = "Assets/Container/Materials/Final4/";

        static bool IsContainerModel(string path) =>
            path.StartsWith(ModelDir) && path.EndsWith(".fbx") && !path.Contains("(old)");

        static bool IsContainerTexture(string path) => path.StartsWith(TextureDir);

        void OnPreprocessModel()
        {
            if (!IsContainerModel(assetPath)) return;
            var imp = (ModelImporter)assetImporter;

            // FBX 가 이미 1/24 로 구워져 있다 → 여기서 또 줄이지 않는다.
            imp.useFileScale = true;
            imp.globalScale  = 1f;

            imp.importNormals  = ModelImporterNormals.Import;      // Blender 에서 정리한 셰이딩 유지
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.importBlendShapes = false;
            imp.importCameras     = false;
            imp.importLights      = false;
            imp.addCollider       = false;
            imp.importAnimation   = false;
            imp.importVisibility  = false;
            imp.animationType     = ModelImporterAnimationType.None;

            imp.meshCompression   = ModelImporterMeshCompression.Off;
            imp.isReadable        = false;   // 런타임 메시 읽기 불필요 → CPU 사본 제거(메모리 절반)
            imp.optimizeMeshVertices  = true;
            imp.optimizeMeshPolygons  = true;
            imp.weldVertices      = true;    // 노멀을 Import 하므로 위치·노멀·UV 가 모두 같은 정점만 합친다 — 하드에지 유지.
            imp.generateSecondaryUV = false; // 라이트맵 미사용

            // 머티리얼은 OnAssignMaterialModel 이 프로젝트 에셋으로 연결한다.
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation   = ModelImporterMaterialLocation.InPrefab;
        }

        /// <summary>FBX 머티리얼 이름(Blender 재질명)을 프로젝트 머티리얼 에셋으로 치환. 없으면 null(Unity 기본 처리).</summary>
        Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!IsContainerModel(assetPath)) return null;
            string path = MaterialDir + material.name + ".mat";
            return AssetDatabase.LoadAssetAtPath<Material>(path);   // 없으면 null
        }

        void OnPreprocessTexture()
        {
            if (!IsContainerTexture(assetPath)) return;
            var imp = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            if (file.EndsWith("_Normal"))
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.sRGBTexture = false;
            }
            else if (file.EndsWith("_MetalSmooth"))
            {
                // R = metallic, A = smoothness, raw(linear) — URP _MetallicGlossMap 규약.
                imp.textureType        = TextureImporterType.Default;
                imp.sRGBTexture        = false;
                imp.alphaSource        = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
            }
            else // _BaseColor 등
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
            }

            imp.mipmapEnabled = true;
            imp.wrapMode      = TextureWrapMode.Repeat;
            imp.filterMode    = FilterMode.Bilinear;
            imp.anisoLevel    = 4;
        }
    }
}
#endif
