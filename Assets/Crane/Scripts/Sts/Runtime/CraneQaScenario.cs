using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// QA 자동 검증 드라이버 — VR 기기 없이 크레인을 코드로 구동해 문서/QA_테스트시나리오.md의
    /// 그룹 B/C 콘솔 라인(PASS·SIDE·LAND·GANTRY·FLOOR·GRAB·HOIST)을 전부 찍게 한다.
    ///
    ///   · S-PHYS-1/5(TROLLEY/HOIST/GANTRY move·band)는 VRController.FixedUpdate 안에서만 로그되므로
    ///     VRController에 '합성 스틱 입력'을 주입해(QaBeginDrive/QaSticks) production 경로 그대로 태운다.
    ///   · S-PASS-*(통과방지·적층·측면·집기)와 FLOOR/guard는 IAxisMover를 직접 구동(MoveN)하고
    ///     SpreaderGrabber/ContainerPhysics의 FixedUpdate가 독립적으로 반응하게 둔다(실제 로직 그대로).
    ///
    /// [속도] 실척 m/s는 모델축척(1/24)을 곱하면 분당 수 cm라 테스트가 수십 초씩 걸린다. 이 드라이버는
    ///   '콘솔 판정용 하니스'이므로 축을 정규화 속도(범위 비율/초)로 브리스크하게 구동한다 — 클램프/충돌은
    ///   매 FixedUpdate 반응하므로 중간 속도에서도 터널링 없이 검증된다(ContinuousSpeculative).
    ///
    /// [충돌 방지] "시나리오는 1개만" 원칙([[feedback_one_scenario_delete_old]])을 지켜, 구동 중 다른
    ///   CraneScenarioBase를 끄고(enabled=false) 끝나면 정지 상태로 둔다. VRController는 단계별로 직접 토글.
    ///
    /// [가설·미검증] Quest 실기 미검증. QA 세션이 끝나면 AutoRun을 false로 두거나 이 파일을 제거할 것.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/QA Auto Scenario (콘솔 검증)")]
    [DisallowMultipleComponent]
    public sealed class CraneQaScenario : MonoBehaviour
    {
        // QA 세션 동안만 켠다. 끝나면 false(또는 파일 제거) — 매 Play 자동 구동을 멈춘다.
        //   2026-06-17 QA 전그룹 PASS 확인 후 false로 내림. 재검증 필요 시 true로 올리면 Play 시 자동 구동.
        const bool AutoRun = false;

        [SerializeField] float startDelay = 1.0f;   // 다른 자동 스폰/스캔/정착이 끝나길 대기
        [SerializeField] bool debugLog = true;

        // 정규화 구동 속도(범위 비율/초) — 테스트용 브리스크. 클램프/충돌은 FixedUpdate가 잡는다.
        const float NsFast = 0.40f;    // 트롤리·갠트리 정렬
        const float NsHoist = 0.35f;   // 호이스트 일반 이동
        const float NsCreep = 0.15f;   // 정밀 하강/측면 진입
        const float MoveTimeout = 25f; // 풀 트래버스도 넉넉히

        StsCrane crane;
        SpreaderGrabber grabber;
        StsCraneVRController vr;
        QuayContainerScanner scanner;

        // 축↔월드 선형 매핑(캘리브레이션). aX=트롤리(X), aZ=갠트리(Z), aY=호이스트(Y).
        float aX0, aX1, aZ0, aZ1, aY0, aY1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (!AutoRun) return;
            if (FindAnyObjectByType<CraneQaScenario>() != null) return;
            new GameObject("CraneQaScenario (auto)").AddComponent<CraneQaScenario>();
        }

        IEnumerator Start()
        {
            crane = FindAnyObjectByType<StsCrane>();
            if (crane == null) { QaLog.Info("DRIVE", "abort", "reason=no-crane"); yield break; }
            grabber = crane.GetComponent<SpreaderGrabber>();
            vr = crane.GetComponent<StsCraneVRController>();

            // 충돌 방지 — 다른 자동 시나리오 정지(이 드라이버만 크레인을 구동).
            foreach (var s in FindObjectsByType<CraneScenarioBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (s != null && s.enabled) { s.enabled = false; if (debugLog) Debug.Log($"[QA드라이브] 다른 시나리오 정지: {s.GetType().Name}"); }
            if (vr != null) vr.enabled = false;   // 직접 구동 단계 기본값

            yield return null;
            yield return new WaitForSeconds(startDelay);

            if (crane.Trolley == null || crane.Spreader == null || crane.Gantry == null || grabber == null)
            { QaLog.Info("DRIVE", "abort", "reason=crane-axes-missing"); yield break; }
            if (crane.Attach == null || crane.Attach.AttachAnchor == null)
            { QaLog.Info("DRIVE", "abort", "reason=no-attach-anchor"); yield break; }

            if (debugLog) Debug.Log("[QA드라이브] ▶ QA 콘솔 검증 시퀀스 시작 (VR 기기 없이 자동 구동)");

            Calibrate();

            // 컨테이너 인식(재시도 — 스폰/정착 타이밍 흡수)
            var found = new List<QuayContainerScanner.Recognized>();
            yield return ScanContainers(found);
            if (found.Count == 0)
            {
                QaLog.Info("DRIVE", "abort", "reason=no-containers (씬에 컨테이너 필요 — 'Container/부두에 컨테이너 배치')");
                yield break;
            }
            found.Sort((a, b) => b.center.y.CompareTo(a.center.y));   // 윗단부터

            // ───── 단계 1: VRController 합성입력으로 S-PHYS-1/5(축적분·하중밴드) ─────
            yield return PhaseInjectAxes();

            // ───── 단계 2: 빈 스프레더 통과방지(S-PASS-1, PASS/order 수렴) ─────
            var a = found[0];
            yield return PhasePassClamp(a);

            // ───── 단계 3: 집기(GRAB/try, HOIST/floorlimit) + 적재 하중밴드(HOIST/speed light/heavy) ─────
            bool grabbed = false;
            yield return PhaseGrab(a, b => grabbed = b);

            // ───── 단계 4: 적층 안착(LAND/stack) — 두 번째 컨테이너 위로 ─────
            if (grabbed && found.Count >= 2) yield return PhaseStack(found[1]);
            if (crane.Attach.HasContainer) { grabber.Release(); QaLog.Info("DRIVE", "release", "phase=stack-done"); yield return new WaitForSeconds(0.3f); }

            // ───── 단계 5: 측면 타넘기(SIDE/hit, SIDE/anticoll) ─────
            yield return PhaseSideHit(found[0]);

            // ───── 단계 6: 갠트리 장애물 정지(GANTRY/block) ─────
            yield return PhaseGantryBlock(found[0]);

            // ───── 단계 7: 바닥 관통 하드클램프(FLOOR/guard, 결함주입) ─────
            yield return PhaseFloorGuard(found);

            if (debugLog) Debug.Log("[QA드라이브] ■ QA 시퀀스 종료 — Console에서 '=> FAIL' 0건이면 그룹 B/C 합격.");
        }

        // ───────────────────────── 단계 구현 ─────────────────────────

        // 합성 스틱으로 production FixedUpdate 축적분을 태워 TROLLEY/HOIST/GANTRY move·band 로그.
        IEnumerator PhaseInjectAxes()
        {
            if (vr == null) { QaLog.Info("DRIVE", "inject", "skip=no-vrcontroller"); yield break; }
            if (debugLog) Debug.Log("[QA드라이브] 단계1 — VRController 합성입력(S-PHYS-1/5)");
            yield return MoveN(crane.Spreader, 0.7f, NsHoist, "안전고");

            vr.enabled = true; yield return null;
            vr.QaBeginDrive(StsCraneVRController.Mode.Crane); yield return null;
            vr.QaSticks(new Vector2(0.6f, 0f), Vector2.zero);          // 트롤리(오른손 X)
            yield return new WaitForSeconds(0.4f);
            vr.QaSticks(Vector2.zero, new Vector2(0f, 0.5f));          // 호이스트 권상(왼손 Y) — band=empty
            yield return new WaitForSeconds(0.4f);
            vr.QaSticks(Vector2.zero, Vector2.zero); yield return null;
            vr.QaBeginDrive(StsCraneVRController.Mode.Gantry); yield return null;
            vr.QaSticks(Vector2.zero, new Vector2(0.5f, 0f));          // 갠트리(왼손 X)
            yield return new WaitForSeconds(0.4f);
            vr.QaEndDrive(); vr.enabled = false; yield return null;
        }

        // 빈 스프레더를 컨테이너 바로 위로 정렬 후 하강 명령 → SpreaderGrabber가 윗면에서 클램프(PASS/clamp·order).
        IEnumerator PhasePassClamp(QuayContainerScanner.Recognized c)
        {
            if (debugLog) Debug.Log($"[QA드라이브] 단계2 — 통과방지 클램프 (대상 {c.id})");
            yield return MoveN(crane.Spreader, 0.7f, NsHoist, "안전고");
            yield return MoveN(crane.Trolley, Inv(c.center.x, aX0, aX1), NsFast, "횡행→대상");
            yield return MoveN(crane.Gantry, Inv(c.center.z, aZ0, aZ1), NsFast, "주행→대상");
            // 컨테이너 밑면 아래까지 '하강 시도' — 클램프가 윗면에서 막아 corr가 0으로 수렴해야 함.
            float belowN = Inv(c.center.y - c.size.y, aY0, aY1);
            yield return MoveN(crane.Spreader, belowN, NsCreep, "권하(통과시도)");
            yield return new WaitForSeconds(0.6f);   // 클램프 정착(PASS/order dCorr→0) 관찰
        }

        // 컨테이너 윗면에 정밀 착상 후 집기 → GRAB/try, HOIST/floorlimit. 이어 적재 하중밴드 로그.
        IEnumerator PhaseGrab(QuayContainerScanner.Recognized c, System.Action<bool> result)
        {
            if (debugLog) Debug.Log($"[QA드라이브] 단계3 — 집기 (대상 {c.id}, {c.tons:F1}t)");
            yield return MoveN(crane.Trolley, Inv(c.center.x, aX0, aX1), NsFast, "횡행");
            yield return MoveN(crane.Gantry, Inv(c.center.z, aZ0, aZ1), NsFast, "주행");
            float topN = Inv(c.center.y + c.size.y * 0.5f, aY0, aY1);
            yield return MoveN(crane.Spreader, topN, NsCreep, "권하(착상)");
            grabber.Grab();
            yield return new WaitForSeconds(0.3f);
            bool ok = crane.Attach.HasContainer;
            result?.Invoke(ok);
            QaLog.Info("DRIVE", "grabResult", $"target={c.id} grabbed={ok}");
            if (!ok) yield break;

            // 적재 상태로 권상 — HOIST/speed band=light/heavy 확인(VRController 합성입력).
            if (vr != null)
            {
                vr.enabled = true; yield return null;
                vr.QaBeginDrive(StsCraneVRController.Mode.Crane); yield return null;
                vr.QaSticks(Vector2.zero, new Vector2(0f, 0.5f));   // 적재 권상
                yield return new WaitForSeconds(0.4f);
                vr.QaEndDrive(); vr.enabled = false; yield return null;
            }
        }

        // 든 컨테이너를 다른 컨테이너 위로 하강 → footprint 겹침≥0.4면 윗면에서 안착(LAND/stack).
        IEnumerator PhaseStack(QuayContainerScanner.Recognized baseC)
        {
            if (debugLog) Debug.Log($"[QA드라이브] 단계4 — 적층 안착 (받침 {baseC.id})");
            yield return MoveN(crane.Spreader, 0.7f, NsHoist, "권상(적재)");
            yield return MoveN(crane.Trolley, Inv(baseC.center.x, aX0, aX1), NsFast, "횡행(적재)");
            yield return MoveN(crane.Gantry, Inv(baseC.center.z, aZ0, aZ1), NsFast, "주행(적재)");
            // 받침 윗면보다 더 아래로 '하강 시도' — 클램프/안착이 윗면에서 멈춰야 함.
            float intoN = Inv(baseC.center.y, aY0, aY1);
            yield return MoveN(crane.Spreader, intoN, NsCreep, "권하(적치)");
            yield return new WaitForSeconds(0.6f);
        }

        // 빈 스프레더를 컨테이너 옆에 낮게 내린 뒤 옆에서 밀어붙여 측면 깊이 진입 → sideHit(SIDE/hit).
        //   결정적 재현: 타깃을 일시 kinematic으로 고정(푸셔가 밀어내 footprint를 벗어나는 것 방지) + 신선 좌표로 재스캔.
        IEnumerator PhaseSideHit(QuayContainerScanner.Recognized _)
        {
            if (debugLog) Debug.Log("[QA드라이브] 단계5 — 측면 타넘기(결정적)");
            if (crane.Attach.HasContainer) { grabber.Release(); yield return new WaitForSeconds(0.3f); }

            // 신선 좌표로 재스캔 — 앞 단계에서 일부 컨테이너가 이동했을 수 있음.
            var fresh = new List<QuayContainerScanner.Recognized>();
            yield return ScanContainers(fresh);
            QuayContainerScanner.Recognized? pick = null;
            foreach (var r in fresh)
            {
                if (r.t == null) continue;
                if (r.t.GetComponentInParent<Rigidbody>() == null) continue;
                if (r.t.IsChildOf(crane.transform)) continue;
                pick = r; break;
            }
            if (pick == null) { QaLog.Info("SIDE", "skip", "reason=no-free-container"); yield break; }
            var c = pick.Value;
            var rb = c.t.GetComponentInParent<Rigidbody>();
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true;   // 푸셔에 안 밀리게 고정 → 측면 진입 기하 결정적

            yield return MoveN(crane.Spreader, 0.7f, NsHoist, "안전고");
            // 컨테이너 옆(footprint 밖으로 확실히 — 반폭+0.15m)으로 트롤리 이동
            float sideX = c.center.x + c.size.x * 0.5f + 0.15f;
            yield return MoveN(crane.Trolley, Inv(sideX, aX0, aX1), NsFast, "횡행→옆");
            yield return MoveN(crane.Gantry, Inv(c.center.z, aZ0, aZ1), NsFast, "주행→정렬");
            // 옆에서 컨테이너 중심 높이까지 하강(footprint 밖이라 클램프 없음) — 깊이 = 반높이 > 임계(0.25배)
            yield return MoveN(crane.Spreader, Inv(c.center.y, aY0, aY1), NsCreep, "권하(옆)");
            // 옆에서 컨테이너 쪽으로 밀어붙임 → 측면 깊이 진입 → sideHit
            yield return MoveN(crane.Trolley, Inv(c.center.x, aX0, aX1), NsCreep, "측면진입");
            yield return new WaitForSeconds(0.6f);

            if (!wasKinematic) rb.isKinematic = false;   // 동적 복원(원래 동적이었으면)
        }

        // 갠트리 주행 경로 앞에 장애물이 있으면 정지(밀지 않음) — GANTRY/block.
        //   결정적 재현: 여유 있는 주행 방향으로 다리 앞에 임시 kinematic 장애물(컨테이너 대용)을 두고 주행시킨다.
        IEnumerator PhaseGantryBlock(QuayContainerScanner.Recognized _)
        {
            if (debugLog) Debug.Log("[QA드라이브] 단계6 — 갠트리 장애물 정지(결정적)");
            yield return MoveN(crane.Spreader, 0.9f, NsHoist, "안전고");

            // 다리 콜라이더 1개 찾기(주행 경로 BoxCast 기준점).
            Transform leg = null;
            foreach (var t in crane.GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == StsPartNames.LegCollider) { leg = t; break; }
            if (leg == null) { QaLog.Info("GANTRY", "skip", "reason=no-leg-collider"); yield break; }

            // 여유 있는 주행 방향(현재 위치 기준)으로 장애물을 다리 앞 0.18m에 둔다.
            float gN = N(crane.Gantry);
            float sign = gN < 0.5f ? 1f : -1f;
            float targetN = sign > 0f ? 1f : 0f;
            Vector3 dir = (crane.transform.parent != null
                ? crane.transform.parent.TransformDirection(new Vector3(0f, 0f, sign))
                : new Vector3(0f, 0f, sign)).normalized;

            var obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obs.name = "QA_Obstacle";
            obs.transform.position = leg.position + dir * 0.18f;
            obs.transform.localScale = Vector3.one * 0.12f;
            var orb = obs.AddComponent<Rigidbody>();
            orb.isKinematic = true; orb.useGravity = false;   // attachedRigidbody!=null → GantryMover가 장애물로 인식
            QaLog.Info("GANTRY", "obstacle", $"at={QaLog.V(obs.transform.position)} dir={QaLog.V(dir)} driveTo={QaLog.F(targetN)}");

            yield return MoveN(crane.Gantry, targetN, NsFast, "주행(장애물향)");
            yield return new WaitForSeconds(0.3f);

            Destroy(obs);
        }

        // 결함주입 — 바닥에 놓인 동적 컨테이너를 바닥 윗면 아래로 강제로 밀어넣고, Stabilizer가 되돌리는지(FLOOR/guard).
        IEnumerator PhaseFloorGuard(List<QuayContainerScanner.Recognized> found)
        {
            if (debugLog) Debug.Log("[QA드라이브] 단계7 — 바닥 관통 하드클램프(결함주입)");
            Rigidbody target = null;
            foreach (var r in found)
            {
                if (r.t == null) continue;
                var rb = r.t.GetComponentInParent<Rigidbody>();
                if (rb == null || rb.isKinematic) continue;
                if (rb.transform.IsChildOf(crane.transform)) continue;
                target = rb; break;
            }
            if (target == null) { QaLog.Info("FLOOR", "inject-skip", "reason=no-dynamic-container"); yield break; }

            var p = target.position;
            QaLog.Info("FLOOR", "inject", $"body={target.name} y_before={QaLog.F(p.y)} push=-0.030");
            p.y -= 0.030f;                       // 바닥 윗면 아래 3cm로 강제 침투(skin 0.004 초과 → 가드 작동해야)
            target.position = p;
            target.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.3f);
        }

        // ───────────────────────── 헬퍼 ─────────────────────────

        IEnumerator ScanContainers(List<QuayContainerScanner.Recognized> outList)
        {
            scanner = FindFirstObjectByType<QuayContainerScanner>();
            if (scanner == null) scanner = gameObject.AddComponent<QuayContainerScanner>();
            int tries;
            for (tries = 0; tries < 30; tries++)
            {
                if (scanner.Scan() > 0) break;
                yield return null;
            }
            outList.Clear();
            outList.AddRange(scanner.Found);
            if (debugLog) Debug.Log($"[QA드라이브] 스캔 — {outList.Count}개 인식(시도 {tries + 1}).");
        }

        // 축을 normalized 목표로 정규화 속도(nps=범위 비율/초)로 구동. MoveToNormalized → AxisMoverBase.MoveTo(클램프/블록 경유).
        //   목표 0.8초간 무진전(클램프/블록)이면 그 이동만 종료 → 클램프가 막는 동안 PASS/order 등이 찍힌다.
        IEnumerator MoveN(IAxisMover m, float targetN, float nps, string label, float timeout = MoveTimeout)
        {
            targetN = Mathf.Clamp01(targetN);
            float t = 0f, best = Mathf.Abs(N(m) - targetN), noProg = 0f;
            while (t < timeout)
            {
                float cur = N(m);
                if (Mathf.Abs(cur - targetN) < 2e-3f) break;
                m.MoveToNormalized(Mathf.MoveTowards(cur, targetN, nps * Time.deltaTime));
                t += Time.deltaTime;
                yield return null;
                float nd = Mathf.Abs(N(m) - targetN);
                if (nd < best - 1e-4f) { best = nd; noProg = 0f; }
                else { noProg += Time.deltaTime; if (noProg > 0.8f) break; }   // 막힘(클램프/블록) — 그 이동만 종료
            }
            if (debugLog) Debug.Log($"[QA드라이브] {label}: N {N(m):F3} (목표 {targetN:F3})");
        }

        static float N(IAxisMover m) => Mathf.InverseLerp(m.Min, m.Max, m.Current);
        static float Inv(float v, float a0, float a1) { float d = a1 - a0; return Mathf.Abs(d) < 1e-6f ? 0.5f : (v - a0) / d; }

        // 축↔월드 선형 매핑 — 부착앵커(그랩 평면)를 두 정규화 위치에서 샘플해 기울기/절편 산출.
        const float CalA = 0.40f, CalB = 0.60f;
        void Calibrate()
        {
            float ogT = N(crane.Trolley), ogG = N(crane.Gantry), ogH = N(crane.Spreader);
            SampleAxis(crane.Trolley, 0, out aX0, out aX1);
            SampleAxis(crane.Gantry, 2, out aZ0, out aZ1);
            SampleAxis(crane.Spreader, 1, out aY0, out aY1);
            crane.Trolley.MoveToNormalized(ogT);
            crane.Gantry.MoveToNormalized(ogG);
            crane.Spreader.MoveToNormalized(ogH);
            Physics.SyncTransforms();
            if (debugLog) Debug.Log($"[QA드라이브] 캘리브 — X[{aX0:F3},{aX1:F3}] Z[{aZ0:F3},{aZ1:F3}] Y[{aY0:F3},{aY1:F3}]");
        }

        void SampleAxis(IAxisMover m, int comp, out float a0, out float a1)
        {
            Transform anchor = crane.Attach.AttachAnchor;
            m.MoveToNormalized(CalA); Physics.SyncTransforms(); float s0 = Comp(anchor.position, comp);
            m.MoveToNormalized(CalB); Physics.SyncTransforms(); float s1 = Comp(anchor.position, comp);
            float slope = (s1 - s0) / (CalB - CalA);
            a0 = s0 - slope * CalA;
            a1 = a0 + slope;
        }

        static float Comp(Vector3 v, int i) => i == 0 ? v.x : (i == 1 ? v.y : v.z);
    }
}
