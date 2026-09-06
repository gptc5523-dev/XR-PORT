using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// **월드좌표 기반 동적 권상 로프** — 각 로프를 매 프레임(LateUpdate) 상단 앵커와 시브 **접선점** 사이에 실린더로 배치.
    ///
    /// 핵심: 하단 끝점은 시브 **중심(피벗)이 아니라 접선점**이다.
    ///   bottom = center + (top − center).normalized × r   (r = 시브 반지름, 월드)
    /// 시브 중심(헤드블록 박스 안쪽)에 붙이면 로프가 헤드블록 윗면을 관통하므로, 반지름만큼 앵커 방향으로
    /// 올린 접선점(시브 홈 윗부분)에 물린다. 스프레더가 오르내려도 매 프레임 재계산돼 자동으로 맞음.
    ///
    /// top(앵커)은 트롤리 자식이라 횡행 추종, bottom(시브)은 스프레더 자식이라 권상 추종.
    /// 로컬축 가정 없음(월드좌표만) → bakeAxisConversion 임포트 프레임 무관. [ExecuteAlways].
    /// </summary>
    [AddComponentMenu("Container/STS Crane/RTG Rope Dynamic")]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RtgRopeDynamic : MonoBehaviour
    {
        [SerializeField] Transform[] tops;        // 상단(트롤리 앵커)
        [SerializeField] Transform[] centers;     // 하단 시브 중심(스프레더) — 접선점은 여기서 r만큼 올림
        [SerializeField] Transform[] ropes;       // 로프 실린더
        [SerializeField] float[]     tangentR;    // 각 시브 반지름(월드) — 접선점 오프셋
        [SerializeField] float worldRadius = 0.003f;   // 로프 튜브 반경(월드 m)

        public void Configure(Transform[] tops, Transform[] centers, Transform[] ropes, float[] tangentR, float worldRadius)
        {
            this.tops = tops; this.centers = centers; this.ropes = ropes;
            this.tangentR = tangentR; this.worldRadius = worldRadius;
        }

        void LateUpdate()
        {
            if (tops == null || centers == null || ropes == null) return;
            int n = Mathf.Min(tops.Length, Mathf.Min(centers.Length, ropes.Length));
            for (int i = 0; i < n; i++)
            {
                Transform t = tops[i], c = centers[i], r = ropes[i];
                if (t == null || c == null || r == null) continue;

                Vector3 top = t.position, center = c.position;
                Vector3 toTop = top - center;
                float d = toTop.magnitude;
                float rr = (tangentR != null && i < tangentR.Length) ? tangentR[i] : 0f;
                Vector3 bottom = d > 1e-6f ? center + toTop / d * rr : center;   // 시브 접선점(관통 방지)

                Vector3 dir = top - bottom;
                float len = dir.magnitude;
                r.position = (top + bottom) * 0.5f;
                r.rotation = len > 1e-6f ? Quaternion.FromToRotation(Vector3.up, dir / len) : Quaternion.identity;
                Vector3 ps = r.parent != null ? r.parent.lossyScale : Vector3.one;
                r.localScale = new Vector3(
                    worldRadius * 2f / Mathf.Max(1e-6f, ps.x),
                    (len * 0.5f)     / Mathf.Max(1e-6f, ps.y),
                    worldRadius * 2f / Mathf.Max(1e-6f, ps.z));

#if UNITY_EDITOR
                if (!_logged && i == 0)
                {
                    _logged = true;
                    float wRad = r.localScale.x * 0.5f * ps.x;   // 실제 렌더 월드 반경
                    Debug.Log($"[RTGROPE] 실측 굵기 — rope[0] 월드지름={(wRad * 2000f):F2}mm, 월드길이={len:F3}m (worldRadius설정={worldRadius:F4}, parentLossy={ps.x:F3}, localScale.x={r.localScale.x:F5})");
                }
#endif
            }
        }

#if UNITY_EDITOR
        bool _logged;
#endif
    }
}
