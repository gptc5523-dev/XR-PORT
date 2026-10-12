#if UNITY_EDITOR
using AIXRCrane.Crane.Sts.Net;
using UnityEditor;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.EditorTools
{
    public static partial class QuayPartsPlacer
    {
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
            // 적용과 동시에 저장 — 저장 안 하면 빌드에 안 들어간다.
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
    }
}
#endif
