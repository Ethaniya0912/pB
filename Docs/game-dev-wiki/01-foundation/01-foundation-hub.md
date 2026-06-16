---
title: 01-foundation-hub
tags: [moc, tooling]
status: done
source:
  - .editorconfig
  - .gitattributes
  - .gitignore
  - Assets/**/*.asmdef
  - .harness/_conventions.md
verified: 2026-06-15
---

# 01 · 기반 (협업/구조/컨벤션)

디자인과 무관하게 가장 먼저 깔아야 하는 협업·구조·컨벤션 토대. **pB는 일부 항목이 구현됐고 일부는 부채로 남아 있다.**

## 현황 (pB)

| 기반 항목 | 상태 | 핵심 이슈 |
|---|---|---|
| 버전관리·LFS | 부분 완료 | LFS 바이너리 패턴 미설정 |
| 프로젝트 구조 | 완료(부분) | 폴더 트리 존재, 공식 표준안 미문서화 |
| 코딩 컨벤션 | 부분 완료 | _conventions.md 존재, .editorconfig 최소화 |
| Assembly Definition | 미흡 | 게임 코드 전체 단일 어셈블리 |

## 문서

- [[version-control-git-lfs|버전관리-git-lfs]]
- [[project-structure|프로젝트-구조]]
- [[coding-conventions|코딩-컨벤션]]
- [[assembly-definition|assembly-definition]]

## ⚠ 비판·리스크

- **심각도 높음**: Assembly-CSharp 단일 어셈블리로 게임 코드가 몰려 있어 컴파일 전략이 없다. 서드파티 6개만 별도 asmdef로 분리됐다.
- **심각도 높음**: LFS 미설정 — 기반 설정 중 가장 시급한 항목.
- **심각도 보통**: `.editorconfig`는 `indent_style=space`와 charset/eol 설정뿐으로, 네이밍/린트 규칙이 포함되지 않는다. `_conventions.md`의 규칙이 도구로 강제되지 않는다.

---
← [[index|인덱스]]
