# Reports 레지스트리 (`Reports/_index.md`)

> **이 폴더는?** 게임 측정·검증의 **증거 문서 보관소**입니다. "어떤 수정이 효과 있었나"를 숫자로 입증하는
> 절차서·베이스라인·Step 증거 문서와, 에디터 도구가 자동 생성하는 보고서가 여기 모입니다.
> 하네스 사이클 산출물(분석·명세·결정)은 [`.harness/cycles/`](../.harness/_index.md), 사이클별 검증 증빙은
> `cycles/<id>/evidence/` — Reports 는 **사이클을 가로지르는 도메인 증거**를 담는다는 점이 다릅니다.

## 폴더 규약 (2026-06-12 체계화)
| 폴더 | 무엇을 담나 | 누가 쓰나 | 네이밍 |
|---|---|---|---|
| [`netcode/`](netcode/) | 코옵 네트워크 측정 체계 — 절차서·베이스라인·Step 증거·계획 원본 | 사람+Claude (사이클·수동 측정) | `StepN_*.md`, `SCN_*.md` |
| [`auto/`](auto/) | **에디터 도구가 자동 생성**하는 보고서 | 코드 (Week2 도구 등 — 경로 하드코딩) | 도구가 타임스탬프 포함해 생성 |
| [`guides/`](guides/) | 워크플로 가이드 문서 | 사람 | 자유 |
| [`archive/`](archive/) | 종료된 기간/도메인의 보고서 묶음 | 사람 (정리 시) | `YYYY-MM_<주제>/` |

- **새 도메인**(예: 세이브 시스템 측정)이 생기면 `Reports/<도메인>/` 폴더를 만들고 여기 표에 등록한다.
- 측정 결과 파일명은 계획서 규약 `SCN-XX_PROF-X_StepN_before|after` 를 따른다([[SCN-시나리오]] 참조).
- 런타임 계측 CSV(events/verdicts/checksum)는 여기가 아니라 `persistentDataPath/NetDiagnostics/<세션>/`
  에 쌓인다 — 위치·diff 방법은 [`Tools/diff_verdicts.ps1`](../Tools/diff_verdicts.ps1) 주석 참조.
  분석에 쓴 CSV 는 해당 Step 증거 문서 옆(`netcode/`)으로 복사해 보존한다.

## 현재 등록 문서
| 경로 | 무엇 | 상태 |
|---|---|---|
| [`netcode/SCN_Procedures.md`](netcode/SCN_Procedures.md) | 표준 시나리오 SCN-01~07 절차서 | 활성 (살아있는 문서) |
| [`netcode/Step0_Baseline.md`](netcode/Step0_Baseline.md) | Step 0 베이스라인 (M1 기입, 잔여=2인 측정 대기) | 활성 |
| [`netcode/Step1_Evidence.md`](netcode/Step1_Evidence.md) | Step 1 전송 안정화 증거 (코드 완료, 측정 대기) | 활성 |
| [`netcode/코옵_Netcode_실행계획_v1.1.md`](netcode/코옵_Netcode_실행계획_v1.1.md) | 실행계획 원본 (사이클 입력의 원전) | 참조 |
| [`netcode/코옵_Netcode_Step1-5_진행계획_검증_체크리스트.md`](netcode/코옵_Netcode_Step1-5_진행계획_검증_체크리스트.md) | Step 1~5 진행·검증 체크리스트 | 참조 |
| [`guides/Git_Worktree_Unity_가이드.html`](guides/Git_Worktree_Unity_가이드.html) | Git worktree + Unity 가이드 | 참조 |
| `archive/2026-04_week2/` (5건) | Week2 자동 보고서·진척률 (구형) | 보관 |

## 이동 이력
- 2026-06-12: 평면 11파일 → `netcode/ auto/ guides/ archive/` 체계화. 도구 출력 경로를 `Reports/auto/` 로
  코드 수정(Week2ProgressTracker·HumanoidVisualStageSetup·MobRegressionRunner·ChecklistPatcher).
  `.harness` 링크 6건 갱신. 계획서 원문의 `Reports\SCN_Procedures.md` 표기는 인용이므로 그대로 두며,
  실제 위치 매핑은 이 문서가 책임진다.

---
## 🔗 관련 문서 (Foam)
- 하네스: [[_index|.harness 사이클 레지스트리]] · [[_conventions|하네스 규약]] (§12 Reports)
- 용어: [[SCN-시나리오]] · [[베이스라인]] · [[M-지표]] → [[_glossary|용어 사전]]
