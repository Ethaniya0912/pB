using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Unity.Collections; // [추가] NativeArray 사용을 위한 네임스페이스
using Dreamcore.Atmosphere;

/// <summary>
/// [Dreamcore/Atmosphere] 
/// [v9.4] 컴파일 에러 해결 및 성능 최적화 버전.
/// 
/// [수정 사항]
/// 1. [Hotfix] NativeArray 오타 수정: Native全力 -> NativeArray로 변경하여 CS0246 에러 해결.
/// 2. [Hotfix] 네임스페이스 추가: Unity.Collections 추가.
/// 3. [Optimization] GC Alloc 제거: PassData 내 배열을 미리 할당하고 Array.Copy를 사용하여 매 프레임 발생하던 .Clone() 부하를 해결.
/// 4. [Optimization] 광원 수집 최적화: FindObjectsByType 대신 URP의 VisibleLights 리스트를 활용하여 CPU 부하 대폭 감소.
/// </summary>
public class AtmosphericMassRendererFeature : ScriptableRendererFeature
{
    public AtmosphericMassConfig.FeatureSettings settings = new AtmosphericMassConfig.FeatureSettings();

    class AtmosphericPass : ScriptableRenderPass
    {
        private AtmosphericMassConfig.FeatureSettings m_Settings;
        [System.NonSerialized] private Material m_FogMat;
        [System.NonSerialized] private RTHandle m_FogRT;
        [System.NonSerialized] private List<Light> m_SceneLights;
        [System.NonSerialized] private Vector4[] m_LightPosRange;
        [System.NonSerialized] private Vector4[] m_LightColorInt;

        public AtmosphericPass(AtmosphericMassConfig.FeatureSettings featureSettings)
        {
            this.m_Settings = featureSettings;
            m_LightPosRange = new Vector4[AtmosphericMassConfig.Constants.MaxShaderLights];
            m_LightColorInt = new Vector4[AtmosphericMassConfig.Constants.MaxShaderLights];
            m_SceneLights = new List<Light>();
        }

        private bool LazyInitialize()
        {
            if (m_Settings == null || m_Settings.atmosphericShader == null) return false;
            if (m_FogMat == null) m_FogMat = CoreUtils.CreateEngineMaterial(m_Settings.atmosphericShader);
            return m_FogMat != null;
        }

        private class PassData
        {
            public Material fogMat;
            public TextureHandle colorSource;
            public TextureHandle depthSource;
            public TextureHandle fogBuffer;
            public Matrix4x4 invViewProj;
            public Vector3 camPosWS;
            public Vector4 fogParams;
            public Vector4 lightParams;
            public Vector4 styleParams;
            public Vector4 debugParams;
            public Vector4 selfShadowParams;
            public Color ambientColor;
            public int customLightCount;
            // [최적화] 배열 참조 대신 고정 크기 배열을 사용하여 GC 할당 방지
            public Vector4[] customLightPosRange = new Vector4[64];
            public Vector4[] customLightColorInt = new Vector4[64];
            public Texture2D blueNoise;
            public Texture2D rampTex;
        }

        private class CopyPassData
        {
            public TextureHandle input;
        }

        /// <summary>
        /// [최적화] 가시 광원 리스트를 활용하여 수동 광원 정보를 수집합니다.
        /// </summary>
        private void CollectLightsOptimized(Vector3 camPos, NativeArray<VisibleLight> visibleLights)
        {
            if (m_Settings == null || !m_Settings.useManualLightCollection) return;

            m_SceneLights.Clear();

            // [최적화] FindObjectsByType 대신 렌더링 파이프라인이 이미 알고 있는 가시 광원 리스트를 순회합니다.
            for (int i = 0; i < visibleLights.Length; i++)
            {
                VisibleLight vl = visibleLights[i];
                Light l = vl.light;

                if (l != null && l.enabled && (l.type == LightType.Point || l.type == LightType.Spot))
                {
                    m_SceneLights.Add(l);
                }
            }

            // 카메라 거리순 정렬
            m_SceneLights.Sort((a, b) => Vector3.SqrMagnitude(a.transform.position - camPos).CompareTo(Vector3.SqrMagnitude(b.transform.position - camPos)));

            int count = Mathf.Min(m_SceneLights.Count, Mathf.Min(m_Settings.maxExtraLights, AtmosphericMassConfig.Constants.MaxShaderLights));
            for (int i = 0; i < AtmosphericMassConfig.Constants.MaxShaderLights; i++)
            {
                if (i < count)
                {
                    Light l = m_SceneLights[i];
                    Color col = QualitySettings.activeColorSpace == ColorSpace.Linear ? l.color.linear : l.color;
                    m_LightPosRange[i] = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
                    m_LightColorInt[i] = new Vector4(col.r, col.g, col.b, l.intensity);
                }
                else
                {
                    m_LightPosRange[i] = Vector4.zero;
                    m_LightColorInt[i] = Vector4.zero;
                }
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!LazyInitialize()) return;

            // [최적화] 안개가 보이지 않는 설정일 경우 패스 기록을 생략하여 CPU 자원 보존
            if (m_Settings.fogDensity <= 0.0001f || m_Settings.maxDistance <= 0.1f) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            if (resourceData == null || !resourceData.activeColorTexture.IsValid()) return;

            TextureHandle activeColor = resourceData.activeColorTexture;

            // 1. Fog Buffer Descriptor 설정
            RenderTextureDescriptor fogDesc = cameraData.cameraTargetDescriptor;
            fogDesc.width = Mathf.Max(1, fogDesc.width / m_Settings.downsample);
            fogDesc.height = Mathf.Max(1, fogDesc.height / m_Settings.downsample);
            fogDesc.msaaSamples = 1;
            fogDesc.depthBufferBits = 0;
            fogDesc.colorFormat = RenderTextureFormat.ARGBHalf;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_FogRT, fogDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_VolumetricFogRT");

            Matrix4x4 projectionMatrix = cameraData.camera.projectionMatrix;
            Matrix4x4 viewProjMat = GL.GetGPUProjectionMatrix(projectionMatrix, false) * cameraData.camera.worldToCameraMatrix;

            // [최적화] 가시 광원 데이터를 사용하여 광원 정보 수집
            CollectLightsOptimized(cameraData.camera.transform.position, lightData.visibleLights);

            TextureHandle fogBufferHandle = renderGraph.ImportTexture(m_FogRT);

            // [Pass 1] Volumetric Fog Generation
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetric_Atmosphere_Gen", out var passData))
            {
                passData.fogMat = m_FogMat;
                passData.invViewProj = viewProjMat.inverse;
                passData.camPosWS = cameraData.camera.transform.position;
                passData.fogParams = new Vector4(m_Settings.fogDensity, (float)m_Settings.stepCount, m_Settings.maxDistance, m_Settings.anisotropy);
                passData.lightParams = new Vector4(m_Settings.lightScatterMult, m_Settings.jitterStrength, m_Settings.shadowContrast, (float)m_Settings.lightQuantization);
                passData.styleParams = new Vector4(m_Settings.rampStrength, m_Settings.shadowThreshold, m_Settings.useManualLightCollection ? 1.0f : 0.0f, 0);
                passData.debugParams = new Vector4((float)m_Settings.debugMode, 0, 0, 0);
                passData.selfShadowParams = new Vector4(3.0f, 0.15f, 0.05f, 0.0f);
                passData.ambientColor = m_Settings.ambientColor;
                passData.blueNoise = m_Settings.blueNoiseTexture;
                passData.rampTex = m_Settings.rampTexture;
                passData.customLightCount = Mathf.Min(m_SceneLights.Count, m_Settings.maxExtraLights);

                // [최적화] Clone() 대신 Array.Copy를 사용하여 힙 할당을 제거함 (PassData 내 고정 배열 활용)
                System.Array.Copy(m_LightPosRange, passData.customLightPosRange, 64);
                System.Array.Copy(m_LightColorInt, passData.customLightColorInt, 64);

                passData.depthSource = resourceData.activeDepthTexture;
                passData.fogBuffer = fogBufferHandle;

                if (resourceData.mainShadowsTexture.IsValid()) builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                if (resourceData.additionalShadowsTexture.IsValid()) builder.UseTexture(resourceData.additionalShadowsTexture, AccessFlags.Read);

                builder.UseTexture(passData.depthSource, AccessFlags.Read);
                builder.SetRenderAttachment(passData.fogBuffer, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    data.fogMat.SetMatrix("_InvViewProj", data.invViewProj);
                    data.fogMat.SetVector("_CameraPosWS", data.camPosWS);
                    data.fogMat.SetVector("_FogParams", data.fogParams);
                    data.fogMat.SetVector("_LightParams", data.lightParams);
                    data.fogMat.SetVector("_StyleParams", data.styleParams);
                    data.fogMat.SetVector("_DebugParams", data.debugParams);
                    data.fogMat.SetVector("_SelfShadowParams", data.selfShadowParams);
                    data.fogMat.SetColor("_AmbientColor", data.ambientColor);
                    if (data.blueNoise) data.fogMat.SetTexture("_NoiseTex", data.blueNoise);
                    if (data.rampTex) data.fogMat.SetTexture("_RampTex", data.rampTex);
                    data.fogMat.SetInt("_CustomLightCount", data.customLightCount);
                    data.fogMat.SetVectorArray("_CustomLightPosRange", data.customLightPosRange);
                    data.fogMat.SetVectorArray("_CustomLightColorInt", data.customLightColorInt);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.fogMat, 0, MeshTopology.Triangles, 3);
                });
            }

            // [Pass 2] Composition 준비 (배경 보존을 위한 Temp 생성 및 복사)
            RenderTextureDescriptor compDesc = cameraData.cameraTargetDescriptor;
            compDesc.depthBufferBits = 0;
            TextureHandle tempColor = renderGraph.CreateTexture(new TextureDesc(compDesc) { name = "Atmosphere_CompositeTemp" });

            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Atmosphere_Prepare_Background", out var copyData))
            {
                copyData.input = activeColor;
                builder.UseTexture(activeColor, AccessFlags.Read);
                builder.SetRenderAttachment(tempColor, 0);
                builder.SetRenderFunc((CopyPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.input, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }

            // [Pass 3] Volumetric Fog Composition (Temp 배경 위에 안개 합성)
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetric_Fog_Composition", out var passData))
            {
                passData.fogMat = m_FogMat;
                passData.fogBuffer = fogBufferHandle;
                passData.colorSource = tempColor;
                passData.debugParams = new Vector4((float)m_Settings.debugMode, 0, 0, 0);

                builder.UseTexture(passData.fogBuffer, AccessFlags.Read);
                builder.UseTexture(passData.colorSource, AccessFlags.Read);
                builder.SetRenderAttachment(activeColor, 0);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    data.fogMat.SetTexture("_MainTex", data.colorSource);
                    data.fogMat.SetTexture("_FogTex", data.fogBuffer);
                    data.fogMat.SetVector("_DebugParams", data.debugParams);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.fogMat, 1, MeshTopology.Triangles, 3);
                });
            }
        }

        public void Dispose() { if (m_FogRT != null) m_FogRT.Release(); }
    }

    private AtmosphericPass m_Pass;
    public override void Create() { m_Pass = new AtmosphericPass(settings); }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Pass != null && renderingData.cameraData.postProcessEnabled)
        {
            m_Pass.renderPassEvent = settings.renderPassEvent;
            renderer.EnqueuePass(m_Pass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (m_Pass != null) m_Pass.Dispose();
    }
}