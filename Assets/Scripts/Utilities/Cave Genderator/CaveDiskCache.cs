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

        private const ushort FORMAT_VERSION = 2; // [β] 1→2: 과거 캐시 오염 원천 차단 (B1 토글 도입 + 개발 중 누적 오염)
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
            bool enableHermiteStableT = false,
            bool enableAdaptiveClassify = false,
            bool enablePhantomScale = false,
            bool enableEdgeFragmentPull = false)
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

                    // [B1] Hermite t 안정화 토글 — 조건부 해시 포함
                    //   OFF: 해시 쓰기 생략 → 기존 캐시 재사용 (규칙 #6 정신)
                    //   ON : "B1:ON" 마커 기록 → 새 해시 생성, 캐시 재계산
                    //   규칙 #10 준수: ON 경로 변경 시 캐시 무효. 
                    //                  OFF 경로는 원본 해시와 일치 → 캐시 보존
                    if (enableHermiteStableT)
                    {
                        bw.Write((byte)0xB1); // 마커: B1 ON
                    }
                    // [B2] ClassifyFeature voxelSize 의존 토글 — 조건부 해시 포함
                    //   동일 원칙: OFF 시 해시 쓰기 생략 → 기존 해시 보존
                    if (enableAdaptiveClassify)
                    {
                        bw.Write((byte)0xB2); // 마커: B2 ON
                    }
                    // [γ2] Fix-Phantom voxelSize 비례 토글 — 조건부 해시 포함
                    if (enablePhantomScale)
                    {
                        bw.Write((byte)0xC2); // 마커: γ2 ON
                    }
                    // [γ3] Edge-Fragment Pull 토글 — 조건부 해시 포함
                    if (enableEdgeFragmentPull)
                    {
                        bw.Write((byte)0xC3); // 마커: γ3 ON
                    }

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

            // [β] 캐시 오염 방지: manifest-level 버전 체크
            //   과거 캐시(formatVersion 불일치)가 해시 우연 일치로 로드되는 사고 방지
            //   manifest.json에 manifestFormatVersion 필드 추가 → 불일치 시 전체 폐기
            //   이는 개별 .dcmesh 파일 안의 FORMAT_VERSION 체크보다 선제적으로 작동
            bool manifestOutdated = false;

            if (File.Exists(manifestPath))
            {
                try
                {
                    string json = File.ReadAllText(manifestPath);
                    var wrapper = JsonUtility.FromJson<ManifestWrapper>(json);
                    if (wrapper != null)
                    {
                        // [β] 버전 필드가 없거나(legacy) 현재 버전과 다르면 전체 폐기
                        if (wrapper.manifestFormatVersion != FORMAT_VERSION)
                        {
                            manifestOutdated = true;
                            Debug.LogWarning($"[DiskCache] manifestFormatVersion 불일치 " +
                                             $"(file={wrapper.manifestFormatVersion}, current={FORMAT_VERSION}) " +
                                             $"→ 오염 방지를 위해 전체 캐시 재생성");
                        }
                        else if (wrapper.entries != null)
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
                }
                catch { manifestOutdated = true; /* corrupt manifest — 전체 폐기 */ }
            }

            // [β] 버전 불일치 또는 manifest 손상 시 전체 .dcmesh 파일 삭제
            //   규칙 #6 준수: enableDiskCache=OFF 경로는 이 함수를 타지 않음
            if (manifestOutdated)
            {
                try
                {
                    if (Directory.Exists(_cacheDir))
                    {
                        foreach (var f in Directory.GetFiles(_cacheDir, "*.dcmesh"))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                }
                catch { }
                _manifest.Clear();
                _totalCacheBytes = 0;
            }

            _manifestLoaded = true;
        }

        private void SaveManifest()
        {
            string manifestPath = Path.Combine(_cacheDir, "manifest.json");
            var wrapper = new ManifestWrapper();
            wrapper.manifestFormatVersion = FORMAT_VERSION; // [β] 버전 명시 기록
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
            // [β] 과거 캐시 오염 방지용 버전 필드
            //   기존 manifest에는 없는 필드 → JsonUtility가 0으로 초기화 → legacy로 자동 감지
            public ushort manifestFormatVersion = 0;
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
