---
title: 테스트-프레임워크
tags: [qa, tooling]
status: done
source:
  - TDA.PB4.Tests.PlayMode.csproj
  - Assets/Tests/PlayMode/TDA.PB4.Tests.PlayMode.asmdef
  - .harness/hooks/tests/run.sh
verified: 2026-06-15
---

# 테스트-프레임워크

Unity Test Framework(NUnit 기반) PlayMode 어셈블리가 존재하나, 실제 테스트 파일은 비어 있고 CI 연결도 없다.

## 현황 (pB)

**PlayMode 어셈블리**

- `TDA.PB4.Tests.PlayMode.csproj` 존재. 루트 네임스페이스 `TDA.PB4.Tests.PlayMode`, Unity 6000.3.1f1, NUnit 참조 포함.
- csproj 내 `Compile` 항목: `Assets\Tests\PlayMode\Week2DynamicTests.cs` 단 1건 선언.
- 실제 파일 시스템 조사 결과: `Assets/Tests/PlayMode/` 디렉토리는 비어 있음(`.meta`만 존재). `Week2DynamicTests.cs` 파일 미존재(2026-06-15 실측).
- 결론: csproj는 생성됐으나 테스트 파일이 삭제 혹은 미커밋 상태 — 사실상 테스트 0건.

**EditMode 어셈블리**

- `Assembly-CSharp-Editor.csproj` 존재하나 전용 EditMode 테스트 asmdef 없음. EditMode 테스트 미구성.

**하네스 훅 테스트**

- `.harness/hooks/tests/run.sh`: bash 픽스처 기반 훅 단위 테스트 러너. `guard-unity-cli.sh`, `post-asset-edit.sh` 훅을 JSON 픽스처로 검증 (16개 케이스).
- 로컬 수동 실행 전용 — CI 파이프라인 연결 없음.

**성능 회귀 도구**

- `Assets/Scripts/pB-4/week2_Tooling/Editor/Week2_T5_3_MobRegressionRunner.cs`: Editor 실행 기반 Mob 회귀 테스트 도구. 합격 기준: Avg FPS ≥60, Min FPS ≥45. 2026-04-27 실측 결과 FAIL(Avg 3.1 FPS, Min 0.2 FPS) — 당시 지형 DC 부하 과부하.

## 설계·결정

- PlayMode 어셈블리를 별도 asmdef(`TDA.PB4.Tests.PlayMode`)로 분리하는 설계는 올바름. Unity Test Framework 규약 준수.
- 하네스 훅 테스트는 Claude 워크플로 안전망(위험 명령 차단) 검증 목적으로 설계됨 — 게임 로직 테스트와 별개.
- 성능 회귀 도구는 Editor 전용 `[MenuItem]` 방식 — 수동 실행만 가능.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | 테스트 파일 실질 0건 | `Assets/Tests/PlayMode/` 비어 있음 — csproj 선언과 불일치 | `Week2DynamicTests.cs` 복원 또는 실질 PlayMode 테스트 신규 작성 |
| 높음 | EditMode 테스트 부재 | EditMode asmdef 없음 — 순수 로직 단위 테스트 불가 | `TDA.PB4.Tests.EditMode` asmdef 추가, 유틸·데이터 로직 커버 |
| 높음 | CI 연결 없음 | 테스트가 있어도 자동 실행 수단 없음 | GitHub Actions + `-runTests -testPlatform playmode` |
| 중간 | 코드 커버리지 미측정 | 커버리지 리포트 없음 — 테스트 없는 경로 미파악 | game-ci `unity-test-runner`의 `--coverage` 옵션 |
| 중간 | 성능 테스트 CI 미연결 | MobRegressionRunner가 수동 실행 전용 | Editor `-executeMethod` 로 CI 자동화 |

## 관련 문서

- [[ci-cd|ci-cd]]
- [[multiplayer-testing|멀티플레이-테스트]]
- [[performance-budget|성능-예산]]

---
← [[08-qa-testing-hub|08 · 테스트 & 품질]] · [[index|인덱스]]
