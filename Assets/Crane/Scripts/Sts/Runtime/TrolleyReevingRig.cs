using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 트롤리 추종 '리빙(reeving)' 케이블 — 트롤리가 붐을 주행하면 트롤리↔시브 구간과 시브 감김(wrap)을
    /// 매 프레임 다시 그린다. BoomRopeRig(고정 앵커로만 그림)와 달리 시브 '접점'과 '감김 호'를 트롤리 위치에서
    /// 재계산하므로, 트롤리가 정지점에서 멀어져도 접점·감김에 꺾임이 생기지 않는다('로프 허공 각짐 금지' 규칙 충족).
    ///
    /// 한 가닥(fall) = [트롤리 앵커 A] →(직선 span)→ [시브 접점 Tt] →(시브 호 reeve)→ [드럼쪽 탈출 접점 Te(고정)].
    ///   - A→Tt 직선은 원에 '접선'이라 Tt에서 꺾임 0(기하 보장). Tt에서 시작하는 호도 접선이라 연속.
    ///   - Te(=exitDeg 접점)는 고정 → 그 아래 드럼/기계실 구간은 정적으로 둬도 이음새가 안 벌어진다.
    ///
    /// 좌표계: 모든 좌표·세그먼트는 boom 로컬. 가닥은 단일 z평면(트롤리는 X만 주행 → A.z=C.z 유지).
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Trolley Reeving Rig")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class TrolleyReevingRig : MonoBehaviour
    {
        // ※ [SerializeField] 필수 — Play 진입(도메인 리로드) 시 Configure 참조가 직렬화 안 되면 null로 리셋된다.
        [SerializeField] Transform trolley;
        [SerializeField] Vector3[] trolleyLocal;   // fall별 트롤리 앵커(트롤리 원점 기준 boom-로컬 오프셋)
        [SerializeField] Vector3[] sheaveCenter;   // fall별 시브 중심(boom-로컬, 고정 폴백)
        [SerializeField] Transform[] sheaveXform;  // fall별 시브 실제 Transform(러핑 추종) — 있으면 매 프레임 이 월드좌표를 boom-로컬로 변환해 사용(붐 기립 시 로프가 시브 따라감)
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
            Apply();   // 생성 즉시 1회 배치 — 에디터 정적 씬에서 거대 실린더가 원점에 방치되는 것 방지.
        }

        void LateUpdate() => Apply();

#if UNITY_EDITOR
        // ExecuteAlways의 LateUpdate는 '정적 씬'에선 매 프레임 안 돌 수 있어, Scene 리페인트마다 호출되는
        //   OnRenderObject로 보완(Play 중엔 LateUpdate가 담당) — 에디터에서 트롤리를 옮겨도 로프가 따라온다.
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
                // [러핑] 시브 실제 Transform 있으면 그 월드좌표를 boom-로컬로 변환(붐 기립 추종) — 없으면 고정 폴백.
                bool luffed = sheaveXform != null && f < sheaveXform.Length && sheaveXform[f] != null && boomT != null;
                Vector3 C = luffed ? boomT.InverseTransformPoint(sheaveXform[f].position) : sheaveCenter[f];
                // [러핑] 붐 기립각 θ — 시브 마커는 luffPivot 하위(로컬 회전 항등)라 그 월드회전=피벗회전.
                //   boom-로컬로 환산하면 순수 Z회전 θ. 시브가 통째로 θ만큼 돌므로 탈출 접점각도 degD→degD+θ 여야 스윕된 RiseRail과 정확히 만난다.
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

                // ── 감김 호: degT → (exitDeg+θ)를 wrapSign 방향으로. degT는 트롤리 추종, 탈출각은 붐 기립 추종 → 양끝 연속.
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
