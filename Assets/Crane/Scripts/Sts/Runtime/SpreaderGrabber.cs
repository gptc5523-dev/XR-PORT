using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>외부 Rigidbody를 트위스트락 콘 기준으로 잡고/놓고, 빈 스프레더의 컨테이너 통과를 클램프로 막는다.
    /// order 50: 클램프가 VRController 축 이동(order 0) 뒤에 돌아야 떨림 없이 침투를 복원한다.</summary>
    [DefaultExecutionOrder(50)]
    [AddComponentMenu("AI-XR Crane/STS Crane/Spreader Grabber")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class SpreaderGrabber : MonoBehaviour
    {
        [Header("잡기")]
        [Tooltip("트위스트락(콘)에서 이 거리 안의 가장 가까운 컨테이너를 잡는다(m). 단 maxGrabRange로 상한이 걸린다.")]
        [SerializeField] float grabRange = 0.35f;
        [Tooltip("잡기 인식 반경의 상한(m). 1/24 미니어처라 grabRange가 커도 실제 반경은 이 값으로 제한된다. " +
                 "잡기 범위를 실제로 넓히려면 grabRange가 아니라 이 값을 올릴 것(기존 코드 상한 0.1을 인스펙터로 노출).")]
        [SerializeField] float maxGrabRange = 0.1f;
        [Tooltip("잡은 컨테이너 긴 축이 이 길이를 넘으면 40ft로 자동 신축(20ft≈0.25 / 40ft≈0.51, m)")]
        [SerializeField] float sizeThreshold = 0.38f;
        [Tooltip("Console에 집기 진단 로그 출력")]
        [SerializeField] bool debugLog = true;

        [Header("코너 안착(체결 조건)")]
        [Tooltip("트위스트락이 컨테이너 상단 코너캐스팅 위에 '동심 정렬 + 윗면 안착'됐을 때만 체결한다. " +
                 "켜면 '근처에만 있어도 잠김'을 차단(근접만으로는 안 잠금). 끄면 종전처럼 근접 잠금.")]
        [SerializeField] bool requireSeatedRegistration = true;
        [Tooltip("동심 정렬 수평 허용오차(m) — 스프레더(트위스트락 평균) 중심이 컨테이너 중심에서 이 거리 안일 때만. " +
                 "사이즈가 자동 정합되므로 이 값 이내면 콘 4개가 코너캐스팅 위에 놓인다. 0.015≈가이드 정합(실척 ~36cm).")]
        [SerializeField] float registerTolXZ = 0.015f;
        // 높이 밴드는 InsertU × [InsertMinFrac, InsertMaxFrac] 하나로 정한다(ConeBottomY 실측 기준).

        [Tooltip("콘 돌출량을 못 재는 크레인(콘 미탐색)에서만 쓰는 폴백 삽입 깊이(실척 m). " +
                 "정상 크레인은 이 값을 무시하고 씬 기하에서 유도한다 — InsertDepthMeters 참고.")]
        [SerializeField] float insertFallbackMeters = 0.04f;
        // ★ 상판 두께(ISO 1161 ≈16mm)는 1차 출처 없음 — 2차 출처로 삽입 상한을 만들지 말 것.

        float protrusionM = -1f;   // 콘 돌출량 실측 캐시(실척 m, 음수 = 아직 안 쟀음). 렌더러가 준비된 첫 사용 때 1회.

        /// <summary>콘 삽입 깊이(실척 m) = 씬 기하의 콘 돌출량(실측 STS 24mm · RTG 56mm), 못 재면 insertFallbackMeters.
        /// ★ 호출측 캐시 금지 — 매번 프로퍼티로 읽는다.</summary>
        public float InsertDepthMeters
        {
            get
            {
                if (protrusionM < 0f) protrusionM = MeasureProtrusionM();
                return protrusionM > 0f ? protrusionM : insertFallbackMeters;
            }
        }

        // 콘 돌출량(실척 m) = 콘 아닌 스프레더 최저 Y − 콘 바닥 Y. StsGrabProbe.ProtrusionM 과 같은 식 유지.
        //   0 이하면 안착 깊이 정의 불가 → 폴백.
        float MeasureProtrusionM()
        {
            var sp = crane != null ? crane.Spreader as Component : null;
            if (sp == null || twistlocks == null || twistlocks.Length == 0) return 0f;
            Transform held = crane.Attach != null ? crane.Attach.AttachedContainer : null;
            float coneB = ConeBottomY(), bodyB = float.MaxValue;
            foreach (var r in sp.GetComponentsInChildren<Renderer>())
            {
                if (held != null && r.transform.IsChildOf(held)) continue;
                bool inCone = false;
                foreach (var t in twistlocks)
                    if (t != null && (r.transform == t || r.transform.IsChildOf(t))) { inCone = true; break; }
                if (!inCone) bodyB = Mathf.Min(bodyB, r.bounds.min.y);
            }
            return bodyB < float.MaxValue ? (bodyB - coneB) / StsConfig.ModelScale : 0f;
        }

        [Header("통과 방지")]
        [SerializeField] bool blockPassThrough = true;
        [Tooltip("컨테이너 윗면 위로 둘 여유(m)")]
        [SerializeField] float topClearance = 0f;
        [Tooltip("footprint 바깥으로 이 수평 거리까지는 '위'로 보고 멈춤(m)")]
        [SerializeField] float passXZmargin = 0.1f;
        [Tooltip("Landing 센서 — 든 컨테이너가 아래 컨테이너 footprint와 이 비율 이상 겹치면 '적층'으로 보고 윗면에서 하강 정지. " +
                 "중심점 일치가 아니라 겹침 비율이라 살짝 어긋나게 내려놔도 멈춤(아래 것을 바닥으로 밀어넣지 않음). 옆 내려놓기(겹침~0)와 구분.")]
        [SerializeField] float landingOverlapFrac = 0.4f;

        [Header("물리 충돌(컨테이너 밀림/토플)")]
        [Tooltip("스프레더에 kinematic 콜라이더를 달아 컨테이너(동적 강체)를 물리적으로 밀어/넘어뜨린다. " +
                 "빈 스프레더가 옆에서 치면 컨테이너가 밀린다. 잡기와 충돌나면 끄고 'sideHit 타넘기 방지'만 유지.")]
        [SerializeField] bool spreaderCollider = true;

        [Header("Anti-Collision 경보(3013 HO Snag)")]
        [Tooltip("근접거리 skin(m). 컨테이너 contactOffset 합(≈0.002)보다 충분히 커야 밀려난 직후에도 근접이 잡힌다 " +
                 "(0.002면 gap과 같은 boundary라 '운 좋을 때만' 떴음). 기본 0.008. 너무 키우면 사전경고.")]
        [SerializeField] float antiCollisionSkin = 0.008f;
        [Tooltip("측면 충돌로 볼 최소 수직 겹침(m). 기본 0.02 ≈ 컨테이너 높이(0.108=2.591/24)×18%. " +
                 "윗면 적층은 수직 겹침≤0이라 자연 제외, 같은 높이 측면충돌만 통과.")]
        [SerializeField] float antiCollisionMinVertical = 0.02f;
        [Tooltip("충돌 해제 후 경보를 잠깐 더 유지하는 시간(s) — 경계 깜빡임 방지.")]
        [SerializeField] float loadCollisionHold = 0.3f;

        [Header("성능")]
        [SerializeField] float refreshInterval = 0.5f;

        StsCrane crane;
        SpreaderLockAnimator lockAnim;
        SpreaderTelescope telescope;      // (절차 STS) 잡은 컨테이너 크기에 맞춰 20/40ft 신축(스케일)
        RtgSpreaderTelescope rtgTele;     // (FBX RTG) 슬라이드 방식 신축
        SpreaderHoist spreaderHoist;   // 잡은 컨테이너 밑면 기준으로 하강 바닥 한계 설정
        Transform[] twistlocks;   // Twistlock_Cone들 — 잡기 기준점
        readonly List<Rigidbody> bodies = new List<Rigidbody>();   // 크레인 외부의 집을 수 있는 강체들(재사용 버퍼 — 매 갱신 새 할당 방지)
        float nextRefresh;

        float floorTopY;             // 바닥 윗면 월드 Y — 통과방지의 '항상 있는 받침'. SSOT = ContainerPhysicsStabilizer.FindFloorTopY(GameObject.Find 라 Start 1회)
        BoxCollider pusherBox;       // SpreaderPusher 콜라이더 — 텔레스코픽 현재 길이(2×current)에 맞춰 X를 매 프레임 갱신
        float lastAntiCollTime = -999f;   // 마지막 Anti-Collision 겹침 시각(hold 동안 경보 유지)

        // QA 판정용 이전 상태 — 엣지와 보정량 변화만 찍어 매 틱 로그 폭주 방지.
        bool qaPrevOver, qaPrevSideHit, qaPrevLoadColl, qaPrevLanded, qaPrevClampEmpty;
        float qaCorrPrev = -1f;

        // 전수 스캔(FindObjectsByType) 최소 간격 — 화물은 거의 안 변하고 잡힘 여부는 매 틱 IsChildOf로 거른다.
        const float MinRescanInterval = 3f;

        // 명명 상수(매직넘버 가시화, 값 불변): AntiCollisionSkinFloor=근접 감지 신뢰 하한,
        //   EmptyPassMarginCap=빈 스프레더 통과방지 여유 상한, SideHitDepthFrac=측면충돌 판정 깊이 비율.
        const float AntiCollisionSkinFloor = 0.006f;
        const float EmptyPassMarginCap = 0.03f;
        const float SideHitDepthFrac = 0.25f;

        Transform AttachPoint => (crane != null && crane.Attach != null) ? crane.Attach.transform : null;

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            lockAnim = GetComponentInChildren<SpreaderLockAnimator>(true);
            telescope = GetComponentInChildren<SpreaderTelescope>(true);
            rtgTele = GetComponentInChildren<RtgSpreaderTelescope>(true);
            spreaderHoist = crane != null ? crane.Spreader as SpreaderHoist : null;

            var list = new List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                // 절차 크레인: 'Twistlock_Cone'(BaseName). FBX 크레인: 'Spreader_Twistlock_F_L' 등(콘 4개).
                if (CraneHud.BaseName(t.name) == StsPartNames.TwistlockCone
                    || t.name.StartsWith(StsPartNames.SpreaderTwistlockPrefix))
                    list.Add(t);
            }
            twistlocks = list.ToArray();

            // 직렬화된 기존 씬 값(옛 0.002)도 신뢰 가능한 최소로 올린다 — gap≈0.002 에서도 근접이 잡히게.
            // 인스펙터 값이 하한/상한에 걸리면 1회 경고한다.
            if (antiCollisionSkin < AntiCollisionSkinFloor)
                Debug.LogWarning($"[Crane] SpreaderGrabber: 인스펙터 antiCollisionSkin {antiCollisionSkin} 가 하한 {AntiCollisionSkinFloor}(으)로 클램프됨 — 더 작게 쓰려면 코드의 AntiCollisionSkinFloor를 낮출 것.");
            antiCollisionSkin = Mathf.Max(antiCollisionSkin, AntiCollisionSkinFloor);

            if (passXZmargin > EmptyPassMarginCap)
                Debug.LogWarning($"[Crane] SpreaderGrabber: 인스펙터 passXZmargin {passXZmargin} 가 상한 {EmptyPassMarginCap}(으)로 클램프됨(빈 스프레더 통과방지) — 더 크게 쓰려면 코드의 EmptyPassMarginCap을 올릴 것.");

            Refresh();
        }

        void Start()
        {
            floorTopY = AIXRCrane.ContainerPhysicsStabilizer.FindFloorTopY(out _);
            CreateSpreaderPusher();   // 물리 충돌용 kinematic 콜라이더 부착(컨테이너 밀림/토플)
            // 이 줄이 Play 시 Console에 안 보이면 = SpreaderGrabber가 안 돌고 있는 것(컴파일/재생성 문제)
            if (debugLog)
                Debug.Log($"[Crane] SpreaderGrabber 활성 — 트위스트락 {twistlocks.Length}개, " +
                          $"집을수있는강체 {bodies.Count}개, 통과방지 {blockPassThrough}, 물리콜라이더 {spreaderCollider}");
        }

        void Refresh()
        {
            // 강체 전부를 담고 크레인 자식 제외는 쓰는 쪽에서 매 틱 건다.
            //   ★ 스캔 때 거르면 재스캔 전까지 통과방지가 그 컨테이너를 못 보고 관통한다.
            var all = FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude);
            bodies.Clear();
            foreach (var rb in all)
                if (rb != null) bodies.Add(rb);
            nextRefresh = Time.time + Mathf.Max(refreshInterval, MinRescanInterval);
        }

        // 트위스트락 콘들의 중심(없으면 부착점) — 잡기 기준점
        public Vector3 GrabPoint()
        {
            if (twistlocks != null && twistlocks.Length > 0)
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var t in twistlocks) if (t != null) { sum += t.position; n++; }
                if (n > 0) return sum / n;
            }
            return AttachPoint != null ? AttachPoint.position : transform.position;
        }

        /// <summary>콘 바닥 월드 Y(매단 컨테이너 제외). 원점≠콘끝(RTG 는 137mm 위) — CraneDemoRunner.SpreaderBottomY 와 같은 식.</summary>
        public float ConeBottomY()
        {
            Transform held = crane != null && crane.Attach != null ? crane.Attach.AttachedContainer : null;
            float y = float.MaxValue;
            if (twistlocks != null)
                foreach (var t in twistlocks)
                    if (t != null)
                        foreach (var r in t.GetComponentsInChildren<Renderer>())
                            if (held == null || !r.transform.IsChildOf(held)) y = Mathf.Min(y, r.bounds.min.y);
            if (y < float.MaxValue) return y;

            var sp = crane != null ? (crane.Spreader as Component) : null;   // 콘 미탐색 폴백 — 스프레더 최저 렌더러
            if (sp == null) return AttachPoint != null ? AttachPoint.position.y : transform.position.y;
            y = sp.transform.position.y;
            foreach (var r in sp.GetComponentsInChildren<Renderer>())
                if (held == null || !r.transform.IsChildOf(held)) y = Mathf.Min(y, r.bounds.min.y);
            return y;
        }

        /// <summary>콘 삽입 깊이(모델 단위).</summary>
        float InsertU => InsertDepthMeters * StsConfig.ModelScale;

        // 체결 인정 삽입 밴드 = InsertU × [최소, 최대] — 공중 체결과 옆면으로 파고든 상태를 거른다.
        const float InsertMinFrac = 0.25f, InsertMaxFrac = 2f;

        // 빈 스프레더가 c 의 코너캐스팅 위에 안착 정렬됐는지(gp=콘 평균): registerTolXZ 이내 동심 + gap 이 삽입 밴드 안.
        bool IsSeatedOver(Transform c, Vector3 gp, out float dXZ, out float gap)
        {
            dXZ = float.MaxValue; gap = float.MaxValue;
            if (!TryBounds(c, out Bounds b)) return false;
            float dx = gp.x - b.center.x, dz = gp.z - b.center.z;
            dXZ = Mathf.Sqrt(dx * dx + dz * dz);
            // gap = 콘 바닥 − 윗면(음수 = 박힘). 원점이 콘 끝이 아니므로 실측 기하로 잰다.
            gap = ConeBottomY() - b.max.y;
            bool centered = dXZ <= registerTolXZ;
            bool inserted = gap <= -InsertU * InsertMinFrac && gap >= -InsertU * InsertMaxFrac;
            return centered && inserted;
        }

        /// <summary>VR 컨트롤러 Y 버튼 — 비어 있으면 트위스트락 근처 컨테이너를 잡는다(이미 잡았으면 무시).</summary>
        public void Grab()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || attach.HasContainer) return;

            Vector3 gp = GrabPoint();
            var c = FindNearest(gp, out float dist);
            // QA S-PASS-6: 유효 반경 = min(grabRange, maxGrabRange).
            float effRange = Mathf.Min(grabRange, maxGrabRange);
            bool inRange = c != null && dist <= effRange;
            // 안착 평가 1회 — 게이트와 try 로그가 같은 값을 쓰게.
            bool seated = false; float segXZ = float.MaxValue, segGap = float.MaxValue;
            if (c != null) seated = IsSeatedOver(c, gp, out segXZ, out segGap);
            bool willGrab = inRange && (!requireSeatedRegistration || seated);
            if (debugLog)
                Debug.Log($"[Crane] 집기 시도 — 기준점(트위스트락) {gp}, 후보 {bodies.Count}개, " +
                          $"최근접 {(c != null ? $"{c.name} (거리 {dist:F3} / 유효범위 {effRange:F3})" : "없음")}");
            QaLog.Info("GRAB", "try",
                $"gp={QaLog.V(gp)} candidates={bodies.Count} nearest={(c != null ? c.name : "none")} " +
                $"dist={(c != null ? QaLog.F(dist) : "inf")} effRange={QaLog.F(effRange)} " +
                $"seated={seated} dXZ={QaLog.F(segXZ)} gap={QaLog.F(segGap)} grabbed={willGrab}");
            if (c == null) return;

            // 코너 안착 게이트 — 근접만으론 안 잠근다(옆면·공중·측면진입 거부).
            if (requireSeatedRegistration && !seated)
            {
                if (debugLog)
                    Debug.Log($"[Crane] 집기 거부 — 코너 미정렬: 중심오차 {segXZ:F3}m(허용 {registerTolXZ:F3}), " +
                              $"콘 바닥−윗면 {segGap:F4}u(체결 밴드 {-InsertU * InsertMaxFrac:F4}~{-InsertU * InsertMinFrac:F4}u). " +
                              $"스프레더를 컨테이너 중심 위에서 콘이 박힐 때까지 내리세요(통과방지가 삽입 {InsertDepthMeters * 1000f:F0}mm 에서 멈춥니다).");
                QaLog.Info("GRAB", "reject",
                    $"reason=unseated dXZ={QaLog.F(segXZ)} tolXZ={QaLog.F(registerTolXZ)} gap={QaLog.F(segGap)}");
                return;
            }

            bool hasBounds = TryBounds(c, out Bounds b);

            // 긴 축 길이로 텔레스코픽 20/40ft 자동 신축. 놓아도 유지.
            if ((telescope != null || rtgTele != null) && hasBounds)
            {
                float longSide = Mathf.Max(b.size.x, b.size.z);
                bool is40 = longSide > sizeThreshold;
                if (telescope != null) telescope.Set40(is40);
                if (rtgTele != null) rtgTele.SetSize(is40 ? RtgSpreaderTelescope.Size.Ft40 : RtgSpreaderTelescope.Size.Ft20);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 긴축 {longSide:F3}m → {(is40 ? "40ft" : "20ft")} 신축");
            }

            attach.Attach(c);   // 월드 자세 그대로 자식이 된다
            // 수평만 콘 중심에 맞춘다. 높이는 게이트가 이미 박힌 상태만 통과시키므로 건드리면 순간이동한다.
            if (hasBounds) c.position += new Vector3(gp.x - b.center.x, 0f, gp.z - b.center.z);

            // 하강 바닥 한계를 '컨테이너 밑면' 기준으로 — 스프레더가 아니라 컨테이너가 바닥(y=0)에 닿고 멈추게.
            if (spreaderHoist != null && TryBounds(c, out Bounds held))
            {
                // 축 목표 = Current + (floorTopY − 밑면Y) / WorldPerUnit — 축 해석(로컬/월드)과 무관.
                //   ★ 부모 position.y 기반 식은 worldVertical 크레인에서 바닥 아래까지 내려갔다.
                float floorMinY = spreaderHoist.Current + (floorTopY - held.min.y) / spreaderHoist.WorldPerUnit;
                spreaderHoist.SetFloorOffset(floorMinY - spreaderHoist.Min);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 밑면 기준 바닥 — 하강한계 +{floorMinY - spreaderHoist.Min:F3} (높이 {held.size.y:F3})");
                // QA S-PHYS-6: LowerLimit≤Max 유지(역전 시 호이스트 먹통).
                float lowerLimit = spreaderHoist.Min + spreaderHoist.FloorOffset;
                QaLog.Check("HOIST", "floorlimit", lowerLimit <= spreaderHoist.Max + 1e-4f,
                    $"heldHeight={QaLog.F(held.size.y)} floorMinY={QaLog.F(floorMinY)} offset={QaLog.F(spreaderHoist.FloorOffset)} " +
                    $"lowerLimit={QaLog.F(lowerLimit)} max={QaLog.F(spreaderHoist.Max)}");
            }

            if (lockAnim != null) lockAnim.SetLocked(true);

            // 운반 중 충돌 감지용 접점 릴레이 — 놓을 때 제거.
            if (c.GetComponent<LoadCollisionRelay>() == null)
                c.gameObject.AddComponent<LoadCollisionRelay>().owner = this;
        }

        /// <summary>VR 컨트롤러 X 버튼 — 잡고 있으면 놓는다(비어 있으면 무시).</summary>
        public void Release()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || !attach.HasContainer) return;

            var held = attach.AttachedContainer;   // Detach 전에 참조 확보 — 접점 릴레이 제거용
            if (held != null) SnapToYardCell(held);   // 아직 kinematic 인 동안 칸에 맞춘다 — 물리와 싸우지 않게
            attach.Detach();
            if (held != null)
            {
                var relay = held.GetComponent<LoadCollisionRelay>();   // 놓으면 더는 스프레더 충돌 대상 아님
                if (relay != null) Destroy(relay);
            }
            if (spreaderHoist != null) spreaderHoist.SetFloorOffset(0f);   // 빈 스프레더 바닥 한계 복원
            if (lockAnim != null) lockAnim.SetLocked(false);
            if (rtgTele != null) rtgTele.SetSize(RtgSpreaderTelescope.Size.Ft40);   // 빈 스프레더는 40ft 기준자세로 복원
            if (debugLog) Debug.Log("[Crane] 놓기(Detach)");
        }

        // 놓을 때 야드 칸 중심·격자 축으로 정렬(SSOT = YardGrid). 높이 불변, 야드 밖·목표 칸 점유 시 건너뜀.
        void SnapToYardCell(Transform c)
        {
            if (!TryBounds(c, out Bounds b)) return;
            float longSide = Mathf.Max(b.size.x, b.size.z);
            if (!YardGrid.TrySnapXZ(b.center, longSide, out Vector3 cell)) return;

            Quaternion rotWas = c.rotation;
            float yaw = YardGrid.SnapYawDeg(c.eulerAngles.y);
            c.rotation = Quaternion.Euler(c.eulerAngles.x, yaw, c.eulerAngles.z);
            // 회전 뒤 바운즈가 달라지므로 다시 재서 중심을 칸에 맞춘다(원점 ≠ 바운즈 중심인 화물 대비).
            if (!TryBounds(c, out Bounds nb)) { c.rotation = rotWas; return; }
            Vector3 delta = new Vector3(cell.x - nb.center.x, 0f, cell.z - nb.center.z);

            // 옮길 자리가 다른 컨테이너와 '교차'하면 원복. 0.98 은 닿음(적층)과 파고듦을 가르는 여유.
            var moved = new Bounds(nb.center + delta, nb.size * 0.98f);
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                Transform t = rb.transform;
                if (t == c || t.IsChildOf(c) || c.IsChildOf(t) || t.IsChildOf(transform)) continue;
                if (TryBounds(t, out Bounds ob) && moved.Intersects(ob))
                {
                    c.rotation = rotWas;
                    if (debugLog) Debug.Log($"[Crane] 야드 칸 정렬 건너뜀 — 목표 칸이 {t.name} 와(과) 겹침. 놓은 자리 그대로 둠.");
                    return;
                }
            }
            c.position += delta;
            if (debugLog)
                Debug.Log($"[Crane] 야드 칸 정렬 — 중심 x {nb.center.x:F4}→{cell.x:F4}, z {nb.center.z:F4}→{cell.z:F4}, yaw {yaw:F0}°");
        }

        /// <summary>SpreaderPusher 콜라이더 on/off — 자동 시나리오 하강 시 대상을 밀어 넘어뜨리지 않게 끈다.</summary>
        public void SetPusherActive(bool on)
        {
            if (pusherBox != null) pusherBox.enabled = on;
        }
        /// <summary>현재 푸셔 콜라이더 활성 여부(없으면 false).</summary>
        public bool IsPusherActive => pusherBox != null && pusherBox.enabled;

        /// <summary>스프레더(또는 잡은 컨테이너)가 받침(컨테이너/바닥) 위에 얹혀 멈췄는지 — 안착 표시용.</summary>
        public bool IsLanded { get; private set; }

        /// <summary>스프레더(빈 프레임/든 화물)가 컨테이너를 옆에서 친 충돌(Anti-Collision) — 경보용(3013 HO Snag).</summary>
        public bool LoadCollision { get; private set; }

        /// <summary>빈 스프레더가 후보 컨테이너 상단 코너캐스팅 위에 안착 정렬됨 — 지금 Y로 체결 가능(HUD 표시용).</summary>
        public bool ReadyToLock { get; private set; }

        /// <summary>빈 스프레더가 후보 근처지만 아직 안착 정렬 안 됨 — '모서리 정렬 필요'(HUD 표시용).</summary>
        public bool NearButUnseated { get; private set; }

        /// <summary>LoadCollisionRelay가 옆/아래 충돌 보고 — hold 동안 경보 유지.</summary>
        public void NotifyContact() => lastAntiCollTime = Time.time;

        // 클램프·푸셔는 축 이동(VRController)과 같은 FixedUpdate 박자 — 프레임 의존·터널링 방지.
        void FixedUpdate()
        {
            if (Time.time >= nextRefresh) Refresh();
            IsLanded = false;

            UpdatePusherBox();   // 텔레스코픽 상태(현재 반길이)에 맞춰 푸셔 박스 X를 동적 갱신

            var attach = crane != null ? crane.Attach : null;
            // Anti-Collision 은 접점(NotifyContact) 방식 — OverlapBox 는 일찍 뜨고 바닥을 놓쳐 폐기.
            LoadCollision = Time.time - lastAntiCollTime <= loadCollisionHold;
            // QA S-PASS-5: 경보 상태 변화만.
            if (QaLog.Enabled && LoadCollision != qaPrevLoadColl)
            {
                QaLog.Info("SIDE", "anticoll",
                    $"skin={QaLog.F(antiCollisionSkin)} minVert={QaLog.F(antiCollisionMinVertical)} " +
                    $"hold={QaLog.F(loadCollisionHold)} LoadCollision={LoadCollision}");
                qaPrevLoadColl = LoadCollision;
            }

            // HUD '잠금' 라인용 안착 정렬 상태 — 잡기와 같은 기준(FindNearest). 잡고 있으면 둘 다 false.
            ReadyToLock = false; NearButUnseated = false;
            if (requireSeatedRegistration && attach != null && !attach.HasContainer
                && twistlocks != null && twistlocks.Length > 0)
            {
                Vector3 gp = GrabPoint();
                var cand = FindNearest(gp, out _);
                if (cand != null)
                {
                    bool seated = IsSeatedOver(cand, gp, out _, out _);
                    ReadyToLock = seated;
                    NearButUnseated = !seated;
                }
            }

            if (!blockPassThrough || crane == null) return;

            var hoist = crane.Spreader;
            Transform ap = AttachPoint;
            if (attach == null || hoist == null || ap == null) return;

            // 하한 기준면·판정 중심: 들고 있으면 컨테이너 밑면·중심, 비었으면 콘 바닥·부착점.
            float refBottomY;
            Vector3 refCenter;
            Bounds heldB = default;
            bool holding = attach.HasContainer;
            if (holding)
            {
                if (!TryBounds(attach.AttachedContainer, out heldB)) return;
                refBottomY = heldB.min.y;
                refCenter = heldB.center;
            }
            else
            {
                // 부착점 기준이면 RTG 는 콘이 300mm 넘게 잠기고 STS 는 체결 자세가 안 나온다 → 콘 바닥 실측.
                refBottomY = ConeBottomY();
                refCenter = ap.position;   // 수평 판정(footprint 안인지)은 종전대로 부착점 XZ
            }

            // 받침 선정: 든 상태는 footprint 겹침 ≥ landingOverlapFrac, 빈 스프레더는 부착점이 footprint(±overMargin) 안.
            float overMargin = Mathf.Min(passXZmargin, EmptyPassMarginCap);
            float top = float.MinValue;
            float topMinY = 0f;   // 선택된(가장 높은) 컨테이너의 밑면 — 측면 침투 깊이 판정용
            bool over = false;
            float selOverlap = 0f;        // 선택된(받침) 컨테이너의 footprint 겹침 비율(적층 판정용·QA)
            float maxOverlapSeen = 0f;    // 든 상태에서 본 최대 겹침(적층 미달 시 '옆 나란히' 근거·QA)
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                if (rb.transform.IsChildOf(transform)) continue;   // 크레인 자식(스프레더/든 화물) 제외
                if (!TryBounds(rb.transform, out Bounds b)) continue;

                bool below;
                float overlapFrac = 0f;
                if (holding)
                {
                    float ox = Mathf.Min(heldB.max.x, b.max.x) - Mathf.Max(heldB.min.x, b.min.x);
                    float oz = Mathf.Min(heldB.max.z, b.max.z) - Mathf.Max(heldB.min.z, b.min.z);
                    float heldArea = heldB.size.x * heldB.size.z;
                    overlapFrac = (ox > 0f && oz > 0f) ? (ox * oz) / Mathf.Max(heldArea, 1e-6f) : 0f;
                    if (overlapFrac > maxOverlapSeen) maxOverlapSeen = overlapFrac;
                    below = overlapFrac >= landingOverlapFrac;
                }
                else
                {
                    below = refCenter.x >= b.min.x - overMargin && refCenter.x <= b.max.x + overMargin
                         && refCenter.z >= b.min.z - overMargin && refCenter.z <= b.max.z + overMargin;
                }
                if (!below) continue;
                if (b.max.y > top) { top = b.max.y; topMinY = b.min.y; over = true; selOverlap = overlapFrac; }
            }

            // 받침 없으면 바닥이 받침. 빈 스프레더는 컨테이너에 InsertU 만큼 박히게 내려간다.
            //   ★ 바닥에서는 삽입을 빼지 않는다 — 코너캐스팅 구멍은 컨테이너에만 있다.
            bool onFloor = !over;
            if (onFloor) top = floorTopY;
            float limit = top + topClearance - (holding || onFloor ? 0f : InsertU);
            // 옆면 깊숙이(높이 25% 이상 아래) 들어오면 측면 충돌 → 클램프 대신 푸셔로 민다. 바닥은 제외.
            bool sideHit = !onFloor && !holding && (top - refBottomY) > (top - topMinY) * SideHitDepthFrac;
            float corr = 0f;
            if (refBottomY < limit && !sideHit)
            {
                corr = limit - refBottomY;
                hoist.MoveTo(hoist.Current + corr);   // 로컬 Y ≈ 월드 Y, 받침 윗면(또는 바닥)에서 하강 정지
            }
            IsLanded = !sideHit && refBottomY <= limit + topClearance;   // 받침·바닥 위에 거의 얹힘 → 안착(Landing 센서)

            QaPassState(holding, over, sideHit, corr, limit, top, topMinY, refBottomY, selOverlap, maxOverlapSeen);
        }

        // QA 판정(그룹 B·C) — 엣지와 보정량 변화만 찍는다.
        void QaPassState(bool holding, bool over, bool sideHit, float corr,
                         float limit, float top, float topMinY, float refBottomY, float selOverlap, float maxOverlapSeen)
        {
            if (!QaLog.Enabled) return;

            // S-PASS-2 측면충돌: sideHit 상승 엣지.
            if (sideHit && !qaPrevSideHit)
            {
                float depth = top - refBottomY;
                float threshold = (top - topMinY) * SideHitDepthFrac;   // ≈ 컨테이너높이×0.25
                QaLog.Check("SIDE", "hit", depth > threshold,
                    $"holding={holding} depth={QaLog.F(depth)} threshold={QaLog.F(threshold)} sideHit=true clampApplied=false");
            }

            // S-PASS-3 적층 안착: IsLanded 상승 엣지에서만. ★ 바닥 안착은 겹침 0 이 정상이라 제외.
            bool landedStack = holding && IsLanded && top > floorTopY + 1e-4f;
            if (landedStack && !qaPrevLanded)
                QaLog.Check("LAND", "stack", selOverlap >= landingOverlapFrac,
                    $"holding=true overlapFrac={QaLog.F(selOverlap)} threshold={QaLog.F(landingOverlapFrac)} " +
                    $"top={QaLog.F(top)} refBottomY={QaLog.F(refBottomY)} landed=true");

            // S-PASS-1 통과방지: 윗면 근처 클램프 상승 엣지만. FAIL = 아래인데 corr≈0(관통).
            bool clampEmpty = over && !holding && !sideHit && refBottomY <= limit + 0.012f && refBottomY > limit - 0.06f;
            if (clampEmpty && !qaPrevClampEmpty)
                QaLog.Check("PASS", "clamp", corr > 1e-4f || refBottomY >= limit - 0.012f,
                    $"holding=false refBottomY={QaLog.F(refBottomY)} top={QaLog.F(top)} limit={QaLog.F(limit)} corr={QaLog.F(corr)}");

            // S-PASS-4 옆 나란히(적층 오인 금지): 든 채 over가 풀린 순간.
            if (holding && !over && qaPrevOver)
                QaLog.Info("LAND", "side",
                    $"holding=true over=false maxOverlap={QaLog.F(maxOverlapSeen)} threshold={QaLog.F(landingOverlapFrac)} below=false");

            // S-PHYS-2 클램프 보정량 수렴: corr가 변할 때만.
            if (corr > 0f && (qaCorrPrev < 0f || Mathf.Abs(corr - qaCorrPrev) > 1e-4f))
            {
                float prev = Mathf.Max(qaCorrPrev, 0f);
                QaLog.Info("PASS", "order",
                    $"moveOrder=0 clampOrder=50 corr={QaLog.F(corr)} corrPrev={QaLog.F(prev)} dCorr={QaLog.F(corr - prev)}");
            }
            qaCorrPrev = corr;
            qaPrevOver = over;
            qaPrevSideHit = sideHit;
            qaPrevLanded = landedStack;
            qaPrevClampEmpty = clampEmpty;
        }

        // 기준점에서 (상한 적용된) grabRange 안의 가장 가까운 컨테이너(콜라이더 있으면 그것 우선)
        Transform FindNearest(Vector3 gp, out float dist)
        {
            dist = float.MaxValue;
            float range = Mathf.Min(grabRange, maxGrabRange);

            // 콜라이더 우선 — range 안 크레인 외부 Rigidbody 중 최근접.
            Transform nearestCol = null;
            float nearestColD = float.MaxValue;
            var hits = Physics.OverlapSphere(gp, range);
            foreach (var h in hits)
            {
                var rb = h.attachedRigidbody;
                if (rb == null || rb.transform.IsChildOf(transform)) continue;
                float d = Vector3.Distance(gp, h.ClosestPoint(gp));   // 콜라이더 표면까지 거리
                if (d < nearestColD) { nearestColD = d; nearestCol = rb.transform; }
            }
            if (nearestCol != null) { dist = nearestColD; return nearestCol; }

            // 렌더러 바운즈 최근접(콜라이더를 못 맞춘 경우)
            Transform best = null;
            foreach (var rb in bodies)
            {
                if (rb == null || rb.transform.IsChildOf(transform)) continue;
                if (!TryBounds(rb.transform, out Bounds b)) continue;
                float d = Vector3.Distance(gp, b.ClosestPoint(gp));   // 바운즈 표면까지 거리
                if (d <= range && d < dist) { dist = d; best = rb.transform; }
            }
            return best;
        }

        // 푸셔 박스 X = 2×CurrentHalf — 스프레더가 줄면 박스도 줄어 옆 컨테이너를 안 민다.
        void UpdatePusherBox()
        {
            if (pusherBox == null || telescope == null) return;
            float want = Mathf.Max(telescope.CurrentHalf * 2f, 0.001f);
            var sz = pusherBox.size;
            if (!Mathf.Approximately(sz.x, want)) { sz.x = want; pusherBox.size = sz; }
        }

        // 스프레더에 kinematic 푸셔 콜라이더를 단다 — 별도 자식(강체 중첩 방지), 바닥은 부착점까지(콘 비움).
        void CreateSpreaderPusher()
        {
            if (!spreaderCollider) return;
            var sp = (crane != null ? crane.Spreader as Component : null)?.transform;
            if (sp == null || sp.Find("SpreaderPusher") != null) return;
            if (!TryLocalBounds(sp, out Bounds lb)) return;

            var go = new GameObject("SpreaderPusher");
            go.transform.SetParent(sp, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;   // 이동 빠를 때 관통 줄임

            // 박스: X=2×CurrentHalf, Z=스프레더 폭, Y=프레임 윗면~그랩 평면(콘 비움), 중심 XZ=0.
            float grabPlaneY = AttachPoint != null ? sp.InverseTransformPoint(AttachPoint.position).y : lb.min.y;
            float topY = lb.max.y;
            float botY = Mathf.Min(grabPlaneY, topY - 0.001f);
            float lenX = telescope != null ? telescope.CurrentHalf * 2f : lb.size.x;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (topY + botY) * 0.5f, 0f);
            box.size   = new Vector3(Mathf.Max(lenX, 0.001f), Mathf.Max(topY - botY, 0.001f), Mathf.Max(lb.size.z, 0.001f));
            // 컨테이너 contactOffset(0.001)과 맞춤 — 기본 0.01이면 Anti-Collision skin이 못 닿는다.
            box.contactOffset = 0.001f;
            pusherBox = box;

            go.AddComponent<LoadCollisionRelay>().owner = this;   // 빈 스프레더 접점 충돌 보고(옆/아래)

            if (debugLog) Debug.Log($"[Crane] SpreaderPusher 콜라이더 생성 — size {box.size:F3}, center {box.center:F3}");
        }

        // 자식 렌더러들의 스프레더 로컬 AABB(월드 AABB 8꼭짓점 변환).
        static bool TryLocalBounds(Transform root, out Bounds local)
        {
            local = default;
            var rends = root.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            bool has = false;
            foreach (var r in rends)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 lc = root.InverseTransformPoint(c);
                    if (!has) { local = new Bounds(lc, Vector3.zero); has = true; }
                    else local.Encapsulate(lc);
                }
            }
            return has;
        }

        // 자식 Renderer들의 월드 AABB(콜라이더 없음 대응)
        static bool TryBounds(Transform t, out Bounds b)
        {
            b = default;
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }
    }
}
