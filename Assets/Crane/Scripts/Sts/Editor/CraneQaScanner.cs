#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 크레인 오브젝트 정밀 검사 — 렌더러 바운즈를 계산해 세 가지를 자동 탐지, 콘솔에 리스트로 출력한다.
    ///   ① 겹침(Overlap): 서로 다른 부재가 깊게 관통(작은 쪽 부피의 OverlapFrac 이상). 부모-자식은 제외.
    ///   ② 부양(Floating): 주변 FloatGap 안에 다른 오브젝트가 하나도 없어 공중에 떠 있는(붙지 않은) 오브젝트.
    ///   ③ 중복(Duplicate): 이름 접두사가 같은데 바운즈가 거의 겹치는(같은 자리 이중 생성) 오브젝트.
    /// 화면을 못 보는 상태에서 겹침·부양·중복을 '전수' 찾는 유일한 정확한 방법. 실행 후 콘솔 리스트대로 수정한다.
    /// </summary>
    public static class CraneQaScanner
    {
        const float OverlapFrac = 0.35f;   // 작은 쪽 부피의 35% 이상 관통 = 딥 겹침(구조 조인트의 정상 접촉은 대개 이하)
        const float FloatGap    = 0.006f;  // 이 거리(모델u≈0.14m) 안에 이웃이 없으면 부양 의심
        const float DupFrac     = 0.80f;   // 같은 접두사 + 바운즈 80%+ 겹침 = 중복

        [MenuItem("Tool/크레인 정밀 검사 (겹침·부양·중복)", false, 20)]
        static void Scan()
        {
            var crane = GameObject.Find("STS_Crane");
            if (crane == null) { Debug.LogWarning("[CraneQA] 씬에 'STS_Crane'이 없습니다."); return; }

            var rends = crane.GetComponentsInChildren<MeshRenderer>(false);
            int n = rends.Length;
            var b = new Bounds[n];
            var t = new Transform[n];
            for (int i = 0; i < n; i++) { b[i] = rends[i].bounds; t[i] = rends[i].transform; }

            var overlaps  = new List<(string s, float f)>();
            var floating  = new List<string>();
            var dups      = new List<(string s, float f)>();

            // ① 겹침 + ③ 중복 (쌍 검사)
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (t[i].IsChildOf(t[j]) || t[j].IsChildOf(t[i])) continue;    // 부모-자식 제외
                    if (!b[i].Intersects(b[j])) continue;
                    float ov = IntersectVol(b[i], b[j]);
                    float vi = Vol(b[i]), vj = Vol(b[j]);
                    float small = Mathf.Min(vi, vj);
                    if (small <= 1e-12f) continue;
                    float frac = ov / small;

                    bool sameKind = BaseName(t[i].name) == BaseName(t[j].name);
                    if (sameKind && frac >= DupFrac)
                        dups.Add(($"{t[i].name} ≈ {t[j].name}  ({frac:P0} 동일자리)", frac));
                    else if (!sameKind && frac >= OverlapFrac)
                        overlaps.Add(($"{t[i].name} ↔ {t[j].name}  ({frac:P0} 관통, @{Fmt(b[i].center)})", frac));
                }
            }

            // ② 부양 (이웃 없음)
            for (int i = 0; i < n; i++)
            {
                Bounds bi = b[i]; bi.Expand(FloatGap * 2f);
                bool near = false;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    if (t[j].IsChildOf(t[i]) || t[i].IsChildOf(t[j])) continue;
                    if (bi.Intersects(b[j])) { near = true; break; }
                }
                if (!near) floating.Add($"{t[i].name}  @{Fmt(b[i].center)}  (최근접 이웃 > {FloatGap * 24f:F2}m)");
            }

            overlaps.Sort((a, c) => c.f.CompareTo(a.f));
            dups.Sort((a, c) => c.f.CompareTo(a.f));

            var sb = new StringBuilder();
            sb.AppendLine($"═══ [CraneQA] STS_Crane 정밀 검사 — 렌더 오브젝트 {n}개 ═══");
            sb.AppendLine($"\n■ 겹침(딥 관통) {overlaps.Count}건  (구조 조인트 정상접촉은 제외됨, 관통율 큰 순)");
            foreach (var o in overlaps) sb.AppendLine("  ⚠ " + o.s);
            sb.AppendLine($"\n■ 중복(같은 자리 이중생성) {dups.Count}건");
            foreach (var o in dups) sb.AppendLine("  ✖ " + o.s);
            sb.AppendLine($"\n■ 부양(주변 {FloatGap * 24f:F2}m 내 이웃 0 → 떠있음) {floating.Count}건");
            foreach (var f in floating) sb.AppendLine("  ○ " + f);
            sb.AppendLine("\n※ 겹침/부양은 휴리스틱 — 리스트를 근거로 사람이 판정·수정. 좌표는 월드(실척은 ×24 아님, 모델u).");

            Debug.Log(sb.ToString());
        }

        // 헬퍼
        static float Vol(Bounds x) { var s = x.size; return s.x * s.y * s.z; }
        static float IntersectVol(Bounds a, Bounds c)
        {
            float dx = Mathf.Min(a.max.x, c.max.x) - Mathf.Max(a.min.x, c.min.x);
            float dy = Mathf.Min(a.max.y, c.max.y) - Mathf.Max(a.min.y, c.min.y);
            float dz = Mathf.Min(a.max.z, c.max.z) - Mathf.Max(a.min.z, c.min.z);
            if (dx <= 0 || dy <= 0 || dz <= 0) return 0f;
            return dx * dy * dz;
        }
        static string BaseName(string s)   // 끝의 _숫자 접미사 제거("Boom_Girder_3" → "Boom_Girder")
        {
            int k = s.LastIndexOf('_');
            if (k > 0 && int.TryParse(s.Substring(k + 1), out _)) return s.Substring(0, k);
            return s;
        }
        static string Fmt(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }
}
#endif
