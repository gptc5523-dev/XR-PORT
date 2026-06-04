#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ContainerProject.EditorTools
{
    /// <summary>
    /// 메뉴에서 한 번에 20ft 컨테이너 프리팹·메시·머티리얼·팔레트를 생성.
    /// 위치:
    ///   Assets/Container/Models/Container_20ft_Procedural.asset
    ///   Assets/Container/Materials/Container_Body.mat / _Door.mat / _Frame.mat / _Castings.mat
    ///   Assets/Container/Container_Palette_Default.asset
    ///   Assets/Container/Prefabs/Container_20ft.prefab
    /// </summary>
    public static class ContainerEditorBuilder
    {
        const string ContainerRoot = "Assets/Container";
        const string ModelDir   = ContainerRoot + "/Models";
        const string MaterialDir= ContainerRoot + "/Materials";
        const string PrefabDir  = ContainerRoot + "/Prefabs";

        const string MeshAsset  = ModelDir   + "/Container_20ft_Procedural.asset";
        const string MatBody    = MaterialDir+ "/Container_Body.mat";
        const string MatDoor    = MaterialDir+ "/Container_Door.mat";
        const string MatFrame   = MaterialDir+ "/Container_Frame.mat";
        const string MatCasting = MaterialDir+ "/Container_Castings.mat";
        const string PaletteAsset = ContainerRoot + "/Container_Palette_Default.asset";
        const string PrefabAsset  = PrefabDir  + "/Container_20ft.prefab";

        // [MenuItem("Container/컨테이너 프리팹 생성", false, 20)]   // 메뉴 숨김(미사용 — prefab 파이프라인 휴면) — 복구하려면 주석 해제
        public static void BuildAll()
        {
            EnsureFolder(ModelDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(PrefabDir);

            var mesh = BuildAndSaveMesh();
            var (bodyMat, doorMat, frameMat, castingMat) = BuildAndSaveMaterials();
            var palette = EnsurePalette();
            var prefab = BuildAndSavePrefab(mesh, bodyMat, doorMat, frameMat, castingMat);

            EditorUtility.DisplayDialog(
                "Container Build 완료",
                $"메시: {mesh.vertexCount} verts / 서브메시 {mesh.subMeshCount}\n" +
                $"프리팹: {AssetDatabase.GetAssetPath(prefab)}\n" +
                $"팔레트: {AssetDatabase.GetAssetPath(palette)}\n\n" +
                "씬에 빈 GameObject를 만들고 ContainerSpawner 컴포넌트를 부착한 뒤,\n" +
                "containerPrefab / palette 슬롯에 위 두 에셋을 연결하고 Play 모드로 검증해 주십시오.",
                "OK");
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        // [MenuItem("Container/절차 메시 재생성", false, 21)]   // 메뉴 숨김(미사용 — prefab 파이프라인 휴면) — 복구하려면 주석 해제
        public static void RegenerateMeshOnly()
        {
            EnsureFolder(ModelDir);
            BuildAndSaveMesh();
            Debug.Log("[ContainerBuilder] 메시 재생성 완료: " + MeshAsset);
        }

        // ───────────────────────────── 메시 ─────────────────────────────
        static Mesh BuildAndSaveMesh()
        {
            var mesh = ProceduralContainerMesh.Build();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                AssetDatabase.SaveAssets();
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, MeshAsset);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        // ───────────────────────────── 머티리얼 ─────────────────────────────
        static (Material body, Material door, Material frame, Material castings) BuildAndSaveMaterials()
        {
            // URP/Lit 우선, 없으면 Standard 폴백
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (litShader == null)
            {
                Debug.LogError("[ContainerBuilder] URP/Lit과 Standard 셰이더를 모두 찾지 못했습니다.");
                return (null, null, null, null);
            }

            // Body: 흰색 기본, 보통 metalness 0, 약간의 러프니스
            var body = EnsureMaterial(MatBody, litShader);
            ApplyLit(body, new Color(0.92f, 0.92f, 0.90f), metallic: 0.05f, smoothness: 0.35f);

            // Door: Body와 동일 톤 (런타임에 같은 색으로 덮어씌워짐)
            var door = EnsureMaterial(MatDoor, litShader);
            ApplyLit(door, new Color(0.92f, 0.92f, 0.90f), metallic: 0.05f, smoothness: 0.35f);

            // Frame: 다크 그레이, 약간의 메탈릭
            var frame = EnsureMaterial(MatFrame, litShader);
            ApplyLit(frame, new Color(0.18f, 0.18f, 0.20f), metallic: 0.45f, smoothness: 0.45f);

            // Castings: 검정에 가까운 회색, 좀 더 거친 표면
            var castings = EnsureMaterial(MatCasting, litShader);
            ApplyLit(castings, new Color(0.10f, 0.10f, 0.11f), metallic: 0.35f, smoothness: 0.25f);

            AssetDatabase.SaveAssets();
            return (body, door, frame, castings);
        }

        static Material EnsureMaterial(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        static void ApplyLit(Material mat, Color baseColor, float metallic, float smoothness)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_Metallic"))  mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(mat);
        }

        // ───────────────────────────── 팔레트 ─────────────────────────────
        static ContainerColorPalette EnsurePalette()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ContainerColorPalette>(PaletteAsset);
            if (existing != null) return existing;

            var palette = ScriptableObject.CreateInstance<ContainerColorPalette>();
            AssetDatabase.CreateAsset(palette, PaletteAsset);
            AssetDatabase.SaveAssets();
            return palette;
        }

        // ───────────────────────────── 프리팹 ─────────────────────────────
        static GameObject BuildAndSavePrefab(Mesh mesh,
            Material body, Material door, Material frame, Material castings)
        {
            // 임시 GameObject 구성
            var root = new GameObject("Container_20ft");
            var mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = root.AddComponent<MeshRenderer>();
            // 서브메시 인덱스: 0=Body, 1=Door, 2=Frame, 3=Castings
            mr.sharedMaterials = new[] { body, door, frame, castings };

            // 메시 실제 크기 (스케일/피봇 적용 후)
            Bounds bounds = mesh.bounds;
            float bW = bounds.size.x;
            float bH = bounds.size.y;
            float bL = bounds.size.z;

            // 콜라이더 — 메시 bounds 그대로
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size   = bounds.size;

            // VR 인터랙션 (기존 미니어처 컨테이너와 동일 패턴)
            var rb = root.AddComponent<Rigidbody>();
            rb.useGravity = true;

            // useDynamicAttach: 손이 닿은 지점이 그립 포인트가 되도록 (피봇 스냅 방지)
            var grab = root.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;
            root.AddComponent<CubeReset>();

            // TMP 라벨 4개 — bounds 기준 비율 (스케일에 자동 동기)
            float labelY = bounds.center.y + bH * 0.22f;   // 상단 약간 아래
            float doorZ  = bounds.max.z + bL * 0.018f;     // 도어 바깥쪽
            float sideX  = bW * 0.501f;                    // 측면 패널 살짝 바깥

            var label_L = CreateIdLabel(root.transform, "Label_Left",
                new Vector3(-sideX, labelY, bounds.center.z),
                new Vector3(0f, -90f, 0f), bH);
            var label_R = CreateIdLabel(root.transform, "Label_Right",
                new Vector3( sideX, labelY, bounds.center.z),
                new Vector3(0f,  90f, 0f), bH);
            var label_DL = CreateIdLabel(root.transform, "Label_Door_Left",
                new Vector3(-bW * 0.24f, labelY, doorZ),
                new Vector3(0f, 0f, 0f), bH);
            var label_DR = CreateIdLabel(root.transform, "Label_Door_Right",
                new Vector3( bW * 0.24f, labelY, doorZ),
                new Vector3(0f, 0f, 0f), bH);

            // ContainerInstance 부착
            var instance = root.AddComponent<ContainerInstance>();
            SerializedObject so = new SerializedObject(instance);

            var slotsProp = so.FindProperty("bodyRenderers");
            slotsProp.arraySize = 2;
            // slot 0: Body (머티리얼 인덱스 0)
            var s0 = slotsProp.GetArrayElementAtIndex(0);
            s0.FindPropertyRelative("renderer").objectReferenceValue = mr;
            s0.FindPropertyRelative("materialIndex").intValue = 0;
            // slot 1: Door (머티리얼 인덱스 1)
            var s1 = slotsProp.GetArrayElementAtIndex(1);
            s1.FindPropertyRelative("renderer").objectReferenceValue = mr;
            s1.FindPropertyRelative("materialIndex").intValue = 1;

            var labelsProp = so.FindProperty("idLabels");
            labelsProp.arraySize = 4;
            labelsProp.GetArrayElementAtIndex(0).objectReferenceValue = label_L;
            labelsProp.GetArrayElementAtIndex(1).objectReferenceValue = label_R;
            labelsProp.GetArrayElementAtIndex(2).objectReferenceValue = label_DL;
            labelsProp.GetArrayElementAtIndex(3).objectReferenceValue = label_DR;

            so.ApplyModifiedPropertiesWithoutUndo();

            // 프리팹 저장
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabAsset);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static TMP_Text CreateIdLabel(Transform parent, string name, Vector3 localPos, Vector3 localEuler, float referenceHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;

            var label = go.AddComponent<TextMeshPro>();
            label.text = "AAAA 000000 [0]";
            // 폰트/사이즈를 컨테이너 높이 비례로 (스케일 자동 동기)
            label.fontSize = referenceHeight * 0.30f;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = false;
            label.color = new Color(0.07f, 0.07f, 0.07f);

            var rt = label.rectTransform;
            rt.sizeDelta = new Vector2(referenceHeight * 0.6f, referenceHeight * 0.13f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            return label;
        }

        // ───────────────────────────── 폴더 ─────────────────────────────
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
