using UnityEngine;
using System.Runtime.InteropServices;

namespace CaveSystem
{
    // C#과 HLSL 간의 1:1 메모리 매칭을 위해 레이아웃을 순차적으로 강제합니다.

    /// <summary>
    /// 동굴의 기본 복셀 데이터 (총 8 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveVoxel
    {
        // 0을 기준으로 마칭 큐브 표면이 결정됨 (4 바이트)
        public float density;
        // 광물 식별자 (0=일반 바위, 1=말라카이트 등) (4 바이트)
        public int oreType;
    }

    /// <summary>
    /// 마칭 큐브 연산으로 생성되는 정점 데이터 (총 32 바이트)
    /// 16바이트(float4)의 배수로 맞추어 GPU 대역폭을 최적화합니다.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveVertex
    {
        public Vector3 position; // 12 바이트
        public Vector3 normal;   // 12 바이트
        public Vector2 uv;       // 8 바이트 (최적화를 위해 추가됨, 총 32 바이트)
    }

    /// <summary>
    /// 스레드 경합(Race Condition)을 원천 차단하기 위해 3개의 정점을 묶은 삼각형 단위 구조체 (총 96 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveTriangle
    {
        public CaveVertex v0;
        public CaveVertex v1;
        public CaveVertex v2;
    }

    /// <summary>
    /// 표면에 노출된 광물 및 식생 프롭 배치를 위한 데이터 (총 32 바이트)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CaveOreData
    {
        public Vector3 position; // 12 바이트
        public int type;         // 4 바이트
        public Vector3 normal;   // 12 바이트
        public float padding;    // 4 바이트 (16바이트 정렬을 위한 명시적 더미 데이터)
    }
}