using UnityEngine;
using UnityEngine.UI;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>VR 참가자 목록 — 모바일 관전 화면과 같은 목록을 시야 좌상단(성능 HUD 자리)에 붙인다. 평면 모드에선 안 띄운다.</summary>
    [DisallowMultipleComponent]
    public sealed class VrParticipantsHud : MonoBehaviour
    {
        [SerializeField] Vector3 hmdOffset = new Vector3(-0.22f, 0.09f, CraneHud.HudDistance);
        [SerializeField, Range(-30f, 30f)] float tiltYawDeg = 15f;
        [SerializeField, Range(-30f, 30f)] float tiltPitchDeg = 8f;
        [SerializeField] float worldScale = 0.00075f;
        [SerializeField] int fontSize = 20;

        Canvas canvas;
        Text text;
        string last;
        float next;
        bool attached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<VrParticipantsHud>("VrWho");

        void Start()
        {
            canvas = CraneHud.BuildPanel(transform, "VrParticipants", new Vector2(420f, 160f), worldScale,
                new Color(0f, 0f, 0f, CraneHud.PanelBgAlpha), fontSize, Color.white, TextAnchor.UpperLeft,
                new Vector2(14, 10), out text, fitToText: true);
            canvas.enabled = false;
        }

        void LateUpdate()
        {
            if (canvas == null || !CraneHud.Due(ref next, CraneHud.TextHz)) return;
            if (Flat.FlatModeBootstrap.Active) { canvas.enabled = false; return; }
            if (!attached)
            {
                var cam = CraneHud.HudCamera(null);
                if (cam == null) return;
                CraneHud.AttachHeadLocked(canvas.transform, cam, hmdOffset, tiltPitchDeg, tiltYawDeg);
                attached = true;
            }
            string body = PlayerAvatarSync.ListText();
            canvas.enabled = body.Length > 0;
            CraneHud.SetTextIfChanged(text, ref last, body);
        }
    }
}
