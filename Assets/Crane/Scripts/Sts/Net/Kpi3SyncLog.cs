using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Net
{
    /// <summary>지표 3(멀티유저 동기화) 로그 — 접속 중 크레인마다 화면 상태를 Netcode ServerTime 과 함께 남긴다. 판정은 Server/kpi3_merge.py.</summary>
    public sealed class Kpi3SyncLog : MonoBehaviour
    {
        public const string EditorPrefKey = "Kpi3SyncLog.On";
        const float HostEveryS = 0.05f, ClientEveryS = 1f;   // 호스트는 기준선이라 촘촘히, 관전자는 시행 단위(1초)

        readonly List<StsCrane> cranes = new List<StsCrane>();
        readonly Dictionary<StsCrane, SpreaderLockAnimator> locks = new Dictionary<StsCrane, SpreaderLockAnimator>();
        StreamWriter w;
        float next, nextScan;

        // 기기·빌드는 실행 인자 -kpi3, 에디터는 메뉴 PLC/지표3 동기화 로그 로 켠다 — 평소엔 아예 안 뜬다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            bool on = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-kpi3") >= 0;
#if UNITY_EDITOR
            on |= UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#endif
            if (on) CraneHud.EnsureSpawned<Kpi3SyncLog>("Kpi3SyncLog");
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !(nm.IsServer || nm.IsConnectedClient)) return;
            if (Time.unscaledTime >= nextScan) Scan();
            bool host = nm.IsServer;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + (host ? HostEveryS : ClientEveryS);
            if (w == null) Open(host, nm.LocalClientId);

            double t = nm.ServerTime.Time;
            foreach (var c in cranes)
            {
                if (c == null) continue;
                locks.TryGetValue(c, out var la);
                var at = c.Attach;
                w.Write($"{t:F3},{(host ? "host" : "client")},{nm.LocalClientId},{c.name},{M(c.Gantry):F4},{M(c.Trolley):F4},{M(c.Spreader):F4}," +
                        $"{CraneNetSync.ActiveAlarmCode(c)},{(int)CraneNetSync.ActiveOpMode(c)},{(la != null && la.Locked ? 1 : 0)}," +
                        $"{(at != null && at.HasContainer ? at.AttachedDisplayId : "")}\n");
            }
        }

        static float M(IAxisMover a) => a != null ? a.Current * a.MetersPerUnit() : 0f;

        void Scan()
        {
            nextScan = Time.unscaledTime + 2f;
            foreach (var c in FindObjectsByType<StsCrane>())
                if (!cranes.Contains(c)) { cranes.Add(c); locks[c] = c.GetComponentInChildren<SpreaderLockAnimator>(true); }
        }

        void Open(bool host, ulong id)
        {
#if UNITY_EDITOR
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KPI");
#else
            string dir = Path.Combine(Application.persistentDataPath, "KPI");
#endif
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, $"kpi3_log_{(host ? "host" : "client")}{id}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            w = new StreamWriter(p, false, new UTF8Encoding(false)) { AutoFlush = true };
            w.Write("server_time_s,role,client_id,crane,gantry_m,trolley_m,hoist_m,alarm,opmode,locked,attached\n");
            Debug.Log($"[Kpi3] 동기화 로그 → {p}");
        }

        void OnDestroy() => w?.Dispose();
    }
}
