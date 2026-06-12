# SCN 표준 시나리오 절차서 — 전 단계 공통 측정 단위
> 실행계획 v1.1 §2 고정판 · Step 0 산출물 · 2026-06-12
> 모든 측정 결과 파일명: `SCN-XX_PROF-X_StepN_before|after` 형식 명기

---

## 0. 사전 준비 (모든 SCN 공통)

### 0.1 계측 시스템 확인
빌드/에디터 실행 시 자동으로 가동된다 (씬 배치 불필요 — `[NetDiagnostics]` 오브젝트 자가 부트스트랩):

| 도구 | 가동 방식 | 출력 |
|---|---|---|
| NetEventLogger | 자동 (NGO·Transport 이벤트 구독) | `events.csv` |
| VerdictLogger | 자동 (데미지 체인 4지점 이식됨) | `verdicts.csv` |
| StateChecksum v0 | 자동 (클라→서버 30초 주기) | `checksum.csv` + 불일치 시 LogError |
| BoundaryEchoHarness | **F9** (클라이언트 머신에서) | `echo.csv` |
| SoakHarness | **F10** 시작/종료 토글 | `soak_samples.csv` + `soak_summary.md` |
| 패킷 카운터 (P2-4 채널화) | 자동 집계, 종료 시 덤프 | `counters_final.csv` |

**세션 출력 폴더**: `%USERPROFILE%\AppData\LocalLow\<회사명>\<제품명>\NetDiagnostics\<timestamp>_pid<N>\`
(실행 시 콘솔 첫 로그 `[NetDiag] 세션 출력 폴더:` 에 전체 경로 표시)

- 패킷당 콘솔 로그를 다시 보려면: Player Settings → Scripting Define Symbols에 `NETCODE_DEBUG` 추가 (기본 OFF = M9 릴리즈 0 상태)
- 계측 전체 끄기(릴리즈 출하): `NETDIAG_DISABLED` 정의 추가

### 0.2 RNSM (Runtime Net Stats Monitor) 배치 — 1회 수동 작업
1. Package Manager에서 **Multiplayer Tools** 설치 확인 (manifest에 `com.unity.multiplayer.tools: 2.2.3` 추가됨 — 버전 해석 실패 시 Package Manager UI에서 "Multiplayer Tools" 검색 후 최신 설치).
2. 부트 씬(또는 NetworkManager가 있는 씬)에 빈 GameObject → `Runtime Net Stats Monitor` 컴포넌트 추가.
3. Configuration 에셋 생성, 카운터 추가: **RTT**, **Bytes Sent/Received**, **Network Objects**.
4. ⚠ **RTT가 0으로 표시되는 것이 현행 정상** — Transport `GetCurrentRtt`가 0 고정(P0-4). 이 0 표시 스크린샷이 M1 베이스라인 증거다.

### 0.3 네트워크 프로파일 (PROF) 주입
Clumsy(https://jagt.github.io/clumsy/) 사용 권장 (Steam 릴레이 트래픽 = UDP 전체에 적용):

| 프로파일 | Clumsy 설정 | 용도 |
|---|---|---|
| PROF-G | Lag 15ms(편도), Drop 0% | 기능 검증 |
| PROF-A | Lag 75ms(편도), Drop 2%, Jitter ±15ms | **표준 측정** |
| PROF-B | Lag 125ms(편도), Drop 5%, Jitter ±30ms | 스트레스 |

- Filter: `udp` (또는 게임 프로세스 포트 한정)
- 편도 Lag × 2 = RTT 목표값. 클라이언트 머신에서 주입.
- 프리셋 3종을 Clumsy 설정으로 저장해 두고 결과 파일명에 PROF 명기.

### 0.4 측정 기록 양식 (모든 SCN 공통)
```
일시 / 측정자 / Step(before|after) / SCN-ID / PROF / 빌드(에디터·스탠드얼론)
참여 머신: 호스트=___, 클라=___ (각 세션폴더 경로 기록)
결과: (SCN별 지표)
이상 관찰: (예외·끊김·시각적 어긋남 — 타임스탬프와 함께)
```

---

## SCN-01 · 접속·초기 동기화 (M2)

**목적**: 대형 초기 스냅샷(인벤 NetworkList 등) 수신 무손상 검증. P0-1 직격.

1. 호스트: 월드 생성 후 자기 인벤토리에 **100+ 항목** 적재 (에디터 치트/세이브 활용).
2. 클라: 접속 시도. 접속 성공/실패, 콘솔 Assert("Message size exceeds...") 여부 기록.
3. 접속 성공 시: 클라 인벤토리 UI와 호스트 원본 대조 (항목 수·배치).
4. **F9 경계값 스윕** (클라 머신): 512→64KB 자동 진행, `echo.csv` 결과 기록.
5. 체크섬: 접속 후 30초 대기 → `checksum.csv` 첫 행 match 여부.

**실패 시 (P0-1 베이스라인)**: 인벤 항목 수를 이등분 탐색(100→50→25...)해 **접속 성공/실패 임계 항목 수**를 기록. 이 임계가 Step 1 After 대비의 가장 선명한 증거.

| 기록 | 값 |
|---|---|
| 접속 성공률 (시도 N회 중) | |
| Assert/손상 발생 크기 (echo.csv) | |
| 실패 임계 인벤 항목 수 | |
| 체크섬 첫 비교 결과 | |

---

## SCN-02 · 끊김·재호스팅 (M3 · M8)

**목적**: 끊김 이벤트 정합(P0-2) + 재호스팅 안정성(P1-1) 측정.

**A. 강제 끊김 ×5** (M3):
1. 2인 접속 상태에서 호스트 머신에서 실행:
   `powershell -File Tools\kill_client.ps1 -ProcessName <클라 프로세스명> -Repeat 5 -DelaySeconds 30`
   (각 회차 사이에 클라 재기동·재접속)
2. 매 회차 호스트에서 확인:
   - `events.csv`: `TRANSPORT-RAW Server.OnDisconnected` 직후 행이 **Connect인지 Disconnect인지** ← M3 핵심
   - 로비 UI 인원 수 / ReadyClientCount 감소 여부
   - 유령 클라이언트(목록에 남은 죽은 클라) 존재 여부

**B. 재호스팅 ×10** (M8):
1. 호스트: 방 생성 → 클라 1인 접속 → 호스트 방 파기(나가기) → 재생성. ×10 반복.
2. 매회 기록: 호스팅 성공 여부, Steam 초기화 에러, `events.csv`의 `Transport.Shutdown → SteamClient.Shutdown()` 행(P1-1 현행 증거), 재접속 가능 여부.

| 기록 | 값 |
|---|---|
| M3: Disconnect 시 수신 이벤트 (Connect/Disconnect) | |
| 유령 클라 발생 횟수 /5 | |
| M8: 재호스팅 성공 /10 | |
| Steam 재초기화 에러 내용 | |

---

## SCN-03 · 전투 50회 (M5) — PROF-A 고정

**목적**: 전투 판정 일치율. P0-3 직격.

1. 클라 머신에 PROF-A 주입.
2. 2인 상호: **타격 50회 / 패링 시도 50회 / 회피 중 타격 50회** (패링·회피는 타이밍이 생명 — 정상 게임플레이 속도로).
3. 종료 후 양측 세션폴더 회수 → 호스트 머신에서:
   `powershell -File Tools\diff_verdicts.ps1 -HostDir <호스트폴더> -ClientDir <클라폴더>`
4. `verdict_diff_report.md`의 ① 전달 일치율 ② 체인 정합 위반 ③ R6 카운터 기록.

| 기록 | 값 |
|---|---|
| M5: RECV 전달 일치율 | |
| 체인 정합 위반 (Hit↔HP_APPLY 불일치) | |
| Blocked/Parried인데 HP 차감된 건수 | |
| 예외 발생 (LogError/Exception) | |

---

## SCN-04 · 획득·인벤 동시조작 (M4)

**목적**: 클라이언트 줍기 성공률(P0-5) + 인벤 동시 조작 정합(P1-4).

1. **클라이언트가** 바닥 아이템 줍기 시도 ×20 — 성공 횟수 기록 (베이스라인 예상 0/20, 호스트는 정상).
2. 호스트도 동일 ×20 (대조군).
3. 공유 컨테이너(또는 동일 인벤)에서 양측 동시 Move ×20 — 겹침/복제/소실 발생 수.
4. 가방 픽업: 호스트가 줍기(현행 유일 동작 경로) → 정상 여부. (클라 줍기는 무반응이 베이스라인)
5. 체크섬 30초 주기 결과에서 인벤 불일치(MISMATCH) 발생 여부 — **불일치가 뜨면 그 자체가 디싱크 증거**.

| 기록 | 값 |
|---|---|
| M4: 클라 줍기 성공 /20 | |
| 호스트 줍기 성공 /20 | |
| 동시 Move 겹침/복제 건수 | |
| 체크섬 MISMATCH 건수 | |

---

## SCN-05 · 난입 (M7 · M10)

**목적**: Late-join 상태 복원 일치 점검표.

1. 2인이 10분+ 플레이하며 상태 변화 축적: **문 2개 열기 / 요리 1건 진행 중 / 아이템 1개 손에 잡기 / 아이템 다수 드롭**.
2. 제3 클라(또는 기존 클라 재접속) 난입.
3. 난입 머신 화면에서 점검표 대조:

| M10 점검 항목 | 호스트 상태 | 난입 화면 | 일치 |
|---|---|---|---|
| 문 #1 열림 상태 | | | |
| 문 #2 열림 상태 | | | |
| 요리 진행도 (±0.1s) | | | |
| 잡은 아이템 손 부착 | | | |
| 드롭 아이템 위치 | | | |
| 지형 (시드 기반) | | | |
| 플레이어 스폰 위치 적법성 (P1-12: 지형 안/밖) | | | |

4. 난입 직후 체크섬 결과 + `events.csv` 접속 타임라인 기록.

---

## SCN-06 · hold-out 부하 (M6)

**목적**: 호스트 업로드 대역폭 실측 (Step 4 Before/After의 기준).

1. AI 20기 활성 전투 상태 5분 유지 (스폰 치트 또는 hold-out 구간).
2. 측정 (병행 3종):
   - **Unity Profiler → Network 모듈** (Multiplayer Tools): 오브젝트별·변수별 분해 캡처 저장
   - RNSM HUD: Bytes Sent/Received 스크린샷 (1분 간격 3회)
   - `counters_final.csv`: `transport.send.bytes` 증분 ÷ 측정 시간 = KB/s
3. 동일 절차를 Step 4의 각 소단위 적용 후 반복 → 기여도 표 작성.

| 기록 | 값 |
|---|---|
| M6: 호스트 업로드 KB/s (counters 기준) | |
| Profiler 상위 5개 대역폭 항목 | |
| M7: 요리 진행 변수 틱당 델타 여부 | |

---

## SCN-07 · soak (통합 게이트)

**목적**: 30분(EA 최종 60분) 무중단 자유 플레이의 무결성.

1. 양측 머신에서 **F10** (soak 시작). PROF-A 주입.
2. 30분 자유 플레이 (전투·획득·요리·문·이동 골고루).
3. 종료 시 **F10** → `soak_summary.md` 자동 생성. 양측 회수.
4. 합격 기준 (soak_summary.md 핵심 카운터 표):
   - `ngo.clientDisconnected` 증분 0 (의도된 종료 제외)
   - `checksum.compare.MISMATCH` 증분 0
   - soak 중 Exception 0
   - (Step 2 이후) `verdict.hp_apply.attackerSide` 증분 0

---

## 부록 A. Step별 사용 SCN 매트릭스

| | SCN-01 | SCN-02 | SCN-03 | SCN-04 | SCN-05 | SCN-06 | SCN-07 |
|---|---|---|---|---|---|---|---|
| Step 0 베이스라인 | ●(PROF-G/A) | ● | ●(A) | ● | ● | ● | ●(30분) |
| Step 1 After | ● | ● | | | | | ●(30분) |
| Step 2 After | | | ● | ● | | | ●(전투) |
| Step 3 After | | ●(재접속) | | | ● | | |
| Step 4 After | | | ●(품질) | | ●(M7·잡기) | ●(소단위별) | |
| Step 5 최종 | | ●(자동화) | | | ●(자동화) | | ●(60분) |

## 부록 B. NETCODE_DEBUG / NETDIAG_DISABLED 정의 정리

| 정의 | 기본 | 효과 |
|---|---|---|
| (없음) | ✓ | 패킷당 콘솔 로그 0 (M9 목표 상태), 카운터·CSV 계측은 가동 |
| `NETCODE_DEBUG` | | Transport 패킷당 Debug.Log 복원 (디버깅 세션 전용) |
| `NETDIAG_DISABLED` | | 계측 부트스트랩 전체 비활성 (릴리즈 출하용) |
