#!/usr/bin/env bash
# 배치 반복시험(WBS 7.2) — Tools/batch_tests.sh [--kpi] [도구 ...]: 서버 자체 시험 + Unity 배치 점검(--kpi 면 지표 1·2·4·6 측정까지)을 돌려 KPI/batch_<시각>.txt 에 요약, 실패 있으면 종료 코드 1(에디터는 닫고).
set -uo pipefail
cd "$(dirname "$0")/.."
P="$PWD"
VER=$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)
U="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
KPI=0; TOOLS=()
for a in "$@"; do [ "$a" = --kpi ] && KPI=1 || TOOLS+=("$a"); done
[ ${#TOOLS[@]} -eq 0 ] && TOOLS=(RtgControlSmoke HostStartProbe PlcReplaySmoke StsGrabProbe FloorClipProbe PlcServerSmoke)
[ $KPI -eq 1 ] && TOOLS+=(Kpi1Measure Kpi2Measure Kpi4Measure Kpi6Measure)   # 100시행씩이라 오래 걸린다 — 지표1·6 은 로컬 서버·PLC 대역을 스스로 띄운다
export XRCRANE_SERVER_URL="${XRCRANE_SERVER_URL:-http://192.168.0.167:5006}"   # 서버 구동 점검용 — 씬 기본값(Tailscale)은 이 Mac 에서 안 닿을 수 있다
STAMP=$(date +%Y%m%d_%H%M%S); OUT="KPI/batch_$STAMP.txt"; LOGS="Logs/batch_$STAMP"; mkdir -p KPI "$LOGS"
[ -x "$U" ] || { echo "Unity $VER 없음: $U"; exit 2; }
pgrep -f "Unity -projectpath $P" >/dev/null && { echo "Unity 에디터가 이 프로젝트를 열고 있다 — 닫고 다시"; exit 2; }

fail=0
row() { echo "$1" | tee -a "$OUT"; }
row "# 배치 반복시험 $STAMP · 커밋 $(git rev-parse --short HEAD) · Unity $VER · 서버 $XRCRANE_SERVER_URL"

for t in test_parts.py test_events.py; do
  if (cd Server && python3 "$t" >/dev/null 2>&1); then row "PASS Server/$t"; else row "FAIL Server/$t"; fail=1; fi
done
if python3 Remote/receiver.py test >/dev/null 2>&1; then row "PASS Remote/receiver.py test"; else row "FAIL Remote/receiver.py test"; fail=1; fi

for t in "${TOOLS[@]}"; do
  start=$(date +%s)
  "$U" -batchmode -projectPath "$P" -executeMethod "AIXRCrane.Crane.Sts.EditorTools.$t.Run" -logFile "$LOGS/$t.log"
  code=$?
  line=$(grep -aE "^\[$t\] (PASS|FAIL)" "$LOGS/$t.log" | tail -1 | cut -c1-200)
  if [ $code -eq 0 ] && [[ "$line" == *PASS* ]]; then row "PASS $t ($(( $(date +%s) - start ))s) ${line#*— }"
  else row "FAIL $t exit=$code ($(( $(date +%s) - start ))s) ${line:-결과 줄 없음 — $LOGS/$t.log}"; fail=1; fi
done

row "# 결과: $([ $fail -eq 0 ] && echo 전부 PASS || echo 실패 있음) — 로그 $LOGS"
exit $fail
