# Step 1 · 전송 안정화 — Evidence (Before/After)
> 실행계획 v1.1 §1 · 게이트 판정 근거 문서 · 측정 절차: [SCN_Procedures.md](SCN_Procedures.md)
> Baseline 출처: [Step0_Baseline.md](Step0_Baseline.md) (없으면 본 After 측정 전에 반드시 선행)

## 구현 내역 (2026-06-12)

| 이슈 | 파일 | 수정 내용 |
|---|---|---|
| P0-1 | `SteamP2PRelayTransport.cs` Client/ServerCallbacks.OnMessage | `byte[1024]` 고정 버퍼 + Assert/TODO 제거 → 메시지 `size` 정확 할당. 클라/서버 경로 동일 로직 통일. 버퍼 재사용 경합 동시 제거 |
| P0-2 | `SteamP2PRelayTransport.cs` ServerCallbacks.OnDisconnected | `NetworkEvent.Connect` → `NetworkEvent.Disconnect` (1단어). 끊김 정리 체인(ready맵·세션 카운트·복귀)이 비로소 도달 가능해짐 |
| P0-4 | `SteamP2PRelayTransport.cs` GetCurrentRtt | 상수 0 → Facepunch `Connection.QuickStatus().Ping` 실측 반환 (클라=서버연결, 서버=clientId 매칭). API는 DLL 리플렉션으로 확정 |
| P1-2 | `SteamP2PRelayTransport.cs` CastToSendType | 매핑표 교정 — 아래 표 참조 |
| P1-1 | `SteamP2PRelayTransport.cs` Shutdown | `SteamClient.Shutdown()` 호출 제거 → `clientConnection.Close()` + `socketManager.Close()` 소켓만 정리 |
| P1-1 | `SteamClient.cs` | 펌핑 유일 지점 명시. `DontDestroyOnLoad` 실제 적용(주석만 있던 의도 구현). **isOwner 가드** — 중복 인스턴스 파괴가 Steam API를 끄던 결함 차단 |
| P1-1 | `SteamLobbyManager.cs` Update | `RunCallbacks` 이중 펌핑 제거 (프레임당 정확히 1회 = SteamClient) |
| P2-8 최소 | `WorldItemSpawner.cs` Awake | 중복 분기 `return` 누락 수정 (죽는 중복에 DontDestroyOnLoad 적용되던 문제) — SCN-02 측정 오염 방지 |

### P1-2 매핑표 (Before → After)

| NGO NetworkDelivery | Before (Steam SendType) | After | 근거 |
|---|---|---|---|
| Unreliable | Unreliable | Unreliable | 의미 일치 |
| UnreliableSequenced | **Unreliable (순서 비보장!)** | **Reliable (승격)** | Steam에 sequenced-unreliable 부재 — 순서 보장이 의미의 핵심 |
| Reliable | Reliable | Reliable | Steam reliable은 순서도 보장 |
| ReliableSequenced | Reliable | Reliable | 〃 |
| ReliableFragmentedSequenced | Reliable | Reliable | Steam 메시지당 512KB 네이티브 지원 (P0-1 수정으로 수신측도 대응) |

---

## 효과 입증 — Before/After 표 (측정 후 기입)

> 동일 SCN · 동일 PROF에서 측정. Baseline 칸은 Step0_Baseline.md에서 전기.

| 지표 | 검증 방법 | Baseline (Step 0) | After (Step 1) | 합격 기준 | 판정 |
|---|---|---|---|---|---|
| M1 RTT | RNSM + PROF-A(150ms)/B(250ms) | 0 고정 | | 주입 지연 ±20% 추종 | |
| M2 대형 메시지 | F9 스윕 (echo.csv) | 임계: ___B에서 손상 | 512: /1023: /1024: /1025: /1300: /4096: /16384: /65536: | 전부 PASS | |
| M2 접속 | SCN-01 인벤 100+ | 성공률 ___% | /회 | 100% | |
| M3 끊김 정합 | SCN-02A kill ×5 + events.csv | Disconnect 시 **Connect** 수신 | | TRANSPORT-RAW Disconnected → TRANSPORT-EVT **Disconnect** 짝 5/5 | |
| M3 정리 체인 | 〃 + 로비 UI | 유령 ___건 | | ready맵·세션 카운트 정상 감소, 유령 0 | |
| P1-2 순서 | Sequenced 채널 연번 100건 | (Step 0 실측) | | 역전 0건 | |
| M8 재호스팅 | SCN-02B ×10 | ___/10 | /10 | 10/10, Steam 재초기화 에러 0 | |
| 통합 | SCN-07 30분 (PROF-A) | — | | 끊김·유령·디싱크 0, 체크섬 MISMATCH 0 | |

### 증거 첨부 목록
- [ ] RNSM RTT 칸 캡처 (Before 0 / After 추종) — M1
- [ ] echo.csv Before/After — M2
- [ ] events.csv 타임라인 발췌 (Disconnected→Connect vs Disconnected→Disconnect) — M3
- [ ] 재호스팅 10회 events.csv — M8
- [ ] soak_summary.md (SCN-07) — 통합

> R2 반영: "재접속 후 재합류"는 본 단계 기준에서 제외 (Step 3 P2-6에서 검증).

---

## Step 1 게이트 판정

- [ ] 구현 체크리스트 6항목 완료 (위 표) — **코드 완료 2026-06-12, 측정 대기**
- [ ] M1·M2·M3·M8 Before/After 합격
- [ ] SCN-07 30분 soak 통과 (**데모 게이트 1차**)

**판정**: ☐ 통과 → Step 2 착수 가능 / ☐ 미통과 (사유: )

### 잔존 알려진 한계 (Step 1 범위 밖 — 기록만)
- 클라이언트 줍기 불가(P0-5)·전투 판정 분산(P0-3)은 Step 2 대상 — soak 중 관련 이상은 합격 판정에서 제외하되 기록.
- 호스트 이탈 시 클라 복귀(RevertToTitleScreen)는 동작하나, 재접속 재합류는 Step 3 P2-6 대상.
- LateUpdate Receive 펌핑의 NullReference try-catch는 유지 (Shutdown이 소켓을 null화하므로 발생 빈도 감소 예상).
