using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>트롤리 추종 리빙 케이블 — 트롤리↔시브 구간과 감김을 매 프레임 다시 그린다.
    /// 한 가닥 = 앵커 A →(접선)→ 시브 접점 Tt →(호)→ 탈출점 Te. 좌표는 boom 로컬, 단일 z평면.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Trolley Reeving Rig")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class TrolleyReevingRig : MonoBehaviour
    {
        // ★ [SerializeField] 필수 — 아니면 도메인 리로드 때 Configure 참조가 null로 리셋된다.
        [SerializeField] Transform trolley;
        [SerializeField] Vector3[] trolleyLocal;   // fall별 트롤리 앵커(트롤리 원점 기준 boom-로컬 오프셋)
        [SerializeField] Vector3[] sheaveCenter;   // fall별 시브 중심(boom-로컬, 고정 폴백)
        [SerializeField] Transform[] sheaveXform;  // fall별 시브 Transform(있으면 러핑 추종, 없으면 고정 폴백)
        [SerializeField] float[]   seat;           // fall별 시브 시트(로프 피치) 반경
        [SerializeField] float[]   tangentSign;    // 접선 분기: +1=상부(az+off) / -1=하부(az-off)
        [SerializeField] float[]   exitDeg;        // 드럼쪽 탈출 접점 각(고정) — 호의 끝
        [SerializeField] float[]   wrapSign;       // 감김 방향: +1=CCW / -1=CW
        [SerializeField] Transform[] segments;     // 평탄화: fall f = [f*segPer .. +segPer-1] (= span + arc)
        [SerializeField] int spanPer = 6;          // 트롤리→접점 직선 토막 수
        [SerializeField] int arcPer  = 18;         // 시브 감김 호 토막 수
        [SerializeField] float radius = 0.0035f;
        [SerializeField] float spanSag = 0.004f;   // 직선 구간 미세 처짐(타이트한 강선이라 작게)

        readonly List<Vector3> nodes = new List<Vector3>();

        public void Configure(Transform trolley, Vector3[] trolleyLocal, Vector3[] sheaveCenter,
                              float[] seat, float[] tangentSign, float[] exitDeg, float[] wrapSign,
                              Transform[] segments, int spanPer, int arcPer, float radius, float spanSag,
                              Transform[] sheaveXform = null)
        {
            this.trolley = trolley;
            this.trolleyLocal = trolleyLocal;
            this.sheaveCenter = sheaveCenter;
            this.sheaveXform = sheaveXform;
            this.seat = seat;
            this.tangentSign = tangentSign;
            this.exitDeg = exitDeg;
            this.wrapSign = wrapSign;
            this.segments = segments;
            this.spanPer = spanPer;
            this.arcPer = arcPer;
            this.radius = radius;
            this.spanSag = spanSag;
            Apply();   // 생성 즉시 배치 — 원점에 거대 실린더 방치 방지
        }

        void LateUpdate() => Apply();

#if UNITY_EDITOR
        // 에디트 모드 정적 씬에선 LateUpdate가 안 돌 수 있어 OnRenderObject로 보완(Play 중엔 LateUpdate).
        void OnRenderObject() { if (!Application.isPlaying) Apply(); }
#endif

        void Apply()
        {
            if (trolley == null || segments == null || trolleyLocal == null || sheaveCenter == null
                || seat == null || tangentSign == null || exitDeg == null || wrapSign == null) return;
            int falls = trolleyLocal.Length;
            int segPer = spanPer + arcPer;
            if (spanPer < 1 || arcPer < 1 || segPer < 1) return;
            if (sheaveCenter.Length != falls || seat.Length != falls || tangentSign.Length != falls
                || exitDeg.Length != falls || wrapSign.Length != falls || segments.Length != falls * segPer) return;

            Vector3 tLocal = trolley.localPosition;
            Transform boomT = trolley.parent;   // 리그 좌표계 = boom-로컬(트롤리·세그먼트 부모)

            for (int f = 0; f < falls; f++)
            {
                Vector3 A = tLocal + trolleyLocal[f];
                // [러핑] 시브 Transform 있으면 boom-로컬로 변환(붐 기립 추종), 없으면 고정 폴백.
                bool luffed = sheaveXform != null && f < sheaveXform.Length && sheaveXform[f] != null && boomT != null;
                Vector3 C = luffed ? boomT.InverseTransformPoint(sheaveXform[f].position) : sheaveCenter[f];
                // [러핑] 시브가 붐과 θ만큼 돌므로 탈출각도 degD+θ(RiseRail과 일치).
                float luffTheta = 0f;
                if (luffed)
                {
                    Quaternion luffLocal = Quaternion.Inverse(boomT.rotation) * sheaveXform[f].rotation;
                    luffTheta = luffLocal.eulerAngles.z;
                    if (luffTheta > 180f) luffTheta -= 360f;   // [-180,180]
                }
                float r = seat[f];

                // ── 시브 접점(외접선) — A에서 반경 r 원으로의 접선각. 직선 A→Tt는 원에 접해 Tt에서 꺾임 0.
                float dx = A.x - C.x, dy = A.y - C.y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float baseAng = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                float off = Mathf.Acos(Mathf.Clamp01(dist > 1e-6f ? r / dist : 1f)) * Mathf.Rad2Deg;
                float degT = baseAng + tangentSign[f] * off;
                Vector3 Tt = OnCircle(C, r, degT);

                // ── 감김 호: degT → (exitDeg+θ)를 wrapSign 방향으로 — 양끝 연속.
                float exitAng = exitDeg[f] + luffTheta;
                float sweep = Mathf.Repeat(exitAng - degT, 360f);   // [0,360)
                if (wrapSign[f] < 0f) sweep -= 360f;                   // CW면 (-360,0]

                // ── 노드 구성: A …span… Tt …arc… Te. Tt 중복 없이 이어붙임.
                nodes.Clear();
                for (int i = 0; i <= spanPer; i++)
                {
                    float t = i / (float)spanPer;
                    Vector3 p = Vector3.Lerp(A, Tt, t);
                    p.y -= 4f * spanSag * t * (1f - t);   // 포물선 처짐(중앙 최대)
                    nodes.Add(p);
                }
                for (int j = 1; j <= arcPer; j++)
                {
                    float deg = degT + sweep * (j / (float)arcPer);
                    nodes.Add(OnCircle(C, r, deg));
                }

                // ── 세그먼트(실린더) 배치 — 각 노드쌍 사이로 회전·신축.
                int baseI = f * segPer;
                for (int k = 0; k < segPer; k++)
                {
                    var seg = segments[baseI + k];
                    if (seg == null) continue;
                    Vector3 p0 = nodes[k], p1 = nodes[k + 1];
                    Vector3 dir = p1 - p0;
                    float len = dir.magnitude;
                    seg.localPosition = (p0 + p1) * 0.5f;
                    seg.localRotation = len > 1e-5f ? Quaternion.FromToRotation(Vector3.up, dir / len)
                                                    : Quaternion.identity;
                    seg.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
                }
            }
        }

        static Vector3 OnCircle(Vector3 c, float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, c.z);
        }
    }
}
