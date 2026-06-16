---
title: 권고 액션 아이템
tags: [issues, recommendation]
status: done
verified: 2026-06-16
---

# 권고 액션 아이템

[[risk-register|리스크 레지스터]]의 133개 이슈별 권고(중복 통합)를 **영역별 체크리스트**로 모은 것. 레지스터 R-항목 1개 = 체크박스 1개. 영역 내 심각도 높음 순. 체크박스로 진행 추적.

### 개요
- [ ] [R-004] 5인 상향 + ScriptableObject/설정 외부화, 5인 부하(로비·대역폭·스폰) 측정 ([[project-overview\|프로젝트-개요]], [[00-overview-hub\|00-overview-hub]], [[decision-priority\|의사결정-우선순위]], [[lobby-matchmaking\|로비-매치메이킹]])
- [ ] [R-012] 최소 사양 약속 전 StandaloneWindows64 빌드 실기기 프로파일(GPU 16ms·Memory Profiler) 필수, MobRegressionRunner 재실행 ([[project-overview\|프로젝트-개요]], [[prep-checklist\|사전준비-체크리스트]], [[decision-priority\|의사결정-우선순위]], [[performance-budget\|성능-예산]])
- [ ] [R-053] 정식명 확정 후 식별자·스토어명 정리 ([[project-overview\|프로젝트-개요]])
- [ ] [R-054] 2인 테스트 절차서 작성 ([[decision-priority\|의사결정-우선순위]])
- [ ] [R-104] 앞 결정 고정 후 문서화 ([[decision-priority\|의사결정-우선순위]])
- [ ] [R-105] — (허브 문서가 링크 목록만, 내용이 각 문서에 분산) ([[00-overview-hub\|00-overview-hub]])

### 기반
- [ ] [R-002] 즉시 Unity LFS 패턴 추가 + `git lfs migrate import`로 공유·빌드 재현 보장 ([[prep-checklist\|사전준비-체크리스트]], [[decision-priority\|의사결정-우선순위]], [[version-control-git-lfs\|버전관리-git-lfs]], [[01-foundation-hub\|01-foundation-hub]])
- [ ] [R-003] asmdef 분리(Interfaces 최하단으로 순환 차단, 클라/서버·에디터 경계 선언, internal 접근 확보) ([[prep-checklist\|사전준비-체크리스트]], [[01-foundation-hub\|01-foundation-hub]], [[project-structure\|프로젝트-구조]], [[assembly-definition\|assembly-definition]])
- [ ] [R-048] Roslyn Analyzer/Code Style로 규칙 강제 + 전 스크립트 네임스페이스 부여 ([[coding-conventions\|코딩-컨벤션]], [[01-foundation-hub\|01-foundation-hub]])
- [ ] [R-049] 빌드 포함 여부 확인·제외 정리 ([[project-structure\|프로젝트-구조]], [[project-overview\|프로젝트-개요]])
- [ ] [R-055] 비동기 표준 규약 문서화 ([[coding-conventions\|코딩-컨벤션]])
- [ ] [R-056] 실행 순서 범위 기준 문서화 ([[coding-conventions\|코딩-컨벤션]])
- [ ] [R-057] 완성 후 기능별 재구조화 ([[project-structure\|프로젝트-구조]], [[ai-bridge-architecture\|ai-bridge-아키텍처]])
- [ ] [R-058] 폴더명 정정(.meta 재생성, `CaveSystem.Multiplayer` using 확인) ([[project-overview\|프로젝트-개요]], [[project-structure\|프로젝트-구조]], [[coding-conventions\|코딩-컨벤션]])
- [ ] [R-106] 로딩 영향 최소화/정리 (`_Recovery/` 복구 씬 29개+) ([[project-structure\|프로젝트-구조]])
- [ ] [R-107] `Bridges&Interfaces`/`Bridges_Interfaces` 중복 폴더 목적 확인·통합 ([[project-structure\|프로젝트-구조]])
- [ ] [R-108] `com.unity.collab-proxy`(UVC) 실제 사용 여부 확인 ([[version-control-git-lfs\|버전관리-git-lfs]])

### 아키텍처
- [ ] [R-010] URP APV 마이그레이션/교체 시점 EA 전 평가(SSGI 비공식 의존) ([[render-pipeline\|렌더-파이프라인]], [[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]])
- [ ] [R-011] 저사양 GPU(GTX 1060급) EA 전 SSGI On/Off 비용 검증 ([[render-pipeline\|렌더-파이프라인]], [[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]])
- [ ] [R-013] 씬당 AI 상한·성능 예산 EA 전 측정, NetworkObject 수 한계 고려한 규모 설계 ([[ecs-vs-oop\|ecs-vs-oop]])
- [ ] [R-014] 책임 분할 + DI 점진 도입으로 테스트 가능 구조 전환(God-Manager·싱글톤 28개+) ([[ecs-vs-oop\|ecs-vs-oop]], [[di-container\|di-컨테이너]])
- [ ] [R-059] GPUDrivenShadowManager `OnDisable` 조건부 해제 + 중복 초기화 방지 가드 ([[render-pipeline\|렌더-파이프라인]], [[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]])
- [ ] [R-060] ShaderCoordinationManager `[ExecuteInEditMode]` 에디터 변수 오염 여부 확인 ([[render-pipeline\|렌더-파이프라인]])
- [ ] [R-061] BiomeSyncMode 원자성 통합 테스트 추가 ([[render-pipeline\|렌더-파이프라인]])
- [ ] [R-062] SteamAudio HRTF 오디오 스레드 점유율 측정 ([[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]], [[render-pipeline\|렌더-파이프라인]])
- [ ] [R-063] EA 이전 ECS 전환 비현실적 — 현 구조 유지 ([[ecs-vs-oop\|ecs-vs-oop]])
- [ ] [R-064] `FindFirstObjectByType`를 Awake/Start 캐시 또는 참조 주입으로 대체 ([[di-container\|di-컨테이너]])
- [ ] [R-065] `DontDestroyOnLoad` 정리 책임 일원화/검증 ([[di-container\|di-컨테이너]])
- [ ] [R-066] AI 현황 코드 실측 기반 보강 + Bridge 등록 순서·폴백 동작 문서화 ([[ai-bridge-architecture\|ai-bridge-아키텍처]])
- [ ] [R-067] 동기화 Trust/Trauma 대역폭 영향을 bandwidth-budget 실측에 포함 ([[ai-bridge-architecture\|ai-bridge-아키텍처]])
- [ ] [R-109] 엔티티 증가 시 SteamAudio HRTF 오디오 비용 측정 ([[render-pipeline\|렌더-파이프라인]])
- [ ] [R-110] Cave GPGPU Job 경계 코드 리뷰(Job 내 managed 접근 금지) ([[ecs-vs-oop\|ecs-vs-oop]])
- [ ] [R-111] VContainer는 신규 시스템부터 점진 적용 ([[di-container\|di-컨테이너]])

### 네트워크
- [ ] [R-007] Step 3 P2-6 ConnectionApproval 구현(정원·버전 거절) ([[network-topology\|네트워크-토폴로지]], [[lobby-matchmaking\|로비-매치메이킹]])
- [ ] [R-008] Step 3 P2-6 재합류+상태 복원 구현(재접속·난입) ([[network-topology\|네트워크-토폴로지]], [[lobby-matchmaking\|로비-매치메이킹]])
- [ ] [R-015] RTT 150ms+ 체감 측정 후 예측·재조정 도입 판단(NGO Prediction/Rollback 미사용) ([[netcode-solution\|netcode-솔루션]], [[prediction-reconciliation\|예측-재조정-보간]])
- [ ] [R-016] Step 5 asmdef 분리를 앞당겨 릴리즈에서 계측 완전 제거 ([[netcode-solution\|netcode-솔루션]], [[transport-layer\|transport-레이어]])
- [ ] [R-017] LateUpdate try-catch를 null 검사로 교체해 오류 가시화 ([[transport-layer\|transport-레이어]])
- [ ] [R-018] M6 승격 전후 대역폭 비교 측정(Unreliable→Reliable) ([[transport-layer\|transport-레이어]], [[adr-0001-netcode\|adr-0001-netcode-선정]], [[bandwidth-budget\|대역폭-예산]])
- [ ] [R-019] Step 2: 방어/패링을 피격자 Owner 위임으로 재설계 (P0-3) ([[authority-model\|권한-모델]])
- [ ] [R-020] Step 2 권위 일원화로 클라 줍기 서버 라우팅 해결 (P0-5) ([[authority-model\|권한-모델]])
- [ ] [R-021] Door NetworkVariable 승격 등으로 문·요리·QTE·잡기 동기화 (P2-3/5/9/11) ([[state-sync\|상태-동기화]])
- [ ] [R-022] EA 전 불일치 시 재동기화/경고 HUD 추가(StateChecksumV0 복구) ([[state-sync\|상태-동기화]])
- [ ] [R-023] `RpcParams.Receive.SenderClientId`로 일괄 교체 (P1-7) ([[state-sync\|상태-동기화]])
- [ ] [R-024] M5 실측(PROF-A) 후 랙보상 방향 결정 ([[lag-compensation\|랙-보상]])
- [ ] [R-025] Step 4 `1-exp(-k·dt)` 지수 감쇠 보정으로 교체 (P2-2) ([[prediction-reconciliation\|예측-재조정-보간]], [[state-sync\|상태-동기화]])
- [ ] [R-026] AI 전용 스크립트 점검해 백본 규모 확인(M6 병목) ([[bandwidth-budget\|대역폭-예산]])
- [ ] [R-068] Step 4 대역폭 최적화(T29) 시 M6 승격 전후 비교 ([[adr-0001-netcode\|adr-0001-netcode-선정]], [[bandwidth-budget\|대역폭-예산]])
- [ ] [R-069] NGO 업그레이드 정책 결정(`[ServerRpc]`↔신문법 혼재) ([[netcode-solution\|netcode-솔루션]])
- [ ] [R-070] 예외 가시화 + Step 2 RNSM RTT 추종 교정(GetCurrentRtt 0) ([[transport-layer\|transport-레이어]], [[multiplayer-testing\|멀티플레이-테스트]])
- [ ] [R-071] Send 1대多 순회 구조 개선(친선 코옵 범위는 무시 가능) ([[transport-layer\|transport-레이어]])
- [ ] [R-072] Steam relay 단일 경로 가용성 의존 인지(실용 위험 낮음) ([[transport-layer\|transport-레이어]])
- [ ] [R-073] Owner Write 위치의 버그성 왜곡 대비 서버 검증 ([[authority-model\|권한-모델]])
- [ ] [R-074] Step 2 사망 권위 일원화(이중 처리 P1-3) ([[authority-model\|권한-모델]])
- [ ] [R-075] 서버 `IsSpaceAvailable` 재검증 추가(인벤 조작 P1-4, M11 보강) ([[authority-model\|권한-모델]])
- [ ] [R-076] NetworkVariable 전송 게이팅 검증/적용(정지 시 매 틱 기록) ([[state-sync\|상태-동기화]])
- [ ] [R-077] Step 5 StateChecksumV0 해시 범위 확장(문·요리·잡기·QTE) ([[state-sync\|상태-동기화]])
- [ ] [R-078] 공정성↔응답성 트레이드오프 2인 실측(M5 베이스라인) ([[lag-compensation\|랙-보상]])
- [ ] [R-079] `CheckObjectVisibility`/커스텀 RPC 검토(NGO 클라별 차등 한계) ([[bandwidth-budget\|대역폭-예산]])
- [ ] [R-080] NGO 내장 보간과 Update 보간 이중 동작 씬 설정 점검 ([[prediction-reconciliation\|예측-재조정-보간]])
- [ ] [R-081] `CharacterNetworkManager.cs` "Client-Side Prediction" 주석 교정 ([[prediction-reconciliation\|예측-재조정-보간]])
- [ ] [R-112] PvE 랙보상 필요성 M5 실측 후 판단(선제 구현 지양) ([[lag-compensation\|랙-보상]])
- [ ] [R-113] Step 4 P2-2 스냅 임계 거리 검사 적용(SmoothDamp 재시작) ([[prediction-reconciliation\|예측-재조정-보간]])

### Steam
- [ ] [R-027] Facepunch DLL 교체일 기록·정기 갱신(보안 패치 반영) ([[steamworks-integration\|steamworks-통합]])
- [ ] [R-028] `SteamRemoteStorage`/자동 클라우드 연결 + 경로 매핑·충돌 해소·할당량 계획 수립 ([[steam-cloud\|steam-cloud]], [[save-load\|세이브-로드]], [[steamworks-admin\|steamworks-행정]])
- [ ] [R-029] EA 전 최소 5개 도전과제 정의·검증 ([[achievements-stats\|도전과제-통계]])
- [ ] [R-030] 파트너 계정·AppID 선행 후 GitHub Actions + `steamcmd +run_app_build` 자동화(시크릿 관리) ([[steam-build-pipeline\|steam-빌드-파이프라인]], [[steamworks-admin\|steamworks-행정]], [[build-automation\|빌드-자동화]], [[ci-cd\|ci-cd]])
- [ ] [R-082] 오프라인/비-Steam 폴백 구현(조용한 실패 방지) ([[steamworks-integration\|steamworks-통합]])
- [ ] [R-083] 1.5초 하드코딩 대기를 소켓 해제 완료 이벤트 기반으로 교체 ([[lobby-matchmaking\|로비-매치메이킹]])
- [ ] [R-084] 친구 초대 버튼의 Steam 초대 API 연결 검증 ([[lobby-matchmaking\|로비-매치메이킹]])
- [ ] [R-085] 종료·씬 전환 시 `StoreStats()` 호출 보장 ([[achievements-stats\|도전과제-통계]])
- [ ] [R-086] QA/공개 빌드 분리 브랜치 구성(default/beta/internal) ([[steam-build-pipeline\|steam-빌드-파이프라인]])
- [ ] [R-130] Win32/Posix DLL Inspector 플랫폼 설정 확인 ([[steamworks-integration\|steamworks-통합]])
- [ ] [R-131] `InitRelayNetworkAccess()` 완료 콜백 대기 로직 추가 ([[steamworks-integration\|steamworks-통합]])
- [ ] [R-132] NGO 서버 권위와 연계해 멀티 도전과제 해제 주체 정책 설계 ([[achievements-stats\|도전과제-통계]])
- [ ] [R-133] Depot 분리로 불필요 플랫폼 파일 제외 ([[steam-build-pipeline\|steam-빌드-파이프라인]])

### 코어
- [ ] [R-032] `InputActionRebindingExtensions`+PlayerPrefs 영속화(런타임 리바인딩) ([[input-system\|input-시스템]])
- [ ] [R-033] 입력 스탬프 구조체 도입(예측-재조정용 입력 캡처 P0-3) ([[input-system\|input-시스템]])
- [ ] [R-034] 전역 이벤트 채널 도입/EventBus 확장 + `sceneUnloaded`서 ClearAll() 자동 호출 ([[event-system\|이벤트-시스템]])
- [ ] [R-035] 이벤트별 ServerOnly/ClientOnly 규약 추가(권위 충돌 방지) ([[event-system\|이벤트-시스템]])
- [ ] [R-036] `ObjectPool<T>`/`NetworkObjectPool` 도입해 AI·드롭템 최소화(GC 스파이크) ([[object-pooling\|오브젝트-풀링]])
- [ ] [R-037] 씬 이름/Addressable 키 기반 전환(인덱스 하드코딩 제거) ([[scene-manager\|씬-매니저]])
- [ ] [R-038] `yield return loadOperation` 추가(로드 완료 전 데이터 주입 방지) ([[scene-manager\|씬-매니저]])
- [ ] [R-039] `saveVersion` 필드 + 마이그레이션 체인 ([[save-load\|세이브-로드]])
- [ ] [R-040] 슬롯 확장/세션 ID 기반 동적 파일명(코옵 다중 세션) ([[save-load\|세이브-로드]])
- [ ] [R-041] `com.unity.localization` 설치·키화 시작 + TMP 폰트 한국어 베이크 확인 ([[localization\|로컬라이제이션]])
- [ ] [R-042] UILayerManager/순서 enum Canvas 스택 도입 + async 메서드 try-catch/UniTask ([[ui-framework\|ui-프레임워크]])
- [ ] [R-087] 컨트롤러 연결 후 전 액션 맵 확인(게임패드 실측 QA) ([[input-system\|input-시스템]])
- [ ] [R-088] PlayerInputDiagnostics `#if UNITY_EDITOR`/빌드 제외 처리 ([[input-system\|input-시스템]])
- [ ] [R-089] CharacterEventManager↔EventBus 브릿지 어댑터/단일 채널 통합 ([[event-system\|이벤트-시스템]])
- [ ] [R-090] VFX 풀 반환을 `VisualEffect.stopped` 콜백/코루틴 후로 변경(NRE) ([[object-pooling\|오브젝트-풀링]])
- [ ] [R-091] 진행도 바 로딩 씬/Overlay Canvas 추가(로딩 화면 없음) ([[scene-manager\|씬-매니저]])
- [ ] [R-092] EA 이후 Addressables 전환 검토 ([[scene-manager\|씬-매니저]])
- [ ] [R-093] Newtonsoft.Json/MessagePack 전환 + 최소 XOR 스크램블/HMAC 체크섬 ([[save-load\|세이브-로드]])
- [ ] [R-094] EA 이후 UI Toolkit 전환 로드맵 + `Time.unscaledDeltaTime`/DOTween ([[ui-framework\|ui-프레임워크]])
- [ ] [R-095] UI 언어 정책 통일(YOU DIED 영문 혼용 정리) ([[localization\|로컬라이제이션]])
- [ ] [R-114] InputControlScheme 기반 디바이스 감지+아이콘 교체(입력 추상화) ([[input-system\|input-시스템]])
- [ ] [R-115] `OnAlignmentChanged`의 int를 전용 enum `AlignmentType`으로 교체 ([[event-system\|이벤트-시스템]])
- [ ] [R-116] CaveChunkManager 풀을 `IObjectPool<T>`로 추상화·공개 ([[object-pooling\|오브젝트-풀링]])
- [ ] [R-117] `IsWorldScene` 판정을 명시적 월드 인덱스 목록으로 관리 ([[scene-manager\|씬-매니저]])
- [ ] [R-118] 저장 전 `realtimeSinceStartup` 누산으로 `secondsPlayed` 갱신 ([[save-load\|세이브-로드]])
- [ ] [R-119] 디버그 로그 영문 유지 ([[localization\|로컬라이제이션]])
- [ ] [R-120] EventBus 연동/UI 이벤트 채널 추가(전역 UI 이벤트 버스) ([[ui-framework\|ui-프레임워크]])

### 데이터
- [ ] [R-043] ID를 SO에 고정값/GUID 영속화 + 마이그레이션 테이블(itemID 순서 의존) ([[scriptableobject-architecture\|scriptableobject-아키텍처]], [[data-pipeline\|데이터-파이프라인]])
- [ ] [R-044] DB 원본 참조 유지 + 런타임 스탯은 래퍼(CharacterStats)에 복사 분리 (P1-5) ([[scriptableobject-architecture\|scriptableobject-아키텍처]])
- [ ] [R-045] Google Sheets→CSV→SO 자동 임포터 구축 ([[data-pipeline\|데이터-파이프라인]])
- [ ] [R-046] 빌드 타임 데이터 해시 + 접속 핸드셰이크 검증(서버·클라 버전) ([[data-pipeline\|데이터-파이프라인]])
- [ ] [R-052] `WorldBiomeFactionRegistrySO` 분리(Faction·Biome 도메인 혼재) ([[scriptableobject-architecture\|scriptableobject-아키텍처]])
- [ ] [R-096] `OnValidate` Assert + 배치 검사 도구(데이터 유효성) ([[data-pipeline\|데이터-파이프라인]])
- [ ] [R-121] 네이밍 컨벤션 문서화+일괄 정리(SO 187개) ([[scriptableobject-architecture\|scriptableobject-아키텍처]])
- [ ] [R-122] 중요 수치 changelog SO/주석(데이터 변경 이력) ([[data-pipeline\|데이터-파이프라인]])

### 빌드·CI
- [ ] [R-031] EA 전 GitHub Actions + game-ci/`unity-test-runner`, PR마다 `-runTests -testPlatform playmode` ([[build-automation\|빌드-자동화]], [[ci-cd\|ci-cd]], [[test-framework\|테스트-프레임워크]])
- [ ] [R-047] `ProjectVersion.txt` 자동 갱신 스크립트(빌드 번호·매니페스트) ([[build-automation\|빌드-자동화]])
- [ ] [R-123] 전용 서버 빌드 타겟은 ADR 작성 후 결정 ([[build-automation\|빌드-자동화]])
- [ ] [R-124] 커밋 해시를 빌드 설명에 기록(빌드↔커밋 추적) ([[ci-cd\|ci-cd]])

### QA
- [ ] [R-005] 2번째 Steam 계정/2대 물리 기기 상시 확보해 EA 전 2인 실측(SCN-01~07 × PROF-G/A) 최우선 집행 ([[adr-0001-netcode\|adr-0001-netcode-선정]], [[netcode-solution\|netcode-솔루션]], [[network-topology\|네트워크-토폴로지]], [[bandwidth-budget\|대역폭-예산]], [[multiplayer-testing\|멀티플레이-테스트]], [[lag-compensation\|랙-보상]], [[performance-budget\|성능-예산]])
- [ ] [R-050] `Week2DynamicTests.cs` 복원/신규 작성 + `TDA.PB4.Tests.EditMode` asmdef 추가 ([[test-framework\|테스트-프레임워크]])
- [ ] [R-051] 2인 세션 베이스라인 실측 후 대역폭 예산(KB/s 상한) 정의 ([[performance-budget\|성능-예산]])
- [ ] [R-097] game-ci `--coverage` + Editor `-executeMethod`로 CI 자동화(커버리지·성능 CI) ([[test-framework\|테스트-프레임워크]])
- [ ] [R-098] Memory Profiler 베이스라인 + GPU 16ms 기준 + StandaloneWindows64 실기기 측정 ([[performance-budget\|성능-예산]])
- [ ] [R-125] 자동 봇 클라이언트(SCN-06 부하) 계획 수립 ([[multiplayer-testing\|멀티플레이-테스트]])
- [ ] [R-126] DCPerformanceProfiler를 PlayMode 테스트로 연결 ([[performance-budget\|성능-예산]])

### 보안
- [ ] [R-009] 아키텍처 문서에 치팅 비방어 명기 + EA 전 EAC 검토·최소 VAC 활성화·신고 시스템; 경쟁 요소 추가 시 구조 재검토 ([[adr-0001-netcode\|adr-0001-netcode-선정]], [[authority-model\|권한-모델]], [[anti-cheat\|안티치트]], [[server-hosting\|서버-호스팅]])
- [ ] [R-099] 호스트서 위치 델타·속도 상한 초과 거부 + Step 2 완료 후 soak SCN-07서 0건 확인 ([[anti-cheat\|안티치트]])
- [ ] [R-127] 장기: 진행도 서버 저장 / 단기: 저장 파일 서명·검사 ([[anti-cheat\|안티치트]])

### 인프라
- [ ] [R-006] Step 3 P2-6 호스트 이탈 시 세이브 보장 + 호스트 마이그레이션/전용 서버 검토 ([[network-topology\|네트워크-토폴로지]], [[server-hosting\|서버-호스팅]])
- [ ] [R-100] 다양한 NAT 환경 접속 테스트 ([[server-hosting\|서버-호스팅]])
- [ ] [R-101] 초기 EA 단일 지역 제한 또는 핑 표시 UI ([[server-hosting\|서버-호스팅]])
- [ ] [R-102] 최소 사양 명시 + 호스트 부하 모니터링 ([[server-hosting\|서버-호스팅]])
- [ ] [R-128] EA 허용, 성장 후 매치메이킹 개선 ([[server-hosting\|서버-호스팅]])

### 운영
- [ ] [R-001] 파트너 포털에서 실 AppID 신청($100)·교체 + `steam_appid.txt` 갱신 ([[project-overview\|프로젝트-개요]], [[netcode-solution\|netcode-솔루션]], [[network-topology\|네트워크-토폴로지]], [[steamworks-integration\|steamworks-통합]], [[server-hosting\|서버-호스팅]], [[steamworks-admin\|steamworks-행정]], [[adr-0001-netcode\|adr-0001-netcode-선정]], [[prep-checklist\|사전준비-체크리스트]], [[lobby-matchmaking\|로비-매치메이킹]])
- [ ] [R-103] EA 후 1차 패치에서 기본 실적 추가 ([[steamworks-admin\|steamworks-행정]])
- [ ] [R-129] 첫 업로드 전 app_build VDF 템플릿 작성·저장 ([[steamworks-admin\|steamworks-행정]])

---
← [[12-issues-hub|12 · 이슈·리스크 레지스터]] · [[index|인덱스]]
