#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""통합서버 장시간·다중 접속 부하 시험(WBS 2.13) — 서버를 새 DB 로 띄우고 실속도 PLC 쓰기 + XR 폴링을 걸어 30초마다 기록한다."""

import argparse
import csv
import json
import os
import statistics
import subprocess
import sys
import threading
import time
import urllib.parse
import urllib.request
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
CRANES = ["STS_Crane", "RTG 크레인_1", "RTG 크레인_2"]   # 씬의 크레인 3대 — 각자 PLC 1대씩
FEED_CSV = os.path.join(ROOT, "PlcSim", "output", "S02", "run_01.csv")


class Stats:
    """구간 집계 — 지연(ms)·실패·요청 수를 스레드 안전하게 모은다."""

    def __init__(self):
        self.lock = threading.Lock()
        self.reset()

    def reset(self):
        self.lat = {"read": [], "write": [], "time": []}
        self.fail = {"read": 0, "write": 0, "time": 0}
        self.errors = {}

    def add(self, kind, ms=None, err=None):
        with self.lock:
            if err is None:
                self.lat[kind].append(ms)
            else:
                self.fail[kind] += 1
                self.errors[err] = self.errors.get(err, 0) + 1

    def take(self):
        with self.lock:
            out = (self.lat, self.fail, self.errors)
            self.reset()
            return out


def pct(v, p):
    if not v:
        return 0.0
    s = sorted(v)
    return s[min(len(s) - 1, max(0, int(round(p / 100 * len(s) + 0.5)) - 1))]


def request(url, data=None, timeout=5):
    req = urllib.request.Request(url, data, {"Content-Type": "application/json"} if data else {})
    if os.environ.get("XRCRANE_TOKEN"): req.add_header("X-Auth-Token", os.environ["XRCRANE_TOKEN"])
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read(), r.headers


def writer(base, crane, rows, stats, stop, written):
    """PLC 1대 — 100ms 마다 한 행(실 PLC 스캔 주기), 서버가 받은 행 수를 센다."""
    i, nxt = 0, time.monotonic()
    while not stop.is_set():
        row = dict(rows[i % len(rows)], crane=crane, source="load_test", t_ms=int(time.time() * 1000), plc_ms=int(time.time() * 1000))
        t0 = time.perf_counter()
        try:
            request(base + "/ingest", json.dumps(row).encode("utf-8"))
            stats.add("write", (time.perf_counter() - t0) * 1000)
            written[crane] += 1
        except Exception as e:
            stats.add("write", err=type(e).__name__)
        i += 1
        nxt += 0.1
        time.sleep(max(0.0, nxt - time.monotonic()))


def poller(base, crane, stats, stop, got, poll_s):
    """XR 단말의 크레인 1대분 ServerPlcSource — 50ms 마다 /since, 받은 행 수를 센다."""
    q = urllib.parse.quote(crane)
    last = None
    while not stop.is_set():
        t0 = time.perf_counter()
        try:
            body, h = request(f"{base}/since?crane={q}" + (f"&after_id={last}" if last is not None else ""))
            stats.add("read", (time.perf_counter() - t0) * 1000)
            lid = h.get("X-Last-Id")
            if last is not None:
                got[crane] += max(0, body.count(b"\n") - 1)
            if lid and lid != "None":
                last = int(lid)
        except Exception as e:
            stats.add("read", err=type(e).__name__)
        time.sleep(max(0.0, poll_s - (time.perf_counter() - t0)))


def clock(base, stats, stop):
    """XR 단말 1대의 시계 맞추기 — 10초마다 /time 5번(ServerPlcSource 와 같다)."""
    while not stop.wait(10):
        for _ in range(5):
            t0 = time.perf_counter()
            try:
                request(base + "/time")
                stats.add("time", (time.perf_counter() - t0) * 1000)
            except Exception as e:
                stats.add("time", err=type(e).__name__)


def size_mb(p):
    return os.path.getsize(p) / 1e6 if os.path.exists(p) else 0.0


def proc_sample(pid):
    try:
        rss = int(subprocess.check_output(["ps", "-o", "rss=", "-p", str(pid)]).strip()) / 1024
    except Exception:
        rss = -1
    try:
        fds = len(subprocess.check_output(["lsof", "-p", str(pid)], stderr=subprocess.DEVNULL).splitlines()) - 1
    except Exception:
        fds = -1
    return rss, fds


def main():
    ap = argparse.ArgumentParser(description="통합서버 장시간·다중 접속 부하 시험(WBS 2.13)")
    ap.add_argument("--minutes", type=float, default=30)
    ap.add_argument("--clients", type=int, default=5, help="XR 단말 수(계획서 동시 접속 2~5명)")
    ap.add_argument("--poll-ms", type=int, default=50, help="단말의 /since 주기 — ServerPlcSource.PollMs")
    ap.add_argument("--port", type=int, default=5026)
    ap.add_argument("--interval", type=int, default=30, help="기록 간격(초)")
    ap.add_argument("--server-args", default="", help="서버에 더 넘길 인자(예: --retain-days 7)")
    args = ap.parse_args()

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_dir = os.path.join(ROOT, "KPI")
    os.makedirs(out_dir, exist_ok=True)
    db = os.path.join(ROOT, "Temp", f"soak_{stamp}.db")
    os.makedirs(os.path.dirname(db), exist_ok=True)
    base = f"http://127.0.0.1:{args.port}"

    srv = subprocess.Popen([sys.executable, os.path.join(HERE, "xrcrane_db.py"), "--db", db, "serve", "--host", "127.0.0.1",
                            "--port", str(args.port)] + args.server_args.split(), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(50):
        try:
            request(base + "/time", timeout=1)
            break
        except Exception:
            time.sleep(0.1)
    else:
        srv.kill()
        sys.exit("서버가 뜨지 않음")

    with open(FEED_CSV, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    stats, stop = Stats(), threading.Event()
    written = {c: 0 for c in CRANES}
    got = {(k, c): 0 for k in range(args.clients) for c in CRANES}
    threads = [threading.Thread(target=writer, args=(base, c, rows, stats, stop, written), daemon=True) for c in CRANES]
    for k in range(args.clients):
        for c in CRANES:
            g = {c: 0}
            threads.append(threading.Thread(target=poller, args=(base, c, stats, stop, g, args.poll_ms / 1000), daemon=True))
            got[(k, c)] = g
        threads.append(threading.Thread(target=clock, args=(base, stats, stop), daemon=True))
    for t in threads:
        t.start()

    series = os.path.join(out_dir, f"server_soak_{stamp}.csv")
    cols = ["t_s", "read_n", "read_fail", "read_p50_ms", "read_p95_ms", "read_max_ms", "write_n", "write_fail", "write_p95_ms",
            "write_max_ms", "time_fail", "rss_mb", "fds", "db_mb", "wal_mb", "rows_written", "errors"]
    total = {"read": 0, "write": 0, "time": 0}
    fails = {"read": 0, "write": 0, "time": 0}
    all_read, all_write, peak = [], [], {"rss_mb": 0, "fds": 0, "wal_mb": 0}
    first = None
    t_start = time.monotonic()
    end = t_start + args.minutes * 60
    print(f"[soak] {args.minutes:g}분 · 단말 {args.clients}대 × 크레인 {len(CRANES)}대 폴링 {args.poll_ms}ms · PLC {len(CRANES)}대 100ms 쓰기 → {series}", flush=True)
    with open(series, "w", newline="", encoding="utf-8") as fo:
        w = csv.writer(fo)
        w.writerow(cols)
        while time.monotonic() < end and srv.poll() is None:
            time.sleep(min(args.interval, max(0.1, end - time.monotonic())))
            lat, fail, errs = stats.take()
            rss, fds = proc_sample(srv.pid)
            db_mb, wal_mb = size_mb(db), size_mb(db + "-wal")
            for k in total:
                total[k] += len(lat[k]) + fail[k]
                fails[k] += fail[k]
            all_read += lat["read"]; all_write += lat["write"]
            peak = {"rss_mb": max(peak["rss_mb"], rss), "fds": max(peak["fds"], fds), "wal_mb": max(peak["wal_mb"], wal_mb)}
            row = [round(time.monotonic() - t_start), len(lat["read"]), fail["read"], round(pct(lat["read"], 50), 1), round(pct(lat["read"], 95), 1),
                   round(max(lat["read"], default=0), 1), len(lat["write"]), fail["write"], round(pct(lat["write"], 95), 1),
                   round(max(lat["write"], default=0), 1), fail["time"], round(rss, 1), fds, round(db_mb, 2), round(wal_mb, 2),
                   sum(written.values()), ";".join(f"{k}:{v}" for k, v in errs.items())]
            first = first or row
            w.writerow(row)
            fo.flush()
            print("[soak] " + " ".join(f"{c}={v}" for c, v in zip(cols, row)), flush=True)

    stop.set()
    time.sleep(1)
    alive = srv.poll() is None
    lag = {f"{k}:{c}": written[c] - got[(k, c)][c] for k in range(args.clients) for c in CRANES}
    srv.terminate()
    elapsed = (time.monotonic() - t_start) / 60
    summary = {
        "minutes": round(elapsed, 1), "clients": args.clients, "cranes": len(CRANES), "poll_ms": args.poll_ms,
        "server_alive_at_end": alive,
        "requests": total, "failures": fails,
        "read_ms": {"p50": round(pct(all_read, 50), 1), "p95": round(pct(all_read, 95), 1), "p99": round(pct(all_read, 99), 1), "max": round(max(all_read, default=0), 1)},
        "write_ms": {"p50": round(pct(all_write, 50), 1), "p95": round(pct(all_write, 95), 1), "max": round(max(all_write, default=0), 1)},
        "rows_written": sum(written.values()),
        "max_rows_missed_by_a_client": max(lag.values()) if lag else 0,
        "rss_mb": {"start": first[11] if first else None, "end": row[11] if first else None, "peak": round(peak["rss_mb"], 1)},
        "fds": {"start": first[12] if first else None, "end": row[12] if first else None, "peak": peak["fds"]},
        "db_mb_end": round(size_mb(db), 2), "wal_mb_peak": round(peak["wal_mb"], 2),
        "db_mb_per_hour": round(size_mb(db) / max(elapsed / 60, 1e-6), 1),
        "series_csv": series,
    }
    ok = alive and sum(fails.values()) == 0 and summary["max_rows_missed_by_a_client"] <= len(CRANES) * 2
    summary["verdict"] = "PASS" if ok else "FAIL"
    with open(series.replace(".csv", "_summary.json"), "w", encoding="utf-8") as fo:
        json.dump(summary, fo, ensure_ascii=False, indent=1)
    print("[soak] " + json.dumps(summary, ensure_ascii=False), flush=True)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
