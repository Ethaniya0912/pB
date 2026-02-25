using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

namespace CaveSystem
{
    /// <summary>
    /// Unity 6.0 (URP 17) Render Graph API를 사용하여
    /// 수만 개의 프롭을 단일 드로우 콜로 렌더링하는 커스텀 렌더러 피처입니다.
    /// </summary>
    public class CaveInstancedPropFeature : ScriptableRendererFeature
    {
        private CaveInstancedPropPass instancedPropPass;

        public override void Create()
        {
            instancedPropPass = new CaveInstancedPropPass();
            // 불투명 지형 렌더링 이후에 그리기
            instancedPropPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        // [URP 17 핵심] Render Graph에서는 구형 SetupRenderPasses를 통한 RTHandle 주입이 필요 없습니다.
        // UniversalResourceData.activeColorTexture (TextureHandle)를 직접 사용합니다.

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (CaveEcosystemManager.Instance == null || CaveEcosystemManager.Instance.PropDataDict.Count == 0)
                return;

            renderer.EnqueuePass(instancedPropPass);
        }
    }

    /// <summary>
    /// Render Graph 체제하에 실행되는 커스텀 렌더 패스입니다.
    /// 구형 Execute API 대신 RecordRenderGraph를 의무적으로 사용합니다.
    /// </summary>
    public class CaveInstancedPropPass : ScriptableRenderPass
    {
        // Render Graph에서 람다 캡처를 피하기 위한 패스 데이터 컨테이너
        private class PassData
        {
            public List<PropRenderData> activeProps = new List<PropRenderData>();
        }

        // [URP 17 핵심] 구형 Execute 대신 Render Graph API 사용
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (CaveEcosystemManager.Instance == null) return;

            // FrameData에서 현재 카메라 및 범용 리소스 획득
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // Scene 뷰 카메라나 미리보기 카메라인지 확인
            if (cameraData.cameraType == CameraType.Preview) return;

            string passName = "Cave GPU Instanced Props";

            // Render Graph에 래스터라이제이션(Rasterization) 렌더 패스 추가
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                // 1. 렌더 타겟 선언 (컴파일 에러 해결: RTHandle 대신 TextureHandle 사용)
                TextureHandle activeColor = resourceData.activeColorTexture;
                TextureHandle activeDepth = resourceData.activeDepthTexture;

                // 텍스처 핸들이 유효하지 않으면 패스 등록 취소
                if (!activeColor.IsValid() || !activeDepth.IsValid())
                    return;

                builder.SetRenderAttachment(activeColor, 0);
                builder.SetRenderAttachmentDepth(activeDepth, AccessFlags.Write);

                // 2. 패스 데이터 수집 (현재 렌더링해야 할 프롭 리스트)
                passData.activeProps.Clear();
                foreach (var kvp in CaveEcosystemManager.Instance.PropDataDict)
                {
                    if (kvp.Value.currentInstanceCount > 0 && kvp.Value.mesh != null && kvp.Value.material != null)
                    {
                        passData.activeProps.Add(kvp.Value);
                    }
                }

                if (passData.activeProps.Count == 0)
                    return; // 렌더링할 데이터가 없으면 그래프에서 제외

                // 3. RenderFunc 설정 (실제 커맨드 버퍼 기록)
                // URP 17의 최적화를 위해 외부 변수 캡처를 최소화하고, 주입된 passData만을 활용합니다.
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // 컴파일 에러 해결: CommandBuffer 대신 URP 17의 RasterCommandBuffer 사용
                    RasterCommandBuffer cmd = context.cmd;

                    foreach (var propData in data.activeProps)
                    {
                        // 극강의 성능: CPU가 1개씩 그리지 않고, GPU 버퍼를 직접 참조하여 10만 개를 1번의 DrawCall로 처리
                        cmd.DrawMeshInstancedIndirect(
                            propData.mesh,
                            0, // Submesh index
                            propData.material,
                            0, // Shader pass (0 = default)
                            propData.argsBuffer
                        );
                    }
                });
            }
        }
    }
}