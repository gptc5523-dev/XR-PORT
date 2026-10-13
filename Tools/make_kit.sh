#!/usr/bin/env bash
# 단말별 설치 키트(WBS 8.2) — Tools/make_kit.sh [원본 서버=aiserver-lan]: 서버(통합서버+AI+크레인 앱)·Quest·안경·폰 폴더를 Build/Kit/<날짜_커밋>/ 에 묶는다. 폐쇄망 현장에 들고 가는 한 폴더.
set -euo pipefail
cd "$(dirname "$0")/.."
SRC=${1:-aiserver-lan}
HASH=$(git rev-parse --short HEAD); VER=$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)
K="Build/Kit/AI-XR-Crane_$(date +%Y%m%d)_$HASH"
[ -z "$(git status --porcelain -- Server PlcSim Assets/Crane/Resources)" ] || { echo "[kit] Server·PlcSim 에 커밋 안 한 변경이 있다 — 커밋 뒤 다시"; exit 1; }
[ -f Build/Win/AI-XR-Crane.exe ] || { echo "[kit] Build/Win 없음 — 메뉴 'Tool/서버 배포용 Windows 빌드' 먼저"; exit 1; }
[ -f Remote/build/CraneRemote.apk ] || Remote/build.sh
rm -rf "$K"; mkdir -p "$K"/{server,quest,glasses,phone}

# 서버: install.sh 가 저장소 경로 그대로 읽으므로 같은 구조로 복사(git 에 든 것만).
git archive HEAD Server PlcSim/live.py PlcSim/generate.py PlcSim/live_layout.json PlcSim/serve_ai.py PlcSim/gen_ai.py PlcSim/train_ai.py \
  Assets/Crane/Resources/Sts/AlarmCodebook.json | tar -x -C "$K/server"
rsync -a Build/Win/ "$K/server/app/" --exclude='glasses_device*.txt'
mkdir -p "$K/server/xrcrane-ai"
rsync -a "$SRC:xrcrane-ai/models" "$SRC:xrcrane-ai/wheels" "$K/server/xrcrane-ai/"
ssh "$SRC" "docker save python:3.12-slim | gzip -1" > "$K/server/python-3.12-slim.tar.gz"
cp Remote/build/CraneRemote.apk "$K/glasses/"
cp Remote/receiver.py "$K/server/remote_receiver.py"

cat > "$K/VERSION.txt" <<EOF
AI-XR-Crane 설치 키트
커밋 $HASH · 만든 날 $(date +%F) · Unity $VER · WiVRn 26.9 · Moonlight(Android) · Docker 이미지 python:3.12-slim
크레인 앱 md5 $(md5 -q Build/Win/AI-XR-Crane_Data/Managed/AIXRCrane.Crane.dll) (AIXRCrane.Crane.dll)
EOF

cat > "$K/README.md" <<'EOF'
# AI-XR-Crane 설치 키트

폐쇄망 현장용 — 인터넷 없이 이 폴더만으로 설치한다. 순서: 서버 → Quest → 안경 → 폰.

## 1. 서버 (Ubuntu 22.04 + NVIDIA GPU, docker·python3·rsync)
1. 공급사에게 `license.json` 을 받는다(크레인 대수·만료 — 없으면 평가판 1대). 공급사 발급: `python3 Server/xrcrane_license.py issue ...`(개인키는 공급사 맥에만).
2. 이 PC에서: `cd server && Server/install.sh <서버 ssh> xrcrane-db --ai xrcrane-ai --image python-3.12-slim.tar.gz --license <license.json>`
   - 통합서버(:5006)·가상 PLC·AI 추론(xrcrane-ai)이 docker compose 로 뜬다. 끝에 `[install] 완료`·`AI 추론 서비스 기동` 이 나오면 성공.
   - 실 PLC 가 있으면 compose 의 xrcrane-plc 대신 `Server/plc_s7.py` adapter.
3. 크레인 앱: `rsync -rlt app/ <서버>:AI-XR-Crane/` (Windows 빌드 — 서버에선 Proton 으로 실행), `remote_receiver.py` 도 같은 폴더에.
4. 스트리밍(헤드리스 NVIDIA Xorg :1 · WiVRn 26.9 flatpak · Sunshine 자리 1~4 · keepalive)은 공급사 기술자가 구축한다 — 절차는 공급사 문서 「빌드·인프라 가이드」 §3·§4.
5. 보안: 통합서버는 허용 대역(XRCRANE_ALLOW, 기본 사설망)만 받는다. **납품 때 토큰 켜기** — 같은 망 누구나 /command 로 사고 지시를 낼 수 있어서: 서버 `~/xrcrane-db/.env` 의 `XRCRANE_TOKEN` 과 단말 `xrcrane_token.txt`(크레인 앱 persistentDataPath)에 같은 값. 개발·시연 중엔 끔. AI(로컬 모델)는 토큰과 무관.

## 2. Quest (VR 운전석) — quest/
- WiVRn 클라이언트 26.9 를 설치(서버와 같은 버전, APK 는 공급사가 함께 전달) → 수동 추가: 주소 = 서버 IP, 포트 9757.
- 연결되면 서버가 크레인 VR 앱을 띄운다(접속 없이 5분이면 꺼짐).

## 3. 안경 (XREAL One Pro + Beam Pro) — glasses/
1. Nebula 의 Air Casting 끄기(끈 뒤 안경 재연결), 화면 밀도 기본·자동 회전 끔.
2. `adb install CraneRemote.apk`(리모컨) → 권한: `appops set com.seoyeonsoft.craneremote SYSTEM_ALERT_WINDOW allow`, `GET_USAGE_STATS allow`, `pm grant com.seoyeonsoft.craneremote android.permission.WRITE_SECURE_SETTINGS`.
3. Moonlight(Android) 설치 → [+] 에 `<서버 IP>:<47989+1000(k-1)>`(자리 k) → 서버에서 `~/sunshine-pair.sh <PIN> <47990+1000(k-1)> <이름>`. 안경 하나 = 자리 하나.
4. 리모컨 앱이 보여 주는 기기 ID(`BEAM:...`)를 서버 등록부(users.json)에 넣는다.

## 4. 폰 (모바일 관전) — phone/
- Moonlight(Android/iOS) → 안경과 같은 방법으로 자기 자리 하나만 추가·페어링. 조작은 화면 탭(선택)·길게 누르기(이동).
EOF
for d in quest glasses phone; do sed -n "/^## .*— $d\//,/^## /p" "$K/README.md" | sed '$d' > "$K/$d/README.md"; done
du -sh "$K"; echo "[kit] $K"
