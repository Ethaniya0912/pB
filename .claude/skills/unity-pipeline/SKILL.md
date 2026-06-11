---
name: unity-pipeline
description: >
  Unity 프로젝트에서 기획/구현/기술 보고 문서를 입력받아 시스템 엔지니어링 관점으로
  분석하고(target→goal→scope→assets→spec→test_env→plan→result), unity-cli로 씬·프리팹·
  스크립트를 셋팅·테스트하는 8단계 파이프라인. 기획서 분석, 개발 계획 수립, 영향도 분석,
  에셋 작업 목록 작성, 기술명세 작성, 테스트 환경 구축, Unity 개발 자동화, 구현 루프가
  언급되거나 다단계 개발 작업이 시작되면 명시적 요청이 없어도 이 스킬을 사용할 것.
---

# unity-pipeline — Unity 개발 자동화 파이프라인

기획 문서를 입력으로 **8단계 SE 분석·명세·테스트 셋업**을 절차적으로 수행하고,
`unity-cli`로 구현 루프를 돌린다. 산출물은 `.harness/cycles/<id>/` 에 누적된다.

## 1. Overview — 무엇을·왜
한 기능/보고 단위(=1 사이클)를 `target→goal→scope→assets→spec→test_env→plan→result`
순서로 분석·셋업하고, 구현 루프로 검증한다. 핵심 원칙은 **결정(Skill)과 강제(Hook)의 분리**:
- 이 스킬은 "무엇을·어떻게"를 안내한다(절차·매트릭스·CLI 템플릿).
- **훅이 게이트를 강제**한다 — 정합화·검증·종료 차단은 LLM 판단과 무관하게 자동으로 일어난다.
  너는 게이트에서 "멈추고 질문"하는 책임만 진다.

## 2. When to use / not
- **사용:** 기획서로 개발 계획 수립, 영향도 분석, 에셋 작업 목록·기술명세 작성,
  테스트 환경 구축, 다중 task 구현 루프 등 **사이클 단위 작업**.
- **비사용:** "이 함수 한 줄 고쳐줘" 같은 단발·1-step 질의. 이때는 그냥 직접 처리한다.

## 3. Preconditions (사이클 시작 전 확인)
1. **사이클 폴더 존재** — `/cycle-start <문서>` 로 스캐폴딩됐는가? 없으면 사용자에게 `/cycle-start`
   실행을 안내한다(이 스킬은 폴더를 직접 만들지 않는다 → 부작용은 cycle-start 전용).
2. **Unity ready** — `unity-cli --json status` 가 `ready` 인지 확인. busy 면 unity-cli 가 대기하므로
   그대로 진행 가능. 무응답이면 "Editor offline"으로 보고 후, 분석 단계(①~⑤)는 계속하되 실행
   단계(⑥ 이후)는 사용자에게 Editor 기동을 요청한다.
3. **컨텍스트 로드** — `.harness/_conventions.md`, 최신 `snapshots/`, 이전 사이클 `04_assets.md` 를
   필요 시 참조한다(③ scope 에서 핵심).

## 4. 8단계 절차
각 단계는 **입력 → 활동 → 산출물 → 게이트 → CLI**. 상세 지침은 `references/stage-0N-*.md`,
매트릭스 컬럼은 `references/matrix-schemas.md`, CLI 스니펫은 `references/cli-cookbook.md`.
산출물은 `assets/templates/` 의 빈 템플릿을 복사해 채운다.

> 진행 규약: 각 단계 완료 시 해당 `0N_*.md` 를 작성하고, `meta.json` 의 `status`/`awaiting_gate`
> 를 갱신한다. 게이트가 걸린 단계는 **멈추고 사용자 확인**을 받은 뒤 다음 단계로 간다.

### ① target — 기획 내용 정리  ▸ `01_target.md`  ▸ **G1**
- 입력: `00_input/` 의 기획·구현·기술개론 문서.
- 활동: 순수 기획 내용을 **빈틈 없이 리스트화·분류**한다(해석/추론과 원문을 구분).
  as-is 스냅샷(`snapshots/`)을 확보한다.
- 산출물: target 매트릭스(ID·항목·분류·원문근거·해석·모호도).
- **G1 게이트**: 모호·누락 항목과 SE 해석을 사용자에게 확인받는다. → 상세 `references/stage-01-target.md`

### ② goal — 개발 요구사항 도출  ▸ `02_goal.md`
- 활동: 각 target → 개발 요구사항으로 변환·**매핑**. 요구사항별 필요 시스템·에셋 범주 분류.
- 산출물: goal 매트릭스(Goal-ID·연결 target·요구사항·시스템·에셋범주·우선순위).

### ③ scope — 변경 범위·영향도  ▸ `03_scope.md`  ▸ **G2**
- 활동: 각 요구사항이 **기구현/신규**인지 판정하고 영향도를 분석한다. `unity-cli exec` 로
  타입·에셋 존재를 확인하고, **이전 사이클 `04_assets.md` 와 최신 `snapshots/` 를 조회**해 중복을 피한다.
- 위임: 존재성 스캔은 **`asset-auditor` 서브에이전트**(read-only)에 맡길 수 있다(§8).
- 산출물: scope 매트릭스(요구사항·상태[기구현/신규/변경]·대상에셋·영향범위·리스크).
- CLI: `unity-cli --json exec "System.Type.GetType(\"Game.Inventory\")!=null"` (쿡북 §존재확인)
- **G2 게이트**: 기존 에셋 수정·영향도 높은 항목을 승인받는다.

### ④ assets — 에셋 작업 목록 확정  ▸ `04_assets.md`  ▸ **G3**
- 활동: 변경·신규 에셋을 범주별로 리스트화하고 **실제 파일·경로명을 확정**해 goal 과 매핑.
- 산출물: assets 매트릭스(Asset-ID·경로·범주·신규/변경·연결 goal·의존).
- **G3 게이트**: 실제 생성 전에 네이밍·경로를 확정받는다(이후 파일명이 계약이 됨).

### ⑤ spec — 기술 명세서  ▸ `05_spec/<asset>.md`
- 활동: 각 에셋의 기술명세 작성. 스크립트는 **클래스·책임·인터페이스·의존성·공개 API**,
  그 외(prefab/scene/asset/mat)는 **사양·포맷·제약·참조 관계**.
- 산출물: `05_spec/` 하위 에셋 단위 파일. 템플릿 `assets/templates/05_spec_asset.md`.

### ⑥ test_env — 테스트 환경 정의  ▸ `06_test_env.md`  ▸ **G5**
- 활동: 프로젝트 구조·오브젝트·프리팹·씬을 **as-is / to-be** 로, unity-cli 가 실행 가능한
  수준으로 기술한다. 텍스트 편집 후 `reserialize` 로 정합화(**PostToolUse 훅이 자동 수행**).
- CLI: 씬/프리팹 셋업 스니펫은 쿡북 §씬셋업. 편집은 Edit/Write 로 → 훅이 reserialize+compile.
- **G5 게이트**: as-is→to-be 실제 적용 전에 승인받는다.

### ⑦ plan — 체크리스트·진행관리  ▸ `07_plan.md`
- 활동: 전 과정 task 를 세분화한 **체크리스트**(`- [ ]`)를 만든다. 구현 루프의 작업 큐가 된다.
- 규약: 미완료 `- [ ]` / 완료 `- [x]`. **Stop 훅이 미체크 개수로 사이클 미완료를 판정**한다.

### ⑧ result — 결과 보고서  ▸ `08_result.md`  ▸ **G7**
- 활동: ①~⑦ 완료 시 결과 종합 + as-is→to-be 변화를 **스냅샷 diff** 로 첨부.
- **G7 게이트**: 보고서·diff 검토 후 사이클 종료. `meta.json.status="signed_off"`, `_index.md` 에 완료 기록.

## 5. 게이트 규약 (R2 · 휴먼-인-더-루프)
- **행동 규칙:** 게이트(G1·G2·G3·G5·G7)에 도달하면 **자동 진행을 멈추고** 결정 요약과
  선택지를 제시한 뒤 사용자 응답을 기다린다. 모호하거나 비복구 변경이면 임의 진행 금지.
- **기록:** 모든 게이트 결정은 `decisions.md` 에 `## G2 (2026-06-11) — 승인 ...` 형식으로 남긴다.
  awaiting_gate 가 걸린 채 종료하면 **Stop 훅이 decisions.md 기록을 요구**한다.
- **G4(파괴적 CLI)** 는 PreToolUse 훅이 자동으로 ask/deny → 사용자가 권한 프롬프트로 응답.
- **G6(설계 분기)** 는 테스트 실패가 기계적 수정이 아닐 때 선택지를 제시(수동).
- 게이트별 질문 양식·기록 형식은 `references/gates.md`.

## 6. 산출물·누적 규약 (R4)
- 경로: `.harness/cycles/<YYYY-MM-DD_slug>/`, 단계 파일 `01~08` 넘버링(상세 `_conventions.md`).
- 갱신: 각 단계가 `0N_*.md` 작성 → `meta.json` 상태 갱신 → 완료 시 `_index.md` append.
- **재사용(누적의 핵심):** ③ scope 에서 이전 사이클 `04_assets.md` 와 `snapshots/` 를 조회해
  "이미 구현됨"을 판단 → 중복 분석 방지·영향도 정확도 향상.
- 보관: 완료 사이클은 `archive/` 로, `_index.md` 에 포인터 유지.

## 7. 구현 루프 (⑦ 이후, task 단위 반복)
각 task 에 대해:
1. **편집** — Edit/Write 로 스크립트·에셋 텍스트 수정.
2. **정합화·컴파일** — `.prefab/.unity/.asset/.mat` 는 **PostToolUse 훅이 자동으로**
   `reserialize → editor refresh --compile → console --filter error` 수행. `.cs` 는 compile 만.
   → **너는 reserialize 를 수동 호출하지 않는다**(훅 책임). 훅이 stderr 로 에러를 돌려주면 수정한다.
3. **콘솔 확인** — 컴파일 에러 없으면 다음으로. 있으면 수정 후 재편집(루프 재진입).
4. **플레이 테스트** — `unity-cli editor play --wait` 로 진입 → 동작 확인 → `console` 재확인 → 판정.
5. **판정** — 통과 시 `07_plan.md` 의 해당 task 를 `- [x]` 로 체크 → 다음 task.
   실패 시 수정 후 루프 재진입. 설계 결정이 필요한 실패는 **G6** 로 사용자에게 질문.
- 루프 위임: `test-runner` 서브에이전트에 play/console 판정을 맡길 수 있다(§8).
- 모든 task 완료 → ⑧ result.

## 8. 서브에이전트 연계
- **`asset-auditor`** (③ scope): 타입·에셋 존재를 read-only 로 스캔하고 이전 사이클과 대조해
  "기구현/신규" scope 매트릭스 초안을 반환. 코드 수정 금지. → `Task(asset-auditor, ...)`.
- **`test-runner`** (구현 루프): play--wait + console 판정을 격리 컨텍스트에서 수행하고
  통과/실패와 에러 요약을 반환. → `Task(test-runner, ...)`.
- 컨텍스트 격리가 이득일 때(대량 존재성 스캔, 반복 플레이 판정)만 위임한다.

## 9. References 인덱스 (필요 시 로드)
| 파일 | 언제 읽나 |
|---|---|
| `references/stage-01-target.md` … `stage-08-result.md` | 해당 단계 수행 시 |
| `references/cli-cookbook.md` | 실행 단계에서 unity-cli 명령이 필요할 때 |
| `references/matrix-schemas.md` | 각 단계 매트릭스 컬럼을 작성할 때 |
| `references/gates.md` | 게이트 질문·decisions.md 기록 형식이 필요할 때 |
| `assets/templates/*` | 사이클 산출물 빈 템플릿이 필요할 때 |
| `.harness/_conventions.md` | 네이밍·에셋 범주·CLI 규칙 |
