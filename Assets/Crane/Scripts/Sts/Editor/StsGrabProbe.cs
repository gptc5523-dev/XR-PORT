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

        // ⑨ 야드 칸 정렬 검사 — 제자리에서 놓으면 이미 칸 위라 '스냅이 돌았는지'만 보이고 '틀어진 걸 바로잡는지'는 안 보인다.
        //   그래서 일부러 칸의 40% 만큼 옆으로 옮기고 7° 틀어서 놓고, 칸 중심·격자 축으로 되돌아오는지 잰다.
        const float OffCellFrac = 0.4f, OffYawDeg = 7f, SnapTolM = 0.005f;

        /// <summary>칸에서 일부러 벗어나게 할 월드 변위(모델 단위) — 행·베이 피치의 OffCellFrac.</summary>
        static Vector3 OffCellU() =>
            new Vector3(PortConfig.RowPitchM * OffCellFrac, 0f, PortConfig.BayPitchM * OffCellFrac) * StsConfig.ModelScale;

        // 놓기 직전 상태 — 기대치가 '칸 정렬'인지 '건드리지 않음'인지는 놓는 자리가 야드 블록 안인지로 갈린다.
        static bool snapExpected;
        static Vector3 snapCellWanted, snapCenterBefore;

        /// <summary>놓기 직전에 호출 — 이 자리가 야드 칸인지(=스냅이 일어나야 하는지) 미리 판정해 둔다.</summary>
        static void MarkSnapExpectation(Case c)
        {
            snapExpected = false; snapCellWanted = snapCenterBefore = Vector3.zero;
            if (!CraneDemoRunner.TryBounds(c.box, out var b)) return;
            snapCenterBefore = b.center;
            snapExpected = YardGrid.TrySnapXZ(b.center, Mathf.Max(b.size.x, b.size.z), out snapCellWanted);
        }

        // 놓은 결과 판정. 수식은 런타임 YardGrid 를 그대로 쓴다 — 검사가 제 식을 따로 두면 서로를 검증하지 못한다.
        //   ★ 기대치가 둘로 갈린다(2026-09-16 첫 실행에서 내 판정이 틀렸던 부분):
        //     · 야드 블록 안에 놓았으면 → 칸 중심 ±SnapTolM + 격자 요각. 이게 오너가 요구한 '라인 지키기'다.
        //     · 야드 블록 밖(STS 는 배·에이프런에서 작업한다)이면 → 아무것도 안 건드리는 게 정상.
        //       첫 판정은 여기서도 칸 정렬을 요구해 STS 6건을 BAD 로 찍었다 — 코드가 아니라 검사가 틀린 것이었다.
        static void MeasureSnap(Case c)
        {
            measured++;
            if (!CraneDemoRunner.TryBounds(c.box, out var b)) { fails++; Debug.Log($"[StsGrabProbe] BAD 칸정렬 {c.box.name} — 바운즈 없음"); return; }

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
                // 야드 밖 — 놓은 자리에서 움직이지 않아야 한다(격자와 무관한 자리를 임의로 옮기면 그게 버그다).
                float movedM = (b.center - snapCenterBefore).magnitude * StsConfig.InvModelScale;
                ok = movedM <= SnapTolM;
                detail = $"야드 밖(배·에이프런) → 놓은 자리 유지 확인, 이동 {movedM * 1000f:F0}mm(허용 {SnapTolM * 1000f:F0})";
            }
            if (!ok) fails++;
            Debug.Log($"[StsGrabProbe] {(ok ? "OK " : "BAD")} 칸정렬 {c.crane.name} {c.box.name} — {detail}");
        }

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
                    // ★ 재는 동안 kinematic 으로 고정한다 — 배 컨테이너는 동적 강체라 측정 창 사이에 중력으로 내려앉고,
                    //   그 낙하가 '집는 순간 튄 거리(윗면)'에 섞여 STS 1건이 허용 0.003u 를 0.0007u 넘겼다(2026-09-16).
                    //   xr-port-42 가 FloorClipProbe 에서 찾은 것과 같은 원인(b56eead) — 그쪽 해법을 그대로 따른다.
                    //   phase 5 에서 kinBefore 로 원복하므로 씬 상태는 유지된다.
                    if (rb != null) rb.isKinematic = true;
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
                    if (wasLocked) MarkSnapExpectation(c);   // 이 자리가 야드 칸인지 먼저 판정(기대치가 갈린다)
                    c.grabber.Release();
                    if (wasLocked) MeasureSnap(c);   // 야드면 칸 정렬, 야드 밖이면 놓은 자리 유지
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

        // 콘 오브젝트 하나의 전체 길이(실척 m) — 렌더러 합 바운즈의 높이. xr-port-ae 요청 2026-09-16:
        //   FBX RTG 는 락(숄더) 높이를 코드로 못 읽어서, '전체 길이 대비 본체 밑면 아래 노출 비'를 그 대용으로 쓴다.
        //   노출 비가 작으면 락이 콘 위쪽에 남아 STS 와 같은 증상(락이 구멍에 안 들어감)이 된다.
        // 콘 반경 프로파일로 '락(노즈+숄더) 높이'를 실측한다 — xr-port-ae 제안 2026-09-16. 비례 추정을 없애려는 계측이다.
        //   ① 콘 메시 정점을 월드로 변환 ② 콘 중심축(정점 XZ 평균) 기준 수평반경 r 과 콘끝 기준 높이 h
        //   ③ h 를 1mm(실척) 버킷으로 묶어 버킷별 최대 r → 반경 프로파일
        //   ④ 아래→위로 노즈(r 증가) → 숄더(r 최대에서 평탄) → 넥(r 감소). 락 높이 = 숄더 평탄 구간의 상단 h.
        //   ★ 계측 자체의 검증: 절차 생성 STS 는 정답을 안다(노즈 52.8 + 숄더 24 = 락 76.8mm, 전체 144mm).
        //     STS 에서 76.8mm 근처가 안 나오면 이 계측을 신뢰하지 말 것 — FBX 쪽 축·스케일이 달라 조용히 틀릴 수 있다.
        //   반환값은 '정점을 재긴 했는가'만 뜻한다. 형상이 노즈·숄더·넥 이 아니면 shapeOk=false 로 알린다 —
        //   두 뜻을 한 반환값에 섞으면 형상 이상일 때 진단 로그 자체가 안 찍혀서, 넣은 이유가 사라진다(내가 방금 그렇게 썼다).
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

            // ★ 고정 간격(예: 8mm)으로 샘플하면 안 된다 — 저폴리 메시는 링 높이에만 정점이 있어 대부분 버킷이 비고,
            //   찍히는 값이 '8의 배수인 높이'라는 우연에 좌우돼 형상이 안 보인다(xr-port-ae 가 자기 1차 계측에서 발견).
            //   정점이 있는 버킷만 전부 찍는다. 로그 폭주를 막으려고 개수만 제한한다.
            const int MaxPrint = 48;
            int step = Mathf.Max(1, keys.Count / MaxPrint);
            var sb2 = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i += step) sb2.Append($"{keys[i]}:{maxR[keys[i]]:F1} ");

            // 계측이 조용히 틀리는 경우를 드러낸다 — 뾰족한 노즈면 콘 끝 반경이 최소여야 한다.
            //   끝에서 반경이 최대면 노즈·숄더·넥 형상이 아니라는 뜻이고, 그때 '숄더 상단'으로 뽑은 락 높이는 의미가 없다.
            // ★ 정렬된 keys 를 훑어야 한다 — Dictionary 순회는 순서가 없어서, RTG(0:59.9 · 45:59.9 동률)에서 45 를 먼저 집고
            //   "끝이 최대"를 놓쳤다(2026-09-16 내 실행에서 가드가 안 걸렸다). 최저 버킷부터 봐야 '끝이 최대인가'를 옳게 판정한다.
            int rMaxAt = keys[0];
            foreach (int k in keys) if (maxR[k] >= rMax * 0.999f) { rMaxAt = k; break; }
            bool tipIsWidest = rMaxAt <= keys[0] + 1;
            shapeOk = !tipIsWidest;
            profile = (tipIsWidest ? $"★형상 이상(콘 끝에서 반경 최대 {rMax:F1}mm — 뾰족한 노즈가 아님 ⇒ 이 락 높이는 신뢰 불가) " : "")
                    + $"[최대반경 {rMax:F1}mm @ h={rMaxAt}mm · 끝 버킷 반경 {maxR[keys[0]]:F1}mm · 버킷 {keys.Count}개] "
                    + sb2.ToString().TrimEnd();
            return true;   // 정점 계측 자체는 성공 — 신뢰 여부는 shapeOk 로 알린다(프로파일은 항상 찍혀야 한다)
        }

        static float ConeHeightM(StsCrane crane)
        {
            var cones = Cones(crane);
            if (cones.Count == 0) return 0f;
            bool any = false;
            Bounds u = default;
            foreach (var r in cones[0].GetComponentsInChildren<Renderer>())
            {
                if (!any) { u = r.bounds; any = true; }
                else u.Encapsulate(r.bounds);
            }
            return any ? u.size.y / StsConfig.ModelScale : 0f;
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
                float coneH = ConeHeightM(crane);                                               // 콘 오브젝트 자체의 전체 길이
                Debug.Log($"[StsGrabProbe] {crane.name}: 후보 {picks.Count}개 중 {chosen.Count()}개 검사 · 콘 {Cones(crane).Count}개 · " +
                          $"돌출(콘만 제외) {protCone * 1000f:F0}mm ← {partCone} · " +
                          $"돌출(트위스트락 전부 제외) {protAsm * 1000f:F0}mm ← {partAsm} · " +
                          $"현재 삽입 설정 {g.InsertDepthMeters * 1000f:F0}mm");
                // xr-port-ae 요청 2026-09-16 — 오너 "락은 컨테이너 안쪽으로 들어간 다음 락을 걸어야 된다".
                //   콘 전체 길이 대비 노출 비가 작으면, 락(숄더)이 콘 위쪽에 있을 때 STS 와 같은 증상(락이 구멍에 안 들어감)이 된다.
                //   FBX RTG 는 생성기 상수가 없어 숄더 높이를 코드로 못 읽는다 — 이 비가 그 판정의 대용이다.
                Debug.Log($"[StsGrabProbe] {crane.name} 콘 기하: 전체 길이 {coneH * 1000f:F0}mm · 본체 밑면 아래 노출 {protCone * 1000f:F0}mm · " +
                          $"노출/전체 {(coneH > 1e-6f ? protCone / coneH * 100f : 0f):F0}% · " +
                          $"본체 안에 숨은 길이 {(coneH - protCone) * 1000f:F0}mm");
                // 락 높이 실측(반경 프로파일) — 비례 추정 대신 수치. STS 는 정답 76.8mm 라 이 계측의 검증도 같이 된다.
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
