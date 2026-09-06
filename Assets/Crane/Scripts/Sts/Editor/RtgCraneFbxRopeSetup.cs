#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// FBX RTG 크레인에 **모양 유지 + 동적 신축 권상 로프**를 세팅.
    /// 「Model ▸ FBX ▸ 크레인 ▸ RTG 크레인 생성」이 자동 호출한다(수동 메뉴 없음).
    ///
    /// Blender에서 모델링한 로프(Hoist_Rope_*, Ø53mm 32각 튜브)의 실측 굵기를 그대로 재현하되, 정적 메시가 아니라
    /// 리빙 경로를 따라 매 프레임 튜브를 재생성해 권상 시 신축한다(<see cref="RtgRopeTube"/>).
    ///
    /// 코너마다 경로: 드럼출구(트롤리 고정) → [낙차] → 시브 접선점 → [시브 하부 감김 호] → 시브 접선점 → [낙차] → 앵커.
    ///
    /// 드럼출구 마커(옛 Hoist_DrumExit_*)는 현재 Blender에서 삭제됨 → 임포트된 모델 로프의 **드럼측 끝 캡
    /// 링 중심**(= 로프 양 끝 캡 2개 중 **앵커에서 먼** 쪽)에서 자동 추출해 트롤리 자식 고정점으로 재생성한다.
    /// 원본 정적 로프 메시는 숨긴다(동적 튜브가 대체).
    ///
    /// 리빙 입력 2개는 **전부 모델 로프 실측에서 유도**한다(상수 박지 않음 → 모델이 바뀌면 따라간다):
    ///   ① 드럼출구 = 로프 드럼측 캡 링의 중심   ② 감김 반경 = min|로프정점−시브중심| + 로프반경
    /// 둘 다 '중심선' 값이라야 한다. 표면 정점이나 시브 외곽 바운즈를 쓰면 로프 반경만큼 어긋난다(아래 각주).
    ///
    /// **형상은 모델을 따르되 색은 따르지 않는다** — 동적 로프는 로프 전용 검정 재질(`RTG_RopeBlack.mat`)을 쓴다.
    /// 드럼 코일은 손대지 않으므로(오너 결정 2026-07-16) 드럼출구에서 코일과 색이 갈린다. 결함 아님.
    /// </summary>
    public static class RtgCraneFbxRopeSetup
    {
        const string CraneName = "RTG 크레인";
        const string GroupName = "Reeving";
        static readonly string[] Corners = { "FL", "FR", "BL", "BR" };

        public static void Setup()
        {
            var crane = Selection.activeGameObject;
            if (crane == null || FindDeep(crane.transform, "Trolley") == null)
                crane = GameObject.Find(CraneName);
            if (crane == null) { Dialog($"대상 크레인을 못 찾음. '{CraneName}' 선택 후 다시."); return; }
            var root = crane.transform;
            var trolley = FindDeep(root, "Trolley");

            // 옛 리빙 그룹 제거
            foreach (var gname in new[] { GroupName, "HoistRopes", "HoistRopes_Auto" })
            {
                Transform g; int guard = 0;
                while ((g = FindDeep(root, gname)) != null && guard++ < 30)
                    Undo.DestroyObjectImmediate(g.gameObject);
            }
            // 옛 드럼출구 고정점 제거(재셋업 중복 방지)
            foreach (var c in Corners)
            {
                Transform de; int guard = 0;
                while ((de = FindDeep(root, "Hoist_DrumExit_" + c)) != null && guard++ < 8)
                    Undo.DestroyObjectImmediate(de.gameObject);
            }

            var diag = new StringBuilder("[RTG] 동적 튜브 리빙 진단:\n");
            var labels = new List<string>();
            var drumExits = new List<Transform>(); var anchors = new List<Transform>();
            var sheaves = new List<Transform>();   var sheaveRadii = new List<float>();
            var modeledRopes = new List<Transform>();
            Material ropeMat = null;
            float ropeWorldRadius = 0f;

            foreach (var c in Corners)
            {
                var sheave = FindDeep(root, "Spreader_HB_Sheave_" + c);
                var anchor = FindDeep(root, "Hoist_RopeAnchor_" + c);
                var rope   = FindDeep(root, "Hoist_Rope_" + c);
                if (sheave == null) { diag.AppendLine($"  {c}: 시브 없음 → 건너뜀"); continue; }

                // 드럼출구 = 모델 로프 드럼측 캡의 중심선 점 → 트롤리 자식 고정점으로 재생성
                Transform drumExit = null;
                float wrapR = 0f;
                if (rope != null)
                {
                    Vector3 exitW = DrumExitWorld(rope, anchor, out float rr, out Material m);
                    var de = new GameObject("Hoist_DrumExit_" + c);
                    Undo.RegisterCreatedObjectUndo(de, "RTG DrumExit");
                    de.transform.SetParent(trolley != null ? trolley : root, false);
                    de.transform.position = exitW;
                    drumExit = de.transform;
                    if (rr > ropeWorldRadius) ropeWorldRadius = rr;    // 코너간 동일하지만 방어적으로 max
                    if (ropeMat == null && m != null) ropeMat = m;
                    modeledRopes.Add(rope);
                    wrapR = WrapRadiusWorld(rope, sheave.position, rr);   // 감김 반경도 같은 로프 실측에서
                }
                if (wrapR <= 0f)
                {
                    wrapR = SheaveRimRadiusWorld(sheave);   // 폴백(로프 메시 없음) — 림 반경이라 실제 감김보다 크다
                    Debug.LogWarning($"[RTG] {c}: 로프 메시가 없어 감김 반경을 시브 림({wrapR:F4})으로 폴백 — " +
                                     "로프가 시브 홈이 아니라 림 위를 도는 것처럼 뜬다.");
                }

                labels.Add(c);
                sheaves.Add(sheave); anchors.Add(anchor); drumExits.Add(drumExit);
                sheaveRadii.Add(wrapR);
                diag.AppendLine($"  {c}: 시브O r={sheaveRadii[sheaveRadii.Count-1]:F3} 앵커={(anchor?"O":"X")} 드럼출구={(drumExit?"O":"X")} 로프={(rope?"O":"X")}");
            }
            if (sheaves.Count == 0) { Dialog("시브(Spreader_HB_Sheave_*)를 못 찾음."); return; }

            // 로프 메시(Hoist_Rope_*)가 FBX에 없으면 드럼출구·굵기 원천이 없어 정상 재현 불가 → 재익스포트 안내.
            if (modeledRopes.Count == 0)
            {
                Debug.LogWarning("[RTG] 로프 메시(Hoist_Rope_FL/FR/BL/BR)가 임포트된 크레인에 없습니다. " +
                    "Blender에서 로프를 추가한 뒤 FBX를 '재익스포트'하지 않으면 로프가 안 생깁니다. " +
                    "(앵커·시브는 있으나 로프 본체가 익스포트에서 누락됨)");
                Dialog("로프 메시(Hoist_Rope_*)가 FBX에 없습니다.\n\n" +
                    "Blender에서 로프를 추가한 뒤 FBX를 다시 익스포트하세요.\n" +
                    "(현재 FBX엔 앵커·시브만 있고 로프 본체가 빠졌습니다.)");
                return;
            }
            if (ropeWorldRadius <= 0f)
            {
                // 폴백: 시브 비율로 월드 반경 환산. 실측 로프 0.0265m ↔ 실측 **감김 반경** 0.4439m.
                //   ropeWorld = 0.0265 × (감김반경 / 0.4439) — 임포트 스케일 무관하게 정확.
                //   (sheaveRadii가 림 0.46이 아니라 감김 0.4439를 담게 바뀌었으므로 분모도 0.4439다.)
                float avgSheave = 0f; foreach (var s in sheaveRadii) avgSheave += s;
                avgSheave = sheaveRadii.Count > 0 ? avgSheave / sheaveRadii.Count : 0.019f;
                ropeWorldRadius = 0.0265f * (avgSheave / 0.4439f);
            }

            // Reeving 그룹 — 월드 스케일 1로(크레인 스케일 상쇄) → 튜브 월드 반경 정확 보존
            var group = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(group, "RTG Reeving");
            group.transform.SetParent(root, false);
            group.transform.localScale = Vector3.one / Mathf.Max(1e-4f, root.lossyScale.x);

            // 동적 로프 재질 — 모델 상속(RTG_DarkMetal)이 아니라 **로프 전용 검정 재질**을 쓴다.
            //   [오너 결정 2026-07-16] 자유 로프만 검정. **드럼 코일은 건드리지 않는다** —
            //   코일은 Hoist_Drum_F/B 메시의 RTG_DarkMetal 슬롯이라 그대로 (0.15,0.16,0.18)로 남고,
            //   드럼출구에서 코일과 자유 로프의 색이 갈리는 건 **의도된 것**이다(형상은 0.03mm로 이어져 있다).
            //   전용 에셋이라 RTG_DarkMetal을 공유하는 나머지 191개 오브젝트는 영향 없다.
            ropeMat = GetRopeMaterial() ?? ropeMat ?? GetFallbackRopeMaterial();

            var filters = new MeshFilter[sheaves.Count];
            for (int i = 0; i < sheaves.Count; i++)
            {
                var go = new GameObject("Hoist_Rope_" + labels[i] + "_Dyn");
                go.transform.SetParent(group.transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ropeMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // VR 모바일 절약
                mr.receiveShadows = false;
                filters[i] = mf;
            }

            var tube = group.AddComponent<RtgRopeTube>();
            tube.Configure(drumExits.ToArray(), anchors.ToArray(), sheaves.ToArray(),
                           sheaveRadii.ToArray(), filters, ropeWorldRadius, 32, 14);

            // 원본 정적 로프 메시 숨김(동적 튜브가 대체) — 재셋업 시 재추출 위해 삭제 대신 비활성
            foreach (var r in modeledRopes) if (r != null) Undo.RecordObject(r.gameObject, "hide rope");
            foreach (var r in modeledRopes) if (r != null) r.gameObject.SetActive(false);

            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(group);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(crane.scene);
            }
            Selection.activeGameObject = group;
            diag.Append($"[RTG] 동적 튜브 로프 {filters.Length}가닥 · 반경 {ropeWorldRadius*1000f:F1}mm(월드) · 32각 · 원본 정적로프 {modeledRopes.Count}개 숨김. 권상 시 신축.");
            Debug.Log(diag.ToString());
        }

        // 드럼출구 = 로프 **드럼측** 평면컷 캡 링의 **중심**(= 로프 중심선 위의 점).
        //
        // Blender 실측(2026-07-16, RTG_Crane_Scene): 드럼 코일 꼬리 캡 중심과 로프 드럼측 캡 중심이
        //   (1.2, 0.8895, 23.74)로 **0.03mm 일치** → 캡 중심이 곧 로프가 드럼을 떠나는 접선 이탈점이다.
        //   (권상 로프의 드럼 감김 코일은 Hoist_Rope_*가 아니라 Hoist_Drum_F/B 메시에 들어 있다.)
        //
        // ※ 드럼측을 **'최상단 캡'으로 고르면 안 된다** — 그건 형상에 딸린 우연이지 의미가 아니다.
        //   2026-07-16 이전엔 로프 앵커측 끝이 z=23.600에서 잘려 앵커에 안 닿아 있었고(공중에 뜬 결함),
        //   그 덕에 드럼측(23.740)이 우연히 최상단이었다. 로프를 앵커에 접합(23.600→24.000)하는 순간
        //   앵커측이 최상단이 되어 드럼출구가 앵커 자리로 잡힌다 = 동적 로프 전체가 틀어진다.
        //   → **앵커에서 먼 쪽 캡**을 택한다. 임계값이 필요 없고 접합 전/후 형상 모두에서 같은 답이다:
        //     접합 후 드럼측 1.728m vs 앵커측 0.015m / 접합 전 드럼측 1.728m vs 앵커측 0.385m.
        //
        // ※ 반드시 '중심'이어야 한다 — 옛 구현은 최장 엣지 끝점, 즉 튜브 **표면 정점 1개**를 집었다.
        //   캡은 32각 평면컷이라 32개 정점이 전부 같은 Y에 있어 그중 아무거나 뽑혔고, 결과가
        //   중심선에서 **로프 반경(26.5mm)만큼** 빗나가 동적 로프가 드럼 코일에서 반경 하나만큼
        //   어긋나 보였다(Unity 실측 (1.1948,23.74,0.8635) vs 참값 (1.2,23.74,0.8895)).
        //   최장 엣지 필터는 '드럼 감김부 배제'가 목적이었으나 로프 메시엔 감김부가 아예 없어 무의미했다.
        //
        // 아울러 월드 반경(최소 bbox 반폭)·머티리얼 반환.
        static Vector3 DrumExitWorld(Transform rope, Transform anchor, out float worldRadius, out Material mat)
        {
            worldRadius = 0f; mat = null;
            var mf = rope.GetComponentInChildren<MeshFilter>();
            var mr = rope.GetComponentInChildren<MeshRenderer>();
            if (mr != null) mat = mr.sharedMaterial;
            if (mf == null || mf.sharedMesh == null) return rope.position;

            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            var lm = mf.transform.localToWorldMatrix;
            if (verts.Length == 0) return rope.position;

            // 월드 정점 + bbox
            var w = new Vector3[verts.Length];
            Vector3 mn = lm.MultiplyPoint3x4(verts[0]), mx = mn;
            for (int i = 0; i < verts.Length; i++)
            {
                w[i] = lm.MultiplyPoint3x4(verts[i]);
                mn = Vector3.Min(mn, w[i]); mx = Vector3.Max(mx, w[i]);
            }
            Vector3 ext = mx - mn;
            worldRadius = Mathf.Max(1e-5f, Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z)) * 0.5f);

            float eps = worldRadius * 0.25f;
            float q   = Mathf.Max(1e-6f, worldRadius * 0.01f);   // 중복 판정용 위치 양자화

            // 캡A = 월드 Y 최대면. 캡B = 캡A 평면을 뺀 나머지의 Y 최대면 = 반대쪽 캡.
            //   로프 양 다리는 통짜 스팬(중간 링 없음: 실측 5.7m)이라 '캡A 다음으로 높은 면'이 곧 반대쪽 캡이다.
            //   Blender 실측 뒷받침: 평면컷(수평) 링은 29개 중 **캡 2개뿐** — 나머지 링은 축에 수직이라 Y가 퍼진다.
            Vector3 capA = PlanarCapCenter(w, mx.y, eps, q);
            float yB = float.NegativeInfinity;
            for (int i = 0; i < w.Length; i++)
                if (w[i].y < mx.y - eps && w[i].y > yB) yB = w[i].y;

            // 앵커가 없거나 캡이 하나뿐이면 판별 불가 → 종전 동작(최상단 캡)
            if (anchor == null || float.IsNegativeInfinity(yB)) return capA;

            Vector3 capB = PlanarCapCenter(w, yB, eps, q);
            return (capA - anchor.position).sqrMagnitude >= (capB - anchor.position).sqrMagnitude ? capA : capB;
        }

        // 평면컷 캡 링의 중심 = planeY 평면(±eps)에 놓인 정점들의 중심.
        //   임포터가 노멀/UV 이음매에서 정점을 쪼개 같은 자리에 중복 정점을 만들므로, 위치 중복을 제거해야
        //   중심이 이음매 쪽으로 쏠리지 않는다.
        static Vector3 PlanarCapCenter(Vector3[] w, float planeY, float eps, float q)
        {
            var seen = new HashSet<Vector3Int>();
            Vector3 sum = Vector3.zero; int n = 0;
            for (int i = 0; i < w.Length; i++)
            {
                if (Mathf.Abs(w[i].y - planeY) > eps) continue;
                var key = new Vector3Int(Mathf.RoundToInt(w[i].x / q),
                                         Mathf.RoundToInt(w[i].y / q),
                                         Mathf.RoundToInt(w[i].z / q));
                if (!seen.Add(key)) continue;
                sum += w[i]; n++;
            }
            return n > 0 ? sum / n : w[TopIndex(w)];
        }

        // 시브 감김 반경 = **로프 중심선이 도는 반경**(시브 바깥 치수가 아니다).
        //   R_w = min|로프 정점 − 시브 중심| + 로프 반경
        //   로프 중심선과 시브 중심은 같은 평면에 있고, 감김의 최내점이 시브 중심에 가장 가까운 로프 점이라
        //   축을 몰라도 3D 거리만으로 정확하다(전부 월드 계산 → 크레인 임포트 스케일 무관).
        //
        // Blender 실측(2026-07-16): R_w = **0.4439** — 감김 구간 25개 링 전부 편차 0.00000.
        //   시브 홈 바닥은 0.4100이고 로프는 그 위 7.4mm에 떠서 앉는다(홈 옆면에 물림) → 홈바닥+로프반경으로
        //   유도하면 안 되고, 로프 실측이 유일한 정답이다.
        // ※ 옛 SheaveWorldRadius(렌더러 bounds 최대 반폭)는 시브 **림** 0.4600을 줘서 16.1mm 컸고,
        //   감김 호와 양쪽 접선점이 통째로 밀렸다.
        static float WrapRadiusWorld(Transform rope, Vector3 sheaveCenter, float ropeWorldRadius)
        {
            var mf = rope.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return 0f;
            var verts = mf.sharedMesh.vertices;
            if (verts.Length == 0) return 0f;
            var lm = mf.transform.localToWorldMatrix;

            float min = float.PositiveInfinity;
            for (int i = 0; i < verts.Length; i++)
                min = Mathf.Min(min, Vector3.Distance(lm.MultiplyPoint3x4(verts[i]), sheaveCenter));
            return float.IsPositiveInfinity(min) ? 0f : min + ropeWorldRadius;
        }

        static int TopIndex(Vector3[] w)
        {
            int t = 0; for (int i = 1; i < w.Length; i++) if (w[i].y > w[t].y) t = i; return t;
        }

        const string RopeMatPath = "Assets/Crane/Materials/RTG/RTG_RopeBlack.mat";

        // 로프 전용 검정 재질. 없으면 만들고, **있으면 그대로 존중**한다(인스펙터로 조정한 값을 재생성이 덮지 않게)
        //   — `RtgCraneFbxPlacer.GetOrCreate`와 같은 규약: **.mat이 정본이고 이 코드의 숫자는 초기값일 뿐**이다.
        //   색만 검정이고 금속/광택은 RTG_DarkMetal과 동일(0.60/0.50) — '색만 바꾼다'는 지시 그대로.
        //   BaseColor가 0이 아니라 0.02인 건 PBR에서 완전 검정인 물질이 없어 0으로 두면 평평하게 뭉개지기 때문.
        static Material GetRopeMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(RopeMatPath);
            if (existing != null) return existing;

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh == null) return null;
            var m = new Material(sh) { name = "RTG_RopeBlack" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 1f));
            if (m.HasProperty("_Color"))     m.SetColor("_Color",     new Color(0.02f, 0.02f, 0.02f, 1f));
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic",   0.60f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.50f);

            var dir = System.IO.Path.GetDirectoryName(RopeMatPath);
            if (!System.IO.Directory.Exists(dir)) { System.IO.Directory.CreateDirectory(dir); AssetDatabase.Refresh(); }
            AssetDatabase.CreateAsset(m, RopeMatPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RTG] 로프 전용 검정 재질 생성: {RopeMatPath} (색만 검정, 금속/광택은 RTG_DarkMetal과 동일)");
            return m;
        }

        static Material GetFallbackRopeMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "RopeTube_Fallback" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.09f, 0.09f, 0.10f));
            if (m.HasProperty("_Metallic"))  m.SetFloat("_Metallic", 0.6f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            return m;
        }

        // 시브 **림** 반지름(월드) = 렌더러 바운즈 최대 반폭(디스크 외경).
        //   로프 메시가 없을 때만 쓰는 폴백 — 실제 감김 반경(WrapRadiusWorld)보다 크다(실측 0.4600 vs 0.4439).
        static float SheaveRimRadiusWorld(Transform sheave)
        {
            var rend = sheave.GetComponentInChildren<Renderer>();
            if (rend == null) return 0.02f;
            Vector3 e = rend.bounds.extents;
            return Mathf.Max(e.x, Mathf.Max(e.y, e.z));
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        static void Dialog(string msg) => EditorUtility.DisplayDialog("RTG 로프 셋업", msg, "확인");
    }
}
#endif
