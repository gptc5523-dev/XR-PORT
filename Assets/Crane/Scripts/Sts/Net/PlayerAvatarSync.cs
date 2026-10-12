using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>참가자 아바타 — 자기 XR 리그를 따라다니며(위치·회전은 NetworkTransform) 남에게 보인다.
    /// 메시는 발바닥이 원점이어야 하고, 리그가 1/24 로 축소돼 있어 프리팹 루트에 ModelScale 이 필요하다(NetLanSetup 이 넣는다).</summary>
    [AddComponentMenu("AI-XR Crane/Net/Player Avatar Sync")]
    [DisallowMultipleComponent]
    public sealed class PlayerAvatarSync : NetworkBehaviour
    {
        [Tooltip("메시가 들어갈 자리. 비어 있어도 동작한다 — 위치 동기화는 그대로 돌고 화면에만 안 보인다.")]
        [SerializeField] Transform body;

        /// <summary>기본 식별색 — 세션 밖이라 번호가 없을 때도 쓴다.</summary>
        public static readonly Color[] DefaultPalette =
        {
            new Color(1.00f, 0.72f, 0.10f),   // 호박
            new Color(0.20f, 0.78f, 0.95f),   // 하늘
            new Color(0.45f, 0.85f, 0.30f),   // 연두
            new Color(0.95f, 0.35f, 0.55f),   // 분홍
            new Color(1.00f, 0.45f, 0.15f),   // 주황
        };

        [Tooltip("참가자 식별색. 인원(NetConfig.MaxPlayers)만큼 필요하다.")]
        [SerializeField] Color[] palette = (Color[])DefaultPalette.Clone();

        /// <summary>이름표 문구·식별색(#RRGGBB) — 주인이 등록 정보로 쓰고 모두가 본다.</summary>
        public readonly NetworkVariable<Unity.Collections.FixedString128Bytes> Label = new(writePerm: NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Unity.Collections.FixedString32Bytes> colorHex = new(writePerm: NetworkVariableWritePermission.Owner);
        /// <summary>이 참가자가 고른 것(CranePartPicker.SelectionKey) — 남의 화면에 이 사람 색 상자로 보인다. 빈 값 = 없음.</summary>
        public readonly NetworkVariable<Unity.Collections.FixedString64Bytes> Selection = new(writePerm: NetworkVariableWritePermission.Owner);

        readonly List<Renderer> rends = new();
        Canvas nameTag;
        UnityEngine.UI.Text tagText;
        Transform rig;
        Camera cam;

        /// <summary>이 아바타의 식별색 — HUD·이름표에서 같은 색을 쓰고 싶을 때.</summary>
        public Color TintColor =>
            ColorUtility.TryParseHtmlString(colorHex.Value.ToString(), out var c) ? c
            : palette.Length == 0 ? Color.white : palette[(int)(OwnerClientId % (ulong)palette.Length)];

        /// <summary>참가자 목록 문구 — 방 안 아바타(없으면 나 혼자), 식별색 ● + 등록 정보. 모바일·VR 목록 공용.</summary>
        public static string ListText()
        {
            var rows = new List<string>();
            foreach (var a in FindObjectsByType<PlayerAvatarSync>())
                if (a.IsSpawned && a.Label.Value.Length > 0) rows.Add(Row(a.TintColor, a.Label.Value.ToString()));
            if (rows.Count == 0 && DeviceIdentity.Looked)
            {
                var me = DeviceIdentity.Me;
                var tint = me != null && !string.IsNullOrEmpty(me.color) && ColorUtility.TryParseHtmlString(me.color, out var c) ? c : DefaultPalette[0];
                rows.Add(Row(tint, DeviceIdentity.Label));
            }
            return rows.Count == 0 ? "" : $"<b>참가자 {rows.Count}</b>\n" + string.Join("\n", rows);
        }

        static string Row(Color tint, string raw) => $"<color=#{AIXRCrane.Crane.Sts.CraneHud.Hex(tint)}>●</color> {DeviceIdentity.LineText(raw)}";

        public override void OnNetworkSpawn()
        {
            CollectRenderers();
            ApplyTint();
            // 자기 아바타는 1인칭이라 숨긴다. 남의 아바타는 보여야 한다.
            SetVisible(!IsOwner);
            Label.OnValueChanged += (_, _) => RefreshTag();
            colorHex.OnValueChanged += (_, _) => { ApplyTint(); RefreshTag(); };
            if (IsServer) Selection.OnValueChanged += (_, v) => SessionEventLog.Record("select", OwnerClientId, v.ToString());   // 선택 이력(빈 값 = 선택 해제)
            if (!IsOwner)
            {
                RefreshTag();
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += FaceCamera;
            }
        }

        public override void OnNetworkDespawn() => UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= FaceCamera;

        // 그리는 카메라마다 이름표를 그쪽으로 — Camera.main 이 보는 카메라와 다르면 글자가 뒤집혀 보였다. 좌우로만 돌려 서 있게.
        void FaceCamera(UnityEngine.Rendering.ScriptableRenderContext _, Camera c)
        {
            if (nameTag == null) return;
            var d = Vector3.ProjectOnPlane(nameTag.transform.position - c.transform.position, Vector3.up);
            if (d.sqrMagnitude > 1e-8f) nameTag.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        // 남의 아바타 머리 위 이름표 — 흰 바탕 검정 글자. 내 것은 안 만든다(1인칭).
        void RefreshTag()
        {
            if (IsOwner) return;
            if (nameTag == null)
            {
                // 큰 글꼴을 작게 줄여 또렷하게(글자 높이 약 0.1 m).
                nameTag = AIXRCrane.Crane.Sts.CraneHud.BuildPanel(transform, "NameTag", new Vector2(1000f, 150f), 0.0016f,
                    new Color(1f, 1f, 1f, 0.85f), 64, Color.black, TextAnchor.MiddleCenter,
                    new Vector2(30, 15), out tagText, fitToText: true);
                // 공용 HUD 의 검정 외곽선·그림자가 검정 글자를 뭉갠다 — 흰 바탕이라 뺀다.
                foreach (var fx in tagText.GetComponents<UnityEngine.UI.Shadow>()) Destroy(fx);
                nameTag.transform.localPosition = Vector3.up * TagHeight;
            }
            tagText.text = DeviceIdentity.TagText(Label.Value.ToString());
        }

        const float TagHeight = 2.1f;   // 리그 로컬(실척 m) — 리그가 1/24 로 줄어 있어 머리 위가 된다

        void CollectRenderers()
        {
            rends.Clear();
            var root = body != null ? body : transform;
            root.GetComponentsInChildren(true, rends);
        }

        void ApplyTint()
        {
            // 프로퍼티 블록 — sharedMaterial 은 에셋을 오염시키고, r.material 은 아바타가 나갈 때마다 인스턴스가 남았다.
            var mpb = new MaterialPropertyBlock();
            var c = TintColor;
            foreach (var r in rends)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                r.SetPropertyBlock(mpb);
            }
        }

        void SetVisible(bool on)
        {
            foreach (var r in rends)
                if (r != null) r.enabled = on;
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner) return;
            string sel = AIXRCrane.Crane.Sts.CranePartPicker.SelectionKey;
            if (Selection.Value.ToString() != sel) Selection.Value = sel;
            if (DeviceIdentity.Looked && Label.Value.Length == 0)
            {
                Label.Value = DeviceIdentity.Label;
                colorHex.Value = DeviceIdentity.Me?.color ?? "";
                ApplyTint();
            }

            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
                rig = cam.transform.root;      // XR Origin 리그 루트 — CranePlayerStartPlacer 와 같은 규약
            }
            if (rig == null) return;

            transform.position = rig.position;
            // yaw 만 — 고개를 숙여도 몸은 세워둔다.
            transform.rotation = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
        }
    }
}
