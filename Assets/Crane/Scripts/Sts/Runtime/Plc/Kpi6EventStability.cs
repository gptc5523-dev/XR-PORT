using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>지표 6 — XR·서버·PLC 이벤트 안정성: PLC 가 보낸 알람 변화(대역의 events CSV)가 마감 안에 XR 알람 표시에 나타난 비율.</summary>
    [AddComponentMenu("AI-XR Crane/STS Crane/KPI 지표6 (이벤트 안정성)")]
    [RequireComponent(typeof(PlcBridge))]
    [DisallowMultipleComponent]
    public sealed class Kpi6EventStability : MonoBehaviour
    {
        [Tooltip("판정할 이벤트 수(발생·해제 각각 1건).")]
        [SerializeField, Min(1)] int trials = 100;
        [Tooltip("이 시간(ms) 안에 화면에 나타나야 성공 — 지표1 한계와 같다.")]
        [SerializeField, Min(1f)] float deadlineMs = 2000f;
        [Tooltip("1차년도 목표(%). 2차 KOLAS 99.")]
        [SerializeField, Range(0f, 100f)] float targetPercent = 85f;
        [SerializeField] bool writeCsv = true;

        /// <summary>PLC 대역(feed --events-out)이 쓰는 보낸 이벤트 CSV(plc_ms,code). 측정 도구가 넣는다.</summary>
        public string eventsPath;

        StsCrane crane;
        PlcBridge bridge;
        readonly List<long> seenT = new List<long>();   // XR 표시 코드가 바뀐 시각(서버 시계 ms)
        readonly List<int> seenCode = new List<int>();
        float nextCheck;
        bool done;
        int n, passN;
        double latSum;
        long latMax;
        StringBuilder csv;

        public bool Done => done;
        public int Trials => n;
        public int PassCount => passN;
        public float PassPercent => n > 0 ? passN * 100f / n : 0f;
        public bool Met => n > 0 && PassPercent >= targetPercent;

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            bridge = GetComponent<PlcBridge>();
            csv = new StringBuilder("trial,plc_ms,code,kind,shown_ms,latency_ms,pass\n");
        }

        void Update()
        {
            if (done || !(bridge.Source is ServerPlcSource src)) return;
            src.TryClock(out long off, out _);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + off;
            int code = CraneLink.ActiveAlarmCode(crane);
            if (seenCode.Count == 0 || seenCode[seenCode.Count - 1] != code) { seenT.Add(now); seenCode.Add(code); }
            if (Time.unscaledTime >= nextCheck) { nextCheck = Time.unscaledTime + 1f; TryJudge(now); }
        }

        // 마감이 지난 이벤트가 trials 개 모이면 한꺼번에 판정 — 늦게 뜬 표시도 마감 안이면 성공.
        void TryJudge(long now)
        {
            if (string.IsNullOrEmpty(eventsPath) || !File.Exists(eventsPath)) return;
            var ev = new List<(long t, int code)>();
            try
            {
                using var fs = new FileStream(eventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var rd = new StreamReader(fs);
                rd.ReadLine();
                string line;
                while ((line = rd.ReadLine()) != null)
                {
                    var c = line.Split(',');
                    if (c.Length >= 2 && long.TryParse(c[0], out long t) && int.TryParse(c[1], out int k) && t >= seenT[0] && t + deadlineMs < now) ev.Add((t, k));
                }
            }
            catch (IOException) { return; }
            if (ev.Count < trials) return;
            for (int i = 0; i < trials; i++) Judge(ev[i].t, ev[i].code);
            Finish();
        }

        void Judge(long t, int code)
        {
            int at = seenT.BinarySearch(t);
            if (at < 0) at = ~at - 1;
            long shown = -1;
            for (int i = Math.Max(at, 0); i < seenT.Count && seenT[i] <= t + deadlineMs; i++)
                if (seenCode[i] == code) { shown = Math.Max(seenT[i], t); break; }
            bool pass = shown >= 0;
            n++;
            if (pass) { passN++; latSum += shown - t; latMax = Math.Max(latMax, shown - t); }
            if (writeCsv)
                csv.Append(n).Append(',').Append(t).Append(',').Append(code).Append(',').Append(code == 0 ? "clear" : "onset").Append(',')
                   .Append(shown).Append(',').Append(pass ? shown - t : -1).Append(',').Append(pass ? 1 : 0).Append('\n');
        }

        void Finish()
        {
            done = true;
            string path = writeCsv ? WriteCsv() : "";
            Debug.Log($"[Kpi6] 지표6 이벤트 안정성 — n={n} 마감 {deadlineMs:F0}ms 소스={bridge.Source?.Name}\n" +
                      $"  성공 {passN}/{n} ({PassPercent:F1}%) 목표 {targetPercent:F0}% → {(Met ? "PASS" : "FAIL")}\n" +
                      $"  표시까지 평균 {(passN > 0 ? latSum / passN : 0):F0} / 최대 {latMax} ms" +
                      (string.IsNullOrEmpty(path) ? "" : $"\n  → {path}"));
            QaLog.Check("KPI6", "result", Met, $"n={n} pass={passN} mean={(passN > 0 ? latSum / passN : 0):F0}ms max={latMax}ms");
        }

        string WriteCsv()
        {
            try
            {
#if UNITY_EDITOR
                string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KPI");
#else
                string dir = Path.Combine(Application.persistentDataPath, "KPI");
#endif
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, $"kpi6_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(p, csv.ToString(), new UTF8Encoding(true));
                return p;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Kpi6] CSV 쓰기 실패(측정 결과는 위 로그가 전부): {e.Message}");
                return "";
            }
        }
    }
}
