#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Container.Crane.Sts.Plc;
using ContainerProject;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 배치 검사 — 바닥(데크 y=0) 관통. 오너 2026-09-16 "컨테이너가 바닥을 뚫는다" 스크린샷 재현.
    ///   ① 하강: 든 채로 '받침이 없는 빈 데크' 위에서 권상을 끝까지 내려도 컨테이너 밑면이 바닥 아래로 가면 안 된다.
    ///      (SpreaderGrabber 통과방지 클램프는 받침 컨테이너가 있을 때만(over) 걸린다 — 빈 땅에서는 하한이 없다.)
    ///   ② 복구: 이미 바닥 아래에 있는 컨테이너는 ContainerPhysicsStabilizer 바닥가드가 끌어올려야 한다.
    ///      런타임에 강체가 붙는(=Start 목록에 없는) kinematic 야드 컨테이너가 가드의 사각이었다.
    /// 판정은 바닥 y 하나로만 한다 — 콘 돌출·삽입 깊이 값에는 기대지 않는다(그 값은 재측정 중, xr-port-04 2026-09-16).
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Container.Crane.Sts.EditorTools.FloorClipProbe.Run -logFile floor.log
    ///   ※ -quit 금지 — EnterPlaymode 방식이라 주면 플레이에 못 들어가고 로그가 빈다.
    /// </summary>
    [InitializeOnLoad]
    public static class FloorClipProbe
    {
        const string Key = "FloorClipProbe", PrevKey = "FloorClipProbe.Prev", ScenePath = "Assets/Scenes/Port.unity";
        const float LiftU = 0.15f;      // 잡은 뒤 들어 올리는 높이(모델 단위) — 빈 자리로 옮기는 동안 받침에 안 걸리게
        const float SinkU = 0.30f;      // ② 바닥 아래로 내려 두는 깊이 — 컨테이너 반높이(≈0.054)보다 훨씬 깊게
        const float TolU = 0.010f;      // 허용 침투 — 가드 스킨(0.004) + 여유. 이보다 깊으면 관통으로 본다
        const float DriveSec = 1.2f;    // 권상 하강을 밀어붙이는 시간(VR 스틱을 계속 내리는 상황)

        struct Case { public StsCrane crane; public SpreaderGrabber g; public Transform box; }
        static readonly List<Case> cases = new List<Case>();
        static readonly List<Transform> boxes = new List<Transform>();
        static int idx, phase, fails, measured, skipped;
        static float waitUntil, driveUntil, floorTop;
        static Transform sinkBox;

        static FloorClipProbe()
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
            waitUntil = Time.time + 1f;   // 그랩버·무버·바닥가드 Start 뒤
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || Time.time < waitUntil) return;
            try { Step(); }
            catch (System.Exception e) { Debug.LogError("[FloorClipProbe] 예외 " + e); fails++; Finish(); }
        }

        static void Wait(float s) => waitUntil = Time.time + s;

        static void Step()
        {
            if (phase == 0) { Setup(); phase = 1; Wait(0.3f); return; }

            if (idx >= cases.Count) { StepGuard(); return; }   // ① 전부 끝나면 ② 가드 복구 검사

            var c = cases[idx];
            switch (phase)
            {
                case 1:   // 컨테이너를 콘 밑으로 가져온다(크레인 축 정렬 대신 — 검사 대상은 '잡은 뒤 하강'이다)
                    if (!CraneDemoRunner.TryBounds(c.box, out var b1)) { Skip(c, "바운즈 없음"); return; }
                    Vector3 gp = c.g.GrabPoint();
                    float topTarget = c.g.ConeBottomY() + c.g.InsertDepthMeters * StsConfig.ModelScale;   // 콘이 박힌 안착 자세
                    Move(c.box, new Vector3(gp.x - b1.center.x, topTarget - b1.max.y, gp.z - b1.center.z));
                    phase = 2; Wait(0.2f); return;

                case 2:
                    c.g.Grab();
                    phase = 3; Wait(0.8f); return;   // 텔레스코프 신축(0.25 m/s)

                case 3:   // 받침이 하나도 없는 자리로 옮긴다 — 여기가 이 검사의 핵심 조건(빈 데크)
                    if (c.crane.Attach == null || c.crane.Attach.AttachedContainer != c.box)
                    {
                        // 왜 게이트에 걸렸는지 숫자로 남긴다 — 이게 없으면 '안 잡힘'이 회귀인지 원래 그런지 구분이 안 된다.
                        CraneDemoRunner.TryBounds(c.box, out var bs);
                        float gapMm = (c.g.ConeBottomY() - bs.max.y) / StsConfig.ModelScale * 1000f;   // 음수 = 콘이 박힌 깊이
                        // '실제로 잡힌 것' 이 다른 컨테이너면 FindNearest 가 옆칸을 골랐다는 뜻(gap 이 −InsertU 인데 이름이 다를 때).
                        //   gap 이 크게 양수면 대상이 콘 밑에 없었던 것 — 원인이 갈린다(xr-port-ae 2026-09-16 제안).
                        var got = c.crane.Attach != null ? c.crane.Attach.AttachedContainer : null;
                        Skip(c, $"안 잡힘 — 콘바닥−윗면 {gapMm:+0;-0}mm(실척), 설정 삽입 {c.g.InsertDepthMeters * 1000f:F0}mm, " +
                                $"실제 잡힌 것 {(got != null ? got.name : "없음")}");
                        return;
                    }
                    var hoist = c.crane.Spreader;
                    hoist.MoveTo(hoist.Current + LiftU / Mathf.Max(hoist.WorldAxis.magnitude, 1e-6f));
                    if (!CraneDemoRunner.TryBounds(c.box, out var b3) || !MoveToBareSpot(c, b3)) { Skip(c, "빈 자리 없음"); return; }
                    phase = 4; Wait(0.4f); return;

                case 4:   // 권상을 하한까지 계속 밀어 내린다(스틱을 계속 내리는 상황)
                    if (driveUntil <= 0f) driveUntil = Time.time + DriveSec;
                    c.crane.Spreader.MoveTo(c.crane.Spreader.Min - 1000f);   // MoveTo 가 하한으로 클램프
                    if (Time.time < driveUntil) return;
                    driveUntil = 0f;
                    phase = 5; Wait(0.3f); return;

                case 5:   // 실측 — 바닥 아래로 내려갔나
                    CraneDemoRunner.TryBounds(c.box, out var b5);
                    float pen = floorTop - b5.min.y;
                    bool ok = pen <= TolU;
                    measured++; if (!ok) fails++;
                    Debug.Log($"[FloorClipProbe] {(ok ? "OK " : "BAD")} ①하강 {c.crane.name} {c.box.name} — " +
                              $"밑면 {b5.min.y:F4}u, 바닥 {floorTop:F4}u, 관통 {pen * 1000f / StsConfig.ModelScale:F0}mm(실척), 허용 {TolU:F3}u");
                    c.g.Release();
                    idx++; phase = 1; Wait(0.3f); return;
            }
        }

        static void Skip(Case c, string why)
        {
            Debug.Log($"[FloorClipProbe] 건너뜀 ①하강 {c.crane.name} {c.box.name} — {why}");
            if (c.crane.Attach != null && c.crane.Attach.HasContainer) c.g.Release();
            skipped++; idx++; phase = 1; Wait(0.2f);
        }

        // ② 바닥 아래 컨테이너를 바닥가드가 끌어올리는가 — 런타임에 강체가 붙은(=Start 목록에 없는) kinematic 야드 컨테이너로 잰다.
        static void StepGuard()
        {
            if (phase != 20)
            {
                sinkBox = boxes.FirstOrDefault(t => t != null && t.GetComponent<Rigidbody>() != null
                                                 && t.GetComponentInParent<StsCrane>() == null);
                if (sinkBox == null) { Debug.LogError("[FloorClipProbe] ②복구 대상 컨테이너 없음"); fails++; Finish(); return; }
                var rb = sinkBox.GetComponent<Rigidbody>();
                Vector3 p = sinkBox.position - new Vector3(0f, SinkU, 0f);
                sinkBox.position = p; rb.position = p;
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                CraneDemoRunner.TryBounds(sinkBox, out var sb);
                Debug.Log($"[FloorClipProbe] ②복구 대상 {sinkBox.name}(kinematic {rb.isKinematic}) — 밑면 {sb.min.y:F4}u 로 내려놓음");
                phase = 20; Wait(1.5f); return;   // 가드가 매 FixedUpdate 로 끌어올릴 시간
            }

            CraneDemoRunner.TryBounds(sinkBox, out var b);
            float pen = floorTop - b.min.y;
            bool ok = pen <= TolU;
            measured++; if (!ok) fails++;
            Debug.Log($"[FloorClipProbe] {(ok ? "OK " : "BAD")} ②복구 {sinkBox.name} — " +
                      $"밑면 {b.min.y:F4}u, 바닥 {floorTop:F4}u, 남은 관통 {pen * 1000f / StsConfig.ModelScale:F0}mm(실척), 허용 {TolU:F3}u");
            Finish();
        }

        // 든 컨테이너 발밑에 받침 강체가 하나도 없는 자리로 축을 옮긴다(트롤리 먼저, 없으면 갠트리).
        //   판정 집합은 통과방지 클램프와 같다 — 크레인 자식(스프레더·든 화물)을 뺀 모든 강체.
        static bool MoveToBareSpot(Case c, Bounds held)
        {
            var others = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(rb => rb != null && !rb.transform.IsChildOf(c.crane.transform))
                .Select(rb => CraneDemoRunner.TryBounds(rb.transform, out var ob) ? (Bounds?)ob : null)
                .Where(ob => ob.HasValue).Select(ob => ob.Value).ToList();

            foreach (var a in new[] { c.crane.Trolley, c.crane.Gantry })
            {
                if (a == null) continue;
                Vector3 w = a.WorldAxis;
                if (w.sqrMagnitude < 1e-12f) continue;
                for (int i = 0; i <= 40; i++)
                {
                    float t = Mathf.Lerp(a.Min, a.Max, i / 40f);
                    Bounds probe = held; probe.center += w * (t - a.Current);
                    if (others.Any(o => Overlap(probe, o))) continue;
                    a.MoveTo(t);
                    return true;
                }
            }
            return false;
        }

        // XZ 겹침(여유 0.02u) — 바로 밑에 깔린 받침이 될 수 있는지만 본다. 높이는 안 본다(내리면 만나므로).
        static bool Overlap(Bounds a, Bounds b)
        {
            const float m = 0.02f;
            return a.min.x - m < b.max.x && a.max.x + m > b.min.x
                && a.min.z - m < b.max.z && a.max.z + m > b.min.z;
        }

        // 옮기기 전에 kinematic 으로 고정한다 — 배 컨테이너는 중력을 받는 동적 강체라, 파킹 높이(공중)의 콘 밑으로
        //   옮겨 두면 Grab() 하기 전에 도로 갑판으로 떨어진다. 2026-09-16 실측: STS 가 '안 잡힘'으로 건너뛴 원인이
        //   이것이었다(건너뜀 로그의 콘바닥−윗면 +30,599mm = 떨어져 돌아간 거리. 크레인이나 게이트 문제가 아니다).
        //   잡히면 어차피 부착 쪽에서 kinematic 이 되고, 놓은 뒤 남는 kinematic 은 바닥가드가 본다.
        static void Move(Transform t, Vector3 d)
        {
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                rb.isKinematic = true;
            }
            t.position += d;
            if (rb != null) rb.position = t.position;
        }

        static void Setup()
        {
            foreach (var b in Object.FindObjectsByType<PlcBridge>(FindObjectsSortMode.None)) b.enabled = false;   // PLC 재생이 축을 잡지 않게
            floorTop = ContainerPhysicsStabilizer.FindFloorTopY(out bool hasFloor);

            var yardRx = new Regex(@"^Cont(20|40)_\d+$");
            boxes.Clear();
            boxes.AddRange(Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Select(l => l.transform)
                .Where(t => t.name.StartsWith("ShipContainer") || yardRx.IsMatch(t.name))
                .OrderBy(t => t.name));

            // 야드 컨테이너에 콜라이더·강체를 붙인다 — PortDemoDirector.MakeGrabbable 과 같은 구성이고,
            //   '플레이 시작 뒤에 붙는 kinematic 강체' 자체가 바닥가드의 사각(②의 대상)이다.
            foreach (var t in boxes.Where(x => yardRx.IsMatch(x.name)))
            {
                if (!CraneDemoRunner.TryBounds(t, out var ob)) continue;
                if (t.GetComponentInChildren<Collider>() == null)
                {
                    var s = t.lossyScale; var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = t.InverseTransformPoint(ob.center);
                    bc.size = new Vector3(ob.size.x / s.x, ob.size.y / s.y, ob.size.z / s.z);
                }
                var rb = t.GetComponent<Rigidbody>();
                if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;
            }

            foreach (var crane in Object.FindObjectsByType<StsCrane>(FindObjectsSortMode.None).OrderBy(c => c.name))
            {
                var g = crane.GetComponent<SpreaderGrabber>();
                if (g == null || crane.Attach == null || crane.Spreader == null) continue;
                if (crane.Trolley is TrolleyMover tm) tm.StopOnObstacle = false;
                if (crane.Gantry is GantryMover gm) gm.StopOnObstacle = false;
                bool rtg = crane.GetComponent<RtgBogieSteering>() != null;
                Vector3 me = crane.Gantry is Component gc ? gc.transform.position : crane.transform.position;
                var box = boxes.Where(t => rtg ? yardRx.IsMatch(t.name) : t.name.StartsWith("ShipContainer"))
                    .Where(t => CraneDemoRunner.TryBounds(t, out _))
                    .OrderBy(t => { CraneDemoRunner.TryBounds(t, out var bb); return (bb.center - me).sqrMagnitude; })
                    .FirstOrDefault();
                if (box == null) continue;
                cases.Add(new Case { crane = crane, g = g, box = box });
            }
            Debug.Log($"[FloorClipProbe] 준비 — 바닥 윗면 y={floorTop:F4}u({(hasFloor ? "VirtualFloor" : "데크 y=0 규약")}), " +
                      $"컨테이너 {boxes.Count}개, ①하강 {cases.Count}건");
        }

        static void Finish()
        {
            bool pass = measured > 0 && fails == 0;
            Debug.Log($"[FloorClipProbe] {(pass ? "PASS" : "FAIL")} — 측정 {measured}, 실패 {fails}, 건너뜀 {skipped}");
            EditorApplication.update -= Tick;
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, SessionState.GetBool(PrevKey, false));
            SessionState.EraseBool(Key);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
