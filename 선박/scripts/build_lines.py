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
col_st = bpy.data.collections.new("Lines_Stations"); root.children.link(col_st)
col_lg = bpy.data.collections.new("Lines_Long");     root.children.link(col_lg)

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
    pts = [(x, y, z) for x, z in sec]
    name = f"St_{y:+08.2f}"
    add_poly(name, pts, col_st)
    report["stations"].append({"name": name, "y": y, "pts": [[round(x,6), round(z,6)] for x, z in sec]})

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

def add_long(name, pts):
    if len(pts) < 3: return
    add_poly(name, pts, col_lg)
    report["longitudinals"].append({"name": name, "pts": [[round(a,6), round(b,6), round(c,6)] for a, b, c in pts]})

# 킬/중심선 프로필 (벌브 코끝 → 트랜섬 하단)
add_long("CL_Keel", [(0.0, y, hf.z_keel(y)) for y in y_samples(hf.Y_BULB, hf.Y_TR, DENSE)])
# 갑판 현측선 (시어)
add_long("Deck_Edge", [(hf.half_deck(y), y, hf.sheer(y)) for y in y_samples(hf.Y_STEM, hf.Y_TR, DENSE)])
# 워터라인
for z in hf.waterline_zs():
    pts = []
    for y in y_samples(hf.Y_BULB, hf.Y_TR, DENSE, base=1.5):
        x = hf.x_at_z(y, z)
        if x is not None and x > 0.2:      # 반폭 0.2m 미만 꼬리는 스템/종단 윤곽에 흡수(제도 해상도)
            pts.append((x, y, z))
        elif len(pts) >= 3:
            break                      # 단일 구간만 (뒤쪽 재진입 방지)
        else:
            pts = []
    add_long(f"WL_{z:05.2f}", pts)
# 버톡 라인
for xb in hf.buttock_xs():
    pts = []
    for y in y_samples(hf.Y_BULB, hf.Y_TR, DENSE, base=1.5):
        zz = hf.z_at_x(y, xb)
        if zz is not None:
            pts.append((xb, y, zz))
        elif len(pts) >= 3:
            break
        else:
            pts = []
    add_long(f"BUT_{xb:05.2f}", pts)

# ── 저장 + 리포트 ───────────────────────────────────────────────────────
os.makedirs(os.path.join(ROOT, "build"), exist_ok=True)
report["counts"] = {"stations": len(report["stations"]), "longitudinals": len(report["longitudinals"]),
                    "objects": len(bpy.data.objects), "meshes": len(bpy.data.meshes)}
json.dump(report, open(os.path.join(ROOT, "build", "lines_report.json"), "w"), ensure_ascii=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "build", "ship.blend"))
print(f"[build_lines] stations {report['counts']['stations']} · long {report['counts']['longitudinals']}"
      f" · meshes {report['counts']['meshes']} · saved build/ship.blend")
