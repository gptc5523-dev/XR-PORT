using Unity.Netcode;
using UnityEngine;

namespace Container.Crane.Sts.Net
{
    /// <summary>
    /// 각 참가자의 머리(HMD)·양손 위치를 네트워크로 공유해 서로의 아바타를 본다.
    /// 소유자(자기 자신)만 자기 XR 포즈를 써서 네트워크 변수에 올리고, 나머지는 받아서 표시한다.
    ///
    /// 자기 자신의 아바타는 화면에 안 보이게 렌더러를 끈다(1인칭이라 시야 방해됨).
    /// head/leftHand/rightHand 자식 Transform은 NetLanSetup 에디터 셋업이 자동으로 만들어 연결한다.
    /// </summary>
    [AddComponentMenu("Container/Net/Player Avatar Sync")]
    [DisallowMultipleComponent]
    public sealed class PlayerAvatarSync : NetworkBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightHand;
        [Tooltip("참가자 구분용 색을 입힐 렌더러(머리). 접속자마다 다른 색이 칠해진다.")]
        [SerializeField] Renderer tintTarget;
        [Tooltip("원격 아바타 보간 속도(클수록 즉각).")]
        [SerializeField] float smooth = 16f;
        [Tooltip("내 포즈 전송 주기(Hz). 매 프레임(72~90Hz) 전송 시 인원수만큼 대역폭이 폭증하므로 제한. 원격은 보간하므로 20~30이면 충분. 0이면 매 프레임.")]
        [SerializeField] float sendRate = 25f;

        Camera cam;        // Camera.main 캐시(매 프레임 태그 검색 방지)
        float nextSend;

        // 참가자(OwnerClientId)별 색 — 선명한 밝은 색으로 서로 구분.
        static readonly Color[] Palette =
        {
            new Color(0.95f, 0.42f, 0.42f), new Color(0.40f, 0.66f, 0.96f),
            new Color(0.48f, 0.84f, 0.52f), new Color(0.98f, 0.82f, 0.36f),
            new Color(0.80f, 0.58f, 0.94f),
        };

        struct RigPose : INetworkSerializable, System.IEquatable<RigPose>
        {
            public Vector3 hP; public Quaternion hR;
            public Vector3 lP; public Quaternion lR;
            public Vector3 rP; public Quaternion rR;
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref hP); s.SerializeValue(ref hR);
                s.SerializeValue(ref lP); s.SerializeValue(ref lR);
                s.SerializeValue(ref rP); s.SerializeValue(ref rR);
            }
            public bool Equals(RigPose o) =>
                hP == o.hP && hR == o.hR && lP == o.lP && lR == o.lR && rP == o.rP && rR == o.rR;
        }

        readonly NetworkVariable<RigPose> nPose = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public override void OnNetworkSpawn()
        {
            // 참가자마다 안전모 색을 다르게 — OwnerClientId는 모든 기기에서 동일해 색이 일치한다.
            if (tintTarget != null)
                tintTarget.material.color = Palette[(int)(OwnerClientId % (ulong)Palette.Length)];

            if (IsOwner) SetVisible(false);   // 내 아바타는 내 화면에서 숨김
        }

        void SetVisible(bool v)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
        }

        void Update()
        {
            if (IsOwner) WriteLocalPose();
            else ApplyRemotePose();
        }

        // ─── 소유자: 자기 XR 리그(머리/양손) 월드 포즈를 네트워크에 올림 ───
        void WriteLocalPose()
        {
            if (cam == null) cam = Camera.main;   // 한 번만 검색해 캐시(매 프레임 태그 스캔 방지)
            if (cam == null) return;
            Transform space = cam.transform.parent;   // Camera Offset = XR 트래킹 공간 원점

            var p = new RigPose
            {
                hP = cam.transform.position, hR = cam.transform.rotation,
            };
            DevicePose(UnityEngine.XR.XRNode.LeftHand,  space, out p.lP, out p.lR);
            DevicePose(UnityEngine.XR.XRNode.RightHand, space, out p.rP, out p.rR);

            // 소유자 본인 자식 Transform은 매 프레임 부드럽게 맞춰둠(다른 컴포넌트 참조 대비).
            Apply(p, 1f);

            // 네트워크 전송은 sendRate(Hz)로 제한 — 인원수×프레임레이트 폭주 방지. (값이 바뀔 때만 전송됨.)
            if (sendRate <= 0f || Time.unscaledTime >= nextSend)
            {
                if (sendRate > 0f) nextSend = Time.unscaledTime + 1f / sendRate;
                nPose.Value = p;
            }
        }

        static void DevicePose(UnityEngine.XR.XRNode node, Transform space, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return;
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 lp);
            d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion lr);
            if (space != null) { pos = space.TransformPoint(lp); rot = space.rotation * lr; }
            else { pos = lp; rot = lr; }
        }

        // ─── 원격: 받은 포즈로 아바타 부드럽게 이동 ───
        void ApplyRemotePose()
        {
            float k = smooth <= 0f ? 1f : 1f - Mathf.Exp(-smooth * Time.deltaTime);
            Apply(nPose.Value, k);
        }

        void Apply(RigPose p, float k)
        {
            if (head)      { head.position      = Vector3.Lerp(head.position, p.hP, k);      head.rotation      = Quaternion.Slerp(head.rotation, p.hR, k); }
            if (leftHand)  { leftHand.position  = Vector3.Lerp(leftHand.position, p.lP, k);  leftHand.rotation  = Quaternion.Slerp(leftHand.rotation, p.lR, k); }
            if (rightHand) { rightHand.position = Vector3.Lerp(rightHand.position, p.rP, k); rightHand.rotation = Quaternion.Slerp(rightHand.rotation, p.rR, k); }
        }
    }
}
