---
name: cycle-start
description: >
  새 개발 사이클을 스캐폴딩하는 진입점. 기획 문서를 입력으로 .harness/cycles/ 폴더를 만들고
  원본을 보관하고 as-is 스냅샷을 떠서 unity-pipeline 절차를 시작한다. 부작용(폴더·파일 생성)이
  있으므로 사용자가 /cycle-start 로 명시 호출할 때만 실행한다.
disable-model-invocation: true
---

# cycle-start — 사이클 스캐폴딩 진입점

`/cycle-start <문서경로> [slug]` 로만 실행된다(부작용 있는 진입점이라 모델 자동 호출 비활성).

## 동작
1. **스캐폴딩 스크립트 실행** — 사이클 폴더·템플릿·스냅샷을 만든다:
   ```bash
   bash .claude/skills/unity-pipeline/scripts/scaffold_cycle.sh "<문서경로>" "[slug]"
   ```
   - `.harness/cycles/<YYYY-MM-DD_slug>/` 에 `00_input/`, `05_spec/`, `evidence/`(증빙 보관),
     템플릿 `01~09`(⑨ next 포함), `meta.json`, `decisions.md` 가 생성된다.
   - 템플릿의 `<cycle-id>` 플레이스홀더가 실제 사이클 ID 로 치환된다(Foam 위키링크 활성화).
   - 입력 문서가 `00_input/` 에 복사된다.
   - unity-cli 가 있으면 `snapshots/<...>_before.txt` 에 as-is 덤프가 저장된다(Editor 직접 기록 — stdout 잘림 회피).
   - `_index.md` 에 시작 행이 추가되고, `glossary/` 분류 폴더(용어 사전)가 보장된다.
   - 스크립트 **마지막 줄**이 생성된 사이클 경로다 — 이를 기억한다.

2. **입력 점검** — `00_input/` 의 문서를 읽고, 인자가 비었으면 사용자에게 문서 경로를 묻는다.

3. **파이프라인 시작** — 곧바로 `unity-pipeline` 스킬 절차의 **① target** 부터 진행한다.
   (cycle-start 는 스캐폴딩만 책임지고, 분석·구현은 unity-pipeline 이 수행한다.)

## 인자
- `<문서경로>` (필수): 기획/구현/기술 문서. 여러 개면 첫 번째로 slug 를 정하고 나머지도 복사 안내.
- `[slug]` (선택): 폴더명에 쓸 짧은 식별자(소문자-하이픈). 없으면 파일명에서 도출.

## 예시
```
/cycle-start docs/기획_인벤토리.md inventory
→ .harness/cycles/2026-06-11_inventory/ 생성 → ① target 시작
```

## 주의
- 이 스킬은 **부작용**이 있으므로 자동 트리거되지 않는다. 분석만 필요하면 `unity-pipeline` 을 쓴다.
- Editor 가 offline 이면 스냅샷은 생략되고 경고만 뜬다(비차단). 이후 실행 단계 전 Editor 기동을 요청.
