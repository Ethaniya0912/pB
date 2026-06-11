#!/usr/bin/env bash
# guard-unity-cli.sh — PreToolUse 훅 / matcher: Bash (계획서 A.2, 게이트 G4)
# 목적: 되돌리기 어려운 unity-cli 명령을 사람 승인 게이트(ask) 또는 차단(deny)으로 보낸다.
# 출력: hookSpecificOutput JSON (ask/deny) 또는 빈 출력(allow). 항상 exit 0.
# 보수적 동작: 안전 게이트이므로 jq 부재여도 폴백 추출로 패턴 검사를 수행한다.
HOOK_NAME=guard-unity-cli
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_lib.sh
. "$DIR/_lib.sh"

input="$(cat)"
cmd="$(json_field "$input" "tool_input.command")"

# 추출 실패 폴백(보수적): cmd 가 비었는데 입력에 unity-cli 흔적이 있으면 raw 입력으로 패턴 검사.
# 안전 게이트는 "파싱 실패 → 통과" 가 아니라 "파싱 실패 → 원문 검사" 로 동작해야 한다.
if [ -z "$cmd" ]; then
  case "$input" in *unity-cli*|*rm\ *|*git\ clean*|*git\ reset*) cmd="$input" ;; esac
fi

emit() { # emit <ask|deny> <reason>
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"%s","permissionDecisionReason":"%s"}}\n' "$1" "$(json_escape "$2")"
}

# ⓪ 비-unity-cli 프로젝트 파괴 명령 → ask (Unity 핵심 폴더·하네스 산출물 보호)
if printf '%s' "$cmd" | grep -Eq \
   '(^|[^[:alnum:]_-])rm[[:space:]]+(-[a-zA-Z]*r[a-zA-Z]*[[:space:]]+)+[^|;&]*("?\.?/?)(Assets|ProjectSettings|Packages|\.harness)|(^|[^[:alnum:]_-])git[[:space:]]+clean[[:space:]]+-[a-zA-Z]*f|(^|[^[:alnum:]_-])git[[:space:]]+reset[[:space:]]+--hard'; then
  log "ASK(shell): $(printf '%s' "$cmd" | cut -c1-160)"
  emit "ask" "Unity 핵심 폴더(Assets/ProjectSettings/Packages/.harness) 또는 작업트리 파괴 가능 명령 — 사람 승인 필요 (G4)."
  exit 0
fi

# ① unity-cli 명령이 아니면 즉시 통과(allow)
case "$cmd" in
  *unity-cli*) : ;;
  *) exit 0 ;;
esac

# ④ DENY — 명백한 비복구·대량 연산(최소한으로만)
if printf '%s' "$cmd" | grep -Eiq \
   'reserialize[^|;&]*(--all|--recursive|(^| )-r( |$))|reserialize[[:space:]]+"?Assets/?"?[[:space:]]*([|;&]|$)|DeleteAllAssets|AssetDatabase\.DeleteAsset\([[:space:]]*"Assets"'; then
  log "DENY: $cmd"
  emit "deny" "프로젝트 전체 대상 비복구 연산 감지 — 단일 에셋으로 범위를 좁히거나 사람이 직접 수행하세요 (G4)."
  exit 0
fi

# ③ ASK — 기존 에셋 변경/삭제·씬 대량 변경 패턴
if printf '%s' "$cmd" | grep -Eiq \
   '(reserialize|DeleteAsset|DestroyImmediate|[^A-Za-z]Destroy\(|AssetDatabase\.[A-Za-z]*Delete|MoveAssetToTrash|File\.Delete|Directory\.Delete|menu[[:space:]]+"File/Save Project"|RemoveComponent|EditorSceneManager\.(Save|Close))'; then
  log "ASK: $cmd"
  emit "ask" "기존 에셋·씬 변경/삭제 가능 명령 — 사람 승인 필요 (G4)."
  exit 0
fi

# ② 안전 명령(status / console / read-only exec / tool list·help 등) → allow
log "allow: $(printf '%s' "$cmd" | cut -c1-120)"
exit 0
