#if UNITY_EDITOR
using ContainerProject.EditorTools;
using UnityEditor;
using UnityEngine;
using ContainerProject;                                       // ProceduralContainerMesh, ContainerPhysics
using UnityEngine.XR.Interaction.Toolkit.Interactables;       // XRGrabInteractable

namespace Container.Ship.EditorTools
{
    /// <summary>
    /// 컨테이너선 생성 에디터 메뉴 — 선체(1) + 해치(2) + 선수루(3) + 거주구·펀넬(4).
    /// 루트 "ContainerShip" 아래 파트별 자식(Hull/HatchCovers/Forecastle/Accommodation)으로 조립.
    /// 폴더는 Assets/Ship 로 분리(크레인과 독립), 메뉴는 기존 컨벤션대로 Container/ 루트에 평평하게
    /// 둔다([[feedback_menu_categories]]). 머티리얼은 인스턴스(에셋 미저장)라 프로젝트 오염 없음.
    /// </summary>
    public static class ShipCreator
    {
        const string RootName = ShipConfig.ShipRootName;   // SSOT — 접안부·부두 재생성과 공유

        // ── 갑판 화물 LOD 임계값 (화면 상대 높이) ──
        // 산식 : relativeHeight = D_bound / (d × 2 × tan(FOV_v / 2))     — 문서/컨테이너_규격.md Part 5 §10.7
        //   D_bound = √(12.192² + 2.438² + 2.591²) = 12.70 m (40ft 표준고 대각)
        //   FOV_v   = 96° (Quest 3 세로) → 2·tan(48°) = 2.2212
        //   d = 15.1 m → 12.70 / (15.1 × 2.2212) = 0.3787
        //   d = 250 m  → 12.70 / (250  × 2.2212) = 0.0229   (컬링)
        // ★ 거리(m)를 그대로 넣으면 안 된다 — 씬은 1 유닛 = 24 m(StsConfig.ModelScale = 1/24).
        //   상대 높이는 크기·거리가 함께 스케일되므로 단위에 무관해 이 값을 그대로 쓸 수 있다.
        const float CargoLod0Height = 0.3787f;   // 이보다 크면(≈15.1 m 이내) 원본 — 집어 든 경우
        const float CargoLod1Height = 0.0229f;   // 이보다 작으면(≈250 m 밖) 컬링

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
        // 갑판 적재 컨테이너 색 팔레트(8종 — 서브메시 0~7)
        static readonly Color[] CCargo = {
            new Color(0.55f, 0.18f, 0.15f), new Color(0.16f, 0.30f, 0.45f), new Color(0.20f, 0.42f, 0.26f),
            new Color(0.62f, 0.52f, 0.22f), new Color(0.46f, 0.47f, 0.49f), new Color(0.72f, 0.41f, 0.12f),
            new Color(0.78f, 0.78f, 0.75f), new Color(0.32f, 0.13f, 0.13f),
        };

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

            // 생성 직후 자동 접안 정렬(오너 지시 — 생성 한 번으로 안벽 접안까지). 크레인이 아직 없으면
            //   조용히 건너뛰고(배는 원점에 둠) 안내만 — 크레인 생성 후 배를 다시 생성하면 자동 접안된다.
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

            // ★ 갑판 화물 = 정밀 FBX. 오너 방침 2026-09-07 "우리는 저폴리 사용 안 할 거야",
            //   "색은 야드랑 같은 색으로" → 야드와 같은 Container_40ft.fbx 를 쓰면 색이 자동으로 같다
            //   (ContainerModelPostprocessor 가 머티리얼 이름으로 Final4/*.mat 을 붙인다).
            //   ※ 정밀본은 1개 110,134 삼각형이라 대수가 곧 예산이다. 슬롯 전체(295)를 채우면
            //     3,250만 삼각형이 되므로 DeckCargoCount 로 제한하고 갑판 전체에 고르게 분산한다.
            const string CargoFbx = "Assets/Container/Models/Container_40ft.fbx";
            ContainerFinal4Builder.EnsureMaterials();
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(CargoFbx);
            if (src == null) { Debug.LogError($"[Ship] 컨테이너 FBX 없음: {CargoFbx}"); return; }

            // 정밀 FBX 규약(실측 확인) — 스케일·회전을 건드리지 않는다.
            //   프리팹 루트가 자체 스케일을 갖고, 길이축이 이미 Z(선체 전후)이며, 피봇이 중앙 높이다.
            //   CargoSlots 의 y 도 '단 중앙' 이라 그대로 맞는다.
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(src);
            var pb = Container.Crane.Sts.EditorTools.RtgCraneFbxPlacer.CombinedBounds(probe);
            float rootScale = probe.transform.localScale.x;
            Object.DestroyImmediate(probe);

            float ms = ShipConfig.ModelScale;
            // 콜라이더는 루트 로컬 공간 — 루트 스케일이 곱해지므로 나눠준다.
            var colSize = new Vector3(ProceduralContainerMesh.StdWidth, ProceduralContainerMesh.HeightStd,
                                      ProceduralContainerMesh.Length40ft) * ms / Mathf.Max(1e-6f, rootScale);

            // 갑판/해치커버 받침 콜라이더 보장 — 동적 화물이 갑판을 뚫고 떨어지지 않게(멱등).
            EnsureRestingColliders(ship);

            var slots = ProceduralShipStructures.CargoSlots();
            // 고르게 분산 — 앞에서부터 N개를 쓰면 선미만 가득 차고 선수가 텅 빈다.
            int want = Mathf.Clamp(ShipConfig.DeckCargoCount, 0, slots.Count);
            float step = want > 0 ? (float)slots.Count / want : 1f;
            for (int k = 0; k < want; k++)
            {
                var sl = slots[Mathf.Min(slots.Count - 1, Mathf.FloorToInt(k * step))];
                // 이름에 'Container' 포함 → ContainerPhysicsStabilizer·바닥가드 대상에 포함.
                var g = (GameObject)PrefabUtility.InstantiatePrefab(src);
                g.name = "ShipContainer";
                g.transform.SetParent(parent.transform, false);
                g.transform.localPosition = new Vector3(sl.x, sl.y, sl.z) * ms;

                var box = g.AddComponent<BoxCollider>(); box.center = Vector3.zero; box.size = colSize;
                var rb = g.AddComponent<Rigidbody>(); rb.useGravity = true;
                ContainerPhysics.Apply(rb, box);
                var grab = g.AddComponent<XRGrabInteractable>(); grab.useDynamicAttach = true;
            }
            float inv = 1f / ms;
            Debug.Log($"[Ship] 갑판 컨테이너 적재 — 정밀 FBX {want}개 / 슬롯 {slots.Count} " +
                      $"(최대 {ShipConfig.DeckMaxTiers}단, 갑판 전체 분산) · 야드와 동일 머티리얼 · " +
                      $"실측 {pb.size.z * inv:F2}L × {pb.size.x * inv:F2}W × {pb.size.y * inv:F2}H m · " +
                      $"삼각형 약 {want * 110134L:N0}");
            Selection.activeGameObject = parent;
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
