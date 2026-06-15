# ⑦ plan — 체크리스트·진행관리 (상세)

목적: 전 과정 task 를 세분화한 **살아있는 체크리스트**를 만든다. 구현 루프의 작업 큐.

## 입력
- `04_assets.md`(의존 순서) · `05_spec/`(구현 단위) · `06_test_env.md`(검증 환경).

## 활동 체크포인트
1. 에셋·spec 을 **구현 가능한 최소 task** 로 분해한다.
2. ④의 **의존**을 반영해 순서를 정한다(선행 에셋 먼저).
3. 각 task 에 **연결 A-ID** 와 **검증 방법**(compile / play / console)을 한 줄에 포함한다.
4. 체크박스 규약을 지킨다: 미완료 `- [ ]`, 완료 `- [x]`.
5. 템플릿의 **"마감 정리" task 2건은 삭제 금지** — ① 용어 사전 갱신, ② 검증 증빙 수집.
   이 두 칸이 미체크면 Stop 훅이 사이클 종료를 막는다(사전 누적·증빙을 체크리스트로 강제).

## 산출물 — `07_plan.md`
형식 예시:
```
## 구현 체크리스트
- [ ] A1 InventorySystem.cs 작성 — 검증: compile
- [ ] A1 InventorySystem 단위 동작 — 검증: play+console
- [ ] A2 InventoryPanel.prefab 구성 — 검증: reserialize(훅)+console
- [ ] A3 PlayerController 인벤토리 참조 추가 — 검증: compile+play
```

## 진행관리 (훅 연계)
- **Stop 훅**(`require-checklist.sh`)이 `- [ ]` 잔여 개수로 사이클 미완료를 판정 → 미완료면 종료 차단.
- 구현 루프에서 task 통과 시 즉시 `- [x]` 로 갱신한다(§7 SKILL).
- 색인: 사이클 폴더명이 `_index.md` 에 등록돼 있어야 Stop 훅을 통과한다.

## 흔한 실수
- task 가 너무 커서 "통과/실패" 판정이 모호 → 검증 가능한 단위로 분할.
- 완료했는데 `- [x]` 체크 누락 → Stop 훅이 계속 차단.
