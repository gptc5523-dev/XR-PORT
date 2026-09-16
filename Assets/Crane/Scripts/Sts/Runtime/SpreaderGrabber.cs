using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 크레인 외부의 Rigidbody(=집을 수 있는 화물/컨테이너)를 트위스트락 콘 위치 기준으로 잡고/놓고,
    /// 빈 스프레더가 컨테이너 윗면을 통과하지 못하게 호이스트를 클램프한다(게임 동작).
    /// 컨테이너 식별은 특정 컴포넌트(ContainerInstance 등)에 의존하지 않는다 — Rigidbody가 달린
    /// 자유 강체면 절차적 스폰(VRTestMenu)이든 프리팹이든 모두 잡힌다. VR 컨트롤러가 ToggleGrab()을 호출한다.
    /// </summary>
    // 실행 순서 고정(중요): 통과방지 클램프는 VRController의 축 이동(호이스트 하강, 기본 order 0) '뒤'에
    //   돌아야 한 틱 침투를 즉시 복원한다. 둘 다 FixedUpdate이고 order가 같으면 순서가 불확정이라
    //   최악 1틱(0.02s) 어긋남이 생긴다. 계산상 그 진폭(공하 0.00225m, 운전실 시점 각크기 ~7.7~25.8 arcmin)이
    //   시각 인지 임계(1 arcmin)를 크게 초과해 떨림으로 보일 수 있으므로, order를 늦춰(50) 어긋남을 0으로 만든다.
    [DefaultExecutionOrder(50)]
    [AddComponentMenu("Container/STS Crane/Spreader Grabber")]
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
        // 옛 높이 밴드 knob 2개 삭제(2026-09-16) — 콘 트랜스폼 원점을 콘 끝으로 착각한 식이라 값 자체가 의미가 없었다.
        //   registerHoverTolY = 0.015u(실척 360mm 공중 체결 허용), seatDepthFrac = 0.45(실척 1.16m 침투 허용).
        //   이제 삽입 밴드는 insertMeters × [InsertMinFrac, InsertMaxFrac] 하나로 정해진다(실측 ConeBottomY 기준).

        [Tooltip("콘 돌출량을 못 재는 크레인(콘 미탐색)에서만 쓰는 폴백 삽입 깊이(실척 m). " +
                 "정상 크레인은 이 값을 무시하고 씬 기하에서 유도한다 — InsertDepthMeters 참고.")]
        [SerializeField] float insertFallbackMeters = 0.04f;
        // 옛 knob insertMeters = 0.04f 는 삭제(2026-09-16). 근거가 "ISO 1161 상면 홀 상판 두께 ≈16mm + 여유" 였는데 1차 출처가 없다:
        //   · ISO 1161:2016 은 1984판 Annex A(외형 치수 '예시, 비의무')를 삭제했다 ⇒ 현행판에 코너피팅 외형·상판 두께 규정이 아예 없다.
        //   · 채택 수치 문서(~/Container/문서/컨테이너_통합.md Part 2)에도 상판 두께가 없다. 있는 것은 개구 63.5 × 124.5(스타디움형)와
        //     캐스팅 외형 178×162×118(ISO 아님 · CIMC 제조사값, 우리 모델은 높이 113.5)뿐이다.
        // ★ 다음 세션 주의 — 상판 두께를 벤더 카탈로그·블로그 같은 2차 출처로 채워 삽입 상한을 만들지 말 것.
        //   Part 2 에 1차 출처로 들어온 뒤에만 쓴다(컨테이너 1차자료는 2026-08-14 전량 삭제됐다).

        float protrusionM = -1f;   // 콘 돌출량 실측 캐시(실척 m, 음수 = 아직 안 쟀음). 렌더러가 준비된 첫 사용 때 1회.

        /// <summary>콘 삽입 깊이(실척 m) — 상수가 아니라 씬 기하에서 유도한다(오너 2026-09-16 "수식을 사용해서 수정하라고 했는데").
        /// 실물 안착 자세는 <b>스프레더 구조 밑면이 컨테이너 최상면에 닿는</b> 깊이다. 그 최상면은 지붕이 아니라 상단 코너캐스팅 상면이다
        /// — ISO 1496-1 5.2 가 상단 코너피팅을 위로 6mm 이상 돌출하도록 의무화하므로 컨테이너에서 가장 높은 면이 늘 캐스팅 상면이고,
        /// 그래서 렌더러 바운즈 max.y 가 바로 그 면이다(잡기·클램프가 쓰는 기준면이 맞다는 근거).
        ///   ⇒ 삽입 깊이 = 콘이 구조 밑면보다 아래로 나온 길이 = <see cref="MeasureProtrusionM"/> 콘 돌출량. 튜닝 상수가 없다.
        ///   ⇒ 불변식: 이 깊이까지만 내려가면 스프레더 어느 부재도 컨테이너 최상면 아래로 안 들어간다.
        /// 2026-09-16 실측(StsGrabProbe, 두 기준 일치): STS 24mm(Beam_Flange_1) · RTG 56mm(EndBeam_F_Body).
        ///   옛 고정 40mm 는 STS 를 16mm 파묻고(구조가 컨테이너 안) RTG 를 16mm 띄웠다(콘이 덜 박힘 = 오너 보고).
        /// 콘을 못 찾으면 <see cref="insertFallbackMeters"/>. ★ 호출자는 이 값을 필드로 캐시하지 말 것 — 프로퍼티로 매번 읽는다.</summary>
        public float InsertDepthMeters
        {
            get
            {
                if (protrusionM < 0f) protrusionM = MeasureProtrusionM();
                return protrusionM > 0f ? protrusionM : insertFallbackMeters;
            }
        }

        // 콘 돌출량(실척 m) = (콘 아닌 스프레더 최저 렌더러 Y) − (콘 바닥 Y). StsGrabProbe.ProtrusionM 과 같은 식 —
        //   배치 검사와 런타임이 같은 수를 쓰게 한다. 0 이하면 구조가 콘보다 낮아 안착 깊이를 정의할 수 없다 → 폴백.
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

        // QA 콘솔 판정용 상태 — FixedUpdate는 매 물리틱 도므로 '상태 변화(엣지)'와 '보정량 수렴'만 찍어 폭주 방지.
        bool qaPrevOver, qaPrevSideHit, qaPrevLoadColl, qaPrevLanded, qaPrevClampEmpty;
        float qaCorrPrev = -1f;

        // 화물은 씬 배치라 런타임에 거의 안 변함 → 무거운 전수 스캔(FindObjectsByType)의 최소 간격.
        // 직렬화된 refreshInterval(기존 0.5s)이 작아도 이 값 이하로는 안 내려가 스캔/GC 빈도를 낮춘다.
        // (잡힌/놓인 상태는 매 프레임 IsChildOf로 거르므로 목록을 자주 다시 만들 필요가 없다.)
        const float MinRescanInterval = 3f;

        // 침묵 클램프를 가시화한 명명 상수(값은 종전 매직넘버 그대로 — 기본값 거동 불변):
        //   · AntiCollisionSkinFloor: antiCollisionSkin의 신뢰 가능한 하한. 옛 boundary 값(0.002)으로 남은
        //     씬 인스턴스도 이 값 이상으로 올려 '운 좋을 때만' 근접이 잡히던 문제를 막는다.
        //   · EmptyPassMarginCap: 빈 스프레더 통과방지 footprint 여유의 상한. passXZmargin(0.1)은 1/24
        //     미니어처엔 커서 옆 컨테이너에 멀리서부터 걸리므로 이 값으로 제한한다.
        //   · SideHitDepthFrac: 빈 스프레더가 컨테이너 옆면 '깊숙이' 들어온 측면충돌 판정 깊이(윗면 기준 높이의 비율).
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
                    || t.name.StartsWith("Spreader_Twistlock_"))
                    list.Add(t);
            }
            twistlocks = list.ToArray();

            // 직렬화된 기존 씬 인스턴스가 옛 boundary 값(0.002)으로 남아 있어도 신뢰 가능한 최소로 올린다
            //   — 밀려난 컨테이너가 gap(≈0.002) 떨어져도 근접이 잡히게(이하면 '운 좋을 때만' 발생).
            // 침묵 클램프 가시화: 인스펙터 값이 하한/상한에 걸리면 1회 경고해, 디자이너가 "왜 안 먹지"를 모르고 헤매지 않게.
            if (antiCollisionSkin < AntiCollisionSkinFloor)
                Debug.LogWarning($"[Crane] SpreaderGrabber: 인스펙터 antiCollisionSkin {antiCollisionSkin} 가 하한 {AntiCollisionSkinFloor}(으)로 클램프됨 — 더 작게 쓰려면 코드의 AntiCollisionSkinFloor를 낮출 것.");
            antiCollisionSkin = Mathf.Max(antiCollisionSkin, AntiCollisionSkinFloor);

            if (passXZmargin > EmptyPassMarginCap)
                Debug.LogWarning($"[Crane] SpreaderGrabber: 인스펙터 passXZmargin {passXZmargin} 가 상한 {EmptyPassMarginCap}(으)로 클램프됨(빈 스프레더 통과방지) — 더 크게 쓰려면 코드의 EmptyPassMarginCap을 올릴 것.");

            Refresh();
        }

        void Start()
        {
            floorTopY = ContainerProject.ContainerPhysicsStabilizer.FindFloorTopY(out _);
            CreateSpreaderPusher();   // 물리 충돌용 kinematic 콜라이더 부착(컨테이너 밀림/토플)
            // 이 줄이 Play 시 Console에 안 보이면 = SpreaderGrabber가 안 돌고 있는 것(컴파일/재생성 문제)
            if (debugLog)
                Debug.Log($"[Crane] SpreaderGrabber 활성 — 트위스트락 {twistlocks.Length}개, " +
                          $"집을수있는강체 {bodies.Count}개, 통과방지 {blockPassThrough}, 물리콜라이더 {spreaderCollider}");
        }

        void Refresh()
        {
            // 강체 전부를 담는다 — '크레인 자식 제외'는 스캔 때가 아니라 쓰는 쪽(FindNearest·통과방지 루프)에서 매 틱 건다.
            //   ★ 스캔 때 걸러내면 그 순간 매달려 있던 컨테이너가 목록에서 빠지고, 놓은 뒤에도 다음 재스캔(MinRescanInterval 3초)
            //     까지 안 돌아온다 → 통과방지가 그 컨테이너를 못 보고 빈 스프레더가 그대로 관통한다.
            //     2026-09-16 StsGrabProbe 실측: 과하강 케이스 8건 중 3건이 삽입 40mm 에서 안 멈추고 200mm 까지 내려갔고,
            //     그 구간엔 PASS/clamp 엣지가 아예 없었다(over=false). 재생·시연이 방금 놓은 컨테이너에도 같은 구멍이 생긴다.
            // 버퍼(bodies)를 비우고 다시 채워 매 갱신 새 List/배열 할당을 피한다(주기적 GC 절감).
            var all = FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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

        /// <summary>콘 바닥(스프레더 최저 기하)의 월드 Y — 매단 컨테이너 렌더러는 뺀다.
        /// 트랜스폼 원점은 콘 끝이 아니다(2026-09-16 실측: 절차 STS 는 원점=콘끝이지만 FBX RTG 는 원점이 콘끝보다 137mm 위).
        /// 그래서 잡기·통과방지 기준은 원점이 아니라 이 실측값을 쓴다 — CraneDemoRunner.SpreaderBottomY 와 같은 식.</summary>
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

        // 체결 인정 삽입 밴드 = InsertU × [최소, 최대]. 클램프가 정확히 InsertU 에 세워 주므로 이 밴드는 여유값이다
        //   — 위(공중 체결)와 아래(옆면으로 깊숙이 파고든 상태)를 모두 거른다.
        const float InsertMinFrac = 0.25f, InsertMaxFrac = 2f;

        // 빈 스프레더가 컨테이너 c의 상단 코너캐스팅 위에 '안착 정렬'됐는지. gp=트위스트락 콘 평균(잡기 기준점).
        //   · 수평(dXZ): gp가 컨테이너 중심 XZ에서 registerTolXZ 이내 → 동심. 집기 시 텔레스코픽이 사이즈를
        //     자동 정합하므로(20/40ft 혼합 야드), 동심이면 콘 4개가 4개 코너캐스팅 위에 놓인다.
        //   · 높이(gap=콘 바닥Y−윗면Y, ConeBottomY 실측): 콘이 insertMeters×[InsertMinFrac, InsertMaxFrac] 만큼
        //     '박혀 있을 때'만 체결. gap>0(공중)·너무 깊음(옆면 진입) 모두 거부. 통과방지가 정확히 insertMeters 에서 세워 준다.
        //   사이즈를 별도 검사하지 않는 이유: 자동 신축이 정합하므로 동심+안착이 곧 코너 정렬과 동치.
        bool IsSeatedOver(Transform c, Vector3 gp, out float dXZ, out float gap)
        {
            dXZ = float.MaxValue; gap = float.MaxValue;
            if (!TryBounds(c, out Bounds b)) return false;
            float dx = gp.x - b.center.x, dz = gp.z - b.center.z;
            dXZ = Mathf.Sqrt(dx * dx + dz * dz);
            // gap = 콘 바닥 − 윗면(음수 = 그만큼 박힘). 실측 기하로 재야 한다 — 옛 코드는 gp.y(콘 트랜스폼 원점 평균)를
            //   콘 끝으로 썼고, 그래서 원점이 콘끝보다 137mm 위인 RTG 는 콘이 135mm 파묻힌 채, STS 는 0mm(닿기만) 체결됐다.
            //   옛 밴드: gap ≤ registerHoverTolY(0.015u=실척 360mm) ~ ≥ −높이×seatDepthFrac(0.45→실척 1.16m).
            //   공중 360mm 에서도 잠겼던 원인(오너 2026-09-16 "락 거는 부분이 컨테이너 안으로 안 들어가").
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
            // QA S-PASS-6: 유효 잡기 반경 = min(grabRange, maxGrabRange). dist가 이 안이고 코너 안착이면 grabbed=true.
            float effRange = Mathf.Min(grabRange, maxGrabRange);
            bool inRange = c != null && dist <= effRange;
            // 코너 안착 평가(후보 있을 때만) — 동심 정렬 + 윗면 높이 안착. 게이트와 try 로그가 같은 값을 쓰게 1회만 계산.
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

            // 코너 안착 게이트 — 근접만으론 안 잠긴다. 트위스트락이 컨테이너 상단 코너캐스팅 위에
            //   '동심 정렬(중심 ±registerTolXZ) + 윗면 높이 안착'했을 때만 체결한다(자동 신축으로 사이즈가
            //   맞으므로 이 조건이 곧 콘 4개가 코너캐스팅 위에 놓임과 동치). 옆면 근처·공중·측면진입은 거부.
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

            // 잡은 컨테이너 긴 축 길이로 스프레더 텔레스코픽 자동 신축(20/40ft). 놓아도 유지.
            if ((telescope != null || rtgTele != null) && hasBounds)
            {
                float longSide = Mathf.Max(b.size.x, b.size.z);
                bool is40 = longSide > sizeThreshold;
                if (telescope != null) telescope.Set40(is40);
                if (rtgTele != null) rtgTele.SetSize(is40 ? RtgSpreaderTelescope.Size.Ft40 : RtgSpreaderTelescope.Size.Ft20);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 긴축 {longSide:F3}m → {(is40 ? "40ft" : "20ft")} 신축");
            }

            attach.Attach(c);   // 월드 자세 그대로 자식이 된다
            // 수평은 트위스트락 중심에 맞춘다(플리퍼·가이드 역할 — 안착 게이트 통과면 오차 ≤ registerTolXZ). 회전은 그대로.
            //   높이는 손대지 않는다 — 게이트가 '콘이 박힌 상태'만 통과시키므로 옮길 이유가 없고, 옮기면 컨테이너가 순간이동한다.
            //     (옛 코드는 게이트를 끈 근접 잡기에서 윗면을 콘 '원점' 높이로 올렸다 — 원점≠콘끝인 RTG 에서 135mm 파묻힘의 원인.)
            //   ※ 옛 코드는 부착점 '로컬' y 에서 월드 거리를 뺐다 — FBX RTG 부착점은 축변환·스케일 4.1667 이라
            //     0.054u × 4.1667 = 0.225u 가 수평으로 튀었다(StsGrabProbe 실측 0.2249u).
            if (hasBounds) c.position += new Vector3(gp.x - b.center.x, 0f, gp.z - b.center.z);

            // 하강 바닥 한계를 '컨테이너 밑면' 기준으로 — 스프레더가 아니라 컨테이너가 바닥(y=0)에 닿고 멈추게.
            if (spreaderHoist != null && TryBounds(c, out Bounds held))
            {
                // 든 컨테이너 밑면이 바닥에 닿는 '축 값' — 축 해석과 무관하게 성립하는 한 식으로 구한다.
                //   밑면을 바닥까지 내리려면 스프레더를 월드로 (floorTopY − 밑면Y) 만큼 움직여야 하고,
                //   월드 이동 → 축 이동 환산이 WorldPerUnit 이다. 그래서 축 목표 = Current + 그 값 / WorldPerUnit.
                //   ★ 옛 식 `(sp.position.y − 밑면Y) − 부모.position.y` 는 축을 로컬 Y 로 읽는 크레인에서만 맞았다.
                //     SpreaderHoist.ReadAxis 는 worldVertical 이면 '월드' Y 를 읽으므로(FBX RTG), 그 크레인은
                //     부모(트롤리) 높이만큼 하강 한계가 어긋나 컨테이너가 바닥 아래까지 내려갈 수 있었다.
                //   검산 — 월드축: Current = sp.position.y · WorldPerUnit = 1 ⇒ sp.position.y − 밑면Y(= 옛 dropToBottom).
                //          로컬축: Current = localPosition.y                  ⇒ localY − 밑면Y(= 옛 식과 동일).
                float floorMinY = spreaderHoist.Current + (floorTopY - held.min.y) / spreaderHoist.WorldPerUnit;
                spreaderHoist.SetFloorOffset(floorMinY - spreaderHoist.Min);
                if (debugLog) Debug.Log($"[Crane] 컨테이너 밑면 기준 바닥 — 하강한계 +{floorMinY - spreaderHoist.Min:F3} (높이 {held.size.y:F3})");
                // QA S-PHYS-6: 하강 바닥한계 오프셋이 LowerLimit≤Max를 유지하는지(역전 시 호이스트 먹통).
                float lowerLimit = spreaderHoist.Min + spreaderHoist.FloorOffset;
                QaLog.Check("HOIST", "floorlimit", lowerLimit <= spreaderHoist.Max + 1e-4f,
                    $"heldHeight={QaLog.F(held.size.y)} floorMinY={QaLog.F(floorMinY)} offset={QaLog.F(spreaderHoist.FloorOffset)} " +
                    $"lowerLimit={QaLog.F(lowerLimit)} max={QaLog.F(spreaderHoist.Max)}");
            }

            if (lockAnim != null) lockAnim.SetLocked(true);

            // 든 컨테이너에 접점 릴레이 부착 — 운반 중 다른 컨테이너에 부딪히는 순간(바닥 포함)을 잡는다. 놓을 때 제거.
            if (c.GetComponent<LoadCollisionRelay>() == null)
                c.gameObject.AddComponent<LoadCollisionRelay>().owner = this;
        }

        /// <summary>VR 컨트롤러 X 버튼 — 잡고 있으면 놓는다(비어 있으면 무시).</summary>
        public void Release()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach == null || !attach.HasContainer) return;

            var held = attach.AttachedContainer;   // Detach 전에 참조 확보 — 접점 릴레이 제거용
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

        /// <summary>잡고 있으면 놓고, 아니면 잡는다(토글). 단일 버튼 매핑용 — 현재 컨트롤러는 Grab/Release를 직접 호출.</summary>
        public void ToggleGrab()
        {
            var attach = crane != null ? crane.Attach : null;
            if (attach != null && attach.HasContainer) Release();
            else Grab();
        }

        /// <summary>스프레더 물리 푸셔(SpreaderPusher) 콜라이더 on/off. 자동 시나리오가 잡으러 하강할 때
        /// 이 kinematic 박스가 대상 컨테이너를 밀어 토플시키므로 끈다(집기 인식·통과방지 클램프엔 무관). 끝나면 원복.</summary>
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

        /// <summary>LoadCollisionRelay(접점·법선)가 옆/아래 충돌을 보고 — 닿는 순간 lastAntiCollTime 갱신(hold 동안 경보 유지).</summary>
        public void NotifyContact() => lastAntiCollTime = Time.time;

        // 통과방지 클램프(hoist.MoveTo)와 푸셔 콜라이더 크기 변경은 kinematic 화물·콜라이더를 다루는
        //   물리 동작이므로 FixedUpdate(PhysX 고정틱)에서 처리한다 — VRController의 축 이동도 FixedUpdate라
        //   같은 박자에서 이동→클램프가 이어져 프레임률 의존·터널링·한 프레임 어긋남을 막는다.
        void FixedUpdate()
        {
            if (Time.time >= nextRefresh) Refresh();
            IsLanded = false;

            UpdatePusherBox();   // 텔레스코픽 상태(현재 반길이)에 맞춰 푸셔 박스 X를 동적 갱신

            var attach = crane != null ? crane.Attach : null;
            // Anti-Collision: LoadCollisionRelay(접점·법선)가 옆/아래 충돌을 NotifyContact로 보고 → lastAntiCollTime 갱신.
            //   기하 OverlapBox 방식은 '닿기 전 일찍 뜸 + 바닥 누락'으로 폐기, 접점 방식으로 교체.
            LoadCollision = Time.time - lastAntiCollTime <= loadCollisionHold;
            // QA S-PASS-5: 측면충돌 경보 상태 변화만(매틱 폭주 방지).
            if (QaLog.Enabled && LoadCollision != qaPrevLoadColl)
            {
                QaLog.Info("SIDE", "anticoll",
                    $"skin={QaLog.F(antiCollisionSkin)} minVert={QaLog.F(antiCollisionMinVertical)} " +
                    $"hold={QaLog.F(loadCollisionHold)} LoadCollision={LoadCollision}");
                qaPrevLoadColl = LoadCollision;
            }

            // 빈 스프레더 안착 정렬 상태 갱신(HUD '잠금' 라인: 정렬됨▸체결 / 모서리 정렬 필요).
            //   잡고 있으면 둘 다 false. 후보 1개를 잡기와 동일 기준(FindNearest)으로 평가해 표시.
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

            // 아래로 내려가면 안 되는 기준면(refBottomY)과, '바로 위에 있는지' 판정에 쓰는 중심(refCenter)을 정한다.
            //  - 컨테이너를 들고 있으면: 들고 있는 컨테이너의 '밑면'과 그 XZ 중심
            //    → 바로 밑에 깔린 컨테이너 윗면 위에 밑면이 얹히고 멈춤(깔아뭉개기/땅속 관통 방지).
            //  - 빈 스프레더면: 부착점(점) — 기존 통과 방지 동작.
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
                // 빈 스프레더 기준 = 콘 바닥(실측). 부착점을 기준으로 막으면 RTG 는 부착점이 콘끝보다 192mm 위라
                //   콘이 컨테이너 안으로 300mm 넘게 잠기고(실측 +135~200mm), STS 는 콘이 윗면에 닿기만 해 체결 자세가 안 나온다.
                refBottomY = ConeBottomY();
                refCenter = ap.position;   // 수평 판정(footprint 안인지)은 종전대로 부착점 XZ
            }

            // 아래에 깔린 컨테이너(받침)를 고른다 — Landing & Position Sensor. bodies는 크레인 자식(스프레더/든 화물) 제외.
            //   · 든 상태: 든 컨테이너 footprint와 'landingOverlapFrac 이상 겹치면' 적층 대상. 중심점 일치가 아니라
            //     겹침 비율 판정이라, 살짝 어긋나게 내려놔도 윗면에서 멈춰 아래 것을 바닥으로 밀어넣지 않는다.
            //     (옆에 나란히=겹침 ~0이라 적층으로 오인 안 함 → 옆 내려놓기 막힘 없음)
            //   · 빈 스프레더: 부착점(점)이 footprint(±overMargin) 안이면 '바로 위'. passXZmargin(0.1m)은 미니어처엔
            //     커서 0.03m로 상한(옆 컨테이너에 멀리서부터 걸리던 것 방지).
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

            // 받침 컨테이너가 없으면 '바닥'이 받침이다. 옛 코드는 그 경우 limit 를 0 으로 두고 클램프를 `over` 안에서만 걸어서
            //   빈 땅 위에서는 하한이 아예 없었다 — 든 채로 내리면 데크를 뚫고 내려갔다(오너 2026-09-16 스크린샷).
            //   바닥 높이는 ContainerPhysicsStabilizer.FindFloorTopY 가 SSOT(VirtualFloor 윗면 / 없으면 데크 y=0) — Start 에서 1회 캐시.
            // 빈 스프레더는 콘이 InsertDepthMeters 만큼 박히는 데까지 내려간다(그 자리가 체결 자세) — 든 상태는 밑면이 윗면에 얹힌다.
            //   ★ 바닥 받침에서는 삽입을 빼지 않는다 — 콘이 박힐 코너캐스팅 구멍은 컨테이너에만 있고, 데크 아래로 내려갈 이유가 없다.
            bool onFloor = !over;
            if (onFloor) top = floorTopY;
            float limit = top + topClearance - (holding || onFloor ? 0f : InsertU);
            // 빈 스프레더가 컨테이너 '옆면 깊숙이'(윗면보다 높이 25% 이상 아래) 들어온 경우 = 측면 충돌 → 클램프 대신 푸셔로 밀기.
            //   바닥은 옆면이 없으므로(무한 두께) 이 판정에서 제외한다.
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

        // QA 콘솔 판정(그룹 B·C) — 상태 변화(엣지)와 보정량 수렴만 찍어 매 물리틱 폭주를 막는다.
        void QaPassState(bool holding, bool over, bool sideHit, float corr,
                         float limit, float top, float topMinY, float refBottomY, float selOverlap, float maxOverlapSeen)
        {
            if (!QaLog.Enabled) return;

            // S-PASS-2 측면충돌: sideHit 상승 엣지(빈 스프레더가 옆면 깊숙이 진입한 순간).
            if (sideHit && !qaPrevSideHit)
            {
                float depth = top - refBottomY;
                float threshold = (top - topMinY) * SideHitDepthFrac;   // ≈ 컨테이너높이×0.25
                QaLog.Check("SIDE", "hit", depth > threshold,
                    $"holding={holding} depth={QaLog.F(depth)} threshold={QaLog.F(threshold)} sideHit=true clampApplied=false");
            }

            // S-PASS-3 적층 안착: '실제로 얹힌 순간'(IsLanded 상승 엣지)에만 판정.
            //   주의: 공중에서 footprint만 겹친 시점(over=true, 높이 높음)이 아니라, 든 컨테이너 밑면이
            //   받침 윗면에 닿아 멈춘 순간을 본다(이전 버전은 공중 over-엣지에서 판정해 오탐 FAIL이 났음).
            bool landedStack = holding && IsLanded;
            if (landedStack && !qaPrevLanded)
                QaLog.Check("LAND", "stack", selOverlap >= landingOverlapFrac,
                    $"holding=true overlapFrac={QaLog.F(selOverlap)} threshold={QaLog.F(landingOverlapFrac)} " +
                    $"top={QaLog.F(top)} refBottomY={QaLog.F(refBottomY)} landed=true");

            // S-PASS-1 빈 스프레더 통과방지: 윗면 근처에 클램프되어 멈춘 순간(상승 엣지).
            //   refBottomY가 윗면(limit) 근처에 머물면(아래로 안 뚫음) PASS. 공중(refBottomY≫limit)은 제외.
            //   판정: top 아래로 내려가면 클램프가 즉시 되밀고 있어야(corr>0) 통과방지 정상. top 근처/위면 그대로 OK.
            //   FAIL = top 아래인데 보정이 없음(corr≈0) = 클램프 미작동·관통. 프레임 끊김의 1틱 과하강은 corr>0라 통과.
            bool clampEmpty = over && !holding && !sideHit && refBottomY <= limit + 0.012f && refBottomY > limit - 0.06f;
            if (clampEmpty && !qaPrevClampEmpty)
                QaLog.Check("PASS", "clamp", corr > 1e-4f || refBottomY >= limit - 0.012f,
                    $"holding=false refBottomY={QaLog.F(refBottomY)} top={QaLog.F(top)} limit={QaLog.F(limit)} corr={QaLog.F(corr)}");

            // S-PASS-4 옆 나란히(적층 오인 금지): 든 채 over가 풀린 순간(겹침<0.4라 받침 아님).
            if (holding && !over && qaPrevOver)
                QaLog.Info("LAND", "side",
                    $"holding=true over=false maxOverlap={QaLog.F(maxOverlapSeen)} threshold={QaLog.F(landingOverlapFrac)} below=false");

            // S-PHYS-2 통과방지 클램프 보정량 수렴: 클램프 중 corr가 의미있게 변할 때만(정착하면 조용).
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

            // 콜라이더 기반 우선 — range 안의 콜라이더 중 크레인 외부 Rigidbody가 달린 것들 중 '가장 가까운' 1개
            //   (예전: 첫 hit을 그대로 잡아 원치 않는 컨테이너가 짚히던 문제 → 최근접으로 선택)
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

        // 텔레스코픽 현재 길이에 맞춰 푸셔 박스 X = 2×current로 갱신 — 스프레더가 줄면 박스도 줄어 옆 컨테이너를 안 민다
        //   (작은 화물 운반 중 큰 컨테이너 밀림 방지). current=컨테이너 반길이라 박스 길이=현재 화물 길이로 정확.
        void UpdatePusherBox()
        {
            if (pusherBox == null || telescope == null) return;
            float want = Mathf.Max(telescope.CurrentHalf * 2f, 0.001f);
            var sz = pusherBox.size;
            if (!Mathf.Approximately(sz.x, want)) { sz.x = want; pusherBox.size = sz; }
        }

        // 스프레더 프레임에 kinematic 콜라이더(SpreaderPusher)를 달아 컨테이너(동적 강체)를 물리로 밀어/넘어뜨린다.
        //   ▸ 별도 자식에 둬서 잡은 컨테이너(AttachPoint 자식)와 rigidbody가 중첩(nested)되지 않게 한다.
        //   ▸ 박스 바닥은 부착점(그랩 평면)까지만 — 그 아래 트위스트락 콘은 비워 안착/잡기를 방해하지 않는다.
        //   ▸ 크레인 다른 부재엔 콜라이더가 없어 자기 구조물과는 안 부딪힌다. 컨테이너 콜라이더하고만 충돌.
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

            // 박스 치수(정확 산식, 추정 없음):
            //   · X(길이) = 2×current. 텔레스코프 반길이 current는 컨테이너 반길이와 정확히 일치(half20=0.126=L20ft/48,
            //     half40=0.254=L40ft/48)하고 트위스트락이 코너캐스팅(±current)에 물리므로, 박스 길이=컨테이너 길이.
            //   · Z(폭) = 실측 스프레더 폭(lb.size.z) — 텔레스코프와 무관(폭 불변).
            //   · Y = 프레임 윗면(topY) ~ 그랩 평면(botY). 콘이 있는 그 아래는 비워 잡기/안착 방해 없음.
            //   · 중심 X·Z = 0 (스프레더는 ±current/±폭 대칭 → 정확히 0).
            float grabPlaneY = AttachPoint != null ? sp.InverseTransformPoint(AttachPoint.position).y : lb.min.y;
            float topY = lb.max.y;
            float botY = Mathf.Min(grabPlaneY, topY - 0.001f);
            float lenX = telescope != null ? telescope.CurrentHalf * 2f : lb.size.x;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (topY + botY) * 0.5f, 0f);
            box.size   = new Vector3(Mathf.Max(lenX, 0.001f), Mathf.Max(topY - botY, 0.001f), Mathf.Max(lb.size.z, 0.001f));
            // 컨테이너 contactOffset(0.001)와 맞춤 — 기본값 0.01이면 밀린 컨테이너가 ~0.011 떨어져 Anti-Collision skin이 못 닿는다(빈 스프레더 경보 누락).
            box.contactOffset = 0.001f;
            pusherBox = box;

            go.AddComponent<LoadCollisionRelay>().owner = this;   // 빈 스프레더 접점 충돌 보고(옆/아래)

            if (debugLog) Debug.Log($"[Crane] SpreaderPusher 콜라이더 생성 — size {box.size:F3}, center {box.center:F3}");
        }

        // 스프레더 자식 렌더러들의 '스프레더 로컬' AABB(콜라이더 박스 산정용). 월드 AABB 8꼭짓점을 로컬로 변환·포함.
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
