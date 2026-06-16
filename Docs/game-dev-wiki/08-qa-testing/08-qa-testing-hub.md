---
title: 08-qa-testing-hub
tags: [moc, qa]
status: done
source: []
verified: 2026-06-15
---

# 08 · 테스트 & 품질

PlayMode 어셈블리가 있으나 테스트 파일 0건. 멀티플레이 실기기 2인 측정 미완료. 성능 예산 일부 정의됨.

## 문서

- [[test-framework|테스트-프레임워크]] — PlayMode asmdef 존재, 파일 0건, CI 미연결
- [[multiplayer-testing|멀티플레이-테스트]] — MPPM 2.0.1, Steam self-connect 차단, 2인 필수
- [[performance-budget|성능-예산]] — DC 버퍼 50MB·NavMesh 200ms·FPS 60/45 기준, 네트워크 예산 미정의

## 핵심 현황 요약

| 항목 | 현황 |
|---|---|
| PlayMode 테스트 수 | 0건 (파일 미존재) |
| EditMode 테스트 | 미구성 |
| 하네스 훅 테스트 | bash 픽스처 16케이스 (로컬 수동) |
| 2인 실기기 측정 | 미완료 (Steam self-connect 차단) |
| DC 버퍼 예산 | 50MB (코드 경고 내장) |
| FPS 기준 | ≥60 Avg / ≥45 Min (Editor 기준, 재측정 필요) |

## ⚠ 가장 중요한 리스크

테스트 파일 0건 + CI 없음 조합은 멀티플레이 코드 회귀를 자동으로 감지할 수단이 전혀 없음을 의미한다. EA 전 최우선 해소 필요.

---
← [[index|인덱스]]
