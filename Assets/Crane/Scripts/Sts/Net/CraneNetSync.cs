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
            "StsCraneVRController", "SpreaderGrabber"
        };
        [Tooltip("네트워크 수신값으로의 보간 속도(클수록 즉각적·지연↓). 0이면 즉시 스냅.")]
        [SerializeField] float smooth = 30f;
        [Tooltip("컨테이너 매칭 허용 반경(m) — 잡힌 컨테이너를 클라이언트에서 위치로 찾을 때.")]
        [SerializeField] float containerMatchRadius = 1.0f;
        [Tooltip("연속 축 값(갠트리/트롤리/호이스트) 전송 주기(Hz). 매 프레임(72~90Hz) 전송 시 Quest Wi-Fi가 포화되므로 제한. 클라이언트는 보간하므로 20~30이면 충분. 0이면 매 프레임.")]
        [SerializeField] float sendRate = 25f;

        // 서버 write / 모두 read 권한 — 호스트(조종자)만 값을 쓴다.
        static NetworkVariableWritePermission S => NetworkVariableWritePermission.Server;
        static NetworkVariableReadPermission E => NetworkVariableReadPermission.Everyone;

        readonly NetworkVariable<float> nGantry  = new(0f, E, S);
        readonly NetworkVariable<float> nTrolley = new(0f, E, S);
        readonly NetworkVariable<float> nHoist   = new(0f, E, S);
        readonly NetworkVariable<float> nHoistFloor = new(0f, E, S);
        readonly NetworkVariable<bool>  nIs40    = new(false, E, S);
        readonly NetworkVariable<bool>  nLocked  = new(false, E, S);
        readonly NetworkVariable<int>   nAlarmCode = new(0, E, S);   // 활성 알람(최고 심각도 1건) 코드. 0=이상 없음 — 관전자도 같은 알람을 보도록 동기화.
        readonly NetworkVariable<int>   nOpMode    = new(0, E, S);   // 운영상태(운전/정지/이상) = (int)OpMode. 호스트 판정을 관전자도 동일하게 보도록 동기화.

        // 컨테이너 적재 동기화 — [외부감사 S2 수정 2026-06-17] has/index/grabWorld/attachLocal을
        //   분리 NetworkVariable 4개로 보내면 수신 순서가 역전될 수 있어(대역폭 혼잡 시) has=true가
        //   먼저 도착하면 클라가 초기 0 오프셋으로 잘못 붙던 위험이 있었다. 한 구조체(단일 NetworkVariable)로
        //   묶어 4필드를 '원자적'으로 한 번에 전송 → 부분 갱신 불가, 선언순서 의존 제거.
        readonly NetworkVariable<GrabState> nGrab = new(default, E, S);

        /// <summary>적재 상태 4필드를 원자적으로 동기화하는 단일 구조체(unmanaged → NetworkVariable 가능).</summary>
        struct GrabState : INetworkSerializable, System.IEquatable<GrabState>
        {
            public bool    Has;          // 컨테이너를 잡고 있는가
            public int     Index;        // 잡은 컨테이너의 결정적 인덱스(정확 매칭용). 미상=-1
            public Vector3 GrabWorld;    // 잡는 순간 월드 위치(인덱스 실패 시 폴백)
            public Vector3 AttachLocal;  // attach 기준 로컬 위치

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Has);
                s.SerializeValue(ref Index);
                s.SerializeValue(ref GrabWorld);
                s.SerializeValue(ref AttachLocal);
            }

            // NetworkVariable의 변경(dirty) 감지를 GC 없이 값비교로 — 4필드 모두 같아야 동일.
            public bool Equals(GrabState o) =>
                Has == o.Has && Index == o.Index && GrabWorld == o.GrabWorld && AttachLocal == o.AttachLocal;
        }

        StsCrane crane;
        SpreaderHoist hoist;
        SpreaderTelescope telescope;
        SpreaderLockAnimator lockAnim;
        SpreaderAttach attach;

        // 호스트 송신 스로틀(연속 축 값만 제한 — 이산 그랩/릴리스는 즉시)
        float nextSend;

        // 클라이언트 보간 상태
        float gantryCur, trolleyCur, hoistCur;
        bool clientConfigured;
        bool clientHasContainer;   // 클라이언트가 마지막으로 반영한 적재 상태(전환 감지용)
        Transform clientHeld;   // 클라이언트가 시각적으로 매단 컨테이너

        // 클라이언트에서 비활성화한 조종 컴포넌트들 — 세션 종료 시 되살리기 위해 보관.
        readonly List<Behaviour> disabledOnClient = new();

        /// <summary>호스트가 판정한 현재 활성 알람 코드(최고 심각도 1건). 0=이상 없음.
        /// 관전자·알람 배너 HUD가 이 값을 읽어 호스트와 동일한 알람을 표시한다(스폰 전엔 0).</summary>
        public int NetAlarmCode => nAlarmCode.Value;

        /// <summary>호스트가 판정한 현재 운영상태(운전/정지/이상)의 정수값. 관전자 상태 HUD가 읽어 동일 표시. 0=정지.</summary>
        public int NetOpMode => nOpMode.Value;

        // 스폰된 활성 인스턴스 — HUD/라벨이 매 프레임 Find 없이 알람 코드를 읽도록 캐시.
        static CraneNetSync _instance;

        /// <summary>현재 활성 알람 코드(최고 심각도 1건)의 단일 출처. 네트워크 접속 중이면 호스트 권위값
        /// (관전자 화면도 정확), 아니면 로컬 판정. 알람 배너·부품 말풍선이 공용으로 쓴다. 0=이상 없음.</summary>
        public static int ActiveAlarmCode(StsCrane crane)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer) && _instance != null)
                return _instance.NetAlarmCode;
            var f = CraneFault.Evaluate(crane);   // 단독/미접속 — 로컬 판정(호스트 시점과 동일)
            return f.IsValid ? f.Code : 0;
        }

        /// <summary>현재 운영상태(운전/정지/이상)의 단일 출처. 네트워크 접속 중이면 호스트 권위값(관전자도 정확),
        /// 아니면 로컬 판정. 상태 HUD가 호스트=관전자 동일 표시를 위해 쓴다.</summary>
        public static OpMode ActiveOpMode(StsCrane crane)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer) && _instance != null)
                return (OpMode)_instance.NetOpMode;
            return crane != null ? crane.OpMode.Current : OpMode.Stopped;   // 단독/미접속 — 로컬 판정
        }

        public override void OnNetworkSpawn()
        {
            _instance = this;
            EnsureRefs();
            if (!IsServer)
                DisableControlOnClient();   // 관전자: 조종 입력/로직 정지
        }

        // 세션 종료/디스폰(호스트 끊김 포함) 시 정리 — 안 하면 클라이언트가 든 컨테이너가 키네마틱·부유
        // 상태로 얼어붙고, 비활성화했던 조종 컴포넌트가 영구히 꺼진 채 남는다.
        public override void OnNetworkDespawn()
        {
            if (_instance == this) _instance = null;
            if (clientHeld != null) ClientDetach();   // 들고 있던 컨테이너 놓아 물리 복원
            foreach (var b in disabledOnClient)
                if (b != null) b.enabled = true;       // 끈 조종 컴포넌트 복원
            disabledOnClient.Clear();
            clientHasContainer = false;
            clientConfigured = false;
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
                    if (b != null && b.enabled && b.GetType().Name == name)
                    {
                        b.enabled = false;
                        disabledOnClient.Add(b);   // 종료 시 되살리기 위해 기록
                    }
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
            // 연속 축 값은 sendRate(Hz)로 제한 — 매 프레임 쓰면 이동 중 72~90Hz로 전송돼 LAN/Wi-Fi가 포화.
            // (NetworkVariable은 '값이 바뀔 때만' 보내므로, 정지 중엔 throttle과 무관하게 0건.)
            if (sendRate <= 0f || Time.unscaledTime >= nextSend)
            {
                if (sendRate > 0f) nextSend = Time.unscaledTime + 1f / sendRate;
                if (crane.Gantry  != null) nGantry.Value  = crane.Gantry.Current;
                if (crane.Trolley != null) nTrolley.Value = crane.Trolley.Current;
                if (crane.Spreader != null) nHoist.Value  = crane.Spreader.Current;
                if (hoist != null) nHoistFloor.Value = hoist.FloorOffset;
                if (telescope != null) nIs40.Value = telescope.Is40;
                if (lockAnim != null) nLocked.Value = lockAnim.Locked;
            }

            // 그랩/릴리스(이산 상태 전환)는 매 프레임 감지 — 지연 없이 즉시 반영(어차피 변할 때만 전송).
            //   4필드를 GrabState 한 구조체로 묶어 '원자적'으로 1회 전송(수신 순서 역전·부분 갱신 불가).
            bool has = attach != null && attach.HasContainer;
            if (has != nGrab.Value.Has)
            {
                var g = new GrabState { Has = has, Index = -1 };
                if (has && attach.AttachedContainer != null)
                {
                    g.Index       = IndexOfContainer(attach.AttachedContainer);
                    g.GrabWorld   = attach.AttachedContainer.position;
                    g.AttachLocal = attach.AttachedContainer.localPosition;
                }
                nGrab.Value = g;
            }

            // 활성 알람 코드(최고 심각도 1건) — 안전 신호라 throttle 없이 즉시 동기화(값이 바뀔 때만 전송).
            //   관전자는 끝단·충돌 플래그 등 알람 판정 상태를 로컬에 다 갖지 못하므로, 호스트가 판정한
            //   결과 코드를 권위값으로 내려보내 호스트=관전자 알람을 100% 일치시킨다(클라 재계산 의존 제거).
            var fault = CraneFault.Evaluate(crane);
            int alarm = fault.IsValid ? fault.Code : 0;
            if (alarm != nAlarmCode.Value) nAlarmCode.Value = alarm;

            // 운영상태(운전/정지/이상) — 안전·상태 신호라 throttle 없이 즉시 동기화(값이 바뀔 때만 전송).
            int opm = (int)crane.OpMode.Current;
            if (opm != nOpMode.Value) nOpMode.Value = opm;
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
            //   GrabState 구조체 한 덩이로 동기화되므로 has/오프셋이 항상 함께 도착해(원자성, S2 수정) 부분 갱신
            //   문제는 없다. 폴링을 유지하는 이유는 '늦게 접속한 관전자'도 현재 적재 상태로 자연 수렴시키기 위함
            //   (OnValueChanged는 가입 후 변경분만 받음). Update 시점엔 그 틱의 값이 모두 적용된 뒤라 일관적.
            var grab = nGrab.Value;
            if (grab.Has != clientHasContainer)
            {
                clientHasContainer = grab.Has;
                if (grab.Has) ClientAttach(grab);
                else ClientDetach();
            }
        }

        void ClientAttach(GrabState grab)
        {
            if (attach == null) return;
            Transform anchor = attach.AttachAnchor;
            // 1순위: 호스트가 보낸 결정적 인덱스로 '바로 그 컨테이너'를 집는다(색·ID 일치 보장).
            //        씬이 양쪽 동일하므로 같은 정렬 목록의 같은 인덱스는 같은 개체다.
            // 2순위: 인덱스가 없거나 못 찾을 때만 기존 좌표 근접 매칭으로 폴백.
            Transform target = ContainerByIndex(grab.Index)
                            ?? FindNearestRigidbody(grab.GrabWorld, containerMatchRadius);
            if (target == null) return;

            var rb = target.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            target.SetParent(anchor, worldPositionStays: false);
            target.localPosition = grab.AttachLocal;
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
                if (rb.name.IndexOf("Container", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
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
