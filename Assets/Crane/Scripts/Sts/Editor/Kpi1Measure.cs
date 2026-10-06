#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AIXRCrane.Crane.Sts.Plc;
using Debug = UnityEngine.Debug;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>지표 1(PLC→XR 지연) 증빙 산출기 — 로컬 서버 + PLC 대역(feed)을 띄우고 Server 소스로 Kpi1Latency 100시행, 끝나면 둘 다 끈다.</summary>
    [InitializeOnLoad]
    public static class Kpi1Measure
    {
        const string Key = "Kpi1Measure", PrevKey = "Kpi1Measure.Prev", StartKey = "Kpi1Measure.Start", PidKey = "Kpi1Measure.Pids";
        const string ScenePath = StsPartNames.PortScenePath, Crane = StsPartNames.StsCraneRoot;
        const int Port = 5016;   // 상시 서버(5006)와 겹치지 않게
        const string Feed = "PlcSim/output/S02/run_01.csv";   // 정상 운전 — 지연만 본다
        const float WallLimitS = 400f;   // 100시행 × 1초 + 기동, 넉넉히

        static Kpi1Latency kpi;
        static string summary = "";
        static int exceptions;

        static string Pref(string k) => PlcBridge.PrefKeyFor(Crane, k);
        static string Url => $"http://127.0.0.1:{Port}";

        static Kpi1Measure()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("PLC/지표1 PLC→XR 지연 측정 (100회)", false, 5)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)   // 다른 세션·사람의 플레이를 가로채지 않는다
            {
                Debug.LogError("[Kpi1Measure] 이미 플레이 중 — 플레이를 멈춘 뒤 다시 실행하세요.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string feed = Path.Combine(root, Feed), server = Path.Combine(root, "Server", "xrcrane_db.py");
            if (!File.Exists(feed) || !File.Exists(server))
            {
                Debug.LogError($"[Kpi1Measure] 없음: {(File.Exists(feed) ? server : feed)}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            string db = Path.Combine(root, "Temp", "kpi1.db");
            if (File.Exists(db)) File.Delete(db);   // 새 DB — after_id 가 이번 런 행만 따라가게

            int srv = Spawn(root, $"\"{server}\" --db \"{db}\" serve --host 127.0.0.1 --port {Port}");
            System.Threading.Thread.Sleep(800);   // 서버가 듣기 시작할 때까지
            int fd = Spawn(root, $"\"{server}\" feed \"{feed}\" --url {Url} --crane {Crane} --loop");
            SessionState.SetString(PidKey, $"{srv},{fd}");

            SessionState.SetBool(Key, true);
            SessionState.SetString(PrevKey,
                $"{EditorPrefs.GetBool(Pref("forceReplay"), false)}|{EditorPrefs.GetBool(Pref("forceServer"), false)}|" +
                $"{EditorPrefs.GetString(Pref("serverUrl"), "")}|{EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false)}");
            EditorPrefs.SetBool(Pref("forceReplay"), false);   // 재생 복원이 서버보다 먼저라 꺼야 서버로 붙는다
            EditorPrefs.SetBool(Pref("forceServer"), true);
            EditorPrefs.SetString(Pref("serverUrl"), Url);
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, false);   // 시연 감독은 PlcBridge 를 끈다

            Debug.Log($"[Kpi1Measure] 지표1 측정 시작 — 서버 pid {srv} {Url}, PLC 대역 pid {fd} ({Feed} 반복), 100시행 × 1초");
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        static int Spawn(string root, string args)
        {
            var p = Process.Start(new ProcessStartInfo("python3", args)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = false, RedirectStandardError = false,
            });
            return p != null ? p.Id : -1;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key, false)) { Finish(false, "플레이가 측정 도중 밖에서 멈춤"); return; }
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            kpi = null;
            summary = "";
            exceptions = 0;
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (msg.StartsWith("[Kpi1]") || msg.StartsWith("[ServerPlc]")) summary += msg + "\n";
            if (type == LogType.Exception && (stack.Contains("PlcBridge") || stack.Contains("Kpi1") || stack.Contains("ServerPlc"))) exceptions++;
        }

        static void Tick()
        {
            float wall = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);
            if (kpi == null)
            {
                var go = GameObject.Find(Crane);
                var bridge = go != null ? go.GetComponent<PlcBridge>() : null;
                if (bridge == null && go != null) bridge = go.AddComponent<PlcBridge>();
                if (bridge == null || !bridge.Active || !(bridge.Source is ServerPlcSource))
                {
                    if (wall > 30f) Finish(false, bridge == null ? $"씬에서 '{Crane}' 를 못 찾음" : $"PlcBridge 가 Server 소스로 붙지 않음 (소스 {bridge.Source?.Name})");
                    return;
                }
                kpi = go.GetComponent<Kpi1Latency>();
                if (kpi == null) kpi = go.AddComponent<Kpi1Latency>();
                Debug.Log("[Kpi1Measure] 부착 완료 — 소스 Server, 1초마다 1시행(100회).");
                return;
            }
            if (!kpi.Done)
            {
                if (wall > WallLimitS) Finish(false, $"시간 초과 — {kpi.Trials}시행까지만 진행(서버·대역 로그 확인)");
                return;
            }
            Finish(kpi.Met, null);
        }

        static void Finish(bool pass, string err)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (exceptions > 0) pass = false;
            KillSpawned();

            Debug.Log($"[Kpi1Measure] {(pass ? "PASS" : "FAIL")}" +
                      (kpi != null ? $" — 통과 {kpi.PassCount}/{kpi.Trials}, 평균 {kpi.MeanMs:F0}ms, 최대 {kpi.MaxMs}ms" : "") +
                      (string.IsNullOrEmpty(err) ? "" : $" · {err}") + (exceptions > 0 ? $" · 예외 {exceptions}" : "") +
                      $"\n{summary}증빙 CSV: {NewestKpiCsv()}");

            var prev = SessionState.GetString(PrevKey, "False|False||False").Split('|');
            EditorPrefs.SetBool(Pref("forceReplay"), prev[0] == "True");
            EditorPrefs.SetBool(Pref("forceServer"), prev.Length > 1 && prev[1] == "True");
            if (prev.Length > 2 && prev[2] != "") EditorPrefs.SetString(Pref("serverUrl"), prev[2]); else EditorPrefs.DeleteKey(Pref("serverUrl"));
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, prev.Length > 3 && prev[3] == "True");
            SessionState.EraseBool(Key);
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }

        // 플레이 진입 도메인 리로드로 Process 핸들이 사라지므로 pid 로 끈다.
        static void KillSpawned()
        {
            foreach (var s in SessionState.GetString(PidKey, "").Split(','))
            {
                if (!int.TryParse(s, out int pid) || pid <= 0) continue;
                try { Process.GetProcessById(pid).Kill(); } catch { }
            }
            SessionState.EraseString(PidKey);
        }

        static string NewestKpiCsv()
        {
            try
            {
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KPI");
                var f = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("kpi1_*.csv").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault() : null;
                return f != null ? $"{f.FullName} ({f.Length / 1024f:F1} KB)" : "(없음)";
            }
            catch { return "(없음)"; }
        }
    }
}
#endif
