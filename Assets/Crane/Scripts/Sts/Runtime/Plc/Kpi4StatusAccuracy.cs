using System.IO;
using System.Text;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>지표 4 — O&amp;M 상태표시 정확도: PLC 가 말하는 상태 vs XR 이 화면에 보여주는 상태를 100회 대조한다.
    /// 채널 넷을 따로 센다 — A 알람 코드(알람 HUD) · B 알람 위치(부품 강조) · C 운전 상태(HUD) · D 하중(HUD).
    /// 시행 통과 = 네 채널 모두 맞음. 실행 순서 100: PlcBridge(-100) 구동 뒤.</summary>
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("AI-XR Crane/STS Crane/KPI 지표4 (O&M 상태표시 정확도)")]
    [RequireComponent(typeof(PlcBridge))]
    [DisallowMultipleComponent]
    public sealed class Kpi4StatusAccuracy : MonoBehaviour
    {
        [Tooltip("반복 시행 수. 계획서 판정 단위(100회).")]
        [SerializeField, Min(1)] int trials = 100;
        [Tooltip("목표 충족률(%). 1차년도 90, 2차 KOLAS 95.")]
        [SerializeField, Range(0f, 100f)] float targetPercent = 90f;
        [Tooltip("시행 간격(초) — 이어 붙인 시나리오 전 구간이 고루 잡히게.")]
        [SerializeField, Min(0.1f)] float sampleIntervalS = 2.5f;
        [Tooltip("알람이 바뀐 직후 이 시간(초)은 화면이 따라올 여유로 건너뛴다(HUD·강조 갱신 4Hz).")]
        [SerializeField, Min(0f)] float settleS = 0.6f;
        [Tooltip("하중 허용오차(t).")]
        [SerializeField] float loadTolT = 1f;
        [SerializeField] bool writeCsv = true;

        static readonly string[] Ch = { "A 알람 코드", "B 알람 위치", "C 운전 상태", "D 하중" };

        StsCrane crane;
        PlcBridge bridge;
        CraneAlarmHighlight highlight;
        float sampleT, changedAt;
        int lastTruthCode = -1;
        int n, passN;
        readonly int[] chPass = new int[4];
        int alarmTrials;
        StringBuilder csv;
        bool done;

        public bool Done => done;
        public float PassPercent => n > 0 ? passN * 100f / n : 0f;
        /// <summary>채널별 충족률(%) — 0 A 코드, 1 B 위치, 2 C 운전 상태, 3 D 하중.</summary>
        public float ChannelPercent(int i) => n > 0 ? chPass[i] * 100f / n : 0f;

        void Awake()
        {
            crane = GetComponent<StsCrane>();
            bridge = GetComponent<PlcBridge>();
            csv = new StringBuilder("trial,t_s,plc_code,plc_sev,xr_code,code_ok,part,part_lit,plc_op,xr_op,op_ok,plc_load_t,xr_load_t,load_ok,pass\n");
        }

        void Update()
        {
            if (done || crane == null || bridge == null || !bridge.Active) return;
            if (highlight == null) highlight = FindAnyObjectByType<CraneAlarmHighlight>();

            var s = bridge.Latest;
            var truth = s.AlarmActive ? AlarmCodebook.Get(s.AlarmCode) : null;
            if (truth != null && !truth.xr) truth = null;   // XR 비대상 알람은 화면에 안 띄우는 게 정답
            int truthCode = truth != null ? truth.code : 0;
            if (truthCode != lastTruthCode) { lastTruthCode = truthCode; changedAt = Time.time; }

            sampleT += Time.deltaTime;
            if (sampleT < sampleIntervalS || Time.time - changedAt < settleS) return;
            sampleT = 0f;

            // A — 알람 HUD 가 띄우는 코드(CraneAlarmHUD 와 같은 출처)
            int xrCode = Net.CraneNetSync.ActiveAlarmCode(crane);
            bool codeOk = xrCode == truthCode;

            // B — 알람 부품이 그 심각도 이상으로 강조됐나. 부품이 없는 알람(통신·기상 등)·정상은 대상 아님 → 통과.
            string part = truth != null && truth.Severity >= FaultSeverity.Warning ? truth.part : "";
            bool partOk = string.IsNullOrEmpty(part) || (highlight != null && highlight.IsLit(crane, part, truth.Severity));

            // C — 운전 상태: 비상정지·심각(Critical 이상) 알람 = 이상, 운전 비트 = 운전, 그 외 정지
            OpMode plcOp = s.EmergencyStop || (truth != null && truth.Severity >= FaultSeverity.Critical) ? OpMode.Fault
                         : s.OpRunning ? OpMode.Running : OpMode.Stopped;
            OpMode xrOp = Net.CraneNetSync.ActiveOpMode(crane);
            bool opOk = plcOp == xrOp;

            // D — 하중: HUD 가 보여주는 값(CraneStatusHUD.DisplayLoadTons)
            float xrLoad = CraneStatusHUD.DisplayLoadTons(crane);
            bool loadOk = Mathf.Abs(s.HoLoad - xrLoad) <= loadTolT;

            bool pass = codeOk && partOk && opOk && loadOk;
            n++;
            if (pass) passN++;
            if (codeOk) chPass[0]++;
            if (partOk) chPass[1]++;
            if (opOk) chPass[2]++;
            if (loadOk) chPass[3]++;
            if (truth != null) alarmTrials++;

            if (writeCsv)
                csv.Append(n).Append(',').Append(Time.time.ToString("F1")).Append(',')
                   .Append(truthCode).Append(',').Append(truth != null ? truth.sev : -1).Append(',')
                   .Append(xrCode).Append(',').Append(codeOk ? 1 : 0).Append(',')
                   .Append(part).Append(',').Append(partOk ? 1 : 0).Append(',')
                   .Append(plcOp).Append(',').Append(xrOp).Append(',').Append(opOk ? 1 : 0).Append(',')
                   .Append(s.HoLoad.ToString("F1")).Append(',').Append(xrLoad.ToString("F1")).Append(',').Append(loadOk ? 1 : 0).Append(',')
                   .Append(pass ? 1 : 0).Append('\n');

            if (n >= trials) Finish();
        }

        void Finish()
        {
            done = true;
            bool pass = PassPercent >= targetPercent;
            string path = writeCsv ? WriteCsv() : "";
            var sb = new StringBuilder($"[Kpi4] 지표4 O&M 상태표시 정확도 — n={n} (알람 있는 시행 {alarmTrials}) 소스={bridge.Source?.Name}\n");
            for (int i = 0; i < 4; i++) sb.Append($"  {Ch[i]} {ChannelPercent(i):F1}%\n");
            sb.Append($"  종합(네 채널 동시) {PassPercent:F1}%  목표 {targetPercent:F0}%  → {(pass ? "PASS" : "FAIL")}");
            if (!string.IsNullOrEmpty(path)) sb.Append($"\n  → {path}");
            Debug.Log(sb.ToString());
            QaLog.Check("KPI4", "result", pass, $"n={n} pass={PassPercent:F1}% A={ChannelPercent(0):F1} B={ChannelPercent(1):F1} C={ChannelPercent(2):F1} D={ChannelPercent(3):F1}");
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
                string p = Path.Combine(dir, $"kpi4_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(p, csv.ToString(), new UTF8Encoding(true));
                return p;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Kpi4] CSV 쓰기 실패(측정 결과는 위 로그가 전부): {e.Message}");
                return "";
            }
        }
    }
}
