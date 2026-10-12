#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""AI 상태 분류 데이터셋 v2 (WBS 6.2 후속 · 6.4·6.7·6.8 용) — 실 PLC 데이터 없이 학습·평가하기 위한 합성 데이터.

v1(output/, generate.py)과 다른 점
  · 라벨이 행마다 있다(y_state) — 런 하나에 라벨 하나가 아니라 상태가 시간에 따라 바뀐다(창 분류용).
  · 긴 교대 근무 런: 양하·적하를 이어 가며 고장·환경 사건이 아무 때나 끼어든다.
  · 경계 사례: 풍속·하중·걸림 하중비·잠금 지연·통신 끊김·권상 속도 저하를 문턱 앞뒤로 훑는 짧은 런.
  · 현실 잡음: 엔코더·하중계(이득·영점·드리프트)·풍속계 잡음, 센서 깜빡임, 스캔 지터·누락, 통신 순단(값 정지).
    하중계는 설치 상태 STS 에서 인양하중의 3~5% 오차가 흔하다(과부하 보호용, 정밀 계량 아님) — 프로파일 A·B.
  · 지표용 기준 데이터셋(bench/): 지표 5 100건 · 지표 7 100건 · 확장 600건(절반은 잡음 특성이 다른 B 프로파일) — 고정 시드.

라벨 규칙(SSOT = 알람 코드북 심각도, 서버 xrcrane_db.py 와 같다): sev 0 정상 · sev 1 주의 · sev ≥2 이상.
  코드 없는 전조(하중 105%·하중 급변·권상 속도 저하)는 주의. 정비(LOTO) 중은 -1(학습 제외).
  v1 은 스내그(3013 sev2)·부분 잠금(4003 sev2)을 '주의'로 붙였다 — v2 는 코드북대로 '이상'.
y_* 열은 정답이다 — 입력 특징으로 쓰면 안 된다. ALM_* 열도 라벨을 거의 그대로 담는다(입력에 넣을지는 학습 쪽 결정).

실행: python3 PlcSim/gen_ai.py [--scale 1.0] [--out PlcSim/ai_data] [--jobs N] · 검사: python3 PlcSim/verify_ai.py
"""
import argparse, csv, gzip, hashlib, io, json, math, os, random, sys
from collections import deque
from multiprocessing import Pool

D = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, D)
import generate as g  # noqa: E402

DT = g.DT
CODEBOOK = os.environ.get("XRCRANE_CODEBOOK") or next(p for p in (os.path.join(D, "..", "Assets", "Crane", "Resources", "Sts", "AlarmCodebook.json"),
                                                               os.path.join(D, "AlarmCodebook.json")) if os.path.exists(p))   # 저장소 · 서버 app/
_cb = {e["code"]: e for e in json.load(open(CODEBOOK, encoding="utf-8"))["entries"]}
for _c, _e in _cb.items():   # 시나리오가 쓰는 코드를 생성기 표에 보탠다(심각도·출처·한글은 코드북 그대로)
    g.ALM.setdefault(_c, (_e["sev"], _c // 1000, _e["ko"]))


def sev_state(sev):
    return min(sev, 2)


SWL = 65.0                       # ContainerLoad.RatedLoadTon(크레인 정격 인양하중)
LOAD_WARN, LOAD_TRIP = 1.05, 1.10   # ContainerLoad.OverloadWarnFrac / OverloadTripFrac
WIND_WARN, WIND_STOP, WIND_CLEAR = 18.0, 25.0, 16.0   # 코드북 6001·6002, 해제 히스테리시스 live.py
SNAG_RATIO, SPIKE_RATIO = 1.25, 1.10   # 하중비 ≥1.25 걸림 트립(3013), 1.10~1.25 급변 주의 — 가정값
SLOW_WARN, SLOW_TRIP = 0.88, 0.80      # 권상 속도 정격비 — 0.88 이하 주의, 0.80 이하 권선 과열(3002) — 가정값
LOCK_ALARM_S = 5.0                      # 5초 안에 못 잠그면 부분 잠금(4003) — v1 S08
COMM_ALARM_S = 3.0                      # 통신이 3초 넘게 끊기면 5023 — 가정값

# 원인 → (한글, 조치, 코드, 코드가 없을 때의 상태)
CAUSES = {
    "wind_warn":      ("풍속 주의", "70% 감속 운전, 풍속 추이 확인", 6001, None),
    "wind_stop":      ("풍속 운전중지", "스프레더 회수·작업 중지, 16 m/s 미만으로 내려갈 때까지 대기", 6002, None),
    "load_high":      ("인양 하중 정격 근접(105% 이상)", "감속 인양, 화물 중량 재확인", 0, 1),
    "overload":       ("권상 과부하(정격 110% 초과)", "인양 중지·화물 내려놓기, 중량 재확인", 3012, None),
    "load_spike":     ("권상 하중 급변", "걸림·간섭 여부 육안 확인", 0, 1),
    "snag":           ("권상 걸림(스내그)", "권상 보호정지, 살짝 내려 걸림 해제", 3013, None),
    "hoist_slow":     ("권상 속도 저하", "권상 모터·인버터 점검 예약", 0, 1),
    "hoist_overtemp": ("권상 모터 권선 온도 초과", "권상 정지, 냉각 후 정비", 3002, None),
    "bearing_temp":   ("권상 베어링 온도 상승", "운전 계속, 온도 추이 감시", 3003, None),
    "overcurrent_gt": ("갠트리 모터 과전류", "주행 정지, 구동부 점검 후 재기동", 1001, None),
    "overcurrent_tr": ("트롤리 모터 과전류", "횡행 정지, 구동부 점검 후 재기동", 2001, None),
    "overcurrent_ho": ("권상 모터 과전류", "권상 정지, 구동부 점검 후 재기동", 3001, None),
    "anticollision":  ("갠트리 충돌방지 센서 작동", "주행로 장애물 확인 후 재기동", 1012, None),
    "estop":          ("비상정지", "원인 확인 후 해제·재기동", 5001, None),
    "partial_lock":   ("스프레더 4코너 부분 잠금", "5~10 cm 들어 재정렬 후 재잠금", 4003, None),
    "no_landing":     ("스프레더 안착 미검출", "살짝 들었다가 재안착", 4006, None),
    "comm_lost":      ("통합서버 통신 단절", "네트워크 확인, 복구까지 화면 값 정지", 5023, None),
}
CSTATE = {c: (sev_state(_cb[v[2]]["sev"]) if v[2] else v[3]) for c, v in CAUSES.items()}
STATE_KO = ("정상", "주의", "이상")
ACT_KO = {"carry": "컨테이너 이송 중", "empty_move": "공차 이동 중", "handling": "컨테이너 체결·해제 중", "idle": "대기 중"}
Y_FIELDS = ["y_state", "y_cause", "y_code", "y_activity", "y_load_t"]
FIELDS = g.CSV_FIELDS + Y_FIELDS
FT = {20: (g.SP20, 6058.0), 40: (g.SP40, 12192.0), 45: (g.SP45, 13716.0), "twin": (g.SPTWIN, 12192.0)}

# 센서 잡음 프로파일 — A: 학습·기본 평가, B: 잡음이 크고 교정이 다른 현장(강건성 평가)
PROFILES = {
    "A": dict(pos=0.002, vel=0.004, load_gain=0.015, load_off=0.3, load_noise=0.08, load_rel=0.004, load_drift=0.002,
              wind_gain=0.02, wind_noise=0.3, dir_noise=3.0, chatter=0.0015, jitter_ms=12, drop=0.002, blip_per_h=6.0),
    "B": dict(pos=0.004, vel=0.008, load_gain=0.03, load_off=0.6, load_noise=0.15, load_rel=0.008, load_drift=0.005,
              wind_gain=0.04, wind_noise=0.5, dir_noise=6.0, chatter=0.004, jitter_ms=25, drop=0.006, blip_per_h=15.0),
}


class LSim(g.Sim):
    """행마다 정답 상태를 남기는 Sim. 물리는 참값으로 돌리고, 잡음은 쓰기 직전에 sensors() 가 입힌다."""
    load = property(lambda s: s.cargo_t * s.spike if s.carry else 0.0, lambda s, v: None)

    def __init__(self, crane, rng, **kw):
        self.cargo_t = 0.0; self.spike = 1.0; self.comm_down = False
        super().__init__(rng=rng, **kw)
        self.crane = crane
        self.active = {}; self.episodes = []; self.maint = False
        self.timers = []; self.faults = []; self.in_handler = False
        self.wind_mean_fn = lambda t: 8.0; self.ou = 0.0; self.w3 = deque(maxlen=int(3.0 / DT))
        self.ho_base = self.vdev["ho"]; self.degrade_rate = 0.0; self.maint_t = 0.0

    # ── 상태(원인) 켜고 끄기 — 물리 플래그·알람 열은 여기서만 바뀐다 ──
    def on(self, cause, hold=None):
        if cause not in self.active:
            self.active[cause] = self.t
            self.episodes.append({"cause": cause, "state": CSTATE[cause], "code": CAUSES[cause][2],
                                  "t_on": round(self.t, 1), "t_off": None})
            if CAUSES[cause][2]: self.event(CAUSES[cause][2])
            self._flags()
        if hold is not None:
            self.timers.append((self.t + hold, lambda c=cause: self.off(c)))

    def off(self, cause):
        if cause in self.active:
            del self.active[cause]
            for e in reversed(self.episodes):
                if e["cause"] == cause and e["t_off"] is None: e["t_off"] = round(self.t, 1); break
            self._flags()

    def _flags(self):
        a = self.active
        self.ho_snag = "snag" in a; self.ho_overload = "overload" in a
        self.ho_motor_alarm = "hoist_overtemp" in a or "overcurrent_ho" in a
        self.wind_alarm = "wind_warn" in a or "wind_stop" in a
        self.mismatch = "partial_lock" in a
        self.comm = not ("comm_lost" in a or self.comm_down)

    def _top(self):
        if not self.active: return 0, "", 0
        c = max(self.active, key=lambda k: (CSTATE[k], self.active[k]))
        return CSTATE[c], c, CAUSES[c][2]

    def _top_alarm(self):
        coded = [k for k in self.active if CAUSES[k][2]]
        if not coded: return 0, 0, 0
        c = max(coded, key=lambda k: (_cb[CAUSES[k][2]]["sev"], self.active[k]))
        code = CAUSES[c][2]
        return code, _cb[code]["sev"], code // 1000

    # ── 시간 진행 ──
    def _tick(self):
        env = (0.7 if "wind_warn" in self.active else 1.0) * (0.8 if "load_high" in self.active else 1.0)
        for ax, k in ((self.gt, "gt"), (self.tr, "tr"), (self.ho, "ho")):
            ax.step(DT, g.VMAX[k] * self.vmul * env * self.vdev[k], g.AMAX[k] * self.adev[k])
        self._step_env(); self._hooks()
        self.rows.append(self._row()); self.t += DT

    def _step_env(self):
        # 바람 = 평균(사건 일정) + 난류(OU, 시간상수 8초, 난류강도 ~15%). 경보는 3초 평균으로 판정한다.
        m = self.wind_mean_fn(self.t); tau = 8.0; sig = 0.15 * m + 0.3
        self.ou += -self.ou / tau * DT + sig * math.sqrt(2 * DT / tau) * self.rng.gauss(0, 1)
        self.wind_inst = max(0.0, m + self.ou); self.w3.append(self.wind_inst)
        self.wind_dir_inst = (self.wind_dir + 12 * math.sin(self.t / 90.0) + self.rng.gauss(0, 1.5)) % 360

    def _hooks(self):
        w = sum(self.w3) / len(self.w3)
        a = self.active
        if w >= WIND_STOP and "wind_stop" not in a:
            self.on("wind_stop"); self.off("wind_warn"); self.faults.insert(0, (0.0, None, self._wind_hold))
        elif WIND_WARN <= w < WIND_STOP and not a.keys() & {"wind_warn", "wind_stop"}:
            self.on("wind_warn")
        elif w < WIND_CLEAR and "wind_warn" in a:
            self.off("wind_warn")
        if self.degrade_rate and not self.maint:   # 권상 성능 저하 — 정비 뒤부터 서서히
            self.vdev["ho"] = self.ho_base - self.degrade_rate * (self.t - self.maint_t)
            r = self.vdev["ho"]
            if r <= SLOW_WARN and "hoist_slow" not in a: self.on("hoist_slow")
            if r <= SLOW_TRIP and "hoist_overtemp" not in a and not any(f[2] == self._overtemp for f in self.faults):
                self.faults.insert(0, (0.0, None, self._overtemp))
        if self.timers:
            due = [f for f in self.timers if f[0] <= self.t]
            if due:
                self.timers = [f for f in self.timers if f[0] > self.t]
                for _, fn in due: fn()

    def _fire(self):
        """막는 사건(정지·대기)은 동작 루프 사이에서만 처리한다 — 처리 중 또 끼어들지 않는다."""
        if self.in_handler or not self.faults: return
        for i, (t, cond, fn) in enumerate(self.faults):
            if t <= self.t:
                if cond is not None and not cond(self):
                    self.faults[i] = (self.t + 3.0, cond, fn); return   # 조건이 안 맞으면 3초 뒤 다시
                del self.faults[i]
                saved = (self.gt.target, self.tr.target, self.ho.target)
                self.in_handler = True
                try: fn()
                finally: self.in_handler = False
                self.gt.target, self.tr.target, self.ho.target = saved   # 하던 동작으로 복귀
                return

    def run_for(self, secs, jitter=True):
        if jitter: secs *= self.rng.uniform(0.85, 1.30)
        for _ in range(max(1, int(round(secs / DT)))):
            self._tick(); self._fire()

    def run_until_settled(self, timeout=240.0):
        for _ in range(int(timeout / DT)):
            self._tick(); self._fire()
            if self.gt.settled() and self.tr.settled() and self.ho.settled(): return

    def wait(self, secs): self.run_for(secs, jitter=False)

    # ── 막는 사건 처리기 ──
    def _wind_hold(self):
        """6002 — 이송고로 올리고 트롤리를 육지측 홈으로, 바람이 16 m/s 아래로 60초 머물 때까지 선다(최대 40분)."""
        self.goto(ho=self.clear_h, tr=self.tr_home); self.run_until_settled()
        calm, waited = 0.0, 0.0
        while waited < 2400 and calm < 60.0:
            self.wait(1.0); waited += 1.0
            calm = calm + 1.0 if sum(self.w3) / len(self.w3) < WIND_CLEAR else 0.0
        self.off("wind_stop")

    def _overtemp(self):
        self.on("hoist_overtemp"); self.arrest(2.0); self.wait(self.rng.uniform(5, 15))
        self.maint = True; self.op_mode = g.MAINT; self.locked_out = True; self.event(5005)
        self.wait(self.rng.uniform(60, 180))             # 정비 — 라벨 -1
        self.vdev["ho"] = self.ho_base = self.rng.gauss(1.0, 0.03); self.maint_t = self.t
        self.off("hoist_overtemp"); self.off("hoist_slow")
        self.maint = False; self.locked_out = False; self.op_mode = g.MANUAL; self.wait(3.0); self.op_mode = g.AUTO

    def stop_fault(self, cause, mul, lo, hi):
        def fn():
            self.on(cause); self.arrest(mul, estop=(cause == "estop")); self.wait(self.rng.uniform(lo, hi))
            self.estop = False; self.off(cause); self.wait(1.0)
        return fn

    def comm_fault(self, secs):
        """통신 끊김 — 행은 계속 쌓이지만 COM_Link 0, 값은 마지막 정상값으로 멈춘다(sensors). 3초 넘으면 5023."""
        def fn():
            self.comm_down = True; self._flags()
            if secs >= COMM_ALARM_S: self.timers.append((self.t + COMM_ALARM_S, lambda: self.on("comm_lost")))
            def restore():
                self.comm_down = False; self.off("comm_lost"); self._flags()
            self.timers.append((self.t + secs, restore))
        return fn

    def _load_cell(self):   # 참값(관성 포함) — 계측 잡음은 sensors() 에서
        if self.load <= 0.0: return 0.0
        inertia = self.load * (self.ho.accel / 9.81) if abs(self.ho.vel) > 1e-3 else 0.0
        return max(0.0, self.load + inertia)

    def _row(self):
        self.alm_code, self.alm_sev, self.alm_src = self._top_alarm()
        r = super()._row()
        r["GT_Motor_Alarm"] = int("overcurrent_gt" in self.active)
        r["TR_Motor_Alarm"] = int("overcurrent_tr" in self.active)
        st, cause, code = self._top()
        moving = any(abs(a.vel) > 1e-3 for a in (self.gt, self.tr, self.ho))
        act = "carry" if self.carry and moving else "empty_move" if moving else "handling" if (self.landed or self.detected) else "idle"
        r.update(y_state=-1 if self.maint else st, y_cause="maint" if self.maint else cause, y_code=code,
                 y_activity=act, y_load_t=round(self.load, 2))
        return r


# ═══════════════ 작업(사이클) — 단계마다 사건이 끼어들 수 있다 ═══════════════

def cargo_spec(rng, force_t=None):
    """화물 종류·총중량(t). ISO 최대 총중량 30.48t, 오신고(실중량 초과) 3%."""
    u = rng.random()
    kind = 40 if u < 0.55 else 20 if u < 0.70 else "twin" if u < 0.90 else 45
    def box(empty):
        return empty if rng.random() < 0.22 else min(30.48, max(empty + 2, rng.gauss(20.0, 6.5)))
    t = (box(2.2) + box(2.2)) if kind == "twin" else box({20: 2.2, 40: 3.8, 45: 4.8}[kind])
    if rng.random() < 0.03: t += rng.uniform(2.0, 10.0)
    if force_t is not None: kind, t = "twin", force_t
    return kind, round(t, 2)


def land(s, ho, creep):
    """착지 — 1 m 위까지 정속, 그 아래 크리프(정격 25%)."""
    if creep:
        s.goto(ho=ho + 1.0); s.run_until_settled(); s.vmul = 0.25
    s.goto(ho=ho); s.ho.target = max(s.ho.target, ho); s.run_until_settled(); s.vmul = 1.0


def engage(s, ho, plan, key):
    """체결·해제 — 안착 미검출(4006)·잠금 지연(5초 넘으면 4003) 경계를 포함."""
    if plan.get(key + "_nolanding"):
        s.wait(s.rng.uniform(1.5, 3.0)); s.on("no_landing")
        s.goto(ho=ho + 0.4); s.run_until_settled(); s.wait(1.0); land(s, ho, False); s.off("no_landing")
    s.landed = True
    delay = plan.get(key + "_delay", s.rng.uniform(0.8, 2.2))
    if delay >= LOCK_ALARM_S:
        s.wait(LOCK_ALARM_S); s.on("partial_lock"); s.wait(1.0)
        s.goto(ho=ho + s.rng.uniform(0.2, 0.4)); s.run_until_settled()
        land(s, ho, False); s.off("partial_lock"); s.wait(s.rng.uniform(0.8, 1.5))
    else:
        s.wait(delay)


def lift(s, hi, plan):
    """인양 — 하중 급변·걸림 경계. 비율 <1.10 은 평범한 흔들림(라벨 없음)."""
    s.goto(ho=hi)
    r = plan.get("spike")
    if r is None: s.run_until_settled(); return
    s.wait(s.rng.uniform(1.0, 3.0))
    n = int(s.rng.uniform(3, 8))
    for i in range(1, n + 1): s.spike = 1 + (r - 1) * i / n; s.wait(DT)
    if r >= SNAG_RATIO:
        s.on("snag"); s.arrest(2.0); s.wait(s.rng.uniform(1.5, 3.0))
        s.goto(ho=s.ho.pos - s.rng.uniform(0.5, 1.2)); s.run_until_settled()
        s.spike = 1.0; s.off("snag"); s.wait(1.0)
    else:
        if r >= SPIKE_RATIO: s.on("load_spike", hold=10.0)
        for i in range(n, -1, -1): s.spike = 1 + (r - 1) * i / n; s.wait(DT)
        s.spike = 1.0
    s.goto(ho=hi); s.run_until_settled()


def transfer(s, gt, frm, to, hi, plan, creep=True):
    """컨테이너 1개 이송. frm/to = (TR, HO). 과부하면 들어 올리다 멈추고 내려놓는다(이송 취소)."""
    kind, s.cargo_t = plan["cargo"]
    s.sp_mode, s.tele_mm = FT[kind]
    s.goto(gt=gt, tr=frm[0], ho=hi); s.run_until_settled()
    land(s, frm[1], creep)
    s.detected = True
    engage(s, frm[1], plan, "pick")
    s.locked = True; s.carry = True; s.wait(1.5); s.landed = False
    ratio = s.cargo_t / SWL
    if ratio >= LOAD_TRIP:
        s.goto(ho=frm[1] + 0.3); s.run_until_settled(); s.wait(s.rng.uniform(0.5, 1.5))
        s.on("overload"); s.arrest(2.0); s.wait(s.rng.uniform(4, 10))
        land(s, frm[1], False); s.landed = True; s.wait(1.0)
        s.locked = False; s.carry = False; s.wait(1.5); s.off("overload")
        s.landed = False; s.detected = False
        s.goto(ho=hi); s.run_until_settled(); return
    if ratio >= LOAD_WARN: s.on("load_high")
    lift(s, hi, plan)
    s.goto(tr=to[0]); s.run_until_settled()
    land(s, to[1], creep)
    engage(s, to[1], plan, "place")
    s.locked = False; s.carry = False; s.wait(1.5); s.off("load_high")
    s.landed = False; s.detected = False
    s.goto(ho=hi); s.run_until_settled()
    s.cycle += 1


def cycle_plan(rng, rates, force=None):
    """사이클 하나에 들어갈 사건 — rates 는 사이클당 확률."""
    force = force or {}
    p = {"cargo": cargo_spec(rng, force.get("load_t"))}
    if "load_t" not in force and rng.random() < rates["heavy"]:   # 하중 경계(정격 98~118%)
        p["cargo"] = ("twin", round(SWL * rng.uniform(0.98, 1.18), 2))
    u = rng.random()
    if "spike" in force: p["spike"] = force["spike"]
    elif u < rates["snag"]: p["spike"] = rng.uniform(SNAG_RATIO, 2.2)
    elif u < rates["snag"] + rates["spike"]: p["spike"] = rng.uniform(SPIKE_RATIO, SNAG_RATIO)
    elif u < rates["snag"] + rates["spike"] + rates["bump"]: p["spike"] = rng.uniform(1.02, SPIKE_RATIO)   # 라벨 없는 흔들림
    for k in ("pick", "place"):
        v = rng.random()
        if k + "_delay" in force: p[k + "_delay"] = force[k + "_delay"]
        elif v < rates["lock_fail"]: p[k + "_delay"] = rng.uniform(LOCK_ALARM_S, 9.0)
        elif v < rates["lock_fail"] + rates["lock_slow"]: p[k + "_delay"] = rng.uniform(2.5, LOCK_ALARM_S)   # 느리지만 정상
        p[k + "_nolanding"] = rng.random() < rates["nolanding"]
    return p


RATES = {"heavy": 0.04, "snag": 0.03, "spike": 0.05, "bump": 0.15, "lock_fail": 0.04, "lock_slow": 0.08, "nolanding": 0.03}
PER_HOUR = {"estop": 0.6, "overcurrent_gt": 0.4, "overcurrent_tr": 0.4, "overcurrent_ho": 0.5,
            "anticollision": 0.3, "comm": 1.0, "bearing_temp": 0.4}


def moving(axis):
    return lambda s: abs(getattr(s, axis).vel) > 0.05


def schedule_faults(s, minutes, mult):
    """시간 단위 사건을 포아송 과정으로 미리 깐다 — 조건(그 축이 움직일 때)이 맞을 때 터진다."""
    rng = s.rng
    def times(rate_h):
        out, t = [], 0.0
        while True:
            t += rng.expovariate(rate_h * mult / 3600.0)
            if t >= minutes * 60: return out
            out.append(t)
    for t in times(PER_HOUR["estop"]):
        s.faults.append((t, lambda x: any(abs(a.vel) > 0.05 for a in (x.gt, x.tr, x.ho)), s.stop_fault("estop", g.EMG_BRAKE, 5, 30)))
    for c, ax in (("overcurrent_gt", "gt"), ("overcurrent_tr", "tr"), ("overcurrent_ho", "ho")):
        for t in times(PER_HOUR[c]): s.faults.append((t, moving(ax), s.stop_fault(c, 2.0, 3, 10)))
    for t in times(PER_HOUR["anticollision"]): s.faults.append((t, moving("gt"), s.stop_fault("anticollision", 3.0, 10, 40)))
    for t in times(PER_HOUR["comm"]):
        secs = rng.uniform(0.5, 2.5) if rng.random() < 0.4 else rng.uniform(COMM_ALARM_S, 25.0)
        s.faults.append((t, None, s.comm_fault(secs)))
    for t in times(PER_HOUR["bearing_temp"]):
        s.faults.append((t, None, lambda: s.on("bearing_temp", hold=s.rng.uniform(60, 300))))
    s.faults.sort(key=lambda f: f[0])


def wind_schedule(rng, minutes, storm_p, force_peak=None):
    """바람 평균 — 평시 3~12 m/s, 확률적으로 폭풍 한 번(오름 2~5분·유지 2~10분·내림 3~8분)."""
    base = rng.uniform(3.0, 12.0)
    if force_peak is None and rng.random() >= storm_p: return lambda t: base
    peak = force_peak if force_peak is not None else rng.uniform(14.0, 30.0)
    if force_peak is None:
        t0, up, hold, down = rng.uniform(0.05, 0.5) * minutes * 60, rng.uniform(120, 300), rng.uniform(120, 600), rng.uniform(180, 480)
    else:   # 경계 런 — 짧은 런 안에 오르고 머무르고 내려온다
        t0, up, hold, down = rng.uniform(10, 30), rng.uniform(30, 60), rng.uniform(60, 120), rng.uniform(30, 60)
    def f(t):
        if t < t0: return base
        if t < t0 + up: return base + (peak - base) * (t - t0) / up
        if t < t0 + up + hold: return peak
        if t < t0 + up + hold + down: return peak - (peak - base) * (t - t0 - up - hold) / down
        return base
    return f


STS_DIS = g.discharge_order(g.NEAR_BAYS)
STS_LOAD = g.load_targets(g.NEAR_BAYS)


def sts_job(s, n):
    if s.rng.random() < 0.5:
        k = s.rng.choice(STS_DIS); a, b = g._ship(k)[:2], g._lane(n, "TRUCK")[:2]
    else:
        k = s.rng.choice(STS_LOAD); a, b = g._lane(n, "TRUCK")[:2], g._ship(k)[:2]
    return g.sts_gt(g.slot_z(k)), a, b


def rtg_job(s, n):
    """RTG — 현재 베이에서 2베이 이내로 집어 놓는다(4단까지)."""
    cur = max(0, min(11, round((s.gt.pos - g.RTG_GT_BAY0) / g.BAY_PITCH)))
    pb = max(0, min(11, cur + s.rng.randint(-2, 2))); qb = max(0, min(11, pb + s.rng.randint(-1, 1)))
    pick = (g.rtg_tr(s.rng.randint(0, 5)), s.rng.randint(1, 4) * g.CONT_H)
    place = (g.rtg_tr(s.rng.randint(0, 5)), s.rng.randint(1, 4) * g.CONT_H)
    return g.rtg_gt(pb), pick, place, g.rtg_gt(qb)


def make_sim(spec):
    rng = random.Random(spec["seed"])
    if spec["crane"] == "STS":
        s = LSim("STS", rng, sp_mode=g.SP40, range_m=g.RANGE, gt0=g.GT_HOME, tr0=g.TR_HOME, ho0=g.HO_HOME)
        s.clear_h, s.tr_home = g.HI, g.TR_HOME
    else:
        s = LSim("RTG", rng, sp_mode=g.SP40, range_m=g.RTG_RANGE, gt0=g.RTG_GT_HOME, tr0=g.RTG_TR_HOME, ho0=g.RTG_HO_HOME)
        s.clear_h, s.tr_home = g.RTG_HO_CLEAR, g.RTG_TR_HOME
    s.sigma = dict(g.AUTO_SIGMA); s.op_mode = g.AUTO
    return s


def run_spec(spec):
    """런 하나 생성 → 잡음 입히기. spec: kind(shift/boundary), crane, seed, minutes, mult, profile, force."""
    s = make_sim(spec)
    rng, mult, force = s.rng, spec.get("mult", 1.0), spec.get("force", {})
    minutes = spec["minutes"]
    storm_p = min(0.9, 0.35 * mult) if spec["kind"] == "shift" else 0.0   # 경계 런은 훑는 변수 하나만
    s.wind_mean_fn = wind_schedule(rng, minutes, storm_p, force.get("wind_peak"))
    if "slow" in force:
        s.vdev["ho"] = s.ho_base = force["slow"]
        if force["slow"] <= SLOW_WARN: s.on("hoist_slow")
        if force["slow"] <= SLOW_TRIP: s.faults.append((rng.uniform(10, 40), None, s._overtemp))
    elif spec["kind"] == "shift" and rng.random() < min(0.6, 0.2 * mult):   # 교대 중 권상 성능 저하 — 0.80 도달까지 10~60분
        s.degrade_rate = (s.ho_base - SLOW_TRIP) / (rng.uniform(10, 60) * 60)
    if spec["kind"] == "shift": schedule_faults(s, minutes, mult)
    if "comm" in force: s.faults.append((rng.uniform(20, 60), None, s.comm_fault(force["comm"])))
    rates = {k: min(0.5, v * mult) for k, v in RATES.items()}
    if spec["kind"] == "shift" and rng.random() < 0.5: s.wait(rng.uniform(5, 30))   # 교대 시작 대기
    n = 0
    while s.t < minutes * 60:
        plan = cycle_plan(rng, rates, force if n == 0 else None)
        if s.crane == "STS":
            gt, a, b = sts_job(s, n); transfer(s, gt, a, b, g.HI, plan)
        else:
            gt, a, b, gt2 = rtg_job(s, n)
            s.goto(gt=gt); s.run_until_settled()
            transfer(s, None, a, b, g.RTG_HO_CLEAR, plan, creep=False)
            s.goto(gt=gt2); s.run_until_settled()
        n += 1
        if rng.random() < 0.3: s.wait(rng.uniform(3, 45))   # 트럭·지시 대기
    for e in s.episodes:
        if e["t_off"] is None: e["t_off"] = round(s.t, 1)
    rows = sensors(s.rows, PROFILES[spec["profile"]], random.Random(spec["seed"] * 7 + 1), s.range)
    return s, rows


def sensors(rows, p, rng, rng_m):
    """계측 잡음 — 정답(y_*)은 건드리지 않는다. 통신이 끊긴 행은 마지막 정상값으로 멈춘다(게이트웨이 값 유지)."""
    gain = rng.gauss(1.0, p["load_gain"]); off = rng.gauss(0.0, p["load_off"]); drift = 0.0
    wgain = rng.gauss(1.0, p["wind_gain"])
    blip_left = 0; out = []; last = None; t_prev = -1
    chat = ("SP_Landed", "SP_Container_Detected", "SP_TwistLock_Locked")
    keep_keys = ("t_ms", "COM_Link_Status") + tuple(Y_FIELDS)
    for r in rows:
        r = dict(r)
        for ax, k in (("GT", "gt"), ("TR", "tr"), ("HO", "ho")):
            r[ax + "_Position"] = round(min(rng_m[k], max(0.0, r[ax + "_Position"] + rng.gauss(0, p["pos"]))), 3)
            r[ax + "_Velocity"] = round(r[ax + "_Velocity"] + rng.gauss(0, p["vel"]), 3)
        drift += rng.gauss(0, p["load_drift"])
        L = r["HO_Load"]
        if L > 0: L = L * gain + off + drift + rng.gauss(0, p["load_noise"] + p["load_rel"] * L)
        else: L = abs(rng.gauss(0, 0.05))
        r["HO_Load"] = round(max(0.0, L), 2)
        r["ENV_Wind_Speed"] = round(max(0.0, r["ENV_Wind_Speed"] * wgain + rng.gauss(0, p["wind_noise"])), 1)
        r["ENV_Wind_Direction"] = round((r["ENV_Wind_Direction"] + rng.gauss(0, p["dir_noise"])) % 360, 1)
        for c in chat:
            if rng.random() < p["chatter"]: r[c] = 1 - r[c]
        if blip_left == 0 and rng.random() < p["blip_per_h"] / 36000.0:
            blip_left = int(rng.uniform(0.2, 2.5) / DT)       # 라벨 없는 순단
        if blip_left > 0: blip_left -= 1; r["COM_Link_Status"] = 0
        if r["COM_Link_Status"] == 0 and last is not None:
            keep = {k: r[k] for k in keep_keys}
            r = dict(last); r.update(keep)
        else:
            last = r
        if rng.random() < p["drop"]: continue                  # 스캔 누락
        t = r["t_ms"] + int(rng.uniform(-p["jitter_ms"], p["jitter_ms"]))
        r["t_ms"] = max(t, t_prev + 1); t_prev = r["t_ms"]
        out.append(r)
    return out


def write_csv_gz(path, rows):
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=FIELDS, lineterminator="\n"); w.writeheader(); w.writerows(rows)
    data = buf.getvalue().encode("utf-8")
    with gzip.GzipFile(path, "wb", mtime=0) as f: f.write(data)   # mtime=0 — 같은 시드면 같은 바이트(해시 고정)
    return hashlib.sha256(data).hexdigest()


def counts(rows):
    c = {"-1": 0, "0": 0, "1": 0, "2": 0}
    for r in rows: c[str(r["y_state"])] += 1
    return c


def worker(spec):
    s, rows = run_spec(spec)
    path = os.path.join(spec["out"], spec["split"], spec["id"] + ".csv.gz")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sha = write_csv_gz(path, rows)
    meta = {k: spec[k] for k in ("id", "split", "kind", "crane", "seed", "profile", "minutes")}
    meta.update(force=spec.get("force", {}), mult=spec.get("mult", 1.0), rows=len(rows), sha256=sha,
                state_rows=counts(rows), episodes=s.episodes, csv=os.path.relpath(path, spec["out"]))
    if spec["split"] == "bench_pool":
        meta["_cases"] = candidates(rows, spec)
    return meta


# ═══════════════ 기준 데이터셋(지표 5·7) ═══════════════
CLIP_MIN, CLIP_MAX = 600, 3000   # 판정 시각 앞 기록 60초~5분(있는 만큼) — 학습 특징의 가장 긴 창이 5분


def summary(state, cause, code, crane, act):   # crane = 크레인 이름(종류명 STS·RTG 도 됨)
    if state == 0: return f"[정상] {crane} — {ACT_KO[act]}"
    ko, action = CAUSES[cause][:2]
    return f"[{STATE_KO[state]}] {crane} — {ko}" + (f" (코드 {code})" if code else "") + f". 조치: {action}"


def candidates(rows, spec):
    """판정 후보 — 같은 상태·원인이 이어진 구간마다 한 건(짧은 이상 사건도 빠지지 않게), 정상은 60초마다.
    상태가 바뀐 지 5초 안이거나 경계 런이면 '어려움'."""
    rng = random.Random(spec["seed"] * 13 + 5)
    key = lambda r: (r["y_state"], r["y_cause"])
    segs, a = [], 0
    for i in range(1, len(rows) + 1):
        if i == len(rows) or key(rows[i]) != key(rows[a]): segs.append((a, i - 1)); a = i
    out = []
    def add(j, a):
        r = rows[j]
        out.append({"i": j, "state": r["y_state"], "cause": r["y_cause"], "code": r["y_code"], "act": r["y_activity"],
                    "hard": spec["kind"] == "boundary" or (j - a) <= 50})
    for a, b in segs:
        lo = max(a, CLIP_MIN)
        if rows[a]["y_state"] < 0 or b < lo: continue
        if rows[a]["y_state"] > 0:
            add(rng.randint(lo, b), a)
        else:
            if a > 0 and lo <= a + 50 <= b: add(rng.randint(lo, min(b, a + 50)), a)   # 사건이 끝난 직후의 정상
            for j in range(lo + 300, b + 1, 600): add(j, a)
    return out


def interleave(xs):
    """원인(정상은 동작)별로 돌아가며 하나씩 — 기준셋이 오래 가는 원인 하나에 쏠리지 않게."""
    buckets = {}
    for x in xs: buckets.setdefault(x[1]["cause"] or x[1]["act"], []).append(x)
    keys, out = sorted(buckets), []
    while any(buckets.values()):
        for k in keys:
            if buckets[k]: out.append(buckets[k].pop(0))
    return out


def build_bench(out, metas):
    """후보 풀에서 계층 추출해 클립 파일·cases.jsonl 을 쓴다. 어려운 사례를 클래스마다 최소 30% 넣는다."""
    rng = random.Random(20261013)
    pool = {}
    for m in metas:
        for c in m.pop("_cases", []):
            pool.setdefault((m["profile"], c["state"]), []).append((m, c))
    for k in sorted(pool): rng.shuffle(pool[k])
    sets = {"kpi5": [("A", 0, 34), ("A", 1, 33), ("A", 2, 33)],
            "kpi7": [("A", 2, 50), ("A", 1, 25), ("A", 0, 25)],
            "ext":  [(p, st, 100) for p in ("A", "B") for st in (0, 1, 2)]}
    used, manifest, cache = set(), {}, {}
    for name, quota in sets.items():
        cases = []
        for prof, st, n in quota:
            cand = [x for x in pool.get((prof, st), []) if (x[0]["id"], x[1]["i"]) not in used]
            hard = interleave([x for x in cand if x[1]["hard"]]); easy = interleave([x for x in cand if not x[1]["hard"]])
            nh = min(len(hard), max(int(n * 0.3), n - len(easy)))
            pick = hard[:nh] + easy[:n - nh]
            if len(pick) < n: raise SystemExit(f"[gen_ai] 기준셋 {name} {STATE_KO[st]}/{prof} 후보 부족 {len(pick)}/{n}")
            for m, c in pick:
                used.add((m["id"], c["i"])); cases.append((m, c))
        cases.sort(key=lambda x: (x[0]["id"], x[1]["i"]))
        d = os.path.join(out, "bench", name); os.makedirs(os.path.join(d, "clips"), exist_ok=True)
        lines = []
        for k, (m, c) in enumerate(cases, 1):
            if m["id"] not in cache:
                with gzip.open(os.path.join(out, m["csv"]), "rt", encoding="utf-8") as f:
                    cache[m["id"]] = list(csv.DictReader(f))
            clip = cache[m["id"]][max(0, c["i"] - CLIP_MAX + 1): c["i"] + 1]
            cid = f"{name}_{k:03d}"
            sha = write_csv_gz(os.path.join(d, "clips", cid + ".csv.gz"), clip)
            lines.append({"id": cid, "clip": f"clips/{cid}.csv.gz", "sha256": sha, "source": m["id"], "row": c["i"],
                          "crane": m["crane"], "profile": m["profile"], "hard": c["hard"],
                          "state": c["state"], "state_ko": STATE_KO[c["state"]], "cause": c["cause"], "code": c["code"],
                          "activity": c["act"],
                          "summary_ref": summary(c["state"], c["cause"], c["code"], m["crane"], c["act"])})
        with open(os.path.join(d, "cases.jsonl"), "w", encoding="utf-8") as f:
            for x in lines: f.write(json.dumps(x, ensure_ascii=False) + "\n")
        manifest[name] = {"cases": len(lines), "by_state": {STATE_KO[s]: sum(x["state"] == s for x in lines) for s in (0, 1, 2)},
                          "hard": sum(x["hard"] for x in lines),
                          "by_profile": {p: sum(x["profile"] == p for x in lines) for p in ("A", "B")}}
    return manifest


# ═══════════════ 런 목록 ═══════════════
BOUNDARY = {   # 문턱 앞뒤를 훑는 범위
    "wind_peak": (14.0, 29.0), "load_t": (SWL * 0.96, SWL * 1.16), "spike": (1.0, 1.45),
    "pick_delay": (2.0, 8.0), "comm": (0.5, 8.0), "slow": (0.74, 0.96),
}


def plan_specs(out, scale):
    specs = []
    def add(split, kind, crane, seed, minutes, profile="A", mult=1.0, force=None):
        specs.append({"out": out, "split": split, "kind": kind, "crane": crane, "seed": seed, "minutes": minutes,
                      "profile": profile, "mult": mult, "force": force or {}, "id": f"{kind}_{crane}_{seed}"})
    n_shift, n_bd = int(400 * scale), int(1800 * scale)
    for split, base, frac in (("train", 1_000_000, 0.70), ("val", 2_000_000, 0.15), ("test", 3_000_000, 0.15)):
        r = random.Random(base)
        for i in range(max(1, int(n_shift * frac))):
            add(split, "shift", "RTG" if r.random() < 0.2 else "STS", base + i, round(r.uniform(20, 45), 1),
                profile="A" if r.random() < 0.85 else "B")
        for i in range(max(6, int(n_bd * frac))):
            key = list(BOUNDARY)[i % len(BOUNDARY)]; lo, hi = BOUNDARY[key]
            add(split, "boundary", "RTG" if r.random() < 0.2 else "STS", base + 500_000 + i, round(r.uniform(3, 6), 1),
                profile="A" if r.random() < 0.85 else "B", force={key: round(r.uniform(lo, hi), 3)})
    for prof, base in (("A", 9_000_000), ("B", 9_500_000)):   # 기준셋 풀 — 사건 빈도 3배, 고정 시드
        r = random.Random(base)
        for i in range(80):
            add("bench_pool", "shift", "RTG" if r.random() < 0.2 else "STS", base + i, round(r.uniform(25, 40), 1),
                profile=prof, mult=3.0)
        for i in range(240):
            key = list(BOUNDARY)[i % len(BOUNDARY)]; lo, hi = BOUNDARY[key]
            add("bench_pool", "boundary", "STS", base + 100_000 + i, 4.0, profile=prof, force={key: round(r.uniform(lo, hi), 3)})
    return specs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(D, "ai_data"))
    ap.add_argument("--scale", type=float, default=1.0, help="학습·검증·시험 런 수 배율(기준셋 풀은 고정)")
    ap.add_argument("--jobs", type=int, default=os.cpu_count())
    ap.add_argument("--only", help="이 id 하나만 생성(디버그)")
    args = ap.parse_args()
    specs = plan_specs(args.out, args.scale)
    if args.only:
        m = worker(next(s for s in specs if s["id"] == args.only)); m.pop("_cases", None)
        print(json.dumps({k: m[k] for k in ("id", "rows", "state_rows")}, ensure_ascii=False)); return
    os.makedirs(args.out, exist_ok=True)
    with Pool(args.jobs) as pool:
        metas = []
        for k, m in enumerate(pool.imap_unordered(worker, specs, chunksize=2), 1):
            metas.append(m)
            if k % 200 == 0 or k == len(specs): print(f"[gen_ai] {k}/{len(specs)}", flush=True)
    metas.sort(key=lambda m: m["id"])
    bench = build_bench(args.out, metas)
    splits = {}
    for m in metas:
        sp = splits.setdefault(m["split"], {"runs": 0, "rows": 0, "hours": 0.0, "state_rows": {"-1": 0, "0": 0, "1": 0, "2": 0},
                                             "episodes": {}})
        sp["runs"] += 1; sp["rows"] += m["rows"]; sp["hours"] = round(sp["rows"] * DT / 3600, 1)
        for k, v in m["state_rows"].items(): sp["state_rows"][k] += v
        for e in m["episodes"]: sp["episodes"][e["cause"]] = sp["episodes"].get(e["cause"], 0) + 1
    manifest = {"generated_by": "PlcSim/gen_ai.py", "dt_ms": int(DT * 1000), "scale": args.scale,
                "label_rule": "y_state: 코드북 sev 0 정상·1 주의·≥2 이상, 코드 없는 전조는 주의, 정비 -1(제외)",
                "thresholds": {"SWL_t": SWL, "load_warn": LOAD_WARN, "load_trip": LOAD_TRIP, "wind_warn": WIND_WARN,
                               "wind_stop": WIND_STOP, "wind_clear": WIND_CLEAR, "snag_ratio": SNAG_RATIO,
                               "spike_ratio": SPIKE_RATIO, "slow_warn": SLOW_WARN, "slow_trip": SLOW_TRIP,
                               "lock_alarm_s": LOCK_ALARM_S, "comm_alarm_s": COMM_ALARM_S},
                "profiles": PROFILES,
                "causes": {c: {"ko": v[0], "action": v[1], "code": v[2], "state": CSTATE[c]} for c, v in CAUSES.items()},
                "splits": splits, "bench": bench, "runs": metas}
    with open(os.path.join(args.out, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    for k, v in splits.items():
        tot = sum(v["state_rows"][s] for s in ("0", "1", "2")) or 1
        print(f"[gen_ai] {k:10s} 런 {v['runs']:5d} · {v['hours']:7.1f}시간 · 정상 {v['state_rows']['0'] / tot:5.1%} "
              f"주의 {v['state_rows']['1'] / tot:5.1%} 이상 {v['state_rows']['2'] / tot:5.1%}")
    for k, v in bench.items(): print(f"[gen_ai] 기준셋 {k}: {v}")


if __name__ == "__main__":
    main()
