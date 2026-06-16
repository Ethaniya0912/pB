# 🎮 Unity 6.3 멀티플레이 (Steam) — 개발 위키

[Foam](https://foambubble.github.io/) 기반 프로젝트 지식 베이스입니다.

> **폴더/파일 이름은 영문(ASCII)** 입니다. Windows 기본 압축 해제 시 한글 이름이 깨지는 문제를 피하기 위함입니다.
> 한글 제목은 각 문서 frontmatter `title` 와 위키링크 별칭 `[[slug|한글제목]]` 으로 표시되어,
> Foam 그래프/링크 UI 에는 한글 제목이 그대로 보입니다.

## 시작하기
1. VS Code에서 이 폴더를 엽니다.
2. 권장 확장(Foam)을 설치합니다 (`.vscode/extensions.json`).
3. [[index|인덱스]] 에서 탐색하거나 `Foam: Show Graph` 로 그래프를 봅니다.

## 규칙
- 모든 문서는 frontmatter(`title`, `tags`, `status`)를 가집니다.
- 문서 연결은 위키링크 `[[slug|한글제목]]` 으로 합니다.
- 의사결정은 [[adr-template|ADR 템플릿]] 형식으로 남깁니다.
- `status`: `todo` / `researching` / `decided` / `done`

## pB 실체 반영 + 유지보수 (2026-06-15)
- 각 문서는 **`## 현황 (pB)`**(실제 구현·코드 경로) + **`## ⚠ 비판·리스크`**(약점·부채·미검증 가정)를 담습니다.
- frontmatter 에 **`source`**(근거 코드 경로)·**`verified`**(검증일)를 두어 신선도를 추적합니다 — 형식·유지보수 상세는 [[_wiki-conventions|위키 규약]].
- **비판 종합(출시 블로커)** 은 [[index|인덱스]] 상단을 보세요.
- 별도 하네스를 만들지 않고, `unity-pipeline` 사이클이 영향 준 문서를 갱신하고 [[_wiki-conventions|규약]]의 stale 점검으로 신선도를 유지합니다.
