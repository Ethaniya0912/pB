---
title: netcode-솔루션
tags: [network, decision]
status: done
source:
  - Packages/manifest.json
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Assets/Scripts/Character/CharacterNetworkManager.cs
  - Reports/netcode/코옵_Netcode_실행계획_v1.1.md
  - .harness/cycles/2026-06-12_netcode/08_result.md
verified: 2026-06-15
---

# netcode-솔루션

pB는 Unity Netcode for GameObjects(NGO) 2.7.0과 커스텀 `SteamP2PRelayTransport`를 결합해 사용한다. NGO의 표준 UTP 대신 Facepunch Steamworks P2P relay를 transport로 직접 구현했다.

## 현황 (pB)

**패키지 버전** (`Packages/manifest.json` 실측)

| 패키지 | 버전 |
|---|---|
| `com.unity.netcode.gameobjects` | 2.7.0 |
| `com.unity.multiplayer.tools` | 2.2.3 |
| `com.unity.multiplayer.playmode` | 2.0.1 |
| `com.unity.multiplayer.center` | 1.0.1 |

**핵심 클래스 분포**

- `SteamP2PRelayTransport : NetworkTransport` — NGO transport 인터페이스를 Facepunch Steamworks로 구현(`Assets/Scripts/Networking/`).
- `SteamLobbyManager` — Steam 매치메이킹·로비·세션 수명 관리.
- `SteamClient` — Steam API 초기화·종료·콜백 펌핑 유일 지점.
- `CharacterNetworkManager : NetworkBehaviour` — 캐릭터 NetworkVariable 백본(위치·HP·스태미나·전투 플래그 등 다수).

**계측·진단 인프라** (Step 0 완료)

- `Assets/Scripts/Utilities/NetDiagnostics/` — NetEventLogger, VerdictLogger, StateChecksumV0, NetSimProfiles, NetSimController, RnsmHud, BoundaryEchoHarness, SoakHarness, NetDiagnosticsBootstrap.
- RNSM(RuntimeNetStatsMonitor) HUD — 플레이 진입 시 자동 부착. RTT/송신·수신 바이트 인게임 표시.
- PROF 프리셋(F8 토글) — PROF-G(RTT 30ms) / PROF-A(150ms) / PROF-B(250ms) 지연·지터 주입. 손실은 Clumsy 보완.

## 설계·결정

**NGO 선정 이유**

| 항목 | 이유 |
|---|---|
| Unity 공식 지원 | Unity 6.3(6000.3.1f1) 환경과 공식 통합 보장 |
| Steam transport 커스터마이징 | `NetworkTransport` 추상 클래스로 교체 가능한 구조 |
| 학습 곡선 | Mirror/FishNet 대비 Unity 에코시스템 친화 |
| 도구 통합 | Multiplayer Tools(RNSM·Network Profiler)가 NGO 종속 |

**UTP 대신 커스텀 Steam transport 선정**

NGO 기본 UTP(`com.unity.transport`)는 Steam relay(SDR)를 지원하지 않는다. IP 노출 없이 NAT 우회가 필요하므로 Facepunch Steamworks `SteamNetworkingSockets` API를 직접 래핑한 `SteamP2PRelayTransport`를 구현했다.

**Multiplayer Play Mode(MPPM) 한계**

MPPM 2.0.1이 설치되어 있으나, 동일 Steam 계정의 relay self-connect가 차단되어 단일 머신 2피어 테스트가 불가능하다(2026-06-13 실증). 모든 2인 측정은 실제 2대의 기기 또는 2개의 Steam 계정이 필요하다.

## ⚠ 비판·리스크

**심각도: 높음**

- **R1 커스텀 transport의 테스트 불가능 구조**: Steam relay 의존으로 단일 에디터 테스트가 구조적으로 제한된다. M2(대형 메시지)·M3(끊김 정합) 정량 측정·M5(전투 판정 일치율) 등 핵심 지표가 전부 2인 실기기에 묶여 있다. QA 사이클이 느릴 수밖에 없다.
- **R2 NGO Prediction/Rollback 미사용**: NGO 2.x의 클라이언트 예측 API(`ClientNetworkTransform`, `NetworkRigidbody` 등)를 활용하지 않는다. `CharacterNetworkManager.Update()`에서 Owner가 직접 위치를 쓰고 비Owner는 `Vector3.SmoothDamp`로 보간할 뿐이다. 예측·재조정 커스텀 구현도 없다(상세 → [[prediction-reconciliation|예측-재조정-보간]]).
- **R3 계측 코드가 transport에 혼재**: `SteamP2PRelayTransport.cs`는 게임 로직(NGO 이벤트 처리)과 계측 코드(`NetDiag.NetDiagnostics.IncrementCounter`, `[Step 0/PROF]` 주석)가 같은 파일에 공존한다. Step 5에서 asmdef 분리가 계획되어 있으나 현재는 릴리즈 빌드와 계측 코드가 분리되지 않는다(`NETDIAG_DISABLED` 심볼로만 비활성화).
- **R4 AppID 480(Spacewar) 사용**: 실제 게임 AppID 없이 공용 개발 AppID로 운영 중. 타 개발자의 로비와 혼재 가능성 있음.

**심각도: 보통**

- **R5 NGO 버전 잠금**: NGO 2.7.0은 Unity 6용 버전이지만 Breaking Change가 잦다. 업그레이드 시 RPC 문법(`[Rpc(SendTo...)]`) 마이그레이션 필요. 현재 `[ServerRpc]`와 신문법이 혼재(Step 3 P1-7 대상).

**권고**: 계측 전용 asmdef를 Step 5 이전으로 앞당기고, 2인 측정용 2번째 Steam 계정을 QA 환경에 상시 확보하라. NGO 버전 업그레이드 정책을 결정하라.

## 관련 문서

- [[transport-layer|transport-레이어]]
- [[network-topology|네트워크-토폴로지]]
- [[prediction-reconciliation|예측-재조정-보간]]

---
← [[03-network-hub|03 · 네트워크 아키텍처]] · [[index|인덱스]]
