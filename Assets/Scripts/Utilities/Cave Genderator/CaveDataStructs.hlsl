// ===============================================================================
// 파일: CaveDataStructs.hlsl
// 역할: C#과 GPU(Compute Shader) 간의 16바이트 메모리 정렬을 맞추는 공통 구조체
// ===============================================================================
#ifndef CAVE_DATA_STRUCTS_INCLUDED
#define CAVE_DATA_STRUCTS_INCLUDED

struct CaveVoxel
{
    float density;
    int oreType;
    float2 padding;
};

struct CaveVertex
{
    float3 position;
    float3 normal;
    float2 uv;
};

struct CaveTriangle
{
    CaveVertex v0;
    CaveVertex v1;
    CaveVertex v2;
};

struct CaveOreData
{
    float3 position;
    int oreType; // [에러 원인 수정] type -> oreType 으로 명칭 완벽 통일
    float3 normal;
    float padding;
};

struct NodeData
{
    float3 position;
    float radius;
    int roomType;
    float3 padding;
};

struct EdgeData
{
    float3 startPos;
    float3 endPos;
    float width;
    float padding;
};

#endif