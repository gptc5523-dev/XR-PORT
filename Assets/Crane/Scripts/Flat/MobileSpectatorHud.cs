using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using AIXRCrane.Crane.Sts;
using AIXRCrane.Crane.Sts.Net;

namespace AIXRCrane.Crane.Flat
{
    /// <summary>모바일 관전 화면(안경·폰). 위: 접속 상태, 아래: 크레인마다 한 줄(이름·축·화물).
    /// ★ 모바일은 무조건 참가자(관전) — 호스트·접속 버튼 없음, 세션 밖이면 스스로 호스트에 붙는다. NetLanUI는 컴포넌트만 끈다.</summary>
    [AddComponentMenu("AI-XR Crane/Flat Mode/Mobile Spectator HUD")]
    [DisallowMultipleComponent]
    public sealed class MobileSpectatorHud : MonoBehaviour
    {
        [Tooltip("화면 가장자리에서 안쪽으로 들이는 비율 — XREAL One Pro 대각 57° 시야에서 끝이 잘리지 않게.")]
        [SerializeField, Range(0f, 0.2f)] float safeInset = 0.06f;
        [Tooltip("글자 크기(참조 1920×1080 기준). 안경으로 멀리 떠 보이는 화면이라 PC HUD(24)보다 크게.")]
        [SerializeField] int fontSize = 34;
        [Tooltip("호스트 비콘이 이 시간(초) 안에 왔을 때만 붙고, 붙어 보는 간격도 이 값이다.")]
        [SerializeField] float joinRetrySeconds = 3f;

        const float RefW = 1920f, RefH = 1080f;

        Canvas canvas;
        Text roleText, statusText, whoText;
        string lastRole, lastStatus, lastWho;
        float nextRefresh, nextJoinTry;

        NetLanUI net;
        FlatPlayerRig rig;
        StsCrane[] cranes = new StsCrane[0];

        /// <summary>STS 먼저, RTG 는 이름 순 — 아래 줄 순서이자 숫자 키 1·2·3 시점 순서(FlatPlayerRig).</summary>
        internal static StsCrane[] Ordered() => FindObjectsByType<StsCrane>(FindObjectsSortMode.None)
            .OrderBy(c => c.GetComponent<RtgBogieSteering>() != null).ThenBy(c => c.name, System.StringComparer.Ordinal).ToArray();
        string[] names = new string[0];

        void Start()   // 부트스트랩이 리그를 만든 뒤라 Start 에서 찾는다
        {
            EnsureEventSystem();
            net = FindAnyObjectByType<NetLanUI>();
            if (net != null) net.enabled = false;
            rig = FindAnyObjectByType<FlatPlayerRig>();

            // STS 먼저, RTG 는 이름 순 — 줄 앞 이름은 STS·RTG1·RTG2 (RTG 번호는 씬 이름 끝 번호)
            cranes = Ordered();
            names = cranes.Select(c => c.GetComponent<RtgBogieSteering>() == null ? "STS" : "RTG" + c.name.Substring(c.name.LastIndexOf('_') + 1)).ToArray();
            Build();

            bool intentOk = FlatPlayerRig.PointerIntentSelfCheck(out string why);
            QaLog.Check("MOBILE", "intent", intentOk, why);
            bool es = EventSystem.current != null;
            QaLog.Check("MOBILE", "boot", canvas != null && rig != null && rig.PointerNav && es && (net == null || !net.enabled),
                $"rig={rig != null} pointerNav={(rig != null && rig.PointerNav)} eventSystem={es} " +
                $"imguiHidden={(net == null || !net.enabled)} cranes={string.Join(",", names)}");
        }

        // EventSystem이 없으면 버튼이 클릭을 못 받는다(VR은 XRI가 다룸). 런타임 AddComponent는 Reset이 안 불려
        //   AssignDefaultActions를 직접 부른다.
        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (mobile)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        void Build()
        {
            var canvasGo = new GameObject("MobileHudCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 1f;   // 높이 기준 — 옆으로 긴 폰에서도 글자가 안 줄게
            canvasGo.AddComponent<GraphicRaycaster>();

            float ix = RefW * safeInset, iy = RefH * safeInset;
            var root = canvasGo.transform;

            // 위 왼쪽: 역할 · 크레인
            roleText = Label(root, "Role", new Vector2(0f, 1f), new Vector2(ix, -iy));

            // 위 오른쪽: 참가자 목록 — 화면이 작아 글자를 줄이고 모서리에 더 붙인다
            whoText = Label(root, "Who", new Vector2(1f, 1f), new Vector2(-ix * 0.5f, -iy * 0.5f), 0.75f);

            // 아래: 크레인마다 한 줄 — 배경은 글자 분량만큼만
            statusText = Label(root, "Status", new Vector2(0.5f, 0f), new Vector2(0f, iy));
        }

        // 반투명 검정 배경 + 글자. 배경은 글자 크기에 맞춰 늘고 준다(앵커 모서리 기준으로 자란다).
        Text Label(Transform parent, string name, Vector2 anchor, Vector2 pos, float textScale = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);
            img.raycastTarget = false;
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 8, 8);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            t.transform.SetParent(go.transform, worldPositionStays: false);
            int size = Mathf.RoundToInt(fontSize * textScale);
            t.font = CraneHud.CreateKoreanFont(size);
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // 세션 밖이면 참가자로 붙는다(호스트 시작 없음). ★ 호스트 비콘이 방금 왔을 때만, 비콘 IP로만 붙는다 —
        //   호스트 없이 붙으면 Netcode가 재시도마다 오류를 쌓았다. 끊기면 다음 비콘에 다시 붙는다.
        void EnsureParticipant()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || net == null || nm.IsClient || nm.IsServer) return;
            if (!net.HostDiscovered || Time.unscaledTime - net.LastHostSeen > joinRetrySeconds) return;
            if (Time.unscaledTime < nextJoinTry) return;
            nextJoinTry = Time.unscaledTime + joinRetrySeconds;
            net.BeginClient();
        }

        void Update()
        {
            EnsureParticipant();
            if (canvas == null || !CraneHud.Due(ref nextRefresh, CraneHud.TextHz)) return;

            var nm = NetworkManager.Singleton;
            bool connected = nm != null && nm.IsConnectedClient;
            string role = connected ? "● 관전 중"
                        : nm != null && nm.IsClient ? "◌ 호스트에 접속 중…"
                        : "○ 호스트 기다리는 중";
            var notice = CraneAlarmHUD.ActiveNotice;   // 나가는 존 카운트 등
            if (notice != null) role = $"<color=#{CraneHud.Hex(CraneHud.HudColor.Danger)}>{notice}</color>";
            CraneHud.SetTextIfChanged(roleText, ref lastRole, role);
            CraneHud.SetTextIfChanged(statusText, ref lastStatus, StatusBody());
            string who = WhoBody();
            whoText.transform.parent.gameObject.SetActive(who.Length > 0);
            CraneHud.SetTextIfChanged(whoText, ref lastWho, who);
        }

        string StatusBody()
        {
            if (cranes.Length == 0) return $"<color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>크레인 없음</color>";
            var lines = new string[cranes.Length];
            for (int i = 0; i < cranes.Length; i++)
            {
                var c = cranes[i];
                if (c == null) { lines[i] = $"<b>{names[i]}</b>  —"; continue; }
                string cargo = c.Attach != null && c.Attach.HasContainer
                    ? $"<color=#{CraneHud.Hex(CraneHud.HudColor.Ok)}>체결 {c.Attach.AttachedLoadTons:0.#} t</color>"
                    : $"<color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>없음</color>";
                lines[i] = $"<b>{names[i],-5}</b>  트롤리 {Pct(c.Trolley)}    호이스트 {Pct(c.Spreader)}    갠트리 {Pct(c.Gantry)}    화물 {cargo}";
            }
            return string.Join("\n", lines);
        }

        // 오른쪽 위 참가자 목록 — 한 사람 한 줄 "● 관전  소속 직급 이름". 세션 밖이면 아바타가 없으니 나만.
        static string WhoBody() => AIXRCrane.Crane.Sts.Net.PlayerAvatarSync.ListText();

        static string Pct(IAxisMover axis) => axis == null ? "—" : $"{FlatHud.Percent(axis):0.0}%";
    }
}
