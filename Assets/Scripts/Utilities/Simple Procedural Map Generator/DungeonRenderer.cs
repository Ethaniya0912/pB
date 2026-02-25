using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 찢어짐(Tearing)을 완벽히 봉합하고, 사실적인 기암괴석 동굴 질감을 구현하는 렌더러
/// </summary>
public class DungeonRenderer
{
    private class MeshData
    {
        public List<Vector3> vertices = new List<Vector3>();
        public List<int> triangles = new List<int>();
        public List<Vector2> uvs = new List<Vector2>();

        public Dictionary<Vector3Int, int> vertexCache = new Dictionary<Vector3Int, int>();
        public bool isOrganic = false;

        public void AddFace(Vector3[] faceVerts)
        {
            int[] indices = new int[4];
            for (int i = 0; i < 4; i++)
            {
                // 해시 키 정밀도를 높여 동일한 위치의 정점을 완벽히 병합 (Welding)
                Vector3Int hash = new Vector3Int(
                    Mathf.RoundToInt(faceVerts[i].x * 100f),
                    Mathf.RoundToInt(faceVerts[i].y * 100f),
                    Mathf.RoundToInt(faceVerts[i].z * 100f)
                );

                if (vertexCache.TryGetValue(hash, out int index))
                {
                    indices[i] = index;
                }
                else
                {
                    indices[i] = vertices.Count;
                    vertices.Add(faceVerts[i]);
                    uvs.Add(new Vector2(faceVerts[i].x, faceVerts[i].z));
                    vertexCache[hash] = indices[i];
                }
            }

            // 시계 방향 삼각형
            triangles.Add(indices[0]); triangles.Add(indices[1]); triangles.Add(indices[2]);
            triangles.Add(indices[0]); triangles.Add(indices[2]); triangles.Add(indices[3]);
        }

        // ==========================================
        // 💡 [혁신] 찢어짐 원천 차단 + 리얼한 던전 바위 텍스처링
        // ==========================================
        public IEnumerator SmoothAndAddRockDetailsRoutine(string matName)
        {
            // 인공물(벙커 등)은 변형 없이 타일과 직각을 유지
            if (!isOrganic || vertices.Count == 0) yield break;

            Debug.Log($"<color=orange>      -> [{matName}] 글로벌 3D 프랙탈 노이즈 조각 중 (찢어짐 방지)...</color>");

            // 기존의 인접 정점 기반 라플라시안 스무딩(Laplacian Smoothing)은 메쉬가 다르면
            // 서로 다르게 움직여 찢어지는 문제가 있었으므로 완전히 폐기합니다.
            // 대신, 절대적인 '우주 공간 좌표'를 기반으로 변형을 주어 경계면이 100% 일치하게 만듭니다.
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 origPos = vertices[i];
                Vector3 displacement = GetDeterministicDisplacement(origPos);

                vertices[i] = origPos + displacement;

                // 프레임 드랍 방지
                if (i % 15000 == 0) yield return null;
            }

            Debug.Log($"<color=green>      -> [{matName}] 던전 암석 디테일 조각 100% 완료!</color>");
        }

        /// <summary>
        /// 위치값을 시드로 사용하여 무작위하지만 항상 고정된(결정론적) 변위 벡터를 반환합니다.
        /// </summary>
        private Vector3 GetDeterministicDisplacement(Vector3 pos)
        {
            // 노이즈의 큼직한 덩어리 빈도 (낮을수록 동굴 벽이 큼직큼직하게 파임)
            float freq = 0.45f;

            // 서로 다른 시드 오프셋을 주어 X, Y, Z축이 독립적으로 일그러지게 함
            float nx = FractalNoise3D(pos.x, pos.y, pos.z, freq, 0f);
            float ny = FractalNoise3D(pos.x, pos.y, pos.z, freq, 1000f);
            float nz = FractalNoise3D(pos.x, pos.y, pos.z, freq, 2000f);

            // [게임성 보장]
            // X, Z축(벽면)은 최대 0.65칸까지 거칠게 튀어나오고 들어가게 하여 기암괴석을 만듭니다.
            // Y축(바닥/천장)은 NavMesh 에이전트가 걷다가 걸리지 않도록 변위를 0.1칸으로 극단적으로 제한합니다.
            return new Vector3(nx * 0.65f, ny * 0.1f, nz * 0.65f);
        }

        /// <summary>
        /// 3 옥타브(Octaves) FBM을 사용해 큰 바위부터 자잘한 돌 표면까지 복합적인 노이즈를 만듭니다.
        /// </summary>
        private float FractalNoise3D(float x, float y, float z, float freq, float seedOffset)
        {
            float val = 0;
            float amp = 1f;
            float maxAmp = 0f;

            for (int i = 0; i < 3; i++)
            {
                float pX = x * freq + seedOffset;
                float pY = y * freq + seedOffset;
                float pZ = z * freq + seedOffset;

                // 2D 펄린 노이즈 3개를 교차합성하여 가짜 3D 노이즈 필드 생성
                float xy = Mathf.PerlinNoise(pX, pY);
                float yz = Mathf.PerlinNoise(pY, pZ);
                float zx = Mathf.PerlinNoise(pZ, pX);

                // 0 ~ 1 범위를 -0.5 ~ 0.5로 중앙 정렬
                float noise = (xy + yz + zx) / 3f - 0.5f;

                val += noise * amp;
                maxAmp += amp;

                // 다음 옥타브: 더 자잘하고(freq 증가) 얕게(amp 감소) 파임
                freq *= 2.1f;
                amp *= 0.5f;
            }

            // 진폭 정규화 (-0.5 ~ 0.5) 반환
            return val / maxAmp;
        }
    }

    private int width, height, depth;
    private VoxelData[,,] voxelMap;
    private Transform chunkContainer;

    public DungeonRenderer(int w, int h, int d, VoxelData[,,] map, Transform container)
    {
        width = w; height = h; depth = d;
        voxelMap = map; chunkContainer = container;
    }

    public IEnumerator BuildOptimizedMeshesRoutine(Func<BiomeType, Material> materialGetter, Material bossMat)
    {
        Dictionary<Material, MeshData> meshDict = new Dictionary<Material, MeshData>();
        Debug.Log($"<color=cyan>▶ [렌더링] 1/2: 전체 복셀 데이터 표면(Face) 스캔 중...</color>");

        for (int x = 0; x < width; x++)
        {
            if (x % 15 == 0) yield return null;

            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    if (voxelMap[x, y, z].Type == VoxelType.Solid)
                    {
                        BiomeType biome = voxelMap[x, y, z].Biome;
                        Material matToUse = materialGetter(biome);
                        if (voxelMap[x, y, z].IsBossRoom) matToUse = bossMat;

                        if (!meshDict.ContainsKey(matToUse))
                        {
                            meshDict[matToUse] = new MeshData();
                            meshDict[matToUse].isOrganic = (biome == BiomeType.MineShafts || biome == BiomeType.DeepAbyss);
                        }

                        MeshData data = meshDict[matToUse];

                        if (IsVoxelAir(x, y + 1, z)) data.AddFace(new Vector3[] { new Vector3(x - 0.5f, y + 0.5f, z + 0.5f), new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), new Vector3(x + 0.5f, y + 0.5f, z - 0.5f), new Vector3(x - 0.5f, y + 0.5f, z - 0.5f) });
                        if (IsVoxelAir(x, y - 1, z)) data.AddFace(new Vector3[] { new Vector3(x - 0.5f, y - 0.5f, z - 0.5f), new Vector3(x + 0.5f, y - 0.5f, z - 0.5f), new Vector3(x + 0.5f, y - 0.5f, z + 0.5f), new Vector3(x - 0.5f, y - 0.5f, z + 0.5f) });
                        if (IsVoxelAir(x + 1, y, z)) data.AddFace(new Vector3[] { new Vector3(x + 0.5f, y - 0.5f, z - 0.5f), new Vector3(x + 0.5f, y + 0.5f, z - 0.5f), new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), new Vector3(x + 0.5f, y - 0.5f, z + 0.5f) });
                        if (IsVoxelAir(x - 1, y, z)) data.AddFace(new Vector3[] { new Vector3(x - 0.5f, y - 0.5f, z + 0.5f), new Vector3(x - 0.5f, y + 0.5f, z + 0.5f), new Vector3(x - 0.5f, y + 0.5f, z - 0.5f), new Vector3(x - 0.5f, y - 0.5f, z - 0.5f) });
                        if (IsVoxelAir(x, y, z + 1)) data.AddFace(new Vector3[] { new Vector3(x + 0.5f, y - 0.5f, z + 0.5f), new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), new Vector3(x - 0.5f, y + 0.5f, z + 0.5f), new Vector3(x - 0.5f, y - 0.5f, z + 0.5f) });
                        if (IsVoxelAir(x, y, z - 1)) data.AddFace(new Vector3[] { new Vector3(x - 0.5f, y - 0.5f, z - 0.5f), new Vector3(x - 0.5f, y + 0.5f, z - 0.5f), new Vector3(x + 0.5f, y + 0.5f, z - 0.5f), new Vector3(x + 0.5f, y - 0.5f, z - 0.5f) });
                    }
                }
            }
        }

        Debug.Log($"<color=cyan>▶ [렌더링] 2/2: {meshDict.Count}개의 바이옴 그룹 메쉬 렌더링 시작...</color>");

        int chunkIdx = 1;
        foreach (var kvp in meshDict)
        {
            Debug.Log($"<color=cyan>  >> 구역 렌더링 ({chunkIdx}/{meshDict.Count}): {kvp.Key.name}</color>");
            yield return kvp.Value.SmoothAndAddRockDetailsRoutine(kvp.Key.name);

            GameObject chunkObj = new GameObject($"VoxelChunk_{kvp.Key.name}");
            chunkObj.transform.SetParent(chunkContainer);

            MeshFilter mf = chunkObj.AddComponent<MeshFilter>();
            MeshRenderer mr = chunkObj.AddComponent<MeshRenderer>();
            mr.material = kvp.Key;

            Mesh mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = kvp.Value.vertices.ToArray();
            mesh.triangles = kvp.Value.triangles.ToArray();
            mesh.uv = kvp.Value.uvs.ToArray();

            // 공유 정점(Welded Vertices)이므로 RecalculateNormals()가 호출될 때 
            // 자연스럽게 부드러운 쉐이딩(Smooth Shading)이 계산되어 사실적인 바위처럼 보이게 됩니다.
            mesh.RecalculateNormals();

            mf.mesh = mesh;
            chunkObj.AddComponent<MeshCollider>().sharedMesh = mesh;

            chunkIdx++;
        }
    }

    private bool IsVoxelAir(int x, int y, int z)
    {
        if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= depth) return true;
        return voxelMap[x, y, z].Type == VoxelType.Air;
    }
}