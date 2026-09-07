#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ContainerProject.EditorTools
{
    /// <summary>
    /// New Final 4종(20ft · 40ft · 40ftHC · 45ftHC) FBX 를 씬에 꺼내 쓰는 메뉴.
    ///
    /// 메뉴는 "생성" 4개뿐이다. 머티리얼은 생성할 때 없으면 자동으로 만든다(최초 1회만 실제로 만들어짐)
    /// — 별도 빌드/검증 메뉴를 두면 순서를 기억해야 해서 오히려 번거롭다.
    /// 프리팹은 만들지 않는다. <c>Prefabs/Container_20ft.prefab</c> 은 절차 생성(ContainerInstance·라벨)
    /// 자산이라 같은 이름으로 저장하면 덮어쓴다.
    ///
    /// 전제 — Blender 익스포트 규약 (2026-08-12)
    ///   축   Blender Y(길이) → Unity X(도어 +X) · Z(높이) → Y · X(폭) → Z
    ///        (axis_forward='-X', axis_up='Z' · 행렬식 +1 이라 좌우 반전 없음)
    ///   피봇 바닥면 중앙 (z_min → 0) → 씬에서 y=0 이면 그대로 지면에 앉는다
    ///   단위 ★FBX 를 **1/24 로 굽는다**(2026-08-12 오너 지시). ContainerModelPostprocessor.globalScale = 1.
    ///        실측(모델 단위) 20ft 0.25242 · 40ft/40ftHC 0.50792 · 45ftHC 0.57142 (= 실척 ÷ 24)
    ///
    /// ★도색은 텍스처에 구워져 있다.
    ///   Blender 의 PaintTint(MULTIPLY 배율 2.024138/2.011376/1.974708)는 FBX 로 넘어오지 않으므로
    ///   Textures/Container_Body_BaseColor.png · Container_DoorHardware_BaseColor.png 를
    ///   리니어 공간에서 배율 적용해 재저장했다(실측 리니어 평균 0.802 / 0.243 · 클립 0).
    ///   따라서 머티리얼 _BaseColor 는 흰색이며, 여기에 색을 또 곱하면 안 된다.
    ///
    /// MetalSmooth 규약 — R = metallic, A = smoothness (URP _MetallicGlossMap 과 동일).
    /// </summary>
    public static class ContainerFinal4Builder
    {
        const string ModelDir    = "Assets/Container/Models/";
        const string TextureDir  = "Assets/Container/Textures/";
        const string MaterialDir = "Assets/Container/Materials/Final4/";

        [MenuItem("Model/FBX/컨테이너/20ft 생성",   false, 1)] static void P20()   => Place("Container_20ft");
        [MenuItem("Model/FBX/컨테이너/40ft 생성",   false, 2)] static void P40()   => Place("Container_40ft");
        [MenuItem("Model/FBX/컨테이너/40ftHC 생성", false, 3)] static void P40HC() => Place("Container_40ftHC");
        [MenuItem("Model/FBX/컨테이너/45ftHC 생성", false, 4)] static void P45HC() => Place("Container_45ftHC");

        static void Place(string size)
        {
            string fbx = ModelDir + size + ".fbx";
            if (!File.Exists(fbx)) { Debug.LogError($"[컨테이너] 없음: {fbx}"); return; }

            // 머티리얼이 아직 없으면 만들고 FBX 를 다시 임포트한다(리맵은 ContainerModelPostprocessor 담당).
            if (EnsureMaterials()) AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (src == null) { Debug.LogError($"[컨테이너] 임포트 실패: {fbx}"); return; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = size;
            Undo.RegisterCreatedObjectUndo(go, "Place " + size);

            // 씬 뷰 카메라 앞 지면에 — 피봇이 바닥면 중앙이라 y=0 이면 바로 지면에 앉는다.
            var sv = SceneView.lastActiveSceneView;
            Vector3 p = Vector3.zero;
            if (sv != null && sv.camera != null)
                p = sv.camera.transform.position + sv.camera.transform.forward * (Bound(go).size.magnitude * 1.2f);
            p.y = 0f;
            go.transform.position = p;

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (sv != null) sv.FrameSelected();

            var b = Bound(go);
            float inv = 1f / Container.Crane.Sts.StsConfig.ModelScale;   // 모델 단위 → 실척 m
            int missing = 0;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || m.name.Contains("Default")) missing++;

            Debug.Log($"[컨테이너] {size} 배치 — 실척 X(길이) {b.size.x * inv:F3} · Y(높이) {b.size.y * inv:F3} · " +
                      $"Z(폭) {b.size.z * inv:F3} m · 바닥 Y {b.min.y * inv:F4} m · 미할당 머티리얼 {missing}");
        }

        // ── 머티리얼 ──────────────────────────────────────────────────────

        struct Spec
        {
            public string baseMap, metalSmooth, normal;
            public Color  flat;          // 텍스처가 없을 때 쓰는 선형색
            public float  smoothness, metallic;
            public bool   HasTex => !string.IsNullOrEmpty(baseMap);
        }

        // 도장 5종 + LOD3 은 Container_Body_* 를 공유한다(도장 마감이라 실물 재질이 달라도 도료가 보인다).
        static Spec Paint() => new Spec {
            baseMap = "Container_Body_BaseColor", metalSmooth = "Container_Body_MetalSmooth",
            smoothness = 1f - 0.52f, metallic = 0f };

        static readonly Dictionary<string, Spec> Table = new Dictionary<string, Spec>
        {
            { "Body",      Paint() },
            { "Door",      Paint() },
            { "Frame",     Paint() },
            { "Steel_HDG", Paint() },
            { "ABS",       Paint() },
            { "Body_Corrugated_LOD3", Paint() },
            { "Container_LOD3",       Paint() },

            { "DoorHardware_Gray", new Spec {
                baseMap = "Container_DoorHardware_BaseColor", metalSmooth = "Container_DoorHardware_MetalSmooth",
                smoothness = 1f - 0.60f, metallic = 0f } },

            { "CSC_Plate_Etched", new Spec {
                baseMap = "CSC_Plate_BaseColor", metalSmooth = "CSC_Plate_MetalSmooth", normal = "CSC_Plate_Normal",
                smoothness = 1f - 0.50f, metallic = 0f } },

            { "Plywood_19ply", new Spec {
                baseMap = "Container_Plywood_BaseColor", metalSmooth = "Container_Plywood_MetalSmooth",
                smoothness = 1f - 0.499f, metallic = 0f } },

            { "Sealant", new Spec {
                baseMap = "Container_Sealant_BaseColor", metalSmooth = "Container_Sealant_MetalSmooth",
                smoothness = 1f - 0.62f, metallic = 0f } },

            { "Stainless", new Spec {
                baseMap = "Container_Stainless_BaseColor", metalSmooth = "Container_Stainless_MetalSmooth",
                smoothness = 1f - 0.32f, metallic = 1f } },

            // 텍스처 없는 플랫 재질 — Blender 실효 선형색 그대로
            { "EPDM_Gasket", new Spec { flat = new Color(0.012f, 0.012f, 0.012f), smoothness = 1f - 0.90f, metallic = 0f } },
            { "Etch_Fill",   new Spec { flat = new Color(0.020f, 0.020f, 0.022f), smoothness = 1f - 0.70f, metallic = 0f } },
        };

        /// <summary>없는 머티리얼만 만든다. 하나라도 만들었으면 true(→ 호출부가 FBX 재임포트).</summary>
        /// <summary>머티리얼이 없으면 만든다(최초 1회만 실제 생성). 야드 적재 등 외부 배치기도
        /// 컨테이너를 꺼내기 전에 호출해야 머티리얼 미할당으로 나오지 않는다.</summary>
        internal static bool EnsureMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Debug.LogError("[컨테이너] URP/Lit 셰이더를 찾지 못했습니다."); return false; }

            Directory.CreateDirectory(MaterialDir);
            int made = 0;
            foreach (var kv in Table)
            {
                string path = MaterialDir + kv.Key + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
                var mat = new Material(shader);
                Apply(mat, kv.Value);
                AssetDatabase.CreateAsset(mat, path);
                made++;
            }
            if (made > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[컨테이너] 머티리얼 {made}종 생성 → {MaterialDir}");
            }
            return made > 0;
        }

        static void Apply(Material m, Spec sp)
        {
            m.SetColor("_BaseColor", Color.white);          // ★도색은 텍스처에 구워져 있다
            m.SetFloat("_Smoothness", Mathf.Clamp01(sp.smoothness));
            m.SetFloat("_Metallic",   Mathf.Clamp01(sp.metallic));
            m.SetFloat("_SmoothnessTextureChannel", 0f);    // 0 = MetallicAlpha

            if (sp.HasTex)
            {
                m.SetTexture("_BaseMap", Load(sp.baseMap));
                var ms = Load(sp.metalSmooth);
                if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            }
            else
            {
                m.SetColor("_BaseColor", sp.flat);           // 플랫 재질만 색을 직접 준다
            }

            if (!string.IsNullOrEmpty(sp.normal))
            {
                var n = Load(sp.normal);
                if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            }
        }

        static Texture2D Load(string name) =>
            string.IsNullOrEmpty(name) ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + name + ".png");

        static Bounds Bound(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
#endif
