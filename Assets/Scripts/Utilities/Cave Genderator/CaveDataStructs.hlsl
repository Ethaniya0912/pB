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
    float3 sculptFlags;  // [A.5] padding 재활용 — x=wantNarrow, y=wantHighGround, z=wantOpen
};

// C#의 EdgeData와 동일 (72 bytes) — Phase 4.5-G Stage 3-C
struct EdgeData
{
    float3 startPos;      // offset 0
    float3 endPos;        // offset 12
    float  width;         // offset 24
    float  curvatureAmp;  // offset 28 — per-edge curvature
    uint   w1_packed;     // offset 32 — waypoint 1 (half3 packed)
    uint   w2_packed;     // offset 36 — waypoint 2 (half3 packed)
    uint   flags;         // offset 40 — bits[0..1] = numWaypoints (0/1/2)
    uint   _padWP;        // offset 44 — alignment
    float3 aabbMin;       // offset 48 — pre-computed AABB
    float3 aabbMax;       // offset 60
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
    float blendDamping;   // [Phase 4.5-G Stage 1-A] blend 중심 detail amp 감쇠 (0.3~1.0)
};

#endif