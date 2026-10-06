#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AIXRCrane.Crane.Sts.Plc;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>배치 검사 — 수동 집기(SpreaderGrabber.Grab)가 컨테이너를 제자리에서 그대로 매다는지 확인.
    /// -executeMethod StsGrabProbe.Run 으로 실행, 위치·회전 오차가 허용치 안이면 종료 코드 0.</summary>
    [InitializeOnLoad]
    public static class StsGrabProbe
    {
        const string Key = "StsGrabProbe", PrevKey = "StsGrabProbe.Prev", ScenePath = StsPartNames.PortScenePath;
        const float TolXZ = 0.015f, TolY = 0.003f, LiftU = 0.05f;
        // 케이스: 호버(안 잠겨야) · 안착(InsertDepthMeters ± 허용오차) · 과하강(클램프가 삽입깊이에서 세워야).
        //   허용오차 = min(InsertBandAbsM, 깊이 × InsertBandFrac).
        const float HoverM = 0.2f, OverdriveM = 0.2f, InsertBandAbsM = 0.005f, InsertBandFrac = 0.25f;

        // 야드 칸 정렬 검사 — 칸의 40% 옆·7° 틀어 놓고 되돌아오는지 잰다.
        const float OffCellFrac = 0.4f, OffYawDeg = 7f, SnapTolM = 0.005f;

        /// <summary>칸에서 일부러 벗어나게 할 월드 변위(모델 단위) — 행·베이 피치의 OffCellFrac.</summary>
        static Vector3 OffCellU() =>
            new Vector3(PortConfig.RowPitchM * OffCellFrac, 0f, PortConfig.BayPitchM * OffCellFrac) * StsConfig.ModelScale;

        // 놓기 직전 상태 — 야드 블록 안이면 칸 정렬, 밖이면 그대로가 기대치.
        static bool snapExpected;
        static Vector3 snapCellWanted, snapCenterBefore;

        /// <summary>놓기 직전 호출 — 이 자리가 야드 칸인지(스냅 기대 여부) 판정.</summary>
        static void MarkSnapExpectation(Case c)
        {
            snapExpected = false; snapCellWanted = snapCenterBefore = Vector3.zero;
            if (!SceneUtil.TryBounds(c.box, out var b)) return;
            snapCenterBefore = b.center;
            snapExpected = YardGrid.TrySnapXZ(b.center, Mathf.Max(b.size.x, b.size.z), out snapCellWanted);
        }

        // 놓은 결과 판정(수식은 런타임 YardGrid 그대로). 야드 안: 칸 중심 ±SnapTolM + 격자 요각, 밖: 그대로.
        static void MeasureSnap(Case c)
        {
            measured++;
            if (!SceneUtil.TryBounds(c.box, out var b)) { fails++; Debug.Log($"[StsGrabProbe] BAD 칸정렬 {c.box.name} — 바운즈 없음"); return; }

            float yawOff = Mathf.Abs(Mathf.DeltaAngle(c.box.eulerAngles.y, Mathf.Round(c.box.eulerAngles.y / 90f) * 90f));
            bool ok; string detail;
            if (snapExpected)
            {
                float dM = new Vector2(b.center.x - snapCellWanted.x, b.center.z - snapCellWanted.z).magnitude * StsConfig.InvModelScale;
                ok = dM <= SnapTolM && yawOff <= 1f;
                detail = $"야드 칸 안 → 칸 중심 이탈 {dM * 1000f:F0}mm(허용 {SnapTolM * 1000f:F0}), yaw 잔차 {yawOff:F1}°";
            }
            else
            {
                // 야드 밖 — 놓은 자리에서 움직이면 버그.
                float movedM = (b.center - snapCenterBefore).magnitude * StsConfig.InvModelScale;
                ok = movedM <= SnapTolM;
                detail = $"야드 밖(배·에이프런) → 놓은 자리 유지 확인, 이동 {movedM * 1000f:F0}mm(허용 {SnapTolM * 1000f:F0})";
            }
            if (!ok) fails++;
            Debug.Log($"[StsGrabProbe] {(ok ? "OK " : "BAD")} 칸정렬 {c.crane.name} {c.box.name} — {detail}");
        }

        // targetOffM = 컨테이너 윗면 대비 콘 바닥 목표(실척 m, + 위 / − 아래). 최종 높이는 클램프가 정할 수 있다.
        struct Case { public StsCrane crane; public SpreaderGrabber grabber; public Transform box; public float targetOffM; public bool expectLock; }
        static readonly List<Case> cases = new List<Case>();
        static int idx, phase, fails, measured, skipped;
        static float waitUntil;
        static Bounds before; static Quaternion rotBefore; static Vector3 posBefore; static Transform parentBefore; static bool kinBefore;
        static Bounds beforeAll;   // 전 LOD 유니온 기준선(활성 전용 바운즈와 비교)

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
                case 1:   // 수평은 콘 중심을 윗면 중심에, 높이는 콘 바닥 실측 기준
                    SceneUtil.TryBounds(c.box, out before);
                    SceneUtil.TryBounds(c.box, out beforeAll, true);   // LOD 무관 기준선
                    rotBefore = c.box.rotation; posBefore = c.box.position; parentBefore = c.box.parent;
                    var rb = c.box.GetComponent<Rigidbody>(); kinBefore = rb != null && rb.isKinematic;
                    // 재는 동안 kinematic 고정(동적 강체가 내려앉아 측정치가 섞임). phase 5 에서 원복.
                    if (rb != null) rb.isKinematic = true;
                    Vector3 gp1 = c.grabber.GrabPoint();
                    float targetConeY = before.max.y + c.targetOffM * StsConfig.ModelScale;
                    Vector3 d = new Vector3(Top(before).x - gp1.x,
                                            targetConeY - BottomY(c.crane, null, cones: true),
                                            Top(before).z - gp1.z);
                    MoveBy(c.crane.Gantry, new Vector3(d.x, 0f, d.z));
                    MoveBy(c.crane.Trolley, new Vector3(d.x, 0f, d.z));
                    MoveBy(c.crane.Spreader, new Vector3(0f, d.y, 0f));
                    phase = 2; Wait(0.4f); return;   // 클램프 정착 대기
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
                    if (c.expectLock)   // 잠긴 케이스만 — 안 잡힌 케이스는 놓을 게 없다
                    {
                        MoveBy(c.crane.Trolley, OffCellU());
                        MoveBy(c.crane.Gantry, OffCellU());
                        c.box.rotation = Quaternion.Euler(0f, OffYawDeg, 0f) * c.box.rotation;
                        phase = 5; Wait(0.3f); return;
                    }
                    goto case 5;
                case 5:
                    bool wasLocked = c.expectLock && c.crane.Attach != null && c.crane.Attach.HasContainer;
                    if (wasLocked) MarkSnapExpectation(c);
                    c.grabber.Release();
                    if (wasLocked) MeasureSnap(c);
                    c.box.SetParent(parentBefore, true);
                    c.box.SetPositionAndRotation(posBefore, rotBefore);
                    var rb2 = c.box.GetComponent<Rigidbody>();
                    if (rb2 != null) { rb2.isKinematic = kinBefore; if (!kinBefore) { rb2.linearVelocity = Vector3.zero; rb2.angularVelocity = Vector3.zero; } }
                    idx++; phase = 1; Wait(0.3f); return;
            }
        }

        static Vector3 Top(Bounds b) => new Vector3(b.center.x, b.max.y, b.center.z);

        // SpreaderGrabber.Awake 와 같은 규약으로 모은 콘 — 0개면 그랩버도 콘을 못 찾는다.
        static List<Transform> Cones(StsCrane crane) => crane.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith(StsPartNames.TwistlockCone) || t.name.StartsWith(StsPartNames.SpreaderTwistlockPrefix))   // Span() 과 같은 규약(Numbered 접미사 포함)
            .ToList();

        // 렌더러 최저점(held 제외). cones=true 면 콘만, false 면 스프레더 전체.
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

        // 돌출 길이(실척 m) = 구조물 밑면이 윗면에 닿기까지의 삽입 상한(STS 24mm · RTG 56mm, 바닥은 빔이 정함).
        //   wholeAssembly=false: 콘만 제외, true: Twistlock 이름 부재 전부 제외.
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

        // 콘 반경 프로파일(h 1mm 버킷별 최대 r)에서 숄더 상단을 락 높이로 실측 — FBX RTG 용.
        //   ★ 절차 STS 정답 76.8mm 가 안 나오면 신뢰 금지. shapeOk=false 면 노즈·숄더·넥 형상 아님.
        static bool TryLockHeightM(StsCrane crane, out float lockM, out string profile, out bool shapeOk)
        {
            lockM = 0f; profile = "없음"; shapeOk = false;
            var cones = Cones(crane);
            if (cones.Count == 0) return false;

            var pts = new List<Vector3>();
            foreach (var mf in cones[0].GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                foreach (var v in m.vertices) pts.Add(mf.transform.TransformPoint(v));
            }
            if (pts.Count == 0) return false;

            float inv = StsConfig.InvModelScale;
            float minY = float.MaxValue, cx = 0f, cz = 0f;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); cx += p.x; cz += p.z; }
            cx /= pts.Count; cz /= pts.Count;

            var maxR = new Dictionary<int, float>();
            foreach (var p in pts)
            {
                int mm = Mathf.RoundToInt((p.y - minY) * inv * 1000f);
                float r = new Vector2(p.x - cx, p.z - cz).magnitude * inv * 1000f;
                if (!maxR.TryGetValue(mm, out float cur) || r > cur) maxR[mm] = r;
            }

            float rMax = 0f;
            foreach (var kv in maxR) rMax = Mathf.Max(rMax, kv.Value);
            int shoulderTop = 0;
            foreach (var kv in maxR) if (kv.Value >= rMax * 0.98f) shoulderTop = Mathf.Max(shoulderTop, kv.Key);
            lockM = shoulderTop / 1000f;

            var keys = new List<int>(maxR.Keys);
            keys.Sort();

            // 정점이 있는 버킷만 찍는다 — 저폴리 메시는 고정 간격 샘플이면 버킷이 빈다.
            const int MaxPrint = 48;
            int step = Mathf.Max(1, keys.Count / MaxPrint);
            var sb2 = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i += step) sb2.Append($"{keys[i]}:{maxR[keys[i]]:F1} ");

            // 콘 끝 반경이 최대면 노즈 형상 아님. Dictionary 는 순서가 없어 정렬된 keys 로 훑는다.
            int rMaxAt = keys[0];
            foreach (int k in keys) if (maxR[k] >= rMax * 0.999f) { rMaxAt = k; break; }
            bool tipIsWidest = rMaxAt <= keys[0] + 1;
            shapeOk = !tipIsWidest;
            profile = (tipIsWidest ? $"★형상 이상(콘 끝에서 반경 최대 {rMax:F1}mm — 뾰족한 노즈가 아님 ⇒ 이 락 높이는 신뢰 불가) " : "")
                    + $"[최대반경 {rMax:F1}mm @ h={rMaxAt}mm · 끝 버킷 반경 {maxR[keys[0]]:F1}mm · 버킷 {keys.Count}개] "
                    + sb2.ToString().TrimEnd();
            return true;   // 계측은 성공 — 신뢰 여부는 shapeOk
        }

        static float ConeHeightM(StsCrane crane)
        {
            var cones = Cones(crane);
            if (cones.Count == 0) return 0f;
            return SceneUtil.TryBounds(cones[0], out var u) ? u.size.y / StsConfig.ModelScale : 0f;
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
            SceneUtil.TryBounds(c.box, out var hb);
            Vector3 gp = c.grabber.GrabPoint();
            float dxz = new Vector2(hb.center.x - gp.x, hb.center.z - gp.z).magnitude;
            float jumpXZ = new Vector2(hb.center.x - before.center.x, hb.center.z - before.center.z).magnitude;
            float dTop = hb.max.y - before.max.y - (stage == "잡음" ? 0f : LiftU);
            float rot = Quaternion.Angle(rotBefore, c.box.rotation);
            bool longZ = hb.size.z > hb.size.x, longZBefore = before.size.z > before.size.x;
            Span(c.crane, out float spanX, out float spanZ);
            bool spreaderLongZ = spanZ > spanX;
            // 삽입 = 윗면 − 콘 바닥(실척 m, 양수 = 박힘). 잠금 케이스는 밴드 안, 호버는 미잠금이 정상.
            float insertM = (hb.max.y - BottomY(c.crane, c.box, cones: true)) / StsConfig.ModelScale;
            float want = c.grabber.InsertDepthMeters;
            float tol = Mathf.Min(InsertBandAbsM, InsertBandFrac * want);
            bool band = Mathf.Abs(insertM - want) <= tol;
            // 세로는 '내려감'(받침 파고듦)만 실패 — 클램프가 들어 올리는 건 정상.
            bool vOk = stage != "잡음" || dTop >= -TolY;
            bool ok = c.expectLock
                ? same && band && dxz <= TolXZ && rot < 1f && longZ == longZBefore
                  && (stage != "잡음" || jumpXZ <= TolXZ) && vOk
                : !same;
            measured++;
            if (!ok) fails++;
            Debug.Log($"[StsGrabProbe] {(ok ? "OK " : "BAD")} {stage} {c.crane.name} {c.box.name} — 잡힘 {same}" +
                      $"{(same ? "" : $"(실제 {(attach != null && attach.AttachedContainer != null ? attach.AttachedContainer.name : "없음")})")}, " +
                      $"트위스트락↔중심 {dxz:F4}u, 튄 거리 수평 {jumpXZ:F4}u·윗면 {dTop:+0.0000;-0.0000}u, 회전 {rot:F1}°, " +
                      $"컨테이너 긴축 {(longZ ? "Z" : "X")}(전 {(longZBefore ? "Z" : "X")}) {Mathf.Max(hb.size.x, hb.size.z):F3}u, " +
                      $"스프레더 긴축 {(spreaderLongZ ? "Z" : "X")} 콘 간격 {Mathf.Max(spanX, spanZ):F3}u, " +
                      // 원인 불명 잔여 — 활성 LOD 바운즈 전후를 그대로 찍는다.
                      $"[활성LOD] 전 size({before.size.x:F4},{before.size.y:F4},{before.size.z:F4}) max.y {before.max.y:F4} " +
                      $"→ 후 size({hb.size.x:F4},{hb.size.y:F4},{hb.size.z:F4}) max.y {hb.max.y:F4} · " +
                      $"활성렌더러 {c.box.GetComponentsInChildren<Renderer>().Length}개 / 전체 {c.box.GetComponentsInChildren<Renderer>(true).Length}개, " +
                      // 전 LOD 유니온 — 이쪽이 같고 활성만 변하면 LOD 전환이 원인.
                      $"[전LOD] 전 size({beforeAll.size.x:F4},{beforeAll.size.y:F4},{beforeAll.size.z:F4}) max.y {beforeAll.max.y:F4} " +
                      $"→ 후 {(SceneUtil.TryBounds(c.box, out Bounds nowAll, true) ? $"size({nowAll.size.x:F4},{nowAll.size.y:F4},{nowAll.size.z:F4}) max.y {nowAll.max.y:F4}" : "측정 실패")}");

            // 삽입 실측(양수=박힘) — 콘 기준·스프레더 기준을 같이 찍는다.
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
                .Where(t => t.name.StartsWith(StsPartNames.TwistlockCone) || t.name.StartsWith(StsPartNames.SpreaderTwistlockPrefix))   // Numbered 접미사 포함
                .Select(t => t.position).ToList();
            spanX = pts.Count > 1 ? pts.Max(p => p.x) - pts.Min(p => p.x) : 0f;
            spanZ = pts.Count > 1 ? pts.Max(p => p.z) - pts.Min(p => p.z) : 0f;
        }

        static void Setup()
        {
            foreach (var b in Object.FindObjectsByType<PlcBridge>()) b.enabled = false;   // PLC 재생이 축을 잡지 않게
            var all = Object.FindObjectsByType<LODGroup>().Select(l => l.transform)
                .Where(t => t.name.StartsWith(StsPartNames.ShipContainer) || StsPartNames.IsYardContainerName(t.name)).ToList();
            var bounds = new Dictionary<Transform, Bounds>();
            foreach (var t in all) if (SceneUtil.TryBounds(t, out var bb)) bounds[t] = bb;

            // 원점 ↔ 바운즈 중심(로컬) — 원점 규약이 다른 컨테이너가 있는지
            foreach (var t in new[] { all.FirstOrDefault(x => x.name.StartsWith(StsPartNames.ShipContainer)), all.FirstOrDefault(x => x.name == StsPartNames.Yard40Prefix + "00"), all.FirstOrDefault(x => x.name == StsPartNames.Yard20Prefix + "00") })
                if (t != null && bounds.TryGetValue(t, out var ob))
                    Debug.Log($"[StsGrabProbe] 원점↔바운즈 중심 {t.name}: 로컬 {t.InverseTransformPoint(ob.center):F4} · 크기 {ob.size:F4} · 회전 {t.rotation.eulerAngles} · 스케일 {t.lossyScale}");

            // 야드 컨테이너는 콜라이더·강체가 없어 시연 감독과 같은 구성으로 붙인다
            foreach (var t in all.Where(x => StsPartNames.IsYardContainerName(x.name)))
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

            foreach (var crane in Object.FindObjectsByType<StsCrane>())
            {
                var g = crane.GetComponent<SpreaderGrabber>();
                if (g == null || crane.Attach == null || crane.Spreader == null) continue;
                if (crane.Trolley is TrolleyMover tm) tm.StopOnObstacle = false;
                if (crane.Gantry is GantryMover gm) gm.StopOnObstacle = false;
                bool rtg = crane.GetComponent<RtgBogieSteering>() != null;
                Vector3 me = crane.Gantry is Component gc ? gc.transform.position : crane.transform.position;
                var picks = all.Where(t => bounds.ContainsKey(t) && (rtg ? StsPartNames.IsYardContainerName(t.name) : t.name.StartsWith(StsPartNames.ShipContainer)))
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
                float coneH = ConeHeightM(crane);                                               // 콘 오브젝트 자체의 전체 길이
                Debug.Log($"[StsGrabProbe] {crane.name}: 후보 {picks.Count}개 중 {chosen.Count()}개 검사 · 콘 {Cones(crane).Count}개 · " +
                          $"돌출(콘만 제외) {protCone * 1000f:F0}mm ← {partCone} · " +
                          $"돌출(트위스트락 전부 제외) {protAsm * 1000f:F0}mm ← {partAsm} · " +
                          $"현재 삽입 설정 {g.InsertDepthMeters * 1000f:F0}mm");
                // 콘 길이 대비 노출 비가 작으면 락(숄더)이 구멍에 안 들어간다 — FBX RTG 판정 대용.
                Debug.Log($"[StsGrabProbe] {crane.name} 콘 기하: 전체 길이 {coneH * 1000f:F0}mm · 본체 밑면 아래 노출 {protCone * 1000f:F0}mm · " +
                          $"노출/전체 {(coneH > 1e-6f ? protCone / coneH * 100f : 0f):F0}% · " +
                          $"본체 안에 숨은 길이 {(coneH - protCone) * 1000f:F0}mm");
                // 락 높이 실측(반경 프로파일). STS 정답 76.8mm 로 계측 자체도 검증.
                if (TryLockHeightM(crane, out float lockM, out string prof, out bool shapeOk))
                {
                    float shortM = lockM - protCone;   // 양수 = 락이 그만큼 본체 안에 남아 구멍에 안 들어간다
                    string verdict = !shapeOk
                        ? "락 높이 판정 보류 — 형상이 노즈·숄더·넥 이 아니다(아래 ★ 참고)"
                        : $"락 높이 {lockM * 1000f:F1}mm · 노출 {protCone * 1000f:F0}mm · " +
                          (shortM > 0.0005f ? $"부족 {shortM * 1000f:F1}mm(그만큼 더 노출해야 락이 구멍 안)" : "락 전체가 구멍 안(부족 없음)");
                    Debug.Log($"[StsGrabProbe] {crane.name} 락 실측: {verdict} · 반경 프로파일(h:최대r, mm) {prof}");
                }
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
