using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// **곡선 포함 동적 권상 리빙** — 코너마다 로프를 LineRenderer로 매 프레임 그린다.
    ///
    /// 경로: 드럼출구(트롤리) → [직선] → 드럼쪽 시브 접선점 → [시브 감김 원호, 바닥 통과]
    ///       → 앵커쪽 시브 접선점 → [직선] → 앵커(트롤리).
    ///
    /// 접선점 = 시브 중심에서 각 상단점 방향으로 반지름 r (관통 방지). 감김 호는 시브 디스크 평면
    /// (회전축 = 시브 로컬 X)에서 두 접선점을 **긴 쪽(바닥 통과)** 으로 잇는 원호. 시브(스프레더)에
    /// 붙어 권상 시 두 직선 다리가 늘어나고 호는 시브 따라 이동. 월드좌표라 임포트 프레임 무관.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/RTG Rope Reeving")]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RtgRopeReeving : MonoBehaviour
    {
        [SerializeField] Transform[] drumExits;   // 코너별 드럼출구(트롤리)
        [SerializeField] Transform[] anchors;     // 코너별 앵커(트롤리)
        [SerializeField] Transform[] sheaves;     // 코너별 시브(스프레더)
        [SerializeField] LineRenderer[] lines;    // 코너별 로프
        [SerializeField] float[] radii;           // 코너별 시브 반지름(월드)
        [SerializeField] int arcSegments = 14;    // 감김 호 분할

        public void Configure(Transform[] drumExits, Transform[] anchors, Transform[] sheaves,
                              LineRenderer[] lines, float[] radii, int arcSegments)
        {
            this.drumExits = drumExits; this.anchors = anchors; this.sheaves = sheaves;
            this.lines = lines; this.radii = radii; this.arcSegments = Mathf.Max(3, arcSegments);
        }

        readonly List<Vector3> _pts = new List<Vector3>(32);

        void LateUpdate()
        {
            if (lines == null || sheaves == null) return;
            int n = lines.Length;
            for (int i = 0; i < n; i++)
            {
                var line = lines[i];
                if (line == null || i >= sheaves.Length || sheaves[i] == null) continue;

                Vector3 ce = sheaves[i].position;
                float r = (radii != null && i < radii.Length) ? radii[i] : 0.02f;

                Vector3 de = (drumExits != null && i < drumExits.Length && drumExits[i]) ? drumExits[i].position : ce;
                Vector3 an = (anchors   != null && i < anchors.Length   && anchors[i])   ? anchors[i].position   : ce;

                // 시브 감김 평면 = 두 다리(드럼·앵커 방향)가 만드는 평면. 그 법선 = 감김 축(외적).
                //   sheave.right는 임포트(bakeAxisConversion)로 틀어질 수 있어 외적으로 프레임 무관하게 구함.
                Vector3 raw1 = de - ce, raw2 = an - ce;
                Vector3 axis = Vector3.Cross(raw1, raw2);
                if (axis.sqrMagnitude < 1e-10f || raw1.sqrMagnitude < 1e-8f || raw2.sqrMagnitude < 1e-8f)
                { line.positionCount = 0; continue; }
                axis.Normalize();
                Vector3 e1 = raw1.normalized;                  // 드럼쪽(평면 내, 축과 수직)
                Vector3 e2 = raw2.normalized;                  // 앵커쪽

                _pts.Clear();
                _pts.Add(de);                                  // 드럼출구(트롤리)
                _pts.Add(ce + e1 * r);                         // 드럼쪽 접선점
                float ang   = Vector3.SignedAngle(e1, e2, axis);          // [-180,180]
                float sweep = (ang >= 0f) ? ang - 360f : ang + 360f;      // 반사각(긴 쪽 = 바닥 통과)
                for (int k = 1; k < arcSegments; k++)
                {
                    float t = (float)k / arcSegments;
                    _pts.Add(ce + (Quaternion.AngleAxis(sweep * t, axis) * e1) * r);
                }
                _pts.Add(ce + e2 * r);                         // 앵커쪽 접선점
                _pts.Add(an);                                  // 앵커(트롤리)

                line.positionCount = _pts.Count;
                for (int p = 0; p < _pts.Count; p++) line.SetPosition(p, _pts[p]);
            }
        }

        static Vector3 InPlane(Vector3 v, Vector3 axis) => v - axis * Vector3.Dot(v, axis);
    }
}
