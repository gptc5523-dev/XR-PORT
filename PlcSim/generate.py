#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
가상 PLC 운영 데이터 생성기 (PLCSIM stopgap)
============================================
㈜엠비이 PLCSIM Advanced / 시뮬레이션 데이터(운영시나리오 부록C, 도착 대기) 전까지,
동일 형식의 가상 데이터를 생성한다. 근거 문서:
  - 태그/주소/단위 : MBE-DOC-2026-XR-002 (PLC 데이터 포인트 리스트, DB100/DB101)
  - 시나리오 시퀀스 : MBE-DOC-2026-XR-004 (운영 시나리오 12선) + S13/S14 (연속 적하·양하)
                     + S15 (STS 5개 양하) · S16 (RTG 5개 야드 정리)
  - 알람 코드/심각도 : MBE-DOC-2026-XR-003 (알람 코드북)
  - 푸시 주기 100ms : MBE-DOC-2026-XR-001/005

출력(벤더 부록C 형식):
  output/<Sxx>/run_NN.csv          : DB100 운영 태그 시계열(100ms)
  output/<Sxx>/run_NN.events.json  : DB101 알람/이벤트 로그
  output/<Sxx>/run_NN.history.csv  : 작업 이력 — 컨테이너 1개 = 1줄(번호·출발·도착·집기/놓기 시각). 옮긴 게 있는 런만
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
#   RANGE 는 아래 '씬 기하' 절에서 유도한다(STS_RANGE — gt 는 GantryRangeFit 식).
# ── 비활성 2026-09-14 (gt 를 설계 상수 GantryRange ±2.2u 로 박았는데 씬 런타임은 236.98m — 베이가 통째로 어긋나 허공 작업) ──
# RANGE = {"gt": 105.6, "tr": 78.9, "ho": 39.2}
# 정격 — SSOT = CraneAxisProfile.cs (VirtualPlcSource 와 같은 출처를 쓴다)
VMAX  = {"gt": 0.7,  "tr": 3.5, "ho": 1.25}   # CraneAxisProfile.*MaxSpeed
AMAX  = {"gt": 0.15, "tr": 0.6, "ho": 0.50}   # CraneAxisProfile.*RatedAccel
# 비상제동 배수 — E-Stop 은 정격을 훨씬 넘겨 세운다. 트립 임계(정격×1.667)를 넘는 건
#   의도된 것: E-Stop 에서는 가속알람이 떠야 맞다. 다만 '순간 0'은 아니다(무한 감속).
EMG_BRAKE = 3.0
# 목표 산포(σ, m) — 수동 운전의 런별 편차. 자동 위치결정 시나리오는 AUTO_SIGMA 로 줄인다.
AIM_SIGMA = {"gt": 0.25, "tr": 0.20, "ho": 0.08}

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


def iso6346(owner, serial):
    """ISO 6346 컨테이너 번호 = 소유자코드 4자 + 일련번호 6자리 + 검사숫자.
    문자값은 A=10 부터 11의 배수(11·22·33)를 건너뛴다. 소유자코드 XRPU 는 가상(XR PORT)."""
    code = f"{owner}{serial:06d}"
    letters = [v for v in range(10, 39) if v % 11]
    s = sum((int(c) if c.isdigit() else letters[ord(c) - 65]) << i for i, c in enumerate(code))
    return code + str(s % 11 % 10)


class Sim:
    def __init__(self, sp_mode=SP40, wind=8.0, rng=None, range_m=None, id_base=0, gt0=0.0, tr0=0.0):
        # 런별 편차의 단일 출처. 시드 고정 = 재현 가능(반복시험 요건).
        self.rng = rng or random.Random(0)
        self.t = 0.0
        # 크레인마다 가동범위가 다르다(STS RANGE / RTG RTG_RANGE). 정격 속도·가속은 같은 CraneAxisProfile.
        self.range = range_m or RANGE
        self.gt = Axis(gt0, self.range["gt"]); self.tr = Axis(tr0, self.range["tr"])
        self.ho = Axis(self.range["ho"], self.range["ho"])
        self.sigma = dict(AIM_SIGMA)   # 목표 산포 — 자동 위치결정 시나리오가 줄인다
        self.moves = []; self.id_base = id_base   # 작업 이력 · 컨테이너 번호 = id_base + 순번
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

    # ── 작업 이력 — 잠금·해제 직전에 부른다. 시각이 CSV 에서 잠금/해제가 처음 찍힌 행의 t_ms 와 같다 ──
    def picked(self, frm):
        self.moves.append({"container": iso6346("XRPU", self.id_base + len(self.moves) + 1),
                           "size_ft": 20 if self.sp_mode == SP20 else 40,
                           "from": frm, "pick_t_ms": int(round(self.t * 1000))})

    def placed(self, to):
        self.moves[-1].update({"to": to, "place_t_ms": int(round(self.t * 1000))})

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
        if gt is not None: self.gt.target = self._aim(gt, "gt")
        if tr is not None: self.tr.target = self._aim(tr, "tr")
        if ho is not None: self.ho.target = self._aim(ho, "ho")

    # 목표는 리밋에 붙이지 않는다 — 한 틱치 여유(vmax·dt)를 남긴다.
    #   붙이면 마지막 접근이 엔드스톱을 때려 속도가 한 틱에 죽고(실측 TR −0.14 → −0.01)
    #   겉보기 가속이 1.3 m/s² 로 튄다. 실제 크레인도 리밋 스위치 위에 주차하지 않는다.
    def _aim(self, v, axis):
        m = VMAX[axis] * DT
        return max(m, min(self.range[axis] - m, v + self.rng.gauss(0.0, self.sigma[axis])))

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



# ── 비활성 2026-09-14 (씬과 불일치 — 갠트리 105.6m 가정·선측 모서리를 열로·해치커버 뺀 갑판고 → 허공 작업) ──
# # ─────────────────── 씬 기하에서 유도한 작업 좌표 (실척 m) ───────────────────
# # ★ 여기 숫자는 지어낸 값이 아니라 전부 Unity SSOT 에서 유도한 값이다.
# #   StsConfig      : LegGaugeXMeters 18 · QuayDeckAboveSeaMeters 4 · ModelScale 1/24
# #   StsCraneCreator: TrolleyMinX −15.9 · TrolleyMaxX 63 · LandLegX 0 · WaterLegX 18 · RailH 44
# #   ShipConfig     : FreeboardMeters 11 · BeamMeters 39.53 · DeckRows 15 · ContainerWidthM 2.438
# #   ShipBerthMenu  : 접안틈 = ApronSeawardM 4 + FenderClearance 1.5 = 5.5
# #
# # ── 트롤리: PLC 좌표 = 붐로컬 X + 15.9 (0 = 백리치 끝) ──
# TR_LANDLEG   = 15.9                     # 육지쪽 다리      (X = 0)
# TR_QUAY      = 33.9                     # 바다쪽 다리·안벽 (X = 18)
# TR_SHIP_NEAR = 39.4                     # 배 현측          (X = 18 + 5.5 = 23.5)
# TR_SHIP_FAR  = 78.9                     # 배 원측          (X = 23.5 + 39.53 = 63.03) — 아웃리치가 전폭을 덮는다
# ROW_PITCH    = 2.478                    # 갑판 열간 피치 = 컨테이너폭 2.438 + 라싱 0.04
# TR_CHASSIS   = 8.0                      # 육지 섀시(트럭) — 백리치 안(< TR_LANDLEG 15.9)
# TR_SHIP_WORK = TR_SHIP_NEAR + 2 * ROW_PITCH   # 배 위 기본 작업 열(현측에서 3번째)
#
# # ── 권상: PLC 좌표 = 스프레더 하단의 안벽 상면 기준 높이 − 0.8 ──
# #   HO=0 은 SpreaderMinY = −(RailH − 0.8) 이라 안벽 상면 +0.8m 에 해당한다.
# QUAY_TO_DECK = 11.0 - 4.0               # 주갑판 − 안벽 = 건현 − 안벽고 = 7.0
# CONT_H       = 2.591                    # ISO 1AA 높이
# HO_DECK_T1   = QUAY_TO_DECK + CONT_H - 0.8         #  8.79  갑판 1단 상면
# HO_DECK_T2   = QUAY_TO_DECK + 2 * CONT_H - 0.8     # 11.38  갑판 2단 상면(오너 지시 갑판 최대 2단)
# HO_CHASSIS   = 1.51 - 0.8                          #  0.71  섀시 데크 상면(40ft 샤시 1.51m)
# HO_CLEAR     = HO_DECK_T2 + 3.0                    # 14.38  이송 클리어고
#
# # ─────────────────────── 시나리오 ───────────────────────
# # ※ 옛 값(HI = RANGE["ho"] = 39.2 최상단 / LO = 2.0 / SEA = range 끝)은 씬이 생기기 전
# #   추상 좌표였다. 매 사이클 최상단까지 올리는 운전은 실물에 없다 — 클리어고까지만 올린다.
# HI   = HO_CLEAR       # 이송 클리어고
# LO   = HO_DECK_T2     # 배쪽 작업고(갑판 2단 상면)
# LO_L = HO_CHASSIS     # 육지쪽 작업고(섀시 데크)
# SEA  = TR_SHIP_WORK   # 트롤리 선박측
# LAND = TR_CHASSIS     # 트롤리 육지측

# ═══════════════════ 씬 기하 — 전부 Unity SSOT 수식에서 유도 (실척 m, 데크 윗면 y=0) ═══════════════════
# 오너 지시 2026-09-14 "허공에 작업하고 있는데 수식을 사용해서 작업해 · 시연이라 더 빡세게".
#   ★ 종전 좌표가 허공이던 이유
#     ① 갠트리 범위를 설계 상수 105.6m 로 가정 — 씬 런타임은 236.98m(GantryRangeFit, Play 로그).
#        GT 는 range 비율로 정규화되므로 베이가 통째로 밀려 컨테이너가 없는 곳에 내려갔다.
#     ② 트롤리 '열' 을 배 현측 모서리에서 셌다 — 실제 열 중심은 사이드데크·해치 폭 식으로 정해진다.
#     ③ 권상 '갑판' 에 해치 코밍 1.8m·커버 0.35m 가 빠졌다 — 1단 윗면은 11.74m(종전 9.59m).
#   아래는 C# 원본 식을 그대로 옮긴 것이다. verify.py ⑦ 이 Port.unity 를 파싱해 좌표·점유를 전부 대조한다.

CONT_W, CONT_H, CONT_L = 2.438, 2.591, 12.192          # ISO 1AA — ProceduralContainerMesh

# ── 부두 (PortConfig · QuayPartsPlacer.PlaceRail/PlaceLane) ──
APRON_SEAWARD, LEG_GAUGE = 4.0, 18.0                    # PortConfig.ApronSeawardM · StsConfig.LegGaugeXMeters
RAIL_WATER_X = -APRON_SEAWARD                           # 바다측 레일 −4
RAIL_LAND_X  = RAIL_WATER_X - LEG_GAUGE                 # 육지측 레일 −22
BERTH_LEN, LANE_PITCH = 340.0, 12.0                     # PortConfig.BerthLengthMeters · QuayPartsPlacer.LanePitchM
LANE_HALF_Z = math.floor(BERTH_LEN / LANE_PITCH) * LANE_PITCH / 2   # 노란 차선 28유닛 = ±168 — 갠트리 한계선

# ── STS (StsCraneCreator · StsConfig · GantryRangeFit) — 루트 = 육지측 레일 위 ──
STS_ROOT_X = RAIL_LAND_X
STS_ROOT_Z = -40.0                  # 씬 배치값(STS_Crane z −1.6667u) — 배치 결정이라 식이 없다. verify ⑦ 대조
BOOM_Y     = 44.0                   # RailH — 붐(트롤리 레일) 높이
TROLLEY_MIN_X, TROLLEY_MAX_X = -13.0 - 0.12 * 24, 63.0  # TrolleyMinX = −13·Scale − BoomBackExtra(0.12u) = −15.88
TROLLEY_REST_X = 8.0                # TrolleyRestX = 8·Scale — 트롤리·SpreaderRoot 휴지 위치(붐 로컬). verify ⑥ 대조
HOIST_X     = 0.027 * 24            # HoistX — 스프레더(트위스트락 중심)가 SpreaderRoot 보다 바다쪽 0.648
ATTACH_DROP = 0.019 * 24            # AttachPoint y −0.019u — 스프레더 원점 → 본체 밑면(= 컨테이너 윗면) 0.456
SPREADER_MIN_Y, SPREADER_MAX_Y = -(BOOM_Y - 0.8), -4.0  # 붐 로컬 — 행정 39.2
LEG_FOOT = 1.0 * 1.7                # 격자 다리 footprint = LegSec × 1.7
LEG_Z    = 16.0 / 2                 # 앞뒤 다리 Z ±8 = GantryBaseZMeters / 2
WHEEL_HALF_Z = 9.5114               # 크레인 중심 → 바깥 바퀴(Wheel 렌더러 실측) — GantryRangeFit 이 재는 값. verify ⑦ 대조

GT_HALF  = min(LANE_HALF_Z - STS_ROOT_Z, STS_ROOT_Z + LANE_HALF_Z) - WHEEL_HALF_Z   # 128 − 9.51 = 118.49
GT_MIN_Z = STS_ROOT_Z - GT_HALF                                                     # GT 0 인 루트 Z = −158.49
STS_RANGE = {"gt": 2 * GT_HALF,
             "tr": TROLLEY_MAX_X - TROLLEY_MIN_X,
             "ho": SPREADER_MAX_Y - SPREADER_MIN_Y}
RANGE = STS_RANGE

# PLC 좌표(0..range) ↔ 월드. PlcBridge.DriveAxis 는 Lerp(Min, Max, real/range) 라 range 가 약분된다.
def sts_gt(z):   return z - GT_MIN_Z                                    # 트위스트락 중심 Z = 루트 Z
def sts_tr(x):   return x - STS_ROOT_X - HOIST_X - TROLLEY_MIN_X        # 트위스트락 중심 X
def sts_ho(top): return top + ATTACH_DROP - BOOM_Y - SPREADER_MIN_Y     # 컨테이너 윗면 = AttachPoint 높이

# ── 컨테이너선 (ShipConfig · ProceduralShipHull · ProceduralShipStructures.CargoSlots · ShipBerthMenu · ShipCreator) ──
SHIP_LOA, SHIP_ROWS_MAX, SHIP_ROW_GAP, SHIP_SIDE_DECK = 294.0, 15, 0.04, 1.2
SHIP_BEAM = SHIP_ROWS_MAX * CONT_W + (SHIP_ROWS_MAX - 1) * SHIP_ROW_GAP + 2 * SHIP_SIDE_DECK   # 39.53
SHIP_FREEBOARD = 24.0 - 13.0                            # Depth − Draft
CARGO_AFT_Z, CARGO_FWD_Z = -82.0, 112.0
COAM_H, COVER_H = 1.8, 0.35                             # 해치 코밍 · 커버 — 컨테이너는 커버 위에 앉는다
SHEER_BOW, SHEER_STERN, STEM_MIN_HALF = 2.2, 0.8, 0.25
DECK_MAX_TIERS, SECOND_TIER_RATIO, STACK_SEED = 2, 0.5, 20260907
FENDER = 1.5                                            # ShipBerthMenu.FenderClearanceM
SHIP_X = RAIL_WATER_X + (APRON_SEAWARD + FENDER) + SHIP_BEAM / 2   # 21.265 — 배 중심선
SHIP_Y = -4.0                                           # 흘수선 = 수면 = 데크 − QuayDeckAboveSeaMeters
SHIP_Z = 0.0                                            # 선석 중앙 — 씬 배치값, verify ⑦ 대조


def ship_half_beam(z):                                  # ProceduralShipHull.HalfBeam
    t = z / (SHIP_LOA / 2); a = abs(t)
    if a <= 0.30: f = 1.0
    else:
        p = (a - 0.30) / 0.70
        f = 1 - p ** 2.2 if t >= 0 else 1 - 0.42 * p ** 1.6
    b = SHIP_BEAM / 2 * f
    if t > 0 and a > 0.85: b = max(b, STEM_MIN_HALF)
    return max(b, 0.02)


def ship_deck_y(z):                                     # ProceduralShipHull.DeckY — 건현 + 시어
    t = z / (SHIP_LOA / 2); a = abs(t)
    if a <= 0.50: return SHIP_FREEBOARD
    q = (a - 0.50) / 0.50
    return SHIP_FREEBOARD + (SHEER_BOW if t >= 0 else SHEER_STERN) * q * q


def _cargo_slots():                                     # ProceduralShipStructures.CargoSlots — 배 로컬
    cargo_len = CARGO_FWD_Z - CARGO_AFT_Z
    bays = max(1, round(cargo_len / (CONT_L + 2)))
    pitch, coam_len = cargo_len / bays, CONT_L + 0.5
    out = []
    for i in range(bays):
        zc = CARGO_AFT_Z + pitch * (i + 0.5)
        hb = min(ship_half_beam(zc - coam_len / 2), ship_half_beam(zc + coam_len / 2))
        rows = min(max(math.floor(2 * (hb - SHIP_SIDE_DECK) / CONT_W), 1), SHIP_ROWS_MAX)
        base = ship_deck_y(zc) + COAM_H + COVER_H
        v = i / (bays - 1) if bays > 1 else 0.0
        tiers = DECK_MAX_TIERS - min(DECK_MAX_TIERS - 1, math.floor(v * DECK_MAX_TIERS))   # 선미 2단 → 선수 1단 램프
        for r in range(rows):
            for t in range(tiers):
                out.append({"bay": i, "row": r, "tier": t + 1,
                            "x": -rows * CONT_W / 2 + (r + 0.5) * CONT_W, "y": base + (t + 0.5) * CONT_H, "z": zc})
    return out


class NetRandom:
    """System.Random(int seed) — .NET/Mono 레거시 감산 생성기. ShipCreator 의 2단 랜덤을 그대로 재현한다."""
    MBIG, MSEED = 2147483647, 161803398

    def __init__(self, seed):
        sa = [0] * 56
        mj = self.MSEED - (self.MBIG if seed == -2147483648 else abs(seed)); sa[55] = mj; mk = 1
        for i in range(1, 55):
            ii = (21 * i) % 55; sa[ii] = mk; mk = mj - mk
            if mk < 0: mk += self.MBIG
            mj = sa[ii]
        for _ in range(4):
            for i in range(1, 56):
                sa[i] -= sa[1 + (i + 30) % 55]
                if sa[i] < 0: sa[i] += self.MBIG
        self.sa, self.inext, self.inextp = sa, 0, 21

    def next_double(self):
        self.inext = 1 if self.inext + 1 >= 56 else self.inext + 1
        self.inextp = 1 if self.inextp + 1 >= 56 else self.inextp + 1
        r = self.sa[self.inext] - self.sa[self.inextp]
        if r == self.MBIG: r -= 1
        if r < 0: r += self.MBIG
        self.sa[self.inext] = r
        return r * (1.0 / self.MBIG)


def _occupied(slots):                                   # ShipCreator.LoadShipCargo — 열(x,z) 단위, 윗단은 확률
    cols, order = {}, []
    for sl in slots:
        k = (round(sl["x"] * 1000), round(sl["z"] * 1000))
        if k not in cols: cols[k] = []; order.append(k)
        cols[k].append(sl)
    rng, out = NetRandom(STACK_SEED), set()
    for k in order:
        for t, sl in enumerate(sorted(cols[k], key=lambda v: v["y"])):
            if t > 0 and rng.next_double() > SECOND_TIER_RATIO: break
            out.add((sl["bay"], sl["row"], sl["tier"]))
    return out


SHIP_SLOTS = _cargo_slots()
SHIP_SLOT  = {(sl["bay"], sl["row"], sl["tier"]): sl for sl in SHIP_SLOTS}
SHIP_OCC   = _occupied(SHIP_SLOTS)                      # 씬에 실제로 있는 238개 (bay, row, tier) — 0부터, 열 0 = 안벽쪽

def slot_label(k): return f"SHIP/B{k[0] + 1:02d}/R{k[1] + 1:02d}/T{k[2]}"
def slot_key(label):
    _, b, r, t = label.split("/"); return (int(b[1:]) - 1, int(r[1:]) - 1, int(t[1:]))
def slot_x(k):   return SHIP_X + SHIP_SLOT[k]["x"]
def slot_z(k):   return SHIP_Z + SHIP_SLOT[k]["z"]
def slot_top(k): return SHIP_Y + SHIP_SLOT[k]["y"] + CONT_H / 2
def bay_z(b):    return SHIP_Z + next(sl["z"] for sl in SHIP_SLOTS if sl["bay"] == b)

SHIP_BAYS  = sorted({sl["bay"] for sl in SHIP_SLOTS})
REACH_BAYS = [b for b in SHIP_BAYS if 0.1 <= sts_gt(bay_z(b)) <= STS_RANGE["gt"] - 0.1]
NEAR_BAYS  = sorted(REACH_BAYS, key=lambda b: abs(bay_z(b) - STS_ROOT_Z))          # 크레인 홈에서 가까운 순
WORK_BAY   = next(b for b in NEAR_BAYS if any(k[0] == b and k[2] == 2 for k in SHIP_OCC))   # 2단이 있는 가장 가까운 베이


def discharge_order(bays):
    """양하 순서 — 베이마다 안벽쪽 열부터, 스택은 위 단 먼저(아래를 먼저 빼면 위가 무너진다)."""
    return [(b, r, t) for b in bays
            for r in sorted({k[1] for k in SHIP_OCC if k[0] == b})
            for t in (2, 1) if (b, r, t) in SHIP_OCC]


def load_targets(bays):
    """적하 자리 — 1단만 있는 스택의 2단(아래가 받쳐야 올린다). 베이마다 안벽쪽 열부터."""
    return [(b, r, 2) for b in bays
            for r in sorted({k[1] for k in SHIP_SLOT if k[0] == b})
            if (b, r, 2) in SHIP_SLOT and (b, r, 1) in SHIP_OCC and (b, r, 2) not in SHIP_OCC]


# ── 포털 밑 트럭 레인 5개 — 두 다리 안쪽면 사이 균등 분할. L1 = 바다쪽(트롤리 이동이 가장 짧다) ──
LANE_N = 5
LANE_W = (LEG_GAUGE - LEG_FOOT) / LANE_N                                    # 3.26
def lane_x(k): return STS_ROOT_X + LEG_GAUGE - LEG_FOOT / 2 - LANE_W * (k + 0.5)

HO_GROUND = sts_ho(CONT_H)                                                  # 데크에 내려놓은 컨테이너 윗면
CARGO_TOP = max(slot_top(k) for k in SHIP_OCC if k[0] in REACH_BAYS)        # 도달 베이의 최고 화물 윗면 14.33
CLEAR_M   = 2.0                                                             # 매단 컨테이너 밑면 ↔ 최고 화물 여유
HO_CLEAR  = sts_ho(CARGO_TOP + CONT_H + CLEAR_M)
HI = HO_CLEAR

# ── 설계 불변식 — 틀리면 import 에서 멈춘다(생성·검증 둘 다) ──
assert LANE_W / 2 - CONT_W / 2 > 0.3,                "트럭 레인 컨테이너가 다리에 닿는다"
assert LEG_Z - LEG_FOOT / 2 - CONT_L / 2 > 0.5,      "컨테이너가 앞뒤 다리 사이를 못 지난다"
assert all(0 < sts_tr(lane_x(k)) < sts_tr(0.0) for k in range(LANE_N)), "레인이 안벽 안쪽·트롤리 범위 안이 아니다"
assert all(0 < sts_tr(slot_x(k)) < STS_RANGE["tr"] for k in SHIP_OCC if k[0] in REACH_BAYS), "배 열이 트롤리 범위 밖"
assert 0 < HO_GROUND < HO_CLEAR < STS_RANGE["ho"],  "권상 범위 밖"
assert len(discharge_order(NEAR_BAYS)) >= 20 and len(load_targets(NEAR_BAYS)) >= 20, "S13/S14 20개 자리가 모자란다"

# ── 단일 사이클 시나리오(S02~S12) 대표 좌표 — 작업 베이 안벽쪽 첫 스택 ──
GT_HOME = GT_HALF                                       # 크레인 홈(씬 배치 위치) = 주행 중앙
TR_HOME = sts_tr(STS_ROOT_X + TROLLEY_REST_X + HOIST_X)  # 트롤리 홈(씬 휴지 위치) = 23.88 — 0(백리치 끝)이 아니다
GT_WORK = sts_gt(bay_z(WORK_BAY))
_P0 = discharge_order([WORK_BAY])[0]                    # 양하 대상 — 첫 스택 맨 위
_E0 = load_targets([WORK_BAY])[0]                       # 적하 대상 — 첫 빈 2단
SEA, LO             = sts_tr(slot_x(_P0)), sts_ho(slot_top(_P0))
SEA_EMPTY, LO_EMPTY = sts_tr(slot_x(_E0)), sts_ho(slot_top(_E0))
LAND, LO_L          = sts_tr(lane_x(0)), HO_GROUND


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

def gen_S03(s):  # 선적 (정상) — 양하의 역순: 트럭 레인에서 집어 배의 빈 2단(1단 위)에 놓는다
    s.op_mode = AUTO
    s.goto(tr=LAND, ho=LO_L); s.run_until_settled()
    s.detected = True; s.landed = True; s.run_for(1.0)
    s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
    s.goto(ho=HI); s.run_until_settled()
    s.goto(tr=SEA_EMPTY); s.run_until_settled()
    s.goto(ho=LO_EMPTY); s.run_until_settled()
    s.landed = True; s.run_for(1.0)
    s.locked = False; s.carry = False; s.run_for(1.5); s.landed = False; s.detected = False
    s.goto(ho=HI); s.run_until_settled(); s.cycle += 1

def gen_S04(s):  # 트윈 리프트 (정상)
    s.sp_mode = SPTWIN
    discharge_cycle(s)

def gen_S05(s):  # 셧다운 (정상)
    s.op_mode = AUTO
    s.goto(tr=LAND, ho=HI, gt=GT_HOME); s.run_until_settled()   # 홈에 주차
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


BAY_PITCH = 12.192 + 0.6   # 야드 베이 피치 = 40ft 12.192 + 베이간격 0.6 (PortConfig.BayPitchM) — RTG 가 쓴다


# ── 연속 이송 시나리오(S13~S15) 공용 — 자동 위치결정 · 착지 크리프 ──
AUTO_SIGMA = {"gt": 0.03, "tr": 0.03, "ho": 0.02}   # 자동 위치결정 산포(σ, m) — 실기 ±30mm 급(수동 AIM_SIGMA 의 약 1/8)
LAND_CREEP_M, LAND_CREEP_V = 1.0, 0.25              # 착지 1m 전부터 정격 25% — 코너캐스팅·트위스트락 충격 방지


def creep_down(s, ho):
    """착지 — 1m 위까지 정속, 그 아래는 크리프. 목표는 접촉면 아래로 내려가지 않는다:
    스프레더는 받침 윗면에서 멈추고, Unity SpreaderGrabber 통과방지 클램프가 그 아래 자세를 매 틱 밀어올린다."""
    s.goto(ho=ho + LAND_CREEP_M); s.run_until_settled()
    s.vmul = LAND_CREEP_V
    s.goto(ho=ho); s.ho.target = max(s.ho.target, ho)
    s.run_until_settled()
    s.vmul = 1.0


def sts_transfer(s, gt, frm, to):
    """STS 컨테이너 1개 이송. frm/to = (TR, HO, 위치명). gt=None 이면 갠트리는 그대로(같은 베이).
    이송고(HI) → 크리프 착지·잠금 → 이송고 → 횡행 → 크리프 착지·해제 → 이송고."""
    s.goto(gt=gt, tr=frm[0], ho=HI); s.run_until_settled()
    creep_down(s, frm[1])
    s.detected = True; s.landed = True; s.run_for(1.0)
    s.picked(frm[2])
    s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
    s.goto(ho=HI); s.run_until_settled()
    s.goto(tr=to[0]); s.run_until_settled()
    creep_down(s, to[1])
    s.landed = True; s.run_for(1.0)
    s.placed(to[2])
    s.locked = False; s.carry = False; s.run_for(1.5)
    s.landed = False; s.detected = False
    s.goto(ho=HI); s.run_until_settled()
    s.cycle += 1


def _ship(k):       return (sts_tr(slot_x(k)), sts_ho(slot_top(k)), slot_label(k))
def _lane(n, area): return (sts_tr(lane_x(n % LANE_N)), HO_GROUND, f"{area}/L{n % LANE_N + 1}")


def gen_S13(s):  # STS 20개 적하 — 트럭 레인에서 집어 가까운 베이의 빈 2단(1단 위)에 올린다
    s.op_mode = AUTO; s.sigma = dict(AUTO_SIGMA)
    prev = None
    for n, k in enumerate(load_targets(NEAR_BAYS)[:20]):
        gt = sts_gt(slot_z(k))
        sts_transfer(s, None if gt == prev else gt, _lane(n, "TRUCK"), _ship(k))
        prev = gt


def gen_S14(s):  # STS 20개 양하 — 가까운 베이부터, 안벽쪽 스택부터 위 단 먼저 → 트럭 레인 순환(트럭이 싣고 떠난다)
    s.op_mode = AUTO; s.sigma = dict(AUTO_SIGMA)
    prev = None
    for n, k in enumerate(discharge_order(NEAR_BAYS)[:20]):
        gt = sts_gt(slot_z(k))
        sts_transfer(s, None if gt == prev else gt, _ship(k), _lane(n, "TRUCK"))
        prev = gt


def gen_S15(s):  # STS 5개 양하(시연) — 작업 베이의 실제 컨테이너 5개 → 포털 밑 에이프런 레인 5칸
    """오너 지시 2026-09-14 "허공에 작업 · 수식으로 · 시연이라 빡세게".
    크레인 홈에서 출발해 작업 베이(2단 스택이 있는 가장 가까운 베이)로 주행 → 안벽쪽 스택부터 위 단 먼저 집고
    바다쪽 레인(L1)부터 한 칸씩 데크에 내려놓는다. 좌표는 전부 '씬 기하' 식에서 나오고 verify ⑦ 이 씬과 대조한다."""
    s.op_mode = AUTO; s.sigma = dict(AUTO_SIGMA)
    s.gt.pos = s.gt.target = GT_HOME
    for n, k in enumerate(discharge_order([WORK_BAY])[:5]):
        sts_transfer(s, GT_WORK if n == 0 else None, _ship(k), _lane(n, "QUAY"))


# ─────────────── RTG (야드 정리) — 씬 기하에서 유도 (실척 m) ───────────────
#   RtgCraneFbxMoverWiring : 트롤리 ±10.095 (레일 끝 − 휠 외측면) · 권상 행정 19.566
#   RtgCraneFbxPlacer      : 주행 = 블록 존 길이 − 크레인 길이 → 씬 실측 (6.466479 − 0.596189)u × 24 = 140.887
#   PortConfig             : 블록 6열 × 12베이 × 4단 · 열 피치 2.838 · 베이 피치 12.792 · 블록이 스팬·주행 중앙
#   ★ PLC 0 = 각 무버 Min. 권상 Min 은 그랩 평면이 지면에 닿는 높이라 HO = 그랩 평면의 지면 기준 높이.
#   ★ 속도·가속은 STS 와 같다 — RTG 도 같은 StsCrane·CraneOpMode 로 가속알람을 판정한다(RTG 동적데이터 §11).
#   YARD1 = 'RTG 크레인_1' 이 선 블록(선미측, z<0).
RTG_RANGE     = {"gt": 140.887, "tr": 20.19, "ho": 19.566}
RTG_ROW_PITCH = 2.438 + 0.4                                     # 2.838 = 컨테이너폭 + 열간격
RTG_TR_ROW0   = RTG_RANGE["tr"] / 2 - 2.5 * RTG_ROW_PITCH       # 3.000 — 6열이 스팬 중앙 대칭
RTG_TR_HOME   = RTG_RANGE["tr"] / 2                             # 10.095 — 트롤리 x=0 파킹(RtgCraneCreator), 범위 ±10.095 대칭
RTG_GT_BAY0   = RTG_RANGE["gt"] / 2 - 5.5 * BAY_PITCH           # 0.087 — 12베이가 주행 중앙 대칭
RTG_HO_CLEAR  = 4 * CONT_H + 3.0                                # 13.36 — 4단 최상단 + 3m (STS HO_CLEAR 와 같은 규칙)

def rtg_gt(bay): return RTG_GT_BAY0 + bay * BAY_PITCH
def rtg_tr(row): return RTG_TR_ROW0 + row * RTG_ROW_PITCH

# (베이, 열, 단) 0부터 — 단은 1부터. 흩어진 1단 5개를 6번 베이 1·2열로 모아 3단·2단으로 쌓는다.
#   씬 야드가 1단 랜덤 산포라(QuayPartsPlacer 셔플) '정리' = 모아 쌓기. 갠트리는 한 번에 2베이 이내 —
#   더 멀면 주행이 run_until_settled 타임아웃(90s)을 넘는다.
RTG_JOBS = [
    ((3, 1, 1), (5, 0, 1)),
    ((4, 4, 1), (5, 0, 2)),
    ((6, 2, 1), (5, 1, 1)),
    ((7, 5, 1), (5, 1, 2)),
    ((5, 3, 1), (5, 0, 3)),
]


def gen_S16(s):  # RTG 야드 정리 5개 — 흩어진 1단 40ft 를 한 베이로 모아 쌓는다
    s.op_mode = AUTO
    s.gt.pos = s.gt.target = rtg_gt(RTG_JOBS[0][0][0])   # 첫 작업 베이에서 시작(S13/S14 와 같은 이유)
    loc = lambda b, r, t: f"YARD1/B{b + 1:02d}/R{r + 1:02d}/T{t}"
    for (pb, pr, pt), (qb, qr, qt) in RTG_JOBS:
        # ── 집기 ──
        s.goto(gt=rtg_gt(pb), tr=rtg_tr(pr), ho=RTG_HO_CLEAR); s.run_until_settled()
        s.goto(ho=pt * CONT_H); s.run_until_settled()
        s.detected = True; s.landed = True; s.run_for(1.0)
        s.picked(loc(pb, pr, pt))
        s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
        s.goto(ho=RTG_HO_CLEAR); s.run_until_settled()
        # ── 놓기 — 단 t 에 놓으면 그랩 평면 = t × 높이 ──
        s.goto(gt=rtg_gt(qb), tr=rtg_tr(qr)); s.run_until_settled()
        s.goto(ho=qt * CONT_H); s.run_until_settled()
        s.landed = True; s.run_for(1.0)
        s.placed(loc(qb, qr, qt))
        s.locked = False; s.carry = False; s.run_for(1.5)
        s.landed = False; s.detected = False
        s.goto(ho=RTG_HO_CLEAR); s.run_until_settled()
        s.cycle += 1


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
    ("S13", "STS 20개 적하 (트럭 레인→배 빈 2단)", "정상", gen_S13),
    ("S14", "STS 20개 양하 (배→트럭 레인)", "정상", gen_S14),
    ("S15", "STS 5개 양하 (작업 베이 실컨테이너→포털 밑 레인·시연)", "정상", gen_S15),
    ("S16", "RTG 5개 야드 정리 (흩어진 1단→한 베이 적층)", "정상", gen_S16),
]
# 시나리오를 도는 크레인 — 없으면 STS. CSV 형식은 같고 가동범위만 다르다(manifest 에 기록).
CRANE = {"S16": "RTG"}
RANGES = {"STS": RANGE, "RTG": RTG_RANGE}

# 벤더 부록C 회차 — 정상 각10/주의 각5/이상 각5/정비 3 = 83. 기본은 샘플(축소), --full로 전체.
FULL_RUNS = {"정상": 10, "주의": 5, "이상": 5, "정비(제외)": 3}
SAMPLE_RUNS = {"정상": 2, "주의": 2, "이상": 2, "정비(제외)": 1}


HIST_FIELDS = ["seq", "crane", "container", "size_ft", "from", "to", "pick_t_ms", "place_t_ms"]


def write_run(out_dir, sid, name, label, idx, sim, crane):
    os.makedirs(out_dir, exist_ok=True)
    hist_path = None
    if sim.moves:
        hist_path = os.path.join(out_dir, f"run_{idx:02d}.history.csv")
        with open(hist_path, "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=HIST_FIELDS)
            w.writeheader()
            w.writerows({"seq": i + 1, "crane": crane, **m} for i, m in enumerate(sim.moves))
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
    return csv_path, ev_path, hist_path, len(sim.rows)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--full", action="store_true", help="벤더 부록C 전체 회차(83런) 생성")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "output"))
    args = ap.parse_args()
    runs_per = FULL_RUNS if args.full else SAMPLE_RUNS

    manifest = {"generated_by": "PlcSim/generate.py (stopgap)", "dt_ms": int(DT * 1000),
                "mode": "full" if args.full else "sample",
                "range_m": dict(RANGE), "range_m_rtg": dict(RTG_RANGE),
                "vmax_ms": dict(VMAX), "amax_ms2": dict(AMAX),
                "runs": []}
    total_rows = 0; label_count = {}
    for sid, name, label, fn in SCENARIOS:
        n = runs_per[label]
        for i in range(1, n + 1):
            # 런마다 다른 시드 — 같은 시나리오 N회가 서로 다른 데이터가 된다(반복시험 요건).
            #   시드는 (시나리오번호, 회차)로 결정 → 재생성해도 같은 값(재현성). hash()는
            #   프로세스마다 달라지므로(PYTHONHASHSEED) 쓰지 않는다.
            seed = int(sid[1:]) * 1000 + i
            crane = CRANE.get(sid, "STS")
            sim = Sim(sp_mode=SPTWIN if sid == "S04" else SP40, rng=random.Random(seed),
                      range_m=RANGES[crane], id_base=int(sid[1:]) * 1000,
                      gt0=GT_WORK if crane == "STS" else 0.0,   # STS 는 작업 베이 위에서 시작
                      # 트롤리는 씬 휴지 위치에서 시작 — 0(백리치 끝)이면 PlcBridge 첫 스캔에 트롤리가 끝으로 튄다(오너 2026-09-16)
                      tr0=TR_HOME if crane == "STS" else RTG_TR_HOME)
            fn(sim)
            out_dir = os.path.join(args.out, sid)
            csv_path, ev_path, hist_path, rows = write_run(out_dir, sid, name, label, i, sim, crane)
            total_rows += rows
            label_count[label] = label_count.get(label, 0) + 1
            manifest["runs"].append({
                "scenario": sid, "name": name, "label": label, "crane": crane, "run": i, "seed": seed,
                "rows": rows, "duration_s": round(rows * DT, 1),
                "events": len(sim.events), "moves": len(sim.moves),
                "csv": os.path.relpath(csv_path, args.out),
                "events_json": os.path.relpath(ev_path, args.out),
                **({"history_csv": os.path.relpath(hist_path, args.out)} if hist_path else {}),
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
