using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 갠트리 주행 — 크레인 루트를 안벽 방향(Z축)으로 슬라이딩.
    /// 트롤리(X)·호이스트(Y)와 동일하게 IAxisMover로 추상화 → 상위(VR/Operator)는 어떤 축인지 모르고 일관되게 호출.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Gantry Mover")]
    [DisallowMultipleComponent]
    public sealed class GantryMover : AxisMoverBase
    {
        [Header("주행 범위 (로컬 Z, 미터)")]
        [SerializeField] float min = -1f;
        [SerializeField] float max =  1f;

        public override float Min => min;
        public override float Max => max;

        protected override float ReadAxis() => transform.localPosition.z;
        protected override void WriteAxis(float clamped)
        {
            var p = transform.localPosition;
            p.z = clamped;
            transform.localPosition = p;
        }

        // ───────── 주행 경로 장애물 정지 ─────────
        [Header("장애물 정지")]
        [Tooltip("주행 경로(다리 콜라이더 앞)에 컨테이너 등 장애물이 있으면 그 방향 주행을 멈춘다(밀지 않음).")]
        [SerializeField] bool stopOnObstacle = true;
        [Tooltip("장애물 감지 여유 거리(m).")]
        [SerializeField] float obstacleSkin = 0.03f;
        [Tooltip("장애물로 감지할 레이어(기본 전체). 화물(Rigidbody)만 정지시키므로 보통 그대로 둬도 됨.")]
        [SerializeField] LayerMask obstacleMask = ~0;

        Collider[] legColliders;
        float nextLegResolve;   // 빈 캐시일 때만 ~1s마다 재탐색(매 프레임 전체 탐색 방지)
        bool legWarned;

        // 진행 방향으로 각 다리 콜라이더(Leg_Collider)를 BoxCast — 크레인 자신·잡은 화물 외의
        //   콜라이더에 닿으면 막힘(정지). 반대 방향(빠져나가기)은 막지 않는다.
        protected override bool IsBlockedToward(float target)
        {
            if (!stopOnObstacle) return false;
            ResolveLegsIfNeeded();
            if (legColliders == null || legColliders.Length == 0) return false;   // 콜라이더 못 찾음 — 정지 기능 비활성(경고 후 주기 재시도)

            float dz = target - transform.localPosition.z;
            if (Mathf.Abs(dz) < 1e-5f) return false;
            float sign = Mathf.Sign(dz);
            Vector3 dir = (transform.parent != null
                ? transform.parent.TransformDirection(new Vector3(0f, 0f, sign))
                : new Vector3(0f, 0f, sign)).normalized;
            float dist = Mathf.Abs(dz) + obstacleSkin;

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
                    // 바닥/안벽/통로/플레이어 리그 등 '정적 구조물'은 무시 — 자유 화물(Rigidbody)만 장애물로 정지.
                    //   (프로젝트 전반에서 '집을 수 있는 화물 = Rigidbody' 규약: SpreaderGrabber/CraneNetSync와 동일)
                    if (hit.collider.attachedRigidbody == null) continue;
                    return true;
                }
            }
            return false;
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
                if (CraneHud.BaseName(t.name) == "Leg_Collider")
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

#if UNITY_EDITOR
        protected override int GizmoAxis => 2;   // Z
        protected override Color GizmoColor => new Color(0.4f, 1f, 0.3f, 0.9f);
#endif
    }
}
