---
title: steamworks-행정
tags: [steam]
status: done
source:
  - steam_appid.txt
  - Assets/Scripts/Networking/SteamClient.cs
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Assets/Plugins/Facepunch/
verified: 2026-06-15
---

# steamworks-행정

Steamworks 파트너 행정은 대부분 출시(EA) 단계에서 필요한 항목이다. 현재는 테스트 AppID 480(Spacewar)으로 개발 중이다.

## 현황 (pB)

**Steam SDK 통합 상태**

- SDK: Facepunch.Steamworks 라이브러리(`Assets/Plugins/Facepunch/`) 적용 완료.
- AppID: `steam_appid.txt` = **480** (Spacewar 공개 테스트 AppID). 실 게임용 AppID 미등록.
- 초기화: `Assets/Scripts/Networking/SteamClient.cs` — `Steamworks.SteamClient.Init(steamAppId)` 호출. AppID 필드 = 480.
- 로비: `Assets/Scripts/Networking/SteamLobbyManager.cs` — Steam 로비 생성/가입 처리.
- Steam 클라우드·실적(Achievement)·리더보드: 코드 없음. 미구현.
- SteamPipe 빌드 업로드: 수동(자동화 없음). app_build VDF 미발견.

**파트너 계정·앱 등록 현황**

- Steamworks 파트너 계정 보유 여부: 코드로 확인 불가.
- 실 AppID 신청 여부: 미확인. `steam_appid.txt` = 480이 유일한 근거 — 실 AppID 없음으로 추정.

## 설계·결정

- 개발 단계에서 AppID 480을 사용해 Steam P2P, 로비 기능을 검증하는 표준 관행을 따름.
- EA 출시 전에 Steamworks 파트너 포털에서 신규 앱 ID를 발급받고 `steam_appid.txt` 및 `SteamClient.cs`의 `steamAppId` 필드를 교체해야 함.

## EA 출시 전 체크리스트

### 계정·앱 행정
- [ ] Steamworks 파트너 계정 보유 확인 (https://partner.steamgames.com)
- [ ] 신규 앱 ID 신청 (기본 수수료 $100)
- [ ] `steam_appid.txt` 실 AppID로 교체
- [ ] `SteamClient.cs` L15 `steamAppId = 480` → 실 AppID 수정

### 스토어 페이지
- [ ] 게임 설명·스크린샷·트레일러 등록
- [ ] 연령 등급 설정 (IARC 자가 평가 또는 ESRB/PEGI 별도 신청)
- [ ] 지원 언어·OS·최소/권장 사양 기재
- [ ] P2P 호스트 방식 명시 (호스트 이탈 시 세션 종료 고지 권장)

### 빌드 배포 (SteamPipe)
- [ ] Steamworks SDK 다운로드·설치 (별도 SDK, Facepunch 라이브러리와 다름)
- [ ] app_build_480.vdf → 실 AppID VDF 작성
- [ ] 빌드 디포(Depot) 구성 (Windows 64-bit 단일 디포)
- [ ] 스테이징 브랜치(default 외 beta/ea 브랜치) 설정
- [ ] `steamcmd +login <계정> +run_app_build <VDF경로>` 첫 업로드 검증

### 실적·기능
- [ ] Steam Cloud 저장 여부 결정 (현재 로컬 저장)
- [ ] 실적(Achievement) 등록 여부 결정 (현재 미구현)
- [ ] VAC 활성화 여부 결정 (anti-cheat.md 참조)
- [ ] 친구 초대·로비 연결(Steam 로비 리스트) 동작 확인

### 릴리즈
- [ ] 리뷰어 키(Press/Reviewer) 발급 계획
- [ ] 출시일·가격 설정
- [ ] Steam 검토(Review) 제출 → 승인(수일 소요)

## ⚠ 비판·리스크

| 심각도 | 항목 | 근거 | 권고 |
|---|---|---|---|
| 높음 | 실 AppID 미등록 | `steam_appid.txt` = 480 — Steam 릴리즈 불가 상태 | Steamworks 파트너 포털에서 즉시 신청 ($100) |
| 높음 | SteamPipe 업로드 자동화 없음 | 수동 업로드 → EA 릴리즈 빈도 증가 시 병목 | GitHub Actions + steamcmd 자동화 (시크릿 관리 필요) |
| 중간 | Steam Cloud 미구현 | 로컬 저장만 — PC 교체·재설치 시 진행도 소실 | `PlayerPrefs` 또는 파일 경로를 Steam Cloud에 연결 |
| 중간 | 실적 미구현 | Achievement 없음 — Steam 유저 참여 수단 부재 | EA 출시 후 1차 패치에서 기본 실적 추가 |
| 낮음 | app_build VDF 미작성 | 업로드 절차 문서화 없음 | 첫 업로드 전 VDF 템플릿 작성·저장 |

## 관련 문서

- [[server-hosting|서버-호스팅]]
- [[build-automation|빌드-자동화]]
- [[ci-cd|ci-cd]]

---
← [[11-ops-biz-hub|11 · 운영 & Steamworks 행정]] · [[index|인덱스]]
