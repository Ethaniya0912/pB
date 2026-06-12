# ④ assets — 에셋 작업 목록 확정 (상세)

목적: 변경·신규 에셋을 범주별로 리스트화하고 **실제 파일·경로명을 확정**한다(이후 계약이 됨).

## 입력
- `03_scope.md` scope 매트릭스(G2 승인본) · `_conventions.md` 네이밍 규약.

## 활동 체크포인트
1. scope 의 신규/변경 항목을 구체 에셋으로 분해한다(1 goal → 여러 에셋 가능).
2. **실제 경로·파일명을 확정**한다. `_conventions.md` §1 네이밍·§2 범주를 따른다.
3. 에셋 간 **의존**(생성 선후)을 표기한다 → ⑦ plan task 순서의 근거.
4. 각 에셋을 goal 에 매핑(역으로 모든 P0 goal 이 에셋을 갖는지 점검).

## 산출물 — assets 매트릭스
컬럼: `A-ID | 경로 | 범주 | 신규/변경 | 연결 goal | 의존`

예시:
| A-ID | 경로 | 범주 | 신규/변경 | 연결 goal | 의존 |
|---|---|---|---|---|---|
| A1 | Assets/Scripts/Inventory/InventorySystem.cs | Script | new | G1 | — |
| A2 | Assets/Prefabs/UI/InventoryPanel.prefab | Prefab | new | G1 | A1 |
| A3 | Assets/Scripts/Player/PlayerController.cs | Script | modify | — | A1 |

## G3 게이트 (Stop 강제)
- 실제 생성 전에 **네이밍·경로를 확정**받는다. 확정 후 변경은 비용이 큼.
- 양식·기록: `references/gates.md`. 통과 후 `awaiting_gate` 해제.

## 흔한 실수
- 경로를 "대충" 정하고 ⑤/⑥에서 바꿈 → spec/plan 과 불일치.
- Assets/ 밖(.harness) 산출물과 Assets/ 내 실제 에셋 경로를 혼동.
