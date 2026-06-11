#!/usr/bin/env bash
# scaffold_cycle.sh — 사이클 폴더 스캐폴딩 + as-is 스냅샷 (cycle-start 가 호출)
#
# 사용:
#   bash scaffold_cycle.sh <input-doc> [slug]
#   bash scaffold_cycle.sh docs/기획_인벤토리.md inventory
#
# 동작:
#   1) .harness/cycles/<YYYY-MM-DD_slug>/ 생성 (00_input/, 05_spec/ 포함)
#   2) 템플릿 01~08 · meta.json · decisions.md 복사
#   3) 입력 문서를 00_input/ 에 복사
#   4) unity-cli 가 있으면 as-is 스냅샷을 snapshots/ 에 덤프
#   5) _index.md 에 시작 행 append
#   6) 생성된 사이클 경로를 stdout 마지막 줄에 출력
set -u

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATES="$DIR/../assets/templates"
ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$DIR/../../../.." && pwd)}"
HARNESS="$ROOT/.harness"

INPUT="${1:-}"
SLUG="${2:-}"

# slug 도출(인자 없으면 입력 파일명에서)
if [ -z "$SLUG" ]; then
  if [ -n "$INPUT" ]; then
    base="$(basename "$INPUT")"; base="${base%.*}"
    SLUG="$(printf '%s' "$base" | tr '[:upper:] ' '[:lower:]-' | tr -cd 'a-z0-9가-힣ㄱ-ㅎㅏ-ㅣ-' )"
  fi
  [ -n "$SLUG" ] || SLUG="cycle"
fi

DATE="$(date +%F 2>/dev/null || echo 0000-00-00)"
TS="$(date '+%Y-%m-%d %H:%M' 2>/dev/null || echo "$DATE 00:00")"
ID="${DATE}_${SLUG}"
CDIR="$HARNESS/cycles/$ID"

if [ -e "$CDIR" ]; then
  echo "[scaffold] 이미 존재: $CDIR (그대로 사용)" >&2
else
  mkdir -p "$CDIR/00_input" "$CDIR/05_spec" "$HARNESS/snapshots" "$HARNESS/cycles"
  # 템플릿 복사 (05_spec_asset.md 는 예시이므로 05_spec/ 안내용으로만 둠)
  for t in 01_target 02_goal 03_scope 04_assets 06_test_env 07_plan 08_result; do
    [ -f "$TEMPLATES/$t.md" ] && cp "$TEMPLATES/$t.md" "$CDIR/$t.md"
  done
  [ -f "$TEMPLATES/05_spec_asset.md" ] && cp "$TEMPLATES/05_spec_asset.md" "$CDIR/05_spec/_template.md"
  [ -f "$TEMPLATES/decisions.md" ] && cp "$TEMPLATES/decisions.md" "$CDIR/decisions.md"

  # meta.json 채우기(치환)
  if [ -f "$TEMPLATES/meta.json" ]; then
    sed -e "s|<YYYY-MM-DD_slug>|$ID|g" \
        -e "s|<사이클 제목>|$SLUG|g" \
        -e "s|<YYYY-MM-DD HH:MM>|$TS|g" \
        "$TEMPLATES/meta.json" > "$CDIR/meta.json"
  fi
  echo "[scaffold] 생성: $CDIR" >&2
fi

# 입력 문서 복사
if [ -n "$INPUT" ] && [ -f "$INPUT" ]; then
  cp "$INPUT" "$CDIR/00_input/" && echo "[scaffold] 입력 복사: $(basename "$INPUT")" >&2
elif [ -n "$INPUT" ]; then
  echo "[scaffold] 경고: 입력 문서를 찾을 수 없음: $INPUT" >&2
fi

# as-is 스냅샷
SNAP="$HARNESS/snapshots/${DATE}_${SLUG}_before.json"
if command -v unity-cli >/dev/null 2>&1; then
  if unity-cli --json exec "return UnityEditor.AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith(\"Assets/\")).ToArray()" --usings System.Linq > "$SNAP" 2>/dev/null && [ -s "$SNAP" ]; then
    echo "[scaffold] 스냅샷: ${SNAP#$ROOT/}" >&2
  else
    echo "[scaffold] 스냅샷 실패(Editor offline?) — 건너뜀" >&2
    rm -f "$SNAP" 2>/dev/null
  fi
else
  echo "[scaffold] unity-cli 미설치 — 스냅샷 생략" >&2
fi

# _index.md append
IDX="$HARNESS/_index.md"
[ -f "$IDX" ] || printf '# 하네스 사이클 레지스트리 (`_index.md`)\n\n| 일시 | 사이클 | 상태 | 비고 |\n|---|---|---|---|\n' > "$IDX"
if ! grep -q "$ID" "$IDX" 2>/dev/null; then
  printf '| %s | %s | started | %s |\n' "$TS" "$ID" "$SLUG" >> "$IDX"
  echo "[scaffold] _index.md 등록: $ID" >&2
fi

# 마지막 줄: 사이클 경로(호출자가 캡처)
printf '%s\n' "$CDIR"
