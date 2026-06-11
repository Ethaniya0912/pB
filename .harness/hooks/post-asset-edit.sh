#!/usr/bin/env bash
# post-asset-edit.sh — PostToolUse 훅 / matcher: Edit|Write (계획서 A.3)
# 목적: 텍스트로 편집된 에셋을 자동 정합화(reserialize)하고 재컴파일·콘솔 검증한다.
# 출력: 에러 있으면 stderr(+exit 2)로 Claude 에 피드백, 정상이면 무출력(exit 0).
HOOK_NAME=post-asset-edit
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_lib.sh
. "$DIR/_lib.sh"

input="$(cat)"
f="$(json_field "$input" "tool_input.file_path")"
[ -n "$f" ] || exit 0

# ② 확장자 분기
ext="${f##*.}"
case "$ext" in
  prefab|unity|asset|mat) kind="asset" ;;   # 정합화 대상
  cs)                     kind="script" ;;  # 컴파일만
  *) exit 0 ;;                               # 비-에셋 → 즉시 종료
esac

# unity-cli 부재 → 경고만, 비차단
if ! command -v unity-cli >/dev/null 2>&1; then
  echo "[post-asset-edit] unity-cli 미설치 — '$f' 자동 정합화/컴파일 생략(수동 확인 필요)." >&2
  log "skip (no unity-cli): $f"
  exit 0
fi

# ③ 에셋이면 reserialize (.cs 는 제외)
if [ "$kind" = "asset" ]; then
  if ! unity-cli reserialize "$f" >/dev/null 2>&1; then
    echo "[post-asset-edit] reserialize 경고: '$f' (계속 진행)." >&2
  fi
fi

# ④ refresh --compile (Unity busy 면 unity-cli 가 자체 대기)
unity-cli editor refresh --compile >/dev/null 2>&1

# ⑤ 콘솔 에러 수집
errout="$(unity-cli --json console --filter error 2>/dev/null)"

# 결과 판정 — 에러/예외 흔적이 있고 비어있지 않으면 피드백
if [ -n "$errout" ] \
   && printf '%s' "$errout" | grep -Eiq '"(message|type)"[[:space:]]*:|error|exception' \
   && ! printf '%s' "$errout" | grep -Eq '^\[\]\s*$|"(logs|errors|entries)"[[:space:]]*:[[:space:]]*\[\]'; then
  echo "[post-asset-edit] '$f' 편집 후 콘솔 에러 감지:" >&2
  printf '%s\n' "$errout" | cut -c1-1500 >&2
  log "errors after edit: $f"
  exit 2   # PostToolUse: exit 2 → stderr 가 Claude 에 전달됨
fi

log "ok: $f"
exit 0
