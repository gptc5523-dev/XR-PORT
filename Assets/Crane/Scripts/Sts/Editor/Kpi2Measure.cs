#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AIXRCrane.Crane.Sts.Plc;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>지표 2(위치·동작 정확도) 증빙 산출기 — Kpi2PositionAccuracy 100시행을 배치로 돌려 CSV 를 남긴다(timeScale 가속 금지).
    ///   -executeMethod …Kpi2Measure.Run · 재생 CSV = KPI2_CSV 또는 PlcSim/output/S02/run_01.csv · 종료코드 0 = 충족률 ≥ 90%.</summary>
    [InitializeOnLoad]
    public static class Kpi2Measure
    {
        const string Key = "Kpi2Measure", PrevKey = "Kpi2Measure.Prev", StartKey = "Kpi2Measure.Start";
        const string ScenePath = StsPartNames.PortScenePath, Crane = StsPartNames.StsCraneRoot;
        // 벽시계 한도 — 100시행 × 0.5초 = 게임 50초, 10분이면 넉넉하다.
        const float WallLimitS = 600f;
        const float TargetPercent = 90f;   // 1차년도 목표(2차 KOLAS 95). Kpi2PositionAccuracy 기본값과 같다.

        static Kpi2PositionAccuracy kpi;
        static string summary = "";
        static int exceptions;

        static string Pref(string k) => $"PlcBridge.{k}.{Crane}";   // PlcBridge.PrefKey 와 같은 키

        // 플레이 진입의 도메인 리로드 뒤에도 이어받는다.
        static Kpi2Measure()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Run()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string csv = System.Environment.GetEnvironmentVariable("KPI2_CSV");
            if (string.IsNullOrEmpty(csv)) csv = Path.Combine(root, "PlcSim", "output", "S02", "run_01.csv");
            if (!File.Exists(csv))
            {
                Debug.LogError($"[Kpi2Measure] 재생할 CSV 가 없습니다: {csv}");
                EditorApplication.Exit(1);
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
            Debug.Log($"[Kpi2Measure] 지표2 측정 시작 — {Path.GetFileName(Path.GetDirectoryName(csv))}/{Path.GetFileName(csv)} " +
                      $"({rows}행 × 100ms = {rows * 0.1f:F1}초 분량, 끝나면 되감아 반복) · 목표 {TargetPercent:F0}% · 등속(timeScale 1)");

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
            if (msg.StartsWith("[Kpi2]")) summary += msg + "\n";
            if (type == LogType.Exception && (stack.Contains("PlcBridge") || stack.Contains("Kpi2"))) exceptions++;
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
                kpi = go.GetComponent<Kpi2PositionAccuracy>();
                if (kpi == null) kpi = go.AddComponent<Kpi2PositionAccuracy>();
                Debug.Log($"[Kpi2Measure] 부착 완료 — 소스 {bridge.Source?.Name}, 이제 0.5초마다 1시행(100회).");
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

            Debug.Log($"[Kpi2Measure] {(pass ? "PASS" : "FAIL")} — 종합 충족률 {pct:F1}% (목표 {TargetPercent:F0}%)" +
                      (string.IsNullOrEmpty(err) ? "" : $" · {err}") +
                      (exceptions > 0 ? $" · 예외 {exceptions}" : "") +
                      $"\n{summary}증빙 CSV: {(string.IsNullOrEmpty(artifact) ? "(없음)" : artifact)}");

            var prev = SessionState.GetString(PrevKey, "False||False").Split('|');
            EditorPrefs.SetBool(Pref("forceReplay"), prev[0] == "True");
            EditorPrefs.SetString(Pref("csvPath"), prev.Length > 1 ? prev[1] : "");
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, prev.Length > 2 && prev[2] == "True");
            SessionState.EraseBool(Key);
            EditorApplication.Exit(pass ? 0 : 1);
        }

        // 하니스가 쓴 <프로젝트>/KPI/kpi2_<날짜>.csv 경로를 로그에 남긴다.
        static string NewestKpiCsv()
        {
            try
            {
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KPI");
                if (!Directory.Exists(dir)) return "";
                var f = new DirectoryInfo(dir).GetFiles("kpi2_*.csv").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                return f != null ? $"{f.FullName} ({f.Length / 1024f:F1} KB)" : "";
            }
            catch { return ""; }
        }
    }
}
#endif
