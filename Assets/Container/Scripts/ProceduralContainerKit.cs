using System;
using UnityEngine;
using Procedural;   // 공유 MeshBuilder

namespace ContainerProject
{
    /// <summary>
    /// 분해형(파트 분리) 20ft 컨테이너 생성기.
    /// 기존 Build()(단일 메시·4 서브메시)와 별개로, 각 부품을 독립 GameObject
    /// (자체 MeshFilter/MeshRenderer/Collider)로 쪼개 계층을 만든다.
    ///
    /// 좌표·치수 상수와 형상 헬퍼(AddCornerCastingWithHoles / BuildCorrugatedPanel /
    /// AddVerticalCylinder / BuildFloor / ApplyTransform)는 메인 파셜과 100% 공유 →
    /// 분해본이 단일메시본과 '동일 형상'으로 조립된다. 배치식(loop)만 여기서 미러한다.
    ///
    /// ⚠ 기존 컨테이너 프리팹/메시 에셋/Build() 경로는 일절 건드리지 않는다(별도 신규 생성물).
    /// </summary>
    public static partial class ProceduralContainerMesh
    {
        /// <summary>분해 파트에 부위별로 입힐 머티리얼(메인 프리팹과 동일 4종).</summary>
        public struct KitMaterials
        {
            public Material body;
            public Material door;
            public Material frame;
            public Material castings;
            public Material marking;   // ID/CSC 플레이트 전용(옅은 무광 흰 — 본체색 미적용). null이면 body 폴백.
        }

        /// <summary>
        /// 임의 사이즈 분해형 컨테이너 — BuildSized의 Kit 버전. 정적 Length/Width/Height를 잠시 바꿔 BuildKit 호출 후 복구.
        /// </summary>
        public static GameObject BuildKitSized(
            float length, float width, float height,
            KitMaterials mats,
            string rootName = "Container_Kit",
            float scale = DefaultMiniatureScale,
            bool centerPivot = false,
            bool xIsLength = true,
            bool addColliders = true)
        {
            float savedL = Length, savedW = Width, savedH = Height;
            Length = length; Width = width; Height = height;
            try { return BuildKit(mats, rootName, scale, centerPivot, xIsLength, addColliders); }
            finally { Length = savedL; Width = savedW; Height = savedH; }
        }

        /// <summary>
        /// 부품 분리 컨테이너 계층 생성. 반환 = 루트 GameObject.
        /// 계층: 루트 / [Castings · Frame · Body · Doors] 그룹 / 개별 파트(약 44개).
        /// 각 파트 메시는 자기 AABB 중심으로 피봇을 옮겨(위치는 localPosition에 인코딩) 분해/회전이 자연스럽다.
        /// </summary>
        /// <param name="mats">부위별 머티리얼</param>
        /// <param name="rootName">루트 이름</param>
        /// <param name="scale">출력 스케일(기본 1/24 미니어처)</param>
        /// <param name="centerPivot">true면 컨테이너 중심, false면 바닥면 기준(기본; 바닥에 앉음)</param>
        /// <param name="xIsLength">true면 도어=+X(기존 좌표계와 동일)</param>
        /// <param name="addColliders">파트마다 BoxCollider 부착(선택/물리 대비)</param>
        public static GameObject BuildKit(
            KitMaterials mats,
            string rootName = "Container_20ft_Kit",
            float scale = DefaultMiniatureScale,
            bool centerPivot = false,
            bool xIsLength = true,
            bool addColliders = true)
        {
            var root = new GameObject(rootName);
            var gCast  = NewGroup("Castings", root.transform);
            var gFrame = NewGroup("Frame", root.transform);
            var gBody  = NewGroup("Body", root.transform);
            var gDoor  = NewGroup("Doors", root.transform);

            // 부품 1개 = 메시 빌드 콜백 → 독립 GameObject 래핑
            GameObject Part(string name, Transform parent, Material mat, Action<MeshBuilder> build)
            {
                var mb = new MeshBuilder();
                build(mb);
                var mesh = mb.ToMesh(name + "_Mesh");
                ApplyTransform(mesh, scale, centerPivot, xIsLength);   // 단일메시본과 동일한 스케일/회전/피봇

                // 파트 자체 AABB 중심으로 피봇 이동 — 위치는 GameObject.localPosition 으로
                var c = mesh.bounds.center;
                var verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++) verts[i] -= c;
                mesh.vertices = verts;
                mesh.RecalculateBounds();

                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = c;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (addColliders)
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = mesh.bounds.center;   // ≈ 0 (피봇 재중심화 후)
                    bc.size = mesh.bounds.size;
                }
                return go;
            }

            float hx = Width  * 0.5f;
            float hz = Length * 0.5f;

            // 코너 캐스팅 8개 (BuildCornerCastings 미러)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            for (int sy = 0; sy <= 1; sy++)
            {
                int ox = sx, oz = sz;
                bool isTop = sy == 1;
                float castH = isTop ? CornerCastTopH : CornerCastH;
                float x = ox * (hx - CornerCastW * 0.5f);
                float z = oz * (hz - CornerCastD * 0.5f);
                float y = isTop ? Height - CornerCastH + castH * 0.5f : castH * 0.5f;
                var center = new Vector3(x, y, z);
                var size   = new Vector3(CornerCastW, castH, CornerCastD);
                string nm = $"Casting_{(isTop ? "Top" : "Bot")}_{(ox < 0 ? "Xn" : "Xp")}{(oz < 0 ? "Zn" : "Zp")}";
                Part(nm, gCast.transform, mats.castings,
                    mb => AddCornerCastingWithHoles(mb, 0, center, size, ox, oz, isTop));
            }

            // 프레임 12개 (BuildFrame 미러)
            float bottomRailY = CornerCastH * 0.5f;
            float topRailY    = Height - CornerCastH * 0.5f;
            float railZSpan   = Length - CornerCastD * 2f;
            float endRailXSpan= Width  - CornerCastW * 2f;

            // 지게차 포켓(fork pocket) 개구 — 사이드 레일 관통
            //   언더프레임에 이미 존재하는 포켓 하우징(BuildUnderframe: ForkPocketZ/ForkPocketWidth)과
            //   Z위치·개구폭을 그대로 일치시킨다(어긋남 방지). 높이=RailH(레일 전 높이 관통), X 전관통.
            //   20ft급(길이<9m)에만 적용(40ft는 포켓 없음). 레일이 포켓에서 3분할된다.
            bool hasForkPockets = Length < 9.0f;      // ForkPlateT는 클래스 상수(공유)
            float pocketZc  = ForkPocketZ;             // 기존 상수 재사용(=1.0)
            float pocketOpenW = ForkPocketWidth;       // 기존 상수 재사용(=0.32)
            float halfRailZ = railZSpan * 0.5f;
            float pHalf = pocketOpenW * 0.5f;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                float cx = sx * (hx - CornerPostW * 0.5f);
                string sn = sx < 0 ? "Xn" : "Xp";
                if (hasForkPockets)
                {
                    // 두 포켓이 비우는 구간을 제외하고 사이드 레일을 3분할(중앙·양끝)로 생성.
                    // z 경계: [-halfRailZ, -Zc-pHalf, -Zc+pHalf, Zc-pHalf, Zc+pHalf, halfRailZ]
                    float[] zb = { -halfRailZ, -pocketZc - pHalf, -pocketZc + pHalf,
                                    pocketZc - pHalf,  pocketZc + pHalf,  halfRailZ };
                    var segs = new (float a, float b)[] { (zb[0], zb[1]), (zb[2], zb[3]), (zb[4], zb[5]) };
                    string[] segName = { "Zn", "Mid", "Zp" };
                    for (int s = 0; s < segs.Length; s++)
                    {
                        float za = segs[s].a, zc2 = segs[s].b;
                        float segLen = zc2 - za;
                        if (segLen <= 0.001f) continue;
                        float segCz = (za + zc2) * 0.5f;
                        Part($"Rail_BotSide_{sn}_{segName[s]}", gFrame.transform, mats.frame,
                            mb => mb.AddBox(0, new Vector3(cx, bottomRailY, segCz),
                                            new Vector3(CornerPostW, RailH, segLen)));
                    }
                }
                else
                {
                    Part($"Rail_BotSide_{sn}", gFrame.transform, mats.frame,
                        mb => mb.AddBox(0, new Vector3(cx, bottomRailY, 0f), new Vector3(CornerPostW, RailH, railZSpan)));
                }
                Part($"Rail_TopSide_{sn}", gFrame.transform, mats.frame,
                    mb => mb.AddBox(0, new Vector3(cx, topRailY, 0f), new Vector3(CornerPostW, RailH, railZSpan)));
            }

            // 포켓 터널 2개 — 폭(X) 전관통, 상·하판 + Z 양벽(X양끝 개방)
            if (hasForkPockets)
            {
                float cxInner = hx - CornerPostW * 0.5f;       // 사이드 레일 중심 X
                float tunXSpan = cxInner * 2f + CornerPostW;   // 레일 바깥면~바깥면 전관통
                // 개구는 바닥 사이드 레일 세그먼트(Rail_BotSide_*)와 동일 높이로 정렬 → 턱 없음.
                //   더 깊은 보강은 언더프레임 ForkPocketDepth 하우징이 별도 표현(아래로 매달림).
                float pocketTopY = bottomRailY + RailH * 0.5f;            // 레일 상단 = 0.1135
                float pocketBotY = bottomRailY - RailH * 0.5f;            // 레일 하단 = 0.0215
                float pocketH    = pocketTopY - pocketBotY;              // = RailH(0.092)
                float pocketCy   = (pocketTopY + pocketBotY) * 0.5f;
                float topPlateY  = pocketTopY - ForkPlateT * 0.5f;
                float botPlateY  = pocketBotY + ForkPlateT * 0.5f;
                for (int pz = -1; pz <= 1; pz += 2)
                {
                    float zc = pz * pocketZc;
                    string pn = pz < 0 ? "Zn" : "Zp";
                    Part($"ForkPocket_{pn}", gFrame.transform, mats.frame, mb =>
                    {
                        // 상판 / 하판(지게차 발이 닿는 면)
                        mb.AddBox(0, new Vector3(0f, topPlateY, zc), new Vector3(tunXSpan, ForkPlateT, pocketOpenW));
                        mb.AddBox(0, new Vector3(0f, botPlateY, zc), new Vector3(tunXSpan, ForkPlateT, pocketOpenW));
                        // Z 양벽(개구 앞/뒤 면) — 개구 전 높이
                        mb.AddBox(0, new Vector3(0f, pocketCy, zc - pHalf + ForkPlateT * 0.5f),
                                  new Vector3(tunXSpan, pocketH, ForkPlateT));
                        mb.AddBox(0, new Vector3(0f, pocketCy, zc + pHalf - ForkPlateT * 0.5f),
                                  new Vector3(tunXSpan, pocketH, ForkPlateT));
                    });
                }
            }
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float cz = sz * (hz - CornerPostW * 0.5f);
                string sn = sz < 0 ? "Zn" : "Zp";
                Part($"Rail_BotEnd_{sn}", gFrame.transform, mats.frame,
                    mb => mb.AddBox(0, new Vector3(0f, bottomRailY, cz), new Vector3(endRailXSpan, RailH, CornerPostW)));
                Part($"Rail_TopEnd_{sn}", gFrame.transform, mats.frame,
                    mb => mb.AddBox(0, new Vector3(0f, topRailY, cz), new Vector3(endRailXSpan, RailH, CornerPostW)));
            }
            float postY      = Height * 0.5f;
            float postHeight = Height - CornerCastH * 2f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float cx = sx * (hx - CornerPostW * 0.5f);
                float cz = sz * (hz - CornerPostW * 0.5f);
                string nm = $"Post_{(sx < 0 ? "Xn" : "Xp")}{(sz < 0 ? "Zn" : "Zp")}";
                Part(nm, gFrame.transform, mats.frame,
                    mb => mb.AddBox(0, new Vector3(cx, postY, cz), new Vector3(CornerPostW, postHeight, CornerPostW)));
            }

            // 본체 패널/지붕/바닥 5개 (BuildBodyPanels/Roof/Floor 미러)
            float panelTop    = Height - CornerCastH * 0.5f - RailH * 0.5f;
            float panelBottom = CornerCastH * 0.5f + RailH * 0.5f;
            float panelHeight = panelTop - panelBottom;
            float depthInsetX = Width  * 0.5f - PanelInset;
            float depthInsetZ = Length * 0.5f - PanelInset;
            const float postInset = 0.002f;
            float sidePanelW = Length - (CornerPostW + postInset) * 2f;

            Part("Panel_Left", gBody.transform, mats.body, mb =>
                BuildCorrugatedPanel(mb, 0,
                    new Vector3(-depthInsetX, panelBottom, -hz + CornerPostW + postInset),
                    new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f),
                    sidePanelW, panelHeight, CorrDepth));
            Part("Panel_Right", gBody.transform, mats.body, mb =>
                BuildCorrugatedPanel(mb, 0,
                    new Vector3(depthInsetX, panelBottom, hz - CornerPostW - postInset),
                    new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f),
                    sidePanelW, panelHeight, CorrDepth));
            Part("Panel_Front", gBody.transform, mats.body, mb =>
                BuildCorrugatedPanel(mb, 0,
                    new Vector3(hx - CornerPostW - postInset, panelBottom, -depthInsetZ),
                    new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                    Width - (CornerPostW + postInset) * 2f, panelHeight, CorrDepth));

            float railTopY  = Height - CornerCastH * 0.5f + RailH * 0.5f;
            float roofBaseY = railTopY - RoofCorrDepth - 0.005f;
            Part("Roof", gBody.transform, mats.body, mb =>
                BuildCorrugatedPanel(mb, 0,
                    new Vector3(-hx, roofBaseY, -hz),
                    new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f),
                    Length, Width, RoofCorrDepth));

            // 바닥은 단일 파트 → 메인 BuildFloor(submesh 0 고정) 직접 재사용
            Part("Floor", gBody.transform, mats.body, mb => BuildFloor(mb));
            // 언더프레임(횡단 크로스멤버) — 프레임 회색. 메인 BuildUnderframe 공유(파트라 submesh 0).
            Part("Underframe", gFrame.transform, mats.frame, mb => BuildUnderframe(mb, 0));

            // 도어 19개 (BuildDoors 미러, 부품 분리)
            float doorZ       = Length * 0.5f;
            float panelMidY   = (panelTop + panelBottom) * 0.5f;
            float fullWidth   = Width - (CornerPostW + postInset) * 2f;
            float doorWidth   = (fullWidth - DoorGap) * 0.5f;
            float doorStartLeft = -hx + CornerPostW + postInset;
            float corrWidth   = doorWidth - SideBeamW;
            float leftBeamX   = doorStartLeft + SideBeamW * 0.5f;
            float rightBeamX  = doorStartLeft + fullWidth - SideBeamW * 0.5f;

            // 측면 빔 2 (Door)
            Part("Door_L_SideBeam", gDoor.transform, mats.door, mb =>
                mb.AddBox(0, new Vector3(leftBeamX, panelMidY, doorZ - PanelInset * 0.5f),
                          new Vector3(SideBeamW, panelHeight, PanelInset)));
            Part("Door_R_SideBeam", gDoor.transform, mats.door, mb =>
                mb.AddBox(0, new Vector3(rightBeamX, panelMidY, doorZ - PanelInset * 0.5f),
                          new Vector3(SideBeamW, panelHeight, PanelInset)));

            // 도어 리프(프레임 패널 A안) 2 (Door) — 돋은 테두리 + 중간 레일 + 상/하 리세스 패널
            Part("Door_L_Leaf", gDoor.transform, mats.door, mb =>
                BuildFramedDoorLeaf(mb, 0, doorStartLeft + SideBeamW, panelBottom, corrWidth, panelHeight, doorZ));
            Part("Door_R_Leaf", gDoor.transform, mats.door, mb =>
                BuildFramedDoorLeaf(mb, 0, doorStartLeft + doorWidth + DoorGap, panelBottom, corrWidth, panelHeight, doorZ));

            // 락바 어셈블리 4 (Frame) — 바 + 상/하 캠 + 손잡이 + 가드 + 브래킷 2
            float lockBarZ  = doorZ + 0.040f;
            float camRadius = LockCamSize * 0.5f;
            for (int doorSide = 0; doorSide < 2; doorSide++)
            {
                float corrStartX = (doorSide == 0)
                    ? doorStartLeft + SideBeamW
                    : doorStartLeft + doorWidth + DoorGap;
                float handleSide = (doorSide == 0) ? -1f : 1f;
                string dn = doorSide == 0 ? "L" : "R";
                for (int bar = 0; bar < LockBarsPerDoor; bar++)
                {
                    float t = (bar + 1f) / (LockBarsPerDoor + 1f);
                    float x = corrStartX + corrWidth * t;
                    int barIdx = bar + 1;
                    Part($"LockBar_{dn}{barIdx}", gDoor.transform, mats.frame, mb =>
                    {
                        AddVerticalCylinder(mb, 0, new Vector3(x, panelBottom, lockBarZ), panelHeight, LockBarDiameter * 0.5f);
                        AddVerticalCylinder(mb, 0, new Vector3(x, panelBottom, lockBarZ), LockCamSize, camRadius);
                        AddVerticalCylinder(mb, 0, new Vector3(x, panelTop - LockCamSize, lockBarZ), LockCamSize, camRadius);
                        // 캠킵 키퍼 (상/하 캠이 헤더·실에 물리는 ㄷ자 리텐션 브래킷)
                        AddCamKeeper(mb, 0, x, panelTop - LockCamSize * 0.5f, lockBarZ, doorZ);
                        AddCamKeeper(mb, 0, x, panelBottom + LockCamSize * 0.5f, lockBarZ, doorZ);
                        // cam-lock 회전 핸들 — 허브+레버암+수직 그립+도어 캐치. 단일메시·Kit 공유 헬퍼(좌표·치수 단일화).
                        AddCamLockHandle(mb, 0, x, panelMidY, lockBarZ, handleSide);
                        float bracketCenterZ = doorZ + LockBracketD * 0.5f;
                        for (int br = 0; br < LockBracketsPerBar; br++)
                        {
                            float bt = (br + 1f) / (LockBracketsPerBar + 1f);
                            float by = panelBottom + panelHeight * bt;
                            mb.AddBox(0, new Vector3(x, by, bracketCenterZ), new Vector3(LockBracketW, LockBracketH, LockBracketD));
                        }
                    });
                }
            }

            // 힌지 8 (Frame) — 배럴(스윙 축)+스트랩 2장, 도어 외측 모서리. BuildDoors와 동일 AddDoorHinge 공유.
            for (int doorSide = 0; doorSide < 2; doorSide++)
            {
                float edgeX = (doorSide == 0) ? doorStartLeft : doorStartLeft + fullWidth;
                float beamX = (doorSide == 0) ? leftBeamX : rightBeamX;
                string dn = doorSide == 0 ? "L" : "R";
                for (int h = 0; h < HingesPerDoor; h++)
                {
                    float t = (h + 1f) / (HingesPerDoor + 1f);
                    float y = panelBottom + panelHeight * t;
                    int hIdx = h + 1;
                    Part($"Hinge_{dn}{hIdx}", gDoor.transform, mats.frame, mb =>
                        AddDoorHinge(mb, 0, edgeX, beamX, y, doorZ));
                }
            }

            // 헤더 1 (Frame)
            float headerWidth = Width - CornerCastW * 2f;
            Part("Door_Header", gDoor.transform, mats.frame, mb =>
                mb.AddBox(0, new Vector3(0f, panelTop + DoorHeaderHeight * 0.5f, doorZ - DoorHeaderDepth * 0.5f),
                          new Vector3(headerWidth, DoorHeaderHeight, DoorHeaderDepth)));

            // 플레이트 2 (Marking — 옅은 무광 흰, 본체색 미적용)
            Material plateMat = mats.marking != null ? mats.marking : mats.body;
            float idPlateX = doorStartLeft + doorWidth + DoorGap + doorWidth * 0.5f;
            float idPlateY = panelTop - IdPlateH * 0.5f - 0.06f;
            Part("Plate_ID", gDoor.transform, plateMat, mb =>
                mb.AddBox(0, new Vector3(idPlateX, idPlateY, doorZ + PlateOut * 0.5f), new Vector3(IdPlateW, IdPlateH, PlateOut)));
            float cscX = doorStartLeft + doorWidth * 0.5f;
            float cscY = panelBottom + CscPlateH * 0.5f + 0.04f;
            Part("Plate_CSC", gDoor.transform, plateMat, mb =>
                mb.AddBox(0, new Vector3(cscX, cscY, doorZ + PlateOut * 0.5f), new Vector3(CscPlateW, CscPlateH, PlateOut)));

            return root;
        }

        static GameObject NewGroup(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g;
        }
    }
}
