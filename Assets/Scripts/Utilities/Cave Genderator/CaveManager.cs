using UnityEngine;
using System.Collections;

namespace CaveSystem
{
    public enum GlobalSystemState
    {
        Initializing,
        LoadingSaveData,
        GeneratingInitialChunks,
        Ready,
        Error
    }

    /// <summary>
    /// 시스템 오케스트레이션 및 글로벌 상태 추적을 담당하는 마스터 컨트롤러입니다.
    /// </summary>
    public class CaveManager : MonoBehaviour
    {
        public static CaveManager Instance { get; private set; }

        public GlobalSystemState CurrentState { get; private set; }

        [Header("Configuration")]
        public CaveSettings caveSettings;
        public Transform playerTransform;

        [Header("System References")]
        public CaveSaveLoadSystem saveLoadSystem;
        public CaveChunkManager chunkManager;
        public CaveComputeDispatcher computeDispatcher;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            CurrentState = GlobalSystemState.Initializing;
        }

        private IEnumerator Start()
        {
            // 1. 세이브 데이터 로드
            CurrentState = GlobalSystemState.LoadingSaveData;
            yield return StartCoroutine(saveLoadSystem.InitializeAndLoadCoroutine());

            // 2. 청크 매니저 오브젝트 풀 및 시스템 초기화
            CurrentState = GlobalSystemState.GeneratingInitialChunks;
            chunkManager.InitializePool();

            // 3. 초기 지형 생성 대기 (플레이어 주변 청크 생성 완료까지 대기)
            chunkManager.ForceGenerateChunksAroundPlayer();
            yield return new WaitUntil(() => chunkManager.IsInitialGenerationComplete());

            // 4. 시스템 준비 완료
            CurrentState = GlobalSystemState.Ready;
            Debug.Log("[CaveManager] 모든 시스템 초기화 및 초기 지형 생성 완료. Ready.");
        }

        /// <summary>
        /// 시각적 검증을 위해 플레이어가 위치한 현재 청크 영역을 기즈모로 그립니다.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (playerTransform == null || caveSettings == null) return;

            float chunkSizeWorld = chunkManager != null ? chunkManager.ChunkSize * chunkManager.VoxelSize : 16f;
            Vector3Int currentChunkPos = new Vector3Int(
                Mathf.FloorToInt(playerTransform.position.x / chunkSizeWorld),
                Mathf.FloorToInt(playerTransform.position.y / chunkSizeWorld),
                Mathf.FloorToInt(playerTransform.position.z / chunkSizeWorld)
            );

            Gizmos.color = Color.green;
            Vector3 center = new Vector3(currentChunkPos.x, currentChunkPos.y, currentChunkPos.z) * chunkSizeWorld + (Vector3.one * chunkSizeWorld * 0.5f);
            Gizmos.DrawWireCube(center, Vector3.one * chunkSizeWorld);
        }
    }
}