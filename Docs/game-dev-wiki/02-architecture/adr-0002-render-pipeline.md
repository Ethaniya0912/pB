---
title: adr-0002-렌더-파이프라인
tags: [adr, decision, render]
status: decided
source:
  - Packages/manifest.json
  - SSGIURP.csproj
  - Assets/Package Install/UnitySSGIURP-main/Runtime/ScreenSpaceGlobalIlluminationURP.cs
  - Assets/Shader/Fog_Compute/GPUDrivenShadowManager.cs
  - Assets/Shader/Fog/ShaderCoordinationManager.cs
  - SteamAudioUnity.csproj
verified: 2026-06-15
---

# adr-0002-렌더-파이프라인

URP 17.3.0 + 커스텀 SSGI(UnitySSGIURP) + GPGPU Shadow + SteamAudio 조합 선정.

## 맥락

pB는 동굴 탐험 협동 코옵 게임이다. 동굴 특유의 폐쇄 간접광·공간 음향이 핵심 경험이므로 GI와 음향 품질이 중요하다. Unity 6000.3.1f1에서 URP는 공식 PC 타겟 파이프라인이며, HDRP는 하드웨어 요구사항이 높고 컨텐츠 파이프라인 전환 비용이 크다. 

검토한 대안:

| 파이프라인 | 요약 | 탈락 이유 |
|---|---|---|
| Built-in | 레거시 기본 파이프라인 | 셰이더 그래프·SRP Batcher 미지원, 미래 지원 중단 예정 |
| HDRP | 풀-피지컬 기반 최고화질 | 최소 GPU 요구치 급상승, 중저사양 Steam PC 타겟 배제 위험, 컨텐츠 재작업 비용 |
| URP (기본 GI 없음) | 공식·경량 | GI 없어 동굴 간접광 재현 불가 → SSGI 커스텀 추가로 보완 |

## 결정

`com.unity.render-pipelines.universal` 17.3.0을 기반으로 다음을 추가 채택한다.

1. **SSGI**: `com.jiaozi158.unityssgiurp` (로컬 패키지 `Assets/Package Install/UnitySSGIURP-main`) — URP ScriptableRendererFeature로 삽입. csproj 심볼 `URP_SSGI`로 확인.
2. **GPGPU Shadow**: `Assets/Shader/Fog_Compute/GPUDrivenShadowManager.cs` — 2,200+ 그림자 캐스터를 GPU에 상주시켜 CPU 오버헤드 없이 간접 렌더링.
3. **Shader Coordination Manager**: `Assets/Shader/Fog/ShaderCoordinationManager.cs` — LUT·안개·모션블러·셔터앵글 등 전역 셰이더 변수 단일 방송국.
4. **SteamAudio**: `Assets/Plugins/SteamAudio/` (csproj `SteamAudioUnity`) — 공간 음향·물리 기반 잔향. 컴파일 심볼 `STEAMAUDIO_ENABLED`로 확인.
5. **동굴 렌더**: `Assets/Scripts/Utilities/Cave Genderator/` 내 커스텀 셰이더(`CaveDreamcoreTerrain.shader`, `CaveUndergroundWater.shader`), Compute Shader 2종(`CaveDensityGenerator.compute`, `CaveMarchingCubes.compute`).

## 근거

1. **낮은 최소 사양 유지**: URP는 중저사양 GPU에서도 동작, Steam 코옵 타겟 인구를 포괄.
2. **동굴 GI 필수**: SSGI 없이는 폐쇄 동굴의 간접광 재현이 불가능하여 핵심 비주얼 경험이 깨진다.
3. **CPU Shadow 병목 우회**: 대형 동굴씬에서 2,200+개 그림자 캐스터를 CPU SetPass로 처리하면 프레임 드롭 → GPGPU 간접 렌더링으로 우회.
4. **공간 음향**: 동굴 잔향이 게임 경험 핵심 요소 — SteamAudio가 물리 기반 잔향을 무료로 제공.
5. **URP Dreamcore 스타일**: ShaderCoordinationManager가 LUT·포스터라이즈·모션블러를 통합 관리하여 아트 스타일 일관성 유지.

## 영향

**장점**
- URP 공식 패키지로 Unity 업데이트 수혜.
- SSGI가 ScriptableRendererFeature로 삽입되어 파이프라인 비침습적.
- GPGPU Shadow로 대형 씬 CPU 부하 경감.
- SteamAudio 무료·크로스플랫폼.

**단점·제약**
- URP 17.3.0 종속 — 버전 업 시 SSGI 커스텀 패키지 호환성 재확인 필요.
- SSGI 외부 커스텀 패키지(`jiaozi158/UnitySSGIURP`)가 비공식이라 Unity 버전 업 또는 URP 내부 변경 시 깨질 수 있다.
- GPGPU Shadow: `ComputeShader`·`GraphicsBuffer` NativeArray 직접 관리로 메모리 누수·씬 전환 정리 책임이 코드에 있다.

**되돌리기 비용**: HDRP 전환 시 셰이더·라이팅·씬 전체 재작업 필요. SSGI → URP 내장 GI 전환은 Unity 6에서 URP APV(Adaptive Probe Volumes)로 교체 가능하나 라이팅 재베이크 필요. 커스텀 셰이더 교체도 수반된다.

## ⚠ 비판·리스크

**[심각도: 높음] SSGI 커스텀 패키지 유지보수 리스크**
`com.jiaozi158.unityssgiurp`는 외부 비공식 패키지다. Unity 6 → 7 업그레이드나 URP 내부 API 변경 시 `ScriptableRendererFeature` 인터페이스가 깨질 수 있다. `ScreenSpaceGlobalIlluminationURP.cs`가 `UnityEngine.Rendering.RenderGraphModule`을 `#if UNITY_6000_0_OR_NEWER` 조건부로 사용 중이라 이미 버전 의존이 시작됐다. URP APV로 교체 시점을 사전에 평가해야 한다.

**[심각도: 높음] SSGI 성능 미측정**
동굴 씬 기준 SSGI On/Off 프레임 비용을 실측한 데이터가 없다. 밀폐 동굴에서 GI 레이캐스트 비용이 커질 수 있으며, 저사양 GPU에서의 실측이 EA 전에 필요하다.

**[심각도: 보통] GPUDrivenShadowManager 씬 전환 정리 검증 미흡**
`DontDestroyOnLoad`로 씬 전환 유지되나, `GraphicsBuffer`·`NativeArray` 해제가 `OnDestroy`에서만 처리된다. 에디터 플레이 모드 중지 시 메모리 누수 경고가 발생할 수 있으며, 씬 재로드 후 중복 초기화 방지 가드가 싱글톤 패턴에만 의존한다.

**[심각도: 보통] SteamAudio HRTF 성능 예산 미확정**
공간 음향 HRTF 연산 비용이 플레이어·NPC 수 증가에 따라 선형으로 증가한다. 4인 협동 + 다수 NPC 씬에서의 오디오 스레드 비용을 측정하지 않았다.

## 관련 문서

- [[render-pipeline|렌더-파이프라인]]
- [[adr-template|adr-template]]

---
← [[02-architecture-hub|02 · 아키텍처 기반 결정]] · [[index|인덱스]]
