# 06 · test_env — 테스트 환경 정의

> **이 문서는?** 씬·에셋을 전혀 편집하지 않고 직전 사이클이 만든 계측 오브젝트(`[NetDiagnostics]`)와
> transport 컴포넌트만 대상으로 검증·측정하는 환경을 정의한 문서입니다(무엇). Step 1 기구현 코드가 올바른
> RTT·끊김·재호스팅 동작을 하는지 수치·로그로 실증해 게이트 근거를 만들려고(왜), ①정합검증→②M1 RTT→
> ③M3 끊김→④M8 재호스팅→⑤MPPM 실증 순으로 단일 에디터 host-only 범위에서 수행하며(어떻게), Step 1 코드
> 동결 후 실행자가 자동 측정하고 2인 필요분은 외부 인계합니다(언제·누가).
> 계측은 RuntimeInitializeOnLoadMethod 자동 부착 → 환경 구성 변경 0, 적용 위험 0.

## 한눈에 — 측정 절차 흐름

```mermaid
flowchart LR
    V1[정합검증 compile]:::flow --> M1[M1 RTT]:::flow --> M3[M3 끊김]:::flow --> M8[M8 재호스팅]:::flow --> MPPM[MPPM 2피어 실증]:::flow

    classDef flow fill:#EBF0FF,stroke:#2A52DB,color:#1e3a8a;
```

## test_env 매트릭스
| 대상 | as-is | to-be | 적용 방법 |
|---|---|---|---|
| 측정용 세션(호스트) | 활성 씬에서 플레이만 | StartHost로 호스트 세션 가동 | `editor play --wait` + exec `NetworkManager.StartHost()` |
| 끊김/재호스팅 | — | exec `Shutdown()` ↔ `StartHost()` 반복 | exec 루프 |
| 2번째 피어(MPPM) | virtual player 0 | Player 2 활성화 시도 → StartClient | MPPM API exec 시도(1회 실증) |
| 증빙 | — | events.csv 발췌·로그를 `evidence/`로 복사 | exec/파일 복사 |

## to-be Hierarchy 배치

**씬·프리팹 편집 0** — 측정 대상은 직전 사이클이 만든 `[NetDiagnostics]`(런타임 자동생성, 6+1종)와 `World Network Manager`(씬 단독 루트)의 SteamP2PRelayTransport 컴포넌트.

```text
DontDestroyOnLoad (플레이 중)
└─ [NetDiagnostics]  ← 직전 사이클 산출, 런타임 자동생성 (편집 없음)
   └─ NetEventLogger 등 7종 (측정 소스)
━━━━ 활성 씬 ━━━━
└─ World Network Manager (NGO 단독 루트)
   └─ [comp] SteamP2PRelayTransport ← Step1 코드 검증 대상 (편집 없음)
```

## 측정 절차 (구현 루프에서 실행)
1. **정합 검증**: `editor refresh --compile` → console error 0(계측 무관 기존 오류 제외). `[NetDiagnostics]` 6+1종 부착 확인.
2. **M1 RTT**: StartHost → `GetCurrentRtt(0)` = 0 (loopback, Step0 Before와 동일) → evidence 기록.
3. **M3 끊김 정합**: StartHost → `Shutdown()` → NetDiagnostics 세션폴더 `events.csv`에서 `TRANSPORT-RAW … Shutdown`/`Disconnect` 짝 확인, **`Connect` 오발화 0**(P0-2 효과) → events.csv 발췌를 evidence로.
4. **M8 재호스팅**: StartHost→Shutdown→StartHost ×3~5 → 매회 `SteamClient.IsValid==true` 유지, StartServer 에러 0 (P1-1: SteamClient.Shutdown 미호출 효과) → evidence.
5. **MPPM 2피어 1회 실증**: MPPM API로 virtual player 활성 시도 → 가능하면 client가 host에 StartClient. **성공=M2/M3 일부 자동화 / 실패=Steam 단일계정 self-connect 차단 실측 확정**. 결과(성공/실패+사유)를 evidence·Step1_Evidence에 기록.

## 검증 범위 분리
- **자동(구현 루프)**: 위 1~4 + 5의 시도. 단일 에디터 host-only 한계 내.
- **수동 인계(2인/Steam/시간)**: M2 경계값(F9 on 원격 클라), M8 정량 10/10, SCN-02 kill×5, SCN-07 30분 soak(데모 게이트 1차), 원격 RTT 추종.

## 적용 스니펫
```bash
# 세션폴더 경로 확인 → events.csv 발췌
unity-cli exec "return NetDiag.NetDiagnostics.SessionDir;"
# 재호스팅 루프 1스텝
unity-cli exec "var nm=Unity.Netcode.NetworkManager.Singleton; nm.Shutdown(); return \"shutdown\";"
```

## G5 확인
- 적용 대상 씬/프리팹: **없음**(플레이 진입만, 부트스트랩 자동). 에셋 정합화 위험 0.
- 영향 범위: 런타임 세션 가동·종료(에디터 내). 게임 로직 무침습.
- 승인 요청: 위 측정 절차(1~5) + MPPM 1회 실증 + 자동/수동 경계로 진행.

---
## 🔗 관련 문서 (Foam)
- 이전 [[2026-06-13_netcode2/04_assets|④ assets]] · **⑥ test_env**(현재) · 다음 [[2026-06-13_netcode2/07_plan|⑦ plan]]
- 게이트 결정: [[2026-06-13_netcode2/decisions|decisions]] (G5)
- 용어: [[M-지표]] · [[RTT]] · [[NetEventLogger]] · [[SteamP2PRelayTransport]] · [[SCN-시나리오]] · [[soak-테스트]] → [[_glossary|용어 사전]]
