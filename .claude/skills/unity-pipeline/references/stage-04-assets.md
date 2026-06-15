# ④ assets — 에셋 작업 목록 확정 (상세)

목적: 변경·신규 에셋을 범주별로 리스트화하고 **실제 파일·경로명을 확정**한다(이후 계약이 됨).

## 입력
- `03_scope.md` scope 매트릭스(G2 승인본) · `_conventions.md` 네이밍 규약.

## 활동 체크포인트
1. scope 의 신규/변경 항목을 구체 에셋으로 분해한다(1 goal → 여러 에셋 가능).
2. **실제 경로·파일명을 확정**한다. `_conventions.md` §1 네이밍·§2 범주를 따른다.
3. 에셋 간 **의존**(생성 선후)을 표기한다 → ⑦ plan task 순서의 근거.
4. 각 에셋을 goal 에 매핑(역으로 모든 P0 goal 이 에셋을 갖는지 점검).
5. **경로는 클릭 링크로** 적는다: `` [`Assets/...`](../../../Assets/...) `` (`_conventions.md` §9).
   신규 파일은 생성 후 링크가 살아난다(깨진 링크 = 아직 미생성 표시로 유용).
6. **신규 Script/Asset 은 용어 사전에도 등록**(`glossary/script/`·`glossary/asset/`) —
   클래스가 무엇인지 쉬운 설명과 함께. 스펙 파일명은 `05_spec/<A-ID>_<에셋명>.md` 로 예약.

## 산출물 — assets 매트릭스 + 변경·활용 맵 Mermaid
컬럼: `A-ID | 경로 | 범주 | 신규/변경/삭제 | 연결 goal | 의존`

**변경·활용 맵 Mermaid 필수**(_conventions §13): `flowchart` + classDef 로 **추가(초록)/수정(골드)/삭제(빨강)/
유지·활용(회색)** 을 색 구분하고 의존을 화살표로. 노드 라벨에 A-ID 포함(`A1[A1 …]`). 손대지 않지만
참조하는 기존 에셋도 keep 으로 그려 "이번 사이클이 무엇을 건드리고 무엇을 쓰는지"를 한눈에 보인다.

**Hierarchy 배치 표 필수**(_conventions §15): 각 에셋이 씬 계층 어디에 들어가나 — 배치 유형(씬배치/DDOL
승격/프리팹/**런타임 자동생성**)과 경로. 자동생성이면 "씬 배치 없음 — 런타임 자동생성"으로 명시한다.

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
