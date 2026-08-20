#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""청사진(설계도) 생성 — spec/ship_spec.json 을 읽어 build/blueprint.svg 를 그린다.
치수는 전부 스펙에서 읽는다(CLAUDE.md 규칙 2). 의존성 없음(표준 라이브러리만)."""
import json, math, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = json.load(open(os.path.join(ROOT, "spec", "ship_spec.json"), encoding="utf-8"))

P  = SPEC["principal"]; E = SPEC["envelope"]; H = SPEC["hull"]
C  = SPEC["cargo"];     S = SPEC["superstructure"]; PR = SPEC["propulsion"]
LOA, LPP, B, D, T = P["LOA"], P["LPP"], P["B"], P["D"], P["T_design"]
HB = B / 2.0

# ── 캔버스/패널 배치 (픽셀 상수 — 선박 치수 아님) ─────────────────────────
PXM = 6.0                      # px per meter
Y_L, Y_R = 160.0, -164.0       # 월드 y 범위(좌=선미쪽 +160, 우=선수쪽 -164)
OX = 46.0
W  = OX * 2 + (Y_L - Y_R) * PXM
BG, LINE, FAINT, DIM, ACC = "#12315e", "#eaf2ff", "#3f5f96", "#9fc3ee", "#7fd4ff"

def sx(y): return OX + (Y_L - y) * PXM          # 선수(-Y)가 화면 오른쪽
PROF_BASE = 486.0                                # 측면도 기선(z=0) 픽셀
def pz(z): return PROF_BASE - z * PXM
PLAN_CL = 690.0                                  # 평면도 중심선 픽셀
def px_(x): return PLAN_CL - x * PXM

SVG = []
def add(s): SVG.append(s)
def line(x1,y1,x2,y2,st=LINE,w=1.2,dash=None,op=1.0):
    d = f' stroke-dasharray="{dash}"' if dash else ''
    add(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{st}" stroke-width="{w}" opacity="{op}"{d}/>')
def path(dstr,st=LINE,w=1.4,fill="none",dash=None,op=1.0):
    dd = f' stroke-dasharray="{dash}"' if dash else ''
    add(f'<path d="{dstr}" stroke="{st}" stroke-width="{w}" fill="{fill}" opacity="{op}"{dd} stroke-linejoin="round" stroke-linecap="round"/>')
def rect(x,y,w_,h,st=LINE,sw=1.2,fill="none",op=1.0):
    add(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w_:.1f}" height="{h:.1f}" stroke="{st}" stroke-width="{sw}" fill="{fill}" opacity="{op}"/>')
def text(x,y,s,size=12,st=LINE,anchor="start",bold=False,mono=False,op=1.0,spacing=None):
    fam = "SF Mono, Menlo, monospace" if mono else "Helvetica Neue, Arial, sans-serif"
    fw  = ' font-weight="600"' if bold else ''
    ls  = f' letter-spacing="{spacing}"' if spacing else ''
    add(f'<text x="{x:.1f}" y="{y:.1f}" font-family="{fam}" font-size="{size}" fill="{st}"{fw}{ls} text-anchor="{anchor}" opacity="{op}">{s}</text>')
def poly(pts,close=False,**kw):
    d = "M " + " L ".join(f"{x:.1f} {y:.1f}" for x,y in pts) + (" Z" if close else "")
    path(d,**kw)
def dim_h(y_px, m0, m1, label, above=True):
    """수평 치수선 (m0,m1 은 월드 y)"""
    x0, x1 = sx(m0), sx(m1)
    if x0 > x1: x0, x1 = x1, x0
    line(x0, y_px, x1, y_px, DIM, 1.0)
    for x in (x0, x1):
        line(x, y_px-4, x, y_px+4, DIM, 1.0)
    ty = y_px - 5 if above else y_px + 14
    text((x0+x1)/2, ty, label, 11.5, DIM, "middle", mono=True)
def dim_v(x_px, z0, z1, label):
    y0, y1 = pz(z0), pz(z1)
    line(x_px, y0, x_px, y1, DIM, 1.0)
    for y in (y0, y1):
        line(x_px-4, y, x_px+4, y, DIM, 1.0)
    add(f'<text x="{x_px-6:.1f}" y="{(y0+y1)/2:.1f}" font-family="SF Mono, Menlo, monospace" font-size="11.5" fill="{DIM}" text-anchor="middle" transform="rotate(-90 {x_px-6:.1f} {(y0+y1)/2:.1f})">{label}</text>')

# ── 배경·헤더·격자 ──────────────────────────────────────────────────────
HGT = 1210
add(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W:.0f}" height="{HGT}" viewBox="0 0 {W:.0f} {HGT}">')
add(f'<rect width="{W:.0f}" height="{HGT}" fill="{BG}"/>')
add(f'<rect x="14" y="14" width="{W-28:.0f}" height="{HGT-28}" fill="none" stroke="{LINE}" stroke-width="2"/>')
add(f'<rect x="20" y="20" width="{W-40:.0f}" height="{HGT-40}" fill="none" stroke="{FAINT}" stroke-width="0.8"/>')
text(OX, 52, SPEC["meta"]["name"] + " — GENERAL ARRANGEMENT", 26, LINE, bold=True, spacing="2px")
text(OX, 72, SPEC["meta"]["class"] + " · " + SPEC["meta"]["operator"].split(" (")[0] + " (가공 선사)", 13, ACC, spacing="1px")
text(W-OX, 52, "DWG NO. SEORA-GA-001  REV 1.0.0", 12, DIM, "end", mono=True)
text(W-OX, 70, "DATE " + SPEC["meta"]["date"] + "  ·  SPEC ship_spec.json", 12, DIM, "end", mono=True)

# 스테이션 격자 (20 m 간격, 측면도+평면도 관통)
for ym in range(-160, 161, 20):
    x = sx(ym)
    line(x, 92, x, 806, FAINT, 0.6, op=0.55)
    text(x, 820, f"{ym:+d}", 10, FAINT, "middle", mono=True)
text(sx(0), 834, "STATION (m, midship=0 · bow −Y →)", 10, FAINT, "middle", mono=True)

# ══════════════ ① 측면도 SIDE PROFILE ══════════════
text(OX, 108, "PROFILE  (측면도)", 13, ACC, bold=True, spacing="2px")

yb_tip = E["y_bulb_tip"]; y_stem = E["y_stem_deck"]; y_tr = E["y_transom_deck"]
y_wl_aft = E["y_wl_aft_end"]; yFP, yAP = E["y_FP"], E["y_AP"]
fk = H["flat_keel"]; bb = H["bulbous_bow"]; st = H["stern"]
fc = S["forecastle"]; dh = S["deckhouse"]; fn = S["funnel"]
z_cm = D + C["hatch_coaming_h"] + C["hatch_cover_h"]          # 코밍+커버 상단
sheer_f, sheer_a = H["sheer_fwd"], H["sheer_aft"]
pm = H["parallel_midbody"]

def z_deck(y):
    if y < pm["y_fwd"]:
        t = (pm["y_fwd"] - y) / (pm["y_fwd"] - y_stem)
        return D + sheer_f * t * t
    if y > pm["y_aft"]:
        t = (y - pm["y_aft"]) / (y_tr - pm["y_aft"])
        return D + sheer_a * t * t
    return D

# 갑판선(시어) — 선수루 구간은 +높이
y_fc_aft = y_stem + fc["length"]
deck_pts = []
yy = y_tr
while yy >= y_stem - 1e-9:
    deck_pts.append((sx(yy), pz(z_deck(yy) + (fc["height"] if yy <= y_fc_aft else 0.0))))
    yy -= 1.0
poly(deck_pts)
# 선수루 단차
line(sx(y_fc_aft), pz(z_deck(y_fc_aft)), sx(y_fc_aft), pz(z_deck(y_fc_aft)+fc["height"]))
line(sx(y_fc_aft), pz(z_deck(y_fc_aft)), sx(pm["y_fwd"]), pz(D))  # 주갑판선 연속(선수루 아래)

# 선수: 스템 → 벌브 → 포어풋 (한 획 연속)
z_bulb, r_bulb = bb["axis_z"], bb["max_radius"]
stem_tip_z = z_deck(y_stem) + fc["height"]
path(f'M {sx(y_stem):.1f} {pz(stem_tip_z):.1f} '
     f'C {sx(y_stem-0.8):.1f} {pz(stem_tip_z-5):.1f} {sx(yFP-3.2):.1f} {pz(T+4.5):.1f} {sx(yFP-3.2):.1f} {pz(T):.1f} '            # 스템(WL까지 완만한 래이크)
     f'C {sx(yFP-3.2):.1f} {pz(T-1.8):.1f} {sx(yFP-4.2):.1f} {pz(z_bulb+r_bulb):.1f} {sx(yFP-6.2):.1f} {pz(z_bulb+r_bulb):.1f} '  # 벌브 어깨
     f'C {sx(yb_tip+0.6):.1f} {pz(z_bulb+r_bulb):.1f} {sx(yb_tip):.1f} {pz(z_bulb+r_bulb*0.55):.1f} {sx(yb_tip):.1f} {pz(z_bulb):.1f} '
     f'C {sx(yb_tip):.1f} {pz(z_bulb-r_bulb*0.55):.1f} {sx(yb_tip+0.6):.1f} {pz(z_bulb-r_bulb):.1f} {sx(yFP-5.2):.1f} {pz(z_bulb-r_bulb):.1f} '
     f'C {sx(yFP+2):.1f} {pz(z_bulb-r_bulb):.1f} {sx(yFP+10):.1f} {pz(0.4):.1f} {sx(fk["y_fwd"]):.1f} {pz(0):.1f}')               # 포어풋 → 평탄 킬
# 평탄 킬
line(sx(fk["y_fwd"]), pz(0), sx(fk["y_aft"]), pz(0), w=1.6)
# 선미: 런 → 스케그 → 프로펠러 개구 → 트랜섬
pr = PR["propeller"]; rd = PR["rudder"]
r_prop = pr["diameter"] / 2.0
path(f'M {sx(fk["y_aft"]):.1f} {pz(0):.1f} '
     f'C {sx(st["skeg"]["y_fwd"]-4):.1f} {pz(0.2):.1f} {sx(st["skeg"]["y_fwd"]):.1f} {pz(0.5):.1f} {sx(st["skeg"]["y_fwd"]+4):.1f} {pz(0.7):.1f} '
     f'L {sx(st["skeg"]["y_aft"]):.1f} {pz(2.0):.1f}')
path(f'M {sx(st["skeg"]["y_aft"]):.1f} {pz(2.0):.1f} '
     f'C {sx(st["skeg"]["y_aft"]+1.5):.1f} {pz(6.5):.1f} {sx(pr["y_center"]-3.5):.1f} {pz(pr["shaft_z"]+3.2):.1f} {sx(pr["y_center"]-2.5):.1f} {pz(pr["shaft_z"]+r_prop+0.9):.1f}')
path(f'M {sx(pr["y_center"]-2.5):.1f} {pz(pr["shaft_z"]+r_prop+0.9):.1f} '
     f'C {sx(pr["y_center"]+4):.1f} {pz(pr["shaft_z"]+r_prop+2.5):.1f} {sx(y_wl_aft+2):.1f} {pz(T-0.4):.1f} {sx(y_wl_aft):.1f} {pz(T):.1f} '
     f'C {sx(y_tr-2):.1f} {pz(st["transom_bottom_z"]-0.8):.1f} {sx(y_tr):.1f} {pz(st["transom_bottom_z"]):.1f} {sx(y_tr):.1f} {pz(st["transom_bottom_z"]):.1f}')
line(sx(y_tr), pz(st["transom_bottom_z"]), sx(y_tr), pz(z_deck(y_tr)))   # 트랜섬
# 프로펠러(측면 디스크) + 허브 + 방향타
add(f'<ellipse cx="{sx(pr["y_center"]):.1f}" cy="{pz(pr["shaft_z"]):.1f}" rx="{0.55*PXM:.1f}" ry="{r_prop*PXM:.1f}" stroke="{ACC}" stroke-width="1.3" fill="none"/>')
add(f'<circle cx="{sx(pr["y_center"]):.1f}" cy="{pz(pr["shaft_z"]):.1f}" r="{0.99*PXM:.1f}" stroke="{ACC}" stroke-width="1.0" fill="{BG}"/>')
rud_h = rd["area_m2"] / 8.0   # 개략 스팬 8m 기준 코드
poly([(sx(rd["y_center"]-rud_h/2), pz(1.2)), (sx(rd["y_center"]+rud_h/2), pz(1.6)),
      (sx(rd["y_center"]+rud_h/2*0.8), pz(9.6)), (sx(rd["y_center"]-rud_h/2*0.9), pz(9.4))], close=True, st=ACC, w=1.2)
text(sx(pr["y_center"]), pz(-1.6), f'PROP Ø{pr["diameter"]:.2f} ×{pr["blades"]}', 10.5, ACC, "middle", mono=True)

# 흘수선·기선·수직선
line(sx(y_wl_aft), pz(T), sx(yb_tip-2), pz(T), ACC, 1.0, dash="8 5")
text(sx(yb_tip-2)+4, pz(T)+4, f"WL  T={T:.1f} m", 11, ACC, mono=True)
line(sx(Y_L), pz(0), sx(Y_R), pz(0), DIM, 0.8, dash="14 4 3 4", op=0.8)
text(sx(Y_R)+2, pz(0)+13, "BASELINE z=0", 10, DIM, "end", mono=True)
for yv, lab in ((yFP, "FP"), (yAP, "AP"), (0.0, "MIDSHIP")):
    line(sx(yv), pz(-2.5), sx(yv), pz(D+22), FAINT, 0.9, dash="3 4")
    text(sx(yv), pz(D+23.5), lab, 10.5, DIM, "middle", mono=True)

# 해치코밍 + 갑판 컨테이너 + 라싱브리지
y0c, y1c = C["hold_y_fwd"], C["hold_y_aft"]
rect(sx(y0c), pz(z_cm), (y1c-y0c)*PXM, (z_cm-D)*PXM, sw=1.0)
box_l = C["container"]["feu_L"]; tier_h = C["container"]["H"]
for i in range(C["bays"]):
    yc = y0c + C["bay_pitch"]*i + (C["bay_pitch"]-box_l)/2
    for t_ in range(C["tiers_deck"]):
        rect(sx(yc+box_l), pz(z_cm+(t_+1)*tier_h), box_l*PXM, tier_h*PXM, sw=0.65, op=0.85)
    if i > 0:
        yl = y0c + C["bay_pitch"]*i
        rect(sx(yl+0.5), pz(z_cm+C["lashing_bridge_h"]), 1.0*PXM, C["lashing_bridge_h"]*PXM, st=FAINT, sw=0.8)

# 거주구(9층+휠하우스) · 펀넬 · 마스트
dhz0, dhz1 = D, D + dh["height"] - dh["wheelhouse_h"]
rect(sx(dh["y_aft"]), pz(dhz1), dh["length"]*PXM, (dhz1-dhz0)*PXM, sw=1.4)
for k in range(1, dh["decks"]):
    zz = dhz0 + k*(dhz1-dhz0)/dh["decks"]
    line(sx(dh["y_aft"])+3, pz(zz), sx(dh["y_fwd"])-3, pz(zz), FAINT, 0.7)
rect(sx(dh["y_aft"]+1.0), pz(dhz1+dh["wheelhouse_h"]), (dh["length"]+2.0)*PXM, dh["wheelhouse_h"]*PXM, sw=1.4)
text(sx(dh["y_fwd"]-3.0), pz(dh["height"]+D-4.0), "DECKHOUSE 9F", 10.5, DIM, "start", mono=True)
fnz1 = D + fn["height"]
poly([(sx(fn["y_aft"]), pz(D)), (sx(fn["y_fwd"]), pz(D)),
      (sx(fn["y_fwd"]+fn["rake"]*0.4+(1-fn["taper"])*fn["length"]), pz(fnz1)),
      (sx(fn["y_aft"]+fn["rake"]), pz(fnz1))], close=True, w=1.4)
line(sx(fn["y_aft"]+fn["rake"]*0.5), pz(fnz1-3), sx(fn["y_fwd"]+(1-fn["taper"])*fn["length"]*0.7), pz(fnz1-3), ACC, 1.4)
text(sx((fn["y_fwd"]+fn["y_aft"])/2), pz(fn["height"]+D+2.5), "FUNNEL", 10.5, DIM, "middle", mono=True)
mast_y = y_stem + fc["length"]*0.45
line(sx(mast_y), pz(z_deck(mast_y)+fc["height"]), sx(mast_y), pz(z_deck(mast_y)+fc["height"]+S["masts"]["fore_h"]), w=1.3)
line(sx(mast_y-2.2), pz(z_deck(mast_y)+fc["height"]+S["masts"]["fore_h"]*0.75), sx(mast_y+2.2), pz(z_deck(mast_y)+fc["height"]+S["masts"]["fore_h"]*0.75), w=1.1)
rx_y = dh["y_fwd"]+dh["length"]*0.5
line(sx(rx_y), pz(dhz1+dh["wheelhouse_h"]), sx(rx_y), pz(dhz1+dh["wheelhouse_h"]+S["masts"]["radar_h"]), w=1.3)

# 5등분 블록 (오너 지시 2026-08-20 — 전장 정확 등분, B1=선미)
n_b = SPEC["blocks"]["count"]
lb = LOA / n_b
for k in range(1, n_b):
    ybk = yb_tip + lb * k
    line(sx(ybk), pz(-2), sx(ybk), pz(D+3), ACC, 0.9, dash="4 4", op=0.75)
for k in range(n_b):
    yc = yb_tip + lb * (k + 0.5)
    text(sx(yc), pz(4.5), f"B{n_b-k}", 12, ACC, "middle", mono=True, bold=True)
text(OX+4, pz(4.5), f"BLOCKS ×{n_b} = {lb:.1f} m", 10, ACC, mono=True)

# 치수선 (측면도) — 전부 기선 아래로 (구조물 관통 금지)
dim_h(pz(-6.5), y_tr, yb_tip, f"LOA {LOA:.1f} m")
dim_h(pz(-13), yAP, yFP, f"LPP {LPP:.1f} m", above=True)
dim_h(pz(-13), yFP, yb_tip, f'BULB {bb["protrusion_beyond_FP"]:.1f}', above=True)
dim_v(sx(Y_L)-14, 0, T, f"T {T:.1f}")
dim_v(sx(Y_L)-34, 0, D, f"D {D:.1f}")

# ══════════════ ② 평면도 DECK PLAN ══════════════
text(OX, 553, "UPPER DECK PLAN  (평면도)", 13, ACC, bold=True, spacing="2px")

def half_b(y):
    if y < pm["y_fwd"]:
        t = (pm["y_fwd"] - y) / (pm["y_fwd"] - y_stem)
        return HB * max(0.0, 1.0 - t**H["entrance_exponent"])
    if y > pm["y_aft"]:
        t = (y - pm["y_aft"]) / (y_tr - pm["y_aft"])
        return st["transom_half_width"] + (HB - st["transom_half_width"]) * (1 - t*t)
    return HB
pts_s, pts_p = [], []
nose = 2.4                                   # 스템 라운딩(둥근 선수 데크)
yy = y_stem + nose
while yy <= y_tr + 1e-9:
    hb = half_b(yy)
    pts_s.append((sx(yy), px_(hb))); pts_p.append((sx(yy), px_(-hb)))
    yy += 1.0
hb_n = half_b(y_stem + nose)
nose_d = (f'M {sx(y_stem+nose):.1f} {px_(hb_n):.1f} '
          f'Q {sx(y_stem):.1f} {px_(hb_n*0.55):.1f} {sx(y_stem):.1f} {px_(0):.1f} '
          f'Q {sx(y_stem):.1f} {px_(-hb_n*0.55):.1f} {sx(y_stem+nose):.1f} {px_(-hb_n):.1f}')
path(nose_d, w=1.5)
poly(pts_s + [(sx(y_tr), px_(-st["transom_half_width"]))] + pts_p[::-1], close=False, w=1.5)
line(sx(Y_L), PLAN_CL, sx(Y_R), PLAN_CL, DIM, 0.7, dash="14 4 3 4", op=0.7)
# 해치 14베이
hw = C["hatch_half_width"]
for i in range(C["bays"]):
    yh = y0c + C["bay_pitch"]*i + 0.7
    rect(sx(yh + (C["bay_pitch"]-1.4)), px_(hw), (C["bay_pitch"]-1.4)*PXM, hw*2*PXM, sw=0.9)
    line(sx(yh+(C["bay_pitch"]-1.4)/2+0.7)-0, px_(hw), sx(yh+(C["bay_pitch"]-1.4)/2+0.7), px_(-hw), FAINT, 0.6)
text(sx(y0c+C["bays"]*C["bay_pitch"]/2), px_(hw)+ -8, f'HATCHES {C["bays"]} BAYS × {C["rows_deck"]} ROWS', 10.5, DIM, "middle", mono=True)
# 거주구·펀넬·선수루 브레이크워터
rect(sx(dh["y_aft"]), px_(dh["width"]/2), dh["length"]*PXM, dh["width"]*PXM, sw=1.3)
rect(sx(fn["y_aft"]), px_(fn["width"]/2), fn["length"]*PXM, fn["width"]*PXM, st=ACC, sw=1.1)
bw_y = y_fc_aft - 3.0
poly([(sx(bw_y), px_(half_b(bw_y)*0.85)), (sx(bw_y-4), px_(0)), (sx(bw_y), px_(-half_b(bw_y)*0.85))], w=1.1, st=FAINT)
dim_v_x = sx(Y_L) - 14
line(dim_v_x, px_(HB), dim_v_x, px_(-HB), DIM, 1.0)
for yv in (px_(HB), px_(-HB)):
    line(dim_v_x-4, yv, dim_v_x+4, yv, DIM, 1.0)
add(f'<text x="{dim_v_x-6:.1f}" y="{PLAN_CL:.1f}" font-family="SF Mono, Menlo, monospace" font-size="11.5" fill="{DIM}" text-anchor="middle" transform="rotate(-90 {dim_v_x-6:.1f} {PLAN_CL:.1f})">B {B:.2f} m</text>')

# ══════════════ ③ 중앙단면 MIDSHIP SECTION ══════════════
MS_CX, MS_BASE, MS_PXM = 300.0, 1140.0, 5.0
def mx(x): return MS_CX + x * MS_PXM
def mz(z): return MS_BASE - z * MS_PXM
text(60, 872, "MIDSHIP SECTION  (중앙단면)", 13, ACC, bold=True, spacing="2px")
br = H["bilge_radius"]
sect = f'M {mx(-HB):.1f} {mz(D):.1f} L {mx(-HB):.1f} {mz(br):.1f} Q {mx(-HB):.1f} {mz(0):.1f} {mx(-HB+br):.1f} {mz(0):.1f} ' \
       f'L {mx(HB-br):.1f} {mz(0):.1f} Q {mx(HB):.1f} {mz(0):.1f} {mx(HB):.1f} {mz(br):.1f} L {mx(HB):.1f} {mz(D):.1f}'
path(sect, w=1.6)
path(f'M {mx(-HB):.1f} {mz(D):.1f} Q {mx(0):.1f} {mz(D+2*H["camber"]):.1f} {mx(HB):.1f} {mz(D):.1f}', w=1.2)
line(mx(-HB+br), mz(H["double_bottom_h"]), mx(HB-br), mz(H["double_bottom_h"]), FAINT, 0.9)
line(mx(-HB-3), mz(T), mx(HB+3), mz(T), ACC, 1.0, dash="8 5")
text(mx(HB+3)+4, mz(T)+4, "WL", 10.5, ACC, mono=True)
# 홀드 셀가이드(8단) + 코밍 + 갑판적(5단)
rw, th = C["row_pitch"], C["container"]["H"]
hold_rows = C["rows_deck"] - 2
for r in range(hold_rows):
    x0 = -(hold_rows*rw)/2 + r*rw
    for t_ in range(C["tiers_hold"]):
        rect(mx(x0)+0.6, mz(H["double_bottom_h"]+(t_+1)*th), rw*MS_PXM-1.2, th*MS_PXM, st=FAINT, sw=0.55, op=0.9)
rect(mx(-hw), mz(z_cm), hw*2*MS_PXM, (z_cm-D)*MS_PXM, sw=1.1)
for r in range(C["rows_deck"]):
    x0 = -(C["rows_deck"]*rw)/2 + r*rw
    for t_ in range(C["tiers_deck"]):
        rect(mx(x0)+0.6, mz(z_cm+(t_+1)*th), rw*MS_PXM-1.2, th*MS_PXM, st=LINE, sw=0.55, op=0.75)
# 빌지킬
bk = H["bilge_keel"]
ang = math.radians(45)
for sgn in (-1, 1):
    x0, z0 = sgn*(HB-br+br*math.cos(ang)*0+br*0.29), br - br*0.29
    line(mx(sgn*(HB-br*0.29)), mz(br*0.29), mx(sgn*(HB-br*0.29)+sgn*bk["height"]*MS_PXM/MS_PXM), mz(br*0.29-bk["height"]), ACC, 1.4)
text(mx(HB+1), mz(0)+12, f'BILGE KEEL {bk["segments"]}×{bk["seg_length"]:.2f}×{bk["height"]:.1f} m', 10, ACC, mono=True)
line(mx(-HB), mz(-2.5), mx(HB), mz(-2.5), DIM, 1.0)
for xv in (mx(-HB), mx(HB)): line(xv, mz(-2.5)-4, xv, mz(-2.5)+4, DIM, 1.0)
text(mx(0), mz(-2.5)+15, f"B {B:.2f} m", 11.5, DIM, "middle", mono=True)
text(mx(0), mz(z_cm+C["tiers_deck"]*th+2.5), f'HOLD {C["tiers_hold"]} TIERS + DECK {C["tiers_deck"]} TIERS', 10, DIM, "middle", mono=True)

# ══════════════ ④ 주요요목표 + 표제란 ══════════════
tbl_x, tbl_y = 620.0, 880.0
text(tbl_x, tbl_y-8, "PRINCIPAL PARTICULARS  (주요요목)", 13, ACC, bold=True, spacing="2px")
rows = [
    ("LENGTH O.A.",        f"{LOA:.1f} m"),
    ("LENGTH B.P.",        f"{LPP:.1f} m"),
    ("BREADTH MLD.",       f"{B:.2f} m"),
    ("DEPTH MLD.",         f"{D:.1f} m"),
    ("DRAFT (DESIGN)",     f"{T:.1f} m"),
    ("BLOCK COEFF. Cb",    f'{P["Cb"]:.3f}'),
    ("LCB",                f'{P["LCB_pct_LPP"]:+.2f} %LPP'),
    ("DISPLACEMENT",       f'{P["displacement_t"]:,} t'),
    ("CAPACITY",           f'{C["teu_nominal"]:,} TEU ({C["bays"]} BAYS × {C["rows_deck"]} ROWS)'),
    ("SERVICE SPEED",      f'{P["speed_kn"]:.0f} kn'),
    ("PROPELLER",          f'Ø{PR["propeller"]["diameter"]:.2f} m · {PR["propeller"]["blades"]} BLADES · Ae/A0 {PR["propeller"]["AeA0"]:.1f}'),
    ("RUDDER",             f'{PR["rudder"]["profile"]} · {PR["rudder"]["area_m2"]:.0f} m²'),
]
rh = 24.5
for i,(k,v) in enumerate(rows):
    yr = tbl_y + 10 + i*rh
    line(tbl_x, yr+7, tbl_x+560, yr+7, FAINT, 0.6, op=0.7)
    text(tbl_x, yr, k, 12, DIM, mono=True)
    text(tbl_x+560, yr, v, 12.5, LINE, "end", mono=True, bold=True)

tb_x = 1260.0
rect(tb_x, tbl_y-2, W-OX-tb_x, 310, sw=1.4)
for i,(k,v,big) in enumerate([
    ("VESSEL",  SPEC["meta"]["name"], True),
    ("OWNER",   "SEORA LINE (FICTIONAL)", False),
    ("DRAWING", "GENERAL ARRANGEMENT", False),
    ("SSOT",    "spec/ship_spec.json v" + SPEC["meta"]["version"], False),
    ("FRAME",   "1 BU = 1 m · BOW −Y · z0 = BASELINE", False),
    ("REFS",    "KCS (R4·R5) · DTC (R6·R7) · 실물비례 R1~R3", False),
    ("PIPELINE","SPEC → LINES → CAGE → FAIRING → SURFACE → MESH", False),
    ("DATE",    SPEC["meta"]["date"] + "  ·  DRAWN BY CLAUDE (concept-designer)", False),
]):
    yr = tbl_y + 24 + i*34
    text(tb_x+16, yr, k, 10.5, DIM, mono=True, spacing="1px")
    text(tb_x+16, yr+15, v, 15 if big else 12.5, LINE, bold=big, mono=not big)
    if i < 7: line(tb_x, yr+22, W-OX, yr+22, FAINT, 0.6, op=0.6)
# 축척 막대 (50 m)
sb_y = tbl_y + 296
line(tb_x+16, sb_y, tb_x+16+50*PXM*0.5, sb_y, LINE, 2.2)
for k in range(6):
    line(tb_x+16+k*10*PXM*0.5, sb_y-4, tb_x+16+k*10*PXM*0.5, sb_y+4, LINE, 1.2)
text(tb_x+16+50*PXM*0.5+8, sb_y+4, "50 m (@0.5×)", 10.5, DIM, mono=True)

add('</svg>')
out = os.path.join(ROOT, "build", "blueprint.svg")
os.makedirs(os.path.dirname(out), exist_ok=True)
open(out, "w", encoding="utf-8").write("\n".join(SVG))
print("WROTE", out, f"({len(SVG)} elements)")
