---
title: 안티치트
tags: [security, network]
status: done
source:
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Assets/Scripts/Character/CharacterCombatManager.cs
  - Assets/Scripts/Utilities/NetDiagnostics/VerdictLogger.cs
  - Assets/Scripts/Utilities/NetDiagnostics/SoakHarness.cs
  - Assets/Scripts/Utilities/NetDiagnostics/NetDiagnostics.cs
verified: 2026-06-15
---

# 안티치트

전용 안티치트 솔루션(VAC/EAC/BattlEye) 미구현. 호스트 권위 모델이 유일한 방어선이다.

## 현황 (pB)

**방어 체계**

- 권위 모델: 호스트가 서버(NGO IsServer = 호스트 머신) 역할. `CharacterCombatManager.cs` L213: `if (HasLocalAuthority)` 조건으로 전투 판정 서버(호스트) 측 처리.
- 데미지 체인: `VerdictLogger.cs` — 4지점에 계측 삽입. Step 2 목표(R6): 공격자 측 HP 차감 경로 실행 0건. `SoakHarness.cs` L106: `verdict.hp_apply.attackerSide` 카운터 soak 중 0 요구.
- 전용 안티치트 SDK: VAC(Steam 내장), EAC(Epic), BattlEye 미도입. `Assets/` 내 관련 코드 없음.
- 입력 검증·속도/위치 sanity check: 코드 내 명시적 구현 없음.
- 민감 데이터 서버 보관: 현재 P2P 호스트 구조라 호스트 머신이 곧 데이터 보유처. 별도 서버 DB 없음.
- Steam 계정 VAC 밴 상태 조회 API(`SteamApps.IsVACBanned`)는 Facepunch.Steamworks에 있으나 게임 코드에서 활용 없음.

## 설계·결정

- 현재 단계(EA 전): 전용 안티치트 없이 호스트 권위 일원화로 클라이언트 조작 차단을 시도.
- Steam P2P 중계 환경에서는 클라이언트가 패킷을 직접 조작해도 호스트가 판정을 무시하면 효과 없는 구조를 목표.
- VAC 연동은 Steam 앱 등록 후 Steamworks 파트너 포털에서 활성화 가능하나 미결정.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | **호스트 자체 치팅 방어 불가** | P2P 호스트 권위 = 호스트 플레이어가 곧 서버. 호스트가 클라이언트 코드를 조작하면 판정 결과 자체를 조작 가능 | 출시 전 ADR 작성. 전용 서버 없이는 구조적 해결 불가. 임시방편으로 VAC 활성화·신고 시스템 검토 |
| 높음 | 전용 안티치트 미구현 | EAC/BattlEye 없음. 메모리 핵·스피드핵 탐지 수단 없음 | EA 출시 전 Easy Anti-Cheat(Epic Games SDK, Steam 연동 가능) 검토. 소규모 게임이면 VAC 최소 활성화 |
| 중간 | 입력 sanity check 없음 | 이동 속도·공격 범위 서버 측 검증 코드 미존재 | 호스트 측에서 위치 델타·속도 상한 초과 시 거부 로직 추가 |
| 중간 | R6 권위 교정 미완료 | `verdict.hp_apply.attackerSide` Step 2 목표 — 아직 진행 중 | Step 2 완료 후 soak SCN-07에서 0건 확인 |
| 낮음 | 민감 데이터 전용 서버 없음 | 인벤토리·진행도가 클라이언트 파일에 저장 — 로컬 파일 조작 가능 | 장기 로드맵: 진행도 서버 저장. 단기: 저장 파일 서명/검사 |

P2P 호스트 권위 모델에서 **호스트 플레이어 치팅은 원천 방어 불가**하다. 이는 구조적 한계이며 EA 출시 전 사용자에게 고지하거나 전용 서버 방향을 결정해야 한다.

## 관련 문서

- [[server-hosting|서버-호스팅]]
- [[multiplayer-testing|멀티플레이-테스트]]

---
← [[09-security-hub|09 · 보안 / 안티치트]] · [[index|인덱스]]
