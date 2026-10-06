using System.IO;
using UnityEngine;

namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>PLC ↔ 크레인 다리 — IPlcSource(Virtual/CsvReplay/Server) 스냅샷을 축 구동에 흘린다. Active면 PlcDriven=true(가속알람 유효).
    /// ★ 가속 측정(CraneOpMode)보다 먼저 실행돼야 같은 입력이 같은 알람을 낸다(H4). 가설 구현·미검증.</summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("AI-XR Crane/STS Crane/PLC Bridge (가상·실 PLC 시임)")]
    [RequireComponent(typeof(StsCrane))]
    [DisallowMultipleComponent]
    public sealed class PlcBridge : MonoBehaviour
    {
        public enum SourceMode { Virtual, CsvReplay, Server }   // 순서 고정 — 씬에 정수로 저장된다

        [Tooltip("켜면 PLC 소스가 3축을 구동하고 PlcDriven=true. 끄면 직접조종(VR) 유지. 둘은 상호배타.")]
        [SerializeField] bool active = false;

        [Tooltip("Virtual=실시간 가상 생성 / CsvReplay=PlcSim 생성 CSV 재생 / Server=통합서버에서 읽기.")]
        [SerializeField] SourceMode sourceMode = SourceMode.Virtual;

        [Header("Virtual 모드")]
        [Tooltip("급조작 주입 — 정격 초과 가속으로 구동해 가속알람(1021/2021/3021)을 자연발생(데모/검증용).")]
        [SerializeField] bool injectAggressive = false;

        [Header("CsvReplay 모드")]
        [Tooltip("Assets 안에 둔 CSV(TextAsset). 빌드/Quest 포함. 비우면 csvPath 사용.")]
        [SerializeField] TextAsset csvAsset;
        [Tooltip("CSV 절대/상대 경로(에디터·스탠드얼론 전용). 예: <프로젝트>/PlcSim/output/S02/run_01.csv")]
        [SerializeField] string csvPath = "";
        [SerializeField] bool loop = true;

        [Header("Server 모드")]
        [Tooltip("통합서버(Server/xrcrane_db.py) 주소. 크레인 이름(gameObject.name)을 DB 의 crane 값으로 조회한다.")]
        [SerializeField] string serverUrl = "http://192.168.0.167:5006";

        [Header("정규화 범위 (실척 m)")]
        // 0이면 무버 기하에서 자동 산출(rangeM = (Max−Min)×(1/ModelScale), SSOT=무버). 0이 아니면 수동 오버라이드.
        [Tooltip("0이면 모델 기하에서 자동 산출(권장). 0이 아니면 그 값을 수동 사용.")]
        [SerializeField] float gtRangeM = 0f;
        [Tooltip("0이면 모델 기하에서 자동 산출(권장). 0이 아니면 그 값을 수동 사용.")]
        [SerializeField] float trRangeM = 0f;
        [Tooltip("0이면 모델 기하에서 자동 산출(권장). 0이 아니면 그 값을 수동 사용.")]
        [SerializeField] float hoRangeM = 0f;

        [Header("스캔 주기 (실 PLC 정합) — H4")]
        [Tooltip("PLC 로직 고정 스캔주기(ms). 실 PLC처럼 이 주기 경계에서만 입력 동결→평가→출력. " +
                 "Unity 물리틱(가변·catch-up 가능)과 분리해 동일 입력이 동일 알람을 내도록 결정성 확보. 통상 1~10ms.")]
        // 가시 클램프 [1,100]ms — OnValidate가 직접입력·프리팹값도 재클램프.
        [Range(1f, 100f)]
        [SerializeField] float scanPeriodMs = 10f;

        [Header("진단")]
        [Tooltip("켜면 구동 중 초당 1회 콘솔에 PLC 읽기/축 값 로그(실시간 검증용). 평소엔 꺼둠.")]
        [SerializeField] bool logTelemetry = true;

        StsCrane crane;
        readonly VirtualPlcSource sim = new VirtualPlcSource();
        IPlcSource source;
        float telemetryT;
        bool rangesResolved;   // 무버 기하로 rangeM을 산출했는지(1회)

        // H4 고정 스캔 그리드 — 물리틱 dt를 누산해 scanPeriod 경계에서만 ScanStep을 돈다.
        float scanAccum;
        PlcSnapshot lastSnap;     // 직전 스캔에서 동결한 Process Image(텔레메트리·HUD 공용).
        bool haveSnap;
        const int MaxScansPerTick = 8;   // 프레임 급락 catch-up 폭주(spiral) 방지 상한.

        /// <summary>현재 PLC 소스. Phase B에서 S7/OPC UA 어댑터로 교체.</summary>
        public IPlcSource Source => source;
        public bool Active => active;
        /// <summary>재생 중인 CSV 경로(csvAsset 이면 빈 값) — 옆의 작업 이력(run_NN.history.csv)을 찾는 데 쓴다.</summary>
        public string CsvPath => csvPath;
        /// <summary>정규화 range 를 무버 기하에서 산출했는지. 산출 전엔 WorldAtPose 가 틀린다.</summary>
        public bool RangesResolved => rangesResolved;

        /// <summary>최신 스냅샷(없으면 default) — HUD·WebSocket 송신(추후 항목) 공용 출처.</summary>
        public PlcSnapshot Latest => source != null && source.TryRead(out var s) ? s : default;

        /// <summary>PLC 가 지금 이 크레인을 구동 중이면 최신 스냅샷 — 그동안 화면의 알람·운전 상태·하중은 PLC 값이 출처다(지표 4).</summary>
        public static bool TryLatest(StsCrane crane, out PlcSnapshot s)
        {
            s = default;
            if (crane == null || !crane.TryGetComponent<PlcBridge>(out var b) || !b.isActiveAndEnabled || !b.active) return false;
            s = b.Latest;
            return true;
        }

        void Awake()
        {
            crane = GetComponent<StsCrane>();
#if UNITY_EDITOR
            // 도메인 리로드로 설정이 기본값으로 돌아가도 마지막 메뉴 선택(EditorPrefs)에서 복원.
            if (UnityEditor.EditorPrefs.GetBool(PrefKey("forceReplay"), false))
            {
                string p = UnityEditor.EditorPrefs.GetString(PrefKey("csvPath"), "");
                if (!string.IsNullOrEmpty(p)) { sourceMode = SourceMode.CsvReplay; csvPath = p; csvAsset = null; active = true; }
            }
            // 재생 복원이 먼저 — 남은 서버 선택이 forceReplay 측정을 가로채지 않게.
            else if (UnityEditor.EditorPrefs.GetBool(PrefKey("forceServer"), false))
            {
                sourceMode = SourceMode.Server; active = true;
                serverUrl = UnityEditor.EditorPrefs.GetString(PrefKey("serverUrl"), serverUrl);   // 측정 도구가 로컬 서버를 가리킬 때
            }
#endif
            source = BuildSource();
            // CSV 재생이면 화물 재생도 붙인다 — 안 붙이면 '축은 재생되는데 스프레더는 빈손'. 작업 이력이 없으면 스스로 꺼진다.
            if (active && source is CsvReplaySource && GetComponent<PlcCargoReplay>() == null) gameObject.AddComponent<PlcCargoReplay>();
            Debug.Log($"[PlcBridge] init: mode={sourceMode} active={active} csvAsset={(csvAsset != null)} " +
                      $"csvPath='{csvPath}' → source={source?.Name} connected={source?.IsConnected}");
        }

        // 스캔주기 [1,100]ms 재클램프 — 과대값은 알람 지연, 0·음수는 0으로 나누기. [Range]는 슬라이더만 막는다.
        void OnValidate()
        {
            scanPeriodMs = Mathf.Clamp(scanPeriodMs, 1f, 100f);
        }

        // 떼이거나 꺼지면 PlcDriven을 끈다 — 안 그러면 직접조종에 가속 오경보(1021/2021/3021).
        void OnDisable()
        {
            var c = crane != null ? crane : GetComponent<StsCrane>();
            if (c != null && c.OpMode != null) c.OpMode.PlcDriven = false;
        }

#if UNITY_EDITOR
        /// <summary>에디터 메뉴용 — CsvReplay로 구성(SerializedObject enum 우회).</summary>
        public void EditorConfigureReplay(string path)
        {
            sourceMode = SourceMode.CsvReplay; csvPath = path; csvAsset = null; active = true;
        }
        /// <summary>에디터 메뉴용 — 통합서버에서 읽도록 구성.</summary>
        public void EditorConfigureServer()
        {
            sourceMode = SourceMode.Server; active = true;
        }
        /// <summary>에디터 메뉴용 — Virtual로 구성.</summary>
        public void EditorConfigureVirtual(bool aggressive)
        {
            sourceMode = SourceMode.Virtual; injectAggressive = aggressive; active = true;
        }
        /// <summary>메뉴 선택 복원 키 — 크레인별(전역 키 하나면 STS·RTG가 같은 CSV를 복원해 서로 데이터를 재생한다).</summary>
        public string PrefKey(string k) => PrefKeyFor(gameObject.name, k);
        /// <summary>크레인 이름으로 같은 키 — 배치 스모크가 Play 전에 EditorPrefs 를 쓸 때.</summary>
        public static string PrefKeyFor(string crane, string k) => $"PlcBridge.{k}.{crane}";
#endif

        // 서버 소스의 폴링 스레드를 멈춘다.
        void OnDestroy() => (source as System.IDisposable)?.Dispose();

        IPlcSource BuildSource()
        {
            if (sourceMode == SourceMode.Server) return new ServerPlcSource(serverUrl, gameObject.name);   // 폴백 없음 — 끊기면 멈춘 채로 둔다
            if (sourceMode == SourceMode.CsvReplay)
            {
                string text = LoadCsvText();
                if (!string.IsNullOrEmpty(text))
                {
                    var rep = new CsvReplaySource(text) { Loop = loop };
                    if (rep.MissingColumns.Count > 0)   // 침묵 실패 방지 — 헤더에 없는 태그 컬럼을 경고로
                        Debug.LogWarning($"[PlcBridge] CSV 누락 컬럼 {rep.MissingColumns.Count}개 → 0으로 처리됨(벤더 태그명/헤더 확인): {string.Join(", ", rep.MissingColumns)}");
                    if (rep.IsConnected) return rep;
                    Debug.LogWarning("[PlcBridge] CSV 파싱 결과가 비어 Virtual로 폴백.");
                }
                else
                {
                    Debug.LogWarning("[PlcBridge] CSV를 못 읽어 Virtual로 폴백 (csvAsset/csvPath 확인).");
                }
            }
            return sim;
        }

        string LoadCsvText()
        {
            if (csvAsset != null) return csvAsset.text;
            if (!string.IsNullOrEmpty(csvPath))
            {
                try { if (File.Exists(csvPath)) return File.ReadAllText(csvPath); }
                catch (System.Exception e) { Debug.LogWarning($"[PlcBridge] CSV 읽기 실패: {e.Message}"); }
            }
            return null;
        }

        // 축 이동은 물리틱(FixedUpdate)에서, PLC 로직은 고정 scanPeriod 그리드(ScanStep)에서만 돈다(H4).
        void FixedUpdate()
        {
            if (source == null || crane == null) return;

            // 무버 기하에서 정규화 range를 1회 산출(빌더가 무버 셋업한 뒤 첫 틱).
            if (!rangesResolved) ResolveRangesFromGeometry();

            if (source is VirtualPlcSource v) v.InjectAggressive = injectAggressive;

            // 직접조종이면 PlcDriven=false 유지(가속 오경보 차단).
            crane.OpMode.PlcDriven = active;

            // 고정 스캔 그리드 — dt를 누산해 scanPeriod 경계에서만 1스캔(입력 동결→평가→출력).
            float scanDt = Mathf.Max(0.001f, scanPeriodMs * 0.001f);
            scanAccum += Time.fixedDeltaTime;
            int guard = 0;
            while (scanAccum >= scanDt && guard < MaxScansPerTick)
            {
                scanAccum -= scanDt;
                guard++;
                ScanStep(scanDt);
            }
            if (guard >= MaxScansPerTick) scanAccum = 0f;   // catch-up 폭주분은 버려 실시간성 유지.

            if (active && logTelemetry && haveSnap)
            {
                telemetryT += Time.fixedDeltaTime;
                if (telemetryT >= 1f)
                {
                    telemetryT = 0f;
                    var s = lastSnap;
                    float axGt = crane.Gantry  != null ? crane.Gantry.Current  : 0f;
                    float axTr = crane.Trolley != null ? crane.Trolley.Current : 0f;
                    float axHo = crane.Spreader != null ? crane.Spreader.Current : 0f;
                    Debug.Log($"[PlcBridge] {source.Name} scan={scanPeriodMs:F0}ms | GT={s.GtPosition:F1}m TR={s.TrPosition:F1}m HO={s.HoPosition:F1}m " +
                              $"load={s.HoLoad:F0}t ctrl={s.ControlMode} run={s.OpRunning} alm={s.AlarmCode} " +
                              $"| 축(model) GT={axGt:F3} TR={axTr:F3} HO={axHo:F3}");
                }
            }
        }

        // 한 스캔 = 실 PLC 1스캔: 소스 고정 dt 전진 → 입력 동결(Process Image) → 출력(축) 기록.
        void ScanStep(float scanDt)
        {
            source.Pump(scanDt);                  // 가상/CSV 소스를 고정 증분으로 전진(결정성).
            // H5: CSV 되감기 직후엔 위치가 불연속 — 가속 추적을 재프라임해 인공 스파이크 알람을 막는다.
            if (source is CsvReplaySource csv && csv.ConsumeDiscontinuity()
                || source is ServerPlcSource srv && srv.ConsumeDiscontinuity()) crane.OpMode.ResetAccelTracking();
            if (!active) return;
            if (!source.TryRead(out var s)) return;
            lastSnap = s; haveSnap = true;        // 이 스캔의 입력 스냅샷을 동결.
            DriveAxis(crane.Gantry,   s.GtPosition, gtRangeM);
            DriveAxis(crane.Trolley,  s.TrPosition, trRangeM);
            DriveAxis(crane.Spreader, s.HoPosition, hoRangeM);
        }

        // 정규화 분모(실척 range) 자동 산출: rangeM = (Max−Min)×WorldPerUnit÷ModelScale(SSOT=무버).
        //   0이 아닌 값은 수동 오버라이드로 보존. VirtualPlcSource에도 주입해 한 출처로 묶는다.
        void ResolveRangesFromGeometry()
        {
            if (crane == null) return;
            float inv = crane.ModelScale > 0f ? 1f / crane.ModelScale : 0f;
            if (inv <= 0f) return;   // ModelScale 비정상이면 산출 스킵(기존값 유지).

            // 무버가 아직 null이면(빌더 미완) 다음 틱에 재시도.
            if (crane.Gantry == null || crane.Trolley == null || crane.Spreader == null) return;

            if (gtRangeM <= 0f) gtRangeM = AxisSpanM(crane.Gantry,   inv);
            if (trRangeM <= 0f) trRangeM = AxisSpanM(crane.Trolley,  inv);
            if (hoRangeM <= 0f) hoRangeM = AxisSpanM(crane.Spreader, inv);

            // 가상 소스에도 같은 range 주입. HoLowM(안착 2m)은 절대 실척값이라 그대로.
            sim.GtRangeM = gtRangeM;
            sim.TrRangeM = trRangeM;
            sim.HoRangeM = hoRangeM;
            sim.ResyncRangeDependent();   // _ho 시작/웨이포인트가 HoRangeM(최상단) 기준이므로 재동기화.

            rangesResolved = true;
            if (logTelemetry)
                Debug.Log($"[PlcBridge] range 자동산출(모델기하×{inv:F0}): " +
                          $"GT={gtRangeM:F2}m TR={trRangeM:F2}m HO={hoRangeM:F2}m (ModelScale={crane.ModelScale:F4})");
        }

        /// <summary>스냅샷 자세에서 크레인에 붙은 월드 점 p(예: 트위스트락 중심)가 갈 자리 — DriveAxis와 같은 정규화,
        /// 세 축 평행이동 합(회전 없음이라 정확).</summary>
        public Vector3 WorldAtPose(in PlcSnapshot s, Vector3 p) =>
            p + Shift(crane.Gantry, s.GtPosition, gtRangeM)
              + Shift(crane.Trolley, s.TrPosition, trRangeM)
              + Shift(crane.Spreader, s.HoPosition, hoRangeM);

        static Vector3 Shift(IAxisMover a, float realPos, float rangeM) =>
            a == null || rangeM <= 0f ? Vector3.zero
            : a.WorldAxis * (Mathf.Lerp(a.Min, a.Max, Mathf.Clamp01(realPos / rangeM)) - a.Current);

        static float AxisSpanM(IAxisMover axis, float invScale)
        {
            if (axis == null) return 0f;
            float span = axis.Max - axis.Min;        // 모델 가동범위(span, 축 단위)
            return span > 0f ? span * axis.WorldPerUnit * invScale : 0f; // 실척 range
        }

        // 실척 위치(0..rangeM) → 정규화 → 축 모델 좌표(Min..Max). 방향 규약은 벤더 확인 대상(질의서).
        static void DriveAxis(IAxisMover axis, float realPos, float rangeM)
        {
            if (axis == null || rangeM <= 0f) return;
            axis.MoveToNormalized(realPos / rangeM);
        }
    }
}
