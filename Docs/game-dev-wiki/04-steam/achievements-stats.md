---
title: 도전과제-통계
tags: [steam]
status: decided
source:
  - Assets/Plugins/Facepunch/Facepunch.Steamworks.Win64.xml
verified: 2026-06-15
---

# 도전과제-통계

Steam Achievements(도전과제) 및 Stats(통계) 해제·누적 연동.

## 현황 (pB)

**미구현**.

`Assets/Scripts/` 전체를 `Achievement|SteamUserStats|UserStats` 패턴으로 Grep한 결과 게임 코드에 해당 호출이 없다. 결과는 Facepunch DLL의 XML 문서 파일에서만 등장한다.

Steamworks 파트너 대시보드에 도전과제/통계 항목 자체가 정의되어 있는지도 확인되지 않음.

Facepunch.Steamworks 에는 `SteamUserStats.AddStat(name, value)`, `SteamUserStats.SetAchievement(name)`, `SteamUserStats.StoreStats()` 등의 API가 탑재 DLL에 포함돼 있어 코드만 추가하면 사용 가능하다.

## 설계·결정

미결정. 출시 전 최소 아래 항목이 필요하다.

1. **도전과제 정의**: Steamworks 파트너 대시보드에서 내부 ID(`cave_complete` 등)·표시 이름·아이콘 등록.
2. **해제 트리거 추상화**: 게임 이벤트(몬스터 처치, 씬 클리어 등) → `SteamUserStats.SetAchievement("id") + StoreStats()` 호출 레이어. 이벤트 시스템이 있다면 해당 버스에 핸들러 추가.
3. **통계 누적**: 다회 플레이에 걸쳐 누적되는 수치(사망 횟수, 탐험 거리 등)는 `SteamUserStats.AddStat` + 세션 종료 시 `StoreStats` 패턴.

권고: EA 직전 최소 5개 기념비적 도전과제(튜토리얼 완료, 첫 보스 처치 등)라도 먼저 정의하고 해제 흐름을 검증.

## ⚠ 비판·리스크

**[높음] 도전과제 없는 Steam 출시는 검색 노출 불이익**: Steam 상점 페이지의 도전과제 배지는 플레이어 관심을 높이는 기본 요소다. 출시 이후 추가 가능하나, EA 단계에서도 없으면 리뷰에서 지적받음.

**[중간] `StoreStats()` 누락 시 통계·도전과제 미저장**: Facepunch API 특성상 `SetAchievement`/`AddStat` 후 반드시 `StoreStats()`를 호출해야 Steam 서버에 기록된다. 게임 종료나 씬 전환 타이밍에 호출 누락 시 데이터 유실.

**[낮음] 멀티 환경에서 해제 주체 미정의**: 코옵 세션에서 특정 도전과제를 호스트만 해제할 것인가, 참여 클라이언트 모두 해제할 것인가에 대한 정책이 없다. NGO 서버 권위 모델과 맞물려 설계 필요.

## 관련 문서

- [[steamworks-integration|steamworks-통합]]
- [[04-steam-hub|04 · Steam 통합]]

---
← [[04-steam-hub|04 · Steam 통합 (Steamworks)]] · [[index|인덱스]]
