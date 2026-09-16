#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Container.Crane.Sts.Plc;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 배치 검사 — 수동 집기(SpreaderGrabber.Grab, VR Y 버튼과 같은 경로)가 컨테이너를 제자리에서 그대로 매다는지.
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Container.Crane.Sts.EditorTools.StsGrabProbe.Run -logFile grab.log
    /// 크레인마다 컨테이너 몇 개(STS = 위가 빈 배 컨테이너, RTG = 야드 40ft·20ft)에 트위스트락 중심을 윗면 중심에 맞추고 Grab() →
    ///   ① 그 컨테이너가 잡혔나 ② 잡는 순간 튀지 않았나(바운즈 중심 수평 이동 ≤ 0.015u, 윗면 높이 변화 ≤ 0.003u, 회전 변화 &lt; 1°)
    ///   ③ 트위스트락 중심 ↔ 바운즈 중심 수평 ≤ 0.015u ④ 들어 올린 뒤에도 ③·회전 유지. 전부 맞으면 종료 코드 0.
    /// </summary>
    [InitializeOnLoad]
    public static class StsGrabProbe
    {
        const string Key = "StsGrabProbe", PrevKey = "StsGrabProbe.Prev", ScenePath = "Assets/Scenes/Port.unity";
        const float TolXZ = 0.015f, TolY = 0.003f, LiftU = 0.05f;
        // 오너 2026-09-16 "락 거는 부분이 컨테이너 안으로 안 들어가" — 케이스 2종으로 나눠 잰다.
        //   · 호버(HoverM 위에서 Y 누름): 잠기면 실패. 공중 체결은 실물에 없다.
        //   · 안착(통과방지 클램프가 멈추는 데까지 내림): 잠겨야 하고, 콘이 InsertDepthMeters ± InsertBandM 만큼 박혀야 한다.
        //   · 과하강(OverdriveM 아래까지 밀어 내림): 통과방지 클램프가 삽입깊이에서 멈춰 세워야 한다. 안 멈추면 콘이 컨테이너를 뚫는다.
        //   허용오차 = min(InsertBandAbsM, 깊이 × InsertBandFrac) — 클램프가 정확히 InsertDepthMeters 에 세우므로 실제 오차는
        //   부동소수 수준이다. 옛 고정 ±20mm 는 STS 24mm 에 대해 검사가 아니었다(깊이의 80% 가 틀려도 통과) — xr-port-ae 지적.
        const float HoverM = 0.2f, OverdriveM = 0.2f, InsertBandAbsM = 0.005f, InsertBandFrac = 0.25f;

        // targetOffM = 콘 바닥을 컨테이너 윗면 대비 어디로 보낼지(실척 m, + 위 / − 아래). 최종 높이는 클램프가 정할 수 있다.
        struct Case { public StsCrane crane; public SpreaderGrabber grabber; public Transform box; public float targetOffM; public bool expectLock; }
        static readonly List<Case> cases = new List<Case>();
        static int idx, phase, fails, measured, skipped;
        static float waitUntil;
        static Bounds before; static Quaternion rotBefore; static Vector3 posBefore; static Transform parentBefore; static bool kinBefore;

        static StsGrabProbe()
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
            waitUntil = Time.time + 1f;   // 그랩버·흔들림 노드·텔레스코프 Start 뒤
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || Time.time < waitUntil) return;
            try { Step(); }
            catch (System.Exception e) { Debug.LogError("[StsGrabProbe] 예외 " + e); fails++; Finish(); }
        }

        static void Wait(float s) => waitUntil = Time.time + s;

        static void Step()
        {
            if (phase == 0) { Setup(); phase = 1; Wait(0.2f); return; }
            if (idx >= cases.Count) { Finish(); return; }
            var c = cases[idx];
            switch (phase)
            {
                case 1:   // 수평은 트위스트락 중심을 윗면 중심에, 높이는 '콘 바닥'을 목표로(원점이 아니라 실측 기하)
                    CraneDemoRunner.TryBounds(c.box, out before);
                    rotBefore = c.box.rotation; posBefore = c.box.position; parentBefore = c.box.parent;
                    var rb = c.box.GetComponent<Rigidbody>(); kinBefore = rb != null && rb.isKinematic;
                    Vector3 gp1 = c.grabber.GrabPoint();
                    float targetConeY = before.max.y + c.targetOffM * StsConfig.ModelScale;
                    Vector3 d = new Vector3(Top(before).x - gp1.x,
                                            targetConeY - BottomY(c.crane, null, cones: true),
                                            Top(before).z - gp1.z);
                    MoveBy(c.crane.Gantry, new Vector3(d.x, 0f, d.z));
                    MoveBy(c.crane.Trolley, new Vector3(d.x, 0f, d.z));
                    MoveBy(c.crane.Spreader, new Vector3(0f, d.y, 0f));
                    phase = 2; Wait(0.4f); return;   // 통과방지 클램프가 되밀어 정착할 시간
                case 2:   // 수평이 맞았으면 잡기(높이는 클램프가 정한 그대로)
                    Vector3 gp2 = c.grabber.GrabPoint();
                    float missXZ = new Vector2(Top(before).x - gp2.x, Top(before).z - gp2.z).magnitude;
                    if (missXZ > 0.005f)
                    {
                        Debug.Log($"[StsGrabProbe] 건너뜀 {c.crane.name} {c.box.name} — 트위스트락이 윗면 중심에 못 감(수평 {missXZ:F4}u)");
                        skipped++; idx++; phase = 1; return;
                    }
                    c.grabber.Grab();
                    phase = 3; Wait(1.2f); return;   // 텔레스코프 신축(0.25 m/s) 끝날 때까지
                case 3:
                    Measure(c, "잡음");
                    var h = c.crane.Spreader;
                    h.MoveTo(h.Current + LiftU / h.WorldPerUnit);
                    phase = 4; Wait(0.3f); return;
                case 4:
                    Measure(c, "들어올림");
                    c.grabber.Release();
                    c.box.SetParent(parentBefore, true);
                    c.box.SetPositionAndRotation(posBefore, rotBefore);
                    var rb2 = c.box.GetComponent<Rigidbody>();
                    if (rb2 != null) { rb2.isKinematic = kinBefore; if (!kinBefore) { rb2.linearVelocity = Vector3.zero; rb2.angularVelocity = Vector3.zero; } }
                    idx++; phase = 1; Wait(0.3f); return;
            }
        }

        static Vector3 Top(Bounds b) => new Vector3(b.center.x, b.max.y, b.center.z);

        // SpreaderGrabber.Awake 와 똑같은 이름 규약으로 모은 콘 — RTG 에서 0개면 그랩버가 콘을 못 찾는다는 증거.
        static List<Transform> Cones(StsCrane crane) => crane.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("Twistlock_Cone") || t.name.StartsWith("Spreader_Twistlock_"))   // Span() 과 같은 규약(Numbered 접미사 포함)
            .ToList();

        // 렌더러 실측 최저점 — held(매단 컨테이너) 렌더러는 뺀다. cones=true 면 규약 일치 콘만, false 면 스프레더 전체.
        static float BottomY(StsCrane crane, Transform held, bool cones)
        {
            var roots = cones ? Cones(crane) : new List<Transform> { ((Component)crane.Spreader).transform };
            float y = float.MaxValue;
            foreach (var root in roots)
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                    if (held == null || !r.transform.IsChildOf(held)) y = Mathf.Min(y, r.bounds.min.y);
            if (y == float.MaxValue && cones) return BottomY(crane, held, cones: false);   // 콘 미탐색 폴백 — 러너 SpreaderBottomY 와 같은 식
            return y;
        }

        // 콘이 스프레더 구조물(빔·플리퍼 등 콘 아닌 부재) 밑으로 나온 길이(실척 m) = 삽입 깊이의 물리 상한.
        //   이만큼 박으면 구조물 밑면이 컨테이너 윗면에 닿는다 — 실물 안착 자세. 오너 2026-09-16 "40mm 로는 부족".
        //   wholeAssembly=false: 'Twistlock_Cone*'(절차)·'Spreader_Twistlock_*'(FBX) 만 콘으로 제외.
        //   wholeAssembly=true: 이름에 Twistlock 이 든 부재를 전부 제외 → 빔·플리퍼 등 진짜 스프레더 구조물까지의 거리.
        //   ★ 2026-09-16 실측 결론: 두 기준이 세 크레인 모두 같은 값 — STS 24mm ← Beam_Flange_1 · RTG 56mm ← EndBeam_F_Body.
        //     '콘 바로 위 Twistlock_Head/Body 로드가 구조물로 잡혀 값이 작게 나온다'던 내 가설은 틀렸다. 바닥을 정하는 건 실제 빔이다.
        //   ★ StsCraneCreator 주석의 '빔 밑 노출 0.005u ≈ 120mm' 와 24mm 는 모순이 아니다 — 주석은 End_Beam 밑면(−0.015) 기준이고
        //     실제 최저 부재는 그보다 낮은 Beam_Flange_1 이다(xr-port-ae 검산). 그 주석을 '틀렸다'고 고치지 말 것.
        //     내 커밋 ad0f17c 메시지가 '주석과 안 맞는다'고 쓴 건 이 구분을 몰랐을 때다 — 삽입 깊이로 읽지만 않으면 둘 다 맞다.
        //   두 기준을 남겨 두는 이유: 모델이 바뀌어 구조 최저 부재가 트위스트락 계열로 바뀌면 두 값이 갈라져 바로 드러난다.
        static float ProtrusionM(StsCrane crane, out string part, bool wholeAssembly = false)
        {
            part = "없음";
            if (crane == null || crane.Spreader == null) return 0f;
            var cones = Cones(crane);
            float coneB = BottomY(crane, null, cones: true);
            float bodyB = float.MaxValue;
            foreach (var r in ((Component)crane.Spreader).transform.GetComponentsInChildren<Renderer>())
            {
                bool skip = wholeAssembly && r.transform.name.Contains("Twistlock");
                if (!skip)
                    foreach (var c in cones)
                        if (r.transform == c || r.transform.IsChildOf(c)) { skip = true; break; }
                if (skip) continue;
                if (r.bounds.min.y < bodyB) { bodyB = r.bounds.min.y; part = r.transform.name; }
            }
            return bodyB < float.MaxValue ? (bodyB - coneB) / StsConfig.ModelScale : 0f;
        }

        static void MoveBy(IAxisMover a, Vector3 d)
        {
            if (a == null) return;
            Vector3 w = a.WorldAxis;
            if (w.sqrMagnitude > 1e-12f) a.MoveTo(a.Current + Vector3.Dot(d, w) / w.sqrMagnitude);
        }

        static void Measure(Case c, string stage)
        {
            var attach = c.crane.Attach;
            bool same = attach != null && attach.HasContainer && attach.AttachedContainer == c.box;
            CraneDemoRunner.TryBounds(c.box, out var hb);
            Vector3 gp = c.grabber.GrabPoint();
            float dxz = new Vector2(hb.center.x - gp.x, hb.center.z - gp.z).magnitude;
            float jumpXZ = new Vector2(hb.center.x - before.center.x, hb.center.z - before.center.z).magnitude;
            float dTop = hb.max.y - before.max.y - (stage == "잡음" ? 0f : LiftU);
            float rot = Quaternion.Angle(rotBefore, c.box.rotation);
            bool longZ = hb.size.z > hb.size.x, longZBefore = before.size.z > before.size.x;
            Span(c.crane, out float spanX, out float spanZ);
            bool spreaderLongZ = spanZ > spanX;
            // 삽입 = 윗면 − 콘 바닥(실척 m, 양수 = 박힘). 잠긴 케이스는 밴드 안이어야, 호버 케이스는 애초에 안 잠겨야 정상.
            float insertM = (hb.max.y - BottomY(c.crane, c.box, cones: true)) / StsConfig.ModelScale;
            float want = c.grabber.InsertDepthMeters;
            float tol = Mathf.Min(InsertBandAbsM, InsertBandFrac * want);
            bool band = Mathf.Abs(insertM - want) <= tol;
            bool ok = c.expectLock
                ? same && band && dxz <= TolXZ && rot < 1f && longZ == longZBefore
                  && (stage != "잡음" || (jumpXZ <= TolXZ && Mathf.Abs(dTop) <= TolY))
                : !same;
            measured++;
            if (!ok) fails++;
            Debug.Log($"[StsGrabProbe] {(ok ? "OK " : "BAD")} {stage} {c.crane.name} {c.box.name} — 잡힘 {same}" +
                      $"{(same ? "" : $"(실제 {(attach != null && attach.AttachedContainer != null ? attach.AttachedContainer.name : "없음")})")}, " +
                      $"트위스트락↔중심 {dxz:F4}u, 튄 거리 수평 {jumpXZ:F4}u·윗면 {dTop:+0.0000;-0.0000}u, 회전 {rot:F1}°, " +
                      $"컨테이너 긴축 {(longZ ? "Z" : "X")}(전 {(longZBefore ? "Z" : "X")}) {Mathf.Max(hb.size.x, hb.size.z):F3}u, " +
                      $"스프레더 긴축 {(spreaderLongZ ? "Z" : "X")} 콘 간격 {Mathf.Max(spanX, spanZ):F3}u");

            // 오너 2026-09-16 "락 거는 부분이 컨테이너 안으로 안 들어가" — 콘이 실제로 박혔는지 실측.
            //   삽입 = 윗면 y − 콘 바닥 y (양수 = 그만큼 박힘, 음수 = 그만큼 떠 있음). 콘 기준·스프레더 기준을 같이 찍어 어느 쪽을 써야 할지 본다.
            float coneB = BottomY(c.crane, c.box, cones: true), spB = BottomY(c.crane, c.box, cones: false);
            float apY = c.crane.Attach.AttachAnchor.position.y;
            Debug.Log($"[StsGrabProbe] 삽입 {stage} {c.crane.name} {c.box.name} — 윗면 {hb.max.y:F4}u, " +
                      $"콘바닥 {(coneB < float.MaxValue ? $"{coneB:F4}u 삽입 {(hb.max.y - coneB) / StsConfig.ModelScale * 1000f:+0;-0}mm(실척)" : "없음(콘 미탐색)")}, " +
                      $"스프레더최저 {spB:F4}u 삽입 {(hb.max.y - spB) / StsConfig.ModelScale * 1000f:+0;-0}mm, " +
                      $"부착점 {apY:F4}u(윗면대비 {(hb.max.y - apY) / StsConfig.ModelScale * 1000f:+0;-0}mm), GrabPoint y {gp.y:F4}u");
        }

        // 트위스트락 콘들의 월드 X·Z 벌어짐 — SpreaderGrabber 와 같은 이름 규약
        static void Span(StsCrane crane, out float spanX, out float spanZ)
        {
            List<Vector3> pts = crane.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Twistlock_Cone") || t.name.StartsWith("Spreader_Twistlock_"))   // Numbered 접미사 포함
                .Select(t => t.position).ToList();
            spanX = pts.Count > 1 ? pts.Max(p => p.x) - pts.Min(p => p.x) : 0f;
            spanZ = pts.Count > 1 ? pts.Max(p => p.z) - pts.Min(p => p.z) : 0f;
        }

        static void Setup()
        {
            foreach (var b in Object.FindObjectsByType<PlcBridge>(FindObjectsSortMode.None)) b.enabled = false;   // PLC 재생이 축을 잡지 않게
            var yardRx = new Regex(@"^Cont(20|40)_\d+$");
            var all = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Select(l => l.transform)
                .Where(t => t.name.StartsWith("ShipContainer") || yardRx.IsMatch(t.name)).ToList();
            var bounds = new Dictionary<Transform, Bounds>();
            foreach (var t in all) if (CraneDemoRunner.TryBounds(t, out var bb)) bounds[t] = bb;

            // 원점 ↔ 바운즈 중심(로컬) — 원점 규약이 다른 컨테이너가 있는지
            foreach (var t in new[] { all.FirstOrDefault(x => x.name.StartsWith("ShipContainer")), all.FirstOrDefault(x => x.name == "Cont40_00"), all.FirstOrDefault(x => x.name == "Cont20_00") })
                if (t != null && bounds.TryGetValue(t, out var ob))
                    Debug.Log($"[StsGrabProbe] 원점↔바운즈 중심 {t.name}: 로컬 {t.InverseTransformPoint(ob.center):F4} · 크기 {ob.size:F4} · 회전 {t.rotation.eulerAngles} · 스케일 {t.lossyScale}");

            // 야드 컨테이너는 콜라이더·강체가 없어 못 집는다 — 시연 감독과 같은 구성으로 붙인다
            foreach (var t in all.Where(x => yardRx.IsMatch(x.name)))
            {
                var ob = bounds[t];
                if (t.GetComponentInChildren<Collider>() == null)
                {
                    var s = t.lossyScale; var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = t.InverseTransformPoint(ob.center);
                    bc.size = new Vector3(ob.size.x / s.x, ob.size.y / s.y, ob.size.z / s.z);
                }
                var rb = t.GetComponent<Rigidbody>();   // ?? 는 Unity 가짜 null 을 못 거른다
                if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;
            }

            foreach (var crane in Object.FindObjectsByType<StsCrane>(FindObjectsSortMode.None))
            {
                var g = crane.GetComponent<SpreaderGrabber>();
                if (g == null || crane.Attach == null || crane.Spreader == null) continue;
                if (crane.Trolley is TrolleyMover tm) tm.StopOnObstacle = false;
                if (crane.Gantry is GantryMover gm) gm.StopOnObstacle = false;
                bool rtg = crane.GetComponent<RtgBogieSteering>() != null;
                Vector3 me = crane.Gantry is Component gc ? gc.transform.position : crane.transform.position;
                var picks = all.Where(t => bounds.ContainsKey(t) && (rtg ? yardRx.IsMatch(t.name) : t.name.StartsWith("ShipContainer")))
                    .Where(t => Uncovered(t, all, bounds) && Reachable(crane, g, bounds[t]))
                    .OrderBy(t => (bounds[t].center - me).sqrMagnitude).ToList();
                var chosen = rtg
                    ? new[] { picks.FirstOrDefault(t => t.name.StartsWith("Cont40")), picks.FirstOrDefault(t => t.name.StartsWith("Cont20")) }.Where(t => t != null)
                    : picks.Where((t, i) => i % 3 == 0).Take(4);
                foreach (var t in chosen)
                {
                    cases.Add(new Case { crane = crane, grabber = g, box = t, targetOffM = HoverM, expectLock = false });                       // 공중 — 안 잠겨야
                    cases.Add(new Case { crane = crane, grabber = g, box = t, targetOffM = -g.InsertDepthMeters, expectLock = true });          // 삽입 자세 — 잠겨야
                    cases.Add(new Case { crane = crane, grabber = g, box = t, targetOffM = -OverdriveM, expectLock = true });                   // 과하강 — 클램프가 삽입깊이에서 세워야
                }
                float protCone = ProtrusionM(crane, out string partCone);                       // 콘만 제외
                float protAsm  = ProtrusionM(crane, out string partAsm, wholeAssembly: true);   // 트위스트락 부재 전부 제외
                Debug.Log($"[StsGrabProbe] {crane.name}: 후보 {picks.Count}개 중 {chosen.Count()}개 검사 · 콘 {Cones(crane).Count}개 · " +
                          $"돌출(콘만 제외) {protCone * 1000f:F0}mm ← {partCone} · " +
                          $"돌출(트위스트락 전부 제외) {protAsm * 1000f:F0}mm ← {partAsm} · " +
                          $"현재 삽입 설정 {g.InsertDepthMeters * 1000f:F0}mm");
            }
        }

        static bool Uncovered(Transform t, List<Transform> all, Dictionary<Transform, Bounds> bounds)
        {
            var b = bounds[t];
            float mx = b.extents.x * 0.5f, mz = b.extents.z * 0.5f;
            return !all.Any(o => o != t && bounds.ContainsKey(o) && bounds[o].min.y > b.center.y
                && bounds[o].min.x < b.max.x - mx && bounds[o].max.x > b.min.x + mx
                && bounds[o].min.z < b.max.z - mz && bounds[o].max.z > b.min.z + mz);
        }

        static bool Reachable(StsCrane crane, SpreaderGrabber g, Bounds b)
        {
            Vector3 d = Top(b) - g.GrabPoint();
            return In(crane.Gantry, new Vector3(d.x, 0f, d.z)) && In(crane.Trolley, new Vector3(d.x, 0f, d.z)) && In(crane.Spreader, new Vector3(0f, d.y, 0f));
        }

        static bool In(IAxisMover a, Vector3 d)
        {
            if (a == null) return true;
            Vector3 w = a.WorldAxis;
            float v = a.Current + Vector3.Dot(d, w) / Mathf.Max(w.sqrMagnitude, 1e-12f), eps = (a.Max - a.Min) * 1e-3f;
            return v >= a.Min - eps && v <= a.Max + eps;
        }

        static void Finish()
        {
            bool pass = measured > 0 && fails == 0;
            Debug.Log($"[StsGrabProbe] {(pass ? "PASS" : "FAIL")} — 검사 {cases.Count}건, 측정 {measured}, 실패 {fails}, 건너뜀 {skipped}");
            EditorApplication.update -= Tick;
            EditorPrefs.SetBool(PortDemoDirector.EditorPrefKey, SessionState.GetBool(PrevKey, false));
            SessionState.EraseBool(Key);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
