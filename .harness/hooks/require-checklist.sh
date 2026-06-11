#!/usr/bin/env bash
# require-checklist.sh — Stop 훅 (계획서 A.4, 게이트 G1·2·3·5·7)
# 목적: 체크리스트 미완료·색인 미갱신·게이트 결정 미기록 상태로 사이클이 종료되는 것을 막는다.
# 출력: {"decision":"block","reason":"..."} 또는 통과(무출력). 항상 exit 0.
HOOK_NAME=require-checklist
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_lib.sh
. "$DIR/_lib.sh"

input="$(cat)"

# ① 무한루프 방지: 이미 Stop 훅으로 막힌 상태면 즉시 통과
active="$(json_field "$input" "stop_hook_active")"
[ "$active" = "true" ] && exit 0

# 사이클이 없으면 일반 대화 → 영향 없음(통과)
cdir="$(cycle_dir)" || exit 0

# 이미 완료/보관된 사이클이면 통과
status=""
[ -f "$cdir/meta.json" ] && status="$(json_field "$(cat "$cdir/meta.json")" "status")"
case "$status" in
  done|complete|completed|archived|signed_off|signoff) exit 0 ;;
esac

base="$(basename "$cdir")"
reasons=""

# ② 미체크 task
if [ -f "$cdir/07_plan.md" ]; then
  todo="$(grep -cE '^[[:space:]]*-[[:space:]]*\[[[:space:]]\]' "$cdir/07_plan.md" 2>/dev/null)"; todo="${todo:-0}"
  [ "$todo" -gt 0 ] 2>/dev/null && reasons="${reasons}미체크 task ${todo}건; "
fi

# ② 색인 갱신 여부
if [ -f "$HARNESS_DIR/_index.md" ]; then
  grep -q "$base" "$HARNESS_DIR/_index.md" 2>/dev/null || reasons="${reasons}_index.md에 '${base}' 미등록; "
else
  reasons="${reasons}_index.md 부재; "
fi

# ② meta.json 존재
[ -f "$cdir/meta.json" ] || reasons="${reasons}meta.json 부재; "

# ③ 게이트 대기 중이면 decisions.md 에 결정이 기록됐는지 확인
gate=""
[ -f "$cdir/meta.json" ] && gate="$(json_field "$(cat "$cdir/meta.json")" "awaiting_gate")"
if [ -n "$gate" ] && [ "$gate" != "null" ]; then
  if ! { [ -f "$cdir/decisions.md" ] && grep -q "$gate" "$cdir/decisions.md" 2>/dev/null; }; then
    reasons="${reasons}게이트 ${gate} 결정 미기록(decisions.md); "
  fi
fi

if [ -n "$reasons" ]; then
  log "BLOCK: $reasons"
  printf '{"decision":"block","reason":"사이클 [%s] 마감 전 정리 필요 — %s게이트 문서화·색인(_index.md/meta.json) 갱신 후 종료하세요."}\n' \
    "$(json_escape "$base")" "$(json_escape "$reasons")"
  exit 0
fi

log "pass: $base"
exit 0
