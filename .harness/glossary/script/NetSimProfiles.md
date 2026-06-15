---
type: script
aliases: [NetSim]
---
# NetSimProfiles

**분류**: script · 네트워크 시뮬레이션 (`NetDiag.NetSimPreset` + `NetDiag.NetSimProfiles`) — 2026-06-12_netcode 사이클 신규(A2)

## 한 줄 정의
- [[PROF-프리셋]](PROF-G/A/B)의 수치 정의·활성 상태 관리를 담은 정적 클래스. [[SteamP2PRelayTransport]]의 수신 경로 지연/지터 주입이 이 상태를 읽어 동작한다. F8 토글은 [[NetSimController]]가 담당(처음엔 같은 파일이었으나 파일명=클래스명 규칙으로 분리 — 23:05 회귀 수정).

## 쉬운 설명
> PROF 스위치의 "회로도이자 리모컨". 어떤 프리셋이 있고(G/A/B), 지금 어떤 게 켜져 있는지 관리하며, 게임 중 F8 키를 누르면 다음 프리셋으로 넘어간다. 패킷 순서가 섞이지 않도록(reliable 메시지 보호) 지연 시간이 항상 단조 증가하게 클램프하는 안전장치가 들어 있다.

## 등장 사이클
- [[2026-06-12_netcode/04_assets|2026-06-12_netcode ④ assets]] — A2 신규 확정
- [[2026-06-12_netcode/05_spec/A2_NetSimProfiles|〃 ⑤ spec A2]] — 프리셋 수치·재정렬 방지 정책 명세
- [[2026-06-12_netcode/08_result#달성 대비표|〃 ⑧ result]] — F8 토글 라이브 동작(OFF→G→A) 확인

## 관련 용어
[[PROF-프리셋]] · [[SteamP2PRelayTransport]] · [[RnsmHud]] · [[Clumsy]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/NetSimProfiles.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/NetSimProfiles.cs)
