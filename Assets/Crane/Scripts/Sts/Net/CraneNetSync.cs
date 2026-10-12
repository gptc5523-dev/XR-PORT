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
    public sealed class CraneNetSync : NetworkBehaviour
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

        // 스폰된 활성 인스턴스 캐시 — HUD가 매 프레임 Find 하지 않게.
        static CraneNetSync _instance;

        int IndexOf(StsCrane c) { for (int i = 0; i < rigs.Count; i++) if (rigs[i].crane == c) return i; return -1; }

        bool TryState(StsCrane c, out CraneState s)
        {
            int i = IndexOf(c);
            if (i >= 0 && i < nCranes.Count) { s = nCranes[i]; return true; }
            s = default; return false;
        }

        /// <summary>이 크레인이 네트워크로 동기화되는 크레인인가 — 관전자 말풍선이 호스트 알람을 붙일지·PLC 를 직접 돌릴지 판단.</summary>
        public static bool Syncs(StsCrane c) => _instance != null && _instance.IsSpawned && _instance.IndexOf(c) >= 0;

        /// <summary>활성 알람 코드 SSOT — 접속 중이면 그 크레인의 호스트 권위값, 아니면 로컬 판정. 0=이상 없음.</summary>
        public static int ActiveAlarmCode(StsCrane crane)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer) && _instance != null && _instance.TryState(crane, out var s))
                return s.Alarm;
            var f = CraneFault.Evaluate(crane);   // 단독/미접속 — 로컬 판정(호스트 시점과 동일)
            return f.IsValid ? f.Code : 0;
        }

        /// <summary>운영상태 SSOT — 접속 중이면 그 크레인의 호스트 권위값, 아니면 로컬 판정.</summary>
        public static OpMode ActiveOpMode(StsCrane crane)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer) && _instance != null && _instance.TryState(crane, out var s))
                return (OpMode)s.OpMode;
            return crane != null ? crane.OpMode.Current : OpMode.Stopped;   // 단독/미접속 — 로컬 판정
        }

        public override void OnNetworkSpawn()
        {
            _instance = this;
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
            if (_instance == this) _instance = null;
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

        // 지금 크레인 상태 — 매단 컨테이너의 잡은 순간 위치는 같은 컨테이너를 매단 동안 처음 값을 유지한다.
        CraneState Read(Rig r, CraneState prev)
        {
            var c = r.crane;
            var fault = CraneFault.Evaluate(c);
            var s = new CraneState
            {
                Gantry = c.Gantry != null ? c.Gantry.Current : 0f,
                Trolley = c.Trolley != null ? c.Trolley.Current : 0f,
                Hoist = c.Spreader != null ? c.Spreader.Current : 0f,
                HoistFloor = r.hoist != null ? r.hoist.FloorOffset : 0f,
                Is40 = r.telescope != null && r.telescope.Is40,
                Locked = r.lockAnim != null && r.lockAnim.Locked,
                Alarm = fault.IsValid ? fault.Code : 0,
                OpMode = (int)c.OpMode.Current,
                Has = r.attach != null && r.attach.HasContainer,
                Index = -1,
            };
            if (s.Has && r.attach.AttachedContainer != null)
            {
                s.Index = IndexOfContainer(r.attach.AttachedContainer);
                if (prev.Has && prev.Index == s.Index) { s.GrabWorld = prev.GrabWorld; s.AttachLocal = prev.AttachLocal; }
                else { s.GrabWorld = r.attach.AttachedContainer.position; s.AttachLocal = r.attach.AttachedContainer.localPosition; }
            }
            return s;
        }

        // 호스트: 크레인마다 상태를 목록에 기록 — 연속 축 값은 sendRate 로 제한(Wi-Fi 포화), 집기·알람·운영 상태는 즉시. 그대로면 전송 0.
        void ServerWrite()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < rigs.Count && i < nCranes.Count; i++)
            {
                var r = rigs[i];
                if (r.crane == null) continue;
                var prev = nCranes[i];
                var s = Read(r, prev);
                bool discrete = s.Has != prev.Has || s.Index != prev.Index || s.Alarm != prev.Alarm || s.OpMode != prev.OpMode
                                || s.Locked != prev.Locked || s.Is40 != prev.Is40;
                bool due = sendRate <= 0f || now >= r.nextSend;
                if (!discrete && (!due || s.Equals(prev))) continue;
                if (due && sendRate > 0f) r.nextSend = now + 1f / sendRate;
                if (s.Has != prev.Has)
                    SessionEventLog.Record(s.Has ? "crane_pick" : "crane_place", NetworkManager.LocalClientId, r.crane.name,
                        "C" + (s.Has ? s.Index : prev.Index) + (Plc.PlcLiveMode.On ? " PLC" : ""));
                nCranes[i] = s;
            }
        }

        // 관전자: 크레인마다 네트워크 값으로 시각 재현
        void ClientApply()
        {
            float k = smooth <= 0f ? 1f : 1f - Mathf.Exp(-smooth * Time.deltaTime);
            for (int i = 0; i < rigs.Count && i < nCranes.Count; i++)
            {
                var r = rigs[i];
                if (r.crane == null) continue;
                var s = nCranes[i];
                if (!r.configured) { r.gCur = s.Gantry; r.tCur = s.Trolley; r.hCur = s.Hoist; r.configured = true; }
                r.gCur = Mathf.Lerp(r.gCur, s.Gantry, k);
                r.tCur = Mathf.Lerp(r.tCur, s.Trolley, k);
                r.hCur = Mathf.Lerp(r.hCur, s.Hoist, k);

                if (r.hoist != null) r.hoist.SetFloorOffset(s.HoistFloor);   // 동일 하강 한계 재현
                r.crane.Gantry?.MoveTo(r.gCur);
                r.crane.Trolley?.MoveTo(r.tCur);
                r.crane.Spreader?.MoveTo(r.hCur);
                r.telescope?.Set40(s.Is40);
                r.lockAnim?.SetLocked(s.Locked);

                // 콜백 아닌 폴링 — 늦게 접속한 관전자도 현재 상태로 수렴(OnListChanged 는 가입 후 변경분만).
                if (s.Has != r.clientHas)
                {
                    r.clientHas = s.Has;
                    if (s.Has) ClientAttach(r, s);
                    else ClientDetach(r);
                }
            }
        }

        void ClientAttach(Rig r, CraneState s)
        {
            if (r.attach == null) return;
            // 결정적 번호로 매칭, 실패 시 좌표 근접 폴백.
            Transform target = ContainerByIndex(s.Index) ?? FindNearestRigidbody(s.GrabWorld, containerMatchRadius);
            if (target == null) return;

            // 호스트와 같은 SpreaderAttach 로 매단다 — 하중·컨테이너 번호가 관전자 말풍선에도 같게 나온다.
            if (!r.attach.Attach(target)) return;
            target.localPosition = s.AttachLocal;   // 위치만(회전 바꾸면 컨테이너가 돈다)
            r.clientHeld = target;
        }

        void ClientDetach(Rig r)
        {
            if (r.clientHeld == null) return;
            r.attach.Detach();
            var rb = r.clientHeld.GetComponent<Rigidbody>();
            if (rb != null && !Plc.PlcLiveMode.On) { rb.isKinematic = false; rb.useGravity = true; }   // 라이브 PLC 면 놓은 자리 고정(PlcCargoFollow)
            r.clientHeld = null;
        }

        Transform FindNearestRigidbody(Vector3 world, float maxDist)
        {
            Transform best = null; float bestD = maxDist;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude))
            {
                if (rb == null) continue;
                bool onCrane = false;
                foreach (var r in rigs) if (r.crane != null && rb.transform.IsChildOf(r.crane.transform)) { onCrane = true; break; }
                if (onCrane) continue;
                float d = Vector3.Distance(world, rb.transform.position);
                if (d < bestD) { bestD = d; best = rb.transform; }
            }
            return best;
        }

        // 컨테이너는 NetworkObject 가 아니라 ContainerRegistry 번호로 식별 — 크레인 집기·손 핸드오프가 같은 번호를 쓴다(WBS 5.6).
        static int IndexOfContainer(Transform t) => ContainerRegistry.IndexOf(t);
        static Transform ContainerByIndex(int index) => ContainerRegistry.At(index);

        // 컨테이너 핸드오프 — 마지막 집기 우선.
        void SubscribeContainers()
        {
            var found = new List<Transform>(ContainerRegistry.All);   // 접속 순간의 위치로 정렬하면 늦게 들어온 참가자는 크레인이 옮긴 뒤라 순서가 달랐다
            containerSnapshot = found;

            containerGrabs.Clear(); grabHooks.Clear();
            for (int i = 0; i < found.Count; i++)
            {
                var g = found[i].GetComponent<XRGrabInteractable>();
                containerGrabs.Add(g);
                int idx = i;
                grabHooks.Add((_ => OnLocalGrab(idx), _ => OnLocalRelease(idx)));
                if (g == null) continue;
                g.selectEntered.AddListener(grabHooks[i].on);
                g.selectExited.AddListener(grabHooks[i].off);
            }
        }

        void UnsubscribeContainers()
        {
            for (int i = 0; i < containerGrabs.Count; i++)
            {
                var g = containerGrabs[i];
                if (g == null) continue;
                g.selectEntered.RemoveListener(grabHooks[i].on);
                g.selectExited.RemoveListener(grabHooks[i].off);
                if (!g.enabled) g.enabled = true;
            }
            foreach (var idx in new List<int>(appliedRemote)) RestoreContainerPhysics(idx);
            containerGrabs.Clear(); grabHooks.Clear(); myOwned.Clear(); appliedRemote.Clear();
            origKinematic.Clear(); grabCooldownUntil.Clear(); _activeNow.Clear();
        }

        Transform CSnap(int idx) =>
            (containerSnapshot != null && idx >= 0 && idx < containerSnapshot.Count) ? containerSnapshot[idx] : null;
        XRGrabInteractable GrabOf(int idx) =>
            (idx >= 0 && idx < containerGrabs.Count) ? containerGrabs[idx] : null;
        int FindHeld(int idx) { for (int i = 0; i < nHeld.Count; i++) if (nHeld[i].Index == idx) return i; return -1; }

        // 크레인이 든 화물은 손 핸드오프 대상에서 제외(크레인 동기화와 충돌 방지).
        bool IsCraneHeld(int idx)
        {
            var t = CSnap(idx); if (t == null) return false;
            foreach (var r in rigs)
                if (r.clientHeld == t || (r.attach != null && r.attach.AttachedContainer == t)) return true;
            return false;
        }

        void OnLocalGrab(int idx)
        {
            if (IsCraneHeld(idx)) return;
            myOwned.Add(idx);
            grabCooldownUntil.Remove(idx);
            var t = CSnap(idx);
            if (t != null) ClaimContainerServerRpc(idx, t.position, t.rotation);
        }

        void OnLocalRelease(int idx)
        {
            if (!myOwned.Remove(idx)) return;   // 이미 소유권을 뺏긴(핸드오프) 경우엔 무시
            var t = CSnap(idx);
            if (t != null) ReleaseContainerServerRpc(idx, t.position, t.rotation);
        }

        void ContainerTick()
        {
            var nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : 0;
            float now = Time.unscaledTime;

            // (1) 내가 든 것 포즈 송신(스로틀)
            if (myOwned.Count > 0 && (containerSendRate <= 0f || now >= nextContainerSend))
            {
                if (containerSendRate > 0f) nextContainerSend = now + 1f / containerSendRate;
                foreach (var idx in myOwned)
                {
                    var t = CSnap(idx);
                    if (t != null) PoseContainerServerRpc(idx, t.position, t.rotation);
                }
            }

            // (2) 소유권 상실 감지(핸드오프) → 내 손 강제 해제 + 쿨다운
            if (myOwned.Count > 0)
            {
                _tmpA.Clear();
                foreach (var idx in myOwned)
                {
                    int li = FindHeld(idx);
                    if (li >= 0 && nHeld[li].Owner != me) _tmpA.Add(idx);
                }
                foreach (var idx in _tmpA) LoseOwnership(idx, now);
            }

            // (3) 남이 든 컨테이너 적용(kinematic + 보간)
            float k = smooth <= 0f ? 1f : 1f - Mathf.Exp(-smooth * Time.deltaTime);
            _activeNow.Clear();
            for (int i = 0; i < nHeld.Count; i++)
            {
                var e = nHeld[i];
                _activeNow.Add(e.Index);
                if (e.Owner == me) continue;        // 내가 든 건 XR이 직접 움직임
                if (IsCraneHeld(e.Index)) continue;
                var t = CSnap(e.Index);
                if (t == null) continue;
                EnsureKinematic(e.Index, t);
                t.position = Vector3.Lerp(t.position, e.Pos, k);
                t.rotation = Quaternion.Slerp(t.rotation, e.Rot, k);
                appliedRemote.Add(e.Index);
            }

            // (4) 놓여서 목록에서 빠진 컨테이너 → 물리 복원
            if (appliedRemote.Count > 0)
            {
                _tmpB.Clear();
                foreach (var idx in appliedRemote) if (!_activeNow.Contains(idx)) _tmpB.Add(idx);
                foreach (var idx in _tmpB) RestoreContainerPhysics(idx);
            }

            // (5) 쿨다운 끝난 interactable 재활성
            if (grabCooldownUntil.Count > 0)
            {
                _tmpB.Clear();
                foreach (var kv in grabCooldownUntil) if (now >= kv.Value) _tmpB.Add(kv.Key);
                foreach (var idx in _tmpB)
                {
                    grabCooldownUntil.Remove(idx);
                    var g = GrabOf(idx); if (g != null && !g.enabled) g.enabled = true;
                }
            }
        }

        void LoseOwnership(int idx, float now)
        {
            myOwned.Remove(idx);
            var g = GrabOf(idx);
            if (g != null) g.enabled = false;            // 손에서 즉시 떨어뜨림(XR 선택 취소)
            grabCooldownUntil[idx] = now + handoffCooldown;
        }

        void EnsureKinematic(int idx, Transform t)
        {
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) return;
            if (!origKinematic.ContainsKey(idx)) origKinematic[idx] = rb.isKinematic;
            rb.isKinematic = true;
        }

        void RestoreContainerPhysics(int idx)
        {
            appliedRemote.Remove(idx);
            var t = CSnap(idx);
            var rb = t != null ? t.GetComponent<Rigidbody>() : null;
            if (rb != null) rb.isKinematic = origKinematic.TryGetValue(idx, out var k0) && k0;
            origKinematic.Remove(idx);
        }

        // 서버 권위 소유권/포즈/해제(RequireOwnership=false — 컨테이너는 NetworkObject 아님)
        [ServerRpc(RequireOwnership = false)]
        void ClaimContainerServerRpc(int idx, Vector3 pos, Quaternion rot, ServerRpcParams p = default)
        {
            if (IsCraneHeld(idx)) return;               // 크레인 화물은 가로채기 불가
            ulong sender = p.Receive.SenderClientId;
            var e = new HeldContainer { Index = idx, Owner = sender, Pos = pos, Rot = rot };
            int li = FindHeld(idx);
            if (li >= 0) nHeld[li] = e; else nHeld.Add(e);   // 마지막 집기 우선(핸드오프)
            SessionEventLog.Record("hand_grab", sender, "C" + idx);
        }

        [ServerRpc(RequireOwnership = false)]
        void PoseContainerServerRpc(int idx, Vector3 pos, Quaternion rot, ServerRpcParams p = default)
        {
            int li = FindHeld(idx); if (li < 0) return;
            var e = nHeld[li];
            if (e.Owner != p.Receive.SenderClientId) return;   // 주인만 갱신(뺏긴 자의 늦은 패킷 무시)
            e.Pos = pos; e.Rot = rot; nHeld[li] = e;
        }

        [ServerRpc(RequireOwnership = false)]
        void ReleaseContainerServerRpc(int idx, Vector3 pos, Quaternion rot, ServerRpcParams p = default)
        {
            int li = FindHeld(idx); if (li < 0) return;
            if (nHeld[li].Owner != p.Receive.SenderClientId) return;   // 이미 남이 가져갔으면 무시
            nHeld.RemoveAt(li);
            SessionEventLog.Record("hand_release", p.Receive.SenderClientId, "C" + idx);
        }
    }
}
