---
type: script
aliases: [넷이벤트 로거]
---
# NetEventLogger

**분류**: script · 네트워크 계측 컴포넌트 (`NetDiag.NetEventLogger : MonoBehaviour`)

## 한 줄 정의
- 접속(Connect)/해제(Disconnect)/Transport 이벤트를 **타임스탬프와 함께 CSV**(events.csv)로 기록하는 계측 컴포넌트. 재접속 시간(M3)·이벤트 정합성(M8) 산출의 원천이며, P0-2(Disconnect 오발화) 같은 버그를 그대로 포착한다.

## 쉬운 설명
> 아파트 출입구의 CCTV 겸 방명록. "누가(클라이언트) 언제 들어오고(접속) 나갔는지(해제)"를 자동으로 장부에 적어둔다. 나중에 "이상하게 끊겼다"는 신고가 들어오면 이 장부를 펴서 그 시각에 무슨 일이 있었는지 확인한다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T6 "NetEventLogger 작성" (기존 파일 존재 의심 → 모호도 높음)
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — **기구현 확정**(NGO/Transport 전 이벤트 구독·재호스팅 추적 완비) → 변경 없이 검증만
- [[2026-06-12_netcode/08_result#달성 대비표|〃 ⑧ result]] — 부착·존재 확인 완료

## 관련 용어
[[VerdictLogger]] · [[StateChecksumV0]] · [[NetDiagnosticsBootstrap]] · [[M-지표]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/NetEventLogger.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/NetEventLogger.cs)
