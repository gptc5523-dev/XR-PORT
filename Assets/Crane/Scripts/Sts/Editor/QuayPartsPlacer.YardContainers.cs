#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using AIXRCrane.EditorTools;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class QuayPartsPlacer
    {
        // ═══ 야드 적재 — 정밀 FBX(LOD0)+LOD1을 LODGroup으로, 대수가 곧 렌더 예산 ═══
        //   규약(항구 부재와 반대): 루트 자체 스케일 유지, 길이=Z축, 피봇=중앙 높이(y=높이/2).
        const string ModelDir = AIXRCrane.EditorTools.ContainerModelPostprocessor.ModelDir;

        // 화면 상대 높이 임계값 — ShipCreator.CargoLod0/1Height 와 같은 값·같은 결정(그쪽 주석 참고, 문서/컨테이너_통합.md Part 6.2).
        const float ContLod0Height = 0.3787f;   // 이보다 크게 보이면 정밀본
        const float ContLod1Height = 0.0229f;   // 이보다 작으면 컬링
        /// <summary>배치 무늬 시드 — 같은 값이면 같은 무늬.</summary>
        const int    YardSeed    = 20260907;
        const float  Yard20ftGapM = YardGrid.Gap20ftM;   // 한 베이 안 20ft 두 개 사이 틈 — 실척 m

        sealed class YardKind
        {
            public string prefix, model;        // LOD1 = ModelDir/LOD1/{model}_LOD1.fbx (문서/스크립트/컨테이너_LOD1_내보내기.py)
            public float heightM, lengthM;      // ISO 668 실척 m
            public int count;
            public GameObject hi, lo;
            public float h, len;                 // 실측(모델 단위)
        }

        // 야드 4종 — 놓는 순서 = 제약이 큰 것부터(45ft 앞뒤 비우기 → 20ft 쌍 → 나머지로 블록 균형). 대수는 실제 항만 비율(40HC 최다).
        static YardKind[] YardKinds() => new[]
        {
            new YardKind { prefix = StsPartNames.Yard45HCPrefix, model = "Container_45ftHC", heightM = ProceduralContainerMesh.HeightHC,  lengthM = ProceduralContainerMesh.Length45ft, count = 2 },
            new YardKind { prefix = StsPartNames.Yard20Prefix,   model = "Container_20ft",   heightM = ProceduralContainerMesh.HeightStd, lengthM = ProceduralContainerMesh.Length20ft, count = 6 },
            new YardKind { prefix = StsPartNames.Yard40HCPrefix, model = "Container_40ftHC", heightM = ProceduralContainerMesh.HeightHC,  lengthM = ProceduralContainerMesh.Length40ft, count = 8 },
            new YardKind { prefix = StsPartNames.Yard40Prefix,   model = "Container_40ft",   heightM = ProceduralContainerMesh.HeightStd, lengthM = ProceduralContainerMesh.Length40ft, count = 4 },
        };

        /// <summary>야드 블록에 4종 컨테이너를 놓는다 — 블록마다 대수가 같거나 1 차이, 블록 안은 길이 방향으로 고르게.
        /// 좌표는 전부 PortConfig 유도값에서 나오므로 컨테이너가 블록 안에 맞는지가 곧 블록 좌표의 검산이다.</summary>
        [MenuItem("Model/FBX/항구/컨테이너 적재 (야드)", false, 7)]
        static void StackYardContainers()
        {
            ContainerFinal4Builder.EnsureMaterials();
            var kinds = YardKinds();
            float inv = StsConfig.InvModelScale;
            foreach (var k in kinds)
            {
                k.hi = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + k.model + ".fbx");
                k.lo = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}LOD1/{k.model}_LOD1.fbx");
                if (k.hi == null || k.lo == null)
                {
                    EditorUtility.DisplayDialog("야드 적재", $"컨테이너 FBX 없음:\n{ModelDir}{k.model}.fbx 또는 LOD1/{k.model}_LOD1.fbx", "확인");
                    return;
                }
                k.h = Probe(k.hi, k.heightM, out k.len, out float wid);   // 실측 — 규격과 다르면 FBX 가 바뀐 것
                if (Mathf.Abs(k.len * inv - k.lengthM) > 0.05f || Mathf.Abs(wid * inv - ProceduralContainerMesh.StdWidth) > 0.05f)
                {
                    Debug.LogError($"[항구] {k.model} 실측 {k.len * inv:F3}L × {wid * inv:F3}W m 가 규격과 다릅니다. 적재를 중단합니다.");
                    return;
                }
            }

            float rowPitch = PortConfig.RowPitchM * StsConfig.ModelScale;
            float bayPitch = PortConfig.BayPitchM * StsConfig.ModelScale;
            float halfW    = PortConfig.YardBlockWidthM  * 0.5f * StsConfig.ModelScale;
            float halfL    = PortConfig.YardBlockLengthM * 0.5f * StsConfig.ModelScale;

            var root = NewRoot("Yard_Containers");
            var stale = GameObject.Find("Container_40ft");   // 테스트로 꺼낸 낱개 정리
            if (stale != null) Undo.DestroyObjectImmediate(stale);

            // 셀은 블록마다 따로 모아 섞는다 — 전체를 한꺼번에 섞으면 한 블록에 몰렸다(15:5).
            var blocks = new List<List<(float x, float z, int r, int b)>>();
            for (int i2 = PortConfig.YardLaneStart; i2 < PortConfig.YardLanes; i2++)
                for (int j2 = 0; j2 < PortConfig.YardBlocksPerLane; j2++)
                {
                    float bx = PortConfig.YardBlockCenterX(i2) * StsConfig.ModelScale;
                    float bz = PortConfig.YardBlockCenterZ(j2) * StsConfig.ModelScale;
                    var cells = new List<(float x, float z, int r, int b)>();
                    for (int r = 0; r < PortConfig.YardRows; r++)
                        for (int b = 0; b < PortConfig.YardBays; b++)
                            cells.Add((bx - halfW + rowPitch * (r + 0.5f), bz - halfL + bayPitch * (b + 0.5f), r, b));
                    blocks.Add(cells);
                }
            if (blocks.Count == 0) { Debug.LogError("[항구] 야드 블록이 없습니다. 적재를 중단합니다."); return; }

            // 블록 안은 베이를 황금비 순서로 돌며 고른다 — 무작위만 쓰면 블록 한쪽 끝에 뭉쳤다(10개 중 9개가 끝 1/3).
            var rng = new System.Random(YardSeed);
            foreach (var cells in blocks) SpreadCells(cells, PortConfig.YardRows, PortConfig.YardBays, rng);

            var used = new HashSet<(int r, int b)>[blocks.Count];
            for (int bi = 0; bi < blocks.Count; bi++) used[bi] = new HashSet<(int r, int b)>();
            var count = new int[blocks.Count];

            // 다음 빈 칸 — 베이 간격보다 긴 컨테이너(45ft)는 앞뒤 베이가 비어 있어야 하고, 놓으면 앞뒤를 막는다.
            bool TryTake(int bi, bool blocksNeighbors, out (float x, float z, int r, int b) cell)
            {
                foreach (var c in blocks[bi])
                {
                    if (used[bi].Contains((c.r, c.b))) continue;
                    if (blocksNeighbors && (used[bi].Contains((c.r, c.b - 1)) || used[bi].Contains((c.r, c.b + 1)))) continue;
                    used[bi].Add((c.r, c.b));
                    if (blocksNeighbors) { used[bi].Add((c.r, c.b - 1)); used[bi].Add((c.r, c.b + 1)); }
                    cell = c;
                    return true;
                }
                cell = default;
                return false;
            }

            var made = new int[kinds.Length];
            for (int ki = 0; ki < kinds.Length; ki++)
            {
                var k = kinds[ki];
                bool pair = 2f * k.lengthM + Yard20ftGapM <= PortConfig.BayPitchM;   // 두 개가 한 베이에 들어간다
                bool longer = k.lengthM > PortConfig.ContainerLenM;                  // 베이 기준(40ft)보다 길다
                float off = (k.len + Yard20ftGapM * StsConfig.ModelScale) * 0.5f;
                while (made[ki] < k.count)
                {
                    // 대수가 가장 적은 블록부터 — 거기 자리가 없으면 다음으로 적은 블록
                    var order = new List<int>();
                    for (int bi = 0; bi < blocks.Count; bi++) order.Add(bi);
                    order.Sort((a, b) => count[a] != count[b] ? count[a].CompareTo(count[b]) : a.CompareTo(b));
                    int at = -1; (float x, float z, int r, int b) c = default;
                    foreach (int bi in order) if (TryTake(bi, longer, out c)) { at = bi; break; }
                    if (at < 0) { Debug.LogError($"[항구] {k.model} 놓을 칸이 없습니다({made[ki]}/{k.count}). 적재를 중단합니다."); return; }

                    foreach (float dz in pair ? new[] { -off, off } : new[] { 0f })
                    {
                        if (made[ki] >= k.count) break;
                        Put(k.hi, k.lo, root, $"{k.prefix}{made[ki]:00}", c.x, k.h * 0.5f, c.z + dz, k.heightM);
                        made[ki]++;
                        count[at]++;
                    }
                }
            }

            var sb = new System.Text.StringBuilder();
            for (int ki = 0; ki < kinds.Length; ki++)
                sb.Append($"{kinds[ki].model.Replace("Container_", "")} {made[ki]}{(kinds[ki].lo == null ? "(정밀본만)" : "")} · ");
            Done(root, $"{sb}블록별 대수 {string.Join(" / ", count)} · 시드 {YardSeed} · 1단");
        }

        /// <summary>블록 칸(열 r × 베이 b, r 우선 순서)을 고를 순서로 다시 줄 세운다 — 베이는 황금비 저불일치 순서, 열은 베이마다 시드로 섞는다.</summary>
        static void SpreadCells(List<(float x, float z, int r, int b)> cells, int rows, int bays, System.Random rng)
        {
            float phi = (Mathf.Sqrt(5f) - 1f) * 0.5f;
            var bayOrder = new List<int>();
            for (int b = 0; b < bays; b++) bayOrder.Add(b);
            bayOrder.Sort((p, q) => Mathf.Repeat(p * phi, 1f).CompareTo(Mathf.Repeat(q * phi, 1f)));
            var rowOrder = new int[bays][];
            for (int b = 0; b < bays; b++)
            {
                rowOrder[b] = new int[rows];
                for (int r = 0; r < rows; r++) rowOrder[b][r] = r;
                for (int k = rows - 1; k > 0; k--) { int m = rng.Next(k + 1); (rowOrder[b][k], rowOrder[b][m]) = (rowOrder[b][m], rowOrder[b][k]); }
            }
            var ordered = new List<(float x, float z, int r, int b)>(cells.Count);
            for (int pass = 0; pass < rows; pass++)
                foreach (int b in bayOrder) ordered.Add(cells[rowOrder[b][pass] * bays + b]);
            cells.Clear();
            cells.AddRange(ordered);
        }

        /// <summary>컨테이너 하나를 LOD0/LOD1 LODGroup으로 놓는다 — 높이는 Fit이 ISO에 맞춘다.</summary>
        static void Put(GameObject hi, GameObject lo, Transform parent, string name,
                        float x, float y, float z, float heightM)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.localPosition = new Vector3(x, y, z);   // 피봇 = 중앙 높이

            var g0 = Fit(hi, root.transform, "LOD0", heightM);
            var r0 = g0.GetComponentsInChildren<Renderer>();

            var r1 = Fit(lo, root.transform, "LOD1", heightM).GetComponentsInChildren<Renderer>();

            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(ContLod0Height, r0), new LOD(ContLod1Height, r1) });
            lg.RecalculateBounds();
        }

        /// <summary>FBX를 꺼내 ISO 높이에 맞춘다 — localScale을 1로 리셋하면 안 된다(정밀본/LOD1 스케일이 이미 있어
        /// 리셋하면 24배/1÷24배로 튄다). 반드시 곱한다.</summary>
        static GameObject Fit(GameObject src, Transform parent, string name, float heightM)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            float m = SceneUtil.BoundsOrPoint(go).size.y;
            float t = heightM * StsConfig.ModelScale;
            if (m > 1e-6f) go.transform.localScale *= t / m;

            // 원점 규약이 FBX마다 다르다(정밀본 '바닥', LOD1 '중앙') — 그대로 두면 LOD 전환 때 반통만큼 튄다.
            //   피봇을 바운즈 중앙으로 통일한다.
            go.transform.position += parent.position - SceneUtil.BoundsOrPoint(go).center;
            return go;
        }

        /// <summary>배치 후 크기를 실측한다 — Put과 같은 스케일 보정을 걸고 잰다.
        /// 보정 전에 재면 LOD1(실척 m)이 24배로 나와 규격 가드가 오작동한다. 측정과 배치가 같은 값을 봐야 한다.</summary>
        static float Probe(GameObject src, float heightM, out float len, out float wid)
        {
            var p = (GameObject)PrefabUtility.InstantiatePrefab(src);
            float m = SceneUtil.BoundsOrPoint(p).size.y;
            float t = heightM * StsConfig.ModelScale;
            if (m > 1e-6f) p.transform.localScale *= t / m;
            var b = SceneUtil.BoundsOrPoint(p);
            len = b.size.z; wid = b.size.x;
            float h = b.size.y;
            Object.DestroyImmediate(p);
            return h;
        }
    }
}
#endif
