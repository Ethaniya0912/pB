---
title: 로컬라이제이션
tags: [framework, i18n]
status: done
source:
  - Packages/manifest.json
  - Assets/Scripts/Character/Player/Player UI/PlayerUIPopUpManager.cs
  - Assets/Scripts/UI/LobbyUIManager.cs
verified: 2026-06-15
---

# 로컬라이제이션

**미구현.** Unity Localization 패키지가 `manifest.json` 에 없고, 하드코딩된 한국어 문자열이 전 코드베이스에 산재한다.

## 현황 (pB)

### 패키지 상태
- `com.unity.localization` 미설치 (manifest.json 전체 확인).
- 설치된 관련 패키지 없음.

### 문자열 처리 현황
- UI 문자열 모두 코드 내 한국어 하드코딩.
  - 예: `PlayerUIPopUpManager` — `"YOU DIED"` (영문)
  - `LobbyUIManager` — `"지형 생성 현황: ... 완료"`, `"대기방"`, `"참여자 대기 중..."`, `"[방장]"` 등 한국어 직접 기재
- Debug 로그도 대부분 한국어.
- `TextMeshProUGUI` 에 한국어 폰트 어사인 여부 미확인.

### 키 관리 파이프라인
- 문자열 키 테이블 없음. 텍스트 변경 시 코드 직접 수정 필요.

## 설계·결정

- 현재 개발팀이 한국인이고 한국 시장 1차 출시 예정 → 로컬라이제이션을 EA 이후 과제로 연기한 것으로 추정.
- Steam 글로벌 기본 지원(영/일/중 번체) 여부는 미정.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | **로컬라이제이션 완전 부재** | 하드코딩 문자열이 수십 개 파일에 산재. 나중에 추출할수록 누락·불일치 위험 증가. Steam 글로벌 출시 시 수동 작업량 기하급수적 증가. | 지금이라도 `com.unity.localization` 설치 후 문자열 키화 시작 (한국어 단일 테이블로도 충분) |
| 높음 | **폰트 누락 위험** | `TextMeshProUGUI` 에 한국어 폰트가 제대로 임베드·어사인되지 않으면 런타임 폴백 폰트로 대체되어 시각적 깨짐 발생 가능. | TMP 폰트 에셋에 한국어 유니코드 범위 베이크 확인 |
| 보통 | **YOU DIED 텍스트 영문 혼용** | `PlayerUIPopUpManager` 의 `YOU DIED` 는 영문, 나머지 UI는 한국어 — 의도적인지 미확인. | 언어 정책 통일: 한국 1차 출시이면 한국어, 글로벌이면 로컬라이제이션 키 |
| 낮음 | **DEBUG 로그 한국어** | 디버그 출력이 한국어이면 해외 협력자·Unity 포럼 지원 요청 시 공유 어려움. | 디버그 로그는 영문 유지 권고 |

## 관련 문서

- [[data-pipeline|데이터 파이프라인]]
- [[ui-framework|UI 프레임워크]]

---
← [[05-core-framework-hub|05 · 재사용 코어 프레임워크]] · [[index|인덱스]]
