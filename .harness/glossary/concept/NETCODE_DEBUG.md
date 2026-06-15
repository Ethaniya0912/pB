---
type: concept
aliases: [넷코드 디버그 심볼, 로그 채널화]
---
# NETCODE_DEBUG

**분류**: concept · 빌드 구성(컴파일 심볼)

## 한 줄 정의
- 네트워크 상세 로그를 켜고 끄는 **컴파일 심볼**(scripting define symbol). 코드에서 `#if NETCODE_DEBUG`로 감싼 로그는 이 심볼을 정의한 빌드에서만 출력되고, 빼면 통째로 사라진다(릴리즈 콘솔 0건 = M9).

## 쉬운 설명
> 공사장에서만 쓰는 안전모 같은 "개발 중 전용 장비" 스위치. 개발할 땐 로그를 켜서 내부 동작을 들여다보고, 출시 빌드에서는 스위치를 꺼서 로그가 아예 컴파일되지 않게 한다 — 성능 저하도, 콘솔 소음도 없어진다. 이렇게 로그를 스위치 뒤로 모으는 작업을 "채널화"라고 부른다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T9 "P2-4 패킷 로그 채널화(M9)"
- [[2026-06-12_netcode/04_assets|〃 ④ assets]] — A4(b) [[SteamP2PRelayTransport]] 수명주기 로그 11건 가드
- [[2026-06-12_netcode/08_result#달성 대비표|〃 ⑧ result]] — 심볼 미정의 빌드에서 transport 콘솔 로그 0 확인

## 관련 용어
[[SteamP2PRelayTransport]] · [[P0-P1-P2-이슈코드]] · [[M-지표]]
