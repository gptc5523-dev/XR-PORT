#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

namespace AIXRCrane.EditorTools
{
    /// <summary>Beam Pro(XREAL One Pro 안경) 안드로이드 빌드 — XR 없이 뜬다: 평면 모드 → 모바일 관전 화면 → 안경 머리 추적.
    ///   오너 2026-09-18 방법 2(우리 앱을 Beam Pro 에 직접). 안경 IMU 는 Beam Pro 에서도 169.254.2.1:52998 로 읽힌다(실측).
    ///   ★ 빌드하는 동안만 안드로이드의 OpenXR 로더(Quest 용)를 뺀다 — 두면 Beam Pro 에서 XR 을 찾다 실패하고,
    ///     매니페스트에 Quest 전용 항목(VR 인텐트·headtracking 필수)이 들어간다. 끝나면 원래대로 되돌린다(에디터에서 눌러도 안전).
    ///   산출물: Build/BeamPro/AIXRCrane-BeamPro.apk.
    ///   CLI(클론에서): Unity -batchmode -nographics -projectPath &lt;클론&gt; -buildTarget Android -quit
    ///                  -executeMethod AIXRCrane.EditorTools.CraneBeamProBuild.BuildApk</summary>
    public static class CraneBeamProBuild
    {
        const string OutPath = "Build/BeamPro/AIXRCrane-BeamPro.apk";
        const string OpenXrLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("Tool/Beam Pro(XREAL 안경) 안드로이드 빌드")]
        public static void BuildApk()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[BeamProBuild] Build Settings 에 활성 씬이 없습니다.");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            int stale = ReimportAllScripts();
            Debug.Log($"[BeamProBuild] 스크립트 정보 {stale}개를 다시 가져왔습니다(옛 이름 방지).");

            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            var mgr = xr != null ? xr.AssignedSettings : null;
            bool hadOpenXr = mgr != null && mgr.activeLoaders.Any(l => l != null && l.GetType().FullName == OpenXrLoader);
            bool initOnStart = xr != null && xr.InitManagerOnStart;
            bool forceNet = PlayerSettings.Android.forceInternetPermission;
            bool aab = EditorUserBuildSettings.buildAppBundle;
            bool customKey = PlayerSettings.Android.useCustomKeystore;

            BuildReport report;
            try
            {
                if (hadOpenXr) XRPackageMetadataStore.RemoveLoader(mgr, OpenXrLoader, BuildTargetGroup.Android);
                if (xr != null) xr.InitManagerOnStart = false;
                PlayerSettings.Android.forceInternetPermission = true;   // 서버 세션(Netcode)·안경 IMU(TCP)
                EditorUserBuildSettings.buildAppBundle = false;          // adb 로 까는 apk
                // 시험 설치는 디버그 키로 — 프로젝트의 user.keystore(Quest 배포용)는 비밀번호가 설정에 안 남아
                //   배치 빌드가 "Can not sign the application" 으로 1초 만에 멈췄다(2026-09-18).
                PlayerSettings.Android.useCustomKeystore = false;

                Debug.Log($"[BeamProBuild] 빌드 시작 — 씬 {scenes.Length}개 → {OutPath} (OpenXR 로더 {(hadOpenXr ? "잠시 뺌" : "원래 없음")})");
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = OutPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                if (hadOpenXr) XRPackageMetadataStore.AssignLoader(mgr, OpenXrLoader, BuildTargetGroup.Android);
                if (xr != null) xr.InitManagerOnStart = initOnStart;
                PlayerSettings.Android.forceInternetPermission = forceNet;
                EditorUserBuildSettings.buildAppBundle = aab;
                PlayerSettings.Android.useCustomKeystore = customKey;
                AssetDatabase.SaveAssets();
            }

            var s = report.summary;
            bool ok = s.result == BuildResult.Succeeded;
            string why = "";
            if (ok && !CheckApk(OutPath, out why)) { ok = false; Debug.LogError($"[BeamProBuild] ★ {why} — 기기에서 씬 스크립트가 '없음' 이 된다. 설치하지 말 것."); }
            Debug.Log($"[BeamProBuild] 결과: {s.result}{(ok ? "" : " · 검사 실패")}, 시간 {s.totalTime}, 크기 {s.totalSize} bytes, 에러 {s.totalErrors}, 경고 {s.totalWarnings}" + (why != "" ? $" · APK 검사: {why}" : ""));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>스크립트 정보(MonoScript)를 전부 다시 가져온다. 가져온 수(실패면 −1).
        ///   ★ 2026-09-18 Beam Pro 빌드에서 씬의 스크립트가 전부 '없음' 이 됐다 — 기기 로그 "The referenced script
        ///     (Container.Crane.Sts.TrolleyMover) … missing", APK data.unity3d 의 스크립트 표에 옛 네임스페이스.
        ///     예전 Quest(안드로이드) 빌드 때 만들어진 안드로이드용 스크립트 정보가 이름 변경(2bb9bf4) 뒤에도 남아 있었다.
        ///   ★ '어긋난 것만 골라' 다시 가져오면 못 잡는다 — 옛 이름으로 저장된 스크립트는 지금 코드에서 클래스를 못 찾아
        ///     GetClass()=null 이 되고, 비교할 게 없어 건너뛴다(39cd867 이 그래서 0개를 잡고 통과했다). 그래서 전부 한다.
        ///   평면·모바일·머리 추적처럼 코드가 스스로 띄우는 것만 멀쩡해 보여 알아채기 어렵다 — 빌드 뒤 검사(CheckApk)로 막는다.</summary>
        static int ReimportAllScripts()
        {
            try
            {
                var paths = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
                AssetDatabase.StartAssetEditing();
                try { foreach (var p in paths) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return paths.Length;
            }
            catch (System.Exception e) { Debug.LogWarning($"[BeamProBuild] 스크립트 다시 가져오기 실패: {e.Message}"); return -1; }
        }

        /// <summary>빌드된 APK 에 옛 네임스페이스가 남았는지 — 있으면 기기에서 씬 스크립트가 '없음' 이 된다.
        ///   데이터는 LZ4 로 묶여 반복은 안 보이지만, 처음 나오는 문자열은 날것으로 남아 한 번은 반드시 보인다.</summary>
        static bool CheckApk(string apk, out string why)
        {
            var old = System.Text.Encoding.ASCII.GetBytes("Container.Crane.");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(apk))
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith("assets/")) continue;
                    using (var ms = new System.IO.MemoryStream())
                    {
                        using (var z = e.Open()) z.CopyTo(ms);
                        var b = ms.GetBuffer(); int n = (int)ms.Length;
                        for (int i = 0; i + old.Length <= n; i++)
                        {
                            int k = 0; while (k < old.Length && b[i + k] == old[k]) k++;
                            if (k == old.Length) { why = $"{e.FullName} 에 옛 이름 '{System.Text.Encoding.ASCII.GetString(old)}'"; return false; }
                        }
                    }
                }
            why = "옛 이름 없음";
            return true;
        }
    }
}
#endif
