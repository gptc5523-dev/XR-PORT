using Unity.Netcode;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Net
{
    public sealed partial class CraneNetSync
    {
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
    }
}
