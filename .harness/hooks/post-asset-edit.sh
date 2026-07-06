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

# ②.5 [.cs 전용] 파일명=클래스명 정적 린트 — unity-cli 불요(순수 텍스트), 컴파일보다 먼저.
#     MonoBehaviour/ScriptableObject/NetworkBehaviour 파생 클래스가 MonoScript(m_Script)
#     바인딩을 못 받으면, 컴파일은 통과하나 도메인 리로드 시 missing-script 로 파괴된다
#     (2026-06-12 NetSimController 회귀). 컴파일이 못 잡는 부류라 작성 시점에 차단한다.
#     판정은 Unity 실동작 기준(_lib: unity_filename_risk) — 단일 이름불일치는 폴백 바인딩되어
#     안전하므로 과탐하지 않는다.
if [ "$ext" = "cs" ]; then
  src=""
  [ -f "$f" ] && src="$f"
  [ -z "$src" ] && [ -f "$HARNESS_PROJECT_DIR/$rel" ] && src="$HARNESS_PROJECT_DIR/$rel"
  if [ -n "$src" ]; then
    risk="$(unity_filename_risk "$src" "$(basename "$rel" .cs)")"
    if [ -n "$risk" ]; then
      tag="${risk%%	*}"; names="${risk#*	}"
      if [ "$tag" = "MULTI" ]; then
        echo "[post-asset-edit] '$rel' 한 파일에 MonoBehaviour 다수 — MonoScript 는 최대 1개만 바인딩, 나머지는 도메인 리로드 시 missing-script:" >&2
      else
        echo "[post-asset-edit] '$rel' 파일명과 동명의 타입이 MonoScript 를 가로채 MonoBehaviour 가 고아가 됨(도메인 리로드 시 missing-script):" >&2
      fi
      printf '%s\n' "$names" | tr ',' '\n' | while IFS= read -r c; do
        [ -n "$c" ] && echo "  · $c → ${c}.cs 로 분리(파일명=클래스명)." >&2
      done
      log "filename-binding risk ($tag): $rel [$names]"
      exit 2
    fi
  fi
fi

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

# ⑤ missing-script 경고 스캔 — "The referenced script ... is missing!" 류는 error 가 아니라
#    warning/log 으로 떠서 아래 ⑥ error 필터를 그대로 통과한다(2026-06-12 회귀가 정확히 이 사각지대로
#    빠졌다). 컴파일은 멀쩡하나 도메인 리로드 시 컴포넌트가 파괴되는 부류(파일명≠클래스명·깨진
#    프리팹 스크립트 참조)를 error 보다 먼저, 구체 메시지로 잡는다.
warnout="$(ucli console --type warning,log 2>/dev/null)"
if printf '%s' "$warnout" | grep -Eiq 'referenced script.*missing|missing script|associated script.*(missing|cannot be loaded)'; then
  echo "[post-asset-edit] '$rel' 편집 후 missing-script 경고 감지(컴파일 통과 — 도메인 리로드 시 컴포넌트 파괴 위험):" >&2
  printf '%s\n' "$warnout" | grep -Ei 'referenced script|missing script|associated script' | head -n 5 | cut -c1-800 >&2
  log "missing-script warning after edit: $rel"
  exit 2
fi

# ⑥ 콘솔 에러 수집 (v0.3.x: --type error 로 에러 항목만 필터)
errout="$(ucli console --type error 2>/dev/null)"

# 결과 판정 — error 타입만 받았으므로 에러/예외 흔적이 있으면 피드백
if [ -n "$errout" ] \
   && printf '%s' "$errout" | grep -Eiq 'error|exception|assert'; then
  echo "[post-asset-edit] '$rel' 편집 후 콘솔 에러 감지:" >&2
  printf '%s\n' "$errout" | cut -c1-1500 >&2
  log "errors after edit: $rel"
  exit 2   # PostToolUse: exit 2 → stderr 가 Claude 에 전달됨
fi

log "ok: $rel"
exit 0
