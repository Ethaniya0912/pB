#ifndef RETRO_TILING_INCLUDED
#define RETRO_TILING_INCLUDED

/**
 * UV Tiling & Offset Logic (Fixed for D3D11 Uninitialized Error)
 * 셰이더 그래프의 Precision 설정이 'Float'일 때 호출됩니다.
 */
void ApplyTiling_float(
    float2 uv, 
    float2 tiling, 
    float2 offset, 
    out float2 outUV)
{
    // D3D11 강제 초기화
    outUV = float2(0, 0);
    outUV = uv * tiling + offset;
}

/**
 * 셰이더 그래프의 Precision 설정이 'Half'일 때 호출됩니다.
 */
void ApplyTiling_half(
    half2 uv, 
    half2 tiling, 
    half2 offset, 
    out half2 outUV)
{
    // D3D11 강제 초기화
    outUV = half2(0, 0);
    outUV = uv * tiling + offset;
}

/**
 * Vector4 대응 버전 (Float)
 */
void ApplyTilingST_float(
    float2 uv, 
    float4 tilingOffset, 
    out float2 outUV)
{
    outUV = float2(0, 0);
    outUV = uv * tilingOffset.xy + tilingOffset.zw;
}

/**
 * Vector4 대응 버전 (Half)
 */
void ApplyTilingST_half(
    half2 uv, 
    half4 tilingOffset, 
    out half2 outUV)
{
    outUV = half2(0, 0);
    outUV = uv * tilingOffset.xy + tilingOffset.zw;
}

#endif