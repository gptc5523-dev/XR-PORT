#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
XR 크레인 통합서버 v0.6 — PLC 이력 수집·저장·조회, 부품 ID 별 상태(/parts). WebSocket 전파는 범위 밖(실 PLC 는 plc_s7.py 어댑터).

의존성 0 — 표준 라이브러리(http.server+sqlite3)만 쓴다. 이 규모 엔드포인트에 FastAPI 는 과투자.

스키마: PlcSim CSV 45컬럼을 그대로 받는다. 자주 쓰는 6개만 컬럼 승격+인덱스, 나머지는 raw(JSON) 보존
  → 벤더가 태그를 추가해도 스키마 변경 없이 쌓인다.

엔드포인트
  GET  /health                                  살아있는지 + 적재 행 수
  GET  /time                                    서버 벽시계(epoch ms) — 기기 간 시계 차 추정용(지표1)
  POST /ingest                                  스냅샷 1건 또는 배열 적재(JSON) — 라이선스 크레인 수를 넘는 크레인은 403, 만료·유예 지나면 402
  GET  /license                                 서버 라이선스 상태(정식·평가판, 허용 크레인 수, 자리 받은 크레인, 만료)
  GET  /latest?crane=STS_Crane                  가장 최근 스냅샷 1건
  GET  /history?crane=&from_ms=&to_ms=&limit=   구간 조회(기본 최근 500)
  GET  /alarms?crane=&limit=                    알람 코드가 0이 아닌 행만
  GET  /runs                                    적재된 (크레인, 출처) 별 행 수·시간 범위
  GET/POST /command?crane=                     사고 지시 {"do": "estop|release|snag|motor|overcurrent|overload|landing|comm|wind|obstruct|clear"} — 라이브 PLC 가 1초 안에 실행
  GET/POST /cargo?crane=                       옮겨진 컨테이너(원래 자리 → 지금 자리) — 라이브 PLC 가 올리고 늦게 뜬 XR 이 맞춘다
  GET  /since?crane=&after_id=                  크레인이 이어 받는 경로(CSV, X-Last-Id) — Unity ServerPlcSource
  GET/POST /users?all=&device=                  등록 사용자(기기 ID → 이름·자리), POST 는 API 로 추가·수정
  GET  /register                               등록 사용자 목록 페이지(읽기 전용)
  GET  /parts?crane=                            부품별 상태(정상·주의·이상)·알람·태그 값, 크레인 운영 상태 — crane 없으면 전 크레인(WBS 2.19)
  GET  /parts/history?crane=&part=&from_ms=&to_ms=   부품 상태가 바뀐 순간들(기본 최근 10분) — 2차년도 AI 학습·상태 요약의 입력
  GET/POST /events?session=&type=&after_id=        세션 조작·선택 이력 — 호스트가 매긴 순서(seq)로 저장, (세션, seq) 중복은 무시(계획서 그림 5 '이벤트 순서 제어')

접근 제어(WBS 9.2, 폐쇄망 전제 9.1)
  XRCRANE_ALLOW  허용 대역(쉼표 구분 CIDR). 기본 = 루프백 + 사설망(10/8, 172.16/12, 192.168/16) + Tailscale(100.64/10). 밖이면 403.
  XRCRANE_ALLOW_EXTRA  기본에 더할 현장 대역(사설망 밖 대역일 때만).
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
import re
import sqlite3
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xrcrane_license as lic  # noqa: E402   서버 라이선스(WBS 8.3) — 같은 app/ 폴더

DEFAULT_DB = os.environ.get("XRCRANE_DB", "/data/xrcrane.db")
DEFAULT_PORT = int(os.environ.get("XRCRANE_PORT", "5006"))
DEFAULT_ALLOW = ",".join(filter(None, [os.environ.get("XRCRANE_ALLOW") or "127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,100.64.0.0/10",   # 100.64/10 = Tailscale VPN(기본 접속 경로 — 항상 허용)
                                         os.environ.get("XRCRANE_ALLOW_EXTRA")]))   # 현장 추가 대역(install.sh --allow)
TOKEN_HEADER = "X-Auth-Token"
DEFAULT_LICENSE = os.environ.get("XRCRANE_LICENSE", "/etc/xrcrane-license.json")
DEFAULT_AI = os.environ.get("XRCRANE_AI_URL", "http://xrcrane-ai:5007")   # 6.4 추론 서비스(compose 내부) — /summary 를 여기로 넘긴다


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
CREATE TABLE IF NOT EXISTS events (
  id        INTEGER PRIMARY KEY AUTOINCREMENT,   -- 서버 저장 순서
  session   TEXT    NOT NULL,                    -- 호스트 세션(시작 시각 epoch ms 문자열)
  seq       INTEGER NOT NULL,                    -- 호스트가 처리한 순서(세션 안 1부터) — 이벤트 순서의 기준
  t_ms      INTEGER NOT NULL,                    -- 호스트 시각(epoch ms)
  recv_ms   INTEGER NOT NULL,
  actor_id  INTEGER, actor TEXT,                 -- Netcode 클라이언트 ID · 등록 정보 문구
  type      TEXT    NOT NULL, target TEXT, detail TEXT,
  UNIQUE(session, seq)                           -- 다시 보내도 한 번만(재전송으로 순서가 꼬이지 않게)
);
CREATE INDEX IF NOT EXISTS ix_events_type ON events(type, id);
CREATE TABLE IF NOT EXISTS users (
  device_id     TEXT PRIMARY KEY,          -- 'SLOT:1'(VR 자리) · 'MAC:aa:bb:…' · 'SN:…' · 'BEAM:…'
  device_type   TEXT NOT NULL,
  device_label  TEXT, name TEXT NOT NULL, position TEXT, department TEXT,
  role          TEXT NOT NULL,             -- 쉼표 구분: 운전,관리,관전
  can_host      INTEGER NOT NULL DEFAULT 0,
  color         TEXT, memo TEXT,
  registered_at INTEGER NOT NULL,          -- epoch ms
  last_seen     INTEGER,
  active        INTEGER NOT NULL DEFAULT 1
);
"""

USER_FIELDS = ("device_type", "device_label", "name", "position", "department", "role", "can_host", "color", "memo", "active")


def user_error(u):
    """등록 한 건이 쓸 만한가 — 문제면 사유 문자열, 괜찮으면 None."""
    if not isinstance(u, dict):
        return "object 필요"
    for k in ("device_id", "device_type", "name", "role"):
        if not str(u.get(k) or "").strip():
            return f"{k} 필요"
    if any(len(str(u.get(k) or "")) > 200 for k in ("device_id", *USER_FIELDS)):
        return "값이 너무 김(200자)"
    if u.get("color") and not re.fullmatch(r"#[0-9A-Fa-f]{6}", str(u["color"]).strip()):
        return "color 는 #RRGGBB 형식"
    return None


def upsert_user(con, u, overwrite=True):
    """users 에 한 건 넣기 — 이미 있으면 내용만 갱신(overwrite=False 면 그대로 둔다), 등록 시각은 처음 값. 커밋은 호출자."""
    vals = [u.get(k) for k in USER_FIELDS]
    vals[USER_FIELDS.index("can_host")] = 1 if u.get("can_host") else 0
    vals[USER_FIELDS.index("active")] = 0 if u.get("active") is False else 1
    con.execute(f"INSERT INTO users(device_id, {', '.join(USER_FIELDS)}, registered_at) VALUES (?{', ?' * len(USER_FIELDS)}, ?) "
                f"ON CONFLICT(device_id) DO " + (f"UPDATE SET {', '.join(f'{k}=excluded.{k}' for k in USER_FIELDS)}" if overwrite else "NOTHING"),
                [str(u["device_id"]).strip(), *vals, int(time.time() * 1000)])


def load_users(con, path):
    """등록 파일(JSON 배열)의 처음 등록만 넣는다 — 이미 있는 기기는 POST /users 로 고친 값을 지킨다. 파일 건수."""
    with open(path, encoding="utf-8") as f:
        users = json.load(f)
    for u in users:
        upsert_user(con, u, overwrite=False)
    con.commit()
    return len(users)


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


def insert_events(con, lock, body):
    """세션 이벤트(조작·선택 이력) 적재 — {"session", "events":[{seq,t_ms,actor_id,actor,type,target,detail}]}. (세션, seq) 가 이미 있으면 건너뛴다."""
    session = str(body.get("session") or "").strip()
    events = body.get("events")
    if not session or not isinstance(events, list):
        raise ValueError("session 과 events 배열 필요")
    now = int(time.time() * 1000)
    rows = [(session, int(e["seq"]), int(e.get("t_ms") or now), now, e.get("actor_id"), str(e.get("actor") or ""),
             str(e["type"]), str(e.get("target") or ""), str(e.get("detail") or "")) for e in events]
    with lock:
        before = con.total_changes
        con.executemany("INSERT OR IGNORE INTO events(session,seq,t_ms,recv_ms,actor_id,actor,type,target,detail) VALUES (?,?,?,?,?,?,?,?,?)", rows)
        con.commit()
        stored = con.total_changes - before
    last = con.execute("SELECT MAX(seq) m FROM events WHERE session=?", (session,)).fetchone()["m"]
    return stored, last


# 부품 상태(WBS 2.19) — 표는 Unity 메뉴 'PLC/부품 ID 매핑표 내보내기'가 쓴 parts.json, 코드북은 Unity 와 같은 AlarmCodebook.json.
HERE = os.path.dirname(os.path.abspath(__file__))
STATE = ("정상", "주의", "이상")
ONLINE_MS = 5000   # 이보다 오래 새 행이 없으면 오프라인


def load_partmap():
    """parts.json 과 알람 코드북 — 코드북은 이 파일 옆(배포본)에 없으면 저장소의 Unity Resources 것을 읽는다."""
    book_path = next(p for p in (os.path.join(HERE, "AlarmCodebook.json"),
                                 os.path.join(HERE, "..", "Assets", "Crane", "Resources", "Sts", "AlarmCodebook.json")) if os.path.exists(p))
    with open(os.path.join(HERE, "parts.json"), encoding="utf-8") as f:
        parts = json.load(f)
    with open(book_path, encoding="utf-8") as f:
        book = {e["code"]: e for e in json.load(f)["entries"]}
    return parts, book


def covers(key, pid, ids):
    """태그 part 가 이 부품을 가리키나 — 정확히 같거나, 표에 없는 그룹 ID(SP.TWL)의 하위. 문서 내보내기(CranePartIdMenu.Covers)와 같다."""
    return key != "" and (pid == key or (pid.startswith(key + ".") and key not in ids))


def part_states(crane, raw, parts, book):
    """스냅샷 한 행(raw 태그) → 크레인 운영 상태·현재 알람·부품별 상태. 판정은 XR 의 지표4(Kpi4StatusAccuracy)와 같다 —
    XR 대상 알람의 part 와 그 하위 부품 = 심각도 1 주의·2 이상, 운영 = 비상정지·심각도 2 이상이면 이상, 운전 비트면 운전, 그 외 정지."""
    def on(tag):
        return str(raw.get(tag, 0)).strip().lower() not in ("0", "0.0", "", "false")
    rtg = "RTG" in crane.upper()
    table = parts["rtg"] if rtg else parts["sts"]
    ids = {p["id"] for p in table}
    alarm = book.get(int(to_num(raw.get("ALM_Latest_Code")) or 0)) if on("ALM_Active") else None
    if alarm is not None and not alarm.get("xr", True):
        alarm = None   # XR 비대상 알람은 화면에 안 띄운다
    sev = alarm["sev"] if alarm else 0
    level = min(sev, 2)
    apart = (alarm or {}).get("part") or ""
    out = []
    for p in table:
        hit = apart != "" and (p["id"] == apart or p["id"].startswith(apart + "."))   # 강조와 같다(CranePartId.Find)
        lv = level if hit else 0
        tags = {t["tag"]: raw.get(t["tag"]) for t in parts["tags"] if covers(t["part"], p["id"], ids)}
        out.append({"id": p["id"], "note": p["note"], "state": STATE[lv], "level": lv,
                    "alarm": alarm["code"] if hit and lv else None, "tags": tags})
    op = "이상" if on("OP_Emergency_Stop") or sev >= 2 else "운전" if on("OP_Running") else "정지"
    return {
        "crane": crane, "kind": "RTG" if rtg else "STS", "op_state": op, "link": on("COM_Link_Status"),
        "alarm": None if alarm is None else {"code": alarm["code"], "sev": sev, "state": STATE[level], "ko": alarm.get("ko", ""),
                                             "part": apart or None, "part_on_model": apart == "" or any(i == apart or i.startswith(apart + ".") for i in ids)},
        "worst": STATE[max((p["level"] for p in out), default=0)],
        "parts": out,
    }


def parts_view(row, parts, book, now_ms):
    v = part_states(row["crane"], json.loads(row["raw"]), parts, book)
    age = max(0, now_ms - row["recv_ms"])
    v.update(t_ms=row["t_ms"], recv_ms=row["recv_ms"], age_ms=age, online=v.pop("link") and age <= ONLINE_MS)
    return v


def part_history(rows, crane, parts, book, part=None):
    """행들을 차례로 보며 부품 상태가 바뀐 순간만 — {t_ms, part, state, level, alarm}. 첫 행이 정상이 아니면 그것도 낸다."""
    prev, out, last = {}, [], None
    for r in rows:
        raw = json.loads(r["raw"])
        sig = (raw.get("ALM_Active"), raw.get("ALM_Latest_Code"))   # 부품 상태는 알람 두 태그로만 정해진다 — 같으면 다시 셀 것 없다
        if sig == last:
            continue
        last = sig
        for p in part_states(crane, raw, parts, book)["parts"]:
            if part and p["id"] != part:
                continue
            key = (p["level"], p["alarm"])
            if prev.get(p["id"], (0, None)) != key:
                out.append({"t_ms": r["t_ms"], "part": p["id"], "state": p["state"], "level": p["level"], "alarm": p["alarm"]})
            prev[p["id"]] = key
    return out


def ai_summary(crane, base=DEFAULT_AI):
    """상태 요약(WBS 6.5) — 추론 서비스 /summary 를 그대로 넘긴다. 404 = 아직 판정 없음, 그 밖의 실패·1초 초과 = 503(클라이언트는 둘 다 로컬 규칙)."""
    try:
        with urllib.request.urlopen(f"{base}/summary?crane={urllib.parse.quote(crane or '')}", timeout=1) as r:
            return json.loads(r.read()), 200
    except urllib.error.HTTPError as e:
        return {"error": "no verdict" if e.code == 404 else "ai error"}, 404 if e.code == 404 else 503
    except Exception:
        return {"error": "ai offline"}, 503


class Handler(BaseHTTPRequestHandler):
    db_path = None
    allow = parse_allow(DEFAULT_ALLOW)
    token = ""
    lock = threading.Lock()
    local = threading.local()
    commands = {}   # 크레인 → 대기 중인 사고 지시(라이브 PLC 가 가져가면 비운다) — 시연 때 원하는 순간에 오류를 낸다
    partmap = None   # (parts.json, 코드북) — /parts 첫 요청 때 읽는다
    lic_doc, lic_err, slots = None, "미설정", None   # 서버 라이선스 — cmd_serve 가 채운다

    def license(self):
        st = lic.status(self.lic_doc, self.lic_err)   # 날짜가 바뀌면 만료·유예가 바로 반영되게 매번 계산
        st["used"] = sorted(self.slots.held) if self.slots else []
        return st
    cargo = {}   # 크레인 → 원래 자리에서 옮겨진 컨테이너 목록(라이브 PLC 가 올리고 XR 이 접속할 때 맞춘다) — 메모리만, 재시작하면 원래 배치

    @property
    def con(self):   # 요청 스레드마다 자기 연결 — 연결 하나를 스레드끼리 나눠 쓰면 동시 읽기·쓰기에서 서버가 멈췄다
        c = getattr(self.local, "con", None)
        if c is None:
            c = self.local.con = connect(self.db_path, schema=False)
        return c

    # 접속 로그를 stderr 로 (serve.sh 로그에서 보이게), 다만 헬스체크 폭주는 접어둔다.
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

        if path == "/summary":
            return self.send_json(*ai_summary(crane))

        if path == "/time":
            return self.send_json({"server_ms": int(time.time() * 1000)})

        if path == "/health":
            n = self.con.execute("SELECT COUNT(*) c FROM snapshot").fetchone()["c"]
            st = self.license()
            return self.send_json({"ok": True, "rows": n, "db": self.server.db_path,
                                   "license": {k: st[k] for k in ("mode", "state", "cranes", "used")}})

        if path == "/license":
            return self.send_json(self.license())

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

        if path == "/command":
            with self.lock:
                return self.send_json({"commands": self.commands.pop(crane or "", [])})

        if path == "/cargo":
            with self.lock:
                return self.send_json(self.cargo.get(crane or "", {"boxes": [], "held": []}))

        if path == "/register":
            with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "register.html"), "rb") as f:
                page = f.read()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(page)))
            self.end_headers()
            return self.wfile.write(page)

        if path == "/users":
            sql, args = ("SELECT * FROM users", []) if self.q("all") else ("SELECT * FROM users WHERE active=1", [])
            if self.q("device"):
                sql += (" AND" if "WHERE" in sql else " WHERE") + " device_id=?"; args.append(self.q("device"))
            return self.send_json({"users": rows_to_dicts(self.con.execute(sql, args))})

        if path in ("/parts", "/parts/history"):
            if Handler.partmap is None:
                try:
                    Handler.partmap = load_partmap()
                except (OSError, StopIteration, ValueError, KeyError) as e:
                    return self.send_json({"error": f"부품 표·코드북을 못 읽음: {e}"}, 503)
            parts, book = Handler.partmap
            if path == "/parts":
                cranes = [crane] if crane else [r["crane"] for r in self.con.execute("SELECT DISTINCT crane FROM snapshot")]
                now = int(time.time() * 1000)
                views = []
                for c in cranes:
                    row = self.con.execute("SELECT crane, t_ms, recv_ms, raw FROM snapshot WHERE crane=? ORDER BY id DESC LIMIT 1", (c,)).fetchone()
                    if row:
                        views.append(parts_view(row, parts, book, now))
                if crane:
                    return self.send_json(views[0] if views else {"error": "no data"}, 200 if views else 404)
                return self.send_json({"cranes": views})
            if not crane:
                return self.send_json({"error": "crane required"}, 400)
            try:   # 기본 = 최근 10분, 한 번에 최대 5만 행(100ms 면 약 83분)
                to_ms = int(float(self.q("to_ms", time.time() * 1000)))
                from_ms = int(float(self.q("from_ms", to_ms - 600_000)))
            except ValueError:
                return self.send_json({"error": "from_ms/to_ms must be numbers"}, 400)
            rows = self.con.execute("SELECT t_ms, raw FROM snapshot WHERE crane=? AND t_ms BETWEEN ? AND ? ORDER BY t_ms LIMIT 50000",
                                    (crane, from_ms, to_ms)).fetchall()
            return self.send_json({"crane": crane, "from_ms": from_ms, "to_ms": to_ms, "rows": len(rows),
                                   "changes": part_history(rows, crane, parts, book, self.q("part"))[-limit:]})

        if path == "/events":
            sql, args = "SELECT * FROM events WHERE 1=1", []
            for key, col in (("session", "session"), ("type", "type")):
                if self.q(key):
                    sql += f" AND {col}=?"; args.append(self.q(key))
            try:
                if self.q("after_id") is not None:
                    sql += " AND id>?"; args.append(int(self.q("after_id")))
            except ValueError:
                return self.send_json({"error": "after_id must be an integer"}, 400)
            sql += " ORDER BY session, seq LIMIT ?" if self.q("session") else " ORDER BY id LIMIT ?"
            args.append(limit)
            return self.send_json({"events": rows_to_dicts(self.con.execute(sql, args))})

        if path == "/runs":
            cur = self.con.execute(
                "SELECT crane, source, COUNT(*) rows, MIN(t_ms) t_min, MAX(t_ms) t_max, "
                "MIN(recv_ms) recv_min, MAX(recv_ms) recv_max "
                "FROM snapshot GROUP BY crane, source ORDER BY recv_max DESC")
            return self.send_json(rows_to_dicts(cur))

        return self.send_json({"error": "not found",
                               "endpoints": ["/health", "/time", "/latest", "/history", "/alarms", "/runs", "/since", "/command", "/cargo", "/users", "/register", "/parts", "/parts/history", "/events", "/license", "POST /ingest", "POST /events", "POST /command", "POST /cargo", "POST /users"]}, 404)

    def do_POST(self):
        if self.denied():
            return
        if urlparse(self.path).path not in ("/ingest", "/cargo", "/command", "/users", "/events"):
            return self.send_json({"error": "not found"}, 404)
        try:
            n = int(self.headers.get("Content-Length", "0"))
            body = json.loads(self.rfile.read(n).decode("utf-8")) if n else None
        except (ValueError, json.JSONDecodeError) as e:
            return self.send_json({"error": f"bad json: {e}"}, 400)
        if urlparse(self.path).path == "/command":
            crane = self.q("crane")
            if not crane or not isinstance(body, dict) or not body.get("do"):
                return self.send_json({"error": "crane 와 {\"do\": \"estop\"} 필요"}, 400)
            with self.lock:
                self.commands.setdefault(crane, []).append(body)
            return self.send_json({"ok": True, "queued": body["do"]})
        if urlparse(self.path).path == "/events":
            if not isinstance(body, dict):
                return self.send_json({"error": "session 과 events 배열 필요"}, 400)
            try:
                stored, last = insert_events(self.con, self.lock, body)
            except (ValueError, KeyError, TypeError) as e:
                return self.send_json({"error": str(e)}, 400)
            return self.send_json({"ok": True, "stored": stored, "last_seq": last})
        if urlparse(self.path).path == "/users":
            err = user_error(body)
            if err:
                return self.send_json({"error": err}, 400)
            with self.lock:
                upsert_user(self.con, body)
                self.con.commit()
            return self.send_json({"ok": True, "device_id": str(body["device_id"]).strip()})
        if urlparse(self.path).path == "/cargo":
            crane = self.q("crane")
            if not crane or not isinstance(body, dict) or not isinstance(body.get("boxes"), list):
                return self.send_json({"error": "crane 와 {\"boxes\": [...]} 필요"}, 400)
            with self.lock:
                self.cargo[crane] = {"boxes": body["boxes"], "held": body.get("held") or []}
            return self.send_json({"ok": True, "boxes": len(body["boxes"])})
        if isinstance(body, dict):
            body = [body]
        if not isinstance(body, list) or not body:
            return self.send_json({"error": "expected object or non-empty array"}, 400)
        st = self.license()
        if not st["accept"]:
            return self.send_json({"error": "license expired", "license": st}, 402)
        now = int(time.time() * 1000)
        ok = [r for r in body if self.slots.allowed(str(r.get("crane", "unknown")), now)]   # 크레인 단위 과금 — 자리 없는 크레인은 버린다
        if not ok:
            return self.send_json({"error": "license crane limit", "cranes": st["cranes"], "used": st["used"]}, 403)
        try:
            count = insert_rows(self.con, self.lock, ok)
        except sqlite3.Error as e:
            return self.send_json({"error": f"db: {e}"}, 500)
        return self.send_json({"ok": True, "inserted": count, **({"rejected": len(body) - len(ok)} if len(ok) < len(body) else {})})


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
    users_path = os.environ.get("XRCRANE_USERS", "")
    if users_path and os.path.exists(users_path):
        print(f"[xrcrane-db] 사용자 등록 {load_users(con, users_path)}명 ← {users_path}", flush=True)
    Handler.db_path = args.db
    Handler.allow = parse_allow(args.allow)
    Handler.token = os.environ.get("XRCRANE_TOKEN", "")
    Handler.lic_doc, Handler.lic_err = lic.load(args.license)
    st = lic.status(Handler.lic_doc, Handler.lic_err)
    Handler.slots = lic.Slots(connect(args.db, schema=False), Handler.lock, st["license_id"], st["cranes"])
    print(f"[xrcrane-db] 라이선스 {st['mode']} {st['license_id']} · 크레인 {st['cranes']}대 · {st['state']}"
          + (f" · {st['note']}" if st.get("note") else ""), flush=True)
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
    with open(args.csv, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    source = args.source or "feed/" + "/".join(args.csv.replace("\\", "/").split("/")[-2:]).replace(".csv", "")
    url = args.url.rstrip("/") + "/ingest"
    print(f"[xrcrane-db] feed {source} {len(rows)}행 → {url} crane={args.crane}{' 반복' if args.loop else ''}", flush=True)
    events = open(args.events_out, "w", encoding="utf-8") if args.events_out else None   # 지표6: 보낸 알람 변화(plc_ms, code) — 잃어버린 이벤트도 셀 수 있게
    if events:
        events.write("plc_ms,code\n")
    prev = None
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
            code = str(row.get("ALM_Latest_Code", "0")) if str(row.get("ALM_Active", "0")) not in ("0", "") else "0"
            if events and prev is not None and code != prev:   # 첫 행은 변화가 아니다
                events.write(f"{row['plc_ms']},{code}\n"); events.flush()
            prev = code
        if not args.loop:
            break


def main():
    p = argparse.ArgumentParser(description="XR 크레인 통합서버 v0.6 (수집·저장·조회·부품 상태)")
    p.add_argument("--db", default=DEFAULT_DB)
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("serve", help="REST 서버 기동")
    s.add_argument("--host", default="0.0.0.0")
    s.add_argument("--port", type=int, default=DEFAULT_PORT)
    s.add_argument("--allow", default=DEFAULT_ALLOW, help="허용 대역 CIDR(쉼표 구분). 기본 루프백+사설망+Tailscale(100.64/10)")
    s.add_argument("--retain-hours", type=float, default=float(os.environ.get("XRCRANE_RETAIN_HOURS", "3")),
                   help="이보다 오래된 행 삭제(개발 중 기본 3시간 ≈ 450MB, 0=무기한). 크레인 3대 100ms 면 시간당 약 150MB")
    s.add_argument("--prune-every", type=float, default=600, help="보존 삭제 주기(초)")
    s.add_argument("--license", default=DEFAULT_LICENSE, help="라이선스 파일(공급사 서명) — 없으면 평가판(크레인 1대)")
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
    fd.add_argument("--events-out", default=None, help="보낸 알람 변화(plc_ms, code)를 적을 CSV — 지표6")
    fd.set_defaults(func=cmd_feed)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
