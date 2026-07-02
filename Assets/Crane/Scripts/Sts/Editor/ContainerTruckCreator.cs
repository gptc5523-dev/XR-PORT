using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using ContainerProject;

namespace Container.Crane.Sts.EditorTools
{
    // 컨테이너 트럭(트랙터 + 40ft 컨테이너 섀시 트레일러) 절차 생성.
    //   크레인 스프레더가 컨테이너를 내려 섀시 위 트위스트락에 안착시키는 시나리오용.
    //   외부 모델 import 없이 ProBuilder 박스(PbBox)/실린더/콘으로만 조형 → 디자이너가 바로 편집.
    //   스케일·헬퍼·머티리얼은 StsCraneCreator(같은 partial 클래스)의 자산을 그대로 재사용한다.
    //
    //   좌표계(로컬): 길이축 +Z(트랙터=−Z, 트레일러 후미=+Z), 폭 X, 높이 Y, 타이어 바닥 Y=0.
    //   실척 m × Scale(1/24) = 모델 단위. 주석의 m 값은 실척 레퍼런스.
    public static partial class StsCraneCreator
    {
        // ── 핵심 치수(모델 단위, 1/24) ──────────────────────────────────────
        const float Tk_TireR     = 1.05f * 0.5f * Scale;   // 타이어 반경 (직경 1.05m)
        const float Tk_TireW     = 0.30f * Scale;          // 타이어 폭
        const float Tk_DeckTopY  = 1.45f * Scale;          // 섀시 데크 상면 = 컨테이너 안착 높이
        const float Tk_FifthY    = 1.20f * Scale;          // 5th wheel(킹핀) 높이
        const float Tk_HalfTrack = 1.00f * Scale;          // 듀얼 타이어 외측 트랙 반값
        const float Tk_DualGap   = 0.36f * Scale;          // 듀얼 한 쌍 내·외측 타이어 중심 간격

        // 40ft 컨테이너 코너캐스팅 중심(트위스트락 위치) — ISO 1496
        const float Tk_TLHalfZ40 = 11.985f * 0.5f * Scale; // ±0.2497 (길이축)
        const float Tk_TLHalfZ20 = 5.853f  * 0.5f * Scale; // ±0.1219 (20ft)
        const float Tk_TLHalfX   = 2.259f  * 0.5f * Scale; // ±0.0470 (폭)

        // 길이축 주요 Z 좌표
        // 실물 MAN TGX 기준: 전방 오버행(전륜→범퍼) 1.475m, 휠베이스(전륜→구동축) ~3.4m, 캡 깊이 ~2.7m.
        const float Tk_BumperZ   = -7.95f * Scale;   // 트랙터 앞 범퍼 (전륜 −6.55 + 오버행 1.40)
        const float Tk_CabBackZ  = -5.10f * Scale;   // 캡 뒤면(캡 깊이 2.7m로 단축 → 길쭉한 박스 인상 제거)
        const float Tk_CabFrontZ = -7.80f * Scale;   // 캡 전면(범퍼 바로 뒤)
        const float Tk_FrontAxZ  = -6.55f * Scale;   // 전륜 축(휠베이스 3.43m)
        const float Tk_DriveAxZ  = -3.12f * Scale;   // 구동축/킹핀   (−0.130)
        const float Tk_ContCZ     = 2.86f * Scale;   // 40ft 컨테이너 중심Z (+0.119) = 킹핀+반길이
        const float Tk_RearBumpZ  = 9.84f * Scale;   // 트레일러 후미 (+0.410)

        static readonly Color Tk_CabRed   = new Color(0.70f, 0.085f, 0.095f); // 캡 레드(도장강 폴백)
        static readonly Color Tk_Chassis  = new Color(0.16f, 0.17f, 0.20f);   // 섀시 프레임(다크 그레이)
        static readonly Color Tk_Tire     = new Color(0.09f, 0.09f, 0.10f);   // 타이어
        static readonly Color Tk_Rim      = new Color(0.62f, 0.64f, 0.68f);   // 휠 림(알루미늄)
        static readonly Color Tk_DarkGlass= new Color(0.15f, 0.21f, 0.28f);   // 측면 틴트 유리(near-black 탈피, 유리톤 유지)
        static readonly Color Tk_WindGlass= new Color(0.22f, 0.31f, 0.40f);   // 앞유리 틴트 글래스(유리 PBR 분기용 전용 색)
        static readonly Color Tk_Frame    = new Color(0.30f, 0.31f, 0.34f);   // 메인 프레임강(섀시보다 밝게 — 하부 명도 계단 상단)
        static readonly Color Tk_FrameLit = new Color(0.40f, 0.41f, 0.43f);   // 데크 상부 플랜지(컨테이너 안착면 명시)
        static readonly Color Tk_WheelDisc= new Color(0.45f, 0.46f, 0.49f);   // 스틸휠 디스크(핸드홀·러그 형상 살림)
        static readonly Color Tk_Amber    = new Color(1.00f, 0.55f, 0.06f);   // 전방·측면 마커/방향지시(앰버 — 전방 적색 금지 준수)
        static readonly Color Tk_RedRefl  = new Color(0.82f, 0.06f, 0.07f);   // 후미 적색 반사판/컨스피큐티 적
        static readonly Color Tk_WhiteRefl= new Color(0.90f, 0.90f, 0.90f);   // 컨스피큐티 백

        [MenuItem("Object/트럭/컨테이너 트럭 생성", false, 8)]
        public static void CreateContainerTruck()
        {
            _nameSeq.Clear();
            var existing = GameObject.Find("ContainerTruck");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("ContainerTruck").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Create Container Truck");

            BuildTractor(root);
            BuildTrailer(root);

            // 컨테이너가 데크 위에 안착하도록 데크 상면 콜라이더(크레인이 내려놓으면 물리로 멈춤).
            var deck = new GameObject("TruckDeck_Collider");
            deck.transform.SetParent(root, false);
            var bc = deck.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, Tk_DeckTopY - 0.002f, Tk_ContCZ);
            bc.size   = new Vector3(2.30f * Scale, 0.004f, 12.6f * Scale);

            ReportOverlaps(root);

            Selection.activeGameObject = root.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("[Truck] 컨테이너 트럭 생성 완료 — 데크 상면 Y=" + Tk_DeckTopY.ToString("F4")
                      + " (40ft 트위스트락 Z=±" + Tk_TLHalfZ40.ToString("F3") + ", X=±" + Tk_TLHalfX.ToString("F3") + ")");
        }

        // ── 겹침 검출(물리/수학팀): 부품 AABB 교차 부피 / 작은쪽 부피 비가 임계 초과인 쌍을 로그 ──
        //   구조상 겹침이 정상인 것(라운드박스 내부·I빔 플랜지·휠 내부 등)은 이름으로 제외.
        static void ReportOverlaps(Transform root)
        {
            // 제외는 "한 박스/휠/빔을 이루는 내부 조각"만 — 실부품(펜더·마커·다리·연료탱크 등)은 절대 제외 안 함.
            string[] skip = { "_Core", "_Edge", "_Corner", "_Lug", "_Hub", "_Rim", "_Disc", "_Hole", "_Tread", "_BeadLip", "Flange", "BeamWeb", "FaceMask", "Headlight" };
            var rends = root.GetComponentsInChildren<MeshRenderer>();
            var allBounds = new System.Collections.Generic.List<Bounds>();   // 고립 검사용(모든 부품, skip 포함)
            foreach (var r in rends) allBounds.Add(r.bounds);
            var items = new System.Collections.Generic.List<(string bn, Bounds b, float v)>();
            foreach (var r in rends)
            {
                string nm = r.gameObject.name;
                bool sk = false; foreach (var s in skip) if (nm.Contains(s)) { sk = true; break; }
                if (sk) continue;
                var b = r.bounds; var sz = b.size;
                items.Add((System.Text.RegularExpressions.Regex.Replace(nm, "_\\d+$", ""), b, sz.x * sz.y * sz.z));
            }
            int hits = 0;
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (items[i].bn == items[j].bn) continue;   // 같은 종류(인접 반복)는 제외
                    Bounds a = items[i].b, c = items[j].b;
                    float ox = Mathf.Min(a.max.x, c.max.x) - Mathf.Max(a.min.x, c.min.x);
                    float oy = Mathf.Min(a.max.y, c.max.y) - Mathf.Max(a.min.y, c.min.y);
                    float oz = Mathf.Min(a.max.z, c.max.z) - Mathf.Max(a.min.z, c.min.z);
                    if (ox <= 0f || oy <= 0f || oz <= 0f) continue;
                    // 관통 깊이 = 최소축 겹침(실척 m). 2cm 초과면 실제 관통으로 보고.
                    float penM = Mathf.Min(ox, Mathf.Min(oy, oz)) / Scale;
                    if (penM > 0.02f)
                    {
                        Debug.LogWarning($"[Overlap] {items[i].bn} ∩ {items[j].bn}  관통={penM:F3}m");
                        hits++;
                    }
                }
            // ── 부착(연결성) 검사: 3cm 이내 접촉을 간선으로 union-find → 최대 연결요소=본체.
            //   본체에 속하지 않는 부품 = 미부착(떠있음). 클러스터로 함께 떠도 검출됨(최근접 이웃 맹점 제거).
            int n = allBounds.Count;
            int[] par = new int[n]; for (int i = 0; i < n; i++) par[i] = i;
            System.Func<int, int> find = null;
            find = (x) => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
            float touch = 0.03f * Scale;   // 3cm 이내면 접촉(부착)
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    Bounds a = allBounds[i], c = allBounds[j];
                    float gx = Mathf.Max(0f, Mathf.Max(a.min.x - c.max.x, c.min.x - a.max.x));
                    float gy = Mathf.Max(0f, Mathf.Max(a.min.y - c.max.y, c.min.y - a.max.y));
                    float gz = Mathf.Max(0f, Mathf.Max(a.min.z - c.max.z, c.min.z - a.max.z));
                    if (gx * gx + gy * gy + gz * gz <= touch * touch)
                    { int ra = find(i), rb = find(j); if (ra != rb) par[ra] = rb; }
                }
            // 최대 연결요소(본체) 찾기
            var comp = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < n; i++) { int r = find(i); comp[r] = comp.TryGetValue(r, out var v) ? v + 1 : 1; }
            int bodyRoot = -1, bodyMax = -1;
            foreach (var kv in comp) if (kv.Value > bodyMax) { bodyMax = kv.Value; bodyRoot = kv.Key; }
            int floats = 0;
            for (int i = 0; i < n; i++)
            {
                if (find(i) == bodyRoot) continue;
                Debug.LogWarning($"[Detached] {rends[i].gameObject.name} 미부착(본체와 안 붙음) — 클러스터 {comp[find(i)]}개");
                floats++;
            }
            Debug.Log($"[Overlap] 검사 완료 — 관통 {hits}쌍, 미부착 {floats}개 (본체 {bodyMax}/{n}개)");
        }

        // ── 트랙터(캡오버 세미트랙터, MAN TGX 풍) ──────────────────────────
        //   실물 비율(2026-07-01 MAN 사양서 기준): 폭 2.50m, 캡 깊이 2.70m, 전방 오버행 1.40m,
        //   휠베이스 3.43m, 캡 높이(루프) 3.70m, 타이어 Ø1.05m. 각진 캡오버 전면을 패널 단위로 조형.
        static void BuildTractor(Transform root)
        {
            var t = new GameObject("Tractor").transform;
            t.SetParent(root, false);

            float cabW    = 2.50f * Scale;                       // 폭
            float halfW   = cabW * 0.5f;
            float frontZ  = Tk_CabFrontZ;                        // 캡 전면
            float backZ   = Tk_CabBackZ;                         // 캡 후면
            float cabZc   = (frontZ + backZ) * 0.5f;
            float cabDz   = (backZ - frontZ);                    // 캡 깊이(≈2.70m)
            float floorY  = 1.50f * Scale;                       // 캡 바닥(도어 하단)
            float roofY   = 3.70f * Scale;                       // 캡 루프
            float cabYc   = (floorY + roofY) * 0.5f;
            float cabH    = (roofY - floorY);
            float faceZ   = frontZ - 0.005f * Scale;             // 전면 패널 부착면(살짝 앞)

            // ── 섀시 프레임 레일(범퍼~5th wheel 후방) ──
            //   레일 뒤끝을 킹핀(−3.12)에서 −2.60까지 연장 → 5th wheel 플레이트(z −3.72~−2.52) 대부분을 받쳐 캔틸레버 완화.
            float railRearZ = Tk_DriveAxZ + 0.52f * Scale;   // −2.60 (구동 내측타이어 x겹침 ≤1cm)
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "Tractor_FrameRail", new Vector3(s * 0.42f * Scale, 1.10f * Scale, (Tk_BumperZ + railRearZ) * 0.5f),
                      new Vector3(0.16f * Scale, 0.30f * Scale, (railRearZ - Tk_BumperZ)), Tk_Frame);
            // 크로스멤버(프레임 가로재 2개)
            for (int i = 0; i < 2; i++)
                PbBox(t, "Tractor_CrossMember", new Vector3(0f, 1.10f * Scale, Mathf.Lerp(Tk_FrontAxZ, Tk_DriveAxZ, 0.3f + i * 0.5f)),
                      new Vector3(0.84f * Scale, 0.14f * Scale, 0.14f * Scale), Tk_Frame);

            // ── 캡 본체(2단 라운드박스) — 압출 슬래브 대신 모서리 라운드로 박스 실루엣 제거 ──
            //   하부 매스(그릴/램프/범퍼 면, 앞 z=−7.80) 위에 그린하우스(창문부)를 얹되 앞면을 0.20 set-back(−7.60)
            //   → 벨트라인에 카울 단차가 생겨 정면이 '한 판때기'로 안 읽힘. 모든 수직·루프 모서리 r0.13~0.15 라운드.
            //   측면 도어/창/미러가 ±1.25에 부착되므로 그린하우스도 전폭 2.50 유지(텀블홈은 측면창 뜸 유발→제외).
            float cabZc2 = (frontZ + backZ) * 0.5f;   // 캡 중심 z(−6.45)
            float cabDz2 = (backZ - frontZ);          // 캡 깊이 2.70
            RoundedBox(t, "Cab_LowerBody", new Vector3(0f, 1.90f * Scale, cabZc2),
                  new Vector3(cabW, 1.40f * Scale, cabDz2), 0.13f * Scale, Tk_CabRed);        // y1.20~2.60, 앞 −7.80
            RoundedBox(t, "Cab_Greenhouse", new Vector3(0f, 3.125f * Scale, cabZc2 + 0.10f * Scale),
                  new Vector3(cabW, 1.15f * Scale, cabDz2 - 0.20f * Scale), 0.15f * Scale, Tk_CabRed);  // y2.55~3.70, 앞 −7.60(카울)

            // ── 전면 요소: 모두 앞면 평면(z=−7.80)에 정렬, y구간 겹침 없음 ──
            float fcZ = -7.80f * Scale;
            //   윈드실드(그린하우스 앞면 −7.60에 flush proud) + A필러(코너 포스트, 유리 바깥)
            PbBox(t, "Cab_Windshield", new Vector3(0f, 3.08f * Scale, -7.70f * Scale),
                  new Vector3(1.90f * Scale, 1.00f * Scale, 0.05f * Scale), Tk_WindGlass);   // 유리 PBR(매끈·반사·강판텍스처 off) + proud0.10(z-파이팅 해소)
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "Cab_APillar", new Vector3(s * 1.02f * Scale, 3.08f * Scale, -7.61f * Scale),
                      new Vector3(0.10f * Scale, 1.06f * Scale, 0.10f * Scale), Tk_CabRed);
            //   와이퍼 2개(윈드실드 하단 y2.64, 살짝 proud)
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "Cab_Wiper", new Vector3(s * 0.42f * Scale, 2.64f * Scale, -7.74f * Scale),
                      new Vector3(cabW * 0.40f, 0.025f * Scale, 0.025f * Scale), CDark, new Vector3(0f, 0f, s * 6f));   // 윈드실드(-7.70) 앞 proud
            //   루프 앞 선바이저(검은 차양) — 윈드실드 상단 위로 앞으로 돌출
            PbBox(t, "Cab_SunVisorFront", new Vector3(0f, 3.66f * Scale, -7.66f * Scale),
                  new Vector3(2.10f * Scale, 0.10f * Scale, 0.34f * Scale), CDark, new Vector3(-16f, 0f, 0f));

            // ── 신형 TGX 페이스(재조형): 통합 다크 그릴 마스크 + 청키 코너 헤드램프 + 중앙 엠블럼 ──
            //   반성(정직): 이전 버전은 red 면 위에 흰 박스 헤드램프와 회색 도넛 엠블럼이 스티커처럼 떠
            //   토이룩이었다. 실물 TGX는 전면 중앙이 '하나의 어두운 마스크'이고 램프/엠블럼/슬랫이 그 위에
            //   얹혀 통합돼 보인다. 그 구조로 재조형. 램프/슬랫/엠블럼은 모두 마스크 위 proud(z 더 −).
            //   ① 통합 다크 마스크: 폭2.02 y1.37~2.53, 얼굴면보다 0.03 proud(리세스 프레임감). red 로어바디가 프레임.
            PbBox(t, "Cab_FaceMask", new Vector3(0f, 1.95f * Scale, fcZ - 0.005f * Scale),
                  new Vector3(2.02f * Scale, 1.16f * Scale, 0.05f * Scale), CDark);
            //   ② 청키 코너 헤드램프(마스크 상단 외측, 코너로 랩): 다크 스모크 렌즈 위에 발광부(백색 DRL L + 프로젝터2 + 앰버)만 밝게
            //   → '흰 카드' 제거. 실물 LED처럼 커버는 어둡고 DRL·프로젝터만 빛남. 내부 조각 겹침은 Headlight 스킵으로 제외(유닛 수동검증).
            //   hx±0.78 w0.48 → x0.54~1.02(마스크 폭2.02 안), y2.00~2.56. 하우징 z fcZ-0.03, 렌즈/발광부는 그 위 proud.
            Color hlLens = new Color(0.05f, 0.06f, 0.08f);   // 스모크 렌즈(짙은 커버)
            float hlY = 2.28f * Scale;
            for (int s = -1; s <= 1; s += 2)
            {
                float hx = s * 0.78f * Scale;
                Vector3 rake = new Vector3(0f, 0f, -s * 8f);   // 외측단 코너로 하향 스윕
                PbBox(t, "Cab_HeadlightHousing", new Vector3(hx, hlY, fcZ - 0.03f * Scale),
                      new Vector3(0.48f * Scale, 0.56f * Scale, 0.08f * Scale), Tk_Chassis, rake);
                PbBox(t, "Cab_HeadlightLens", new Vector3(hx, hlY, fcZ - 0.08f * Scale),
                      new Vector3(0.38f * Scale, 0.46f * Scale, 0.05f * Scale), hlLens, rake);
                //   백색 DRL L스트립(상단 가로 + 외측 세로) — 발광부
                PbBox(t, "Cab_HeadlightDRL", new Vector3(hx, hlY + 0.20f * Scale, fcZ - 0.095f * Scale),
                      new Vector3(0.36f * Scale, 0.045f * Scale, 0.04f * Scale), Tk_WhiteRefl, rake);
                PbBox(t, "Cab_HeadlightDRL", new Vector3(hx + s * 0.155f * Scale, hlY + 0.02f * Scale, fcZ - 0.095f * Scale),
                      new Vector3(0.045f * Scale, 0.40f * Scale, 0.04f * Scale), Tk_WhiteRefl, rake);
                //   라운드 프로젝터 2(하이/로우 빔, 밝은 렌즈) — 스모크 렌즈 안쪽 세로 배열
                for (int p = 0; p < 2; p++)
                {
                    var proj = NewPrimitive(PrimitiveType.Cylinder, "Cab_HeadlightProj", t);
                    proj.transform.localPosition = new Vector3(hx - s * 0.05f * Scale, hlY + (0.09f - p * 0.18f) * Scale, fcZ - 0.11f * Scale);
                    proj.transform.localRotation = Quaternion.Euler(90f, 0f, -s * 8f);
                    proj.transform.localScale = new Vector3(0.14f * Scale, 0.02f * Scale, 0.14f * Scale);
                    Colorize(proj, CLight);
                }
                //   하단 앰버 턴시그널(렌즈 하부, 발광부)
                PbBox(t, "Cab_HeadlightTurn", new Vector3(hx, hlY - 0.20f * Scale, fcZ - 0.095f * Scale),
                      new Vector3(0.34f * Scale, 0.06f * Scale, 0.04f * Scale), Tk_Amber, rake);
            }
            //   ③ 중앙 상단 리온 엠블럼(다크 실드 배킹 + 크롬 링 + 다크 중심) — 마스크 상단 y2.40, 배킹으로 도넛감 제거
            PbBox(t, "Cab_EmblemShield", new Vector3(0f, 2.40f * Scale, fcZ - 0.03f * Scale),
                  new Vector3(0.44f * Scale, 0.30f * Scale, 0.03f * Scale), CDark);
            var emblem = NewPrimitive(PrimitiveType.Cylinder, "Cab_Emblem", t);
            emblem.transform.localPosition = new Vector3(0f, 2.40f * Scale, fcZ - 0.05f * Scale);
            emblem.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            emblem.transform.localScale = new Vector3(0.30f * Scale, 0.03f * Scale, 0.22f * Scale);   // 크롬 링(가로0.30×세로0.22)
            Colorize(emblem, Tk_Rim);
            var emblemIn = NewPrimitive(PrimitiveType.Cylinder, "Cab_EmblemCore", t);
            emblemIn.transform.localPosition = new Vector3(0f, 2.40f * Scale, fcZ - 0.065f * Scale);
            emblemIn.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            emblemIn.transform.localScale = new Vector3(0.18f * Scale, 0.03f * Scale, 0.13f * Scale);
            Colorize(emblemIn, CDark);
            //   ④ 그릴 슬랫 5(마스크 중하부 y1.52~2.12, 중앙, 크롬) — 좌우 헤드램프 사이
            for (int i = 0; i < 5; i++)
                PbBox(t, "Cab_GrilleSlat", new Vector3(0f, (1.52f + i * 0.15f) * Scale, fcZ - 0.05f * Scale),
                      new Vector3(1.08f * Scale, 0.05f * Scale, 0.02f * Scale), Tk_Rim);

            // ── 범퍼(Actros풍: 바디색 대형 범퍼 + 중앙 검은 그릴 슬롯 + 포그 + 번호판 + 발판) ──
            //   범퍼 어셈블리 전체 y = bumpY 기준(요청: Cab_Bumper 약간 올림 0.74→0.82, 부속 정합 유지)
            float bumpY = 0.82f * Scale, bumpZ = Tk_BumperZ + 0.06f * Scale;
            // 범퍼 상단을 프레임레일 top(y=1.25)까지 연장: 바닥 0.57 고정, 상단 1.25 → 중심 0.91, 높이 0.68
            RoundedBox(t, "Cab_Bumper", new Vector3(0f, 0.91f * Scale, bumpZ),
                  new Vector3(cabW * 1.02f, 0.68f * Scale, 0.28f * Scale), 0.10f * Scale, Tk_CabRed);
            PbBox(t, "Cab_BumperSlot", new Vector3(0f, bumpY, bumpZ - 0.12f * Scale),
                  new Vector3(cabW * 0.34f, 0.26f * Scale, 0.06f * Scale), CDark);   // 중앙 검은 인테이크
            PbBox(t, "Cab_LicensePlate", new Vector3(0f, bumpY + 0.04f * Scale, bumpZ - 0.19f * Scale),
                  new Vector3(0.44f * Scale, 0.20f * Scale, 0.03f * Scale), Tk_Rim);   // 슬롯 앞면에 부착(관통 회피)
            for (int s = -1; s <= 1; s += 2)   // 다크 리세스 하우징 + 작은 렌즈(바 노출 흰박스=이빨 방지)
            {
                PbBox(t, "Cab_FogHousing", new Vector3(s * cabW * 0.36f, bumpY - 0.08f * Scale, bumpZ - 0.11f * Scale),
                      new Vector3(0.28f * Scale, 0.20f * Scale, 0.05f * Scale), CDark);
                PbBox(t, "Cab_FogLamp", new Vector3(s * cabW * 0.36f, bumpY - 0.08f * Scale, bumpZ - 0.15f * Scale),
                      new Vector3(0.16f * Scale, 0.11f * Scale, 0.04f * Scale), CLight);
            }
            PbBox(t, "Cab_BumperStep", new Vector3(0f, bumpY - 0.24f * Scale, bumpZ + 0.02f * Scale),
                  new Vector3(cabW * 0.5f, 0.05f * Scale, 0.16f * Scale), Tk_Rim);   // 하단 발판
            for (int s = -1; s <= 1; s += 2)   // 범퍼 좌우 끝 코너 마커(주황)
                PbBox(t, "Cab_BumperCornerLamp", new Vector3(s * cabW * 0.47f, bumpY + 0.08f * Scale, bumpZ - 0.10f * Scale),
                      new Vector3(0.10f * Scale, 0.14f * Scale, 0.05f * Scale), Tk_Amber);

            // ── 루프 디플렉터(라운드, 루프 위 밀착, 앞 낮고 뒤 높게 → 컨테이너 상단으로 매끈) ──
            //   루프 평평면 y=3.70(z −7.45~−5.10) 위에 바닥 닿게: 중심 y=3.70+높이/2. 앞끝 −7.30(루프 안).
            //   길이 1.30(후미 z=−6.10)으로 단축 → 에어혼(z −6.05~−5.40) 앞에서 끝나 관통 제거(뒤 0.05 이격).
            RoundedBox(t, "Cab_RoofDeflector", new Vector3(0f, roofY + 0.12f * Scale, frontZ + 1.05f * Scale),
                  new Vector3(cabW * 0.88f, 0.24f * Scale, 1.30f * Scale), 0.10f * Scale, Tk_CabRed, new Vector3(-7f, 0f, 0f));

            // ── 측면(도어 글래스 + 손잡이 + 승강 스텝) ──
            for (int s = -1; s <= 1; s += 2)
            {
                // ── 도어 기준 좌표 ──
                float xP  = s * (halfW + 0.006f * Scale);   // 표면 proud(트림·핸들·시임)
                float xF  = s * (halfW - 0.005f * Scale);   // 창틀(면 근처)
                float xG  = s * (halfW + 0.010f * Scale);   // 유리(틀보다 살짝 proud → 다크 실 테두리 보임)
                float dcz = cabZc - 0.05f * Scale;          // 도어 중앙 z(−6.50)
                float dFz = dcz - 0.80f * Scale;            // 도어 앞 경계(−7.30)
                float dRz = dcz + 0.72f * Scale;            // 도어 뒤 경계(−5.78)
                float dTy = 3.46f * Scale, dBy = 1.30f * Scale;   // 도어 상/하

                // ── 도어 셧라인(외곽 리세스 시임) ──
                PbBox(t, "Cab_DoorSeam", new Vector3(xP, (dTy + dBy) * 0.5f, dFz),
                      new Vector3(0.025f * Scale, dTy - dBy, 0.03f * Scale), CDark);   // 앞 세로
                PbBox(t, "Cab_DoorSeam", new Vector3(xP, (dTy + dBy) * 0.5f, dRz),
                      new Vector3(0.025f * Scale, dTy - dBy, 0.03f * Scale), CDark);   // 뒤 세로
                PbBox(t, "Cab_DoorSeam", new Vector3(xP, dTy, dcz),
                      new Vector3(0.025f * Scale, 0.03f * Scale, dRz - dFz), CDark);   // 상 가로
                PbBox(t, "Cab_DoorSeam", new Vector3(xP, dBy, dcz),
                      new Vector3(0.025f * Scale, 0.03f * Scale, dRz - dFz), CDark);   // 하 가로

                // ── 메인 창(상부): 다크 실 프레임 + 틴트 유리(proud) ──
                float mwYc = 3.02f * Scale, mwH = 0.80f * Scale;   // y2.62~3.42
                float mwZc = dcz + 0.06f * Scale, mwL = 1.20f * Scale;
                PbBox(t, "Cab_WindowFrame", new Vector3(xF, mwYc, mwZc),
                      new Vector3(0.03f * Scale, mwH, mwL), CDark);
                PbBox(t, "Cab_SideGlass", new Vector3(xG, mwYc, mwZc),
                      new Vector3(0.03f * Scale, mwH - 0.08f * Scale, mwL - 0.08f * Scale), Tk_DarkGlass);

                // ── 벨트 몰딩(창 하단 크롬 띠) + 하부 캐릭터 라인 ──
                PbBox(t, "Cab_BeltMolding", new Vector3(xP, 2.56f * Scale, dcz),
                      new Vector3(0.03f * Scale, 0.055f * Scale, dRz - dFz - 0.06f * Scale), Tk_Rim);
                PbBox(t, "Cab_DoorCrease", new Vector3(xP, 1.82f * Scale, dcz),
                      new Vector3(0.02f * Scale, 0.03f * Scale, dRz - dFz - 0.10f * Scale), CDark);

                // ── 커브(kerb) 하부 비전 창(도어 앞하단, 캡오버 시그니처) ──
                float kwZc = dFz + 0.36f * Scale, kwYc = 2.04f * Scale;
                PbBox(t, "Cab_KerbFrame", new Vector3(xF, kwYc, kwZc),
                      new Vector3(0.03f * Scale, 0.62f * Scale, 0.54f * Scale), CDark);
                PbBox(t, "Cab_KerbGlass", new Vector3(xG, kwYc, kwZc),
                      new Vector3(0.03f * Scale, 0.54f * Scale, 0.46f * Scale), Tk_DarkGlass);

                // ── 리세스 그랩핸들(벨트 아래) + A필러 승강 그랩바 ──
                PbBox(t, "Cab_DoorHandleRecess", new Vector3(xP, 2.44f * Scale, dcz + 0.34f * Scale),
                      new Vector3(0.05f * Scale, 0.11f * Scale, 0.26f * Scale), CDark);
                PbBox(t, "Cab_DoorHandle", new Vector3(xP, 2.44f * Scale, dcz + 0.34f * Scale),
                      new Vector3(0.05f * Scale, 0.05f * Scale, 0.20f * Scale), Tk_Rim);
                Rod(t, "Cab_GrabRail", new Vector3(xP, 1.95f * Scale, dFz - 0.02f * Scale),
                    new Vector3(xP, 2.95f * Scale, dFz - 0.02f * Scale), 0.03f * Scale, Tk_Rim);

                // ── 승강 스텝 2단(도어 아래, 전륜 바깥 x1.23) ──
                PbBox(t, "Cab_EntryStep", new Vector3(s * (halfW - 0.02f * Scale), 1.08f * Scale, dcz),
                      new Vector3(0.12f * Scale, 0.05f * Scale, 0.55f * Scale), CDark);
                PbBox(t, "Cab_EntryStep", new Vector3(s * (halfW + 0.01f * Scale), 0.72f * Scale, dcz),
                      new Vector3(0.14f * Scale, 0.05f * Scale, 0.50f * Scale), CDark);
            }

            // 사이드 미러 — 도어 앞기둥 옆(z=frontZ+0.28)에서 암으로 바깥(0.24)으로 확실히 띄워 돌출.
            //   캡면 ±1.25 → 하우징 중심 ±1.49(암 길이 0.24). 창문처럼 붙지 않게 충분히 이격.
            float mz = frontZ + 0.28f * Scale;
            for (int s = -1; s <= 1; s += 2)
            {
                float mxCab = s * halfW;                       // 캡 측면 부착점
                float mxOut = s * (halfW + 0.24f * Scale);     // 미러 하우징 중심
                // 지지암 2개(위·아래, 캡→미러 수평 연결)
                Strut(t, "Cab_MirrorArm", new Vector3(mxCab, 3.10f * Scale, mz),
                      new Vector3(mxOut, 3.10f * Scale, mz), 0.028f * Scale, Tk_Chassis);
                Strut(t, "Cab_MirrorArm", new Vector3(mxCab, 2.55f * Scale, mz),
                      new Vector3(mxOut, 2.55f * Scale, mz), 0.028f * Scale, Tk_Chassis);
                // 하우징 — 어두운 라운드 에어로 포드(빨간 판=안테나 느낌 제거). Y축 22° 틀어 거울면이 후방(운전자)을 향하게.
                Vector3 mAng = new Vector3(0f, s * 22f, 0f);
                RoundedBox(t, "Cab_Mirror", new Vector3(mxOut, 2.82f * Scale, mz),
                      new Vector3(0.09f * Scale, 0.72f * Scale, 0.22f * Scale), 0.04f * Scale, new Color(0.10f, 0.10f, 0.11f), mAng);
                // 거울면(안쪽 = 운전자 방향)
                PbBox(t, "Cab_MirrorGlass", new Vector3(mxOut - s * 0.05f * Scale, 2.82f * Scale, mz + 0.02f * Scale),
                      new Vector3(0.02f * Scale, 0.58f * Scale, 0.18f * Scale), Tk_DarkGlass, mAng);
            }

            // 루프 마커등(4) + 에어혼(루프 후방)
            for (int i = 0; i < 4; i++)   // 루프 앞끝(y=3.70, z≈−7.42) 위에 바닥 닿게(중심 y=3.70+0.04)
                PbBox(t, "Cab_RoofMarker", new Vector3((-1.5f + i) * 0.55f * Scale, roofY + 0.04f * Scale, frontZ + 0.38f * Scale),
                      new Vector3(0.16f * Scale, 0.08f * Scale, 0.10f * Scale), Tk_Amber);
            for (int s = -1; s <= 1; s += 2)   // 에어혼: 루프 평평영역 안(중심 z≈−5.7)
                Rod(t, "Cab_AirHorn", new Vector3(s * 0.65f * Scale, roofY + 0.05f * Scale, Tk_CabBackZ - 0.95f * Scale),
                    new Vector3(s * 0.65f * Scale, roofY + 0.05f * Scale, Tk_CabBackZ - 0.30f * Scale), 0.06f * Scale, Tk_Rim);

            // ── 캡 뒤 섀시(연료탱크 + 밴드 스트랩 + 배기 + 캣워크 데크판) ──
            // 탱크 뒤끝 = 구동타이어 앞끝(DriveAxZ−0.525)보다 0.10 앞으로 → 휠 관통 제거
            float tankA = Tk_CabBackZ + 0.10f * Scale, tankB = Tk_DriveAxZ - 0.63f * Scale;
            float tankC = (tankA + tankB) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                Rod(t, "Tractor_FuelTank", new Vector3(s * 1.02f * Scale, 0.82f * Scale, tankA),
                    new Vector3(s * 1.02f * Scale, 0.82f * Scale, tankB), 0.26f * Scale, Tk_Rim);
                for (int k = -1; k <= 1; k += 2)   // 탱크 고정 밴드 2개(탱크 범위 안, ±0.45)
                    PbBox(t, "Tractor_FuelStrap",
                          new Vector3(s * 1.02f * Scale, 0.82f * Scale, tankC + k * 0.45f * Scale),
                          new Vector3(0.56f * Scale, 0.55f * Scale, 0.03f * Scale), CDark);
                for (int k = -1; k <= 1; k += 2)   // 탱크 마운트 브래킷 2개(프레임레일 x0.42 ↔ 탱크 x1.02)
                    PbBox(t, "Tractor_FuelBracket",
                          new Vector3(s * 0.72f * Scale, 1.00f * Scale, tankC + k * 0.45f * Scale),
                          new Vector3(0.62f * Scale, 0.08f * Scale, 0.10f * Scale), Tk_Chassis);
            }
            Rod(t, "Tractor_Exhaust", new Vector3(1.04f * Scale, 1.3f * Scale, Tk_CabBackZ + 0.10f * Scale),
                new Vector3(1.04f * Scale, 3.4f * Scale, Tk_CabBackZ + 0.10f * Scale), 0.08f * Scale, Tk_Chassis);
            // 캣워크(킹핀 앞 미끄럼방지 데크)
            PbBox(t, "Tractor_Catwalk", new Vector3(0f, 1.28f * Scale, (Tk_CabBackZ + Tk_DriveAxZ) * 0.5f + 0.05f * Scale),
                  new Vector3(0.95f * Scale, 0.05f * Scale, (Tk_CabBackZ - Tk_DriveAxZ) * 0.55f), Tk_Chassis);

            // ── 5th wheel(킹핀 커플러) 판 + 진입 램프 ──
            PbBox(t, "FifthWheel", new Vector3(0f, Tk_FifthY, Tk_DriveAxZ),
                  new Vector3(1.2f * Scale, 0.10f * Scale, 1.2f * Scale), Tk_Chassis);
            PbBox(t, "FifthWheel_Ramp", new Vector3(0f, Tk_FifthY - 0.06f * Scale, Tk_DriveAxZ + 0.55f * Scale),
                  new Vector3(1.2f * Scale, 0.08f * Scale, 0.7f * Scale), Tk_Chassis, new Vector3(18f, 0f, 0f));
            // 그리스 슬라이딩 판(밝은 상면) + 킹핀 록킹 조(x슬롯, 킹핀 −3.12 감쌈) — '물린 커플러' 신호
            PbBox(t, "FifthWheel_GreasePlate", new Vector3(0f, Tk_FifthY + 0.055f * Scale, Tk_DriveAxZ),
                  new Vector3(0.98f * Scale, 0.02f * Scale, 0.98f * Scale), Tk_Rim);
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "FifthWheel_Jaw", new Vector3(s * 0.22f * Scale, Tk_FifthY + 0.09f * Scale, Tk_DriveAxZ - 0.02f * Scale),
                      new Vector3(0.30f * Scale, 0.10f * Scale, 0.44f * Scale), Tk_Frame);
            // 좌측 릴리즈 핸들(L자, 바깥→후방) + 하부 마운트 행어(판↔프레임레일)
            Rod(t, "FifthWheel_Release", new Vector3(-0.58f * Scale, Tk_FifthY - 0.02f * Scale, Tk_DriveAxZ + 0.18f * Scale),
                new Vector3(-0.86f * Scale, Tk_FifthY - 0.02f * Scale, Tk_DriveAxZ + 0.18f * Scale), 0.02f * Scale, Tk_Rim);
            Rod(t, "FifthWheel_Release", new Vector3(-0.86f * Scale, Tk_FifthY - 0.02f * Scale, Tk_DriveAxZ + 0.18f * Scale),
                new Vector3(-0.86f * Scale, Tk_FifthY - 0.02f * Scale, Tk_DriveAxZ + 0.42f * Scale), 0.02f * Scale, Tk_Rim);
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "FifthWheel_Mount", new Vector3(s * 0.44f * Scale, Tk_FifthY - 0.14f * Scale, Tk_DriveAxZ),
                      new Vector3(0.14f * Scale, 0.18f * Scale, 0.62f * Scale), Tk_Frame);

            // ── 서비스 라인 3선(적 에어·청 에어·흑 전기) — 캡 헤드보드 → 트레일러 노즈, ConduitPath 매끈 드룹 ──
            PbBox(t, "Tractor_HeadboardShelf", new Vector3(0f, 1.75f * Scale, Tk_CabBackZ + 0.06f * Scale),
                  new Vector3(0.70f * Scale, 0.10f * Scale, 0.14f * Scale), Tk_Chassis);
            Color svcRed = new Color(0.72f, 0.10f, 0.10f), svcBlue = new Color(0.12f, 0.28f, 0.62f);
            float[] svcX = { -0.14f, 0.00f, 0.14f };
            Color[] svcC = { svcRed, svcBlue, CDark };
            for (int i = 0; i < 3; i++)
            {
                var pts = new Vector3[]
                {
                    new Vector3(svcX[i] * Scale, 1.72f * Scale, Tk_CabBackZ + 0.04f * Scale),
                    new Vector3(svcX[i] * Scale, 1.44f * Scale, -4.20f * Scale),
                    new Vector3(svcX[i] * Scale, 1.40f * Scale, -3.52f * Scale),
                };
                ConduitPath(t, "Tractor_ServiceLine", pts, 0.022f * Scale, 0.14f * Scale, svcC[i]);
            }

            // ── 휠 + 펜더(전륜 싱글+아치 펜더, 구동축 듀얼+머드플랩) ──
            SingleAxle(t, "Tractor_Steer", Tk_FrontAxZ);
            DualAxle(t, "Tractor_Drive", Tk_DriveAxZ);
            // 전륜 아치 펜더(바디색) — 떠 있는 듯한 전륜을 캡과 시각적으로 연결
            for (int s = -1; s <= 1; s += 2)
                FrontFender(t, new Vector3(s * Tk_HalfTrack, Tk_TireR, Tk_FrontAxZ));
            // 구동축 머드가드(상단 평판) — 타이어 top(1.05) 위 0.01 이격, 뒤로 연장해 머드플랩을 받침.
            PbBox(t, "Tractor_DriveMudguard", new Vector3(0f, Tk_TireR * 2.0f + 0.035f * Scale, Tk_DriveAxZ + 0.08f * Scale),
                  new Vector3(2.55f * Scale, 0.05f * Scale, 1.15f * Scale), Tk_Chassis);
            // 머드플랩: 타이어 후단(z=−2.595) 뒤 1.5cm(z=−2.56)에 매달아 상단이 머드가드(하단 1.06)에 접합
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "Tractor_MudFlap", new Vector3(s * 1.05f * Scale, 0.55f * Scale, Tk_DriveAxZ + 0.56f * Scale),
                      new Vector3(0.5f * Scale, 1.0f * Scale, 0.04f * Scale), Tk_Tire);
        }

        // ── 라운드 박스(필렛) ─────────────────────────────────────────────
        //   수학(장현우/서지안): 코어 십자 3박스로 6면 평평 + 12모서리 원통(반경 r) + 8꼭짓점 구.
        //   → 박스의 모든 모서리를 반경 r로 둥글림. euler로 통째 회전(자식 홀더에 적용).
        static void RoundedBox(Transform parent, string name, Vector3 localPos, Vector3 size, float r, Color color, Vector3 euler = default)
        {
            var holder = new GameObject(Numbered(name + "_R")).transform;
            holder.SetParent(parent, false);
            holder.localPosition = localPos;
            if (euler != Vector3.zero) holder.localRotation = Quaternion.Euler(euler);

            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            r = Mathf.Min(r, Mathf.Min(hx, Mathf.Min(hy, hz)) - 0.0001f);
            float ex = hx - r, ey = hy - r, ez = hz - r;   // 모서리 중심 오프셋

            // ① 코어 십자 3박스(6면 평평)
            PbBox(holder, name + "_Core", Vector3.zero, new Vector3(size.x, size.y - 2f * r, size.z - 2f * r), color);
            PbBox(holder, name + "_Core", Vector3.zero, new Vector3(size.x - 2f * r, size.y, size.z - 2f * r), color);
            PbBox(holder, name + "_Core", Vector3.zero, new Vector3(size.x - 2f * r, size.y - 2f * r, size.z), color);

            // ② 12 모서리 원통 — 축별 4개
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                EdgeCyl(holder, name + "_Edge", new Vector3(0f, a * ey, b * ez), 0, size.x - 2f * r, r, color); // X평행
                EdgeCyl(holder, name + "_Edge", new Vector3(a * ex, 0f, b * ez), 1, size.y - 2f * r, r, color); // Y평행
                EdgeCyl(holder, name + "_Edge", new Vector3(a * ex, b * ey, 0f), 2, size.z - 2f * r, r, color); // Z평행
            }

            // ③ 8 꼭짓점 구
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            for (int c = -1; c <= 1; c += 2)
            {
                var sp = NewPrimitive(PrimitiveType.Sphere, name + "_Corner", holder);
                sp.transform.localPosition = new Vector3(a * ex, b * ey, c * ez);
                sp.transform.localScale = new Vector3(2f * r, 2f * r, 2f * r);
                Colorize(sp, color);
            }
        }

        // 모서리 원통 — axis 0=X,1=Y,2=Z 방향으로 길이 len, 반경 r.
        static void EdgeCyl(Transform parent, string name, Vector3 localPos, int axis, float len, float r, Color color)
        {
            var cy = NewPrimitive(PrimitiveType.Cylinder, name, parent);   // 기본: 축 Y, 높이2, 반경0.5
            cy.transform.localPosition = localPos;
            cy.transform.localRotation = axis == 0 ? Quaternion.Euler(0f, 0f, 90f)
                                       : axis == 2 ? Quaternion.Euler(90f, 0f, 0f)
                                                   : Quaternion.identity;
            cy.transform.localScale = new Vector3(2f * r, len * 0.5f, 2f * r);
            Colorize(cy, color);
        }

        // ── 측면 실루엣 압출 ──────────────────────────────────────────────
        //   차량 측면 윤곽(z=길이, y=높이)을 폭(width)만큼 압출 → 경사 윈드실드가 한 덩어리로
        //   흐르는 진짜 캡오버 실루엣. profile은 (z,y) 점들(실척 m, 내부에서 ×Scale은 호출부가 처리).
        static GameObject ExtrudeProfile(Transform parent, string name, Vector2[] profile, float width, Color color)
        {
            var pts = new System.Collections.Generic.List<Vector3>(profile.Length);
            foreach (var p in profile) pts.Add(new Vector3(p.x, 0f, p.y));   // XZ 평면(x=길이, z=높이), 노멀 Y로 압출
            var pb = UnityEngine.ProBuilder.ProBuilderMesh.Create();
            pb.name = Numbered(name);
            UnityEngine.ProBuilder.MeshOperations.AppendElements.CreateShapeFromPolygon(pb, pts, width, false);
            pb.ToMesh();
            pb.Refresh();
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = GetMaterial(color);
            var col = pb.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var tr = pb.transform;
            tr.SetParent(parent, false);
            // 로컬(x=길이,y=폭,z=높이) → 월드(z=길이,x=폭,y=높이) 매핑 + 폭 중앙 정렬
            tr.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.right);
            tr.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
            return pb.gameObject;
        }

        // 전륜 휠아치 — 타이어 바로 위에 밀착하는 다크 머드가드(3분할, 타이어색). center=타이어 중심.
        //   타이어 반경 r 표면을 감싸도록: 상단 캡 y=r*1.05, 앞·뒤 스커트가 타이어 곡면을 타고 내려감.
        static void FrontFender(Transform parent, Vector3 center)
        {
            float r = Tk_TireR;
            float w = Tk_TireW * 1.35f;
            // 상단 캡(타이어 top r*1.0 바로 위)
            PbBox(parent, "Tractor_SteerFender", center + new Vector3(0f, r * 1.06f, 0f),
                  new Vector3(w, 0.05f * Scale, r * 1.3f), Tk_Tire);
            // 앞·뒤 스커트(타이어 곡면을 타고 아래로) — 접선각으로 밀착
            for (int d = -1; d <= 1; d += 2)
                PbBox(parent, "Tractor_SteerFenderSkirt", center + new Vector3(0f, r * 0.80f, d * r * 0.72f),
                      new Vector3(w, 0.05f * Scale, r * 0.75f), Tk_Tire, new Vector3(d * 50f, 0f, 0f));
        }

        // ── 트레일러(40ft 컨테이너 섀시) ────────────────────────────────────
        static void BuildTrailer(Transform root)
        {
            var t = new GameObject("Trailer").transform;
            t.SetParent(root, false);

            float railTopY = Tk_DeckTopY;
            float railH    = 0.30f * Scale;
            float railYc   = railTopY - railH * 0.5f;
            float frontZ   = Tk_DriveAxZ - 0.30f * Scale;   // 거위목 시작(킹핀 근처)
            float backZ    = Tk_RearBumpZ;

            // ── 프레임 재설계(수학팀): 메인 I-빔 2개 + 거위목 + 크로스멤버 + 아우트리거 ──
            float beamX    = 0.50f * Scale;                 // 메인빔 중심 간격 반값
            float beamH    = 0.26f * Scale;                 // 빔 웹 높이
            float beamYc   = railTopY - beamH * 0.5f;       // 빔 중심(데크 아래)
            float kingpinZ = Tk_DriveAxZ;                   // 킹핀 z
            float kingY    = Tk_FifthY;                     // 킹핀 y(1.20)
            float gooseTopZ= kingpinZ + 1.70f * Scale;      // 거위목 끝 = 데크 시작
            float deckZc   = (gooseTopZ + backZ) * 0.5f, deckZl = backZ - gooseTopZ;

            // 메인 I-빔(웹 + 상·하 플랜지) — 데크 구간
            for (int s = -1; s <= 1; s += 2)
            {
                PbBox(t, "Trailer_BeamWeb", new Vector3(s * beamX, beamYc, deckZc),
                      new Vector3(0.07f * Scale, beamH, deckZl), Tk_Frame);
                PbBox(t, "Trailer_BeamFlangeTop", new Vector3(s * beamX, railTopY - 0.03f * Scale, deckZc),
                      new Vector3(0.20f * Scale, 0.05f * Scale, deckZl), Tk_FrameLit);
                PbBox(t, "Trailer_BeamFlangeBot", new Vector3(s * beamX, beamYc - beamH * 0.5f + 0.03f * Scale, deckZc),
                      new Vector3(0.20f * Scale, 0.05f * Scale, deckZl), Tk_Frame);
            }
            // 거위목(데크→킹핀 하강 경사 빔) + 킹핀 상판 + 킹핀
            for (int s = -1; s <= 1; s += 2)
                Strut(t, "Trailer_Gooseneck", new Vector3(s * beamX, railTopY - 0.15f * Scale, gooseTopZ),
                      new Vector3(s * beamX, kingY + 0.06f * Scale, kingpinZ + 0.10f * Scale), 0.15f * Scale, Tk_Frame);
            PbBox(t, "Trailer_KingpinPlate", new Vector3(0f, kingY, kingpinZ),
                  new Vector3(0.95f * Scale, 0.08f * Scale, 0.95f * Scale), Tk_Chassis);
            Rod(t, "Trailer_Kingpin", new Vector3(0f, kingY - 0.20f * Scale, kingpinZ),
                new Vector3(0f, kingY - 0.02f * Scale, kingpinZ), 0.026f * Scale, Tk_Rim);   // Ø52mm=SAE 2"

            // ── 트레일러 노즈 리셉터클: 글래드핸드(적·청)+7핀 전기소켓 — 트랙터 서비스라인(z≈−3.52) 종점 ──
            Color tSvcRed = new Color(0.72f, 0.10f, 0.10f), tSvcBlue = new Color(0.12f, 0.28f, 0.62f);
            PbBox(t, "Trailer_NosePlate", new Vector3(0f, 1.42f * Scale, kingpinZ - 0.30f * Scale),
                  new Vector3(0.44f * Scale, 0.30f * Scale, 0.06f * Scale), Tk_Frame);
            for (int j = -1; j <= 1; j += 2)
                Rod(t, "Trailer_Gladhand", new Vector3(j * 0.12f * Scale, 1.46f * Scale, kingpinZ - 0.34f * Scale),
                    new Vector3(j * 0.12f * Scale, 1.46f * Scale, kingpinZ - 0.44f * Scale), 0.03f * Scale, (j < 0) ? tSvcRed : tSvcBlue);
            PbBox(t, "Trailer_ElecSocket", new Vector3(0f, 1.33f * Scale, kingpinZ - 0.35f * Scale),
                  new Vector3(0.12f * Scale, 0.12f * Scale, 0.08f * Scale), CDark);

            // 크로스멤버 6개(등간격) + 사이드 아우트리거(좌우 끝, 데크 폭 지지)
            for (int i = 0; i < 6; i++)
            {
                float cz = Mathf.Lerp(gooseTopZ + 0.3f * Scale, backZ - 0.3f * Scale, i / 5f);
                PbBox(t, "Trailer_CrossMember", new Vector3(0f, beamYc, cz),
                      new Vector3(2f * beamX + 0.18f * Scale, 0.08f * Scale, 0.10f * Scale), Tk_Frame);
            }
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "Trailer_Outrigger", new Vector3(s * 1.20f * Scale, railTopY - 0.05f * Scale, deckZc),
                      new Vector3(0.05f * Scale, 0.07f * Scale, deckZl), Tk_Frame);

            // 크로스 볼스터(40ft 앞·뒤 코너) + 트위스트락 콘 4개
            float[] bz = { Tk_ContCZ - Tk_TLHalfZ40, Tk_ContCZ + Tk_TLHalfZ40 };
            string[] bn = { "Bolster_Front", "Bolster_Rear" };
            for (int i = 0; i < bz.Length; i++)
            {
                // center.y = railTopY − 0.07 → 상면 정확히 데크면 1.45(컨테이너 바닥)에 정렬. 트위스트락 콘만 코너캐스팅으로 돌출.
                PbBox(t, bn[i], new Vector3(0f, railTopY - 0.07f * Scale, bz[i]),
                      new Vector3(2.44f * Scale, 0.14f * Scale, 0.22f * Scale), Tk_Chassis);
                if (i == 0)   // 앞 볼스터 지지 라이저(킹핀 상판 top 1.24 ↔ 볼스터 저면 1.31 수직 공백 충전)
                    PbBox(t, "Trailer_BolsterRiser", new Vector3(0f, 1.31f * Scale, bz[i]),
                          new Vector3(0.95f * Scale, 0.18f * Scale, 0.30f * Scale), Tk_Chassis);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    Vector3 baseP = new Vector3(sx * Tk_TLHalfX, railTopY, bz[i]);
                    Cone(t, "Twistlock", baseP, baseP + new Vector3(0f, 0.16f * Scale, 0f),
                         0.085f * Scale, 0.045f * Scale, CDark);
                }
            }

            // 랜딩기어(받침다리) — 거위목 직후 데크빔에 마운트로 직결, 프레임 아래로 매달림.
            //   수치: 레그 top = lgCy+lgLegH/2 = 1.44 (< 데크면 1.45) → 적재 40ft 컨테이너 바닥 미관통.
            //   커플(트랙터 연결) 상태라 풋 바닥은 지상 0.58m로 접힘. lgZ는 거위목(gooseTopZ −1.42) 직후 첫 크로스멤버 앞.
            float lgZ      = gooseTopZ + 0.12f * Scale;   // −1.30: 거위목 뒤 데크빔 위(빔 웹에 마운트 직결)
            float lgX      = 0.90f * Scale;
            float lgLegH   = 0.86f * Scale;               // 레그 길이
            float lgCy     = 1.01f * Scale;               // 레그 중심 → top 1.44, bottom 0.58
            float lgBottom = lgCy - lgLegH * 0.5f;        // 0.58 (풋 상단)
            float lgGbY    = lgCy + lgLegH * 0.5f - 0.14f * Scale;   // 기어박스/크랭크 높이 1.30 (< 1.45)
            // 마운트 브래킷(빔 웹 ↔ 레그 상단, 데크 바로 아래) — 랜딩기어를 프레임에 직결
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "LandingGear_Mount", new Vector3(s * 0.71f * Scale, railTopY - 0.09f * Scale, lgZ),
                      new Vector3(0.50f * Scale, 0.10f * Scale, 0.14f * Scale), Tk_Chassis);
            for (int s = -1; s <= 1; s += 2)
            {
                PbBox(t, "LandingGear", new Vector3(s * lgX, lgCy, lgZ),
                      new Vector3(0.16f * Scale, lgLegH, 0.16f * Scale), Tk_Chassis);
                PbBox(t, "LandingGear_Gearbox", new Vector3(s * lgX, lgGbY, lgZ),
                      new Vector3(0.22f * Scale, 0.22f * Scale, 0.22f * Scale), Tk_Chassis);   // 2단 기어박스
                PbBox(t, "LandingGear_Foot", new Vector3(s * lgX, lgBottom + 0.03f * Scale, lgZ),
                      new Vector3(0.34f * Scale, 0.06f * Scale, 0.34f * Scale), Tk_Chassis);   // 샌드슈 풋패드
            }
            // 두 레그 연결 크로스샤프트(브레이스, 기어박스 아래 y1.10) + 크랭크(우측, 기어박스 높이)
            PbBox(t, "LandingGear_Brace", new Vector3(0f, lgCy + 0.09f * Scale, lgZ),
                  new Vector3(1.66f * Scale, 0.07f * Scale, 0.07f * Scale), Tk_Chassis);
            Rod(t, "LandingGear_Crank", new Vector3(lgX, lgGbY, lgZ - 0.10f * Scale),
                new Vector3(lgX, lgGbY, lgZ - 0.34f * Scale), 0.03f * Scale, Tk_Rim);
            Rod(t, "LandingGear_CrankHandle", new Vector3(lgX, lgGbY, lgZ - 0.34f * Scale),
                new Vector3(lgX, lgGbY - 0.17f * Scale, lgZ - 0.34f * Scale), 0.025f * Scale, Tk_Rim);

            // 에어 리시버 탱크(원통, 프레임 아래 가로) + ABS 모듈밸브 + 측면 마커등/리플렉터
            Rod(t, "Trailer_AirTank", new Vector3(-0.7f * Scale, railYc - 0.22f * Scale, frontZ + 2.6f * Scale),
                new Vector3(0.7f * Scale, railYc - 0.22f * Scale, frontZ + 2.6f * Scale), 0.14f * Scale, Tk_Rim);
            PbBox(t, "Trailer_ABSModule", new Vector3(0.55f * Scale, railYc - 0.20f * Scale, frontZ + 2.95f * Scale),
                  new Vector3(0.16f * Scale, 0.16f * Scale, 0.14f * Scale), CDark);
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < 3; k++)
                    PbBox(t, "Trailer_SideMarker",
                          new Vector3(s * 1.23f * Scale, railTopY - 0.08f * Scale, Tk_ContCZ + (k - 1) * 3.4f * Scale),
                          new Vector3(0.04f * Scale, 0.10f * Scale, 0.18f * Scale), Tk_Amber);

            // ── 후미: 언더런 방호바(다리 2로 프레임에 매달림) + 테일라이트 + 번호판 + 리플렉터 ──
            float rupdY = 0.55f * Scale;               // 방호바 높이(RUPD)
            PbBox(t, "Trailer_RearBar", new Vector3(0f, rupdY, backZ + 0.05f * Scale),
                  new Vector3(2.4f * Scale, 0.16f * Scale, 0.10f * Scale), Tk_Chassis);
            for (int s = -1; s <= 1; s += 2)   // 지지다리: 방호바(rupdY) → 메인빔 속(y=1.32, x±0.50)까지 관통 연결
            {
                float legTopY = 1.32f * Scale;                  // 메인빔 중심(빔 1.19~1.45 안)
                PbBox(t, "Trailer_UnderrunLeg",
                      new Vector3(s * 0.50f * Scale, (rupdY + legTopY) * 0.5f, backZ - 0.03f * Scale),
                      new Vector3(0.09f * Scale, legTopY - rupdY, 0.09f * Scale), Tk_Chassis);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                PbBox(t, "Trailer_TailLight", new Vector3(s * 1.0f * Scale, 0.80f * Scale, backZ + 0.10f * Scale),
                      new Vector3(0.30f * Scale, 0.25f * Scale, 0.05f * Scale), CWarn);
                PbBox(t, "Trailer_TailReflector", new Vector3(s * 1.0f * Scale, 0.62f * Scale, backZ + 0.10f * Scale),
                      new Vector3(0.30f * Scale, 0.06f * Scale, 0.04f * Scale), Tk_RedRefl);   // 후미 적색 반사판(의무)
            }
            PbBox(t, "Trailer_RearPlate", new Vector3(0f, 0.68f * Scale, backZ + 0.11f * Scale),
                  new Vector3(0.46f * Scale, 0.22f * Scale, 0.03f * Scale), Tk_Rim);   // 번호판

            // ── 컨스피큐티 반사테이프(적/백 교호) — FMVSS108/ECE104 의무, 하부 실루엣 밀도 보강 ──
            //   후미 전폭(RUPD 바 앞면 y0.53) 9칸 + 측면 하부(양측 메인 레일 바깥면 y1.22) 8칸.
            int nR = 9; float tapeW = 2.16f * Scale / nR;
            for (int k = 0; k < nR; k++)
                PbBox(t, "Trailer_ConspicRear", new Vector3(-1.08f * Scale + tapeW * (k + 0.5f), 0.53f * Scale, backZ + 0.11f * Scale),
                      new Vector3(tapeW * 0.92f, 0.10f * Scale, 0.01f * Scale), (k % 2 == 0) ? Tk_RedRefl : Tk_WhiteRefl);
            for (int s = -1; s <= 1; s += 2)
            {
                int nS = 8; float segL = (backZ - gooseTopZ) / nS;
                for (int k = 0; k < nS; k++)
                    PbBox(t, "Trailer_ConspicSide", new Vector3(s * 0.605f * Scale, 1.22f * Scale, gooseTopZ + segL * (k + 0.5f)),
                          new Vector3(0.01f * Scale, 0.10f * Scale, segL * 0.9f), (k % 2 == 0) ? Tk_RedRefl : Tk_WhiteRefl);
            }

            // ── 트레일러 3축 보기: 휠 + 서스펜션(행어+에어백) + 브레이크 챔버 ──
            float[] az = { Tk_ContCZ + 3.0f * Scale, Tk_ContCZ + 4.31f * Scale, Tk_ContCZ + 5.62f * Scale };
            float beamBotY = railTopY - 0.26f * Scale;   // 메인 I빔 실측 하단(=1.19), 빔 y 1.19~1.45
            float hangTopY = 1.28f * Scale;              // 행어/에어백 top = 빔 속(부착 보장)
            for (int i = 0; i < az.Length; i++)
            {
                DualAxle(t, "Trailer_Bogie", az[i]);
                // 서스펜션·브레이크: 빔 웹(x±0.465~0.535)에 닿게 x0.46, top을 빔 속(1.28)까지 → 후미 휠군 본체 부착
                for (int s = -1; s <= 1; s += 2)
                {
                    PbBox(t, "Trailer_SuspHanger", new Vector3(s * 0.46f * Scale, (hangTopY + Tk_TireR) * 0.5f, az[i]),
                          new Vector3(0.10f * Scale, hangTopY - Tk_TireR, 0.12f * Scale), Tk_Chassis);
                    Rod(t, "Trailer_AirBag", new Vector3(s * 0.44f * Scale, Tk_TireR + 0.02f * Scale, az[i] + 0.30f * Scale),
                        new Vector3(s * 0.44f * Scale, hangTopY, az[i] + 0.30f * Scale), 0.10f * Scale, CDark);
                    Rod(t, "Trailer_BrakeChamber", new Vector3(s * 0.42f * Scale, Tk_TireR, az[i] - 0.30f * Scale),
                        new Vector3(s * 0.42f * Scale, Tk_TireR, az[i] - 0.50f * Scale), 0.08f * Scale, Tk_Chassis);
                }
            }
            // 곡면 머드가드(좌·우, 3축 통 아치) + 프레임 부착 브래킷(빔 x0.50 ↔ 펜더 x1.0)
            float bogZc = (az[0] + az[2]) * 0.5f, bogZl = az[2] - az[0] + 1.3f * Scale;
            for (int s = -1; s <= 1; s += 2)
            {
                PbBox(t, "Trailer_Fender", new Vector3(s * Tk_HalfTrack, Tk_TireR * 2.15f, bogZc),
                      new Vector3(Tk_TireW * 2.4f, 0.05f * Scale, bogZl), Tk_Chassis);
                for (int b = 0; b < 2; b++)   // 브래킷 2개(빔↔펜더 연결) — top 1.21로 빔(하단 1.19) 속 2cm 물림
                    PbBox(t, "Trailer_FenderBracket",
                          new Vector3(s * 0.75f * Scale, 1.16f * Scale, Mathf.Lerp(az[0], az[2], b)),
                          new Vector3(0.54f * Scale, 0.10f * Scale, 0.10f * Scale), Tk_Chassis);
            }
            // 후축 뒤 머드플랩: 펜더 뒤끝(z=az2+0.65)에 매달아 상단이 펜더(y=1.13)에 접합
            for (int s = -1; s <= 1; s += 2)
                PbBox(t, "Trailer_MudFlap", new Vector3(s * Tk_HalfTrack, 0.59f * Scale, bogZc + bogZl * 0.5f),
                      new Vector3(0.6f * Scale, 1.08f * Scale, 0.04f * Scale), Tk_Tire);

        }

        // ── 휠 헬퍼 ────────────────────────────────────────────────────────
        static void SingleAxle(Transform parent, string name, float z)
        {
            for (int s = -1; s <= 1; s += 2)
                Wheel(parent, name + "_Wheel", new Vector3(s * Tk_HalfTrack, Tk_TireR, z));
            Rod(parent, name + "_Axle", new Vector3(-Tk_HalfTrack, Tk_TireR, z),
                new Vector3(Tk_HalfTrack, Tk_TireR, z), 0.06f * Scale, Tk_Chassis);
        }

        static void DualAxle(Transform parent, string name, float z)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Wheel(parent, name + "_WheelIn",  new Vector3(s * (Tk_HalfTrack - Tk_DualGap), Tk_TireR, z), detail: false);
                Wheel(parent, name + "_WheelOut", new Vector3(s * Tk_HalfTrack, Tk_TireR, z), detail: true);
            }
            Rod(parent, name + "_Axle", new Vector3(-Tk_HalfTrack, Tk_TireR, z),
                new Vector3(Tk_HalfTrack, Tk_TireR, z), 0.07f * Scale, Tk_Chassis);
        }

        // 축(X) 방향으로 눕힌 원통 하나 — 휠 부품 공용. radius=반경, width=폭(축길이).
        static GameObject CylX(Transform parent, string name, Vector3 center, float radius, float width, Color color)
        {
            var cy = NewPrimitive(PrimitiveType.Cylinder, name, parent);
            cy.transform.localPosition = center;
            cy.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            cy.transform.localScale = new Vector3(radius * 2f, width * 0.5f, radius * 2f);
            Colorize(cy, color);
            return cy;
        }

        // 타이어 + 딥디시 스틸 림(비드립·디스크·통풍구·러그·돌출 허브).
        //   ★핵심: 모든 면요소를 타이어 외측면(+0.5W)보다 proud로 X 스태거 → z-fighting/'과녁' 제거, 입체 그림자.
        static void Wheel(Transform parent, string name, Vector3 center, bool detail = true)
        {
            float R = Tk_TireR, W = Tk_TireW;
            float side = Mathf.Sign(center.x == 0f ? 1f : center.x);
            float fx(float f) => center.x + side * W * f;   // W배수 X오프셋(양수=외측 proud)

            // 타이어(검정 고무) + 중앙 트레드 크라운(살짝 큰 반경으로 둥근 고무 실루엣)
            CylX(parent, name, center, R, W, Tk_Tire);
            CylX(parent, name + "_Tread", center, R * 1.02f, W * 0.60f, Tk_Tire);

            if (!detail)   // 안쪽 듀얼 휠: 가려지므로 밝은 디스크만(외측 proud로 z-fight 회피)
            {
                CylX(parent, name + "_Disc", new Vector3(fx(0.52f), center.y, center.z), R * 0.74f, W * 0.12f, Tk_WheelDisc);
                return;
            }

            // 림 비드 립(밝은 폴리시 링, 타이어 가장자리) + 딥디시 디스크 면(밝은 스틸)
            CylX(parent, name + "_BeadLip", new Vector3(fx(0.51f), center.y, center.z), R * 0.82f, W * 0.10f, Tk_Rim);
            CylX(parent, name + "_Disc",    new Vector3(fx(0.53f), center.y, center.z), R * 0.78f, W * 0.14f, Tk_WheelDisc);
            // 통풍구 5개(디스크 위 어두운 리세스, 반경 0.40R)
            for (int j = 0; j < 5; j++)
            {
                float a = j * Mathf.PI * 2f / 5f + 0.3f;
                CylX(parent, name + "_Hole",
                     new Vector3(fx(0.55f), center.y + R * 0.40f * Mathf.Cos(a), center.z + R * 0.40f * Mathf.Sin(a)),
                     R * 0.14f, W * 0.16f, Tk_Tire);
            }
            // 러그 너트 10개(돌출 크롬, 반경 0.55R)
            for (int j = 0; j < 10; j++)
            {
                float a = j * Mathf.PI * 2f / 10f;
                CylX(parent, name + "_Lug",
                     new Vector3(fx(0.58f), center.y + R * 0.55f * Mathf.Cos(a), center.z + R * 0.55f * Mathf.Sin(a)),
                     R * 0.06f, W * 0.22f, Tk_Rim);
            }
            // 허브(돌출 보스 + 크롬 캡 + 어두운 액슬 너트)
            CylX(parent, name + "_HubBoss", new Vector3(fx(0.56f), center.y, center.z), R * 0.30f, W * 0.20f, Tk_WheelDisc);
            CylX(parent, name + "_Hub",     new Vector3(fx(0.62f), center.y, center.z), R * 0.18f, W * 0.16f, Tk_Rim);
            CylX(parent, name + "_HubNut",  new Vector3(fx(0.66f), center.y, center.z), R * 0.09f, W * 0.12f, CDark);
        }

        // ── 데크에 표준 40ft 컨테이너(Container_Procedural_40ft) 적재 ──────
        //   ★ 우리 표준 컨테이너 생성기(VRTestMenu.BuildOne = 분해형 키트 + KitMaterials + 물리/그랩)를
        //     그대로 호출 → 크레인이 다루는 컨테이너와 100% 동일. (임시 단일메시 버전 폐기)
        [MenuItem("Object/트럭/컨테이너 트럭 + 컨테이너 적재", false, 9)]
        public static GameObject CreateTruckWithContainer()
        {
            CreateContainerTruck();
            var root = GameObject.Find("ContainerTruck");
            if (root == null) return null;

            var old = GameObject.Find("Container_Procedural_40ft_OnTruck");
            if (old != null) Object.DestroyImmediate(old);

            // 표준 40ft 컨테이너 — 바닥 피벗, 길이축 = 로컬 X. withReset:false(Play 시 순간이동 방지).
            var c = ContainerProject.EditorTools.VRTestMenu.BuildOne(
                new Color(0.30f, 0.42f, 0.58f), "Container_Procedural_40ft_OnTruck",
                ProceduralContainerMesh.Length40ft, withReset: false);

            c.transform.SetParent(root.transform, worldPositionStays: false);
            // 길이축 X→Z 회전(Euler 0,90,0), 바닥을 데크 상면에 안착
            c.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            c.transform.localPosition = new Vector3(0f, Tk_DeckTopY, Tk_ContCZ);

            Selection.activeGameObject = root;
            return c;
        }

        // ── 에디터에서 PNG 렌더(URP, 에디터 모드) ──────────────────────────
        [MenuItem("Object/트럭/컨테이너 트럭 렌더 → PNG", false, 10)]
        public static void RenderTruckPng()
        {
            CreateContainerTruck();   // 트럭만(컨테이너 미적재)
            var oldCont = GameObject.Find("Container_Procedural_40ft_OnTruck");
            if (oldCont != null) Object.DestroyImmediate(oldCont);   // 남은 적재 컨테이너 제거
            var root = GameObject.Find("ContainerTruck");
            if (root == null) { Debug.LogError("[Truck] root 없음"); return; }

            const int W = 1600, H = 1000;
            // 렌더 동안 트럭을 하늘 높이로 격리 → 씬의 크레인/컨테이너와 완전 분리(가림 없음). 끝나면 원위치.
            Vector3 savedPos = root.transform.position;
            Vector3 isoOrigin = new Vector3(0f, 50f, 0f);
            Vector3 target = isoOrigin + new Vector3(0f, 0.09f, 0.05f);

            GameObject camGo = null, lightGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                root.transform.position = isoOrigin;

                camGo = new GameObject("__TruckShotCam");
                var cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                cam.fieldOfView = 32f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 80f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.72f, 0.85f);  // 하늘색 배경

                lightGo = new GameObject("__TruckShotLight");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.25f;
                light.transform.rotation = Quaternion.Euler(38f, 150f, 0f);

                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                tex = new Texture2D(W, H, TextureFormat.RGB24, false);

                // 사용자도 바로 볼 수 있게 바탕화면 폴더에 저장 + 클로드 확인용 스크래치패드에도 복사.
                string dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "트럭렌더");
                Directory.CreateDirectory(dir);
                string mirror = "/private/tmp/claude-501/-Users-seoyeonsoft-Container/a70aec1c-b7b6-4e16-9ff1-5f58abd065d6/scratchpad";
                Directory.CreateDirectory(mirror);

                // 3개 앵글: 3/4 정면-좌, 측면, 3/4 후면-우 (트럭 로컬 오프셋)
                (string tag, Vector3 off)[] shots =
                {
                    ("front34", new Vector3(-0.62f, 0.34f, -0.70f)),
                    ("side",    new Vector3(-0.95f, 0.18f,  0.05f)),
                    ("rear34",  new Vector3( 0.62f, 0.34f,  0.78f)),
                };

                foreach (var (tag, off) in shots)
                {
                    Vector3 pos = isoOrigin + off;
                    camGo.transform.position = pos;
                    camGo.transform.rotation = Quaternion.LookRotation((target - pos).normalized, Vector3.up);

                    var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req))
                        RenderPipeline.SubmitRenderRequest(cam, req);
                    else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    var png = tex.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(dir, "truck_" + tag + ".png"), png);
                    File.WriteAllBytes(Path.Combine(mirror, "truck_" + tag + ".png"), png);
                }
                Debug.Log("[Truck] 렌더 완료 → " + dir + "/truck_front34.png, truck_side.png, truck_rear34.png");
            }
            finally
            {
                if (root != null) root.transform.position = savedPos;   // 원위치 복원
                if (camGo != null)   Object.DestroyImmediate(camGo);
                if (lightGo != null) Object.DestroyImmediate(lightGo);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }
    }
}
