---
title: 00-overview-hub
tags: [moc]
status: done
source:
  - CLAUDE.md
  - ProjectSettings/ProjectVersion.txt
  - Assets/Scripts/Networking/SteamLobbyManager.cs
verified: 2026-06-15
---

# 00 · 개요 & 우선순위

가칭 **pC**(코드 폴더명 pB) — **2-5인 PvE 코옵 다크/로우판타지 생존·던전 채굴 게임**(프로시저럴 동굴·숄더뷰·리얼리즘) 개요와 의사결정 우선순위, 마스터 체크리스트로 가는 진입점.

## 현황 (pB)

Unity 6000.3.1f1(6.3 LTS) PC/Steam 게임. **정식 게임명 미정(가칭 pC)** — 코드 식별자는 `Assets/Scripts/Networking/SteamLobbyManager.cs` 의 `GameIdValue = "PennutButterProject"`(화면 표기 "PennutButter 3"은 잠정). NGO(Netcode for GameObjects 2.7.0) + Facepunch.Steamworks 기반 Steam P2P 멀티플레이. **동접은 기획 2-5명**(몹 10마리 내외)이나 코드 `SteamLobbyManager.maxPlayers = 4` 로 불일치 — 5인 상향 필요. 기획 상세: [[project-overview|프로젝트-개요]].

핵심 패키지:
- `com.unity.netcode.gameobjects` 2.7.0
- `com.unity.render-pipelines.universal` 17.3.0 (URP)
- `com.unity.behavior` 1.0.15 (BT 기반 AI)
- `com.unity.multiplayer.tools` 2.2.3
- Facepunch.Steamworks(Assets 내 포함)

## 설계·결정

- Unity 6.3 LTS 선택: 장기 지원 + NGO 2.x 호환 + URP 17.x의 SSGI 실험적 지원
- Steam P2P(relay) 방식: 전용 서버 비용 없이 2-5인 소규모 코옵 구현(현재 코드 상한 4 — 상향 필요)
- NGO + 커스텀 `SteamP2PRelayTransport`로 transport 계층 교체

## 문서

- [[project-overview|프로젝트-개요]]
- [[prep-checklist|사전준비-체크리스트]]
- [[decision-priority|의사결정-우선순위]]

## 최우선 의사결정 (전체 설계의 분기점)

- [[network-topology|네트워크-토폴로지]]
- [[authority-model|권한-모델]]
- [[netcode-solution|netcode-솔루션]]
- [[render-pipeline|렌더-파이프라인]]
- [[server-hosting|서버-호스팅]]

## ⚠ 비판·리스크

- **심각도 보통(해소)**: 장르·코어루프·동접은 2026-06-15 확정([[project-overview|개요]]). **타겟 사양만 실측 후 확정** 남음.
- **심각도 높음 — 동접 불일치**: 기획 2-5명인데 코드 `maxPlayers = 4`. 5인 지원 시 로비·대역폭·스폰 재검토 필요. 게다가 하드코딩이라 ScriptableObject/설정 외부화도 안 됨.
- **심각도 낮음**: 이 허브 문서는 링크 목록만 있고 실제 내용은 각 문서에 분산됨 — 새 팀원이 진입점을 파악하는 데 여러 문서를 순회해야 한다.

## 관련 문서

- [[project-overview|프로젝트-개요]]
- [[01-foundation-hub|01 · 기반]]
- [[index|인덱스]]

---
← [[index|인덱스]]
