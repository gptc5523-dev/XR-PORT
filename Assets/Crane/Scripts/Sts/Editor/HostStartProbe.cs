#if UNITY_EDITOR
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
    ///   ③ 나가기: 세션을 끊으면 포트가 풀려 **다시 호스트가 될 수 있어야** 한다(오너 "호스트 세션이 안 끊긴다").
    ///      서버의 Crane.exe 는 헤드셋이 빠져도 살아 있어, 안 끊으면 그 인스턴스가 7777 을 쥔 채 남는다.
    ///   ④ 헤드셋 이탈 자동 종료 '규칙'(ExitZone.ShouldAutoEnd) — 관전자가 있으면 유지, 혼자면 종료.
    ///      ※ 규칙만 잰다. 배관(XRDisplaySubsystem 폴링 → xrSeenRunning 가드 → 타이머 누적)은 배치에
    ///        XR 서브시스템이 없어 도달 자체가 불가능하므로 **헤드셋에서만 확인된다**. 특히 '관전자가 있는
    ///        동안은 타이머를 아예 안 쌓는다'(관전자가 나간 순간 즉시 종료 방지)는 이 검사 밖이다(c8 미검증 항목).
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
        static float waitUntil, downDeadline;
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

                    // ③ 나가기 — NetLanUI.Leave() 는 다른 세션이 작업 중이라 아직 HEAD 에 없다.
                    //   직접 호출하면 HEAD 빌드가 깨져 배포 체인이 멈추므로, 있으면 부르고 없으면 '미구현'으로 판정만 남긴다.
                    //   ※ Leave() 가 커밋되면 이 리플렉션을 ui.Leave() 직접 호출로 바꿀 것.
                    var leave = typeof(NetLanUI).GetMethod("Leave", BindingFlags.Public | BindingFlags.Instance);
                    if (leave == null)
                    {
                        measured++; fails++;
                        Debug.Log("[HostStartProbe] BAD ③나가기 — NetLanUI.Leave() 미구현. 헤드셋에서 세션을 끊을 방법이 없어 " +
                                  "그 인스턴스가 포트를 쥔 채 남는다(기능이 들어오면 이 줄이 실제 측정으로 바뀐다).");
                        if (serverUp) NetworkManager.Singleton.Shutdown();
                        Finish(); return;
                    }
                    leave.Invoke(ui, null);
                    downDeadline = Time.time + 5f;
                    phase = 4; Wait(1.0f); return;
                }

                case 4:   // 세션이 실제로 끊겼는지 — 안 끊기는 것 자체가 오너가 보고한 증상이다
                {
                    var nm4 = NetworkManager.Singleton;
                    if (nm4 != null && (nm4.IsServer || nm4.IsClient))
                    {
                        if (Time.time < downDeadline) { Wait(0.5f); return; }
                        measured++; fails++;
                        Debug.Log($"[HostStartProbe] BAD ③나가기 — Leave() 뒤 5초가 지나도 세션이 안 끊김(IsServer {nm4.IsServer}, IsClient {nm4.IsClient}). " +
                                  "포트가 계속 잡혀 있어 다음 호스트 시작이 실패한다.");
                        nm4.Shutdown();
                        Finish(); return;
                    }
                    ui.BeginHost();   // 포트가 풀렸으면 다시 호스트가 되어야 한다
                    phase = 5; Wait(1.5f); return;
                }

                case 5:
                {
                    bool failed = ui.HostFailed;
                    bool serverUp = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
                    bool ok = !failed && serverUp;
                    measured++; if (!ok) fails++;
                    Debug.Log($"[HostStartProbe] {(ok ? "OK " : "BAD")} ③나가기후재호스트 — HostFailed {failed}(기대 false), IsServer {serverUp}(기대 true)");
                    if (serverUp) NetworkManager.Singleton.Shutdown();
                    phase = 6; Wait(0.2f); return;
                }

                case 6:   // ④ 자동 종료 규칙 — 순수 함수라 XR 없이 결정적으로 잰다
                    Rule("관전자 있는 호스트 → 유지",  lostFor: 999f, grace: 15f, spectators:  1, expect: false);
                    Rule("혼자 남은 호스트 → 종료",    lostFor:  16f, grace: 15f, spectators:  0, expect: true);
                    Rule("관전자 본인 → 자기만 종료",  lostFor:  16f, grace: 15f, spectators: -1, expect: true);
                    Rule("유예 전 → 유지",             lostFor:  14f, grace: 15f, spectators:  0, expect: false);
                    Rule("유예 0(기능 끔) → 유지",     lostFor:  16f, grace:  0f, spectators:  0, expect: false);
                    Finish(); return;
            }
        }

        // ④ 규칙 한 줄 판정. spectators 는 인원수가 아니라 **3상태**다(c8 규약, ExitZone.SpectatorCount 와 동일):
        //   ≥1 관전자가 붙은 호스트 · 0 혼자 남은 호스트 · −1 나는 관전자(호스트 아님).
        //   그래서 조건이 `spectators <= 0` 이고, 이게 "혼자 남은 호스트"와 "관전자 본인"을 함께 덮는다.
        //   ★ 누가 `<= 0` 을 `== 0` 으로 '고치면' 관전자 자동 정리가 조용히 죽는다 — −1 케이스가 그걸 잡는다.
        static void Rule(string what, float lostFor, float grace, int spectators, bool expect)
        {
            bool got = ExitZone.ShouldAutoEnd(lostFor, grace, spectators);
            bool ok = got == expect;
            measured++; if (!ok) fails++;
            Debug.Log($"[HostStartProbe] {(ok ? "OK " : "BAD")} ④{what} — " +
                      $"ShouldAutoEnd(상실 {lostFor:0}초, 유예 {grace:0}초, 관전자 {spectators}) = {got}(기대 {expect})");
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
