using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>항구 배경(먼바다·육지·산·안개, 편집 모드도 표시, DontSave 로 켜질 때마다 재생성, MR 에선 생략).
    /// ★ 자식 이름은 Backdrop_* 유지 — StsPartNames.IsNotGroundName 이 지면 오탐을 거른다. 좌표: 안벽 X0, 바다 +X.</summary>
    [ExecuteAlways]
    public class PortBackdrop : MonoBehaviour
    {
        [Tooltip("항내 해수 머티리얼(Sea_Water). 먼바다가 같은 물로 이어지게.")]
        [SerializeField] Material seaWater;
        [SerializeField] int seed = 7;

        [Header("색")]
        [SerializeField] Color landColor  = new(0.24f, 0.30f, 0.17f);
        [SerializeField] Color treeColor  = new(0.10f, 0.20f, 0.09f);
        [SerializeField] Color hillColor  = new(0.20f, 0.26f, 0.19f);
        [SerializeField] Color fogColor   = new(0.70f, 0.78f, 0.85f);   // 스카이박스 지평선 색에 맞출 것

        [Header("안개 — 실척 m")]
        [SerializeField] float fogStartM = 1500f;
        [SerializeField] float fogEndM   = 9000f;

        // 치수 — 실척 m. 안개 끝(9km)보다 바다·육지가 넓어야 가장자리가 안 보인다.
        const float WorldHalfM   = 10000f;
        const float RidgeInnerM  = 2500f, RidgeOuterM = 5500f;   // 산 능선 링
        const float RidgeMinH    = 250f,  RidgeMaxH   = 850f;
        const float ForestNearM  = 140f;                          // 야드 끝(~90m) 너머부터
        const float ForestFarM   = 2500f;
        const int   TreeCap      = 2000;                          // 6정점 × 2000 = 12k — 안개 속 먼 나무는 몇 픽셀이라 더 늘려도 안 보인다

        // 부두 불투명(2000)보다 늦게 그린다 — 부두 밑에 깔린 육지·먼바다가 깊이 테스트에서 버려져 오버드로가 없다.
        // 스카이박스(2500)보다는 먼저.
        const int   LateQueue    = 2450;

        // 생성물이 씬에 저장되지 않고, 편집 모드에서 실수로 고쳐 저장했다고 믿는 일이 없게.
        const HideFlags Generated = HideFlags.DontSave | HideFlags.NotEditable;

        static float S => StsConfig.ModelScale;

        readonly System.Collections.Generic.List<Object> made = new();

        void OnEnable()  => Rebuild();
        void OnDisable() => Clear();

#if UNITY_EDITOR
        // OnValidate 안에서는 오브젝트를 만들 수 없다 — 한 틱 미룬다.
        void OnValidate() => UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Rebuild(); };
#endif

        void Clear()
        {
            foreach (var o in made)
                if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
            made.Clear();
        }

        void Rebuild()
        {
            Clear();
            var cam = Application.isPlaying ? Camera.main : null;
            if (cam != null && cam.clearFlags != CameraClearFlags.Skybox) { RenderSettings.fog = false; return; }

            // 씬에도 안개를 켜 둬야 빌드에서 안개 셰이더 변형이 안 지워진다(그래픽 설정 Fog Automatic).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStartM * S;
            RenderSettings.fogEndDistance = fogEndM * S;
            if (cam != null && cam.farClipPlane < WorldHalfM * S) cam.farClipPlane = WorldHalfM * S;

            var rnd = new System.Random(seed);
            float seaY  = StsConfig.SeaLevelY;
            float landY = -0.1f * S;   // 데크(0) 바로 아래 — 부두 포장에 가려진다

            // 먼바다 — 항내 바다(Sea_Body)보다 10cm 아래에 깔아 겹치는 곳 Z-fighting 회피.
            var seaMat = seaWater != null ? Keep(new Material(seaWater) { renderQueue = LateQueue }) : Mat(fogColor);
            var sea = Prim(PrimitiveType.Plane, "Backdrop_Sea", seaMat);
            sea.localPosition = new Vector3(0f, seaY - 0.1f * S, 0f);
            sea.localScale = Vector3.one * (2f * WorldHalfM * S / 10f);   // Plane = 10 단위

            // 육지 — 안벽 뒤(−X) 반쪽. 윗면 landY, 수면 아래 1m 까지.
            var land = Prim(PrimitiveType.Cube, "Backdrop_Land", Mat(landColor));
            float top = landY, bottom = seaY - 1f * S, x0 = -WorldHalfM * S, x1 = -PortConfig.SeaOverlapM * S;
            land.localPosition = new Vector3((x0 + x1) * 0.5f, (top + bottom) * 0.5f, 0f);
            land.localScale = new Vector3(x1 - x0, top - bottom, 2f * WorldHalfM * S);

            Spawn("Backdrop_Ridge", Ridge(rnd, seaY), Mat(hillColor));
            Spawn("Backdrop_Forest", Forest(rnd, landY), Mat(treeColor));
        }

        /// <summary>생성물 등록 — 씬 저장 제외 + Clear 대상.</summary>
        T Keep<T>(T o) where T : Object
        {
            o.hideFlags = Generated;
            made.Add(o);
            return o;
        }

        /// <summary>육지쪽을 감싸는 산 능선 — 바다쪽 ±70° 는 비워 수평선이 트이게. 단면 4점(기슭→봉우리→뒷면).</summary>
        Mesh Ridge(System.Random rnd, float baseY)
        {
            const int N = 160;
            const float a0 = 70f * Mathf.Deg2Rad, a1 = 290f * Mathf.Deg2Rad;   // +X 기준, 육지(180°) 쪽 호
            float[] rf = { 0f, 0.35f, 0.6f, 1f };                              // 반경 비율
            float[] hf = { 0f, 0.55f, 1f, 0.5f };                              // 높이 비율
            float off = (float)rnd.NextDouble() * 100f;
            var v = new Vector3[(N + 1) * 4];
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N, a = Mathf.Lerp(a0, a1, t);
                // 양끝(곶)은 낮게 — 수평선과 자연스럽게 만나게.
                float taper = Mathf.Sin(t * Mathf.PI);
                float h = Mathf.Lerp(RidgeMinH, RidgeMaxH, Mathf.PerlinNoise(off + t * 9f, 0.5f)) * (0.3f + 0.7f * taper);
                h *= 0.8f + 0.4f * Mathf.PerlinNoise(off + t * 40f, 3.3f);     // 잔봉우리
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < 4; k++)
                {
                    float r = Mathf.Lerp(RidgeInnerM, RidgeOuterM, rf[k]);
                    v[i * 4 + k] = dir * (r * S) + Vector3.up * (baseY + h * hf[k] * S);
                }
            }
            var tri = new int[N * 3 * 6];
            for (int i = 0, n = 0; i < N; i++)
                for (int k = 0; k < 3; k++)
                {
                    int p = i * 4 + k, q = p + 4;
                    tri[n++] = p; tri[n++] = q; tri[n++] = p + 1;   // 시계방향 = 안쪽(부두)을 본다
                    tri[n++] = q; tri[n++] = q + 1; tri[n++] = p + 1;
                }
            return Build(v, tri);
        }

        /// <summary>침엽수 원뿔 — 노이즈로 군락을 만들고 부두·야드 자리는 비운다.</summary>
        Mesh Forest(System.Random rnd, float groundY)
        {
            const int Sides = 5;
            var v = new System.Collections.Generic.List<Vector3>();
            var tri = new System.Collections.Generic.List<int>();
            float off = (float)rnd.NextDouble() * 100f;
            float portHalfZ = PortConfig.BerthLengthMeters * 0.5f + 40f;
            for (int tries = 0; tries < TreeCap * 8 && v.Count < TreeCap * (Sides + 1); tries++)
            {
                float x = -Mathf.Lerp(ForestNearM, ForestFarM, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-ForestFarM, ForestFarM, (float)rnd.NextDouble());
                if (x > -ForestNearM - 60f && Mathf.Abs(z) < portHalfZ) continue;      // 야드 뒤 공터
                if (Mathf.PerlinNoise(off + x * 0.003f, off + z * 0.003f) < 0.5f) continue; // 군락 사이 들판
                float h = Mathf.Lerp(9f, 20f, (float)rnd.NextDouble()), r = h * 0.28f;
                var b = new Vector3(x, 0f, z) * S + Vector3.up * groundY;
                int at = v.Count;
                v.Add(b + Vector3.up * (h * S));
                for (int s = 0; s < Sides; s++)
                {
                    float a = (s + (float)rnd.NextDouble() * 0.3f) * Mathf.PI * 2f / Sides;
                    v.Add(b + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * S));
                    tri.Add(at); tri.Add(at + 1 + (s + 1) % Sides); tri.Add(at + 1 + s);
                }
            }
            return Build(v.ToArray(), tri.ToArray());
        }

        static Mesh Build(Vector3[] v, int[] tri)
        {
            var m = new Mesh { vertices = v, triangles = tri };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        void Spawn(string name, Mesh mesh, Material mat)
        {
            var go = Keep(new GameObject(name));
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Keep(mesh);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            Cheap(mr);
            mesh.UploadMeshData(true);   // 다시 안 고친다 — CPU 사본 해제
        }

        Transform Prim(PrimitiveType type, string name, Material mat)
        {
            var go = Keep(GameObject.CreatePrimitive(type));
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());   // 배경은 밟지 않는다 — 텔레포트·지면 레이캐스트에 안 걸리게
            go.transform.SetParent(transform, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            Cheap(mr);
            return go.transform;
        }

        // 모바일(Beam Pro/Quest) 비용 — 배경은 화면 대부분을 덮으니 픽셀당 비용이 크다.
        // 그림자: 수 km 밖은 그림자 거리 밖이라 드리울 일도, 받을 일도 없다 — 받기를 끄면 그림자 맵 샘플링이 빠진다.
        static void Cheap(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        // 항내 바다와 같은 셰이더(URP/Lit)를 쓴다 — 빌드에 포함이 보장된다.
        Material Mat(Color c)
        {
            var sh = seaWater != null ? seaWater.shader : Shader.Find("Universal Render Pipeline/Lit");
            var m = Keep(new Material(sh));
            m.SetColor("_BaseColor", c);
            m.renderQueue = LateQueue;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            return m;
        }
    }
}
