#!/usr/bin/env bash
# load-context.sh — SessionStart 훅 (계획서 A.1)
# 목적: 세션 시작 시 R1 컨텍스트(Unity 상태·최신 사이클·색인)를 stdout 으로 주입한다.
# stdout 전체가 Claude 컨텍스트로 추가된다. 절대 차단하지 않는다(항상 exit 0).
HOOK_NAME=load-context
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_lib.sh
. "$DIR/_lib.sh"

input="$(cat)"
source="$(json_field "$input" "source")"
[ -n "$source" ] || source="startup"

echo "=== Unity 하네스 컨텍스트 · source=${source} ==="

# ① Unity Editor 상태 (10s 타임아웃 — 세션 시작을 지연시키지 않는다)
if command -v unity-cli >/dev/null 2>&1; then
  status="$(HARNESS_UCLI_TIMEOUT=10 ucli status 2>/dev/null)"
  if [ -n "$status" ]; then
    printf '[unity] %s\n' "$(printf '%s' "$status" | tr -d '\n' | cut -c1-600)"
  else
    echo "[unity] Editor offline — unity-cli 무응답 (비차단). Editor를 열고 Connector 설치를 확인하세요."
  fi
else
  echo "[unity] unity-cli 미설치 — 상태 확인 생략 (비차단). 설치: 쿡북 references/cli-cookbook.md §0."
fi

# ②③ 최신 사이클 요약 + 대기 게이트 + 미완료 task 수
if cdir="$(cycle_dir)"; then
  echo "[cycle] 최신: $(basename "$cdir")"
  if [ -f "$cdir/meta.json" ]; then
    echo "  meta: $(tr -d '\n' < "$cdir/meta.json" | cut -c1-400)"
    gate="$(json_field "$(cat "$cdir/meta.json")" "awaiting_gate")"
    if [ -n "$gate" ] && [ "$gate" != "null" ]; then
      echo "  !! 대기 게이트: ${gate} — 사용자 확인 후 decisions.md 기록 필요."
    fi
  fi
  if [ -f "$cdir/07_plan.md" ]; then
    todo="$(grep -cE '^[[:space:]]*-[[:space:]]*\[[[:space:]]\]' "$cdir/07_plan.md" 2>/dev/null)"; todo="${todo:-0}"
    done="$(grep -cE '^[[:space:]]*-[[:space:]]*\[[xX]\]' "$cdir/07_plan.md" 2>/dev/null)"; done="${done:-0}"
    echo "  plan: 미완료 ${todo} · 완료 ${done}"
  fi
else
  echo "[cycle] 진행 중 사이클 없음 — /cycle-start <문서> 로 시작."
fi

# ④ 색인 꼬리 5줄
if [ -f "$HARNESS_DIR/_index.md" ]; then
  echo "[index] _index.md 최근:"
  tail -5 "$HARNESS_DIR/_index.md" 2>/dev/null | sed 's/^/  /'
fi

log "session start (source=$source, json=$(json_engine))"
exit 0
