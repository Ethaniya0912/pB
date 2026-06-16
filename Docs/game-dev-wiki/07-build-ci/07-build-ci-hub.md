---
title: 07-build-ci-hub
tags: [moc, tooling]
status: done
source: []
verified: 2026-06-15
---

# 07 · 빌드 & CI/CD

pB는 현재 에디터 수동 빌드이며 CI/CD 파이프라인이 없다. `.github/` 디렉토리 미존재 확인(2026-06-15).

## 문서

- [[build-automation|빌드-자동화]] — 수동 빌드 현황, BuildPipeline 스크립트 부재
- [[ci-cd|ci-cd]] — GitHub Actions/game-ci 미도입, PlayMode 테스트 CI 연결 없음

## 핵심 현황 요약

| 항목 | 현황 |
|---|---|
| 빌드 방식 | Unity Editor 수동 (StandaloneWindows64) |
| CI 파이프라인 | 미구축 (.github/ 없음) |
| 자동화 테스트 연결 | 없음 |
| Steam 업로드 | 수동 SteamPipe |

## ⚠ 가장 중요한 리스크

CI 부재로 멀티플레이 코드 회귀가 PR 단계에서 감지되지 않는다. EA 출시 전 GitHub Actions + game-ci 도입이 필요하다.

---
← [[index|인덱스]]
