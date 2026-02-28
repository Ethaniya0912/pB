Shader "CaveSystem/CaveTerrain"
{
    Properties
    {
        // [🔥 매터리얼 None 에러 수정 완료] 괄호() 문법 오류 제거
        [Header(Textures RGB Albedo and A Roughness)]
        _DirtAlbedo ("Dirt Albedo", 2D) = "white" {}
        _RockAlbedo ("Rock Albedo", 2D) = "grey" {}
        _MossAlbedo ("Moss Albedo", 2D) = "green" {}
        
        [Header(Normals)]
        _DirtNormal ("Dirt Normal", 2D) = "bump" {}
        _RockNormal ("Rock Normal", 2D) = "bump" {}

        [Header(Settings)]
        _Tiling ("Triplanar Tiling", Float) = 0.1
        _WaterLevel ("Water Level", Float) = -10.0
        _WetHeight ("Wet Height Margin", Float) = 2.0
        _WetDarken ("Wet Darken Multiplier", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _WaterLevel;
                float _WetHeight;
                float _WetDarken;
            CBUFFER_END

            #include "CaveTriplanarSplat.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 albedo = 0;
                float3 normal = 0;
                float4 mohr = 0; 

                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);

                GetCaveSurfaceData(
                    input.positionWS, normalize(input.normalWS), viewDirWS, 
                    _Tiling, 
                    0.05, // heightScale
                    1.0,  // normalScale (이 인자가 누락되어 에러가 발생했습니다!)
                    0.0,  // enablePomFading
                    15.0, // pomFadeStart
                    25.0, // pomFadeEnd
                    albedo, normal, mohr
                );

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(normal);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = saturate(mohr.r); 
                surfaceData.smoothness = 1.0 - saturate(mohr.a);
                surfaceData.alpha = 1.0;
                surfaceData.occlusion = saturate(mohr.g);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                return color;
            }
            ENDHLSL
        }
    }
}