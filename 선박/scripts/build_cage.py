#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""철골(cage) 빌더 — 블록 인식 늑골 + 전선 종통재. 커브만(메시 0). 실행:
blender --background build/ship.blend --python scripts/build_cage.py
산출: build/ship.blend 갱신 + build/cage_report.json"""
import bpy, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
import hull_form as hf

# 기존 Cage 제거(멱등 재실행)
old = bpy.data.collections.get("ContainerShip_Cage")
if old:
    for c in list(old.children_recursive) + [old]:
        for ob in list(c.objects):
            cu = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if cu and cu.users == 0: bpy.data.curves.remove(cu)
        bpy.data.collections.remove(c)

scene = bpy.context.scene
root = bpy.data.collections.new("ContainerShip_Cage")
scene.collection.children.link(root)
col_blk = {}
for i in range(hf.N_BLK):
    nm = hf.block_name(i)
    c = bpy.data.collections.new(f"Cage_{nm}"); root.children.link(c)
    col_blk[nm] = c
col_lg = bpy.data.collections.new("Cage_Long"); root.children.link(col_lg)

def add_poly(name, pts, coll):
    cu = bpy.data.curves.new(name, type='CURVE'); cu.dimensions = '3D'
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for p, (x, y, z) in zip(sp.points, pts):
        p.co = (x, y, z, 1.0)
    ob = bpy.data.objects.new(name, cu); coll.objects.link(ob)

report = {"frames": [], "longitudinals": []}

# ── 늑골: 블록별 균일 분할 — 이음 늑골 = 분할 양 끝 (경계 오차 0) ──────────
import math
FRAME_MAX = 3.0
n_per = math.ceil(hf.L_BLK / FRAME_MAX - 1e-9)
h = hf.L_BLK / n_per                      # 58.8/20 = 2.94
made = set()
order = {hf.block_name(i): i for i in range(hf.N_BLK)}
for bname in hf.ERECTION_ORDER:           # 탑재 순서대로 생성 — 이음 늑골은 선행 블록에 배속
    i = hf.N_BLK - int(bname[1])          # 블록명 → 구간 인덱스(0=선수)
    y0 = hf.BOUNDS[i]
    ys = [round(y0 + h * k, 6) for k in range(n_per + 1)]
    if bname == hf.block_name(0):         # 선수 블록: 벌브 보충 링
        yb = hf.Y_STEM - 0.6
        while yb > hf.Y_BULB + 0.35:
            ys.append(round(yb, 6)); yb -= 0.6
    if bname == hf.block_name(hf.N_BLK - 1):   # 선미 블록: 오버행 보충 늑골(방향타 자리 ≥3)
        ys.append(round(y0 + h * (n_per - 0.5), 6))
    for y in sorted(ys, reverse=True):
        if y in made: continue
        sec = hf.section(y, n_wl=60)
        if hf.half_deck(y) < 0.30:
            # 스템 늑골(갑판 반폭 < 판폭): 벌브 정점(중심선)에서 끝낸다 — 그 위는 스템바
            cut = None
            for i, (x, _) in enumerate(sec):
                if x < 1e-3 and i > 0 and sec[i-1][0] > 1e-2:
                    cut = i; break
            if cut: sec = sec[:cut + 1]
        while len(sec) > 2 and sec[-1][0] < 1e-3 and sec[-2][0] < 1e-3:
            sec.pop()                 # 잔여 0 꼬리 정리
        if len(sec) < 2: continue
        made.add(y)
        name = f"Frm_{y:+08.2f}"
        add_poly(name, [(x, y, z) for x, z in sec], col_blk[bname])
        report["frames"].append({"name": name, "y": y, "block": bname,
                                 "pts": [[round(x, 6), round(z, 6)] for x, z in sec]})

# ── 종통재: 전선 공통 (z 홀수 레벨) ─────────────────────────────────────
DENSE = [(hf.FK["y_aft"] + 15.0, hf.Y_TR), (hf.Y_BULB, hf.Y_FP + 12.0),
         (hf.FK["y_aft"] - 4.0, hf.FK["y_aft"] + 16.0)]   # 선미 런 전체 조밀(스트링거 감김부)
def y_samples(y0, y1, base=1.0, dense=0.35):
    ys, y = [], y0
    while y < y1 - 1e-9:
        ys.append(round(y, 5))
        y += dense if any(a <= y <= b for a, b in DENSE) else base
    ys.append(round(y1, 5))
    return ys
for z in [float(v) for v in range(1, 24, 2)]:
    pts = []
    for y in y_samples(hf.Y_BULB, hf.Y_TR):
        x = hf.x_at_z(y, z)
        if x is not None and x > 0.35:     # 판폭 미만 꼬리는 스템바/종단 윤곽에 흡수
            pts.append((x, y, z))
        elif len(pts) >= 3:
            break
        else:
            pts = []
    if len(pts) >= 3:
        name = f"Str_{z:05.2f}"
        add_poly(name, pts, col_lg)
        report["longitudinals"].append({"name": name,
                                        "pts": [[round(a,6), round(b,6), round(c,6)] for a, b, c in pts]})

report["counts"] = {"frames": len(report["frames"]), "longitudinals": len(report["longitudinals"]),
                    "meshes": len(bpy.data.meshes), "h": h, "n_per_block": n_per}
report["blocks"] = {hf.block_name(i): sum(1 for f in report["frames"] if f["block"] == hf.block_name(i))
                    for i in range(hf.N_BLK)}
json.dump(report, open(os.path.join(ROOT, "build", "cage_report.json"), "w"), ensure_ascii=False)
BLEND = os.path.expanduser(hf.SPEC["meta"]["output"]["blend"])
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print(f"[build_cage] 늑골 {report['counts']['frames']} (h={h:.4f}) · 종통재 {report['counts']['longitudinals']}"
      f" · 메시 {report['counts']['meshes']} · 배분 {report['blocks']}")
