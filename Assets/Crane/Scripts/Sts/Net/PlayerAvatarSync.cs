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
        bool avatarVisible = true;   // 현재 렌더 표시 상태(소유자=1인칭이면 false). 런타임 추가 부품(전신 마네킹) 동기화에 사용.

        // ─── 전신 마네킹 빌더(PlayerAvatarBody)가 참조하는 접근자 ───
        /// <summary>머리(HMD) Transform — 매 프레임 카메라/네트워크 포즈로 갱신됨.</summary>
        public Transform Head => head;
        /// <summary>왼손 컨트롤러 Transform.</summary>
        public Transform LeftHand => leftHand;
        /// <summary>오른손 컨트롤러 Transform.</summary>
        public Transform RightHand => rightHand;
        /// <summary>참가자 식별색(머리 틴트와 동일) — 안전모 등 강조용.</summary>
        public Color OwnerColor => Palette[(int)(OwnerClientId % (ulong)Palette.Length)];
        /// <summary>현재 표시 상태를 모든 자식 렌더러(런타임 추가 부품 포함)에 다시 적용 — 마네킹 부품 생성 직후 호출.</summary>
        public void ReapplyVisibility() => SetVisible(avatarVisible);

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
            // ★ 아바타를 미니어처 월드(1/24)에 맞춰 축소 — 안 하면 0.2m 머리 구가 미니어처(사람 키 ~0.07m) 월드에서
            //   '거대한 공'으로 뜬다. 머리/손 위치는 Apply가 월드 좌표로 직접 세우므로 루트 스케일과 무관 →
            //   위치는 그대로, 시각 크기만 1/24로 맞춰진다. 축척은 StsCrane.ModelScale 단일 소스.
            var crane = FindAnyObjectByType<StsCrane>();
            float s = crane != null ? crane.ModelScale : StsConfig.ModelScale;   // 폴백도 SSOT 값(1/24)
            transform.localScale = Vector3.one * s;

            if (IsOwner) SetVisible(false);   // 내 아바타는 내 화면에서 숨김

            // 전신 마네킹(목·가슴·골반·다리·양팔 IK)을 절차적으로 부착 — 프리팹 수정 없이 모든 클라이언트에서 동일 생성.
            //   참가자색은 머리 전체가 아니라 헬멧(PlayerAvatarBody.BuildHelmet)에만 칠해 차콜 마네킹을 유지.
            if (GetComponent<PlayerAvatarBody>() == null) gameObject.AddComponent<PlayerAvatarBody>();
        }

        void SetVisible(bool v)
        {
            avatarVisible = v;
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
