using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

public class MultiLayerStencilFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class SimplePassSettings
    {
        public string label = "Player Effect Group";
        public RenderPassEvent renderEvent = RenderPassEvent.AfterRenderingOpaques;
        public LayerMask layerMask = -1;
        [Range(0, 255)]
        public int stencilReference = 1;
        public Material overrideMaterial;
        public int materialPassIndex = 0;
    }

    public List<SimplePassSettings> effects = new List<SimplePassSettings>();
    private List<CombinedStencilPass> _passes = new List<CombinedStencilPass>();

    public override void Create()
    {
        _passes.Clear();
        if (effects == null) return;

        foreach (var setting in effects)
        {
            _passes.Add(new CombinedStencilPass(setting));
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        foreach (var pass in _passes)
        {
            renderer.EnqueuePass(pass);
        }
    }

    class CombinedStencilPass : ScriptableRenderPass
    {
        private SimplePassSettings _s;
        private FilteringSettings _filter;
        private List<ShaderTagId> _tags = new List<ShaderTagId>();
        private RenderStateBlock _writeState;
        private RenderStateBlock _applyState;
        private ProfilingSampler _profilingSampler;

        public CombinedStencilPass(SimplePassSettings settings)
        {
            _s = settings;
            this.renderPassEvent = settings.renderEvent;
            _profilingSampler = new ProfilingSampler(settings.label);

            _filter = new FilteringSettings(RenderQueueRange.all, settings.layerMask);

            // URP 표준 패스 태그들
            _tags.Add(new ShaderTagId("UniversalForward"));
            _tags.Add(new ShaderTagId("UniversalForwardOnly"));
            _tags.Add(new ShaderTagId("SRPDefaultUnlit"));
            _tags.Add(new ShaderTagId("LightweightForward"));

            // [1단계] 원본 드로우 및 스텐실 기록 (Replace)
            _writeState = new RenderStateBlock(RenderStateMask.Stencil | RenderStateMask.Depth);
            _writeState.stencilReference = _s.stencilReference;
            _writeState.stencilState = new StencilState(
                enabled: true,
                readMask: 255,
                writeMask: 255,
                compareFunction: CompareFunction.Always,
                passOperation: StencilOp.Replace,
                failOperation: StencilOp.Keep,
                zFailOperation: StencilOp.Keep
            );
            _writeState.depthState = new DepthState(true, CompareFunction.LessEqual);

            // [2단계] 효과 메터리얼 덮어쓰기 (Equal)
            _applyState = new RenderStateBlock(RenderStateMask.Stencil | RenderStateMask.Depth);
            _applyState.stencilReference = _s.stencilReference;
            _applyState.stencilState = new StencilState(
                enabled: true,
                readMask: 255,
                writeMask: 255,
                compareFunction: CompareFunction.Equal,
                passOperation: StencilOp.Keep,
                failOperation: StencilOp.Keep,
                zFailOperation: StencilOp.Keep
            );
            _applyState.depthState = new DepthState(false, CompareFunction.Always);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 씬 뷰의 프리뷰 카메라 등 불필요한 렌더링 제외
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview) return;

            CommandBuffer cmd = CommandBufferPool.Get();

            // 프레임 디버거에서 패스 그룹 이름으로 표시됨
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // 정렬 기준 설정
                var sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;

                // --- 1회차 호출: 스텐실 기록 ---
                DrawingSettings drawSettingsWrite = CreateDrawingSettings(_tags, ref renderingData, sortingCriteria);
                context.DrawRenderers(renderingData.cullResults, ref drawSettingsWrite, ref _filter, ref _writeState);

                // --- 2회차 호출: 메터리얼 오버라이드 ---
                if (_s.overrideMaterial != null)
                {
                    DrawingSettings drawSettingsApply = CreateDrawingSettings(_tags, ref renderingData, sortingCriteria);
                    drawSettingsApply.overrideMaterial = _s.overrideMaterial;
                    drawSettingsApply.overrideMaterialPassIndex = _s.materialPassIndex;

                    context.DrawRenderers(renderingData.cullResults, ref drawSettingsApply, ref _filter, ref _applyState);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}