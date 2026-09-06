#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// 메뉴에서 STS(Ship-To-Shore) Crane GameObject 계층을 자동 생성.
    /// 
    /// 생성되는 hierarchy:
    ///     STS_Crane                       (StsCrane) — 다리·포털·A프레임·스테이 케이블(정적)
    ///       └─ Boom                       (붐 거더 + 기계실, 정적)
    ///           └─ Trolley                (TrolleyMover, X 슬라이딩)
    ///           └─ SpreaderRoot           (트롤리 X를 따라감)
    ///               └─ Spreader           (SpreaderHoist, Y 승강)
    ///                   └─ AttachPoint     (SpreaderAttach, 컨테이너 부착)
    /// 
    /// 형상은 박스/스트럿 primitive 조합으로 실제 STS 크레인 실루엣(포털 다리 4개,
    /// 격자 붐, A-프레임 정상, 포어/백 스테이, 기계실, 트롤리·스프레더)을 흉내낸다.
    /// 치수는 1/24 미니어처(컨테이너와 비례)에 맞춘 기본값.
    /// 
    /// 트롤리/스프레더는 무버 컴포넌트로 분리돼 있고, 루트에 VR 수동 조종(StsCraneVRController)·
    /// 컨테이너 집기(SpreaderGrabber) 드라이버가 붙는다. (데모 자동사이클은 제거 — 시나리오 부착 시에만 구동)
    /// 
    /// ※ 보류(미사용)된 디테일 형상 블록(포털브레이스·데빗·접근계단 등 12종)은 백업에만 보존:
    ///    ~/Backups/Container_문서삭제_DEFERRED_2026-07-13/DEFERRED_DETAILS.md (2026-07-13 문서정리로 워킹트리서 제거).
    /// </summary>
    public static partial class StsCraneCreator
    {
        const float Scale = StsConfig.ModelScale;   // SSOT — 런타임 StsConfig.ModelScale(1/24)과 동일. const은 const 참조 가능.

        // 트롤리 가동 (붐 로컬 X) — 음수=육지쪽 backreach, 양수=바다쪽 outreach
        // Panamax→Post-Panamax 팔(아웃리치)·백리치 정합.
        //   백리치 = −TrolleyMinX(실척) = 13 + BoomBackExtra×24(=2.88) = 15.9m. 백리치/아웃리치 = 15.9/45 = 0.35 ∈ 0.35~0.45 ✓.
        //   아웃리치 = TrolleyMaxX−WaterLegX = 63−18 = 45m. 아웃리치/높이 = 45/44 = 1.02 ✓. 트롤리 총주행 = 45+18+15.9 = 78.9m.
        const float TrolleyMinX  = -13f * Scale - BoomBackExtra;   // 백트래블 한계(실척 ~16m 백리치) — 거더 백리치 연장(BoomBackExtra)만큼 더 뒤로
        const float TrolleyMaxX  =  63f * Scale;   // 트롤리 바다쪽 한계(붐 로컬 X) = 실척 63m. 아웃리치(=TrolleyMaxX−WaterLegX)= 63−18 = 45m
        const float TrolleyRestX =  8f  * Scale;   // ≈  0.333m

        // 높이/치수
        // 32→44: Panamax→Post-Panamax 양정 상향.
        //   양정 = SpreaderMaxY−SpreaderMinY = (−4 −(−(RailH−0.8)))×Scale = (RailH−4.8)×Scale = (44−4.8)/24 → 실척 39.2m ∈ Post-PMX 36~40m ✓.
        //   레그(legTopY=RailH+0.088)·브레이스(RailH*0.4,0.92)·양정(SpreaderMinY)은 RailH 파생이라 자동 전파. ApexH는 비율 유지 위해 동반 상향(아래).
        //   ※ 가설·Quest/렌더 미검증([[feedback_dont_claim_fixed_without_test]]) — 스테이/시브 클리어런스는 스크린샷 수렴 필요. 되돌리지 말 것([[feedback_fix_dont_revert_chosen_feature]]).
        const float RailH    = 44f   * Scale;      // 붐(트롤리 레일) 높이 = 다리 높이 ≈ 실척 44m (Post-Panamax). 양정 39.2m
        const float ApexH    = 27.5f * Scale;      // A-프레임 정상 높이 — 20→27.5. A프레임/레그 비율 0.625(=20/32=27.5/44) 유지 → 백스테이 클리어런스·실루엣 비례 보존
        const float GaugeZ   = StsConfig.GantryBaseZMeters * Scale;   // 갠트리 베이스(주행방향 다리행 간격, Z) — 레일 게이지 아님. SSOT=StsConfig.GantryBaseZMeters(16). 40ft(12.192/24=0.508m) 길이 + 스프레더 양끝 클리어런스 수용
        const float LegSpanX = StsConfig.LegGaugeXMeters   * Scale;   // 레일 게이지(X, 육지/바다 다리 간격=두 주행레일 간격). SSOT=StsConfig.LegGaugeXMeters(실척 15m). 비율(게이지≈0.6×아웃리치). 트롤리 아웃리치 27m
        const float LegSec   = 1.0f * Scale;       // 다리 단면 한 변 — 0.6→1.0. footprint=LegSec×1.7=1.70m, 코너포스트=×0.28=0.48m,
                                                   //   세장비 44/1.70=25.9:1(현장 22~28 범위). 디자인팀 "얇다" 지적 → 현실 정상화. 파생(포스트·라싱·베이스) 자동 전파.
        const float LegTopY  = RailH + 0.088f;     // 포털 다리/상부 크로스빔/A프레임 베이스 공통 상단. 붐 거더 윗면(≈RailH+0.065)보다 살짝 위 → 거더가 크로스빔에 붙고 트롤리·거더가 그 아래
        // 트윈(더블 박스) 거더 — 두 박스 거더를 z=±GirderGapZ에 두고 사이를 횡프레임·평면 대각으로 결속.
        const float GirderGapZ   = 0.16f;                          // 각 거더 중심 Z — 붐 바깥 끝쪽까지 넓게(다리 게이지 ±0.333 안쪽)
        const float GirderWidthZ = 0.055f;                         // 각 박스 거더 단면 폭(Z) — 0.045→0.055 확대(깊이 0.075와 균형 1.36:1, 갭 0.265 유지)
        const float GirderOuterZ = GirderGapZ + GirderWidthZ * 0.5f;// 트윈 거더 바깥 가장자리(캣워크 난간 위치)
        // 박스 거더 단면(붐-로컬) — 종전 BuildBoomStructure/BuildBoomCatwalk/BuildBoomSplices 3곳에
        //   `const float gY=0.0275f, gH=0.075f`로 손복제돼 있던 값을 클래스 const로 승격(값 비트 불변).
        const float GirderCenterY = 0.020f;                         // 거더 단면 중심 y(붐-로컬). 깊이 0.09로 키우며 윗면 0.065 보존 위해 중심 0.0275→0.020(아래로만 확장)
        const float GirderDepthH  = 0.09f;                          // 거더 단면 깊이(y) — 0.075→0.09: 1.8→2.16m(현장 2.0~2.5 진입). 깊이/폭 1.36→1.64:1. 바닥만 -0.01→-0.025 하강(트롤리 z-분리, 충돌0)
        const float GirderTopLocal = GirderCenterY + GirderDepthH * 0.5f; // 거더 윗면(=0.065 유지, 상부 시스템 정합 기준). 0.020+0.045=0.065
        const float GirderBotLocal = GirderCenterY - GirderDepthH * 0.5f; // 거더 밑면(=-0.025, 깊어지면 따라 내려감). 바닥 현수물(트레이·투광등·단부프레임) SSOT
        // 위 거더 단면 상수(GapZ/WidthZ 및 GirderCenterY/GirderDepthH)는 Scale 미곱 raw 값 —
        //   다리/포털 치수(N*Scale)와 달리 Scale 변경 시 거더/게이지 비율이 깨짐. 현재 Scale 변경 경로 없어 '관찰'로 한정(값 유지).
        // 트롤리 레일 윗면 = 바퀴 트레드 접지면. 둘을 이 공유 상수에서 파생해 한쪽만 바뀌어 desync 되는 회귀 방지.
        const float BoomRailTopY = 0.026f;   // 트롤리 주행 레일 윗면 y(= 레일 중심 0.02 + 높이 0.012/2). 바퀴 트레드 하단이 여기에 접지.

        // 붐 거더 X 끝점 — 거더/레일/격자/스테이가 공유(한 군데서 길이 관리)
        const float BoomBackExtra = 0.12f;             // 트롤리 백트래블 + 거더 백리치를 함께 뒤로 빼는 양(기계실은 고정)
        const float GantryRange   = 2.2f;              // 갠트리 주행 범위(±, 모델 단위) ≈ 실척 ±53m, 총 106m. 원래 ±36m(72m)에서 확대해 레일 끝까지 주행. 부두 바닥 Z 길이는 StsQuayGroundCreator.QuaySlabZ(선석 344m + 양끝 여유 = 384m)로 분리돼 있어 이 값을 키워도 바닥은 안 커짐(원래 바닥 안에서 더 멀리 감)
        const float BoomBackX = TrolleyMinX - 0.27f;   // 백리치(육지쪽) 끝 — 트롤리 뒤 0.27 여유(TrolleyMinX가 이미 BoomBackExtra만큼 뒤로 감)
        const float BoomTipX  = TrolleyMaxX + 0.1f;    // 아웃리치 끝 — 트롤리 끝 + 팁 구조 여유(트롤리가 거의 끝까지)

        // 백리치 끝 플랫폼 위 디플렉터 시브 — 권상 로프(트롤리→플랫폼)를 받아 U자로 되돌려 기계실 진입 포트로 보냄.
        //   동적 로프 뒤끝을 기계실 뒷벽(처짐이 기계실 밑을 뚫음) 대신 여기로 옮겨, 처짐이 뒷공간(백리치)에서 일어나게 함.
        const float BackSheaveX   = BoomBackX + 0.03f;   // 시브 중심 X — 백리치 플랫폼(중심 x0+0.02) 아래
        const float BackSheaveY   = 0.02f;               // 시브 중심 Y — 데크(0.068) 밑으로 내려 매닮(행어로 현수)
        const float BackSheaveR   = 0.011f;              // 시브 외경 — 축소(끝단 브레이스 관통 회피)
        const float BackSheaveHZ  = 0.016f;              // 시브 반폭(Z) — side당 2폴(z=sz±0.008) 더블그루브 수용
        const float BackSheaveSeat = 0.008f;             // 로프가 앉는 V홈 바닥 반경(=R-grooveDepth) — 리브·접선 공유

        // 기계실 — 거더 백리치 연장과 무관하게 고정 위치. 백스테이가 기계실을 안 뚫게 앞(바다쪽)으로 MHForward만큼 당김.
        const float MHForward        = 0.12f;
        const float MachineryHouseHX = 0.085f;                              // 기계실 X 반폭(0.17의 절반)
        // 백리치 연장(-4→-13)에 맞춰 기계실을 새 백리치 끝으로 재정착.
        //   (종전엔 -4 고정이라 백리치만 늘리면 기계실이 붐 중간에 떠 보임 — 디자인팀 지적. 실제 STS는 기계실/평형추가
        //    백리치 끝에 위치하므로 -13 기준으로 이동.) 캣워크·드럼·진입구는 이 상수 파생이라 자동 추종.
        //   ※ 백스테이가 기계실을 안 뚫는지·캣워크 정합은 렌더 수렴 필요([[feedback_unity_visual_small_increments]]).
        const float MachineryHouseX  = (-13f * Scale - 0.27f) + 0.11f + MHForward;  // 기계실 중심 X — 새 백리치(-13) 기준 + 앞당김

        // 다리 X 위치(붐 로컬 = 루트 로컬, 붐이 루트 x=0에 있으므로 동일)
        const float LandLegX  = 0f;
        const float WaterLegX = LegSpanX;

        // 스프레더 승강 (spreaderRoot=붐 레벨 기준 로컬 Y, 음수=아래)
        const float SpreaderMaxY  = -4f  * Scale;           // 완전 상승 = 헤드블록이 트롤리 헤드 바로 아래 도킹. -3→-4: 헤드 로프소켓 콘 top(spreader-로컬 0.083)이 트롤리 헤드 하단(-0.0725)을 파고들어 -1*Scale 더 내려 ~11mm 여유 확보 (top=-4*Scale+0.083=-0.0837, -0.0725-(-0.0837)≈0.011)
        const float SpreaderMinY  = -(RailH - 0.8f * Scale); // 지면 직전(붐 높이에 연동)
        const float SpreaderRestY = -10f * Scale;

        // 스프레더 텔레스코픽 반길이(로컬 X, m). 중앙부는 항상 20ft, 좌/우 암이 사이즈별로 슬라이드.
        const float SpreaderHalf20 = 0.126f;   // 20ft (6.058/24/2 ≈ 0.1262)
        const float SpreaderHalf40 = 0.254f;   // 40ft (12.192/24/2 = 0.254)

        // [인양점 SSOT] 호이스트(헤드/시브/데드엔드/로프/헤드블록 소켓)를 한 상수로 묶어 양끝 정렬(gap=0 by construction).
        //   +X=항구(바다). 트롤리를 실척 ≈7m로 키운 뒤(Option C) 스프레더가 운전실 앞 빈 베이 중앙에 오도록 +X 이동.
        const float HoistX    = 0.027f;   // 인양점 항구쪽 이동 = (운전실전면 −0.062 + 트롤리바다끝 +0.115)/2 ≈ +0.027(≈0.65m)
        const float HoistSprX = 0.05f;    // 호이스트 폴 X 반간격(트롤리/parent) — 4가닥 분리(기존 0.03)
        const float HoistSprZ = 0.075f;   // 호이스트 폴 Z 반간격(트롤리/parent) — (기존 0.05)

        // [트롤리 본체 치수 SSOT] Option C 기계실 통합 박스. 여러 메서드(본체/프레임/붐로프 앵커)가 공유.
        const float TrolleyHX = 0.145f;   // 본체 반길이 X: full 0.29 = 실척 6.96m ≈7m
        const float TrolleyCX = -0.03f;   // 본체 X중심(육지 −X 이동) → 본체 X[−0.175,+0.115]
        const float TrolleyBackX = TrolleyCX - TrolleyHX;   // 본체 육지쪽(뒤) 끝면 = −0.175 (붐 호이스트 로프 트롤리측 앵커 기준)

        // 색
        static readonly Color CStruct  = new Color(0.82f, 0.83f, 0.85f); // 다리/포털/거더
        static readonly Color CBoom    = new Color(0.70f, 0.74f, 0.80f); // 붐 거더 본체
        static readonly Color CRail     = new Color(0.55f, 0.57f, 0.62f); // 트롤리 레일
        static readonly Color CMachine = new Color(0.30f, 0.33f, 0.38f); // 기계실
        static readonly Color CTrolley = new Color(0.95f, 0.45f, 0.10f); // 트롤리(안전 주황)
        static readonly Color CSpread  = new Color(0.98f, 0.80f, 0.10f); // 스프레더(안전 노랑)
        static readonly Color CSafety  = new Color(0.96f, 0.78f, 0.10f); // 난간·계단·케이지·토보드(안전 노랑 — 스프레더와 통일)
        static readonly Color CDark    = new Color(0.13f, 0.13f, 0.15f); // 트위스트락/헤드블록
        static readonly Color CCable   = new Color(0.22f, 0.23f, 0.25f); // 와이어 로프(오일드 강철 회색 — 순흑X)
        static readonly Color CGlass   = new Color(0.25f, 0.55f, 0.70f); // 운전실 창
        static readonly Color CLight   = new Color(1.00f, 0.95f, 0.70f); // 작업등 렌즈
        static readonly Color CWarn    = new Color(0.90f, 0.10f, 0.10f); // 항공장애등(적색)
        static readonly Color CTemp    = new Color(0.90f, 0.10f, 0.85f); // ★임시 변경표시색(마젠타) — 이번 작업 부위 식별용. 승인 후 원색(CStruct)으로 환원.
        static readonly Color CMark    = new Color(0.10f, 0.45f, 1.00f); // ★작업 대상 표시색(파랑) — 수정 예정 부위(형상 미수정) 식별용. 확정 후 CCable로 환원.

        const string RootName = "STS_Crane";

        // 같은 색은 머티리얼 1개를 재사용(빌드 1회 한정)
        static Dictionary<Color, Material> _matCache;
        // 모든 머티리얼이 공유하는 절차 생성 강철 디테일 텍스처(_BaseColor로 틴트)
        static Texture2D _steelTex;
        // 와이어 로프 전용 절차 텍스처 — 나선 strand 밴드(헬리컬 레이)
        static Texture2D _ropeTex;
        // PBR_Library 텍스처 세트 캐시(폴더명 → [albedo, normal, ao]). 빌드 1회 한정.
        static Dictionary<string, Texture2D[]> _pbrCache;
        // 모든 Box/Strut가 공유하는 단위 큐브 메시(모서리 안 깎음 — 사용자 요청으로 챔퍼 제거)
        // ※ init/cleanup 리셋 제외 — 토폴로지 불변(파라미터·GameObject 종속 데이터 없음)이라 빌드 간 영구 공유. 다른 캐시와 라이프사이클 비대칭은 의도된 것.
        static Mesh _unitCube;
        // 트러스 절점 연결판(거싯) — 같은 치수끼리 메시 1개 재사용
        static Dictionary<string, Mesh> _plateCache;
        // 접합부 볼트 패턴 — 한 메시에 여러 볼트 헤드. 같은 패턴끼리 메시 1개 재사용(드로콜·정점 절약)
        static Dictionary<string, Mesh> _boltCache;

        [MenuItem("Model/PG/크레인/STS 크레인 생성", false, 0)]
        public static void CreateFromMenu40ft() => CreateAtContainer(SpreaderHalf40);

        // STS 2대 배치 — 같은 레일 공유, 지면 중심 ±(배 LOA/4)로 대칭(1/4·3/4 위치). 지면은 그대로.
        [MenuItem("Model/PG/크레인/STS 2대 배치", false, 1)]
        public static void PlaceTwoSts()
        {
            // 기준 X/Y — 기존 STS(정확히 배치됨) 우선, 없으면 QuayRail
            var existing = GameObject.Find(RootName);
            float baseX, baseY;
            if (existing != null) { baseX = existing.transform.position.x; baseY = existing.transform.position.y; }
            else if (TryFindQuayRailLandX(out float qx)) { baseX = qx; baseY = 0f; }
            else { Debug.LogWarning("[STS] 기준 STS/QuayRail 없음 — 'STS 크레인 생성' 또는 '부두 바닥 생성' 먼저."); return; }

            // 대칭 중심 Z — 지면(슬래브) 중심. 없으면 기존 STS Z.
            float centerZ = existing != null ? existing.transform.position.z : 0f;
            var groundGo = GameObject.Find(StsPartNames.QuayGround);
            if (groundGo != null)
            {
                // 걷는 면(Asphalt) 기준 — '가장 넓은 렌더러'로 고르면 바다가 뽑힌다(공용 헬퍼가 차단).
                var slab = StsQuayGroundCreator.FindQuayDeckRenderer(groundGo);
                if (slab != null) centerZ = slab.bounds.center.z;
            }

            // 기존 STS 전부 제거(2대 새로 배치)
            foreach (var nm in new[] { RootName, RootName + "_2" })
            { var g = GameObject.Find(nm); if (g != null) Undo.DestroyObjectImmediate(g); }

            // 대칭 간격 — 갠트리 주행 ±53m가 '중앙에서 겹치게' ±40m로 좁힘(가운데 컨테이너도 두 크레인이 짚음).
            //   STS#1 [-93,+13], STS#2 [-13,+93] → 중앙 ±13m 겹침.
            float half = 40f * Scale;   // ±40m, 간격 80m
            var c1 = Create(new Vector3(baseX, baseY, centerZ - half));
            var c2 = Create(new Vector3(baseX, baseY, centerZ + half)); c2.name = RootName + "_2";

            // 갠트리 주행을 레일 전장으로 맞춤 — 두 대가 배 전구간(선수·선미 포함) 커버. 충돌방지 로직(GantryMover)이 안전 보장.
            foreach (var c in new[] { c1, c2 })
            {
                var gm = c.GetComponent<GantryMover>();
                if (gm != null) GantryRangeFitMenu.ApplyFit(c, gm, out _);
            }

            Selection.activeGameObject = c1;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[STS] 2대 배치 — X={baseX:F2}, Z={centerZ - half:F2}/{centerZ + half:F2}(±{half:F2}u), 레일 전장 주행+충돌방지 → 배 전구간 커버·중앙 겹침. 지면 유지.");
        }

        // 컨테이너 위치에 지정 스프레더 사이즈(반길이)로 생성 — 기존 인스턴스는 교체.
        static void CreateAtContainer(float spreaderHalf)
        {
            // 중복 방지 — 기존 인스턴스 제거(Undo 가능)
            var prev = GameObject.Find(RootName);
            if (prev != null) Undo.DestroyObjectImmediate(prev);

            // 컨테이너가 스프레더 정지 위치 아래에 오도록 배치
            Vector3 anchor = FindContainerAnchor();
            Vector3 pos = anchor - new Vector3(TrolleyRestX, 0f, 0f);

            // [부두 우선 정렬] 씬에 Quay_Ground가 이미 있으면 크레인 X를 '육지측' QuayRail에 스냅한다.
            //   ★ 크레인 루트 X = 육지측 레일(LandLegX=0), 중심이 아니다. 루트를 육지 QuayRail에 놓으면
            //     Rail_Land가 육지 QuayRail에, Rail_Water(=root+LegSpanX 0.75u)가 바다 QuayRail에 정확히 포개진다.
            //   (부두를 먼저 깔고 크레인을 나중에 만들면 컨테이너 앵커가 부두와 무관한 곳이라 크레인이 부두를 벗어나던 문제 —
            //    QuayRail이 곧 크레인 자리이므로 그쪽으로 생성. [[feedback_match_real_world_reference]])
            if (TryFindQuayRailLandX(out float quayLandX))
            {
                Debug.Log($"[STS] 기존 Quay_Ground 발견 → 크레인 루트 X를 육지측 QuayRail {quayLandX:F3}u에 정렬(컨테이너 앵커 X={pos.x:F3}u 대신). Rail_Water는 {quayLandX + LegSpanX:F3}u.");
                pos.x = quayLandX;
            }

            var root = Create(pos, spreaderHalf);

            // 부두 컨테이너 인식(스캔) 자동 실행 — 별도 메뉴에서 빠지고 생성에 통합됨.
            // 선택은 크레인에 유지(select:false)하고, 스캔이 앵커 산정 이후라 배치엔 영향 없음.
            QuayScannerMenu.ScanNow(select: false);

            // 부두가 이미 있으면 갠트리 주행범위를 레일에 자동 맞춤 — 생성 순서 무관(부두를 먼저 만든 경우에도
            //   배 전구간 커버 보장). 부두가 아직 없으면 ApplyFit가 false 반환 → 조용히 패스(부두 생성 시 자동맞춤됨).
            var gantry = root.GetComponent<GantryMover>();
            if (gantry != null && GantryRangeFitMenu.ApplyFit(root, gantry, out string fitMsg))
                Debug.Log($"[STS] 갠트리 주행범위 레일 자동 맞춤 — {fitMsg}");

            Selection.activeGameObject = root;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
        }

        /// <summary>
        /// hierarchy를 생성해서 root GameObject를 반환. 다른 에디터/런타임 코드에서도 호출 가능.
        /// Undo 시스템에 등록 → Ctrl+Z 한 번으로 되돌릴 수 있음.
        /// </summary>
        public static GameObject Create(Vector3 worldPosition, float spreaderHalf = SpreaderHalf40)
        {
            _matCache = new Dictionary<Color, Material>();
            _steelTex = null;
            _ropeTex = null;
            _pbrCache = new Dictionary<string, Texture2D[]>();
            _plateCache = new Dictionary<string, Mesh>();
            _boltCache = new Dictionary<string, Mesh>();
            _nameSeq.Clear();   // 파츠 번호 카운터 리셋(생성마다 _1부터)

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create STS Crane");
            root.transform.position = worldPosition;

            // 정적 구조
            BuildGroundRails(root.transform);
            BuildPortal(root.transform);
            BuildAccessDetails(root.transform);   // 사다리 + 점검 플랫폼

            // 붐 (루트 x=0, y=RailH) — 자식 트롤리/스프레더가 붐 로컬 좌표로 동작
            var boom = new GameObject(Numbered("Boom"));
            boom.transform.SetParent(root.transform, worldPositionStays: false);
            boom.transform.localPosition = new Vector3(0f, RailH, 0f);
            BuildBoomStructure(boom.transform);
            BuildBoomDetails(boom.transform);     // 보도·시브·작업등
            BuildBoomTrussDepth(boom.transform);  // 측면 워런 트러스(깊이감) + 측면 케이블 트레이
            BuildBoomSplices(boom.transform);     // 거더 스플라이스 연결판 + 볼트열
            BuildMachineryHouseAccess(boom.transform);  // 사다리 정상 ↔ 기계실 접근 캣워크 + 사다리쪽 출입문

            // [붐 러핑] 힌지 피벗 생성(빈 노드). 재부모화 스윕·BoomLuffMover는 붐·리빙·케이블·트롤리 전부 지은 '맨 끝'([러핑 최종 편입])에서 한 번에.
            //   후속 빌더(리빙·스테이)가 이 피벗을 참조하므로 생성만 먼저. 피벗 = 바다다리 상단 힌지(WaterLegX, 거더 중심선).
            var luffPivot = new GameObject("Boom_LuffPivot");
            luffPivot.transform.SetParent(boom.transform, worldPositionStays: false);
            luffPivot.transform.localPosition = new Vector3(WaterLegX, GirderCenterY, 0f);
            BuildBoomHinge(boom.transform, luffPivot.transform);          // 힌지 관절(클레비스+텅+핀) — 스윕 이후(직접 부모 지정)
            BuildBoomHoistReeving(root.transform, boom.transform, luffPivot.transform);   // 붐호이스트 리빙(정점 시브↔브라이들 동적)

            // A-프레임 + 스테이 케이블 (루트 레벨)
            BuildApexAndStays(root.transform, luffPivot.transform);
            // [삭제] 붐 래치(6) — 83° 완전 스토우에서만 작동하는 잠금장치인데, 우리 시뮬은 그렇게 안 세우므로 불필요(죽은 부품) → 제거.
            BuildBoltedJoints(root.transform);    // 주요 구조 접합부 연결판 + 볼트(다리/포털/실빔/브레이스)

            // 트롤리 (붐 직속 자식)
            var trolley = new GameObject(Numbered("Trolley"));
            trolley.transform.SetParent(boom.transform, worldPositionStays: false);
            BuildTrolleyVisual(trolley.transform);

            // 스프레더 루트 (트롤리의 형제 — TrolleyMover가 X 동기)
            var spreaderRoot = new GameObject(Numbered("SpreaderRoot"));
            spreaderRoot.transform.SetParent(boom.transform, worldPositionStays: false);

            var spreader = new GameObject(Numbered("Spreader"));
            spreader.transform.SetParent(spreaderRoot.transform, worldPositionStays: false);
            BuildSpreaderVisual(spreader.transform, spreaderHalf);
            spreader.AddComponent<SpreaderLockAnimator>();   // 트위스트락 잠금 모션

            var attachPoint = new GameObject(Numbered("AttachPoint"));
            attachPoint.transform.SetParent(spreader.transform, worldPositionStays: false);
            // 컨테이너 윗면이 여기에 정렬됨(SpreaderGrabber.Grab). 스프레더 본체 최하단(하단 Beam_Flange 밑면 ≈ -0.019)에
            // 맞춰야 빔·플랜지가 컨테이너 위에 얹히고 트위스트락만 코너 캐스팅으로 삽입된다. (-0.01이면 플랜지가 컨테이너에 묻힘)
            attachPoint.transform.localPosition = new Vector3(0f, -0.019f, 0f);

            // 호이스트 로프 — HoistRopeRig가 매 프레임 스프레더 Y에 맞춰 신축
            BuildHoistRopes(spreaderRoot.transform, spreader.transform);
            // 권상 윗구간 — 트롤리↔시브 구간·시브 감김을 TrolleyReevingRig로 트롤리 추종(꺾임 0).
            BuildHoistUpper(boom.transform, trolley.transform);
            // 견인로프 — 동일하게 트롤리 추종 리빙.
            BuildTowReeving(boom.transform, trolley.transform, luffPivot.transform);

            // 컴포넌트 부착 + 설정
            var trolleyMover = trolley.AddComponent<TrolleyMover>();
            var spreaderHoist = spreader.AddComponent<SpreaderHoist>();
            var spreaderAttach = attachPoint.AddComponent<SpreaderAttach>();

            trolleyMover.Configure(TrolleyMinX, TrolleyMaxX, spreaderRoot.transform);
            spreaderHoist.Configure(SpreaderMinY, SpreaderMaxY);
            spreaderAttach.Configure(attachPoint.transform);

            // 초기 위치 (rest pose) — Boom 로컬 좌표계 기준
            trolley.transform.localPosition = new Vector3(TrolleyRestX, 0f, 0f);
            spreaderRoot.transform.localPosition = new Vector3(TrolleyRestX, 0f, 0f);
            spreader.transform.localPosition = new Vector3(HoistX, SpreaderRestY, 0f);   // 인양점 항구쪽 이동(SpreaderHoist는 Y만 갱신, X 보존)

            // [러핑 최종 편입] 붐·리빙·케이블·트롤리·스프레더까지 전부 지은 '맨 끝'에서, '완전히 바다측(힌지 초과)'인
            //   모든 렌더 부재를 한 번에 루핑 피벗으로 재부모화. 끝에서 하므로 리빙 서플라이·케이블 세그먼트·팁 시브까지 전부
            //   자동 편입 → 부재를 하나씩 빠뜨리지 않는다. (전장 부재는 힌지서 분할된 _Luff가 잡히고, 힌지를 '걸치는' 세그먼트만
            //   고정으로 남음. 트롤리·스프레더는 육지측 rest라 안 편입. 렌더러 없는 리빙 호스트/마커도 안 잡힘.)
            {
                float hingeLocalX = WaterLegX;
                var luffParts = new List<Transform>();
                foreach (Transform c in boom.transform)
                {
                    if (c == luffPivot.transform) continue;
                    if (c.name.Contains("TowFore_Rope") || c.name.Contains("HoistU_Rope")) continue;   // TrolleyReevingRig가 boom-로컬로 매 프레임 배치하는 세그먼트("TowFore_Rope_41" 등 Numbered) — 재부모화 금지(프레임 어긋남=허공 수직선 원인). 리그가 러핑된 팁 시브 좌표를 직접 읽어 따라감. (구조물 MH_Rope*·Sheave_Rope는 제외 대상 아님)
                    if (c.name.EndsWith("_Luff")) { luffParts.Add(c); continue; }             // 분할된 바다측 절반
                    if (TryBoomLocalMinX(c, boom.transform, out float minX) && minX >= hingeLocalX - 0.002f)
                        luffParts.Add(c);                                                      // 완전히 바다측인 부재(구조·리빙·케이블 전부)
                }
                foreach (var tt in luffParts) tt.SetParent(luffPivot.transform, worldPositionStays: true);  // 월드 보존 → 0°서 위치 불변
                var luffMover = luffPivot.AddComponent<BoomLuffMover>();
                luffMover.Configure(0f, 83f);   // rest 0°(수평)·max 83°(확정)
                luffMover.Current = 0f;         // rest 0°(수평 운용 자세)
            }


            // 갠트리 주행 — 크레인 루트 Z축, 초기 위치(worldPosition.z) 기준 ±GantryRange. VR controller A 토글로 활성.
            var gantryMover = root.AddComponent<GantryMover>();
            gantryMover.Configure(worldPosition.z - GantryRange, worldPosition.z + GantryRange);

            // 갠트리 주행부(루트)에 kinematic Rigidbody — 다리 콜라이더(Leg_Collider)가 dynamic 컨테이너를
            //   밀어내도록 물리 바디 부여. 중력/외력은 안 받고(kinematic), GantryMover의 위치 이동만 충돌로 전달.
            var craneBody = root.AddComponent<Rigidbody>();
            craneBody.isKinematic = true;
            craneBody.useGravity = false;
            craneBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;   // 느린 주행도 터널링 방지

            // 루트 컴포넌트 — Facade로 묶기
            var stsCrane = root.AddComponent<StsCrane>();
            stsCrane.Configure(boom.transform, trolleyMover, spreaderHoist, spreaderAttach, gantryMover);

            // (데모 자동사이클 StsCraneOperator 제거 — 새 크레인은 시나리오 부착 전엔 가만히 있는다)
            // 폭풍 계류 결박 봉 — 갠트리 주행 시 살짝 들렸다(분리) 정지 시 내려 박힘. Start에서 Tiedown_Rod 자동 수집.
            root.AddComponent<TiedownController>();
            // VR 컨트롤러 수동 조종 — 켜지면 자동 사이클을 끄고 스틱/트리거로 직접 조종
            root.AddComponent<StsCraneVRController>();
            // 컨테이너 집기/놓기 + 통과 방지(콜라이더 없는 컨테이너 대응)
            root.AddComponent<SpreaderGrabber>();

            PruneDeletedParts(root.transform);   // 삭제 지정 파츠 일괄 제거(번호 유지)

            _matCache = null;
            _steelTex = null;   // 텍스처는 머티리얼이 참조 유지 → 캐시 핸들만 해제
            _ropeTex = null;    // 로프 텍스처도 머티리얼이 참조 유지 → 캐시 핸들만 해제
            _pbrCache = null;   // PBR 라이브러리 텍스처는 에셋이라 영속 → 캐시 핸들만 해제
            _plateCache = null; // 거싯 메시도 GameObject가 참조 유지 → 핸들만 해제
            _boltCache = null;  // 볼트 메시도 GameObject가 참조 유지 → 핸들만 해제
            return root;
        }

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
            // 다리 하단 = Leg_BasePlate 윗면(0.066 + 0.012/2 = 0.072). 다리가 베이스 위에 앉음.
            //   (기존 0f는 다리·콜라이더가 베이스 플레이트·보기 피벗(0.05)·레일(0.008)을 뚫고 지면(0)에 박혔음)
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
                        // [계산] 트레드 반경 0.013(지름 0.026), 플랜지 반경 0.01495(지름 0.0299).
                        //   4륜 균등 피치 p=0.032 → 오프셋 {-1.5,-0.5,+0.5,+1.5}×p = {-0.048,-0.016,+0.016,+0.048}.
                        //   트레드 지름 0.026<0.032(6mm 간극)·플랜지 지름 0.0299<0.032(2.1mm 간극) → 휠/플랜지 비간섭.
                        const float p  = StsConfig.BogieEqualizerPitch;   // [H2] 보기 4륜 균등 피치 SSOT
                        float bz = StsConfig.BogieLengthZ;                // [H2] 보기 전장(Z) SSOT (= p × 3.25 ≈ 0.104, 4륜 스팬 0.096 + 마진)
                        // 메인 이퀄라이저 빔(다리 하단 중앙 피벗으로 매달림) + 피벗 핀(축 X)
                        Box(root, "Bogie_Equalizer", new Vector3(x, by, z),
                            new Vector3(LegSec * 0.5f, 0.014f, bz), CStruct);
                        Rod(root, "Bogie_Pivot",
                            new Vector3(x - LegSec * 0.55f, by, z), new Vector3(x + LegSec * 0.55f, by, z), 0.005f, CDark);
                        // 보기 이퀄라이저 ↔ 베이스플레이트 하중경로 연결 — 킹핀 클레비스(귀판 2장).
                        //   (기존: 이퀄라이저 윗면 0.057·피벗핀 윗면 0.055과 베이스플레이트 밑면 0.060 사이 3mm에 부재가 없어
                        //    보기 어셈블리 전체가 매달린 곳 없이 부유 → 하중전달 끊김. PORTAL-1 수정(다리 0→0.072)의 잔여 부작용.)
                        //   [계산] 귀판 y[0.047,0.063]: top 0.063으로 플레이트(밑면 0.060)에 0.003 물리고, 핀(0.05) 감싸 이퀄라이저 윗면(0.057)까지 연속.
                        //          x±0.009(두께 0.004, 안쪽면 0.007 > 이퀄라이저 반폭 LegSec*0.25=0.00625) → 이퀄라이저 혀를 양 귀판이 물고 핀이 관통.
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
                                // 트레드 중심 y=0.021 — 레일 윗면(0.008)+트레드 반경(0.013) → 트레드 접지, 플랜지 하단 y=0.006(레일 옆면을 묾).
                                //   (이전 0.014는 바퀴 하단 0.001로 레일을 7mm 관통, 트레드가 아스팔트에 닿았음)
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
            //   붐 상부 보도(Boom_Toe/Mid-rail/Walkway_Deck)가 이 빔과 같은 높이대라, 보도 부재 쪽을
            //   포털 통과 지점(육지/바다 다리 X)에서 끊어 개구부로 지나가게 한다(BoomTopWalkwayGaps 참조).
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

            // 측면 대각 브레이스(앞/뒤 다리 사이) — Sill_Beam(RailH*0.4)을 하현재로 그 위에 X 패턴.
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
                    //   [기존 버그] (x+0.03, 0.012)는 플레이트(0.060~0.072)보다 0.039 아래 + 지면서도 0.003 떠, 위아래 어디에도 안 붙은 '공중 부유'였음.
                    //   [수정·계산] 플레이트 +X면(x+0.03)에 물려 x+0.034±0.005=0.029~0.039, top 0.067로 플레이트 안에 박고 0.037까지 늘어뜨려 용접.
                    Box(root, "Tiedown_Lug", new Vector3(x + 0.034f, 0.052f, lz),
                        new Vector3(0.01f, 0.03f, 0.016f), CStruct);
                    // 폭풍 계류 타이로드(봉 + 발바닥 일체) — 러그(패드아이) 하단 0.037에서 안벽으로 내려와 발바닥(풋 베이스)으로 선다.
                    //   기존 'Tiedown_Anchor'는 '부두 매립 고정 앵커' 가정이었으나, 실제로는 크레인 루트 자식이라
                    //     갠트리 주행 시 크레인 따라 같이 굴러가 고정이 아니었다. 정체는 '봉의 발바닥(풋)' → 봉과 한 부품으로 합친다.
                    //     세 조각(샤프트·보스·풋) 모두 "Tiedown_Rod*"로 명명해 TiedownController가 동일 높이로 들어올려 강체로 유지
                    //     (주행 시 봉+풋이 함께 살짝 들려 안벽서 분리, 정지 시 다시 내려 선다).
                    //   [계산] x+0.034·z=lz는 베이스플레이트 +X면(0.03) 밖 · 보기(x±0.0125) 밖 · 스토우핀(z±0.0375) 밖이라 간섭 0.
                    //     수직 적층: 샤프트 0.037→0.014 / 보스 0.014→0.008(r0.007>봉0.004, <풋반폭0.009) / 풋 0.000~0.008. 전부 맞닿음.
                    Rod(root, StsPartNames.TiedownRodPrefix, new Vector3(x + 0.034f, 0.037f, lz),
                        new Vector3(x + 0.034f, 0.014f, lz), 0.004f, CDark);
                    // 단조 보스 — 둥근 봉을 평평한 발바닥에 매끈히 물리는 짧은 칼라(평강 모서리엔 구 금지, 솔리드 전이).
                    Rod(root, "Tiedown_Rod_Boss", new Vector3(x + 0.034f, 0.014f, lz),
                        new Vector3(x + 0.034f, 0.008f, lz), 0.007f, CMachine);
                    // 발바닥(풋 베이스) — 안벽에 서는 단조 베이스 패드. 봉과 한 부품으로 함께 들림.
                    PbBox(root, "Tiedown_Rod_Foot", new Vector3(x + 0.034f, 0.004f, lz),
                        new Vector3(0.018f, 0.008f, 0.018f), CMachine);
                    // 휠 레일 스위퍼(주행방향 Z, 보기 앞/뒤 끝 — 레일 위 이물질을 밀어내는 플라우).
                    //   브래킷+경사 디플렉터 블레이드+마모 스트립+측면 거싯으로 정교화(기존 단일 박스 대체).
                    //   [계산] 외측 휠 z=±0.048·플랜지 반경 0.01495(z 최대 0.063) → 스위퍼 z=±0.07로 휠 앞에 둬 비간섭.
                    //          레일 윗면 0.008 → 마모 스트립 하단 0.0085(0.5mm 클리어런스). 블레이드 폭 0.026>레일 0.02.
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float swz = lz + e * 0.07f;                  // 스위퍼 Z(보기 앞/뒤 끝, 외측 휠 너머)
                        // 마운팅 브래킷(보기 끝 프레임으로 올라가는 수직판)
                        // 스위퍼(swz=lz±0.07)가 보기 구조(메인 빔 끝 lz±0.052)보다 0.018 바깥에 떠 있었음 →
                        //   브래킷을 메인 빔 끝까지 Z로 연장해 스위퍼를 보기에 물림(정적 접점 확보, 부유 해소).
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

        // 붐: 메인 거더 + 주행 레일 + 격자 대각 + 기계실 (붐 로컬 좌표)
        static void BuildBoomStructure(Transform boom)
        {
            float x0 = BoomBackX;   // 백리치 끝
            float x1 = BoomTipX;    // 아웃리치 끝(바다쪽)

            // 트윈(더블 박스) 거더 — 두 박스 거더(z=±GirderGapZ) + 거더별 하단 주행 레일
            const float gY = GirderCenterY, gH = GirderDepthH; // 클래스 const 참조(값 불변). 지역 별칭으로 아래 식 가독성 유지
            float gTop = GirderTopLocal;               // 거더 윗면(=0.065, 유지 → 상부 시스템 불변)
            float gBot = gY - gH * 0.5f;               // 거더 밑면(=-0.01, 깊어짐)
            // [붐 러핑 1단계] 거더·레일을 힌지 X(=WaterLegX, 바다다리 상단)에서 '육지측 고정' + '바다측 러핑' 2조각으로 분할.
            //   두 조각은 힌지에서 정확히 맞닿아 union이 원래 단일 박스와 기하학적으로 동일(0°에선 형상 불변) —
            //   바다측(_Luff)만 나중에 Boom_LuffPivot 하위로 재부모화해 기립시킨다. 실물: 힌지 바다측만 올라가고 육지 거더는 고정.
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

            // [rail-in-middle] 바닥면 평면 대각 브레이스(Boom_Plan_Brace) 전면 삭제
            //   사유: 트롤리가 거더 사이(중간 레일)에 nested 되고 훅·로프가 중앙 갭으로 하강하므로,
            //   중앙을 가로지르던 평면 지그재그는 통로를 막아 제거. 비틀림 강성은 깊어진 박스 거더 +
            //   상·하 플랜지 횡프레임 + 거더 바깥면 트러스(BuildBoomTrussDepth)가 담당.

            // 기계실(육지쪽 위) + 디테일 — 붐 가로(Z)로 넓혀 육중하게(거더보다 양옆 돌출)
            // ※ 기계실은 고정(거더만 뒤로 연장) + 앞으로 MHForward만큼 당김 — 백스테이가 기계실을 안 뚫게
            float mhx = MachineryHouseX;
            float mhZ = 2f * GirderGapZ + 0.08f;   // Z 폭 — 트윈 거더(±GirderGapZ)를 가로질러 얹히게 넓힘(거더 바깥으로 약간 오버행)
            float mhHZ = mhZ * 0.5f;       // Z 반폭
            float mhHX = MachineryHouseHX; // X 반폭(0.17의 절반)
            // 밑면을 거더 윗면(gTop=0.065)에 안착 — 기존 중심0.105·높이0.11은 밑면 0.05로 거더에 15mm 침하.
            //   윗면(0.16)·지붕은 유지하고 높이만 0.095로 줄여 밑면을 0.065로 올림(중심 0.1125).
            Box(boom, StsPartNames.MachineryHouse, new Vector3(mhx, 0.1125f, 0f),
                new Vector3(0.17f, 0.095f, mhZ), CMachine);
            Box(boom, "MH_Roof", new Vector3(mhx, 0.165f, 0f),
                new Vector3(0.185f, 0.012f, mhZ + 0.01f), CStruct);
            // 출입문 세트 숨김(사용자 요청) — 손잡이는 문이 없으면 떠서 함께 숨김. 복구하려면 주석 해제
            // Box(boom, "MH_DoorFrame", new Vector3(mhx + 0.085f, 0.086f, 0f),
            //     new Vector3(0.004f, 0.066f, 0.05f), CStruct);
            // Box(boom, "MH_Door", new Vector3(mhx + 0.087f, 0.085f, 0f),
            //     new Vector3(0.005f, 0.058f, 0.042f), CDark);
            // Box(boom, "MH_DoorHandle", new Vector3(mhx + 0.0905f, 0.085f, 0.014f),
            //     new Vector3(0.004f, 0.012f, 0.004f), CStruct);
            // Box(boom, "MH_DoorLanding", new Vector3(mhx + 0.1f, 0.056f, 0f),
            //     new Vector3(0.03f, 0.004f, 0.05f), CMachine);
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
            // 트레이 행어 브래킷 — 거더 밑면(GirderBotLocal)에서 트레이(-0.01)로 매단다(0.17m 공중부양 해소).
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
                Rod(boom, "Hoist_Motor",                  // 구동 모터
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
            // 트롤리 주행(견인) 윈치 — STS는 견인로프式, 주행 모터/감속기는 기계실에
            //   [출처: Casper Phillips STS 용어집 — machinery house houses main hoist/boom hoist/trolley drive]
            //   [수학팀 검증] 중심 twx=-0.535(호이스트 wx=-0.602의 바다쪽). 호이스트 윈치 +X끝 -0.5667과 11.7mm,
            //     공동 +X 내벽 -0.5047과 ~10mm 클리어. 호이스트 전부재와 AABB 겹침 0 (python 산식 검증).
            //   드럼 축은 호이스트와 평행(Z). 로프는 바다쪽(+X)으로 나가 붐 따라 트롤리로(견인 로프는 다음 증분).
            {
                float twx = -0.535f, twy = 0.095f, twHalfZ = 0.06f;
                Rod(boom, "Trolley_Travel_Drum",                                  // 견인 로프 스풀 드럼
                    new Vector3(twx, twy, -twHalfZ), new Vector3(twx, twy, twHalfZ), 0.016f, CMachine);
                for (int s = -1; s <= 1; s += 2)                                  // 드럼 양끝 플랜지
                    Rod(boom, "Travel_Drum_Flange",
                        new Vector3(twx, twy, s * twHalfZ), new Vector3(twx, twy, s * (twHalfZ + 0.006f)), 0.020f, CMachine);
                Rod(boom, "Travel_Gearbox",                                       // 감속기(드럼 -Z 직결)
                    new Vector3(twx, twy, -twHalfZ - 0.006f), new Vector3(twx, twy, -twHalfZ - 0.016f), 0.018f, CMachine);
                Rod(boom, "Travel_Motor",                                         // 주행 모터
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

            // 다리 도관 → 기계실 정션박스(MH_JBox) 연결 — '붐 거더 선'을 따라 라우팅해 거더가 밑을 받치게(안 뜸).
            //   이전엔 수평 구간이 z=다리(0.333)로 가서 붐에서 떨어진 허공에 떴음 → 거더 z(≈0.172, 거더 폭 안)로 옮김.
            //   엘보 박스도 거더 위에 마운트해 복원(삭제 아님 — 재설계).
            {
                float condX2 = LandLegX - 0.034f;          // 다리 도관 x (다리 밖 — 기둥/래이싱 관통 회피, condX와 정렬)
                float legZ   = GaugeZ * 0.5f;               // 0.333 (다리 z)
                float gz     = GirderOuterZ - 0.01f;        // ≈0.1775 — 거더 폭 안(인보드), 거더 밑면 아래로 현수
                // [페스툰 회피] 페스툰 케이블 z=0.1955, 최저 y = festBotY-festSag-반경 = -0.0336.
                //   거더 밑면(-0.01)~페스툰 윗변(-0.0184) 틈(0.0084) < 관경(0.01) → 그 사이로 못 지남 → 현수를 페스툰 '밑'으로.
                float runY   = -0.042f;                     // 윗면 -0.037 < 페스툰 밑 -0.0336 (3.4mm 여유). 거더 밑면(-0.01)보다도 아래.
                float jbX = mhx - 0.07f, jbY = 0.06f;
                float jbZ = mhFZ;                           // 0.20 — MH_JBox(기계실 +Z벽 z0.20) 본체로 진입하는 종단 z
                float r   = 0.005f;
                float Rb  = 1.5f * r;                       // 곡관 굽힘반경(센터라인). Rb>r → 내반경(Rb-r)>0, 핀치 없음
                // [Sill+캣워크 회피 — 사용자 지적: Leg_Conduit_Link_5가 MH_Sill 이어 Catwalk_Rail 통과] 상승 구간(V4→V5)이 기계실 +Z 앞
                //   층층 부재 — Sill 바깥면 0.2065, 하부 점검 캣워크 데크·난간 바깥면 (GirderOuterZ+0.012)+0.011=0.2105 — 를 통과했음.
                //   → 가장 바깥(캣워크 0.2105) 너머로 상승 z(clearZ)를 빼고, 캣워크·Sill 위(jbY0.06 > 난간top 0.028)서 -Z로 꺾어 벽 진입.
                float clearZ = (GirderOuterZ + 0.012f) + 0.011f + r + 0.002f;   // ≈0.2175 = 캣워크 데크/난간 바깥면(0.2105) + 관반경 r + 여유 → 관 안쪽면 0.2125 > 0.2105
                // [곡관 엘보] 90° 굽힘을 짧은 원통 호 + 패싯 절점 구로 근사(관 굵기 일정, 부풀지 않음). 직선 구간은 접점 트림→접선 연속.
                //   경로: 다리에서 내려와 거더·페스툰 밑으로 횡주행 → 캣워크·Sill 바깥(z≈0.2175)으로 빠져 상승 → 캣워크 위서 벽 진입 → MH_JBox.
                Vector3[] path = {
                    new Vector3(condX2, -0.005f, legZ),       // V0 다리 도관 접속(자유단)
                    new Vector3(condX2, runY,    legZ),       // V1
                    new Vector3(condX2, runY,    gz),         // V2
                    new Vector3(jbX,    runY,    gz),         // V3 (거더·페스툰 밑 -X 횡주행 끝)
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
            // 지붕 접근 수직 사다리(바다쪽 +Z면 → 지붕)
            // 사다리 베이스가 데크(0.067)보다 아래(0.06)서 허공 종단 → ① y0를 데크면(0.067)으로 올림,
            //   ② 기계실 출입 캣워크 도어(x=mhx, z=corZ≈0.235)에서 사다리 베이스(z=0.208)로 잇는 step-off 랜딩 데크 추가 → 동선 연속.
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

            // 바다쪽(+X) 끝면 창 숨김(사용자 요청) — 복구하려면 주석 해제
            // float mhEndX2 = mhx + mhHX + 0.001f;
            // for (int s = -1; s <= 1; s += 2)
            // {
            //     Box(boom, "MH_EndWinFrame", new Vector3(mhEndX2, 0.114f, s * 0.078f),
            //         new Vector3(0.005f, 0.03f, 0.03f), CDark);
            //     Box(boom, "MH_EndWindow", new Vector3(mhEndX2 + 0.001f, 0.114f, s * 0.078f),
            //         new Vector3(0.004f, 0.024f, 0.024f), CGlass);
            // }

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
                    // 밑동을 트러스 상현재 캡 윗면(0.0685) 위로 올림(0.069) — 캡(y0.0615~0.0685, z0.1855~0.1925)에 3.5mm 박히던 것 해소. 윗단(railTop)·중간레일·토보드는 불변.
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
                        new Vector3(0.004f, 0.004f, halfPZ * 2f + 0.004f), CSafety);   // 양끝 3면코너 +t/2씩
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
                    new Vector3(0.003f, 0.01f, halfPZ * 2f + 0.001f), CSafety);   // 토보드 inset0.001 보정 후 바깥면 맞춤
            }
            // 정션 박스 2(반대편 데크) — 데크 볼트 고정형(덮개는 빔 방향, 글랜드는 데크 아래로)
            JunctionBox(root, "Apex_JBox", new Vector3(apex.x + halfX * 0.45f, platY + 0.016f, -halfPZ * 0.5f),
                new Vector3(0.016f, 0.022f, 0.014f), new Vector3(1f, 0f, 0f), Vector3.down, CDark);
            JunctionBox(root, "Apex_JBox", new Vector3(apex.x - halfX * 0.5f, platY + 0.012f, -halfPZ * 0.2f),
                new Vector3(0.012f, 0.016f, 0.012f), new Vector3(-1f, 0f, 0f), Vector3.down, CMachine);

            // 비콘 마스트 — 단단한 마스트 + 하우징 달린 적색 항공장애등 2단 + 풍속계 + 피뢰침
            float mb = platY + 0.010f;   // 0.012→0.010: Mast_Base 밑면(mb-0.006=platY+0.004)이 데크 윗면(platY+0.004)에 안착(48mm 뜸 해소)
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
                        new Vector3(lx, LegTopY, s * halfZ), 0.016f, CStruct);   // 0.012→0.016: 지름 0.58→0.77m(현장 0.8~1.2 하한대). 라싱·가새·스테이는 정상이라 유지.
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

            // 정상 시브 네스트(스테이 도르래) + 장비 하우징.
            //   시브를 가로보 Apex(밑면 apex.y-0.02)와 솔리드 하우스에서 빼냄(매립+로프 벽관통 해소).
            //   ① 시브 y를 apex.y-0.01 → apex.y-0.042로 내려 시브 top(=center+0.016=apex.y-0.026)이 가로보 밑면 아래 6mm로 떨어짐
            //      (포어/백스테이는 같은 시브를 공유하는 턴 시브이고 정착점 y≈1.40/1.43으로 한참 아래라, 시브를 내려도 양 스테이는 정상 강하).
            //   ② 하우스를 솔리드 박스 → 개방 슈라우드(상부 캡 + 양 Z단부 측벽)로. 시브가 아래로(포어 +X·백 -X) 로프를 빼므로
            //      상부·Z단부만 가리고 X양면·하부는 개방(페어리드) → 로프가 더 이상 벽을 관통 안 함.
            float sheaveY = apex.y - 0.042f;
            // 상부 캡(가로보 Apex 밑면 apex.y-0.02에 현수) — 시브 top(apex.y-0.026)과 1.5mm 이격
            Box(root, "Apex_SheaveHouse", new Vector3(apex.x, apex.y - 0.0215f, 0f),
                new Vector3(0.03f, 0.006f, 2f * GirderGapZ + 0.08f), CMachine);
            for (int s = -1; s <= 1; s += 2)   // 양 Z단부 측벽(시브 z=±0.16 바깥 ±0.20에서 끝막음, 로프 경로 z=±0.16엔 비관여)
                Box(root, "Apex_SheaveHouse", new Vector3(apex.x, sheaveY + 0.004f, s * (GirderGapZ + 0.04f)),
                    new Vector3(0.03f, 0.05f, 0.006f), CMachine);
            // 정상 스테이 정착부 — 회전 시브가 아니라 핀-클레비스(패드아이) 정착.
            //   이 붐은 고정(스테이 지지)식이라 정상부를 넘는 러핑(boom-hoist)
            //   로프가 없음 → 도르래(시브)는 기능상 불필요. 포어/백스테이는 각도·장력이 다른 별개의
            //   정적 인장재이므로(한 줄이 시브를 넘는 구조가 아님), 실제 STS A-프레임처럼 가로 정착 핀
            //   (축 Z) + 양 치크판으로 핀 정착함. 종전: 5줄 스테이가 시브 허브 중심(r=0)에서 발사돼
            //   V홈을 안 쓰고 드럼을 관통했음 → 핀 정착으로 교체하니 로프 끝점(=핀)이 곧 정착점이 됨.
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
                // 스테이 집합 소켓 제거 — 붐호이스트 시브가 이 자리를 차지(중복·매립 해소). 포어스테이는 동적화됨.
                // [붐 러핑] 붐호이스트 시브 — 정착 핀을 축으로 회전 도르래를 얹는다(핀-클레비스 재활용, 충돌 0).
                //   반경 0.020→0.016 축소: 시브 top=C.y+0.016=apex.y-0.026 < 캡 밑면 apex.y-0.0245(1.5mm 여유), 하단=치크 하단 정렬.
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
            // 리프팅 러그 y +0.008→+0.027: 정점보 top(apex.y+0.02)에 밑면 얹혀 위로 0.014 돌출(종전엔 보 속 완전 매립).
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
                    // 앵커를 데크 X-중심(apex.x)→바깥 가장자리(apex.x+halfX)로 이동. 중심에 박으면
                    // 가장자리까지 비스듬히 내려가며 데크 두께(platY±0.004) 안을 관통함. 가장자리 코너에서
                    // 뻗어야 Apex_Platform을 뚫지 않고, 주석상 의도("바다쪽 가장자리에서 뻗은 프레임")와도 일치.
                    new Vector3(apex.x + halfX, platY + 0.004f, s * halfPZ * 0.7f),
                    new Vector3(galX, galY, s * halfPZ * 0.7f), 0.004f, CStruct);
            for (int i = -2; i <= 2; i++)
            {
                float fz = i * halfPZ * 0.4f;
                // 에이펙스 투광등 — FloodBar(galX)에서 +X 아래 45°로 안벽 작업역 조사. 박스+구 → 방향지정 어셈블리.
                Floodlight(root, new Vector3(galX + 0.004f, galY, fz), Quaternion.Euler(0f, 0f, 45f), 0.013f, CDark, CLight);
            }


            // 스테이 케이블 — 부채꼴 + 앵커 플레이트·턴버클. 측면 시브(z=±GirderGapZ)에서 같은 쪽 거더로 내림(측면별 수직면).
            // 포어스테이 정착 y = 거더 윗면(boom-local GirderTopLocal=GirderCenterY+GirderDepthH/2=0.0275+0.0375=0.065 → 루트 RailH+0.065) 기준 +0.005.
            // [포어스테이 = 굵은 강성 타이바] 정점 크로스헤드 ↔ 붐 외측(f=0.9). 붐 무게를 받는 굵은 바.
            //   ※ 붐 러핑 시 길이 변화는 rig가 동적으로 처리(강성 바는 원래 안 늘어남 — 정지/운전 자세에서 정착 지지 역할).
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
            // 정상 후방 연장(아웃리거) 보류(사용자 요청) — A-프레임을 높여(ApexH↑) 정상 시브 하우스에서 바로 내려도 기계실 위를 넘으므로 불필요. 복구하려면 블록주석 제거
            /*
            float backMastX = apex.x - 0.22f;
            float bmY = apex.y - 0.005f;
            Box(root, "Apex_BackBeam", new Vector3(backMastX, bmY, 0f),
                new Vector3(0.016f, 0.016f, 2f * GirderGapZ), CStruct);
            for (int s = -1; s <= 1; s += 2)
            {
                float armZ = s * GirderGapZ;
                Strut(root, "Apex_BackArm", new Vector3(apex.x, apex.y, armZ),
                    new Vector3(backMastX, bmY, armZ), 0.009f, CStruct);
                Strut(root, "Apex_BackBrace", new Vector3(backMastX, bmY, armZ),
                    new Vector3(apex.x, apex.y - 0.09f, armZ), 0.006f, CStruct);
                Rod(root, "Apex_BackSheave",
                    new Vector3(backMastX, bmY, armZ - 0.014f),
                    new Vector3(backMastX, bmY, armZ + 0.014f), 0.013f, CDark);
            }
            */

            // 백스테이 — 정상 시브 하우스(z=±GirderGapZ)에서 거더 맨뒤(이퀄라이저 빔)로 가는 '주 백스테이'.
            //   ※ 앞쪽 줄(bsFrontX, 기계실 앞)은 짧고 높아 육지측 A-프레임 X-브레이스(Aframe_Lace)를
            //     정상부 바로 아래(x≈0.54)에서 관통 → 제거. (백스테이는 시브 z=±0.16에 물려 z 회피 불가,
            //     lace 대각이 모든 z를 훑어 충돌 불가피 → 짧은 앞 줄을 뺌. 뒤 줄은 가팔라 통과함.)
            // [백스테이 = 굵은 강성 타이바] 앞(포어스테이)과 대칭. 가는 로프(0.004)·시브 정착 → 굵은 바(0.012)·크로스헤드 정착.
            //   양끝 고정(붐 백리치는 힌지 육지측이라 러핑 안 함) → 정적. 정점 끝은 시브 아니라 크로스헤드(가로보 밑면)에 핀.
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
                    // 종전 턴버클은 타이바와 동일반경(0.012)·동일축이라 표면 완전 겹침(z-fighting).
                    //   턴버클 = 로드가 나사물림되는 '배럴(슬리브)'이라 로드(0.012)보다 굵어야 함 → 배럴 0.019(1.6×) + 양끝 락너트 0.016. 타이바는 배럴 관통(로드 표현).
                    const float barR = 0.019f, nutR = 0.016f;
                    Vector3 tbMid = bBoom + d * 0.065f;                    // 배럴 중심(붐 정착 근처 = 조절단)
                    Vector3 tbA = tbMid - d * 0.028f, tbB = tbMid + d * 0.028f;
                    Rod(root, "Backstay_Turnbuckle", tbA, tbB, barR, CDark);                       // 배럴(굵은 슬리브)
                    Rod(root, "Backstay_TB_Nut", tbA - d * 0.006f, tbA + d * 0.006f, nutR, CMachine);   // 락너트(양끝)
                    Rod(root, "Backstay_TB_Nut", tbB - d * 0.006f, tbB + d * 0.006f, nutR, CMachine);
                }
            }
        }

        // 가동부 시각화

        static void BuildTrolleyVisual(Transform trolley)
        {
            // [Option C 기계실 통합 박스] 트롤리를 실척 ≈7m 박스 프레임으로 확장(육지쪽 절반에 운전실 통합).
            //   +X=바다, −X=육지. 인양점(헤드/시브)은 스프레더 직상 X=HoistX, 본체 중심은 육지쪽으로 TrolleyCX 이동 → 육지절반이 캐빈 통합부.
            float tZ = 0.24f;   // nested 본체 폭(z±0.12 = 바퀴·다운레그와 일치, < 갭/2=0.1325)
            PbBox(trolley, "Trolley_Body", new Vector3(TrolleyCX, -0.025f, 0f),
                new Vector3(2f * TrolleyHX, 0.05f, tZ), CTrolley, bevel: 0.05f);
            BuildTrolleyBodyFrame(trolley, TrolleyHX, TrolleyCX);   // 1) 본체 프레임화(확장 치수 전달)
            // 헤드(로프 인양점) — 인양점 X=HoistX(항구쪽 이동), 스프레더 직상
            PbBox(trolley, StsPartNames.TrolleyHead, new Vector3(HoistX, -0.06f, 0f),
                new Vector3(0.07f, 0.025f, tZ * 0.85f), CDark, bevel: 0.2f);
            // [rail-in-middle] 주행 보기 — 긴 본체 양 끝 근처 2스테이션 × 좌우 레일(Z=±railZ). 레일 위 트레드 접지(중심 0.037).
            //   8륜(2스테이션×2륜×2레일)으로 긴 박스 하중 분산. 다운레그는 레일 안쪽 z=0.10(레일 z=0.12 회피).
            float railZ = GirderGapZ - GirderWidthZ * 0.5f - 0.0125f;   // ≈0.12, 붐 레일·본체와 동일
            float wheelY = BoomRailTopY + 0.011f;   // 바퀴 축 중심 = 레일 윗면 + 트레드 반경(0.011) → 트레드 하단 접지(공유 상수 파생)
            foreach (float bx in new[] { TrolleyCX - TrolleyHX * 0.66f, TrolleyCX + TrolleyHX * 0.66f })   // 보기 X스테이션 ≈ 중심±0.096(끝 근처)
            for (int s = -1; s <= 1; s += 2)
            {
                float gz = s * railZ;
                float frameZ = gz - s * 0.015f;   // ≈0.105 — 보기 사이드프레임(저널 판), 레일 안쪽
                Box(trolley, "Trolley_Bogie", new Vector3(bx, wheelY, frameZ),
                    new Vector3(0.075f, 0.03f, 0.006f), CDark);
                foreach (float dwx in new[] { -0.028f, 0.028f })   // 보기당 2륜 — 트레드+가이드 플랜지+축
                {
                    float wx = bx + dwx;
                    Rod(trolley, "Trolley_Wheel", new Vector3(wx, wheelY, gz - 0.007f),
                        new Vector3(wx, wheelY, gz + 0.007f), 0.011f, CRail);          // 트레드(반경 0.011)
                    Rod(trolley, "Wheel_Flange", new Vector3(wx, wheelY, gz - s * 0.008f),
                        new Vector3(wx, wheelY, gz - s * 0.011f), 0.0145f, CRail);     // 안쪽 가이드 플랜지(이탈 방지)
                    Rod(trolley, "Wheel_Axle", new Vector3(wx, wheelY, gz),
                        new Vector3(wx, wheelY, frameZ), 0.003f, CDark);               // 축 → 사이드프레임
                }
                // 다운레그 — 보기(Y0.037) → 본체 윗면(Y0). 레일 안쪽(z0.10)
                Box(trolley, "Trolley_Leg", new Vector3(bx, 0.0185f, s * 0.10f),
                    new Vector3(0.012f, 0.037f, 0.012f), CTrolley);
            }
            //   '케이블 트롤리 내부 통과' 컨셉: 윗구간(HoistU)은 본체 안으로 인입(은폐), 양정 로프(Hoist_Rope)는 트롤리
            //   바닥에서 그대로 강하 → 노출 도르래 없음.
            // 로프 데드엔드 소켓 (와이어 연결부, 육지쪽 인입 정착)
            // 데드엔드 소켓 — 육지쪽(x=-0.03) 로프 falls 2개를 트롤리에 정착(스펠터 소켓 몸체 + 클레비스 핀 + 바스켓)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 sk = new Vector3(HoistX - HoistSprX, -0.052f, sz * HoistSprZ);
                Box(trolley, "Rope_Socket_Body", sk, new Vector3(0.012f, 0.02f, 0.012f), CStruct);
                Rod(trolley, "Rope_Socket_Pin",
                    sk + new Vector3(-0.008f, 0.011f, 0f), sk + new Vector3(0.008f, 0.011f, 0f), 0.0025f, CDark);
                Cone(trolley, "Rope_Socket_Basket",
                    sk + new Vector3(0f, -0.016f, 0f), sk + new Vector3(0f, -0.004f, 0f), 0.003f, 0.007f, CDark);
            }
            // 운전실(운전석) — 현실 STS대로 스프레더의 '육지쪽(−X)'에 매달려 '바다쪽(+X=배)'을 향해 전면 경사창으로 발밑 화물을 내려다본다.
            BuildOperatorCab(trolley);

            // 트롤리 주행 모터 — 숨김(사용자 요청). 복구하려면 주석 해제.
            // for (int s = -1; s <= 1; s += 2)
            //     Rod(trolley, "Trolley_Motor", new Vector3(-0.085f, -0.062f, s * 0.075f),
            //         new Vector3(-0.055f, -0.062f, s * 0.075f), 0.013f, CMachine);
            PbBox(trolley, "Trolley_FestoonBox", new Vector3(-0.06f, -0.06f, 0f),
                new Vector3(0.018f, 0.025f, 0.03f), CDark);
            // 트롤리 양끝 완충 버퍼(적색) — 본체 커진(Option-C) 뒤 낡은 ±0.058은 본체 속 57mm 매립됐음.
            //   본체 끝면(TrolleyCX±TrolleyHX)에서 4mm 돌출로 이동 → 실제 '양끝'에서 완충 기능.
            for (int sx = -1; sx <= 1; sx += 2)
                PbBox(trolley, "Trolley_Bumper", new Vector3(TrolleyCX + sx * (TrolleyHX + 0.004f), -0.01f, 0f),
                    new Vector3(0.008f, 0.014f, 0.04f), CWarn, bevel: 0.3f);

            // 호이스트 윗구간(뒷면→백리치 앵커)은 BuildHoistUpper에서 동적(TrolleyReevingRig)으로 생성.
        }

        // 트롤리 본체 프레임화 (Trolley 디테일 1) — 민짜 박스를 용접 프레임으로 분절.
        //   코너 포스트 4 + 상·하 둘레 종재 + 측면(±X) 수직 리브 + 단부(±Z) 리브 + 패널 이음매.
        //   본체 박스(0.11×0.05×0.37) 좌표 불변, 표면에 proud 부재만 추가(폴리: 가는 박스 ~36개).
        // 확장 치수화(Option C): hx=본체 반길이X, cx=본체 X중심(육지쪽 −X 이동). 긴 박스라 리브를
        //   '긴 ±Z 면(X×Y)'에 X-다단으로, 단부는 '짧은 ±X 끝면(Z×Y)'에 Z-다단으로 건다.
        static void BuildTrolleyBodyFrame(Transform trolley, float hx, float cx)
        {
            float hz = 0.12f;   // 본체 반치수 Z — [rail-in-middle] 본체(tZ=0.24)·바퀴 z0.12에 맞춤
            float yTop = 0f, yBot = -0.05f, yMid = -0.025f, h = 0.05f;

            // 코너 포스트 4 — 본체 모서리 수직 트림
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Box(trolley, "TB_Corner", new Vector3(cx + sx * hx, yMid, sz * hz),
                    new Vector3(0.009f, h + 0.006f, 0.009f), CStruct);

            // 상·하 둘레 종재(긴 변 X + 짧은 변 Z)
            foreach (float y in new[] { yTop, yBot })
            {
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(trolley, "TB_Chord", new Vector3(cx, y, sz * hz),
                        new Vector3(2f * hx, 0.007f, 0.008f), CStruct);
                for (int sx = -1; sx <= 1; sx += 2)
                    Box(trolley, "TB_Chord", new Vector3(cx + sx * hx, y, 0f),
                        new Vector3(0.008f, 0.007f, 2f * hz), CStruct);
            }

            // 긴 측면(±Z) 수직 리브 — X 따라 다단(평탄한 긴 면 분절)
            int ribs = 8;
            for (int sz = -1; sz <= 1; sz += 2)
            for (int i = 1; i < ribs; i++)
            {
                float x = cx + Mathf.Lerp(-hx, hx, i / (float)ribs);
                Box(trolley, "TB_Rib", new Vector3(x, yMid, sz * (hz + 0.001f)),
                    new Vector3(0.006f, h, 0.005f), CTrolley);
            }

            // 단부(±X) 수직 리브 3 — 짧은 끝면 분절(Z 따라)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int i = -1; i <= 1; i++)
                Box(trolley, "TB_EndRib", new Vector3(cx + sx * (hx + 0.001f), yMid, i * hz * 0.55f),
                    new Vector3(0.005f, h, 0.006f), CTrolley);

            // 패널 이음매(가로) — 긴 ±Z 면 평탄함 제거
            for (int sz = -1; sz <= 1; sz += 2)
                Box(trolley, "TB_Seam", new Vector3(cx, yMid, sz * (hz + 0.0005f)),
                    new Vector3(2f * hx, 0.003f, 0.003f), CDark);
        }

        // 운전실(운전석) — 현실 STS대로 트롤리 하부 '육지쪽(−X)'에 매달려 '바다쪽(+X=배)'을 향해 전면 경사창으로 발밑 화물(스프레더/선박)을 내려다본다.
        //   [배치] 좌표 산식은 트롤리-로컬 그대로(cx=-0.078 등)이고, 'cab' 홀더 회전 identity + 평행이동 −0.018로
        //          본체가 육지쪽(−X)에 가고 전면(cab-local +X)이 스프레더/배(+X 방향)를 향한다(이전 바다쪽 매달림에서 거울 반전).
        //   실제 STS 운전실: 전면 하부 '경사창'(내려다보기) + 좌석/콘솔 + 측·후면 도어 + 지붕 + 하부 작업등 + 후면 접근 플랫폼.
        //   본체(y≥-0.05)·헤드(x∈±0.035)·로프소켓(x≈-0.03)과 안 겹치게(미러 후 |x|≥0.042 대칭 유지): y≤-0.052.
        // 통짜 강철 셸(CSG, 리플렉션 호출) — ProBuilder의 CSG/Model은 internal(에디터 어셈블리에만 InternalsVisibleTo)이라
        //   어셈블리를 참조해도 직접 못 씀 → 리플렉션으로 Subtract 호출. 임시 큐브는 CreatePrimitive(ProBuilder 의존 0), 결과는 평범한 메시.
        //   좌표계: 임시 큐브를 월드 원점에 cab-local 값으로 세워 빼기 → 결과 메시 정점이 cab-local 좌표 →
        //          cab(identity 홀더, −0.018 이동) 자식으로 SetParent(false) 시 cab 변환이 적용돼 정위치.
        static System.Type _csgT;
        static System.Reflection.MethodInfo _csgSub, _csgToMesh;
        static bool _csgResolved, _csgOk;
        static void ResolveCsg()
        {
            if (_csgResolved) return; _csgResolved = true;
            _csgT = System.Type.GetType("UnityEngine.ProBuilder.Csg.CSG, Unity.ProBuilder.Csg");
            var modelT = System.Type.GetType("UnityEngine.ProBuilder.Csg.Model, Unity.ProBuilder.Csg");
            if (_csgT == null || modelT == null) return;
            var bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            _csgSub = _csgT.GetMethod("Subtract", bf, null, new[] { typeof(GameObject), typeof(GameObject) }, null);
            foreach (var m in modelT.GetMethods(bf))
                if (m.Name == "op_Explicit" && m.ReturnType == typeof(Mesh)) { _csgToMesh = m; break; }
            _csgOk = _csgSub != null && _csgToMesh != null;
            Debug.Log($"[OperatorCab][진단] CSG 리플렉션 해석 — csgType={_csgT != null}, modelType={modelT != null}, Subtract={_csgSub != null}, op_Explicit→Mesh={_csgToMesh != null} ⇒ csgOk={_csgOk}");
        }

        static GameObject CabCsgShell(Transform cab, Vector3 outerC, Vector3 outerS,
                                      Vector3 cavC, Vector3 cavS,
                                      List<Vector3> cutC, List<Vector3> cutS, Color color)
        {
            ResolveCsg();
            if (!_csgOk) throw new System.Exception("ProBuilder CSG(reflection) 사용 불가");
            var mat = GetMaterial(color);
            var temps = new List<GameObject>();
            GameObject Cube(Vector3 c, Vector3 s)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
                go.transform.position = c; go.transform.localScale = s;
                var r = go.GetComponent<MeshRenderer>(); if (r != null) r.sharedMaterial = mat;
                temps.Add(go);
                return go;
            }
            GameObject FromMesh(Mesh m)
            {
                var go = new GameObject("csg_tmp");
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                temps.Add(go);
                return go;
            }
            Mesh ToMesh(object model) => (Mesh) _csgToMesh.Invoke(null, new[] { model });

            GameObject body = Cube(outerC, outerS);
            object mdl = _csgSub.Invoke(null, new object[] { body, Cube(cavC, cavS) });
            for (int i = 0; i < cutC.Count; i++)
            {
                body = FromMesh(ToMesh(mdl));
                mdl = _csgSub.Invoke(null, new object[] { body, Cube(cutC[i], cutS[i]) });
            }
            Mesh shellMesh = ToMesh(mdl);
            shellMesh.name = "Cab_Shell_Mesh";
            // ★ Cab_Shell 메시 직접 닫힘검사(수학적): 위치로 정점 용접 후, 삼각형 1개에만 속한 에지(=경계/구멍) 수.
            //    watertight면 0. >0이면 CSG가 면 빠짐/뒤집힘 → see-through("다 뚫림"). 그 경우 temps 정리 후 throw → 패널 셸로 폴백.
            // ★ Cab_Shell 닫힘검사 — 측정값 boundaryEdges=105 확인: 실험적 CSG가 이 형상(동일평면 다발: Y=-0.087에
            //    공동top·창top 4겹)에서 결정적으로 면이 빠져 사방이 열림. T-정션 수준(<10) 아님. → temps 정리 후 throw → watertight 패널 셸로 폴백.
            int boundaryEdges = CountBoundaryEdges(shellMesh);
            Debug.Log($"[OperatorCab][진단] Cab_Shell 닫힘검사 — verts={shellMesh.vertexCount}, tris={shellMesh.triangles.Length / 3}, boundaryEdges={boundaryEdges} (0이어야 watertight), bounds={shellMesh.bounds.size}");
            if (boundaryEdges > 0)
            {
                foreach (var t in temps) if (t != null) Object.DestroyImmediate(t);
                throw new System.Exception($"CSG 셸 열림(구멍 {boundaryEdges}개) → watertight 패널 셸로 폴백");
            }

            var shell = new GameObject(Numbered("Cab_Shell"));
            shell.transform.SetParent(cab, false);
            shell.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            var mr = shell.AddComponent<MeshRenderer>();
            int sc = Mathf.Max(1, shellMesh.subMeshCount);
            var arr = new Material[sc]; for (int i = 0; i < sc; i++) arr[i] = mat;
            mr.sharedMaterials = arr;

            foreach (var t in temps) if (t != null) Object.DestroyImmediate(t);
            return shell;
        }

        // 메시 닫힘 검사 — 위치(1e-5 양자화)로 정점을 용접하고, 삼각형 1개에만 속한 에지(경계=구멍) 수를 반환.
        //   닫힌(watertight) 다양체면 모든 에지가 정확히 2개 삼각형에 공유되어 0. 면 빠짐/뒤집힘이 있으면 >0.
        static int CountBoundaryEdges(Mesh m)
        {
            var verts = m.vertices;
            var tris = m.triangles;
            var id = new int[verts.Length];
            var posMap = new Dictionary<string, int>();
            int next = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i];
                string key = Mathf.RoundToInt(p.x * 100000f) + "_" + Mathf.RoundToInt(p.y * 100000f) + "_" + Mathf.RoundToInt(p.z * 100000f);
                if (!posMap.TryGetValue(key, out int vid)) { vid = next++; posMap[key] = vid; }
                id[i] = vid;
            }
            var edge = new Dictionary<long, int>();
            void Add(int a, int b)
            {
                int lo = a < b ? a : b, hi = a < b ? b : a;
                long e = ((long)lo << 32) | (uint)hi;
                edge.TryGetValue(e, out int c); edge[e] = c + 1;
            }
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = id[tris[t]], b = id[tris[t + 1]], c = id[tris[t + 2]];
                if (a == b || b == c || c == a) continue;   // 퇴화 삼각형 무시
                Add(a, b); Add(b, c); Add(c, a);
            }
            int boundary = 0;
            foreach (var kv in edge) if (kv.Value == 1) boundary++;
            return boundary;
        }

        // CSG 실패(Experimental) 시 폴백 — **완전 watertight 6면 셸**(패널 타일링). 개구(전면창·측면창·후면도어·바닥
        // lookdown)는 호출부 유리 좌표에 정확히 맞춰 4편 프레임으로 둘러쌈. 인접 면은 코너에서 솔리드 직교 중첩(틈 0).
        // (구버전 폴백은 전벽/측벽/프레임이 없어 유리가 허공에 뜨고 바닥만 노치 물고 삐져나옴 — 그 깨짐을 전면 보강.)
        static void BuildCabBoxShellFallback(Transform cab, float hx, float hz, float cx,
                                             float floorY, float roofY, float floorTopY, float hdrY,
                                             float sillY, float gfBackX, float rim, Color bodyC)
        {
            float frontX = cx + hx, backX = cx - hx;      // -0.044 / -0.112
            float Zs = hz;                                 // 반폭 0.040
            float Yb = floorY, Yt = roofY + 0.003f;        // 바닥 -0.152 / 지붕 top -0.074
            const float t = 0.004f, tf = 0.006f, tr = 0.005f;
            float Xfp = frontX - t * 0.5f;                 // 전벽 평면 -0.046
            float Xbp = backX + t * 0.5f;                  // 후벽 평면 -0.110
            float Zsp = Zs - t * 0.5f;                     // 측벽 평면 ±0.038
            float Yfp = Yb + tf * 0.5f;                    // 바닥 평면
            float Yrp = Yt - tr * 0.5f;                    // 지붕 평면
            float D = frontX - backX;                      // 깊이 0.068

            // 지붕(솔리드)
            PbBox(cab, "Cab_Fb_Roof", new Vector3(cx, Yrp, 0f), new Vector3(D, tr, 2f * Zs), bodyC);

            // 바닥: 전방 lookdown 개구(X∈[gfBackX,frontX], Z∈±(hz-rim)) 둘러싼 프레임(좌석부 솔리드 + 측면 림)
            float gfz = hz - rim;                          // 0.036
            float rearW = gfBackX - backX;
            PbBox(cab, "Cab_Fb_FloorRear", new Vector3((backX + gfBackX) * 0.5f, Yfp, 0f), new Vector3(rearW, tf, 2f * Zs), bodyC);
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_Fb_FloorRim", new Vector3((gfBackX + frontX) * 0.5f, Yfp, sz * (Zs + gfz) * 0.5f), new Vector3(frontX - gfBackX, tf, Zs - gfz), bodyC);

            // 전벽(X=Xfp): 전면창(Y∈[fwBot,hdrY], Z∈±0.9hz) 둘러싼 헤더/실/좌·우 코너기둥
            float fwBot = floorTopY + 0.002f, fwTop = hdrY, fwHz = hz * 0.90f;
            PbBox(cab, "Cab_Fb_FrontHdr",  new Vector3(Xfp, (fwTop + Yt) * 0.5f, 0f), new Vector3(t, Yt - fwTop, 2f * Zs), bodyC);
            PbBox(cab, "Cab_Fb_FrontSill", new Vector3(Xfp, (Yb + fwBot) * 0.5f, 0f), new Vector3(t, fwBot - Yb, 2f * Zs), bodyC);
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_Fb_FrontPost", new Vector3(Xfp, (fwBot + fwTop) * 0.5f, sz * (Zs + fwHz) * 0.5f), new Vector3(t, fwTop - fwBot, Zs - fwHz), bodyC);

            // 후벽(X=Xbp): 도어 개구(Z∈±0.012, Y∈[floorTopY, +0.82·bwH]) 둘러싼 헤더/실/좌·우 잼
            float bwH = hdrY - floorTopY;
            float dwb = floorTopY, dwt = floorTopY + bwH * 0.82f, dwHz = 0.012f;
            PbBox(cab, "Cab_Fb_BackHdr",  new Vector3(Xbp, (dwt + Yt) * 0.5f, 0f), new Vector3(t, Yt - dwt, 2f * Zs), bodyC);
            PbBox(cab, "Cab_Fb_BackSill", new Vector3(Xbp, (Yb + dwb) * 0.5f, 0f), new Vector3(t, dwb - Yb, 2f * Zs), bodyC);
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_Fb_BackJamb", new Vector3(Xbp, (dwb + dwt) * 0.5f, sz * (Zs + dwHz) * 0.5f), new Vector3(t, dwt - dwb, Zs - dwHz), bodyC);

            // 측벽(Z=±Zsp)×2: 측면창(X∈[-0.100,-0.048], Y∈[sillY,hdrY]) 둘러싼 헤더/실/전·후 잼
            float swX0 = -0.100f, swX1 = -0.048f, swYb = sillY, swYt = hdrY;
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float z = sz * Zsp;
                PbBox(cab, "Cab_Fb_SideHdr",   new Vector3(cx, (swYt + Yt) * 0.5f, z), new Vector3(D, Yt - swYt, t), bodyC);
                PbBox(cab, "Cab_Fb_SideSill",  new Vector3(cx, (Yb + swYb) * 0.5f, z), new Vector3(D, swYb - Yb, t), bodyC);
                PbBox(cab, "Cab_Fb_SideJambF", new Vector3((swX1 + frontX) * 0.5f, (swYb + swYt) * 0.5f, z), new Vector3(frontX - swX1, swYt - swYb, t), bodyC);
                PbBox(cab, "Cab_Fb_SideJambB", new Vector3((backX + swX0) * 0.5f, (swYb + swYt) * 0.5f, z), new Vector3(swX0 - backX, swYt - swYb, t), bodyC);
            }
        }

        //  mountTopY: 통합 마운트 상단 Y(운전실이 매달리는 상부 구조 밑면, cab-local model 단위).
        //    STS(기본 −0.05)=트롤리 박스 하단. RTG는 프레임 밑면(t-local 23.2)에 맞추려 −0.072를 넘긴다
        //    (RtgCraneCreator.BuildTrolleyCab의 holder scale24·pos.y24.88 기준: post top=mountTopY+0.003 → t-local 23.224).
        static void BuildOperatorCab(Transform trolley, float mountTopY = -0.05f)
        {
            // 육지쪽 매달림 — 현실 STS대로 운전실은 스프레더의 '육지쪽(−X)'에 있고 운전자가 '바다쪽(+X=배)'을 바라본다.
            //   실제 STS 운전자는 선박 셀에 컨테이너 꽂는 걸 봐야 하므로 시선이
            //   스프레더 너머 배 안쪽까지 뻗어야 함 → 운전실은 인양점의 '육지쪽'에 두고 '바다쪽'을 향해 내려다본다.
            //   이전 구현(바다쪽 매달림·육지 응시)은 좌우 반대였음. 트롤리 X=0 평면 기준 거울 반전으로 정정.
            //   거울 산식: 기존 final_x = 0.018 − pₓ(홀더 +0.018 + Rot180). 거울상 −(0.018 − pₓ) = −0.018 + pₓ
            //   ⇒ 홀더 회전 identity(정상회전, 노멀/와인딩 보존·캡 안 뒤집힘) + 평행이동 −0.018. 운전실이 Z대칭이라
            //   180°Y와 동일 실루엣 유지하며 본체중심 −0.096(육지쪽)·전면창 +X(바다=스프레더/배) 응시로 정확히 반전.
            //   현수 루트는 트롤리 X=0 기준 대칭(−0.048)이라 본체(±0.055) 안에 그대로 안착.
            Transform cab = new GameObject("OperatorCab").transform;
            cab.SetParent(trolley, false);
            cab.localRotation = Quaternion.identity;
            // 클리어런스 평행이동도 거울 반전: 스프레더 헤드블록(±0.045)·플랜지(±0.054)와 X그림자 겹침 회피를
            //   육지쪽(−X)으로 0.018 이동해 동일하게 유지(거울상이므로 부호만 반대).
            const float CabLandwardShift = 0.018f;
            cab.localPosition = new Vector3(-CabLandwardShift, 0f, 0f);
            //  OPERATOR CAB (CSG 통짜 강철 셸 유지 — 오너 지시: CSG 폐기 금지, 원인 진단 후 수정).
            //  cab-local x<0(미러 전), +X=전방(스프레더). 지붕 top < 헤드 하단(-0.0725).
            //  CSG 105구멍의 근본원인 = 절단 상/하단이 공동 면과 동일평면(coplanar):
            //     공동top(-0.087)에 전면창·측면창×2 top 4겹, 공동bottom(-0.147)에 도어 bottom 2겹 → BSP가 면 떨굼.
            //     → 각 절단 면을 hidden 좌표(헤더밴드/바닥솔리드 안)로 서로 다른 미소량 이동해 동일평면 해소(아래 cut 정의).
            //     닫힘검사(boundaryEdges>0 → throw → 폴백) 게이트가 보호하므로 0이 안 되면 자동으로 안전 폴백.
            //     ※ 폴백(BuildCabBoxShellFallback)은 이미 6면+코너기둥+도어/측창 프레임 watertight 완성본임(과거 '미완성' 주석은 스테일).
            float hx = 0.034f, hz = 0.040f, cx = -0.078f;       // 반깊이/반폭, 중심 X (cab-local; 홀더 identity → 트롤리 −X=육지쪽에 위치)
            float roofY = -0.077f, floorY = -0.152f;            // 지붕 top=-0.074<-0.0725; 높이 0.075(~1.8m)
            float frontX = cx + hx, backX = cx - hx;            // 전 -0.044, 후 -0.112 (둘 다 <0)
            float floorTopY = floorY + 0.005f;                  // 구조 바닥 윗면(밟는 면)
            float gfBackX   = cx + 0.010f;                      // 글래스 플로어 뒤 경계(좌석 앞) = -0.068
            float sillY = floorY + 0.024f;                      // 전면 무릎 transom(=-0.128) — 아래는 그린하우스 유리
            float hdrY  = roofY  - 0.010f;                      // 창 밴드 상단(헤더 하단) = -0.087
            Color frame = CDark, bodyC = CStruct, glass = CGlass;
            Color shellC = CStruct;

            const float rim = 0.004f, wt = 0.004f;
            float roofTopY = roofY + 0.003f;                                   // 지붕 윗면 -0.074 (<-0.0725)
            float gfCx = (gfBackX + frontX) * 0.5f, gfW = frontX - gfBackX;
            float ubY = (sillY + hdrY) * 0.5f, ubH = hdrY - sillY;
            float bwH = hdrY - floorTopY;
            float fwBot = floorTopY + 0.002f;                                  // 전면창 하단(바닥 직전까지)
            // [수학팀] CSG watertight: 절단 면이 공동 면과 동일평면이면 BSP가 면을 떨군다(측정 105 구멍).
            //    공동 top=hdrY(-0.087)·bottom=floorTopY(-0.147). 아래 4값으로 각 절단을 서로 다른 hidden 면으로 분리.
            float fwTopCut  = hdrY + 0.0020f;                                  // 전면창 top -0.0850 (헤더밴드 -0.0895..-0.0845 안)
            float swTopCutP = hdrY + 0.0015f, swTopCutN = hdrY + 0.0010f;      // 측면창 +Z/-Z top -0.0855 / -0.0860 (서로·공동top과 비동일평면)
            float doorBot   = floorTopY - 0.0015f;                             // 도어 bottom -0.1485 (바닥솔리드 -0.152..-0.147 안에 묻힘)
            float doorTop   = floorTopY + bwH * 0.82f;                         // 도어 top -0.0978 (불변)
            var cutC = new List<Vector3>();
            var cutS = new List<Vector3>();
            cutC.Add(new Vector3(frontX, (fwBot + fwTopCut) * 0.5f, 0f));      // 1) 전면창(지배 요소 — 폭 0.90·코너 기둥 자리만 남김)
            cutS.Add(new Vector3(wt * 4f, fwTopCut - fwBot, 2f * hz * 0.90f));
            for (int sz = -1; sz <= 1; sz += 2)                                // 2) 측면창 ×2(top 좌우 다르게 → 비동일평면)
            {
                float swTop = sz > 0 ? swTopCutP : swTopCutN;
                cutC.Add(new Vector3(-0.074f, (sillY + swTop) * 0.5f, sz * hz));
                cutS.Add(new Vector3(0.048f, swTop - sillY, wt * 4f));
            }
            cutC.Add(new Vector3(backX, (doorBot + doorTop) * 0.5f, 0f));      // 3) 후면 도어 개구(bottom을 floorTopY 아래로 내려 비동일평면)
            cutS.Add(new Vector3(wt * 4f, doorTop - doorBot, 0.024f));
            cutC.Add(new Vector3(gfCx - 0.001f, floorY + 0.0025f, 0f));        // 4) 바닥 lookdown 개구
            cutS.Add(new Vector3(gfW - 0.002f, wt * 4f, 2f * (hz - rim)));

            GameObject shell = null;
            try
            {
                shell = CabCsgShell(cab,
                    new Vector3(cx, (floorY + roofTopY) * 0.5f, 0f), new Vector3(2f * hx, roofTopY - floorY, 2f * hz),                 // 외곽 솔리드
                    new Vector3(cx, (floorTopY + hdrY) * 0.5f, 0f), new Vector3(2f * hx - 2f * wt, hdrY - floorTopY, 2f * hz - 2f * wt), // 내부 공동
                    cutC, cutS, shellC);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[OperatorCab] CSG 셸 실패 → 박스 셸 폴백: " + e);
                shell = null;
            }
            if (shell == null)
                BuildCabBoxShellFallback(cab, hx, hz, cx, floorY, roofY, floorTopY, hdrY, sillY, gfBackX, rim, shellC);

            // ---- 유리(개구에 끼움; 가장자리를 살짝 키워 벽 두께 뒤로 숨김 → 동일평면 없음) ----
            // 바닥 유리(개구 안쪽에 tuck — 캡 밖 돌출 제거). X∈[-0.069,-0.047](개구 -0.068..-0.046 안), Z 살짝 키워 가장자리 숨김.
            PbBox(cab, "Cab_GlassFloor", new Vector3(-0.058f, floorY + 0.0025f, 0f), new Vector3(0.022f, 0.0015f, 2f * (hz - rim) + 0.004f), glass);
            // 전면 유리(평면, 개구에 flush) + 깔끔한 평면 차양 — v7: 캔티드 wedge 제거(언더사이드 돌출·유리 중첩이 지저분 → 뒷면 수준으로 정리).
            PbBox(cab, "Cab_GlassFront", new Vector3(frontX - wt * 0.5f, (fwBot + hdrY) * 0.5f, 0f), new Vector3(0.002f, (hdrY - fwBot) + 0.004f, 2f * hz * 0.90f + 0.004f), glass);
            PbBox(cab, "Cab_Visor", new Vector3(frontX + 0.005f, hdrY + 0.003f, 0f), new Vector3(0.012f, 0.004f, 2f * hz + 0.006f), frame); // 평면 차양(전방 12mm, 비돌출·비경사)
            for (int sz = -1; sz <= 1; sz += 2)
                PbBox(cab, "Cab_GlassSide", new Vector3(-0.074f, ubY, sz * (hz - wt * 0.5f)), new Vector3(0.048f + 0.004f, ubH + 0.004f, 0.002f), glass);

            // ---- 프레임(슬림 다크): 4 코너기둥 + 헤더 + 지붕캡. v4: 오렌지 바이저/허리선·멀리언·플로어격자 제거(노이즈) ----
            {
                float postBot = ubY - (bwH + 0.02f) * 0.5f;        // -0.1475 (하단 유지)
                float postTop = roofTopY;                           // -0.074: 클리어런스 -0.0725 준수(상단 클램프)
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    PbBox(cab, "Cab_Post", new Vector3(cx + sx * hx, (postBot + postTop) * 0.5f, sz * hz), new Vector3(0.005f, postTop - postBot, 0.005f), frame);   // 코너 기둥(top=roofTopY로 클램프)
            }
            PbBox(cab, "Cab_HeaderRail", new Vector3(cx, hdrY, 0f), new Vector3(2f * hx - 0.004f, 0.005f, 2f * hz + 0.006f), frame); // 헤더 밴드(앞·옆 proud, 양끝 벽에 묻힘)
            PbBox(cab, "Cab_Roof", new Vector3(cx, roofTopY - 0.0013f, 0f), new Vector3(2f * hx + 0.012f, 0.005f, 2f * hz + 0.012f), frame); // 지붕 캡 top=-0.0728: 셸 지붕(-0.074)보다 proud로 올려 z-fight 제거 + 클리어런스 -0.0725 준수(0.3mm 여유)

            // ---- 후면 도어 리프(개구에 proud로 끼움) + 소창 ----
            PbBox(cab, "Cab_Door",       new Vector3(backX - 0.002f,  floorTopY + bwH * 0.41f, 0f),     new Vector3(0.004f, bwH * 0.82f, 0.024f), frame);
            PbBox(cab, "Cab_DoorWindow", new Vector3(backX - 0.0045f, floorTopY + bwH * 0.60f, 0f),     new Vector3(0.002f, bwH * 0.30f, 0.016f), glass);

            //  후면 외장 의장 — "뒤에 문밖에 없다" → 실제 STS 뒤편 의장.
            //  접근 발판(그레이팅)+안전난간(노랑)+도어 그랩봉+공조(HVAC). 전부 후벽 뒤(X<-0.112)로 돌출/벽에 묻어 z-fight 0. 좌표 산식.
            {
                float pBackX = -0.136f, pWallX = -0.111f, pZ = 0.028f;            // 발판 후방/벽쪽 X(앞단 1mm 벽에 묻음), 반폭 Z
                float pTopY = floorY + 0.003f;                                    // 발판 윗면 -0.149 (≈문턱)
                float railTop = floorY + 0.030f, railMid = floorY + 0.017f;       // 난간 상/중
                // 1) 후면 접근 발판(그레이팅)
                PbBox(cab, "Cab_RearDeck", new Vector3((pBackX + pWallX) * 0.5f, pTopY - 0.002f, 0f), new Vector3(pWallX - pBackX, 0.004f, 2f * pZ + 0.004f), CRail);
                // 2) 안전 난간(노랑, ㄷ자: 후방+양측, 문쪽 개방) — 기둥4 + 상/중 가로봉2
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Rod(cab, "Cab_RailPost", new Vector3(pBackX, pTopY, sz * pZ), new Vector3(pBackX, railTop, sz * pZ), 0.0016f, CSafety);
                    Rod(cab, "Cab_RailPost", new Vector3(pWallX, pTopY, sz * pZ), new Vector3(pWallX, railTop, sz * pZ), 0.0016f, CSafety);
                }
                for (int lvl = 0; lvl < 2; lvl++)
                {
                    float ry = lvl == 0 ? railTop : railMid;
                    Rod(cab, "Cab_RailBack", new Vector3(pBackX, ry, -pZ), new Vector3(pBackX, ry, pZ), 0.0016f, CSafety);
                    for (int sz = -1; sz <= 1; sz += 2)
                        Rod(cab, "Cab_RailSide", new Vector3(pBackX, ry, sz * pZ), new Vector3(pWallX, ry, sz * pZ), 0.0016f, CSafety);
                }
                // 코너 마감 — 둥근 관 조인트는 노드마다 구로 막음(Rb≈1.5r). 후방 코너=3-way(기둥+후방봉+측봉), 벽쪽=2-way(기둥+측봉).
                //   [[reference_corner_solid_joint]]·[[feedback_sphere_for_round_joints_not_flat_steel]] — 평강 아닌 둥근 봉이라 구가 정석.
                {
                    float jD = 0.0032f;   // 조인트 구 지름 = 정확히 2r(=관굵기, r=0.0016). [[reference_conduit_pipe_elbow_routing]]: 2r라 안 부풀고 틈만 메움. (이전 0.0035도 약간 컸음)
                    for (int sz = -1; sz <= 1; sz += 2)
                    for (int lvl = 0; lvl < 2; lvl++)
                    {
                        float ry = lvl == 0 ? railTop : railMid;
                        Ball(cab, "Cab_RailJoint", new Vector3(pBackX, ry, sz * pZ), new Vector3(jD, jD, jD), CSafety);
                        Ball(cab, "Cab_RailJoint", new Vector3(pWallX, ry, sz * pZ), new Vector3(jD, jD, jD), CSafety);
                    }
                }
                // 3) 도어 양옆 수직 그랩봉(노랑) — U핸들: 수직봉 + 상/하 벽 리턴 + 굽힘 구 마감(개방 끝 제거)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float gx = backX - 0.004f, gz = sz * 0.018f, gyb = floorTopY + 0.004f, gyt = floorTopY + 0.040f;
                    Rod(cab,  "Cab_DoorGrab",    new Vector3(gx, gyb, gz), new Vector3(gx, gyt, gz), 0.0015f, CSafety);
                    Rod(cab,  "Cab_DoorGrabRet", new Vector3(gx, gyt, gz), new Vector3(backX, gyt, gz), 0.0014f, CSafety);
                    Rod(cab,  "Cab_DoorGrabRet", new Vector3(gx, gyb, gz), new Vector3(backX, gyb, gz), 0.0014f, CSafety);
                    Ball(cab, "Cab_DoorGrabBend", new Vector3(gx, gyt, gz), new Vector3(0.003f, 0.003f, 0.003f), CSafety);   // 굽힘 구 = 정확히 2r(=관굵기, r=0.0015), [[reference_conduit_pipe_elbow_routing]]
                    Ball(cab, "Cab_DoorGrabBend", new Vector3(gx, gyb, gz), new Vector3(0.003f, 0.003f, 0.003f), CSafety);   // 굽힘 구 = 정확히 2r(=관굵기, r=0.0015), [[reference_conduit_pipe_elbow_routing]]
                }
                // 4) 공조(HVAC) 유닛 + 그릴 — 도어 위(Z=0) 벽에 묻고 뒤로 돌출. 이전 +Z 배치는 그랩봉(Z=±0.018) 관통 → 도어 상단~지붕 중간으로 이동.
                float hvacY = (floorTopY + bwH * 0.82f + roofY + 0.003f) * 0.5f;   // 도어상단(-0.0978)~지붕top(-0.074) 중간 = -0.0859
                PbBox(cab, "Cab_RearHVAC",        new Vector3(backX - 0.006f,  hvacY, 0f), new Vector3(0.014f, 0.018f, 0.030f), CMachine);  // Z∈[-0.015,0.015] → 그랩봉 ±0.018 밖
                PbBox(cab, "Cab_RearHVAC_Grille", new Vector3(backX - 0.0135f, hvacY, 0f), new Vector3(0.002f, 0.014f, 0.024f), frame);
            }

            //  운전실 통합 마운트 v3 (Option C) — 긴 캔틸레버 브래킷 폐기.
            //  트롤리가 실척 ≈7m 박스로 커져 운전실이 '박스 육지절반 바로 아래'에 들어오므로, 박스 하단(Y=-0.05)에서
            //  운전실 지붕(roofTopY=-0.074)까지 짧은(≈0.025) 굵은 수직 포스트 4개 + 하부 결합 종재로 직결한다.
            //  cab-local 좌표(홀더 −0.018): 운전실 4코너 X=frontX(-0.044)/backX(-0.112), Z=±hz(0.040) 모두
            //  박스 X[cab-local −0.157~+0.133]·Z(±0.12) 풋프린트 안 → 포스트가 박스 하단면에 정확히 안착.
            {
                // mountTopY = 상부 결합면(STS 박스 하단 −0.05 / RTG 프레임 밑면 대응 −0.072). 파라미터로 주입.
                float mountBotY = roofTopY - 0.001f;      // 운전실 지붕 결합부 -0.075 (1mm 묻힘)
                float bt = 0.006f;                        // 종재 단면 t — 코너 솔리드 가로질러 덮기
                // 수직 마운트 포스트 4 (front/back × 좌우) — 굵은 box
                for (int sx = -1; sx <= 1; sx += 2)       // sx<0: 후면(backX, 육지끝) / sx>0: 전면(frontX, 바다측)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float px = (sx < 0) ? backX : frontX;
                    PbBox(cab, "Cab_Mount_Post", new Vector3(px, (mountTopY + mountBotY) * 0.5f, sz * hz),
                        new Vector3(0.010f, mountTopY - mountBotY + bt, 0.010f), CStruct);   // 위 +t/2 박스 하단에 묻힘
                }
                // 전·후 결합 종재(Z방향) — 포스트 머리를 가로질러 덮어 박스 하단에 결합([[reference_corner_solid_joint]])
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    float px = (sx < 0) ? backX : frontX;
                    PbBox(cab, "Cab_Mount_Tie", new Vector3(px, mountTopY - 0.003f, 0f),
                        new Vector3(0.012f, 0.006f, 2f * hz + bt), CStruct);
                }
                // 좌·우 결합 종재(X방향) — 전후 포스트를 잇는 하부 둘레
                for (int sz = -1; sz <= 1; sz += 2)
                    PbBox(cab, "Cab_Mount_Tie", new Vector3((frontX + backX) * 0.5f, mountTopY - 0.003f, sz * hz),
                        new Vector3((frontX - backX) + bt, 0.006f, 0.012f), CStruct);
            }

            // ---- 내부(좌석+콘솔+모니터) — 유리 너머로 보임 ----
            float seatX = cx + 0.004f, seatY = floorY + 0.014f;
            PbBox(cab, "Cab_SeatPedestal", new Vector3(seatX - 0.002f, floorY + 0.007f, 0f), new Vector3(0.008f, 0.014f, 0.010f), bodyC);
            PbBox(cab, "Cab_Seat",      new Vector3(seatX, seatY, 0f),                   new Vector3(0.016f, 0.006f, 0.018f), frame);
            PbBox(cab, "Cab_SeatBack",  new Vector3(seatX - 0.010f, seatY + 0.016f, 0f), new Vector3(0.005f, 0.028f, 0.018f), frame);
            PbBox(cab, "Cab_HeadRest",  new Vector3(seatX - 0.009f, seatY + 0.034f, 0f), new Vector3(0.005f, 0.008f, 0.012f), frame);
            for (int sz = -1; sz <= 1; sz += 2)   // 좌우 콘솔만(조이스틱/암레스트 잔부품은 증분2)
                PbBox(cab, "Cab_Console", new Vector3(seatX + 0.012f, seatY + 0.006f, sz * 0.014f), new Vector3(0.014f, 0.007f, 0.007f), bodyC);
            PbBox(cab, "Cab_Monitor",       new Vector3(seatX + 0.020f, seatY + 0.016f, 0f), new Vector3(0.004f, 0.010f, 0.013f), bodyC);
            PbBox(cab, "Cab_MonitorScreen", new Vector3(seatX + 0.0222f, seatY + 0.016f, 0f), new Vector3(0.001f, 0.008f, 0.011f), CLight);

            // 운전실 시점 앵커(빈 오브젝트) — VR 운전 시 카메라가 여기로 정렬(좌석 눈높이, 발밑 화물 향)
            //   StsCraneVRController가 'Cab_Viewpoint'를 최우선 앵커로 잡아 카메라를 이 좌표·전방에 둠(오프셋 0).
            //   cab-local: 좌석 앞쪽 + 눈높이(floorY+0.044≈바닥 위 ~1m). 전방=스프레더(+X), 수평.
            //   → 180° 홀더로 트롤리 -X(발밑 스프레더/선박) 향. 상하 시선은 사용자 머리에 위임.
            //   앵커에 강제 -35° 피치(0,-0.7,..)를 박으면 HMD 수평선과 어긋나 멀미 유발 →
            //   앵커는 수평(피치 0)으로. 발밑은 고개 숙여 본다.
            var viewpoint = new GameObject(StsPartNames.CabViewpoint).transform;
            viewpoint.SetParent(cab, false);
            viewpoint.localPosition = new Vector3(cx + 0.018f, floorY + 0.044f, 0f);
            viewpoint.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);   // 수평 전방(+X) — 멀미 방지

            // v4 보류: 노즈 작업등·후면 안테나 등 외장 잔부품 제거 → 실루엣 확정 후 증분2에서 선별 재도입.
        }

        // 스프레더 — 중앙 고정부(항상 20ft) + 좌/우 텔레스코픽 암(끝빔 + 트위스트락).
        // spreaderHalf(반길이)로 암 위치를 정함: 20ft=중앙 끝에 밀착, 40ft=바깥으로 신장(텔레스코핑 빔이 연결).
        static void BuildSpreaderVisual(Transform spreader, float spreaderHalf, bool includeHead = true)
        {
            // 컨테이너 긴 축이 안벽/주행 방향(Z)을 향하도록 스프레더 전체를 90° 회전
            // (부속은 긴 축=로컬 X로 배치 → Y축 90° 회전으로 월드 Z가 긴 축이 됨)
            spreader.localRotation = Quaternion.Euler(0f, 90f, 0f);

            float hw = 0.05f;                  // 반폭(로컬 Z) — 컨테이너 폭 비례(20·40ft 동일)
            const float hl0 = SpreaderHalf20;  // 중앙 고정부 반길이(항상 20ft)
            Color CMetal = CRail;              // 트위스트락 회색

            // 중앙 고정부(항상 20ft) — 메인 빔 + 상·하 플랜지
            Box(spreader, "Spreader_Bar", Vector3.zero,
                new Vector3(hl0 * 2f, 0.028f, hw * 2f), CSpread);
            for (int sy = -1; sy <= 1; sy += 2)
                Box(spreader, "Beam_Flange", new Vector3(0f, sy * 0.016f, 0f),
                    new Vector3(hl0 * 2f, 0.006f, hw * 2f + 0.008f), CSpread);

            // 헤드블록 — includeHead=false(예: RTG)면 외부가 자체 헤드블록을 얹으므로 생략
            if (includeHead)
            {
            float hbY = 0.058f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Strut(spreader, "Head_Frame",
                    new Vector3(sx * 0.06f, 0.016f, sz * 0.038f),
                    new Vector3(sx * 0.035f, hbY - 0.012f, sz * 0.02f), 0.006f, CSpread);
            // [디자인] 헤드블록 확대 — 넓힌 데드엔드 소켓 4(로컬 x±HoistSprZ=0.075, z±HoistSprX=0.05 + 콘 베이스 0.0085)가 모서리 밖으로 안 삐지게.
            //   반폭 x 0.09(>0.0835), z 0.0625(>0.0585) → 각 변 ~0.005~0.007 여유.
            Box(spreader, "Spreader_Head", new Vector3(0f, hbY, 0f),
                new Vector3(0.18f, 0.026f, 0.125f), CSpread);
            // 헤드블록 시브/치크/핀 제거 → STS 데드엔드형.
            //   호이스트 로프 4가닥(parent x=HoistX±HoistSprX, z±HoistSprZ)이 시브를 안 거치고 곧장 내려와 헤드블록 상단에 정착(spelter socket dead-end).
            //   리빙/도르래는 트롤리 쪽에만 둠 — 로프가 안 감기는 헤드블록 시브는 비기능 장식이라 제거.
            // 스프레더가 Y축 90° 회전이라, 로프(부모공간 x±HoistSprX, z±HoistSprZ)와 맞추려면 스프레더-로컬은 x±HoistSprZ, z±HoistSprX (xz 스왑 보정).
            //   parent의 HoistX(항구 이동)는 스프레더 transform(localPosition.x=HoistX)이 부여 → 소켓 로컬엔 안 넣음(중복 금지).
            foreach (float rx in new[] { -HoistSprZ, HoistSprZ })
            foreach (float rz in new[] { -HoistSprX, HoistSprX })
            {
                Vector3 sk = new Vector3(rx, hbY + 0.013f, rz);   // 헤드블록 상단면(0.071), 회전 보정해 로프 바로 아래
                // 스펠터 소켓 — 위 좁은 넥(r0.0045)=로프 인입측(로프가 위에서 강하), 아래 넓은 바스켓(r0.0085)=헤드블록 정착측.
                //   (트롤리 소켓 Rope_Socket_Basket과 동일 규약: 좁은 넥이 로프쪽, 넓은 바스켓이 정착쪽 — 감사 SPR-2는 허위양성, 반전 불필요)
                Cone(spreader, "Head_Rope_Socket",
                    sk + new Vector3(0f, -0.002f, 0f), sk + new Vector3(0f, 0.012f, 0f), 0.0085f, 0.0045f, CStruct);
                Rod(spreader, "Head_Rope_Collar",                 // 넥 칼라 밴드(단조 디테일)
                    sk + new Vector3(0f, 0.008f, 0f), sk + new Vector3(0f, 0.011f, 0f), 0.0055f, CMachine);
                Rod(spreader, "Head_Rope_Pin",                    // 클레비스 핀(베이스 관통, 양옆 돌출)
                    sk + new Vector3(0f, 0.001f, -0.011f), sk + new Vector3(0f, 0.001f, 0.011f), 0.0022f, CDark);
            }
            }   // if (includeHead)

            // 부속(중앙) — 파워팩/정션박스/작업등
            Vector3[] ppTips = PowerPack(spreader, "Spreader_PowerPack", new Vector3(0.07f, 0.022f, 0f),
                new Vector3(0.04f, 0.022f, hw * 1.2f), CMachine);
            JunctionBox(spreader, "Spreader_JBox", new Vector3(-0.07f, 0.02f, 0f),
                new Vector3(0.03f, 0.018f, 0.03f), Vector3.up, Vector3.down, CDark);
            // 스프레더 작업등(양 끝) — 구 → 하향 Floodlight 어셈블리(컨테이너 조사).
            for (int sx = -1; sx <= 1; sx += 2)
                Floodlight(spreader, new Vector3(sx * 0.085f, 0.016f, 0f), 0.010f, CDark, CLight);

            // 유압/제어 호스 — 파워팩 포트 → J박스 → 헤드블록. 끝점을 부품 '안으로' 묻고 접합부마다 클램프로 봉합(틈 제거)
            //   J박스 본체: 중심(-0.07,0.02,0), 치수(0.03,0.018,0.03) → x:-0.085~-0.055, y:0.011~0.029, z:±0.015
            //   헤드블록 본체: 중심(0,hbY=0.058,0), 치수(0.11,0.026,..) → 밑면 y=0.045
            for (int i = 0; i < 2; i++)
            {
                int sz = i == 0 ? -1 : 1;
                float cz = sz * 0.008f;
                Vector3 jbIn = new Vector3(-0.062f, 0.022f, cz);          // J박스 내부로 묻은 끝점
                Vector3 hbIn = new Vector3(-0.038f, 0.05f,  sz * 0.012f); // 헤드블록 내부로 묻은 끝점

                // 파워팩 포트(ppTips) → J박스 (포트 끝에서 시작, J박스 안으로 종단)
                CableCatenary(spreader, "Spreader_Hose", ppTips[i], jbIn, 0.005f, 0.0014f, CCable);
                Ball(spreader, "Hose_Clamp", new Vector3(-0.056f, 0.024f, cz), Vector3.one * 0.005f, CStruct);

                // J박스(내부) → 헤드블록 밑면(안으로 묻음) : 제어 커넥터
                CableCatenary(spreader, "Spreader_Hose",
                    new Vector3(-0.063f, 0.026f, cz), hbIn, 0.004f, 0.0013f, CCable);
                Ball(spreader, "Hose_Clamp", new Vector3(-0.04f, 0.046f, sz * 0.012f), Vector3.one * 0.005f, CStruct);
            }

            // 좌/우 텔레스코픽 암(끝빔 + 트위스트락) — 런타임에 20↔40ft 슬라이드
            // 각 암을 Transform으로 묶고 자식은 '암 로컬' 좌표(암 원점 = 끝빔 위치)로 배치 →
            // SpreaderTelescope가 암의 로컬 X만 옮기면 끝빔·트위스트락이 통째로 슬라이드한다.
            Transform armL = null, armR = null;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var arm = new GameObject(Numbered(sx < 0 ? "TeleArm_L" : "TeleArm_R"));
                arm.transform.SetParent(spreader, worldPositionStays: false);
                arm.transform.localPosition = new Vector3(sx * spreaderHalf, 0f, 0f);
                Transform a = arm.transform;
                if (sx < 0) armL = a; else armR = a;

                // 끝단 크로스 빔(컨테이너 단부) — 암 원점
                Box(a, "End_Beam", new Vector3(-sx * 0.008f, 0f, 0f),
                    new Vector3(0.016f, 0.03f, hw * 2f + 0.012f), CSpread);
                // 트위스트락 2(앞/뒤 코너) — 둥근 핀(Head) + 길쭉 쐐기 락 콘(Cone). 실물 구조.
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    // 트위스트락 Z를 컨테이너 외폭/2(0.0508)가 아닌 ISO 코너캐스팅 '횡 중심'에 정렬.
                    //   코너캐스팅 중심 = (컨테이너폭/2 − CornerCastD/2)/24 = (2.438/2 − 0.162/2)/24 = 1.138/24 = 0.04742.
                    //   (ProceduralContainerMesh: z = sz·(Width/2 − CornerCastD/2), CornerCastD=0.162). 기존 0.0508은 중심보다 0.0034(실척 ~81mm) 바깥이라 홀을 빗나감.
                    const float isoCornerHalfZ = 0.04742f;
                    // 트위스트락 X를 ISO 코너캐스팅 중심에 정렬. arm 원점=spreaderHalf(컨테이너 끝), 코너는 그보다 안쪽.
                    //   20ft 코너 half=5.853/24/2=0.1219(spreaderHalf 0.126−0.0041), 40ft=11.985/24/2=0.2497(0.254−0.0043).
                    //   기존 −0.006은 20ft 0.120/40ft 0.248로 ~1.8mm(실척 ~43mm) 안쪽 빗남 → −0.0042로 두 사이즈 동시 정렬(±0.1mm).
                    Vector3 c = new Vector3(-sx * 0.0042f, 0f, sz * isoCornerHalfZ);
                    TwistlockHead(a, c, CMetal);
                    TwistlockCone(a, c, CMetal);
                }
            }

            // 텔레스코핑 빔(스프레더 직속) — 중앙 끝(hl0)↔암 사이를 SpreaderTelescope가 X 스케일로 신축.
            // 20ft면 수축(거의 사라짐), 40ft면 신장. 초기 크기는 생성 사이즈 기준(에디터 표시용).
            float initLen = Mathf.Max(0.0001f, (spreaderHalf - hl0) + 0.02f);
            float initMid = (hl0 + spreaderHalf) * 0.5f;
            Transform teleL = Box(spreader, "Tele_Beam", new Vector3(-initMid, 0f, 0f),
                new Vector3(initLen, 0.018f, hw * 1.2f), CDark).transform;
            Transform teleR = Box(spreader, "Tele_Beam", new Vector3(initMid, 0f, 0f),
                new Vector3(initLen, 0.018f, hw * 1.2f), CDark).transform;

            // 런타임 텔레스코픽 전환(시작 사이즈 = 생성 시 spreaderHalf)
            var tele = spreader.gameObject.AddComponent<SpreaderTelescope>();
            tele.Configure(armL, armR, teleL, teleR, hl0, SpreaderHalf20, SpreaderHalf40,
                           spreaderHalf > hl0 + 1e-4f);
        }

        // 트위스트락 핀(둥근 샤프트) — 실물: 스프레더 코너 하우징 안의 회전 너트에 나사 체결된 원형 핀.
        //   'Twistlock_Head'(빈 그룹)를 코너 수직축(=트위스트 회전축)에 두고, 그 아래로 둥근 핀을 내린다.
        //   ★ 핀은 원형이라 90° 회전이 시각적으로 무변화(자기복귀). '보이는 잠금'은 아래의 뭉툭한 락 헤드(숄더)가 담당.
        //   ★ 끝빔 밑면 y=-0.015 위는 빔에 묻히므로, 가는 샤프트는 전부 빔 속에 숨고(체결 너트) '짧은 락 헤드'만 밑으로 노출.
        //     (곤봉 교정: 옛 샤프트가 빔 아래로 길게 노출돼 '가는 막대+혹=곤봉' 실루엣이었음 → 샤프트 끝을 빔 속 -0.014로 끌어올려 감춤.)
        static void TwistlockHead(Transform arm, Vector3 corner, Color metal)
        {
            var head = new GameObject(Numbered(StsPartNames.TwistlockHead));
            head.transform.SetParent(arm, worldPositionStays: false);
            head.transform.localPosition = corner;       // 콘과 동일한 코너 수직축 = 트위스트 회전축
            Transform h = head.transform;

            // 체결 너트/칼라 — 끝빔 밑면에 물리는 머시닝 칼라(실물 더블칼라 나사부). 빔 속~밑면.
            Rod(h, "Twistlock_Collar", new Vector3(0f, 0.006f, 0f), new Vector3(0f, 0.000f, 0f), 0.0013f, CMachine);
            // 단조 핀 샤프트 — 빔 속(+0.005)에서 락 헤드 넥 상단(arm -0.014)까지. 끝이 빔 밑면(-0.015)보다 위라 전부 빔 속에 숨음(곤봉 방지).
            Rod(h, "Twistlock_Body",   new Vector3(0f, 0.005f, 0f), new Vector3(0f, -0.014f, 0f), 0.001f, metal);
        }

        // 트위스트락 콘(락 헤드) — 코너캐스팅 타원 구멍에 삽입돼 90° 돌아 걸리는 단조 락 헤드.
        //   'Twistlock_Cone'(그룹) 원점 = 코너 수직축 위 y=-0.020(SpreaderGrabber 잡기/안착 기준점 = 콘 노즈 tip Y).
        //   원점을 -0.03→-0.020로 올려 빔 밑면(-0.015) 아래 노출을 0.015→0.005(실척 360→120mm)로 축소.
        //     실물 트위스트락은 샤프트가 코너 하우징 안에 숨고 '짧은 헤드'만 빼꼼 나옴 — 옛 0.015 노출은 '가는 막대+혹=곤봉'이라 오류.
        //     안착 기준점도 함께 올라가 '안착 시 스프레더가 컨테이너 위 360mm 부양'하던 비현실 갭이 120mm로 개선(잡기 밴드 [-0.049,+0.015] 내라 안전).
        //   ★ 아래(삽입)=좁은 유도 노즈 → 가운데=넓은 베어링 숄더(90° 회전 시 캐스팅 밑에 걸림) → 위=샤프트로 넥킹.
        //   ★ 장축=Z(상면 구멍 장축 0.00519)에 정렬 삽입 → 90° 회전 시 X로 돌아 숄더가 캐스팅 밑에 걸림(=실제 잠금).
        //     장축 0.00433<0.00519, 단축 0.00233<0.00265 → 구멍 통과 여유(실척 헤드 ≈104×56mm, ISO 오벌홀 124.5×63.5mm).
        //   ★ 둥근 단조 부재라 Cone(프러스텀) 관례 적용 + headGroup X스케일로 타원 단면(Z장축·X단축)을 만든다.
        static void TwistlockCone(Transform arm, Vector3 corner, Color metal)
        {
            var cone = new GameObject(Numbered(StsPartNames.TwistlockCone));
            cone.transform.SetParent(arm, worldPositionStays: false);
            cone.transform.localPosition = corner + new Vector3(0f, -0.020f, 0f);
            Transform c = cone.transform;

            const float zHalf     = 0.00217f;  // 장축 half (104mm/24/2) — 숄더 최대폭
            const float xHalf     = 0.00117f;  //  단축 half (56mm/24/2)
            const float shaftHalf = 0.001f;    // 샤프트 반경(=Twistlock_Body) — 넥 상단
            const float tipHalf   = 0.0009f;   // 노즈 끝 반경(단조 블런트 촉)

            // 타원 단면 그룹 — 원형 프러스텀을 X로 눌러 Z장축/X단축 타원으로(스케일 = 단축half/장축half ≈ 0.539).
            //   ★ 이름은 "Twistlock_Head"(StsPartNames.TwistlockHead)와 겹치면 SpreaderLockAnimator가 이중 수집·회전하므로 반드시 다른 이름.
            var ell = new GameObject("Twistlock_LockHead");
            ell.transform.SetParent(c, worldPositionStays: false);
            ell.transform.localScale = new Vector3(xHalf / zHalf, 1f, 1f);
            Transform h = ell.transform;

            // 노즈(유도 쐐기): local y 0→0.0022, tip→숄더로 벌어짐. tip Y=그룹원점(=안착 기준·arm -0.020, 빔 밑 0.005 노출).
            Cone(h, "Twistlock_LugNose", new Vector3(0f, 0f,      0f), new Vector3(0f, 0.0022f, 0f), tipHalf,   zHalf,     metal, 20);
            // 숄더(베어링 밴드): 0.0022→0.0032, 곧은 옆면 — 90° 회전 시 코너캐스팅 밑에 걸리는 어깨.
            Cone(h, "Twistlock_LugBody", new Vector3(0f, 0.0022f, 0f), new Vector3(0f, 0.0032f, 0f), zHalf,     zHalf,     metal, 20);
            // 넥(샤프트 전이): 0.0032→0.006(arm -0.014, 빔 속), 숄더→샤프트로 좁혀 위쪽 샤프트에 매끈히 연결.
            Cone(h, "Twistlock_LugNeck", new Vector3(0f, 0.0032f, 0f), new Vector3(0f, 0.006f,  0f), zHalf,     shaftHalf, metal, 20);
        }

        // 호이스트 로프 4줄 — spreaderRoot(붐 레벨 y=0) → 스프레더 헤드.
        // 정적 생성 후 HoistRopeRig가 매 프레임 스프레더 Y에 맞춰 신축(게임 런타임).
        static void BuildHoistRopes(Transform spreaderRoot, Transform spreader)
        {
            float topY = -0.02f;               // 로프 상단 = 트롤리 리빙 시브 바로 아래(시브 외경 하단 -0.019 직하), 트롤리 본체 내부. 트롤리 헤드(상면 -0.0475) 위라 '헤드 아래' 아님
            // 로프 하단 = 헤드블록 데드엔드 소켓 베이스(스프레더-로컬 hbY+0.011≈0.069). 이전 0.05는
            //   소켓(0.069)보다 19mm 아래서 끝나 로프가 소켓에 안 닿고 헤드블록을 관통했음 → 0.07로 봉합.
            float attachOffsetY = 0.07f;       // 스프레더 원점 → 헤드블록 소켓 베이스(로프 하단 정착점)
            float radius = 0.0035f;
            float restBotY = SpreaderRestY + attachOffsetY;

            var ropes = new List<Transform>();
            var topAnchors = new List<Vector2>();
            var botAnchors = new List<Vector2>();
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float x = HoistX + sx * HoistSprX;   // 인양점 항구쪽 이동 + 넓힌 X간격(4가닥 분리)
                float z = sz * HoistSprZ;            // 스프레더 헤드블록 코너에 맞춘 수직 로프(넓힌 Z간격)
                var rope = Rod(spreaderRoot, "Hoist_Rope",
                    new Vector3(x, topY, z), new Vector3(x, restBotY, z), radius, CCable);
                ropes.Add(rope.transform);
                topAnchors.Add(new Vector2(x, z));
                botAnchors.Add(new Vector2(x, z));
            }
            // 매 프레임 트롤리↔스프레더 사이로 로프 신축(상≠하 각진 리빙)
            var rig = spreaderRoot.gameObject.AddComponent<HoistRopeRig>();
            rig.Configure(spreader, topY, attachOffsetY, ropes.ToArray(),
                          topAnchors.ToArray(), botAnchors.ToArray(), radius);
        }

        // 호이스트 윗구간(트롤리 뒷면 → 백리치 고정 앵커) — 동적(BoomRopeRig).
        //   고정 앵커를 백리치 끝(Stay_Anchor 부근 x≈-0.537)에 둠 — 트롤리 backmost(-0.345)보다
        //   더 뒤라, 트롤리가 어디 있든 케이블이 항상 뒤로 향함(기계실 앞면이면 트롤리가 지나쳐 버려 NG).
        //   경로는 트롤리 뒷면(x-0.062)에서 곧장 -X, 붐 밑(y-0.02)으로 주행 → 거더/brace/cross(전부 y0.015 위)·본체 회피.
        //   시브↔뒷면 reeving은 트롤리 내부라 암시(페어리드까지만). z측당 1줄.
        // [견인(주행) 로프 — 문서 레퍼런스/STS/견인로프_reeving_실제구조.html 기반] 하나씩 진행. 1단계 = 바다끝(항구쪽) 리버싱 시브 신설.
        //   붐 팁 Tip_Platform 밑에 현수(gap 0). 육지끝 BackSheave와 대칭. 트롤리 직결 소켓·로프·텐셔너는 다음 단계.
        // 견인로프 — 트롤리↔시브 구간·시브 감김을 TrolleyReevingRig가 트롤리 추종(꺾임 0).
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
                Vector3 drum = new Vector3(MachineryHouseX - 0.02f, 0.085f, sz);   // 드럼 방향(U반전 끝각 결정용)
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

                // 3단계: supply — 시브 → 레인 → 기계실 드럼. y0.055→0.038로 낮춤: 다리 굵힘(LegSec 0.6→1.0)으로
                //   Shoulder_Beam(구 Portal_Cross) 바닥이 boom-local 0.063→0.0464로 내려와 supply(윗면 0.0585)를 관통 →
                //   supply 윗면 0.0415 < 0.0464(5mm 여유)로 통과. 아래로는 트롤리 본체 상단(0, z=±0.05엔 보기 없음)과 38mm 여유. Boom_Cross(0.061) 아래 유지.
                float supplyY = 0.038f;
                float entryX  = MachineryHouseX + MachineryHouseHX;   // 기계실 앞벽(바다쪽) — supply가 바다끝에서 오니 앞으로 진입(뒷벽까지 안 지나감)
                Vector3 Td   = OnSheave(C, degD);                          // 시브 드럼쪽 접점(reeve 끝)
                Vector3 gTip = new Vector3(tipX - 0.05f, supplyY, sz);     // 시브 옆 붐위 진입(가이드 롤러)
                Vector3 mhE  = new Vector3(entryX, supplyY, sz);           // 기계실 진입(붐위 높이)
                Rod(boom, "TowFore_GuideRoller", gTip + new Vector3(0f, 0f, -0.006f), gTip + new Vector3(0f, 0f, 0.006f), 0.005f, CDark);
                PbBox(boom, "TowFore_GuideBracket", new Vector3(gTip.x, (0.066f + gTip.y) * 0.5f, sz),
                    new Vector3(0.005f, 0.066f - gTip.y + 0.003f, 0.005f), CStruct);   // Tip_Platform 밑면(0.064)에 롤러 현수 — gTip.x(2.645)엔 횡재가 없고 플랫폼이 있음(공중부양 해소)
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
                Rod(boom, "Hoist_Drum", new Vector3(MachineryHouseX - 0.065f, 0.085f, sz - 0.028f),
                    new Vector3(MachineryHouseX - 0.065f, 0.085f, sz + 0.028f), 0.014f, CMachine);            // 권상 드럼(기계실 내부)

                // side당 1가닥(총 2) — 윗구간은 드럼 로프라 양정 4-fall과 별개. 트롤리 → 시브 → 기계실 → 드럼
                foreach (float pole in new[] { 0f })
                {
                    float zp = sz + pole;
                    Vector3 C    = new Vector3(BackSheaveX, BackSheaveY, zp);
                    Vector3 Pm   = new Vector3(entryX - 0.012f, entryY, zp);
                    Vector3 drum = new Vector3(MachineryHouseX - 0.02f, 0.085f, zp);

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

            // 갠트리 주행 시 레일 위 장애물(컨테이너 등)과 충돌하도록 다리 하나를 감싸는 BoxCollider 1개.
            //   격자 부재(Leg_Post/Rung/Lace)는 전부 시각용이라 콜라이더가 없다 → 다리를 통째로 물리화.
            //   (크레인 루트의 kinematic Rigidbody가 이 콜라이더로 dynamic 컨테이너를 밀어낸다.)
            //   시각 다리는 베이스 위(y0)에 앉지만, 콜라이더는 지면(0)까지 풀하이트 유지 —
            //   부두 바닥에 놓인 컨테이너를 주행 중 확실히 밀어내는 기능을 보존하기 위함(기능 회귀 0).
            float colY0 = 0f;
            float colH  = y1 - colY0;
            var legCol = new GameObject(Numbered(StsPartNames.LegCollider));
            legCol.transform.SetParent(root, worldPositionStays: false);
            legCol.transform.localPosition = new Vector3(cx, (colY0 + y1) * 0.5f, cz);
            legCol.AddComponent<BoxCollider>().size = new Vector3(foot, colH, foot);
        }

        // 붐 디테일: 보도 그레이팅 + 끝단 시브(도르래) + 작업등
        static void BuildBoomDetails(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            float mid = (x0 + x1) * 0.5f;   // len은 캣워크 분할(BoomSpanBox) 후 활성코드 미사용 → 비활성 페스툰 블록 안에서만 지역 선언

            // 보도 그레이팅 — 거더 위 2장 + 가운데(Boom_Vertical/브레이스 위) 1장으로 상단 전체를 막음(연속 보도, 패널 이음새 유지)
            for (int s = -1; s <= 1; s += 2)
                BoxGappedX(boom, "Walkway_Deck", x0, x1, 0.067f, s * GirderGapZ,
                    0.004f, 0.05f, CMachine, BoomTopWalkwayGapX, BoomTopWalkwayGapHalf);
            // 중앙 보도 숨김(사용자 요청) — 복구하려면 주석 해제
            // Box(boom, "Walkway_Deck_Mid", new Vector3(mid, 0.067f, 0f),
            //     new Vector3(len, 0.004f, 2f * (GirderGapZ - 0.025f)), CMachine);

            // 끝단 시브(도르래)는 아래 팁 플랫폼 뒤에서 트윈 시브 블록으로 한 번에 생성(겹침 제거 재배치).

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

            // 끝단 플랫폼 + 추가 시브 + 항해등
            // 끝단 플랫폼 — 트윈 거더 전폭으로(좁던 것 수정) + 둘레 안전 난간
            Box(boom, "Tip_Platform", new Vector3(x1 - 0.075f, 0.066f, 0f),
                new Vector3(0.15f, 0.004f, 2f * GirderOuterZ), CMachine);   // 육지쪽으로 연장(0.08→0.15) — 견인 가이드 롤러(gTip x≈2.645)를 플랫폼 밑에 받침
            {
                float tpY0 = 0.068f, tpRailY = 0.105f;   // Boom_Railing railTop(0.105)에 높이 맞춤 — 붐난간↔Tip 단차 0.003 제거(옆난간_1·끝난간_3·기둥 전부 정렬)
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
            // 끝단 트윈 시브 블록(도르래) 제거(사용자 지시) — Sheave_Tip/Tip2·Sheave_Cheek·Sheave_Pin/PinCap·Sheave_Hanger(Gusset).
            //   호이스트 로프 리그(트롤리↔스프레더 / 트롤리↔백앵커)는 이 되돌이 시브를 참조하지 않아 로프 영향 없음.
            // 항해등 숨김(사용자 요청) — 복구하려면 주석 해제
            // Ball(boom, "Nav_Light", new Vector3(x1 - 0.003f, 0.05f, 0f),
            //     new Vector3(0.012f, 0.016f, 0.012f), CWarn);

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

#if false   // 페스툰 삭제 — 케이블 1가닥뿐 빈약. 트랙+다발 재설계 예정.
            // 페스툰(전력·제어 케이블) — 붐 하부 트랙 + 늘어진 케이블 다발
            float len = x1 - x0;
            float festZ = GirderOuterZ + 0.008f;
            Box(boom, "Festoon_Track", new Vector3(mid, -0.006f, festZ),
                new Vector3(len, 0.004f, 0.004f), CDark);
            // 페스툰 트랙 행어 브래킷 — 트랙(z≈0.1905)을 옆 캣워크(z≈0.1945)에 고정(2mm 공중부양 제거).
            foreach (float fbx in new[] { x0 + len * 0.25f, mid, x1 - len * 0.25f })
                Box(boom, "Festoon_Bracket", new Vector3(fbx, -0.003f, GirderOuterZ + 0.010f),
                    new Vector3(0.006f, 0.006f, 0.013f), CStruct);
            int loops = 12;
            var festX = new float[loops];
            for (int i = 0; i < loops; i++)
            {
                float fx = Mathf.Lerp(x0 + 0.05f, x1 - 0.05f, i / (float)(loops - 1));
                festX[i] = fx;
                // 페스툰 새들(트랙에 매달린 케이블 행거)
                Box(boom, "Festoon_Saddle", new Vector3(fx, -0.013f, festZ),
                    new Vector3(0.005f, 0.014f, 0.005f), CDark);
            }
            // 늘어진 케이블 루프 — 인접 새들 사이가 아래로 처지는 카테너리(1가닥).
            float festBotY = -0.020f, festSag = 0.012f;
            for (int i = 0; i < loops - 1; i++)
                CableCatenary(boom, "Festoon_Cable",
                    new Vector3(festX[i], festBotY, festZ),
                    new Vector3(festX[i + 1], festBotY, festZ),
                    festSag, 0.0016f, CCable);
#endif

            // 뒷부분(육지측 백리치) 디테일
            // 적층 무게 블록 'Counterweight'와 받침 'CW_Brace' 제거.
            //   STS는 선회·기복 크레인이 아니라 적층 죽은무게 평형추가 없음 — 긴 아웃리치는
            //   A프레임 정점 + 포어/백스테이 텐션 + 기계실 질량 + 백리치 구조로 균형. 이 구조는
            //   아래 Stay_Anchor / BuildApexAndStays 에 이미 존재하므로 평형추는 중복이자 오류였다.
            // 백스테이 이퀄라이저 노드 (빌트업)
            //   단일 박스 → 웹+상·하 플랜지 빌트업 횡빔 + 수직 스티프너 + 거더 접속 거싯
            //   + 양끝 백스테이 클레비스 러그/핀(Backstay_TieBar 끝이 이 러그 사이에 핀으로 물림).
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
                // 백리치 자체 기둥(Back_Rail_Post) 폐지 + 끝변 난간을 붐 보도 난간 첫 기둥(Railing_Post, x0)에 붙임.
                //   끝변 난간을 데크 끝(x0-0.01)이 아니라 붐 끝면 라인 x0(=단부 프레임 Backreach_EndPost 라인)에 두면,
                //   붐 보도 난간 ±Z 첫 기둥 두 개(z=±GirderOuterZ)를 잇는 가로바 = 붐 보도 난간 육지쪽 'ㄷ자' 마감이 되어
                //   붐 기둥이 그대로 지지한다(데크 육지 0.01 돌출부는 단부 프레임 EndPost/EndTie가 막음).
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
                    //   기존 중앙 1장(Z폭 0.016)은 시브 휠(±BackSheaveHZ)을 정통으로 관통했음 →
                    //   치크 바깥(SheaveNest chZ=HZ+0.003)에 여유를 더해 양옆에 두고, 핀(Z로 돌출)을 잡는다.
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
                float bgB = GirderBotLocal, bgT = GirderTopLocal;   // 거더 상·하면 SSOT 추종. 하드코딩 -0.01/0.065는 거더 깊어짐(-0.025)에 안 따라와 단부프레임이 바닥서 0.36m 떴음 → 상수화로 봉합.
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
            // 백리치 경고등 숨김(사용자 요청) — 복구하려면 주석 해제
            // Ball(boom, "Back_Light", new Vector3(x0 + 0.004f, 0.05f, 0f),
            //     new Vector3(0.012f, 0.016f, 0.012f), CWarn);
            // 변압기 + 냉각 유닛 숨김(사용자 요청) — 복구하려면 주석 해제
            // Box(boom, "Transformer", new Vector3(x0 + 0.15f, 0.085f, GirderGapZ),
            //     new Vector3(0.05f, 0.05f, 0.04f), CMachine);
            // Box(boom, "Cooling_Unit", new Vector3(x0 + 0.15f, 0.082f, -GirderGapZ),
            //     new Vector3(0.05f, 0.044f, 0.04f), CDark);

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

        // 붐 측면 트러스 입체화 + 측면 케이블 트레이 (1단계)
        // 트윈 박스 거더가 옆에서 보면 납작한 띠 두 줄 → '장난감 같다'의 핵심 원인.
        //   각 거더 바깥/안쪽 면에 상·하현재(chord)를, 바깥 면엔 워런 빗재(지그재그)를 둘러
        //   깊이감 있는 트러스 거더 실루엣으로. 거더 본체/레일/트롤리 좌표는 건드리지 않는 순수 추가.
        // 폴리 통제: 워런(빗재 위주) + 수직재 격번(2칸마다), 현재 캡은 가는 박스.
        static void BuildBoomTrussDepth(Transform boom)
        {
            float x0 = BoomBackX, x1 = BoomTipX;
            float len = x1 - x0, mid = (x0 + x1) * 0.5f;

            // 거더 윗면만 필요 → 클래스 const 직접 참조(종전 지역 gY/gH는 미사용이라 생략). 값 불변.
            float gTop = GirderTopLocal;   // 거더 윗면 0.065 (보도 그레이팅 밑면과 같은 레벨)
            // 거더 밑면 = GirderCenterY - GirderDepthH*0.5 = -0.01 (gTop만 chTop에 쓰이므로 변수는 생략)

            // 트러스 현재 — 위는 보도 밑면(0.065)에 맞춤. 아래는 주행 트롤리 상단(y=0, Z 반폭 0.185로
            //   거더 바깥 면까지 옴)과 겹치지 않게 그 바로 위(0.003)까지만 내려 깊이 확장.
            float chTop = gTop;            // 상현재 0.065 (gTop 유지)
            float chBot = -0.008f;         // 하현재 — 깊어진 거더 밑면(gBot=-0.01)에 맞춤(도관 -0.012 바로 위)
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

        // 접근 디테일: 다리 수직 사다리 + 포털 상단 점검 플랫폼 + 난간
        static void BuildAccessDetails(Transform root)
        {
            // 다리 오른쪽(star측) 케이지 수직 사다리 — 격자 다리 외곽 밖으로 띄워 겹침 방지
            float legOuter = GaugeZ * 0.5f + LegSec * 1.7f * 0.5f;   // 격자 다리 외곽 z
            float ladderZ  = legOuter + 0.022f;                     // 사다리 위치
            // 붐 데크(붐 로컬 0.067 → 루트 RailH+0.067)까지 올려 기계실 접근 캣워크와 연결
            float ly0 = 0.072f, ly1 = RailH + 0.067f;   // 다리 시각 하단(legFootY 0.072)·Stow_Pin_Housing 윗면(0.07) 위에서 시작. 0.0125로 내렸더니 다리/하우징보다 아래라 Ladder_Bracket 공중부유 + 사다리 하우징 관통 → 다리 발치에 맞춤(사다리는 부두 직접이 아니라 다리·하우징 위에서 출발)
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
            // 후프 간격 실척 ~0.84m(미니어처 0.035)로 촘촘히 — 기존 7개는 실척 4.9m 간격이라 듬성
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


            // 갠트리 페스툰 트랙 숨김(사용자 요청) — 복구하려면 주석 해제
            // float gfZ = -(GaugeZ * 0.5f);
            // Box(root, "Gantry_Festoon_Track",
            //     new Vector3((LandLegX + WaterLegX) * 0.5f, 0.088f, gfZ),
            //     new Vector3(LegSpanX, 0.004f, 0.004f), CDark);

            // 포털 상단 점검 캣워크 — 사용자 요청으로 제거(Access_Platform / Platform_Rail / RailMid / Toe / Post
            // + 받침 브래킷 Platform_Bracket). 복구하려면 아래 /* */ 만 지우면 됨.
            /*
            float apY = RailH - 0.09f;          // 데크 높이
            float apHZ = GaugeZ * 0.5f;         // 다리 위치(±)까지
            float apW = 0.05f;                  // 통로 폭(X)
            Box(root, "Access_Platform", new Vector3(LandLegX, apY, 0f),
                new Vector3(apW, 0.004f, apHZ * 2f), CMachine);
            // 다리에 받침 브래킷(떠 있지 않게) — 양 끝
            for (int s = -1; s <= 1; s += 2)
                Strut(root, "Platform_Bracket",
                    new Vector3(LandLegX, apY - 0.045f, s * apHZ),
                    new Vector3(LandLegX, apY - 0.002f, s * apHZ * 0.55f), 0.006f, CStruct);
            // 난간 — 긴 옆면(±X) 양쪽: 상단+중간 레일 + 토보드 + 기둥
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float rx = LandLegX + sx * apW * 0.5f;
                Box(root, "Platform_Rail", new Vector3(rx, apY + 0.032f, 0f),
                    new Vector3(0.004f, 0.004f, apHZ * 2f), CSafety);
                Box(root, "Platform_RailMid", new Vector3(rx, apY + 0.017f, 0f),
                    new Vector3(0.003f, 0.003f, apHZ * 2f), CSafety);
                Box(root, "Platform_Toe", new Vector3(rx, apY + 0.006f, 0f),
                    new Vector3(0.003f, 0.008f, apHZ * 2f), CSafety);
                int np = 6;
                for (int i = 0; i <= np; i++)
                {
                    float pz = Mathf.Lerp(-apHZ, apHZ, i / (float)np);
                    Box(root, "Platform_Post", new Vector3(rx, apY + 0.017f, pz),
                        new Vector3(0.004f, 0.034f, 0.004f), CSafety);
                }
            }
            */
        }

        // 사다리 정상 ↔ 기계실 접근 캣워크 + 기계실 +Z(사다리쪽) 출입문 (붐 로컬).
        //   붐은 루트에 (0,RailH,0)로 고정 → 루트 사다리의 z는 그대로, y만 RailH 오프셋으로 정합.
        //   캣워크: 사다리(z≈0.377) → 코너(x=LandLegX) → 기계실 문(x=MachineryHouseX), 데크 레벨(붐 로컬 0.067).
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
            // 세그먼트1: 사다리(x=LandLegX) → 기계실 X(x=mhx), z=ladderZ (다리 바깥 우회)
            //   사다리(x=LandLegX)는 데크로 덮지 않는다. 덮으면 사다리 위가 철판에 막힘(기존 +X 오버행 walkW/2가 원인).
            //   데크를 사다리 가장자리에서 끝내 step-off 랜딩으로 만들고, 머리 위는 개구부로 비운다.
            float deckFarX  = mhx - walkW * 0.5f;   // 기계실 코너쪽 끝 — 세그2와 정합 위해 walkW/2 오버행 유지
            float cageOuterX = 0.027f;              // 케이지 측면바 바깥면(side bar x=±0.025, 두께0.004 → 0.027) — 케이지가 사다리(±0.015)보다 넓다.
            float deckNearX = LandLegX - cageOuterX; // 케이지 '바깥면'에서 끝냄 — 데크가 케이지(Cage_Hoop 측면바)를 타고 넘지 않게.
                                                    //   사다리 가장자리(±0.015)로 끝내면 케이지(±0.025)를 0.01 덮어 Cage_Hoop을 침범한다(이번 버그).
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
            // i=0(x=LandLegX, 옛 MHAccess_Post_1)은 사용자 요청으로 제외 — i=1부터 생성.
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
            int rungs = Mathf.Max(2, Mathf.RoundToInt(h / 0.0125f));   // 가로대 간격 실척 0.3m(표준) — 기존 0.04=실척 0.96m라 못 올라감
            for (int i = 0; i <= rungs; i++)
            {
                float ry = Mathf.Lerp(y0, y1, i / (float)rungs);
                Rod(parent, "Ladder_Rung",
                    new Vector3(x - widthX * 0.5f, ry, z),
                    new Vector3(x + widthX * 0.5f, ry, z), 0.0022f, CSafety);
            }
        }

        // primitive 헬퍼

        static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = NewCube(name, parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Colorize(go, color);
            return go;
        }

        // 장비 인클로저 라이브러리 (민짜 큐브 → 실제 형상)
        // 정션박스/캐비닛/공조유닛/파워팩을 '함체+부속'으로 조형하는 재사용 헬퍼.
        // 면 법선에서 면내 축을 산식으로 도출 → 어느 방향으로 두든 부속이 면에 맞물린다.

        static Vector3 AbsV(Vector3 a) => new Vector3(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z));

        // 정션 박스(전기 결선함) — 함체 + 볼트 덮개 + 케이블 글랜드 + 베이스 플랜지.
        //   center=함체 중심, body=함체 치수, coverFace=점검 도어 향하는 면 법선(축정렬 단위),
        //   glandDir=케이블 인입/부착 방향(데크형=Vector3.down, 벽부착형=벽 안쪽).
        static void JunctionBox(Transform parent, string name, Vector3 center, Vector3 body,
                                Vector3 coverFace, Vector3 glandDir, Color bodyC)
        {
            Vector3 fAbs = AbsV(coverFace);
            Vector3 u, v;   // 덮개 면내 두 축
            if (fAbs.x > 0.5f) { u = Vector3.up;    v = Vector3.forward; }
            else if (fAbs.y > 0.5f) { u = Vector3.right; v = Vector3.forward; }
            else { u = Vector3.right; v = Vector3.up; }
            float eF = Vector3.Dot(fAbs, body) * 0.5f;
            float eU = Vector3.Dot(AbsV(u), body) * 0.5f;
            float eV = Vector3.Dot(AbsV(v), body) * 0.5f;

            // 1) 함체
            Box(parent, name + "_Body", center, body, bodyC);

            // 2) 볼트 덮개(면내 86% + 모서리 볼트 4)
            float coverT = Mathf.Max(0.0022f, eF * 0.18f);
            Vector3 coverSize = fAbs * coverT + AbsV(u) * (2f * eU * 0.86f) + AbsV(v) * (2f * eV * 0.86f);
            Box(parent, name + "_Cover", center + coverFace * (eF + coverT * 0.5f), coverSize, CStruct);
            float boltR = Mathf.Max(0.0011f, Mathf.Min(eU, eV) * 0.14f);
            Vector3 boltScale = fAbs * (boltR * 1.4f) + (AbsV(u) + AbsV(v)) * (boltR * 2f);
            Vector3 boltBase = center + coverFace * (eF + coverT);
            for (int su = -1; su <= 1; su += 2)
            for (int sv = -1; sv <= 1; sv += 2)
                Ball(parent, name + "_Bolt",
                    boltBase + u * (su * eU * 0.66f) + v * (sv * eV * 0.66f), boltScale, CDark);

            // 3) 케이블 글랜드 2(너트 + 케이블 스텁)
            Vector3 gAbs = AbsV(glandDir);
            Vector3 gu = (gAbs.y > 0.5f) ? Vector3.right : Vector3.up;
            float eG  = Vector3.Dot(gAbs, body) * 0.5f;
            float eGu = Vector3.Dot(AbsV(gu), body) * 0.5f;
            float glandR = Mathf.Max(0.0012f, eGu * 0.18f);
            float glandLen = Mathf.Max(0.004f, eG * 0.8f);
            Vector3 gNutScale = gAbs * (glandR * 1.2f) + (Vector3.one - gAbs) * (glandR * 2.2f);
            Vector3 gBase = center + glandDir * eG;
            for (int g = -1; g <= 1; g += 2)
            {
                Vector3 gp = gBase + gu * (g * eGu * 0.45f);
                Ball(parent, name + "_GlandNut", gp, gNutScale, CStruct);
                Rod(parent, name + "_Cable", gp, gp + glandDir * glandLen, glandR * 0.7f, CCable);
            }

            // 4) 베이스 플랜지(부착면) + 앵커 볼트 2 — 떠 보이지 않게 접지
            Vector3 bodyPerpG = Vector3.Scale(body, Vector3.one - gAbs);
            Box(parent, name + "_Base", center + glandDir * (eG + 0.0015f),
                gAbs * 0.003f + bodyPerpG * 1.35f, CStruct);
            Vector3 abAxis = (gAbs.y > 0.5f) ? Vector3.forward : Vector3.up;
            float eAb = Vector3.Dot(AbsV(abAxis), body) * 0.5f;
            for (int s = -1; s <= 1; s += 2)
                Ball(parent, name + "_AnchorBolt",
                    center + glandDir * (eG + 0.003f) + abAxis * (s * eAb * 1.2f),
                    gAbs * (boltR * 1.2f) + (Vector3.one - gAbs) * (boltR * 1.8f), CDark);
        }

        // 제어 캐비닛(직립) — 플린스 + 본체 + 더블도어/손잡이 + 측면 루버 + 지붕 오버행 + 상부 공조 + 인양러그.
        //   frontFace=도어 향하는 수평 면 법선(±X/±Z). 높이는 항상 +Y.
        static void ControlCabinet(Transform parent, string name, Vector3 center, Vector3 body,
                                   Vector3 frontFace, Color bodyC)
        {
            Vector3 up = Vector3.up;
            Vector3 fAbs = AbsV(frontFace);
            Vector3 wide = Vector3.Cross(up, frontFace);
            if (wide.sqrMagnitude < 1e-6f) wide = Vector3.right;
            wide = wide.normalized;
            Vector3 wAbs = AbsV(wide);
            float eF = Vector3.Dot(fAbs, body) * 0.5f;
            float eW = Vector3.Dot(wAbs, body) * 0.5f;
            float eH = body.y * 0.5f;
            float topY = center.y + eH, botY = center.y - eH;

            Box(parent, name + "_Plinth", new Vector3(center.x, botY + 0.004f, center.z),
                fAbs * (2f * eF * 0.9f) + wAbs * (2f * eW * 0.96f) + up * 0.008f, CDark);
            Box(parent, name + "_Body", center + up * 0.004f, body, bodyC);
            Box(parent, name + "_Roof", new Vector3(center.x, topY + 0.003f, center.z),
                fAbs * (2f * eF + 0.006f) + wAbs * (2f * eW + 0.006f) + up * 0.005f, CStruct);

            // 더블 도어(중앙 이음매 분할) + 손잡이
            float doorT = 0.0022f;
            Vector3 doorFacePos = center + frontFace * (eF + doorT * 0.5f) + up * 0.004f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 leaf = doorFacePos + wide * (s * eW * 0.49f);
                Box(parent, name + "_Door", leaf,
                    fAbs * doorT + wAbs * (eW * 0.9f) + up * (2f * eH * 0.82f), CStruct);
                Box(parent, name + "_DoorHandle", leaf - wide * (s * eW * 0.42f) + frontFace * doorT,
                    fAbs * 0.004f + wAbs * 0.004f + up * (eH * 0.5f), CDark);
            }
            // 측면(±wide) 가로 루버 3
            for (int s = -1; s <= 1; s += 2)
            for (int l = -1; l <= 1; l++)
                Box(parent, name + "_Louver",
                    center + wide * (s * (eW + 0.0015f)) + up * (l * eH * 0.4f),
                    wAbs * 0.003f + fAbs * (2f * eF * 0.7f) + up * 0.004f, CDark);
            // 지붕 공조 박스 + 인양 러그 2
            Box(parent, name + "_TopAC",
                new Vector3(center.x, topY + 0.012f, center.z) - frontFace * (eF * 0.3f),
                fAbs * (2f * eF * 0.5f) + wAbs * (2f * eW * 0.5f) + up * 0.014f, CMachine);
            for (int s = -1; s <= 1; s += 2)
                Box(parent, name + "_LiftLug",
                    new Vector3(center.x, topY + 0.008f, center.z) + wide * (s * eW * 0.8f),
                    wAbs * 0.004f + fAbs * 0.006f + up * 0.008f, CStruct);
        }

        // 공조/냉각 유닛(상부 팬) — 스키드 + 본체 + 팬가드(링+십자스포크+허브) + 측면 루버 + 전면 파이프/패널 + 인양러그.
        static void EquipmentUnit(Transform parent, string name, Vector3 center, Vector3 body, Color bodyC)
        {
            float ex = body.x * 0.5f, ey = body.y * 0.5f, ez = body.z * 0.5f;
            float topY = center.y + ey, botY = center.y - ey;

            Box(parent, name + "_Skid", new Vector3(center.x, botY + 0.002f, center.z),
                new Vector3(body.x * 1.1f, 0.005f, body.z * 1.1f), CStruct);
            Box(parent, name + "_Body", center, body, bodyC);

            float fanR = Mathf.Min(ex, ez) * 0.82f;
            Vector3 fanCtr = new Vector3(center.x, topY + 0.003f, center.z);
            Rod(parent, name + "_FanRing",
                fanCtr - Vector3.up * 0.0015f, fanCtr + Vector3.up * 0.0015f, fanR, CDark);
            Box(parent, name + "_FanSpoke", fanCtr, new Vector3(2f * fanR, 0.0015f, 0.0025f), CStruct);
            Box(parent, name + "_FanSpoke", fanCtr, new Vector3(0.0025f, 0.0015f, 2f * fanR), CStruct);
            Ball(parent, name + "_FanHub", fanCtr, Vector3.one * (fanR * 0.4f), CMachine);

            for (int s = -1; s <= 1; s += 2)
            for (int l = -1; l <= 1; l++)
                Box(parent, name + "_Louver",
                    new Vector3(center.x, center.y + l * ey * 0.45f, center.z + s * (ez + 0.0015f)),
                    new Vector3(2f * ex * 0.8f, 0.004f, 0.003f), CDark);

            for (int s = -1; s <= 1; s += 2)
                Rod(parent, name + "_Pipe",
                    new Vector3(center.x - ex, center.y + s * ey * 0.35f, center.z + s * ez * 0.3f),
                    new Vector3(center.x - ex - 0.008f, center.y + s * ey * 0.35f, center.z + s * ez * 0.3f),
                    0.0022f, CMachine);
            Box(parent, name + "_AccessPanel", new Vector3(center.x - ex - 0.0011f, center.y, center.z),
                new Vector3(0.0022f, 2f * ey * 0.7f, 2f * ez * 0.7f), CStruct);
            for (int s = -1; s <= 1; s += 2)
                Box(parent, name + "_LiftLug",
                    new Vector3(center.x + s * ex * 0.8f, topY + 0.006f, center.z),
                    new Vector3(0.004f, 0.008f, 0.004f), CStruct);
        }

        // 유압 파워팩 — 오일탱크 + 상부 전동기/펌프(가로 실린더) + 매니폴드/유압포트 + 필러캡 + 유면계 + 스키드.
        //   매니폴드/포트는 중앙 방향(-X)으로 인출하고, 포트 끝 좌표 2개를 반환 → 호스가 그 끝에 물려 허공에 안 뜬다.
        static Vector3[] PowerPack(Transform parent, string name, Vector3 center, Vector3 body, Color bodyC)
        {
            float ex = body.x * 0.5f, ey = body.y * 0.5f, ez = body.z * 0.5f;
            float mY = center.y + ey + 0.006f;

            Box(parent, name + "_Tank", center, body, bodyC);
            Box(parent, name + "_Skid", new Vector3(center.x, center.y - ey + 0.002f, center.z),
                new Vector3(body.x * 1.1f, 0.004f, body.z * 1.1f), CStruct);
            // 전동기(축 Z) + 펌프(끝단 작은 실린더)
            Rod(parent, name + "_Motor",
                new Vector3(center.x, mY, center.z - ez * 0.6f),
                new Vector3(center.x, mY, center.z + ez * 0.6f), Mathf.Min(ex, ey) * 0.45f, CMachine);
            Rod(parent, name + "_Pump",
                new Vector3(center.x, mY, center.z + ez * 0.6f),
                new Vector3(center.x, mY, center.z + ez * 0.92f), Mathf.Min(ex, ey) * 0.28f, CDark);
            // 매니폴드(중앙 방향 -X 끝 상부) + 유압 포트 2(-X로 인출, 끝에 커플링)
            float portY = center.y + ey + 0.003f;
            float manX = center.x - ex * 0.35f;
            Box(parent, name + "_Manifold", new Vector3(manX, portY, center.z),
                new Vector3(ex * 0.5f, 0.01f, ez * 0.7f), CDark);
            var tips = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                int s = i == 0 ? -1 : 1;
                Vector3 a = new Vector3(manX - ex * 0.2f, portY, center.z + s * ez * 0.35f);
                Vector3 b = new Vector3(center.x - ex - 0.01f, portY, center.z + s * ez * 0.35f);
                Rod(parent, name + "_Port", a, b, 0.0018f, CMachine);
                Ball(parent, name + "_PortCoupling", b, Vector3.one * 0.004f, CStruct);
                tips[i] = b;
            }
            // 주유/브리더 캡 — 떠 보이지 않게 탱크 윗면에 '묻은' 짧은 원통 넥 + 윗 캡(구 아님). +X쪽 배치.
            Vector3 fcBase = new Vector3(center.x + ex * 0.4f, center.y + ey - 0.002f, center.z); // 탱크 안에서 시작
            Rod(parent, name + "_FillerNeck", fcBase, fcBase + Vector3.up * 0.008f, 0.0035f, CMachine);
            Rod(parent, name + "_FillerCap", fcBase + Vector3.up * 0.007f, fcBase + Vector3.up * 0.011f, 0.0042f, CStruct);
            Box(parent, name + "_SightGauge", new Vector3(center.x + ex + 0.0011f, center.y, center.z + ez * 0.4f),
                new Vector3(0.0022f, 2f * ey * 0.5f, 0.004f), CGlass);
            return tips;
        }

        // 붐 상부 보도 부재가 포털 빔과 만나는 X 구간 — 그 폭만큼 보도를 끊는다(다리 X마다).
        // Shoulder_Beam(구 Portal_Cross) 플랜지 X 반폭(LegSec*0.55) + 여유. BuildBoomStructure/Details(붐 로컬)에서 공통 사용.
        // 붐 상단 보도/난간을 끊는 통과 구간 — 포털 다리 2곳 + 기계실 1곳(BOOM-1=MH-1 수정).
        //   기계실 구간은 보도/난간이 기계실 벽체를 관통하던 것을 끊고, +Z쪽 기계실 접근 캣워크(BuildMachineryHouseAccess)로 우회.
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

        // a→b 를 잇는 가는 막대(다리/케이블/대각). 부모는 회전·스케일 없는 노드여야 정확.
        static GameObject Strut(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Color color)
        {
            var go = NewCube(name, parent);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(thickness, len, thickness);
            Colorize(go, color);
            return go;
        }

        // 빌트업 브레이스 — 상수 z 평면 위 대각 부재를 웹 플레이트 + 양 가장자리 플랜지 리브(I/채널 단면)로.
        //   민짜 정사각 Strut보다 단면감↑. a→b 는 같은 z 평면(면밖=±Z). LookRotation으로 평면에 평평히 눕혀 롤 고정.
        static void BuiltUpBrace(Transform parent, string name, Vector3 a, Vector3 b, Color color,
                                 float webWidth = 0.024f, float webThick = 0.007f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, d / len);  // 로컬 Y=축, Z=면밖(+Z), X=면내수직
            Vector3 mid = (a + b) * 0.5f;
            var web = NewCube(name, parent);
            web.transform.localPosition = mid;
            web.transform.localRotation = rot;
            web.transform.localScale = new Vector3(webWidth, len, webThick);
            Colorize(web, color);
            for (int s = -1; s <= 1; s += 2)   // 양 가장자리 플랜지 리브(면밖으로 도드라져 I 단면감)
            {
                var fl = NewCube(name + "_Flange", parent);
                fl.transform.localPosition = mid + rot * new Vector3(s * (webWidth * 0.5f - 0.002f), 0f, 0f);
                fl.transform.localRotation = rot;
                fl.transform.localScale = new Vector3(0.004f, len, webThick * 2.2f);
                Colorize(fl, color);
            }
        }

        // a→b 를 잇는 원통(케이블/로프/바퀴 등 둥근 부재). 기본 실린더 높이 2 기준.
        static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Color color)
        {
            var go = NewPrimitive(PrimitiveType.Cylinder, name, parent);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
            Colorize(go, color);
            return go;
        }

        // 투광등 어셈블리 — at=부착점(중심), 렌즈는 -Y로 비춤. w=본체 폭. 수직 치수는 w 비례(소형등도 비례 유지).
        //   브래킷판(부착) → 요크 암(±X로 본체 감싸 받침) → 방열핀 3장(LED 히트싱크 실루엣) → 램프 본체
        //   → 베젤 림(렌즈 프레임) → 평면 발광 렌즈(구 아님, 납작 원반). 전부 수직 적층·맞닿음.
        //   비례계수는 w=0.018에서 기존 승인 치수(판0.004/간격0.005/본체0.011/베젤0.0025/렌즈0.002) 재현.
        static void Floodlight(Transform parent, Vector3 at, float w, Color body, Color lens)
        {
            float plateT = 0.222f * w;   // 브래킷 판 두께
            float gap    = 0.278f * w;   // 요크 간격(방열핀이 사는 공간)
            float hBody  = 0.611f * w;   // 본체 높이
            float bezT   = 0.139f * w;   // 베젤 두께
            float lensT  = 0.111f * w;   // 렌즈 두께
            float armT   = 0.170f * w;   // 요크 암 두께
            float finT   = 0.120f * w;   // 방열핀 두께

            float yPlate   = at.y - plateT * 0.5f;             // 브래킷 중심(top=at.y, 면에 물림)
            float yBodyTop = at.y - plateT - gap;              // 본체 윗면
            float yBody    = yBodyTop - hBody * 0.5f;          // 본체 중심
            float yBodyBot = yBodyTop - hBody;                 // 본체 밑면
            float yBez     = yBodyBot - bezT * 0.5f;           // 베젤 중심
            float yLens    = yBodyBot - bezT - lensT * 0.5f;   // 렌즈 중심(최하단, 조사면)

            // 1) 부착 브래킷 판
            PbBox(parent, "Floodlight_Bracket", new Vector3(at.x, yPlate, at.z),
                new Vector3(w * 0.7f, plateT, w * 0.7f), CStruct);
            // 2) 요크 암(±X) — 판 밑에서 본체 밑까지 본체를 감싸 받친다(안쪽면=본체 가장자리)
            float yArm = ((at.y - plateT) + yBodyBot) * 0.5f;
            float hArm = (at.y - plateT) - yBodyBot;
            for (int a = -1; a <= 1; a += 2)
                PbBox(parent, "Floodlight_Yoke", new Vector3(at.x + a * (w * 0.5f + armT * 0.5f), yArm, at.z),
                    new Vector3(armT, hArm, w * 0.55f), CStruct);
            // 3) 방열핀 3장 — 요크 간격에 X로 나란히(±0.27w±finT/2 < 본체 반폭이라 비간섭)
            for (int f = -1; f <= 1; f++)
                PbBox(parent, "Floodlight_Fin", new Vector3(at.x + f * w * 0.27f, yBodyTop + gap * 0.5f, at.z),
                    new Vector3(finT, gap, w * 0.82f), CMachine);
            // 4) 램프 본체
            PbBox(parent, "Floodlight_Housing", new Vector3(at.x, yBody, at.z),
                new Vector3(w, hBody, w), body);
            // 5) 베젤 림 — 렌즈 둘레 프레임(본체보다 살짝 넓게)
            PbBox(parent, "Floodlight_Bezel", new Vector3(at.x, yBez, at.z),
                new Vector3(w * 1.06f, bezT, w * 1.06f), CMachine);
            // 6) 평면 렌즈(발광) — 조사면 보는 납작 원반(구 금지). Rod 짧은 원통=디스크.
            Rod(parent, "Floodlight_Lens", new Vector3(at.x, yLens + lensT * 0.5f, at.z),
                new Vector3(at.x, yLens - lensT * 0.5f, at.z), w * 0.42f, lens);
        }

        // 방향 지정 투광등 — at에 회전 피벗(빈 오브젝트)을 두고 하향 어셈블리를 aim으로 돌린다.
        //   aim=identity면 하향(-Y). Euler(0,0,90)→렌즈 +X 수평, Euler(0,0,45)→+X 아래 45°(안벽 작업역).
        static void Floodlight(Transform parent, Vector3 at, Quaternion aim, float w, Color body, Color lens)
        {
            var pivot = new GameObject(Numbered("Floodlight_Mount"));
            pivot.transform.SetParent(parent, worldPositionStays: false);
            pivot.transform.localPosition = at;
            pivot.transform.localRotation = aim;
            Floodlight(pivot.transform, Vector3.zero, w, body, lens);
        }

        // 비콘(항공장애등/마커등) — 구 돔 대체. baseCenter=바닥 부착점, 위로 적층.
        //   검은 베이스 드럼 + 컬러 발광 렌즈 드럼(구 아님) + 검은 캡. 전부 맞닿음. 총높이 = 2.5r.
        static void Beacon(Transform parent, string prefix, Vector3 baseCenter, float r, Color lensColor)
        {
            float hBase = r * 0.9f, hLens = r * 1.1f, hCap = r * 0.5f;
            float y0 = baseCenter.y;
            Rod(parent, prefix + "_Base", new Vector3(baseCenter.x, y0, baseCenter.z),
                new Vector3(baseCenter.x, y0 + hBase, baseCenter.z), r, CDark);
            Rod(parent, prefix + "_Lens", new Vector3(baseCenter.x, y0 + hBase, baseCenter.z),
                new Vector3(baseCenter.x, y0 + hBase + hLens, baseCenter.z), r * 0.92f, lensColor);
            Rod(parent, prefix + "_Cap", new Vector3(baseCenter.x, y0 + hBase + hLens, baseCenter.z),
                new Vector3(baseCenter.x, y0 + hBase + hLens + hCap, baseCenter.z), r * 0.62f, CDark);
        }

        // 파이프/도관 90° 곡관 엘보 — 짧은 원통(Rod) segs개로 호를 근사 + 패싯 절점마다 관 굵기 구로 메움.
        //   관 반경 r 일정(부풀지 않음 — 참고사진 토러스 엘보 형태). din=V로 도착하는 단위 진행방향, dout=V에서 나가는 단위 진행방향(직교 가정).
        //   호 중심 O=V+Rb(dout-din), 접점 T1=V-din·Rb / T2=V+dout·Rb (직선 구간을 여기서 트림하면 접선 연속).
        static void PipeElbow(Transform parent, string name, Vector3 V, Vector3 din, Vector3 dout,
                              float r, float Rb, Color color, int segs = 10)
        {
            Vector3 O = V + Rb * (dout - din);
            Vector3 prev = V - din * Rb;   // T1 (직선 구간과의 접점)
            // 모든 절점(T1·T2 및 내부 패싯)에 관 굵기 구로 메움 — 짧은 원통은 접점에서도 호의 접선이 아닌 현(chord)이라
            //   ~(90/segs/2)° 꺾여 틈이 생긴다. 구(지름=관 굵기, 안 부풀음)가 그 킹크/틈을 모두 덮는다.
            Ball(parent, name + "_Node", prev, new Vector3(2f * r, 2f * r, 2f * r), color);  // T1 — 직선↔엘보 이음
            for (int i = 1; i <= segs; i++)
            {
                float ph = (i / (float)segs) * (Mathf.PI * 0.5f);
                Vector3 p = O - Rb * Mathf.Cos(ph) * dout + Rb * Mathf.Sin(ph) * din;
                Rod(parent, name, prev, p, r, color);
                Ball(parent, name + "_Node", p, new Vector3(2f * r, 2f * r, 2f * r), color);  // 모든 절점(T2 포함) 메움 → 이음 틈/킹크 제거
                prev = p;
            }
        }

        // 꺾인 도관 경로 — 정점 배열을 직선 Rod로 잇되, 내부 90° 굽힘마다 PipeElbow로 둥글게.
        //   직선 구간은 양 끝이 내부 굽힘이면 Rb만큼 트림해 엘보 접점과 정확히 만난다.
        static void ConduitPath(Transform parent, string name, Vector3[] pts, float r, float Rb, Color color)
        {
            int n = pts.Length;
            var dir = new Vector3[n - 1];
            for (int i = 0; i < n - 1; i++) dir[i] = (pts[i + 1] - pts[i]).normalized;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                if (i > 0)     a += dir[i] * Rb;   // 시작점이 내부 굽힘 → 접점까지 당김
                if (i < n - 2) b -= dir[i] * Rb;   // 끝점이 내부 굽힘 → 접점까지 당김
                Rod(parent, name, a, b, r, color);
            }
            for (int i = 1; i < n - 1; i++)
                PipeElbow(parent, name + "_Elbow", pts[i], dir[i - 1], dir[i], r, Rb, color);
        }

        // 복선 플랜지 레일 휠 — 트레드(레일 접지 원통) + 양측 플랜지(콘 플레어 림, 레일 이탈방지) + 허브 보스.
        //   center: 휠 중심, axleAxis: 축 방향(보통 Vector3.right). treadR: 트레드 반지름(접지 기준).
        //   매끈한 원통 한 개 → 풀리/스풀 실루엣으로 격상. 외부 모델 없이 절차 생성(둥근 부재 관례=Rod/Cone).
        static void RailWheel(Transform parent, string name, Vector3 center, Vector3 axleAxis,
                              float treadR, float treadHalfW, Color wheelC)
        {
            Vector3 ax = axleAxis.normalized;
            float flangeR = treadR * 1.15f;     // 플랜지 오버행(레일 옆면을 무는 림). 지름 0.0299 < 휠피치 0.032 → 비간섭
            float flangeW = treadR * 0.20f;     // 플랜지 두께
            // 트레드(레일과 구르는 원통면)
            Rod(parent, name, center - ax * treadHalfW, center + ax * treadHalfW, treadR, wheelC);
            // 양측 플랜지 — 트레드(반지름 treadR)에서 림(flangeR)으로 플레어하는 절두콘.
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 inner = center + ax * (s * treadHalfW);                 // 트레드 접합부
                Vector3 outer = center + ax * (s * (treadHalfW + flangeW));     // 림 바깥면
                Cone(parent, name + "_Flange", inner, outer, treadR, flangeR, wheelC, 24);
            }
            // 허브 보스(축 중앙 어두운 보스 — 림보다 안쪽으로 살짝 더 길게 빼 축 느낌)
            Rod(parent, name + "_Hub",
                center - ax * (treadHalfW * 1.25f), center + ax * (treadHalfW * 1.25f),
                treadR * 0.42f, CDark);
        }

        // 구체(램프·돔·풍속계 컵 등 둥근 부재). scale로 눌러 돔/타원도 표현.
        static GameObject Ball(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = NewPrimitive(PrimitiveType.Sphere, name, parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Colorize(go, color);
            return go;
        }

        // 원뿔/절두원뿔 — Unity 기본 프리미티브에 없어 메시를 절차 생성.
        // a=밑면 중심, b=꼭대기 중심. rBottom=밑면 반지름, rTop=윗면 반지름(0이면 뾰족한 콘).
        static GameObject Cone(Transform parent, string name, Vector3 a, Vector3 b,
                               float rBottom, float rTop, Color color, int seg = 24)
        {
            var go = new GameObject(Numbered(name));
            go.transform.SetParent(parent, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildFrustumMesh(rBottom, rTop, seg);   // 단위 높이(y=0→1), 반지름은 메시에 반영

            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = a;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(1f, len, 1f);     // 높이만 늘림(반지름 유지)
            mr.sharedMaterial = GetMaterial(color);
            return go;
        }

        // 단위 높이(y=0→1) 절두원뿔 메시 — 옆면 + 위/아래 캡.
        static Mesh BuildFrustumMesh(float rBottom, float rTop, int seg)
        {
            seg = Mathf.Max(8, seg);
            var verts = new List<Vector3>(seg * 2 + 2);
            var tris  = new List<int>(seg * 12);
            for (int i = 0; i < seg; i++)
            {
                float ang = (i / (float)seg) * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                verts.Add(new Vector3(cx * rBottom, 0f, cz * rBottom));  // 밑면 i → 2i
                verts.Add(new Vector3(cx * rTop,    1f, cz * rTop));     // 윗면 i → 2i+1
            }
            for (int i = 0; i < seg; i++)
            {
                int b0 = 2 * i, t0 = 2 * i + 1;
                int j = (i + 1) % seg;
                int b1 = 2 * j, t1 = 2 * j + 1;
                tris.Add(b0); tris.Add(t0); tris.Add(b1);   // 옆면(바깥 향함)
                tris.Add(b1); tris.Add(t0); tris.Add(t1);
            }
            int cBot = verts.Count; verts.Add(new Vector3(0f, 0f, 0f));
            int cTop = verts.Count; verts.Add(new Vector3(0f, 1f, 0f));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                tris.Add(cBot); tris.Add(2 * i);     tris.Add(2 * j);       // 아래 캡(-Y)
                tris.Add(cTop); tris.Add(2 * j + 1); tris.Add(2 * i + 1);   // 위 캡(+Y)
            }
            var m = new Mesh { name = "STS_Cone" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // 홈 파인 시브(도르래) 휠 — 두 플랜지 + 중앙 V홈(로프 시트) + 허브 보어. 회전체(lathe).
        //   a/b = 양 축단 중심(축 방향), rOuter=플랜지 외경, rHub=허브 보어 반경, grooveDepth=홈 깊이.
        //   민짜 원기둥(Rod)과 달리 폭을 좁히고 림에 홈을 파 '드럼통'이 아닌 도르래 실루엣을 만든다.
        static GameObject Sheave(Transform parent, string name, Vector3 a, Vector3 b,
                                 float rOuter, float rHub, float grooveDepth, Color color, int seg = 24)
        {
            var go = new GameObject(Numbered(name));
            go.transform.SetParent(parent, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildSheaveMesh(rOuter, rHub, grooveDepth, seg);
            Vector3 dir = b - a;
            float len = dir.magnitude;
            go.transform.localPosition = a;
            if (len > 1e-5f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            go.transform.localScale = new Vector3(1f, len, 1f);   // 폭(축 길이)만 늘림, 반경은 메시에 반영
            mr.sharedMaterial = GetMaterial(color);
            return go;
        }

        // 단위 폭(y=0→1) 홈 파인 시브 메시 — 닫힌 단면(좌면→림 플랜지+V홈→우면→보어)을 Y축으로 회전.
        static Mesh BuildSheaveMesh(float rOuter, float rHub, float grooveDepth, int seg)
        {
            seg = Mathf.Max(12, seg);
            float R  = rOuter;
            float rh = Mathf.Clamp(rHub, 0.0003f, rOuter * 0.6f);
            float rg = Mathf.Max(rh + 0.0005f, rOuter - grooveDepth);   // 홈 바닥(로프 시트) 반경
            // 단면 닫힌 루프 (축 t=0→1, 반경 r): 좌 허브→좌 플랜지(얇은 립)→V홈 바닥→우 플랜지 립→우 허브, 보어로 닫힘.
            //   플랜지를 얇은 립(0.15)으로, 홈을 넓게(중앙 70%) → '아령'이 아닌 납작한 도르래 림.
            (float t, float r)[] prof = {
                (0f, rh), (0f, R), (0.15f, R), (0.5f, rg), (0.85f, R), (1f, R), (1f, rh),
            };
            int n = prof.Length;
            var verts = new List<Vector3>(n * seg);
            var tris  = new List<int>(n * seg * 6);
            for (int p = 0; p < n; p++)
                for (int i = 0; i < seg; i++)
                {
                    float ang = (i / (float)seg) * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(ang) * prof[p].r, prof[p].t, Mathf.Sin(ang) * prof[p].r));
                }
            for (int p = 0; p < n; p++)
            {
                int pa = p * seg, pb = ((p + 1) % n) * seg;   // 마지막 p6→p0 = 내부 보어(닫힘)
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    int a0 = pa + i, a1 = pa + j, b0 = pb + i, b1 = pb + j;
                    tris.Add(a0); tris.Add(b0); tris.Add(a1);   // BuildFrustumMesh와 동일 와인딩(바깥 노멀)
                    tris.Add(a1); tris.Add(b0); tris.Add(b1);
                }
            }
            var m = new Mesh { name = "STS_Sheave" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // 트러스 절점 연결판(거싯)

        // 트러스 절점에 면과 평평하게 붙는 모서리 챔퍼 8각 판(용접 거싯 표현).
        // faceRot: 판 로컬 +Z를 면 바깥 노멀로 향하게 하는 회전(+Z면=identity, -Z면=Euler(0,180,0),
        //          +X면=Euler(0,90,0), -X면=Euler(0,-90,0)). 8각이라 직사각 박스보다 곡면 느낌.
        static GameObject Gusset(Transform parent, Vector3 localPos, Quaternion faceRot,
                                 float size, float thick, Color color)
        {
            string key = $"g{size:F4}_{thick:F4}";
            if (_plateCache == null) _plateCache = new Dictionary<string, Mesh>();
            if (!_plateCache.TryGetValue(key, out var mesh))
            {
                float s = size, c = size * 0.22f;   // c=모서리 챔퍼 폭(과하면 알약처럼 보임)
                var poly = new[]
                {
                    new Vector2(-s + c, -s), new Vector2(s - c, -s),
                    new Vector2(s, -s + c),  new Vector2(s,  s - c),
                    new Vector2(s - c,  s),  new Vector2(-s + c,  s),
                    new Vector2(-s,  s - c), new Vector2(-s, -s + c),
                };
                mesh = BuildPlateMesh(poly, thick);
                _plateCache[key] = mesh;
            }
            string gnm = Numbered("Truss_Gusset");
            if (_deletedParts.Contains(gnm)) return null;   // 삭제 지정 제외
            var go = new GameObject(gnm);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = faceRot;
            Colorize(go, color);
            return go;
        }

        // 평면 볼록 다각형(로컬 XY 점들)을 thickness(로컬 Z)로 압출한 판재 — 앞/뒤 캡 + 옆면.
        // 플랫 셰이딩: 캡·옆면이 정점을 공유하지 않게 면마다 별도 정점을 둬 모서리가 날카롭게(강철판 느낌).
        static Mesh BuildPlateMesh(Vector2[] poly, float thickness)
        {
            int n = poly.Length;
            // CCW 보정 — 앞면(+Z) 캡 노멀이 바깥(+Z)을 향하게.
            float area = 0f;
            for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            var p = poly;
            if (area < 0f) { p = new Vector2[n]; for (int i = 0; i < n; i++) p[i] = poly[n - 1 - i]; }

            float hz = thickness * 0.5f;
            var v = new List<Vector3>();
            var t = new List<int>();

            // 앞 캡(+Z) — 자체 정점
            int f0 = v.Count;
            for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, hz));
            for (int i = 1; i < n - 1; i++) { t.Add(f0); t.Add(f0 + i); t.Add(f0 + i + 1); }
            // 뒤 캡(-Z) — 자체 정점(역와인딩)
            int b0 = v.Count;
            for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, -hz));
            for (int i = 1; i < n - 1; i++) { t.Add(b0); t.Add(b0 + i + 1); t.Add(b0 + i); }
            // 옆면 — 변마다 4정점(공유 안 함)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int q = v.Count;
                v.Add(new Vector3(p[i].x, p[i].y,  hz));   // fi
                v.Add(new Vector3(p[i].x, p[i].y, -hz));   // bi
                v.Add(new Vector3(p[j].x, p[j].y,  hz));   // fj
                v.Add(new Vector3(p[j].x, p[j].y, -hz));   // bj
                t.Add(q); t.Add(q + 1); t.Add(q + 2);
                t.Add(q + 2); t.Add(q + 1); t.Add(q + 3);
            }
            var m = new Mesh { name = "STS_Plate" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // 접합부 볼트 (2단계)

        // 볼트 패턴 — 원형 링(앵커/플랜지 볼트 서클)
        // 볼트 패턴 — 격자(스플라이스 플레이트 볼트 그리드)
        static Vector2[] BoltGrid(int nx, int ny, float dx, float dy)
        {
            var list = new List<Vector2>(nx * ny);
            for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
                list.Add(new Vector2((ix - (nx - 1) * 0.5f) * dx, (iy - (ny - 1) * 0.5f) * dy));
            return list.ToArray();
        }

        // 여러 볼트 헤드를 담은 공유 메시 — 접합부당 GameObject 1개로 N개 볼트 표현.
        // 평면(로컬 z=0)에서 +Z로 돌출. faceRot로 면 노멀에 맞춰 배치.
        static Mesh GetBoltMesh(string key, Vector2[] pos, float boltR, float boltH, int seg = 6)
        {
            if (_boltCache == null) _boltCache = new Dictionary<string, Mesh>();
            if (_boltCache.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var p in pos) AddBoltPrism(v, t, p, boltR, boltH, seg);
            m = new Mesh { name = "STS_Bolts" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            _boltCache[key] = m;
            return m;
        }

        // 단일 고력볼트 헤드 — 면(z=0)에서 +Z로 h 돌출. 둥근 받침 와셔 + 육각 머리(곧은 벽 + 상단 모따기 + 윗 육각면).
        // 실제 ISO 육각볼트의 시그니처(머리 모서리 챔퍼 + 와셔)를 살려 납작한 프리즘 느낌을 제거. 아랫면은 면에 묻혀 생략.
        static void AddBoltPrism(List<Vector3> v, List<int> t, Vector2 c, float r, float h, int seg)
        {
            // 머리 높이 h 기준 비례(전체 돌출 = h 유지)
            float washerH  = h * 0.2f;                          // 받침 와셔 두께
            float wallTopZ = washerH + (h - washerH) * 0.55f;   // 육각 머리 곧은 벽 상단
            float topZ     = h;                                 // 모따기 끝 = 윗 육각면
            float topR     = r * 0.82f;                         // 모따기 후 윗 육각면 외접반경
            float washerR  = r * 1.12f;                         // 와셔 반경 — 머리 대각폭(r)보다 살짝만 크게(고리처럼 안 보이게)

            // 1) 둥근 받침 와셔(매끈) — 옆면 + 윗 환형 캡
            AddRoundCollar(v, t, c, washerR, 0f, washerH, 12);
            // 2) 육각 머리 곧은 벽(평면 패싯)
            AddHexBand(v, t, c, r, r, washerH, wallTopZ, seg);
            // 3) 상단 모따기 밴드(벽 → 좁아진 윗면)
            AddHexBand(v, t, c, r, topR, wallTopZ, topZ, seg);
            // 4) 윗 육각 캡
            AddHexCap(v, t, c, topR, topZ, seg);
        }

        // 육각 단면 코너 좌표(밴드/캡 정렬용). 원래 프리즘과 동일하게 코너를 step/2만큼 회전.
        static Vector3 HexCorner(Vector2 c, float r, float ang, float z)
            => new Vector3(c.x + Mathf.Cos(ang) * r, c.y + Mathf.Sin(ang) * r, z);

        // 육각 밴드(하단링 rLow@zLow → 상단링 rHigh@zHigh) — 면마다 독립 정점으로 평면 패싯(각진 육각 유지).
        static void AddHexBand(List<Vector3> v, List<int> t, Vector2 c,
                               float rLow, float rHigh, float zLow, float zHigh, int seg)
        {
            float step = Mathf.PI * 2f / seg;
            for (int f = 0; f < seg; f++)
            {
                float a0 = f * step + step * 0.5f, a1 = (f + 1) * step + step * 0.5f;
                int b = v.Count;
                v.Add(HexCorner(c, rLow,  a0, zLow));    // b  : low A
                v.Add(HexCorner(c, rLow,  a1, zLow));    // b+1: low B
                v.Add(HexCorner(c, rHigh, a1, zHigh));   // b+2: high B
                v.Add(HexCorner(c, rHigh, a0, zHigh));   // b+3: high A
                t.Add(b); t.Add(b + 1); t.Add(b + 2);    // 바깥(반경) 노멀
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }

        // 윗 육각 캡(중심 팬, +Z 향함)
        static void AddHexCap(List<Vector3> v, List<int> t, Vector2 c, float r, float z, int seg)
        {
            float step = Mathf.PI * 2f / seg;
            int cc = v.Count; v.Add(new Vector3(c.x, c.y, z));   // 중심
            int rb = v.Count;
            for (int i = 0; i < seg; i++) v.Add(HexCorner(c, r, i * step + step * 0.5f, z));
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                t.Add(cc); t.Add(rb + i); t.Add(rb + j);
            }
        }

        // 둥근 와셔 칼라 — 매끈한 옆면(정점 공유 → RecalculateNormals로 원통 음영) + 윗 환형 디스크(육각 머리 밑면 덮음).
        static void AddRoundCollar(List<Vector3> v, List<int> t, Vector2 c, float r, float zLo, float zHi, int seg)
        {
            int b = v.Count;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float cx = Mathf.Cos(a) * r, cy = Mathf.Sin(a) * r;
                v.Add(new Vector3(c.x + cx, c.y + cy, zLo));   // b+2i  : 밑
                v.Add(new Vector3(c.x + cx, c.y + cy, zHi));   // b+2i+1: 위
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                int b0 = b + 2 * i, t0 = b + 2 * i + 1, b1 = b + 2 * j, t1 = b + 2 * j + 1;
                t.Add(b0); t.Add(b1); t.Add(t1);   // 옆면(바깥 노멀)
                t.Add(b0); t.Add(t1); t.Add(t0);
            }
            int cap = v.Count; v.Add(new Vector3(c.x, c.y, zHi));   // 윗 디스크 중심(+Z)
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                t.Add(cap); t.Add(b + 2 * i + 1); t.Add(b + 2 * j + 1);
            }
        }

        // 볼트 패턴 메시를 면(faceRot)에 부착 — 다크 강철. plateOffset만큼 면 바깥으로 띄움.
        static void Bolts(Transform parent, Vector3 pos, Quaternion faceRot, string key,
                          Vector2[] patt, float boltR, float plateOffset)
        {
            var mesh = GetBoltMesh(key, patt, boltR, boltR * 0.85f, 6);
            string bnm = Numbered("Joint_Bolts");
            if (_deletedParts.Contains(bnm)) return;        // 삭제 지정 제외
            var go = new GameObject(bnm);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = pos + faceRot * new Vector3(0f, 0f, plateOffset);
            go.transform.localRotation = faceRot;
            Colorize(go, CDark);
        }

        // 연결 플레이트(거싯) + 볼트 사각 격자 한 세트 — 면 노멀(faceRot)에 정렬.
        //   실제 구조 접합은 원형 링이 아니라 nx×ny 사각 그리드(행×열) — 그에 맞춰 격자로 박는다.
        static void BoltedPlate(Transform parent, Vector3 pos, Quaternion faceRot,
                                float size, int nx, int ny, float dx, float dy, float boltR)
        {
            float thick = 0.005f;
            Gusset(parent, pos, faceRot, size, thick, CStruct);
            Bolts(parent, pos, faceRot, $"grid{nx}x{ny}_{dx:F4}_{dy:F4}_{boltR:F4}",
                  BoltGrid(nx, ny, dx, dy), boltR, thick * 0.5f + 0.0004f);
        }

        // 주요 구조 접합부 연결판 + 볼트 — 다리 상·하단, 포털, 실빔/브레이스 절점(루트 로컬).
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

                // 1) 다리 상단(포털 크로스/A프레임/거더 수렴) — 볼트 링 연결판.
                //    판 폭(2*0.02=0.04)을 다리 외곽폭(LegSec*1.7=0.0425) 안으로 유지 → 가장자리 삐져나옴 방지.
                BoltedPlate(root, new Vector3(x, LegTopY - 0.02f, zOuter),
                    fr, 0.02f, 3, 4, 0.010f, 0.0085f, 0.0019f);   // 다리상단 주절점 3×4=12

                // 2) 실빔(Sill_Beam) ↔ 다리 절점(RailH*0.4)
                BoltedPlate(root, new Vector3(x, RailH * 0.4f, zOuter),
                    fr, 0.02f, 3, 3, 0.011f, 0.011f, 0.0019f);    // 실빔 절점 3×3=9

                // 3) 측면 대각 브레이스 상단 절점(RailH*0.92) — 두 X-브레이스가 각 다리 상단에서 만남
                BoltedPlate(root, new Vector3(x, RailH * 0.92f, zOuter),
                    fr, 0.017f, 3, 2, 0.009f, 0.008f, 0.0018f);   // 브레이스 절점 3×2=6

                // 4) 다리 베이스 받침판 앵커 볼트 그리드(위로 향함)
                Bolts(root, new Vector3(x, 0.073f, s * halfZ), Quaternion.Euler(-90, 0, 0),
                    "grid3x2", BoltGrid(3, 2, 0.016f, 0.012f), 0.0022f, 0.0004f);
            }
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
                // 덮개판 폭을 플랜지 바깥 가장자리(±GirderOuterZ)까지 넓힘(기존 ±GirderGapZ는 플랜지 안쪽만 덮음).
                Box(boom, "Girder_FlangeSplice", new Vector3(sx, gTop + 0.004f, 0f),
                    new Vector3(0.016f, 0.004f, 2f * GirderOuterZ), CStruct);
                // 볼트열이 두 거더 사이 갭(±0.1)에 떨어져 플랜지(|z|≈0.16)를 한 줄도 안 물었음
                //   → 거더별 2x2 클러스터를 z=±GirderGapZ 플랜지 위에 배치.
                for (int gs = -1; gs <= 1; gs += 2)
                    Bolts(boom, new Vector3(sx, gTop + 0.008f, gs * GirderGapZ), Quaternion.Euler(-90, 0, 0),
                        "splice_flange", BoltGrid(2, 2, 0.007f, 0.018f), 0.0015f, 0.0004f);
            }
        }

        // 안 깎은 단위 큐브를 공유 — 모든 Box/Strut가 같은 메시 사용(모서리 챔퍼 제거, 사용자 요청)
        static GameObject NewCube(string name, Transform parent)
        {
            var go = new GameObject(Numbered(name));
            go.AddComponent<MeshFilter>().sharedMesh = GetUnitCube();
            go.AddComponent<MeshRenderer>();
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        // 안 깎은 단위 큐브 메시(모서리 챔퍼 제거 — 사용자 요청). 면 방향은 outward로 자동 보정, 면당 UV 0~1 유지.
        static Mesh GetUnitCube()
        {
            if (_unitCube != null) return _unitCube;
            const float h = 0.5f;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };

            // 6 면(각 면 ±h 전체 — 날카로운 직각 모서리)
            for (int a = 0; a < 3; a++)
            for (int s = -1; s <= 1; s += 2)
            {
                int a1 = (a + 1) % 3, a2 = (a + 2) % 3;
                AddBevQuad(v, t, uv,
                    AxV(a, s * h, a1, -h, a2, -h), AxV(a, s * h, a1, h, a2, -h),
                    AxV(a, s * h, a1, h, a2, h),   AxV(a, s * h, a1, -h, a2, h), ax[a] * s);
            }

            var m = new Mesh { name = "STS_UnitCube" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return _unitCube = m;
        }

        // 축 인덱스로 Vector3 구성
        static Vector3 AxV(int a, float va, int a1, float va1, int a2, float va2)
        { var p = Vector3.zero; p[a] = va; p[a1] = va1; p[a2] = va2; return p; }

        // 사각/삼각 추가 — outward 기준으로 와인딩 자동 보정(뒤집힘 방지) + UV 0~1
        static void AddBevQuad(List<Vector3> v, List<int> t, List<Vector2> uv,
                               Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            else
            { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        }

        // 늘어진 케이블(카테너리) — Unity Splines로 a→b 사이에 sag만큼 처지는 곡선 튜브 메시를 만들어 붙인다.
        //   knot을 포물선 근사(중앙 최대 처짐)로 깔고 SplineMesh.Extrude로 생성 시점에 메시를 굽는다(런타임 컴포넌트 불필요).
        //   a/b/sag는 parent 로컬 좌표·길이. 호이스트 로프(팽팽=직선)엔 안 쓰고, 처지는 전력/제어 케이블에만.
        static GameObject CableCatenary(Transform parent, string name, Vector3 a, Vector3 b,
                                        float sag, float radius, Color color,
                                        int knots = 5, int sides = 5, int segments = 18)
        {
            var spline = new UnityEngine.Splines.Spline();
            for (int i = 0; i < knots; i++)
            {
                float t = i / (float)(knots - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= 4f * sag * t * (1f - t);   // 포물선 처짐(중앙 최대) — 카테너리 근사
                spline.Add(new Unity.Mathematics.float3(p.x, p.y, p.z),
                           UnityEngine.Splines.TangentMode.AutoSmooth);
            }
            var mesh = new Mesh { name = name + "_Mesh" };
            UnityEngine.Splines.SplineMesh.Extrude(spline, mesh, radius, sides, segments);

            var go = new GameObject(Numbered(name));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color);
            return go;
        }

        // 분절(세그먼트) 케이블 — 카테너리 포물선을 따라 짧은 원통 토막을 이어 붙임. 스플라인 튜브(CableCatenary)가
        //   '딱딱'해 보일 때, 토막 이음새가 미세하게 꺾여 실제 와이어로프처럼 늘어져 보이게 한다. 토막↑ = 매끈.
        static void CableSegmented(Transform parent, string name, Vector3 a, Vector3 b,
                                   float sag, float radius, Color color, int segCount = 14)
        {
            segCount = Mathf.Max(2, segCount);
            Vector3 prev = a;
            for (int i = 1; i <= segCount; i++)
            {
                float t = i / (float)segCount;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= 4f * sag * t * (1f - t);   // 포물선 처짐(중앙 최대) — 카테너리 근사
                Rod(parent, name, prev, p, radius, color);
                prev = p;
            }
        }

        // 시브 홈을 감는 분절 로프 — 중심 center, 시트 반경 rseat의 원(X-Y 평면)을 따라 startDeg에서 sweepDeg만큼
        //   짧은 원통 토막으로 잇는다. 양 끝각은 두 로프 암(트롤리/기계실)의 접선점과 맞물려 연속. 감김각이 커질수록
        //   '도르래에 둘러 감긴' 무거운 리브로 보인다(0°=+X, 90°=+Y 위; sweep 음수=시계방향).
        static void SheaveReeve(Transform parent, string name, Vector3 center, float rseat,
                                float startDeg, float sweepDeg, float ropeR, Color color, int segCount = 22)
        {
            segCount = Mathf.Max(3, segCount);
            Vector3 Pt(float deg)
            {
                float r = deg * Mathf.Deg2Rad;
                return center + new Vector3(Mathf.Cos(r) * rseat, Mathf.Sin(r) * rseat, 0f);
            }
            // [도관 표준] 곡선을 짧은 원통으로 그리면 절점마다 원통 끝이 호의 현이라 꺾여 틈/킹크가 생긴다.
            //   전 절점(시작 포함)에 로프 굵기(지름=2·ropeR) 구를 넣어 메움 → 부풀지 않고 매끈한 곡선.
            Vector3 prev = Pt(startDeg);
            Ball(parent, name + "_Node", prev, new Vector3(2f * ropeR, 2f * ropeR, 2f * ropeR), color);  // 시작 절점(인접 로프 이음부) 메움
            for (int i = 1; i <= segCount; i++)
            {
                Vector3 p = Pt(startDeg + sweepDeg * (i / (float)segCount));
                Rod(parent, name, prev, p, ropeR, color);
                Ball(parent, name + "_Node", p, new Vector3(2f * ropeR, 2f * ropeR, 2f * ropeR), color);  // 절점 메움(틈/킹크 제거)
                prev = p;
            }
        }

        // 시브를 감는 호이스트 로프 — 뒤(-X, 기계실 윗구간)에서 와서 시브 위를 넘어 앞·아래(스프레더)로 강하.
        //   양 끝을 시브 치크판(±0.016) 밖으로 빼서 앞(아래)·뒤 둘 다 보이게. Splines 곡선 튜브.
        static void SheaveWrap(Transform parent, Vector3 center, float sheaveR, float ropeRadius, Color color)
        {
            // 시브를 크라운(정상)으로 감고 '양 끝이 모두 아래로' 강하하는 리빙:
            //   −X측 접선 = 뒤·아래로 → 백 페어리드(기계실 윗구간), +X측 접선 = 곧장 아래 → 스프레더(Hoist_Rope).
            //   스프레더 강하를 더 길게(지배적) 둬 로프가 '바닥/스프레더'를 향하게(기계실 쪽으로 눕지 않게).
            var spline = new UnityEngine.Splines.Spline();
            Vector3[] pts = {
                center + new Vector3(-sheaveR,        -0.028f,         0f),  // −X측 접선, 뒤·아래 → 백 페어리드(기계실 윗구간)
                center + new Vector3(-sheaveR * 0.9f,  sheaveR * 0.4f, 0f),  // −X 상부
                center + new Vector3( 0.000f,          sheaveR,        0f),  // 시브 정상(크라운, 홈)
                center + new Vector3( sheaveR * 0.9f,  sheaveR * 0.4f, 0f),  // +X 상부
                center + new Vector3( sheaveR,        -0.044f,         0f),  // +X측 접선, 곧장 아래 → 스프레더(지배적 강하)
            };
            foreach (var p in pts)
                spline.Add(new Unity.Mathematics.float3(p.x, p.y, p.z),
                           UnityEngine.Splines.TangentMode.AutoSmooth);
            var mesh = new Mesh { name = "Sheave_Rope_Mesh" };
            // Extrude(spline, mesh, radius, sides, segments): sides=단면 각수.
            //   기존 5 = 5각 각기둥이라 평평한 면이 옆을 향해 '기울어진 리본'처럼 보였음 → 12각으로 둥근 로프.
            UnityEngine.Splines.SplineMesh.Extrude(spline, mesh, ropeRadius, 12, 16);
            var go = new GameObject(Numbered("Sheave_Rope"));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color);
        }

        // ProBuilder 편집형 박스 — CreatePrimitive 대신 ProBuilder 메시로 생성. 디자이너가 ProBuilderize 없이
        //   바로 ProBuilder(베벨·면 분할)·Polybrush(스컬프팅)로 다듬을 수 있다. (디자인 워크플로용 — 신규/재설계 부품에 사용)
        //   euler 주면 회전. 콜라이더는 시각 전용으로 제거(기존 부품과 통일).
        static GameObject PbBox(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, Vector3 euler = default, float bevel = 0f)
        {
            var pb = UnityEngine.ProBuilder.ShapeGenerator.GenerateCube(UnityEngine.ProBuilder.PivotLocation.Center, size);
            pb.name = Numbered(name);
            pb.transform.SetParent(parent, worldPositionStays: false);
            pb.transform.localPosition = localPos;
            if (euler != Vector3.zero) pb.transform.localRotation = Quaternion.Euler(euler);
            // 베벨 비활성 — 전 모서리 일괄 베벨은 blind로 자기교차해 메시가 산산조각 남(Trolley_Gearbox 등 파편).
            //   깨끗한 ProBuilder 박스로 생성만 하고, 베벨/면다듬기는 디자이너가 ProBuilder 에디터에서 시각적으로 한다.
            //   (bevel 인자는 호출부 호환 위해 유지하되 무시. 안전한 자동 베벨이 필요하면 부품별 소량으로 재도입.)
            _ = bevel;
            pb.ToMesh();
            pb.Refresh();
            var mr = pb.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = GetMaterial(color);
            var col = pb.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            return pb.gameObject;
        }

        // 파츠 번호 — 같은 이름마다 _1,_2…를 붙여 하이어라키에서 인스턴스를 구분(작업 지정 편의). 생성마다 _nameSeq 리셋.
        static readonly Dictionary<string, int> _nameSeq = new Dictionary<string, int>();
        // 삭제 지정 인스턴스 — 절차 생성이 결정적이라 이름(번호)이 고정됨. 여기 적힌 이름만 생성 제외(번호는 소비돼 이후 번호 안 밀림).
        //   하이어라키에서 지우고 싶은 객체 이름을 추가하면 재생성해도 안 나옴.
        static readonly HashSet<string> _deletedParts = new HashSet<string>
        {
            "Truss_Gusset_129", "Truss_Gusset_130", "Joint_Bolts_1", "Joint_Bolts_2",
            "MH_DoorKick_1", "MHAccess_Bracket_2", "Ladder_Bracket_5",
            "Boom_Cross_4",   // 권상 로프(HoistU_ToSheave) 통로의 바닥 횡재 — 로프 관통 제거
        };

        // 삭제 지정 일괄 제거 — Gusset/Joint_Bolts 외 모든 파츠(Box/Strut/Cone/PbBox…)에 대응.
        //   생성은 정상 진행돼 Numbered 번호가 소비되므로 이후 인스턴스 번호가 밀리지 않고, 생성 후 이름으로 제거한다.
        static void PruneDeletedParts(Transform root)
        {
            if (_deletedParts.Count == 0) return;
            var doomed = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && _deletedParts.Contains(t.name))
                    doomed.Add(t.gameObject);
            foreach (var go in doomed)
                if (go != null) Object.DestroyImmediate(go);
        }
        static string Numbered(string n)
        {
            int c = _nameSeq.TryGetValue(n, out var v) ? v + 1 : 1;
            _nameSeq[n] = c;
            return n + "_" + c;
        }

        static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = Numbered(name);
            // 에디터 클릭 방해 줄이려 collider 제거(시각화 전용).
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        static void Colorize(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial = GetMaterial(c);
        }

        // 같은 색 머티리얼 재사용 — 프로젝트 머티리얼 오염 방지 위해 인스턴스(에셋 미저장).
        // 단색 평면 → 절차 강철 텍스처(albedo) + 카테고리별 PBR + 일부 에미시브.
        static Material GetMaterial(Color c)
        {
            if (_matCache != null && _matCache.TryGetValue(c, out var cached)) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "STS_Mat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);

            // 카테고리별 PBR + 텍스처 적용 여부
            float metallic = 0.38f, smooth = 0.45f;   // 구조 강철 기본 — 광택 약간↑(매트 플라스틱 '토이' 느낌 완화)
            bool  useTex = true, emissive = false, useRopeTex = false;
            float emi = 0f;
            // [디테일 페인팅] 카테고리 → PBR_Library 폴더 + 타일링. 라이브러리 없으면 ApplyPbr이 false → 절차 강철 폴백.
            string pbr = "PaintedMetal001"; Vector2 pbrTile = new Vector2(2f, 2f);   // 기본: 도장 구조강

            if (Same(c, CGlass))       { metallic = 0.0f;  smooth = 0.92f; useTex = false; pbr = null; }  // 유리: 매끈
            else if (Same(c, CCable))  { metallic = 0.80f; smooth = 0.34f; useTex = false; useRopeTex = true; pbr = null; } // 와이어 로프: 강철 광택 + 나선 strand
            else if (Same(c, CLight))  { metallic = 0.0f;  smooth = 0.60f; useTex = false; pbr = null; emissive = true; emi = 1.8f; } // 작업등 렌즈: 발광
            else if (Same(c, CWarn))   { metallic = 0.0f;  smooth = 0.55f; pbr = null; emissive = true; emi = 1.1f; } // 경고/항공등: 약발광
            else if (Same(c, CDark))   { metallic = 0.20f; smooth = 0.25f; pbr = "Metal032";        pbrTile = new Vector2(3f, 3f); } // 다크 머신 강철(트위스트락/허브)
            else if (Same(c, CRail))   { metallic = 0.65f; smooth = 0.55f; pbr = "Metal055A";       pbrTile = new Vector2(6f, 2f); } // 마모된 레일: 베어 메탈
            else if (Same(c, CMachine)){ metallic = 0.35f; smooth = 0.30f; pbr = "CorrugatedSteel002"; pbrTile = new Vector2(3f, 3f); } // 기계실: 골강판 외벽
            else if (Same(c, CTrolley) || Same(c, CSpread)) { pbr = null; }  // 안전색(주황/노랑): 채도 보존 위해 그레이스케일 절차강에 클린 틴트
            // 그 외(CStruct/CBoom): 도장 구조강(PaintedMetal001)

            if (useTex)
            {
                // 색(알베도)은 항상 그레이스케일 절차강 × 틴트 → 의도색(회색) 정확 유지. 표면 굴곡만 PBR 노멀/AO로 보강.
                //   ※ 프레임/구조 강철 회색은 절대 라이브러리 Color로 바꾸지 말 것(사용자 지시) — 노멀/AO만 덧댄다.
                Vector2 tile = (pbr != null) ? pbrTile : Vector2.one;
                var tex = GetSteelTexture();
                if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", tile); }
                if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", tile); }
                if (pbr != null) ApplyPbrDetail(mat, pbr, tile);   // 노멀+AO만(색 불변)
            }
            else if (useRopeTex)
            {
                var rt = GetRopeTexture();
                var tile = new Vector2(1f, 14f);   // 둘레 1회(6 strand) × 길이방향 촘촘히 → 나선 결
                if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", rt); mat.SetTextureScale("_BaseMap", tile); }
                if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", rt); mat.SetTextureScale("_MainTex", tile); }
            }
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * emi);
            }

            _matCache?.Add(c, mat);
            return mat;
        }

        // 색 근사 비교(머티리얼 카테고리 분류용)
        static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        // PBR_Library 폴더에서 [albedo, normal, ao] 텍스처를 로드(없으면 null). AmbientCG 명명규칙: <폴더>_1K-PNG_<맵>.png
        static Texture2D[] GetPbrSet(string folder)
        {
            if (_pbrCache != null && _pbrCache.TryGetValue(folder, out var cached)) return cached;
            const string root = "Assets/PBR_Library/";
            Texture2D Load(string suffix) =>
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}{folder}/{folder}_1K-PNG_{suffix}.png");
            var set = new[] { Load("Color"), Load("NormalGL"), Load("AmbientOcclusion") };
            _pbrCache?.Add(folder, set);
            return set;
        }

        // PBR 라이브러리의 노멀+AO만 입혀 표면 디테일을 더함(색은 건드리지 않음).
        //   알베도는 그레이스케일 절차강×_BaseColor 틴트가 담당 → 의도한 색이 탁해지지 않음.
        //   AO는 강도를 낮춰(0.5) 과하게 어두워지지 않게 한다.
        static void ApplyPbrDetail(Material mat, string folder, Vector2 tile)
        {
            var set = GetPbrSet(folder);
            if (set == null) return;
            if (set[1] != null && mat.HasProperty("_BumpMap"))          // 노멀(굴곡 디테일)
            {
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_BumpMap", set[1]);
                mat.SetTextureScale("_BumpMap", tile);
            }
            if (set[2] != null && mat.HasProperty("_OcclusionMap"))     // AO(약하게)
            {
                mat.SetTexture("_OcclusionMap", set[2]);
                mat.SetTextureScale("_OcclusionMap", tile);
                if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", 0.5f);
            }
        }

        // 절차 생성 강철 디테일 텍스처 — 그레이스케일(평균≈0.9), 다중 옥타브 노이즈 + 세로 때 스트릭 + 미세 그레인.
        // _BaseColor가 곱해져 각 색의 "칠한 강철" 질감이 됨. PBR 라이브러리 폴백 + 안전색(주황/노랑) 전용. 에셋 미저장.
        static Texture2D GetSteelTexture()
        {
            if (_steelTex != null) return _steelTex;

            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "STS_SteelDetail", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 다중 옥타브 노이즈(얼룩/때)
                float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.55f
                        + Mathf.PerlinNoise(x * 0.13f + 100f, y * 0.13f) * 0.30f
                        + Mathf.PerlinNoise(x * 0.40f + 50f,  y * 0.40f) * 0.15f;
                // 세로로 흘러내린 때 스트릭(상단일수록 옅게)
                float streak = Mathf.PerlinNoise(x * 0.6f, y * 0.015f);
                streak = Mathf.SmoothStep(0.62f, 1f, streak) * (y / (float)N) * 0.18f;
                // 미세 그레인
                float grain = (Hash(x, y) - 0.5f) * 0.05f;

                float v = Mathf.Clamp01(0.93f + (n - 0.5f) * 0.22f + grain - streak);
                byte b = (byte)(v * 255f);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _steelTex = tex;
        }

        // 와이어 로프 텍스처 — 헬리컬 6-strand 대각 밴드 + strand 내 미세 와이어 + 그레인.
        //   그레이스케일(평균≈0.8)이라 _BaseColor(CCable 강철 회색)에 곱해져 꼬인 로프 결을 만든다.
        static Texture2D GetRopeTexture()
        {
            if (_ropeTex != null) return _ropeTex;
            const int N = 128;
            const float TAU = Mathf.PI * 2f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "STS_RopeStrand", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                // 외측 6 strand 나선 — u(둘레) 6회, v(길이) 따라 감김 → 대각 밴드
                float strand = Mathf.Cos((6f * u + 3f * v) * TAU);      // 능선(+)=밝게, 골(-)=어둡게
                float shade  = 0.80f + 0.20f * strand;
                // strand 골 음영 강조(둥근 단면 느낌)
                shade -= Mathf.SmoothStep(0.0f, 1.0f, -strand) * 0.12f;
                // strand 안 미세 와이어(촘촘한 결)
                float wire = Mathf.Cos((36f * u + 18f * v) * TAU);
                shade *= 0.95f + 0.05f * wire;
                float grain = (Hash(x, y) - 0.5f) * 0.05f;
                float val = Mathf.Clamp01(shade + grain);
                byte b = (byte)(val * 255f);
                px[y * N + x] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return _ropeTex = tex;
        }

        // 결정적 정수 해시 → [0,1) (텍스처 그레인용)
        static float Hash(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
        }

        static Vector3 FindContainerAnchor()
        {
            // 1) 실제 컨테이너 인스턴스 우선(가장 정확)
            var instType = System.Type.GetType("ContainerProject.ContainerInstance, Assembly-CSharp");
            if (instType != null)
            {
                var inst = Object.FindFirstObjectByType(instType) as Component;
                if (inst != null) return inst.transform.position;
            }
            // 2) 스포너(보통 매니저 — 원점일 수 있음)
            var spawnerType = System.Type.GetType("ContainerProject.ContainerSpawner, Assembly-CSharp");
            if (spawnerType != null)
            {
                var spawner = Object.FindFirstObjectByType(spawnerType) as Component;
                if (spawner != null) return spawner.transform.position;
            }
            // 3) 이름으로
            var named = GameObject.Find("Container_Procedural");
            if (named != null) return named.transform.position;
            return Vector3.zero;
        }

        /// <summary>씬에 이미 있는 Quay_Ground의 '육지측' QuayRail 월드 X를 반환.
        /// ★ 크레인 루트 X = 육지측 레일(LandLegX=0)이다 — 중심이 아님. 바다측 다리는 +X(WaterLegX=+LegSpanX).
        ///   따라서 크레인 루트를 '육지측 QuayRail'(= 물측이 +X이므로 두 레일 중 작은 X)에 놓으면
        ///   Rail_Land가 육지 QuayRail에, Rail_Water(root+LegSpanX)가 바다 QuayRail에 정확히 포개진다.
        /// 부두(또는 QuayRail)가 없으면 false → 호출부는 컨테이너 앵커 기준 유지.</summary>
        static bool TryFindQuayRailLandX(out float landX)
        {
            landX = 0f;
            var quay = GameObject.Find(StsPartNames.QuayGround);
            if (quay == null) return false;
            bool found = false;
            foreach (var t in quay.GetComponentsInChildren<Transform>(true))
                if (t.name == StsPartNames.QuayRail)
                {
                    if (!found || t.position.x < landX) landX = t.position.x;   // 육지측 = 물측(+X) 반대 = 최소 X
                    found = true;
                }
            return found;
        }

        /// <summary>붐호이스트 리빙 — 정점 시브(고정)↔붐 브라이들(러핑) 로프를 BoomHoistRig로 동적 연결.
        /// 붐 기립 시 브라이들이 회전해 올라가 로프가 신축(붐을 세우는 로프계 시각화). 브라이들 마스트는 luffPivot 하위(추종),
        /// 정점 접점 마커는 root 하위(고정). z=±거더간격 2줄(트윈 거더·2 시브 대응).</summary>
        static void BuildBoomHoistReeving(Transform root, Transform boom, Transform luffPivot)
        {
            float apexY   = RailH + ApexH;
            float sheaveY = apexY - 0.042f;                      // 정점 붐호이스트 시브 Y(Apex_BoomHoistSheave와 동일 SSOT)
            // [물리 산출] 브라이들 X = 힌지서 바다측 f=0.72 지점. 로프 모멘트암 d(u)=26.5u/√(u²+24.5²) 최대화 —
            //   팁일수록 유리하나 트롤리 간섭 회피로 3/4 지점. 중간(0.5) 대비 장력 ~18%↓(1.0→0.82MN). [[feedback_always_verify_mathematically]]
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

                // 붐 브라이들(러핑 추종) — 로드1개 → 삼각 A형 프레임: 발 2개(전후 splay) + 헤드 + 발 타이 + 헤드 피팅.
                //   붐호이스트 로프가 헤드를 위로 당기면 A다리 2개가 붐 윗면 두 발로 하중 전달(단일 스틱은 굽힘에 취약·가짜).
                //   pivot-local = 붐-로컬 − 피벗원점(WaterLegX, GirderCenterY). 헤드=로프 픽업점.
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

                // 급전 로프는 '기존' 붐호이스트 윈치(Boom_Hoist_Drum, 기계실 내부 wx=mhx−0.02, wy=0.095)에서 뽑는다.
                // 정점 시브 →(기계실 지붕 위 디플렉터 시브에서 수직으로 꺾어)→ 드럼 강하.
                //   종전 단일 직선은 지붕 윗면(root RailH+0.171≈2.004)을 X≈−0.51에서 관통해 드럼에 꽂혀 마감 불량. → 드럼 바로 위 지붕에 디플렉터 시브+로프 포트(트럼펫)를
                //   두어, 지붕을 '구멍'으로 통과(마감) + 대각선을 수직으로 꺾어 드럼에 강하. 꺾임은 정점시브·디플렉터(둘 다 시브)에서만. [[feedback_rope_no_midspan_kink]]
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

        /// <summary>붐 힌지 관절 — 각 거더 butt joint(x=WaterLegX)에 클레비스(고정 2판)+텅(러핑 1판)+핀(Z축).
        /// 실물 STS 붐 힌지처럼 러그 사이를 핀이 관통해 붐이 그 핀을 축으로 기립한다. 고정 클레비스·핀은 boom(육지측),
        /// 텅은 luffPivot(바다측·회전)에 둔다. 스윕 이후에 호출(직접 부모 지정 → 중복 편입 없음).</summary>
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

        /// <summary>붐 종방향 '전장(BoomBackX~BoomTipX)' 박스를 힌지(WaterLegX)에서 육지(name)+바다(name_Luff) 2조각으로
        /// 분할 생성. 두 조각 union = 원래 단일 박스와 기하 동일(0°에선 형상 불변). 바다측(_Luff)은 러핑 스윕이 편입.
        /// 전장 종방향 부재(현재·트레이·페스툰·데크 등)를 러핑시킬 때 이 헬퍼로 통일한다.</summary>
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
            var rends = c.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return false;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            localMinX = b.min.x - boom.position.x;
            return true;
        }
    }
}
#endif
