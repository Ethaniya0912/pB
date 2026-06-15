---
type: script
aliases: [경계 에코 하네스, F9 스윕]
---
# BoundaryEchoHarness

**분류**: script · 페이로드 경계 검증 (`NetDiag.BoundaryEchoHarness : MonoBehaviour`, F9)

## 한 줄 정의
- **F9 키**로 512B~64KB 크기의 페이로드(데이터 덩어리)를 에코(보냈다가 그대로 돌려받기) 왕복시켜, 수신 버퍼 경계에서 데이터가 깨지지 않는지([[M-지표|M2]]) 스윕 검증하고 `echo.csv`에 기록하는 하네스.

## 쉬운 설명
> 우체국에 점점 더 큰 소포를 보내보면서 "몇 kg부터 반송되거나 내용물이 깨지는지" 확인하는 실험. P0-1(수신 버퍼) 수정이 제대로 됐는지를 1023/1024/1025바이트 같은 "경계값"에서 집중적으로 시험한다.

## 등장 사이클
- [[2026-06-12_netcode/03_scope|2026-06-12_netcode ③ scope]] — **기구현 확정**(512~64KB 스윕·echo.csv) → 검증만
- [[2026-06-12_netcode/07_plan|〃 ⑦ plan]] — F9 실행은 수동 측정 인계 항목

## 관련 용어
[[SoakHarness]] · [[NetDiagnosticsBootstrap]] · [[M-지표]] · [[P0-P1-P2-이슈코드]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/BoundaryEchoHarness.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/BoundaryEchoHarness.cs)
