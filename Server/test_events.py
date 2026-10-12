#!/usr/bin/env python3
"""세션 이벤트(/events) 자체 시험 — python3 Server/test_events.py. 임시 DB 로 서버를 띄워 순서·중복·조회를 본다."""
import json
import os
import tempfile
import threading
import urllib.request
from http.server import ThreadingHTTPServer

import xrcrane_db as x

db = os.path.join(tempfile.mkdtemp(), "t.db")
x.connect(db).close()
x.Handler.db_path = db
srv = ThreadingHTTPServer(("127.0.0.1", 0), x.Handler)
srv.db_path = db
threading.Thread(target=srv.serve_forever, daemon=True).start()
base = f"http://127.0.0.1:{srv.server_port}"


def call(path, body=None):
    req = urllib.request.Request(base + path, json.dumps(body).encode() if body is not None else None,
                                 {"Content-Type": "application/json"})
    try:
        return json.loads(urllib.request.urlopen(req).read())
    except urllib.error.HTTPError as e:
        return {"code": e.code, **json.loads(e.read())}


ev = lambda seq, t, target="": {"seq": seq, "t_ms": 1000 + seq, "actor_id": 0, "actor": "호스트", "type": t, "target": target}

r = call("/events", {"session": "S1", "events": [ev(1, "session_start", "조종"), ev(2, "select", "STS_Crane|TR")]})
assert r["stored"] == 2 and r["last_seq"] == 2, r
r = call("/events", {"session": "S1", "events": [ev(2, "select", "STS_Crane|TR"), ev(3, "hand_grab", "C17")]})   # 재전송 섞임
assert r["stored"] == 1 and r["last_seq"] == 3, r
call("/events", {"session": "S2", "events": [ev(1, "session_start", "PLC")]})
call("/events", {"session": "S1", "events": [ev(5, "hand_release", "C17"), ev(4, "select", "")]})   # 늦게 도착한 4

got = call("/events?session=S1")["events"]
assert [e["seq"] for e in got] == [1, 2, 3, 4, 5], got   # 세션 조회는 호스트 순서(seq)
assert got[2]["type"] == "hand_grab" and got[2]["target"] == "C17" and got[0]["actor"] == "호스트"
assert [e["type"] for e in call("/events?type=session_start")["events"]] == ["session_start", "session_start"]
first = call("/events")["events"][0]["id"]
assert all(e["id"] > first for e in call(f"/events?after_id={first}")["events"])
assert call("/events", {"events": []})["code"] == 400 and call("/events", {"session": "S1"})["code"] == 400
srv.shutdown()
print("ok")
