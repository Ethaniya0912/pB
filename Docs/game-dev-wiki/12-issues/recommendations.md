---
title: 권고 액션 아이템
tags: [issues, recommendation]
status: done
verified: 2026-06-16
---

# 권고 액션 아이템

[[risk-register|리스크 레지스터]]의 권고를 **영역별 체크리스트**로 모은 것. 중복 제거. 체크박스로 진행 추적.

### 개요
- [ ] maxPlayers를 5인으로 상향하고 ScriptableObject/설정으로 외부화한 뒤 5인 부하(로비·대역폭·스폰) 측정 ([[project-overview|프로젝트-개요]])
- [ ] 타겟 사양 확정 전 SSGI+GPGPU 동굴 FPS 프로파일 완료(현재 일부 씬 60 미달) ([[project-overview|프로젝트-개요]])
- [ ] 정식 게임명 확정 후 코드 식별자·스토어명 정리 ([[project-overview|프로젝트-개요]])
- [ ] `Cave Genderator` 폴더 오타 정정 ([[project-overview|프로젝트-개요]])
- [ ] 테스트/레거시 씬을 빌드에서 제외 정리 ([[project-overview|프로젝트-개요]])
- [ ] 2인 동시 테스트 절차서 작성 ([[decision-priority|의사결정-우선순위]])

### 기반
- [ ] `.gitattributes`에 Unity 표준 LFS 패턴 추가(+ 기존 추적 파일은 `git lfs migrate import`) ([[version-control-git-lfs|버전관리-git-lfs]])
- [ ] `.gitignore`로 미추적 중인 대형 PNG를 LFS로 전환해 공유·빌드 재현 보장 ([[version-control-git-lfs|버전관리-git-lfs]])
- [ ] `.gitattributes`에 UnityYAMLMerge 드라이버 등록(`*.unity/*.prefab/*.asset/*.mat`) ([[version-control-git-lfs|버전관리-git-lfs]])
- [ ] `com.unity.collab-proxy`(UVC) 실제 사용 여부 확인 ([[version-control-git-lfs|버전관리-git-lfs]])
- [ ] `Assets/Scripts/`를 `Interfaces→Core→Networking→Gameplay→Editor` 순으로 점진 asmdef 분리(순환 차단·재컴파일 가속·클라/서버 경계) ([[assembly-definition|assembly-definition]])
- [ ] 에디터/런타임 경계를 명시한 asmdef 선언 ([[assembly-definition|assembly-definition]])
- [ ] 코딩 컨벤션을 Roslyn Analyzer/Unity Code Style로 빌드·PR 시점 강제 ([[coding-conventions|코딩-컨벤션]])
- [ ] 전 스크립트에 네임스페이스 부여(SteamLobbyManager 등 글로벌 제거) ([[coding-conventions|코딩-컨벤션]])
- [ ] async/await 에러 핸들링·취소 토큰 표준 규약 문서화 ([[coding-conventions|코딩-컨벤션]])
- [ ] `[DefaultExecutionOrder]` 범위 배치 기준을 위키에 문서화 ([[coding-conventions|코딩-컨벤션]])
- [ ] 테스트/프로덕션 씬 빌드 포함 여부 확인·분리 ([[project-structure|프로젝트-구조]])
- [ ] `pB-4/week*/` 폴더를 완성 후 기능별로 재구조화 ([[project-structure|프로젝트-구조]])
- [ ] `Bridges&Interfaces`/`Bridges_Interfaces` 중복 폴더 목적 확인·통합 ([[project-structure|프로젝트-구조]])

### 아키텍처
- [ ] SSGI 커스텀 패키지의 URP 버전 의존을 평가하고 URP APV 마이그레이션 시점을 EA 전 결정 ([[render-pipeline|렌더-파이프라인]])
- [ ] 동굴 씬 SSGI On/Off 프레임 비용을 저사양 GPU에서 EA 전 실측 ([[render-pipeline|렌더-파이프라인]])
- [ ] GPUDrivenShadowManager의 `NativeArray`/`GraphicsBuffer`를 `OnDisable` 조건부 해제로 수명 강화 ([[render-pipeline|렌더-파이프라인]])
- [ ] ShaderCoordinationManager `[ExecuteInEditMode]`의 에디터 셰이더 변수 오염 확인 ([[render-pipeline|렌더-파이프라인]])
- [ ] BiomeSyncMode 원자성 통합 테스트 추가 ([[render-pipeline|렌더-파이프라인]])
- [ ] SteamAudio HRTF 오디오 스레드 점유율을 엔티티 증가 시나리오로 측정 ([[adr-0002-render-pipeline|adr-0002-렌더-파이프라인]])
- [ ] 씬당 AI 상한과 성능 예산을 EA 전 측정(WorldAISpawnManager 단일 병목 점검) ([[ecs-vs-oop|ecs-vs-oop]])
- [ ] god-manager(WorldGameStateManager·CaveManager) 책임 분할로 테스트 가능화 ([[ecs-vs-oop|ecs-vs-oop]])
- [ ] Cave Job System 경계(Job 내 managed 접근 금지) 코드 리뷰 ([[ecs-vs-oop|ecs-vs-oop]])
- [ ] 싱글톤 의존성 그래프를 명시화하고 신규 시스템부터 DI(VContainer) 점진 적용 ([[di-container|di-컨테이너]])
- [ ] `FindFirstObjectByType` 호출을 Awake/Start 캐시 또는 참조 주입으로 대체 ([[di-container|di-컨테이너]])
- [ ] AI 시스템(Trust/Trauma/CommandAcceptance/Group) 현황·비판을 별도 문서로 분리·보강 ([[ai-bridge-architecture|ai-bridge-아키텍처]])
- [ ] Bridge 등록 순서·널 브릿지 폴백 동작 문서화 ([[ai-bridge-architecture|ai-bridge-아키텍처]])

### 네트워크
- [ ] 2인 실기기(또는 2 Steam 계정) 측정 1회를 최우선 실행 — M2·M5·M6·M8 정량·SCN-06/07 베이스라인 확보 ([[bandwidth-budget|대역폭-예산]])
- [ ] AI NetworkBehaviour가 플레이어 풀 백본을 상속하는지 코드 점검해 M6 병목 규모 확인 ([[bandwidth-budget|대역폭-예산]])
- [ ] 계측 전용 asmdef 분리를 앞당겨 릴리즈 빌드에서 시뮬 큐·계측 코드 완전 제거 ([[transport-layer|transport-레이어]])
- [ ] LateUpdate try-catch를 null 검사로 교체해 오류 가시화 ([[transport-layer|transport-레이어]])
- [ ] GetCurrentRtt의 예외 무시(`catch{return 0}`)를 가시화 ([[transport-layer|transport-레이어]])
- [ ] Step 2(권위 일원화)를 데모 전 완료 — 방어/패링을 피격자 Owner 위임(P0-3), 줍기 서버 라우팅(P0-5), 사망 권위(P1-3), 인벤 서버 재검증(P1-4) ([[authority-model|권한-모델]])
- [ ] "치팅 방지가 목표가 아님" 제약을 아키텍처 문서에 명기 ([[authority-model|권한-모델]])
- [ ] Door 상태를 NetworkVariable로 승격(Step 3 P2-5)하고 StateChecksumV0 desync 경보 HUD를 EA 전 추가 ([[state-sync|상태-동기화]])
- [ ] RPC clientId 구형 패턴을 `RpcParams.Receive.SenderClientId`로 일괄 교체(P1-7) ([[state-sync|상태-동기화]])
- [ ] 요리 진행도를 `{state, startServerTime}`로 전환해 M7=0 달성(Step 4 P2-3) ([[state-sync|상태-동기화]])
- [ ] 호스트 이탈 시 세이브 보장과 ConnectionApproval(정원·버전) 우선 구현(Step 3 P2-6) ([[network-topology|네트워크-토폴로지]])
- [ ] 재접속 재합류·난입 상태 동기화 구현(P2-5/P2-6) ([[network-topology|네트워크-토폴로지]])
- [ ] Steam AppID 480 교체 일정을 EA 로드맵에 명기 ([[network-topology|네트워크-토폴로지]])
- [ ] 원격 위치 보간을 프레임률 독립(`1-exp(-k·dt)`)으로 교체하고 스냅 임계 거리 적용(Step 4 P2-2) ([[prediction-reconciliation|예측-재조정-보간]])
- [ ] RTT 150ms+ 체감을 PROF-A 환경에서 측정 후 예측·재조정 도입 여부 판단 ([[prediction-reconciliation|예측-재조정-보간]])
- [ ] `CharacterNetworkManager.cs` "Client-Side Prediction" 주석 교정 ([[prediction-reconciliation|예측-재조정-보간]])
- [ ] NGO 내장 보간과 Update 보간의 이중 동작 여부 씬 설정 점검 ([[prediction-reconciliation|예측-재조정-보간]])
- [ ] M5 판정 불일치율 베이스라인(2인, PROF-A)을 Step 2 착수 전 확보하고 결과로 보상 방향 결정 ([[lag-compensation|랙-보상]])
- [ ] UnreliableSequenced→Reliable 승격의 대역폭 영향(M6) 승격 전후 비교 측정 ([[bandwidth-budget|대역폭-예산]])
- [ ] NGO 버전 업그레이드 정책 결정(`[ServerRpc]`↔신문법 혼재 해소) ([[netcode-solution|netcode-솔루션]])

### Steam
- [ ] 실 AppID 발급·교체 + `steam_appid.txt`·`SteamClient.cs` steamAppId 갱신 ([[steamworks-integration|steamworks-통합]])
- [ ] Facepunch DLL 교체일 기록·정기 갱신(보안 패치 반영) ([[steamworks-integration|steamworks-통합]])
- [ ] 비-Steam/오프라인 폴백 구현(조용한 실패 방지) ([[steamworks-integration|steamworks-통합]])
- [ ] Win32/Posix DLL의 Inspector 플랫폼 설정 확인 ([[steamworks-integration|steamworks-통합]])
- [ ] `InitRelayNetworkAccess()` 완료 콜백 대기 로직 추가 ([[steamworks-integration|steamworks-통합]])
- [ ] ConnectionApproval(정원·버전)과 재접속 재합류+상태 복원 구현(Step 3 P2-6) ([[lobby-matchmaking|로비-매치메이킹]])
- [ ] `StartHostWithLobby` 1.5초 하드코딩 대기를 소켓 해제 완료 이벤트 기반으로 교체 ([[lobby-matchmaking|로비-매치메이킹]])
- [ ] `maxPlayers`를 게임 설계 상한과 명시적으로 연동 ([[lobby-matchmaking|로비-매치메이킹]])
- [ ] 친구 초대 버튼의 Steam 초대 API 연결 검증 ([[lobby-matchmaking|로비-매치메이킹]])
- [ ] Steam Cloud(최소 자동 클라우드) 설정 추가 + 로컬-클라우드 충돌 해소 정책 정의 + 저장 경로 호환·할당량 확인 ([[steam-cloud|steam-cloud]])
- [ ] EA 전 최소 5개 도전과제 정의·해제 흐름 검증 + `StoreStats()` 호출 보장 + 멀티 해제 주체 정책 설계 ([[achievements-stats|도전과제-통계]])
- [ ] SteamPipe 수동 업로드 검증 후 자동화 + default/beta 브랜치 구성 + Depot 분리(불필요 플랫폼 제외) ([[steam-build-pipeline|steam-빌드-파이프라인]])

### 코어
- [ ] 런타임 키 리바인딩 UI(`InputActionRebindingExtensions`) + PlayerPrefs 영속화 구현 ([[input-system|input-시스템]])
- [ ] 예측·재조정용 입력 스탬프 구조체(입력 프레임 캡처) 도입 ([[input-system|input-시스템]])
- [ ] 게임패드 전 액션 맵 실측 QA(Steam Deck 대비) ([[input-system|input-시스템]])
- [ ] PlayerInputDiagnostics를 `#if UNITY_EDITOR`/빌드 제외 처리 ([[input-system|input-시스템]])
- [ ] 입력 디바이스(PS/Xbox/키보드) 감지+아이콘 교체 추상화 레이어 추가 ([[input-system|input-시스템]])
- [ ] 게임 전역 이벤트 채널 도입(또는 EventBus 확장)으로 직접 호출 체인 해소 ([[event-system|이벤트-시스템]])
- [ ] `SceneManager.sceneUnloaded`에서 `EventBus.ClearAll()` 자동 호출 보장(구독 누수 방지) ([[event-system|이벤트-시스템]])
- [ ] 이벤트별 ServerOnly/ClientOnly 규약 추가(권위 충돌 방지) ([[event-system|이벤트-시스템]])
- [ ] CharacterEventManager↔EventBus 브릿지 어댑터 또는 단일 채널 통합 ([[event-system|이벤트-시스템]])
- [ ] `OnAlignmentChanged`의 int를 전용 enum `AlignmentType`으로 교체 ([[event-system|이벤트-시스템]])
- [ ] `ObjectPool<T>`/NGO `NetworkObjectPool` 도입(드롭템·투사체·히트VFX·AI 1순위) ([[object-pooling|오브젝트-풀링]])
- [ ] VFX 풀 반환을 `VisualEffect.stopped` 콜백/코루틴 대기 후로 변경(NRE 방지) ([[object-pooling|오브젝트-풀링]])
- [ ] CaveChunkManager 풀을 `IObjectPool<T>`로 추상화·공개 ([[object-pooling|오브젝트-풀링]])
- [ ] 씬 전환을 씬 이름/Addressable 키 기반으로 변경(하드코딩 인덱스 제거) ([[scene-manager|씬-매니저]])
- [ ] `LoadSceneAsync`에 `yield return loadOperation` 추가(로드 완료 전 데이터 주입 방지) ([[scene-manager|씬-매니저]])
- [ ] 진행도 바 로딩 화면(로딩 씬/Overlay Canvas) 추가 ([[scene-manager|씬-매니저]])
- [ ] `IsWorldScene` 판정을 명시적 월드 인덱스 목록으로 관리 ([[scene-manager|씬-매니저]])
- [ ] EA 이후 Addressables 전환 검토 ([[scene-manager|씬-매니저]])
- [ ] 세이브에 `saveVersion` 필드 + 마이그레이션 함수 체인 도입 ([[save-load|세이브-로드]])
- [ ] 세이브를 `SteamRemoteStorage`/Steam 자동 클라우드에 연결 ([[save-load|세이브-로드]])
- [ ] 월드 슬롯을 세션 ID 기반 동적 파일명으로 다중화 ([[save-load|세이브-로드]])
- [ ] JsonUtility를 Newtonsoft.Json/MessagePack으로 전환 검토(딕셔너리 직렬화 안정성) ([[save-load|세이브-로드]])
- [ ] 세이브 파일에 최소 XOR 스크램블/HMAC 체크섬 적용 ([[save-load|세이브-로드]])
- [ ] `secondsPlayed`를 저장 전 `realtimeSinceStartup` 누산으로 갱신 ([[save-load|세이브-로드]])
- [ ] `com.unity.localization` 설치 후 하드코딩 문자열 키화 시작(한국어 단일 테이블) ([[localization|로컬라이제이션]])
- [ ] TMP 폰트에 한국어 유니코드 범위 베이크 확인(폴백 깨짐 방지) ([[localization|로컬라이제이션]])
- [ ] UI 언어 정책 통일(YOU DIED 영문 혼용 정리), 디버그 로그는 영문 유지 ([[localization|로컬라이제이션]])
- [ ] UILayerManager/순서 enum 기반 Canvas 스택 도입 ([[ui-framework|ui-프레임워크]])
- [ ] LobbyUIManager의 `async void`에 try-catch 추가 또는 UniTask 사용 ([[ui-framework|ui-프레임워크]])
- [ ] YOU DIED 팝업 코루틴을 `Time.unscaledDeltaTime`/DOTween으로 교체(timeScale=0 대응) ([[ui-framework|ui-프레임워크]])
- [ ] EA 이후 UI Toolkit 전환 로드맵 수립 ([[ui-framework|ui-프레임워크]])

### 데이터
- [ ] WeaponItem SO를 DB 원본 참조로 유지하고 런타임 스탯 변경은 별도 구조체(CharacterStats)로 분리(P1-5 누수) ([[scriptableobject-architecture|scriptableobject-아키텍처]])
- [ ] itemID를 SO 파일에 고정값/GUID로 영속화 + 마이그레이션 테이블 도입 ([[scriptableobject-architecture|scriptableobject-아키텍처]])
- [ ] 런타임 변경 필드는 래퍼에 복사 후 사용(SO 에셋 변조 가드) ([[scriptableobject-architecture|scriptableobject-아키텍처]])
- [ ] `FactionDefinitionSO`↔`BiomeAffinitySO` 결합을 `WorldBiomeFactionRegistrySO`로 분리 ([[scriptableobject-architecture|scriptableobject-아키텍처]])
- [ ] SO 네이밍 컨벤션 문서화 + 일괄 정리 ([[scriptableobject-architecture|scriptableobject-아키텍처]])
- [ ] Google Sheets→CSV→ScriptableObject 자동 임포터 에디터 도구 구축 ([[data-pipeline|데이터-파이프라인]])
- [ ] 빌드 타임 데이터 해시 + 접속 핸드셰이크로 서버·클라 DB 버전 검증 ([[data-pipeline|데이터-파이프라인]])
- [ ] `OnValidate`에 Assert(중복 ID·음수·null) + 에디터 배치 검사 도구 추가 ([[data-pipeline|데이터-파이프라인]])
- [ ] 중요 수치를 changelog SO/주석으로 변경 이력 추적 ([[data-pipeline|데이터-파이프라인]])

### 빌드·CI
- [ ] EA 전 GitHub Actions + game-ci 도입 및 `BuildPlayer` Editor 스크립트 작성 ([[build-automation|빌드-자동화]])
- [ ] `ProjectVersion.txt`/빌드 번호·매니페스트 자동 갱신 스크립트 작성 ([[build-automation|빌드-자동화]])
- [ ] CI에서 PlayMode 테스트(`-runTests -testPlatform playmode`)를 PR마다 자동 실행 ([[ci-cd|ci-cd]])
- [ ] GitHub Actions에서 `steamcmd +run_app_build` 업로드 자동화(시크릿 관리) ([[ci-cd|ci-cd]])
- [ ] 커밋 해시를 빌드 설명에 기록해 빌드↔커밋 추적 ([[ci-cd|ci-cd]])
- [ ] 전용 서버 빌드 타겟은 ADR 작성 후 결정 ([[build-automation|빌드-자동화]])

### QA
- [ ] `Assets/Tests/PlayMode/` 실질 PlayMode 테스트 복원/신규 작성 ([[test-framework|테스트-프레임워크]])
- [ ] `TDA.PB4.Tests.EditMode` asmdef 추가해 순수 로직 단위 테스트 커버 ([[test-framework|테스트-프레임워크]])
- [ ] CI에서 테스트 자동 실행 + game-ci `--coverage`로 커버리지 측정 ([[test-framework|테스트-프레임워크]])
- [ ] MobRegressionRunner를 Editor `-executeMethod`로 CI 자동화 ([[test-framework|테스트-프레임워크]])
- [ ] 2인 실기기/2계정 세션으로 M2·M8 정량·SCN-07 soak 조속 실시 ([[multiplayer-testing|멀티플레이-테스트]])
- [ ] RTT 실측값 0 고정을 Step 2에서 RNSM RTT 추종으로 교정 ([[multiplayer-testing|멀티플레이-테스트]])
- [ ] 자동 봇 클라이언트(SCN-06 부하) 계획 수립 ([[multiplayer-testing|멀티플레이-테스트]])
- [ ] MobRegressionRunner 재실행으로 최신 FPS 기준(≥60/≥45) 달성 확인 ([[performance-budget|성능-예산]])
- [ ] 네트워크 대역폭 예산(KB/s 상한)을 2인 베이스라인 후 정의 ([[performance-budget|성능-예산]])
- [ ] Unity Memory Profiler로 RAM 예산 베이스라인 측정·문서화 ([[performance-budget|성능-예산]])
- [ ] GPU 프레임 시간 예산(16ms@60FPS) 측정 ([[performance-budget|성능-예산]])
- [ ] StandaloneWindows64 빌드 실기기 FPS 측정 추가 ([[performance-budget|성능-예산]])
- [ ] DCPerformanceProfiler를 PlayMode 테스트로 연결 ([[performance-budget|성능-예산]])

### 보안
- [ ] 출시 전 안티치트 방향 ADR 작성(전용 서버 없이는 호스트 치팅 구조적 미해결) ([[anti-cheat|안티치트]])
- [ ] EA 전 Easy Anti-Cheat 검토, 최소 VAC 활성화 ([[anti-cheat|안티치트]])
- [ ] 호스트 측 입력 sanity check(위치 델타·속도 상한 초과 거부) 추가 ([[anti-cheat|안티치트]])
- [ ] Step 2 권위 교정 완료 후 soak SCN-07에서 `verdict.hp_apply.attackerSide` 0건 확인 ([[anti-cheat|안티치트]])
- [ ] 장기 진행도 서버 저장 / 단기 저장 파일 서명·검사 ([[anti-cheat|안티치트]])

### 인프라
- [ ] 호스트 마이그레이션(별도 구현) 또는 전용 서버 도입 검토 ([[server-hosting|서버-호스팅]])
- [ ] Steamworks 파트너에서 신규 AppID 신청 ([[server-hosting|서버-호스팅]])
- [ ] 다양한 NAT 환경 접속 테스트 ([[server-hosting|서버-호스팅]])
- [ ] 초기 EA 단일 지역 제한 또는 핑 표시 UI 도입 ([[server-hosting|서버-호스팅]])
- [ ] 최소 사양 명시 + 호스트 부하 모니터링 ([[server-hosting|서버-호스팅]])
- [ ] P2P 호스트 트레이드오프(호스트 이탈 시 세션 종료)를 스팀 페이지에 명시 ([[server-hosting|서버-호스팅]])

### 운영
- [ ] Steamworks 파트너 포털에서 실 AppID 즉시 신청($100) 및 교체 ([[steamworks-admin|steamworks-행정]])
- [ ] GitHub Actions + steamcmd로 SteamPipe 업로드 자동화(시크릿 관리) ([[steamworks-admin|steamworks-행정]])
- [ ] Steam Cloud 저장 연결(PC 교체·재설치 대비) ([[steamworks-admin|steamworks-행정]])
- [ ] EA 후 1차 패치에서 기본 실적 추가 ([[steamworks-admin|steamworks-행정]])
- [ ] 첫 업로드 전 app_build VDF 템플릿 작성·저장 ([[steamworks-admin|steamworks-행정]])

---
← [[12-issues-hub|12 · 이슈·리스크 레지스터]] · [[index|인덱스]]
