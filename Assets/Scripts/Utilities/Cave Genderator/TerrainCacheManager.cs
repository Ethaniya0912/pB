using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;

namespace CaveSystem
{
    /// <summary>
    /// 비가시적(Headless) 모드에서 생성된 메쉬/배열 데이터를 보관하는 컨테이너
    /// </summary>
    public struct PrecookedChunkData
    {
        public NativeArray<CaveVertex> vertices;
        public NativeArray<int> indices;
        public NativeArray<CaveOreData> oreData;
        public Mesh bakedMesh; // Physics.BakeMesh가 완료된 메쉬
    }

    /// <summary>
    /// [Phase 1] 로비 씬에서 게임 씬으로 구워진 지형 데이터를 안전하게 운반하는 영속성 캐시 매니저
    /// </summary>
    public class TerrainCacheManager : MonoBehaviour
    {
        public static TerrainCacheManager Instance { get; private set; }

        [Header("Cache State (Read Only)")]
        [SerializeField] private int cachedChunkCount = 0;

        [Header("Gizmos Settings")]
        public float chunkSize = 16f; // Gizmo 시각화를 위한 청크 물리 크기
        public float voxelSize = 1.0f;

        // 청크 좌표를 Key로 하여 구워진 데이터를 캐싱하는 딕셔너리
        private Dictionary<Vector3Int, PrecookedChunkData> cachedChunks = new Dictionary<Vector3Int, PrecookedChunkData>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(this.gameObject); // 씬 전환 시 파괴 방지
        }

        /// <summary>
        /// 로비 백그라운드 스레드에서 생성 완료된 데이터를 캐시에 적재합니다.
        /// </summary>
        public void AddCache(Vector3Int chunkPos, PrecookedChunkData data)
        {
            if (cachedChunks.ContainsKey(chunkPos))
            {
                // 이미 존재한다면 기존 데이터의 언매니지드 메모리를 해제 후 덮어쓰기
                DisposeData(cachedChunks[chunkPos]);
            }

            cachedChunks[chunkPos] = data;
            cachedChunkCount = cachedChunks.Count;
        }

        /// <summary>
        /// 게임 씬 로드 후 ChunkManager가 데이터를 꺼내갑니다.
        /// 꺼내간 후에는 언매니지드 메모리 누수 방지를 위해 반드시 Dispose 처리합니다.
        /// </summary>
        public bool ConsumeCache(Vector3Int chunkPos, out Mesh readyMesh, out NativeArray<CaveOreData> oreData)
        {
            if (cachedChunks.TryGetValue(chunkPos, out PrecookedChunkData data))
            {
                readyMesh = data.bakedMesh;

                // 광물 데이터는 생태계 스폰을 위해 원본을 복사해서 넘기거나 그대로 이관합니다.
                // 여기서는 생명주기 관리를 위해 새 배열에 복사하여 반환 (또는 참조 전달 후 관리 위임)
                // 안전을 위해 참조를 넘기되, vertices와 indices만 여기서 즉시 폐기합니다.
                oreData = data.oreData;

                // [가장 중요] 메쉬에 이미 바인딩된 정점/인덱스 네이티브 배열은 즉시 메모리 해제(Memory Leak 철벽 방어)
                if (data.vertices.IsCreated) data.vertices.Dispose();
                if (data.indices.IsCreated) data.indices.Dispose();

                // 꺼내간 캐시 데이터는 딕셔너리에서 제거
                cachedChunks.Remove(chunkPos);
                cachedChunkCount = cachedChunks.Count;

                return true;
            }

            readyMesh = null;
            oreData = default;
            return false;
        }

        /// <summary>
        /// 특정 데이터의 네이티브 메모리를 안전하게 해제합니다.
        /// </summary>
        private void DisposeData(PrecookedChunkData data)
        {
            if (data.vertices.IsCreated) data.vertices.Dispose();
            if (data.indices.IsCreated) data.indices.Dispose();
            if (data.oreData.IsCreated) data.oreData.Dispose();
        }

        /// <summary>
        /// 강제 종료나 비정상 상황 시 메모리 릭을 방지합니다.
        /// </summary>
        private void OnDestroy()
        {
            foreach (var kvp in cachedChunks)
            {
                DisposeData(kvp.Value);
            }
            cachedChunks.Clear();
        }

        /// <summary>
        /// 에디터 씬 뷰에서 베이킹 진행 상황을 시각적으로 모니터링하기 위한 기즈모 렌더링
        /// </summary>
        private void OnDrawGizmos()
        {
            if (cachedChunks == null || cachedChunks.Count == 0) return;

            // 이미 구워져서 캐시에 적재된 청크는 파란색 반투명 큐브로 표시
            Gizmos.color = new Color(0.0f, 1.0f, 1.0f, 0.3f);

            float worldChunkSize = chunkSize * voxelSize;
            Vector3 centerOffset = new Vector3(worldChunkSize * 0.5f, worldChunkSize * 0.5f, worldChunkSize * 0.5f);

            foreach (var kvp in cachedChunks)
            {
                Vector3 worldPos = new Vector3(kvp.Key.x, kvp.Key.y, kvp.Key.z) * worldChunkSize + centerOffset;
                Gizmos.DrawCube(worldPos, Vector3.one * worldChunkSize);

                // 와이어프레임으로 윤곽선 뚜렷하게 표시
                Gizmos.color = new Color(0.0f, 1.0f, 1.0f, 0.8f);
                Gizmos.DrawWireCube(worldPos, Vector3.one * worldChunkSize);
                Gizmos.color = new Color(0.0f, 1.0f, 1.0f, 0.3f);
            }
        }
    }
}