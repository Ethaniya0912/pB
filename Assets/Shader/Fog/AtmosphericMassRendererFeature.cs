using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Dreamcore.Atmosphere;

/// <summary>
/// [Dreamcore/Atmosphere] 
/// [v9.0] Recursive Thinking 기반 아티팩트 완전 제거 버전.
/// 
/// [수정 사항]
/// 15. [Critical Fix] 원형 구체(Blob) 및 플레어 아티팩트 해결:
///     ㄴ 원인: 고정된 샘플 개수와 동심원 구조의 샘플링이 카메라 이동 시 '구' 형태로 가시화됨.
///     ㄴ 해결: 2차 레이마칭의 보폭을 픽셀별로 무작위화하고, 광원 방향으로의 탐색을 '비정형 랜덤 분포'로 전환.
///     ㄴ 파라미터: SelfShadowParams (x: 강도 3.0, y: 랜덤 확산 계수 0.15, z: 최소 투과 0.05).
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
            public TextureHandle mainShadowmap;
            public TextureHandle additionalShadowmap;
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
            public Vector4[] customLightPosRange;
            public Vector4[] customLightColorInt;
            public Texture2D blueNoise;
            public Texture2D rampTex;
        }

        private void CollectLightsManual(Vector3 camPos)
        {
            if (!Application.isPlaying && !Application.isEditor) return;
            if (m_Settings == null || !m_Settings.useManualLightCollection) return;

            m_SceneLights.Clear();
            var allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in allLights)
            {
                if (l != null && l.enabled && (l.type == LightType.Point || l.type == LightType.Spot))
                    m_SceneLights.Add(l);
            }
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
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

            if (resourceData == null || !resourceData.activeColorTexture.IsValid()) return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.width = Mathf.Max(1, desc.width / m_Settings.downsample);
            desc.height = Mathf.Max(1, desc.height / m_Settings.downsample);
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.colorFormat = RenderTextureFormat.ARGBHalf;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_FogRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_VolumetricFogRT");
            Matrix4x4 viewProjMat = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, false) * cameraData.camera.worldToCameraMatrix;
            CollectLightsManual(cameraData.camera.transform.position);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetric_Atmosphere_Gen", out var passData))
            {
                passData.fogMat = m_FogMat;
                passData.invViewProj = viewProjMat.inverse;
                passData.camPosWS = cameraData.camera.transform.position;
                passData.fogParams = new Vector4(m_Settings.fogDensity, (float)m_Settings.stepCount, m_Settings.maxDistance, m_Settings.anisotropy);
                passData.lightParams = new Vector4(m_Settings.lightScatterMult, m_Settings.jitterStrength, m_Settings.shadowContrast, (float)m_Settings.lightQuantization);
                passData.styleParams = new Vector4(m_Settings.rampStrength, m_Settings.shadowThreshold, m_Settings.useManualLightCollection ? 1.0f : 0.0f, 0);
                passData.debugParams = new Vector4((float)m_Settings.debugMode, 0, 0, 0);
                passData.selfShadowParams = new Vector4(3.0f, 0.15f, 0.05f, 0.0f); // v9.0 정밀 튜닝
                passData.ambientColor = m_Settings.ambientColor;
                passData.blueNoise = m_Settings.blueNoiseTexture;
                passData.rampTex = m_Settings.rampTexture;
                passData.customLightCount = Mathf.Min(m_SceneLights.Count, m_Settings.maxExtraLights);
                passData.customLightPosRange = m_LightPosRange;
                passData.customLightColorInt = m_LightColorInt;

                passData.colorSource = resourceData.activeColorTexture;
                passData.depthSource = resourceData.activeDepthTexture;
                passData.fogBuffer = renderGraph.ImportTexture(m_FogRT);

                if (resourceData.mainShadowsTexture.IsValid()) builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                if (resourceData.additionalShadowsTexture.IsValid()) builder.UseTexture(resourceData.additionalShadowsTexture, AccessFlags.Read);

                builder.UseTexture(passData.colorSource, AccessFlags.Read);
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

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetric_Fog_Composition", out var passData))
            {
                passData.fogMat = m_FogMat;
                passData.fogBuffer = renderGraph.ImportTexture(m_FogRT);
                passData.debugParams = new Vector4((float)m_Settings.debugMode, 0, 0, 0);
                builder.UseTexture(passData.fogBuffer, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
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
}