using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CranePartPicker
    {
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
            foreach (var a in CraneLink.Peers)
            {
                if (!others.TryGetValue(a, out var l) || l == null) others[a] = l = MakeLine("OtherPick", 16, 0.003f, a.Tint);
                var e = Resolve(a.SelectionKey);
                l.enabled = Union(e, out var b);
                if (!l.enabled) continue;
                l.startColor = l.endColor = a.Tint;
                DrawBox(l, b);
            }
            foreach (var a in CraneLink.Peers)
            {
                if (!otherRays.TryGetValue(a, out var ray) || ray == null) otherRays[a] = ray = MakeLine("OtherPointer", 2, 0.002f, a.Tint);
                ray.enabled = a.PointerOn;
                if (!a.PointerOn) continue;
                ray.startColor = ray.endColor = a.Tint;
                ray.SetPosition(0, a.PointerFrom);
                ray.SetPosition(1, a.PointerTo);
            }
            List<IRemotePeer> gone = null;
            foreach (var map in new[] { others, otherRays })
            {
                gone?.Clear();
                foreach (var kv in map) if (!CraneLink.Peers.Contains(kv.Key)) (gone ??= new()).Add(kv.Key);   // 나간 사람
                if (gone == null) continue;
                foreach (var k in gone)
                {
                    if (map[k] != null) { Destroy(map[k].sharedMaterial); Destroy(map[k].gameObject); }
                    map.Remove(k);
                }
            }
        }
    }
}
