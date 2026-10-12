#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""지표 3(멀티유저 동기화 정확도) 판정 — 호스트·관전자 Kpi3SyncLog CSV 를 Netcode ServerTime 으로 맞대어 시행마다 일치 여부를 낸다."""

import argparse
import bisect
import csv
import glob
import json
import os
import sys
from datetime import datetime

CONT = ["gantry_m", "trolley_m", "hoist_m"]
DISC = ["alarm", "opmode", "locked", "attached"]


def load(path):
    with open(path, newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def host_index(rows):
    """크레인별 (시각 목록, 행 목록) — 시각 오름차순."""
    by = {}
    for r in rows:
        by.setdefault(r["crane"], []).append(r)
    out = {}
    for c, rs in by.items():
        rs.sort(key=lambda r: float(r["server_time_s"]))
        out[c] = ([float(r["server_time_s"]) for r in rs], rs)
    return out


def matches(sample, ts, rs, grace_s, tol_m):
    """관전자 표본이 [t−grace, t] 사이 어느 순간의 호스트 상태와 같으면 일치 — 네트워크 지연만큼은 늦어도 된다."""
    t = float(sample["server_time_s"])
    lo, hi = bisect.bisect_left(ts, t - grace_s), bisect.bisect_right(ts, t)
    window = rs[max(0, lo - 1):hi]
    if not window:
        return False, {}
    err = {}
    for k in CONT:
        v = float(sample[k])
        best = min(abs(v - float(h[k])) for h in window)
        for a, b in zip(window, window[1:]):   # 두 표본 사이를 지나갔으면(보간) 0
            if (float(a[k]) - v) * (float(b[k]) - v) <= 0:
                best = 0.0
        err[k] = best
    ok = all(err[k] <= tol_m for k in CONT) and all(any(sample[k] == h[k] for h in window) for k in DISC)
    return ok, err


def main():
    ap = argparse.ArgumentParser(description="지표3 멀티유저 동기화 정확도 판정")
    ap.add_argument("logs", nargs="*", help="Kpi3SyncLog CSV(생략하면 KPI/kpi3_log_*.csv 전부)")
    ap.add_argument("--grace-s", type=float, default=0.5, help="허용 지연(초)")
    ap.add_argument("--tol-m", type=float, default=0.05, help="위치 허용오차(m, 지표2 와 같다)")
    ap.add_argument("--target", type=float, default=95.0, help="1차년도 목표(%)")
    ap.add_argument("--settle-s", type=float, default=0.0, help="관전자마다 첫 기록 뒤 이 초만큼은 판정에서 뺀다(접속 직후 초기 정렬)")
    args = ap.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    files = args.logs or sorted(glob.glob(os.path.join(root, "KPI", "kpi3_log_*.csv")))
    rows = [r for f in files for r in load(f)]
    host = host_index([r for r in rows if r["role"] == "host"])
    clients = [r for r in rows if r["role"] == "client"]
    first = {}
    for r in clients:
        first[r["client_id"]] = min(first.get(r["client_id"], float("inf")), float(r["server_time_s"]))
    settled = [r for r in clients if float(r["server_time_s"]) >= first[r["client_id"]] + args.settle_s]
    skipped = len(clients) - len(settled)
    clients = settled
    if not host or not clients:
        sys.exit(f"호스트·관전자 로그가 둘 다 있어야 한다 (파일 {len(files)}개, 호스트 {sum(len(v[1]) for v in host.values())}행, 관전자 {len(clients)}행)")

    out = os.path.join(root, "KPI", f"kpi3_{datetime.now():%Y%m%d_%H%M%S}.csv")
    n = ok_n = 0
    with open(out, "w", newline="", encoding="utf-8-sig") as fo:
        w = csv.writer(fo)
        w.writerow(["trial", "client", "crane", "server_time_s"] + [f"err_{k}" for k in CONT] + ["pass"])
        for s in clients:
            if s["crane"] not in host:
                continue
            ts, rs = host[s["crane"]]
            ok, err = matches(s, ts, rs, args.grace_s, args.tol_m)
            n += 1
            ok_n += ok
            w.writerow([n, s["client_id"], s["crane"], s["server_time_s"]] + [round(err.get(k, -1), 4) for k in CONT] + [int(ok)])
    pct = ok_n * 100 / n if n else 0
    summary = {"trials": n, "pass": ok_n, "percent": round(pct, 1), "target": args.target,
               "verdict": "PASS" if pct >= args.target else "FAIL", "grace_s": args.grace_s, "tol_m": args.tol_m,
               "settle_s": args.settle_s, "skipped_settle": skipped,
               "clients": sorted({s["client_id"] for s in clients}), "logs": files, "csv": out}
    print(json.dumps(summary, ensure_ascii=False, indent=1))
    sys.exit(0 if summary["verdict"] == "PASS" else 1)


if __name__ == "__main__":
    main()
