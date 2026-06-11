#!/usr/bin/env bash
# _lib.sh — 하네스 훅 공통 유틸리티. 모든 훅이 `source` 한다.
#
# 설계 원칙(계획서 A.0):
#   - 실패 안전: 인프라 오류(unity-cli/jq 부재·타임아웃)로 개발 흐름을 죽이지 않는다.
#     안전 게이트(guard)만 예외로 보수적으로 동작한다.
#   - 절대경로 우선: $CLAUDE_PROJECT_DIR(훅 실행 시 Claude Code가 주입)를 신뢰하되,
#     없으면 스크립트 위치에서 프로젝트 루트를 역산한다.
#
# 제공: HARNESS_PROJECT_DIR / HARNESS_DIR / HOOK_LOG
#       log()  json_field()  json_engine()  cycle_dir()

# ── 경로 해석 ────────────────────────────────────────────────────────────────
HARNESS_PROJECT_DIR="${CLAUDE_PROJECT_DIR:-}"
if [ -z "$HARNESS_PROJECT_DIR" ]; then
  # _lib.sh 는 <root>/.harness/hooks/_lib.sh 에 위치한다.
  HARNESS_PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fi
HARNESS_DIR="$HARNESS_PROJECT_DIR/.harness"
HOOK_LOG="$HARNESS_DIR/hooks/hook.log"

# ── 로깅 ─────────────────────────────────────────────────────────────────────
# log <message> : hook.log 에 타임스탬프 1줄 추가. 절대 실패하지 않는다.
log() {
  local msg="$1" ts
  ts="$(date '+%Y-%m-%d %H:%M:%S' 2>/dev/null || echo '----')"
  mkdir -p "$HARNESS_DIR/hooks" 2>/dev/null || true
  printf '%s [%s] %s\n' "$ts" "${HOOK_NAME:-hook}" "$msg" >> "$HOOK_LOG" 2>/dev/null || true
}

# ── JSON 엔진 감지 (jq → python → sed 폴백) ──────────────────────────────────
_JSON_ENGINE=""
_detect_json_engine() {
  [ -n "$_JSON_ENGINE" ] && return 0
  if command -v jq >/dev/null 2>&1; then _JSON_ENGINE="jq"
  elif command -v python3 >/dev/null 2>&1; then _JSON_ENGINE="python3"
  elif command -v python >/dev/null 2>&1 && python -c "import sys" >/dev/null 2>&1; then _JSON_ENGINE="python"
  elif command -v py >/dev/null 2>&1 && py -c "import sys" >/dev/null 2>&1; then _JSON_ENGINE="py"
  else _JSON_ENGINE="sed"; fi
}
json_engine() { _detect_json_engine; printf '%s' "$_JSON_ENGINE"; }

# json_field <json-string> <dotted.path>  → 값 출력(없으면 빈 문자열)
# 예: json_field "$input" "tool_input.command"
json_field() {
  local json="$1" path="$2"
  _detect_json_engine
  case "$_JSON_ENGINE" in
    jq)
      printf '%s' "$json" | jq -r --arg p "$path" \
        '(getpath($p|split(".")) // "") | if type=="string" then . else tojson end' 2>/dev/null
      ;;
    python3|python|py)
      printf '%s' "$json" | "$_JSON_ENGINE" -c '
import sys, json
try:
    d = json.load(sys.stdin)
except Exception:
    print(""); sys.exit(0)
cur = d
for k in sys.argv[1].split("."):
    if isinstance(cur, dict) and k in cur:
        cur = cur[k]
    else:
        cur = ""; break
if cur is None: cur = ""
print(cur if isinstance(cur, str) else json.dumps(cur, ensure_ascii=False))
' "$path" 2>/dev/null
      ;;
    *)
      # 최후 폴백: 평면적인 "key":"value" 만 추출(중첩 경로는 마지막 키 사용).
      local key="${path##*.}"
      printf '%s' "$json" \
        | tr -d '\n' \
        | sed -n "s/.*\"$key\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" \
        | head -1
      ;;
  esac
}

# cycle_dir : 가장 최신 사이클 폴더의 절대경로를 출력. 없으면 비제로 종료.
# 정렬 규칙: YYYY-MM-DD_<slug> 는 사전식 정렬이 곧 시간순이다.
cycle_dir() {
  local cycles="$HARNESS_DIR/cycles" latest
  [ -d "$cycles" ] || return 1
  latest="$(ls -1 "$cycles" 2>/dev/null | grep -E '^[0-9]{4}-[0-9]{2}-[0-9]{2}_' | sort | tail -1)"
  [ -n "$latest" ] || return 1
  printf '%s' "$cycles/$latest"
}
