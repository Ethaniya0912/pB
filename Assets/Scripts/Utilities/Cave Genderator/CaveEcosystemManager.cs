using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Collections.Generic;

namespace CaveSystem
{
    public enum PropType
    {
        Stalactite, // 종유석
        Malachite,  // 말라카이트 광석
        Fern        // 고사리/식생
    }

    /// <summary>
    /// GPU 렌더링을 위한 렌더 데이터를 담는 컨테이너입니다.
    /// </summary>
    public class PropRenderData
    {
        public Mesh mesh;
        public Material material;
        public ComputeBuffer matrixBuffer;
        public ComputeBuffer argsBuffer;
        public int currentInstanceCount;
    }

    /// <summary>
    /// 마칭 큐브에서 추출된 광맥 데이터를 분석하고, 푸아송 디스크 샘플링 기반의 거리 필터링을 거쳐
    /// GPU Instancing을 위한 Matrix4x4 버퍼를 생성하는 두뇌 역할을 합니다.
    /// </summary>
    public class CaveEcosystemManager : MonoBehaviour
    {
        public static CaveEcosystemManager Instance { get; private set; }

        [Header("Prop Assets")]
        public Mesh stalactiteMesh;
        public Material stalactiteMaterial;

        public Mesh malachiteMesh;
        public Material malachiteMaterial;

        public Mesh fernMesh;
        public Material fernMaterial;

        [Header("Ecosystem Settings")]
        public float minStalactiteDistance = 2.0f;
        public float minMalachiteDistance = 1.5f;
        public float minFernDistance = 1.0f;
        public int maxInstancesPerType = 100000;

        // Renderer Feature에서 접근할 수 있도록 딕셔너리로 관리
        public Dictionary<PropType, PropRenderData> PropDataDict { get; private set; } = new Dictionary<PropType, PropRenderData>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializeBuffers();
        }

        private void InitializeBuffers()
        {
            // 각 프롭 타입별 버퍼 초기화
            InitializePropType(PropType.Stalactite, stalactiteMesh, stalactiteMaterial);
            InitializePropType(PropType.Malachite, malachiteMesh, malachiteMaterial);
            InitializePropType(PropType.Fern, fernMesh, fernMaterial);
        }

        private void InitializePropType(PropType type, Mesh mesh, Material mat)
        {
            if (mesh == null || mat == null) return;

            PropRenderData data = new PropRenderData
            {
                mesh = mesh,
                material = mat,
                matrixBuffer = new ComputeBuffer(maxInstancesPerType, sizeof(float) * 16),
                // DrawMeshInstancedIndirect를 위한 5개의 uint Args 버퍼 (indexCount, instanceCount, startIndex, baseVertex, startInstance)
                argsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments)
            };

            uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
            args[0] = (uint)mesh.GetIndexCount(0);
            data.argsBuffer.SetData(args);

            // 매터리얼에 버퍼 바인딩 (커스텀 셰이더에서 StructuredBuffer<float4x4> _PropMatrices 로 읽어옴)
            mat.SetBuffer("_PropMatrices", data.matrixBuffer);

            PropDataDict.Add(type, data);
        }

        /// <summary>
        /// Phase 2에서 넘어온 광석 데이터를 분석하여 버퍼를 갱신합니다.
        /// </summary>
        public void ProcessEcosystem(NativeArray<CaveOreData> oreDataArray)
        {
            if (oreDataArray.Length == 0) return;

            // [수정] TempJob 대신 Persistent를 사용하여 4프레임 수명 에러(JobTempAlloc) 해결
            NativeList<Matrix4x4> stalactiteList = new NativeList<Matrix4x4>(Allocator.Persistent);
            NativeList<Matrix4x4> malachiteList = new NativeList<Matrix4x4>(Allocator.Persistent);
            NativeList<Matrix4x4> fernList = new NativeList<Matrix4x4>(Allocator.Persistent);

            // 공간 해싱을 통한 거리 필터링(Poisson Disk 유사 효과)을 위한 그리드 맵
            NativeParallelHashMap<int3, bool> spatialGrid = new NativeParallelHashMap<int3, bool>(oreDataArray.Length, Allocator.Persistent);

            // C# Job System 스케줄링
            PropPlacementJob job = new PropPlacementJob
            {
                oreData = oreDataArray,
                stalactiteMatrices = stalactiteList,
                malachiteMatrices = malachiteList,
                fernMatrices = fernList,
                spatialGrid = spatialGrid,
                gridCellSize = 1.0f // 해상도 기반 최소 거리 필터링
            };

            JobHandle handle = job.Schedule();
            handle.Complete();

            // 버퍼 갱신 (누적 렌더링을 위해 실무에서는 Append 방식이나 청크별 관리가 필요하지만, 예제에서는 전체 갱신)
            UpdatePropBuffer(PropType.Stalactite, stalactiteList);
            UpdatePropBuffer(PropType.Malachite, malachiteList);
            UpdatePropBuffer(PropType.Fern, fernList);

            stalactiteList.Dispose();
            malachiteList.Dispose();
            fernList.Dispose();
            spatialGrid.Dispose();
        }

        private void UpdatePropBuffer(PropType type, NativeList<Matrix4x4> matrices)
        {
            if (!PropDataDict.TryGetValue(type, out var data)) return;

            int count = Mathf.Min(matrices.Length, maxInstancesPerType);
            if (count > 0)
            {
                data.matrixBuffer.SetData(matrices.AsArray(), 0, 0, count);
            }

            // Args 버퍼의 instanceCount(인덱스 1) 업데이트
            // 실무에서는 Compute Shader 안에서 직접 쓸 수도 있음
            uint[] args = new uint[5];
            data.argsBuffer.GetData(args);
            args[1] = (uint)count;
            data.argsBuffer.SetData(args);

            data.currentInstanceCount = count;
        }

        private void OnDestroy()
        {
            foreach (var kvp in PropDataDict)
            {
                kvp.Value.matrixBuffer?.Release();
                kvp.Value.argsBuffer?.Release();
            }
        }
    }

    /// <summary>
    /// 생태계 배치 분석을 수행하는 Burst Job입니다.
    /// </summary>
    [BurstCompile]
    public struct PropPlacementJob : IJob
    {
        [ReadOnly] public NativeArray<CaveOreData> oreData;
        public NativeList<Matrix4x4> stalactiteMatrices;
        public NativeList<Matrix4x4> malachiteMatrices;
        public NativeList<Matrix4x4> fernMatrices;

        public NativeParallelHashMap<int3, bool> spatialGrid;
        public float gridCellSize;

        public void Execute()
        {
            Unity.Mathematics.Random rand = new Unity.Mathematics.Random(12345);

            for (int i = 0; i < oreData.Length; i++)
            {
                CaveOreData data = oreData[i];
                float3 pos = data.position;
                float3 normal = data.normal;

                // 1. 공간 해싱을 이용한 초고속 푸아송 디스크(거리) 필터링
                int3 gridPos = new int3(math.floor(pos / gridCellSize));
                if (spatialGrid.ContainsKey(gridPos)) continue; // 이미 근처에 다른 프롭이 있으면 스킵
                spatialGrid.TryAdd(gridPos, true);

                // 랜덤 스케일
                float scale = rand.NextFloat(0.8f, 1.5f);
                quaternion rotation = quaternion.identity;

                // 2. 표면 법선 내적을 통한 지향성 분석 및 배치
                float dotUp = math.dot(normal, new float3(0, 1, 0));

                if (dotUp < -0.8f) // 천장 (종유석)
                {
                    // 종유석은 무조건 아래를 향하게 고정 (천장 기울기 무시)
                    rotation = quaternion.LookRotationSafe(new float3(1, 0, 0), new float3(0, -1, 0));
                    stalactiteMatrices.Add(float4x4.TRS(pos, rotation, new float3(scale)));
                }
                else if (dotUp > -0.5f && dotUp < 0.5f) // 벽면 (말라카이트)
                {
                    // 벽의 노멀 방향으로 회전하여 자연스럽게 박힌 모습 연출
                    rotation = quaternion.LookRotationSafe(normal, new float3(0, 1, 0));
                    malachiteMatrices.Add(float4x4.TRS(pos, rotation, new float3(scale)));
                }
                else if (dotUp > 0.8f) // 바닥 (고사리)
                {
                    // 위를 향하되 Y축으로 랜덤 회전
                    rotation = quaternion.Euler(0, rand.NextFloat(0, math.PI * 2), 0);
                    fernMatrices.Add(float4x4.TRS(pos, rotation, new float3(scale)));
                }
            }
        }
    }
}