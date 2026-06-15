# 03 · scope — 변경 범위·영향도

> **이 문서는?** 만들 것 중 "이미 있는 것 / 새로 만들 것 / 고칠 것"을 가려낸 판정표입니다(무엇).
> 중복 구현을 막고 이번 사이클이 **건드릴 범위**를 못박으려고(왜) 프로젝트를 read-only 스캔해
> 상태별 색으로 시각화하며(어떻게), ②goal 직후 스캔하고 사람은 [G2](decisions.md#G2) 에서 범위를 승인합니다(언제·누가).
> **핵심 발견: Step 1 코드는 전부 기구현(직전 Step 0 사이클서 스캔 확인). 본 사이클은 신규 구현 0 — "기구현 재검증 + 근사 측정 집행"이 전부.** → G2 승인 대상.

## 한눈에 — 변경 범위 (이 사이클은 변경 0)
> 색: 기구현·검증만=회색(keep)·단일 에디터 측정=파랑(flow)·2인 대기=골드(mod 보류). **손대는 Unity 에셋 0** — 전부 직전 사이클 산출물을 측정만.
```mermaid
flowchart TB
  subgraph asis["as-is — 전부 기구현 (변경 0)"]
    direction TB
    TR["SteamP2PRelayTransport<br/>P0-1/2/4·P1-2/1"]:::keep
    WIS["WorldItemSpawner<br/>P2-8 가드"]:::keep
    NEL["계측 7종 (Step 0)"]:::keep
  end
  subgraph tobe["to-be — 측정만 (신규 0)"]
    direction TB
    M1["M1 RTT (host)"]:::flow
    M3["M3 끊김 (host)"]:::flow
    M8["M8 재호스팅 (host)"]:::flow
    WAIT["M2·정량·SCN-07 soak<br/>2인 수동 대기"]:::mod
  end
  TR -. 측정 .-> M1
  TR -. 측정 .-> M3
  TR -. 측정 .-> M8
  NEL -. events.csv .-> M3
  classDef keep fill:#ECEFF3,stroke:#5C6675,color:#374151;
  classDef flow fill:#EBF0FF,stroke:#2A52DB,color:#1e3a8a;
  classDef mod  fill:#FBF0DD,stroke:#B5731A,color:#7c4a03;
```

## A. Step 1 코드 기구현 판정 (변경 0, 검증만)
| 연결 goal | 상태 | 대상 에셋·타입 | 존재 근거 | 리스크 |
|---|---|---|---|---|
| [G1](02_goal.md#G1) / T1 (P0-1) | **기구현** | [[SteamP2PRelayTransport]] OnMessage ×2 | L84·L159 "size만큼 정확 할당", 1KB 고정버퍼 흔적 0 | 낮음 |
| [G1](02_goal.md#G1) / T2 (P0-2) | **기구현** | 〃 ServerCallbacks.OnDisconnected | L141 `InvokeOnTransportEvent(NetworkEvent.Disconnect…)` | 낮음 |
| [G1](02_goal.md#G1) / T3 (P0-4) | **기구현** | 〃 GetCurrentRtt | L347 `QuickStatus().Ping` 반환 | 낮음 |
| [G1](02_goal.md#G1) / T4 (P1-2) | **기구현** | 〃 CastToSendType | L421~ 매핑표(UnreliableSequenced→Reliable 승격) | 낮음 |
| [G1](02_goal.md#G1) / T5 (P1-1) | **기구현** | 〃 Shutdown + SteamClient/SteamLobbyManager | L381 "SteamClient.Shutdown 금지, 소켓만 정리" | 낮음 |
| [G1](02_goal.md#G1) / T6 (P2-8최소) | **기구현** | WorldItemSpawner Awake 가드 | Step1_Evidence 기록 | 낮음 |
| (정합) 직전 Step 0 추가분 | **공존 확인** | 동 파일 DeliverData/PROF/P2-4 채널화 | Step 1 마커 11곳 + Step 0 추가분 동시 존재, compile 0(직전 사이클) | 낮음 |

## B. 측정(근사) 실행 가능성 — **이 사이클의 실질 작업**
> 사용자 G1-Q2 "근사 더 적극 시도"에 따라 단일 에디터에서 가능한 한 측정한다. 도구는 키(F9/F10) 기반이나 `NetworkManager.StartHost/Shutdown`은 exec 구동 가능.

| 지표 | 단일 에디터 가능? | 방법 | 한계 |
|---|---|---|---|
| **M1 RTT** | ✅ 가능 | StartHost → `GetCurrentRtt(0)` | loopback=0 (Step0 Before와 동일, 정상) |
| **M3 끊김 정합** | ✅ 가능(부분) | StartHost → `Shutdown()` → events.csv `Disconnect` 짝 확인 (P0-2 효과: Connect 오발화 0) | host-local 끊김 — 원격 클라 끊김 정리체인은 2인 |
| **M8 재호스팅** | ✅ 가능(부분) | StartHost→Shutdown→StartHost ×N → Steam 재초기화 에러 0 (P1-1 효과: SteamClient 생존) | 정량 10/10·유령 클라는 2인 |
| **M2 경계값** | ❌ 불가 | — | **BoundaryEchoHarness L70 명시**: "호스트 루프백은 전송 계층을 타지 않아 의미 없음". 별도 클라 필수 |
| SCN-07 30분 soak | ❌ 불가(시간·2인) | — | 데모 게이트 1차 — 수동 |

## C. 2번째 피어 가능성 조사 (적극 근사 시도)
- **Multiplayer Play Mode 2.0.1 설치됨**(`manifest.json`) — 에디터 내 virtual player 기능 존재.
- **그러나 추정 차단**: [[SteamP2PRelayTransport]]는 [[Facepunch-Steamworks]] P2P 릴레이 — **단일 머신/단일 Steam 계정에서 host↔client self-connect 불가**(Steam이 동일 사용자로 인식). 계획 §7이 "2인 실기기"를 못박은 근본 이유. → MPPM 2피어로도 Steam P2P 측정은 불가 추정.
- **판정**: M2/정량 M8/soak는 **2인 실기기 수동**으로 인계. 단일 에디터는 M1·M3·M8(생존)까지.

## 이전 사이클 재사용
- 참조: [[2026-06-12_netcode/03_scope|netcode(Step0) scope]](기구현 스캔 원본) · [[2026-06-12_netcode/08_result|Step0 결과]]. **재작성 금지** — Step 1 코드·계측 7종 전부 보존, 본 사이클은 측정·검증만.

## G2 확인 대상 (승인 필요)
1. **사이클 목적 재정의 확정**: 신규 구현 0 → **기구현 재검증 + 근사 측정(M1·M3·M8 host-only)**. 동의?
2. **2피어 측정 차단 수용**: MPPM 있으나 Steam 단일계정 self-connect 한계로 M2·정량 soak는 2인 수동 인계. 동의? (혹은 MPPM 2피어를 1회 실증 시도해볼지.)
3. 측정 실행은 구현 루프(플레이 진입 + exec StartHost/Shutdown 반복)에서 수행 → 증빙은 `evidence/`에 저장.

---
## 🔗 관련 문서 (Foam)
- 이전 [[2026-06-13_netcode2/02_goal|② goal]] · **③ scope**(현재) · 다음 [[2026-06-13_netcode2/04_assets|④ assets]]
- 게이트 결정: [[2026-06-13_netcode2/decisions|decisions]] (G2)
- 직전 스캔: [[2026-06-12_netcode/03_scope|Step0 scope]]
- 용어: [[SteamP2PRelayTransport]] · [[Facepunch-Steamworks]] · [[M-지표]] · [[BoundaryEchoHarness]] · [[NetEventLogger]] · [[SCN-시나리오]] · [[soak-테스트]] · [[RTT]] → [[_glossary|용어 사전]]
