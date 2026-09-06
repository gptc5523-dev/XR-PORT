# 자산 출처 · 라이선스 대장 (Third-Party Asset Credits)

> **작업 환경 — [Unity 작업]** · 대상 리포 `~/Container` (Unity 프로젝트 루트)
> **갱신 2026-08-14** — 전수 조사 후 재작성. `.blend` 무관(Blender 작업 아님).
> 이 파일이 **"이 프로젝트에 외부 저작물이 무엇이 들어 있는가"의 단일 출처(SSOT)** 다.
> 외부 에셋을 새로 가져오면 **가져온 그 커밋에서** 아래 표에 행을 추가한다.

---

## 0. 결론 (한 줄 요약)

| 질문 | 답 |
|---|---|
| 컨테이너 FBX 4종을 상업적으로 써도 되는가 | **된다.** 전량 자체 제작 저작물이며 제3자 권리 없음 |
| 현재 크레딧 표기 **의무**가 있는 자산이 있는가 | **없다.** 셰이더·모델·텍스처 모두 CC0 또는 자체 제작 |
| 배포물에 넣으면 **안 되는** 파일이 있는가 | **이제 없다.** 유일 대상이던 외부 규격·카탈로그 PDF·사진을 **2026-08-14 전량 삭제**했다 (§4) |

---

## 1. 자체 제작 자산 — 제3자 권리 없음

전부 사내에서 Blender 스크립트로 절차 생성했고, 원본 `.blend` 와 생성 스크립트가 프로젝트에 남아 있다.
외부 마켓(Sketchfab · TurboSquid · CGTrader 등) 구매·다운로드 이력 **없음**.

### 1.1 컨테이너 모델 (`Assets/Container/Models/`)

| 파일 | md5 | 크기 | 원본 `.blend` |
|---|---|---|---|
| `Container_20ft.fbx` | `06806bf508be1df85f38f0b79277344e` | 6.6 MB | `~/New Final/Container_20ft.blend` |
| `Container_40ft.fbx` | `57f07e5d3f169b0782c8526a687cfe74` | 4.7 MB | `~/New Final/Container_40ft.blend` |
| `Container_40ftHC.fbx` | `24b627d50b89fe775f6ebb8fbf7a0b21` | 4.2 MB | `~/New Final/Container_40ftHC.blend` |
| `Container_45ftHC.fbx` | `ad8f956a4ce4227d5798c01548a82c86` | 4.9 MB | `~/New Final/Container_45ftHC.blend` |

- **제작자 메타데이터** — 4종 모두 FBX 헤더 Creator 가 `Blender (stable FBX IO) - 5.1.2 - 5.15.0` (Blender Foundation).
  구매 모델이면 상용 DCC(Autodesk Maya/3ds Max) 또는 재배포 도구 흔적이 남는데, 그런 흔적이 **0건**이다.
- **형상 근거** — ISO 668 / 1496-1 / 1161 및 CIMC·Bullbox 사양서의 **치수를 읽고 자체 모델링**했다.
  치수·규격 수치는 저작물이 아니므로, 이 경로로 만든 결과물에 규격 발행처의 권리가 미치지 않는다. 다만 §4 참조.
- **텍스처 25종** (`Assets/Container/Textures/`) — `*_BaseColor` · `*_MetalSmooth` · `*_Normal` 전량 자체 베이크 산출물.

### 1.2 크레인 · 선박 · 부두

| 자산 | 경로 | 제작 방식 |
|---|---|---|
| RTG 크레인 | `Assets/Crane/Models/RTG_Crane.fbx` (md5 `c81e0d8e2f1841422d19863f8b8bb98d`) | Blender 자체 제작 → FBX |
| STS 크레인 | `Assets/Crane/Scripts/Sts/Editor/StsCraneCreator.cs` | **에디터 절차 생성** (메시 원본이 코드) |
| 컨테이너선 | `Assets/Ship/Scripts/Procedural*.cs` | 에디터 절차 생성 |
| 부두 · 야드 | `Assets/Crane/Scripts/Sts/Editor/StsQuayGroundCreator.cs` | 에디터 절차 생성 |
| 절차 컨테이너 | `Assets/Container/Scripts/ProceduralContainer*.cs` | 런타임/에디터 절차 생성 |

---

## 2. 외부 자산 — 현재 프로젝트에 포함된 것 전부

| # | 자산 | 경로 | 라이선스 | 상업 이용 | 크레딧 의무 |
|---|---|---|---|---|---|
| 1 | **ambientCG PBR 텍스처 10 세트** | `Assets/PBR_Library/` | **CC0 1.0** (퍼블릭 도메인) | 가능 | **없음** |
| 2 | **Unity 공식 패키지** (URP · XRI · XR Hands · OpenXR · Netcode 등) | `Packages/manifest.json` | Unity Companion License / Unity EULA | 가능 (Unity 로 만든 제품 한정) | 없음 |
| 3 | **Unity 패키지 샘플 에셋** (XRI Starter Assets · Hands Interaction Demo · XR Hands HandVisualizer) | `Assets/Samples/` | 상동 (패키지에 종속) | 가능 | 없음 |
| 4 | **TextMesh Pro 기본 폰트** LiberationSans | `Assets/TextMesh Pro/Fonts/` | SIL OFL 1.1 | 가능 | 없음 (폰트 파일 재배포 시 라이선스 동봉) |
| 5 | **나눔고딕** (한글 HUD) | `Assets/Crane/Resources/Fonts/NanumGothic-Regular.ttf` | SIL OFL 1.1 | 가능 | 없음 (동일 조건) |

### 2.1 ambientCG 상세 — 유일하게 "빌드에 나가는" 외부 저작물

- **세트 10종** — `Asphalt012` · `Concrete034` · `CorrugatedSteel002` · `Metal032` · `Metal055A` ·
  `MetalPlates006` · `MetalWalkway010` · `PaintedMetal001` · `Rubber004` · `Rust004`
- **사용처** — `StsCraneCreator.cs:3922~3980` 의 디테일 페인팅(도장 구조강 = `PaintedMetal001`, 기계실 외벽 = `CorrugatedSteel002` 등).
  라이브러리가 없으면 절차 강철로 폴백하도록 되어 있어, 최악의 경우 **제거해도 빌드는 깨지지 않는다**.
- **라이선스 원문 확인 (2026-08-14)** — ambientCG 에셋 페이지:
  > "All assets are released under the Creative Commons CC0 license, making them free to use without attribution — even in commercial circumstances."
- 즉 **표기 의무 없음**. 아래 문구는 의무가 아니라 예의 차원의 선택 사항이다.
  > Textures from ambientCG.com, licensed under CC0 1.0.

### 2.2 폰트 취급 주의

OFL 1.1 은 **상업적 사용·임베딩을 허용**하지만 두 가지 제약이 있다.

1. 폰트 파일을 **단독 판매**할 수 없다 (게임에 임베딩하는 것은 무관).
2. 폰트를 **원본 파일 형태로 재배포**하면 OFL 사본을 동봉해야 한다.
   TMP 로 SDF 아틀라스만 굽고 `.ttf` 를 빌드에서 제외하면 이 조항 자체가 걸리지 않는다.

---

## 3. 크레딧 화면 체크리스트

현재 **의무 항목이 0건**이므로 인게임 크레딧 화면은 필수가 아니다.
아래는 외부 에셋이 추가되었을 때 되살릴 절차다.

- [ ] §2 표에 **크레딧 의무 "있음"** 행이 생겼는가 → 생겼다면 아래를 모두 수행
- [ ] 인게임 About / Credits 화면에 요구 문구 삽입
- [ ] 배포 README · 프레스킷에 동일 문구 삽입
- [ ] 원저작물을 **수정**했다면 그 사실을 이 파일에 명기 (CC-BY 계열 필수 요건)

---

## 4. 외부 자료 — ★ **2026-08-14 전량 삭제 완료** (우리 자산 아님)

이 절의 대상은 **전부 남의 저작물**이었다. **우리 자산 중 반출 금지인 것은 하나도 없다**(§1 은 전량 자유).
**오너 지시로 리포·백업 전량에서 삭제했다. 되돌릴 수 없다.**

| 삭제된 파일 | 발행처 | 삭제 위치 |
|---|---|---|
| `CIMC_ISO_DRY_CONTAINER_REPAIRING_SPARE_PARTS_CATALOG_2018.pdf` (8.5 MB · 154쪽) | CIMC | 리포 + 백업 2곳 |
| `ISO_1161_2016_preview.pdf` (0.4 MB) | ISO | 리포 + 백업 2곳 |
| `IACS_Rec45_Container_Corner_Fittings_1996.pdf` (0.1 MB) | IACS | 리포 + 백업 2곳 |
| `Bullbox_TEC-SPEC-20STANDARD_Oct17_rev4.pdf` (0.6 MB) | Bullbox | 리포 + 백업 2곳 |
| `컨테이너_참고사진/` 8장 (캠·키퍼 6 · 힌지 2) | 촬영자 각각 | 리포 + 백업 2곳 |
| `트럭_참고사진/` 8장 | 촬영자 각각 | 백업 2곳 (리포엔 2026-07-23 에 이미 없었음) |

- **git 이력 없음** — `문서/` 는 `.gitignore:62` 에 등재돼 있어 **한 번도 커밋된 적이 없다.** 이력에서 지울 것도 없다.
- **채택한 수치는 남는다** — `문서/컨테이너_통합.md` **Part 2** 에 추출·검증된 형태로 보존. 잃은 것은 **재검증 능력**이다.
- ★ **이후 규칙** — Part 2 에 없는 수치는 **출처가 없는 것**이다. 2차 출처로 확정하지 말고 **원문을 다시 확보**한다.
  **특수 컨테이너(Part 7) 착수 시 1차 출처 재확보가 선행 조건**이다.
- ★ **판정 기준은 "베이크했는가"가 아니라 "외부 픽셀이 최종 산출물에 들어갔는가"다.**
  **베이크는 세탁이 아니다** — 외부 사진을 물려 새 PNG 로 구워도 그 결과물은 그 사진의 2차적저작물이다.
  직접 할당·베이크·프로젝션 페인트·포토그래메트리·사진에서 노멀 생성 **전부 동일**하게 취급한다.
  반대로 **우리 메시의 AO·노멀·커비처 베이크는 자유**다(나온 픽셀이 우리 것).
- ★ 단 **"외부 이미지 사용" 자체가 금지는 아니다 — 라이선스가 정한다.**
  `Assets/PBR_Library/` 의 ambientCG 10세트는 외부 이미지를 그대로 적용하고 있으나 **CC0 라 자유**다(§2.1).
  판정은 **두 축** — ①외부 픽셀이 들어가는가(베이크 무관) ②들어간다면 라이선스가 허용하는가. **새 이미지는 ②를 확인**한다.
- ★ 외부 자료를 다시 들이면 **`Assets/` 밖(`문서/`)에 두고 이 표에 행을 추가**한다. `Assets/` 로 옮기면 빌드에 포함된다.

### 4.1 오해 방지 — 금지 대상은 "지식"이 아니라 "파일"이다

2026-08-14 실제로 나온 질문이다. 위 목록을 **"CSC 명판 내용이 금지"** 로 읽을 수 있어 명시해 둔다.

| 항목 | 판정 | 근거 |
|---|---|---|
| **CSC 안전승인 명판** (`CSC_Plate_BaseColor.png`) | **자유** | 내용 전부 자체 작성 — 제조사 `SEOYEON SOFT CO., LTD. / BUSAN`, 소유자 코드 `SYSU 260715 [5]`, 제조번호 `SYS-22G1-260715`. 남의 도면 스캔이 아니라 **규격 서식에 자사 데이터를 채워 자체 베이크**했다 |
| 명판의 ISO 수치 (30,480 kg · 22G1 · 150 kN 등) | **자유** | 규격 **수치는 저작물이 아니다.** 저작물은 표현(도면·문장)이지 사실 데이터가 아니다 |
| PDF 치수를 읽고 만든 FBX | **자유** | §1 참조 |

### 4.2 별도 축 — 상표 (저작권 문제가 아니다)

**"직접 만들었다"가 방어가 되지 않는 유일한 항목이다.**

| 항목 | 위험 | 내용 |
|---|---|---|
| 실존 선사 소유자 코드 — `ContainerIdGenerator.cs:14-28` | 중 | `MAEU`(Maersk) · `MSCU`(MSC) · `HLXU`(Hapag-Lloyd) · `EGHU`(Evergreen) · `TCLU`(Triton) 등 **BIC 등록 실제 기업 식별자**. `ContainerColorPalette.cs:23-28` 의 선사 컬러와 겹치면 야드 박스가 **특정 실존 선사 컨테이너로 읽힌다** |
| 명판 승인번호의 `BV` (`KR / BV / 2026 / 0715`) | 낮음 | Bureau Veritas(실존 선급)가 하지 않은 승인 표기. 게임 에셋에선 통상 넘어가는 수준 |

- **사내 시뮬레이션·훈련용**이면 현행 유지 가능. **상용 배포·홍보물**이면 가상 코드 교체 권장.
- 교체는 `OwnerCodes` 배열과 팔레트 **이름만** 바꾸면 된다. 체크디지트 로직은 그대로 동작하고 **색상 값은 유지해도 된다 — 색은 상표가 아니다.**
- 현재 **명판(`SYSU`)과 생성기(`MAEU` 등)가 어긋나 있다.** 가상 코드로 통일하면 이 불일치도 함께 해소된다.
- **미결 — 오너 결정 대기.** 결정되면 이 절에 근거와 함께 기록한다.

### 4.3 출고 전 점검판

한눈에 보는 요약판 → **`문서/html/라이선스_한눈에.html`**

---

## 5. 제거 이력 — 과거에 있었으나 지금은 없는 외부 자산

| 자산 | 출처 · 저작자 | 라이선스 | 처리 |
|---|---|---|---|
| Harbor Crane (STS 안벽 크레인) | Sketchfab / MisterH ([@TGVMisterH](https://sketchfab.com/TGVMisterH)) | CC-BY-4.0 | **커밋 `671d8a0` 에서 전량 삭제** (`Assets/Crane/Imported/` 통째). 자체 제작 STS 절차 생성으로 대체 |

이 자산은 **크레인**이었고 컨테이너와 무관하다. 삭제로 CC-BY 표기 의무도 함께 소멸했다.

---

## 6. 새 외부 에셋을 들일 때 (도입 전 필수)

1. **라이선스 원문을 받은 그 자리에서 확인**한다 — 마켓 요약 배지가 아니라 라이선스 페이지 본문.
   CC-BY / CC-BY-SA / NonCommercial / NoDerivatives 는 각각 조건이 전혀 다르다.
2. **NonCommercial(NC) 은 도입 금지.** 이 프로젝트는 상업 목적이다.
3. **CC-BY-SA 도 사실상 금지** — 파생물에 동일 라이선스 전파 요구가 붙는다.
4. 도입이 확정되면 **같은 커밋에서** §2 표에 행을 추가하고, 라이선스 원문 파일을 에셋 폴더에 함께 둔다.
5. 원본을 수정했다면 **무엇을 어떻게 바꿨는지** 한 줄로 남긴다.

---

## 7. 조사 방법 (재검증용)

이 대장을 다시 검증할 때 쓸 명령이다.

```bash
cd ~/Container
# 1) 모델 제작자 메타데이터 — 자체 제작이면 Blender 만 나온다
for f in $(find Assets -name "*.fbx" ! -path "*/Samples/*"); do
  echo "=== $f"; head -c 2000 "$f" | strings | grep -iE "blender|autodesk|maya|sketchfab" | head -3
done

# 2) 외부 마켓 흔적 · 라이선스 파일 잔존 여부
grep -rniE "sketchfab|turbosquid|cgtrader|free3d|CC-BY|NonCommercial" Assets --include="*.md" --include="*.txt"
find Assets -iname "*licen*" -o -iname "*credit*"

# 3) 모델·폰트·오디오 전수 — Samples 밖에 정체 불명 파일이 있는지
find Assets -type f \( -iname "*.fbx" -o -iname "*.gltf" -o -iname "*.glb" -o -iname "*.obj" \
  -o -iname "*.blend" -o -iname "*.ttf" -o -iname "*.otf" -o -iname "*.wav" \) ! -name "*.meta"
```

**2026-08-14 실행 결과** — 위 3개 모두 통과. `Assets/Samples/`(Unity 공식) 외의 외부 유래 파일은
`Assets/PBR_Library/`(CC0) 와 나눔고딕(OFL) **둘뿐**이며, 컨테이너·크레인·선박·부두 모델은 전량 자체 제작이다.
