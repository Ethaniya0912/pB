---
title: 렌더-파이프라인
tags: [architecture, decision, render]
status: done
source:
  - Packages/manifest.json
  - SSGIURP.csproj
  - Assets/Package Install/UnitySSGIURP-main/Runtime/ScreenSpaceGlobalIlluminationURP.cs
  - Assets/Package Install/UnitySSGIURP-main/Runtime/ScreenSpaceGlobalIlluminationVolume.cs
  - Assets/Shader/Fog_Compute/GPUDrivenShadowManager.cs
  - Assets/Shader/Fog/ShaderCoordinationManager.cs
  - Assets/Scripts/Utilities/Cave Genderator/CaveComputeDispatcher.cs
  - SteamAudioUnity.csproj
verified: 2026-06-15
---

# 렌더-파이프라인

URP 17.3.0 기반, 커스텀 SSGI·GPGPU Shadow·Shader Coordination Manager·SteamAudio 공간음향으로 구성된 동굴 특화 렌더 스택.

## 현황 (pB)

### URP 버전

`Packages/manifest.json`:
```json
"com.unity.render-pipelines.universal": "17.3.0"
```
Unity 6000.3.1f1 기준 최신 LTS 패치. `SSGIURP.csproj` DefineConstants에서 `UNITY_PIPELINE_URP` 심볼 확인됨.

### SSGI (Screen-Space Global Illumination)

- 패키지: `com.jiaozi158.unityssgiurp` → 로컬 경로 `Assets/Package Install/UnitySSGIURP-main`
- 핵심 파일: `ScreenSpaceGlobalIlluminationURP.cs` (`ScriptableRendererFeature` 서브클래스), `ScreenSpaceGlobalIlluminationVolume.cs`
- URP Renderer Feature로 비침습 삽입. `#if UNITY_6000_0_OR_NEWER`로 RenderGraph API 분기 처리.
- 컴파일 심볼: `URP_SSGI`

### GPGPU Shadow Manager

`Assets/Shader/Fog_Compute/GPUDrivenShadowManager.cs`:
- 역할: 2,200+ 그림자 캐스터 데이터를 `GraphicsBuffer`에 업로드, `GPUCulling.compute`로 GPU Frustum Culling, `DrawMeshInstancedIndirect`로 간접 렌더링.
- 데이터 구조: `InstanceData` = `Matrix4x4 localToWorld`(64B) + `Vector3 boundsCenter`(12B) + `float boundsRadius`(4B) = 80B, 16바이트 패딩 준수.
- 씬 전환: `DontDestroyOnLoad` + 싱글톤, `NativeArray` 해제는 `OnDestroy`.
- Prefab: `Assets/Shader/Fog_Compute/GPGPU_ShadowManager.prefab`

### Shader Coordination Manager

`Assets/Shader/Fog/ShaderCoordinationManager.cs`:
- 역할: 전역 셰이더 변수 단일 방송국. LUT·안개(`fogDensity`, `raySteps`)·엣지 보호·속도 피드백·모션블러(VP 행렬 델타·셔터앵글) 통합.
- `[ExecuteInEditMode]` — 에디터에서도 항상 동작.
- Prefab: `Assets/Shader/Fog/Wolrd Shader Manager.prefab`

### 동굴 렌더 Compute

`Assets/Scripts/Utilities/Cave Genderator/` 내:
- `CaveDensityGenerator.compute` — 동굴 밀도장 GPU 생성
- `CaveMarchingCubes.compute` — Marching Cubes 메시 GPU 추출
- `CaveComputeDispatcher.cs` — Dispatch 진입점, `BiomeSyncMode` 열거형(Legacy→GpuAligned→SingleSourceEcotone→FullMerge)으로 원자적 기능 토글
- 커스텀 셰이더: `CaveDreamcoreTerrain.shader`, `CaveUndergroundWater.shader`

### SteamAudio 공간음향

- 플러그인: `Assets/Plugins/SteamAudio/`, csproj 심볼 `STEAMAUDIO_ENABLED`
- 역할: 물리 기반 잔향·HRTF 공간음향. 동굴 음향 경험 핵심 요소.

## 설계·결정

ADR → [[adr-0002-render-pipeline|adr-0002-렌더-파이프라인]] 참조.

핵심 설계 선택:
1. **HDRP 불채택** — Steam PC 중저사양 포괄 목적. URP + 커스텀 확장으로 화질 보완.
2. **SSGI ScriptableRendererFeature** — 파이프라인 비침습. 필요 시 OnOff 가능.
3. **GPGPU 그림자** — 동굴 씬의 2,200+ 캐스터를 CPU SetPass 없이 처리. GPU DrawIndirect 구조.
4. **단일 Shader Coordination Manager** — 셰이더 전역 변수를 한 곳에서 관리해 중복 `Shader.SetGlobal*` 호출 방지.
5. **SteamAudio** — 물리 기반 잔향은 직접 구현 대비 품질·비용 모두 유리.

## ⚠ 비판·리스크

**[심각도: 높음] SSGI 커스텀 패키지 비공식 의존**
`jiaozi158/UnitySSGIURP`는 Unity 공식 패키지가 아니다. URP 17→18 업그레이드 시 `ScriptableRendererFeature` 내부 API 변경으로 깨질 가능성이 있으며, 패키지 관리자가 메인테이너 개인이라 장기 유지보수 보장이 없다. Unity 6 URP에 APV(Adaptive Probe Volumes)가 내장됐으므로 마이그레이션 시점을 EA 전에 평가해야 한다.

**[심각도: 높음] SSGI 성능 예산 실측 없음**
동굴 씬 복잡도 기준 SSGI On/Off 비용 측정 데이터가 존재하지 않는다. 밀폐 다중 반사 환경에서 SSGI 레이마치 비용은 오픈 씬보다 높을 수 있으며, 저사양 GPU(GTX 1060급) 타겟 검증이 EA 전에 필요하다.

**[심각도: 보통] GPUDrivenShadowManager NativeArray 수명 취약**
`NativeArray<InstanceData>`를 `OnDestroy`에서만 Dispose한다. 에디터 플레이 중지 시 GC 호출 순서에 따라 Unity가 "A Native Collection has not been disposed" 경고를 발생시킬 수 있다. `OnDisable`에서도 조건부 해제 또는 `[RuntimeInitializeOnLoadMethod]`로 수명 관리 강화가 필요하다.

**[심각도: 보통] ShaderCoordinationManager `[ExecuteInEditMode]` 부작용**
에디터에서 항상 셰이더 전역 변수를 세팅하므로 에디터 내 다른 씬 시각화 도구(SceneView 라이팅 모드 등)가 의도치 않게 영향을 받을 수 있다. 에디터 전용 셰이더 변수 오염 여부를 확인하지 않았다.

**[심각도: 보통] BiomeSyncMode 원자성 보장 미검증**
`CaveComputeDispatcher`의 `BiomeSyncMode` 열거형은 CPU·GPU 쪽 동시 일괄 적용을 설계 목표로 하지만, 실제로 같은 프레임 내 원자적 적용이 보장되는지 통합 테스트가 없다.

**[심각도: 낮음] SteamAudio HRTF 스레드 비용 미측정**
4인 협동 + 다수 NPC 동굴 씬에서 SteamAudio 오디오 스레드 점유율을 측정하지 않았다. 엔티티 수 증가 시 선형 비용 증가로 오디오 레이턴시 문제가 발생할 수 있다.

## 관련 문서

- [[adr-0002-render-pipeline|adr-0002-렌더-파이프라인]]
- [[ecs-vs-oop|ecs-vs-oop]]

---
← [[02-architecture-hub|02 · 아키텍처 기반 결정]] · [[index|인덱스]]
