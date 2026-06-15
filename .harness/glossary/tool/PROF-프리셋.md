---
type: tool
aliases: [PROF-G/A/B, PROF-G, PROF-A, PROF-B, 네트워크 프로파일]
---
# PROF-프리셋 (PROF-G/A/B)

**분류**: tool · 네트워크 환경 시뮬레이션 프리셋

## 한 줄 정의
- 네트워크 상태를 3단계로 흉내내는 **환경 프리셋** — PROF-G(양호: [[RTT]] 30ms), PROF-A(평균: 150ms+지터 30), PROF-B(열악: 250ms+지터 60). 플레이 중 **F8 키**로 순환 토글한다.

## 쉬운 설명
> 에어컨의 약/중/강처럼 인터넷 상태를 "좋음/보통/나쁨"으로 바꿔 끼우는 스위치. 개발자의 쾌적한 환경에서만 테스트하면 실제 유저의 나쁜 인터넷에서 터지는 문제를 못 보므로, 일부러 나쁜 조건을 만들어 시험한다.
> ※ 지터(jitter) = 지연 시간이 들쭉날쭉 흔들리는 정도. ※ 손실률(패킷이 사라지는 비율)은 코드로 주입하지 않고 [[Clumsy]]로 보완한다(G3 결정 — Steam 신뢰성 계층 뒤라 코드 주입 시 메시지가 영구 유실될 위험).

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T5 "네트워크 프로파일 PROF-G/A/B 프리셋화"
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — 신규 판정(프리셋 미구성)
- [[2026-06-12_netcode/04_assets|〃 ④ assets]] — A2 [[NetSimProfiles]]로 구현(코드 프리셋 + F8 토글)
- [[2026-06-12_netcode/decisions|〃 decisions]] — G3: 지연/지터만 주입·손실 제외·토글 키 F8 확정

## 관련 용어
[[NetSimProfiles]] · [[Clumsy]] · [[RTT]] · [[SteamP2PRelayTransport]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/NetSimProfiles.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/NetSimProfiles.cs)
