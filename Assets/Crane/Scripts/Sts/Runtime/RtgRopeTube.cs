using System.Collections.Generic;
using UnityEngine;

namespace Container.Crane.Sts
{
    /// <summary>
    /// **모양 유지 + 동적 신축 권상 로프** — Blender에서 모델링한 로프(Hoist_Rope_*)의 실측 굵기(Ø53mm=반경 0.0265m,
    /// 32각 단면)를 그대로 재현하되, 정적 메시가 아니라 리빙 경로를 따라 매 프레임 **튜브 메시를 재생성**해 권상 시 신축한다.
    ///
    /// 경로(코너별, <see cref="RtgRopeReeving"/>과 동일): 드럼출구(트롤리 고정) → [직선 낙차] → 드럼쪽 시브 접선점
    ///   → [시브 하부 감김 원호] → 앵커쪽 시브 접선점 → [직선 낙차] → 앵커(트롤리 고정).
    /// 시브(스프레더)가 내려가면 두 직선 낙차가 늘어나고 감김 호는 시브 따라 이동 → 실물처럼 신축.
    ///
    /// 왜 튜브 재생성인가: 로프는 "모양=움직임"이라 정점을 얼릴 수 없다(권상 시 늘어나야 함). 대신 Blender에서
    /// 측정한 단면(반경·측면수)을 유지하며 중심선만 동적으로 계산 → 굵기·둥근 단면·시브 감김은 모델 그대로,
    /// 오직 늘어나는 길이만 실시간 변한다.
    ///
    /// 좌표계: 호스트(Reeving 그룹)의 월드 스케일이 1이 되도록(크레인 스케일 상쇄) 배치되어, 월드 반경이 정확히
    /// 보존된다. 정점은 월드에서 계산 후 <see cref="Transform.InverseTransformPoint"/>로 그룹 로컬로 변환한다.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/RTG Rope Tube")]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RtgRopeTube : MonoBehaviour
    {
        [SerializeField] Transform[] drumExits;   // 코너별 드럼출구(트롤리 고정)
        [SerializeField] Transform[] anchors;     // 코너별 앵커(트롤리 고정)
        [SerializeField] Transform[] sheaves;     // 코너별 시브(스프레더 — 권상 시 하강)
        [SerializeField] float[] sheaveRadii;     // 코너별 시브 반지름(월드)
        [SerializeField] MeshFilter[] filters;    // 코너별 튜브 메시 필터
        [SerializeField] float ropeRadius = 0.0011f; // 로프 튜브 반경(월드 m). 실측 0.0265 × 크레인스케일.
        [SerializeField] int tubeSides   = 32;    // 단면 측면 수(실측 32각)
        [SerializeField] int arcSegments = 14;    // 시브 감김 호 분할

        public void Configure(Transform[] drumExits, Transform[] anchors, Transform[] sheaves,
                              float[] sheaveRadii, MeshFilter[] filters,
                              float ropeRadius, int tubeSides, int arcSegments)
        {
            this.drumExits = drumExits; this.anchors = anchors; this.sheaves = sheaves;
            this.sheaveRadii = sheaveRadii; this.filters = filters;
            this.ropeRadius = Mathf.Max(1e-5f, ropeRadius);
            this.tubeSides = Mathf.Max(3, tubeSides);
            this.arcSegments = Mathf.Max(3, arcSegments);
            _dirty = true;
        }

        readonly List<Vector3> _center = new List<Vector3>(32);
        Vector3[] _lastSheave, _lastDrum, _lastAnchor;
        bool _dirty = true;

        [Tooltip("진단 로그(약 3Hz) — 시브 높이·로프 길이가 승강 따라 변하는지 콘솔 확인용. 검증 후 끄기.")]
        [SerializeField] bool logDiag = true;
        float _logNext;

        void OnEnable()  { _dirty = true; }
        void OnDestroy()
        {
            if (filters == null) return;
            foreach (var mf in filters)
            {
                if (mf == null) continue;
                var m = mf.sharedMesh;
                if (m != null && m.name.StartsWith("RopeTube")) DestroyImmediate(m);
            }
        }

        void LateUpdate()
        {
            if (filters == null || sheaves == null) return;
            int n = filters.Length;
            EnsureBuffers(n);

            for (int i = 0; i < n; i++)
            {
                var mf = filters[i];
                if (mf == null || i >= sheaves.Length || sheaves[i] == null) continue;

                Vector3 ce = sheaves[i].position;
                Vector3 de = (drumExits != null && i < drumExits.Length && drumExits[i]) ? drumExits[i].position : ce;
                Vector3 an = (anchors   != null && i < anchors.Length   && anchors[i])   ? anchors[i].position   : ce;

                // 움직이지 않았으면 재생성 생략(Quest 절약). 셋 중 하나라도 임계 이상 이동 시만 갱신.
                if (!_dirty && Approx(_lastSheave[i], ce) && Approx(_lastDrum[i], de) && Approx(_lastAnchor[i], an))
                    continue;
                _lastSheave[i] = ce; _lastDrum[i] = de; _lastAnchor[i] = an;

                float sr = (sheaveRadii != null && i < sheaveRadii.Length) ? sheaveRadii[i] : 0.02f;
                BuildCenterline(de, an, ce, sr);
                BuildTube(mf, _center);

                // 진단: 코너0의 시브 높이·로프 길이를 약 3Hz로 로그(승강 따라 변하는지 검증)
                if (logDiag && i == 0 && Time.time >= _logNext)
                {
                    _logNext = Time.time + 0.33f;
                    float len = 0f;
                    for (int p = 1; p < _center.Count; p++) len += (_center[p] - _center[p - 1]).magnitude;
                    Debug.Log($"[ROPE] sheaveY={ce.y:F3} ropeLen={len:F3}m ({_center.Count}pt) drumY={de.y:F3} anchY={an.y:F3}");
                }
            }
            _dirty = false;
        }

        // ── 리빙 중심선(월드) : 외부점→시브 진짜 접선점 + 바닥으로 감기는 호 ──────────
        // 수직에 가까운 다리는 시브 '옆면'에 접한다(중심 방향 꼭대기가 아님). 두 다리는 서로 반대편에
        // 접해 시브 하부를 ~180° 감는다. 접선점 T: 외부점 P에서 원까지 CT⊥PT (거리 L, cosθ=r/L).
        void BuildCenterline(Vector3 de, Vector3 an, Vector3 ce, float r)
        {
            _center.Clear();
            Vector3 raw1 = de - ce, raw2 = an - ce;
            Vector3 axis = Vector3.Cross(raw1, raw2);
            if (axis.sqrMagnitude < 1e-10f || raw1.sqrMagnitude < 1e-8f || raw2.sqrMagnitude < 1e-8f)
                return;
            axis.Normalize();

            // 시브 평면 내 '위' 방향(월드 업 투영) → 바닥 = -up. 감김은 바닥 통과.
            Vector3 up = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
            if (up.sqrMagnitude < 1e-8f) up = raw1 - axis * Vector3.Dot(raw1, axis); // 평면이 수평이면 폴백
            up.Normalize();
            Vector3 lat = Vector3.Cross(axis, up);                 // 평면 내 수평(좌우)

            // 두 다리가 접하는 좌우 부호(반대편이어야 하부 감김)
            float sDrum = Mathf.Sign(Vector3.Dot(raw1, lat));
            float sAnch = Mathf.Sign(Vector3.Dot(raw2, lat));
            if (Mathf.Approximately(sDrum, sAnch)) sAnch = -sDrum; // 같은 쪽이면 강제로 반대편

            Vector3 Td = TangentPoint(de, ce, r, axis, up, lat, sDrum);
            Vector3 Ta = TangentPoint(an, ce, r, axis, up, lat, sAnch);

            // Td→Ta 를 '바닥(각 ±180) 통과' 호로 잇는다(꼭대기 통과 아님).
            float aTd = AngleOf(Td - ce, up, lat);
            float aTa = AngleOf(Ta - ce, up, lat);
            float sweep = ArcThroughBottom(aTd, aTa);

            _center.Add(de);                       // 드럼출구
            _center.Add(Td);                       // 드럼쪽 접선점(옆면)
            for (int k = 1; k < arcSegments; k++)
            {
                float t = (float)k / arcSegments;
                _center.Add(ce + (Quaternion.AngleAxis(sweep * t, axis) * (Td - ce)));
            }
            _center.Add(Ta);                       // 앵커쪽 접선점(옆면)
            _center.Add(an);                       // 앵커
        }

        // 외부점 P에서 원(중심 C, 반경 r, 축 axis)으로의 접선점. side로 좌우 택1.
        static Vector3 TangentPoint(Vector3 P, Vector3 C, float r, Vector3 axis,
                                    Vector3 up, Vector3 lat, float side)
        {
            Vector3 d = P - C; d -= axis * Vector3.Dot(d, axis);   // 평면 투영
            float L = d.magnitude;
            if (L <= r * 1.0001f) return C + (L > 1e-6f ? d / L : up) * r; // 내부/근접 폴백
            Vector3 u = d / L;
            float cosT = r / L;
            float sinT = Mathf.Sqrt(Mathf.Max(0f, 1f - cosT * cosT));
            Vector3 w = Vector3.Cross(axis, u);                     // 평면 내 수직
            Vector3 t1 = C + r * (cosT * u + sinT * w);
            Vector3 t2 = C + r * (cosT * u - sinT * w);
            // side(좌우 부호)와 lat 부호가 맞는 접선점 선택
            return (Mathf.Sign(Vector3.Dot(t1 - C, lat)) == side) ? t1 : t2;
        }

        // 평면 내 각도: up=0(꼭대기), +90=lat쪽, ±180=바닥.
        static float AngleOf(Vector3 v, Vector3 up, Vector3 lat)
            => Mathf.Atan2(Vector3.Dot(v, lat), Vector3.Dot(v, up)) * Mathf.Rad2Deg;

        // aTd→aTa 중 바닥(±180) 통과하는 호의 부호있는 스윕(도).
        static float ArcThroughBottom(float aTd, float aTa)
        {
            float d = Mathf.DeltaAngle(aTd, aTa);                   // (-180,180] 짧은 쪽
            float dLong = d - Mathf.Sign(d) * 360f;                // 반대(긴) 쪽
            // 두 후보 중 '중점이 바닥(±180)에 가까운' 쪽 선택
            float m1 = Mathf.Abs(Mathf.DeltaAngle(aTd + d * 0.5f, 180f));
            float m2 = Mathf.Abs(Mathf.DeltaAngle(aTd + dLong * 0.5f, 180f));
            return (m1 <= m2) ? d : dLong;
        }

        // ── 중심선을 따라 32각 튜브 메시 생성(평행이동 프레임으로 비틀림 방지) ──────
        static readonly List<Vector3> _verts = new List<Vector3>(256);
        static readonly List<int> _tris = new List<int>(1024);
        static readonly List<Vector3> _norms = new List<Vector3>(256);

        void BuildTube(MeshFilter mf, List<Vector3> path)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || !mesh.name.StartsWith("RopeTube"))
            { mesh = new Mesh { name = "RopeTube" }; mesh.MarkDynamic(); mf.sharedMesh = mesh; }

            int m = path.Count;
            if (m < 2) { mesh.Clear(); return; }

            _verts.Clear(); _tris.Clear(); _norms.Clear();
            var t = mf.transform;

            // 첫 접선에 수직한 초기 프레임
            Vector3 tan = (path[1] - path[0]);
            if (tan.sqrMagnitude < 1e-12f) tan = Vector3.up;
            tan.Normalize();
            Vector3 nrm = Vector3.Cross(tan, Vector3.up);
            if (nrm.sqrMagnitude < 1e-8f) nrm = Vector3.Cross(tan, Vector3.right);
            nrm.Normalize();
            Vector3 bin = Vector3.Cross(tan, nrm).normalized;

            for (int i = 0; i < m; i++)
            {
                // 이 지점의 접선(중앙차분)
                Vector3 ti = (i == 0) ? path[1] - path[0]
                           : (i == m - 1) ? path[m - 1] - path[m - 2]
                           : path[i + 1] - path[i - 1];
                if (ti.sqrMagnitude < 1e-12f) ti = tan; else ti.Normalize();
                // 평행이동: 이전 프레임을 새 접선으로 회전
                Quaternion q = Quaternion.FromToRotation(tan, ti);
                nrm = (q * nrm).normalized;
                bin = Vector3.Cross(ti, nrm).normalized;
                nrm = Vector3.Cross(bin, ti).normalized;
                tan = ti;

                Vector3 cW = path[i];
                for (int s = 0; s < tubeSides; s++)
                {
                    float a = (2f * Mathf.PI * s) / tubeSides;
                    Vector3 dir = Mathf.Cos(a) * nrm + Mathf.Sin(a) * bin;
                    Vector3 wp  = cW + dir * ropeRadius;
                    _verts.Add(t.InverseTransformPoint(wp));
                    _norms.Add(t.InverseTransformDirection(dir).normalized);
                }
            }

            for (int i = 0; i < m - 1; i++)
            {
                int a = i * tubeSides, b = (i + 1) * tubeSides;
                for (int s = 0; s < tubeSides; s++)
                {
                    int s2 = (s + 1) % tubeSides;
                    _tris.Add(a + s); _tris.Add(b + s); _tris.Add(a + s2);
                    _tris.Add(a + s2); _tris.Add(b + s); _tris.Add(b + s2);
                }
            }

            mesh.Clear();
            mesh.SetVertices(_verts);
            mesh.SetNormals(_norms);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
        }

        void EnsureBuffers(int n)
        {
            if (_lastSheave == null || _lastSheave.Length != n)
            {
                _lastSheave = new Vector3[n]; _lastDrum = new Vector3[n]; _lastAnchor = new Vector3[n];
                for (int i = 0; i < n; i++) _lastSheave[i] = _lastDrum[i] = _lastAnchor[i] = new Vector3(9e9f, 9e9f, 9e9f);
                _dirty = true;
            }
        }

        static bool Approx(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-8f;
    }
}
