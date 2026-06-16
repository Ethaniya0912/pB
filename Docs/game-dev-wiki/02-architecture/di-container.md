---
title: di-컨테이너
tags: [architecture]
status: done
source:
  - Assets/Scripts/World Manager/
  - Assets/Scripts/Helper/DontDestroyOnLoadHelper.cs
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Assets/Scripts/Utilities/Cave Genderator/CaveManager.cs
  - Assets/Shader/Fog_Compute/GPUDrivenShadowManager.cs
verified: 2026-06-15
---

# di-컨테이너

pB에는 DI 컨테이너(Zenject/VContainer 등)가 도입되지 않았다. 수동 싱글톤 + `DontDestroyOnLoad` + `FindObjectOfType` 패턴으로 의존성을 해소하고 있다.

## 현황 (pB)

### DI 컨테이너 부재 확인

`Assets/Scripts` 전체를 `Zenject|VContainer|\[Inject\]|InjectionContext` 패턴으로 검색한 결과 **0건**이다. Zenject(Extenject) 또는 VContainer 패키지는 `Packages/manifest.json`에 존재하지 않는다.

### 실제 의존성 해소 패턴

**패턴 1 — 정적 싱글톤 + `DontDestroyOnLoad`**

`Assets/Scripts/World Manager/` 내 매니저 11종 전체가 `public static [ClassName] Instance { get; private set; }` + `DontDestroyOnLoad(gameObject)` 구조를 취한다:
```
WorldGameStateManager, WorldSaveGameManager, WorldCameraManager,
WorldSoundFXManager, WorldCharacterEffectsManager, WorldActionManager,
WorldAISpawnManager, WorldUtilityManager, WorldItemDatabase,
WorldGameSessionManager, WorldFactionStateManager
```
Cave 시스템(`CaveManager`), 렌더 매니저(`GPUDrivenShadowManager`, `ShaderCoordinationManager`), 네트워크(`SteamLobbyManager`) 포함 전체 싱글톤 인스턴스 28개 이상이 동일 패턴을 사용한다.

**패턴 2 — `DontDestroyOnLoadHelper` 컴포넌트**

`Assets/Scripts/Helper/DontDestroyOnLoadHelper.cs`:
- 자식 오브젝트에서 `DontDestroyOnLoad`가 silent-fail하는 Unity 규칙 우회용 Helper.
- `DefaultExecutionOrder(-800)` — Bridge(-500)보다 먼저, World(-1000)보다 뒤 실행.
- 컨테이너 GameObject의 자식에 배치된 매니저에 AddComponent로 적용.

**패턴 3 — `FindFirstObjectByType` / `FindObjectOfType`**

40+ 파일에서 `FindFirstObjectByType` / `FindObjectOfType`가 사용된다. 싱글톤 Instance가 null인 경우의 폴백 조회, 또는 런타임에 컴포넌트를 직접 탐색하는 용도로 쓰인다.

```csharp
// SteamLobbyManager.cs:84
transport = FindFirstObjectByType<SteamP2PRelayTransport>();
```

**패턴 4 — SerializeField 인스펙터 직접 연결**

일부 컴포넌트는 Inspector에서 `[SerializeField]` 레퍼런스로 의존성을 직접 주입한다.

## 설계·결정

DI 컨테이너 미도입은 명시적 결정이 아니라 **초기 프로토타입 패턴이 그대로 확장된 결과**다. `Assets/Scripts/World Manager/` 폴더가 Soul-Like RPG 튜토리얼 패턴("World Manager" 싱글톤 컬렉션)에서 출발했음이 코드 스타일에서 확인된다.

## ⚠ 비판·리스크

**[심각도: 높음] 싱글톤 28개 이상 — 암묵적 전역 상태 그물망**
28개 이상의 정적 싱글톤이 서로 `[ClassName].Instance.[Method]`로 직접 호출한다. 의존성 그래프가 코드에서 보이지 않아 "어떤 매니저가 어떤 매니저를 호출하는지" 추적이 어렵다. 특히 `OnDisable`/`OnDestroy` 순서에서 다른 싱글톤이 먼저 파괴되면 NullReferenceException이 발생한다. 실제로 `SteamLobbyManager`는 `NetworkManager.Singleton == null` 가드를 여러 곳에 중복 삽입하여 이 문제에 대응하고 있다.

**[심각도: 높음] 유닛 테스트 불가**
`WorldGameStateManager.Instance` 형태의 정적 접근은 MonoBehaviour 수명주기에 묶여 있어 PlayMode 없이 NUnit 테스트에서 호출이 불가하다. `TDA.PB4.Tests.PlayMode.csproj`가 존재하지만 PlayMode 테스트에서도 씬 설정이 없으면 싱글톤 Instance는 null이다. 현재 코드베이스의 매니저 로직은 사실상 자동화 테스트 밖에 있다.

**[심각도: 보통] `FindFirstObjectByType` 런타임 비용**
40+ 파일에서 씬 전체 오브젝트 탐색이 발생한다. 씬 규모 증가 시(동굴 청크 다수 활성화 상태) 이 비용이 체감될 수 있다. 특히 Awake/Start 이외 업데이트 루프에서 호출되는 경우 프레임 스파이크 원인이 된다.

**[심각도: 보통] `DontDestroyOnLoad` 씬 전환 정리 복잡도**
28개 이상의 싱글톤이 모두 씬 전환을 초월해 살아있으므로, 게임-타이틀 씬 전환 시 상태 초기화 책임이 각 싱글톤에 분산된다. `SteamLobbyManager.RevertToTitleScreen()`처럼 복잡한 정리 체인이 필요하고, 누락 시 이전 세션 상태가 다음 세션으로 오염된다.

**[심각도: 낮음] VContainer 도입 시 전환 비용**
28개 싱글톤 전체를 VContainer LifetimeScope로 교체하려면 Awake 싱글톤 초기화 → 생성자 DI 전환, `Instance` 참조 전수 제거, `DontDestroyOnLoad` 수명 관리 변경이 필요하다. 현 규모(28+ 파일 직접 참조 포함)에서 전환 비용이 상당하므로 신규 시스템부터 점진 적용이 현실적이다.

## 관련 문서

- [[ecs-vs-oop|ecs-vs-oop]]
- [[adr-0001-netcode|adr-0001-netcode-선정]]

---
← [[02-architecture-hub|02 · 아키텍처 기반 결정]] · [[index|인덱스]]
