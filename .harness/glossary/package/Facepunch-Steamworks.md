---
type: package
aliases: [Facepunch.Steamworks, 페이스펀치]
---
# Facepunch.Steamworks

**분류**: package · 외부 라이브러리 (Steam API C# 래퍼)

## 한 줄 정의
- Steam의 기능(로비·친구·P2P 릴레이 소켓 등)을 C#에서 쓰기 쉽게 감싼 **래퍼 라이브러리**. [[SteamP2PRelayTransport]]가 이 라이브러리로 Steam 릴레이 통신을 수행한다.

## 쉬운 설명
> 스팀이라는 거대한 우체국의 "한국어 안내 창구". 스팀의 원래 창구(C++ API)는 다루기 까다로운데, 이 라이브러리가 C# 개발자가 쓰기 편한 형태로 통역해 준다. P2P 릴레이 = 두 플레이어가 서로 직접 연결되지 않아도 스팀 서버가 중간에서 소포를 전달해 주는 방식.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T14 "P0-4 RTT 보고 구현"의 API 확인 대상(Step 1, 후속 사이클)
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — Step 1 기구현 코드(SteamClient 등) 확인

## 관련 용어
[[SteamP2PRelayTransport]] · [[NGO]] · [[RTT]]
