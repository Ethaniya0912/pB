---
type: package
aliases: [com.unity.multiplayer.tools, Unity Multiplayer Tools]
---
# Multiplayer Tools

**분류**: package · Unity 공식 패키지 (`com.unity.multiplayer.tools` 2.2.3)

## 한 줄 정의
- Unity 공식 **멀티플레이 계측 패키지** — [[RNSM]](런타임 HUD), [[Network-Profiler]](Profiler 모듈), Network Simulator(네트워크 열화 시뮬레이터)를 포함한다.

## 쉬운 설명
> 멀티플레이 게임의 "정비 공구함". 게임이 네트워크를 어떻게 쓰고 있는지 들여다보는 도구들이 한 상자에 들어 있다.
> ※ 주의: 공구함의 Network Simulator는 Unity 표준 통신 장비(UnityTransport) 전용이라, 본 프로젝트의 커스텀 장비([[SteamP2PRelayTransport]])에는 맞지 않았다 → PROF 시뮬레이션을 직접 구현([[NetSimProfiles]])한 배경.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T4 패키지 도입 항목
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — manifest에 2.2.3 기설치 확인
- [[2026-06-12_netcode/decisions|〃 decisions]] — G2 "Simulator는 UnityTransport 전용" 리스크 기록

## 관련 용어
[[RNSM]] · [[Network-Profiler]] · [[RnsmHud]] · [[NGO]]

## 실제 위치
- [`Packages/manifest.json`](../../../Packages/manifest.json) — 패키지 선언
