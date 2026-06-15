# Step 0 베이스라인 측정 기록 (M1~M11)
> 실행계획 v1.1 §0.A.4 · 측정 절차: [SCN_Procedures.md](SCN_Procedures.md)
> ⚠ **본 측정은 알려진 P0 결함(P0-1·2·3·4·5) 하에서 수행된 것이다.**
> 끊김 지표는 P0-2로 인해 왜곡된 형태로 기록되며 — **그 왜곡 자체가 증거다.**
> SCN-04 클라 줍기는 P0-5로 인해 0%로 기록될 것으로 예상된다.

| 측정 일시 | 2026-06-12 (단일 에디터 부분 측정 — 하네스 사이클 2026-06-12_netcode) |
|---|---|
| 측정자 | 하네스(unity-cli) 단일 에디터 StartHost 스모크 |
| 빌드 | 에디터 (Unity 6000.3.1f1) |
| 호스트 머신 / 세션폴더 | 단일 에디터 호스트(로컬 클라 1) — Steam valid |
| 클라 머신 / 세션폴더 | **없음(2인 미실시)** → M2~M11 대부분 수동 대기 |

> **단일 에디터 부분 측정 결과(2026-06-12)**: StartHost 성공(isHost·listening·로컬클라1). 데이터 흐름이
> 필요 없는 항목만 자동 확정. 원격 클라(2번째)가 없어 OnMessage 경유 트래픽이 없으므로 NetSim
> 주입 지연·M2/3/5/6/7/8/10/11은 **2인/Steam 측정 대기**.

---

## M1~M11 실측표

> "예상 베이스라인"은 점검 보고서 v2의 정적 분석 결과. **실측이 예상과 다르면 §재검토 메모에 기재하고 보고서 해당 항목을 재검토한다.**

| ID | 지표 | 측정 도구 / SCN | 예상 베이스라인 | **실측치 (기입)** | 예상과 일치? |
|---|---|---|---|---|---|
| M1 | Transport 보고 RTT | RNSM HUD (PROF-A 주입 상태) | 0 고정 (P0-4) | **0ms (단일 에디터 호스트 로컬클라, 2026-06-12)** | ✅ 예상대로 0 (loopback) — P0-4 구현 후에도 로컬 클라 RTT는 0이 정상. 원격 클라 RTT는 2인 측정 대기 |
| M2 | 대형 메시지 수신 성공률 | F9 경계값 스윕 → echo.csv / SCN-01 | 1025B+에서 손상/Assert | 512: / 1023: / 1024: / 1025: / 1300: / 4096: / 16384: / 65536: | |
| M3 | 끊김 이벤트 정합 | kill_client.ps1 ×5 → events.csv / SCN-02A | Disconnect 시 **Connect** 수신 (P0-2) | | |
| M4 | 클라이언트 줍기 성공률 | SCN-04 | 0% (P0-5) | /20 | |
| M5 | 전투 판정 일치율 | SCN-03 + diff_verdicts.ps1 | 지연 환경에서 불일치 존재 | 전달 일치율: % / 체인 위반: 건 | |
| M6 | 호스트 업로드 | SCN-06 (counters + Profiler) | ~200KB/s 추정 | KB/s | |
| M7 | 진행형 값 트래픽 | SCN-06 Profiler 변수별 | 요리 4종 매 틱 델타 | | |
| M8 | 재호스팅 성공률 | SCN-02B ×10 | 불안정 (P1-1) | /10 | |
| M9 | 패킷당 콘솔 로그 | 콘솔 + counters (transport.recv.*) | 메시지당 1+ | 채널화 후: NETCODE_DEBUG 없을 때 0 확인 → | |
| M10 | 난입 상태 일치 항목 | SCN-05 점검표 | 문/잡기/요리 불일치 | 일치 /7 항목 | |
| M11 | 체크섬 불일치 검출력 | 인벤 에디터 강제 변조 → 30초 내 LogError | 검출 수단 없음 → v0 골격 가동 | 검출 (성공/실패), 소요 s | |

---

## 측정 부속 기록

### M2 — 실패 임계 (P0-1 이등분 탐색)
SCN-01에서 접속 실패 재현 시: 인벤 항목 수 100 → 50 → 25 ... 이등분 탐색.

| 시도 항목 수 | 접속 결과 | Assert 발생 |
|---|---|---|
| 100 | | |
| | | |

**실패 임계 항목 수**: ___ (Step 1 After 대비용 핵심 증거)

### M3 — 타임라인 발췌 (events.csv)
강제 끊김 1회분의 호스트 events.csv 행을 그대로 붙여넣기:
```
(예상 형태 — P0-2 증거)
...,TRANSPORT-RAW,"Server.OnDisconnected conn=131073 endReason=..."
...,TRANSPORT-EVT,"Connect clientId=131073 ..."   ← 끊김인데 Connect
...,NGO,"OnClientConnectedCallback clientId=131073 ..."  ← 유령 클라 등록
```

### M5 — verdict_diff_report.md 첨부 경로
- Before 리포트:

### M6 — Profiler 캡처 저장 경로 / 상위 대역폭 항목 5개
1.
2.
3.
4.
5.

---

## 도구 자체 검증 (실행계획 v1.1 §0.B)

> **단일 에디터 스모크(2026-06-12, 하네스 사이클 `2026-06-12_netcode`)**: 부착·컴파일·플레이 진입까지
> 자동 검증 완료. **데이터 흐름이 필요한 항목(RTT 실추종·CSV diff·MISMATCH·echo·soak 요약)은
> StartHost + 2인/Steam 필요 → 수동 측정 대기**로 분리(G1-Q2 자동화 경계).

| 검증 | 합격 기준 | 결과 |
|---|---|---|
| RNSM/Profiler 가동 | 지표 표시 (RTT 0이어도 무방 — 기록) | ☑ **부착·구성 OK**(RnsmHud→RuntimeNetStatsMonitor 런타임 생성, RTT/Sent/Recv 카운터 config). 지표 실표시는 StartHost 후 — 수동 |
| NetEventLogger | 정상/강제 종료 각 1회 타임라인 CSV 생성, Connect 오발화 포착 | ☑ 부착 OK / CSV·오발화 포착은 수동(끊김 발생 필요) |
| VerdictLogger | 2인 타격 10회 → 양측 CSV diff 산출 가능 | 호출부 이식 확인(전투 3파일) / diff는 2인 수동 |
| StateChecksum v0 | 인벤 강제 변조 → 30초 내 MISMATCH LogError | ☑ 부착 OK / MISMATCH는 변조+세션 수동 |
| P2-4 채널화 | 정의 없음=패킷 로그 0 + 카운터 동작 / NETCODE_DEBUG=로그 복원 | ☑ **확인** — NETCODE_DEBUG 미정의 빌드에서 transport 수명주기 로그 11건+패킷 로그 콘솔 0. 카운터(transport.recv/send.*) 코드 보존 |
| soak 하네스 | F10 토글 → soak_summary.md 생성 | ☑ 부착 OK(F10) / 요약 생성은 수동 |
| kill 매크로 | 클라 프로세스 강제 종료 동작 | 스크립트 존재 / 실행은 수동 |
| F9 에코 하니스 | echo.csv 생성 (실패 결과도 정상 기록) | ☑ 부착 OK(F9) / 스윕 실행은 수동 |
| **NetSim 토글(신규)** | F8 → PROF-G/A/B 순환, OnGUI 라벨, NETSIM events 기록 | ☑ **부착·컴파일 OK**(NetSimController). 주입 효과(RNSM RTT 추종)는 StartHost 후 수동 |

> **NetSim 범위 한정**: PROF 코드 시뮬레이터는 **지연(RTT/2)·지터만** 주입한다. 손실(loss)은
> 커스텀 Steam transport의 OnMessage(신뢰성 계층 이후)에서 드롭하면 reliable 메시지가 영구
> 유실되므로 미주입. **PROF-A(2%)·PROF-B(5%) 손실률은 OS레벨 Clumsy로 측정 시 보완**한다(계획 §2 대안).
> 재정렬 방지를 위해 release time을 FIFO 단조 증가로 클램프 → sequenced/reliable 순서 보존.

---

## 재검토 메모 (실측 ≠ 예상 항목)

| 지표 | 예상 | 실측 | 보고서 재검토 결론 |
|---|---|---|---|
| | | | |

---

## Step 0 완료 판정

- [ ] 구축 체크리스트 전 항목 (v1.1 §0.C 구축)
- [ ] SCN-01~07 × PROF-G/A 1회전 측정 완료
- [ ] M1~M11 실측치 본 문서 기입 완료
- [ ] P0-1 실패 임계 기록
- [ ] 재검토 메모 작성 (해당 시)

**판정**: ☐ 통과 → Step 1 착수 가능 / ☐ 미통과 (사유: )
