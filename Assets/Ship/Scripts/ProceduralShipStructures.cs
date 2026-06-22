using System.Collections.Generic;
using UnityEngine;
using Procedural;
using ContainerProject;   // ProceduralContainerMesh.Length40ft

namespace Container.Ship
{
    /// <summary>
    /// 컨테이너선 상부구조 절차 생성 — 2~4단계(솔리드 마스, 미세 디테일은 6단계).
    ///   · BuildHatches      : 화물구 해치(코밍+커버) 격자 — 컨테이너가 안착할 면
    ///   · BuildForecastle   : 선수루(상승 갑판) + 현측벽 + 방파판(breakwater)
    ///   · BuildAccommodation: 거주구 타워 + 항해선교(브리지 윙) + 펀넬
    ///
    /// 폭·갑판높이는 선체 곡선(ProceduralShipHull.HalfBeam/DeckY)을 직접 읽어 선형에 정합.
    /// 좌표/스케일은 선체와 동일(실척 m → ModelScale 1/24). 각 파트는 자체 서브메시(머티리얼)를 가짐.
    /// </summary>
    public static class ProceduralShipStructures
    {
        const float Scale = ShipConfig.ModelScale;
        static readonly float Len40 = ProceduralContainerMesh.Length40ft;   // 12.192m — 베이 길이 기준

        static void Box(MeshBuilder mb, int sub, Vector3 center, Vector3 size)
            => mb.AddBox(sub, center * Scale, size * Scale);

        static readonly Vector2[] QUV =
            { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };

        static Vector3 Mir(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        // 외향(want) 보장 로프트 쿼드 — 노멀이 want 반대면 감김 반전
        static void Quad(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, want) < 0f) { var t = b; b = d; d = t; n = -n; }
            n = n.sqrMagnitude > 1e-9f ? n.normalized : want;
            int ia = mb.AddVertex(a * Scale, n, QUV[0]);
            int ib = mb.AddVertex(b * Scale, n, QUV[1]);
            int ic = mb.AddVertex(c * Scale, n, QUV[2]);
            int id = mb.AddVertex(d * Scale, n, QUV[3]);
            mb.AddQuad(sub, ia, ib, ic, id);
        }

        static void TriS(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 want)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, want) < 0f) { var t = b; b = c; c = t; n = -n; }
            n = n.sqrMagnitude > 1e-9f ? n.normalized : want;
            int ia = mb.AddVertex(a * Scale, n, QUV[0]), ib = mb.AddVertex(b * Scale, n, QUV[1]), ic = mb.AddVertex(c * Scale, n, QUV[2]);
            mb.AddTriangle(sub, ia, ib, ic);
        }

        // 수직 원기둥(배기관 등) — base에서 위로 h, 최소 14면(둥글게)
        static void CylV(MeshBuilder mb, int sub, Vector3 baseC, float r, float h, int sides)
        {
            sides = Mathf.Max(sides, 14);
            Vector3 top = baseC + Vector3.up * h, bot = baseC;
            for (int i = 0; i < sides; i++)
            {
                float a0 = Mathf.PI * 2f * i / sides, a1 = Mathf.PI * 2f * (i + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                Vector3 d1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                Quad(mb, sub, bot + d0, top + d0, top + d1, bot + d1, d0 + d1);   // 옆면
                TriS(mb, sub, top, top + d0, top + d1, Vector3.up);               // 윗 캡
                TriS(mb, sub, bot, bot + d1, bot + d0, Vector3.down);             // 아래 캡
            }
        }

        // ── 화물 컨테이너 블록 반폭(15열 + 라싱간격)의 절반 ──
        static float CargoHalfW =>
            (ShipConfig.DeckRows * ShipConfig.ContainerWidthM + (ShipConfig.DeckRows - 1) * ShipConfig.RowGapM) * 0.5f;

        // ── 2단계: 해치(코밍 + 커버) ───────────────────────────────────────────
        // 서브메시: 0=코밍, 1=커버
        public static Mesh BuildHatches()
        {
            var mb = new MeshBuilder();

            float Wc = ShipConfig.ContainerWidthM;                // 컨테이너 폭(입력값)
            float cargoLen = ShipConfig.CargoFwdZ - ShipConfig.CargoAftZ;
            int bays = Mathf.Max(1, Mathf.RoundToInt(cargoLen / (Len40 + 2f)));  // 베이 개수 = 길이/피치 산출
            float pitch = cargoLen / bays;
            float coamLen = Len40 + 0.5f;                         // 해치 전후 길이(40ft + 코밍 여유)
            const float coamH = 1.8f, coverH = 0.35f, wall = 0.25f;

            for (int i = 0; i < bays; i++)
            {
                float zc = ShipConfig.CargoAftZ + pitch * (i + 0.5f);
                float dY = ProceduralShipHull.DeckY(zc);
                // 베이는 길이(coamLen)를 차지하므로 선체가 좁아지는 '양끝 중 좁은 쪽'(hbMin)으로 폭 산출 →
                //   브래킷까지 난간선(HalfBeam-0.15) 안쪽 유지(통과 방지). 가용폭 → 컨테이너 '열수' 산출.
                float hbMin = Mathf.Min(ProceduralShipHull.HalfBeam(zc - coamLen * 0.5f),
                                        ProceduralShipHull.HalfBeam(zc + coamLen * 0.5f));
                float avail = 2f * (hbMin - ShipConfig.SideDeckM);
                int rows = Mathf.Clamp(Mathf.FloorToInt(avail / Wc), 1, ShipConfig.DeckRows);
                float halfW = rows * Wc * 0.5f;
                float cy = dY + coamH * 0.5f - 0.075f;            // 갑판에 0.15 박힘
                float ch = coamH + 0.15f;

                // 코밍 림(4벽) — 솔리드 박스 폐기, 실제 코밍처럼 테두리만
                Box(mb, 0, new Vector3(0f, cy, zc + coamLen * 0.5f), new Vector3(halfW * 2f + wall, ch, wall)); // 앞
                Box(mb, 0, new Vector3(0f, cy, zc - coamLen * 0.5f), new Vector3(halfW * 2f + wall, ch, wall)); // 뒤
                Box(mb, 0, new Vector3( halfW + wall * 0.5f, cy, zc), new Vector3(wall, ch, coamLen));           // 우
                Box(mb, 0, new Vector3(-halfW - wall * 0.5f, cy, zc), new Vector3(wall, ch, coamLen));           // 좌
                // 코밍 외측 보강 브래킷 — 개수 = 코밍 길이/간격 산출
                int brk = Mathf.Max(2, Mathf.RoundToInt(coamLen / 3f));
                for (int b = 0; b <= brk; b++)
                {
                    float bz = zc - coamLen * 0.5f + coamLen * b / brk;
                    Box(mb, 0, new Vector3( halfW + wall, dY + coamH * 0.4f, bz), new Vector3(0.3f, coamH * 0.8f, 0.18f));
                    Box(mb, 0, new Vector3(-halfW - wall, dY + coamH * 0.4f, bz), new Vector3(0.3f, coamH * 0.8f, 0.18f));
                }
                // 해치 커버 패널(평탄 = 컨테이너 안착면), 코밍 위
                Box(mb, 1, new Vector3(0f, dY + coamH + coverH * 0.5f, zc),
                           new Vector3(halfW * 2f + wall, coverH, coamLen + wall));
            }
            return mb.ToMesh("ContainerShip_Hatches");
        }

        // ── 5단계: 갑판 컨테이너 적재 — 해치 커버 위 베이×로우×티어 ──
        // 레이아웃은 BuildHatches와 동일(동일 베이/로우 산출) → 커버 위에 정확히 안착.
        // 서브메시 0~7 = 컨테이너 색 팔레트(패치워크). 가장자리 낮은 크라운 프로파일·베이별 변주.
        public const int CargoPalette = 8;
        public const float CargoTierH = 2.591f;   // 컨테이너 적층 피치(Std, 실척 m)

        // 갑판 컨테이너 슬롯 — xyz=컨테이너 중심(실척 m), w=색 인덱스(0~7).
        // 레이아웃은 BuildHatches와 동일 산출 → 커버 위에 정확히 안착. 적재용 메뉴가 이 좌표에 그랩 컨테이너를 배치.
        public static List<Vector4> CargoSlots()
        {
            var slots = new List<Vector4>(1024);
            float Wc = ShipConfig.ContainerWidthM, Hc = CargoTierH;
            float cargoLen = ShipConfig.CargoFwdZ - ShipConfig.CargoAftZ;
            int bays = Mathf.Max(1, Mathf.RoundToInt(cargoLen / (Len40 + 2f)));
            float pitch = cargoLen / bays;
            float coamLen = Len40 + 0.5f;
            const float coamH = 1.8f, coverH = 0.35f;

            for (int i = 0; i < bays; i++)
            {
                float zc = ShipConfig.CargoAftZ + pitch * (i + 0.5f);
                float hbMin = Mathf.Min(ProceduralShipHull.HalfBeam(zc - coamLen * 0.5f),
                                        ProceduralShipHull.HalfBeam(zc + coamLen * 0.5f));
                int rows = Mathf.Clamp(Mathf.FloorToInt(2f * (hbMin - ShipConfig.SideDeckM) / Wc), 1, ShipConfig.DeckRows);
                float halfW = rows * Wc * 0.5f;
                float baseY = ProceduralShipHull.DeckY(zc) + coamH + coverH;   // 커버 윗면=적재 기준

                // 스택 높이 = 길이방향 램프: 선미(뒷부분) 3단 → 선수(앞부분) 1단 계단 하강(랜덤 아님).
                //   i=0=선미(CargoAftZ), i 증가 → 선수(CargoFwdZ). 폭 방향은 균일.
                float v = (bays > 1) ? (float)i / (bays - 1) : 0f;     // 0(선미)~1(선수)
                int tiers = v < 0.34f ? 3 : (v < 0.67f ? 2 : 1);      // 선미 3단 → 중앙 2단 → 선수 1단

                for (int r = 0; r < rows; r++)
                {
                    float x = -halfW + (r + 0.5f) * Wc;
                    for (int t = 0; t < tiers; t++)
                    {
                        float y = baseY + (t + 0.5f) * Hc;
                        int sub = ((i * 7 + r * 13 + t * 5) % CargoPalette + CargoPalette) % CargoPalette;
                        slots.Add(new Vector4(x, y, zc, sub));
                    }
                }
            }
            return slots;
        }

        // ── 3단계: 선수루(forecastle) — 연속 로프트 솔리드 + 둘러싼 불워크 + 방파판 ──
        // 서브메시: 0=상승갑판, 1=현측벽·불워크·방파판
        // 박스 적층(계단형) 폐기 → 이어진 갑판 + 매끈한 현측벽 + 가장자리 불워크로 마감.
        public static Mesh BuildForecastle()
        {
            var mb = new MeshBuilder();
            float z0 = ShipConfig.ForecastleAftZ;
            float z1 = ShipConfig.HalfLoa - 1.5f;                 // 선수 끝 직전
            float raise = ShipConfig.ForecastleRaiseM;
            const float bulw = 1.2f, lip = 0.35f;                 // 불워크 높이·두께
            const int NF = 22;
            Vector3 X = Vector3.right, Y = Vector3.up, Zf = Vector3.forward;

            float[] zz = new float[NF], hw = new float[NF], dM = new float[NF], dT = new float[NF];
            for (int i = 0; i < NF; i++)
            {
                float u = (float)i / (NF - 1);
                float z = Mathf.Lerp(z0, z1, u);
                zz[i] = z;
                hw[i] = Mathf.Max(0.4f, ProceduralShipHull.HalfBeam(z));
                dM[i] = ProceduralShipHull.DeckY(z);
                dT[i] = dM[i] + raise;
            }

            for (int i = 0; i < NF - 1; i++)
            {
                float za = zz[i], zb = zz[i + 1], ha = hw[i], hb = hw[i + 1];
                // 현측벽(주갑판→선수루갑판) 우/좌
                Quad(mb, 1, new Vector3(ha, dM[i], za), new Vector3(ha, dT[i], za),
                            new Vector3(hb, dT[i + 1], zb), new Vector3(hb, dM[i + 1], zb), X);
                Quad(mb, 1, Mir(new Vector3(ha, dM[i], za)), Mir(new Vector3(ha, dT[i], za)),
                            Mir(new Vector3(hb, dT[i + 1], zb)), Mir(new Vector3(hb, dM[i + 1], zb)), -X);
                // 상승 갑판(이어진 면)
                Quad(mb, 0, new Vector3(ha, dT[i], za), new Vector3(-ha, dT[i], za),
                            new Vector3(-hb, dT[i + 1], zb), new Vector3(hb, dT[i + 1], zb), Y);
                // 둘러싼 불워크(우/좌): 외면·내면·윗면
                BulwarkSeg(mb, za, zb, ha, hb, dT[i], dT[i + 1], bulw, lip, false);
                BulwarkSeg(mb, za, zb, ha, hb, dT[i], dT[i + 1], bulw, lip, true);
            }

            // 후벽(화물구 쪽) — 주갑판~불워크 상단 막음
            Quad(mb, 1, new Vector3(hw[0], dM[0], z0), new Vector3(-hw[0], dM[0], z0),
                        new Vector3(-hw[0], dT[0] + bulw, z0), new Vector3(hw[0], dT[0] + bulw, z0), -Zf);
            // 선수 캡(앞면 막음)
            int e = NF - 1;
            Quad(mb, 1, new Vector3(hw[e], dM[e], zz[e]), new Vector3(hw[e], dT[e] + bulw, zz[e]),
                        new Vector3(-hw[e], dT[e] + bulw, zz[e]), new Vector3(-hw[e], dM[e], zz[e]), Zf);

            // 방파판(breakwater) — 선수루 앞 가로 물막이벽
            {
                float zbw = z0 - 2.5f;
                float h = Mathf.Min(CargoHalfW, ProceduralShipHull.HalfBeam(zbw) - ShipConfig.SideDeckM);
                float dy = ProceduralShipHull.DeckY(zbw);
                Box(mb, 1, new Vector3(0f, dy + ShipConfig.BulwarkHeightM * 0.5f, zbw),
                           new Vector3(h * 2f, ShipConfig.BulwarkHeightM, 0.5f));
            }
            return mb.ToMesh("ContainerShip_Forecastle");
        }

        // 불워크 한 변(외면+내면+윗면) — port=true면 좌현
        static void BulwarkSeg(MeshBuilder mb, float za, float zb, float ha, float hb,
                               float dTa, float dTb, float bulw, float lip, bool port)
        {
            float s = port ? -1f : 1f;
            Vector3 outw = Vector3.right * s;
            // 외면(현측벽과 연속, dT→dT+bulw)
            Quad(mb, 1, new Vector3(s * ha, dTa, za), new Vector3(s * ha, dTa + bulw, za),
                        new Vector3(s * hb, dTb + bulw, zb), new Vector3(s * hb, dTb, zb), outw);
            // 내면(lip 안쪽)
            Quad(mb, 1, new Vector3(s * (ha - lip), dTa, za), new Vector3(s * (ha - lip), dTa + bulw, za),
                        new Vector3(s * (hb - lip), dTb + bulw, zb), new Vector3(s * (hb - lip), dTb, zb), -outw);
            // 윗면 캡
            Quad(mb, 1, new Vector3(s * ha, dTa + bulw, za), new Vector3(s * (ha - lip), dTa + bulw, za),
                        new Vector3(s * (hb - lip), dTb + bulw, zb), new Vector3(s * hb, dTb + bulw, zb), Vector3.up);
        }

        // ── 4단계: 거주구 타워 + 브리지 + 펀넬 ─────────────────────────────────
        // 서브메시: 0=하우스, 1=창문대, 2=펀넬, 3=펀넬 식별 밴드
        // 높이는 SOLAS 전방시야로 역산(브리지 눈높이 ≥ 주갑판+20m → 9층). 폭28>길이22 타워형.
        public static Mesh BuildAccommodation()
        {
            var mb = new MeshBuilder();

            float zF = ShipConfig.AccomFrontZ, zA = ShipConfig.AccomAftZ;
            float zc = (zF + zA) * 0.5f;
            float lenZ = Mathf.Abs(zF - zA);                       // 22 (전후)
            float baseY = ProceduralShipHull.DeckY(zc);
            int decks = ShipConfig.AccomDecks;                     // 9
            float dh = ShipConfig.DeckHeightM;                     // 3
            float halfW = ProceduralShipHull.HalfBeam(zc) - 1.6f;  // 폭 ≈29.3m = 0.74·B (표준 0.72~0.85)

            // ── 하우스 ── 베이스 플린스 + 층마다 [벽 + 갑판엣지 립 + 개별 창] + 모서리 필라스터
            // 베이스(기관 케이싱) + 받침 코스
            Box(mb, 0, new Vector3(0f, baseY + dh * 0.5f, zc), new Vector3(halfW * 2f, dh, lenZ));
            Box(mb, 0, new Vector3(0f, baseY + 0.18f, zc), new Vector3(halfW * 2f + 0.6f, 0.36f, lenZ + 0.6f)); // 받침 코스

            for (int d = 1; d < decks; d++)
            {
                float y0 = baseY + d * dh;
                Box(mb, 0, new Vector3(0f, y0 + dh * 0.5f, zc), new Vector3(halfW * 2f, dh, lenZ));                  // 벽(솔리드)
                Box(mb, 0, new Vector3(0f, y0 + 0.13f, zc), new Vector3(halfW * 2f + 0.4f, 0.26f, lenZ + 0.4f));    // 갑판 엣지 립(수평선)
                DeckWindowRow(mb, zF, zA, halfW, y0 + dh * 0.58f, dh * 0.42f, 1.4f);                                 // 개별 창
            }

            float bridgeY = baseY + decks * dh;                    // 브리지 갑판 상면(주갑판 위 27m)

            // 4모서리 수직 필라스터(풀높이) — 박스감 완화
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(mb, 0, new Vector3(sx * halfW, baseY + decks * dh * 0.5f, zc + sz * lenZ * 0.5f),
                               new Vector3(0.6f, decks * dh, 0.6f));

            // ── 브리지 데크(최상) — 윙 일체 + 전방 오버행 + 대형 창 + 처마 ──
            float wingHalf = ProceduralShipHull.HalfBeam(zF);      // 현측까지
            float brLen = lenZ * 0.72f, brCz = zc + 1.6f;          // 전방으로 내밈
            Box(mb, 0, new Vector3(0f, bridgeY + dh * 0.5f, brCz), new Vector3(wingHalf * 2f, dh, brLen));            // 본체(풀폭=윙)
            Box(mb, 0, new Vector3(0f, bridgeY + 0.13f, brCz), new Vector3(wingHalf * 2f + 0.4f, 0.26f, brLen + 0.4f)); // 윙 갑판 립
            DeckWindowRow(mb, brCz + brLen * 0.5f, brCz - brLen * 0.5f, wingHalf, bridgeY + dh * 0.55f, dh * 0.5f, 2.2f); // 대형 창
            Box(mb, 0, new Vector3(0f, bridgeY + dh + 0.18f, brCz + 0.4f), new Vector3(wingHalf * 2f + 0.7f, 0.36f, brLen + 1f)); // 처마
            // 처마 톱 = 주갑판 위 ~30.4m (메인마스트 베이스, ProceduralShipDetails와 정합)

            // ── 펀넬(거주구 바로 뒤) — 테이퍼 + 후방 래이크 + 배기관·플랫폼·사다리 ──
            BuildFunnel(mb, baseY);

            return mb.ToMesh("ContainerShip_Accommodation");
        }

        // 연돌(funnel) — 위로 좁아지고 뒤로 기운 케이싱(로프트) + 빨간 식별밴드 + 배기관 + 플랫폼·사다리
        static void BuildFunnel(MeshBuilder mb, float baseY)
        {
            float zfun = ShipConfig.FunnelZ;
            float y0 = ProceduralShipHull.DeckY(zfun);
            float top = baseY + 22f;                               // 주갑판 위 ~22m(하우스보다 낮게)
            float H = top - y0;
            const float hw0 = 6.0f, hl0 = 4.5f, taper = 0.80f, shear = 3.0f;   // 폭12·전후9, 위 80%로 좁힘, 3m 후방경사
            const int LV = 12;
            Vector3 X = Vector3.right, Y = Vector3.up, Zf = Vector3.forward;

            Vector3[] A = new Vector3[LV], B = new Vector3[LV], C = new Vector3[LV], D = new Vector3[LV];
            for (int k = 0; k < LV; k++)
            {
                float t = (float)k / (LV - 1);
                float hw = Mathf.Lerp(hw0, hw0 * taper, t), hl = Mathf.Lerp(hl0, hl0 * taper, t);
                float cz = zfun - shear * t, cy = y0 + H * t;
                A[k] = new Vector3( hw, cy, cz + hl);   // 전-우
                B[k] = new Vector3( hw, cy, cz - hl);   // 후-우
                C[k] = new Vector3(-hw, cy, cz - hl);   // 후-좌
                D[k] = new Vector3(-hw, cy, cz + hl);   // 전-좌
            }
            for (int k = 0; k < LV - 1; k++)
            {
                float tm = (k + 0.5f) / (LV - 1);
                int sub = (tm >= 0.68f && tm <= 0.84f) ? 3 : 2;   // 빨간 식별 밴드
                Quad(mb, sub, A[k], B[k], B[k + 1], A[k + 1],  X);   // +X
                Quad(mb, sub, D[k], C[k], C[k + 1], D[k + 1], -X);   // -X
                Quad(mb, sub, A[k], D[k], D[k + 1], A[k + 1],  Zf);  // +Z(전면)
                Quad(mb, sub, B[k], C[k], C[k + 1], B[k + 1], -Zf);  // -Z(후면)
            }
            Quad(mb, 2, A[LV - 1], B[LV - 1], C[LV - 1], D[LV - 1], Y);   // 상부 캡

            // 배기관 2개(윗면에서 솟음) — 원기둥
            float czt = zfun - shear, hlt = hl0 * taper;
            for (int s = -1; s <= 1; s += 2)
                CylV(mb, 2, new Vector3(s * 2.0f, top, czt - hlt * 0.2f), 0.7f, 3.6f, 16);
            // 점검 플랫폼(중간) + 수직 사다리(전면)
            Box(mb, 2, new Vector3(0f, y0 + H * 0.55f, zfun - shear * 0.5f),
                       new Vector3(hw0 * 2f + 1.0f, 0.3f, hl0 * 2f * 0.82f + 1.0f));
            Box(mb, 2, new Vector3(0f, y0 + H * 0.5f, zfun + hl0 + 0.15f), new Vector3(0.9f, H, 0.15f)); // 사다리
        }

        // 한 층의 개별 창문 한 줄 — 전면 + 양현(후면은 펀넬쪽이라 생략). 면에 살짝 박아(eps) 진짜 창처럼.
        static void DeckWindowRow(MeshBuilder mb, float frontZ, float aftZ, float halfW, float wy, float ph, float pw)
        {
            const float gap = 1.0f, depth = 0.3f, eps = 0.03f;
            float zc = (frontZ + aftZ) * 0.5f, lenZ = Mathf.Abs(frontZ - aftZ);

            // 전면(z=frontZ, 법선 +Z)
            float usableF = 2f * (halfW - 0.9f);
            int nf = Mathf.Max(1, Mathf.FloorToInt((usableF + gap) / (pw + gap)));
            float spanF = nf * pw + (nf - 1) * gap, xf0 = -spanF * 0.5f + pw * 0.5f;
            for (int i = 0; i < nf; i++)
                Box(mb, 1, new Vector3(xf0 + i * (pw + gap), wy, frontZ + eps - depth * 0.5f), new Vector3(pw, ph, depth));

            // 양현(x=±halfW, 법선 ±X)
            float usableS = lenZ - 1.8f;
            int ns = Mathf.Max(1, Mathf.FloorToInt((usableS + gap) / (pw + gap)));
            float spanS = ns * pw + (ns - 1) * gap, zs0 = zc - spanS * 0.5f + pw * 0.5f;
            for (int i = 0; i < ns; i++)
            {
                float z = zs0 + i * (pw + gap);
                Box(mb, 1, new Vector3( halfW + eps - depth * 0.5f, wy, z), new Vector3(depth, ph, pw));
                Box(mb, 1, new Vector3(-halfW - eps + depth * 0.5f, wy, z), new Vector3(depth, ph, pw));
            }
        }
    }
}
