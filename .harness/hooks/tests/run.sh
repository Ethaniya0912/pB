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

echo "== post-asset-edit · 파일명=클래스명 린트 =="
# STEAL/MULTI 케이스는 unity-cli 호출 전에 exit 2 로 단락 → 실제 에디터 비접촉(테스트 안전).
LTMP="$(mktemp -d 2>/dev/null || echo "${TMPDIR:-/tmp}/harness_lint_$$")"; mkdir -p "$LTMP/Assets/Net"
# STEAL: 파일명 동명 타입(static class NetSimProfiles)이 바인딩을 가로채 NetSimController 고아 (실제 회귀 형태)
printf 'using UnityEngine;\npublic static class NetSimProfiles {}\npublic class NetSimController : MonoBehaviour {}\n' > "$LTMP/Assets/Net/NetSimProfiles.cs"
ljson="{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"$LTMP/Assets/Net/NetSimProfiles.cs\"},\"session_id\":\"t1\"}"
lerr="$(printf '%s' "$ljson" | CLAUDE_PROJECT_DIR="$LTMP" bash "$HOOKS/post-asset-edit.sh" 2>&1 >/dev/null)"
assert_contains "post-STEAL(동명 타입 가로챔 → 경고)" "$lerr" '가로채'
assert_contains "post-STEAL 위반 클래스명 명시" "$lerr" 'NetSimController'
# MULTI: 한 파일에 MB 2개 → 경고
printf 'using UnityEngine;\npublic class A : MonoBehaviour {}\npublic class B : MonoBehaviour {}\n' > "$LTMP/Assets/Net/Pair.cs"
mjson="{\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"$LTMP/Assets/Net/Pair.cs\"},\"session_id\":\"t1\"}"
merr="$(printf '%s' "$mjson" | CLAUDE_PROJECT_DIR="$LTMP" bash "$HOOKS/post-asset-edit.sh" 2>&1 >/dev/null)"
assert_contains "post-MULTI(MB 2개 → 경고)" "$merr" 'MonoBehaviour 다수'
rm -rf "$LTMP" 2>/dev/null

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

echo "== _lib · unity_object_classes (.cs 정적 검사) =="
ULTMP="$(mktemp -d 2>/dev/null || echo "${TMPDIR:-/tmp}/harness_lib_$$")"; mkdir -p "$ULTMP"
printf 'using UnityEngine;\npublic class NetSimController : MonoBehaviour { void Update(){} }\n' > "$ULTMP/a.cs"
oc="$(bash -c ". '$HOOKS/_lib.sh'; unity_object_classes '$ULTMP/a.cs'")"
assert_contains "lib-MB 파생 클래스 추출" "$oc" 'NetSimController'
# struct/static 만 있는 파일(수정 후 NetSimProfiles.cs 모양)은 미검출
printf 'namespace N {\n  public readonly struct P { public int X; }\n  public static class NetSimProfiles { public static void Set(){} }\n}\n' > "$ULTMP/b.cs"
oc2="$(bash -c ". '$HOOKS/_lib.sh'; unity_object_classes '$ULTMP/b.cs'")"
assert_empty "lib-비-MB(struct/static)는 미검출" "$oc2"
# abstract MB 베이스는 첨부 불가 → 제외(파일명 규칙 면제)
printf 'using UnityEngine;\npublic abstract class Base : MonoBehaviour {}\n' > "$ULTMP/c.cs"
oc3="$(bash -c ". '$HOOKS/_lib.sh'; unity_object_classes '$ULTMP/c.cs'")"
assert_empty "lib-abstract MB 제외" "$oc3"
# 주석 처리된 선언은 미검출(GroupAIManager.Debug.cs 류 partial 분할 파일의 주석 오탐 방지)
printf 'using UnityEngine;\n// public class Commented : MonoBehaviour {}\n  //  public partial class Foo : MonoBehaviour, IBar\n' > "$ULTMP/d.cs"
oc4="$(bash -c ". '$HOOKS/_lib.sh'; unity_object_classes '$ULTMP/d.cs'")"
assert_empty "lib-주석 처리된 선언 미검출" "$oc4"
# 린트 합성(미스매치 검출 / 일치 통과) — stem 비교까지 포함
mm="$(bash -c ". '$HOOKS/_lib.sh'; unity_object_classes '$ULTMP/a.cs' | grep -vxF 'NetSimController' || true")"
assert_empty "lib-stem 일치 시 미스매치 없음" "$mm"
mm2="$(bash -c ". '$HOOKS/_lib.sh'; unity_object_classes '$ULTMP/a.cs' | grep -vxF 'NetSimProfiles' || true")"
assert_contains "lib-stem 불일치 시 위반 검출" "$mm2" 'NetSimController'
rm -rf "$ULTMP" 2>/dev/null

echo "== _lib · unity_filename_risk (정밀 판정 — Unity 실동작 기준) =="
RTMP="$(mktemp -d 2>/dev/null || echo "${TMPDIR:-/tmp}/harness_risk_$$")"; mkdir -p "$RTMP"
# STEAL: 파일명 동명 타입이 바인딩 가로챔 → 위험
printf 'using UnityEngine;\npublic static class NetSimProfiles {}\npublic class NetSimController : MonoBehaviour {}\n' > "$RTMP/NetSimProfiles.cs"
r1="$(bash -c ". '$HOOKS/_lib.sh'; unity_filename_risk '$RTMP/NetSimProfiles.cs' 'NetSimProfiles'")"
assert_contains "lib-STEAL 검출" "$r1" 'STEAL'
assert_contains "lib-STEAL 대상=NetSimController" "$r1" 'NetSimController'
# MULTI: MB 2개 → 위험
printf 'using UnityEngine;\npublic class A : MonoBehaviour {}\npublic class B : MonoBehaviour {}\n' > "$RTMP/Pair.cs"
r2="$(bash -c ". '$HOOKS/_lib.sh'; unity_filename_risk '$RTMP/Pair.cs' 'Pair'")"
assert_contains "lib-MULTI 검출" "$r2" 'MULTI'
# BENIGN: 단일 이름불일치(동명 타입 없음) → Unity 폴백 바인딩 → 무위험 (TriggerProxy 형태)
printf 'using UnityEngine;\npublic class Bar : MonoBehaviour {}\n' > "$RTMP/Foo.cs"
r3="$(bash -c ". '$HOOKS/_lib.sh'; unity_filename_risk '$RTMP/Foo.cs' 'Foo'")"
assert_empty "lib-단일 이름불일치 무위험(폴백 바인딩)" "$r3"
# 파일명=클래스명 일치 → 무위험
printf 'using UnityEngine;\npublic class Match : MonoBehaviour {}\n' > "$RTMP/Match.cs"
r4="$(bash -c ". '$HOOKS/_lib.sh'; unity_filename_risk '$RTMP/Match.cs' 'Match'")"
assert_empty "lib-파일명=클래스명 무위험" "$r4"
rm -rf "$RTMP" 2>/dev/null

echo "== load-context =="
out="$(run_hook load-context.sh session-startup.json)";    assert_contains "session-컨텍스트 헤더" "$out" 'Unity 하네스 컨텍스트'

echo
echo "RESULT: PASS=$PASS FAIL=$FAIL"
[ "$FAIL" -eq 0 ]
