using UnityEngine;
using UnityEngine.UI;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 처음 잡는 사람을 위해 — 글자 설명 없이, 각 컨트롤러 '위에' 그 모드에서 스틱을 미는 방향을
    /// 화살표로만 띄운다. 모드(이동/운전/갠트리)에 따라 화살표가 자동으로 바뀐다.
    ///
    /// [표시 규칙 — StsCraneVRController가 실제로 읽는 축과 1:1]
    ///   조종모드 : 왼손 ↕(호이스트 상하)        오른손 ↔(트롤리 좌우)
    ///   갠트리   : 왼손 ↔(갠트리 좌우)           오른손 (없음 → 숨김)
    ///   이동     : 왼손 ✛(걷기 4방향)            오른손 ↔(회전)
    ///
    ///   - 표시 전용 — 입력은 StsCraneVRController가 처리. 화살표는 '어느 스틱을 어디로'만 알려준다.
    ///   - 모드 선택(오른 스틱 ↑↓ / B)은 ModeSelectorHUD가 따로 안내하므로 여기선 운전 방향만 보여준다.
    ///   - 관전자(순수 클라이언트)에겐 숨긴다 — 조종을 못 하니 의미가 없다.
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰. 이미 있으면 스킵.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Controller Arrow HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneControllerArrowHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCraneVRController controller;
        [Tooltip("왼/오른 컨트롤러 Transform. 비우면 이름으로 자동 탐색.")]
        [SerializeField] Transform leftController;
        [SerializeField] Transform rightController;

        [Header("컨트롤러 위 배치(월드 up, m) — 항상 카메라를 향함(빌보드)")]
        [Tooltip("왼손: 모드선택 패널이 없으니 낮게.")]
        [SerializeField] float leftHeight = 0.12f;
        [Tooltip("오른손: 모드선택 패널 '위'에 오도록 더 높게 — 화살표가 모드선택을 가리지 않게.")]
        [SerializeField] float rightHeight = 0.19f;

        [Header("화살표")]
        [SerializeField] int fontSize = 46;
        [SerializeField] Color arrowColor = new Color(0.373f, 0.878f, 1f, 1f);   // #5FE0FF (Accent 토큰과 정합)
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, 0.55f);   // 예외: 화살표는 부두/하늘 배경 덜 가리려 표준(PanelBgAlpha)보다 투명
        [SerializeField] float worldScale = 0.00042f;   // 기존 0.0006에서 축소 — 화살표가 너무 컸음

        Canvas leftCanvas, rightCanvas;
        Text leftText, rightText;
        bool leftAttached, rightAttached;
        float nextAttachTry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneControllerArrowHUD>("ArrowHUD");

        void Start()
        {
            if (controller == null) controller = CraneHud.FindVrController();
            leftCanvas = BuildArrowCanvas("CraneArrowL", out leftText);
            rightCanvas = BuildArrowCanvas("CraneArrowR", out rightText);
            TryAttach();
        }

        void LateUpdate()
        {
            if (leftCanvas == null || rightCanvas == null) return;

            // 표시 조건: 호스트로 시작했을 때(IsServer) 또는 네트워킹이 없을 때(싱글)만.
            //   - 접속 전(시작 메뉴가 떠 있는 동안)엔 숨김 → 처음엔 'STS 크레인 멀티플레이' 메뉴만 보이게.
            //   - 관전자(순수 클라이언트)도 숨김 — 조종을 못 하니 화살표가 무의미.
            // 조종 HUD라 '조종 활성'(스틱클릭 진입)일 때만 — 관찰(기본)이면 화살표 숨김.
            if (controller == null) controller = CraneHud.FindVrController();
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool show = (nm == null || nm.IsServer) && controller != null && controller.ControlActive;
            if (!show)
            {
                Show(leftCanvas, false);
                Show(rightCanvas, false);
                return;
            }

            // 컨트롤러가 늦게 켜지는 경우 — 아직 못 붙은 쪽만 주기적으로 재시도.
            if ((!leftAttached || !rightAttached) && Time.time >= nextAttachTry)
            {
                nextAttachTry = Time.time + 0.5f;
                TryAttach();
            }

            var mode = controller != null ? controller.CurrentMode : StsCraneVRController.Mode.Move;
            string l, r;
            ArrowsFor(mode, out l, out r);

            var cam = Camera.main;
            UpdateSide(leftCanvas, leftText, leftController, leftAttached, l, leftHeight, cam);
            UpdateSide(rightCanvas, rightText, rightController, rightAttached, r, rightHeight, cam);
        }

        // ───────── 모드별 화살표(빈 문자열 = 그 손은 숨김) ─────────
        static void ArrowsFor(StsCraneVRController.Mode mode, out string left, out string right)
        {
            switch (mode)
            {
                case StsCraneVRController.Mode.Crane:   // 왼:호이스트 상하, 오른:트롤리 좌우
                    // 화살표는 방향만 — 버튼 안내(Y잡기/X놓기)는 모드선택 HUD가 전담(3중복 제거).
                    left = "↑   ↓";
                    right = "←   →";
                    break;
                case StsCraneVRController.Mode.Gantry:  // 왼:갠트리 좌우, 오른:없음
                    left = "←   →";
                    right = "";
                    break;
                default:                                 // 이동: 왼 걷기(4방향, 가로 한 줄), 오른 회전
                    left = "←  ↑  ↓  →";   // 가로로 눕힘 — 세로 십자 대신 한 줄
                    right = "←   →";
                    break;
            }
        }

        // 부착된 손에만, 화살표가 있을 때만 표시(미부착이면 숨겨 바닥 잔상 방지) + 손 위 빌보드.
        void UpdateSide(Canvas canvas, Text text, Transform ctrl, bool attached, string arrows, float height, Camera cam)
        {
            bool on = attached && !string.IsNullOrEmpty(arrows);
            Show(canvas, on);
            if (!on) return;
            if (text.text != arrows) text.text = arrows;
            CraneHud.FaceCameraAbove(canvas.transform, ctrl, height, cam);
        }

        static void Show(Canvas canvas, bool on)
        {
            if (canvas != null && canvas.gameObject.activeSelf != on)
                canvas.gameObject.SetActive(on);
        }

        // ───────── 부착(왼/오른 각각, 실패 쪽은 다음 주기에 재시도) ─────────
        //   원점/미추적 객체는 거른다 → 못 찾으면 안 붙이고(=숨김) 다음 주기에 재시도 → 바닥 잔상 방지.
        void TryAttach()
        {
            if (!leftAttached)
            {
                if (leftController == null) leftController = FindController("left");
                if (leftController != null) leftAttached = Attach(leftCanvas, leftController, "왼");
                else if (Time.frameCount % 120 == 0)
                    Debug.LogWarning($"[ArrowHUD] 왼쪽 '컨트롤러'(원점 아님) 미발견 — 화살표 숨김 후 재시도. 후보: {Candidates("left")}");
            }
            if (!rightAttached)
            {
                if (rightController == null) rightController = FindController("right");
                if (rightController != null) rightAttached = Attach(rightCanvas, rightController, "오른");
                else if (Time.frameCount % 120 == 0)
                    Debug.LogWarning($"[ArrowHUD] 오른쪽 '컨트롤러' 미발견 — 재시도. 후보: {Candidates("right")}");
            }
        }

        bool Attach(Canvas canvas, Transform ctrl, string side)
        {
            if (canvas == null) return false;
            // 위치/회전은 LateUpdate의 FaceCameraAbove가 매 프레임 잡는다(여기선 부모만 지정).
            canvas.transform.SetParent(ctrl, worldPositionStays: false);
            Debug.Log($"[ArrowHUD] {side}쪽 컨트롤러 '{ctrl.name}'에 화살표 부착 (위치 {ctrl.position})");
            return true;
        }

        // 컨트롤러 탐색 — ModeSelectorHUD와 동일한 견고한 공유 로직(CraneHud) 사용.
        static Transform FindController(string side) => CraneHud.FindController(side);

        static bool Has(string name, string sub) => name.IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // 진단용 — side+'controller' 들어간 활성 객체와 그 위치 나열(실제 이름/원점 여부 확인용)
        static string Candidates(string side)
        {
            var s = new System.Text.StringBuilder();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (Has(t.name, side) || Has(t.name, StsPartNames.ControllerNameHint))
                    s.Append($"{t.name}@{t.position}  ·  ");
            return s.Length > 0 ? s.ToString() : "(없음)";
        }

        // ───────── 화살표 캔버스 1개 생성(글자만, 배경은 살짝) ─────────
        Canvas BuildArrowCanvas(string name, out Text text)
        {
            var canvas = CraneHud.BuildPanel(transform, name, new Vector2(220f, 160f), worldScale,
                bgColor, fontSize, arrowColor, TextAnchor.MiddleCenter, new Vector2(16, 10), out text,
                fitToText: true);
            text.text = "↔";
            return canvas;
        }
    }
}
