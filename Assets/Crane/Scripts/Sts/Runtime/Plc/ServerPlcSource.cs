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

        public void Pump(float dt)
        {
            if (thread == null) (thread = new Thread(PollLoop) { IsBackground = true, Name = "ServerPlc" }).Start();

            lock (gate)
            {
                for (int i = 0; i < inTimes.Count; i++)
                {
                    if (times.Count > 0 && inTimes[i] < times[times.Count - 1]) { frames.Clear(); times.Clear(); wrapped = true; }   // 새 런(피더 반복·PLC 재시작)
                    frames.Add(inFrames[i]); times.Add(inTimes[i]);
                }
                inFrames.Clear(); inTimes.Clear();
            }
            LogState();
            if (times.Count == 0) return;

            float newest = times[times.Count - 1];
            playT += dt;
            if (wrapped || playT < newest - MaxLagS) playT = newest - DelayS;
            if (playT > newest) playT = newest;   // 새 행이 안 오면 마지막 자세에서 멈춘다
            int drop = 0;                          // 재생 위치 직전 한 칸만 남긴다
            while (drop + 1 < times.Count && times[drop + 1] <= playT) drop++;
            if (drop > 0) { frames.RemoveRange(0, drop); times.RemoveRange(0, drop); }
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
                        lock (gate) { inFrames.AddRange(f); inTimes.AddRange(t); }
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
