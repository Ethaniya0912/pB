# 하네스 규약 (`_conventions.md`)

> 네이밍·에셋 범주·CLI 규칙·문서 연결(Foam)·용어 사전·증빙의 단일 진실원.
> 스킬·훅·템플릿이 이 문서를 참조한다.

## 1. 디렉토리·파일 네이밍
- 하네스 산출물은 **`Assets/` 밖** `.harness/` 에만 둔다(Unity `.meta` 오염 방지).
- 1 사이클 = 1 보고/기능. 폴더명 `YYYY-MM-DD_<slug>` (slug: 소문자·하이픈).
- 단계 파일은 `01_target.md` … `08_result.md` `09_next.md` 넘버링으로 정렬을 보장한다.
- 스펙은 `05_spec/` 하위에 **`<A-ID>_<에셋명>.md`** 로 분리한다(예: `A1_RnsmHud.md`).
  A-ID 접두로 정렬을 보장하고, 파일명이 용어 사전(`glossary/`)과 충돌하지 않게 한다.
- 검증 증빙(콘솔 덤프·스크린샷·CSV 등)은 사이클 폴더의 **`evidence/`** 에 둔다(§10).
- 용어 사전은 사이클 밖 **`.harness/glossary/<분류>/<용어>.md`** 에 누적한다(§8).
- **Assets 코드: MonoBehaviour/ScriptableObject 는 파일명=클래스명 단독 파일** — 어기면 MonoScript
  미바인딩으로 도메인 리로드 1회에 missing-script(husk)가 된다(netcode 사이클 회귀 실증, 23:05 수정).

## 2. 에셋 범주 (assets 매트릭스 Category 열의 표준값)
- `Script` (.cs) · `Prefab` (.prefab) · `Scene` (.unity) · `ScriptableObject`/`Data` (.asset)
- `Material` (.mat) · `Shader` · `Texture`/`Sprite` · `Audio` · `Animation`/`Animator` · `UI`/`UXML`/`USS`
- `Config`/`Settings` · `Other`(자유 기술)

## 3. unity-cli 규칙
- 멀티 인스턴스 혼선 방지: 가능하면 `unity-cli --project <ProjectRoot> ...` 로 대상을 고정한다.
- 상태 게이팅: 변경성 명령 전 `unity-cli status` 로 `ready` 확인(busy 면 unity-cli 가 대기).
- 안전장치: Unity YAML 에셋(`.prefab/.unity/.asset/.mat/.anim/.controller/.physicMaterial` 등) 텍스트
  편집 후 반드시 `reserialize`(PostToolUse 훅이 자동 수행). 코드 계열(`.cs/.asmdef/.shader` 등)은 컴파일만.
- 타임아웃: 훅 내 unity-cli 호출은 `ucli` 래퍼(기본 50s, `HARNESS_UCLI_TIMEOUT` 으로 조정)를 쓴다.
- 읽기 전용 질의는 `exec` 로(복잡한 코드는 stdin 파이프), 반복 셋업은 `[UnityCliTool]` 커스텀 툴로 승격한다.
- v0.3.x 문법: `--json`/`--port` 없음. 콘솔 필터는 `console --type error`. 전역 `--project`/`--timeout <ms>`.
- 권장 설정: Unity Preferences → Interaction Mode = `No Throttling`.

## 4. 체크박스 규약 (07_plan.md)
- 미완료 task: `- [ ] ...`  / 완료 task: `- [x] ...`
- Stop 훅이 `- [ ]` 잔여 개수로 사이클 미완료를 판정한다.

## 5. 게이트 ↔ 훅 매핑 (상세는 skills/unity-pipeline/references/gates.md)
| 게이트 | 시점 | 강제 훅 |
|---|---|---|
| G1 기획 해석 확인 | ① 직후 | Stop |
| G2 범위·영향도 승인 | ③ 직후 | Stop |
| G3 파일·경로명 확정 | ④ 직후 | Stop |
| G4 파괴적 CLI 차단 | 파괴적 명령 직전 | PreToolUse |
| G5 테스트 환경 적용 승인 | ⑥ 직후 | Stop |
| G6 설계 분기 질문 | 테스트 실패 시 | 수동 |
| G7 최종 사인오프 | ⑧ 직전 | Stop |
| G8 다음 사이클 진행 결정 | ⑨ 직후 | 수동(질의) |

## 6. meta.json 상태값
`started` → `planning` → `implementing` → `signed_off`(=완료) / `archived`.
게이트 대기 시 `awaiting_gate: "G2"` 식으로 표기 → Stop 훅이 decisions.md 기록을 요구한다.

## 7. 문서 연결 규약 (VS Code Foam 지식 그래프)
모든 하네스 md 는 Foam 익스텐션(`foam.foam-vscode`)으로 서로 연결한다.
목적: 어떤 문서에서든 클릭 몇 번으로 관련 사이클·명세·용어·실제 에셋에 도달할 수 있게 한다.

- **md ↔ md 연결은 위키링크** `[[...]]` 를 쓴다. 실제 에셋/폴더 연결은 위키링크가 아니라
  **상대경로 마크다운 링크**를 쓴다(아래 §9 — Foam 은 md 노트만 그래프 노드로 취급).
- **링크 형태** (파일명 중복 시 경로 접두로 한정하는 Foam 규칙):
  | 대상 | 형식 | 예 |
  |---|---|---|
  | 같은 사이클의 단계 문서 | `[[<cycle-id>/0N_name\|표시명]]` | `[[2026-06-12_netcode/03_scope\|③ scope]]` |
  | 스펙 문서 | `[[<cycle-id>/05_spec/<A-ID>_<에셋명>\|에셋명]]` | `[[2026-06-12_netcode/05_spec/A1_RnsmHud\|RnsmHud 명세]]` |
  | 용어 사전 항목 | `[[<용어>]]` (사전 파일명은 전역 유일 — §8) | `[[NetEventLogger]]`, `[[RNSM]]` |
  | 특정 섹션(본문 위치) | `[[파일#헤딩 텍스트\|표시명]]` | `[[2026-06-12_netcode/08_result#요약\|결과 요약]]` |
- **모든 단계 문서는 문서 끝에 `## 🔗 관련 문서 (Foam)` 블록**을 둔다:
  이전/다음 단계 · decisions · 관련 스펙 · **이 문서에 등장한 용어 사전 링크**.
- 본문에서는 용어 **첫 등장 위치에만** 위키링크를 건다(표 안 반복 링크 금지 — 가독성 우선).
- 역링크는 Foam 백링크 패널이 자동 표시하지만, **사전 → 사이클 방향은 명시적으로** 적는다(§8).

### 7-A. 매트릭스 ID 앵커·점프 링크 (T-ID·G-ID·A-ID·게이트)
매트릭스의 식별자(T1·G1·A1·게이트 G1~G8)는 다른 단계 문서가 빈번히 가리킨다. **참조되는 ID는
점프 가능한 앵커로 만들고, 가리키는 쪽은 그 앵커로 링크한다** — "이 goal이 어느 target에서 왔지?"를
클릭 한 번에 추적하게 한다.

- **앵커 심기**: 매트릭스 ID 셀 맨 앞에 HTML 앵커를 단다 — `| <a id="T1"></a>T1 | … |`.
  (Foam 위키링크는 노트(파일) 단위라 표 안 특정 행 점프엔 부적합 → 문서 내 행 점프는 HTML 앵커가 정확.)
- **같은 문서 내 점프**: `[T1](#T1)`.
- **다른 단계 문서로 점프**: 상대경로 + 앵커 `[T1](01_target.md#T1)` (같은 사이클 폴더 기준).
  예: `02_goal.md` 의 "연결 target" 열에서 `[T1](01_target.md#T1)`, `03_scope.md` 의 "연결 goal" 열에서
  `[G1](02_goal.md#G1)`.
- **원문근거 링크**: target 매트릭스 "원문근거" 열은 입력 문서로 링크한다 —
  `[§0.A.1](00_input/<문서>.md)` (md 면 헤딩 앵커까지, 비-md 면 파일 링크 + 위치 텍스트).
- **게이트**: `decisions.md` 의 각 게이트 헤더에 앵커(`## <a id="G2"></a>G2 (…) — 승인`)를 달면
  다른 문서에서 `[G2 결정](decisions.md#G2)` 로 점프. 매트릭스가 게이트를 언급하면 이 앵커로 연결.
- 전수 앵커는 권장이되 **최소 의무는 "실제로 참조되는 ID"**. 미참조 ID는 생략 가능.

## 8. 용어 사전 (glossary) — 누적 지식 베이스
새 용어가 산출물에 등장하면 사전에 등록하고, 사이클이 끝나도 재사용되도록 누적한다.

- **위치·분류**: `.harness/glossary/<분류>/<용어>.md` — 분류는 5종 고정:
  | 분류 폴더 | 무엇을 담나 | 예 |
  |---|---|---|
  | `concept/` | 개념·지표·시나리오·프로토콜·빌드 심볼 | RNSM, RTT, M-지표, soak-테스트 |
  | `tool/` | 에디터 도구·외부 도구·CLI·프리셋 | PROF-프리셋, unity-cli, Clumsy |
  | `script/` | 프로젝트 C# 클래스·컴포넌트 | NetEventLogger, VerdictLogger |
  | `asset/` | 씬·프리팹·ScriptableObject 등 비코드 에셋 | (씬/프리팹 도입 시) |
  | `package/` | 외부 패키지·라이브러리 | Multiplayer-Tools, NGO |
- **파일명 = 용어 그대로**(전역 유일). 슬래시 등 금지 문자는 하이픈으로(`PROF-G/A/B` → `PROF-프리셋`).
- **항목 형식**: `glossary/_template.md` — frontmatter `type`(분류)·`aliases`, 한 줄 정의,
  **쉬운 설명(비개발자용 비유·풀이)**, 등장 사이클(섹션 앵커 위키링크로 본문 연결), 관련 용어,
  (script/asset 이면) 실제 파일 상대경로 링크.
- **등록 시점**: 각 단계 산출물을 쓰면서 새 용어가 나오면 **그 단계에서 바로** 등록/갱신한다.
  최소 보장: ⑧ result 작성 전 `07_plan.md` 의 "용어 사전 갱신" task 를 체크해야 Stop 훅을 통과한다.
- **양방향 링크 의무**: 사이클 문서 → `[[용어]]` (첫 등장), 사전 항목 → "등장 사이클" 섹션에
  `[[<cycle-id>/0N_name#섹션|...]]` 역링크. 인덱스 `glossary/_glossary.md` 에 한 줄 추가.

## 9. 실제 에셋·산출물 링크 규약 (클릭해서 바로 열기)
md 에서 실제 파일·폴더를 언급할 때는 **클릭으로 열리는 상대경로 링크**를 건다.

- 형식: `` [`Assets/Scripts/...cs`](<상대경로>) `` — 표시는 코드체 경로, href 는 문서 위치 기준 상대경로.
- 상대경로 깊이 기준표:
  | 문서 위치 | 프로젝트 루트까지 | 예 (`Assets/Scripts/Foo.cs`) |
  |---|---|---|
  | `cycles/<id>/0N_*.md` | `../../../` | `[Foo.cs](../../../Assets/Scripts/Foo.cs)` |
  | `cycles/<id>/05_spec/*.md` | `../../../../` | `[Foo.cs](../../../../Assets/Scripts/Foo.cs)` |
  | `glossary/<분류>/*.md` | `../../../` | `[Foo.cs](../../../Assets/Scripts/Foo.cs)` |
- 적용 대상: `04_assets.md` 경로 컬럼, `08_result.md` diff·산출물, spec 메타의 경로,
  사전 항목의 "실제 위치". 폴더는 끝에 `/` 를 붙여 링크한다.
- Unity YAML 에셋(`.unity/.prefab/.asset/.mat`)은 VS Code 에서 yaml 로 열린다(읽기 확인용).

## 10. 검증 증빙 (evidence) 규약 — result 의 근거
"검증 완료"는 **언제·어떤 환경에서·무엇으로 확인했는지**가 첨부돼야 성립한다.

- **보관**: 원본 증빙 파일은 `cycles/<id>/evidence/` — 콘솔 덤프(`.txt`), 스크린샷(`.png`),
  측정 CSV, scoped 스냅샷 등. md 에는 발췌+링크(파일 비대화 방지).
- **08_result.md 필수 증빙 섹션** (템플릿 `08_result.md` 준수):
  1. **검증 환경·시각** — 검증 일시(시작~종료), `unity-cli status` 출력(Unity·Connector 버전), 검증 방법 목록.
  2. **task별 검증 기록** — task(A-ID)·시각·방법(compile/play/console/수동)·판정·근거 파일 링크.
  3. **콘솔 로그 발췌** — 판정 근거가 된 핵심 출력을 ` ```log ` 블록으로, 전문은 evidence/ 링크.
  4. **스크린샷·산출물** — 화면 증빙 `![..](evidence/..png)` 임베드 + 결과물 실파일 링크(§9).
- **수집 명령**: 쿡북 §8 (콘솔 덤프 저장·플레이 스크린샷·시각 기록). 스크린샷은 플레이 중
  `ScreenCapture.CaptureScreenshot` (경로는 프로젝트 루트 기준 → `.harness/cycles/<id>/evidence/`).
- 수동(2인·실기기) 검증은 "근거: 수동 — 절차서 링크 + 수행자/일시" 로 기록한다.

## 11. 가독성 규약 — 비개발자도 읽는 문서
산출물은 개발자 외 기획·아트 등 **프로젝트 구성원 전체의 보고 문서**다.

- 모든 단계 문서 도입부에 **"이 문서는?"** 블록을 두고 본문을 시작한다(§14-A — 강화 양식).
- 어려운 용어는 **첫 등장에서 괄호 풀이 또는 `[[용어]]` 사전 링크** 중 하나 이상.
  사전 항목의 "쉬운 설명"은 비유·일상어로(예: RTT = "탁구공이 갔다 돌아오는 시간").
- 표는 핵심만, 흐름·이유는 표 밖 평문으로 설명한다. 약어를 정의 없이 쓰지 않는다.
- **매트릭스(정밀) + Mermaid(직관)를 함께** 둔다 — 표로 데이터를, 다이어그램으로 관계·흐름을 보인다(§13).
- ⑤ spec 은 기술 명세 앞에 **"쉬운 설명" 섹션**(이 에셋이 뭐고 왜 필요한지)을 둔다.

## 12. Reports/ 규약 — 측정·증거 문서 보관소
`Reports/` 는 **사이클을 가로지르는 도메인 증거**(절차서·베이스라인·Step 증거·도구 자동 보고서)의 집이다.
사이클 분석 산출물(.harness/cycles)·사이클 증빙(evidence/)과 구분된다. 단일 진실원: [`Reports/_index.md`](../Reports/_index.md).
- 구조: `Reports/<도메인>/`(예: netcode) · `Reports/auto/`(도구 자동 출력 — 코드가 쓴다) ·
  `Reports/guides/` · `Reports/archive/YYYY-MM_<주제>/`.
- 새 도메인·새 문서는 `Reports/_index.md` 등록 표에 한 줄 추가한다(레지스트리).
- 도구(에디터 스크립트)가 보고서를 쓸 때는 반드시 `Reports/auto/` 하위로 — 루트 직치 금지.
- 측정 결과 파일명: `SCN-XX_PROF-X_StepN_before|after` (계획서 규약).

## 13. Mermaid 시각화 규약 — 표는 데이터, 그림은 관계
모든 단계 문서는 매트릭스(정밀 데이터)와 함께 **Mermaid 다이어그램**으로 관계·흐름을 시각화한다.
Foam 은 `bierner.markdown-mermaid`(추천 확장)로 프리뷰에서 렌더링한다. 코드펜스는 ` ```mermaid `.

### 13-A. 단계별 권장 다이어그램
| 위치 | 다이어그램 타입 | 무엇을 그리나 |
|---|---|---|
| 파이프라인 전체(SKILL·README) | `flowchart` | 9단계 + 게이트 흐름 |
| ① target | `mindmap` | 기획 내용 분류 트리(카테고리→target) |
| ② goal | `flowchart LR` | T-ID → G-ID 매핑(다대다) |
| ③ scope | `flowchart` + subgraph(as-is/to-be) | **변경 범위 시각화** — 접근/수정 대상을 상태색으로 |
| ④ assets | `flowchart` + classDef | **추가/수정/삭제/유지 시각화** + 의존(화살표) |
| ⑤ spec | `classDiagram` 또는 `flowchart` | 클래스·책임·의존/공개 API 관계 |
| ⑥ test_env | `flowchart LR` | as-is → to-be 환경 변화 |
| ⑦ plan | `flowchart TD` | task 의존 순서(작업 큐) |
| ⑧ result | `flowchart` 또는 `mindmap` | 달성 흐름 / 다측면 이점 |
| ⑨ next | `flowchart LR` | 이관 항목 → 후속 사이클 |

### 13-B. 의미색 표준 (classDef — 어느 문서나 동일)
```
classDef add  fill:#E5F4EC,stroke:#1E8A5B,color:#14532d;   %% 추가 new
classDef mod  fill:#FBF0DD,stroke:#B5731A,color:#7c4a03;   %% 수정 modify
classDef del  fill:#FBEAEA,stroke:#C0392B,color:#7c1d1d;   %% 삭제 delete
classDef keep fill:#ECEFF3,stroke:#5C6675,color:#374151;   %% 유지 기구현 keep
classDef flow fill:#EBF0FF,stroke:#2A52DB,color:#1e3a8a;   %% 흐름·신규개념 flow
```
- 노드 라벨에 **매트릭스 ID 를 포함**(예: `A1[A1 RnsmHud]`)해 표와 1:1 대응시킨다.
- 다이어그램은 표의 **요약·관계화**이지 표 대체가 아니다 — 정밀 데이터는 매트릭스가 권위.
- 노드가 20개를 넘으면 핵심만 추리거나 subgraph 로 그룹화한다(가독성 우선).
- 색은 위 5색만 — 일관된 시각 언어를 유지(추가=초록·수정=골드·삭제=빨강·유지=회색·흐름=파랑).
- 정합 검사: `bash .harness/hooks/check-mermaid.sh` (펜스 짝·subgraph/end 균형·`:::class` 정의 여부·헤더 유효).
  ⑦ plan 마감 task 로 강제. Foam 프리뷰 확장은 `bierner.markdown-mermaid`(추천 확장).

## 14. ⑨ next(이관)·이점 정리 규약
- **⑨ next(`09_next.md`)**: 이번 사이클에서 다음으로 넘길 것(미완 task·후속 제안·발견된 신규 작업·
  측정 대기)을 **다음 사이클의 입력 형태**로 구조화한다. result 의 "잔여 이슈"를 실행 가능한 후보로 승격.
  - 매트릭스: `차기후보 | 유형(이월/신규/측정) | 출처(이번 사이클 ID·앵커) | 우선순위 | 비고`.
  - Mermaid(`flowchart LR`)로 "이번 사이클 산출 → 차기 사이클 후보" 이관 흐름을 그린다.
  - **문서 끝에서 "바로 다음 사이클을 진행할까요?"를 사용자에게 질의**(G8 next 게이트). 이관 없으면 "이관 없음"
    명시 후 사이클 종결. yes 면 `09_next.md` 의 우선 후보를 입력으로 `/cycle-start` 를 안내·실행한다.
- **이점 정리(⑧ result 내 "다측면 이점" 섹션)**: 이번 사이클이 가져온 가치를 측면별로 적는다 —
  **게임(플레이어 체감)·개발(생산성·안정성)·기획(검증·의사결정)·아트(작업 기반)·운영(측정·이력)**.
  해당 없는 측면은 "해당 없음"으로 남겨 누락과 구분한다.

## 14-A. "이 문서는?" 강화 양식 (모든 단계 문서 도입부)
도입부 인용블록(`>`)에 **이 문서가 왜·무엇을·어떻게·언제·누가**의 관점으로 구체적 목적을 적고 본문을 시작한다.
형식(1~3줄로 압축, 항목명을 굳이 다 쓰기보다 자연스러운 문장에 녹인다):
- **무엇** — 이 문서가 담는 산출물 한 줄.
- **왜** — 파이프라인에서 이 단계가 필요한 이유(앞 단계와의 연결).
- **어떻게** — 작성 방법(매트릭스+Mermaid, 어떤 입력으로).
- **언제·누가** — 시점(어느 게이트 전후)과 주체(Claude 작성 / 사용자 결정).
예: "이 문서는? ④에서 확정한 에셋을 **어떻게 만들지** 적은 설계도(무엇)입니다. 구현(⑦)이 이걸 직접 근거로
삼기에(왜) 코드를 읽어 변경점 중심으로 쓰며(어떻게), G3 확정 직후 Claude 가 작성하고 사람은 명세 정합만 봅니다(언제·누가)."

## 15. Hierarchy 배치 규약 — 오브젝트가 씬 계층 어디에 들어가나 (④ assets · ⑥ test_env)
에셋·오브젝트는 **"Hierarchy 어디에 어떻게 들어가는지"** 를 반드시 명시한다. 특히 **런타임 자동생성이면
"씬 배치 불필요 — 자동생성"임을 분명히 알려** 사람이 헛되이 씬을 뒤지지 않게 한다.

### 15-A. 이 프로젝트의 배치 유형 (현재 Hierarchy 구조 기준)
씬 계층은 **레이어(`━━━━ X Layer ━━━━`, 이중선) → 카테고리 그룹(`─── X ───`, 단선) → 매니저** 로 조직된다.
| 유형 | 표기 | 어떻게 들어가나 |
|---|---|---|
| ① 레이어 컨테이너 | `━━━━ Cyber/Physics/UI/Helper Layer ━━━━` | 씬 루트 직접 배치(그룹 구조화용, 씬 종속) |
| ② 카테고리 그룹 자식 | `━━━━ X Layer ━━━━/─── Bridge System ───/<매니저>` | 단선 그룹 아래 배치(에디터 구조화용) |
| ③ DDOL root 승격 | `DontDestroyOnLoadHelper` 부착 | Awake 에 `SetParent(null)`+DontDestroyOnLoad → 씬 전환 유지(매니저 다수) |
| ④ 단독 루트(NGO) | `World Network Manager` | NetworkManager 상속은 DDOLHelper **미사용**(충돌) — 루트 직접 |
| ⑤ **런타임 자동생성** | `[NetDiagnostics]` 등 | **씬·프리팹에 없음.** `RuntimeInitializeOnLoadMethod` 가 `new GameObject`+AddComponent. → "자동생성"이라 명시 |
| ⑥ UI/프리팹 | `━━━━ UI Layer ━━━━/…Canvas`, 프리팹 인스턴스 | UI 레이어 배치 또는 프리팹에서 인스턴스화 |

### 15-B. 명시 방법
- **④ assets**: 각 에셋 행/요지에 **배치 유형 + 경로**를 적는다. 예:
  - 수동 배치: "`━━━━ Physics Layer ━━━━/─── Terrain ───` 아래 GameObject, DDOLHelper 부착(③)".
  - 자동생성: "**씬 배치 없음 — ⑤ 런타임 자동생성**(NetDiagnosticsBootstrap 이 `[NetDiagnostics]` 생성·AddComponent)".
- **⑥ test_env**: to-be 의 Hierarchy 배치를 **트리(```text 코드블록) 또는 mermaid** 로 그린다(현재 구조에 맞춰 부모 경로 포함). 자동생성은 "런타임 생성(플레이 시)"로 표기하고 에디트 모드 씬엔 없음을 명시.
- 배치가 게임 로직에 영향 주면(예: 실행 순서 `[DefaultExecutionOrder]`) 함께 적는다.

## 16. 산출물 사용 가이드 규약 — 만든 걸 어떻게 쓰나 (⑤ spec · ⑧ result)
산출물(스크립트·에셋)은 **만든 뒤 어떻게 Unity 에 적용해 쓰는지**를 문서가 안내해야 한다. 코드를 모르는
팀원도 "이건 언제·왜 생겼고, 어떻게 켜고, 뭘 주의하나"를 알 수 있게 한다.

### 16-A. ⑤ spec 의 "산출물 사용 가이드" 섹션 (기술 명세 뒤)
각 spec 에 아래 4항목을 적는다(쉬운 말로):
1. **언제·왜 만들어졌나** — 어느 사이클·어느 goal/이슈에서, 무슨 문제를 풀려고.
2. **Unity 적용법** — 어떻게 프로젝트에 들어가나(§15 배치 유형 참조: 자동생성/씬 배치/프리팹). 추가 설정(패키지·심볼·키)이 있으면.
3. **사용법** — 실제로 어떻게 쓰나(예: "플레이 → 화면 우상단 HUD 확인", "F8 로 프로파일 전환", "결과 CSV 는 `persistentDataPath/…`").
4. **주의점** — 함정·전제·하지 말 것(예: "릴리즈는 `NETDIAG_DISABLED` 로 끔", "2인 측정 필요", "파일명=클래스명 유지").

### 16-B. ⑧ result 의 "산출물 인수인계" 요약 표
이번 사이클 전체 산출물을 한눈에 — `산출물 | 무엇 | 적용(자동/수동) | 사용법 한 줄 | 주의`.
재사용 가능한 도구·스크립트는 **용어 사전(`glossary/script/`)에도 "사용법" 한 줄**을 남겨 다음 사이클이 참조한다.
