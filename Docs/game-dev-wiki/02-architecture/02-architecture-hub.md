---
title: 02-architecture-hub
tags: [moc, architecture]
status: done
source:
  - Assets/Scripts/Networking/
  - Assets/Scripts/World Manager/
  - Assets/Scripts/Character/
  - Assets/Scripts/Utilities/Cave Genderator/
  - Assets/Shader/Fog_Compute/
  - Assets/Shader/Fog/
  - Packages/manifest.json
verified: 2026-06-15
---

# 02 · 아키텍처 기반 결정

후반 전환 비용이 큰 핵심 기술 선택. 결정은 ADR로 남긴다.

> 이 허브의 모든 문서가 2026-06-15 기준으로 코드 실측 반영 완료됐다.

## 요약 (pB 현재 스택)

| 영역 | 선택 | 상태 |
|---|---|---|
| 네트워크 | NGO 2.7.0 + SteamP2PRelayTransport (Facepunch) | decided · 코드 완료 |
| 렌더 파이프라인 | URP 17.3.0 + SSGI(jiaozi158) + GPGPU Shadow | decided · 동작 중 |
| 공간음향 | SteamAudio (HRTF + 물리 잔향) | decided · 동작 중 |
| DI 컨테이너 | 미사용 — 수동 싱글톤 28개+ | 결정 아님, 누적 패턴 |
| ECS / DOTS | 미도입 — OOP MonoBehaviour 매니저 | 결정 아님, 초기 템플릿 답습 |
| 동굴 생성 | GPGPU Compute Shader (Marching Cubes + Density) | 동작 중 |
| AI 결합 | Bridge + Interface(Contract) 분리 | researching · 구조 매핑 |

## 문서

- [[render-pipeline|렌더-파이프라인]] — URP 현황·SSGI·GPGPU Shadow·ShaderCoordinationManager
- [[ecs-vs-oop|ecs-vs-oop]] — OOP 매니저 패턴 현황·DOTS 미도입·비판
- [[di-container|di-컨테이너]] — DI 미도입·수동 싱글톤 28개+·비판
- [[ai-bridge-architecture|ai-bridge-아키텍처]] — Bridge 패턴·도메인 인터페이스·AI 결합 구조 (신규)
- [[adr-0001-netcode|adr-0001-netcode-선정]] — NGO + SteamP2PRelayTransport 선정 ADR
- [[adr-0002-render-pipeline|adr-0002-렌더-파이프라인]] — URP + SSGI 선정 ADR

## ADR (아키텍처 결정 기록)

- [[adr-template|adr-template]] (템플릿, 수정 금지)
- [[adr-0001-netcode|adr-0001-netcode-선정]] — status: decided
- [[adr-0002-render-pipeline|adr-0002-렌더-파이프라인]] — status: decided

## 비판 요약 (심각도 높음)

- **netcode**: 2인 P2P 베이스라인 미측정 — Steam 릴레이 실측 RTT 확인 필요.
- **netcode**: 호스트 이탈=세션 종료 구조 — 재호스팅·ConnectionApproval 미구현(Step 3 예정).
- **render**: SSGI 비공식 패키지 — URP 버전 업 시 파손 위험.
- **render**: SSGI 성능 실측 없음 — 저사양 GPU 동굴씬 비용 미확인.
- **DI**: 정적 싱글톤 28개+ — 암묵적 의존성·유닛 테스트 불가.
- **ECS**: OOP god-manager 경향 — AI·아이템 규모 확장 시 성능 한계.
- **AI**: 최대 비중 도메인인데 위키 문서 부재(착수)·동기화 Trust/Trauma 대역폭 미측정.

---
← [[index|인덱스]]
