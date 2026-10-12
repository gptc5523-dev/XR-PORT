using System.Collections.Generic;
using System.Text;
using AIXRCrane.Crane.Sts.Plc;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AIXRCrane.Crane.Sts
{
    /// <summary>부위·컨테이너 선택(WBS 3.7) — VR 그립 광선·모바일 탭·Enter(화면 가운데 조준점)로 골라 말풍선에 PLC 값·알람·컨테이너 정보를 띄운다(바운즈 광선, 작은 것 우선, 읽기 전용).</summary>
    [DisallowMultipleComponent]
    public sealed class CranePartPicker : MonoBehaviour
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
        readonly Dictionary<Net.PlayerAvatarSync, LineRenderer> others = new();   // 남의 선택 상자(그 사람 색)
        Net.PlayerAvatarSync[] avatars = System.Array.Empty<Net.PlayerAvatarSync>();
        float nextAvatarScan;

        /// <summary>내가 고른 것 — 부품 '크레인|부품ID', 컨테이너 'C'+ContainerRegistry 번호, 없으면 "". 남에게 보낸다(PlayerAvatarSync.Selection).</summary>
        public static string SelectionKey { get; private set; } = "";

        /// <summary>내가 가리키는 곳 — VR 은 그립 중 손 레이저, 안경은 조준점이 보일 때 시선. 남에게 보낸다(PlayerAvatarSync.Pointer).</summary>
        public static bool PointerOn { get; private set; }
        public static Vector3 PointerFrom { get; private set; }
        public static Vector3 PointerTo { get; private set; }
        float nextPointer;
        readonly Dictionary<Net.PlayerAvatarSync, LineRenderer> otherRays = new();   // 남의 포인터 선(그 사람 색)

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

            bool flat = Flat.FlatModeBootstrap.Active;
            if (flat) PcInput();
            else VrInput();
        }

        void LateUpdate()
        {
            DrawOthers();
            var head = Flat.XrealHeadTracker.Instance;
            reticle.enabled = Flat.FlatModeBootstrap.Active && (reticleOn || (head != null && head.Connected));
            if (Flat.FlatModeBootstrap.Active && Time.unscaledTime >= nextPointer) { nextPointer = Time.unscaledTime + 0.1f; GazePointer(reticle.enabled); }
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

        // ── 입력 ─────────────────────────────────────────────────────────────

        void PcInput()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            {
                reticleOn = true;
                var c = CraneHud.HudCamera(null);
                if (c != null) Select(Pick(c.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))));
                return;
            }
            TapInput();
        }

        // 모바일: 짧게 탭(누름 < TapMaxS, 이동 < tapSlopPx)이면 선택 — 길게 누르면 비행(FlatPlayerRig), 버튼 위 누름은 무시.
        void TapInput()
        {
            var p = Pointer.current;
            if (p == null) return;
            if (p.press.wasPressedThisFrame)
            {
                tapAt = Time.unscaledTime; tapPos = p.position.ReadValue();
                tapOnUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            }
            if (!p.press.wasReleasedThisFrame || tapOnUi) return;
            Vector2 pos = p.position.ReadValue();
            if (Time.unscaledTime - tapAt > Flat.FlatPlayerRig.TapMaxS || (pos - tapPos).magnitude > tapSlopPx) return;
            var cam = CraneHud.HudCamera(null);
            if (cam != null) Select(Pick(cam.ScreenPointToRay(pos)));
        }

        // 그립을 누른 동안 광선·후보 상자 표시, 놓으면 선택. 그립으로 컨테이너를 잡았으면(XRI) 그 컨테이너를 고른다.
        void VrInput()
        {
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool grip = dev.isValid && dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool g) && g;
            if (grip && hand == null) hand = CraneHud.FindController("right");
            if (grip && hand != null)
            {
                var ray = new Ray(hand.position, hand.forward);
                hovered = Pick(ray);
                if (grabbedDuringGrip == null) grabbedDuringGrip = AnyGrabbed();
                laser.enabled = true;
                laser.SetPosition(0, ray.origin);
                laser.SetPosition(1, ray.GetPoint(hovered != null && Union(hovered, out var hb) && hb.IntersectRay(ray, out float d) ? d : vrRayLength));
                PointerOn = true; PointerFrom = ray.origin; PointerTo = laser.GetPosition(1);
            }
            else if (gripWas)
            {
                Select(grabbedDuringGrip != null ? BoxEntry(grabbedDuringGrip) : hovered);
                hovered = null;
                grabbedDuringGrip = null;
                laser.enabled = false;
                PointerOn = false;
            }
            gripWas = grip;
        }

        static Transform AnyGrabbed()
        {
            foreach (var g in FindObjectsByType<XRGrabInteractable>())
                if (g.isSelected) return g.transform;
            return null;
        }

        Entry BoxEntry(Transform t)
        {
            foreach (var e in boxes) if (e.box == t) return e;
            var r = t.GetComponentsInChildren<Renderer>();
            return r.Length > 0 ? new Entry { box = t, rends = r } : null;
        }

        void Select(Entry e)
        {
            selected = e;
            SelectionKey = KeyOf(e);
            lastText = null;
            nextText = 0f;
            if (e != null) Debug.Log(e.box != null ? $"[PartPick] 선택 컨테이너 {SpreaderAttach.Identify(e.box, out _)} ({e.box.name})" : $"[PartPick] 선택 {e.part.id} ({e.part.name})");
        }

        // ── 고르기 ───────────────────────────────────────────────────────────

        void Scan()
        {
            nextScan = Time.unscaledTime + 1f;
            entries.Clear();
            foreach (var p in FindObjectsByType<CranePartId>())
            {
                var r = p.GetComponentsInChildren<Renderer>();
                if (r.Length > 0) entries.Add(new Entry { part = p, rends = r, crane = p.GetComponentInParent<StsCrane>() });
            }
        }

        // 컨테이너는 생기고 사라지니(스폰·재생) 2초마다 다시 모은다.
        void ScanBoxes()
        {
            nextBoxScan = Time.unscaledTime + 2f;
            boxes.Clear();
            foreach (var rb in FindObjectsByType<Rigidbody>())
            {
                if (rb.name.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var r = rb.GetComponentsInChildren<Renderer>();
                if (r.Length > 0) boxes.Add(new Entry { box = rb.transform, rends = r });
            }
        }

        /// <summary>광선이 맞힌 부품·컨테이너 중 바운즈가 가장 작은 것(조립체보다 구성품). 없으면 null.</summary>
        Entry Pick(Ray ray)
        {
            Entry best = null;
            float bestVol = float.MaxValue;
            foreach (var list in new[] { entries, boxes })
                foreach (var e in list)
                {
                    if (!Union(e, out var b) || !b.IntersectRay(ray)) continue;
                    float vol = b.size.x * b.size.y * b.size.z;
                    if (vol < bestVol) { bestVol = vol; best = e; }
                }
            return best;
        }

        static bool Union(Entry e, out Bounds b)
        {
            b = default;
            if (e == null || (e.part == null && e.box == null)) return false;
            bool any = false;
            foreach (var r in e.rends)
            {
                if (r == null || !r.enabled) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return any;
        }

        // ── 정보 ─────────────────────────────────────────────────────────────

        string BuildText(Entry e)
        {
            if (e.box != null) return BoxText(e);
            string id = e.part.id;
            sb.Clear();
            string note = "";
            foreach (var p in CranePartId.PartsFor(e.crane != null ? e.crane.transform : null)) if (p.id == id) { note = p.note; break; }
            sb.Append("<b>").Append(note).Append("</b>\n<color=#999999>").Append(id).Append("</color>");

            var crane = e.crane;
            PlcBridge bridge = null;
            if (crane != null)
                foreach (var br in crane.GetComponents<PlcBridge>())
                    if (br.isActiveAndEnabled && br.Active) { bridge = br; break; }
            var s = bridge != null ? bridge.Latest : default;

            bool header = false;
            foreach (var t in CranePartId.Tags)
            {
                if (!Related(t.part, id)) continue;
                if (!header) { sb.Append(bridge != null ? "\n<color=#5FE0FF>PLC</color>" : "\n<color=#5FE0FF>PLC 태그 (PLC 없음 — 3D 값)</color>"); header = true; }
                sb.Append("\n").Append(t.tag).Append("  <b>")
                  .Append(bridge != null ? PlcValue(t.tag, s) : ModelValue(t.tag, crane)).Append("</b>");
            }

            bool any = false;
            if (crane != null && PortDemoDirector.Spectator)
            {
                // 관전자는 그랩·충돌 판정이 꺼져 있다 — 호스트가 보낸 알람(최고 심각도 1건)만 믿는다.
                int code = Net.CraneNetSync.Syncs(crane) ? Net.CraneNetSync.ActiveAlarmCode(crane) : 0;
                var f = code != 0 ? CraneFault.FromCodebook(code) : default;
                if (f.IsValid) any |= AppendAlarm(f.Code, f.Sev, CraneFault.Format(f), id);
            }
            else
            {
                int n = crane != null ? CraneFault.EvaluateAll(crane, faults) : 0;
                for (int i = 0; i < n; i++) any |= AppendAlarm(faults[i].Code, faults[i].Sev, CraneFault.Format(faults[i]), id);
            }
            if (bridge != null && s.AlarmActive)
            {
                var a = AlarmCodebook.Get(s.AlarmCode);
                if (a != null) any |= AppendAlarm(a.code, a.Severity, a.ToString(), id);
            }
            if (!any) sb.Append("\n<color=#7FFF7F>알람 없음</color>");
            return sb.ToString();
        }

        string BoxText(Entry e)
        {
            var c = e.box;
            Union(e, out var b);
            string id = SpreaderAttach.Identify(c, out float tons);
            float k = 1f / StsConfig.ModelScale;   // 월드 바운즈 → 실척 m
            float len = Mathf.Max(b.size.x, b.size.z) * k, hgt = b.size.y * k;
            string holder = null;
            foreach (var cr in FindObjectsByType<StsCrane>())
                if (cr.Attach != null && cr.Attach.AttachedContainer == c) { holder = cr.name; id = cr.Attach.AttachedDisplayId ?? id; break; }
            var g = ContainerLoad.Grade(tons);
            sb.Clear();
            sb.Append("<b>컨테이너 ").Append(id).Append("</b>");
            sb.Append("\n규격  <b>").Append(len > 13f ? "45ft" : len > 9f ? "40ft" : "20ft").Append(hgt > 2.75f ? " 하이큐브" : "").Append("</b>");
            sb.Append("\n하중  <b>").Append(tons.ToString("F1")).Append(" t</b>  <color=#")
              .Append(CraneHud.Hex(ContainerLoad.GradeColor(g))).Append(">").Append(ContainerLoad.GradeLabel(g)).Append("</color>");
            sb.Append("\n상태  <b>").Append(holder != null ? holder + " 스프레더에 체결" : (c.GetComponent<Rigidbody>() is Rigidbody rb && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 1e-4f ? "이동 중" : "적치")).Append("</b>");
            return sb.ToString();
        }

        bool AppendAlarm(int code, FaultSeverity sev, string line, string id)
        {
            var a = AlarmCodebook.Get(code);
            if (a == null || !Related(a.part, id)) return false;
            sb.Append("\n<color=#").Append(CraneHud.Hex(CraneFault.SevColor(sev))).Append(">⚠ ").Append(line).Append("</color>");
            return true;
        }

        /// <summary>태그·알람의 부품 ID 가 고른 부품과 관련 있는가 — 같거나, 상위(조립체·그룹)거나, 하위(구성품).</summary>
        public static bool Related(string partOf, string selectedId) =>
            !string.IsNullOrEmpty(partOf) &&
            (partOf == selectedId || selectedId.StartsWith(partOf + ".") || partOf.StartsWith(selectedId + "."));

        static string OnOff(bool b) => b ? "예" : "아니오";

        static string PlcValue(string tag, in PlcSnapshot s) => tag switch
        {
            "GT_Position" => $"{s.GtPosition:F2} m", "GT_Velocity" => $"{s.GtVelocity:F2} m/s",
            "GT_Running" => OnOff(s.GtRunning), "GT_Direction" => s.GtDirStern ? "Stern" : "Bow",
            "TR_Position" => $"{s.TrPosition:F2} m", "TR_Velocity" => $"{s.TrVelocity:F2} m/s",
            "TR_Running" => OnOff(s.TrRunning), "TR_Direction" => s.TrDirSea ? "Sea" : "Land",
            "HO_Position" => $"{s.HoPosition:F2} m", "HO_Velocity" => $"{s.HoVelocity:F2} m/s",
            "HO_Running" => OnOff(s.HoRunning), "HO_Direction" => s.HoDirUp ? "Up" : "Down",
            "HO_Load" => $"{s.HoLoad:F1} t", "SP_Mode" => s.SpMode.ToString(),
            "SP_TwistLock_Locked" => OnOff(s.TwistLockLocked), "SP_TwistLock_Unlocked" => OnOff(s.TwistLockUnlocked),
            "SP_Landed" => OnOff(s.Landed), "SP_Container_Detected" => OnOff(s.ContainerDetected),
            "OP_Mode" => s.ControlMode.ToString(), "OP_Ready" => OnOff(s.OpReady), "OP_Running" => OnOff(s.OpRunning),
            "OP_Standby" => OnOff(s.OpStandby), "OP_Emergency_Stop" => OnOff(s.EmergencyStop),
            "ENV_Wind_Speed" => $"{s.WindSpeed:F1} m/s", "ENV_Wind_Direction" => $"{s.WindDirection:F0}°",
            "ENV_Wind_Alarm" => OnOff(s.WindAlarm),
            _ => "--",
        };

        // PLC 가 없을 때 3D 상태로 대신 보여줄 수 있는 태그만 — 나머지는 '--'.
        static string ModelValue(string tag, StsCrane c)
        {
            if (c == null) return "--";
            switch (tag)
            {
                case "GT_Position": return c.Gantry != null ? $"{c.Gantry.PlcMeters():F2} m" : "--";
                case "TR_Position": return c.Trolley != null ? $"{c.Trolley.PlcMeters():F2} m" : "--";
                case "HO_Position": return c.Spreader != null ? $"{c.Spreader.PlcMeters():F2} m" : "--";
                case "HO_Load": return c.Attach != null ? $"{c.Attach.AttachedLoadTons:F1} t" : "--";
                case "SP_Container_Detected": return c.Attach != null ? OnOff(c.Attach.HasContainer) : "--";
                case "SP_TwistLock_Locked":
                case "SP_TwistLock_Unlocked":
                    var la = c.GetComponentInChildren<SpreaderLockAnimator>(true);
                    return la != null ? OnOff(la.Locked == (tag == "SP_TwistLock_Locked")) : "--";
                case "SP_Landed":
                    var gr = c.GetComponent<SpreaderGrabber>();
                    return gr != null ? OnOff(gr.IsLanded) : "--";
                case "SP_Mode":
                    var tele = c.GetComponentInChildren<SpreaderTelescope>(true);
                    return tele != null ? (tele.Is40 ? PlcSpreaderMode.FortyFt : PlcSpreaderMode.TwentyFt).ToString() : "--";
                case "ENV_Wind_Speed": return $"{CraneSway.WindMps:F1} m/s";
                case "ENV_Wind_Direction": return $"{CraneSway.WindFromDeg:F0}°";
                default: return "--";
            }
        }

        // ── 남의 선택 ─────────────────────────────────────────────────────────

        // 안경 — 조준점이 보이는 동안 시선 광선이 맞힌 곳(없으면 광선 끝)을 가리킨다.
        void GazePointer(bool on)
        {
            var cam = on ? CraneHud.HudCamera(null) : null;
            if (cam == null) { PointerOn = false; return; }
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var e = Pick(ray);
            PointerOn = true; PointerFrom = ray.origin;
            PointerTo = ray.GetPoint(e != null && Union(e, out var b) && b.IntersectRay(ray, out float d) ? d : vrRayLength);
        }

        static string KeyOf(Entry e)
        {
            if (e == null) return "";
            if (e.box != null) { int i = ContainerRegistry.IndexOf(e.box); return i >= 0 ? "C" + i : ""; }
            return e.part != null && e.crane != null ? e.crane.name + "|" + e.part.id : "";
        }

        Entry Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key[0] == 'C' && int.TryParse(key.Substring(1), out int i)) { var t = ContainerRegistry.At(i); return t != null ? BoxEntry(t) : null; }
            int bar = key.IndexOf('|');
            if (bar < 0) return null;
            string crane = key.Substring(0, bar), id = key.Substring(bar + 1);
            foreach (var e in entries) if (e.crane != null && e.crane.name == crane && e.part.id == id) return e;
            return null;
        }

        // 세션의 다른 참가자가 고른 것을 그 사람 식별색 상자로 — 같은 것을 보며 이야기할 수 있게(계획서 그림 5 '동일한 선택 표시').
        void DrawOthers()
        {
            if (Time.unscaledTime >= nextAvatarScan) { nextAvatarScan = Time.unscaledTime + 1f; avatars = FindObjectsByType<Net.PlayerAvatarSync>(); }   // 씬 전체 검색은 1초에 한 번
            foreach (var a in avatars)
            {
                if (a == null) continue;
                if (a.IsOwner || !a.IsSpawned) continue;
                if (!others.TryGetValue(a, out var l) || l == null) others[a] = l = MakeLine("OtherPick", 16, 0.003f, a.TintColor);
                var e = Resolve(a.Selection.Value.ToString());
                l.enabled = Union(e, out var b);
                if (!l.enabled) continue;
                l.startColor = l.endColor = a.TintColor;
                DrawBox(l, b);
            }
            foreach (var a in avatars)
            {
                if (a == null || a.IsOwner || !a.IsSpawned) continue;
                if (!otherRays.TryGetValue(a, out var ray) || ray == null) otherRays[a] = ray = MakeLine("OtherPointer", 2, 0.002f, a.TintColor);
                var p = a.Pointer.Value;
                ray.enabled = p.On;
                if (!p.On) continue;
                ray.startColor = ray.endColor = a.TintColor;
                ray.SetPosition(0, p.From);
                ray.SetPosition(1, p.To);
            }
            List<Net.PlayerAvatarSync> gone = null;
            foreach (var map in new[] { others, otherRays })
            {
                gone?.Clear();
                foreach (var kv in map) if (kv.Key == null || !kv.Key.IsSpawned) (gone ??= new()).Add(kv.Key);
                if (gone == null) continue;
                foreach (var k in gone)
                {
                    if (map[k] != null) { Destroy(map[k].sharedMaterial); Destroy(map[k].gameObject); }
                    map.Remove(k);
                }
            }
        }

        // ── 그리기 ───────────────────────────────────────────────────────────

        LineRenderer MakeLine(string name, int n, float width, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            var sh = Shader.Find(CraneHud.OverlayShader) ?? Shader.Find("Sprites/Default");
            if (sh != null) l.material = new Material(sh);
            l.useWorldSpace = true;
            l.positionCount = n;
            l.startWidth = l.endWidth = width;
            l.startColor = l.endColor = c;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            l.enabled = false;
            return l;
        }

        // 화면 가운데 조준점 — 리모컨 '선택'(Enter)이 고르는 자리. 안경 머리 추적이 붙었거나 Enter 를 한 번 쓰면 보인다.
        Canvas MakeReticle()
        {
            var go = new GameObject("PickReticle");
            go.transform.SetParent(transform, false);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 500;
            var t = new GameObject("Dot").AddComponent<Text>();
            t.transform.SetParent(go.transform, false);
            t.font = CraneHud.CreateKoreanFont(40);
            t.fontSize = 40;
            t.text = "+";
            t.alignment = TextAnchor.MiddleCenter;
            t.color = boxColor;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(60f, 60f);
            cv.enabled = false;
            return cv;
        }

        // 상자 12모서리를 한 줄로(몇 모서리는 두 번 지난다).
        static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };
        void DrawBox(Bounds b) => DrawBox(box, b);

        static void DrawBox(LineRenderer box, Bounds b)
        {
            Vector3 mn = b.min, mx = b.max;
            for (int i = 0; i < BoxPath.Length; i++)
            {
                int k = BoxPath[i];
                float x = (k == 1 || k == 2 || k == 5 || k == 6) ? mx.x : mn.x;
                float z = (k == 2 || k == 3 || k == 6 || k == 7) ? mx.z : mn.z;
                box.SetPosition(i, new Vector3(x, k < 4 ? mn.y : mx.y, z));
            }
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
