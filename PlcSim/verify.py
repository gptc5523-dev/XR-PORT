#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
생성 데이터 자체검사 — generate.py 를 돌린 뒤 실행한다. 실패하면 종료코드 1.

무엇을 왜 보는가 (전부 실제로 한 번씩 깨졌던 항목이다):
  1) 위치 ≤ 가동범위   : 넘으면 Unity(PlcBridge)가 realPos/rangeM 을 클램프해 축이 끝에 붙는다.
  2) 겉보기 가속 < 트립: CraneOpMode 가 위치를 2차 미분해 가속알람(1021/2021/3021)을 낸다.
                         판정은 CraneOpMode 와 같은 규칙 — 한계 초과가 AccelTripSetN(=3)
                         '연속' 이어야 트립이다. 1틱 스파이크는 디바운스가 흡수하므로
                         최대값이 아니라 '연속 초과 길이'를 본다. 방향 전환(감속 amax →
                         반대로 가속 amax)은 위치 2차차분이 한 틱만 2·amax 로 잡히는데,
                         이건 정상 운전이고 실제로 트립되지 않는다.
                         급정지 이벤트(E-Stop·스내그·모터고장)가 있는 런은 급감속이
                         설계된 동작이라 제외한다 — 라벨이 아니라 '그 런에 실제로 급정지
                         이벤트가 있었는가'로 판정한다(주의 라벨에도 스내그가 들어있다).
  3) 런별 상이         : 같은 시나리오 N 회가 같은 파일이면 100회 반복시험이 1회와 같다.
  4) 적하·양하 방향     : S13(육지→배)·S14(배→육지)가 뒤집히면 데이터가 통째로 거짓이다.
                         양하의 단 순서(위→아래)도 본다 — 아래부터 내리면 실물은 무너진다.
  5) 헤더 계약         : CsvReplaySource.ParseCsv 가 이름으로 찾는 컬럼이 전부 있어야 한다.
"""
import csv, glob, json, os, sys, collections

D = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(D, "output")
sys.path.insert(0, D)
from generate import RANGE, AMAX                                  # noqa: E402

TRIP = {k: AMAX[k] * 1.667 for k in AMAX}      # CraneAxisProfile.TripMargin
SET_N = 3                                      # CraneAxisProfile.AccelTripSetN
# 급정지 이벤트 — 이 코드가 있는 런은 정격 초과 감속이 설계된 동작이라 가속 검사에서 뺀다.
ARREST = {5001, 3013, 3002, 3001, 3005}
COL = {"gt": "GT_Position", "tr": "TR_Position", "ho": "HO_Position"}
# CsvReplaySource.ParseCsv 가 이름으로 찾는 컬럼(누락 시 조용히 0 이 된다)
NEEDED = ["t_ms", "GT_Position", "GT_Velocity", "GT_Running", "GT_Direction",
          "TR_Position", "TR_Velocity", "TR_Running", "TR_Direction",
          "HO_Position", "HO_Velocity", "HO_Running", "HO_Direction", "HO_Load",
          "SP_Mode", "SP_TwistLock_Locked", "SP_TwistLock_Unlocked", "SP_Landed",
          "SP_Container_Detected", "OP_Mode", "OP_Ready", "OP_Running", "OP_Standby",
          "OP_Emergency_Stop", "ENV_Wind_Speed", "ENV_Wind_Direction", "ENV_Wind_Alarm",
          "ALM_Active", "ALM_Latest_Code", "ALM_Latest_Severity", "ALM_Latest_Source",
          "COM_Link_Status"]

man = json.load(open(os.path.join(OUT, "manifest.json"), encoding="utf-8"))
label = {r["scenario"]: r["label"] for r in man["runs"]}
fails, exempt, dt = [], [], man["dt_ms"] / 1000.0

acc = collections.defaultdict(lambda: {k: 0.0 for k in COL})     # 최대 겉보기 가속(참고용)
run = collections.defaultdict(lambda: {k: 0 for k in COL})       # 한계 초과 최대 연속 길이(판정용)
pos = collections.defaultdict(lambda: {k: 0.0 for k in COL})
digests = collections.defaultdict(set)

for f in sorted(glob.glob(os.path.join(OUT, "*", "*.csv"))):
    sid = os.path.basename(os.path.dirname(f)); L = label[sid]
    rows = list(csv.DictReader(open(f, encoding="utf-8")))
    miss = [c for c in NEEDED if c not in rows[0]]
    if miss: fails.append(f"[헤더] {sid} 누락 {miss}")
    digests[sid].add(hash(tuple(r["HO_Position"] + r["TR_Position"] for r in rows)))
    ev = json.load(open(f[:-4] + ".events.json", encoding="utf-8"))
    arrested = any(e["code"] in ARREST for e in ev["events"])
    if arrested: exempt.append(os.path.relpath(f, OUT))
    # 가속은 위치의 2차 미분이라 연속 3 샘플이 있어야 한다. 앞 두 샘플은 이전 속도를
    #   모르므로(0 으로 가정하면 시작 가속이 2배로 잡히는 가짜 스파이크가 난다) 건너뛴다.
    prev = {k: None for k in COL}; pv = {k: None for k in COL}; streak = {k: 0 for k in COL}
    for r in rows:
        for k, c in COL.items():
            v = float(r[c]); pos[L][k] = max(pos[L][k], v)
            if prev[k] is not None:
                vel = (v - prev[k]) / dt
                if pv[k] is not None and not arrested:
                    a = abs((vel - pv[k]) / dt)
                    acc[L][k] = max(acc[L][k], a)
                    streak[k] = streak[k] + 1 if a >= TRIP[k] else 0
                    run[L][k] = max(run[L][k], streak[k])
                pv[k] = vel
            prev[k] = v

print("=== 1) 위치 최대 vs 가동범위 (초과 = Unity 클램프) ===")
for L in sorted(pos):
    over = [k.upper() for k in COL if pos[L][k] > RANGE[k] + 1e-6]
    print(f"  {L:9s}" + "  ".join(f"{k.upper()} {pos[L][k]:7.2f}/{RANGE[k]:6.1f}" for k in COL)
          + ("   ← 초과 " + ",".join(over) if over else "   OK"))
    if over: fails.append(f"[클램프] {L}: {over}")

print(f"\n=== 2) 가속 트립 (초과 연속 {SET_N}틱 이상이면 알람 · 급정지 런 {len(exempt)}개 제외) ===")
print(f"  {'라벨':9s}" + "  ".join(f"{k.upper()} 최대/한계 연속" for k in COL))
for L in sorted(acc):
    ex = [k.upper() for k in COL if run[L][k] >= SET_N]
    print(f"  {L:9s}" + "  ".join(f"{acc[L][k]:5.2f}/{TRIP[k]:4.2f} {run[L][k]:>2}틱" for k in COL)
          + ("   ← 오경보 " + ",".join(ex) if ex else "   OK"))
    if ex: fails.append(f"[오경보] {L}: {ex}")

print("\n=== 3) 적하·양하 방향과 단 순서 ===")
# S13 적하(육지→배) · S14 양하(배→육지)가 뒤집히면 데이터가 통째로 거짓이 된다.
#   특히 양하의 '단 순서' — 아래 단부터 내리면 실물에선 위 컨테이너가 무너진다.
for sid, pick_ship in (("S13", False), ("S14", True)):
    f = os.path.join(OUT, sid, "run_01.csv")
    if not os.path.exists(f):
        print(f"  {sid} 없음 — 건너뜀"); continue
    rows = list(csv.DictReader(open(f, encoding="utf-8")))
    picks, places, prev = [], [], None
    for r in rows:
        lk = r["SP_TwistLock_Locked"]
        if prev is not None and lk != prev:
            (picks if lk == "1" else places).append(
                (float(r["TR_Position"]), float(r["HO_Position"])))
        prev = lk
    if not picks or not places:
        fails.append(f"[방향] {sid}: 잠금/해제 전이 없음"); continue
    # 배쪽 = 안벽(TR_QUAY 33.9)보다 바다쪽, 육지쪽 = 육지쪽 다리(15.9)보다 안쪽
    pick_side  = all(tr > 33.9 for tr, _ in picks)  if pick_ship else all(tr < 15.9 for tr, _ in picks)
    place_side = all(tr < 15.9 for tr, _ in places) if pick_ship else all(tr > 33.9 for tr, _ in places)
    tiers = [ho for _, ho in picks] if pick_ship else [ho for _, ho in places]
    tier_ok = tiers[0] >= tiers[-1] if pick_ship else tiers[0] <= tiers[-1]   # 양하 위→아래 / 적하 아래→위
    d = "배→육지" if pick_ship else "육지→배"
    print(f"  {sid} {d}  집는곳 {'배' if pick_ship else '육지'}={'OK' if pick_side else 'NG'}"
          f"  놓는곳 {'육지' if pick_ship else '배'}={'OK' if place_side else 'NG'}"
          f"  단 {tiers[0]:.1f}→{tiers[-1]:.1f}m {'OK' if tier_ok else 'NG'}  ({len(picks)}개)")
    if not pick_side:  fails.append(f"[방향] {sid}: 집는 위치가 반대")
    if not place_side: fails.append(f"[방향] {sid}: 놓는 위치가 반대")
    if not tier_ok:    fails.append(f"[단순서] {sid}: {'양하는 위 단부터' if pick_ship else '적하는 아래 단부터'}")

print("\n=== 4) 같은 시나리오의 런이 서로 다른가 ===")
dup = [s for s, d in digests.items() if len(d) == 1 and
       sum(1 for r in man["runs"] if r["scenario"] == s) > 1]
print(f"  시나리오 {len(digests)}종 · 총 {man['total_runs']}런 · " +
      ("전부 상이 OK" if not dup else f"중복 {dup}"))
if dup: fails.append(f"[중복] {dup}")

print("\n=== 5) 요약 ===")
print(f"  {man['total_rows']:,} 행 · {man['total_rows']*dt/60:.1f} 분 · 라벨 {man['label_distribution']}")
print(f"  range_m={man['range_m']}  vmax={man['vmax_ms']}  amax={man['amax_ms2']}")

if fails:
    print("\n실패 %d 건:" % len(fails)); [print("  -", x) for x in fails]; sys.exit(1)
print("\n전 항목 통과.")
