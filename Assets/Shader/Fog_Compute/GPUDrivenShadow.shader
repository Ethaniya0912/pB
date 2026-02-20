Shader "Hidden/Dreamcore/GPUDrivenShadow"
{
    Properties
    {
        _MainColor ("Color", Color) = (1,1,1,1)
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        // [핵심 Fix 3] 유령 실린더 소멸!
        // 화면에 하얀색 프록시 메쉬(큐브/실린더)를 겹쳐 그리던 범인인 ForwardLit 패스를 통째로 삭제했습니다.
        // 이제 이 셰이더는 오직 바닥과 벽에 맺히는 그림자(ShadowCaster)로만 작동합니다.
        
        // [Shadow Caster Pass]
        // URP가 그림자 맵을 그릴 때 호출하는 패스입니다. 이 패스가 있어야 2,200개 물체가 그림자를 던집니다.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ColorMask 0 
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // [Fix] URP 섀도우 캐스터에서 요구하는 빛 방향 전역 변수 수동 선언
            float3 _LightDirection;

            struct InstanceData
            {
                float4x4 localToWorld;
                float3 boundsCenter;
                float boundsRadius;
            };

            StructuredBuffer<InstanceData> _InstanceDataBuffer;
            StructuredBuffer<uint> _VisibleIndexBuffer;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                uint actualIndex = _VisibleIndexBuffer[input.instanceID];
                InstanceData data = _InstanceDataBuffer[actualIndex];

                float3 positionWS = mul(data.localToWorld, float4(input.positionOS.xyz, 1.0)).xyz;
                float3 normalWS = normalize(mul(input.normalOS, (float3x3)data.localToWorld));

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return 0; 
            }
            ENDHLSL
        }
    }
}