# ⑥ test_env — 테스트 환경 정의 (상세)

목적: 구현을 검증할 **테스트 환경**(씬·프리팹·오브젝트)을 as-is/to-be 로 기술하고 적용한다.

## 입력
- `05_spec/` 명세 · 최신 `snapshots/` · 쿡북 §4 씬셋업.

## 활동 체크포인트
1. 검증에 필요한 **씬/프리팹/오브젝트**를 식별한다(예: 테스트용 씬 + 리그 오브젝트).
2. 각 대상의 **as-is**(현재)와 **to-be**(목표)를 적는다 — to-be 는 unity-cli 로 재현 가능한 수준.
3. **적용 방법**을 정한다:
   - 우선 **Edit/Write 로 텍스트 편집** → PostToolUse 훅이 `reserialize → refresh --compile → console` 자동.
   - 절차적 생성이 쉬우면 `unity-cli exec` 스니펫(쿡북 §4). 파괴적이면 G4 가드가 ask.
4. 적용 후 console 에러 0 을 확인한다(훅이 stderr 로 피드백).

## 산출물 — test_env 매트릭스 + 적용 스니펫
컬럼: `대상 | as-is | to-be | 적용 방법`

예시:
| 대상 | as-is | to-be | 적용 방법 |
|---|---|---|---|
| Test.unity | 없음 | 인벤토리 리그 씬 | Write 새 씬 → 훅 reserialize |
| InventoryRig | 없음 | InventorySystem 부착 GO | exec AddComponent |

## G5 게이트 (Stop 강제)
- **as-is→to-be 적용 전** 승인받는다(실제 씬/프리팹이 바뀜).
- 양식·기록: `references/gates.md`. 통과 후 `meta.json.status="implementing"`.

## 정합화 책임 (중요)
- `.prefab/.unity/.asset/.mat` 텍스트 편집의 reserialize·compile·console 은 **훅이 자동 수행**한다.
  너는 reserialize 를 수동 호출하지 않는다. 훅 에러 피드백에만 반응한다.

## 흔한 실수
- to-be 가 추상적이라 재현 불가 → 컴포넌트·필드 수준까지 구체화.
- 테스트 씬을 기존 씬에 섞어 오염 → 전용 테스트 씬/리그 사용.
