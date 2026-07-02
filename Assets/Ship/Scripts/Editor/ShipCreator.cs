#if UNITY_EDITOR
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
        const string RootName = "ContainerShip";

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

        [MenuItem("Object/선박/컨테이너선 생성 (선체+상부구조)")]
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
        [MenuItem("Object/선박/컨테이너선에 컨테이너 적재 (그랩)")]
        public static void LoadShipCargo()
        {
            var ship = GameObject.Find(RootName);
            if (ship == null) { Debug.LogWarning("[Ship] ContainerShip 없음 — 먼저 컨테이너선을 생성하세요."); return; }
            var oldT = ship.transform.Find("ShipCargo");
            if (oldT != null) Undo.DestroyObjectImmediate(oldT.gameObject);

            var parent = new GameObject("ShipCargo");
            Undo.RegisterCreatedObjectUndo(parent, "Load Ship Cargo");
            parent.transform.SetParent(ship.transform, false);

            // 40ft 단일 메시 1개를 공유(인스턴스) — 우리 절차 컨테이너
            var cMesh = ProceduralContainerMesh.BuildSized(
                ProceduralContainerMesh.Length40ft, ProceduralContainerMesh.StdWidth, ProceduralContainerMesh.HeightStd,
                "ShipCargo_40ft", ProceduralContainerMesh.DefaultMiniatureScale, centerPivot: true, xIsLength: true);

            // 머티리얼 변주(0 Body·1 Door·2 Frame·3 Castings·4 Marking) — 프레임/캐스팅/마킹 공유
            var frame   = Mat(new Color(0.20f, 0.20f, 0.21f), 0.4f, 0.30f);
            var casting = Mat(new Color(0.10f, 0.10f, 0.11f), 0.4f, 0.25f);
            var marking = Mat(new Color(0.85f, 0.85f, 0.82f), 0.0f, 0.20f);
            var variants = new Material[CCargo.Length][];
            for (int i = 0; i < CCargo.Length; i++)
                variants[i] = new[] { Mat(CCargo[i], 0.1f, 0.32f), Mat(CCargo[i] * 0.85f, 0.1f, 0.32f), frame, casting, marking };

            float ms = ShipConfig.ModelScale;
            var colSize = new Vector3(ProceduralContainerMesh.Length40ft, ProceduralContainerMesh.HeightStd, ProceduralContainerMesh.StdWidth) * ms;
            var rot = Quaternion.Euler(0f, 90f, 0f);   // 컨테이너 길이축 X → 선체 전후(Z)

            // 갑판/해치커버 받침 콜라이더 보장(기존 씬에 이미 구워진 선체엔 콜라이더가 없을 수 있어 멱등 보강) —
            //   동적 화물이 갑판을 뚫고 떨어지지 않게. 새로 생성한 배는 CreateShip에서 이미 부여돼 멱등.
            EnsureRestingColliders(ship);

            var slots = ProceduralShipStructures.CargoSlots();
            foreach (var sl in slots)
            {
                // 이름에 'Container' 포함 → ContainerPhysicsStabilizer(연속충돌·접촉오프셋 튜닝)·바닥가드 대상에 포함.
                var g = new GameObject("ShipContainer");
                g.transform.SetParent(parent.transform, false);
                g.transform.localPosition = new Vector3(sl.x, sl.y, sl.z) * ms;
                g.transform.localRotation = rot;
                g.AddComponent<MeshFilter>().sharedMesh = cMesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = variants[Mathf.RoundToInt(sl.w) % CCargo.Length];
                var box = g.AddComponent<BoxCollider>(); box.center = Vector3.zero; box.size = colSize;
                // 부두 컨테이너와 동일한 '동적 강체'. kinematic으로 두면 스프레더 물리충돌(SpreaderPusher,
                //   kinematic 콜라이더)이 못 민다 — PhysX는 kinematic↔kinematic 접촉을 해소하지 않아 그냥 통과한다.
                //   동적이라야 밀림/토플(사용자가 택한 물리충돌 기능)이 배 위에서도 동일하게 작동한다.
                var rb = g.AddComponent<Rigidbody>(); rb.useGravity = true;
                ContainerPhysics.Apply(rb, box);   // 미니어처 접촉오프셋·솔버·연속충돌·마찰(부두 적층 안정화와 동일)
                var grab = g.AddComponent<XRGrabInteractable>(); grab.useDynamicAttach = true;   // 손 직접잡기(필요 기능)
            }
            Selection.activeGameObject = parent;
            Debug.Log($"[Ship] 갑판 컨테이너 적재 — {slots.Count}개(그랩 가능, 동적 강체). 스프레더 통과방지·물리충돌이 부두와 동일하게 적용.");
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
