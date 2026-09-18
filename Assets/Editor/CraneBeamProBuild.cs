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

            int stale = ReimportStaleScripts();
            if (stale > 0) Debug.Log($"[BeamProBuild] 옛 이름으로 남은 스크립트 정보 {stale}개를 다시 가져왔습니다.");

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
            if (stale < 0) Debug.LogWarning("[BeamProBuild] 스크립트 정보 검사를 못 했습니다 — 기기에서 '스크립트 없음' 경고를 볼 것.");
            Debug.Log($"[BeamProBuild] 결과: {s.result}, 시간 {s.totalTime}, 크기 {s.totalSize} bytes, 에러 {s.totalErrors}, 경고 {s.totalWarnings}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>스크립트 정보(MonoScript)에 저장된 네임스페이스가 실제 코드와 다르면 다시 가져온다. 다시 가져온 수(검사 실패면 −1).
        ///   ★ 2026-09-18 첫 Beam Pro 빌드에서 씬의 스크립트가 전부 '없음' 이 됐다 — APK 의 globalgamemanagers 에
        ///     옛 이름 `Container.Crane` 만 있고 `AIXRCrane` 은 0. 예전 Quest(안드로이드) 빌드 때 만들어진 안드로이드용
        ///     스크립트 정보가 이름 변경(2bb9bf4) 뒤에도 그대로 남아 있었다. 씬은 GUID 로 스크립트를 찾지만 빌드는 이 이름표로
        ///     클래스를 붙이므로, 이름표가 옛것이면 '스크립트 없음'·"serialization layout" 이 난다.
        ///     평면 모드·모바일 화면·머리 추적처럼 코드가 스스로 띄우는 것만 멀쩡해 보여서 알아채기 어렵다.</summary>
        static int ReimportStaleScripts()
        {
            try
            {
                var stale = new System.Collections.Generic.List<string>();
                foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    var cls = ms != null ? ms.GetClass() : null;
                    if (cls == null) continue;
                    var ns = new SerializedObject(ms).FindProperty("m_Namespace");
                    if (ns != null && ns.stringValue != (cls.Namespace ?? "")) stale.Add(path);
                }
                if (stale.Count == 0) return 0;
                AssetDatabase.StartAssetEditing();
                try { foreach (var p in stale) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.Refresh();
                return stale.Count;
            }
            catch (System.Exception e) { Debug.LogWarning($"[BeamProBuild] 스크립트 정보 검사 실패: {e.Message}"); return -1; }
        }
    }
}
#endif
