using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>크레인 상태(STS·RTG 전부)를 호스트(조종자) → 관전자로 단방향 동기화 — 크레인마다 구조체 하나를 이름순 목록으로 보내고, 관전자는 조종·자동 운전을 끈 채 그 값으로 시각만 재현한다.</summary>
    [AddComponentMenu("AI-XR Crane/Net/Crane Net Sync")]
    [DisallowMultipleComponent]
    public sealed partial class CraneNetSync : NetworkBehaviour, ICraneNetState
    {
        [Tooltip("클라이언트(관전자)에서 비활성화할 조종/로직 컴포넌트 타입명(네임스페이스 제외). 비우면 기본 목록 사용.")]
        [SerializeField] string[] disableOnClient =
        {
            "StsCraneVRController", "SpreaderGrabber"
        };
        static readonly string[] AlwaysDisableOnClient = { "CraneDemoRunner" };   // 관전자 쪽 시연 자동 운전이 동기화 값과 같은 크레인을 같이 움직였다(씬 값과 무관하게 끈다)
        [Tooltip("네트워크 수신값으로의 보간 속도(클수록 즉각적·지연↓). 0이면 즉시 스냅.")]
        [SerializeField] float smooth = 30f;
        [Tooltip("컨테이너 매칭 허용 반경(m) — 잡힌 컨테이너를 클라이언트에서 위치로 찾을 때.")]
        [SerializeField] float containerMatchRadius = 1.0f;
        [Tooltip("연속 축 값(갠트리/트롤리/호이스트) 전송 주기(Hz). 매 프레임(72~90Hz) 전송 시 Quest Wi-Fi가 포화되므로 제한. 클라이언트는 보간하므로 20~30이면 충분. 0이면 매 프레임.")]
        [SerializeField] float sendRate = 25f;

        /// <summary>크레인 한 대 상태 — 한 구조체로 원자 전송(나눠 보내면 수신 순서가 뒤집혀 일부만 갱신된다).</summary>
        struct CraneState : INetworkSerializable, System.IEquatable<CraneState>
        {
            public float Gantry, Trolley, Hoist, HoistFloor;
            public bool Is40, Locked;
            public int Alarm, OpMode;      // 활성 알람(최고 심각도 1건, 0=없음) · (int)OpMode — 호스트 판정
            public bool Has;               // 컨테이너를 매달았는가
            public int Index;              // ContainerRegistry 번호, 미상 −1
            public Vector3 GrabWorld;      // 잡는 순간 월드 위치(번호 실패 시 폴백)
            public Vector3 AttachLocal;    // 부착점 기준 로컬 위치

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Gantry); s.SerializeValue(ref Trolley); s.SerializeValue(ref Hoist); s.SerializeValue(ref HoistFloor);
                s.SerializeValue(ref Is40); s.SerializeValue(ref Locked); s.SerializeValue(ref Alarm); s.SerializeValue(ref OpMode);
                s.SerializeValue(ref Has); s.SerializeValue(ref Index); s.SerializeValue(ref GrabWorld); s.SerializeValue(ref AttachLocal);
            }

            public bool Equals(CraneState o) =>
                Gantry == o.Gantry && Trolley == o.Trolley && Hoist == o.Hoist && HoistFloor == o.HoistFloor && Is40 == o.Is40 && Locked == o.Locked
                && Alarm == o.Alarm && OpMode == o.OpMode && Has == o.Has && Index == o.Index && GrabWorld == o.GrabWorld && AttachLocal == o.AttachLocal;
        }
        readonly NetworkList<CraneState> nCranes = new();   // 서버만 쓴다 — 순서 = rigs(이름순)

        // 컨테이너 핸드오프(누구나 손으로 옮기고 전원이 봄) 동기화
        [Tooltip("핸드오프(소유권 이전) 후 같은 손이 즉시 되집는 핑퐁을 막는 쿨다운(초).")]
        [SerializeField] float handoffCooldown = 0.75f;
        [Tooltip("들고 있는 컨테이너 포즈 전송 주기(Hz). 매 프레임이면 인원·개수만큼 대역폭 폭증 → 제한. 0이면 매 프레임.")]
        [SerializeField] float containerSendRate = 25f;

        /// <summary>지금 손에 들린 컨테이너만 올리는 활성 목록(정지한 것은 전송 0). Owner = 마지막에 집은 사람(핸드오프), Pos/Rot = 월드 포즈.</summary>
        struct HeldContainer : INetworkSerializable, System.IEquatable<HeldContainer>
        {
            public int Index; public ulong Owner; public Vector3 Pos; public Quaternion Rot;
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Index); s.SerializeValue(ref Owner);
                s.SerializeValue(ref Pos);   s.SerializeValue(ref Rot);
            }
            public bool Equals(HeldContainer o) => Index == o.Index && Owner == o.Owner && Pos == o.Pos && Rot == o.Rot;
        }
        readonly NetworkList<HeldContainer> nHeld = new();

        List<Transform> containerSnapshot;
        readonly List<XRGrabInteractable> containerGrabs = new();   // snapshot과 인덱스 정렬
        readonly List<(UnityEngine.Events.UnityAction<SelectEnterEventArgs> on, UnityEngine.Events.UnityAction<SelectExitEventArgs> off)> grabHooks = new();   // 내가 단 리스너만 뗀다
        readonly HashSet<int> myOwned = new();              // 내가 손에 든 인덱스
        readonly HashSet<int> appliedRemote = new();        // 남이 든 것(내가 kinematic 적용 중)
        readonly Dictionary<int, bool> origKinematic = new();   // 적용 전 원래 isKinematic
        readonly Dictionary<int, float> grabCooldownUntil = new();
        readonly List<int> _tmpA = new(); readonly List<int> _tmpB = new(); readonly HashSet<int> _activeNow = new();
        float nextContainerSend;

        /// <summary>크레인 한 대의 참조와 관전자 쪽 재현 상태.</summary>
        sealed class Rig
        {
            public StsCrane crane;
            public SpreaderHoist hoist;
            public SpreaderTelescope telescope;
            public SpreaderLockAnimator lockAnim;
            public SpreaderAttach attach;
            public float nextSend;                 // 호스트 송신 스로틀(연속 축 값만 — 이산 값은 즉시)
            public float gCur, tCur, hCur;         // 관전자 보간 상태
            public bool configured, clientHas;
            public Transform clientHeld;           // 관전자가 시각적으로 매단 컨테이너
        }
        readonly List<Rig> rigs = new();

        // 클라이언트에서 끈 조종 컴포넌트 — 세션 종료 시 복원.
        readonly List<Behaviour> disabledOnClient = new();

        int IndexOf(StsCrane c) { for (int i = 0; i < rigs.Count; i++) if (rigs[i].crane == c) return i; return -1; }

        bool TryState(StsCrane c, out CraneState s)
        {
            int i = IndexOf(c);
            if (i >= 0 && i < nCranes.Count) { s = nCranes[i]; return true; }
            s = default; return false;
        }

        bool ICraneNetState.Syncs(StsCrane c) => IsSpawned && IndexOf(c) >= 0;

        bool ICraneNetState.TryState(StsCrane c, out int alarm, out OpMode op)
        {
            alarm = 0; op = OpMode.Stopped;
            var nm = NetworkManager.Singleton;
            if (nm == null || !(nm.IsClient || nm.IsServer) || !TryState(c, out var s)) return false;
            alarm = s.Alarm; op = (OpMode)s.OpMode;
            return true;
        }

        public override void OnNetworkSpawn()
        {
            CraneLink.NetState = this;
            BuildRigs();
            if (IsServer)
            {
                nCranes.Clear();
                foreach (var r in rigs) nCranes.Add(Read(r, new CraneState { Index = -1 }));
                NetworkManager.OnClientDisconnectCallback += DropHeldOf;
            }
            else DisableControlOnClient();   // 관전자: 조종 입력·자동 운전 정지
            SubscribeContainers();          // 호스트·관전자 모두: 손 집기/놓기 후킹(핸드오프)
        }

        // 컨테이너를 든 채 나간 참가자 — 놓기 RPC 가 안 오므로 서버가 놓는다(안 하면 모두에게 kinematic 으로 언 채 남는다).
        void DropHeldOf(ulong clientId)
        {
            for (int i = nHeld.Count - 1; i >= 0; i--)
                if (nHeld[i].Owner == clientId) nHeld.RemoveAt(i);
        }

        // 디스폰(호스트 끊김 포함) 시 정리 — 안 하면 든 컨테이너가 kinematic 으로 얼고 조종이 꺼진 채 남는다.
        public override void OnNetworkDespawn()
        {
            if (CraneLink.NetState == this) CraneLink.NetState = null;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= DropHeldOf;
            foreach (var r in rigs)
            {
                if (r.clientHeld != null) ClientDetach(r);   // 들고 있던 컨테이너 놓아 물리 복원
                r.clientHas = false; r.configured = false;
            }
            foreach (var b in disabledOnClient)
                if (b != null) b.enabled = true;       // 끈 조종 컴포넌트 복원
            disabledOnClient.Clear();
            UnsubscribeContainers();   // 핸드오프 후킹/물리 복원
        }

        // 크레인 목록 — 이름순이라 모든 기기가 같은 순서(목록 번호 = 크레인).
        void BuildRigs()
        {
            rigs.Clear();
            var cranes = new List<StsCrane>(FindObjectsByType<StsCrane>());
            cranes.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var c in cranes)
                rigs.Add(new Rig
                {
                    crane = c, hoist = c.Spreader as SpreaderHoist, attach = c.Attach,
                    telescope = c.GetComponentInChildren<SpreaderTelescope>(true),
                    lockAnim = c.GetComponentInChildren<SpreaderLockAnimator>(true),
                });
        }

        void DisableControlOnClient()
        {
            foreach (var r in rigs)
                foreach (var b in r.crane.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null || !b.enabled) continue;
                    string n = b.GetType().Name;
                    if (System.Array.IndexOf(disableOnClient, n) < 0 && System.Array.IndexOf(AlwaysDisableOnClient, n) < 0) continue;
                    b.enabled = false;
                    disabledOnClient.Add(b);   // 종료 시 되살리기 위해 기록
                }
        }

        void Update()
        {
            // 스폰 전(혼자 플레이)엔 아무것도 안 한다 — 가드 없으면 ClientApply가 크레인을 0으로 덮어쓴다.
            if (!IsSpawned) return;

            if (IsServer) ServerWrite();
            else ClientApply();

            ContainerTick();   // 호스트·관전자 모두: 컨테이너 핸드오프 송신/적용
        }
    }
}
