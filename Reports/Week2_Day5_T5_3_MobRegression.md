# Week 2 Day 5 T5.3 — Mob 회귀 테스트 결과 (v4)

> **작성**: 2026-04-27 10:02:36
> **도구**: `Week2_T5_3_MobRegressionRunner` v4
> **종합 판정**: ❌ FAIL
> **Stub Mode**: OFF

## 1. 통과 기준

| 지표 | 기준 | 측정값 | 판정 |
|---|---|---|---|
| Avg FPS | ≥60 | 3.1 | ❌ |
| Min FPS | ≥45 | 0.2 | ❌ |
| 에러 | =0 | 1 | ❌ |
| 경고 | ≤5 | 0 | ✅ |
| Brain | ≥15 | 15 | ✅ |
| Manager 의존성 | 5/5 | 5/5 | ✅ |
| Swarm | 시각 | - | ❌ |
| Duel | 시각 | - | ❌ |
| Phalanx | 시각 | - | ❌ |
| AttackToken | 시각 | - | ⚠️ |
| Week 1 무수정 | git diff | - | ❌ |

## 2. 환경

- Duration: 30.0s, Frames: 70, Brains: 15, Transitions: 0, Stub: OFF

## 3. 상태 분포

| State | Count |
|---|---|
| Idle | 15 |

## 4. Fear 분포

| Faction | Count | Avg | Min | Max |
|---|---|---|---|---|
| goblin | 5 | 0.200 | 0.200 | 0.200 |
| orc | 5 | 0.250 | 0.250 | 0.250 |
| skeleton | 5 | 0.200 | 0.200 | 0.200 |

## 5. PanicChain

⚠️ 미트리거.

## 6. Console 에러

- `Screen Space Global Illumination URP: Material is not using Hidden/Lighting/ScreenSpaceGlobalIllumination shader.`

## 8. Week 3 시사점

- ❌ 회귀 의심
  - FPS 저하
  - 에러 1건
  - GroupPolicy 시각 미통과
  - Week 1 파일 수정

## 9. 매니저 의존성 검증 (v4 — 5종)

| 매니저 | 상태 |
|---|---|
| WorldItemDatabase | ✅ |
| WorldUtilityManager | ✅ |
| WorldGameStateManager | ✅ |
| WorldSaveGameManager | ✅ |
| WorldAIManager | ✅ |

✅ 5종 모두 활성. 실제 환경 동등.

---
*핸드오버 §2.4 갱신용.*
