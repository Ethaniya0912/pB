---
title: steam-cloud
tags: [steam]
status: decided
source:
  - Assets/Scripts/Networking/SteamClient.cs
  - Assets/Plugins/Facepunch/Facepunch.Steamworks.Win64.xml
verified: 2026-06-15
---

# steam-cloud

Steam Cloud(원격 저장소) 연동으로 세이브 파일을 계정 간 자동 동기화한다.

## 현황 (pB)

**미구현**.

`Assets/Scripts/` 전체에 `SteamRemoteStorage`, `SteamCloud`, `ISteamRemoteStorage` 호출이 존재하지 않는다(Grep 결과: 해당 심볼은 Facepunch XML 문서에만 존재, 게임 코드 없음).

현재 세이브/로드는 별도 파일 시스템(`WorldSaveGameManager.cs`) 로컬 경로에 의존한다. Steam Cloud 활성화를 위한 Steamworks 파트너 대시보드 설정(`steamcloud.vdf`, 할당량 설정)도 확인되지 않음.

Facepunch.Steamworks의 `SteamRemoteStorage` API(`FileRead`, `FileWrite`, `FileExists` 등)는 탑재된 DLL에 포함돼 있으므로 코드만 추가하면 사용 가능하다.

## 설계·결정

아직 결정되지 않음. 아래는 출시 전 검토가 필요한 선택지다.

**옵션 A — Facepunch `SteamRemoteStorage` 직접 사용**: `SteamRemoteStorage.FileWrite(filename, bytes)` / `FileRead` 로 로컬 저장과 동일 코드 경로를 덮어씌우는 방식. 구현 단순.

**옵션 B — Steam에 위임(Steam 자동 클라우드)**: Steamworks 파트너 대시보드에서 동기화할 파일 경로 패턴만 설정하면 Steam이 자동 동기화. 코드 변경 없음. 단, 충돌 해소 로직이 Steam 기본 UI(덮어쓰기 경고 다이얼로그)에 의존하게 됨.

권고: 빠른 적용이 필요하다면 옵션 B(자동 클라우드)로 시작하고, 충돌 해소 커스터마이즈가 필요해지면 옵션 A로 전환.

## ⚠ 비판·리스크

**[높음] EA·출시 전 미구현**: Steam 게임에서 클라우드 세이브 미지원은 사용자 리뷰에서 지적받는 기본 결함이다. 출시 전 최소 Steamworks 파트너 대시보드에 자동 클라우드 설정이라도 필요.

**[중간] 로컬-클라우드 충돌 해소 로직 없음**: 자동 클라우드로 가더라도 여러 PC에서 동시 플레이 시 충돌이 발생할 수 있다. 현재 어떤 충돌 정책도 정의되지 않음.

**[중간] 세이브 파일 경로 미확인**: `WorldSaveGameManager.cs` 의 실제 저장 경로가 Steam Cloud 호환 경로(`%appdata%`, `StreamingAssets` 등)인지 확인되지 않음. 자동 클라우드 설정 시 경로 매핑 오류 가능성 존재.

**[낮음] 할당량 계획 없음**: Steam Cloud 기본 할당량은 계정당 100MB. 저장 파일 크기 추정(지형 시드·플레이어 데이터)이 없으면 할당 요청 시점 불명.

## 관련 문서

- [[save-load|세이브-로드]]
- [[steamworks-integration|steamworks-통합]]
- [[04-steam-hub|04 · Steam 통합]]

---
← [[04-steam-hub|04 · Steam 통합 (Steamworks)]] · [[index|인덱스]]
