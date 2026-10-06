using UnityEngine;
using UnityEngine.UI;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>글자 없는 화살표 HUD — 각 컨트롤러 위에 현재 모드의 미는 방향만 표시. 관전자 숨김, 자동 스폰.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Crane Controller Arrow HUD")]
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
        [SerializeField] float worldScale = 0.00042f;   // 화살표가 너무 컸음

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

            // 표시: 호스트 또는 싱글이고 조종 활성일 때만(관전자·관찰 모드 숨김).
            if (controller == null || !controller.isActiveAndEnabled) controller = CraneHud.FindVrController();
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

        // 모드별 화살표(빈 문자열 = 그 손은 숨김)
        static void ArrowsFor(StsCraneVRController.Mode mode, out string left, out string right)
        {
            switch (mode)
            {
                case StsCraneVRController.Mode.Crane:   // 왼:호이스트 상하, 오른:트롤리 좌우
                    // 방향만 — 버튼 안내는 모드선택 HUD 전담.
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

        // 왼/오른 각각 부착, 못 찾으면(원점/미추적 제외) 숨긴 채 다음 주기 재시도 — 바닥 잔상 방지.
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

        // 컨트롤러 탐색 — 공유 로직(CraneHud).
        static Transform FindController(string side) => CraneHud.FindController(side);

        static bool Has(string name, string sub) => name.IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // 진단용 — side/'controller' 이름의 활성 객체와 위치 나열
        static string Candidates(string side)
        {
            var s = new System.Text.StringBuilder();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                if (Has(t.name, side) || Has(t.name, StsPartNames.ControllerNameHint))
                    s.Append($"{t.name}@{t.position}  ·  ");
            return s.Length > 0 ? s.ToString() : "(없음)";
        }

        // 화살표 캔버스 1개 생성(글자만, 배경은 살짝)
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
