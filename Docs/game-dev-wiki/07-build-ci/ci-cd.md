---
title: ci-cd
tags: [tooling]
status: done
source:
  - .github/
  - TDA.PB4.Tests.PlayMode.csproj
verified: 2026-06-15
---

# ci-cd

CI/CD 파이프라인 미구축. 빌드·테스트·배포는 전부 수동이다.

## 현황 (pB)

- `.github/` 디렉토리 없음(2026-06-15 실측). GitHub Actions 워크플로 0건.
- game-ci(Unity 공식 GitHub Actions), Unity Build Automation(구 Cloud Build) 미도입.
- Unity Test Runner 자동 실행 없음 — PlayMode 테스트(`TDA.PB4.Tests.PlayMode.csproj`)가 존재하지만 CI 파이프라인에 연결되지 않음.
- SteamPipe 업로드 자동화 없음 — 수동 `steamcmd` 실행.
- 하네스 내 훅 테스트(`.harness/hooks/tests/run.sh`)는 bash 픽스처 기반이며 로컬 수동 실행 전용. CI 연결 없음.

## 설계·결정

- 현재는 수동 개발 흐름(에디터 빌드 → 로컬 테스트 → 수동 Steam 업로드)을 유지.
- 테스트는 Unity Editor의 Test Runner(Window > General > Test Runner)에서 수동 실행.
- 팀 규모(소규모)·EA 전 단계를 이유로 자동화를 유보한 상태.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | CI 완전 부재 | `.github/` 없음 — 코드 머지 시 회귀 자동 감지 불가 | GitHub Actions + game-ci `unity-test-runner` 도입. PlayMode 테스트를 PR마다 자동 실행 |
| 높음 | PlayMode 테스트가 CI에 연결되지 않음 | `TDA.PB4.Tests.PlayMode.csproj` 존재하지만 자동 실행 없음 | CI 파이프라인에서 `-runTests -testPlatform playmode` 실행 |
| 중간 | Steam 업로드 자동화 없음 | EA 릴리즈 빈도 증가 시 수동 업로드 병목·오류 위험 | GitHub Actions에서 `steamcmd +login +run_app_build` 자동화 |
| 낮음 | 빌드 아티팩트 버전 추적 없음 | 어떤 커밋이 어느 빌드인지 Steam 대시보드에서 추적 불가 | 커밋 해시를 빌드 설명에 기록하는 스크립트 |

출시(EA) 전에 최소한 GitHub Actions로 테스트 자동화를 구성하지 않으면, 멀티플레이 코드 회귀를 코드 리뷰 단계에서 감지할 수단이 없다.

## 관련 문서

- [[build-automation|빌드-자동화]]
- [[test-framework|테스트-프레임워크]]

---
← [[07-build-ci-hub|07 · 빌드 & CI/CD]] · [[index|인덱스]]
