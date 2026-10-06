#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>FBX RTG 에 동적 신축 권상 로프 세팅(RTG 생성이 자동 호출). 드럼출구·감김반경은 모델 로프 실측에서 유도.
    /// 로프 전용 검정 재질이라 드럼 코일과 색이 다르다(의도).</summary>
    public static class RtgCraneFbxRopeSetup
    {
        const string CraneName = StsPartNames.RtgCraneRoot;
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
            // 옛 드럼출구 고정점 제거
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

                // 드럼출구 = 로프 드럼측 캡 중심 → 트롤리 자식 고정점
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

            // 로프 메시(Hoist_Rope_*)가 없으면 정상 재현 불가 → 재익스포트 안내.
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
                // 폴백: 시브 비율로 월드 반경 환산(임포트 스케일 무관).
                float avgSheave = 0f; foreach (var s in sheaveRadii) avgSheave += s;
                avgSheave = sheaveRadii.Count > 0 ? avgSheave / sheaveRadii.Count : 0.019f;
                ropeWorldRadius = 0.0265f * (avgSheave / 0.4439f);
            }

            // Reeving 그룹 — 월드 스케일 1(튜브 월드 반경 보존)
            var group = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(group, "RTG Reeving");
            group.transform.SetParent(root, false);
            group.transform.localScale = Vector3.one / Mathf.Max(1e-4f, root.lossyScale.x);

            // 동적 로프는 전용 검정 재질 — 드럼 코일(RTG_DarkMetal)과 출구에서 색이 갈린다(의도).
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

            // 원본 정적 로프는 재셋업 재추출용으로 삭제 대신 비활성
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

        // 드럼출구 = 드럼측 캡 링 중심(표면 정점이면 로프반경만큼 빗나감). 월드 반경·머티리얼도 반환.
        //   ★ 앵커에서 먼 쪽 캡 — 가까운 쪽/최상단을 고르면 로프 전체가 틀어진다.
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

            // 캡A = Y 최대면, 캡B = 그다음 높은 면(중간 링 없는 통짜 스팬이라 반대쪽 캡).
            Vector3 capA = PlanarCapCenter(w, mx.y, eps, q);
            float yB = float.NegativeInfinity;
            for (int i = 0; i < w.Length; i++)
                if (w[i].y < mx.y - eps && w[i].y > yB) yB = w[i].y;

            // 판별 불가 → 최상단 캡
            if (anchor == null || float.IsNegativeInfinity(yB)) return capA;

            Vector3 capB = PlanarCapCenter(w, yB, eps, q);
            return (capA - anchor.position).sqrMagnitude >= (capB - anchor.position).sqrMagnitude ? capA : capB;
        }

        // 캡 링 중심 = planeY(±eps) 정점 중심. 이음매 중복 정점을 제거해야 중심이 쏠리지 않는다.
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

        // 감김 반경 = min|로프 정점 − 시브 중심| + 로프 반경. 로프가 홈 옆면에 물려 떠 있어 실측만 정답.
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

        // 로프 전용 검정 재질 — 있으면 그대로(.mat이 정본). BaseColor 0.02는 순수 검정이 평평해 보이는 것 회피.
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

        // 시브 림 반지름(월드) = 바운즈 최대 반폭. 로프 메시 없을 때만 쓰는 폴백 — 실제 감김보다 크다.
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
