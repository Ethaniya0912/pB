# Bridges&Interfaces/Stubs — Null Object Pattern fallback

## 본 폴더의 책임

**Null Object Pattern** 의 정상 구현체. 외부 매니저 / SO 가 미존재일 때
NullReferenceException 회피용 안전한 default 응답 제공.

★ **폐기 인터페이스의 임시 stub 과 본질 다름**

## 수록 컴포넌트

| 클래스 | 구현 인터페이스 | 용도 |
|---|---|---|
| `StubBlackboard` | `IBlackboard` | GameBlackboard 미존재 시 fallback |

## Stubs vs Mocks vs Deprecated 구분

| 폴더 | 용도 | 라이프사이클 |
|---|---|---|
| **Stubs/** (본 폴더) | Null Object Pattern — runtime 안전성 | ★ 영구 유지 |
| **Mocks/** | Bridge 통합 테스트 — 가짜 구현체로 검증 | 개발 기간 (#if UNITY_EDITOR) |
| **Deprecated/** | 폐기 예정 인터페이스 정의 | 마이그레이션 후 제거 (P3) |

## 사용 예시

```csharp
// BaseAIBrain.cs 의 fallback 패턴
var gb = GameBlackboard.Instance;
blackboard = (gb != null) 
    ? new GameBlackboardAdapter(gb)
    : new StubBlackboard();  // ★ 안전한 default

// 실제 연결 여부 검사
public bool IsStubFree() {
    return blackboard != null && !(blackboard is StubBlackboard);
}
```

## 새 Stub 추가 기준

| 조건 | 추가 |
|---|---|
| 외부 매니저 / SO 가 미존재 가능성 있음 | ✓ |
| 미존재 시 NRE 회피 필요 | ✓ |
| 호출자가 매번 null 검사 회피 | ✓ |
| 안전한 default 응답 가능 | ✓ |

**X 사용 X 인 경우**:
- 임시 폐기 인터페이스 → Deprecated/ 또는 제거
- 테스트용 가짜 구현 → Mocks/

## 명명 규칙

| 항목 | 규칙 | 예시 |
|---|---|---|
| 파일 | `Stub{InterfaceName}.cs` | `StubBlackboard.cs` |
| 클래스 | `Stub{Interface 이름}` | `StubBlackboard` |
| namespace | `TDA.PB4.Stubs` | (기존 보존) |
| ToString() | "Stub{X} (Null Object — {대상} 미존재)" | 디버깅 식별 |
