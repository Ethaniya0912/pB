#ifndef CAVE_DATA_STRUCTS_INCLUDED
#define CAVE_DATA_STRUCTS_INCLUDED

// [🔥 컴파일 에러 수정] 
// StructuredBuffer는 cbuffer의 16바이트 패딩 규칙을 엄격하게 받지 않으므로, 
// C# 구조체(Vector3)와 100% 동일하게 float3를 연달아 선언해도 32바이트(12+12+8)로 정확히 정렬됩니다.
struct CaveVertex
{
    float3 position; // 12 bytes
    float3 normal; // 12 bytes (nx, nyz 쪼개기 폐기 -> C# Vector3와 일치)
    float2 uv; // 8 bytes (총 32바이트 완벽 일치)
};

// 총 96 바이트 (스레드 경합 방지용 원자적 데이터 묶음)
struct CaveTriangle
{
    CaveVertex v0;
    CaveVertex v1;
    CaveVertex v2;
};

// 총 8 바이트
struct CaveVoxel
{
    float density;
    int oreType;
};

// 총 32 바이트 (16바이트 정렬을 위한 padding 포함)
struct CaveOreData
{
    float3 position; // 12
    int type; // 4
    float3 normal; // 12
    float padding; // 4
};

#endif