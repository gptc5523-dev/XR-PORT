#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
가상 PLC 운영 데이터 생성기 (PLCSIM stopgap)
============================================
㈜엠비이 PLCSIM Advanced / 시뮬레이션 데이터(운영시나리오 부록C, 도착 대기) 전까지,
동일 형식의 가상 데이터를 생성한다. 근거 문서:
  - 태그/주소/단위 : MBE-DOC-2026-XR-002 (PLC 데이터 포인트 리스트, DB100/DB101)
  - 시나리오 시퀀스 : MBE-DOC-2026-XR-004 (운영 시나리오 12선) + S13/S14 (연속 적하·양하)
  - 알람 코드/심각도 : MBE-DOC-2026-XR-003 (알람 코드북)
  - 푸시 주기 100ms : MBE-DOC-2026-XR-001/005

출력(벤더 부록C 형식):
  output/<Sxx>/run_NN.csv          : DB100 운영 태그 시계열(100ms)
  output/<Sxx>/run_NN.events.json  : DB101 알람/이벤트 로그
  output/manifest.json             : 전체 런 목록 + AI 라벨 분포(정상/주의/이상)

※ 가설 데이터 — 실 PLCSIM 도착 시 이 생성기는 폐기/검증대조용으로만 남긴다.
   속도/가속 기본값은 통상 STS 보수값(벤더 확정·튜닝 대상).
"""
import csv, json, os, math, argparse, random

DT = 0.1  # 100ms 폴링 (사양서 §3.2)

# ── 실척 가동 범위 · 정격 속도(m/s)/가속(m/s²) ──
# ★ SSOT 는 Unity 크레인 기하다. PlcBridge 는 무버 Min/Max 에서 rangeM 을 자동 산출해
#   realPos / rangeM 으로 정규화한다 → 여기 값이 실제 가동범위와 다르면 그 비율만큼
#   위치가 통째로 어긋나고, 초과분은 클램프돼 축이 끝에 붙어버린다.
#   유도 (StsCraneCreator.cs, Scale = 1/24):
#     tr = (TrolleyMaxX − TrolleyMinX) × 24 = (63/24 − (−13/24 − 0.12)) × 24 = 78.9 m
#     ho = (SpreaderMaxY − SpreaderMinY) × 24 = (44 − 4.8)               = 39.2 m
#   gt 는 씬의 부두 레일에서 런타임 산출(GantryRangeFit)이라 정적으로 못 박는다.
#     기본값 = 설계 상수 GantryRange(±2.2 모델) × 2 × 24 = 105.6 m.
#     ponytail: Play 로그 "[PlcBridge] range 자동산출 ... GT=__m" 의 실측값을
#               --gt-range 로 주면 정확해진다. 시나리오는 range 비율로 쓰므로 클램프는 안 난다.
RANGE = {"gt": 105.6, "tr": 78.9, "ho": 39.2}
# 정격 — SSOT = CraneAxisProfile.cs (VirtualPlcSource 와 같은 출처를 쓴다)
VMAX  = {"gt": 0.7,  "tr": 3.5, "ho": 1.25}   # CraneAxisProfile.*MaxSpeed
AMAX  = {"gt": 0.15, "tr": 0.6, "ho": 0.50}   # CraneAxisProfile.*RatedAccel
# 비상제동 배수 — E-Stop 은 정격을 훨씬 넘겨 세운다. 트립 임계(정격×1.667)를 넘는 건
#   의도된 것: E-Stop 에서는 가속알람이 떠야 맞다. 다만 '순간 0'은 아니다(무한 감속).
EMG_BRAKE = 3.0

# ── 알람 코드북 발췌(코드→심각도/소스/한글) — 시나리오가 쓰는 것만 ──
ALM = {
    3012: (3, 3, "HO 과부하 (정격 초과)"),
    3013: (2, 3, "HO Snag 발생(걸림 감지)"),
    3001: (2, 3, "HO 모터 과전류 발생"),
    3002: (2, 3, "HO 모터 권선 온도 한계 초과"),
    3005: (3, 3, "HO 인버터 통신 단절"),
    4003: (2, 4, "SP 4 코너 중 부분 잠금"),
    4006: (2, 4, "SP 안착 신호 미검출"),
    6001: (1, 6, "풍속 주의 (≥18 m/s)"),
    6002: (3, 6, "풍속 운전중지 (≥25 m/s)"),
    5001: (3, 5, "운전실 비상정지 버튼 작동"),
    5005: (2, 5, "LOTO 잠금 상태 활성"),
    5016: (0, 5, "시스템 정상 기동 완료"),
    5017: (0, 5, "시스템 정상 셧다운"),
    5015: (0, 5, "운전 모드 변경"),
    5023: (2, 5, "외부 서버(통합서버) 통신 단절"),
}

# OP_Mode 코드(사양서): 0=Manual 1=Semi 2=Auto 3=Maintenance
MANUAL, SEMI, AUTO, MAINT = 0, 1, 2, 3
# SP_Mode: 0=20 1=40 2=45 3=Twin
SP20, SP40, SP45, SPTWIN = 0, 1, 2, 3

CSV_FIELDS = [
    "t_ms",
    "GT_Position", "GT_Velocity", "GT_Direction", "GT_Running", "GT_Brake_Released", "GT_Motor_Alarm",
    "TR_Position", "TR_Velocity", "TR_Direction", "TR_Running", "TR_Brake_Released", "TR_Motor_Alarm",
    "HO_Position", "HO_Velocity", "HO_Direction", "HO_Load", "HO_Overload_Alarm", "HO_Snag_Alarm",
    "HO_Running", "HO_Brake_Released", "HO_Motor_Alarm",
    "SP_Mode", "SP_TwistLock_Locked", "SP_TwistLock_Unlocked", "SP_Landed", "SP_Container_Detected",
    "SP_Mismatch_Alarm", "SP_Telescopic_Position",
    "OP_Mode", "OP_Power_On", "OP_Ready", "OP_Running", "OP_Standby", "OP_Emergency_Stop",
    "OP_AntiSway_Active", "OP_Cycle_Count_Today",
    "ENV_Wind_Speed", "ENV_Wind_Direction", "ENV_Wind_Alarm",
    "ALM_Active", "ALM_Latest_Code", "ALM_Latest_Severity", "ALM_Latest_Source",
    "COM_Link_Status",
]


class Axis:
    """가속도 한계 트라페조이드 프로파일(점프 없음) — VirtualPlcSource.cs 와 같은 의도.

    ※ C# 과 한 곳이 다르다: C# 은 정착 시 Pos = Target 으로 '위치를' 스냅하지만 여기선
      위치를 절대 건드리지 않고 Target 을 현재 위치로 당긴다. CSV 는 100ms 격자라
      5cm 위치 텔레포트가 겉보기 가속 Δd/dt² = 5 m/s² 로 보이고, Unity 의 CraneOpMode 가
      위치를 2차 미분하므로 정상 운전에서 가속알람(1021/2021/3021)이 오발한다
      (패치 전 실측: 정상 시나리오 GT 5.10 · TR 5.60 · HO 10.30 m/s², 트립 0.25/1.00/0.833).
      속도만 죽이면 Δv ≤ amax·dt → 겉보기 가속 ≤ amax < 트립 이라 구조적으로 안전하다."""
    def __init__(self, pos, lim=None):
        self.pos = pos; self.vel = 0.0; self.target = pos; self.accel = 0.0
        self.lim = lim   # 실척 가동 상한(엔드스톱). None 이면 무제한.

    def step(self, dt, vmax, amax):
        d = self.target - self.pos
        dist = abs(d); vabs = abs(self.vel); v0 = self.vel
        stop = (vabs * vabs) / (2 * amax) if amax > 0 else 0.0
        if dist <= stop + 1e-4:
            a = (-1.0 if self.vel > 0 else (1.0 if self.vel < 0 else 0.0)) * amax
        else:
            a = (1.0 if d > 0 else -1.0) * amax
        self.vel += a * dt
        self.vel = max(-vmax, min(vmax, self.vel))
        # 감속 한계 — '남은 거리 안에서 반드시 멈출 수 있는 속도'로 제한한다.
        #   100ms 이산 틱이라 감속 시작 판정만으로는 목표를 v·dt 만큼 지나친다(TR 실측 0.50 m).
        #   지나치면 엔드스톱이 속도를 한 틱에 죽여 겉보기 가속 5~7 m/s² → 가속알람 오발.
        #   vcap 은 dist 에 대해 매끄러워서(Δv/틱 ≈ amax·dt) 이 제한 자체는 스파이크를 안 만든다.
        #   바닥(amax*dt) — vcap 은 dist→0 에서 기울기가 발산해 마지막 한 틱에 속도를
        #   정격의 2배로 깎는다(실측 TR 1.30 / 정격 0.6). 정격 한 틱치 속도를 바닥으로 깔면
        #   그 속도에서 아래 '도착 정착'(임계 amax*dt+1e-3)이 바로 걸려 Δv ≤ amax*dt,
        #   즉 겉보기 가속 ≤ amax < 트립이 된다.
        vcap = max(math.sqrt(2.0 * amax * dist), amax * dt) if dist > 0.0 else 0.0
        self.vel = max(-vcap, min(vcap, self.vel))
        # ※ 여기에 '틱당 속도변화 ≤ amax' 상한을 두면 안 된다 — vcap 을 무력화해서
        #   감속이 늦어지고 축이 엔드스톱을 때린다(실측: TR 이 0 을 지나쳐 −0.45 m/s 에서
        #   급정지 → 겉보기 가속 4.3). 급정지는 상한이 아니라 Sim.arrest() 로 분리한다.
        self.pos += self.vel * dt
        self.accel = a
        # 엔드스톱(리밋 스위치) — 트라페조이드 오버슈트가 가동범위를 넘지 않게 한다.
        #   넘긴 채로 내보내면 Unity 가 realPos/rangeM > 1 을 클램프해 축이 끝에 붙는다.
        if self.lim is not None and not (0.0 <= self.pos <= self.lim):
            self.pos = min(max(self.pos, 0.0), self.lim); self.vel = 0.0; self.accel = 0.0
        # 도착 정착 — 위치가 아니라 목표를 당긴다(위 클래스 주석: 위치 텔레포트 금지).
        #   vel 임계는 틱 양자화(amax*dt)보다 커야 한다(안 그러면 목표 근처서 진동만 하고 영영 안 멈춤).
        if abs(self.target - self.pos) < 0.05 and abs(self.vel) <= amax * dt + 1e-3:
            self.target = self.pos; self.vel = 0.0; self.accel = 0.0

    def settled(self):
        return abs(self.target - self.pos) < 0.05 and abs(self.vel) < 0.05


class Sim:
    def __init__(self, sp_mode=SP40, wind=8.0, rng=None):
        # 런별 편차의 단일 출처. 시드 고정 = 재현 가능(반복시험 요건).
        self.rng = rng or random.Random(0)
        self.t = 0.0
        self.gt = Axis(0.0, RANGE["gt"]); self.tr = Axis(0.0, RANGE["tr"]); self.ho = Axis(RANGE["ho"], RANGE["ho"])
        self.vmul = 1.0  # 풍속 감속 등 속도 스케일
        # 설비 편차 — 인버터 튜닝·로프 마모·운전자 습관으로 런마다 정격이 미세하게 다르다.
        self.vdev = {k: self.rng.gauss(1.0, 0.03) for k in ("gt", "tr", "ho")}
        self.adev = {k: self.rng.gauss(1.0, 0.04) for k in ("gt", "tr", "ho")}
        self.sp_mode = sp_mode
        self.carry = False; self.load = 0.0
        self.locked = False; self.landed = False; self.detected = False
        self.mismatch = False; self.tele_mm = 12192.0  # 40ft
        self.op_mode = AUTO; self.power = True; self.ready = True
        self.estop = False; self.locked_out = False; self.antisway = True
        self.cycle = 0
        self.wind = wind; self.wind_dir = 270.0; self.wind_alarm = False
        self.wind_inst = wind; self.wind_dir_inst = 270.0   # 계측 순시값(_step_env가 갱신)
        self.ho_overload = False; self.ho_snag = False; self.ho_motor_alarm = False
        self.comm = True
        # 현재 활성 알람(스티키) : code/sev/src
        self.alm_code = 0; self.alm_sev = 0; self.alm_src = 0
        self.rows = []; self.events = []

    # ── 이벤트/알람 ──
    def event(self, code, kind="alarm", clear=False):
        sev, src, desc = ALM[code]
        self.events.append({
            "t_ms": int(round(self.t * 1000)), "kind": kind, "code": code,
            "severity": sev, "source": src, "description": desc,
        })
        if kind == "alarm" and not clear:
            self.alm_code, self.alm_sev, self.alm_src = code, sev, src

    def clear_alarm(self):
        self.alm_code = self.alm_sev = self.alm_src = 0

    # ── 시간 진행 ──
    def _tick(self):
        self.gt.step(DT, VMAX["gt"] * self.vmul * self.vdev["gt"], AMAX["gt"] * self.adev["gt"])
        self.tr.step(DT, VMAX["tr"] * self.vmul * self.vdev["tr"], AMAX["tr"] * self.adev["tr"])
        self.ho.step(DT, VMAX["ho"] * self.vmul * self.vdev["ho"], AMAX["ho"] * self.adev["ho"])
        self.load = (32.0 if self.sp_mode != SPTWIN else 40.0) if self.carry else 0.0
        self._step_env()
        self.rows.append(self._row())
        self.t += DT

    # 풍속은 상수가 아니라 돌풍(랜덤워크+저주파) — 시나리오가 정한 self.wind 를 평균으로 흔든다.
    #   위치와 달리 미분되지 않는 채널이라 노이즈를 넣어도 가속알람(1021/2021/3021)에 영향이 없다.
    def _step_env(self):
        self.wind_inst = max(0.0, self.wind + self.rng.gauss(0.0, 0.45) + 0.6 * math.sin(self.t * 0.7))
        self.wind_dir_inst = (self.wind_dir + self.rng.gauss(0.0, 2.5)) % 360.0

    # 대기시간(트위스트락 잠금·안착 확인 등)도 런마다 다르다 — 사람·유압이 개입하는 구간.
    def run_for(self, secs, jitter=True):
        if jitter: secs *= self.rng.uniform(0.85, 1.30)
        for _ in range(max(1, int(round(secs / DT)))):
            self._tick()

    # 목표 위치에 런별 산포를 섞는다 — 실제 운전은 같은 베이도 매번 몇 cm 씩 다르게 선다.
    #   가동범위 밖으로 새지 않게 클램프한다(넘기면 Unity 축이 끝에 붙어 추종이 끊긴다).
    def goto(self, gt=None, tr=None, ho=None):
        if gt is not None: self.gt.target = self._aim(gt, "gt", 0.25)
        if tr is not None: self.tr.target = self._aim(tr, "tr", 0.20)
        if ho is not None: self.ho.target = self._aim(ho, "ho", 0.08)

    # 목표는 리밋에 붙이지 않는다 — 한 틱치 여유(vmax·dt)를 남긴다.
    #   붙이면 마지막 접근이 엔드스톱을 때려 속도가 한 틱에 죽고(실측 TR −0.14 → −0.01)
    #   겉보기 가속이 1.3 m/s² 로 튄다. 실제 크레인도 리밋 스위치 위에 주차하지 않는다.
    def _aim(self, v, axis, sigma):
        m = VMAX[axis] * DT
        return max(m, min(RANGE[axis] - m, v + self.rng.gauss(0.0, sigma)))

    # 급정지 — E-Stop·스내그·모터고장처럼 정격을 넘겨 세우는 구간.
    #   브레이크가 물리는 데 시간이 걸린다: '속도를 한 틱에 0' 으로 두면 미분 시 무한 감속이라
    #   실 데이터로 안 보이고, 반대로 정격 감속으로 세우면 급정지처럼 안 보인다.
    #   mul = 정격 대비 감속 배수. 트립 임계(정격×1.667)를 넘는 건 의도된 것 — 알람이 떠야 맞다.
    def arrest(self, mul=EMG_BRAKE, estop=False, safety_ticks=200):
        self.estop = estop
        for _ in range(safety_ticks):
            moving = False
            for ax, k in ((self.gt, "gt"), (self.tr, "tr"), (self.ho, "ho")):
                dv = AMAX[k] * mul * DT
                v0 = ax.vel
                ax.vel = 0.0 if abs(v0) <= dv else (v0 - dv if v0 > 0 else v0 + dv)
                ax.accel = (ax.vel - v0) / DT
                ax.pos += ax.vel * DT
                if ax.lim is not None: ax.pos = min(max(ax.pos, 0.0), ax.lim)
                ax.target = ax.pos          # 추종 중단 — 재가동까지 그 자리
                if abs(ax.vel) > 1e-3: moving = True
            self.load = (32.0 if self.sp_mode != SPTWIN else 40.0) if self.carry else 0.0
            self._step_env(); self.rows.append(self._row()); self.t += DT
            if not moving: return

    def run_until_settled(self, timeout=90.0):
        n = int(timeout / DT)
        for _ in range(n):
            self._tick()
            if self.gt.settled() and self.tr.settled() and self.ho.settled():
                return
    # ── 스냅샷 행 ──
    @staticmethod
    def _b(x): return 1 if x else 0

    # 로드셀 실측값 — 정지 하중에 계측 노이즈 + 권상 가감속 관성분(F=ma)을 얹는다.
    #   무부하는 0 고정(스프레더 자중은 태그상 tare 보정됨).
    def _load_cell(self):
        if self.load <= 0.0: return 0.0
        inertia = self.load * (self.ho.accel / 9.81) if abs(self.ho.vel) > 1e-3 else 0.0
        return max(0.0, self.load + inertia + self.rng.gauss(0.0, 0.12))

    def _row(self):
        moving = (abs(self.gt.vel) > 1e-3) or (abs(self.tr.vel) > 1e-3) or (abs(self.ho.vel) > 1e-3)
        running = moving and not self.estop
        return {
            "t_ms": int(round(self.t * 1000)),
            "GT_Position": round(self.gt.pos, 3), "GT_Velocity": round(self.gt.vel, 3),
            "GT_Direction": self._b(self.gt.vel > 0), "GT_Running": self._b(abs(self.gt.vel) > 1e-3 and not self.estop),
            "GT_Brake_Released": self._b(running), "GT_Motor_Alarm": 0,
            "TR_Position": round(self.tr.pos, 3), "TR_Velocity": round(self.tr.vel, 3),
            "TR_Direction": self._b(self.tr.vel > 0), "TR_Running": self._b(abs(self.tr.vel) > 1e-3 and not self.estop),
            "TR_Brake_Released": self._b(running), "TR_Motor_Alarm": 0,
            "HO_Position": round(self.ho.pos, 3), "HO_Velocity": round(self.ho.vel, 3),
            "HO_Direction": self._b(self.ho.vel > 0), "HO_Load": round(self._load_cell(), 2),
            "HO_Overload_Alarm": self._b(self.ho_overload), "HO_Snag_Alarm": self._b(self.ho_snag),
            "HO_Running": self._b(abs(self.ho.vel) > 1e-3 and not self.estop),
            "HO_Brake_Released": self._b(running), "HO_Motor_Alarm": self._b(self.ho_motor_alarm),
            "SP_Mode": self.sp_mode,
            "SP_TwistLock_Locked": self._b(self.locked), "SP_TwistLock_Unlocked": self._b(not self.locked),
            "SP_Landed": self._b(self.landed), "SP_Container_Detected": self._b(self.detected),
            "SP_Mismatch_Alarm": self._b(self.mismatch), "SP_Telescopic_Position": round(self.tele_mm, 1),
            "OP_Mode": self.op_mode, "OP_Power_On": self._b(self.power), "OP_Ready": self._b(self.ready),
            "OP_Running": self._b(running), "OP_Standby": self._b(not running and not self.estop),
            "OP_Emergency_Stop": self._b(self.estop), "OP_AntiSway_Active": self._b(self.antisway),
            "OP_Cycle_Count_Today": self.cycle,
            "ENV_Wind_Speed": round(self.wind_inst, 1), "ENV_Wind_Direction": round(self.wind_dir_inst, 1),
            "ENV_Wind_Alarm": self._b(self.wind_alarm),
            "ALM_Active": self._b(self.alm_code != 0), "ALM_Latest_Code": self.alm_code,
            "ALM_Latest_Severity": self.alm_sev, "ALM_Latest_Source": self.alm_src,
            "COM_Link_Status": self._b(self.comm),
        }


# ─────────────────── 씬 기하에서 유도한 작업 좌표 (실척 m) ───────────────────
# ★ 여기 숫자는 지어낸 값이 아니라 전부 Unity SSOT 에서 유도한 값이다.
#   StsConfig      : LegGaugeXMeters 18 · QuayDeckAboveSeaMeters 4 · ModelScale 1/24
#   StsCraneCreator: TrolleyMinX −15.9 · TrolleyMaxX 63 · LandLegX 0 · WaterLegX 18 · RailH 44
#   ShipConfig     : FreeboardMeters 11 · BeamMeters 39.53 · DeckRows 15 · ContainerWidthM 2.438
#   ShipBerthMenu  : 접안틈 = ApronSeawardM 4 + FenderClearance 1.5 = 5.5
#
# ── 트롤리: PLC 좌표 = 붐로컬 X + 15.9 (0 = 백리치 끝) ──
TR_LANDLEG   = 15.9                     # 육지쪽 다리      (X = 0)
TR_QUAY      = 33.9                     # 바다쪽 다리·안벽 (X = 18)
TR_SHIP_NEAR = 39.4                     # 배 현측          (X = 18 + 5.5 = 23.5)
TR_SHIP_FAR  = 78.9                     # 배 원측          (X = 23.5 + 39.53 = 63.03) — 아웃리치가 전폭을 덮는다
ROW_PITCH    = 2.478                    # 갑판 열간 피치 = 컨테이너폭 2.438 + 라싱 0.04
TR_CHASSIS   = 8.0                      # 육지 섀시(트럭) — 백리치 안(< TR_LANDLEG 15.9)
TR_SHIP_WORK = TR_SHIP_NEAR + 2 * ROW_PITCH   # 배 위 기본 작업 열(현측에서 3번째)

# ── 권상: PLC 좌표 = 스프레더 하단의 안벽 상면 기준 높이 − 0.8 ──
#   HO=0 은 SpreaderMinY = −(RailH − 0.8) 이라 안벽 상면 +0.8m 에 해당한다.
QUAY_TO_DECK = 11.0 - 4.0               # 주갑판 − 안벽 = 건현 − 안벽고 = 7.0
CONT_H       = 2.591                    # ISO 1AA 높이
HO_DECK_T1   = QUAY_TO_DECK + CONT_H - 0.8         #  8.79  갑판 1단 상면
HO_DECK_T2   = QUAY_TO_DECK + 2 * CONT_H - 0.8     # 11.38  갑판 2단 상면(오너 지시 갑판 최대 2단)
HO_CHASSIS   = 1.51 - 0.8                          #  0.71  섀시 데크 상면(40ft 샤시 1.51m)
HO_CLEAR     = HO_DECK_T2 + 3.0                    # 14.38  이송 클리어고

# ─────────────────────── 시나리오 ───────────────────────
# ※ 옛 값(HI = RANGE["ho"] = 39.2 최상단 / LO = 2.0 / SEA = range 끝)은 씬이 생기기 전
#   추상 좌표였다. 매 사이클 최상단까지 올리는 운전은 실물에 없다 — 클리어고까지만 올린다.
HI   = HO_CLEAR       # 이송 클리어고
LO   = HO_DECK_T2     # 배쪽 작업고(갑판 2단 상면)
LO_L = HO_CHASSIS     # 육지쪽 작업고(섀시 데크)
SEA  = TR_SHIP_WORK   # 트롤리 선박측
LAND = TR_CHASSIS     # 트롤리 육지측

def discharge_cycle(s, with_pick=True):
    """S02 양하 1사이클 (선박→안벽)."""
    s.op_mode = AUTO
    s.goto(tr=SEA, ho=HI); s.run_until_settled()           # 트롤리 선박측
    s.goto(ho=LO); s.run_until_settled()                    # 권상 하강(픽업)
    if with_pick:
        s.detected = True; s.landed = True; s.run_for(1.0)
        s.locked = True; s.carry = True; s.run_for(1.5)     # 트위스트락 잠금
        s.landed = False
    s.goto(ho=HI); s.run_until_settled()                    # 권상 상승(적재)
    s.goto(tr=LAND); s.run_until_settled()                  # 트롤리 육지측(섀시)
    s.goto(ho=LO_L); s.run_until_settled()                  # 권상 하강(섀시 안착)
    s.landed = True; s.run_for(1.0)
    s.locked = False; s.carry = False; s.run_for(1.5)       # 트위스트락 해제
    s.landed = False; s.detected = False
    s.goto(ho=HI); s.run_until_settled()                    # 권상 상승(공차)
    s.cycle += 1


def gen_S01(s):  # 기동 및 자체 진단 (정상)
    s.power = True; s.ready = False; s.op_mode = MANUAL
    s.run_for(3.0)
    s.event(5016, kind="event")  # 시스템 정상 기동
    s.ready = True; s.run_for(2.0)

def gen_S02(s):  # 양하 (정상)
    discharge_cycle(s)

def gen_S03(s):  # 선적 (정상) — 양하의 역순: 육지 섀시에서 집어 배 갑판에 놓는다
    s.op_mode = AUTO
    s.goto(tr=LAND, ho=LO_L); s.run_until_settled()
    s.detected = True; s.landed = True; s.run_for(1.0)
    s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
    s.goto(ho=HI); s.run_until_settled()
    s.goto(tr=SEA); s.run_until_settled()
    s.goto(ho=LO); s.run_until_settled()
    s.landed = True; s.run_for(1.0)
    s.locked = False; s.carry = False; s.run_for(1.5); s.landed = False; s.detected = False
    s.goto(ho=HI); s.run_until_settled(); s.cycle += 1

def gen_S04(s):  # 트윈 리프트 (정상)
    s.sp_mode = SPTWIN
    discharge_cycle(s)

def gen_S05(s):  # 셧다운 (정상)
    s.op_mode = AUTO
    s.goto(tr=LAND, ho=HI, gt=0.0); s.run_until_settled()
    s.op_mode = MANUAL; s.event(5015, kind="event"); s.run_for(1.0)
    s.event(5017, kind="event"); s.ready = False; s.power = False; s.run_for(2.0)

def gen_S06(s):  # 풍속 주의 (주의) — 18 m/s 접근, 감속
    s.goto(tr=SEA, ho=HI); s.run_until_settled()
    s.wind = 18.4; s.wind_alarm = True; s.event(6001); s.vmul = 0.7   # 70% 감속
    s.goto(ho=LO); s.run_until_settled()
    s.detected = True; s.landed = True; s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
    s.goto(ho=HI); s.run_until_settled()
    s.wind = 12.0; s.wind_alarm = False; s.clear_alarm(); s.vmul = 1.0
    s.goto(tr=LAND); s.run_until_settled(); s.cycle += 1

def gen_S07(s):  # 스내그 (주의) — 권상 중 걸림
    s.goto(tr=SEA, ho=HI); s.run_until_settled()
    s.goto(ho=LO); s.run_until_settled()
    s.detected = True; s.landed = True; s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
    s.goto(ho=HI / 2)  # 권상 상승 시작
    s.run_for(3.0)
    s.ho_snag = True; s.event(3013); s.arrest(2.0); s.run_for(2.0)         # 스내그 → 권상 보호정지
    s.goto(ho=s.ho.pos - 1.0); s.run_until_settled()                       # 약간 하강(걸림 해제)
    s.ho_snag = False; s.clear_alarm(); s.run_for(1.0)
    s.goto(ho=HI); s.run_until_settled(); s.cycle += 1

def gen_S08(s):  # 안착 실패/재시도 (주의)
    s.goto(tr=SEA, ho=LO); s.run_until_settled()   # 배 갑판 위 안착 시도
    s.detected = True; s.landed = True; s.run_for(5.0)        # 5초 내 미잠금
    s.mismatch = True; s.event(4003); s.run_for(1.0)
    s.goto(ho=LO + 0.4); s.run_until_settled()                # 5~10cm 들어 재정렬
    s.mismatch = False; s.clear_alarm()
    s.goto(ho=LO); s.run_until_settled()
    s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
    s.goto(ho=HI); s.run_until_settled(); s.cycle += 1

def gen_S09(s):  # 비상정지 (이상)
    s.goto(tr=SEA, ho=LO); s.run_until_settled()
    s.detected = True; s.locked = True; s.carry = True
    s.goto(ho=HI); s.run_for(2.0)
    s.event(5001); s.arrest(estop=True)                        # E-Stop — 비상제동으로 감속 정지
    s.run_for(5.0)
    s.estop = False; s.clear_alarm(); s.ready = True; s.run_for(1.0)

def gen_S10(s):  # 풍속 한계 초과 (이상) — 작업 강제 중단
    s.goto(tr=SEA, ho=HI); s.run_until_settled()
    s.wind = 26.0; s.wind_alarm = True; s.event(6002)          # ≥25 m/s
    s.goto(tr=LAND, ho=HI); s.run_until_settled()              # 스프레더 안전 회수
    s.op_mode = MAINT; s.event(5015, kind="event"); s.run_for(3.0)

def gen_S11(s):  # 설비 고장 (이상) — 권상 모터 과열
    s.goto(tr=SEA, ho=LO); s.run_until_settled()
    s.detected = True; s.locked = True; s.carry = True
    s.goto(ho=HI); s.run_for(2.5)
    s.ho_motor_alarm = True; s.event(3002)                     # 권선 온도 초과 → 권상 정지
    s.arrest(2.0); s.run_for(4.0)
    s.ho_motor_alarm = False; s.clear_alarm(); s.run_for(1.0)

def gen_S12(s):  # 정비 모드 (정비 — 라벨 제외)
    s.op_mode = MAINT; s.event(5015, kind="event")
    s.locked_out = True; s.event(5005); s.run_for(8.0)         # LOTO
    s.locked_out = False; s.clear_alarm(); s.op_mode = MANUAL; s.run_for(2.0)


BAY_PITCH = 12.192 + 0.6   # 선박 베이 피치 = 40ft 12.192 + 라싱 간격


def gen_S13(s):  # 20개 적하 (육지 섀시 → 배 갑판), 20ft/40ft 혼합
    s.op_mode = AUTO
    GT0 = RANGE["gt"] * 0.35           # 첫 작업 베이 — 절대 m 이 아니라 가동범위 비율(클램프 방지)
    # 크레인이 이미 작업 베이에 위치 — 0에서 장거리 주행/타임아웃 카스케이드 제거.
    s.gt.pos = GT0; s.gt.target = GT0
    n = 0
    for bay in range(2):                       # 베이 2개
        s.goto(gt=GT0 + bay * BAY_PITCH)
        for tier in (1, 2):                    # 적하는 아래 단부터 쌓는다
            ho_place = HO_DECK_T1 if tier == 1 else HO_DECK_T2
            for row in range(5):               # 현측에서 5열
                if n >= 20: break
                s.sp_mode = SP40 if n % 2 else SP20
                # ── 픽업(육지 섀시) ──
                s.goto(tr=LAND, ho=HI); s.run_until_settled()
                s.goto(ho=LO_L); s.run_until_settled()
                s.detected = True; s.landed = True; s.run_for(1.0)
                s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
                s.goto(ho=HI); s.run_until_settled()
                # ── 적치(배 갑판) — 열은 트롤리, 단은 권상 ──
                s.goto(tr=TR_SHIP_NEAR + row * ROW_PITCH); s.run_until_settled()
                s.goto(ho=ho_place); s.run_until_settled()
                s.landed = True; s.run_for(1.0)
                s.locked = False; s.carry = False; s.run_for(1.5)
                s.landed = False; s.detected = False
                s.goto(ho=HI); s.run_until_settled()
                s.cycle += 1; n += 1


def gen_S14(s):  # 20개 양하 (배 갑판 → 육지 섀시), 20ft/40ft 혼합
    """오너 요청 2026-09-09 "배에서 컨테이너를 내리는 PLC".

    S02 는 양하 1사이클, S13 은 20개 적하(육지→배)라 '연속 양하'가 비어 있었다.
    실물 양하 순서를 그대로 따른다 — 한 베이 안에서 <b>위 단부터</b> 열을 훑고, 다 비우면
    갠트리로 다음 베이. 단을 아래부터 내리면 위 컨테이너가 무너지므로 순서가 뒤집히면 안 된다.
      갠트리 = 베이(선박 길이방향) · 트롤리 = 열(선폭방향) · 권상 = 단
    """
    s.op_mode = AUTO
    GT0 = RANGE["gt"] * 0.35
    s.gt.pos = GT0; s.gt.target = GT0
    n = 0
    for bay in range(2):
        s.goto(gt=GT0 + bay * BAY_PITCH)
        for tier in (2, 1):                    # ★ 양하는 위 단부터
            ho_pick = HO_DECK_T2 if tier == 2 else HO_DECK_T1
            for row in range(5):
                if n >= 20: break
                s.sp_mode = SP40 if n % 2 else SP20
                # ── 픽업(배 갑판) ──
                s.goto(tr=TR_SHIP_NEAR + row * ROW_PITCH, ho=HI); s.run_until_settled()
                s.goto(ho=ho_pick); s.run_until_settled()
                s.detected = True; s.landed = True; s.run_for(1.0)
                s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
                s.goto(ho=HI); s.run_until_settled()
                # ── 안착(육지 섀시) ──
                s.goto(tr=LAND); s.run_until_settled()
                s.goto(ho=LO_L); s.run_until_settled()
                s.landed = True; s.run_for(1.0)
                s.locked = False; s.carry = False; s.run_for(1.5)
                s.landed = False; s.detected = False
                s.goto(ho=HI); s.run_until_settled()
                s.cycle += 1; n += 1


SCENARIOS = [
    ("S01", "시스템 기동 및 자체 진단", "정상", gen_S01),
    ("S02", "컨테이너 양하", "정상", gen_S02),
    ("S03", "컨테이너 선적", "정상", gen_S03),
    ("S04", "트윈 리프트", "정상", gen_S04),
    ("S05", "작업 종료 및 셧다운", "정상", gen_S05),
    ("S06", "풍속 한계 접근(감속)", "주의", gen_S06),
    ("S07", "스내그 알람", "주의", gen_S07),
    ("S08", "안착 실패/재시도", "주의", gen_S08),
    ("S09", "비상정지", "이상", gen_S09),
    ("S10", "풍속 한계 초과(중단)", "이상", gen_S10),
    ("S11", "설비 고장", "이상", gen_S11),
    ("S12", "정비 모드", "정비(제외)", gen_S12),
    ("S13", "20개 적하 (육지 섀시→배 갑판)", "정상", gen_S13),
    ("S14", "20개 양하 (배 갑판→육지 섀시)", "정상", gen_S14),
]

# 벤더 부록C 회차 — 정상 각10/주의 각5/이상 각5/정비 3 = 83. 기본은 샘플(축소), --full로 전체.
FULL_RUNS = {"정상": 10, "주의": 5, "이상": 5, "정비(제외)": 3}
SAMPLE_RUNS = {"정상": 2, "주의": 2, "이상": 2, "정비(제외)": 1}


def write_run(out_dir, sid, name, label, idx, sim):
    os.makedirs(out_dir, exist_ok=True)
    csv_path = os.path.join(out_dir, f"run_{idx:02d}.csv")
    with open(csv_path, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        w.writeheader(); w.writerows(sim.rows)
    ev_path = os.path.join(out_dir, f"run_{idx:02d}.events.json")
    with open(ev_path, "w", encoding="utf-8") as f:
        json.dump({"scenario": sid, "name": name, "label": label,
                   "duration_s": round(len(sim.rows) * DT, 1),
                   "rows": len(sim.rows), "events": sim.events},
                  f, ensure_ascii=False, indent=2)
    return csv_path, ev_path, len(sim.rows)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--full", action="store_true", help="벤더 부록C 전체 회차(83런) 생성")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "output"))
    ap.add_argument("--gt-range", type=float, default=None,
                    help="갠트리 실척 주행범위(m). Play 로그 '[PlcBridge] range 자동산출 ... GT=' 실측값.")
    args = ap.parse_args()
    if args.gt_range: RANGE["gt"] = args.gt_range
    runs_per = FULL_RUNS if args.full else SAMPLE_RUNS

    manifest = {"generated_by": "PlcSim/generate.py (stopgap)", "dt_ms": int(DT * 1000),
                "mode": "full" if args.full else "sample",
                "range_m": dict(RANGE), "vmax_ms": dict(VMAX), "amax_ms2": dict(AMAX),
                "runs": []}
    total_rows = 0; label_count = {}
    for sid, name, label, fn in SCENARIOS:
        n = runs_per[label]
        for i in range(1, n + 1):
            # 런마다 다른 시드 — 같은 시나리오 N회가 서로 다른 데이터가 된다(반복시험 요건).
            #   시드는 (시나리오번호, 회차)로 결정 → 재생성해도 같은 값(재현성). hash()는
            #   프로세스마다 달라지므로(PYTHONHASHSEED) 쓰지 않는다.
            seed = int(sid[1:]) * 1000 + i
            sim = Sim(sp_mode=SPTWIN if sid == "S04" else SP40, rng=random.Random(seed))
            fn(sim)
            out_dir = os.path.join(args.out, sid)
            csv_path, ev_path, rows = write_run(out_dir, sid, name, label, i, sim)
            total_rows += rows
            label_count[label] = label_count.get(label, 0) + 1
            manifest["runs"].append({
                "scenario": sid, "name": name, "label": label, "run": i, "seed": seed,
                "rows": rows, "duration_s": round(rows * DT, 1),
                "events": len(sim.events),
                "csv": os.path.relpath(csv_path, args.out),
                "events_json": os.path.relpath(ev_path, args.out),
            })
    manifest["label_distribution"] = label_count
    manifest["total_runs"] = len(manifest["runs"])
    manifest["total_rows"] = total_rows
    with open(os.path.join(args.out, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)

    print(f"[PlcSim] mode={manifest['mode']} runs={manifest['total_runs']} rows={total_rows} "
          f"({total_rows*DT/60:.1f}분 분량) → {args.out}")
    print(f"[PlcSim] 라벨 분포: {label_count}")


if __name__ == "__main__":
    main()
