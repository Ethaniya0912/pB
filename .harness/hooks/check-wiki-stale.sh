#!/usr/bin/env bash
# check-wiki-stale.sh — game-dev-wiki 문서 신선도 점검 (별도 하네스 대신 경량 유지보수)
# 원리: 각 위키 md frontmatter 의 source(근거 코드 경로)가 verified(검증일) 이후 변경됐으면 "갱신 필요".
# 변경 판정은 git 마지막 커밋일(%cs) 우선, 없으면 건너뜀(파일시스템 mtime 은 checkout 마다 바뀌어 부정확).
# 사용:  bash .harness/hooks/check-wiki-stale.sh   # stale 목록 출력 + 있으면 exit 1
set -u
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
WIKI="$ROOT/Docs/game-dev-wiki"
[ -d "$WIKI" ] || { echo "[wiki-stale] 위키 폴더 없음: $WIKI"; exit 0; }

stale=0; checked=0

while IFS= read -r f; do
  # frontmatter(첫 --- ~ 다음 ---)만 추출
  fm="$(awk 'NR==1&&/^---/{i=1;next} i&&/^---/{exit} i{print}' "$f")"
  verified="$(printf '%s' "$fm" | sed -n 's/^verified:[[:space:]]*//p' | head -1 | tr -d '"')"
  [ -n "$verified" ] || continue
  # source: [a, b] 또는 멀티라인 "- a" 모두에서 경로 후보 추출(Assets/.harness/Reports/Packages 로 시작)
  srcline="$(printf '%s' "$fm" | sed -n '/^source:/,/^[a-zA-Z]/p')"
  srcs="$(printf '%s\n%s' "$fm" "$srcline" | grep -oE '(Assets|\.harness|Reports|Packages|ProjectSettings)[^",'"'"' ]*' | sort -u)"
  [ -n "$srcs" ] || continue
  while IFS= read -r src; do
    [ -n "$src" ] || continue
    [ -e "$ROOT/$src" ] || { echo "MISSING-SRC  ${f#$ROOT/}: $src (경로 없음 — 위키 갱신 필요)"; stale=$((stale+1)); continue; }
    checked=$((checked+1))
    last="$(git -C "$ROOT" log -1 --format=%cs -- "$src" 2>/dev/null)"
    [ -n "$last" ] || continue   # git 미추적은 판정 불가 → 건너뜀
    # 문자열 날짜 비교(YYYY-MM-DD 사전식)
    if [ "$last" \> "$verified" ]; then
      echo "STALE  ${f#$ROOT/}: source '$src' 변경 $last > verified $verified"
      stale=$((stale+1))
    fi
  done <<< "$srcs"
done < <(find "$WIKI" -name '*.md' -not -path '*/.foam/*' 2>/dev/null)

echo "[check-wiki-stale] source 점검 $checked건 · stale/missing $stale건"
[ "$stale" -eq 0 ]
