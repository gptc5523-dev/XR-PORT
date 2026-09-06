namespace Container.Crane.Sts.Plc
{
    /// <summary>
    /// 가상 PLC(stopgap) — ㈜엠비이 PLCSIM Advanced(사양서 §7.1, 도착 대기) 전까지 동일 계약(<see cref="PlcSnapshot"/>)을
    /// 충실히 생성한다. <b>순수 C#</b>(UnityEngine 비의존) → 헤드리스 테스트 가능. 실척 단위.
    ///
    /// 핵심 = <b>가감속 한계(정격 이내) 운전</b>. 직접조종(임시입력)은 출발/정지마다 0↔정격을 한 틱에 점프(≈37 m/s²)해
    /// 가속알람이 항상 오경보였다(문서/PLC.md §5.2). 가상 PLC는 인버터처럼 가속도를 정격으로 제한(트라페조이드 프로파일)해
    /// 정상 운전은 한계 이내, <see cref="InjectAggressive"/>로 급조작을 주입하면 1021/2021/3021이 자연발생한다.
    ///
    /// ※ 가설 구현 — Unity 미실행·Quest 미검증. 속도/가속 기본값은 통상 STS 보수값으로, 벤더 확정·튜닝 대상.
    /// </summary>
    public sealed class VirtualPlcSource : IPlcSource
    {
        // 실척 가동 범위 (m)
        // 하드코딩 금지(SSOT=무버 기하). PlcBridge가 무버 Max/Min에서 산출해 주입한다:
        //   RangeM = (Max − Min) × (1/ModelScale).  주입 후 ResyncRangeDependent()로 range 의존값 재동기화.
        // 헤드리스 단독 사용 시에는 호출측이 직접 set한 뒤 ResyncRangeDependent()를 부른다(미주입이면 0→축 정지).
        public float GtRangeM = 0f;
        public float TrRangeM = 0f;
        public float HoRangeM = 0f;

        // 정격 속도(m/s)·가속(m/s²) — SSOT: CraneAxisProfile
        //  H5(정격 속도·가감속 하드코딩) 대응: 값은 CraneAxisProfile 단일 출처를 참조(여기 박지 않음).
        //  트립 판정(CraneOpMode)도 같은 출처를 참조해 정격↔트립 관계가 한곳에서 관리된다.
        //  필드는 (테스트가 set할 수 있게) 유지하되 기본값만 프로파일에서 가져온다 → 동작 비트 동일.
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

        /// <summary>
        /// Range를 주입(set)한 뒤 호출 — range 의존 상태(권상 시작/목표=최상단 HoRangeM)를 새 값으로 재동기화.
        /// 구성 시점엔 Range가 0(미주입)이라 _ho가 0에서 시작했을 수 있으므로, 주입 직후 최상단으로 끌어올린다.
        /// 사이클이 이미 진행 중이면(권상이 최상단을 목표로 한 상태가 아니면) 위치를 강제로 옮기지 않는다.
        /// </summary>
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

                // 정착 스냅 — 미세 떨림 제거.
                //  스냅이 잔여속도를 1틱에 0으로 죽이면, 트립 측정(CraneOpMode.StepAccel)이
                //    위치를 2차 미분해 인공 가속 스파이크 a = Δv/dt 를 본다. 기존 임계 vNow<0.02 → 50Hz(dt=0.02s)에서
                //    a ≈ 0.02/0.02 = 1.0 m/s² → GT 트립 0.25·HO 트립 0.833을 초과(디바운스만으로 흡수 — 안전마진 취약).
                //
                //  해법(a, 임계 하향): 스냅 속도 임계를 '정격 감속 1틱이 어차피 없앨 속도' = maxAccel×dt 로 둔다.
                //    이러면 스냅 시 Δv ≤ maxAccel×dt → 측정 가속 a = Δv/dt ≤ maxAccel(= 정격 감속도)로,
                //    이 인공 스파이크가 '정상 감속 1틱'과 물리적으로 구별 불가능해진다(저크도 추가되지 않음).
                //  정량 검증(CraneAxisProfile 정격·트립, k=trip/rated=1.667):
                //    a_spike ≤ maxAccel = trip / 1.667 = trip × 0.6  →  trip×0.6 < clear(=trip×0.8) < trip.
                //    축별: GT 0.15<0.20<0.25 / TR 0.60<0.80<1.000 / HO 0.50<0.667<0.833 — 모두 해제임계 미만.
                //    ∴ 스냅 스파이크가 가장 낮은 GT 트립 0.25는 물론 각 축 clear 임계까지 밑돌아, 트립이
                //    디바운스에 의존하지 않고 '구조적으로' 안전(set/clear 카운터가 한 번도 안 오름).
                //  정착 거동 보존: 정격 감속 구간에서 |Vel|은 틱당 maxAccel×dt 씩 감소하므로, 잔여속도가 이 임계
                //    밑으로 떨어지는 시점은 곧 0으로 수렴하는 시점 → 스냅이 반드시 발동(dNow<0.01 위치창 동시 충족).
                //    최종 결과(Pos=Target, Vel=0, Accel=0)는 종전과 동일.
                float vSnap = maxAccel * dt;   // 정격 감속 1틱분 속도(축별·dt별 자기일관, 리터럴 추측 금지)
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
