using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-G Stage 3-C] RouteGeometry
    //
    // 공통 유틸리티 — Route A* 및 Spatial Hash에서 사용.
    //   - PackHalf3 / UnpackHalf3  : waypoint offset을 uint에 압축
    //   - SegmentToSegmentDistance : 3D capsule-capsule 충돌 검사 (P^2)
    //   - ComputeEdgeAABB          : waypoint 포함 tight AABB 계산
    //   - SegmentAABB              : 단일 segment AABB
    // ══════════════════════════════════════════════════════════════════════

    public static class RouteGeometry
    {
        // ─────────────────────────────────────────────────────────────────
        // Packing — Vector3 → uint (R10 / G11 / B11 유사, 0.01m step)
        //   X: 10 bits signed  → ±512 → ±5.12m
        //   Y: 11 bits signed  → ±1024 → ±10.24m
        //   Z: 11 bits signed  → ±1024 → ±10.24m
        //   Total: 32 bits → single uint
        //
        //   편향: Y를 가장 크게 — Stage 3-C는 "Y offset 경유" 많음 (Overpass)
        // ─────────────────────────────────────────────────────────────────

        public const float PackedUnit = 0.01f;
        public const float PackedMaxX = 5.11f;   // 511 × 0.01m
        public const float PackedMaxY = 10.23f;  // 1023 × 0.01m
        public const float PackedMaxZ = 10.23f;

        /// <summary>
        /// Vector3 offset (midpoint 기준 상대 좌표) → uint 압축.
        /// 범위 초과 시 clamp.
        /// </summary>
        public static uint PackHalf3(Vector3 offset)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(offset.x / PackedUnit), -512, 511);
            int y = Mathf.Clamp(Mathf.RoundToInt(offset.y / PackedUnit), -1024, 1023);
            int z = Mathf.Clamp(Mathf.RoundToInt(offset.z / PackedUnit), -1024, 1023);

            // Signed → Unsigned bit pattern (two's complement in N bits)
            uint ux = (uint)(x & 0x3FF);   // 10 bits mask
            uint uy = (uint)(y & 0x7FF);   // 11 bits mask
            uint uz = (uint)(z & 0x7FF);   // 11 bits mask

            return ux | (uy << 10) | (uz << 21);
        }

        /// <summary>
        /// uint → Vector3 offset (C# 역변환 검증용).
        /// Shader 쪽도 동일한 방식으로 unpack.
        /// </summary>
        public static Vector3 UnpackHalf3(uint packed)
        {
            int x = (int)(packed & 0x3FFu);
            if (x > 511) x -= 1024;  // sign extend (10 bit)

            int y = (int)((packed >> 10) & 0x7FFu);
            if (y > 1023) y -= 2048; // sign extend (11 bit)

            int z = (int)((packed >> 21) & 0x7FFu);
            if (z > 1023) z -= 2048; // sign extend (11 bit)

            return new Vector3(x * PackedUnit, y * PackedUnit, z * PackedUnit);
        }

        /// <summary>
        /// flags 필드에서 numWaypoints 추출 (lower 2 bits).
        /// </summary>
        public static int GetNumWaypoints(uint flags) => (int)(flags & 0x3u);

        /// <summary>
        /// numWaypoints 세팅 (lower 2 bits).
        /// </summary>
        public static uint SetNumWaypoints(uint flags, int count)
        {
            count = Mathf.Clamp(count, 0, 2);
            return (flags & ~0x3u) | (uint)count;
        }

        // ─────────────────────────────────────────────────────────────────
        // Edge 의 waypoint 실제 world 위치 계산 (midpoint + offset)
        // ─────────────────────────────────────────────────────────────────
        public static Vector3 ComputeWaypointWorld(EdgeData edge, int waypointIdx)
        {
            Vector3 mid = (edge.startPos + edge.endPos) * 0.5f;
            uint packed = (waypointIdx == 0) ? edge.w1_packed : edge.w2_packed;
            return mid + UnpackHalf3(packed);
        }

        // ─────────────────────────────────────────────────────────────────
        // Segment-to-Segment 3D 최단거리 (P^2 알고리즘)
        //   두 선분의 가장 가까운 점 쌍 간 거리를 반환.
        //   Collision 검사에 사용: distance < (width_a + width_b)/2 + buffer
        // ─────────────────────────────────────────────────────────────────
        public static float SegmentToSegmentDistance(
            Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            Vector3 d1 = a1 - a0;
            Vector3 d2 = b1 - b0;
            Vector3 r = a0 - b0;

            float A = Vector3.Dot(d1, d1);
            float E = Vector3.Dot(d2, d2);
            float F = Vector3.Dot(d2, r);

            float s, t;

            if (A < 1e-6f && E < 1e-6f)
                return Vector3.Distance(a0, b0);  // both degenerate

            if (A < 1e-6f)
            {
                s = 0f;
                t = Mathf.Clamp01(F / E);
            }
            else
            {
                float C = Vector3.Dot(d1, r);

                if (E < 1e-6f)
                {
                    s = Mathf.Clamp01(-C / A);
                    t = 0f;
                }
                else
                {
                    float B = Vector3.Dot(d1, d2);
                    float denom = A * E - B * B;

                    s = (denom != 0f) ? Mathf.Clamp01((B * F - C * E) / denom) : 0f;
                    t = (B * s + F) / E;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-C / A);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((B - C) / A);
                    }
                }
            }

            Vector3 p1 = a0 + d1 * s;
            Vector3 p2 = b0 + d2 * t;
            return Vector3.Distance(p1, p2);
        }

        // ─────────────────────────────────────────────────────────────────
        // Collision 검사 — segment가 obstacles (existing edges)와 충돌하는지
        // ─────────────────────────────────────────────────────────────────
        public static bool SegmentCollidesWith(
            Vector3 a, Vector3 b, float width,
            List<EdgeData> obstacles,
            float buffer = 2.0f)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                var obs = obstacles[i];
                int numWP = GetNumWaypoints(obs.flags);

                if (numWP == 0)
                {
                    // 직선 edge — single segment
                    if (SegmentCollisionCheck(a, b, width, obs.startPos, obs.endPos, obs.width, buffer))
                        return true;
                }
                else if (numWP == 1)
                {
                    Vector3 w1 = ComputeWaypointWorld(obs, 0);
                    if (SegmentCollisionCheck(a, b, width, obs.startPos, w1, obs.width, buffer)) return true;
                    if (SegmentCollisionCheck(a, b, width, w1, obs.endPos, obs.width, buffer)) return true;
                }
                else // numWP == 2
                {
                    Vector3 w1 = ComputeWaypointWorld(obs, 0);
                    Vector3 w2 = ComputeWaypointWorld(obs, 1);
                    if (SegmentCollisionCheck(a, b, width, obs.startPos, w1, obs.width, buffer)) return true;
                    if (SegmentCollisionCheck(a, b, width, w1, w2, obs.width, buffer)) return true;
                    if (SegmentCollisionCheck(a, b, width, w2, obs.endPos, obs.width, buffer)) return true;
                }
            }
            return false;
        }

        private static bool SegmentCollisionCheck(
            Vector3 a0, Vector3 a1, float widthA,
            Vector3 b0, Vector3 b1, float widthB,
            float buffer)
        {
            // 공유 endpoint 예외 — node 공유 시 collision 아님
            const float shareThreshold = 0.5f;
            if (Vector3.Distance(a0, b0) < shareThreshold) return false;
            if (Vector3.Distance(a0, b1) < shareThreshold) return false;
            if (Vector3.Distance(a1, b0) < shareThreshold) return false;
            if (Vector3.Distance(a1, b1) < shareThreshold) return false;

            float dist = SegmentToSegmentDistance(a0, a1, b0, b1);
            float minAllowed = (widthA + widthB) * 0.5f + buffer;
            return dist < minAllowed;
        }

        /// <summary>
        /// Node collision 검사 — segment가 관련 없는 노드 (sphere)와 충돌하는지.
        /// startNodeIdx, endNodeIdx의 노드는 제외 (자기 자신).
        /// transitNodeIdx도 제외 (flyby 허용).
        /// </summary>
        public static bool SegmentCollidesWithNodes(
            Vector3 a, Vector3 b, float width,
            List<NodeData> nodes,
            int skipA, int skipB, int skipTransit = -1,
            float buffer = 2.0f)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i == skipA || i == skipB || i == skipTransit) continue;
                var n = nodes[i];
                float dist = DistancePointToSegment(n.position, a, b);
                float minAllowed = n.radius + width * 0.5f + buffer;
                if (dist < minAllowed) return true;
            }
            return false;
        }

        public static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = Vector3.Dot(ab, ab);
            if (len2 < 1e-6f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return Vector3.Distance(p, a + ab * t);
        }

        // ─────────────────────────────────────────────────────────────────
        // Pre-computed AABB — edge 전체 (waypoint 포함) tight bounding box
        //   [Phase 4.5-G Stage 3-C, O1] pregen에서 계산, runtime 저장
        // ─────────────────────────────────────────────────────────────────
        public static void ComputeEdgeAABB(ref EdgeData edge, float sminMargin = 2.0f)
        {
            Vector3 min = Vector3.Min(edge.startPos, edge.endPos);
            Vector3 max = Vector3.Max(edge.startPos, edge.endPos);

            int numWP = GetNumWaypoints(edge.flags);
            if (numWP >= 1)
            {
                Vector3 w1 = ComputeWaypointWorld(edge, 0);
                min = Vector3.Min(min, w1);
                max = Vector3.Max(max, w1);
            }
            if (numWP >= 2)
            {
                Vector3 w2 = ComputeWaypointWorld(edge, 1);
                min = Vector3.Min(min, w2);
                max = Vector3.Max(max, w2);
            }

            // Width + smin margin 추가 (sdCapsule radius 커버)
            // CurvatureAmp도 offset 유발하므로 그만큼 확장
            float totalMargin = edge.width + sminMargin + edge.curvatureAmp;
            Vector3 marginVec = Vector3.one * totalMargin;

            edge.aabbMin = min - marginVec;
            edge.aabbMax = max + marginVec;
        }

        /// <summary>
        /// 전체 edge 리스트에 pre-computed AABB 적용.
        /// </summary>
        public static void BatchComputeAABBs(List<EdgeData> edges, float sminMargin = 2.0f)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                ComputeEdgeAABB(ref e, sminMargin);
                edges[i] = e;
            }
        }

        /// <summary>
        /// Point가 AABB 내부인지 (with margin).
        /// Shader 쪽과 동일 로직 — 검증용.
        /// </summary>
        public static bool PointInAABB(Vector3 p, Vector3 aabbMin, Vector3 aabbMax)
        {
            return p.x >= aabbMin.x && p.x <= aabbMax.x
                && p.y >= aabbMin.y && p.y <= aabbMax.y
                && p.z >= aabbMin.z && p.z <= aabbMax.z;
        }
    }
}
