using System.Collections.Generic;
using UnityEngine;
using Procedural;   // 공유 MeshBuilder (크레인·컨테이너와 동일 빌더)

namespace Container.Ship
{
    /// <summary>
    /// Post-Panamax 컨테이너선 '선체(Hull)' 절차적 메시 생성기 — 1단계(솔리드 우선).
    ///
    /// 단순 박스가 아니라 실제 선형(線型)을 로프트로 조형한다([[feedback_no_placeholder_primitives]]):
    ///   · 평탄한 선저(flat of bottom) + 빌지(bilge) 라운드 + 수직 현측(wall-sided) — 컨테이너선 표준 단면
    ///   · 긴 평행중앙부(parallel midbody)
    ///   · 가는 선수 진입각(fine entrance) → 거의 수직 스템
    ///   · 풍만한 선미 + 트랜섬(transom) 평면 마감
    ///   · 선수/선미 킬 상승(선저 융기), 선수 시어(sheer)
    ///
    /// 좌표계: +Z=선수, 미드십 z=0, ±X=선폭, +Y=상방. 흘수선 y=0(피벗).
    /// 서브메시: 0=Topside(흘수선 위), 1=Bottom(흘수선 아래 방오도장), 2=Boot(흘수선 띠), 3=Deck(주갑판).
    ///
    /// 실척(m)으로 빌드 후 ShipConfig.ModelScale(1/24)로 정점 축소(크레인·컨테이너와 동일 스케일).
    /// 2단계(갑판 해치코밍)·3단계(거주구/펀넬)·4단계(컨테이너 적재)는 화면 확인 후 누적([[feedback_unity_visual_small_increments]]).
    /// </summary>
    public static class ProceduralShipHull
    {
        // 서브메시 인덱스
        public const int SubTopside = 0;   // 흘수선 위 토프사이드
        public const int SubBottom  = 1;   // 흘수선 아래 방오도장(red)
        public const int SubBoot    = 2;   // 흘수선 부트탑(boot-top) 띠
        public const int SubDeck    = 3;   // 주갑판

        const int NZ = 64;   // 길이방향 스테이션 수(코사인 분포 — 양끝 조밀)
        // 단면 거스 분할: 킬 중심+평탄끝(2) + 빌지 호 + 현측(부트 아래) + 부트 상단(1) + 현측(부트 위)
        const int NBilge  = 4;
        const int NSideLo = 5;
        const int NSideHi = 5;
        const int S = 2 + NBilge + NSideLo + 1 + NSideHi;   // = 17 (킬 중심 → 갑판 가장자리)

        const float BootHalf    = 0.45f;   // 부트탑 띠 반높이(m) — 흘수선 ±0.45m
        const float BilgeR      = 2.6f;    // 빌지 반경(m)
        const float StemMinHalf = 0.25f;   // 선수 최소 반폭(스템 바)
        const float SheerBow    = 2.2f;    // 선수 시어 상승(m)
        const float SheerStern  = 0.8f;    // 선미 시어 상승(m)

        // ── 구상선수(bulbous bow) — 흘수선 아래 전방 돌출 물방울형(1b) ──
        //   실척 비율: 돌출 0.03·LOA(FP전방), 중심깊이 0.55·흘수, 반높이 0.30·흘수, 반폭 0.09·선폭.
        //   뿌리(root)는 선체 선수부 안쪽에 임베드되어 한 덩어리로 보임(반폭<선체 반폭).
        const float BulbProtrudeFrac = 0.030f;  // 스템(FP) 전방 돌출량 / LOA
        const float BulbRootInsetFrac = 0.10f;  // 스템 후방 뿌리 임베드 깊이 / LOA
        const float BulbCenterYFrac  = 0.55f;   // 볼브 중심 깊이 / 흘수 (흘수선 아래)
        const float BulbHalfHFrac    = 0.30f;   // 볼브 반높이 / 흘수
        const float BulbHalfWFrac    = 0.09f;   // 볼브 반폭 / 선폭
        const int   NB = 26;                    // 볼브 길이방향 링 수
        const int   RS = 18;                    // 볼브 링 둘레 분할

        static readonly Vector2[] QUV =
            { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };

        public static Mesh Build()
        {
            float draft = ShipConfig.DraftMeters;
            float half  = ShipConfig.LoaMeters * 0.5f;
            float s     = ShipConfig.ModelScale;

            var mb = new MeshBuilder();

            // 스테이션 z (코사인 분포: 선수/선미 곡률 큰 곳을 조밀하게)
            float[] zs = new float[NZ];
            for (int i = 0; i < NZ; i++)
            {
                float u = (float)i / (NZ - 1);
                zs[i] = -Mathf.Cos(Mathf.PI * u) * half;   // -half(선미) → +half(선수)
            }

            // 각 스테이션 단면(스타보드 절반, x>=0) 사전계산
            var secs = new Vector2[NZ][];
            for (int i = 0; i < NZ; i++) secs[i] = Section(zs[i]);

            // ── 셸 로프트(현측) + 갑판 스트립 ──
            for (int i = 0; i < NZ - 1; i++)
            {
                Vector2[] A = secs[i], B = secs[i + 1];
                float z0 = zs[i], z1 = zs[i + 1];

                for (int k = 0; k < S - 1; k++)
                {
                    Vector3 a0 = new Vector3(A[k].x,     A[k].y,     z0);
                    Vector3 a1 = new Vector3(A[k + 1].x, A[k + 1].y, z0);
                    Vector3 b0 = new Vector3(B[k].x,     B[k].y,     z1);
                    Vector3 b1 = new Vector3(B[k + 1].x, B[k + 1].y, z1);
                    int sub = ZoneOf((a0.y + a1.y + b0.y + b1.y) * 0.25f);

                    Quad(mb, sub, s, a0, a1, b1, b0);                 // 스타보드(+X 외향)
                    Quad(mb, sub, s, Mir(a0), Mir(b0), Mir(b1), Mir(a1)); // 포트(-X 외향)
                }

                // 주갑판 스트립(스타보드 갑판가장자리 ↔ 포트, +Y 외향)
                Vector3 sbD = new Vector3(A[S - 1].x, A[S - 1].y, z0);
                Vector3 sbE = new Vector3(B[S - 1].x, B[S - 1].y, z1);
                Quad(mb, SubDeck, s, sbD, Mir(sbD), Mir(sbE), sbE);
            }

            // ── 끝단 캡(선미 트랜섬 -Z, 선수 +Z) ──
            CapStation(mb, secs[0],      zs[0],      s, false);  // 선미
            CapStation(mb, secs[NZ - 1], zs[NZ - 1], s, true);   // 선수

            // ── 구상선수(1b) ──
            BuildBulb(mb, half, draft, s);

            return mb.ToMesh("ContainerShip_Hull");
        }

        // ── 선체 곡선 공개 함수 (갑판/거주구/컨테이너 배치가 폭·높이를 정확히 읽는 SSOT) ──

        /// <summary>스테이션 z 의 현측 반폭(half-beam) — 실척 m. 평행중앙부 일정, 선수 가늘게/선미 풍만.</summary>
        public static float HalfBeam(float z)
        {
            float half = ShipConfig.LoaMeters * 0.5f;
            float Bh   = ShipConfig.BeamMeters * 0.5f;
            float t = z / half, a = Mathf.Abs(t);
            float f;
            if (a <= 0.30f) f = 1f;
            else
            {
                float p = (a - 0.30f) / 0.70f;
                f = (t >= 0f) ? 1f - Mathf.Pow(p, 2.2f)          // 선수: 가는 진입각
                              : 1f - 0.42f * Mathf.Pow(p, 1.6f); // 선미: 풍만(트랜섬 ≈0.58)
            }
            float b = Bh * f;
            if (t > 0f && a > 0.85f) b = Mathf.Max(b, StemMinHalf); // 스템 바 최소폭
            return Mathf.Max(b, 0.02f);
        }

        /// <summary>스테이션 z 의 선저(킬) 높이 — 실척 m. 중앙 -draft, 선수/선미 융기.</summary>
        public static float KeelY(float z)
        {
            float half = ShipConfig.LoaMeters * 0.5f, draft = ShipConfig.DraftMeters;
            float t = z / half, a = Mathf.Abs(t);
            float yb = -draft;
            if (t >= 0f && a > 0.80f)     { float q = (a - 0.80f) / 0.20f; yb = -draft + draft * 0.92f * Mathf.Pow(q, 1.7f); }
            else if (t < 0f && a > 0.72f) { float q = (a - 0.72f) / 0.28f; yb = -draft + draft * 0.78f * Mathf.Pow(q, 1.5f); }
            return yb;
        }

        /// <summary>스테이션 z 의 주갑판 높이 — 실척 m. 건현 + 시어(선수/선미 상승).</summary>
        public static float DeckY(float z)
        {
            float half = ShipConfig.LoaMeters * 0.5f, fb = ShipConfig.FreeboardMeters;
            float t = z / half, a = Mathf.Abs(t);
            float yd = fb;
            if (a > 0.50f) { float q = (a - 0.50f) / 0.50f; yd += (t >= 0f ? SheerBow : SheerStern) * q * q; }
            return yd;
        }

        /// <summary>스테이션 z 의 단면(스타보드 절반) — 킬 중심(0)→갑판 가장자리(S-1).
        /// 현측에 부트탑 경계(흘수선 ±BootHalf)를 '정확한 정점'으로 꽂아, 그 사이 한 행만 깔끔한 부트 띠가 되게 한다
        /// (면 행이 흘수선을 비스듬히 가로질러 검은 패치가 계단지던 문제 해소).</summary>
        static Vector2[] Section(float z)
        {
            float b  = HalfBeam(z);
            float yb = KeelY(z);
            float yd = DeckY(z);
            float br = Mathf.Min(BilgeR, b * 0.9f);
            float bf = Mathf.Max(0f, b - br);
            float sideBot = yb + br;
            float yLo = Mathf.Clamp(-BootHalf, sideBot, yd);   // 부트 하단 경계
            float yHi = Mathf.Clamp( BootHalf, sideBot, yd);   // 부트 상단 경계

            var pts = new Vector2[S];   // = 2(킬+평탄끝) + NBilge + NSideLo + 1(yHi) + NSideHi
            int k = 0;
            pts[k++] = new Vector2(0f, yb);     // 킬 중심
            pts[k++] = new Vector2(bf, yb);     // 평탄 선저 끝
            for (int i = 1; i <= NBilge; i++)   // 빌지 호 (bf,yb)→(b,sideBot)
            {
                float a = -Mathf.PI * 0.5f + (Mathf.PI * 0.5f) * i / NBilge;
                pts[k++] = new Vector2(bf + br * Mathf.Cos(a), sideBot + br * Mathf.Sin(a));
            }
            for (int i = 1; i <= NSideLo; i++)  // 현측 sideBot→yLo (마지막=부트 하단 경계)
                pts[k++] = new Vector2(b, Mathf.Lerp(sideBot, yLo, (float)i / NSideLo));
            pts[k++] = new Vector2(b, yHi);     // 부트 상단 경계(앞 점과의 사이 = 부트 띠)
            for (int i = 1; i <= NSideHi; i++)  // 현측 yHi→갑판
                pts[k++] = new Vector2(b, Mathf.Lerp(yHi, yd, (float)i / NSideHi));
            return pts;
        }

        /// <summary>끝단(선수/선미) 단면을 포트+스타보드 폐곡선으로 캡(부채꼴 삼각분할).</summary>
        static void CapStation(MeshBuilder mb, Vector2[] sec, float z, float s, bool bowEnd)
        {
            int n = sec.Length;
            var poly = new List<Vector3>(n * 2);
            for (int k = 0; k < n; k++)     poly.Add(new Vector3(sec[k].x,  sec[k].y, z));        // 스타보드 킬→갑판
            for (int k = n - 1; k >= 0; k--) poly.Add(new Vector3(-sec[k].x, sec[k].y, z));       // 포트 갑판→킬

            Vector3 c = Vector3.zero;
            foreach (var p in poly) c += p;
            c /= poly.Count;

            Vector3 want = bowEnd ? Vector3.forward : Vector3.back;
            int m = poly.Count;
            for (int k = 0; k < m; k++)
            {
                Vector3 p0 = poly[k], p1 = poly[(k + 1) % m];
                int sub = ZoneOf((p0.y + p1.y + c.y) / 3f);
                Tri(mb, sub, s, c, p0, p1, want);
            }
        }

        /// <summary>구상선수 — 흘수선 아래 전방 돌출 물방울형. 뿌리는 선체 선수부 안쪽에 임베드.</summary>
        static void BuildBulb(MeshBuilder mb, float half, float draft, float s)
        {
            // 스템(FP) ≈ 선체 최전방(half) 기준으로 전방 돌출/후방 임베드 산출
            float zTip  = half + BulbProtrudeFrac * ShipConfig.LoaMeters;   // 코 끝(전방 돌출)
            float zRoot = half - BulbRootInsetFrac * ShipConfig.LoaMeters;  // 뿌리(선체 안 임베드)
            float yc    = -BulbCenterYFrac * draft;              // 중심 깊이
            float hwMax = BulbHalfWFrac * ShipConfig.BeamMeters; // 최대 반폭
            float hhMax = BulbHalfHFrac * draft;                 // 최대 반높이
            float zC    = Mathf.Lerp(zRoot, zTip, 0.45f);        // 살찐 중심
            float Lfwd  = zTip - zC;
            float Laft  = zC - zRoot;

            // 길이방향 shape(z): 코끝(zTip)→0, 뿌리쪽은 1.5배 폭으로 0에 안 닿게(임베드)
            float Shape(float z)
            {
                float e = (z >= zC) ? (z - zC) / Lfwd : (zC - z) / (Laft * 1.5f);
                return Mathf.Sqrt(Mathf.Max(0f, 1f - e * e));
            }

            Vector3 Ring(int j, float z, float sh)
            {
                float a = (Mathf.PI * 2f) * j / RS;
                return new Vector3(hwMax * sh * Mathf.Cos(a), yc + hhMax * sh * Mathf.Sin(a), z);
            }

            float[] zr = new float[NB];
            float[] shp = new float[NB];
            for (int i = 0; i < NB; i++)
            {
                float u = (float)i / (NB - 1);
                zr[i] = Mathf.Lerp(zRoot, zTip, u);
                shp[i] = Shape(zr[i]);
            }

            // 링 로프트(둘레 외향)
            for (int i = 0; i < NB - 1; i++)
            {
                for (int j = 0; j < RS; j++)
                {
                    Vector3 a = Ring(j,     zr[i],     shp[i]);
                    Vector3 b = Ring(j + 1, zr[i],     shp[i]);
                    Vector3 c = Ring(j + 1, zr[i + 1], shp[i + 1]);
                    Vector3 d = Ring(j,     zr[i + 1], shp[i + 1]);
                    Vector3 want = new Vector3(a.x + c.x, (a.y - yc) + (c.y - yc), 0f); // 반경 외향
                    QuadN(mb, ZoneOf((a.y + c.y) * 0.5f), s, a, b, c, d, want);
                }
            }

            // 코끝 캡(+Z) — 마지막 링 → 끝점
            Vector3 tip = new Vector3(0f, yc, zTip);
            for (int j = 0; j < RS; j++)
            {
                Vector3 a = Ring(j,     zr[NB - 1], shp[NB - 1]);
                Vector3 b = Ring(j + 1, zr[NB - 1], shp[NB - 1]);
                Tri(mb, SubBottom, s, tip, a, b, Vector3.forward);
            }
            // 뿌리 캡(-Z) — 선체 안 임베드(은폐)
            Vector3 root = new Vector3(0f, yc, zRoot);
            for (int j = 0; j < RS; j++)
            {
                Vector3 a = Ring(j,     zr[0], shp[0]);
                Vector3 b = Ring(j + 1, zr[0], shp[0]);
                Tri(mb, SubBottom, s, root, a, b, Vector3.back);
            }
        }

        // 흘수선 기준 서브메시 구역
        static int ZoneOf(float y)
        {
            if (Mathf.Abs(y) < BootHalf) return SubBoot;
            return (y < 0f) ? SubBottom : SubTopside;
        }

        static Vector3 Mir(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        static void Quad(MeshBuilder mb, int sub, float s, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int ia = mb.AddVertex(a * s, n, QUV[0]);
            int ib = mb.AddVertex(b * s, n, QUV[1]);
            int ic = mb.AddVertex(c * s, n, QUV[2]);
            int id = mb.AddVertex(d * s, n, QUV[3]);
            mb.AddQuad(sub, ia, ib, ic, id);
        }

        // 외향(want) 보장 쿼드 — 노멀이 want 반대면 순서 뒤집음(볼브 둘레용)
        static void QuadN(MeshBuilder mb, int sub, float s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, want) < 0f) { var t = b; b = d; d = t; }  // 감김 반전
            Quad(mb, sub, s, a, b, c, d);
        }

        static void Tri(MeshBuilder mb, int sub, float s, Vector3 a, Vector3 b, Vector3 c, Vector3 want)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, want) < 0f) { var t = b; b = c; c = t; n = -n; }  // 외향 보장
            n = n.sqrMagnitude > 1e-9f ? n.normalized : want;
            int ia = mb.AddVertex(a * s, n, new Vector2(0f, 0f));
            int ib = mb.AddVertex(b * s, n, new Vector2(1f, 0f));
            int ic = mb.AddVertex(c * s, n, new Vector2(0f, 1f));
            mb.AddTriangle(sub, ia, ib, ic);
        }
    }
}
