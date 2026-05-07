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
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace CaveSystem
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // [F-4 / FORMAT_VERSION 13] Ghost Density Buffer — Burst Job helper
    // ═══════════════════════════════════════════════════════════════════════════════
    // 배경: NormalBaker의 SampleDensity가 chunk boundary에서 clamp 비대칭으로 인접 chunk와
    //       gradient 불일치 → Image 4의 세로 stripe 발생. 업계 표준 Halo Exchange 패턴으로
    //       인접 chunk density를 Job에 전달해 out-of-range에서 neighbor 조회.
    //
    // 저장 전략 (Option B — 단일 연속 버퍼):
    //   self + 6 neighbor density를 하나의 NativeArray<float>에 concat.
    //   offsetTable[slot] = 해당 chunk의 density 시작 인덱스.
    //   메모리 locality 우수, Burst 최적화 유리.
    //
    // Job lifecycle:
    //   [Pre-Schedule] QueryDensityNeighbors → NativeArray allocate + CopyFrom
    //   [Execute]      self density 또는 neighbor density 조회 (branch-free fast path)
    //   [Post-Complete] NativeArray Dispose
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [F-4] Burst Job에 전달되는 neighbor density 패키지.
    /// 7 slots: [0]=self, [1]=+X, [2]=-X, [3]=+Y, [4]=-Y, [5]=+Z, [6]=-Z
    ///
    /// allocation 약속:
    ///   - 모든 NativeArray는 호출자(BakeNormalMap)가 할당·Dispose 책임
    ///   - Job 내부는 read-only 참조만 (NativeDisableParallelForRestriction)
    ///   - existsMask[0]==1 보장 (self는 항상 존재). neighbor는 0 or 1.
    /// </summary>
    public struct PackedNeighborDensity
    {
        public NativeArray<float>  densityBuffer;   // concat: [self..][+X..]...[-Z..]
        public NativeArray<int>    offsetTable;     // length 8 (7 slots + end sentinel)
        public NativeArray<float3> dcBasePosTable;  // length 7 (self + 6)
        public NativeArray<int>    dcNTable;        // length 7
        public NativeArray<int>    existsMask;      // length 7 (0 or 1)
        public float voxelSize;
        public int   enabled;                       // 0=F-4 OFF (기존 clamp 경로), 1=ON
    }

    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 3채널 NormalBakeJob: worldPos triplanar UV 기반 X/Y/Z 프로젝션 별도 베이킹.
    /// 셰이더 샘플링 UV = worldPos × tiling 와 완전히 동일한 공간 사용.
    /// </summary>
    [BurstCompile]
    public struct NormalBakeJobV4 : IJob
    {
        [ReadOnly] public NativeArray<float3> vertexPositions;  // 청크 로컬 좌표
        [ReadOnly] public NativeArray<float3> vertexNormals;    // 블렌딩된 버텍스 노말
        [ReadOnly] public NativeArray<float>  densities;        // voxelBuffer density
        // 3채널 텍스처 출력
        public NativeArray<Color32> normalMapX;  // uvX = worldPos.zy × tiling
        public NativeArray<Color32> normalMapY;  // uvY = worldPos.xz × tiling
        public NativeArray<Color32> normalMapZ;  // uvZ = worldPos.xy × tiling
        public int  texWidth, texHeight, dcN;
        public float3 dcBasePos;
        public float  voxelSize, sampleStep, tiling;

        // [F-4] Ghost density 패키지 — enabled=0 시 기존 clamp 경로 그대로
        //   NativeDisableParallelForRestriction: densityBuffer는 index i와 무관한 접근
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float>  f4_densityBuffer;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<int>    f4_offsetTable;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float3> f4_dcBasePosTable;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<int>    f4_dcNTable;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<int>    f4_existsMask;
        public int f4_enabled;  // 0=OFF (기존 clamp), 1=ON (ghost-aware)

        // ── 밀도 조회 (trilinear, 내부 배열/dcN 기준 헬퍼) ──────────────
        private static float TrilinearAt(NativeArray<float> buf, int bufOffset,
                                          int N, float fx, float fy, float fz)
        {
            int x0 = math.clamp((int)fx, 0, N - 2);
            int y0 = math.clamp((int)fy, 0, N - 2);
            int z0 = math.clamp((int)fz, 0, N - 2);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            int n2 = N * N;
            int baseIdx = bufOffset;
            float d000 = buf[baseIdx + x0     + y0     * N + z0     * n2];
            float d100 = buf[baseIdx + x0 + 1 + y0     * N + z0     * n2];
            float d010 = buf[baseIdx + x0     + (y0+1) * N + z0     * n2];
            float d110 = buf[baseIdx + x0 + 1 + (y0+1) * N + z0     * n2];
            float d001 = buf[baseIdx + x0     + y0     * N + (z0+1) * n2];
            float d101 = buf[baseIdx + x0 + 1 + y0     * N + (z0+1) * n2];
            float d011 = buf[baseIdx + x0     + (y0+1) * N + (z0+1) * n2];
            float d111 = buf[baseIdx + x0 + 1 + (y0+1) * N + (z0+1) * n2];
            return math.lerp(
                math.lerp(math.lerp(d000, d100, tx), math.lerp(d010, d110, tx), ty),
                math.lerp(math.lerp(d001, d101, tx), math.lerp(d011, d111, tx), ty), tz);
        }

        // ── 밀도 조회 (trilinear) ─────────────────────────────────────────
        private float SampleDensity(float wx, float wy, float wz)
        {
            // [F-4] Ghost-aware 경로 (enabled=1 시)
            //   in-range fast path → out-of-range neighbor 조회 → fallback clamp
            if (f4_enabled == 1)
            {
                float fx = (wx - dcBasePos.x) / voxelSize;
                float fy = (wy - dcBasePos.y) / voxelSize;
                float fz = (wz - dcBasePos.z) / voxelSize;

                // Step 1: In-range fast path (>95% 케이스)
                bool inRange = (fx >= 0f) & (fx <= dcN - 2) &
                               (fy >= 0f) & (fy <= dcN - 2) &
                               (fz >= 0f) & (fz <= dcN - 2);
                if (inRange)
                {
                    // self slot offset = 0 (self는 항상 slot 0)
                    return TrilinearAt(f4_densityBuffer, 0, dcN, fx, fy, fz);
                }

                // Step 2: Out-of-range — 가장 violation 큰 axis로 face 결정
                //   face: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
                int face = -1;
                float maxViolation = 0f;
                float vCur;
                vCur = -fx;              if (vCur > maxViolation) { maxViolation = vCur; face = 1; } // -X
                vCur = fx - (dcN - 2);   if (vCur > maxViolation) { maxViolation = vCur; face = 0; } // +X
                vCur = -fy;              if (vCur > maxViolation) { maxViolation = vCur; face = 3; } // -Y
                vCur = fy - (dcN - 2);   if (vCur > maxViolation) { maxViolation = vCur; face = 2; } // +Y
                vCur = -fz;              if (vCur > maxViolation) { maxViolation = vCur; face = 5; } // -Z
                vCur = fz - (dcN - 2);   if (vCur > maxViolation) { maxViolation = vCur; face = 4; } // +Z

                // Step 3: Neighbor exists check — neighbor slot = face + 1
                if (face >= 0 && f4_existsMask[face + 1] == 1)
                {
                    float3 nBase = f4_dcBasePosTable[face + 1];
                    int    nN    = f4_dcNTable[face + 1];
                    float  nfx   = (wx - nBase.x) / voxelSize;
                    float  nfy   = (wy - nBase.y) / voxelSize;
                    float  nfz   = (wz - nBase.z) / voxelSize;
                    // neighbor range 재확인 (diagonal 등 예외 케이스)
                    bool inNeighbor = (nfx >= 0f) & (nfx <= nN - 2) &
                                      (nfy >= 0f) & (nfy <= nN - 2) &
                                      (nfz >= 0f) & (nfz <= nN - 2);
                    if (inNeighbor)
                    {
                        int nOffset = f4_offsetTable[face + 1];
                        return TrilinearAt(f4_densityBuffer, nOffset, nN, nfx, nfy, nfz);
                    }
                }
                // Step 4: fallback → self clamp (원본 경로와 동일)
                return TrilinearAt(f4_densityBuffer, 0, dcN, fx, fy, fz);
            }

            // ── OFF 경로 (byte-identical 원본) ───────────────────────────
            float fxO = (wx - dcBasePos.x) / voxelSize;
            float fyO = (wy - dcBasePos.y) / voxelSize;
            float fzO = (wz - dcBasePos.z) / voxelSize;
            int x0 = math.clamp((int)fxO, 0, dcN-2);
            int y0 = math.clamp((int)fyO, 0, dcN-2);
            int z0 = math.clamp((int)fzO, 0, dcN-2);
            float tx = fxO-x0, ty = fyO-y0, tz = fzO-z0;
            int n2 = dcN*dcN;
            float d000 = densities[x0   + y0   *dcN + z0   *n2];
            float d100 = densities[x0+1 + y0   *dcN + z0   *n2];
            float d010 = densities[x0   +(y0+1)*dcN + z0   *n2];
            float d110 = densities[x0+1 +(y0+1)*dcN + z0   *n2];
            float d001 = densities[x0   + y0   *dcN +(z0+1)*n2];
            float d101 = densities[x0+1 + y0   *dcN +(z0+1)*n2];
            float d011 = densities[x0   +(y0+1)*dcN +(z0+1)*n2];
            float d111 = densities[x0+1 +(y0+1)*dcN +(z0+1)*n2];
            return math.lerp(
                math.lerp(math.lerp(d000,d100,tx), math.lerp(d010,d110,tx), ty),
                math.lerp(math.lerp(d001,d101,tx), math.lerp(d011,d111,tx), ty), tz);
        }

        // ── 서브복셀 그래디언트 노말 ──────────────────────────────────────
        private float3 SubVoxelNormal(float3 wp)
        {
            float s = sampleStep;
            float3 g = new float3(
                SampleDensity(wp.x+s, wp.y, wp.z) - SampleDensity(wp.x-s, wp.y, wp.z),
                SampleDensity(wp.x, wp.y+s, wp.z) - SampleDensity(wp.x, wp.y-s, wp.z),
                SampleDensity(wp.x, wp.y, wp.z+s) - SampleDensity(wp.x, wp.y, wp.z-s));
            float len = math.length(g);
            return len > 1e-6f ? -g/len : new float3(0,1,0);
        }

        // ── smoothstep soft-clamp [-0.3, 0.3] ───────────────────────────
        private static float SoftClamp(float x)
        {
            const float lim = 0.3f;
            float a = x < 0 ? -x : x;
            if (a <= lim) return x;
            float t = math.saturate((a - lim) / (1f - lim));
            t = t*t*(3f - 2f*t); // smoothstep
            float clamped = lim + t*(1f - lim);
            return x < 0 ? -clamped : clamped;
        }

        private static Color32 Encode(float pert_u, float pert_v)
        {
            return new Color32(
                (byte)((pert_u*0.5f+0.5f)*255f),
                (byte)((pert_v*0.5f+0.5f)*255f),
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
    // [P0.5-C] NormalBakeJobV5 — IJobParallelFor (전축+Y-Offset+부호분리)
    // =========================================================================
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct NormalBakeJobV5 : IJobParallelFor
    {
        // [ReadOnly + NativeDisableParallelForRestriction]
        //   vertex i의 densities[x0+y0*dcN+z0*n2] 는 i와 무관한 인덱스 접근
        //   → 두 특성 모두 필요 (ReadOnly만으로는 Burst가 차단)
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float3> vertexPositions;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float3> vertexNormals;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float>  densities;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> accumPosX, accumNegX;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> accumPosY, accumNegY;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> accumPosZ, accumNegZ;
        public int texSize, dcN;
        public float3 dcBasePos;
        public float voxelSize, sampleStep, tiling, bandHeight;

        // [F-4] Ghost density 패키지 (V4와 동일 schema)
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float>  f4_densityBuffer;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<int>    f4_offsetTable;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float3> f4_dcBasePosTable;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<int>    f4_dcNTable;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<int>    f4_existsMask;
        public int f4_enabled;

        private static float2 Hash2D(float seed)
        {
            float2 p = new float2(seed, seed * 1.37f);
            p = math.frac(p * new float2(443.897f, 441.423f));
            p += math.dot(p, p + 19.19f);
            return math.frac(new float2(p.x * p.y, p.x + p.y));
        }

        // [F-4] 공용 trilinear — V4와 동일한 static helper
        private static float TrilinearAt(NativeArray<float> buf, int bufOffset,
                                          int N, float fx, float fy, float fz)
        {
            int x0 = math.clamp((int)fx, 0, N - 2);
            int y0 = math.clamp((int)fy, 0, N - 2);
            int z0 = math.clamp((int)fz, 0, N - 2);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            int n2 = N * N;
            int bi = bufOffset;
            return math.lerp(
                math.lerp(math.lerp(buf[bi + x0     + y0     * N + z0     * n2],
                                    buf[bi + x0 + 1 + y0     * N + z0     * n2], tx),
                           math.lerp(buf[bi + x0     + (y0+1) * N + z0     * n2],
                                    buf[bi + x0 + 1 + (y0+1) * N + z0     * n2], tx), ty),
                math.lerp(math.lerp(buf[bi + x0     + y0     * N + (z0+1) * n2],
                                    buf[bi + x0 + 1 + y0     * N + (z0+1) * n2], tx),
                           math.lerp(buf[bi + x0     + (y0+1) * N + (z0+1) * n2],
                                    buf[bi + x0 + 1 + (y0+1) * N + (z0+1) * n2], tx), ty), tz);
        }

        private float SampleDensity(float wx, float wy, float wz)
        {
            // [F-4] Ghost-aware 경로
            if (f4_enabled == 1)
            {
                float fxG = (wx - dcBasePos.x) / voxelSize;
                float fyG = (wy - dcBasePos.y) / voxelSize;
                float fzG = (wz - dcBasePos.z) / voxelSize;
                bool inRange = (fxG >= 0f) & (fxG <= dcN - 2) &
                               (fyG >= 0f) & (fyG <= dcN - 2) &
                               (fzG >= 0f) & (fzG <= dcN - 2);
                if (inRange)
                    return TrilinearAt(f4_densityBuffer, 0, dcN, fxG, fyG, fzG);

                int face = -1;
                float maxViolation = 0f, vCur;
                vCur = -fxG;             if (vCur > maxViolation) { maxViolation = vCur; face = 1; }
                vCur = fxG - (dcN - 2);  if (vCur > maxViolation) { maxViolation = vCur; face = 0; }
                vCur = -fyG;             if (vCur > maxViolation) { maxViolation = vCur; face = 3; }
                vCur = fyG - (dcN - 2);  if (vCur > maxViolation) { maxViolation = vCur; face = 2; }
                vCur = -fzG;             if (vCur > maxViolation) { maxViolation = vCur; face = 5; }
                vCur = fzG - (dcN - 2);  if (vCur > maxViolation) { maxViolation = vCur; face = 4; }

                if (face >= 0 && f4_existsMask[face + 1] == 1)
                {
                    float3 nBase = f4_dcBasePosTable[face + 1];
                    int    nN    = f4_dcNTable[face + 1];
                    float  nfx   = (wx - nBase.x) / voxelSize;
                    float  nfy   = (wy - nBase.y) / voxelSize;
                    float  nfz   = (wz - nBase.z) / voxelSize;
                    bool inN = (nfx >= 0f) & (nfx <= nN - 2) &
                               (nfy >= 0f) & (nfy <= nN - 2) &
                               (nfz >= 0f) & (nfz <= nN - 2);
                    if (inN)
                    {
                        int nOff = f4_offsetTable[face + 1];
                        return TrilinearAt(f4_densityBuffer, nOff, nN, nfx, nfy, nfz);
                    }
                }
                return TrilinearAt(f4_densityBuffer, 0, dcN, fxG, fyG, fzG);
            }

            // ── OFF 경로 (원본 byte-identical) ────────────────────────────
            float fx=(wx-dcBasePos.x)/voxelSize; float fy=(wy-dcBasePos.y)/voxelSize; float fz=(wz-dcBasePos.z)/voxelSize;
            int x0=math.clamp((int)fx,0,dcN-2); int y0=math.clamp((int)fy,0,dcN-2); int z0=math.clamp((int)fz,0,dcN-2);
            float tx=fx-x0,ty=fy-y0,tz=fz-z0; int n2=dcN*dcN;
            return math.lerp(
                math.lerp(math.lerp(densities[x0+y0*dcN+z0*n2],densities[x0+1+y0*dcN+z0*n2],tx),
                           math.lerp(densities[x0+(y0+1)*dcN+z0*n2],densities[x0+1+(y0+1)*dcN+z0*n2],tx),ty),
                math.lerp(math.lerp(densities[x0+y0*dcN+(z0+1)*n2],densities[x0+1+y0*dcN+(z0+1)*n2],tx),
                           math.lerp(densities[x0+(y0+1)*dcN+(z0+1)*n2],densities[x0+1+(y0+1)*dcN+(z0+1)*n2],tx),ty),tz);
        }
        private float3 SubVoxelNormal(float3 wp)
        {
            float s=sampleStep;
            float3 g=new float3(SampleDensity(wp.x+s,wp.y,wp.z)-SampleDensity(wp.x-s,wp.y,wp.z),
                SampleDensity(wp.x,wp.y+s,wp.z)-SampleDensity(wp.x,wp.y-s,wp.z),
                SampleDensity(wp.x,wp.y,wp.z+s)-SampleDensity(wp.x,wp.y,wp.z-s));
            float len=math.length(g); return len>1e-6f?-g/len:new float3(0,1,0);
        }
        private static float SoftClamp(float x)
        {
            const float lim=0.3f; float a=x<0?-x:x; if(a<=lim)return x;
            float t=math.saturate((a-lim)/(1f-lim)); t=t*t*(3f-2f*t);
            return x<0?-(lim+t*(1f-lim)):(lim+t*(1f-lim));
        }
        private unsafe void WriteAccum(NativeArray<int> pos, NativeArray<int> neg,
            float2 uv, float pu, float pv, float w, float signAxis)
        {
            int px=((int)(math.frac(uv.x)*texSize)+texSize)%texSize;
            int py=((int)(math.frac(uv.y)*texSize)+texSize)%texSize;
            int bi=(py*texSize+px)*3;
            if(bi<0||bi+2>=pos.Length) return;
            int iPU=(int)(pu*w*10000f); int iPV=(int)(pv*w*10000f); int iW=(int)(w*10000f);
            if(iW==0) return;
            int* t=(signAxis>=0)?(int*)pos.GetUnsafePtr():(int*)neg.GetUnsafePtr();
            System.Threading.Interlocked.Add(ref t[bi],iPU);
            System.Threading.Interlocked.Add(ref t[bi+1],iPV);
            System.Threading.Interlocked.Add(ref t[bi+2],iW);
        }
        public void Execute(int i)
        {
            float3 wp=dcBasePos+vertexPositions[i]+new float3(voxelSize,voxelSize,voxelSize);
            float3 vN=vertexNormals[i]; float vL=math.length(vN); if(vL<0.001f)return; vN/=vL;
            float3 dN=SubVoxelNormal(wp);
            float3 absN=math.abs(vN); float sum=absN.x+absN.y+absN.z; if(sum<0.001f)return;
            float yBand=math.floor(wp.y/bandHeight);
            float2 offXZ=Hash2D(yBand)*0.4f, offZY=Hash2D(yBand+100f)*0.4f, offXY=Hash2D(yBand+200f)*0.4f;
            WriteAccum(accumPosY,accumNegY,(wp.xz+offXZ)*tiling,SoftClamp(dN.x-vN.x),SoftClamp(dN.z-vN.z),absN.y/sum,vN.y);
            WriteAccum(accumPosX,accumNegX,(wp.zy+offZY)*tiling,SoftClamp(dN.z-vN.z),SoftClamp(dN.y-vN.y),absN.x/sum,vN.x);
            WriteAccum(accumPosZ,accumNegZ,(wp.xy+offXY)*tiling,SoftClamp(dN.x-vN.x),SoftClamp(dN.y-vN.y),absN.z/sum,vN.z);
        }
    }

    [BurstCompile]
    public struct FinalizeAccumJob : IJobParallelFor
    {
        // [ReadOnly + NativeDisableParallelForRestriction]
        //   Execute(idx)에서 accumPos[idx*3+0], [idx*3+1], [idx*3+2] 접근
        //   → idx와 다른 인덱스이므로 두 특성 모두 필요
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<int> accumPos, accumNeg;
        public NativeArray<Color32> output;
        public void Execute(int idx)
        {
            int b=idx*3; int wP=accumPos[b+2]; int wN=accumNeg[b+2];
            if(wP==0&&wN==0){output[idx]=new Color32(128,128,255,255);return;}
            bool useP=wP>=wN;
            float pu=(float)(useP?accumPos[b]:accumNeg[b])/(float)(useP?wP:wN);
            float pv=(float)(useP?accumPos[b+1]:accumNeg[b+1])/(float)(useP?wP:wN);
            output[idx]=new Color32((byte)math.clamp((int)((pu*0.5f+0.5f)*255f),0,255),
                (byte)math.clamp((int)((pv*0.5f+0.5f)*255f),0,255),255,255);
        }
    }

    [BurstCompile]
    public struct DilationJob : IJobParallelFor
    {
        // [ReadOnly + NativeDisableParallelForRestriction]
        //   Execute(idx)에서 input[neighbor idx] 접근 (8-neighbor)
        //   → idx와 다른 인덱스이므로 두 특성 모두 필요
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<Color32> input;
        [WriteOnly] public NativeArray<Color32> output;
        public int texSize;
        public void Execute(int idx)
        {
            Color32 c=input[idx];
            if(c.r!=128||c.g!=128){output[idx]=c;return;}
            int x=idx%texSize,y=idx/texSize; int sR=0,sG=0,cnt=0;
            for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++){
                if(dx==0&&dy==0)continue;
                Color32 n=input[((y+dy+texSize)%texSize)*texSize+((x+dx+texSize)%texSize)];
                if(n.r!=128||n.g!=128){sR+=n.r;sG+=n.g;cnt++;}
            }
            output[idx]=cnt>0?new Color32((byte)(sR/cnt),(byte)(sG/cnt),255,255):new Color32(128,128,255,255);
        }
    }

    // =========================================================================

    public class CaveNormalBaker : MonoBehaviour
    {
        [Header("Bake Settings")]
        public int   normalMapResolution = 512;

        [Tooltip("true: _DCNormalMap_X/Y/Z 슬롯에 자동 할당 (셰이더 슬롯 준비 후 활성화)")]
        public bool  autoAssignToShader = false;

        [Tooltip("재질에서 사용하는 tiling 값과 반드시 일치해야 함")]
        public float tiling = 0.2f;  // 셰이더 기본값 _Tiling=0.2 일치

        [Tooltip("ON: 대상 MeshRenderer 재질의 _Tiling을 자동 읽기")]
        public bool autoSyncTiling = true;

        [Tooltip("서브복셀 스텝 = voxelSize × factor. 기본 0.1 = 10배 세밀")]
        [Range(0.05f, 0.5f)]
        public float subVoxelFactor = 0.1f;

        [Header("P0.5-C — NormalBaker v5")]
        [Tooltip("ON: v5 (전축+Y-Offset+부호분리+Dilation+병렬). OFF: 원본 v4")]
        public bool enableBakerV5 = false;

        [Tooltip("Y-Offset 대역 높이(m). 셰이더 _BandHeight와 동일")]
        public float bandHeight = 5.0f;

        [Header("P3-NB-A — Async Bake")]
        [Tooltip("ON: BakeJob 체인을 비동기로 스케줄 + Update 폴링 (청크당 -14.9ms 메인). OFF: sync (원본)")]
        public bool enableAsyncBake = false;

        [Header("Debug")]
        [SerializeField] private int   lastBakeVertexCount;
        [SerializeField] private float lastBakeTimeMs;
        [SerializeField] private int   lastTexResolution;

        [Header("디버깅 — 진단 로그")]
        [Tooltip("ON → 청크 베이킹 / 리베이크 진단 로그 출력. " +
         "Production / 다른 트랙 테스트 시 OFF 권장.")]
        [SerializeField] private bool _verboseDiagLogging = false;

        // =====================================================================
        // [P3-NB-A] Async Bake — JobHandle 체인 + Update 폴링 + 3중 Dispose 방어
        // =====================================================================
        public static CaveNormalBaker Instance { get; private set; }

        private struct PendingBake
        {
            public JobHandle finalHandle;
            public MeshRenderer renderer;
            public Mesh expectedMesh;          // [위험 ① 방어] 풀 재사용 감지
            public string capturedMeshName;    // [Mesh.name 안전 캡처]
            public int texRes;
            // 입력 버퍼
            public NativeArray<float3> nPos, nNorm;
            public NativeArray<float>  nDens;
            // accum (8)  ※ accumPosX/accumNegX/... 6개
            public NativeArray<int> aPX, aNX, aPY, aNY, aPZ, aNZ;
            // pixel ping-pong per channel (6)
            public NativeArray<Color32> pixX_A, pixX_B;
            public NativeArray<Color32> pixY_A, pixY_B;
            public NativeArray<Color32> pixZ_A, pixZ_B;
            // 최종 데이터 슬롯 (0=A, 1=B)
            public int finalSlotX, finalSlotY, finalSlotZ;
            // [F-4] Ghost density pack — Complete 후 Dispose
            public PackedNeighborDensity f4Pack;
            public int f4Enabled;
            // [F-4-RB] re-bake 트리거용 chunk 좌표
            public Vector3Int chunkPos;
            public int chunkPosValid;  // 0=invalid (legacy path), 1=valid
        }
        private List<PendingBake> _pendingBakes = new List<PendingBake>();

        // ═══════════════════════════════════════════════════════════════════════════════
        // [F-4-RB Re-Bake Trigger] — Streaming Timing Race 해결
        // ═══════════════════════════════════════════════════════════════════════════════
        // 배경: Chunk bake 시점에 neighbor density 미등록이면 해당 방향은 fallback clamp.
        //       나중에 neighbor가 등록돼도 re-bake 없으면 영원히 불완전.
        // 원리: ChunkGhostDataManager.OnDensityRegistered 구독.
        //       새 chunk 등록 시 6방향 neighbor 중 이미 bake된 것을 _rebakeQueue에 추가.
        //       Update에서 프레임당 maxRebakesPerFrame개씩 처리 (throttle).
        //
        // 수명 관리:
        //   - _bakedChunks: bake 완료된 chunk 추적 (중복 re-bake 방지)
        //   - _rebakeSet: 큐 중복 등록 방지 (동일 chunk가 여러 neighbor로 여러 번 트리거돼도 1회만 처리)
        //   - CaveChunkManager.ReturnToPool의 UnregisterChunk 경로에서 제거 필요 (아래 API)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("F-4-RB — Re-Bake Trigger (Streaming Timing Race 해결)")]
        [Tooltip("신규 chunk density 등록 시 인접 이미-bake된 chunk를 재 bake. enableGhostDensityBaking=true일 때만 활성.")]
        public bool enableRebakeTrigger = true;

        [Tooltip("프레임당 re-bake 처리 최대 개수 — 기본 2. Streaming throughput과 품질 수렴 속도 balance.")]
        [Range(0, 8)]
        public int maxRebakesPerFrame = 2;

        [Tooltip("F-4-RB 디버그 로그 출력")]
        public bool rebakeDebugLogs = true;  // [진단] 임시 기본 true — 문제 해결 후 false로 복귀

        private readonly HashSet<Vector3Int> _bakedChunks = new HashSet<Vector3Int>();
        private readonly Queue<Vector3Int>   _rebakeQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> _rebakeSet   = new HashSet<Vector3Int>();
        // [F-4-RB v2] 무한 루프 방지용 추적
        private readonly HashSet<Vector3Int> _completeChunks = new HashSet<Vector3Int>();  // 6 neighbor 모두 보유 → 추가 re-bake 불필요
        private readonly Dictionary<Vector3Int, int> _rebakeCount = new Dictionary<Vector3Int, int>();  // 하드 safety bound
        private const int MAX_REBAKE_PER_CHUNK = 6;  // 초기 bake + 최대 6회 re-bake (streaming 느린 neighbor 수집 커버)
        private bool _subscribedToGhostManager = false;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // [F-4-RB] GhostDataManager가 씬에 로드된 후 구독
            //   Awake가 아닌 Start에서 구독 → 모든 매니저 Awake 완료 보장
            TrySubscribeToGhostManager();
        }

        private void TrySubscribeToGhostManager()
        {
            if (_subscribedToGhostManager) return;

            var mgr = ChunkGhostDataManager.Instance;
            if (mgr == null)
            {
                // Fallback: Instance null이어도 scene에 GameObject가 있을 수 있음
                mgr = UnityEngine.Object.FindObjectOfType<ChunkGhostDataManager>();
                if (mgr != null)
                    Debug.LogWarning("[F-4-RB:DIAG] TrySubscribe: Instance null but FindObjectOfType found one! " +
                                     "Singleton setup race detected.");
            }

            if (mgr == null)
            {
                // [진단] 지연 구독 상태 확인용 (첫 수회만 출력하도록 아래 플래그로 throttle)
                if (!_loggedSubscribeDefer)
                {
                    Debug.LogWarning("[F-4-RB:DIAG] Subscription deferred — ChunkGhostDataManager not found in scene yet. " +
                                     "Will keep retrying in Update. " +
                                     "If this persists, verify ChunkGhostDataManager component is attached to a GameObject.");
                    _loggedSubscribeDefer = true;
                }
                return;
            }

            mgr.OnDensityRegistered += OnNeighborDensityRegistered;
            _subscribedToGhostManager = true;
            // [진단] 구독 성공 확정 로그 (무조건)
            Debug.Log($"[F-4-RB:DIAG] ✓ Subscribed to OnDensityRegistered on {mgr.gameObject.name}");
        }

        private bool _loggedSubscribeDefer = false;

        /// <summary>
        /// [F-4-RB] 새 chunk density 등록 시 콜백.
        /// 영향 범위 6 neighbor 중 이미 bake된 것을 re-bake 큐에 추가.
        /// _completeChunks (6 neighbor 완료)와 _rebakeCount 하드 제한 체크.
        /// </summary>
        private void OnNeighborDensityRegistered(Vector3Int newChunk)
        {
            if (!enableRebakeTrigger) return;

            var offsets = new[] {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };
            int queued = 0;
            int skippedComplete = 0;
            int skippedMaxed = 0;
            for (int f = 0; f < 6; f++)
            {
                var pos = newChunk + offsets[f];
                if (!_bakedChunks.Contains(pos)) continue;
                if (_completeChunks.Contains(pos)) { skippedComplete++; continue; }
                if (_rebakeSet.Contains(pos)) continue;

                // 하드 제한 확인
                int cnt; _rebakeCount.TryGetValue(pos, out cnt);
                if (cnt >= MAX_REBAKE_PER_CHUNK) { skippedMaxed++; continue; }

                _rebakeQueue.Enqueue(pos);
                _rebakeSet.Add(pos);
                queued++;
            }
            // [진단] 결과 로그 (queued=0이어도 skip 이유 확인용)
            if (queued > 0 || skippedComplete > 0 || skippedMaxed > 0)
                Debug.Log($"[F-4-RB:DIAG] Event {newChunk}: queued={queued}, " +
                          $"skippedComplete={skippedComplete}, skippedMaxed={skippedMaxed}, " +
                          $"totalQueue={_rebakeQueue.Count}");
        }

        /// <summary>
        /// [F-4-RB v3] chunk bake 완료 표시. Activity-aware completion.
        ///
        /// 안전장치:
        ///   1) added=false (re-bake)면 SymmetricRebake cascade 금지
        ///   2) 활성 neighbor 모두 baked면 _completeChunks로 승급 → 추가 queue 차단
        ///      (world edge / viewDistance 밖 neighbor는 제외 — Y축 1-layer 등)
        ///   3) chunk당 MAX_REBAKE_PER_CHUNK 하드 제한
        /// </summary>
        internal void MarkChunkBaked(Vector3Int chunkPos)
        {
            bool added = _bakedChunks.Add(chunkPos);

            // [Activity-aware completion] 현재 활성 chunks 중 이 chunk의 neighbor는 몇 개이고
            // 그 중 baked는 몇 개인지 계산
            var offsets = new[] {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };
            int bakedNeighbors = 0;
            int activeNeighbors = 0;
            int totalBakedNeighbors = 0;  // 진단용 (활성 여부 무관)
            var chunkMgr = CaveManager.Instance?.chunkManager;
            for (int f = 0; f < 6; f++)
            {
                var nPos = chunkPos + offsets[f];
                bool isBaked = _bakedChunks.Contains(nPos);
                if (isBaked) totalBakedNeighbors++;

                // 활성 범위 내인지 체크 — activeChunks에 있으면 "곧 baked 될 예정"
                bool isActive = chunkMgr != null && chunkMgr.TryGetActiveChunk(nPos, out _);
                if (isActive)
                {
                    activeNeighbors++;
                    if (isBaked) bakedNeighbors++;
                }
                // else: 이 neighbor는 현재 활성 아님 (world edge, viewDistance 밖) → complete 판정에서 제외
            }

            // 모든 활성 neighbor가 baked이면 complete로 승급
            bool becameComplete = false;
            if (activeNeighbors > 0 && bakedNeighbors == activeNeighbors && !_completeChunks.Contains(chunkPos))
            {
                _completeChunks.Add(chunkPos);
                becameComplete = true;
            }
            if (_verboseDiagLogging)
            {
                Debug.Log($"[F-4-RB:DIAG] MarkChunkBaked {chunkPos}, added={added}, " +
                          $"baked={_bakedChunks.Count}, complete={_completeChunks.Count}, " +
                          $"neighbors={bakedNeighbors}/{activeNeighbors}active({totalBakedNeighbors}total)" +
                          $"{(becameComplete ? " ★COMPLETE" : "")}");
            }

            // ═══════════════════════════════════════════════════════════════════════════
            // [Gate 3 / A-α v2] Track E-II Stitch Trigger — v1 결함 수정
            //
            // v1 결함:
            //   1. 양쪽 모두 complete 필요 → edge chunk (neighbor<5/5)는 영원히 미트리거
            //   2. re-bake 시(added=false) trigger 안됨 → 재 bake된 normal map이 
            //      기존 stitch 결과를 덮어쓰기만 하고 재평균 안 됨
            //
            // v2 수정:
            //   - 양쪽 "baked" 상태이기만 하면 stitch 시도 (complete 대기 X)
            //   - becameComplete || (added==false && 기존 baked) 둘 다 trigger
            //   - 단 over-trigger 방지 위해: neighbor가 baked 상태일 때만 (미bake skip)
            // ═══════════════════════════════════════════════════════════════════════════
            if (ChunkSeamStitcher.Instance != null
                && ChunkSeamStitcher.Instance.enableNormalTexelStitch)
            {
                // re-bake: added=false이면서 이 chunk가 이미 baked 상태 → normal map 갱신됨
                bool isRebakeUpdate = !added && _bakedChunks.Contains(chunkPos);
                bool shouldStitch = becameComplete || isRebakeUpdate;

                if (shouldStitch)
                {
                    var offsetsStitch = new[] {
                        new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                        new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                        new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
                    };
                    for (int f = 0; f < 6; f++)
                    {
                        var nPos = chunkPos + offsetsStitch[f];
                        // [v2] baked만 확인 (complete까지 기다리지 않음)
                        //     → edge chunk + in-progress neighbor 모두 커버
                        //     → re-bake cycle 중에도 stitch가 최신 상태 따라잡음
                        if (_bakedChunks.Contains(nPos))
                        {
                            ChunkSeamStitcher.Instance.StitchNormalMapTexels(chunkPos, nPos, f);
                        }
                    }
                }
            }

            if (!enableRebakeTrigger) return;
            // [안전장치 1] 재호출(re-bake 완료)은 symmetric cascade 발생 금지
            if (!added) return;

            // ── 초기 bake: symmetric rebake로 주변 incomplete neighbor를 queue ──
            int symQueued = 0;
            int skippedComplete = 0;
            int skippedMaxed = 0;
            for (int f = 0; f < 6; f++)
            {
                var nPos = chunkPos + offsets[f];
                if (!_bakedChunks.Contains(nPos)) continue;
                if (_completeChunks.Contains(nPos)) { skippedComplete++; continue; }
                if (_rebakeSet.Contains(nPos)) continue;

                int cnt; _rebakeCount.TryGetValue(nPos, out cnt);
                if (cnt >= MAX_REBAKE_PER_CHUNK) { skippedMaxed++; continue; }

                _rebakeQueue.Enqueue(nPos);
                _rebakeSet.Add(nPos);
                symQueued++;
            }
            if (symQueued > 0 || skippedComplete > 0 || skippedMaxed > 0)
            {
                if (_verboseDiagLogging)
                {
                    Debug.Log($"[F-4-RB:DIAG] MarkBaked({chunkPos}) SymRB: queued={symQueued}, " +
                          $"skipComp={skippedComplete}, skipMax={skippedMaxed}, totalQueue={_rebakeQueue.Count}");
                }
            }
        }

        /// <summary>
        /// [F-4-RB] CaveChunkManager.ReturnToPool에서 호출 — chunk pool 반환 시 정리.
        /// Optional — 호출 안 해도 누수 없으나 _bakedChunks 증가 방지 효과.
        /// </summary>
        public void NotifyChunkUnregistered(Vector3Int chunkPos)
        {
            _bakedChunks.Remove(chunkPos);
            _rebakeSet.Remove(chunkPos);
            _completeChunks.Remove(chunkPos);  // [F-4-RB v2]
            _rebakeCount.Remove(chunkPos);     // [F-4-RB v2]
            // _rebakeQueue은 Dequeue 시점 자연 정리 (RequestRebake에서 TryGetActiveChunk로 검증)
        }

        private void Update()
        {
            // [F-4-RB] GhostDataManager가 나중에 생성됐을 경우 재시도
            if (!_subscribedToGhostManager && ChunkGhostDataManager.Instance != null)
                TrySubscribeToGhostManager();

            // ── [F-4-RB] Re-bake 큐 처리 (프레임당 throttle) ──
            if (enableRebakeTrigger && _rebakeQueue.Count > 0 && maxRebakesPerFrame > 0)
            {
                var meshBuilder = GetComponent<DCMeshBuilder>();
                if (meshBuilder != null)
                {
                    int processed = 0;
                    while (processed < maxRebakesPerFrame && _rebakeQueue.Count > 0)
                    {
                        var pos = _rebakeQueue.Dequeue();
                        _rebakeSet.Remove(pos);

                        // [하드 제한 최종 방어] dequeue 시점에도 다시 확인
                        int cnt; _rebakeCount.TryGetValue(pos, out cnt);
                        if (cnt >= MAX_REBAKE_PER_CHUNK) continue;
                        if (_completeChunks.Contains(pos)) continue;  // complete 된 것도 skip

                        // count 증가 후 re-bake 호출
                        _rebakeCount[pos] = cnt + 1;
                        meshBuilder.RequestRebake(pos);
                        processed++;
                    }
                    if (rebakeDebugLogs && processed > 0)
                        Debug.Log($"[F-4-RB] Processed {processed} re-bakes, queue={_rebakeQueue.Count}, " +
                                  $"complete={_completeChunks.Count}");
                }
            }

            if (!enableAsyncBake) return;
            if (_pendingBakes.Count == 0) return;

            // 완료된 pending 처리 (역순 순회 — 중간 제거 안전)
            for (int i = _pendingBakes.Count - 1; i >= 0; i--)
            {
                var pb = _pendingBakes[i];
                if (!pb.finalHandle.IsCompleted) continue;

                pb.finalHandle.Complete(); // 완료 확실히 (동기화)

                // [위험 ① 방어] renderer + mesh 일치 확인
                bool valid = pb.renderer != null;
                if (valid)
                {
                    var filter = pb.renderer.GetComponent<MeshFilter>();
                    valid = filter != null && filter.sharedMesh == pb.expectedMesh;
                }

                if (valid)
                {
                    // 텍스처 생성 + Apply + MaterialPropertyBlock
                    ApplyBakedTextures(pb);
                    // [F-4-RB] Async 경로 성공 시 bake 완료 표시
                    if (pb.chunkPosValid == 1)
                        MarkChunkBaked(pb.chunkPos);
                }
                // else: 풀 재사용 또는 renderer 제거됨 → 텍스처 버림

                DisposePendingBake(pb);
                _pendingBakes.RemoveAt(i);
            }
        }

        private void ApplyBakedTextures(PendingBake pb)
        {
            var finalX = (pb.finalSlotX == 0) ? pb.pixX_A : pb.pixX_B;
            var finalY = (pb.finalSlotY == 0) ? pb.pixY_A : pb.pixY_B;
            var finalZ = (pb.finalSlotZ == 0) ? pb.pixZ_A : pb.pixZ_B;

            var textures = new Texture2D[3];
            var finals = new [] { finalX, finalY, finalZ };
            string[] suf = { "X", "Y", "Z" };
            for (int ch = 0; ch < 3; ch++)
            {
                textures[ch] = new Texture2D(pb.texRes, pb.texRes, TextureFormat.RGBA32, true)
                {
                    name = $"DC_NormalV5_{suf[ch]}_{pb.capturedMeshName}_async",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear
                };
                textures[ch].SetPixelData(finals[ch], 0);  // NativeArray 직접 (GC 0, -3.15MB/chunk/ch)
                textures[ch].Apply(true);
            }
            if (autoAssignToShader && pb.renderer != null)
            {
                // [FIX-TEX] 이전 텍스처 Destroy 후 새 텍스처 할당
                DestroyOldDCNormalTextures(pb.renderer);
                var mpb = new MaterialPropertyBlock();
                pb.renderer.GetPropertyBlock(mpb);
                mpb.SetTexture("_DCNormalMap_X", textures[0]);
                mpb.SetTexture("_DCNormalMap_Y", textures[1]);
                mpb.SetTexture("_DCNormalMap_Z", textures[2]);
                pb.renderer.SetPropertyBlock(mpb);
            }
        }

        // =====================================================================
        // [FIX-TEX] 이전 DC NormalMap 텍스처 Destroy
        //   청크 re-bake 또는 풀 재사용 시 이전 텍스처 GPU 누수 방지
        //   MaterialPropertyBlock에서 텍스처 참조 추출 → Object.Destroy
        // =====================================================================
        private static readonly string[] _dcNormalSlots = {
            "_DCNormalMap_X", "_DCNormalMap_Y", "_DCNormalMap_Z"
        };

        private static void DestroyOldDCNormalTextures(MeshRenderer renderer)
        {
            if (renderer == null) return;
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            for (int i = 0; i < _dcNormalSlots.Length; i++)
            {
                var oldTex = mpb.GetTexture(_dcNormalSlots[i]);
                if (oldTex != null)
                    Object.Destroy(oldTex);
            }
        }

        private static void DisposePendingBake(PendingBake pb)
        {
            if (pb.nPos.IsCreated)  pb.nPos.Dispose();
            if (pb.nNorm.IsCreated) pb.nNorm.Dispose();
            if (pb.nDens.IsCreated) pb.nDens.Dispose();
            if (pb.aPX.IsCreated)   pb.aPX.Dispose();
            if (pb.aNX.IsCreated)   pb.aNX.Dispose();
            if (pb.aPY.IsCreated)   pb.aPY.Dispose();
            if (pb.aNY.IsCreated)   pb.aNY.Dispose();
            if (pb.aPZ.IsCreated)   pb.aPZ.Dispose();
            if (pb.aNZ.IsCreated)   pb.aNZ.Dispose();
            if (pb.pixX_A.IsCreated) pb.pixX_A.Dispose();
            if (pb.pixX_B.IsCreated) pb.pixX_B.Dispose();
            if (pb.pixY_A.IsCreated) pb.pixY_A.Dispose();
            if (pb.pixY_B.IsCreated) pb.pixY_B.Dispose();
            if (pb.pixZ_A.IsCreated) pb.pixZ_A.Dispose();
            if (pb.pixZ_B.IsCreated) pb.pixZ_B.Dispose();
            // [F-4] Ghost density pack Dispose
            DisposeF4NeighborPack(pb.f4Pack);
        }

        /// <summary>
        /// [위험 ③ 방어] 풀링/파괴 전에 해당 renderer의 pending bake 강제 완료+정리.
        /// CaveChunkManager.ReturnToPool, DestroyCoarse에서 호출.
        /// </summary>
        public void CancelPendingBakesForRenderer(MeshRenderer target)
        {
            if (target == null || _pendingBakes.Count == 0) return;
            for (int i = _pendingBakes.Count - 1; i >= 0; i--)
            {
                if (_pendingBakes[i].renderer != target) continue;
                var pb = _pendingBakes[i];
                pb.finalHandle.Complete();   // 강제 동기화
                DisposePendingBake(pb);      // NativeArray 해제
                _pendingBakes.RemoveAt(i);
            }
        }

        /// <summary>[위험 ③ 방어] OnDestroy 시 전체 pending 순회 강제 완료+정리.</summary>
        private void OnDestroy()
        {
            // [F-4-RB] 구독 해제 (누수 방지)
            if (_subscribedToGhostManager && ChunkGhostDataManager.Instance != null)
            {
                ChunkGhostDataManager.Instance.OnDensityRegistered -= OnNeighborDensityRegistered;
                _subscribedToGhostManager = false;
            }
            _bakedChunks.Clear();
            _rebakeQueue.Clear();
            _rebakeSet.Clear();
            _completeChunks.Clear();  // [F-4-RB v2]
            _rebakeCount.Clear();     // [F-4-RB v2]

            for (int i = 0; i < _pendingBakes.Count; i++)
            {
                var pb = _pendingBakes[i];
                pb.finalHandle.Complete();
                DisposePendingBake(pb);
            }
            _pendingBakes.Clear();
            if (Instance == this) Instance = null;
        }

        // =====================================================================
        /// <summary>
        /// [P0.5-C 토글] enableBakerV5=OFF→v4 원본, ON→v5 경로
        /// [Legacy 시그니처] chunkPos 없이 호출 — F-4 비활성 (하위 호환)
        /// </summary>
        public Texture2D[] BakeNormalMap(Mesh mesh, MeshRenderer targetRenderer,
                                          float[] densities, Vector3 dcBasePos,
                                          int dcN, float voxelSize)
        {
            // [F-4] chunkPos 미전달 → F-4 비활성화 (dummy chunkPos)
            return BakeNormalMap(mesh, targetRenderer, densities, dcBasePos,
                                  dcN, voxelSize, Vector3Int.zero, /*f4Enabled=*/false);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [F-4 / FORMAT_VERSION 13] Ghost Density Buffer — 확장 시그니처 + 준비 헬퍼
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [F-4] ChunkGhostDataManager에서 neighbor density를 조회하고
        /// Burst Job에 전달할 PackedNeighborDensity를 구성.
        /// 모든 NativeArray는 TempJob Allocator — 호출자가 Dispose 책임.
        /// </summary>
        private static PackedNeighborDensity PrepareF4NeighborPack(
            Vector3Int chunkPos, float[] selfDensity, Vector3 selfDcBasePos,
            int selfDcN, float voxelSize, bool f4Enabled)
        {
            // 비활성 경로: 최소 크기 dummy NativeArray 반환 (SetData 안전성)
            if (!f4Enabled)
            {
                return new PackedNeighborDensity {
                    densityBuffer   = new NativeArray<float>(1, Allocator.TempJob),
                    offsetTable     = new NativeArray<int>(8, Allocator.TempJob),
                    dcBasePosTable  = new NativeArray<float3>(7, Allocator.TempJob),
                    dcNTable        = new NativeArray<int>(7, Allocator.TempJob),
                    existsMask      = new NativeArray<int>(7, Allocator.TempJob),
                    voxelSize       = voxelSize,
                    enabled         = 0
                };
            }

            // 활성 경로
            DensitySnapshot[] neighbors = (ChunkGhostDataManager.Instance != null)
                ? ChunkGhostDataManager.Instance.QueryDensityNeighbors(chunkPos)
                : new DensitySnapshot[6];

            // 필요 총 크기 계산 (self + 존재하는 neighbor들)
            int selfSize = selfDensity.Length;
            long totalSize = selfSize;
            var slotSizes = new int[7];
            slotSizes[0] = selfSize;
            for (int f = 0; f < 6; f++)
            {
                if (neighbors[f].exists && neighbors[f].densityCache != null)
                {
                    slotSizes[f + 1] = neighbors[f].densityCache.Length;
                    totalSize += neighbors[f].densityCache.Length;
                }
                // 존재하지 않는 neighbor는 size=0 (buffer 공간 할당 안 함)
            }

            var densityBuffer  = new NativeArray<float>((int)totalSize, Allocator.TempJob);
            var offsetTable    = new NativeArray<int>(8, Allocator.TempJob);
            var dcBasePosTable = new NativeArray<float3>(7, Allocator.TempJob);
            var dcNTable       = new NativeArray<int>(7, Allocator.TempJob);
            var existsMask     = new NativeArray<int>(7, Allocator.TempJob);

            // slot 0 = self
            int offset = 0;
            offsetTable[0] = 0;
            NativeArray<float>.Copy(selfDensity, 0, densityBuffer, 0, selfSize);
            dcBasePosTable[0] = new float3(selfDcBasePos.x, selfDcBasePos.y, selfDcBasePos.z);
            dcNTable[0]       = selfDcN;
            existsMask[0]     = 1;
            offset += selfSize;

            // slots 1~6 = neighbors (+X, -X, +Y, -Y, +Z, -Z)
            for (int f = 0; f < 6; f++)
            {
                int slotIdx = f + 1;
                offsetTable[slotIdx] = offset;
                if (neighbors[f].exists && neighbors[f].densityCache != null)
                {
                    NativeArray<float>.Copy(neighbors[f].densityCache, 0,
                                             densityBuffer, offset, slotSizes[slotIdx]);
                    dcBasePosTable[slotIdx] = new float3(
                        neighbors[f].dcBasePos.x,
                        neighbors[f].dcBasePos.y,
                        neighbors[f].dcBasePos.z);
                    dcNTable[slotIdx]   = neighbors[f].dcN;
                    existsMask[slotIdx] = 1;
                    offset += slotSizes[slotIdx];
                }
                else
                {
                    dcBasePosTable[slotIdx] = float3.zero;
                    dcNTable[slotIdx]       = 0;
                    existsMask[slotIdx]     = 0;
                }
            }
            offsetTable[7] = offset;  // end sentinel

            return new PackedNeighborDensity {
                densityBuffer   = densityBuffer,
                offsetTable     = offsetTable,
                dcBasePosTable  = dcBasePosTable,
                dcNTable        = dcNTable,
                existsMask      = existsMask,
                voxelSize       = voxelSize,
                enabled         = 1
            };
        }

        /// <summary>[F-4] 모든 NativeArray Dispose</summary>
        private static void DisposeF4NeighborPack(PackedNeighborDensity pack)
        {
            if (pack.densityBuffer.IsCreated)  pack.densityBuffer.Dispose();
            if (pack.offsetTable.IsCreated)    pack.offsetTable.Dispose();
            if (pack.dcBasePosTable.IsCreated) pack.dcBasePosTable.Dispose();
            if (pack.dcNTable.IsCreated)       pack.dcNTable.Dispose();
            if (pack.existsMask.IsCreated)     pack.existsMask.Dispose();
        }

        /// <summary>
        /// [F-4] Async 경로용 Persistent allocator 버전.
        /// BakeNormalMapV5_Async가 JobHandle 체인으로 4프레임 이상 생존하므로 Persistent 필수.
        /// PendingBake.Dispose 시점에 DisposeF4NeighborPack이 해제.
        /// </summary>
        private static PackedNeighborDensity PrepareF4NeighborPackPersistent(
            Vector3Int chunkPos, float[] selfDensity, Vector3 selfDcBasePos,
            int selfDcN, float voxelSize, bool f4Enabled)
        {
            if (!f4Enabled)
            {
                return new PackedNeighborDensity {
                    densityBuffer   = new NativeArray<float>(1, Allocator.Persistent),
                    offsetTable     = new NativeArray<int>(8, Allocator.Persistent),
                    dcBasePosTable  = new NativeArray<float3>(7, Allocator.Persistent),
                    dcNTable        = new NativeArray<int>(7, Allocator.Persistent),
                    existsMask      = new NativeArray<int>(7, Allocator.Persistent),
                    voxelSize       = voxelSize,
                    enabled         = 0
                };
            }

            DensitySnapshot[] neighbors = (ChunkGhostDataManager.Instance != null)
                ? ChunkGhostDataManager.Instance.QueryDensityNeighbors(chunkPos)
                : new DensitySnapshot[6];

            int selfSize = selfDensity.Length;
            long totalSize = selfSize;
            var slotSizes = new int[7];
            slotSizes[0] = selfSize;
            for (int f = 0; f < 6; f++)
            {
                if (neighbors[f].exists && neighbors[f].densityCache != null)
                {
                    slotSizes[f + 1] = neighbors[f].densityCache.Length;
                    totalSize += neighbors[f].densityCache.Length;
                }
            }

            var densityBuffer  = new NativeArray<float>((int)totalSize, Allocator.Persistent);
            var offsetTable    = new NativeArray<int>(8, Allocator.Persistent);
            var dcBasePosTable = new NativeArray<float3>(7, Allocator.Persistent);
            var dcNTable       = new NativeArray<int>(7, Allocator.Persistent);
            var existsMask     = new NativeArray<int>(7, Allocator.Persistent);

            int offset = 0;
            offsetTable[0] = 0;
            NativeArray<float>.Copy(selfDensity, 0, densityBuffer, 0, selfSize);
            dcBasePosTable[0] = new float3(selfDcBasePos.x, selfDcBasePos.y, selfDcBasePos.z);
            dcNTable[0]       = selfDcN;
            existsMask[0]     = 1;
            offset += selfSize;

            for (int f = 0; f < 6; f++)
            {
                int slotIdx = f + 1;
                offsetTable[slotIdx] = offset;
                if (neighbors[f].exists && neighbors[f].densityCache != null)
                {
                    NativeArray<float>.Copy(neighbors[f].densityCache, 0,
                                             densityBuffer, offset, slotSizes[slotIdx]);
                    dcBasePosTable[slotIdx] = new float3(
                        neighbors[f].dcBasePos.x,
                        neighbors[f].dcBasePos.y,
                        neighbors[f].dcBasePos.z);
                    dcNTable[slotIdx]   = neighbors[f].dcN;
                    existsMask[slotIdx] = 1;
                    offset += slotSizes[slotIdx];
                }
                else
                {
                    dcBasePosTable[slotIdx] = float3.zero;
                    dcNTable[slotIdx]       = 0;
                    existsMask[slotIdx]     = 0;
                }
            }
            offsetTable[7] = offset;

            return new PackedNeighborDensity {
                densityBuffer   = densityBuffer,
                offsetTable     = offsetTable,
                dcBasePosTable  = dcBasePosTable,
                dcNTable        = dcNTable,
                existsMask      = existsMask,
                voxelSize       = voxelSize,
                enabled         = 1
            };
        }

        /// <summary>
        /// [F-4 확장 시그니처] chunkPos 포함 — ChunkGhostDataManager 조회로 neighbor density 참조.
        /// </summary>
        public Texture2D[] BakeNormalMap(Mesh mesh, MeshRenderer targetRenderer,
                                          float[] densities, Vector3 dcBasePos,
                                          int dcN, float voxelSize,
                                          Vector3Int chunkPos, bool f4Enabled)
        {
            if (mesh == null) return BakeFlat3(mesh, targetRenderer);
            if (densities == null || densities.Length == 0)
                return BakeFlat3(mesh, targetRenderer);

            // [P3-NB-A] Async 경로 (v5 기반, enableAsyncBake=ON 시)
            if (enableBakerV5 && enableAsyncBake)
                return BakeNormalMapV5_Async(mesh, targetRenderer, densities, dcBasePos,
                                              dcN, voxelSize, chunkPos, f4Enabled);

            if (enableBakerV5)
                return BakeNormalMapV5(mesh, targetRenderer, densities, dcBasePos,
                                        dcN, voxelSize, chunkPos, f4Enabled);

            // ── v4 원본 경로 (아래 코드는 원본과 기본 동일, F-4 필드 주입만 추가) ──

            float startT    = Time.realtimeSinceStartup;
            // [tiling 자동 동기화] 재질의 _Tiling 값 읽기
            float effectiveTiling = tiling;
            if (autoSyncTiling && targetRenderer != null && targetRenderer.sharedMaterial != null)
            {
                if (targetRenderer.sharedMaterial.HasFloat("_Tiling"))
                    effectiveTiling = targetRenderer.sharedMaterial.GetFloat("_Tiling");
            }
            var   positions = mesh.vertices;
            var   normals   = mesh.normals;
            int   vertCount = positions.Length;
            int   texW = normalMapResolution, texH = normalMapResolution;
            float step = voxelSize * subVoxelFactor;

            var nPos  = new NativeArray<float3>(vertCount, Allocator.TempJob);
            var nNorm = new NativeArray<float3>(vertCount, Allocator.TempJob);
            for (int i = 0; i < vertCount; i++) {
                nPos[i]  = new float3(positions[i].x, positions[i].y, positions[i].z);
                nNorm[i] = new float3(normals[i].x,   normals[i].y,   normals[i].z);
            }
            var nDens  = new NativeArray<float>(densities, Allocator.TempJob);
            var nPixX  = new NativeArray<Color32>(texW*texH, Allocator.TempJob);
            var nPixY  = new NativeArray<Color32>(texW*texH, Allocator.TempJob);
            var nPixZ  = new NativeArray<Color32>(texW*texH, Allocator.TempJob);
            // 배경: (128,128,255) = 편차 없음
            var flat = new Color32(128, 128, 255, 255);
            for (int i = 0; i < texW*texH; i++) { nPixX[i]=flat; nPixY[i]=flat; nPixZ[i]=flat; }

            // [F-4] Neighbor density 패키지 준비 (sync 경로 — Complete 후 Dispose)
            var f4Pack = PrepareF4NeighborPack(chunkPos, densities, dcBasePos, dcN, voxelSize, f4Enabled);

            var job = new NormalBakeJobV4 {
                vertexPositions = nPos,   vertexNormals = nNorm, densities = nDens,
                normalMapX = nPixX, normalMapY = nPixY, normalMapZ = nPixZ,
                texWidth = texW,    texHeight = texH,
                dcN = dcN, dcBasePos = new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize = voxelSize, sampleStep = step, tiling = effectiveTiling,
                // [F-4] ghost density 주입
                f4_densityBuffer  = f4Pack.densityBuffer,
                f4_offsetTable    = f4Pack.offsetTable,
                f4_dcBasePosTable = f4Pack.dcBasePosTable,
                f4_dcNTable       = f4Pack.dcNTable,
                f4_existsMask     = f4Pack.existsMask,
                f4_enabled        = f4Pack.enabled,
            };
            job.Schedule().Complete();

            var textures = new Texture2D[3];
            string[] suffixes = {"X", "Y", "Z"};
            NativeArray<Color32>[] pixes = {nPixX, nPixY, nPixZ};
            for (int ch = 0; ch < 3; ch++) {
                textures[ch] = new Texture2D(texW, texH, TextureFormat.RGBA32, true) {
                    name = $"DC_NormalMap{suffixes[ch]}_{mesh.name}",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                };
                textures[ch].SetPixelData(pixes[ch], 0);  // NativeArray 직접 (GC 0)
                textures[ch].Apply(true);
            }

            nPos.Dispose(); nNorm.Dispose(); nDens.Dispose();
            nPixX.Dispose(); nPixY.Dispose(); nPixZ.Dispose();
            DisposeF4NeighborPack(f4Pack);  // [F-4]

            if (autoAssignToShader && targetRenderer != null) {
                DestroyOldDCNormalTextures(targetRenderer); // [FIX-TEX]
                var mpb = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(mpb);
                mpb.SetTexture("_DCNormalMap_X", textures[0]);
                mpb.SetTexture("_DCNormalMap_Y", textures[1]);
                mpb.SetTexture("_DCNormalMap_Z", textures[2]);
                targetRenderer.SetPropertyBlock(mpb);
                #if CAVE_VERBOSE
                Debug.Log("[CaveNormalBaker v4] 3채널 DC 노말맵 MPB 설정 완료");
                #endif
            }

            float ms = (Time.realtimeSinceStartup - startT) * 1000f;
            lastBakeVertexCount = vertCount; lastBakeTimeMs = ms;
            #if CAVE_VERBOSE
            Debug.Log($"[CaveNormalBaker v4] 완료: {texW}×{texH}×3ch, vert={vertCount}, {ms:F1}ms, F4={f4Enabled}");
            #endif
            MarkChunkBaked(chunkPos);  // [F-4-RB] bake 완료 추적
            return textures;
        }

        // ── v5 헬퍼 ──
        private int GetAdaptiveResolution(float voxelSize)
        {
            if (voxelSize <= 0.16f) return normalMapResolution;
            if (voxelSize <= 0.25f) return Mathf.Min(normalMapResolution, 256);
            return Mathf.Min(normalMapResolution, 128);
        }
        private int GetDilationPasses(float voxelSize)
        {
            if (voxelSize <= 0.16f) return 5;
            if (voxelSize <= 0.25f) return 3;
            return 2;
        }

        // ── v5 베이킹 (enableBakerV5=ON 시) ──
        private Texture2D[] BakeNormalMapV5(Mesh mesh, MeshRenderer targetRenderer,
                                             float[] densities, Vector3 dcBasePos,
                                             int dcN, float voxelSize,
                                             Vector3Int chunkPos, bool f4Enabled)
        {
            float startT = Time.realtimeSinceStartup;
            float effectiveTiling = tiling;
            if (autoSyncTiling && targetRenderer != null && targetRenderer.sharedMaterial != null)
                if (targetRenderer.sharedMaterial.HasFloat("_Tiling"))
                    effectiveTiling = targetRenderer.sharedMaterial.GetFloat("_Tiling");

            var positions = mesh.vertices;
            var normals   = mesh.normals;
            int vertCount = positions.Length;
            int texRes    = GetAdaptiveResolution(voxelSize);
            int texTotal  = texRes * texRes;
            float step    = voxelSize * subVoxelFactor;
            int dilPasses = GetDilationPasses(voxelSize);

            var nPos  = new NativeArray<float3>(vertCount, Allocator.TempJob);
            var nNorm = new NativeArray<float3>(vertCount, Allocator.TempJob);
            for (int i = 0; i < vertCount; i++) {
                nPos[i]  = new float3(positions[i].x, positions[i].y, positions[i].z);
                nNorm[i] = new float3(normals[i].x,   normals[i].y,   normals[i].z);
            }
            var nDens = new NativeArray<float>(densities, Allocator.TempJob);

            int accumLen = texTotal * 3;
            var aPX = new NativeArray<int>(accumLen, Allocator.TempJob);
            var aNX = new NativeArray<int>(accumLen, Allocator.TempJob);
            var aPY = new NativeArray<int>(accumLen, Allocator.TempJob);
            var aNY = new NativeArray<int>(accumLen, Allocator.TempJob);
            var aPZ = new NativeArray<int>(accumLen, Allocator.TempJob);
            var aNZ = new NativeArray<int>(accumLen, Allocator.TempJob);

            // [F-4] Neighbor density pack
            var f4Pack = PrepareF4NeighborPack(chunkPos, densities, dcBasePos, dcN, voxelSize, f4Enabled);

            new NormalBakeJobV5 {
                vertexPositions=nPos, vertexNormals=nNorm, densities=nDens,
                accumPosX=aPX, accumNegX=aNX, accumPosY=aPY, accumNegY=aNY,
                accumPosZ=aPZ, accumNegZ=aNZ,
                texSize=texRes, dcN=dcN,
                dcBasePos=new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize=voxelSize, sampleStep=step,
                tiling=effectiveTiling, bandHeight=bandHeight,
                // [F-4]
                f4_densityBuffer  = f4Pack.densityBuffer,
                f4_offsetTable    = f4Pack.offsetTable,
                f4_dcBasePosTable = f4Pack.dcBasePosTable,
                f4_dcNTable       = f4Pack.dcNTable,
                f4_existsMask     = f4Pack.existsMask,
                f4_enabled        = f4Pack.enabled,
            }.Schedule(vertCount, 64).Complete();

            nPos.Dispose(); nNorm.Dispose(); nDens.Dispose();
            DisposeF4NeighborPack(f4Pack);  // [F-4] Complete 후 release

            // ═══════════════════════════════════════════════════════════════════════════
            // [Gate 4-C / Halo Bake] HaloAccumulator Pass
            //   V5 Job 완료 직후, FinalizeAccumJob 호출 전에 실행.
            //   Neighbor chunk의 boundary vertex를 이 chunk의 accum 배열에 추가 기여.
            //   → 경계 texel이 "이 chunk vertex + neighbor chunk vertex" 양쪽 기여로 평균
            //   → Rendering 시 양 chunk가 같은 texel 값 sample → truly seamless texture
            //
            //   알고리즘 (WriteAccum과 일치):
            //     1. 6 neighbor의 Ghost Cache 조회
            //     2. neighbor의 반대 face vertex+normal 목록 (facesVerticesWorld + facesNormalsWorld)
            //     3. 각 vertex에 대해 Execute와 동일 수식:
            //        - wp = worldPos (이미 world)
            //        - vN = storedNormal (neighbor에서 가져온)
            //        - dN ≈ vN (neighbor density는 없으므로 vN으로 근사 — SoftClamp(dN-vN) ≈ 0)
            //        - 3채널 WriteAccum (자기 chunk의 accum에 기여)
            //   
            //   토글: CaveTerrainConfig.enableHaloAwareBake
            //   규칙 #6: OFF 시 skip (V5 Job 결과 변화 없음 = bit-identical)
            // ═══════════════════════════════════════════════════════════════════════════
            bool haloEnabled = false;
            if (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null &&
                CaveManager.Instance.chunkManager.terrainConfig != null)
            {
                haloEnabled = CaveManager.Instance.chunkManager.terrainConfig.enableHaloAwareBake;
            }
            if (haloEnabled && ChunkGhostDataManager.Instance != null)
            {
                ApplyHaloAccumulation(
                    chunkPos, aPX, aNX, aPY, aNY, aPZ, aNZ,
                    texRes, effectiveTiling, bandHeight, voxelSize,
                    // [Phase 2] density 정밀 dN 계산용
                    densities, dcBasePos, dcN, step);
            }

            var pixX = new NativeArray<Color32>(texTotal, Allocator.TempJob);
            var pixY = new NativeArray<Color32>(texTotal, Allocator.TempJob);
            var pixZ = new NativeArray<Color32>(texTotal, Allocator.TempJob);
            NativeArray<int>[] pA = {aPX,aPY,aPZ}; NativeArray<int>[] nA = {aNX,aNY,aNZ};
            NativeArray<Color32>[] px = {pixX,pixY,pixZ};
            for (int ch = 0; ch < 3; ch++)
                new FinalizeAccumJob {accumPos=pA[ch], accumNeg=nA[ch], output=px[ch]}
                    .Schedule(texTotal, 256).Complete();
            aPX.Dispose(); aNX.Dispose(); aPY.Dispose(); aNY.Dispose(); aPZ.Dispose(); aNZ.Dispose();

            for (int ch = 0; ch < 3; ch++) {
                var ping = px[ch];
                var pong = new NativeArray<Color32>(texTotal, Allocator.TempJob);
                for (int p = 0; p < dilPasses; p++) {
                    new DilationJob {input=ping, output=pong, texSize=texRes}
                        .Schedule(texTotal, 256).Complete();
                    var tmp=ping; ping=pong; pong=tmp;
                }
                px[ch]=ping; if(pong.IsCreated) pong.Dispose();
            }

            var textures = new Texture2D[3];
            string[] suf = {"X","Y","Z"};
            for (int ch = 0; ch < 3; ch++) {
                textures[ch] = new Texture2D(texRes, texRes, TextureFormat.RGBA32, true) {
                    name=$"DC_NormalV5_{suf[ch]}_{mesh.name}",
                    wrapMode=TextureWrapMode.Repeat, filterMode=FilterMode.Trilinear };
                textures[ch].SetPixelData(px[ch], 0);  // NativeArray 직접 (GC 0)
                textures[ch].Apply(true);
                px[ch].Dispose();
            }
            if (autoAssignToShader && targetRenderer != null) {
                DestroyOldDCNormalTextures(targetRenderer); // [FIX-TEX]
                var mpb = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(mpb);
                mpb.SetTexture("_DCNormalMap_X", textures[0]);
                mpb.SetTexture("_DCNormalMap_Y", textures[1]);
                mpb.SetTexture("_DCNormalMap_Z", textures[2]);
                targetRenderer.SetPropertyBlock(mpb);
            }
            float ms = (Time.realtimeSinceStartup - startT) * 1000f;
            lastBakeVertexCount = vertCount; lastBakeTimeMs = ms; lastTexResolution = texRes;
            #if CAVE_VERBOSE
            Debug.Log($"[CaveNormalBaker v5] {texRes}²×3ch, vert={vertCount}, dil={dilPasses}p, {ms:F1}ms");
            #endif
            MarkChunkBaked(chunkPos);  // [F-4-RB] bake 완료 추적
            return textures;
        }

        // =====================================================================
        // [P3-NB-A] BakeNormalMapV5_Async — JobHandle 체인, 메인 블록 없음
        //   NormalBakeJobV5 알고리즘 불변, Schedule 방식만 변경 (규칙 #11 준수)
        //   Persistent Allocator + 3중 Dispose 방어 (규칙 #12 준수)
        // =====================================================================
        private Texture2D[] BakeNormalMapV5_Async(Mesh mesh, MeshRenderer targetRenderer,
                                                   float[] densities, Vector3 dcBasePos,
                                                   int dcN, float voxelSize,
                                                   Vector3Int chunkPos, bool f4Enabled)
        {
            // [위험 ② 방어] Coarse 청크는 수초 후 Destroy되므로 bake 건너뜀
            if (targetRenderer != null && targetRenderer.gameObject != null
                && targetRenderer.gameObject.name != null
                && targetRenderer.gameObject.name.StartsWith("Coarse_"))
            {
                return null;  // Coarse는 normal map 없이 flat normal만 (수초 후 Fine으로 교체)
            }

            float effectiveTiling = tiling;
            if (autoSyncTiling && targetRenderer != null && targetRenderer.sharedMaterial != null)
                if (targetRenderer.sharedMaterial.HasFloat("_Tiling"))
                    effectiveTiling = targetRenderer.sharedMaterial.GetFloat("_Tiling");

            var positions = mesh.vertices;
            var normals   = mesh.normals;
            int vertCount = positions.Length;
            int texRes    = GetAdaptiveResolution(voxelSize);
            int texTotal  = texRes * texRes;
            float step    = voxelSize * subVoxelFactor;
            int dilPasses = GetDilationPasses(voxelSize);

            // Phase A: Persistent NativeArray (4프레임 이상 생존)
            var nPos  = new NativeArray<float3>(vertCount, Allocator.Persistent);
            var nNorm = new NativeArray<float3>(vertCount, Allocator.Persistent);
            for (int i = 0; i < vertCount; i++) {
                nPos[i]  = new float3(positions[i].x, positions[i].y, positions[i].z);
                nNorm[i] = new float3(normals[i].x,   normals[i].y,   normals[i].z);
            }
            var nDens = new NativeArray<float>(densities, Allocator.Persistent);

            int accumLen = texTotal * 3;
            var aPX = new NativeArray<int>(accumLen, Allocator.Persistent);
            var aNX = new NativeArray<int>(accumLen, Allocator.Persistent);
            var aPY = new NativeArray<int>(accumLen, Allocator.Persistent);
            var aNY = new NativeArray<int>(accumLen, Allocator.Persistent);
            var aPZ = new NativeArray<int>(accumLen, Allocator.Persistent);
            var aNZ = new NativeArray<int>(accumLen, Allocator.Persistent);

            // [F-4] Persistent allocator — PendingBake.Dispose에서 release
            var f4Pack = PrepareF4NeighborPackPersistent(
                chunkPos, densities, dcBasePos, dcN, voxelSize, f4Enabled);

            // Phase B: NormalBakeJobV5 스케줄 (Complete 없음)
            JobHandle h1 = new NormalBakeJobV5 {
                vertexPositions=nPos, vertexNormals=nNorm, densities=nDens,
                accumPosX=aPX, accumNegX=aNX, accumPosY=aPY, accumNegY=aNY,
                accumPosZ=aPZ, accumNegZ=aNZ,
                texSize=texRes, dcN=dcN,
                dcBasePos=new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize=voxelSize, sampleStep=step,
                tiling=effectiveTiling, bandHeight=bandHeight,
                // [F-4]
                f4_densityBuffer  = f4Pack.densityBuffer,
                f4_offsetTable    = f4Pack.offsetTable,
                f4_dcBasePosTable = f4Pack.dcBasePosTable,
                f4_dcNTable       = f4Pack.dcNTable,
                f4_existsMask     = f4Pack.existsMask,
                f4_enabled        = f4Pack.enabled,
            }.Schedule(vertCount, 64);

            // Phase D용 pixel 버퍼 (ping-pong: A/B per channel)
            var pixX_A = new NativeArray<Color32>(texTotal, Allocator.Persistent);
            var pixX_B = new NativeArray<Color32>(texTotal, Allocator.Persistent);
            var pixY_A = new NativeArray<Color32>(texTotal, Allocator.Persistent);
            var pixY_B = new NativeArray<Color32>(texTotal, Allocator.Persistent);
            var pixZ_A = new NativeArray<Color32>(texTotal, Allocator.Persistent);
            var pixZ_B = new NativeArray<Color32>(texTotal, Allocator.Persistent);

            // Phase D: FinalizeAccumJob × 3 — h1 의존, 채널별 독립 병렬
            JobHandle h2x = new FinalizeAccumJob { accumPos=aPX, accumNeg=aNX, output=pixX_A }
                .Schedule(texTotal, 256, h1);
            JobHandle h2y = new FinalizeAccumJob { accumPos=aPY, accumNeg=aNY, output=pixY_A }
                .Schedule(texTotal, 256, h1);
            JobHandle h2z = new FinalizeAccumJob { accumPos=aPZ, accumNeg=aNZ, output=pixZ_A }
                .Schedule(texTotal, 256, h1);

            // Phase E: Dilation 체인 (채널별 ping-pong)
            JobHandle dilX = h2x;
            NativeArray<Color32> curInX = pixX_A, curOutX = pixX_B;
            for (int p = 0; p < dilPasses; p++) {
                dilX = new DilationJob { input=curInX, output=curOutX, texSize=texRes }
                    .Schedule(texTotal, 256, dilX);
                var t = curInX; curInX = curOutX; curOutX = t;
            }
            JobHandle dilY = h2y;
            NativeArray<Color32> curInY = pixY_A, curOutY = pixY_B;
            for (int p = 0; p < dilPasses; p++) {
                dilY = new DilationJob { input=curInY, output=curOutY, texSize=texRes }
                    .Schedule(texTotal, 256, dilY);
                var t = curInY; curInY = curOutY; curOutY = t;
            }
            JobHandle dilZ = h2z;
            NativeArray<Color32> curInZ = pixZ_A, curOutZ = pixZ_B;
            for (int p = 0; p < dilPasses; p++) {
                dilZ = new DilationJob { input=curInZ, output=curOutZ, texSize=texRes }
                    .Schedule(texTotal, 256, dilZ);
                var t = curInZ; curInZ = curOutZ; curOutZ = t;
            }

            JobHandle finalH = JobHandle.CombineDependencies(dilX, dilY, dilZ);

            // 최종 슬롯 결정: dilPasses 홀수 → B, 짝수 → A (0 포함)
            int finalSlot = dilPasses % 2;

            // pending에 등록 (완료는 Update에서 폴링)
            _pendingBakes.Add(new PendingBake {
                finalHandle = finalH,
                renderer = targetRenderer,
                expectedMesh = mesh,                 // [위험 ① 방어]
                capturedMeshName = mesh.name,
                texRes = texRes,
                nPos = nPos, nNorm = nNorm, nDens = nDens,
                aPX = aPX, aNX = aNX, aPY = aPY, aNY = aNY, aPZ = aPZ, aNZ = aNZ,
                pixX_A = pixX_A, pixX_B = pixX_B,
                pixY_A = pixY_A, pixY_B = pixY_B,
                pixZ_A = pixZ_A, pixZ_B = pixZ_B,
                finalSlotX = finalSlot, finalSlotY = finalSlot, finalSlotZ = finalSlot,
                // [F-4]
                f4Pack = f4Pack,
                f4Enabled = f4Pack.enabled,
                // [F-4-RB]
                chunkPos = chunkPos,
                chunkPosValid = f4Enabled ? 1 : 0,
            });

            lastBakeVertexCount = vertCount;
            lastTexResolution = texRes;
            return null;  // 텍스처는 Update에서 나중에 생성
        }

        // ── flat fallback (density 없을 때, 3채널 모두 flat) ────────────
        public Texture2D[] BakeFlat3(Mesh mesh, MeshRenderer targetRenderer)
        {
            int texW = normalMapResolution, texH = normalMapResolution;
            var textures = new Texture2D[3];
            for (int ch = 0; ch < 3; ch++) {
                textures[ch] = new Texture2D(texW, texH, TextureFormat.RGBA32, true) {
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                var px = new Color32[texW*texH];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(128,128,255,255);
                textures[ch].SetPixelData(px, 0);
                textures[ch].Apply(true);
            }
            if (autoAssignToShader && targetRenderer != null) {
                DestroyOldDCNormalTextures(targetRenderer); // [FIX-TEX]
                var mpb = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(mpb);
                string[] slots = {"_DCNormalMap_X","_DCNormalMap_Y","_DCNormalMap_Z"};
                for (int ch = 0; ch < 3; ch++) mpb.SetTexture(slots[ch], textures[ch]);
                targetRenderer.SetPropertyBlock(mpb);
            }
            return textures;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Gate 4-C / Halo Bake Phase 2] ApplyHaloAccumulation
        //   Phase 1 → Phase 2 업그레이드:
        //     Phase 1: dN ≈ vN 근사 (pu=pv=0, flat 기여만)
        //     Phase 2: 자기 density + neighbor density(TryGetDensity) 활용 →
        //              SubVoxelNormal(wp) 정밀 계산 → 진짜 dN → 진짜 pu/pv
        //              → detail 보존된 seamless 기여
        //
        //   Density 샘플링 전략 (SampleDensityHalo):
        //     1. wp를 자기 chunk의 DC grid 좌표로 변환
        //     2. 자기 grid 범위 내면 자기 density 사용
        //     3. 범위 밖이면 해당 face의 neighbor density 사용 (TryGetDensity 조회)
        //     4. neighbor 없으면 자기 density clamp 사용 (fallback)
        //   
        //   NormalBakeJobV5의 F-4 SampleDensity 로직과 동일 철학,
        //   다만 managed 경로라 TryGetDensity로 on-demand 조회 (Burst Job의 pack 불필요).
        //
        //   성능: chunk당 ~1000~5000 halo vertex × 6 samples per SubVoxelNormal
        //         = 6,000~30,000 density sample / chunk
        //         trilinear 1 sample ~ 100ns → chunk당 0.6~3ms 추가
        // ═══════════════════════════════════════════════════════════════════════════════
        private void ApplyHaloAccumulation(
            Vector3Int chunkPos,
            NativeArray<int> aPX, NativeArray<int> aNX,
            NativeArray<int> aPY, NativeArray<int> aNY,
            NativeArray<int> aPZ, NativeArray<int> aNZ,
            int texSize, float tiling, float bandHeight, float voxelSize,
            // [Phase 2] density 파라미터
            float[] selfDensities, Vector3 selfDcBasePos, int selfDcN, float sampleStep)
        {
            var offsets = new Vector3Int[] {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };

            // [Phase 2] 6 neighbor density snapshot 미리 조회 (반복 lookup 회피)
            var neighborDensities = new DensitySnapshot[6];
            int neighborDensityCount = 0;
            for (int f = 0; f < 6; f++)
            {
                Vector3Int np = chunkPos + offsets[f];
                if (ChunkGhostDataManager.Instance.TryGetDensity(np, out var snap) && snap.exists)
                {
                    neighborDensities[f] = snap;
                    neighborDensityCount++;
                }
            }

            int totalHalo = 0;
            int diag_phase2Used = 0;  // 진짜 dN 계산에 neighbor density 사용된 vertex

            for (int face = 0; face < 6; face++)
            {
                Vector3Int neighborPos = chunkPos + offsets[face];
                if (!ChunkGhostDataManager.Instance.TryGetChunkGhostData(neighborPos, out var nData)) continue;
                if (!nData.hasBoundaryVertices) continue;

                int neighborFace = face ^ 1;
                var nVerts = nData.facesVerticesWorld[neighborFace];
                var nNorms = nData.facesNormalsWorld[neighborFace];
                if (nVerts == null || nVerts.Count == 0) continue;
                if (nNorms == null || nNorms.Count != nVerts.Count) continue;

                for (int k = 0; k < nVerts.Count; k++)
                {
                    Vector3 wpVec = nVerts[k];
                    Vector3 vNVec = nNorms[k];
                    float vL = vNVec.magnitude;
                    if (vL < 0.001f) continue;
                    vNVec /= vL;

                    // ═════════════════════════════════════════════════════════════════════
                    // [Phase 2] 진짜 dN 계산 — Halo-aware SubVoxelNormal
                    //   SampleDensityHalo(wp ± sampleStep)을 6번 호출 → gradient 계산
                    //   자기 grid 범위 내이면 자기 density 사용,
                    //   범위 밖이면 해당 face의 neighbor density 사용 (trilinear)
                    //   neighbor 없으면 자기 density clamp fallback
                    // ═════════════════════════════════════════════════════════════════════
                    float s = sampleStep;
                    float dxp = SampleDensityHalo(wpVec.x + s, wpVec.y,     wpVec.z,     selfDensities, selfDcBasePos, selfDcN, voxelSize, neighborDensities);
                    float dxn = SampleDensityHalo(wpVec.x - s, wpVec.y,     wpVec.z,     selfDensities, selfDcBasePos, selfDcN, voxelSize, neighborDensities);
                    float dyp = SampleDensityHalo(wpVec.x,     wpVec.y + s, wpVec.z,     selfDensities, selfDcBasePos, selfDcN, voxelSize, neighborDensities);
                    float dyn = SampleDensityHalo(wpVec.x,     wpVec.y - s, wpVec.z,     selfDensities, selfDcBasePos, selfDcN, voxelSize, neighborDensities);
                    float dzp = SampleDensityHalo(wpVec.x,     wpVec.y,     wpVec.z + s, selfDensities, selfDcBasePos, selfDcN, voxelSize, neighborDensities);
                    float dzn = SampleDensityHalo(wpVec.x,     wpVec.y,     wpVec.z - s, selfDensities, selfDcBasePos, selfDcN, voxelSize, neighborDensities);
                    Vector3 g = new Vector3(dxp - dxn, dyp - dyn, dzp - dzn);
                    float gLen = g.magnitude;
                    Vector3 dN;
                    bool hasDN = false;
                    if (gLen > 1e-6f)
                    {
                        dN = -g / gLen;   // Execute와 동일 부호 규약
                        hasDN = true;
                        diag_phase2Used++;
                    }
                    else
                    {
                        dN = vNVec;       // fallback — Phase 1과 동일 (flat 기여)
                    }

                    Vector3 absN = new Vector3(Mathf.Abs(vNVec.x), Mathf.Abs(vNVec.y), Mathf.Abs(vNVec.z));
                    float sum = absN.x + absN.y + absN.z;
                    if (sum < 0.001f) continue;

                    float yBand = Mathf.Floor(wpVec.y / bandHeight);
                    float2 offXZ = Hash2DManaged(yBand) * 0.4f;
                    float2 offZY = Hash2DManaged(yBand + 100f) * 0.4f;
                    float2 offXY = Hash2DManaged(yBand + 200f) * 0.4f;

                    // [Phase 2] Execute L424-426과 동일 수식
                    //   Y plane: pu = SoftClamp(dN.x - vN.x), pv = SoftClamp(dN.z - vN.z)
                    //   X plane: pu = SoftClamp(dN.z - vN.z), pv = SoftClamp(dN.y - vN.y)
                    //   Z plane: pu = SoftClamp(dN.x - vN.x), pv = SoftClamp(dN.y - vN.y)
                    float puY = SoftClampManaged(dN.x - vNVec.x);
                    float pvY = SoftClampManaged(dN.z - vNVec.z);
                    float puX = SoftClampManaged(dN.z - vNVec.z);
                    float pvX = SoftClampManaged(dN.y - vNVec.y);
                    float puZ = SoftClampManaged(dN.x - vNVec.x);
                    float pvZ = SoftClampManaged(dN.y - vNVec.y);

                    WriteAccumManaged(aPY, aNY, new float2(wpVec.x, wpVec.z) * tiling + offXZ, puY, pvY, absN.y / sum, vNVec.y, texSize);
                    WriteAccumManaged(aPX, aNX, new float2(wpVec.z, wpVec.y) * tiling + offZY, puX, pvX, absN.x / sum, vNVec.x, texSize);
                    WriteAccumManaged(aPZ, aNZ, new float2(wpVec.x, wpVec.y) * tiling + offXY, puZ, pvZ, absN.z / sum, vNVec.z, texSize);
                    totalHalo++;
                }
            }

            if (totalHalo > 0)
                Debug.Log($"[Halo Bake P2] {chunkPos}: {totalHalo} neighbor verts contributed, " +
                          $"{diag_phase2Used} with real dN ({(totalHalo > 0 ? (100f * diag_phase2Used / totalHalo):0):F1}%), " +
                          $"neighborDensityCount={neighborDensityCount}/6");
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Halo Bake Phase 2] SampleDensityHalo
        //   World position wp의 density trilinear sample.
        //   자기 grid 범위 내이면 자기 density, 범위 밖이면 neighbor density 조회.
        //   NormalBakeJobV5의 SampleDensity F-4 경로와 동일 철학, managed 버전.
        // ═══════════════════════════════════════════════════════════════════════════════
        private static float SampleDensityHalo(float wx, float wy, float wz,
                                                float[] selfDensities, Vector3 selfDcBasePos, int selfDcN, float voxelSize,
                                                DensitySnapshot[] neighborDensities)
        {
            // 자기 grid 좌표
            float fxG = (wx - selfDcBasePos.x) / voxelSize;
            float fyG = (wy - selfDcBasePos.y) / voxelSize;
            float fzG = (wz - selfDcBasePos.z) / voxelSize;
            bool inSelf = (fxG >= 0f) & (fxG <= selfDcN - 2) &
                          (fyG >= 0f) & (fyG <= selfDcN - 2) &
                          (fzG >= 0f) & (fzG <= selfDcN - 2);
            if (inSelf)
                return TrilinearManaged(selfDensities, selfDcN, fxG, fyG, fzG);

            // 범위 밖 — 최대 violation 방향 판단 (F-4 경로와 동일)
            int face = -1;
            float maxV = 0f, vCur;
            vCur = -fxG;                if (vCur > maxV) { maxV = vCur; face = 1; }
            vCur = fxG - (selfDcN - 2); if (vCur > maxV) { maxV = vCur; face = 0; }
            vCur = -fyG;                if (vCur > maxV) { maxV = vCur; face = 3; }
            vCur = fyG - (selfDcN - 2); if (vCur > maxV) { maxV = vCur; face = 2; }
            vCur = -fzG;                if (vCur > maxV) { maxV = vCur; face = 5; }
            vCur = fzG - (selfDcN - 2); if (vCur > maxV) { maxV = vCur; face = 4; }

            if (face >= 0 && neighborDensities[face].exists)
            {
                var snap = neighborDensities[face];
                // [Approach B] voxelSize 일치 검증 — LOD mismatch chunks 제외
                //   Coarse chunks는 이미 RegisterDensity skip되므로 정상 경로에서는 도달 안 함.
                //   하지만 안전장치로 voxelSize 체크: 1% 이내 일치만 허용.
                //   Mismatch 시 fallback (자기 grid clamp 사용).
                if (Mathf.Abs(snap.voxelSize - voxelSize) / Mathf.Max(voxelSize, 0.001f) > 0.01f)
                {
                    // voxelSize 불일치 (이론상 거의 발생 안 함) → fallback
                }
                else
                {
                    float nfx = (wx - snap.dcBasePos.x) / voxelSize;
                    float nfy = (wy - snap.dcBasePos.y) / voxelSize;
                    float nfz = (wz - snap.dcBasePos.z) / voxelSize;
                    bool inN = (nfx >= 0f) & (nfx <= snap.dcN - 2) &
                               (nfy >= 0f) & (nfy <= snap.dcN - 2) &
                               (nfz >= 0f) & (nfz <= snap.dcN - 2);
                    if (inN)
                        return TrilinearManaged(snap.densityCache, snap.dcN, nfx, nfy, nfz);
                }
            }

            // fallback: 자기 grid clamp
            float cfx = Mathf.Clamp(fxG, 0f, selfDcN - 2);
            float cfy = Mathf.Clamp(fyG, 0f, selfDcN - 2);
            float cfz = Mathf.Clamp(fzG, 0f, selfDcN - 2);
            return TrilinearManaged(selfDensities, selfDcN, cfx, cfy, cfz);
        }

        private static float TrilinearManaged(float[] d, int dcN, float fx, float fy, float fz)
        {
            int x0 = Mathf.Clamp((int)fx, 0, dcN - 2);
            int y0 = Mathf.Clamp((int)fy, 0, dcN - 2);
            int z0 = Mathf.Clamp((int)fz, 0, dcN - 2);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            int n2 = dcN * dcN;
            int b000 = x0 + y0 * dcN + z0 * n2;
            int b100 = b000 + 1;
            int b010 = b000 + dcN;
            int b110 = b010 + 1;
            int b001 = b000 + n2;
            int b101 = b001 + 1;
            int b011 = b001 + dcN;
            int b111 = b011 + 1;
            float c00 = d[b000] * (1 - tx) + d[b100] * tx;
            float c10 = d[b010] * (1 - tx) + d[b110] * tx;
            float c01 = d[b001] * (1 - tx) + d[b101] * tx;
            float c11 = d[b011] * (1 - tx) + d[b111] * tx;
            float c0 = c00 * (1 - ty) + c10 * ty;
            float c1 = c01 * (1 - ty) + c11 * ty;
            return c0 * (1 - tz) + c1 * tz;
        }

        // SoftClamp의 managed 버전 — Job의 SoftClamp과 동일 로직
        private static float SoftClampManaged(float x)
        {
            const float lim = 0.3f;
            float a = x < 0 ? -x : x;
            if (a <= lim) return x;
            float t = Mathf.Clamp01((a - lim) / (1f - lim));
            t = t * t * (3f - 2f * t);
            float clamped = lim + t * (1f - lim);
            return x < 0 ? -clamped : clamped;
        }

        // Execute의 Hash2D와 동일 (managed 버전)
        private static float2 Hash2DManaged(float seed)
        {
            float2 p = new float2(seed, seed * 1.37f);
            p = math.frac(p * new float2(443.897f, 441.423f));
            p += math.dot(p, p + 19.19f);
            return math.frac(new float2(p.x * p.y, p.x + p.y));
        }

        // WriteAccum의 managed 버전 (Interlocked.Add는 NativeArray indexer 통해 안전하게)
        private static unsafe void WriteAccumManaged(
            NativeArray<int> pos, NativeArray<int> neg,
            float2 uv, float pu, float pv, float w, float signAxis, int texSize)
        {
            int px = ((int)(math.frac(uv.x) * texSize) + texSize) % texSize;
            int py = ((int)(math.frac(uv.y) * texSize) + texSize) % texSize;
            int bi = (py * texSize + px) * 3;
            if (bi < 0 || bi + 2 >= pos.Length) return;
            int iPU = (int)(pu * w * 10000f);
            int iPV = (int)(pv * w * 10000f);
            int iW  = (int)(w * 10000f);
            if (iW == 0) return;
            int* t = (signAxis >= 0) ? (int*)pos.GetUnsafePtr() : (int*)neg.GetUnsafePtr();
            System.Threading.Interlocked.Add(ref t[bi],     iPU);
            System.Threading.Interlocked.Add(ref t[bi + 1], iPV);
            System.Threading.Interlocked.Add(ref t[bi + 2], iW);
        }
    }
}
