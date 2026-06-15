#!/usr/bin/env bash
# check-links.sh — .harness/Reports md 의 Foam 위키링크·상대경로 링크 정합 검사
# 사용: bash .harness/hooks/check-links.sh        # 깨진 링크 있으면 목록 출력 + exit 1
# 범위: .harness/**, Reports/** (archive/, 00_input/ 제외 — 불변 보관물)
# 규약: _conventions.md §7(위키링크)·§9(상대경로). 플레이스홀더(`…`,`<`,`Foo.cs`,`xxx`)는 건너뜀.
set -u
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
SCOPE1="$ROOT/.harness"; SCOPE2="$ROOT/Reports"
broken=0; checked=0

is_placeholder() {
  case "$1" in
    *…*|*\<*|*Foo.cs*|*xxx*|*0N_name*|*용어1*|*관련용어*|*에셋명*) return 0;;
    *...*|*..png*|용어|파일|관련문서) return 0;;   # 템플릿·규약 예시 표기
  esac
  return 1
}

resolve_wiki() { # $1 = 타깃(alias/anchor 제거됨) → 존재하면 0
  local t="$1" hit
  case "$t" in
    */*) hit="$(find "$SCOPE1" "$SCOPE2" -path "*${t}.md" -print -quit 2>/dev/null)";;
    *)   hit="$(find "$SCOPE1" "$SCOPE2" -name "${t}.md" -print -quit 2>/dev/null)";;
  esac
  [ -n "$hit" ]
}

while IFS= read -r f; do
  d="$(dirname "$f")"
  # 1) 위키링크 [[target|alias]] / [[target#anchor]]
  while IFS= read -r w; do
    t="${w#\[\[}"; t="${t%\]\]}"; t="${t%%|*}"; t="${t%%#*}"
    t="$(printf '%s' "$t" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
    [ -z "$t" ] && continue
    is_placeholder "$t" && continue
    checked=$((checked+1))
    resolve_wiki "$t" || { echo "BROKEN wiki  ${f#$ROOT/}: [[$t]]"; broken=$((broken+1)); }
  done < <(grep -oE '\[\[[^]]+\]\]' "$f" 2>/dev/null || true)
  # 2) 상대경로 링크 ](path) — http/절대경로/anchor-only 제외.
  #    (?<!\]) 룩비하인드: 위키링크 직후 괄호 문구 `]](…)` 를 md 링크로 오인하지 않게 한다.
  while IFS= read -r l; do
    p="${l#\](}"; p="${p%)}"; p="${p%%#*}"
    case "$p" in http*|mailto:*|/*|[A-Za-z]:*|"") continue;; esac
    is_placeholder "$p" && continue
    checked=$((checked+1))
    [ -e "$d/$p" ] || { echo "BROKEN path  ${f#$ROOT/}: ($p)"; broken=$((broken+1)); }
  done < <(grep -oP '(?<!\])\]\([^)]+\)' "$f" 2>/dev/null || true)
done < <(find "$SCOPE1" "$SCOPE2" -name '*.md' -not -path '*/archive/*' -not -path '*/00_input/*' 2>/dev/null)

echo "[check-links] checked=$checked broken=$broken"
[ "$broken" -eq 0 ]
