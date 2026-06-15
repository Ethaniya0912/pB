# 하네스 사이클 레지스트리 (`_index.md`)

> 단일 진실원(append-only). 모든 사이클의 시작·게이트·완료 이벤트를 한 줄씩 누적한다.
> Stop 훅(`require-checklist.sh`)이 사이클 폴더명이 여기 등록됐는지 검사한다.

## 사용법
- 새 사이클: `/cycle-start <문서>` 가 한 행을 추가한다.
- 상태 갱신: 게이트 통과·완료 시 행을 추가(기존 행 수정 금지, append-only).
- 보관: 완료 사이클은 `archive/` 로 이동하되 여기 포인터는 유지한다.
- 용어 사전: [[_glossary|glossary/_glossary.md]] — 사이클 산출물에 등장한 용어의 누적 사전(분류·쉬운 설명·등장 사이클 역링크).

## 레지스트리

| 일시 | 사이클 | 상태 | 비고 |
|---|---|---|---|
<!-- 예: | 2026-06-11 14:02 | 2026-06-11_inventory | started | 인벤토리 시스템 -->
| 2026-06-12 17:59 | 2026-06-12_netcode | started | netcode |
| 2026-06-12 19:20 | 2026-06-12_netcode | signed_off | Step 0 계측 잔여 갭 마감+검증 (RnsmHud·NetSimProfiles 신규, P2-4 채널화, M1=0 확정; M2~M11 2인 수동 인계) |
| 2026-06-12 21:50 | 2026-06-12_netcode | retrofit | 하네스 개선 소급 적용 — 용어 사전 25종 시드·Foam 링크·evidence 증빙(재검증 스모크). ⚠ NetSimController 부트스트랩 파괴 회귀 발견([[2026-06-12_netcode/08_result|08_result]] 잔여 0번) |
| 2026-06-12 23:05 | 2026-06-12_netcode | fix | 회귀 해결 — 원인=파일명≠클래스명 MonoScript 미바인딩+DontSave 좀비. NetSimController.cs 분리 등 4건 수정, 2회 연속+강제 리로드 7종 생존 재검증([[2026-06-12_netcode/08_result|08_result]] 잔여 0번 ✅) |
| 2026-06-12 23:50 | 2026-06-12_netcode | retrofit-v4 | 하네스 v4 소급 — 9단계화(⑨ next 신설)·Mermaid 시각화(scope 변경범위·assets 변경맵 등 12종)·ID 앵커·다측면 이점·09_next 이관([[2026-06-12_netcode/09_next|09_next]]) |
| 2026-06-13 12:40 | 2026-06-13_netcode2 | started | netcode2 |
| 2026-06-13 13:40 | 2026-06-13_netcode2 | signed_off | Step 1 전송안정화 — 코드 전부 기구현 재검증(compile 0, Step0와 공존) + 단일 에디터 근사 측정: M1 RTT=0(loopback)·M3 끊김 Connect오발화 0(P0-2)·M8 재호스팅 Steam생존(P1-1). MPPM 2피어 불가 확정(Steam self-connect 차단). M2·정량·SCN-07 30분 soak=2인 수동 인계, 데모 게이트 부분통과 |
| 2026-06-13 23:55 | 2026-06-13_netcode2 | retrofit-v4 | 하네스 v4 소급 — 9단계화(⑨ next 신설·Step 2 이관)·Mermaid 7종(scope 변경범위·assets 변경맵·달성 등)·ID 앵커(T1~10·G1~7)·다측면 이점·산출물 인수인계·Hierarchy 배치([[2026-06-13_netcode2/09_next|09_next]]) |
