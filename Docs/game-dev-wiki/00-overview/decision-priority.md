---
title: 의사결정-우선순위
tags: [overview, decision]
status: done
source:
  - Assets/Scripts/Networking/SteamLobbyManager.cs
  - Assets/Scripts/Networking/SteamP2PRelayTransport.cs
  - Packages/manifest.json
  - ProjectSettings/ProjectVersion.txt
verified: 2026-06-15
---

# 의사결정-우선순위

후반 전환 비용이 크거나 모든 것을 좌우하는 항목 순서. **코드로 확정된 항목은 [결정됨]으로 표기; 미확정은 [확인 필요]로 표기한다.**

## 현황 (pB)

| 순위 | 항목 | 결정 상태 | 근거 코드 | 비고 |
|---|---|---|---|---|
| 1 | 네트워크 토폴로지/권한/Netcode | **결정됨** | `SteamP2PRelayTransport.cs`, `SteamLobbyManager.cs` | NGO + Steam P2P relay, Host-Authority 모델 |
| 2 | 버전관리·LFS·직렬화 설정 | **부분 결정** | `.gitattributes`, `.gitignore` | LFS 미설정(바이너리 추적 패턴 없음), Force Text 직렬화 설정 확인됨 |
| 3 | 렌더 파이프라인 | **결정됨** | `manifest.json`: `com.unity.render-pipelines.universal` 17.3.0 | URP, SSGI 실험적 사용 |
| 4 | 네트워크 아키텍처 골격 | **부분 구현** | `Assets/Scripts/Utilities/NetDiagnostics/` | NGO 기반 구조 구현, 세부 state-sync 설계는 진행 중 |
| 5 | 멀티플레이 테스트 환경 | **부분 구현** | `com.unity.multiplayer.playmode` 2.0.1 | 패키지만 설치, 절차서 미작성 |

## 설계·결정

**1순위: Netcode**
- NGO 2.7.0 + 커스텀 `SteamP2PRelayTransport`(NetworkTransport 상속) 결정됨
- Facepunch.Steamworks + Steam Relay API 사용 — 전용 서버 없는 P2P

**2순위: 버전관리**
- `.gitattributes`에 셸 스크립트 eol=lf 설정만 존재. Unity 바이너리(`.png`, `.fbx`, `.wav` 등) LFS 트래킹 패턴 미설정 — **확인 필요**
- Force Text 직렬화: `EditorSettings.asset`의 `m_SerializationMode: 2` 로 확인

**3순위: 렌더 파이프라인**
- URP 17.3.0 결정됨. SSGIURP 패키지(Assets/Package Install)로 SSGI 추가. URP Toon Shader 실험 사용 중

**4~5순위**
- 멀티플레이 도구 패키지는 설치됐으나 실제 테스트 씬/절차는 `AI TEST`, `Scene_World_01` 정도에 국한 — 2인 이상 통합 검증 절차서 없음(확인 필요)

## ⚠ 비판·리스크

- **심각도 높음**: LFS 설정이 누락됐다. `.gitattributes`에 바이너리 에셋 패턴이 없으므로 `.png`/`.fbx`/`.wav` 등 대형 바이너리가 git 오브젝트로 직접 추적될 위험. 레포 크기 폭증 및 clone 시간 증가가 예상된다. → **즉시 `.gitattributes`에 LFS 패턴 추가 권고**
- **심각도 보통 (대부분 해소)**: 장르·코어루프·동접은 2026-06-15 확정([[project-overview|개요]] — 2-5인 PvE 코옵 던전·채굴, 몹 10내외). **타겟 사양만 실측 후 확정** 남음. 단 동접이 기획 **2-5명**인데 코드 `maxPlayers=4` — 5인 상향 + 5인 부하 측정 필요.
- **심각도 보통**: `multiplayer.playmode` 패키지가 설치됐으나 2인 동시 테스트 절차서가 없다. 현재는 단일 클라이언트 에디터에서만 검증 중으로 추정.
- **심각도 낮음**: 순위 4·5(state-sync, 멀티플레이 테스트)는 1·2·3에 의존하므로 앞 결정이 고정되기 전까지 문서화가 어렵다. 현재 상태는 "구현 진행 중, 설계 확정 전".

## 관련 문서

- [[prep-checklist|사전준비-체크리스트]]
- [[project-overview|프로젝트-개요]]
- [[version-control-git-lfs|버전관리-git-lfs]]
- [[network-topology|네트워크-토폴로지]]

---
← [[00-overview-hub|00 · 개요 & 우선순위]] · [[index|인덱스]]
