# test-run 재개 검증 (Editor play 정상) — 2026-06-15

커넥터 재연결 후 격리 씬 play 검증 (Steam 미실행 환경).

| 항목 | 결과 |
|---|---|
| 활성 씬 | 2026-06-13_netcode2_TestScene ✅ |
| [NetDiagnostics] 부착 | 7종 ✅ (NetEventLogger·StateChecksumV0·BoundaryEchoHarness·SoakHarness·RnsmHud·RuntimeNetStatsMonitor·NetSimController) |
| RNSM | cfg=True · visible=True · DisplayElements=3 (RTT/Sent/Recv) ✅ |
| NetSim 토글 | OFF→PROF-G→PROF-A, Enabled@PROF-A=True, reset OFF ✅ |
| Steam | **False (미실행)** → StartHost=False (예외 없이 graceful, 무블로킹) |
| M1 실측 RTT | host 미가동 — 미검증(transport 0 반환) |
| M3 끊김 / M8 재호스팅 | Steam+StartHost 필요 — 미검증 |

G6 원인 규명: play 진입 자체는 정상(Editor playing 확인). 이전 "무응답"은 `--wait`가 play 진입/도메인 리로드 타이밍에 커넥터 세션을 끊은 것(원인 #1). Steam 부재 시 StartHost가 예외 없이 false 반환 → Steam 초기화 블로킹(원인 #2) 기각.

판정: PARTIAL — 계측 스택(부착·RNSM·NetSim) 격리 씬 검증 통과. Steam 의존 측정(M1실측·M3·M8)은 Steam 클라이언트 실행 후 재실행 필요.
