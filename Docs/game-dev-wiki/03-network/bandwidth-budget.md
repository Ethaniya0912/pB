---
title: 대역폭-예산
tags: [network]
status: done
source:
  - Assets/Scripts/Character/CharacterNetworkManager.cs
  - Assets/Scripts/Utilities/NetDiagnostics/NetSimProfiles.cs
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Reports/netcode/코옵_Netcode_실행계획_v1.1.md
  - Reports/netcode/Step0_Baseline.md
verified: 2026-06-15
---

# 대역폭-예산

pB의 대역폭 실측은 미집행 상태다. M6(호스트 업로드)·M7(진행형 값 트래픽) 모두 베이스라인이 없다. 측정 인프라(RNSM HUD, Network Profiler, 계측 카운터)는 Step 0에서 도입됐으나 2인 P2P 측정이 실행되지 않았다. 최적화 계획(Step 4)은 존재하지만 현재 어떤 오브젝트가 얼마를 쓰는지 아는 사람이 없다.

## 현황 (pB)

**계측 인프라** (Step 0 완료)

- RNSM(RuntimeNetStatsMonitor) HUD — 플레이 진입 시 자동 부착. RTT·호스트 송신·수신 바이트 인게임 표시.
- Network Profiler(`com.unity.multiplayer.tools 2.2.3`) — 오브젝트·변수별 대역폭 분해 가능. 2인 세션에서만 의미 있는 데이터 수집 가능.
- Transport 송신 카운터 — `NetDiag.NetDiagnostics.AddCounter("transport.send.bytes", payload.Count)` (SteamP2PRelayTransport.Send, L234).
- Transport 수신 카운터 — `transport.recv.client.bytes`, `transport.recv.server.bytes`.

**베이스라인 측정 현황** (`Reports/netcode/Step0_Baseline.md`)

| 지표 | 예상 | 실측 |
|---|---|---|
| M6 호스트 업로드(SCN-06) | ~200KB/s 추정 | **미집행** |
| M7 진행형 값 트래픽 | 요리 4종 매 틱 델타 | **미집행** |

2026-06-12 단일 에디터 부분 측정에서 M1(RTT=0ms)만 확정됐다. M6·M7은 원격 피어 없이 의미 있는 데이터가 수집되지 않아 공란이다.

**NetworkVariable 브로드캐스트 규모 추정**

`CharacterNetworkManager.cs` 기준 캐릭터 1인당 NetworkVariable 수:
- 위치(Vector3) + 회전(Quaternion) = 12 + 16 = 28B/틱 (변화 없음도 Owner가 매 프레임 쓰는 구조)
- 애니메이터 블렌드 3종(float×3) = 12B/틱
- 전투 플래그 7종(bool×7) = 7B/틱
- 자원 6종(int·float 혼합) ≈ 24B/틱
- 기타 (타겟 ID, 잡기 ID, 스탯 등)

캐릭터 1인당 대략 80~120B/틱 추정(변경 없는 변수 포함 시). AI 오브젝트가 플레이어 풀 백본을 상속하는지 별도 경량 백본인지 미확인.

**최적화 계획** (Step 4 P2-1~P2-3, 미착수)

| 항목 | 내용 | 예상 효과 |
|---|---|---|
| AI 위치 송신 게이팅 | 정지·비전투 시 중단(OptimizedNetworkItem 패턴 이식) | 미측정 |
| 블렌드 값 양자화 | 0.05 단위 양자화 + 변경 시에만 송신 | 미측정 |
| AI 경량 백본 분리 | 위치+상태Enum+어그로만 (플레이어 풀 백본 상속 해제) | 미측정 |
| 거리 기반 갱신 차등 | NetworkVariable 클라별 차등 불가 → CheckObjectVisibility 또는 커스텀 RPC 필요 | 미착수(①~③ 후 M6 미달 시만) |
| 요리 {state, startServerTime} | 진행도 매 틱 전송 → 상태+시작 시각만 | M7 = 0 목표 |

목표: M6 -40% 이상.

**PROF 프리셋** (Step 0)

F8 토글: PROF-G(RTT 30ms) / PROF-A(150ms, 지터±30ms) / PROF-B(250ms, 지터±60ms). 손실 시뮬은 Clumsy 보완(코드 Transport 이후 지점이라 Reliable 패킷 손실 미지원).

## 설계·결정

| 결정 | 근거 |
|---|---|
| 대역폭 예산 수치 미정 | 실측 먼저, 예산은 M6 베이스라인 후 설정 방침 |
| Network Profiler 채택 | NGO 종속이나 변수별 분해가 가능한 유일한 무료 도구 |
| 최적화 순서 | AI 게이팅 → 양자화 → 경량 백본 → 거리 차등(효과 큰 순) |

## ⚠ 비판·리스크

**심각도: 높음**

- **R1 베이스라인 없이 최적화 계획만 존재**: M6 실측 없이 "-40% 목표"를 세운 상태다. 실제 병목이 AI 위치인지, 캐릭터 백본인지, 요리 진행도인지 불명확하다. 최적화 순서(게이팅→양자화→백본)가 효과 검증 없이 정해졌다. Steam self-connect 불가로 2인 측정이 구조적으로 어렵다는 점이 이 문제의 근본 원인이다.
- **R2 AI가 플레이어 풀 백본 상속 여부 미확인**: 실행계획에 "AI가 플레이어 풀 백본을 상속"한다고 기재되어 있으나, 코드에서 AI 관련 NetworkBehaviour 파일을 실측하지 않았다. AI 20기가 플레이어와 동일한 백본으로 동작한다면 M6 병목이 예상보다 훨씬 클 수 있다.

**심각도: 보통**

- **R3 거리 기반 갱신 차등의 NGO 구조적 한계**: NetworkVariable은 클라이언트별 차등 송신이 불가능하다(단일 델타 브로드캐스트). 거리 기반 AoI는 `NGO CheckObjectVisibility`(오브젝트 단위 on/off) 또는 완전 커스텀 RPC 설계가 필요하다. 실행계획 R5에서 이미 강등됐으나 ①~③ 효과 미달 시 다시 검토해야 한다.
- **R4 UnreliableSequenced→Reliable 승격의 대역폭 영향**: Step 1 P1-2에서 NGO UnreliableSequenced를 Steam Reliable로 승격했다. 위치·애니메이터 블렌드가 이 채널을 사용하면 재전송·순서 버퍼 오버헤드가 M6에 포함된다. 승격 전·후 대역폭 비교가 없다.
- **R5 SCN-06 절차 미집행**: AI 20기 활성 5분 부하 테스트(SCN-06)가 2인 기기 없이 실행 불가. 최악 시나리오 대역폭이 확인된 적 없다.

**권고**: 2인 실기기 측정 1회를 최우선 계획에 넣어라. M6 베이스라인 없이 Step 4를 착수하면 어느 최적화가 얼마를 벌었는지 입증할 수 없다. AI NetworkBehaviour 파일(`Assets/Scripts/Character/` 외 AI 전용 스크립트)을 점검해 실제 백본 규모를 확인하라.

## 관련 문서

- [[state-sync|상태-동기화]]
- [[transport-layer|transport-레이어]]
- [[prediction-reconciliation|예측-재조정-보간]]

---
← [[03-network-hub|03 · 네트워크 아키텍처]] · [[index|인덱스]]
