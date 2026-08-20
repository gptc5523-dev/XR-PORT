#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""선형 수식 SSOT — spec/ship_spec.json 에서 전량 파생. bpy 무관(게이트에서도 임포트).
좌표: 선수 -Y · 원점 = 미드십×중심선×기선(z0=선저) · 우현 +X. 반폭(half-breadth)만 정의(우현).
"""
import json, math, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = json.load(open(os.path.join(ROOT, "spec", "ship_spec.json"), encoding="utf-8"))
P, E, H, PR = SPEC["principal"], SPEC["envelope"], SPEC["hull"], SPEC["propulsion"]

LOA, LPP, B, D, T = P["LOA"], P["LPP"], P["B"], P["D"], P["T_design"]
HB = B / 2.0
Y_STEM   = E["y_stem_deck"]        # 갑판 스템 끝 (-147)
Y_BULB   = E["y_bulb_tip"]         # 벌브 코끝 (-149.8)
Y_TR     = E["y_transom_deck"]     # 트랜섬 (+144.2)
Y_WL_AFT = E["y_wl_aft_end"]       # 수선 후단 (+136)
Y_FP, Y_AP = E["y_FP"], E["y_AP"]
PM  = H["parallel_midbody"];  FK = H["flat_keel"]
BB  = H["bulbous_bow"];       ST = H["stern"]
BR  = H["bilge_radius"]
Z_BULB, R_BULB = BB["axis_z"], BB["max_radius"]
Y_STEM_WL = Y_FP - 3.2             # 스템이 흘수선과 만나는 y (선수 프로필과 일치)
PROP = PR["propeller"]
R_PROP = PROP["diameter"] / 2.0
Z_SHAFT = PROP["shaft_z"]
Y_PROP  = PROP["y_center"]
APERTURE_CLR = 0.10 * PROP["diameter"]   # 개구 상단 여유 (게이트 L10)

def _smooth(t):
    """quintic smootherstep — 양끝 곡률 0 (양끝에 각짐을 안 남긴다)"""
    t = max(0.0, min(1.0, t))
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0)

# ── 종방향 기본 곡선 ────────────────────────────────────────────────────
def sheer(y):
    """주갑판 시어(현호) 높이 z."""
    if y < PM["y_fwd"]:
        t = (PM["y_fwd"] - y) / (PM["y_fwd"] - Y_STEM)
        return D + H["sheer_fwd"] * t * t
    if y > PM["y_aft"]:
        t = (y - PM["y_aft"]) / (Y_TR - PM["y_aft"])
        return D + H["sheer_aft"] * t * t
    return D

def half_deck(y):
    """갑판 반폭."""
    if y <= Y_STEM: return 0.0
    if y < PM["y_fwd"]:
        t = (PM["y_fwd"] - y) / (PM["y_fwd"] - Y_STEM)
        return HB * max(0.0, 1.0 - t ** H["entrance_exponent"])
    if y > PM["y_aft"]:
        t = (y - PM["y_aft"]) / (Y_TR - PM["y_aft"])
        return ST["transom_half_width"] + (HB - ST["transom_half_width"]) * (1 - t * t)
    return HB

def half_wl(y):
    """설계 흘수선(z=T) 반폭. 수선 후단 Y_WL_AFT 는 zk=T 에서 유도(아래서 재계산)."""
    if y <= Y_STEM_WL or y >= Y_WL_AFT: return 0.0
    if y < PM["y_fwd"]:
        t = (PM["y_fwd"] - y) / (PM["y_fwd"] - Y_STEM_WL)
        return HB * max(0.0, 1.0 - t ** 2.6)
    if y > PM["y_aft"]:
        t = (y - PM["y_aft"]) / (Y_WL_AFT - PM["y_aft"])
        return HB * max(0.0, 1.0 - t ** 2.2)
    return HB

def bulb_r(y):
    """벌브 국소 반경 (타원 프로파일, 중심 Y_FP-0.5)."""
    yc = Y_FP - 0.5
    if y < yc:                       # 전방 반타원 (코끝까지)
        L = yc - Y_BULB
        u = (yc - y) / L
    else:                            # 후방 반타원 (선체로 흡수 — 어깨를 완만하게)
        L = 10.0
        u = (y - yc) / L
    if u >= 1.0: return 0.0
    return R_BULB * math.sqrt(1.0 - u * u)

STERN_RUN_EXP = 2.45   # 나선(bare hull) 선미 런 지수 — zk(Y_PROP) ≥ 개구 상단이 되게 선정

def z_keel(y):
    """중심선 바닥 프로필 — **나선(bare hull)만**. 스케그·개구는 별도 부재 단계
    (구판 규약: 선미 추진계통은 ContainerShip_Stern 별도 컬렉션 — 선도에 안 섞는다)."""
    if y <= FK["y_fwd"]:             # 선수쪽
        zb_low = Z_BULB - bulb_r(y) if bulb_r(y) > 1e-9 else Z_BULB
        if y <= Y_FP:                # FP 앞은 벌브 하연이 바닥
            return zb_low
        t = (FK["y_fwd"] - y) / (FK["y_fwd"] - Y_FP)
        return (Z_BULB - R_BULB) * _smooth(t) ** 1.5
    if y <= FK["y_aft"]:
        return 0.0
    # 선미 런: 평탄킬 끝 → 트랜섬 하단, 단조 파워커브(시작 기울기 0 · 각짐 없음)
    t = (y - FK["y_aft"]) / (Y_TR - FK["y_aft"])
    return ST["transom_bottom_z"] * (t ** STERN_RUN_EXP)

def _solve_wl_aft():
    """수선 후단 = zk(y) = T 가 되는 y (이분법). envelope 의 도식값 대신 이걸 쓴다."""
    a, b = FK["y_aft"] + 0.1, Y_TR - 1e-4
    for _ in range(60):
        m = (a + b) / 2.0
        if z_keel(m) < T: a = m
        else: b = m
    return (a + b) / 2.0
Y_WL_AFT = _solve_wl_aft()           # 도식값(E) 대신 유도값으로 덮어씀

def flat_bottom(y):
    """평탄 선저 반폭(빌지 시작점)."""
    fb_max = HB - BR
    if FK["y_fwd"] <= y <= FK["y_aft"]:
        core0, core1 = FK["y_fwd"] + 25.0, FK["y_aft"] - 25.0
        if y < core0:  return fb_max * _smooth((y - FK["y_fwd"]) / 25.0)
        if y > core1:  return fb_max * _smooth((FK["y_aft"] - y) / 25.0)
        return fb_max
    return 0.0

def section_exp(y):
    """단면 충실 지수 n — 미드십 4.5(각형·빌지) → 양끝 V/U."""
    if y < PM["y_fwd"]:
        t = (PM["y_fwd"] - y) / (PM["y_fwd"] - Y_STEM_WL)
        return 4.5 - 2.9 * _smooth(min(1.0, t))
    if y > PM["y_aft"]:
        t = (y - PM["y_aft"]) / (Y_WL_AFT - PM["y_aft"])
        return 4.5 - 2.7 * _smooth(min(1.0, t))
    return 4.5
# (섹션 지수의 선미 분모도 유도된 Y_WL_AFT 를 따른다)

# ── 단면 생성 ───────────────────────────────────────────────────────────
def section(y, n_wl=26):
    """스테이션 단면 폴리라인 [(x,z)...] — 킬(중심)에서 갑판까지, 우현.
    존재하지 않는 y(선체 범위 밖)면 []"""
    if y < Y_BULB or y > Y_TR: return []
    zk, zd = z_keel(y), sheer(y)
    bd = half_deck(y)
    pts = []
    if y < Y_STEM:                    # 벌브 전용 구간 (스템 앞)
        rb = bulb_r(y)
        if rb < 1e-6: return []
        for k in range(n_wl + 1):
            a = math.pi * (k / n_wl - 0.5)   # -90°(하) → +90°(상)
            pts.append((rb * math.cos(a), Z_BULB + rb * math.sin(a)))
        return pts
    bw = half_wl(y)
    fb = flat_bottom(y)
    n = section_exp(y)
    rb = bulb_r(y)
    FLARE = 1.35                      # >1 → WL 에서 접선 연속(수직), 갑판으로 벌어짐
    def blend_bulb(base, z):
        """벌브 p-노름 합성(매끈한 합집합, max() 각짐 방지)"""
        if rb > 1e-6 and abs(z - Z_BULB) < rb:
            xb = math.sqrt(rb * rb - (z - Z_BULB) ** 2)
            p = 6.0
            return (base ** p + xb ** p) ** (1.0 / p) if base > 1e-9 else xb
        return base
    if zk >= T - 1e-6:                # 선미 오버행 단면 — 플레어식과 경계에서 항등
                                      # (선수 스템은 bw=0 인 일반식으로 — 위 분기에 넣으면 u 기준이 달라 종통선이 꺾인다)
        for k in range(n_wl + 1):
            u = k / n_wl
            z = zk + (zd - zk) * u
            pts.append((blend_bulb(bd * (u ** FLARE), z), z))
        return pts
    # 수선 아래: x = fb + (bw-fb)·(1-((T-z)/(T-zk))^n)^(1/n)  + 벌브 합성
    n_low = max(8, int(n_wl * 0.62))
    for k in range(n_low + 1):
        u = k / n_low
        z = zk + (T - zk) * u
        base = fb + (bw - fb) * (1.0 - (1.0 - u) ** n) ** (1.0 / n)
        pts.append((max(0.0, blend_bulb(base, z)), z))
    # 수선 위: 플레어로 갑판 반폭까지 (z=T 에서 접선 연속)
    n_up = n_wl - n_low
    for k in range(1, n_up + 1):
        u = k / n_up
        z = T + (zd - T) * u
        pts.append((blend_bulb(bw + (bd - bw) * (u ** FLARE), z), z))
    return pts

def x_at_z(y, z):
    """높이 z 의 반폭 — **해석식 직접 평가** (폴리라인 보간은 초입 급경사에서
    노드 통과 순간 기울기가 점프해 종통선에 가짜 너클을 만든다). 범위 밖이면 None."""
    if y < Y_BULB or y > Y_TR: return None
    zk, zd = z_keel(y), sheer(y)
    if z < zk - 1e-9 or z > zd + 1e-9: return None
    bd = half_deck(y)
    rb = bulb_r(y)
    FLARE = 1.35
    def bulb(base):
        if rb > 1e-6 and abs(z - Z_BULB) < rb:
            xb = math.sqrt(rb * rb - (z - Z_BULB) ** 2)
            return (base ** 6.0 + xb ** 6.0) ** (1.0 / 6.0) if base > 1e-9 else xb
        return base
    if y < Y_STEM:                    # 벌브 전용
        return bulb(0.0) if rb > 1e-6 else None
    if zk >= T - 1e-6:                # 선미 오버행
        u = (z - zk) / max(1e-12, zd - zk)
        return bulb(bd * (u ** FLARE))
    bw, fb, n = half_wl(y), flat_bottom(y), section_exp(y)
    if z <= T:
        u = (z - zk) / max(1e-12, T - zk)
        base = fb + (bw - fb) * (1.0 - (1.0 - u) ** n) ** (1.0 / n)
        return max(0.0, bulb(base))
    u = (z - T) / max(1e-12, zd - T)
    return bulb(bw + (bd - bw) * (u ** FLARE))

def z_at_x(y, xb):
    """반폭 xb 가 되는 최저 z (버톡 라인용) — 조밀 폴리라인에서 첫 교차."""
    pts = section(y, n_wl=120)
    if not pts or max(p[0] for p in pts) < xb: return None
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        if (x0 - xb) * (x1 - xb) <= 0 and abs(x1 - x0) > 1e-12:
            t = (xb - x0) / (x1 - x0)
            return z0 + (z1 - z0) * t
    return None

# ── 5등분 블록 (스펙 blocks — 오너 지시 2026-08-20) ─────────────────────
BLK = SPEC["blocks"]
N_BLK = BLK["count"]
L_BLK = (Y_TR - Y_BULB) / N_BLK                     # = LOA/5 = 58.8
BOUNDS = [round(Y_BULB + L_BLK * k, 6) for k in range(N_BLK + 1)]   # 선수→선미 오름차순
ERECTION_ORDER = BLK["erection_order"]

def block_name(i):
    """구간 인덱스(0=선수측) → 블록명 (B1=선미)"""
    return f"B{N_BLK - i}"

def block_of(y):
    """y 가 속한 블록명. 내부 경계 위의 값은 선미쪽(+y) 블록에 배속."""
    for i in range(N_BLK):
        if BOUNDS[i] <= y < BOUNDS[i + 1] - 1e-9:
            return block_name(i)
    return block_name(N_BLK - 1) if y >= BOUNDS[N_BLK] - 1e-9 else block_name(0)

# ── 스테이션 배치 ────────────────────────────────────────────────────────
def station_ys():
    """선도 스테이션 y 목록 — 끝단 조밀·중앙 성김, 특이점(AP·FP·트랜섬·벌브) 포함."""
    ys = set()
    def rng(a, b, step):
        n = max(1, round(abs(b - a) / step))
        for i in range(n + 1):
            ys.add(round(a + (b - a) * i / n, 4))
    rng(Y_TR, Y_AP, 1.05)                        # 트랜섬~AP (오버행 — 늑골 자리 ≥3)
    rng(Y_AP, ST["skeg"]["y_aft"], 2.6)          # AP~스케그(예정 자리)
    rng(ST["skeg"]["y_aft"], PM["y_aft"], 4.0)   # 스케그~평행부
    rng(PM["y_aft"], PM["y_fwd"], 8.0)           # 평행 중앙부
    rng(PM["y_fwd"], Y_STEM, 3.5)                # 선수부
    rng(Y_STEM, Y_BULB + 0.4, 1.2)               # 벌브 (코끝 0.4m 전까지)
    for s in (Y_AP, Y_FP, Y_PROP, 0.0):
        ys.add(round(s, 4))
    out = sorted(ys)
    out = [v for i, v in enumerate(out) if i == 0 or abs(v - out[i-1]) > 0.5]
    # 블록 경계 스냅 — 삽입 금지, 가장 가까운 스테이션을 「이동」 (스펙 boundary_rule)
    specials = {round(v, 4) for v in (Y_AP, Y_FP, 0.0, Y_PROP)} | set(BOUNDS)
    for b in BOUNDS[1:-1]:
        i = min(range(len(out)), key=lambda k: abs(out[k] - b))
        out[i] = b
    # 이동으로 생긴 근접쌍 정리 — 특이점(경계·AP·FP·미드십·프로펠러)은 남기고 이웃을 뺀다
    cleaned = []
    for v in sorted(out):
        if cleaned and v - cleaned[-1] <= 0.5:
            if round(cleaned[-1], 4) in specials and round(v, 4) not in specials:
                continue
            if round(v, 4) in specials and round(cleaned[-1], 4) not in specials:
                cleaned[-1] = v
                continue
        cleaned.append(v)
    # 스냅 이동으로 벌어진 간격(>8.0)은 중점으로 채움 (구판 규약 — 경계 삽입과 무관한 보충)
    filled = []
    for v in cleaned:
        while filled and v - filled[-1] > 8.0:
            filled.append(round((filled[-1] + v) / 2.0, 4))
        filled.append(v)
    return sorted(filled, reverse=True)          # 선미(+)→선수(−)

def waterline_zs():
    zs = [1.0, 2.0, 3.3, Z_BULB, 8.0, Z_BULB + R_BULB, 11.0, T, 15.5, 18.0, 21.0, D]
    return sorted(set(round(z, 4) for z in zs))

def buttock_xs():
    return [2.5, 5.0, 7.5, 10.0, 12.5, 15.0, HB - BR]

if __name__ == "__main__":
    ys = station_ys()
    mid = section(0.0)
    print(f"stations {len(ys)}  y∈[{min(ys)}, {max(ys)}]")
    print(f"midship: width@WL {2*x_at_z(0,T):.4f} (B {B})  deck {2*half_deck(0):.4f}  zk {z_keel(0)}")
    print(f"bulb tip r {bulb_r(Y_BULB):.4f}  FP r {bulb_r(Y_FP):.4f}")
    print(f"aperture zk@prop {z_keel(Y_PROP):.4f} vs prop top+clr {Z_SHAFT+R_PROP+APERTURE_CLR:.4f}")
