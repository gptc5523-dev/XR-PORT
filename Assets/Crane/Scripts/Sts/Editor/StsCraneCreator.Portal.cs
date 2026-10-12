#if UNITY_EDITOR
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 정적 구조

        // 부두 위 주행 레일 — 안벽(quay)을 따라 Z축으로 깔린다(붐이 뻗는 X와 수직).
        // 육지측·바다측 다리행 아래에 1줄씩(두 레일 간격 = 레일 게이지 = LegSpanX).
        static void BuildGroundRails(Transform root)
        {
            // 크레인 측 레일은 '위치 마커'만 남기고 렌더러 OFF — 시각 레일은 부두(Quay_Ground)에 고정으로 그려진다.
            //   Quay는 이 Rail_ Transform의 X를 읽어 같은 X로 긴 고정 레일을 깐다(크레인과 같이 움직이지 않음).
            float railLen = GaugeZ + 0.5f;
            foreach (float x in new[] { LandLegX, WaterLegX })
            {
                var rail = Box(root, StsPartNames.RailPrefix + (x == LandLegX ? "Land" : "Water"),
                    new Vector3(x, 0.004f, 0f),
                    new Vector3(StsConfig.RailSectionW, StsConfig.RailSectionH, railLen), CRail);  // [H2] 레일 단면 SSOT
                var mr = rail.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
        }

        // 포털 게이트: 다리 4개 + 실 빔 + 보기 + 상부 크로스 빔 + 대각 브레이스
        static void BuildPortal(Transform root)
        {
            float halfZ = GaugeZ * 0.5f;
            float[] legX = { LandLegX, WaterLegX };
            // 다리 상단 = LegTopY(공통 상수). 붐 거더 윗면에 크로스빔이 붙고, A프레임 베이스도 같은 높이로 정합.
            float legTopY = LegTopY;
            // 다리 하단 = Leg_BasePlate 윗면 — 0이면 베이스·보기·레일을 뚫고 지면에 박힌다.
            float legFootY = 0.072f;

            foreach (float x in legX)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * halfZ;
                    // 다리 — 격자 트러스(코너 포스트 + 가로 rung + 대각 lacing)
                    BuildLatticeLeg(root, x, z, legFootY, legTopY, LegSec * 1.7f);
                    // 보기(주행 대차) — 굴절 이퀄라이저 트럭: 중앙 피벗 1 → 서브 이퀄라이저 2 → 바퀴 4(균등 피치 분산).
                    {
                        float by = 0.05f;                          // 메인 이퀄라이저 빔 높이
                        var wheelC = new Color(0.05f, 0.05f, 0.06f);
                        // 4륜 오프셋 {-1.5,-0.5,+0.5,+1.5}×p — 트레드 지름 0.026·플랜지 지름 0.0299 < 피치 0.032라 비간섭.
                        const float p  = StsConfig.BogieEqualizerPitch;   // [H2] 보기 4륜 균등 피치 SSOT
                        float bz = StsConfig.BogieLengthZ;                // [H2] 보기 전장(Z) SSOT
                        // 메인 이퀄라이저 빔(다리 하단 중앙 피벗으로 매달림) + 피벗 핀(축 X)
                        Box(root, "Bogie_Equalizer", new Vector3(x, by, z),
                            new Vector3(LegSec * 0.5f, 0.014f, bz), CStruct);
                        Rod(root, "Bogie_Pivot",
                            new Vector3(x - LegSec * 0.55f, by, z), new Vector3(x + LegSec * 0.55f, by, z), 0.005f, CDark);
                        // 보기 이퀄라이저 ↔ 베이스플레이트 하중경로 연결 — 킹핀 클레비스(귀판 2장).
                        //   귀판 y[0.047,0.063]: 플레이트 밑면(0.060)에 물리고 핀(0.05)·이퀄라이저 윗면(0.057)까지 연속.
                        for (int ce = -1; ce <= 1; ce += 2)
                            Box(root, "Bogie_PivotClevis", new Vector3(x + ce * 0.009f, by + 0.005f, z),
                                new Vector3(0.004f, 0.016f, 0.02f), CStruct);
                        for (int sb = -1; sb <= 1; sb += 2)
                        {
                            float sbz = z + sb * p;                // 서브 이퀄라이저 중심 = ±p (각자 휠 2개 담당)
                            Box(root, "Bogie_SubEqualizer", new Vector3(x, by - 0.016f, sbz),
                                new Vector3(LegSec * 0.42f, 0.011f, p * 1.25f), CStruct);   // Z len 0.04, 두 서브 간극 0.024
                            Rod(root, "Bogie_SubPivot",            // 메인↔서브 피벗(축 X)
                                new Vector3(x - LegSec * 0.46f, by - 0.008f, sbz),
                                new Vector3(x + LegSec * 0.46f, by - 0.008f, sbz), 0.004f, CDark);
                            // 측면 프레임(바퀴 가드)
                            for (int fz = -1; fz <= 1; fz += 2)
                                Box(root, "Bogie_SideFrame", new Vector3(x + fz * LegSec * 0.5f, 0.024f, sbz),
                                    new Vector3(0.005f, 0.03f, p * 1.4f), CStruct);
                            // 바퀴 2(서브 빔 양 끝, 오프셋 ±0.5p) + 허브 보스 + 저널 박스
                            for (int w = -1; w <= 1; w += 2)
                            {
                                float wz = sbz + w * (p * 0.5f);   // ±0.016 → 4륜 전체 균등 피치 0.032
                                // 트레드 중심 y=0.021 = 레일 윗면(0.008)+트레드 반경(0.013) → 접지, 플랜지 하단 y=0.006이 레일 옆면을 묾.
                                RailWheel(root, "Wheel", new Vector3(x, 0.021f, wz),
                                    Vector3.right, 0.013f, LegSec * 0.44f, wheelC);
                                for (int hs = -1; hs <= 1; hs += 2)
                                    Rod(root, "Wheel_Hub",
                                        new Vector3(x + hs * LegSec * 0.5f, 0.021f, wz),
                                        new Vector3(x + hs * LegSec * 0.66f, 0.021f, wz), 0.006f, CDark);
                                Box(root, "Bogie_AxleBox", new Vector3(x, 0.024f, wz),
                                    new Vector3(LegSec * 0.5f, 0.012f, 0.01f), CMachine);
                            }
                        }
                    }
                }
            }

            // 좌우 다리를 잇는 상부 크로스 빔(포털 상단) — X 위치마다 1개.
            //   붐 상부 보도가 같은 높이대라, 포털 통과 지점(육지/바다 다리 X)에서 끊어 개구부로 지나감(BoomTopWalkwayGaps).
            foreach (float x in legX)
            {
                // 상부 게이지 횡빔(Shoulder_Beam, 구 Portal_Cross — 실물용어 정합 개명. 실제 "portal beam"은 Portal_TieBeam)
                //   — 빌트업 I-빔(웹+상·하 플랜지) + 수직 스티프너 + 단부 캡판. 플랜지 X폭(LegSec×1.1)이 붐 상부 보도 개구부(LegSec×0.55+여유)와 정합.
                float pcY = legTopY - LegSec * 0.5f;
                float pcLenZ = GaugeZ + LegSec;
                PbBox(root, "Shoulder_Beam", new Vector3(x, pcY, 0f),
                    new Vector3(0.01f, LegSec, pcLenZ), CStruct);                       // 웹(수직판)
                for (int sy = -1; sy <= 1; sy += 2)
                    PbBox(root, "Shoulder_Beam_Flange", new Vector3(x, pcY + sy * (LegSec * 0.5f - 0.0025f), 0f),
                        new Vector3(LegSec * 1.1f, 0.005f, pcLenZ), CStruct);            // 상·하 플랜지
                int pcStiff = 6;
                for (int i = 0; i <= pcStiff; i++)
                {
                    float sz = Mathf.Lerp(-halfZ + 0.05f, halfZ - 0.05f, i / (float)pcStiff);
                    PbBox(root, "Shoulder_Beam_Stiffener", new Vector3(x, pcY, sz),
                        new Vector3(LegSec * 1.05f, LegSec * 0.9f, 0.005f), CStruct);     // 웹 수직 스티프너
                }
                for (int s = -1; s <= 1; s += 2)   // 단부 캡판(다리 상단 접속부 마감)
                    PbBox(root, "Shoulder_Beam_EndCap", new Vector3(x, pcY, s * pcLenZ * 0.5f),
                        new Vector3(LegSec * 1.1f, LegSec, 0.005f), CStruct);
            }

            // 측면 대각 브레이스(앞/뒤 다리 사이) — Portal_TieBeam(RailH*0.4)을 하현재로 그 위에 X 패턴.
            //   [디자인] 민짜 막대 → 빌트업(웹+플랜지) 부재 + 절점 거싯판·볼트로 퀄리티 보강.
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * halfZ;
                BuiltUpBrace(root, "Brace",
                    new Vector3(LandLegX, RailH * 0.4f, z), new Vector3(WaterLegX, RailH * 0.92f, z), CStruct);
                BuiltUpBrace(root, "Brace",
                    new Vector3(WaterLegX, RailH * 0.4f, z), new Vector3(LandLegX, RailH * 0.92f, z), CStruct);
                // 중앙 X 교차 절점 거싯 + 볼트 링 (다리 단부 절점 RailH*0.4·0.92 는 BuildBoltedJoints가 이미 처리 → 중복 회피).
                Quaternion fr = s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                float midX = (LandLegX + WaterLegX) * 0.5f;
                BoltedPlate(root, new Vector3(midX, RailH * 0.66f, z), fr, 0.044f, 4, 4, 0.016f, 0.016f, 0.0022f);  // 포털 중간 4×4=16
            }

            // 다리 타이빔 — 같은 쪽 두 다리(육지·바다)를 잇는 게이지(X)방향 횡 타이(다리 40% 높이 RailH*0.4).
            //   '실빔(Sill)'은 오칭(실은 레일 부근 최하단 부재) → Portal_TieBeam로 개명. 위치·역할은 유지.
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, "Portal_TieBeam",
                    new Vector3((LandLegX + WaterLegX) * 0.5f, RailH * 0.4f, s * halfZ),
                    new Vector3(LegSpanX + LegSec, 0.02f, LegSec * 0.9f), CStruct);
            }
            // ※ 게이지(Z) 하부 횡빔/대각은 두지 않는다 — 포털 다리 사이는 컨테이너·트럭이 지나는 통로라 막으면 안 됨.
            //   게이지 방향은 상단 Shoulder_Beam + 다리로 버티는 모멘트 프레임(하부는 통과용으로 비움).

            // 전체 디테일 보강 (베이스/다리)
            foreach (float x in legX)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float lz = s * halfZ;
                    // 다리 베이스 받침 플레이트
                    Box(root, "Leg_BasePlate", new Vector3(x, 0.066f, lz),
                        new Vector3(LegSec * 2.4f, 0.012f, LegSec * 2.0f), CStruct);
                }
            }
            // 도관/케이블 번들 — 육지쪽 다리 +Z 코너를 따라 '베이스 플레이트(아래) → 붐 거더 밑(위)'까지 연속.
            //   양 끝을 정션박스로 구조물에 접속(아래=베이스, 위=붐 밑) → 위가 허공에 끊겨 떠 보이던 것 해결(계산: 끝점을 구조물에 맞춤).
            float condX = LandLegX - 0.034f;   // 다리 밖(-X)으로 — 다리 -X면 럼/래이싱(x[-0.025,-0.017])에 안 박히게(클램프로 다리에 부착)
            float condBotY = 0.072f;          // 베이스 플레이트 윗면(0.066 + 0.012/2) — 바닥 정션박스 접속
            float condTopY = RailH - 0.005f;  // 다리 상단 코너 포스트(z≈0.333) — 상단 정션박스로 접속(거더 z=±0.16엔 안 닿음)
            for (int c = -1; c <= 1; c++)   // 3줄 번들
                Rod(root, "Leg_Conduit",
                    new Vector3(condX, condBotY, halfZ + c * 0.006f),
                    new Vector3(condX, condTopY, halfZ + c * 0.006f), 0.0035f, CDark);
            // 클램프(고정 브래킷) 다단 — 전 구간을 다리에 붙임
            for (int i = 0; i <= 8; i++)
            {
                float cy = Mathf.Lerp(condBotY + 0.02f, condTopY - 0.02f, i / 8f);
                Box(root, "Conduit_Clamp", new Vector3(condX + 0.008f, cy, halfZ),
                    new Vector3(0.018f, 0.005f, 0.026f), CStruct);
            }
            // 정션 박스(상·하) — 도관 양 끝에 정확히 얹어 구조물 접속부 표현(끝이 안 뜨게)
            Box(root, "Junction_Box", new Vector3(condX, condBotY + 0.016f, halfZ),
                new Vector3(0.02f, 0.032f, 0.026f), CMachine);
            Box(root, "Junction_Box", new Vector3(condX, condTopY - 0.016f, halfZ),
                new Vector3(0.02f, 0.032f, 0.026f), CMachine);

            // 베이스/부두 인터페이스 디테일
            foreach (float x in legX)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float lz = s * halfZ;
                    // 폭풍 계류 스토우 핀(부두에 박는 핀) + 하우징
                    Box(root, "Stow_Pin_Housing", new Vector3(x, 0.045f, lz + s * LegSec * 1.5f),
                        new Vector3(0.022f, 0.05f, 0.022f), CMachine);
                    Rod(root, "Stow_Pin", new Vector3(x, 0f, lz + s * LegSec * 1.5f),
                        new Vector3(x, 0.05f, lz + s * LegSec * 1.5f), 0.006f, CDark);
                    // 타이다운 러그(부두 고정 패드아이) — 베이스 플레이트(+X 가장자리, y0.060~0.072) 밑면에 용접해 아래로 늘어뜨림.
                    //   플레이트 +X면(x+0.03)에 물려 top 0.067로 박고 0.037까지 늘어뜨림.
                    Box(root, "Tiedown_Lug", new Vector3(x + 0.034f, 0.052f, lz),
                        new Vector3(0.01f, 0.03f, 0.016f), CStruct);
                    // 폭풍 계류 타이로드(봉+발바닥 일체) — 러그 하단에서 안벽으로 내려와 발바닥(풋)으로 선다.
                    //   세 조각(샤프트·보스·풋) 모두 "Tiedown_Rod*"로 명명 — TiedownController가 같이 들어올려 강체 유지.
                    Rod(root, StsPartNames.TiedownRodPrefix, new Vector3(x + 0.034f, 0.037f, lz),
                        new Vector3(x + 0.034f, 0.014f, lz), 0.004f, CDark);
                    // 단조 보스 — 둥근 봉을 평평한 발바닥에 매끈히 물리는 짧은 칼라(평강 모서리엔 구 금지, 솔리드 전이).
                    Rod(root, "Tiedown_Rod_Boss", new Vector3(x + 0.034f, 0.014f, lz),
                        new Vector3(x + 0.034f, 0.008f, lz), 0.007f, CMachine);
                    // 발바닥(풋 베이스) — 안벽에 서는 단조 베이스 패드. 봉과 한 부품으로 함께 들림.
                    PbBox(root, "Tiedown_Rod_Foot", new Vector3(x + 0.034f, 0.004f, lz),
                        new Vector3(0.018f, 0.008f, 0.018f), CMachine);
                    // 휠 레일 스위퍼(주행방향 Z, 보기 앞/뒤 끝) — 레일 위 이물질을 밀어내는 플라우.
                    //   브래킷+경사 디플렉터 블레이드+마모 스트립+측면 거싯 구성. 스위퍼 z=±0.07로 휠(±0.048) 비간섭.
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float swz = lz + e * 0.07f;                  // 스위퍼 Z(보기 앞/뒤 끝, 외측 휠 너머)
                        // 마운팅 브래킷(보기 끝 프레임으로 올라가는 수직판) — 메인 빔 끝까지 Z로 연장해 스위퍼를 보기에 물림.
                        float brZ0 = lz + e * 0.052f;   // 보기 메인 이퀄라이저 빔 끝(±bz/2, bz=p*3.25=0.104)
                        PbBox(root, "Sweeper_Bracket", new Vector3(x, 0.032f, (brZ0 + swz) * 0.5f),
                            new Vector3(0.02f, 0.024f, Mathf.Abs(swz - brZ0) + 0.006f), CStruct);
                        // 경사 디플렉터 블레이드(주행방향으로 기울인 플라우 면) — 폭 레일보다 넓게
                        PbBox(root, StsPartNames.RailPrefix + "Sweeper", new Vector3(x, 0.019f, swz),
                            new Vector3(0.026f, 0.018f, 0.004f), CDark, new Vector3(e * 22f, 0f, 0f));
                        // 마모 스트립(레일을 긁는 교체식 하단 웨어 플레이트) — 레일 바로 위
                        PbBox(root, "Sweeper_WearStrip", new Vector3(x, 0.010f, swz + e * 0.004f),
                            new Vector3(0.026f, 0.003f, 0.005f), CMachine);
                        // 측면 거싯(플라우 옆판, X 양끝)
                        for (int g = -1; g <= 1; g += 2)
                            PbBox(root, "Sweeper_Cheek", new Vector3(x + g * 0.013f, 0.015f, swz),
                                new Vector3(0.004f, 0.014f, 0.012f), CStruct);
                    }
                }
            }
        }

        // 디테일 지오메트리 (D)

        // 격자 트러스 다리 — 4 코너 포스트 + 가로 rung + 4면 대각 lacing
        static void BuildLatticeLeg(Transform root, float cx, float cz, float y0, float y1, float foot)
        {
            float h = y1 - y0;
            float half = foot * 0.5f;
            float postT = foot * 0.28f;

            // 4 코너 포스트
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Box(root, StsPartNames.LegPost, new Vector3(cx + sx * half, (y0 + y1) * 0.5f, cz + sz * half),
                    new Vector3(postT, h, postT), CStruct);
            }

            int segs = Mathf.Max(4, Mathf.RoundToInt(h / 0.10f));
            float step = h / segs;

            // 가로 rung — 매 노드, 4면
            for (int i = 0; i <= segs; i++)
            {
                float y = y0 + step * i;
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(root, "Leg_Rung", new Vector3(cx, y, cz + sz * half),
                        new Vector3(foot, postT * 0.7f, postT * 0.7f), CStruct);
                for (int sx = -1; sx <= 1; sx += 2)
                    Box(root, "Leg_Rung", new Vector3(cx + sx * half, y, cz),
                        new Vector3(postT * 0.7f, postT * 0.7f, foot), CStruct);
            }

            // 대각 lacing — 4면 지그재그
            for (int i = 0; i < segs; i++)
            {
                float ya = y0 + step * i;
                float yb = y0 + step * (i + 1);
                bool up = (i % 2) == 0;
                for (int sz = -1; sz <= 1; sz += 2)
                    Strut(root, "Leg_Lace",
                        new Vector3(cx - half, up ? ya : yb, cz + sz * half),
                        new Vector3(cx + half, up ? yb : ya, cz + sz * half), postT * 0.55f, CStruct);
                for (int sx = -1; sx <= 1; sx += 2)
                    Strut(root, "Leg_Lace",
                        new Vector3(cx + sx * half, up ? ya : yb, cz - half),
                        new Vector3(cx + sx * half, up ? yb : ya, cz + half), postT * 0.55f, CStruct);
            }

            // 절점 연결판(거싯) — 라싱/포스트 교차 노드에 면마다 챔퍼 8각 판(용접 연결 표현).
            //   순수 추가(기존 포스트/라싱 좌표는 그대로) — 4 코너 포스트가 만나는 ±X·±Z 면에 부착.
            float gSize = postT * 0.85f;          // 코너 포스트보다 약간 큰 정도(삐져나옴 방지)
            float gThick = postT * 0.22f;
            float gOff = gThick * 0.55f;          // 면에서 살짝 띄워 z-fighting 방지
            // 거싯 중심을 모서리 안쪽으로 당겨, 바깥 모서리를 코너 포스트 외곽면(half+postT/2)에 맞춤 → 다리 envelope 밖으로 안 나감
            float gCorner = half + postT * 0.5f - gSize;
            for (int i = 2; i < segs; i += 3)     // 내부 노드 일부(과밀 방지)
            {
                float y = y0 + step * i;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    // ±Z 면(코너 x=cx±half) — X로 안쪽 당김
                    Gusset(root, new Vector3(cx + sx * gCorner, y, cz + half + gOff),
                        Quaternion.identity,        gSize, gThick, CStruct);
                    Gusset(root, new Vector3(cx + sx * gCorner, y, cz - half - gOff),
                        Quaternion.Euler(0, 180, 0), gSize, gThick, CStruct);
                    // ±X 면(코너 z=cz±half) — Z로 안쪽 당김
                    Gusset(root, new Vector3(cx + half + gOff, y, cz + sx * gCorner),
                        Quaternion.Euler(0, 90, 0),  gSize, gThick, CStruct);
                    Gusset(root, new Vector3(cx - half - gOff, y, cz + sx * gCorner),
                        Quaternion.Euler(0, -90, 0), gSize, gThick, CStruct);
                }
            }

            // 갠트리 주행 시 레일 위 장애물과 충돌하도록 다리 하나를 감싸는 BoxCollider 1개(격자 부재는 시각용이라 콜라이더 없음).
            //   크레인 루트의 kinematic Rigidbody가 밀어낸다. 콜라이더는 지면(0)까지 풀하이트(시각 다리는 베이스 위).
            float colY0 = 0f;
            float colH  = y1 - colY0;
            var legCol = new GameObject(Numbered(StsPartNames.LegCollider));
            legCol.transform.SetParent(root, worldPositionStays: false);
            legCol.transform.localPosition = new Vector3(cx, (colY0 + y1) * 0.5f, cz);
            legCol.AddComponent<BoxCollider>().size = new Vector3(foot, colH, foot);
        }

        // 접근 디테일: 다리 수직 사다리 + 포털 상단 점검 플랫폼 + 난간
        static void BuildAccessDetails(Transform root)
        {
            // 다리 오른쪽(star측) 케이지 수직 사다리 — 격자 다리 외곽 밖으로 띄워 겹침 방지
            float legOuter = GaugeZ * 0.5f + LegSec * 1.7f * 0.5f;   // 격자 다리 외곽 z
            float ladderZ  = legOuter + 0.022f;                     // 사다리 위치
            // 붐 데크(붐 로컬 0.067 → 루트 RailH+0.067)까지 올려 기계실 접근 캣워크와 연결
            float ly0 = 0.072f, ly1 = RailH + 0.067f;   // 사다리는 다리·하우징 위에서 시작(legFootY 0.072) — 그 아래로 내리면 부유/관통.
            float ladW = 0.03f;                       // 사다리 폭(stile 간격) — 그립 연장과 공유
            BuildLadder(root, LandLegX, ladderZ, ly0, ly1, ladW);

            // 사다리 상단 그립 연장 — 데크(ly1) 위로 stile을 더 올려 잡고 내려서는 손잡이 + 상단 가로대.
            //   데크가 사다리 가장자리에서 끝나(MHAccess_Deck step-off) 머리 위가 비므로, 이 연장으로 안전 승강 마감.
            float grabH = 0.04f;
            for (int s = -1; s <= 1; s += 2)
                Box(root, "Ladder_Grab", new Vector3(LandLegX + s * ladW * 0.5f, ly1 + grabH * 0.5f, ladderZ),
                    new Vector3(0.004f, grabH, 0.004f), CSafety);
            Rod(root, "Ladder_Grab_Top",
                new Vector3(LandLegX - ladW * 0.5f, ly1 + grabH, ladderZ),
                new Vector3(LandLegX + ladW * 0.5f, ly1 + grabH, ladderZ), 0.0022f, CSafety);

            // 다리에 고정하는 standoff 브래킷
            for (int i = 0; i <= 5; i++)
            {
                float by = Mathf.Lerp(ly0, ly1, i / 5f);
                Box(root, "Ladder_Bracket",
                    new Vector3(LandLegX, by, (legOuter + ladderZ) * 0.5f),
                    new Vector3(0.005f, 0.005f, ladderZ - legOuter), CSafety);
            }

            // 안전 케이지 — 외측 세로 가드바 3 + 후프(ㄷ자) 다단 (하부는 승하강 위해 생략)
            float cageZ = ladderZ + 0.03f;
            float cy0 = ly0 + 0.12f;
            foreach (float gx2 in new[] { -0.022f, 0f, 0.022f })
            {
                Box(root, "Cage_Bar", new Vector3(LandLegX + gx2, (cy0 + ly1) * 0.5f, cageZ),
                    new Vector3(0.004f, ly1 - cy0, 0.004f), CSafety);
            }
            // 측면 세로 가드바 — 케이지 옆이 후프만으로 뚫리지 않게 좌우 각 1 (둘레 5개)
            for (int sb = -1; sb <= 1; sb += 2)
                Box(root, "Cage_Bar", new Vector3(LandLegX + sb * 0.025f, (cy0 + ly1) * 0.5f, (cageZ + ladderZ) * 0.5f),
                    new Vector3(0.004f, ly1 - cy0, 0.004f), CSafety);
            // 후프 간격 실척 ~0.84m(모델 0.035)
            int hoops = Mathf.Max(2, Mathf.RoundToInt((ly1 - cy0) / 0.035f));
            for (int i = 0; i <= hoops; i++)
            {
                float hy = Mathf.Lerp(cy0, ly1, i / (float)hoops);
                Box(root, "Cage_Hoop", new Vector3(LandLegX, hy, cageZ),
                    new Vector3(0.054f, 0.004f, 0.004f), CSafety);   // 뒤 후프 양끝 코너 +t/2씩(측면 후프 바깥면까지), center 유지
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(root, "Cage_Hoop", new Vector3(LandLegX + s * 0.025f, hy, (cageZ + ladderZ) * 0.5f + 0.001f),
                        new Vector3(0.004f, 0.004f, cageZ - ladderZ + 0.002f), CSafety);   // 측면 후프 cageZ쪽(뒤 후프) 코너만 +t/2, 사다리 입구쪽 유지
                }
            }


            // 비활성: 갠트리 페스툰 트랙(사용자 요청 숨김) — git 이력 참조

            // 비활성: 포털 상단 점검 캣워크(Access_Platform 등, 사용자 요청 제거) — git 이력 참조
        }

        // 수직 사다리: 양옆 stile + 가로 rung(원통)
        static void BuildLadder(Transform parent, float x, float z, float y0, float y1, float widthX)
        {
            float midY = (y0 + y1) * 0.5f;
            float h = y1 - y0;
            for (int s = -1; s <= 1; s += 2)
            {
                Box(parent, "Ladder_Stile", new Vector3(x + s * widthX * 0.5f, midY, z),
                    new Vector3(0.004f, h, 0.004f), CSafety);
            }
            int rungs = Mathf.Max(2, Mathf.RoundToInt(h / 0.0125f));   // 가로대 간격 실척 0.3m(표준)
            for (int i = 0; i <= rungs; i++)
            {
                float ry = Mathf.Lerp(y0, y1, i / (float)rungs);
                Rod(parent, "Ladder_Rung",
                    new Vector3(x - widthX * 0.5f, ry, z),
                    new Vector3(x + widthX * 0.5f, ry, z), 0.0022f, CSafety);
            }
        }

        // 주요 구조 접합부 연결판 + 볼트 — 다리 상·하단, 포털, 타이빔/브레이스 절점(루트 로컬).
        static void BuildBoltedJoints(Transform root)
        {
            float halfZ = GaugeZ * 0.5f;
            float legHalf = LegSec * 1.7f * 0.5f;   // 격자 다리 외곽 반폭
            float[] legX = { LandLegX, WaterLegX };

            foreach (float x in legX)
            for (int s = -1; s <= 1; s += 2)
            {
                // 면 회전: +Z 외측 면=identity, -Z 외측 면=180°
                Quaternion fr = s > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                float zOuter = s * (halfZ + legHalf + 0.0006f);   // 다리 바깥 ±Z 면

                // 1) 다리 상단(포털 크로스/A프레임/거더 수렴) — 볼트 연결판.
                //    판 폭(2*0.02=0.04)을 다리 외곽폭(LegSec*1.7=0.0425) 안으로 유지 → 가장자리 삐져나옴 방지.
                BoltedPlate(root, new Vector3(x, LegTopY - 0.02f, zOuter),
                    fr, 0.02f, 3, 4, 0.010f, 0.0085f, 0.0019f);   // 다리상단 주절점 3×4=12

                // 2) Portal_TieBeam ↔ 다리 절점(RailH*0.4)
                BoltedPlate(root, new Vector3(x, RailH * 0.4f, zOuter),
                    fr, 0.02f, 3, 3, 0.011f, 0.011f, 0.0019f);    // 타이빔 절점 3×3=9

                // 3) 측면 대각 브레이스 상단 절점(RailH*0.92) — 두 X-브레이스가 각 다리 상단에서 만남
                BoltedPlate(root, new Vector3(x, RailH * 0.92f, zOuter),
                    fr, 0.017f, 3, 2, 0.009f, 0.008f, 0.0018f);   // 브레이스 절점 3×2=6

                // 4) 다리 베이스 받침판 앵커 볼트 그리드(위로 향함)
                Bolts(root, new Vector3(x, 0.073f, s * halfZ), Quaternion.Euler(-90, 0, 0),
                    "grid3x2", BoltGrid(3, 2, 0.016f, 0.012f), 0.0022f, 0.0004f);
            }
        }
    }
}
#endif
