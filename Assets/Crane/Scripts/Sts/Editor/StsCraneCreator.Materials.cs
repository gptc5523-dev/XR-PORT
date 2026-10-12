#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 같은 색은 머티리얼 1개를 재사용(빌드 1회 한정)
        static Dictionary<Color, Material> _matCache;
        // 모든 머티리얼이 공유하는 절차 생성 강철 디테일 텍스처(_BaseColor로 틴트)
        static Texture2D _steelTex;
        // 와이어 로프 전용 절차 텍스처 — 나선 strand 밴드(헬리컬 레이)
        static Texture2D _ropeTex;
        // PBR_Library 텍스처 세트 캐시(폴더명 → [albedo, normal, ao]). 빌드 1회 한정.
        static Dictionary<string, Texture2D[]> _pbrCache;

        static void Colorize(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial = GetMaterial(c);
        }

        // 같은 색 머티리얼 재사용 — 프로젝트 머티리얼 오염 방지 위해 인스턴스(에셋 미저장).
        // 단색 평면 → 절차 강철 텍스처(albedo) + 카테고리별 PBR + 일부 에미시브.
        static Material GetMaterial(Color c)
        {
            if (_matCache != null && _matCache.TryGetValue(c, out var cached)) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "STS_Mat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);

            // 카테고리별 PBR + 텍스처 적용 여부
            float metallic = 0.38f, smooth = 0.45f;   // 구조 강철 기본 — 광택 약간↑(매트 플라스틱 '토이' 느낌 완화)
            bool  useTex = true, emissive = false, useRopeTex = false;
            float emi = 0f;
            // [디테일 페인팅] 카테고리 → PBR_Library 폴더 + 타일링. 라이브러리 없으면 ApplyPbr이 false → 절차 강철 폴백.
            string pbr = "PaintedMetal001"; Vector2 pbrTile = new Vector2(2f, 2f);   // 기본: 도장 구조강

            if (Same(c, CGlass))       { metallic = 0.0f;  smooth = 0.92f; useTex = false; pbr = null; }  // 유리: 매끈
            else if (Same(c, CCable))  { metallic = 0.80f; smooth = 0.34f; useTex = false; useRopeTex = true; pbr = null; } // 와이어 로프: 강철 광택 + 나선 strand
            else if (Same(c, CLight))  { metallic = 0.0f;  smooth = 0.60f; useTex = false; pbr = null; emissive = true; emi = 1.8f; } // 작업등 렌즈: 발광
            else if (Same(c, CWarn))   { metallic = 0.0f;  smooth = 0.55f; pbr = null; emissive = true; emi = 1.1f; } // 경고/항공등: 약발광
            else if (Same(c, CDark))   { metallic = 0.20f; smooth = 0.25f; pbr = "Metal032";        pbrTile = new Vector2(3f, 3f); } // 다크 머신 강철(트위스트락/허브)
            else if (Same(c, CRail))   { metallic = 0.65f; smooth = 0.55f; pbr = "Metal055A";       pbrTile = new Vector2(6f, 2f); } // 마모된 레일: 베어 메탈
            else if (Same(c, CMachine)){ metallic = 0.35f; smooth = 0.30f; pbr = "CorrugatedSteel002"; pbrTile = new Vector2(3f, 3f); } // 기계실: 골강판 외벽
            else if (Same(c, CTrolley) || Same(c, CSpread)) { pbr = null; }  // 안전색(주황/노랑): 채도 보존 위해 그레이스케일 절차강에 클린 틴트
            // 그 외(CStruct/CBoom): 도장 구조강(PaintedMetal001)

            if (useTex)
            {
                // 색(알베도)은 항상 그레이스케일 절차강 × 틴트 → 의도색(회색) 정확 유지. 표면 굴곡만 PBR 노멀/AO로 보강.
                //   ※ 프레임/구조 강철 회색은 절대 라이브러리 Color로 바꾸지 말 것(사용자 지시) — 노멀/AO만 덧댄다.
                Vector2 tile = (pbr != null) ? pbrTile : Vector2.one;
                var tex = GetSteelTexture();
                if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", tile); }
                if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", tile); }
                if (pbr != null) ApplyPbrDetail(mat, pbr, tile);   // 노멀+AO만(색 불변)
            }
            else if (useRopeTex)
            {
                var rt = GetRopeTexture();
                var tile = new Vector2(1f, 14f);   // 둘레 1회(6 strand) × 길이방향 촘촘히 → 나선 결
                if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", rt); mat.SetTextureScale("_BaseMap", tile); }
                if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", rt); mat.SetTextureScale("_MainTex", tile); }
            }
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * emi);
            }

            _matCache?.Add(c, mat);
            return mat;
        }

        // 색 근사 비교(머티리얼 카테고리 분류용)
        static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        // PBR_Library 폴더에서 [albedo, normal, ao] 텍스처를 로드(없으면 null). AmbientCG 명명규칙: <폴더>_1K-PNG_<맵>.png
        static Texture2D[] GetPbrSet(string folder)
        {
            if (_pbrCache != null && _pbrCache.TryGetValue(folder, out var cached)) return cached;
            const string root = "Assets/PBR_Library/";
            Texture2D Load(string suffix) =>
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}{folder}/{folder}_1K-PNG_{suffix}.png");
            var set = new[] { Load("Color"), Load("NormalGL"), Load("AmbientOcclusion") };
            _pbrCache?.Add(folder, set);
            return set;
        }

        // PBR 라이브러리의 노멀+AO만 입혀 표면 디테일을 더함(색은 건드리지 않음).
        //   알베도는 그레이스케일 절차강×_BaseColor 틴트가 담당. AO는 강도를 낮춰(0.5) 과하게 어두워지지 않게 한다.
        static void ApplyPbrDetail(Material mat, string folder, Vector2 tile)
        {
            var set = GetPbrSet(folder);
            if (set == null) return;
            if (set[1] != null && mat.HasProperty("_BumpMap"))          // 노멀(굴곡 디테일)
            {
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_BumpMap", set[1]);
                mat.SetTextureScale("_BumpMap", tile);
            }
            if (set[2] != null && mat.HasProperty("_OcclusionMap"))     // AO(약하게)
            {
                mat.SetTexture("_OcclusionMap", set[2]);
                mat.SetTextureScale("_OcclusionMap", tile);
                if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", 0.5f);
            }
        }

        // 절차 생성 강철 디테일 텍스처 — 그레이스케일(평균≈0.9), 다중 옥타브 노이즈 + 세로 때 스트릭 + 미세 그레인.
        // _BaseColor가 곱해져 각 색의 "칠한 강철" 질감이 됨. PBR 라이브러리 폴백 + 안전색(주황/노랑) 전용. 에셋 미저장.
        static Texture2D GetSteelTexture()
        {
            if (_steelTex != null) return _steelTex;

            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "STS_SteelDetail", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 다중 옥타브 노이즈(얼룩/때)
                float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.55f
                        + Mathf.PerlinNoise(x * 0.13f + 100f, y * 0.13f) * 0.30f
                        + Mathf.PerlinNoise(x * 0.40f + 50f,  y * 0.40f) * 0.15f;
                // 세로로 흘러내린 때 스트릭(상단일수록 옅게)
                float streak = Mathf.PerlinNoise(x * 0.6f, y * 0.015f);
                streak = Mathf.SmoothStep(0.62f, 1f, streak) * (y / (float)N) * 0.18f;
                // 미세 그레인
                float grain = (Hash(x, y) - 0.5f) * 0.05f;

                float v = Mathf.Clamp01(0.93f + (n - 0.5f) * 0.22f + grain - streak);
                byte b = (byte)(v * 255f);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _steelTex = tex;
        }

        // 와이어 로프 텍스처 — 헬리컬 6-strand 대각 밴드 + strand 내 미세 와이어 + 그레인.
        //   그레이스케일(평균≈0.8)이라 _BaseColor(CCable 강철 회색)에 곱해져 꼬인 로프 결을 만든다.
        static Texture2D GetRopeTexture()
        {
            if (_ropeTex != null) return _ropeTex;
            const int N = 128;
            const float TAU = Mathf.PI * 2f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "STS_RopeStrand", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                // 외측 6 strand 나선 — u(둘레) 6회, v(길이) 따라 감김 → 대각 밴드
                float strand = Mathf.Cos((6f * u + 3f * v) * TAU);      // 능선(+)=밝게, 골(-)=어둡게
                float shade  = 0.80f + 0.20f * strand;
                // strand 골 음영 강조(둥근 단면 느낌)
                shade -= Mathf.SmoothStep(0.0f, 1.0f, -strand) * 0.12f;
                // strand 안 미세 와이어(촘촘한 결)
                float wire = Mathf.Cos((36f * u + 18f * v) * TAU);
                shade *= 0.95f + 0.05f * wire;
                float grain = (Hash(x, y) - 0.5f) * 0.05f;
                float val = Mathf.Clamp01(shade + grain);
                byte b = (byte)(val * 255f);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _ropeTex = tex;
        }

        // 결정적 정수 해시 → [0,1) (텍스처 그레인용)
        static float Hash(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
        }
    }
}
#endif
