#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""결정론 게이트 — 선도(L군). 씬 실측(build/lines_report.json)과 수식(hull_form) 둘 다 잰다.
(구판 교훈: 수식이 맞아도 씬은 틀릴 수 있다 — 둘 다 재라)
실행: python3 gates/gate_check.py   → 실패 시 exit 1"""
import json, math, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
import hull_form as hf

REP_PATH = os.path.join(ROOT, "build", "lines_report.json")
FAIL = []
def check(cid, ok, msg):
    print(f"[{cid}] {'PASS' if ok else 'FAIL'} — {msg}")
    if not ok: FAIL.append(cid)

if not os.path.exists(REP_PATH):
    print("lines_report.json 없음 — 먼저 build_lines.py 를 돌려라"); sys.exit(1)
rep = json.load(open(REP_PATH, encoding="utf-8"))
sts, longs = rep["stations"], rep["longitudinals"]

# L1 커버리지 (씬): y·z 범위가 스펙 끝단을 덮는가
all_y = [p[1] for L in longs for p in L["pts"]] + [s["y"] for s in sts]
all_z = [p[2] for L in longs for p in L["pts"]] + [z for s in sts for _, z in s["pts"]]
check("L1", abs(min(all_y) - hf.Y_BULB) < 0.05 and abs(max(all_y) - hf.Y_TR) < 0.05
            and min(all_z) > -1e-6 and max(all_z) < hf.D + hf.H["sheer_fwd"] + 1e-6,
      f"y [{min(all_y):.2f},{max(all_y):.2f}] (요구 [{hf.Y_BULB},{hf.Y_TR}]) · z [{min(all_z):.3f},{max(all_z):.3f}]")

# L2 스테이션 간격: 퇴화(<0.5) 0 · 성김(>8.5) 0
ys = sorted(s["y"] for s in sts)
gaps = [b - a for a, b in zip(ys, ys[1:])]
check("L2", all(0.5 - 1e-6 <= g <= 8.5 for g in gaps),
      f"스테이션 {len(ys)} · 간격 {min(gaps):.3f}~{max(gaps):.3f} m (요구 0.5~8.5)")

# L3 종방향 페어링: 인접 세그먼트 꺾임 ≤ 25° (구판 기준)
worst = ("-", 0.0)
n_over = 0
for L in longs:
    P = L["pts"]
    for i in range(1, len(P) - 1):
        v0 = [P[i][k] - P[i-1][k] for k in range(3)]
        v1 = [P[i+1][k] - P[i][k] for k in range(3)]
        n0 = math.sqrt(sum(a*a for a in v0)); n1 = math.sqrt(sum(a*a for a in v1))
        if n0 < 1e-9 or n1 < 1e-9: continue
        c = max(-1.0, min(1.0, sum(a*b for a, b in zip(v0, v1)) / (n0 * n1)))
        ang = math.degrees(math.acos(c))
        if ang > worst[1]: worst = (L["name"], ang)
        if ang > 25.0: n_over += 1
check("L3", n_over == 0, f"종통선 {len(longs)}개 · 25° 초과 {n_over} · 최대 {worst[1]:.1f}° ({worst[0]})")

# L4 우현 반폭 음수 0 · 스테이션 z 단조증가
bad_x = sum(1 for s in sts for x, _ in s["pts"] if x < -1e-6)
bad_mono = [s["name"] for s in sts if any(b[1] <= a[1] - 1e-9 for a, b in zip(s["pts"], s["pts"][1:]))]
check("L4", bad_x == 0 and not bad_mono, f"x<0 {bad_x} · z비단조 {len(bad_mono)} {bad_mono[:3]}")

# L5 씬↔수식 항등: 미드십 WL 반폭 = B/2 · 갑판 반폭 = B/2
mid = min(sts, key=lambda s: abs(s["y"]))
def interp_x(pts, z):
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        if z0 - 1e-9 <= z <= z1 + 1e-9:
            return x0 if abs(z1-z0) < 1e-12 else x0 + (x1-x0)*(z-z0)/(z1-z0)
    return None
xw = interp_x(mid["pts"], hf.T); xd = mid["pts"][-1][0]
check("L5", abs(mid["y"]) < 1e-6 and abs(xw - hf.HB) < 1e-4 and abs(xd - hf.HB) < 1e-4,
      f"미드십(y={mid['y']}) WL반폭 {xw:.5f} · 갑판 {xd:.5f} (요구 {hf.HB})")

# L6 평행중앙부 WL 반폭 일정
wl = next((L for L in longs if L["name"] == f"WL_{hf.T:05.2f}"), None)
xs_pm = [p[0] for p in wl["pts"] if hf.PM["y_fwd"] + 1e-6 < p[1] < hf.PM["y_aft"] - 1e-6] if wl else []
check("L6", wl is not None and xs_pm and max(xs_pm) - min(xs_pm) < 1e-6,
      f"WL(T) 평행부 표본 {len(xs_pm)} · 편차 {max(xs_pm)-min(xs_pm) if xs_pm else -1:.2e}")

# L7 개수: 스테이션·종통선·메시 0
exp_st = len([y for y in hf.station_ys() if len(hf.section(y)) >= 2])
check("L7", rep["counts"]["stations"] == exp_st and rep["counts"]["meshes"] == 0,
      f"스테이션 {rep['counts']['stations']}/{exp_st} · 종통선 {rep['counts']['longitudinals']} · 메시 {rep['counts']['meshes']} (요구 0)")

# L8 프로펠러 개구 여유 (씬 킬 커브 실측)
keel = next(L for L in longs if L["name"] == "CL_Keel")
need = hf.Z_SHAFT + hf.R_PROP + hf.APERTURE_CLR
z_at_prop = None
for a, b in zip(keel["pts"], keel["pts"][1:]):
    if a[1] - 1e-9 <= hf.Y_PROP <= b[1] + 1e-9:
        t = (hf.Y_PROP - a[1]) / max(1e-12, b[1] - a[1]); z_at_prop = a[2] + (b[2]-a[2])*t
check("L8", z_at_prop is not None and z_at_prop >= need - 1e-4,
      f"개구 상단(씬) {z_at_prop if z_at_prop is not None else -1:.4f} ≥ 프로펠러 상단+여유 {need:.4f}")

# L9 벌브: 최전방 y = 코끝 · 최대 반경 ≤ 스펙 (씬 스테이션 실측)
bulb_pts = [(x, z) for s in sts if s["y"] < hf.Y_FP for x, z in s["pts"] if abs(z - hf.Z_BULB) < hf.R_BULB]
r_max = max((x for x, _ in bulb_pts), default=0.0)
check("L9", 0 < r_max <= hf.R_BULB + 1e-6, f"벌브 최대 반폭(씬) {r_max:.4f} ≤ 스펙 {hf.R_BULB}")

# L10 선미 오버행 늑골 자리: 트랜섬~AP 스테이션 ≥ 3 (구판 교훈 — 오버행에 늑골 0 사고)
n_oh = sum(1 for s in sts if s["y"] > hf.Y_AP - 1e-6)
check("L10", n_oh >= 3, f"AP({hf.Y_AP})~트랜섬 스테이션 {n_oh} (요구 ≥3)")

print(f"\n== 게이트 L군: {'PASS' if not FAIL else 'FAIL ' + str(FAIL)} ({len(sts)} 스테이션 · {len(longs)} 종통선) ==")
sys.exit(1 if FAIL else 0)
