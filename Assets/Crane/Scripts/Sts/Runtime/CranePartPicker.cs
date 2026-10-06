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
    /// <summary>부위 선택·정보 조회(WBS 3.7) — 부품 ID(CranePartId)가 붙은 부품을 가리켜 고르면 옆에 말풍선으로
    /// 이름·ID·설명, 연결된 PLC 태그 실시간 값, 그 부품의 현재 알람을 띄운다.
    /// 입력: VR = 오른손 그립을 누른 채 가리키고 놓기 · PC 평면 = 마우스 왼쪽 클릭. 빈 곳을 고르면 해제.
    /// 부품엔 콜라이더가 없어(시각 전용) 광선 ↔ 렌더러 바운즈로 고른다 — 여러 개 맞으면 가장 작은 부품(조립체보다 구성품).
    /// 읽기 전용이라 관전자도 쓴다(네트워크 동기화 없음).</summary>
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

        sealed class Entry { public CranePartId part; public Renderer[] rends; public StsCrane crane; }

        readonly List<Entry> entries = new List<Entry>();
        readonly StringBuilder sb = new StringBuilder(256);
        readonly FaultDef[] faults = new FaultDef[8];
        Entry selected, hovered;
        Canvas canvas;
        Text text;
        LineRenderer box, leader, laser;
        string lastText;
        float nextText, nextScan;
        bool gripWas, grabbedDuringGrip;
        Transform hand;   // 오른손 컨트롤러 — 찾는 데 씬 전체를 훑으니 한 번 찾으면 둔다

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<CranePartPicker>("PartPick");

        void Start()
        {
            canvas = CraneHud.BuildPanel(transform, "PartInfo", panelPixels, worldScale,
                new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha), fontSize, Color.white, TextAnchor.UpperLeft,
                new Vector2(14, 10), out text, fitToText: true);
            canvas.enabled = false;
            box = MakeLine("PartBox", 16, 0.002f, boxColor);
            leader = MakeLine("PartLeader", 2, 0.003f, new Color(1f, 1f, 1f, 0.5f));
            laser = MakeLine("PartLaser", 2, 0.002f, boxColor);
            SelfCheck();
        }

        void Update()
        {
            if (entries.Count == 0 && Time.unscaledTime >= nextScan) Scan();
            if (entries.Count == 0) return;

            bool flat = Flat.FlatModeBootstrap.Active;
            if (flat) PcInput();
            else VrInput();
        }

        void LateUpdate()
        {
            bool show = selected != null && selected.part != null;
            canvas.enabled = show;
            box.enabled = leader.enabled = show || hovered != null;
            if (!show && hovered == null) return;

            var target = hovered ?? selected;
            if (!Union(target, out var b)) return;
            DrawBox(b);
            if (!show) { leader.enabled = false; return; }

            if (!Union(selected, out b)) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);
            Vector3 bubble = top + Vector3.up * bubbleHeight;
            Vector3 toBubble = bubble - cam.transform.position;
            float distScale = Mathf.Clamp(toBubble.magnitude / Mathf.Max(labelRefDist, 0.01f), 1f, labelMaxScale);
            canvas.transform.position = bubble;
            canvas.transform.localScale = Vector3.one * (worldScale * distScale);
            if (toBubble.sqrMagnitude > 1e-6f) canvas.transform.rotation = Quaternion.LookRotation(toBubble.normalized, Vector3.up);
            leader.SetPosition(0, top);
            leader.SetPosition(1, bubble - Vector3.up * (panelPixels.y * worldScale * 0.5f * distScale));

            if (CraneHud.Due(ref nextText, CraneHud.TextHz))
                CraneHud.SetTextIfChanged(text, ref lastText, BuildText(selected));
        }

        // ── 입력 ─────────────────────────────────────────────────────────────

        void PcInput()
        {
            if (Flat.FlatModeBootstrap.Mobile) return;   // 모바일 탭은 날기 — 이번 범위 밖
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var cam = Camera.main;
            if (cam == null) return;
            Select(Pick(cam.ScreenPointToRay(mouse.position.ReadValue())));
        }

        // 그립을 누른 동안 광선·후보 상자 표시, 놓으면 선택. 그립으로 컨테이너를 잡았으면(XRI) 고르지 않는다.
        void VrInput()
        {
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool grip = dev.isValid && dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool g) && g;
            if (grip && hand == null) hand = CraneHud.FindController("right");
            if (grip && hand != null)
            {
                var ray = new Ray(hand.position, hand.forward);
                hovered = Pick(ray);
                grabbedDuringGrip |= AnyGrabbed();
                laser.enabled = true;
                laser.SetPosition(0, ray.origin);
                laser.SetPosition(1, ray.GetPoint(hovered != null && Union(hovered, out var hb) && hb.IntersectRay(ray, out float d) ? d : vrRayLength));
            }
            else if (gripWas)
            {
                if (!grabbedDuringGrip) Select(hovered);
                hovered = null;
                grabbedDuringGrip = false;
                laser.enabled = false;
            }
            gripWas = grip;
        }

        static bool AnyGrabbed()
        {
            foreach (var g in FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
                if (g.isSelected) return true;
            return false;
        }

        void Select(Entry e)
        {
            selected = e;
            lastText = null;
            nextText = 0f;
            if (e != null) Debug.Log($"[PartPick] 선택 {e.part.id} ({e.part.name})");
        }

        // ── 고르기 ───────────────────────────────────────────────────────────

        void Scan()
        {
            nextScan = Time.unscaledTime + 1f;
            entries.Clear();
            foreach (var p in FindObjectsByType<CranePartId>(FindObjectsSortMode.None))
            {
                var r = p.GetComponentsInChildren<Renderer>();
                if (r.Length > 0) entries.Add(new Entry { part = p, rends = r, crane = p.GetComponentInParent<StsCrane>() });
            }
        }

        /// <summary>광선이 맞힌 부품 중 바운즈가 가장 작은 것(조립체보다 구성품). 없으면 null.</summary>
        Entry Pick(Ray ray)
        {
            Entry best = null;
            float bestVol = float.MaxValue;
            foreach (var e in entries)
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
            if (e == null || e.part == null) return false;
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
            string id = e.part.id;
            sb.Clear();
            string note = "";
            foreach (var p in CranePartId.Parts) if (p.id == id) { note = p.note; break; }
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

        // 상자 12모서리를 한 줄로(몇 모서리는 두 번 지난다).
        static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };
        void DrawBox(Bounds b)
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
