---
title: 코딩-컨벤션
tags: [tooling]
status: done
source:
  - .editorconfig
  - .harness/_conventions.md
  - Assets/Scripts/Helper/DontDestroyOnLoadHelper.cs
  - Assets/Scripts/Utilities/NetDiagnostics/NetSimController.cs
  - Assets/Scripts/pB-4/week0_Core/GameBlackboard.cs
  - Assets/Scripts/pB-4/week0_Core/EventBus.cs
verified: 2026-06-15
---

# 코딩-컨벤션

팀 코드 일관성 규칙. pB 프로젝트의 실제 적용 규칙을 코드에서 실측한 결과를 기록한다.

## 현황 (pB)

### `.editorconfig` 실측 내용

```ini
[*]
indent_style = space

[*.xml]
indent_size = 2

root = true
[*.cs]
charset = utf-8
end_of_line = crlf
```

- `.cs` 파일: UTF-8 인코딩, CRLF 줄 끝
- XML 파일: 인덴트 2칸
- **네이밍 규칙, 네임스페이스, 최대 줄 길이 등은 `.editorconfig`에 정의되지 않음**

### 핵심 규칙 (`.harness/_conventions.md` §1 + 코드 실측)

#### 규칙 1: 파일명 = 클래스명 (단독 파일)

`_conventions.md` §1에 명시: "MonoBehaviour/ScriptableObject는 파일명=클래스명 단독 파일 — 어기면 MonoScript 미바인딩으로 도메인 리로드 1회에 missing-script(husk)가 된다(netcode 사이클 회귀 실증)."

**실측 확인**: `Assets/Scripts/Helper/DontDestroyOnLoadHelper.cs` → 클래스명 `DontDestroyOnLoadHelper`, `Assets/Scripts/Utilities/NetDiagnostics/NetSimController.cs` → 클래스명 `NetSimController` 등 모두 일치.

#### 규칙 2: DontDestroyOnLoadHelper 패턴

씬 전환을 넘겨야 하는 매니저 패턴 (`Assets/Scripts/Helper/DontDestroyOnLoadHelper.cs`):

- `[DefaultExecutionOrder(-800)]` — Bridge 컴포넌트(-500)보다 먼저 실행
- `Awake()`에서 `transform.SetParent(null)` 후 `DontDestroyOnLoad(gameObject)` 호출
- NGO NetworkManager 상속 클래스에는 적용 금지(충돌)
- 씬마다 리셋이 필요한 매니저에는 적용 금지

#### 규칙 3: Hierarchy 레이어·그룹 구조 (`_conventions.md` §15-A)

씬 계층은 3단계로 구성:

```
━━━━ X Layer ━━━━          ← 레이어 컨테이너 (이중선, 씬 루트에 배치)
  └─── X ───               ← 카테고리 그룹 (단선)
       └─ 매니저 GO        ← 실제 오브젝트
```

실제 레이어 예시: `Cyber Layer`, `Physics Layer`, `UI Layer`, `Helper Layer`

#### 규칙 4: 네임스페이스

코드 실측으로 확인된 네임스페이스 패턴:

- `TDA.PB4.Core` — GameBlackboard, EventBus 등 주차 공통 코어 (`Assets/Scripts/pB-4/week0_Core/`)
- `TDA.PB4.Helpers` — DontDestroyOnLoadHelper (`Assets/Scripts/Helper/`)
- `TDA.PB4.Interfaces.Narrative` — 내러티브 도메인 인터페이스
- 상위 스크립트 일부는 네임스페이스 없음 (SteamLobbyManager, SteamP2PRelayTransport)

#### 규칙 5: 조건부 컴파일 심볼

`Assets/Scripts/Networking/SteamP2PRelayTransport.cs` 실측:
- `#if NETCODE_DEBUG` — 수명주기 로그를 릴리즈에서 제거

#### 규칙 6: 스크립트 실행 순서 어노테이션

`[DefaultExecutionOrder]` 어노테이션으로 실행 순서를 명시:
- `DontDestroyOnLoadHelper`: `-800`
- 주석에서 확인: Bridge 컴포넌트 `-500`, World 컴포넌트 `-1000`

### 비동기·스레딩 컨벤션

`SteamLobbyManager.cs`에서 `async Task` 패턴 사용 확인. 구체적 스레딩 규칙은 미문서화.

## 설계·결정

- **파일명=클래스명 강제**: NetSimController 회귀(missing-script)를 실증 후 `_conventions.md`에 명시된 필수 규칙. 위반 시 도메인 리로드마다 missing-script 발생.
- **CRLF 유지**: Windows 개발 환경(개인 프로젝트) 맞춤 설정. 하네스 셸 스크립트만 `.gitattributes`로 LF 강제.
- **DontDestroyOnLoadHelper 컴포넌트 패턴**: NGO와의 충돌을 피하면서 씬 간 매니저를 유지하는 표준 수단.

## ⚠ 비판·리스크

- **심각도 높음**: 코딩 컨벤션이 도구로 강제되지 않는다. `.editorconfig`에 네이밍·린트 규칙이 없고, Roslyn Analyzer나 Unity의 Code Style 설정도 없다. `_conventions.md`는 텍스트 문서일 뿐 — 규칙 위반을 빌드·PR 시점에 자동으로 잡을 수단이 없다.
- **심각도 높음**: 네임스페이스가 일부 스크립트에만 적용됐다. `SteamLobbyManager`, `SteamP2PRelayTransport`는 글로벌 네임스페이스에 있어 이름 충돌 가능성이 있다.
- **심각도 보통**: 비동기(`async/await`) 규칙이 미문서화. `SteamLobbyManager`에 `async Task` 패턴이 있으나 에러 핸들링·취소 토큰 사용 방식이 팀 내 표준화되지 않았다.
- **심각도 보통**: `[DefaultExecutionOrder]` 숫자 범위가 주석(-800, -500, -1000 등)에만 있다. 새 매니저를 추가할 때 어느 범위에 배치할지 기준이 위키에 없다.
- **심각도 낮음**: `Assets/Scripts/Utilities/Cave Genderator/` 폴더명 오타("Genderator")가 코드 내 네임스페이스/클래스명에 오염 전파됐을 가능성. `CaveSystem.Multiplayer` 네임스페이스는 SteamLobbyManager.cs에서 using으로 확인됨.

## 관련 문서

- [[project-structure|프로젝트-구조]]
- [[assembly-definition|assembly-definition]]

---
← [[01-foundation-hub|01 · 기반 (협업/구조/컨벤션)]] · [[index|인덱스]]
