#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AIXRCrane.Crane.Sts.Plc;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>지표 6(XR·서버·PLC 이벤트 안정성) 증빙 산출기 — 코드북 XR 알람을 1.5초씩 켜고 끄는 이벤트 열을 PLC 대역으로 서버에 흘리고 Kpi6EventStability 로 100건 판정.</summary>
    [InitializeOnLoad]
    public static class Kpi6Measure
    {
        const string Key = "Kpi6Measure", PrevKey = "Kpi6Measure.Prev", StartKey = "Kpi6Measure.Start", PidKey = "Kpi6Measure.Pids", EventsKey = "Kpi6Measure.Events";
        const string ScenePath = StsPartNames.PortScenePath, Crane = StsPartNames.StsCraneRoot;
        const int Port = 5017;   // 지표1(5016)·상시 서버(5006)와 겹치지 않게
        const int HalfPeriodRows = 15;   // 100ms 행 15개 = 1.5초 켜짐 / 1.5초 꺼짐
        const int Cycles = 60;   // 발생·해제 120건 — 판정 100건 + 여유
        const float WallLimitS = 420f;

        static Kpi6EventStability kpi;
        static string summary = "";
        static int exceptions;

        static string Pref(string k) => PlcBridge.PrefKeyFor(Crane, k);
        static string Url => $"http://127.0.0.1:{Port}";

        static Kpi6Measure()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("PLC/지표6 이벤트 안정성 측정 (100건)", false, 7)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)   // 다른 세션·사람의 플레이를 가로채지 않는다
            {
                Debug.LogError("[Kpi6Measure] 이미 플레이 중 — 플레이를 멈춘 뒤 다시 실행하세요.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string server = Path.Combine(root, "Server", "xrcrane_db.py");
            string feed = BuildEventTrain(root);
            if (feed == null || !File.Exists(server))
            {
                Debug.LogError($"[Kpi6Measure] 준비 실패 — 이벤트 열 {(feed != null ? "있음" : "없음")}, 서버 {server}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            string db = Path.Combine(root, "Temp", "kpi6.db"), events = Path.Combine(root, "Temp", "kpi6_events.csv");
            foreach (var f in new[] { db, events }) if (File.Exists(f)) File.Delete(f);

            int srv = Kpi1Measure.Spawn(root, $"\"{server}\" --db \"{db}\" serve --host 127.0.0.1 --port {Port}");
            System.Threading.Thread.Sleep(800);
            int fd = Kpi1Measure.Spawn(root, $"\"{server}\" feed \"{feed}\" --url {Url} --crane {Crane} --events-out \"{events}\"");
            SessionState.SetString(PidKey, $"{srv},{fd}");
            SessionState.SetString(EventsKey, events);

            SessionState.SetBool(Key, true);
            SessionState.SetString(PrevKey,
                $"{EditorPrefs.GetBool(Pref("forceReplay"), false)}|{EditorPrefs.GetBool(Pref("forceServer"), false)}|" +
                $"{EditorPrefs.GetString(Pref("serverUrl"), "")}|{EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false)}");
            EditorPrefs.SetBool(Pref("forceReplay"), false);
            EditorPrefs.SetBool(Pref("forceServer"), true);
            EditorPrefs.SetString(Pref("serverUrl"), Url);
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, false);

            Debug.Log($"[Kpi6Measure] 지표6 측정 시작 — 서버 pid {srv} {Url}, PLC 대역 pid {fd}, 알람 1.5초 켜짐·1.5초 꺼짐 × {Cycles}");
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        // S02 첫 행(정지 자세)에 코드북 XR 알람을 차례로 켜고 끈다 — 축이 안 움직여 가속 트립이 끼어들지 않는다.
        static string BuildEventTrain(string root)
        {
            string src = Path.Combine(root, "PlcSim", "output", "S02", "run_01.csv");
            if (!File.Exists(src)) return null;
            var lines = File.ReadAllLines(src).Where(l => l.Trim().Length > 0).ToArray();
            var head = lines[0].Split(',');
            int ti = System.Array.IndexOf(head, "t_ms"), ia = System.Array.IndexOf(head, "ALM_Active"), ic = System.Array.IndexOf(head, "ALM_Latest_Code"),
                isv = System.Array.IndexOf(head, "ALM_Latest_Severity"), iso = System.Array.IndexOf(head, "ALM_Latest_Source");
            var codes = AlarmCodebook.All.Where(e => e != null && e.xr).ToArray();
            if (ti < 0 || ia < 0 || ic < 0 || codes.Length == 0) return null;
            var sb = new StringBuilder(lines[0]).Append('\n');
            for (int r = 0; r < Cycles * HalfPeriodRows * 2; r++)
            {
                var c = lines[1].Split(',');
                c[ti] = (r * 100).ToString();
                bool on = (r / HalfPeriodRows) % 2 == 0;
                var e = codes[(r / (HalfPeriodRows * 2)) % codes.Length];
                c[ia] = on ? "1" : "0";
                c[ic] = on ? e.code.ToString() : "0";
                if (isv >= 0) c[isv] = on ? e.sev.ToString() : "0";
                if (iso >= 0) c[iso] = on ? (e.code / 1000).ToString() : "0";
                sb.Append(string.Join(",", c)).Append('\n');
            }
            string path = Path.Combine(root, "Temp", "kpi6_train.csv");
            File.WriteAllText(path, sb.ToString());
            return path;
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
            if (msg.StartsWith("[Kpi6]") || msg.StartsWith("[ServerPlc]")) summary += msg + "\n";
            if (type == LogType.Exception && (stack.Contains("PlcBridge") || stack.Contains("Kpi6") || stack.Contains("ServerPlc"))) exceptions++;
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
                kpi = go.GetComponent<Kpi6EventStability>();
                if (kpi == null) kpi = go.AddComponent<Kpi6EventStability>();
                kpi.eventsPath = SessionState.GetString(EventsKey, "");
                Debug.Log("[Kpi6Measure] 부착 완료 — 마감 지난 이벤트가 100건 모이면 판정.");
                return;
            }
            if (!kpi.Done)
            {
                if (wall > WallLimitS) Finish(false, "시간 초과 — 이벤트가 100건 모이지 않음(대역·서버 로그 확인)");
                return;
            }
            Finish(kpi.Met, null);
        }

        static void Finish(bool pass, string err)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (exceptions > 0) pass = false;
            Kpi1Measure.KillSpawned(PidKey);

            Debug.Log($"[Kpi6Measure] {(pass ? "PASS" : "FAIL")}" +
                      (kpi != null ? $" — 성공 {kpi.PassCount}/{kpi.Trials} ({kpi.PassPercent:F1}%)" : "") +
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

        static string NewestKpiCsv()
        {
            try
            {
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KPI");
                var f = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("kpi6_*.csv").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault() : null;
                return f != null ? $"{f.FullName} ({f.Length / 1024f:F1} KB)" : "(없음)";
            }
            catch { return "(없음)"; }
        }
    }
}
#endif
