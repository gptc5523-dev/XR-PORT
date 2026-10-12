#!/usr/bin/env python3
"""상태 요약 프록시 자체 시험 — python3 Server/test_summary.py (실패하면 AssertionError)."""
import json, threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from xrcrane_db import ai_summary

VERDICT = {"crane": "STS_Crane", "t": 1, "state": 1, "cause": "wind_warn", "code": 6001, "text": "[주의] STS_Crane — 풍속 주의 (코드 6001). 조치: 70% 감속 운전, 풍속 추이 확인", "rule": False}


class Ai(BaseHTTPRequestHandler):
    def do_GET(self):
        hit = "crane=STS_Crane" in self.path
        body = json.dumps(VERDICT if hit else {"error": "no verdict"}).encode()
        self.send_response(200 if hit else 404); self.end_headers(); self.wfile.write(body)

    def log_message(self, *a): pass


srv = HTTPServer(("127.0.0.1", 0), Ai)
threading.Thread(target=srv.serve_forever, daemon=True).start()
base = f"http://127.0.0.1:{srv.server_port}"

assert ai_summary("STS_Crane", base) == (VERDICT, 200)
assert ai_summary("RTG 크레인_1", base)[1] == 404            # 판정 전 — 이름의 공백·한글도 넘어간다
assert ai_summary("STS_Crane", "http://127.0.0.1:9")[1] == 503   # 추론 서비스 꺼짐
srv.shutdown()
print("ok")
