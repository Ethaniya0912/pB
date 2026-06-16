---
title: 사전준비-체크리스트
tags: [overview, checklist]
status: done
source:
  - Packages/manifest.json
  - Assets/Scripts/Networking/
  - Assets/Scripts/Utilities/NetDiagnostics/
  - .gitattributes
  - .gitignore
  - ProjectSettings/EditorSettings.asset
verified: 2026-06-15
---

# 사전준비-체크리스트

본 개발 진입 전 준비 항목 마스터 리스트. **pB 실제 상태를 반영하여 완료/미완료/부분을 표기한다.**

## 현황 (pB)

| 항목 | 상태 | 근거 |
|---|---|---|
| Git 초기화 | 완료 | 레포 운영 중 |
| Git LFS 세팅 | **미완료** | `.gitattributes`: 바이너리 패턴 없음 |
| Unity `.gitignore` | 완료 | `.gitignore`: Library/Temp/Build/Logs 제외 확인 |
| `.gitattributes` LFS 트래킹 | **미완료** | 셸 스크립트 eol=lf 만 설정 |
| Force Text 직렬화 | 완료 | `EditorSettings.asset` SerializationMode: 2 |
| 프로젝트 폴더 구조 | 완료(부분) | `Assets/Scripts/` 아래 기능별 폴더 존재 |
| Assembly Definition 분리 | **미완료** | 게임 코드 대부분 Assembly-CSharp 단일 어셈블리 |
| 코딩 컨벤션 정의 | 완료(부분) | `.harness/_conventions.md` §1 파일명=클래스명 규칙 |
| 렌더 파이프라인 선택 | 완료 | URP 17.3.0 |
| Netcode 솔루션 선택 | 완료 | NGO 2.7.0 + SteamP2PRelayTransport |
| Steam 통합 | 완료(부분) | SteamClient/SteamLobbyManager 구현, 앱ID 480(개발용) |
| 멀티플레이 테스트 환경 | 부분 완료 | `com.unity.multiplayer.playmode` 설치, 절차서 없음 |
| 성능 예산 정의 | **미완료** | 타겟 사양·FPS 목표 미문서화 |
| 안티치트 | **미완료** | 미구현 |

## 카테고리별 진입점

### 기반 (Foundation)
- [[version-control-git-lfs|버전관리-git-lfs]] — LFS 미설정, 즉시 조치 필요
- [[project-structure|프로젝트-구조]] — Scripts/ 폴더 트리 실측됨
- [[coding-conventions|코딩-컨벤션]] — 파일명=클래스명 규칙 등 _conventions.md 기반
- [[assembly-definition|assembly-definition]] — 게임 코드 단일 어셈블리 현황 + 분리 로드맵

### 렌더 & 아키텍처
- [[render-pipeline|렌더-파이프라인]]
- [[ecs-vs-oop|ecs-vs-oop]]
- [[di-container|di-컨테이너]]

### 네트워크
- [[network-topology|네트워크-토폴로지]]
- [[state-sync|상태-동기화]]
- [[prediction-reconciliation|예측-재조정-보간]]

### Steam
- [[steamworks-integration|steamworks-통합]]
- [[lobby-matchmaking|로비-매치메이킹]]
- [[steam-cloud|steam-cloud]]

### 시스템
- [[event-system|이벤트-시스템]]
- [[object-pooling|오브젝트-풀링]]
- [[save-load|세이브-로드]]

### 품질
- [[multiplayer-testing|멀티플레이-테스트]]
- [[performance-budget|성능-예산]]
- [[anti-cheat|안티치트]]

## ⚠ 비판·리스크

- **심각도 높음**: LFS 설정 누락 — 개발 초기인 지금 fix 하지 않으면 후속 히스토리 재작성이 매우 어렵다.
- **심각도 높음**: Assembly Definition이 게임 코드 전체를 단일 어셈블리(Assembly-CSharp)에 담는다. 컴파일 시간 증가 및 순환 의존 감지 불가. 서드파티 6개만 asmdef로 분리됨.
- **심각도 보통**: 성능 예산(타겟 FPS/해상도/사양)이 미정의 상태. 그래픽 최적화 방향을 정하지 못하고 있다.
- **심각도 낮음**: 안티치트는 코옵 PvE면 우선순위 낮지만, Steam 앱ID가 480(개발용)인 채로 릴리즈하면 안 된다.

## 관련 문서

- [[project-overview|프로젝트-개요]]
- [[decision-priority|의사결정-우선순위]]

---
← [[00-overview-hub|00 · 개요 & 우선순위]] · [[index|인덱스]]
