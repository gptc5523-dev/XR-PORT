#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
XR 크레인 통합서버 v0.5 — PLC 이력 수집·저장·조회. 파트ID 매핑·WebSocket 전파·실 PLC 어댑터는 범위 밖.

의존성 0 — 표준 라이브러리(http.server+sqlite3)만 쓴다. 엔드포인트 5개에 FastAPI 는 과투자.

스키마: PlcSim CSV 45컬럼을 그대로 받는다. 자주 쓰는 6개만 컬럼 승격+인덱스, 나머지는 raw(JSON) 보존
  → 벤더가 태그를 추가해도 스키마 변경 없이 쌓인다.

엔드포인트
  GET  /health                                  살아있는지 + 적재 행 수
  GET  /time                                    서버 벽시계(epoch ms) — 기기 간 시계 차 추정용(지표1)
  POST /ingest                                  스냅샷 1건 또는 배열 적재(JSON)
  GET  /latest?crane=STS_Crane                  가장 최근 스냅샷 1건
  GET  /history?crane=&from_ms=&to_ms=&limit=   구간 조회(기본 최근 500)
  GET  /alarms?crane=&limit=                    알람 코드가 0이 아닌 행만
  GET  /runs                                    적재된 (크레인, 출처) 별 행 수·시간 범위
  GET  /since?crane=&after_id=                  크레인이 이어 받는 경로(CSV, X-Last-Id) — Unity ServerPlcSource

접근 제어(WBS 9.2, 폐쇄망 전제 9.1)
  XRCRANE_ALLOW  허용 대역(쉼표 구분 CIDR). 기본 = 루프백 + 사설망(10/8, 172.16/12, 192.168/16). 밖이면 403.
  XRCRANE_TOKEN  공유 토큰. 설정하면 모든 요청에 X-Auth-Token 헤더가 같아야 한다(아니면 401). 비우면 토큰 검사 안 함.
  클라이언트(feed·plc_s7·load_test·Unity ServerPlcSource)는 같은 환경변수(Unity 는 파일)에서 토큰을 읽어 보낸다.

CLI
  python3 xrcrane_db.py serve [--port 5006] [--db /data/xrcrane.db]
  python3 xrcrane_db.py import <csv...> [--crane STS_Crane] [--source S02/run_01]   기존 PlcSim CSV 적재
  python3 xrcrane_db.py feed <csv> [--url http://127.0.0.1:5006] [--crane STS_Crane] [--loop]   CSV 를 실시간 속도로 /ingest 전송
"""

import argparse
import csv
import hmac
import io
import ipaddress
import json
import os
import sqlite3
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

DEFAULT_DB = os.environ.get("XRCRANE_DB", "/data/xrcrane.db")
DEFAULT_PORT = int(os.environ.get("XRCRANE_PORT", "5006"))
DEFAULT_ALLOW = os.environ.get("XRCRANE_ALLOW", "127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16")
TOKEN_HEADER = "X-Auth-Token"


def auth_headers(extra=None):
    """클라이언트 공용 — XRCRANE_TOKEN 이 있으면 토큰 헤더를 붙인다."""
    h = dict(extra or {})
    if os.environ.get("XRCRANE_TOKEN"):
        h[TOKEN_HEADER] = os.environ["XRCRANE_TOKEN"]
    return h


def parse_allow(spec):
    return [ipaddress.ip_network(s.strip(), strict=False) for s in spec.split(",") if s.strip()]

# 조회용으로 승격한 컬럼 ← CSV 헤더 이름. 나머지 39개는 raw JSON 에 그대로 남는다.
PROMOTED = {
    "gt_position": "GT_Position",
    "tr_position": "TR_Position",
    "ho_position": "HO_Position",
    "ho_load": "HO_Load",
    "op_mode": "OP_Mode",
    "alarm_code": "ALM_Latest_Code",
}

SCHEMA = """
CREATE TABLE IF NOT EXISTS snapshot (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  crane       TEXT    NOT NULL,
  source      TEXT    NOT NULL,          -- 'S02/run_01' 처럼 출처를 남긴다(재현성)
  t_ms        INTEGER NOT NULL,          -- 시나리오 내 경과 시각(PLC 100ms 그리드)
  recv_ms     INTEGER NOT NULL,          -- 서버 수신 시각(epoch ms) — 지표1(지연) 측정의 기준점
  gt_position REAL, tr_position REAL, ho_position REAL,
  ho_load     REAL, op_mode TEXT, alarm_code INTEGER,
  raw         TEXT    NOT NULL           -- 45컬럼 전량(JSON)
);
CREATE INDEX IF NOT EXISTS ix_snap_crane_t ON snapshot(crane, t_ms);
CREATE INDEX IF NOT EXISTS ix_snap_recv    ON snapshot(recv_ms);   -- 보존 기간 삭제용
CREATE INDEX IF NOT EXISTS ix_snap_alarm   ON snapshot(crane, alarm_code) WHERE alarm_code IS NOT NULL AND alarm_code != 0;
"""


def connect(path, schema=True):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    con = sqlite3.connect(path, check_same_thread=False, timeout=5)
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA busy_timeout=5000")     # 쓰기 잠금이 겹치면 실패 대신 5초까지 기다린다
    if schema:   # 요청마다 스키마를 다시 돌리지 않는다 — WAL 모드는 파일에 남는다
        con.execute("PRAGMA journal_mode=WAL")      # 읽는 쪽이 쓰는 쪽을 막지 않게
        con.executescript(SCHEMA)
    return con


def prune(con, lock, retain_hours, chunk=5000):
    """보존 기간보다 오래된 행을 나눠 지운다 — 한 번에 지우면 쓰기 잠금이 길어 PLC 적재가 밀린다. 지운 행 수."""
    cutoff = int(time.time() * 1000 - retain_hours * 3600_000)
    total = 0
    while True:
        with lock:
            n = con.execute("DELETE FROM snapshot WHERE id IN (SELECT id FROM snapshot WHERE recv_ms < ? LIMIT ?)", (cutoff, chunk)).rowcount
            con.commit()
        total += n
        if n < chunk:
            return total


def to_num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def insert_rows(con, lock, rows):
    """rows: dict 리스트. 각 dict 는 crane/source/t_ms/recv_ms + CSV 컬럼 전량."""
    import time
    now = int(time.time() * 1000)
    payload = []
    for r in rows:
        raw = {k: v for k, v in r.items() if k not in ("crane", "source", "t_ms", "recv_ms")}
        alarm = to_num(r.get(PROMOTED["alarm_code"]))
        payload.append((
            r.get("crane", "unknown"),
            r.get("source", "unknown"),
            int(to_num(r.get("t_ms")) or 0),
            now,   # 수신 시각은 서버만 찍는다 — 클라이언트가 보낸 recv_ms 는 버린다(지표1 독립성)
            to_num(r.get(PROMOTED["gt_position"])),
            to_num(r.get(PROMOTED["tr_position"])),
            to_num(r.get(PROMOTED["ho_position"])),
            to_num(r.get(PROMOTED["ho_load"])),
            str(r.get(PROMOTED["op_mode"], "")),
            int(alarm) if alarm is not None else None,
            json.dumps(raw, ensure_ascii=False, separators=(",", ":")),
        ))
    with lock:
        con.executemany(
            "INSERT INTO snapshot (crane,source,t_ms,recv_ms,gt_position,tr_position,ho_position,"
            "ho_load,op_mode,alarm_code,raw) VALUES (?,?,?,?,?,?,?,?,?,?,?)", payload)
        con.commit()
    return len(payload)


def rows_to_dicts(cur):
    return [dict(r) for r in cur.fetchall()]


class Handler(BaseHTTPRequestHandler):
    db_path = None
    allow = parse_allow(DEFAULT_ALLOW)
    token = ""
    lock = threading.Lock()
    local = threading.local()

    @property
    def con(self):   # 요청 스레드마다 자기 연결 — 연결 하나를 스레드끼리 나눠 쓰면 동시 읽기·쓰기에서 서버가 멈췄다
        c = getattr(self.local, "con", None)
        if c is None:
            c = self.local.con = connect(self.db_path, schema=False)
        return c

    # 접속 로그를 stderr 로 (도커 logs 에서 보이게), 다만 헬스체크 폭주는 접어둔다.
    def log_message(self, fmt, *args):
        if "/health" not in self.path:
            sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))

    def send_json(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False, default=str).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def send_csv(self, rows, last_id):
        # 헤더는 PlcSim CSV 와 같다(t_ms + raw 태그) — Unity 쪽이 CsvReplaySource 파서를 그대로 써서 태그 계약이 한 곳에 남는다.
        dicts = [{"t_ms": r["t_ms"], "recv_ms": r["recv_ms"], **json.loads(r["raw"])} for r in rows]
        buf = io.StringIO()
        w = csv.DictWriter(buf, fieldnames=list(dict.fromkeys(k for d in dicts for k in d)), restval="", lineterminator="\n")
        w.writeheader()
        w.writerows(dicts)
        body = buf.getvalue().encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/csv; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        if last_id is not None:
            self.send_header("X-Last-Id", str(last_id))
        self.end_headers()
        self.wfile.write(body)

    def denied(self):
        """허용 대역 밖이면 403, 토큰이 설정됐는데 헤더가 다르면 401 을 보내고 True."""
        ip = ipaddress.ip_address(self.client_address[0].split("%")[0])
        if getattr(ip, "ipv4_mapped", None):
            ip = ip.ipv4_mapped
        if not any(ip.version == n.version and ip in n for n in self.allow):
            self.send_json({"error": "forbidden"}, 403)
            return True
        if self.token and not hmac.compare_digest(self.headers.get(TOKEN_HEADER, ""), self.token):
            self.send_json({"error": "unauthorized"}, 401)
            return True
        return False

    def q(self, name, default=None):
        vals = parse_qs(urlparse(self.path).query).get(name)
        return vals[0] if vals else default

    def do_GET(self):
        if self.denied():
            return
        path = urlparse(self.path).path
        crane = self.q("crane")
        try:
            limit = max(1, min(int(self.q("limit", "500")), 10000))
        except ValueError:
            limit = 500

        if path == "/time":
            return self.send_json({"server_ms": int(time.time() * 1000)})

        if path == "/health":
            n = self.con.execute("SELECT COUNT(*) c FROM snapshot").fetchone()["c"]
            return self.send_json({"ok": True, "rows": n, "db": self.server.db_path})

        if path == "/latest":
            sql = "SELECT * FROM snapshot"
            args = []
            if crane:
                sql += " WHERE crane=?"; args.append(crane)
            sql += " ORDER BY id DESC LIMIT 1"
            rows = rows_to_dicts(self.con.execute(sql, args))
            return self.send_json(rows[0] if rows else {}, 200 if rows else 404)

        if path == "/history":
            sql, args = "SELECT * FROM snapshot WHERE 1=1", []
            if crane:
                sql += " AND crane=?"; args.append(crane)
            for key, op in (("from_ms", ">="), ("to_ms", "<=")):
                v = self.q(key)
                if v is not None:
                    sql += f" AND t_ms {op} ?"; args.append(int(float(v)))
            sql += " ORDER BY id DESC LIMIT ?"; args.append(limit)
            return self.send_json(rows_to_dicts(self.con.execute(sql, args)))

        if path == "/alarms":
            sql = "SELECT * FROM snapshot WHERE alarm_code IS NOT NULL AND alarm_code != 0"
            args = []
            if crane:
                sql += " AND crane=?"; args.append(crane)
            sql += " ORDER BY id DESC LIMIT ?"; args.append(limit)
            return self.send_json(rows_to_dicts(self.con.execute(sql, args)))

        if path == "/since":
            # after_id 가 없으면 최신 1행(접속 순간의 자세), 있으면 그 뒤 행 전부를 오름차순으로.
            if not crane:
                return self.send_json({"error": "crane required"}, 400)
            after = self.q("after_id")
            try:
                if after is None:
                    rows = self.con.execute("SELECT id, t_ms, recv_ms, raw FROM snapshot WHERE crane=? ORDER BY id DESC LIMIT 1",
                                            (crane,)).fetchall()
                else:
                    rows = self.con.execute("SELECT id, t_ms, recv_ms, raw FROM snapshot WHERE crane=? AND id>? ORDER BY id LIMIT ?",
                                            (crane, int(after), limit)).fetchall()
            except ValueError:
                return self.send_json({"error": "after_id must be an integer"}, 400)
            return self.send_csv(rows, rows[-1]["id"] if rows else after)

        if path == "/runs":
            cur = self.con.execute(
                "SELECT crane, source, COUNT(*) rows, MIN(t_ms) t_min, MAX(t_ms) t_max, "
                "MIN(recv_ms) recv_min, MAX(recv_ms) recv_max "
                "FROM snapshot GROUP BY crane, source ORDER BY recv_max DESC")
            return self.send_json(rows_to_dicts(cur))

        return self.send_json({"error": "not found",
                               "endpoints": ["/health", "/latest", "/history", "/alarms", "/runs", "/since", "POST /ingest"]}, 404)

    def do_POST(self):
        if self.denied():
            return
        if urlparse(self.path).path != "/ingest":
            return self.send_json({"error": "not found"}, 404)
        try:
            n = int(self.headers.get("Content-Length", "0"))
            body = json.loads(self.rfile.read(n).decode("utf-8")) if n else None
        except (ValueError, json.JSONDecodeError) as e:
            return self.send_json({"error": f"bad json: {e}"}, 400)
        if isinstance(body, dict):
            body = [body]
        if not isinstance(body, list) or not body:
            return self.send_json({"error": "expected object or non-empty array"}, 400)
        try:
            count = insert_rows(self.con, self.lock, body)
        except sqlite3.Error as e:
            return self.send_json({"error": f"db: {e}"}, 500)
        return self.send_json({"ok": True, "inserted": count})


def prune_loop(db, retain_hours, every_s):
    """서버 수명 동안 every_s 마다 보존 기간을 넘은 행을 지운다 — 빈 페이지는 다음 적재가 다시 쓰므로 파일이 더 자라지 않는다."""
    con = connect(db, schema=False)
    while True:
        try:
            n = prune(con, Handler.lock, retain_hours)
            if n:
                print(f"[xrcrane-db] 보존 {retain_hours:g}시간 넘은 {n}행 삭제", flush=True)
        except sqlite3.Error as e:
            print(f"[xrcrane-db] 보존 삭제 실패: {e}", flush=True)
        time.sleep(every_s)


def cmd_serve(args):
    con = connect(args.db)   # 스키마만 만들고, 요청은 Handler.con 이 스레드별로 연다
    Handler.db_path = args.db
    Handler.allow = parse_allow(args.allow)
    Handler.token = os.environ.get("XRCRANE_TOKEN", "")
    srv = ThreadingHTTPServer((args.host, args.port), Handler)
    srv.db_path = args.db
    if args.retain_hours > 0:
        threading.Thread(target=prune_loop, args=(args.db, args.retain_hours, args.prune_every), daemon=True).start()
    keep = f"보존 {args.retain_hours:g}시간" if args.retain_hours > 0 else "보존 무기한"
    auth = "토큰 필요" if Handler.token else "토큰 없음"
    print(f"[xrcrane-db] listening on {args.host}:{args.port} · db={args.db} · {keep} · 허용 {args.allow} · {auth}", flush=True)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        con.close()


def cmd_import(args):
    """PlcSim CSV 를 그대로 적재 — Unity 를 건드리지 않는 1차 수집 경로."""
    con = connect(args.db)
    lock = threading.Lock()
    total = 0
    for path in args.csv:
        source = args.source or "/".join(path.replace("\\", "/").split("/")[-2:]).replace(".csv", "")
        with open(path, newline="", encoding="utf-8") as f:
            rows = []
            for row in csv.DictReader(f):
                row["crane"] = args.crane
                row["source"] = source
                row["t_ms"] = row.get("t_ms", 0)
                rows.append(row)
        n = insert_rows(con, lock, rows)
        total += n
        print(f"  {source}: {n}행")
    print(f"[xrcrane-db] 적재 완료 — 총 {total}행 → {args.db}")
    con.close()


def clock_offset(base_url, n=8):
    """(서버 시계 − 내 시계, 왕복시간) ms — 왕복이 가장 짧은 표본을 쓴다(Cristian). 오차는 왕복의 절반 이내."""
    import time
    import urllib.request
    best = None
    for _ in range(n):
        t0 = time.time() * 1000
        req = urllib.request.Request(base_url.rstrip("/") + "/time", headers=auth_headers())
        server = json.loads(urllib.request.urlopen(req, timeout=2).read())["server_ms"]
        t1 = time.time() * 1000
        if best is None or t1 - t0 < best[1]:
            best = (server - (t0 + t1) / 2, t1 - t0)
    return int(round(best[0])), int(round(best[1]))


def cmd_feed(args):
    """PLC 대역 — CSV 를 t_ms 간격 그대로 /ingest 에 한 행씩 보낸다. 서버→크레인 경로를 살아 있는 데이터로 돌린다."""
    import time
    import urllib.request
    with open(args.csv, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    source = args.source or "feed/" + "/".join(args.csv.replace("\\", "/").split("/")[-2:]).replace(".csv", "")
    url = args.url.rstrip("/") + "/ingest"
    print(f"[xrcrane-db] feed {source} {len(rows)}행 → {url} crane={args.crane}{' 반복' if args.loop else ''}", flush=True)
    while True:
        off, rtt = clock_offset(args.url)   # 바퀴마다 다시 — 장시간 반복 중 시계가 흘러도 따라간다
        print(f"[xrcrane-db] feed 시계 차 {off:+d}ms (왕복 {rtt}ms) — plc_ms 를 서버 시계로 맞춰 보냄", flush=True)
        t0 = time.monotonic()
        for row in rows:
            wait = t0 + float(row["t_ms"]) / 1000 - time.monotonic()
            if wait > 0:
                time.sleep(wait)
            row.update(crane=args.crane, source=source, plc_ms=int(time.time() * 1000) + off)   # PLC 측 송출 시각(서버 시계 epoch ms) — 지표1 시작점
            req = urllib.request.Request(url, json.dumps(row).encode("utf-8"), auth_headers({"Content-Type": "application/json"}))
            urllib.request.urlopen(req, timeout=5).read()
        if not args.loop:
            break


def main():
    p = argparse.ArgumentParser(description="XR 크레인 통합서버 v0.5 (수집·저장·조회)")
    p.add_argument("--db", default=DEFAULT_DB)
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("serve", help="REST 서버 기동")
    s.add_argument("--host", default="0.0.0.0")
    s.add_argument("--port", type=int, default=DEFAULT_PORT)
    s.add_argument("--allow", default=DEFAULT_ALLOW, help="허용 대역 CIDR(쉼표 구분). 기본 루프백+사설망")
    s.add_argument("--retain-hours", type=float, default=float(os.environ.get("XRCRANE_RETAIN_HOURS", "3")),
                   help="이보다 오래된 행 삭제(개발 중 기본 3시간 ≈ 450MB, 0=무기한). 크레인 3대 100ms 면 시간당 약 150MB")
    s.add_argument("--prune-every", type=float, default=600, help="보존 삭제 주기(초)")
    s.set_defaults(func=cmd_serve)

    i = sub.add_parser("import", help="PlcSim CSV 적재")
    i.add_argument("csv", nargs="+")
    i.add_argument("--crane", default="STS_Crane")
    i.add_argument("--source", default=None)
    i.set_defaults(func=cmd_import)

    fd = sub.add_parser("feed", help="PLC 대역: CSV 를 실시간 속도로 /ingest 에 흘린다")
    fd.add_argument("csv")
    fd.add_argument("--url", default=f"http://127.0.0.1:{DEFAULT_PORT}")
    fd.add_argument("--crane", default="STS_Crane")
    fd.add_argument("--source", default=None)
    fd.add_argument("--loop", action="store_true")
    fd.set_defaults(func=cmd_feed)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
