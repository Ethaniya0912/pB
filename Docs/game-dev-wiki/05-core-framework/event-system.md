---
title: 이벤트-시스템
tags: [framework, ai]
status: done
source:
  - Assets/Scripts/pB-4/week0_Core/EventBus.cs
  - Assets/Scripts/Character/CharacterEventManager.cs
  - Assets/Scripts/Character/Player/PlayerEventManager.cs
  - Assets/Scripts/Character/Ai/AICharacterEventManager.cs
verified: 2026-06-15
---

# 이벤트-시스템

두 개의 독립된 이벤트 채널이 존재한다. pB-4 AI 서브시스템 전용 `EventBus`(정적 멀티캐스트 델리게이트)와 캐릭터 도메인 전용 `CharacterEventManager`(컴포넌트 기반) 가 완전히 분리된 구조다. 게임 전역 통합 이벤트 버스는 없다.

## 현황 (pB)

### pB-4 EventBus (`Assets/Scripts/pB-4/week0_Core/EventBus.cs`)
AI·팩션·시나리오 도메인 연결을 위한 정적 이벤트 버스. **Week 0에서 동결 — 이벤트 서명 변경 금지** 주석 명시.

| 채널 # | 이벤트 | 발행자 → 구독자 |
|---|---|---|
| 1 | `OnTerrainTagChanged(IReadOnlyList<string>)` | 지형 태그 변경 → AI 전술 분기 |
| 2 | `OnArcPhaseChanged(arcId, phaseIndex)` | 시나리오 아크 → AI 목표 갱신 |
| 3 | `OnIncidentRecorded(incidentId)` | 사건 기록 → 저널 자동 기술 |
| 4 | `OnEscalationTriggered(factionId, level)` | 팩션 에스컬레이션 |
| 5 | `OnFactionStateChanged(factionId, FactionWorldState)` | 팩션 상태 전파 |
| 6 | `OnBiomeSpawnCompleted(chunkIndex)` | 바이옴 스폰 완료 |
| 7 | `OnSoundEmitted(Vector3, SoundType, float)` | 소리 이벤트 → AI 지각 |
| 8 | `OnFactionDetectedPlayer(Transform)` | 팩션 플레이어 감지 |
| 9 | `OnTrustTierChanged(npcId, oldTier, newTier)` | NPC 신뢰 단계 전이 |
| 10 | `OnKarmaChanged(playerId, old, new, reason)` | 카르마 변화 |
| 11 | `OnKarmaTierChanged(playerId, oldTier, newTier)` | 카르마 등급 전이 |
| 12 | `OnAlignmentChanged(npcId, old, new, reason)` | NPC 진영 전이 |
| 13 | `OnSpeechTrigger(triggerId, ctx)` | 대화 트리거 발행 → SpeechDispatcher |

- `EventBus.ClearAll()` 로 씬 전환 시 전 구독 강제 해제 설계.
- 구독자는 `OnEnable`/`OnDisable` 에서 짝 맞춤 의무 (주석 명시).

### 캐릭터 이벤트 (`CharacterEventManager`)
- `Assets/Scripts/Character/CharacterEventManager.cs` — 플레이어·AI 공통 기반 이벤트 처리
- `PlayerEventManager`, `AICharacterEventManager` 로 상속 확장
- UnityEvent 또는 직접 메서드 호출 방식으로 추정 (코드 상세 미열람)

### 주요 이벤트 통합 부재
- 세이브/로드 완료, UI 상태 전환, 멀티플레이 세션 이벤트는 `EventBus` 로 전달되지 않음 — 직접 함수 호출 체인.
- `WorldGameStateManager`, `TitleScreenManager` 와 `EventBus` 는 연결 없음.

## 설계·결정

- AI 서브시스템과 게임 코어를 분리: pB-4 AI 도메인은 `EventBus` 를 통해 코어 시스템(저장·씬 전환 등)을 직접 호출하지 않는다. 결합도 최소화 의도.
- 정적 이벤트: 씬 전환 없이 AI 상태가 유지되어야 하는 경우 적합. 단, 구독 누수 위험 동반.
- Week 0 동결: 멀티팀 작업에서 서명 변경이 연쇄 빌드 실패를 일으키므로 인터페이스 고정.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | **게임 전역 이벤트 버스 부재** | `EventBus` 는 pB-4 AI 전용. 세이브 완료·씬 전환·멀티플레이 연결 이벤트가 직접 호출 체인으로 분산 — 리스너 추가 시마다 발행자 코드 수정 필요. | 게임 전역 이벤트 채널 별도 도입 또는 `EventBus` 확장 |
| 높음 | **정적 이벤트 구독 누수 위험** | `EventBus` 모든 채널이 `static event`. 구독자가 `OnDisable` 해제를 빠뜨리면 GC 루트로 남아 메모리 누수. `ClearAll()` 이 씬 전환 시점에 실제로 호출되는지 보장 코드 미확인. | `SceneManager.sceneUnloaded` 에서 `EventBus.ClearAll()` 자동 호출 보장 |
| 높음 | **네트워크 이벤트 경계 미정의** | `EventBus` 이벤트가 호스트·클라이언트 양쪽에서 발행·구독될 경우 권위 충돌 가능. 어느 이벤트가 서버 전용인지 규약 없음. | 이벤트별 `[ServerOnly]` / `[ClientOnly]` 주석 또는 래퍼 추가 |
| 보통 | **두 이벤트 시스템 간 브릿지 없음** | `CharacterEventManager`(컴포넌트 기반)와 `EventBus`(정적)가 완전 분리. AI가 플레이어 캐릭터 이벤트를 수신하려면 별도 어댑터 필요. | 이벤트 브릿지 어댑터 또는 단일 채널로 통합 계획 수립 |
| 낮음 | **타입 안전성 부분 부족** | `OnAlignmentChanged` 의 `oldAlignment`·`newAlignment` 가 `int` 형 — enum 대신 int 사용으로 컴파일 타임 오류 미감지. | 전용 enum `AlignmentType` 로 교체 |

## 관련 문서

- [[ui-framework|UI 프레임워크]]
- [[scene-manager|씬 매니저]]

---
← [[05-core-framework-hub|05 · 재사용 코어 프레임워크]] · [[index|인덱스]]
