#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
XR 크레인 통합서버 v0.5 — PLC 이력 수집·저장·조회 (WBS 2.6 최소 골격)

계획서가 요구하는 '중앙 통합 서버'(PLC 수집 → DB 저장·이력 → 조회) 중 **수집·저장·조회**만 세운다.
파트ID 매핑·WebSocket 전파(WBS 3.4)·실 PLC 어댑터(S7/OPC UA)는 범위 밖이다.

★ 의존성 0 — 표준 라이브러리(http.server + sqlite3)만 쓴다.
  FastAPI/uvicorn 을 넣으면 pip·이미지 빌드가 따라붙는데, 엔드포인트 5개에 그럴 이유가 없다.
  나중에 동시 접속이 수백으로 늘면 그때 갈아타면 된다(그 전엔 과투자).

스키마 — PlcSim CSV 45컬럼을 그대로 받는다(새로 설계하지 않는다. 데이터가 이미 정본이다).
  자주 조회하는 6개만 컬럼으로 승격해 인덱스를 걸고, 나머지 전량은 raw(JSON)에 보존한다.
  → 벤더가 태그를 추가해도 스키마 변경 없이 그대로 쌓인다.

엔드포인트
  GET  /health                                  살아있는지 + 적재 행 수
  POST /ingest                                  스냅샷 1건 또는 배열 적재(JSON)
  GET  /latest?crane=STS_Crane                  가장 최근 스냅샷 1건
  GET  /history?crane=&from_ms=&to_ms=&limit=   구간 조회(기본 최근 500)
  GET  /alarms?crane=&limit=                    알람 코드가 0이 아닌 행만
  GET  /runs                                    적재된 (크레인, 출처) 별 행 수·시간 범위

CLI
  python3 xrcrane_db.py serve [--port 5006] [--db /data/xrcrane.db]
  python3 xrcrane_db.py import <csv...> [--crane STS_Crane] [--source S02/run_01]
    ─ Unity 를 건드리지 않고 기존 PlcSim CSV 를 그대로 적재한다(1차 수집 경로).
"""

import argparse
import csv
import json
import os
import sqlite3
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

DEFAULT_DB = os.environ.get("XRCRANE_DB", "/data/xrcrane.db")
DEFAULT_PORT = int(os.environ.get("XRCRANE_PORT", "5006"))

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
CREATE INDEX IF NOT EXISTS ix_snap_alarm   ON snapshot(crane, alarm_code) WHERE alarm_code IS NOT NULL AND alarm_code != 0;
"""


def connect(path):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    con = sqlite3.connect(path, check_same_thread=False)
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA journal_mode=WAL")      # 읽는 쪽이 쓰는 쪽을 막지 않게
    con.executescript(SCHEMA)
    return con


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
            int(to_num(r.get("recv_ms")) or now),
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
    con = None
    lock = threading.Lock()

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

    def q(self, name, default=None):
        vals = parse_qs(urlparse(self.path).query).get(name)
        return vals[0] if vals else default

    def do_GET(self):
        path = urlparse(self.path).path
        crane = self.q("crane")
        try:
            limit = max(1, min(int(self.q("limit", "500")), 10000))
        except ValueError:
            limit = 500

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

        if path == "/runs":
            cur = self.con.execute(
                "SELECT crane, source, COUNT(*) rows, MIN(t_ms) t_min, MAX(t_ms) t_max, "
                "MIN(recv_ms) recv_min, MAX(recv_ms) recv_max "
                "FROM snapshot GROUP BY crane, source ORDER BY recv_max DESC")
            return self.send_json(rows_to_dicts(cur))

        return self.send_json({"error": "not found",
                               "endpoints": ["/health", "/latest", "/history", "/alarms", "/runs", "POST /ingest"]}, 404)

    def do_POST(self):
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


def cmd_serve(args):
    con = connect(args.db)
    Handler.con = con
    srv = ThreadingHTTPServer((args.host, args.port), Handler)
    srv.db_path = args.db
    print(f"[xrcrane-db] listening on {args.host}:{args.port} · db={args.db}", flush=True)
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


def main():
    p = argparse.ArgumentParser(description="XR 크레인 통합서버 v0.5 (수집·저장·조회)")
    p.add_argument("--db", default=DEFAULT_DB)
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("serve", help="REST 서버 기동")
    s.add_argument("--host", default="0.0.0.0")
    s.add_argument("--port", type=int, default=DEFAULT_PORT)
    s.set_defaults(func=cmd_serve)

    i = sub.add_parser("import", help="PlcSim CSV 적재")
    i.add_argument("csv", nargs="+")
    i.add_argument("--crane", default="STS_Crane")
    i.add_argument("--source", default=None)
    i.set_defaults(func=cmd_import)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
