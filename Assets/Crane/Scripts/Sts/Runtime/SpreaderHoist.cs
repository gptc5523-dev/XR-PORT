using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 스프레더(Spreader) Y축 호이스트 — 케이블을 감거나 풀어 상하 이동.
    /// X/Z는 TrolleyMover가 동기화하므로 여기서는 건드리지 않는다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Spreader Hoist")]
    [DisallowMultipleComponent]
    public sealed class SpreaderHoist : AxisMoverBase
    {
        [Header("승강 범위 (미터)")]
        [SerializeField] float min = 0.05f;
        [SerializeField] float max = 4f;

        // 월드 수직 모드: FBX 임포트(bakeAxisConversion)로 스프레더 로컬 Y축이 월드 위쪽과 어긋난 크레인용.
        //   true  → transform.position.y(월드 절대) 직접 구동, x/z 보존. min/max도 월드 Y 절대값.
        //   false → 기존 localPosition.y(부모 로컬). 절차 생성 크레인(로컬축=월드축)은 이대로 정상.
        [Tooltip("FBX 크레인: 로컬 Y가 월드 위쪽과 어긋나므로 월드 Y로 직접 구동. 절차 크레인은 off.")]
        [SerializeField] bool worldVertical = false;
        public void SetWorldVertical(bool v) => worldVertical = v;

        // 컨테이너 적재 시 하강 한계를 올리는 양 — 스프레더가 아니라 '컨테이너 밑면'이 바닥에 닿게.
        // SpreaderGrabber가 잡을 때 설정, 놓을 때 0으로. 0이면 빈 스프레더(기존 동작).
        float floorOffset = 0f;
        // 0 이상이되, 하강 한계(min+offset)가 상한(max)을 넘지 않게 클램프.
        // 안 그러면 키 큰 컨테이너를 바닥 근처에서 잡을 때 LowerLimit>Max가 돼 Mathf.Clamp가 역전(항상 max 반환)→호이스트 먹통.
        public void SetFloorOffset(float v) => floorOffset = Mathf.Clamp(v, 0f, Mathf.Max(0f, max - min));
        /// <summary>현재 하강 한계 오프셋(m). 네트워크 동기화가 클라이언트에 동일 한계를 재현하는 데 사용.</summary>
        public float FloorOffset => floorOffset;

        public override float Min => min;
        public override float Max => max;

        // 하강 한계만 floorOffset 만큼 올린다(MoveToNormalized의 Lerp 끝점은 기존대로 min..max 유지).
        protected override float LowerLimit => min + floorOffset;

        // 월드 수직 모드는 min/max 가 월드 Y 절대값이라 부모의 회전·스케일을 타지 않는다.
        protected override Vector3 LocalAxis => Vector3.up;
        public override Vector3 WorldAxis => worldVertical ? Vector3.up : base.WorldAxis;

        protected override float ReadAxis()
            => worldVertical ? transform.position.y : transform.localPosition.y;
        protected override void WriteAxis(float clamped)
        {
            if (worldVertical)
            {
                var p = transform.position;      // 월드 Y만 (x,z 보존 → 트롤리 추종 유지)
                p.y = clamped;
                transform.position = p;
            }
            else
            {
                var p = transform.localPosition;
                p.y = clamped;
                transform.localPosition = p;
            }
        }

        public void Configure(float minY, float maxY)
        {
            min = minY;
            max = maxY;
        }

#if UNITY_EDITOR
        protected override int GizmoAxis => 1;   // Y
        protected override Color GizmoColor => new Color(1f, 0.85f, 0.2f, 0.9f);

        // worldVertical이면 min/max는 월드 Y 절대값 — WriteAxis와 같은 해석을 써야 한다.
        // 기본 구현(부모 로컬 축 + TransformPoint)에 넣으면 부모 스케일·베이크된 축 회전을 타고
        // 범위선이 크레인 밖 엉뚱한 곳에 그려진다(FBX RTG: 0.87 범위가 3.6유닛 선으로 표시됐음).
        protected override Vector3 GizmoPointAt(float axisValue)
        {
            if (!worldVertical) return base.GizmoPointAt(axisValue);
            var p = transform.position;
            p.y = axisValue;
            return p;
        }
#endif
    }
}
