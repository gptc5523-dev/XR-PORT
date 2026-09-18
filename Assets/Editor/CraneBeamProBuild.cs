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
            Debug.Log($"[BeamProBuild] 결과: {s.result}, 시간 {s.totalTime}, 크기 {s.totalSize} bytes, 에러 {s.totalErrors}, 경고 {s.totalWarnings}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
