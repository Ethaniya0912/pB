# 게이트 결정 로그 — 2026-06-12_netcode

> **이 문서는?** 진행 중 사람이 내린 결정(승인·선택·반려)의 기록부입니다. "왜 이렇게 만들었지?"를 나중에 추적하는 근거가 됩니다.
> 게이트(G1·G2·G3·G5·G7)마다 사용자 결정을 기록. 헤더에 게이트 ID 를 반드시 포함.
> Stop 훅이 `awaiting_gate` 와 이 파일의 게이트 ID 를 대조한다.

<!-- 형식:
## G2 (2026-06-11 14:30) — 승인
- 결정: ...
- 사유: ...
- 후속: ...
-->

## <a id="G1"></a>G1 (2026-06-12 18:15) — 승인 (기획 해석 확인)

target 매트릭스 T1~T41(6 Step + 횡단 관심사) 작성 완료. 4개 구조적 결정 확정:

- **Q1 사이클 범위 = Step 0만**. 이번 사이클은 계측 기반 구축(T4~T11)으로 한정.
  Step 1~5는 각각 후속 `/cycle-start` 로 분리. (사유: 계획 §7 "어떤 수정도
  베이스라인 없이 착수 금지" — Step 0 산출물이 이후 모든 게이트의 근거.)
- **Q2 자동화 경계 = 가능한 만큼 단일 에디터로 근사**. NGO Network Simulator +
  에디터 내 다중 인스턴스(호스트/클라)로 측정까지 최대한 자동화 시도. 단,
  실기기 Steam P2P 2자 측정과는 **오차 존재를 증거 문서에 명시**한다.
- **Q3 기존 자산 = 우선 재사용·확장**. `Assets/Scripts/Utilities/NetDiagnostics/`
  의 기존 NetEventLogger.cs 등을 ③ scope에서 read-only 스캔해 기구현/미흡 판정 후
  부족분만 확장. 중복 신규 작성 금지.
- **Q5 PROF 도구 = NGO Network Simulator** (Multiplayer Tools 패키지 내장).
  Clumsy 외부 도구 미채택(자동화·재현성 우위).

미결(해당 Step 착수 시 결정, Step 0 범위 밖): Q4 (a)가방 권위 방향 (b)AI 거리차등 설계
(c)아이템 보간식 (d)Facepunch RTT API명.

## <a id="G2"></a>G2 (2026-06-12 18:30) — 승인 (범위·영향도) ⚠ 전제 변경

존재성 스캔 결과 **Step 0 산출물 대부분이 선행 작업(2026-06-12 16:43)으로 기구현**.
Step 1 코드까지 완료("코드 완료, 측정 대기"). → 사이클 목적 재정의.

- **기구현(검증만)**: NetDiagnostics 코어·Bootstrap·NetEventLogger(M3·M8)·VerdictLogger
  (M5, 전투 3파일 호출부 이식 완료)·StateChecksumV0(M11)·SoakHarness(F10)·
  BoundaryEchoHarness(F9·M2) / Multiplayer Tools 2.2.3 / SCN_Procedures.md·
  Step0_Baseline.md·Step1_Evidence.md(양식) / (보너스)Step1 코드 전체.
- **결정 1 — 사이클 목적 = 잔여 갭 마감 + 검증**. 미흡/신규 3건만 구현하고,
  기구현 7종은 컴파일 0 + 단일 에디터 플레이 스모크로 검증. 측정은 수동 분리.
- **결정 2 — PROF 구현 = 코드 프리셋 + 런타임 토글**(SimulatorConfiguration .asset 미채택).

### 잔여 갭 3건 (본 사이클 구현 대상)
1. RNSM HUD 배치 (Bootstrap에 미포함 — 패키지만 존재)
2. PROF-G/A/B 코드 프리셋 + 런타임 토글
3. P2-4 수명주기 Debug.Log 9건 `#if NETCODE_DEBUG` 채널화

### ⑤ spec에서 검토할 기술 리스크 (G6 가능)
- **MP Tools Network Simulator는 UnityTransport 전용** — 본 프로젝트는 커스텀
  `SteamP2PRelayTransport`라 표준 Network Simulator 파이프라인이 적용 안 됨.
  "코드 프리셋 + 런타임 토글"은 **transport 수신 경로 내 인공 지연/손실 주입**으로
  구현(P2-4와 같은 계측-인접 수정). spec에서 구체화하고 필요 시 G6로 재확인.

## <a id="G3"></a>G3 (2026-06-12 18:40) — 승인 (파일·경로명 확정)

assets 5건 확정: A1 `NetDiagnostics/RnsmHud.cs`(new), A2 `NetDiagnostics/NetSimProfiles.cs`(new),
A3 `NetDiagnosticsBootstrap.cs`(modify·append), A4 `Networking/SteamP2PRelayTransport.cs`(modify),
A5 `Reports/Step0_Baseline.md`(modify).

- **결정 1 — PROF 시뮬레이터: 지연/지터만 주입, 손실(loss) 제외.** 커스텀 Steam transport의
  OnMessage는 Steam 신뢰성 계층 이후라 손실 주입 시 reliable 메시지 영구 유실 → NGO 파손.
  PROF-A/B의 손실률(2~5%)은 OS레벨 Clumsy로 측정 시 보완하며, 이를 Step0_Baseline.md·
  SCN 절차에 명시한다. (계획 §2도 Clumsy를 대안으로 명시.) → G6 불요, 본 결정으로 갈음.
- **결정 2 — PROF 토글 키 = F8** (F9=경계 스윕, F10=soak와 충돌 없음).

## <a id="G5"></a>G5 (2026-06-12 18:50) — 승인 (테스트 환경 적용)

- **씬·프리팹 편집 없음** — 계측은 RuntimeInitializeOnLoadMethod 자동 부착. 코드 4파일(.cs)만 변경.
- **결정 — 검증 경계 승인**: 자동(구현 루프) = compile 0 + `editor play --wait` 진입 스모크
  (`[NetDiagnostics]` 6종 부착) + console error 0. 수동(분리) = RNSM RTT 실추종·NetSim 체감·
  M1~M11 실측·F9/F10 하니스(StartHost+2인/Steam 필요) → Step0_Baseline.md.
- 플레이 스모크는 활성 씬(Scene_main_menu_01)에서 진입(부트스트랩 씬 무관).

## <a id="G7"></a>G7 (2026-06-12 19:20) — 사인오프 (최종)

사용자 요청으로 단일 에디터 베이스라인 측정까지 시도 후 사인오프.

- **구현 완료**: 잔여 갭 3건 — RnsmHud.cs(new)·NetSimProfiles.cs(new)·SteamP2PRelayTransport.cs(P2-4 채널화+지연/지터 주입)·NetDiagnosticsBootstrap.cs(append). 모두 compile 0.
- **검증 완료(단일 에디터)**: 플레이 진입 [NetDiagnostics] 7 MonoBehaviour 부착 / console 계측 에러 0 / StartHost 성공 / **M1 RTT=0ms 확정**(loopback) / NetSim 토글 라이브(OFF→G→A, Enabled) / RNSM Visible+Config.
- **수동 인계(미집행·자동화 불가)**: M2/3/5/6/7/8/10/11 + NetSim 주입 지연 = 2인/Steam 필요. Step0_Baseline.md 양식·M1 기입 완료 상태로 분리. PROF 손실은 Clumsy.
- **후속**: Step 1(코드 기구현, 측정 대기) → Step 2~5 각 /cycle-start.
- 판정: ✅ Step 0 잔여 갭 마감+검증 완료. 베이스라인 전량 실측은 후속 수동.

## <a id="G8"></a>G8 (2026-06-12 23:50) — 미질의 (소급 기록)

> 본 사이클은 하네스 v4(9단계화) 개선 **이전**에 G7 사인오프로 종료됐다. ⑨ next·G8 은 소급 신설이라
> "다음 사이클 즉시 진행" 질의가 이뤄지지 않았다.

- **상태**: 다음 사이클 미착수. [[2026-06-12_netcode/09_next|09_next]] 에 차기 후보 N1~N4 구조화 완료.
- **권장**: N2(Step 1 검증·측정, 단일 에디터 착수 가능) 또는 N1(베이스라인 실측, 2인 환경 선행).
- **후속 운영**: 다음 `/cycle-start` 는 사용자 판단으로 시작. 이후 사이클부터 ⑨ next 끝에서 G8 질의가 정상 작동.

---
## 🔗 관련 문서 (Foam)
- 단계별 게이트: [[2026-06-12_netcode/01_target|01_target]] (G1) · [[2026-06-12_netcode/03_scope|03_scope]] (G2) · [[2026-06-12_netcode/04_assets|04_assets]] (G3) · [[2026-06-12_netcode/06_test_env|06_test_env]] (G5) · [[2026-06-12_netcode/08_result|08_result]] (G7) · [[2026-06-12_netcode/09_next|09_next]] (G8)
- 명세 참조: [[2026-06-12_netcode/05_spec/A3_A4_Transport_and_Bootstrap_mods|A3_A4_Transport_and_Bootstrap_mods]] (G3 손실/F8)
- 증거: [[Step0_Baseline]] · 재검증 [retrofit_smoke_20260612.md](evidence/retrofit_smoke_20260612.md)
- 용어: [[PROF-프리셋]] · [[Clumsy]] · [[NetSimProfiles]] · [[Multiplayer-Tools]] → [[_glossary|용어 사전]]
