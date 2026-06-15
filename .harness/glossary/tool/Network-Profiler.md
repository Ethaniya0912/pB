---
type: tool
aliases: [네트워크 프로파일러, Multiplayer Tools Profiler]
---
# Network Profiler

**분류**: tool · Unity 에디터 측정 모듈

## 한 줄 정의
- [[Multiplayer-Tools]] 패키지가 Unity Profiler 창에 추가하는 **네트워크 측정 모듈** — 프레임 단위로 어떤 메시지를 몇 바이트 주고받았는지 기록한다. [[M-지표]]의 M6(대역폭)·M7(메시지량) 측정기.

## 쉬운 설명
> 게임이 주고받는 데이터의 "가계부". 총액(초당 바이트)만 보여주는 [[RNSM]]과 달리, 항목별(어떤 오브젝트가, 어떤 메시지로) 지출 내역까지 보여줘서 "데이터를 어디서 많이 쓰는지" 범인을 찾을 때 쓴다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T4 "RNSM HUD + Network Profiler 도입"
- [[2026-06-12_netcode/02_goal|〃 ② goal]] — G1 캡처 절차 확인 요구
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — 절차서 보강 필요(미흡) 판정

## 관련 용어
[[Multiplayer-Tools]] · [[RNSM]] · [[M-지표]]
