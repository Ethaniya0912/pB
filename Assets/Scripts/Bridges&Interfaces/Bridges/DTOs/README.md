# Bridge DTOs 정책

본 폴더는 Bridge 통신 시 사용되는 **데이터 컨테이너 (DTO)** 모음입니다.

## 현재 상태 — 도메인별 정책 분기

### AI 도메인 — 기존 namespace 사용 (DTO 신설 X)

AI Bridge 가 사용하는 DTO 는 **기존 `TDA.PB4.AI` namespace** 의 정의를 사용:

| DTO | 위치 | Bridge 사용 메서드 |
|---|---|---|
| `SpawnRequest` (struct) | `TDA.PB4.AI` namespace | `WorldAIBridgeManager.ExecuteSpawnRequest`, `QueueRequest` 등 |
| `SpawnRequestResponse` | `TDA.PB4.AI` namespace | 반환값 |

→ Bridge 의 `using TDA.PB4.AI;` 로 가져오면 됨. 본 폴더에 별도 정의 X.

### Terrain 도메인 — 도메인 타입 그대로 노출 (DTO 신설 X)

Terrain Bridge 는 **CaveSystem / TDA.PB4.Terrain namespace** 의 타입을 그대로 외부에 노출:

| 타입 | 위치 |
|---|---|
| `ElementKind`, `IdentityTag`, `StateTag`, `EventTag`, `PotentialTag` (enum) | `CaveSystem` |
| `ContextTagSet`, `TagApplicationLog` (struct) | `CaveSystem` |
| `NodeRole`, `PassageRole` (enum) | `CaveSystem` |
| `JunctionStage` (enum) | `TDA.PB4.Terrain` |

→ TerrainContextAnalyzer 의 `IJunctionContextAnalyzer` 인터페이스가 이미 잘 설계됨. Bridge 가 변환 layer 두지 않음 (얇음 원칙).

### Scenario 도메인 — 향후

ScenarioManager 신설 후 본 폴더에 DTO 추가 예정.

---

## 새 DTO 가 필요해질 경우

### 신설 판단 기준

| 상황 | 권장 |
|---|---|
| 기존 도메인 namespace 에 이미 DTO 존재 | 기존 것 사용 (Bridge 가 `using` 추가) |
| 도메인 API 가 이미 잘 설계됨 | Bridge 가 도메인 타입 그대로 노출 (변환 X) |
| Bridge 전용 DTO 필요 (다중 도메인 통신) | 본 폴더에 신설 |

### 본 폴더에 신설 시 명명 규칙

| 항목 | 규칙 | 예시 |
|---|---|---|
| 파일 | `{Domain}{System}DTO.cs` 또는 `{Action}DTO.cs` | `AISpawnDTO.cs` |
| namespace | `TDA.PB4.Bridge.DTOs` (또는 도메인 결정 따라) | |
| 클래스 | v2 명명 컨벤션 Suffix | `~Request`, `~Result`, `~Notification` |

---

## DTO 설계 원칙

| 원칙 | 설명 |
|---|---|
| **불변성** | `struct` 또는 `[Serializable] class` |
| **무상태** | 비즈니스 로직 X, 형식 검증만 |
| **소유** | 도메인이 자기 DTO 정의 / Bridge 는 사용만 |
| **★ 변환 layer 회피** | 도메인 API 가 잘 설계된 경우 그대로 노출 |

---

## 참조 문서

- 통신 책임 경감 분석 v1
- pB-4 명명 컨벤션 v2 (11 장 Bridge / 12 장 메타 영역)
- pB-4 시스템 메타 모델 v2
