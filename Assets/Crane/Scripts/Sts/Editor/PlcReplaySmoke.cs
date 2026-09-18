#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>배치 스모크 — STS CSV 재생 시 화물을 실제로 집는지 검증(PASS/FAIL 로 종료코드).
    /// Unity -batchmode -nographics -projectPath . -executeMethod AIXRCrane.Crane.Sts.EditorTools.PlcReplaySmoke.Run -logFile plc.log</summary>
    [InitializeOnLoad]
    public static class PlcReplaySmoke
    {
        const string Key = "PlcReplaySmoke", StartKey = "PlcReplaySmoke.Start", EndKey = "PlcReplaySmoke.End",
                     MovesKey = "PlcReplaySmoke.Moves", PrevKey = "PlcReplaySmoke.Prev";
        const string ScenePath = StsPartNames.PortScenePath, Crane = StsPartNames.StsCraneRoot;
        // 벽시계 한도 — 재생 배속을 걸어도 스캔·물리 부하로 실효 배속은 더 낮다.
        const float TimeScale = 10f, WallLimitS = 900f, PickTolM = 0.36f;

        static int picks, places, shipPicks, shipAdopted, exceptions;
        static float maxDxz;
        static readonly Regex DxzRx = new Regex(@"중심 ([0-9.]+)m");

        static string Pref(string k) => $"PlcBridge.{k}.{Crane}";   // PlcBridge.PrefKey 와 같은 키

        // 플레이 진입의 도메인 리로드 뒤에도 이어받는다.
        static PlcReplaySmoke()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Run()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string csv = System.Environment.GetEnvironmentVariable("PLC_SMOKE_CSV");
            if (string.IsNullOrEmpty(csv)) csv = Path.Combine(root, "PlcSim", "output", "S14", "run_01.csv");
            string hist = Path.ChangeExtension(csv, ".history.csv");
            // 이동 수·끝 시각은 이력 파일에서 직접 센다 — PlcCargo 로그는 구독 전에 찍혀 놓칠 수 있다.
            var rows = File.Exists(hist) ? File.ReadAllLines(hist).Skip(1).Where(l => l.Trim().Length > 0).ToArray() : new string[0];
            float endS = rows.Length > 0 ? rows.Max(l => int.Parse(l.Split(',')[7], CultureInfo.InvariantCulture)) / 1000f + 5f : 60f;   // place_t_ms

            SessionState.SetBool(Key, true);
            SessionState.SetFloat(EndKey, endS);
            SessionState.SetInt(MovesKey, rows.Length);
            SessionState.SetString(PrevKey, $"{EditorPrefs.GetBool(Pref("forceReplay"), false)}|{EditorPrefs.GetString(Pref("csvPath"), "")}|" +
                                            $"{EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false)}");
            EditorPrefs.SetBool(Pref("forceReplay"), true);
            EditorPrefs.SetString(Pref("csvPath"), csv);
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, false);   // 시연 감독은 PlcBridge 를 끈다
            Debug.Log($"[PlcReplaySmoke] {Path.GetFileName(Path.GetDirectoryName(csv))}/{Path.GetFileName(csv)} — 이동 {rows.Length}개, 게임 {endS:F0}초까지 ×{TimeScale}");

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
            if (type == LogType.Exception && (stack.Contains("PlcCargoReplay") || stack.Contains("PlcBridge"))) exceptions++;
            if (msg.StartsWith("[PlcCargo] 집기 "))
            {
                picks++;
                var d = DxzRx.Match(msg);
                if (d.Success) maxDxz = Mathf.Max(maxDxz, float.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture));
                if (msg.Contains(" SHIP/")) { shipPicks++; if (msg.Contains("— 씬 컨테이너")) shipAdopted++; }
            }
            if (msg.StartsWith("[PlcCargo] 놓기 ")) places++;
        }

        static void Tick()
        {
            float wall = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);
            if (Time.time < SessionState.GetFloat(EndKey, 60f) && wall < WallLimitS && exceptions == 0) return;

            int moves = SessionState.GetInt(MovesKey, 0);
            bool pass = moves > 0 && picks >= moves && places >= moves && shipPicks == shipAdopted && maxDxz <= PickTolM && exceptions == 0;
            Debug.Log($"[PlcReplaySmoke] {(pass ? "PASS" : "FAIL")} — 이동 {moves}, 집기 {picks}, 놓기 {places}, " +
                      $"배에서 집기 {shipPicks}(씬 컨테이너 {shipAdopted}), 트위스트락↔윗면 최대 {maxDxz:F3}m(≤{PickTolM}), " +
                      $"예외 {exceptions}, 게임 {Time.time:F0}s · 벽 {wall:F0}s");

            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            var prev = SessionState.GetString(PrevKey, "False||False").Split('|');
            EditorPrefs.SetBool(Pref("forceReplay"), prev[0] == "True");
            EditorPrefs.SetString(Pref("csvPath"), prev.Length > 1 ? prev[1] : "");
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, prev.Length > 2 && prev[2] == "True");
            SessionState.EraseBool(Key);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
