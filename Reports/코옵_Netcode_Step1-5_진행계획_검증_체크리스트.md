# 코옵 Netcode 수정 — Step 1~5 상세 진행계획 · 검증계획 · 체크리스트

> 근거 문서: **코옵 Netcode 상태 점검 보고서 v2 (3차 재점검판, 2026-06-10)** · **EnvFlagRegistry 설계 보고서 (2026-06-10)**
> 대상 스택: Unity 6.3 · NGO 2.x · Facepunch Steamworks P2P
> 작성일: 2026-06-11

---

## 0. 문서 개요

본 문서는 Netcode 점검 보고서 v2 §5의 **5단계 수정 로드맵**을 실행 가능한 작업 단위로 분해하고, 각 단계마다 **① 상세 진행계획 ② 검증계획 ③ 체크리스트**를 제공한다. EnvFlagRegistry 설계 보고서는 Step 1 완료를 착수 전제로 하는 후행 과제이므로, 본 문서 말미(§6)에 진입 게이트로 연결한다.

### 0.1 단계-게이트 정렬

| 단계 | 명칭 | 포함 등급 | 게이트 |
|---|---|---|---|
| **Step 1** | 전송 안정화 | P0-1, P0-2, P0-4, P1-2, P1-1 | 데모 게이트 **필수 선행** |
| **Step 2** | 권위 일원화 | P0-3, P0-5, P1-10, P1-3, P1-4, P1-9, P1-11 | 데모 게이트 **필수** |
| **Step 3** | 규약 표준화 | P1-7, P1-12, P2-5, P2-6, P2-10, (P1-8) | 데모~EA 게이트 |
| **Step 4** | 효율화 | P2-1, P2-2, P2-3, P2-9, (P1-5, P1-6) | EA 게이트 |
| **Step 5** | 계측·검증 | P2-7, P2-11, P2-12, P2-4, P2-8 | EA 게이트 + 상시 |

### 0.2 이슈 → 단계 매핑 (전 29건)

| 이슈 | 요약 | 단계 |
|---|---|---|
| P0-1 | 전송 수신 버퍼 1KB 고정 | 1 |
| P0-2 | 서버 OnDisconnected가 Connect 발화 | 1 |
| P0-3 | 히트+방어 판정 공격자 분산 | 2 |
| P0-4 | GetCurrentRtt 항상 0 | 1 |
| P0-5 | 클라이언트 아이템 획득 불가 | 2 |
| P1-1 | RunCallbacks ×2 + Steam 수명주기 충돌 | 1 |
| P1-2 | UnreliableSequenced 순서 비보장 | 1 |
| P1-3 | 사망 처리 IsOwner 게이트 부재 | 2 |
| P1-4 | 인벤토리 서버측 검증 부재 | 2 |
| P1-5 | WeaponItem SO Instantiate 누수 | 4 |
| P1-6 | OnClientConnectedCallback 중복 등록 | 4 |
| P1-7 | RPC 문법 3종 혼재 + clientId 수동 전달 | 3 |
| P1-8 | 지형 Ready 보고 경로 이원화·미완 | 3 |
| P1-9 | 방어 판정 부수효과 권위 불일치 | 2 |
| P1-10 | 서버가 Owner-Write 변수 직접 기록 | 2 |
| P1-11 | 슬라이싱 파편 공격자 로컬 전용 | 2 |
| P1-12 | 코옵 클라 자가 텔레포트 + SpawnPos 데드코드 | 3 |
| P2-1 | 위치 매 프레임 풀정밀 동기화 | 4 |
| P2-2 | 아이템 스폰 스냅 부재 | 4 |
| P2-3 | 진행도 float 연속 동기화 (요리 4종) | 4 |
| P2-4 | 전송 계층 매 메시지 Debug.Log | 5(정리) |
| P2-5 | Door 상태 NetworkVariable 미사용 | 3 |
| P2-6 | ConnectionApproval·재접속·호스트 이탈 부재 | 3 |
| P2-7 | desync 감지·계측 도구 부재 | 5 |
| P2-8 | DontDestroyOnLoad NetworkBehaviour 싱글톤 다수 | 5 |
| P2-9 | 잡기 시각 상태 ClientRpc 전용 | 4 |
| P2-10 | 해제 불가 람다 OnValueChanged 구독 6건 | 3 |
| P2-11 | QTE 동기화 '주석만 존재' | 5 |
| P2-12 | NPC 발화 동기화 미구현 | 5 |

### 0.3 공통 검증 환경 (전 단계 적용)

- **2인 실기기 + 강제 지연** 환경을 기본 검증 베드로 한다. 호스트 단독 테스트는 P0-5/P1-10/P1-12 류 결함을 영원히 드러내지 못함.
- 네트워크 시뮬레이션: NGO `NetworkSimulator` 또는 Clumsy 등으로 **RTT 150~250ms / 패킷 손실 2~5% / 지터** 주입.
- **30분 soak**(무중단 플레이)을 단계 완료 판정의 공통 게이트로 둔다.
- 검증 로그는 `Reports\` 하위에 단계별 결과 md로 누적(기존 `Day4_ChecklistReport.md` 형식 준용).

### 0.4 범례

- `[ ]` 미완료 · `[x]` 완료
- **(선행)** = 동일/이전 단계 내 다른 작업의 완료가 전제
- 파일 경로는 점검 보고서가 지목한 클래스명 기준(실제 경로는 착수 시 grep 확정)

---

## Step 1 · 전송 안정화

> **목표**: 전송 계층(L1)의 P0 3건 + P1 2건을 제거해 상위 모든 동기화의 신뢰 바닥을 확보한다. 수정 규모는 작으나(합계 수십 줄) 효과는 전 계층. **이 단계 없이는 이후 어떤 테스트 결과도 신뢰 불가.**
> **포함**: P0-1, P0-2, P0-4, P1-2, P1-1

### 1.A 상세 진행계획

#### 1.A.1 — P0-1 수신 버퍼 동적화
- **대상**: `SteamP2PRelayTransport` (Client/Server Callbacks의 수신 경로)
- **현행**: `byte[1024]` 고정 버퍼 + 서버 측 크기 검사 TODO. 씬 이벤트·NetworkList 초기 스냅샷 등 대형/단편 메시지가 `Marshal.Copy` 단계에서 손상.
- **수정 방향**:
  1. 수신 메시지의 실제 `size`만큼 정확히 할당하여 NGO에 전달(고정 버퍼 재사용 폐기 → 경합도 동시 제거).
  2. 서버 측 크기 검사 TODO 제거하고 클라이언트 경로와 동일 로직으로 통일.
  3. (선택) `ArrayPool<byte>` 도입으로 GC 완화 — 단 단순 정확 할당이 1순위.
- **의존성**: 없음(최우선 착수)

#### 1.A.2 — P0-2 Disconnect 오발화 수정
- **대상**: `SteamP2PRelayTransport.ServerCallbacks.OnDisconnected`
- **현행**: 이탈 시 `NetworkEvent.Connect`를 발화 → 이탈을 신규 접속으로 오인 → 유령 클라, ready맵 제거·로비 복귀·세션 카운트 전부 미동작.
- **수정 방향**: `NetworkEvent.Connect` → `NetworkEvent.Disconnect` (1단어). 단, 이 한 줄이 끊김 정리 전 경로의 생사를 쥐므로 **수정 후 끊김 정리 체인 전체를 재검증**.
- **의존성**: 없음

#### 1.A.3 — P0-4 RTT 보고 구현
- **대상**: `SteamP2PRelayTransport.GetCurrentRtt`
- **현행**: 항상 0 반환 → NGO 시간 동기화·버퍼링 품질 저하, 지연 보상 기반 부재.
- **수정 방향**: Facepunch `Connection`의 ping/상태 조회 API(`ConnectionInfo`/`QuickConnectionStatus`의 ping)로 연결별 RTT(ms)를 반환. clientId→Connection 매핑 경유.
- **의존성**: 없음

#### 1.A.4 — P1-2 SendType 매핑 교정
- **대상**: `SteamP2PRelayTransport.CastToSendType`
- **현행**: `UnreliableSequenced`를 순서 비보장으로 매핑 → 순서 의존 메시지 역전 수신 가능.
- **수정 방향**: NGO `NetworkDelivery` ↔ Steam send flag 매핑표를 재정의. Sequenced 의미가 보존되도록(또는 해당 채널을 Reliable로 승격) 매핑하고, 매핑 정합성을 단위 표로 문서화.
- **의존성**: 없음

#### 1.A.5 — P1-1 RunCallbacks/Shutdown 단일화
- **대상**: `SteamClient` / `SteamLobbyManager` / `SteamP2PRelayTransport.Shutdown`
- **현행**: `RunCallbacks` ×2/프레임(콜백 이중 펌핑), 방 퇴장 시 Transport가 Steam API 전체 종료 → 재호스팅 불안정.
- **수정 방향**:
  1. `RunCallbacks` 펌핑과 Steam API 수명을 **SteamClient 한 곳으로 단일화**.
  2. Transport.Shutdown은 **소켓/릴레이 연결만** 정리, `SteamClient.Shutdown()` 호출 금지.
  3. 재호스팅 시나리오(방 나가기 → 재호스트)에서 Steam 초기화가 1회만 유지되는지 확인.
- **의존성**: 1.A.2(끊김 정리 정상화) 이후 통합 검증하면 효율적

### 1.B 검증계획

| 검증 | 방법 | 합격 기준 |
|---|---|---|
| P0-1 대형 메시지 | NetworkList 100~200항목 강제 초기 동기화 + 씬 전환 반복 | Assert/잘림 0건, 클라 접속 100% 성공 |
| P0-1 경계값 | 1023/1024/1025/4096B 메시지 송수신 | 전 크기 무손상 수신 |
| P0-2 끊김 정리 | 2인 접속 후 클라 강제 종료(Alt+F4) | 호스트 ready맵/세션 카운트 정상 감소, 유령 클라 0 |
| P0-2 복귀 | 클라 끊김 후 재접속 | 로비 복귀·재합류 정상 |
| P0-4 RTT | HUD/로그로 RTT 출력, Clumsy로 150ms 주입 | 측정값이 주입 지연 ±20% 내 추종(0 아님) |
| P1-2 순서 | Sequenced 채널로 연속 번호 메시지 100건 송신 | 수신 순서 역전 0건 |
| P1-1 재호스팅 | 방 생성→나가기→재생성 ×10 | 매회 정상 호스팅, Steam 재초기화 에러 0 |
| 통합 soak | 2인 실기기 + 강제 지연 30분 | 끊김·유령 클라·디싱크 0건 |

### 1.C 체크리스트

**구현**
- [ ] P0-1: 수신 버퍼를 메시지 size 기준 정확 할당으로 교체 (Client 경로)
- [ ] P0-1: 서버 측 크기 검사 TODO 제거 + 클라 경로와 통일
- [ ] P0-1: 고정 버퍼 재사용 경합 제거 확인
- [ ] P0-2: `OnDisconnected` 내 `Connect` → `Disconnect` 수정
- [ ] P0-2: 끊김 정리 체인(ready맵·로비 복귀·세션 카운트) 연결 재확인
- [ ] P0-4: `GetCurrentRtt`를 Connection ping 조회로 구현
- [ ] P0-4: clientId → Connection 매핑 경유 확인
- [ ] P1-2: `CastToSendType` 매핑표 재정의 (Sequenced 의미 보존)
- [ ] P1-1: RunCallbacks 펌핑을 SteamClient 단일 지점으로 이전
- [ ] P1-1: Transport.Shutdown에서 Steam API 전체 종료 호출 제거

**검증**
- [ ] 대형 메시지(NetworkList 100+) 초기 동기화 무손상
- [ ] 메시지 크기 경계값(1024 전후) 테스트 통과
- [ ] 클라 강제 종료 시 유령 클라 0 / 세션 카운트 정상
- [ ] RTT 측정값이 주입 지연을 추종
- [ ] Sequenced 채널 순서 역전 0건
- [ ] 재호스팅 ×10 무에러
- [ ] **2인 실기기 30분 soak: 끊김·유령·디싱크 0건** (데모 게이트 1차)

---

## Step 2 · 권위 일원화

> **목표**: 게임플레이 권위 경계를 일원화한다. 데미지는 피격자 Owner 단일 판정점으로, 아이템 획득은 서버 라우팅 + 권한 방향 확정으로, 사망/인벤/슬라이싱의 권위 게이트를 정렬한다. 기획 P3(desync) 폴리시 직격 항목.
> **포함**: P0-3, **P0-5+P1-10(한 묶음)**, P1-3, P1-4, P1-9, P1-11

### 2.A 상세 진행계획

#### 2.A.1 — P0-3 + P1-9 데미지 파이프라인 권위 일원화 (보고서 Fig 3)
- **대상**: `MeleeWeaponDamageCollider` → 데미지 RPC 체인, `CharacterDefenseManager`
- **현행**: 공격자 클라가 히트 감지 + 피격자 방어/패링 심사까지 수행(보간된 과거 위치 기준), 서버는 무검증 중계, 수치 차감만 피격자 Owner — **3분할 권위**.
- **수정 방향 (제안안 B)**:
  1. 공격자: 히트 **감지(후보 보고)만** 수행 → `HitCandidateRpc(SendTo.Server)`.
  2. 서버: 후보를 피격자 Owner에게 **단일 대상**(RpcParams)으로 전달.
  3. 피격자 Owner: **자기 화면 기준**으로 방어/패링/무적 최종 심사 + Owner-Write HP·포이즈·스태미나 차감(권위 1곳). → P1-9(권한 위반 경로)도 동시 소멸.
  4. 연출: 판정 결과+페이로드 보고 → 서버가 **연출 전용 브로드캐스트**(VFX/SFX/히트스탑만, 수치 연산 없음).
- **의존성**: Step 1 완료(전송 신뢰)

#### 2.A.2 — P0-5 + P1-10 아이템 획득 결함 연쇄 (보고서 Fig 4, **한 묶음 필수**)
- **대상**: `PickUpItemInteractable.Interact`, `PlayerInteractionManager`, `PlayerEquipmentManager`/`PlayerNetworkManager`의 `currentBackpackID`
- **현행**:
  - P0-5: `Interact`가 `if(!IsServer) return;`으로 시작 → 클라 줍기 무반응(라우팅 RPC 부재). Door는 라우팅 있으나 PickUp은 없음.
  - P1-10: 서버 경로가 Owner-Write 변수 `currentBackpackID` 2종을 직접 기록 → 호스트 자신 픽업 시에만 우연히 동작. P0-5를 라우팅으로 고치는 순간 원격 대상에서 `InvalidOperationException`.
- **수정 방향 (반드시 동시)**:
  1. `Interact` → `RequestPickupServerRpc(playerNetId)` 라우팅 신설(**Door 패턴 재사용**).
  2. 서버는 검증 + 인벤토리(Server Write) 변경 + Despawn만 수행.
  3. 가방 장착 ID: **(a) Server Write로 권한 변경** 또는 **(b) 대상 Owner에게 단일 RPC로 위임** — 장비 전반의 권위 방향과 함께 결정(아래 결정 항목).
- **결정 필요 항목**: `currentBackpackID`를 포함한 장비 NetworkVariable의 권위 방향을 Owner-Write 유지(=Owner RPC 위임) vs Server-Write 전환 중 택1. 본 결정은 Step 3의 "지속 상태 권위 규약"과 정합되어야 함.
- **의존성**: Step 1 완료. P0-5만 단독 수정 금지(P1-10 즉시 표면화).

#### 2.A.3 — P1-3 사망 처리 권위 게이트
- **대상**: `CheckHP` → `ProcessDeathEvent`
- **현행**: `CheckHP`가 OnValueChanged로 전 클라 호출되는데 `ProcessDeathEvent` 코루틴 시작에 권위 게이트 없음 → 드롭·연출 중복.
- **수정 방향**: 권위 행위(수치·드롭)는 Owner(또는 서버)만, 연출은 `isDead` NetworkVariable 변경 구독으로 분리.
- **의존성**: 없음(Step 2 내 독립)

#### 2.A.4 — P1-4 인벤토리 서버측 검증
- **대상**: `CharacterInventoryManager` RPC 4종(Add/Move 등)
- **현행**: `IsSpaceAvailable` 검증이 클라 UI에만, 서버 RPC는 무검증 반영 → 겹침·복제·용량 초과. ID+좌표 식별로 오식별 여지.
- **수정 방향**:
  1. 서버 진입부 **재검증** + 거절 시 요청자 UI 롤백(Request/Ack 패턴).
  2. 아이템 식별을 **인스턴스 ID(ulong)** 도입으로 오식별 차단.
- **의존성**: 없음

#### 2.A.5 — P1-11 슬라이싱 파편 동기화
- **대상**: `SlicingDamageCollider.PerformSlice` + `MeshSlicer`
- **현행**: 절단 헐(파편)이 비네트워크 로컬 오브젝트, 동기화는 절단 횟수뿐 → 단면/파편 클라별 상이, 최대 절단 시 원본만 서버 Despawn되어 타 클라엔 소실.
- **수정 방향 (보고서 권장 ①+③)**:
  1. 절단을 '연출'로 규정 → 절단 입력(평면 위치·법선·시드)을 RPC 전파 → 각 클라가 동일 입력으로 **결정론 재절단**.
  2. 원본 Despawn 시 **파편 생성 이벤트를 함께 브로드캐스트**.
  3. (보조) 파편 수명 단축 + 게임플레이 영향 제거(순수 비주얼).
- **의존성**: Step 1 완료(입력 RPC 신뢰)

### 2.B 검증계획

| 검증 | 방법 | 합격 기준 |
|---|---|---|
| P0-3 패링 디싱크 | 2인, 한쪽 강제 지연 200ms 상태로 패링/회피 50회 | 양쪽 화면 판정 결과 100% 일치 |
| P0-3 권위 | 공격자 화면 조작으로 피격자 수치 변조 시도 | 피격자 Owner만 수치 변경, 변조 불가 |
| P0-5 클라 획득 | **클라이언트**가 아이템 줍기 | 정상 획득, 호스트와 동일 결과 |
| P1-10 원격 픽업 | 원격 플레이어 대상 가방 장착 | `InvalidOperationException` 0건 |
| P1-3 사망 | 2인, 동시 사망/동시 타격 | 드롭·연출 중복 0건 |
| P1-4 인벤 | 동시 Add/Move, 용량 초과 시도, 복제 시도 | 겹침·복제·초과 0건, 거절 시 UI 롤백 |
| P1-11 슬라이싱 | 2인, 한쪽이 객체 절단 후 양쪽 비교 | 단면/파편 위치 일치, 최대 절단 시 양쪽 동일 소실 |
| 통합 soak | 2인 실기기 + 지연 30분 전투/획득 반복 | 디싱크·예외·복제 0건 |

### 2.C 체크리스트

**구현**
- [ ] P0-3: 공격자를 히트 '감지만'으로 축소 (`HitCandidateRpc`)
- [ ] P0-3: 서버가 피격자 Owner에게 단일 대상 전달(RpcParams)
- [ ] P0-3: 피격자 Owner 단일 판정 + Owner-Write 수치 차감
- [ ] P0-3/P1-9: 연출 전용 브로드캐스트 분리(수치 연산 제거)
- [ ] **(한 묶음)** P0-5: `Interact` → `RequestPickupServerRpc` 라우팅 신설(Door 패턴)
- [ ] **(한 묶음)** P1-10: 서버의 Owner-Write `currentBackpackID` 직접 기록 제거
- [ ] **(결정)** 가방/장비 권위 방향 확정 (Server-Write vs Owner RPC 위임)
- [ ] P1-3: `ProcessDeathEvent` 권위 게이트 추가, 연출=`isDead` 구독 분리
- [ ] P1-4: 서버 RPC 진입부 재검증 + Request/Ack 롤백
- [ ] P1-4: 아이템 인스턴스 ID(ulong) 도입
- [ ] P1-11: 절단 입력(평면·법선·시드) RPC 전파 + 클라 결정론 재절단
- [ ] P1-11: 원본 Despawn 시 파편 생성 이벤트 브로드캐스트

**검증**
- [ ] 패링/회피 양쪽 화면 판정 일치(지연 환경)
- [ ] 클라이언트 아이템 획득 정상
- [ ] 원격 픽업 시 쓰기 권한 예외 0건
- [ ] 동시 사망 드롭/연출 중복 0건
- [ ] 인벤 복제·겹침·초과 0건 + UI 롤백 동작
- [ ] 슬라이싱 단면/파편 양쪽 일치
- [ ] **2인 실기기 30분 전투 soak: 디싱크·예외 0건** (데모 게이트 2차)

---

## Step 3 · 규약 표준화

> **목표**: RPC 문법을 단일화하고, 지속 상태의 동기화 규약(NetworkVariable/List 경유)과 코옵 스폰 정책을 확정한다. 난입(Late-join) 대응의 기반을 마련하고, 누수성 람다 구독을 정리한다.
> **포함**: P1-7, P1-12, P2-5, P2-6, P2-10, (P1-8)

### 3.A 상세 진행계획

#### 3.A.1 — P1-7 RPC 단일 문법 + SenderClientId
- **대상**: 전역 + `TerrainSync`
- **현행**: `[ServerRpc]` / `RequireOwnership` / `InvokePermission` 3종 혼재 + clientId 수동 전달 → NGO 버전업 일괄 파손, Sender 위·변조/실수 여지.
- **수정 방향**:
  1. NGO 2.x `[Rpc(SendTo.Server, InvokePermission=...)]` **단일 문법으로 통일**.
  2. clientId는 인자 전달 금지 → `RpcParams.Receive.SenderClientId` 사용.
  3. 마이그레이션 대상 RPC 전수 목록화 후 일괄 변환.
- **의존성**: 없음(전역 작업이므로 Step 2 권위 변경 확정 후 착수가 충돌 적음)

#### 3.A.2 — P1-12 코옵 스폰 정책 확정 (SyncedSpawnPosition 부활)
- **대상**: `PlayerManager.OnNetworkSpawn`, `TerrainSync.SyncedSpawnPosition`
- **현행**: 클라가 **자기 로컬 세이브 좌표·씬 인덱스**로 transform 직접 설정 → 호스트 월드(다른 시드)와 무관해 지형 밖 스폰 위험. `SyncedSpawnPosition`은 선언만 되고 읽기/쓰기 0건(데드코드).
- **수정 방향**:
  1. 현행 로컬 좌표 이동 로직을 **싱글 이어하기 전용으로 격리**.
  2. 코옵은 **서버가 결정한 스폰 지점**으로 일원화 → `SyncedSpawnPosition`을 실제 기록·소비.
  3. 규약 명문화: **스탯(vitality 등)=Owner Write 유효**, **좌표·씬=월드(호스트) 권위**.
- **의존성**: 없음. EnvFlagRegistry 난입 복원의 전제이기도 함.

#### 3.A.3 — P2-5 Door 상태 NetworkVariable화
- **대상**: `DoorInteractable`
- **현행**: 상태가 ClientRpc 전용 → 난입 유저에 문 상태 미전달. (코드 주석 스스로 "NetworkVariable 권장" 기재)
- **수정 방향**: 문 열림/닫힘은 **가역 상태**이므로 EnvFlag 아닌 개별 `NetworkVariable<bool>`로 승격(EnvFlagRegistry 설계서 §2 경계 정의 준수). 난입 시 자동 초기 동기화.
- **의존성**: 없음. P0-5 라우팅 패턴과 동형이므로 함께 검토 효율적.

#### 3.A.4 — P2-6 ConnectionApproval·재접속·호스트 이탈 정책
- **대상**: 세션 계층
- **현행**: 정원·버전 검증 불가, 끊김 시 진행 손실.
- **수정 방향**:
  1. `ConnectionApprovalCallback` 구현: 정원·버전·비밀번호(선택) 검증.
  2. 재접속/난입 시 상태 스냅샷 수신 경로 정의(NetworkVariable/List 경유로 자동화).
  3. 호스트 이탈 정책 명시(세션 종료 vs 마이그레이션 — 친선 코옵 기준 세션 종료 + 세이브로 단순화 권장).
- **의존성**: Step 1(끊김 처리 정상화) 완료

#### 3.A.5 — P2-10 해제 불가 람다 구독 6건 정리
- **대상**: `PickUpItemInteractable`, `PlayerEquipmentManager`×3, `LobbyUIManager`×2
- **현행**: 람다 OnValueChanged 구독은 `-=` 해제 불가 → Despawn/재스폰·풀링 시 중복 호출·누수.
- **수정 방향**: 6건 전부 **메서드 참조**로 변경, `OnNetworkDespawn`(또는 OnDisable)에서 `-=` 해제.
- **의존성**: 없음(기계적 작업, 풀링 도입 전 필수)

#### 3.A.6 — (P1-8) 지형 Ready 보고 경로 일원화
- **대상**: `LobbyUIManager` / `TerrainSync`
- **현행**: 준비 카운트 이중 관리, `allReady` 데드 코드.
- **수정 방향**: Ready 보고를 단일 경로(TerrainSync 권위)로 통합, allReady 데드코드 제거 또는 활성화. (양호 확인된 LobbyUIManager ready interlock 패턴 보존)
- **의존성**: P1-12 스폰 정책과 함께 검토(난입 Ready 흐름 공유)

### 3.B 검증계획

| 검증 | 방법 | 합격 기준 |
|---|---|---|
| P1-7 문법 | 전체 빌드 + RPC 전수 호출 스모크 | 컴파일 통과, 혼용 잔존 0건(grep) |
| P1-7 Sender | 위조 clientId 인자 제거 확인 | 모든 RPC가 RpcParams Sender 사용 |
| P1-12 스폰 | 코옵 클라 신규 접속 + 난입 | 호스트 월드 좌표에 정상 스폰, 지형 밖 0건 |
| P1-12 싱글 | 싱글 이어하기 | 로컬 세이브 좌표 정상 복원(회귀 없음) |
| P2-5 Door | 문 조작 후 제3자 난입 | 난입 유저 화면 문 상태 일치 |
| P2-6 Approval | 정원 초과/버전 불일치 접속 시도 | 거절 정상 동작 |
| P2-10 람다 | Despawn→재스폰 반복 후 콜백 횟수 계측 | 중복 호출 0, 누수 0 |
| P1-8 Ready | 2인+난입 Ready 카운트 추적 | 카운트 단일 관리, 데드코드 미경유 |

### 3.C 체크리스트

**구현**
- [ ] P1-7: RPC 전수 목록화 후 `[Rpc]` 단일 문법 변환
- [ ] P1-7: clientId 수동 전달 제거 → `RpcParams.Receive.SenderClientId`
- [ ] P1-12: 현행 로컬 좌표 이동을 싱글 이어하기 전용으로 격리
- [ ] P1-12: `SyncedSpawnPosition` 실제 기록·소비 구현
- [ ] P1-12: 좌표·씬=호스트 권위 / 스탯=Owner 규약 문서화
- [ ] P2-5: Door 상태 `NetworkVariable<bool>` 승격
- [ ] P2-6: `ConnectionApprovalCallback`(정원·버전) 구현
- [ ] P2-6: 호스트 이탈 정책 결정 및 구현
- [ ] P2-10: 람다 구독 6건 → 메서드 참조 + Despawn 해제
- [ ] P1-8: Ready 보고 경로 단일화 + allReady 데드코드 정리

**검증**
- [ ] RPC 문법 혼용 잔존 0건 + 빌드 통과
- [ ] 코옵 스폰이 호스트 월드 좌표에 정상(지형 밖 0)
- [ ] 싱글 이어하기 회귀 없음
- [ ] 난입 유저 문 상태 일치
- [ ] 정원 초과/버전 불일치 거절 동작
- [ ] 람다 정리 후 재스폰 중복 호출 0
- [ ] Ready 카운트 단일 관리 확인

---

## Step 4 · 효율화

> **목표**: 대역폭·표현 품질을 최적화한다. 기획 hold-out(다수 AI 동시 활성) 구간 대비 AI 송신 게이팅·양자화·거리 차등을 도입하고, 진행형 값을 '상태+시작시각' 패턴으로 전환한다.
> **포함**: P2-1, P2-2, P2-3, P2-9, (P1-5, P1-6)

> **대역폭 추산(보고서)**: 캐릭터 1기당 틱당 60~80B, 30Hz ≈ 2KB/s. 4인+AI 20기 → 호스트 업로드 ~200KB/s. hold-out 구간 여유 부족.

### 4.A 상세 진행계획

#### 4.A.1 — P2-1 + AI 백본 다이어트
- **대상**: `CharacterNetworkManager.Update`
- **현행**: 위치 매 프레임 풀정밀 동기화, 압축·게이팅 없음.
- **수정 방향 (보고서 권장 순서)**:
  1. **AI 위치 송신 게이팅**: 정지·비전투 시 중단(`OptimizedNetworkItem` 패턴 이식).
  2. **블렌드 값 0.05 양자화** + 변경 시에만 송신.
  3. **거리 기반 갱신 주기 차등**.
  4. **AI 전용 경량 백본** 분리(위치+상태Enum+어그로만) — 현재 AI가 플레이어와 동일 풀 백본 상속.
- **의존성**: Step 1 완료. 대규모이므로 ①→④ 순차 적용.

#### 4.A.2 — P2-3 진행형 값 → '상태+시작시각' 패턴
- **대상**: `CookingStation` / `GrillCookingStation`(bottom·top·burn·cooking 4종)
- **현행**: 4개 NetworkVariable이 매 틱 델타.
- **수정 방향**: **{state Enum, startServerTime(double)}** 만 동기화 → 각 클라가 결정 함수로 진행도 재계산. 트래픽 0 수렴. (EnvFlagRegistry 시계형 플래그의 예행연습)
- **의존성**: 없음. EnvFlagRegistry 착수 전 검증용으로 우선 권장.

#### 4.A.3 — P2-2 아이템 스폰 스냅 + 보간 개선
- **대상**: `OptimizedNetworkItem`
- **현행**: 난입 시 원점에서 미끄러짐, 제자리 회전 미동기, 프레임률 의존 Lerp.
- **수정 방향**: 스폰 시 위치 스냅, 회전 동기화 추가, Lerp를 `Time.deltaTime` 기반 프레임률 독립으로.
- **의존성**: 없음

#### 4.A.4 — P2-9 잡기 시각 상태 NetworkVariable화
- **대상**: `InteractableItem.AttachToHandClientRpc`
- **현행**: `isHeld`는 NetworkVariable이나 손 부착 대상은 RPC 전용 → 난입 유저 화면에서 '손에 없는' 아이템.
- **수정 방향**: 손 부착 대상을 `NetworkVariable<ulong>`(잡은 캐릭터 ID)로 승격 → 난입 문제 동시 해결.
- **의존성**: 없음

#### 4.A.5 — (P1-5) WeaponItem SO 누수
- **대상**: `PlayerNetworkManager.OnCurrent*WeaponIDChange`
- **현행**: OnValueChanged마다 Instantiate(WeaponItem) 후 이전 인스턴스 미파괴 → 메모리 증가.
- **수정 방향**: DB 원본 참조 + 런타임 스탯 분리(근본 해법). 임시로는 교체 시 이전 인스턴스 Destroy.
- **의존성**: 없음

#### 4.A.6 — (P1-6) OnClientConnectedCallback 중복 등록
- **대상**: `PlayerManager`
- **현행**: 접속 1회당 모든 플레이어 객체가 목록 재추가 호출.
- **수정 방향**: 콜백 등록을 1회로 보장(중복 += 제거), 등록/해제 짝 정리.
- **의존성**: P2-10 람다 정리와 함께 검토

### 4.B 검증계획

| 검증 | 방법 | 합격 기준 |
|---|---|---|
| P2-1 대역폭 | AI 20기 hold-out 구간 패킷 캡처(전/후) | 호스트 업로드 유의미 감소(목표 -40%↑) |
| P2-1 게이팅 | 정지 AI 다수 배치 후 송신량 측정 | 정지 시 위치 송신 ~0 |
| P2-1 품질 | 양자화 후 이동/애니 시각 검수 | 끊김·튐 체감 없음 |
| P2-3 요리 | 요리 진행 중 난입 | 진행도 호스트와 ±0.1초 일치, 진행도 트래픽 0 |
| P2-2 아이템 | 아이템 스폰 직후/난입 시 위치·회전 | 미끄러짐·회전 미동기 0 |
| P2-9 잡기 | 잡은 상태에서 제3자 난입 | 난입 화면 손에 부착 정상 |
| P1-5 메모리 | 장비 교체 1000회 후 메모리 프로파일 | SO 인스턴스 누적 없음 |
| P1-6 중복 | 접속/재접속 시 목록 추가 횟수 | 1회당 1추가 |

### 4.C 체크리스트

**구현**
- [ ] P2-1: AI 위치 송신 게이팅(정지·비전투 중단)
- [ ] P2-1: 블렌드 값 0.05 양자화 + 변경 시 송신
- [ ] P2-1: 거리 기반 갱신 주기 차등
- [ ] P2-1: AI 전용 경량 백본 분리
- [ ] P2-3: 요리 4종 → {state, startServerTime} 패턴 전환
- [ ] P2-2: 아이템 스폰 위치 스냅 + 회전 동기 + 프레임률 독립 Lerp
- [ ] P2-9: 손 부착 대상 `NetworkVariable<ulong>` 승격
- [ ] P1-5: WeaponItem DB 원본 참조 + 런타임 스탯 분리
- [ ] P1-6: OnClientConnectedCallback 중복 등록 제거

**검증**
- [ ] hold-out 대역폭 목표 감소 달성
- [ ] 정지 AI 송신 ~0
- [ ] 양자화 후 시각 품질 유지
- [ ] 요리 진행도 난입 일치 + 트래픽 0
- [ ] 아이템 스폰/난입 위치·회전 정상
- [ ] 잡기 난입 부착 정상
- [ ] 장비 교체 메모리 누수 0
- [ ] 접속당 목록 1회 추가

---

## Step 5 · 계측·검증

> **목표**: desync 감지·계측 체계를 구축하고(기획 CL-6 'desync' 경보), 미구현 동기화(QTE·NPC 발화)를 마무리한다. 자동화 테스트로 끊김/난입을 상시 회귀 검증한다.
> **포함**: P2-7, P2-11, P2-12, P2-4, P2-8

### 5.A 상세 진행계획

#### 5.A.1 — P2-7 desync 감지·계측 + 네트워크 HUD
- **대상**: 전역
- **현행**: desync 감지 수단 전무. 기획 CL-6 'desync' 경보 대응 불가.
- **수정 방향**:
  1. **상태 체크섬**: 핵심 동기화 상태(지형 paramHash, 인벤/위치 등)의 주기 해시 교차 검증.
  2. **지형 해시 검증**: 클라 간 동일 지형 보장(시드 결정론 검증).
  3. **네트워크 HUD**: RTT(P0-4 활용)·송수신 대역폭·패킷손실·desync 경보 표시.
  4. 불일치 시 즉시 로그 + HUD 경보 (+ 선택 재동기화 트리거).
- **의존성**: P0-4(RTT) 완료. EnvFlagRegistry의 체크섬 소비자(④)와 직접 연결.

#### 5.A.2 — P2-11 QTE 동기화 구현
- **대상**: `CharacterQTEManager` / `PlayerQTEManager`
- **현행**: 주석은 "결과를 ServerRpc로 동기화"라 명시하나 실제 RPC 0건 → QTE 결과·연출 로컬 전용.
- **수정 방향**: QTE 결과를 서버 권위로 동기화(`[Rpc(SendTo.Server)]` → 결과 브로드캐스트). 권위 방향은 Step 2 데미지 패턴 준용(연출 분리).
- **의존성**: Step 2(권위 패턴), Step 3(RPC 문법)

#### 5.A.3 — P2-12 NPC 발화 동기화
- **대상**: `pB-4 SpeechDispatcher`
- **현행**: 서버 권한 게이트는 있으나 발화 전파 RPC 없음 → NPC 대사 호스트 화면 전용(Week7 예정 주석).
- **수정 방향**: 발화 이벤트를 서버 권위로 전파(ClientRpc 또는 NetworkVariable 큐). 양호 확인된 서버 권위 게이트 위에 전파 계층만 추가.
- **의존성**: Step 3(RPC 문법)

#### 5.A.4 — P2-4 전송 계층 로그 정리
- **대상**: Transport `OnMessage` 등
- **현행**: 패킷당 Debug.Log → GC·성능 저하.
- **수정 방향**: 패킷별 로그 제거 또는 조건부 컴파일(`#if NETCODE_DEBUG`)로 게이팅.
- **의존성**: Step 1 안정화 후

#### 5.A.5 — P2-8 DontDestroyOnLoad 싱글톤 정리
- **대상**: `WorldItemSpawner` 등 DontDestroyOnLoad NetworkBehaviour 싱글톤 다수
- **현행**: 씬 전환·재호스팅 시 중복/고아 위험.
- **수정 방향**: 싱글톤 생명주기 가드(중복 인스턴스 파괴), 씬 전환·재호스팅 시 정리 경로 명시.
- **의존성**: Step 1(재호스팅 안정화)

#### 5.A.6 — 자동화 테스트 베드
- **수정 방향**:
  1. **강제 끊김/난입 자동 테스트**: 스크립트로 클라 연결/해제/난입 반복.
  2. **30분 soak 자동화**: 무인 장시간 플레이 + 체크섬 모니터.
  3. CI 또는 야간 배치로 회귀 상시화.
- **의존성**: 5.A.1(체크섬) 완료

### 5.B 검증계획

| 검증 | 방법 | 합격 기준 |
|---|---|---|
| P2-7 체크섬 | 의도적 상태 불일치 주입 | desync 감지 + HUD 경보 발생 |
| P2-7 HUD | 지연·손실 주입 환경 플레이 | RTT·대역폭·손실 실시간 표시 정확 |
| P2-7 지형 해시 | 2인 지형 생성 후 해시 비교 | 일치, 불일치 시 즉시 경보 |
| P2-11 QTE | 2인 QTE 수행 | 결과·연출 양쪽 일치 |
| P2-12 발화 | NPC 발화 + 제3자 관전/난입 | 전 클라 대사 표시 |
| P2-4 로그 | 빌드 프로파일 | 패킷당 로그 0, GC 스파이크 감소 |
| P2-8 싱글톤 | 씬 전환·재호스팅 ×10 | 중복/고아 인스턴스 0 |
| 자동화 | 강제 끊김/난입 100회 자동 | 크래시·디싱크 0, 리포트 자동 생성 |
| 최종 soak | 2인 실기기 + 지연 30~60분 | 무중단·무 desync, 체크섬 상시 일치 |

### 5.C 체크리스트

**구현**
- [ ] P2-7: 핵심 상태 주기 체크섬 + 교차 검증
- [ ] P2-7: 지형 paramHash 해시 검증
- [ ] P2-7: 네트워크 HUD(RTT·대역폭·손실·desync 경보)
- [ ] P2-7: 불일치 시 로그+HUD 경보(+선택 재동기화)
- [ ] P2-11: QTE 결과 서버 권위 동기화 + 연출 분리
- [ ] P2-12: NPC 발화 전파 계층 구현
- [ ] P2-4: 전송 계층 패킷별 로그 제거/조건부 컴파일
- [ ] P2-8: 싱글톤 생명주기 가드 + 씬/재호스팅 정리
- [ ] 강제 끊김/난입 자동 테스트 작성
- [ ] 30분 soak 자동화 + 체크섬 모니터

**검증**
- [ ] 불일치 주입 시 desync 감지·경보 동작
- [ ] HUD 지표 정확
- [ ] 지형 해시 교차 검증 동작
- [ ] QTE 결과 양쪽 일치
- [ ] NPC 발화 전 클라 표시
- [ ] 패킷 로그 0 + GC 감소
- [ ] 씬/재호스팅 ×10 고아 0
- [ ] 자동 끊김/난입 100회 무크래시
- [ ] **최종 2인 실기기 30~60분 soak: 무 desync** (EA 게이트)

---

## 6. 후행 과제 — EnvFlagRegistry 진입 게이트

EnvFlagRegistry(환경 상태 동기화 척추)는 본 로드맵과 별개 설계서로 다루며, **착수 조건은 Step 1(전송 안정화) 완료**다. 설계서 §6의 명시 전제:

- **선행 필수**: P0-1(전송 버퍼) — 플래그 200건이면 난입 초기 스냅샷이 3KB+. 버퍼 수정 없이는 난입이 곧 접속 실패.
- **선행 필수**: P0-2(Disconnect 오발화) — 원장 자체와 무관하나 테스트 신뢰성의 전제.
- **권장 시점**: 전송 안정화 직후 + 환경 mutation 콘텐츠(T2) 본격화 **전**. 콘텐츠가 먼저 들어오면 각 시스템이 자기만의 동기화를 만들어 통합 비용이 급증.

### 6.1 본 로드맵과의 연결 고리

| 본 로드맵 산출물 | EnvFlagRegistry에서의 활용 |
|---|---|
| Step 1 전송 버퍼/Disconnect | 난입 초기 스냅샷·테스트 신뢰성 전제 |
| Step 3 RPC 단일 문법 | `ProposeEnvEventServerRpc`가 RpcParams Sender 사용 |
| Step 3 람다 정리 규약 | `OnListChanged` 구독을 메서드 참조+Despawn 해제로 |
| Step 3 스폰 정책(SyncedSpawnPos) | 난입 복원 흐름과 동일 Ready Interlock 공유 |
| Step 4 요리 '상태+시작시각' | 시계형 플래그(침수·tide)의 예행연습 |
| Step 5 체크섬·지형 해시 | desync 소비자 ④(flagSetHash + terrainParamHash) |

### 6.2 EnvFlagRegistry 도입 차수(설계서 §7 요약)

| 차수 | 작업 | 완료 기준 |
|---|---|---|
| 0 | 선행: 본 로드맵 Step 1 | 2인 30분 soak 끊김·유령 0 |
| 1 | CookingStation '상태+시작시각' 예행연습 | 난입 진행도 ±0.1초 일치, 트래픽 0 |
| 2 | EnvFlag·Registry·체크섬 골격 + 가짜 소비자 | RaiseFlag 전 클라 수신, 난입 리플레이, 해시 일치 |
| 3 | 듀얼베이크 1호: 다리(BridgeBuilt) | 설치→즉시 표시→난입 유지→세이브/로드 유지 |
| 4 | 붕괴 3종 + NavMesh 토글 + AI 경로 정합 | 붕괴 후 AI 우회, 전 클라 경로 일치, 30분 무불일치 |
| 5 | 세이브 연동 + 시계형 침수 + 숨은 상태 Director | 로드=종료 월드(해시 동일), 침수 일치, 숨은 상태 패킷 부재 |

---

## 7. 전체 진행 체크리스트 (요약)

- [ ] **Step 1 · 전송 안정화** — P0-1, P0-2, P0-4, P1-2, P1-1 → 2인 30분 soak 통과
- [ ] **Step 2 · 권위 일원화** — P0-3, P0-5+P1-10(묶음), P1-3, P1-4, P1-9, P1-11 → 전투/획득 soak 통과
- [ ] **Step 3 · 규약 표준화** — P1-7, P1-12, P2-5, P2-6, P2-10, P1-8 → 난입/스폰 검증 통과
- [ ] **Step 4 · 효율화** — P2-1, P2-2, P2-3, P2-9, P1-5, P1-6 → 대역폭 목표 달성
- [ ] **Step 5 · 계측·검증** — P2-7, P2-11, P2-12, P2-4, P2-8 + 자동화 → 최종 soak 통과
- [ ] **데모 게이트**: Step 1~2 + Step 5 1차 가동 완료 (기획 P3 폴리시 충족)
- [ ] **EA 게이트**: Step 3~5 완료
- [ ] **후행**: EnvFlagRegistry 착수(Step 1 완료 후)

> **최우선 4가지(보고서 §7)**: ① Step 1 전송 4건 ② P0-5+P1-10 한 묶음 ③ 코옵 스폰 정책 확정 ④ EnvFlagRegistry 선제 구현.
>
> **검증 제1원칙**: Step 1 직후 **2인 실기기 + 강제 지연 30분 soak**를 첫 검증 활동으로. P0-5/P1-10/P1-12 류는 호스트 단독 테스트로는 영원히 보이지 않는다.
