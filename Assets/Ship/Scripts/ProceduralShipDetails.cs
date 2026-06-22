using UnityEngine;
using Procedural;
using ContainerProject;   // ProceduralContainerMesh.Length40ft (베이 피치)

namespace Container.Ship
{
    /// <summary>
    /// 컨테이너선 디테일 — 패스 1(라싱 브리지 + 마스트). 레퍼런스의 수직 실루엣 정의 요소.
    ///   · 라싱 브리지 : 베이 사이마다 횡방향 강프레임(측주 2 + 상·중 빔 + 워크웨이). 컨테이너 고박부.
    ///   · 포어마스트  : 선수루 위 수직 마스트 + 야드 + 플랫폼 + 항해등.
    ///   · 메인마스트  : 휠하우스 위 마스트 + 레이더 플랫폼 + 야드 + 마스트헤드등.
    /// 위치/높이는 선체곡선·레이아웃 SSOT에서 읽음. 좌표/스케일은 선체와 동일(실척→1/24).
    /// 서브메시: 0=강구조(마스트·프레임), 1=항해등(발광).
    /// </summary>
    public static class ProceduralShipDetails
    {
        const float Scale = ShipConfig.ModelScale;

        static void Box(MeshBuilder mb, int sub, Vector3 c, Vector3 s)
            => mb.AddBox(sub, c * Scale, s * Scale);

        static float CargoHalfW =>
            (ShipConfig.DeckRows * ShipConfig.ContainerWidthM + (ShipConfig.DeckRows - 1) * ShipConfig.RowGapM) * 0.5f;

        public const int SubSteel = 0;
        public const int SubLight = 1;
        public const int SubDark  = 2;   // 앵커·드럼·계선주·방향타 캐스트강(차콜)
        public const int SubBoat  = 3;   // 구명정(오렌지)
        public const int SubProp  = 4;   // 프로펠러(청동)

        public static Mesh BuildDetails()
        {
            var mb = new MeshBuilder();
            BuildLashingBridges(mb);
            BuildMasts(mb);
            BuildGroundTackle(mb);   // 양묘기·호스 앵커
            BuildMooring(mb);        // 계선주(볼라드)
            BuildRailings(mb);       // 측면 난간
            BuildStern(mb);          // 선미: 계선윈치·자유낙하 구명정·난간·깃대
            BuildPropulsion(mb);     // 추진·조향: 프로펠러 + 방향타
            return mb.ToMesh("ContainerShip_Details");
        }

        // ── 추진·조향(선미 수면 아래): 프로펠러(샤프트+허브+블레이드) + 방향타(스톡+블레이드) ──
        static void BuildPropulsion(MeshBuilder mb)
        {
            float zHub = -ShipConfig.HalfLoa + 4f;     // 허브 z (트랜섬 앞 4m)
            const float yAx = -6.5f;                   // 축 깊이(흘수선 아래)
            Vector3 hub = new Vector3(0f, yAx, zHub);

            Cyl(mb, SubProp, new Vector3(0f, yAx, zHub + 3f), 0.5f, 3f, 14, 2);    // 샤프트(선체→허브, Z축)
            Cyl(mb, SubProp, hub, 0.9f, 1.3f, 16, 2);                              // 허브
            Cyl(mb, SubProp, new Vector3(0f, yAx, zHub - 1.5f), 0.55f, 0.5f, 14, 2); // 허브 콘(뒤)

            const int NBlade = 4;
            const float hubR = 0.9f, bladeLen = 3.4f, chord = 2.1f, thick = 0.28f;
            float fr = 28f * Mathf.Deg2Rad;            // 블레이드 피치 트위스트
            for (int i = 0; i < NBlade; i++)
            {
                float th = TAU * i / NBlade;
                Vector3 radial = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0f);
                Vector3 tang = new Vector3(-Mathf.Sin(th), Mathf.Cos(th), 0f);
                Vector3 ay = (tang * Mathf.Cos(fr) + Vector3.forward * Mathf.Sin(fr)).normalized;
                Vector3 az = Vector3.Cross(radial, ay).normalized;
                Vector3 c = hub + radial * (hubR + bladeLen * 0.5f);
                BoxAxes(mb, SubProp, c, new Vector3(bladeLen * 0.5f, chord * 0.5f, thick * 0.5f), radial, ay, az);
            }

            // 방향타(rudder) — 프로펠러 뒤, 스톡(수직) + 블레이드
            float zR = -ShipConfig.HalfLoa - 1f;
            Cyl(mb, SubDark, new Vector3(0f, yAx - 1f, -ShipConfig.HalfLoa + 1f), 0.45f, 3.2f, 14, 1); // 러더 스톡
            Box(mb, SubDark, new Vector3(0f, -6f, zR), new Vector3(0.8f, 8.5f, 4.2f));                 // 러더 블레이드
        }

        // 임의 직교축(ax,ay,az) 정렬 박스 — 프로펠러 블레이드 등 비축정렬용(실척)
        static void BoxAxes(MeshBuilder mb, int sub, Vector3 c, Vector3 h, Vector3 ax, Vector3 ay, Vector3 az)
        {
            Vector3 X = ax * h.x, Y = ay * h.y, Z = az * h.z;
            Vector3 p000 = c - X - Y - Z, p100 = c + X - Y - Z, p110 = c + X + Y - Z, p010 = c - X + Y - Z;
            Vector3 p001 = c - X - Y + Z, p101 = c + X - Y + Z, p111 = c + X + Y + Z, p011 = c - X + Y + Z;
            FaceMW(mb, sub, p001, p101, p111, p011,  az);
            FaceMW(mb, sub, p100, p000, p010, p110, -az);
            FaceMW(mb, sub, p101, p100, p110, p111,  ax);
            FaceMW(mb, sub, p000, p001, p011, p010, -ax);
            FaceMW(mb, sub, p011, p111, p110, p010,  ay);
            FaceMW(mb, sub, p000, p100, p101, p001, -ay);
        }

        // ── 라싱 브리지 — 베이 사이 횡프레임(2티어) ──
        static void BuildLashingBridges(MeshBuilder mb)
        {
            float cargoLen = ShipConfig.CargoFwdZ - ShipConfig.CargoAftZ;
            float pitch = cargoLen / Mathf.Max(1, Mathf.RoundToInt(cargoLen / (ProceduralContainerMesh.Length40ft + 2f)));
            int N = Mathf.RoundToInt(cargoLen / pitch);

            const float H = 5.8f, post = 0.5f, bm = 0.4f;
            for (int i = 0; i < N - 1; i++)        // 해치 i ↔ i+1 사이 간극
            {
                float z = ShipConfig.CargoAftZ + pitch * (i + 1);
                float halfW = Mathf.Min(CargoHalfW, ProceduralShipHull.HalfBeam(z) - ShipConfig.SideDeckM);
                if (halfW < 1f) continue;
                float dY = ProceduralShipHull.DeckY(z);

                Box(mb, SubSteel, new Vector3( halfW, dY + H * 0.5f, z), new Vector3(post, H, post)); // 우측주
                Box(mb, SubSteel, new Vector3(-halfW, dY + H * 0.5f, z), new Vector3(post, H, post)); // 좌측주
                // 가로 빔은 측주 중심이 아니라 '바깥면'까지(+post = 각 끝 +post/2) 가로질러 덮어 빈틈없는 솔리드 코너
                Box(mb, SubSteel, new Vector3(0f, dY + H,        z), new Vector3(halfW * 2f + post, bm, bm));          // 상부빔
                Box(mb, SubSteel, new Vector3(0f, dY + H * 0.5f, z), new Vector3(halfW * 2f + post, bm * 0.7f, bm * 0.7f)); // 중간빔
                Box(mb, SubSteel, new Vector3(0f, dY + H * 0.5f + 0.25f, z), new Vector3(halfW * 2f + post, 0.1f, 0.4f)); // 워크웨이
            }
        }

        // ── 마스트(포어 + 메인) ──
        static void BuildMasts(MeshBuilder mb)
        {
            // 포어마스트 — 선수루 위
            {
                float z = ShipConfig.ForecastleAftZ + 4f;
                float baseY = ProceduralShipHull.DeckY(z) + ShipConfig.ForecastleRaiseM;
                const float h = 12f, sec = 0.6f;
                Cyl(mb, SubSteel, new Vector3(0f, baseY + h * 0.5f,  z), sec * 0.5f, h * 0.5f, 20, 1); // 마스트(원통)
                Box(mb, SubSteel, new Vector3(0f, baseY + h * 0.55f, z), new Vector3(2.2f, 0.2f, 2.2f)); // 플랫폼
                Cyl(mb, SubSteel, new Vector3(0f, baseY + h * 0.62f, z), 0.15f, 3f, 6, 0); // 야드(원통)
                Box(mb, SubLight, new Vector3(0f, baseY + h + 0.4f,  z), new Vector3(0.5f, 0.8f, 0.5f)); // 항해등
            }
            // 메인마스트 — 휠하우스 위(몽키 아일랜드)
            {
                float zc = (ShipConfig.AccomFrontZ + ShipConfig.AccomAftZ) * 0.5f;
                // 브리지 처마 톱(=주갑판 위 ~30.4m) — BuildAccommodation과 정합(브리지27 + 본체3 + 처마0.36)
                float houseTop = ProceduralShipHull.DeckY(zc)
                               + ShipConfig.AccomDecks * ShipConfig.DeckHeightM + ShipConfig.DeckHeightM + 0.4f;
                const float h = 10f, sec = 0.7f;
                Cyl(mb, SubSteel, new Vector3(0f, houseTop + h * 0.5f,  zc), sec * 0.5f, h * 0.5f, 20, 1); // 마스트(원통)
                Box(mb, SubSteel, new Vector3(0f, houseTop + h * 0.45f, zc), new Vector3(4f, 0.25f, 4f));  // 레이더 플랫폼
                Cyl(mb, SubSteel, new Vector3(0f, houseTop + h * 0.70f, zc), 0.15f, 3.5f, 6, 0); // 야드(원통)
                Box(mb, SubLight, new Vector3(0f, houseTop + h + 0.4f,  zc), new Vector3(0.6f, 0.9f, 0.6f)); // 마스트헤드등
            }
        }

        // ── 양묘 장비(선수루): 윈들러스 2드럼 + 호스파이프에 물린 앵커 ──
        static void BuildGroundTackle(MeshBuilder mb)
        {
            float zw = ShipConfig.ForecastleAftZ + 3f;                  // 위치=선수루 SSOT 기준
            float baseY = ProceduralShipHull.DeckY(zw) + ShipConfig.ForecastleRaiseM;
            float wOff = ProceduralShipHull.HalfBeam(zw) * 0.35f;       // 측방 오프셋 = 선폭에서 산출
            for (int s = -1; s <= 1; s += 2)
            {
                Winch(mb, new Vector3(s * wOff, baseY, zw));                                            // 윈들러스(윈치형)
                Box(mb, SubDark, new Vector3(s * wOff, baseY + 0.4f, zw + 2.6f), new Vector3(0.8f, 0.8f, 1.2f)); // 체인 스토퍼
            }
            // 호스파이프에 물린 앵커 = 선수루~선수 중간 위치 산출
            float za = Mathf.Lerp(ShipConfig.ForecastleAftZ, ShipConfig.HalfLoa, 0.5f);
            float hw = ProceduralShipHull.HalfBeam(za);
            float dEdge = ProceduralShipHull.DeckY(za) + ShipConfig.ForecastleRaiseM;
            for (int s = -1; s <= 1; s += 2)
            {
                Box(mb, SubDark, new Vector3(s * (hw - 0.5f), dEdge + 0.3f, za), new Vector3(1.2f, 0.6f, 1.4f)); // 호스파이프 개구
                Box(mb, SubDark, new Vector3(s * (hw - 0.1f), dEdge - 1.6f, za), new Vector3(0.35f, 2.4f, 2.6f)); // 앵커(현측에 매입)
            }
        }

        // ── 계선주(볼라드) — 화물구 양현, 개수는 길이에서 산출(상수 박지 않음) ──
        static void BuildMooring(MeshBuilder mb)
        {
            float zA = ShipConfig.CargoAftZ, zB = ShipConfig.CargoFwdZ;
            int nB = Mathf.Max(2, Mathf.RoundToInt((zB - zA) / 55f));   // 개수 = 화물구 길이/간격(≈55m) 산출
            for (int i = 0; i <= nB; i++)
            {
                float z = Mathf.Lerp(zA + 6f, zB - 6f, (float)i / nB);
                float hw = ProceduralShipHull.HalfBeam(z) - 0.8f;   // 사이드데크 안, 난간선(−0.15) 안쪽: 외측 −0.35
                float dY = ProceduralShipHull.DeckY(z);
                for (int s = -1; s <= 1; s += 2) Bollard(mb, new Vector3(s * hw, dY, z));
            }
        }

        // 계선/양묘 윈치 1기 — 베이스 + 모터 하우징 + 플랜지 드럼 + 워핑헤드(드럼축=X, 알아보게).
        static void Winch(MeshBuilder mb, Vector3 at)
        {
            Box(mb, SubSteel, at + new Vector3(0f, 0.35f, 0f), new Vector3(5.2f, 0.7f, 2.8f));   // 베이스 시트
            Box(mb, SubSteel, at + new Vector3(-2.0f, 1.6f, 0f), new Vector3(1.4f, 2.0f, 2.4f)); // 모터/기어 하우징(한쪽)
            Cyl(mb, SubDark, at + new Vector3( 0.4f, 1.5f, 0f), 0.7f, 1.5f, 16, 0);              // 로프 드럼
            Cyl(mb, SubDark, at + new Vector3(-1.1f, 1.5f, 0f), 1.05f, 0.18f, 16, 0);            // 안쪽 플랜지
            Cyl(mb, SubDark, at + new Vector3( 1.9f, 1.5f, 0f), 1.05f, 0.18f, 16, 0);            // 바깥 플랜지
            Cyl(mb, SubDark, at + new Vector3( 2.5f, 1.5f, 0f), 0.55f, 0.45f, 14, 0);            // 워핑 헤드
        }

        // 계선주 1기(베이스 + 비트 2주 + 머리캡) — 선수/선미 공통 재사용.
        // 측방 반폭 ≤ 0.45m(사이드데크 1.2m·난간선 안쪽에 들어가게 축소 — 통과 방지).
        static void Bollard(MeshBuilder mb, Vector3 at)
        {
            Box(mb, SubDark, at + new Vector3(0f, 0.3f, 0f), new Vector3(0.9f, 0.6f, 2.4f)); // 베이스(반폭 0.45)
            for (int k = -1; k <= 1; k += 2)
            {
                Cyl(mb, SubDark, at + new Vector3(0f, 1.0f, k * 0.6f), 0.3f, 0.8f, 16, 1);    // 비트
                Cyl(mb, SubDark, at + new Vector3(0f, 1.7f, k * 0.6f), 0.38f, 0.12f, 16, 1);  // 머리캡
            }
        }

        // ── 측면 난간(화물구 양현): 곡면 갑판 가장자리 따라 스탠션(원통)+레일(이은 막대) ──
        static void BuildRailings(MeshBuilder mb)
        {
            // 선미 트랜섬~화물구 전단까지 양현 '연속' 1줄 — 거주구·펀넬 옆 빈 구간 없게(선수루는 불워크가 대신)
            RailRun(mb, -ShipConfig.HalfLoa + 1f, ShipConfig.CargoFwdZ);
        }

        // z구간 양현 난간 — 스탠션은 Cyl, 상·중 레일은 이웃 스탠션을 Bar로 연결(곡선 정합, 떨어짐 방지)
        static void RailRun(MeshBuilder mb, float zA, float zB)
        {
            const float railH = 1.1f, step = 6f;
            int n = Mathf.Max(1, Mathf.RoundToInt((zB - zA) / step));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 prevTop = Vector3.zero, prevMid = Vector3.zero; bool has = false;
                for (int i = 0; i <= n; i++)
                {
                    float z = Mathf.Lerp(zA, zB, (float)i / n);
                    float hw = ProceduralShipHull.HalfBeam(z) - 0.15f, dY = ProceduralShipHull.DeckY(z);
                    Cyl(mb, SubSteel, new Vector3(s * hw, dY + railH * 0.5f, z), 0.07f, railH * 0.5f, 6, 1); // 스탠션
                    Vector3 top = new Vector3(s * hw, dY + railH, z), mid = new Vector3(s * hw, dY + railH * 0.6f, z);
                    if (has) { Bar(mb, SubSteel, prevTop, top, 0.1f); Bar(mb, SubSteel, prevMid, mid, 0.1f); }
                    prevTop = top; prevMid = mid; has = true;
                }
            }
        }

        // ── 선미(푸프) 디테일: 계선 윈치·볼라드 + 자유낙하 구명정 + 깃대 + 난간 ──
        static void BuildStern(MeshBuilder mb)
        {
            // 계선 윈치(양현) — 펀넬 후방 푸프(위치=FunnelZ SSOT, 오프셋=선폭 산출)
            {
                float z = ShipConfig.FunnelZ - 10f, dY = ProceduralShipHull.DeckY(z);
                float wOff = ProceduralShipHull.HalfBeam(z) * 0.5f;
                for (int s = -1; s <= 1; s += 2) Winch(mb, new Vector3(s * wOff, dY, z));
            }
            // 볼라드(선미) — 펀넬 후방 + 트랜섬 근처, 위치 산출(상수 박지 않음)
            foreach (float z in new[] { ShipConfig.FunnelZ - 13f, -ShipConfig.HalfLoa + 4f })
            {
                float hw = ProceduralShipHull.HalfBeam(z) - 0.8f, dY = ProceduralShipHull.DeckY(z); // 난간선 안쪽
                for (int s = -1; s <= 1; s += 2) Bollard(mb, new Vector3(s * hw, dY, z));
            }
            // 자유낙하 구명정(선미 중앙, 후방-하향 경사) — 매끈한 밀폐 캡슐 + 다빗 램프
            {
                float z = -ShipConfig.HalfLoa + 9f, dY = ProceduralShipHull.DeckY(z); const float pitch = 24f;
                BoxRot(mb, SubSteel, new Vector3(-1.6f, dY + 1.6f, z), new Vector3(0.4f, 0.4f, 10f), pitch);  // 램프 빔
                BoxRot(mb, SubSteel, new Vector3( 1.6f, dY + 1.6f, z), new Vector3(0.4f, 0.4f, 10f), pitch);
                BuildLifeboat(mb, new Vector3(0f, dY + 2.8f, z), pitch);                                      // 캡슐 본체
            }
            // 깃대 + 선미등(트랜섬 근처)
            {
                float z = -ShipConfig.HalfLoa + 2f, dY = ProceduralShipHull.DeckY(z); // 트랜섬 근처 산출
                Cyl(mb, SubSteel, new Vector3(0f, dY + 3f, z), 0.15f, 3f, 6, 1);       // 깃대(원통)
                Box(mb, SubLight, new Vector3(0f, dY + 6.2f, z), new Vector3(0.4f, 0.6f, 0.4f));
            }
            // 푸프 난간(양현 RailRun + 선미 횡단)
            {
                // 양현 난간은 BuildRailings가 선미까지 연속 처리 → 여기선 선미 횡단(트랜섬)만.
                // 측면과 동일하게 상·중 2줄 + 스탠션 개수=폭/간격 산출(양끝이 측면난간 코너와 일치).
                const float railH = 1.1f;
                float zT = -ShipConfig.HalfLoa + 1f, hwT = ProceduralShipHull.HalfBeam(zT) - 0.15f, dT = ProceduralShipHull.DeckY(zT);
                int nt = Mathf.Max(2, Mathf.RoundToInt(hwT * 2f / 3f));
                Vector3 prevTop = Vector3.zero, prevMid = Vector3.zero; bool has = false;
                for (int i = 0; i <= nt; i++)
                {
                    float x = Mathf.Lerp(-hwT, hwT, (float)i / nt);
                    Cyl(mb, SubSteel, new Vector3(x, dT + railH * 0.5f, zT), 0.07f, railH * 0.5f, 12, 1);
                    Vector3 top = new Vector3(x, dT + railH, zT), mid = new Vector3(x, dT + railH * 0.6f, zT);
                    if (has) { Bar(mb, SubSteel, prevTop, top, 0.1f); Bar(mb, SubSteel, prevMid, mid, 0.1f); }
                    prevTop = top; prevMid = mid; has = true;
                }
            }
        }

        // 자유낙하 구명정 — 매끈한 밀폐 캡슐(둥근 단면 로프트, 뾰족 선수). c=중심, pitchDeg=후방하향 경사.
        static void BuildLifeboat(MeshBuilder mb, Vector3 c, float pitchDeg)
        {
            const float L = 8.6f, hwMax = 1.55f, hhMax = 1.75f;   // 길이 8.6·폭 3.1·높이 3.5m
            const int NS = 18, RS = 14;
            float a = pitchDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            Vector3 P(float x, float y, float z) => c + new Vector3(x, y * ca - z * sa, y * sa + z * ca); // X축 회전+위치(실척)
            float S(float t)   // 단면 스케일: 후미 라운드 → 본체 풀 → 선수 뾰족
            {
                if (t < 0.15f) return Mathf.Lerp(0.55f, 1f, t / 0.15f);
                if (t < 0.62f) return 1f;
                float u = (t - 0.62f) / 0.38f; return Mathf.Lerp(1f, 0.12f, Mathf.Sqrt(u));
            }
            var ring = new Vector3[NS + 1][];
            var cen = new Vector3[NS + 1];
            for (int k = 0; k <= NS; k++)
            {
                float t = (float)k / NS, zc = (t - 0.5f) * L, w = hwMax * S(t), h = hhMax * S(t);
                cen[k] = P(0f, 0f, zc);
                ring[k] = new Vector3[RS];
                for (int j = 0; j < RS; j++)
                {
                    float th = TAU * j / RS;
                    ring[k][j] = P(w * Mathf.Cos(th), h * Mathf.Sin(th), zc);
                }
            }
            for (int k = 0; k < NS; k++)
                for (int j = 0; j < RS; j++)
                {
                    int j2 = (j + 1) % RS;
                    Vector3 a0 = ring[k][j], b0 = ring[k][j2], b1 = ring[k + 1][j2], a1 = ring[k + 1][j];
                    Vector3 want = (a0 + b0 + b1 + a1) * 0.25f - (cen[k] + cen[k + 1]) * 0.5f;
                    FaceMW(mb, SubBoat, a0, b0, b1, a1, want);
                }
            for (int j = 0; j < RS; j++)   // 양끝 캡
            {
                int j2 = (j + 1) % RS;
                TriMW(mb, SubBoat, cen[0], ring[0][j2], ring[0][j], cen[0] - cen[1]);
                TriMW(mb, SubBoat, cen[NS], ring[NS][j], ring[NS][j2], cen[NS] - cen[NS - 1]);
            }
        }

        // X축 피치 회전 박스(구명정·경사 램프용) — 로컬 박스를 회전 후 ModelScale 적용
        static readonly Vector2[] BV = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        static void BoxRot(MeshBuilder mb, int sub, Vector3 c, Vector3 size, float pitchDeg)
        {
            float a = pitchDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z)
            {
                Vector3 r = new Vector3(x, y * ca - z * sa, y * sa + z * ca);   // X축 회전
                return (c + r) * Scale;
            }
            Vector3 p000 = P(-h.x, -h.y, -h.z), p100 = P(h.x, -h.y, -h.z), p110 = P(h.x, h.y, -h.z), p010 = P(-h.x, h.y, -h.z);
            Vector3 p001 = P(-h.x, -h.y, h.z), p101 = P(h.x, -h.y, h.z), p111 = P(h.x, h.y, h.z), p011 = P(-h.x, h.y, h.z);
            Face(mb, sub, p001, p101, p111, p011);
            Face(mb, sub, p100, p000, p010, p110);
            Face(mb, sub, p101, p100, p110, p111);
            Face(mb, sub, p000, p001, p011, p010);
            Face(mb, sub, p011, p111, p110, p010);
            Face(mb, sub, p000, p100, p101, p001);
        }
        static void Face(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int ia = mb.AddVertex(a, n, BV[0]), ib = mb.AddVertex(b, n, BV[1]);
            int ic = mb.AddVertex(c, n, BV[2]), id = mb.AddVertex(d, n, BV[3]);
            mb.AddQuad(sub, ia, ib, ic, id);
        }

        // ── 비-큐브 프리미티브 (실척 m 입력, 내부에서 ModelScale 적용) ──
        const float TAU = 6.28318530718f;

        // 외향(want) 보장 쿼드/삼각 (실척)
        static void FaceMW(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, want) < 0f) { var t = b; b = d; d = t; n = -n; }
            n = n.sqrMagnitude > 1e-9f ? n.normalized : want.normalized;
            int ia = mb.AddVertex(a * Scale, n, BV[0]), ib = mb.AddVertex(b * Scale, n, BV[1]);
            int ic = mb.AddVertex(c * Scale, n, BV[2]), id = mb.AddVertex(d * Scale, n, BV[3]);
            mb.AddQuad(sub, ia, ib, ic, id);
        }
        static void TriMW(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 want)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, want) < 0f) { var t = b; b = c; c = t; n = -n; }
            n = n.sqrMagnitude > 1e-9f ? n.normalized : want.normalized;
            int ia = mb.AddVertex(a * Scale, n, BV[0]), ib = mb.AddVertex(b * Scale, n, BV[1]), ic = mb.AddVertex(c * Scale, n, BV[2]);
            mb.AddTriangle(sub, ia, ib, ic);
        }

        // N각 기둥(원통 근사). axis 0=X,1=Y,2=Z. center=중심, halfLen=축방향 반길이.
        static void Cyl(MeshBuilder mb, int sub, Vector3 center, float r, float halfLen, int sides, int axis)
        {
            sides = Mathf.Max(sides, 14);   // 각진 원기둥 방지 — 최소 14면(둥글게 보이게)
            Vector3 ax = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 u = axis == 1 ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(ax, u).normalized;
            Vector3 c0 = center - ax * halfLen, c1 = center + ax * halfLen;
            for (int i = 0; i < sides; i++)
            {
                float a0 = TAU * i / sides, a1 = TAU * (i + 1) / sides;
                Vector3 d0 = (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * r;
                Vector3 d1 = (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * r;
                Vector3 p00 = c0 + d0, p01 = c0 + d1, p10 = c1 + d0, p11 = c1 + d1;
                FaceMW(mb, sub, p00, p10, p11, p01, (d0 + d1) * 0.5f); // 옆면
                TriMW(mb, sub, c1, p10, p11, ax);                      // 위 캡
                TriMW(mb, sub, c0, p00, p01, -ax);                     // 아래 캡
            }
        }

        // 두 점을 잇는 얇은 사각 막대(난간 레일 등 — 곡면 따라가게). 실척.
        static void Bar(MeshBuilder mb, int sub, Vector3 p0, Vector3 p1, float thick)
        {
            Vector3 dir = p1 - p0; float len = dir.magnitude;
            if (len < 1e-4f) return; dir /= len;
            Vector3 up = Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 rt = Vector3.Cross(dir, up).normalized * (thick * 0.5f);
            Vector3 uv = Vector3.Cross(rt, dir).normalized * (thick * 0.5f);
            Vector3 a = p0 - rt - uv, b = p0 + rt - uv, c = p0 + rt + uv, d = p0 - rt + uv;
            Vector3 e = p1 - rt - uv, f = p1 + rt - uv, g = p1 + rt + uv, h = p1 - rt + uv;
            FaceMW(mb, sub, a, b, f, e, -uv); FaceMW(mb, sub, c, d, h, g, uv);
            FaceMW(mb, sub, b, c, g, f, rt);  FaceMW(mb, sub, d, a, e, h, -rt);
            FaceMW(mb, sub, a, d, c, b, -dir); FaceMW(mb, sub, e, f, g, h, dir);
        }
    }
}
