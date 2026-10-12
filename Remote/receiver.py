#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""크레인 리모컨 수신기 — Beam Pro 리모컨 앱의 UDP 신호를 이 PC의 키 입력(방향키·Enter)으로 바꾼다.

  python3 receiver.py [--port 5017] [--print]     --print 는 키를 누르지 않고 받은 신호만 출력한다(시험용).
  python3 receiver.py test                         해석 규칙 자체 점검.

메시지: "D <키>" 누름, "U <키>" 뗌, "PING" 연결 확인, "ID BEAM:<기기번호>" 안경 기기 ID — 휴대폰 IP 별로 기억했다가 Sunshine 이 스트리밍 중인 IP 의 ID 를 glasses_device.txt 에 적는다(관전 앱이 등록을 찾는다). 키 = FWD BACK STRAFE_L STRAFE_R TURN_L TURN_R LOOK_UP LOOK_DOWN OK CRANE1~3.
사설망·루프백에서 온 신호만 받는다(폐쇄망 전제). Windows 는 SendInput, macOS 는 CGEvent, Linux 는 X11 XTest — 표준 라이브러리만 쓴다.
"""
import argparse
import ctypes
import ipaddress
import os
import re
import socket
import sys

# 리모컨 → 크레인 평면 모드 키: 조이스틱 = W·A·S·D(이동), 좌·우 회전 = ←·→, 고개 = ↑·↓, 선택 = Enter, STS·RTG1·RTG2 = 1·2·3
KEYS = {"FWD", "BACK", "STRAFE_L", "STRAFE_R", "TURN_L", "TURN_R", "LOOK_UP", "LOOK_DOWN", "OK", "CRANE1", "CRANE2", "CRANE3", "CENTER"}
ARROWS = {"TURN_L", "TURN_R", "LOOK_UP", "LOOK_DOWN"}
WIN_VK = {"FWD": 0x57, "BACK": 0x53, "STRAFE_L": 0x41, "STRAFE_R": 0x44, "TURN_L": 0x25, "TURN_R": 0x27, "LOOK_UP": 0x26, "LOOK_DOWN": 0x28, "OK": 0x0D, "CRANE1": 0x31, "CRANE2": 0x32, "CRANE3": 0x33, "CENTER": 0x52}
MAC_KC = {"FWD": 13, "BACK": 1, "STRAFE_L": 0, "STRAFE_R": 2, "TURN_L": 123, "TURN_R": 124, "LOOK_UP": 126, "LOOK_DOWN": 125, "OK": 36, "CRANE1": 18, "CRANE2": 19, "CRANE3": 20, "CENTER": 15}
X_SYM = {"FWD": 0x77, "BACK": 0x73, "STRAFE_L": 0x61, "STRAFE_R": 0x64, "TURN_L": 0xFF51, "TURN_R": 0xFF53, "LOOK_UP": 0xFF52, "LOOK_DOWN": 0xFF54, "OK": 0xFF0D, "CRANE1": 0x31, "CRANE2": 0x32, "CRANE3": 0x33, "CENTER": 0x72}


def parse(msg):
    parts = msg.strip().split()
    if parts == ["PING"]:
        return ("PING", None)
    if len(parts) == 2 and parts[0] == "ID" and re.fullmatch(r"BEAM:[0-9a-f]{8,32}", parts[1]):
        return ("ID", parts[1])
    if len(parts) == 2 and parts[0] in ("D", "U") and parts[1] in KEYS:
        return (parts[0], parts[1])
    return None


DEVICE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "glasses_device.txt")   # 크레인 앱 폴더에 둔다 — 관전 앱이 읽는다
APP_PORT = 6000   # 관전 앱 자리 k 의 리모컨 직접 입력 포트 = 6000+k (RemoteKeys.cs)
PEER_RE = re.compile(r"Control peer address \[([0-9.]+):\d+\]")


def streaming_ip(lines):
    """Sunshine 로그 줄들에서 마지막으로 스트리밍에 붙은 휴대폰 IP — 없으면 None."""
    ip = None
    for l in lines:
        m = PEER_RE.search(l)
        if m:
            ip = m.group(1)
    return ip


def pick_id(ids, stream_ip, last_id):
    """관전 앱에 넘길 기기 ID — 지금 스트리밍 중인 IP 의 ID, 모르면 마지막으로 받은 ID."""
    return ids.get(stream_ip) or last_id


def seat_for(seats, ip):
    """키를 보낼 자리 — 그 휴대폰이 스트리밍 중인 자리. 자리가 여럿인데 모르면 None(남의 자리를 움직이지 않는다)."""
    for st in seats:
        if st["ip"] == ip:
            return st
    return seats[0] if len(seats) == 1 else None


def seat_file(i):
    """자리 i(1부터)의 기기 ID 파일 — 1번은 예전 이름도 같이 쓴다(관전 앱 1대 시절 호환)."""
    return DEVICE_FILE if i == 1 else DEVICE_FILE.replace(".txt", f"_{i}.txt")


VPN = ipaddress.ip_network("100.64.0.0/10")   # Tailscale — 다른 지역 사용자도 서버 VPN 으로 붙는다


def allowed(ip):
    a = ipaddress.ip_address(ip)
    return a.is_private or a.is_loopback or a in VPN


def make_presser(display=None):
    if sys.platform == "win32":
        class KI(ctypes.Structure):
            _fields_ = [("wVk", ctypes.c_ushort), ("wScan", ctypes.c_ushort), ("dwFlags", ctypes.c_ulong),
                        ("time", ctypes.c_ulong), ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong))]

        class INPUT(ctypes.Structure):
            _fields_ = [("type", ctypes.c_ulong), ("ki", KI), ("pad", ctypes.c_ubyte * 8)]

        def press(key, down):
            flags = (0 if down else 0x0002) | (0x0001 if key in ARROWS else 0)   # KEYUP · EXTENDEDKEY(방향키)
            inp = INPUT(type=1, ki=KI(wVk=WIN_VK[key], dwFlags=flags))
            ctypes.windll.user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(inp))
        return press
    if sys.platform == "darwin":
        cg = ctypes.CDLL("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")
        cg.CGEventCreateKeyboardEvent.restype = ctypes.c_void_p
        cg.CGEventCreateKeyboardEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint16, ctypes.c_bool]
        cg.CGEventPost.argtypes = [ctypes.c_uint32, ctypes.c_void_p]
        cf = ctypes.CDLL("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")
        cf.CFRelease.argtypes = [ctypes.c_void_p]

        def press(key, down):
            e = cg.CGEventCreateKeyboardEvent(None, MAC_KC[key], down)
            cg.CGEventPost(0, e)
            cf.CFRelease(e)
        return press
    if sys.platform.startswith("linux"):
        x = ctypes.CDLL("libX11.so.6")
        xt = ctypes.CDLL("libXtst.so.6")
        x.XOpenDisplay.restype = ctypes.c_void_p
        x.XOpenDisplay.argtypes = [ctypes.c_char_p]
        x.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
        x.XFlush.argtypes = [ctypes.c_void_p]
        xt.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
        dpy = x.XOpenDisplay(display.encode() if display else None)
        if not dpy:
            raise SystemExit("X 디스플레이를 열 수 없습니다 — DISPLAY 환경변수를 크레인 앱 화면(예: :1)으로 주세요")

        def press(key, down):
            xt.XTestFakeKeyEvent(dpy, x.XKeysymToKeycode(dpy, X_SYM[key]), down, 0)
            x.XFlush(dpy)
        return press
    raise SystemExit(f"지원하지 않는 OS: {sys.platform}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", nargs="?")
    ap.add_argument("--port", type=int, default=5017)
    ap.add_argument("--print", action="store_true", help="키를 누르지 않고 출력만")
    ap.add_argument("--seat", action="append", default=[], metavar="LOG",
                    help="관전 자리(순서대로 1·2·…)의 Sunshine 로그. 있으면 키를 그 자리 관전 앱 127.0.0.1:(6000+자리)로 넘긴다(앱이 직접 받음). 없으면 예전처럼 $DISPLAY 에 XTest 한 자리")
    a = ap.parse_args()
    if a.cmd == "test":
        assert parse("D FWD") == ("D", "FWD") and parse("U OK") == ("U", "OK") and parse("PING") == ("PING", None)
        assert parse("ID BEAM:3d3db192cc110f41") == ("ID", "BEAM:3d3db192cc110f41") and parse("ID ../x") is None and parse("ID BEAM:XYZ") is None
        assert parse("D JUMP") is None and parse("X UP") is None and parse("") is None
        assert streaming_ip(["x", "[t]: Debug: Control peer address [192.168.0.200:59806]", "CLIENT CONNECTED"]) == "192.168.0.200"
        assert streaming_ip(["nothing"]) is None
        ids = {"192.168.0.69": "BEAM:aaaaaaaa", "192.168.0.200": "BEAM:bbbbbbbb"}
        assert pick_id(ids, "192.168.0.200", "BEAM:aaaaaaaa") == "BEAM:bbbbbbbb" and pick_id(ids, None, "BEAM:aaaaaaaa") == "BEAM:aaaaaaaa"
        seats = [{"ip": "192.168.0.69"}, {"ip": "192.168.0.200"}]
        assert seat_for(seats, "192.168.0.200") is seats[1] and seat_for(seats, "10.0.0.9") is None and seat_for(seats[:1], "10.0.0.9") is seats[0]
        assert seat_file(1).endswith("glasses_device.txt") and seat_file(2).endswith("glasses_device_2.txt")
        assert allowed("192.168.0.69") and allowed("127.0.0.1") and allowed("100.93.77.48") and not allowed("8.8.8.8")
        print("ok")
        return
    forward = bool(a.seat)
    specs = a.seat or [os.path.expanduser("~/wivrn-logs/sunshine.log")]
    seats = []
    for i, spec in enumerate(specs, 1):
        log = spec.partition("=")[0]
        seats.append({"n": i, "log": os.path.expanduser(log), "display": os.environ.get("DISPLAY") or None, "pos": 0, "ip": None,
                      "written": None, "press": None, "port": APP_PORT + i})
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("0.0.0.0", a.port))
    s.settimeout(1.0)
    print(f"[remote] UDP {a.port} 대기 — {'출력만' if a.print else '키 입력'}, 자리 {len(seats)}개", flush=True)
    ids, last_id = {}, None
    while True:
        for st in seats:
            try:
                with open(st["log"], "rb") as f:
                    f.seek(0, 2)
                    if f.tell() < st["pos"]:
                        st["pos"] = 0
                    f.seek(st["pos"])
                    new = f.read().decode("utf-8", "replace").splitlines()
                    st["pos"] = f.tell()
                ip = streaming_ip(new)
                if ip:   # 휴대폰이 자리를 옮기면 옛 자리에서 지운다 — 남으면 키가 옛 자리로 간다
                    for o in seats:
                        if o["ip"] == ip:
                            o["ip"] = None
                    st["ip"] = ip
            except OSError:
                pass
            want = pick_id(ids, st["ip"], last_id if len(seats) == 1 else None)   # 자리가 여럿이면 스트리밍 IP 로만 — 남의 ID 를 1번에 적던 것
            if want and want != st["written"]:
                with open(seat_file(st["n"]), "w") as f:
                    f.write(want)
                st["written"] = want
                print(f"[remote] 자리{st['n']} 관전 기기 {want} (스트리밍 {st['ip'] or '모름'})", flush=True)
        try:
            data, (ip, _) = s.recvfrom(64)
        except socket.timeout:
            continue
        if not allowed(ip):
            continue
        p = parse(data.decode("utf-8", "replace"))
        if p is None:
            continue
        print(f"[remote] {ip} {p[0]} {p[1] or ''}", flush=True)
        if p[0] == "ID":
            ids[ip], last_id = p[1], p[1]
            continue
        st = seat_for(seats, ip)
        if st is None:
            print(f"[remote] {ip} 스트리밍 중인 자리 모름 — 키 버림", flush=True)
            continue
        if forward:
            if p[1] and not a.print:
                s.sendto(f"{p[0]} {p[1]}".encode(), ("127.0.0.1", st["port"]))
            continue
        if st["press"] is None and not a.print:
            try:
                st["press"] = make_presser(st["display"])   # 그 자리 화면이 늦게 떠도 키가 올 때 다시 연다
            except SystemExit as e:
                print(f"[remote] 자리{st['n']} 화면 {st['display']} 열기 실패 — {e}", flush=True)
        if st["press"] and p[1]:
            st["press"](p[1], p[0] == "D")

if __name__ == "__main__":
    main()
