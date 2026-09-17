using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using UnityEngine;

namespace Container.Crane.Sts.Plc
{
    /// <summary>
    /// 통합서버 PLC 소스 — 서버(Server/xrcrane_db.py)에 쌓이는 스냅샷을 읽어 크레인을 움직인다.
    /// 흐름: 외부 PLC → 서버 /ingest → (여기) /since 폴링 → PlcBridge → 3축. 오너 2026-09-17 "서버에서 데이터 읽어서 크레인 움직이게".
    /// 앱 안 가상 PLC(<see cref="VirtualPlcSource"/>)는 오너 지시 임시였고, 이게 그 자리를 잇는 소스다.
    ///
    ///   · 폴링은 백그라운드 스레드 — 네트워크 대기가 물리틱을 막지 않게. 행은 서버 id 로 이어 받는다(X-Last-Id).
    ///     스레드는 첫 Pump 에서 띄운다 — 시연 감독이 PlcBridge 를 꺼 두면 서버를 두드리지 않는다.
    ///   · 파싱은 <see cref="CsvReplaySource"/> 파서 그대로 — 서버가 PlcSim CSV 와 같은 헤더로 돌려줘 태그 계약이 한 곳이다.
    ///   · 최신 행보다 DelayS 뒤를 보간 재생 — PLC 100ms 그리드를 계단으로 따라가면 위치 미분이 가속 알람을 낸다(H5 와 같은 이유).
    ///   · 서버가 끊기면 마지막 자세에서 멈춘다. 가상 데이터로 폴백하지 않는다 — 가짜 움직임은 연결된 것처럼 보이게 한다.
    /// </summary>
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

        /// <summary>직전 Pump 에서 t_ms 가 되돌아갔으면(새 런) true 1회 — PlcBridge 가 가속 추적을 재프라임한다(H5).</summary>
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
