using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 붐 러핑(기립) 구동 — 바다측 붐을 힌지 피벗에서 Z축으로 회전시켜 세우고 내린다.
    ///
    /// 좌표 규약: 피벗(Boom_LuffPivot)은 바다다리 상단 힌지(=거더 중심선)에 있고, 러핑 대상 구조는
    ///   피벗 로컬 +X(바다측)로 뻗는다. 로컬 Z축 +회전 → +X 부재가 +Y(위)로 올라간다(붐 기립).
    ///   current=0 = 수평(작업 자세), current=max = 스토우(기립 자세).
    ///
    /// 물리 무관 kinematic 회전(AxisMover/SpreaderHoist와 동일 규약) — 값을 넣으면 즉시 자세 반영.
    /// [ExecuteAlways]로 에디터에서도 자세 확인(Play 없이). Play 진입(도메인 리로드) 대비 [SerializeField] 유지.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Boom Luff Mover")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class BoomLuffMover : MonoBehaviour
    {
        [SerializeField] float restDeg = 0f;    // 수평(작업) 각 — 최소각
        [SerializeField] float maxDeg  = 83f;   // 기립(스토우) 각 — 최대각(회의 확정 83°)
        [SerializeField] float current = 0f;    // 현재 각(0 = 수평)

        public float Rest => restDeg;
        public float Max  => maxDeg;

        /// <summary>현재 러핑 각(도). rest~max로 클램프 후 자세 반영.</summary>
        public float Current
        {
            get => current;
            set { current = Mathf.Clamp(value, restDeg, maxDeg); Apply(); }
        }

        /// <summary>0~1 정규화 위치(0=수평, 1=완전 기립).</summary>
        public float Normalized
        {
            get => Mathf.Approximately(maxDeg, restDeg) ? 0f : (current - restDeg) / (maxDeg - restDeg);
            set => Current = Mathf.Lerp(restDeg, maxDeg, Mathf.Clamp01(value));
        }

        public void Configure(float rest, float max)
        {
            restDeg = rest;
            maxDeg  = max;
            current = Mathf.Clamp(current, restDeg, maxDeg);
            Apply();
        }

        void OnEnable()  => Apply();
        void OnValidate() => Apply();   // 인스펙터에서 값 바꿔도 즉시 반영(에디터 확인용)

        void Apply()
        {
            // 로컬 +X(바다측 붐)를 +Y로 올리는 방향 = 로컬 Z + 회전.
            transform.localRotation = Quaternion.Euler(0f, 0f, current);
        }
    }
}
