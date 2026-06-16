---
title: ecs-vs-oop
tags: [architecture, decision]
status: done
source:
  - Assets/Scripts/World Manager/
  - Assets/Scripts/Character/
  - Assets/Scripts/Utilities/Cave Genderator/CaveComputeDispatcher.cs
  - Assets/Scripts/Utilities/Cave Genderator/CaveMeshJobManager.cs
  - Assets/Scripts/Utilities/Cave Genderator/CaveManager.cs
  - Packages/manifest.json
verified: 2026-06-15
---

# ecs-vs-oop

pB는 OOP MonoBehaviour 매니저 패턴을 기반으로 하며, DOTS/ECS는 미도입이다. 일부 GPGPU Compute Shader가 데이터 지향 연산을 부분적으로 수행한다.

## 현황 (pB)

### ECS / DOTS 부재 확인

`Assets/Scripts` 전체에서 `Unity.Entities|com.unity.entities|DOTS|EntityManager|ISystem|IComponentData` 패턴 검색 결과 **0건**이다. `Packages/manifest.json`에 `com.unity.entities`가 없으며, `com.unity.netcode.entities`(Netcode for Entities) 패키지도 없다.

### 실제 구조 — OOP 매니저 패턴

**World Manager 계층**: `Assets/Scripts/World Manager/`에 정적 싱글톤 매니저 11종이 있으며, Character, Cave, Render, Network를 합산하면 싱글톤 매니저 28개 이상이다.

**Character Manager 계층**: `Assets/Scripts/Character/`에 캐릭터별 서브매니저 패턴이 존재한다:
```
CharacterAnimationManager, CharacterCombatManager, CharacterDefenseManager,
CharacterEffectsManager, CharacterEquipmentManager, CharacterEventManager,
CharacterExecutionManager, CharacterInventoryManager, CharacterLocomotionManager,
CharacterNetworkManager, CharacterStatsManager, CharacterQTEManager 등
```
각 캐릭터 `CharacterManager`가 이들 서브매니저를 레퍼런스로 보유하는 컴포지션 구조.

**Cave System 매니저 계층**: `CaveManager`(싱글톤·글로벌) + `CaveChunkManager`(청크별) + `CaveEcosystemManager` + `CaveSpawnerManager` + `CaveLightingManager` + `TerrainSyncNetworkManager` 등 Cave 전용 매니저 6종 이상.

### 데이터 지향 일부 적용 — GPGPU

OOP 매니저 패턴 내에서 연산 집약 구간은 Compute Shader로 오프로드한다:
- `CaveDensityGenerator.compute` — 동굴 밀도장 GPU 병렬 계산
- `CaveMarchingCubes.compute` — Marching Cubes 메시 GPU 추출
- `GPUCulling.compute` — 2,200+ 그림자 캐스터 GPU Frustum Culling
- `CaveMeshJobManager.cs` — Unity Job System을 사용한 메시 작업 CPU 병렬화 추정(`Unity.Collections` import 확인)

이 GPGPU 접근은 DOTS ECS와 무관하게 MonoBehaviour 래퍼가 Dispatch를 호출하는 구조다.

## 설계·결정

ECS/DOTS 미도입은 프로젝트 초기 Soul-Like 템플릿에서 시작해 그대로 확장된 결과다. 현재 게임 규모(4인 협동, 소규모 NPC)에서 MonoBehaviour OOP가 개발 속도 면에서 유리하다고 암묵적으로 판단된 것으로 보이나, 명시적 ADR이나 비교 평가 기록은 없다.

Cave 생성 연산이 GPGPU Compute Shader로 분리된 것은 실용적 절충이다 — 핵심 대량 연산만 GPU로 오프로드하고, 나머지 게임 로직은 MonoBehaviour로 유지.

## ⚠ 비판·리스크

**[심각도: 높음] AI·아이템 대량 엔티티 시 성능 스케일 한계**
현재 AI NPC 각각이 `WorldAISpawnManager`·`CharacterManager` 기반 MonoBehaviour 오브젝트다. 동굴씬에서 AI 수가 수십 이상으로 증가하면 MonoBehaviour Update 오버헤드가 ECS 대비 선형 이상으로 커진다. `WorldAISpawnManager`는 `DontDestroyOnLoad` 싱글톤으로 전역 AI 상태를 관리하고 있어, AI 수 증가 시 단일 매니저 병목 가능성이 있다. EA 전에 씬당 AI 상한과 성능 예산을 측정하지 않았다.

**[심각도: 높음] God-Manager 경향 — 단일 책임 위반**
`WorldGameStateManager`가 게임 상태 전이, 카메라 연출 프리셋(인벤토리·락온·요리·보스), 타이틀 복귀까지 복수 책임을 진다. `CaveManager`는 `BiomeSyncMode` 원자 토글 + 동굴 생성 상태 머신 + 씬 전환을 모두 포함하고 있다. 이런 god-manager는 단위 테스트가 불가하고 변경 시 충돌 범위가 넓다.

**[심각도: 보통] ECS 전환 비용이 사실상 전면 재작성 수준**
28개 매니저 + Character 서브매니저 12종 + Cave 매니저 6종 전체가 MonoBehaviour 기반이다. DOTS/ECS 전환은 컴포넌트 시스템 설계를 처음부터 다시 하는 수준이며, NGO와 Netcode for Entities 병행 고려 시 네트워크 코드도 함께 교체해야 한다. 현 시점에서 ECS 전환은 EA 이전에 현실적이지 않다.

**[심각도: 보통] `Netcode for Entities` 비채택으로 네트워크 ECS 경로 막힘**
NGO + OOP 구조를 선택했으므로 향후 대량 엔티티 동기화(AI 군집·아이템 필드)를 Netcode for Entities로 처리하는 경로가 막혔다. 규모 확장 시 NGO `NetworkObject` 수 한계에 부딪힐 수 있다.

**[심각도: 낮음] Cave GPGPU와 OOP 경계 검증 누락**
`CaveMeshJobManager`가 Unity Job System을 사용한다면 Job 내부에서 MonoBehaviour 접근이 금지되는데, 이 경계를 엄밀히 지키는지 코드 리뷰가 없다. Job 내 managed 객체 접근은 컴파일 에러 없이 런타임 크래시를 유발할 수 있다.

## 관련 문서

- [[di-container|DI-컨테이너]]
- [[adr-0001-netcode|adr-0001-netcode-선정]]
- [[render-pipeline|렌더-파이프라인]]

---
← [[02-architecture-hub|02 · 아키텍처 기반 결정]] · [[index|인덱스]]
