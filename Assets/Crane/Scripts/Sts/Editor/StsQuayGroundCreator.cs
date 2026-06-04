#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// STS 크레인이 서는 부두(quay) 아스팔트 바닥을 절차적으로 생성.
    ///
    /// - 슬래브 윗면 = y 0 (VirtualFloor 윗면·크레인 접지면과 동일 평면).
    /// - 아스팔트 텍스처는 절차 생성: 저주파 얼룩(시일코트) + 골재 그레인 + 드문드문 밝은 골재.
    ///   매트(거친) PBR로 빛 반사 최소.
    /// - 씬에 STS_Crane 이 있으면 그 XZ 바운즈에 여유를 둬 자동으로 크기·위치를 맞추고,
    ///   레일(Rail_*)을 찾아 양옆에 노란 안전 차선을 그린다. 없으면 원점에 기본 크기로 생성.
    /// - 시각 전용(콜라이더 없음) — 물리 바닥은 기존 VirtualFloor 가 담당.
    ///
    /// 생성 머티리얼/텍스처는 인스턴스(에셋 미저장)라 프로젝트 머티리얼을 오염시키지 않는다.
    /// </summary>
    public static class StsQuayGroundCreator
    {
        const string RootName  = "Quay_Ground";
        const string CraneName = "STS_Crane";

        // 기본 치수(크레인을 못 찾을 때) — X=apron(붐 방향), Z=안벽 길이(레일 방향), m
        const float DefaultSizeX = 5.0f;    // 3.0→5.0 (실척 ≈120m)
        const float DefaultSizeZ = 2.4f;    // Z는 원복
        const float MarginX      = 2.0f;    // X(붐 방향) 여유 — 0.8→2.0 (실척 ≈48m 사방 추가, 사용자 요청으로 X만 확대)
        const float MarginZ      = 0.8f;    // Z(레일 방향) 여유 — 원래대로
        const float Thickness    = 0.08f;   // 슬래브 두께(가장자리 단차)
        const float TileMeters   = 0.5f;    // 아스팔트 텍스처 1타일 = 0.5m

        const float RailGauge    = 15f / 24f; // 크레인 레일 게이지(StsCraneCreator.LegSpanX=15)와 동일 — 크레인 없을 때 폴백 차선용
        const float LaneOffset   = 0.045f;   // 레일 중심에서 차선까지(양옆)
        const float LaneWidth    = 0.014f;   // 차선 폭
        const float LaneY        = 0.0016f;  // 아스팔트 윗면 바로 위(z-fighting 방지)

        static readonly Color CAsphalt = new Color(0.205f, 0.20f, 0.215f); // 어두운 중성 회색
        static readonly Color CPaint   = new Color(0.88f, 0.74f, 0.10f);   // 안전 노랑 차선
        static readonly Color CRail    = new Color(0.55f, 0.58f, 0.62f);   // 강철 레일(크레인 Rail 색과 동일)

        const float RailY      = 0.004f;   // 레일 중심 Y(크레인 짧은 레일과 일치)
        const float RailH      = 0.008f;   // 레일 단면 높이
        const float RailW      = 0.020f;   // 레일 단면 폭(X)

        static Mesh _unitQuad;

        [MenuItem("Container/부두 바닥 생성", false, 1)]
        public static void CreateFromMenu()
        {
            var prev = GameObject.Find(RootName);
            if (prev != null) Undo.DestroyObjectImmediate(prev);

            // 크레인이 있으면 그 XZ 바운즈에 맞춰 크기·중심을 잡는다. 갠트리 주행 범위가 있으면 Z로 그만큼 더 길게.
            Vector3 center = Vector3.zero;
            float sizeX = DefaultSizeX, sizeZ = DefaultSizeZ;
            float gantryRange = 0f;
            var crane = GameObject.Find(CraneName);
            if (crane != null)
            {
                var gantry = crane.GetComponent<GantryMover>();
                if (gantry != null) gantryRange = gantry.Max - gantry.Min;

                if (TryWorldBounds(crane.transform, out Bounds b))
                {
                    // Z 중심: 갠트리가 있으면 주행 범위 중간(=초기 위치), 없으면 현재 바운즈 중심
                    float centerZ = gantry != null ? (gantry.Min + gantry.Max) * 0.5f : b.center.z;
                    center = new Vector3(b.center.x, 0f, centerZ);
                    sizeX = Mathf.Max(DefaultSizeX, b.size.x + MarginX * 2f);
                    sizeZ = Mathf.Max(DefaultSizeZ, b.size.z + MarginZ * 2f + gantryRange);
                }
            }

            var root = Build(center, sizeX, sizeZ, crane);
            Undo.RegisterCreatedObjectUndo(root, "Create Quay Ground");
            Selection.activeGameObject = root;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
            Debug.Log($"[QuayGround] 아스팔트 부두 바닥 생성 — {sizeX:F2}m × {sizeZ:F2}m (갠트리 주행 {gantryRange:F2}m 반영), 윗면 y=0, 중심 {center}. " +
                      (crane != null ? "크레인 레일에 맞춰 긴 트랙 + 안전 차선 표시." : "크레인 없음 → 기본 차선."));
        }

        static GameObject Build(Vector3 center, float sizeX, float sizeZ, GameObject crane)
        {
            var root = new GameObject(RootName);
            root.transform.position = center;

            // 1) 아스팔트 슬래브 — 윗면 y=0, 아래로 Thickness
            var slab = new GameObject("Asphalt");
            slab.transform.SetParent(root.transform, worldPositionStays: false);
            slab.AddComponent<MeshFilter>().sharedMesh = BuildSlab(sizeX, sizeZ, Thickness, TileMeters);
            slab.AddComponent<MeshRenderer>().sharedMaterial =
                MakeMat(CAsphalt, metallic: 0.0f, smooth: 0.12f, tex: BuildAsphaltTexture());

            // 2) 레일 안전 차선(노랑) — 크레인 레일 X를 찾아 양옆에, 없으면 기본 게이지
            var lanesX = new List<float>();
            if (crane != null)
            {
                foreach (var t in crane.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Rail_")) lanesX.Add(t.position.x - center.x);
            }
            if (lanesX.Count == 0) { lanesX.Add(-RailGauge * 0.5f); lanesX.Add(RailGauge * 0.5f); }

            var paint = MakeMat(CPaint, metallic: 0.0f, smooth: 0.25f, tex: null);
            foreach (float lx in lanesX)
                foreach (float off in new[] { -LaneOffset, LaneOffset })
                    Stripe(root.transform, "Lane", new Vector3(lx + off, LaneY, 0f),
                           LaneWidth, sizeZ * 0.96f, paint);

            // 3) 고정 긴 레일(트랙) — 부두에 고정되어 있고 크레인이 그 위를 굴러간다(크레인 측 Rail_*는 렌더러 OFF).
            //    크레인의 Rail_ Transform이 정해준 X를 그대로 사용해 정렬.
            if (lanesX.Count > 0)
            {
                var railMat = MakeMat(CRail, metallic: 0.7f, smooth: 0.35f, tex: null);
                float trackLen = sizeZ * 0.98f;
                foreach (float lx in lanesX)
                    QuayBox(root.transform, "QuayRail", new Vector3(lx, RailY, 0f),
                            new Vector3(RailW, RailH, trackLen), railMat);
            }

            return root;
        }

        // 단순 박스(콜라이더 없음) — 고정 트랙 레일용
        static void QuayBox(Transform parent, string name, Vector3 localCenter, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // 윗면이 y=0, 아래로 th 만큼 두께를 가진 슬래브 박스 메시. 윗면 UV 는 타일 반복.
        static Mesh BuildSlab(float sx, float sz, float th, float tile)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            float ux = sx / tile, uz = sz / tile, ut = th / tile;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();

            // Top (+Y) — 보이는 면, 타일 UV
            AddQuad(v, uv, t,
                new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, uz), new Vector2(0, uz), Vector3.up);
            // Bottom (-Y)
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, -hz), new Vector3(hx, -th, -hz), new Vector3(hx, -th, hz), new Vector3(-hx, -th, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, uz), new Vector2(0, uz), Vector3.down);
            // 4 측면(가장자리 단차)
            AddQuad(v, uv, t,
                new Vector3(hx, -th, -hz), new Vector3(hx, -th, hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(uz, 0), new Vector2(uz, ut), new Vector2(0, ut), Vector3.right);
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, -hz), new Vector3(-hx, -th, hz), new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(uz, 0), new Vector2(uz, ut), new Vector2(0, ut), Vector3.left);
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, hz), new Vector3(hx, -th, hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, ut), new Vector2(0, ut), Vector3.forward);
            AddQuad(v, uv, t,
                new Vector3(-hx, -th, -hz), new Vector3(hx, -th, -hz), new Vector3(hx, 0f, -hz), new Vector3(-hx, 0f, -hz),
                new Vector2(0, 0), new Vector2(ux, 0), new Vector2(ux, ut), new Vector2(0, ut), Vector3.back);

            var m = new Mesh { name = "Quay_Slab" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // 노란 차선 1줄 — 단위 쿼드를 눕혀 스케일.
        static void Stripe(Transform parent, string name, Vector3 localCenter, float w, float len, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = new Vector3(w, 1f, len);
            go.AddComponent<MeshFilter>().sharedMesh = UnitQuad();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // 윗면을 향한 1×1 단위 쿼드(중심 원점, y=0).
        static Mesh UnitQuad()
        {
            if (_unitQuad != null) return _unitQuad;
            var m = new Mesh { name = "Quay_UnitQuad" };
            m.SetVertices(new List<Vector3> {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),   new Vector3(-0.5f, 0f, 0.5f) });
            m.SetUVs(0, new List<Vector2> {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return _unitQuad = m;
        }

        // outward 기준 와인딩 자동 보정 + UV.
        static void AddQuad(List<Vector3> v, List<Vector2> uv, List<int> t,
                            Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 outward)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            else
            { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        }

        static Material MakeMat(Color c, float metallic, float smooth, Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "Quay_Mat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            return mat;
        }

        // 절차 아스팔트 텍스처(그레이스케일, _BaseColor 곱) — 저주파 얼룩 + 골재 그레인 + 드문 밝은 골재.
        static Texture2D BuildAsphaltTexture()
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Quay_Asphalt", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 시일코트 불균일(저주파 얼룩)
                float patch = Mathf.PerlinNoise(x * 0.012f, y * 0.012f) * 0.5f
                            + Mathf.PerlinNoise(x * 0.030f + 50f, y * 0.030f) * 0.3f
                            + Mathf.PerlinNoise(x * 0.080f + 120f, y * 0.080f) * 0.2f;
                // 골재 그레인(미세 스페클)
                float grain = Hash(x, y);
                // 드문드문 밝은 골재(돌)
                float stone = grain > 0.94f ? 0.18f : 0f;

                float v = 0.86f + (patch - 0.5f) * 0.22f + (grain - 0.5f) * 0.26f + stone;
                byte bb = (byte)(Mathf.Clamp01(v) * 255f);
                px[y * N + x] = new Color32(bb, bb, bb, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // 자식 Renderer들의 월드 AABB.
        static bool TryWorldBounds(Transform t, out Bounds b)
        {
            b = default;
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }

        // 결정적 정수 해시 → [0,1) (그레인용)
        static float Hash(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
        }
    }
}
#endif
