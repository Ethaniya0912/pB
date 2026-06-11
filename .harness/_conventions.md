# 하네스 규약 (`_conventions.md`)

> 네이밍·에셋 범주·CLI 규칙의 단일 진실원. 스킬·훅·템플릿이 이 문서를 참조한다.

## 1. 디렉토리·파일 네이밍
- 하네스 산출물은 **`Assets/` 밖** `.harness/` 에만 둔다(Unity `.meta` 오염 방지).
- 1 사이클 = 1 보고/기능. 폴더명 `YYYY-MM-DD_<slug>` (slug: 소문자·하이픈).
- 단계 파일은 `01_target.md` … `08_result.md` 넘버링으로 정렬을 보장한다.
- 스펙은 `05_spec/` 하위에 에셋 단위 파일로 분리한다.

## 2. 에셋 범주 (assets 매트릭스 Category 열의 표준값)
- `Script` (.cs) · `Prefab` (.prefab) · `Scene` (.unity) · `ScriptableObject`/`Data` (.asset)
- `Material` (.mat) · `Shader` · `Texture`/`Sprite` · `Audio` · `Animation`/`Animator` · `UI`/`UXML`/`USS`
- `Config`/`Settings` · `Other`(자유 기술)

## 3. unity-cli 규칙
- 멀티 인스턴스 혼선 방지: 가능하면 `unity-cli --project <ProjectRoot> ...` 로 대상을 고정한다.
- 상태 게이팅: 변경성 명령 전 `unity-cli --json status` 로 `ready` 확인(busy 면 unity-cli 가 대기).
- 안전장치: `.prefab/.unity/.asset/.mat` 텍스트 편집 후 반드시 `reserialize`(PostToolUse 훅이 자동 수행).
- 읽기 전용 질의는 `--json exec` 로, 반복 셋업은 `[UnityCliTool]` 커스텀 툴로 승격한다.
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

## 6. meta.json 상태값
`started` → `planning` → `implementing` → `signed_off`(=완료) / `archived`.
게이트 대기 시 `awaiting_gate: "G2"` 식으로 표기 → Stop 훅이 decisions.md 기록을 요구한다.
