---
title: input-시스템
tags: [framework, input]
status: done
source:
  - Assets/PlayerControls.inputactions
  - Assets/Scripts/Character/Player/PlayerLocomotionManager.cs
  - Assets/Scripts/Character/Player/PlayerInputDiagnostics.cs
  - Packages/manifest.json
verified: 2026-06-15
---

# input-시스템

Unity Input System 패키지(`com.unity.inputsystem 1.17.0`)를 사용한다. 레거시 Input Manager 미사용. `PlayerControls.inputactions` 에셋에 액션 맵을 정의하고, 생성된 C# 클래스로 폴링한다.

## 현황 (pB)

### 패키지
- `com.unity.inputsystem`: **1.17.0** (manifest.json 확인)
- 레거시 `UnityEngine.Input` 은 진단 도구(`PlayerInputDiagnostics.cs`)에서만 `Keyboard.current` API 호출.

### InputActions 에셋 (`Assets/PlayerControls.inputactions`)
확인된 액션 맵:

| 맵 이름 | 주요 액션 |
|---|---|
| Player MoveMent | Movement (WASD + 스틱 2DVector) |
| Player Camera | (마우스 / 스틱 카메라 조작) |
| (추가 맵) | 공격, 구르기, 상호작용, 무기 전환 등 추정 |

- `Movement` 액션: `PassThrough` 타입, `Vector2`, 2DVector(mode=2) 합성 바인딩으로 WASD + 컨트롤러 좌스틱 동시 지원.

### 입력 처리 흐름
- `PlayerControls` C# 클래스 자동 생성 → `PlayerManager` 또는 `PlayerLocomotionManager` 에서 인스턴스화 후 각 액션 이벤트 구독
- `PlayerInputDiagnostics.cs` — Z/C 키 눌림을 `Keyboard.current.zKey.wasPressedThisFrame` 으로 감지하는 비침투적 진단 도구 (DontDestroyOnLoad)

### 리바인딩·디바이스 전환
- 런타임 리바인딩 UI 미구현.
- 게임패드 지원: 바인딩에 스틱/버튼 경로 포함되나 실제 QA 여부 미확인.

## 설계·결정

- Input System 1.17.0 채택: NGO 2.x 와 호환성 검증. 레거시와 혼용 방지.
- .inputactions 단일 에셋 집중: 바인딩 변경이 에셋 파일 한 곳에서 완결. 코드 재생성 트리거 자동화.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | **런타임 리바인딩 없음** | Steam 출시 게임 표준(키 재지정 지원). 접근성 규정 일부에서도 요구. 현재 하드코딩된 바인딩. | `InputActionRebindingExtensions` API로 리바인딩 UI 구현 + `PlayerPrefs` 영속화 |
| 높음 | **네트워크 입력 캡처 없음** | 예측-재조정(P0-3)을 위한 입력 프레임 캡처·재생 구조 미존재. 현재 로컬 입력이 즉시 서버 요청으로 전송. | 입력 스탬프 구조체 도입 (Reports/netcode Step 2 관련) |
| 보통 | **게임패드 실측 QA 부재** | 바인딩 파일에 스틱 경로가 존재하나 컨트롤러 테스트 여부 미확인. Steam Deck 출시 고려 시 필수. | 컨트롤러 연결 후 전 액션 맵 동작 확인 |
| 보통 | **PlayerInputDiagnostics 프로덕션 잔류 주의** | `DontDestroyOnLoad` 디버그 오브젝트가 씬에 남아있으면 릴리즈 빌드에서도 동작. | `#if UNITY_EDITOR` 또는 빌드 제외 레이어 처리 |
| 낮음 | **입력 추상화 레이어 없음** | 컨트롤러 타입별 UI 프롬프트(PS/Xbox/키보드) 분기 구조 없음. | InputControlScheme 기반 디바이스 감지 + 아이콘 교체 레이어 |

## 관련 문서

- [[prediction-reconciliation|예측·재조정·보간]]
- [[ui-framework|UI 프레임워크]]

---
← [[05-core-framework-hub|05 · 재사용 코어 프레임워크]] · [[index|인덱스]]
