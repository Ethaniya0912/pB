---
type: script
aliases: [Steam 릴레이 트랜스포트, 커스텀 트랜스포트]
---
# SteamP2PRelayTransport

**분류**: script · 네트워크 전송 계층 (NGO custom transport)

## 한 줄 정의
- [[NGO]](게임 로직)와 Steam P2P 릴레이(실제 통신망) 사이를 잇는 **커스텀 전송 계층**(transport). 모든 네트워크 메시지가 이 클래스를 거친다. 2026-06-12_netcode 사이클에서 (a) [[PROF-프리셋]] 지연/지터 주입 경로(simQueue), (b) 수명주기 로그 11건 [[NETCODE_DEBUG]] 채널화를 적용했다.

## 쉬운 설명
> 게임과 스팀 네트워크 사이의 "우체국". 게임이 보내는 모든 소포(패킷)가 여기를 거쳐 나가고 들어온다. 우체국이기 때문에, 여기에 "배달을 일부러 늦추는 장치"(PROF 시뮬레이션)를 달면 게임 전체가 나쁜 인터넷을 겪는 것처럼 만들 수 있다. Unity 표준 시뮬레이터가 이 커스텀 우체국에는 안 붙어서 직접 장치를 단 것.

## 등장 사이클
- [[2026-06-12_netcode/03_scope|2026-06-12_netcode ③ scope]] — 수명주기 로그 잔류 판정(미흡)
- [[2026-06-12_netcode/04_assets|〃 ④ assets]] — A4 modify: 채널화(b) + 지연/지터 주입(a)
- [[2026-06-12_netcode/05_spec/A3_A4_Transport_and_Bootstrap_mods|〃 ⑤ spec A4]] — DeliverData/simQueue/PumpSimQueue 설계
- [[2026-06-12_netcode/decisions|〃 decisions]] — G3 손실 미주입 결정(Steam reliable 계층 뒤라 영구 유실 위험)

## 관련 용어
[[NGO]] · [[Facepunch-Steamworks]] · [[PROF-프리셋]] · [[NetSimProfiles]] · [[NETCODE_DEBUG]]

## 실제 위치
- [`Assets/Scripts/Networking/SteamP2PRelayTransport.cs`](../../../Assets/Scripts/Networking/SteamP2PRelayTransport.cs)
