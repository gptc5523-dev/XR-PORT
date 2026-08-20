#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""선도 3면도 렌더 — build/lines_report.json(씬 실측)을 그대로 투영. render-critic 입력."""
import json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
rep = json.load(open(os.path.join(ROOT, "build", "lines_report.json"), encoding="utf-8"))
spec = json.load(open(os.path.join(ROOT, "spec", "ship_spec.json"), encoding="utf-8"))
sts, longs = rep["stations"], rep["longitudinals"]

BG, LINE, FAINT, ACC, DIM = "#12315e", "#eaf2ff", "#3f5f96", "#7fd4ff", "#9fc3ee"
PXM = 6.0; OX = 46.0
W = OX*2 + 324*PXM
def sx(y): return OX + (160.0 - y) * PXM
S = []
def add(s): S.append(s)
def pl(pts, st=LINE, w=0.9, op=1.0):
    d = "M " + " L ".join(f"{a:.1f} {b:.1f}" for a, b in pts)
    add(f'<path d="{d}" stroke="{st}" stroke-width="{w}" fill="none" opacity="{op}" stroke-linejoin="round"/>')
def text(x, y, s, size=12, st=LINE, anchor="start", bold=False, mono=True):
    fam = "SF Mono, Menlo, monospace" if mono else "Helvetica Neue, Arial, sans-serif"
    fw = ' font-weight="600"' if bold else ''
    add(f'<text x="{x:.1f}" y="{y:.1f}" font-family="{fam}" font-size="{size}" fill="{st}"'
        f'{fw} text-anchor="{anchor}">{s}</text>')

HGT = 1210
add(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W:.0f}" height="{HGT}" viewBox="0 0 {W:.0f} {HGT}">')
add(f'<rect width="{W:.0f}" height="{HGT}" fill="{BG}"/>')
add(f'<rect x="14" y="14" width="{W-28:.0f}" height="{HGT-28}" fill="none" stroke="{LINE}" stroke-width="2"/>')
text(OX, 52, "MV SEORA — LINES PLAN (선도)", 24, LINE, bold=True, mono=False)
text(W-OX, 52, "SOURCE build/lines_report.json (씬 실측) · GATE L1~L10 PASS", 12, DIM, "end")

for ym in range(-160, 161, 20):
    add(f'<line x1="{sx(ym):.1f}" y1="80" x2="{sx(ym):.1f}" y2="1120" stroke="{FAINT}" stroke-width="0.5" opacity="0.4"/>')
    text(sx(ym), 1136, f"{ym:+d}", 10, FAINT, "middle")

# ── ① SHEER PLAN (버톡·킬·갑판, 측면) ──
BASE1 = 420.0
def pz(z): return BASE1 - z * PXM
text(OX, 96, "SHEER PLAN — CL·BUTTOCKS·DECK", 13, ACC, bold=True)
add(f'<line x1="{sx(160)}" y1="{pz(0):.1f}" x2="{sx(-160)}" y2="{pz(0):.1f}" stroke="{DIM}" stroke-width="0.7" stroke-dasharray="14 4 3 4" opacity="0.7"/>')
add(f'<line x1="{sx(160)}" y1="{pz(13):.1f}" x2="{sx(-160)}" y2="{pz(13):.1f}" stroke="{ACC}" stroke-width="0.8" stroke-dasharray="8 5" opacity="0.8"/>')
text(sx(-160)+4, pz(13)-4, "WL", 10, ACC)
for L in longs:
    if L["name"].startswith(("CL_", "Deck", "BUT_")):
        w = 1.4 if L["name"] in ("CL_Keel", "Deck_Edge") else 0.8
        pl([(sx(p[1]), pz(p[2])) for p in L["pts"]], LINE, w)

# ── ② HALF-BREADTH PLAN (워터라인, 평면·우현) ──
CL2 = 620.0
def px2(x): return CL2 - x * PXM
text(OX, 480, "HALF-BREADTH PLAN — WATERLINES", 13, ACC, bold=True)
add(f'<line x1="{sx(160)}" y1="{CL2:.1f}" x2="{sx(-160)}" y2="{CL2:.1f}" stroke="{DIM}" stroke-width="0.7" stroke-dasharray="14 4 3 4" opacity="0.7"/>')
for L in longs:
    if L["name"].startswith("WL_"):
        z = float(L["name"][3:])
        st = ACC if abs(z - 13.0) < 1e-6 else LINE
        pl([(sx(p[1]), px2(p[0])) for p in L["pts"]], st, 1.3 if st == ACC else 0.8)
    if L["name"] == "Deck_Edge":
        pl([(sx(p[1]), px2(p[0])) for p in L["pts"]], LINE, 1.4)

# ── ③ BODY PLAN (스테이션 — 선수측 우현/선미측 좌현 표기) ──
BCX, BBASE, BPXM = W/2, 1095.0, 9.0
def bx(x): return BCX + x * BPXM
def bz(z): return BBASE - z * BPXM
text(OX, 786, "BODY PLAN — STATIONS (right: FWD of midship · left: AFT)", 13, ACC, bold=True)
add(f'<line x1="{BCX:.1f}" y1="{bz(27):.1f}" x2="{BCX:.1f}" y2="{bz(-1):.1f}" stroke="{DIM}" stroke-width="0.7" stroke-dasharray="10 4" opacity="0.8"/>')
add(f'<line x1="{bx(-21):.1f}" y1="{bz(13):.1f}" x2="{bx(21):.1f}" y2="{bz(13):.1f}" stroke="{ACC}" stroke-width="0.8" stroke-dasharray="8 5" opacity="0.8"/>')
add(f'<line x1="{bx(-21):.1f}" y1="{bz(0):.1f}" x2="{bx(21):.1f}" y2="{bz(0):.1f}" stroke="{DIM}" stroke-width="0.7" opacity="0.7"/>')
for s in sts:
    sgn = 1.0 if s["y"] <= 0 else -1.0          # 선수(−y)=우현측, 선미(+y)=좌현측
    mid = abs(s["y"]) < 1e-6
    pl([(bx(sgn*x), bz(z)) for x, z in s["pts"]], ACC if mid else LINE, 1.6 if mid else 0.55,
       op=1.0 if mid else 0.85)
text(bx(20.5), bz(13)-5, "WL", 10, ACC)
c = rep["counts"]
text(W-OX, 1160, f'STATIONS {c["stations"]} · LONGITUDINALS {c["longitudinals"]} · MESHES {c["meshes"]}'
     f' · SPEC v{spec["meta"]["version"]} · 2026-08-20', 11, DIM, "end")
add('</svg>')
out = os.path.join(ROOT, "build", "lines_plan.svg")
open(out, "w", encoding="utf-8").write("\n".join(S))
print("WROTE", out)
