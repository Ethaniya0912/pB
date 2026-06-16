---
title: 05-core-framework-hub
tags: [moc]
status: done
source: []
verified: 2026-06-15
---

# 05 · 재사용 코어 프레임워크

pB 코어 프레임워크 현황 요약. 전용 프레임워크 패키지는 없으며 역할별 매니저 클래스들의 조합으로 구성된다.

## 구현 현황 요약

| 시스템 | 상태 | 핵심 클래스 |
|---|---|---|
| 세이브-로드 | 구현 완료 | `WorldSaveGameManager`, `SaveFileDataWriter` |
| 씬 매니저 | 부분 구현 (전용 클래스 없음) | `WorldSaveGameManager.LoadWorldScene()` |
| UI 프레임워크 | 부분 구현 | `PlayerUIManager`, `LobbyUIManager` |
| Input 시스템 | 구현 완료 | `PlayerControls.inputactions` (Input System 1.17.0) |
| 이벤트 시스템 | 부분 구현 (AI 전용) | `EventBus` (pB-4 전용), `CharacterEventManager` |
| 오브젝트 풀링 | 미구현 (지형 청크 한정) | `CaveChunkManager` 내부 풀만 |
| 로컬라이제이션 | 미구현 | 하드코딩 한국어 문자열 |

## 문서

- [[event-system|이벤트-시스템]] — pB-4 전용 정적 EventBus + CharacterEventManager, 전역 버스 부재
- [[object-pooling|오브젝트-풀링]] — 범용 풀 미구현, 지형 청크 한정
- [[scene-manager|씬-매니저]] — WorldSaveGameManager 통합, 전용 클래스 없음
- [[save-load|세이브-로드]] — JSON 파일 기반, 5슬롯, Steam Cloud 미연동
- [[input-system|input-시스템]] — Unity Input System 1.17.0, PlayerControls.inputactions
- [[localization|로컬라이제이션]] — 미구현, 하드코딩 한국어
- [[ui-framework|ui-프레임워크]] — uGUI 기반, UI Toolkit 미사용

---
← [[index|인덱스]]
