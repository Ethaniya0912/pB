using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Dreamcore.Atmosphere;

/// <summary>
/// [Dreamcore/Atmosphere] 
/// [v9.2] Render Graph 안정화 및 오브젝트 블랙 현상 해결 버전.
/// 
/// [수정 사항]
/// 15. [Critical Fix] 원형 구체(Blob) 및 플레어 아티팩트 해결:
///     ㄴ 원인: 고정된 샘플 개수와 동심원 구조의 샘플링이 카메라 이동 시 '구' 형태로 가시화됨.
///     ㄴ 해결: 2차 레이마칭의 보폭을 픽셀별로 무작위화하고, 광원 방향으로의 탐색을 '비정형 랜덤 분포'로 전환.
///     ㄴ 파라미터: SelfShadowParams (x: 강도 3.0, y: 랜덤 확산 계수 0.15, z: 최소 투과 0.05).
/// [Fix v9.2] Object Black-out Fix:
///     ㄴ 합성 패스에서 배경(activeColor)을 Temp로 복사한 뒤, 그 위에 안개를 그려 배경 유실 방지.
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
            public Vector4[] customLightPosRange;
            public Vector4[] customLightColorInt;
            public Texture2D blueNoise;
            public Texture2D rampTex;
        }

        private class CopyPassData
        {
            public TextureHandle input;
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
            Matrix4x4 viewProjMat = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, false) * cameraData.camera.worldToCameraMatrix;
            CollectLightsManual(cameraData.camera.transform.position);

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
                passData.customLightPosRange = (Vector4[])m_LightPosRange.Clone();
                passData.customLightColorInt = (Vector4[])m_LightColorInt.Clone();

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

            // 현재 화면을 Temp로 복사
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
                passData.colorSource = tempColor; // 복사된 배경
                passData.debugParams = new Vector4((float)m_Settings.debugMode, 0, 0, 0);

                builder.UseTexture(passData.fogBuffer, AccessFlags.Read);
                builder.UseTexture(passData.colorSource, AccessFlags.Read);

                // 결과물은 다시 activeColorTexture로 출력
                builder.SetRenderAttachment(activeColor, 0);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // 쉐이더가 배경 화면을 인지하도록 _MainTex로 전달
                    data.fogMat.SetTexture("_MainTex", data.colorSource);
                    data.fogMat.SetTexture("_FogTex", data.fogBuffer);
                    data.fogMat.SetVector("_DebugParams", data.debugParams);

                    // Pass 1 (Composition Pass) 실행
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
