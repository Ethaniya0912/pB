---
title: scriptableobject-아키텍처
tags: [data, architecture]
status: done
source:
  - Assets/Scripts/Items/Item.cs
  - Assets/Scripts/Items/WeaponItem.cs
  - Assets/Scripts/Items/MeleeWeaponItem.cs
  - Assets/Scripts/Items/ShieldWeaponItemSO.cs
  - Assets/Scripts/World Manager/WorldItemDatabase.cs
  - Assets/Data/Items/
  - Assets/Data/SO/
verified: 2026-06-15
---

# scriptableobject-아키텍처

ScriptableObject(SO)를 데이터 컨테이너로 사용하는 설계. 아이템, AI 행동, 팩션, 애니메이션, 카메라 프리셋 등 광범위한 영역에 SO가 실제 사용되고 있다. 코드-데이터 분리는 이루어졌으나 런타임 변조 가드는 취약하다.

## 현황 (pB)

### SO 클래스 계층 (아이템)

```
Item (ScriptableObject)
├── WeaponItem
│   └── MeleeWeaponItem
└── FoodItem
ShieldWeaponItemSO (별도 경로)
```

- `Item` 기반 공통 필드: `itemIcon`, `itemName`, `itemID`, `itemModel`(드롭 프리팹), 인벤토리 칸 크기(`itemSizeWidth`, `itemSizeHeight`), `itemDescription`
- `WeaponItem` 추가 필드: 데미지(`physicalDamage`, `elementalDamage`), 포이즈 데미지, 공격 모디파이어(6종), 스태미나 비용, `WeaponItemAction` SO 참조(콤보 액션 체계)
- `itemID` 는 런타임에 `WorldItemDatabase.InitializeItemDatabase()` 에서 리스트 순서 기반으로 부여 (0, 1, 2...) — SO 파일 자체에도 저장

### 실존 SO 자산 현황 (`Assets/Data/`)

| 카테고리 | 디렉토리 | 대략 수량 |
|---|---|---|
| 무기(Melee/Shield) | `Data/Items/Weapons/` | 9개 |
| 음식·재료 | `Data/Items/Foods/`, `CookingRecipe/` | 15개+ |
| AI 아키타입 | `Data/SO/AI_Archetype/` | 20개+ |
| AI 팩션 | `Data/SO/AI_Faction/` | 다수 |
| AI 전투 프로필 | `Data/SO/AI_Combat Profiles/` | 9개 |
| AI 대화 프리셋 | `Data/SO/AI_DialoguePreset/` | 다수 |
| AI 퍼스널리티 | `Data/SO/AI_Personality/` | 다수 |
| 카메라 프리셋 | `Data/SO/Camera/` | 다수 |
| 애니메이션 셋 | `Data/SO/Animations/` | 플레이어·스켈레톤 |
| 이펙트 | `Data/Effects/HitSpark/`, `Instant Effects/` | 5개 |
| **총 SO 자산 수** | `Assets/Data/SO/` | **187개+** |

### WorldItemDatabase (런타임 등록)
- `WorldItemDatabase` 싱글톤이 `List<WeaponItem>`, `List<FoodItem>` 을 보유
- `InitializeItemDatabase()` 에서 리스트 → 딕셔너리(`Dictionary<int, Item>`, `Dictionary<int, GameObject>`) 변환
- ID 기반 O(1) 조회: `GetWeaponByID(int)`, `GetItemByID(int)`, `GetItemPrefab(int)`
- 쿠킹 레시피도 `List<CookingRecipeSO>` 로 등록

### pB-4 AI SO 체계
- `FactionGroupPolicySO`, `MobFactionDataSO`, `GroupArchetypeSO`, `PersonalityTagSO`, `FactionTierSO` 등
- `BiomeAffinitySO`: 지역-팩션 친화도 (Issue: FactionDefinitionSO가 참조하여 도메인 경계 혼재 — 분리 의제 존재)
- `CaveBiomeSettings` 등 지형 관련 SO도 별도 존재

## 설계·결정

- SO 선택 이유: 에디터 인스펙터에서 디자이너가 직접 수치 조정 가능. 참조 공유로 프리팹 중복 없음.
- `itemID` 런타임 부여: SO 자체에 ID를 저장하지 않고 리스트 순서에 의존. 에디터 `OnValidate` 에서도 갱신.
- 별도 `WeaponItemAction` SO: 콤보 액션을 교체 가능한 SO로 분리 — 무기마다 다른 공격 모션 적용.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | **P1-5: WeaponItem SO 인스턴스 누수** | Reports/netcode Step 4 기록: 장비 교체 시 `WeaponItem` SO 원본 자산을 런타임에서 참조하지 않고 인스턴스를 생성하는 경로가 있으면 GC 누적. 1000회 장비 교체 시 메모리 누적 증가 실측 필요. | DB 원본 참조 유지 + 런타임 스탯 변경은 별도 구조체로 분리. 임시: 교체 시 이전 인스턴스 `Destroy`. |
| 높음 | **itemID 순서 의존** | `WorldItemDatabase` 리스트 순서가 곧 ID. 에디터에서 항목 순서가 바뀌면 기존 세이브 파일의 장비 ID가 모두 틀어짐. | ID를 SO 파일에 명시적 고정값으로 영속화(GUID 또는 수동 부여 int) + 마이그레이션 테이블 |
| 높음 | **런타임 SO 직접 변조 가드 없음** | SO 필드가 `public` — 코드에서 `weapon.physicalDamage = 999` 처럼 직접 쓰면 에셋 자체가 오염됨(에디터 에셋이므로 PlayMode 종료 후 리셋되나, NetworkVariable과 혼용 시 혼란). | 런타임 변경이 필요한 필드는 래퍼(`CharacterStats`) 에 복사 후 사용 |
| 보통 | **FactionDefinitionSO·BiomeAffinitySO 도메인 혼재** | `FactionDefinitionSO` 가 `BiomeAffinitySO` 를 참조 = 팩션 정의 + 지역 분포 정보 결합. 확장 시 의존성 폭발. | `WorldBiomeFactionRegistrySO` 별도 도입으로 분리 (Wk5+ 의제로 기록됨) |
| 낮음 | **SO 파일 187개 — 명명 규칙 불일치 존재** | 무기 SO는 "Straight Sword", "Broad Sword" 등 영문, 일부 파일은 한글 혼용 추정. | 네이밍 컨벤션 문서화 + 일괄 정리 |

## 관련 문서

- [[data-pipeline|데이터 파이프라인]]
- [[save-load|세이브-로드]]

---
← [[06-data-hub|06 · 데이터 파이프라인]] · [[index|인덱스]]
