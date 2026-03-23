Shader "Custom/SparkRender_Procedural"
{
    Properties
    {
        _ShutterAngle ("Shutter Angle", Float) = 180.0
        _TargetFPS    ("Target FPS",    Float) = 60.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+100"
            "IgnoreProjector" = "True"
        }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "SparkProceduralPass"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ─────────────────────────────────────────────────────────────────
            // SparkData — C# 측 struct 와 메모리 레이아웃 완전 일치 필수
            //   positionAge  : xyz=월드위치, w=나이(age)
            //   velocityLife : xyz=속도벡터, w=최대수명
            //   baseSize     : x=폭, y=높이, z=타입(0/1/2), w=flickerSeed(0~1)
            //
            // [개선 ⑥] baseSize.w 를 colorSeed / flickerSeed 로 활용
            //   이전: w=0 고정(미사용)
            //   수정: 생성 시 Random.value 를 저장 → 파티클마다 개성 있는
            //         색조 편차(±20%)와 flicker 위상 오프셋을 제공
            // ─────────────────────────────────────────────────────────────────
            struct SparkData
            {
                float4 positionAge;
                float4 velocityLife;
                float4 baseSize;   // z: 0=주줄기, 1=2차원형, 2=단일빔  w: seed
            };

            StructuredBuffer<SparkData> _SparkBuffer;

            CBUFFER_START(UnityPerMaterial)
                float _ShutterAngle;
                float _TargetFPS;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  lifeRatio  : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float  sparkType  : TEXCOORD2;
                float  seed       : TEXCOORD3; // [개선 ④⑥] flicker + color seed
                float  speed      : TEXCOORD4; // [개선 ①] 속도 기반 끝 날카로움
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                uint instanceID    = input.vertexID / 6;
                uint localVertexID = input.vertexID % 6;

                SparkData spark = _SparkBuffer[instanceID];

                float age      = spark.positionAge.w;
                float lifetime = spark.velocityLife.w;
                float ratio    = (lifetime > 0.001) ? saturate(age / lifetime) : 1.0;

                output.lifeRatio = ratio;
                output.sparkType = spark.baseSize.z;
                output.seed      = spark.baseSize.w;

                float3 velocity = spark.velocityLife.xyz;
                float  spd      = length(velocity);
                output.speed    = spd;

                if (ratio >= 1.0)
                {
                    output.positionCS = float4(99999, 99999, 99999, 1);
                    output.uv = float2(0, 0);
                    return output;
                }

                float2 quadPositions[6] = {
                    float2(-0.5, -0.5), float2(-0.5,  0.5), float2( 0.5, -0.5),
                    float2( 0.5, -0.5), float2(-0.5,  0.5), float2( 0.5,  0.5)
                };
                float2 posOS = quadPositions[localVertexID];
                output.uv    = posOS;

                float3 camRight = normalize(float3(
                    UNITY_MATRIX_V[0][0], UNITY_MATRIX_V[0][1], UNITY_MATRIX_V[0][2]));
                float3 camUp = normalize(float3(
                    UNITY_MATRIX_V[1][0], UNITY_MATRIX_V[1][1], UNITY_MATRIX_V[1][2]));

                float  t = spark.baseSize.z;
                float3 worldPos;

                if (t > 1.5)
                {
                    // ── 타입 2: 단일 빔 ─────────────────────────────────────
                    float3 beamDir = (spd > 0.001) ? (velocity / spd) : camUp;
                    float  expTime = (_ShutterAngle / 360.0) / max(_TargetFPS, 1.0);
                    float  stretch = max(spark.baseSize.y, spd * expTime * 3.0);
                    worldPos = spark.positionAge.xyz
                             + camRight * (posOS.x * spark.baseSize.x)
                             + beamDir  * (posOS.y * stretch);
                }
                else if (t > 0.5)
                {
                    // ── 타입 1: 2차 원형 Billboard ──────────────────────────
                    worldPos = spark.positionAge.xyz
                             + camRight * (posOS.x * spark.baseSize.x)
                             + camUp    * (posOS.y * spark.baseSize.y);
                }
                else
                {
                    // ── 타입 0: 주 줄기 — 셔터 앵글 스트레칭 ────────────────
                    float3 stretchDir = (spd > 0.001) ? (velocity / spd) : camUp;
                    float  expTime    = (_ShutterAngle / 360.0) / max(_TargetFPS, 1.0);
                    float  stretchLen = max(spd * expTime, spark.baseSize.y * 0.5);

                    float thinFactor  = saturate(1.0 - stretchLen * 0.4);
                    float w = spark.baseSize.x * (0.3 + 0.7 * thinFactor);
                    float h = spark.baseSize.y + stretchLen;

                    worldPos = spark.positionAge.xyz
                             + camRight   * (posOS.x * w)
                             + stretchDir * (posOS.y * h);
                }

                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            // ─────────────────────────────────────────────────────────────────
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv        = input.uv;
                float  lifeRatio = input.lifeRatio;
                float  t         = input.sparkType;
                float  seed      = input.seed;
                float  spd       = input.speed;
                float  lifeHeat  = saturate(1.0 - lifeRatio);

                // ─────────────────────────────────────────────────────────────
                // [개선 ①] Tapered 끝 마스크 — 속도 기반 날카로움
                //   이전: smoothstep(0.40, 0.5) → 뭉툭하게 잘림
                //   수정: 속도가 빠를수록 끝이 더 날카롭게 수렴하는 포물선 마스크
                //         sharpness: 느린파티클=1.5(약간뭉툭), 빠른파티클=4.0(바늘)
                // ─────────────────────────────────────────────────────────────
                float sharpness = lerp(1.5, 4.0, saturate(spd / 10.0));
                float tipFade   = pow(max(0.0, 1.0 - abs(uv.y) * 1.85), sharpness);

                // ─────────────────────────────────────────────────────────────
                // [개선 ②] 색상 재조정 — 황금-주황 베이스, 적갈색 소멸
                //   이전: hotColor=(6.5,6.0,4.0) → Bloom 후 흰색 포화
                //         coldColor=(0.9,0.05,0.0) → 순수 빨강, 금속감 없음
                //   수정: 모든 색상 채도 유지, coldColor에 갈색기(G+0.16) 추가
                //         파티클별 seed로 색조를 ±15% 편차 부여
                // ─────────────────────────────────────────────────────────────

                // [개선 ⑥] seed 기반 색조 편차 — 파티클마다 다른 색 개성
                float colorShift = (seed - 0.5) * 0.3; // -0.15 ~ +0.15

                float3 hotColor, midColor, coldColor;

                if (t > 1.5)
                {
                    // 단일 빔: 밝고 청백에 가까운 황금
                    hotColor  = float3(7.0, 5.5, 2.5);
                    midColor  = float3(3.5, 2.2, 0.3);
                    coldColor = float3(0.8, 0.18, 0.01);
                }
                else
                {
                    // 주 줄기 / 2차 스파크: 황금-주황-적갈
                    hotColor  = float3(4.5 + colorShift * 0.5,
                                       2.8 + colorShift * 0.3,
                                       0.3 + colorShift * 0.1);
                    midColor  = float3(2.8 + colorShift * 0.3,
                                       1.2,
                                       0.05);
                    coldColor = float3(0.7,
                                       0.18 + colorShift * 0.15, // 갈색기 seed 반영
                                       0.02);
                }

                // 축 방향 열 그라디언트 (줄기 머리=고온, 꼬리=저온)
                float axialHeat    = saturate(uv.y + 0.5);
                float combinedHeat = axialHeat * lifeHeat;

                float3 finalColor  = lerp(coldColor, midColor,  saturate(combinedHeat * 2.0));
                finalColor         = lerp(finalColor, hotColor, saturate((combinedHeat - 0.5) * 2.0));

                // ─────────────────────────────────────────────────────────────
                // [개선 ⑤] 2단 코어 글로우 — 극밝은 내부 + 중간 글로우 분리
                //   이전: core 레이어 1개, smoothstep(0.0, 0.2)
                //   수정: core1(극내부 0.08) + core2(중간 0.25) 2단 구성
                //         내부는 황금백, 외부는 주황 → 밝기 집중도 극대화
                // ─────────────────────────────────────────────────────────────
                float core1 = 1.0 - smoothstep(0.0, 0.08, length(uv)); // 극밝은 내부
                float core2 = 1.0 - smoothstep(0.0, 0.25, length(uv)); // 중간 글로우

                finalColor += core1 * float3(5.0, 4.0, 1.8) * lifeHeat; // 황금백 핵심
                finalColor += core2 * float3(1.5, 0.8, 0.1) * lifeHeat; // 주황 글로우

                // ─────────────────────────────────────────────────────────────
                // [개선 ④] Flicker 소멸 — seed 기반 sin 진동
                //   이전: lifeHeat 선형 감소 → 기계적 페이드아웃
                //   수정: 수명 후반(60% 이후)에 sin 기반 불규칙 깜빡임 적용
                //         seed가 위상 오프셋 역할 → 파티클마다 다른 타이밍으로 꺼짐
                //         frequency: 8~14Hz (메탈 불꽃의 전형적 깜빡임 주파수)
                // ─────────────────────────────────────────────────────────────
                float flickerFreq  = lerp(8.0, 14.0, seed);
                float flickerPhase = seed * 6.283;
                float flickerWave  = 0.55 + 0.45 * sin(lifeRatio * flickerFreq * 6.283 + flickerPhase);
                float flickerMix   = saturate((lifeRatio - 0.60) * 2.5); // 60% 이후 적용
                float flickerAlpha = lerp(lifeHeat, lifeHeat * flickerWave, flickerMix);

                // ─────────────────────────────────────────────────────────────
                // 엣지 마스크 — 타입별 분기
                // ─────────────────────────────────────────────────────────────
                float edgeFade;

                if (t > 0.5 && t < 1.5)
                {
                    // 타입 1 (2차 원형): 부드러운 원형 마스크
                    edgeFade = 1.0 - smoothstep(0.2, 0.5, length(uv));
                }
                else if (t > 1.5)
                {
                    // 타입 2 (단일 빔): X 소프트, Y tipFade
                    edgeFade  = 1.0 - smoothstep(0.15, 0.5, abs(uv.x));
                    edgeFade *= tipFade;
                }
                else
                {
                    // 타입 0 (주 줄기): X 소프트 + [개선 ①] Y tipFade
                    edgeFade  = 1.0 - smoothstep(0.22, 0.5, abs(uv.x));
                    edgeFade *= tipFade;
                }

                float alpha = flickerAlpha * edgeFade;
                return float4(finalColor * alpha, alpha);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
