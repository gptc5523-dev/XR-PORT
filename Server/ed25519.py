# -*- coding: utf-8 -*-
"""Ed25519 서명·검증 — RFC 8032 §6 참조 구현 그대로(표준 라이브러리만). 라이선스 파일 한 장을 기동 때 한 번 검증하는 용도라 속도는 상관없다.
상수 시간 구현이 아니다 — 서명(개인키 사용)은 공급사 PC 에서만 하고, 서버는 검증(공개키)만 한다."""
import hashlib

p = 2 ** 255 - 19
q = 2 ** 252 + 27742317777372353535851937790883648493


def _inv(x):
    return pow(x, p - 2, p)


d = -121665 * _inv(121666) % p
_SQRT_M1 = pow(2, (p - 1) // 4, p)


def _sha512(s):
    return hashlib.sha512(s).digest()


def _add(P, Q):
    A, B = (P[1] - P[0]) * (Q[1] - Q[0]) % p, (P[1] + P[0]) * (Q[1] + Q[0]) % p
    C, D = 2 * P[3] * Q[3] * d % p, 2 * P[2] * Q[2] % p
    E, F, G_, H = B - A, D - C, D + C, B + A
    return (E * F, G_ * H, F * G_, E * H)


def _mul(s, P):
    Q = (0, 1, 1, 0)
    while s > 0:
        if s & 1: Q = _add(Q, P)
        P = _add(P, P); s >>= 1
    return Q


def _equal(P, Q):
    return (P[0] * Q[2] - Q[0] * P[2]) % p == 0 and (P[1] * Q[2] - Q[1] * P[2]) % p == 0


def _recover_x(y, sign):
    if y >= p: return None
    x2 = (y * y - 1) * _inv(d * y * y + 1)
    if x2 == 0: return None if sign else 0
    x = pow(x2, (p + 3) // 8, p)
    if (x * x - x2) % p != 0: x = x * _SQRT_M1 % p
    if (x * x - x2) % p != 0: return None
    if (x & 1) != sign: x = p - x
    return x


_gy = 4 * _inv(5) % p
_gx = _recover_x(_gy, 0)
G = (_gx, _gy, 1, _gx * _gy % p)


def _compress(P):
    zi = _inv(P[2]); x, y = P[0] * zi % p, P[1] * zi % p
    return int.to_bytes(y | ((x & 1) << 255), 32, "little")


def _decompress(s):
    if len(s) != 32: return None
    y = int.from_bytes(s, "little"); sign = y >> 255; y &= (1 << 255) - 1
    x = _recover_x(y, sign)
    return None if x is None else (x, y, 1, x * y % p)


def _expand(secret):
    h = _sha512(secret); a = int.from_bytes(h[:32], "little")
    a &= (1 << 254) - 8; a |= 1 << 254
    return a, h[32:]


def _hq(s):
    return int.from_bytes(_sha512(s), "little") % q


def public_key(secret):
    return _compress(_mul(_expand(secret)[0], G))


def sign(secret, msg):
    a, prefix = _expand(secret); A = _compress(_mul(a, G))
    r = _hq(prefix + msg); Rs = _compress(_mul(r, G))
    s = (r + _hq(Rs + A + msg) * a) % q
    return Rs + int.to_bytes(s, 32, "little")


def verify(public, msg, sig):
    if len(public) != 32 or len(sig) != 64: return False
    A, R = _decompress(public), _decompress(sig[:32])
    if A is None or R is None: return False
    s = int.from_bytes(sig[32:], "little")
    if s >= q: return False
    return _equal(_mul(s, G), _add(R, _mul(_hq(sig[:32] + public + msg), A)))
