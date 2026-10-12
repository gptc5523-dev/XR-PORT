#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 스프레더 — 중앙 고정부(항상 20ft) + 좌/우 텔레스코픽 암(끝빔 + 트위스트락).
        // spreaderHalf(반길이)로 암 위치를 정함: 20ft=중앙 끝에 밀착, 40ft=바깥으로 신장(텔레스코핑 빔이 연결).
        static void BuildSpreaderVisual(Transform spreader, float spreaderHalf)
        {
            // 컨테이너 긴 축이 안벽/주행 방향(Z)을 향하도록 스프레더 전체를 90° 회전
            // (부속은 긴 축=로컬 X로 배치 → Y축 90° 회전으로 월드 Z가 긴 축이 됨)
            spreader.localRotation = Quaternion.Euler(0f, 90f, 0f);

            float hw = 0.05f;                  // 반폭(로컬 Z) — 컨테이너 폭 비례(20·40ft 동일)
            const float hl0 = SpreaderHalf20;  // 중앙 고정부 반길이(항상 20ft)
            Color CMetal = CRail;              // 트위스트락 회색

            // 중앙 고정부(항상 20ft) — 메인 빔 + 상·하 플랜지
            Box(spreader, "Spreader_Bar", Vector3.zero,
                new Vector3(hl0 * 2f, 0.028f, hw * 2f), CSpread);
            for (int sy = -1; sy <= 1; sy += 2)
                Box(spreader, "Beam_Flange", new Vector3(0f, sy * FlangeCenterY, 0f),
                    new Vector3(hl0 * 2f, FlangeThick, hw * 2f + 0.008f), CSpread);

            // 헤드블록
            float hbY = 0.058f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Strut(spreader, "Head_Frame",
                    new Vector3(sx * 0.06f, 0.016f, sz * 0.038f),
                    new Vector3(sx * 0.035f, hbY - 0.012f, sz * 0.02f), 0.006f, CSpread);
            // [디자인] 헤드블록 확대 — 넓힌 데드엔드 소켓 4(로컬 x±HoistSprZ=0.075, z±HoistSprX=0.05 + 콘 베이스 0.0085)가 모서리 밖으로 안 삐지게.
            //   반폭 x 0.09(>0.0835), z 0.0625(>0.0585) → 각 변 ~0.005~0.007 여유.
            Box(spreader, "Spreader_Head", new Vector3(0f, hbY, 0f),
                new Vector3(0.18f, 0.026f, 0.125f), CSpread);
            // STS 데드엔드형: 로프 4가닥이 시브 없이 헤드블록에 정착. 스프레더 Y 90° 회전이라 로컬은 xz 스왑.
            //   HoistX는 스프레더 transform이 부여 — 소켓 로컬에 중복 금지.
            foreach (float rx in new[] { -HoistSprZ, HoistSprZ })
            foreach (float rz in new[] { -HoistSprX, HoistSprX })
            {
                Vector3 sk = new Vector3(rx, hbY + 0.013f, rz);   // 헤드블록 상단면(0.071), 회전 보정해 로프 바로 아래
                // 스펠터 소켓 — 위 좁은 넥(r0.0045)=로프 인입측, 아래 넓은 바스켓(r0.0085)=헤드블록 정착측(트롤리 소켓과 동일 규약).
                Cone(spreader, "Head_Rope_Socket",
                    sk + new Vector3(0f, -0.002f, 0f), sk + new Vector3(0f, 0.012f, 0f), 0.0085f, 0.0045f, CStruct);
                Rod(spreader, "Head_Rope_Collar",                 // 넥 칼라 밴드(단조 디테일)
                    sk + new Vector3(0f, 0.008f, 0f), sk + new Vector3(0f, 0.011f, 0f), 0.0055f, CMachine);
                Rod(spreader, "Head_Rope_Pin",                    // 클레비스 핀(베이스 관통, 양옆 돌출)
                    sk + new Vector3(0f, 0.001f, -0.011f), sk + new Vector3(0f, 0.001f, 0.011f), 0.0022f, CDark);
            }

            // 부속(중앙) — 파워팩/정션박스/작업등
            Vector3[] ppTips = PowerPack(spreader, "Spreader_PowerPack", new Vector3(0.07f, 0.022f, 0f),
                new Vector3(0.04f, 0.022f, hw * 1.2f), CMachine);
            JunctionBox(spreader, "Spreader_JBox", new Vector3(-0.07f, 0.02f, 0f),
                new Vector3(0.03f, 0.018f, 0.03f), Vector3.up, Vector3.down, CDark);
            // 스프레더 작업등(양 끝) — 구 → 하향 Floodlight 어셈블리(컨테이너 조사).
            for (int sx = -1; sx <= 1; sx += 2)
                Floodlight(spreader, new Vector3(sx * 0.085f, 0.016f, 0f), 0.010f, CDark, CLight);

            // 유압/제어 호스 — 파워팩 포트 → J박스 → 헤드블록. 끝점을 부품 안으로 묻고 클램프로 접합부 봉합(틈 제거).
            for (int i = 0; i < 2; i++)
            {
                int sz = i == 0 ? -1 : 1;
                float cz = sz * 0.008f;
                Vector3 jbIn = new Vector3(-0.062f, 0.022f, cz);          // J박스 내부로 묻은 끝점
                Vector3 hbIn = new Vector3(-0.038f, 0.05f,  sz * 0.012f); // 헤드블록 내부로 묻은 끝점

                // 파워팩 포트(ppTips) → J박스 (포트 끝에서 시작, J박스 안으로 종단)
                CableCatenary(spreader, "Spreader_Hose", ppTips[i], jbIn, 0.005f, 0.0014f, CCable);
                Ball(spreader, "Hose_Clamp", new Vector3(-0.056f, 0.024f, cz), Vector3.one * 0.005f, CStruct);

                // J박스(내부) → 헤드블록 밑면(안으로 묻음) : 제어 커넥터
                CableCatenary(spreader, "Spreader_Hose",
                    new Vector3(-0.063f, 0.026f, cz), hbIn, 0.004f, 0.0013f, CCable);
                Ball(spreader, "Hose_Clamp", new Vector3(-0.04f, 0.046f, sz * 0.012f), Vector3.one * 0.005f, CStruct);
            }

            // 좌/우 텔레스코픽 암(끝빔 + 트위스트락) — 런타임에 20↔40ft 슬라이드.
            //   각 암을 Transform으로 묶어 로컬 X만 옮기면 끝빔·트위스트락이 통째로 슬라이드(SpreaderTelescope).
            Transform armL = null, armR = null;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var arm = new GameObject(Numbered(sx < 0 ? "TeleArm_L" : "TeleArm_R"));
                arm.transform.SetParent(spreader, worldPositionStays: false);
                arm.transform.localPosition = new Vector3(sx * spreaderHalf, 0f, 0f);
                Transform a = arm.transform;
                if (sx < 0) armL = a; else armR = a;

                // 끝단 크로스 빔(컨테이너 단부) — 암 원점
                Box(a, "End_Beam", new Vector3(-sx * 0.008f, 0f, 0f),
                    new Vector3(0.016f, 0.03f, hw * 2f + 0.012f), CSpread);
                // 트위스트락 2(앞/뒤 코너) — 둥근 핀(Head) + 길쭉 쐐기 락 콘(Cone). 실물 구조.
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    // 트위스트락 Z를 컨테이너 외폭/2가 아닌 ISO 코너캐스팅 '횡 중심'에 정렬.
                    //   중심 = (컨테이너폭/2 − CornerCastD/2)/24.
                    const float isoCornerHalfZ = 0.04742f;
                    // 트위스트락 X를 ISO 코너캐스팅 중심에 정렬 — arm 원점=spreaderHalf(컨테이너 끝), 코너는 안쪽.
                    //   20ft·40ft 모두 spreaderHalf−≈0.0042.
                    Vector3 c = new Vector3(-sx * 0.0042f, 0f, sz * isoCornerHalfZ);
                    TwistlockHead(a, c, CMetal);
                    TwistlockCone(a, c, CMetal);
                }
            }

            // 텔레스코핑 빔(스프레더 직속) — 중앙 끝(hl0)↔암 사이를 SpreaderTelescope가 X 스케일로 신축.
            // 20ft면 수축(거의 사라짐), 40ft면 신장. 초기 크기는 생성 사이즈 기준(에디터 표시용).
            float initLen = Mathf.Max(0.0001f, (spreaderHalf - hl0) + 0.02f);
            float initMid = (hl0 + spreaderHalf) * 0.5f;
            Transform teleL = Box(spreader, "Tele_Beam", new Vector3(-initMid, 0f, 0f),
                new Vector3(initLen, 0.018f, hw * 1.2f), CDark).transform;
            Transform teleR = Box(spreader, "Tele_Beam", new Vector3(initMid, 0f, 0f),
                new Vector3(initLen, 0.018f, hw * 1.2f), CDark).transform;

            // 런타임 텔레스코픽 전환(시작 사이즈 = 생성 시 spreaderHalf)
            var tele = spreader.gameObject.AddComponent<SpreaderTelescope>();
            tele.Configure(armL, armR, teleL, teleR, hl0, SpreaderHalf20, SpreaderHalf40,
                           spreaderHalf > hl0 + 1e-4f);
        }

        /// <summary>트위스트락 '락 높이'를 콘 메시 정점에서 실측한다 — FBX RTG처럼 생성기 상수가 없는 크레인용.
        /// 절차 STS를 대조군으로 함께 찍는다(정답: 락 76.8mm = 노즈 52.8+숄더 24) — STS가 안 맞으면 RTG 값도 믿지 말 것.</summary>
        [MenuItem("Model/PG/크레인/트위스트락 락 높이 실측", false, 3)]
        public static void MeasureLockHeight()
        {
            if (Application.isBatchMode) EditorSceneManager.OpenScene(StsPartNames.PortScenePath);
            foreach (var crane in Object.FindObjectsByType<StsCrane>())
            {
                Transform body = crane.Spreader is Component sc ? sc.transform : null;
                // 콘은 4개 전부 모은다 — 하나만 제외하면 본체 최저면에 나머지 콘이 잡혀 노출이 0으로 나온다.
                var cones = new List<Transform>();
                foreach (var t in crane.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(StsPartNames.TwistlockCone) || t.name.StartsWith(StsPartNames.SpreaderTwistlockPrefix)) cones.Add(t);
                Transform cone = cones.Count > 0 ? cones[0] : null;
                if (cone == null || body == null) { Debug.Log($"[락높이] {crane.name} — 콘 미탐색"); continue; }

                // 콘 정점 수집(월드) — 축은 정점 XZ 평균.
                var pts = new List<Vector3>();
                foreach (var mf in cone.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh; if (m == null) continue;
                    foreach (var v in m.vertices) pts.Add(mf.transform.TransformPoint(v));
                }
                if (pts.Count == 0) { Debug.Log($"[락높이] {crane.name} {cone.name} — 메시 정점 없음(스킨드 메시?)"); continue; }
                Vector3 axis = Vector3.zero;
                foreach (var p in pts) axis += p;
                axis /= pts.Count;

                float tipY = float.MaxValue, topY = float.MinValue;
                foreach (var p in pts) { tipY = Mathf.Min(tipY, p.y); topY = Mathf.Max(topY, p.y); }
                float toMm = 1000f / StsConfig.ModelScale;                       // 모델 단위 → 실척 mm
                int n = Mathf.Max(1, Mathf.CeilToInt((topY - tipY) * toMm)) + 1;  // 실척 1mm 버킷
                var maxR = new float[n];
                foreach (var p in pts)
                {
                    int b = Mathf.Clamp(Mathf.FloorToInt((p.y - tipY) * toMm), 0, n - 1);
                    float r = new Vector2(p.x - axis.x, p.z - axis.z).magnitude * toMm;
                    if (r > maxR[b]) maxR[b] = r;
                }
                float rMax = 0f;
                foreach (var r in maxR) rMax = Mathf.Max(rMax, r);

                // 숄더 = 최대 반경의 ShoulderFrac 이상인 버킷 중 '가장 위' — 그 위가 넥(샤프트로 좁아짐).
                int shoulderTop = 0;
                for (int i = 0; i < n; i++) if (maxR[i] >= rMax * ShoulderFrac) shoulderTop = i;
                float lockMm = shoulderTop + 1f;                                  // 콘 끝 → 숄더 상단(실척 mm)

                // 지금 노출 = 콘 아닌 스프레더 최저면 − 콘 끝(SpreaderGrabber·StsGrabProbe 와 같은 식).
                float bodyB = float.MaxValue;
                foreach (var r in body.GetComponentsInChildren<Renderer>())
                {
                    bool inCone = false;
                    foreach (var c in cones) if (r.transform == c || r.transform.IsChildOf(c)) { inCone = true; break; }
                    if (!inCone) bodyB = Mathf.Min(bodyB, r.bounds.min.y);
                }
                float exposeMm = bodyB < float.MaxValue ? (bodyB - tipY) * toMm : float.NaN;

                // 부재별 Y 구간(콘 끝 기준 실척 mm) — 이 오브젝트가 '콘만'인지 '하우징까지 포함'인지 가른다.
                //   하우징까지면 콘 전체 길이·노출의 의미가 달라지므로 수치를 그대로 쓰면 안 된다.
                var parts = new System.Text.StringBuilder();
                foreach (var r in cone.GetComponentsInChildren<Renderer>())
                    parts.Append($"{r.name}[{(r.bounds.min.y - tipY) * toMm:F0}~{(r.bounds.max.y - tipY) * toMm:F0}] ");

                // 정점이 있는 버킷만 찍는다 — 저폴리 메시는 고정 간격으로 찍으면 대부분 0.0이 나와 형상을 못 읽는다.
                var prof = new System.Text.StringBuilder();
                int filled = 0;
                for (int i = 0; i < n; i++) if (maxR[i] > 0f) { prof.Append($"{i}:{maxR[i]:F1} "); filled++; }
                Debug.Log($"[락높이] {crane.name} {cone.name} — 콘 전체 {(topY - tipY) * toMm:F0}mm · " +
                          $"락(노즈+숄더) {lockMm:F0}mm · 현재 노출 {exposeMm:F0}mm · 부족 {lockMm - exposeMm:F0}mm · " +
                          $"최대반경 {rMax:F1}mm · 정점링 {filled}개/{n}버킷 · 콘 {cones.Count}개\n" +
                          $"    반경 프로파일(정점 있는 버킷만, 실척 mm:반경): {prof}\n" +
                          $"    부재(콘 끝 기준 실척 mm 구간): {parts}");
            }
            Debug.Log("[락높이] 대조군 확인 — 절차 STS 의 정답은 락 76.8mm(노즈 52.8 + 숄더 24). 이 값이 안 나오면 계측을 믿지 말 것.");
        }

        /// <summary>숄더(평평한 베어링 밴드) 판정 — 버킷 최대 반경이 콘 최대 반경의 이 비율 이상이면 숄더로 본다.</summary>
        const float ShoulderFrac = 0.98f;

        /// <summary>씬에 이미 구워진 절차 STS 트위스트락 콘 그룹을 생성기 값(<see cref="ConeTipY"/>)에 맞춘다 —
        /// 크레인은 다시 굽지 않고 콘 그룹 로컬 Y만 옮긴다. FBX RTG(Spreader_Twistlock_*)는 건드리지 않는다.</summary>
        [MenuItem("Model/PG/크레인/트위스트락 노출 씬 동기화", false, 2)]
        public static void SyncTwistlockExposure()
        {
            if (Application.isBatchMode) EditorSceneManager.OpenScene(StsPartNames.PortScenePath);
            int moved = 0, already = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                // 이름 규약은 StsGrabProbe·SpreaderGrabber 와 같다 — 절차 STS 는 'Twistlock_Cone'(+번호), FBX RTG 는 'Spreader_Twistlock_'(제외).
                if (!t.name.StartsWith(StsPartNames.TwistlockCone)) continue;
                Vector3 p = t.localPosition;
                if (Mathf.Abs(p.y - ConeTipY) < 1e-6f) { already++; continue; }
                Undo.RecordObject(t, "트위스트락 노출 씬 동기화");
                Debug.Log($"[StsCraneCreator] {t.parent?.name}/{t.name} 콘 끝 {p.y:F5} → {ConeTipY:F5}u " +
                          $"(실척 {(ConeTipY - p.y) / StsConfig.ModelScale * 1000f:+0;-0}mm)");
                p.y = ConeTipY;
                t.localPosition = p;
                moved++;
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
            if (Application.isBatchMode && moved > 0) EditorSceneManager.SaveOpenScenes();   // 배치(클론 검증)에서는 바로 저장
            Debug.Log($"[StsCraneCreator] 트위스트락 노출 동기화 — 옮김 {moved}개 · 이미 맞음 {already}개 · " +
                      $"노출 = 락 높이 {LugH / StsConfig.ModelScale * 1000f:F1}mm(실척). " +
                      (moved > 0 && !Application.isBatchMode ? "씬을 저장하세요(Ctrl+S)." : "변경 없음 또는 저장됨."));
        }

        // ── 스프레더 밑면 · 트위스트락 노출 길이(모델 단위 · 실척 = ×24) ──────────────────────────
        //   안착 시 스프레더 밑면이 컨테이너 최상면에 얹히고, 노출 = 락(노즈+숄더) 높이만큼 아래로 나와야 한다.
        const float FlangeCenterY = 0.016f, FlangeThick = 0.006f;         // 하단 Beam_Flange — 중심 · 두께
        /// <summary>스프레더 본체 최하단(하단 플랜지 밑면). 안착 시 컨테이너 최상면에 닿는 면 — 삽입 깊이의 기준면.</summary>
        const float SpreaderBodyBottom = -(FlangeCenterY + FlangeThick * 0.5f);   // -0.019u
        /// <summary>락 헤드 구간 높이 — 노즈(유도 쐐기) · 숄더(캐스팅 밑에 걸리는 베어링 밴드) · 넥(샤프트 전이).</summary>
        const float LugNoseH = 0.0022f, LugShoulderH = 0.0010f, LugNeckH = 0.0028f;
        /// <summary>'락' 높이 = 콘 끝 → 숄더 상단. 코너캐스팅 구멍 안으로 들어가야 하는 길이(실척 76.8mm).</summary>
        const float LugH = LugNoseH + LugShoulderH;                       // 0.0032u
        /// <summary>콘 끝(Twistlock_Cone 그룹 원점) Y = 본체 밑면 − 락 높이 ⇒ 락 전체가 본체 밑으로 노출된다.</summary>
        const float ConeTipY = SpreaderBodyBottom - LugH;                 // -0.0222u (실척 노출 76.8mm)
        /// <summary>넥 상단 = 샤프트가 시작되는 높이. 플랜지 두께 안이라 샤프트는 빔 속에 숨는다.</summary>
        const float ConeNeckTopY = ConeTipY + LugH + LugNeckH;            // -0.0162u

        // 트위스트락 핀(둥근 샤프트) — 스프레더 코너 하우징 안의 회전 너트에 나사 체결된 원형 핀.
        //   핀은 원형이라 회전이 시각적으로 무변화 — '보이는 잠금'은 뭉툭한 락 헤드(숄더)가 담당. 샤프트는 전부 빔 속에 숨는다.
        static void TwistlockHead(Transform arm, Vector3 corner, Color metal)
        {
            var head = new GameObject(Numbered(StsPartNames.TwistlockHead));
            head.transform.SetParent(arm, worldPositionStays: false);
            head.transform.localPosition = corner;       // 콘과 동일한 코너 수직축 = 트위스트 회전축
            Transform h = head.transform;

            // 체결 너트/칼라 — 끝빔 밑면에 물리는 머시닝 칼라(실물 더블칼라 나사부). 빔 속~밑면.
            Rod(h, "Twistlock_Collar", new Vector3(0f, 0.006f, 0f), new Vector3(0f, 0.000f, 0f), 0.0013f, CMachine);
            // 단조 핀 샤프트 — 빔 속(+0.005)에서 락 헤드 넥 상단(arm -0.014)까지. 끝이 빔 밑면(-0.015)보다 위라 전부 빔 속에 숨음(곤봉 방지).
            Rod(h, "Twistlock_Body",   new Vector3(0f, 0.005f, 0f), new Vector3(0f, ConeNeckTopY, 0f), 0.001f, metal);
        }

        // 트위스트락 콘: 아래 유도 노즈 → 넓은 숄더(캐스팅에 걸림) → 넥. 장축 Z로 삽입, 90° 돌면 X로 잠김.
        //   타원 단면은 headGroup X스케일로 만든다.
        static void TwistlockCone(Transform arm, Vector3 corner, Color metal)
        {
            var cone = new GameObject(Numbered(StsPartNames.TwistlockCone));
            cone.transform.SetParent(arm, worldPositionStays: false);
            cone.transform.localPosition = corner + new Vector3(0f, ConeTipY, 0f);
            Transform c = cone.transform;

            const float zHalf     = 0.00217f;  // 장축 half (104mm/24/2) — 숄더 최대폭
            const float xHalf     = 0.00117f;  //  단축 half (56mm/24/2)
            const float shaftHalf = 0.001f;    // 샤프트 반경(=Twistlock_Body) — 넥 상단
            const float tipHalf   = 0.0009f;   // 노즈 끝 반경(단조 블런트 촉)

            // 타원 단면 그룹 — 원형 프러스텀을 X로 눌러 Z장축/X단축 타원으로(스케일 ≈ 0.539).
            //   이름이 "Twistlock_Head"와 겹치면 SpreaderLockAnimator가 이중 수집·회전하므로 반드시 다른 이름.
            var ell = new GameObject("Twistlock_LockHead");
            ell.transform.SetParent(c, worldPositionStays: false);
            ell.transform.localScale = new Vector3(xHalf / zHalf, 1f, 1f);
            Transform h = ell.transform;

            // 노즈(유도 쐐기): local y 0→LugNoseH, tip→숄더로 벌어짐. tip Y = 그룹원점(= 콘 끝 · 본체 밑면보다 LugH 아래).
            Cone(h, "Twistlock_LugNose", new Vector3(0f, 0f,        0f), new Vector3(0f, LugNoseH, 0f), tipHalf, zHalf,     metal, 20);
            // 숄더(베어링 밴드): LugNoseH→LugH, 곧은 옆면 — 90° 회전 시 코너캐스팅 밑에 걸리는 어깨. 여기까지가 구멍에 들어가야 한다.
            Cone(h, "Twistlock_LugBody", new Vector3(0f, LugNoseH,  0f), new Vector3(0f, LugH,     0f), zHalf,   zHalf,     metal, 20);
            // 넥(샤프트 전이): LugH→LugH+LugNeckH(= 플랜지 두께 안, 빔 속), 숄더→샤프트로 좁혀 위쪽 샤프트에 매끈히 연결.
            Cone(h, "Twistlock_LugNeck", new Vector3(0f, LugH,      0f), new Vector3(0f, LugH + LugNeckH, 0f), zHalf, shaftHalf, metal, 20);
        }

        // 호이스트 로프 4줄 — spreaderRoot(붐 레벨 y=0) → 스프레더 헤드.
        // 정적 생성 후 HoistRopeRig가 매 프레임 스프레더 Y에 맞춰 신축(게임 런타임).
        static void BuildHoistRopes(Transform spreaderRoot, Transform spreader)
        {
            float topY = -0.02f;               // 로프 상단 = 트롤리 리빙 시브 바로 아래, 트롤리 본체 내부
            // 로프 하단 = 헤드블록 데드엔드 소켓 베이스(스프레더-로컬 hbY+0.011≈0.069).
            float attachOffsetY = 0.07f;       // 스프레더 원점 → 헤드블록 소켓 베이스(로프 하단 정착점)
            float radius = 0.0035f;
            float restBotY = SpreaderRestY + attachOffsetY;

            var ropes = new List<Transform>();
            var anchors = new List<Vector2>();   // 상·하 정착점이 같은 XZ(수직 로프)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float x = HoistX + sx * HoistSprX;   // 인양점 항구쪽 이동 + 넓힌 X간격(4가닥 분리)
                float z = sz * HoistSprZ;            // 스프레더 헤드블록 코너에 맞춘 수직 로프(넓힌 Z간격)
                var rope = Rod(spreaderRoot, "Hoist_Rope",
                    new Vector3(x, topY, z), new Vector3(x, restBotY, z), radius, CCable);
                ropes.Add(rope.transform);
                anchors.Add(new Vector2(x, z));
            }
            // 매 프레임 트롤리↔스프레더 사이로 로프 신축(상≠하 각진 리빙)
            var rig = spreaderRoot.gameObject.AddComponent<HoistRopeRig>();
            rig.Configure(spreader, topY, attachOffsetY, ropes.ToArray(),
                          anchors.ToArray(), anchors.ToArray(), radius);
        }
    }
}
#endif
