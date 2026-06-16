---
title: 빌드-자동화
tags: [tooling]
status: done
source:
  - TDA.PB4.Tests.PlayMode.csproj
  - Assets/Scripts/pB-4/week2_Tooling/
verified: 2026-06-15
---

# 빌드-자동화

pB Unity 프로젝트의 빌드는 현재 에디터 수동 빌드 방식이다. 전용 빌드 스크립트나 파이프라인 코드는 존재하지 않는다.

## 현황 (pB)

- `.github/` 디렉토리 없음 — GitHub Actions 또는 외부 CI 파이프라인 미도입 확인(2026-06-15 실측).
- `BuildPipeline` 호출 스크립트: `Assets/` 내 전용 빌드 자동화 스크립트 없음. Grep 결과 `BuildPlayer` 호출 코드 미발견.
- 빌드 타겟: `TDA.PB4.Tests.PlayMode.csproj` 에서 `UnityBuildTarget=StandaloneWindows64:19`, Unity 6000.3.1f1 확인.
- 클라이언트 단독 타겟(Windows 64-bit). 전용 서버 빌드 타겟 없음.
- 버전·매니페스트 자동 부여 절차 미구성.
- `steam_appid.txt`에 AppID 480(테스트용 Spacewar)이 기재되어 있어 Steam 초기화는 가능한 상태.

## 설계·결정

- 현 단계(EA 전)에서 에디터 수동 빌드를 유지. 빌드 빈도가 낮고 인원이 소수라 자동화 필요성을 뒤로 미룬 것으로 보인다.
- 수동 빌드 절차: Unity Editor > File > Build Settings > Build (StandaloneWindows64).
- Steam 업로드: Steamworks SDK의 SteamPipe CLI(steamcmd + app_build VDF)로 수동 수행.

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | CI/CD 완전 부재 | `.github/` 없음, BuildPipeline 스크립트 없음 — 인간 오류·빌드 누락 위험 | EA 전까지 GitHub Actions + game-ci 도입. `BuildPlayer` Editor 스크립트 작성 |
| 높음 | 빌드 번호·매니페스트 미자동화 | 수동 버전 관리 → 릴리즈 혼선 가능성 | `ProjectSettings/ProjectVersion.txt` 자동 갱신 스크립트 |
| 중간 | Steam 업로드 수동화 | SteamPipe 수동 실행 → 누락·오버라이트 위험 | GitHub Actions에서 steamcmd 자동화(시크릿 관리 필요) |
| 낮음 | 전용 서버 빌드 타겟 없음 | 현재 P2P 호스트 방식이라 미필요, 그러나 향후 dedicated 전환 시 추가 작업 | ADR 작성 후 결정 |

## 관련 문서

- [[ci-cd|ci-cd]]
- [[server-hosting|서버-호스팅]]

---
← [[07-build-ci-hub|07 · 빌드 & CI/CD]] · [[index|인덱스]]
