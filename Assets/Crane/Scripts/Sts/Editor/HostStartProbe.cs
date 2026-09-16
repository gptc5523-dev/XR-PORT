#if UNITY_EDITOR
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Container.Crane.Sts.Net;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 배치 검사 — '호스트 시작'의 결과가 실제로 남는가(오너 2026-09-16 "호스트 참가가 안 된다").
    ///   ① 포트가 점유된 상태: 호스트가 실패하고 NetLanUI.HostFailed 가 true 여야 한다.
    ///      → 이 상태를 못 잡으면 VR 메뉴가 아무 말도 못 하고, 사용자는 눌렀는지조차 모른다(종전 거동).
    ///   ② 포트가 빈 상태: 호스트가 뜨고(IsServer) HostFailed 는 false 여야 한다.
    /// 서버는 한 머신에 인스턴스 5개를 띄우므로 ①은 실제로 일어나는 상황이다(먼저 뜬 쪽이 포트를 쥠).
    /// UnityTransport 는 UDP 라 점유도 UdpClient 로 한다(TcpListener 로는 충돌하지 않는다).
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Container.Crane.Sts.EditorTools.HostStartProbe.Run -logFile host.log
    ///   ※ -quit 금지 — EnterPlaymode 방식이라 주면 플레이에 못 들어가고 로그가 빈다.
    /// </summary>
    [InitializeOnLoad]
    public static class HostStartProbe
    {
        const string Key = "HostStartProbe", PrevKey = "HostStartProbe.Prev", ScenePath = "Assets/Scenes/Port.unity";

        static int phase, fails, measured;
        static float waitUntil;
        static NetLanUI ui;
        static UdpClient squatter;   // 7777 을 먼저 쥐는 역할(다른 인스턴스 흉내)

        static HostStartProbe()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlay;
            EditorApplication.playModeStateChanged += OnPlay;
        }

        public static void Run()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetBool(PrevKey, EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, false);   // 시연 감독이 크레인을 움직이지 않게
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.playModeStateChanged -= OnPlay;
            EditorApplication.playModeStateChanged += OnPlay;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlay(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            waitUntil = Time.time + 1f;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || Time.time < waitUntil) return;
            try { Step(); }
            catch (System.Exception e) { Debug.LogError("[HostStartProbe] 예외 " + e); fails++; Finish(); }
        }

        static void Wait(float s) => waitUntil = Time.time + s;

        static void Step()
        {
            var nm = NetworkManager.Singleton;
            switch (phase)
            {
                case 0:
                    ui = Object.FindAnyObjectByType<NetLanUI>();
                    if (ui == null || nm == null)
                    {
                        Debug.LogError($"[HostStartProbe] 씬에 접속 구성이 없음 — NetLanUI {(ui != null)} · NetworkManager {(nm != null)}. " +
                                       "VR 시작 메뉴가 동작할 수 없는 상태다.");
                        fails++; Finish(); return;
                    }
                    // ① 다른 인스턴스가 먼저 포트를 쥔 상황을 만든다.
                    try { squatter = new UdpClient(new IPEndPoint(IPAddress.Any, NetConfig.DefaultPort)); }
                    catch (System.Exception e) { Debug.LogError($"[HostStartProbe] 포트 점유 실패 — 검사 불가: {e.Message}"); fails++; Finish(); return; }
                    Debug.Log($"[HostStartProbe] 포트 {NetConfig.DefaultPort} 을(를) 다른 소켓이 쥔 상태로 만듦 — 이제 호스트 시작");
                    ui.BeginHost();
                    phase = 1; Wait(1.0f); return;

                case 1:
                {
                    bool failed = ui.HostFailed;
                    bool serverUp = nm.IsServer;
                    bool ok = failed && !serverUp;   // 실패해야 하고, 실패 사실이 남아야 한다
                    measured++; if (!ok) fails++;
                    Debug.Log($"[HostStartProbe] {(ok ? "OK " : "BAD")} ①포트점유 — HostFailed {failed}(기대 true), IsServer {serverUp}(기대 false)");
                    if (serverUp) nm.Shutdown();
                    try { squatter?.Close(); } catch { }
                    squatter = null;
                    phase = 2; Wait(1.0f); return;
                }

                case 2:   // ② 포트가 비었으면 정상적으로 떠야 한다
                    ui.BeginHost();
                    phase = 3; Wait(1.5f); return;

                case 3:
                {
                    bool failed = ui.HostFailed;
                    bool serverUp = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
                    bool ok = !failed && serverUp;
                    measured++; if (!ok) fails++;
                    Debug.Log($"[HostStartProbe] {(ok ? "OK " : "BAD")} ②포트정상 — HostFailed {failed}(기대 false), IsServer {serverUp}(기대 true)");
                    if (serverUp) NetworkManager.Singleton.Shutdown();
                    Finish(); return;
                }
            }
        }

        static void Finish()
        {
            try { squatter?.Close(); } catch { }
            squatter = null;
            bool pass = measured > 0 && fails == 0;
            Debug.Log($"[HostStartProbe] {(pass ? "PASS" : "FAIL")} — 측정 {measured}, 실패 {fails}");
            EditorApplication.update -= Tick;
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, SessionState.GetBool(PrevKey, false));
            SessionState.EraseBool(Key);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
