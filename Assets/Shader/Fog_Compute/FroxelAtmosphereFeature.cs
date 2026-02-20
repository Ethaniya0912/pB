using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// [Lead Architect]
/// ShaderCoordinationManager와 동기화된 Froxel(3D 격자) 볼륨메트릭 안개 렌더러입니다.
/// 깜빡임(Flickering) 방지 및 DX11 충돌 최적화가 완벽히 적용된 마스터 버전입니다.
/// </summary>
public class FroxelAtmosphereFeature : ScriptableRendererFeature
{
    [Header("Compute Settings")]
    public ComputeShader froxelCompute;
    public Material compositionMaterial;

    [Header("Grid Resolution (Hardware Fixed)")]
    public Vector3Int gridSize = new Vector3Int(160, 90, 64);

    [Header("Scattering Settings")]
    [Range(-1f, 1f)] public float anisotropy = 0.5f;

    // GPU 메모리 버스 효율을 위해 16바이트 정렬(48 Bytes)을 완벽히 맞춥니다.
    [StructLayout(LayoutKind.Sequential)]
    public struct FroxelLightData
    {
        public Vector4 colorAndInten; // 16 bytes
        public Vector4 posAndRange;   // 16 bytes
        public int visibleLightIndex; // 4 bytes (URP 그림자 매핑용)
        public Vector3 padding;       // 12 bytes -> 총 48 bytes
    }

    private FroxelPass _froxelPass;

    public override void Create()
    {
        if (froxelCompute == null || compositionMaterial == null) return;
        _froxelPass = new FroxelPass(this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_froxelPass != null && (renderingData.cameraData.cameraType == CameraType.Game || renderingData.cameraData.cameraType == CameraType.SceneView))
        {
            renderer.EnqueuePass(_froxelPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _froxelPass?.Cleanup();
    }

    class FroxelPass : ScriptableRenderPass
    {
        private FroxelAtmosphereFeature _settings;
        private GraphicsBuffer _lightBuffer;
        private NativeArray<FroxelLightData> _nativeLightData;

        private int _injectKernel;
        private int _accumulateKernel;

        private static readonly int _FogDensityID = Shader.PropertyToID("_FogDensity");
        private static readonly int _MaxDistID = Shader.PropertyToID("_MaxDist");
        private static readonly int _GlobalAmbientColorID = Shader.PropertyToID("_GlobalAmbientColor");

        public FroxelPass(FroxelAtmosphereFeature settings)
        {
            _settings = settings;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

            _injectKernel = _settings.froxelCompute.FindKernel("CSInjectLighting");
            _accumulateKernel = _settings.froxelCompute.FindKernel("CSAccumulateScattering");

            _nativeLightData = new NativeArray<FroxelLightData>(64, Allocator.Persistent);
            _lightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, Marshal.SizeOf<FroxelLightData>());
        }

        public void Cleanup()
        {
            if (_nativeLightData.IsCreated) _nativeLightData.Dispose();
            _lightBuffer?.Release();
        }

        private class PassData
        {
            public ComputeShader computeShader;
            public Material compMaterial;

            // DX11 충돌을 막기 위한 2개의 분리된 텍스처
            public TextureHandle froxelVolumeInject;
            public TextureHandle froxelVolumeScattering;

            public TextureHandle activeColor;
            public GraphicsBuffer lightBuffer;
            public int lightCount;
            public Vector3 gridSize;
            public float nearClip, maxDist, density, anisotropy;
            public Color ambientColor;
            public Matrix4x4 invViewProj;
            public Vector3 cameraPosWS;
            public float jitterOffset;
            public TextureHandle mainShadowsTexture;
            public TextureHandle additionalShadowsTexture;
            public int mainLightCascadesCount;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

            if (!resourceData.activeColorTexture.IsValid()) return;

            float globalDensity = Shader.GetGlobalFloat(_FogDensityID);
            float globalMaxDist = Shader.GetGlobalFloat(_MaxDistID);
            Color globalAmbient = Shader.GetGlobalColor(_GlobalAmbientColorID);

            if (globalDensity <= 0.001f) return;

            // 텍스처 생성 (Bilinear 필터링 적용으로 깍두기 방지)
            TextureDesc injectDesc = new TextureDesc(_settings.gridSize.x, _settings.gridSize.y)
            {
                dimension = TextureDimension.Tex3D,
                slices = _settings.gridSize.z,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                msaaSamples = MSAASamples.None,
                name = "FroxelVolume_Inject"
            };
            TextureHandle froxelInjectHandle = renderGraph.CreateTexture(injectDesc);

            TextureDesc scatterDesc = new TextureDesc(_settings.gridSize.x, _settings.gridSize.y)
            {
                dimension = TextureDimension.Tex3D,
                slices = _settings.gridSize.z,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                msaaSamples = MSAASamples.None,
                name = "FroxelVolume_Scatter"
            };
            TextureHandle froxelScatterHandle = renderGraph.CreateTexture(scatterDesc);

            // [Fix 1] 빛 수집 최적화 및 태양광 필터링 (화면 폭주 방지)
            int mainLightIndex = lightData.mainLightIndex;
            int additionalLightIndex = 0;
            int lCount = 0;

            for (int i = 0; i < lightData.visibleLights.Length; i++)
            {
                if (i == mainLightIndex) continue; // 태양광 제외

                var l = lightData.visibleLights[i];
                if (l.lightType == LightType.Directional) continue; // 안전장치

                if (lCount < 64)
                {
                    float intensity = l.light != null ? l.light.intensity : 1f;
                    Vector3 pos = l.light != null ? l.light.transform.position : (Vector3)l.localToWorldMatrix.GetColumn(3);

                    _nativeLightData[lCount] = new FroxelLightData
                    {
                        colorAndInten = new Vector4(l.finalColor.r, l.finalColor.g, l.finalColor.b, intensity),
                        posAndRange = new Vector4(pos.x, pos.y, pos.z, l.range),
                        visibleLightIndex = additionalLightIndex,
                        padding = Vector3.zero
                    };
                    lCount++;
                }
                additionalLightIndex++;
            }
            _lightBuffer.SetData(_nativeLightData, 0, 0, lCount);

            // [Fix 2] Jitter 0 고정 (TAA가 없을 때 발생하는 사이클링 깜빡임 원천 차단!)
            float jitter = 0f;

            Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, false) * cameraData.camera.worldToCameraMatrix;

            float safeNearClip = Mathf.Max(0.01f, cameraData.camera.nearClipPlane);
            float safeMaxDist = Mathf.Max(safeNearClip + 1.0f, globalMaxDist);

            using (var builder = renderGraph.AddComputePass<PassData>("Froxel_Compute_Pass", out var passData))
            {
                passData.computeShader = _settings.froxelCompute;
                passData.froxelVolumeInject = froxelInjectHandle;
                passData.froxelVolumeScattering = froxelScatterHandle;
                passData.lightBuffer = _lightBuffer;
                passData.lightCount = lCount;
                passData.gridSize = new Vector3(_settings.gridSize.x, _settings.gridSize.y, _settings.gridSize.z);
                passData.nearClip = safeNearClip;
                passData.maxDist = safeMaxDist;
                passData.density = globalDensity;
                passData.ambientColor = globalAmbient;
                passData.anisotropy = _settings.anisotropy;
                passData.invViewProj = viewProj.inverse;
                passData.cameraPosWS = cameraData.camera.transform.position;
                passData.jitterOffset = jitter;
                passData.mainLightCascadesCount = shadowData.mainLightShadowCascadesCount;

                builder.UseTexture(passData.froxelVolumeInject, AccessFlags.ReadWrite);
                builder.UseTexture(passData.froxelVolumeScattering, AccessFlags.ReadWrite);

                if (resourceData.mainShadowsTexture.IsValid())
                {
                    builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                    passData.mainShadowsTexture = resourceData.mainShadowsTexture;
                }

                if (resourceData.additionalShadowsTexture.IsValid())
                {
                    builder.UseTexture(resourceData.additionalShadowsTexture, AccessFlags.Read);
                    passData.additionalShadowsTexture = resourceData.additionalShadowsTexture;
                }

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    if (data.mainShadowsTexture.IsValid())
                    {
                        cmd.SetGlobalTexture("_MainLightShadowmapTexture", data.mainShadowsTexture);
                        if (data.mainLightCascadesCount > 1)
                        {
                            cmd.EnableShaderKeyword("_MAIN_LIGHT_SHADOWS_CASCADE");
                            cmd.DisableShaderKeyword("_MAIN_LIGHT_SHADOWS");
                        }
                        else
                        {
                            cmd.EnableShaderKeyword("_MAIN_LIGHT_SHADOWS");
                            cmd.DisableShaderKeyword("_MAIN_LIGHT_SHADOWS_CASCADE");
                        }
                        cmd.EnableShaderKeyword("_SHADOWS_SOFT");
                    }
                    else
                    {
                        cmd.DisableShaderKeyword("_MAIN_LIGHT_SHADOWS");
                        cmd.DisableShaderKeyword("_MAIN_LIGHT_SHADOWS_CASCADE");
                    }

                    if (data.additionalShadowsTexture.IsValid())
                    {
                        cmd.SetGlobalTexture("_AdditionalLightsShadowmapTexture", data.additionalShadowsTexture);
                        cmd.EnableShaderKeyword("_ADDITIONAL_LIGHT_SHADOWS");
                    }
                    else
                    {
                        cmd.DisableShaderKeyword("_ADDITIONAL_LIGHT_SHADOWS");
                    }

                    cmd.SetComputeVectorParam(data.computeShader, "_GridSize", data.gridSize);
                    cmd.SetComputeFloatParam(data.computeShader, "_NearClip", data.nearClip);
                    cmd.SetComputeFloatParam(data.computeShader, "_MaxDist", data.maxDist);
                    cmd.SetComputeFloatParam(data.computeShader, "_FogDensity", data.density);
                    cmd.SetComputeVectorParam(data.computeShader, "_GlobalAmbientColor", data.ambientColor);
                    cmd.SetComputeFloatParam(data.computeShader, "_Anisotropy", data.anisotropy);
                    cmd.SetComputeMatrixParam(data.computeShader, "_InverseViewProjMatrix", data.invViewProj);
                    cmd.SetComputeVectorParam(data.computeShader, "_CameraPosWS", data.cameraPosWS);
                    cmd.SetComputeFloatParam(data.computeShader, "_JitterOffset", data.jitterOffset);
                    cmd.SetComputeIntParam(data.computeShader, "_LightCount", data.lightCount);
                    cmd.SetComputeBufferParam(data.computeShader, _injectKernel, "_LightBuffer", data.lightBuffer);

                    // [핵심] Read/Write 충돌(Hazard) 방지
                    cmd.SetComputeTextureParam(data.computeShader, _injectKernel, "_FroxelVolumeWrite", data.froxelVolumeInject);
                    cmd.DispatchCompute(data.computeShader, _injectKernel,
                        Mathf.CeilToInt(data.gridSize.x / 8f), Mathf.CeilToInt(data.gridSize.y / 8f), Mathf.CeilToInt(data.gridSize.z / 4f));

                    cmd.SetComputeTextureParam(data.computeShader, _accumulateKernel, "_FroxelVolumeRead", data.froxelVolumeInject);
                    cmd.SetComputeTextureParam(data.computeShader, _accumulateKernel, "_FroxelVolumeWrite", data.froxelVolumeScattering);
                    cmd.DispatchCompute(data.computeShader, _accumulateKernel,
                        Mathf.CeilToInt(data.gridSize.x / 8f), Mathf.CeilToInt(data.gridSize.y / 8f), 1);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Froxel_Composition_Pass", out var passData))
            {
                passData.compMaterial = _settings.compositionMaterial;
                passData.froxelVolumeScattering = froxelScatterHandle; // 산란 텍스처 사용
                passData.activeColor = resourceData.activeColorTexture;
                passData.nearClip = safeNearClip;
                passData.maxDist = safeMaxDist;

                builder.UseTexture(passData.froxelVolumeScattering, AccessFlags.Read);
                builder.SetRenderAttachment(passData.activeColor, 0);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    data.compMaterial.SetTexture("_FroxelVolume", data.froxelVolumeScattering);
                    data.compMaterial.SetFloat("_MaxDist", data.maxDist);
                    data.compMaterial.SetFloat("_NearClip", data.nearClip);

                    context.cmd.DrawProcedural(Matrix4x4.identity, data.compMaterial, 0, MeshTopology.Triangles, 3);
                });
            }
        }
    }
}