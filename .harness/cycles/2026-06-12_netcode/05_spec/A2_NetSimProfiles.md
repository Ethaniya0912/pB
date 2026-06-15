# 05 · spec — A2 NetSimProfiles

> **이 문서는?** 새로 만들 스크립트 [[NetSimProfiles]] 의 설계도입니다. 아래 "쉬운 설명"만 읽어도 무엇을 왜 만드는지 알 수 있습니다.

## 메타
- A-ID: A2
- 경로: [`Assets/Scripts/Utilities/NetDiagnostics/NetSimProfiles.cs`](../../../../Assets/Scripts/Utilities/NetDiagnostics/NetSimProfiles.cs) ← 클릭=실제 파일
- 범주: Script
- 신규/변경: new

## 쉬운 설명 (비개발자용)
> 인터넷 상태를 "좋음/보통/나쁨"으로 흉내내는 스위치([[PROF-프리셋|PROF-G/A/B]])의 회로도이자 리모컨입니다. 개발자의 쾌적한 환경에서만 테스트하면 실제 유저의 나쁜 인터넷에서 터지는 문제를 못 보므로, 게임 중 **F8 키**로 일부러 느린 인터넷을 만들어 시험합니다. 패킷을 "잃어버리는" 흉내(손실)는 안전상 하지 않고(메시지가 영영 사라질 위험 — G3 결정), 그건 외부 도구 [[Clumsy]]로 보완합니다.

## 한눈에 — 의존·관계

```mermaid
flowchart LR
  NetSimProfiles["A2 NetSimProfiles"]:::mod --> Transport["A4 SteamP2PRelayTransport<br/>소비"]:::flow
  F8["F8 토글"]:::flow --> NetSimProfiles

  classDef mod  fill:#FBF0DD,stroke:#B5731A,color:#7c4a03;
  classDef flow fill:#EBF0FF,stroke:#2A52DB,color:#1e3a8a;
```

---
## 스크립트 (한 파일에 2형 + 컨트롤러)
- **네임스페이스**: `NetDiag`
- **책임** (1문장): PROF-G/A/B 네트워크 조건을 코드 프리셋으로 보유하고, 런타임 토글(F8)로 활성 프로파일을 바꿔 transport 수신 경로의 지연/지터 주입을 제어한다. **손실(loss)은 주입하지 않는다**(G3 결정).

### (1) `public readonly struct NetSimPreset`
- 필드: `string Name; int RttMs; int JitterMs;` (LossPct 없음 — 미주입)
- `public int OneWayDelayMs => RttMs / 2;`

### (2) `public static class NetSimProfiles`
- **공개 API**:
  - `static readonly NetSimPreset Off` = {"OFF", 0, 0} (passthrough)
  - `static readonly NetSimPreset G` = {"PROF-G", 30, 0}
  - `static readonly NetSimPreset A` = {"PROF-A", 150, 30}
  - `static readonly NetSimPreset B` = {"PROF-B", 250, 60}
  - `static NetSimPreset Active { get; private set; } = Off`
  - `static bool Enabled => Active.RttMs > 0 || Active.JitterMs > 0` (OFF면 false)
  - `static void Set(NetSimPreset p)` — Active 교체 + `NetDiag.NetDiagnostics.Event("NETSIM", $"profile={p.Name} rtt={p.RttMs} jitter={p.JitterMs}")` 기록(측정 파일 PROF-X 표기 근거)
  - `static NetSimPreset Cycle()` — Off→G→A→B→Off 순환 후 Set, 반환
  - `static float NextReleaseDelaySeconds(float lastReleaseAt, float now)` — 보조: 지연+지터를 **FIFO 단조 증가**로 계산(아래 정책)
- **지연/지터 정책 (reliable 안전)**:
  - one-way 지연 = `OneWayDelayMs/1000`초. 지터는 `[-JitterMs/2, +JitterMs/2]` 균일난수(`UnityEngine.Random`).
  - **재정렬 방지**: 각 패킷 release time이 직전 패킷 release time 이상이 되도록 클램프(`max(now+delay+jitter, lastRelease)`). → sequenced/reliable 메시지 순서 보존. (계측 목적상 지연 추종 확인이 핵심이고, 재정렬은 별도 위험이라 배제.)

### (3) `public class NetSimController : MonoBehaviour`
- **책임**: F8 키 입력으로 `NetSimProfiles.Cycle()` 호출 + `OnGUI`로 현재 프로파일 라벨(좌상단, RNSM과 겹치지 않게 우상단) 표시.
- **수명주기**: `Update()`에서 `Input.GetKeyDown(KeyCode.F8)` 감지. `OnGUI()` 라벨.
- **의존성**: `NetSimProfiles`, `NetDiag.NetDiagnostics`(Event 기록).

## 참조 관계
- A4 `SteamP2PRelayTransport`가 `NetSimProfiles.Enabled`/`Active`/`NextReleaseDelaySeconds`를 소비.
- A3 `NetDiagnosticsBootstrap`가 `NetSimController`를 부트스트랩 GO에 부착.

## 비고
- 손실 미주입 근거: transport OnMessage는 Steam reliable 계층 이후 → 손실 주입 시 영구 유실. PROF-A/B 손실률(2~5%)은 OS레벨 Clumsy로 보완(Step0_Baseline.md에 명시). 계획 §2도 Clumsy 대안 명시.
- 단일 에디터(loopback) RTT≈0에 PROF-A(150ms) 주입 시 RNSM RTT 칸이 ~150ms 추종 → A1·A2·P0-4의 통합 교차 검증.

## 산출물 사용 가이드
> 만든 뒤 어떻게 쓰는지. 코드를 몰라도 켜고·쓰고·주의할 점을 알 수 있게.
- **언제·왜 만들어졌나**: 2026-06-12_netcode 사이클(Step 0 계측), goal G2. 개발자의 좋은 인터넷에서만 테스트하면 못 잡는, 나쁜 망(지연·지터)에서의 문제를 재현하려고. PROF-G/A/B 프리셋이 미구성이던 잔여 갭.
- **Unity 적용법**: 씬 배치 불필요 — 런타임 자동생성. Bootstrap이 NetSimController를 `[NetDiagnostics]`에 AddComponent. (회귀 수정으로 NetSimController는 NetSimProfiles.cs에서 분리된 단독 파일 — 파일명=클래스명 규칙.)
- **사용법**: 플레이 중 **F8 키**로 OFF→PROF-G(30ms)→PROF-A(150ms+지터)→PROF-B(250ms+지터) 순환. 화면 우상단에 현재 프로파일 라벨 표시. SteamP2PRelayTransport 수신 경로에 지연/지터가 주입됨. 측정 파일명의 PROF-X 표기 근거로 NETSIM 이벤트가 events.csv에 기록됨.
- **주의점**: 손실(loss)은 주입 안 함(G3 결정 — Steam reliable 계층 뒤라 영구 유실 위험). PROF-A/B의 손실률은 외부 도구 Clumsy로 보완. 기본 OFF라 평소엔 기존 경로와 100% 동일.

---
## 🔗 관련 문서 (Foam)
- 상위 작업목록: [[2026-06-12_netcode/04_assets|04_assets]] (A2)
- 소비처: [[2026-06-12_netcode/05_spec/A3_A4_Transport_and_Bootstrap_mods|A3_A4_Transport_and_Bootstrap_mods]] (A4 수신 지연/지터)
- 관련 명세: [[2026-06-12_netcode/05_spec/A1_RnsmHud|A1_RnsmHud]]
- 검증·진행: [[2026-06-12_netcode/07_plan|07_plan]] · [[2026-06-12_netcode/08_result|08_result]]
- 용어: [[NetSimProfiles]] · [[PROF-프리셋]] · [[Clumsy]] · [[SteamP2PRelayTransport]] · [[RTT]] → [[_glossary|용어 사전]]
