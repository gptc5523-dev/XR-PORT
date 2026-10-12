#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""AI 데이터셋 v2 자체검사 — gen_ai.py 를 돌린 뒤 실행한다. 실패하면 종료코드 1.
분할 시드 분리 · 라벨 분포 · 라벨↔알람 규칙 · 가동범위 · 시각 단조 · 기준셋 개수·해시·라벨을 검사한다."""
import csv, gzip, hashlib, json, os, sys

D = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(D, "ai_data")
sys.path.insert(0, D)
import gen_ai as G  # noqa: E402

fails = []
def check(ok, msg):
    if not ok: fails.append(msg)

m = json.load(open(os.path.join(OUT, "manifest.json"), encoding="utf-8"))
runs = m["runs"]

seeds = {}
for r in runs: seeds.setdefault(r["split"], set()).add(r["seed"])
names = sorted(seeds)
for i, a in enumerate(names):
    for b in names[i + 1:]: check(not (seeds[a] & seeds[b]), f"시드 겹침 {a}↔{b}")

for sp in ("train", "val", "test"):
    v = m["splits"].get(sp); check(v is not None, f"분할 없음 {sp}")
    if v is None: continue
    tot = sum(v["state_rows"][s] for s in ("0", "1", "2"))
    for s, lo, hi in (("0", 0.5, 0.97), ("1", 0.02, 0.4), ("2", 0.005, 0.3)):
        share = v["state_rows"][s] / tot
        check(lo <= share <= hi, f"{sp} 상태 {s} 비율 {share:.3f} 범위 밖 [{lo},{hi}]")
    for c in G.CAUSES: check(v["episodes"].get(c, 0) > 0, f"{sp} 원인 없음 {c}")

rng_m = {"STS": G.g.RANGE, "RTG": G.g.RTG_RANGE}
sev = {c: e["sev"] for c, e in G._cb.items()}
for r in runs:
    path = os.path.join(OUT, r["csv"])
    with gzip.open(path, "rb") as f: data = f.read()
    check(hashlib.sha256(data).hexdigest() == r["sha256"], f"{r['id']} 해시 불일치")
    rows = list(csv.DictReader(data.decode("utf-8").splitlines()))
    check(len(rows) == r["rows"], f"{r['id']} 행 수")
    t_prev, bad_rule, bad_rng = -1, 0, 0
    for x in rows:
        t = int(x["t_ms"]); check(t > t_prev, f"{r['id']} 시각 역행 {t}"); t_prev = t
        y = int(x["y_state"])
        if x["COM_Link_Status"] == "1" and y >= 0:   # 통신 정상 행 — 알람 sev≥2 면 반드시 이상, sev1 이면 주의 이상
            code = int(x["ALM_Latest_Code"])
            if code and y < G.sev_state(sev[code]): bad_rule += 1
            if y >= 1 and int(x["y_code"]) and not code: bad_rule += 1
        for ax, k in (("GT", "gt"), ("TR", "tr"), ("HO", "ho")):
            p = float(x[ax + "_Position"])
            if not (-1e-6 <= p <= rng_m[r["crane"]][k] + 1e-6): bad_rng += 1
    check(bad_rule == 0, f"{r['id']} 라벨↔알람 규칙 위반 {bad_rule}행")
    check(bad_rng == 0, f"{r['id']} 가동범위 밖 {bad_rng}행")
    if fails and len(fails) > 20: break

want = {"kpi5": (100, {"정상": 34, "주의": 33, "이상": 33}), "kpi7": (100, {"정상": 25, "주의": 25, "이상": 50}),
        "ext": (600, {"정상": 200, "주의": 200, "이상": 200})}
for name, (n, by) in want.items():
    d = os.path.join(OUT, "bench", name)
    cases = [json.loads(l) for l in open(os.path.join(d, "cases.jsonl"), encoding="utf-8")]
    check(len(cases) == n, f"기준셋 {name} {len(cases)}건 ≠ {n}")
    for st, k in by.items(): check(sum(c["state_ko"] == st for c in cases) == k, f"기준셋 {name} {st} 개수")
    check(sum(c["hard"] for c in cases) >= 0.25 * n, f"기준셋 {name} 어려운 사례 25% 미만")
    for c in cases:
        with gzip.open(os.path.join(d, c["clip"]), "rb") as f: data = f.read()
        check(hashlib.sha256(data).hexdigest() == c["sha256"], f"{c['id']} 해시 불일치")
        rows = list(csv.DictReader(data.decode("utf-8").splitlines()))
        check(len(rows) == G.CLIP, f"{c['id']} 클립 길이 {len(rows)}")
        check(int(rows[-1]["y_state"]) == c["state"], f"{c['id']} 라벨 불일치")
    if name == "ext": check(sum(c["profile"] == "B" for c in cases) == 300, "ext B 프로파일 300건 아님")
ids = [c["source"] + str(c["row"]) for name in want for c in
       (json.loads(l) for l in open(os.path.join(OUT, "bench", name, "cases.jsonl"), encoding="utf-8"))]
check(len(ids) == len(set(ids)), "기준셋끼리 같은 사례 중복")

for f in fails[:30]: print("FAIL", f)
print(f"[verify_ai] 런 {len(runs)} · 기준셋 {sum(v[0] for v in want.values())}건 — {'PASS' if not fails else f'FAIL {len(fails)}건'}")
sys.exit(1 if fails else 0)
