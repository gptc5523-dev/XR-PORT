using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 트롤리(Trolley)를 붐 위 레일을 따라 X축으로 슬라이딩.
    /// 스프레더는 트롤리의 X를 따라가야 하므로 호이스트 참조를 받아 동기화한다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Trolley Mover")]
    [DisallowMultipleComponent]
    public sealed class TrolleyMover : AxisMoverBase
    {
        [Header("레일 범위 (로컬 X, 미터)")]
        [SerializeField] float min = -5f;
        [SerializeField] float max =  15f;

        [Tooltip("스프레더 케이블/헤드블록 Transform — 트롤리 X를 따라간다.")]
        [SerializeField] Transform spreaderRoot;

        public override float Min => min;
        public override float Max => max;
        protected override Vector3 LocalAxis => Vector3.right;

        protected override float ReadAxis() => transform.localPosition.x;
        protected override void WriteAxis(float clamped)
        {
            var p = transform.localPosition;
            p.x = clamped;
            transform.localPosition = p;
        }

        // 스프레더가 트롤리의 자식이면(예: FBX RTG 계층 Trolley→Spreader) 트롤리가 이동할 때 부모를 따라
        //   자동으로 딸려온다 → 여기서 X를 또 쓰면 이중 적용(두 배 이동·프레임 불일치로 정렬 깨짐).
        //   자식이 아닐 때(STS: 스프레더 루트가 트롤리의 형제)만 수동 동기화한다. 참조가 바뀔 때만 1회 재판정.
        Transform syncCheckedFor;
        bool spreaderIsDescendant;

        // 트롤리 X 이동 후 스프레더도 같은 X로 동기화(단, 트롤리 자식이면 생략).
        protected override void OnMoved(float clamped)
        {
            if (spreaderRoot == null) return;
            if (spreaderRoot != syncCheckedFor)
            {
                syncCheckedFor = spreaderRoot;
                spreaderIsDescendant = spreaderRoot.IsChildOf(transform);
            }
            if (spreaderIsDescendant) return;   // 부모(트롤리)가 이미 X로 옮김 → 중복 금지

            var sp = spreaderRoot.localPosition;
            sp.x = clamped;
            spreaderRoot.localPosition = sp;
        }

        // 이동 경로 장애물 정지 (충돌방지 — 갠트리와 동일 방식)
        [Header("장애물 정지 (충돌방지)")]
        [Tooltip("이동 경로에 컨테이너 등 장애물이 있으면 그 방향 이동을 멈춘다(밀지 않음).")]
        [SerializeField] bool stopOnObstacle = true;
        [Tooltip("장애물 감지 여유 거리(m).")]
        [SerializeField] float obstacleSkin = 0.03f;
        [Tooltip("감지 박스 반크기(m) — 매달린 스프레더/컨테이너 대략 크기.")]
        [SerializeField] Vector3 obstacleHalf = new Vector3(0.04f, 0.06f, 0.06f);
        [SerializeField] LayerMask obstacleMask = ~0;

        // 트롤리 X 이동 경로에 화물이 있으면 막힘. 기준은 '매달린 스프레더/컨테이너' 위치
        //   (트롤리 본체는 붐 위라 야드 컨테이너와 안 부딪힘 → 스프레더가 내려가 있을 때만 충돌).
        protected override bool IsBlockedToward(float target)
        {
            if (!stopOnObstacle || spreaderRoot == null) return false;
            float dx = target - transform.localPosition.x;
            if (Mathf.Abs(dx) < 1e-5f) return false;
            float sign = Mathf.Sign(dx);
            Vector3 dir = (transform.parent != null
                ? transform.parent.TransformDirection(new Vector3(sign, 0f, 0f))
                : new Vector3(sign, 0f, 0f)).normalized;
            float dist = Mathf.Abs(dx) + obstacleSkin;
            var hits = Physics.BoxCastAll(spreaderRoot.position, obstacleHalf, dir, spreaderRoot.rotation,
                                          dist, obstacleMask, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;   // 자기(트롤리·잡은 화물) 제외
                // 컨테이너면 자유/고정 무관 장애물. ContainerInstance 단독 판정은 메뉴/씬의 테스트
                //   컨테이너(ContainerInstance 미부착, Rigidbody+BoxCollider만)를 전부 놓쳐 감지가 무력화됐었다.
                //   → ContainerInstance 또는 Rigidbody 보유면 컨테이너로 인정. 둘 다 없는 바닥·안벽·리그 등 정적 구조물만 무시.
                if (hit.collider.GetComponentInParent<ContainerProject.ContainerInstance>() == null
                    && hit.collider.attachedRigidbody == null) continue;
                return true;
            }
            return false;
        }

        /// <summary>Builder가 한 번에 셋업할 때 사용.</summary>
        public void Configure(float minX, float maxX, Transform spreader)
        {
            min = minX;
            max = maxX;
            spreaderRoot = spreader;
        }

#if UNITY_EDITOR
        protected override int GizmoAxis => 0;   // X
        protected override Color GizmoColor => new Color(0.2f, 0.9f, 1f, 0.9f);
#endif
    }
}
