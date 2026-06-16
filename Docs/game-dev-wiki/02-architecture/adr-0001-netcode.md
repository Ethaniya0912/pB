---
title: adr-0001-netcode-선정
tags: [adr, network, decision]
status: decided
source:
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Assets/Scripts/Networking/SteamClient.cs
  - Packages/manifest.json
  - .harness/cycles/2026-06-12_netcode/decisions.md
verified: 2026-06-15
---

# adr-0001-netcode-선정

NGO(Netcode for GameObjects) + 커스텀 SteamP2PRelayTransport(Facepunch.Steamworks P2P 릴레이) 선정.

## 맥락

pB는 Steam 플랫폼 Steam 전용 협동 코옵 게임이다. 전용 서버 없이 호스트가 게임 서버 역할을 겸하는 P2P-relay 구조가 비용·플랫폼 통합 양면에서 유리하다. Unity 6000.3.1f1 환경에서 NGO가 공식 패키지이며, Steam 매치메이킹·로비·릴레이 인프라가 이미 갖춰져 있다.

검토한 대안:

| 솔루션 | 요약 | 탈락 이유 |
|---|---|---|
| Mirror | 오픈소스·커뮤니티 기반 | Unity 6 공식 지원 불확실, 장기 유지보수 리스크 |
| FishNet | 고성능 오픈소스 | 동일한 비공식 패키지 리스크, 팀 학습 비용 |
| Unity Netcode for Entities (DOTS) | ECS 기반 고성능 | 프로젝트가 OOP MonoBehaviour 기반이라 전면 리팩터 없이 도입 불가 |
| 전용 서버 | 지연 최소화 | 운영 비용·인프라 부담, 인디 EA 규모에 부적합 |

## 결정

`com.unity.netcode.gameobjects` 2.7.0 + 커스텀 `SteamP2PRelayTransport` 조합을 선택한다.

- `Assets/Plugins/Facepunch/Facepunch.Steamworks.Win64.dll` 로 Steam P2P 릴레이 접속
- `SteamNetworkingSockets.CreateRelaySocket` / `ConnectRelay` 로 서버·클라이언트 소켓 관리
- `SteamLobbyManager` 가 로비 생성·입장·탈퇴 전체 수명주기를 담당
- `SteamClient` 컴포넌트가 Steam API 초기화·`RunCallbacks` 단독 소유

## 근거

1. **플랫폼 통합**: Steam 로비·매치메이킹·릴레이가 패키지로 묶여 있어 별도 NAT 펀치스루 구현 불필요.
2. **공식 NGO**: Unity 6 버전 지원, 멀티플레이어 툴(RNSM HUD, Network Profiler) 공식 연동.
3. **최소 추가 의존**: Facepunch.Steamworks 단일 dll, 코드 규모 작음.
4. **하네스 실증**: 2026-06-12_netcode 사이클에서 Step 0 계측 기반(NetDiagnostics·RnsmHud·NetSimProfiles) 구축 완료, Step 1 전송 안정화(P0-1 버퍼 동적화·P0-2 Disconnect 수정·P0-4 RTT·P1-1 Shutdown 단일화·P1-2 SendType 교정) 코드 완료.

## 영향

**장점**
- 전용 서버 비용 없음, Steam 릴레이로 NAT 우회 자동 처리.
- Unity 공식 NGO 에코시스템(Multiplayer Tools 2.2.3 패키지 포함).
- 코드베이스가 이미 NGO NetworkObject·RPC 패턴으로 작성되어 있어 추가 마이그레이션 불필요.

**단점·제약**
- 최대 플레이어 수가 Steam 릴레이 제한(현재 `maxPlayers = 4`)에 종속된다.
- NGO Network Simulator가 UnityTransport 전용이라 커스텀 transport에서 지연/손실 시뮬레이션을 직접 구현해야 함(NetSimProfiles.cs로 해결).
- 호스트 이탈 = 세션 종료 구조 — 재호스팅·난입(T26)은 후속 Step 3에서 해결 예정.
- `GetCurrentRtt` 실측값 반환(P0-4)은 Step 1에서 구현됐으나 2인 P2P 실측은 미집행(수동 인계).

**되돌리기 비용**: NGO 전면 교체는 NetworkObject·RPC 전수 리팩터 + 새 transport 구현 필요. NGO 도입 초기 단계에서만 현실적이며 현 시점에서는 매우 높다.

## ⚠ 비판·리스크

**[심각도: 높음] 2인 P2P 베이스라인 미집행**
M1~M11 지표(RTT·패킷 바이트·Disconnect 오발화 등)가 단일 에디터 루프백으로만 확인됐다. Steam 실제 릴레이를 경유한 2인 측정이 없으며, 릴레이 지연·NAT 경로 차이로 실제 RTT는 M1 loopback 기록(0 ms)과 크게 다를 수 있다. EA 진입 전 반드시 2인 실측(SCN-01~07 × PROF-G/A) 이 필요하다.

**[심각도: 높음] 호스트 단독 서버 구조의 보안·권위 취약**
호스트가 게임 서버이므로 호스트 치팅 방어 수단이 없다. pB 설계 목표가 "일관성(치팅 방지 아님)"(T18 R6)으로 명시됐으나, 경쟁 요소 추가 시 구조적 한계로 돌아온다.

**[심각도: 보통] UnreliableSequenced → Reliable 승격 대역폭 증가**
SteamNetworkingSockets에는 순서 보장 비신뢰 전송이 없어 NGO `UnreliableSequenced`를 `Reliable`로 승격했다(P1-2). 애니메이션 블렌드값처럼 드롭해도 되는 데이터가 신뢰 전송을 쓰게 되어 대역폭이 의도보다 높아질 수 있다. Step 4 대역폭 최적화(T29) 시 재검토 필요.

**[심각도: 보통] Steam AppID 480(Spacewar) 사용 중**
`steam_appid.txt`가 개발용 480 사용 중이다. 실제 앱 ID 미신청 상태로 EA 빌드 배포 시 로비 격리(`GameIdValue = "PennutButterProject"` 키로 우회 중)가 불완전할 수 있다. 정식 AppID 신청 및 적용이 필요하다.

## 관련 문서

- [[adr-template|adr-template]]
- [[render-pipeline|렌더-파이프라인]]
- [[di-container|DI-컨테이너]]

---
← [[02-architecture-hub|02 · 아키텍처 기반 결정]] · [[index|인덱스]]
