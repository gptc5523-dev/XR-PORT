using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 호이스트 윗구간(트롤리 뒷면 ↔ 백리치 고정 앵커)을 매 프레임 갱신한다.
    /// 트롤리(이동)와 백리치 앵커(고정)가 서로 다른 거리라, 트롤리가 주행하면 스팬·처짐이 변한다.
    ///
    /// 이질감 완화: 각 노드를 '카테너리 목표'로 즉시 스냅하지 않고 SmoothDamp(지연 추종)시킨다.
    ///   → 트롤리가 움직이면 케이블이 살짝 늘어지듯 끌려오고(관성 느낌), 멈추면 부드럽게 가라앉는다.
    ///   양 끝(트롤리/앵커)은 핀(즉시 부착). 직선 실린더 토막으로 곡선을 근사(토막↑ = 매끈).
    ///
    /// 좌표계: 세그먼트·노드는 boom 로컬. boom-로컬 트롤리 앵커 = trolley.localPosition + trolleyLocal[f].
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Boom Rope Rig")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class BoomRopeRig : MonoBehaviour
    {
        // ※ [SerializeField] 필수 — Play 진입(도메인 리로드) 시 Configure 참조가 직렬화 안 되면 null로 리셋됨.
        [SerializeField] Transform trolley;
        [SerializeField] Vector3[] trolleyLocal;     // fall별 트롤리 뒷면 앵커(트롤리 로컬)
        [SerializeField] Vector3[] backAnchor;       // fall별 백리치 고정 앵커(boom 로컬)
        [SerializeField] Transform[] segments;       // 평탄화: fall f = [f*segPer .. f*segPer+segPer-1]
        [SerializeField] int segPer = 12;
        [SerializeField] float radius = 0.0035f;
        [SerializeField] float sagFactor = 0.02f;    // 처짐 = sagFactor × 스팬
        [SerializeField] float smoothTime = 0.14f;   // 지연 추종 시간(클수록 더 늘어지듯 따라옴/늦게 가라앉음)

        // 런타임 시뮬 상태(직렬화 불필요 — 도메인 리로드 시 재시드)
        Vector3[] nodePos, nodeVel;

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
            nodePos = null; nodeVel = null;   // 재시드
            Apply();   // 생성 직후 즉시 1회 배치 — ExecuteAlways LateUpdate는 에디터 정적 씬에서 안 돌 수 있어,
                       // 세그먼트가 기본 실린더(거대) 크기로 원점에 방치되는 것 방지.
        }

        void LateUpdate() => Apply();

#if UNITY_EDITOR
        // 에디터 Scene에서 트롤리를 옮기거나 위치가 바뀌어도 로프가 따라오게 한다.
        //   ExecuteAlways의 LateUpdate는 '정적 씬'(아무 변화 없음)에선 매 프레임 안 돌 수 있어,
        //   Scene 리페인트마다 호출되는 OnRenderObject로 보완(Play 중엔 LateUpdate가 담당).
        void OnRenderObject()
        {
            if (!Application.isPlaying) Apply();
        }
#endif

        void Apply()
        {
            if (trolley == null || segments == null || trolleyLocal == null || backAnchor == null) return;
            int falls = trolleyLocal.Length;
            int nodeCount = segPer + 1;
            if (backAnchor.Length != falls || segPer < 1 || segments.Length != falls * segPer) return;

            if (nodePos == null || nodePos.Length != falls * nodeCount)
            {
                nodePos = new Vector3[falls * nodeCount];
                nodeVel = new Vector3[falls * nodeCount];
                Seed(falls, nodeCount);   // 첫 프레임 점프 방지 — 정적 카테너리로 시드
            }

            float dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;   // 에디터 큰 dt 클램프(SmoothDamp 안정)
            Vector3 tLocal = trolley.localPosition;

            for (int f = 0; f < falls; f++)
            {
                Vector3 a = tLocal + trolleyLocal[f];
                Vector3 b = backAnchor[f];
                float sag = sagFactor * (b - a).magnitude;
                int baseI = f * nodeCount;

                for (int i = 0; i < nodeCount; i++)
                {
                    Vector3 target = CatPoint(a, b, i / (float)segPer, sag);
                    int n = baseI + i;
                    if (i == 0 || i == segPer)        // 양 끝 = 핀(즉시 부착)
                    {
                        nodePos[n] = target; nodeVel[n] = Vector3.zero;
                    }
                    else if (dt <= 0f)                // 정지(에디터 비틱) = 정적 타깃
                    {
                        nodePos[n] = target;
                    }
                    else                              // 중간 = 지연 추종
                    {
                        nodePos[n] = Vector3.SmoothDamp(nodePos[n], target, ref nodeVel[n], smoothTime, Mathf.Infinity, dt);
                    }
                }

                for (int k = 0; k < segPer; k++)
                {
                    var seg = segments[f * segPer + k];
                    if (seg == null) continue;
                    Vector3 p0 = nodePos[baseI + k], p1 = nodePos[baseI + k + 1];
                    Vector3 dir = p1 - p0;
                    float len = dir.magnitude;
                    seg.localPosition = (p0 + p1) * 0.5f;
                    seg.localRotation = len > 1e-5f ? Quaternion.FromToRotation(Vector3.up, dir / len)
                                                    : Quaternion.identity;
                    seg.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
                }
            }
        }

        void Seed(int falls, int nodeCount)
        {
            Vector3 tLocal = trolley.localPosition;
            for (int f = 0; f < falls; f++)
            {
                Vector3 a = tLocal + trolleyLocal[f], b = backAnchor[f];
                float sag = sagFactor * (b - a).magnitude;
                for (int i = 0; i < nodeCount; i++)
                    nodePos[f * nodeCount + i] = CatPoint(a, b, i / (float)segPer, sag);
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
