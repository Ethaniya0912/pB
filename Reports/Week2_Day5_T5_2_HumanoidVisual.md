# Week 2 Day 5 T5.2 — Humanoid AI 시각 검증 결과 (자동화 v5)

> **작성**: 2026-04-27 11:02:45 (Editor 자동 출력 — 무인 자동화)
> **도구**: `HumanoidVisualAutoVerifier` + `HumanoidVisualStageSetup` v5
> **Auto Mode**: ON (One-Click 자동화)
> **종합 판정**: ⚠️ 부분 통과 (4/5 시나리오)

## 1. 자동 시나리오 검증 결과

| # | 시나리오 | 자동 검증 | 검증 방법 |
|---|---|---|---|
| 1 | Coward 도주 | ❌ | Humanoid가 player에서 멀어지는 거리 변화 (Archetype_Coward 효과) |
| 2 | Friendly 강제 | ✅ | Speech 발화 + Alignment 전이 로그 |
| 3 | Hostile 거부 | ✅ | Alignment 전이 카운트 |
| 4 | Companion 인사 | ✅ | DialogueRenderer 발화 로그 |
| 5 | Karma 자동 체인 | ✅ | Karma Tier 전이 + Alignment 전이 |

**Auto-Verify 통과**: 4/5

## 2. Humanoid 동작 검증

- Humanoid 이동: ❌ (0.00m)
- 시작 위치: (0.00, 0.00, 0.00)
- 종료 위치: (0.00, 0.00, 0.00)
- DialogueRenderer 부착: ✅
- SpeechBubble 인스턴스: ✅

## 3. 로그 패턴 카운트

- DialogueRenderer 발화: 4회
- SpeechAssembler/Dispatcher: 9회
- Alignment 전이: 21회
- Karma Tier 전이: 1회

## 4. Console 에러/경고

- 에러: 6
- 경고: 3

**에러 메시지** (최대 20):
- `NullReferenceException: Object reference not set to an instance of an object`
- `[UtilityMasterFormula] Skeleton_Humanoid_01: actions 미로드. Inspector의 externalConfig 또는 HumanoidBootstrapper.defaultActionConfig 할당 필요.`
- `[TagResolver] Skeleton_Humanoid_01: rules 미로드. Inspector의 externalRuleLibrary 또는 HumanoidBootstrapper.defaultTagRules 할당 필요.`
- `Screen Space Global Illumination URP: Material is not using Hidden/Lighting/ScreenSpaceGlobalIllumination shader.`
- `<color=yellow>[AI Equip 진단:오른손 자동]</color> ❌ WorldItemDatabase.Instance = NULL! 씬에 WorldItemDatabase가 없습니다.`
- `<color=yellow>[AI Equip 검증:오른손]</color> ❌ 'WeaponSocket_Right'에 무기 프리팹이 생성되지 않았습니다!
  가능한 원인:
  1. weapon.weaponModel이 NULL → SO에 프리팹 미할당
  2. WorldItemDatabase에서 ID=14를 못 찾음
  3. rightHandSlot/leftHa…`

## 5. 시나리오 호출 로그

- ④ Companion 강제 @ 3.0s
- ② Friendly 강제 @ 7.1s
- ③ Hostile 강제 @ 11.1s
- ⑤ Karma +80 (Saint) @ 15.1s

## 6. Week 3 진입 시사점

- ⚠️ 일부 시나리오 자동 검증 미통과
  - 시나리오 1 Coward 도주 미검출 (Archetype_Coward 효과 약함)
  - Humanoid 이동 검출 안 됨 (NavMesh + BT 트리거 의심)
  - Console 에러 6건 — §4 참조

---
*핸드오버 §2.3 갱신용. 자동 검증 + 시각 추가 확인 권장.*
