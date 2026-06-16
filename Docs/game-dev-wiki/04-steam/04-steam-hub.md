---
title: 04-steam-hub
tags: [moc, steam]
status: done
source:
  - Assets/Scripts/Networking/SteamClient.cs
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Assets/Plugins/Facepunch/
  - steam_appid.txt
verified: 2026-06-15
---

# 04 · Steam 통합 (Steamworks)

Facepunch.Steamworks DLL 기반 Steam 통합 현황. 초기화·로비·P2P 트랜스포트는 구현 완료; Cloud·도전과제·빌드 파이프라인은 미구현.

## 현황 요약

| 항목 | 상태 | 핵심 파일 |
|---|---|---|
| 래퍼 선정 | 완료 (Facepunch.Steamworks) | `Assets/Plugins/Facepunch/` |
| SteamClient init/shutdown | 완료 (P1-1 단독 수명 소유) | `SteamClient.cs` |
| P2P 트랜스포트 (SDR Relay) | 완료 | `SteamP2PRelayTransport.cs` |
| 로비 생성·참가 | 완료 | `SteamLobbyManager.cs` |
| 친구 초대 버튼 | UI만 존재, 로직 미확인 | `LobbyUIManager.cs` |
| ConnectionApproval | 미구현 (Step 3 P2-6) | — |
| 재접속 재합류 | 미구현 (Step 3 P2-6) | — |
| Steam Cloud | 미구현 | — |
| Achievements / Stats | 미구현 | — |
| SteamPipe 빌드 업로드 | 미구현 | — |
| AppID | 480 (개발용 Spacewar) | `steam_appid.txt` |

## 문서

- [[steamworks-integration|steamworks-통합]] — 래퍼 선정·초기화·트랜스포트·DLL 위치
- [[lobby-matchmaking|로비-매치메이킹]] — 로비 생성·참가·난입·연결 해제 흐름
- [[steam-cloud|steam-cloud]] — 미구현 · 출시 전 권고
- [[achievements-stats|도전과제-통계]] — 미구현 · 출시 전 권고
- [[steam-build-pipeline|steam-빌드-파이프라인]] — 미구현 · EA 전 블로커

## ⚠ 최우선 리스크 (출시 전)

1. **AppID 480 교체** — Spacewar 공유 AppID는 EA/출시 전 정식 AppID로 교체 필수.
2. **SteamPipe 파이프라인** — Steamworks 파트너 계정 + VDF + steamcmd 없이 Steam 출시 불가.
3. **ConnectionApproval** — 정원 초과·버전 불일치 거절 미구현 (Step 3 P2-6).

---
← [[index|인덱스]]
