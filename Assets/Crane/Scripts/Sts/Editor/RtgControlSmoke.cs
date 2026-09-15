#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 배치 스모크 — 시연 씬에서 RTG 를 VR 조종할 수 있는지(오너 2026-09-15 "RTG 크레인은 내가 조종이 불가능").
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Container.Crane.Sts.EditorTools.RtgControlSmoke.Run -logFile rtg.log
    ///   오너 흐름 그대로: (호스트 접속) STS 조종 켬 → B 로 이동모드 → 걸어서 RTG 발치로.
    ///   PASS(종료 0) =
    ///     ① 걸어온 것만으로 그 RTG 가 조종기를 받고 조종 토글이 이어진다
    ///     ② 상태 HUD 가 조종기를 받은 크레인(STS → RTG)을 보여 준다
    ///     ③ 합성 스틱(QaBeginDrive/QaSticks) 1.5초씩 — 트롤리·호이스트·갠트리 실척 속도
    ///        |Δ축| × WorldPerUnit ÷ ModelScale ÷ Δt 가 StsCraneVRController 정격(3.3 · 공하 2.7 · 0.75 m/s) ±10%
    /// </summary>
    [InitializeOnLoad]
    public static class RtgControlSmoke
    {
        const string Key = "RtgControlSmoke", PrevKey = "RtgControlSmoke.Prev";
        const float StepS = 1.5f, Tol = 0.10f;
        const System.Reflection.BindingFlags Priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        static int step;
        static float t0, stepAt, a0, aTime;
        static StsCrane sts, rtg;
        static StsCraneVRController stsCtrl, rtgCtrl;
        static bool switchedByWalk, carried, switched, hudSts, hudRtg, fail;
        static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();

        static RtgControlSmoke()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Run()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetBool(PrevKey, EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, true);   // 시연 감독이 크레인마다 조종기를 붙이고 가까운 한 대만 켠다
            EditorSceneManager.OpenScene("Assets/Scenes/Port.unity");
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            t0 = (float)EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static float Wall => (float)EditorApplication.timeSinceStartup - t0;

        static void Tick()
        {
            if (Wall > 90f) { Log("시간 초과"); fail = true; Finish(); return; }
            switch (step)
            {
                case 0:   // 감독 Start·계획이 끝날 때까지
                    if (Wall < 4f) return;
                    foreach (var c in Object.FindObjectsByType<StsCrane>(FindObjectsSortMode.InstanceID))
                    {
                        bool isRtg = c.GetComponent<RtgBogieSteering>() != null;
                        if (!isRtg && sts == null) sts = c;
                        if (isRtg && (rtg == null || string.CompareOrdinal(c.name, rtg.name) < 0)) rtg = c;
                    }
                    if (sts == null || rtg == null) { Log("STS/RTG 못 찾음"); fail = true; Finish(); return; }
                    stsCtrl = sts.GetComponent<StsCraneVRController>();
                    rtgCtrl = rtg.GetComponent<StsCraneVRController>();
                    if (stsCtrl == null || rtgCtrl == null) { Log($"조종기 없음 — STS {stsCtrl != null}, {rtg.name} {rtgCtrl != null}"); fail = true; Finish(); return; }
                    // 호스트 접속 상태로 — 배치엔 네트워크가 없어 넷 메뉴(CraneNetMenuHUD)가 조종기를 계속 막는다. 접속 순간과 같게 되살리고 뗀다.
                    foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                        if (mb.GetType().Name == "CraneNetMenuHUD")
                        {
                            mb.enabled = false;   // 먼저 끈다 — Destroy 는 프레임 끝이라 그 사이 Update 가 한 번 더 막았다(시작 HUD 판정 오염)
                            Log($"넷 메뉴가 막아 둔 조종기 = {Name(mb.GetType().GetField("craneController", Priv)?.GetValue(mb) as Object)} — 호스트 접속처럼 되살림");
                            mb.GetType().GetMethod("RestoreController", Priv)?.Invoke(mb, new object[] { true });
                            Object.Destroy(mb);
                        }
                    // 오너 흐름: STS 조종 켬 → 조종모드 → B 로 이동모드(조종 토글은 켠 채)
                    stsCtrl.QaBeginDrive(StsCraneVRController.Mode.Crane);
                    stsCtrl.QaBeginDrive(StsCraneVRController.Mode.Move);
                    Next(); return;

                case 1:   // 걸어서 RTG 발치로
                    if (Wall - stepAt < 1f) return;
                    int on = 0;
                    foreach (var vc in Object.FindObjectsByType<StsCraneVRController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (vc.enabled) on++;
                    hudSts = HudCrane() == sts && StsCraneVRController.Active == stsCtrl && on == 1;   // 켜진 조종기는 STS 한 대
                    Log($"접속 직후 — 켜진 조종기 {on}대(=1), Active {Name(StsCraneVRController.Active)}, 상태 HUD {Name(HudCrane())} ({(hudSts ? "OK" : "FAIL")})");
                    var cam = Camera.main;
                    if (cam == null) { Log("Camera.main 없음"); fail = true; Finish(); return; }
                    CraneDemoRunner.TryBounds(rtg.transform, out var b);
                    cam.transform.root.position += new Vector3(b.center.x - cam.transform.position.x, 0f, b.center.z - cam.transform.position.z);
                    Log($"리그 → {rtg.name} 발치 {b.center:F3}");
                    Next(); return;

                case 2:   // 조종기를 받았나(감독 선택 주기 0.2초)
                    if (Wall - stepAt < StepS) return;
                    switchedByWalk = StsCraneVRController.Active == rtgCtrl;
                    carried = switchedByWalk && rtgCtrl.ControlActive;
                    Log($"이동모드로 걸어와서 조종기 → {Name(StsCraneVRController.Active)} ({(switchedByWalk ? "RTG 받음" : "RTG 못 받음")}), 조종 토글 이어받음 {carried}");
                    if (!switchedByWalk) stsCtrl.QaEndDrive();   // 오른스틱 클릭으로 STS 조종을 끈 것과 같다
                    Next(); return;

                case 3:
                    if (Wall - stepAt < StepS) return;
                    switched = StsCraneVRController.Active == rtgCtrl;
                    hudRtg = HudCrane() == rtg;
                    Log($"조종기 → {Name(StsCraneVRController.Active)}, 상태 HUD → {Name(HudCrane())} ({(hudRtg ? "OK" : "FAIL")})");
                    if (!switched) { fail = true; Finish(); return; }
                    rtgCtrl.QaBeginDrive(StsCraneVRController.Mode.Crane);
                    Begin(rtg.Trolley, out float dt0);
                    rtgCtrl.QaSticks(new Vector2(dt0, 0f), Vector2.zero);
                    Next(); return;

                case 4:   // 트롤리
                    if (Wall - stepAt < StepS) return;
                    Check("트롤리", rtg.Trolley, 3.3f);
                    Begin(rtg.Spreader, out float dh);
                    rtgCtrl.QaSticks(Vector2.zero, new Vector2(0f, dh));
                    Next(); return;

                case 5:   // 호이스트
                    if (Wall - stepAt < StepS) return;
                    var at = rtg.Attach;
                    float tons = at != null && at.HasContainer ? at.AttachedLoadTons : 0f;
                    Check("호이스트", rtg.Spreader, tons > 0f ? Mathf.Lerp(1.8f, 0.9f, Mathf.InverseLerp(8f, 32f, tons)) : 2.7f);
                    rtgCtrl.QaBeginDrive(StsCraneVRController.Mode.Gantry);
                    Begin(rtg.Gantry, out float dg);
                    rtgCtrl.QaSticks(Vector2.zero, new Vector2(dg, 0f));
                    Next(); return;

                case 6:   // 갠트리
                    if (Wall - stepAt < StepS) return;
                    Check("갠트리", rtg.Gantry, 0.75f);
                    rtgCtrl.QaEndDrive();
                    Finish(); return;
            }
        }

        static StsCrane HudCrane()
        {
            var hud = Object.FindAnyObjectByType<CraneStatusHUD>();
            return hud != null ? typeof(CraneStatusHUD).GetField("crane", Priv)?.GetValue(hud) as StsCrane : null;
        }

        // 가까운 끝단 반대쪽으로 민다 — 1.5초에 끝단에 닿아 속도가 잘리지 않게.
        static void Begin(IAxisMover a, out float dir)
        {
            dir = a != null && (a.Current - a.Min) < (a.Max - a.Current) ? 1f : -1f;
            a0 = a != null ? a.Current : 0f;
            aTime = Time.time;
        }

        static void Check(string what, IAxisMover a, float wantMps)
        {
            if (a == null) { Log($"{what}: 무버 없음"); fail = true; return; }
            float dt = Mathf.Max(Time.time - aTime, 1e-3f);
            float mps = Mathf.Abs(a.Current - a0) * a.WorldPerUnit / StsConfig.ModelScale / dt;
            bool ok = Mathf.Abs(mps - wantMps) <= wantMps * Tol;
            if (!ok) fail = true;
            Log($"{what}: {a0:F4}→{a.Current:F4} ({dt:F2}s) 실척 {mps:F2} m/s, 정격 {wantMps:F2} — {(ok ? "OK" : "FAIL")}" +
                $"{(a is AxisMoverBase m && m.IsBlocked ? " (장애물 정지)" : "")}");
        }

        static void Next() { step++; stepAt = Wall; }
        static string Name(Object o) => o != null ? o.name : "없음";
        static void Log(string s) { report.AppendLine(s); Debug.Log($"[RtgControlSmoke] {s}"); }

        static void Finish()
        {
            bool pass = !fail && switchedByWalk && carried && switched && hudSts && hudRtg;
            Debug.Log($"[RtgControlSmoke] {(pass ? "PASS" : "FAIL")} — 걸어와서 받음 {switchedByWalk} · 토글 이어받음 {carried} · 조종기 RTG {switched} · " +
                      $"HUD STS {hudSts}→RTG {hudRtg}\n{report}");
            EditorApplication.update -= Tick;
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, SessionState.GetBool(PrevKey, false));
            SessionState.EraseBool(Key);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
