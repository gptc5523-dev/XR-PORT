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
    ///     Port 씬을 10배속으로 돌려, 계획이 선 크레인마다 '놓기' 10회(옮기고 되돌리기)가 나오고 러너의 수식 검증
    ///     (CraneDemoRunner.Violations — 집기 정렬·안착·겹침·축 한계) 위반이 0 이면 0, 아니면·시간 초과·시나리오 예외면 1 로 종료.
    /// </summary>
    [InitializeOnLoad]
    public static class PortDemoMenu
    {
        const string MenuPath = "PLC/시연 시나리오 (에디터 Play)";
        const string ScenePath = "Assets/Scenes/Port.unity";
        const string SmokeKey = "PortDemo.Smoke", PrevKey = "PortDemo.SmokePrev", StartKey = "PortDemo.SmokeStart";
        const float TimeScale = 10f, LimitSeconds = 600f;

        static readonly Dictionary<string, int> placedBy = new Dictionary<string, int>();
        static int planned, stalls, ourExceptions, otherExceptions, holds, resumes;

        // 접근 멈춤 시험 — ApproachAt 초(벽시계)에 플레이어 리그를 조종기 크레인 발치로 옮겨 ApproachFor 초 두었다가 되돌린다.
        //   러너가 정격 감속으로 서고(검증 ④가 틱마다 감속을 잰다) 떠나면 이어서 돌아야 PASS — 멈춤·재개 로그를 센다.
        const float ApproachAt = 30f, ApproachFor = 8f;
        static bool approachStarted, approachEnded;
        static Vector3 rigHome;

        static void Approach(float elapsed)
        {
            var ctrl = StsCraneVRController.Active;
            if (ctrl == null) return;
            var cam = Camera.main;
            if (cam == null)   // 배치 스모크엔 Camera.main 이 없을 수 있다(2026-09-15 접근 시험이 안 걸렸다) — 감독이 볼 카메라를 세운다
            {
                cam = new GameObject("PortDemoSmokeCam").AddComponent<Camera>();
                cam.tag = "MainCamera";
                Debug.Log("[PortDemoSmoke] Camera.main 없음 — 시험용 카메라 생성");
            }
            var rig = cam.transform.root;
            if (!approachStarted && elapsed >= ApproachAt)
            {
                approachStarted = true;
                rigHome = rig.position;
                var c = ctrl.GetComponent<StsCrane>();
                Vector3 at = c != null && c.Gantry is Component g ? g.transform.position : ctrl.transform.position;
                rig.position += new Vector3(at.x - cam.transform.position.x, 0f, at.z - cam.transform.position.z);
                Debug.Log($"[PortDemoSmoke] 접근 — 리그를 {ctrl.name} 발치로");
            }
            else if (approachStarted && !approachEnded && elapsed >= ApproachAt + ApproachFor)
            {
                approachEnded = true;
                rig.position = rigHome + new Vector3(1000f, 0f, 1000f);   // 원위치가 크레인 발자국 안일 수 있다(시험용 카메라는 원점) — 멀리 뺀다
                Debug.Log("[PortDemoSmoke] 떠남 — 리그를 크레인에서 멀리");
            }
        }

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
            if (msg.Contains(": 멈춤 — 접근")) holds++;
            if (msg.EndsWith(": 재개")) resumes++;
        }

        static void Tick()
        {
            float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);
            Approach(elapsed);
            int done = 0;
            foreach (int n in placedBy.Values) if (n >= 2 * CraneDemoRunner.Count) done++;   // 옮기고 되돌리기까지 — 두 방향 다
            bool finished = planned > 0 && done >= planned && approachEnded;
            if (!finished && ourExceptions == 0 && elapsed < LimitSeconds) return;
            bool pass = finished && ourExceptions == 0 && CraneDemoRunner.Violations == 0 && holds > 0 && resumes > 0;

            var parts = new List<string>();
            foreach (var kv in placedBy) parts.Add($"{kv.Key}={kv.Value}");
            Debug.Log($"[PortDemoSmoke] {(pass ? "PASS" : "FAIL")} — 계획 크레인 {planned}대, 놓기 [{string.Join(", ", parts)}], " +
                      $"막힘 {stalls}, 접근 멈춤 {holds} · 재개 {resumes}, 시나리오 예외 {ourExceptions}, 기타 예외 {otherExceptions}, {elapsed:F0}s(×{TimeScale}) | " +
                      $"검증 실패 {CraneDemoRunner.Violations} · 집기 정렬 최대 {CraneDemoRunner.MaxPickErrM:F3}m(≤0.36) · " +
                      $"안착 오차 최대 {CraneDemoRunner.MaxSupportErrM:F3}m(≤0.02) · 속도/정격 최대 {CraneDemoRunner.MaxSpeedRatio:F3}(≤1) · " +
                      $"가속/트립 최대 {CraneDemoRunner.MaxAccelRatio:F2}(한 틱 ≤1.2)");
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, SessionState.GetBool(PrevKey, false));
            SessionState.EraseBool(SmokeKey);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
