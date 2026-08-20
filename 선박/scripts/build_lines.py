#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""선도(lines plan) 빌더 — 커브만 생성(메시 0). 실행:
blender --background --factory-startup --python scripts/build_lines.py
산출: build/ship.blend + build/lines_report.json (씬 실측 → 게이트 입력)"""
import bpy, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
import hull_form as hf

# ── 씬 초기화 (기본 오브젝트 제거) ──────────────────────────────────────
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
for coll in list(bpy.data.collections):
    bpy.data.collections.remove(coll)
for blk_set in (bpy.data.meshes, bpy.data.cameras, bpy.data.lights, bpy.data.materials):
    for blk in list(blk_set):
        blk_set.remove(blk)          # 팩토리 씬 잔재(큐브 메시 등) 고아 데이터 제거

scene = bpy.context.scene
root = bpy.data.collections.new("ContainerShip_Lines")
scene.collection.children.link(root)
# 블록별 스테이션 하위 컬렉션 (Lines_B1…B5) — 종방향 선은 전선 공통 최상위
col_blk = {}
for i in range(hf.N_BLK):
    nm = hf.block_name(i)
    c = bpy.data.collections.new(f"Lines_{nm}"); root.children.link(c)
    col_blk[nm] = c
# 롤케익 절단(스펙 cut_rule): 종방향 선도 경계에서 잘라 블록 컬렉션에 담는다 — 별도 Long 컬렉션 없음

def add_poly(name, pts, coll):
    """pts: [(x,y,z)...] → POLY 커브 오브젝트"""
    cu = bpy.data.curves.new(name, type='CURVE')
    cu.dimensions = '3D'
    sp = cu.splines.new('POLY')
    sp.points.add(len(pts) - 1)
    for p, (x, y, z) in zip(sp.points, pts):
        p.co = (x, y, z, 1.0)
    ob = bpy.data.objects.new(name, cu)
    coll.objects.link(ob)
    return ob

report = {"stations": [], "longitudinals": []}

# ── 스테이션 (횡단면 커브, 우현) ────────────────────────────────────────
st_ys = hf.station_ys()
for y in st_ys:
    sec = hf.section(y)
    if len(sec) < 2: continue
    blk = hf.block_of(y)
    pts = [(x, y, z) for x, z in sec]
    name = f"St_{y:+08.2f}"
    add_poly(name, pts, col_blk[blk])
    report["stations"].append({"name": name, "y": y, "block": blk,
                               "pts": [[round(x,6), round(z,6)] for x, z in sec]})

# ── 종방향 곡선 샘플링 (끝단·개구는 조밀) ───────────────────────────────
def y_samples(y0, y1, dense_zones, base=1.5, dense=0.4):
    ys, y = [], y0
    while y < y1 - 1e-9:
        ys.append(round(y, 5))
        step = dense if any(a <= y <= b for a, b in dense_zones) else base
        y += step
    ys.append(round(y1, 5))
    return ys

DENSE = [(hf.ST["skeg"]["y_aft"] - 4.0, hf.Y_TR), (hf.Y_BULB, hf.Y_FP + 12.0),
         (hf.FK["y_aft"] - 4.0, hf.FK["y_aft"] + 16.0)]   # 버톡 종단(빌지 이탈부) 조밀

def split_by_blocks(pts, evalf):
    """롤케익 절단 — 경계 y 를 지나는 곳에서 정확한 경계점(수식 평가)을 양쪽에 공유시켜 자른다."""
    inner = hf.BOUNDS[1:-1]
    segs, cur = [], [pts[0]]
    for p0, p1 in zip(pts, pts[1:]):
        for b in inner:
            if p0[1] < b - 1e-9 and p1[1] > b + 1e-9:
                bp = evalf(b)
                if bp is not None:
                    cur.append(bp); segs.append(cur); cur = [bp]
        cur.append(p1)
        if any(abs(p1[1] - b) < 1e-9 for b in inner):
            segs.append(cur); cur = [p1]
    if len(cur) > 1: segs.append(cur)
    return segs

def add_long(name, pts, evalf):
    if len(pts) < 3: return
    for seg in split_by_blocks(pts, evalf):
        if len(seg) < 2: continue
        blk = hf.block_of((seg[0][1] + seg[-1][1]) / 2.0)
        segname = f"{name}_{blk}"
        add_poly(segname, seg, col_blk[blk])
        report["longitudinals"].append({"name": name, "seg": segname, "block": blk,
            "pts": [[round(a,6), round(b,6), round(c,6)] for a, b, c in seg]})

# 킬/중심선 프로필 (벌브 코끝 → 트랜섬 하단)
ev_cl = lambda y: (0.0, y, hf.z_keel(y))
add_long("CL_Keel", [ev_cl(y) for y in y_samples(hf.Y_BULB, hf.Y_TR, DENSE)], ev_cl)
# 갑판 현측선 (시어)
ev_dk = lambda y: (hf.half_deck(y), y, hf.sheer(y))
add_long("Deck_Edge", [ev_dk(y) for y in y_samples(hf.Y_STEM, hf.Y_TR, DENSE)], ev_dk)
# 워터라인
for z in hf.waterline_zs():
    def ev_wl(y, z=z):
        x = hf.x_at_z(y, z)
        return (x, y, z) if x is not None else None
    pts = []
    for y in y_samples(hf.Y_BULB, hf.Y_TR, DENSE, base=1.5):
        x = hf.x_at_z(y, z)
        if x is not None and x > 0.2:      # 반폭 0.2m 미만 꼬리는 스템/종단 윤곽에 흡수(제도 해상도)
            pts.append((x, y, z))
        elif len(pts) >= 3:
            break                      # 단일 구간만 (뒤쪽 재진입 방지)
        else:
            pts = []
    add_long(f"WL_{z:05.2f}", pts, ev_wl)
# 버톡 라인
for xb in hf.buttock_xs():
    def ev_bt(y, xb=xb):
        zz = hf.z_at_x(y, xb)
        return (xb, y, zz) if zz is not None else None
    pts = []
    for y in y_samples(hf.Y_BULB, hf.Y_TR, DENSE, base=1.5):
        zz = hf.z_at_x(y, xb)
        if zz is not None:
            pts.append((xb, y, zz))
        elif len(pts) >= 3:
            break
        else:
            pts = []
    add_long(f"BUT_{xb:05.2f}", pts, ev_bt)

# ── 저장 + 리포트 ───────────────────────────────────────────────────────
os.makedirs(os.path.join(ROOT, "build"), exist_ok=True)
report["counts"] = {"stations": len(report["stations"]), "longitudinals": len(report["longitudinals"]),
                    "objects": len(bpy.data.objects), "meshes": len(bpy.data.meshes)}
report["blocks"] = {"bounds": hf.BOUNDS,
                    "alloc": {hf.block_name(i): sum(1 for s in report["stations"] if s["block"] == hf.block_name(i))
                              for i in range(hf.N_BLK)}}
json.dump(report, open(os.path.join(ROOT, "build", "lines_report.json"), "w"), ensure_ascii=False)
BLEND = os.path.expanduser(hf.SPEC["meta"]["output"]["blend"])
os.makedirs(os.path.dirname(BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print(f"[build_lines] stations {report['counts']['stations']} · long {report['counts']['longitudinals']}"
      f" · meshes {report['counts']['meshes']} · saved {BLEND}")
