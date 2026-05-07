using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;

namespace CaveSystem
{
    /// <summary>
    /// [Phase 3] CPU의 연산 데이터(그래프 설계도 및 바이옴 데이터)를 GPU로 전송하고, 
    /// 컴퓨트 셰이더의 멀티 커널 실행 및 비동기 회수(AsyncReadback)를 총괄하는 디스패처입니다.
    /// </summary>
    public class CaveComputeDispatcher : MonoBehaviour
    {
        [Header("Compute Shaders")]
        public ComputeShader densityShader;
        public ComputeShader marchingCubesShader;
        public CaveBiomeSettings caveSettings;

        [Header("디버깅 — 진단 로그")]
        [Tooltip("ON → 청크 디스패치 / 캐시 HIT/MISS 진단 로그 출력.")]
        [SerializeField] private bool _verboseDiagLogging = false;

        // [🔥 Race Condition 조치] GPU 버퍼 덮어쓰기 방지를 위한 락(Lock) 플래그
        public bool IsBusy { get; set; } = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase A - Race Fix 2025] 같은 chunk 중복 dispatch 방지 (In-flight Deduplication)
        // ═══════════════════════════════════════════════════════════════════════════════
        // 배경:
        //   로그 분석 결과 같은 chunk가 두 번 DispatchChunk 호출되고, 2차 호출이 
        //   race condition으로 인해 empty(0 quads) 또는 거대 mesh(68838 quads) 생성.
        //   
        //   1차 dispatch의 BuildMeshFromDCData 완료 callback에서 IsBusy=false 해제 시점에
        //   누적된 재요청이 rapid fire되어 PreFilter의 공유 buffer(filteredNode/EdgeBuffer)
        //   를 덮어쓰며 corruption 유발.
        //
        // 해결:
        //   - 진행 중 chunk pos를 HashSet에 등록 → 중복 요청 차단
        //   - IsBusy flag만으로는 부족 (false 해제 시점 race window 존재)
        //   - 모든 exit point에서 Remove 호출 필수
        // ═══════════════════════════════════════════════════════════════════════════════
        private HashSet<Vector3Int> _inFlightChunks = new HashSet<Vector3Int>();

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase B - Retry Queue 2025] 정당한 재요청 보존 (Pending Request Coalescing)
        // ═══════════════════════════════════════════════════════════════════════════════
        // 배경:
        //   Phase A의 단순 dedup은 ChunkLOD level change, streaming reload 등
        //   정당한 재생성 요청도 손실시킴. 이는 VRAM 관리와 streaming 속도에 치명적.
        //
        // 해결:
        //   - 진행 중 chunk에 대한 재요청은 Pending queue에 저장
        //   - 1차 완료 시 자동으로 Pending 요청 재실행
        //   - Coalescing: 같은 chunk의 여러 pending 요청은 최신 것만 유지
        //     (중간 stale 요청 폐기 → camera 이동 중 누적 요청 최적화)
        //
        // 안전성:
        //   - Recursive call (DispatchChunk → completion → DispatchChunk) 발생
        //     but coalescing으로 같은 chunk는 항상 1개 pending 
        //     → 1회 재실행 후 pending 없으면 종료 → stack overflow 없음
        // ═══════════════════════════════════════════════════════════════════════════════
        private struct PendingRequest
        {
            public ChunkRequestContext context;
            public int chunkSize;
            public float voxelSize;
            public Action<ChunkRequestContext, ComputeBuffer, ComputeBuffer> onGpuCompleted;
        }
        private Dictionary<Vector3Int, PendingRequest> _pendingRequests = new Dictionary<Vector3Int, PendingRequest>();

        /// <summary>
        /// [Phase B] chunk 완료 후 해당 chunk의 pending 요청이 있으면 자동 재실행.
        /// 호출 규칙: _inFlightChunks.Remove + IsBusy=false 이후에 호출해야 함.
        /// 토글: enablePendingRetryQueue=false 시 no-op.
        /// </summary>
        private void TryDispatchPending(Vector3Int chunkPos)
        {
            if (!enablePendingRetryQueue) return; // Phase B 토글 OFF

            if (_pendingRequests.TryGetValue(chunkPos, out var pending))
            {
                _pendingRequests.Remove(chunkPos);
                if (enableRaceFixDiagLogs)
                    Debug.Log($"[Dispatcher] PENDING RE-DISPATCH: {chunkPos} (coalesced from defer queue)");
                DispatchChunk(pending.context, pending.chunkSize, pending.voxelSize, pending.onGpuCompleted);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase C - Completion Cooldown 2026] 완료 직후 재요청 차단
        // ═══════════════════════════════════════════════════════════════════════════════
        // 배경 (발견된 문제):
        //   1차 DispatchChunk 완료 직후 (_inFlightChunks.Remove 이후) 
        //   같은 chunk에 2차 dispatch가 도착하면 Phase A+B가 차단 못 함.
        //   2차가 cache HIT 경로를 타면 AssignCachedMeshToScene 실행 →
        //   기존 MeshCollider의 pending physics bake가 "풀 재사용" 조건으로 skip →
        //   MeshCollider가 영원히 null로 남음.
        //
        // 로그 증거:
        //   "[DC] 청크 완성: (-7,-1,-5), 12076 quads" (L911, GPU 경로)
        //   "[DC-Cache] HIT: (-7,-1,-5)" (L732, 같은 chunk의 cache 경로!)
        //
        // 해결:
        //   1차 완료 직후 N ms 내 같은 chunk 재요청을 차단 (cooldown).
        //   Cache write가 안정되기 전의 race window를 차단.
        //   B-6 auto regen 또는 CaveChunkManager 재요청도 이 cooldown 적용됨 →
        //   사용자가 의도하지 않은 중복 dispatch 방어.
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Phase C] 최근 완료 chunk 기록. GPU 경로와 Cache HIT 경로의 완료 callback에서 호출.
        /// </summary>
        private void RecordCompletion(Vector3Int chunkPos)
        {
            if (!enableCompletionCooldown) return;
            _recentlyCompleted[chunkPos] = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// [Phase C] 최근 완료 chunk인지 확인. DispatchChunk 진입부에서 호출.
        /// 반환 true면 이번 요청 차단해야 함.
        /// </summary>
        private bool IsInCooldown(Vector3Int chunkPos)
        {
            if (!enableCompletionCooldown) return false;
            if (!_recentlyCompleted.TryGetValue(chunkPos, out float completedAt)) return false;
            
            float elapsed = Time.realtimeSinceStartup - completedAt;
            if (elapsed < completionCooldownSeconds)
            {
                // 아직 cooldown 중
                return true;
            }
            // Cooldown 종료 → 항목 제거 (주기적 청소 대신 lazy eviction)
            _recentlyCompleted.Remove(chunkPos);
            return false;
        }

        /// <summary>
        /// [Phase C] 주기적 청소 — Update에서 호출하여 expired 항목 제거.
        /// Dictionary 크기 제한 (장시간 플레이 누수 방지).
        /// </summary>
        private void CleanupExpiredCooldowns()
        {
            if (_recentlyCompleted.Count < 50) return; // 크기 작으면 skip

            float now = Time.realtimeSinceStartup;
            // 1개씩 제거 (per-frame cost 최소화)
            Vector3Int? toRemove = null;
            foreach (var kv in _recentlyCompleted)
            {
                if (now - kv.Value > completionCooldownSeconds * 2f)
                {
                    toRemove = kv.Key;
                    break;
                }
            }
            if (toRemove.HasValue)
                _recentlyCompleted.Remove(toRemove.Value);
        }
        // ═══════════════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-6] Auto Regeneration 메서드들
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [B-6] chunk 완료 시 현재 paramHash 기록. 이후 dirty 감지의 기준.
        /// DispatchChunk의 paramHash 계산 직후 또는 캐시 저장 직후 호출.
        /// </summary>
        private void RecordChunkParamHash(Vector3Int chunkPos, string paramHash,
                                           ChunkRequestContext ctx, int chunkSize, float voxelSize,
                                           Action<ChunkRequestContext, ComputeBuffer, ComputeBuffer> callback)
        {
            if (!enableAutoRegeneration) return; // B-6 토글 OFF

            _chunkParamHashes[chunkPos] = paramHash;
            // 마지막 요청 보관 → regen 시 같은 파라미터로 재요청
            _chunkLastRequests[chunkPos] = new PendingRequest
            {
                context = ctx,
                chunkSize = chunkSize,
                voxelSize = voxelSize,
                onGpuCompleted = callback
            };
        }

        /// <summary>
        /// [B-6] 현재 파라미터 기준으로 모든 기록된 chunk의 dirty 여부 감지.
        /// 외부에서 파라미터 변경 후 호출 (또는 주기적 자동 감지).
        /// </summary>
        public void DetectDirtyChunks(string currentParamHash)
        {
            if (!enableAutoRegeneration) return; // B-6 토글 OFF

            int dirtyCount = 0;
            foreach (var kv in _chunkParamHashes)
            {
                if (kv.Value != currentParamHash && !_dirtyChunks.Contains(kv.Key))
                {
                    _dirtyChunks.Enqueue(kv.Key);
                    dirtyCount++;
                }
            }
            if (dirtyCount > 0 && enableRegenDiagLogs)
                Debug.Log($"[B-6] DIRTY DETECTED: {dirtyCount} chunks 재생성 queue 등록 (total pending: {_dirtyChunks.Count})");
        }

        /// <summary>
        /// [B-6] 외부 단일 chunk dirty 마킹 (수동 또는 특정 chunk만 재생성 시).
        /// </summary>
        public void MarkChunkDirty(Vector3Int chunkPos)
        {
            if (!enableAutoRegeneration) return;
            if (!_dirtyChunks.Contains(chunkPos))
            {
                _dirtyChunks.Enqueue(chunkPos);
                if (enableRegenDiagLogs)
                    Debug.Log($"[B-6] MARK DIRTY: {chunkPos}");
            }
        }

        /// <summary>
        /// [B-6] Update 호출당 batch 크기만큼 dirty chunks 재dispatch.
        /// Phase A+B 경로 재사용 (중복 차단, pending queue 통합).
        /// </summary>
        private void ProcessRegenerationQueue()
        {
            if (!enableAutoRegeneration) return;
            if (_dirtyChunks.Count == 0) return;

            int processed = 0;
            int batchSize = Mathf.Max(1, regenerationBatchSize);

            while (processed < batchSize && _dirtyChunks.Count > 0)
            {
                Vector3Int chunkPos = _dirtyChunks.Dequeue();
                
                // 보관된 마지막 요청이 있어야 재dispatch 가능
                if (_chunkLastRequests.TryGetValue(chunkPos, out var lastReq))
                {
                    if (enableRegenDiagLogs)
                        Debug.Log($"[B-6] REGEN: {chunkPos} (batch {processed + 1}/{batchSize})");
                    
                    // DispatchChunk 경로 호출 → Phase A+B가 자동 처리
                    // (이미 진행 중이면 pending queue에 들어감)
                    DispatchChunk(lastReq.context, lastReq.chunkSize, lastReq.voxelSize, lastReq.onGpuCompleted);
                }
                else
                {
                    if (enableRegenDiagLogs)
                        Debug.LogWarning($"[B-6] REGEN SKIP: {chunkPos} 마지막 요청 기록 없음");
                }
                processed++;
            }
        }

        /// <summary>
        /// [B-6] Unity Update — B-6 dirty queue 자동 처리. 
        /// 다른 B-시리즈 작업도 필요 시 여기에 추가.
        /// [Phase C] Cooldown Dictionary 주기적 청소 (메모리 누수 방지).
        /// </summary>
        private void Update()
        {
            ProcessRegenerationQueue();
            CleanupExpiredCooldowns(); // [Phase C] 만료된 cooldown 항목 제거
        }
        // ═══════════════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-10] KPI 측정 메서드들
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [B-10] chunk 완료 이벤트 수집 (enableKpiMeasurement=true 시 동작).
        /// DispatchChunk의 완료 callback에서 호출.
        /// </summary>
        private void RecordKpiEvent(int vertexCount, int quadCount, float timeMs)
        {
            if (!enableKpiMeasurement) return;

            _kpiTotalChunkCount++;
            _kpiChunkGenTimes.Add(timeMs);
            if (quadCount == 0) _kpiEmptyChunkCount++;
            if (vertexCount > 20000) _kpiHugeChunkCount++;

            // Sample count 도달 시 리포트
            if (enableRegressionMode && _kpiTotalChunkCount >= kpiSampleChunkCount)
            {
                LogKpiReport();
                ResetKpiStats();
            }
        }

        /// <summary>
        /// [B-10] KPI 리포트 출력. 수동 호출 가능 (Inspector 버튼 등에서).
        /// </summary>
        public void LogKpiReport()
        {
            if (_kpiChunkGenTimes.Count == 0)
            {
                Debug.Log("[B-10] KPI: 데이터 없음");
                return;
            }

            float total = 0f; float max = 0f;
            foreach (var t in _kpiChunkGenTimes) { total += t; if (t > max) max = t; }
            float avg = total / _kpiChunkGenTimes.Count;

            float emptyRate = 100f * _kpiEmptyChunkCount / Mathf.Max(1, _kpiTotalChunkCount);
            float hugeRate = 100f * _kpiHugeChunkCount / Mathf.Max(1, _kpiTotalChunkCount);

            Debug.Log(
                $"<color=cyan>═══ [B-10] KPI REPORT ═══</color>\n" +
                $"Sampled chunks:    {_kpiTotalChunkCount}\n" +
                $"Avg gen time:      {avg:F2}ms (target: < 10ms)\n" +
                $"Max gen time:      {max:F2}ms (target: < 25ms)\n" +
                $"Empty chunks:      {_kpiEmptyChunkCount} ({emptyRate:F2}%, target: 0%)\n" +
                $"Huge chunks:       {_kpiHugeChunkCount} ({hugeRate:F2}%, target: 0%)\n" +
                $"KPI 판정:          {(avg < 10f && max < 25f && _kpiEmptyChunkCount == 0 && _kpiHugeChunkCount == 0 ? "<color=green>PASS ✓</color>" : "<color=red>REVIEW NEEDED</color>")}"
            );
        }

        /// <summary>
        /// [B-10] KPI 통계 리셋.
        /// </summary>
        public void ResetKpiStats()
        {
            _kpiChunkGenTimes.Clear();
            _kpiEmptyChunkCount = 0;
            _kpiHugeChunkCount = 0;
            _kpiTotalChunkCount = 0;
            _kpiStartTicks = System.DateTime.Now.Ticks;
            if (enableKpiMeasurement)
                Debug.Log("[B-10] KPI 통계 리셋.");
        }
        // ═══════════════════════════════════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════════════════════════════════

        // --- GPU 통신용 버퍼 ---
        private ComputeBuffer nodeBuffer;
        private ComputeBuffer edgeBuffer;
        private ComputeBuffer biomeBuffer; // [에러 조치] 다중 지대 파라미터 버퍼 추가

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 12-E + Phase 13] M5 + G2용 biome lookup buffer
        //
        //   _BiomeMeanOffsetLookup: biome별 평균 detailSDF offset (음수, M5)
        //   _BiomeAmpLookup:        biome별 max amp (양수, G2)
        //
        //   UpdateBiomeBuffer()에서 함께 채움 (globalBiomes 순회, noiseType별 hardcoded lookup)
        //   D2 보장: 모든 값 0이면 영향 없음
        // ═══════════════════════════════════════════════════════════════════════════════
        private ComputeBuffer biomeMeanOffsetBuffer;
        private ComputeBuffer biomeAmpBuffer;

        // 지형 연산용 공통 버퍼 (청크 생성 시 재사용)
        private ComputeBuffer voxelBuffer;
        // [Inspector] Domain Warp 진폭 — 천장/벽 형태 자연화 (권장: 0.5)
        [SerializeField] private float warpAmplitude = 0.5f;

        // [AABB 최적화 토글]
        [Header("AABB Optimization")]
        [Tooltip("D: 적응형 마진 (smin×3+warp+2). OFF=고정 10m")]
        public bool enableAdaptiveMargin = false;
        [Tooltip("C: CPU 사전 필터링 (청크별 노드/엣지). OFF=전체 순회")]
        public bool enableChunkPreFilter = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-7] Phase 2 Compatibility — 개별 기능별 ON/OFF 토글
        // ═══════════════════════════════════════════════════════════════════════════════
        // 각 기능을 독립적으로 OFF 하여 회귀 테스트/원인 분리 가능.
        // 기본값: 전체 true (원본 동작). OFF 시 해당 기능 compute shader에서 스킵.
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("B-7: Phase 2 Compatibility")]
        [Tooltip("바닥 Y clamp (FloorAltitude 아래는 rock 보장). OFF 시 통로 바닥 관통 가능.")]
        public bool enablePhase2FloorClamp = true;
        [Tooltip("천장 Y clamp (CeilAltitude 위는 rock 보장). OFF 시 통로 천장 관통 가능.")]
        public bool enablePhase2CeilClamp = true;
        [Tooltip("바닥 자연 요철 (bumpAmplitude 기반). OFF 시 완전 평평.")]
        public bool enablePhase2FloorBump = true;
        [Tooltip("표면 침식 노이즈 (erosionNoise). OFF 시 mesh 매끄러움.")]
        public bool enablePhase2Erosion = true;
        [Tooltip("Sinkhole 생성. OFF 시 수직 구멍 없음.")]
        public bool enablePhase2Sinkhole = true;
        [Tooltip("Ore flag 쓰기. OFF 시 ore 생성 안 됨.")]
        public bool enablePhase2Ore = true;
        // ═══════════════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G] Atomic Biome Sync Preset 부속 + 옵션 토글
        //   본 토글들은 D2 원칙 준수: default OFF/0 → byte-identical to Route_Astar.
        //   atomic preset (CaveManager.biomeSyncMode == GpuAligned)일 때 자동 ON.
        //   E4 Phase 1 (BlendDetailSuppression)은 atomic preset 외부 옵션 — 사용자 선택.
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("Phase 4.5-G: I7 Ecotone / E4 / H1 (Atomic-managed except E4 P1)")]

        [Tooltip("[I7] Ecotone SDF — blend 중심 영역 안전 SDF + Anchor 패턴.\n" +
                 "OFF (기본): byte-identical, ApplyBiomeDetail_Ecotone 호출 안 됨.\n" +
                 "ON: blendCentrality > threshold 영역에서 ecotoneDensity로 lerp.\n" +
                 "Anchor: typeA/B 중 Replacement (Case 2/3/5)면 baseSDF anchor 사용.\n" +
                 "atomic preset GpuAligned 시 자동 ON.\n" +
                 "★ atomic preset SingleSourceEcotone (γ) 또는 그 이상 (δ/ε) 진입 시\n" +
                 "  ★ 자동 OFF 강제 — Single-Source가 ecotone을 자동 처리하므로\n" +
                 "    중복 활성 시 노이즈 폭발 위험 (사용자 결함 보고).")]
        public bool enableEcotoneSDF = false;

        [Tooltip("[I7] Ecotone 활성 centrality 임계.\n" +
                 "0.3 (기본): blend 중심 영역만 활성. 권장.\n" +
                 "0.0: 모든 blend 영역 활성 (cost ↑).\n" +
                 "0.5+: 매우 좁은 중심만.")]
        [Range(0.0f, 0.7f)] public float ecotoneThreshold = 0.3f;

        [Tooltip("[E4 Phase 1] Blend Detail Suppression — 옵션 토글 (Anchor와 분리).\n" +
                 "OFF (기본, 권장): Stage 1-A 정통식 그대로, 자기 영역 detail 100% 보존 (R-VIS).\n" +
                 "ON: ampDampingFactor = lerp(0.95, 0.0, centrality) → 자기 영역 5% 감쇠 (R-VIS 위반).\n" +
                 "주의: I7 Anchor 패턴이 통상 Add↔Rep 미스매치를 처리하므로 ON 불필요.\n" +
                 "atomic preset에 포함 안 됨 — 사용자 명시적 결정 시에만 ON.")]
        public bool enableBlendDetailSuppression = false;

        [Tooltip("[E4 Phase 2] Blend Calm Detail — blend 중심 ripple 시각 보강.\n" +
                 "OFF (기본): byte-identical.\n" +
                 "ON: ±0.13m ripple × centrality × wallMask. P3/P4 안전.\n" +
                 "atomic preset GpuAligned 시 자동 ON.")]
        public bool enableBlendCalmDetail = false;

        [Tooltip("[H1] Disable Biome Blend (DEBUG) — biome 경계 hard step.\n" +
                 "OFF (기본): smooth blendWeight (P2 C¹).\n" +
                 "ON: blendWeight 0/1 강제 step. DEBUG 한정. 운영 빌드 OFF.")]
        public bool debugDisableBiomeBlend = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G P2] Single-Source-of-Noise (γ State 전용)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("Phase 4.5-G P2: Single-Source Noise (γ State)")]
        [Tooltip("[Single-Source] biome blend 모델 선택.\n" +
                 "OFF (기본): Legacy/GpuAligned dual blend (densityA/B 모두 evaluate, lerp).\n" +
                 "ON: Single-Source — 한 noise만 사용, amp = (1-c)^power\n" +
                 "    blend center 자연 blank, P1/P2 자동 안전, GPU 비용 절감.\n" +
                 "atomic preset SingleSourceEcotone (γ) 진입 시 자동 ON.")]
        public bool enableSingleSourceNoise = false;

        [Tooltip("[Single-Source FadePower] amp 감쇠 곡선 power.\n" +
                 "1.0: linear (1-c). 자기영역 100% → blend center 0%.\n" +
                 "2.0 (사용자 권장): quadratic (1-c)^2 — faded 영역 길게, blend center 좁게.\n" +
                 "3.0: cubic — 매우 짧은 blank zone.\n" +
                 "0.5: sqrt — blank zone 길게 (faded 짧게).\n" +
                 "사용자 요청: blend center 최소화 + faded 길게 → 2.0 권장.")]
        [Range(0.5f, 3f)] public float singleSourceFadePower = 2.0f;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 12-A] ★ 옵션 ζ — Boundary Blank Zone
        //   blend boundary (bw=0.5) 근처 명시적 평탄 zone (typeA/typeB ApplyBiomeDetail 호출 안 함)
        //   사용자 의도: "능선/sediment/기둥이 평탄화 영역으로 접근하면서 점진 zero화"
        //   D2 보장: 0이면 OFF (기존 동작 — 1 voxel만 blank)
        //   atomic preset η/ε에서 자동 ON 권장 (γ/δ는 D2 보장 위해 OFF 유지)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Phase 12-A ζ] Boundary Blank Zone 폭 (bw 단위).\n" +
                 "0.0: OFF (기존 동작 — bw==0.5 한 지점만 blank).\n" +
                 "0.05: ~16m 평탄 zone (약함).\n" +
                 "0.10 (★ 권장): ~33m 평탄 zone (자연스런 transition).\n" +
                 "0.20: ~66m 강한 평탄 (단조 가능).\n" +
                 "0.30: ~100m (★ 너무 단조).\n" +
                 "Karst↔Columnar surface jump 해결, blend 영역 명시적 baseSDF.")]
        [Range(0f, 0.30f)] public float blankZoneWidth = 0f;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 12-B] ★ 옵션 G1 — Tunnel Surface Protection
        //   fBm 양방향 [-1,+1] 양수 noise만 baseSDF 깊이 비례 약화 (음수 외부 확장 그대로)
        //   guardFactor = smoothstep(-_TunnelGuardMargin, _OuterFadeMargin, baseSDF)
        //   D2 보장: tunnelGuardMargin = 0이면 OFF (guardFactor 항상 1.0)
        //   atomic preset η/ε에서 자동 ON 권장
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Phase 12-B G1] Tunnel Surface Protection — 통로 안쪽 보호 거리 (m).\n" +
                 "0.0: OFF (기존 동작 — fBm noise 통로 침투 가능).\n" +
                 "1.0: 약한 보호 (잔존 침해 0.30m).\n" +
                 "1.5 (★ 권장): 표준 보호 (잔존 침해 0.06m).\n" +
                 "2.0: 강한 보호 (잔존 침해 0).\n" +
                 "3.0: 매우 강함 (표면 ±2m 약화 → 단조).\n" +
                 "통로 영역 침해 절대 금지 위해 도입.")]
        [Range(0f, 3f)] public float tunnelGuardMargin = 0f;

        [Tooltip("[Phase 12-B G1] 외부 fade 끝 거리 (m).\n" +
                 "guardFactor = smoothstep(-tunnelGuardMargin, outerFadeMargin, baseSDF).\n" +
                 "0.5 (★ 권장): 표면 0.5m 외부에서 guard 100%.\n" +
                 "tunnelGuardMargin > 0일 때만 의미.")]
        [Range(0f, 2f)] public float outerFadeMargin = 0.5f;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 12-D] ★ M1 — Columnar DC Offset 보상 (단층 본질 해결)
        //
        //   문제: Columnar Voronoi (-= ridge × ...) → 항상 음수 → 평균 -0.26m
        //         → Karst 자기영역 (대칭, 평균 0)과 surface baseline 0.34m 차이
        //         → ζ Blank Zone 통과 후 vertical level step (단층) 발생
        //
        //   해결: ridgeBalanced = ridge - _ColumnarDCOffset (default 0.5 = ridge 평균)
        //         결과: ridgeBalanced 평균 0 → 두 biome surface baseline 일치
        //
        //   D2 보장: 0 = OFF (ridge 그대로)
        //   atomic preset η/ε에서 자동 0.5
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Phase 12-D M1] Columnar DC Offset — 단층 본질 해결.\n" +
                 "0.0: OFF (ridge 그대로, 기존 동작 — 단층 발생).\n" +
                 "0.5 (★ 권장): ridge - 0.5 → 평균 0 (대칭) → Karst와 baseline 일치.\n" +
                 "Karst↔Columnar boundary에서 vertical level step 사라짐.\n" +
                 "G1 (TunnelGuard)과 자연 정합 — Columnar 양수는 G1이 차단.")]
        [Range(0f, 1f)] public float columnarDCOffset = 0f;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 12-E] ★ M5 — Base Level Normalization
        //
        //   biome별 평균 detailSDF offset만큼 baseSDF 사전 보정 → 모든 biome 통로
        //   surface가 같은 baseline → vertical 단층 사라짐.
        //
        //   M1 (Columnar 직접)과 직교 — M5는 max() 패턴 (Sediment, Colonnade, Rocky)에 효과.
        //   Colonnade NEVER MODIFIED 룰 준수 — case 안 건드리고 baseSDF만 보정.
        //
        //   biomeMeanOffsets는 noiseType별 평균값 (분석적 추정).
        //   D2 보장: enableBaseSDFLevelOffset=false면 OFF.
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Phase 12-E M5] Base Level Normalization — 모든 case 단층 해결.\n" +
                 "OFF (default): 기존 동작.\n" +
                 "ON: baseSDF -= biomeMeanOffset → 모든 biome 통로 baseline 통일.\n" +
                 "Colonnade NEVER MODIFIED 룰 준수 — case 안 건드림.")]
        public bool enableBaseSDFLevelOffset = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 13] ★ G2 — Tunnel Width Expansion
        //
        //   biome별 amp 비례 baseSDF 사전 확장 → noise가 들어올 공간 확보 →
        //   디테일 100% 유지 + 통로 침해 방지.
        //
        //   G1 (Attenuation) 보완 — G1은 noise 약화, G2는 통로 확장.
        //   결합 시 가장 안전.
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Phase 13 G2] Tunnel Width Expansion 강도.\n" +
                 "0.0: OFF (기존 동작).\n" +
                 "0.5 (★ 권장): biome amp의 50% 확장 — 통로 변화 절제.\n" +
                 "1.0: full amp 확장 — 디테일 100% 유지하면서 통로 보호.\n" +
                 "G1과 결합 권장 (양수 noise는 G1이 차단, 음수는 외부 확장 그대로).")]
        [Range(0f, 1f)] public float tunnelExpansionFactor = 0f;

        [Tooltip("[Phase 13 G2] Tunnel Width Expansion 최대 cap (m).\n" +
                 "1.5 (★ 권장): P4 위반 biome (Sediment 7m 등) 방지.\n" +
                 "tunnelExpansionFactor > 0일 때만 의미.")]
        [Range(0f, 5f)] public float maxTunnelExpansion = 1.5f;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 14] ★ N1 — Node Chamber Biome Lock
        //
        //   ★ chamber radius 안 voxel을 노드 위치 biome으로 강제 통일
        //
        //   ★ 사용자 단층 본질 해결:
        //     - F2 boundary 근처 노드 (★ 16% — 큰 맵에서 일반)
        //     - F10 큰 chamber (radius 16m, 안에서 macroBiome 변화 ~0.10)
        //     - F19 boundary cluster (다수 boundary 노드)
        //
        //   메커니즘:
        //     1. 가장 가까운 노드까지 XZ 거리 dist 계산
        //     2. dist < lockRadius (= node.radius × radiusMul) 시
        //     3. lockStrength = smoothstep (innerFrac × lockRadius, lockRadius, dist)
        //     4. blendWeight = lerp(blendWeight, nodeOwnBW, lockStrength)
        //
        //   D2 보장: nodeChamberLockRadiusMul = 0이면 OFF (byte-identical)
        //   atomic preset: η/ε 자동 1.5 (chamber radius × 1.5 = ~12m default)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Phase 14 N1] Node Chamber Biome Lock — chamber 단위 biome 통일.\n" +
                 "0.0: OFF (기존 동작, byte-identical).\n" +
                 "1.5 (★ 권장): chamber radius × 1.5 = ~12m lock 영역.\n" +
                 "★ F2 boundary 노드 / F10 큰 chamber / F19 boundary cluster 단층 해결.")]
        [Range(0f, 3f)] public float nodeChamberLockRadiusMul = 0f;

        [Tooltip("[Phase 14 N1] Lock 강도 fade 시작 fraction.\n" +
                 "0.5 (★ 권장): innerR = lockRadius × 0.5 까지 완전 lock, 그 외 smoothstep fade.\n" +
                 "값 작을수록 부드러운 fade, 클수록 가장자리만 fade.")]
        [Range(0f, 1f)] public float nodeChamberLockInnerFrac = 0.5f;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G P3] Columnar (Case 1) Soft-Terrace
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Soft-Terrace] Columnar (Case 1) terracedY 처리 모드.\n" +
                 "OFF (기본): floor() hard step (Route_Astar baseline). 매 0.333m마다 SDF 0.4~0.8m 점프 (P2 위반).\n" +
                 "ON: smoothstep 기반 soft transition. P2 C¹ 회복, wedge 제거.\n" +
                 "atomic preset SingleSourceEcotone (γ) 진입 시 자동 ON.")]
        public bool enableColumnarSoftTerrace = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G P1] Columnar Voronoi Noise — Case 1 식 재설계 (옵션 A)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[Columnar Voronoi] Case 1 노이즈 식 선택.\n" +
                 "OFF (기본): Legacy fBm + terracedY (Route_Astar baseline, byte-identical).\n" +
                 "    Additive (+=) — 통로에 ridge 자라남 (사용자 보고 결함).\n" +
                 "ON: Voronoi 2D Subtractive — 자연 주상절리 시각.\n" +
                 "    cellSize 2.5m, sharpness 5, amp 0.4m. P1~P4 모두 안전.\n" +
                 "    Subtractive (-=) — 벽이 식각, 통로 가로막지 않음.\n" +
                 "atomic preset SingleSourceEcotone (γ) 진입 시 자동 ON.\n" +
                 "옵션 A 적용: γ에서만 ON, α/β는 Legacy fBm 보존 (비교군 대조).")]
        public bool enableColumnarVoronoiNoise = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [FORMAT_VERSION 12] Erosion 3D Signed Narrow-mask 모드
        // ═══════════════════════════════════════════════════════════════════════════════
        // [히스토리] FV 11의 enableErosion3DBiasOnly는 단방향(-|noise|) 주입으로 detail 손실
        //            ("뭉툭함") 유발하여 사용자 보고에 따라 은퇴. 이 토글로 대체.
        //
        // enablePhase2Erosion == true 전제. 이 토글은 Erosion 내부 모드 선택만 담당.
        //   OFF (false): 2-octave signed XZ-only erosion (원본 동작, byte-identical)
        //   ON  (true) : 3-octave signed 3D erosion + narrow mask
        //                - 양방향 detail 유지 (기존 signed noise의 장점 보존)
        //                - 3D noise → Y-correlation 제거 (vertical stacking 차단)
        //                - Narrow mask (|d| > 0.67m에서 mask=0) → 영향 범위 ~4 voxel
        //                  → sign flip은 가능하지만 여러 cell로 확산 불가 → 파편 방지
        //                - 파편 이론 v28: P(좁은 통로) factor 억제로 AND 붕괴
        //                - Nyquist 안전 (λ 1.67/0.83/0.50m 모두 ≥ 0.375m)
        //                - 원칙 4 준수 (total amp 0.35m < 1.2m)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("FORMAT_VERSION 12: Erosion 3D Signed Narrow-mask")]
        [Tooltip("Erosion 3D 3-octave signed + narrow mask 모드. " +
                 "OFF: 원본 2-oct signed XZ-only (byte-identical). " +
                 "ON: 양방향 detail 유지 + vertical stacking 차단 + 파편 방지. enablePhase2Erosion=true 전제.")]
        public bool enableErosion3DSignedNarrow = false; // 기본 false → backup과 동일

        // ═══════════════════════════════════════════════════════════════════════════════
        // [FORMAT_VERSION 11] Per-Voxel DepthLayer Blend (인접 청크 SDF C⁰ 연속 확보)
        // ═══════════════════════════════════════════════════════════════════════════════
        // 배경: GetLayerSettings(chunkBasePos.y)가 chunk당 단일 layer 반환 → 14개 uniform이
        //       hard step으로 점프 → Y 인접 chunk 간 SDF 불연속 → mesh 교차 (돌/흙 overlap)
        //
        // 동작: OFF (false) → 기존 Dispatcher-SetFloat 14개 경로 (byte-identical)
        //       ON  (true)  → depthLayers 배열을 ComputeBuffer로 shader에 전달
        //                     각 voxel에서 worldPos.y 기준 soft-blended layer 해석
        //
        // 적용 범위 (이번 patch): FloorClamp / CeilClamp / FloorDetail (주 증상)
        // 제외 (향후 patch): Sinkhole, EvaluateSkeleton 내부 tunnelScale 등
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("FORMAT_VERSION 11: Per-Voxel Layer Blend")]
        [Tooltip("DepthLayer per-voxel blend (Y 인접 chunk SDF C⁰ 연속). " +
                 "OFF: chunkBasePos.y 단일 layer (원본). " +
                 "ON: worldPos.y 기반 인접 layer smoothstep blend → mesh 교차 해소.")]
        public bool enablePerVoxelLayerBlend = false; // 기본 false → backup과 동일

        [Range(0.5f, 10.0f)]
        [Tooltip("Layer 경계 부근 blend 반경 (m). 기본 2.0. " +
                 "너무 작으면 경계 티 남, 너무 크면 layer 정체성 흐려짐.")]
        public float layerBlendWidth = 2.0f;

        // 내부: DepthLayer GPU 전송 버퍼 (ON 시에만 사용)
        private ComputeBuffer _depthLayerBuffer;
        private int _depthLayerBufferCount = 0;

        // [Race Condition Fix 토글 - Phase A/B]
        [Header("Race Condition Fix")]
        [Tooltip("Phase A: 같은 chunk 동시 dispatch 차단 (in-flight dedup). " +
                 "OFF 시 원본 동작(race condition 발생 가능).")]
        public bool enableInFlightDedup = true;
        [Tooltip("Phase B: 차단된 재요청을 pending queue에 저장 후 자동 재실행 (ChunkLOD/streaming 호환). " +
                 "Phase A 활성화 필요. OFF 시 정당한 재요청(LOD 변경 등) 손실.")]
        public bool enablePendingRetryQueue = true;
        [Tooltip("Race Fix 진단 로그 (START, DEFER, PENDING RE-DISPATCH, PreFilter count, Readback). " +
                 "디버깅 완료 후 OFF 권장 (로그 spam 방지).")]
        public bool enableRaceFixDiagLogs = true;

        // [Phase C - Completion Cooldown 토글]
        [Header("Phase C: Completion Cooldown")]
        [Tooltip("완료 직후 같은 chunk 재요청 차단 기간. OFF 시 cache HIT 중복 dispatch → MeshCollider null 재발 가능.")]
        public bool enableCompletionCooldown = true;
        [Range(0.05f, 2.0f)]
        [Tooltip("Cooldown 지속 시간 (초). 너무 길면 정당한 재요청 지연. 권장 0.5.")]
        public float completionCooldownSeconds = 0.5f;

        // [Phase C] 완료된 chunk의 타임스탬프
        private Dictionary<Vector3Int, float> _recentlyCompleted = new Dictionary<Vector3Int, float>();

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-6] Auto Regeneration — paramHash dirty 감지 기반 자동 재생성
        // ═══════════════════════════════════════════════════════════════════════════════
        // 배경:
        //   BiomeData, DepthLayer, shader 토글 변경 시 기존 chunk가 stale 상태.
        //   수동 cache clear 없이 자동으로 영향받는 chunk만 재생성 필요.
        //
        // 동작:
        //   1. 각 chunk 완료 시 paramHash 기록 (_chunkParamHashes)
        //   2. 현재 파라미터의 paramHash와 비교
        //   3. 불일치 = dirty → _dirtyChunks queue 등록
        //   4. 매 프레임 regenerationBatchSize개씩 재요청 (Phase A+B 통합)
        //
        // 안전성:
        //   - 완료된 chunk만 dirty 체크 (진행 중은 skip)
        //   - 재요청은 Phase A+B가 중복 차단 + pending 저장
        //   - 대량 dirty 시 batch 단위 처리 → frame drop 방지
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("B-6: Auto Regeneration")]
        [Tooltip("paramHash 변경 감지 시 영향받는 chunk 자동 재생성. " +
                 "OFF 시 수동 cache clear 필요.")]
        public bool enableAutoRegeneration = true;
        [Range(1, 10)]
        [Tooltip("프레임당 재생성 처리 chunk 수. 높을수록 반영 빠르지만 frame drop 가능.")]
        public int regenerationBatchSize = 2;
        [Tooltip("B-6 진단 로그 (dirty 감지, regen trigger). 완료 후 OFF 권장.")]
        public bool enableRegenDiagLogs = true;

        // [B-6] chunk별 생성 시점의 paramHash 기록 (dirty 감지용)
        private Dictionary<Vector3Int, string> _chunkParamHashes = new Dictionary<Vector3Int, string>();
        // [B-6] dirty chunk queue — 매 프레임 batch 처리
        private Queue<Vector3Int> _dirtyChunks = new Queue<Vector3Int>();
        // [B-6] chunk → (context, size, vSize, callback) 보관 (재요청용)
        private Dictionary<Vector3Int, PendingRequest> _chunkLastRequests = new Dictionary<Vector3Int, PendingRequest>();
        // ═══════════════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-8] Stitcher Tuning — ChunkSeamStitcher 파라미터 제어
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("B-8: Stitcher Tuning")]
        [Tooltip("Chunk 경계 seam 해결 (ChunkSeamStitcher). OFF 시 경계에 틈 보임.")]
        public bool enableStitching = true;
        [Range(0.3f, 2.0f)]
        [Tooltip("Snap 거리 배수 (voxelSize × multiplier). " +
                 "기본 0.75, 조정 시 B-8 체크리스트 참조.")]
        public float snapDistMultiplier = 0.75f;
        [Tooltip("Stitcher 상세 로그 (Stitch+Rebake). Stitcher 디버깅 시에만 ON.")]
        public bool enableStitchLogs = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Track E — FORMAT_VERSION 12] ChunkSeamStitcher Normal Averaging
        // ═══════════════════════════════════════════════════════════════════════════════
        // 목적: Chunk overlap 영역 인접 chunk 간 vertex normal 미세 차이(ε)가 shader
        //       triplanar blend의 비선형성(pow(|normal|, sharpness))에 의해 증폭되어
        //       dirt↔rock binary flip을 유발하는 문제(Issue 2, Image 1·2) 해소.
        //
        // 동작: enableNormalAveraging=false (기본) → 기존 position-only 평균화 (byte-identical)
        //       enableNormalAveraging=true          → 매칭 vertex 쌍의 normal도 평균화 + 정규화
        //
        // 전제: enableStitching=true, 동등 LOD 인접 chunk 쌍 (Coarse-Fine skip 제외)
        // 범위: mesh.normals만 수정. vertices/triangles 불변.
        // 비용: 무시할 수준 (stitch 호출당 몇 normal 연산 추가).
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("Stitcher가 매칭된 경계 vertex 쌍의 normal도 평균화. " +
                 "OFF: position만 평균 (원본, byte-identical). " +
                 "ON: normal 평균화로 chunk 경계 triplanar blend 불연속 완화.")]
        public bool enableNormalAveraging = false;
        // ═══════════════════════════════════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-10] Regression Test Mode — KPI 측정 및 회귀 테스트 모드
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("B-10: Regression Test Mode")]
        [Tooltip("KPI 측정 활성화 (avg/max chunk gen time, 메모리, 버그 카운트).")]
        public bool enableKpiMeasurement = false;
        [Tooltip("회귀 테스트 모드 — 샘플 chunks로 KPI 집계 후 자동 리포트.")]
        public bool enableRegressionMode = false;
        [Range(10, 500)]
        [Tooltip("KPI 집계 샘플 chunk 수.")]
        public int kpiSampleChunkCount = 100;

        // [B-10] KPI 수집용 필드
        private List<float> _kpiChunkGenTimes = new List<float>();
        private int _kpiEmptyChunkCount = 0;       // V=0 empty (race 의심)
        private int _kpiHugeChunkCount = 0;        // V > 20000 (race 의심)
        private int _kpiTotalChunkCount = 0;
        private long _kpiStartTicks = 0;
        // ═══════════════════════════════════════════════════════════════════════════════

        // [P2 1단계 — Warp Normalization]
        [Header("P2 — Warp Normalization")]
        [Tooltip("ON: warp × (voxelSize / 0.125) — LOD 간 voxel 단위 변위 일관성. OFF: warpAmplitude 원본값 (기존)")]
        public bool enableWarpNormalization = false;
        [Tooltip("정규화 기준 voxelSize (Fine 기준). 이 값에서 warp 변위가 원본과 동일")]
        [SerializeField] private float warpNormalizationBaseVoxelSize = 0.125f;

        [Header("Phase 1 — SDF Feature Toggles")]
        [Tooltip("방/통로 크기 배율 (SO에서 값 읽기)")]
        public bool enableScaling = false;
        [Tooltip("per-edge 폭 ±20% 변형")]
        public bool enableWidthVariation = false;
        [Tooltip("U자 퇴적 (SO에서 값 읽기)")]
        public bool enableSediment = false;
        [Tooltip("바닥 표면 디테일 노이즈 (SO에서 값 읽기)")]
        public bool enableFloorDetail = false;

        // CPU 사전 필터링용 임시 버퍼
        private ComputeBuffer filteredNodeBuffer, filteredEdgeBuffer;
        private int filteredNodeCount, filteredEdgeCount;

        private ComputeBuffer triangleBuffer;
        private ComputeBuffer oreBuffer;
        private ComputeBuffer triCountBuffer;
        private ComputeBuffer oreCountBuffer;

        // [🔥 추가: 마칭 큐브 룩업 테이블 버퍼]
        private ComputeBuffer mcEdgeTableBuffer;
        private ComputeBuffer mcTriangleTableBuffer;

        // 메모리 재할당 체크용 변수
        private int currentPointsPerAxis = 0;

        // 커널 캐싱
        private int kernelGenerateDensity;
        private int kernelSimulateErosion;
        private int kernelGenerateMesh;

        private void Awake()
        {
            InitializeKernels();
            SetupMarchingCubesTables(); // [🔥 추가] 테이블 버퍼 초기화
            UpdateBiomeBuffer(); // 초기화 시 바이옴 버퍼를 무조건 1회 셋업합니다.
        }

        private void OnEnable()
        {
            // 에디터에서 기획자가 바이옴 데이터를 수정하면 즉시 감지하여 GPU 버퍼를 갱신합니다.
            CaveBiomeData.OnBiomeModified += UpdateBiomeBuffer;
            // [Gate 5 Phase A.3] BiomeSDF_ProfileSO 변경도 실시간 반영
            BiomeSDF_ProfileSO.OnProfileModified += UpdateBiomeBuffer;
            // [Gate 5 Phase B.0] SubBiomeProfileSO 변경도 실시간 반영
            //   B.0에서는 shader 소비 없으므로 UpdateBiomeBuffer로 묶어두되,
            //   Phase 5 (B.1~.15)에서 별도 SubBiome 버퍼 갱신 메서드로 분리 예정.
            SubBiomeProfileSO.OnSubBiomeModified += UpdateBiomeBuffer;
        }

        private void OnDisable()
        {
            // 메모리 누수 방지
            CaveBiomeData.OnBiomeModified -= UpdateBiomeBuffer;
            // [Gate 5 Phase A.3] 구독 해제
            BiomeSDF_ProfileSO.OnProfileModified -= UpdateBiomeBuffer;
            // [Gate 5 Phase B.0] 구독 해제
            SubBiomeProfileSO.OnSubBiomeModified -= UpdateBiomeBuffer;
        }

        private void InitializeKernels()
        {
            kernelGenerateDensity = densityShader.FindKernel("GenerateDensity");
            kernelSimulateErosion = densityShader.FindKernel("SimulateErosion");
            kernelGenerateMesh = marchingCubesShader.FindKernel("GenerateMesh");
        }

        // [🔥 추가] MarchingCubesTables 데이터를 GPU 버퍼로 로드
        private void SetupMarchingCubesTables()
        {
            mcEdgeTableBuffer = new ComputeBuffer(256, sizeof(int));
            mcEdgeTableBuffer.SetData(MarchingCubesTables.EdgeTable);

            mcTriangleTableBuffer = new ComputeBuffer(4096, sizeof(int));
            mcTriangleTableBuffer.SetData(MarchingCubesTables.TriangleTable);
        }

        /// <summary>
        /// [에러 조치] CaveBiomeSettings에 등록된 바이옴 에셋들을 구조체 배열로 패킹하여 GPU에 업로드합니다.
        /// </summary>
        public void UpdateBiomeBuffer()
        {
            if (caveSettings == null || caveSettings.globalBiomes == null || caveSettings.globalBiomes.Count == 0)
            {
                Debug.LogWarning("[CaveComputeDispatcher] 바이옴 데이터가 세팅되지 않았습니다. 안전한 기본값을 주입합니다.");
                if (biomeBuffer != null) { biomeBuffer.Release(); biomeBuffer = null; }

                biomeBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(BiomeParamData)));
                biomeBuffer.SetData(new BiomeParamData[] { new BiomeParamData { blendDamping = 1.0f } });
                return;
            }

            int count = caveSettings.globalBiomes.Count;
            int stride = Marshal.SizeOf(typeof(BiomeParamData));

            // 배열 크기가 달라졌다면 재할당을 위해 기존 버퍼를 파괴
            if (biomeBuffer != null && biomeBuffer.count != count)
            {
                biomeBuffer.Release();
                biomeBuffer = null;
            }

            if (biomeBuffer == null)
            {
                biomeBuffer = new ComputeBuffer(count, stride);
            }

            // ScriptableObject에서 순수 구조체 데이터만 추출
            // [Gate 5 Phase A.3] BiomeSDF_ProfileSO optional override
            //   caveSettings.sdfProfiles[i]가 null이 아니면 해당 profile의 GetBiomeParamData() 사용.
            //   null이면 기존 CaveBiomeData.GetStructData() fallback → 규칙 #6 byte-identical.
            //   sdfProfiles.Count != globalBiomes.Count 인 경우도 안전 (index 범위 검사).
            BiomeParamData[] biomeDataArray = new BiomeParamData[count];
            bool hasProfiles = (caveSettings.sdfProfiles != null && caveSettings.sdfProfiles.Count > 0);
            for (int i = 0; i < count; i++)
            {
                // [A.3] SDF profile 우선 — 할당된 슬롯만
                if (hasProfiles && i < caveSettings.sdfProfiles.Count
                    && caveSettings.sdfProfiles[i] != null)
                {
                    biomeDataArray[i] = caveSettings.sdfProfiles[i].GetBiomeParamData();
                    continue;
                }

                // Fallback: 레거시 CaveBiomeData 경로
                if (caveSettings.globalBiomes[i] != null)
                {
                    biomeDataArray[i] = caveSettings.globalBiomes[i].GetStructData();
                }
                else
                {
                    biomeDataArray[i] = new BiomeParamData { blendDamping = 1.0f };
                }
            }

            biomeBuffer.SetData(biomeDataArray);

            // ═══════════════════════════════════════════════════════════════
            // [Phase 12-E + Phase 13] ★ M5/G2 buffer 채우기
            //
            //   noiseType별 hardcoded lookup:
            //     0 Karst:    mean=0,    amp=1.125
            //     1 Columnar: mean=0,    amp=0.7   (M1 적용 후 평균 0)
            //     2 Sediment: mean=-1.5, amp=7.0   (P4 위반 cap 적용)
            //     3 Colonnade: mean=-0.5, amp=2.1
            //     4 Rocky:    mean=-1.2, amp=3.0
            //     5 Canyon:   mean=-0.3, amp=0.4
            //     6 Marine:   mean=-0.4, amp=1.6
            //     기타:       mean=0,   amp=0     (안전 default)
            //
            //   배열 크기 = globalBiomes.Count, index = i (biome index)
            //   값은 noiseType별 hardcoded — biome.noiseType으로 lookup
            // ═══════════════════════════════════════════════════════════════
            float[] meanArr = new float[count];
            float[] ampArr = new float[count];
            for (int i = 0; i < count; i++)
            {
                int nt = (caveSettings.globalBiomes[i] != null)
                    ? caveSettings.globalBiomes[i].noiseType : 0;
                switch (nt)
                {
                    case 0:  // Karst
                        meanArr[i] = 0f;     ampArr[i] = 1.125f; break;
                    case 1:  // Columnar (M1 적용 후 평균 0)
                        meanArr[i] = 0f;     ampArr[i] = 0.7f;   break;
                    case 2:  // Sediment
                        meanArr[i] = -1.5f;  ampArr[i] = 7.0f;   break;
                    case 3:  // Colonnade (NEVER MODIFIED — M5만으로 보정)
                        meanArr[i] = -0.5f;  ampArr[i] = 2.1f;   break;
                    case 4:  // Rocky
                        meanArr[i] = -1.2f;  ampArr[i] = 3.0f;   break;
                    case 5:  // Canyon
                        meanArr[i] = -0.3f;  ampArr[i] = 0.4f;   break;
                    case 6:  // Marine
                        meanArr[i] = -0.4f;  ampArr[i] = 1.6f;   break;
                    default:
                        meanArr[i] = 0f;     ampArr[i] = 0f;     break;
                }
            }

            // Buffer 크기 갱신
            if (biomeMeanOffsetBuffer != null && biomeMeanOffsetBuffer.count != count)
            {
                biomeMeanOffsetBuffer.Release(); biomeMeanOffsetBuffer = null;
            }
            if (biomeMeanOffsetBuffer == null)
                biomeMeanOffsetBuffer = new ComputeBuffer(count, sizeof(float));
            biomeMeanOffsetBuffer.SetData(meanArr);

            if (biomeAmpBuffer != null && biomeAmpBuffer.count != count)
            {
                biomeAmpBuffer.Release(); biomeAmpBuffer = null;
            }
            if (biomeAmpBuffer == null)
                biomeAmpBuffer = new ComputeBuffer(count, sizeof(float));
            biomeAmpBuffer.SetData(ampArr);
        }

        /// <summary>
        /// Phase 2에서 완성된 글로벌 노드 그래프 데이터를 GPU 버퍼로 패킹합니다.
        /// </summary>
        public void SetupGraphBuffers(List<NodeData> nodes, List<EdgeData> edges)
        {
            ReleaseGraphBuffers();

            int nodeStride = Marshal.SizeOf(typeof(NodeData));
            int edgeStride = Marshal.SizeOf(typeof(EdgeData));

            int nodeCount = Mathf.Max(1, nodes.Count);
            nodeBuffer = new ComputeBuffer(nodeCount, nodeStride);
            if (nodes.Count > 0) nodeBuffer.SetData(nodes);

            int edgeCount = Mathf.Max(1, edges.Count);
            edgeBuffer = new ComputeBuffer(edgeCount, edgeStride);
            if (edges.Count > 0) edgeBuffer.SetData(edges);

            Debug.Log($"<color=cyan>[ComputeDispatcher]</color> 그래프 데이터 GPU 버퍼 패킹 완료.");
        }

        /// <summary>
        /// 단일 청크에 대한 밀도 생성, 침식 시뮬레이션, 마칭 큐브 연산을 연속 실행합니다.
        /// </summary>
        public void DispatchChunk(ChunkRequestContext context, int chunkSize, float voxelSize, Action<ChunkRequestContext, ComputeBuffer, ComputeBuffer> onGpuCompleted)
        {
            // [Phase A - Diag] 진입 로그 — 같은 chunk 중복 호출 추적용
            if (enableRaceFixDiagLogs)
                Debug.Log($"[Dispatcher] START: {context.ChunkPos} IsBusy={IsBusy} InFlight={_inFlightChunks.Contains(context.ChunkPos)}");

            // [Phase C] Completion Cooldown — 최근 완료 chunk의 즉시 재요청 차단.
            //   Phase A+B 앞단에서 차단하여 cache HIT race 원천 방지.
            //   GPU 경로 완료 직후 cache 저장 전 race window에서의 중복 dispatch를 막음.
            if (IsInCooldown(context.ChunkPos))
            {
                if (enableRaceFixDiagLogs)
                {
                    float elapsed = Time.realtimeSinceStartup - _recentlyCompleted[context.ChunkPos];
                    Debug.LogWarning($"[Dispatcher] COOLDOWN REJECT: {context.ChunkPos} (완료 후 {elapsed*1000:F0}ms / cooldown {completionCooldownSeconds*1000:F0}ms)");
                }
                return;
            }

            // [Phase A - Dedup] 같은 chunk가 이미 진행 중인지 체크
            //   토글 OFF 시: 원본 race 발생 가능 (디버깅/비교용)
            //   토글 ON + Phase B OFF: 단순 차단 (ChunkLOD 손실 주의)
            //   토글 ON + Phase B ON: 차단 후 pending 저장 (완전 해결, default)
            if (enableInFlightDedup && _inFlightChunks.Contains(context.ChunkPos))
            {
                if (enablePendingRetryQueue)
                {
                    // [Phase B - Retry Queue] 정당한 재요청 (ChunkLOD 변경, streaming 등) 보존
                    //   Coalescing: 같은 chunk의 여러 pending은 최신 것만 유지.
                    _pendingRequests[context.ChunkPos] = new PendingRequest
                    {
                        context = context,
                        chunkSize = chunkSize,
                        voxelSize = voxelSize,
                        onGpuCompleted = onGpuCompleted
                    };
                    if (enableRaceFixDiagLogs)
                        Debug.Log($"[Dispatcher] DEFER: {context.ChunkPos} 이미 진행 중 → pending queue 저장 (coalesced)");
                }
                else
                {
                    // Phase A만 활성: 단순 차단 (정당한 재요청 손실)
                    if (enableRaceFixDiagLogs)
                        Debug.LogWarning($"[Dispatcher] ⚠️ {context.ChunkPos} 이미 진행 중. 중복 요청 무시 (Phase B OFF).");
                }
                return;
            }

            if (IsBusy)
            {
                // 다른 chunk가 GPU 사용 중 (정상 serialization). pending 불필요 — 이 chunk는 다시 호출될 것.
                if (enableRaceFixDiagLogs)
                    Debug.LogWarning($"[ComputeDispatcher] ⚠️ GPU가 다른 chunk 처리 중. {context.ChunkPos} 이번 요청 skip (재요청 기대).");
                return;
            }

            // [Phase A - Dedup] 진행 중 목록 등록 (토글 조건부)
            //   참고: enableInFlightDedup=false여도 exit path의 Remove/TryDispatchPending은 
            //         HashSet/Dictionary가 비어있으면 no-op이므로 안전.
            if (enableInFlightDedup) _inFlightChunks.Add(context.ChunkPos);
            IsBusy = true; // 락(Lock) 걸기

            if (nodeBuffer == null || edgeBuffer == null)
            {
                Debug.LogError("[ComputeDispatcher] 그래프 버퍼가 초기화되지 않았습니다.");
                _inFlightChunks.Remove(context.ChunkPos); // [Phase A - Dedup] 실패 시 해제 (no-op safe)
                IsBusy = false;
                RecordCompletion(context.ChunkPos); // [Phase C] 실패도 cooldown으로 flood 방지
                TryDispatchPending(context.ChunkPos); // [Phase B] 실패해도 pending 재시도 (내부 토글 체크)
                return;
            }

            // [에러 조치] 바이옴 버퍼 널 체크 및 안전 보장
            if (biomeBuffer == null)
            {
                UpdateBiomeBuffer();
            }

            // [🚨 3번 조치 완수] 조명 이음새(Normal Seam) 방지를 위한 +2 패딩(Double Ghost Voxel) 도입
            int pointsPerAxis = chunkSize + 2;
            AllocateTempBuffers(pointsPerAxis, chunkSize);

            Vector3 chunkBasePos = new Vector3(context.ChunkPos.x, context.ChunkPos.y, context.ChunkPos.z) * (chunkSize * voxelSize);
            DepthLayer currentLayer = caveSettings.GetLayerSettings(chunkBasePos.y);

            // ----------------------------------------------------
            // 커널 1: 밀도장 연산 (Density Field Generation)
            // ----------------------------------------------------
            densityShader.SetBuffer(kernelGenerateDensity, "_VoxelBuffer", voxelBuffer);
            densityShader.SetBuffer(kernelGenerateDensity, "_NodeBuffer", nodeBuffer);
            densityShader.SetInt("_NodeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.nodesData.Count : 0);
            densityShader.SetBuffer(kernelGenerateDensity, "_EdgeBuffer", edgeBuffer);
            densityShader.SetInt("_EdgeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.edgesData.Count : 0);

            // [🔥 에러 조치: 바이옴 파라미터 및 버퍼 명시적 주입]
            densityShader.SetBuffer(kernelGenerateDensity, "_BiomeBuffer", biomeBuffer);
            densityShader.SetInt("_BiomeCount", biomeBuffer.count);
            densityShader.SetFloat("_MacroBiomeScale", Mathf.Max(caveSettings.macroBiomeScale, 1.0f));

            densityShader.SetVector("_ChunkBasePosition", chunkBasePos);
            densityShader.SetInt("_ChunkSize", chunkSize);
            densityShader.SetInt("_PointsPerAxis", pointsPerAxis);
            densityShader.SetFloat("_VoxelSize", voxelSize);
            densityShader.SetInt("_DebugStage", (int)caveSettings.debugStage);

            // 레이어별 SDF 파라미터 적용
            densityShader.SetFloat("_SdfSmoothness", currentLayer.sdfSmoothness);
            densityShader.SetFloat("_FloorAltitude", currentLayer.minAltitude);
            densityShader.SetFloat("_CeilAltitude", currentLayer.maxAltitude);
            densityShader.SetFloat("_FloorBlendRadius", currentLayer.floorBlendRadius);
            densityShader.SetFloat("_CeilBlendRadius", currentLayer.ceilBlendRadius);

            // 바닥 요철 파라미터
            densityShader.SetFloat("_FloorBumpAmplitude", currentLayer.floorBumpAmplitude);
            densityShader.SetFloat("_FloorBumpFrequency", currentLayer.floorBumpFrequency);

            // 3D 스레드 실행 (PointsPerAxis를 기준으로 넉넉하게 할당하여 유령 복셀 구역 연산)
            int threadGroups3D = Mathf.CeilToInt(pointsPerAxis / 8.0f);

            // [P2 1단계] warp 정규화: warp × (voxelSize / baseVoxelSize)
            //   OFF: warpAmplitude 그대로 (원본)
            //   ON:  LOD 간 voxel 단위 변위 일관성 보장 (Fine 기준 4 voxels)
            float effectiveWarp = enableWarpNormalization
                ? warpAmplitude * (voxelSize / Mathf.Max(0.001f, warpNormalizationBaseVoxelSize))
                : warpAmplitude;
            // [Gate 5 Phase A.1] warpAmplitudeOverride fallback
            //   DepthLayer.warpAmplitudeOverride > 0.01 이면 해당 값으로 override
            //   0 이면 기존 effectiveWarp 유지 → 규칙 #6 byte-identical
            if (currentLayer.warpAmplitudeOverride > 0.01f)
                effectiveWarp = currentLayer.warpAmplitudeOverride;
            densityShader.SetFloat("_WarpAmplitude", effectiveWarp);

            // [Gate 5 Phase A.1] canyonCeilingHeight fallback
            //   DepthLayer.canyonCeilingHeight > 0.01 이면 해당 값, 아니면 25.0 (기존 하드코딩)
            //   규칙 #6: 기존 DepthLayer asset (모두 0) → 25.0 → byte-identical
            float effectiveMaxCanyonH = currentLayer.canyonCeilingHeight > 0.01f
                                         ? currentLayer.canyonCeilingHeight : 25.0f;
            densityShader.SetFloat("_MaxCanyonHeight", effectiveMaxCanyonH);

            // [Gate 5 Phase E-β.2] _CurvatureAmplitude 주입
            //   CaveTerrainConfig.enableCurvedTunnels × curvatureAmplitude 양자 AND
            //   둘 중 하나라도 0이면 0 → HLSL fast-path에서 sdCapsule 직접 (byte-identical)
            float effectiveCurvAmp = 0f;
            float effectiveFloorVar = 0f;
            float effectiveCeilVar = 0f;
            if (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null)
            {
                var tc = CaveManager.Instance.chunkManager.terrainConfig;
                effectiveCurvAmp = tc.enableCurvedTunnels ? tc.curvatureAmplitude : 0f;

                // [Gate 5 Phase E-β.4/.5] Floor/Ceil variation — biome × multiplier
                //   biome.floorVarAmp (CaveBiomeData) × tc.floorVariationMultiplier (전역)
                //   둘 중 하나라도 0이면 0 → byte-identical
                if (tc.enableFloorVariation
                    && caveSettings != null && caveSettings.globalBiomes != null
                    && caveSettings.globalBiomes.Count > 0 && caveSettings.globalBiomes[0] != null)
                {
                    effectiveFloorVar = caveSettings.globalBiomes[0].floorVarAmp * tc.floorVariationMultiplier;
                }
                if (tc.enableCeilVariation
                    && caveSettings != null && caveSettings.globalBiomes != null
                    && caveSettings.globalBiomes.Count > 0 && caveSettings.globalBiomes[0] != null)
                {
                    effectiveCeilVar = caveSettings.globalBiomes[0].ceilVarAmp * tc.ceilVariationMultiplier;
                }
            }
            densityShader.SetFloat("_CurvatureAmplitude", effectiveCurvAmp);
            densityShader.SetFloat("_FloorVariationAmp", effectiveFloorVar);
            densityShader.SetFloat("_CeilVariationAmp", effectiveCeilVar);

            // [AABB] 적응형 마진 계산 — warp 정규화 시 effectiveWarp 사용 (규칙 #1)
            float aabbMargin = 10.0f; // 기본 = 원본
            if (enableAdaptiveMargin)
            {
                float sminMax = 2.0f; // 바이옴 sminStrength 최대 추정
                if (caveSettings.globalBiomes?.Count > 0 && caveSettings.globalBiomes[0] != null)
                    sminMax = caveSettings.globalBiomes[0].GetStructData().sminStrength;
                aabbMargin = sminMax * 3f + effectiveWarp + 2f;
            }
            densityShader.SetFloat("_AABBMargin", aabbMargin);

            // [Phase 1] SDF 토글
            densityShader.SetInt("_EnableScaling", enableScaling ? 1 : 0);
            densityShader.SetInt("_EnableWidthVariation", enableWidthVariation ? 1 : 0);
            densityShader.SetInt("_EnableSediment", enableSediment ? 1 : 0);
            densityShader.SetFloat("_TunnelWidthScale", currentLayer.tunnelWidthScale > 0.01f ? currentLayer.tunnelWidthScale : 1f);
            densityShader.SetFloat("_RoomSizeScale", currentLayer.roomSizeScale > 0.01f ? currentLayer.roomSizeScale : 1f);
            densityShader.SetFloat("_SedimentAmplitude", currentLayer.sedimentAmplitude);
            densityShader.SetInt("_EnableFloorDetail", enableFloorDetail ? 1 : 0);
            densityShader.SetFloat("_FloorDetailAmplitude", currentLayer.floorDetailAmplitude);
            densityShader.SetFloat("_FloorDetailFrequency", currentLayer.floorDetailFrequency);
            densityShader.SetFloat("_FloorDetailRadius", currentLayer.floorDetailRadius);

            // [B-7] Phase 2 기능별 토글 GPU 전달
            densityShader.SetInt("_EnablePhase2FloorClamp", enablePhase2FloorClamp ? 1 : 0);
            densityShader.SetInt("_EnablePhase2CeilClamp", enablePhase2CeilClamp ? 1 : 0);
            densityShader.SetInt("_EnablePhase2FloorBump", enablePhase2FloorBump ? 1 : 0);
            densityShader.SetInt("_EnablePhase2Erosion", enablePhase2Erosion ? 1 : 0);
            densityShader.SetInt("_EnablePhase2Sinkhole", enablePhase2Sinkhole ? 1 : 0);
            densityShader.SetInt("_EnablePhase2Ore", enablePhase2Ore ? 1 : 0);

            // ═══════════════════════════════════════════════════════════════════════════════
            // [Phase 4.5-G Atomic Preset] BiomeSyncMode 적용 (3-State)
            //   Legacy:               Inspector 값 그대로 (수동 테스트 허용)
            //   GpuAligned:           I7 / E4 P2 강제 ON (dual blend 유지)
            //   SingleSourceEcotone:  Single-Source + Soft-Terrace + E4 P2 ON, I7 OFF (대체됨)
            //
            //   E4 P1 (BlendDetailSuppression) / H1 (DisableBiomeBlend)은 atomic 외부 — Inspector만
            // ═══════════════════════════════════════════════════════════════════════════════
            bool gpuAligned = (CaveManager.Instance != null && CaveManager.Instance.IsGpuAligned);
            // ★ Phase 4.5-G δ/ε: Single-Source 활성 검사를 5-state 호환으로 확장
            //   IsSingleSourceActive = γ (SingleSourceEcotone) || δ (SingleSourceEcotonePlus) || ε (FullMerge)
            //   → δ가 γ의 다음 버전으로 동작 (Single-Source / Soft-Terrace / Voronoi 모두 활성)
            bool singleSource = (CaveManager.Instance != null && CaveManager.Instance.IsSingleSourceActive);
            bool anyEnhanced = (CaveManager.Instance != null && CaveManager.Instance.IsAnyEnhanced);

            // ═══════════════════════════════════════════════════════════════════
            // [Phase 4.5-G I7 Ecotone — Single-Source 우선 강제 격리]
            //
            //   문제 (사용자 보고):
            //     이전 식: ((gpuAligned && !singleSource) || enableEcotoneSDF) ? 1 : 0
            //     → Inspector 토글이 OR 조건이라 γ/δ/ε에서도 강제 ON 가능
            //     → Single-Source (1 noise) + I7 Ecotone (2 noise) 동시 활성
            //     → 노이즈 중복 폭발 → 파편 발생
            //
            //   수정: singleSource 활성 시 enableEcotoneSDF 토글 무시 (강제 OFF)
            //     β GpuAligned: Inspector + atomic preset 모두 가능
            //     γ/δ/ε:        ★ 강제 OFF (Single-Source가 ecotone 대체)
            //
            //   원리: Single-Source는 한 voxel에 한 case만 평가하여 ecotone을 자동
            //         처리. I7 Ecotone (양 case Add-only)는 중복이며 노이즈 폭발 야기.
            // ═══════════════════════════════════════════════════════════════════
            int effectiveEcotoneSDF;
            if (singleSource)
            {
                // ★ γ/δ/ε에서 enableEcotoneSDF 토글 무시 — 강제 OFF
                effectiveEcotoneSDF = 0;
            }
            else
            {
                // β GpuAligned: atomic preset OR Inspector 토글
                effectiveEcotoneSDF = ((gpuAligned) || enableEcotoneSDF) ? 1 : 0;
            }
            // E4 P2 ripple — β / γ 모두 ON (시각 보강)
            int effectiveBlendCalm       = (anyEnhanced || enableBlendCalmDetail) ? 1 : 0;
            // E4 P1 / H1 — atomic 외부
            int effectiveBlendDetailSupp = enableBlendDetailSuppression ? 1 : 0;
            int effectiveDisableBlend    = debugDisableBiomeBlend ? 1 : 0;

            // ★ Single-Source — γ에서만 ON
            int effectiveSingleSource    = (singleSource || enableSingleSourceNoise) ? 1 : 0;
            // ★ Soft-Terrace — γ에서 자동 ON
            int effectiveSoftTerrace     = (singleSource || enableColumnarSoftTerrace) ? 1 : 0;
            // ★ Columnar Voronoi — γ에서 자동 ON (옵션 A: α/β는 Legacy fBm 비교군)
            int effectiveColumnarVoronoi = (singleSource || enableColumnarVoronoiNoise) ? 1 : 0;

            densityShader.SetInt("_EnableEcotoneSDF", effectiveEcotoneSDF);
            densityShader.SetFloat("_EcotoneThreshold", ecotoneThreshold);
            densityShader.SetInt("_EnableBlendDetailSuppression", effectiveBlendDetailSupp);
            densityShader.SetInt("_EnableBlendCalmDetail", effectiveBlendCalm);
            densityShader.SetInt("_DisableBiomeBlend", effectiveDisableBlend);

            // [P2] Single-Source uniform
            densityShader.SetInt("_BiomeBlendMode", effectiveSingleSource);
            densityShader.SetFloat("_SingleSourceFadePower", singleSourceFadePower);

            // [P3] Columnar Soft-Terrace uniform
            densityShader.SetInt("_EnableColumnarSoftTerrace", effectiveSoftTerrace);

            // [P1] Columnar Voronoi Noise uniform (옵션 A)
            densityShader.SetInt("_EnableColumnarVoronoiNoise", effectiveColumnarVoronoi);

            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Phase 7+11] FragmentSafe + Case 4 Voronoi atomic 토글
            //
            //   default 0 (OFF) — fast-path, 기존 동작
            //   η DebugAware OR ε FullMerge에서만 1 (atomic preset에 의해)
            //
            //   각 토글은 CaveNodeGraphBuilder.Inspector에서 접근:
            //     Phase 7: enableEdgeIDDecorrelation
            //     Phase 11: enableCase4Voronoi
            // ═══════════════════════════════════════════════════════════════
            float effCase4Voronoi = 0f;
            float effEdgeIDDecorr = 0f;
            if (CaveManager.Instance != null && CaveManager.Instance.IsDebugAwareOrLater)
            {
                var gb = CaveNodeGraphBuilder.Instance;
                if (gb != null)
                {
                    effCase4Voronoi = gb.enableCase4Voronoi ? 1f : 0f;
                    effEdgeIDDecorr = gb.enableEdgeIDDecorrelation ? 1f : 0f;
                }
            }
            densityShader.SetFloat("_EnableCase4Voronoi", effCase4Voronoi);
            densityShader.SetFloat("_EnableEdgeIDDecorrelation", effEdgeIDDecorr);

            // ═══════════════════════════════════════════════════════════════
            // [Phase 12] ★ ζ Blank Zone + G1 Tunnel Surface Protection
            //
            //   Inspector 값을 그대로 GPU로 전송. atomic preset (η/ε)에서
            //   default 0 → 권장값으로 변경 (NodeGraphBuilder의 BiomeSyncMode
            //   case 분기에서 enable* 토글 + 권장값 설정).
            //
            //   Inspector default 0 = OFF → byte-identical 보장 (D2)
            //   atomic preset η/ε에서 자동 활성:
            //     blankZoneWidth: 0 → 0.10 (~33m 평탄)
            //     tunnelGuardMargin: 0 → 1.5m
            //     outerFadeMargin: 0.5 (default 그대로)
            //   γ/δ는 OFF 유지 (D2 보장 우선)
            // ═══════════════════════════════════════════════════════════════
            densityShader.SetFloat("_BlankZoneWidth", blankZoneWidth);
            densityShader.SetFloat("_TunnelGuardMargin", tunnelGuardMargin);
            densityShader.SetFloat("_OuterFadeMargin", outerFadeMargin);
            densityShader.SetFloat("_ColumnarDCOffset", columnarDCOffset);  // [Phase 12-D] M1

            // [Phase 12-E] ★ M5 — Base Level Normalization
            densityShader.SetFloat("_EnableBaseSDFLevelOffset", enableBaseSDFLevelOffset ? 1f : 0f);
            if (biomeMeanOffsetBuffer != null)
                densityShader.SetBuffer(kernelGenerateDensity, "_BiomeMeanOffsetLookup", biomeMeanOffsetBuffer);

            // [Phase 13] ★ G2 — Tunnel Width Expansion
            densityShader.SetFloat("_TunnelExpansionFactor", tunnelExpansionFactor);
            densityShader.SetFloat("_MaxTunnelExpansion", maxTunnelExpansion);
            if (biomeAmpBuffer != null)
                densityShader.SetBuffer(kernelGenerateDensity, "_BiomeAmpLookup", biomeAmpBuffer);

            // ═══════════════════════════════════════════════════════════════════════════════
            // [Phase 14] ★ N1 — Node Chamber Biome Lock
            //   ★ 사용자 단층 본질 해결 (F2/F10/F19)
            //   D2 보장: nodeChamberLockRadiusMul = 0이면 GPU shader에서 전체 skip
            // ═══════════════════════════════════════════════════════════════════════════════
            densityShader.SetFloat("_NodeChamberLockRadiusMul", nodeChamberLockRadiusMul);
            densityShader.SetFloat("_NodeChamberLockInnerFrac", nodeChamberLockInnerFrac);
            // ═══════════════════════════════════════════════════════════════════════════════

            // [Gate 5 Phase A.4] DomainWarp 확장 주입
            //   Y scale + 재귀 warp. 둘 다 0이면 기존 XZ-only warp → byte-identical
            float effectiveWarpYScale = 0f;
            float effectiveWarpRecursive = 0f;
            if (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null)
            {
                var tc2 = CaveManager.Instance.chunkManager.terrainConfig;
                effectiveWarpYScale = tc2.enableWarpY ? tc2.warpYScale : 0f;
                effectiveWarpRecursive = tc2.enableWarpRecursive ? 1f : 0f;
            }
            densityShader.SetFloat("_WarpYScale", effectiveWarpYScale);
            densityShader.SetFloat("_WarpRecursive", effectiveWarpRecursive);

            // [Gate 5 Phase A.8] Stalactite master toggle
            //   Case 0 (Karst) 천장에 종유석 추가. 기본 OFF (byte-identical)
            float effectiveStalactite = 0f;
            if (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null)
            {
                effectiveStalactite = CaveManager.Instance.chunkManager.terrainConfig.enableStalactite ? 1f : 0f;
            }
            densityShader.SetFloat("_EnableStalactite", effectiveStalactite);

            // [Gate 5 Phase 4-1 사전 조치] Case 6 Marine tideLineY 주입
            //   biome 0의 tideLineY 사용 (현재 single-biome 가정)
            //   Case 6 이외에서는 참조되지 않음 → byte-identical
            float effectiveTideLineY = 0f;
            if (caveSettings != null && caveSettings.globalBiomes != null
                && caveSettings.globalBiomes.Count > 0 && caveSettings.globalBiomes[0] != null)
            {
                effectiveTideLineY = caveSettings.globalBiomes[0].tideLineY;
            }
            densityShader.SetFloat("_TideLineY", effectiveTideLineY);

            // [Gate 5 Phase E-α.4] Primitive Routing 주입
            //   전역 토글 × biome 0 enum. 토글 OFF 또는 enum 0 → byte-identical
            float effectivePrimRouting = 0f;
            int effectiveTunnelPrim = 0;
            int effectiveRoomPrim = 0;
            float effectiveCliffJoint = 0f;  // [E-α.8]
            if (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null)
            {
                var tc3 = CaveManager.Instance.chunkManager.terrainConfig;
                if (tc3.enablePrimitiveRouting)
                {
                    effectivePrimRouting = 1f;
                    if (caveSettings != null && caveSettings.globalBiomes != null
                        && caveSettings.globalBiomes.Count > 0 && caveSettings.globalBiomes[0] != null)
                    {
                        effectiveTunnelPrim = (int)caveSettings.globalBiomes[0].tunnelPrimitive;
                        effectiveRoomPrim = (int)caveSettings.globalBiomes[0].roomPrimitive;
                    }
                }
                effectiveCliffJoint = tc3.enableCliffJoint ? 1f : 0f;  // [E-α.8]
            }
            densityShader.SetFloat("_EnablePrimitiveRouting", effectivePrimRouting);
            densityShader.SetInt("_TunnelPrimitive", effectiveTunnelPrim);
            densityShader.SetInt("_RoomPrimitive", effectiveRoomPrim);
            densityShader.SetFloat("_EnableCliffJoint", effectiveCliffJoint);  // [E-α.8]

            // [Gate 5 Phase A.9] Sinkhole master toggle (기존 _EnablePhase2Sinkhole과 보완)
            //   두 토글 모두 ON이어야 실제 sinkhole 생성
            densityShader.SetFloat("_EnableSinkhole", 1f);  // 기본 활성, 세부는 _EnablePhase2Sinkhole

            // ═══════════════════════════════════════════════════════════════
            // [FORMAT_VERSION 11] 신규 토글 + DepthLayer 버퍼 전달
            //   Toggle OFF 시 shader의 기존 uniform이 그대로 사용됨 → byte-identical
            //   Toggle ON 시에만 _DepthLayerBuffer 내용이 SDF 계산에 영향
            // ═══════════════════════════════════════════════════════════════
            densityShader.SetInt("_EnableErosion3DSignedNarrow", enableErosion3DSignedNarrow ? 1 : 0);
            densityShader.SetInt("_EnablePerVoxelLayerBlend", enablePerVoxelLayerBlend ? 1 : 0);
            densityShader.SetFloat("_LayerBlendWidth", Mathf.Max(0.01f, layerBlendWidth));
            BindDepthLayerBuffer(kernelGenerateDensity);

            densityShader.Dispatch(kernelGenerateDensity, threadGroups3D, threadGroups3D, threadGroups3D);

            // ═══════════════════════════════════════════════════════════════
            // [pB-4 Week 0] Dual Contouring 분기
            // 밀도장(커널 1)은 위에서 이미 생성 완료.
            // useDualContouring=true이면 MC(커널 2) 대신 DC 3커널을 실행.
            // useDualContouring=false이면 이 블록을 완전히 건너뛰어 기존 MC 동작 100% 유지.
            // ═══════════════════════════════════════════════════════════════
            var dcExtension = GetComponent<DCPipelineExtension>();
            if (dcExtension != null && dcExtension.useDualContouring && dcExtension.IsInitialized)
            {
                // ================================================================
                // [N-2] DiskCache — 캐시 HIT 시 GPU 파이프라인 완전 생략
                // ================================================================
                var diskCache = GetComponent<CaveDiskCache>();
                if (diskCache != null && diskCache.enableDiskCache)
                {
                    DepthLayer cacheLayer = caveSettings.GetLayerSettings(chunkBasePos.y);
                    float cacheEffectiveWarp = enableWarpNormalization
                        ? warpAmplitude * (voxelSize / 0.125f)
                        : warpAmplitude;

                    var meshBuilder = GetComponent<DCMeshBuilder>();
                    // [FORMAT_VERSION 11] allLayerHash: depthLayers 배열 전체의 해시
                    //   Per-voxel Layer Blend ON 시 인접 layer의 내용도 현재 chunk의
                    //   SDF 결과에 영향 → neighbor layer 변경 감지용 별도 해시 필요.
                    //   cacheLayer(단일 current layer)만으론 neighbor 변경 감지 불가.
                    int allLayerHash = ComputeAllLayerHash();

                    // [Gate 5 Phase E-β.3.5] paramHash curvature — effective × biome amp
                    //   effectiveCurvAmp: enableCurvedTunnels × curvatureMultiplier (CaveTerrainConfig)
                    //   biomeCurvAmp:     globalBiomes[0].curvatureAmp (CaveBiomeData)
                    //   paramHash 값 = 둘의 곱 → 어느 쪽 변경이든 cache 자동 무효화
                    //   E-β.7에서 다중 biome sampling 시 전체 biome hash로 확장 예정
                    float biomeCurvAmp = 0f;
                    if (caveSettings != null && caveSettings.globalBiomes != null
                        && caveSettings.globalBiomes.Count > 0 && caveSettings.globalBiomes[0] != null)
                    {
                        biomeCurvAmp = caveSettings.globalBiomes[0].curvatureAmp;
                    }
                    float paramCurvAmp = effectiveCurvAmp * biomeCurvAmp;

                    // [Gate 5 Phase E-β.9] Floor/Ceil variation — 이미 effective (biome × multiplier) 계산됨
                    //   effectiveFloorVar, effectiveCeilVar는 앞서 계산됨 (같은 scope)
                    string paramHash = diskCache.ComputeParamHash(
                        caveSettings.seed, voxelSize, chunkSize,
                        cacheLayer,
                        enableScaling, enableWidthVariation, enableSediment, enableFloorDetail,
                        enableWarpNormalization, cacheEffectiveWarp,
                        meshBuilder != null && meshBuilder.enableReducedSmoothing,
                        meshBuilder != null && meshBuilder.enableFloorSmoothingJob,
                        dcExtension.enableCompressedHermite, dcExtension.enableCompressedVertex,
                        3.0f, // _DCNormalAmplify 기본값
                        // [Phase 1/2/3-A/3-B] 기본값 전달 (기존 호출 준수)
                        false, false, false, false, 1.0f,
                        false, false, false, 0.0f,
                        false,
                        // [FORMAT_VERSION 11] 신규 토글 4개
                        enableErosion3DSignedNarrow,
                        enablePerVoxelLayerBlend,
                        layerBlendWidth,
                        allLayerHash,
                        // [Gate 5 Phase E-β.3.5] effective × biome curvature
                        paramCurvAmp,
                        // [Gate 5 Phase E-β.9] Floor/Ceil variation (effective = biome × multiplier)
                        effectiveFloorVar,
                        effectiveCeilVar,
                        // [Gate 5 Phase A.12] A.4 WarpY/Recursive + A.8 Stalactite
                        effectiveWarpYScale,
                        effectiveWarpRecursive,
                        effectiveStalactite,
                        // [Gate 5 Phase E-α.10] Primitive routing + Cliff joint
                        effectivePrimRouting,
                        effectiveTunnelPrim,
                        effectiveRoomPrim,
                        effectiveCliffJoint
                    );
                    string cacheKey = diskCache.GetCacheKey(context.ChunkPos, paramHash);

                    if (diskCache.HasCache(cacheKey))
                    {
                        // ── 캐시 HIT: 디스크에서 로드, GPU 완전 생략 ──
                        diskCache.LoadAsync(cacheKey, (cachedData) =>
                        {
                            if (cachedData == null)
                            {
                                // 로드 실패 → 캐시 파일 삭제, 다음 사이클에서 GPU 재생성
                                Debug.LogWarning($"[DC-Cache] 로드 실패, 캐시 삭제: {cacheKey}");
                                try { System.IO.File.Delete(diskCache.GetCachePath(cacheKey)); } catch { }
                                context.State = ChunkState.Completed;
                                onGpuCompleted?.Invoke(context, null, null);
                                _inFlightChunks.Remove(context.ChunkPos); // [Phase A - Dedup]
                                IsBusy = false;
                                RecordCompletion(context.ChunkPos); // [Phase C] cooldown — 실패도 flood 방지
                                TryDispatchPending(context.ChunkPos); // [Phase B] pending 재시도
                                return;
                            }

                            var data = cachedData.Value;
                            Mesh mesh = CaveDiskCache.BuildMeshFromCache(data, $"DCChunk_{context.ChunkPos}");
                            context.FeatureTypes = data.featureTypes;

                            if (meshBuilder != null)
                            {
                                meshBuilder.AssignCachedMeshToScene(mesh, context, chunkSize, voxelSize,
                                    (completedCtx) =>
                                    {
                                        if (_verboseDiagLogging)
                                        {
                                            Debug.Log($"[DC-Cache] HIT: {context.ChunkPos}");
                                        }
                                        onGpuCompleted?.Invoke(completedCtx, null, null);
                                        _inFlightChunks.Remove(context.ChunkPos); // [Phase A - Dedup]
                                        IsBusy = false;
                                        RecordCompletion(context.ChunkPos); // [Phase C] cooldown 시작
                                        TryDispatchPending(context.ChunkPos); // [Phase B] pending 재시도
                                    });
                            }
                            else
                            {
                                context.State = ChunkState.Completed;
                                onGpuCompleted?.Invoke(context, null, null);
                                _inFlightChunks.Remove(context.ChunkPos); // [Phase A - Dedup]
                                IsBusy = false;
                                RecordCompletion(context.ChunkPos); // [Phase C] cooldown 시작
                                TryDispatchPending(context.ChunkPos); // [Phase B] pending 재시도
                            }
                        });
                        return; // GPU 파이프라인 생략
                    }

                    // ── 캐시 MISS: 인스턴스 필드 설정 → 기존 DC 경로 실행 → 완료 시 저장 ──
                    _pendingCacheKey = cacheKey;
                    _pendingDiskCache = diskCache;
                    // fall through to existing DC code below
                }
                else
                {
                    _pendingCacheKey = null;
                    _pendingDiskCache = null;
                }

                // ── DC 파이프라인 (DiskCache 유무 무관, 기존 코드) ──
                // [FIX-H] DC는 +3 패딩 (Seamless Overlap)
                int dcPointsPerAxis = chunkSize + 3;
                AllocateTempBuffers(dcPointsPerAxis, chunkSize, isDCMode: true);
                int dcTG = Mathf.CeilToInt(dcPointsPerAxis / 8.0f);

                // DC용 밀도장 재생성 (dcBasePos = -voxelSize 오프셋으로 오버랩)
                Vector3 dcBasePos = chunkBasePos - new Vector3(voxelSize, voxelSize, voxelSize);
                densityShader.SetBuffer(kernelGenerateDensity, "_VoxelBuffer", voxelBuffer);

                // [Rank 2] CPU 사전 필터링
                if (enableChunkPreFilter)
                {
                    float chunkWorldSize = chunkSize * voxelSize;
                    PreFilterForChunk(dcBasePos, chunkWorldSize + voxelSize * 3, aabbMargin + 5f);
                    BindFilteredBuffers(kernelGenerateDensity);
                }
                else
                {
                    densityShader.SetBuffer(kernelGenerateDensity, "_NodeBuffer", nodeBuffer);
                    densityShader.SetInt("_NodeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.nodesData.Count : 0);
                    densityShader.SetBuffer(kernelGenerateDensity, "_EdgeBuffer", edgeBuffer);
                    densityShader.SetInt("_EdgeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.edgesData.Count : 0);
                }
                densityShader.SetBuffer(kernelGenerateDensity, "_BiomeBuffer", biomeBuffer);
                densityShader.SetInt("_BiomeCount", biomeBuffer.count);
                densityShader.SetFloat("_MacroBiomeScale", Mathf.Max(caveSettings.macroBiomeScale, 1.0f));
                densityShader.SetVector("_ChunkBasePosition", dcBasePos);
                densityShader.SetInt("_ChunkSize", chunkSize);
                densityShader.SetInt("_PointsPerAxis", dcPointsPerAxis);
                densityShader.SetFloat("_VoxelSize", voxelSize);
                densityShader.SetInt("_DebugStage", (int)caveSettings.debugStage);
                densityShader.SetFloat("_SdfSmoothness", currentLayer.sdfSmoothness);
                densityShader.SetFloat("_FloorAltitude", currentLayer.minAltitude);
                densityShader.SetFloat("_CeilAltitude", currentLayer.maxAltitude);
                densityShader.SetFloat("_FloorBlendRadius", currentLayer.floorBlendRadius);
                densityShader.SetFloat("_CeilBlendRadius", currentLayer.ceilBlendRadius);
                densityShader.SetFloat("_FloorBumpAmplitude", currentLayer.floorBumpAmplitude);
                densityShader.SetFloat("_FloorBumpFrequency", currentLayer.floorBumpFrequency);
                // [P2 1단계] effectiveWarp는 MC 분기에서 이미 계산됨 (같은 DispatchChunk 호출 내)
                //   [A.1] warpAmplitudeOverride도 앞서 계산 시 반영됨
                densityShader.SetFloat("_WarpAmplitude", effectiveWarp);
                // [Gate 5 Phase A.1] canyonCeilingHeight fallback (두 번째 dispatch 경로에도 동일 주입)
                //   effectiveMaxCanyonH도 앞서 이미 계산됨 (같은 scope)
                densityShader.SetFloat("_MaxCanyonHeight", effectiveMaxCanyonH);
                // [Gate 5 Phase E-β.2] _CurvatureAmplitude 주입 (같은 scope 재사용)
                densityShader.SetFloat("_CurvatureAmplitude", effectiveCurvAmp);
                // [Gate 5 Phase E-β.4/.5] Floor/Ceil variation (같은 scope 재사용)
                densityShader.SetFloat("_FloorVariationAmp", effectiveFloorVar);
                densityShader.SetFloat("_CeilVariationAmp", effectiveCeilVar);
                densityShader.SetFloat("_AABBMargin", aabbMargin);
                densityShader.SetInt("_EnableScaling", enableScaling ? 1 : 0);
                densityShader.SetInt("_EnableWidthVariation", enableWidthVariation ? 1 : 0);
                densityShader.SetInt("_EnableSediment", enableSediment ? 1 : 0);
                densityShader.SetFloat("_TunnelWidthScale", currentLayer.tunnelWidthScale > 0.01f ? currentLayer.tunnelWidthScale : 1f);
                densityShader.SetFloat("_RoomSizeScale", currentLayer.roomSizeScale > 0.01f ? currentLayer.roomSizeScale : 1f);
                densityShader.SetFloat("_SedimentAmplitude", currentLayer.sedimentAmplitude);
                densityShader.SetInt("_EnableFloorDetail", enableFloorDetail ? 1 : 0);
                densityShader.SetFloat("_FloorDetailAmplitude", currentLayer.floorDetailAmplitude);
                densityShader.SetFloat("_FloorDetailFrequency", currentLayer.floorDetailFrequency);
                densityShader.SetFloat("_FloorDetailRadius", currentLayer.floorDetailRadius);

                // [B-7] Phase 2 기능별 토글 GPU 전달 (DC 경로)
                densityShader.SetInt("_EnablePhase2FloorClamp", enablePhase2FloorClamp ? 1 : 0);
                densityShader.SetInt("_EnablePhase2CeilClamp", enablePhase2CeilClamp ? 1 : 0);
                densityShader.SetInt("_EnablePhase2FloorBump", enablePhase2FloorBump ? 1 : 0);
                densityShader.SetInt("_EnablePhase2Erosion", enablePhase2Erosion ? 1 : 0);
                densityShader.SetInt("_EnablePhase2Sinkhole", enablePhase2Sinkhole ? 1 : 0);
                densityShader.SetInt("_EnablePhase2Ore", enablePhase2Ore ? 1 : 0);

                // [A.4/.8/.9] 신규 토글 주입 (같은 scope 재사용)
                densityShader.SetFloat("_WarpYScale", effectiveWarpYScale);
                densityShader.SetFloat("_WarpRecursive", effectiveWarpRecursive);
                densityShader.SetFloat("_EnableStalactite", effectiveStalactite);
                densityShader.SetFloat("_TideLineY", effectiveTideLineY);  // Phase 4-1 사전 조치
                densityShader.SetFloat("_EnablePrimitiveRouting", effectivePrimRouting);  // E-α.4
                densityShader.SetInt("_TunnelPrimitive", effectiveTunnelPrim);
                densityShader.SetInt("_RoomPrimitive", effectiveRoomPrim);
                densityShader.SetFloat("_EnableCliffJoint", effectiveCliffJoint);  // E-α.8
                densityShader.SetFloat("_EnableSinkhole", 1f);

                // ═══════════════════════════════════════════════════════════════
                // [FORMAT_VERSION 11] 신규 토글 + DepthLayer 버퍼 (DC 경로)
                //   두 dispatch 경로 모두 동일 토글 주입 — 일관성 보장
                // ═══════════════════════════════════════════════════════════════
                densityShader.SetInt("_EnableErosion3DSignedNarrow", enableErosion3DSignedNarrow ? 1 : 0);
                densityShader.SetInt("_EnablePerVoxelLayerBlend", enablePerVoxelLayerBlend ? 1 : 0);
                densityShader.SetFloat("_LayerBlendWidth", Mathf.Max(0.01f, layerBlendWidth));
                BindDepthLayerBuffer(kernelGenerateDensity);

                densityShader.Dispatch(kernelGenerateDensity, dcTG, dcTG, dcTG);

                // 침식도 DC에 적용
                densityShader.SetBuffer(kernelSimulateErosion, "_VoxelBuffer", voxelBuffer);
                densityShader.Dispatch(kernelSimulateErosion, dcTG, dcTG, dcTG);

                // [v3] voxelBuffer density readback → NormalBakerV3 서브복셀 베이킹
                Vector3 bakedBasePos = new Vector3(context.ChunkPos.x, context.ChunkPos.y, context.ChunkPos.z)
                                       * (chunkSize * voxelSize) - Vector3.one * voxelSize;
                int totalVoxels = dcPointsPerAxis * dcPointsPerAxis * dcPointsPerAxis;
                UnityEngine.Rendering.AsyncGPUReadback.Request(voxelBuffer, (densReq) =>
                {
                    // density 배열 추출 (or null if error)
                    float[] densityData = null;
                    if (!densReq.hasError)
                    {
                        var rawVoxels = densReq.GetData<CaveVoxel>();
                        densityData = new float[rawVoxels.Length];
                        for (int di = 0; di < rawVoxels.Length; di++)
                            densityData[di] = rawVoxels[di].density;
                    }

                    // [FIX-G] DC 3커널 디스패치 (density readback 완료 후)
                    // [Phase 3-B 보강] isCoarse 전달 — Coarse 청크는 Phase 3-B skip (scale 불일치 방지)
                    dcExtension.DispatchDC(context.ChunkPos, dcPointsPerAxis, chunkSize, voxelSize, voxelBuffer, context.IsCoarse);

                    dcExtension.ReadbackAsync((dcVerts, dcQuads, quadCount) =>
                    {
                        // [Phase A - Diag] Readback 진입 로그 — 2차 dispatch 오염 추적용
                        if (enableRaceFixDiagLogs)
                            Debug.Log($"[Readback] {context.ChunkPos} quadCount={quadCount} verts={(dcVerts != null ? dcVerts.Length : 0)}");

                        // density + featureType 전달
                        context.DensityCache = densityData;
                        context.DensityDcN = dcPointsPerAxis;
                        context.DensityDcBasePos = bakedBasePos;
                        context.DensityVoxelSize = voxelSize;

                        // ═══════════════════════════════════════════════════════════
                        // [F-4 / FORMAT_VERSION 13] Ghost Density Buffer 등록
                        //   NormalBaker가 인접 chunk density를 조회할 수 있도록 
                        //   ChunkGhostDataManager에 strong reference 저장.
                        //   토글 OFF 시에도 Register는 수행 — NormalBaker에서 실제
                        //   neighbor 조회 여부가 결정되므로 안전 (LRU가 메모리 관리).
                        //   단, config.enableGhostDensityBaking=false 시 메모리 절약 위해 skip.
                        //
                        //   규칙 #23: 수명 관리는 CaveChunkManager.ReturnToPool에서
                        //   ChunkGhostDataManager.UnregisterChunk 호출로 자동 처리.
                        //   UnregisterChunk 내부가 _densityCache도 함께 제거 (확장됨).
                        // ═══════════════════════════════════════════════════════════
                        // [Approach B] Coarse chunks는 Ghost Cache density 등록 skip.
                        //   Coarse voxelSize (0.25m)와 Fine voxelSize (0.15m)가 달라 
                        //   neighbor 조회 시 mismatch → Halo Bake Phase 2 수치 오류 유발.
                        //   Coarse는 곧 Fine으로 교체되므로 임시 density는 불필요.
                        if (!context.IsCoarse &&
                            densityData != null &&
                            ChunkGhostDataManager.Instance != null &&
                            CaveManager.Instance != null &&
                            CaveManager.Instance.chunkManager != null &&
                            CaveManager.Instance.chunkManager.terrainConfig != null &&
                            CaveManager.Instance.chunkManager.terrainConfig.enableGhostDensityBaking)
                        {
                            ChunkGhostDataManager.Instance.RegisterDensity(
                                context.ChunkPos,
                                densityData,
                                bakedBasePos,
                                dcPointsPerAxis,
                                voxelSize);
                        }

                        // ═══════════════════════════════════════════════════════════════════
                        // [Approach B] Vertex Position Mirror — IsCoarse 가드
                        //   Coarse chunks는 임시 프리뷰이므로 Ghost Cache / Mirror / Halo Bake 모두 skip.
                        //   → Ghost Cache 오염 차단 (voxelSize mismatch 방지)
                        //   → Mirror outOfSnap 비정상 % 방지
                        //   → Halo Bake의 neighbor density 조회 일관성 확보
                        //
                        //   이전 (임시 봉합):
                        //     bool isLod0 = (voxelSize <= baseVS * 1.3f);  ← 휴리스틱
                        //   지금 (명시 플래그):
                        //     if (!context.IsCoarse)  ← 의도 명확
                        //
                        //   토글: CaveTerrainConfig.enableVertexPositionMirror
                        //   규칙 #6: OFF 시 skip (bit-identical)
                        // ═══════════════════════════════════════════════════════════════════
                        if (!context.IsCoarse &&
                            dcVerts != null && dcVerts.Length > 0 &&
                            ChunkGhostDataManager.Instance != null &&
                            CaveManager.Instance != null &&
                            CaveManager.Instance.chunkManager != null &&
                            CaveManager.Instance.chunkManager.terrainConfig != null &&
                            CaveManager.Instance.chunkManager.terrainConfig.enableVertexPositionMirror)
                        {
                            int cs = CaveManager.Instance.chunkManager.terrainConfig.ChunkSize;

                            // Phase 3: Mirror — neighbor로부터 덮어쓰기 (Save보다 먼저)
                            //   snap distance: Inspector의 vertexMirrorSnapMultiplier (기본 1.5)
                            //   outOfSnap % 로그 결과에 따라 튜닝 가능
                            float snapMult = CaveManager.Instance.chunkManager.terrainConfig.vertexMirrorSnapMultiplier;
                            ChunkGhostDataManager.Instance.MirrorFromNeighbors(
                                context.ChunkPos, dcVerts, bakedBasePos, voxelSize, cs, snapMult);

                            // Phase 2: Save — (잠재적으로 덮어써진) boundary vertex를 Ghost Cache에 저장
                            //   다음 chunk가 neighbor로 조회 시 master 역할
                            ChunkGhostDataManager.Instance.SaveBoundaryVertices(
                                context.ChunkPos, dcVerts, bakedBasePos, voxelSize, cs);
                        }

                        // [Phase 2] featureType 추출 (null/빈 배열 안전 처리)
                        if (dcVerts != null && dcVerts.Length > 0)
                        {
                            var ftArr = new int[dcVerts.Length];
                            for (int fi = 0; fi < dcVerts.Length; fi++)
                                ftArr[fi] = dcVerts[fi].featureType;
                            context.FeatureTypes = ftArr;
                        }
                        else
                        {
                            context.FeatureTypes = null;
                        }

                        var meshBuilder = GetComponent<DCMeshBuilder>();
                        // [진단 개선] meshBuilder 미부착과 실제 빈 청크를 분리 로깅
                        if (meshBuilder == null)
                        {
                            Debug.LogError("[DC] DCMeshBuilder 컴포넌트 없음! CaveComputeDispatcher와 동일 GameObject에 부착 필요.");
                            context.State = ChunkState.Completed;
                            onGpuCompleted?.Invoke(context, null, null);
                            _inFlightChunks.Remove(context.ChunkPos); // [Phase A - Dedup]
                            IsBusy = false;
                            RecordCompletion(context.ChunkPos); // [Phase C] cooldown — 에러도 flood 방지
                            TryDispatchPending(context.ChunkPos); // [Phase B] pending 재시도
                            return;
                        }

                        if (dcVerts != null && quadCount > 0)
                        {
                            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                            sw.Start();
                            meshBuilder.BuildMeshFromDCData(
                                dcVerts, dcQuads, quadCount,
                                context, chunkSize, voxelSize,
                                (completedCtx) =>
                                {
                                    sw.Stop();

                                    // // [v3] NormalBaker는 DCMeshBuilder에서 density와 함께 호출됨
                                    var profiler = GetComponent<DCPerformanceProfiler>();
                                    if (profiler != null)
                                    {
                                        profiler.RecordChunkResult(new DCProfileResult
                                        {
                                            chunkPos = context.ChunkPos,
                                            vertexCount = dcVerts.Length,
                                            triangleCount = quadCount * 2,
                                            quadCount = quadCount,
                                            totalTimeMs = (float)sw.Elapsed.TotalMilliseconds,
                                            gpuBufferBytes = DCPerformanceProfiler.CalculateGPUBufferMemory(dcPointsPerAxis)
                                        });
                                    }

                                    Debug.Log($"[DC] 청크 완성: {context.ChunkPos}, {quadCount} quads, {sw.ElapsedMilliseconds}ms");

                                    // [B-10] KPI 수집 훅
                                    RecordKpiEvent(dcVerts.Length, quadCount, (float)sw.Elapsed.TotalMilliseconds);

                                    // [B-6] paramHash 기록 — 이후 dirty 감지 기준. 
                                    //       _pendingCacheKey가 설정된 경우 그 안에 paramHash 포함 (GetCacheKey 구조).
                                    //       cacheKey 파싱 대신 _chunkLastRequests에 요청 보관 + hash는 CaveManager에서 전달 필요.
                                    //       → 현재는 요청 보관만 수행, dirty 감지는 외부 API (DetectDirtyChunks)로 처리.
                                    if (enableAutoRegeneration)
                                    {
                                        _chunkLastRequests[context.ChunkPos] = new PendingRequest
                                        {
                                            context = context, chunkSize = chunkSize,
                                            voxelSize = voxelSize, onGpuCompleted = onGpuCompleted
                                        };
                                        // paramHash는 cacheKey로부터 복원 (fallback: "unknown")
                                        string ph = (_pendingCacheKey != null) ? _pendingCacheKey : "unknown";
                                        _chunkParamHashes[context.ChunkPos] = ph;
                                    }

                                    // [N-2] 캐시 MISS 후 완료 → 디스크에 저장
                                    if (_pendingCacheKey != null && _pendingDiskCache != null)
                                    {
                                        var meshForCache = completedCtx.ChunkObject?.GetComponent<MeshFilter>()?.sharedMesh;
                                        _pendingDiskCache.SaveAsync(_pendingCacheKey, meshForCache, completedCtx.FeatureTypes);
                                        _pendingCacheKey = null;
                                        _pendingDiskCache = null;
                                    }

                                    // [FIX-I] onGpuCompleted 호출 → completedChunks 증가
                                    onGpuCompleted?.Invoke(completedCtx, null, null);
                                    // [Phase A - Dedup] InFlight 제거 (IsBusy 해제 전)
                                    _inFlightChunks.Remove(context.ChunkPos);
                                    // [FIX-J] IsBusy 해제를 BuildMesh 완료 시점으로 이동
                                    IsBusy = false;
                                    RecordCompletion(context.ChunkPos); // [Phase C] cooldown 시작
                                    TryDispatchPending(context.ChunkPos); // [Phase B] pending 재시도
                                }
                            );
                        }
                        else
                        {
                            Debug.Log($"[DC] 빈 청크 (표면 없음): {context.ChunkPos}, quads={quadCount}");
                            // [B-10] KPI: empty chunk 카운트 (0ms 기록)
                            RecordKpiEvent(dcVerts != null ? dcVerts.Length : 0, quadCount, 0f);
                            context.State = ChunkState.Completed;
                            // [FIX-I] 빈 청크도 onGpuCompleted 호출
                            onGpuCompleted?.Invoke(context, null, null);
                            _inFlightChunks.Remove(context.ChunkPos); // [Phase A - Dedup]
                            IsBusy = false;
                            RecordCompletion(context.ChunkPos); // [Phase C] cooldown 시작 (빈 청크도 재요청 방지)
                            TryDispatchPending(context.ChunkPos); // [Phase B] pending 재시도
                        }
                        // IsBusy=false 제거 ← FIX-J (BuildMesh 완료 전 해제 방지)
                    }); // end dcExtension.ReadbackAsync
                }); // end density AsyncGPUReadback.Request
                return;
            }
            // ═══ DC 분기 끝. 아래는 기존 MC 코드가 그대로 유지됨 ═══


            // ----------------------------------------------------
            // 커널 2: 마칭 큐브 메쉬 추출 (Marching Cubes)
            // ----------------------------------------------------
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_VoxelBuffer", voxelBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_TriangleBuffer", triangleBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_OreBuffer", oreBuffer);

            // [🔥 추가: 룩업 테이블 버퍼 셰이더 주입]
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_EdgeTable", mcEdgeTableBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_TriangleTable", mcTriangleTableBuffer);

            marchingCubesShader.SetVector("_ChunkBasePosition", chunkBasePos);
            marchingCubesShader.SetInt("_ChunkSize", chunkSize);
            marchingCubesShader.SetInt("_PointsPerAxis", pointsPerAxis);
            marchingCubesShader.SetFloat("_VoxelSize", voxelSize);
            marchingCubesShader.SetFloat("_IsoLevel", 0.0f);

            triangleBuffer.SetCounterValue(0);
            oreBuffer.SetCounterValue(0);

            // 마칭 큐브 스레드는 삼각형을 만드는 기준이므로 ChunkSize 기준으로 할당
            int mcThreadGroups = Mathf.CeilToInt(chunkSize / 8.0f);
            marchingCubesShader.Dispatch(kernelGenerateMesh, mcThreadGroups, mcThreadGroups, mcThreadGroups);

            onGpuCompleted?.Invoke(context, triangleBuffer, oreBuffer);
        }

        public void EnqueueChunk(ChunkRequestContext context)
        {
            int size = 16;
            float vSize = 1.0f;
            DispatchChunk(context, size, vSize, null);
        }

        private void AllocateTempBuffers(int pointsPerAxis, int chunkSize)
        {
            AllocateTempBuffers(pointsPerAxis, chunkSize, isDCMode: false);
        }

        // [triangleBuffer 최적화] DC 모드에서는 voxelBuffer만 할당
        // isDCMode=true → triangleBuffer/oreBuffer 스킵 (청크당 14.1MB 절약)
        private void AllocateTempBuffers(int pointsPerAxis, int chunkSize, bool isDCMode)
        {
            int requiredVoxelCount = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxCubeCount = chunkSize * chunkSize * chunkSize;

            if (voxelBuffer == null || currentPointsPerAxis != pointsPerAxis)
            {
                ReleaseTempBuffers();
                currentPointsPerAxis = pointsPerAxis;

                voxelBuffer = new ComputeBuffer(requiredVoxelCount, Marshal.SizeOf(typeof(CaveVoxel)));

                if (!isDCMode)
                {
                    // MC 전용 버퍼: DC 모드에서는 생략 (14.1MB 절약)
                    triangleBuffer = new ComputeBuffer(maxCubeCount * 5, Marshal.SizeOf(typeof(CaveTriangle)), ComputeBufferType.Append);
                    oreBuffer = new ComputeBuffer(maxCubeCount, Marshal.SizeOf(typeof(CaveOreData)), ComputeBufferType.Append);
                    triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                    oreCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                }
            }
        }

        private void ReleaseGraphBuffers()
        {
            if (nodeBuffer != null) { nodeBuffer.Release(); nodeBuffer = null; }
            if (edgeBuffer != null) { edgeBuffer.Release(); edgeBuffer = null; }
        }

        private void ReleaseTempBuffers()
        {
            if (voxelBuffer != null) { voxelBuffer.Release(); voxelBuffer = null; }
            if (triangleBuffer != null) { triangleBuffer.Release(); triangleBuffer = null; }
            if (oreBuffer != null) { oreBuffer.Release(); oreBuffer = null; }
            if (triCountBuffer != null) { triCountBuffer.Release(); triCountBuffer = null; }
            if (oreCountBuffer != null) { oreCountBuffer.Release(); oreCountBuffer = null; }
            if (filteredNodeBuffer != null) { filteredNodeBuffer.Release(); filteredNodeBuffer = null; }
            if (filteredEdgeBuffer != null) { filteredEdgeBuffer.Release(); filteredEdgeBuffer = null; }
            // [FORMAT_VERSION 11] DepthLayer GPU 버퍼 해제
            if (_depthLayerBuffer != null) { _depthLayerBuffer.Release(); _depthLayerBuffer = null; _depthLayerBufferCount = 0; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // [FORMAT_VERSION 11] DepthLayer GPU 버퍼 업로드 & 바인딩
        //   호출 규칙: density dispatch 직전에 각 경로에서 한 번 호출.
        //   OFF (enablePerVoxelLayerBlend=false) 시에도 shader의 StructuredBuffer는
        //   바인딩되어야 컴파일/런타임 오류 없음 → 최소 1개 dummy layer 업로드.
        //   Shader는 _EnablePerVoxelLayerBlend==0 시 이 버퍼를 무시하고 기존 uniform 사용.
        //
        //   규칙 #10 (paramHash): enablePerVoxelLayerBlend + layerBlendWidth +
        //                         depthLayers 배열 내용은 이미 paramHash에 반영됨.
        //                         Layer 변경 시 B-6 Auto Regen이 기존 mesh를 재생성.
        // ═══════════════════════════════════════════════════════════════════════
        private void BindDepthLayerBuffer(int kernel)
        {
            if (caveSettings == null)
            {
                // 완전 fallback — 빈 버퍼 방지용 single dummy layer
                EnsureDepthLayerBuffer(1);
                densityShader.SetBuffer(kernel, "_DepthLayerBuffer", _depthLayerBuffer);
                densityShader.SetInt("_DepthLayerCount", 0);
                return;
            }

            var gpuArr = caveSettings.BuildDepthLayerGPUArray();
            int needed = Mathf.Max(1, gpuArr.Length);
            EnsureDepthLayerBuffer(needed);
            _depthLayerBuffer.SetData(gpuArr);

            densityShader.SetBuffer(kernel, "_DepthLayerBuffer", _depthLayerBuffer);
            // 실제 layer count — OFF 시 shader가 버퍼 내용을 무시하므로 값 무관하지만
            // 진단 목적으로 실제 값 전송
            densityShader.SetInt("_DepthLayerCount", gpuArr.Length);
        }

        private void EnsureDepthLayerBuffer(int needed)
        {
            int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DepthLayerGPUData));
            if (_depthLayerBuffer == null || _depthLayerBufferCount < needed)
            {
                if (_depthLayerBuffer != null) { _depthLayerBuffer.Release(); _depthLayerBuffer = null; }
                _depthLayerBuffer = new ComputeBuffer(needed, stride);
                _depthLayerBufferCount = needed;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // [FORMAT_VERSION 11] depthLayers 배열 전체의 해시 (paramHash 일부로 사용)
        //   enablePerVoxelLayerBlend ON 시 인접 layer의 내용이 현재 chunk SDF에 영향
        //   → ComputeParamHash의 cacheLayer(단일 현재 layer)만으론 neighbor 변경 감지 불가
        //   → 모든 layer의 GPU-relevant 필드를 순서대로 누적 XOR 해시
        //   OFF 시에도 이 값은 paramHash에 반영되지만 enablePerVoxelLayerBlend=false가
        //   해시 바이트에 포함되어 있으므로 OFF/ON 경로 캐시가 서로 분리됨 (정상).
        // ═══════════════════════════════════════════════════════════════════════
        private int ComputeAllLayerHash()
        {
            if (caveSettings == null || caveSettings.depthLayers == null || caveSettings.depthLayers.Count == 0)
                return 0;

            unchecked
            {
                int h = (int)2166136261; // FNV-1a 32-bit offset basis
                foreach (var layer in caveSettings.depthLayers)
                {
                    h = (h * 16777619) ^ layer.minAltitude.GetHashCode();
                    h = (h * 16777619) ^ layer.maxAltitude.GetHashCode();
                    h = (h * 16777619) ^ layer.floorBlendRadius.GetHashCode();
                    h = (h * 16777619) ^ layer.ceilBlendRadius.GetHashCode();
                    h = (h * 16777619) ^ layer.floorBumpAmplitude.GetHashCode();
                    h = (h * 16777619) ^ layer.floorBumpFrequency.GetHashCode();
                    h = (h * 16777619) ^ layer.sdfSmoothness.GetHashCode();
                    h = (h * 16777619) ^ layer.sinkholeProbability.GetHashCode();
                    h = (h * 16777619) ^ layer.sinkholeMinRadius.GetHashCode();
                    h = (h * 16777619) ^ layer.sinkholeMaxRadius.GetHashCode();
                    h = (h * 16777619) ^ layer.sinkholeSmoothness.GetHashCode();
                    h = (h * 16777619) ^ layer.ledgeStepHeight.GetHashCode();
                    h = (h * 16777619) ^ layer.spiralFrequency.GetHashCode();
                    h = (h * 16777619) ^ layer.spiralAmplitude.GetHashCode();
                    h = (h * 16777619) ^ layer.tunnelWidthScale.GetHashCode();
                    h = (h * 16777619) ^ layer.roomSizeScale.GetHashCode();
                    h = (h * 16777619) ^ layer.sedimentAmplitude.GetHashCode();
                    h = (h * 16777619) ^ layer.floorDetailAmplitude.GetHashCode();
                    h = (h * 16777619) ^ layer.floorDetailFrequency.GetHashCode();
                    h = (h * 16777619) ^ layer.floorDetailRadius.GetHashCode();
                }
                return h;
            }
        }

        // ═══ [Rank 2] CPU 사전 필터링: 청크별 노드/엣지 AABB 교차 검사 ═══
        // 청크 AABB와 노드/엣지 영향 범위가 겹치는 것만 GPU에 전달
        // 효과: GPU 순회 ~70~90% 감소 (노드 50→5~15개, 엣지 80→10~25개)
        public void PreFilterForChunk(Vector3 chunkWorldMin, float chunkWorldSize, float margin)
        {
            if (CaveNodeGraphBuilder.Instance == null) return;
            var allNodes = CaveNodeGraphBuilder.Instance.nodesData;
            var allEdges = CaveNodeGraphBuilder.Instance.edgesData;
            Vector3 cMin = chunkWorldMin - Vector3.one * margin;
            Vector3 cMax = chunkWorldMin + Vector3.one * (chunkWorldSize + margin);

            var fNodes = new System.Collections.Generic.List<NodeData>();
            var fEdges = new System.Collections.Generic.List<EdgeData>();

            for (int i = 0; i < allNodes.Count; i++)
            {
                var n = allNodes[i];
                float r = n.radius + margin;
                if (n.position.x + r < cMin.x || n.position.x - r > cMax.x) continue;
                if (n.position.y + r < cMin.y || n.position.y - r > cMax.y) continue;
                if (n.position.z + r < cMin.z || n.position.z - r > cMax.z) continue;
                fNodes.Add(n);
            }
            for (int i = 0; i < allEdges.Count; i++)
            {
                var e = allEdges[i];
                Vector3 mid = (e.startPos + e.endPos) * 0.5f;
                float halfLen = Vector3.Distance(e.startPos, e.endPos) * 0.5f;
                float r = halfLen + e.width + margin;
                if (mid.x + r < cMin.x || mid.x - r > cMax.x) continue;
                if (mid.y + r < cMin.y || mid.y - r > cMax.y) continue;
                if (mid.z + r < cMin.z || mid.z - r > cMax.z) continue;
                fEdges.Add(e);
            }

            filteredNodeCount = fNodes.Count;
            filteredEdgeCount = fEdges.Count;

            // 최소 1개 보장 (빈 버퍼 방지)
            if (filteredNodeCount == 0) { fNodes.Add(new NodeData()); filteredNodeCount = 0; }
            if (filteredEdgeCount == 0) { fEdges.Add(new EdgeData()); filteredEdgeCount = 0; }

            if (filteredNodeBuffer != null) filteredNodeBuffer.Release();
            if (filteredEdgeBuffer != null) filteredEdgeBuffer.Release();
            filteredNodeBuffer = new ComputeBuffer(fNodes.Count, System.Runtime.InteropServices.Marshal.SizeOf<NodeData>());
            filteredEdgeBuffer = new ComputeBuffer(fEdges.Count, System.Runtime.InteropServices.Marshal.SizeOf<EdgeData>());
            filteredNodeBuffer.SetData(fNodes);
            filteredEdgeBuffer.SetData(fEdges);

            // [Phase A - Diag] PreFilter 결과 로그 — 2차 dispatch 시 비정상 edge 수 확인용
            //   정상 범위: nodes 3~10, edges 10~25 (Case 1/2 재설계 기준)
            //   0 = 빈 청크 유발, 너무 큰 값 = 다른 chunk buffer 오염
            if (enableRaceFixDiagLogs)
                Debug.Log($"[PreFilter] chunkMin=({chunkWorldMin.x:F1},{chunkWorldMin.y:F1},{chunkWorldMin.z:F1}) nodes={filteredNodeCount} edges={filteredEdgeCount}");
        }

        // 사전 필터된 버퍼를 density shader에 바인딩
        public void BindFilteredBuffers(int kernel)
        {
            densityShader.SetBuffer(kernel, "_NodeBuffer", filteredNodeBuffer);
            densityShader.SetInt("_NodeCount", filteredNodeCount);
            densityShader.SetBuffer(kernel, "_EdgeBuffer", filteredEdgeBuffer);
            densityShader.SetInt("_EdgeCount", filteredEdgeCount);
        }

        // =====================================================================
        // [N-2] DiskCache — pending cache save (completion callback에서 참조)
        // =====================================================================
        private string _pendingCacheKey = null;
        private CaveDiskCache _pendingDiskCache = null;

        private void OnDestroy()
        {
            ReleaseGraphBuffers();
            ReleaseTempBuffers();
            if (biomeBuffer != null) { biomeBuffer.Release(); biomeBuffer = null; }

            // [Phase 12-E + Phase 13] M5/G2 buffer release
            if (biomeMeanOffsetBuffer != null) { biomeMeanOffsetBuffer.Release(); biomeMeanOffsetBuffer = null; }
            if (biomeAmpBuffer != null) { biomeAmpBuffer.Release(); biomeAmpBuffer = null; }

            // [🔥 추가: 룩업 테이블 버퍼 해제]
            if (mcEdgeTableBuffer != null) { mcEdgeTableBuffer.Release(); mcEdgeTableBuffer = null; }
            if (mcTriangleTableBuffer != null) { mcTriangleTableBuffer.Release(); mcTriangleTableBuffer = null; }
        }
    }
}