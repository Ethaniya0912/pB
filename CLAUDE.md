# pB — Claude Code 프로젝트 가이드

## 프로젝트
- Unity 6.3 LTS (6000.3.x) 게임 프로젝트. 에셋은 `Assets/`, 하네스 산출물은 `.harness/`.
- 개발 자동화 하네스 적용: 훅 4종(`.harness/hooks/`) + `unity-pipeline` 스킬.
  사람용 매뉴얼: `.harness/usage-guide.html` (브라우저로 열기).

## 중요 규칙
- 사이클 단위 개발(기획서 분석 → 구현)은 반드시 `/cycle-start <문서>` 로 시작한다.
- 게이트(G1·G2·G3·G5·G7)에 도달하면 **멈추고 사용자 확인**을 받는다 — 이것이 정상 동작이다.
  결정은 해당 사이클의 `decisions.md` 에 기록한다(미기록 시 Stop 훅이 종료를 차단).
- `.harness/cycles/` 산출물과 `_index.md` 는 append-only — 삭제·기존 행 수정 금지.
- `.prefab/.unity/.asset/.mat` 등 에셋 편집 후 reserialize 는 **PostToolUse 훅이 자동 수행**한다.
  수동으로 호출하지 않는다. 훅이 stderr 로 콘솔 에러를 돌려주면 그것을 수정한다.

## unity-cli (v0.3.x 문법 — 구버전 예제 주의)
- `--json` / `--port` 플래그는 **존재하지 않는다**. 상태는 `unity-cli status`,
  콘솔 에러는 `unity-cli console --type error`.
- 명령 템플릿(검증됨): `.claude/skills/unity-pipeline/references/cli-cookbook.md`
- **프로젝트 루트에서 실행**해야 멀티 인스턴스 시 올바른 Editor 가 선택된다(CWD 우선 매칭).
- 복잡한 C# 은 stdin 파이프: `echo '...; return null;' | unity-cli exec`

## 검증
- 훅/하네스 스크립트를 수정하면 반드시 실행: `bash .harness/hooks/tests/run.sh` → 전체 PASS 확인.
- 구현 task 판정: compile 에러 0 + `editor play --wait` 동작 확인 + console error 0.
