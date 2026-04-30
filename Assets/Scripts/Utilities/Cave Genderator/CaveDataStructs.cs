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
        public Vector3 sculptFlags;  // 12 bytes (Offset 20) [A.5] padding → sculpt 편향
        //                                                   x = wantNarrow    (-1~+1: 넓게↔좁게)
        //                                                   y = wantHighGround (-1~+1: 낮게↔높게)
        //                                                   z = wantOpen      (-1~+1: 밀집↔개방)
        //                                                   NodeGraphBuilder가 roomType별 자동 설정.
        //                                                   Shader 소비는 A.7/.8에서 (현재 padding 역할).
    }

    /// <summary>
    /// 노드 간 연결 통로(Edge) 데이터 (총 32 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EdgeData
    {
        // ── 기본 (32B, 기존 호환) ──────────────────────────────────────
        public Vector3 startPos;     // 12 bytes (Offset 0)
        public Vector3 endPos;       // 12 bytes (Offset 12)
        public float width;          // 4 bytes  (Offset 24)
        public float curvatureAmp;   // 4 bytes  (Offset 28) [E-β.3.5] padding 재활용
        //                                                   0 = 직선 (기본), > 0 = capsule chain
        //                                                   NodeGraphBuilder가 biome.curvatureAmp 주입

        // ── Route A* waypoint (16B) — [Phase 4.5-G Stage 3-C] ─────────
        // Packed half3 (R10G11B11 유사) — midpoint 기준 offset (±10m, 0.01m step)
        // numWaypoints=0 → 기존 fast-path (byte-identical 유지)
        public uint w1_packed;       // 4 bytes (Offset 32) — waypoint 1 offset
        public uint w2_packed;       // 4 bytes (Offset 36) — waypoint 2 offset
        public uint flags;           // 4 bytes (Offset 40)
        //   bits[0..1] = numWaypoints (0/1/2)
        //   bit[2]     = [Phase 4.5-G Stage 3-D D4] BypassBlendBoost
        //                디자이너가 좁은 통로 의도 시 set → BlendWidthBoost 무시
        //   bit[3]     = [D4] WidthBoosted   (D4가 width 확장함)
        //   bit[4]     = [D4] WidthNarrowed  (D4가 width 축소함)
        //   bits[5..31] = reserved
        public uint _padWP;          // 4 bytes (Offset 44) — 16B alignment

        // ── Pre-computed AABB (24B) — [Phase 4.5-G Stage 3-C, O1] ────
        // Runtime에서 실시간 계산 대신 pregen 시 저장 (immutable phase 활용)
        // Waypoint edges는 전체 sub-segment 커버하는 tight AABB
        public Vector3 aabbMin;      // 12 bytes (Offset 48)
        public Vector3 aabbMax;      // 12 bytes (Offset 60)
        //                                                   총 72 bytes
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
        public float blendDamping;   // 4 bytes (Offset 28) [Phase 4.5-G Stage 1-A]
                                      //   Blend 중심 detail amp 감쇠 비율 (0.3~1.0)
                                      //   기존 padding 슬롯 재활용 — 구조체 크기 변경 없음
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

        // [Phase 2] featureType 배열 — Laplacian 억제에 사용
        public int[] FeatureTypes;  // dcVerts 인덱스 기준, -1=무효/0=Smooth/1=Edge/2=Corner

        // ═══════════════════════════════════════════════════════════════════════════
        // [Approach B / LOD Isolation] IsCoarse 플래그
        //   역할: Coarse-First 프리뷰 chunk인지 식별.
        //   true  → 임시 저해상 mesh. Ghost Cache 등록 skip, Mirror skip, Halo Bake skip.
        //           Fine chunk 도착 시 파괴됨 (CleanupCompletedCoarse).
        //   false → 정상 Fine chunk. 모든 G4-A / G4-C 로직 적용 대상.
        //
        //   설정 위치: CaveChunkManager.TryProcessCoarseQueue (Coarse 생성 시 true)
        //              기본값 false (Fine 기본 경로).
        //
        //   미래 Multi-LOD (Approach A) 업그레이드 시:
        //     bool IsCoarse → int LodLevel (0=Fine, 1=Medium, 2=Coarse)로 확장 가능.
        //     이 플래그는 "L0 vs 非L0"의 이분법을 유지하여 점진 마이그레이션 보조.
        // ═══════════════════════════════════════════════════════════════════════════
        public bool IsCoarse = false;

        public void Dispose() { DensityCache = null; FeatureTypes = null; }
    }
}