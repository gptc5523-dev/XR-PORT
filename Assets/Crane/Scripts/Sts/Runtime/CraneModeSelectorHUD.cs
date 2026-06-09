using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Container.Crane.Sts
{
    /// <summary>
    /// 오른쪽 컨트롤러에 항상 붙어 다니는 모드 선택 패널.
    ///   - 이동/운전/갠트리 3모드를 목록으로 보여주고 현재 모드를 강조
    ///   - 선택은 StsCraneVRController가 처리(오른쪽 스틱 위/아래 또는 B). 이 패널은 표시 전용.
    ///   - 오른쪽 컨트롤러 Transform을 이름으로 자동 탐색(실패 시 카메라 좌하단에 폴백 → 어쨌든 보이게)
    /// 씬에 안 붙여도 [RuntimeInitializeOnLoadMethod]로 자동 스폰. 이미 있으면 스킵.
    /// </summary>
    [AddComponentMenu("Container/STS Crane/Crane Mode Selector HUD")]
    [DisallowMultipleComponent]
    public sealed class CraneModeSelectorHUD : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] StsCraneVRController controller;
        [Tooltip("오른쪽 컨트롤러 Transform. 비우면 이름으로 자동 탐색.")]
        [SerializeField] Transform rightController;
        [Tooltip("컨트롤러를 못 찾을 때 폴백으로 붙을 카메라. 비우면 Camera.main.")]
        [SerializeField] Camera fallbackCamera;

        [Header("컨트롤러 부착")]
        [Tooltip("컨트롤러 위(월드 up) 띄울 높이 m. 패널은 항상 카메라를 향함(빌보드) — 손을 기울여도 안 꺾임.")]
        [SerializeField] float aboveHeight = 0.07f;

        [Header("카메라 폴백(로컬 좌표, m)")]
        [SerializeField] Vector3 cameraOffset = new Vector3(-0.30f, -0.18f, 0.7f);

        [Header("패널/텍스트")]
        [SerializeField] Vector2 panelPixels = new Vector2(360f, 230f);
        [SerializeField] float worldScale = 0.0006f;
        [SerializeField] Color bgColor = new Color(0f, 0f, 0f, 0.82f);
        [SerializeField] int fontSize = 20;

        Canvas canvas;
        Text text;
        string lastText;   // 바뀔 때만 Text.text 대입(모드/커서 바뀔 때만 변함 → 캔버스 리빌드 절감)
        float nextTextRefresh;   // 텍스트 생성/대입 스로틀(CraneHud.TextHz) — 매 프레임 문자열 생성 방지
        bool attachedToController;
        float nextAttachTry;   // 폴백 상태에서 전체 씬 스캔을 매 프레임 말고 ~0.5s마다만 재시도
        readonly StringBuilder sb = new StringBuilder(256);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CraneModeSelectorHUD>("ModeHUD");

        void Start()
        {
            if (controller == null) controller = FindAnyObjectByType<StsCraneVRController>();
            BuildCanvas();
            TryAttach();
        }

        void LateUpdate()
        {
            if (canvas == null || text == null) return;

            // 표시 조건: 호스트로 시작했을 때(IsServer) 또는 네트워킹이 아예 없을 때(싱글)만.
            //   - 접속 전(시작 메뉴 'STS 크레인 멀티플레이'가 떠 있는 동안)엔 숨긴다 → 처음엔 메뉴만 보이게.
            //   - 관전자(순수 클라이언트)도 숨긴다 — 조종을 못 하니 의미가 없다.
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool show = nm == null || nm.IsServer;
            if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
            if (!show) return;

            // 컨트롤러가 늦게 활성화되는 경우 — 폴백 상태면 주기적으로만 재시도해 컨트롤러로 승격
            //   (전체 씬 Transform 스캔이라 매 프레임 돌리지 않음)
            if (!attachedToController && Time.time >= nextAttachTry)
            {
                nextAttachTry = Time.time + 0.5f;
                TryAttach();
            }

            // 컨트롤러에 붙었으면 매 프레임 손 위에 띄우고 카메라를 향하게(빌보드).
            if (attachedToController)
                CraneHud.FaceCameraAbove(canvas.transform, rightController, aboveHeight,
                    fallbackCamera != null ? fallbackCamera : Camera.main);

            if (CraneHud.Due(ref nextTextRefresh, CraneHud.TextHz))
                CraneHud.SetTextIfChanged(text, ref lastText, BuildText());
        }

        // ───────── 부착(오른쪽 컨트롤러 우선, 실패 시 카메라) ─────────
        void TryAttach()
        {
            if (canvas == null) return;

            if (rightController == null) rightController = FindRightController();
            if (rightController != null)
            {
                // 위치/회전은 LateUpdate의 FaceCameraAbove가 매 프레임 잡는다(여기선 부모만 지정).
                canvas.transform.SetParent(rightController, worldPositionStays: false);
                if (!attachedToController)
                    Debug.Log($"[ModeHUD] 오른쪽 컨트롤러 '{rightController.name}'에 부착");
                attachedToController = true;
                return;
            }

            // 폴백 — 카메라 좌하단(머리 따라옴). 어쨌든 보이게.
            var cam = fallbackCamera != null ? fallbackCamera : Camera.main;
            if (cam != null)
            {
                canvas.transform.SetParent(cam.transform, worldPositionStays: false);
                canvas.transform.localPosition = cameraOffset;
                canvas.transform.localRotation = Quaternion.identity;
                attachedToController = false;
                if (Time.frameCount % 120 == 0)
                    Debug.LogWarning($"[ModeHUD] 오른쪽 '컨트롤러' 객체 미발견 — 손엔 안 붙임, 카메라 좌하단 폴백. " +
                                     $"활성 후보(이 중 컨트롤러 이름을 알려주거나 rightController에 직접 지정): {RightSideCandidates()}");
            }
        }

        // 오른쪽 컨트롤러 탐색 — ArrowHUD와 동일한 견고한 공유 로직(CraneHud) 사용.
        //   기존 '첫 매치' 방식은 'Right Controller Stabilized' 같은 보조 객체를 잡아 패널이
        //   엉뚱한 곳에 붙어 안 보이던 원인이 됐다. 못 찾으면 null → 카메라 폴백(손엔 절대 안 붙음).
        static Transform FindRightController() => CraneHud.FindController("right");

        // 대소문자 무시 부분일치 — ToLowerInvariant(프레임당 문자열 할당) 대신 IndexOf 사용
        static bool Has(string name, string sub) => name.IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // 진단용 — 'right'/'controller'/'hand' 들어간 활성 객체 이름 나열(실제 컨트롤러 이름 확인용)
        static string RightSideCandidates()
        {
            var names = new StringBuilder();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (Has(t.name, "right") || Has(t.name, "controller") || Has(t.name, "hand"))
                    names.Append(t.name).Append("  ·  ");
            return names.Length > 0 ? names.ToString() : "(없음)";
        }

        // ───────── Canvas/배경/텍스트 자동 생성 ─────────
        void BuildCanvas()
        {
            // fitToText: 검정 배경이 글자 분량에 맞춰 자동 축소(여백=아래 inset). panelPixels는 무시됨.
            canvas = CraneHud.BuildPanel(transform, "CraneModeCanvas", panelPixels, worldScale,
                bgColor, fontSize, Color.white, TextAnchor.UpperLeft, new Vector2(18, 14), out text,
                fitToText: true);
            text.text = "...";
        }

        // ───────── 모드 목록 텍스트 ─────────
        string BuildText()
        {
            sb.Clear();
            sb.AppendLine("<b><size=22>모드 선택</size></b>");
            sb.AppendLine();

            int cur = controller != null ? (int)controller.CurrentMode : -1;   // 적용된 모드(●현재)
            int sel = controller != null ? controller.SelectedIndex : -1;       // 스틱 후보(▸ 커서)
            var names = StsCraneVRController.ModeNames;
            for (int i = 0; i < names.Length; i++)
            {
                string cursor = (i == sel) ? "▸ " : "   ";
                string line = $"{cursor}{i + 1}. {names[i]}";
                string activeTag = (i == cur) ? "  <color=#7FFF7F>● 현재</color>" : "";
                if (i == sel)
                    sb.AppendLine($"<color=#5FE0FF><b>{line}</b></color>{activeTag}");   // 후보 = 청록 강조
                else
                    sb.AppendLine($"<color=#999999>{line}</color>{activeTag}");
            }
            sb.AppendLine();
            sb.AppendLine("<size=13><color=#BBBBBB>스틱 ↑↓ 선택 · B로 확정</color></size>");

            // 모드별 버튼 안내 — 처음 하는 사람도 어느 버튼이 무슨 동작인지 알게.
            //   집기/놓기(Y/X)는 모든 모드 공통, 운전실 시점(A)은 운전·갠트리에서만.
            string btns = ((StsCraneVRController.Mode)cur == StsCraneVRController.Mode.Move)
                ? "<b>Y</b> 잡기 · <b>X</b> 놓기"
                : "<b>Y</b> 잡기 · <b>X</b> 놓기 · <b>A</b> 운전실";
            sb.AppendLine($"<size=13><color=#7FFF7F>{btns}</color></size>");
            // 시점 높이 조절 안내(모든 모드 공통) — 오른손 검지 트리거 누른 채 왼손 스틱 위/아래.
            sb.AppendLine("<size=13><color=#7FFF7F>오른<b>트리거</b>+왼스틱 ↑↓ : 시점 높이</color></size>");
            return sb.ToString();
        }
    }
}
