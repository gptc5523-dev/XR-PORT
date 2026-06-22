#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
가상 PLC 운영 데이터 생성기 (PLCSIM stopgap)
============================================
㈜엠비이 PLCSIM Advanced / 시뮬레이션 데이터(운영시나리오 부록C, 도착 대기) 전까지,
동일 형식의 가상 데이터를 생성한다. 근거 문서:
  - 태그/주소/단위 : MBE-DOC-2026-XR-002 (PLC 데이터 포인트 리스트, DB100/DB101)
  - 시나리오 시퀀스 : MBE-DOC-2026-XR-004 (운영 시나리오 12선)
  - 알람 코드/심각도 : MBE-DOC-2026-XR-003 (알람 코드북)
  - 푸시 주기 100ms : MBE-DOC-2026-XR-001/005

출력(벤더 부록C 형식):
  output/<Sxx>/run_NN.csv          : DB100 운영 태그 시계열(100ms)
  output/<Sxx>/run_NN.events.json  : DB101 알람/이벤트 로그
  output/manifest.json             : 전체 런 목록 + AI 라벨 분포(정상/주의/이상)

※ 가설 데이터 — 실 PLCSIM 도착 시 이 생성기는 폐기/검증대조용으로만 남긴다.
   속도/가속 기본값은 통상 STS 보수값(벤더 확정·튜닝 대상).
"""
import csv, json, os, math, argparse

DT = 0.1  # 100ms 폴링 (사양서 §3.2)

# ── 실척 가동 범위(사양서 §2) · 정격 속도(m/s)/가속(m/s²) ──
RANGE = {"gt": 450.0, "tr": 60.0, "ho": 45.0}
VMAX  = {"gt": 0.7,  "tr": 4.0, "ho": 1.3}    # 통상 STS 보수값(가설)
AMAX  = {"gt": 0.15, "tr": 0.6, "ho": 0.6}    # CraneOpMode 정격 근거

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
    """가속도 한계 트라페조이드 프로파일(점프 없음) — VirtualPlcSource.cs와 동일 로직."""
    def __init__(self, pos):
        self.pos = pos; self.vel = 0.0; self.target = pos; self.accel = 0.0

    def step(self, dt, vmax, amax):
        d = self.target - self.pos
        dist = abs(d); vabs = abs(self.vel)
        stop = (vabs * vabs) / (2 * amax) if amax > 0 else 0.0
        if dist <= stop + 1e-4:
            a = (-1.0 if self.vel > 0 else (1.0 if self.vel < 0 else 0.0)) * amax
        else:
            a = (1.0 if d > 0 else -1.0) * amax
        self.vel += a * dt
        self.vel = max(-vmax, min(vmax, self.vel))
        self.pos += self.vel * dt
        self.accel = a
        # 도착 스냅 — vel 임계는 틱 양자화(amax*dt)보다 커야 한다(안 그러면 목표 근처서 진동만 하고 영영 안 멈춤).
        if abs(self.target - self.pos) < 0.05 and abs(self.vel) <= amax * dt + 1e-3:
            self.pos = self.target; self.vel = 0.0; self.accel = 0.0

    def settled(self):
        return abs(self.target - self.pos) < 0.05 and abs(self.vel) < 0.05


class Sim:
    def __init__(self, sp_mode=SP40, wind=8.0):
        self.t = 0.0
        self.gt = Axis(0.0); self.tr = Axis(0.0); self.ho = Axis(RANGE["ho"])
        self.vmul = 1.0  # 풍속 감속 등 속도 스케일
        self.sp_mode = sp_mode
        self.carry = False; self.load = 0.0
        self.locked = False; self.landed = False; self.detected = False
        self.mismatch = False; self.tele_mm = 12192.0  # 40ft
        self.op_mode = AUTO; self.power = True; self.ready = True
        self.estop = False; self.locked_out = False; self.antisway = True
        self.cycle = 0
        self.wind = wind; self.wind_dir = 270.0; self.wind_alarm = False
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
        self.gt.step(DT, VMAX["gt"] * self.vmul, AMAX["gt"])
        self.tr.step(DT, VMAX["tr"] * self.vmul, AMAX["tr"])
        self.ho.step(DT, VMAX["ho"] * self.vmul, AMAX["ho"])
        self.load = (32.0 if self.sp_mode != SPTWIN else 40.0) if self.carry else 0.0
        self.rows.append(self._row())
        self.t += DT

    def run_for(self, secs):
        for _ in range(int(round(secs / DT))):
            self._tick()

    def goto(self, gt=None, tr=None, ho=None):
        if gt is not None: self.gt.target = gt
        if tr is not None: self.tr.target = tr
        if ho is not None: self.ho.target = ho

    def run_until_settled(self, timeout=90.0):
        n = int(timeout / DT)
        for _ in range(n):
            self._tick()
            if self.gt.settled() and self.tr.settled() and self.ho.settled():
                return
    # ── 스냅샷 행 ──
    @staticmethod
    def _b(x): return 1 if x else 0

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
            "HO_Direction": self._b(self.ho.vel > 0), "HO_Load": round(self.load, 2),
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
            "ENV_Wind_Speed": round(self.wind, 1), "ENV_Wind_Direction": round(self.wind_dir, 1),
            "ENV_Wind_Alarm": self._b(self.wind_alarm),
            "ALM_Active": self._b(self.alm_code != 0), "ALM_Latest_Code": self.alm_code,
            "ALM_Latest_Severity": self.alm_sev, "ALM_Latest_Source": self.alm_src,
            "COM_Link_Status": self._b(self.comm),
        }


# ─────────────────────── 시나리오 ───────────────────────
HI = RANGE["ho"]      # 권상 최상단
LO = 2.0              # 안착 높이
SEA = RANGE["tr"]     # 트롤리 선박측
LAND = 0.0            # 트롤리 안벽측

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
    s.goto(tr=LAND); s.run_until_settled()                  # 트롤리 안벽측
    s.goto(ho=LO); s.run_until_settled()                    # 권상 하강(안착)
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

def gen_S03(s):  # 선적 (정상) — 양하의 역순 근사
    s.op_mode = AUTO
    s.goto(tr=LAND, ho=LO); s.run_until_settled()
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
    s.ho_snag = True; s.event(3013); s.goto(ho=s.ho.pos); s.run_for(2.0)   # 권상 자동 정지
    s.goto(ho=s.ho.pos - 1.0); s.run_until_settled()                       # 약간 하강(걸림 해제)
    s.ho_snag = False; s.clear_alarm(); s.run_for(1.0)
    s.goto(ho=HI); s.run_until_settled(); s.cycle += 1

def gen_S08(s):  # 안착 실패/재시도 (주의)
    s.goto(tr=SEA, ho=LO); s.run_until_settled()
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
    s.estop = True; s.event(5001)                              # E-Stop — 모든 축 즉시 정지
    s.gt.vel = s.tr.vel = s.ho.vel = 0.0
    s.goto(gt=s.gt.pos, tr=s.tr.pos, ho=s.ho.pos)
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
    s.goto(ho=s.ho.pos); s.run_for(4.0)
    s.ho_motor_alarm = False; s.clear_alarm(); s.run_for(1.0)

def gen_S12(s):  # 정비 모드 (정비 — 라벨 제외)
    s.op_mode = MAINT; s.event(5015, kind="event")
    s.locked_out = True; s.event(5005); s.run_for(8.0)         # LOTO
    s.locked_out = False; s.clear_alarm(); s.op_mode = MANUAL; s.run_for(2.0)


def gen_S13(s):  # 20개 적하 (육지 야드 → 배), 20ft/40ft 혼합 — 배 없이 바다쪽 TR/GT 좌표로 적재
    s.op_mode = AUTO
    TR_LAND, TR_SEA = 8.0, 50.0        # 트롤리: 육지(backreach) ↔ 바다(배 위) — 픽업/적치를 트롤리로만 전환
    PICK_LO, SHIP_DECK = 2.0, 12.0     # 권상: 야드 픽업고 ↔ 배 갑판 적치고
    HI_CLEAR = 18.0                    # 이송 클리어고(맨 위까지 안 올림 — 현실 작업고)
    GT0 = 112.0                        # 첫 작업 베이(갠트리)
    # 크레인이 이미 작업 베이에 위치 — 0에서 120m 장거리 주행/타임아웃 카스케이드 제거.
    s.gt.pos = GT0; s.gt.target = GT0
    for i in range(20):
        is40 = (i % 2 == 1)            # 20ft/40ft 교대 혼합
        s.sp_mode = SP40 if is40 else SP20
        gt_bay = GT0 + i * 1.0         # 배 베이 — 안벽(갠트리축) 1m 간격 소폭 스텝(현실: 베이마다 약간 이동)
        s.goto(gt=gt_bay)              # 베이로 소폭 이동(다음 settle 중 완료)
        # ── 픽업(육지) — 같은 갠트리에서 트롤리만 육지쪽 ──
        s.goto(tr=TR_LAND, ho=HI_CLEAR); s.run_until_settled()
        s.goto(ho=PICK_LO); s.run_until_settled()
        s.detected = True; s.landed = True; s.run_for(1.0)
        s.locked = True; s.carry = True; s.run_for(1.5); s.landed = False
        s.goto(ho=HI_CLEAR); s.run_until_settled()
        # ── 적치(배) — 트롤리 바다쪽, 같은 갠트리 ──
        s.goto(tr=TR_SEA); s.run_until_settled()
        s.goto(ho=SHIP_DECK); s.run_until_settled()
        s.landed = True; s.run_for(1.0)
        s.locked = False; s.carry = False; s.run_for(1.5)
        s.landed = False; s.detected = False
        s.goto(ho=HI_CLEAR); s.run_until_settled()
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
    ("S13", "20개 적하 (육지→배)", "정상", gen_S13),
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
    args = ap.parse_args()
    runs_per = FULL_RUNS if args.full else SAMPLE_RUNS

    manifest = {"generated_by": "PlcSim/generate.py (stopgap)", "dt_ms": int(DT * 1000),
                "mode": "full" if args.full else "sample", "runs": []}
    total_rows = 0; label_count = {}
    for sid, name, label, fn in SCENARIOS:
        n = runs_per[label]
        for i in range(1, n + 1):
            sim = Sim(sp_mode=SPTWIN if sid == "S04" else SP40)
            fn(sim)
            out_dir = os.path.join(args.out, sid)
            csv_path, ev_path, rows = write_run(out_dir, sid, name, label, i, sim)
            total_rows += rows
            label_count[label] = label_count.get(label, 0) + 1
            manifest["runs"].append({
                "scenario": sid, "name": name, "label": label, "run": i,
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
