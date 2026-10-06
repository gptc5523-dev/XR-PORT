using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>갠트리 주행 — 크레인 루트를 로컬 Z로 슬라이딩(IAxisMover).
    /// RTG는 <see cref="RtgBogieSteering"/>가 보기를 90° 꺾으면 주행축이 X(레인 이동)로 바뀐다.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Gantry Mover")]
    [DisallowMultipleComponent]
    public sealed class GantryMover : AxisMoverBase
    {
        /// <summary>주행 축(로컬). Z=기본 주행, X=RTG 레인 이동.
        /// ★ Z가 0번인 건 의도 — 바꾸면 옛 씬이 레인 모드(범위 0~0)로 깨어나 주행이 멎는다.</summary>
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

        /// <summary>장애물 정지 on/off — 자동 시나리오 베이 주행 중엔 끈다(컨테이너 오인 정지 방지).</summary>
        public bool StopOnObstacle { get => stopOnObstacle; set => stopOnObstacle = value; }
        [Tooltip("장애물 감지 여유 거리(m).")]
        [SerializeField] float obstacleSkin = 0.03f;
        [Tooltip("장애물로 감지할 레이어(기본 전체). 화물(Rigidbody)만 정지시키므로 보통 그대로 둬도 됨.")]
        [SerializeField] LayerMask obstacleMask = ~0;

        Collider[] legColliders;
        float nextLegResolve;   // 빈 캐시일 때만 ~1s마다 재탐색
        bool legWarned;

        // 크레인 간 충돌방지 — 같은 레일(X 근접)의 다른 STS 쪽으로 안전간격 안에 들어가는 이동만 막는다.
        const float SafeGapMeters = 22f;   // 크레인 중심간 최소 간격(포털 ≈18m + 여유 4m)
        GantryMover[] others;
        float nextOtherResolve;

        void ResolveOthersIfNeeded()
        {
            if (others != null && Time.unscaledTime < nextOtherResolve) return;
            nextOtherResolve = Time.unscaledTime + 1f;
            var all = FindObjectsByType<GantryMover>();
            var list = new List<GantryMover>();
            foreach (var g in all) if (g != null && g != this) list.Add(g);
            others = list.ToArray();
        }

        bool BlockedByOtherCrane(float target)
        {
            // 레인 이동(RTG X)은 이 규칙 밖 — RTG끼리 간섭이 필요해지면 X 기준으로 따로 설계.
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
                if (Mathf.Abs(targetZ - otherZ) < Mathf.Abs(curZ - otherZ) &&    // 가까워지는 이동이고
                    Mathf.Abs(targetZ - otherZ) < safeGap)                        // 이동 후 안전간격 미만
                    return true;
            }
            return false;
        }

        // 진행 방향으로 다리 콜라이더를 BoxCast — 자기 외 콜라이더에 닿으면 정지. 빠져나가는 방향은 허용.
        protected override bool IsBlockedToward(float target)
        {
            // 보기를 꺾는 중엔 주행 금지.
            if (TravelLocked) return true;

            // 크레인 간 충돌방지 — 항상 검사, stopOnObstacle 과 독립.
            if (BlockedByOtherCrane(target)) return true;

            if (!stopOnObstacle) return false;
            ResolveLegsIfNeeded();
            if (legColliders == null || legColliders.Length == 0) return false;   // 콜라이더 없음 — 정지 비활성(주기 재시도)

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
                // BoxCastAll — 자기 부품이 먼저 맞아도 뒤의 화물을 놓치지 않게.
                var hits = Physics.BoxCastAll(b.center, b.extents, dir, transform.rotation,
                                              dist, obstacleMask, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    if (hit.collider == null) continue;
                    if (hit.collider.transform.IsChildOf(transform)) continue;   // 자기(크레인·잡은 화물) 제외
                    // 컨테이너 = ContainerInstance 또는 Rigidbody 보유(테스트 컨테이너 포함). 정적 구조물은 무시.
                    if (hit.collider.GetComponentInParent<AIXRCrane.ContainerInstance>() == null
                        && hit.collider.attachedRigidbody == null) continue;
                    QaBlockEdge(true, target, hit.collider.name);   // QA S-PHYS-4
                    return true;
                }
            }
            QaBlockEdge(false, target, null);
            return false;
        }

        // QA S-PHYS-4 — 막힘 상태가 바뀐 순간만 한 줄(막히면 AxisMoverBase가 위치 불변 보장).
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

        // legColliders가 비었을 때만 ~1s마다 재탐색 + 1회 경고(매 프레임 계층 탐색 방지).
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

        /// <summary>주행축 전환 — <see cref="RtgBogieSteering"/>가 보기 회전 완료 시 호출. 반대 축 좌표는 유지.</summary>
        public void SetTravelAxis(TravelAxis axis) => travelAxis = axis;

#if UNITY_EDITOR
        protected override int GizmoAxis => IsLane ? 0 : 2;   // 레인 이동 X(0) 또는 주행 Z(2)
        protected override Color GizmoColor => new Color(0.4f, 1f, 0.3f, 0.9f);
#endif
    }
}
