using UnityEngine;
using UnityEngine.UI;
using Container.Crane.Sts;

namespace Container.Crane.Flat
{
    /// <summary>
    /// 평면 모드 HUD — 화면 고정(Screen Space Overlay).
    ///
    /// ★ 기존 VR HUD들(CraneStatusHUD·CraneAlarmHUD 등)은 world-space 캔버스를 머리/컨트롤러에 붙이는
    ///   head-locked 방식이라 평면 화면에선 안 읽힌다. 그쪽을 고치지 않고 여기서 화면 고정 HUD를 따로 만든다.
    ///
    /// ★ 안전 여백(safeInset): XREAL One Pro 는 대각 57°로 시야가 좁아 화면 '끝'이 잘 안 보인다.
    ///   그래서 HUD를 가장자리에 딱 붙이지 않고 안쪽으로 들인다. 일반 모니터에서도 손해가 없다.
    ///
    /// 갱신은 CraneHud.TextHz(8Hz)로 스로틀하고, 문자열이 바뀔 때만 대입해 캔버스 리빌드를 줄인다
    /// (VR HUD들과 같은 규약).
    /// </summary>
    [AddComponentMenu("Container/Flat Mode/Flat HUD")]
    [DisallowMultipleComponent]
    public sealed class FlatHud : MonoBehaviour
    {
        [Tooltip("화면 가장자리에서 안쪽으로 들이는 비율(0~0.2). XREAL 57° 시야에서 끝이 잘리는 것 방지.")]
        [SerializeField, Range(0f, 0.2f)] float safeInset = 0.06f;
        [SerializeField] int fontSize = 24;
        [Tooltip("시작 시 HUD 표시 여부. 실행 중 Y 버튼(또는 F1)으로 토글.")]
        [SerializeField] bool visibleOnStart = true;

        Canvas canvas;
        Text statusText, helpText;
        string lastStatus, lastHelp;
        float nextRefresh;

        FlatCraneController controller;

        /// <summary>HUD 표시/숨김 토글 — 게임패드 Y 버튼이 호출.</summary>
        public void Toggle()
        {
            if (canvas == null) return;
            canvas.enabled = !canvas.enabled;
        }

        void Awake()
        {
            Build();
            if (canvas != null) canvas.enabled = visibleOnStart;
        }

        void Build()
        {
            var canvasGo = new GameObject("FlatHudCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;   // 다른 UI 위에

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;   // 폭·높이 절반씩 반영 — 안경(16:9)과 모니터 양쪽에서 무난

            // 우상단 = 축 상태, 좌하단 = 조작 안내.
            statusText = BuildPanel(canvasGo.transform, "Status",
                anchor: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f), align: TextAnchor.UpperRight,
                size: new Vector2(560f, 260f));
            helpText = BuildPanel(canvasGo.transform, "Help",
                anchor: new Vector2(0f, 0f), pivot: new Vector2(0f, 0f), align: TextAnchor.LowerLeft,
                size: new Vector2(720f, 260f));

            helpText.text = lastHelp = HelpBody();
        }

        // 반투명 배경 + 텍스트 1개짜리 패널. 앵커 모서리에서 safeInset 만큼 안쪽으로 들인다.
        Text BuildPanel(Transform parent, string name, Vector2 anchor, Vector2 pivot, TextAnchor align, Vector2 size)
        {
            var panel = new GameObject(name, typeof(Image));
            panel.transform.SetParent(parent, worldPositionStays: false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            // 앵커 모서리 기준 안쪽 방향 = (0.5 - anchor) 의 부호. 참조 해상도 기준으로 픽셀 환산.
            rt.anchoredPosition = new Vector2(
                Mathf.Sign(0.5f - anchor.x) * (1920f * safeInset),
                Mathf.Sign(0.5f - anchor.y) * (1080f * safeInset));

            var img = panel.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);
            img.raycastTarget = false;

            var txtGo = new GameObject("Text", typeof(Text));
            txtGo.transform.SetParent(panel.transform, worldPositionStays: false);
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18f, 14f);
            trt.offsetMax = new Vector2(-18f, -14f);

            var txt = txtGo.GetComponent<Text>();
            txt.font = CraneHud.CreateKoreanFont(fontSize);
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.alignment = align;
            txt.supportRichText = true;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        void Update()
        {
            if (canvas == null || !canvas.enabled) return;
            if (!CraneHud.Due(ref nextRefresh, CraneHud.TextHz)) return;

            if (controller == null) controller = FindAnyObjectByType<FlatCraneController>();
            CraneHud.SetTextIfChanged(statusText, ref lastStatus, StatusBody());
        }

        string StatusBody()
        {
            var crane = controller != null ? controller.Crane : null;
            if (crane == null) return $"<color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>크레인 없음 — 씬에 STS 크레인을 생성하세요</color>";

            var sb = new System.Text.StringBuilder(256);
            sb.Append($"<b>평면 모드</b>   <color=#{CraneHud.Hex(CraneHud.HudColor.Accent)}>게임패드 조작</color>\n");
            sb.Append(AxisLine("트롤리", crane.Trolley));
            sb.Append(AxisLine("호이스트", crane.Spreader));
            sb.Append(AxisLine("갠트리", crane.Gantry));

            var attach = crane.Attach;
            if (attach != null && attach.HasContainer)
                sb.Append($"화물   <color=#{CraneHud.Hex(CraneHud.HudColor.Ok)}>체결 {attach.AttachedLoadTons:0.#} t</color>\n");
            else
            {
                var grabber = crane.GetComponent<SpreaderGrabber>();
                if (grabber != null && grabber.ReadyToLock)
                    sb.Append($"화물   <color=#{CraneHud.Hex(CraneHud.HudColor.Ok)}>정렬됨 ▸ A로 체결</color>\n");
                else if (grabber != null && grabber.NearButUnseated)
                    sb.Append($"화물   <color=#{CraneHud.Hex(CraneHud.HudColor.Danger)}>모서리 정렬 필요</color>\n");
                else
                    sb.Append($"화물   <color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>없음</color>\n");
            }

            if (controller != null && controller.CabView)
                sb.Append($"<color=#{CraneHud.Hex(CraneHud.HudColor.Accent)}>운전실 시점 — X로 복귀</color>");
            return sb.ToString();
        }

        // 축 1줄: 이름 + 가동범위 내 위치(%) + 실척 좌표(m). 모델 단위 × InvModelScale = 실척 m.
        static string AxisLine(string label, IAxisMover axis)
        {
            if (axis == null)
                return $"{label}   <color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>없음</color>\n";
            float span = axis.Max - axis.Min;
            float pct = span > 1e-6f ? Mathf.Clamp01((axis.Current - axis.Min) / span) * 100f : 0f;
            float meters = axis.Current * StsConfig.InvModelScale;
            return $"{label}   {pct,5:0.0}%   ({meters,7:0.00} m)\n";
        }

        static string HelpBody() =>
            "<b>조작</b>\n" +
            "왼쪽 스틱 이동   ·   오른쪽 스틱 시선   ·   LB/RB 하강·상승\n" +
            "D패드 ←→ 트롤리   ·   D패드 ↑↓ 갠트리   ·   LT/RT 권하·권상\n" +
            "A 집기   ·   B 놓기   ·   X 운전실 시점   ·   Y HUD 토글\n" +
            "<color=#888888>키보드: WASD 이동 · 방향키 시선 · Q/E 상하 · J/L 트롤리 · U/O 갠트리 · K/I 호이스트 · G 집기 · H 놓기 · C 운전실 · F1 HUD</color>";
    }
}
