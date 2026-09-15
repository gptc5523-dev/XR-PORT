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

        struct Case { public StsCrane crane; public SpreaderGrabber grabber; public Transform box; }
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
                case 1:   // 트위스트락 중심을 윗면 중심에
                    CraneDemoRunner.TryBounds(c.box, out before);
                    rotBefore = c.box.rotation; posBefore = c.box.position; parentBefore = c.box.parent;
                    var rb = c.box.GetComponent<Rigidbody>(); kinBefore = rb != null && rb.isKinematic;
                    Vector3 d = Top(before) - c.grabber.GrabPoint();
                    MoveBy(c.crane.Gantry, new Vector3(d.x, 0f, d.z));
                    MoveBy(c.crane.Trolley, new Vector3(d.x, 0f, d.z));
                    MoveBy(c.crane.Spreader, new Vector3(0f, d.y, 0f));
                    phase = 2; Wait(0.3f); return;
                case 2:   // 닿았으면 잡기
                    float miss = (Top(before) - c.grabber.GrabPoint()).magnitude;
                    if (miss > 0.005f)
                    {
                        Debug.Log($"[StsGrabProbe] 건너뜀 {c.crane.name} {c.box.name} — 트위스트락이 윗면 중심에 못 감(남은 {miss:F4}u)");
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
            bool ok = same && dxz <= TolXZ && rot < 1f && longZ == longZBefore
                   && (stage != "잡음" || (jumpXZ <= TolXZ && Mathf.Abs(dTop) <= TolY));
            measured++;
            if (!ok) fails++;
            Debug.Log($"[StsGrabProbe] {(ok ? "OK " : "BAD")} {stage} {c.crane.name} {c.box.name} — 잡힘 {same}" +
                      $"{(same ? "" : $"(실제 {(attach != null && attach.AttachedContainer != null ? attach.AttachedContainer.name : "없음")})")}, " +
                      $"트위스트락↔중심 {dxz:F4}u, 튄 거리 수평 {jumpXZ:F4}u·윗면 {dTop:+0.0000;-0.0000}u, 회전 {rot:F1}°, " +
                      $"컨테이너 긴축 {(longZ ? "Z" : "X")}(전 {(longZBefore ? "Z" : "X")}) {Mathf.Max(hb.size.x, hb.size.z):F3}u, " +
                      $"스프레더 긴축 {(spreaderLongZ ? "Z" : "X")} 콘 간격 {Mathf.Max(spanX, spanZ):F3}u");
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
                foreach (var t in chosen) cases.Add(new Case { crane = crane, grabber = g, box = t });
                Debug.Log($"[StsGrabProbe] {crane.name}: 후보 {picks.Count}개 중 {chosen.Count()}개 검사");
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
