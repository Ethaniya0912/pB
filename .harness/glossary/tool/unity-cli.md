---
type: tool
aliases: [Unity CLI]
---
# unity-cli

**분류**: tool · 에디터 제어 CLI

## 한 줄 정의
- 터미널(또는 Claude)이 Unity Editor를 **외부에서 명령으로 제어**하게 해주는 단일 바이너리 CLI — 상태 확인(`status`), 임의 C# 실행(`exec`), 재컴파일(`editor refresh --compile`), 플레이(`editor play --wait`), 콘솔 조회(`console`), 에셋 정합화(`reserialize`)를 제공한다. 본 하네스 자동화의 손발.

## 쉬운 설명
> 에디터에 마우스 대신 "말로 시키는 리모컨". 사람이 에디터에서 클릭으로 하던 일(플레이 누르기, 콘솔 보기, 오브젝트 만들기)을 명령어 한 줄로 시킬 수 있어서, Claude가 코드를 고치고→컴파일하고→실행해 보고→결과를 읽는 반복 루프를 자동으로 돌 수 있다.

## 등장 사이클
- (하네스 공통) 모든 사이클의 구현 루프·스냅샷·검증에서 사용. 명령 템플릿은 unity-pipeline 스킬의 `cli-cookbook.md` 참조.
- [[2026-06-12_netcode/06_test_env|2026-06-12_netcode ⑥ test_env]] — 플레이 스모크·컴포넌트 부착 확인에 사용

## 관련 용어
[[PROF-프리셋]] (토글 라이브 확인) · [[RNSM]] (표시 확인)

## 실제 위치
- 설치·사용 규칙: [`.harness/_conventions.md`](../../_conventions.md) §3 · 쿡북 [`cli-cookbook.md`](../../../.claude/skills/unity-pipeline/references/cli-cookbook.md)
