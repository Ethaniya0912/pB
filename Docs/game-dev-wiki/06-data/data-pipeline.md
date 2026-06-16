---
title: 데이터-파이프라인
tags: [data, tooling]
status: done
source:
  - Assets/Scripts/World Manager/WorldItemDatabase.cs
  - Assets/Data/Items/
  - Assets/Data/SO/
  - Packages/manifest.json
verified: 2026-06-15
---

# 데이터-파이프라인

**외부 데이터 임포트 자동화 파이프라인은 없다.** 모든 게임 데이터는 Unity 에디터에서 SO 에셋을 직접 생성·편집하는 수동 워크플로우다. 버전 일치 검증·서버·클라 동기화 자동화도 미구현이다.

## 현황 (pB)

> **다이어그램 — 데이터 흐름** (빨강 = itemID 순서 의존 = 세이브 호환 리스크):

```mermaid
flowchart LR
  SO["수작업 ScriptableObject<br/>WeaponItem · FoodItem · HumanoidArchetypeSO …"]
  SO -->|"WorldItemDatabase ContextMenu"| ID["리스트 순서로 정수 ID 부여<br/>(+ 프리팹 링크)"]
  ID --> RUN["런타임 GetItemByID / GetItemPrefab"]
  RUN --> SAVE["세이브엔 정수 ID만 저장"]
  SAVE -. 다음 세션 .-> LOAD["ID → SO 복원"]
  LOAD --> RUN
  classDef warn fill:#fee2e2,stroke:#b91c1c,color:#000;
  class ID warn
```

### 현행 데이터 워크플로우

```
디자이너/개발자
    → Unity 에디터 (Inspector)
        → ScriptableObject 에셋 직접 편집 (.asset)
            → git 커밋 (LFS)
                → 빌드 번들에 포함
```

- 엑셀/CSV/JSON 임포트 자동화 없음.
- Google Sheets 연동 없음.
- 외부 데이터 파싱 에디터 도구 없음.

### WorldItemDatabase 임포트 경로
- `WorldItemDatabase` Inspector 에서 `List<WeaponItem>`, `List<FoodItem>` 에 SO 드래그&드롭 수동 등록.
- `[ContextMenu("Save Database & Link Prefabs")]` 에디터 컨텍스트 메뉴로 ID 일괄 재부여 + 프리팹 링크 수동 실행.

### 서버·클라 데이터 버전 일치
- 검증 없음. 세이브 파일에 데이터 버전 필드 없음.
- 멀티플레이에서 호스트와 클라이언트의 아이템 DB가 다를 경우 감지 수단 없음.

### 데이터 유효성 검사
- `CaveTerrainValidator.cs` 가 지형 설정 검증에 존재하나 아이템/AI 데이터 유효성 검사기 없음.
- `WorldItemDatabase.OnValidate()` 에서 에디터 시간 ID 갱신만 수행. 중복·범위 검사 없음.

## 설계·결정

- 수동 에디터 워크플로우 채택: 프로토타입 단계에서 파이프라인 구축 비용 대비 필요성 낮음. 데이터 규모가 수백 개 이하이므로 허용.
- SO 직접 편집 방식: 디자이너가 Unity를 직접 사용하는 경우 가장 직관적.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | **외부 데이터 임포트 완전 수동** | 아이템·AI 밸런스 조정이 대규모화되면 에디터 개별 수정에 시간이 선형 증가. 출시 후 핫픽스 속도 저하 직결. | Google Sheets → CSV → ScriptableObject 자동 임포터 에디터 도구 구축 |
| 높음 | **서버·클라 데이터 버전 불일치 감지 없음** | 멀티플레이에서 호스트와 클라의 `WorldItemDatabase` 아이템 목록이 다르면 잘못된 무기·아이템이 표시되거나 ID 매핑 오류 발생. 실측 미집행. | 빌드 타임에 데이터 해시 생성 + 접속 시 핸드셰이크 검증 |
| 높음 | **itemID 순서 의존** | 리스트 순서가 ID — 에디터에서 항목 순서 변경 시 기존 세이브 파일 장비 ID 전부 무효화. 자동화된 안전장치 없음. | 고정 ID 필드(`[SerializeField] int _fixedItemID`) 도입 또는 GUID 기반 |
| 보통 | **데이터 유효성 검사기 없음** | SO 중복 ID, 음수 데미지, null 프리팹 참조 등이 에디터에서 조용히 통과. 런타임에서 NRE 또는 예기치 않은 동작. | `ScriptableObject.OnValidate()` 에 Assert 체크 추가, 에디터 배치 검사 도구 |
| 낮음 | **데이터 변경 이력 추적 어려움** | SO가 바이너리 .asset (Git LFS) — diff 가 의미 없어 변경 내역이 불투명. | 중요 수치는 `#if UNITY_EDITOR` 코멘트 또는 changelog SO 추가 |

## 관련 문서

- [[scriptableobject-architecture|ScriptableObject 아키텍처]]
- [[save-load|세이브-로드]]

---
← [[06-data-hub|06 · 데이터 파이프라인]] · [[index|인덱스]]
