using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>지표 1 — PLC→XR 갱신 지연: PLC 시각(plc_ms) → 서버 수신(recv_ms) → 그 값을 그린 프레임의 렌더 완료(xr_ms)를 100회 잰다.</summary>
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("AI-XR Crane/STS Crane/KPI 지표1 (PLC→XR 지연)")]
    [RequireComponent(typeof(PlcBridge))]
    [DisallowMultipleComponent]
    public sealed class Kpi1Latency : MonoBehaviour
    {
        [Tooltip("반복 시행 수. 계획서 판정 단위(100회).")]
        [SerializeField, Min(1)] int trials = 100;
        [Tooltip("시행 통과 한계(ms). 1차년도 2초, 2차 KOLAS 1초.")]
        [SerializeField, Min(1f)] float targetMs = 2000f;
        [Tooltip("지표 충족에 필요한 통과 비율(%). KOLAS 판정식(≥98%)을 1차 한계에 적용.")]
        [SerializeField, Range(0f, 100f)] float requiredPercent = 98f;
        [Tooltip("시행 간격(초) — 100ms 연속 행은 서로 묶여 있어 띄워 잰다.")]
        [SerializeField, Min(0f)] float sampleIntervalS = 1f;
        [SerializeField] bool writeCsv = true;

        PlcBridge bridge;
        long lastPlc, pendPlc, pendRecv;
        bool pending, done;
        float nextSample;
        int n, passN, badOrder;
        readonly List<long> plcToRecv = new List<long>(), recvToXr = new List<long>(), plcToXr = new List<long>();
        StringBuilder csv;

        public bool Done => done;
        public int Trials => n;
        public int PassCount => passN;
        public float PassPercent => n > 0 ? passN * 100f / n : 0f;
        public bool Met => n > 0 && PassPercent >= requiredPercent;
        public double MeanMs => Mean(plcToXr);
        public long MaxMs => plcToXr.Count > 0 ? Max(plcToXr) : 0;

        void Awake()
        {
            bridge = GetComponent<PlcBridge>();
            csv = new StringBuilder("trial,plc_ms,recv_ms,xr_ms,plc_to_recv_ms,recv_to_xr_ms,plc_to_xr_ms,order_ok,pass\n");
        }

        void OnEnable() => RenderPipelineManager.endContextRendering += OnRendered;
        void OnDisable() => RenderPipelineManager.endContextRendering -= OnRendered;

        // PlcBridge 가 FixedUpdate 에서 축을 그 행으로 옮긴 뒤 — 새 행이 화면 자세가 된 프레임을 찜한다.
        void LateUpdate()
        {
            if (done || pending || !(bridge.Source is ServerPlcSource src)) return;
            if (!src.TryShownStamp(out long plc, out long recv) || plc == lastPlc) return;
            lastPlc = plc;
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + sampleIntervalS;
            pendPlc = plc; pendRecv = recv; pending = true;
        }

        // 그 프레임의 렌더가 끝난 순간이 XR 표시 시각 — 세 시각 모두 같은 벽시계(epoch ms)라 기기가 같으면 바로 뺀다.
        void OnRendered(ScriptableRenderContext ctx, List<Camera> cams)
        {
            if (!pending) return;
            pending = false;
            long xr = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long a = pendRecv - pendPlc, b = xr - pendRecv, total = xr - pendPlc;
            bool orderOk = a >= 0 && b >= 0;   // 음수면 시계가 어긋난 것 — 잰 값이 아니므로 실패로 센다
            bool pass = orderOk && total <= targetMs;
            n++;
            if (pass) passN++;
            if (!orderOk) badOrder++;
            plcToRecv.Add(a); recvToXr.Add(b); plcToXr.Add(total);
            if (writeCsv)
                csv.Append(n).Append(',').Append(pendPlc).Append(',').Append(pendRecv).Append(',').Append(xr).Append(',')
                   .Append(a).Append(',').Append(b).Append(',').Append(total).Append(',')
                   .Append(orderOk ? 1 : 0).Append(',').Append(pass ? 1 : 0).Append('\n');
            if (n >= trials) Finish();
        }

        void Finish()
        {
            done = true;
            string path = writeCsv ? WriteCsv() : "";
            var sb = new StringBuilder($"[Kpi1] 지표1 PLC→XR 지연 — n={n} 소스={bridge.Source?.Name} 한계 {targetMs:F0}ms\n");
            sb.Append($"  PLC→서버 평균 {Mean(plcToRecv):F0} / 최대 {Max(plcToRecv)} ms\n");
            sb.Append($"  서버→XR 평균 {Mean(recvToXr):F0} / 최대 {Max(recvToXr)} ms\n");
            sb.Append($"  PLC→XR 평균 {MeanMs:F0} / p95 {P95(plcToXr)} / 최대 {MaxMs} ms\n");
            sb.Append($"  통과 {passN}/{n} ({PassPercent:F1}%) 필요 {requiredPercent:F0}% → {(Met ? "PASS" : "FAIL")}");
            if (badOrder > 0) sb.Append($"  · 시각 역전 {badOrder}건(시계 확인)");
            if (!string.IsNullOrEmpty(path)) sb.Append($"\n  → {path}");
            Debug.Log(sb.ToString());
            QaLog.Check("KPI1", "result", Met, $"n={n} pass={passN} mean={MeanMs:F0}ms p95={P95(plcToXr)}ms max={MaxMs}ms bad_order={badOrder}");
        }

        static double Mean(List<long> v) { if (v.Count == 0) return 0; double s = 0; foreach (var x in v) s += x; return s / v.Count; }
        static long Max(List<long> v) { long m = long.MinValue; foreach (var x in v) if (x > m) m = x; return v.Count > 0 ? m : 0; }
        static long P95(List<long> v)
        {
            if (v.Count == 0) return 0;
            var s = new List<long>(v); s.Sort();
            return s[Mathf.Clamp(Mathf.CeilToInt(s.Count * 0.95f) - 1, 0, s.Count - 1)];
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
                string p = Path.Combine(dir, $"kpi1_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(p, csv.ToString(), new UTF8Encoding(true));
                return p;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Kpi1] CSV 쓰기 실패(측정 결과는 위 로그가 전부): {e.Message}");
                return "";
            }
        }
    }
}
