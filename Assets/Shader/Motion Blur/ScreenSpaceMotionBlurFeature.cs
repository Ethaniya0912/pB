using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// [Phase 2 — ScreenSpaceMotionBlurFeature]
/// URP Renderer Feature: 카메라 이동/회전 시 전체 화면에 적용되는 스크린스페이스 모션 블러.
///
/// ■ 설계 근거
///   카메라가 이동·회전할 때 화면 전체는 이동 반대 방향으로 흐릅니다.
///   이를 표현하는 방법에는 두 가지가 있습니다.
///   (A) 모션 벡터 버퍼 기반: URP가 자동 생성하는 _MotionVectorTexture를 읽어 픽셀별 이동 방향·속도를 얻습니다.
///   (B) VP 행렬 델타 기반: ShaderCoordinationManager가 주입하는 이전/현재 VP 행렬의 차이를 계산합니다.
///
///   이 구현은 (A) 모션 벡터 버퍼 기반을 사용합니다.
///   근거: URP의 _MotionVectorTexture는 카메라 이동과 오브젝트 이동을 모두 인코딩합니다.
///   Phase 3의 ObjectMotionBlur는 깊이 기반 마스킹으로 오브젝트 픽셀을 제외하므로,
///   버퍼를 그대로 읽어도 카메라 성분이 주로 배경에 적용됩니다.
///   VP 행렬 방식(B)은 더 순수한 카메라 성분을 추출하지만 뎁스 샘플 비용이 추가됩니다.
///   Forward+ 환경에서 버퍼 읽기 비용이 더 저렴하므로 (A)를 선택합니다.
///
/// ■ URP 6.3 Render Graph 패턴 준수
///   이 Feature는 기존 DreamcoreLUTRendererFeature.cs의 RecordRenderGraph 패턴을
///   동일하게 따릅니다. 핵심 규칙:
///   1. RecordRenderGraph() 진입 시 모든 TextureHandle을 로컬 변수로 즉시 추출
///   2. 람다(SetRenderFunc) 내부에서 외부 변수를 캡처하지 않음 (PassData에 복사)
///   3. 임시 버퍼 생성 → 효과 적용(src→tmp) → 복사(tmp→src)의 2패스 구조
///
/// ■ 기존 DepthBlur.shader와의 관계
///   기존 DepthBlur.shader에 있는 _USE_MOTION_BLUR 토글을 OFF(0)로 설정하면
///   peripheral motion blur 블록이 비활성화됩니다. 이 Feature가 그 역할을 대체합니다.
///   두 시스템이 동시에 활성화되면 블러가 이중 적용되므로 반드시 하나만 활성화하세요.
///
/// ■ 효과
///   - 카메라 이동 방향과 정확히 일치하는 잔상 효과
///   - 셔터앵글 물리 공식 기반으로 FPS가 낮을수록 블러가 강해지는 자연스러운 반응
///   - 깊이 마스킹으로 가까운 오브젝트는 제외 → Phase 3의 오브젝트 블러와 분업
///   - 12샘플 누적으로 밴딩(줄무늬) 없는 부드러운 블러
///
/// ■ 사용 방법
///   1. URP Renderer Asset의 Renderer Feature 목록에 이 Feature 추가
///   2. Material 슬롯에 ScreenSpaceMotionBlur.mat (Hidden/Dreamcore/ScreenSpaceMotionBlur) 할당
///   3. DepthBlur.shader 머티리얼의 _UseMotionBlur 토글을 OFF로 변경
///   4. ShaderCoordinationManager의 shutterAngle 값으로 강도 조절 (기본 180도)
/// </summary>
public class ScreenSpaceMotionBlurFeature : ScriptableRendererFeature
{
    // ─────────────────────────────────────────────────────────────
    // Settings
    // ─────────────────────────────────────────────────────────────
    [System.Serializable]
    public class Settings
    {
        [Tooltip("블러 셰이더가 적용된 머티리얼. Hidden/Dreamcore/ScreenSpaceMotionBlur 셰이더 사용.")]
        public Material material;

        [Tooltip(
            "블러 샘플 수. 높을수록 부드럽지만 GPU 비용 증가.\n" +
            "권장: 8 (성능 우선) ~ 16 (품질 우선). 기본값 12는 양쪽의 균형점.")]
        [Range(4, 16)]
        public int samples = 12;

        [Tooltip(
            "이 Feature를 실행할 렌더 이벤트 타이밍.\n" +
            "BeforeRenderingPostProcessing: 톤맵 전. 색상 정보가 선형이므로 더 정확합니다.\n" +
            "AfterRenderingPostProcessing: 톤맵 후. 최종 화면에 가장 가까운 결과.")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Tooltip("씬 뷰에도 블러를 적용할지 여부. 개발 중에는 false 권장.")]
        public bool applyInSceneView = false;
    }

    public Settings settings = new Settings();

    private SSMBPass _pass;

    // ─────────────────────────────────────────────────────────────
    // ScriptableRendererFeature 인터페이스
    // ─────────────────────────────────────────────────────────────
    public override void Create()
    {
        _pass = new SSMBPass(settings);
        _pass.renderPassEvent = settings.passEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null)
        {
            Debug.LogWarning("[ScreenSpaceMotionBlurFeature] Material이 할당되지 않았습니다.", this);
            return;
        }

        var camType = renderingData.cameraData.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection) return;
        if (camType == CameraType.SceneView && !settings.applyInSceneView) return;

        // [중요] Motion 플래그: URP가 _MotionVectorTexture를 생성하도록 요청
        // Color + Depth도 함께 요청해야 블랙스크린 방지
        _pass.ConfigureInput(
            ScriptableRenderPassInput.Color   |
            ScriptableRenderPassInput.Depth   |
            ScriptableRenderPassInput.Motion
        );

        renderer.EnqueuePass(_pass);
    }

    // ─────────────────────────────────────────────────────────────
    // Inner Pass
    // ─────────────────────────────────────────────────────────────
    private class SSMBPass : ScriptableRenderPass
    {
        private readonly Settings _settings;

        // 셰이더 파라미터 IDs
        // [Phase 0 ShaderCoordinationManager와 공유하는 전역 파라미터]
        //   _ShutterAngle, _TargetFPS, _SSMBIntensity, _SSBlurDepthCutoff
        //   → ShaderCoordinationManager.cs가 매 프레임 Shader.SetGlobal*으로 주입
        //   → 이 Feature는 읽기만 합니다. 여기서 SetGlobal 하지 마세요.
        //
        // [이 Feature 전용 파라미터]
        //   _SSMBSamples → Inspector 설정값, 매 프레임 Material에 설정
        private static readonly int SamplesID = Shader.PropertyToID("_SSMBSamples");

        public SSMBPass(Settings s) { _settings = s; }

        // PassData: 람다 내부에서 사용할 데이터를 담는 컨테이너.
        // RecordRenderGraph()의 람다는 실제 실행 시점이 다르므로
        // 람다가 외부 변수를 직접 캡처하면 null 참조나 크래시가 발생합니다.
        // (MultiLayerLUTIntegratedFeature.cs의 '크래시 방지 규칙'과 동일한 원칙)
        private class PassData
        {
            public Material      mat;
            public TextureHandle src;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // ── [규칙] 모든 핸들은 RecordRenderGraph 진입 즉시 추출 ──
            var resources = frameData.Get<UniversalResourceData>();
            var camData   = frameData.Get<UniversalCameraData>();

            TextureHandle src = resources.activeColorTexture;
            if (!src.IsValid()) return;

            // 임시 버퍼 Descriptor: MSAA 없이 단일 샘플 HDR 포맷
            // MSAA를 켜면 Blit 시 resolve 비용이 추가됩니다.
            var desc = camData.cameraTargetDescriptor;
            desc.msaaSamples      = 1;
            desc.depthBufferBits  = 0;

            TextureHandle tmp = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SSMB_Temp", true);

            // Material에 샘플 수 설정 (전역 파라미터가 아닌 Material 파라미터)
            _settings.material.SetFloat(SamplesID, _settings.samples);

            // ── Pass 1: 블러 적용 (src → tmp) ───────────────────────
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Dreamcore_SSMB_Apply", out var passData))
            {
                passData.mat = _settings.material;
                passData.src = src;

                builder.UseTexture(src, AccessFlags.Read);
                builder.SetRenderAttachment(tmp, 0, AccessFlags.Write);

                // 깊이 텍스처: 깊이 마스킹에 필요 (가까운 오브젝트 제외)
                if (resources.cameraDepthTexture.IsValid())
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);

                // 모션 벡터 버퍼: 픽셀별 이동 방향/속도 읽기
                if (resources.motionVectorColor.IsValid())
                    builder.UseTexture(resources.motionVectorColor, AccessFlags.Read);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), data.mat, 0));
            }

            // ── Pass 2: 복사 (tmp → src) ─────────────────────────────
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Dreamcore_SSMB_CopyBack", out var passData))
            {
                passData.src = tmp;

                builder.UseTexture(tmp, AccessFlags.Read);
                builder.SetRenderAttachment(src, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false));
            }
        }
    }
}
