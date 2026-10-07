#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""라이브 가상 PLC — 씬 배치(live_layout.json)대로 크레인마다 DB100/DB101 을 100ms 실시간으로 만들어 통합서버 /ingest 에 넣는다.
정상 작업(STS 양하→적하, RTG 야드 정리) 사이에 풍속·스내그·안착 실패·과전류·비상정지·설비 고장·통신 끊김이 실제 빈도로 섞인다."""

import argparse
import json
import math
import os
import queue
import random
import sys
import threading
import time
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [HERE, os.path.join(os.path.dirname(HERE), "Server")]   # 저장소에선 ../Server, 서버 배포본은 한 폴더
import generate as g                                                   # noqa: E402 — 축 프로파일·태그 행·알람표는 배치 생성기와 같은 원본
from xrcrane_db import auth_headers, clock_offset                      # noqa: E402

FT = {20: (g.SP20, 6058.0), 40: (g.SP40, 12192.0), 45: (g.SP45, 13716.0)}
H_STD = 2.591
g.ALM.setdefault(1012, (3, 1, "GT 충돌방지 센서 작동"))   # 코드북 1012 — 배치 생성기 알람표엔 없다
ANTI_COLL_M = 0.5   # 다리 안쪽면 ↔ 장애물 끝 간격이 이보다 줄면 충돌방지 센서가 선다
SETTLE_S = 400.0   # 축 정착 대기 상한 — 갠트리 140m 를 0.7m/s 로 가도 200초


class Wind:
    """현장 하나의 바람 — 평균 8m/s 근처 저주파 + 드물게 폭풍(20~28m/s 몇 분). 모든 크레인이 같은 값을 읽는다."""

    def __init__(self, rng):
        self.rng, self.v, self.storm_until, self.storm_peak, self.lock = rng, 8.0, 0.0, 0.0, threading.Lock()
        threading.Thread(target=self._run, daemon=True).start()

    def _run(self):
        while True:
            with self.lock:
                now = time.time()
                if now > self.storm_until and self.rng.random() < 1 / 2400:   # 초당 1/2400 ≈ 40분에 한 번 돌풍 장
                    self.storm_until, self.storm_peak = now + self.rng.uniform(120, 300), self.rng.uniform(19, 28)
                base = self.storm_peak if now < self.storm_until else 8.0
                self.v += (base - self.v) * 0.02 + self.rng.gauss(0, 0.25)   # 평균으로 끌리는 랜덤워크(OU)
                self.v = max(0.5, self.v)
            time.sleep(1.0)

    def value(self):
        with self.lock:
            return self.v

    def force(self, peak=27.0, secs=180.0):
        """지시로 강풍 장을 연다 — 모든 크레인이 같은 바람을 맞는다."""
        with self.lock:
            self.storm_until, self.storm_peak = time.time() + secs, peak
            self.v = max(self.v, peak - 4.0)


class Stream(list):
    """Sim.rows 대신 — 행을 큐에 넣고, 큐가 차면 막혀 생성 속도가 송신(100ms)에 묶인다."""

    def __init__(self, q):
        super().__init__()
        self.q = q

    def append(self, row):
        self.q.put(row)


class LiveSim(g.Sim):
    """하중은 지금 매단 컨테이너의 실제 무게, 풍속 평균은 현장 바람을 따른다."""
    cargo_t = 0.0
    wind_src = None

    @property
    def load(self):
        return self.cargo_t if self.carry else 0.0

    @load.setter
    def load(self, v):
        pass

    def _step_env(self):
        if self.wind_src is not None:
            self.wind = self.wind_src.value()
        super()._step_env()


class Box:
    def __init__(self, d):
        self.id, self.t, self.ft, self.h = d["id"], d["t"], d["ft"], d.get("h", H_STD)   # h = 씬 실측 높이(하이큐브 2.896)
        self.o = self.c = self.p = (d["gt"], d["tr"], d["ho_top"])   # 원래·지금·직전 자리(PLC m) — 이름이 겹쳐 자리가 신원이다


class Stack:
    """같은 (갠트리, 트롤리) 자리의 컨테이너 더미 — ground 는 맨 아래 컨테이너 밑면의 권상 값."""

    def __init__(self, gt, tr, ground, cap):
        self.gt, self.tr, self.ground, self.cap, self.boxes = gt, tr, ground, cap, []

    def top(self):
        return self.ground + sum(b.h for b in self.boxes)


class Crane(threading.Thread):
    def __init__(self, spec, args, wind, seed):
        super().__init__(daemon=True, name=spec["name"])
        self.name_, self.kind, self.args = spec["name"], spec["kind"], args
        self.rng = random.Random(seed)
        self.q = queue.Queue(maxsize=2)
        rng_sim = random.Random(seed + 1)
        h = spec["home"]
        self.s = LiveSim(sp_mode=g.SP40, wind=8.0, rng=rng_sim, range_m=spec["range"], gt0=h["gt"], tr0=h["tr"], ho0=h["ho"])
        self.s.rows = Stream(self.q)
        self.s.sigma = {"gt": 0.03, "tr": 0.03, "ho": 0.01}   # 자동 위치결정 + 플리퍼 — 수동 운전 산포(±0.25m)로는 코너캐스팅에 안 물린다
        self.s.wind_src = wind
        self.clear = g.HO_CLEAR if self.kind == "STS" else g.RTG_HO_CLEAR
        self.home = h
        self.comm_down_until = 0.0
        self.stacks = self._stacks(spec["containers"])
        self.boxes = [b for st in self.stacks for b in st.boxes]
        self.cmds = queue.Queue()   # 사고 지시(/command) — 다음 스캔에 그 자리에서 실행
        self.in_cmd = False
        self.wind = wind
        self.obstacle = None   # (주행로 위 더미, 원래 더미) — 갠트리가 다가가면 1012 로 선다
        self.want_obstruct = False
        self.held = None   # 매단 컨테이너의 집기 전 자리 — 운반 중에 뜬 XR 이 스프레더에 매달게
        self.sender = threading.Thread(target=self._send, daemon=True, name=self.name_ + "/send")

    # ── 배치 ──
    def _stacks(self, cs):
        stacks = {}
        for d in sorted(cs, key=lambda d: d["ho_top"]):
            key = (round(d["gt"], 0), round(d["tr"], 0))
            b = Box(d)
            if key not in stacks:
                stacks[key] = Stack(d["gt"], d["tr"], d["ho_top"] - b.h, 2 if self.kind == "STS" else 3)
            stacks[key].boxes.append(b)
        return list(stacks.values())

    def log(self, msg):
        print(f"[live] {time.strftime('%H:%M:%S')} {self.name_}: {msg}", flush=True)

    # ── 시간 진행 ──
    def settle(self, timeout=SETTLE_S):
        s = self.s
        for _ in range(int(timeout / g.DT)):
            self.weather()
            self.anti_collision()
            s._tick()
            if s.gt.settled() and s.tr.settled() and s.ho.settled():
                return

    def wait(self, secs):
        for _ in range(int(secs / g.DT)):
            self.weather()
            self.s._tick()

    # 풍속 규칙(코드북 6001/6002) — 18 이상 감속 70%, 25 이상 작업 중지·안전 위치로 회수 후 16 아래로 1분 내려가면 재개.
    def weather(self):
        if not self.in_cmd and not self.cmds.empty():
            self.run_command(self.cmds.get())
        s, w = self.s, self.s.wind_inst
        if w >= 25.0 and s.op_mode != g.MAINT:
            s.wind_alarm = True; s.event(6002); self.log(f"풍속 {w:.1f}m/s — 운전 중지(6002), 스프레더 회수")
            s.goto(ho=self.clear)
            for _ in range(int(120 / g.DT)):
                s._tick()
                if s.ho.settled(): break
            s.op_mode = g.MAINT; s.event(5015, kind="event")
            calm = 0.0
            while calm < 60.0:
                s._tick(); calm = calm + g.DT if s.wind_inst < 16.0 else 0.0
            s.wind_alarm = False; s.vmul = 1.0; s.clear_alarm(); s.op_mode = g.AUTO; self.log("풍속 회복 — 작업 재개")
        elif w >= 18.0 and not s.wind_alarm:
            s.wind_alarm = True; s.vmul = 0.7; s.event(6001); self.log(f"풍속 {w:.1f}m/s — 주의(6001), 70% 감속")
        elif s.wind_alarm and w < 16.0 and s.alm_code in (6001, 0):
            s.wind_alarm = False; s.vmul = 1.0; s.clear_alarm()

    # 지시 사고 — 동작 도중이어도 그 스캔에 낸다. 끝나면 하던 동작의 목표를 되살려 이어서 간다(급정지가 목표를 현재 위치로 덮는다).
    def run_command(self, cmd):
        s, do = self.s, cmd.get("do", "")
        saved = (s.gt.target, s.tr.target, s.ho.target)
        self.in_cmd = True
        try:
            self.log(f"지시 사고 — {do}")
            if do == "estop": self.estop(hold=True)
            elif do == "release": self.log("해제할 비상정지가 없다")
            elif do == "obstruct":
                if self.kind != "STS": self.log("장애물 배치는 STS 만 — 다리 사이 주행 레일 위")
                elif self.obstacle: self.log("이미 주행로에 장애물이 있다")
                else: self.want_obstruct = True; self.log("다음 동작 전에 컨테이너를 갠트리 주행로에 놓는다")
            elif do == "clear": self.remove_obstacle()
            elif do == "motor": self.motor_fault()
            elif do == "comm": self.comm_blip()
            elif do == "wind": self.wind.force(float(cmd.get("peak", 27)), float(cmd.get("secs", 180)))
            elif do == "snag":
                s.ho_snag = True; s.event(3013); s.arrest(2.0); self.wait(3.0)
                s.ho_snag = False; s.clear_alarm(); self.wait(1.0)
            elif do == "overcurrent":
                s.event(3001); self.wait(5.0); s.clear_alarm()
            elif do == "overload":
                s.ho_overload = True; s.event(3012); s.arrest(2.0); self.wait(10.0)
                s.ho_overload = False; s.clear_alarm(); self.wait(1.0)
            elif do == "landing":
                s.mismatch = True; s.event(4003); self.wait(5.0); s.mismatch = False; s.clear_alarm()
            else:
                self.log(f"모르는 지시 '{do}' — estop·release·snag·motor·overcurrent·overload·landing·comm·wind·obstruct·clear")
        finally:
            s.gt.target, s.tr.target, s.ho.target = saved
            self.in_cmd = False

    # 지시 장애물 — 배에서 컨테이너 하나를 집어 육측 주행 레일 위(지금 갠트리 자리, 앞뒤 다리 사이)에 놓는다.
    def place_obstacle(self):
        here = self.s.gt.pos
        src = min((x for x in self.stacks if x.boxes and x.tr > 40), key=lambda x: abs(x.gt - here))
        rail = Stack(here, g.sts_tr(g.RAIL_LAND_X), g.HO_GROUND - H_STD, 1)
        self.move(src, rail, "장애물 배치(갠트리 주행 레일 위)")
        self.obstacle = (rail, src)

    # 갠트리 충돌방지 센서 — 다리가 장애물 끝으로 ANTI_COLL_M 안까지 오면 비상제동·1012, 'clear'(지상 반출)까지 정지.
    def anti_collision(self):
        if self.obstacle is None or self.in_cmd or abs(self.s.gt.vel) < 1e-3:
            return
        s, (rail, _) = self.s, self.obstacle
        clearance = g.LEG_Z - g.LEG_FOOT / 2 - g.CONT_L / 2 - abs(s.gt.pos - rail.gt)
        if clearance > ANTI_COLL_M:
            return
        saved = (s.gt.target, s.tr.target, s.ho.target)
        self.in_cmd = True
        s.event(1012); self.log(f"갠트리 충돌방지(1012) — 다리↔장애물 {clearance:.2f}m, 'clear' 지시까지 정지")
        s.arrest(estop=False)
        while self.obstacle is not None:
            s._tick()
            if not self.cmds.empty() and self.cmds.get().get("do") == "clear":
                self.remove_obstacle()
        s.gt.target, s.tr.target, s.ho.target = saved
        self.in_cmd = False

    # 지상 장비가 주행로 컨테이너를 원래 배 자리로 되돌렸다고 본다 — 화물 상태만 고쳐 올리면 XR 이 그 자리로 옮긴다.
    def remove_obstacle(self):
        if self.obstacle is None:
            self.log("치울 장애물이 없다"); return
        rail, src = self.obstacle
        b = rail.boxes.pop()
        b.p, b.c = b.c, (src.gt, src.tr, src.top() + b.h)
        src.boxes.append(b)
        self.obstacle = None
        self.s.clear_alarm()
        self.post_cargo()
        self.log("주행로 장애물 반출 — 1012 해제, 작업 재개")

    def _poll_commands(self):
        url = f"{self.args.url.rstrip('/')}/command?crane={urllib.parse.quote(self.name_)}"
        while True:
            try:
                req = urllib.request.Request(url, headers=auth_headers({}))
                for c in json.loads(urllib.request.urlopen(req, timeout=3).read()).get("commands", []):
                    self.cmds.put(c)
            except Exception:
                pass
            time.sleep(1.0)

    # ── 사고 ──
    def chance(self, p):
        return self.rng.random() < p * self.args.incidents

    def estop(self, hold=False):
        s = self.s
        s.event(5001); self.log("비상정지(5001)" + (" — 'release' 지시까지 유지" if hold else ""))
        s.arrest(estop=True)
        if hold:   # 지시로 건 비상정지 — 사람이 리셋할 때까지 그대로(실제 운전실 버튼과 같다)
            while True:
                s._tick()
                if not self.cmds.empty() and self.cmds.get().get("do") == "release": break
        else:
            self.wait(self.rng.uniform(8, 20))
        s.estop = False; s.clear_alarm(); s.ready = True; self.wait(1.0); self.log("비상정지 해제")

    def motor_fault(self):
        s = self.s
        s.ho_motor_alarm = True; s.event(3002); self.log("권상 모터 권선 온도 초과(3002) — 정비 전환")
        s.arrest(2.0); self.wait(3.0)
        s.op_mode = g.MAINT; s.event(5015, kind="event"); self.wait(2.0)
        s.locked_out = True; s.event(5005); self.wait(self.rng.uniform(60, 180))
        s.locked_out = False; s.ho_motor_alarm = False; s.clear_alarm(); s.op_mode = g.MANUAL; self.wait(5.0)
        s.op_mode = g.AUTO; self.log("정비 완료 — 자동 운전 복귀")

    def comm_blip(self):
        secs = self.rng.uniform(3, 8)
        self.comm_down_until = time.time() + secs + 0.5
        self.log(f"통신 끊김 {secs:.0f}초(행 미전송)")

    # ── 한 번 옮기기 — 사고는 이 동작들 사이에 끼어든다 ──
    def move(self, src, dst, label):
        if self.want_obstruct:
            self.want_obstruct = False
            self.place_obstacle()
        s, b = self.s, src.boxes[-1]
        s.op_mode = g.AUTO
        s.goto(ho=self.clear); self.settle()
        if self.chance(0.005): self.comm_blip()
        s.goto(gt=src.gt, tr=src.tr); self.settle()
        if self.chance(0.01): self.estop()
        s.goto(ho=src.top()); self.settle()
        if self.chance(0.03):   # 안착 신호 미검출 — 살짝 들었다 다시 내린다
            s.event(4006); self.log("안착 신호 미검출(4006) — 재안착"); self.wait(2.0)
            s.goto(ho=src.top() + 0.4); self.settle(); s.clear_alarm(); s.goto(ho=src.top()); self.settle()
        s.sp_mode, s.tele_mm = FT.get(b.ft, FT[40])
        s.cargo_t = b.t
        s.detected = True; s.landed = True; self.wait(1.0)
        s.locked = True; s.carry = True; self.wait(1.5); s.landed = False
        src.boxes.pop()
        self.held = b.c
        self.post_cargo()
        s.goto(ho=self.clear)
        if self.chance(0.03):   # 스내그 — 권상 중 걸림(S07)
            self.wait(2.5); s.ho_snag = True; s.event(3013); self.log("스내그(3013) — 권상 보호정지")
            s.arrest(2.0); self.wait(2.0); s.goto(ho=s.ho.pos - 1.0); self.settle()
            s.ho_snag = False; s.clear_alarm(); self.wait(1.0); s.goto(ho=self.clear)
        elif b.t > 28 and self.chance(0.04):   # 무거운 화물 기동 과전류 — 경고만
            self.wait(1.5); s.event(3001); self.log(f"권상 과전류(3001) {b.t:.1f}t"); self.wait(3.0); s.clear_alarm()
        self.settle()
        s.goto(gt=dst.gt, tr=dst.tr); self.settle()
        if self.chance(0.01): self.estop()
        if self.chance(0.005): self.motor_fault()
        s.goto(ho=dst.top() + b.h); self.settle()
        s.landed = True; self.wait(1.0)
        if self.chance(0.05):   # 부분 잠금 해제 — 5~10cm 들어 재정렬(S08)
            s.mismatch = True; s.event(4003); self.log("4코너 부분 잠금(4003) — 재정렬"); self.wait(1.0)
            s.goto(ho=dst.top() + b.h + 0.4); self.settle(); s.mismatch = False; s.clear_alarm()
            s.goto(ho=dst.top() + b.h); self.settle(); self.wait(1.0)
        s.locked = False; s.carry = False; self.wait(1.5); s.landed = False; s.detected = False
        dst.boxes.append(b)
        dst.gt, dst.tr = (s.gt.pos, s.tr.pos) if len(dst.boxes) == 1 else (dst.gt, dst.tr)   # 빈 자리에 놓으면 실제 선 자리가 그 더미의 자리
        b.p, b.c = b.c, (s.gt.pos, s.tr.pos, s.ho.pos)
        self.held = None
        self.post_cargo()
        s.goto(ho=self.clear); self.settle()
        s.cycle += 1
        self.log(f"{label} {b.id} {b.t:.1f}t ({b.ft}ft) · 금일 {s.cycle}회 · 풍속 {s.wind_inst:.1f}")

    # 옮겨진 컨테이너(원래 자리에서 30cm 넘게)만 서버에 — 늦게 뜬 XR 이 씬을 지금 상태로 맞춘다(TOS 재고와 같은 역할).
    def post_cargo(self):
        far = lambda a, b: max(abs(x - y) for x, y in zip(a, b)) > 0.3
        moved = [{"o": [round(v, 3) for v in b.o], "c": [round(v, 3) for v in b.c], "p": [round(v, 3) for v in b.p]}
                 for b in self.boxes if far(b.o, b.c) or far(b.p, b.c)]   # 제자리로 돌아온 것도 직전 자리(p)에서 찾게 남긴다
        try:
            req = urllib.request.Request(f"{self.args.url.rstrip('/')}/cargo?crane={urllib.parse.quote(self.name_)}",
                                         json.dumps({"boxes": moved, "held": [round(v, 3) for v in self.held] if self.held else []}).encode("utf-8"), auth_headers({"Content-Type": "application/json"}))
            urllib.request.urlopen(req, timeout=3).read()
        except Exception as e:
            self.log(f"화물 상태 올리기 실패: {e}")

    # ── 작업 계획 ──
    def sts_job(self):
        """한 베이 양하 3~5개(배→포털 밑 트럭 레인) → 트럭 교대 → 같은 수를 원래 자리로 적하(이중 사이클)."""
        bays = {}
        for st in self.stacks:
            if st.boxes: bays.setdefault(round(st.gt), []).append(st)
        bay = self.rng.choice([k for k, v in bays.items() if sum(len(x.boxes) for x in v) >= 3])
        gt = bays[bay][0].gt
        lanes = [Stack(gt, g.sts_tr(g.lane_x(k)), g.HO_GROUND - H_STD, 1) for k in range(g.LANE_N)]
        n = self.rng.randint(3, 5)
        done = []
        for i in range(n):
            src = sorted((x for x in bays[bay] if x.boxes), key=lambda x: (x.tr, -len(x.boxes)))[0]   # 안벽쪽 열, 윗단 먼저
            self.move(src, lanes[i], f"양하 {i + 1}/{n}")
            done.append((src, lanes[i]))
        self.wait(self.rng.uniform(10, 40))   # 트럭 교대
        for i, (ship, lane) in enumerate(reversed(done)):
            self.move(lane, ship, f"적하 {i + 1}/{n}")

    def rtg_job(self):
        """야드 정리 1회 — 한 더미 맨 위를 가까운 다른 더미(3단 미만)로 옮긴다. 가까운 쪽을 고르는 게 실제 RTG 운전 습관."""
        full = [x for x in self.stacks if x.boxes]
        src = self.rng.choice(full)
        b = src.boxes[-1]
        fits = lambda x: not x.boxes or ((x.boxes[-1].ft == 20) == (b.ft == 20))   # 20ft 위엔 20ft, 40·45ft 위엔 40·45ft — 40ft 를 20ft 하나에 걸치지 않는다
        cands = [x for x in self.stacks if x is not src and len(x.boxes) < x.cap and fits(x)]
        if not cands:
            return
        cands.sort(key=lambda x: abs(x.gt - src.gt) + 0.3 * abs(x.tr - src.tr))
        dst = self.rng.choice(cands[:3])
        self.move(src, dst, "야드 정리")

    def run(self):
        s = self.s
        self.sender.start()
        threading.Thread(target=self._poll_commands, daemon=True, name=self.name_ + "/cmd").start()
        s.ready = False; s.op_mode = g.MANUAL; self.wait(3.0)
        s.event(5016, kind="event"); s.ready = True; s.op_mode = g.AUTO; self.wait(2.0)
        self.post_cargo()   # 기동 = 원래 배치(옮긴 것 0개)
        self.log(f"기동 — {self.kind}, 컨테이너 {sum(len(x.boxes) for x in self.stacks)}개 · 더미 {len(self.stacks)}개")
        while True:
            (self.sts_job if self.kind == "STS" else self.rtg_job)()
            if self.chance(0.02):   # 휴식·교대 — 대기
                s.goto(gt=self.home["gt"], tr=self.home["tr"], ho=self.home["ho"]); self.settle()
                self.log("교대 대기"); self.wait(self.rng.uniform(60, 240))
            else:
                self.wait(self.rng.uniform(3, 15))

    # ── 송신 — PLC 스캔 100ms 마다 한 행. 시각은 서버 시계로 맞춘 epoch ms ──
    def _send(self):
        url = self.args.url.rstrip("/") + "/ingest"
        off, synced, warned = 0, 0.0, False
        nxt = time.monotonic()
        while True:
            row = self.q.get()
            nxt += g.DT
            time.sleep(max(0.0, nxt - time.monotonic()))
            if time.monotonic() - synced > 60:
                try:
                    off, _ = clock_offset(self.args.url)
                except Exception:
                    pass
                synced = time.monotonic()
            if time.time() < self.comm_down_until:
                continue
            ms = int(time.time() * 1000) + off
            row.update(t_ms=ms, plc_ms=ms, crane=self.name_, source=f"live/{self.name_}")
            try:
                req = urllib.request.Request(url, json.dumps(row).encode("utf-8"), auth_headers({"Content-Type": "application/json"}))
                urllib.request.urlopen(req, timeout=2).read()
                warned = False
            except Exception as e:
                if not warned:
                    self.log(f"서버 적재 실패: {e}")
                    warned = True


def main():
    ap = argparse.ArgumentParser(description="라이브 가상 PLC — 씬 배치대로 크레인 데이터를 실시간 생성해 통합서버에 넣는다")
    ap.add_argument("--url", default="http://127.0.0.1:5006")
    ap.add_argument("--layout", default=os.path.join(HERE, "live_layout.json"))
    ap.add_argument("--seed", type=int, default=int(time.time()))
    ap.add_argument("--incidents", type=float, default=1.0, help="사고 빈도 배율(1=실제 빈도, 시연 때 3~5)")
    ap.add_argument("--cranes", default="", help="쉼표 구분 크레인 이름(비우면 전부)")
    ap.add_argument("--minutes", type=float, default=0, help="이 시간 뒤 종료(0=무한)")
    args = ap.parse_args()

    with open(args.layout, encoding="utf-8") as f:
        layout = json.load(f)
    want = {x.strip() for x in args.cranes.split(",") if x.strip()}
    wind = Wind(random.Random(args.seed))
    cranes = [Crane(c, args, wind, args.seed + 10 * i) for i, c in enumerate(layout["cranes"]) if not want or c["name"] in want]
    print(f"[live] 라이브 PLC — 크레인 {len(cranes)}대 → {args.url} · 사고 배율 {args.incidents:g} · 시드 {args.seed}", flush=True)
    for c in cranes:
        c.start()
    end = time.time() + args.minutes * 60 if args.minutes > 0 else math.inf
    while time.time() < end:
        time.sleep(1)


if __name__ == "__main__":
    main()
