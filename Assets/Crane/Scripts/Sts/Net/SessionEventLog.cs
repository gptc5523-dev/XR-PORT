using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>세션 조작·선택 이력(계획서 그림 5 '이벤트 순서 제어') — 호스트가 처리한 순서대로 seq 를 매겨 통합서버 POST /events 로 보낸다. 한 번에 한 묶음만 보내 순서가 바뀌지 않고, 실패하면 남겨 두었다 다시 보낸다(서버가 중복을 버린다).</summary>
    public sealed class SessionEventLog : MonoBehaviour
    {
        [Serializable] class Ev { public long seq, t_ms, actor_id; public string actor, type, target, detail; [NonSerialized] public string sess; }
        [Serializable] class Batch { public string session; public Ev[] events; }

        const int MaxQueue = 5000, MaxBatch = 200;
        static readonly List<Ev> queue = new();
        static string session;
        static long seq;

        HttpClient http;
        string url;
        Task<HttpResponseMessage> inflight;
        int inflightCount;
        bool warned;
        float nextFlush, nextControl;
        NetworkManager hooked;
        string lastControl = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => CraneHud.EnsureSpawned<SessionEventLog>("SessionEventLog");

        /// <summary>호스트에서만 남는다 — actorId 는 Netcode 클라이언트 ID, target 은 부품 '크레인|ID'·컨테이너 'C'+번호 등.</summary>
        public static void Record(string type, ulong actorId, string target, string detail = "")
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || session == null) return;
            queue.Add(new Ev { sess = session, seq = ++seq, t_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), actor_id = (long)actorId,
                               actor = ActorLabel(actorId), type = type, target = target ?? "", detail = detail ?? "" });
            if (queue.Count > MaxQueue) queue.RemoveAt(0);   // 서버가 오래 안 닿으면 오래된 것부터 버린다
        }

        static string ActorLabel(ulong id)
        {
            foreach (var a in FindObjectsByType<PlayerAvatarSync>())
                if (a.IsSpawned && a.OwnerClientId == id) return a.Label.Value.ToString();
            return "";
        }

        void Start()
        {
            url = Plc.ServerPlcSource.ResolveUrl(Plc.PlcBridge.DefaultServerUrl).TrimEnd('/') + "/events";
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            string token = Plc.ServerPlcSource.Token();
            if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.Add("X-Auth-Token", token);
        }

        void OnDestroy()
        {
            if (hooked != null) Unhook(hooked);
            http?.Dispose();
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm != hooked) { if (hooked != null) Unhook(hooked); if (nm != null) Hook(nm); hooked = nm; }
            if (nm != null && nm.IsServer && session != null && Time.unscaledTime >= nextControl) { nextControl = Time.unscaledTime + 0.25f; WatchControl(nm); }
            if (Time.unscaledTime >= nextFlush) { nextFlush = Time.unscaledTime + 1f; Flush(); }
        }

        void Hook(NetworkManager nm)
        {
            nm.OnServerStarted += OnStarted;
            nm.OnServerStopped += OnStopped;
            nm.OnClientConnectedCallback += OnJoin;
            nm.OnClientDisconnectCallback += OnLeave;
        }

        void Unhook(NetworkManager nm)
        {
            nm.OnServerStarted -= OnStarted;
            nm.OnServerStopped -= OnStopped;
            nm.OnClientConnectedCallback -= OnJoin;
            nm.OnClientDisconnectCallback -= OnLeave;
        }

        void OnStarted()
        {
            session = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            seq = 0;
            lastControl = "";
            Record("session_start", NetworkManager.Singleton.LocalClientId, Plc.PlcLiveMode.On ? "PLC 모니터링" : "크레인 조종", DeviceIdentity.Label);
        }

        void OnStopped(bool _) { Record("session_end", NetworkManager.ServerClientId, ""); session = null; }
        void OnJoin(ulong id) { if (id != NetworkManager.ServerClientId) Record("join", id, ""); }
        void OnLeave(ulong id) => Record("leave", id, "");

        // 호스트의 운전 시작·끝 — 조종 모드(갠트리·트롤리)이거나 운전실 시점이면 운전 중(PortDemoDirector 와 같은 판정).
        void WatchControl(NetworkManager nm)
        {
            var c = StsCraneVRController.Active;
            var crane = c != null ? c.GetComponent<StsCrane>() : null;
            string now = crane != null && (c.CabView || (c.ControlActive && c.CraneMode)) ? crane.name : "";
            if (now == lastControl) return;
            if (lastControl != "") Record("control_end", nm.LocalClientId, lastControl);
            if (now != "") Record("control_start", nm.LocalClientId, now);
            lastControl = now;
        }

        void Flush()
        {
            if (inflight != null)
            {
                if (!inflight.IsCompleted) return;
                bool ok = inflight.Status == TaskStatus.RanToCompletion && inflight.Result.IsSuccessStatusCode;
                if (ok) { queue.RemoveRange(0, Mathf.Min(inflightCount, queue.Count)); warned = false; }
                else if (!warned) { warned = true; Debug.LogWarning($"[EventLog] 서버 전송 실패 — {queue.Count}건 남겨 두고 다시 보냄 ({url})"); }
                if (inflight.Status == TaskStatus.RanToCompletion) inflight.Result.Dispose();
                inflight = null;
            }
            if (queue.Count == 0 || http == null) return;
            // 같은 세션끼리만 한 묶음 — 세션이 바뀌는 사이 못 보낸 이전 것이 새 세션 이름으로 붙지 않게.
            string sess = queue[0].sess;
            inflightCount = 0;
            while (inflightCount < queue.Count && inflightCount < MaxBatch && queue[inflightCount].sess == sess) inflightCount++;
            var batch = new Batch { session = sess, events = queue.GetRange(0, inflightCount).ToArray() };
            inflight = http.PostAsync(url, new StringContent(JsonUtility.ToJson(batch), Encoding.UTF8, "application/json"));
        }
    }
}
