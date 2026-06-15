# 게이트 결정 로그 — 2026-06-13_netcode2

> **이 문서는?** 진행 중 사람이 내린 결정(승인·선택·반려)의 기록부입니다. "왜 이렇게 만들었지?"를 나중에 추적하는 근거가 됩니다.
> 게이트(G1·G2·G5·G7·G8)마다 사용자 결정을 기록(신규 에셋 0이라 G3 생략). **헤더에 `<a id>` 앵커 + 게이트 ID** 포함(다른 문서가 `[G2](decisions.md#G2)` 로 점프).
> Stop 훅이 `awaiting_gate` 와 이 파일의 게이트 ID 를 대조한다.

<!-- 형식:
## G2 (2026-06-11 14:30) — 승인
- 결정: ...
- 사유: ...
- 후속: ...
-->

## <a id="G1"></a>G1 (2026-06-13 12:45) — 승인 (기획 해석 확인)

Step 1(전송 안정화) target T1~T10 작성. 직전 `2026-06-12_netcode`(Step 0) 사인오프 후 다음 단계.

- **결정 1 — 범위 = Step 1 단독**. P0-1·P0-2·P0-4·P1-2·P1-1·P2-8(최소). 데모 게이트 1차 단위.
  Step 2~5는 후속 `/cycle-start`.
- **결정 2 — 측정 자동화 경계 = 근사 더 적극 시도**. 단일 에디터에서 컴파일·플레이 스모크·StartHost
  뿐 아니라 **M2 경계값(BoundaryEchoHarness)·M3 끊김(kill/Shutdown) 등을 에디터 내에서 최대한 밀어붙임**.
  실기기 2인 P2P 대비 오차는 증거 문서에 명시. (Step 0 G1-Q2의 "근사" 방침을 더 적극화.)
- **전제(③ scope에서 확정)**: Step1_Evidence.md "코드 완료 2026-06-12, 측정 대기" → T1~T6 기구현 추정.
  본 사이클 목적은 **기구현 재검증 + 근사 측정 집행**으로 재정의될 가능성(G2에서 확정).

## <a id="G2"></a>G2 (2026-06-13 13:05) — 승인 (범위·영향도)

scope 스캔: Step 1 코드 전부 기구현(transport 마커 11곳 + WorldItemSpawner 가드), 직전 Step 0 PROF/채널화와 정합 공존. **신규 구현 0.**

- **결정 1 — 사이클 목적 확정**: 신규 구현 0 + 기구현 재검증(compile·플레이 스모크) + **근사 측정 M1·M3·M8 (host-only, exec StartHost/Shutdown 구동)**. 증빙은 `evidence/`.
- **결정 2 — MPPM 2피어 1회 실증 시도**: Multiplayer Play Mode 2.0.1로 host↔client 접속 시도.
  성공 시 M2/M3/M8 일부 자동화, 실패 시 **Steam 단일계정 self-connect 차단을 실측으로 확정**하고 2인 수동 인계.
- **측정 한계**: M2 경계값(BoundaryEchoHarness L70 "호스트 루프백 무의미")·정량 M8 10/10·SCN-07 30분 soak = 2인 실기기 필수 → Step1_Evidence.md 수동 인계 명시.
- 신규/변경 Unity 에셋 없음 → G3 네이밍 계약 불요(④에 산출물=evidence/+Step1_Evidence.md 기록).

## <a id="G5"></a>G5 (2026-06-13 13:15) — 승인 (테스트 환경 적용)

- **씬·에셋 편집 없음** — 플레이 진입만, 계측 자동 부착. 적용 위험 0.
- **결정 — 측정 절차 1~5 진행**: ①정합 검증 ②M1 RTT(loopback=0) ③M3 끊김(Shutdown→events.csv Disconnect, Connect 오발화 0) ④M8 재호스팅(StartHost↔Shutdown ×3~5, Steam 생존) ⑤MPPM 2피어 1회 실증. 증빙 evidence/, 권위 Step1_Evidence.md.
- 수동 인계: M2 경계·정량 M8 10/10·SCN-07 30분 soak.

## <a id="G7"></a>G7 (2026-06-13 13:35) — 사인오프 (최종)

Step 1 = 신규 구현 0, 기구현 재검증 + 단일 에디터 근사 측정 완료.

- **재검증**: compile 0(Step 0 PROF/채널화와 공존), `[NetDiagnostics]` 7종 부착, transport 마커 11곳 보존.
- **측정(host-only)**: M1 RTT=0ms(loopback 정상) / M3 Shutdown→OnClientDisconnectCallback 정상·Connect 오발화 0(P0-2) / M8 재호스팅 ×3 SteamClient 생존·에러 0(P1-1).
- **MPPM 2피어 실증**: CLI 구동 API 없음(GUI 전용) + Steam 단일계정 self-connect 차단 → 2피어 불가 확정.
- **인계(2인 실기기)**: M2 경계값·정량 M8 10/10·SCN-07 30분 soak(데모 게이트 1차). → Step1_Evidence.md.
- 검증: 링크 broken=0, 최종 스모크 회귀 0. 증빙 `evidence/Step1_smoke_20260613.md`.
- 판정: ◑ **부분 통과** — 코드 정합·host-only 측정 합격. 데모 게이트는 2인 측정 후 최종.
- 후속: Step 2(권위 일원화) `/cycle-start`.

## <a id="G8"></a>G8 (2026-06-13 23:55) — 미질의 (소급 기록)

> 본 사이클은 하네스 v4(9단계화) 개선 **이전**에 G7 사인오프로 종료됐다. ⑨ next·G8 은 소급 신설이라
> "다음 사이클 즉시 진행" 질의가 이뤄지지 않았다.

- **상태**: 다음 사이클 미착수. [[2026-06-13_netcode2/09_next|09_next]] 에 차기 후보 N1~N3 구조화 완료.
- **권장**: N1(Step 2 권위 일원화 — 단일 에디터 검증 착수 가능) 또는 N2(데모 게이트 1차 측정 — 2인 환경 선행).
- **후속 운영**: 다음 `/cycle-start` 는 사용자 판단으로 시작. 이후 사이클부터 ⑨ next 끝에서 G8 질의 정상 작동.

---
## 🔗 관련 문서 (Foam)
- 단계별 게이트: [[2026-06-13_netcode2/01_target|①]] (G1) · [[2026-06-13_netcode2/03_scope|③]] (G2) · [[2026-06-13_netcode2/06_test_env|⑥]] (G5) · [[2026-06-13_netcode2/08_result|⑧]] (G7) · [[2026-06-13_netcode2/09_next|⑨]] (G8)
- 용어: [[_glossary|용어 사전]]
