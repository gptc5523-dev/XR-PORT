#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using AIXRCrane.EditorTools;
using UnityEditor;
using UnityEngine;
using AIXRCrane.Crane.Sts;                             // StsPartNames
using UnityEngine.XR.Interaction.Toolkit.Interactables;       // XRGrabInteractable

namespace AIXRCrane.Ship.EditorTools
{
    /// <summary>컨테이너선 생성 에디터 메뉴 — 선체+해치+선수루+거주구/펀넬을 루트 "ContainerShip" 아래 자식으로 조립.
    /// 머티리얼은 인스턴스(에셋 미저장)라 프로젝트 오염 없음.</summary>
    public static class ShipCreator
    {
        const string RootName = ShipConfig.ShipRootName;   // SSOT — 접안부·부두 재생성과 공유

        // relativeHeight = D_bound / (d × 2 × tan(FOV_v/2)), D_bound=12.70m(40ft 대각), FOV_v=96°(Quest3)
        //   상대 높이라 단위 무관 — 거리(m)를 그대로 넣지 말 것.
        const float CargoLod0Height = 0.3787f;   // ≈15.1m 이내 원본
        const float CargoLod1Height = 0.0229f;   // ≈250m 밖 컬링

        // ── 통일 도장 팔레트 (네이비 선체 기준 실선 livery) ──
        // 선체: 토프사이드 네이비 / 선저 적방오 / 부트탑 흑 / 갑판 녹색
        static readonly Color CTopside = new Color(0.08f, 0.12f, 0.27f);   // 다크 네이비
        static readonly Color CBottom  = new Color(0.45f, 0.12f, 0.09f);   // 적갈 방오도장
        static readonly Color CBoot    = new Color(0.04f, 0.04f, 0.05f);   // 흑 부트탑
        static readonly Color CDeck    = new Color(0.18f, 0.26f, 0.20f);   // 갑판 녹색(deck green)
        // 화물: 코밍 미들그레이 / 커버 스틸그레이
        static readonly Color CCoaming = new Color(0.33f, 0.34f, 0.36f);   // 해치 코밍
        static readonly Color CCover   = new Color(0.45f, 0.46f, 0.48f);   // 해치 커버(스틸 그레이)
        // 상부구조: 하우스 화이트 / 창 다크 / 펀넬 흑 / 밴드 적
        static readonly Color CHouse   = new Color(0.90f, 0.91f, 0.92f);   // 거주구 화이트
        static readonly Color CWindow  = new Color(0.09f, 0.12f, 0.15f);   // 창문대
        static readonly Color CFunnel  = new Color(0.10f, 0.11f, 0.12f);   // 펀넬 흑(차콜)
        static readonly Color CBand    = new Color(0.78f, 0.16f, 0.12f);   // 펀넬 식별 밴드(적)
        // 의장: 강철 라이트그레이 / 등 발광 / 차콜 / 구명정 오렌지 / 프로펠러 청동
        static readonly Color CSteel   = new Color(0.64f, 0.65f, 0.68f);   // 마스트·라싱브리지·난간·윈치
        static readonly Color CLight   = new Color(0.95f, 0.93f, 0.70f);   // 항해등(발광)
        static readonly Color CDark    = new Color(0.12f, 0.12f, 0.13f);   // 앵커·드럼·계선주·방향타 캐스트강(차콜)
        static readonly Color CBoat    = new Color(0.92f, 0.42f, 0.05f);   // 구명정(국제 오렌지)
        static readonly Color CProp    = new Color(0.62f, 0.46f, 0.22f);   // 프로펠러(청동)

        [MenuItem("Model/PG/선박/컨테이너선 생성 (선체+상부구조)", false, 10)]
        public static void CreateShip()
        {
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Container Ship");
            root.transform.position = Vector3.zero;   // 흘수선 y=0, 미드십·중심선 원점

            // Hull·HatchCovers엔 MeshCollider를 부여한다 — 갑판 적재 화물(동적 강체)이 갑판/해치커버 위에
            //   물리적으로 얹히고, 토플돼 굴러떨어져도 갑판에 받쳐 배를 뚫고 빠지지 않게(상부구조/의장은 콜라이더 불필요).
            AddPart(root, "Hull", ProceduralShipHull.Build(), new[]
            {
                Mat(CTopside, 0.10f, 0.35f),
                Mat(CBottom,  0.05f, 0.25f),
                Mat(CBoot,    0.10f, 0.30f),
                Mat(CDeck,    0.05f, 0.20f),
            }, collider: true);

            AddPart(root, "HatchCovers", ProceduralShipStructures.BuildHatches(), new[]
            {
                Mat(CCoaming, 0.10f, 0.30f),
                Mat(CCover,   0.05f, 0.25f),
            }, collider: true);

            AddPart(root, "Forecastle", ProceduralShipStructures.BuildForecastle(), new[]
            {
                Mat(CDeck,    0.05f, 0.25f),
                Mat(CTopside, 0.10f, 0.30f),
            });

            AddPart(root, "Accommodation", ProceduralShipStructures.BuildAccommodation(), new[]
            {
                Mat(CHouse,  0.04f, 0.30f),
                Mat(CWindow, 0.00f, 0.80f),
                Mat(CFunnel, 0.10f, 0.30f),
                Mat(CBand,   0.05f, 0.35f),
            });

            AddPart(root, "Details", ProceduralShipDetails.BuildDetails(), new[]
            {
                Mat(CSteel, 0.30f, 0.40f),
                MatEmissive(CLight, 1.6f),
                Mat(CDark, 0.25f, 0.30f),
                Mat(CBoat, 0.05f, 0.45f),
                Mat(CProp, 0.75f, 0.55f),
            });

            Debug.Log($"[Ship] 컨테이너선 생성 완료 — LOA {ShipConfig.LoaMeters}m × Beam {ShipConfig.BeamMeters:0.0}m " +
                      $"({ShipConfig.DeckRows}열 역산) × Depth {ShipConfig.DepthMeters}m, 스케일 1/{1f / ShipConfig.ModelScale:0}. " +
                      $"파트: 선체+해치+선수루+거주구/펀넬.");

            // 생성 직후 자동 접안 정렬. 크레인이 아직 없으면 배는 원점에 두고 안내만(재생성 시 자동 접안).
            if (!ShipBerthMenu.TryBerth(root, out string berthMsg))
                Debug.LogWarning(berthMsg);   // 크레인 미존재 — 배는 원점, 안내만
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }

        // ── 갑판에 '우리 컨테이너'(절차 메시) 그랩 가능하게 적재 — 위치는 CargoSlots에서 산출 ──
        [MenuItem("Model/PG/선박/컨테이너선에 컨테이너 적재 (그랩)", false, 11)]
        public static void LoadShipCargo()
        {
            var ship = GameObject.Find(RootName);
            if (ship == null) { Debug.LogWarning("[Ship] ContainerShip 없음 — 먼저 컨테이너선을 생성하세요."); return; }
            var oldT = ship.transform.Find("ShipCargo");
            if (oldT != null) Undo.DestroyObjectImmediate(oldT.gameObject);

            var parent = new GameObject("ShipCargo");
            Undo.RegisterCreatedObjectUndo(parent, "Load Ship Cargo");
            parent.transform.SetParent(ship.transform, false);

            // 갑판 화물 = 정밀 FBX(Container_40ft.fbx, 야드와 동일 머티리얼로 색 자동 일치) + LOD1(원저자 제공, 삼각형 1/10).
            // LOD0=근접(정밀), LOD1=원경 — LODGroup 둘 다 넣어 삼각형 예산과 디자인을 함께 지킨다.
            const string CargoFbx    = "Assets/Container/Models/Container_40ft.fbx";
            const string CargoLodFbx = "Assets/Container/Models/LOD1/Container_40ft_LOD1.fbx";
            ContainerFinal4Builder.EnsureMaterials();
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(CargoFbx);
            if (src == null) { Debug.LogError($"[Ship] 컨테이너 FBX 없음: {CargoFbx}"); return; }
            var lodSrc = AssetDatabase.LoadAssetAtPath<GameObject>(CargoLodFbx);   // 없으면 정밀본만

            // 정밀 FBX 규약: 프리팹 루트가 자체 스케일, 길이축 Z, 피봇 중앙 높이(CargoSlots의 y와 일치) — 스케일·회전 건드리지 않는다.
            // 프로브도 실제 배치와 같은 보정을 걸어 재야 크기 오진을 피한다.
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(src);
            float pm = SceneUtil.BoundsOrPoint(probe).size.y;
            float pt = ProceduralContainerMesh.HeightStd * ShipConfig.ModelScale;
            if (pm > 1e-6f) probe.transform.localScale *= pt / pm;
            var pb = SceneUtil.BoundsOrPoint(probe);
            Object.DestroyImmediate(probe);

            float ms = ShipConfig.ModelScale;
            // 콜라이더는 루트 로컬 공간 — 루트 스케일(4.1667)로 나누고, FBX 의 −90° X 회전(Blender Z-up→Unity
            //   Y-up)으로 로컬 Y·Z 가 월드에서 뒤바뀐다: 로컬 (폭,길이,높이) 순 → 월드 (폭,높이,길이).

            // 갑판/해치커버 받침 콜라이더 보장 — 동적 화물이 갑판을 뚫고 떨어지지 않게(멱등).
            EnsureRestingColliders(ship);

            var slots = ProceduralShipStructures.CargoSlots();

            // 슬롯은 '베이→열→단' 순서 — 낱개로 균등 샘플링하면 윗단만 집혀 컨테이너가 공중에 뜬다.
            //   '열(같은 x·z)' 단위로 뽑아 그 열의 스택을 통째로 세운다.
            var columns = slots
                .GroupBy(v => (Mathf.RoundToInt(v.x * 1000f), Mathf.RoundToInt(v.z * 1000f)))
                .Select(g => g.OrderBy(v => v.y).ToList())   // 아래 단부터
                .ToList();

            // 갑판 전체에 고르게(앞부터 채우면 선수가 빈다). 0 이면 슬롯 전부.
            int want = ShipConfig.DeckCargoCount <= 0
                     ? slots.Count
                     : Mathf.Clamp(ShipConfig.DeckCargoCount, 0, slots.Count);
            var picked = new List<Vector3>();
            // 필요한 컨테이너 수를 채울 때까지 열을 균등 간격으로 고른다.
            int needCols = Mathf.Clamp(Mathf.CeilToInt(want / Mathf.Max(1f, (float)slots.Count / Mathf.Max(1, columns.Count))),
                                       0, columns.Count);
            float cstep = needCols > 0 ? (float)columns.Count / needCols : 1f;
            // 윗단은 확률로 얹는다 — 아래 단은 항상 넣는다(빠지면 윗단이 허공에 뜬다).
            var rng = new System.Random(ShipConfig.DeckStackSeed);
            int upper = 0;
            var used = new HashSet<int>();
            for (int k = 0; k < needCols && picked.Count < want; k++)
            {
                int ci = Mathf.Min(columns.Count - 1, Mathf.FloorToInt(k * cstep));
                if (!used.Add(ci)) continue;
                var col = columns[ci];
                for (int t = 0; t < col.Count; t++)
                {
                    if (picked.Count >= want) break;   // 잘려도 아래 단부터라 뜨지 않는다
                    if (t > 0 && rng.NextDouble() > ShipConfig.DeckSecondTierRatio) break;  // 윗단 생략
                    picked.Add(col[t]);
                    if (t > 0) upper++;
                }
            }

            foreach (var sl in picked)
            {
                // 이름에 'Container' 포함 → ContainerPhysicsStabilizer·바닥가드 대상에 포함.
                var g = new GameObject(StsPartNames.ShipContainer);
                g.transform.SetParent(parent.transform, false);
                g.transform.localPosition = new Vector3(sl.x, sl.y, sl.z) * ms;

                var g0 = FitCargo(src, g.transform, "LOD0", ms);
                var r0 = g0.GetComponentsInChildren<Renderer>();
                Renderer[] r1 = System.Array.Empty<Renderer>();
                if (lodSrc != null)
                    r1 = FitCargo(lodSrc, g.transform, "LOD1", ms).GetComponentsInChildren<Renderer>();

                var lg = g.AddComponent<LODGroup>();
                lg.SetLODs(r1.Length > 0
                    ? new[] { new LOD(CargoLod0Height, r0), new LOD(CargoLod1Height, r1) }
                    : new[] { new LOD(CargoLod1Height, r0) });
                lg.RecalculateBounds();

                // 콜라이더는 회전 없는 빈 루트에 붙으므로 축 보정이 필요 없다.
                var box = g.AddComponent<BoxCollider>(); box.center = Vector3.zero;
                box.size = new Vector3(ProceduralContainerMesh.StdWidth, ProceduralContainerMesh.HeightStd,
                                       ProceduralContainerMesh.Length40ft) * ms;
                var rb = g.AddComponent<Rigidbody>(); rb.useGravity = true;
                ContainerPhysics.Apply(rb, box);
                var grab = g.AddComponent<XRGrabInteractable>(); grab.useDynamicAttach = true;
            }
            float inv = 1f / ms;
            Debug.Log($"[Ship] 갑판 컨테이너 적재 — 정밀 FBX {picked.Count}개 / 슬롯 {slots.Count}(열 {columns.Count}) " +
                      $"(최대 {ShipConfig.DeckMaxTiers}단 · 1단 {picked.Count - upper} + 2단 {upper}" +
                      $"(확률 {ShipConfig.DeckSecondTierRatio:P0}, 시드 {ShipConfig.DeckStackSeed})) · 야드와 동일 머티리얼 · " +
                      $"실측 {pb.size.z * inv:F2}L × {pb.size.x * inv:F2}W × {pb.size.y * inv:F2}H m · " +
                      $"삼각형 약 {picked.Count * 110302L:N0}");
            Selection.activeGameObject = parent;
        }

        /// <summary>화물 FBX 를 꺼내 ISO 높이에 맞춘다. localScale 을 1 로 리셋하면 안 된다 —
        /// 정밀본은 루트가 자체 스케일을 가져 리셋 시 24배로 튄다. 반드시 곱한다.</summary>
        static GameObject FitCargo(GameObject src, Transform parent, string name, float ms)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            float m = SceneUtil.BoundsOrPoint(go).size.y;
            float t = ProceduralContainerMesh.HeightStd * ms;
            if (m > 1e-6f) go.transform.localScale *= t / m;

            // 원점 규약이 FBX 마다 다르다(정밀본 바닥, LOD1 중앙) — 피봇을 바운즈 중앙으로 통일해 LOD 전환 튐 방지.
            go.transform.position += parent.position
                - SceneUtil.BoundsOrPoint(go).center;
            return go;
        }

        static void AddPart(GameObject root, string name, Mesh mesh, Material[] mats, bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            // 정적(비볼록) MeshCollider — 동적 화물의 받침면. 메시 그대로라 선체 스케일과 자동 정합.
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // 기존 씬에 이미 구워진 선체(Hull/HatchCovers)에 MeshCollider가 없으면 멱등으로 보강 — 동적 화물 받침면.
        //   재생성(CreateShip) 없이 적재 메뉴만 다시 돌려도 받침이 생기게 한다.
        static void EnsureRestingColliders(GameObject ship)
        {
            foreach (var name in new[] { "Hull", "HatchCovers" })
            {
                var part = ship.transform.Find(name);
                if (part == null) continue;
                if (part.GetComponent<MeshCollider>() != null) continue;
                var mf = part.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Undo.AddComponent<MeshCollider>(part.gameObject).sharedMesh = mf.sharedMesh;
            }
        }

        static Material Mat(Color c, float metallic, float smooth)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "Ship_Mat" };
            if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))      m.SetColor("_Color", c);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            return m;
        }

        static Material MatEmissive(Color c, float intensity)
        {
            var m = Mat(c, 0f, 0.6f);
            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", c * intensity);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }
    }
}
#endif
