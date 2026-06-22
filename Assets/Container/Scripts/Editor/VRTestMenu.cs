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

        [MenuItem("Container/컨테이너 생성 (1개)", false, 2)]
        public static void SpawnSingleProcedural()
        {
            SpawnSingle(length: ProceduralContainerMesh.Length20ft, suffix: "");
        }

        [MenuItem("Container/컨테이너 생성 40ft (1개)", false, 3)]
        public static void SpawnSingleProcedural40ft()
        {
            SpawnSingle(length: ProceduralContainerMesh.Length40ft, suffix: "40ft");
        }

        [MenuItem("Container/컨테이너 생성 20ft+40ft (각 1개)", false, 4)]
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
            var go = BuildOne(PaletteRandom(),
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
            var go = BuildOne(PaletteRandom(), name, length);
            var reset = go.GetComponent<CubeReset>();
            if (reset != null) reset.SetHorizontalOffset(0f);

            Undo.RegisterCreatedObjectUndo(go, "Spawn Procedural");
            Selection.activeGameObject = go;

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();

            Debug.Log($"[VRTestMenu] 분해형 컨테이너 1개 스폰 (length={length}m, bounds: {go.GetComponent<BoxCollider>().size}, parts: {go.GetComponentsInChildren<MeshFilter>().Length}개)");
        }

        [MenuItem("Container/컨테이너 생성 (2x2)", false, 5)]
        public static void SpawnProcedural2x2()
        {
            // 재실행 대비 — 기존 절차 컨테이너 제거(CubeReset 유무 무관, 이름으로)
            foreach (var existing in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (existing.name.StartsWith("Container_Procedural")) Undo.DestroyObjectImmediate(existing);

            var quay = GameObject.Find(Container.Crane.Sts.StsPartNames.QuayGround);
            if (quay == null)
                Debug.LogWarning("[VRTestMenu] Quay_Ground 가 없습니다 — 월드 원점 기준 배치. 먼저 'Container/부두 바닥 생성' 권장.");

            // 부두 야드('부두에 컨테이너 배치', X 0.60~1.08) 바다쪽 옆에 2x2 블록을 '고정' 배치.
            //   ★ CubeReset 미부착(withReset:false) → Play 시 카메라 앞 절대높이로 순간이동하지 않고 부두에 안착.
            //     (기존엔 CubeReset이 카메라 앞 1.4m로 옮겨, 1/24 리그에선 크레인 높이로 떠버렸음 — 그래서 부두 고정으로 변경.)
            //   긴 축 Z(야드와 동일 정렬), 좌/우 칸은 X로 폭만큼 벌리고 위 칸은 Y로 적층.
            const float yRest = 0.002f;
            const float ContainerH = ProceduralContainerMesh.HeightStd * ProceduralContainerMesh.DefaultMiniatureScale;
            const float yStack = yRest + ContainerH;   // 위 칸을 아래 칸 지붕에 '딱' 안착(군더더기 +4mm 제거 — 공중부양 원인). contactOffset이 접촉 감지만 하고 안착 간격은 0(restOffset)이라 면이 맞닿음.
            const float colGap = ProceduralContainerMesh.StdWidth * ProceduralContainerMesh.DefaultMiniatureScale + 0.006f;
            const float baseX = 1.22f;   // 야드 마지막(1.08) 바다쪽 옆
            var rot = Quaternion.Euler(0f, 90f, 0f);

            var specs = new (Vector3 pos, string name)[]
            {
                (new Vector3(baseX,          yRest,  0f), "Container_Procedural_BL"),
                (new Vector3(baseX + colGap, yRest,  0f), "Container_Procedural_BR"),
                (new Vector3(baseX,          yStack, 0f), "Container_Procedural_TL"),
                (new Vector3(baseX + colGap, yStack, 0f), "Container_Procedural_TR"),
            };

            GameObject last = null;
            for (int i = 0; i < specs.Length; i++)
            {
                var go = BuildOne(PaletteAt(i), specs[i].name, withReset: false);
                go.transform.SetPositionAndRotation(specs[i].pos, rot);
                if (quay != null) go.transform.SetParent(quay.transform, worldPositionStays: true);
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }   // 부두에 중력 안착, 집으면 풀림
                Undo.RegisterCreatedObjectUndo(go, "Spawn Procedural 2x2");
                last = go;
            }
            if (last != null)
            {
                Selection.activeGameObject = last;
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) sv.FrameSelected();
            }
            Debug.Log("[VRTestMenu] 절차 컨테이너 2x2 — 부두 야드 바다쪽 옆에 고정 배치(CubeReset 없음 → 카메라 앞으로 안 튐).");
        }

        // Quay_Ground 육지쪽 야드에 20ft·40ft 혼합 20개를 적하 시작 상태로 고정 배치(X-0.55/0.0).
        // CubeReset 미부착 → Play 시 카메라 앞으로 순간이동하지 않고 부두에 그대로 안착(크레인/손 집기 가능).
        [MenuItem("Container/부두에 컨테이너 배치", false, 6)]
        public static void PlaceContainersOnQuay()
        {
            // 재실행 대비 — 기존 야드 컨테이너 제거.
            // 순회 중 부모를 DestroyImmediate하면 자식도 즉시 파괴되어 배열 뒷항목이 죽은 채 남음 →
            // 먼저 매칭 대상만 모은 뒤 파괴하고, 파괴 시점에도 null 가드(자식이 먼저 죽었을 수 있음).
            var stale = new System.Collections.Generic.List<GameObject>();
            foreach (var existing in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (existing != null && existing.name.StartsWith("Yard_Container")) stale.Add(existing);
            foreach (var existing in stale)
                if (existing != null) Undo.DestroyObjectImmediate(existing);

            var quay = GameObject.Find(Container.Crane.Sts.StsPartNames.QuayGround);
            if (quay == null)
                Debug.LogWarning("[VRTestMenu] Quay_Ground 가 없습니다 — 월드 원점 기준으로 배치합니다. " +
                                 "먼저 'Container/Create Quay Ground' 실행을 권장합니다.");

            // 긴 축 = Z(안벽과 나란히) → 로컬 X(길이)를 월드 Z로 돌리는 Y축 90° 회전.
            // 바닥 피봇이라 y=아스팔트 윗면. 시작 관통 방지로 살짝 띄움.
            const float yRest = 0.002f;
            const float scale  = ProceduralContainerMesh.DefaultMiniatureScale;

            // 컨테이너를 '육지쪽 야드'에 둔다(양하 후 상태/적하 시작 상태에 해당하는 배치).
            // ── 단일 1단 행(계산 배치) — 추측 금지: 씬의 실제 레일·갠트리 좌표에서 산출 ──
            //   · X: STS_Crane의 Rail_Land 월드 X(= Quay가 노란 Lane을 그리는 바로 그 X)에서
            //        (Lane오프셋0.045 + Lane반폭0.007 + 여유0.04 + 컨테이너 반폭)만큼 '육지쪽'으로 → Lane 비침범.
            //        레일에 최대한 붙여(가장 가깝게) 트롤리 백리치 도달을 보장. 단일 X = 한 줄.
            //   · Z: GantryMover.Min~Max(크레인이 닿는 Z) 안에 중앙정렬, 80%만 사용 → 들어가는 만큼만 배치.
            //   1단(토플 없음·깔끔)·20ft/40ft 교대 혼합·yaw 90°(길이축 Z).
            const float gapZ = 0.06f;
            float halfW  = ProceduralContainerMesh.StdWidth * scale * 0.5f;
            float len20m = ProceduralContainerMesh.Length20ft * scale;
            float len40m = ProceduralContainerMesh.Length40ft * scale;

            var craneGo = GameObject.Find("STS_Crane");
            float railLandX = -0.30f;   // 폴백(크레인 못 찾을 때)
            if (craneGo != null)
            {
                float lo = float.MaxValue;
                foreach (var tr in craneGo.GetComponentsInChildren<Transform>(true))
                    if (tr.name.StartsWith(Container.Crane.Sts.StsPartNames.RailPrefix) && tr.position.x < lo) lo = tr.position.x;
                if (lo < float.MaxValue) railLandX = lo;
            }
            float zMin = -1.0f, zMax = 1.0f;
            var gm = craneGo != null ? craneGo.GetComponent<Container.Crane.Sts.GantryMover>() : null;
            if (gm != null) { zMin = gm.Min; zMax = gm.Max; }
            float zMid = (zMin + zMax) * 0.5f, zUse = (zMax - zMin) * 0.8f;

            // 행 X — Land 레일에서 육지쪽으로 Lane+여유+반폭 (레일에 최대한 붙여 도달 보장)
            float rowX = railLandX - (0.045f + 0.007f + 0.04f + halfW);

            // 들어가는 만큼 교대 길이 누적(중앙정렬)
            var lens = new System.Collections.Generic.List<float>();
            float tot = 0f; int k = 0;
            while (true)
            {
                float len = (k % 2 == 1) ? len40m : len20m;
                float add = (lens.Count == 0) ? len : gapZ + len;
                if (tot + add > zUse) break;
                tot += add; lens.Add(len); k++;
            }
            if (lens.Count == 0) lens.Add(len20m);   // 최소 1개

            GameObject last = null;
            float zc = zMid - tot * 0.5f;
            for (int i = 0; i < lens.Count; i++)
            {
                bool is40 = (i % 2 == 1);
                float realLen = is40 ? ProceduralContainerMesh.Length40ft : ProceduralContainerMesh.Length20ft;
                float center = zc + lens[i] * 0.5f;
                string nm = $"Yard_Container_{(is40 ? "40ft" : "20ft")}_{i:00}";
                var go = BuildOne(PaletteAt(i), nm, realLen, withReset: false);
                go.transform.SetPositionAndRotation(new Vector3(rowX, yRest, center), Quaternion.Euler(0f, 90f, 0f));
                if (quay != null) go.transform.SetParent(quay.transform, worldPositionStays: true);
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }
                Undo.RegisterCreatedObjectUndo(go, "Place Containers on Quay");
                last = go;
                zc += lens[i] + gapZ;
            }

            if (last != null)
            {
                Selection.activeGameObject = last;
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) sv.FrameSelected();
            }
            Debug.Log($"[VRTestMenu] 단일 행 {lens.Count}개(1단·20/40 혼합) — 행 X={rowX:F3} (Land레일 {railLandX:F3}의 육지쪽, Lane 비침범), " +
                      $"갠트리 도달 Z[{zMin:F2},{zMax:F2}] 중앙정렬(80% 사용). 적하 시나리오가 집어 바다로 싣습니다. " +
                      $"한 줄 도달 한계라 {lens.Count}개 — 더 필요하면 갠트리 주행범위↑ 또는 여러 줄.");
        }

        // 컨테이너 야드 배치 — 칸을 확률(fillRate)로 띄엄띄엄만 채우는 '랜덤 듬성' 배치(컨테이너 수↓ → 랙 해소).
        //  ★ 좌표는 추측 없이 씬 실측에서 산출:
        //    - 격자: 주차장 마킹(Yard_Edge/Row/Slot) bounds → rows(폭 X)·slots(길이 Z)·Wc·slotL (BuildContainerYard와 동일)
        //    - '크레인 주변' 판정: STS_Crane GantryMover.Min~Max(갠트리 Z 도달 범위). 주변이면 좀 더 높이 쌓음.
        //  슬롯 피치 40ft → 40ft는 슬롯당 1개, 20ft는 앞뒤 2개. 같은 칸 스택은 같은 사이즈·색, 높이는 랜덤.
        //  isKinematic 고정(배경). Yard_Container(적하 시나리오)와 이름 분리(YardPark_), 재실행 시 자체 정리.
        [MenuItem("Container/컨테이너 야드 배치 (크레인 주변)", false, 7)]
        public static void FillContainerYard()
        {
            // 재실행 정리 — 이전 주차장 적치분만 제거(YardPark_ prefix).
            var stale = new System.Collections.Generic.List<GameObject>();
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && go.name.StartsWith("YardPark_")) stale.Add(go);
            foreach (var go in stale) if (go != null) Undo.DestroyObjectImmediate(go);

            var quay = GameObject.Find(Container.Crane.Sts.StsPartNames.QuayGround);
            if (quay == null)
            {
                Debug.LogWarning("[VRTestMenu] Quay_Ground 가 없습니다 — 먼저 'Ground/...' 부두 바닥을 생성하세요.");
                return;
            }

            // 주차장 마킹 실측 → 격자 역산(전체 AABB + 분리선 개수)
            Bounds yard = default; bool has = false; int rowLines = 0, slotLines = 0;
            foreach (var t in quay.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Yard_Edge" || t.name == "Yard_Row" || t.name == "Yard_Slot")
                {
                    var rend = t.GetComponent<Renderer>();
                    if (rend != null) { if (!has) { yard = rend.bounds; has = true; } else yard.Encapsulate(rend.bounds); }
                }
                if (t.name == "Yard_Row")  rowLines++;
                if (t.name == "Yard_Slot") slotLines++;
            }
            if (!has)
            {
                Debug.LogWarning("[VRTestMenu] 주차장 마킹(Yard_Edge/Row/Slot)이 없습니다 — 'Ground/컨테이너 주차장'을 켜고 부두 바닥을 재생성하세요.");
                return;
            }

            int rows  = rowLines + 1;     // 줄(폭축 X)
            int slots = slotLines + 1;    // 슬롯(길이축 Z, 40ft 피치)
            float xMin = yard.min.x, zMin = yard.min.z;
            float Wc    = (yard.max.x - xMin) / rows;     // 줄 폭(컨테이너 폭 ≈ 2.438/24)
            float slotL = (yard.max.z - zMin) / slots;    // 슬롯 피치(40ft ≈ 12.192/24)

            const float s     = ProceduralContainerMesh.DefaultMiniatureScale;
            const float yRest = 0.002f;
            float containerH  = ProceduralContainerMesh.HeightStd * s;       // 티어 적층 피치(지붕에 안착)
            float len20       = ProceduralContainerMesh.Length20ft * s;      // 20ft 미니어처 길이 — 한 슬롯(40ft)에 앞뒤 2개
            var rot = Quaternion.Euler(0f, 90f, 0f);                          // 길이축(로컬 X) → 월드 Z

            // '크레인 주변' Z 범위 = 갠트리 도달(GantryMover.Min~Max). 못 찾으면 주차장 Z 전체로 폴백.
            float gCenter = (yard.min.z + yard.max.z) * 0.5f, gReach = yard.max.z - yard.min.z;
            var craneGo = GameObject.Find("STS_Crane");
            var gm = craneGo != null ? craneGo.GetComponent<Container.Crane.Sts.GantryMover>() : null;
            if (gm != null) { gCenter = (gm.Min + gm.Max) * 0.5f; gReach = gm.Max - gm.Min; }
            float nearHalf = gReach * 0.5f;

            // 랜덤 듬성 배치 — 칸을 확률로 띄엄띄엄만 채워 컨테이너 수를 줄임(랙 해소). 같은 칸 스택은 같은 사이즈·색(선사 정렬감).
            const float fillRate = 0.4f;   // 칸 채움 확률(낮을수록 컨테이너 적음). 너무 많으면 ↓.

            int idx = 0, made = 0; GameObject last = null;

            GameObject Place(Color col, float lenReal, float x, float y, float z, string nm)
            {
                var go = BuildOne(col, nm, lenReal, withReset: false);
                go.transform.SetPositionAndRotation(new Vector3(x, y, z), rot);
                go.transform.SetParent(quay.transform, worldPositionStays: true);
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }   // 배경 고정(집으면 별도 해제)
                Undo.RegisterCreatedObjectUndo(go, "Fill Container Yard");
                made++; last = go;
                return go;
            }

            for (int j = 0; j < slots; j++)
            {
                float slotZ = zMin + slotL * (j + 0.5f);
                bool near = Mathf.Abs(slotZ - gCenter) <= nearHalf;          // 크레인 주변이면 좀 더 높이 쌓을 수 있음
                for (int r = 0; r < rows; r++)
                {
                    if (Random.value > fillRate) continue;                  // 듬성 — 대부분 빈 칸으로 수 감소
                    float rowX = xMin + Wc * (r + 0.5f);
                    bool is40 = Random.value < 0.5f;                        // 칸 사이즈 랜덤(40/20)
                    Color col = PaletteAt(idx);                             // 칸(스택) 공통색 — 같은 스택 같은 색
                    int tiers = Random.Range(1, (near ? 3 : 2) + 1);       // 높이 랜덤(주변 최대 3단, 밖 최대 2단)
                    for (int tr = 0; tr < tiers; tr++)
                    {
                        float y = yRest + tr * containerH;
                        if (is40)
                            Place(col, ProceduralContainerMesh.Length40ft, rowX, y, slotZ, $"YardPark_40_{idx:000}_{tr}");
                        else                                                // 20ft 슬롯: 앞뒤 2개로 40ft 슬롯을 메움
                            foreach (float dz in new[] { -(len20 * 0.5f + 0.002f), +(len20 * 0.5f + 0.002f) })
                                Place(col, ProceduralContainerMesh.Length20ft, rowX, y, slotZ + dz, $"YardPark_20_{idx:000}_{tr}");
                    }
                    idx++;
                }
            }

            if (last != null)
            {
                Selection.activeGameObject = last;
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) sv.FrameSelected();
            }
            Debug.Log($"[VRTestMenu] 야드 랜덤 배치 — 격자 {rows}줄×{slots}슬롯, 칸 채움률 {fillRate:P0}로 {idx}칸 사용, 컨테이너 {made}개. " +
                      $"같은 칸 스택=같은 사이즈·색, 높이 랜덤(주변 ~3단). 많/적으면 fillRate 조정.");
        }

        // ───────────────────────────── 컨테이너 빌더 (분해형/파트 분리) ─────────────────────────────
        // 모든 스폰 메뉴가 이걸 쓴다 → 컨테이너는 항상 분해형(부품마다 독립 GameObject, 디자이너가 바로 편집).
        // 물리/그랩은 루트 한 덩어리로 동작: 루트에 Rigidbody+XRGrabInteractable+단일 BoxCollider(전체 바운즈),
        //   파트 콜라이더는 끄고(addColliders:false) 루트 박스 하나로만 충돌 → 컴파운드 콜라이더 중복 방지.
        // withReset:false → CubeReset 미부착(Play 시 카메라 앞으로 순간이동하지 않음). 야드 고정 배치용.
        static GameObject BuildOne(Color bodyColor, string name, float length = -1f, bool withReset = true)
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
            //    [버그수정] 기존엔 전체 파트 합산 AABB(RootLocalBounds)라 도어 락바·핸들·힌지 돌출까지 포함돼
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
