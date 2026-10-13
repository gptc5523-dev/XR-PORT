#!/usr/bin/env bash
# 통합서버 설치(WBS 8.2) — Server/install.sh <ssh 호스트> [설치 폴더=xrcrane-db] [--adopt] [--ai 폴더] [--image tar] [--license 파일]: 파일을 올리고 대상 자체 시험이 통과하면 docker compose 로 띄운다(--adopt = 손으로 띄운 같은 이름 컨테이너 교체, 나머지 셋은 새 현장 폐쇄망 설치 — 설치 키트 Tools/make_kit.sh 가 채운다).
set -euo pipefail
cd "$(dirname "$0")/.."
ADOPT=0; AI=""; IMAGE=""; LIC=""; ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --adopt) ADOPT=1 ;;
    --ai) AI=$2; shift ;;          # models/·wheels/ 가 든 폴더 → 대상 ~/xrcrane-ai (compose 기본 XRCRANE_AI_DIR=../xrcrane-ai)
    --image) IMAGE=$2; shift ;;    # docker save 한 python:3.12-slim — 대상에 이미지가 없을 때만 load(폐쇄망은 pull 불가)
    --license) LIC=$2; shift ;;    # 공급사가 발급한 license.json(없으면 평가판 1대)
    *) ARGS+=("$1") ;;
  esac; shift
done
H=${ARGS[0]:?사용: Server/install.sh <ssh 호스트> [설치 폴더] [--adopt]}; D=${ARGS[1]:-xrcrane-db}
FILES=(Server/xrcrane_db.py Server/xrcrane_license.py Server/ed25519.py Server/test_license.py Server/parts.json Server/register.html Server/plc_s7.py Server/test_parts.py Server/test_events.py Server/test_summary.py
       PlcSim/live.py PlcSim/generate.py PlcSim/live_layout.json Assets/Crane/Resources/Sts/AlarmCodebook.json
       PlcSim/serve_ai.py PlcSim/gen_ai.py PlcSim/train_ai.py)

ssh "$H" "mkdir -p $D/app $D/data && cd $D && { [ -f users.json ] || echo '[]' > users.json; } && { [ -f license.json ] || echo '{}' > license.json; } && { [ -f .env ] || printf 'XRCRANE_TOKEN=\nXRCRANE_RETAIN_HOURS=3\nXRCRANE_USERS_FILE=./users.json\n' > .env; }"
rsync -a "${FILES[@]}" "$H:$D/app/"
rsync -a Server/docker-compose.yml "$H:$D/"
[ -n "$LIC" ] && rsync -a "$LIC" "$H:$D/license.json" && echo "[install] 라이선스 → $H:$D/license.json"
[ -n "$AI" ] && ssh "$H" "mkdir -p xrcrane-ai" && rsync -a "$AI/models" "$AI/wheels" "$H:xrcrane-ai/" && echo "[install] AI 모델·휠 → $H:xrcrane-ai"
if [ -n "$IMAGE" ] && ! ssh "$H" "docker image inspect python:3.12-slim >/dev/null 2>&1"; then
  ssh "$H" "docker load" < "$IMAGE" && echo "[install] 이미지 python:3.12-slim 적재(폐쇄망)"
fi
echo "[install] 파일 → $H:$D (app ${#FILES[@]}개)"

ssh "$H" "cd $D/app && python3 test_parts.py >/dev/null && python3 test_events.py >/dev/null 2>&1 && python3 test_summary.py >/dev/null && python3 test_license.py >/dev/null 2>&1" || { echo "[install] 대상 자체 시험 실패 — 띄우지 않는다"; exit 1; }
echo "[install] 자체 시험 통과"

# 같은 이름이 compose 밖에서 떠 있으면 compose 가 못 띄운다 — --adopt 일 때만 내린다(데이터는 ./data 그대로).
for c in xrcrane-db xrcrane-plc xrcrane-ai; do
  if ssh "$H" "docker inspect $c >/dev/null 2>&1 && [ -z \"\$(docker inspect -f '{{index .Config.Labels \"com.docker.compose.project\"}}' $c)\" ]"; then
    [ $ADOPT -eq 1 ] || { echo "[install] $c 가 compose 밖에서 떠 있다 — --adopt 로 다시"; exit 1; }
    ssh "$H" "docker rm -f $c >/dev/null" && echo "[install] 손으로 띄운 $c 내림"
  fi
done

ssh "$H" "cd $D && docker compose up -d --force-recreate && for i in \$(seq 30); do curl -sf -m2 localhost:5006/health && exit 0; sleep 1; done; exit 1" \
  && echo && echo "[install] 완료 — http://$H:5006/health" || { echo "[install] 서버가 응답하지 않는다 — docker compose logs 확인"; exit 1; }

# 상태 요약 추론(xrcrane-ai) — 모델이 없으면 꺼진 채로 두고 클라이언트는 규칙으로 표시한다(설치 실패로 보지 않는다).
ssh "$H" "for i in \$(seq 60); do docker logs xrcrane-ai 2>&1 | grep -q '^\[serve_ai\] :' && exit 0; sleep 1; done; exit 1" \
  && echo "[install] AI 추론 서비스 기동" || echo "[install] 경고: xrcrane-ai 가 뜨지 않았다(~/xrcrane-ai/models·wheels 확인) — /summary 는 503, 클라이언트는 규칙"
