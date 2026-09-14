#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 시연 시나리오(<see cref="PortDemoDirector"/>) 에디터 스위치 + 배치모드 스모크 실행기.
    ///   메뉴를 체크하면 에디터 ▶Play 에서도 시나리오가 돈다(빌드는 항상). 끄면 PLC·KPI 작업 그대로.
    ///   스모크(에디터 닫고):
    ///     Unity -batchmode -nographics -projectPath . -executeMethod Container.Crane.Sts.EditorTools.PortDemoMenu.SmokeRun -logFile smoke.log
    ///     Port 씬을 10배속으로 돌려, 계획이 선 크레인마다 '놓기' 5회(한 방향)가 나오면 0, 시간 초과·시나리오 예외면 1 로 종료.
    /// </summary>
    [InitializeOnLoad]
    public static class PortDemoMenu
    {
        const string MenuPath = "PLC/시연 시나리오 (에디터 Play)";
        const string ScenePath = "Assets/Scenes/Port.unity";
        const string SmokeKey = "PortDemo.Smoke", PrevKey = "PortDemo.SmokePrev", StartKey = "PortDemo.SmokeStart";
        const float TimeScale = 10f, LimitSeconds = 600f;

        static readonly Dictionary<string, int> placedBy = new Dictionary<string, int>();
        static int planned, stalls, ourExceptions, otherExceptions;

        // 플레이 진입의 도메인 리로드 뒤에도 스모크를 이어받는다.
        static PortDemoMenu()
        {
            if (!SessionState.GetBool(SmokeKey, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem(MenuPath, false, 20)]
        static void Toggle() =>
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, !EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));

        [MenuItem(MenuPath, true)]
        static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));
            return true;
        }

        public static void SmokeRun()
        {
            SessionState.SetBool(SmokeKey, true);
            SessionState.SetBool(PrevKey, EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, true);
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            Time.timeScale = TimeScale;
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                if (stack.Contains("CraneDemoRunner") || stack.Contains("PortDemoDirector")) ourExceptions++;
                else otherExceptions++;
            }
            if (!msg.StartsWith("[PortDemo] ")) return;
            int i = msg.IndexOf(" 놓기 ");
            if (i > 0)
            {
                string crane = msg.Substring(11, i - 11);
                placedBy[crane] = placedBy.TryGetValue(crane, out int n) ? n + 1 : 1;
            }
            if (msg.Contains(": 계획 ") && !msg.Contains(": 계획 0개")) planned++;
            if (msg.Contains("막힘")) stalls++;
        }

        static void Tick()
        {
            float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);
            int done = 0;
            foreach (int n in placedBy.Values) if (n >= CraneDemoRunner.Count) done++;
            bool pass = planned > 0 && done >= planned && ourExceptions == 0;
            if (!pass && ourExceptions == 0 && elapsed < LimitSeconds) return;

            var parts = new List<string>();
            foreach (var kv in placedBy) parts.Add($"{kv.Key}={kv.Value}");
            Debug.Log($"[PortDemoSmoke] {(pass ? "PASS" : "FAIL")} — 계획 크레인 {planned}대, 놓기 [{string.Join(", ", parts)}], " +
                      $"막힘 {stalls}, 시나리오 예외 {ourExceptions}, 기타 예외 {otherExceptions}, {elapsed:F0}s(×{TimeScale})");
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, SessionState.GetBool(PrevKey, false));
            SessionState.EraseBool(SmokeKey);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
