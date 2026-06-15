---
type: script
aliases: [소크 하네스]
---
# SoakHarness

**분류**: script · 장시간 테스트 수집기 (`NetDiag.SoakHarness : MonoBehaviour`, F10)

## 한 줄 정의
- **F10 키**로 시작하는 [[soak-테스트]] 수집기 — 30분 동안 10초 간격으로 상태 샘플을 수집하고, 종료 시 `soak_summary.md` 요약 보고서를 자동 생성한다.

## 쉬운 설명
> 장시간 시험의 "시험 감독관". 사람이 30분 동안 지켜보며 기록하는 대신, F10을 누르면 알아서 10초마다 상태를 받아 적고 끝나면 성적표(요약 md)를 출력해 준다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T10 "soak 하네스 v0"
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — **기구현 확정** → 검증만
- [[2026-06-12_netcode/07_plan|〃 ⑦ plan]] — 30분 실행은 수동 측정 인계 항목

## 관련 용어
[[soak-테스트]] · [[NetDiagnosticsBootstrap]] · [[BoundaryEchoHarness]] · [[SCN-시나리오]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/SoakHarness.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/SoakHarness.cs)
