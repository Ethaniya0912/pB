#!/usr/bin/env bash
# run.sh — 훅 픽스처 테스트 러너 (계획서 A.6)
# 각 케이스에 stdin JSON 을 파이프하고 stdout/exit code 를 단언한다.
# 사용: bash .harness/hooks/tests/run.sh
set -u
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOOKS="$(cd "$DIR/.." && pwd)"
PASS=0; FAIL=0

# assert_contains <name> <output> <needle>
assert_contains() {
  if printf '%s' "$2" | grep -q -- "$3"; then echo "  PASS $1"; PASS=$((PASS+1));
  else echo "  FAIL $1 — '$3' 미포함. 실제: $(printf '%s' "$2" | head -c 200)"; FAIL=$((FAIL+1)); fi
}
# assert_empty <name> <output>
assert_empty() {
  if [ -z "$(printf '%s' "$2" | tr -d '[:space:]')" ]; then echo "  PASS $1"; PASS=$((PASS+1));
  else echo "  FAIL $1 — 비어있어야 함. 실제: $(printf '%s' "$2" | head -c 200)"; FAIL=$((FAIL+1)); fi
}
run_hook() { # run_hook <script> <fixture>  → stdout, sets RC
  out="$(bash "$HOOKS/$1" < "$DIR/$2" 2>/dev/null)"; RC=$?; printf '%s' "$out"
}

echo "== guard-unity-cli =="
out="$(run_hook guard-unity-cli.sh guard-safe.json)";      assert_empty   "guard-안전(console)" "$out"
out="$(run_hook guard-unity-cli.sh guard-status.json)";    assert_empty   "guard-안전(status)"  "$out"
out="$(run_hook guard-unity-cli.sh guard-danger.json)";    assert_contains "guard-위험(DeleteAsset→ask)" "$out" '"permissionDecision":"ask"'
out="$(run_hook guard-unity-cli.sh guard-deny.json)";      assert_contains "guard-비복구(→deny)"          "$out" '"permissionDecision":"deny"'
out="$(run_hook guard-unity-cli.sh guard-nonunity.json)";  assert_empty   "guard-비unity(git status)" "$out"
out="$(run_hook guard-unity-cli.sh guard-rmrf.json)";      assert_contains "guard-셸파괴(rm -rf Assets→ask)" "$out" '"permissionDecision":"ask"'
out="$(run_hook guard-unity-cli.sh guard-gitreset.json)";  assert_contains "guard-셸파괴(git reset --hard→ask)" "$out" '"permissionDecision":"ask"'
out="$(run_hook guard-unity-cli.sh guard-rmsafe.json)";    assert_empty   "guard-셸안전(rm -rf node_modules)" "$out"

echo "== post-asset-edit =="
out="$(run_hook post-asset-edit.sh post-nonasset.json)";   assert_empty   "post-비에셋(README.md)" "$out"
# 에셋 케이스: unity-cli 부재 환경에서는 stderr 경고 + exit 0 (비차단). stdout 은 비어야 함.
out="$(run_hook post-asset-edit.sh post-asset.json)";      assert_empty   "post-에셋 stdout(무출력)" "$out"
out="$(run_hook post-asset-edit.sh post-anim-abs.json)";   assert_empty   "post-에셋 절대경로(.anim) stdout(무출력)" "$out"
# Assets/Packages 외부 파일은 경로 필터로 무시(unity-cli 경고조차 없어야 함 → stderr 도 빈 값)
err="$(bash "$HOOKS/post-asset-edit.sh" < "$DIR/post-outside.json" 2>&1 >/dev/null)"
assert_empty "post-프로젝트외부(.cs → 완전 무시)" "$err"

echo "== require-checklist =="
out="$(run_hook require-checklist.sh stop-loopguard.json)"; assert_empty  "stop-무한루프가드(active=true)" "$out"
out="$(run_hook require-checklist.sh stop-nocycle.json)";   assert_empty  "stop-사이클없음(통과)" "$out"

echo "== require-checklist (block 경로, 격리 사이클) =="
TMP="$(mktemp -d 2>/dev/null || echo "${TMPDIR:-/tmp}/harness_test_$$")"; mkdir -p "$TMP"
mkdir -p "$TMP/.harness/cycles/2026-06-11_test"
printf -- '- [ ] task A\n- [x] task B\n' > "$TMP/.harness/cycles/2026-06-11_test/07_plan.md"
printf '{"status":"implementing"}' > "$TMP/.harness/cycles/2026-06-11_test/meta.json"
: > "$TMP/.harness/_index.md"   # 색인에 미등록 상태
out="$(CLAUDE_PROJECT_DIR="$TMP" bash "$HOOKS/require-checklist.sh" < "$DIR/stop-nocycle.json" 2>/dev/null)"
assert_contains "stop-미완료(미체크 task → block)" "$out" '"decision":"block"'
assert_contains "stop-미완료(사유에 미체크 1건)" "$out" '미체크 task 1건'
# 전 task 체크 + 색인 등록 → 통과
printf -- '- [x] task A\n- [x] task B\n' > "$TMP/.harness/cycles/2026-06-11_test/07_plan.md"
printf '2026-06-11_test\n' > "$TMP/.harness/_index.md"
out="$(CLAUDE_PROJECT_DIR="$TMP" bash "$HOOKS/require-checklist.sh" < "$DIR/stop-nocycle.json" 2>/dev/null)"
assert_empty "stop-완료(전 task 체크 → 통과)" "$out"
# awaiting_gate 가 걸렸는데 decisions.md 에 기록이 없으면 block
printf '{"status":"implementing","awaiting_gate":"G2"}' > "$TMP/.harness/cycles/2026-06-11_test/meta.json"
out="$(CLAUDE_PROJECT_DIR="$TMP" bash "$HOOKS/require-checklist.sh" < "$DIR/stop-nocycle.json" 2>/dev/null)"
assert_contains "stop-게이트 미기록(G2 → block)" "$out" '게이트 G2 결정 미기록'
# decisions.md 에 G2 기록 → 통과
printf '## G2 (2026-06-11) — 승인\n' > "$TMP/.harness/cycles/2026-06-11_test/decisions.md"
out="$(CLAUDE_PROJECT_DIR="$TMP" bash "$HOOKS/require-checklist.sh" < "$DIR/stop-nocycle.json" 2>/dev/null)"
assert_empty "stop-게이트 기록 후(통과)" "$out"
rm -rf "$TMP" 2>/dev/null

echo "== _lib (단위) =="
esc="$(bash -c ". '$HOOKS/_lib.sh'; json_escape 'a\"b\\c
d'")"
assert_contains "lib-json_escape(따옴표)" "$esc" 'a\\"b'
out="$(bash -c ". '$HOOKS/_lib.sh'; json_escape 'x' | head -c 9999" )"
assert_contains "lib-json_escape(통상 문자열)" "$out" 'x'
# json_field 실동작 — 감지된 엔진이 무엇이든 실제 값을 돌려줘야 한다
# (jq 가 PATH 에 있지만 실행이 깨진 환경에서 G2 게이트 테스트가 침묵 실패하던 회귀 방지)
eng="$(bash -c ". '$HOOKS/_lib.sh'; json_engine")"
jf="$(bash -c ". '$HOOKS/_lib.sh'; json_field '{\"a\":{\"b\":\"val_xyz\"}}' 'a.b'")"
assert_contains "lib-json_field(중첩 a.b, engine=$eng)" "$jf" 'val_xyz'
jf2="$(bash -c ". '$HOOKS/_lib.sh'; json_field '{\"status\":\"implementing\",\"awaiting_gate\":\"G2\"}' 'awaiting_gate'")"
assert_contains "lib-json_field(평면 awaiting_gate)" "$jf2" 'G2'

echo "== load-context =="
out="$(run_hook load-context.sh session-startup.json)";    assert_contains "session-컨텍스트 헤더" "$out" 'Unity 하네스 컨텍스트'

echo
echo "RESULT: PASS=$PASS FAIL=$FAIL"
[ "$FAIL" -eq 0 ]
