namespace Container.Crane.Sts.Plc
{
    /// <summary>PLC <b>제어모드</b> — ㈜엠비이 데이터포인트리스트 OP_Mode 태그(DB100.DBW130)의 값(수동/반자동/자동/정비).
    /// ※ 명칭 주의: 이건 '제어모드'다. 크레인 <b>운영상태</b>(운전/정지/이상)는 별개 타입
    ///   <see cref="Container.Crane.Sts.OpMode"/>(컴포넌트 CraneOpMode)이며 혼동 금지 — 과거 둘 다 'OpMode'라 꼬였음.
    ///   CSV/PLC 태그명은 벤더 사양이라 OP_Mode를 유지하고, C# 명칭만 ControlMode로 분리한다.</summary>
    public enum PlcControlMode { Manual = 0, Semi = 1, Auto = 2, Maintenance = 3 }

    /// <summary>스프레더 모드 — ㈜엠비이 SP_Mode(DB100.DBW100).</summary>
    public enum PlcSpreaderMode { TwentyFt = 0, FortyFt = 1, FortyFiveFt = 2, Twin = 3 }

    /// <summary>
    /// PLC 한 스냅샷 — ㈜엠비이 「PLC 데이터 포인트 리스트」(MBE-DOC-2026-XR-002) DB100(운영)·DB101(알람)의
    /// XR 대상 태그를 <b>실척 단위</b>로 담는다. 프로토콜 무관(S7/OPC UA/가상 동일).
    ///
    /// ※ 기존 문서(문서/PLC.md §5.1)의 placeholder PlcSnapshot(정규화 위치·opMode 0~2)을
    ///   벤더 확정 사양대로 격상한 것. 정규화는 PlcBridge가 축 주입 시점에 수행한다.
    /// </summary>
    public struct PlcSnapshot
    {
        // 축 위치/속도 (실척) — DB100 GT/TR/HO
        public float GtPosition, GtVelocity;   // m, m/s (속도 부호 = 방향)
        public float TrPosition, TrVelocity;
        public float HoPosition, HoVelocity;
        public bool  GtRunning, TrRunning, HoRunning;
        public bool  GtDirStern, TrDirSea, HoDirUp;   // 방향 비트(코드북 규약: GT 1=Stern, TR 1=Sea, HO 1=Up)
        public float HoLoad;                          // ton (4로프 합산, HO_Load DB100.DBD70)

        // 가속도 (실척 m/s²) — 가속알람 1021/2021/3021 검증용 그라운드트루스
        public float GtAccel, TrAccel, HoAccel;

        // 스프레더 — DB100 SP_*
        public PlcSpreaderMode SpMode;
        public bool TwistLockLocked, TwistLockUnlocked, Landed, ContainerDetected;

        // 제어모드 + 운영 상태비트 — DB100 OP_*
        public PlcControlMode ControlMode;   // 제어모드(수동/반자동/자동/정비). 크레인 '운영상태'(OpMode 운전/정지/이상)와 별개.
        public bool OpReady, OpRunning, OpStandby, EmergencyStop;

        // 환경 — DB100 ENV_*
        public float WindSpeed, WindDirection;   // m/s, deg
        public bool  WindAlarm;

        // 알람 — DB101 ALM_* (실 PLC 연동 시 직접 채움. 가상 stopgap에선 CraneFault가 SSOT)
        public bool AlarmActive;
        public int  AlarmCode;        // 코드북 4자리
        public int  AlarmSeverity;    // 0=Info,1=Warning,2=Critical,3=Fatal
        public int  AlarmSource;      // 1=GT 2=TR 3=HO 4=SP 5=SYS 6=ENV

        // 통신 — COM_Link_Status(DB100.DBX200.0)
        public bool LinkStatus;
    }

    /// <summary>
    /// "PLC에서 한 스냅샷을 읽어온다"는 프로토콜 무관 계약(문서/PLC.md §5.1).
    /// 구현: <see cref="VirtualPlcSource"/>(가상 stopgap, 지금) → S7PlcSource/OpcUaPlcSource(실어댑터, Phase B).
    /// </summary>
    public interface IPlcSource
    {
        string Name { get; }
        bool IsConnected { get; }

        /// <summary>시뮬형은 dt만큼 내부 진행, 실어댑터(백그라운드 스레드)는 큐 최신값 반영(또는 no-op).</summary>
        void Pump(float dt);

        /// <summary>최신 스냅샷. 아직 수신 전이면 false.</summary>
        bool TryRead(out PlcSnapshot snap);
    }
}
