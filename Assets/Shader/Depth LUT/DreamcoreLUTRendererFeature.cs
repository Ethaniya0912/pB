using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// 유니티 6.3 URP 및 Render Graph 전용 Dreamcore LUT 렌더 피처
/// </summary>
public class DreamcoreLUTRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Material material;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public bool applyInSceneView = true;
    }

    public Settings settings = new Settings();
    private DreamcoreLUTPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new DreamcoreLUTPass(settings.material);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null) return;

        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;
        if (cameraType == CameraType.SceneView && !settings.applyInSceneView) return;

        // 중요: 셰이더에서 깊이 텍스처를 사용하므로 명시적으로 선언해야 블랙 스크린을 방지합니다.
        m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        m_ScriptablePass.renderPassEvent = settings.renderPassEvent;

        renderer.EnqueuePass(m_ScriptablePass);
    }

    class DreamcoreLUTPass : ScriptableRenderPass
    {
        private Material m_Material;

        public DreamcoreLUTPass(Material material)
        {
            m_Material = material;
        }

        private class PassData
        {
            public Material material;
            public TextureHandle source;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle src = resourceData.activeColorTexture;
            if (!src.IsValid()) return;

            // 1. 임시 버퍼 생성
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // 이펙트용이므로 깊이 버퍼는 불필요
            TextureHandle dst = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_DreamcoreTemp", true);

            // 2. [Pass 1] 효과 적용 (src -> dst)
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("DreamcoreEffectPass", out var passData))
            {
                passData.material = m_Material;
                passData.source = src;

                builder.UseTexture(src, AccessFlags.Read);
                builder.SetRenderAttachment(dst, 0, AccessFlags.Write);

                // 깊이 텍스처 읽기 권한 확보
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // Blitter.BlitTexture 사용 시 셰이더의 _BlitTexture에 자동으로 소스가 할당됩니다.
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            // 3. [Pass 2] 다시 카메라 텍스처로 복사 (dst -> src)
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("DreamcoreCopyBack", out var passData))
            {
                passData.source = dst;

                builder.UseTexture(dst, AccessFlags.Read);
                builder.SetRenderAttachment(src, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // 기본 복사 셰이더 (0번 패스) 사용
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }
}