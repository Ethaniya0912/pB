---
type: script
aliases: [StateChecksum, 상태 체크섬]
---
# StateChecksumV0

**분류**: script · desync 감지 (`NetDiag.StateChecksumV0 : MonoBehaviour`)

## 한 줄 정의
- 지형 시드와 인벤토리 목록을 해시(FNV-1a)로 요약해 **30초마다 8바이트 RPC로 서버와 비교**하고, 불일치([[desync]]) 시 LogError를 남기는 감지 골격. [[M-지표|M11]](체크섬 검출력)의 v0 구현.

## 쉬운 설명
> 두 사람이 가진 서류 뭉치가 같은지 전부 한 장씩 비교하는 대신, 각자 서류 전체를 압축한 "요약 도장"(체크섬)만 찍어서 도장끼리 맞춰보는 방식. 도장이 다르면 어딘가 서류가 어긋났다는 뜻이다. 8바이트라는 아주 작은 데이터만 오가므로 게임에 부담이 없다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T8 "StateChecksum v0 (M11 골격, 8B RPC)"
- [[2026-06-12_netcode/03_scope|〃 ③ scope]] — **기구현 확정**(TerrainSync 시드 + 인벤토리 해시) → 검증만

## 관련 용어
[[desync]] · [[NetEventLogger]] · [[M-지표]]

## 실제 위치
- [`Assets/Scripts/Utilities/NetDiagnostics/StateChecksumV0.cs`](../../../Assets/Scripts/Utilities/NetDiagnostics/StateChecksumV0.cs)
