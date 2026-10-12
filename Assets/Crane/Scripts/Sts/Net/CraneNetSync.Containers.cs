using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AIXRCrane.Crane.Sts.Net
{
    public sealed partial class CraneNetSync
    {
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
