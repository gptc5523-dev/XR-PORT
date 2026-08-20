# 컨테이너선 3D 모델링 파이프라인

## 이 프로젝트가 하는 일

Blender(bpy)로 컨테이너선을 **절차적으로** 생성한다. 손으로 모델링하지 않는다.
모든 형상은 `spec/ship_spec.json` 하나에서 파생되며, 스펙을 바꾸면 배가 바뀐다.

## 절대 규칙 (위반 시 작업 중단)

1. **GUI 조작을 전제하지 않는다.** 모든 작업은 `blender --background --python <script>` 로 재현 가능해야 한다.
2. **스펙이 유일한 진실이다.** 스크립트 안에 치수 하드코딩 금지. 반드시 `ship_spec.json`에서 읽는다.
3. **게이트를 우회하지 않는다.** `python gates/gate_check.py` 실패 시 다음 단계로 넘어가지 않는다.
   게이트를 통과시키려고 게이트 코드를 수정하는 것은 금지. 모델을 고친다.
4. **작업 에이전트는 자기 결과를 승인할 수 없다.** 판정은 `render-critic` 과 게이트만 한다.
5. **`bpy.ops` 는 최소화한다.** 컨텍스트 의존적이라 헤드리스에서 깨진다. `bpy.data` + `bmesh` 우선.
6. **모든 오브젝트는 scale (1,1,1)** 로 적용(apply)된 상태여야 한다.
7. **외부 에셋/텍스처/참조 이미지를 가져오면 `ASSET_LEDGER.md` 에 출처와 라이선스를 기록한다.** 기록 없는 에셋은 씬에 들어갈 수 없다.
8. **실존 선사의 로고·도장·상표를 재현하지 않는다.** 가공 선사명을 사용한다.

## 상위 규약 (이 문서보다 우선)

- **조형**: 메모리 `reference_design_laws.md` (본칙 제0~14 + 부칙 A~K) 가 모든 3D 조형·배치의 유일 SSOT.
- **간섭·겹침 판정**: 메모리 `reference_interference_verdict.md` 의 게이트 [0]~[7] 을 따른다.
  `gates/gate_check.py` 의 간섭 검사도 이 규약으로 짠다 (판정용 불리언은 MANIFOLD 솔버 고정).
- 스펙(치수)의 진실은 `ship_spec.json`, 조형·판정 방법의 진실은 위 두 규약이다. 서로 침범하지 않는다.

## 단위 규약

- 1 Blender unit = 1 meter
- Up = +Z, 선수(bow) = -Y 방향, 우현(starboard) = +X
- 원점(0,0,0) = 선체 중앙(midship) × 중심선(centerline) × 기선(baseline, 선저)
- 흘수선은 `z = spec.principal.T_design`

⚠ **구판 좌표계와 다르다.** 2026-08-20 이전 작업(백업: `~/Backups/선박_재건조전_20260820/`)은
선수 = +Y, z = 0 이 설계 흘수선이었다. 구판 수치를 참조할 때는 반드시 변환한다.

## 네이밍 규약

| 접두 | 대상 |
|---|---|
| `hull_` | 선체 외판, 벌버스바우, 스케그, 트랜섬 |
| `deck_` | 갑판, 해치코밍, 브레이크워터 |
| `dh_` | 거주구/선교(deckhouse) |
| `fn_` | 퍼널, 배기구 |
| `lash_` | 라싱브리지 |
| `ctr_` | 컨테이너 (인스턴싱 소스 + 컬렉션) |
| `ref_` | 워터라인 등 참조용 (렌더 제외) |

컬렉션도 동일 접두로 묶는다. LOD는 접미 `_LOD0` / `_LOD1` / `_LOD2`.

## 디렉토리 (루트 = `~/Container/선박/`)

```
spec/ship_spec.json      스펙 (유일한 진실)
spec/ship_spec.schema.json
scripts/                 각 에이전트가 만드는 bpy 스크립트
gates/gate_check.py      결정론적 검증
build/                   .blend, 렌더, 리포트 산출물
ASSET_LEDGER.md          에셋 출처 원장
```

## 표준 작업 순서

```
naval-architect → concept-designer
        ↓
hull-modeler → structure-modeler → cargo-placer → topology-material
        ↓
gate_check.py (실패 시 되돌아감)
        ↓
render-critic (4뷰 렌더 + 시각 판정, 실패 시 되돌아감)
        ↓
export-ledger → 사람 승인
```

**hull-modeler 는 실물 조선 순서를 따른다 (오너 확정 2026-08-20):**
**선도(lines, 커브) → 철골(cage) → 페어링 → 표면 → 메시는 맨 마지막.**
메시 이전 단계는 커브·NURBS 로만 작업하고, 각 단계도 게이트로 수치 검증한다.

값싼 검사를 비싼 검사 앞에 둔다. 게이트를 통과하지 못한 모델은 렌더 크리틱에 보내지 않는다.

## 기존 자산 (재사용 가능)

- **프로펠러 완성품** — `~/_Blender/3D Object/_scripts/ship_propeller.py` +
  `~/_Blender/3D Object/ObjectIng/Propeller.blend`. 6섬 폐합 · 부호부피 전부 양수 ·
  G1~G11 0 FAIL 로 마감된 유일한 보존 부재.
  - 원점 = 샤프트 축선(로컬 좌표). **정점을 선박 전역 좌표로 굽지 말 것** — float32
    양자화 여유가 177배 → 6배로 줄어 없던 비매니폴드가 생긴다. 배치는 오브젝트
    트랜스폼으로만 한다.
  - 스펙의 프로펠러 직경·축심이 구판(D 9.51 m, 축심 z −8.065 구좌표)과 달라지면
    재생성이 필요하다.
- **실물 레퍼런스** — `~/Container/문서/레퍼런스/선박/컨테이너선_실물_레퍼런스.md`,
  KCS 오프셋 `오프셋/kcs_offsets.json`. 스펙 작성 시 근거로 인용하고 출처를 남긴다.

## 실행 명령

```bash
blender --background --python scripts/build_all.py
python gates/gate_check.py            # 게이트
blender --background --python scripts/render_views.py   # 4뷰 렌더
```

## 커밋 규칙

한 커밋 = 한 에이전트의 한 단계. 게이트 통과 전에는 커밋하지 않는다.
