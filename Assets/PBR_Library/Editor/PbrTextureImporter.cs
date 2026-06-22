using UnityEditor;
using UnityEngine;

namespace Sts.Art
{
    /// <summary>
    /// PBR_Library 아래로 들어오는 ambientCG 텍스처의 import 설정을 파일명 접미사로 자동 구성한다.
    /// - *_NormalGL  : 노멀맵으로 인식 (OpenGL 규약 = Unity 기본)
    /// - *_Roughness / *_Metalness / *_Displacement / *_AmbientOcclusion : 선형 데이터(sRGB off)
    /// - *_Color     : 알베도(sRGB on, 기본값 유지)
    /// 아티스트가 텍스처마다 수동으로 설정을 바꾸지 않도록 하기 위함.
    /// </summary>
    public class PbrTextureImporter : AssetPostprocessor
    {
        const string LibraryRoot = "Assets/PBR_Library/";

        void OnPreprocessTexture()
        {
            if (assetPath.IndexOf(LibraryRoot, System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var importer = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            if (EndsWith(name, "_NormalGL") || EndsWith(name, "_NormalDX"))
            {
                importer.textureType = TextureImporterType.NormalMap;
            }
            else if (EndsWith(name, "_Roughness") || EndsWith(name, "_Metalness") ||
                     EndsWith(name, "_Displacement") || EndsWith(name, "_AmbientOcclusion"))
            {
                // 데이터 맵: 색공간 변환 없이 선형으로 읽어야 함
                importer.sRGBTexture = false;
            }
            // _Color / _Opacity 등은 기본(sRGB) 유지

            // 항만 씬 표면은 대부분 타일링되므로 반복 샘플링 허용
            importer.wrapMode = TextureWrapMode.Repeat;
        }

        static bool EndsWith(string s, string suffix)
            => s.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase);
    }
}
