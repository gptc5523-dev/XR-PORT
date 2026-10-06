#if UNITY_EDITOR
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AIXRCrane.Crane.Sts.Net;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>배치 검사 — 호스트 시작 결과가 실제로 남는지 검증(포트 점유/정상 시작·나가기 후 재호스트·헤드셋 이탈 자동 종료 규칙, ④는 규칙 함수만).
    /// Unity -batchmode -nographics -projectPath . -executeMethod AIXRCrane.Crane.Sts.EditorTools.HostStartProbe.Run -logFile host.log (-quit 금지, 로그가 빈다).</summary>
    [InitializeOnLoad]
    public static class HostStartProbe
    {
        const string Key = "HostStartProbe", PrevKey = "HostStartProbe.Prev", ScenePath = StsPartNames.PortScenePath;

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

                    // ③ 나가기
                    ui.Leave();
                    downDeadline = Time.time + 5f;
                    phase = 4; Wait(1.0f); return;
                }

                case 4:   // 세션이 실제로 끊겼는지 확인(안 끊기면 포트가 잡혀 다음 호스트 시작이 실패)
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

        // ④ 규칙 판정. spectators는 인원수가 아니라 3상태: ≥1 관전자 붙음 · 0 혼자 남음 · −1 나는 관전자.
        //   조건 `spectators <= 0`을 `== 0`으로 고치면 관전자 자동 정리가 조용히 죽는다(−1 케이스가 빠짐).
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
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
#endif
