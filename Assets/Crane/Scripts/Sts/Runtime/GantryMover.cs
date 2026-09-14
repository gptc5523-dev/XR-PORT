using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 갠트리 주행 — 크레인 루트를 안벽 방향(Z축)으로 슬라이딩.
    /// 트롤리(X)·호이스트(Y)와 동일하게 IAxisMover로 추상화 → 상위(VR/Operator)는 어떤 축인지 모르고 일관되게 호출.
    ///
    /// RTG는 고무 타이어라 <see cref="RtgBogieSteering"/>가 보기를 90° 꺾으면 주행축이 로컬 Z→X로 바뀐다(레인 이동).
    /// STS는 레일 위라 Z 고정 — 기본값이 Z이므로 STS 동작은 그대로다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Gantry Mover")]
    [DisallowMultipleComponent]
    public sealed class GantryMover : AxisMoverBase
    {
        /// <summary>주행 축(로컬). Z=기본 주행(STS 안벽 / RTG 스택 길이방향), X=RTG 레인 이동(스티어링 90°).
        /// ※ Z를 0번으로 두는 건 의도적 — 기존 씬/프리팹의 GantryMover엔 이 필드가 직렬화돼 있지 않아
        ///   기본값으로 떨어지는데, X가 0번이면 옛 STS 크레인이 전부 레인 모드(범위 0~0)로 깨어나 주행이 멎는다.</summary>
        public enum TravelAxis { Z, X }

        [Header("주행 범위 (로컬 Z, 미터)")]
        [SerializeField] float min = -1f;
        [SerializeField] float max =  1f;

        [Header("레인 이동 범위 (로컬 X, 미터) — RTG 스티어링 90° 전용")]
        [Tooltip("STS는 미사용(레일 고정). RTG만 RtgBogieSteering이 90°로 꺾었을 때 이 범위로 주행한다.")]
        [SerializeField] float minX = 0f;
        [SerializeField] float maxX = 0f;

        [Tooltip("현재 주행축. RtgBogieSteering이 보기 회전을 '완료한 순간'에만 바꾼다(꺾는 중 옆으로 미끄러짐 방지).")]
        [SerializeField] TravelAxis travelAxis = TravelAxis.Z;

        bool IsLane => travelAxis == TravelAxis.X;

        public override float Min => IsLane ? minX : min;
        public override float Max => IsLane ? maxX : max;
        protected override Vector3 LocalAxis => IsLane ? Vector3.right : Vector3.forward;

        /// <summary>주행 정지(잠금) — 보기를 꺾는 중엔 주행 금지. RtgBogieSteering이 제어.</summary>
        public bool TravelLocked { get; set; }

        /// <summary>현재 주행축. 배선/HUD 확인용.</summary>
        public TravelAxis Axis => travelAxis;

        protected override float ReadAxis() =>
            IsLane ? transform.localPosition.x : transform.localPosition.z;

        protected override void WriteAxis(float clamped)
        {
            var p = transform.localPosition;
            if (IsLane) p.x = clamped; else p.z = clamped;
            transform.localPosition = p;
        }

        // 주행 경로 장애물 정지
        [Header("장애물 정지")]
        [Tooltip("주행 경로(다리 콜라이더 앞)에 컨테이너 등 장애물이 있으면 그 방향 주행을 멈춘다(밀지 않음).")]
        [SerializeField] bool stopOnObstacle = true;

        /// <summary>장애물 정지 on/off — 자동 시나리오가 베이로 주행할 땐 끄고(컨테이너를 장애물로 오인해 멈추는 것 방지),
        /// 끝나면 원복한다. 수동 VR 주행에선 true 유지.</summary>
        public bool StopOnObstacle { get => stopOnObstacle; set => stopOnObstacle = value; }
        [Tooltip("장애물 감지 여유 거리(m).")]
        [SerializeField] float obstacleSkin = 0.03f;
        [Tooltip("장애물로 감지할 레이어(기본 전체). 화물(Rigidbody)만 정지시키므로 보통 그대로 둬도 됨.")]
        [SerializeField] LayerMask obstacleMask = ~0;

        Collider[] legColliders;
        float nextLegResolve;   // 빈 캐시일 때만 ~1s마다 재탐색(매 프레임 전체 탐색 방지)
        bool legWarned;

        // 크레인 간 충돌방지 (같은 레일 2대)
        //   진행방향에 다른 STS가 안전간격 안으로 들어오면 그 방향 주행만 막는다(멀어지는 건 허용 → 둘 다 중앙 접근 가능, 안 부딪침).
        //   Z만 비교하면 야드 RTG 등 다른 X의 크레인을 오인하므로 '같은 레일(X 근접)'만 본다.
        const float SafeGapMeters = 22f;   // 크레인 중심간 최소 간격(포털 ≈18m + 여유 4m)
        GantryMover[] others;
        float nextOtherResolve;

        void ResolveOthersIfNeeded()
        {
            if (others != null && Time.unscaledTime < nextOtherResolve) return;
            nextOtherResolve = Time.unscaledTime + 1f;
            var all = FindObjectsByType<GantryMover>(FindObjectsSortMode.None);
            var list = new List<GantryMover>();
            foreach (var g in all) if (g != null && g != this) list.Add(g);
            others = list.ToArray();
        }

        bool BlockedByOtherCrane(float target)
        {
            // 레인 이동(RTG 90°)은 이 규칙 밖 — '같은 레일 위 두 STS가 Z로 서로 접근'을 막는 로직이라
            //   X로 레인을 건너는 RTG엔 기준(같은 레일=X 근접 / 간격=Z 차)이 통째로 뒤집힌다.
            //   RTG끼리의 야드 간섭이 필요해지면 X 기준으로 따로 설계할 것(지금은 미대상).
            if (IsLane) return false;

            ResolveOthersIfNeeded();
            if (others == null || others.Length == 0) return false;
            float safeGap = SafeGapMeters * StsConfig.ModelScale;
            float curZ = transform.position.z, curX = transform.position.x;
            float delta = target - ReadAxis();
            if (Mathf.Abs(delta) < 1e-6f) return false;
            float targetZ = curZ + delta;   // 크레인 루트는 무부모라 로컬Z=월드Z
            foreach (var g in others)
            {
                if (g == null) continue;
                if (Mathf.Abs(g.transform.position.x - curX) > 1.0f) continue;   // 같은 레일(X 근접)만 — 야드 RTG 제외
                float otherZ = g.transform.position.z;
                if (Mathf.Abs(targetZ - otherZ) < Mathf.Abs(curZ - otherZ) &&    // 그 크레인 쪽으로 가까워지는 이동이고
                    Mathf.Abs(targetZ - otherZ) < safeGap)                        // 이동 후 간격이 안전간격 미만이면
                    return true;                                                  // → 막음
            }
            return false;
        }

        // 진행 방향으로 각 다리 콜라이더(Leg_Collider)를 BoxCast — 크레인 자신·잡은 화물 외의
        //   콜라이더에 닿으면 막힘(정지). 반대 방향(빠져나가기)은 막지 않는다.
        protected override bool IsBlockedToward(float target)
        {
            // 보기를 꺾는 중 — 타이어가 진행방향을 안 보고 있으므로 주행 금지.
            if (TravelLocked) return true;

            // 크레인 간 충돌방지 — 항상 검사(안전). 컨테이너 정지(stopOnObstacle)와 독립.
            if (BlockedByOtherCrane(target)) return true;

            if (!stopOnObstacle) return false;
            ResolveLegsIfNeeded();
            if (legColliders == null || legColliders.Length == 0) return false;   // 콜라이더 못 찾음 — 정지 기능 비활성(경고 후 주기 재시도)

            float d = target - ReadAxis();
            if (Mathf.Abs(d) < 1e-5f) return false;
            float sign = Mathf.Sign(d);
            Vector3 localDir = IsLane ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);
            Vector3 dir = (transform.parent != null
                ? transform.parent.TransformDirection(localDir)
                : localDir).normalized;
            float dist = Mathf.Abs(d) + obstacleSkin;

            foreach (var col in legColliders)
            {
                if (col == null) continue;
                Bounds b = col.bounds;
                // BoxCastAll: 가까운 것부터 1개만 보는 BoxCast와 달리 경로상 모두 검사 →
                //   자기 부품이 먼저 맞아도 뒤의 진짜 화물을 놓치지 않음.
                var hits = Physics.BoxCastAll(b.center, b.extents, dir, transform.rotation,
                                              dist, obstacleMask, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    if (hit.collider == null) continue;
                    if (hit.collider.transform.IsChildOf(transform)) continue;   // 자기(크레인·잡은 화물) 제외
                    // 컨테이너면 자유/고정 무관 장애물. ContainerInstance 단독 판정은 메뉴/씬의 테스트
                    //   컨테이너(ContainerInstance 미부착, Rigidbody+BoxCollider만)를 전부 놓쳐 감지가 무력화됐었다.
                    //   → ContainerInstance 또는 Rigidbody 보유면 컨테이너로 인정. 둘 다 없는 바닥/안벽/리그 등 정적 구조물만 무시.
                    if (hit.collider.GetComponentInParent<ContainerProject.ContainerInstance>() == null
                        && hit.collider.attachedRigidbody == null) continue;
                    QaBlockEdge(true, target, hit.collider.name);   // QA S-PHYS-4: 막힘 검출(밀지 않음) — 엣지에서만
                    return true;
                }
            }
            QaBlockEdge(false, target, null);
            return false;
        }

        // QA 콘솔 판정(S-PHYS-4) — 막힘 상태가 바뀐 순간만 한 줄. blocked=true면 MoveTo가 WriteAxis를 안 해
        //   위치 불변(moved=false)임이 AxisMoverBase에서 보장된다.
        bool qaBlockedPrev;
        void QaBlockEdge(bool blocked, float target, string obstacle)
        {
            if (!QaLog.Enabled || blocked == qaBlockedPrev) return;
            qaBlockedPrev = blocked;
            if (blocked)
                QaLog.Check("GANTRY", "block", true,
                    $"target={QaLog.F(target)} obstacle={obstacle} blocked=true moved=false");
            else
                QaLog.Info("GANTRY", "clear", $"target={QaLog.F(target)} blocked=false moved=true");
        }

        // legColliders가 비어 있을 때만(초기/구버전 크레인) ~1s마다 재탐색 + 1회 경고.
        //   IsBlockedToward는 주행 중 매 프레임 호출되므로 전체 계층 탐색을 빈 경우로만 제한한다.
        void ResolveLegsIfNeeded()
        {
            if (legColliders != null && legColliders.Length > 0) return;
            if (Time.unscaledTime < nextLegResolve) return;
            nextLegResolve = Time.unscaledTime + 1f;

            var list = new List<Collider>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (CraneHud.BaseName(t.name) == StsPartNames.LegCollider)
                {
                    var c = t.GetComponent<Collider>();
                    if (c != null) list.Add(c);
                }
            legColliders = list.ToArray();

            if (legColliders.Length == 0 && !legWarned)
            {
                legWarned = true;
                Debug.LogWarning("[Gantry] Leg_Collider를 못 찾음 — 장애물 정지 비활성. 크레인을 재생성해야 할 수 있습니다.");
            }
        }

        /// <summary>Builder가 한 번에 셋업할 때 사용. min/max는 절대 로컬 Z(생성 시 초기 위치 기준 ±range로 줄 것).</summary>
        public void Configure(float minZ, float maxZ)
        {
            min = minZ;
            max = maxZ;
        }

        /// <summary>RTG 레인 이동(스티어링 90°) 범위 — 절대 로컬 X. STS는 호출하지 않는다.</summary>
        public void ConfigureLane(float minLaneX, float maxLaneX)
        {
            minX = minLaneX;
            maxX = maxLaneX;
        }

        /// <summary>주행축 전환 — <see cref="RtgBogieSteering"/>가 보기 회전 완료 시에만 호출.
        /// 전환해도 반대 축 좌표는 유지된다(레인 이동 후에도 원래 Z를 지킴).</summary>
        public void SetTravelAxis(TravelAxis axis) => travelAxis = axis;

#if UNITY_EDITOR
        protected override int GizmoAxis => IsLane ? 0 : 2;   // 레인 이동 X(0) 또는 주행 Z(2)
        protected override Color GizmoColor => new Color(0.4f, 1f, 0.3f, 0.9f);
#endif
    }
}
