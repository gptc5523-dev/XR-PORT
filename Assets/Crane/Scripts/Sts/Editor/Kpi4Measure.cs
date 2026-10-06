#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AIXRCrane.Crane.Sts.Plc;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>지표 4(O&amp;M 상태표시 정확도) 증빙 산출기 — Kpi4StatusAccuracy 100시행을 배치로 돌려 CSV 를 남긴다(timeScale 가속 금지).
    ///   알람이 고루 나오게 시나리오 8개(정상 S02 + 알람 S06~S12)의 run_01 을 이어 붙여 재생한다(t_ms 를 이어서).
    ///   -executeMethod AIXRCrane.Crane.Sts.EditorTools.Kpi4Measure.Run · 메뉴 PLC/지표4 상태표시 정확도 측정 · 종료코드 0 = 충족률 ≥ 90%.</summary>
    [InitializeOnLoad]
    public static class Kpi4Measure
    {
        const string Key = "Kpi4Measure", PrevKey = "Kpi4Measure.Prev", StartKey = "Kpi4Measure.Start";
        const string ScenePath = StsPartNames.PortScenePath, Crane = StsPartNames.StsCraneRoot;
        // 벽시계 한도 — 100시행 × 2.5초 = 게임 250초, 10분이면 넉넉하다.
        const float WallLimitS = 600f;
        const float TargetPercent = 90f;   // 1차년도 목표(2차 KOLAS 95). Kpi4StatusAccuracy 기본값과 같다.
        static readonly string[] Mix = { "S02", "S06", "S07", "S08", "S09", "S10", "S11", "S12" };

        static Kpi4StatusAccuracy kpi;
        static string summary = "";
        static int exceptions;

        static string Pref(string k) => PlcBridge.PrefKeyFor(Crane, k);

        // 플레이 진입의 도메인 리로드 뒤에도 이어받는다.
        static Kpi4Measure()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("PLC/지표4 상태표시 정확도 측정 (100회)", false, 6)]
        public static void Run()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string csv = BuildMix(root);
            if (csv == null)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(Key, true);
            SessionState.SetString(PrevKey,
                $"{EditorPrefs.GetBool(Pref("forceReplay"), false)}|{EditorPrefs.GetString(Pref("csvPath"), "")}|" +
                $"{EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false)}");
            EditorPrefs.SetBool(Pref("forceReplay"), true);
            EditorPrefs.SetString(Pref("csvPath"), csv);
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, false);   // 시연 감독은 PlcBridge 를 끈다 → 끄고 잰다

            int rows = File.ReadAllLines(csv).Length - 1;
            Debug.Log($"[Kpi4Measure] 지표4 측정 시작 — {string.Join("+", Mix)} 이어 붙임 " +
                      $"({rows}행 × 100ms = {rows * 0.1f:F1}초 분량) · 목표 {TargetPercent:F0}% · 등속(timeScale 1)");

            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
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

        // 하니스 요약을 그대로 싣는다 — 숫자를 두 곳에서 만들지 않는다.
        static void OnLog(string msg, string stack, LogType type)
        {
            if (msg.StartsWith("[Kpi4]")) summary += msg + "\n";
            if (type == LogType.Exception && (stack.Contains("PlcBridge") || stack.Contains("Kpi4"))) exceptions++;
        }

        static void Tick()
        {
            float wall = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);

            // 1) PlcBridge(재생) + 하니스 부착. Play 중 AddComponent 면 Awake 가 EditorPrefs 로 재생을 복원한다.
            if (kpi == null)
            {
                var go = GameObject.Find(Crane);
                if (go == null)
                {
                    if (wall > 30f) Finish(false, $"씬에서 '{Crane}' 를 못 찾음");
                    return;
                }
                var bridge = go.GetComponent<PlcBridge>();
                if (bridge == null) bridge = go.AddComponent<PlcBridge>();
                if (!bridge.Active)
                {
                    if (wall > 30f) Finish(false, "PlcBridge 가 Active 가 되지 않음 — 지령이 없어 잴 대상이 없다");
                    return;
                }
                kpi = go.GetComponent<Kpi4StatusAccuracy>();
                if (kpi == null) kpi = go.AddComponent<Kpi4StatusAccuracy>();
                Debug.Log($"[Kpi4Measure] 부착 완료 — 소스 {bridge.Source?.Name}, 이제 2.5초마다 1시행(100회).");
                return;
            }

            // 2) 하니스가 100시행을 채우고 Finish() 에서 요약 + CSV 를 낸다. 여기선 기다리기만.
            if (!kpi.Done)
            {
                if (wall > WallLimitS) Finish(false, $"시간 초과 — 충족률 {kpi.PassPercent:F1}% 까지만 진행");
                return;
            }
            Finish(kpi.PassPercent >= TargetPercent, null);
        }

        static void Finish(bool pass, string err)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;

            string artifact = NewestKpiCsv();
            float pct = kpi != null ? kpi.PassPercent : 0f;
            if (exceptions > 0) pass = false;

            Debug.Log($"[Kpi4Measure] {(pass ? "PASS" : "FAIL")} — 종합 충족률 {pct:F1}% (목표 {TargetPercent:F0}%)" +
                      (string.IsNullOrEmpty(err) ? "" : $" · {err}") +
                      (exceptions > 0 ? $" · 예외 {exceptions}" : "") +
                      $"\n{summary}증빙 CSV: {(string.IsNullOrEmpty(artifact) ? "(없음)" : artifact)}");

            var prev = SessionState.GetString(PrevKey, "False||False").Split('|');
            EditorPrefs.SetBool(Pref("forceReplay"), prev[0] == "True");
            EditorPrefs.SetString(Pref("csvPath"), prev.Length > 1 ? prev[1] : "");
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, prev.Length > 2 && prev[2] == "True");
            SessionState.EraseBool(Key);
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }

        // 시나리오별 run_01 을 이어 붙인 재생 CSV(Temp/kpi4_mix.csv) — 머리줄은 첫 파일 것, t_ms 는 앞 파일 끝에서 이어간다.
        static string BuildMix(string root)
        {
            var sb = new System.Text.StringBuilder();
            long offset = 0;
            string header = null;
            foreach (var sc in Mix)
            {
                string f = Path.Combine(root, "PlcSim", "output", sc, "run_01.csv");
                if (!File.Exists(f)) { Debug.LogError($"[Kpi4Measure] 재생할 CSV 가 없습니다: {f}"); return null; }
                var lines = File.ReadAllLines(f);
                if (lines.Length < 2) continue;
                if (header == null) { header = lines[0]; sb.AppendLine(header); }
                else if (lines[0] != header) { Debug.LogError($"[Kpi4Measure] {sc} 의 열 구성이 다릅니다 — 이어 붙일 수 없음"); return null; }
                int ti = System.Array.IndexOf(header.Split(','), "t_ms");
                long last = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    var cells = lines[i].Split(',');
                    long t = long.Parse(cells[ti]) + offset;
                    cells[ti] = t.ToString();
                    last = t;
                    sb.AppendLine(string.Join(",", cells));
                }
                offset = last + 100;   // 100ms 한 칸 띄워 다음 시나리오
            }
            string path = Path.Combine(root, "Temp", "kpi4_mix.csv");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        // 하니스가 쓴 <프로젝트>/KPI/kpi4_<날짜>.csv 경로를 로그에 남긴다.
        static string NewestKpiCsv()
        {
            try
            {
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KPI");
                if (!Directory.Exists(dir)) return "";
                var f = new DirectoryInfo(dir).GetFiles("kpi4_*.csv").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                return f != null ? $"{f.FullName} ({f.Length / 1024f:F1} KB)" : "";
            }
            catch { return ""; }
        }
    }
}
#endif
