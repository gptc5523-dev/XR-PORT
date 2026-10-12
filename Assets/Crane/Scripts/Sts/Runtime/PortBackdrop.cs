using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>항구 배경(먼바다·육지·배후 항만(도로·가건물)·산·안개, 편집 모드도 표시, DontSave 로 켜질 때마다 재생성, MR 에선 생략).
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
        [SerializeField] Color paveColor  = new(0.27f, 0.27f, 0.26f);   // 부두 아스팔트와 이어지게
        [SerializeField] Color roadColor  = new(0.16f, 0.16f, 0.16f);
        [SerializeField] Color lineColor  = new(0.85f, 0.85f, 0.80f);
        [SerializeField] Color steelColor = new(0.62f, 0.66f, 0.70f);   // 먼 크레인·조명탑
        [SerializeField] Color lampColor  = new(0.95f, 0.92f, 0.78f);   // 등기구 — 낮 배경이라 발광 없이 밝은 색만
        [SerializeField] Color[] wallColors = { new(0.78f, 0.78f, 0.75f), new(0.80f, 0.74f, 0.62f), new(0.62f, 0.70f, 0.76f) };
        [SerializeField] Color[] roofColors = { new(0.20f, 0.33f, 0.52f), new(0.25f, 0.42f, 0.32f), new(0.38f, 0.38f, 0.38f) };
        [SerializeField] Color[] boxColors  = { new(0.55f, 0.16f, 0.12f), new(0.13f, 0.27f, 0.52f), new(0.72f, 0.40f, 0.13f) };

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
        // 배후 항만 띠 — 안벽을 따라 ±1.2km, 안쪽 700m 까지만. 그 너머는 들판·숲 그대로.
        const float BeltInnerM   = 180f, BeltOuterM = 700f, BeltHalfZM = 1200f;
        const float CellZM       = 300f, RoadM      = 18f;        // 도로 격자 — 안벽과 나란한 도로 3개 + 300m 마다 가로 도로
        const float YardBackRoadM = -92f;                         // 우리 야드(포장 끝 X −84) 바로 뒤 — 야드를 배후 도로망에 잇는다

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
            Port(rnd, landY);
#if UNITY_EDITOR
            // 편집 모드 재생성 뒤 GPU Resident Drawer 가 지운 머티리얼로 그려 분홍이 된다 — 다시 만든다(CraneWinBuild 와 같은 internal 호출). 빌드는 시작 때 한 번이라 무관.
            if (!Application.isPlaying)
                typeof(UnityEngine.Rendering.GPUResidentDrawer).GetMethod("Reinitialize", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.Invoke(null, null);
#endif
        }

        /// <summary>배후 항만 — 포장 띠 + 장치장 컨테이너 더미(색별 메시) + 다른 선석의 STS 실루엣·조명탑. 걸을 수 없는 그림이다.</summary>
        void Port(System.Random rnd, float groundY)
        {
            // 포장 — 육지 윗면보다 5cm 위(부두 데크 0 아래라 부두 밑은 가려진다).
            var pave = Prim(PrimitiveType.Cube, "Backdrop_Pave", Mat(paveColor));
            float px0 = -BeltOuterM * S, px1 = -PortConfig.SeaOverlapM * S;
            pave.localPosition = new Vector3((px0 + px1) * 0.5f, groundY + 0.025f * S, 0f);
            pave.localScale = new Vector3(px1 - px0, 0.05f * S, 2f * BeltHalfZM * S);
            float y0 = groundY + 0.05f * S;   // 포장 윗면(모델 단위)

            BoxSet[] Sets(int n) { var r = new BoxSet[n]; for (int i = 0; i < n; i++) r[i] = new BoxSet(); return r; }
            var walls = Sets(wallColors.Length); var roofs = Sets(roofColors.Length); var boxes = Sets(boxColors.Length);
            var steel = new BoxSet(); var road = new BoxSet(); var line = new BoxSet(); var lamp = new BoxSet();
            float portHalfZ = PortConfig.BerthLengthMeters * 0.5f + 40f;

            // 도로 — 안벽과 나란히(X 고정) 2개, 가로(Z 고정) 300m 마다. 가운데 흰 점선 대신 실선 하나(멀리선 같다).
            float[] roadX = { -BeltInnerM + RoadM * 0.5f, -(BeltInnerM + BeltOuterM) * 0.5f };
            foreach (float x in new[] { YardBackRoadM, roadX[0], roadX[1] })
            {
                road.Add(new Vector3(x, 0.01f, 0f), new Vector3(RoadM, 0.02f, 2f * BeltHalfZM), y0);
                line.Add(new Vector3(x, 0.025f, 0f), new Vector3(0.3f, 0.02f, 2f * BeltHalfZM), y0);
                // 가로등 — 양쪽 번갈아 50m 마다, 기둥 10m + 도로 쪽 팔 + 등기구.
                for (float z = -BeltHalfZM + 25f, side = 1f; z < BeltHalfZM; z += 50f, side = -side)
                {
                    float px = x + side * (RoadM * 0.5f + 1f);
                    if (px > YardBackRoadM + RoadM * 0.5f && Mathf.Abs(z) < portHalfZ) continue;   // 우리 야드 안에는 안 세운다
                    steel.Add(new Vector3(px, 5f, z), new Vector3(0.25f, 10f, 0.25f), y0);
                    steel.Add(new Vector3(px - side * 1.4f, 9.9f, z), new Vector3(2.8f, 0.15f, 0.15f), y0);
                    lamp.Add(new Vector3(px - side * 2.6f, 9.75f, z), new Vector3(0.9f, 0.25f, 0.4f), y0);
                }
            }
            // 가로 도로 — 야드 뒤 도로부터 띠 끝까지. z=0 줄이 우리 야드 정문 길이 된다.
            float cx0 = YardBackRoadM, cxL = BeltOuterM + cx0;
            for (float z = -BeltHalfZM + CellZM; z < BeltHalfZM; z += CellZM)
            {
                road.Add(new Vector3(cx0 - cxL * 0.5f, 0.012f, z), new Vector3(cxL, 0.02f, RoadM), y0);
                line.Add(new Vector3(cx0 - cxL * 0.5f, 0.027f, z), new Vector3(cxL, 0.02f, 0.3f), y0);
            }

            // 구획마다 가건물 1~3채(창고·사무동·정비고), 가끔 컨테이너 몇 줄, 나머지는 공터.
            float[] bandX0 = { roadX[0] - RoadM * 0.5f - 10f, roadX[1] - RoadM * 0.5f - 10f };   // 구획 안벽쪽 끝
            float bandW = (roadX[0] - roadX[1]) - RoadM - 20f;
            foreach (float x0 in bandX0)
                for (float z0 = -BeltHalfZM + 10f; z0 + CellZM - RoadM - 20f <= BeltHalfZM; z0 += CellZM)
                {
                    double pick = rnd.NextDouble();
                    float cellL = CellZM - RoadM - 20f;
                    if (pick < 0.2) continue;                                   // 공터
                    if (pick < 0.35)                                            // 컨테이너 몇 줄
                    {
                        for (int row = 0; row < 4; row++)
                            for (float z = z0; z < z0 + cellL * 0.6f; z += 24.4f)
                            {
                                int tiers = rnd.Next(1, 4);
                                boxes[rnd.Next(boxes.Length)].Add(new Vector3(x0 - 5f - row * 2.6f, tiers * 1.3f, z + 12.2f),
                                                                  new Vector3(2.44f, tiers * 2.6f, 23.8f), y0);
                            }
                        continue;
                    }
                    int n = rnd.Next(1, 4);
                    float zc = z0;
                    for (int k = 0; k < n && zc < z0 + cellL - 30f; k++)
                    {
                        // 창고(길고 낮다) · 사무동(작고 높다) · 정비고(중간)
                        int kind = rnd.Next(3);
                        float w = kind == 0 ? Mathf.Lerp(40f, 70f, (float)rnd.NextDouble()) : kind == 1 ? Mathf.Lerp(15f, 22f, (float)rnd.NextDouble()) : Mathf.Lerp(25f, 40f, (float)rnd.NextDouble());
                        float l = kind == 0 ? Mathf.Lerp(80f, 140f, (float)rnd.NextDouble()) : kind == 1 ? Mathf.Lerp(25f, 40f, (float)rnd.NextDouble()) : Mathf.Lerp(35f, 60f, (float)rnd.NextDouble());
                        float h = kind == 0 ? Mathf.Lerp(10f, 14f, (float)rnd.NextDouble()) : kind == 1 ? Mathf.Lerp(12f, 24f, (float)rnd.NextDouble()) : Mathf.Lerp(8f, 12f, (float)rnd.NextDouble());
                        w = Mathf.Min(w, bandW); l = Mathf.Min(l, z0 + cellL - zc);
                        float cx = x0 - (float)rnd.NextDouble() * (bandW - w) - w * 0.5f, cz = zc + l * 0.5f;
                        walls[rnd.Next(walls.Length)].Add(new Vector3(cx, h * 0.5f, cz), new Vector3(w, h, l), y0);
                        roofs[rnd.Next(roofs.Length)].Add(new Vector3(cx, h + 0.4f, cz), new Vector3(w + 1f, 0.8f, l + 1f), y0);
                        zc += l + Mathf.Lerp(15f, 40f, (float)rnd.NextDouble());
                    }
                    HighMast(steel, lamp, new Vector3(x0 + 4f, 0f, z0), y0);
                }
            // 다른 선석 STS — 우리 STS 의 모양만 구워 안벽을 따라 양옆에 세운다(스크립트·충돌 없음, 안 움직임).
            var sts = GameObject.Find(StsPartNames.StsCraneRoot);
            if (sts != null)
            {
                var mesh = BakeShape(sts.transform, out var mats);
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 4; i++)
                    {
                        float z = side * (portHalfZ + 120f + i * 95f + (float)rnd.NextDouble() * 20f);
                        var go = Keep(new GameObject("Backdrop_Sts"));
                        go.transform.SetParent(transform, false);
                        go.transform.SetPositionAndRotation(new Vector3(sts.transform.position.x, sts.transform.position.y, z * S), sts.transform.rotation);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = mats;   // 원본 에셋 머티리얼 공유 — Keep 하지 않는다(Clear 가 지우면 안 됨)
                        Cheap(mr);
                    }
            }

            for (int i = 0; i < walls.Length; i++) Spawn("Backdrop_Wall" + i, walls[i].Mesh(), Mat(wallColors[i]));
            for (int i = 0; i < roofs.Length; i++) Spawn("Backdrop_Roof" + i, roofs[i].Mesh(), Mat(roofColors[i]));
            for (int i = 0; i < boxes.Length; i++) Spawn("Backdrop_Stacks" + i, boxes[i].Mesh(), Mat(boxColors[i]));
            Spawn("Backdrop_Road", road.Mesh(), Mat(roadColor));
            Spawn("Backdrop_RoadLine", line.Mesh(), Mat(lineColor));
            Spawn("Backdrop_Steel", steel.Mesh(), Mat(steelColor));
            Spawn("Backdrop_Lamp", lamp.Mesh(), Mat(lampColor));
        }

        /// <summary>크레인 모양 굽기 — 실척 2m 미만 부품(볼트·난간 등, 100m 밖에선 1px 미만)은 빼고 머티리얼별 서브메시 하나로.
        /// 실척 2m 이상만 남기면 STS 21만 → 2만 정점.</summary>
        Mesh BakeShape(Transform root, out Material[] mats)
        {
            var byMat = new System.Collections.Generic.Dictionary<Material, System.Collections.Generic.List<CombineInstance>>();
            var w2l = root.worldToLocalMatrix;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!r.enabled || mf == null || mf.sharedMesh == null) continue;
                var e = r.bounds.size / S;
                if (Mathf.Max(e.x, Mathf.Max(e.y, e.z)) < 2f) continue;
                var mesh = mf.sharedMesh;
                for (int i = 0; i < mesh.subMeshCount && i < r.sharedMaterials.Length; i++)
                {
                    var m = r.sharedMaterials[i];
                    if (m == null) continue;
                    if (!byMat.TryGetValue(m, out var list)) byMat[m] = list = new();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = w2l * r.localToWorldMatrix });
                }
            }
            mats = new Material[byMat.Count];
            var parts = new CombineInstance[byMat.Count];
            int k = 0;
            foreach (var kv in byMat)
            {
                var part = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                part.CombineMeshes(kv.Value.ToArray(), true, true);
                parts[k] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
                mats[k++] = kv.Key;
            }
            var all = Keep(new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "Backdrop_StsShape" });
            all.CombineMeshes(parts, false, false);
            foreach (var p in parts) DestroyImmediate(p.mesh);
            all.RecalculateBounds();
            all.UploadMeshData(true);
            return all;
        }

        /// <summary>항만 조명탑(하이마스트) — 위로 갈수록 가는 3단 기둥 35m, 꼭대기 사각 링 + 링 둘레 투광등 8개.</summary>
        static void HighMast(BoxSet steel, BoxSet lamp, Vector3 p, float y0)
        {
            steel.Add(p + new Vector3(0f, 6f, 0f),     new Vector3(1.4f, 12f, 1.4f), y0);
            steel.Add(p + new Vector3(0f, 18f, 0f),    new Vector3(1.0f, 12f, 1.0f), y0);
            steel.Add(p + new Vector3(0f, 29.5f, 0f),  new Vector3(0.7f, 11f, 0.7f), y0);
            const float R = 2.2f, top = 35f;
            foreach (float s in new[] { -R, R })
            {
                steel.Add(p + new Vector3(s, top, 0f), new Vector3(0.3f, 0.5f, 2f * R + 0.3f), y0);
                steel.Add(p + new Vector3(0f, top, s), new Vector3(2f * R + 0.3f, 0.5f, 0.3f), y0);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                lamp.Add(p + new Vector3(Mathf.Cos(a) * (R + 0.4f), top - 0.6f, Mathf.Sin(a) * (R + 0.4f)), new Vector3(0.9f, 0.7f, 0.9f), y0);
            }
        }

        /// <summary>상자 모음 — 면마다 정점을 따로 둬(24정점) 각진 음영. 입력은 실척 m, y 는 바닥 기준.</summary>
        sealed class BoxSet
        {
            readonly System.Collections.Generic.List<Vector3> v = new();
            readonly System.Collections.Generic.List<int> t = new();
            static readonly Vector3[] Corner =
            {
                new(-1,-1,-1), new(1,-1,-1), new(1,1,-1), new(-1,1,-1), new(-1,-1,1), new(1,-1,1), new(1,1,1), new(-1,1,1),
            };
            static readonly int[] Face = { 0,3,2,1, 4,5,6,7, 0,4,7,3, 1,2,6,5, 3,7,6,2, 0,1,5,4 };   // 바깥을 보는 시계방향

            public void Add(Vector3 c, Vector3 size, float y0)
            {
                for (int f = 0; f < 6; f++)
                {
                    int at = v.Count;
                    for (int k = 0; k < 4; k++)
                        v.Add(Vector3.Scale(Corner[Face[f * 4 + k]] * 0.5f, size) * S + c * S + Vector3.up * y0);
                    t.Add(at); t.Add(at + 1); t.Add(at + 2); t.Add(at); t.Add(at + 2); t.Add(at + 3);
                }
            }

            public Mesh Mesh() => Build(v.ToArray(), t.ToArray());
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
                if (x > -BeltOuterM - 30f && Mathf.Abs(z) < BeltHalfZM + 30f) continue; // 배후 항만 띠
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
