#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Container.Crane.Sts.Plc;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 배치 스모크 — 통합서버에서 읽은 PLC 데이터로 STS 크레인 축이 움직이는지.
    ///   Unity -batchmode -nographics -projectPath &lt;클론&gt; -executeMethod Container.Crane.Sts.EditorTools.PlcServerSmoke.Run -logFile srv.log
    ///   서버에 데이터가 흘러야 PASS 다 — 먼저 <c>python3 Server/xrcrane_db.py feed &lt;csv&gt; --url … --loop</c> 를 띄울 것.
    ///   PASS(종료 0) = 서버 소스 연결 · 측정 구간에서 PLC 위치가 0.5m 이상 변하고 축도 따라 움직임 · 예외 0.
    ///   ★ 감도: 피더 없이 돌리면 최신 1행 자세에서 멈춰 FAIL 이어야 한다. PASS 면 서버가 아닌 곳에서 움직임이 온 것이다.
    /// 오너 에디터와 같은 EditorPrefs 를 쓰므로 원래 값을 두었다가 끝나면 되돌린다.
    /// </summary>
    [InitializeOnLoad]
    public static class PlcServerSmoke
    {
        const string Key = "PlcServerSmoke", StartKey = "PlcServerSmoke.Start", PrevKey = "PlcServerSmoke.Prev";
        const string ScenePath = "Assets/Scenes/Port.unity", Crane = "STS_Crane";
        const float WarmupS = 5f, MeasureS = 15f, MinSpanM = 0.5f;

        static int exceptions, samples;
        static Vector3 posMin, posMax, axMin, axMax;   // x=GT y=TR z=HO — PLC 실척 m / 축 모델값

        static string Pref(string k) => $"PlcBridge.{k}.{Crane}";   // PlcBridge.PrefKey 와 같은 키

        // 플레이 진입의 도메인 리로드 뒤에도 이어받는다.
        static PlcServerSmoke()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Run()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetString(PrevKey, $"{EditorPrefs.GetBool(Pref("forceReplay"), false)}|{EditorPrefs.GetBool(Pref("forceServer"), false)}|" +
                                            $"{EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false)}");
            EditorPrefs.SetBool(Pref("forceReplay"), false);   // 재생 복원이 서버보다 우선이라 끈다
            EditorPrefs.SetBool(Pref("forceServer"), true);
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, false);   // 시연 감독은 PlcBridge 를 끈다

            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception && (stack.Contains("ServerPlcSource") || stack.Contains("PlcBridge"))) exceptions++;
        }

        static void Tick()
        {
            float wall = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);
            var go = GameObject.Find(Crane);
            var bridge = go != null ? go.GetComponent<PlcBridge>() : null;
            var crane = go != null ? go.GetComponent<StsCrane>() : null;
            if (wall < WarmupS) return;   // 첫 행으로 건너뛰는 한 번의 이동은 재지 않는다
            if (wall < WarmupS + MeasureS)
            {
                if (bridge != null && crane != null && bridge.Source != null && bridge.Source.TryRead(out var snap)) Sample(snap, crane);
                return;
            }

            bool server = bridge != null && bridge.Source is ServerPlcSource;
            bool connected = server && bridge.Source.IsConnected;
            Vector3 dp = posMax - posMin, da = axMax - axMin;
            float posSpan = Mathf.Max(dp.x, Mathf.Max(dp.y, dp.z)), axSpan = Mathf.Max(da.x, Mathf.Max(da.y, da.z));
            bool pass = connected && samples > 0 && posSpan >= MinSpanM && axSpan > 0f && exceptions == 0;
            Debug.Log($"[PlcServerSmoke] {(pass ? "PASS" : "FAIL")} — 소스 {bridge?.Source?.Name ?? "없음"} 연결 {connected}, 표본 {samples}, " +
                      $"PLC 변화 GT {dp.x:F2} · TR {dp.y:F2} · HO {dp.z:F2}m (≥{MinSpanM}), 축 변화(model) GT {da.x:F4} · TR {da.y:F4} · HO {da.z:F4}, " +
                      $"예외 {exceptions}, 벽 {wall:F0}s");

            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            var prev = SessionState.GetString(PrevKey, "False|False|False").Split('|');
            EditorPrefs.SetBool(Pref("forceReplay"), prev[0] == "True");
            EditorPrefs.SetBool(Pref("forceServer"), prev.Length > 1 && prev[1] == "True");
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, prev.Length > 2 && prev[2] == "True");
            SessionState.EraseBool(Key);
            EditorApplication.Exit(pass ? 0 : 1);
        }

        static void Sample(PlcSnapshot s, StsCrane c)
        {
            var p = new Vector3(s.GtPosition, s.TrPosition, s.HoPosition);
            var a = new Vector3(c.Gantry?.Current ?? 0f, c.Trolley?.Current ?? 0f, c.Spreader?.Current ?? 0f);
            if (samples++ == 0) { posMin = posMax = p; axMin = axMax = a; return; }
            posMin = Vector3.Min(posMin, p); posMax = Vector3.Max(posMax, p);
            axMin = Vector3.Min(axMin, a); axMax = Vector3.Max(axMax, a);
        }
    }
}
#endif
