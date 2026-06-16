---
title: 리스크 레지스터
tags: [issues, risk, moc]
status: done
verified: 2026-06-16
---

# 리스크 레지스터 (전체 216건)

각 문서 `## ⚠ 비판·리스크`를 심각도순으로 집계. **출처** 열의 위키링크로 원본 문서로 이동한다.
🔴 높음 99 · 🟡 보통 80 · ⚪ 낮음 37. 권고 모음 → [[recommendations|권고 액션 아이템]].

| ID | 영역 | 심각도 | 이슈 | 근거 | 권고 | 출처 |
|---|---|---|---|---|---|---|
| R-001 | 개요 | 높음 | 동접 불일치 — 기획 2-5명인데 코드 maxPlayers=4 | `SteamLobbyManager` maxPlayers=4 vs 기획 2-5인 | maxPlayers 상향 + 5인 부하(로비·대역폭·스폰) 측정 | [[project-overview\|프로젝트-개요]] |
| R-002 | 개요 | 높음 | AppID 480(Spacewar) 공용 ID 사용 | 출시 불가·타 로비 혼재 | 실 AppID 등록·교체 | [[project-overview\|프로젝트-개요]] |
| R-003 | 개요 | 높음 | 타겟 사양 미실측 (SSGI+GPGPU 동굴 부하 큼) | FPS 일부 씬 60 미달 | 최소 사양 약속 전 프로파일 필수 | [[project-overview\|프로젝트-개요]] |
| R-004 | 개요 | 높음 | LFS 설정 누락 — 지금 안 고치면 후속 히스토리 재작성 곤란 | `.gitattributes`에 바이너리 패턴 없음 | 개발 초기인 지금 즉시 LFS 설정 | [[prep-checklist\|사전준비-체크리스트]] |
| R-005 | 개요 | 높음 | 게임 코드 전체가 단일 어셈블리(Assembly-CSharp) | 서드파티 6개만 asmdef 분리 | asmdef 분리(컴파일 시간·순환 의존) | [[prep-checklist\|사전준비-체크리스트]] |
| R-006 | 개요 | 높음 | LFS 설정 누락 — 바이너리가 git 오브젝트로 직접 추적 위험 | `.gitattributes` 바이너리 패턴 부재 | 즉시 `.gitattributes`에 LFS 패턴 추가 | [[decision-priority\|의사결정-우선순위]] |
| R-007 | 개요 | 높음 | 동접 불일치 + maxPlayers 하드코딩(외부화 안 됨) | 코드 maxPlayers=4, 하드코딩 | 5인 상향 + ScriptableObject/설정 외부화 | [[00-overview-hub\|00-overview-hub]] |
| R-008 | 기반 | 높음 | 코딩 컨벤션이 도구로 강제되지 않음 | `.editorconfig`에 네이밍·린트 없음, Analyzer 없음 | Roslyn Analyzer/Code Style로 규칙 강제 | [[coding-conventions\|코딩-컨벤션]] |
| R-009 | 기반 | 높음 | 네임스페이스가 일부 스크립트에만 적용 | SteamLobbyManager·SteamP2PRelayTransport 글로벌 네임스페이스 | 전 스크립트 네임스페이스 부여(이름 충돌 방지) | [[coding-conventions\|코딩-컨벤션]] |
| R-010 | 기반 | 높음 | LFS 미설정 — 모든 바이너리가 git 직접 추적 | `.png/.fbx/.wav/.psd/.asset` 등 LFS 패턴 0 | `.gitattributes` Unity LFS 패턴 추가 + `git lfs migrate import` | [[version-control-git-lfs\|버전관리-git-lfs]] |
| R-011 | 기반 | 높음 | 대형 PNG를 경로별 `.gitignore`로 아예 미추적(임시방편) | `ranger_without_cape_rigged_v2/upscale/` 명시 제외 | 에셋을 LFS로 추적해 공유·빌드 재현 보장 | [[version-control-git-lfs\|버전관리-git-lfs]] |
| R-012 | 기반 | 높음 | 단일 Assembly-CSharp — 컴파일 전략 없음 | 서드파티 6개만 asmdef | asmdef 분리 | [[01-foundation-hub\|01-foundation-hub]] |
| R-013 | 기반 | 높음 | LFS 미설정 — 기반 설정 중 가장 시급 | `.gitattributes` 바이너리 패턴 부재 | LFS 즉시 설정 | [[01-foundation-hub\|01-foundation-hub]] |
| R-014 | 기반 | 높음 | 전체 단일 어셈블리 — 모듈 간 방향 강제 없음 | Networking↔Character↔Utilities 자유 참조 | asmdef 분리(상세) | [[project-structure\|프로젝트-구조]] |
| R-015 | 기반 | 높음 | 테스트/프로덕션 씬이 `Assets/Scenes/`에 혼재 | AI TEST·Fog·map generator vs World_01 | 빌드 포함 여부 확인·정리 | [[project-structure\|프로젝트-구조]] |
| R-016 | 기반 | 높음 | 게임 코드 전체 단일 Assembly-CSharp → 부분 변경이 전체 재컴파일 | 씬 스크립트 변경이 네트워크 코드 재컴파일 유발 | asmdef 분리 | [[assembly-definition\|assembly-definition]] |
| R-017 | 기반 | 높음 | 의존성 방향 강제 불가 — 순환 의존 미감지 | Networking↔Character 양방향 참조 가능 | Interfaces 최하단 asmdef로 순환 차단 | [[assembly-definition\|assembly-definition]] |
| R-018 | 기반 | 높음 | 클라 전용 코드와 공통 로직 경계 없음 | 전용 서버 빌드 시 클라 코드 제외 곤란 | 클라/서버 asmdef 경계 도입 | [[assembly-definition\|assembly-definition]] |
| R-019 | 아키텍처 | 높음 | SSGI 커스텀 패키지 비공식 의존 | jiaozi158/UnitySSGIURP, URP 17→18 시 RendererFeature API 파손 위험 | URP APV 마이그레이션 시점 EA 전 평가 | [[render-pipeline\|렌더-파이프라인]] |
| R-020 | 아키텍처 | 높음 | SSGI 성능 예산 실측 없음 | 동굴 SSGI On/Off 비용 데이터 없음 | 저사양 GPU(GTX 1060급) EA 전 검증 | [[render-pipeline\|렌더-파이프라인]] |
| R-021 | 아키텍처 | 높음 | AI·아이템 대량 엔티티 시 성능 스케일 한계 | AI가 MonoBehaviour, WorldAISpawnManager 단일 매니저 병목 | 씬당 AI 상한·성능 예산 EA 전 측정 | [[ecs-vs-oop\|ecs-vs-oop]] |
| R-022 | 아키텍처 | 높음 | God-Manager 경향 — 단일 책임 위반 | WorldGameStateManager·CaveManager 복수 책임 | 책임 분할(단위 테스트 가능화) | [[ecs-vs-oop\|ecs-vs-oop]] |
| R-023 | 아키텍처 | 높음 | 싱글톤 28개+ — 암묵적 전역 상태 그물망 | 파괴 순서 NRE, SteamLobbyManager 가드 중복 | 의존성 명시화/DI 점진 도입 | [[di-container\|di-컨테이너]] |
| R-024 | 아키텍처 | 높음 | 유닛 테스트 불가 | `WorldGameStateManager.Instance` 정적 접근, PlayMode 없이 호출 불가 | 매니저 로직 테스트 가능 구조로 전환 | [[di-container\|di-컨테이너]] |
| R-025 | 아키텍처 | 높음 | 2인 P2P 베이스라인 미집행 | M1~M11이 단일 에디터 루프백만, 실 릴레이 RTT 미측정 | EA 전 2인 실측(SCN-01~07 × PROF-G/A) | [[adr-0001-netcode\|adr-0001-netcode-선정]] |
| R-026 | 아키텍처 | 높음 | 호스트 단독 서버 구조의 보안·권위 취약 | 호스트 치팅 방어 수단 없음(T18 R6) | 경쟁 요소 추가 시 구조 재검토 | [[adr-0001-netcode\|adr-0001-netcode-선정]] |
| R-027 | 아키텍처 | 높음 | SSGI 커스텀 패키지 유지보수 리스크 | RenderGraphModule `#if UNITY_6000_0_OR_NEWER` 버전 의존 시작 | URP APV 교체 시점 사전 평가 | [[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]] |
| R-028 | 아키텍처 | 높음 | SSGI 성능 미측정 | 동굴 SSGI On/Off 프레임 비용 데이터 없음 | 저사양 GPU EA 전 실측 | [[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]] |
| R-029 | 네트워크 | 높음 | 커스텀 transport의 테스트 불가능 구조 | Steam relay 의존, M2·M3·M5가 2인 실기기에 묶임 | 2인 측정용 2번째 Steam 계정 상시 확보 | [[netcode-solution\|netcode-솔루션]] |
| R-030 | 네트워크 | 높음 | NGO Prediction/Rollback 미사용 | Owner 직접 위치 쓰기 + SmoothDamp 보간만 | 예측·재조정 필요성 측정 후 판단 | [[netcode-solution\|netcode-솔루션]] |
| R-031 | 네트워크 | 높음 | 계측 코드가 transport에 혼재 | `SteamP2PRelayTransport.cs`에 게임 로직+계측 공존 | Step 5 asmdef 분리를 앞당김 | [[netcode-solution\|netcode-솔루션]] |
| R-032 | 네트워크 | 높음 | AppID 480(Spacewar) 사용 | 실 AppID 없이 공용 개발 ID | 실 AppID 신청·교체 | [[netcode-solution\|netcode-솔루션]] |
| R-033 | 네트워크 | 높음 | 계측·PROF 코드가 게임 transport에 혼재 | NETDIAG_DISABLED 미정의 시 릴리즈에 시뮬 큐 오버헤드 | asmdef 분리로 릴리즈에서 계측 완전 제거 | [[transport-layer\|transport-레이어]] |
| R-034 | 네트워크 | 높음 | LateUpdate NullReference try-catch 방치 | L403~415 Receive() 예외를 '정상' 처리 | try-catch를 null 검사로 교체해 오류 가시화 | [[transport-layer\|transport-레이어]] |
| R-035 | 네트워크 | 높음 | UnreliableSequenced→Reliable 승격 대역폭 영향 미측정 | 위치·블렌드가 Reliable 승격 시 재전송 오버헤드 | M6 승격 전후 대역폭 비교 측정 | [[transport-layer\|transport-레이어]] |
| R-036 | 네트워크 | 높음 | 방어/패링 판정이 공격자 화면 기준 (P0-3 미해결) | `DamageTarget`이 공격자 머신에서 패링 심사 | Step 2: 피격자 Owner 위임으로 재설계 | [[authority-model\|권한-모델]] |
| R-037 | 네트워크 | 높음 | 클라 줍기 라우팅 없음 (P0-5 미해결) | 아이템 획득 서버 검증 없음, M4 0% | Step 2 권위 일원화로 해결 | [[authority-model\|권한-모델]] |
| R-038 | 네트워크 | 높음 | "일관성이지 치팅 방지 아님" 제약이 미문서화 | 호스트가 위치·HP·데미지 Owner Write 조작 가능 | 아키텍처 문서에 치팅 비방어 명기 | [[authority-model\|권한-모델]] |
| R-039 | 네트워크 | 높음 | Door·요리·QTE·잡기 상태 비동기화 (P2-3/5/9/11) | 난입 시 문·요리·잡기·QTE 미수신 | Door NetworkVariable 승격 등 처리 | [[state-sync\|상태-동기화]] |
| R-040 | 네트워크 | 높음 | StateChecksumV0가 탐지만 하고 복구 못 함 | 30초 LogError만, 재동기화 트리거 없음 | EA 전 불일치 시 재동기화/경고 HUD 추가 | [[state-sync\|상태-동기화]] |
| R-041 | 네트워크 | 높음 | RPC clientId 매개변수 구형 패턴 (P1-7) | `...ClientRpc(ulong clientID)` 조작 가능 | `RpcParams.Receive.SenderClientId`로 일괄 교체 | [[state-sync\|상태-동기화]] |
| R-042 | 네트워크 | 높음 | 호스트 이탈 = 세션 소멸 | `OnClientDisconnected`서 ServerClientId 끊김 시 전원 타이틀 | Step 3 P2-6 호스트 이탈 시 세이브 보장 | [[network-topology\|네트워크-토폴로지]] |
| R-043 | 네트워크 | 높음 | 접속 승인(ConnectionApproval) 없음 | 정원 초과·버전 불일치 거절 불가, NGO 레벨 미검증 | Step 3 P2-6 ConnectionApproval 구현 | [[network-topology\|네트워크-토폴로지]] |
| R-044 | 네트워크 | 높음 | Steam self-connect 불가 — 2인 실측 불가능 | MPPM 2.0.1 self-connect 차단(2026-06-13) | 2대 물리 기기/2계정으로 측정 | [[network-topology\|네트워크-토폴로지]] |
| R-045 | 네트워크 | 높음 | 테스트 AppID 480 — QA 환경 오염 | Spacewar 공개 ID로 타 480 앱과 로비 혼재 | 출시 전 실 AppID 교체(추적 항목 추가) | [[network-topology\|네트워크-토폴로지]] |
| R-046 | 네트워크 | 높음 | 재접속·난입 미지원 (P2-6/P2-5) | 난입 플레이어가 문·요리·잡기 현재 상태 미수신 | 재합류·난입 동기화 구현 | [[network-topology\|네트워크-토폴로지]] |
| R-047 | 네트워크 | 높음 | 예측 없음 → 원격 플레이어 입력 지연 체감 | PROF-A(150ms)서 ~75ms 표시 지연, 은폐 불가 | RTT 150ms+ 체감 측정 후 예측 도입 판단 | [[prediction-reconciliation\|예측-재조정-보간]] |
| R-048 | 네트워크 | 높음 | 원격 위치 보간이 프레임률 의존 (P2-2) | `Slerp(.., Time.deltaTime*rotSpeed)` FPS별 수렴 차이 | `1-exp(-k·dt)` 보정으로 교체 | [[prediction-reconciliation\|예측-재조정-보간]] |
| R-049 | 네트워크 | 높음 | 핑 높은 클라이언트는 항상 불리 (랙보상 없음) | 서버 현재 시점 판정, 되감기 없음 | M5 실측(PROF-A) 후 보상 방향 결정 | [[lag-compensation\|랙-보상]] |
| R-050 | 네트워크 | 높음 | 베이스라인 없이 최적화 계획만 존재 | M6 미측정인데 "-40% 목표" | 2인 실기기 측정을 최우선화 | [[bandwidth-budget\|대역폭-예산]] |
| R-051 | 네트워크 | 높음 | AI가 플레이어 풀 백본 상속 여부 미확인 | AI NetworkBehaviour 파일 미실측, M6 병목 과소평가 위험 | AI 전용 스크립트 점검해 백본 규모 확인 | [[bandwidth-budget\|대역폭-예산]] |
| R-052 | Steam | 높음 | AppID 480 고정 — 출시 전 필수 교체 | `SteamClient.cs:15` steamAppId=480, GameIdValue는 소프트 필터 | 실 AppID 발급·교체 + `steam_appid.txt` 갱신 | [[steamworks-integration\|steamworks-통합]] |
| R-053 | Steam | 높음 | DLL 버전 고정 — 업데이트 미자동화 | `Assets/Plugins/Facepunch/` 수동 복사본 | 교체일 기록·정기 갱신(보안 패치 반영) | [[steamworks-integration\|steamworks-통합]] |
| R-054 | Steam | 높음 | ConnectionApproval 미구현 | 정원 초과·버전 불일치 거절 콜백 없음 | Step 3 P2-6에서 구현 | [[lobby-matchmaking\|로비-매치메이킹]] |
| R-055 | Steam | 높음 | 재접속 재합류 미구현 | 끊긴 후 상태 스냅샷 수신 흐름 미정의 | Step 3 P2-6 재합류+복원 구현 | [[lobby-matchmaking\|로비-매치메이킹]] |
| R-056 | Steam | 높음 | Steam Cloud EA·출시 전 미구현 | 클라우드 세이브 미지원은 리뷰 지적 기본 결함 | 최소 자동 클라우드 설정 추가 | [[steam-cloud\|steam-cloud]] |
| R-057 | Steam | 높음 | 도전과제 없는 출시는 검색 노출 불이익 | Steam 상점 도전과제 배지 부재 | EA 전 최소 5개 도전과제 정의·검증 | [[achievements-stats\|도전과제-통계]] |
| R-058 | Steam | 높음 | SteamPipe EA 전 전제 조건 미비 | 파트너 계정·AppID 미완료가 직접 블로커 | 파트너 계정 발급·AppID 신청 선행 | [[steam-build-pipeline\|steam-빌드-파이프라인]] |
| R-059 | Steam | 높음 | 수동 빌드 의존 — 반복 출시 비용 | 릴리즈마다 수동 빌드→복사→steamcmd | SteamPipe 업로드 자동화 | [[steam-build-pipeline\|steam-빌드-파이프라인]] |
| R-060 | 코어 | 높음 | 런타임 리바인딩 없음 | Steam 표준·접근성 요구, 바인딩 하드코딩 | `InputActionRebindingExtensions`+PlayerPrefs 영속화 | [[input-system\|input-시스템]] |
| R-061 | 코어 | 높음 | 네트워크 입력 캡처 없음 | 예측-재조정(P0-3)용 입력 프레임 캡처 구조 부재 | 입력 스탬프 구조체 도입 | [[input-system\|input-시스템]] |
| R-062 | 코어 | 높음 | 게임 전역 이벤트 버스 부재 | EventBus는 pB-4 AI 전용, 코어 이벤트는 직접 호출 체인 | 전역 이벤트 채널 도입 또는 EventBus 확장 | [[event-system\|이벤트-시스템]] |
| R-063 | 코어 | 높음 | 정적 이벤트 구독 누수 위험 | 전 채널 `static event`, ClearAll() 호출 보장 미확인 | `sceneUnloaded`에서 ClearAll() 자동 호출 | [[event-system\|이벤트-시스템]] |
| R-064 | 코어 | 높음 | 네트워크 이벤트 경계 미정의 | 호스트·클라 양쪽 발행 시 권위 충돌 가능 | 이벤트별 ServerOnly/ClientOnly 규약 추가 | [[event-system\|이벤트-시스템]] |
| R-065 | 코어 | 높음 | 범용 풀 미구현 — GC 스파이크 위험 | AI 20기 전투(SCN-06) 사망/재스폰 GC.Alloc 급증 | `ObjectPool<T>`/`NetworkObjectPool` 도입 | [[object-pooling\|오브젝트-풀링]] |
| R-066 | 코어 | 높음 | NetworkObject 재사용 없음 | `Despawn(false)` 미사용, 스폰마다 ID 등록/해제 | `NetworkObjectPool`로 AI·드롭템 최소화 | [[object-pooling\|오브젝트-풀링]] |
| R-067 | 코어 | 높음 | 씬 인덱스 하드코딩 | `worldSceneIndex=1` Inspector, 빌드 순서 변경 시 오로드 | 씬 이름/Addressable 키 기반 전환 | [[scene-manager\|씬-매니저]] |
| R-068 | 코어 | 높음 | loadOperation 반환값 미처리 | `LoadSceneAsync` 결과를 yield 안 함 → 로드 완료 전 데이터 로드 | `yield return loadOperation` 추가 | [[scene-manager\|씬-매니저]] |
| R-069 | 코어 | 높음 | 세이브 버전 마이그레이션 없음 | 필드 추가/삭제 시 구 세이브 깨짐, 버전 필드 부재 | `saveVersion` 필드 + 마이그레이션 체인 | [[save-load\|세이브-로드]] |
| R-070 | 코어 | 높음 | Steam Cloud 연동 없음 | 다른 PC 접속 시 세이브 소실 | `SteamRemoteStorage`/자동 클라우드 추가 | [[save-load\|세이브-로드]] |
| R-071 | 코어 | 높음 | 월드 슬롯 1개 고정 | `WorldSlots_01`만 존재, 코옵 다중 세션 덮어쓰기 | 슬롯 확장/세션 ID 기반 동적 파일명 | [[save-load\|세이브-로드]] |
| R-072 | 코어 | 높음 | 로컬라이제이션 완전 부재 | 하드코딩 문자열 다수 파일 산재 | 지금 `com.unity.localization` 설치·키화 시작 | [[localization\|로컬라이제이션]] |
| R-073 | 코어 | 높음 | 폰트 누락 위험 | TMP에 한국어 폰트 임베드·어사인 미확인 | TMP 폰트에 한국어 유니코드 범위 베이크 확인 | [[localization\|로컬라이제이션]] |
| R-074 | 코어 | 높음 | 중앙화된 UI 스택 부재 | 팝업·로딩·HUD·로비 독립 Canvas, 순서·규약 없음 | UILayerManager/순서 enum Canvas 스택 도입 | [[ui-framework\|ui-프레임워크]] |
| R-075 | 코어 | 높음 | LobbyUIManager async/await 예외 미처리 | `RefreshRoomList()` async void → 조용한 실패 | try-catch 추가 또는 UniTask 사용 | [[ui-framework\|ui-프레임워크]] |
| R-076 | 데이터 | 높음 | P1-5 WeaponItem SO 인스턴스 누수 | 장비 교체 시 SO 인스턴스 생성 경로 GC 누적 | DB 원본 참조 유지 + 런타임 스탯 별도 구조체 분리 | [[scriptableobject-architecture\|scriptableobject-아키텍처]] |
| R-077 | 데이터 | 높음 | itemID 순서 의존 | 리스트 순서가 ID, 순서 변경 시 세이브 장비 ID 틀어짐 | ID를 SO에 고정값/GUID 영속화 + 마이그레이션 테이블 | [[scriptableobject-architecture\|scriptableobject-아키텍처]] |
| R-078 | 데이터 | 높음 | 런타임 SO 직접 변조 가드 없음 | `public` 필드 직접 쓰면 에셋 오염 | 변경 필드는 래퍼(CharacterStats)에 복사 후 사용 | [[scriptableobject-architecture\|scriptableobject-아키텍처]] |
| R-079 | 데이터 | 높음 | 외부 데이터 임포트 완전 수동 | 밸런스 대규모화 시 수정 시간 선형 증가 | Google Sheets→CSV→SO 자동 임포터 구축 | [[data-pipeline\|데이터-파이프라인]] |
| R-080 | 데이터 | 높음 | 서버·클라 데이터 버전 불일치 감지 없음 | 호스트/클라 DB 다르면 ID 매핑 오류 | 빌드 타임 데이터 해시 + 접속 핸드셰이크 검증 | [[data-pipeline\|데이터-파이프라인]] |
| R-081 | 데이터 | 높음 | itemID 순서 의존(안전장치 없음) | 리스트 순서가 ID, 순서 변경 시 세이브 무효화 | 고정 ID 필드 도입 또는 GUID 기반 | [[data-pipeline\|데이터-파이프라인]] |
| R-082 | 빌드·CI | 높음 | CI/CD 완전 부재 | `.github/` 없음, BuildPipeline 스크립트 없음 | EA 전 GitHub Actions + game-ci 도입 | [[build-automation\|빌드-자동화]] |
| R-083 | 빌드·CI | 높음 | 빌드 번호·매니페스트 미자동화 | 수동 버전 관리 → 릴리즈 혼선 | `ProjectVersion.txt` 자동 갱신 스크립트 | [[build-automation\|빌드-자동화]] |
| R-084 | 빌드·CI | 높음 | CI 완전 부재 — 회귀 자동 감지 불가 | `.github/` 없음 | GitHub Actions + `unity-test-runner`, PR마다 PlayMode 실행 | [[ci-cd\|ci-cd]] |
| R-085 | 빌드·CI | 높음 | PlayMode 테스트가 CI에 연결 안 됨 | `TDA.PB4.Tests.PlayMode.csproj` 자동 실행 없음 | CI에서 `-runTests -testPlatform playmode` | [[ci-cd\|ci-cd]] |
| R-086 | QA | 높음 | 테스트 파일 실질 0건 | `Assets/Tests/PlayMode/` 비어 있음(csproj와 불일치) | `Week2DynamicTests.cs` 복원/신규 작성 | [[test-framework\|테스트-프레임워크]] |
| R-087 | QA | 높음 | EditMode 테스트 부재 | EditMode asmdef 없음 | `TDA.PB4.Tests.EditMode` asmdef 추가 | [[test-framework\|테스트-프레임워크]] |
| R-088 | QA | 높음 | CI 연결 없음 (테스트 자동 실행 수단 없음) | 테스트 있어도 자동 실행 불가 | GitHub Actions + `-runTests -testPlatform playmode` | [[test-framework\|테스트-프레임워크]] |
| R-089 | QA | 높음 | 2인 실측 미완료 | M2·M8 정량·SCN-07 soak 2인 대기, host-only 근사만 | 2인 실기기/2계정 세션 조속 실시 | [[multiplayer-testing\|멀티플레이-테스트]] |
| R-090 | QA | 높음 | Steam self-connect 차단으로 에디터 자동화 불가 | 2026-06-13 실증 | 2인 머신 없이는 멀티 회귀 자동화 불가(대안 수립) | [[multiplayer-testing\|멀티플레이-테스트]] |
| R-091 | QA | 높음 | FPS 기준 미재측정 | 2026-04-27 측정(Avg 3.1 FAIL) 후 개선 재측 없음 | MobRegressionRunner 재실행해 달성 확인 | [[performance-budget\|성능-예산]] |
| R-092 | QA | 높음 | 네트워크 대역폭 예산 미정의 | SCN-06 절차 있으나 KB/s 상한 미설정 | 2인 세션 후 베이스라인 실측→예산 정의 | [[performance-budget\|성능-예산]] |
| R-093 | 보안 | 높음 | 호스트 자체 치팅 방어 불가 | P2P 호스트=서버, 판정 조작 가능 | 출시 전 ADR; 임시 VAC 활성화·신고 시스템 검토 | [[anti-cheat\|안티치트]] |
| R-094 | 보안 | 높음 | 전용 안티치트 미구현 | EAC/BattlEye 없음, 메모리·스피드핵 탐지 불가 | EA 전 EAC 검토, 최소 VAC 활성화 | [[anti-cheat\|안티치트]] |
| R-095 | 인프라 | 높음 | 호스트 이탈 시 세션 소멸 | NGO P2P — 호스트 이탈 시 전 클라 끊김 | 호스트 마이그레이션(별도 구현) 또는 전용 서버 검토 | [[server-hosting\|서버-호스팅]] |
| R-096 | 인프라 | 높음 | 호스트 치팅 방어 불가 | 호스트가 곧 서버 | 전용 서버 없이는 구조적 해결 불가 | [[server-hosting\|서버-호스팅]] |
| R-097 | 인프라 | 높음 | Steam AppID = 480(테스트용) | 실 앱 ID 미등록 → 릴리즈 불가 | Steamworks 파트너에서 신규 AppID 신청 | [[server-hosting\|서버-호스팅]] |
| R-098 | 운영 | 높음 | 실 AppID 미등록 | `steam_appid.txt`=480, 릴리즈 불가 | 파트너 포털에서 즉시 신청($100) | [[steamworks-admin\|steamworks-행정]] |
| R-099 | 운영 | 높음 | SteamPipe 업로드 자동화 없음 | 수동 업로드, 릴리즈 빈도 증가 시 병목 | GitHub Actions + steamcmd 자동화(시크릿 관리) | [[steamworks-admin\|steamworks-행정]] |
| R-100 | 개요 | 보통 | 게임명 미정 — 식별자·가칭 혼재 | 코드 `PennutButterProject` vs 가칭 pC | 정식명 확정 후 식별자·스토어명 정리 | [[project-overview\|프로젝트-개요]] |
| R-101 | 개요 | 보통 | 폴더 오타 `Cave Genderator` | Generator 오타 | 폴더명 정정(검색·참조 혼선 제거) | [[project-overview\|프로젝트-개요]] |
| R-102 | 개요 | 보통 | 성능 예산(타겟 FPS/해상도/사양) 미정의 | 그래픽 최적화 방향 미결정 | 성능 예산 정의 | [[prep-checklist\|사전준비-체크리스트]] |
| R-103 | 개요 | 보통 | 동접 기획 2-5명 vs 코드 maxPlayers=4 | 타겟 사양만 실측 남음, maxPlayers 불일치 | 5인 상향 + 5인 부하 측정 | [[decision-priority\|의사결정-우선순위]] |
| R-104 | 개요 | 보통 | 2인 동시 테스트 절차서 없음 | `multiplayer.playmode` 설치만, 단일 클라 검증 추정 | 2인 테스트 절차서 작성 | [[decision-priority\|의사결정-우선순위]] |
| R-105 | 기반 | 보통 | `.editorconfig`가 네이밍/린트 규칙 미포함 | charset/eol만, `_conventions.md` 미강제 | 규칙을 도구로 강제 | [[01-foundation-hub\|01-foundation-hub]] |
| R-106 | 기반 | 보통 | 비동기(async/await) 규칙 미문서화 | 에러 핸들링·취소 토큰 미표준화 | 비동기 표준 규약 문서화 | [[coding-conventions\|코딩-컨벤션]] |
| R-107 | 기반 | 보통 | `[DefaultExecutionOrder]` 배치 기준 위키에 없음 | 숫자 범위가 주석에만(-800/-500/-1000) | 실행 순서 범위 기준 문서화 | [[coding-conventions\|코딩-컨벤션]] |
| R-108 | 기반 | 보통 | `pB-4/week*/` 폴더 83개+ 증식 | Inspector·Project 뷰 탐색 곤란 | 완성 후 기능별 재구조화 | [[project-structure\|프로젝트-구조]] |
| R-109 | 기반 | 보통 | 폴더명 오타 `Cave Genderator` | 네임스페이스·주석·문서 전파 가능 | 폴더명 정정(.meta 재생성) | [[project-structure\|프로젝트-구조]] |
| R-110 | 기반 | 보통 | 에디터/런타임 경계 명시 asmdef 없음 | 에디터 코드 런타임 참조 오류 컴파일 시 미감지 | 명시적 asmdef 경계 선언 | [[assembly-definition\|assembly-definition]] |
| R-111 | 아키텍처 | 보통 | GPUDrivenShadowManager NativeArray 수명 취약 | `NativeArray`를 `OnDestroy`에서만 Dispose | `OnDisable` 조건부 해제로 수명 강화 | [[render-pipeline\|렌더-파이프라인]] |
| R-112 | 아키텍처 | 보통 | ShaderCoordinationManager `[ExecuteInEditMode]` 부작용 | 에디터 셰이더 전역 변수 오염 가능 | 에디터 전용 변수 오염 여부 확인 | [[render-pipeline\|렌더-파이프라인]] |
| R-113 | 아키텍처 | 보통 | BiomeSyncMode 원자성 보장 미검증 | 같은 프레임 원자 적용 통합 테스트 없음 | 원자성 통합 테스트 추가 | [[render-pipeline\|렌더-파이프라인]] |
| R-114 | 아키텍처 | 보통 | ECS 전환 비용이 사실상 전면 재작성 | 28+ 매니저 전체 MonoBehaviour | EA 이전 ECS 전환 비현실적(현 구조 유지) | [[ecs-vs-oop\|ecs-vs-oop]] |
| R-115 | 아키텍처 | 보통 | Netcode for Entities 비채택으로 ECS 경로 막힘 | NGO+OOP 선택, 대량 엔티티 동기화 경로 차단 | NetworkObject 수 한계 고려한 규모 설계 | [[ecs-vs-oop\|ecs-vs-oop]] |
| R-116 | 아키텍처 | 보통 | `FindFirstObjectByType` 런타임 비용 | 40+ 파일 씬 전체 탐색, 업데이트 루프 시 스파이크 | Awake/Start 캐시 또는 참조 주입 | [[di-container\|di-컨테이너]] |
| R-117 | 아키텍처 | 보통 | `DontDestroyOnLoad` 씬 전환 정리 복잡도 | 28+ 싱글톤 초월 생존, 정리 누락 시 상태 오염 | 정리 책임 일원화/검증 | [[di-container\|di-컨테이너]] |
| R-118 | 아키텍처 | 보통 | AI 아키텍처 문서 부재(착수 단계) | pB-4 week0~8 AI 미문서화, status: researching | 코드 실측 기반 현황 보강 | [[ai-bridge-architecture\|ai-bridge-아키텍처]] |
| R-119 | 아키텍처 | 보통 | Bridge 계약의 런타임 검증·수명 미확인 | 등록 순서·널 브릿지 폴백 미문서화 | 등록 순서·폴백 동작 문서화 | [[ai-bridge-architecture\|ai-bridge-아키텍처]] |
| R-120 | 아키텍처 | 보통 | 동기화된 Trust/Trauma 대역폭 영향 미측정 | NPC×플레이어별 신뢰 딕셔너리 동기화 트래픽 미반영 | bandwidth-budget 실측에 포함 | [[ai-bridge-architecture\|ai-bridge-아키텍처]] |
| R-121 | 아키텍처 | 보통 | UnreliableSequenced→Reliable 승격 대역폭 증가 | Steam에 sequenced-unreliable 부재로 승격 | Step 4 대역폭 최적화(T29) 시 재검토 | [[adr-0001-netcode\|adr-0001-netcode-선정]] |
| R-122 | 아키텍처 | 보통 | Steam AppID 480(Spacewar) 사용 중 | `steam_appid.txt`=480, 로비 격리 불완전 | 정식 AppID 신청·적용 | [[adr-0001-netcode\|adr-0001-netcode-선정]] |
| R-123 | 아키텍처 | 보통 | GPUDrivenShadowManager 씬 전환 정리 검증 미흡 | `GraphicsBuffer`·`NativeArray` `OnDestroy`만 해제 | 중복 초기화 방지 가드·해제 강화 | [[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]] |
| R-124 | 아키텍처 | 보통 | SteamAudio HRTF 성능 예산 미확정 | 4인+다수 NPC 오디오 스레드 비용 미측정 | 오디오 스레드 점유율 측정 | [[adr-0002-render-pipeline\|adr-0002-렌더-파이프라인]] |
| R-125 | 네트워크 | 보통 | NGO 버전 잠금 — Breaking Change 잦음 | `[ServerRpc]`와 신문법 혼재(P1-7) | NGO 업그레이드 정책 결정 | [[netcode-solution\|netcode-솔루션]] |
| R-126 | 네트워크 | 보통 | GetCurrentRtt 예외 무시 | `catch { return 0; }` → 실패 시 RTT 0 표시 | 예외 가시화로 진단 개선 | [[transport-layer\|transport-레이어]] |
| R-127 | 네트워크 | 보통 | Send 경로의 1대多 순회 | `socketManager.Connected` O(N) ID 대조 | 친선 코옵 범위는 무시 가능하나 구조 개선 | [[transport-layer\|transport-레이어]] |
| R-128 | 네트워크 | 보통 | Steam relay 단일 경로 의존 | Steam 장애 시 연결 불가, 직접 P2P 폴백 없음 | 가용성 의존 인지(실용 위험 낮음) | [[transport-layer\|transport-레이어]] |
| R-129 | 네트워크 | 보통 | Owner Write 위치의 서버 검증 없음 | 클라가 `networkPosition.Value` 임의 기록 | 버그성 위치 왜곡 대비 검증 | [[authority-model\|권한-모델]] |
| R-130 | 네트워크 | 보통 | 사망 이중 처리 가능성 (P1-3) | 동시 히트 시 `ProcessDeathEvent` 중복, 드롭 중복 | Step 2 사망 권위 일원화 | [[authority-model\|권한-모델]] |
| R-131 | 네트워크 | 보통 | 인벤토리 클라 조작 무방비 (P1-4) | 서버 `IsSpaceAvailable` 재검증 없음 | 서버 재검증 추가(M11 탐지 보강) | [[authority-model\|권한-모델]] |
| R-132 | 네트워크 | 보통 | 원격 위치 보간 프레임률 의존 (P2-2) | `Slerp(cur,target,dt*rotSpeed)` FPS별 차이 | Step 4 P2-2 지수 감쇠 보정 | [[state-sync\|상태-동기화]] |
| R-133 | 네트워크 | 보통 | NetworkVariable 변경 없을 때 전송 억제 미확인 | 정지 시에도 매 틱 `networkPosition.Value` 기록 | 전송 게이팅 검증/적용 | [[state-sync\|상태-동기화]] |
| R-134 | 네트워크 | 보통 | StateChecksumV0 해시 범위가 지형+인벤만 | 문·요리·잡기·QTE desync 미탐지 | Step 5 해시 범위 확장 | [[state-sync\|상태-동기화]] |
| R-135 | 네트워크 | 보통 | Step 2 피격자 Owner 위임도 랙보상 아님 | 추가 RTT로 공격자 피드백 지연 | 공정성↔응답성 트레이드오프 측정 | [[lag-compensation\|랙-보상]] |
| R-136 | 네트워크 | 보통 | 판정 불일치율(M5) 베이스라인 없음 | VerdictLogger 있으나 2인 실측 없음 | 2인 실측으로 불일치 실수치 확보 | [[lag-compensation\|랙-보상]] |
| R-137 | 네트워크 | 보통 | 거리 기반 갱신 차등의 NGO 구조적 한계 | NetworkVariable 클라별 차등 불가 | `CheckObjectVisibility`/커스텀 RPC 검토 | [[bandwidth-budget\|대역폭-예산]] |
| R-138 | 네트워크 | 보통 | UnreliableSequenced→Reliable 승격 대역폭 영향 | 위치·블렌드가 Reliable 채널 시 오버헤드 M6 포함 | 승격 전후 대역폭 비교 | [[bandwidth-budget\|대역폭-예산]] |
| R-139 | 네트워크 | 보통 | SCN-06 절차 미집행 | AI 20기 5분 부하 테스트 2인 없이 불가 | 2인 기기 확보 후 최악 시나리오 측정 | [[bandwidth-budget\|대역폭-예산]] |
| R-140 | 네트워크 | 보통 | "Client-Side Prediction" 용어 오용 | `CharacterNetworkManager.cs` L163 주석 오해 유발 | 주석 교정 | [[prediction-reconciliation\|예측-재조정-보간]] |
| R-141 | 네트워크 | 보통 | NGO 내장 보간과 중복 여부 미확인 | NetworkObject Interpolation + Update 보간 이중 가능 | 씬 설정 점검 | [[prediction-reconciliation\|예측-재조정-보간]] |
| R-142 | 네트워크 | 보통 | 목표 갱신 시 SmoothDamp 재시작 없음 | 텔레포트·스폰 시 긴 거리 미끄러짐 | Step 4 P2-2 스냅 임계 거리 검사 | [[prediction-reconciliation\|예측-재조정-보간]] |
| R-143 | Steam | 보통 | 오프라인/비-Steam 폴백 없음 | `Init` 실패 시 LogError 후 무동작, 멀티 조용히 실패 | 오프라인 폴백 구현 | [[steamworks-integration\|steamworks-통합]] |
| R-144 | Steam | 보통 | 1.5초 하드코딩 대기 | `await Task.Delay(1500)` 소켓 클린업 고정 | 소켓 해제 완료 이벤트 기반 대기 | [[lobby-matchmaking\|로비-매치메이킹]] |
| R-145 | Steam | 보통 | 최대 플레이어 4인 인스펙터 의존 | `maxPlayers=4` SerializeField, 설계 상한 미연동 | 게임 설계 상한과 명시적 연동 | [[lobby-matchmaking\|로비-매치메이킹]] |
| R-146 | Steam | 보통 | 친구 초대 버튼 로직 미연결 추정 | `inviteFriendButton` 있으나 초대 API 호출 미확인 | 초대 API 연결 검증 | [[lobby-matchmaking\|로비-매치메이킹]] |
| R-147 | Steam | 보통 | 로컬-클라우드 충돌 해소 로직 없음 | 여러 PC 동시 플레이 충돌 정책 미정의 | 충돌 해소 정책 정의 | [[steam-cloud\|steam-cloud]] |
| R-148 | Steam | 보통 | 세이브 파일 경로 Cloud 호환 미확인 | `WorldSaveGameManager` 저장 경로 미확인 | 자동 클라우드 전 경로 매핑 확인 | [[steam-cloud\|steam-cloud]] |
| R-149 | Steam | 보통 | `StoreStats()` 누락 시 통계·도전과제 미저장 | `SetAchievement/AddStat` 후 `StoreStats` 필수 | 종료·씬 전환 시 StoreStats 호출 보장 | [[achievements-stats\|도전과제-통계]] |
| R-150 | Steam | 보통 | 브랜치 정책 미정 | default/beta/internal 브랜치 없음 | QA/공개 빌드 분리 브랜치 구성 | [[steam-build-pipeline\|steam-빌드-파이프라인]] |
| R-151 | 코어 | 보통 | 게임패드 실측 QA 부재 | 스틱 경로 있으나 컨트롤러 테스트 미확인 | 컨트롤러 연결 후 전 액션 맵 확인 | [[input-system\|input-시스템]] |
| R-152 | 코어 | 보통 | PlayerInputDiagnostics 프로덕션 잔류 주의 | `DontDestroyOnLoad` 디버그 오브젝트 릴리즈 동작 | `#if UNITY_EDITOR`/빌드 제외 처리 | [[input-system\|input-시스템]] |
| R-153 | 코어 | 보통 | 두 이벤트 시스템 간 브릿지 없음 | CharacterEventManager(컴포넌트)와 EventBus(정적) 분리 | 이벤트 브릿지 어댑터/단일 채널 통합 | [[event-system\|이벤트-시스템]] |
| R-154 | 코어 | 보통 | VFX 소멸 타이머 방식 | `Utility_DestroyAfterTime` 풀 반환 시점 부정확, NRE | `VisualEffect.stopped` 콜백/코루틴 후 반환 | [[object-pooling\|오브젝트-풀링]] |
| R-155 | 코어 | 보통 | 로딩 화면 없음 | 씬 전환 중 검은 화면/이전 씬 노출 | 진행도 바 로딩 씬/Overlay Canvas | [[scene-manager\|씬-매니저]] |
| R-156 | 코어 | 보통 | Addressables 미도입 | 씬·에셋 증가 시 빌드 크기·로딩 증가 | EA 이후 Addressables 전환 검토 | [[scene-manager\|씬-매니저]] |
| R-157 | 코어 | 보통 | JsonUtility 한계 (`Dictionary<>` 미지원) | `SerializableDictionary` 직렬화 에러 시 데이터 소실 | Newtonsoft.Json/MessagePack 전환 검토 | [[save-load\|세이브-로드]] |
| R-158 | 코어 | 보통 | 세이브 파일 암호화·체크섬 없음 | 텍스트 JSON 무한 스탯 주입 가능 | 최소 XOR 스크램블/HMAC 체크섬 | [[save-load\|세이브-로드]] |
| R-159 | 코어 | 보통 | YOU DIED 텍스트 영문 혼용 | 나머지 UI는 한국어 | 언어 정책 통일 | [[localization\|로컬라이제이션]] |
| R-160 | 코어 | 보통 | UI Toolkit 미사용 | Unity 6 권장 방향, uGUI 레거시 경로 | EA 이후 UI Toolkit 전환 로드맵 | [[ui-framework\|ui-프레임워크]] |
| R-161 | 코어 | 보통 | YOU DIED 팝업 타이밍 의존 | `Time.deltaTime` 누산, `timeScale=0` 시 미동작 | `Time.unscaledDeltaTime`/DOTween | [[ui-framework\|ui-프레임워크]] |
| R-162 | 데이터 | 보통 | FactionDefinitionSO·BiomeAffinitySO 도메인 혼재 | 팩션 정의+지역 분포 결합, 의존성 폭발 | `WorldBiomeFactionRegistrySO` 분리 | [[scriptableobject-architecture\|scriptableobject-아키텍처]] |
| R-163 | 데이터 | 보통 | 데이터 유효성 검사기 없음 | 중복 ID·음수 데미지·null 프리팹 조용히 통과 | `OnValidate` Assert + 배치 검사 도구 | [[data-pipeline\|데이터-파이프라인]] |
| R-164 | 빌드·CI | 보통 | Steam 업로드 수동화 | SteamPipe 수동 실행, 누락·오버라이트 위험 | GitHub Actions에서 steamcmd 자동화 | [[build-automation\|빌드-자동화]] |
| R-165 | 빌드·CI | 보통 | Steam 업로드 자동화 없음 | EA 빈도 증가 시 병목·오류 | `steamcmd +run_app_build` 자동화 | [[ci-cd\|ci-cd]] |
| R-166 | QA | 보통 | 코드 커버리지 미측정 | 커버리지 리포트 없음 | game-ci `--coverage` 옵션 | [[test-framework\|테스트-프레임워크]] |
| R-167 | QA | 보통 | 성능 테스트 CI 미연결 | MobRegressionRunner 수동 전용 | Editor `-executeMethod`로 CI 자동화 | [[test-framework\|테스트-프레임워크]] |
| R-168 | QA | 보통 | RTT 실측값 0 고정 (P0-4) | `GetCurrentRtt`=0(loopback) | Step 2 RNSM RTT 추종 교정 | [[multiplayer-testing\|멀티플레이-테스트]] |
| R-169 | QA | 보통 | soak 테스트(SCN-07) 미집행 | 30분 무중단 체크섬 0 미달성 | 2인 세션 확보 후 최우선 실행 | [[multiplayer-testing\|멀티플레이-테스트]] |
| R-170 | QA | 보통 | 메모리 예산(RAM) 미정의 | GPU 버퍼(50MB)만, 전체 메모리 상한 없음 | Unity Memory Profiler 베이스라인 측정 | [[performance-budget\|성능-예산]] |
| R-171 | QA | 보통 | GPU 프레임 시간 예산 없음 | CPU 프레임(FPS)만, GPU 목표 없음 | GPU 16ms(60FPS) 기준 Profiler 측정 | [[performance-budget\|성능-예산]] |
| R-172 | QA | 보통 | 빌드 환경 FPS 미측정 | Editor Play Mode 기준, 빌드와 괴리 | StandaloneWindows64 빌드 실기기 측정 | [[performance-budget\|성능-예산]] |
| R-173 | 보안 | 보통 | 입력 sanity check 없음 | 이동 속도·공격 범위 서버 검증 코드 부재 | 호스트서 위치 델타·속도 상한 초과 거부 | [[anti-cheat\|안티치트]] |
| R-174 | 보안 | 보통 | R6 권위 교정 미완료 | `verdict.hp_apply.attackerSide` Step 2 진행 중 | Step 2 완료 후 soak SCN-07서 0건 확인 | [[anti-cheat\|안티치트]] |
| R-175 | 인프라 | 보통 | NAT 홀펀칭 실패 시 중계 의존 | Steam 중계 지연 증가 가능 | 다양한 NAT 환경 접속 테스트 | [[server-hosting\|서버-호스팅]] |
| R-176 | 인프라 | 보통 | 지역 배치 없음 | 글로벌 출시 시 원거리 고지연 | 초기 EA 단일 지역 제한 또는 핑 표시 UI | [[server-hosting\|서버-호스팅]] |
| R-177 | 인프라 | 보통 | 성능이 호스트 머신 사양에 종속 | 서버 품질=플레이어 PC 사양 | 최소 사양 명시 + 호스트 부하 모니터링 | [[server-hosting\|서버-호스팅]] |
| R-178 | 운영 | 보통 | Steam Cloud 미구현 | 로컬 저장만, PC 교체·재설치 시 소실 | 파일 경로를 Steam Cloud에 연결 | [[steamworks-admin\|steamworks-행정]] |
| R-179 | 운영 | 보통 | 실적 미구현 | Achievement 없음, 유저 참여 수단 부재 | EA 후 1차 패치에서 기본 실적 추가 | [[steamworks-admin\|steamworks-행정]] |
| R-180 | 개요 | 낮음 | 씬 정리 — 테스트/레거시 씬 다수 | 빌드 포함 여부 불명확 | 빌드 제외 정리 | [[project-overview\|프로젝트-개요]] |
| R-181 | 개요 | 낮음 | 안티치트 미구현 + AppID 480 릴리즈 금지 | 코옵 PvE면 우선순위 낮음 | AppID 480인 채 릴리즈 금지 | [[prep-checklist\|사전준비-체크리스트]] |
| R-182 | 개요 | 낮음 | 순위 4·5(state-sync, 멀티 테스트)는 선행 결정에 의존 | 1·2·3 고정 전 문서화 곤란 | 앞 결정 고정 후 문서화 | [[decision-priority\|의사결정-우선순위]] |
| R-183 | 개요 | 낮음 | 허브 문서가 링크 목록만 — 진입 비용 | 내용이 각 문서에 분산 | — | [[00-overview-hub\|00-overview-hub]] |
| R-184 | 기반 | 낮음 | `Cave Genderator` 오타가 네임스페이스 오염 가능 | `CaveSystem.Multiplayer` using 확인 | 오타 정정 | [[coding-conventions\|코딩-컨벤션]] |
| R-185 | 기반 | 낮음 | `_Recovery/` 폴더가 에디터 로딩 시간 영향 | 복구 씬 29개+ Unity 임포트 | 로딩 영향 최소화/정리 | [[project-structure\|프로젝트-구조]] |
| R-186 | 기반 | 낮음 | `Bridges&Interfaces`/`Bridges_Interfaces` 중복 폴더 의심 | `&`와 `_` 차이 두 폴더 | 목적 확인·통합 | [[project-structure\|프로젝트-구조]] |
| R-187 | 기반 | 낮음 | 테스트 어셈블리가 `internal` 접근 불가 | 게임 코드 asmdef 미분리 | asmdef 분리로 internal 접근 확보 | [[assembly-definition\|assembly-definition]] |
| R-188 | 기반 | 낮음 | `com.unity.collab-proxy` Git 병행 충돌 위험 | UVC 통합 2.10.2 설치 | 실제 사용 여부 확인 | [[version-control-git-lfs\|버전관리-git-lfs]] |
| R-189 | 아키텍처 | 낮음 | SteamAudio HRTF 스레드 비용 미측정 | 4인+다수 NPC 오디오 스레드 점유율 미측정 | 엔티티 증가 시 오디오 비용 측정 | [[render-pipeline\|렌더-파이프라인]] |
| R-190 | 아키텍처 | 낮음 | Cave GPGPU와 OOP 경계 검증 누락 | Job 내 managed 객체 접근 시 런타임 크래시 | Job 경계 코드 리뷰 | [[ecs-vs-oop\|ecs-vs-oop]] |
| R-191 | 아키텍처 | 낮음 | VContainer 도입 시 전환 비용 | 28+ 싱글톤 전수 교체 부담 | 신규 시스템부터 점진 적용 | [[di-container\|di-컨테이너]] |
| R-192 | 아키텍처 | 낮음 | week 단위 폴더 증식(AI) | 주차별 80+ 폴더 분산 | 완성 후 기능별 재구조화 | [[ai-bridge-architecture\|ai-bridge-아키텍처]] |
| R-193 | 네트워크 | 낮음 | PvE 코옵에서 랙보상 필요성 자체 불명확 | PvP 없음, AI는 호스트 권위 | M5 실측 후 판단(선제 구현 지양) | [[lag-compensation\|랙-보상]] |
| R-194 | 네트워크 | 낮음 | 목표 갱신 시 SmoothDamp 재시작 없음(스냅 임계) | 원격 텔레포트/스폰 미끄러짐 | Step 4 P2-2 스냅 임계 적용 | [[prediction-reconciliation\|예측-재조정-보간]] |
| R-195 | 코어 | 낮음 | 입력 추상화 레이어 없음 | PS/Xbox/키보드 프롬프트 분기 없음 | InputControlScheme 기반 디바이스 감지+아이콘 교체 | [[input-system\|input-시스템]] |
| R-196 | 코어 | 낮음 | 이벤트 타입 안전성 부분 부족 | `OnAlignmentChanged`의 old/new가 int | 전용 enum `AlignmentType` 교체 | [[event-system\|이벤트-시스템]] |
| R-197 | 코어 | 낮음 | CaveChunkManager 풀 API 미공개 | 청크 풀이 클래스 내부에만 존재 | `IObjectPool<T>`로 추상화 | [[object-pooling\|오브젝트-풀링]] |
| R-198 | 코어 | 낮음 | IsWorldScene 판정 단순 | `buildIndex>0`이면 모두 월드 취급 | 명시적 월드 인덱스 목록 관리 | [[scene-manager\|씬-매니저]] |
| R-199 | 코어 | 낮음 | `secondsPlayed` 미갱신 확인 필요 | 필드 있으나 갱신 코드 미발견 | 저장 전 `realtimeSinceStartup` 누산 갱신 | [[save-load\|세이브-로드]] |
| R-200 | 코어 | 낮음 | DEBUG 로그 한국어 | 해외 협력자·포럼 공유 어려움 | 디버그 로그 영문 유지 | [[localization\|로컬라이제이션]] |
| R-201 | 코어 | 낮음 | 전역 UI 이벤트 버스 없음 | UI 갱신 직접 호출 체인, 복수 플레이어 UI 확장성 낮음 | EventBus 연동/UI 이벤트 채널 추가 | [[ui-framework\|ui-프레임워크]] |
| R-202 | 데이터 | 낮음 | SO 파일 187개 — 명명 규칙 불일치 | 영문/한글 혼용 추정 | 네이밍 컨벤션 문서화+일괄 정리 | [[scriptableobject-architecture\|scriptableobject-아키텍처]] |
| R-203 | 데이터 | 낮음 | 데이터 변경 이력 추적 어려움 | SO가 바이너리 .asset, diff 무의미 | 중요 수치 changelog SO/주석 | [[data-pipeline\|데이터-파이프라인]] |
| R-204 | 빌드·CI | 낮음 | 전용 서버 빌드 타겟 없음 | 현재 P2P 호스트, dedicated 전환 시 작업 | ADR 작성 후 결정 | [[build-automation\|빌드-자동화]] |
| R-205 | 빌드·CI | 낮음 | 빌드 아티팩트 버전 추적 없음 | 커밋↔빌드 추적 불가 | 커밋 해시를 빌드 설명에 기록 | [[ci-cd\|ci-cd]] |
| R-206 | QA | 낮음 | 봇 부하 테스트 계획 없음 | SCN-06 AI 20기 수동 절차만 | 자동 봇 클라이언트 계획 수립 | [[multiplayer-testing\|멀티플레이-테스트]] |
| R-207 | QA | 낮음 | DC 버퍼 초과 감지가 런타임 LogError 뿐 | 자동 테스트 없어 인지 못할 수 있음 | DCPerformanceProfiler를 PlayMode 테스트로 연결 | [[performance-budget\|성능-예산]] |
| R-208 | 보안 | 낮음 | 민감 데이터 전용 서버 없음 | 인벤토리·진행도 클라 파일 저장(조작 가능) | 장기: 진행도 서버 저장 / 단기: 저장 파일 서명·검사 | [[anti-cheat\|안티치트]] |
| R-209 | 인프라 | 낮음 | 매치메이킹 없음 | 방 코드/로비 리스트 수동 탐색 | EA 허용, 성장 후 개선 | [[server-hosting\|서버-호스팅]] |
| R-210 | 운영 | 낮음 | app_build VDF 미작성 | 업로드 절차 문서화 없음 | 첫 업로드 전 VDF 템플릿 작성·저장 | [[steamworks-admin\|steamworks-행정]] |
| R-211 | Steam | 낮음 | Win32/Posix DLL 플랫폼 설정 미검증 | Inspector 플랫폼 설정 코드 미확인 | 플랫폼 설정 확인(빌드 포함 오류 방지) | [[steamworks-integration\|steamworks-통합]] |
| R-212 | Steam | 낮음 | `InitRelayNetworkAccess()` 지연 미처리 | 준비 완료 전 연결 시 첫 호스팅 지연·실패 | 완료 콜백 대기 로직 추가 | [[steamworks-integration\|steamworks-통합]] |
| R-213 | Steam | 낮음 | 로비 검색 필터 신뢰도 (소프트 필터) | `GameUniqueId` 동일 설정 시 노출 | 정식 AppID 교체로 근본 해결 | [[lobby-matchmaking\|로비-매치메이킹]] |
| R-214 | Steam | 낮음 | Steam Cloud 할당량 계획 없음 | 기본 100MB, 저장 크기 추정 없음 | 저장 크기 추정 후 할당 요청 시점 결정 | [[steam-cloud\|steam-cloud]] |
| R-215 | Steam | 낮음 | 멀티 환경 도전과제 해제 주체 미정의 | 호스트만 vs 전원 해제 정책 없음 | NGO 서버 권위와 연계해 정책 설계 | [[achievements-stats\|도전과제-통계]] |
| R-216 | Steam | 낮음 | 멀티플랫폼 Depot 미설계 | Win32/64/Posix DLL 전부 탑재, Depot 분리 없음 | Depot 분리로 불필요 플랫폼 파일 제외 | [[steam-build-pipeline\|steam-빌드-파이프라인]] |

---
← [[12-issues-hub|12 · 이슈·리스크 레지스터]] · [[index|인덱스]]
