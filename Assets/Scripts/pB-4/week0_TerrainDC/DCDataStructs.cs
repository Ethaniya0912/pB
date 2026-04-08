// =============================================================================
// DCDataStructs.cs  |  pB-4 Project — Week 0
// Layer  : Data (지형 파이프라인)
// Owner  : Person B
//
// 역할:
//   기존 CaveDataStructs.cs를 건드리지 않고, DC 전용 구조체를 동일 네임스페이스에
//   추가하여 기존 파이프라인과 자연스럽게 공존.
//   기존 CaveVoxel, NodeData, EdgeData, BiomeParamData는 그대로 유지.
//
// GPU 정렬 규칙:
//   - 모든 구조체는 16바이트 배수 정렬 필수
//   - Marshal.SizeOf 검증을 Week 0 체크리스트에 포함
// =============================================================================
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace CaveSystem
{
    /// <summary>
    /// DC 버텍스 데이터 (총 32 바이트) [O1] 56B→32B 압축 (uv/qefError/padding 제거)
    /// QEF로 계산된 최적 버텍스 위치 + Hermite 노말 + 피처 유형
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DCVertex
    {
        public Vector3 position;    // 12B
        public Vector3 normal;      // 12B
        public int featureType;     // 4B
        public int materialIndex;   // 4B
    }

    /// <summary>
    /// DC Hermite Edge 데이터 (총 16 바이트) [O3] 32B→16B 압축 (Oct16 packedNormal)
    /// 밀도장의 부호 전환 에지 위치 및 교차점 노말
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DCHermiteEdge
    {
        float3 intersectionPoint;   // 12B
        uint packedNormal;        //  4B  Oct16 인코딩 (오차 최대 0.5°)
    }

    /// <summary>
    /// DC 쿼드 데이터 (총 16 바이트)
    /// 4개 버텍스 인덱스로 구성된 면
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DCQuad
    {
        public int v0; // 4 bytes (Offset 0)
        public int v1; // 4 bytes (Offset 4)
        public int v2; // 4 bytes (Offset 8)
        public int v3; // 4 bytes (Offset 12)
    }

    /// <summary>
    /// SDF 게임플레이 조각 버퍼 항목 (총 16 바이트)
    /// HNGS/T-ASP가 밀도장 생성 전 주입하는 게임플레이 요구사항
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GameplaySculptData
    {
        public int wantNarrow;     // 4 bytes (Offset 0)  : bool as int (0/1)
        public int wantHighGround; // 4 bytes (Offset 4)  : bool as int (0/1)
        public int wantDeadEnd;    // 4 bytes (Offset 8)  : bool as int (0/1)
        public float ambushScore;  // 4 bytes (Offset 12) : 0.0~1.0 매복 적합도
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터 로드 시 DC 구조체 메모리 정렬 검증.
    /// 기존 CaveBiomeSettings의 ValidateDataStructures()와 동일 패턴.
    /// </summary>
    [UnityEditor.InitializeOnLoad]
    public static class DCStructValidator
    {
        static DCStructValidator()
        {
            int dcVertexSize = Marshal.SizeOf(typeof(DCVertex));
            if (dcVertexSize != 32) // [O1] 56B→32B 압축 후
                Debug.LogError($"[치명적 오류] DCVertex 크기가 32바이트가 아닙니다! 현재: {dcVertexSize}");

            int dcHermiteSize = Marshal.SizeOf(typeof(DCHermiteEdge));
            if (dcHermiteSize != 16) // [O3] 32B→16B 압축 후
                Debug.LogError($"[치명적 오류] DCHermiteEdge 크기가 16바이트가 아닙니다! 현재: {dcHermiteSize}");

            int dcQuadSize = Marshal.SizeOf(typeof(DCQuad));
            if (dcQuadSize != 16)
                Debug.LogError($"[치명적 오류] DCQuad 크기가 16바이트가 아닙니다! 현재: {dcQuadSize}");

            int sculptSize = Marshal.SizeOf(typeof(GameplaySculptData));
            if (sculptSize != 16)
                Debug.LogError($"[치명적 오류] GameplaySculptData 크기가 16바이트가 아닙니다! 현재: {sculptSize}");

            Debug.Log("[DCStructValidator] DC 구조체 메모리 정렬 검증 완료 ✓");
        }
    }
#endif
}