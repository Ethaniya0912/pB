// =============================================================================
// CaveNormalBaker.cs  |  pB-4 Project — Week 0, Stage 5
// Layer  : Pipeline (지형)
// Owner  : Person B
//
// 역할:
//   DC 메쉬의 Hi-poly 노말 정보를 Lo-poly Normal Map으로 베이킹한다.
//   DC는 MC보다 정밀한 노말을 생성하므로, 이 노말을 텍스처에 베이킹하면
//   Lo-poly 메쉬에서도 Hi-poly 수준의 라이팅 디테일을 표현할 수 있다.
//
//   CaveDreamcoreTerrain.shader와 연동하여 _NormalScale 파라미터로 강도 조절.
//   DynamicFogController의 NarrativeTag 연동도 이 단계에서 검증한다.
//
// 기존 파이프라인과의 관계:
//   - 기존 MC 메쉬에는 적용하지 않음 (DC 전용)
//   - DCMeshBuilder.BuildMeshFromDCData() 완료 후 호출
//   - 생성된 Normal Map은 CaveDreamcoreTerrain.shader의 _BumpMap 슬롯에 할당
// =============================================================================
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

namespace CaveSystem
{
    /// <summary>
    /// 버텍스 노말을 탄젠트 공간 Normal Map 텍셀로 변환하는 Job.
    /// </summary>
    [BurstCompile]
    public struct NormalBakeJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> vertexNormals;
        [ReadOnly] public NativeArray<Vector3> vertexPositions;
        [ReadOnly] public NativeArray<Vector2> vertexUVs;
        [WriteOnly] public NativeArray<Color32> normalMapPixels;
        public int textureWidth;
        public int textureHeight;

        public void Execute(int index)
        {
            if (index >= vertexNormals.Length) return;

            Vector3 normal = vertexNormals[index];
            Vector2 uv = vertexUVs[index];

            // UV → 텍셀 좌표
            int px = Mathf.Clamp((int)(uv.x * textureWidth), 0, textureWidth - 1);
            int py = Mathf.Clamp((int)(uv.y * textureHeight), 0, textureHeight - 1);
            int pixelIdx = py * textureWidth + px;

            if (pixelIdx < 0 || pixelIdx >= normalMapPixels.Length) return;

            // 월드 스페이스 노말 → 탄젠트 스페이스 인코딩 (0~1 범위)
            // 단순화: 오브젝트 스페이스 노말을 직접 인코딩
            // 완전한 탄젠트 스페이스 변환은 pC에서 TBN 행렬 기반으로 개선
            byte r = (byte)((normal.x * 0.5f + 0.5f) * 255);
            byte g = (byte)((normal.y * 0.5f + 0.5f) * 255);
            byte b = (byte)((normal.z * 0.5f + 0.5f) * 255);

            normalMapPixels[pixelIdx] = new Color32(r, g, b, 255);
        }
    }

    public class CaveNormalBaker : MonoBehaviour
    {
        [Header("Bake Settings")]
        [Tooltip("Normal Map 텍스처 해상도")]
        public int normalMapResolution = 512;

        [Tooltip("생성된 Normal Map을 자동으로 셰이더에 할당할지 여부")]
        public bool autoAssignToShader = true;

        [Header("Debug")]
        [SerializeField] private int lastBakeVertexCount;
        [SerializeField] private float lastBakeTimeMs;

        /// <summary>
        /// DC 메쉬의 노말을 텍스처에 베이킹한다.
        /// DCMeshBuilder.BuildMeshFromDCData() 완료 후 호출.
        /// </summary>
        /// <param name="mesh">DC로 생성된 메쉬</param>
        /// <param name="targetRenderer">Normal Map을 적용할 MeshRenderer</param>
        /// <returns>생성된 Normal Map 텍스처</returns>
        public Texture2D BakeNormalMap(Mesh mesh, MeshRenderer targetRenderer = null)
        {
            if (mesh == null)
            {
                Debug.LogWarning("[CaveNormalBaker] 메쉬가 null입니다.");
                return null;
            }

            float startTime = Time.realtimeSinceStartup;

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;

            if (positions == null || normals == null || uvs == null ||
                positions.Length == 0 || normals.Length == 0 || uvs.Length == 0)
            {
                Debug.LogWarning("[CaveNormalBaker] 메쉬 데이터가 불완전합니다.");
                return null;
            }

            int vertCount = positions.Length;
            int texW = normalMapResolution;
            int texH = normalMapResolution;
            int pixelCount = texW * texH;

            // NativeArray 할당
            var nativeNormals = new NativeArray<Vector3>(normals, Allocator.TempJob);
            var nativePositions = new NativeArray<Vector3>(positions, Allocator.TempJob);
            var nativeUVs = new NativeArray<Vector2>(uvs, Allocator.TempJob);
            var nativePixels = new NativeArray<Color32>(pixelCount, Allocator.TempJob);

            // 기본 배경색: (0.5, 0.5, 1.0) = 평탄한 노말 (위를 향함)
            for (int i = 0; i < pixelCount; i++)
                nativePixels[i] = new Color32(128, 128, 255, 255);

            // 베이킹 Job 실행
            var bakeJob = new NormalBakeJob
            {
                vertexNormals = nativeNormals,
                vertexPositions = nativePositions,
                vertexUVs = nativeUVs,
                normalMapPixels = nativePixels,
                textureWidth = texW,
                textureHeight = texH
            };

            bakeJob.Schedule(vertCount, 64).Complete();

            // Texture2D 생성
            Texture2D normalMap = new Texture2D(texW, texH, TextureFormat.RGBA32, true)
            {
                name = $"DC_NormalMap_{mesh.name}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear
            };

            normalMap.SetPixelData(nativePixels.ToArray(), 0);
            normalMap.Apply(true); // Mipmap 생성

            // NativeArray 해제
            nativeNormals.Dispose();
            nativePositions.Dispose();
            nativeUVs.Dispose();
            nativePixels.Dispose();

            // 셰이더에 자동 할당
            if (autoAssignToShader && targetRenderer != null)
            {
                Material mat = targetRenderer.material;
                if (mat != null)
                {
                    // CaveDreamcoreTerrain.shader의 _BumpMap 또는 _NormalMap 슬롯
                    if (mat.HasProperty("_BumpMap"))
                        mat.SetTexture("_BumpMap", normalMap);
                    else if (mat.HasProperty("_NormalMap"))
                        mat.SetTexture("_NormalMap", normalMap);

                    Debug.Log($"[CaveNormalBaker] Normal Map이 셰이더에 할당되었습니다: {normalMap.name}");
                }
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            lastBakeVertexCount = vertCount;
            lastBakeTimeMs = elapsed;

            Debug.Log($"[CaveNormalBaker] 베이킹 완료: {texW}×{texH}, 버텍스={vertCount}, 소요={elapsed:F1}ms");

            return normalMap;
        }
    }
}
