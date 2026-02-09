Shader "Hidden/Dreamcore/EdgePreservingBleed"
{
    Properties 
    { 
        _MainTex("Source", 2D) = "white" {} 
        _FogTex("Fog Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            struct Varyings 
            { 
                float4 positionCS : SV_POSITION; 
                float2 uv : TEXCOORD0; 
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_FogTex); SAMPLER(sampler_FogTex);
            
            // --- [통합 데이터 영역] C# 매니저와 피처에서 주입되는 변수들 ---
            float _Threshold, _EdgeStrength;
            float4 _BleedColor;
            int _DebugMode;
            float3 _FogZoneMin, _FogZoneMax;
            float4x4 _InverseVP; 

            /**
             * [Procedural Vertex Shader]
             * 메쉬 데이터 없이 정점 ID만으로 화면 전체를 채우는 삼각형을 생성합니다.
             * UV 좌표가 뒤집히는 현상을 방지하기 위한 전처리기가 포함되어 있습니다.
             */
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.uv = float2((vertexID << 1) & 2, vertexID & 2);
                output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
                
                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
                #endif
                
                return output;
            }

            /**
             * [Ray-Box AABB Intersection]
             * 안개 구역(FogZone)의 경계를 픽셀 단위로 판정합니다.
             */
            bool GetRayBoxDistance(float3 rayOrigin, float3 rayDir, out float tNear, out float tFar)
            {
                float3 invDir = 1.0 / (rayDir + 0.000001);
                float3 t0 = (_FogZoneMin - rayOrigin) * invDir;
                float3 t1 = (_FogZoneMax - rayOrigin) * invDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                tNear = max(max(tmin.x, tmin.y), tmin.z);
                tFar = min(min(tmax.x, tmax.y), tmax.z);
                return tNear < tFar && tFar > 0;
            }

            /**
             * [Main Composite Function]
             * 원본 장면, 에지 마스크, 안개 데이터를 하나로 합칩니다.
             */
            float4 frag(Varyings input) : SV_Target
            {
                // 1. 기초 샘플링
                float4 sceneCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float4 fogSample = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, input.uv);

                // [조치] _InverseVP가 정상인지 확인하기 위한 디버그 로직 (ZoneMask)
                if(_DebugMode == 2)
                {
                    float3 d_rayStart = GetCameraPositionWS();
                    // 0.5 깊이에서 박스 형태가 보이는지 확인
                    float3 d_worldPos = ComputeWorldSpacePosition(input.uv, 0.5, _InverseVP);
                    float3 d_rayDir = normalize(d_worldPos - d_rayStart);
                    float d_tNear, d_tFar;
                    
                    if(GetRayBoxDistance(d_rayStart, d_rayDir, d_tNear, d_tFar)) 
                        return float4(lerp(sceneCol.rgb, float3(1, 0, 0), 0.7), 1.0);
                    else 
                        return sceneCol; 
                }

                // 2. 노멀 기반 에지 마스크 생성 (뼈대 보호)
                float2 uv = input.uv;
                float offset = 1.0 / _ScreenParams.x;
                float3 n1 = SampleSceneNormals(uv + float2(-offset, 0));
                float3 n2 = SampleSceneNormals(uv + float2(offset, 0));
                float edgeDist = distance(n1, n2);
                float edgeMask = saturate(1.0 - (edgeDist * _EdgeStrength));

                // 3. 밝기 기반 라이트 블리드 (면에서 새는 빛)
                float brightness = max(sceneCol.r, max(sceneCol.g, sceneCol.b));
                float bloomEffect = smoothstep(_Threshold, 1.0, brightness) * edgeMask;

                // 4. 최종 색상 합성
                // 안개 입자가 원본 배경을 덮는 비율(Alpha)을 lerp로 처리합니다.
                float3 fogRGB = fogSample.rgb * _BleedColor.rgb;
                float3 bloomRGB = bloomEffect * _BleedColor.rgb * 0.4;
                
                // lerp: (배경 + 블룸) 위에 안개(fogRGB)를 알파 농도만큼 섞음
                float3 finalRGB = lerp(sceneCol.rgb + bloomRGB, fogRGB, fogSample.a);
                
                // 갓레이 산란 효과를 보강하기 위해 안개 색상을 살짝 더해줌 (Add)
                finalRGB += fogRGB * 0.6;

                // 디버그 모드 1 (안개만 보기)
                if(_DebugMode == 1) return float4(fogRGB * 2.0, 1.0);

                return float4(finalRGB, sceneCol.a);
            }
            ENDHLSL
        }
    }
}