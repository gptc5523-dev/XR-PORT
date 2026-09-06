#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// Blender 임포트 크레인(<c>Assets/Crane/Models/RTG_Crane.fbx</c>)을 씬에 생성.
    ///
    /// 메뉴: <b>Model ▸ FBX ▸ 크레인 ▸ RTG 크레인 생성</b>
    ///
    /// 절차생성(ProBuilder) 크레인과 동일하게 루트 localScale = 1/24(ModelScale)로 넣어
    /// 크기·정합을 맞춘다. 동작(무버)은 아직 배선 전 — 형상·스케일·URP 색까지.
    ///
    /// 이 한 파일이 자립적으로:
    ///   1) FBX ModelImporter를 실척 1:1·축보정·애니메이션 없음·콜라이더 없음으로 고정
    ///   2) Blender 머티리얼 12종을 동일 색 URP/Lit 에셋으로 생성·외부 리맵(있으면 재사용)
    ///   3) FBX 인스턴스를 1/24로, 기존 크레인 위치에 맞춰 배치
    /// </summary>
    public static class RtgCraneFbxPlacer
    {
        const string Fbx       = "Assets/Crane/Models/RTG_Crane.fbx";
        const string MatDir    = "Assets/Crane/Materials/RTG";
        const string CraneName = "RTG 크레인";
        const float  RealCraneHeightM = 25.042f;  // Blender 실측 RTG 총높이(m, 2026-07-15 재측정). 목표 크기 = ×ModelScale(1/24)로 절차 크레인과 동일.

        // name, r, g, b, metallic, smoothness(=1-rough), alpha(<1 → 투명), emis(발광 강도, 0=없음)  ── Blender Principled 실측값
        //   발광색 = BaseColor × emis. Blender RTG_Lens는 Emission Color가 Base Color와 동일해 이 식이 정확히 일치.
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

        [MenuItem("Model/FBX/크레인/RTG 크레인 생성", false, 1)]
        public static void Create()
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
            go.name = CraneName;
            Undo.RegisterCreatedObjectUndo(go, "Create " + CraneName);

            // 스케일: 결정적 목표 = 실척 높이 × ModelScale(1/24) → 절차생성 RTG_Crane과 항상 동일 크기.
            //   ※ 기존 '기준 크레인 매칭'은 비결정적이었음(STS 있으면 3.15/RTG 있으면 1.08/없으면 극소) → "크기 갑자기 작아짐"의 원인. 제거.
            //   FBX가 실척 25.76m를 0.26유닛으로 임포트(≈1/100, Blender FBX cm) → 목표/측정으로 보정.
            go.transform.localScale = Vector3.one;
            float fbxH = CombinedBounds(go).size.y;
            float target = RealCraneHeightM * StsConfig.ModelScale;   // 25.76 × 1/24 ≈ 1.073
            float s = fbxH > 1e-4f ? target / fbxH : 1f;
            go.transform.localScale = Vector3.one * s;
            Debug.Log($"[RTG] 스케일 결정 — 목표 {target:F3}(실척 {RealCraneHeightM}m × 1/24) / FBX측정 {fbxH:F3} → localScale {s:F4}");

            // 배치 = 야드 블록(YardBlock_Zone) 중심에 접지. 블록이 없으면 종전대로 지면 중앙.
            //   ★ FBX RTG의 주행축은 로컬 Z(= RtgBogieSteering.Mode.Travel 의 정의)이고, 야드 블록도
            //     장축이 Z(안벽 평행)라 회전 없이 그대로 정합한다. 별도 yaw를 주면 오히려 어긋난다.
            var zone = FirstYardZone();
            go.transform.position = zone != null
                ? new Vector3(zone.bounds.center.x, GroundPosition().y, zone.bounds.center.z)
                : GroundPosition();   // Quay_Ground 지면 윗면에 접지

            Selection.activeGameObject = go;   // 후속 배선 3종이 이 선택으로 대상 크레인을 찾는다.

            // ── 후속 배선 자동 실행 (수동 메뉴 없음 — 생성 한 번으로 구동 가능 상태까지) ──
            // Blender Hoist_Rope 구조(코너당 드럼출구·앵커→시브 2-fall)를 동적 재현 — 권상 시 신축.
            RtgCraneFbxRopeSetup.Setup();
            // 주행·횡행·권상 무버 + 그랩/트위스트락. 범위는 임포트 지오메트리에서 자동 산출.
            RtgCraneFbxMoverWiring.Wire();
            // 신축 드라이버. 현재 임포트 포즈를 40ft 기준자세로 캡처하므로 빔이 움직이기 전에 마지막으로.
            RtgSpreaderTelescopeSetup.Setup();

            // 주행(Z) 범위를 '야드 블록'에서 재유도 — 배선 기본값은 크레인 치수 ±2배라 야드와 무관한 임시값이다
            //   (RtgCraneFbxMoverWiring 주석: "범위는 야드 레이아웃이 정하는 몫"). 블록 밖으로 안 나가게 클램프.
            string gantryMsg = "야드 블록 없음 → 배선 기본 주행범위 유지";
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
            Debug.Log($"[RTG] '{CraneName}' 생성 완료 — 크기 결정적 정합(실척×1/24) · URP 머티리얼 {Mats.Length}종 · 로프·무버·신축 배선 자동 완료.\n" +
                      $"  배치: {(zone != null ? $"야드 블록 중심 ({zone.bounds.center.x:F3}, {zone.bounds.center.z:F3})u" : "지면 중앙(블록 없음)")} · {gantryMsg}\n" +
                      $"  ※ 신축·트위스트락·스티어링 확인은 RtgSpreaderTelescopeSetup / RtgCraneFbxMoverWiring 의 public 메서드를 직접 호출하십시오(메뉴는 생성 하나만 둔다).");
        }

        // 안벽에 가장 가까운 야드 블록 존(없으면 null) — 좌표 산식 복사 금지, 부두가 그린 실측을 쓴다.
        static Renderer FirstYardZone()
        {
            var zones = StsQuayGroundCreator.FindYardBlockZones();
            return zones.Count > 0 ? zones[0] : null;
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
            // ★ 부두가 있으면 걷는 면(Asphalt)을 공용 헬퍼로 지목한다. '평평한 것 중 면적 최대' 휴리스틱만 쓰면
            //   바다(6u×16u)가 아스팔트(3.9u×16u)보다 넓어 Sea가 뽑히고, RTG가 바다 한가운데 수면 위에 놓인다
            //   (오너 지적 2026-08-10 "RTG 크레인이 바다에 출력된다"). 수면은 데크 아래 StsConfig.SeaLevelY 라 Y까지 어긋난다.
            var best = StsQuayGroundCreator.FindQuayDeckRenderer();
            if (best == null)
            {
                float bestArea = 0f;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
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
            mi.materialLocation   = ModelImporterMaterialLocation.External;

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
