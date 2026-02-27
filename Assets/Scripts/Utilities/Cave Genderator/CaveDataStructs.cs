using UnityEngine;
using System.Runtime.InteropServices;
using System;

namespace CaveSystem
{
    // ====================================================================
    // [시스템 매니지먼트 데이터] 누락 복구
    // ====================================================================

    /// <summary>
    /// 청크의 생성 및 로딩 상태를 정의하는 열거형
    /// </summary>
    public enum ChunkState
    {
        Queued,         // 생성 대기열 진입
        Generating,     // GPU 연산 중
        BakingPhysics,  // 물리 메시 베이킹 중
        Completed,      // 생성 완료 및 배치 성공
        Aborted         // 생성 취소됨 (플레이어 멀어짐 등)
    }

    /// <summary>
    /// 청크 생성을 요청할 때 필요한 모든 정보를 담는 컨텍스트 객체
    /// </summary>
    public class ChunkRequestContext : IDisposable
    {
        public Vector3Int ChunkPos;      // 청크의 그리드 좌표
        public ChunkState State;         // 현재 진행 상태
        public GameObject ChunkObject;   // 씬 내 실제 오브젝트 참조 (Headless일 경우 null)

        public void Dispose()
        {
            // 리소스 해제 로직 (필요 시 확장)
        }
    }

    // ====================================================================
    // [Phase 1/7] 16바이트(float4) 정렬을 엄격하게 준수하는 크로스 플랫폼 데이터 구조체
    // ====================================================================

    [StructLayout(LayoutKind.Sequential)]
    public struct CaveVoxel
    {
        public float density;  // 4 바이트
        public int oreType;    // 4 바이트
        public Vector2 padding; // 8 바이트 (총 16바이트)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CaveVertex
    {
        public Vector3 position; // 12 바이트
        public Vector3 normal;   // 12 바이트
        public Vector2 uv;       // 8 바이트 (총 32바이트)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CaveTriangle
    {
        public CaveVertex v0;
        public CaveVertex v1;
        public CaveVertex v2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CaveOreData
    {
        public Vector3 position; // 12 바이트
        public int oreType;      // 4 바이트 ('type'에서 'oreType'으로 통일됨)
        public Vector3 normal;   // 12 바이트
        public float padding;    // 4 바이트 (총 32바이트)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NodeData
    {
        public Vector3 position; // 12 바이트
        public float radius;     // 4 바이트
        public int roomType;     // 4 바이트
        public Vector3 padding;  // 12 바이트 (총 32바이트)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EdgeData
    {
        public Vector3 startPos; // 12 바이트
        public Vector3 endPos;   // 12 바이트
        public float width;      // 4 바이트
        public float padding;    // 4 바이트 (총 32바이트)
    }
}