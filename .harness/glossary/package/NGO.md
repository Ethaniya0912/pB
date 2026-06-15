---
type: package
aliases: [Netcode for GameObjects, com.unity.netcode.gameobjects, 넷코드]
---
# NGO (Netcode for GameObjects)

**분류**: package · Unity 공식 네트워킹 프레임워크

## 한 줄 정의
- Unity 공식 **고수준 네트워킹 프레임워크**. 상태 동기화(NetworkVariable)·원격 호출(RPC)·오브젝트 스폰과 "누가 결정권을 갖나"(권위, authority) 모델을 제공한다. 본 프로젝트 코옵 멀티플레이의 토대.

## 쉬운 설명
> 멀티플레이 규칙이 미리 짜여 있는 "레고 베이스판". 캐릭터 위치를 서로 맞추는 법, 서버에 행동을 요청하는 법 같은 어려운 기반을 Unity가 만들어 두었고, 게임은 그 위에 블록(게임 로직)을 쌓는다. 실제 데이터가 오가는 통로는 transport([[SteamP2PRelayTransport]])가 담당한다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — 전 Step의 기반(NetworkVariable·RPC 규약 등)
- [[2026-06-12_netcode/02_goal|〃 ② goal]] — G1~G5 필요 시스템 열에 등장

## 관련 용어
[[SteamP2PRelayTransport]] · [[Multiplayer-Tools]] · [[desync]] · [[RTT]]
