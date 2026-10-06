using System.Collections.Generic;
using UnityEngine;
using Procedural;   // 공유 MeshBuilder (크레인 생성기와 공유)

namespace AIXRCrane
{
    /// <summary>20ft Dry 컨테이너 절차적 메시. 기본 1/24 미니어처, 중심 피벗, +Z=도어.
    /// 서브메시: 0=Body,1=Door,2=Frame,3=Castings,4=Marking(ID/CSC).</summary>
    public static partial class ProceduralContainerMesh
    {
        // 기본 출력 스케일: VR 미니어처(1/24)
        public const float DefaultMiniatureScale = 1f / 24f;

        // 빌더 내부에서 사용하는 ISO 668 실측 (m). 마지막에 일괄 스케일됨.
        // 기본값 = 20ft Std (22G1). BuildSized() 로 다른 사이즈도 빌드 가능.
        public static float Length = Length20ft;
        public static float Width  = StdWidth;
        public static float Height = HeightStd;

        // 표준 사이즈 프리셋
        public const float Length20ft = 6.058f;
        public const float Length40ft = 12.192f;
        public const float HeightStd  = 2.591f;  // 8'6"
        public const float StdWidth   = 2.438f;

        // 프레임 / 캐스팅 / 패널 치수
        const float CornerCastW = 0.178f;
        const float CornerCastH = 0.135f;
        const float CornerCastTopH = 0.135f;  // = CornerCastH(대칭), top이 Height(2.591)와 일치(스택 정합)
        const float CornerCastD = 0.162f;
        // ISO 1161 코너 캐스팅 구멍(외측 3면) 124.5×63.5mm. 면별 long/short 는 AddCornerCastingWithHoles.
        const float CastHoleLong  = 0.1245f;
        const float CastHoleShort = 0.0635f;
        const float CastWallThick = 0.018f;  // 벽 두께 — 구멍이 안쪽으로 들어가는 recess 깊이
        const float RailH       = 0.092f;
        const float CornerPostW = 0.098f;
        // 패널 base 안쪽 들임 = CorrDepth → 주름 외측면이 캐스팅·포스트와 동일 평면(틈 없음).
        const float PanelInset  = 0.028f;

        // 주름판(vertical corrugation) — 바깥 크라운(flatOut)이 안쪽 밸리(flatIn)보다 좁은 비대칭 사다리꼴.
        // period(=fIn+slope+fOut+slope)는 0.20 유지(산 개수 불변).
        const float CorrDepth   = 0.028f;
        const float CorrFlatIn  = 0.070f;   // 안쪽 밸리(넓게)
        const float CorrFlatOut = 0.050f;   // 바깥 크라운(좁게)
        const float CorrSlope   = 0.040f;

        // 도어
        const float DoorGap          = 0.004f;  // 도어 사이 틈 최소화
        const float LockBarDiameter  = 0.030f;
        const int   LockBarSides     = 8;
        const float HingeBlockH      = 0.090f;
        const float HingeBlockD      = 0.050f;
        const int   HingesPerDoor    = 4;
        const int   LockBarsPerDoor  = 2;
        // 락바 부속
        const float LockCamSize      = 0.045f;  // 락바 상단/하단 캠 (원기둥 직경/높이)
        // 락바 마운트 브래킷 (도어 표면에 락바를 잡아주는 클램프)
        const int   LockBracketsPerBar = 2;
        const float LockBracketW       = 0.050f;
        const float LockBracketH       = 0.020f;
        const float LockBracketD       = 0.060f;  // Z 깊이 — 도어 외측면부터 락바 너머까지
        // 도어 측면 빔 (도어 외측 모서리의 평평한 세로 띠 — 힌지가 여기 붙음)
        const float SideBeamW          = 0.080f;
        // 도어 헤더 (얇게)
        const float DoorHeaderHeight = 0.025f;
        const float DoorHeaderDepth  = 0.020f;
        // ID/CSC plate
        const float IdPlateW         = 0.300f;
        const float IdPlateH         = 0.180f;
        const float CscPlateW        = 0.140f;
        const float CscPlateH        = 0.100f;
        const float PlateOut         = 0.003f;

        // 지붕 코르게이션
        const float RoofCorrDepth = 0.020f;  // 지붕 코르게이션 깊이 (산이 캐스팅 top 직전까지 솟음)

        static void ApplyTransform(Mesh mesh, float scale, bool centerPivot, bool xIsLength)
        {
            if (scale == 1f && !centerPivot && !xIsLength) return;

            var verts = mesh.vertices;
            float yShift = centerPivot ? -Height * 0.5f : 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                v.y += yShift;
                if (xIsLength)
                {
                    // Y축 -90도 회전: (x, y, z) → (z, y, -x). 도어=+Z였던 게 도어=+X가 됨.
                    v = new Vector3(v.z, v.y, -v.x);
                }
                v *= scale;
                verts[i] = v;
            }
            mesh.vertices = verts;

            if (xIsLength)
            {
                var normals = mesh.normals;
                for (int i = 0; i < normals.Length; i++)
                {
                    var n = normals[i];
                    normals[i] = new Vector3(n.z, n.y, -n.x);
                }
                mesh.normals = normals;
                mesh.RecalculateTangents();
            }
            mesh.RecalculateBounds();
        }

        // 외측 3면 (컨테이너 바깥쪽 X/Z/Y) 에 사각 구멍이 있는 코너 캐스팅.
        // 내측 3면은 솔리드. outX/outZ: ±1, outYUp: true이면 상단 캐스팅 (+Y 외측).
        static void AddCornerCastingWithHoles(MeshBuilder b, int submesh,
            Vector3 center, Vector3 size, int outX, int outZ, bool outYUp)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = center + new Vector3( h.x, -h.y, -h.z);
            Vector3 p110 = center + new Vector3( h.x,  h.y, -h.z);
            Vector3 p010 = center + new Vector3(-h.x,  h.y, -h.z);
            Vector3 p001 = center + new Vector3(-h.x, -h.y,  h.z);
            Vector3 p101 = center + new Vector3( h.x, -h.y,  h.z);
            Vector3 p111 = center + new Vector3( h.x,  h.y,  h.z);
            Vector3 p011 = center + new Vector3(-h.x,  h.y,  h.z);

            // 내측 3면 (컨테이너 내부 향함 — 솔리드)
            if (outX > 0) AddFlatQuad(b, submesh, p000, p001, p011, p010, Vector3.left);
            else          AddFlatQuad(b, submesh, p101, p100, p110, p111, Vector3.right);

            if (outZ > 0) AddFlatQuad(b, submesh, p100, p000, p010, p110, Vector3.back);
            else          AddFlatQuad(b, submesh, p001, p101, p111, p011, Vector3.forward);

            if (outYUp)   AddFlatQuad(b, submesh, p000, p100, p101, p001, Vector3.down);
            else          AddFlatQuad(b, submesh, p011, p111, p110, p010, Vector3.up);

            // 외측 X 면 — 구멍 long axis 은 face-local right (= 컨테이너 Z = 길이)
            if (outX > 0)
                AddFaceWithRectHole(b, submesh, p101, p100, p110, p111, Vector3.right,
                    CastHoleLong / size.z, CastHoleShort / size.y, CastWallThick);
            else
                AddFaceWithRectHole(b, submesh, p000, p001, p011, p010, Vector3.left,
                    CastHoleLong / size.z, CastHoleShort / size.y, CastWallThick);

            // 외측 Z 면 — 구멍 long axis 은 face-local right (= 컨테이너 X = 폭)
            if (outZ > 0)
                AddFaceWithRectHole(b, submesh, p001, p101, p111, p011, Vector3.forward,
                    CastHoleLong / size.x, CastHoleShort / size.y, CastWallThick);
            else
                AddFaceWithRectHole(b, submesh, p100, p000, p010, p110, Vector3.back,
                    CastHoleLong / size.x, CastHoleShort / size.y, CastWallThick);

            // 외측 Y 면 — 구멍 long axis 은 face-local up (= 컨테이너 Z = 길이), short = X
            if (outYUp)
                AddFaceWithRectHole(b, submesh, p011, p111, p110, p010, Vector3.up,
                    CastHoleShort / size.x, CastHoleLong / size.z, CastWallThick);
            else
                AddFaceWithRectHole(b, submesh, p000, p100, p101, p001, Vector3.down,
                    CastHoleShort / size.x, CastHoleLong / size.z, CastWallThick);
        }

        // 사각형 면(c00→c10→c11→c01 CCW)에 구멍을 뚫고 holeDepth만큼 들어간 recess 생성.
        // holeRightFrac/holeUpFrac: 구멍 크기(0~1, 중앙 정렬).
        static void AddFaceWithRectHole(MeshBuilder b, int submesh,
            Vector3 c00, Vector3 c10, Vector3 c11, Vector3 c01, Vector3 normal,
            float holeRightFrac, float holeUpFrac, float holeDepth)
        {
            float u0 = (1f - holeRightFrac) * 0.5f;
            float u1 = 1f - u0;
            float v0 = (1f - holeUpFrac)    * 0.5f;
            float v1 = 1f - v0;

            Vector3 right = c10 - c00;
            Vector3 up    = c01 - c00;

            Vector3 onLeftTop  = c00 +              up * v1;
            Vector3 onLeftBot  = c00 +              up * v0;
            Vector3 onRightTop = c00 + right +      up * v1;
            Vector3 onRightBot = c00 + right +      up * v0;
            Vector3 hBL = c00 + right * u0 + up * v0;
            Vector3 hBR = c00 + right * u1 + up * v0;
            Vector3 hTR = c00 + right * u1 + up * v1;
            Vector3 hTL = c00 + right * u0 + up * v1;

            // 외측 면 — 구멍 주위 4 strip (모서리 중복 없음)
            AddFlatQuad(b, submesh, onLeftTop, onRightTop, c11, c01, normal);    // 상단 (full width)
            AddFlatQuad(b, submesh, c00, c10, onRightBot, onLeftBot, normal);    // 하단 (full width)
            AddFlatQuad(b, submesh, onLeftBot, hBL, hTL, onLeftTop, normal);     // 좌측 (구멍 사이만)
            AddFlatQuad(b, submesh, hBR, onRightBot, onRightTop, hTR, normal);   // 우측 (구멍 사이만)

            // 구멍 안쪽 4 벽 + 뒷면
            Vector3 backOffset = -normal * holeDepth;
            Vector3 bhBL = hBL + backOffset;
            Vector3 bhBR = hBR + backOffset;
            Vector3 bhTR = hTR + backOffset;
            Vector3 bhTL = hTL + backOffset;
            Vector3 upN    = up.normalized;
            Vector3 rightN = right.normalized;

            AddFlatQuad(b, submesh, hTR, hTL, bhTL, bhTR, -upN);      // 위쪽 벽 (구멍 안에서 보면 천장)
            AddFlatQuad(b, submesh, hBL, hBR, bhBR, bhBL,  upN);      // 아래쪽 벽
            AddFlatQuad(b, submesh, hTL, hBL, bhBL, bhTL,  rightN);   // 좌측 벽
            AddFlatQuad(b, submesh, hBR, hTR, bhTR, bhBR, -rightN);   // 우측 벽
            AddFlatQuad(b, submesh, bhBL, bhBR, bhTR, bhTL, normal);  // 뒷면 (recess 바닥)
        }

        // 4-vertex flat quad (CCW from +normal) — MeshBuilder.AddFace와 동일 기능을 외부 노출.
        static void AddFlatQuad(MeshBuilder mb, int submesh,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int ia = mb.AddVertex(a, normal, new Vector2(0f, 0f));
            int ib = mb.AddVertex(b, normal, new Vector2(1f, 0f));
            int ic = mb.AddVertex(c, normal, new Vector2(1f, 1f));
            int id = mb.AddVertex(d, normal, new Vector2(0f, 1f));
            mb.AddQuad(submesh, ia, ib, ic, id);
        }

        /// <summary>수직 주름판(corrugated panel) 생성. origin=좌하단, right=폭 방향, up=높이 방향.
        /// outward 법선=Cross(right,up) — 호출자가 그 방향으로 right/up을 선택해야 함.</summary>
        static void BuildCorrugatedPanel(MeshBuilder b, int submesh,
            Vector3 origin, Vector3 right, Vector3 up,
            float width, float height, float depth)
        {
            Vector3 outDir = Vector3.Cross(right, up).normalized;

            // 한 주기 = flatIn + slope + flatOut + slope
            float period = CorrFlatIn + CorrSlope + CorrFlatOut + CorrSlope;
            int periods = Mathf.Max(1, Mathf.RoundToInt(width / period));
            float actualPeriod = width / periods;
            // 비율 유지하면서 폭에 맞춤
            float scale = actualPeriod / period;
            float fIn  = CorrFlatIn  * scale;
            float fOut = CorrFlatOut * scale;
            float slp  = CorrSlope   * scale;

            // 단면 노드(along, outOffset, normal) — 주기당 flatIn·slope↑·flatOut·slope↓.

            var profile = new List<(float along, float outOff, Vector3 normal)>();
            float along = 0f;
            // slope 노드의 normal 기울기
            Vector3 slopeUpNormal   = (outDir * slp + right * depth).normalized;
            Vector3 slopeDownNormal = (outDir * slp - right * depth).normalized;

            for (int p = 0; p < periods; p++)
            {
                // flatIn start
                profile.Add((along, 0f, outDir));
                along += fIn;
                profile.Add((along, 0f, outDir));
                // slope up
                profile.Add((along, 0f, slopeUpNormal));
                along += slp;
                profile.Add((along, depth, slopeUpNormal));
                // flatOut
                profile.Add((along, depth, outDir));
                along += fOut;
                profile.Add((along, depth, outDir));
                // slope down
                profile.Add((along, depth, slopeDownNormal));
                along += slp;
                profile.Add((along, 0f, slopeDownNormal));
            }

            // 위/아래 두 줄 vertex, segment마다 quad 1개. U=along, V=height 정규화.
            int[] bottomIdx = new int[profile.Count];
            int[] topIdx    = new int[profile.Count];
            for (int i = 0; i < profile.Count; i++)
            {
                var (al, oo, nrm) = profile[i];
                Vector3 basePos = origin + right * al;
                Vector3 outOff  = outDir * oo;
                float u = al / width;
                bottomIdx[i] = b.AddVertex(basePos + outOff,          nrm, new Vector2(u, 0f));
                topIdx[i]    = b.AddVertex(basePos + outOff + up * height, nrm, new Vector2(u, 1f));
            }
            // (bottom_i, bottom_i+1, top_i+1, top_i) winding → 법선 = outDir.
            for (int i = 0; i < profile.Count - 1; i++)
            {
                b.AddQuad(submesh, bottomIdx[i], bottomIdx[i + 1], topIdx[i + 1], topIdx[i]);
            }
        }

        // 도어 면 전용: 평탄 외측 면에 가로 홈 grooves개 균등 배치(사다리꼴 단면: 평탄→경사→바닥→경사→평탄).
        // right=단면 진행축, up=전폭 방향, 외측 법선=Cross(right,up).
        static void BuildGroovedDoorPanel(MeshBuilder b, int submesh,
            Vector3 origin, Vector3 right, Vector3 up,
            float length, float span, float grooveDepth,
            int grooves, float grooveBottomFrac, float grooveSlopeFrac)
        {
            Vector3 outDir = Vector3.Cross(right, up).normalized;

            float band    = length / grooves;
            float bottomW = band * grooveBottomFrac;
            float slopeW  = band * grooveSlopeFrac;

            // 진입(외측→안쪽)·탈출(안쪽→외측) 경사면 법선 — BuildCorrugatedPanel과 동일 휴리스틱.
            Vector3 slopeInN  = (outDir * slopeW - right * grooveDepth).normalized;  // along 증가 시 안쪽으로 내려감
            Vector3 slopeOutN = (outDir * slopeW + right * grooveDepth).normalized;  // along 증가 시 외측으로 올라옴

            var profile = new List<(float along, float depth, Vector3 normal)>();
            profile.Add((0f, 0f, outDir));
            for (int k = 0; k < grooves; k++)
            {
                float yc     = (k + 0.5f) * band;
                float gStart = yc - bottomW * 0.5f - slopeW;
                float gBotS  = yc - bottomW * 0.5f;
                float gBotE  = yc + bottomW * 0.5f;
                float gEnd   = yc + bottomW * 0.5f + slopeW;

                profile.Add((gStart, 0f, outDir));            // 외측 평탄 끝
                profile.Add((gStart, 0f, slopeInN));          // 경사 진입 시작
                profile.Add((gBotS, grooveDepth, slopeInN));  // 홈 바닥 진입
                profile.Add((gBotS, grooveDepth, outDir));    // 바닥 평탄 시작
                profile.Add((gBotE, grooveDepth, outDir));    // 바닥 평탄 끝
                profile.Add((gBotE, grooveDepth, slopeOutN)); // 경사 탈출 시작
                profile.Add((gEnd, 0f, slopeOutN));           // 외측 복귀
                profile.Add((gEnd, 0f, outDir));              // 외측 평탄 재개
            }
            profile.Add((length, 0f, outDir));

            int n = profile.Count;
            int[] loIdx = new int[n];
            int[] hiIdx = new int[n];
            for (int i = 0; i < n; i++)
            {
                var (al, dp, nrm) = profile[i];
                Vector3 basePos = origin + right * al - outDir * dp;  // depth는 안쪽(-outDir)으로 리세스
                float u = al / length;
                loIdx[i] = b.AddVertex(basePos,            nrm, new Vector2(u, 0f));
                hiIdx[i] = b.AddVertex(basePos + up * span, nrm, new Vector2(u, 1f));
            }
            for (int i = 0; i < n - 1; i++)
            {
                b.AddQuad(submesh, loIdx[i], loIdx[i + 1], hiIdx[i + 1], hiIdx[i]);
            }
        }

        // 바닥
        static void BuildFloor(MeshBuilder b)
        {
            // 외측 경계를 코너 캐스팅 외측면까지 확장
            float hx = Width  * 0.5f;
            float hz = Length * 0.5f;

            // 외측 바닥(-Y): rail bottom 5mm 위 — 레일이 가장자리를 덮어 마감.
            float yOut = CornerCastH * 0.5f - RailH * 0.5f + 0.005f;  // = railBottomY + 5mm
            int a = b.AddVertex(new Vector3(-hx, yOut, -hz), Vector3.down, new Vector2(0f, 0f));
            int b1 = b.AddVertex(new Vector3( hx, yOut, -hz), Vector3.down, new Vector2(1f, 0f));
            int c1 = b.AddVertex(new Vector3( hx, yOut,  hz), Vector3.down, new Vector2(1f, 1f));
            int d1 = b.AddVertex(new Vector3(-hx, yOut,  hz), Vector3.down, new Vector2(0f, 1f));
            // (a, b, c, d) winding → normal = -Y
            b.AddQuad(0, a, b1, c1, d1);

            // 내측 바닥(+Y, 도어 열면 보임) — panelBottom 높이와 일치.
            float yIn = CornerCastH * 0.5f + RailH * 0.5f;
            int e = b.AddVertex(new Vector3(-hx, yIn, -hz), Vector3.up, new Vector2(0f, 0f));
            int f = b.AddVertex(new Vector3( hx, yIn, -hz), Vector3.up, new Vector2(1f, 0f));
            int g = b.AddVertex(new Vector3( hx, yIn,  hz), Vector3.up, new Vector2(1f, 1f));
            int h = b.AddVertex(new Vector3(-hx, yIn,  hz), Vector3.up, new Vector2(0f, 1f));
            // 역 winding → normal = +Y
            b.AddQuad(0, e, h, g, f);
        }

        // 바닥 하부 구조(언더프레임) — ISO1496 크로스멤버가 바닥판을 받치고, 포크포켓·구스넥 터널도 포함.
        // 바닥 외측판 바로 아래 매달림(서브메시 2). 코너 캐스팅 밑면(y=0) 위에 머물러 안착 유지.
        const float CrossMemberSpacing = 0.30f;   // 실측 중심 간격(~300mm)
        const float CrossMemberThick   = 0.05f;   // Z 두께(C채널 플랜지 폭 근사)
        const float CrossMemberDepth   = 0.018f;  // 바닥판 아래로 매달리는 깊이
        const float ForkPocketZ        = 1.025f;  // 포크포켓 중심 Z(±, 20ft 2개) — ISO 표준 센터간격 2050mm
        const float ForkPocketWidth    = 0.35f;   // 포켓 개구 폭(Z) — ISO 표준 350mm
        const float ForkPocketDepth    = 0.022f;  // 보강 하우징 깊이(크로스멤버보다 굵게, 최저점 y≈0.0045>0 유지)
        const float ForkPlateT         = 0.006f;  // 포켓 강판 두께(6mm) — 단일메시·Kit 공유
        const float GooseneckHalfW     = 0.34f;   // 구스넥 터널 반폭(X)
        const float GooseneckLen       = 1.30f;   // 구스넥 터널 길이(전면에서 Z)
        const float GooseneckDepth     = 0.022f;  // 최저점 y≈0.0045>0 (캐스팅 안착 유지)
        // submesh: 단일메시 Build()는 2(Frame). 분해 Kit은 파트당 단일 머티라 0으로 호출.
        static void BuildUnderframe(MeshBuilder b, int submesh = 2)
        {
            float railZSpan = Length - CornerCastD * 2f;            // 크로스멤버 분포 Z 범위(끝 캐스팅 사이)
            float spanX     = Width - CornerPostW * 2f;             // 좌우 바텀 사이드레일 안쪽 사이
            float floorOutY = CornerCastH * 0.5f - RailH * 0.5f + 0.005f;  // BuildFloor yOut(바닥 외측판) 동일
            float centerY   = floorOutY - CrossMemberDepth * 0.5f;  // 바닥판 바로 아래 매달림
            int n = Mathf.Max(3, Mathf.RoundToInt(railZSpan / CrossMemberSpacing));
            float usable = railZSpan - CrossMemberThick;            // 양 끝 캐스팅 안쪽으로 들임
            // 포켓 개구를 침범하는 크로스멤버는 제거(지게차 타인 인입 경로).
            float fpGate = ForkPocketWidth * 0.5f + CrossMemberThick * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = (n == 1) ? 0.5f : (float)i / (n - 1);
                float z = -railZSpan * 0.5f + CrossMemberThick * 0.5f + usable * t;
                if (Mathf.Abs(Mathf.Abs(z) - ForkPocketZ) < fpGate) continue;  // 포켓 개구 침범 멤버 제외
                b.AddBox(submesh, new Vector3(0f, centerY, z), new Vector3(spanX, CrossMemberDepth, CrossMemberThick));
            }

            // 포크포켓 — 측면 인입 지게차 포켓(20ft 2개, ±ForkPocketZ). 크로스멤버보다 굵은 보강 하우징.
            float fpY = floorOutY - ForkPocketDepth * 0.5f;
            for (int s = -1; s <= 1; s += 2)
                b.AddBox(submesh, new Vector3(0f, fpY, s * ForkPocketZ), new Vector3(spanX, ForkPocketDepth, ForkPocketWidth));

            // 구스넥 터널 — 전면(도어 반대, -Z) 하부 중앙 채널의 양 벽(섀시 구스넥 안착부).
            float gnY  = floorOutY - GooseneckDepth * 0.5f;
            float gnZc = -Length * 0.5f + CornerCastD + GooseneckLen * 0.5f;
            for (int s = -1; s <= 1; s += 2)
                b.AddBox(submesh, new Vector3(s * GooseneckHalfW, gnY, gnZc), new Vector3(0.02f, GooseneckDepth, GooseneckLen));
        }

        // 캠 키퍼 — 상/하 캠이 도어 헤더/실에 물려 잠그는 ㄷ자 리텐션 브래킷(백월+위아래 암, C형).
        // submesh: 단일메시 Build()는 2(Frame), 분해 Kit은 0.
        static void AddCamKeeper(MeshBuilder b, int submesh, float x, float camCenterY, float lockBarZ, float doorZ)
        {
            float camR      = LockCamSize * 0.5f;
            float backZ     = lockBarZ + camR + 0.005f;   // 캠 바깥(+Z)에 백월
            float backThick = 0.008f;
            float wX        = 0.052f;                      // X 폭(캠 지름보다 약간 넓게)
            float hY        = LockCamSize + 0.024f;        // Y 높이(캠보다 큼)
            float armThickY = 0.010f;
            float armZ0     = doorZ + 0.004f;              // 도어 표면 근처
            float armZc     = (armZ0 + backZ) * 0.5f;
            float armLenZ   = backZ - armZ0;
            // 백월(도어와 평행, 캠 바깥)
            b.AddBox(submesh, new Vector3(x, camCenterY, backZ + backThick * 0.5f), new Vector3(wX, hY, backThick));
            // 상/하 암(백월→도어, 캠 위·아래)
            for (int s = -1; s <= 1; s += 2)
            {
                float ay = camCenterY + s * (hY * 0.5f - armThickY * 0.5f);
                b.AddBox(submesh, new Vector3(x, ay, armZc), new Vector3(wX, armThickY, armLenZ));
            }
        }

        // cam-lock 회전 핸들 — 허브+레버암+수직 그립+도어 캐치(잠금/봉인부). 단일메시·Kit 공유.
        // x=락바 중심X, panelMidY=락바 중앙 높이, lockBarZ=락바 축Z, handleSide=레버 방향(±1).
        static void AddCamLockHandle(MeshBuilder b, int submesh, float x, float panelMidY, float lockBarZ, float handleSide)
        {
            b.AddBox(submesh, new Vector3(x, panelMidY, lockBarZ + 0.008f),
                new Vector3(0.045f, 0.055f, 0.045f));                                              // 허브(단조 칼라, 바를 묾)
            b.AddBox(submesh, new Vector3(x + handleSide * 0.07f, panelMidY - 0.006f, lockBarZ + 0.024f),
                new Vector3(0.10f, 0.024f, 0.024f));                                               // 레버암(허브→그립)
            AddVerticalCylinder(b, submesh,
                new Vector3(x + handleSide * 0.118f, panelMidY - 0.05f, lockBarZ + 0.024f),
                0.088f, 0.013f);                                                                   // 수직 그립(쥐는 봉)
            b.AddBox(submesh, new Vector3(x + handleSide * 0.118f, panelMidY - 0.062f, lockBarZ - 0.004f),
                new Vector3(0.028f, 0.030f, 0.052f));                                              // 도어 캐치(그립 밑동이 물림·봉인부)
        }

        // 도어 리프 — 둘레 프레임(평판 강재) 안에 강판, 세로 3등분해 가로 홈(swage) 3개 삽입.
        // 외측면(doorZ)에 프레임·평판이 닿고 홈 바닥만 리세스. submesh: Build()=1(Door), Kit=0.
        static void BuildFramedDoorLeaf(MeshBuilder b, int submesh, float x0, float yBot, float width, float height, float doorZ)
        {
            const float borderW   = 0.055f;  // 둘레 프레임 레일 폭(평판 강재)
            const float leafThick = 0.02f;   // 리세스 면 뒤 두께
            const float GrooveDepth = 0.020f;  // 가로 홈 깊이(작게)
            float proudZsize = PanelInset + leafThick;    // 도드라진 면 두께(밸리 뒤 ~ 외측면)
            float proudZc    = doorZ - proudZsize * 0.5f;
            float xc     = x0 + width * 0.5f;
            float innerW = width - borderW * 2f;

            // (베이스 슬랩 없음 — 측벽 주름과 동일하게 면 자체가 마감. 슬랩을 두면 홈 바닥과 z-fighting)

            // 1) 평판 둘레 프레임 4변(외측면까지). 좌/우는 상/하 레일 사이만(모서리 중복 회피).
            b.AddBox(submesh, new Vector3(xc, yBot + height - borderW * 0.5f, proudZc), new Vector3(width, borderW, proudZsize));
            b.AddBox(submesh, new Vector3(xc, yBot + borderW * 0.5f, proudZc),          new Vector3(width, borderW, proudZsize));
            float sideH = height - borderW * 2f;
            b.AddBox(submesh, new Vector3(x0 + borderW * 0.5f, yBot + height * 0.5f, proudZc),         new Vector3(borderW, sideH, proudZsize));
            b.AddBox(submesh, new Vector3(x0 + width - borderW * 0.5f, yBot + height * 0.5f, proudZc), new Vector3(borderW, sideH, proudZsize));

            // 평판 면 + 가로 홈 3개(세로 3등분). right=-Y(위→아래 sweep), up=+X(가로 전폭).
            // 외측 법선 +Z, 평탄면=doorZ, 홈 바닥만 GrooveDepth 안쪽.
            float secBot = yBot + borderW;
            float secTop = yBot + height - borderW;
            BuildGroovedDoorPanel(b, submesh,
                origin: new Vector3(x0 + borderW, secTop, doorZ),
                right:  new Vector3(0f, -1f, 0f),
                up:     new Vector3(1f, 0f, 0f),
                length: secTop - secBot,
                span:   innerW,
                grooveDepth: GrooveDepth,
                grooves: 3,
                grooveBottomFrac: 0.20f,   // 밴드 내 홈 바닥 폭 비율(작게)
                grooveSlopeFrac:  0.10f);  // 밴드 내 진입/탈출 경사 폭 비율(작게)
        }

        // 도어 힌지 — 스윙 축(핀 배럴)을 외측 세로 모서리에 두고, 배럴 위/아래에서 스트랩 2장이 빔으로 뻗어 볼트.
        // barrelX=외측 모서리X, beamX=측빔 중심X, yCenter=힌지 높이, doorZ=후면 외측면.
        static void AddDoorHinge(MeshBuilder b, int submesh, float barrelX, float beamX, float yCenter, float doorZ)
        {
            const float barrelR = 0.020f;                 // 핀 배럴 반지름(굵게)
            const float barrelZ = 0.022f;                 // 배럴 축 Z(후면 외측면 바깥)
            const float strapH  = HingeBlockH * 0.42f;    // 위/아래 스트랩 두께

            // 핀 배럴(수직 원기둥 = 스윙 축) — 도어 외측 모서리에
            AddVerticalCylinder(b, submesh,
                bottom: new Vector3(barrelX, yCenter - HingeBlockH * 0.5f, doorZ + barrelZ),
                height: HingeBlockH, radius: barrelR);

            // 스트랩 2장(배럴 위/아래 → 도어 면으로 가로로 뻗어 볼트)
            float strapXc = (barrelX + beamX) * 0.5f;
            float strapW  = Mathf.Abs(beamX - barrelX) + barrelR * 2f;
            for (int s = -1; s <= 1; s += 2)
            {
                float sy = yCenter + s * (HingeBlockH * 0.5f - strapH * 0.5f);
                b.AddBox(submesh,
                    new Vector3(strapXc, sy, doorZ + HingeBlockD * 0.35f),
                    new Vector3(strapW, strapH, HingeBlockD * 0.7f));
            }
        }

        // 수직 원기둥 (락바)
        static void AddVerticalCylinder(MeshBuilder b, int submesh, Vector3 bottom, float height, float radius)
        {
            int sides = LockBarSides;
            int[] bot = new int[sides];
            int[] top = new int[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (float)i / sides * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 pb = bottom + dir * radius;
                Vector3 pt = pb + Vector3.up * height;
                Vector2 uv = new Vector2((float)i / sides, 0f);
                bot[i] = b.AddVertex(pb, dir, uv);
                top[i] = b.AddVertex(pt, dir, new Vector2((float)i / sides, 1f));
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                b.AddQuad(submesh, bot[i], bot[j], top[j], top[i]);
            }
            // 캡(fan): 아래 -Y는 (center,i,j), 위 +Y는 (center,j,i) 순서.
            int botCenter = b.AddVertex(bottom, Vector3.down, new Vector2(0.5f, 0.5f));
            int topCenter = b.AddVertex(bottom + Vector3.up * height, Vector3.up, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                b.AddTriangle(submesh, botCenter, bot[i], bot[j]); // 아래 캡 (-Y)
                b.AddTriangle(submesh, topCenter, top[j], top[i]); // 위 캡 (+Y)
            }
        }

        // MeshBuilder 는 Assets/Shared/MeshBuilder.cs (크레인 생성기와 공유).
    }
}
