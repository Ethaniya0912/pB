// =============================================================================
// CaveDiskCache.cs  |  pB-4 Project — N-2 (P4 DiskCache)
// =============================================================================
// 재방문 시 GPU Compute 재실행 없이 디스크에서 mesh 로드 (~3ms vs 26ms)
//
// 규칙 준수:
//   #6  enableDiskCache=OFF → 캐시 미사용 (원본 동작)
//   #10 paramHash에 모든 SDF 토글 + DepthLayer + FloorSmoothing 파라미터 포함
//   #12 Dispose 불필요 (managed 배열 사용)
//
// 파편 방지:
//   paramHash 불일치 → 자동 캐시 MISS → 재생성
//   formatVersion 변경 시 자동 무효
// =============================================================================

using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CaveSystem
{
    public class CaveDiskCache : MonoBehaviour
    {
        [Header("N-2 — DiskCache")]
        [Tooltip("ON: 재방문 시 디스크 캐시 사용 (26ms→3ms). OFF: 항상 GPU 재생성 (원본)")]
        public bool enableDiskCache = false;

        [Tooltip("최대 캐시 크기 (MB). 초과 시 LRU 삭제")]
        public int maxCacheSizeMB = 2048;

        // [Phase 3-B] FORMAT_VERSION 2→3 승격
        //   이전: 1 (원본)
        //   이전: 3 (Phase 3-B DC Overlap Removal 반영)
        //   이전: 4 (CaveBiomeMath.hlsl Case 1/4/5 terrace jitter 적용)
        //   이전: 5 (Case 0 fBm octave 4→3, amp 2.0→1.2 — Nyquist 한계 준수)
        //   이전: 6 (Case 0 Karst Cave 재설계 — 4-layer 구조, 4원칙 적용)
        //   이전: 7 (Case 1 Columnar Jointing 재설계 — 주상절리 수직 기둥 구조)
        //          Case 2 Faulted Sedimentary 재설계 — 수평 bedding + 경사 단층
        //   이전: 8 (Case 1 verticalCrack 튜닝 — chunk empty 방지)
        //   이전: 9 (B-6~B-10 통합: auto regen + Phase 2 토글 + Stitcher 튜닝 + KPI)
        //   이전: 10 (Phase C: Completion Cooldown + DCMeshBuilder collider skip 수정)
        //           MeshCollider null 버그 해결: 1차 완료 후 cache HIT 경로 중복 dispatch 차단.
        //   이전: 11 (Erosion 3D Bias-Only + Per-Voxel DepthLayer Blend — 2+4 병행 patch)
        //           Bias-Only 모드는 단방향 주입으로 detail 손실 확인 → 이번 버전에서 은퇴.
        //           Per-Voxel Layer Blend는 성공 평가 유지.
        //   현재: 12 (Erosion 3D Bias-Only 은퇴 + Signed Narrow-mask 도입)
        //           Bias-Only(-|n|)를 Signed Narrow-mask(+n × narrow saturate)로 대체.
        //             양방향 detail 유지 + 3D noise Y-decorrelation + narrow mask로 영향범위
        //             제한 → 파편 이론 v28의 P(좁은 통로) factor 억제 경로로 AND 붕괴.
        //           Per-Voxel Layer Blend는 FV 11 구조 그대로 유지.
        //           shader 수식 변경 → 규칙 #15에 의해 FORMAT_VERSION 승격 필수.
        //   규칙 #15: mesh 포맷 또는 shader 수식 변경 시 자동 캐시 무효화 보장.
        //   규칙 #25: SDF 최고 frequency ≤ 1/(voxelSize×2.5) — 모든 layer 준수.
        //   규칙 #28: biome 설계 우선순위 — analytical 우선, noise는 표면 detail만.
        //   토글 OFF 상태에서도 caching 경로의 전역 공유를 위해 버전 승격.
        //   (토글 ON/OFF 차이는 paramHash에서 구분됨)
        //   [주의] shader 수식 변경은 paramHash 미반영 → FORMAT_VERSION 승격만이 유효
        //
        //   [v13] F-4 Ghost Density Buffer (Halo Exchange 패턴):
        //         ChunkGhostDataManager에 density cache 등록 + SampleDensityGhostAware.
        //         NormalBaker와 DCMeshBuilder의 PerVertexSubVoxelJob에서 인접 chunk density 조회.
        //         Normal texture 및 Per-Vertex subNormal UV2 결과가 변경됨 → 캐시 무효화 필요.
        //         (Mesh position/topology는 무변 → paramHash 불필요, 규칙 #10 미적용)
        //         Toggle OFF 시 byte-identical (규칙 #6) — 그러나 cache 정합성 위해 승격.
        //
        //   [v19] Phase 3-B Cross-Chunk QEF 경로 완성 (규칙 #24/#25 적용):
        //         DCPipelineExtension에 ExtractBoundaryHermiteEdges kernel dispatch +
        //         async readback + ChunkGhostData.facesEdges 저장 경로 추가.
        //         이전까지 "작동 안 하던" enablePhase3OverlapRemoval 토글이 실제로 작동.
        //         토글 ON 시 boundary cell QEF가 neighbor ghost edges 포함 → mesh vertex 위치 변경.
        //         토글 OFF 시에도 Extract kernel dispatch 경로 자체는 코드 경로가 달라졌으므로
        //         (규칙 #6은 syntactic bit-identical까진 보장하나 cache 정합성은 승격으로 보장) 승격.
        //         토글 ON/OFF 차이는 paramHash에서 구분.
        private const ushort FORMAT_VERSION = 26;  // [E-α.10] Phase 4 완료: E-α.1 primitive library, E-α.2 라우터, E-α.3 enum, E-α.4 primitive routing, E-α.5 EdgeConstraint SO, E-α.6 Case-aware graph+CardinalBias, E-α.7 RoomGeometryMatrix SO, E-α.8 Cliff Joint, E-α.9 Auditor/Visualizer 확장+Map size 통합. 전체 토글 OFF 시 byte-identical.
        private static readonly byte[] MAGIC = { 0x44, 0x43, 0x43, 0x48 }; // "DCCH"

        private string _cacheDir;
        private Dictionary<string, long> _manifest = new Dictionary<string, long>(); // key → lastAccessTick
        private long _totalCacheBytes = 0;
        private bool _manifestLoaded = false;

        // =====================================================================
        // 초기화
        // =====================================================================

        private void Awake()
        {
            _cacheDir = Path.Combine(Application.persistentDataPath, "cave_cache");
            if (!Directory.Exists(_cacheDir))
                Directory.CreateDirectory(_cacheDir);
            LoadManifest();
        }

        // =====================================================================
        // paramHash 계산 — 규칙 #10 핵심
        //   모든 SDF/DC/FS 파라미터 포함
        //   어떤 필드라도 변경 → 해시 변경 → 캐시 자동 무효
        // =====================================================================

        public string ComputeParamHash(
            int seed, float voxelSize, int chunkSize,
            DepthLayer layer,
            bool enableScaling, bool enableWidthVariation,
            bool enableSediment, bool enableFloorDetail,
            bool enableWarpNorm, float effectiveWarp,
            bool enableReducedSmoothing, bool enableFloorSmoothingJob,
            bool enableCompressedHermite, bool enableCompressedVertex,
            float dcNormalAmplify,
            // [Phase 1/2/3-A/3-B] mesh 결과에 영향하는 추가 토글 — 규칙 #10
            bool enablePhase1ExpandedSnap = false,
            bool enablePhase1BestMatch = false,
            bool enablePhase1RecalculateNormals = false,
            bool enablePhase1UploadMeshData = false,
            float phase1SnapMultiplier = 1.0f,
            bool enableMassPointA = false,
            bool enableMassPointB = false,
            bool enablePhase2MassPointQEF = false,
            float phase2MassPointStrength = 0.0f,
            bool enablePhase3OverlapRemoval = false,
            // [FORMAT_VERSION 11] 신규 토글 — default false/0 로 기존 호출자 하위 호환
            //   기존 호출자는 이 4개를 전달 안 해도 컴파일 OK, 결과 해시는 기존과 다른 값
            //   (FORMAT_VERSION 자체가 10→11 승격되어 모든 기존 캐시 무효 — 규칙 #15)
            bool enableErosion3DSignedNarrow = false,
            bool enablePerVoxelLayerBlend = false,
            float layerBlendWidth = 0.0f,
            int allLayerHash = 0,
            // [Gate 5 Phase E-β.3] Edge curvature amp — 규칙 #10
            //   effectiveCurvAmp = enableCurvedTunnels ? curvatureAmplitude : 0
            //   default 0 → 기존 호출자 하위 호환 (단, FV 22 승급으로 기존 cache 전부 무효)
            float curvatureAmp = 0f,
            // [Gate 5 Phase E-β.9] Floor/Ceil variation — 규칙 #10
            //   둘 다 0 기본 → byte-identical
            //   biomeFloor × 전역 multiplier 곱한 effective 값 전달
            float floorVariationAmp = 0f,
            float ceilVariationAmp = 0f,
            // [Gate 5 Phase A.12] A.4 WarpY/Recursive + A.8 Stalactite — 규칙 #10
            //   전부 0 기본 → byte-identical
            float warpYScale = 0f,
            float warpRecursive = 0f,
            float enableStalactite = 0f,
            // [Gate 5 Phase E-α.10] Primitive routing + Cliff joint — 규칙 #10
            //   전부 0 기본 → byte-identical
            float enablePrimRouting = 0f,
            int tunnelPrim = 0,
            int roomPrim = 0,
            float enableCliffJoint = 0f)
        {
            using (var sha = SHA256.Create())
            {
                using (var ms = new MemoryStream(256))
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(FORMAT_VERSION);
                    bw.Write(seed);
                    bw.Write(voxelSize);
                    bw.Write(chunkSize);

                    // DepthLayer 전 필드 (layerName, fogColor, ore 제외 — 렌더/셰이더 전용)
                    bw.Write(layer.maxAltitude);
                    bw.Write(layer.minAltitude);
                    bw.Write(layer.floorBlendRadius);
                    bw.Write(layer.ceilBlendRadius);
                    bw.Write(layer.floorBumpAmplitude);
                    bw.Write(layer.floorBumpFrequency);
                    bw.Write(layer.noiseFrequency);
                    bw.Write(layer.sdfSmoothness);
                    bw.Write(layer.sinkholeProbability);
                    bw.Write(layer.sinkholeMinRadius);
                    bw.Write(layer.sinkholeMaxRadius);
                    bw.Write(layer.sinkholeSmoothness);
                    bw.Write(layer.ledgeStepHeight);
                    bw.Write(layer.spiralFrequency);
                    bw.Write(layer.spiralAmplitude);
                    bw.Write(layer.tunnelWidthScale);
                    bw.Write(layer.roomSizeScale);
                    bw.Write(layer.sedimentAmplitude);
                    bw.Write(layer.floorDetailAmplitude);
                    bw.Write(layer.floorDetailFrequency);
                    bw.Write(layer.floorDetailRadius);

                    // [Gate 5 Phase A.1] v34 MD §6.1 신규 필드 — paramHash 포함 (규칙 #10)
                    //   기존 asset은 모두 0 → Dispatcher fallback으로 기존 동작 유지
                    //   그러나 paramHash 값은 바뀌므로 기존 cache는 첫 play 시 자동 regen
                    //   새 mesh는 fallback으로 기존과 byte-identical (규칙 #6)
                    bw.Write(layer.canyonCeilingHeight);
                    bw.Write(layer.warpAmplitudeOverride);

                    // [Gate 5 Phase E-β.3] Edge curvature amp (규칙 #10)
                    //   0 = 직선 sdCapsule (기존 동작 byte-identical)
                    //   > 0 = EvaluateCurvedTunnel capsule chain
                    bw.Write(curvatureAmp);

                    // [Gate 5 Phase E-β.9] Floor/Ceil Y variation (규칙 #10)
                    //   둘 다 0 = 평탄 (기존 동작 byte-identical)
                    //   > 0 = shader에서 noise 기반 변조 (hard bound 내부 clamp)
                    bw.Write(floorVariationAmp);
                    bw.Write(ceilVariationAmp);

                    // [Gate 5 Phase A.12] A.4 WarpY/Recursive + A.8 Stalactite (규칙 #10)
                    //   전부 0 = 기존 동작 byte-identical
                    bw.Write(warpYScale);
                    bw.Write(warpRecursive);
                    bw.Write(enableStalactite);

                    // [Gate 5 Phase E-α.10] Primitive routing + Cliff joint (규칙 #10)
                    //   enablePrimRouting=0 OR prim=0 → 기본 경로
                    //   enableCliffJoint=0 → cliff skip
                    bw.Write(enablePrimRouting);
                    bw.Write(tunnelPrim);
                    bw.Write(roomPrim);
                    bw.Write(enableCliffJoint);

                    // SDF 토글
                    bw.Write(enableScaling);
                    bw.Write(enableWidthVariation);
                    bw.Write(enableSediment);
                    bw.Write(enableFloorDetail);
                    bw.Write(enableWarpNorm);
                    bw.Write(effectiveWarp);

                    // FloorSmoothing 토글 (결과에 영향)
                    bw.Write(enableReducedSmoothing);
                    bw.Write(enableFloorSmoothingJob);

                    // O1/O3 토글 (미세 정밀도 차이)
                    bw.Write(enableCompressedHermite);
                    bw.Write(enableCompressedVertex);

                    // NormalBaker amplify (normalmap에 영향)
                    bw.Write(dcNormalAmplify);

                    // [Phase 1 — Stitcher 긴급 패치] 규칙 #10
                    //   mesh vertex 위치가 stitcher에 의해 수정됨 → 캐시된 mesh와 달라짐
                    bw.Write(enablePhase1ExpandedSnap);
                    bw.Write(enablePhase1BestMatch);
                    bw.Write(enablePhase1RecalculateNormals);
                    bw.Write(enablePhase1UploadMeshData);
                    bw.Write(phase1SnapMultiplier);

                    // [Phase 2 — Mass Point QEF] 규칙 #10
                    //   QEF solver 경로 변경 → vertex 위치 변경
                    bw.Write(enableMassPointA);
                    bw.Write(enableMassPointB);
                    bw.Write(enablePhase2MassPointQEF);
                    bw.Write(phase2MassPointStrength);

                    // [Phase 3-B — DC Overlap Removal] 규칙 #10
                    //   DCPointsPerAxis + bakedBasePos 변경 → mesh 구조 자체 변경
                    //   Phase 3-A는 collider 타이밍만 영향 → mesh 내용 무변경 → paramHash 제외
                    bw.Write(enablePhase3OverlapRemoval);

                    // ═══════════════════════════════════════════════════════════════════
                    // [FORMAT_VERSION 12] 신규 토글 — SDF 결과에 직접 영향 → 규칙 #10 필수
                    //   enableErosion3DSignedNarrow: Erosion 수식 변경 (2-oct XZ signed → 3-oct 3D signed narrow)
                    //   enablePerVoxelLayerBlend: FloorClamp/CeilClamp/FloorDetail 경로 변경
                    //   layerBlendWidth: blend 전이 구간 폭
                    //   allLayerHash: depthLayers 배열 자체 변경 시 (neighbor blend에 영향)
                    //                 chunk당 단일 cacheLayer만 기록되면 neighbor layer 변경
                    //                 감지 실패 → 별도 해시 추가
                    //
                    //   OFF 조합 (enableErosion3DSignedNarrow=false, enablePerVoxelLayerBlend=false)
                    //   시 아래 4개 bw.Write는 실행되지만 기본값이 모두 false/0 이므로
                    //   paramHash 전체 바이트 시퀀스는 변경되고(→ 새 캐시 파일) 값은 고정되어
                    //   동일 OFF 설정에서는 동일 해시가 반복 생성됨 (cache 재활용 정상).
                    // ═══════════════════════════════════════════════════════════════════
                    bw.Write(enableErosion3DSignedNarrow);
                    bw.Write(enablePerVoxelLayerBlend);
                    bw.Write(layerBlendWidth);
                    bw.Write(allLayerHash);

                    bw.Flush();
                    byte[] hash = sha.ComputeHash(ms.ToArray());
                    // 앞 8바이트 = 16자 hex
                    return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLower();
                }
            }
        }

        public string GetCacheKey(Vector3Int chunkPos, string paramHash)
        {
            return $"{chunkPos.x}_{chunkPos.y}_{chunkPos.z}_{paramHash}";
        }

        public string GetCachePath(string cacheKey)
        {
            return Path.Combine(_cacheDir, cacheKey + ".dcmesh");
        }

        // =====================================================================
        // 캐시 조회 (동기 — 파일 존재 여부만)
        // =====================================================================

        public bool HasCache(string cacheKey)
        {
            if (!enableDiskCache) return false;
            return File.Exists(GetCachePath(cacheKey));
        }

        // =====================================================================
        // 캐시 저장 — mesh 데이터를 binary로 직렬화
        //   NormalBaker는 async 재베이크 (~2ms, 캐시 불포함)
        //   SeamStitcher 이전 상태 저장 (HIT 시에도 SeamStitcher 실행)
        // =====================================================================

        public void SaveAsync(string cacheKey, Mesh mesh, int[] featureTypes)
        {
            if (!enableDiskCache || mesh == null) return;

            // managed 배열 추출 (메인스레드에서)
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] indices = mesh.triangles;
            Bounds bounds = mesh.bounds;
            string indexFmt = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? "u32" : "u16";

            string path = GetCachePath(cacheKey);

            // 비동기 파일 쓰기
            Task.Run(() =>
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                    using (var bw = new BinaryWriter(fs))
                    {
                        // Header
                        bw.Write(MAGIC);
                        bw.Write(FORMAT_VERSION);

                        // Mesh metadata
                        int vertCount = verts.Length;
                        int idxCount = indices.Length;
                        bw.Write(vertCount);
                        bw.Write(idxCount);
                        bw.Write(indexFmt == "u32");

                        // Bounds
                        WriteVector3(bw, bounds.center);
                        WriteVector3(bw, bounds.size);

                        // Vertices
                        for (int i = 0; i < vertCount; i++) WriteVector3(bw, verts[i]);
                        // Normals
                        for (int i = 0; i < vertCount; i++) WriteVector3(bw, normals[i]);
                        // UVs
                        for (int i = 0; i < vertCount; i++) WriteVector2(bw, uvs[i]);
                        // Indices
                        for (int i = 0; i < idxCount; i++) bw.Write(indices[i]);

                        // FeatureTypes (optional)
                        bool hasFT = featureTypes != null && featureTypes.Length > 0;
                        bw.Write(hasFT);
                        if (hasFT)
                        {
                            bw.Write(featureTypes.Length);
                            for (int i = 0; i < featureTypes.Length; i++)
                                bw.Write(featureTypes[i]);
                        }
                    }

                    // manifest 갱신
                    long fileSize = new FileInfo(path).Length;
                    lock (_manifest)
                    {
                        _manifest[cacheKey] = DateTime.UtcNow.Ticks;
                        _totalCacheBytes += fileSize;
                    }

                    // LRU 정리 (비동기에서 실행)
                    EnforceCacheLimit();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DiskCache] 저장 실패: {cacheKey} — {e.Message}");
                }
            });
        }

        // =====================================================================
        // 캐시 로드 — binary에서 mesh 데이터 복원
        //   메인스레드에서 호출, Task.Run으로 파일 읽기 후 메인 복귀
        // =====================================================================

        public struct CachedMeshData
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector2[] uvs;
            public int[] indices;
            public Bounds bounds;
            public bool is32bit;
            public int[] featureTypes; // nullable
        }

        /// <summary>
        /// 캐시에서 mesh 데이터 로드. 성공 시 onLoaded(data) 호출, 실패 시 onLoaded(null).
        /// 파일 읽기는 비동기, Mesh 구성은 메인스레드에서.
        /// </summary>
        public async void LoadAsync(string cacheKey, System.Action<CachedMeshData?> onLoaded)
        {
            if (!enableDiskCache)
            {
                onLoaded?.Invoke(null);
                return;
            }

            string path = GetCachePath(cacheKey);
            if (!File.Exists(path))
            {
                onLoaded?.Invoke(null);
                return;
            }

            CachedMeshData? result = null;

            try
            {
                result = await Task.Run(() =>
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                    using (var br = new BinaryReader(fs))
                    {
                        // Header 검증
                        byte[] magic = br.ReadBytes(4);
                        if (magic[0] != MAGIC[0] || magic[1] != MAGIC[1] ||
                            magic[2] != MAGIC[2] || magic[3] != MAGIC[3])
                            return (CachedMeshData?)null;

                        ushort version = br.ReadUInt16();
                        if (version != FORMAT_VERSION)
                            return null; // 포맷 버전 불일치 → 무효

                        var data = new CachedMeshData();

                        int vertCount = br.ReadInt32();
                        int idxCount = br.ReadInt32();
                        data.is32bit = br.ReadBoolean();

                        data.bounds = new Bounds(ReadVector3(br), ReadVector3(br));

                        data.vertices = new Vector3[vertCount];
                        for (int i = 0; i < vertCount; i++) data.vertices[i] = ReadVector3(br);

                        data.normals = new Vector3[vertCount];
                        for (int i = 0; i < vertCount; i++) data.normals[i] = ReadVector3(br);

                        data.uvs = new Vector2[vertCount];
                        for (int i = 0; i < vertCount; i++) data.uvs[i] = ReadVector2(br);

                        data.indices = new int[idxCount];
                        for (int i = 0; i < idxCount; i++) data.indices[i] = br.ReadInt32();

                        bool hasFT = br.ReadBoolean();
                        if (hasFT)
                        {
                            int ftLen = br.ReadInt32();
                            data.featureTypes = new int[ftLen];
                            for (int i = 0; i < ftLen; i++)
                                data.featureTypes[i] = br.ReadInt32();
                        }

                        return (CachedMeshData?)data;
                    }
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DiskCache] 로드 실패: {cacheKey} — {e.Message}");
                result = null;
            }

            // manifest 갱신 (lastAccess)
            if (result != null)
            {
                lock (_manifest)
                {
                    _manifest[cacheKey] = DateTime.UtcNow.Ticks;
                }
            }

            onLoaded?.Invoke(result);
        }

        /// <summary>
        /// 캐시된 데이터를 Unity Mesh로 구성 (메인스레드에서 호출).
        /// </summary>
        public static Mesh BuildMeshFromCache(CachedMeshData data, string chunkName)
        {
            var mesh = new Mesh
            {
                name = chunkName,
                indexFormat = data.is32bit
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = data.vertices;
            mesh.normals = data.normals;
            mesh.uv = data.uvs;
            mesh.triangles = data.indices;
            mesh.bounds = data.bounds;
            mesh.RecalculateTangents();
            return mesh;
        }

        // =====================================================================
        // LRU + Manifest
        // =====================================================================

        private void EnforceCacheLimit()
        {
            long maxBytes = (long)maxCacheSizeMB * 1024 * 1024;
            if (_totalCacheBytes <= maxBytes) return;

            // LRU: lastAccess가 가장 오래된 것부터 삭제
            List<KeyValuePair<string, long>> entries;
            lock (_manifest)
            {
                entries = new List<KeyValuePair<string, long>>(_manifest);
            }
            entries.Sort((a, b) => a.Value.CompareTo(b.Value)); // oldest first

            foreach (var entry in entries)
            {
                if (_totalCacheBytes <= maxBytes) break;
                string path = GetCachePath(entry.Key);
                try
                {
                    if (File.Exists(path))
                    {
                        long size = new FileInfo(path).Length;
                        File.Delete(path);
                        _totalCacheBytes -= size;
                    }
                    lock (_manifest) { _manifest.Remove(entry.Key); }
                }
                catch { /* ignore */ }
            }

            SaveManifest();
        }

        private void LoadManifest()
        {
            string manifestPath = Path.Combine(_cacheDir, "manifest.json");
            _manifest.Clear();
            _totalCacheBytes = 0;

            if (File.Exists(manifestPath))
            {
                try
                {
                    string json = File.ReadAllText(manifestPath);
                    var wrapper = JsonUtility.FromJson<ManifestWrapper>(json);
                    if (wrapper != null && wrapper.entries != null)
                    {
                        foreach (var e in wrapper.entries)
                        {
                            _manifest[e.key] = e.lastAccessTick;
                            string fp = GetCachePath(e.key);
                            if (File.Exists(fp))
                                _totalCacheBytes += new FileInfo(fp).Length;
                        }
                    }
                }
                catch { /* corrupt manifest — start fresh */ }
            }
            _manifestLoaded = true;
        }

        private void SaveManifest()
        {
            string manifestPath = Path.Combine(_cacheDir, "manifest.json");
            var wrapper = new ManifestWrapper();
            lock (_manifest)
            {
                wrapper.entries = new List<ManifestEntry>(_manifest.Count);
                foreach (var kvp in _manifest)
                    wrapper.entries.Add(new ManifestEntry { key = kvp.Key, lastAccessTick = kvp.Value });
            }
            try { File.WriteAllText(manifestPath, JsonUtility.ToJson(wrapper, false)); }
            catch { /* ignore */ }
        }

        private void OnApplicationQuit() { SaveManifest(); }
        private void OnDestroy() { SaveManifest(); }

        /// <summary>에디터용: 전체 캐시 삭제 (파편 방지 최후 안전장치)</summary>
        public void ClearAllCache()
        {
            if (Directory.Exists(_cacheDir))
            {
                try { Directory.Delete(_cacheDir, true); } catch { }
                Directory.CreateDirectory(_cacheDir);
            }
            _manifest.Clear();
            _totalCacheBytes = 0;
            Debug.Log("[DiskCache] 전체 캐시 삭제 완료.");
        }

        // =====================================================================
        // 내부 헬퍼
        // =====================================================================

        private static void WriteVector3(BinaryWriter bw, Vector3 v)
        {
            bw.Write(v.x); bw.Write(v.y); bw.Write(v.z);
        }
        private static void WriteVector2(BinaryWriter bw, Vector2 v)
        {
            bw.Write(v.x); bw.Write(v.y);
        }
        private static Vector3 ReadVector3(BinaryReader br)
        {
            return new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        }
        private static Vector2 ReadVector2(BinaryReader br)
        {
            return new Vector2(br.ReadSingle(), br.ReadSingle());
        }

        [Serializable]
        private class ManifestWrapper
        {
            public List<ManifestEntry> entries;
        }
        [Serializable]
        private class ManifestEntry
        {
            public string key;
            public long lastAccessTick;
        }
    }
}
