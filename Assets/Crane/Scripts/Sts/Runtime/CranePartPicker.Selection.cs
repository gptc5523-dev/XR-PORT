using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CranePartPicker
    {
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
            if (Time.unscaledTime - tapAt > CraneLink.TapMaxS || (pos - tapPos).magnitude > tapSlopPx) return;
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
    }
}
