---
title: transport-레이어
tags: [network, decision]
status: done
source:
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Assets/Scripts/Networking/SteamClient.cs
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Reports/netcode/Step1_Evidence.md
  - .harness/cycles/2026-06-12_netcode/08_result.md
  - .harness/cycles/2026-06-13_netcode2/08_result.md
verified: 2026-06-15
---

# transport-레이어

`SteamP2PRelayTransport`는 NGO `NetworkTransport`를 상속해 Facepunch Steamworks P2P relay를 전송 계층으로 구현한다. 2026-06-12 Step 1에서 P0·P1 결함 5건이 코드 수정됐고, Step 0에서 지연/지터 시뮬레이터와 계측 코드가 추가됐다.

## 현황 (pB)

**클래스 구조** (`Assets/Scripts/Networking/SteamP2PRelayTransport.cs`)

```
SteamP2PRelayTransport : NetworkTransport
  ├── ClientCallbacks : IConnectionManager   // 클라이언트 이벤트
  ├── ServerCallbacks : ISocketManager       // 서버(호스트) 이벤트
  ├── DeliverData(clientId, payload)         // NetSim 분기 지점
  ├── PumpSimQueue()                         // 지연 만기 패킷 방출
  └── CastToSendType(NetworkDelivery)        // NGO→Steam QoS 매핑
```

**수신 버퍼** (P0-1 수정 완료)

`OnMessage`에서 `byte[size]` 정확 할당 + `Marshal.Copy`. 이전의 `byte[1024]` 고정 버퍼 재사용 제거됨. 서버/클라 경로 동일 로직 통일.

**NGO→Steam 전송 타입 매핑** (P1-2 수정 완료, `CastToSendType`)

| NGO NetworkDelivery | Steam SendType | 비고 |
|---|---|---|
| Unreliable | Unreliable | 의미 일치 |
| UnreliableSequenced | **Reliable** | Steam에 sequenced-unreliable 부재 → 순서 보장 우선 승격 |
| Reliable | Reliable | Steam Reliable은 순서 보장 포함 |
| ReliableSequenced | Reliable | 동일 |
| ReliableFragmentedSequenced | Reliable | Steam 메시지당 512KB 네이티브 지원 |

**RTT 보고** (P0-4 수정 완료)

`GetCurrentRtt`에서 `Connection.QuickStatus().Ping` 반환. 이전에는 상수 0 반환. RNSM HUD의 RTT 칸이 이 값을 소비한다.

**Disconnect 오발화** (P0-2 수정 완료)

`ServerCallbacks.OnDisconnected`에서 `NetworkEvent.Connect` → `NetworkEvent.Disconnect` 수정(1단어 교정). 이전에는 클라이언트 이탈 시 NGO에 '신규 접속'으로 보고되어 유령 클라이언트가 생성됐다.

**Steam API 수명** (P1-1 수정 완료)

- `SteamClient.cs` — RunCallbacks 유일 지점(Update), `DontDestroyOnLoad` 적용, `isOwner` 가드(중복 인스턴스가 Steam API를 끌 수 없게 차단).
- `SteamP2PRelayTransport.Shutdown()` — `SteamClient.Shutdown()` 호출 제거 → 소켓(`clientConnection.Close()`, `socketManager.Close()`)만 정리. 재호스팅 시 Steam API 1회 초기화 유지.

**NetSim 지연/지터 주입** (Step 0 추가)

`DeliverData` → `simQueue(DelayedPacket)` → `PumpSimQueue()` 경로. `NetSimProfiles.Enabled == false`(기본)이면 즉시 전달 — 무침습. 손실은 신뢰성 계층 이후라 미주입(Clumsy 보완). F8 토글로 PROF-G/A/B 순환.

**NETCODE_DEBUG 채널화** (Step 0 P2-4 완료)

수명주기 `Debug.Log` 11건 + 패킷당 로그를 `#if NETCODE_DEBUG` 가드로 격리. 릴리즈 빌드 콘솔 출력 0. 카운터(`transport.recv.client.bytes`, `transport.send.bytes` 등)는 항상 집계.

**LateUpdate 수신 루프**

`socketManager?.Receive()` / `clientConnection?.Receive()` 호출 후 `PumpSimQueue()`. `SteamClient.IsValid && NetworkManager.Singleton.IsListening` 가드로 불필요한 호출 차단.

## 설계·결정

- NGO 기본 UTP 미사용: Steam relay(SDR) 경유 NAT 우회·IP 비노출 필요 → `NetworkTransport` 상속 커스텀 구현.
- 고정 버퍼 대신 정확 할당: NGO 비동기 소비 경쟁조건 제거 + 대형 메시지(씬 이벤트·NetworkList 초기 스냅샷) 손상 방지.
- UnreliableSequenced → Reliable 승격: Steam에 sequenced-unreliable가 없어 순서 보장을 우선시.
- Steam API 수명 단일 소유(SteamClient): Transport.Shutdown이 Steam API를 끄던 기존 결함(재호스팅 불안정, M8) 제거.

## ⚠ 비판·리스크

**심각도: 높음**

- **R1 계측·PROF 코드가 게임 transport에 혼재**: `SteamP2PRelayTransport.cs` 단일 파일에 NGO 이벤트 처리와 NetDiag 카운터·시뮬 큐·`[Step 0/PROF]` 주석이 뒤섞인다. `NETDIAG_DISABLED` 심볼 미정의 시 릴리즈 빌드에도 시뮬 큐 메모리·PumpSimQueue 호출 오버헤드가 존재한다. asmdef 분리(Step 5 계획)가 미착수인 현재, 계측 코드 버그가 transport를 직접 오염할 수 있다.
- **R2 LateUpdate NullReference try-catch 방치**: `LateUpdate`의 `Receive()` 호출을 try-catch로 감싸고 있다(L403~415). Shutdown이 소켓을 null화하면서 발생하는 예외를 "정상"으로 방치한 것이다. 예외 발생 빈도가 보이지 않고, 실제 문제적 null 참조와 구분이 안 된다.
- **R3 UnreliableSequenced→Reliable 승격의 대역폭 영향 미측정**: NGO가 UnreliableSequenced로 송신하는 패킷(주로 위치·애니메이터 블렌드)이 Reliable로 승격되면 재전송·순서 버퍼 오버헤드가 추가된다. Before(Unreliable 매핑) 대비 대역폭 증가분이 실측된 적 없다(M6 미집행).

**심각도: 보통**

- **R4 GetCurrentRtt의 예외 무시**: `try { ... } catch { return 0; }` 구조라 QuickStatus 조회 실패가 조용히 0으로 반환된다. 연결 이상 시 RNSM RTT가 0으로 표시되어 진단을 어렵게 한다.
- **R5 Send 경로의 1대多 순회**: 서버에서 특정 clientId로 송신 시 `socketManager.Connected`를 순회해 ID 대조. 클라이언트 수가 많으면 O(N) 루프 발생. 친선 코옵(2~4인) 범위에서는 무시할 수 있으나 구조적 취약점이다.
- **R6 Steam relay 단일 경로 의존**: Steam 서버 장애 시 연결 불가. 직접 P2P 폴백 없음. 친선 코옵에서 실용적 위험은 낮지만 Steam 가용성에 100% 종속.

**권고**: Step 5에서 asmdef 분리를 수행해 릴리즈 빌드에서 시뮬 큐·계측 코드를 완전 제거하라. LateUpdate try-catch를 null 검사로 교체해 오류를 가시화하라.

## 관련 문서

- [[netcode-solution|netcode-솔루션]]
- [[network-topology|네트워크-토폴로지]]
- [[bandwidth-budget|대역폭-예산]]

---
← [[03-network-hub|03 · 네트워크 아키텍처]] · [[index|인덱스]]
