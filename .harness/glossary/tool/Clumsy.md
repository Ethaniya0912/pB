---
type: tool
aliases: [클럼지]
---
# Clumsy

**분류**: tool · 외부 네트워크 열화 도구 (Windows)

## 한 줄 정의
- Windows에서 **OS(운영체제) 수준으로** 패킷 지연·손실·중복·순서 섞임을 인위로 발생시키는 외부 도구. 코드 시뮬레이터([[NetSimProfiles]])가 안전상 주입하지 않는 **손실률**(PROF-A 2%·PROF-B 5%) 측정을 보완한다.

## 쉬운 설명
> 수도꼭지를 일부러 반쯤 잠가서 "수압이 약할 때 샤워기가 어떻게 되나" 실험하듯, 컴퓨터 전체의 인터넷을 일부러 나쁘게 만드는 프로그램. 게임 코드 바깥(OS)에서 작동하므로 게임 입장에선 진짜 나쁜 인터넷과 구분이 없다.

## 등장 사이클
- [[2026-06-12_netcode/01_target|2026-06-12_netcode ① target]] — T5 시뮬레이터 후보(Simulator vs Clumsy)
- [[2026-06-12_netcode/decisions|〃 decisions]] — G1: 기본 도구는 NGO Simulator 채택, G3: **손실률 측정만 Clumsy로 보완**
- [[2026-06-12_netcode/08_result#잔여 이슈 / 후속 제안|〃 ⑧ result]] — 후속 제안 2번(PROF 손실 보완)

## 관련 용어
[[PROF-프리셋]] · [[NetSimProfiles]] · [[SteamP2PRelayTransport]]
