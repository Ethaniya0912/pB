---
type: concept
aliases: [SCN-01~07, 표준 시나리오, SCN]
---
# SCN-시나리오 (SCN-01~07)

**분류**: concept · 측정 체계

## 한 줄 정의
- 측정·검증을 항상 같은 조건으로 반복하기 위한 **7종 표준 테스트 시나리오** — 접속, 끊김 재호스팅, 전투 50회, 획득·인벤토리, 난입, holdout 부하, [[soak-테스트|soak]].

## 쉬운 설명
> 자동차 충돌 테스트의 "정해진 코스"와 같다. 매번 다른 방식으로 테스트하면 수정 전후를 공정하게 비교할 수 없으므로, 누가 언제 해도 같은 절차가 되도록 시나리오를 표준화해 둔 것. 결과 파일명도 `SCN-XX_PROF-X_StepN_before|after` 규칙을 따른다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T38 "표준 시나리오 SCN-01~07"
- [[2026-06-12_netcode/02_goal|〃 ② goal]] — G7 절차서(`SCN_Procedures.md`) 요구
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — 절차서 기구현 확인

## 관련 용어
[[M-지표]] · [[soak-테스트]] · [[베이스라인]] · [[PROF-프리셋]]

## 실제 위치
- [`Reports/netcode/SCN_Procedures.md`](../../../Reports/netcode/SCN_Procedures.md) — 시나리오 절차서
