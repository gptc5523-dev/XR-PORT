#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""서버 라이선스(WBS 8.3) — 크레인 단위 과금. 공급사가 서명한 license.json 을 통합서버가 기동 때 검증한다.

license.json = {"license": {...}, "sig": "<Ed25519 서명 hex>"}, license 필드:
  license_id · licensee(고객) · site(현장) · cranes(허용 크레인 수) · issued · expires(YYYY-MM-DD) · grace_days(만료 뒤 유예, 기본 14)
판정: 서명이 맞으면 '정식' — 크레인은 처음 데이터를 보낸 순서로 cranes 대까지 자리를 받고(재시작해도 유지), 넘는 크레인은 거부.
      만료 뒤 유예 기간은 경고만, 유예가 지나면 적재를 받지 않는다. 파일이 없거나 서명이 틀리면 '평가판' — 크레인 1대.

공급사 도구(개인키는 저장소 밖 ~/.xrcrane-license/ 에만):
  python3 Server/xrcrane_license.py keygen
  python3 Server/xrcrane_license.py issue --licensee 고객 --site 현장 --cranes 3 --expires 2027-12-31 --out license.json
  python3 Server/xrcrane_license.py verify license.json
"""
import argparse, datetime, json, os, secrets, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ed25519  # noqa: E402

PUBKEY = "7d8e2584e88a171bcf2db842f61ec4bc07bea22eb6d6c3ee3e3a00c7c59d303a"   # 공급사 공개키(2026-10-13 keygen) — 개인키는 ~/.xrcrane-license/
KEY_PATH = os.path.expanduser("~/.xrcrane-license/vendor_ed25519.key")
EVAL_CRANES = 1
SLOTS_SQL = "CREATE TABLE IF NOT EXISTS license_slots (crane TEXT PRIMARY KEY, license_id TEXT NOT NULL, first_ms INTEGER NOT NULL)"


def canonical(lic):
    return json.dumps(lic, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def load(path, pubkey=PUBKEY):
    """(license dict 또는 None, 문제 설명) — 파일 없음·빈 파일·서명 불일치는 None."""
    if not path or not os.path.exists(path): return None, "라이선스 파일 없음"
    try:
        doc = json.load(open(path, encoding="utf-8"))
    except (OSError, ValueError) as e:
        return None, f"라이선스 파일을 못 읽음: {e}"
    if not doc: return None, "라이선스 없음(빈 파일)"
    lic, sig = doc.get("license"), doc.get("sig", "")
    try:
        ok = isinstance(lic, dict) and ed25519.verify(bytes.fromhex(pubkey), canonical(lic), bytes.fromhex(sig))
    except ValueError:
        ok = False
    if not ok: return None, "서명 불일치 — 공급사 발급본이 아니거나 내용이 바뀜"
    for k in ("license_id", "cranes", "expires"):
        if k not in lic: return None, f"필드 없음: {k}"
    return lic, ""


def status(lic, err, today=None):
    """판정 결과 — accept=False 면 적재를 받지 않는다."""
    today = today or datetime.date.today()
    if lic is None:
        return {"mode": "평가판", "cranes": EVAL_CRANES, "license_id": "EVAL", "accept": True, "state": "ok", "note": err}
    exp = datetime.date.fromisoformat(lic["expires"]); grace = int(lic.get("grace_days", 14))
    left = (exp - today).days
    state = "ok" if left >= 0 else "grace" if left >= -grace else "expired"
    return {"mode": "정식", "license_id": lic["license_id"], "licensee": lic.get("licensee", ""), "site": lic.get("site", ""),
            "cranes": int(lic["cranes"]), "expires": lic["expires"], "grace_days": grace, "days_left": left,
            "state": state, "accept": state != "expired",
            "note": "" if state == "ok" else f"만료 {-left}일 지남 — 유예 {grace}일" if state == "grace" else "만료·유예 지남 — 적재 중지"}


class Slots:
    """크레인 자리 — 처음 데이터를 보낸 순서로 limit 대까지. 라이선스 번호가 바뀌면 자리를 새로 매긴다."""
    def __init__(self, con, lock, license_id, limit):
        self.con, self.lock, self.limit = con, lock, limit
        with lock:
            con.execute(SLOTS_SQL)
            con.execute("DELETE FROM license_slots WHERE license_id != ?", (license_id,))
            con.commit()
            self.held = {r[0] for r in con.execute("SELECT crane FROM license_slots")}
        self.license_id = license_id

    def allowed(self, crane, now_ms):
        if crane in self.held: return True
        with self.lock:
            if crane in self.held: return True
            if len(self.held) >= self.limit: return False
            self.con.execute("INSERT OR IGNORE INTO license_slots VALUES (?,?,?)", (crane, self.license_id, now_ms))
            self.con.commit(); self.held.add(crane)
        return True


def main():
    ap = argparse.ArgumentParser(description="XR 크레인 서버 라이선스 — 공급사 발급 도구")
    sub = ap.add_subparsers(dest="cmd", required=True)
    k = sub.add_parser("keygen", help="공급사 키 만들기(이미 있으면 공개키만 출력)")
    k.add_argument("--key", default=KEY_PATH)
    i = sub.add_parser("issue", help="라이선스 발급")
    i.add_argument("--key", default=KEY_PATH)
    i.add_argument("--licensee", required=True); i.add_argument("--site", required=True)
    i.add_argument("--cranes", type=int, required=True); i.add_argument("--expires", required=True)
    i.add_argument("--grace", type=int, default=14); i.add_argument("--id", default=None)
    i.add_argument("--out", required=True)
    v = sub.add_parser("verify", help="발급본 확인")
    v.add_argument("file")
    a = ap.parse_args()
    if a.cmd == "keygen":
        if not os.path.exists(a.key):
            os.makedirs(os.path.dirname(a.key), exist_ok=True)
            fd = os.open(a.key, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            os.write(fd, secrets.token_bytes(32).hex().encode()); os.close(fd)
            print(f"[license] 개인키 생성 → {a.key} (백업 필수, 저장소에 넣지 말 것)")
        print("PUBKEY =", ed25519.public_key(bytes.fromhex(open(a.key).read().strip())).hex())
    elif a.cmd == "issue":
        datetime.date.fromisoformat(a.expires)
        sk = bytes.fromhex(open(a.key).read().strip())
        if ed25519.public_key(sk).hex() != PUBKEY: sys.exit("[license] 이 개인키는 코드의 PUBKEY 와 짝이 아니다")
        lic = {"license_id": a.id or f"XRC-{datetime.date.today():%Y%m%d}-{secrets.token_hex(3)}", "licensee": a.licensee,
               "site": a.site, "cranes": a.cranes, "issued": f"{datetime.date.today()}", "expires": a.expires, "grace_days": a.grace}
        json.dump({"license": lic, "sig": ed25519.sign(sk, canonical(lic)).hex()}, open(a.out, "w", encoding="utf-8"),
                  ensure_ascii=False, indent=1)
        print(f"[license] 발급 {lic['license_id']} · {a.licensee}/{a.site} · 크레인 {a.cranes}대 · {a.expires}까지 → {a.out}")
    else:
        lic, err = load(a.file)
        print(json.dumps(status(lic, err), ensure_ascii=False, indent=1))
        sys.exit(0 if lic else 1)


if __name__ == "__main__":
    main()
