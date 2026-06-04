#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ContainerProject.EditorTools
{
    /// <summary>
    /// Unity 에디터는 보안상 Android 키스토어/키 비밀번호를 프로젝트에 저장하지 않아
    /// 에디터를 새로 켤 때마다 Publishing Settings 비밀번호 칸이 비워진다.
    /// 이 스크립트가 도메인 리로드(에디터 로드/스크립트 재컴파일)마다 비밀번호를
    /// 자동으로 채워, 매번 손으로 입력하지 않고 바로 빌드할 수 있게 한다.
    ///
    /// 주의: 비밀번호가 이 .cs 파일에 평문으로 들어간다. 로컬 전용 프로젝트라 괜찮지만,
    /// 외부로 공유/배포할 빌드라면 이 파일을 빼거나 비밀번호를 환경변수로 옮길 것.
    /// </summary>
    [InitializeOnLoad]
    public static class AndroidKeystoreAutoFill
    {
        const string KeyaliasName  = "container";
        const string KeystorePass  = "container123";
        const string KeyaliasPass  = "container123";

        static AndroidKeystoreAutoFill()
        {
            // 도메인 리로드 직후 PlayerSettings 가 준비된 뒤에 적용.
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keyaliasName = KeyaliasName;
            PlayerSettings.Android.keystorePass = KeystorePass;
            PlayerSettings.Android.keyaliasPass = KeyaliasPass;
        }
    }
}
#endif
