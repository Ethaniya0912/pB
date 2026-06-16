---
title: 03-network-hub
tags: [moc, network]
status: done
source:
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Assets/Scripts/Networking/SteamClient.cs
  - Assets/Scripts/Character/CharacterNetworkManager.cs
  - Reports/netcode/코옵_Netcode_실행계획_v1.1.md
verified: 2026-06-15
---

# 03 · 네트워크 아키텍처

pB 멀티플레이는 NGO(Netcode for GameObjects) 2.7.0 + Facepunch Steamworks P2P 릴레이를 조합한 호스트-클라이언트 P2P 구조다. 전용 서버 없음. 코옵 PvE 전용(2~N인). 36개 이슈 수정 계획이 진행 중(Step 0·1 완료, Step 2~5 미착수).

## 영역 문서

- [[network-topology|네트워크-토폴로지]] — Steam relay P2P, 전용 서버 미보유, 호스트 이탈=세션 종료
- [[authority-model|권한-모델]] — 호스트(서버) 권위, 친선 코옵 일관성 목표, 치팅 방지 비목표
- [[netcode-solution|netcode-솔루션]] — NGO 2.7.0 + 커스텀 SteamP2PRelayTransport 선정 근거
- [[transport-layer|transport-레이어]] — Facepunch P2P relay, CastToSendType, 동적 버퍼, PROF 지연주입
- [[state-sync|상태-동기화]] — NetworkVariable + RPC, StateChecksumV0 desync 감지
- [[prediction-reconciliation|예측-재조정-보간]] — 예측·재조정 미구현, 원격 캐릭터 보간만 존재
- [[lag-compensation|랙-보상]] — 히트 판정 lag compensation 미구현
- [[bandwidth-budget|대역폭-예산]] — 실측 미집행(2인 P2P 필요), 최적화 계획만 존재

## 진행 상태 요약 (2026-06-15)

| 단계 | 내용 | 상태 |
|---|---|---|
| Step 0 | 계측 기반(RNSM·VerdictLogger·StateChecksumV0·PROF 프리셋) | 완료(2026-06-12) |
| Step 1 | 전송 안정화(P0-1/2/4·P1-1/2·P2-8) | 코드 완료(2026-06-12) / 2인 측정 대기 |
| Step 2 | 권위 일원화(P0-3·P0-5+P1-10·P1-3/4/9/11) | 미착수 |
| Step 3 | 규약 표준화(P1-7/12·P2-5/6/10 등) | 미착수 |
| Step 4 | 효율화(P2-1/2/3/9·P1-5) | 미착수 |
| Step 5 | 검증 고도화(P2-7/11/12/8+자동화) | 미착수 |

---
← [[index|인덱스]]
