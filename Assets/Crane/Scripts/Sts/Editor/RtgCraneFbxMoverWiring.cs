#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// Blender 임포트 RTG 크레인("RTG 크레인")에 **핵심 구동 무버**를 붙이고 배선한다.
    /// 「Model ▸ FBX ▸ 크레인 ▸ RTG 크레인 생성」이 자동 호출한다(수동 메뉴 없음).
    ///
    /// 붙이는 것(축이 Blender→Unity 변환 프레임과 일치하는 것만):
    ///   • GantryMover   (루트, 로컬 Z = 주행)
    ///   • TrolleyMover  (Trolley, 로컬 X = 거더 횡행) + 스프레더 X 동기
    ///   • SpreaderHoist (Spreader, 로컬 Y = 권상)
    ///   • Rigidbody(kinematic) + StsCrane(애그리게이터) 배선
    ///
    /// 범위(Min/Max)는 **임포트된 실제 지오메트리/피벗에서 자동 산출** — 하드코딩 금지
    /// (절차생성 크레인과 좌표·스케일 관례가 달라 값 복사는 틀림).
    ///
    /// 신축(Telescope)은 <see cref="RtgSpreaderTelescopeSetup"/>가 단독 소유 — 생성 시 이 툴 다음에 이어서 호출된다.
    /// 여기서 제외: Flipper. VR컨트롤러/그래버도 XR 리그 의존이라 제외 — 필요 시 별도 부착.
    /// 배선 후 각 무버를 선택하면 기즈모로 Min/Max 범위 선이 보이므로 눈으로 검증 가능.
    /// </summary>
    public static class RtgCraneFbxMoverWiring
    {
        const string CraneName = "RTG 크레인";

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
            // Trolley X: 휠 외측면이 레일 끝에 닿는 지점. 레일·휠 실지오메트리에서 산출(실측 ±10.095 재현).
            //   ※ 옛 식 ±0.72×스팬반폭은 근거 없는 계수라 ±9.07로 1.02m 짧았다 —
            //     문서/크레인_동적데이터/RTG_크레인_동적데이터.md §4 참조.
            if (!TrolleyRange(root, trolleyT, out float txMin, out float txMax))
            {
                float halfX = LocalHalfExtent(crane, root, 0);
                txMin = -0.72f * halfX; txMax = 0.72f * halfX;
                Debug.LogWarning("[RTG] TrolleyRail_*/Trolley_Wheel_* 를 못 찾아 트롤리 범위를 스팬 비율(±72%)로 폴백했습니다. " +
                                 "FBX 노드명이 바뀌었는지 확인하세요.");
            }

            // Hoist Y(월드 절대): FBX는 로컬Y≠월드상이라 월드 Y로 직접 구동.
            // 상한 = 스프레더가 머리 위 구조물에 닿기 직전(헤드룸에서 산출 — 유도는 Headroom() 주석).
            //   ※ 임포트 포즈를 상한으로 쓰면 안 된다 — 모델의 스프레더는 도킹이 아니라 로프 중간에 매달린 자세라
            //     상한으로 잡는 순간 위로 전혀 못 올라간다(실측 양정의 절반만 사용).
            //     실측 근거: 문서/크레인_동적데이터/RTG_크레인_동적데이터.md §5 — 한계쌍은 주거더 밑면(20.500)
            //     ↔ Spreader_TeleBeam_F 상단(10.876), 헤드룸 9.624m(실척) → 상한 월드 z 19.893.
            float headroom = Headroom(root, spreadT, out string limitPair);
            float hyMax = spreadT.position.y + headroom;
            // 하한 = 그랩 평면(트위스트락 콘 바닥 = 스프레더 최저점)이 지면에 닿는 높이.
            // 상한과 같은 방식으로 실지오메트리에서 산출 — 상수 금지.
            //   ※ 옛 식 root.y + 0.02f 는 스케일맹이었다: 1/24 스케일에서 2cm = 실척 0.48m라
            //     그랩 평면이 지면 위 0.157m(실척)에 떠서 컨테이너가 바닥에 안 닿았다.
            //     실측 근거: 문서/크레인_동적데이터/RTG_크레인_동적데이터.md §5 — 그랩 평면은
            //     스프레더 원점보다 0.3275 아래, 하한 월드 z 0.3275(행정 19.566). 아래 식이 이를 재현한다.
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

            // ── 컨테이너 결합점(SpreaderAttach) + 그랩버(SpreaderGrabber) ──
            //   Attach: 부모변경+kinematic(축 무관). 결합점=스프레더. 컨테이너 위치는 필요시 오프셋 조정.
            var attach = GetOrAdd<SpreaderAttach>(spreadT.gameObject);
            attach.Configure(spreadT);

            var sts = GetOrAdd<StsCrane>(crane);
            sts.Configure(null, trolley, hoist, attach, gantry);

            // Grabber: [RequireComponent(StsCrane)] 충족(sts 먼저 부착됨). 트위스트락(Spreader_Twistlock_*) 자동 탐색.
            var grabber = GetOrAdd<SpreaderGrabber>(crane);

            // ── 트위스트락 잠금 애니 ──
            //   신축(RtgSpreaderTelescope)은 여기서 손대지 않는다 — RtgSpreaderTelescopeSetup이 단독 소유하며
            //   생성 흐름에서 이 메서드 직후 호출된다(빔 탐색·기준자세 캡처가 그쪽이 정확).
            var lockAnim = GetOrAdd<SpreaderLockAnimator>(spreadT.gameObject);
            lockAnim.SetWorldVertical(true);    // FBX 축 우회(월드 수직 기준 회전·딥)

            // ── 보기 스티어링(0°/90°) ──
            //   Bogie_* 4개를 킹핀 축으로 꺾고, 90° 완료 시 갠트리 주행축을 로컬 Z→X로 인계한다.
            var steer = GetOrAdd<RtgBogieSteering>(crane);
            steer.Configure(root, gantry);
            if (steer.BogieCount != 4)
                Debug.LogWarning($"[RTG] 보기를 {steer.BogieCount}/4개만 찾았습니다 — 스티어링이 일부만 돕니다. " +
                                 "FBX에 Bogie_LF/LB/RF/RB 가 있는지 확인하세요.");

            // Configure로 넣은 값(범위·참조)이 Play/도메인리로드에도 유지되도록 오버라이드/씬 기록.
            // ※ Play 모드에선 MarkSceneDirty/오버라이드 기록이 금지 → 에디트 모드에서만(런타임엔 값이 바로 적용됨).
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

        // 컴포넌트 있으면 반환, 없으면 부착. ??/?.는 Unity의 오버로드된 ==(가짜 null)을 우회하므로 금지 → 명시적 == null.
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

        // 호이스트 상한 헤드룸(월드 단위) = 스프레더가 위로 올라갈 수 있는 거리
        //   = min over (장애물 삼각형 T, 스프레더 부품 S | XZ 겹치고 T가 S 위) ( T밑면 − S상단 )
        //
        // 아래 셋을 동시에 지켜야 값이 맞는다 — 하나만 빠져도 거더를 뚫거나 양정을 손해본다:
        //   ① 트롤리가 아니라 **크레인 전체**를 훑는다. 주거더(GantryFrame_Body)는 Structure 소속이라
        //      트롤리 자식이 아니다. 트롤리만 보면 거더를 통째로 놓쳐 Trolley_DeckFrame(월드z 23.250)까지
        //      올라간다 → 스프레더가 거더를 1.52m(실척) 관통. 이게 실제로 났던 버그다.
        //   ② 장애물은 렌더러 AABB가 아니라 **삼각형 단위**로 본다. GantryFrame_Body는 다리+거더가
        //      한 메시(월드z 2.01~22.52)라 AABB 밑면이 다리 바닥이라 헤드룸이 0으로 무너진다.
        //   ③ 스프레더도 통짜 bbox가 아니라 **부품 단위**로 본다. RTG는 z(Blender y) ±3.0~4.75 쌍거더에
        //      가운데가 뚫린 슬롯이라 시브(±1.58)는 거더에 안 막히고 슬롯으로 올라간다. 통짜로 보면
        //      시브가 막힌 걸로 오판해 1.23m 손해다.
        //
        // 실측 검증(Blender RTG_Crane_Scene, 2026-07-15, 전부 실척 m):
        //   한계쌍 GantryFrame_Body(밑면 20.500) ↔ Spreader_TeleBeam_F(상단 10.876) → 헤드룸 9.624
        //   → 상한 월드 10.269+9.624 = 19.893 · 하한 0.3275 · 행정 19.566.
        //   문서/크레인_동적데이터/RTG_크레인_동적데이터.md §5 참조.
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

        // 정적 로프(Hoist_Rope_FL)·동적 튜브(Hoist_Rope_FL_Dyn) 모두. 정적은 로프 셋업이 이미 숨기지만
        // 이 배선만 단독 실행될 때를 대비해 이름으로도 막는다(로프를 장애물로 세면 헤드룸이 0이 된다).
        static bool IsRope(Transform t) => t.name.StartsWith("Hoist_Rope");

        static Bounds CombinedBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // 트롤리 로컬X 범위 = 레일 X 범위에서 휠 그룹이 삐져나가지 않는 조건.
        //   휠 외측면이 레일 끝에 닿을 때가 한계: railMin ≤ (휠최소 + 이동량), (휠최대 + 이동량) ≤ railMax.
        //   이동량 = x − x0 이므로 → x ∈ [railMin − (wMin − x0), railMax − (wMax − x0)].
        //   실측(문서 §4): 레일 ±12.550 − 휠 그룹 반폭 2.455 = ±10.095.
        static bool TrolleyRange(Transform root, Transform trolley, out float min, out float max)
        {
            min = max = 0f;
            Transform frame = trolley.parent != null ? trolley.parent : root;   // localPosition.x가 사는 프레임
            var rails  = new System.Collections.Generic.List<Renderer>();
            var wheels = new System.Collections.Generic.List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.transform.name.StartsWith("TrolleyRail") && !IsUnder(r.transform, trolley)) rails.Add(r);
            // 접두어 끝 '_' 필수 — Trolley_WheelBearing_* 를 휠로 세면 반폭이 틀어진다(휠은 Trolley_Wheel_1..4 뿐).
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

        // 렌더러들의 frame-로컬 X 범위. 로컬 bbox 8코너를 직접 변환한다 —
        // Renderer.bounds(월드 AABB)를 거치면 크레인이 회전해 있을 때 범위가 부풀어 한계가 틀어진다.
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
