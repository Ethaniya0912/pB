---
title: 프로젝트-개요
tags: [overview]
status: done
source:
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Assets/Scripts/Networking/SteamClient.cs
  - Assets/Scenes/
  - Assets/Scripts/Utilities/Cave Genderator/
  - Assets/Scripts/Character/
  - CLAUDE.md
  - ProjectSettings/ProjectVersion.txt
  - Packages/manifest.json
verified: 2026-06-15
---

# 프로젝트-개요

**가칭 pC** — Unity 6.3 LTS 기반 PC/Steam 게임. **2-5인 PvE 코옵 다크·로우판타지 생존/던전 채굴 게임**(프로시저럴 동굴, 숄더뷰, 리얼리즘 지향). 기획 정보는 2026-06-15 확정(타겟 사양만 실측 후 확정).

## 기획 확정 (2026-06-15)
| 항목 | 값 |
|---|---|
| 정식 게임명 | **미정 (가칭 pC)** — 코드 식별자는 `PennutButterProject`, 화면 표기 "PennutButter 3"(잠정) |
| 장르 | 2-5인 **PvE 코옵** · 다크판타지/로우판타지 · 생존 · 던전 · 채굴 · 프로시저럴 던전(동굴) · 숄더뷰(TPS) · 리얼리즘 |
| 코어 루프 | 동굴 던전 탐험 → 채굴·자원 수집 → 생존(전투·관리) → 더 깊은 던전 (코옵 협동) |
| 최대 동접 | **2-5명** (한 세션) · 몹 동시 **10마리 내외** |
| 플랫폼 | Steam PC (P2P relay, 전용 서버 없음) |
| 타겟 사양 | **실측 후 확정** — 아래 잠정 권장안 참조([[performance-budget|성능-예산]]) |

### 타겟 사양 (잠정 권장 — 실측 전)
> 커스텀 SSGI + GPGPU 동굴(Marching Cubes compute, 2,200+ shadow caster) + SteamAudio + 리얼리즘 렌더라 **GPU 부하가 높다**.
> [[performance-budget|성능-예산]] 의 FPS 실측(현재 일부 씬 60 미달)을 끝낸 뒤 확정해야 한다.
| 등급 | 잠정 |
|---|---|
| 최소 | GTX 1660 / RX 590 (6GB) · i5-9400 / Ryzen 5 2600 · 16GB · SSD · **SSGI 옵션 OFF 가정** |
| 권장 | RTX 3060 / RX 6600 XT (8GB+) · i5-12400 / Ryzen 5 5600 · 16GB · NVMe SSD · SSGI ON |
- 저사양 대응: SSGI·동굴 그림자 품질을 **옵션 토글**로 빼는 것을 권고(미구현 — 후속).

## 현황 (pB) — 코드 확인된 사실
| 항목 | 값 | 근거 |
|---|---|---|
| 엔진 | Unity 6000.3.1f1 (6.3 LTS) | `ProjectSettings/ProjectVersion.txt` |
| Netcode | NGO 2.7.0 + Steam P2P relay | `manifest.json`, `Networking/SteamP2PRelayTransport.cs` |
| 렌더 | URP 17.3.0 + 커스텀 SSGI | `manifest.json`, SSGIURP |
| Steam | Facepunch.Steamworks · **AppID 480(테스트용)** | `Networking/SteamClient.cs` |
| 코드상 maxPlayers | **4** ⚠ 기획(2-5명)과 불일치 | `Networking/SteamLobbyManager.cs` |

### 핵심 시스템 (코드 확인)
| 시스템 | 경로 |
|---|---|
| Steam 멀티플레이 | `Assets/Scripts/Networking/` |
| 캐릭터/AI | `Assets/Scripts/Character/` · `Assets/Scripts/pB-4/`(BT+Blackboard+EventBus) |
| 프로시저럴 동굴 | `Assets/Scripts/Utilities/Cave Genderator/`(Marching Cubes + Compute) |
| 인벤토리·채굴 | `Assets/Scripts/Inventory/` |
| 세이브 | `Assets/Scripts/Game Saving/` · `World Manager/WorldSaveGameManager.cs` |
| 네트워크 진단 | `Assets/Scripts/Utilities/NetDiagnostics/` |

### 씬 (`Assets/Scenes/`)
- 프로덕션: `Scene_main_menu_01`(타이틀) · `Scene_World_01`(메인 월드)
- 테스트/레거시: `Scene_pB2`·`Scene_S6/S11/S13`·`Wk3_NaturalEmergence`·`Scene_AI_Test`·`AI TEST`·`Scene_Fog`·`Scene_Simple_map_generator` (빌드 제외 여부 정리 필요)

## 설계·결정
- **Unity 6.3 LTS**: NGO 2.x 공식 지원, URP 17.x, 장기 지원.
- **Steam P2P (전용 서버 0원)**: 2-5인 소규모 코옵에 적합 — 단, 호스트 이탈·치팅 취약은 트레이드오프([[network-topology|토폴로지]]·[[anti-cheat|안티치트]]).
- **프로시저럴 동굴(GPGPU)**: Marching Cubes + Compute Shader로 실시간 생성 — 채굴·던전의 핵심.
- **pB-4 주차 레이어링**: `Assets/Scripts/pB-4/week0~week8` 기능 단위 누적.

## ⚠ 비판·리스크
- **심각도 높음 — 동접 불일치**: 기획은 2-5명인데 코드 `maxPlayers=4`. 5인 지원하려면 로비·대역폭·스폰을 5인 기준으로 재검토해야 한다(NGO·UI·Steam 로비 정원). → `maxPlayers` 상향 + 5인 부하 측정.
- **심각도 높음 — AppID 480**: Spacewar 공용 ID. 출시 불가·타인 로비 혼재. 실 AppID 등록·교체 필요([[steamworks-integration|Steamworks]]).
- **심각도 높음 — 타겟 사양 미실측**: SSGI+GPGPU 동굴 부하가 큰데 FPS 실측이 일부 씬 60 미달([[performance-budget|성능-예산]]). 최소 사양을 약속하기 전 프로파일 필수.
- **심각도 보통 — 게임명 미정**: 코드 식별자(`PennutButterProject`)와 가칭(pC)이 혼재. 정식명 확정 후 식별자·스토어명 정리.
- **심각도 보통 — 폴더 오타**: `Cave Genderator`(Generator 오타) — 검색·참조 혼선.
- **심각도 낮음 — 씬 정리**: 테스트/레거시 씬 다수, 빌드 포함 여부 불명확.

## 관련 문서
- [[prep-checklist|사전준비-체크리스트]] · [[decision-priority|의사결정-우선순위]]
- [[network-topology|네트워크-토폴로지]] · [[performance-budget|성능-예산]]
- [[project-structure|프로젝트-구조]] · [[scriptableobject-architecture|SO-아키텍처]]

---
← [[00-overview-hub|00 · 개요 & 우선순위]] · [[index|인덱스]]
