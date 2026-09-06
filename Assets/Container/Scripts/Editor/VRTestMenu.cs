#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ContainerProject.EditorTools
{
    /// <summary>
    /// 절차적(Procedural) 컨테이너 스폰 메뉴.
    /// 기능·사이즈는 기존 SpawnContainers.Spawn20ftStd와 동일. 디자인(메시)만 ProceduralContainerMesh 사용.
    /// 프리팹/팔레트/ContainerInstance 사용하지 않음 — Std Set 패턴 그대로.
    /// </summary>
    public static class VRTestMenu
    {
        // 폴백 색상 풀 — 팔레트 에셋 로드 실패 시에만 사용. 평소엔 Container_Palette_Default.asset(24색)을 쓴다.
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

        // 24색 팔레트(런타임 ContainerInstance와 동일 SSOT). 한 곳에서만 관리해 색 중복/드리프트 방지.
        const string PalettePath = "Assets/Container/Container_Palette_Default.asset";
        static ContainerColorPalette _palette;
        static ContainerColorPalette Palette()
        {
            if (_palette == null) _palette = AssetDatabase.LoadAssetAtPath<ContainerColorPalette>(PalettePath);
            return _palette;
        }
        // 인덱스로 결정적 선택(야드 순환 — 24개면 24색 전부 1회씩). 팔레트 없으면 폴백.
        static Color PaletteAt(int index)
        {
            var p = Palette();
            if (p != null && p.Count > 0) return p.Get(index % p.Count).color;
            return PaletteColors[index % PaletteColors.Length];
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
            // 기존 컨테이너 모두 삭제 (Std Set 동일 패턴)
            var existing = Object.FindObjectsByType<CubeReset>(FindObjectsSortMode.None);
            foreach (var c in existing) Undo.DestroyObjectImmediate(c.gameObject);

            string name = string.IsNullOrEmpty(suffix) ? "Container_Procedural" : "Container_Procedural_" + suffix;
            var go = BuildOne(PaletteRandom(), name, length);
            var reset = go.GetComponent<CubeReset>();
            if (reset != null) reset.SetHorizontalOffset(0f);

            Undo.RegisterCreatedObjectUndo(go, "Spawn Procedural");
            Selection.activeGameObject = go;

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();

            Debug.Log($"[VRTestMenu] 분해형 컨테이너 1개 스폰 (length={length}m, bounds: {go.GetComponent<BoxCollider>().size}, parts: {go.GetComponentsInChildren<MeshFilter>().Length}개)");
        }

        // 컨테이너 빌더 (분해형/파트 분리)
        // 모든 스폰 메뉴가 이걸 쓴다 → 컨테이너는 항상 분해형(부품마다 독립 GameObject, 디자이너가 바로 편집).
        // 물리/그랩은 루트 한 덩어리로 동작: 루트에 Rigidbody+XRGrabInteractable+단일 BoxCollider(전체 바운즈),
        //   파트 콜라이더는 끄고(addColliders:false) 루트 박스 하나로만 충돌 → 컴파운드 콜라이더 중복 방지.
        // withReset:false → CubeReset 미부착(Play 시 카메라 앞으로 순간이동하지 않음). 야드 고정 배치용.
        public static GameObject BuildOne(Color bodyColor, string name, float length = -1f, bool withReset = true)
        {
            // 1. 셰이더
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // 2. 컨테이너별 색 변주(이름 시드 결정적) — 복붙처럼 안 보이게 명도/채도/광택을 Body·Door에만.
            //    ContainerInstance.ApplyColor와 동일 기법. 프레임/캐스팅 회색은 변주 제외(회색 유지 제약).
            float h1 = StableHash.Hash01(name, 0x9E3779B9u);
            float h2 = StableHash.Hash01(name, 0x85EBCA6Bu);
            float h3 = StableHash.Hash01(name, 0xC2B2AE35u);
            Color.RGBToHSV(bodyColor, out float ch, out float cs, out float cv);
            cv = Mathf.Clamp01(cv * Mathf.Lerp(0.90f, 1.04f, h1));   // 명도: 주로 약간 어둡게(먼지)
            cs = Mathf.Clamp01(cs * Mathf.Lerp(0.90f, 1.02f, h2));   // 채도: 약간 빠짐(색바램)
            ch = Mathf.Repeat(ch + Mathf.Lerp(-0.014f, 0.014f, h3), 1f);   // 색조: ±~5°(선사색 정체성 유지 위해 좁게)
            Color variedBody = Color.HSVToRGB(ch, cs, cv); variedBody.a = bodyColor.a;
            float bodySmooth = Mathf.Lerp(0.22f, 0.40f, h1);          // 광택: 무광(낡음)~반광

            // 4=Marking: ID/CSC 플레이트 — 변주와 무관한 고정 옅은 무광 흰(검정 번호 대비 보존).
            var mats = new ProceduralContainerMesh.KitMaterials
            {
                body     = MakeMat(litShader, variedBody, metallic: 0.10f, smoothness: bodySmooth, suffix: "_Body"),
                door     = MakeMat(litShader, MulColor(variedBody, 0.80f), metallic: 0.10f, smoothness: bodySmooth, suffix: "_Door"),
                frame    = MakeMat(litShader, new Color(0.18f, 0.18f, 0.20f), metallic: 0.55f, smoothness: 0.45f, suffix: "_Frame"),
                castings = MakeMat(litShader, new Color(0.10f, 0.10f, 0.11f), metallic: 0.40f, smoothness: 0.25f, suffix: "_Castings"),
                marking  = MakeMat(litShader, new Color(0.88f, 0.88f, 0.86f), metallic: 0.0f, smoothness: 0.20f, suffix: "_Marking"),
            };

            // 3. 분해형 계층 (미니어처 1/24, 바닥 피봇, X=긴 방향). 파트 콜라이더는 끄고 루트 박스만 사용.
            //    length 양수면 BuildKitSized(임의 사이즈), 아니면 기본 20ft BuildKit.
            GameObject root = length > 0f
                ? ProceduralContainerMesh.BuildKitSized(length, ProceduralContainerMesh.StdWidth, ProceduralContainerMesh.HeightStd,
                                                        mats, name, centerPivot: false, addColliders: false)
                : ProceduralContainerMesh.BuildKit(mats, name, centerPivot: false, addColliders: false);

            // 4. 루트 콜라이더 = 컨테이너 '공칭 외형'(ISO 코너캐스팅 기준 length×width×height) 단일 박스.
            //    기존엔 전체 파트 합산 AABB(RootLocalBounds)라 도어 락바·핸들·힌지 돌출까지 포함돼
            //    강철 외피보다 수 mm~1cm 부풀었고, 그 결과 강철 면은 떨어져 있는데 콜라이더만 닿아 컨테이너끼리
            //    '관통'처럼 보였다. 메시는 바닥 피봇·X=길이·X/Z 중심정렬(ProceduralContainerMesh.ApplyTransform)이라
            //    공칭 박스를 산식으로 직접 지정한다(돌출 하드웨어는 콜라이더에서 제외).
            const float s = ProceduralContainerMesh.DefaultMiniatureScale;
            float lenM = (length > 0f ? length : ProceduralContainerMesh.Length20ft) * s;  // X = 길이
            float widM = ProceduralContainerMesh.StdWidth  * s;                             // Z = 폭
            float hgtM = ProceduralContainerMesh.HeightStd * s;                             // Y = 높이
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, hgtM * 0.5f, 0f);   // 바닥 피봇 → 중심 y = 높이/2
            box.size   = new Vector3(lenM, hgtM, widM);

            // 5. VR 인터랙션 (기존 SpawnContainers Std Set 패턴 동일 — 루트를 한 덩어리로 잡고 옮김)
            var rb = root.AddComponent<Rigidbody>();
            rb.useGravity = true;
            // useDynamicAttach: 손이 닿은 지점이 그립 포인트가 되도록 (피봇 스냅 방지)
            var grab = root.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;
            if (withReset) root.AddComponent<CubeReset>();

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
