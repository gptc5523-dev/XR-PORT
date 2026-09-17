using System.IO;
using UnityEngine;

namespace Container.Crane.Sts.Plc
{
    /// <summary>
    /// PLC ↔ 크레인 다리(문서/PLC.md §5.2) — <see cref="IPlcSource"/>의 스냅샷을 기존 교체 지점에 흘려보낸다.
    /// 소스는 셋 중 하나: <b>Virtual</b>(실시간 가상 생성) / <b>CsvReplay</b>(PlcSim 생성 CSV 재생) / <b>Server</b>(통합서버에서 읽기).
    /// ㈜엠비이 PLCSIM/실 PLC 도착 시 S7/OPC UA 어댑터를 한 갈래 더 추가하면 상위 코드(HUD·알람)는 그대로다.
    ///
    /// <para><b>상호배타:</b> Active=true면 PLC 소스가 축을 구동하고 PlcDriven=true(가속알람 1021/2021/3021 유효).
    /// Active=false면 직접조종(VR) 유지·PlcDriven=false(오경보 방지). 기본 비활성 — 부착해도 흐름 안 깨짐.</para>
    ///
    /// 가설 구현·Quest 미검증.
    /// </summary>
    // H4: PLC 출력(축 구동)은 가속 측정(CraneOpMode)보다 먼저 실행돼야 같은 입력이 같은 가속·알람을 낸다.
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Container/STS Crane/PLC Bridge (가상·실 PLC 시임)")]
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
        // 0이면 자동 산출 — crane 무버 기하에서 rangeM = (Max−Min)×(1/ModelScale)로 채운다(SSOT=무버 기하).
        // 인스펙터/프리팹에 0이 아닌 값이 있으면 그 값을 우선 사용(수동 오버라이드). 기본은 0=자동.
        [Tooltip("0이면 모델 기하에서 자동 산출(권장). 0이 아니면 그 값을 수동 사용.")]
        [SerializeField] float gtRangeM = 0f;
        [Tooltip("0이면 모델 기하에서 자동 산출(권장). 0이 아니면 그 값을 수동 사용.")]
        [SerializeField] float trRangeM = 0f;
        [Tooltip("0이면 모델 기하에서 자동 산출(권장). 0이 아니면 그 값을 수동 사용.")]
        [SerializeField] float hoRangeM = 0f;

        [Header("스캔 주기 (실 PLC 정합) — H4")]
        [Tooltip("PLC 로직 고정 스캔주기(ms). 실 PLC처럼 이 주기 경계에서만 입력 동결→평가→출력. " +
                 "Unity 물리틱(가변·catch-up 가능)과 분리해 동일 입력이 동일 알람을 내도록 결정성 확보. 통상 1~10ms.")]
        // 가시 클램프 — 인스펙터 슬라이더로 [1,100]ms 강제(아래 OnValidate가 직접입력·프리팹값도 재클램프).
        [Range(1f, 100f)]
        [SerializeField] float scanPeriodMs = 10f;

        [Header("진단")]
        [Tooltip("켜면 구동 중 초당 1회 콘솔에 PLC 읽기/축 값 로그(실시간 검증용). 평소엔 꺼둠.")]
        [SerializeField] bool logTelemetry = true;

        StsCrane crane;
        readonly VirtualPlcSource sim = new VirtualPlcSource();
        IPlcSource source;
        float telemetryT;
        bool rangesResolved;   // 무버 기하로 rangeM을 한 번 산출했는지(첫 틱에 빌더가 무버를 셋업한 뒤 1회).

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

        void Awake()
        {
            crane = GetComponent<StsCrane>();
#if UNITY_EDITOR
            // 도메인 리로드로 인스펙터 설정이 기본값(Virtual·비활성)으로 되돌아가도,
            // 마지막 메뉴 선택(EditorPrefs)에서 복원 — 씬 저장 없이도 Play마다 유지된다.
            if (UnityEditor.EditorPrefs.GetBool(PrefKey("forceReplay"), false))
            {
                string p = UnityEditor.EditorPrefs.GetString(PrefKey("csvPath"), "");
                if (!string.IsNullOrEmpty(p)) { sourceMode = SourceMode.CsvReplay; csvPath = p; csvAsset = null; active = true; }
            }
            // 재생 복원이 먼저다 — 재생 스모크·지표2 측정이 forceReplay 만 켜고 도는데, 서버 선택이 남아 있으면 그걸 가로챈다.
            else if (UnityEditor.EditorPrefs.GetBool(PrefKey("forceServer"), false)) { sourceMode = SourceMode.Server; active = true; }
#endif
            source = BuildSource();
            // CSV 재생이면 화물 재생도 붙인다 — 메뉴(PlcBridgeMenu)가 Undo.AddComponent 로 붙인 건 씬을 저장해야 남는데, 재생 설정은
            //   EditorPrefs 로 매 Play 복원돼서 '축은 재생되는데 스프레더는 빈손'이 됐다(2026-09-15 오너: 배 컨테이너를 안 잡음).
            //   작업 이력(run_NN.history.csv)이 없으면 PlcCargoReplay 가 스스로 꺼진다.
            if (active && source is CsvReplaySource && GetComponent<PlcCargoReplay>() == null) gameObject.AddComponent<PlcCargoReplay>();
            Debug.Log($"[PlcBridge] init: mode={sourceMode} active={active} csvAsset={(csvAsset != null)} " +
                      $"csvPath='{csvPath}' → source={source?.Name} connected={source?.IsConnected}");
        }

        // 스캔주기 가시 클램프 — 인스펙터 직접입력·프리팹 박힌 값까지 [1,100]ms로 재클램프.
        // 근거: 통상 1~10ms, 여유 상한 100ms=10Hz. 그 이상이면 입력 동결 간격이 벌어져
        //       가속/위치 변화가 스캔 경계 사이에 묻혀 알람 반영이 지연될 위험(과대값=알람 침묵).
        //       하한 1ms 미만(0·음수)은 0으로 나누는 catch-up 폭주/무한루프 위험.
        // [Range]는 슬라이더 가시 강제, OnValidate는 코드/프리팹 경로까지 이중 보장. 기본값 10f는 범위 안이라 불변.
        void OnValidate()
        {
            scanPeriodMs = Mathf.Clamp(scanPeriodMs, 1f, 100f);
        }

        // 컴포넌트가 떼이거나(Inspector 제거) 비활성화되면 PlcDriven을 끈다 — 브리지가 안 도는데
        //   stale true로 남으면 직접조종에 가속 오경보(1021/2021/3021)가 낀다.
        //   (과거 'Container/가상 PLC 분리' 메뉴가 하던 정리를 컴포넌트 자체에 내재화 — 이제 Inspector에서
        //    떼기만 해도 안전. crane은 Awake에서 캐시, 미설정이면 즉석 조회.)
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
        /// <summary>메뉴 선택 복원 키 — 크레인별. 전역 키 하나면 STS·RTG 브리지가 같은 CSV 를 복원해
        /// 서로의 데이터를 재생한다(가동범위가 달라 축이 끝에 붙는다).</summary>
        public string PrefKey(string k) => $"PlcBridge.{k}.{gameObject.name}";
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
                    if (rep.MissingColumns.Count > 0)   // 침묵 실패 방지 — 기대한 태그 컬럼이 CSV 헤더에 없으면 조용히 0이 되던 것을 경고로 노출
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

        // 축 이동·물리 정합은 물리틱(FixedUpdate)에서 — 메모리 물리 파이프라인 규약과 정합.
        // 단, PLC 로직 자체는 여기 dt가 아니라 고정 scanPeriod 그리드(ScanStep)에서만 돈다(H4).
        void FixedUpdate()
        {
            if (source == null || crane == null) return;

            // 무버 기하에서 정규화 range를 1회 산출(빌더가 무버를 셋업한 뒤 첫 가용 틱). SSOT=무버 Min/Max.
            if (!rangesResolved) ResolveRangesFromGeometry();

            if (source is VirtualPlcSource v) v.InjectAggressive = injectAggressive;

            // 직접조종이면 PlcDriven=false 유지(가속 오경보 차단). 켜져 있을 때만 PLC가 축을 잡는다.
            crane.OpMode.PlcDriven = active;

            // 고정 스캔 그리드 — 물리틱 dt를 누산해 scanPeriod 경계에서만 1스캔(입력 동결→평가→출력)을 실행.
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

        // 한 스캔주기 = 실 PLC 1스캔: 소스를 고정 dt만큼 전진 → 입력 1회 동결(Process Image) → 출력(축) 1회 기록.
        void ScanStep(float scanDt)
        {
            source.Pump(scanDt);                  // 가상/CSV 소스를 고정 증분으로 전진(결정성).
            // H5: CSV 되감기(끝→0) 직후엔 위치가 불연속이므로 가속 추적을 재프라임해 인공 스파이크 알람을 막는다.
            if (source is CsvReplaySource csv && csv.ConsumeDiscontinuity()
                || source is ServerPlcSource srv && srv.ConsumeDiscontinuity()) crane.OpMode.ResetAccelTracking();
            if (!active) return;
            if (!source.TryRead(out var s)) return;
            lastSnap = s; haveSnap = true;        // 이 스캔의 입력 스냅샷을 동결.
            DriveAxis(crane.Gantry,   s.GtPosition, gtRangeM);
            DriveAxis(crane.Trolley,  s.TrPosition, trRangeM);
            DriveAxis(crane.Spreader, s.HoPosition, hoRangeM);
        }

        // 정규화 분모(실척 range)를 모델 기하에서 자동 산출 — 하드코딩 제거(SSOT=무버 Min/Max).
        //   rangeM = (Max − Min) × WorldPerUnit ÷ ModelScale.  ModelScale=1/24 → ×24.
        //   WorldPerUnit — STS 1 · FBX RTG 트롤리 4.17(루트 스케일) · 월드수직 권상 1.
        // 빌더(StsCraneCreator)가 무버 min/max를 셋업한 뒤 첫 가용 틱에 1회 산출한다.
        // 인스펙터/프리팹에 0이 아닌 값이 박혀 있으면(수동 오버라이드) 그 값을 보존하고 산출은 스킵.
        // 산출한 실척 range는 VirtualPlcSource(가상 위치 생성)에도 주입해 두 정의를 한 출처로 묶는다.
        void ResolveRangesFromGeometry()
        {
            if (crane == null) return;
            float inv = crane.ModelScale > 0f ? 1f / crane.ModelScale : 0f;
            if (inv <= 0f) return;   // ModelScale 비정상이면 산출 스킵(기존값 유지).

            // 무버 중 하나라도 아직 null이면(빌더 미완) 이번 틱은 미루고 다음 틱에 재시도.
            if (crane.Gantry == null || crane.Trolley == null || crane.Spreader == null) return;

            // gtRangeM 등이 0이면 자동, 0이 아니면 수동값 보존.
            if (gtRangeM <= 0f) gtRangeM = AxisSpanM(crane.Gantry,   inv);
            if (trRangeM <= 0f) trRangeM = AxisSpanM(crane.Trolley,  inv);
            if (hoRangeM <= 0f) hoRangeM = AxisSpanM(crane.Spreader, inv);

            // 두 정의 SSOT화 — 가상 소스가 실척 위치를 만들 때 같은 range를 쓰도록 주입.
            // HoLowM(안착 높이)은 절대 실척값(2m)이라 range에 비례하지 않으므로 그대로 둔다(비율 불변).
            sim.GtRangeM = gtRangeM;
            sim.TrRangeM = trRangeM;
            sim.HoRangeM = hoRangeM;
            sim.ResyncRangeDependent();   // _ho 시작/웨이포인트가 HoRangeM(최상단) 기준이므로 재동기화.

            rangesResolved = true;
            if (logTelemetry)
                Debug.Log($"[PlcBridge] range 자동산출(모델기하×{inv:F0}): " +
                          $"GT={gtRangeM:F2}m TR={trRangeM:F2}m HO={hoRangeM:F2}m (ModelScale={crane.ModelScale:F4})");
        }

        /// <summary>스냅샷 자세에서 크레인에 붙어 움직이는 월드 점 p(예: 트위스트락 중심)가 어디 오나.
        /// DriveAxis 와 같은 정규화로 목표 축 값을 구하고 (목표 − 현재) × WorldAxis 만큼 평행이동한다.
        /// 세 축이 전부 평행이동이라(회전 없음) 합이 정확하다.</summary>
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
            return span > 0f ? span * axis.WorldPerUnit * invScale : 0f; // 실척 range = span × 월드/축 ÷ ModelScale
        }

        // 실척 위치(0..rangeM) → 정규화 → 축의 모델 좌표(Min..Max). 방향 규약(0=어느 끝)은 벤더 확인 대상(질의서).
        static void DriveAxis(IAxisMover axis, float realPos, float rangeM)
        {
            if (axis == null || rangeM <= 0f) return;
            axis.MoveToNormalized(realPos / rangeM);
        }
    }
}
