---
type: script
aliases: [F8 토글 컨트롤러]
---
# NetSimController

**분류**: script · 네트워크 시뮬 토글 (`NetDiag.NetSimController : MonoBehaviour`)

## 한 줄 정의
- 게임 중 **F8 키**로 [[PROF-프리셋]]을 순환시키고(OFF→G→A→B) 현재 프로파일을 화면 라벨로 표시하는 컨트롤러. [[NetSimProfiles]]의 상태를 조작하는 리모컨 역할.

## 쉬운 설명
> 인터넷 상태 흉내 스위치의 "버튼" 부분. 원래 [[NetSimProfiles]] 파일 안에 같이 들어 있었는데, Unity 는 **파일명과 클래스명이 같아야** 컴포넌트를 스크립트 파일에 제대로 연결(바인딩)하기 때문에 도메인 리로드 때 "주인 잃은 컴포넌트"(missing script)가 되는 회귀가 발생 → 단독 파일로 분리했다(2026-06-12 23:05 수정).

## 등장 사이클
- [[2026-06-12_netcode/04_assets|2026-06-12_netcode ④ assets]] — A2 의 일부로 설계(당시 NetSimProfiles.cs 내 정의)
- [[2026-06-12_netcode/08_result|〃 ⑧ result]] — 잔여 이슈 0번: 초기화 윈도우 파괴 회귀 발견 → 원인(파일명≠클래스명) 규명 후 단독 파일 분리로 해결([retrofit_smoke_20260612.md](../../cycles/2026-06-12_netcode/evidence/retrofit_smoke_20260612.md) §해결)

## 관련 용어
[[NetSimProfiles]] · [[PROF-프리셋]] · [[NetDiagnosticsBootstrap]] · [[SteamP2PRelayTransport]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/NetSimController.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/NetSimController.cs)
