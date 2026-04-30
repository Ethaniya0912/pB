using UnityEngine;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase E-α.5] EdgeConstraintSO
    //
    // 목적:
    //   Edge 생성 시 특정 방향/경사 선호. Case별 지질학적 경향 반영.
    //
    //   예:
    //     Case 2 Sedimentary: 수평 방향 강 선호 (층리 따라 통로 형성)
    //     Case 5 Canyon:      수직 경사 허용 (협곡 drop)
    //     Case 3 Colonnade:   중립 (모든 방향 균등)
    //     Case 4 Rocky:       불규칙 (랜덤성 최대)
    //
    // 구조:
    //   preferredDirection: Vector3 (편향 방향 벡터, magnitude 0=중립)
    //   directionStrength:  float   (선호 강도 0~1)
    //   allowVerticalEdges: bool    (수직 드롭 허용)
    //   maxSlopeOverride:   float   (-1=CaveBiomeData.maxSlope 사용)
    //
    // 현재 상태:
    //   ✓ SO 정의 + Inspector 편집 가능
    //   ○ Phase 4-2 E-α.6에서 실제 소비 (NodeGraphBuilder edge 필터링)
    //   ○ Shader 영향 없음 (구조적 선택, 렌더링 무관)
    //
    // 규칙 #6:
    //   null / directionStrength=0 → 기존 NodeGraph 생성 경로 (등방성)
    // ══════════════════════════════════════════════════════════════════════

    [CreateAssetMenu(fileName = "NewEdgeConstraint", menuName = "Cave System/Edge Constraint", order = 13)]
    public class EdgeConstraintSO : ScriptableObject
    {
        [Header("Identity")]
        public string constraintName = "Default Constraint";

        // ═══════════════════════════════════════
        // Direction Preference
        // ═══════════════════════════════════════
        [Header("Direction Preference")]
        [Tooltip("선호 방향 벡터. magnitude 0 = 중립 (모든 방향 균등). " +
                 "예: Vector3(1, 0, 1).normalized = 대각 XZ 선호 (Canyon-like). " +
                 "Vector3(0, 0, 1) = +Z 방향 강 선호.")]
        public Vector3 preferredDirection = Vector3.zero;

        [Tooltip("방향 선호 강도. 0=무효, 1=최대 편향. " +
                 "내부 계산: edge vector와 preferredDirection의 dot × strength.")]
        [Range(0f, 1f)]
        public float directionStrength = 0f;

        // ═══════════════════════════════════════
        // Slope Constraint
        // ═══════════════════════════════════════
        [Header("Slope Constraint")]
        [Tooltip("수직/near-수직 edge 허용 여부. OFF: Δy/Δxz ≤ 2 강제. ON: 어떤 경사도 허용.")]
        public bool allowVerticalEdges = false;

        [Tooltip("max slope override (°). -1 = CaveBiomeData.maxSlope 사용. " +
                 "양수 값이면 이 값으로 덮어씀.")]
        public float maxSlopeOverride = -1f;

        // ═══════════════════════════════════════
        // Length Constraint
        // ═══════════════════════════════════════
        [Header("Length Constraint")]
        [Tooltip("최소 edge 길이 (m). 너무 짧은 edge 거부 (노드 밀집 방지).")]
        [Range(0f, 30f)]
        public float minEdgeLength = 5f;

        [Tooltip("최대 edge 길이 (m). 너무 긴 edge 거부 (지질적 비현실).")]
        [Range(10f, 200f)]
        public float maxEdgeLength = 100f;

        // ═══════════════════════════════════════
        // Editor Event
        // ═══════════════════════════════════════
        public static event System.Action OnConstraintModified;

        private void OnValidate()
        {
            if (Application.isPlaying) OnConstraintModified?.Invoke();
        }

        // ═══════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════

        /// <summary>
        /// Edge 방향이 preferredDirection과 얼마나 일치하는지 점수 (0~1).
        /// directionStrength=0 이면 항상 1.0 반환 (영향 없음).
        /// </summary>
        public float EvaluateDirectionScore(Vector3 edgeDir)
        {
            if (directionStrength <= 0.001f) return 1f;
            if (preferredDirection.sqrMagnitude < 0.001f) return 1f;

            Vector3 normDir = preferredDirection.normalized;
            float dotVal = Vector3.Dot(edgeDir.normalized, normDir);
            // dot [-1, 1] → [0, 1] 매핑 + strength 가중
            float score01 = (dotVal + 1f) * 0.5f;
            return Mathf.Lerp(1f, score01, directionStrength);
        }

        /// <summary>
        /// Edge 길이 허용 범위 체크.
        /// </summary>
        public bool IsLengthValid(float length)
        {
            return length >= minEdgeLength && length <= maxEdgeLength;
        }

        /// <summary>
        /// Edge slope이 이 constraint에서 허용되는지.
        /// caveBiomeMaxSlope는 CaveBiomeData.maxSlope에서 전달.
        /// </summary>
        public bool IsSlopeValid(float slopeDeg, float caveBiomeMaxSlope)
        {
            float limit = (maxSlopeOverride > 0f) ? maxSlopeOverride : caveBiomeMaxSlope;
            if (allowVerticalEdges) return slopeDeg <= 89f;
            return slopeDeg <= limit;
        }
    }
}
