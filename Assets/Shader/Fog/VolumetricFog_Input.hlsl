#ifndef VOLUMETRIC_FOG_INPUT_INCLUDED
#define VOLUMETRIC_FOG_INPUT_INCLUDED

float4x4 _FrustumCorners;
float3 _CameraPosWS;
float3 _CameraForwardWS; // [V111 추가]
float _FogDensity;
float _StepCount;
float _MaxDist;
float4 _AmbientColor;

TEXTURE2D(_NoiseTex); 
SAMPLER(sampler_NoiseTex);

int _CustomLightCount;
float4 _CustomLightPosRange[128];
float4 _CustomLightColor[128];

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 viewVector : TEXCOORD1;
};

#endif