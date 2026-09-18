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

        [Tooltip("참가자 식별색. 인원(NetConfig.MaxPlayers)만큼 필요하다.")]
        [SerializeField] Color[] palette =
        {
            new Color(1.00f, 0.72f, 0.10f),   // 호박
            new Color(0.20f, 0.78f, 0.95f),   // 하늘
            new Color(0.45f, 0.85f, 0.30f),   // 연두
            new Color(0.95f, 0.35f, 0.55f),   // 분홍
            new Color(1.00f, 0.45f, 0.15f),   // 주황
        };

        readonly List<Renderer> rends = new();
        Transform rig;
        Camera cam;

        /// <summary>이 아바타의 식별색 — HUD·이름표에서 같은 색을 쓰고 싶을 때.</summary>
        public Color TintColor =>
            palette.Length == 0 ? Color.white : palette[(int)(OwnerClientId % (ulong)palette.Length)];

        public override void OnNetworkSpawn()
        {
            CollectRenderers();
            ApplyTint();
            // 자기 아바타는 1인칭이라 숨긴다. 남의 아바타는 보여야 한다.
            SetVisible(!IsOwner);
        }

        void CollectRenderers()
        {
            rends.Clear();
            var root = body != null ? body : transform;
            root.GetComponentsInChildren(true, rends);
        }

        void ApplyTint()
        {
            var c = TintColor;
            foreach (var r in rends)
            {
                if (r == null) continue;
                // sharedMaterial 을 만지면 에셋이 오염된다. material 은 인스턴스를 만든다.
                var m = r.material;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
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
