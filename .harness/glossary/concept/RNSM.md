---
type: concept
aliases: [Runtime Network Stats Monitor, RNSM HUD]
---
# RNSM (Runtime Network Stats Monitor)

**분류**: concept · 네트워크 계측

## 한 줄 정의
- Unity [[Multiplayer-Tools]] 패키지에 포함된, 게임 화면 위에 네트워크 상태(왕복 지연 [[RTT]]·송수신 바이트 등)를 실시간 숫자로 표시하는 런타임 모니터(HUD).

## 쉬운 설명
> 자동차 계기판처럼 게임 화면 구석에 "지금 네트워크가 얼마나 빠른지, 데이터를 얼마나 주고받는지"를 보여주는 작은 창. 멀티플레이 문제를 플레이 중 눈으로 즉시 확인하는 용도다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T4 "Multiplayer Tools 도입(RNSM HUD + Network Profiler)" 로 첫 등장
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — 패키지는 있으나 HUD 미배치(잔여 갭) 판정
- [[2026-06-12_netcode/04_assets|〃 ④ assets]] — A1 [[RnsmHud]] 스크립트로 구현 확정
- [[2026-06-12_netcode/08_result#달성 대비표|〃 ⑧ result]] — 플레이 스모크에서 표시 확인, M1 RTT=0ms(루프백) 증거 확보

## 관련 용어
[[RnsmHud]] · [[Multiplayer-Tools]] · [[RTT]] · [[Network-Profiler]] · [[M-지표]]
