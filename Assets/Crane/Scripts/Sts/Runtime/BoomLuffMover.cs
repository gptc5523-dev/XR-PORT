using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>붐 러핑 구동 — 피벗 로컬 +X(바다측)를 로컬 Z+회전으로 올린다. current=0 수평, current=max 기립.
    /// 물리 무관 kinematic 회전, ExecuteAlways로 에디터에서도 자세 확인.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Boom Luff Mover")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class BoomLuffMover : MonoBehaviour
    {
        [SerializeField] float restDeg = 0f;    // 수평(작업) 각 — 최소각
        [SerializeField] float maxDeg  = 83f;   // 기립(스토우) 각 — 최대각(회의 확정 83°)
        [SerializeField] float current = 0f;    // 현재 각(0 = 수평)

        /// <summary>현재 러핑 각(도). rest~max로 클램프 후 자세 반영.</summary>
        public float Current
        {
            get => current;
            set { current = Mathf.Clamp(value, restDeg, maxDeg); Apply(); }
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
