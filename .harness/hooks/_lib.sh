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
#       log()  json_field()  json_engine()  json_escape()  cycle_dir()  ucli()

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
# 존재 확인이 아니라 **실제 파싱 셀프테스트**로 검증한다 — PATH 에 있어도 깨진
# 셔틀일 수 있다(winget Links 심링크가 Git Bash 에서 실행 실패, 스토어 파이썬 스텁,
# 정책 차단 exe 등). 동작이 확인된 엔진만 채택한다.
_JSON_ENGINE=""
_detect_json_engine() {
  [ -n "$_JSON_ENGINE" ] && return 0
  if command -v jq >/dev/null 2>&1 \
     && [ "$(printf '{"k":"v"}' | jq -r '.k' 2>/dev/null)" = "v" ]; then
    _JSON_ENGINE="jq"; return 0
  fi
  local _py
  for _py in python3 python py; do
    if command -v "$_py" >/dev/null 2>&1 \
       && [ "$(printf '{"k":"v"}' | "$_py" -c 'import sys,json;print(json.load(sys.stdin)["k"])' 2>/dev/null)" = "v" ]; then
      _JSON_ENGINE="$_py"; return 0
    fi
  done
  _JSON_ENGINE="sed"
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

# json_escape <string> : JSON 문자열 리터럴 내부에 안전하게 들어가도록 이스케이프.
# 제어문자(개행 포함)는 공백화·제거 — 엔진 불요, 결정적 동작.
json_escape() {
  printf '%s' "$1" | tr '\n\r\t' '   ' | tr -d '\000-\037' | sed 's/\\/\\\\/g; s/"/\\"/g'
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

# ── unity-cli 타임아웃 래퍼 ──────────────────────────────────────────────────
# ucli <args...> : GNU timeout 이 있으면 제한 시간(기본 50s) 내로 unity-cli 실행.
# Editor 행(컴파일 교착·무응답) 시 훅 전체가 Claude 훅 타임아웃에 걸려 죽는 것을 방지.
# 주의: Windows system32 의 timeout.exe 는 GNU 와 다르므로 --version 으로 판별한다.
_UCLI_TIMEOUT_BIN=""
_detect_timeout() {
  [ -n "$_UCLI_TIMEOUT_BIN" ] && return 0
  if command -v timeout >/dev/null 2>&1 && timeout --version >/dev/null 2>&1; then
    _UCLI_TIMEOUT_BIN="timeout"
  else
    _UCLI_TIMEOUT_BIN="none"
  fi
}
ucli() {
  local t="${HARNESS_UCLI_TIMEOUT:-50}"
  _detect_timeout
  if [ "$_UCLI_TIMEOUT_BIN" = "timeout" ]; then
    timeout "${t}s" unity-cli "$@"
  else
    unity-cli "$@"
  fi
}
