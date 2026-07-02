using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 배 → 항구(육지 야드) 전량 양하 시나리오 — 처음부터 새로 작성(2026-06-22, 오너 지시).
    ///
    /// 흐름: ①갠트리 주행범위 자동 계산 → ②컨테이너 인식(재시도 스캔) → ③축 캘리브레이션 →
    ///       ④윗단부터 정렬·도달성 필터·개수 상한(기본 10) → ⑤각 컨테이너: 정렬→하강→★집기(닫힌 루프)
    ///       →들어올림→야드로 이송→크기별 슬롯 적치→복귀. 매 단계 알람 자동정지(AutoStop) 확인.
    ///
    /// [이번 재작성의 핵심 — 집기 오류 해결]
    ///   기존엔 "윗면까지 하강 → Grab() 1회 → 실패면 건너뜀"이라, 스프레더가 컨테이너 근처에 갔는데도
    ///   클램프 정착 타이밍/미세 정렬오차로 1회 시도가 빗나가면 그냥 스킵됐다(= '근처 갔는데 못 잡음').
    ///   → '닫힌 루프 집기'로 교체: 통과방지 클램프가 컨테이너 윗면에 정착하길 기다린 뒤, 여러 번
    ///     재시도하고 미세 하강으로 접촉을 보장한다(클램프가 윗면을 유지하므로 과하강·관통 없음).
    ///
    /// 야드 배치(계산): 20ft 열 X≈-0.55(백리치, 다리 육지쪽) / 40ft 열 X≈0.0(트럭차선, 다리 사이).
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Unload Scenario (배→항구 양하, 최대 10)")]
    [RequireComponent(typeof(StsCrane))]
    [RequireComponent(typeof(SpreaderGrabber))]
    [DisallowMultipleComponent]
    public sealed class CraneUnloadScenario : CraneScenarioBase
    {
        [Header("실척 속도 (m/s) — StsCraneVRController와 동일 모델")]
        [SerializeField] float trolleyMps = 3.3f;
        [SerializeField] float gantryMps = 0.75f;
        [Tooltip("빈 스프레더(공하) 권상 ≈2.7 m/s")]
        [SerializeField] float hoistEmptyMps = 2.7f;
        [Tooltip("경하중(8t) 권상 ≈1.8 m/s")]
        [SerializeField] float hoistLightMps = 1.8f;
        [Tooltip("정격(32t) 권상 ≈0.9 m/s — 무거울수록 이 값에 수렴")]
        [SerializeField] float hoistHeavyMps = 0.9f;

        [Header("가감속 / 크리프 (현실화)")]
        [Tooltip("0→최대속도 도달 시간(초). 가속·감속 램프 = S커브 흉내.")]
        [SerializeField] float accelTime = 1.2f;
        [Tooltip("착상 직전 정밀 접근 속도(m/s).")]
        [SerializeField] float hoistCreepMps = 0.4f;
        [Tooltip("이 정규화 거리 안에서 크리프로 감속(착상/적치 정밀).")]
        [SerializeField] float creepZoneN = 0.06f;

        [Header("자세")]
        [SerializeField, Range(0f, 1f)] float safeHoist = 0.88f;
        [Tooltip("이동 상한(초). 현실 속도는 느려 길게 둠 — 막힘은 무진전 1.0초로 별도 차단.")]
        [SerializeField] float moveTimeout = 50f;

        [Header("양하 개수")]
        [Tooltip("옮길 컨테이너 개수(윗단부터). 0 이하 = 전량. 기본 10개.")]
        [SerializeField] int maxContainers = 10;

        [Header("야드 (월드 X) — 육지쪽")]
        [Tooltip("20ft 열 X — 백리치(다리 육지쪽 X<-0.33).")]
        [SerializeField] float row20X = -0.55f;
        [Tooltip("40ft 열 X — 트럭차선(다리 사이 X≈0).")]
        [SerializeField] float row40X = 0.0f;
        [Tooltip("슬롯당 적재 단수.")]
        [SerializeField] int perSlot = 2;
        [Tooltip("슬롯 간 Z 여유(컨테이너 길이에 더함).")]
        [SerializeField] float slotGapZ = 0.06f;

        [Header("집기 (닫힌 루프 — '근처 갔는데 못 잡음' 해결)")]
        [Tooltip("윗면 하강 후 집기 재시도 횟수.")]
        [SerializeField] int grabAttempts = 6;
        [Tooltip("각 집기 시도 후 부착 확인까지 대기(초).")]
        [SerializeField] float grabSettle = 0.12f;
        [Tooltip("재시도 시 미세 하강량(정규화). 클램프가 윗면을 유지하므로 과하강 없음 — 접촉 보장용.")]
        [SerializeField] float grabNudgeN = 0.01f;

        [Header("인식 (견고화)")]
        [Tooltip("스캔이 0개면 한 프레임 뒤 재시도 — 최대 시도 횟수(스폰/물리 정착 타이밍 흡수).")]
        [SerializeField] int scanRetries = 30;
        [Tooltip("도달성 마진 — 정규화 [0,1]에서 이만큼 밖이어도 클램프해 '시도'한다(끝단 경계 구제). 이 범위마저 넘으면 건너뜀.")]
        [SerializeField] float reachMargin = 0.15f;

        [Header("시작 시 갠트리 주행범위 자동 계산")]
        [Tooltip("배 앞/뒤 끝 베이까지 도달하도록 레일 길이에 맞춰 갠트리 Min/Max를 스캔/캘리브 전에 자동 계산(권장 ON).")]
        [SerializeField] bool fitGantryRangeOnStart = true;

        protected override string Tag => "양하";

        QuayContainerScanner scanner;
        float aX0, aX1, aZ0, aZ1, aY0, aY1, floorY, destFloorY, cH;

        GantryMover gantry;          // 시나리오가 직접 잡는 갠트리(장애물정지 토글·범위맞춤용)
        bool gantryStopSaved;        // 시작 시점의 StopOnObstacle 값(끝나면 원복)
        bool gantryStopOverridden;   // 우리가 껐는지(원복 가드)

        Collider[] legCols;          // 다리 콜라이더(Leg_Collider) — 자동 주행 중 토플 방지로 잠깐 끔
        bool[] legColSaved;          // 끄기 전 enabled 값(원복용)
        bool legColsDisabled;        // 우리가 껐는지(원복 가드)

        bool pusherSaved;            // 끄기 전 스프레더 푸셔 활성값(원복용)
        bool pusherOverridden;       // 우리가 껐는지(원복 가드)
        float yardCenterZ;           // 야드 슬롯 Z 중심 = 크레인 시작 Z (갠트리 왕복 최소화)

        // 자동 양하 동안 소스 컨테이너를 kinematic 고정 — 잡기·들기 시 주변 밀림/토플 + HO Snag(3013) 오발 방지.
        readonly Dictionary<Rigidbody, (bool kin, bool grav)> frozen = new Dictionary<Rigidbody, (bool, bool)>();
        readonly HashSet<Transform> placedSet = new HashSet<Transform>();   // 적치 완료(끝나도 kinematic 유지)
        bool containersFrozen;

        // ★ CargoSleepManager — 스프레더 접근 시 화물을 '깨워(동적)' freeze를 덮어 튕김을 유발. 시나리오 동안 끈다.
        CargoSleepManager sleepMgr;
        bool sleepMgrSaved;
        bool sleepMgrOverridden;

        // ★ 첫 물리틱 '전에' 소스 컨테이너를 고정 — 겹쳐 스폰된 컨테이너가 startDelay(0.5s) 동안 물리로
        //   폭발해 허공으로 튕겨 흩어지던 것(호 모양)을 원천 차단. RunScenario의 FreezeContainers는 가드로 no-op.
        protected override void Awake()
        {
            base.Awake();
            if (!runOnStart) return;
            // CargoSleepManager가 첫 Update(0.15s)에 근처 화물을 '깨우기' 전에 선제로 끈다(있으면). 없으면 RunScenario에서 처리.
            var sm = FindAnyObjectByType<CargoSleepManager>();
            if (sm != null) { sleepMgr = sm; sleepMgrSaved = sm.enabled; sm.enabled = false; sleepMgrOverridden = true; }
            FreezeContainers();
        }

        protected override IEnumerator RunScenario()
        {
            gantry = crane != null ? crane.GetComponent<GantryMover>() : null;

            // ★(B) 자동 시나리오는 갠트리를 결정적으로 구동한다 → 장애물정지(다리 BoxCast)가 컨테이너를
            //   '장애물'로 보고 멈춰 베이 도달을 막던 문제(=갠트리가 컨테이너 위치 못 감)를 차단한다.
            //   시나리오 동안만 끄고, 끝나거나 컴포넌트가 꺼지면 원복(수동 VR 주행은 장애물정지 유지).
            if (gantry != null && !gantryStopOverridden)
            { gantryStopSaved = gantry.StopOnObstacle; gantry.StopOnObstacle = false; gantryStopOverridden = true; }

            // ★(B-2) 장애물정지를 끄면 다리 콜라이더(kinematic)가 주행 중 컨테이너를 밀어 '잡기도 전에' 무너뜨린다.
            //   → 자동 주행 동안 다리 콜라이더를 비활성(통과만, 밀지 않음)하고 끝나면 원복한다.
            //     (수동 VR 주행에선 콜라이더 유지 — 거기선 장애물정지가 켜져 있어 밀 일이 없다.)
            DisableLegColliders();

            // ★ 스프레더 물리 푸셔 끄기 — 잡으러 하강할 때 푸셔 박스가 대상 컨테이너를 밀어 무너뜨리던 문제 해결.
            if (grabber != null && !pusherOverridden)
            { pusherSaved = grabber.IsPusherActive; grabber.SetPusherActive(false); pusherOverridden = true; }

            // 야드 슬롯 Z 중심 = 크레인 현재 Z. 슬롯이 월드 Z=0 고정이라 매 컨테이너 크레인↔Z0 왕복하던
            //   '갠트리 이동 과다'를 제거 — 픽업 Z 근처에 적치해 갠트리 주행을 최소화한다.
            yardCenterZ = crane != null ? crane.transform.position.z : 0f;

            // ★ CargoSleepManager 끄기 — 이게 스프레더 접근 시 화물을 동적으로 '깨워' freeze를 덮고 겹침해소로
            //   컨테이너를 튕기던 진짜 원인. 자동 시나리오는 화물을 직접 freeze로 다루므로 근접-깨우기가 불필요·유해.
            sleepMgr = FindAnyObjectByType<CargoSleepManager>();
            if (sleepMgr != null && !sleepMgrOverridden)
            { sleepMgrSaved = sleepMgr.enabled; sleepMgr.enabled = false; sleepMgrOverridden = true;
              Debug.Log($"[{Tag}] CargoSleepManager 비활성 — 근접 시 화물을 동적으로 깨워 freeze를 덮던 것 차단(끝나면 원복)."); }

            // ★ 소스 컨테이너 kinematic 고정 — 꽉 찬 그리드에서 한 개 들 때 옆면 접촉으로 HO Snag(3013) 오발 +
            //   동적 충돌로 주변이 튕겨 떠오르던 문제 해결(kinematic↔kinematic은 충돌 이벤트·밀림 없음). 끝나면 미배치분 원복.
            FreezeContainers();

            // 0) ★ 갠트리 주행범위 자동 계산 — 캘리브레이션/도달성 판정 '전에'.
            if (fitGantryRangeOnStart)
            {
                if (crane != null && gantry != null && GantryRangeFit.Apply(crane.gameObject, gantry, out string fitMsg))
                    Debug.Log($"[{Tag}] 갠트리 주행범위 자동 맞춤 — {fitMsg}");
                else
                    Debug.LogWarning($"[{Tag}] 갠트리 주행범위 자동 맞춤 생략 — " +
                                     (gantry == null ? "GantryMover 없음." : "부두 레일(Lane/QuayRail) 못 찾음.") +
                                     " 기존 Min/Max로 진행(끝 베이 도달불가일 수 있음).");
                Physics.SyncTransforms();
            }

            // 1) 인식 — 재시도 스캔(타이밍 흡수)
            var all = new List<QuayContainerScanner.Recognized>();
            yield return ScanContainers(all);
            if (all.Count == 0)
            {
                Debug.LogWarning($"[{Tag}] 컨테이너 0개 — 중단. (씬에 화물이 있는지, 직전 사이클이 크레인에 매단 채 " +
                                 "남겼는지 확인. 메뉴 'Object/컨테이너/부두에 컨테이너 배치'로 재배치 가능)");
                RestoreGantryStop();
                yield break;
            }

            // 2) 캘리브레이션(축 정규화 ↔ 월드 좌표 실측) — 도달성/픽업 좌표의 기준. 도달성 필터보다 먼저.
            Calibrate();

            // 2.5) 도달성 사전 필터 — 상한 모드(cap>0)에서만. 갑판 시어로 뱃머리 갑판이 높아 'Y 상위 N개'를
            //   그냥 뽑으면 도달불가 베이로 몰려 크레인이 안 움직이는 버그 방지. 가동범위 '안쪽(마진 0)'만 남긴다.
            int cap = maxContainers;
            if (cap > 0)
            {
                int rawCount = all.Count;
                all.RemoveAll(t => !IsReachable(t, 0f));
                int unreachable = rawCount - all.Count;
                if (unreachable > 0)
                    Debug.Log($"[{Tag}] 도달성 필터 — {rawCount}개 중 {unreachable}개는 가동범위 밖(앞/뒤 베이 등) 제외 → 대상 {all.Count}개.");
                if (all.Count == 0)
                {
                    Debug.LogWarning($"[{Tag}] 도달 가능한 컨테이너 0개 — 중단. 인식 {rawCount}개가 모두 가동범위 밖 " +
                                     $"(갠트리 Z[{aZ0:F2},{aZ1:F2}]·트롤리 X[{aX0:F2},{aX1:F2}]). 접안 정렬/미드십 위치 확인.");
                    RestoreGantryStop();
                    yield break;
                }
            }

            // 2.6) ★ 정렬 = '크레인 Z에서 가까운 베이 먼저', '같은 베이(Z 동일) 안에서만 윗단부터'.
            //   전역 최상단 정렬은 갑판 시어로 가장 높은 뱃머리(배 끝) 컨테이너를 첫 픽업으로 뽑아
            //   시작하자마자 갠트리가 배 끝까지 달려가게 했다. → 가까운 베이부터 처리해 갠트리 주행 최소화.
            //   같은 Z(한 스택/한 베이)면 Y 내림차순이라 윗단부터 집어 아래 것이 위 것에 안 막힘(스택 안전 유지).
            all.Sort((a, b) =>
            {
                float da = Mathf.Abs(a.center.z - yardCenterZ);
                float db = Mathf.Abs(b.center.z - yardCenterZ);
                int c = da.CompareTo(db);
                return c != 0 ? c : b.center.y.CompareTo(a.center.y);
            });
            if (cap > 0 && all.Count > cap)
            {
                Debug.Log($"[{Tag}] 처리 개수 상한 {cap}개 — 도달가능 {all.Count}개 중 가까운 베이 {cap}개만 옮기고 나머지는 유지.");
                all.RemoveRange(cap, all.Count - cap);
            }

            // 3) 바닥Y·높이·크기별 길이·개수
            floorY = float.MaxValue;
            float len20 = 0.25f, len40 = 0.51f;
            int n20 = 0, n40 = 0;
            foreach (var r in all)
            {
                float bot = r.center.y - r.size.y * 0.5f; if (bot < floorY) floorY = bot;
                if (r.is40ft) { n40++; len40 = Mathf.Max(len40, r.size.z); }
                else { n20++; len20 = Mathf.Max(len20, r.size.z); }
            }
            if (floorY > 1e5f) floorY = 0f;
            cH = all[0].size.y;

            // ★ 하역면은 '소스 컨테이너 바닥'이 아니라 '목적지 야드의 실제 지면'. 양하는 소스가 배 갑판(높음)이라
            //   floorY를 그대로 쓰면 육지 야드 위 공중부양 적치가 된다 → destFloorY로 분리해 지면에 안착.
            destFloorY = ResolveDestFloorY();

            var slot20Z = MakeSlots(n20, len20 + slotGapZ);
            var slot40Z = MakeSlots(n40, len40 + slotGapZ);
            Debug.Log($"[{Tag}] 총 {all.Count} (20ft {n20}, 40ft {n40}) · 소스바닥Y {floorY:F3} 하역지면Y {destFloorY:F3} H {cH:F3} · " +
                      $"20슬롯 {slot20Z.Count} 40슬롯 {slot40Z.Count} · 목표열 20ft X{row20X:F2} / 40ft X{row40X:F2}");

            // 4) 적치 — 매 단계 AutoStop 확인
            int c20 = 0, c40 = 0, done = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (CheckAutoStop()) break;
                var t = all[i];
                bool is40 = t.is40ft;
                int idx = is40 ? c40 : c20;
                int slot = idx / Mathf.Max(1, perSlot);
                int level = idx % Mathf.Max(1, perSlot);
                var slots = is40 ? slot40Z : slot20Z;
                float slotZ = slots[Mathf.Min(slot, slots.Count - 1)] + yardCenterZ;   // 크레인 Z 중심(갠트리 왕복 최소화)
                float rowX = is40 ? row40X : row20X;
                Debug.Log($"[{Tag}] ===== {i + 1}/{all.Count} {(is40 ? "40ft" : "20ft")} → 열X {rowX:F2}, 슬롯{slot} Z{slotZ:F2}, {level + 1}단 =====");
                bool placed = false;
                yield return PickAndStow(t, rowX, slotZ, level, b => placed = b);
                if (placed) { if (is40) c40++; else c20++; done++; }
            }

            if (autoStopped)
                Debug.LogWarning($"[{Tag}] ⛔ 알람 자동정지로 중단 — {done}/{all.Count} 처리 후 멈춤.");
            else
                Debug.Log($"[{Tag}] ✅ 양하 완료 — 20ft {c20}개 + 40ft {c40}개" +
                          (maxContainers > 0 ? $"(상한 {maxContainers}개)" : " 전량") + " 육지쪽 야드에 정리 적재");

            scanner.Scan();   // 최종 상태 재스캔 → 겹침 전수 검사 보고
            RestoreGantryStop();   // 끝 → 갠트리 장애물정지 원복(이후 수동 VR 주행 대비)
        }

        // 갠트리 장애물정지 + 다리 콜라이더를 시작 시점 값으로 원복(우리가 바꿨을 때만).
        void RestoreGantryStop()
        {
            if (gantryStopOverridden && gantry != null) gantry.StopOnObstacle = gantryStopSaved;
            gantryStopOverridden = false;
            RestoreLegColliders();
            if (pusherOverridden && grabber != null) grabber.SetPusherActive(pusherSaved);
            pusherOverridden = false;
            if (sleepMgrOverridden && sleepMgr != null) sleepMgr.enabled = sleepMgrSaved;   // CargoSleepManager 원복(VR 수동조작 복귀)
            sleepMgrOverridden = false;
            RestoreFrozenContainers();
        }

        // 소스 컨테이너 kinematic '재확인' 고정 — kinematic↔kinematic은 충돌 이벤트·밀림이 없어 토플/Snag/튕김을 원천 차단.
        //   ★ 가드로 1회만 걸지 않고, 호출될 때마다 현재 dynamic인 컨테이너를 다시 고정한다(다른 스크립트가 풀어도 잡기 전 재확인).
        //   최초 상태만 저장해 끝나면 원복하고, 적치분(placedSet)은 건드리지 않는다.
        void FreezeContainers()
        {
            if (crane == null) return;
            int froze = 0;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude))
            {
                if (rb == null || rb.transform.IsChildOf(crane.transform)) continue;   // 크레인 부속/매단 화물 제외
                if (!frozen.ContainsKey(rb)) frozen[rb] = (rb.isKinematic, rb.useGravity);   // 최초 상태만 저장(원복용)
                if (placedSet.Contains(rb.transform)) continue;                              // 적치분은 그대로
                if (!rb.isKinematic) { rb.isKinematic = true; rb.useGravity = false; froze++; }
            }
            if (!containersFrozen)
                Debug.Log($"[{Tag}] 소스 컨테이너 {frozen.Count}개 kinematic 고정 — 잡기·들기 시 밀림/토플/튕김·Snag 방지(끝나면 미배치분 원복).");
            else if (froze > 0)
                Debug.LogWarning($"[{Tag}] 컨테이너 {froze}개가 dynamic으로 풀려 있어 재고정 — 다른 스크립트가 freeze를 덮는 중(추적 필요).");
            containersFrozen = true;
        }

        // 미배치(안 옮긴) 컨테이너만 원래 물리 상태로 원복 — 적치분(placedSet)·매단 것은 kinematic 유지.
        void RestoreFrozenContainers()
        {
            if (!containersFrozen) return;
            foreach (var kv in frozen)
            {
                var rb = kv.Key;
                if (rb == null) continue;
                if (placedSet.Contains(rb.transform)) continue;                          // 적치분: kinematic 유지(드리프트 방지)
                if (crane != null && rb.transform.IsChildOf(crane.transform)) continue;   // 아직 매달린 것 제외
                rb.isKinematic = kv.Value.kin;
                rb.useGravity = kv.Value.grav;
            }
            containersFrozen = false;
        }

        // 다리 콜라이더(Leg_Collider) 비활성 — 자동 주행이 컨테이너를 밀어 토플시키는 것 방지(잡기 전 무너짐).
        void DisableLegColliders()
        {
            if (legColsDisabled || crane == null) return;
            var found = new List<Collider>();
            foreach (var tr in crane.GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(tr.name) == StsPartNames.LegCollider)
                {
                    var c = tr.GetComponent<Collider>();
                    if (c != null) found.Add(c);
                }
            legCols = found.ToArray();
            legColSaved = new bool[legCols.Length];
            for (int i = 0; i < legCols.Length; i++) { legColSaved[i] = legCols[i].enabled; legCols[i].enabled = false; }
            legColsDisabled = true;
            Debug.Log($"[{Tag}] 다리 콜라이더 {legCols.Length}개 비활성 — 자동 주행 중 컨테이너 토플 방지(끝나면 원복).");
        }

        // 다리 콜라이더 원복(우리가 껐을 때만).
        void RestoreLegColliders()
        {
            if (!legColsDisabled) return;
            if (legCols != null)
                for (int i = 0; i < legCols.Length; i++)
                    if (legCols[i] != null) legCols[i].enabled = legColSaved[i];
            legColsDisabled = false;
        }

        // ───────── 진단: 잡기로 '주변 컨테이너가 반응'하는지 ─────────
        // 씬의 컨테이너(크레인 외부 강체) 현재 위치·회전 스냅샷.
        Dictionary<Transform, (Vector3 pos, Quaternion rot)> SnapshotContainers(string targetId)
        {
            var snap = new Dictionary<Transform, (Vector3, Quaternion)>();
            if (crane == null) return snap;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (rb != null && !rb.transform.IsChildOf(crane.transform))
                    snap[rb.transform] = (rb.position, rb.rotation);
            Debug.Log($"[양하·진단] {targetId} 하강 전 — 주변 컨테이너 {snap.Count}개 스냅샷(잡은 것 제외하고 반응 추적).");
            return snap;
        }

        // 스냅샷 대비 움직인(밀림/토플) 컨테이너 보고. 잡은 것은 제외.
        void ReportContainerMovement(Dictionary<Transform, (Vector3 pos, Quaternion rot)> before, Transform grabbed, string when)
        {
            if (before == null) return;
            int reacted = 0;
            foreach (var kv in before)
            {
                var tr = kv.Key;
                if (tr == null) continue;
                if (grabbed != null && (tr == grabbed || tr.IsChildOf(grabbed) || grabbed.IsChildOf(tr))) continue;   // 잡은 것 제외
                float dPosM = Vector3.Distance(tr.position, kv.Value.pos) * StsConfig.InvModelScale;   // 실척 m
                float dAng = Quaternion.Angle(tr.rotation, kv.Value.rot);
                if (dPosM > 0.02f || dAng > 1f)   // 실척 2cm 이상 이동 또는 1° 이상 회전 = 반응
                {
                    reacted++;
                    Debug.LogWarning($"[양하·진단] {when} — 주변 반응: {CraneHud.BaseName(tr.name)} 이동 {dPosM * 100f:F1}cm · 회전 {dAng:F1}°");
                }
            }
            if (reacted == 0) Debug.Log($"[양하·진단] {when} — 주변 컨테이너 반응 없음(이동<2cm·회전<1°). ✓");
            else Debug.LogWarning($"[양하·진단] {when} — 주변 {reacted}개 반응(밀림/토플). 위 목록 참조 — 원인 파악 자료.");
        }

        // 시나리오가 중간에 꺼지거나(컴포넌트 제거·Play 종료) 코루틴이 끊겨도 장애물정지를 반드시 원복.
        protected override void OnDisable()
        {
            base.OnDisable();   // 코루틴 정지 + VR 복귀
            RestoreGantryStop();
        }

        // ───────── 인식(재시도 스캔) ─────────
        IEnumerator ScanContainers(List<QuayContainerScanner.Recognized> outList)
        {
            scanner = FindFirstObjectByType<QuayContainerScanner>();
            if (scanner == null) scanner = gameObject.AddComponent<QuayContainerScanner>();
            int n = 0, tries = 0;
            for (tries = 0; tries < Mathf.Max(1, scanRetries); tries++)
            {
                n = scanner.Scan();
                if (n > 0) break;
                yield return null;
            }
            outList.Clear();
            outList.AddRange(scanner.Found);
            Debug.Log($"[{Tag}] 스캔 — {outList.Count}개 인식 (시도 {tries + 1}회).");
        }

        // 하역 목적지(육지 야드)의 지면 월드Y — ContainerPhysics와 동일 규약.
        float ResolveDestFloorY()
        {
            var vf = GameObject.Find("VirtualFloor");
            if (vf != null && vf.TryGetComponent<BoxCollider>(out var bc))
                return bc.transform.TransformPoint(bc.center + new Vector3(0f, bc.size.y * 0.5f, 0f)).y;

            var quay = GameObject.Find(StsPartNames.QuayGround);
            if (quay != null)
            {
                var rends = quay.GetComponentsInChildren<Renderer>(true);
                if (rends != null && rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    return b.max.y;
                }
            }
            Debug.LogWarning($"[{Tag}] 하역지면 산출 — VirtualFloor·{StsPartNames.QuayGround} 모두 없음 → 0(아스팔트 윗면 규약) 사용.");
            return 0f;
        }

        // 컨테이너가 트롤리(X)·갠트리(Z) 가동범위 안(±margin)인가 — Calibrate() 이후에만 유효.
        bool IsReachable(QuayContainerScanner.Recognized t, float margin)
        {
            float pT = Inv(t.center.x, aX0, aX1), pG = Inv(t.center.z, aZ0, aZ1);
            return pT >= -margin && pT <= 1f + margin && pG >= -margin && pG <= 1f + margin;
        }

        // 컨테이너 n개를 perSlot 단으로 → 슬롯 수, Z 중앙정렬 배열
        List<float> MakeSlots(int nContainers, float spacing)
        {
            int nSlots = Mathf.Max(1, Mathf.CeilToInt(nContainers / (float)Mathf.Max(1, perSlot)));
            var list = new List<float>();
            float mid = (nSlots - 1) * 0.5f;
            for (int i = 0; i < nSlots; i++) list.Add((i - mid) * spacing);
            return list;
        }

        // 한 컨테이너: 픽업(정렬→하강→★닫힌 루프 집기) → 적치(이송→단 높이 하강→놓기·고정). 성공 여부를 result로 보고.
        IEnumerator PickAndStow(QuayContainerScanner.Recognized t, float rowX, float slotZ, int level, System.Action<bool> result)
        {
            result?.Invoke(false);
            float pT = Inv(t.center.x, aX0, aX1), pG = Inv(t.center.z, aZ0, aZ1);
            Debug.Log($"[{Tag}] 도달성 — {t.id} 월드({t.center.x:F2},{t.center.y:F2},{t.center.z:F2}) → 트롤리 {pT:F2}, 갠트리 {pG:F2}");
            if (pT < -reachMargin || pT > 1f + reachMargin || pG < -reachMargin || pG > 1f + reachMargin)
            { Debug.LogWarning($"[{Tag}] 도달불가 — 건너뜀 (트롤리 {pT:F2}, 갠트리 {pG:F2}, 마진 ±{reachMargin:F2})."); yield break; }

            float trolN = NPerSec(crane.Trolley, trolleyMps);
            float gantN = NPerSec(crane.Gantry, gantryMps);
            float emptyN = NPerSec(crane.Spreader, hoistEmptyMps);
            float loadMps = HoistMpsForLoad(t.tons);
            float loadN = NPerSec(crane.Spreader, loadMps);
            float creepN = NPerSec(crane.Spreader, hoistCreepMps);
            Debug.Log($"[{Tag}] {t.id} {t.tons:F1}t → 권상속도 {loadMps:F2} m/s (공하 {hoistEmptyMps:F1})");

            // ── 픽업 ── 안전고 → 갠트리 단독 → 트롤리 → 사이즈 맞춤 → 크리프 착상 → ★닫힌 루프 집기·잠금
            yield return MoveP(crane.Spreader, safeHoist, emptyN, "권상↑");
            yield return MoveP(crane.Gantry, pG, gantN, "주행");
            yield return MoveP(crane.Trolley, pT, trolN, "횡행");
            if (autoStopped) yield break;

            // ── 진단: 갠트리(Z)·트롤리(X) 정렬 오차 — 앵커가 컨테이너 중심에서 실제 몇 cm 어긋났나(실척). ──
            if (crane.Attach != null && crane.Attach.AttachAnchor != null)
            {
                var ap = crane.Attach.AttachAnchor.position;
                float exZ = Mathf.Abs(ap.z - t.center.z) * StsConfig.InvModelScale * 100f;   // cm(실척)
                float exX = Mathf.Abs(ap.x - t.center.x) * StsConfig.InvModelScale * 100f;
                Debug.Log($"[{Tag}·정렬] {t.id} 정렬오차 — 갠트리Z {exZ:F1}cm · 트롤리X {exX:F1}cm " +
                          $"(앵커 {ap.z:F3}/{ap.x:F3} vs 컨테이너 {t.center.z:F3}/{t.center.x:F3})");
            }
            if (telescope != null) { telescope.Set40(t.is40ft); yield return WaitTelescope(); }

            // ★ 하강 직전 freeze 재확인 — 어떤 스크립트가 풀었어도 잡기 전에 다시 kinematic 고정(튕김 원천 차단).
            FreezeContainers();

            // ── 진단: 하강·집기로 '주변 컨테이너가 반응(밀림/토플)'하는지 — 직전 스냅샷 ──
            var preGrab = SnapshotContainers(t.id);

            yield return MoveP(crane.Spreader, Inv(t.center.y + t.size.y * 0.5f, aY0, aY1), emptyN, "권상↓", creepN);
            if (autoStopped) yield break;

            bool grabbed = false;
            yield return GrabClosedLoop(t, b => grabbed = b);
            // 집기 직후: 잡은 것 제외하고 움직인 주변 컨테이너 보고(반응 진단).
            ReportContainerMovement(preGrab, crane.Attach != null ? crane.Attach.AttachedContainer : null, $"{t.id} 하강+집기");
            if (autoStopped) yield break;
            if (!grabbed)
            { Debug.LogWarning($"[{Tag}] 집기 실패 — {t.id} 근처 도달했으나 {grabAttempts}회 시도 실패. 정렬/유효범위 확인 후 건너뜀."); yield break; }
            yield return WaitLock(true);

            // ── 이송 ── 똑바로 올려 파일을 벗어난 뒤 이동(옆 컨테이너 안 침) → 갠트리 단독 → 횡행
            float dT = Inv(rowX, aX0, aX1), dG = Inv(slotZ, aZ0, aZ1);
            yield return MoveP(crane.Spreader, safeHoist, loadN, "권상↑(적재)");
            yield return MoveP(crane.Gantry, dG, gantN, "주행(적재)");
            yield return MoveP(crane.Trolley, dT, trolN, "횡행(적재)");
            if (autoStopped) yield break;   // 이송 중 알람 → 화물 든 채 정지(놓지 않음)
            float anchorY = destFloorY + (level + 1) * cH + 0.004f;   // 목적지 지면 기준 단 높이
            yield return MoveP(crane.Spreader, Inv(anchorY, aY0, aY1), loadN, "권상↓(적치)", creepN);
            if (autoStopped) yield break;
            Transform held = crane.Attach.AttachedContainer;
            grabber.Release();
            if (held != null)
            {
                var rb = held.GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
                placedSet.Add(held);   // 적치 완료 — 끝나도 kinematic 유지(드리프트 방지, 원복 대상에서 제외)
                Debug.Log($"[{Tag}] 적치완료 — 실제({held.position.x:F2},{held.position.y:F2},{held.position.z:F2}) vs 목표(X{rowX:F2},Z{slotZ:F2},Y{anchorY:F2})");
            }
            yield return WaitLock(false);
            yield return MoveP(crane.Spreader, safeHoist, emptyN, "권상↑(복귀)");
            result?.Invoke(true);
        }

        // ★ 닫힌 루프 집기 — '근처 갔는데 못 잡음' 해결.
        //   통과방지 클램프가 컨테이너 윗면에 attachPoint를 고정하므로, 한 물리틱 정착을 기다린 뒤 Grab()을
        //   반복 시도하고, 안 잡히면 미세 하강(클램프가 윗면을 유지 → 과하강·관통 불가)으로 접촉을 보장한다.
        IEnumerator GrabClosedLoop(QuayContainerScanner.Recognized t, System.Action<bool> result)
        {
            result?.Invoke(false);
            float creepN = NPerSec(crane.Spreader, hoistCreepMps);
            for (int attempt = 0; attempt < Mathf.Max(1, grabAttempts); attempt++)
            {
                if (CheckAutoStop()) yield break;
                yield return new WaitForFixedUpdate();   // 통과방지 클램프가 윗면에 정착할 한 물리틱
                grabber.Grab();
                yield return new WaitForSeconds(grabSettle);
                if (crane.Attach != null && crane.Attach.HasContainer)
                {
                    if (attempt > 0) Debug.Log($"[{Tag}] 집기 성공 — {t.id} (재시도 {attempt + 1}회째).");
                    result?.Invoke(true);
                    yield break;
                }
                // 미세 하강 재시도(권상↓ = 정규화 감소). 클램프가 윗면을 유지하므로 접촉만 확실해짐.
                float curN = N(crane.Spreader);
                yield return MoveP(crane.Spreader, Mathf.Clamp01(curN - grabNudgeN), creepN, "권상↓(집기 재시도)");
            }
        }

        // 텔레스코프(20/40 사이즈 변경)가 멈출 때까지 대기 — 사이즈 차만큼 시간(같은 사이즈면 즉시).
        IEnumerator WaitTelescope()
        {
            if (telescope == null) yield break;
            float prev = telescope.CurrentHalf;
            yield return null;
            float t = 0f;
            while (t < 3f)
            {
                float cur = telescope.CurrentHalf;
                if (Mathf.Abs(cur - prev) < 1e-5f) break;
                prev = cur; t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[{Tag}] 텔레스코프 완료 — {(telescope.Is40 ? "40ft" : "20ft")} (half {telescope.CurrentHalf:F3})");
        }

        // 트위스트락 잠금/해제 애니메이션이 끝날 때까지 대기(실제 LockProgress 동기화).
        IEnumerator WaitLock(bool locked)
        {
            if (lockAnim == null) yield break;
            float t = 0f;
            while (t < 3f)
            {
                bool ok = locked ? lockAnim.LockProgress >= 0.999f : lockAnim.LockProgress <= 0.001f;
                if (ok) break;
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[{Tag}] 트위스트락 {(locked ? "잠금" : "해제")} 완료 — 진행 {lockAnim.LockProgress:F2}{(t >= 3f ? " (타임아웃)" : "")}");
        }

        // 실척 m/s → 정규화/초 (모델=실척×ModelScale, 정규화=모델÷가동범위)
        float NPerSec(IAxisMover m, float mps)
        {
            float range = Mathf.Abs(m.Max - m.Min);
            return range < 1e-6f ? 1f : (mps * crane.ModelScale) / range;
        }

        // 무게→권상 m/s: 8t→경하중, 32t→정격. 무거울수록 hoistHeavyMps에 수렴.
        float HoistMpsForLoad(float tons)
        {
            float loadT = Mathf.InverseLerp(8f, 32f, tons);
            return Mathf.Lerp(hoistLightMps, hoistHeavyMps, loadT);
        }

        // ───────── 캘리브레이션/이동 헬퍼 ─────────
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
            Debug.Log($"[{Tag}] 캘리브레이션 — X[{aX0:F3},{aX1:F3}] Z[{aZ0:F3},{aZ1:F3}] Y[{aY0:F3},{aY1:F3}]");
        }

        void SampleAxis(IAxisMover m, int comp, out float a0, out float a1)
        {
            // 방어 가드 — Attach/AttachAnchor 미준비 시 항등 매핑(a0=0,a1=1)으로 안전 탈출.
            if (crane == null || crane.Attach == null || crane.Attach.AttachAnchor == null)
            { Debug.LogWarning($"[{Tag}] 캘리브 스킵 — Attach/AttachAnchor 없음(항등 매핑)."); a0 = 0f; a1 = 1f; return; }
            Transform anchor = crane.Attach.AttachAnchor;
            m.MoveToNormalized(CalA); Physics.SyncTransforms(); float s0 = Comp(anchor.position, comp);
            m.MoveToNormalized(CalB); Physics.SyncTransforms(); float s1 = Comp(anchor.position, comp);
            float slope = (s1 - s0) / (CalB - CalA);
            a0 = s0 - slope * CalA;
            a1 = a0 + slope;
        }

        static float Comp(Vector3 v, int i) => i == 0 ? v.x : (i == 1 ? v.y : v.z);
        static float N(IAxisMover m) => Mathf.InverseLerp(m.Min, m.Max, m.Current);
        static float Inv(float v, float a0, float a1) { float d = a1 - a0; return Mathf.Abs(d) < 1e-6f ? 0.5f : (v - a0) / d; }

        // 가감속(S커브 흉내) + 선택 크리프 + ★AutoStop 게이트. 매 프레임 CheckAutoStop() 확인.
        //   목표 1.0초간 더 안 가까워지면(막힘/진동) 그 이동만 종료. creepV>0이면 마지막 creepZoneN에서 그 속도로 감속.
        IEnumerator MoveP(IAxisMover m, float target, float vmax, string name, float creepV = 0f)
        {
            if (autoStopped) yield break;
            target = Mathf.Clamp01(target);
            float accel = vmax / Mathf.Max(0.1f, accelTime);
            float dist0 = Mathf.Abs(N(m) - target);
            // ★(A) 타임아웃을 '거리/속도'로 계산 — 느린 갠트리(전구간 ~128초)의 장거리 이동이 진행 중인데도
            //   고정 50초에 잘리던 문제 해결. 예상 = 거리/속도 + 가감속 여유, 안전계수 2배 + 바닥(moveTimeout).
            //   진짜 막힘(장애물·진동)은 아래 무진전 1.0초로 별도 차단하므로 길게 잡아도 안전하다.
            float estimate = dist0 / Mathf.Max(vmax, 1e-4f) + 2f * accelTime;
            float timeout = Mathf.Max(moveTimeout, estimate * 2f + 2f);
            float v = 0f, t = 0f, noProg = 0f, best = dist0;
            while (t < timeout)
            {
                if (CheckAutoStop()) yield break;
                float cur = N(m);
                float dist = Mathf.Abs(cur - target);
                if (dist < 5e-4f) break;   // 도착 임계(정렬 정확도) — 갠트리 범위가 커서 1.5e-3은 ~16cm였음 → 5e-4 ≈ 5cm
                float vWant = Mathf.Min(vmax, Mathf.Sqrt(2f * accel * dist));
                if (creepV > 0f && dist < creepZoneN) vWant = Mathf.Min(vWant, creepV);
                v = Mathf.MoveTowards(v, vWant, accel * Time.deltaTime);
                m.MoveToNormalized(Mathf.MoveTowards(cur, target, v * Time.deltaTime));
                t += Time.deltaTime;
                yield return null;
                float nd = Mathf.Abs(N(m) - target);
                if (nd < best - 1e-5f) { best = nd; noProg = 0f; }   // 막판 크리프(미세 진전)도 '진전'으로 인정 → 5e-4까지 수렴(정렬 정확도)
                else { noProg += Time.deltaTime; if (noProg > 1.0f) { Debug.LogWarning($"[{Tag}] {name} 정지(막힘) {N(m):F3}/{target:F3}"); break; } }
            }
            if (t >= timeout) Debug.LogWarning($"[{Tag}] {name} 타임아웃 {N(m):F3}/{target:F3} (제한 {timeout:F0}s)");
        }
    }
}
