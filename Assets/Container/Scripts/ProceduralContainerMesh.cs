using System.Collections.Generic;
using UnityEngine;
using Procedural;   // 공유 MeshBuilder (ProceduralCraneMesh와 중복이던 것을 합침)

namespace ContainerProject
{
    /// <summary>
    /// 20ft Dry 컨테이너 절차적 메시 생성기.
    /// 기본 출력은 VR 미니어처 스케일(1/24, 약 0.252 × 0.102 × 0.108 m),
    /// 메시 중심(0,0,0)이 컨테이너의 가운데(잡기 좋은 위치). Forward: +Z = 도어.
    /// 서브메시: 0=Body, 1=Door, 2=Frame, 3=Castings, 4=Marking(ID/CSC 플레이트 — 본체색 미적용용 분리).
    /// </summary>
    public static partial class ProceduralContainerMesh
    {
        // 기본 출력 스케일: VR 미니어처 (기존 SpawnContainers Std20과 동일 사이즈)
        public const float DefaultMiniatureScale = 1f / 24f;

        // 빌더 내부에서 사용하는 ISO 668 실측 (m). 마지막에 일괄 스케일됨.
        // 기본값 = 20ft Std (22G1). BuildSized() 로 다른 사이즈도 빌드 가능.
        public static float Length = 6.058f;
        public static float Width  = 2.438f;
        public static float Height = 2.591f;

        // 표준 사이즈 프리셋
        public const float Length20ft = 6.058f;
        public const float Length40ft = 12.192f;
        public const float HeightStd  = 2.591f;  // 8'6"
        public const float HeightHC   = 2.896f;  // 9'6" (High Cube)
        public const float StdWidth   = 2.438f;

        // 프레임 / 캐스팅 / 패널 치수
        const float CornerCastW = 0.178f;
        const float CornerCastH = 0.135f;
        const float CornerCastTopH = 0.135f;  // = CornerCastH(대칭) — 상단 캐스팅 top이 정확히 Height에 맞아 외고 = 2.591 ISO 표준(스택 정합).
        const float CornerCastD = 0.162f;
        // ISO 1161 코너 캐스팅 구멍 (외측 3면) — 가로 장공 124.5 × 63.5mm.
        //   면별 long/short 매핑은 AddCornerCastingWithHoles 에서 처리(상면·측면 long축 = 컨테이너 길이/폭).
        const float CastHoleLong  = 0.1245f;
        const float CastHoleShort = 0.0635f;
        const float CastWallThick = 0.018f;  // 벽 두께 — 구멍이 안쪽으로 들어가는 recess 깊이
        const float RailH       = 0.092f;
        const float CornerPostW = 0.098f;
        // 패널 base가 컨테이너 외측에서 안쪽으로 들어간 깊이.
        // corrugated 외측 평면(+CorrDepth)이 컨테이너 외측면과 일치하도록 = CorrDepth와 같게.
        // 이러면 corrugated 산이 코너 캐스팅·포스트와 같은 평면 → 외관 시 틈이 사라짐.
        const float PanelInset  = 0.028f;

        // 주름판 (vertical corrugation)
        // 실측 ISO 주름은 바깥 크라운(flatOut)이 안쪽 밸리(flatIn)보다 좁은 비대칭 사다리꼴 →
        //   정면광에서 산이 더 또렷한 그림자 라인을 만든다. period(=fIn+slope+fOut+slope)는 0.20 유지(산 개수 불변).
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

        /// <summary>
        /// 절차적 메시 생성.
        /// 기본 출력: 미니어처 스케일(1/24) + 중심 피봇 + X축이 긴 방향(도어=+X).
        /// 이는 기존 SpawnContainers Std20과 동일 좌표계.
        /// 현재 Length/Width/Height 상수 기반 — 다른 사이즈는 BuildSized() 사용.
        /// </summary>
        /// <summary>
        /// 감축 단계. 0 = 원본(기본, 이 값이 SSOT 동작). 1 이상은 배경 프롭용 저폴리.
        /// 실루엣·외곽 치수·서브메시 구성은 유지하고 화면에서 1 px 미만인 요소만 뺀다.
        ///   1: 골판/도어 홈을 평판으로(외측 크라운 평면 유지) · 언더프레임 생략
        /// ★ 판정 근거는 문서/컨테이너_규격.md Part 5 §11.9 — 배경 화물 허용치가 대당 1,100~3,100 tris 다.
        /// ★ 정점 포맷도 같이 줄여야 효과가 난다(§11.7: 정점 12B→60B 에서 처리율 4배 하락).
        /// </summary>
        public static int LodLevel = 0;

        public static Mesh Build(
            string meshName = "Container_20ft_Procedural",
            float scale = DefaultMiniatureScale,
            bool centerPivot = true,
            bool xIsLength = true)
        {
            var b = new MeshBuilder();

            BuildCornerCastings(b);
            BuildFrame(b);
            BuildBodyPanels(b);
            BuildRoof(b);
            BuildFloor(b);
            if (LodLevel < 1) BuildUnderframe(b);   // 하부 구조 — 갑판 적재 시 영구 은폐
            BuildDoors(b);

            var mesh = b.ToMesh(meshName, tangents: LodLevel < 1);
            ApplyTransform(mesh, scale, centerPivot, xIsLength);
            return mesh;
        }

        /// <summary>
        /// 감축 단계를 지정해 빌드. <see cref="BuildSized"/> 와 같은 방식으로 정적 상태를 복구한다.
        /// </summary>
        public static Mesh BuildSizedLod(
            float length, float width, float height, int lodLevel,
            string meshName = "Container_Procedural_LOD",
            float scale = DefaultMiniatureScale,
            bool centerPivot = true,
            bool xIsLength = true)
        {
            int saved = LodLevel;
            LodLevel = lodLevel;
            try
            {
                return BuildSized(length, width, height, meshName, scale, centerPivot, xIsLength);
            }
            finally
            {
                LodLevel = saved;
            }
        }

        /// <summary>
        /// 임의 사이즈로 컨테이너 빌드. 표준 사이즈는 Length20ft/Length40ft, HeightStd/HeightHC 상수 사용.
        /// 내부적으로 정적 Length/Width/Height 를 잠시 바꿔서 Build() 호출 후 복구.
        /// </summary>
        public static Mesh BuildSized(
            float length, float width, float height,
            string meshName = "Container_Procedural",
            float scale = DefaultMiniatureScale,
            bool centerPivot = true,
            bool xIsLength = true)
        {
            float savedL = Length, savedW = Width, savedH = Height;
            Length = length; Width = width; Height = height;
            try
            {
                return Build(meshName, scale, centerPivot, xIsLength);
            }
            finally
            {
                Length = savedL; Width = savedW; Height = savedH;
            }
        }

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
                // LOD1+ 는 탄젠트를 만들지 않는다 — 배경 화물 재질에 노멀맵이 없어 쓰이지 않는 데이터이고,
                //   정점이 48 B → 32 B 로 줄어 처리율 구간이 달라진다(문서 §11.7 실측: 44 B 1.82 → 28 B 2.60 G tris/s).
                if (LodLevel < 1) mesh.RecalculateTangents();
            }
            mesh.RecalculateBounds();
        }

        // 코너 캐스팅
        static void BuildCornerCastings(MeshBuilder b)
        {
            float hx = Width  * 0.5f;
            float hz = Length * 0.5f;
            // 8개 캐스팅: 위(+Y top) / 아래(0 bottom), 4 모서리. 외측 3면에 ISO 1161 구멍.
            // 상단은 CornerCastTopH(=CornerCastH 0.135, 대칭) — 아래 면 Height-CornerCastH, top이 정확히 Height(외고 2.591).
            // 하단은 CornerCastH(0.135) 유지.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            for (int sy = 0; sy <= 1; sy++)
            {
                float x = sx * (hx - CornerCastW * 0.5f);
                float z = sz * (hz - CornerCastD * 0.5f);
                bool isTop = sy == 1;
                float castH = isTop ? CornerCastTopH : CornerCastH;
                float y = isTop
                    ? Height - CornerCastH + castH * 0.5f  // 상단: 바닥 Y=Height-CornerCastH 고정, 위로 castH만큼
                    : castH * 0.5f;                        // 하단: Y=0 부터 castH 만큼
                AddCornerCastingWithHoles(b, submesh: 3,
                    center: new Vector3(x, y, z),
                    size:   new Vector3(CornerCastW, castH, CornerCastD),
                    outX: sx, outZ: sz, outYUp: isTop);
            }
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

        // 사각형 면 (c00→c10→c11→c01 CCW from +normal) 에 사각 구멍을 뚫고,
        // holeDepth 만큼 안쪽으로 들어간 뒤 닫는 recess 생성.
        // holeRightFrac/holeUpFrac: 구멍 크기 (face dimension 대비 0~1, 중앙 정렬)
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

        // 4-vertex flat quad (CCW from +normal). 기존 MeshBuilder.AddFace 와 동일 기능을 외부에서 호출 가능하게 노출.
        static void AddFlatQuad(MeshBuilder mb, int submesh,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int ia = mb.AddVertex(a, normal, new Vector2(0f, 0f));
            int ib = mb.AddVertex(b, normal, new Vector2(1f, 0f));
            int ic = mb.AddVertex(c, normal, new Vector2(1f, 1f));
            int id = mb.AddVertex(d, normal, new Vector2(0f, 1f));
            mb.AddQuad(submesh, ia, ib, ic, id);
        }

        // 프레임
        static void BuildFrame(MeshBuilder b)
        {
            float hx = Width  * 0.5f;
            float hz = Length * 0.5f;

            // Bottom side rails (좌/우 길이 방향) — 지게차 포켓 분할/터널 포함(공유 헬퍼)
            float bottomRailY = CornerCastH * 0.5f;
            float railZSpan   = Length - CornerCastD * 2f;
            float endRailXSpan= Width  - CornerCastW * 2f;
            AddBottomSideRailsWithForkPockets(b, 2);
            // Top side rails
            float topRailY = Height - CornerCastH * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                b.AddBox(2,
                    center: new Vector3(sx * (hx - CornerPostW * 0.5f), topRailY, 0f),
                    size:   new Vector3(CornerPostW, RailH, railZSpan));
            }
            // Bottom end rails (전/후 폭 방향)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                b.AddBox(2,
                    center: new Vector3(0f, bottomRailY, sz * (hz - CornerPostW * 0.5f)),
                    size:   new Vector3(endRailXSpan, RailH, CornerPostW));
            }
            // Top end rails (header)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                b.AddBox(2,
                    center: new Vector3(0f, topRailY, sz * (hz - CornerPostW * 0.5f)),
                    size:   new Vector3(endRailXSpan, RailH, CornerPostW));
            }
            // Corner posts (4개, 수직). 두 코너 캐스팅 사이를 정확히 채우도록 컨테이너 정중앙에 배치.
            float postY = Height * 0.5f;
            float postHeight = Height - CornerCastH * 2f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                b.AddBox(2,
                    center: new Vector3(sx * (hx - CornerPostW * 0.5f), postY, sz * (hz - CornerPostW * 0.5f)),
                    size:   new Vector3(CornerPostW, postHeight, CornerPostW));
            }
        }

        // 바닥 사이드 레일 + 지게차 포켓(공유)
        //   단일메시 BuildFrame(submesh 2)·분해 Kit 모두 호출해 동일 형상 보장.
        //   20ft급(Length<9m)이면 사이드 레일을 포켓 구간에서 3분할하고, X 전관통 터널(상·하판+Z양벽)을 추가한다.
        //   포켓 Z위치·폭은 언더프레임 하우징(ForkPocketZ/ForkPocketWidth)과 동일, 개구 높이는 레일상단~하우징 바닥(~109mm).
        static void AddBottomSideRailsWithForkPockets(MeshBuilder b, int submesh)
        {
            float hx          = Width * 0.5f;
            float bottomRailY = CornerCastH * 0.5f;
            float railZSpan   = Length - CornerCastD * 2f;
            bool  hasPockets  = Length < 9.0f;
            float pHalf       = ForkPocketWidth * 0.5f;
            float halfRailZ   = railZSpan * 0.5f;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                float cx = sx * (hx - CornerPostW * 0.5f);
                if (hasPockets)
                {
                    float[] zb = { -halfRailZ, -ForkPocketZ - pHalf, -ForkPocketZ + pHalf,
                                    ForkPocketZ - pHalf,  ForkPocketZ + pHalf,  halfRailZ };
                    var segs = new (float a, float b)[] { (zb[0], zb[1]), (zb[2], zb[3]), (zb[4], zb[5]) };
                    foreach (var s in segs)
                    {
                        float len = s.b - s.a;
                        if (len <= 0.001f) continue;
                        b.AddBox(submesh, new Vector3(cx, bottomRailY, (s.a + s.b) * 0.5f),
                                 new Vector3(CornerPostW, RailH, len));
                    }
                }
                else
                {
                    b.AddBox(submesh, new Vector3(cx, bottomRailY, 0f),
                             new Vector3(CornerPostW, RailH, railZSpan));
                }
            }

            if (!hasPockets) return;
            // 포켓 터널 — X 전관통, 상·하판 + Z 양벽(X양끝 개방)
            //   개구는 바닥 사이드 레일과 동일 높이(상·하단 일치) → Mid 레일과 턱 없이 정렬.
            //   더 깊은 보강 하우징은 언더프레임 ForkPocketDepth 박스가 별도로 표현(아래로 매달림).
            float tunXSpan   = hx * 2f;                       // = Width (전관통)
            float pocketTopY = bottomRailY + RailH * 0.5f;    // 레일 상단
            float pocketBotY = bottomRailY - RailH * 0.5f;    // 레일 하단(= Mid 레일 바닥과 일치)
            float pocketH    = pocketTopY - pocketBotY;       // = RailH(0.092)
            float pocketCy   = (pocketTopY + pocketBotY) * 0.5f;
            float topPlateY  = pocketTopY - ForkPlateT * 0.5f;
            float botPlateY  = pocketBotY + ForkPlateT * 0.5f;
            for (int pz = -1; pz <= 1; pz += 2)
            {
                float zc = pz * ForkPocketZ;
                b.AddBox(submesh, new Vector3(0f, topPlateY, zc), new Vector3(tunXSpan, ForkPlateT, ForkPocketWidth));
                b.AddBox(submesh, new Vector3(0f, botPlateY, zc), new Vector3(tunXSpan, ForkPlateT, ForkPocketWidth));
                b.AddBox(submesh, new Vector3(0f, pocketCy, zc - pHalf + ForkPlateT * 0.5f),
                         new Vector3(tunXSpan, pocketH, ForkPlateT));
                b.AddBox(submesh, new Vector3(0f, pocketCy, zc + pHalf - ForkPlateT * 0.5f),
                         new Vector3(tunXSpan, pocketH, ForkPlateT));
            }
        }

        // 본체 패널 (좌/우/전)
        static void BuildBodyPanels(MeshBuilder b)
        {
            // 레일 중심 Y = CornerCastH * 0.5f, 높이 = RailH.
            // 패널이 상/하 레일 안쪽 면과 맞닿게 — 틈 제거.
            float panelTop    = Height - CornerCastH * 0.5f - RailH * 0.5f;
            float panelBottom = CornerCastH * 0.5f + RailH * 0.5f;
            float panelHeight = panelTop - panelBottom;

            // PanelInset은 깊이 방향(외측면에서 안쪽)에만; 길이/폭 방향은 전체 dimension 사용해야
            // 코너 포스트 안쪽 면까지 패널이 닿음.
            float depthInsetX = Width  * 0.5f - PanelInset;  // 좌/우 패널 base X (절대값)
            float depthInsetZ = Length * 0.5f - PanelInset;  // 전면 패널 base Z (절대값)
            float halfX = Width  * 0.5f;
            float halfZ = Length * 0.5f;

            // 코너 포스트 안쪽 면에 패널이 거의 맞닿게 (틈 최소화)
            const float postInset = 0.002f;

            // 좌측면 (x = -depthInsetX, 법선 -X). right=+Z, up=+Y → Cross=-X = outward
            BuildCorrugatedPanel(b, submesh: 0,
                origin: new Vector3(-depthInsetX, panelBottom, -halfZ + CornerPostW + postInset),
                right:  new Vector3(0f, 0f, 1f),
                up:     new Vector3(0f, 1f, 0f),
                width:  Length - (CornerPostW + postInset) * 2f,
                height: panelHeight,
                depth:  CorrDepth);

            // 우측면 (x = +depthInsetX, 법선 +X). right=-Z, up=+Y → Cross=+X = outward
            BuildCorrugatedPanel(b, submesh: 0,
                origin: new Vector3(depthInsetX, panelBottom, halfZ - CornerPostW - postInset),
                right:  new Vector3(0f, 0f, -1f),
                up:     new Vector3(0f, 1f, 0f),
                width:  Length - (CornerPostW + postInset) * 2f,
                height: panelHeight,
                depth:  CorrDepth);

            // 전면 고정벽 (z = -depthInsetZ, 법선 -Z). right=-X, up=+Y → Cross=-Z = outward
            BuildCorrugatedPanel(b, submesh: 0,
                origin: new Vector3(halfX - CornerPostW - postInset, panelBottom, -depthInsetZ),
                right:  new Vector3(-1f, 0f, 0f),
                up:     new Vector3(0f, 1f, 0f),
                width:  Width - (CornerPostW + postInset) * 2f,
                height: panelHeight,
                depth:  CorrDepth);
        }

        /// <summary>
        /// 수직 주름판(corrugated panel) 한 장을 생성.
        /// origin = 좌하단 모서리, right = 폭 방향, up = 높이 방향.
        /// outward 법선은 Cross(right, up) 방향이어야 하므로 호출자가 그렇게 right/up을 선택해야 함.
        /// </summary>
        static void BuildCorrugatedPanel(MeshBuilder b, int submesh,
            Vector3 origin, Vector3 right, Vector3 up,
            float width, float height, float depth)
        {
            Vector3 outDir = Vector3.Cross(right, up).normalized;

            // LOD1+ : 골판을 외측 크라운 평면(+depth)의 평판 1장으로 대체.
            //   크라운 평면을 쓰는 이유는 그 면이 코너 캐스팅·포스트와 같은 평면이라(상단 주석 참조)
            //   실루엣과 이웃 부재와의 정합이 그대로 유지되기 때문이다. 골 깊이 28 mm 는
            //   전환거리 33 m 에서 0.03 px 라 보이지 않는다(문서 §11.9).
            if (LodLevel >= 1)
            {
                Vector3 o = origin + outDir * depth;
                int f0 = b.AddVertex(o,                          outDir, new Vector2(0f, 0f));
                int f1 = b.AddVertex(o + right * width,          outDir, new Vector2(1f, 0f));
                int f2 = b.AddVertex(o + right * width + up * height, outDir, new Vector2(1f, 1f));
                int f3 = b.AddVertex(o + up * height,            outDir, new Vector2(0f, 1f));
                b.AddQuad(submesh, f0, f1, f2, f3);
                return;
            }

            // 한 주기 = flatIn + slope + flatOut + slope
            float period = CorrFlatIn + CorrSlope + CorrFlatOut + CorrSlope;
            int periods = Mathf.Max(1, Mathf.RoundToInt(width / period));
            float actualPeriod = width / periods;
            // 비율 유지하면서 폭에 맞춤
            float scale = actualPeriod / period;
            float fIn  = CorrFlatIn  * scale;
            float fOut = CorrFlatOut * scale;
            float slp  = CorrSlope   * scale;

            // 단면을 따라 (x = 진행 거리, d = 깊이) 노드 생성. 각 노드는 (offset, outwardOffset, normal).
            // 4 segments per period:
            //   [0] flatIn   (d=0,     normal=outDir)
            //   [1] slope ↑  (d=depth, normal=outDir tilted)
            //   [2] flatOut  (d=depth, normal=outDir)
            //   [3] slope ↓  (d=0,     normal=outDir tilted)
            // 마지막 폐쇄 노드 추가 (안쪽으로 복귀)

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
            // 마지막 폐쇄 (다음 주기 시작점이 안쪽 평면이므로 자연스럽게 종료)

            // 위/아래 두 줄의 vertex 생성, segment마다 quad 1개
            // along 정규화 → U, height 정규화 → V
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
            // segment 단위로 quad. (bottom_i, bottom_i+1, top_i+1, top_i) 순서로 winding하면
            // normal이 Cross(right, up) 방향 = outDir로 자동 정렬됨.
            for (int i = 0; i < profile.Count - 1; i++)
            {
                b.AddQuad(submesh, bottomIdx[i], bottomIdx[i + 1], topIdx[i + 1], topIdx[i]);
            }
        }

        // 도어 면 전용: 평평한 외측 면(z=outer 평판)에 큰 가로 홈 grooves개를 균등 배치(세로 3등분 등).
        //   각 홈은 사다리꼴 단면(평탄 외측 → 경사 진입 → 평탄 바닥(grooveDepth만큼 안쪽) → 경사 탈출 → 평탄 외측).
        //   홈 중심은 길이를 grooves등분한 각 밴드의 중앙(=맨위/중간/맨아래). 단면은 'right' 진행축으로 흐르고
        //   판은 'up' 축 전폭을 덮어 가로로 흐른다. 외측 법선 = Cross(right, up).
        static void BuildGroovedDoorPanel(MeshBuilder b, int submesh,
            Vector3 origin, Vector3 right, Vector3 up,
            float length, float span, float grooveDepth,
            int grooves, float grooveBottomFrac, float grooveSlopeFrac)
        {
            Vector3 outDir = Vector3.Cross(right, up).normalized;

            // LOD1+ : 도어 가로 홈을 평판 1장으로. 홈 깊이는 골판보다 얕아 더 일찍 사라진다.
            if (LodLevel >= 1)
            {
                int f0 = b.AddVertex(origin,                              outDir, new Vector2(0f, 0f));
                int f1 = b.AddVertex(origin + right * length,             outDir, new Vector2(1f, 0f));
                int f2 = b.AddVertex(origin + right * length + up * span, outDir, new Vector2(1f, 1f));
                int f3 = b.AddVertex(origin + up * span,                  outDir, new Vector2(0f, 1f));
                b.AddQuad(submesh, f0, f1, f2, f3);
                return;
            }

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

        // 지붕 (corrugated)
        static void BuildRoof(MeshBuilder b)
        {
            // ISO 컨테이너 지붕: 산/골이 폭(X) 방향으로 길게 흘러가고, 길이(Z) 방향으로 산/골 반복 (가로 줄무늬).
            // 천장 산이 rail top 보다 5mm 아래에 위치 — 끝 레일이 천장 끝을 덮어줘서 앞뒤 마감이 깔끔.
            float hx = Width  * 0.5f;
            float hz = Length * 0.5f;
            float railTopY = Height - CornerCastH * 0.5f + RailH * 0.5f;
            float baseY = railTopY - RoofCorrDepth - 0.005f;  // 골 + 깊이 + 5mm 여유 = 산이 rail top 보다 5mm 낮음

            // 산/골이 폭(X) 방향으로 길게 흐름 — 문에서 봤을 때 가로 줄무늬로 보임.
            // right=+Z (코르게이션 프로파일이 길이 방향으로 진행), up=+X (각 산이 -X→+X로 길게 흐름)
            // outDir = Cross(+Z, +X) = +Y (지붕은 위로 향함)
            BuildCorrugatedPanel(b, submesh: 0,
                origin: new Vector3(-hx, baseY, -hz),
                right:  new Vector3(0f, 0f, 1f),
                up:     new Vector3(1f, 0f, 0f),
                width:  Length,
                height: Width,
                depth:  RoofCorrDepth);
        }

        // 바닥
        static void BuildFloor(MeshBuilder b)
        {
            // 외측 경계를 코너 캐스팅 외측면까지 확장
            float hx = Width  * 0.5f;
            float hz = Length * 0.5f;

            // 외측 바닥 (normal -Y, 컨테이너 아래에서 보임)
            // 천장이 rail top 5mm 아래로 들어간 것과 대칭 — 바닥도 rail bottom 5mm 위로 올림.
            // 끝 레일/사이드 레일이 외측에서 바닥 가장자리를 덮어줌 (앞뒤/좌우 마감).
            float yOut = CornerCastH * 0.5f - RailH * 0.5f + 0.005f;  // = railBottomY + 5mm
            int a = b.AddVertex(new Vector3(-hx, yOut, -hz), Vector3.down, new Vector2(0f, 0f));
            int b1 = b.AddVertex(new Vector3( hx, yOut, -hz), Vector3.down, new Vector2(1f, 0f));
            int c1 = b.AddVertex(new Vector3( hx, yOut,  hz), Vector3.down, new Vector2(1f, 1f));
            int d1 = b.AddVertex(new Vector3(-hx, yOut,  hz), Vector3.down, new Vector2(0f, 1f));
            // (a, b, c, d) winding → normal = -Y
            b.AddQuad(0, a, b1, c1, d1);

            // 내측 바닥 (normal +Y, 도어 열렸을 때 내부에서 보임)
            // panelBottom과 동일한 높이로 측면 패널 하단과 일치
            float yIn = CornerCastH * 0.5f + RailH * 0.5f;
            int e = b.AddVertex(new Vector3(-hx, yIn, -hz), Vector3.up, new Vector2(0f, 0f));
            int f = b.AddVertex(new Vector3( hx, yIn, -hz), Vector3.up, new Vector2(1f, 0f));
            int g = b.AddVertex(new Vector3( hx, yIn,  hz), Vector3.up, new Vector2(1f, 1f));
            int h = b.AddVertex(new Vector3(-hx, yIn,  hz), Vector3.up, new Vector2(0f, 1f));
            // 역 winding → normal = +Y
            b.AddQuad(0, e, h, g, f);
        }

        // 바닥 하부 구조 (언더프레임)
        // ISO1496 정규 부재: 바텀 사이드레일 사이를 가로지르는 횡단 크로스멤버가 바닥판을 받친다.
        //   현재 증분 = 크로스멤버(횡단 리브)만. 포크포켓·구스넥 터널은 후속 증분(스크린샷 수렴 후).
        //   바닥 외측판(yOut) 바로 아래에 매달리는 리브 → 밑에서 보면 가로 리브가 줄지어 보임.
        //   서브메시 2(Frame 회색). 코너 캐스팅 밑면(y=0)보다 위에 머물러 컨테이너는 여전히 캐스팅으로 안착.
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
            // 포켓 구간을 가로지르는 크로스멤버는 제거(지게차 타인 인입 경로 확보).
            //   |z - ForkPocketZ| < ForkPocketWidth/2 + CrossMemberThick/2 이면 개구를 침범 → skip.
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

        // 캠킵 키퍼 — 상/하 캠이 도어 헤더/실에 물려 도어를 닫아주는 ㄷ자 리텐션 브래킷.
        //   캠 바깥(+Z)에 백월 + 캠 위·아래 두 암 = C형. 캠(원기둥)이 도어면과 백월 사이에 들어앉아 회전 잠금.
        //   submesh: 단일메시 Build()는 2(Frame), 분해 Kit은 파트당 단일 머티라 0.
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

        // cam-lock 회전 핸들 — 허브(바 클램프)+레버암+수직 그립+도어 캐치(잠금/봉인부).
        //   단일메시 Build()와 분해 Kit가 좌표·치수까지 공유(형상 단일화). 정점 생성 순서: 허브→레버암→그립→캐치.
        //   x=락바 중심 X, panelMidY=락바 중앙 높이, lockBarZ=락바 축 Z, handleSide=레버 뻗는 방향(±1, 한 도어 두 바는 동일 외측).
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

        // 도어 리프 — 실물(ISO 드라이 컨테이너 후면도어) 레퍼런스 형태:
        //   평판 강재 둘레 프레임(세로 내·외측 레일 + 가로 상·하 레일) 안에 평평한 강판,
        //   그 면을 세로 3등분해 큰 가로 홈(swage) 3개를 맨위·중간·맨아래에 눌러 넣는다.
        //   외측면(doorZ)에 프레임·평판 면이 닿고, 홈 바닥만 GrooveDepth만큼 안쪽으로 리세스.
        //   submesh: 단일메시 Build()는 1(Door), 분해 Kit은 0.
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

            // 2) 평판 면 + 큰 가로 홈 3개(세로 3등분: 맨위/중간/맨아래).
            //    right=-Y(단면이 Y로 흐름, origin=상단에서 아래로 sweep)·up=+X(판이 가로 전폭으로 흐름)
            //    → 외측 법선 +Z, 평탄 면은 doorZ, 홈 바닥만 GrooveDepth 안쪽.
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

        // 도어 힌지 1개 — 스윙 축(핀 배럴)을 도어 외측 세로 모서리(=코너 포스트 라인)에 두고,
        //   배럴 위/아래에서 스트랩 2장이 도어 면(빔)으로 뻗어 볼트되는 실물 형태.
        //   barrelX=도어 외측 모서리 X, beamX=측면 빔 중심 X(스트랩이 닿는 안쪽), yCenter=힌지 높이, doorZ=후면 외측면.
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

        // 도어 (후면)
        static void BuildDoors(MeshBuilder b)
        {
            // BuildBodyPanels와 동일 — 상/하 레일 안쪽 면에 맞춤.
            float panelTop    = Height - CornerCastH * 0.5f - RailH * 0.5f;
            float panelBottom = CornerCastH * 0.5f + RailH * 0.5f;
            float panelHeight = panelTop - panelBottom;
            float panelMidY   = (panelTop + panelBottom) * 0.5f;

            float halfX = Width * 0.5f;
            float doorZ = Length * 0.5f; // 후면 outer face (+Z); 락바/힌지/플레이트 기준점

            const float postInset = 0.002f;
            float fullWidth = Width - (CornerPostW + postInset) * 2f;
            float doorWidth = (fullWidth - DoorGap) * 0.5f;
            float doorStartLeft = -halfX + CornerPostW + postInset;
            // 코르게이션 폭 = 도어 폭 - 측면 빔 폭 (각 도어 외측에 빔 1개)
            float corrWidth = doorWidth - SideBeamW;

            // 도어 측면 빔 (각 도어 외측 모서리의 평평한 세로 띠)
            float leftBeamX  = doorStartLeft + SideBeamW * 0.5f;
            float rightBeamX = doorStartLeft + fullWidth - SideBeamW * 0.5f;
            b.AddBox(1,
                center: new Vector3(leftBeamX, panelMidY, doorZ - PanelInset * 0.5f),
                size:   new Vector3(SideBeamW, panelHeight, PanelInset));
            b.AddBox(1,
                center: new Vector3(rightBeamX, panelMidY, doorZ - PanelInset * 0.5f),
                size:   new Vector3(SideBeamW, panelHeight, PanelInset));

            // 도어 리프 = 프레임 패널(A안): 돋은 테두리 + 중간 레일 + 상/하 리세스 패널 (각 도어 빔 옆 corrWidth)
            BuildFramedDoorLeaf(b, submesh: 1, doorStartLeft + SideBeamW, panelBottom, corrWidth, panelHeight, doorZ);
            BuildFramedDoorLeaf(b, submesh: 1, doorStartLeft + doorWidth + DoorGap, panelBottom, corrWidth, panelHeight, doorZ);

            // 락바 + 캠(원기둥) + 손잡이 + 가드 + 마운트 브래킷
            float lockBarZ = doorZ + 0.040f;
            float camRadius = LockCamSize * 0.5f;
            for (int doorSide = 0; doorSide < 2; doorSide++)
            {
                // 락바는 코르게이션 영역에 분산 — 빔 위가 아니라 코르게이션 위에 위치
                float corrStartX = (doorSide == 0)
                    ? doorStartLeft + SideBeamW                    // 좌측: 빔 뒤
                    : doorStartLeft + doorWidth + DoorGap;         // 우측: 내측 시작
                for (int bar = 0; bar < LockBarsPerDoor; bar++)
                {
                    float t = (bar + 1f) / (LockBarsPerDoor + 1f);
                    float x = corrStartX + corrWidth * t;

                    // 수직 락바
                    AddVerticalCylinder(b, submesh: 2,
                        bottom: new Vector3(x, panelBottom, lockBarZ),
                        height: panelHeight,
                        radius: LockBarDiameter * 0.5f);

                    // 상/하 캠 (원기둥 — 락바보다 굵음)
                    AddVerticalCylinder(b, submesh: 2,
                        bottom: new Vector3(x, panelBottom, lockBarZ),
                        height: LockCamSize,
                        radius: camRadius);
                    AddVerticalCylinder(b, submesh: 2,
                        bottom: new Vector3(x, panelTop - LockCamSize, lockBarZ),
                        height: LockCamSize,
                        radius: camRadius);

                    // 캠킵 키퍼 (상/하 캠이 헤더·실에 물리는 ㄷ자 리텐션 브래킷)
                    AddCamKeeper(b, 2, x, panelTop - LockCamSize * 0.5f, lockBarZ, doorZ);
                    AddCamKeeper(b, 2, x, panelBottom + LockCamSize * 0.5f, lockBarZ, doorZ);

                    // cam-lock 회전 핸들 (락바 중앙 — 한 도어의 두 바는 같은 외측 방향)
                    //   허브+레버암+수직 그립+도어 캐치. 단일메시·Kit 공유 헬퍼(좌표·치수 단일화).
                    float handleSide = (doorSide == 0) ? -1f : 1f;
                    AddCamLockHandle(b, 2, x, panelMidY, lockBarZ, handleSide);

                    // 마운트 브래킷 (락바를 도어 표면에 잡아주는 클램프) — 캠과 손잡이 사이에 2개
                    float bracketCenterZ = doorZ + LockBracketD * 0.5f;
                    for (int br = 0; br < LockBracketsPerBar; br++)
                    {
                        float bt = (br + 1f) / (LockBracketsPerBar + 1f);
                        float by = panelBottom + panelHeight * bt;
                        b.AddBox(2,
                            center: new Vector3(x, by, bracketCenterZ),
                            size:   new Vector3(LockBracketW, LockBracketH, LockBracketD));
                    }
                }
            }

            // 힌지 (도어 외측 세로 모서리 = 스윙 축에 배럴+스트랩)
            for (int doorSide = 0; doorSide < 2; doorSide++)
            {
                float edgeX = (doorSide == 0) ? doorStartLeft : doorStartLeft + fullWidth;  // 도어 외측 모서리
                float beamX = (doorSide == 0) ? leftBeamX : rightBeamX;
                for (int h = 0; h < HingesPerDoor; h++)
                {
                    float t = (h + 1f) / (HingesPerDoor + 1f);
                    float y = panelBottom + panelHeight * t;
                    AddDoorHinge(b, 2, edgeX, beamX, y, doorZ);
                }
            }

            // 도어 헤더 (얇게). X는 두 코너 캐스팅 사이 (≠ 도어 폭 fullWidth — 그러면 캐스팅 안으로 파고듦).
            // Z는 캐스팅 외측면(doorZ) 안쪽에 배치 — 외측면 밖으로 튀어나오지 않도록.
            float headerWidth = Width - CornerCastW * 2f;
            b.AddBox(2,
                center: new Vector3(0f, panelTop + DoorHeaderHeight * 0.5f, doorZ - DoorHeaderDepth * 0.5f),
                size:   new Vector3(headerWidth, DoorHeaderHeight, DoorHeaderDepth));

            // ID Plate (우측 도어에 큰 사각 패널 — 컨테이너 번호용)
            //   0.78f로 우측 코르게이션 바깥쪽 빈 구간(우측 락바~측면빔 사이)에 배치해 로드 회피.
            float idPlateX = doorStartLeft + doorWidth + DoorGap + doorWidth * 0.78f;
            float idPlateY = panelTop - IdPlateH * 0.5f - 0.06f;
            b.AddBox(4,   // submesh 4 = Marking — 본체 선사색이 안 입혀지게 분리(검정 번호 대비 보존)
                center: new Vector3(idPlateX, idPlateY, doorZ + PlateOut * 0.5f),
                size:   new Vector3(IdPlateW, IdPlateH, PlateOut));

            // CSC Plate (좌측 도어 하단 — 안전 인증판)
            //   패널 높이 15% 지점(~0.47m)으로 상향.
            float cscX = doorStartLeft + doorWidth * 0.5f;
            float cscY = panelBottom + panelHeight * 0.15f;
            b.AddBox(4,   // submesh 4 = Marking
                center: new Vector3(cscX, cscY, doorZ + PlateOut * 0.5f),
                size:   new Vector3(CscPlateW, CscPlateH, PlateOut));
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
            // 캡 (단순 fan). dir_i = (cos(i), 0, sin(i)), i가 증가하면 시계반대로 도는데,
            // 아래 캡은 normal=-Y여야 하므로 (center, i, j) 순서, 위 캡은 +Y이므로 (center, j, i) 순서.
            int botCenter = b.AddVertex(bottom, Vector3.down, new Vector2(0.5f, 0.5f));
            int topCenter = b.AddVertex(bottom + Vector3.up * height, Vector3.up, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                b.AddTriangle(submesh, botCenter, bot[i], bot[j]); // 아래 캡 (-Y)
                b.AddTriangle(submesh, topCenter, top[j], top[i]); // 위 캡 (+Y)
            }
        }

        // MeshBuilder는 Assets/Shared/MeshBuilder.cs(namespace Procedural)로 이동 — 크레인 생성기와 공유.
    }
}
