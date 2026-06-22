using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 현재 모드에 맞는 조작 안내(스틱 방향 화살표·버튼)를 HMD 하단에 고정 표시.
    ///   - 모드(이동/운전/갠트리)에 따라 안내 내용 자동 변경
    ///   - 표시 전용 — 실제 입력은 StsCraneVRController가 처리
    ///   - head-locked: 카메라 자식으로 붙어 머리를 돌려도 하단에 따라옴
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰. 이미 있으면 스킵.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Control Hints HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneControlHintsHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCraneVRController controller;
        [SerializeField] Camera targetCamera;

        [Header("HMD 좌하단 위치 (카메라 로컬 좌표, m)")]
        [SerializeField] Vector3 hmdOffset = new Vector3(-0.26f, -0.32f, CraneHud.HudDistance);

        [Header("패널/텍스트")]
        [SerializeField] Vector2 panelPixels = new Vector2(640f, 250f);
        [Tooltip("패널 전체 크기 배율(1px=이 값 m). 작게 하려면 줄임.")]
        [SerializeField] float worldScale = 0.00055f;
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);   // 패널 배경 알파 표준(공용 토큰)
        [SerializeField] int fontSize = 18;

        Canvas canvas;
        Text text;
        string lastText;        // 바뀔 때만 Text.text 대입(모드 바뀔 때만 변함 → 캔버스 리빌드 절감)
        float nextTextRefresh;  // 텍스트 생성/대입 스로틀(CraneHud.TextHz) — 매 프레임 문자열 생성 방지
        readonly StringBuilder sb = new StringBuilder(512);

        // 일단 주석처리(사용자 요청) — '조작' 안내 HUD 비표시. 복구하려면 주석 해제
        // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        // static void AutoSpawn() => CraneHud.EnsureSpawned<CraneControlHintsHUD>("HintsHUD");

        void Start()
        {
            if (controller == null) controller = CraneHud.FindVrController();
            BuildCanvas();
            TryAttachToCamera();
        }

        void LateUpdate()
        {
            if (canvas == null || text == null) return;

            // 조종 HUD라 '조종 활성'(스틱클릭 진입)일 때만 표시 — 관찰(기본)이면 숨김(호스트·관전자 동일 화면).
            if (controller == null) controller = CraneHud.FindVrController();
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool show = (nm == null || nm.IsServer) && controller != null && controller.ControlActive;
            if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
            if (!show) return;

            // 빌보드 회전은 부착 시 1회만 설정 — 캔버스가 카메라 자식이라 향하는 로컬 회전은 매 프레임 동일(상수).
            if (canvas.transform.parent == null || canvas.transform.parent == transform)
                TryAttachToCamera();

            if (CraneHud.Due(ref nextTextRefresh, CraneHud.TextHz))
                CraneHud.SetTextIfChanged(text, ref lastText, BuildText());
        }

        void TryAttachToCamera()
        {
            if (canvas == null) return;
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;
            canvas.transform.SetParent(cam.transform, worldPositionStays: false);
            canvas.transform.localPosition = hmdOffset;
            CraneHud.FaceCameraChild(canvas.transform, hmdOffset);   // 카메라 향함 + 거울 해소 — 부착 시 1회
        }

        // ───────── Canvas/배경/텍스트 자동 생성 ─────────
        void BuildCanvas()
        {
            // fitToText: 배경이 글자 분량에 맞춰 자동 축소(빈 여백 제거)
            canvas = CraneHud.BuildPanel(transform, "CraneHintsCanvas", panelPixels, worldScale,
                bgColor, fontSize, Color.white, TextAnchor.UpperLeft, new Vector2(16, 12), out text, fitToText: true);
            text.text = "...";
        }

        // ───────── 모드별 조작 안내 텍스트 ─────────
        string BuildText()
        {
            sb.Clear();
            var mode = controller != null ? controller.CurrentMode : StsCraneVRController.Mode.Move;
            bool cab = controller != null && controller.CabView;

            sb.Append("<b><size=20>조작 — ");
            sb.Append(StsCraneVRController.ModeNames[(int)mode]);
            if (cab) sb.Append(" <color=#7FFF7F>(운전실 시점)</color>");
            sb.AppendLine("</size></b>");

            switch (mode)
            {
                case StsCraneVRController.Mode.Crane:
                    Line("오른 스틱", "<color=#5FE0FF>←  →</color>", "트롤리 이동");
                    Line("왼 스틱", "<color=#5FE0FF>↑</color> 올림 / <color=#5FE0FF>↓</color> 내림", "호이스트(스프레더 상하)");
                    Line("Y / X", "", "집기 / 놓기");
                    Line("A", "", cab ? "운전실 시점 끄기" : "운전실 시점");
                    break;
                case StsCraneVRController.Mode.Gantry:
                    Line("왼 스틱", "<color=#5FE0FF>←  →</color>", "갠트리 주행(크레인 전체)");
                    Line("A", "", cab ? "운전실 시점 끄기" : "운전실 시점");
                    break;
                default: // Move(이동)
                    Line("스틱", "", "걷기 · 회전 (이동)");
                    break;
            }

            sb.AppendLine();
            Line("오른 스틱", "<color=#FFD25F>↑  ↓</color>", "모드 선택(후보)");
            Line("B", "", "모드 확정");
            return sb.ToString();
        }

        // "• 컨트롤  화살표  설명" 한 줄
        void Line(string ctrl, string arrows, string desc)
        {
            sb.Append("• <b>");
            sb.Append(ctrl);
            sb.Append("</b>  ");
            if (!string.IsNullOrEmpty(arrows)) { sb.Append(arrows); sb.Append("  "); }
            sb.Append("<color=#CCCCCC>");
            sb.Append(desc);
            sb.Append("</color>");
            sb.AppendLine();
        }
    }
}
