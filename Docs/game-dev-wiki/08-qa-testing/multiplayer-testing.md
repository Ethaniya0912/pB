---
title: 멀티플레이-테스트
tags: [qa, network]
status: done
source:
  - .harness/cycles/2026-06-13_netcode2/
  - .harness/cycles/2026-06-13_netcode2/08_result.md
  - .harness/cycles/2026-06-13_netcode2/06_test_env.md
  - Reports/netcode/SCN_Procedures.md
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Assets/Scripts/Utilities/NetDiagnostics/
verified: 2026-06-15
---

# 멀티플레이-테스트

Multiplayer Play Mode 2.0.1 설치됨. 단, Steam 단일계정 self-connect 차단으로 에디터 내 2피어 실측이 불가 — 실기기 2대가 필수다.

## 현황 (pB)

**Multiplayer Play Mode(MPPM)**

- Unity 6000.3.1f1에 MPPM 2.0.1 설치됨(`manifest.json` 내 `com.unity.multiplayer.tools: 2.2.3` 포함).
- 2026-06-13 사이클(`2026-06-13_netcode2`) 실증 결과: MPPM Virtual Player 2번째 피어 StartClient 시 **Steam self-connect 차단** — 단일 계정·단일 머신에서 호스트-클라이언트 동시 접속 불가 확정.
- 에디터 내 loopback(host-only) 측정 가능 범위: M1 RTT(0ms 확인), M3 끊김 이벤트 정합, M8 재호스팅 ×3 생존.

**2인 실기기 필수 측정**

- M2 경계값(BoundaryEchoHarness F9, 원격 클라 머신), M8 정량 10/10, SCN-02 강제끊김 ×5, SCN-07 30분 soak — 전부 2인 실기기 대기 상태.

**계측 인프라**

- `[NetDiagnostics]` 오브젝트(RuntimeInitializeOnLoadMethod 자동 부착): NetEventLogger, VerdictLogger, StateChecksumV0, BoundaryEchoHarness, SoakHarness, NetSimProfiles, RnsmHud 7종.
- 출력: `events.csv`, `verdicts.csv`, `checksum.csv`, `echo.csv`, `soak_samples.csv`.
- SCN 절차서: `Reports/netcode/SCN_Procedures.md` (SCN-01~07 전 단계 기술).
- 네트워크 조건 시뮬레이션: Clumsy 사용(UDP 전체). PROF-G(Lag 15ms), PROF-A(Lag 75ms, Drop 2%, Jitter ±15ms), PROF-B(Lag 125ms, Drop 5%, Jitter ±30ms) 정의됨.
- RNSM 컴포넌트: Multiplayer Tools 설치로 Unity Profiler Network 모듈 사용 가능. RTT 현재 0 고정(P0-4 이슈, Step 2 교정 예정).

**인벤토리 P2P 제약**

- 현재 클라이언트의 바닥 아이템 줍기는 호스트만 동작(P0-5) — SCN-04 베이스라인 예상 0/20 성공.
- `Assets/Scripts/Inventory/WorldItemSpawner.cs`에 P2-8 가드 적용됨.

## 설계·결정

- 교통 계층: `SteamP2PRelayTransport`(`Assets/Scripts/Networking/SteamP2PRelayTransport.cs`) — NGO 위에 Facepunch.Steamworks P2P 중계.
- 호스트 권위(host authority): 전투 판정, 아이템 스폰, AI 등 서버(호스트) 측에서 결정.
- 측정 체계: Step 0~5 단계별 SCN 매트릭스(`SCN_Procedures.md` 부록 A). Step 1 코드(전송 안정화) 검증 완료, Step 2 이후 진행 중.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | 2인 실측 미완료 | M2·M8 정량·SCN-07 soak 모두 2인 대기 — 현재 host-only 근사치만 존재 | 2인 실기기(혹은 Steam 2계정) 세션 조속 실시 |
| 높음 | Steam self-connect 차단으로 에디터 내 자동화 불가 | 2026-06-13 실증 | 2인 머신 없이 멀티플레이 자동 회귀 테스트 방법 없음 |
| 중간 | RTT 실측값 0 고정(P0-4) | `GetCurrentRtt(0)` = 0(loopback). 실 클라이언트 RTT 미측정 | Step 2에서 RNSM RTT 추종 교정 |
| 중간 | soak 테스트(데모 게이트 SCN-07) 미집행 | 30분 무중단 자유플레이 체크섬 불일치 0 기준 — 미달성 | 2인 세션 확보 후 최우선 실행 |
| 낮음 | 봇 부하 테스트 계획 없음 | SCN-06 AI 20기 hold-out 수동 절차만 있음 | 자동 봇 클라이언트 계획 수립 |

## 관련 문서

- [[performance-budget|성능-예산]]
- [[server-hosting|서버-호스팅]]
- [[anti-cheat|안티치트]]
- [[test-framework|테스트-프레임워크]]

---
← [[08-qa-testing-hub|08 · 테스트 & 품질]] · [[index|인덱스]]
