#!/usr/bin/env bash
# scaffold_testrun.sh — /test-run 스캐폴딩 (test-run 스킬이 호출)
#
# 사용:  bash scaffold_testrun.sh <cycle-id>
#        bash scaffold_testrun.sh 2026-06-15_inventory
#
# 동작:
#   1) cycles/<cycle>/test_run/ + evidence/ 생성
#   2) test_def.md · asset_map.md · result.md 템플릿 복사(+ <cycle-id> 치환)
#   3) Assets/_TestRuns/<cycle>/assets/ 안내(실제 폴더·에셋은 SKILL 이 unity-cli 로 생성 — .meta 정합)
#   4) 생성된 test_run 경로를 stdout 마지막 줄에 출력
# 주의: 재실행 시 기존 문서·에셋을 덮지 않는다(누적·교체 보존 — _conventions §17-D/F).
set -u

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATES="$DIR/../assets/templates/test_run"
ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$DIR/../../../.." && pwd)}"
HARNESS="$ROOT/.harness"

CYCLE="${1:-}"
if [ -z "$CYCLE" ]; then echo "[testrun] 사용: scaffold_testrun.sh <cycle-id>" >&2; exit 1; fi

CDIR="$HARNESS/cycles/$CYCLE"
if [ ! -d "$CDIR" ]; then echo "[testrun] 사이클 없음: $CDIR" >&2; exit 1; fi

TRDIR="$CDIR/test_run"
mkdir -p "$TRDIR/evidence"

# 템플릿 복사(+치환). 기존 파일 보존(재실행 시 사용자 편집·매핑 유지).
for t in test_def asset_map result; do
  if [ -f "$TEMPLATES/$t.md" ] && [ ! -f "$TRDIR/$t.md" ]; then
    sed "s|<cycle-id>|$CYCLE|g" "$TEMPLATES/$t.md" > "$TRDIR/$t.md"
    echo "[testrun] 생성: test_run/$t.md" >&2
  elif [ -f "$TRDIR/$t.md" ]; then
    echo "[testrun] 보존(기존): test_run/$t.md" >&2
  fi
done

# Assets 테스트 루트(폴더만 안내 — 실제 생성은 unity-cli AssetDatabase 로 SKILL 이 수행)
ASSETS_REL="Assets/_TestRuns/$CYCLE/assets"
echo "[testrun] Unity 테스트 에셋 루트: $ASSETS_REL (SKILL 이 unity-cli 로 폴더·에셋 생성)" >&2

# 마지막 줄: test_run 경로(호출자가 캡처)
printf '%s\n' "$TRDIR"
