using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 호이스트 윗구간(트롤리 뒷면 ↔ 백리치 고정 앵커)을 매 프레임 갱신한다.
    /// 트롤리(이동)와 백리치 앵커(고정, 트롤리 backmost보다 더 뒤)가 서로 다른 거리라,
    /// 트롤리가 주행하면 로프 스팬이 변한다 → fall마다 직선 실린더 여러 토막으로 카테너리를 근사하고
    /// 처짐을 스팬에 비례시켜 '늘어남'을 표현. 경로는 붐 밑(거더/brace 아래)이라 관통 없음.
    ///
    /// 좌표계: 로프 세그먼트는 boom 자식(boom 로컬). 트롤리도 boom 자식이라
    ///   boom-로컬 트롤리 앵커 = trolley.localPosition + trolleyLocal[f] (트롤리는 회전·스케일 없음).
    ///   백리치 앵커는 boom 로컬 고정.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Boom Rope Rig")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class BoomRopeRig : MonoBehaviour
    {
        // ※ [SerializeField] 필수 — Play 진입(도메인 리로드) 시 Configure 참조가 직렬화 안 되면 null로 리셋됨.
        [SerializeField] Transform trolley;          // 이동하는 트롤리(boom 자식)
        [SerializeField] Vector3[] trolleyLocal;     // fall별 트롤리 뒷면 앵커(트롤리 로컬)
        [SerializeField] Vector3[] backAnchor;       // fall별 백리치 고정 앵커(boom 로컬)
        [SerializeField] Transform[] segments;       // 평탄화: fall f의 토막 = [f*segPer .. f*segPer+segPer-1]
        [SerializeField] int segPer = 6;
        [SerializeField] float radius = 0.0035f;
        [SerializeField] float sagFactor = 0.02f;    // 처짐 = sagFactor × 스팬

        public void Configure(Transform trolley, Vector3[] trolleyLocal, Vector3[] backAnchor,
                              Transform[] segments, int segPer, float radius, float sagFactor)
        {
            this.trolley = trolley;
            this.trolleyLocal = trolleyLocal;
            this.backAnchor = backAnchor;
            this.segments = segments;
            this.segPer = segPer;
            this.radius = radius;
            this.sagFactor = sagFactor;
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (trolley == null || segments == null || trolleyLocal == null || backAnchor == null) return;
            int falls = trolleyLocal.Length;
            if (backAnchor.Length != falls || segPer < 1 || segments.Length != falls * segPer) return;

            Vector3 tLocal = trolley.localPosition;
            for (int f = 0; f < falls; f++)
            {
                Vector3 a = tLocal + trolleyLocal[f];   // 트롤리 뒷면(boom 로컬)
                Vector3 b = backAnchor[f];              // 백리치 고정(boom 로컬)
                float sag = sagFactor * (b - a).magnitude;
                for (int k = 0; k < segPer; k++)
                {
                    var seg = segments[f * segPer + k];
                    if (seg == null) continue;
                    Vector3 p0 = CatPoint(a, b, k / (float)segPer, sag);
                    Vector3 p1 = CatPoint(a, b, (k + 1) / (float)segPer, sag);
                    Vector3 dir = p1 - p0;
                    float len = dir.magnitude;
                    seg.localPosition = (p0 + p1) * 0.5f;
                    seg.localRotation = len > 1e-5f ? Quaternion.FromToRotation(Vector3.up, dir / len)
                                                    : Quaternion.identity;
                    seg.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
                }
            }
        }

        static Vector3 CatPoint(Vector3 a, Vector3 b, float t, float sag)
        {
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y -= 4f * sag * t * (1f - t);
            return p;
        }
    }
}
