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
        [Header("승강 범위 (로컬 Y, 미터)")]
        [SerializeField] float min = 0.05f;
        [SerializeField] float max = 4f;

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

        protected override float ReadAxis() => transform.localPosition.y;
        protected override void WriteAxis(float clamped)
        {
            var p = transform.localPosition;
            p.y = clamped;
            transform.localPosition = p;
        }

        public void Configure(float minY, float maxY)
        {
            min = minY;
            max = maxY;
        }

#if UNITY_EDITOR
        protected override int GizmoAxis => 1;   // Y
        protected override Color GizmoColor => new Color(1f, 0.85f, 0.2f, 0.9f);
#endif
    }
}
