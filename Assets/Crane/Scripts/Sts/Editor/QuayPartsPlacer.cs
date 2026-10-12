#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using AIXRCrane.Ship;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>항구 부재 FBX 배치 — 블렌더에서 만든 부재를 씬에 깐다. 메시는 C#에서 만들지 않고 '어디에 몇 개'만 정한다.
    /// 좌표 규약: 안벽 SSOT가 없어 안벽 가장자리=월드 원점 X0, 안벽 방향=Z로 깐다(부두 FBX 도입 시 실측으로 교체).</summary>
    public static partial class QuayPartsPlacer
    {
        const string CurbFbx    = "Assets/Crane/Models/Quay_Curb.fbx";
        const string BollardFbx = "Assets/Crane/Models/Quay_Bollard.fbx";
        const string RailFbx    = "Assets/Crane/Models/Quay_Rail.fbx";
        const string CaissonFbx = "Assets/Crane/Models/Quay_Caisson.fbx";
        const string SeaFbx     = "Assets/Crane/Models/Sea.fbx";
        const string YardPaveFbx  = "Assets/Crane/Models/Yard_Pavement.fbx";
        const string YardBlockFbx = "Assets/Crane/Models/Yard_Block.fbx";
        const string LaneFbx      = "Assets/Crane/Models/Quay_Lane.fbx";

        // 부재 실척 높이 — 블렌더 빌드 스크립트와 쌍으로 유지한다(문서/스크립트/부두연석_유닛_빌드.py).
        const float CurbHeightM    = 0.528f;   // 단면 0.72W × 0.528H
        const float BollardHeightM = 1.368f;   // 기둥 1.08 + 갓 0.288
        const float RailHeightM    = 0.192f;   // DIN 536 A120 170 + 소플레이트 22
                                               //   = StsConfig.RailSectionH(0.008u) × 24. SSOT 일치

        // ═══ 항구 치수 SSOT — PortConfig가 설계선 LOA에서 유도한다. 여기에 숫자를 박지 말 것. ═══
        static float BerthLenM => PortConfig.BerthLengthMeters;   // 294 × 1.15 → 340m

        // 부재 배치 간격 — 부재 자체 규격에서 나온 값이라 항구 사이즈와 무관하게 유지.
        const float CurbPitchM     = 4.0f;    // 프리캐스트 유닛 피치(유닛 3.985 + 줄눈 0.015) = FBX 규격
        const float CurbWidthM     = 0.72f;   // 연석 단면 폭 = FBX 규격. 해측면을 안벽 가장자리에 맞추는 데 쓴다
        const float BollardGapM    = 20f;     // 계선주 간격 — 미정이면 오너 값으로 교체
        const float BollardInsetM  = 1.08f;   // 안벽 가장자리 → 육지쪽 계선주 중심 — 미정이면 교체
        const float RailPitchM     = 12.0f;   // 레일 정척 12m + 신축이음 10mm = FBX 규격
        // ═══ 색 — URP/Lit 머티리얼 에셋을 만들어 FBX에 리맵한다 ═══
        //   FBX 내장 머티리얼(Blender→Unity 자동변환)은 URP에서 색이 그대로 안 와 .mat을 명시 생성해 고정한다.
        const string MatDir = "Assets/Crane/Materials/Port";

        // 이름은 Blender 빌드 스크립트가 만든 머티리얼 이름과 정확히 일치해야 리맵이 걸린다.
        //   smooth = 1 − roughness.
        static readonly (string n, float r, float g, float b, float metal, float smooth)[] Mats =
        {
            ("Quay_Caisson",      0.56f, 0.55f, 0.52f, 0.00f, 0.10f),   // 해수 얼룩 콘크리트
            ("Quay_DeckAsphalt",  0.16f, 0.16f, 0.17f, 0.00f, 0.06f),   // 에이프런 아스팔트(매트)
            ("Curb_Concrete",     0.70f, 0.69f, 0.66f, 0.00f, 0.15f),   // 프리캐스트 연석(밝게 — 가장자리 인지)
            ("Bollard_CastSteel", 0.13f, 0.14f, 0.15f, 0.60f, 0.35f),   // 계선주 주강(차콜)
            ("Rail_Steel",        0.34f, 0.34f, 0.36f, 1.00f, 0.55f),   // 압연강 레일
            ("Sea_Water",         0.045f,0.115f,0.145f,0.00f, 0.92f),   // 항내 해수 — 잔잔해 반사 높게
            ("Sea_Bed",           0.05f, 0.07f, 0.08f, 0.00f, 0.05f),   // 해저·측면(거의 안 보임)
            ("Yard_Asphalt",      0.19f, 0.19f, 0.20f, 0.00f, 0.08f),   // 야드 포장 — 에이프런보다 살짝 밝게 구분
            ("Yard_Fill",         0.48f, 0.46f, 0.43f, 0.00f, 0.08f),   // 야드 성토 측면
            ("Yard_Paint",        0.85f, 0.68f, 0.08f, 0.00f, 0.30f),   // 블록 도색(황색)
            ("Lane_Paint",        0.88f, 0.74f, 0.10f, 0.00f, 0.25f),   // 안전 차선(안전 노랑 — 블록보다 밝게)
            ("StartMarker_Red",   0.80f, 0.12f, 0.10f, 0.00f, 0.65f),   // 체스말(임시) — 회색 부두에서 튀게.
        };

        /// <summary>FBX 별로 리맵할 머티리얼 — 그 FBX 에 없는 이름을 리맵하면 .meta 만 지저분해진다.</summary>
        static readonly Dictionary<string, string[]> FbxMats = new()
        {
            ["Assets/Crane/Models/Quay_Caisson.fbx"]  = new[] { "Quay_Caisson", "Quay_DeckAsphalt" },
            ["Assets/Crane/Models/Quay_Curb.fbx"]     = new[] { "Curb_Concrete" },
            ["Assets/Crane/Models/Quay_Bollard.fbx"]  = new[] { "Bollard_CastSteel" },
            ["Assets/Crane/Models/Quay_Rail.fbx"]     = new[] { "Rail_Steel" },
            ["Assets/Crane/Models/Sea.fbx"]           = new[] { "Sea_Bed", "Sea_Water" },
            ["Assets/Crane/Models/Yard_Pavement.fbx"] = new[] { "Yard_Fill", "Yard_Asphalt" },
            ["Assets/Crane/Models/Yard_Block.fbx"]    = new[] { "Yard_Paint" },
            ["Assets/Crane/Models/Quay_Lane.fbx"]     = new[] { "Lane_Paint" },
            [PawnFbx]                                 = new[] { "StartMarker_Red" },
        };

        const float YardMarkThickM = 0.015f;  // 블록 도색 두께 = FBX 규격. 실측 스케일 기준값
        const float LaneThickM     = 0.015f;  // 차선 도색 두께 = FBX 규격
        const float LanePitchM     = 12.0f;   // 차선 유닛 길이 = 레일 피치 — 총길이가 레일과 같아야 갠트리 한계가 밖으로 안 나간다
        const float CaissonPitchM  = 20.0f;   // 케이슨 1함 20m + 줄눈 30mm = FBX 규격. 340/20 = 17함

        [MenuItem("Model/FBX/항구/연석 배치 (Quay_Curb)", false, 1)]
        static void PlaceCurb()
        {
            var fbx = Load(CurbFbx, "연석"); if (fbx == null) return;

            float pitch = CurbPitchM * StsConfig.ModelScale;
            // 내림 — 올림하면 마지막 유닛이 안벽 끝을 반피치 넘어 바다로 튀어나온다. 나눗셈은 반드시 실척 m끼리
            //   (모델 단위로 나누면 부동소수 오차로 유닛 1개가 조용히 사라질 수 있다).
            int   units = Mathf.FloorToInt(BerthLenM / CurbPitchM);
            float run   = pitch * units;
            float scale = FbxScaleByHeight(fbx, CurbHeightM);

            // X0 = 안벽 가장자리. 중심을 X0 에 두면 폭의 절반이 바다로 튀어나오므로 반폭만큼 육지쪽(−X).
            float x = -CurbWidthM * 0.5f * StsConfig.ModelScale;

            var root = NewRoot("Quay_Curb");
            for (int i = 0; i < units; i++)
                Place(fbx, root, "Quay_CurbUnit",
                      new Vector3(x, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"연석 {units}유닛 · 피치 {CurbPitchM:F1}m · 총 {run * StsConfig.InvModelScale:F1}m " +
                       $"(안벽 {BerthLenM:F0}m) · scale {scale:F4}");
        }

        [MenuItem("Model/FBX/항구/계선주 배치 (Quay_Bollard)", false, 2)]
        static void PlaceBollard()
        {
            var fbx = Load(BollardFbx, "계선주"); if (fbx == null) return;

            // 구간 '중앙'에 놓는다 — 끝점 배치(z=±안벽/2)는 계선주 반지름만큼 허공으로 나간다.
            //   중앙 배치는 양끝에 반피치 여유가 생겨 원기둥이 데크 위에 올라온다(연석·레일·케이슨과 동일 식).
            float pitch = BollardGapM * StsConfig.ModelScale;
            int   n     = Mathf.FloorToInt(BerthLenM / BollardGapM);
            float run   = pitch * n;
            float scale = FbxScaleByHeight(fbx, BollardHeightM);
            float x     = -BollardInsetM * StsConfig.ModelScale;       // 육지쪽(−X)

            var root = NewRoot("Quay_Bollard");
            for (int i = 0; i < n; i++)
                Place(fbx, root, "Quay_Bollard",
                      new Vector3(x, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"계선주 {n}개 · 간격 {BollardGapM:F0}m · 끝여유 {BollardGapM * 0.5f:F0}m " +
                       $"(안벽 {BerthLenM:F0}m) · 안쪽 {BollardInsetM:F2}m · scale {scale:F4}");
        }

        /// <summary>안벽 케이슨 — 항구의 본체. 데크 윗면이 y=0(크레인 접지·컨테이너 착지면)에 오도록 안벽고만큼 내려 놓는다.
        /// 바다 +X·육지 −X·안벽 가장자리 X0 규약이라 케이슨은 가장자리에서 육지쪽으로 에이프런 폭만큼 뻗는다.</summary>
        [MenuItem("Model/FBX/항구/안벽 배치 (Quay_Caisson)", false, 0)]
        static void PlaceCaisson()
        {
            var fbx = Load(CaissonFbx, "안벽"); if (fbx == null) return;

            float pitch = CaissonPitchM * StsConfig.ModelScale;
            int   units = Mathf.FloorToInt(BerthLenM / CaissonPitchM);   // 나눗셈은 실척 m 끼리
            float run   = pitch * units;
            float wallH = PortConfig.QuayWallHeightMeters;
            float scale = FbxScaleByHeight(fbx, wallH);
            float x     = -PortConfig.ApronWidthMeters * 0.5f * StsConfig.ModelScale;  // 가장자리 X0 → 육지쪽
            float y     = -wallH * StsConfig.ModelScale;                               // 데크 윗면을 y=0 으로

            var root = NewRoot("Quay_Caisson");
            for (int i = 0; i < units; i++)
                Place(fbx, root, "Quay_CaissonUnit",
                      new Vector3(x, y, -run * 0.5f + pitch * (i + 0.5f)), scale);

            // 걷는 면·컨테이너 착지면 — 부두 전체를 감싸는 BoxCollider 1개. FBX는 addColliders:0라 콜라이더가 안 생겨
            //   없으면 플레이어가 부두를 뚫고 떨어진다. 유닛마다 안 달고 직육면체 하나로 덮는다(케이슨이 실제 직육면체라 오차 0).
            var col = Undo.AddComponent<BoxCollider>(root.gameObject);
            col.size   = new Vector3(PortConfig.ApronWidthMeters, wallH, BerthLenM) * StsConfig.ModelScale;
            col.center = new Vector3(x, y * 0.5f, 0f);

            Done(root, $"케이슨 {units}함 · 피치 {CaissonPitchM:F0}m · 총 {run * StsConfig.InvModelScale:F1}m · " +
                       $"에이프런 {PortConfig.ApronWidthMeters:F0}m · 안벽고 {wallH:F0}m" +
                       $"(코핑 {StsConfig.QuayDeckAboveSeaMeters:F0} + 수심 {PortConfig.WaterDepthMeters:F0}) · scale {scale:F4}");
        }

        /// <summary>주행 레일 2줄 — 게이지는 StsConfig.LegGaugeXMeters(18m, Post-Panamax 표준), 안벽 가장자리로부터의
        /// 거리는 PortConfig.ApronSeawardM SSOT. 원점 대칭으로 깔면 바다 +X 규약 때문에 해측 레일이 물 위로 나간다.</summary>
        [MenuItem("Model/FBX/항구/레일 배치 (Quay_Rail)", false, 3)]
        static void PlaceRail()
        {
            var fbx = Load(RailFbx, "레일"); if (fbx == null) return;

            float pitch = RailPitchM * StsConfig.ModelScale;
            int   units = Mathf.FloorToInt(BerthLenM / RailPitchM);   // 나눗셈은 실척 m 끼리
            float run   = pitch * units;
            float scale = FbxScaleByHeight(fbx, RailHeightM);
            // X0 = 안벽 가장자리, 바다 +X. 해측 레일은 가장자리에서 육지쪽으로 ApronSeawardM,
            //   육측 레일은 거기서 게이지만큼 더 육지쪽. 둘 다 −X 다.
            float water = -PortConfig.ApronSeawardM * StsConfig.ModelScale;
            float land  = water - StsConfig.LegGaugeXMeters * StsConfig.ModelScale;

            var root = NewRoot(StsPartNames.QuayRail);
            foreach (float x in new[] { water, land })
                for (int i = 0; i < units; i++)
                    Place(fbx, root, StsPartNames.QuayRail,
                          new Vector3(x, 0f, -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"레일 2줄 × {units}유닛 · 게이지 {StsConfig.LegGaugeXMeters:F0}m · " +
                       $"해측 −{PortConfig.ApronSeawardM:F0}m/육측 −{PortConfig.ApronSeawardM + StsConfig.LegGaugeXMeters:F0}m · " +
                       $"피치 {RailPitchM:F0}m · 총 {run * StsConfig.InvModelScale:F1}m · scale {scale:F4}");
        }

        /// <summary>에이프런 안전 차선 — 레일 양옆 ±LaneOffsetM에 4줄. 이름을 "Lane"으로 두는 게 핵심 —
        /// GantryRangeFit이 이 이름의 렌더러 Z 바운즈를 갠트리 주행 한계로 쓴다(장식이 아니라 SSOT). 레일 사이는 비움.</summary>
        [MenuItem("Model/FBX/항구/차선 배치 (Lane)", false, 6)]
        static void PlaceLane()
        {
            var fbx = Load(LaneFbx, "차선"); if (fbx == null) return;

            float pitch = LanePitchM * StsConfig.ModelScale;
            int   units = Mathf.FloorToInt(BerthLenM / LanePitchM);   // 레일과 같은 28
            float run   = pitch * units;
            float scale = FbxScaleByHeight(fbx, LaneThickM);
            float off   = PortConfig.LaneOffsetM * StsConfig.ModelScale;
            float water = -PortConfig.ApronSeawardM * StsConfig.ModelScale;
            float land  = water - StsConfig.LegGaugeXMeters * StsConfig.ModelScale;

            var root = NewRoot("Quay_Lane");
            foreach (float railX in new[] { water, land })
                foreach (float sign in new[] { -1f, 1f })
                    for (int i = 0; i < units; i++)
                        Place(fbx, root, "Lane",
                              new Vector3(railX + sign * off, 0f,
                                          -run * 0.5f + pitch * (i + 0.5f)), scale);

            Done(root, $"안전 차선 4줄 × {units}유닛 · 폭 {PortConfig.LaneWidthM:F2}m · " +
                       $"레일 중심 ±{PortConfig.LaneOffsetM:F2}m · 총 {run * StsConfig.InvModelScale:F1}m" +
                       $"(레일과 동일) · 레일 사이는 비움 · scale {scale:F4}");
        }

        /// <summary>야드 — 포장 1장 + 블록 마킹. 포장은 케이슨과 정확히 맞댄다(겹치면 Z-fighting).
        /// ★ 블록 이름은 반드시 YardBlock_Zone — RtgCraneFbxPlacer가 이 bounds로 RTG 위치·갠트리 범위를 잡는 SSOT.</summary>
        [MenuItem("Model/FBX/항구/야드 배치 (Yard)", false, 5)]
        static void PlaceYard()
        {
            var pave  = Load(YardPaveFbx,  "야드 포장");  if (pave  == null) return;
            var block = Load(YardBlockFbx, "야드 블록");  if (block == null) return;

            float wallH = PortConfig.QuayWallHeightMeters;
            float depth = PortConfig.YardDepthM;
            float px    = -(PortConfig.ApronWidthMeters + depth * 0.5f) * StsConfig.ModelScale;
            float py    = -wallH * StsConfig.ModelScale;

            // ① 포장 — 케이슨과 같은 두께라 항구가 하나의 land mass 로 읽힌다.
            var pRoot = NewRoot("Yard_Pavement");
            Place(pave, pRoot, "Yard_Pavement", new Vector3(px, py, 0f),
                  FbxScaleByHeight(pave, wallH));

            // 걷는 면 — FBX 는 addColliders:0 이라 여기서 달아야 한다(케이슨과 같은 이유).
            var col = Undo.AddComponent<BoxCollider>(pRoot.gameObject);
            col.size   = new Vector3(depth, wallH, BerthLenM) * StsConfig.ModelScale;
            col.center = new Vector3(px, py * 0.5f, 0f);

            // ② 블록 마킹 — 레인 × 블록. 도색이라 콜라이더 없음.
            var bRoot = NewRoot("Yard_Blocks");
            float bScale = FbxScaleByHeight(block, YardMarkThickM);
            int n = 0;
            for (int i = PortConfig.YardLaneStart; i < PortConfig.YardLanes; i++)
                for (int j = 0; j < PortConfig.YardBlocksPerLane; j++, n++)
                    Place(block, bRoot, StsPartNames.YardBlockZone,
                          new Vector3(PortConfig.YardBlockCenterX(i) * StsConfig.ModelScale, 0f,
                                      PortConfig.YardBlockCenterZ(j) * StsConfig.ModelScale), bScale);

            Selection.activeGameObject = bRoot.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[항구] 야드 — 포장 {depth:F1} × {BerthLenM:F0}m · 블록 {n}개" +
                      $"({PortConfig.YardActiveLanes}/{PortConfig.YardLanes}레인 × {PortConfig.YardBlocksPerLane}, 육지쪽부터) " +
                      $"각 {PortConfig.YardBlockWidthM:F2}m({PortConfig.YardRows}열) × " +
                      $"{PortConfig.YardBlockLengthM:F1}m({PortConfig.YardBays}베이) · " +
                      $"장치능력 {PortConfig.YardCapacityTeu:N0} TEU({PortConfig.YardTiers}단) · " +
                      $"블록↔다리 여유 {PortConfig.YardBlockLegClearanceM:F2}m(편측) · " +
                      $"바다쪽 이동 {PortConfig.YardShiftAppliedM:F1}m(한계 {PortConfig.YardShiftMaxM:F1}m) · " +
                      $"야드 x −{PortConfig.ApronWidthMeters:F0}~−{PortConfig.ApronWidthMeters + depth:F1}m");
        }

        /// <summary>바다 — 수면이 StsConfig.SeaLevelY에 정확히 오도록 해저 깊이만큼 내려 놓는다. 이름이 Sea/Sea_*여야
        /// IsSeaName()이 지면 탐색에서 걸러낸다(안 그러면 넓은 바다가 '면적 최대' 휴리스틱에 골라진다). 콜라이더 없음.</summary>
        [MenuItem("Model/FBX/항구/바다 배치 (Sea)", false, 4)]
        static void PlaceSea()
        {
            var fbx = Load(SeaFbx, "바다"); if (fbx == null) return;

            // 바다를 줄이다 보면 접안한 배가 물 밖으로 나간다. 조용히 넘어가지 않게 여기서 잡는다.
            if (!PortConfig.SeaFitsShip)
                Debug.LogWarning($"[항구] 바다가 설계선보다 작습니다 — 바다 {PortConfig.SeaWidthMeters:F0}×" +
                                 $"{PortConfig.SeaLengthMeters:F0}m vs 선박 {ShipConfig.BeamMeters:F1}×" +
                                 $"{ShipConfig.LoaMeters:F0}m. PortConfig.SeaApronRatio 를 키우세요.");

            float scale = FbxScaleByHeight(fbx, PortConfig.WaterDepthMeters);
            // 겹침 1m 만큼 안벽 안으로 파고들게 — x=0 에서 면이 딱 만나면 Z-fighting.
            float x = (PortConfig.SeaWidthMeters - PortConfig.SeaOverlapM) * 0.5f * StsConfig.ModelScale;
            float y = -PortConfig.QuayWallHeightMeters * StsConfig.ModelScale;   // 원점 = 해저

            var root = NewRoot(StsPartNames.QuaySea);
            Place(fbx, root, StsPartNames.QuaySea + "_Body", new Vector3(x, y, 0f), scale);

            Done(root, $"수면 y={StsConfig.SeaLevelY:F4}u(−{StsConfig.QuayDeckAboveSeaMeters:F0}m) · " +
                       $"{PortConfig.SeaWidthMeters:F0} × {PortConfig.SeaLengthMeters:F0}m · " +
                       $"수심 {PortConfig.WaterDepthMeters:F0}m · 폭=에이프런×{PortConfig.SeaApronRatio:F0} · " +
                       $"겹침 {PortConfig.SeaOverlapM:F0}m · scale {scale:F4}");
        }

        /// <summary>항구 전체를 씬 뷰에 담고 부재별 실측을 찍는다. 부재마다 배치 직후 FrameSelected를 하는데
        /// 마지막이 바다(1,516m)라 부두(30m 폭)가 실 한 가닥으로 보인다 — 안 보이는 렌더러도 같이 잡아낸다.</summary>
        [MenuItem("Model/FBX/항구/전체 보기 + 실측", false, 20)]
        static void FrameAll()
        {
            var quay = GameObject.Find(StsPartNames.QuayGround);
            if (quay == null) { Debug.LogWarning("[항구] Quay_Ground 없음 — 부재를 먼저 배치하세요."); return; }

            var sb = new System.Text.StringBuilder($"[항구] 전체 실측 — {StsPartNames.QuayGround} 자식 {quay.transform.childCount}\n");
            float M = StsConfig.InvModelScale;
            Bounds? all = null;
            foreach (Transform c in quay.transform)
            {
                var rs = c.GetComponentsInChildren<Renderer>();
                int off = 0; Bounds? b = null;
                foreach (var r in rs)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) { off++; continue; }
                    if (b == null) b = r.bounds; else { var t = b.Value; t.Encapsulate(r.bounds); b = t; }
                }
                if (b == null) { sb.AppendLine($"  {c.name,-14} 렌더러 {rs.Length} · 보이는 것 0  ← 안 보임"); continue; }
                if (all == null) all = b; else { var t = all.Value; t.Encapsulate(b.Value); all = t; }
                var v = b.Value;
                sb.AppendLine($"  {c.name,-14} 렌더러 {rs.Length,4}{(off > 0 ? $" (꺼짐 {off})" : "")}" +
                              $"  x {v.min.x * M,8:F1}~{v.max.x * M,7:F1}" +
                              $"  y {v.min.y * M,7:F1}~{v.max.y * M,6:F1}" +
                              $"  z {v.min.z * M,8:F1}~{v.max.z * M,7:F1} m");
            }
            if (all == null) { Debug.LogWarning(sb.ToString() + "  보이는 렌더러가 하나도 없습니다."); return; }
            var A = all.Value;
            sb.AppendLine($"  {"── 합계",-14} {A.size.x * M,25:F1} × {A.size.y * M,13:F1} × {A.size.z * M,17:F1} m");

            Selection.activeGameObject = quay;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { sv.orthographic = false; sv.Frame(A, false); sv.Repaint(); }
            Debug.Log(sb.ToString());
        }

        /// <summary>씬의 모든 머티리얼에 GPU 인스턴싱을 켠다 — 같은 메시+머티리얼 조합이 한 드로우콜로 묶인다.
        /// 형상·색이 바뀌지 않는 무손실 최적화다. 서버가 5인스턴스로 씬을 렌더하므로 드로우콜이 5배로 곱해져 CPU 병목이 여기다.</summary>
        [MenuItem("Model/FBX/항구/GPU 인스턴싱 켜기 (전체)", false, 21)]
        static void EnableInstancingAll()
        {
            var mats = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
                foreach (var m in r.sharedMaterials)
                    if (m != null) mats.Add(m);

            int on = 0;
            foreach (var m in mats)
            {
                if (m.enableInstancing) continue;
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                on++;
            }
            AssetDatabase.SaveAssets();

            // 드로우콜 근사 = (메시, 머티리얼) 고유 조합 수. 인스턴싱이 이 단위로 묶는다.
            var combos = new HashSet<(Mesh, Material)>();
            int renderers = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                renderers++;
                foreach (var m in r.sharedMaterials)
                    if (m != null) combos.Add((mf.sharedMesh, m));
            }
            Debug.Log($"[항구] GPU 인스턴싱 — 머티리얼 {mats.Count}종 중 {on}종 신규 활성.\n" +
                      $"  렌더러 {renderers:N0} → 인스턴싱 후 드로우콜 근사 {combos.Count:N0}" +
                      $" (감소 {(1f - (float)combos.Count / Mathf.Max(1, renderers)):P1})\n" +
                      $"  서버 5인스턴스 환산 {renderers * 5:N0} → {combos.Count * 5:N0}");
        }
    }
}
#endif
