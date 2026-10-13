#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""서버 라이선스 자체 시험(WBS 8.3) — 실패하면 종료코드 1. 공급사 개인키 없이 돈다(시험용 키를 즉석에서 만든다).
RFC 8032 시험 벡터 · 서명 위조/변조 거부 · 만료/유예 판정 · 크레인 자리(재시작 유지·초과 거부·라이선스 바뀌면 새로) · /ingest 403·402."""
import datetime, json, os, secrets, sqlite3, sys, tempfile, threading, urllib.error, urllib.request
from http.server import ThreadingHTTPServer

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ed25519, xrcrane_license as L, xrcrane_db as X  # noqa: E401,E402

sk = bytes.fromhex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60")
assert ed25519.public_key(sk).hex() == "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"
assert ed25519.sign(sk, b"").hex().startswith("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb882")

tk = secrets.token_bytes(32); pub = ed25519.public_key(tk).hex()
d = tempfile.mkdtemp()
def write(lic, key=tk, tamper=None):
    doc = {"license": lic, "sig": ed25519.sign(key, L.canonical(lic)).hex()}
    if tamper: doc["license"] = dict(lic, **tamper)
    p = os.path.join(d, secrets.token_hex(4) + ".json"); json.dump(doc, open(p, "w")); return p
base = {"license_id": "T-1", "licensee": "시험", "site": "시험", "cranes": 2, "expires": "2027-01-31", "grace_days": 10}

lic, err = L.load(write(base), pub); assert lic and not err
assert L.load(write(base, tamper={"cranes": 99}), pub)[0] is None, "변조 통과"
assert L.load(write(base, key=secrets.token_bytes(32)), pub)[0] is None, "남의 키 통과"
assert L.load(os.path.join(d, "none.json"), pub)[0] is None
D = datetime.date
assert L.status(lic, "", D(2027, 1, 31))["state"] == "ok"
assert L.status(lic, "", D(2027, 2, 10))["state"] == "grace" and L.status(lic, "", D(2027, 2, 10))["accept"]
assert L.status(lic, "", D(2027, 2, 11))["state"] == "expired" and not L.status(lic, "", D(2027, 2, 11))["accept"]
ev = L.status(None, "없음"); assert ev["mode"] == "평가판" and ev["cranes"] == 1 and ev["accept"]

db = os.path.join(d, "t.db"); lock = threading.Lock()
s = L.Slots(X.connect(db), lock, "T-1", 2)
assert s.allowed("A", 1) and s.allowed("B", 2) and s.allowed("A", 3) and not s.allowed("C", 4)
s2 = L.Slots(X.connect(db, schema=False), lock, "T-1", 2); assert s2.held == {"A", "B"}, "재시작에 자리 유지 안 됨"
s3 = L.Slots(X.connect(db, schema=False), lock, "T-2", 2); assert s3.held == set(), "새 라이선스인데 옛 자리 남음"

# /ingest — 자리 2대 → 세 번째 크레인 403, 섞인 배치는 받은 것만, 만료면 402
X.Handler.db_path = db; X.Handler.token = ""; X.Handler.allow = X.parse_allow("127.0.0.0/8")
X.Handler.lic_doc, X.Handler.lic_err = lic, ""
X.Handler.slots = L.Slots(X.connect(db, schema=False), X.Handler.lock, "T-3", 2)
srv = ThreadingHTTPServer(("127.0.0.1", 0), X.Handler); srv.db_path = db
threading.Thread(target=srv.serve_forever, daemon=True).start()
url = f"http://127.0.0.1:{srv.server_address[1]}"
def post(rows):
    req = urllib.request.Request(url + "/ingest", json.dumps(rows).encode(), {"Content-Type": "application/json"})
    try: return 200, json.loads(urllib.request.urlopen(req, timeout=5).read())
    except urllib.error.HTTPError as e: return e.code, json.loads(e.read())
row = lambda c: {"crane": c, "t_ms": 1, "GT_Position": 1}
assert post([row("A"), row("B")]) == (200, {"ok": True, "inserted": 2})
assert post([row("C")])[0] == 403
code, r = post([row("A"), row("C")]); assert code == 200 and r["inserted"] == 1 and r["rejected"] == 1
st = json.loads(urllib.request.urlopen(url + "/license").read()); assert st["used"] == ["A", "B"] and st["cranes"] == 2
X.Handler.lic_doc = dict(lic, expires="2020-01-01")
assert post([row("A")])[0] == 402
srv.shutdown()
print("[test_license] PASS — 서명 3 · 만료/유예 3 · 자리 3 · /ingest 4")
