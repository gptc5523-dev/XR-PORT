#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIXRCrane.Ship;
using AIXRCrane.EditorTools;
using AIXRCrane.Crane.Sts.Net;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    /// <summary>항구 부재 FBX 배치 — 블렌더에서 만든 부재를 씬에 깐다. 메시는 C#에서 만들지 않고 '어디에 몇 개'만 정한다.
    /// 좌표 규약: 안벽 SSOT가 없어 안벽 가장자리=월드 원점 X0, 안벽 방향=Z로 깐다(부두 FBX 도입 시 실측으로 교체).</summary>
    public static class QuayPartsPlacer
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
        static readonly (string n, float r, float g, float b, float metal, float smooth, string normal)[] Mats =
        {
            ("Quay_Caisson",      0.56f, 0.55f, 0.52f, 0.00f, 0.10f, null),   // 해수 얼룩 콘크리트
            ("Quay_DeckAsphalt",  0.16f, 0.16f, 0.17f, 0.00f, 0.06f, null),   // 에이프런 아스팔트(매트)
            ("Curb_Concrete",     0.70f, 0.69f, 0.66f, 0.00f, 0.15f, null),   // 프리캐스트 연석(밝게 — 가장자리 인지)
            ("Bollard_CastSteel", 0.13f, 0.14f, 0.15f, 0.60f, 0.35f, null),   // 계선주 주강(차콜)
            ("Rail_Steel",        0.34f, 0.34f, 0.36f, 1.00f, 0.55f, null),   // 압연강 레일
            ("Sea_Water",         0.045f,0.115f,0.145f,0.00f, 0.92f, null),   // 항내 해수 — 잔잔해 반사 높게
            ("Sea_Bed",           0.05f, 0.07f, 0.08f, 0.00f, 0.05f, null),   // 해저·측면(거의 안 보임)
            ("Yard_Asphalt",      0.19f, 0.19f, 0.20f, 0.00f, 0.08f, null),   // 야드 포장 — 에이프런보다 살짝 밝게 구분
            ("Yard_Fill",         0.48f, 0.46f, 0.43f, 0.00f, 0.08f, null),   // 야드 성토 측면
            ("Yard_Paint",        0.85f, 0.68f, 0.08f, 0.00f, 0.30f, null),   // 블록 도색(황색)
            ("Lane_Paint",        0.88f, 0.74f, 0.10f, 0.00f, 0.25f, null),   // 안전 차선(안전 노랑 — 블록보다 밝게)
            ("StartMarker_Red",   0.80f, 0.12f, 0.10f, 0.00f, 0.65f, null),   // 체스말(임시) — 회색 부두에서 튀게.
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
        // 체스말(임시 위치 표시) — 에셋 GUID를 유지해야 머티리얼 리맵이 안 끊긴다(재임포트 금지).
        const string PawnFbx       = "Assets/Crane/Models/StartMarker_Pawn.fbx";
        const float  PawnHeightM   = 2.0f;    // 사람 키 — 멀리서도 자리가 보이게.
        const string ExitPawnName  = "ExitMarker_Pawn";

        /// <summary>'나가는 존'(<see cref="AIXRCrane.Crane.Sts.Net.ExitZone"/>) 자리를 빨간 체스말(폰 2m)로 표시(임시).
        /// 런타임 존과 같은 계산을 쓴다(자리가 어긋나면 표시가 무의미). EditorOnly라 빌드 제외, 다시 누르면 교체.</summary>
        [MenuItem("Model/FBX/항구/나가는 존 체스말 (임시)", false, 8)]
        static void PlaceExitPawn()
        {
            if (!AIXRCrane.Crane.Sts.Net.ExitZone.TryComputeCenter(
                    AIXRCrane.Crane.Sts.Net.ExitZone.DefaultInsetMeters, out Vector3 c))
            {
                EditorUtility.DisplayDialog("나가는 존 체스말",
                    "걷는 땅(부두)을 못 찾았습니다.\n안벽·야드를 먼저 배치하세요.", "확인");
                return;
            }
            var fbx = Load(PawnFbx, "나가는 존 체스말"); if (fbx == null) return;

            var prev = GameObject.Find(ExitPawnName);
            if (prev != null) Undo.DestroyObjectImmediate(prev);

            float scale = FbxScaleByHeight(fbx, PawnHeightM);
            // 부모 null → localPosition 이 곧 월드 좌표. 발은 데크 윗면 y=0(PlaceCaisson 규약)에 둔다.
            Place(fbx, null, ExitPawnName, new Vector3(c.x, 0f, c.z), scale);
            var pawn = GameObject.Find(ExitPawnName);
            if (pawn == null) return;
            pawn.tag = "EditorOnly";
            Undo.RegisterCreatedObjectUndo(pawn, "Place " + ExitPawnName);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pawn.scene);

            Vector3 real = c * StsConfig.InvModelScale;
            Done(pawn.transform, $"나가는 존 자리 실척 ({real.x:F1}, {real.z:F1})m · 데크 윗면 · 체스말 {PawnHeightM}m · " +
                                 $"scale {scale:F4} · EditorOnly(빌드 제외). 실제 존은 반경 실척 3m, 2초 서 있으면 접속 종료.");
        }

        const string SpawnPawnName = "SpawnMarker_Pawn";

        /// <summary>플레이어 리스폰 자리를 빨간 체스말(폰 2m)로 표시(임시) — 좌표를 부르는 도구라 1mm도 어긋나면 안 된다.
        /// 런타임과 같은 계산(TryComputeSpawnXZ, 클램프까지 끝난 값)을 쓴다. Y=0, EditorOnly. ExitMarker_Pawn과 별개로 공존 가능.</summary>
        [MenuItem("Model/FBX/항구/리스폰 지점 체스말 (임시)", false, 9)]
        static void PlaceSpawnPawn()
        {
            if (!AIXRCrane.Crane.Sts.CranePlayerStartPlacer.TryComputeSpawnXZ(out Vector3 xz))
            {
                EditorUtility.DisplayDialog("리스폰 지점 체스말",
                    "시작 지점을 못 잡았습니다.\n시작 마커(CranePlayerStartPoint)도 걷는 땅(부두)도 없습니다.\n" +
                    "안벽·야드를 먼저 배치하세요.", "확인");
                return;
            }
            var fbx = Load(PawnFbx, "리스폰 지점 체스말"); if (fbx == null) return;

            var prev = GameObject.Find(SpawnPawnName);
            if (prev != null) Undo.DestroyObjectImmediate(prev);

            float scale = FbxScaleByHeight(fbx, PawnHeightM);
            // 부모 null → localPosition 이 곧 월드 좌표. 발은 걷는 면 윗면 y=0(PlaceCaisson 규약)에 둔다.
            Place(fbx, null, SpawnPawnName, new Vector3(xz.x, 0f, xz.z), scale);
            var pawn = GameObject.Find(SpawnPawnName);
            if (pawn == null) return;
            pawn.tag = "EditorOnly";
            Undo.RegisterCreatedObjectUndo(pawn, "Place " + SpawnPawnName);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pawn.scene);

            // 실척으로도 찍는다 — 모델 단위(×1/24)로만 보면 숫자가 안 읽혀 부르기 어렵다.
            Vector3 real = xz * StsConfig.InvModelScale;
            Done(pawn.transform, $"리스폰 지점 — 실척 X {real.x:F2}m · Z {real.z:F2}m  (모델 {xz.x:F4}, {xz.z:F4}) · " +
                                 $"발은 y=0 · 체스말 {PawnHeightM}m · scale {scale:F4} · EditorOnly(빌드 제외). " +
                                 $"런타임 배치와 같은 계산(TryComputeSpawnXZ) — 클램프까지 끝난 최종 좌표다.");
        }

        /// <summary>체스말을 옮긴 자리를 실제 시작 지점(PlayerStartPoint)으로 적용 — 체스말은 표시일 뿐, 이 메뉴가
        /// 마커를 그 자리로 옮겨야 반영된다. Y는 유지(런타임이 마커 Y 무시, 걷는 면에 발 붙임), 회전은 체스말 yaw를 그대로 가져온다.</summary>
        [MenuItem("Model/FBX/항구/체스말 자리를 시작 지점으로 적용", false, 10)]
        static void ApplyPawnToStartPoint()
        {
            var pawn = GameObject.Find(SpawnPawnName);
            if (pawn == null)
            {
                EditorUtility.DisplayDialog("시작 지점 적용",
                    $"'{SpawnPawnName}' 을(를) 씬에서 못 찾았습니다.\n\n" +
                    "먼저 [리스폰 지점 체스말 (임시)] 로 체스말을 세운 뒤\n원하는 자리로 옮기고 다시 누르세요.", "확인");
                return;
            }
            var marker = Object.FindAnyObjectByType<AIXRCrane.Crane.Sts.CranePlayerStartPoint>(FindObjectsInactive.Include);
            GameObject mgo = marker != null ? marker.gameObject : GameObject.Find(StsPartNames.PlayerStartPoint);
            if (mgo == null)
            {
                EditorUtility.DisplayDialog("시작 지점 적용",
                    $"시작 지점 마커('{StsPartNames.PlayerStartPoint}')가 씬에 없습니다.\n\n" +
                    "마커가 없으면 저장 좌표(PortConfig)로 시작하므로\n이 적용이 의미가 없습니다.", "확인");
                return;
            }

            Undo.RecordObject(mgo.transform, "시작 지점을 체스말 자리로");
            Vector3 before = mgo.transform.position;
            Vector3 p = pawn.transform.position;
            // Y 는 유지 — 런타임이 마커 Y 를 무시하고 걷는 면 윗면에 발을 붙인다.
            mgo.transform.position = new Vector3(p.x, before.y, p.z);
            Vector3 f = pawn.transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) mgo.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            EditorUtility.SetDirty(mgo.transform);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(mgo.scene);
            // 적용과 동시에 저장한다 — Unity는 저장 전까지 디스크에 안 써서, 저장 안 하면 빌드에도 안 들어간다.
            //   메뉴를 누른 것 자체가 '이 상태를 원한다'는 의사표시라 여기서 저장까지 끝낸다.
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

            Vector3 rb = before * StsConfig.InvModelScale, ra = mgo.transform.position * StsConfig.InvModelScale;
            Done(mgo.transform,
                 $"시작 지점 적용 — 실척 (X {rb.x:F2}, Z {rb.z:F2})m → (X {ra.x:F2}, Z {ra.z:F2})m " +
                 $"(모델 {mgo.transform.position.x:F4}, {mgo.transform.position.z:F4}) · 방향 {mgo.transform.forward} · " +
                 $"★ 씬 저장까지 완료 — 이제 배포하면 반영됩니다.");
        }

        /// <summary>체스말을 옮긴 자리를 나가는 존(ExitZonePoint) 자리로 적용 — 시작 지점 적용과 같은 흐름(옮기고 누르면 저장까지).
        /// 마커가 없으면 ExitZone은 걷는 땅 모서리를 계산하는 폴백으로 동작한다.</summary>
        [MenuItem("Model/FBX/항구/체스말 자리를 나가는 존으로 적용", false, 11)]
        static void ApplyPawnToExitZone()
        {
            var pawn = GameObject.Find(ExitPawnName);
            if (pawn == null)
            {
                EditorUtility.DisplayDialog("나가는 존 적용",
                    $"'{ExitPawnName}' 을(를) 씬에서 못 찾았습니다.\n\n" +
                    "먼저 [나가는 존 체스말 (임시)] 로 체스말을 세운 뒤\n원하는 자리로 옮기고 다시 누르세요.", "확인");
                return;
            }
            var point = GameObject.Find(StsPartNames.ExitZonePoint);
            if (point == null)
            {
                point = new GameObject(StsPartNames.ExitZonePoint);
                Undo.RegisterCreatedObjectUndo(point, "Create " + StsPartNames.ExitZonePoint);
            }
            else Undo.RecordObject(point.transform, "나가는 존을 체스말 자리로");

            Vector3 p = pawn.transform.position;
            point.transform.position = new Vector3(p.x, 0f, p.z);   // Y 는 런타임이 걷는 면 윗면으로 올린다
            EditorUtility.SetDirty(point.transform);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(point.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

            Vector3 real = point.transform.position * StsConfig.InvModelScale;
            Done(point.transform,
                 $"나가는 존 적용 — 실척 X {real.x:F2}m · Z {real.z:F2}m (모델 {p.x:F4}, {p.z:F4}) · " +
                 $"반경 실척 3m · 2초 머물면 접속 종료 · 씬 저장까지 완료.");
        }

        const string ExitSignName = "ExitSign";
        const string ExitBandName = "ExitBand";

        /// <summary>'나가는 문' 표지판과 존 띠를 씬에 심는다(빌드 포함). 자리·자세는 런타임과 같은 코드
        /// (<see cref="ExitZone.FitSign"/>·<see cref="ExitZone.CreateBand"/>)를 쓴다.</summary>
        [MenuItem("Model/FBX/항구/나가는 문 표지판·존 배치", false, 12)]
        public static void PlaceExitSign()   // 배치 검증이 부를 수 있게 public
        {
            if (!ExitZone.TryComputeCenter(ExitZone.DefaultInsetMeters, out Vector3 c))
            {
                EditorUtility.DisplayDialog("나가는 문 표지판",
                    "나가는 존 자리를 못 잡았습니다.\n걷는 땅(부두)을 먼저 배치하세요.", "확인");
                return;
            }
            var prefab = Resources.Load<GameObject>(ExitZone.SignResourcePath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("나가는 문 표지판",
                    $"표지판을 못 찾았습니다:\nAssets/Crane/Resources/{ExitZone.SignResourcePath}.fbx\n\n" +
                    "유니티 창을 한 번 포커스해 임포트되게 하세요.", "확인");
                return;
            }

            foreach (var n in new[] { ExitSignName, ExitBandName })      // 다시 눌러도 하나만 남는다
            {
                var prev = GameObject.Find(n);
                if (prev != null) Undo.DestroyObjectImmediate(prev);
            }

            var sign = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            sign.name = ExitSignName;
            sign.transform.position = c;
            ExitZone.FitSign(sign, prefab.transform.localRotation, c);   // 런타임과 같은 식
            Undo.RegisterCreatedObjectUndo(sign, "Place " + ExitSignName);

            var band = ExitZone.CreateBand(null, ExitZone.DefaultRadiusMeters);
            band.transform.position = c;
            Undo.RegisterCreatedObjectUndo(band.gameObject, "Place " + ExitBandName);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sign.scene);
            Vector3 real = c * StsConfig.InvModelScale;
            Done(sign.transform,
                 $"나가는 문 표지판·존 — 실척 X {real.x:F2}m · Z {real.z:F2}m (모델 {c.x:F4}, {c.z:F4}) · " +
                 $"반경 실척 {ExitZone.DefaultRadiusMeters:0.#}m · 런타임과 같은 식. 씬 저장은 직접(⌘S).");
        }

        [MenuItem("Model/FBX/항구/연석 배치 (Quay_Curb)", false, 1)]
        static void PlaceCurb()
        {
            if (!BerthReady("연석")) return;
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
            if (!BerthReady("계선주")) return;
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
            if (!BerthReady("안벽")) return;
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
            if (!BerthReady("레일")) return;
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

        // ═══ 야드 적재 — 정밀 FBX만 사용(저폴리 안 씀), 대수가 그대로 렌더 예산이다 ═══
        //   정밀 FBX 규약(항구 부재와 반대): 루트가 자체 스케일을 가져 localScale은 안 건드림, 길이=Z축(회전 없음), 피봇=중앙 높이(y=높이/2).
        //   LOD1(원저자 export)과 정밀본(LOD0)을 LODGroup으로 묶어 근접 디테일과 성능을 함께 잡는다.
        const string YardFbx40 = "Assets/Container/Models/Container_40ft.fbx";
        const string YardFbx20 = "Assets/Container/Models/Container_20ft.fbx";
        const string YardLod40 = "Assets/Container/Models/LOD1/Container_40ft_LOD1.fbx";
        const string YardLod20 = "Assets/Container/Models/LOD1/Container_20ft_LOD1.fbx";

        // 화면 상대 높이 임계값 — ShipCreator 가 쓰던 값(문서/컨테이너_규격.md §10.7 실측 근거).
        //   씬은 1유닛 = 24m 라 거리 상수를 직접 쓰면 안 된다. 상대 높이라 단위 무관.
        const float ContLod0Height = 0.3787f;   // 이보다 크게 보이면 정밀본
        const float ContLod1Height = 0.0229f;   // 이보다 작으면 컬링
        const int    YardCount40 = 10;
        const int    YardCount20 = 10;   // 20ft 는 40ft 베이 한 칸에 두 개 → 셀 5개 사용
        /// <summary>배치 무늬 시드 — 같은 값이면 같은 무늬. 0 이면 매번 다르다.</summary>
        const int    YardSeed    = 20260907;
        /// <summary>ISO 컨테이너 표준 높이 — 실척 m. 실측 스케일 기준값.</summary>
        const float  ContainerHeightM = 2.591f;
        const float  Yard20ftGapM = YardGrid.Gap20ftM;   // 한 베이 안 20ft 두 개 사이 틈 — 실척 m

        /// <summary>야드 블록에 컨테이너를 놓는다 — 40ft·20ft 지정 개수만큼, 셀 순서대로 결정적으로.
        /// 좌표는 전부 PortConfig 유도값에서 나오므로 컨테이너가 블록 안에 맞는지가 곧 블록 좌표의 검산이다.</summary>
        [MenuItem("Model/FBX/항구/컨테이너 적재 (야드)", false, 7)]
        static void StackYardContainers()
        {
            if (!BerthReady("야드 적재")) return;
            ContainerFinal4Builder.EnsureMaterials();
            var f40 = AssetDatabase.LoadAssetAtPath<GameObject>(YardFbx40);
            var f20 = AssetDatabase.LoadAssetAtPath<GameObject>(YardFbx20);
            var d40 = AssetDatabase.LoadAssetAtPath<GameObject>(YardLod40);   // 없으면 정밀본만
            var d20 = AssetDatabase.LoadAssetAtPath<GameObject>(YardLod20);
            if (f40 == null || f20 == null)
            {
                EditorUtility.DisplayDialog("야드 적재", $"컨테이너 FBX 없음:\n{YardFbx40}\n{YardFbx20}", "확인");
                return;
            }

            // 실측 — 규격을 박아두면 FBX 가 바뀔 때 조용히 어긋난다. 스케일은 건드리지 않는다.
            float h40 = Probe(f40, out float len40, out float wid40);
            float h20 = Probe(f20, out float len20, out _);
            float inv = StsConfig.InvModelScale;
            if (Mathf.Abs(len40 * inv - PortConfig.ContainerLenM) > 0.05f ||
                Mathf.Abs(wid40 * inv - ProceduralContainerMesh.StdWidth) > 0.05f ||
                Mathf.Abs(len20 * inv - YardGrid.Len20ftM) > 0.05f)
            {
                Debug.LogError($"[항구] 컨테이너 FBX 실측이 규격과 다릅니다 — 40ft {len40*inv:F3}L×{wid40*inv:F3}W, " +
                               $"20ft {len20*inv:F3}L m. 적재를 중단합니다.");
                return;
            }

            float rowPitch = PortConfig.RowPitchM * StsConfig.ModelScale;
            float bayPitch = PortConfig.BayPitchM * StsConfig.ModelScale;
            float halfW    = PortConfig.YardBlockWidthM  * 0.5f * StsConfig.ModelScale;
            float halfL    = PortConfig.YardBlockLengthM * 0.5f * StsConfig.ModelScale;

            var root = NewRoot("Yard_Containers");
            var stale = GameObject.Find("Container_40ft");   // 테스트로 꺼낸 낱개 정리
            if (stale != null) Undo.DestroyObjectImmediate(stale);

            // 셀 목록 — 두 블록 전체를 모아 아래에서 섞어 쓴다. 순서대로 쓰면 첫 블록 첫 열이 일자로 다 차버린다
            //   (실제 야드도 한 줄로 늘어놓지 않는다).
            var cells = new List<(float x, float z)>();
            for (int i2 = PortConfig.YardLaneStart; i2 < PortConfig.YardLanes; i2++)
                for (int j2 = 0; j2 < PortConfig.YardBlocksPerLane; j2++)
                {
                    float bx = PortConfig.YardBlockCenterX(i2) * StsConfig.ModelScale;
                    float bz = PortConfig.YardBlockCenterZ(j2) * StsConfig.ModelScale;
                    for (int r = 0; r < PortConfig.YardRows; r++)
                        for (int b = 0; b < PortConfig.YardBays; b++)
                            cells.Add((bx - halfW + rowPitch * (r + 0.5f),
                                       bz - halfL + bayPitch * (b + 0.5f)));
                }

            int pairs20  = Mathf.CeilToInt(YardCount20 / 2f);   // 20ft 는 한 셀에 두 개
            int needCells = YardCount40 + pairs20;
            if (cells.Count < needCells)
            {
                Debug.LogError($"[항구] 셀 {cells.Count}개 < 필요 {needCells}개. 적재를 중단합니다.");
                return;
            }

            // 결정적 셔플(Fisher-Yates) — 두 블록·모든 열·모든 베이에 고르게 흩어진다.
            var rng = YardSeed == 0 ? new System.Random() : new System.Random(YardSeed);
            for (int k = cells.Count - 1; k > 0; k--)
            {
                int m2 = rng.Next(k + 1);
                (cells[k], cells[m2]) = (cells[m2], cells[k]);
            }

            for (int k = 0; k < YardCount40; k++)
                Put(f40, d40, root, $"Cont40_{k:00}", cells[k].x, h40 * 0.5f, cells[k].z);

            // 20ft 는 실물처럼 40ft 베이 한 칸에 두 개를 앞뒤로 넣는다.
            float off20 = (len20 + Yard20ftGapM * StsConfig.ModelScale) * 0.5f;
            int made20 = 0;
            for (int k = 0; k < pairs20 && made20 < YardCount20; k++)
            {
                var c = cells[YardCount40 + k];
                foreach (float dz in new[] { -off20, off20 })
                {
                    if (made20 >= YardCount20) break;
                    Put(f20, d20, root, $"Cont20_{made20:00}", c.x, h20 * 0.5f, c.z + dz);
                    made20++;
                }
            }

            // 두 블록에 실제로 흩어졌는지 — 한쪽만 차면 배치 로직이 잘못된 것이다.
            int inBlock0 = 0;
            foreach (Transform t in root)
                if (t.localPosition.z < 0f) inBlock0++;

            Done(root, $"40ft {YardCount40}개 + 20ft {made20}개(쌍 {pairs20}) = {YardCount40 + made20}개 · " +
                       $"셀 {needCells}/{cells.Count} · 시드 {YardSeed} 셔플 · " +
                       $"선미측 블록 {inBlock0} / 선수측 {YardCount40 + made20 - inBlock0} · " +
                       $"40ft {len40*inv:F2}L × {wid40*inv:F2}W × {h40*inv:F2}H m · 20ft {len20*inv:F2}L m · 1단");
        }

        /// <summary>컨테이너 하나를 놓는다 — 실측해서 ISO 높이에 맞춘다.
        /// localScale을 1로 리셋하면 안 된다 — 정밀본/LOD1 둘 다 이미 스케일이 있어 리셋하면 24배/1÷24배로 튄다(반드시 곱한다).</summary>
        static void Put(GameObject hi, GameObject lo, Transform parent, string name,
                        float x, float y, float z)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.localPosition = new Vector3(x, y, z);   // 피봇 = 중앙 높이

            var g0 = Fit(hi, root.transform, "LOD0");
            var r0 = g0.GetComponentsInChildren<Renderer>();

            Renderer[] r1 = System.Array.Empty<Renderer>();
            if (lo != null) r1 = Fit(lo, root.transform, "LOD1").GetComponentsInChildren<Renderer>();

            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(r1.Length > 0
                ? new[] { new LOD(ContLod0Height, r0), new LOD(ContLod1Height, r1) }
                : new[] { new LOD(ContLod1Height, r0) });
            lg.RecalculateBounds();
        }

        /// <summary>FBX를 꺼내 ISO 높이에 맞춘다 — localScale을 1로 리셋하면 안 된다(정밀본/LOD1 스케일이 이미 있어
        /// 리셋하면 24배/1÷24배로 튄다). 반드시 곱한다.</summary>
        static GameObject Fit(GameObject src, Transform parent, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            float m = RtgCraneFbxPlacer.CombinedBounds(go).size.y;
            float t = ContainerHeightM * StsConfig.ModelScale;
            if (m > 1e-6f) go.transform.localScale *= t / m;

            // 원점 규약이 FBX마다 다르다 — 정밀본은 '바닥', LOD1은 '중앙'. 래퍼는 y를 '중앙 높이'로 놓으므로
            //   바닥 원점 FBX는 반통(2.591/2=1.296m)만큼 떠오른다 — LOD가 바뀌는 순간 컨테이너가 튀어오른다.
            //   피봇을 바운즈 중앙으로 통일하면 어느 규약이든 같은 자리에 앉는다.
            go.transform.position += parent.position - RtgCraneFbxPlacer.CombinedBounds(go).center;
            return go;
        }

        /// <summary>배치 후 크기를 실측한다 — Put과 같은 스케일 보정을 걸고 잰다.
        /// 보정 전에 재면 LOD1(실척 m)이 24배로 나와 규격 가드가 오작동한다. 측정과 배치가 같은 값을 봐야 한다.</summary>
        static float Probe(GameObject src, out float len, out float wid)
        {
            var p = (GameObject)PrefabUtility.InstantiatePrefab(src);
            float m = RtgCraneFbxPlacer.CombinedBounds(p).size.y;
            float t = ContainerHeightM * StsConfig.ModelScale;
            if (m > 1e-6f) p.transform.localScale *= t / m;
            var b = RtgCraneFbxPlacer.CombinedBounds(p);
            len = b.size.z; wid = b.size.x;
            float h = b.size.y;
            Object.DestroyImmediate(p);
            return h;
        }

        /// <summary>에이프런 안전 차선 — 레일 양옆 ±LaneOffsetM에 4줄. 이름을 "Lane"으로 두는 게 핵심 —
        /// GantryRangeFit이 이 이름의 렌더러 Z 바운즈를 갠트리 주행 한계로 쓴다(장식이 아니라 SSOT). 레일 사이는 비움.</summary>
        [MenuItem("Model/FBX/항구/차선 배치 (Lane)", false, 6)]
        static void PlaceLane()
        {
            if (!BerthReady("차선")) return;
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

        /// <summary>야드 — 포장 1장 + 블록 마킹 4개(항상 같이 감). 포장은 에이프런 끝에서 케이슨과 정확히 맞댄다
        /// (겹치면 y=0 걷는 면에서 Z-fighting). 블록 마킹 이름은 반드시 YardBlock_Zone — RtgCraneCreator가 이 bounds로
        /// RTG 위치·갠트리 범위를 잡는 SSOT다.</summary>
        [MenuItem("Model/FBX/항구/야드 배치 (Yard)", false, 5)]
        static void PlaceYard()
        {
            if (!BerthReady("야드")) return;
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

            // 트럭 주행레인 도색은 제거했다(RTG를 블록 중앙으로) — 전에 깔았던 그룹이 남아 있으면 지운다.
            var stale = GameObject.Find("Yard_TruckLane");
            if (stale != null) Undo.DestroyObjectImmediate(stale);

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
            if (!BerthReady("바다")) return;
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

        // ── 공용 ──

        /// <summary>안벽 길이가 아직 안 정해졌으면 배치를 막는다 — 옛 값을 조용히 되살리는 대신 새 숫자를 요구한다.</summary>
        static bool BerthReady(string label)
        {
            if (BerthLenM > 0f) return true;
            EditorUtility.DisplayDialog(label + " 배치",
                "항구 치수가 아직 정해지지 않았습니다.\n\n" +
                "QuayPartsPlacer.BerthLenM (안벽 길이, 실척 m) 에 값을 넣어주세요.\n" +
                "기존 값(344m)은 오너 지시로 삭제했습니다.", "확인");
            return false;
        }

        static GameObject Load(string path, string label)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (fbx == null)
            {
                EditorUtility.DisplayDialog(label + " 배치",
                    $"FBX를 찾을 수 없습니다:\n{path}\n\n유니티 창을 한 번 포커스해 임포트되게 하세요.", "확인");
                return null;
            }
            if (EnsureMaterials(path))
                fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);   // 리임포트 후 재로드
            return fbx;
        }

        /// <summary>FBX 의 Blender 머티리얼을 URP/Lit 에셋으로 리맵한다(idempotent).
        /// 이미 전부 걸려 있으면 아무것도 안 하고 false 를 돌려 불필요한 리임포트를 피한다.</summary>
        internal static bool EnsureMaterials(string fbxPath)
        {
            if (!FbxMats.TryGetValue(fbxPath, out var names)) return false;
            if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter mi) return false;

            var already = mi.GetExternalObjectMap()
                            .Where(kv => kv.Key.type == typeof(Material) && kv.Value != null)
                            .Select(kv => kv.Key.name).ToHashSet();
            if (names.All(already.Contains)) return false;

            if (!Directory.Exists(MatDir)) { Directory.CreateDirectory(MatDir); AssetDatabase.Refresh(); }
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var n in names)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), GetOrCreateMat(n));
            mi.SaveAndReimport();
            Debug.Log($"[항구] 머티리얼 리맵 — {Path.GetFileName(fbxPath)} ← {string.Join(", ", names)}");
            return true;
        }

        static Material GetOrCreateMat(string name)
        {
            string path = $"{MatDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var d = Mats.FirstOrDefault(m => m.n == name);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", new Color(d.r, d.g, d.b, 1f));
            mat.SetFloat("_Metallic", d.metal);
            mat.SetFloat("_Smoothness", d.smooth);
            if (!string.IsNullOrEmpty(d.normal))
            {
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(d.normal);
                if (nrm != null)
                {
                    // 노멀맵으로 임포트돼 있어야 정상 반영된다(멱등).
                    if (AssetImporter.GetAtPath(d.normal) is TextureImporter ti &&
                        ti.textureType != TextureImporterType.NormalMap)
                    {
                        ti.textureType = TextureImporterType.NormalMap;
                        ti.SaveAndReimport();
                        nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(d.normal);
                    }
                    mat.SetTexture("_BumpMap", nrm);
                    mat.EnableKeyword("_NORMALMAP");
                }
                else Debug.LogWarning($"[항구] 노멀맵 없음: {d.normal}");
            }
            mat.enableInstancing = true;   // 같은 메시+머티리얼 반복이라 인스턴싱이 그대로 먹는다
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>부두 루트 — 없으면 만든다. StsCraneCreator·RtgCraneCreator·RtgCraneFbxPlacer·GantryRangeFit·ShipBerthMenu
        /// 5곳이 GameObject.Find(QuayGround)로 부두를 찾으므로, 부재를 씬 루트에 흩어놓으면 아무도 못 찾는다.</summary>
        static Transform QuayRoot()
        {
            var go = GameObject.Find(StsPartNames.QuayGround);
            if (go == null)
            {
                go = new GameObject(StsPartNames.QuayGround);
                Undo.RegisterCreatedObjectUndo(go, "Create " + StsPartNames.QuayGround);
            }
            return go.transform;
        }

        /// <summary>같은 이름의 기존 그룹을 지우고 Quay_Ground 아래에 새로 만든다
        /// — 두 번 눌러도 겹쳐 쌓이지 않게.</summary>
        static Transform NewRoot(string name)
        {
            var prev = GameObject.Find(name);
            if (prev != null) Undo.DestroyObjectImmediate(prev);
            var root = new GameObject(name).transform;
            root.SetParent(QuayRoot(), worldPositionStays: false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Place " + name);
            return root;
        }

        static void Place(GameObject fbx, Transform parent, string name, Vector3 localPos, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;      // FBX 원점 = 바닥·길이 중앙
            go.transform.localScale    = Vector3.one * scale;
        }

        static void Done(Transform root, string msg)
        {
            Selection.activeGameObject = root.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[항구] {root.name} — {msg}");
        }

        /// <summary>FBX 인스턴스를 '실척 높이 × ModelScale' 로 맞추는 배율. FBX 단위계(m/cm)를 몰라도
        /// 측정으로 수렴하므로 임포트 설정이 바뀌어도 안 깨진다. 항구 부재 공통.</summary>
        static float FbxScaleByHeight(GameObject fbx, float realHeightM)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            probe.transform.localScale = Vector3.one;
            float h = RtgCraneFbxPlacer.CombinedBounds(probe).size.y;   // 기존 헬퍼 재사용
            Object.DestroyImmediate(probe);
            return h > 1e-5f ? realHeightM * StsConfig.ModelScale / h : 1f;
        }
    }
}
#endif
