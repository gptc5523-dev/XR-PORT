using UnityEngine;
using UnityEngine.UI;

namespace AIXRCrane.Crane.Sts
{
    public sealed partial class CranePartPicker
    {
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
    }
}
