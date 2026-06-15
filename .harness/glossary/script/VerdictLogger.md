---
type: script
aliases: [판정 로거]
---
# VerdictLogger

**분류**: script · 전투 계측 (`NetDiag.VerdictLogger`, static)

## 한 줄 정의
- 전투 판정 이벤트(히트·패링·블록·데미지)를 **양측 머신 각각** `{serverTime, attacker, victim, verdict}` 형식 CSV로 기록하는 계측기. 두 기록을 diff(비교)하면 판정 일치율([[M-지표|M5]])이 나온다.

## 쉬운 설명
> 권투 경기에서 양쪽 코너에 심판을 한 명씩 두고 따로 채점시키는 것. 경기가 끝나고 두 채점표를 맞춰보면 "두 심판(호스트/클라이언트)이 같은 장면을 같은 판정으로 봤는지"를 알 수 있다. 채점표가 어긋나면 네트워크 동기화에 문제가 있다는 뜻.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T7 "VerdictLogger 작성 (M5)"
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — **기구현 확정**: 전투 3파일(TakeDamageEffect·MeleeWeaponDamageCollider·CharacterNetworkManager)에 호출부 이식 확인

## 관련 용어
[[NetEventLogger]] · [[M-지표]] · [[desync]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/VerdictLogger.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/VerdictLogger.cs)
