#!/usr/bin/env bash
# check-mermaid.sh — .harness md 의 ```mermaid 블록 정적 검사(외부 의존 0)
# 검사: ① 펜스 짝 ② subgraph/end 균형 ③ :::class 참조가 classDef 정의에 있는지 ④ 다이어그램 헤더 유효
# 사용: bash .harness/hooks/check-mermaid.sh   # 문제 있으면 목록 + exit 1
set -u
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"
bad=0; blocks=0

while IFS= read -r f; do
  awk -v FN="${f#$ROOT/}" '
    BEGIN{ inb=0; nblk=0 }
    /^```mermaid[ \t]*$/ { inb=1; nblk++; sg=0; en=0; hdr=""; delete defs; delete refs; ln=0; next }
    inb && /^```[ \t]*$/ {
      # 블록 종료 — 검사
      if (hdr !~ /^(flowchart|graph|mindmap|classDiagram|stateDiagram(-v2)?|sequenceDiagram|erDiagram|gantt|journey|pie)/)
        { print "FAIL " FN " blk" nblk ": 알 수 없는 다이어그램 헤더 [" hdr "]"; bad++ }
      if (sg != en) { print "FAIL " FN " blk" nblk ": subgraph(" sg ") != end(" en ")"; bad++ }
      for (r in refs) if (!(r in defs)) { print "FAIL " FN " blk" nblk ": 미정의 class :::" r; bad++ }
      inb=0; next
    }
    inb {
      ln++
      line=$0; gsub(/^[ \t]+/,"",line)
      if (ln==1 && hdr=="") hdr=line
      if (line ~ /^subgraph/) sg++
      if (line ~ /^end$/) en++
      if (line ~ /^classDef[ \t]/) { split(line,a,/[ \t]+/); defs[a[2]]=1 }
      # :::NAME 참조 수집
      tmp=$0
      while (match(tmp, /:::[A-Za-z_][A-Za-z0-9_]*/)) {
        ref=substr(tmp, RSTART+3, RLENGTH-3); refs[ref]=1
        tmp=substr(tmp, RSTART+RLENGTH)
      }
    }
    END{ if(inb) { print "FAIL " FN ": mermaid 블록 펜스 안 닫힘"; bad++ }
         print "__BLK__" nblk; print "__BAD__" bad }
  ' "$f"
done < <(grep -rl '```mermaid' "$ROOT/.harness" --include='*.md' 2>/dev/null) > /tmp/_mmcheck.out 2>&1

# 집계
blocks=$(awk -F'__BLK__' '/__BLK__/{s+=$2} END{print s+0}' /tmp/_mmcheck.out)
bad=$(awk -F'__BAD__' '/__BAD__/{s+=$2} END{print s+0}' /tmp/_mmcheck.out)
grep -v '^__' /tmp/_mmcheck.out
echo "[check-mermaid] blocks=$blocks bad=$bad"
rm -f /tmp/_mmcheck.out
[ "$bad" -eq 0 ]
