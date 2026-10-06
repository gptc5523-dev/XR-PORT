#if UNITY_EDITOR
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>Blender 임포트 크레인(RTG_Crane.fbx)을 씬에 생성(Model ▸ FBX ▸ 크레인 ▸ RTG 크레인 생성).
    /// 절차생성과 같이 루트 localScale=1/24. 임포트 설정·머티리얼 리맵·배치까지 여기서 자립 처리.</summary>
    public static class RtgCraneFbxPlacer
    {
        const string Fbx       = "Assets/Crane/Models/RTG_Crane.fbx";
        const string MatDir    = "Assets/Crane/Materials/RTG";
        const string CraneName = StsPartNames.RtgCraneRoot;
        const float  RealCraneHeightM = 25.042f;  // Blender 실측 RTG 총높이(m). 목표 크기 = ×ModelScale(1/24)로 절차 크레인과 동일.

        // name, r, g, b, metallic, smoothness(=1-rough), alpha(<1 투명), emis(0=없음) — Blender Principled 실측값
        //   발광색 = BaseColor × emis (RTG_Lens 는 Emission Color = Base Color 라 일치).
        static readonly (string n, float r, float g, float b, float metal, float smooth, float alpha, float emis)[] Mats =
        {
            ("RTG_Yellow",          0.85f, 0.72f, 0.10f,  0.02f, 0.45f, 1f,    0f),
            ("RTG_StructureYellow", 0.80f, 0.52f, 0.045f, 0.02f, 0.45f, 1f,    0f),
            ("RTG_TrolleyYellow",   0.86f, 0.60f, 0.07f,  0.02f, 0.45f, 1f,    0f),
            ("RTG_DarkMetal",       0.15f, 0.16f, 0.18f,  0.60f, 0.50f, 1f,    0f),
            ("RTG_Steel",           0.34f, 0.36f, 0.39f,  0.75f, 0.55f, 1f,    0f),
            ("RTG_Rubber",          0.055f,0.055f,0.06f,  0.00f, 0.10f, 1f,    0f),
            ("RTG_Lens",            1.00f, 0.95f, 0.72f,  0.00f, 0.40f, 1f,    2.5f),
            ("RTG_DecalBlack",      0.012f,0.012f,0.012f, 0.00f, 0.40f, 1f,    0f),
            ("RTG_Glass",           0.05f, 0.09f, 0.11f,  0.10f, 0.92f, 0.55f, 0f),
            ("MH_Roof_Grey",        0.40f, 0.42f, 0.45f,  0.85f, 0.45f, 1f,    0f),
            ("MH_Trim_Grey",        0.32f, 0.34f, 0.37f,  0.85f, 0.55f, 1f,    0f),
            ("MH_Body_Grey",        0.47f, 0.49f, 0.52f,  0.85f, 0.50f, 1f,    0f),
        };

        /// <summary>야드에 놓을 RTG 대수. 블록이 더 적으면 블록 수만큼만 놓는다.</summary>
        const int YardRtgCount = 2;

        [MenuItem("Model/FBX/크레인/RTG 야드 배치 (블록마다)", false, 2)]
        public static void PlaceInYard()
        {
            var zones = YardZones();
            if (zones.Count == 0)
            {
                Debug.LogWarning("[RTG] YardBlock_Zone 이 없습니다 — 'Model ▸ FBX ▸ 항구 ▸ 야드 배치' 를 먼저 실행하세요.");
                return;
            }
            ClearExistingRtgs();
            int n = Mathf.Min(YardRtgCount, zones.Count);
            for (int i = 0; i < n; i++) CreateOnZone(zones[i], i + 1);
            Debug.Log($"[RTG] FBX 크레인 {n}대 배치 완료(블록 {zones.Count}개 중 안벽 가까운 순).");
        }

        /// <summary>기존 RTG(FBX·절차생성)를 모두 지운다 — 안 지우면 같은 야드 블록에 겹쳐 쌓인다.</summary>
        static void ClearExistingRtgs()
        {
            int killed = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t == null || t.parent != null) continue;                 // 루트만
                string n = t.gameObject.name;
                if (!n.StartsWith(CraneName) && !n.StartsWith("RTG_Crane")) continue;
                Undo.DestroyObjectImmediate(t.gameObject); killed++;
            }
            if (killed > 0) Debug.Log($"[RTG] 기존 크레인 {killed}개 제거(FBX·절차생성 모두).");
        }

        /// <summary>야드 블록 존 — 안벽 가까운 순.</summary>
        static System.Collections.Generic.List<Renderer> YardZones()
        {
            var ground = GameObject.Find(StsPartNames.QuayGround);
            if (ground == null) return new System.Collections.Generic.List<Renderer>();
            return ground.GetComponentsInChildren<Renderer>()
                         .Where(r => r.gameObject.name.StartsWith(StsPartNames.YardBlockZone))
                         .OrderBy(r => Mathf.Abs(r.bounds.center.x))
                         .ThenBy(r => r.bounds.center.z)
                         .ToList();
        }

        [MenuItem("Model/FBX/크레인/RTG 크레인 생성", false, 1)]
        public static void Create()
        {
            ClearExistingRtgs();
            CreateOnZone(FirstYardZone(), 0);
        }

        /// <summary>RTG 1대를 지정 블록 위에 생성하고 로프·무버·신축까지 배선한다.
        /// index 0 = 단독 생성(이름 그대로), 1 이상 = 야드 배치(이름에 번호).</summary>
        static void CreateOnZone(Renderer zone, int index)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (fbx == null)
            {
                EditorUtility.DisplayDialog("RTG 크레인 생성 (FBX)",
                    $"FBX를 찾을 수 없습니다:\n{Fbx}\n\nBlender에서 먼저 익스포트하세요.", "확인");
                return;
            }

            EnsureImport();
            fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx); // 리임포트 후 재로드

            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            go.name = index > 0 ? $"{CraneName}_{index}" : CraneName;
            Undo.RegisterCreatedObjectUndo(go, "Create " + CraneName);

            // 스케일 목표 = 실척 높이 × ModelScale → 절차생성 RTG 와 같은 크기.
            //   FBX 가 cm 단위로 임포트되므로 목표/실측 비율로 보정한다.
            go.transform.localScale = Vector3.one;
            float fbxH = CombinedBounds(go).size.y;
            float target = RealCraneHeightM * StsConfig.ModelScale;   // 25.76 × 1/24 ≈ 1.073
            float s = fbxH > 1e-4f ? target / fbxH : 1f;
            go.transform.localScale = Vector3.one * s;
            Debug.Log($"[RTG] 스케일 결정 — 목표 {target:F3}(실척 {RealCraneHeightM}m × 1/24) / FBX측정 {fbxH:F3} → localScale {s:F4}");

            // 배치 = 야드 블록 존 중심에 접지(없으면 지면 중앙). RTG 주행축·블록 장축 모두 Z 라
            //   회전 없이 정합 — yaw 를 주면 어긋난다. 블록 존 중심 = RTG 스팬 중심.
            go.transform.position = zone != null
                ? new Vector3(zone.bounds.center.x, GroundPosition().y, zone.bounds.center.z)
                : GroundPosition();   // Quay_Ground 지면 윗면에 접지

            // 후속 배선 자동 실행. 배선이 선택(Selection)으로 대상을 찾고 끝에서 옮기므로 매번 다시 세운다(안 그러면 야드 배치에서 빠짐).
            // 로프: Blender Hoist_Rope 구조(코너당 2-fall)를 동적 재현 — 권상 시 신축.
            Selection.activeGameObject = go;
            RtgCraneFbxRopeSetup.Setup();
            // 주행·횡행·권상 무버 + 그랩/트위스트락. 범위는 임포트 지오메트리에서 자동 산출.
            Selection.activeGameObject = go;
            RtgCraneFbxMoverWiring.Wire();
            // 신축 드라이버. 현재 임포트 포즈를 40ft 기준자세로 캡처하므로 빔이 움직이기 전에 마지막으로.
            Selection.activeGameObject = go;
            RtgSpreaderTelescopeSetup.Setup();

            // 주행(Z) 범위를 야드 블록에서 재유도 — 배선 기본값은 임시값이라 블록 밖으로 안 나가게 클램프.
            string gantryMsg = zone == null ? "야드 블록 없음 → 배선 기본 주행범위 유지"
                                            : "GantryMover 없음(무버 배선 실패) → 주행범위 미설정";
            if (zone != null)
            {
                var gm = go.GetComponent<GantryMover>();
                if (gm != null)
                {
                    float craneZ = CombinedBounds(go).size.z;
                    float half   = Mathf.Max(0.1f, (zone.bounds.size.z - craneZ) * 0.5f);
                    float z0     = go.transform.localPosition.z;   // 루트는 무부모 → 로컬 Z = 월드 Z
                    gm.Configure(z0 - half, z0 + half);
                    gantryMsg = $"주행 ±{half * StsConfig.InvModelScale:F1}m(블록 {zone.bounds.size.z * StsConfig.InvModelScale:F1}m − 크레인 {craneZ * StsConfig.InvModelScale:F1}m)";
                }
            }

            Selection.activeGameObject = go;   // 신축 배선이 스프레더로 옮긴 선택을 크레인으로 복귀
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[RTG] '{go.name}' 생성 완료 — 크기 결정적 정합(실척×1/24) · URP 머티리얼 {Mats.Length}종 · 로프·무버·신축 배선 자동 완료.\n" +
                      $"  배치: {(zone != null ? $"블록·스팬 중심 ({go.transform.position.x:F3}, {go.transform.position.z:F3})u = 실척 ({go.transform.position.x * StsConfig.InvModelScale:F1}, {go.transform.position.z * StsConfig.InvModelScale:F1})m" : "지면 중앙(블록 없음)")} · {gantryMsg}\n" +
                      $"  ※ 신축·트위스트락·스티어링 확인은 RtgSpreaderTelescopeSetup / RtgCraneFbxMoverWiring 의 public 메서드를 직접 호출하십시오(메뉴는 생성 하나만 둔다).");
        }

        // 안벽에 가장 가까운 야드 블록 존(없으면 null) — 좌표 산식 복사 금지, 부두가 그린 실측을 쓴다.
        static Renderer FirstYardZone()
        {
            var ground = GameObject.Find(StsPartNames.QuayGround);
            if (ground == null) return null;
            return ground.GetComponentsInChildren<Renderer>()
                         .Where(r => r.gameObject.name.StartsWith(StsPartNames.YardBlockZone))
                         .OrderBy(r => Mathf.Abs(r.bounds.center.x)).FirstOrDefault();
        }

        // 하위 모든 렌더러를 감싸는 월드 바운즈(현재 스케일 반영).
        internal static Bounds CombinedBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ── 지면(Quay_Ground) 윗면 중심 위치 — 없으면 가장 넓은 평평 렌더러, 그마저 없으면 씬뷰 XZ·Y0 ──
        static Vector3 GroundPosition()
        {
            // 면적 최대 휴리스틱만 쓰면 바다가 아스팔트보다 넓어 뽑혀 RTG가 수면 위에 놓인다 — 바다는 제외.
            //   수면(StsConfig.SeaLevelY)은 데크 아래라 Y도 어긋난다.
            Renderer best = null;   // 걷는 면 헬퍼 없음 — 아래 면적 최대 휴리스틱으로 폴백
            if (best == null)
            {
                float bestArea = 0f;
                foreach (var r in Object.FindObjectsByType<Renderer>())
                {
                    if (StsPartNames.IsSeaName(r.gameObject.name)) continue;
                    var s = r.bounds.size;
                    if (s.y > Mathf.Max(s.x, s.z) * 0.25f) continue;   // 평평한 지면만
                    float area = s.x * s.z;
                    if (area > bestArea) { bestArea = area; best = r; }
                }
            }
            if (best != null)
                return new Vector3(best.bounds.center.x, best.bounds.max.y, best.bounds.center.z);

            var sv = SceneView.lastActiveSceneView;
            return sv != null ? new Vector3(sv.pivot.x, 0f, sv.pivot.z) : Vector3.zero;
        }

        // ── FBX 임포트 설정 + URP 머티리얼 생성/리맵 (idempotent) ──────────────
        static void EnsureImport()
        {
            if (AssetImporter.GetAtPath(Fbx) is not ModelImporter mi) return;

            mi.useFileScale       = true;
            mi.globalScale        = 1f;
            mi.bakeAxisConversion = true;
            mi.importAnimation    = false;
            mi.animationType      = ModelImporterAnimationType.None;
            mi.importBlendShapes  = false;
            mi.importCameras      = false;
            mi.importLights       = false;
            mi.addCollider        = false;
            mi.importNormals      = ModelImporterNormals.Import;
            mi.importTangents     = ModelImporterTangents.CalculateMikk;
            mi.weldVertices       = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

            if (!Directory.Exists(MatDir)) { Directory.CreateDirectory(MatDir); AssetDatabase.Refresh(); }
            foreach (var d in Mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), d.n), GetOrCreate(d));

            mi.SaveAndReimport();
        }

        static Material GetOrCreate((string n, float r, float g, float b, float metal, float smooth, float alpha, float emis) d)
        {
            string path = $"{MatDir}/{d.n}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = d.n };
            m.SetColor("_BaseColor", new Color(d.r, d.g, d.b, d.alpha));
            m.SetFloat("_Metallic", d.metal);
            m.SetFloat("_Smoothness", d.smooth);
            if (d.alpha < 1f)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (d.emis > 0f)
            {
                m.SetColor("_EmissionColor", new Color(d.r, d.g, d.b) * d.emis);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
#endif
