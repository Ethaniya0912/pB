# test_run · 테스트 결과 — 2026-06-13_netcode2

> **이 문서는?** `/test-run 2026-06-13_netcode2` 의 셋업·플레이 검증 결과·증빙입니다.
> **현재 판정: ◑ PARTIAL** — 씬 셋업 + 계측 스택(부착·RNSM·NetSim) 격리 검증 통과. Steam 의존 측정(M1 실측·M3·M8)은 Steam 미실행으로 미검증.
> (최초 시도는 `--wait` 타이밍으로 커넥터 세션이 끊겨 "무응답"으로 보였으나, Editor는 정상 play 중이었음 — 재연결 후 재개. G6 해소.)

## 검증 환경·시각
| 항목 | 값 |
|---|---|
| 실행 일시 | 2026-06-15 16:02 ~ 16:14(최초·연결끊김) / 재개 ~16:2x |
| Unity / Connector | 6000.3.1f1 / 0.3.22 — 최초 `--wait` 세션 끊김 → 재연결 후 `playing` 정상 |
| 테스트 씬 | [`Assets/_TestRuns/2026-06-13_netcode2/2026-06-13_netcode2_TestScene.unity`](../../../../Assets/_TestRuns/2026-06-13_netcode2/2026-06-13_netcode2_TestScene.unity) |
| 에셋 구성 | 더미 0 / 실제 0 (코드 컴포넌트만 — asset_map 슬롯 없음) |

## 셋업 결과
| 단계 | 결과 | 근거 |
|---|---|---|
| 폴더 생성 | ✅ | `Assets/_TestRuns/2026-06-13_netcode2/` |
| 씬·Hierarchy 셋업 | ✅ | `saved=True`, 그룹 2(=== Environment ===, === Netcode ===), `TestNetworkManager`(NetworkManager+SteamP2PRelayTransport) |
| transport 링크 | ✅ | `NetworkConfig.NetworkTransport==tp` (transportLinked=True) |
| 사전 컴파일 | ✅ | 계측 관련 CS 에러 0 (CaveBiomeSettings/SSGI 기존 오류만) |

## 플레이 테스트 판정 (재개 후)
| 검증 항목 | 판정 | 근거 |
|---|---|---|
| 씬 로드 console error 0 | ✅ | [console_step2](evidence/console_step2_preplay_1602.txt) |
| 활성 씬 = TestScene | ✅ | `scene=2026-06-13_netcode2_TestScene` |
| [NetDiagnostics] 7종 자동부착 | ✅ | NetEventLogger·StateChecksumV0·BoundaryEchoHarness·SoakHarness·RnsmHud·RuntimeNetStatsMonitor·NetSimController |
| RNSM 구성 | ✅ | cfg=True · visible=True · DisplayElements=3(RTT/Sent/Recv) |
| NetSim 토글 | ✅ | OFF→PROF-G→PROF-A, Enabled@PROF-A=True, reset OFF |
| Steam | ⚠ 미실행 | `SteamClient.IsValid=False` → StartHost=False (예외 없이 graceful) |
| M1 실측 RTT | ◐ 미검증 | host 미가동(Steam 부재). transport는 0 반환 |
| M3 끊김 / M8 재호스팅 | ◐ 미검증 | Steam+StartHost 필요 |

> 직전 netcode2 **구현 루프(08_result)** 에서는 Steam 실행 환경에서 StartHost·M1=0·M3·M8 정상 측정됨. 본 격리 씬도 동일 코드라 Steam 실행 시 동일 결과 기대. 증빙: [testrun_play_RESUMED_20260615.md](evidence/testrun_play_RESUMED_20260615.md)

## 자동 수정 내역 (테스트 환경·매핑만)
| # | 증상 | 수정 | 결과 |
|---|---|---|---|
| — | play 진입 무응답 | (자동 수정 불가 — Editor 무응답, unity-cli 제어 불가) | G6 보고 |

## G6 보고 — 해소됨
**증상(최초)**: `editor play --wait` 직후 "cannot connect to Unity" → status not responding(11m+). 
**규명**: 사용자가 Editor 창 확인 = **play 중·정상**. 즉 무응답은 실제 행이 아니라 **`--wait`가 play 진입/도메인 리로드 타이밍에 커넥터 WebSocket 세션을 끊은 것**(원인 #1). 재연결 후 isPlaying=true·정상 제어 확인.
- 원인 #2(Steam 초기화 블로킹) **기각** — Steam 미실행에도 StartHost가 예외 없이 false 반환(무블로킹).
- **본 코드 버그 아님**. transport·계측 코드 정상. test-run 자동수정/본코드수정 없음.
- 재발 완화: 격리 씬 검증 시 `play --wait` 대신 play 후 짧은 지연+재연결 확인 권장(하네스 운영 팁).

## 스크린샷·산출물
- 증빙: [testrun_play_20260613.md](evidence/testrun_play_20260613.md) · [status](evidence/status.txt) · [console_playmode_BLOCKED](evidence/console_playmode_BLOCKED_1602.txt)
- 테스트 에셋 루트: [`Assets/_TestRuns/2026-06-13_netcode2/`](../../../../Assets/_TestRuns/2026-06-13_netcode2/)

## 잔여 / 다음 실행 안내
- Editor 복구 후 `/test-run 2026-06-13_netcode2` 재실행 또는 "이어서 진행" → 씬은 보존되므로 ⑤ 플레이 검증부터 재개.

---
## 🔗 관련 문서 (Foam)
- 테스트: [[2026-06-13_netcode2/test_run/test_def|테스트 정의]] · [[2026-06-13_netcode2/test_run/asset_map|에셋 매핑]] · **result**(현재)
- 사이클: [[2026-06-13_netcode2/08_result|⑧ result]]
- 용어: [[SteamP2PRelayTransport]] · [[NetDiagnosticsBootstrap]] → [[_glossary|용어 사전]]
