# 용어 사전 인덱스 (`_glossary.md`)

> `.harness` 산출물에 등장하는 용어의 **누적 사전**. 사이클이 끝나도 남아 재사용된다.
> 등록 규칙: [[_conventions#8. 용어 사전 (glossary) — 누적 지식 베이스|_conventions §8]] ·
> 새 용어 = `<분류>/<용어>.md` 생성([[_template]] 복사) + **여기 분류 표에 한 줄 추가**.
> 각 항목은 "어떤 개념인지(쉬운 설명 포함) + 어느 사이클 어느 본문에서 쓰였는지"를 담고,
> 사이클 문서와 **양방향**으로 위키링크된다(Foam 그래프/백링크로 탐색).
> 프로젝트 설계·비판 지식베이스: [game-dev-wiki](../../Docs/game-dev-wiki/index.md) — 용어가 위키에서 깊게 다뤄지면 상호 참조한다(위키는 별도 워크스페이스라 상대경로 링크).

## concept — 개념·지표·시나리오·심볼
| 용어 | 한 줄 | 첫 등장 |
|---|---|---|
| [[RNSM]] | 게임 화면 위 실시간 네트워크 계기판 | 2026-06-12_netcode |
| [[RTT]] | 패킷 왕복 시간(ms) — 랙의 핵심 수치 | 2026-06-12_netcode |
| [[M-지표]] | 네트워크 품질 측정 지표 11종(M1~M11) | 2026-06-12_netcode |
| [[SCN-시나리오]] | 공정 비교용 표준 테스트 시나리오 7종 | 2026-06-12_netcode |
| [[베이스라인]] | 수정 전 최초 측정값(효과 입증의 기준점) | 2026-06-12_netcode |
| [[soak-테스트]] | 30~60분 장시간 방치 내구 테스트 | 2026-06-12_netcode |
| [[desync]] | 참가자 간 게임 상태가 어긋나는 현상 | 2026-06-12_netcode |
| [[NETCODE_DEBUG]] | 네트워크 디버그 로그 on/off 컴파일 심볼 | 2026-06-12_netcode |
| [[P0-P1-P2-이슈코드]] | 이슈 심각도 분류 라벨(P0 치명~P2 개선) | 2026-06-12_netcode |

## tool — 에디터 도구·외부 도구·CLI·프리셋
| 용어 | 한 줄 | 첫 등장 |
|---|---|---|
| [[PROF-프리셋]] | 네트워크 상태(양호/평균/열악) 흉내 스위치 — PROF-G/A/B | 2026-06-12_netcode |
| [[unity-cli]] | Unity Editor를 명령어로 제어하는 CLI(하네스의 손발) | (하네스 공통) |
| [[Clumsy]] | OS 수준 패킷 지연·손실 발생기(손실 측정 보완) | 2026-06-12_netcode |
| [[Network-Profiler]] | Unity Profiler의 네트워크 가계부 모듈 | 2026-06-12_netcode |

## script — 프로젝트 C# 클래스·컴포넌트
| 용어 | 한 줄 | 첫 등장 |
|---|---|---|
| [[NetEventLogger]] | 접속/해제 이벤트 CSV 기록기 (M3·M8) | 2026-06-12_netcode |
| [[VerdictLogger]] | 전투 판정 양측 CSV 기록기 (M5) | 2026-06-12_netcode |
| [[StateChecksumV0]] | 30초 주기 상태 체크섬 비교기 (M11·desync 감지) | 2026-06-12_netcode |
| [[RnsmHud]] | RNSM HUD 런타임 부착·구성 컴포넌트 | 2026-06-12_netcode |
| [[NetSimProfiles]] | PROF 프리셋 수치 정의·활성 상태 관리 | 2026-06-12_netcode |
| [[NetSimController]] | F8 PROF 순환 토글 + 화면 라벨 (회귀 수정으로 단독 파일 분리) | 2026-06-12_netcode |
| [[NetDiagnosticsBootstrap]] | 계측 컴포넌트 자동 부착 진입점 | 2026-06-12_netcode |
| [[SteamP2PRelayTransport]] | NGO↔Steam P2P 릴레이 전송 계층 | 2026-06-12_netcode |
| [[SoakHarness]] | F10 soak 수집기(30분 샘플→요약 md) | 2026-06-12_netcode |
| [[BoundaryEchoHarness]] | F9 페이로드 경계 스윕 하네스 (M2) | 2026-06-12_netcode |

## asset — 씬·프리팹·ScriptableObject 등
| 용어 | 한 줄 | 첫 등장 |
|---|---|---|
| _(아직 없음 — 씬/프리팹/SO 에셋이 사이클에 등장하면 등록)_ | | |

## package — 외부 패키지·라이브러리
| 용어 | 한 줄 | 첫 등장 |
|---|---|---|
| [[Multiplayer-Tools]] | Unity 공식 멀티플레이 계측 공구함(RNSM·Profiler) | 2026-06-12_netcode |
| [[NGO]] | Unity 공식 네트워킹 프레임워크(Netcode for GameObjects) | 2026-06-12_netcode |
| [[Facepunch-Steamworks]] | Steam API C# 래퍼(로비·P2P 소켓) | 2026-06-12_netcode |
