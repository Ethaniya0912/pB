/*
    ================================================================================
    [File]          MultiLayerLUTIntegratedFeature.cs
    [Role]          Integrated Render Feature - 드림코어 표준 v10.5 (Final Resolution)
    [Version]       10.5 (Strict Property Isolation & Zero-Capture)
    [Last Updated]  2026.02.12
    [Description]   
        - [Fixed] 'Trying to access Universal Resources outside frame setup' 해결 (프로퍼티 접근 차단).
        - [Fixed] Blit 시 'ArgumentNullException' 해결 (핸들 사전 추출 및 유효성 검증).
        - [Rules] 트러블슈팅 매트릭스의 모든 시스템 규칙을 100% 준수합니다.
    ================================================================================
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public class MultiLayerLUTIntegratedFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class IDLayerSettings
    {
        public string passName = "Layer Section";
        public LayerMask layerMask;
        [ColorUsage(false)] public Color outputColor = Color.red;
    }

    [Header("1. Generation (ID Map)")]
    public Material overrideMaterial;
    public List<IDLayerSettings> renderLayers = new List<IDLayerSettings>();

    [Header("2. Composition (Flesh)")]
    public Material compositeMaterial;
    public Texture2D neutralLUT;
    public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

    [Header("Debug Options")]
    public bool debugViewIDMap = false;

    private StandardIntegratedPass _mainPass;

    public override void Create()
    {
        if (overrideMaterial == null || compositeMaterial == null) return;
        _mainPass = new StandardIntegratedPass(this);
        _mainPass.renderPassEvent = passEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_mainPass != null) renderer.EnqueuePass(_mainPass);
    }

    class StandardIntegratedPass : ScriptableRenderPass
    {
        private readonly MultiLayerLUTIntegratedFeature _owner;
        private readonly Dictionary<int, Material> _mats = new Dictionary<int, Material>();

        private static readonly int IDMapID = Shader.PropertyToID("_DreamcoreIDMap_Internal");
        private static readonly int IDColorID = Shader.PropertyToID("_IDColor");
        private static readonly int NeutralLUTID = Shader.PropertyToID("_DefaultNeutralLUT");

        public StandardIntegratedPass(MultiLayerLUTIntegratedFeature owner) { _owner = owner; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // [매트릭스 규칙 1] Record 단계에서 필요한 모든 데이터를 로컬 변수로 즉시 추출합니다.
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

            // 실행 시점에 resources.activeColorTexture (Property)를 호출하면 크래시가 나므로 여기서 핸들을 뽑습니다.
            TextureHandle cameraColor = resources.activeColorTexture;
            TextureHandle cameraDepth = resources.activeDepthTexture;

            if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView) return;
            if (!cameraColor.IsValid()) return;

            // 1-Sample HDR 격리 설정 (MSAA 오염 방지)
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

            TextureHandle idMap = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_IDMap_Buffer", false);
            TextureHandle tempColor = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_Flesh_Buffer", false);

            // [Pass 1] ID Map 생성
            using (var builder = renderGraph.AddRasterRenderPass<IDPassData>("Dreamcore_ID_Gen", out var passData))
            {
                builder.SetRenderAttachment(idMap, 0, AccessFlags.Write);
                if (cameraDepth.IsValid()) builder.SetRenderAttachmentDepth(cameraDepth, AccessFlags.Read);
                builder.AllowPassCulling(false);

                passData.rendererLists = new List<RendererListHandle>();
                for (int i = 0; i < _owner.renderLayers.Count; i++)
                {
                    var layer = _owner.renderLayers[i];
                    if (!_mats.TryGetValue(i, out Material m) || m == null)
                    {
                        m = CoreUtils.CreateEngineMaterial(_owner.overrideMaterial.shader);
                        _mats[i] = m;
                    }
                    m.SetColor(IDColorID, layer.outputColor);

                    var rlParams = new RendererListParams(renderingData.cullResults, new DrawingSettings(new ShaderTagId("UniversalForward"), new SortingSettings(cameraData.camera)) { overrideMaterial = m }, new FilteringSettings(RenderQueueRange.opaque, layer.layerMask));
                    var rl = renderGraph.CreateRendererList(rlParams);
                    builder.UseRendererList(rl);
                    passData.rendererLists.Add(rl);
                }

                builder.SetRenderFunc((IDPassData data, RasterGraphContext context) => {
                    context.cmd.ClearRenderTarget(true, true, Color.black);
                    foreach (var h in data.rendererLists) if (h.IsValid()) context.cmd.DrawRendererList(h);
                });
            }

            // [Pass 2] 드림코어 합성 (Zero-Capture & Property Isolation ★)
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Dreamcore_Composite", out var passData))
            {
                // [중요] 람다 내부에서 외부 객체(resources, _owner 등)를 절대 참조하지 않도록 PassData에 모든 값을 복사합니다.
                passData.source = cameraColor;
                passData.idMap = idMap;
                passData.material = _owner.compositeMaterial;
                passData.neutralLUT = _owner.neutralLUT;
                passData.debugViewIDMap = _owner.debugViewIDMap;

                // 의존성 신고
                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(passData.idMap, AccessFlags.Read);
                builder.SetRenderAttachment(tempColor, 0, AccessFlags.Write);

                builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) => {
                    // [에러 해결] 이 블록 안에서 'resources'나 'tempColor'를 타이핑하는 행위 자체가 금지됩니다.
                    // 오직 'data' 파라미터만 사용하여 유효성을 검사하고 실행합니다.
                    if (!data.source.IsValid() || !data.idMap.IsValid() || data.material == null) return;

                    data.material.SetTexture(IDMapID, data.idMap);
                    if (data.neutralLUT != null) data.material.SetTexture(NeutralLUTID, data.neutralLUT);

                    if (data.debugViewIDMap) data.material.EnableKeyword("_DEBUG_VIEW_IDMAP");
                    else data.material.DisableKeyword("_DEBUG_VIEW_IDMAP");

                    // Blitter는 셰이더의 _BlitTexture를 자동으로 채웁니다.
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            // [Pass 3] 최종 결과 복사
            using (var builder = renderGraph.AddRasterRenderPass<FinalBlitPassData>("Dreamcore_Final_Blit", out var passData))
            {
                passData.source = tempColor;
                passData.destination = cameraColor;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);

                builder.SetRenderFunc((FinalBlitPassData data, RasterGraphContext context) => {
                    if (!data.source.IsValid() || !data.destination.IsValid()) return;
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }
        }

        // --- 데이터 격리 보관함 (Struct-like Class) ---
        private class IDPassData { public List<RendererListHandle> rendererLists; }
        private class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle idMap;
            public Material material;
            public Texture2D neutralLUT;
            public bool debugViewIDMap;
        }
        private class FinalBlitPassData
        {
            public TextureHandle source;
            public TextureHandle destination;
        }
    }
}