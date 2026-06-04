using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Container.Crane.Sts;

namespace Container.Crane.Sts.Net
{
    /// <summary>
    /// STS 크레인 상태를 호스트(=조종자) → 모든 클라이언트(=관전자)로 단방향 동기화.
    ///
    /// 설계 요지:
    ///   - 호스트만 크레인을 실제로 조종한다(StsCraneVRController/Operator/Grabber가 호스트에서만 동작).
    ///   - 클라이언트는 조종 입력 컴포넌트를 끄고, 네트워크로 받은 값으로 무버(MoveTo)만 호출 → 시각 재현.
    ///   - 무버들은 자체 Update가 없어(값이 들어올 때만 Transform을 세팅) 단순 적용으로 충돌이 없다.
    ///
    /// 이 컴포넌트는 NetworkObject가 붙은 '씬에 배치된' 별도 GameObject에 둔다(크레인이 런타임 생성이라도
    /// FindObjectOfType로 각 기기에서 자기 크레인을 찾으므로 무방). 같은 씬을 모두 로드하므로 씬 NetworkObject는
    /// 호스트 시작 시 자동 스폰된다.
    /// </summary>
    [AddComponentMenu("Container/Net/Crane Net Sync")]
    [DisallowMultipleComponent]
    public sealed class CraneNetSync : NetworkBehaviour
    {
        [Tooltip("클라이언트(관전자)에서 비활성화할 조종/로직 컴포넌트 타입명(네임스페이스 제외). 비우면 기본 목록 사용.")]
        [SerializeField] string[] disableOnClient =
        {
            "StsCraneVRController", "StsCraneOperator", "SpreaderGrabber"
        };
        [Tooltip("네트워크 수신값으로의 보간 속도(클수록 즉각적·지연↓). 0이면 즉시 스냅.")]
        [SerializeField] float smooth = 30f;
        [Tooltip("컨테이너 매칭 허용 반경(m) — 잡힌 컨테이너를 클라이언트에서 위치로 찾을 때.")]
        [SerializeField] float containerMatchRadius = 1.0f;

        // 서버 write / 모두 read 권한 — 호스트(조종자)만 값을 쓴다.
        static NetworkVariableWritePermission S => NetworkVariableWritePermission.Server;
        static NetworkVariableReadPermission E => NetworkVariableReadPermission.Everyone;

        readonly NetworkVariable<float> nGantry  = new(0f, E, S);
        readonly NetworkVariable<float> nTrolley = new(0f, E, S);
        readonly NetworkVariable<float> nHoist   = new(0f, E, S);
        readonly NetworkVariable<float> nHoistFloor = new(0f, E, S);
        readonly NetworkVariable<bool>  nIs40    = new(false, E, S);
        readonly NetworkVariable<bool>  nLocked  = new(false, E, S);

        // 컨테이너 적재 동기화
        readonly NetworkVariable<bool>    nHasContainer = new(false, E, S);
        readonly NetworkVariable<int>     nGrabIndex    = new(-1, E, S);            // 잡은 컨테이너의 결정적 인덱스(정확 매칭용). -1이면 미상.
        readonly NetworkVariable<Vector3> nGrabWorld    = new(Vector3.zero, E, S);  // 잡는 순간 컨테이너 월드 위치(인덱스 실패 시 폴백)
        readonly NetworkVariable<Vector3> nAttachLocal  = new(Vector3.zero, E, S);  // attach 기준 로컬 위치

        StsCrane crane;
        SpreaderHoist hoist;
        SpreaderTelescope telescope;
        SpreaderLockAnimator lockAnim;
        SpreaderAttach attach;

        // 클라이언트 보간 상태
        float gantryCur, trolleyCur, hoistCur;
        bool clientConfigured;
        bool clientHasContainer;   // 클라이언트가 마지막으로 반영한 적재 상태(전환 감지용)
        Transform clientHeld;   // 클라이언트가 시각적으로 매단 컨테이너

        public override void OnNetworkSpawn()
        {
            EnsureRefs();
            if (!IsServer)
                DisableControlOnClient();   // 관전자: 조종 입력/로직 정지
        }

        void EnsureRefs()
        {
            if (crane == null) crane = FindFirstObjectByType<StsCrane>();
            if (crane == null) return;
            hoist     = crane.Spreader as SpreaderHoist;
            attach    = crane.Attach;
            telescope = crane.GetComponentInChildren<SpreaderTelescope>(true);
            lockAnim  = crane.GetComponentInChildren<SpreaderLockAnimator>(true);
        }

        void DisableControlOnClient()
        {
            if (crane == null) return;
            foreach (var name in disableOnClient)
            {
                foreach (var b in crane.GetComponentsInChildren<Behaviour>(true))
                    if (b != null && b.GetType().Name == name) b.enabled = false;
            }
        }

        void Update()
        {
            // 네트워크 세션에 스폰되기 전(=혼자 플레이/편집 중)에는 아무것도 하지 않는다.
            // 가드가 없으면 IsServer=false라 ClientApply가 매 프레임 크레인을 기본값(0)으로 덮어써
            // 싱글플레이에서 크레인이 움직이지 않는 것처럼 보인다.
            if (!IsSpawned) return;

            if (crane == null) { EnsureRefs(); if (crane == null) return; }

            if (IsServer) ServerWrite();
            else ClientApply();
        }

        // ───────── 호스트: 현재 크레인 상태를 네트워크 변수에 기록 ─────────
        void ServerWrite()
        {
            if (crane.Gantry  != null) nGantry.Value  = crane.Gantry.Current;
            if (crane.Trolley != null) nTrolley.Value = crane.Trolley.Current;
            if (crane.Spreader != null) nHoist.Value  = crane.Spreader.Current;
            if (hoist != null) nHoistFloor.Value = hoist.FloorOffset;
            if (telescope != null) nIs40.Value = telescope.Is40;
            if (lockAnim != null) nLocked.Value = lockAnim.Locked;

            bool has = attach != null && attach.HasContainer;
            if (has != nHasContainer.Value)
            {
                if (has && attach.AttachedContainer != null)
                {
                    nGrabIndex.Value   = IndexOfContainer(attach.AttachedContainer);
                    nGrabWorld.Value   = attach.AttachedContainer.position;
                    nAttachLocal.Value = attach.AttachedContainer.localPosition;
                }
                nHasContainer.Value = has;
            }
        }

        // ───────── 관전자: 네트워크 값으로 크레인 시각 재현 ─────────
        void ClientApply()
        {
            float k = smooth <= 0f ? 1f : 1f - Mathf.Exp(-smooth * Time.deltaTime);

            if (!clientConfigured)
            {
                gantryCur = nGantry.Value; trolleyCur = nTrolley.Value; hoistCur = nHoist.Value;
                clientConfigured = true;
            }
            gantryCur  = Mathf.Lerp(gantryCur,  nGantry.Value,  k);
            trolleyCur = Mathf.Lerp(trolleyCur, nTrolley.Value, k);
            hoistCur   = Mathf.Lerp(hoistCur,   nHoist.Value,   k);

            if (hoist != null) hoist.SetFloorOffset(nHoistFloor.Value);   // 동일 하강 한계 재현
            crane.Gantry?.MoveTo(gantryCur);
            crane.Trolley?.MoveTo(trolleyCur);
            crane.Spreader?.MoveTo(hoistCur);
            telescope?.Set40(nIs40.Value);
            lockAnim?.SetLocked(nLocked.Value);

            // 적재 상태는 콜백이 아니라 폴링으로 감지한다.
            //   nHasContainer.OnValueChanged에서 바로 처리하면, NetworkVariable이 '선언 순서'대로
            //   적용되는 탓에 nHasContainer(먼저 선언)가 nGrabWorld/nAttachLocal보다 먼저 반영돼
            //   ClientAttach가 아직 갱신 안 된(초기 0) 오프셋을 읽어 컨테이너가 스프레더 '위'에 붙던 버그.
            //   Update 시점엔 그 틱의 모든 변수가 적용된 뒤라 일관되며, 늦게 접속한 관전자도 반영된다.
            bool has = nHasContainer.Value;
            if (has != clientHasContainer)
            {
                clientHasContainer = has;
                if (has) ClientAttach();
                else ClientDetach();
            }
        }

        void ClientAttach()
        {
            if (attach == null) return;
            Transform anchor = attach.AttachAnchor;
            // 1순위: 호스트가 보낸 결정적 인덱스로 '바로 그 컨테이너'를 집는다(색·ID 일치 보장).
            //        씬이 양쪽 동일하므로 같은 정렬 목록의 같은 인덱스는 같은 개체다.
            // 2순위: 인덱스가 없거나 못 찾을 때만 기존 좌표 근접 매칭으로 폴백.
            Transform target = ContainerByIndex(nGrabIndex.Value)
                            ?? FindNearestRigidbody(nGrabWorld.Value, containerMatchRadius);
            if (target == null) return;

            var rb = target.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            target.SetParent(anchor, worldPositionStays: false);
            target.localPosition = nAttachLocal.Value;
            target.localRotation = Quaternion.identity;
            clientHeld = target;
        }

        void ClientDetach()
        {
            if (clientHeld == null) return;
            clientHeld.SetParent(null, worldPositionStays: true);
            var rb = clientHeld.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }
            clientHeld = null;
        }

        Transform FindNearestRigidbody(Vector3 world, float maxDist)
        {
            Transform best = null; float bestD = maxDist;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (rb == null) continue;
                if (crane != null && rb.transform.IsChildOf(crane.transform)) continue;
                float d = Vector3.Distance(world, rb.transform.position);
                if (d < bestD) { bestD = d; best = rb.transform; }
            }
            return best;
        }

        // 모든 컨테이너 Rigidbody를 이름순으로 정렬한 결정적 목록.
        // 씬이 호스트·관전자 모두 동일하므로(같은 이름 집합·고유 이름) 같은 순서가 보장된다.
        // 잡혀서 스프레더(크레인 자식)로 옮겨가도 목록에서 빠지지 않도록 부모 관계로 거르지 않는다
        // — 그래야 인덱스가 잡기 전후로 변하지 않는다.
        static readonly List<Transform> _containerBuf = new();
        List<Transform> BuildContainerList()
        {
            _containerBuf.Clear();
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (rb == null) continue;
                if (rb.name.IndexOf("Container", System.StringComparison.Ordinal) < 0) continue;
                _containerBuf.Add(rb.transform);
            }
            _containerBuf.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return _containerBuf;
        }

        int IndexOfContainer(Transform t)
        {
            var list = BuildContainerList();
            return list.IndexOf(t);
        }

        Transform ContainerByIndex(int index)
        {
            if (index < 0) return null;
            var list = BuildContainerList();
            return (index < list.Count) ? list[index] : null;
        }
    }
}
