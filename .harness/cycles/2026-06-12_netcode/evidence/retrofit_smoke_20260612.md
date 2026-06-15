# 소급 재검증 스모크 기록 — 2026-06-12 21:35~21:44

> 하네스 증빙 규약(§10) 도입에 따른 **사후 재검증**. 사인오프(19:20) 당시 기록을 현재 상태에서 재확인했다.
> 환경: [status.txt](status.txt) — Unity 6000.3.1f1 · Connector 0.3.22 · PID 17540.

## 타임라인 (unity-cli 실측)
| 시각 | 행위 | 결과 |
|---|---|---|
| 21:35:51 | `editor play --wait` 진입 (#1) | playing 확인 |
| 21:36 | `[NetDiagnostics]` 컴포넌트 조회 | **7슬롯 중 1개 null** — 첫 시도는 null 미필터로 NRE, 필터 후 6종 생존 확인 |
| 21:39 | 스크린샷 #1 | [play_smoke_session1.png](play_smoke_session1.png) |
| 21:39 | stop → fresh play 재진입 (#2) | 재현 확인 목적 |
| 21:40:06 | fresh 조회 | `total=7 nulls=1 netsim=False` — **재현 2/2** |
| 21:41 | 프로브: 새 GO에 NetSimController 단독 부착 | 부착·3초 후 **생존** (`added=True / alive2=True`) |
| 21:42 | `[NetDiagnostics]`에 NetSimController **재부착** | **생존** (`total=8 nulls=1 netsim=True`) |
| 21:42 | 프로브2: RnsmHud 단독 부착 | `total=2 nulls=0` (RnsmHud+RuntimeNetStatsMonitor — RNSM 내부 컴포넌트 가설 기각) |
| 21:43 | 최종 증빙 수집 | [components.txt](components.txt)(재부착 후 상태) · [console_error.txt](console_error.txt) · [console_tail.txt](console_tail.txt) · [play_smoke_rnsm.png](play_smoke_rnsm.png) |
| 21:44 | `editor stop` | ready 복귀 |

## 판정
| 항목 | 원기록 (19:20 사인오프) | 재검증 (21:35~) | 판정 |
|---|---|---|---|
| `[NetDiagnostics]` GO 생성 | OK | OK | ✅ 유지 |
| RnsmHud + RuntimeNetStatsMonitor 부착·HUD 표시 | OK | OK (스크린샷: 좌상단 RTT/Sent/Recv 패널) | ✅ 유지 |
| 계측 4종(NetEventLogger·StateChecksumV0·BoundaryEchoHarness·SoakHarness) | OK | OK | ✅ 유지 |
| **NetSimController 부착** | OK(7종 생존 기록) | **null(파괴됨)** — fresh 재현 2/2 | ⚠ **회귀 발견** |
| console 계측 관련 에러 | 0 | 0 (잔여 에러는 기존 Cave/DDOLHelper/SSGI — 본 작업 무관) | ✅ 유지 |

## ⚠ 발견: NetSimController 초기화 윈도우 파괴 (후속 조사 필요)
- **증상**: 부트스트랩(`RuntimeInitializeOnLoadMethod`)이 부착한 `NetSimController`만 시작 직후 파괴되어 fake-null 슬롯으로 남음. → 현재 상태에서 **F8 PROF 토글이 부트스트랩 경로로는 동작 불가**.
- **반증 실험**: 플레이 중 수동 `AddComponent`(프로브 GO·실제 GO 모두)는 생존 → 클래스 자체 문제 아님, **시작 윈도우 한정**의 외부 파괴.
- **배제된 가설**: RNSM 내부 컴포넌트(프로브2로 기각) · NetDiagnostics 폴더 내 Destroy 코드(없음) · 범용 MonoBehaviour 스윕(에디터 툴뿐).
- **원기록과의 관계**: 19:20 검증 스니펫은 null이 있었다면 NRE가 났을 것 → 당시엔 7종 생존이 맞고, **사인오프 이후 프로젝트 변경으로 회귀**했을 가능성이 높음.
- **스크린샷의 NetSim 라벨 주의**: [play_smoke_rnsm.png](play_smoke_rnsm.png) 우상단 "NetSim: OFF (F8)" 라벨은 21:42 **수동 재부착분**의 OnGUI다(부트스트랩분 아님).

---

## ✅ 해결 — 원인 규명·수정·재검증 (2026-06-12 ~23:05, 후속 조사)

### 근본 원인 (3중 결합 — "외부 파괴 코드"는 존재하지 않았다)
1. **파일명≠클래스명 → MonoScript 미바인딩**: `NetSimController`가 `NetSimProfiles.cs` 안에 정의되어
   Unity 가 스크립트 에셋(m_Script)을 연결하지 못함. AddComponent 직후엔 정상 동작하지만,
   **살아있는 상태에서도** `GameObjectUtility.GetMonoBehavioursWithMissingScriptCount`=1 로 집계됨(라이브 확인).
2. **도메인 리로드 시 husk화**: m_Script 가 없으면 리로드의 직렬화→복원에서 클래스 재결합 실패 →
   `The referenced script (Unknown) on this Behaviour is missing!` → GetComponents 배열에 null 슬롯로 잔존.
   세션 #1(21:35:56)은 부트스트랩 직후 강제 리로드가 끼어 사망 — Editor.log 36869(bootstrap)
   → 37040~37042(`Reloading assemblies for play mode` + ReloadAssembly) → 37148(`StopAssetImportingV2(... | ForceDomainReload)`)
   → 37182~37186(missing! ×5).
3. **DontSave 좀비 누적**: 부트스트랩 GO 의 `HideFlags.DontSaveInEditor`는 플레이 종료 시 자동 파괴를 막는다
   → 세션마다 GO 가 에디터에 누적(조사 시점 **5개**: 18:47/19:04/19:10/21:35/21:39 세션분).
   **세션 #2(21:40)의 "재현"은 신규 GO 가 아니라 `GameObject.Find` 가 잡은 구세대 좀비**(husk 보유)였다.
   → 기존 반증 실험(수동 부착 생존·RnsmHud 단독 생존)과 모두 정합: 그 인스턴스들은 이후 리로드를 다시 안 거쳤을 뿐.

### 라이브 증거 (수정 전, 에디트 모드 프로브)
```
goCount=5
-31144(21:39분): total=7 전원 생존인데 missingCnt=1   ← 살아있어도 m_Script=None
-29760/-19082/-17308(구세대 3개): total=7 realNulls=1 netsim=False ← 리로드 횡단 후 husk(재현 시그니처)
-15708(최고참 18:47분, Find 적중): total=8 realNulls=1 netsim=True ← 21:42 수동 재부착 흔적 잔존
```

### 수정 (4건, 모두 `Assets/Scripts/Utilities/NetDiagnostics/`)
| 파일 | 변경 |
|---|---|
| `NetSimController.cs` **신규** | MonoBehaviour 를 파일명=클래스명 단독 파일로 분리 — **m_Script 바인딩 확보(본질 수정)** |
| `NetSimProfiles.cs` | NetSimController 클래스 제거(NetSimPreset/NetSimProfiles 로직 무변경) |
| `NetDiagnosticsBootstrap.cs` | (a) 부트스트랩 시 잔존 `[NetDiagnostics]` GO 스윕 회수(에디터 전용) (b) ExitingPlayMode 자체 정리 |
| `RnsmHud.cs` | 런타임 생성 config(SO, DontSave) OnDestroy 정리 — 세션 반복 누수 방지 |

### 재검증 (23:00~23:05, unity-cli 실측)
| 단계 | 결과 |
|---|---|
| `editor refresh --compile` | CS 에러 **0** (콘솔 잔여 3건은 기존 Cave/SSGI — 본 작업 무관) |
| play #1 + 프로브 | goCount=**1**(좀비 5개 회수), total=7 nulls=0 **missingCnt=0** netsim=True — 7종 전원 생존 |
| **플레이 중 강제 리로드**(`refresh --compile --force`, 원 트리거 재현) | 동일 GO(-36982)가 도메인 리로드를 거치고도 **7종 전원 생존** ✅ |
| play #2 + 프로브 | goCount=1(중간 누수분 스윕 회수·신규 id -39602), 7종 전원 생존 — **2회 연속 통과** |
| stop 후 에디트 모드 | zombieGOs=**0** (ExitingPlayMode 정리 동작) |
| `console --type error` | NetDiag 관련 **0** (잔여는 기존 Cave/DDOLHelper/SSGI) |

### 운영 노트
- **플레이 중 `editor refresh --compile` 은 도메인 리로드를 유발**한다(v0.3.22 는 기본 차단, `--force` 시 수행되며 플레이가 종료될 수 있음). 검증 플로우는 stop → refresh → play 순서 유지 권장.
- 21:36 재현 당시의 리로드는 플레이 진입 직전 큐잉된 컴파일이 진입 직후 떨어진 것으로 추정(`Reloading assemblies for play mode`).
- 수정 전 세션들이 남긴 RNSM config SO 6개(메모리 내 잔재)는 에디터 재시작 시 소멸 — 별도 조치 불요.
- 재발 방지 규칙: **MonoBehaviour/ScriptableObject 는 반드시 파일명=클래스명 단독 파일**. 런타임 AddComponent 만 쓰더라도 도메인 리로드 한 번에 missing-script 가 된다.
