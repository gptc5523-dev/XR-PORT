#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>시연 시나리오(PortDemoDirector) 에디터 스위치 + 배치모드 스모크 실행기.
    /// 메뉴로 에디터 ▶Play 시연을 켜고, SmokeRun 은 -batchmode 로 러너 위반(집기·안착·겹침·축 한계)을 0/1 로 판정한다.</summary>
    [InitializeOnLoad]
    public static class PortDemoMenu
    {
        const string MenuPath = "PLC/시연 시나리오 (에디터 Play)";
        const string ScenePath = StsPartNames.PortScenePath;
        const string SmokeKey = "PortDemo.Smoke", PrevKey = "PortDemo.SmokePrev", StartKey = "PortDemo.SmokeStart";
        const float TimeScale = 10f, LimitSeconds = 600f;

        static readonly Dictionary<string, int> placedBy = new Dictionary<string, int>();
        static int planned, stalls, ourExceptions, otherExceptions, holds, resumes;

        // 접근 멈춤 시험 — ApproachAt 초(벽시계)에 플레이어 리그를 조종기 크레인 발치로 옮겨 ApproachFor 초 두었다가 되돌린다.
        //   러너가 정격 감속으로 서고(검증 ④가 틱마다 감속을 잰다) 떠나면 이어서 돌아야 PASS — 멈춤·재개 로그를 센다.
        const float ApproachAt = 30f, ApproachFor = 8f;
        static bool approachStarted, approachEnded;
        static Vector3 rigHome;

        static void Approach(float elapsed)
        {
            // 대상 = 감독이 조종기를 준 크레인. 접속 전엔 접속 메뉴가 조종기를 꺼 둬 Active 가 빌 수 있다.
            // 그땐 감독의 첫 크레인(STS)으로 잡는다.
            StsCrane crane = StsCraneVRController.Active != null ? StsCraneVRController.Active.GetComponent<StsCrane>() : null;
            if (crane == null)
                foreach (var sc in Object.FindObjectsByType<StsCrane>())
                    if (sc.GetComponent<RtgBogieSteering>() == null) { crane = sc; break; }
            if (crane == null) return;
            var cam = Camera.main;
            if (cam == null)   // 배치 스모크엔 Camera.main 이 없을 수 있다 — 감독이 볼 카메라를 세운다
            {
                cam = new GameObject("PortDemoSmokeCam").AddComponent<Camera>();
                cam.tag = "MainCamera";
                Debug.Log("[PortDemoSmoke] Camera.main 없음 — 시험용 카메라 생성");
            }
            var rig = cam.transform.root;
            if (!approachStarted && elapsed >= ApproachAt)
            {
                approachStarted = true;
                rigHome = rig.position;
                Vector3 at = crane.Gantry is Component g ? g.transform.position : crane.transform.position;
                rig.position += new Vector3(at.x - cam.transform.position.x, 0f, at.z - cam.transform.position.z);
                Debug.Log($"[PortDemoSmoke] 접근 — 리그를 {crane.name} 발치로");
            }
            else if (approachStarted && !approachEnded && elapsed >= ApproachAt + ApproachFor)
            {
                approachEnded = true;
                rig.position = rigHome + new Vector3(1000f, 0f, 1000f);   // 원위치가 크레인 발자국 안일 수 있다(시험용 카메라는 원점) — 멀리 뺀다
                Debug.Log("[PortDemoSmoke] 떠남 — 리그를 크레인에서 멀리");
            }
        }

        // 플레이 진입의 도메인 리로드 뒤에도 스모크를 이어받는다.
        static PortDemoMenu()
        {
            if (!SessionState.GetBool(SmokeKey, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem(MenuPath, false, 20)]
        static void Toggle() =>
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, !EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));

        [MenuItem(MenuPath, true)]
        static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));
            return true;
        }

        public static void SmokeRun()
        {
            SessionState.SetBool(SmokeKey, true);
            SessionState.SetBool(PrevKey, EditorPrefs.GetBool(PortDemoDirector.EditorPrefKey, false));
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, true);
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
            if (type == LogType.Exception)
            {
                if (stack.Contains("CraneDemoRunner") || stack.Contains("PortDemoDirector")) ourExceptions++;
                else otherExceptions++;
            }
            if (!msg.StartsWith("[PortDemo] ")) return;
            int i = msg.IndexOf(" 놓기 ");
            if (i > 0)
            {
                string crane = msg.Substring(11, i - 11);
                placedBy[crane] = placedBy.TryGetValue(crane, out int n) ? n + 1 : 1;
            }
            if (msg.Contains(": 계획 ") && !msg.Contains(": 계획 0개")) planned++;
            if (msg.Contains("막힘")) stalls++;
            if (msg.Contains(": 멈춤 — 접근")) holds++;
            if (msg.EndsWith(": 재개")) resumes++;
        }

        /// <summary>야드 컨테이너가 칸 중심에서 벗어난 최대 거리(실척 m)와 그 이름 — 수식은 런타임 YardGrid 그대로 쓴다.
        /// 야드 블록 밖은 격자 대상이 아니므로 제외하고 개수만 센다(에이프런에 정당하게 놓일 수 있다).</summary>
        static float YardCellMaxErrM(out string worst)
        {
            worst = "없음";
            float max = 0f;
            int outside = 0, measured2 = 0;
            foreach (var lg in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude))
            {
                var t = lg.transform;
                // 이름 규약으로 야드 컨테이너만 — Regex 를 쓰면 이 파일에 using 이 없어 컴파일이 깨진다.
                if (!t.name.StartsWith("Cont20_") && !t.name.StartsWith("Cont40_")) continue;
                // 조건을 나눠 쓴다 — || 로 묶으면 단축 평가 때문에 컴파일러가 out 변수 할당을 증명하지 못한다(CS0165).
                if (!CraneDemoRunner.TryBounds(t, out var b)) continue;
                if (!YardGrid.TrySnapXZ(b.center, Mathf.Max(b.size.x, b.size.z), out Vector3 cell)) { outside++; continue; }
                measured2++;
                float d = new Vector2(b.center.x - cell.x, b.center.z - cell.z).magnitude * StsConfig.InvModelScale;
                if (d > max) { max = d; worst = t.name; }
            }
            if (measured2 == 0) worst = $"측정 0개(야드 밖 {outside}개)";
            else if (outside > 0) worst += $" (야드 밖 {outside}개 제외)";
            return max;
        }

        static void Tick()
        {
            float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);
            Approach(elapsed);
            int done = 0;
            foreach (int n in placedBy.Values) if (n >= 2 * CraneDemoRunner.Count) done++;   // 옮기고 되돌리기까지 — 두 방향 다
            bool finished = planned > 0 && done >= planned && approachEnded;
            if (!finished && ourExceptions == 0 && elapsed < LimitSeconds) return;
            float ringErr = PortDemoDirector.RingMaxErrM();
            // 자동 시나리오가 놓은 것도 칸 위인지 확인 — 수동 Release 정렬(FindSlot 라운딩)을 시나리오는 우회하므로 기계로 잰다.
            // 씬은 전수 칸 위에서 시작하므로, 끝났을 때 칸을 벗어난 게 있으면 그건 시나리오가 그렇게 놓은 것이다.
            float yardErr = YardCellMaxErrM(out string yardWorst);
            //   yardErr 허용 0.2m — 행 간 틈(0.4m)의 절반. 이보다 벗어나면 틈을 먹기 시작한다 = 라인 침범(YardSnapProbe 와 같은 기준).
            bool pass = finished && ourExceptions == 0 && CraneDemoRunner.Violations == 0 && holds > 0 && resumes > 0
                     && ringErr <= 0.1f && yardErr <= 0.2f;

            var parts = new List<string>();
            foreach (var kv in placedBy) parts.Add($"{kv.Key}={kv.Value}");
            Debug.Log($"[PortDemoSmoke] {(pass ? "PASS" : "FAIL")} — 계획 크레인 {planned}대, 놓기 [{string.Join(", ", parts)}], " +
                      $"막힘 {stalls}, 접근 멈춤 {holds} · 재개 {resumes}, 시나리오 예외 {ourExceptions}, 기타 예외 {otherExceptions}, {elapsed:F0}s(×{TimeScale}) | " +
                      $"검증 실패 {CraneDemoRunner.Violations} · 집기 정렬 최대 {CraneDemoRunner.MaxPickErrM:F3}m(≤0.36) · " +
                      $"안착 오차 최대 {CraneDemoRunner.MaxSupportErrM:F3}m(≤0.02) · 속도/정격 최대 {CraneDemoRunner.MaxSpeedRatio:F3}(≤1) · " +
                      // 상한은 CraneDemoRunner.AccelTripRatioMax = 2/TripMargin × 1.05 = 1.26(옛 라벨 1.2 는 오기).
                      // 숫자를 다시 베끼지 않고 규칙만 적는다 — 넘을 때만 실패.
                      $"가속/트립 최대 {CraneDemoRunner.MaxAccelRatio:F2}(상한 2/TripMargin×1.05, 초과 시에만 실패) · " +
                      $"접근 띠 경계 오차 최대 {ringErr:F3}m(≤0.1) · " +
                      // 자동 시나리오가 놓은 야드 컨테이너가 칸 중심에서 벗어난 최대 거리. 실패하면 어느 상자인지 이름으로 바로 좁힌다.
                      $"야드 칸 이탈 최대 {yardErr:F3}m(≤0.2) ← {yardWorst}");
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, SessionState.GetBool(PrevKey, false));
            SessionState.EraseBool(SmokeKey);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
