// =============================================================================
// CaveNormalBaker.cs  |  pB-4 Project — Week 0~1  (v3)
// =============================================================================
// [v3 핵심 변경]
//   - NormalBakeJobV3: voxelBuffer density 데이터로 서브복셀 그래디언트 샘플링
//   - 버텍스 노말(QEF 평균, ±0.5m 차분) → 서브복셀 노말(±0.05m 차분, 10배 세밀)
//   - RNM 호환 PERTURBATION 인코딩 (이중가산 방지)
//   - autoAssignToShader = true (탄젠트공간 편차 인코딩이 올바르므로)
//
// [v1/v2 오류 요약]
//   v1: 오브젝트공간 인코딩 → RNM이 오브젝트→탄젠트 이중변환 → 방향 폭발
//   v2: tX.xy = (n.z, n.y) → RNM이 n.zy를 이중가산 → 방향 폭발
//   v3: tX.xy = (densNormal.zy - vertNormal.zy) = DEVIATION → RNM 정확 복원
//
// [GPU Readback 활용 이유]
//   voxelBuffer에 저장된 density 값을 서브복셀 스텝(0.05m)으로 재샘플링.
//   GPU 커널(SolveQEF)의 ±0.5m 차분보다 10배 세밀한 그래디언트 계산.
//   ReadbackAsync는 GenerateDensity 직후에 호출되어 voxelBuffer 해제 전에 완료.
//
// [RNM 수식]
//   nX = float3(tX.xy + worldNormal.zy, abs(worldNormal.x))
//   tX.xy = densNormal.zy - vertNormal.zy 이면:
//   nX.xy = (densNormal.zy - vertNormal.zy) + vertNormal.zy = densNormal.zy ✓
//   nX.z  = abs(vertNormal.x)                                              ✓
// =============================================================================
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace CaveSystem
{
    /// <summary>
    /// v3 NormalBakeJob: voxelBuffer density로 서브복셀 그래디언트 → RNM 편차 인코딩.
    /// </summary>
    [BurstCompile]
    public struct NormalBakeJobV3 : IJob
    {
        // ── 메쉬 데이터 ──────────────────────────────────────────────────
        [ReadOnly] public NativeArray<float3> vertexPositions; // 청크 로컬 좌표
        [ReadOnly] public NativeArray<float3> vertexNormals;   // 블렌딩된 버텍스 노말
        [ReadOnly] public NativeArray<float2> vertexUVs;
        // ── 밀도 데이터 (GPU ReadbackAsync 결과) ───────────────────────
        [ReadOnly] public NativeArray<float> densities;       // voxelBuffer density만 추출
        // ── 파라미터 ────────────────────────────────────────────────────
        public NativeArray<Color32> normalMapPixels;
        public int textureWidth;
        public int textureHeight;
        public int dcN;           // pointsPerAxis (35 for chunkSize=32)
        public float3 dcBasePos;   // 청크 베이스 월드 좌표 (chunkPos*worldSize - voxelSize)
        public float voxelSize;
        public float sampleStep;   // 서브복셀 차분 스텝 (권장: voxelSize * 0.1 = 0.05m)

        // ── 밀도 조회 (trilinear 보간) ──────────────────────────────────
        private float SampleDensity(float wx, float wy, float wz)
        {
            // 월드 → 복셀 좌표
            float fx = (wx - dcBasePos.x) / voxelSize;
            float fy = (wy - dcBasePos.y) / voxelSize;
            float fz = (wz - dcBasePos.z) / voxelSize;

            int x0 = math.clamp((int)fx, 0, dcN - 2);
            int y0 = math.clamp((int)fy, 0, dcN - 2);
            int z0 = math.clamp((int)fz, 0, dcN - 2);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;

            float d000 = densities[x0 + y0 * dcN + z0 * dcN * dcN];
            float d100 = densities[x0 + 1 + y0 * dcN + z0 * dcN * dcN];
            float d010 = densities[x0 + (y0 + 1) * dcN + z0 * dcN * dcN];
            float d110 = densities[x0 + 1 + (y0 + 1) * dcN + z0 * dcN * dcN];
            float d001 = densities[x0 + y0 * dcN + (z0 + 1) * dcN * dcN];
            float d101 = densities[x0 + 1 + y0 * dcN + (z0 + 1) * dcN * dcN];
            float d011 = densities[x0 + (y0 + 1) * dcN + (z0 + 1) * dcN * dcN];
            float d111 = densities[x0 + 1 + (y0 + 1) * dcN + (z0 + 1) * dcN * dcN];

            return math.lerp(
                math.lerp(math.lerp(d000, d100, tx), math.lerp(d010, d110, tx), ty),
                math.lerp(math.lerp(d001, d101, tx), math.lerp(d011, d111, tx), ty),
                tz);
        }

        // ── 서브복셀 그래디언트 → 노말 ──────────────────────────────────
        private float3 ComputeSubVoxelNormal(float3 worldPos, float s)
        {
            float3 grad = new float3(
                SampleDensity(worldPos.x + s, worldPos.y, worldPos.z) -
                SampleDensity(worldPos.x - s, worldPos.y, worldPos.z),
                SampleDensity(worldPos.x, worldPos.y + s, worldPos.z) -
                SampleDensity(worldPos.x, worldPos.y - s, worldPos.z),
                SampleDensity(worldPos.x, worldPos.y, worldPos.z + s) -
                SampleDensity(worldPos.x, worldPos.y, worldPos.z - s)
            );
            float len = math.length(grad);
            // density 증가 = 암석 방향 → 반전하여 동굴 내부(공기) 방향
            return len > 1e-6f ? -grad / len : new float3(0, 1, 0);
        }

        public void Execute()
        {
            for (int i = 0; i < vertexPositions.Length; i++)
            {
                float3 localPos = vertexPositions[i];
                float3 vertN = math.normalize(vertexNormals[i]);

                // 로컬 → 월드 좌표 (dcBasePos + voxelSize 오프셋 보정)
                float3 worldPos = dcBasePos + localPos + new float3(voxelSize, voxelSize, voxelSize);

                // 서브복셀 그래디언트 노말
                float3 densN = ComputeSubVoxelNormal(worldPos, sampleStep);

                // ── 지배 축 판별 → RNM 편차 계산 ─────────────────────
                // RNM 수식: nX = (tX.xy + worldNormal.zy, abs(wx))
                // 원하는 결과: densN
                // 편차: tX.xy = densN.zy - vertN.zy
                float3 absN = math.abs(vertN);
                float pert_u, pert_v;

                if (absN.y >= absN.x && absN.y >= absN.z)
                {
                    // Y 지배: uvY = worldPos.xz → nY = (tY.xy + worldN.xz, abs(ny))
                    // pert = densN.xz - vertN.xz
                    pert_u = densN.x - vertN.x;
                    pert_v = densN.z - vertN.z;
                }
                else if (absN.x >= absN.z)
                {
                    // X 지배: uvX = worldPos.zy → nX = (tX.xy + worldN.zy, abs(nx))
                    // pert = densN.zy - vertN.zy
                    pert_u = densN.z - vertN.z;
                    pert_v = densN.y - vertN.y;
                }
                else
                {
                    // Z 지배: uvZ = worldPos.xy → nZ = (tZ.xy + worldN.xy, abs(nz))
                    // pert = densN.xy - vertN.xy
                    pert_u = densN.x - vertN.x;
                    pert_v = densN.y - vertN.y;
                }

                // 클램프: 편차는 ±1 범위
                pert_u = math.clamp(pert_u, -1f, 1f);
                pert_v = math.clamp(pert_v, -1f, 1f);

                // 인코딩: 0~1 → RGB
                byte r = (byte)((pert_u * 0.5f + 0.5f) * 255f);
                byte g = (byte)((pert_v * 0.5f + 0.5f) * 255f);
                byte b = 255; // Z 성분 = 1 (면 방향)

                // UV → 텍셀
                float2 uv = vertexUVs[i];
                int px = math.clamp((int)(uv.x * textureWidth), 0, textureWidth - 1);
                int py = math.clamp((int)(uv.y * textureHeight), 0, textureHeight - 1);
                int pidx = py * textureWidth + px;
                if (pidx >= 0 && pidx < normalMapPixels.Length)
                    normalMapPixels[pidx] = new Color32(r, g, b, 255);
            }
        }
    }

    // =========================================================================

    public class CaveNormalBaker : MonoBehaviour
    {
        [Header("Bake Settings")]
        public int normalMapResolution = 512;

        [Tooltip("true: v3 서브복셀 편차 인코딩 → RNM과 수학적으로 올바름")]
        // [재진단] CaveDreamcoreTerrain.shader는 _BumpMap을 사용하지 않음.
        // 실제 노말은 _RockNormal, _DirtNormal, _MossNormal에서 오며 Inspector에서 설정.
        // 현재 NormalBaker의 의미: _BumpMap 할당 → 셰이더가 무시 → 시각적 효과 없음.
        // autoAssignToShader=false 권장. true 시 _RockNormal 슬롯에 할당하도록 변경됨.
        public bool autoAssignToShader = false;

        [Tooltip("서브복셀 샘플 스텝 = voxelSize × subVoxelFactor. 기본 0.1 = 10배 세밀")]
        [Range(0.05f, 0.5f)]
        public float subVoxelFactor = 0.1f;

        [Header("Debug")]
        [SerializeField] private int lastBakeVertexCount;
        [SerializeField] private float lastBakeTimeMs;

        // =====================================================================
        /// <summary>
        /// v3 베이킹 — GPU density 데이터를 활용한 서브복셀 노말 맵.
        /// densities: voxelBuffer readback에서 추출한 float[] (density 값만).
        /// dcBasePos: chunkPos * chunkWorldSize - voxelSize (월드 좌표).
        /// dcN: DC pointsPerAxis (chunkSize + 3).
        /// </summary>
        public Texture2D BakeNormalMap(Mesh mesh, MeshRenderer targetRenderer,
                                       float[] densities, Vector3 dcBasePos,
                                       int dcN, float voxelSize)
        {
            if (mesh == null) { Debug.LogWarning("[CaveNormalBaker v3] mesh null"); return null; }
            if (densities == null || densities.Length == 0)
            {
                Debug.LogWarning("[CaveNormalBaker v3] density 없음 → flat fallback");
                return BakeFlat(mesh, targetRenderer);
            }

            float startT = Time.realtimeSinceStartup;
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            int vertCount = positions.Length;

            if (vertCount == 0 || normals == null || uvs == null)
                return BakeFlat(mesh, targetRenderer);

            int texW = normalMapResolution;
            int texH = normalMapResolution;
            float step = voxelSize * subVoxelFactor;

            // NativeArray 변환
            var nPos = new NativeArray<float3>(vertCount, Allocator.TempJob);
            var nNorm = new NativeArray<float3>(vertCount, Allocator.TempJob);
            var nUV = new NativeArray<float2>(vertCount, Allocator.TempJob);
            for (int i = 0; i < vertCount; i++)
            {
                nPos[i] = new float3(positions[i].x, positions[i].y, positions[i].z);
                nNorm[i] = new float3(normals[i].x, normals[i].y, normals[i].z);
                nUV[i] = new float2(uvs[i].x, uvs[i].y);
            }
            var nDens = new NativeArray<float>(densities, Allocator.TempJob);
            var nPix = new NativeArray<Color32>(texW * texH, Allocator.TempJob);

            // 초기 배경: (128,128,255) = 편차 0 (버텍스 노말 그대로)
            for (int i = 0; i < nPix.Length; i++)
                nPix[i] = new Color32(128, 128, 255, 255);

            var job = new NormalBakeJobV3
            {
                vertexPositions = nPos,
                vertexNormals = nNorm,
                vertexUVs = nUV,
                densities = nDens,
                normalMapPixels = nPix,
                textureWidth = texW,
                textureHeight = texH,
                dcN = dcN,
                dcBasePos = new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize = voxelSize,
                sampleStep = step,
            };
            job.Schedule().Complete();

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, true)
            {
                name = $"DC_NormalMapV3_{mesh.name}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };
            tex.SetPixelData(nPix.ToArray(), 0);
            tex.Apply(true);

            nPos.Dispose(); nNorm.Dispose(); nUV.Dispose();
            nDens.Dispose(); nPix.Dispose();

            if (autoAssignToShader && targetRenderer != null)
            {
                // MaterialPropertyBlock: sharedMaterial 변경 없이 인스턴스별 프로퍼티 설정
                // → Debug View Normal에 영향 없음, material 인스턴스 생성 오버헤드 없음
                var mpb = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(mpb);
                // [수정] _RockNormal에 할당 — 셰이더가 실제로 사용하는 슬롯
                mpb.SetTexture("_RockNormal", tex);
                targetRenderer.SetPropertyBlock(mpb);
                Debug.Log($"[CaveNormalBaker v3] 서브복셀 노말맵 MPB 설정: step={step:F3}m");
            }

            float elapsed = (Time.realtimeSinceStartup - startT) * 1000f;
            lastBakeVertexCount = vertCount;
            lastBakeTimeMs = elapsed;
            Debug.Log($"[CaveNormalBaker v3] 완료: {texW}×{texH}, vert={vertCount}, {elapsed:F1}ms");
            return tex;
        }

        // ── flat fallback (density 없을 때) ──────────────────────────────────
        public Texture2D BakeFlat(Mesh mesh, MeshRenderer targetRenderer)
        {
            int texW = normalMapResolution, texH = normalMapResolution;
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, true)
            { name = $"DC_NormalFlat_{mesh.name}", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var pixels = new Color32[texW * texH];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(128, 128, 255, 255);
            tex.SetPixelData(pixels, 0); tex.Apply(true);

            if (autoAssignToShader && targetRenderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(mpb);
                // [수정] _RockNormal에 할당 — 셰이더가 실제로 사용하는 슬롯
                mpb.SetTexture("_RockNormal", tex);
                targetRenderer.SetPropertyBlock(mpb);
            }
            return tex;
        }
    }
}