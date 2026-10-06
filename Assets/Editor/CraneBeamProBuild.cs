#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

namespace AIXRCrane.EditorTools
{
    /// <summary>Beam Pro(XREAL 안경) 안드로이드 빌드(XR 없이 평면·모바일·머리 추적). CLI: -executeMethod …CraneBeamProBuild.BuildApk.
    ///   ★ 빌드 중에만 OpenXR 로더(Quest 용)를 뺀다 — 두면 XR 탐색 실패·Quest 전용 매니페스트. 끝나면 복원.</summary>
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
                // 시험 설치는 디버그 키 — user.keystore 비밀번호가 설정에 없어 배치 빌드 서명이 실패한다.
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

        /// <summary>MonoScript 전부 재임포트, 가져온 수 반환(실패 −1). ★ 옛 네임스페이스 스크립트 정보가 남으면 기기에서 씬 스크립트 '없음'.
        ///   ★ 골라서 하면 못 잡는다(옛 이름은 GetClass()=null 이라 건너뜀) — 그래서 전부. 결과는 CheckApk 로 확인.</summary>
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

        /// <summary>APK 에 옛 네임스페이스가 남았는지 검사(있으면 기기에서 스크립트 '없음').
        ///   LZ4 라도 첫 등장 문자열은 날것으로 남아 반드시 한 번은 보인다.</summary>
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
