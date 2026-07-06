---
name: test-run
description: >
  사이클이 정의한 씬/프리팹/Hierarchy/월드 셋팅을 실제 Unity 에 재현해 플레이로 검증하는 진입점.
  cycles/<cycle>/test_run/ 에 상세 테스트 정의·에셋 매핑을 정리하고, Assets/_TestRuns/<cycle>/ 에
  전체 에셋 구조(없으면 동작 stub 더미)를 unity-cli 로 생성·씬 셋업한 뒤, 에셋 로딩·참조·플레이를
  검증하고 테스트 환경·매핑 오류를 자동 수정한다. 부작용(폴더·에셋·씬 생성)이 있어 사용자가
  /test-run 으로 명시 호출할 때만 실행한다.
disable-model-invocation: true
---

# test-run — 테스트 씬 자동 셋업·플레이 검증 진입점

`/test-run <cycle-id> [slug]` 로만 실행된다(부작용 있는 진입점). 규약: `.harness/_conventions.md` §17.
한 사이클을 **반복 테스트**하는 전제이므로, 재실행 시 기존 더미·실제 에셋·매핑을 보존하고 델타만 반영한다.

## 0. 전제 확인 (셋팅 명시 — 없으면 멈춤)
대상 사이클의 **④ assets·⑥ test_env 에 씬/프리팹/Hierarchy/월드 셋팅이 구체적으로 명시**돼 있어야 한다.
- 씬 이름, 어떤 GO 가 어떤 부모(레이어/그룹) 밑에, 어떤 컴포넌트·핵심 필드값으로, 어떤 에셋(프리팹/머티리얼/SO)을 참조하는지.
- 명시가 부족하거나 "런타임 자동생성"뿐이면 → **자동 추측 금지**. 부족한 항목을 사용자에게 질문하고 멈춘다
  (예: "테스트 씬에 배치할 GO·컴포넌트가 ⑥에 없습니다. 무엇을 셋업할까요?").

## 1. 스캐폴딩
```bash
bash .claude/skills/unity-pipeline/scripts/scaffold_testrun.sh "<cycle-id>"
```
- `cycles/<cycle-id>/test_run/` 에 `test_def.md`·`asset_map.md`·`result.md`·`evidence/` 생성(기존 보존).
- 스크립트 **마지막 줄**이 test_run 경로 — 기억한다.

## 2. test_def.md 작성 (그 시점 문서 → 실행 사양)
사이클 ④ assets·⑥ test_env 를 읽어 **unity-cli 가 그대로 셋업할 수준**으로 구체화한다:
- 테스트 씬 경로(`Assets/_TestRuns/<cycle-id>/<cycle-id>_TestScene.unity`, 빌드 미포함), 렌더/월드 셋팅.
- to-be Hierarchy 트리(레이어→그룹→GO, 컴포넌트·필드값까지). 에셋 슬롯 목록(S1, S2 …)을 asset_map 과 1:1.
- 검증 항목(에셋 로딩·참조 missing 0·console 0·플레이 동작)을 체크리스트로.

## 3. 에셋 준비 (asset_map 따라 — 실제 링크 or 동작 stub)
`Assets/_TestRuns/<cycle-id>/assets/` 에 사이클이 정의한 **전체 디렉토리 구조**를 만들고 슬롯을 채운다(쿡북 §10):
- **폴더 생성**: `AssetDatabase.CreateFolder` 로 `_TestRuns/<cycle>/assets/Prefabs|Materials|…`(.meta 정합).
- **실제 에셋 있으면**(asset_map 의 실제 경로 또는 폴더 드롭) → 그대로 사용(덮지 않음).
- **없으면 동작 stub 생성**(§17-C): 프리팹=컴포넌트(있으면 실제 타입·없으면 placeholder)+primitive 메시+머티리얼,
  머티리얼=URP Lit 기본, SO=타입 존재 시 CreateInstance. 더미 이름에 `__DUMMY` 마커. asset_map "더미 생성 기록" 갱신.

## 4. 씬·Hierarchy·월드 셋업 (unity-cli)
- 새 씬 생성 → test_def 의 Hierarchy 대로 GO 생성·부모 지정·컴포넌트 부착·필드값 설정·프리팹 인스턴스화.
- 월드/런타임 셋팅(카메라·라이트·매니저 등) 적용 → 씬 저장 → `reserialize`(PostToolUse 훅 또는 직접).
- 파괴적 패턴은 G4 가드가 가로챈다.

## 5. 플레이 테스트 (test-runner 위임)
`test-runner` 서브에이전트에 위임 — 테스트 씬 로드 → `editor play --wait` → 검증:
- 에셋 슬롯 로딩(`AssetDatabase.LoadAssetAtPath != null`), 인스턴스 참조 missing 0,
  `console --type error` 0(무관 기존 에러 제외), 의도 동작(있으면). 증빙(콘솔·스크린샷)을 `test_run/evidence/` 에 저장.

## 6. 자동 수정 루프 (테스트 환경·매핑만 — §17-E)
플레이 실패 시 **테스트 환경·매핑 오류만** 고치고 4~5 재실행:
- 씬 배치 누락·더미 미생성·경로 오타·참조 미연결·컴포넌트 미부착 등 → 수정 후 재시도.
- **게임 본 코드(`Assets/Scripts/...`)는 수정 금지.** 실패 원인이 본 코드 버그로 의심되면 **G6** 로 선택지와 함께
  사용자에게 보고만 한다(result.md "G6 보고"). 자동 수정 내역은 result.md 에 기록.

## 7. 결과·증빙 (result.md)
검증 환경·시각, 셋업 결과, 플레이 판정, 자동 수정 내역, 콘솔 발췌, 스크린샷, G6 보고를 채운다(템플릿 준수).

## 8. 교체 안내 (더미 ↔ 실제)
사용자에게 안내: 더미를 실제로 바꾸려면 ① `asset_map.md` 표에서 슬롯의 `현재=실제`+실제 경로 기입, 또는
② `Assets/_TestRuns/<cycle-id>/assets/` 에 같은 경로·이름으로 실제 에셋 드롭 → `/test-run <cycle-id>` 재실행.

## 9. 정리
- `_TestRuns/<cycle>` 테스트 씬은 **빌드 씬 목록에 넣지 않는다**. 더미는 git 추적하되 빌드 산출 제외.
- 사이클 본 산출물(09 next 등)과 무관 — test-run 은 검증 보조이며 사이클 게이트(G7/G8)와 별개다.

## 인자
- `<cycle-id>` (필수): `.harness/cycles/` 의 사이클 폴더명(예: `2026-06-15_inventory`).

## 주의
- **부작용**(Assets/ 에 실제 에셋·씬 생성)이 있어 자동 트리거되지 않는다.
- Editor 가 offline 이면 셋업 불가 → Editor 기동 요청 후 진행.
- 본 코드 미수정 안전선을 지킨다(§17-E). 되돌리기 어려운 변경은 G4 가드가 확인.
