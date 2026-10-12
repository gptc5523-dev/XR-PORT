#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 붐: 메인 거더 + 주행 레일 + 격자 대각 + 기계실 (붐 로컬 좌표)
        static void BuildBoomStructure(Transform boom)
        {
            float x0 = BoomBackX;   // 백리치 끝
            float x1 = BoomTipX;    // 아웃리치 끝(바다쪽)

            // 트윈(더블 박스) 거더 — 두 박스 거더(z=±GirderGapZ) + 거더별 하단 주행 레일
            const float gY = GirderCenterY, gH = GirderDepthH; // 클래스 const 참조(값 불변). 지역 별칭으로 아래 식 가독성 유지
            float gTop = GirderTopLocal;               // 거더 윗면(=0.065, 유지 → 상부 시스템 불변)
            float gBot = gY - gH * 0.5f;               // 거더 밑면(=-0.025)
            // [붐 러핑 1단계] 거더·레일을 힌지 X(=WaterLegX)에서 '육지측 고정'+'바다측 러핑' 2조각으로 분할.
            //   두 조각이 힌지에서 맞닿아 0°에선 원래 단일 박스와 동일 — 바다측(_Luff)만 나중에 재부모화해 기립.
            float hingeX  = WaterLegX;
            float landMid = (x0 + hingeX) * 0.5f, landLen = hingeX - x0;   // 육지측(고정)
            float seaMid  = (hingeX + x1) * 0.5f, seaLen  = x1 - hingeX;   // 바다측(러핑)
            for (int s = -1; s <= 1; s += 2)
            {
                float gz    = s * GirderGapZ;
                float railZ = s * (GirderGapZ - GirderWidthZ * 0.5f - 0.0125f);   // 트롤리 주행 레일 Z(거더 안쪽 웹 중간 아래)
                // 육지측(고정) — 기존 이름 유지
                Box(boom, StsPartNames.BoomGirder, new Vector3(landMid, gY, gz),
                    new Vector3(landLen, gH, GirderWidthZ), CBoom);
                Box(boom, "Boom_Rail", new Vector3(landMid, BoomRailTopY - 0.006f, railZ),
                    new Vector3(landLen, 0.012f, 0.02f), CRail);
                // 바다측(러핑) — '_Luff' 이름으로 구분(피벗 재부모화 대상)
                Box(boom, "Boom_Girder_Luff", new Vector3(seaMid, gY, gz),
                    new Vector3(seaLen, gH, GirderWidthZ), CBoom);
                Box(boom, "Boom_Rail_Luff", new Vector3(seaMid, BoomRailTopY - 0.006f, railZ),
                    new Vector3(seaLen, 0.012f, 0.02f), CRail);
            }

            // 두 거더 결속: 횡프레임(상·하 횡재 + 거더별 수직재)
            // 횡프레임 분할수를 트러스(BuildBoomTrussDepth web=16)와 일치 → 수직재·횡재·트러스 패널·스플라이스 절점이 같은 X에 정렬.
            int frames = 16;
            float crossBot = gBot + 0.004f, crossTop = gTop - 0.004f;
            for (int i = 0; i <= frames; i++)
            {
                float fx = Mathf.Lerp(x0, x1, i / (float)frames);
                // [rail-in-middle] 횡 다이어프램 = 포털 프레임. 윗 횡재(crossTop) + 수직재(아래 Boom_Vertical)가
                //   각 거더의 상·하 플랜지를 다 잡는다(밑→수직재→위→crossTop→반대편). 바닥만 열어 훅 통로 확보.
                Box(boom, "Boom_Cross", new Vector3(fx, crossTop, 0f),
                    new Vector3(0.006f, 0.006f, 2f * GirderGapZ), CStruct);   // 윗 횡재(전구간)
                // 바닥 횡재(crossBot)는 훅이 안 지나는 단부(트롤리 가동역 밖)에서만 → 단부=닫힌 다이어프램, 중앙=포털(열림).
                if (fx < TrolleyMinX || fx > TrolleyMaxX)
                    Box(boom, "Boom_Cross", new Vector3(fx, crossBot, 0f),
                        new Vector3(0.006f, 0.006f, 2f * GirderGapZ), CStruct);   // 바닥 횡재(단부만)
                for (int s = -1; s <= 1; s += 2)
                    Box(boom, "Boom_Vertical", new Vector3(fx, gY, s * GirderGapZ),
                        new Vector3(0.006f, gH, 0.006f), CStruct);
            }

            // [rail-in-middle] 바닥면 평면 대각 브레이스(Boom_Plan_Brace) 삭제 — 트롤리가 거더 사이에 nested돼 통로 필요.
            //   비틀림 강성은 깊어진 박스 거더 + 상·하 플랜지 횡프레임 + 거더 바깥면 트러스가 담당.

            // 기계실(육지쪽 위) + 디테일 — 붐 가로(Z)로 넓혀 육중하게(거더보다 양옆 돌출)
            // ※ 기계실은 고정(거더만 뒤로 연장) + 앞으로 MHForward만큼 당김 — 백스테이가 기계실을 안 뚫게
            float mhx = MachineryHouseX;
            float mhZ = 2f * GirderGapZ + 0.08f;   // Z 폭 — 트윈 거더(±GirderGapZ)를 가로질러 얹히게 넓힘(거더 바깥으로 약간 오버행)
            float mhHZ = mhZ * 0.5f;       // Z 반폭
            float mhHX = MachineryHouseHX; // X 반폭(0.17의 절반)
            // 밑면을 거더 윗면(gTop=0.065)에 안착 — 윗면 0.16 유지, 높이 0.095.
            Box(boom, StsPartNames.MachineryHouse, new Vector3(mhx, 0.1125f, 0f),
                new Vector3(0.17f, 0.095f, mhZ), CMachine);
            Box(boom, "MH_Roof", new Vector3(mhx, 0.165f, 0f),
                new Vector3(0.185f, 0.012f, mhZ + 0.01f), CStruct);
            // 비활성: 기계실 바다쪽 출입문 세트(사용자 요청 숨김) — git 이력 참조
            // 루버 환기 패널(양 ±Z면 육지쪽) — 프레임 + 가로 슬랫 다단, 상·하 2뱅크
            for (int s = -1; s <= 1; s += 2)
            {
                float fz = s * mhHZ;
                foreach (float vy in new[] { 0.118f, 0.078f })
                {
                    Box(boom, "MH_VentFrame", new Vector3(mhx - 0.05f, vy, fz - s * 0.001f),
                        new Vector3(0.042f, 0.034f, 0.005f), CDark);
                    for (int l = -2; l <= 2; l++)
                        Box(boom, "MH_Louver", new Vector3(mhx - 0.05f, vy + l * 0.007f, fz),
                            new Vector3(0.038f, 0.004f, 0.004f), CStruct);
                }
            }
            EquipmentUnit(boom, "MH_AC_Unit", new Vector3(mhx - 0.055f, 0.185f, 0f),   // 0.18→0.185: 지붕 윗면(0.171)에 안착(파묻힘 해소)
                new Vector3(0.05f, 0.028f, 0.05f), CDark);
            // 기계실 추가 디테일 — 배기구·창·보조 E-house·케이블 트레이·붐호이스트 윈치
            Rod(boom, "MH_Exhaust", new Vector3(mhx + 0.035f, 0.171f, -0.025f),       // z 0.02→-0.025: HVAC2(+Z) 관통 해소
                new Vector3(mhx + 0.035f, 0.215f, -0.025f), 0.006f, CDark);
            // 창 — 큰 단일 유리 → 리세스 프레임 + 분할 유리 3칸 + 세로 멀리언.
            //   +Z(사다리)면은 출입문으로 대체(BuildMachineryHouseAccess)하므로 창은 -Z면에만.
            for (int s = -1; s <= 1; s += 2)
            {
                if (s > 0) continue;   // +Z = 사다리 접근측 → 창 대신 문
                float fz = s * mhHZ;
                Box(boom, "MH_WinFrame", new Vector3(mhx + 0.01f, 0.118f, fz - s * 0.002f),
                    new Vector3(0.094f, 0.032f, 0.005f), CDark);
                for (int g = -1; g <= 1; g++)
                    Box(boom, "MH_Window", new Vector3(mhx + 0.01f + g * 0.03f, 0.118f, fz),
                        new Vector3(0.024f, 0.024f, 0.004f), CGlass);
                for (int m = -1; m <= 1; m += 2)
                    Box(boom, "MH_Mullion", new Vector3(mhx + 0.01f + m * 0.015f, 0.118f, fz),
                        new Vector3(0.004f, 0.03f, 0.004f), CStruct);
            }
            // 케이블 트레이 — 거더 밑면 아래로 현수. 거더 깊어짐 반영: 하드코딩 -0.02 → GirderBotLocal-0.01(=-0.035) 추종(매립 해소).
            Box(boom, "Cable_Tray", new Vector3(mhx + 0.18f, GirderBotLocal - 0.01f, GirderGapZ),
                new Vector3(0.35f, 0.006f, 0.008f), CDark);
            // 트레이 행어 브래킷 — 거더 밑면(GirderBotLocal)에서 트레이(-0.035)로 매단다(0.17m 공중부양 해소).
            foreach (float dx in new[] { -0.14f, 0f, 0.14f })
                Box(boom, "Cable_Tray_Hanger", new Vector3(mhx + 0.18f + dx, GirderBotLocal - 0.005f, GirderGapZ),
                    new Vector3(0.004f, 0.012f, 0.006f), CStruct);
            // 붐호이스트 윈치 — 기계실 내부 공동(바닥 0.05~지붕 0.16)에 완전 수납. 드럼+플랜지+감속기/모터+디스크브레이크+베드+베어링.
            {
                float wy = 0.095f, wx = mhx - 0.02f;     // 기계실 안쪽(노출 X)
                float wHalfZ = 0.09f;                     // 드럼 반폭
                Rod(boom, "Boom_Hoist_Drum",              // 로프 스풀 드럼
                    new Vector3(wx, wy, -wHalfZ), new Vector3(wx, wy, wHalfZ), 0.026f, CMachine);
                for (int s = -1; s <= 1; s += 2)          // 드럼 양끝 플랜지
                    Rod(boom, "Drum_Flange",
                        new Vector3(wx, wy, s * wHalfZ), new Vector3(wx, wy, s * (wHalfZ + 0.006f)), 0.032f, CDark);
                Rod(boom, "Hoist_Gearbox",                // 감속기(드럼 -Z 직결)
                    new Vector3(wx, wy, -wHalfZ - 0.006f), new Vector3(wx, wy, -wHalfZ - 0.016f), 0.03f, CMachine);
                Rod(boom, "Hoist_Motor",
                    new Vector3(wx, wy, -wHalfZ - 0.016f), new Vector3(wx, wy, -wHalfZ - 0.055f), 0.02f, CDark);
                Rod(boom, "Hoist_Brake",                  // 디스크 브레이크(드럼 +Z)
                    new Vector3(wx, wy, wHalfZ + 0.008f), new Vector3(wx, wy, wHalfZ + 0.016f), 0.034f, CStruct);
                // 베드플레이트 y 0.058→0.070: 밑면(0.065)이 기계실 바닥(0.065)에 안착(종전 0.053은 바닥 밑 12mm 돌출).
                Box(boom, "Hoist_Bedplate", new Vector3(wx, 0.070f, 0f),   // 베드플레이트(기계실 바닥 위)
                    new Vector3(0.07f, 0.01f, 0.24f), CStruct);
                for (int s = -1; s <= 1; s += 2)          // 베어링 페디스털 — 베드플레이트 top(0.075)→드럼축(0.095)
                    Box(boom, "Drum_Pedestal", new Vector3(wx, 0.085f, s * (wHalfZ - 0.01f)),
                        new Vector3(0.03f, 0.02f, 0.014f), CMachine);
            }
            // 트롤리 주행(견인) 윈치 — STS는 견인로프式, 주행 모터/감속기는 기계실에.
            //   드럼 축은 호이스트와 평행(Z), 로프는 바다쪽(+X)으로 나가 붐 따라 트롤리로.
            {
                float twx = TravelDrumX, twy = TravelDrumY, twHalfZ = 0.06f;
                Rod(boom, "Trolley_Travel_Drum",                                  // 견인 로프 스풀 드럼
                    new Vector3(twx, twy, -twHalfZ), new Vector3(twx, twy, twHalfZ), TravelDrumR, CMachine);
                for (int s = -1; s <= 1; s += 2)                                  // 드럼 양끝 플랜지
                    Rod(boom, "Travel_Drum_Flange",
                        new Vector3(twx, twy, s * twHalfZ), new Vector3(twx, twy, s * (twHalfZ + 0.006f)), 0.020f, CMachine);
                Rod(boom, "Travel_Gearbox",                                       // 감속기(드럼 -Z 직결)
                    new Vector3(twx, twy, -twHalfZ - 0.006f), new Vector3(twx, twy, -twHalfZ - 0.016f), 0.018f, CMachine);
                Rod(boom, "Travel_Motor",
                    new Vector3(twx, twy, -twHalfZ - 0.016f), new Vector3(twx, twy, -twHalfZ - 0.045f), 0.014f, CMachine);
                Rod(boom, "Travel_Brake",                                         // 디스크 브레이크(드럼 +Z)
                    new Vector3(twx, twy, twHalfZ + 0.002f), new Vector3(twx, twy, twHalfZ + 0.010f), 0.021f, CMachine);
                // 베드플레이트 y 0.058→0.070: 기계실 바닥(0.065)에 안착(종전 바닥 밑 돌출).
                Box(boom, "Travel_Bedplate", new Vector3(twx, 0.070f, 0f),        // 베드플레이트(기계실 바닥 위)
                    new Vector3(0.04f, 0.01f, 0.16f), CMachine);
                // 주행 드럼 받침대 추가 — 호이스트엔 있는데 누락돼 드럼이 베드플레이트 위 0.38m 떠 있었음. 베드top(0.075)→드럼축(twy=0.095).
                for (int s = -1; s <= 1; s += 2)
                    Box(boom, "Travel_Drum_Pedestal", new Vector3(twx, 0.085f, s * (twHalfZ - 0.01f)),
                        new Vector3(0.03f, 0.02f, 0.012f), CMachine);
            }
            // 기계실 지붕 난간 — 4변 둘레 레일 + 토보드(킥플레이트) + 둘레 기둥
            float mhRoofY = 0.171f;
            float rlZ = mhHZ + 0.003f, rlX = mhHX + 0.003f, rlH = 0.026f;
            for (int s = -1; s <= 1; s += 2)
            {
                // ±Z 긴 변 — 양끝이 3면 코너. 레일·토보드를 양끝 +t/2(각 0.002/0.0015 → size +0.004/+0.003)만 늘려
                //   ±X 변·기둥의 '바깥 면'에 딱 맞춤(상대 단면 절반까지만 덮어 튀어나옴 0).
                Box(boom, "MH_Roof_Rail", new Vector3(mhx, mhRoofY + rlH, s * rlZ),
                    new Vector3(rlX * 2f + 0.004f, 0.004f, 0.004f), CSafety);
                Box(boom, "MH_Roof_Toe", new Vector3(mhx, mhRoofY + 0.006f, s * rlZ),
                    new Vector3(rlX * 2f + 0.003f, 0.009f, 0.003f), CSafety);
                // ±X 끝 변 — 동일하게 양끝 +t/2로 ±Z 변·기둥 바깥 면에 맞춤(코너에서 X바·Z바·기둥 3겹, 튀어나옴 0).
                Box(boom, "MH_Roof_Rail", new Vector3(mhx + s * rlX, mhRoofY + rlH, 0f),
                    new Vector3(0.004f, 0.004f, rlZ * 2f + 0.004f), CSafety);
                Box(boom, "MH_Roof_Toe", new Vector3(mhx + s * rlX, mhRoofY + 0.006f, 0f),
                    new Vector3(0.003f, 0.009f, rlZ * 2f + 0.003f), CSafety);
            }
            // 둘레 기둥(코너 4 + 각 변 중간 4) — 3×3 격자에서 내부 1칸만 제외
            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                if (ix == 0 && iz == 0) continue;
                Box(boom, "MH_Roof_Post", new Vector3(mhx + ix * rlX, mhRoofY + rlH * 0.5f, iz * rlZ),
                    new Vector3(0.004f, rlH, 0.004f), CSafety);   // 안전 노랑(지붕 난간과 통일)
            }

            // 기계실 표면/장비 디테일(이 박스만 고도화)
            float mhFZ = mhHZ;
            // 수직 코너 엣지 포스트 4(상자 모서리 트림)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Box(boom, "MH_Corner", new Vector3(mhx + sx * mhHX, 0.105f, sz * mhFZ),
                    new Vector3(0.006f, 0.112f, 0.006f), CStruct);
            // 패널 이음매(가로 2단 + 세로 2줄, 창 비껴서) — 큰 ±Z면 평탄함 제거
            for (int s = -1; s <= 1; s += 2)
            {
                float fz = s * (mhFZ + 0.001f);
                foreach (float ry in new[] { 0.062f, 0.148f })
                    Box(boom, "MH_PanelSeam", new Vector3(mhx, ry, fz),
                        new Vector3(0.166f, 0.0035f, 0.003f), CStruct);
                for (int c = -1; c <= 1; c += 2)
                    Box(boom, "MH_PanelSeam", new Vector3(mhx + c * 0.055f, 0.105f, fz),
                        new Vector3(0.003f, 0.106f, 0.003f), CStruct);
            }
            // 벽면 정션박스 2 + 수직 도관(육지쪽 +Z면)
            JunctionBox(boom, "MH_JBox", new Vector3(mhx - 0.07f, 0.09f, mhFZ),
                new Vector3(0.016f, 0.022f, 0.008f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f), CDark);
            JunctionBox(boom, "MH_JBox", new Vector3(mhx - 0.07f, 0.06f, mhFZ),
                new Vector3(0.012f, 0.016f, 0.007f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f), CStruct);
            Rod(boom, "MH_Conduit", new Vector3(mhx - 0.07f, 0.055f, mhFZ),
                new Vector3(mhx - 0.07f, 0.155f, mhFZ), 0.003f, CDark);

            // 다리 도관 → 기계실 정션박스(MH_JBox) 연결 — 붐 거더 선을 따라 라우팅해 거더가 밑을 받치게.
            //   엘보 박스도 거더 위에 마운트.
            {
                float condX2 = LandLegX - 0.034f;          // 다리 도관 x (다리 밖 — 기둥/래이싱 관통 회피, condX와 정렬)
                float legZ   = GaugeZ * 0.5f;               // 0.333 (다리 z)
                float gz     = GirderOuterZ - 0.01f;        // ≈0.1775 — 거더 폭 안(인보드), 거더 밑면 아래로 현수
                float runY   = -0.042f;                     // 윗면 -0.037 — 거더 밑면(-0.025)보다 아래로 현수.
                float jbX = mhx - 0.07f, jbY = 0.06f;
                float jbZ = mhFZ;                           // 0.20 — MH_JBox(기계실 +Z벽 z0.20) 본체로 진입하는 종단 z
                float r   = 0.005f;
                float Rb  = 1.5f * r;                       // 곡관 굽힘반경(센터라인). Rb>r → 내반경(Rb-r)>0, 핀치 없음
                // [Sill+캣워크 회피] 상승 구간(V4→V5)이 Sill·캣워크 바깥면(0.2105)을 피해야 함.
                //   가장 바깥(0.2105) 너머로 상승 z(clearZ)를 빼고, 캣워크·Sill 위서 -Z로 꺾어 벽 진입.
                float clearZ = (GirderOuterZ + 0.012f) + 0.011f + r + 0.002f;   // clearZ ≈ 0.2175 — 캣워크 바깥면(0.2105) + 관반경 + 여유
                // [곡관 엘보] 90° 굽힘을 짧은 원통 호 + 패싯 절점 구로 근사(관 굵기 일정, 부풀지 않음). 직선 구간은 접점 트림→접선 연속.
                //   경로: 다리에서 내려와 거더 밑으로 횡주행 → 캣워크·Sill 바깥(z≈0.2175)으로 빠져 상승 → 캣워크 위서 벽 진입 → MH_JBox.
                Vector3[] path = {
                    new Vector3(condX2, -0.005f, legZ),       // V0 다리 도관 접속(자유단)
                    new Vector3(condX2, runY,    legZ),       // V1
                    new Vector3(condX2, runY,    gz),         // V2
                    new Vector3(jbX,    runY,    gz),         // V3 (거더 밑 -X 횡주행 끝)
                    new Vector3(jbX,    runY,    clearZ),      // V4 (캣워크·Sill 바깥 z로 빠져나옴)
                    new Vector3(jbX,    jbY,     clearZ),      // V5 (캣워크 바깥에서 상승 — Leg_Conduit_Link_5, 관통 해소)
                    new Vector3(jbX,    jbY,     jbZ),         // V6 → MH_JBox(자유단, 캣워크 위서 -Z로 꺾어 벽 진입)
                };
                ConduitPath(boom, "Leg_Conduit_Link", path, r, Rb, CDark);
            }
            // 벽면 작업등(아래 향함, 양 ±Z면 바다쪽 상단) — 박스+구 → 하향 Floodlight 어셈블리.
            for (int s = -1; s <= 1; s += 2)
                Floodlight(boom, new Vector3(mhx + 0.06f, 0.155f, s * mhFZ), 0.012f, CDark, CLight);
            // 지붕 디테일 — 점검 해치 + 보조 HVAC + 배기 캡
            Box(boom, "MH_RoofHatch", new Vector3(mhx + 0.06f, mhRoofY + 0.006f, -0.012f),
                new Vector3(0.03f, 0.008f, 0.03f), CDark);
            EquipmentUnit(boom, "MH_HVAC2", new Vector3(mhx + 0.03f, mhRoofY + 0.011f, 0.025f),   // 지붕 윗면 안착
                new Vector3(0.04f, 0.022f, 0.03f), CDark);
            Ball(boom, "MH_ExhaustCap", new Vector3(mhx + 0.035f, 0.218f, -0.025f),              // 배기 스택 따라 -Z로
                new Vector3(0.012f, 0.008f, 0.012f), CDark);
            // 지붕 접근 수직 사다리(바다쪽 +Z면 → 지붕). y0=데크면(0.067)에서 시작.
            //   출입 캣워크 도어에서 사다리 베이스로 잇는 step-off 랜딩 데크 추가.
            Box(boom, "MHAccess_RoofLandingDeck", new Vector3(mhx + 0.035f, 0.067f, 0.225f),
                new Vector3(0.10f, 0.004f, 0.05f), CMachine);
            BuildLadder(boom, mhx + 0.07f, mhFZ + 0.008f, 0.067f, mhRoofY, 0.02f);
            // 지붕 사다리 스탠드오프 브래킷 — 벽(z=mhFZ)↔사다리(z≈mhFZ+0.008) 고정(6mm 공중부양 제거).
            for (int li = 0; li < 3; li++)
                Box(boom, "Ladder_Bracket",
                    new Vector3(mhx + 0.07f, Mathf.Lerp(0.085f, mhRoofY - 0.02f, li / 2f), mhFZ + 0.004f),
                    new Vector3(0.02f, 0.006f, 0.012f), CSafety);

            // 기계실 전체 마감/디테일 (넓힌 게이지 폭에 맞춤)
            float mhTopY = 0.16f, mhBotY = 0.05f;

            // 마감: 상부 처마(eave) + 하부 실(sill) 둘레 띠 — 박스 4변 테두리 정리
            for (int s = -1; s <= 1; s += 2)
            {
                Box(boom, "MH_Eave", new Vector3(mhx, mhTopY + 0.003f, s * (mhHZ + 0.004f)),
                    new Vector3(0.176f, 0.006f, 0.006f), CStruct);
                Box(boom, "MH_Sill", new Vector3(mhx, mhBotY + 0.001f, s * (mhHZ + 0.003f)),
                    new Vector3(0.176f, 0.008f, 0.007f), CStruct);
                Box(boom, "MH_Eave", new Vector3(mhx + s * (mhHX + 0.004f), mhTopY + 0.003f, 0f),
                    new Vector3(0.006f, 0.006f, mhZ + 0.008f), CStruct);
                Box(boom, "MH_Sill", new Vector3(mhx + s * (mhHX + 0.003f), mhBotY + 0.001f, 0f),
                    new Vector3(0.007f, 0.008f, mhZ + 0.006f), CStruct);
            }

            // 육지쪽(-X) 끝면: 대형 루버 흡기 뱅크 3(프레임 + 가로 슬랫)
            float mhEndX = mhx - mhHX - 0.001f;
            for (int iz = -1; iz <= 1; iz++)
            {
                float lz = iz * 0.072f;
                Box(boom, "MH_EndLouverFrame", new Vector3(mhEndX, 0.103f, lz),
                    new Vector3(0.005f, 0.062f, 0.052f), CDark);
                for (int l = -3; l <= 3; l++)
                    Box(boom, "MH_EndLouver", new Vector3(mhEndX - 0.001f, 0.103f + l * 0.008f, lz),
                        new Vector3(0.004f, 0.004f, 0.048f), CStruct);
            }

            // 비활성: 기계실 바다쪽(+X) 끝면 창(사용자 요청 숨김) — git 이력 참조

            // 넓어진 지붕 양 끝(Z flank) 장비 — 콘덴서 2 + 팬그릴, 버섯 벤트 2, 케이블 트레이 횡단
            for (int s = -1; s <= 1; s += 2)
            {
                EquipmentUnit(boom, "MH_Condenser", new Vector3(mhx - 0.02f, mhRoofY + 0.016f, s * 0.085f),
                    new Vector3(0.06f, 0.03f, 0.05f), CDark);
                Rod(boom, "MH_RoofVent", new Vector3(mhx + 0.055f, mhRoofY + 0.004f, s * 0.08f),
                    new Vector3(mhx + 0.055f, mhRoofY + 0.022f, s * 0.08f), 0.005f, CStruct);
                Ball(boom, "MH_RoofVent_Cap", new Vector3(mhx + 0.055f, mhRoofY + 0.024f, s * 0.08f),
                    new Vector3(0.014f, 0.009f, 0.014f), CStruct);
            }
            Box(boom, "MH_RoofTray", new Vector3(mhx + 0.02f, mhRoofY + 0.0025f, 0f),  // 밑면 지붕면(mhRoofY) 안착: center=mhRoofY+높이0.005/2
                new Vector3(0.012f, 0.005f, mhZ * 0.85f), CDark);

            // 지붕 네 모서리 적색 마커등(항공/안전)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Beacon(boom, "MH_CornerLight", new Vector3(mhx + sx * mhHX, mhRoofY + 0.004f, sz * mhHZ), 0.0045f, CWarn);

            // 넓은 ±Z면 보강 — 수직 도관 + 벽 작업등(육지쪽 분산)
            for (int s = -1; s <= 1; s += 2)
            {
                Rod(boom, "MH_WallConduit", new Vector3(mhx + 0.05f, 0.055f, s * (mhHZ + 0.0015f)),
                    new Vector3(mhx + 0.05f, 0.15f, s * (mhHZ + 0.0015f)), 0.003f, CDark);
                // 벽 작업등(육지쪽) — 박스+구 → 하향 Floodlight 어셈블리.
                Floodlight(boom, new Vector3(mhx - 0.02f, 0.155f, s * (mhHZ + 0.001f)), 0.012f, CDark, CLight);
            }

            // 붐 상단 양옆 난간(walkway railing) — 트윈 거더 바깥 가장자리
            float girderTop = 0.065f;   // 거더 윗면
            float railTop   = 0.105f;   // 상단 가로 레일 높이
            int posts = 7;
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * GirderOuterZ;
                // 상단 가로 레일 — [BOOM-1=MH-1 수정] 통짜 Box → BoxGappedX. 포털·기계실 통과 구간에서 끊어 벽체 관통 제거.
                BoxGappedX(boom, "Boom_Railing", x0, x1, railTop, z,
                    0.006f, 0.006f, CSafety, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
                // 중간 가로대 — 포털·기계실 통과 지점에서 끊음
                BoxGappedX(boom, "Boom_Railing_Mid", x0, x1, (railTop + girderTop) * 0.5f, z,
                    0.004f, 0.004f, CStruct, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
                // 토보드(킥플레이트) — 보도 가장자리. 포털·기계실 통과 지점에서 끊음(Shoulder_Beam·기계실 벽체 관통 방지)
                BoxGappedX(boom, "Boom_Toe", x0, x1, girderTop + 0.009f, z,
                    0.012f, 0.003f, CStruct, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
                for (int i = 0; i <= posts; i++)
                {
                    float px = Mathf.Lerp(x0, x1, i / (float)posts);
                    if (InWalkwayGap(px)) continue;   // [BOOM-1] 포털/기계실 통과 구간엔 난간 기둥 생략(기계실 벽체 매립 방지)
                    // 밑동은 트러스 상현재 캡 윗면(0.0685) 위 — 낮추면 캡에 박힌다.
                    float postBot = 0.069f;
                    Box(boom, "Railing_Post",
                        new Vector3(px, (railTop + postBot) * 0.5f + 0.0015f, z),
                        new Vector3(0.005f, railTop - postBot + 0.003f, 0.005f), CSafety);   // 기둥 top을 상단레일 바깥면(+t/2)까지 위로만
                }

                // [BOOM-1 마감] 기계실 구간에서 끊긴 난간(상단·중간레일·토보드)의 민짜 잘린 끝면을
                //   수직 단부 캡판으로 마감. 기계실 양옆 갭 경계(좌/우)에 1장씩, 토보드 하단~상단레일 윗면을 덮음.
                float mhGapHalf = MachineryHouseHX + 0.008f;             // 기계실 갭 반폭(BoomTopWalkwayGapHalf[2]와 동일 식)
                float capBot = girderTop + 0.002f, capTop = railTop + 0.003f;
                foreach (float capX in new[] { MachineryHouseX - mhGapHalf, MachineryHouseX + mhGapHalf })
                    Box(boom, "Boom_Railing_EndCap", new Vector3(capX, (capBot + capTop) * 0.5f, z),
                        new Vector3(0.004f, capTop - capBot, 0.014f), CSafety);
            }
        }

        // A-프레임 정상 + 포어/백 스테이 케이블 (루트 로컬)
        static void BuildApexAndStays(Transform root, Transform luffPivot)
        {
            float halfZ = GaugeZ * 0.5f;
            float apexHalfZ = GaugeZ * 0.35f;   // 정상 가로보(Apex, 폭 GaugeZ*0.7) 끝=A-프레임 다리가 물리는 코너
            Vector3 apex = new Vector3(WaterLegX, RailH + ApexH, 0f);

            // 정상 가로보
            Box(root, "Apex", new Vector3(apex.x, apex.y, 0f),
                new Vector3(0.04f, 0.04f, GaugeZ * 0.7f), CStruct);

            // 정상 점검 플랫폼(데크 + 난간 기둥/상·중단 가로대 + 제어 캐비닛)
            float platY = apex.y + 0.03f;
            float halfX = 0.035f;
            float halfPZ = GaugeZ * 0.275f;
            float railH2 = 0.032f;

            Box(root, "Apex_Platform", new Vector3(apex.x, platY, 0f),
                new Vector3(halfX * 2f, 0.008f, halfPZ * 2f), CStruct);
            // 데크 받침 스터브 — 데크 밑면(platY-0.004)을 정상 가로보 윗면(apex.y+0.02)에 받침(6mm 공중부양 제거).
            for (int psz = -1; psz <= 1; psz += 2)
                Box(root, "Apex_PlatformStub", new Vector3(apex.x, apex.y + 0.023f, psz * halfPZ * 0.6f),
                    new Vector3(halfX * 1.6f, 0.006f, 0.006f), CStruct);

            // 난간 기둥(둘레 8개)
            float[] gx = { -halfX, 0f, halfX };
            float[] gz = { -halfPZ, 0f, halfPZ };
            foreach (float fx in gx)
            foreach (float fz in gz)
            {
                if (Mathf.Abs(fx) >= halfX - 1e-4f || Mathf.Abs(fz) >= halfPZ - 1e-4f)
                    Box(root, "Apex_Post", new Vector3(apex.x + fx, platY + railH2 * 0.5f, fz),
                        new Vector3(0.004f, railH2, 0.004f), CStruct);
            }
            // 상단·중간 가로 레일(4변 × 2단)
            foreach (float h in new[] { railH2, railH2 * 0.55f })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(root, "Apex_Rail", new Vector3(apex.x, platY + h, s * halfPZ),
                        new Vector3(halfX * 2f + 0.004f, 0.004f, 0.004f), CSafety);   // 양끝 3면코너 +t/2씩
                    Box(root, "Apex_Rail", new Vector3(apex.x + s * halfX, platY + h, 0f),
                        new Vector3(0.004f, 0.004f, halfPZ * 2f + 0.004f), CSafety);
                }
            }
            // 제어 캐비닛 — 직립 인클로저(도어/루버/지붕/공조)
            ControlCabinet(root, "Apex_Cabinet", new Vector3(apex.x - halfX * 0.4f, platY + 0.02f, halfPZ * 0.45f),
                new Vector3(0.025f, 0.03f, 0.02f), new Vector3(1f, 0f, 0f), CMachine);
            // 토보드(난간 하부 킥플레이트, 4변) — 공구 낙하 방지 + 디테일
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, "Apex_ToeBoard", new Vector3(apex.x, platY + 0.007f, s * (halfPZ - 0.001f)),
                    new Vector3(halfX * 2f + 0.001f, 0.01f, 0.003f), CSafety);   // 토보드 inset0.001 보정 후 바깥면 맞춤
                Box(root, "Apex_ToeBoard", new Vector3(apex.x + s * (halfX - 0.001f), platY + 0.007f, 0f),
                    new Vector3(0.003f, 0.01f, halfPZ * 2f + 0.001f), CSafety);
            }
            // 정션 박스 2(반대편 데크) — 데크 볼트 고정형(덮개는 빔 방향, 글랜드는 데크 아래로)
            JunctionBox(root, "Apex_JBox", new Vector3(apex.x + halfX * 0.45f, platY + 0.016f, -halfPZ * 0.5f),
                new Vector3(0.016f, 0.022f, 0.014f), new Vector3(1f, 0f, 0f), Vector3.down, CDark);
            JunctionBox(root, "Apex_JBox", new Vector3(apex.x - halfX * 0.5f, platY + 0.012f, -halfPZ * 0.2f),
                new Vector3(0.012f, 0.016f, 0.012f), new Vector3(-1f, 0f, 0f), Vector3.down, CMachine);

            // 비콘 마스트 — 단단한 마스트 + 하우징 달린 적색 항공장애등 2단 + 풍속계 + 피뢰침
            float mb = platY + 0.010f;   // Mast_Base 밑면이 데크 윗면에 안착
            float mastTop = mb + 0.10f;
            // 받침 플랜지 2단(원형)
            Rod(root, "Mast_Base", new Vector3(apex.x, mb - 0.006f, 0f),
                new Vector3(apex.x, mb + 0.004f, 0f), 0.012f, CMachine);
            Rod(root, "Mast_Base", new Vector3(apex.x, mb + 0.004f, 0f),
                new Vector3(apex.x, mb + 0.012f, 0f), 0.0075f, CStruct);
            // 마스트 기둥(약간 굵게)
            Rod(root, "Apex_Mast", new Vector3(apex.x, mb + 0.012f, 0f),
                new Vector3(apex.x, mastTop, 0f), 0.0035f, CStruct);
            // 적색 항공장애등 2단 — 구 돔 → 비콘(검은 베이스+적색 발광 렌즈 드럼+캡). 마스트에 적층.
            foreach (float ly in new[] { mb + 0.045f, mb + 0.085f })
                Beacon(root, "Aviation_Light", new Vector3(apex.x, ly - 0.006f, 0f), 0.0085f, CWarn);
            // 피뢰침(마스트 꼭대기 → 뾰족)
            Cone(root, "Lightning_Rod", new Vector3(apex.x, mastTop, 0f),
                new Vector3(apex.x, mastTop + 0.028f, 0f), 0.002f, 0f, CStruct, 12);
            // 풍속계 — 측면 브래킷 + 수직 스핀들 + 수평 3컵(맞바람에 도는 컵)
            Vector3 anBrkt = new Vector3(apex.x, mb + 0.07f, 0.022f);
            Rod(root, "Anemo_Arm", new Vector3(apex.x, mb + 0.07f, 0.004f), anBrkt, 0.0015f, CStruct);
            Vector3 anHub = anBrkt + new Vector3(0f, 0.012f, 0f);
            Rod(root, "Anemo_Spindle", anBrkt, anHub, 0.0014f, CDark);
            Ball(root, "Anemo_Hub", anHub, new Vector3(0.006f, 0.006f, 0.006f), CDark);
            for (int k = 0; k < 3; k++)
            {
                float a3 = k * Mathf.PI * 2f / 3f;
                Vector3 cupP = anHub + new Vector3(Mathf.Cos(a3) * 0.011f, 0.001f, Mathf.Sin(a3) * 0.011f);
                Rod(root, "Anemo_CupArm", anHub, cupP, 0.0008f, CStruct);
                Ball(root, "Anemo_Cup", cupP, new Vector3(0.005f, 0.005f, 0.005f), CStruct);
            }

            // A-프레임 다리 4개 — 같은 쪽 정상 코너(±apexHalfZ)에서 같은 쪽 다리 상단으로 물림
            // → port/star 두 개의 A가 정상 가로보로 묶이는 형상(한 점 수렴 아님)
            foreach (float lx in new[] { LandLegX, WaterLegX })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Strut(root, "Aframe",
                        new Vector3(apex.x, apex.y, s * apexHalfZ),
                        new Vector3(lx, LegTopY, s * halfZ), 0.016f, CStruct);   // 지름 실척 ≈0.77m(현장 0.8~1.2m 하한대)
                }
            }

            // A-프레임 면을 격자 트러스로 — 다단 가로 브레이스 + 대각 지그재그(앞·뒤 면)
            float[] lv = { 0.20f, 0.40f, 0.60f, 0.80f };
            foreach (float legX in new[] { LandLegX, WaterLegX })
            {
                foreach (float t in lv)
                {
                    float bx = Mathf.Lerp(WaterLegX, legX, t);
                    float by = Mathf.Lerp(apex.y, LegTopY, t);
                    // 가로 브레이스 폭 — 꼭대기(코너 ±apexHalfZ)에서 다리 베이스(±halfZ)로
                    Box(root, "Aframe_Brace", new Vector3(bx, by, 0f),
                        new Vector3(0.007f, 0.007f, 2f * Mathf.Lerp(apexHalfZ, halfZ, t)), CStruct);
                }
                // 큰 X 하나 — 면의 꼭대기 코너(±apexHalfZ)에서 다리 베이스 반대편(±halfZ)으로 교차(지그재그 제거)
                Vector3 tN = new Vector3(apex.x, apex.y, -apexHalfZ);
                Vector3 tP = new Vector3(apex.x, apex.y,  apexHalfZ);
                Vector3 bN = new Vector3(legX, LegTopY, -halfZ);
                Vector3 bP = new Vector3(legX, LegTopY,  halfZ);
                Strut(root, "Aframe_Lace", tN, bP, 0.008f, CStruct);
                Strut(root, "Aframe_Lace", tP, bN, 0.008f, CStruct);
            }

            // A-프레임 측면(port/star) 가로 브레이스 — 바다·육지 다리쌍 연결
            foreach (float t in new[] { 0.38f, 0.66f })
            {
                float lx = Mathf.Lerp(WaterLegX, LandLegX, t);
                float yy = Mathf.Lerp(apex.y, LegTopY, t);
                float sz = Mathf.Lerp(apexHalfZ, halfZ, t);   // 다리의 안쪽 기울기에 맞춤(코너→베이스)
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(root, "Aframe_SideBrace",
                        new Vector3((WaterLegX + lx) * 0.5f, yy, s * sz),
                        new Vector3(Mathf.Abs(WaterLegX - lx), 0.006f, 0.006f), CStruct);
                }
            }

            // A-프레임 절점 거싯 — 정상 코너 결합부 + 다리 상단 결합부(측면 ±Z 면에 연결판).
            //   A-프레임 다리(Strut)·라싱이 한 점에 모이는 곳에 용접 거싯을 덧대 절점 표현.
            float afG = 0.013f, afT = 0.004f, afOff = 0.003f;
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion fr = s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                // 정상 코너(가로보 끝, A-프레임 다리가 물리는 곳)
                Gusset(root, new Vector3(apex.x, apex.y, s * apexHalfZ + s * afOff), fr, afG, afT, CStruct);
                // 다리 상단 결합부(육지/바다 다리) — 다리 격자 외곽면(halfZ + legHalf)에 부착(파묻힘 방지)
                float legHalf = LegSec * 1.7f * 0.5f;
                foreach (float lx in new[] { LandLegX, WaterLegX })
                    Gusset(root, new Vector3(lx, LegTopY + 0.012f, s * (halfZ + legHalf) + s * afOff), fr, afG, afT, CStruct);
            }

            // 정상 시브 네스트(스테이 도르래) + 장비 하우징 — 시브를 가로보 밑면 아래로 내리고
            //   하우스를 솔리드 박스 → 개방 슈라우드(상부 캡+양 Z단부 측벽)로 바꿔 로프 벽관통 방지.
            float sheaveY = apex.y - 0.042f;
            // 상부 캡(가로보 Apex 밑면 apex.y-0.02에 현수) — 시브 top(apex.y-0.026)과 1.5mm 이격
            Box(root, "Apex_SheaveHouse", new Vector3(apex.x, apex.y - 0.0215f, 0f),
                new Vector3(0.03f, 0.006f, 2f * GirderGapZ + 0.08f), CMachine);
            for (int s = -1; s <= 1; s += 2)   // 양 Z단부 측벽(시브 z=±0.16 바깥 ±0.20에서 끝막음, 로프 경로 z=±0.16엔 비관여)
                Box(root, "Apex_SheaveHouse", new Vector3(apex.x, sheaveY + 0.004f, s * (GirderGapZ + 0.04f)),
                    new Vector3(0.03f, 0.05f, 0.006f), CMachine);
            // 정상 스테이 정착부 — 회전 시브가 아니라 핀-클레비스(패드아이) 정착.
            //   포어/백스테이는 별개의 정적 인장재(시브를 넘지 않음) — 가로 정착 핀(축 Z)+양 치크판으로 정착.
            for (int s = -1; s <= 1; s += 2)
            {
                float sz = s * GirderGapZ;
                Vector3 C = new Vector3(apex.x, sheaveY, sz);
                // 이 치크·핀은 이제 '붐호이스트 시브의 지지 프레임'이다(포어스테이는 굵은 타이바로 분리됨 → 여기 아님).
                //   시브 치크판 2장(z=sz 양옆) — 윗변을 정점 가로보 밑면(apex.y-0.02)까지 연장해 크로스헤드에 용접(공중부양 0).
                float cheekTop = apex.y - 0.02f;                 // 정점 가로보(Apex) 밑면
                float cheekBot = C.y - 0.015f;
                float cheekCY  = (cheekTop + cheekBot) * 0.5f;
                float cheekH   = cheekTop - cheekBot;
                for (int e = -1; e <= 1; e += 2)
                    PbBox(root, "Apex_SheaveCheek", new Vector3(C.x, cheekCY, C.z + e * 0.010f),
                        new Vector3(0.024f, cheekH, 0.004f), CStruct);
                // 시브 축(핀, 축 Z) — 치크 사이를 관통, 붐호이스트 시브가 이 축에서 회전
                Rod(root, "Apex_SheaveAxle",
                    C + new Vector3(0f, 0f, -0.016f), C + new Vector3(0f, 0f, 0.016f), 0.006f, CDark);
                // 축 리테이너 캡(양 외측)
                for (int e = -1; e <= 1; e += 2)
                    Rod(root, "Apex_SheaveAxleCap",
                        C + new Vector3(0f, 0f, e * 0.014f), C + new Vector3(0f, 0f, e * 0.018f), 0.009f, CStruct);
                // 스테이 집합 소켓은 없다 — 붐호이스트 시브가 이 자리를 차지한다(포어스테이는 동적화됨).

                // [붐 러핑] 붐호이스트 시브 — 정착 핀을 축으로 회전 도르래를 얹는다(핀-클레비스 재활용, 충돌 0).
                //   반경 0.016 — 시브 top(apex.y-0.026) < 캡 밑면(apex.y-0.0245, 1.5mm 여유), 하단=치크 하단 정렬.
                Sheave(root, "Apex_BoomHoistSheave",
                    C + new Vector3(0f, 0f, -0.009f), C + new Vector3(0f, 0f, 0.009f),
                    0.016f, 0.006f, 0.003f, CDark);
            }
            // 시브 하우스 디테일 — 내부 웹 리브(시브 사이 z=±0.12, 시브 비관통) + 점검 해치 + 리프팅 러그
            //   리브 z를 GaugeZ*0.24(=0.16=시브 중심) → GaugeZ*0.18(=0.12, 두 시브 안쪽)로 옮겨 V홈 관통 해소.
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, "SheaveHouse_Rib", new Vector3(apex.x, apex.y - 0.030f, s * GaugeZ * 0.18f),
                    new Vector3(0.034f, 0.04f, 0.004f), CStruct);
            }
            Box(root, "SheaveHouse_Hatch", new Vector3(apex.x + 0.016f, apex.y - 0.0215f, 0f),
                new Vector3(0.004f, 0.02f, 0.02f), CDark);
            // 리프팅 러그 y=apex.y+0.027 — 정점보 top(apex.y+0.02) 위로 0.014 돌출.
            Box(root, "Lifting_Lug", new Vector3(apex.x, apex.y + 0.027f, 0f),
                new Vector3(0.006f, 0.014f, 0.006f), CStruct);

            // 정상 작업조명 갤러리 — 플랫폼 바다쪽 가장자리에서 받침대로 뻗은 프레임 +
            // 하우징 달린 플러드라이트(아래·바다쪽 작업면을 비춤). 떠 있지 않게 받침으로 고정.
            float galX = apex.x + 0.042f;
            float galY = platY - 0.004f;
            Box(root, "FloodBar", new Vector3(galX, galY, 0f),
                new Vector3(0.006f, 0.008f, halfPZ * 1.7f), CStruct);
            for (int s = -1; s <= 1; s += 2)
                Strut(root, "FloodBar_Brace",
                    // 앵커는 데크 가장자리(apex.x+halfX) — 중심에서 뻗으면 데크 두께를 관통한다.
                    new Vector3(apex.x + halfX, platY + 0.004f, s * halfPZ * 0.7f),
                    new Vector3(galX, galY, s * halfPZ * 0.7f), 0.004f, CStruct);
            for (int i = -2; i <= 2; i++)
            {
                float fz = i * halfPZ * 0.4f;
                // 에이펙스 투광등 — FloodBar(galX)에서 +X 아래 45°로 안벽 작업역 조사. 박스+구 → 방향지정 어셈블리.
                Floodlight(root, new Vector3(galX + 0.004f, galY, fz), Quaternion.Euler(0f, 0f, 45f), 0.013f, CDark, CLight);
            }


            // 스테이 케이블 — 부채꼴 + 앵커 플레이트·턴버클. 측면 시브(z=±GirderGapZ)에서 같은 쪽 거더로 내림.
            // [포어스테이 = 굵은 강성 타이바] 정점 크로스헤드 ↔ 붐 외측(f=0.9) — 길이 변화는 rig가 동적 처리.
            float boomTopY = RailH + (GirderTopLocal + 0.005f);
            float pivotRootY = RailH + GirderCenterY;   // luffPivot의 root-Y
            const float fsBoomF = 0.9f;                 // 붐 정착점(외측, 힌지서 90%)
            var fsApex = new List<Transform>(); var fsBoom = new List<Transform>(); var fsBar = new List<Transform>();
            var fsHost = new GameObject("Forestay_TieBar"); fsHost.transform.SetParent(root, worldPositionStays: false);
            for (int s = -1; s <= 1; s += 2)
            {
                float fgz = s * GirderGapZ;
                var ap = new GameObject("Forestay_ApexPin");
                ap.transform.SetParent(root, worldPositionStays: false);
                ap.transform.localPosition = new Vector3(apex.x, apex.y - 0.02f, fgz);   // 정점 크로스헤드 정착
                fsApex.Add(ap.transform);
                var bp = new GameObject("Forestay_BoomPin");
                bp.transform.SetParent(luffPivot, worldPositionStays: false);            // 러핑 추종
                Vector3 boomPinL = new Vector3(Mathf.Lerp(WaterLegX, BoomTipX, fsBoomF) - WaterLegX, boomTopY - pivotRootY, fgz);
                bp.transform.localPosition = boomPinL;
                fsBoom.Add(bp.transform);
                Box(luffPivot, "Forestay_BoomLug", boomPinL + new Vector3(0f, -0.012f, 0f),
                    new Vector3(0.022f, 0.030f, 0.008f), CStruct);   // 붐측 클레비스 러그
                var bar = NewPrimitive(PrimitiveType.Cylinder, "Forestay_TieBar", fsHost.transform);
                Colorize(bar, CStruct);   // 굵은 강성 타이바(강재색)
                fsBar.Add(bar.transform);
            }
            fsHost.AddComponent<BoomHoistRig>().Configure(fsApex.ToArray(), fsBoom.ToArray(), fsBar.ToArray(), 0.012f);   // 반경 0.012u(≈0.29m)
            // 비활성: 정상 후방 아웃리거 — ApexH↑로 백스테이가 기계실을 넘어 불필요. git 이력 참조

            // 백스테이 — 정상 시브 하우스(z=±GirderGapZ)에서 거더 맨뒤(이퀄라이저 빔)로 가는 '주 백스테이'. 짧은 앞 줄은 A-프레임과 겹쳐 제외.
            // [백스테이 = 굵은 강성 타이바] 앞(포어스테이)과 대칭, 양끝 고정(정적) — 정점 끝은 시브 아닌 크로스헤드에 핀.
            float bsBackX = BoomBackX + 0.02f;
            for (int s = -1; s <= 1; s += 2)
            {
                float bgz = s * GirderGapZ;
                Vector3 aApex = new Vector3(apex.x, apex.y - 0.02f, bgz);        // 정점 크로스헤드(시브 아님)
                Vector3 bBoom = new Vector3(bsBackX, RailH + 0.092f, bgz);       // 붐 맨뒤(이퀄라이저 노드) 정착
                Rod(root, "Backstay_TieBar", aApex, bBoom, 0.012f, CStruct);     // 굵은 강성 타이바(강재색) — 끝은 기존 붐측 Backstay_Lug 클레비스에 물림(중복 정착판 안 만듦)
                Vector3 d = aApex - bBoom; float dl = d.magnitude;
                if (dl > 1e-4f)
                {
                    d /= dl;
                    // 턴버클 = 로드가 나사물림되는 '배럴(슬리브)' — 로드(0.012)보다 굵어야 해 배럴 0.019+락너트 0.016.
                    const float barR = 0.019f, nutR = 0.016f;
                    Vector3 tbMid = bBoom + d * 0.065f;                    // 배럴 중심(붐 정착 근처 = 조절단)
                    Vector3 tbA = tbMid - d * 0.028f, tbB = tbMid + d * 0.028f;
                    Rod(root, "Backstay_Turnbuckle", tbA, tbB, barR, CDark);                       // 배럴(굵은 슬리브)
                    Rod(root, "Backstay_TB_Nut", tbA - d * 0.006f, tbA + d * 0.006f, nutR, CMachine);   // 락너트(양끝)
                    Rod(root, "Backstay_TB_Nut", tbB - d * 0.006f, tbB + d * 0.006f, nutR, CMachine);
                }
            }
        }

        // 붐 디테일: 보도 그레이팅 + 끝단 시브(도르래) + 작업등
        static void BuildBoomDetails(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            float mid = (x0 + x1) * 0.5f;

            // 보도 그레이팅 — 거더 위 2장 + 가운데(Boom_Vertical/브레이스 위) 1장으로 상단 전체를 막음(연속 보도, 패널 이음새 유지)
            for (int s = -1; s <= 1; s += 2)
                BoxGappedX(boom, "Walkway_Deck", x0, x1, 0.067f, s * GirderGapZ,
                    0.004f, 0.05f, CMachine, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
            // 비활성: 중앙 보도(사용자 요청 숨김) — git 이력 참조

            // 작업등(floodlight) — 붐 하부, 아래를 비춤
            foreach (float lx in new[] { mid + 0.10f, x1 - 0.12f, x1 - 0.30f })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * GirderGapZ;
                    // 투광등 — 박스+구 → 요크/방열핀/본체/베젤/평면렌즈 어셈블리(Floodlight 헬퍼). at=거더 밑면, 하향. -0.010 하드코딩 → GirderBotLocal 추종(매립 해소).
                    Floodlight(boom, new Vector3(lx, GirderBotLocal, z), 0.018f, CDark, CLight);
                }
            }

            // 끝단 플랫폼 — 트윈 거더 전폭으로(좁던 것 수정) + 둘레 안전 난간
            Box(boom, "Tip_Platform", new Vector3(x1 - 0.075f, 0.066f, 0f),
                new Vector3(0.15f, 0.004f, 2f * GirderOuterZ), CMachine);   // 육지쪽으로 연장(0.08→0.15) — 견인 가이드 롤러(gTip x≈2.645)를 플랫폼 밑에 받침
            {
                float tpY0 = 0.068f, tpRailY = 0.105f;   // Boom_Railing railTop에 높이 맞춤(붐난간↔Tip 단차 제거)
                float tpX0 = x1 - 0.08f, tpX1 = x1, tpHZ = GirderOuterZ;
                for (int s = -1; s <= 1; s += 2)   // ±Z 옆 난간
                    Box(boom, "Tip_Rail", new Vector3((tpX0 + tpX1) * 0.5f + 0.00125f, tpRailY, s * tpHZ),
                        new Vector3(tpX1 - tpX0 + 0.0025f, 0.005f, 0.005f), CSafety);   // 바다쪽(tpX1)만 코너 → +t/2, center +t/4
                Box(boom, "Tip_Rail", new Vector3(tpX1, tpRailY, 0f),   // 바다쪽 끝 난간
                    new Vector3(0.005f, 0.005f, tpHZ * 2f + 0.005f), CSafety);   // 양끝 3면코너 +t/2씩
                foreach (float pz in new[] { -tpHZ, 0f, tpHZ })        // 기둥(끝 코너+중앙)
                    Box(boom, "Tip_Rail_Post", new Vector3(tpX1, (tpY0 + tpRailY) * 0.5f, pz),
                        new Vector3(0.005f, tpRailY - tpY0, 0.005f), CSafety);
            }
            // 끝단 트윈 시브 블록 없음(사용자 지시) — 호이스트 로프 리그는 이 시브를 참조하지 않는다.
            // 비활성: 항해등(사용자 요청 숨김) — git 이력 참조

            // 하부 점검 캣워크(양옆) + 난간(상단·중간대 + 기둥)
            for (int s = -1; s <= 1; s += 2)
            {
                float cz = s * (GirderOuterZ + 0.012f);
                float railZ = cz + s * 0.009f;
                // [붐 러핑] 하부 캣워크·난간을 힌지에서 분할(BoomSpanBox) → 바다측(_Luff) 편입. (끝단 +0.003 코너연장은 생략, 무시가능)
                BoomSpanBox(boom, "Catwalk",        0f,     cz,    0.004f, 0.022f, CMachine);
                BoomSpanBox(boom, "Catwalk_Rail",   0.026f, railZ, 0.004f, 0.004f, CSafety);
                BoomSpanBox(boom, "Catwalk_RailMid", 0.013f, railZ, 0.003f, 0.003f, CSafety);
                int cposts = 14;
                for (int i = 0; i <= cposts; i++)
                {
                    float px = Mathf.Lerp(x0, x1, i / (float)cposts);
                    Box(boom, "Catwalk_Post", new Vector3(px, 0.014f, railZ),
                        new Vector3(0.003f, 0.028f, 0.003f), CSafety);   // 기둥 top을 상단레일 바깥면(+t/2)까지 위로
                }
            }

            // 뒷부분(육지측 백리치) 디테일. 평형추는 없다 — 긴 아웃리치는 A프레임+포어/백스테이 텐션+기계실 질량+백리치 구조로 균형.
            // 백스테이 이퀄라이저 노드(빌트업) — 웹+상하 플랜지+수직 스티프너+거더 접속 거싯+양끝 클레비스 러그/핀.
            {
                float ax = x0 + 0.02f;
                float ay = 0.072f;                  // 빔 중심 — 백스테이 정착 y0.092가 윗면 바로 위에 오게
                float hz = GirderGapZ;              // 빔 끝 = 거더 z(±0.16) = 백스테이 정착선
                PbBox(boom, "Stay_Anchor", new Vector3(ax, ay, 0f),
                    new Vector3(0.018f, 0.03f, 2f * hz), CMachine);                  // 웹
                for (int sy = -1; sy <= 1; sy += 2)
                    PbBox(boom, "Stay_Anchor_Flange", new Vector3(ax, ay + sy * 0.016f, 0f),
                        new Vector3(0.026f, 0.006f, 2f * hz + 0.012f), CStruct);     // 상·하 플랜지
                foreach (float sz in new[] { -0.085f, 0f, 0.085f })                 // 수직 스티프너(다이어프램)
                    PbBox(boom, "Stay_Anchor_Stiff", new Vector3(ax, ay, sz),
                        new Vector3(0.02f, 0.028f, 0.005f), CStruct);
                for (int s = -1; s <= 1; s += 2)   // 양 끝(z=±0.16): 거더 접속 거싯 + 백스테이 클레비스 러그·핀
                {
                    float ez = s * hz;
                    Gusset(boom, new Vector3(ax + 0.010f, ay, ez), Quaternion.Euler(0, 90, 0), 0.018f, 0.004f, CStruct);
                    Gusset(boom, new Vector3(ax - 0.010f, ay, ez), Quaternion.Euler(0, -90, 0), 0.018f, 0.004f, CStruct);
                    for (int ex = -1; ex <= 1; ex += 2)   // 클레비스 러그 귀 2(X로 벌려 Stay_Plate를 사이에 끼움)
                        PbBox(boom, "Backstay_Lug", new Vector3(ax + ex * 0.011f, 0.09f, ez),
                            new Vector3(0.005f, 0.018f, 0.014f), CStruct);
                    Rod(boom, "Backstay_Pin",             // 클레비스 핀(러그 관통, X축 양옆 돌출)
                        new Vector3(ax - 0.016f, 0.09f, ez), new Vector3(ax + 0.016f, 0.09f, ez), 0.0026f, CDark);
                    for (int px = -1; px <= 1; px += 2)
                        Rod(boom, "Backstay_PinCap",
                            new Vector3(ax + px * 0.013f, 0.09f, ez), new Vector3(ax + px * 0.016f, 0.09f, ez), 0.004f, CStruct);
                }
            }

            // 백리치 끝 플랫폼 + 둘레 안전 난간
            //   난간 없는 평판 → Tip_Platform과 동일하게 둘레 난간(상단 레일/중간대/기둥/토보드).
            {
                float bpX = x0 + 0.02f, bpY = 0.066f;
                float bpHX = 0.03f, bpHZ = GirderOuterZ;     // 반치수
                PbBox(boom, "Back_Platform", new Vector3(bpX, bpY, 0f),
                    new Vector3(bpHX * 2f, 0.004f, bpHZ * 2f), CMachine);
                float pTop = 0.068f, railY = 0.105f;   // pTop=데크 윗면(BackSheave 행어 기준), railY=붐 보도 난간 railTop(0.105)과 동일
                // 백리치 자체 기둥 폐지 — 끝변 난간을 붐 보도 난간 첫 기둥(Railing_Post, x0)에 붙인다.
                //   붐 보도 난간 ±Z 첫 기둥을 잇는 가로바가 육지쪽 'ㄷ자' 마감이 되어 붐 기둥이 그대로 지지한다.
                PbBox(boom, "Back_Rail", new Vector3(x0, railY, 0f),        // 상단레일 — z 양끝이 붐 첫 기둥(±GirderOuterZ) 바깥면까지 +t/2
                    new Vector3(0.005f, 0.005f, bpHZ * 2f + 0.005f), CSafety);
                PbBox(boom, "Back_RailMid", new Vector3(x0, 0.085f, 0f),    // 중간레일 — 붐 보도 중간레일(Boom_Railing_Mid=0.085)에 맞춰 연속
                    new Vector3(0.004f, 0.004f, bpHZ * 2f + 0.004f), CSafety);

                // 백리치 디플렉터 시브 — 권상 로프(트롤리→여기)를 받아 시브를 감고 돌려 기계실 진입 포트로 보냄
                //   side(z=±0.05)별 1개, 각 시브가 더블그루브로 2폴(z=sz±0.008) 수용(반폭 BackSheaveHZ=0.016). 데크 밑으로 내려 행어 현수(축 Z).
                foreach (float zs in new[] { -1f, 1f })
                {
                    float sz = zs * 0.05f;
                    Vector3 sc = new Vector3(BackSheaveX, BackSheaveY, sz);
                    // 행어 — 데크 밑면에서 시브 핀까지 내려뜨린 스트랩 2장(클레비스).
                    //   치크 바깥(SheaveNest chZ=HZ+0.003)에 여유를 두고 양옆에서 핀을 잡는다.
                    float hangZ = (BackSheaveHZ + 0.003f) + 0.006f;   // 휠·치크 바깥
                    for (int hs = -1; hs <= 1; hs += 2)
                        PbBox(boom, "BackSheave_Hanger",
                            new Vector3(BackSheaveX, (pTop + BackSheaveY) * 0.5f, sz + hs * hangZ),
                            new Vector3(0.005f, pTop - BackSheaveY, 0.004f), CStruct);
                    Sheave(boom, "BackSheave",
                        sc + new Vector3(0f, 0f, -BackSheaveHZ), sc + new Vector3(0f, 0f, BackSheaveHZ),
                        BackSheaveR, 0.004f, 0.003f, CDark);
                    SheaveNest(boom, sc, BackSheaveR, BackSheaveHZ, CStruct);       // 치크판 + 핀 보스
                }
            }

            // 백리치 끝 면(x0) 단부 프레임 — 두 거더를 잇는 빌트업 X-브레이스 + 상·하 타이 + 코너 포스트 + 절점 거싯.
            //   막대 4개 → 빌트업 브레이스 + 코너 포스트/거싯으로 제대로 된 단부 포털(현실 STS 백리치 단부).
            {
                float bgB = GirderBotLocal, bgT = GirderTopLocal;   // 거더 상·하면 SSOT 추종(하드코딩 금지 — 깊어지면 단부프레임이 뜬다)
                // ※ BuiltUpBrace 금지: Z를 가로지르는 대각(−GirderGapZ↔+GirderGapZ)은 그 헬퍼의 '같은 z 평면' 전제를
                //   깨 회전이 무너지고 수직 판때기가 된다. 임의 3D 방향을 올바로 정렬하는 Strut 사용.
                Strut(boom, "Backreach_EndBrace", new Vector3(x0, bgB, -GirderGapZ), new Vector3(x0, bgT, GirderGapZ), 0.007f, CStruct);
                Strut(boom, "Backreach_EndBrace", new Vector3(x0, bgB, GirderGapZ), new Vector3(x0, bgT, -GirderGapZ), 0.007f, CStruct);
                PbBox(boom, "Backreach_EndTie", new Vector3(x0, bgB, 0f), new Vector3(0.009f, 0.009f, 2f * GirderGapZ), CStruct);
                PbBox(boom, "Backreach_EndTie", new Vector3(x0, bgT, 0f), new Vector3(0.009f, 0.009f, 2f * GirderGapZ), CStruct);
                for (int s = -1; s <= 1; s += 2)    // 코너 수직 포스트(거더 끝 ±z) + 상·하 절점 거싯
                {
                    PbBox(boom, "Backreach_EndPost", new Vector3(x0, (bgB + bgT) * 0.5f, s * GirderGapZ),
                        new Vector3(0.009f, bgT - bgB + 0.009f, 0.009f), CStruct);   // 상·하 타이 가로지르게 양끝 +t/2(=0.0045), center 유지
                    Gusset(boom, new Vector3(x0 - 0.006f, bgT, s * GirderGapZ), Quaternion.Euler(0, -90, 0), 0.014f, 0.004f, CStruct);
                    Gusset(boom, new Vector3(x0 - 0.006f, bgB, s * GirderGapZ), Quaternion.Euler(0, -90, 0), 0.014f, 0.004f, CStruct);
                }
            }
            // 비활성: 백리치 경고등·변압기·냉각 유닛(사용자 요청 숨김) — git 이력 참조

            // 전체 디테일 보강 (붐)
            // 전력/제어 도관(conduit) — 붐 하부 전장 양옆
            for (int s = -1; s <= 1; s += 2)
            {
                // [붐 러핑] 도관을 힌지에서 분할 → 바다측(_Luff) 편입
                Rod(boom, "Conduit",
                    new Vector3(x0, -0.012f, s * GirderGapZ),
                    new Vector3(WaterLegX, -0.012f, s * GirderGapZ), 0.004f, CDark);
                Rod(boom, "Conduit_Luff",
                    new Vector3(WaterLegX, -0.012f, s * GirderGapZ),
                    new Vector3(x1, -0.012f, s * GirderGapZ), 0.004f, CDark);
            }
            // 붐 코너 작업등 추가
            foreach (float lx in new[] { x0 + 0.06f, x1 - 0.05f })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    // 투광등(붐 코너) — 동일 헬퍼, 폭만 0.014로. -0.010 → GirderBotLocal 추종(거더 깊어짐 매립 해소).
                    Floodlight(boom, new Vector3(lx, GirderBotLocal, s * GirderGapZ), 0.014f, CDark, CLight);
                }
            }
        }

        // 붐 측면 트러스 입체화 + 측면 케이블 트레이 — 트윈 박스 거더가 납작해 보이던 것 보강.
        //   각 거더 바깥/안쪽 면에 상·하현재+워런 빗재를 둘러 깊이감(거더 본체/레일/트롤리 좌표는 안 건드림).
        static void BuildBoomTrussDepth(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            float len = x1 - x0, mid = (x0 + x1) * 0.5f;

            // 거더 윗면만 필요 → 클래스 const 직접 참조.
            float gTop = GirderTopLocal;   // 거더 윗면 0.065 (보도 그레이팅 밑면과 같은 레벨)

            // 트러스 현재 — 위는 보도 밑면(0.065)에 맞춤. 아래는 주행 트롤리 상단(y=0, Z 반폭 0.185로
            //   거더 바깥 면까지 옴)과 겹치지 않게 그 바로 위(0.003)까지만 내려 깊이 확장.
            float chTop = gTop;            // 상현재 0.065 (gTop 유지)
            float chBot = -0.008f;         // 하현재 — 도관(-0.012) 바로 위
            float chMidY = (chTop + chBot) * 0.5f;
            float chH = chTop - chBot;

            int web = 16;                  // 빗재 베이 수
            float wstep = len / web;

            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * GirderGapZ;
                float zo = gz + s * (GirderWidthZ * 0.5f + 0.0015f);  // 바깥 면(웹 패널 + 트레이)
                float zi = gz - s * (GirderWidthZ * 0.5f + 0.0015f);  // 안쪽 면(현재만)

                // 상·하현재 캡 — 바깥/안쪽 양면(거더를 ㅁ자 프레임으로 감쌈).
                //   [붐 러핑] 전장 현재를 힌지에서 분할(BoomSpanBox) → 바다측 절반(_Luff)이 붐과 함께 기립.
                foreach (float zf in new[] { zo, zi })
                {
                    BoomSpanBox(boom, "Boom_Chord_Top", chTop, zf, 0.007f, 0.007f, CStruct);
                    BoomSpanBox(boom, "Boom_Chord_Bot", chBot, zf, 0.007f, 0.007f, CStruct);
                }

                // 바깥 면 워런 빗재 — 상↔하현재 지그재그(삼각 분할 → 트러스 느낌)
                for (int i = 0; i < web; i++)
                {
                    float xa = x0 + wstep * i;
                    float xb = x0 + wstep * (i + 1);
                    bool up = (i % 2) == 0;
                    Strut(boom, "Boom_Web",
                        new Vector3(xa, up ? chBot : chTop, zo),
                        new Vector3(xb, up ? chTop : chBot, zo), 0.004f, CStruct);
                }
                // 패널 포인트 수직재 — 폴리 절약 위해 2칸마다만
                for (int i = 0; i <= web; i += 2)
                {
                    float px = x0 + wstep * i;
                    Box(boom, "Boom_WebPost", new Vector3(px, chMidY, zo),
                        new Vector3(0.005f, chH, 0.005f), CStruct);
                }

                // 연속 측면 케이블 트레이(바깥) + 클램프 + 케이블 다발
                float trayZ = zo + s * 0.007f;
                float trayY = 0.03f;
                // [붐 러핑] 측면 케이블 트레이·립을 힌지에서 분할 → 바다측(_Luff) 편입
                BoomSpanBox(boom, "Boom_CableTray",     trayY,          trayZ,               0.004f, 0.013f, CDark);    // 트레이 바닥
                BoomSpanBox(boom, "Boom_CableTray_Lip", trayY + 0.006f, trayZ + s * 0.0055f, 0.009f, 0.003f, CStruct);  // 바깥 립
                int clamps = 13;
                for (int i = 0; i <= clamps; i++)
                {
                    float cx = Mathf.Lerp(x0, x1, i / (float)clamps);
                    Box(boom, "Boom_TrayClamp", new Vector3(cx, trayY - 0.004f, trayZ),
                        new Vector3(0.005f, 0.011f, 0.02f), CStruct);               // 트레이 행거 클램프
                }
                for (int c = -1; c <= 1; c += 2)                                    // 케이블 다발 2줄
                {
                    // [러핑 수정] 트레이 케이블도 힌지(WaterLegX)에서 분할 — 바다측(_Luff)이 붐 따라 기립(종전 전장 1줄이라 안 따라옴).
                    float ccz = trayZ + c * 0.0028f;
                    Rod(boom, "Boom_TrayCable",      new Vector3(x0, trayY + 0.001f, ccz),
                        new Vector3(WaterLegX, trayY + 0.001f, ccz), 0.0026f, CCable);
                    Rod(boom, "Boom_TrayCable_Luff", new Vector3(WaterLegX, trayY + 0.001f, ccz),
                        new Vector3(x1, trayY + 0.001f, ccz), 0.0026f, CCable);
                }
                // 트레이 정션 박스 2 — 트레이 립 위에 얹고 글랜드를 아래로(케이블 위 y≈0.038로 올림) → 베이스가 케이블 관통하던 것 제거.
                foreach (float jx in new[] { mid - 0.45f, mid + 0.55f })
                    JunctionBox(boom, "Boom_TrayJBox", new Vector3(jx, 0.052f, trayZ + s * 0.006f),
                        new Vector3(0.03f, 0.024f, 0.013f), new Vector3(0f, 0f, s), new Vector3(0f, -1f, 0f), CMachine);
            }
        }

        // 사다리 정상 ↔ 기계실 접근 캣워크 + 기계실 +Z(사다리쪽) 출입문(붐 로컬).
        //   캣워크: 사다리(z≈0.377) → 코너(x=LandLegX) → 기계실 문(x=MachineryHouseX), 데크 레벨 0.067.
        static void BuildMachineryHouseAccess(Transform boom)
        {
            float deckY   = 0.067f;
            float mhx     = MachineryHouseX;                 // ≈ -0.207
            float mhZ     = 2f * GirderGapZ + 0.08f;         // 0.40
            float mhHZ    = mhZ * 0.5f;                      // +Z 면 z = 0.20
            float ladderZ = GaugeZ * 0.5f + LegSec * 1.7f * 0.5f + 0.022f;  // 사다리 z ≈ 0.377
            float walkW   = 0.055f;                          // 캣워크 폭
            float railH   = 0.035f;
            float corZ    = mhHZ + 0.035f;                   // 캣워크 코너 z(=0.235, 기계실 벽 바깥)

            // 경로(우회): 사다리(z≈ladderZ) 정상에서 다리·포털 바깥(+Z)으로 빠져나간 뒤,
            //   기계실 X(x=mhx)에서 안쪽(−Z)으로 꺾어 문 앞(corZ)으로 진입. → 다리 기둥/포털 크로스빔을 통과하지 않음.

            // 세그먼트1: 사다리(x=LandLegX) → 기계실 X(x=mhx), z=ladderZ (다리 바깥 우회).
            //   사다리 위는 데크로 덮지 않는다 — 덮으면 철판에 막힌다. 사다리 가장자리에서 step-off 랜딩으로 끝낸다.
            float deckFarX  = mhx - walkW * 0.5f;   // 기계실 코너쪽 끝 — 세그2와 정합 위해 walkW/2 오버행 유지
            float cageOuterX = 0.027f;              // 케이지 측면바 바깥면(±0.025+0.004) — 사다리(±0.015)보다 넓다.
            float deckNearX = LandLegX - cageOuterX; // 케이지 '바깥면'에서 끝냄 — 데크가 케이지(Cage_Hoop 측면바)를 타고 넘지 않게.
                                                    //   사다리 가장자리(±0.015)로 끝내면 케이지(±0.025)를 0.01 덮어 Cage_Hoop을 침범한다.
                                                    //   머리 위 개구부: 사다리(±0.015)는 데크 끝(-0.027)보다 +X라 그대로 비어 있음.
            float s1x   = (deckFarX + deckNearX) * 0.5f;
            float s1len = deckNearX - deckFarX;
            Box(boom, "MHAccess_Deck", new Vector3(s1x, deckY, ladderZ),
                new Vector3(s1len, 0.004f, walkW), CMachine);

            // 세그먼트2: 코너(x=mhx) → 문 앞(z=corZ), x=mhx
            float s2z = (ladderZ + corZ) * 0.5f;
            float s2len = ladderZ - corZ + walkW;
            Box(boom, "MHAccess_Deck", new Vector3(mhx, deckY, s2z),
                new Vector3(walkW, 0.004f, s2len), CMachine);

            // 난간: 다리 반대편(바깥)에만 — 세그1 +Z, 세그2 −X(기계실 쪽). 다리에 안 닿게 통로 확보.
            float r1z = ladderZ + walkW * 0.5f;
            Box(boom, "MHAccess_Rail", new Vector3(s1x - 0.001f, deckY + railH, r1z),
                new Vector3(s1len + 0.002f, 0.004f, 0.004f), CSafety);   // 코너A(X하한)만 +t/2
            // i=0(x=LandLegX) 기둥은 사용자 요청으로 제외.
            for (int i = 1; i <= 4; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(Mathf.Lerp(LandLegX, mhx, i / 4f), deckY + railH * 0.5f + 0.001f, r1z),
                    new Vector3(0.004f, railH + 0.002f, 0.004f), CSafety);   // 기둥 top을 상단레일 바깥면(+t/2)까지 위로
            float r2x = mhx - walkW * 0.5f;
            Box(boom, "MHAccess_Rail", new Vector3(r2x, deckY + railH, s2z + 0.001f),
                new Vector3(0.004f, 0.004f, s2len + 0.002f), CSafety);   // 코너A(Z상한)만 +t/2 (코너B는 양호)
            for (int i = 0; i <= 2; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(r2x, deckY + railH * 0.5f + 0.001f, Mathf.Lerp(corZ, ladderZ, i / 2f)),
                    new Vector3(0.004f, railH + 0.002f, 0.004f), CSafety);   // 기둥 top을 상단레일 바깥면(+t/2)까지 위로
            // 바깥쪽 볼록 꼭지점 기둥 — 두 바깥 난간(r1z·r2x)이 만나는 코너. 없으면 코너가 ㄴ처럼 비어 보임.
            Box(boom, "MHAccess_Post",
                new Vector3(mhx - walkW * 0.5f, deckY + railH * 0.5f + 0.001f, ladderZ + walkW * 0.5f),
                new Vector3(0.004f, railH + 0.002f, 0.004f), CSafety);   // 꼭지점 기둥 top 상단레일 바깥면까지 +t/2

            // 안쪽 난간(다리 반대 면) — 세그1 −Z, 세그2 +X. 양측 난간으로 통로 완성.
            // 세그1 안쪽: 다리 기둥/사다리 승강 갭 확보 위해 다리쪽을 살짝 띄워 시작.
            float in1Start = LandLegX - 0.025f;
            float in1End   = mhx + walkW * 0.5f;   // 안쪽 코너에서 끝냄 — 세그2(직각 통로) 입구를 안 막게(길이 계산)
            float in1cx = (in1Start + in1End) * 0.5f;
            float in1len = Mathf.Abs(in1Start - in1End);
            float in1z = ladderZ - walkW * 0.5f;
            Box(boom, "MHAccess_Rail", new Vector3(in1cx - 0.001f, deckY + railH, in1z),
                new Vector3(in1len + 0.002f, 0.004f, 0.004f), CSafety);   // 코너C(X하한)만 +t/2
            for (int i = 0; i <= 3; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(Mathf.Lerp(in1Start, in1End, i / 3f), deckY + railH * 0.5f + 0.001f, in1z),
                    new Vector3(0.004f, railH + 0.002f, 0.004f), CSafety);   // 기둥 top을 상단레일 바깥면(+t/2)까지 위로
            // 세그2 안쪽: 문 진입(낮은 z)은 열어두고, 위로는 안쪽 코너에서 끝냄(세그1 통로를 안 막게).
            float in2x = mhx + walkW * 0.5f;
            float in2Top = ladderZ - walkW * 0.5f;   // 안쪽 코너 z — 세그1 통로 침범 방지(길이 계산)
            float in2cz  = (corZ + in2Top) * 0.5f;
            float in2len = Mathf.Abs(in2Top - corZ);
            Box(boom, "MHAccess_Rail", new Vector3(in2x, deckY + railH, in2cz),
                new Vector3(0.004f, 0.004f, in2len + 0.004f), CSafety);   // 코너C+D 양끝 → +t/2씩(size+t), center 유지
            for (int i = 0; i <= 2; i++)
                Box(boom, "MHAccess_Post",
                    new Vector3(in2x, deckY + railH * 0.5f + 0.001f, Mathf.Lerp(corZ, in2Top, i / 2f)),
                    new Vector3(0.004f, railH + 0.002f, 0.004f), CSafety);   // 기둥 top을 상단레일 바깥면(+t/2)까지 위로

            // 받침 아웃리거 브래킷(떠 있지 않게) — 캣워크 → 거더 상단. 다리선(x=LandLegX)은 피함.
            foreach (float bx in new[] { LandLegX - 0.06f, mhx })
                Strut(boom, "MHAccess_Bracket",
                    new Vector3(bx, deckY, ladderZ), new Vector3(bx, 0.065f, GirderOuterZ), 0.004f, CStruct);
            Strut(boom, "MHAccess_Bracket",
                new Vector3(mhx, deckY, corZ), new Vector3(mhx, 0.065f, GirderOuterZ), 0.004f, CStruct);

            // 기계실 +Z(사다리쪽) 출입문 한 짝: 문틀 + 문짝 + 손잡이 + 킥플레이트 + 문턱
            float dz = mhHZ;          // +Z 면
            float doorY = 0.103f;     // 문 중심(밑은 데크 0.067, 위는 -Z면 창 영역 아래)
            Box(boom, "MH_DoorFrame", new Vector3(mhx, doorY, dz - 0.001f),
                new Vector3(0.052f, 0.078f, 0.005f), CStruct);
            Box(boom, "MH_Door", new Vector3(mhx, doorY - 0.002f, dz + 0.002f),
                new Vector3(0.045f, 0.068f, 0.005f), CDark);
            Box(boom, "MH_DoorHandle", new Vector3(mhx + 0.016f, doorY, dz + 0.005f),
                new Vector3(0.004f, 0.012f, 0.004f), CStruct);
            Box(boom, "MH_DoorKick", new Vector3(mhx, doorY - 0.03f, dz + 0.003f),
                new Vector3(0.045f, 0.016f, 0.003f), CStruct);
            Box(boom, "MH_DoorThreshold", new Vector3(mhx, deckY + 0.001f, dz + 0.016f),
                new Vector3(0.052f, 0.004f, 0.032f), CMachine);
        }

        // 붐 상부 보도/난간이 포털 빔·기계실과 만나는 X 구간을 끊는 통과 구간(다리 2곳 + 기계실 1곳).
        //   Shoulder_Beam 플랜지 X 반폭(LegSec*0.55)+여유 기준. 기계실 구간은 +Z쪽 접근 캣워크로 우회.
        static readonly float[] BoomTopWalkwayGapX    = { LandLegX, WaterLegX, MachineryHouseX };
        static readonly float[] BoomTopWalkwayGapHalf = { LegSec * 0.55f + 0.006f,   // 포털(육지) — 플랜지(LegSec×0.55)와 같은 계수+여유로 항상 클리어(LegSec 무관). ≈0.029
                                                          LegSec * 0.55f + 0.006f,   // 포털(바다) 동일
                                                          MachineryHouseHX + 0.008f }; // 기계실 ≈0.093(반폭 0.085 + 8mm 여유)

        // X축으로 긴 보도 박스를 gapCenters±gapHalf 구간에서 끊어 여러 토막으로 생성(포털 빔 관통 방지).
        static void BoxGappedX(Transform parent, string name, float x0, float x1, float y, float z,
                               float thickY, float thickZ, Color color, float[] gapCenters, float[] gapHalves)
        {
            var cuts = new List<float> { x0 };
            for (int g = 0; g < gapCenters.Length; g++)
            {
                float gc = gapCenters[g];
                float gh = gapHalves[g];   // 갭별 반폭(포털=좁게, 기계실=넓게)
                if (gc - gh > x0 && gc + gh < x1) { cuts.Add(gc - gh); cuts.Add(gc + gh); }
            }
            cuts.Add(x1);
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i += 2)   // 유지 구간 = (cuts[0],cuts[1]),(cuts[2],cuts[3])...
            {
                float a = cuts[i], b = cuts[i + 1];
                if (b - a > 0.004f)
                    Box(parent, name, new Vector3((a + b) * 0.5f, y, z),
                        new Vector3(b - a, thickY, thickZ), color);
            }
        }

        // 붐 상단 X가 보도 통과 구간(포털/기계실)에 드는지 — 통과 구간엔 난간 기둥을 생략한다.
        static bool InWalkwayGap(float px)
        {
            for (int g = 0; g < BoomTopWalkwayGapX.Length; g++)
                if (Mathf.Abs(px - BoomTopWalkwayGapX[g]) < BoomTopWalkwayGapHalf[g]) return true;
            return false;
        }

        // 붐 거더 스플라이스 연결판 + 볼트열 — 박스 거더를 분절된 볼트 접합 세그먼트로(붐 로컬).
        static void BuildBoomSplices(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            const float gY = GirderCenterY, gH = GirderDepthH;  // 클래스 const 참조(값 불변)
            float gTop = GirderTopLocal;

            // 스플라이스를 횡프레임/트러스 절점 격자(16분할)의 i=6,10 위에 둠 — 부재 없는 베이 중앙이 아닌 패널포인트에 현장 이음.
            foreach (float f in new[] { 6f / 16f, 10f / 16f })
            {
                float sx = Mathf.Lerp(x0, x1, f);
                for (int s = -1; s <= 1; s += 2)
                {
                    float gz = s * GirderGapZ;
                    float zo = gz + s * (GirderWidthZ * 0.5f + 0.0015f);
                    // 측면 스플라이스 플레이트(거더 면을 덮는 가는 띠) + 세로 볼트열
                    Box(boom, "Girder_Splice", new Vector3(sx, gY, zo),
                        new Vector3(0.012f, gH + 0.006f, 0.004f), CStruct);
                    // -Z 거더(s=-1)는 볼트가 +Z(거더 안쪽)로 박혀 파묻혔음 → 면 노멀에 맞춰 180° 회전(바깥 돌출).
                    Bolts(boom, new Vector3(sx, gY, zo), s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0),
                        "splice_col", BoltGrid(1, 4, 0f, 0.013f), 0.0016f, 0.0026f);
                }
                // 상부 플랜지 가로 스플라이스(두 거더를 잇는 덮개판) + 가로 볼트열
                //   폭은 플랜지 바깥 가장자리(±GirderOuterZ)까지.
                Box(boom, "Girder_FlangeSplice", new Vector3(sx, gTop + 0.004f, 0f),
                    new Vector3(0.016f, 0.004f, 2f * GirderOuterZ), CStruct);
                // 볼트열이 두 거더 사이 갭(±0.1)에 떨어져 플랜지(|z|≈0.16)를 한 줄도 안 물었음
                //   → 거더별 2x2 클러스터를 z=±GirderGapZ 플랜지 위에 배치.
                for (int gs = -1; gs <= 1; gs += 2)
                    Bolts(boom, new Vector3(sx, gTop + 0.008f, gs * GirderGapZ), Quaternion.Euler(-90, 0, 0),
                        "splice_flange", BoltGrid(2, 2, 0.007f, 0.018f), 0.0015f, 0.0004f);
            }
        }

        /// <summary>붐 힌지 관절 — 각 거더 butt joint(x=WaterLegX)에 클레비스(고정 2판)+텅(러핑 1판)+핀(Z축).
        /// 고정 클레비스·핀은 boom(육지측), 텅은 luffPivot(바다측·회전)에 둔다. 스윕 이후에 호출.</summary>
        static void BuildBoomHinge(Transform boom, Transform luffPivot)
        {
            float hx = WaterLegX, hy = GirderCenterY;
            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * GirderGapZ;
                // 고정 클레비스 2판(육지측 거더 끝, 핀축 Z로 벌어짐) — boom
                foreach (float e in new[] { -1f, 1f })
                    Box(boom, "Boom_HingeClevis", new Vector3(hx - 0.012f, hy, gz + e * 0.013f),
                        new Vector3(0.028f, 0.075f, 0.006f), CStruct);
                // 러핑 텅 1판(바다측 거더 끝, 클레비스 사이) — luffPivot(피벗-로컬 = 붐-로컬 − 피벗원점)
                Box(luffPivot, "Boom_HingeTongue",
                    new Vector3((hx + 0.012f) - WaterLegX, hy - GirderCenterY, gz),
                    new Vector3(0.028f, 0.075f, 0.010f), CStruct);
                // 힌지 핀(Z축, 회전축) — boom. 클레비스·텅 관통.
                //   핀 길이 ±0.02→±0.028: 거더 z면(gz±0.025) 밖으로 3mm 돌출 → 0°에서도 핀 끝이 보임(매립 해소).
                Rod(boom, "Boom_HingePin",
                    new Vector3(hx, hy, gz - 0.028f), new Vector3(hx, hy, gz + 0.028f), 0.009f, CDark);
                // 핀 리테이너 캡(양 외측, 돌출 핀 끝에)
                foreach (float e in new[] { -1f, 1f })
                    Rod(boom, "Boom_HingePinCap",
                        new Vector3(hx, hy, gz + e * 0.029f), new Vector3(hx, hy, gz + e * 0.033f), 0.013f, CStruct);
            }
        }

        /// <summary>붐 종방향 전장(BoomBackX~BoomTipX) 박스를 힌지(WaterLegX)에서 육지(name)+바다(name_Luff) 2조각으로 분할 생성.
        /// 두 조각 union = 원래 단일 박스와 기하 동일(0°) — 바다측(_Luff)은 러핑 스윕이 편입한다.</summary>
        static void BoomSpanBox(Transform boom, string name, float y, float z, float sizeY, float sizeZ, Color color)
        {
            float x0 = BoomBackX, x1 = BoomTipX, h = WaterLegX;
            Box(boom, name,           new Vector3((x0 + h) * 0.5f, y, z), new Vector3(h - x0, sizeY, sizeZ), color);
            Box(boom, name + "_Luff", new Vector3((h + x1) * 0.5f, y, z), new Vector3(x1 - h, sizeY, sizeZ), color);
        }

        /// <summary>붐 자식 c의 '붐-로컬 X 최소단'(월드 렌더러 바운즈 → 붐 로컬)을 반환. 렌더러 없으면 false.
        /// 붐은 스케일 1·무회전이라 로컬 X = 월드 X − 붐 원점 X(선형). 러핑 재부모화에서 '완전히 바다측' 판정에 사용.</summary>
        static bool TryBoomLocalMinX(Transform c, Transform boom, out float localMinX)
        {
            localMinX = 0f;
            if (!SceneUtil.TryBounds(c, out var b, true)) return false;
            localMinX = b.min.x - boom.position.x;
            return true;
        }
    }
}
#endif
