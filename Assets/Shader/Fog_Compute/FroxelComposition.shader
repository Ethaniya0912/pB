Shader "Hidden/Dreamcore/FroxelComposition"
{
    Properties
    {
        _FroxelVolume ("Froxel Volume", 3D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "FroxelComposition"
            ZWrite Off
            ZTest Always
            Cull Off
            
            // Additive 블렌딩을 통해 기존 배경 위에 안개(빛)를 덧씌우고, 
            // SrcAlpha를 통해 빛이 통과하지 못하는 투과율(Transmittance)을 조절합니다.
            Blend One SrcAlpha 

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // 3D 전용 텍스처와 샘플러 명시적 선언
            TEXTURE3D(_FroxelVolume);
            SAMPLER(sampler_FroxelVolume);

            float _NearClip;
            float _MaxDist;

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 풀스크린 삼각형(Full-screen Triangle) 생성 알고리즘
                output.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
                
                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
                #endif
                
                return output;
            }

            // 고주파 노이즈 생성기 (디더링용)
            float InterleavedGradientNoise(float2 uv)
            {
                return frac(52.9829189 * frac(dot(uv, float2(0.06711056, 0.00583715))));
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 화면의 진짜 깊이 값(Depth)을 가져옵니다.
                float rawDepth = SampleSceneDepth(input.uv);
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // [핵심 Fix 1] 스카이박스 판별
                // URP의 표준 Z-버퍼 방식에 따라 스카이박스(무한대 깊이) 여부를 확인합니다.
                #if UNITY_REVERSED_Z
                bool isSkybox = rawDepth < 0.00001;
                #else
                bool isSkybox = rawDepth > 0.99999;
                #endif

                // 안개 최대 거리를 넘어서는 깊이는 잘라냅니다.
                float z = min(linearDepth, _MaxDist);

                // 깊이 값을 3D 텍스처의 Z축 샘플링 좌표(0~1)로 역변환합니다.
                float normalizedZ = log(z / max(_NearClip, 0.001)) / log(max(_MaxDist / max(_NearClip, 0.001), 1.001));
                normalizedZ = saturate(normalizedZ);

                // [핵심 Fix 2] 조건부 공간 디더링 (깍두기 파괴 및 잔상 방어)
                float noise = InterleavedGradientNoise(input.positionCS.xy) - 0.5;
                
                // 프록셀 해상도(160x90x64)의 딱 1텍셀 크기 수준으로만 미세하게 오프셋을 줍니다.
                float2 uvOffset = float2(noise * 0.006, noise * 0.011);
                float zOffset = noise * 0.015;

                float3 uvw;
                
                if (isSkybox)
                {
                    // 스카이박스 영역은 노이즈 왜곡을 주지 않고 깔끔하게 1.0(가장 짙은 끝단)을 샘플링합니다.
                    uvw = float3(input.uv, 1.0);
                }
                else
                {
                    // 카메라 바로 앞(Near)에서 캐릭터나 물체의 실루엣이 노이즈로 인해 떨려보이는 '잔상' 현상을 막기 위해,
                    // 거리가 멀어질수록 노이즈 강도가 강해지도록 조절합니다.
                    float distanceFade = smoothstep(0.0, 0.1, normalizedZ); 
                    uvw = float3(input.uv + uvOffset * distanceFade, saturate(normalizedZ + zOffset * distanceFade));
                }

                // 3D 텍스처 샘플링 
                half4 froxelData = SAMPLE_TEXTURE3D_LOD(_FroxelVolume, sampler_FroxelVolume, uvw, 0);

                // [핵심 Fix 3] 스카이박스 솔리드 컬러 차단
                if (isSkybox)
                {
                    // 픽셀이 스카이박스라면 투과율(A)을 강제로 0.0으로 만들어 뒷배경을 지워버리고 포그 색상만 남깁니다.
                    froxelData.a = 0.0;
                }

                return froxelData;
            }
            ENDHLSL
        }
    }
}