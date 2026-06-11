# ⑧ result — 결과 보고서 (상세)

목적: ①~⑦ 완료 시 결과를 종합하고 as-is→to-be 변화를 스냅샷 diff 로 첨부한다. 사이클을 종료·보관.

## 입력
- 전 단계 산출물 · `decisions.md` · 시작 시 `snapshots/`(as-is) · 종료 시점 스냅샷(to-be).

## 활동 체크포인트
1. **요약** — 무엇을 했는가(1문단).
2. **달성 대비표** — target/goal 대비 완료 여부(미완료·보류 항목 명시).
3. **as-is→to-be diff** — 시작 스냅샷과 종료 스냅샷을 비교(추가/변경 에셋 목록).
   - 종료 스냅샷: 쿡북 §7 덤프를 `snapshots/<ts>_after.json` 로 저장 후 diff.
4. **잔여 이슈 / 후속 사이클 제안**.
5. **게이트 결정 요약** — `decisions.md` 핵심을 인용·링크.

## 산출물 — `08_result.md`
섹션: 요약 · 달성 대비표 · diff · 잔여 이슈 · 게이트 결정 요약.

## G7 게이트 (Stop 강제)
- 보고서·diff 를 사용자에게 검토받고 **사인오프**한다.
- 사인오프 후:
  - `meta.json.status="signed_off"`, `awaiting_gate` 제거.
  - `_index.md` 에 완료 행 append.
  - 합의되면 사이클 폴더를 `archive/` 로 이동(`_index.md` 포인터 유지).
- 양식·기록: `references/gates.md`.

## 종료 후 (사용자 수동)
- git 커밋/PR(사용자). 결정 로그(`decisions.md`)는 이후 사이클의 컨텍스트가 된다.

## 흔한 실수
- diff 없이 "완료"만 보고 → 반드시 스냅샷 비교를 첨부.
- 미완료 task 를 숨김 → Stop 훅이 차단하며, 보고서에 잔여로 명시해야 함.
