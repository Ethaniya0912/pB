// =============================================================================
// CaveNormalBaker.cs  |  pB-4 Project — Week 0
// =============================================================================
// 동굴 메시의 Triplanar 노멀맵을 밀도장(density field)에서 베이킹.
//
// ■ 베이킹 흐름
//   BakeNormalMap(mesh, renderer, densityCache, basePos, dcN, voxelSize)
//     1. _Tiling 자동 동기화 (머티리얼 → MPB → Inspector 우선순위)
//     2. 버텍스별 밀도 그래디언트 계산 (∇ρ → 표면 법선)
//     3. Triplanar 3방향(XZ·XY·YZ) 512×512 노멀맵 텍스처 생성
//     4. MaterialPropertyBlock으로 셰이더에 직접 할당
//
// ■ 폴백
//   BakeFlat3(mesh, renderer)
//     → densityCache 없을 때 flat 노멀맵(파랑) 3채널 할당
//
// ■ 셰이더 프로퍼티 (MaterialPropertyBlock) — CaveTriplanarSplat.hlsl UV 기준
//   _DCNormalMap_X : Texture2D  (dcUvX = samplePos.zy  → 벽면 좌우, X축 뷰)
//   _DCNormalMap_Y : Texture2D  (dcUvY = samplePos.xz  → 바닥·천장, Y축 뷰)
//   _DCNormalMap_Z : Texture2D  (dcUvZ = samplePos.xy  → 벽면 전후, Z축 뷰)
//   _Tiling      : float      (triplanar UV 타일링)
//
// [변경 이력]
// v4 : _Tiling 자동 동기화 추가 (머티리얼/MPB/Inspector 3단계 우선순위)
//      DCMeshBuilder Step 8b 에서 호출 복원 (호출 누락 수정)
//      [FIX-NORMALMAP-KEY] MPB 키 _NormalMapX/Y/Z → _DCNormalMap_X/Y/Z 교체
//      [FIX-NORMALMAP-UV]  텍스처-셰이더 UV 축 정렬 (texYZ→X, texXZ→Y, texXY→Z)
// v3 : 밀도 그래디언트 기반 노멀 계산 추가
// v2 : MaterialPropertyBlock 방식으로 전환 (머티리얼 공유 깨짐 방지)
// v1 : 초기 구현
// =============================================================================

using System.Diagnostics;
using UnityEngine;

namespace CaveSystem
{
    [RequireComponent(typeof(CaveComputeDispatcher))]
    public class CaveNormalBaker : MonoBehaviour
    {
        // ──────────────────────────────────────────────────────────────────────
        // Inspector
        // ──────────────────────────────────────────────────────────────────────

        [Header("Baking")]
        [Tooltip("true = 베이킹 완료 즉시 MaterialPropertyBlock으로 셰이더에 자동 할당")]
        public bool autoAssignToShader = true;

        [Tooltip("Triplanar UV 월드 타일링 스케일. 머티리얼 _Tiling이 있으면 자동 동기화.")]
        public float tiling = 0.15f;

        [Range(64, 1024)]
        [Tooltip("노멀맵 텍스처 해상도 (기본 512)")]
        public int textureResolution = 512;

        [Header("Density Sampling")]
        [Range(1, 4)]
        [Tooltip("그래디언트 중앙차분 간격 (1 = 1복셀 간격, 기본값 권장)")]
        public int gradientStep = 1;

        [Range(0f, 1f)]
        [Tooltip("밀도 그래디언트 노멀 기여 비율 (0 = 메시 노멀만, 1 = 밀도 노멀만)")]
        public float densityNormalWeight = 0.8f;

        // ──────────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 밀도장에서 Triplanar 노멀맵 3개(XZ·XY·YZ)를 베이킹하여
        /// renderer에 MaterialPropertyBlock으로 할당.
        /// </summary>
        /// <param name="mesh">대상 메시 (vertices, normals 필요)</param>
        /// <param name="renderer">대상 MeshRenderer</param>
        /// <param name="densityCache">복셀 밀도 배열 (float[dcN³])</param>
        /// <param name="basePos">밀도 그리드 원점 (월드 좌표)</param>
        /// <param name="dcN">한 축 복셀 수 (pointsPerAxis)</param>
        /// <param name="voxelSize">복셀 하나의 월드 크기 (m)</param>
        public void BakeNormalMap(
            Mesh mesh,
            MeshRenderer renderer,
            float[] densityCache,
            Vector3 basePos,
            int dcN,
            float voxelSize)
        {
            if (mesh == null || renderer == null)
            {
                UnityEngine.Debug.LogWarning("[CaveNormalBaker] mesh 또는 renderer가 null. 건너뜀.");
                return;
            }

            var sw = Stopwatch.StartNew();

            // ── 1. _Tiling 자동 동기화 ────────────────────────────────────────
            // 우선순위: ① 머티리얼 _Tiling  ② MPB _Tiling  ③ Inspector tiling(기본 0.15)
            float effectiveTiling = tiling;

            if (renderer.sharedMaterial != null)
            {
                // ① 머티리얼 프로퍼티 직접 확인
                if (renderer.sharedMaterial.HasProperty("_Tiling"))
                {
                    float matTiling = renderer.sharedMaterial.GetFloat("_Tiling");
                    if (Mathf.Abs(matTiling - effectiveTiling) > 0.0001f)
                    {
                        UnityEngine.Debug.Log(
                            $"[CaveNormalBaker] tiling sync: " +
                            $"Inspector={effectiveTiling:F3} → Material._Tiling={matTiling:F3}");
                        effectiveTiling = matTiling;
                    }
                }
                else
                {
                    // ② MPB 확인 (머티리얼에 없을 때)
                    var mpbRead = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(mpbRead);
                    float mpbTiling = mpbRead.GetFloat("_Tiling"); // 없으면 0 반환
                    if (mpbTiling > 0.0001f && Mathf.Abs(mpbTiling - effectiveTiling) > 0.0001f)
                    {
                        UnityEngine.Debug.Log(
                            $"[CaveNormalBaker] tiling sync (MPB): " +
                            $"Inspector={effectiveTiling:F3} → MPB._Tiling={mpbTiling:F3}");
                        effectiveTiling = mpbTiling;
                    }
                    // ③ 둘 다 없으면 Inspector 기본값(0.15) 유지
                }
            }

            // ── 2. 버텍스 노멀 계산 ───────────────────────────────────────────
            var vertices = mesh.vertices;
            var meshNormals = mesh.normals;
            int vertCount = vertices.Length;

            var finalNormals = new Vector3[vertCount];
            bool hasDensity = densityCache != null && densityCache.Length > 0;

            for (int i = 0; i < vertCount; i++)
            {
                Vector3 meshN = (meshNormals != null && i < meshNormals.Length)
                    ? meshNormals[i] : Vector3.up;

                if (hasDensity)
                {
                    Vector3 densN = SampleDensityGradient(
                        vertices[i], densityCache, basePos, dcN, voxelSize);

                    // 밀도 그래디언트가 유효할 때만 혼합
                    if (densN.sqrMagnitude > 0.0001f)
                        finalNormals[i] = Vector3.Lerp(meshN, densN, densityNormalWeight).normalized;
                    else
                        finalNormals[i] = meshN;
                }
                else
                {
                    finalNormals[i] = meshN;
                }
            }

            // ── 3. Triplanar 노멀맵 텍스처 생성 ──────────────────────────────
            int res = textureResolution;
            var pixXZ = new Color32[res * res]; // 바닥·천장 (Y축 방향)
            var pixXY = new Color32[res * res]; // 벽면 (Z축 방향)
            var pixYZ = new Color32[res * res]; // 벽면 (X축 방향)

            // 기본값: flat 노멀 (0,0,1) → (128,128,255)
            var flat = new Color32(128, 128, 255, 255);
            for (int i = 0; i < res * res; i++)
                pixXZ[i] = pixXY[i] = pixYZ[i] = flat;

            // 각 버텍스 → UV 공간에 노멀 기록
            for (int i = 0; i < vertCount; i++)
            {
                Vector3 n = finalNormals[i];
                Vector3 p = vertices[i];

                // Normal → Color32 인코딩: (n×0.5+0.5) × 255
                byte NtoB(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 0.5f * 255 + 127.5f), 0, 255);

                // ① XZ 평면 (Y축 법선, 바닥·천장)
                //    u=p.x, v=p.z, 인코딩 = (nx, nz, ny)
                {
                    float u = Mathf.Repeat(p.x * effectiveTiling, 1f);
                    float v = Mathf.Repeat(p.z * effectiveTiling, 1f);
                    int px = Mathf.Clamp((int)(u * res), 0, res - 1);
                    int py = Mathf.Clamp((int)(v * res), 0, res - 1);
                    pixXZ[py * res + px] = new Color32(NtoB(n.x), NtoB(n.z), NtoB(n.y), 255);
                }

                // ② XY 평면 (Z축 법선, 벽면 전후)
                //    u=p.x, v=p.y, 인코딩 = (nx, ny, nz)
                {
                    float u = Mathf.Repeat(p.x * effectiveTiling, 1f);
                    float v = Mathf.Repeat(p.y * effectiveTiling, 1f);
                    int px = Mathf.Clamp((int)(u * res), 0, res - 1);
                    int py = Mathf.Clamp((int)(v * res), 0, res - 1);
                    pixXY[py * res + px] = new Color32(NtoB(n.x), NtoB(n.y), NtoB(n.z), 255);
                }

                // ③ YZ 평면 (X축 법선, 벽면 좌우)
                //    u=p.z, v=p.y, 인코딩 = (nz, ny, nx)
                {
                    float u = Mathf.Repeat(p.z * effectiveTiling, 1f);
                    float v = Mathf.Repeat(p.y * effectiveTiling, 1f);
                    int px = Mathf.Clamp((int)(u * res), 0, res - 1);
                    int py = Mathf.Clamp((int)(v * res), 0, res - 1);
                    pixYZ[py * res + px] = new Color32(NtoB(n.z), NtoB(n.y), NtoB(n.x), 255);
                }
            }

            // 텍스처 생성 (mipmap = true, Linear 색공간)
            var texXZ = new Texture2D(res, res, TextureFormat.RGB24, true, true);
            var texXY = new Texture2D(res, res, TextureFormat.RGB24, true, true);
            var texYZ = new Texture2D(res, res, TextureFormat.RGB24, true, true);

            texXZ.SetPixels32(pixXZ); texXZ.Apply();
            texXY.SetPixels32(pixXY); texXY.Apply();
            texYZ.SetPixels32(pixYZ); texYZ.Apply();

            texXZ.wrapMode = texXY.wrapMode = texYZ.wrapMode = TextureWrapMode.Repeat;

            // ── 4. 셰이더에 할당 (MPB) ───────────────────────────────────────
            if (autoAssignToShader)
            {
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);

                // [FIX-NORMALMAP-KEY] 셰이더 실제 프로퍼티명 + UV 축 정렬
                // CaveTriplanarSplat.hlsl UV 매핑:
                //   _DCNormalMap_X: dcUvX = samplePos.zy  → baker texYZ (p.z, p.y 기반)
                //   _DCNormalMap_Y: dcUvY = samplePos.xz  → baker texXZ (p.x, p.z 기반, 바닥/천장)
                //   _DCNormalMap_Z: dcUvZ = samplePos.xy  → baker texXY (p.x, p.y 기반)
                mpb.SetTexture("_DCNormalMap_X", texYZ);  // p.zy ↔ samplePos.zy ✓
                mpb.SetTexture("_DCNormalMap_Y", texXZ);  // p.xz ↔ samplePos.xz ✓
                mpb.SetTexture("_DCNormalMap_Z", texXY);  // p.xy ↔ samplePos.xy ✓
                mpb.SetFloat("_Tiling", effectiveTiling);

                renderer.SetPropertyBlock(mpb);
            }

            sw.Stop();
            UnityEngine.Debug.Log(
                $"[CaveNormalBaker v4] 완료: {res}×{res}×3ch, " +
                $"vert={vertCount}, " +
                $"tiling={effectiveTiling:F3}, " +
                $"{sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// densityCache 없을 때 폴백: flat 노멀맵(128,128,255) 3개를 할당.
        /// </summary>
        public void BakeFlat3(Mesh mesh, MeshRenderer renderer)
        {
            if (renderer == null) return;

            int res = textureResolution;
            var flat = new Color32(128, 128, 255, 255);
            var pixels = new Color32[res * res];

            for (int i = 0; i < pixels.Length; i++) pixels[i] = flat;

            var tex = new Texture2D(res, res, TextureFormat.RGB24, false, true);
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;

            if (autoAssignToShader)
            {
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);
                // [FIX-NORMALMAP-KEY] BakeFlat3도 동일하게 올바른 프로퍼티명 사용
                mpb.SetTexture("_DCNormalMap_X", tex);
                mpb.SetTexture("_DCNormalMap_Y", tex);
                mpb.SetTexture("_DCNormalMap_Z", tex);
                mpb.SetFloat("_Tiling", tiling);
                renderer.SetPropertyBlock(mpb);
            }

            UnityEngine.Debug.Log(
                $"[CaveNormalBaker v4] BakeFlat3 완료: {res}×{res}×3ch " +
                $"(densityCache 없음 — flat 폴백 사용)");
        }

        // ──────────────────────────────────────────────────────────────────────
        // 내부 유틸
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 복셀 밀도 배열에서 월드 위치 wp의 밀도 그래디언트(= 표면 법선 방향)를 샘플링.
        /// 중앙차분법: ∇ρ = ( ρ(x+s)-ρ(x-s), ρ(y+s)-ρ(y-s), ρ(z+s)-ρ(z-s) ) / 2s
        /// </summary>
        private Vector3 SampleDensityGradient(
            Vector3 wp,
            float[] density,
            Vector3 basePos,
            int N,
            float voxelSize)
        {
            // 월드 → 그리드 좌표 변환
            Vector3 local = (wp - basePos) / voxelSize;
            int cx = Mathf.RoundToInt(local.x);
            int cy = Mathf.RoundToInt(local.y);
            int cz = Mathf.RoundToInt(local.z);

            int s = gradientStep;

            float Sample(int gx, int gy, int gz)
            {
                gx = Mathf.Clamp(gx, 0, N - 1);
                gy = Mathf.Clamp(gy, 0, N - 1);
                gz = Mathf.Clamp(gz, 0, N - 1);
                // FlatIndex: x + y*N + z*N² (Z-major, CaveDualContouring 기준)
                int idx = gx + gy * N + gz * N * N;
                return idx >= 0 && idx < density.Length ? density[idx] : 0f;
            }

            float dx = Sample(cx + s, cy, cz) - Sample(cx - s, cy, cz);
            float dy = Sample(cx, cy + s, cz) - Sample(cx, cy - s, cz);
            float dz = Sample(cx, cy, cz + s) - Sample(cx, cy, cz - s);

            var grad = new Vector3(dx, dy, dz);
            return grad.sqrMagnitude > 0.0001f ? grad.normalized : Vector3.zero;
        }
    }
}