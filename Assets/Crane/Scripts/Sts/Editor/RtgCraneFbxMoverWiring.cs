#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>RTG FBX 크레인에 Gantry/Trolley/Hoist 무버+StsCrane 배선. 범위는 임포트 지오메트리에서 산출(하드코딩 금지).
    /// 「RTG 크레인 생성」이 자동 호출. 신축은 <see cref="RtgSpreaderTelescopeSetup"/> 단독 소유.</summary>
    public static class RtgCraneFbxMoverWiring
    {
        const string CraneName = StsPartNames.RtgCraneRoot;

        public static void Wire()
        {
            var crane = Selection.activeGameObject;
            if (crane == null || FindDeep(crane.transform, "Trolley") == null)
                crane = GameObject.Find(CraneName);
            if (crane == null)
            {
                EditorUtility.DisplayDialog("무버 배선",
                    $"대상 크레인을 찾지 못했습니다.\n'{CraneName}'를 선택하거나 씬에 두고 다시 실행하세요.", "확인");
                return;
            }

            var root     = crane.transform;
            var trolleyT = FindDeep(root, "Trolley");
            var spreadT  = FindDeep(root, "Spreader");
            if (trolleyT == null || spreadT == null)
            {
                EditorUtility.DisplayDialog("무버 배선",
                    $"계층에서 Trolley/Spreader 트랜스폼을 못 찾았습니다.\nTrolley={trolleyT}, Spreader={spreadT}", "확인");
                return;
            }

            // ── Rigidbody (스크립트 구동 → kinematic) ──
            var rb = GetOrAdd<Rigidbody>(crane);
            rb.isKinematic = true; rb.useGravity = false;

            // ── 범위 자동 산출 ──
            // Trolley X: 휠 외측면이 레일 끝에 닿는 지점(실측 ±10.095). 문서/크레인_동적데이터/RTG_크레인_동적데이터.md §4.
            if (!TrolleyRange(root, trolleyT, out float txMin, out float txMax))
            {
                float halfX = LocalHalfExtent(crane, root, 0);
                txMin = -0.72f * halfX; txMax = 0.72f * halfX;
                Debug.LogWarning("[RTG] TrolleyRail_*/Trolley_Wheel_* 를 못 찾아 트롤리 범위를 스팬 비율(±72%)로 폴백했습니다. " +
                                 "FBX 노드명이 바뀌었는지 확인하세요.");
            }

            // Hoist Y(월드 절대 — FBX 로컬Y≠월드). 상한 = 스프레더가 머리 위 구조물에 닿기 직전(Headroom()).
            //   실측(같은 문서 §5): 헤드룸 9.624m → 상한 19.893.
            float headroom = Headroom(root, spreadT, out string limitPair);
            float hyMax = spreadT.position.y + headroom;
            // 하한 = 그랩 평면(트위스트락 콘 바닥 = 스프레더 최저점)이 지면에 닿는 높이. 상한과 같은 방식으로 실지오메트리에서 산출.
            //   실측(§5): 그랩 평면은 스프레더 원점보다 0.3275 아래, 하한 월드 z 0.3275(행정 19.566).
            float grabDrop = spreadT.position.y - CombinedBounds(spreadT.gameObject).min.y;
            float hyMin = root.position.y + grabDrop;
            if (hyMin > hyMax - 0.1f) hyMin = hyMax - 0.1f;         // 최소 여유(퇴화 방지)

            // Gantry Z: 크레인 Z 길이의 ±2배 주행(월드 단위 — 루트는 무부모라 로컬Z=월드Z).
            float craneZ = CombinedBounds(crane).size.z;
            float gz = root.localPosition.z, gRange = Mathf.Max(0.5f, craneZ * 2f);
            float gzMin = gz - gRange, gzMax = gz + gRange;

            // Gantry X(레인 이동, 스티어링 90°): RTG는 타이어 주행이라 물리 한계가 없다 —
            //   범위는 야드 레이아웃이 정하는 몫이라 주행(Z)과 같은 관례(크레인 치수 ±2배)로 잡는다.
            float craneX = CombinedBounds(crane).size.x;
            float gx = root.localPosition.x, gxRange = Mathf.Max(0.5f, craneX * 2f);
            float gxMin = gx - gxRange, gxMax = gx + gxRange;

            // ── 컴포넌트 부착 + 배선 ──
            var gantry = GetOrAdd<GantryMover>(crane);
            gantry.Configure(gzMin, gzMax);
            gantry.ConfigureLane(gxMin, gxMax);

            var trolley = GetOrAdd<TrolleyMover>(trolleyT.gameObject);
            trolley.Configure(txMin, txMax, spreadT);

            var hoist = GetOrAdd<SpreaderHoist>(spreadT.gameObject);
            hoist.SetWorldVertical(true);     // FBX: 월드 Y 직접 구동(로컬Y 어긋남 회피)
            hoist.Configure(hyMin, hyMax);

            // ── 컨테이너 결합점(SpreaderAttach: 부모변경+kinematic) + 그랩버(SpreaderGrabber) ──
            var attach = GetOrAdd<SpreaderAttach>(spreadT.gameObject);
            attach.Configure(spreadT);

            var sts = GetOrAdd<StsCrane>(crane);
            sts.Configure(null, trolley, hoist, attach, gantry);

            // Grabber 는 [RequireComponent(StsCrane)] — sts 먼저 부착. 트위스트락 자동 탐색.
            var grabber = GetOrAdd<SpreaderGrabber>(crane);

            // ── 트위스트락 잠금 애니 ── (신축은 RtgSpreaderTelescopeSetup 단독 소유, 이 직후 호출)
            var lockAnim = GetOrAdd<SpreaderLockAnimator>(spreadT.gameObject);
            lockAnim.SetWorldVertical(true);    // FBX 축 우회(월드 수직 기준 회전·딥)

            // ── 보기 스티어링(0°/90°) ── Bogie_* 4개를 킹핀 축으로 꺾고, 90° 완료 시 주행축 Z→X 인계.
            var steer = GetOrAdd<RtgBogieSteering>(crane);
            steer.Configure(root, gantry);
            if (steer.BogieCount != 4)
                Debug.LogWarning($"[RTG] 보기를 {steer.BogieCount}/4개만 찾았습니다 — 스티어링이 일부만 돕니다. " +
                                 "FBX에 Bogie_LF/LB/RF/RB 가 있는지 확인하세요.");

            // Configure 값이 Play/도메인리로드에도 남도록 오버라이드·씬 기록(에디트 모드에서만).
            if (!Application.isPlaying)
            {
                foreach (Component comp in new Component[] { rb, gantry, trolley, hoist, attach, sts, grabber, lockAnim, steer })
                {
                    if (comp == null) continue;
                    EditorUtility.SetDirty(comp);
                    if (PrefabUtility.IsPartOfPrefabInstance(comp))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                }
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(crane.scene);
            }
            Debug.Log($"[RTG] 무버 배선 완료 (자동 산출 범위):\n" +
                      $"  Gantry(Z)  min {gzMin:F3} ~ max {gzMax:F3}\n" +
                      $"  Trolley(X) min {txMin:F3} ~ max {txMax:F3} — 레일 끝 − 휠 외측면에서 산출(실측 ±10.095)\n" +
                      $"  Hoist(Y)   min {hyMin:F4} ~ max {hyMax:F4} (월드Y 절대) — 행정 {hyMax - hyMin:F4}\n" +
                      $"             상한 = 임포트 자세 {spreadT.position.y:F4} + 헤드룸 {headroom:F4} — 한계쌍 {limitPair}\n" +
                      $"             하한 = 지면 {root.position.y:F4} + 그랩드롭 {grabDrop:F4}(원점 → 트위스트락 콘 바닥) — 그랩 평면이 지면 착지\n" +
                      $"  Lane(X)    min {gxMin:F3} ~ max {gxMax:F3} (스티어링 90° 시 주행축)\n" +
                      $"  Hoist 월드수직 · Attach+Grabber(잡기/놓기) · Twistlock 잠금(월드축) 배선 · kinematic.\n" +
                      $"  스티어링: 보기 {steer.BogieCount}개 배선(0°=Z 주행 / 90°=X 레인 이동, 꺾는 중 주행 잠금).\n" +
                      $"  잡을 때 컨테이너 크기로 20/40ft 신축 + 트위스트락 잠금, 놓을 때 40ft 복원+해제.");
            Selection.activeGameObject = crane;
        }

        // ── 권상 상·하한 눈으로 확인 (Play 불필요 — 로프 튜브는 [ExecuteAlways]라 따라 신축) ──
        public static void HoistUp() => HoistTo(1f, "최상단");
        public static void HoistDown() => HoistTo(0f, "지면");

        static void HoistTo(float t01, string label)
        {
            var crane = Selection.activeGameObject;
            if (crane == null || FindDeep(crane.transform, "Spreader") == null)
                crane = GameObject.Find(CraneName);
            var spreader = crane != null ? FindDeep(crane.transform, "Spreader") : null;
            var hoist = spreader != null ? spreader.GetComponent<SpreaderHoist>() : null;
            if (hoist == null)
            {
                EditorUtility.DisplayDialog("스프레더 권상",
                    "먼저 'RTG 크레인 생성'을 실행하세요. (SpreaderHoist 미배선)", "확인");
                return;
            }
            Undo.RecordObject(spreader, "Spreader Hoist");
            hoist.MoveToNormalized(t01);
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(spreader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(spreader.gameObject.scene);
            }
            SceneView.RepaintAll();
            Debug.Log($"[RTG] 스프레더 {label} 이동 완료 — 월드 Y {hoist.Current:F3} (범위 {hoist.Min:F3} ~ {hoist.Max:F3}). 로프가 따라 신축합니다.");
        }

        // ── 스티어링 0°/90° 눈으로 확인 (Play 불필요 — RtgBogieSteering은 [ExecuteAlways]) ──
        public static void SteerLane() => SteerTo(RtgBogieSteering.Mode.Lane, "90° 레인 이동(로컬 X)");
        public static void SteerTravel() => SteerTo(RtgBogieSteering.Mode.Travel, "0° 주행(로컬 Z)");

        static void SteerTo(RtgBogieSteering.Mode m, string label)
        {
            var crane = Selection.activeGameObject;
            if (crane == null || crane.GetComponent<RtgBogieSteering>() == null)
                crane = GameObject.Find(CraneName);
            var steer = crane != null ? crane.GetComponent<RtgBogieSteering>() : null;
            if (steer == null)
            {
                EditorUtility.DisplayDialog("스티어링",
                    "먼저 'RTG 크레인 생성'을 실행하세요. (RtgBogieSteering 미배선)", "확인");
                return;
            }
            Undo.RecordObject(steer, "Bogie Steering");
            foreach (var t in steer.GetBogies()) if (t != null) Undo.RecordObject(t, "Bogie Steering");
            steer.SetModeNow(m);
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(steer);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(crane.scene);
            }
            SceneView.RepaintAll();
            var g = crane.GetComponent<GantryMover>();
            Debug.Log($"[RTG] 스티어링 {label} — 보기 {steer.BogieCount}개 회전 완료. " +
                      (g != null ? $"주행축 = 로컬 {g.Axis} (범위 {g.Min:F3} ~ {g.Max:F3})" : "GantryMover 없음"));
        }

        public static void AttachContainerTest()
        {
            var crane = Selection.activeGameObject;
            if (crane == null || FindDeep(crane.transform, "Trolley") == null)
                crane = GameObject.Find(CraneName);
            if (crane == null || FindDeep(crane.transform, "Spreader") == null)
            {
                EditorUtility.DisplayDialog("컨테이너 잡기 테스트 부착",
                    $"'{CraneName}'(Spreader 포함)을 찾지 못했습니다.\n크레인을 선택하거나 씬에 두고 다시 실행하세요.", "확인");
                return;
            }
            if (crane.GetComponent<SpreaderGrabber>() == null)
                EditorUtility.DisplayDialog("컨테이너 잡기 테스트",
                    "먼저 'RTG 크레인 생성'을 실행해 권상·그랩을 배선하세요. (Grabber 미배선)", "확인");
            var scn = crane.GetComponent<ContainerLiftScenario>();
            if (scn == null) scn = Undo.AddComponent<ContainerLiftScenario>(crane);
            // 실제 컨테이너 프리팹 자동 지정(없으면 큐브 폴백)
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Container/Prefabs/Container_20ft.prefab");
            if (prefab != null) { scn.SetContainerPrefab(prefab); EditorUtility.SetDirty(scn); }
            Selection.activeGameObject = crane;
            Debug.Log($"[RTG] 컨테이너 잡기 테스트 부착 완료 — Play 시 스프레더 아래에 {(prefab ? "Container_20ft 프리팹" : "큐브")}을 놓고 하강→잡기→들어올림→내림→놓기 반복. 콘솔 [컨테이너테스트] 로그로 확인.");
        }

        // 있으면 반환, 없으면 부착. ★ ??/?. 는 Unity 가짜 null 을 우회하므로 금지.
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }

        // 하위에서 이름으로 트랜스폼 탐색(BFS).
        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        // 호이스트 상한 헤드룸(월드 단위) = min over (장애물 삼각형 T, 스프레더 부품 S | XZ 겹치고 T가 S 위) (T밑면 − S상단).
        //   ① 트롤리가 아니라 크레인 전체를 훑는다(주거더는 트롤리 자식이 아님). ② 장애물은 렌더러 AABB가 아니라
        //   삼각형 단위로 본다. ③ 스프레더도 부품 단위로 본다(통짜 bbox면 슬롯을 막힌 걸로 오판).
        //   실측(문서/크레인_동적데이터/RTG_크레인_동적데이터.md §5): 한계쌍 GantryFrame_Body(20.500)↔TeleBeam_F(10.876)
        //   → 헤드룸 9.624, 상한 19.893 · 하한 0.3275 · 행정 19.566.
        static float Headroom(Transform root, Transform spreader, out string limitPair)
        {
            limitPair = "(장애물 없음)";
            var parts = new System.Collections.Generic.List<(Bounds b, string name)>();
            foreach (var r in spreader.GetComponentsInChildren<Renderer>())
                if (!IsRope(r.transform)) parts.Add((r.bounds, r.transform.name));
            if (parts.Count == 0) return 0f;

            Bounds sb = parts[0].b;
            for (int i = 1; i < parts.Count; i++) sb.Encapsulate(parts[i].b);

            bool anyMeshRead = false;
            float best = float.MaxValue;
            var cand = new System.Collections.Generic.List<(Bounds b, string name)>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var t = r.transform;
                if (IsUnder(t, spreader)) continue;                          // 스프레더 자신
                if (IsRope(t)) continue;                                     // 리빙 로프는 스프레더까지 내려오므로 장애물이 아님
                Bounds rb = r.bounds;
                if (rb.max.y <= sb.min.y) continue;                          // 스프레더보다 통째로 아래
                if (rb.max.x <= sb.min.x || rb.min.x >= sb.max.x) continue;  // XZ 풋프린트 미겹침 → 안 닿음
                if (rb.max.z <= sb.min.z || rb.min.z >= sb.max.z) continue;

                // 이 렌더러가 위에 걸칠 수 있는 스프레더 부품만 추림 — 삼각형 루프를 이 후보들로만 돈다.
                cand.Clear();
                foreach (var p in parts)
                    if (rb.max.y > p.b.max.y &&
                        rb.max.x > p.b.min.x && rb.min.x < p.b.max.x &&
                        rb.max.z > p.b.min.z && rb.min.z < p.b.max.z) cand.Add(p);
                if (cand.Count == 0) continue;

                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var verts = mf.sharedMesh.vertices;
                var tris  = mf.sharedMesh.triangles;
                if (verts.Length == 0 || tris.Length == 0) continue;
                anyMeshRead = true;

                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 v0 = t.TransformPoint(verts[tris[i]]);
                    Vector3 v1 = t.TransformPoint(verts[tris[i + 1]]);
                    Vector3 v2 = t.TransformPoint(verts[tris[i + 2]]);
                    float yLo = Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y));
                    float yHi = Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y));
                    float xLo = Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x));
                    float xHi = Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x));
                    float zLo = Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z));
                    float zHi = Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z));
                    foreach (var p in cand)
                    {
                        if (yHi <= p.b.max.y) continue;                      // 이 부품 위가 아님
                        if (xHi < p.b.min.x || xLo > p.b.max.x) continue;    // 삼각형 XZ 투영 미겹침
                        if (zHi < p.b.min.z || zLo > p.b.max.z) continue;
                        float gap = yLo - p.b.max.y;
                        if (gap < best)
                        {
                            best = gap;
                            limitPair = $"{t.name}(밑면 {yLo:F4}) ↔ {p.name}(상단 {p.b.max.y:F4})";
                        }
                    }
                }
            }

            if (!anyMeshRead)
            {
                Debug.LogWarning("[RTG] 헤드룸 산출: 장애물 메시 정점을 하나도 읽지 못했습니다(isReadable=0 + Play 모드?). " +
                                 "권상 상한을 신뢰할 수 없습니다 — 에디트 모드에서 'RTG 크레인 생성'을 다시 실행하세요.");
                return 0f;
            }
            return best == float.MaxValue ? 0f : Mathf.Max(0f, best);
        }

        static bool IsUnder(Transform t, Transform ancestor)
        {
            for (var p = t; p != null; p = p.parent) if (p == ancestor) return true;
            return false;
        }

        // 정적/동적 로프 모두 제외(이름으로 판정) — 로프를 장애물로 세면 헤드룸이 0이 된다.
        static bool IsRope(Transform t) => t.name.StartsWith("Hoist_Rope");

        static Bounds CombinedBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // 트롤리 로컬X 범위: 휠 외측면이 레일 끝에 닿을 때가 한계 → x ∈ [railMin−(wMin−x0), railMax−(wMax−x0)].
        //   실측(문서 §4): 레일 ±12.550 − 휠 그룹 반폭 2.455 = ±10.095.
        static bool TrolleyRange(Transform root, Transform trolley, out float min, out float max)
        {
            min = max = 0f;
            Transform frame = trolley.parent != null ? trolley.parent : root;   // localPosition.x가 사는 프레임
            var rails  = new System.Collections.Generic.List<Renderer>();
            var wheels = new System.Collections.Generic.List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.transform.name.StartsWith("TrolleyRail") && !IsUnder(r.transform, trolley)) rails.Add(r);
            // ★ 접두어 끝 '_' 필수 — Trolley_WheelBearing_* 를 휠로 세면 반폭이 틀어진다.
            foreach (var r in trolley.GetComponentsInChildren<Renderer>(true))
                if (r.transform.name.StartsWith("Trolley_Wheel_")) wheels.Add(r);
            if (rails.Count == 0 || wheels.Count == 0) return false;
            if (!LocalRangeX(frame, rails,  out float railMin, out float railMax)) return false;
            if (!LocalRangeX(frame, wheels, out float wMin,    out float wMax))    return false;

            float x0 = trolley.localPosition.x;
            min = railMin - (wMin - x0);
            max = railMax - (wMax - x0);
            if (min > max) { min = max = (min + max) * 0.5f; }   // 휠이 레일보다 길면(모델 이상) 중앙 고정
            return true;
        }

        // frame-로컬 X 범위. 로컬 bbox 8코너를 직접 변환(월드 AABB 경유 시 회전에 부풀어 틀어짐).
        static bool LocalRangeX(Transform frame, System.Collections.Generic.List<Renderer> rs, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds lb = mf.sharedMesh.bounds;
                Vector3 c = lb.center, e = lb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                 (i & 2) == 0 ? -e.y : e.y,
                                                 (i & 4) == 0 ? -e.z : e.z);
                    float v = frame.InverseTransformPoint(r.transform.TransformPoint(corner)).x;
                    min = Mathf.Min(min, v); max = Mathf.Max(max, v);
                }
            }
            return min <= max;
        }

        // 크레인 월드 AABB의 8코너를 frame 로컬로 변환해 axis(0/1/2) 반폭(로컬 단위) 산출.
        static float LocalHalfExtent(GameObject crane, Transform frame, int axis)
        {
            var b = CombinedBounds(crane);
            Vector3 c = b.center, e = b.extents;
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                             (i & 2) == 0 ? -e.y : e.y,
                                             (i & 4) == 0 ? -e.z : e.z);
                float v = frame.InverseTransformPoint(corner)[axis];
                mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v);
            }
            return (mx - mn) * 0.5f;
        }
    }
}
#endif
