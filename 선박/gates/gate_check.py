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

# L6 평행중앙부 WL 반폭 일정 (절단된 세그먼트 전체 합산)
wl_pts = [p for L in longs if L["name"] == f"WL_{hf.T:05.2f}" for p in L["pts"]]
xs_pm = [p[0] for p in wl_pts if hf.PM["y_fwd"] + 1e-6 < p[1] < hf.PM["y_aft"] - 1e-6]
check("L6", xs_pm and max(xs_pm) - min(xs_pm) < 1e-6,
      f"WL(T) 평행부 표본 {len(xs_pm)} · 편차 {max(xs_pm)-min(xs_pm) if xs_pm else -1:.2e}")

# L7 개수: 스테이션·종통선·메시 0
exp_st = len([y for y in hf.station_ys() if len(hf.section(y)) >= 2])
check("L7", rep["counts"]["stations"] == exp_st and rep["counts"]["meshes"] == 0,
      f"스테이션 {rep['counts']['stations']}/{exp_st} · 종통선 {rep['counts']['longitudinals']} · 메시 {rep['counts']['meshes']} (요구 0)")

# L8 프로펠러 개구 여유 (씬 킬 커브 실측 — 세그먼트 합산)
keel_pts = sorted((p for L in longs if L["name"] == "CL_Keel" for p in L["pts"]), key=lambda p: p[1])
need = hf.Z_SHAFT + hf.R_PROP + hf.APERTURE_CLR
z_at_prop = None
for a, b in zip(keel_pts, keel_pts[1:]):
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

# ── B군: 5등분 블록 (오너 지시 — 가로 정확 등분) ──
blk = rep.get("blocks", {})
# B1 등분: 경계가 정확한 1/5 등분인가 (씬 리포트 ↔ 수식)
bl = [b2 - b1 for b1, b2 in zip(hf.BOUNDS, hf.BOUNDS[1:])]
dev = max(abs(v - hf.L_BLK) for v in bl)
gap_sum = abs(sum(bl) - (hf.Y_TR - hf.Y_BULB))
check("B1", blk.get("bounds") == hf.BOUNDS and dev < 1e-9 and gap_sum < 1e-9,
      f"블록장 {hf.L_BLK:.4f} m ×{hf.N_BLK} · 등분 최대 편차 {dev:.2e} · 이음 틈/겹침 {gap_sum:.2e}")
# B2 경계 스냅: 내부 경계마다 스테이션이 정확히 존재 (삽입 아닌 이동 — 오차 0)
st_set = {round(s["y"], 6) for s in sts}
miss = [b for b in hf.BOUNDS[1:-1] if round(b, 6) not in st_set]
check("B2", not miss, f"경계 스테이션 스냅 오차 0 × {len(hf.BOUNDS)-2-len(miss)} (누락 {miss})")
# B3 블록 배분: 빈 블록 0 · 배속 규칙(경계=선미쪽) 일치
alloc = blk.get("alloc", {})
mis_assign = [s["name"] for s in sts if s.get("block") != hf.block_of(s["y"])]
check("B3", alloc and min(alloc.values()) >= 1 and not mis_assign,
      f"배분 {'/'.join(f'{k}:{v}' for k, v in sorted(alloc.items()))} · 오배속 {len(mis_assign)}")

# B4 롤케익 절단면 연속성 — 같은 곡선의 이웃 조각이 경계점을 공유(틈 0)하고 접선이 이어지는가
def seam_check(entries, ang_limit):
    from collections import defaultdict
    groups = defaultdict(list)
    for L in entries:
        if "seg" in L: groups[L["name"]].append(L)
    n_seam, max_gap, worst = 0, 0.0, ("-", 0.0)
    for name, segs in groups.items():
        segs.sort(key=lambda L: L["pts"][0][1])
        for a, b in zip(segs, segs[1:]):
            pa, pb = a["pts"], b["pts"]
            n_seam += 1
            max_gap = max(max_gap, math.dist(pa[-1], pb[0]))
            if len(pa) >= 2 and len(pb) >= 2:
                v0 = [pa[-1][k] - pa[-2][k] for k in range(3)]
                v1 = [pb[1][k] - pb[0][k] for k in range(3)]
                n0 = math.sqrt(sum(q*q for q in v0)); n1 = math.sqrt(sum(q*q for q in v1))
                if n0 > 1e-9 and n1 > 1e-9:
                    cth = max(-1.0, min(1.0, sum(p*q for p, q in zip(v0, v1)) / (n0*n1)))
                    ang = math.degrees(math.acos(cth))
                    if ang > worst[1]: worst = (name, ang)
    return n_seam, max_gap, worst
if any("seg" in L for L in longs):
    ns, mg, wo = seam_check(longs, 25.0)
    check("B4", mg < 1e-9 and wo[1] <= 25.0,
          f"선도 절단면 {ns} · 최대 틈 {mg:.2e} m (요구 0) · 이음 접선 최대 {wo[1]:.1f}° ({wo[0]})")

# ── C군: 철골 (cage_report.json 있을 때만) ──
CAGE_PATH = os.path.join(ROOT, "build", "cage_report.json")
if os.path.exists(CAGE_PATH):
    cg = json.load(open(CAGE_PATH, encoding="utf-8"))
    frs, strs = cg["frames"], cg["longitudinals"]
    fy = sorted(f["y"] for f in frs)
    fgaps = [b - a for a, b in zip(fy, fy[1:])]
    # C1 늑골 간격: 0.4~3.4 · 블록 내 주격자 균일(벌브 보충 링 제외)
    h = cg["counts"]["h"]
    uni_bad = 0
    for i in range(hf.N_BLK):
        bn = hf.block_name(i)
        ys_b = sorted(f["y"] for f in frs if f["block"] == bn and f["y"] >= hf.Y_STEM - 1e-6)
        uni_bad += sum(1 for a, b in zip(ys_b, ys_b[1:]) if abs((b - a) - h) > 1e-6 and (b - a) > h + 1e-6)
    check("C1", all(0.4 - 1e-6 <= g <= 3.4 + 1e-9 for g in fgaps) and h <= 3.4 and uni_bad == 0,
          f"늑골 {len(fy)} · h {h:.4f} (≤3.4) · 간격 {min(fgaps):.3f}~{max(fgaps):.3f} · 균일 위반 {uni_bad}")
    # C2 이음 늑골: 내부 경계 오차 0 ×4 · 커버리지(벌브~트랜섬) · 오버행 ≥3
    fset = {round(y, 6) for y in fy}
    miss = [b for b in hf.BOUNDS[1:-1] if round(b, 6) not in fset]
    n_oh = sum(1 for y in fy if y > hf.Y_AP - 1e-6)
    check("C2", not miss and min(fy) < hf.Y_STEM + 0.1 and abs(max(fy) - hf.Y_TR) < 1e-6 and n_oh >= 3,
          f"이음 오차 0 × {4-len(miss)} (누락 {miss}) · 범위 [{min(fy):.2f},{max(fy):.2f}] · 오버행 늑골 {n_oh}")
    # C3 늑골 곡률반경 ≥ 0.40 (판 냉간성형 20·t)
    def min_radius(pts):
        """판 냉간성형 반경 — 반폭 0.30m 미만(스템바·중심선 형강)은 성형 판이 아니므로 제외
        (구판 오탐 규약: 「짧은 변이 판폭 미만이면 성형 대상 아님」)"""
        rmin = 1e9
        for (x0,z0),(x1,z1),(x2,z2) in zip(pts, pts[1:], pts[2:]):
            if max(x0, x1, x2) < 0.30: continue          # 판폭 미만 형강(스템바) 구간
            if min(x0, x1, x2) < 0.05: continue          # 중심선 이음(용접 시임) — 성형 아님
            a = math.hypot(x1-x0, z1-z0); b = math.hypot(x2-x1, z2-z1); c = math.hypot(x2-x0, z2-z0)
            area2 = abs((x1-x0)*(z2-z0) - (x2-x0)*(z1-z0))
            if area2 < 1e-12: continue
            rmin = min(rmin, a*b*c / (2*area2))
        return rmin
    worst_r = min(((min_radius(f["pts"]), f["name"]) for f in frs), key=lambda t: t[0])
    check("C3", worst_r[0] >= 0.40, f"늑골 최소 곡률반경 {worst_r[0]:.3f} m ({worst_r[1]}, 요구 ≥0.40)")
    # C4 종통재 꺾임 ≤ 20°
    w2 = ("-", 0.0); over2 = 0
    for L in strs:
        P = L["pts"]
        for i in range(1, len(P)-1):
            v0 = [P[i][k]-P[i-1][k] for k in range(3)]; v1 = [P[i+1][k]-P[i][k] for k in range(3)]
            n0 = math.sqrt(sum(a*a for a in v0)); n1 = math.sqrt(sum(a*a for a in v1))
            if n0 < 1e-9 or n1 < 1e-9: continue
            cth = max(-1.0, min(1.0, sum(a*b for a,b in zip(v0,v1))/(n0*n1)))
            ang = math.degrees(math.acos(cth))
            if ang > w2[1]: w2 = (L["name"], ang)
            if ang > 20.0: over2 += 1
    check("C4", over2 == 0, f"종통재 {len(strs)} · 20° 초과 {over2} · 최대 {w2[1]:.1f}° ({w2[0]})")
    # C5 블록 배분·메시 0
    alloc_c = cg["blocks"]
    check("C5", min(alloc_c.values()) >= 1 and cg["counts"]["meshes"] == 0,
          f"배분 {'/'.join(f'{k}:{v}' for k,v in sorted(alloc_c.items()))} · 메시 {cg['counts']['meshes']} (요구 0)")
    # C6 종통재 절단면 연속성
    if any("seg" in L for L in strs):
        ns2, mg2, wo2 = seam_check(strs, 20.0)
        check("C6", mg2 < 1e-9 and wo2[1] <= 20.0,
              f"종통재 절단면 {ns2} · 최대 틈 {mg2:.2e} m · 이음 접선 최대 {wo2[1]:.1f}° ({wo2[0]})")

print(f"\n== 게이트: {'PASS' if not FAIL else 'FAIL ' + str(FAIL)} ({len(sts)} 스테이션 · {len(longs)} 종통선) ==")
sys.exit(1 if FAIL else 0)
