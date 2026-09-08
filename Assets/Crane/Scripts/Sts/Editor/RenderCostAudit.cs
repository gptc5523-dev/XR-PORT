#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>렌더 비용 측정 — 최적화 전후를 같은 잣대로 비교하기 위한 도구.
    ///
    /// 왜 있는가: 최적화는 '했다'가 아니라 '얼마나 줄었다'로 말해야 한다.
    /// 산식으로 추정하지 말고 이걸 돌려서 숫자를 남긴다.
    ///
    /// 주의 — 여기 숫자는 에디터 씬 통계지 프레임타임이 아니다.
    ///   최종 판단은 서버 빌드에서 해야 한다(맥 에디터가 버벅인다고 서버도 버벅이는 건 아니다).</summary>
    internal static class RenderCostAudit
    {
        [MenuItem("Model/FBX/항구/렌더 비용 측정", priority = 30)]
        static void Run()
        {
            var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // LODGroup 이 관리하는 렌더러는 어느 단계인지 표시해 둔다.
            var lodLevel = new Dictionary<Renderer, int>();
            var groups = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var g in groups)
            {
                var lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null) lodLevel[r] = i;
            }

            long triLod0 = 0, triLodLast = 0, triNoLod = 0;
            long triShadow = 0;
            int nShadow = 0;
            var mats = new HashSet<Material>();

            foreach (var r in all)
            {
                long t = Tris(r);
                if (t == 0) continue;

                foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);

                if (lodLevel.TryGetValue(r, out int lv))
                {
                    if (lv == 0) triLod0 += t; else triLodLast += t;
                }
                else triNoLod += t;

                if (r.shadowCastingMode != ShadowCastingMode.Off)
                {
                    nShadow++;
                    // 그림자는 '실제로 그려지는 단계'만 던진다 — 배경은 마지막 LOD.
                    if (!lodLevel.TryGetValue(r, out int lv2) || lv2 > 0) triShadow += t;
                }
            }

            // 실제 렌더 = LOD 없는 것 + 마지막 LOD(원거리 기준). 근접분은 LOD0 이 대신 들어온다.
            long effective = triNoLod + triLodLast;

            var rp = UniversalRenderPipelineAssetInfo();

            Debug.Log(
                "[비용] 렌더 비용 측정\n" +
                $"  렌더러        {all.Length,10:N0}개 (LODGroup {groups.Length:N0}개 · 머티리얼 {mats.Count:N0}종)\n" +
                $"  LOD0 정밀     {triLod0,10:N0} 삼각형 (근접 시에만)\n" +
                $"  LOD 마지막    {triLodLast,10:N0} 삼각형 (원거리 배경)\n" +
                $"  LOD 없음      {triNoLod,10:N0} 삼각형 (부두·크레인·선체)\n" +
                $"  ── 실제 렌더  {effective,10:N0} 삼각형\n" +
                $"  그림자 캐스터 {nShadow,10:N0}개 → {triShadow,10:N0} 삼각형 × 캐스케이드\n" +
                $"  {rp}");

            // 무거운 순 상위 — 어디를 손봐야 하는지
            var top = all.Where(r => Tris(r) > 0)
                         .GroupBy(r => RootName(r.transform))
                         .Select(g => (name: g.Key, tri: g.Sum(r => Tris(r)), n: g.Count()))
                         .OrderByDescending(x => x.tri).Take(8);
            foreach (var x in top)
                Debug.Log($"       {x.name,-22} {x.tri,12:N0} 삼각형 · 렌더러 {x.n:N0}개");
        }

        static long Tris(Renderer r)
        {
            Mesh m = r switch
            {
                SkinnedMeshRenderer s => s.sharedMesh,
                _ => r.GetComponent<MeshFilter>()?.sharedMesh
            };
            if (m == null) return 0;
            long t = 0;
            for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3;
            return t;
        }

        static string RootName(Transform t)
        {
            while (t.parent != null && t.parent.parent != null) t = t.parent;
            return t.name;
        }

        static string UniversalRenderPipelineAssetInfo()
        {
            var a = GraphicsSettings.defaultRenderPipeline;
            if (a == null) return "렌더파이프라인 없음(빌트인)";
            var so = new SerializedObject(a);
            string G(string p) => so.FindProperty(p)?.intValue.ToString() ?? "?";
            string F(string p) => so.FindProperty(p)?.floatValue.ToString("F0") ?? "?";
            return $"{a.name} — 그림자거리 {F("m_ShadowDistance")} · 캐스케이드 {G("m_ShadowCascadeCount")} · MSAA {G("m_MSAA")}";
        }
    }
}
#endif
