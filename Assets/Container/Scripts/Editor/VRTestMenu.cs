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
        // 기존 SpawnContainers Std20과 동일 색상 풀
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

        [MenuItem("Container/컨테이너 생성 (1개)", false, 22)]
        public static void SpawnSingleProcedural()
        {
            SpawnSingle(length: ProceduralContainerMesh.Length20ft, suffix: "");
        }

        [MenuItem("Container/컨테이너 생성 40ft (1개)", false, 23)]
        public static void SpawnSingleProcedural40ft()
        {
            SpawnSingle(length: ProceduralContainerMesh.Length40ft, suffix: "40ft");
        }

        [MenuItem("Container/컨테이너 생성 20ft+40ft (각 1개)", false, 24)]
        public static void SpawnBothSizes()
        {
            // 기존 컨테이너 모두 삭제 (Std Set 동일 패턴)
            var existing = Object.FindObjectsByType<CubeReset>(FindObjectsSortMode.None);
            foreach (var c in existing) Undo.DestroyObjectImmediate(c.gameObject);

            // 폭(가로) 방향으로 나란히 — 컨테이너 폭 + 여유만큼 좌우로 벌림
            const float gap = 0.03f;
            const float containerWidth = 2.438f / 24f;   // 미니어처 폭(1/24)
            float half = (containerWidth + gap) * 0.5f;

            SpawnOneOffset(ProceduralContainerMesh.Length20ft, "20ft", -half);
            var last = SpawnOneOffset(ProceduralContainerMesh.Length40ft, "40ft", +half);

            Selection.activeGameObject = last;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
            Debug.Log("[VRTestMenu] Procedural 20ft + 40ft 각 1개 스폰 — kinematic 고정. 그랩 후 놓으면 물리 활성화.");
        }

        // 단일 컨테이너를 hOffset(폭 방향) 위치에 스폰. 에디터 미리보기에서도 겹치지 않게 실제 위치를 벌려 둔다.
        static GameObject SpawnOneOffset(float length, string suffix, float hOffset)
        {
            var go = BuildOne(PaletteColors[Random.Range(0, PaletteColors.Length)],
                              "Container_Procedural_" + suffix, length);
            var reset = go.GetComponent<CubeReset>();
            if (reset != null) { reset.SetHorizontalOffset(hOffset); reset.SetVerticalOffset(0f); }
            // 메시 긴 축이 X라 폭(Z) 방향으로 벌려 나란히 배치 → 에디터에서 둘이 겹쳐 보이던 문제 해결.
            // Play 진입 시 CubeReset.PlaceInFrontOfCamera 가 카메라 기준 위치로 다시 배치한다.
            go.transform.position = new Vector3(0f, 0f, hOffset);
            // 스폰 직후엔 kinematic 고정(물리 튕김 방지). 그랩 후 놓으면 CubeReset 이 풀어줌.
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Undo.RegisterCreatedObjectUndo(go, "Spawn 20ft + 40ft");
            return go;
        }

        static void SpawnSingle(float length, string suffix)
        {
            // 기존 컨테이너 모두 삭제 (Std Set 동일 패턴)
            var existing = Object.FindObjectsByType<CubeReset>(FindObjectsSortMode.None);
            foreach (var c in existing) Undo.DestroyObjectImmediate(c.gameObject);

            string name = string.IsNullOrEmpty(suffix) ? "Container_Procedural" : "Container_Procedural_" + suffix;
            var go = BuildOne(PaletteColors[Random.Range(0, PaletteColors.Length)], name, length);
            var reset = go.GetComponent<CubeReset>();
            if (reset != null) reset.SetHorizontalOffset(0f);

            Undo.RegisterCreatedObjectUndo(go, "Spawn Procedural");
            Selection.activeGameObject = go;

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();

            Debug.Log($"[VRTestMenu] Procedural 컨테이너 1개 스폰 (length={length}m, mesh bounds: {go.GetComponent<MeshFilter>().sharedMesh.bounds.size})");
        }

        [MenuItem("Container/컨테이너 생성 (2x2)", false, 25)]
        public static void SpawnProcedural2x2()
        {
            var existing = Object.FindObjectsByType<CubeReset>(FindObjectsSortMode.None);
            foreach (var c in existing) Undo.DestroyObjectImmediate(c.gameObject);

            // 미니어처 크기 (1/24): 폭(Z) 0.102m, 높이(Y) 0.108m
            const float gap = 0.005f;
            const float containerWidth  = 2.438f / 24f;
            const float containerHeight = 2.591f / 24f;
            float halfH = (containerWidth + gap) * 0.5f;
            float stackV = containerHeight + gap;

            // (h, v) = (horizontalOffset, verticalOffset) — 좌하/우하/좌상/우상
            var positions = new (float h, float v)[]
            {
                (-halfH, 0f),
                (+halfH, 0f),
                (-halfH, stackV),
                (+halfH, stackV),
            };
            Color[] colors = { PaletteColors[0], PaletteColors[1], PaletteColors[2], PaletteColors[3] };
            string[] names = { "Container_Procedural_BL", "Container_Procedural_BR", "Container_Procedural_TL", "Container_Procedural_TR" };

            GameObject last = null;
            for (int i = 0; i < 4; i++)
            {
                var go = BuildOne(colors[i], names[i]);
                var reset = go.GetComponent<CubeReset>();
                if (reset != null)
                {
                    reset.SetHorizontalOffset(positions[i].h);
                    reset.SetVerticalOffset(positions[i].v);
                }
                // 4개가 동시에 (0,0,0)에서 시작 → 콜라이더 겹쳐서 서로 튕기는 문제 방지.
                // 스폰 직후엔 kinematic 고정. 사용자가 그랩 후 놓으면 CubeReset 이 자동으로 풀어줌.
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                Undo.RegisterCreatedObjectUndo(go, "Spawn Procedural 2x2");
                last = go;
            }
            if (last != null)
            {
                Selection.activeGameObject = last;
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) sv.FrameSelected();
            }
            Debug.Log("[VRTestMenu] Procedural 컨테이너 4개 (2x2) 스폰 — kinematic 으로 고정. 그랩 후 놓으면 물리 활성화.");
        }

        // Quay_Ground 아스팔트 위에 20ft·40ft 각 2개를 야드처럼 고정 배치.
        // CubeReset 미부착 → Play 시 카메라 앞으로 순간이동하지 않고 부두에 그대로 안착(크레인/손 집기 가능).
        [MenuItem("Container/부두에 컨테이너 배치", false, 26)]
        public static void PlaceContainersOnQuay()
        {
            // 재실행 대비 — 기존 야드 컨테이너 제거
            foreach (var existing in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (existing.name.StartsWith("Yard_Container")) Undo.DestroyObjectImmediate(existing);

            var quay = GameObject.Find("Quay_Ground");
            if (quay == null)
                Debug.LogWarning("[VRTestMenu] Quay_Ground 가 없습니다 — 월드 원점 기준으로 배치합니다. " +
                                 "먼저 'Container/Create Quay Ground' 실행을 권장합니다.");

            // 긴 축 = Z(안벽과 나란히) → 로컬 X(길이)를 월드 Z로 돌리는 Y축 90° 회전.
            // 위치는 바다(+X) 아웃리치 쪽으로 한 줄 배치(z=0 → 트롤리 X 이동만으로 집기 가능).
            // 바닥 피봇이라 y=아스팔트 윗면. 시작 관통 방지로 살짝 띄움.
            // ※ 좌표는 현재 크레인 위치(루트 x≈-0.333)에 맞춘 값 — 크레인을 옮기면 같이 조정 필요.
            const float yRest = 0.002f;
            const float ContainerH = ProceduralContainerMesh.HeightStd * ProceduralContainerMesh.DefaultMiniatureScale;
            const float yStack = yRest + ContainerH + 0.002f;   // 아래 칸 윗면 + 2mm 여유
            var rot = Quaternion.Euler(0f, 90f, 0f);
            // 하단 줄: 40·40·20·20·40 (육지→바다 순, 중심 간격 0.12m, 폭 ≈0.102 → 틈 ≈1.8cm).
            // 상단 2칸: 40ft_A·20ft_A 위에 같은 X로 같은 사이즈 2단 적층.
            var specs = new (float len, string name, Vector3 pos)[]
            {
                (ProceduralContainerMesh.Length40ft, "Yard_Container_40ft_A", new Vector3(0.60f, yRest,  0f)),
                (ProceduralContainerMesh.Length40ft, "Yard_Container_40ft_B", new Vector3(0.72f, yRest,  0f)),
                (ProceduralContainerMesh.Length20ft, "Yard_Container_20ft_A", new Vector3(0.84f, yRest,  0f)),
                (ProceduralContainerMesh.Length20ft, "Yard_Container_20ft_B", new Vector3(0.96f, yRest,  0f)),
                (ProceduralContainerMesh.Length40ft, "Yard_Container_40ft_D", new Vector3(1.08f, yRest,  0f)),  // 20ft_B 바다쪽 옆
                (ProceduralContainerMesh.Length40ft, "Yard_Container_40ft_C", new Vector3(0.60f, yStack, 0f)),  // 40ft_A 위
                (ProceduralContainerMesh.Length20ft, "Yard_Container_20ft_C", new Vector3(0.84f, yStack, 0f)),  // 20ft_A 위
            };

            GameObject last = null;
            for (int i = 0; i < specs.Length; i++)
            {
                var s = specs[i];
                var go = BuildOne(PaletteColors[i % PaletteColors.Length], s.name, s.len, withReset: false);
                go.transform.SetPositionAndRotation(s.pos, rot);
                if (quay != null) go.transform.SetParent(quay.transform, worldPositionStays: true);
                // 정적 화물 — 중력으로 아스팔트(VirtualFloor)에 안착. 크레인/손이 집으면 풀림.
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }
                Undo.RegisterCreatedObjectUndo(go, "Place Containers on Quay");
                last = go;
            }

            if (last != null)
            {
                Selection.activeGameObject = last;
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) sv.FrameSelected();
            }
            Debug.Log("[VRTestMenu] Quay_Ground 에 컨테이너 7개 배치 — 하단 40·40·20·20·40 한 줄(긴 축 Z, 육지→바다), " +
                      "상단 40ft·20ft 1개씩 2단 적층. CubeReset 없음 → Play 시 카메라 앞으로 안 튐.");
        }

        // ───────────────────────────── 단일 컨테이너 빌더 ─────────────────────────────
        // withReset:false → CubeReset 미부착(Play 시 카메라 앞으로 순간이동하지 않음). 야드 고정 배치용.
        static GameObject BuildOne(Color bodyColor, string name, float length = -1f, bool withReset = true)
        {
            // 1. 메시 (매번 새로 — 미니어처 스케일 1/24, 바닥 피봇, X=긴 방향)
            //    centerPivot:false → 피봇(원점)이 컨테이너 바닥면. 루트=바닥면 규칙을 크레인(루트=접지)·
            //    VirtualFloor(윗면 y=0)과 통일해, y=0에 두면 바닥에 정확히 올라앉는다(가라앉지 않음).
            //    length 인자 양수면 BuildSized 로 임의 사이즈, 아니면 기본 20ft Build
            Mesh mesh = length > 0f
                ? ProceduralContainerMesh.BuildSized(length, ProceduralContainerMesh.StdWidth, ProceduralContainerMesh.HeightStd, name + "_Mesh", centerPivot: false)
                : ProceduralContainerMesh.Build(name + "_Mesh", centerPivot: false);

            // 2. 셰이더
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // 3. 머티리얼 인스턴스 4개 (서브메시 순서: 0=Body, 1=Door, 2=Frame, 3=Castings)
            var matBody = MakeMat(litShader, bodyColor, metallic: 0.10f, smoothness: 0.40f, suffix: "_Body");
            var matDoor = MakeMat(litShader, MulColor(bodyColor, 0.80f), metallic: 0.10f, smoothness: 0.40f, suffix: "_Door");
            var matFrame = MakeMat(litShader, new Color(0.18f, 0.18f, 0.20f), metallic: 0.55f, smoothness: 0.45f, suffix: "_Frame");
            var matCastings = MakeMat(litShader, new Color(0.10f, 0.10f, 0.11f), metallic: 0.40f, smoothness: 0.25f, suffix: "_Castings");

            // 4. GameObject + 메시
            var root = new GameObject(name);
            var mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { matBody, matDoor, matFrame, matCastings };

            // 5. 콜라이더 (메시 bounds 기반 — 스케일에 자동 동기)
            var box = root.AddComponent<BoxCollider>();
            var bounds = mesh.bounds;
            box.center = bounds.center;
            box.size = bounds.size;

            // 6. VR 인터랙션 (기존 SpawnContainers Std Set 패턴 동일)
            var rb = root.AddComponent<Rigidbody>();
            rb.useGravity = true;
            // useDynamicAttach: 손이 닿은 지점이 그립 포인트가 되도록 (피봇 스냅 방지)
            var grab = root.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;
            if (withReset) root.AddComponent<CubeReset>();

            // 7. 적층 안정화(방식 A) — 컨테이너 한정(전역 물리 미변경). 기존 씬 컨테이너는 ContainerPhysicsStabilizer가 처리.
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
