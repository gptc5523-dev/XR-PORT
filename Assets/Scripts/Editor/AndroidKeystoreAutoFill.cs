#if UNITY_EDITOR
using UnityEditor;

namespace AIXRCrane.EditorTools
{
    /// <summary>도메인 리로드마다 비는 Android 키스토어 비밀번호를 자동으로 채운다.
    /// 주의: 비밀번호가 평문으로 들어간다 — 외부 배포 빌드면 빼거나 환경변수로.</summary>
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
