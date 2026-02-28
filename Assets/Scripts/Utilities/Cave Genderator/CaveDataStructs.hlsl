#ifndef CAVE_DATA_STRUCTS_INCLUDED
#define CAVE_DATA_STRUCTS_INCLUDED

// C#의 CaveVoxel과 동일 (16 bytes)
struct CaveVoxel
{
    float density;
    int oreType;
    float2 padding;
};

// C#의 CaveVertex와 동일 (32 bytes)
struct CaveVertex
{
    float3 position;
    float3 normal;
    float2 uv;
};

// (96 bytes)
struct CaveTriangle
{
    CaveVertex v0;
    CaveVertex v1;
    CaveVertex v2;
};

// C#의 CaveOreData와 동일 (32 bytes)
struct CaveOreData
{
    float3 position;
    int oreType;
    float3 normal;
    float padding;
};

// C#의 NodeData와 동일 (32 bytes)
struct NodeData
{
    float3 position;
    float radius;
    int roomType;
    float3 padding;
};

// C#의 EdgeData와 동일 (32 bytes)
struct EdgeData
{
    float3 startPos;
    float3 endPos;
    float width;
    float padding;
};

// C#의 BiomeParamData와 동일 (32 bytes)
struct BiomeParamData
{
    float noiseFrequency;
    float yCompression;
    float sminStrength;
    float terraceSteps;

    float bumpAmplitude;
    float bumpFrequency;
    int noiseType;
    float padding;
};

#endif