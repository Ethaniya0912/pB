---
type: script
aliases: [RNSM HUD 컴포넌트]
---
# RnsmHud

**분류**: script · 계측 HUD (`NetDiag.RnsmHud : MonoBehaviour`) — 2026-06-12_netcode 사이클 신규(A1)

## 한 줄 정의
- [[RNSM]](RuntimeNetStatsMonitor)을 **런타임에 부착·구성**해 RTT/송수신 바이트 HUD를 화면에 띄우는 MonoBehaviour. 씬·프리팹을 편집하지 않고 부트스트랩 GO에만 부착(게임 로직 무침습).

## 쉬운 설명
> 계기판([[RNSM]])을 차에 고정하는 "거치대". 계기판 자체는 Unity가 만들어 둔 것이고, 이 스크립트는 게임이 켜질 때 그 계기판을 화면에 달고(부착) 어떤 숫자를 보여줄지(RTT·송신·수신) 설정하는 역할만 한다.

## 등장 사이클
- [[2026-06-12_netcode/04_assets|2026-06-12_netcode ④ assets]] — A1 신규 확정
- [[2026-06-12_netcode/05_spec/A1_RnsmHud|〃 ⑤ spec A1]] — 기술 명세(구성 3종: RTT/Sent/Recv)
- [[2026-06-12_netcode/08_result#달성 대비표|〃 ⑧ result]] — 플레이 스모크 부착·표시 확인

## 관련 용어
[[RNSM]] · [[NetSimProfiles]] · [[NetDiagnosticsBootstrap]] · [[Multiplayer-Tools]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/RnsmHud.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/RnsmHud.cs)
