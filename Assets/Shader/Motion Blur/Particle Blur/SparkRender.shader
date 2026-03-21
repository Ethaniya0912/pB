Shader "Custom/SparkRender_Procedural"
{
    Properties
    {
        _ShutterAngle ("Shutter Angle", Float) = 180.0
        _TargetFPS ("Target FPS", Float) = 60.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct SparkData
            {
                float4 positionAge;
                float4 velocityLife;
                float4 baseSize;
            };

            StructuredBuffer<SparkData> _SparkBuffer;

            CBUFFER_START(UnityPerMaterial)
                float _ShutterAngle;
                float _TargetFPS;
            CBUFFER_END

            void setup() {}

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float lifeRatio : TEXCOORD0; 
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                uint instanceID = UNITY_GET_INSTANCE_ID(input);
                SparkData spark = _SparkBuffer[instanceID];
                
                float age = spark.positionAge.w;
                float lifetime = spark.velocityLife.w;
                float ratio = saturate(age / lifetime);
                output.lifeRatio = ratio;

                // Safely discard dead particles far behind the camera
                if (ratio >= 1.0)
                {
                    output.positionCS = float4(9999.0, 9999.0, 9999.0, 1.0);
                    return output;
                }

                // Billboard Math
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - spark.positionAge.xyz);
                float3 velocity = spark.velocityLife.xyz;
                float speed = length(velocity);
                
                float3 quadUp = speed > 0.001 ? (velocity / speed) : UNITY_MATRIX_I_V[1].xyz;
                float3 quadRight = cross(viewDir, quadUp);
                
                if (length(quadRight) < 0.001) {
                    quadRight = UNITY_MATRIX_I_V[0].xyz;
                } else {
                    quadRight = normalize(quadRight);
                }
                
                // Shutter Angle Stretching
                float exposureTime = (_ShutterAngle / 360.0) / max(_TargetFPS, 1.0);
                float stretchLength = speed * exposureTime;

                float thinFactor = saturate(1.0 - stretchLength * 0.5);
                float width = spark.baseSize.x * (0.5 + 0.5 * thinFactor);
                float height = spark.baseSize.y + stretchLength;

                float3 finalPos = spark.positionAge.xyz 
                                  + quadRight * (input.positionOS.x * width)
                                  + quadUp * (input.positionOS.y * height);

                output.positionCS = TransformWorldToHClip(finalPos);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 hotColor = float3(5.0, 5.0, 5.0);   
                float3 midColor = float3(3.0, 2.0, 0.1);   
                float3 coldColor = float3(1.0, 0.1, 0.0);  
                
                float3 finalColor = lerp(hotColor, midColor, saturate(input.lifeRatio * 2.0));
                finalColor = lerp(finalColor, coldColor, saturate((input.lifeRatio - 0.5) * 2.0));

                float alpha = 1.0 - input.lifeRatio;
                
                return float4(finalColor * alpha, 1.0); 
            }
            ENDHLSL
        }
    }
}