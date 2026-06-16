---
title: 06-data-hub
tags: [moc]
status: done
source: []
verified: 2026-06-15
---

# 06 · 데이터 파이프라인

pB 데이터 아키텍처 현황 요약. SO 기반 데이터 저장은 광범위하게 사용되지만 외부 임포트 자동화와 버전 관리는 미구현이다.

## 구현 현황 요약

| 항목 | 상태 | 비고 |
|---|---|---|
| SO 기반 데이터 저장 | 구현 완료 | 187개+ SO 자산, 카테고리별 디렉토리 정리 |
| 런타임 DB 등록 | 구현 완료 | `WorldItemDatabase` — ID 딕셔너리 기반 조회 |
| 외부 데이터 임포트 | 미구현 | 수동 에디터 편집만 |
| 버전 일치 검증 | 미구현 | 클라/호스트 DB 불일치 감지 없음 |
| 데이터 유효성 검사 | 미구현 (지형 한정) | `CaveTerrainValidator` 만 존재 |

## 직렬화 지점 (코드 맵)

> 데이터가 어디서 어떤 형식으로 직렬화되나 — 4종 (조사: 2026-06-15).

**① 세이브 — JSON (`JsonUtility`)**
- `Assets/Scripts/Game Saving/` : `SaveFileDataWriter.cs`(파일 입출력) · `WorldSaveData.cs` · `CharacterSaveData.cs` · `SerializableDictionary.cs`(`ISerializationCallbackReceiver`)
- `Assets/Scripts/World Manager/` : `WorldSaveGameManager.cs`(5슬롯 오케스트레이션) · `WorldGameStateManager.cs`

**② 네트워크 — NGO 바이너리 (`INetworkSerializable` / `NetworkVariable`)**
- `Bridges&Interfaces/Interfaces/Domain/CharacterDomainInterfaces.cs` · `pB-4/week1_AI_HumanoidAI/HumanoidAIBrain.cs` · `pB-4/week2_AI/UtilityMasterFormula.cs` · `Inventory/InventoryItem.cs`

**③ 에셋 — ScriptableObject (YAML `.asset`)**
- `Inventory/` Item·WeaponItem SO → [[scriptableobject-architecture|SO-아키텍처]]

**④ 캐시·기록 — 디스크 직렬화**
- `Utilities/Cave Genderator/CaveDiskCache.cs`(동굴 청크 캐시) · `pB-4/week7_Scenario/IncidentRecorder.cs`(사건 기록)

> ⚠ **세이브 직렬화 비판**: 버전 마이그레이션 필드 없음(필드 추가 시 구 세이브 호환 깨짐) · itemID 순서 의존(SO 순서 변경 시 장비 무효) — [[save-load|세이브-로드]]·[[scriptableobject-architecture|SO-아키텍처]] 참조.

## 문서

- [[scriptableobject-architecture|scriptableobject-아키텍처]] — 187개+ SO 자산, WeaponItem 계층, P1-5 누수 위험
- [[data-pipeline|데이터-파이프라인]] — 수동 워크플로우, 외부 임포트·버전 검증 모두 미구현
- [[save-load|세이브-로드]] — JSON 5슬롯, 멀티플레이 분기

---
← [[index|인덱스]]
