// =============================================================================
// CaveNormalBaker.cs  |  pB-4 — v4 (장기 해결책)
// =============================================================================
// [v4 핵심 변경]
//   - NormalBakeJob: 3개 별도 텍스처 (X/Y/Z 프로젝션)
//   - UV: mesh.uv(localPos) → worldPos × tiling (셰이더 샘플링과 동일 공간)
//   - 편차 클램프: 하드[-1,1] → smoothstep(-0.3, 0.3) (SDF 불연속점 과도 편차 방지)
//   - 결과: _DCNormalMap_X/Y/Z 3개 슬롯 → CaveTriplanarSplat.hlsl에서 RNM 합성
//
// [v1~v3 오류 요약]
//   v1: 오브젝트공간 인코딩 → RNM 이중변환
//   v2: tX.xy=(n.z,n.y) 이중가산
//   v3: worldPos UV 미적용 — mesh.uv 사용 → 셰이더 샘플 UV와 공간 불일치
//   v4: worldPos triplanar UV + 3채널 분리 → 완전 호환
//
// [적용 조건]
//   CaveTriplanarSplat.hlsl에 _DCNormalMap_X/Y/Z 슬롯 및 RNM 합성 코드 추가 필요
//   CaveDreamcoreTerrain.shader에 3개 텍스처 프로퍼티 + 유니폼 선언 필요
// =============================================================================
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace CaveSystem
{
    /// <summary>
    /// 3채널 NormalBakeJob: worldPos triplanar UV 기반 X/Y/Z 프로젝션 별도 베이킹.
    /// 셰이더 샘플링 UV = worldPos × tiling 와 완전히 동일한 공간 사용.
    /// </summary>
    [BurstCompile]
    public struct NormalBakeJobV4 : IJob
    {
        [ReadOnly] public NativeArray<float3> vertexPositions;  // 청크 로컬 좌표
        [ReadOnly] public NativeArray<float3> vertexNormals;    // 블렌딩된 버텍스 노말
        [ReadOnly] public NativeArray<float> densities;        // voxelBuffer density
        // 3채널 텍스처 출력
        public NativeArray<Color32> normalMapX;  // uvX = worldPos.zy × tiling
        public NativeArray<Color32> normalMapY;  // uvY = worldPos.xz × tiling
        public NativeArray<Color32> normalMapZ;  // uvZ = worldPos.xy × tiling
        public int texWidth, texHeight, dcN;
        public float3 dcBasePos;
        public float voxelSize, sampleStep, tiling;

        // ── 밀도 조회 (trilinear) ─────────────────────────────────────────
        private float SampleDensity(float wx, float wy, float wz)
        {
            float fx = (wx - dcBasePos.x) / voxelSize;
            float fy = (wy - dcBasePos.y) / voxelSize;
            float fz = (wz - dcBasePos.z) / voxelSize;
            int x0 = math.clamp((int)fx, 0, dcN - 2);
            int y0 = math.clamp((int)fy, 0, dcN - 2);
            int z0 = math.clamp((int)fz, 0, dcN - 2);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            int n2 = dcN * dcN;
            float d000 = densities[x0 + y0 * dcN + z0 * n2];
            float d100 = densities[x0 + 1 + y0 * dcN + z0 * n2];
            float d010 = densities[x0 + (y0 + 1) * dcN + z0 * n2];
            float d110 = densities[x0 + 1 + (y0 + 1) * dcN + z0 * n2];
            float d001 = densities[x0 + y0 * dcN + (z0 + 1) * n2];
            float d101 = densities[x0 + 1 + y0 * dcN + (z0 + 1) * n2];
            float d011 = densities[x0 + (y0 + 1) * dcN + (z0 + 1) * n2];
            float d111 = densities[x0 + 1 + (y0 + 1) * dcN + (z0 + 1) * n2];
            return math.lerp(
                math.lerp(math.lerp(d000, d100, tx), math.lerp(d010, d110, tx), ty),
                math.lerp(math.lerp(d001, d101, tx), math.lerp(d011, d111, tx), ty), tz);
        }

        // ── 서브복셀 그래디언트 노말 ──────────────────────────────────────
        private float3 SubVoxelNormal(float3 wp)
        {
            float s = sampleStep;
            float3 g = new float3(
                SampleDensity(wp.x + s, wp.y, wp.z) - SampleDensity(wp.x - s, wp.y, wp.z),
                SampleDensity(wp.x, wp.y + s, wp.z) - SampleDensity(wp.x, wp.y - s, wp.z),
                SampleDensity(wp.x, wp.y, wp.z + s) - SampleDensity(wp.x, wp.y, wp.z - s));
            float len = math.length(g);
            return len > 1e-6f ? -g / len : new float3(0, 1, 0);
        }

        // ── smoothstep soft-clamp [-0.3, 0.3] ───────────────────────────
        private static float SoftClamp(float x)
        {
            const float lim = 0.3f;
            float a = x < 0 ? -x : x;
            if (a <= lim) return x;
            float t = math.saturate((a - lim) / (1f - lim));
            t = t * t * (3f - 2f * t); // smoothstep
            float clamped = lim + t * (1f - lim);
            return x < 0 ? -clamped : clamped;
        }

        private static Color32 Encode(float pert_u, float pert_v)
        {
            return new Color32(
                (byte)((pert_u * 0.5f + 0.5f) * 255f),
                (byte)((pert_v * 0.5f + 0.5f) * 255f),
                255, 255);
        }

        private void WriteTexel(NativeArray<Color32> map, float u, float v, float pu, float pv)
        {
            int px = ((int)(math.frac(u) * texWidth) + texWidth) % texWidth;
            int py = ((int)(math.frac(v) * texHeight) + texHeight) % texHeight;
            int idx = py * texWidth + px;
            if (idx >= 0 && idx < map.Length)
                map[idx] = Encode(pu, pv);
        }

        public void Execute()
        {
            for (int i = 0; i < vertexPositions.Length; i++)
            {
                float3 wp = dcBasePos + vertexPositions[i]
                            + new float3(voxelSize, voxelSize, voxelSize); // 월드좌표 복원
                float3 vN = math.normalize(vertexNormals[i]);
                float3 dN = SubVoxelNormal(wp);

                float3 absN = math.abs(vN);

                if (absN.y >= absN.x && absN.y >= absN.z)
                {
                    // Y dominant — uvY = worldPos.xz × tiling
                    // RNM: nY = (tY.xy + worldNormal.xz, abs(ny))
                    // perturbation: tY.xy = dN.xz - vN.xz
                    float pu = SoftClamp(dN.x - vN.x);
                    float pv = SoftClamp(dN.z - vN.z);
                    WriteTexel(normalMapY, wp.x * tiling, wp.z * tiling, pu, pv);
                }
                else if (absN.x >= absN.z)
                {
                    // X dominant — uvX = worldPos.zy × tiling
                    // RNM: nX = (tX.xy + worldNormal.zy, abs(nx))
                    // perturbation: tX.xy = dN.zy - vN.zy
                    float pu = SoftClamp(dN.z - vN.z);
                    float pv = SoftClamp(dN.y - vN.y);
                    WriteTexel(normalMapX, wp.z * tiling, wp.y * tiling, pu, pv);
                }
                else
                {
                    // Z dominant — uvZ = worldPos.xy × tiling
                    // RNM: nZ = (tZ.xy + worldNormal.xy, abs(nz))
                    // perturbation: tZ.xy = dN.xy - vN.xy
                    float pu = SoftClamp(dN.x - vN.x);
                    float pv = SoftClamp(dN.y - vN.y);
                    WriteTexel(normalMapZ, wp.x * tiling, wp.y * tiling, pu, pv);
                }
            }
        }
    }

    // =========================================================================

    public class CaveNormalBaker : MonoBehaviour
    {
        [Header("Bake Settings")]
        public int normalMapResolution = 512;

        [Tooltip("true: _DCNormalMap_X/Y/Z 슬롯에 자동 할당 (셰이더 슬롯 준비 후 활성화)")]
        public bool autoAssignToShader = false;

        [Tooltip("재질에서 사용하는 tiling 값과 반드시 일치해야 함")]
        public float tiling = 0.15f;

        [Tooltip("서브복셀 스텝 = voxelSize × factor. 기본 0.1 = 10배 세밀")]
        [Range(0.05f, 0.5f)]
        public float subVoxelFactor = 0.1f;

        [Header("Debug")]
        [SerializeField] private int lastBakeVertexCount;
        [SerializeField] private float lastBakeTimeMs;

        // =====================================================================
        /// <summary>
        /// v4 베이킹: 3채널(X/Y/Z) worldPos triplanar 서브복셀 노말맵.
        /// 반환: [0]=_DCNormalMap_X, [1]=_DCNormalMap_Y, [2]=_DCNormalMap_Z
        /// </summary>
        public Texture2D[] BakeNormalMap(Mesh mesh, MeshRenderer targetRenderer,
                                          float[] densities, Vector3 dcBasePos,
                                          int dcN, float voxelSize)
        {
            if (mesh == null) return BakeFlat3(mesh, targetRenderer);
            if (densities == null || densities.Length == 0)
                return BakeFlat3(mesh, targetRenderer);

            // [v4-TILING-SYNC] 머티리얼의 _Tiling 값을 자동 동기화
            // 셰이더 샘플링 UV = worldPos × _Tiling 이므로, 베이킹도 동일 값 사용 필수
            float effectiveTiling = tiling;
            if (targetRenderer != null && targetRenderer.sharedMaterial != null
                && targetRenderer.sharedMaterial.HasFloat("_Tiling"))
            {
                effectiveTiling = targetRenderer.sharedMaterial.GetFloat("_Tiling");
            }

            float startT = Time.realtimeSinceStartup;
            var positions = mesh.vertices;
            var normals = mesh.normals;
            int vertCount = positions.Length;
            int texW = normalMapResolution, texH = normalMapResolution;
            float step = voxelSize * subVoxelFactor;

            var nPos = new NativeArray<float3>(vertCount, Allocator.TempJob);
            var nNorm = new NativeArray<float3>(vertCount, Allocator.TempJob);
            for (int i = 0; i < vertCount; i++)
            {
                nPos[i] = new float3(positions[i].x, positions[i].y, positions[i].z);
                nNorm[i] = new float3(normals[i].x, normals[i].y, normals[i].z);
            }
            var nDens = new NativeArray<float>(densities, Allocator.TempJob);
            var nPixX = new NativeArray<Color32>(texW * texH, Allocator.TempJob);
            var nPixY = new NativeArray<Color32>(texW * texH, Allocator.TempJob);
            var nPixZ = new NativeArray<Color32>(texW * texH, Allocator.TempJob);
            // 배경: (128,128,255) = 편차 없음
            var flat = new Color32(128, 128, 255, 255);
            for (int i = 0; i < texW * texH; i++) { nPixX[i] = flat; nPixY[i] = flat; nPixZ[i] = flat; }

            var job = new NormalBakeJobV4
            {
                vertexPositions = nPos,
                vertexNormals = nNorm,
                densities = nDens,
                normalMapX = nPixX,
                normalMapY = nPixY,
                normalMapZ = nPixZ,
                texWidth = texW,
                texHeight = texH,
                dcN = dcN,
                dcBasePos = new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize = voxelSize,
                sampleStep = step,
                tiling = effectiveTiling,
            };
            job.Schedule().Complete();

            var textures = new Texture2D[3];
            string[] suffixes = { "X", "Y", "Z" };
            NativeArray<Color32>[] pixes = { nPixX, nPixY, nPixZ };
            for (int ch = 0; ch < 3; ch++)
            {
                textures[ch] = new Texture2D(texW, texH, TextureFormat.RGBA32, true)
                {
                    name = $"DC_NormalMap{suffixes[ch]}_{mesh.name}",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                };
                textures[ch].SetPixelData(pixes[ch].ToArray(), 0);
                textures[ch].Apply(true);
            }

            nPos.Dispose(); nNorm.Dispose(); nDens.Dispose();
            nPixX.Dispose(); nPixY.Dispose(); nPixZ.Dispose();

            if (autoAssignToShader && targetRenderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(mpb);
                mpb.SetTexture("_DCNormalMap_X", textures[0]);
                mpb.SetTexture("_DCNormalMap_Y", textures[1]);
                mpb.SetTexture("_DCNormalMap_Z", textures[2]);
                targetRenderer.SetPropertyBlock(mpb);
                Debug.Log("[CaveNormalBaker v4] 3채널 DC 노말맵 MPB 설정 완료");
            }

            float ms = (Time.realtimeSinceStartup - startT) * 1000f;
            lastBakeVertexCount = vertCount; lastBakeTimeMs = ms;
            Debug.Log($"[CaveNormalBaker v4] 완료: {texW}×{texH}×3ch, vert={vertCount}, tiling={effectiveTiling:F3}, {ms:F1}ms");
            return textures;
        }

        // ── flat fallback (density 없을 때, 3채널 모두 flat) ────────────
        public Texture2D[] BakeFlat3(Mesh mesh, MeshRenderer targetRenderer)
        {
            int texW = normalMapResolution, texH = normalMapResolution;
            var textures = new Texture2D[3];
            for (int ch = 0; ch < 3; ch++)
            {
                textures[ch] = new Texture2D(texW, texH, TextureFormat.RGBA32, true)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear
                };
                var px = new Color32[texW * texH];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 255, 255);
                textures[ch].SetPixelData(px, 0);
                textures[ch].Apply(true);
            }
            if (autoAssignToShader && targetRenderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(mpb);
                string[] slots = { "_DCNormalMap_X", "_DCNormalMap_Y", "_DCNormalMap_Z" };
                for (int ch = 0; ch < 3; ch++) mpb.SetTexture(slots[ch], textures[ch]);
                targetRenderer.SetPropertyBlock(mpb);
            }
            return textures;
        }
    }
}