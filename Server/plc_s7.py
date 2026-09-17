#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
가상 S7 PLC + PLC 어댑터 — 실 PLC(㈜엠비이) 가 오기 전까지 'PLC 에 접속해 주소로 읽는' 경로를 진짜 S7 통신으로 돌린다.
오너 2026-09-17 "python-snap7 이걸로 작업하자" — PLCSIM Advanced 는 유료·Windows 전용이라 직접 만든다.

  sim      가상 PLC. S7 통신(TCP 102)으로 DB100(운영)·DB101(알람)을 열고 PlcSim CSV 를 실시간으로 써 넣는다.
  adapter  PLC 어댑터. PLC 에 접속해 DB100·DB101 을 주소로 읽고 통합서버 /ingest 에 넣는다.
           실 PLC 가 오면 --plc 주소만 바꾼다(아래 LAYOUT 이 벤더 주소표와 같다는 전제에서).
  check    LAYOUT 자기 검사 — 주소 겹침·고정점·CSV 전 행 인코딩 왕복. 네트워크·snap7 없이 돈다.

흐름: sim(또는 실 PLC) → adapter → xrcrane_db.py /ingest → /since → Unity ServerPlcSource → 크레인.
의존성: python-snap7==2.0.2 (sim·adapter 만). 나머지는 표준 라이브러리.
  ★ 버전 고정. 3.x 는 순수 파이썬 구현이라 register_area 가 버퍼를 복사한다 — sim 이 써도 클라이언트엔 0 만 보였다
    (2026-09-17 aiserver: 버전 미지정 pip 이 3.1.2 를 깔아 610행 전부 0). sim 은 기동 때 되읽어 확인한다.
"""
import argparse
import csv
import ctypes
import glob
import json
import os
import struct
import time
import urllib.request

# 주소표 — (태그, DB, 바이트, 비트, 형식). 형식 R=REAL(4) I=INT(2) X=BOOL. S7 은 빅엔디언.
# ★ 가정 주소다. 코드에 남은 벤더 주소는 4개뿐(ANCHORS)이라 그걸 고정점으로 두고 나머지를 축 블록으로 채웠다.
#   MBE-DOC-2026-XR-002(PLC 데이터 포인트 리스트)를 받으면 이 표만 바꾼다 — sim·adapter 가 같이 따라간다.
LAYOUT = [
    ("GT_Position", 100, 0, 0, "R"), ("GT_Velocity", 100, 4, 0, "R"),
    ("GT_Direction", 100, 8, 0, "X"), ("GT_Running", 100, 8, 1, "X"),
    ("GT_Brake_Released", 100, 8, 2, "X"), ("GT_Motor_Alarm", 100, 8, 3, "X"),

    ("TR_Position", 100, 30, 0, "R"), ("TR_Velocity", 100, 34, 0, "R"),
    ("TR_Direction", 100, 38, 0, "X"), ("TR_Running", 100, 38, 1, "X"),
    ("TR_Brake_Released", 100, 38, 2, "X"), ("TR_Motor_Alarm", 100, 38, 3, "X"),

    ("HO_Position", 100, 60, 0, "R"), ("HO_Velocity", 100, 64, 0, "R"),
    ("HO_Direction", 100, 68, 0, "X"), ("HO_Overload_Alarm", 100, 68, 1, "X"), ("HO_Snag_Alarm", 100, 68, 2, "X"),
    ("HO_Running", 100, 68, 3, "X"), ("HO_Brake_Released", 100, 68, 4, "X"), ("HO_Motor_Alarm", 100, 68, 5, "X"),
    ("HO_Load", 100, 70, 0, "R"),

    ("SP_Mode", 100, 100, 0, "I"),
    ("SP_TwistLock_Locked", 100, 102, 0, "X"), ("SP_TwistLock_Unlocked", 100, 102, 1, "X"),
    ("SP_Landed", 100, 102, 2, "X"), ("SP_Container_Detected", 100, 102, 3, "X"), ("SP_Mismatch_Alarm", 100, 102, 4, "X"),
    ("SP_Telescopic_Position", 100, 104, 0, "R"),

    ("OP_Mode", 100, 130, 0, "I"),
    ("OP_Power_On", 100, 132, 0, "X"), ("OP_Ready", 100, 132, 1, "X"), ("OP_Running", 100, 132, 2, "X"),
    ("OP_Standby", 100, 132, 3, "X"), ("OP_Emergency_Stop", 100, 132, 4, "X"), ("OP_AntiSway_Active", 100, 132, 5, "X"),
    ("OP_Cycle_Count_Today", 100, 134, 0, "I"),

    ("ENV_Wind_Speed", 100, 160, 0, "R"), ("ENV_Wind_Direction", 100, 164, 0, "R"), ("ENV_Wind_Alarm", 100, 168, 0, "X"),

    ("COM_Link_Status", 100, 200, 0, "X"),

    ("ALM_Active", 101, 0, 0, "X"), ("ALM_Latest_Code", 101, 2, 0, "I"),
    ("ALM_Latest_Severity", 101, 4, 0, "I"), ("ALM_Latest_Source", 101, 6, 0, "I"),
]
# 코드에 남아 있는 벤더 주소(IPlcSource.cs) — 표를 고쳐도 이건 어긋나면 안 된다.
ANCHORS = {"HO_Load": (100, 70, 0), "SP_Mode": (100, 100, 0), "OP_Mode": (100, 130, 0), "COM_Link_Status": (100, 200, 0)}
DBS = (100, 101)
DB_SIZE = 256
SIZE = {"R": 4, "I": 2, "X": 1}
HERE = os.path.dirname(os.path.abspath(__file__))


def encode(row, dbs):
    """태그→값(CSV 문자열이든 숫자든) 을 DB 바이트에 쓴다."""
    for tag, db, byte, bit, kind in LAYOUT:
        v = float(row.get(tag) or 0)
        b = dbs[db]
        if kind == "R":
            struct.pack_into(">f", b, byte, v)
        elif kind == "I":
            struct.pack_into(">h", b, byte, int(v))
        elif v:
            b[byte] |= 1 << bit
        else:
            b[byte] &= ~(1 << bit) & 0xFF


def decode(dbs):
    """DB 바이트 → 태그→값. BOOL 은 0/1 — 서버가 CSV 로 되돌려줄 때 Unity 파서가 int 로 읽는다('True' 는 0 이 된다)."""
    row = {}
    for tag, db, byte, bit, kind in LAYOUT:
        b = dbs[db]
        if kind == "R":
            row[tag] = round(struct.unpack_from(">f", b, byte)[0], 4)
        elif kind == "I":
            row[tag] = struct.unpack_from(">h", b, byte)[0]
        else:
            row[tag] = (b[byte] >> bit) & 1
    return row


def cmd_check(args):
    used = {}
    for tag, db, byte, bit, kind in LAYOUT:
        assert db in DBS and byte + SIZE[kind] <= DB_SIZE, f"범위 밖: {tag}"
        cells = [(db, byte, bit)] if kind == "X" else [(db, byte + i, k) for i in range(SIZE[kind]) for k in range(8)]
        for c in cells:
            assert c not in used, f"주소 겹침: {tag} ↔ {used[c]} @ DB{c[0]}.{c[1]}.{c[2]}"
            used[c] = tag
    for tag, addr in ANCHORS.items():
        assert next((db, byte, bit) for t, db, byte, bit, _ in LAYOUT if t == tag) == addr, f"고정점 어긋남: {tag}"

    files = args.csv or sorted(glob.glob(os.path.join(HERE, "..", "PlcSim", "output", "S*", "run_01.csv")))
    assert files, "검사할 CSV 가 없다"
    rows = 0
    for path in files:
        with open(path, newline="", encoding="utf-8") as f:
            reader = csv.DictReader(f)
            missing = set(reader.fieldnames) - {"t_ms"} - {t for t, *_ in LAYOUT}
            assert not missing, f"{path}: 주소표에 없는 태그 {sorted(missing)}"
            for row in reader:
                dbs = {db: bytearray(DB_SIZE) for db in DBS}
                encode(row, dbs)
                back = decode(dbs)
                for tag, _, _, _, kind in LAYOUT:
                    want = float(row[tag])
                    ok = abs(back[tag] - want) <= 1e-3 * max(1.0, abs(want)) if kind == "R" else back[tag] == int(want)
                    assert ok, f"{path} t_ms={row['t_ms']} {tag}: {want} → {back[tag]}"
                rows += 1
    print(f"[plc-s7] check OK — 태그 {len(LAYOUT)}개 겹침 0 · 고정점 {len(ANCHORS)}/{len(ANCHORS)} · CSV {len(files)}개 {rows}행 왕복 일치")


def cmd_sim(args):
    from snap7.server import Server
    from snap7.type import SrvArea

    with open(args.csv, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    times = [float(r["t_ms"]) / 1000 for r in rows]
    reals = [t for t, _, _, _, kind in LAYOUT if kind == "R"]
    bufs = {db: (ctypes.c_ubyte * DB_SIZE)() for db in DBS}
    dbs = {db: bytearray(DB_SIZE) for db in DBS}

    srv = Server(log=False)
    for db, buf in bufs.items():
        srv.register_area(SrvArea.DB, db, buf)
    srv.start(tcp_port=args.port)

    def publish(row):
        encode(row, dbs)
        for db, buf in bufs.items():
            srv.lock_area(SrvArea.DB, db)
            ctypes.memmove(buf, bytes(dbs[db]), DB_SIZE)
            srv.unlock_area(SrvArea.DB, db)

    # 기동 자기 확인 — 쓴 값이 S7 클라이언트에 그대로 보이는가. 라이브러리가 버퍼를 복사하면 여기서 멈춘다(위 ★).
    from snap7.client import Client
    publish(rows[0])
    probe = Client()
    probe.connect("127.0.0.1", 0, 1, args.port)
    seen = {db: bytes(probe.db_read(db, 0, DB_SIZE)) for db in DBS}
    probe.disconnect()
    if any(seen[db] != bytes(dbs[db]) for db in DBS):
        srv.stop()
        srv.destroy()
        raise SystemExit("[plc-s7] 기동 실패 — 쓴 DB 값이 S7 클라이언트에 안 보인다(python-snap7 버전 확인, 2.0.2 고정)")

    print(f"[plc-s7] 가상 PLC 기동 — TCP {args.port} · DB100/DB101 {DB_SIZE}바이트 · {args.csv} {len(rows)}행"
          f"{' 반복' if args.loop else ''} · 되읽기 확인 OK", flush=True)
    try:
        while True:
            t0, i = time.monotonic(), 0
            while (t := time.monotonic() - t0) <= times[-1]:
                while i + 1 < len(rows) and times[i + 1] <= t:
                    i += 1
                row = dict(rows[i])
                # 실 PLC 처럼 값이 연속으로 변하게 REAL 은 행 사이를 보간한다 — 100ms 계단이면 어댑터 표본 시각과 엇갈려
                #   같은 값을 두 번 읽고 다음에 두 칸 뛰어, 위치 미분이 가속 알람을 낸다.
                if i + 1 < len(rows) and times[i + 1] > times[i]:
                    k = (t - times[i]) / (times[i + 1] - times[i])
                    for tag in reals:
                        a = float(rows[i][tag] or 0)
                        row[tag] = a + (float(rows[i + 1][tag] or 0) - a) * k
                publish(row)
                time.sleep(args.scan)
            if not args.loop:
                break
    finally:
        srv.stop()
        srv.destroy()


def cmd_adapter(args):
    from snap7.client import Client

    cli = Client()
    url = args.url.rstrip("/") + "/ingest"
    source = f"s7/{args.plc}"
    t0 = time.monotonic()
    nxt, up, down_logged, sent = t0, False, False, 0
    while True:
        try:
            if not cli.get_connected():
                cli.connect(args.plc, args.rack, args.slot, args.port)
            dbs = {db: cli.db_read(db, 0, DB_SIZE) for db in DBS}
        except Exception as e:
            if not down_logged:   # 끊길 때 한 번만 — 1초마다 찍으면 로그가 묻힌다
                print(f"[plc-adapter] {args.plc}:{args.port} 읽기 실패: {e} — 1초마다 재접속", flush=True)
            up, down_logged = False, True
            try:
                cli.disconnect()
            except Exception:
                pass
            time.sleep(1)
            nxt = time.monotonic()
            continue
        if not up:
            print(f"[plc-adapter] {args.plc}:{args.port} 연결 — DB100·DB101 을 {args.period * 1000:.0f}ms 마다 읽어 {url} (crane={args.crane})", flush=True)
            up, down_logged = True, False

        # t_ms 는 어댑터가 읽은 시각 — PLC 엔 시나리오 시각이 없다. Unity 는 이 간격으로 보간한다.
        row = decode(dbs)
        row.update(crane=args.crane, source=source, t_ms=int((time.monotonic() - t0) * 1000))
        try:
            req = urllib.request.Request(url, json.dumps(row).encode("utf-8"), {"Content-Type": "application/json"})
            urllib.request.urlopen(req, timeout=2).read()
            sent += 1
            if sent % 600 == 0:
                print(f"[plc-adapter] {sent}행 적재", flush=True)
        except Exception as e:
            print(f"[plc-adapter] 서버 적재 실패: {e}", flush=True)
        nxt += args.period
        time.sleep(max(0.0, nxt - time.monotonic()))


def main():
    p = argparse.ArgumentParser(description="가상 S7 PLC + PLC 어댑터")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("sim", help="가상 PLC: CSV 를 DB100/DB101 에 실시간으로 써 넣는다")
    s.add_argument("csv")
    s.add_argument("--port", type=int, default=102)
    s.add_argument("--scan", type=float, default=0.01, help="DB 갱신 주기(초)")
    s.add_argument("--loop", action="store_true")
    s.set_defaults(func=cmd_sim)

    a = sub.add_parser("adapter", help="PLC 어댑터: DB100/DB101 을 읽어 /ingest 에 넣는다")
    a.add_argument("--plc", default="127.0.0.1")
    a.add_argument("--port", type=int, default=102)
    a.add_argument("--rack", type=int, default=0)
    a.add_argument("--slot", type=int, default=1)   # S7-1500 CPU 슬롯
    a.add_argument("--url", default="http://127.0.0.1:5006")
    a.add_argument("--crane", default="STS_Crane")
    a.add_argument("--period", type=float, default=0.1, help="읽기 주기(초) — 벤더 푸시 주기 100ms")
    a.set_defaults(func=cmd_adapter)

    c = sub.add_parser("check", help="주소표 자기 검사(네트워크 없음)")
    c.add_argument("csv", nargs="*")
    c.set_defaults(func=cmd_check)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
