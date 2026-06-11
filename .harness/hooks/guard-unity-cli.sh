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

# ① unity-cli 명령이 아니면 즉시 통과(allow)
case "$cmd" in
  *unity-cli*) : ;;
  *) exit 0 ;;
esac

emit() { # emit <ask|deny> <reason>
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"%s","permissionDecisionReason":"%s"}}\n' "$1" "$2"
}

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
