#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.EditorTools
{
    /// <summary>도메인 리로드마다 비는 Android 키스토어 비밀번호를 채운다 — 환경변수 XRCRANE_KEYSTORE_PASS, 없으면 루트 user.keystore.pass(git 제외).</summary>
    [InitializeOnLoad]
    public static class AndroidKeystoreAutoFill
    {
        const string KeyaliasName  = "container";
        const string PassFile = "user.keystore.pass";

        static AndroidKeystoreAutoFill()
        {
            // 도메인 리로드 직후 PlayerSettings 가 준비된 뒤에 적용.
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            string pass = System.Environment.GetEnvironmentVariable("XRCRANE_KEYSTORE_PASS");
            if (string.IsNullOrEmpty(pass) && File.Exists(PassFile)) pass = File.ReadAllText(PassFile).Trim();
            if (string.IsNullOrEmpty(pass))
            {
                Debug.LogWarning($"[Keystore] 비밀번호 없음 — XRCRANE_KEYSTORE_PASS 또는 프로젝트 루트 {PassFile} 에 넣어야 Android 빌드가 서명된다.");
                return;
            }
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keyaliasName = KeyaliasName;
            PlayerSettings.Android.keystorePass = pass;
            PlayerSettings.Android.keyaliasPass = pass;
        }
    }
}
#endif
