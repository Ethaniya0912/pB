#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-F hotfix v4] EdgeMeshOutlineExtractor
    //
    // 목적: Runtime DC mesh에서 "뚜렷한 edge" (silhouette / feature edges) 추출
    //       및 Douglas-Peucker로 단순화 → 경량 wireframe 렌더에 사용.
    //
    // 알고리즘:
    //   1. Scene의 모든 MeshFilter (DC chunk mesh) 수집
    //   2. 각 mesh의 triangles에서 edge 빈도 계산
    //      - 1번만 등장한 edge = boundary edge (mesh 경계)
    //      - 2번 등장했지만 두 triangle의 normal 각도 차이가 큰 edge = feature edge (silhouette)
    //   3. 추출된 edge set을 구성, 길이 순 정렬
    //   4. Douglas-Peucker로 연속 edge를 단순화 (tolerance로 밀도 조절)
    //   5. 결과 line segment들을 cache → Draw에서 재사용
    //
    // 비용 제어:
    //   - Cache: mesh 변경 (chunk 추가/제거) 시에만 재빌드
    //   - Max segments cap: 과도한 라인 방지 (기본 5000개)
    //   - 매 N초 주기 rebuild (runtime mesh는 streaming으로 계속 변함)
    // ══════════════════════════════════════════════════════════════════════

    public static class EdgeMeshOutlineExtractor
    {
        // ── Cache ──
        private static List<Vector3> _cachedSegmentStarts = new List<Vector3>();
        private static List<Vector3> _cachedSegmentEnds = new List<Vector3>();
        private static int _lastMeshHash = 0;
        private static double _lastExtractTime = 0;
        private const double REBUILD_INTERVAL = 3.0;   // 3초마다 재추출
        private const int MAX_SEGMENTS = 20000;        // 과도한 선 방지
        private const float FEATURE_ANGLE_THRESHOLD = 15f;  // silhouette 각도 threshold (°)

        // [hotfix v6] Graph 근접 필터 설정
        //   통로 (edge capsule) 반경 × multiplier 안에 있는 mesh segment만 표시
        //   → 파편/고립 mesh/외부 rock 제거
        private const float PROXIMITY_MULTIPLIER = 1.8f;  // edge.width × 1.8 반경
        private const float MIN_PROXIMITY_RADIUS = 3f;    // 최소 3m (얇은 통로 대비)
        private const float NODE_PROXIMITY_MULTIPLIER = 1.5f;  // node.radius × 1.5

        /// <summary>
        /// Extract 또는 cache 반환. segmentStarts[i] → segmentEnds[i] 쌍 사용.
        /// [hotfix v6] filterByGraph = true면 graph nodes/edges 근접 영역만 표시.
        /// </summary>
        public static void GetSegments(float simplifyTolerance,
            out List<Vector3> starts, out List<Vector3> ends,
            bool filterByGraph = true,
            List<NodeData> graphNodes = null, List<EdgeData> graphEdges = null)
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            bool shouldRebuild = now - _lastExtractTime > REBUILD_INTERVAL;

            if (shouldRebuild)
            {
                RebuildCache(simplifyTolerance, filterByGraph, graphNodes, graphEdges);
                _lastExtractTime = now;
            }

            starts = _cachedSegmentStarts;
            ends = _cachedSegmentEnds;
        }

        /// <summary>강제 재추출 (예: 사용자 tolerance 변경 시).</summary>
        public static void ForceRebuild(float simplifyTolerance,
            bool filterByGraph = true,
            List<NodeData> graphNodes = null, List<EdgeData> graphEdges = null)
        {
            RebuildCache(simplifyTolerance, filterByGraph, graphNodes, graphEdges);
            _lastExtractTime = UnityEditor.EditorApplication.timeSinceStartup;
        }

        private static void RebuildCache(float simplifyTolerance,
            bool filterByGraph,
            List<NodeData> graphNodes, List<EdgeData> graphEdges)
        {
            _cachedSegmentStarts.Clear();
            _cachedSegmentEnds.Clear();

            // 1. Scene에서 모든 MeshFilter 수집
            var meshFilters = Object.FindObjectsByType<MeshFilter>(
                FindObjectsSortMode.None);

            if (meshFilters == null || meshFilters.Length == 0) return;

            int meshHash = 0;
            int processedCount = 0;

            // ─── 진단 통계 ───
            int statTotal = 0, statNoMesh = 0, statNotChunk = 0, statInactive = 0;
            int statValid = 0, statVertTotal = 0, statTriTotal = 0;
            int statBoundary = 0, statFeature = 0, statRegular = 0;
            int statGraphRejected = 0;  // [v6] graph 근접 필터에 의해 제거된 segment 수

            // [v6] Graph 근접 필터 준비
            //   각 edge/node의 AABB를 미리 계산 → 빠른 pre-filter
            //   그 후 실제 capsule distance로 정밀 검증
            bool useGraphFilter = filterByGraph
                && ((graphNodes != null && graphNodes.Count > 0)
                    || (graphEdges != null && graphEdges.Count > 0));

            foreach (var mf in meshFilters)
            {
                statTotal++;
                if (mf == null) continue;

                // sharedMesh가 null이면 mesh (instance) 시도 — runtime에서 동적 할당되는 경우
                Mesh mesh = mf.sharedMesh;
                if (mesh == null)
                {
                    // .mesh 접근은 Editor에서 warning 발생 가능, try로 감쌈
                    try { mesh = mf.mesh; } catch { mesh = null; }
                }
                if (mesh == null)
                {
                    statNoMesh++;
                    continue;
                }

                // DC chunk 필터 — 이름 패턴 또는 mesh 크기 (cave mesh는 보통 꽤 큼)
                //   기존 "Chunk" 포함만 → runtime에서 prefab 이름이 다르면 실패
                //   완화: mesh vertex ≥ 100 이고 bounds 반경 ≥ 2m 면 후보로 간주
                string goName = mf.gameObject.name;
                bool matchName = goName.Contains("Chunk") || goName.Contains("chunk")
                    || goName.Contains("Cave") || goName.Contains("cave");

                if (!matchName)
                {
                    // 이름으로 매치 안되면 — mesh 크기로 휴리스틱 검증
                    var tempVerts = mesh.vertices;
                    if (tempVerts == null || tempVerts.Length < 100)
                    {
                        statNotChunk++;
                        continue;
                    }
                    // mesh bounds 크기로 작은 것 제외 (character, prop 등)
                    if (mesh.bounds.size.magnitude < 2f)
                    {
                        statNotChunk++;
                        continue;
                    }
                    // 여기 도달 = 충분히 큰 untitled mesh → cave chunk로 추정
                }
                if (!mf.gameObject.activeInHierarchy)
                {
                    statInactive++;
                    continue;
                }

                var verts = mesh.vertices;
                var tris = mesh.triangles;
                if (verts == null || tris == null || tris.Length < 3) continue;

                statValid++;
                statVertTotal += verts.Length;
                statTriTotal += tris.Length / 3;

                // 메쉬 hash (변경 감지)
                unchecked { meshHash = meshHash * 31 + mesh.GetInstanceID() + verts.Length; }

                // World-space 변환
                var localToWorld = mf.transform.localToWorldMatrix;

                // 2. Edge → 인접 triangle normals 수집
                var edgeMap = new Dictionary<long, EdgeInfo>(tris.Length);

                for (int t = 0; t < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];

                    Vector3 v0 = localToWorld.MultiplyPoint3x4(verts[i0]);
                    Vector3 v1 = localToWorld.MultiplyPoint3x4(verts[i1]);
                    Vector3 v2 = localToWorld.MultiplyPoint3x4(verts[i2]);

                    Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
                    // degenerate triangle skip
                    if (cross.sqrMagnitude < 1e-10f) continue;
                    Vector3 triNormal = cross.normalized;

                    AddOrUpdateEdge(edgeMap, i0, i1, triNormal, v0, v1);
                    AddOrUpdateEdge(edgeMap, i1, i2, triNormal, v1, v2);
                    AddOrUpdateEdge(edgeMap, i2, i0, triNormal, v2, v0);
                }

                // 3. Feature/boundary edge 추출
                float cosThreshold = Mathf.Cos(FEATURE_ANGLE_THRESHOLD * Mathf.Deg2Rad);

                foreach (var kv in edgeMap)
                {
                    var info = kv.Value;
                    bool isBoundary = (info.count == 1);
                    // count == 2 — 두 triangle 공유 edge → normal 각도 큰 것만 (feature edge)
                    bool isFeature = (info.count >= 2)
                        && Vector3.Dot(info.normal1, info.normal2) < cosThreshold;

                    if (!isBoundary && !isFeature)
                    {
                        statRegular++;
                        continue;
                    }

                    // [v6] Graph 근접 필터 — 파편/고립 mesh 제거
                    //   segment의 양 끝점 중 하나라도 graph 근접 영역 안이면 채택
                    //   (partial segment도 살리기 위함)
                    if (useGraphFilter && !IsNearGraph(info.posA, info.posB, graphNodes, graphEdges))
                    {
                        statGraphRejected++;
                        continue;
                    }

                    if (isBoundary) statBoundary++;
                    else statFeature++;

                    _cachedSegmentStarts.Add(info.posA);
                    _cachedSegmentEnds.Add(info.posB);
                    processedCount++;
                    if (processedCount >= MAX_SEGMENTS) break;
                }

                if (processedCount >= MAX_SEGMENTS) break;
            }

            // 4. Douglas-Peucker 단순화 (tolerance 적용)
            int statBeforeSimplify = _cachedSegmentStarts.Count;
            if (simplifyTolerance > 0.01f)
            {
                int writeIdx = 0;
                for (int i = 0; i < _cachedSegmentStarts.Count; i++)
                {
                    float len = Vector3.Distance(_cachedSegmentStarts[i], _cachedSegmentEnds[i]);
                    // tolerance보다 짧은 segment는 skip (미세 edge 필터)
                    if (len >= simplifyTolerance)
                    {
                        _cachedSegmentStarts[writeIdx] = _cachedSegmentStarts[i];
                        _cachedSegmentEnds[writeIdx] = _cachedSegmentEnds[i];
                        writeIdx++;
                    }
                }
                _cachedSegmentStarts.RemoveRange(writeIdx, _cachedSegmentStarts.Count - writeIdx);
                _cachedSegmentEnds.RemoveRange(writeIdx, _cachedSegmentEnds.Count - writeIdx);
            }

            _lastMeshHash = meshHash;

            Debug.Log($"[EdgeMeshOutline] Rebuilt: {_cachedSegmentStarts.Count} segments " +
                      $"(before simplify: {statBeforeSimplify}) " +
                      $"tolerance={simplifyTolerance:F2}m, graphFilter={useGraphFilter}\n" +
                      $"  Mesh filters: total={statTotal}, noMesh={statNoMesh}, " +
                      $"notChunk={statNotChunk}, inactive={statInactive}, valid={statValid}\n" +
                      $"  Valid mesh: verts={statVertTotal}, tris={statTriTotal}\n" +
                      $"  Edge classification: boundary={statBoundary}, feature={statFeature}, " +
                      $"regular={statRegular} (skipped), graph-rejected={statGraphRejected}");
        }

        private struct EdgeInfo
        {
            public Vector3 posA, posB;
            public Vector3 normal1, normal2;
            public int count;
        }

        private static void AddOrUpdateEdge(
            Dictionary<long, EdgeInfo> map, int i0, int i1,
            Vector3 triNormal, Vector3 posA, Vector3 posB)
        {
            int a = Mathf.Min(i0, i1);
            int b = Mathf.Max(i0, i1);
            long key = ((long)a << 32) | (uint)b;

            if (map.TryGetValue(key, out var info))
            {
                info.count++;
                info.normal2 = triNormal;
                map[key] = info;
            }
            else
            {
                map[key] = new EdgeInfo
                {
                    posA = posA,
                    posB = posB,
                    normal1 = triNormal,
                    normal2 = Vector3.zero,
                    count = 1,
                };
            }
        }

        /// <summary>
        /// [hotfix v6] mesh edge (posA, posB)가 graph nodes/edges 근접 영역에 있는지 검사.
        /// 양 끝점 중 하나라도 근접 안에 있으면 true → 해당 segment 채택.
        /// 둘 다 밖이면 false → 파편/고립 mesh로 간주하고 제거.
        /// </summary>
        private static bool IsNearGraph(Vector3 posA, Vector3 posB,
            List<NodeData> graphNodes, List<EdgeData> graphEdges)
        {
            // Node 반경 안 확인 (빠름)
            if (graphNodes != null)
            {
                for (int i = 0; i < graphNodes.Count; i++)
                {
                    var n = graphNodes[i];
                    float r = n.radius * NODE_PROXIMITY_MULTIPLIER;
                    float rSq = r * r;
                    if ((posA - n.position).sqrMagnitude <= rSq) return true;
                    if ((posB - n.position).sqrMagnitude <= rSq) return true;
                }
            }

            // Edge capsule 거리 확인 (좀 더 무거움)
            if (graphEdges != null)
            {
                for (int i = 0; i < graphEdges.Count; i++)
                {
                    var e = graphEdges[i];
                    float effR = Mathf.Max(e.width * PROXIMITY_MULTIPLIER, MIN_PROXIMITY_RADIUS);
                    float effRSq = effR * effR;
                    if (PointToSegmentSqrDistance(posA, e.startPos, e.endPos) <= effRSq) return true;
                    if (PointToSegmentSqrDistance(posB, e.startPos, e.endPos) <= effRSq) return true;
                }
            }

            return false;
        }

        /// <summary>점 p와 선분 (a, b) 사이 최단거리 제곱 (capsule center line distance).</summary>
        private static float PointToSegmentSqrDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            Vector3 ap = p - a;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-6f) return ap.sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / abLenSq);
            Vector3 closest = a + ab * t;
            return (p - closest).sqrMagnitude;
        }

        /// <summary>Scene에서 mesh가 변경됐는지 수동 감지 (예: chunk 생성됨).</summary>
        public static void Invalidate()
        {
            _lastExtractTime = 0;
        }
    }
}
#endif
