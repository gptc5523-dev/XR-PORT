using UnityEditor;

namespace Sts.Art
{
    /// <summary>Blender FBX(내 모델 폴더 한정) 임포트 시 방향 중립·항상 정답인 설정만 자동 고정
    /// (애니메이션/콜라이더 OFF, 노멀=Import·탄젠트 MikkTSpace, useFileScale=true). bakeAxisConversion·머티리얼 리맵은 모델마다 달라 안 건드림.</summary>
    public class BlenderModelPostprocessor : AssetPostprocessor
    {
        // 화이트리스트: 내가 Blender로 만든 모델 폴더만. 컨테이너 폴더는 ContainerModelPostprocessor 단독(두 개가 같은 폴더를 반대로 설정하던 충돌, WBS 9.7).
        static readonly string[] MyModelRoots =
        {
            "Assets/Crane/Models/",
        };

        void OnPreprocessModel()
        {
            if (!IsMyBlenderModel(assetPath))
                return;

            var mi = (ModelImporter)assetImporter;

            // 스케일: 실척 1:1 그대로 받기
            mi.useFileScale = true;
            mi.globalScale  = 1f;

            // 정적 형상: 불필요한 데이터 임포트 차단
            mi.importAnimation   = false;
            mi.animationType     = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.importCameras     = false;
            mi.importLights      = false;
            mi.addCollider       = false;

            // 노멀/탄젠트: 면 사라짐 방지의 핵심 (Blender서 바깥 재계산 → 여기선 Import)
            mi.importNormals  = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.weldVertices   = true;
        }

        static bool IsMyBlenderModel(string path)
        {
            foreach (var root in MyModelRoots)
                if (path.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
