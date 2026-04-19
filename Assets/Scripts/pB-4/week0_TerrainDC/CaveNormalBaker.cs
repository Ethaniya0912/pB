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

        // ── 밀도 조회 (trilinear) ─────────────────────────────────────────
        private float SampleDensity(float wx, float wy, float wz)
        {
            float fx = (wx - dcBasePos.x) / voxelSize;
            float fy = (wy - dcBasePos.y) / voxelSize;
            float fz = (wz - dcBasePos.z) / voxelSize;
            int x0 = math.clamp((int)fx, 0, dcN-2);
            int y0 = math.clamp((int)fy, 0, dcN-2);
            int z0 = math.clamp((int)fz, 0, dcN-2);
            float tx = fx-x0, ty = fy-y0, tz = fz-z0;
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

        private static float2 Hash2D(float seed)
        {
            float2 p = new float2(seed, seed * 1.37f);
            p = math.frac(p * new float2(443.897f, 441.423f));
            p += math.dot(p, p + 19.19f);
            return math.frac(new float2(p.x * p.y, p.x + p.y));
        }
        private float SampleDensity(float wx, float wy, float wz)
        {
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
        }
        private List<PendingBake> _pendingBakes = new List<PendingBake>();

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
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
        /// </summary>
        public Texture2D[] BakeNormalMap(Mesh mesh, MeshRenderer targetRenderer,
                                          float[] densities, Vector3 dcBasePos,
                                          int dcN, float voxelSize)
        {
            if (mesh == null) return BakeFlat3(mesh, targetRenderer);
            if (densities == null || densities.Length == 0)
                return BakeFlat3(mesh, targetRenderer);

            // [P3-NB-A] Async 경로 (v5 기반, enableAsyncBake=ON 시)
            if (enableBakerV5 && enableAsyncBake)
                return BakeNormalMapV5_Async(mesh, targetRenderer, densities, dcBasePos, dcN, voxelSize);

            if (enableBakerV5)
                return BakeNormalMapV5(mesh, targetRenderer, densities, dcBasePos, dcN, voxelSize);

            // ── v4 원본 경로 (아래 코드는 원본과 100% 동일) ──

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

            var job = new NormalBakeJobV4 {
                vertexPositions = nPos,   vertexNormals = nNorm, densities = nDens,
                normalMapX = nPixX, normalMapY = nPixY, normalMapZ = nPixZ,
                texWidth = texW,    texHeight = texH,
                dcN = dcN, dcBasePos = new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize = voxelSize, sampleStep = step, tiling = effectiveTiling,
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
            Debug.Log($"[CaveNormalBaker v4] 완료: {texW}×{texH}×3ch, vert={vertCount}, {ms:F1}ms");
            #endif
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
                                             int dcN, float voxelSize)
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

            new NormalBakeJobV5 {
                vertexPositions=nPos, vertexNormals=nNorm, densities=nDens,
                accumPosX=aPX, accumNegX=aNX, accumPosY=aPY, accumNegY=aNY,
                accumPosZ=aPZ, accumNegZ=aNZ,
                texSize=texRes, dcN=dcN,
                dcBasePos=new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize=voxelSize, sampleStep=step,
                tiling=effectiveTiling, bandHeight=bandHeight,
            }.Schedule(vertCount, 64).Complete();

            nPos.Dispose(); nNorm.Dispose(); nDens.Dispose();

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
            return textures;
        }

        // =====================================================================
        // [P3-NB-A] BakeNormalMapV5_Async — JobHandle 체인, 메인 블록 없음
        //   NormalBakeJobV5 알고리즘 불변, Schedule 방식만 변경 (규칙 #11 준수)
        //   Persistent Allocator + 3중 Dispose 방어 (규칙 #12 준수)
        // =====================================================================
        private Texture2D[] BakeNormalMapV5_Async(Mesh mesh, MeshRenderer targetRenderer,
                                                   float[] densities, Vector3 dcBasePos,
                                                   int dcN, float voxelSize)
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

            // Phase B: NormalBakeJobV5 스케줄 (Complete 없음)
            JobHandle h1 = new NormalBakeJobV5 {
                vertexPositions=nPos, vertexNormals=nNorm, densities=nDens,
                accumPosX=aPX, accumNegX=aNX, accumPosY=aPY, accumNegY=aNY,
                accumPosZ=aPZ, accumNegZ=aNZ,
                texSize=texRes, dcN=dcN,
                dcBasePos=new float3(dcBasePos.x, dcBasePos.y, dcBasePos.z),
                voxelSize=voxelSize, sampleStep=step,
                tiling=effectiveTiling, bandHeight=bandHeight,
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
    }
}
