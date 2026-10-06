#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AIXRCrane.EditorTools
{
    /// <summary>절차적(ProceduralContainerMesh) 컨테이너 스폰 메뉴 — 분해형 계층 + 루트 한 덩어리 물리·그랩.</summary>
    public static class VRTestMenu
    {
        // 폴백 색 — 팔레트 에셋(Container_Palette_Default.asset, 24색) 로드 실패 시에만.
        static readonly Color[] PaletteColors =
        {
            new Color(0.72f, 0.19f, 0.18f),  // Red
            new Color(0.12f, 0.31f, 0.49f),  // Blue
            new Color(0.29f, 0.42f, 0.23f),  // Green
            new Color(0.85f, 0.45f, 0.15f),  // Orange
            new Color(0.40f, 0.26f, 0.18f),  // Brown
            new Color(0.28f, 0.28f, 0.30f),  // DarkGray
            new Color(0.92f, 0.92f, 0.90f),  // White
            new Color(0.85f, 0.78f, 0.58f),  // Beige
        };

        // 24색 팔레트(런타임 ContainerInstance 와 같은 SSOT).
        const string PalettePath = "Assets/Container/Container_Palette_Default.asset";
        static ContainerColorPalette _palette;
        static ContainerColorPalette Palette()
        {
            if (_palette == null) _palette = AssetDatabase.LoadAssetAtPath<ContainerColorPalette>(PalettePath);
            return _palette;
        }
        // 랜덤 선택(단일 스폰). 팔레트 없으면 폴백.
        static Color PaletteRandom()
        {
            var p = Palette();
            if (p != null && p.Count > 0) return p.Get(Random.Range(0, p.Count)).color;
            return PaletteColors[Random.Range(0, PaletteColors.Length)];
        }

        [MenuItem("Model/PG/컨테이너/컨테이너 생성 (1개)", false, 2)]
        public static void SpawnSingleProcedural()
        {
            SpawnSingle(length: ProceduralContainerMesh.Length20ft, suffix: "");
        }

        [MenuItem("Model/PG/컨테이너/컨테이너 생성 40ft (1개)", false, 3)]
        public static void SpawnSingleProcedural40ft()
        {
            SpawnSingle(length: ProceduralContainerMesh.Length40ft, suffix: "40ft");
        }

        static void SpawnSingle(float length, string suffix)
        {
            // 기존 컨테이너 모두 삭제
            var existing = Object.FindObjectsByType<CubeReset>();
            foreach (var c in existing) Undo.DestroyObjectImmediate(c.gameObject);

            string name = string.IsNullOrEmpty(suffix) ? "Container_Procedural" : "Container_Procedural_" + suffix;
            var go = BuildOne(PaletteRandom(), name, length);

            Undo.RegisterCreatedObjectUndo(go, "Spawn Procedural");
            Selection.activeGameObject = go;

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();

            Debug.Log($"[VRTestMenu] 분해형 컨테이너 1개 스폰 (length={length}m, bounds: {go.GetComponent<BoxCollider>().size}, parts: {go.GetComponentsInChildren<MeshFilter>().Length}개)");
        }

        // 분해형 컨테이너 빌더 — 파트 콜라이더는 끄고 루트 Rigidbody+XRGrabInteractable+단일 BoxCollider 로 동작.
        public static GameObject BuildOne(Color bodyColor, string name, float length)
        {
            // 1. 셰이더
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // 2. 이름 시드로 Body·Door 명도/채도/광택 변주(ContainerInstance.ApplyColor 와 같은 기법).
            float h1 = StableHash.Hash01(name, 0x9E3779B9u);
            float h2 = StableHash.Hash01(name, 0x85EBCA6Bu);
            float h3 = StableHash.Hash01(name, 0xC2B2AE35u);
            Color.RGBToHSV(bodyColor, out float ch, out float cs, out float cv);
            cv = Mathf.Clamp01(cv * Mathf.Lerp(0.90f, 1.04f, h1));   // 명도: 주로 약간 어둡게(먼지)
            cs = Mathf.Clamp01(cs * Mathf.Lerp(0.90f, 1.02f, h2));   // 채도: 약간 빠짐(색바램)
            ch = Mathf.Repeat(ch + Mathf.Lerp(-0.014f, 0.014f, h3), 1f);   // 색조: ±~5°
            Color variedBody = Color.HSVToRGB(ch, cs, cv); variedBody.a = bodyColor.a;
            float bodySmooth = Mathf.Lerp(0.22f, 0.40f, h1);          // 광택: 무광(낡음)~반광

            // 4=Marking: ID/CSC 플레이트 — 변주 없이 옅은 무광 흰.
            var mats = new ProceduralContainerMesh.KitMaterials
            {
                body     = MakeMat(litShader, variedBody, metallic: 0.10f, smoothness: bodySmooth, suffix: "_Body"),
                door     = MakeMat(litShader, MulColor(variedBody, 0.80f), metallic: 0.10f, smoothness: bodySmooth, suffix: "_Door"),
                frame    = MakeMat(litShader, new Color(0.18f, 0.18f, 0.20f), metallic: 0.55f, smoothness: 0.45f, suffix: "_Frame"),
                castings = MakeMat(litShader, new Color(0.10f, 0.10f, 0.11f), metallic: 0.40f, smoothness: 0.25f, suffix: "_Castings"),
                marking  = MakeMat(litShader, new Color(0.88f, 0.88f, 0.86f), metallic: 0.0f, smoothness: 0.20f, suffix: "_Marking"),
            };

            // 3. 분해형 계층(1/24, 바닥 피봇, X=길이).
            GameObject root = ProceduralContainerMesh.BuildKitSized(length, ProceduralContainerMesh.StdWidth, ProceduralContainerMesh.HeightStd,
                                                                    mats, name, centerPivot: false, addColliders: false);

            // 4. 루트 콜라이더 = 공칭 외형 단일 박스 — 돌출 하드웨어까지 넣으면 관통처럼 보인다.
            const float s = ProceduralContainerMesh.DefaultMiniatureScale;
            float lenM = length * s;                                                        // X = 길이
            float widM = ProceduralContainerMesh.StdWidth  * s;                             // Z = 폭
            float hgtM = ProceduralContainerMesh.HeightStd * s;                             // Y = 높이
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, hgtM * 0.5f, 0f);   // 바닥 피봇 → 중심 y = 높이/2
            box.size   = new Vector3(lenM, hgtM, widM);

            // 5. VR 인터랙션 — 루트를 한 덩어리로 잡고 옮김
            var rb = root.AddComponent<Rigidbody>();
            rb.useGravity = true;
            // useDynamicAttach: 손이 닿은 지점이 그립 포인트(피봇 스냅 방지)
            var grab = root.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;
            root.AddComponent<CubeReset>();

            // 6. 적층 안정화(방식 A) — 컨테이너 한정(전역 물리 미변경).
            ContainerPhysics.Apply(rb, box);

            return root;
        }

        static Material MakeMat(Shader shader, Color c, float metallic, float smoothness, string suffix)
        {
            var mat = new Material(shader) { name = "ProcMat" + suffix };
            if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))       mat.SetColor("_Color", c);
            if (mat.HasProperty("_Metallic"))    mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness"))  mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness"))  mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }

        static Color MulColor(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    }
}
#endif
