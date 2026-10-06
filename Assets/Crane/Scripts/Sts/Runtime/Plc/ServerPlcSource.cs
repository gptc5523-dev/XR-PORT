using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>통합서버 PLC 소스 — 서버(Server/xrcrane_db.py)의 스냅샷을 폴링해 크레인 3축을 움직인다.
    /// 서버가 끊기면 마지막 자세에서 멈춘다(가상 데이터로 폴백하지 않음 — 침묵 실패 방지).</summary>
    public sealed class ServerPlcSource : IPlcSource, IDisposable
    {
        const float DelayS = 0.2f;   // 최신 행보다 이만큼 뒤를 재생 — PLC 2스캔(100ms) 여유
        const float MaxLagS = 1f;    // 이보다 뒤처지면(몰아서 도착) 최신 − DelayS 로 건너뛴다
        const int PollMs = 50;

        readonly string url, crane;
        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        readonly object gate = new object();
        readonly List<PlcSnapshot> inFrames = new List<PlcSnapshot>(), frames = new List<PlcSnapshot>();
        readonly List<float> inTimes = new List<float>(), times = new List<float>();
        readonly List<long> inPlc = new List<long>(), plcMs = new List<long>(), inRecv = new List<long>(), recvMs = new List<long>();   // 지표1 스탬프(epoch ms, 행과 같은 순서)
        Thread thread;
        volatile bool running = true, online;
        volatile string error, missing;
        int shown = -1;   // 마지막으로 로그한 연결 상태(−1 모름 · 0 끊김 · 1 연결)
        bool warnedMissing, wrapped;
        long lastId = -1;
        float playT;

        public ServerPlcSource(string baseUrl, string craneName)
        {
            url = baseUrl.TrimEnd('/');
            crane = craneName;
        }

        public string Name => "Server";
        public bool IsConnected => online;

        /// <summary>직전 Pump 에서 t_ms 가 되돌아갔으면(새 런) true 1회 — PlcBridge 가 가속 추적을 재프라임한다.</summary>
        public bool ConsumeDiscontinuity()
        {
            bool w = wrapped; wrapped = false; return w;
        }

        /// <summary>지금 화면에 다 도달한 행(재생 위치가 지난 첫 행)의 PLC·서버 수신 시각(epoch ms). 스탬프 없는 행이면 false.</summary>
        public bool TryShownStamp(out long plc, out long recv)
        {
            plc = recv = 0;
            if (times.Count == 0 || times[0] > playT) return false;
            plc = plcMs[0]; recv = recvMs[0];
            return plc > 0 && recv > 0;
        }

        public void Pump(float dt)
        {
            if (thread == null) (thread = new Thread(PollLoop) { IsBackground = true, Name = "ServerPlc" }).Start();

            lock (gate)
            {
                for (int i = 0; i < inTimes.Count; i++)
                {
                    if (times.Count > 0 && inTimes[i] < times[times.Count - 1]) { frames.Clear(); times.Clear(); plcMs.Clear(); recvMs.Clear(); wrapped = true; }   // 새 런(피더 반복·PLC 재시작)
                    frames.Add(inFrames[i]); times.Add(inTimes[i]); plcMs.Add(inPlc[i]); recvMs.Add(inRecv[i]);
                }
                inFrames.Clear(); inTimes.Clear(); inPlc.Clear(); inRecv.Clear();
            }
            LogState();
            if (times.Count == 0) return;

            float newest = times[times.Count - 1];
            playT += dt;
            if (wrapped || playT < newest - MaxLagS) playT = newest - DelayS;
            if (playT > newest) playT = newest;   // 새 행이 안 오면 마지막 자세에서 멈춘다
            int drop = 0;                          // 재생 위치 직전 한 칸만 남긴다
            while (drop + 1 < times.Count && times[drop + 1] <= playT) drop++;
            if (drop > 0) { frames.RemoveRange(0, drop); times.RemoveRange(0, drop); plcMs.RemoveRange(0, drop); recvMs.RemoveRange(0, drop); }
        }

        public bool TryRead(out PlcSnapshot snap)
        {
            snap = default;
            if (times.Count == 0) return false;
            if (times.Count == 1) { snap = frames[0]; return true; }
            float span = times[1] - times[0];
            snap = CsvReplaySource.LerpFrame(frames[0], frames[1], span > 1e-6f ? Mathf.Clamp01((playT - times[0]) / span) : 0f);
            return true;
        }

        public void Dispose()
        {
            running = false;
            http.Dispose();
        }

        void PollLoop()
        {
            while (running)
            {
                try
                {
                    string q = $"{url}/since?crane={Uri.EscapeDataString(crane)}" + (lastId >= 0 ? $"&after_id={lastId}" : "");
                    using (var resp = http.GetAsync(q).Result)
                    {
                        resp.EnsureSuccessStatusCode();
                        string text = resp.Content.ReadAsStringAsync().Result;
                        if (resp.Headers.TryGetValues("X-Last-Id", out var ids)) foreach (var id in ids) lastId = long.Parse(id);
                        var miss = new List<string>();
                        CsvReplaySource.ParseCsv(text, out var f, out var t, miss);
                        if (miss.Count > 0) missing = string.Join(", ", miss);
                        var pl = new List<long>(f.Length); var rc = new List<long>(f.Length);
                        ParseStamps(text, pl, rc);
                        if (pl.Count != f.Length) { pl.Clear(); rc.Clear(); for (int i = 0; i < f.Length; i++) { pl.Add(0); rc.Add(0); } }   // 행 수가 어긋나면 스탬프를 버린다(잘못 짝짓기 방지)
                        lock (gate) { inFrames.AddRange(f); inTimes.AddRange(t); inPlc.AddRange(pl); inRecv.AddRange(rc); }
                    }
                    online = true;
                }
                catch (Exception e)
                {
                    if (!running) return;
                    error = e.GetBaseException().Message;
                    online = false;
                }
                Thread.Sleep(PollMs);
            }
        }

        // plc_ms·recv_ms 열을 long 으로 — ParseCsv 는 int·float 라 epoch ms 가 깨진다(WBS 9.6). 빈 줄 건너뛰기는 ParseCsv 와 같다.
        static void ParseStamps(string text, List<long> plc, List<long> recv)
        {
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            if (lines.Length < 2) return;
            var h = lines[0].Split(',');
            int ip = Array.FindIndex(h, x => x.Trim() == "plc_ms"), ir = Array.FindIndex(h, x => x.Trim() == "recv_ms");
            for (int li = 1; li < lines.Length; li++)
            {
                if (string.IsNullOrEmpty(lines[li])) continue;
                var c = lines[li].Split(',');
                plc.Add(ip >= 0 && ip < c.Length && long.TryParse(c[ip], out long p) ? p : 0);
                recv.Add(ir >= 0 && ir < c.Length && long.TryParse(c[ir], out long r) ? r : 0);
            }
        }

        // 연결 상태가 바뀔 때만 로그 — 서버가 아예 안 닿아도 한 번은 경고가 남는다(침묵 실패 방지).
        void LogState()
        {
            int now = online ? 1 : error != null ? 0 : -1;
            if (now != -1 && now != shown)
            {
                shown = now;
                if (now == 1) Debug.Log($"[ServerPlc] {url} 연결 — crane='{crane}' 행을 읽어 3축을 움직입니다.");
                else Debug.LogWarning($"[ServerPlc] {url} 끊김 — 마지막 자세에서 멈춤: {error}");
            }
            if (!warnedMissing && missing != null)
            {
                warnedMissing = true;
                Debug.LogWarning($"[ServerPlc] 서버 행에 없는 태그 → 0으로 처리됨(벤더 태그명/헤더 확인): {missing}");
            }
        }
    }
}
