#!/usr/bin/env bash
# 통합서버 기동(WBS 8.2) — 같은 폴더의 xrcrane.env 를 읽어 xrcrane_db.py serve 를 띄운다. 사용: ./serve.sh [추가 인자]
set -euo pipefail
cd "$(dirname "$0")"

if [ -f xrcrane.env ]; then set -a; . ./xrcrane.env; set +a; else echo "[serve] xrcrane.env 없음 — 기본값으로 뜸(xrcrane.env.example 참고)" >&2; fi

if [ -n "${XRCRANE_TOKEN:-}" ]; then tok="설정됨"; else tok="없음"; fi
echo "[serve] 포트 ${XRCRANE_PORT:-5006} · DB ${XRCRANE_DB:-/data/xrcrane.db} · 토큰 ${tok} · 보존 ${XRCRANE_RETAIN_HOURS:-3}시간" >&2
exec python3 xrcrane_db.py serve "$@"
