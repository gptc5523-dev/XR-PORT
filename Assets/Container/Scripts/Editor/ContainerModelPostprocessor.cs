#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
// (StsConfig.ModelScale 은 더 이상 쓰지 않는다 — 1/24 는 Blender 익스포트에서 이미 적용됨)

namespace ContainerProject.EditorTools
{
    /// <summary>
    /// final_4 컨테이너 FBX·텍스처 임포트 규약을 코드로 고정한다.
    ///
    /// ★2026-08-12 변경 — FBX 를 Blender 에서 **1/24 로 굽는다**(오너 지시 "fbx 컨테이너 사이즈 1/24 로 줄여줘").
    /// 따라서 여기 globalScale 은 **1** 이다. 1/24 를 또 걸면 1/576 로 줄어든다.
    /// 익스포트 규약 — 루트 EMPTY 에 scale 1/24 · 피봇 바닥면 중앙(z_min → 0)
    ///   · 축 Blender Y(길이)→Unity X(도어 +X) · Z(높이)→Y · X(폭)→Z (axis_forward='-X', axis_up='Z')
    /// 실측(모델 단위) — 20ft 0.25242 × 0.10158 × 0.10796 · 40ft 0.50792 · 45ftHC 0.57142 (= 실척 ÷ 24)
    ///
    /// 종전 설계는 "FBX 는 실척 1:1, 환산은 globalScale 이 전담"이었다. 그 방식은
    /// Assets/Editor/BlenderModelPostprocessor 가 같은 폴더에 globalScale=1 을 강제해 충돌한다
    /// (AssetPostprocessor 실행 순서가 정해져 있지 않아 어느 쪽이 이길지 불확실했다).
    ///
    /// 내보내기 규약(Blender 측 export 스크립트와 짝):
    ///   X = 길이(도어 = +X) · Y = 높이 · Z = 폭 · 피봇 = 바닥면 중앙
    ///   → 기존 ProceduralContainerMesh.ApplyTransform(xIsLength:true, centerPivot:false) 와 동일.
    /// </summary>
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
            imp.importAnimation   = false;
            imp.importVisibility  = false;
            imp.animationType     = ModelImporterAnimationType.None;

            imp.meshCompression   = ModelImporterMeshCompression.Off;
            imp.isReadable        = false;   // 런타임 메시 읽기 불필요 → CPU 사본 제거(메모리 절반)
            imp.optimizeMeshVertices  = true;
            imp.optimizeMeshPolygons  = true;
            imp.weldVertices      = false;   // Blender 에서 이미 정리됨. 켜면 하드에지가 뭉개진다.
            imp.generateSecondaryUV = false; // 라이트맵 미사용

            // 머티리얼은 OnAssignMaterialModel 이 프로젝트 에셋으로 연결한다.
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation   = ModelImporterMaterialLocation.InPrefab;
        }

        /// <summary>
        /// FBX 안의 머티리얼 이름(Blender 재질명)을 프로젝트 머티리얼 에셋으로 치환.
        /// 없으면 null 반환 → Unity 기본 처리. (에셋 생성은 ContainerPrefabBuilder 메뉴가 담당)
        /// </summary>
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
