using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>부위·컨테이너 선택(WBS 3.7) — VR 그립 광선·모바일 탭·Enter(화면 가운데 조준점)로 골라 말풍선에 PLC 값·알람·컨테이너 정보를 띄운다(바운즈 광선, 작은 것 우선, 읽기 전용).</summary>
    [DisallowMultipleComponent]
    public sealed partial class CranePartPicker : MonoBehaviour
    {
        [SerializeField] float worldScale = 0.0009f;   // CranePartLabels 와 같은 말풍선 크기
        [SerializeField] Vector2 panelPixels = new Vector2(420f, 200f);
        [SerializeField] int fontSize = 24;
        [SerializeField] float bubbleHeight = 0.08f;   // 부품 바운즈 윗면 위로 띄우는 높이(월드)
        [SerializeField] float labelRefDist = 2.5f, labelMaxScale = 3.5f;
        [SerializeField] float vrRayLength = 30f;      // 월드 — 미니어처라 실척 720m
        [SerializeField] Color boxColor = new Color(0.373f, 0.878f, 1f, 0.9f);   // HudColor.Accent

        sealed class Entry { public CranePartId part; public Transform box; public Renderer[] rends; public StsCrane crane; }
        float tapAt; Vector2 tapPos; bool tapOnUi;
        [Tooltip("모바일 탭 판정 — 누른 자리에서 이 픽셀 넘게 움직이면 탭이 아니다.")]
        [SerializeField] float tapSlopPx = 12f;

        readonly List<Entry> entries = new List<Entry>();
        readonly List<Entry> boxes = new List<Entry>();
        float nextBoxScan;
        Canvas reticle;
        bool reticleOn;
        readonly StringBuilder sb = new StringBuilder(256);
        readonly FaultDef[] faults = new FaultDef[8];
        Entry selected, hovered;
        Canvas canvas;
        Text text;
        LineRenderer box, laser;
        string lastText;
        float nextText, nextScan;
        bool gripWas;
        Transform grabbedDuringGrip;   // 그립 중 잡힌 컨테이너 — 놓을 때 부품 대신 이것을 고른다
        Transform hand;   // 오른손 컨트롤러 — 찾는 데 씬 전체를 훑으니 한 번 찾으면 둔다
        readonly Dictionary<IRemotePeer, LineRenderer> others = new();   // 남의 선택 상자(그 사람 색)

        /// <summary>내가 고른 것 — 부품 '크레인|부품ID', 컨테이너 'C'+ContainerRegistry 번호, 없으면 "". 남에게 보낸다(PlayerAvatarSync.Selection).</summary>
        public static string SelectionKey { get; private set; } = "";

        /// <summary>내가 가리키는 곳 — VR 은 그립 중 손 레이저, 안경은 조준점이 보일 때 시선. 남에게 보낸다(PlayerAvatarSync.Pointer).</summary>
        public static bool PointerOn { get; private set; }
        public static Vector3 PointerFrom { get; private set; }
        public static Vector3 PointerTo { get; private set; }
        float nextPointer;
        readonly Dictionary<IRemotePeer, LineRenderer> otherRays = new();   // 남의 포인터 선(그 사람 색)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePartPicker>("PartPick");

        void OnDestroy()
        {
            foreach (var l in new[] { box, laser })
                if (l != null && l.sharedMaterial != null) Destroy(l.sharedMaterial);   // MakeLine 이 만든 인스턴스
            foreach (var l in others.Values)
                if (l != null && l.sharedMaterial != null) Destroy(l.sharedMaterial);
            foreach (var l in otherRays.Values)
                if (l != null && l.sharedMaterial != null) Destroy(l.sharedMaterial);
            SelectionKey = "";
            PointerOn = false;
        }

        void Start()
        {
            canvas = CraneHud.BuildPanel(transform, "PartInfo", panelPixels, worldScale,
                new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha), fontSize, Color.white, TextAnchor.UpperLeft,
                new Vector2(14, 10), out text, fitToText: true);
            canvas.enabled = false;
            box = MakeLine("PartBox", 16, 0.002f, boxColor);
            laser = MakeLine("PartLaser", 2, 0.002f, boxColor);
            reticle = MakeReticle();
            SelfCheck();
        }

        void Update()
        {
            if (entries.Count == 0 && Time.unscaledTime >= nextScan) Scan();
            if (entries.Count == 0) return;
            if (Time.unscaledTime >= nextBoxScan) ScanBoxes();

            bool flat = CraneLink.FlatMode;
            if (flat) PcInput();
            else VrInput();
        }

        void LateUpdate()
        {
            DrawOthers();
            reticle.enabled = CraneLink.FlatMode && (reticleOn || CraneLink.GlassesConnected());
            if (CraneLink.FlatMode && Time.unscaledTime >= nextPointer) { nextPointer = Time.unscaledTime + 0.1f; GazePointer(reticle.enabled); }
            bool show = Union(selected, out _);
            canvas.enabled = show;
            box.enabled = show || hovered != null;
            if (!show && hovered == null) return;

            var target = hovered ?? selected;
            if (!Union(target, out var b)) return;
            DrawBox(b);
            if (!show) return;

            if (!Union(selected, out b)) return;
            var cam = CraneHud.HudCamera(null);
            if (cam == null) return;
            Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);
            Vector3 bubble = top + Vector3.up * bubbleHeight;
            Vector3 toBubble = bubble - cam.transform.position;
            float distScale = Mathf.Clamp(toBubble.magnitude / Mathf.Max(labelRefDist, 0.01f), 1f, labelMaxScale);
            canvas.transform.position = bubble;
            canvas.transform.localScale = Vector3.one * (worldScale * distScale);
            if (toBubble.sqrMagnitude > 1e-6f) canvas.transform.rotation = Quaternion.LookRotation(toBubble.normalized, Vector3.up);

            if (CraneHud.Due(ref nextText, CraneHud.TextHz))
                CraneHud.SetTextIfChanged(text, ref lastText, BuildText(selected));
        }

        // 자체 점검 — 각 부품 중심을 위에서 내리꽂는 광선은 그 부품이나 더 작은 부품을 골라야 한다(가장 작은 것 우선 규칙).
        void SelfCheck()
        {
            Scan();
            if (entries.Count == 0) return;
            int self = 0, bad = 0;
            foreach (var e in entries)
            {
                if (!Union(e, out var b)) continue;
                var got = Pick(new Ray(b.center + Vector3.up * 100f, Vector3.down));
                if (got == e) self++;
                else if (got == null || !Union(got, out var gb) || gb.size.x * gb.size.y * gb.size.z > b.size.x * b.size.y * b.size.z) bad++;
            }
            QaLog.Check("PICK", "selfcheck", bad == 0, $"parts={entries.Count} self={self} smaller={entries.Count - self - bad} bad={bad}");
        }
    }
}
