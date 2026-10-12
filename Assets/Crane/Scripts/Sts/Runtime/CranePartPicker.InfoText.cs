using AIXRCrane.Crane.Sts.Plc;
using UnityEngine;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CranePartPicker
    {
        // ── 정보 ─────────────────────────────────────────────────────────────

        string BuildText(Entry e)
        {
            if (e.box != null) return BoxText(e);
            string id = e.part.id;
            sb.Clear();
            var root = e.crane != null ? e.crane.transform : null;
            string note = "";
            foreach (var p in CranePartId.PartsFor(root)) if (p.id == id) { note = p.note; break; }
            sb.Append("<b>").Append(note).Append("</b>\n<color=#999999>").Append(id).Append("</color>");
            string desc = AutoDesc(root, id);
            if (desc != "") sb.Append("\n<color=#AAAAAA>").Append(desc).Append("</color>");

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
                int code = CraneLink.Syncs(crane) ? CraneLink.ActiveAlarmCode(crane) : 0;
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

        // 자동 설명(WBS 6.6)은 정적이라 크레인·부품마다 한 번만 만든다.
        readonly System.Collections.Generic.Dictionary<(Transform, string), string> descCache = new();

        string AutoDesc(Transform root, string id)
        {
            if (descCache.TryGetValue((root, id), out var d)) return d;
            var table = CranePartId.PartsFor(root);
            d = "";
            foreach (var p in table) if (p.id == id) { d = PartDescriber.Describe(root, p, table).desc; break; }
            descCache[(root, id)] = d;
            return d;
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
    }
}
