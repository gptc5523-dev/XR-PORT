using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>모드 선택 패널 — HMD 우하단에 고정되는 표시 전용 HUD(선택은 StsCraneVRController).
    /// 씬에 없어도 [RuntimeInitializeOnLoadMethod]로 자동 생성된다.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/Crane Mode Selector HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneModeSelectorHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCraneVRController controller;
        [Tooltip("HUD를 붙일 카메라. 비우면 Camera.main.")]
        [SerializeField] Camera hmdCamera;

        [Header("HMD 우하단 위치 (카메라 로컬 좌표, m)")]
        [Tooltip("x=오른쪽(+), y=아래(-). z는 CraneHud.HudDistance로 통일. VR에서 보며 미세조정.")]
        [SerializeField] Vector3 hmdOffset = new Vector3(0.28f, -0.15f, CraneHud.HudDistance);   // 상태 패널 바로 아래(추정값 — 실제 x 는 AlignRightToStatus 가 맞춘다)
        [Tooltip("StatusHUD와 동일하게 기울임 보정 — 같은 우측 영역이라 값도 그대로 맞춤(휘어짐 방지).")]
        [SerializeField, Range(-30f, 30f)] float tiltYawDeg = -15f;    // StatusHUD와 동일
        [SerializeField, Range(-30f, 30f)] float tiltPitchDeg = 8f;    // StatusHUD와 동일

        [Header("패널/텍스트")]
        [SerializeField] Vector2 panelPixels = new Vector2(360f, 230f);
        [SerializeField] float worldScale = 0.0006f;
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);   // 패널 배경 알파 표준(공용 토큰)
        [SerializeField] int fontSize = 20;

        Canvas canvas;
        Text text;
        string lastText;   // 바뀔 때만 Text.text 대입(모드/커서 바뀔 때만 변함 → 캔버스 리빌드 절감)
        float nextTextRefresh;   // 텍스트 생성/대입 스로틀(CraneHud.TextHz) — 매 프레임 문자열 생성 방지
        bool attached;
        float nextAttachTry;   // 카메라가 늦게 켜질 때 매 프레임 말고 ~0.5s마다만 재시도
        readonly StringBuilder sb = new StringBuilder(256);
        Transform statusCanvasT;            // STS 크레인 상태 패널 캔버스(오른쪽 변 정렬 대상) — 1회 탐색 후 캐시
        RectTransform statusBg, myBg;       // 두 패널의 BG(폭 측정용)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() { /* 모드 선택은 CraneStatusHUD 패널에 합쳐짐 — 별도 패널 자동스폰 중단 */ }

        void Start()
        {
            if (controller == null) controller = CraneHud.FindVrController();
            BuildCanvas();
            AttachToHmd();
        }

        void LateUpdate()
        {
            if (canvas == null || text == null) return;

            // 표시 조건: 조종 활성일 때만(조종 진입 = 오른쪽 스틱 클릭). 관찰이면 호스트·관전자 모두 숨김.
            if (controller == null || !controller.isActiveAndEnabled) controller = CraneHud.FindVrController();
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool show = (nm == null || nm.IsServer) && controller != null && controller.ControlActive;
            if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
            if (!show) return;

            // 카메라가 늦게 켜지는 경우 — 아직 못 붙었으면 주기적으로만 재시도.
            if (!attached && Time.time >= nextAttachTry)
            {
                nextAttachTry = Time.time + 0.5f;
                AttachToHmd();
            }

            if (attached) AlignRightToStatus();   // 오른쪽 변을 STS 크레인 상태 패널에 맞춤(런타임 폭 측정)

            if (CraneHud.Due(ref nextTextRefresh, CraneHud.TextHz))
                CraneHud.SetTextIfChanged(text, ref lastText, BuildText());
        }

        // HMD(카메라) 우하단에 고정 부착 — 카메라 자식이라 매 프레임 추적 없이 머리를 따라온다.
        void AttachToHmd()
        {
            if (canvas == null) return;
            var cam = hmdCamera != null ? hmdCamera : Camera.main;
            if (cam == null) return;

            canvas.transform.SetParent(cam.transform, worldPositionStays: false);
            canvas.transform.localPosition = hmdOffset;
            CraneHud.FaceCameraChild(canvas.transform, hmdOffset, tiltPitchDeg, tiltYawDeg);   // StatusHUD와 동일 보정 — 휘어짐 방지
            attached = true;
        }

        // 오른쪽 변을 상태 패널에 맞춘다 — 폭이 런타임에 정해지므로 중심 = 상태 패널 오른쪽 변 − 내 반폭.
        //   폭(월드) = BG.rect.width × 캔버스 localScale. 상태 패널이 없으면 hmdOffset 유지.
        void AlignRightToStatus()
        {
            if (canvas == null) return;
            if (myBg == null) myBg = canvas.transform.Find("BG") as RectTransform;
            if (myBg == null || myBg.rect.width <= 1f) return;   // 레이아웃 아직이면 다음 프레임

            if (statusCanvasT == null)
            {
                var go = GameObject.Find(StsPartNames.CraneStatusCanvas);
                if (go == null) return;   // 상태 패널 미발견 → 정적 위치 유지
                statusCanvasT = go.transform;
                statusBg = statusCanvasT.Find("BG") as RectTransform;
            }
            if (statusBg == null || statusBg.rect.width <= 1f) return;

            float statusRight = statusCanvasT.localPosition.x + statusBg.rect.width * statusCanvasT.localScale.x * 0.5f;
            float myHalf = myBg.rect.width * canvas.transform.localScale.x * 0.5f;
            float newX = statusRight - myHalf;

            var p = canvas.transform.localPosition;
            if (Mathf.Abs(p.x - newX) > 0.0005f)   // 바뀔 때만 갱신(빌보드 재계산 절감)
            {
                p.x = newX; p.y = hmdOffset.y; p.z = hmdOffset.z;
                canvas.transform.localPosition = p;
                CraneHud.FaceCameraChild(canvas.transform, p, tiltPitchDeg, tiltYawDeg);   // 위치 변경 → 빌보드 재설정
            }
        }

        // Canvas/배경/텍스트 자동 생성
        void BuildCanvas()
        {
            // fitToText: 검정 배경이 글자 분량에 맞춰 자동 축소(여백=아래 inset). panelPixels는 무시됨.
            canvas = CraneHud.BuildPanel(transform, "CraneModeCanvas", panelPixels, worldScale,
                bgColor, fontSize, Color.white, TextAnchor.UpperLeft, new Vector2(18, 14), out text,
                fitToText: true);
            text.text = "...";
        }

        // 모드 목록 텍스트
        string BuildText()
        {
            sb.Clear();
            sb.AppendLine("<b><size=22>모드 선택</size></b>");
            sb.AppendLine();

            int cur = controller != null ? (int)controller.CurrentMode : -1;   // 적용된 모드 — 초록으로 표시
            int sel = controller != null ? controller.SelectedIndex : -1;       // 스틱 후보 — 굵게로 표시
            var names = StsCraneVRController.ModeNames;
            for (int i = 0; i < names.Length; i++)
            {
                string line = $"{i + 1}. {names[i]}";
                // 글리프 없이 색+굵기로 네 상태 구분: 후보이자 적용 = 굵게+초록 · 후보 = 굵게+청록 · 적용 = 초록 · 그 외 = 회색.
                if (i == sel && i == cur)
                    sb.AppendLine($"<color=#7FFF7F><b>{line}</b></color>");
                else if (i == sel)
                    sb.AppendLine($"<color=#5FE0FF><b>{line}</b></color>");
                else if (i == cur)
                    sb.AppendLine($"<color=#7FFF7F>{line}</color>");
                else
                    sb.AppendLine($"<color=#999999>{line}</color>");
            }
            sb.AppendLine();
            sb.AppendLine("<size=13><color=#BBBBBB>트리거 당긴 채 스틱 ↑↓ 선택 · B로 확정</color></size>");

            // 모드별 버튼 안내 — 집기/놓기(Y/X)는 공통, 운전실 시점(A)은 조종·갠트리에서만.
            //   cur 가 -1(controller null)이면 (Mode)(-1)==Move 가 false 라 cur 로 명시 비교한다.
            bool cabCapable = cur == (int)StsCraneVRController.Mode.Crane
                           || cur == (int)StsCraneVRController.Mode.Gantry;
            string btns = cabCapable
                ? "<b>Y</b> 잡기 · <b>X</b> 놓기 · <b>A</b> 운전실"
                : "<b>Y</b> 잡기 · <b>X</b> 놓기";
            sb.AppendLine($"<size=13><color=#5FE0FF>{btns}</color></size>");   // 안내=청록(녹색은 상태 전용)
            // 시점 높이 조절 안내(모든 모드 공통) — 오른손 검지 트리거 누른 채 왼손 스틱 위/아래.
            sb.AppendLine("<size=13><color=#5FE0FF>오른<b>트리거</b>+왼스틱 ↑↓ : 시점 높이</color></size>");
            return sb.ToString();
        }
    }
}
