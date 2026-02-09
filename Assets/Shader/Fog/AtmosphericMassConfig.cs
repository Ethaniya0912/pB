using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dreamcore.Atmosphere
{
    /// <summary>
    /// [Atmospheric Library] 
    /// 렌더러 피처의 설정값, 상수, 열거형 데이터를 관리하는 라이브러리 파일입니다.
    /// 메인 로직(AtmosphericMassRendererFeature)의 부하를 줄이기 위해 분리되었습니다.
    /// </summary>
    public static class AtmosphericMassConfig
    {
        public static class Constants
        {
            public const int MaxShaderLights = 64;
            // 볼류메트릭 그림자 구현을 위한 핵심 셰이더 프로퍼티 ID
            public static readonly int MainLightShadowmapID = Shader.PropertyToID("_MainLightShadowmapTexture");
            // 추가적인 그림자 데이터 바인딩 (갓레이 정밀도 향상)
            public static readonly int MainLightShadowParamsID = Shader.PropertyToID("_MainLightShadowParams");
        }

        // 디버깅 모드: 셰이더 내부 연산의 무결성을 시각적으로 확인
        public enum DebugMode
        {
            None = 0,           // 정상 렌더링
            OnlyFog = 1,        // 배경 제외, 안개 성분만 출력 (알파 채널 확인)
            DepthBuffer = 2,    // 깊이 버퍼 시각화 (0~1)
            WorldPosition = 3,  // 복원된 월드 좌표 시각화 (좌표 흔들림 검증)
            StepComplexity = 4, // 레이마칭 단계 부하 시각화 (성능 체크)
            NoiseCheck = 5,     // 지터링 노이즈 패턴 확인
            ShadowCheck = 6,    // 그림자 감쇠 상태 확인 (God Ray 생성 근거 확인용)
            LightVolume = 7     // 수동 수집된 광원의 유효 범위 확인 (컬링 방지 확인용)
        }

        [System.Serializable]
        public class FeatureSettings
        {
            [Header("Shader Reference")]
            [Tooltip("반드시 'Hidden/Dreamcore/ChunkyAtmosphere' 셰이더를 할당해야 합니다.")]
            public Shader atmosphericShader;
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            [Header("Performance Settings")]
            [Tooltip("해상도 다운샘플링 비율입니다. (1=원본, 2=1/2해상도)")]
            [Range(1, 4)] public int downsample = 2;
            [Tooltip("레이마칭 반복 횟수입니다. 높을수록 품질이 좋아지지만 GPU 부하가 매우 커집니다.")]
            [Range(16, 256)] public int stepCount = 64;
            [Tooltip("안개 렌더링 최대 거리입니다.")]
            public float maxDistance = 150f;

            [Header("Light Stability (Anti-Culling)")]
            [Tooltip("카메라 시야 밖의 광원이 꺼지는 현상을 방지하기 위해 직접 광원을 수집합니다.")]
            public bool useManualLightCollection = true;
            [Tooltip("수동으로 수집할 최대 광원 개수입니다. (Shader 배열 크기: 64)")]
            [Range(1, 64)] public int maxExtraLights = 32;

            [Header("Atmosphere Visuals")]
            [ColorUsage(false, true)]
            public Color ambientColor = new Color(0.02f, 0.02f, 0.05f, 1.0f);
            [Tooltip("빛이 산란되는 강도입니다. God Ray의 밝기를 결정합니다.")]
            public float lightScatterMult = 2.0f;
            [Tooltip("전체적인 안개의 밀도입니다.")]
            [Range(0f, 1f)] public float fogDensity = 0.05f;
            [Tooltip("빛의 산란 방향성(Anisotropy G값). 양수=전방산란, 음수=후방산란.")]
            [Range(-1f, 1f)] public float anisotropy = 0.5f;

            [Header("Shadow & Contrast")]
            [Tooltip("그림자의 경계 선명도(대비)를 조절합니다. 값이 높을수록 빛 가닥이 뚜렷해집니다.")]
            [Range(1f, 20f)] public float shadowContrast = 1.0f;
            [Tooltip("그림자로 판정할 밝기 기준값입니다. 0.5가 기본이며, 낮추면 그림자 영역이 넓어집니다.")]
            [Range(0.01f, 0.99f)] public float shadowThreshold = 0.5f;

            [Header("Stylization")]
            [Tooltip("빛의 감쇄를 단계적으로 끊어 만화/레트로 느낌을 줍니다.")]
            [Range(1f, 64f)] public float lightQuantization = 16.0f;
            [Tooltip("빛의 색상을 재매핑할 Ramp Texture입니다.")]
            public Texture2D rampTexture;
            [Tooltip("Ramp Texture 적용 강도입니다.")]
            [Range(0f, 1f)] public float rampStrength = 0.0f;

            [Header("Jitter & Noise")]
            [Tooltip("레이마칭 밴딩 현상을 제거하기 위한 노이즈 텍스처입니다.")]
            public Texture2D blueNoiseTexture;
            [Range(0f, 2f)] public float jitterStrength = 1.0f;

            [Header("Debugging Tool")]
            [Tooltip("렌더링 연산 단계를 확인하기 위한 디버깅 툴입니다.")]
            public DebugMode debugMode = DebugMode.None;
        }
    }
}