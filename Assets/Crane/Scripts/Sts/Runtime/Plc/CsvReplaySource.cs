using System.Collections.Generic;
using System.Globalization;

namespace Container.Crane.Sts.Plc
{
    /// <summary>
    /// 기록 재생 PLC 소스 — PlcSim/generate.py가 만든 CSV(DB100 태그 시계열)를 <see cref="PlcSnapshot"/>로 재생한다.
    /// "운영시나리오 로그 재생"(첫 회의 옵션 b) 경로. 가상 데이터 파일이 실제로 크레인을 움직이게 하는 소스.
    ///
    /// 순수 C#(UnityEngine 비의존) — CSV 텍스트를 받아 파싱. 파일 로드는 PlcBridge가 담당(에디터/스트리밍에셋).
    /// 가속도(GtAccel 등)는 채우지 않는다 — CraneOpMode가 위치 미분으로 산출하므로 가속알람은 모션에서 자연 발생.
    /// </summary>
    public sealed class CsvReplaySource : IPlcSource
    {
        readonly PlcSnapshot[] _frames;
        readonly float[] _times;   // 초
        float _t;
        int _idx;
        bool _wrapped;             // 직전 Pump에서 되감기(끝→0)가 일어났는지(H5 마스킹용 1회성 플래그).

        /// <summary>끝에서 처음으로 되감아 반복할지(기본 true).</summary>
        public bool Loop = true;

        public string Name => "CsvReplay";
        public bool IsConnected => _frames != null && _frames.Length > 0;

        /// <summary>재생 위치(초). 되감기면 줄어든다.</summary>
        public float PlayheadS => _t;

        /// <summary>t 초 시점의 프레임(그 이하 마지막 행). 작업 이력 시각(t_ms)으로 그 순간의 PLC 자세를 찾는다.</summary>
        public PlcSnapshot FrameAt(float t)
        {
            int i = System.Array.BinarySearch(_times, t);
            if (i < 0) i = ~i - 1;
            return _frames[i < 0 ? 0 : (i >= _frames.Length ? _frames.Length - 1 : i)];
        }

        /// <summary>CSV 헤더에 없어 0으로 처리된, 파서가 기대한 컬럼명들. 침묵 실패(벤더 태그명 변경/헤더 오타 → 조용히 0) 가시화용 — PlcBridge가 이걸 보고 경고 로그한다. 헤더가 정상이면 비어 있음.</summary>
        public readonly List<string> MissingColumns = new List<string>();

        public CsvReplaySource(string csvText)
        {
            ParseCsv(csvText, out _frames, out _times, MissingColumns);
        }

        /// <summary>직전 Pump가 되감겼으면 true를 1회 반환(소비 후 리셋) — PlcBridge가 가속 추적 재프라임에 사용(H5).</summary>
        public bool ConsumeDiscontinuity()
        {
            bool w = _wrapped; _wrapped = false; return w;
        }

        public void Pump(float dt)
        {
            if (!IsConnected || dt <= 0f) return;
            _t += dt;
            float total = _times[_times.Length - 1];
            if (Loop && total > 0f && _t > total) { _t %= total; _idx = 0; _wrapped = true; }   // 되감기(불연속)
            while (_idx < _frames.Length - 1 && _times[_idx + 1] <= _t) _idx++;
        }

        // H5: 위치를 프레임 간 선형보간(FOH)으로 연속화 → ZOH 계단형 미분이 만들던 인공 가속 스파이크 제거.
        //     이산 필드(bool/enum/알람코드)는 floor 프레임 값을 유지.
        public bool TryRead(out PlcSnapshot snap)
        {
            if (!IsConnected) { snap = default; return false; }
            int i = _idx;
            if (i >= _frames.Length - 1) { snap = _frames[_frames.Length - 1]; return true; }
            float t0 = _times[i], t1 = _times[i + 1];
            float span = t1 - t0;
            float f = span > 1e-6f ? (_t - t0) / span : 0f;
            if (f < 0f) f = 0f; else if (f > 1f) f = 1f;
            snap = LerpFrame(_frames[i], _frames[i + 1], f);
            return true;
        }

        // 연속량(위치/속도/하중/가속ground-truth/풍속)만 선형보간. 나머지(상태비트·모드·알람)는 a(floor) 유지.
        internal static PlcSnapshot LerpFrame(PlcSnapshot a, PlcSnapshot b, float f)
        {
            var s = a;
            s.GtPosition = a.GtPosition + (b.GtPosition - a.GtPosition) * f;
            s.TrPosition = a.TrPosition + (b.TrPosition - a.TrPosition) * f;
            s.HoPosition = a.HoPosition + (b.HoPosition - a.HoPosition) * f;
            s.GtVelocity = a.GtVelocity + (b.GtVelocity - a.GtVelocity) * f;
            s.TrVelocity = a.TrVelocity + (b.TrVelocity - a.TrVelocity) * f;
            s.HoVelocity = a.HoVelocity + (b.HoVelocity - a.HoVelocity) * f;
            s.HoLoad     = a.HoLoad     + (b.HoLoad     - a.HoLoad)     * f;
            s.GtAccel    = a.GtAccel    + (b.GtAccel    - a.GtAccel)    * f;
            s.TrAccel    = a.TrAccel    + (b.TrAccel    - a.TrAccel)    * f;
            s.HoAccel    = a.HoAccel    + (b.HoAccel    - a.HoAccel)    * f;
            s.WindSpeed  = a.WindSpeed  + (b.WindSpeed  - a.WindSpeed)  * f;   // 풍향(0/360 wrap)은 보류 라인 — 단순 선형.
            return s;
        }

        // CSV 파싱 (헤더 이름 기반 — 컬럼 순서 변동에 견고). ServerPlcSource 도 이 파서를 쓴다 — 서버가 같은 헤더로 돌려준다.
        internal static void ParseCsv(string text, out PlcSnapshot[] frames, out float[] times, List<string> missingCols)
        {
            frames = new PlcSnapshot[0]; times = new float[0];
            if (string.IsNullOrEmpty(text)) return;

            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            if (lines.Length < 2) return;

            var header = lines[0].Split(',');
            var col = new Dictionary<string, int>(header.Length);
            for (int i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

            var missing = new HashSet<string>();   // 헤더에 없어 0으로 처리된 기대 컬럼(침묵 실패 가시화)

            var fr = new List<PlcSnapshot>(lines.Length);
            var tm = new List<float>(lines.Length);
            var inv = CultureInfo.InvariantCulture;

            for (int li = 1; li < lines.Length; li++)
            {
                var line = lines[li];
                if (string.IsNullOrEmpty(line)) continue;
                var c = line.Split(',');

                float F(string name) {
                    if (!col.TryGetValue(name, out int k)) { missing.Add(name); return 0f; }   // 헤더에 없음 → 0(기록)
                    return k < c.Length && float.TryParse(c[k], NumberStyles.Float, inv, out float v) ? v : 0f;
                }
                int I(string name) {
                    if (!col.TryGetValue(name, out int k)) { missing.Add(name); return 0; }
                    return k < c.Length && int.TryParse(c[k], out int v) ? v : 0;
                }
                bool B(string name) => I(name) != 0;

                var s = new PlcSnapshot
                {
                    GtPosition = F("GT_Position"), GtVelocity = F("GT_Velocity"),
                    GtRunning = B("GT_Running"), GtDirStern = B("GT_Direction"),
                    TrPosition = F("TR_Position"), TrVelocity = F("TR_Velocity"),
                    TrRunning = B("TR_Running"), TrDirSea = B("TR_Direction"),
                    HoPosition = F("HO_Position"), HoVelocity = F("HO_Velocity"),
                    HoRunning = B("HO_Running"), HoDirUp = B("HO_Direction"), HoLoad = F("HO_Load"),
                    SpMode = (PlcSpreaderMode)I("SP_Mode"),
                    TwistLockLocked = B("SP_TwistLock_Locked"), TwistLockUnlocked = B("SP_TwistLock_Unlocked"),
                    Landed = B("SP_Landed"), ContainerDetected = B("SP_Container_Detected"),
                    ControlMode = (PlcControlMode)I("OP_Mode"),
                    OpReady = B("OP_Ready"), OpRunning = B("OP_Running"), OpStandby = B("OP_Standby"),
                    EmergencyStop = B("OP_Emergency_Stop"),
                    WindSpeed = F("ENV_Wind_Speed"), WindDirection = F("ENV_Wind_Direction"),
                    WindAlarm = B("ENV_Wind_Alarm"),
                    AlarmActive = B("ALM_Active"), AlarmCode = I("ALM_Latest_Code"),
                    AlarmSeverity = I("ALM_Latest_Severity"), AlarmSource = I("ALM_Latest_Source"),
                    LinkStatus = B("COM_Link_Status"),
                };
                fr.Add(s);
                tm.Add(I("t_ms") / 1000f);
            }

            foreach (var m in missing) missingCols.Add(m);   // 침묵 실패 가시화 — PlcBridge가 경고

            frames = fr.ToArray();
            times = tm.ToArray();
        }
    }
}
