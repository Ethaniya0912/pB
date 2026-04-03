using UnityEngine;
using System.Runtime.InteropServices;
using System;

namespace CaveSystem
{
    public enum ChunkState
    {
        Queued,         // 생성 대기열 진입
        Generating,     // GPU 연산 중
        BakingPhysics,  // 물리 베이킹 중
        Completed,      // 생성 완료
        Aborted         // 취소됨
    }

    // ====================================================================
    // [16바이트 정렬] GPU 통신용 핵심 구조체 모음
    // ====================================================================

    /// <summary>
    /// 동굴의 기본 복셀 데이터 (총 16 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveVoxel
    {
        public float density;   // 4 bytes (Offset 0) : 밀도장 값
        public int oreType;     // 4 bytes (Offset 4) : 메타데이터 각인 (광물 ID 및 RoomType 비트플래그)
        public Vector2 padding; // 8 bytes (Offset 8) : 16바이트 정렬용 패딩
    }

    /// <summary>
    /// 마칭 큐브 추출 정점 데이터 (총 32 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveVertex
    {
        public Vector3 position; // 12 bytes (Offset 0)
        public Vector3 normal;   // 12 bytes (Offset 12)
        public Vector2 uv;       // 8 bytes  (Offset 24)
    }

    /// <summary>
    /// 원자적 처리를 위한 트라이앵글 묶음 (총 96 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveTriangle
    {
        public CaveVertex v0;
        public CaveVertex v1;
        public CaveVertex v2;
    }

    /// <summary>
    /// 생태계 매니저로 전달되는 특이점/광석 데이터 (총 32 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveOreData
    {
        public Vector3 position; // 12 bytes (Offset 0)
        public int oreType;      // 4 bytes  (Offset 12) : 융합된 메타데이터
        public Vector3 normal;   // 12 bytes (Offset 16) : 프롭 배향을 위한 법선
        public float padding;    // 4 bytes  (Offset 28) : 32바이트 정렬 마감
    }

    // ====================================================================
    // [그래프 기반 설계도 규격]
    // ====================================================================

    /// <summary>
    /// 그래프 노드(방) 설계 데이터 (총 32 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NodeData
    {
        public Vector3 position; // 12 bytes (Offset 0)
        public float radius;     // 4 bytes  (Offset 12)
        public int roomType;     // 4 bytes  (Offset 16) : 0=일반, 1=스폰, 2=보스, 3=보물, 4=싱크홀
        public Vector3 padding;  // 12 bytes (Offset 20) : 정렬 마감
    }

    /// <summary>
    /// 노드 간 연결 통로(Edge) 데이터 (총 32 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EdgeData
    {
        public Vector3 startPos; // 12 bytes (Offset 0)
        public Vector3 endPos;   // 12 bytes (Offset 12)
        public float width;      // 4 bytes  (Offset 24)
        public float padding;    // 4 bytes  (Offset 28) : 정렬 마감
    }

    /// <summary>
    /// 다중 지대(Biome) 파라미터 전달용 데이터 (총 32 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BiomeParamData
    {
        public float noiseFrequency; // 4 bytes (Offset 0)
        public float yCompression;   // 4 bytes (Offset 4)
        public float sminStrength;   // 4 bytes (Offset 8)
        public float terraceSteps;   // 4 bytes (Offset 12)

        public float bumpAmplitude;  // 4 bytes (Offset 16)
        public float bumpFrequency;  // 4 bytes (Offset 20)
        public int noiseType;        // 4 bytes (Offset 24)
        public float padding;        // 4 bytes (Offset 28) : 정렬 마감
    }

    // ====================================================================
    // [매니지먼트 컨텍스트]
    // ====================================================================
    public class ChunkRequestContext : IDisposable
    {
        public Vector3Int ChunkPos;
        public ChunkState State;
        public GameObject ChunkObject;

        // [v3] NormalBakerV3 서브복셀 베이킹용 density 데이터
        public float[] DensityCache;     // voxelBuffer에서 추출한 density 배열
        public int DensityDcN;       // DC pointsPerAxis
        public Vector3 DensityDcBasePos; // 청크 기준 월드좌표 (chunkBasePos - voxelSize)
        public float DensityVoxelSize;

        public void Dispose() { DensityCache = null; }
    }
}