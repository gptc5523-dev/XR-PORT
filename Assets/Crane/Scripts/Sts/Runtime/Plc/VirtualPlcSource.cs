namespace AIXRCrane.Crane.Sts.Plc
{
    /// <summary>가상 PLC(실 PLC 도착 전 스텁) — 순수 C#(UnityEngine 비의존)이라 헤드리스 테스트 가능, 실척 단위·<see cref="PlcSnapshot"/> 계약 동일.
    /// 가감속을 정격 이내로 제한(트라페조이드)해 정상 운전은 오경보 없음, <see cref="InjectAggressive"/>로 급조작을 주입하면 알람이 자연발생.</summary>
    public sealed class VirtualPlcSource : IPlcSource
    {
        // 실척 가동 범위(m) — 하드코딩 금지(SSOT=무버 기하). PlcBridge가 RangeM = (Max−Min)×(1/ModelScale) 로 주입.
        // 헤드리스 단독 사용 시 set 후 ResyncRangeDependent()를 직접 호출(미호출이면 0→축 정지).
        public float GtRangeM = 0f;
        public float TrRangeM = 0f;
        public float HoRangeM = 0f;

        // 정격 속도(m/s)·가속(m/s²) — SSOT: CraneAxisProfile(트립 판정 CraneOpMode도 동일 출처 참조).
        // 필드는 테스트가 override 할 수 있게 유지, 기본값만 프로파일에서 가져온다.
        public float GtMaxSpeed = CraneAxisProfile.GantryMaxSpeed,  GtRatedAccel = CraneAxisProfile.GantryRatedAccel;
        public float TrMaxSpeed = CraneAxisProfile.TrolleyMaxSpeed, TrRatedAccel = CraneAxisProfile.TrolleyRatedAccel;
        public float HoMaxSpeed = CraneAxisProfile.HoistMaxSpeed,   HoRatedAccel = CraneAxisProfile.HoistRatedAccel;

        public float CarryLoadTon = CraneAxisProfile.CarryLoadTon;   // 양하 표준 하중(S02). 과부하(3012)는 CraneFault가 판정.
        public float HoLowM = CraneAxisProfile.HoLowM;                // 안착 하강 높이(절대 실척, range 비례 아님)

        /// <summary>급조작 주입 — true면 가속 한계를 배수로 풀어 정격 초과 가속 → 가속알람 자연발생(데모/검증).</summary>
        public bool InjectAggressive = false;
        public float AggressiveAccelMul = CraneAxisProfile.AggressiveAccelMul;

        AxisSim _gt, _tr, _ho;
        int _wp;            // 현재 웨이포인트(S02 양하 사이클 근사)
        bool _carrying;     // 컨테이너 파지 중
        bool _has;
        PlcSnapshot _snap;

        public string Name => "VirtualPlc(stopgap)";
        public bool IsConnected => true;

        public VirtualPlcSource()
        {
            _gt = new AxisSim { Pos = 0f,        Target = 0f };
            _tr = new AxisSim { Pos = 0f,        Target = 0f };
            _ho = new AxisSim { Pos = HoRangeM,  Target = HoRangeM };   // 권상 최상단에서 시작
            _wp = 0;
        }

        /// <summary>Range 주입 후 호출 — range 의존 상태(권상 목표=최상단 HoRangeM)를 재동기화한다.
        /// 사이클이 이미 진행 중이면(권상이 최상단 대기가 아니면) 위치를 강제로 옮기지 않는다.</summary>
        public void ResyncRangeDependent()
        {
            // 아직 한 번도 Pump되지 않았거나 권상이 최상단 대기 상태면 새 최상단으로 정렬.
            if (!_has || (!_carrying && _wp == 0))
            {
                _ho.Pos = HoRangeM;
                _ho.Target = HoRangeM;
                _ho.Vel = 0f;
                _ho.Accel = 0f;
            }
        }

        public void Pump(float dt)
        {
            if (dt <= 0f) return;
            float mul = InjectAggressive ? AggressiveAccelMul : 1f;
            _gt.Step(dt, GtMaxSpeed, GtRatedAccel * mul);
            _tr.Step(dt, TrMaxSpeed, TrRatedAccel * mul);
            _ho.Step(dt, HoMaxSpeed, HoRatedAccel * mul);

            if (_gt.Settled && _tr.Settled && _ho.Settled) NextWaypoint();

            Build();
            _has = true;
        }

        public bool TryRead(out PlcSnapshot snap)
        {
            snap = _snap;
            return _has;
        }

        // S02(양하: 선박→안벽) 근사 — 7개 웨이포인트 순환. 갠트리는 고정(붐다운 정박 기준).
        void NextWaypoint()
        {
            _wp = (_wp + 1) % 7;
            switch (_wp)
            {
                case 0: _tr.Target = 0f;        _ho.Target = HoRangeM; break;  // 홈(안벽측·최상단)
                case 1: _tr.Target = TrRangeM;  _ho.Target = HoRangeM; break;  // 트롤리 선박측
                case 2: _tr.Target = TrRangeM;  _ho.Target = HoLowM;   break;  // 권상 하강(픽업)
                case 3: _carrying = true;       _ho.Target = HoRangeM; break;  // 잠금+권상 상승(적재)
                case 4: _tr.Target = 0f;        _ho.Target = HoRangeM; break;  // 트롤리 안벽측
                case 5: _tr.Target = 0f;        _ho.Target = HoLowM;   break;  // 권상 하강(안착)
                case 6: _carrying = false;      _ho.Target = HoRangeM; break;  // 해제+권상 상승(공차)
            }
        }

        void Build()
        {
            _snap.GtPosition = _gt.Pos; _snap.GtVelocity = _gt.Vel; _snap.GtAccel = Abs(_gt.Accel);
            _snap.TrPosition = _tr.Pos; _snap.TrVelocity = _tr.Vel; _snap.TrAccel = Abs(_tr.Accel);
            _snap.HoPosition = _ho.Pos; _snap.HoVelocity = _ho.Vel; _snap.HoAccel = Abs(_ho.Accel);

            _snap.GtRunning = Moving(_gt.Vel); _snap.TrRunning = Moving(_tr.Vel); _snap.HoRunning = Moving(_ho.Vel);
            _snap.GtDirStern = _gt.Vel > 0f; _snap.TrDirSea = _tr.Vel > 0f; _snap.HoDirUp = _ho.Vel > 0f;
            _snap.HoLoad = _carrying ? CarryLoadTon : 0f;

            bool low = _ho.Pos < HoLowM + 0.5f;
            _snap.SpMode = PlcSpreaderMode.FortyFt;
            _snap.TwistLockLocked = _carrying;
            _snap.TwistLockUnlocked = !_carrying;
            _snap.Landed = _carrying && low;
            _snap.ContainerDetected = _carrying;

            bool anyMove = _snap.GtRunning || _snap.TrRunning || _snap.HoRunning;
            _snap.ControlMode = PlcControlMode.Auto;
            _snap.OpRunning = anyMove;
            _snap.OpStandby = !anyMove;
            _snap.OpReady = true;
            _snap.EmergencyStop = false;

            _snap.WindSpeed = 8.0f; _snap.WindDirection = 270f; _snap.WindAlarm = false;   // 정적 placeholder(무풍 근사)

            // 알람은 Unity측 CraneFault(SSOT)가 판정 — stopgap에선 비움. 실 PLC 연동 시 DB101 ALM_*를 직접 채운다.
            _snap.AlarmActive = false; _snap.AlarmCode = 0; _snap.AlarmSeverity = 0; _snap.AlarmSource = 0;
            _snap.LinkStatus = true;
        }

        static float Abs(float v) => v < 0f ? -v : v;
        static bool Moving(float v) => Abs(v) > 1e-3f;

        /// <summary>단일 축 — 가속도 한계 트라페조이드 프로파일(가속/정속/감속). 가속 크기 ≤ maxAccel(점프 없음).</summary>
        struct AxisSim
        {
            public float Pos, Vel, Target, Accel;

            public void Step(float dt, float maxSpeed, float maxAccel)
            {
                if (dt <= 0f || maxAccel <= 0f) return;
                float d = Target - Pos;
                float dist = d < 0f ? -d : d;
                float vAbs = Vel < 0f ? -Vel : Vel;
                float stopDist = vAbs * vAbs / (2f * maxAccel);   // 현재 속도로 멈추는 데 필요한 거리

                float a;
                if (dist <= stopDist + 1e-4f)
                    a = (Vel > 0f ? -1f : Vel < 0f ? 1f : 0f) * maxAccel;   // 감속 구간
                else
                    a = (d > 0f ? 1f : -1f) * maxAccel;                      // 가속 구간

                Vel += a * dt;
                if (Vel > maxSpeed) Vel = maxSpeed;
                else if (Vel < -maxSpeed) Vel = -maxSpeed;
                Pos += Vel * dt;
                Accel = a;

                // 정착 스냅 — 잔여속도를 0으로 죽여 미세 떨림을 없앤다. 스냅이 만드는 인공 가속 스파이크가
                // 트립 측정(CraneOpMode.StepAccel)을 오경보하지 않도록, 임계 vSnap = maxAccel×dt(정격 감속
                // 1틱이 없앨 속도)로 잡아 스파이크를 정상 감속과 구별 불가능하게 한다 — 임계를 올리면 오경보 위험.
                float vSnap = maxAccel * dt;   // 임의 리터럴로 바꾸지 말 것(축·dt별 자기일관)
                float vNow = Vel < 0f ? -Vel : Vel;
                float dNow = (Target - Pos) < 0f ? -(Target - Pos) : (Target - Pos);
                if (dNow < 0.01f && vNow < vSnap) { Pos = Target; Vel = 0f; Accel = 0f; }
            }

            public bool Settled
            {
                get
                {
                    float d = Target - Pos; if (d < 0f) d = -d;
                    float v = Vel < 0f ? -Vel : Vel;
                    return d < 0.02f && v < 0.02f;
                }
            }
        }
    }
}
