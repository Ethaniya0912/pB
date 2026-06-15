# 04 · assets — 에셋 작업 목록 확정

> **이 문서는?** 이번 사이클에서 만들거나 고칠 파일들의 확정 목록입니다(무엇). 보통은 신규/변경 에셋의
> 계약이지만(왜) **본 사이클은 검증·측정이라 Unity 에셋 변경이 0** — 산출물은 측정 증빙·문서뿐이며(어떻게),
> ③scope 직후 정리하고 사람은 [G3](decisions.md#G3) 에서 확인합니다(언제·누가). (Step 1 코드·계측 7종 전부 기구현, 재작성 금지.)

## 한눈에 — 변경·활용 맵 (Unity 에셋 변경 0)
> 회색=검증만 하는 기구현 에셋, 초록=이번 사이클 산출 문서/증빙, 골드=기존 문서 갱신.
```mermaid
flowchart LR
  subgraph keep["Unity 에셋 — 변경 0 (검증 대상)"]
    TR["SteamP2PRelayTransport<br/>Step1 코드"]:::keep
    DIAG["계측 7종<br/>(Step 0 산출)"]:::keep
  end
  TR -. 측정 .-> O1["O1 evidence/<br/>측정 덤프"]:::add
  DIAG -. events.csv .-> O1
  O1 --> O2["O2 Step1_Evidence.md<br/>Before/After 기입"]:::mod
  O1 --> O3["O3 08_result<br/>측정 요약"]:::add
  classDef keep fill:#ECEFF3,stroke:#5C6675,color:#374151;
  classDef add  fill:#E5F4EC,stroke:#1E8A5B,color:#14532d;
  classDef mod  fill:#FBF0DD,stroke:#B5731A,color:#7c4a03;
```

## assets 매트릭스
| A-ID | 경로 (클릭=열기) | 범주 | 신규/변경 | 연결 goal | 의존 |
|---|---|---|---|---|---|
| — | (Unity 에셋 변경 없음) | — | — | — | — |

## 산출물 (Unity 에셋 아님 — 측정 증빙·문서)
| ID | 경로 | 범주 | 신규/변경 | 연결 goal |
|---|---|---|---|---|
| O1 | `evidence/` 하위 측정 덤프(events.csv 발췌·StartHost/Shutdown 로그·MPPM 시도 결과) | Doc/증빙 | new | [G2](02_goal.md#G2)~[G5](02_goal.md#G5) |
| O2 | [`Reports/netcode/Step1_Evidence.md`](../../../Reports/netcode/Step1_Evidence.md) | Doc | modify | [G6](02_goal.md#G6) |
| O3 | `08_result.md` + 본 사이클 측정 요약 | Doc | new | [G6](02_goal.md#G6)·[G7](02_goal.md#G7) |

## Hierarchy 배치 (`_conventions.md` §15)
> **신규 씬 배치 0** — 이 사이클은 아무 GameObject 도 새로 만들지 않는다. 측정 대상은 **직전 사이클이 만든** 것:
| 검증 대상 | 배치 유형 | 경로 / 방식 |
|---|---|---|
| 계측 7종(`[NetDiagnostics]`) | ⑤ 런타임 자동생성 | 직전 `2026-06-12_netcode` 산출. 플레이 시 자동 생성 — 씬에 없음 |
| SteamP2PRelayTransport | 기존 컴포넌트(검증만) | `World Network Manager`(④ 씬 단독 루트, NGO)의 Transport 컴포넌트 — 편집 없음 |

## G3 확인 대상 (네이밍·경로 확정)
- **신규 코드 파일 없음 → 네이밍 계약 불요.** G2 결정으로 갈음(decisions G2 마지막 줄).
- 측정 증빙은 `evidence/`에 자유 파일명으로 누적, 권위 기록은 `Step1_Evidence.md`.

---
## 🔗 관련 문서 (Foam)
- 이전 [[2026-06-13_netcode2/03_scope|③ scope]] · **④ assets**(현재) · 다음 [[2026-06-13_netcode2/06_test_env|⑥ test_env]] / [[2026-06-13_netcode2/07_plan|⑦ plan]]
- 게이트 결정: [[2026-06-13_netcode2/decisions|decisions]] (G2·G3)
- 용어: [[NetEventLogger]] · [[BoundaryEchoHarness]] → [[_glossary|용어 사전]]
