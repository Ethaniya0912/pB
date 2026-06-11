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

# ① 경로 정규화 — 백슬래시→슬래시, Assets/Packages 기준 프로젝트 상대경로로 변환.
#    Assets/Packages 외부 파일(.harness 템플릿·docs 등)은 Unity 와 무관 → 즉시 통과.
norm="$(printf '%s' "$f" | tr '\\' '/')"
case "$norm" in
  Assets/*|Packages/*) rel="$norm" ;;
  */Assets/*)          rel="Assets/${norm#*/Assets/}" ;;
  */Packages/*)        rel="Packages/${norm#*/Packages/}" ;;
  *) exit 0 ;;
esac

# ② 확장자 분기 — Unity YAML 에셋은 정합화 대상, 코드·셰이더 계열은 컴파일만.
ext="${rel##*.}"
case "$ext" in
  prefab|unity|asset|mat|anim|controller|overrideController|physicMaterial|mask|playable|spriteatlas|terrainlayer|guiskin)
    kind="asset" ;;
  cs|asmdef|asmref|shader|cginc|hlsl|compute|uss|uxml)
    kind="script" ;;
  *) exit 0 ;;
esac

# unity-cli 부재 → 경고만, 비차단
if ! command -v unity-cli >/dev/null 2>&1; then
  echo "[post-asset-edit] unity-cli 미설치 — '$rel' 자동 정합화/컴파일 생략(수동 확인 필요)." >&2
  log "skip (no unity-cli): $rel"
  exit 0
fi

# ③ 에셋이면 reserialize (코드 계열은 제외) — ucli: 타임아웃 래퍼(_lib.sh)
if [ "$kind" = "asset" ]; then
  if ! ucli reserialize "$rel" >/dev/null 2>&1; then
    echo "[post-asset-edit] reserialize 경고: '$rel' (계속 진행)." >&2
  fi
fi

# ④ refresh --compile (Unity busy 면 unity-cli 가 자체 대기, 대형 컴파일 대비 타임아웃 연장)
HARNESS_UCLI_TIMEOUT=120 ucli editor refresh --compile >/dev/null 2>&1

# ⑤ 콘솔 에러 수집
errout="$(ucli --json console --filter error 2>/dev/null)"

# 결과 판정 — 에러/예외 흔적이 있고 비어있지 않으면 피드백
if [ -n "$errout" ] \
   && printf '%s' "$errout" | grep -Eiq '"(message|type)"[[:space:]]*:|error|exception' \
   && ! printf '%s' "$errout" | grep -Eq '^\[\]\s*$|"(logs|errors|entries)"[[:space:]]*:[[:space:]]*\[\]'; then
  echo "[post-asset-edit] '$rel' 편집 후 콘솔 에러 감지:" >&2
  printf '%s\n' "$errout" | cut -c1-1500 >&2
  log "errors after edit: $rel"
  exit 2   # PostToolUse: exit 2 → stderr 가 Claude 에 전달됨
fi

log "ok: $rel"
exit 0
