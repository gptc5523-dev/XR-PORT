using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using AIXRCrane.Crane.Sts;
using AIXRCrane.Crane.Sts.Net;
using AIXRCrane.Ship;

namespace AIXRCrane.Crane.Flat
{
    /// <summary>모바일 관전 화면(안경·폰) — 오너 2026-09-18 "VR 이랑 모바일이랑 화면이 다르게".
    ///   Beam Pro '공간 마우스'(커서 하나 + 누르기)로 쓰는 전제라 버튼은 크게, 조작 안내는 없다.
    ///   위: 접속 상태 · 크레인 이름 / 시점 버튼 [STS][RTG1][RTG2][배] · 아래: 고른 크레인의 축·화물 한 줄.
    ///   ★ 모바일은 **무조건 참가자(관전)** 다 — 오너 2026-09-18 "모바일로 접속하면 참가자로 고정". 호스트는 절대 안 잡고,
    ///     접속 버튼도 없다. 세션 밖이면 스스로 호스트에 붙고, 못 붙으면 잠시 뒤 다시 붙는다.
    ///   작은 IMGUI 접속 창(NetLanUI)은 컴포넌트만 끈다 — BeginClient 는 꺼져 있어도 그대로 부를 수 있다.</summary>
    [AddComponentMenu("AI-XR Crane/Flat Mode/Mobile Spectator HUD")]
    [DisallowMultipleComponent]
    public sealed class MobileSpectatorHud : MonoBehaviour
    {
        [Tooltip("화면 가장자리에서 안쪽으로 들이는 비율 — XREAL One Pro 대각 57° 시야에서 끝이 잘리지 않게.")]
        [SerializeField, Range(0f, 0.2f)] float safeInset = 0.06f;
        [Tooltip("글자 크기(참조 1920×1080 기준). 안경으로 멀리 떠 보이는 화면이라 PC HUD(24)보다 크게.")]
        [SerializeField] int fontSize = 34;
        [Tooltip("시점 버튼을 누른 뒤 그 자리까지 나는 시간(초).")]
        [SerializeField] float viewSeconds = 1.2f;
        [Tooltip("시점 버튼 — 대상을 내려다보는 각(도).")]
        [SerializeField] float viewElevationDeg = 28f;
        [Tooltip("시점 버튼 — 대상 바운즈 반지름 × 이 값만큼 떨어진다. 세로 시야 60° 에 구가 꽉 차는 거리가 2.0.")]
        [SerializeField] float viewDistanceFactor = 1.9f;
        [Tooltip("호스트에 못 붙었을 때(호스트 없음·끊김) 다시 붙어 보는 간격(초).")]
        [SerializeField] float joinRetrySeconds = 3f;

        const float RefW = 1920f, RefH = 1080f;
        const float BarH = 84f, ViewBtnW = 170f, Gap = 16f;

        Canvas canvas;
        Text roleText, statusText;
        string lastRole, lastStatus;
        float nextRefresh, nextJoinTry;

        NetLanUI net;
        FlatPlayerRig rig;
        StsCrane selected;
        string selectedLabel = "STS";

        void Start()   // 부트스트랩이 리그를 만든 뒤라 Start 에서 찾는다
        {
            EnsureEventSystem();
            net = FindAnyObjectByType<NetLanUI>();
            if (net != null) net.enabled = false;
            rig = FindAnyObjectByType<FlatPlayerRig>();

            int views = Build();
            Select(StsPartNames.StsCraneRoot, "STS", fly: false);

            bool intentOk = FlatPlayerRig.PointerIntentSelfCheck(out string why);
            QaLog.Check("MOBILE", "intent", intentOk, why);
            bool es = EventSystem.current != null;
            QaLog.Check("MOBILE", "boot", canvas != null && rig != null && rig.PointerNav && es && (net == null || !net.enabled),
                $"views={views} rig={rig != null} pointerNav={(rig != null && rig.PointerNav)} eventSystem={es} " +
                $"imguiHidden={(net == null || !net.enabled)} crane={(selected != null ? selected.name : "null")}");
        }

        // 씬에 EventSystem 이 없다(VR 은 XRI 가 따로 다룬다) — 없으면 버튼이 클릭을 못 받는다.
        //   런타임 AddComponent 는 Reset 이 안 불려 기본 액션이 비므로 AssignDefaultActions 를 직접 부른다.
        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem (mobile)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        int Build()
        {
            var canvasGo = new GameObject("MobileHudCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 1f;   // 높이 기준 — 폰(20:9)처럼 옆으로 긴 화면에서도 글자 크기가 안 줄어든다
            canvasGo.AddComponent<GraphicRaycaster>();

            float ix = RefW * safeInset, iy = RefH * safeInset;
            var root = canvasGo.transform;

            // 위 왼쪽: 역할 · 크레인
            roleText = Label(root, "Role", new Vector2(0f, 1f), new Vector2(ix, -iy), new Vector2(900f, BarH), TextAnchor.MiddleLeft, bg: true);

            // 시점 버튼 — 씬에 있는 대상만 만든다(RTG 가 한 대뿐이면 버튼도 하나)
            var views = new List<(string label, string root, bool crane)> { ("STS", StsPartNames.StsCraneRoot, true) };
            for (int i = 1; i <= 4; i++) views.Add(($"RTG{i}", $"{StsPartNames.RtgCraneRoot}_{i}", true));
            views.Add(("배", ShipConfig.ShipRootName, false));
            float x = ix; int n = 0;
            foreach (var v in views)
            {
                if (GameObject.Find(v.root) == null) continue;
                var (label, rootName) = (v.label, v.root);
                ButtonWithLabel(root, "View_" + label, new Vector2(0f, 1f), new Vector2(x, -(iy + BarH + Gap)), new Vector2(ViewBtnW, BarH),
                    () => Select(rootName, label, fly: true), new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha)).text = label;
                x += ViewBtnW + Gap; n++;
            }

            // 아래: 고른 크레인의 축·화물 한 줄
            statusText = Label(root, "Status", new Vector2(0.5f, 0f), new Vector2(0f, iy), new Vector2(RefW - 2f * ix, BarH + 12f), TextAnchor.MiddleCenter, bg: true);
            return n;
        }

        Text Label(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, TextAnchor align, bool bg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            Place(go.GetComponent<RectTransform>(), anchor, pos, size);
            if (bg)
            {
                var img = go.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha);
                img.raycastTarget = false;
            }
            return AddText(go.transform, align);
        }

        Text ButtonWithLabel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                             UnityEngine.Events.UnityAction onClick, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, worldPositionStays: false);
            Place(go.GetComponent<RectTransform>(), anchor, pos, size);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return AddText(go.transform, TextAnchor.MiddleCenter);
        }

        // 앵커 모서리에 붙이고 pivot 도 같은 모서리로 — pos 는 그 모서리에서 안쪽으로의 거리(참조 해상도 px).
        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        Text AddText(Transform parent, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20f, 6f); rt.offsetMax = new Vector2(-20f, -6f);
            var t = go.GetComponent<Text>();
            t.font = CraneHud.CreateKoreanFont(fontSize);
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = align;
            t.supportRichText = true;
            t.raycastTarget = false;
            // 넘치면 칸 밖으로 흘리지 않고 줄인다 — PC HUD 에서 안내 글이 패널 밖으로 새던 것과 같은 일을 막는다.
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(12, fontSize / 2);
            t.resizeTextMaxSize = fontSize;
            return t;
        }

        // 시점 버튼 — 크레인이면 아래 상태 줄 대상도 바꾼다(배는 대상만 보고 상태 줄은 그대로).
        void Select(string rootName, string label, bool fly)
        {
            var go = GameObject.Find(rootName);
            if (go == null) return;
            var crane = go.GetComponent<StsCrane>();
            if (crane != null) { selected = crane; selectedLabel = label; }
            if (fly && rig != null && rig.Cam != null && TryBounds(go, out Bounds b))
                rig.FlyTo(ViewEye(b), b.center, viewSeconds);
            nextRefresh = 0f;   // 바로 다시 그린다
        }

        // 지금 서 있는 쪽에서 대상을 내려다보는 자리 — 좌표를 적지 않고 대상 크기에서 거리를 낸다.
        //   대상 위에 서 있어 방향이 안 나오면 육지(−X, 안벽 가장자리가 X0) 쪽에서 본다.
        //   부두 밖으로 나가는 자리는 CranePlayerStartPlacer 가 안벽 안으로 끌어온다.
        Vector3 ViewEye(Bounds b)
        {
            Vector3 dir = rig.Cam.transform.position - b.center; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.left;
            dir.Normalize();
            float el = viewElevationDeg * Mathf.Deg2Rad;
            return b.center + (dir * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el)) * (b.extents.magnitude * viewDistanceFactor);
        }

        static bool TryBounds(GameObject go, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        // 세션 밖이면 호스트에 참가자로 붙는다 — 호스트 시작은 어디에도 없다(참가자 고정).
        //   호스트 비콘을 받았으면(LanDiscovery) 그 IP, 못 받았으면 이 기기 IP — 서버 관전 인스턴스는 호스트와 같은 기계에서 돈다.
        //   못 붙으면 NGO 가 클라이언트를 내리고(IsClient=false) 여기서 잠시 뒤 다시 붙는다 — 호스트가 나중에 떠도 알아서 들어간다.
        void EnsureParticipant()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || net == null || nm.IsClient || nm.IsServer) return;
            if (Time.unscaledTime < nextJoinTry) return;
            nextJoinTry = Time.unscaledTime + joinRetrySeconds;
            if (!net.HostDiscovered) net.JoinIp = net.LocalIp;
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
            CraneHud.SetTextIfChanged(roleText, ref lastRole, $"{role}  ·  {selectedLabel} 크레인");
            CraneHud.SetTextIfChanged(statusText, ref lastStatus, StatusBody());
        }

        string StatusBody()
        {
            if (selected == null) return $"<color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>크레인 없음</color>";
            string cargo = selected.Attach != null && selected.Attach.HasContainer
                ? $"<color=#{CraneHud.Hex(CraneHud.HudColor.Ok)}>체결 {selected.Attach.AttachedLoadTons:0.#} t</color>"
                : $"<color=#{CraneHud.Hex(CraneHud.HudColor.IdleDim)}>없음</color>";
            return $"트롤리 {Pct(selected.Trolley)}     호이스트 {Pct(selected.Spreader)}     갠트리 {Pct(selected.Gantry)}     화물 {cargo}";
        }

        static string Pct(IAxisMover axis) => axis == null ? "—" : $"{FlatHud.Percent(axis):0.0}%";
    }
}
