---
title: 성능-예산
tags: [qa]
status: done
source:
  - Assets/Scripts/pB-4/week0_TerrainDC/DCPerformanceProfiler.cs
  - Assets/Scripts/Utilities/Cave Genderator/CaveTerrainConfig.cs
  - Assets/Scripts/pB-4/week8_Integration/PipelineBenchmark.cs
  - Assets/Scripts/pB-4/week2_Tooling/Editor/Week2_T5_3_MobRegressionRunner.cs
  - Reports/archive/2026-04_week2/Week2_Day5_T5_3_MobRegression.md
verified: 2026-06-15
---

# 성능-예산

지형 DC(Dual Contouring) 버퍼와 FPS 기준이 코드에 명시되어 있다. 네트워크 대역폭·메모리·GPU 프레임 예산은 미정의.

## 현황 (pB)

**지형 DC GPU 버퍼 예산**

- `Assets/Scripts/pB-4/week0_TerrainDC/DCPerformanceProfiler.cs` L58: `maxGpuBufferBytes = 50L * 1024 * 1024` (50MB) — Week 0 마일스톤.
- `Assets/Scripts/Utilities/Cave Genderator/CaveTerrainConfig.cs` L471-473: 런타임 유효성 검사 — 청크당 예상 DC 버퍼가 50MB 초과 시 `Debug.LogError("[CaveTerrainConfig] 청크당 예상 DC 버퍼 {mem:F1}MB > 50MB 마일스톤 초과!")` 발화.
- `Assets/Scripts/pB-4/week8_Integration/PipelineBenchmark.cs` L13: GPU 버퍼 메모리 50MB 이하 기준 동일 명시.
- NavMesh 재생성 시간 기준: `maxNavMeshRebuildMs = 200f` (200ms) — `DCPerformanceProfiler.cs` L61.

**FPS 기준 (Mob 회귀 테스트)**

- `Assets/Scripts/pB-4/week2_Tooling/Editor/Week2_T5_3_MobRegressionRunner.cs` L30-31: `MIN_FPS_PASS = 60f`, `MIN_MIN_FPS_PASS = 45f`.
- 2026-04-27 실측(`Reports/archive/2026-04_week2/Week2_Day5_T5_3_MobRegression.md`): Avg FPS 3.1(기준 ≥60 ❌), Min FPS 0.2(기준 ≥45 ❌) — 당시 DC 파이프라인 초기 구현 부하로 FAIL. 이후 개선 여부 미재측.

**프로파일링 도구**

- `DCPerformanceProfiler`: 청크별 버텍스 수, GPU 버퍼, NavMesh 타이밍 자동 기록(히스토리 30개).
- Unity Profiler Network 모듈: Multiplayer Tools 2.2.3 설치로 사용 가능. 오브젝트별·변수별 대역폭 분해.
- RNSM HUD: RTT, Bytes Sent/Received, Network Objects 실시간 표시.
- 출력: `counters_final.csv`(패킷 카운터), `soak_summary.md`(soak 통계).

## 설계·결정

- Week 0에서 GPU 버퍼 50MB, NavMesh 200ms를 마일스톤 기준으로 설정. 코드와 에디터 양쪽에 경고 내장.
- FPS 기준(60/45)은 Editor Play Mode 기준이며 빌드 환경 측정 아님.
- DC 파이프라인이 복잡해질수록 GPU 버퍼가 50MB 초과할 수 있어 `CaveTerrainConfig.EstimatedDCBufferMB` 계산식으로 사전 경고.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | FPS 기준 미재측정 | 2026-04-27 측정 후 DC 개선이 있었으나 재측정 기록 없음 | MobRegressionRunner 재실행해 최신 FPS 기준 달성 여부 확인 |
| 높음 | 네트워크 대역폭 예산 미정의 | SCN-06 절차는 있으나 합격 기준 수치(KB/s 상한) 미설정 | 2인 세션 후 베이스라인 실측 → 예산 정의 |
| 중간 | 메모리 예산(RAM) 미정의 | GPU 버퍼(50MB)는 정의됐으나 게임 전체 메모리 상한 없음 | 출시 전 Unity Memory Profiler로 베이스라인 측정·문서화 |
| 중간 | GPU 프레임 시간 예산 없음 | CPU 프레임(FPS)만 있고 GPU 파이프라인 시간 목표 없음 | GPU 프레임 16ms(60FPS) 기준으로 Profiler 측정 |
| 중간 | 빌드 환경 FPS 미측정 | Editor Play Mode 기준 FPS — 빌드 성능과 괴리 가능 | StandaloneWindows64 빌드 실기기 측정 추가 |
| 낮음 | DC 버퍼 초과 감지가 런타임 LogError 뿐 | 자동 테스트 없어 인지 못할 수 있음 | DCPerformanceProfiler를 PlayMode 테스트로 연결 |

## 관련 문서

- [[test-framework|테스트-프레임워크]]
- [[multiplayer-testing|멀티플레이-테스트]]

---
← [[08-qa-testing-hub|08 · 테스트 & 품질]] · [[index|인덱스]]
