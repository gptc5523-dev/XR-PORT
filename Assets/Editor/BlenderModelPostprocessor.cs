using UnityEditor;

namespace Sts.Art
{
    /// <summary>
    /// Blender에서 익스포트해 프로젝트로 들어오는 FBX(내 모델 폴더 한정)의 임포트 설정을
    /// 파일이 들어오는 "순간" 자동으로 고정한다. 메뉴 클릭·손설정 없이 매 (재)임포트마다 강제.
    ///
    /// 왜 필요한가: 기존엔 이 설정이 'RTG 크레인 생성' 메뉴(RtgCraneFbxPlacer.EnsureImport)를 눌러야만
    /// 적용됐다. 새 FBX를 넣거나 Blender 구조가 바뀌면 그 메뉴 전까지 설정이 안 잡혀 매번 손으로 다시 잡게 됨.
    /// 텍스처용 PbrTextureImporter와 동일한 발상의 모델판.
    ///
    /// ── 강제하는 것(방향 중립·항상 정답인 것만) ──
    ///   · 애니메이션/카메라/라이트/블렌드셰이프 임포트 OFF (크레인·컨테이너는 정적 형상)
    ///   · 콜라이더 자동 생성 OFF (물리는 스크립트가 붙임)
    ///   · 노멀 = Import (Blender에서 바깥 재계산해 온 노멀을 그대로 받음 → 면 사라짐 방지, 익스포트 체크리스트 원칙)
    ///   · 탄젠트 = CalculateMikk, 정점 용접 ON
    ///   · useFileScale = true / globalScale = 1 (스케일 0.01 튐 방지, 두 파일 다 이미 이 값)
    ///
    /// ── 일부러 안 건드리는 것 ──
    ///   · bakeAxisConversion(축 보정): RTG는 ON, Container는 OFF로 "서로 다른데 각자 잘 돎".
    ///     획일적으로 덮으면 한쪽이 돌아가 버림 → 방향은 Blender 익스포트 단계에서 잡는다.
    ///   · 머티리얼 리맵: 모델별로 이름·개수가 다름(RTG는 12종) → RtgCraneFbxPlacer가 모델별로 처리.
    /// </summary>
    public class BlenderModelPostprocessor : AssetPostprocessor
    {
        // 화이트리스트: 내가 Blender로 만든 모델 폴더만. Unity 샘플 패키지(XR Hands/Interaction 등)는 절대 제외.
        static readonly string[] MyModelRoots =
        {
            "Assets/Crane/Models/",
            "Assets/Container/Models/",
        };

        void OnPreprocessModel()
        {
            if (!IsMyBlenderModel(assetPath))
                return;

            var mi = (ModelImporter)assetImporter;

            // 스케일: 실척 1:1 그대로 받기 (두 기존 FBX와 동일 값 → 변화 없음, 신규 파일 안전)
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

            // ※ bakeAxisConversion·머티리얼 리맵은 여기서 안 건드림 (위 주석 참조).
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
