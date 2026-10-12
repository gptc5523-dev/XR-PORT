#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class StsCraneCreator
    {
        // 견인로프 — 트롤리↔시브 구간·시브 감김을 TrolleyReevingRig가 트롤리 추종(꺾임 0). 바다끝 리버싱 시브는 Tip_Platform 밑에 현수.
        //   시브 휠·현수·텐셔너·supply(시브 탈출 접점→드럼)는 고정. 트롤리쪽 소켓은 트롤리에 부착.
        static void BuildTowReeving(Transform boom, Transform trolley, Transform luffPivot)
        {
            const float ropeR = 0.0035f;
            const int spanPer = 6, arcPer = 18;
            float pitch = BackSheaveR;               // 로프 피치 반경(플랜지 외경)
            float tipX  = BoomTipX - 0.03f;          // 바다끝 시브 X — 육지끝 BackSheaveX(BoomBackX+0.03)와 대칭
            float pTop  = 0.068f;                     // Tip_Platform 윗면(현수 행어 상단)
            float troFrontX = TrolleyRestX + TrolleyCX + TrolleyHX + 0.003f;   // 트롤리 앞끝(boom 좌표, 바다쪽)
            float troFrontLx = troFrontX - TrolleyRestX;                       // 트롤리-로컬 X

            Vector3 OnSheave(Vector3 c, float deg)
            {
                float r = deg * Mathf.Deg2Rad;
                return c + new Vector3(Mathf.Cos(r) * pitch, Mathf.Sin(r) * pitch, 0f);
            }
            float TangDeg(Vector3 c, Vector3 P, bool upper)
            {
                Vector3 d = P - c; d.z = 0f;
                float az  = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float off = Mathf.Acos(Mathf.Clamp01(pitch / d.magnitude)) * Mathf.Rad2Deg;
                return upper ? az + off : az - off;
            }

            var trolleyLocal = new System.Collections.Generic.List<Vector3>();
            var sheaveCenter = new System.Collections.Generic.List<Vector3>();
            var seatR        = new System.Collections.Generic.List<float>();
            var tanSign      = new System.Collections.Generic.List<float>();
            var exitDegs     = new System.Collections.Generic.List<float>();
            var wrapSigns    = new System.Collections.Generic.List<float>();
            var segs         = new System.Collections.Generic.List<Transform>();
            var sheaveXf     = new System.Collections.Generic.List<Transform>();   // [러핑] fall별 팁 시브 마커(luffPivot 하위 → 붐 기립 추종)
            var supGtip      = new System.Collections.Generic.List<Transform>();   // [러핑] 서플라이 팁측 끝(gTip, luffPivot 하위 → 러핑)
            var supMhe       = new System.Collections.Generic.List<Transform>();   // [러핑] 서플라이 기계실측 끝(mhE, boom 하위 → 고정)
            var supRope      = new System.Collections.Generic.List<Transform>();   // [러핑] 서플라이 동적 로프 세그먼트

            foreach (float zs in new[] { -1f, 1f })
            {
                float sz = zs * 0.05f;
                Vector3 C = new Vector3(tipX, BackSheaveY, sz);   // 팁 시브 중심(바다측·러핑)
                // 1단계: 바다끝 리버싱 시브(현수)
                float hangZ = (BackSheaveHZ + 0.003f) + 0.006f;
                for (int hs = -1; hs <= 1; hs += 2)
                    PbBox(boom, "TipSheave_Hanger",
                        new Vector3(tipX, (pTop + BackSheaveY) * 0.5f, sz + hs * hangZ),
                        new Vector3(0.005f, pTop - BackSheaveY, 0.004f), CStruct);
                Sheave(boom, "TipSheave",
                    C + new Vector3(0f, 0f, -BackSheaveHZ), C + new Vector3(0f, 0f, BackSheaveHZ),
                    BackSheaveR, 0.004f, 0.003f, CDark);     // 시브(도르래) — 어두운 강철색
                SheaveNest(boom, C, BackSheaveR, BackSheaveHZ, CStruct);

                // 2단계: 트롤리 앞끝 직결 소켓 + 동적 로프(트롤리 앞끝 → 시브 U반전). supply(시브→드럼)는 다음 조각.
                Vector3 Pt   = new Vector3(troFrontX, -0.025f, sz);
                Vector3 drum = new Vector3(TravelDrumX, TravelDrumY + TravelDrumR, sz);   // 견인 드럼 윗면 — 로프 끝·U반전 끝각
                float degT = TangDeg(C, Pt, true);     // 트롤리(시브 −X 아래) 하부 접점 — φ≈180이라 upper분기가 하부 (정지점, 소켓 정렬용)
                float degD = TangDeg(C, drum, false);  // 드럼(시브 −X 위) 상부 접점 = 탈출각(고정)
                Vector3 Tt = OnSheave(C, degT);
                // 트롤리 앞끝 직결 소켓 — 트롤리에 부착(함께 주행). 정지점 로프방향(Pt→Tt)으로 정렬.
                Vector3 sockDir = Tt - Pt;
                float sockAng = Mathf.Atan2(sockDir.y, sockDir.x) * Mathf.Rad2Deg;
                PbBox(trolley, "TowFore_Socket", new Vector3(troFrontLx, -0.025f, sz), new Vector3(0.020f, 0.010f, 0.010f), CStruct, new Vector3(0f, 0f, sockAng));

                // 동적: 트롤리 앵커 → 시브 상부 접선(+1) → CCW 감김(+1) → 탈출 접점(degD).
                trolleyLocal.Add(new Vector3(troFrontLx, -0.025f, sz));
                sheaveCenter.Add(C); seatR.Add(pitch); tanSign.Add(+1f); exitDegs.Add(degD); wrapSigns.Add(+1f);
                // [러핑] 팁 시브 마커 — luffPivot 하위(붐 기립 시 함께 회전). 리그가 이 월드좌표를 매 프레임 boom-로컬로 읽어 로프 재계산.
                var tipMk = new GameObject("TowFore_TipMarker");
                tipMk.transform.SetParent(luffPivot, worldPositionStays: false);
                tipMk.transform.localPosition = C - new Vector3(WaterLegX, GirderCenterY, 0f);   // C(boom-로컬) → 피벗-로컬(0°서 피벗원점 뺌)
                sheaveXf.Add(tipMk.transform);
                for (int k = 0; k < spanPer + arcPer; k++)
                {
                    var seg = NewPrimitive(PrimitiveType.Cylinder, "TowFore_Rope", boom);
                    Colorize(seg, CCable);
                    segs.Add(seg.transform);
                }

                // 3단계: supply — 시브 → 레인 → 기계실 드럼. y=0.038 — Shoulder_Beam 바닥(0.0464)과 5mm 여유로 통과.
                //   트롤리 본체 상단과 38mm 여유, Boom_Cross(0.061) 아래 유지.
                float supplyY = 0.038f;
                float entryX  = MachineryHouseX + MachineryHouseHX;   // 기계실 앞벽(바다쪽) — supply가 바다끝에서 오니 앞으로 진입(뒷벽까지 안 지나감)
                Vector3 Td   = OnSheave(C, degD);                          // 시브 드럼쪽 접점(reeve 끝)
                Vector3 gTip = new Vector3(tipX - 0.05f, supplyY, sz);     // 시브 옆 붐위 진입(가이드 롤러)
                Vector3 mhE  = new Vector3(entryX, supplyY, sz);           // 기계실 진입(붐위 높이)
                Rod(boom, "TowFore_GuideRoller", gTip + new Vector3(0f, 0f, -0.006f), gTip + new Vector3(0f, 0f, 0.006f), 0.005f, CDark);
                PbBox(boom, "TowFore_GuideBracket", new Vector3(gTip.x, (0.066f + gTip.y) * 0.5f, sz),
                    new Vector3(0.005f, 0.066f - gTip.y + 0.003f, 0.005f), CStruct);   // Tip_Platform 밑면에 롤러 현수(공중부양 해소)
                CableCatenary(boom, "TowFore_RiseRail", Td, gTip, 0.003f, ropeR, CCable, 5, 12, 14);    // 시브 → 붐위 가이드
                // [러핑] 서플라이 = 정적 catenary → 동적 로프. gTip(팁측)은 luffPivot 하위(러핑), mhE(기계실측)는 boom 하위(고정)
                //   → 붐 기립 시 gTip이 팁 따라 올라가 로프가 자동 추종(BoomHoistRig가 매 프레임 두 실좌표 사이로 그림).
                var gMk = new GameObject("TowFore_SupplyGtip");
                gMk.transform.SetParent(luffPivot, worldPositionStays: false);
                gMk.transform.localPosition = gTip - new Vector3(WaterLegX, GirderCenterY, 0f);   // gTip(boom-로컬) → 피벗-로컬
                supGtip.Add(gMk.transform);
                var mMk = new GameObject("TowFore_SupplyMhe");
                mMk.transform.SetParent(boom, worldPositionStays: false);
                mMk.transform.localPosition = mhE;
                supMhe.Add(mMk.transform);
                var supSeg = NewPrimitive(PrimitiveType.Cylinder, "TowFore_Supply", boom);
                Colorize(supSeg, CCable);
                supRope.Add(supSeg.transform);
                Cone(boom, "TowFore_MHFairlead", mhE + new Vector3(0.006f, 0f, 0f), mhE + new Vector3(-0.006f, 0f, 0f), 0.009f, 0.005f, CDark);  // 기계실 앞벽 진입 트럼펫(입구 바다쪽 넓음)
                CableCatenary(boom, "TowFore_ToDrum",   mhE, drum, 0.003f, ropeR, CCable, 5, 12, 14);   // 진입 → 드럼(기계실)

                // 4단계: 유압 텐셔너 — 시브 베어링(y0.02)을 플랫폼(0.068)에서 당겨 로프 장력 관리. 시브 +X 옆 수직 실린더.
                PbBox(boom, "TipTensioner", new Vector3(tipX + 0.016f, (pTop + BackSheaveY) * 0.5f, sz),
                    new Vector3(0.009f, pTop - BackSheaveY, 0.009f), CMachine);                              // 유압 배럴
                Rod(boom, "TipTensioner_Rod", new Vector3(tipX + 0.016f, BackSheaveY + 0.006f, sz),
                    new Vector3(tipX + 0.008f, BackSheaveY, sz), 0.0035f, CDark);                            // 로드 → 시브 베어링
            }

            var host = new GameObject("TowFore_Reeving_Rig");
            host.transform.SetParent(boom, false);
            host.AddComponent<TrolleyReevingRig>().Configure(trolley, trolleyLocal.ToArray(),
                sheaveCenter.ToArray(), seatR.ToArray(), tanSign.ToArray(), exitDegs.ToArray(),
                wrapSigns.ToArray(), segs.ToArray(), spanPer, arcPer, ropeR, 0.004f,
                sheaveXf.ToArray());   // [러핑] 팁 시브 마커 전달 → 리그가 러핑 위치 추종

            // [러핑] 서플라이 동적 리그 — mhE(기계실측 고정) ↔ gTip(팁측 러핑) 사이로 매 프레임 로프 재배치. 붐 기립 시 자동 추종.
            var supHost = new GameObject("TowFore_Supply_Rig");
            supHost.transform.SetParent(boom, false);
            supHost.AddComponent<BoomHoistRig>().Configure(supMhe.ToArray(), supGtip.ToArray(), supRope.ToArray(), ropeR);
        }

        // 권상 윗구간 — 트롤리↔시브 구간과 시브 감김(reeve)을 TrolleyReevingRig가 트롤리 위치에서
        //   매 프레임 재계산해 추종(접점·감김 꺾임 0). 시브 휠·기계실 진입·드럼·하류(Tm→드럼)는 고정. 트롤리쪽 부품은 트롤리에 부착.
        static void BuildHoistUpper(Transform boom, Transform trolley)
        {
            const float ropeR = 0.0035f;
            const int spanPer = 6, arcPer = 18;   // 직선 토막 / 감김 호 토막
            float pitch  = BackSheaveR;          // 로프 중심 피치 반경(플랜지 외경) — 홈바닥(0.008)이면 grooveDepth<ropeR라 로프가 림에 묻힘.
            float entryX = MachineryHouseX - MachineryHouseHX;
            float entryY = 0.08f;

            Vector3 OnSheave(Vector3 c, float deg)
            {
                float r = deg * Mathf.Deg2Rad;
                return c + new Vector3(Mathf.Cos(r) * pitch, Mathf.Sin(r) * pitch, 0f);
            }
            float TangDeg(Vector3 c, Vector3 P, bool upper)                 // 점 P에서 시브(피치 원)로의 외접선 각
            {
                Vector3 d = P - c; d.z = 0f;
                float az  = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float off = Mathf.Acos(Mathf.Clamp01(pitch / d.magnitude)) * Mathf.Rad2Deg;
                return upper ? az + off : az - off;
            }

            var trolleyLocal = new System.Collections.Generic.List<Vector3>();
            var sheaveCenter = new System.Collections.Generic.List<Vector3>();
            var seatR        = new System.Collections.Generic.List<float>();
            var tanSign      = new System.Collections.Generic.List<float>();
            var exitDegs     = new System.Collections.Generic.List<float>();
            var wrapSigns    = new System.Collections.Generic.List<float>();
            var segs         = new System.Collections.Generic.List<Transform>();

            foreach (float sz in new[] { -0.05f, 0.05f })   // side(좌/우 디플렉터)
            {
                // 트롤리쪽 페어리드/롤러 — 트롤리에 부착(함께 주행). 트롤리-로컬 = boom좌표 − TrolleyRestX.
                Vector3 PtS  = new Vector3(TrolleyRestX + TrolleyBackX - 0.003f, -0.025f, sz);   // boom 좌표(정지점)
                float   ptLx = PtS.x - TrolleyRestX;                                             // 트롤리-로컬 X
                PbBox(trolley, "HoistU_TrolleyFairlead", new Vector3(ptLx, -0.025f, sz), new Vector3(0.012f, 0.014f, 0.024f), CStruct);
                Rod(trolley, "HoistU_TrolleyRoller", new Vector3(ptLx - 0.003f, -0.031f, sz),
                    new Vector3(ptLx - 0.003f, -0.019f, sz), 0.004f, CDark);
                // 기계실 진입포트 · 권상 드럼 (고정, 두 폴 포괄 폭)
                PbBox(boom, "MH_RopeEntry_Plate", new Vector3(entryX - 0.002f, entryY, sz),
                    new Vector3(0.004f, 0.026f, 0.032f), CStruct);                                          // 뒷벽 둘레 보강판
                Cone(boom, "MH_RopeFairlead", new Vector3(entryX - 0.014f, entryY, sz),
                    new Vector3(entryX, entryY, sz), 0.013f, 0.006f, CDark);                                 // 트럼펫(2폴 포괄)
                // X −0.02→−0.065: 붐호이스트 윈치(Boom_Hoist_Drum, x−0.02·r0.026)와 같은 자리 동심 관통 → 육지쪽 별도 베이로 이격(gap 0.045>반경합 0.04).
                Rod(boom, "Hoist_Drum", new Vector3(HoistDrumX, HoistDrumY, sz - 0.028f),
                    new Vector3(HoistDrumX, HoistDrumY, sz + 0.028f), HoistDrumR, CMachine);            // 권상 드럼(기계실 내부)

                // side당 1가닥(총 2) — 윗구간은 드럼 로프라 양정 4-fall과 별개. 트롤리 → 시브 → 기계실 → 드럼
                {
                    float zp = sz;
                    Vector3 C    = new Vector3(BackSheaveX, BackSheaveY, zp);
                    Vector3 Pm   = new Vector3(entryX - 0.012f, entryY, zp);
                    Vector3 drum = new Vector3(HoistDrumX, HoistDrumY + HoistDrumR, zp);   // 권상 드럼 윗면

                    float degM = TangDeg(C, Pm, true);     // 기계실쪽: 시브 상부 접선(고정 = 탈출각)
                    Vector3 Tm = OnSheave(C, degM);

                    // 고정 하류: 탈출 접점 Tm → 기계실 → 드럼(이음새는 동적 호 끝 = Tm에서 정확히 만남).
                    CableCatenary(boom, "HoistU_ToMH",   Tm, Pm, 0.004f, ropeR, CCable, 6, 12, 20);
                    CableCatenary(boom, "HoistU_ToDrum", Pm, drum, 0.002f, ropeR, CCable, 4, 12, 10);
                    // 트롤리 내부 인입(은닉) — 트롤리에 부착(함께 주행). 끝점은 본체 중심 내부라 메시에 가려짐.
                    CableCatenary(trolley, "HoistU_IntoTrolley",
                        new Vector3(ptLx, -0.025f, zp), new Vector3(TrolleyBackX + 0.045f, -0.025f, zp), 0f, ropeR, CCable, 3, 12, 8);

                    // 동적: 트롤리 앵커 → 시브 하부 접선(−1) → CW 감김(−1) → 탈출 접점(degM).
                    trolleyLocal.Add(new Vector3(ptLx, -0.025f, zp));
                    sheaveCenter.Add(C); seatR.Add(pitch); tanSign.Add(-1f); exitDegs.Add(degM); wrapSigns.Add(-1f);
                    for (int k = 0; k < spanPer + arcPer; k++)
                    {
                        var seg = NewPrimitive(PrimitiveType.Cylinder, "HoistU_Rope", boom);
                        Colorize(seg, CCable);
                        segs.Add(seg.transform);
                    }
                }
            }

            var host = new GameObject("HoistUpper_Reeving_Rig");
            host.transform.SetParent(boom, false);
            host.AddComponent<TrolleyReevingRig>().Configure(trolley, trolleyLocal.ToArray(),
                sheaveCenter.ToArray(), seatR.ToArray(), tanSign.ToArray(), exitDegs.ToArray(),
                wrapSigns.ToArray(), segs.ToArray(), spanPer, arcPer, ropeR, 0.004f);
        }

        // 시브 네스트 (4단계) — 시브(축 Z) 양옆 치크 플레이트 + 관통 핀 보스(축단 캡 포함).
        //   납작한 실린더로 치크 원판을, 가는 실린더로 축을 표현해 '시브 블록' 느낌으로.
        static void SheaveNest(Transform parent, Vector3 center, float sheaveR, float sheaveHalfZ, Color cheekC)
        {
            float chZ = sheaveHalfZ + 0.003f;
            for (int s = -1; s <= 1; s += 2)
                Rod(parent, "Sheave_Cheek",
                    center + new Vector3(0f, 0f, s * chZ),
                    center + new Vector3(0f, 0f, s * (chZ + 0.004f)), sheaveR * 1.18f, cheekC);
            Rod(parent, "Sheave_Pin",
                center + new Vector3(0f, 0f, -(chZ + 0.01f)),
                center + new Vector3(0f, 0f,  (chZ + 0.01f)), sheaveR * 0.26f, CDark);
            for (int s = -1; s <= 1; s += 2)
                Rod(parent, "Sheave_PinCap",
                    center + new Vector3(0f, 0f, s * (chZ + 0.008f)),
                    center + new Vector3(0f, 0f, s * (chZ + 0.012f)), sheaveR * 0.45f, CStruct);
        }

        /// <summary>붐호이스트 리빙 — 정점 시브(고정)↔붐 브라이들(러핑) 로프를 BoomHoistRig로 동적 연결.
        /// 브라이들 마스트는 luffPivot 하위(추종), 정점 접점 마커는 root 하위(고정). z=±거더간격 2줄.</summary>
        static void BuildBoomHoistReeving(Transform root, Transform luffPivot)
        {
            float apexY   = RailH + ApexH;
            float sheaveY = apexY - 0.042f;                      // 정점 붐호이스트 시브 Y(Apex_BoomHoistSheave와 동일 SSOT)
            // [물리 산출] 브라이들 X = 힌지서 바다측 f=0.72 지점(팁일수록 유리하나 트롤리 간섭 회피로 3/4 지점).
            float bridleX = WaterLegX + 0.72f * (BoomTipX - WaterLegX);
            float bridleTopY = GirderTopLocal + 0.05f;           // 붐 윗면 위 브라이들 마스트 꼭대기(부착점)

            var apexPts = new List<Transform>();
            var boomPts = new List<Transform>();
            var ropes   = new List<Transform>();
            var host = new GameObject("BoomHoist_Reeving");
            host.transform.SetParent(root, worldPositionStays: false);

            // 로프를 시브 허브(r=0)가 아니라 'V홈 접선점'(r=rOuter−grooveDepth=0.013)에 앉힌다 → 원판 관통 대신 홈에 감김.
            //   붐호이스트 로프는 브라이들(바다측) 방향 홈, 급전 로프는 드럼(육지측) 방향 홈에 착점 → 시브 위로 감기는 리빙.
            const float sheaveSeat = 0.016f - 0.003f;
            Vector3 sheaveC  = new Vector3(WaterLegX, sheaveY, 0f);
            Vector3 dBridle  = new Vector3(bridleX, RailH + bridleTopY, 0f) - sheaveC; dBridle.z = 0f; dBridle.Normalize();
            Vector3 dDrum    = new Vector3(MachineryHouseX - 0.02f, RailH + 0.095f, 0f) - sheaveC; dDrum.z = 0f; dDrum.Normalize();
            Vector3 bhSeat   = sheaveC + dBridle * sheaveSeat;   // 붐호이스트 로프 착점(바다측 홈)
            Vector3 feedSeat = sheaveC + dDrum   * sheaveSeat;   // 급전 로프 착점(육지측 홈)

            foreach (float s in new[] { -1f, 1f })
            {
                float gz = s * GirderGapZ;
                // 정점 시브 V홈 접선 착점 마커(root 고정) — 바다측(브라이들 방향) 홈
                var ap = new GameObject("BoomHoist_ApexPt");
                ap.transform.SetParent(root, worldPositionStays: false);
                ap.transform.localPosition = new Vector3(bhSeat.x, bhSeat.y, gz);
                apexPts.Add(ap.transform);

                // 붐 브라이들(러핑 추종) — 삼각 A형 프레임: 발 2개(전후 splay) + 헤드 + 발 타이 + 헤드 피팅.
                //   A다리 2개가 붐 윗면 두 발로 하중 전달(단일 스틱은 굽힘에 취약). pivot-local = 붐-로컬 − 피벗원점.
                Vector3 headL   = new Vector3(bridleX - WaterLegX, bridleTopY   - GirderCenterY, gz);   // 헤드(로프 착점)
                float footY     = GirderTopLocal - GirderCenterY;                                       // 붐 윗면(발 높이)
                float legSpread = 0.045f;                                                               // 발 전후(X) 반간격
                Vector3 footFore = new Vector3(headL.x + legSpread, footY, gz);                         // 바다쪽 발
                Vector3 footAft  = new Vector3(headL.x - legSpread, footY, gz);                         // 육지쪽 발
                Strut(luffPivot, "BoomHoist_BridleLeg", footFore, headL, 0.007f, CStruct);              // A다리(바다)
                Strut(luffPivot, "BoomHoist_BridleLeg", footAft,  headL, 0.007f, CStruct);              // A다리(육지)
                Strut(luffPivot, "BoomHoist_BridleTie", footFore, footAft, 0.005f, CStruct);            // 발 타이(붐 윗면, 두 발 결속)
                Box(luffPivot, "BoomHoist_BridleHead", headL, new Vector3(0.018f, 0.016f, 0.016f), CMachine);  // 헤드 피팅(두 다리 솔리드 결합 + 로프 소켓)
                var bp = new GameObject("BoomHoist_BridlePt");
                bp.transform.SetParent(luffPivot, worldPositionStays: false);
                bp.transform.localPosition = headL;
                boomPts.Add(bp.transform);

                // 동적 로프(정점 시브 → 브라이들) — host 하위 실린더, BoomHoistRig가 매 프레임 배치
                var seg = NewPrimitive(PrimitiveType.Cylinder, "BoomHoist_Rope", host.transform);
                Colorize(seg, CCable);
                ropes.Add(seg.transform);

                // 급전 로프는 붐호이스트 윈치(Boom_Hoist_Drum, 기계실 내부)에서 뽑는다.
                //   정점 시브 → 지붕 위 디플렉터 시브(트럼펫 포트로 지붕 통과) → 수직으로 꺾어 드럼 강하. 꺾임은 시브에서만.
                Vector3 drumP = new Vector3(MachineryHouseX - 0.02f, RailH + 0.095f, gz);   // 붐호이스트 윈치 드럼(기계실 내부)
                Vector3 seatP = new Vector3(feedSeat.x, feedSeat.y, gz);                     // 정점시브 육지측 V홈 접점(고정)
                const float defR = 0.014f;
                float roofY   = RailH + 0.171f;                                             // 기계실 지붕 윗면(SSOT: MH_Roof top = 0.165+0.006)
                Vector3 defC  = new Vector3(drumP.x, roofY + 0.012f, gz);                    // 디플렉터 시브(드럼 바로 위, 지붕서 살짝 띄움)
                Sheave(root, "BoomHoist_FeedDeflector",                                      // 디플렉터 시브(축 Z) — 대각→수직 리다이렉트
                    defC + new Vector3(0f, 0f, -0.006f), defC + new Vector3(0f, 0f, 0.006f), defR, 0.004f, 0.003f, CDark);
                Strut(root, "BoomHoist_FeedDeflStand", defC, new Vector3(defC.x, roofY, gz), 0.005f, CStruct);   // 지붕 위 받침대(부양 0)
                Cone(root, "BoomHoist_FeedPort",                                            // 지붕 로프 포트(트럼펫) — 로프가 지붕을 '구멍'으로 통과(마감)
                    new Vector3(drumP.x, roofY + 0.006f, gz), new Vector3(drumP.x, roofY - 0.010f, gz), 0.011f, 0.006f, CDark);
                Vector3 rimApex = defC + (seatP - defC).normalized * (defR - 0.003f);        // 정점측 V홈 시트
                Vector3 rimDrum = defC + (drumP - defC).normalized * (defR - 0.003f);        // 드럼측 V홈 시트(아래)
                Rod(root, "BoomHoist_Feed", seatP,   rimApex, 0.004f, CCable);               // ① 정점 시브 → 디플렉터(대각 강하)
                Rod(root, "BoomHoist_Feed", rimDrum, drumP,   0.004f, CCable);               // ② 디플렉터 → 드럼(수직 강하, 지붕 포트 통과)
            }

            host.AddComponent<BoomHoistRig>().Configure(apexPts.ToArray(), boomPts.ToArray(), ropes.ToArray(), 0.004f);
        }
    }
}
#endif
