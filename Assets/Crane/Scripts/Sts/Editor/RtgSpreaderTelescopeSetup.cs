#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Container.Crane.Sts.EditorTools
{
    /// <summary>
    /// FBX RTG 크레인 스프레더에 **텔레스코픽 신축(20/40/45ft) 드라이버**를 붙이고 배선한다.
    /// 「Model ▸ FBX ▸ 크레인 ▸ RTG 크레인 생성」이 자동 호출한다(수동 메뉴 없음).
    ///
    /// 빔을 스케일하지 않고 TeleBeam_F/B를 신축축으로 슬라이드(끝빔·트위스트락·플리퍼는 자식이라 자동 추종).
    /// 신축축·스케일은 <see cref="Container.Crane.Sts.RtgSpreaderTelescope"/>가 빔 위치에서 자동 산출하므로
    /// 하드코딩 없음. 배선 후 「스프레더 20ft로 줄이기 / 40ft로 늘이기」 메뉴나 스프레더의 RtgSpreaderTelescope에서
    /// Size를 바꿔(컨텍스트메뉴 'Cycle') 20↔40↔45 신축을 **반드시 눈으로 확인**한다 — 자동배선이어도 검증은 남는다.
    /// </summary>
    public static class RtgSpreaderTelescopeSetup
    {
        const string CraneName = "RTG 크레인";
        const string BeamF = "Spreader_TeleBeam_F";
        const string BeamB = "Spreader_TeleBeam_B";

        public static void Setup()
        {
            var crane = Selection.activeGameObject;
            if (crane == null || FindDeep(crane.transform, "Spreader") == null)
                crane = GameObject.Find(CraneName);
            if (crane == null) { Dialog($"대상 크레인을 못 찾음. '{CraneName}' 선택 후 다시 실행하세요."); return; }

            var spreader = FindDeep(crane.transform, "Spreader");
            var beamF = FindBeam(crane.transform, BeamF);
            var beamB = FindBeam(crane.transform, BeamB);
            if (spreader == null || beamF == null || beamB == null)
            {
                Dialog($"신축 빔을 못 찾음(FBX 계층 확인).\nSpreader={spreader}\n{BeamF}={beamF}\n{BeamB}={beamB}");
                return;
            }

            // 기존 드라이버 있으면 먼저 40ft 기준으로 복원 후 재캡처(재실행 시 이동된 자세를 기준으로 잘못 잡는 것 방지).
            var tele = spreader.GetComponent<RtgSpreaderTelescope>();
            if (tele != null) tele.RestoreRest();
            else tele = Undo.AddComponent<RtgSpreaderTelescope>(spreader.gameObject);

            tele.Configure(beamF, beamB, RtgSpreaderTelescope.Size.Ft40);

            // 배선 값이 Play/도메인리로드에도 유지되도록 오버라이드/씬 기록(Play 모드에선 금지 → 에디트 모드만).
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(tele);
                EditorUtility.SetDirty(beamF); EditorUtility.SetDirty(beamB);
                if (PrefabUtility.IsPartOfPrefabInstance(tele))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(tele);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(crane.scene);
            }
            Selection.activeGameObject = spreader.gameObject;
            Debug.Log("[RTG] 스프레더 신축 배선 완료 — 빔 F/B 슬라이드 방식(스케일 없음, 트위스트락 왜곡 없음).\n" +
                      "  기준자세=40ft(현재 임포트 포즈 캡처) · 신축축·스케일 자동검출.\n" +
                      "  확인: 스프레더의 RtgSpreaderTelescope에서 Size=Ft20/Ft45로 바꾸거나 컨텍스트메뉴 'Cycle 20 → 40 → 45ft'로 토글.");
        }

        // ── 유니티에서 즉시 신축 확인(Play 불필요, [ExecuteAlways]) ──
        public static void Shrink20() => Resize(RtgSpreaderTelescope.Size.Ft20, "20ft");
        public static void Extend40() => Resize(RtgSpreaderTelescope.Size.Ft40, "40ft");

        static void Resize(RtgSpreaderTelescope.Size s, string label)
        {
            var crane = Selection.activeGameObject;
            if (crane == null || FindDeep(crane.transform, "Spreader") == null)
                crane = GameObject.Find(CraneName);
            var spreader = crane != null ? FindDeep(crane.transform, "Spreader") : null;
            var tele = spreader != null ? spreader.GetComponent<RtgSpreaderTelescope>() : null;
            if (tele == null || !tele.IsConfigured)
            {
                Dialog("먼저 'RTG 크레인 생성'을 실행하세요. (RtgSpreaderTelescope 미배선/미설정)");
                return;
            }
            Undo.RecordObject(tele, "Spreader Resize");
            tele.SetSizeNow(s);               // 즉시 빔 슬라이드
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(tele);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tele.gameObject.scene);
            }
            SceneView.RepaintAll();
            Debug.Log($"[RTG] 스프레더 {label}로 신축 완료 (Size={s}). 트위스트락 스팬이 즉시 바뀝니다(Play 불필요).");
        }

        // ── 트위스트락 잠그기/풀기 (Play 불필요, [ExecuteAlways]) ──
        public static void Lock() => SetLock(true, "잠금");
        public static void Unlock() => SetLock(false, "해제");

        static void SetLock(bool locked, string label)
        {
            var crane = Selection.activeGameObject;
            if (crane == null || FindDeep(crane.transform, "Spreader") == null)
                crane = GameObject.Find(CraneName);
            var spreader = crane != null ? FindDeep(crane.transform, "Spreader") : null;
            var la = spreader != null ? spreader.GetComponent<Container.Crane.Sts.SpreaderLockAnimator>() : null;
            if (la == null)
            {
                Dialog("먼저 'RTG 크레인 생성'을 실행하세요. (SpreaderLockAnimator 미배선)");
                return;
            }
            Undo.RecordObject(la, "Twistlock");
            la.SetLockedNow(locked);            // 즉시 회전/딥
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(la);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(la.gameObject.scene);
            }
            SceneView.RepaintAll();
            Debug.Log($"[RTG] 트위스트락 {label} 완료 — 콘 4개가 90° 회전(잠금 시). Play 불필요.");
        }

        // 노드명이 접미사(_D 등) 붙어 임포트될 수 있어 정확일치 → 접두어일치 순으로 탐색.
        static Transform FindBeam(Transform root, string name) =>
            FindDeep(root, name) ?? FindDeepPrefix(root, name);

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        static Transform FindDeepPrefix(Transform root, string prefix)
        {
            if (root.name.StartsWith(prefix)) return root;
            foreach (Transform c in root) { var r = FindDeepPrefix(c, prefix); if (r != null) return r; }
            return null;
        }

        static void Dialog(string msg) => EditorUtility.DisplayDialog("RTG 스프레더 신축 배선", msg, "확인");
    }
}
#endif
