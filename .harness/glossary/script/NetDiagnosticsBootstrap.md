---
type: script
aliases: [계측 부트스트랩, NetDiagnostics 부트스트랩]
---
# NetDiagnosticsBootstrap

**분류**: script · 계측 진입점 (`NetDiag.NetDiagnosticsBootstrap`, `RuntimeInitializeOnLoadMethod`)

## 한 줄 정의
- 게임이 시작될 때 자동으로 `[NetDiagnostics]` GameObject를 만들어 계측 컴포넌트들([[NetEventLogger]]·[[StateChecksumV0]]·[[BoundaryEchoHarness]]·[[SoakHarness]]·[[RnsmHud]]·[[NetSimProfiles|NetSimController]])을 부착하는 진입점. **씬·프리팹을 편집하지 않아도 계측이 항상 켜지는 이유**가 이 스크립트다.

## 쉬운 설명
> 공연장 문이 열리면 자동으로 무대 뒤에서 계측 장비를 설치하는 스태프. 무대(씬)를 바꿀 필요 없이 어느 공연(어느 씬으로 시작하든)에서나 장비가 준비된다. `#if !NETDIAG_DISABLED` 스위치로 통째로 끌 수도 있다.

## 등장 사이클
- [[2026-06-12_netcode/03_scope|2026-06-12_netcode ③ scope]] — 기구현 확인(기존 4종 부착)
- [[2026-06-12_netcode/04_assets|〃 ④ assets]] — A3 modify: RnsmHud·NetSimController 2종 **append**
- [[2026-06-12_netcode/06_test_env|〃 ⑥ test_env]] — "씬 편집 불필요" 환경 설계의 근거

## 관련 용어
[[NetEventLogger]] · [[RnsmHud]] · [[NetSimProfiles]] · [[SoakHarness]] · [[BoundaryEchoHarness]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/NetDiagnosticsBootstrap.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/NetDiagnosticsBootstrap.cs)
