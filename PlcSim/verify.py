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
  6) 작업 이력         : 이력 한 줄이 PLC 시계열에서 실제로 그 자리였는가(집기/놓기 시각의 축 위치),
                         그리고 적치 규칙 — 위에 얹힌 걸 빼거나 허공에 놓는 이력은 실물에서 불가능하다.
                         run_until_settled 타임아웃은 조용히 넘어가서, 가는 도중에 집은 이력이 남을 수 있다.
                         배 자리는 씬의 실제 점유(2단 랜덤 포함)에서 출발한다.
  7) Port.unity 대조   : 생성기 '씬 기하' 식(배·크레인 위치, 갠트리 범위, 스프레더 오프셋, 갑판 컨테이너 전량)이
                         실제 씬과 같은가. 오너 지적 2026-09-14 "허공에 작업" — 좌표가 씬과 갈라지면 여기서 멈춘다.
"""
import csv, glob, json, os, sys, collections

D = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(D, "output")
sys.path.insert(0, D)
import generate as G                                                         # noqa: E402
from generate import RANGE, AMAX, RANGES, CONT_H, rtg_gt, rtg_tr, iso6346  # noqa: E402

assert iso6346("CSQU", 305438) == "CSQU3054383"   # ISO 6346 표준 예시 번호

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
crane = {r["scenario"]: r.get("crane", "STS") for r in man["runs"]}
fails, exempt, dt = [], [], man["dt_ms"] / 1000.0

acc = collections.defaultdict(lambda: {k: 0.0 for k in COL})     # 최대 겉보기 가속(참고용)
run = collections.defaultdict(lambda: {k: 0 for k in COL})       # 한계 초과 최대 연속 길이(판정용)
pos = collections.defaultdict(lambda: {k: 0.0 for k in COL})
digests = collections.defaultdict(set)

for f in sorted(glob.glob(os.path.join(OUT, "*", "*.csv"))):
    if f.endswith(".history.csv"): continue                     # 작업 이력은 PLC 시계열이 아니다(6번에서 본다)
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
            v = float(r[c]); pos[L, crane[sid]][k] = max(pos[L, crane[sid]][k], v)
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
for L, c in sorted(pos):
    R, P = RANGES[c], pos[L, c]
    over = [k.upper() for k in COL if P[k] > R[k] + 1e-6]
    print(f"  {L:9s}{c}  " + "  ".join(f"{k.upper()} {P[k]:7.2f}/{R[k]:6.1f}" for k in COL)
          + ("   ← 초과 " + ",".join(over) if over else "   OK"))
    if over: fails.append(f"[클램프] {L} {c}: {over}")

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
for sid, pick_ship in (("S13", False), ("S14", True), ("S15", True)):
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
    # 배쪽 = 안벽 가장자리(x=0)보다 바다쪽, 육지쪽 = 그보다 안쪽(포털 밑 레인) — 경계도 '씬 기하' 식
    edge = G.sts_tr(0.0)
    pick_side  = all(tr > edge for tr, _ in picks)  if pick_ship else all(tr < edge for tr, _ in picks)
    place_side = all(tr < edge for tr, _ in places) if pick_ship else all(tr > edge for tr, _ in places)
    tiers = [ho for _, ho in picks] if pick_ship else [ho for _, ho in places]
    tier_ok = tiers[0] >= tiers[-1] if pick_ship else tiers[0] <= tiers[-1]   # 양하 위→아래 / 적하 아래→위
    d = "배→육지" if pick_ship else "육지→배"
    print(f"  {sid} {d}  집는곳 {'배' if pick_ship else '육지'}={'OK' if pick_side else 'NG'}"
          f"  놓는곳 {'육지' if pick_ship else '배'}={'OK' if place_side else 'NG'}"
          f"  단 {tiers[0]:.1f}→{tiers[-1]:.1f}m {'OK' if tier_ok else 'NG'}  ({len(picks)}개)")
    if not pick_side:  fails.append(f"[방향] {sid}: 집는 위치가 반대")
    if not place_side: fails.append(f"[방향] {sid}: 놓는 위치가 반대")
    if not tier_ok:    fails.append(f"[단순서] {sid}: {'양하는 위 단부터' if pick_ship else '적하는 아래 단부터'}")

print("\n=== 4) 작업 이력 ↔ PLC 시계열 · 적치 규칙 ===")
# 허용오차 = 목표 산포 σ 의 4배 + 정착 판정 0.05 — STS 연속 이송은 자동 위치결정(AUTO_SIGMA), RTG 는 수동(AIM_SIGMA)
TOL = {c: {f"{a.upper()}_Position": 4 * sig[a] + 0.05 for a in sig}
       for c, sig in (("STS", G.AUTO_SIGMA), ("RTG", G.AIM_SIGMA))}
COUNT = {"S13": 20, "S14": 20, "S15": 5, "S16": 5}

def expect(loc):
    """이력 위치 → 그 자리에서 PLC 축이 있어야 할 값(generate.py '씬 기하' 식)."""
    area = loc.split("/")[0]
    if area in ("QUAY", "TRUCK"):                       # 포털 밑 레인 — 데크에 내려놓는다(갠트리는 그 베이 그대로)
        k = int(loc.rsplit("/L", 1)[1]) - 1
        return {"TR_Position": G.sts_tr(G.lane_x(k)), "HO_Position": G.HO_GROUND}
    if area == "SHIP":
        k = G.slot_key(loc)
        return {"GT_Position": G.sts_gt(G.slot_z(k)), "TR_Position": G.sts_tr(G.slot_x(k)),
                "HO_Position": G.sts_ho(G.slot_top(k))}
    _, b, r, t = loc.split("/"); b, r, t = int(b[1:]) - 1, int(r[1:]) - 1, int(t[1:])
    return {"GT_Position": rtg_gt(b), "TR_Position": rtg_tr(r), "HO_Position": t * CONT_H}

def tier(loc, d):
    base, t = loc.rsplit("/T", 1); return f"{base}/T{int(t) + d}"

for f in sorted(glob.glob(os.path.join(OUT, "*", "*.history.csv"))):
    sid, run_name = os.path.basename(os.path.dirname(f)), os.path.basename(f)[:6]
    mv = list(csv.DictReader(open(f, encoding="utf-8")))
    rows = {r["t_ms"]: r for r in csv.DictReader(open(f.replace(".history.csv", ".csv"), encoding="utf-8"))}
    bad = []
    if sid in COUNT and len(mv) != COUNT[sid]: bad.append(f"{len(mv)}개 ≠ {COUNT[sid]}")
    for m in mv:
        for loc, t in ((m["from"], m["pick_t_ms"]), (m["to"], m["place_t_ms"])):
            for col, v in expect(loc).items():
                if abs(float(rows[t][col]) - v) > TOL[crane[sid]][col]:
                    bad.append(f"#{m['seq']} {loc} {col} {float(rows[t][col]):.2f}≠{v:.2f}")
        if int(m["place_t_ms"]) <= int(m["pick_t_ms"]): bad.append(f"#{m['seq']} 놓기가 집기보다 먼저")
    # 적치 — 처음부터 있던 슬롯(출발지 중 한 번도 도착지가 아닌 곳)에서 시작해 순서대로 옮겨 본다.
    slot = lambda loc: "/T" in loc
    occ = {m["from"] for m in mv if slot(m["from"])} - {m["to"] for m in mv}
    if any(m["from"].startswith("SHIP/") or m["to"].startswith("SHIP/") for m in mv):
        occ |= {G.slot_label(k) for k in G.SHIP_OCC}          # 배는 씬의 실제 점유(2단 랜덤 포함)에서 출발
    quay = [m["to"] for m in mv if m["to"].startswith("QUAY/")]
    if len(quay) != len(set(quay)): bad.append("같은 에이프런 레인에 두 번 놓음")
    for m in mv:
        a, b = m["from"], m["to"]
        if slot(a) and (a not in occ or tier(a, +1) in occ): bad.append(f"#{m['seq']} {a} 위가 막힘/없음")
        occ.discard(a)
        if slot(b) and (b in occ or not (b.endswith("/T1") or tier(b, -1) in occ)): bad.append(f"#{m['seq']} {b} 허공/중복")
        occ.add(b)
    if run_name == "run_01" or bad:
        print(f"  {sid}/{run_name} {crane[sid]} {len(mv)}개  " + ("OK" if not bad else "NG " + "; ".join(bad[:3])))
    if bad: fails.append(f"[이력] {sid}/{run_name}: {bad[:3]}")

print("\n=== 5) 같은 시나리오의 런이 서로 다른가 ===")
dup = [s for s, d in digests.items() if len(d) == 1 and
       sum(1 for r in man["runs"] if r["scenario"] == s) > 1]
print(f"  시나리오 {len(digests)}종 · 총 {man['total_runs']}런 · " +
      ("전부 상이 OK" if not dup else f"중복 {dup}"))
if dup: fails.append(f"[중복] {dup}")

print("\n=== 6) Port.unity 대조 — 생성기 '씬 기하' 식이 실제 씬과 같은가 ===")
SCENE = os.path.join(D, "..", "Assets", "Scenes", "Port.unity")
if not os.path.exists(SCENE):
    print("  Port.unity 없음 — 건너뜀")
else:
    import re
    txt = open(SCENE, encoding="utf-8").read()
    blocks = {m.group(2): (m.group(1), m.group(3))
              for m in re.finditer(r'--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=\n--- !u!|\Z)', txt, re.S)}
    names = {f: n.group(1) for f, (t, b) in blocks.items() if t == "1" and (n := re.search(r'm_Name: (.*)', b))}
    pos = {}
    for f, (t, b) in blocks.items():
        g = re.search(r'm_GameObject: \{fileID: (-?\d+)', b)
        p = re.search(r'm_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}', b)
        if t == "4" and g and p: pos.setdefault(names.get(g.group(1)), []).append(tuple(float(v) * 24 for v in p.groups()))

    def chk(label, got, want, tol):
        ok = got is not None and all(abs(a - b) <= tol for a, b in zip(got, want))
        print(f"  {label:22s} 씬 {tuple(round(v, 3) for v in got) if got else None} · 식 {tuple(round(v, 3) for v in want)}  "
              + ("OK" if ok else "NG"))
        if not ok: fails.append(f"[씬] {label}")

    first = lambda n: (pos.get(n) or [None])[0]
    chk("ContainerShip", first("ContainerShip"), (G.SHIP_X, G.SHIP_Y, G.SHIP_Z), 1e-3)
    chk("STS_Crane", first("STS_Crane"), (G.STS_ROOT_X, 0.0, G.STS_ROOT_Z), 1e-3)
    chk("Boom_1", first("Boom_1"), (0.0, G.BOOM_Y, 0.0), 1e-3)
    chk("AttachPoint_1", first("AttachPoint_1"), (0.0, -G.ATTACH_DROP, 0.0), 1e-3)
    sp = first("Spreader_1")
    chk("Spreader_1 X(HoistX)", sp and sp[:1], (G.HOIST_X,), 1e-3)
    gg = re.search(r'guid: (\w+)', open(os.path.join(D, "..", "Assets/Crane/Scripts/Sts/Runtime/GantryMover.cs.meta")).read()).group(1)
    gm = next((b for f, (t, b) in blocks.items() if t == "114" and gg in b
               and names.get(re.search(r'm_GameObject: \{fileID: (-?\d+)', b).group(1)) == "STS_Crane"), None)
    if gm is None: fails.append("[씬] STS GantryMover 없음")
    else:
        chk("STS 갠트리 min/max", tuple(float(re.search(rf'\n  {k}: (\S+)', gm).group(1)) * 24 for k in ("min", "max")),
            (G.GT_MIN_Z, G.GT_MIN_Z + G.STS_RANGE["gt"]), 0.01)
    # 1:1 짝짓기 — 씬 좌표는 float32 라 반올림·정렬로 비교하면 경계값에서 순서가 흔들린다.
    #   식의 자리마다 1mm 안의 씬 컨테이너를 하나씩 소거하고, 남는 쪽이 없어야 일치다.
    scene_c, formula = pos.get("ShipContainer", []), G.SHIP_OCC
    left, miss = list(scene_c), 0
    for k in formula:
        want = (G.SHIP_SLOT[k]["x"], G.SHIP_SLOT[k]["y"], G.SHIP_SLOT[k]["z"])
        i = next((i for i, c in enumerate(left) if all(abs(a - b) <= 1e-3 for a, b in zip(c, want))), None)
        if i is None: miss += 1
        else: left.pop(i)
    same = miss == 0 and not left
    print(f"  갑판 컨테이너           씬 {len(scene_c)}개 · 식 {len(formula)}개(2단 {sum(k[2] == 2 for k in G.SHIP_OCC)}) · "
          f"좌표·점유 {'전부 일치(1mm)' if same else '불일치'}")
    if not same: fails.append("[씬] 배 갑판 컨테이너 좌표·점유")
    print(f"  작업 베이 B{G.WORK_BAY + 1:02d} z {G.bay_z(G.WORK_BAY):.2f} · GT {G.GT_WORK:.2f} · 이송고 HO {G.HO_CLEAR:.2f} "
          f"(최고 화물 {G.CARGO_TOP:.2f}m + 컨테이너 + {G.CLEAR_M}m)")

print("\n=== 7) 요약 ===")
print(f"  {man['total_rows']:,} 행 · {man['total_rows']*dt/60:.1f} 분 · 라벨 {man['label_distribution']}")
print(f"  range_m={man['range_m']}  vmax={man['vmax_ms']}  amax={man['amax_ms2']}")

if fails:
    print("\n실패 %d 건:" % len(fails)); [print("  -", x) for x in fails]; sys.exit(1)
print("\n전 항목 통과.")
