# Bridges/Mocks — Mock 구현체 가이드

## 용도

실제 도메인 매니저 (WorldAISpawnManager / GroupAIManager / TerrainContextAnalyzer 등) 가 **미존재** 또는 **미구현** 상태일 때 Bridge 의 라우팅 / 디버그 / 로그 시스템을 단독으로 테스트.

## 수록 Mock 컴포넌트

### AI 도메인 (MockAIReceivers.cs)

| 컴포넌트 | 구현 인터페이스 | 용도 |
|---|---|---|
| `MockSpawnReceiver` | `ISpawnRequestReceiver` | Spawn 명령 수신 시뮬레이션 |
| `MockGroupDashboard` | `IGroupDashboard` (13 메서드) | 그룹 상태 시뮬레이션 (Wk5+ GroupAIManager 신설 전) |
| `MockGroupCommandReceiver` | `IGroupCommandReceiver` | 분대 명령 시뮬레이션 |

### Terrain 도메인

별도 Mock 불필요 — `TerrainContextAnalyzer` 가 이미 존재.
필요 시 `MockTerrainAnalyzer` 추가 가능 (`IJunctionContextAnalyzer` 24 메서드 구현).

### Scenario 도메인

`ScenarioManager` 신설 전 placeholder Mock 추가 가능.

---

## 사용 시나리오

### 시나리오 1 — AI Bridge 라우팅 검증 (실제 AI 매니저 없을 때)

```
1. 씬에 빈 GameObject "MockAIReceivers" 생성
2. MockSpawnReceiver 컴포넌트 추가
3. MockGroupDashboard 컴포넌트 추가 (선택)
4. MockGroupCommandReceiver 컴포넌트 추가 (선택)

5. WorldAIBridgeManager GameObject 선택
6. ContextMenu > [6] Force Auto-Find
   → Mock 컴포넌트들이 자동 발견됨
   → Inspector 의 _spawnReceiverImpl, _groupDashboardImpl 등에 표시

7. ContextMenu > [Mock A1] Test ExecuteSpawnRequest
   → Console:
     ════════ [MOCK START] ExecuteSpawnRequest ════════
     [AIBridge → ISpawnRequestReceiver] ExecuteSpawnRequest(args=SpawnRequest...)
     [MockSpawnReceiver] ExecuteSpawnRequest 호출
     [AIBridge ← ISpawnRequestReceiver] ExecuteSpawnRequest → SpawnRequestResponse...
     [MOCK] 응답: ...
     ════════ [MOCK END] ExecuteSpawnRequest ════════
```

### 시나리오 2 — 이벤트 발행 테스트

```
1. MockSpawnReceiver Inspector
2. ContextMenu > Fire OnSpawnRequestCompleted
   → 이벤트 강제 발행 → Bridge 의 OnSpawnRequestCompleted 구독자가 수신
```

### 시나리오 3 — Threshold Event 테스트 (Wk5+ 시뮬레이션)

```
1. MockGroupDashboard Inspector
2. ContextMenu > Fire OnThresholdCrossed (MoraleCollapse)
   → 가상 MoraleCollapse 이벤트 발행
   → Bridge 통해 시나리오 시스템 (또는 구독자) 가 수신
```

---

## 주의 사항

| 항목 | 설명 |
|---|---|
| **`#if UNITY_EDITOR` 조건부 컴파일** | 본 Mock 들은 Editor 만 활성화 — 실제 빌드에 포함 X |
| **Mock 우선순위 (Auto-Find)** | 실제 구현체와 Mock 이 같은 씬에 있으면 — `FindObjectsByType` 가 둘 다 발견 → Warning 출력 + 첫 번째 선택 |
| **명시적 할당 권장** | 실제 vs Mock 명확히 구분하려면 Bridge Inspector 에 수동 할당 |

---

## 새 Mock 추가 시

### 1. 어느 폴더에 둘지

`Bridges/Mocks/` 폴더 안에:
- `Mock{Domain}Receivers.cs` 형태로 묶음 (도메인 별 한 파일)
- 또는 별도 파일 (`MockScenarioManager.cs` 등)

### 2. 명명 규칙

| 항목 | 규칙 | 예시 |
|---|---|---|
| 파일 | `Mock{Domain}{Subject}.cs` | `MockAIReceivers.cs` |
| 클래스 | `Mock{Interface 이름}` 또는 `Mock{역할}` | `MockSpawnReceiver` |
| namespace | `TDA.PB4.Bridge.Mocks` | |

### 3. 권장 패턴

```csharp
#if UNITY_EDITOR

namespace TDA.PB4.Bridge.Mocks
{
    public class MockXxx : MonoBehaviour, IXxx
    {
        [SerializeField] private bool _logCalls = true;
        
        // SerializeField 로 Mock 동작 제어 (Inspector 에서 조정)
        [SerializeField] private bool _alwaysSucceed = true;
        
        // 인터페이스 메서드 구현
        public ReturnType DoSomething(...) {
            if (_logCalls) Debug.Log($"[MockXxx] DoSomething", this);
            return default;
        }
        
        // 이벤트 강제 발행 ContextMenu (옵션)
        [ContextMenu("Fire SomeEvent")]
        private void FireSomeEvent() { ... }
    }
}

#endif
```

---

## 관련 문서

- pB-4 Bridges & Interfaces 통합 가이드 v1 (§ 8 작업 시나리오 / § 13 디버깅)
- Contracts/AIBridgeContracts.cs (실제 인터페이스 시그니처 참조)
- Contracts/TerrainBridgeContracts.cs (Terrain 협업 계약)
