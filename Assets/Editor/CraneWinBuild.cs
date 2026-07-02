#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Container.EditorTools
{
    /// <summary>
    /// 서버 배포용 Windows(Win64) 빌드 — 헤드리스 NVIDIA→Proton→WiVRn 경로.
    ///   ProjectSettings(Mono 백엔드·Vulkan 전용·Meta OpenXR 기능 OFF)는 이미 구성돼 있어 그대로 따른다.
    ///   산출물: <프로젝트>/Build/Win/Crane.exe  (서버 ~/CraneWin 으로 rsync/scp).
    ///   CLI(에디터 닫고): Unity -quit -batchmode -projectPath . -executeMethod Container.EditorTools.CraneWinBuild.BuildWin64
    /// </summary>
    public static class CraneWinBuild
    {
        const string OutDir = "Build/Win";
        const string ExeName = "Crane.exe";

        [MenuItem("Tool/서버 배포용 Windows 빌드")]
        public static void BuildWin64()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[CraneWinBuild] Build Settings에 활성 씬이 없습니다. 씬을 추가하세요.");
                EditorApplication.Exit(2);
                return;
            }

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = System.IO.Path.Combine(OutDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[CraneWinBuild] Win64 빌드 시작 — 씬 {scenes.Length}개 → {opts.locationPathName}");
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[CraneWinBuild] 결과: {s.result}, 시간 {s.totalTime}, 크기 {s.totalSize} bytes, 에러 {s.totalErrors}, 경고 {s.totalWarnings}");

            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[CraneWinBuild] 빌드 실패: {s.result}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            else if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
    }
}
#endif
