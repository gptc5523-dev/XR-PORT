#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""상태 요약 추론 서비스 (WBS 6.4 → 6.5) — 통합서버 기록을 1초마다 읽어 크레인마다 정상·주의·이상과 요약문을 판정한다.

GET /summary?crane=<이름> → {"crane","t","state","cause","code","text","activity","conf","cause_ko","action","model","rule":false}
    404 = 아직 판정 없음(기록 60초 미만) · /health → {"ok", "cranes", "model"}
통합서버(xrcrane_db.py)가 /summary 를 이리로 넘긴다(XRCRANE_AI_URL). 학습과 같은 특징(train_ai.features)·같은 문장(gen_ai.summary).
환경변수: XRCRANE_DB_URL(http://xrcrane-db:5006) · XRCRANE_TOKEN · XRCRANE_AI_MODELS(모델 폴더) · XRCRANE_AI_SET(full|phys) · XRCRANE_AI_PORT(5007)
"""
import json, os, pickle, sys, threading, time, urllib.parse, urllib.request
from collections import deque
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np

D = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, D)
import gen_ai as G      # noqa: E402
import train_ai as T    # noqa: E402

DB = os.environ.get("XRCRANE_DB_URL", "http://xrcrane-db:5006").rstrip("/")
TOKEN = os.environ.get("XRCRANE_TOKEN", "")
MODELS = os.environ.get("XRCRANE_AI_MODELS", os.path.join(D, "models"))
SET = os.environ.get("XRCRANE_AI_SET", "full")
PORT = int(os.environ.get("XRCRANE_AI_PORT", "5007"))
NUM = T.CONT + T.BIN + T.CAT + T.ALARM + ["ALM_Latest_Code"]

state_m = pickle.load(open(os.path.join(MODELS, f"state_{SET}.pkl"), "rb"))
cause_m = pickle.load(open(os.path.join(MODELS, f"cause_{SET}.pkl"), "rb"))
buf, verdict, lock = {}, {}, threading.Lock()


def get(path, **q):
    url = f"{DB}{path}?{urllib.parse.urlencode(q)}"
    req = urllib.request.Request(url, headers={"X-Auth-Token": TOKEN} if TOKEN else {})
    with urllib.request.urlopen(req, timeout=5) as r: return json.loads(r.read())


def pull(crane):
    """크레인 기록을 5분치까지 쌓는다 — 처음엔 한 번에, 그 뒤엔 마지막 시각 이후만."""
    b = buf.setdefault(crane, deque(maxlen=T.L5))
    last = b[-1]["t_ms"] if b else None
    rows = get("/history", crane=crane, limit=T.L5 if last is None else 200, **({} if last is None else {"from_ms": last + 1}))
    for r in sorted(rows, key=lambda r: r["t_ms"]):   # 태그 값은 raw(JSON 문자열) 안에 있다
        b.append({**json.loads(r["raw"]), "t_ms": r["t_ms"]})
    return b


def judge(crane, b):
    n = len(b)
    if n < T.L: return None   # 60초 미만 — 판정 보류
    cols = {c: np.array([float(r.get(c) or 0) for r in b], np.float32) for c in NUM}
    ph, al, _, _ = T.features(cols, np.array([n - 1]))
    X = ph if SET == "phys" else np.hstack([ph, al])
    st, ca = T.joint(state_m, cause_m, X)
    st, ca = int(st[0]), int(ca[0])
    conf = float(state_m.predict_proba(X)[0].max())
    cause = T.CAUSES[ca] if st else ""
    code = G.CAUSES[cause][2] if st else 0
    act = T.activity(cols, -1)
    return {"crane": crane, "t": int(b[-1]["t_ms"]), "state": st, "cause": cause, "code": code,
            "text": G.summary(st, cause, code, crane, act), "activity": act, "conf": round(conf, 3),
            "cause_ko": G.CAUSES[cause][0] if st else "", "action": G.CAUSES[cause][1] if st else "",
            "model": SET, "rule": False}


def loop():
    cranes, seen = [], 0.0
    while True:
        t0 = time.time()
        try:
            if time.time() - seen > 30:   # 크레인 목록은 30초마다 — 최근 기록에 나온 이름
                cranes = sorted({r["crane"] for r in get("/history", limit=500)}); seen = time.time()
            for c in cranes:
                v = judge(c, pull(c))
                with lock:
                    if v: verdict[c] = v
                    else: verdict.pop(c, None)
        except Exception as e:
            print(f"[serve_ai] {type(e).__name__}: {e}", flush=True)
        time.sleep(max(0.0, 1.0 - (time.time() - t0)))


class H(BaseHTTPRequestHandler):
    def send(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code); self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

    def do_GET(self):
        u = urllib.parse.urlparse(self.path); q = urllib.parse.parse_qs(u.query)
        if u.path == "/health":
            with lock: return self.send({"ok": True, "cranes": sorted(verdict), "model": SET})
        if u.path == "/summary":
            with lock: v = verdict.get((q.get("crane") or [""])[0])
            return self.send(v) if v else self.send({"error": "no verdict"}, 404)
        self.send({"error": "not found", "endpoints": ["/summary", "/health"]}, 404)

    def log_message(self, *a): pass


if __name__ == "__main__":
    threading.Thread(target=loop, daemon=True).start()
    print(f"[serve_ai] :{PORT} · 모델 {SET} · 기록 {DB}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), H).serve_forever()
