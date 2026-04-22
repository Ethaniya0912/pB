// =============================================================================
// BiomeMapDebugger.cs  |  pB-4 Week 2 — Day 2 T2.6
// 배치: Assets/Scripts/pB-4/week2_Tooling/BiomeMapDebugger.cs
//       (Editor 폴더 아님! MonoBehaviour이므로 일반 스크립트 위치)
//
// 역할: GameBlackboard.ActiveTerrainTags + GlobalIntensityScore를 Scene 뷰에 Gizmo로 표시.
//       지형팀이 Week 3부터 배치할 지형 태그를 Play 중 실시간 시각화.
//       Humanoid 팀도 GameBlackboard 데이터 디버깅에 활용.
//
// 사용법:
//   1. Scene의 아무 GameObject에 BiomeMapDebugger 컴포넌트 추가
//      (권장: "BiomeMapDebugger"라는 빈 GameObject 하나 생성 후 부착)
//   2. Inspector에서 visualRadius, drawTagLabels, drawIntensityHeatmap 설정
//   3. Play 모드 → Scene 뷰에서 Intensity 색상 Plane + 태그 Label 관찰
//
// 확장 아이디어 (Week 3+ 연계):
//   - 태그별 고유 색상 (해시 기반)
//   - 지형 Volume 여러 개 지원 (현재는 단일 위치)
//   - SceneView.duringSceneGui로 더 풍부한 오버레이
// =============================================================================
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Tooling
{
    public class BiomeMapDebugger : MonoBehaviour
    {
        [Header("━━━ 표시 옵션 ━━━━━━━━━━━━━━━━━━")]

        [Tooltip("Scene 뷰에 태그 텍스트 + Intensity 수치 표시.")]
        public bool drawTagLabels = true;

        [Tooltip("Global Intensity를 색상으로 표시 (빨강=높음, 초록=낮음).")]
        public bool drawIntensityHeatmap = true;

        [Tooltip("선택되지 않았을 때도 와이어프레임 경계 표시.")]
        public bool drawBoundsAlways = true;

        [Tooltip("표시 반경 (m). Plane/박스 크기.")]
        [Range(1f, 100f)] public float visualRadius = 30f;

        [Tooltip("Plane 높이 (기본 0.1 = 납작한 매트). 지형에 가려지지 않도록 약간 띄움.")]
        [Range(0.05f, 2f)] public float planeHeight = 0.1f;

        [Header("━━━ 색상 ━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("Intensity=0일 때 색상.")]
        public Color lowIntensityColor = new Color(0.2f, 0.8f, 0.2f, 0.3f);

        [Tooltip("Intensity=1일 때 색상.")]
        public Color highIntensityColor = new Color(0.8f, 0.2f, 0.2f, 0.5f);

        [Tooltip("태그 Label 배경 틴트.")]
        public Color labelBackgroundColor = new Color(0.08f, 0.10f, 0.15f, 0.85f);

        [Tooltip("태그 Label 텍스트 색상.")]
        public Color labelTextColor = new Color(0.0f, 0.9f, 1.0f, 1.0f);

        [Header("━━━ Debug (Read Only, Play 모드) ━━━")]

        [SerializeField] private string lastSeenTagsDebug = "(not playing)";
        [SerializeField] private float lastSeenIntensityDebug = 0f;

        // ==================================================================
        // Gizmo 렌더링
        // ==================================================================
        private void OnDrawGizmos()
        {
            // Play 모드가 아니면 와이어프레임만 (디자이너가 배치 위치 확인용)
            if (!Application.isPlaying)
            {
                if (drawBoundsAlways)
                {
                    Gizmos.color = new Color(0.4f, 0.5f, 0.6f, 0.6f);
                    Gizmos.DrawWireCube(transform.position, new Vector3(visualRadius, planeHeight, visualRadius));
                }
                return;
            }

            var bb = GameBlackboard.Instance;
            if (bb == null)
            {
                // Play 중인데 Blackboard 없음 → 핑크 경고
                Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.8f);
                Gizmos.DrawWireCube(transform.position, new Vector3(visualRadius, planeHeight, visualRadius));
#if UNITY_EDITOR
                if (drawTagLabels)
                {
                    var warnStyle = MakeLabelStyle(Color.magenta, 12, true);
                    UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                        "[BiomeMap] ⚠ GameBlackboard.Instance = null", warnStyle);
                }
#endif
                return;
            }

            // 디버그 필드 업데이트 (Inspector에서 관찰 가능)
            float intensity = Mathf.Clamp01(bb.GlobalIntensityScore);
            lastSeenIntensityDebug = intensity;
            lastSeenTagsDebug = (bb.ActiveTerrainTags != null && bb.ActiveTerrainTags.Count > 0)
                ? string.Join(", ", bb.ActiveTerrainTags)
                : "(empty)";

            // Intensity heatmap (지면 위 평평한 박스)
            if (drawIntensityHeatmap)
            {
                Gizmos.color = Color.Lerp(lowIntensityColor, highIntensityColor, intensity);
                Gizmos.DrawCube(transform.position, new Vector3(visualRadius, planeHeight, visualRadius));
            }

            // 와이어프레임 경계 (더 선명한 테두리)
            if (drawBoundsAlways)
            {
                Gizmos.color = Color.Lerp(
                    new Color(0.2f, 0.8f, 0.2f, 0.8f),
                    new Color(1.0f, 0.3f, 0.3f, 0.8f),
                    intensity);
                Gizmos.DrawWireCube(transform.position, new Vector3(visualRadius, planeHeight * 2f, visualRadius));
            }

#if UNITY_EDITOR
            // 태그 Label (UnityEditor.Handles API 필요)
            if (drawTagLabels)
            {
                var pos = transform.position + Vector3.up * (planeHeight + 1.5f);
                string display = BuildLabelText(bb);

                // 라벨 스타일: 어두운 배경 + 네온 시안 텍스트 (Dashboard 테마와 일관)
                var style = MakeLabelStyle(labelTextColor, 12, true);
                UnityEditor.Handles.Label(pos, display, style);
            }
#endif
        }

        private void OnDrawGizmosSelected()
        {
            // 선택 시 더 두꺼운 노란색 테두리
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(visualRadius, planeHeight * 3f, visualRadius));

            // 선택 시 중심 표시 (작은 구)
            Gizmos.color = new Color(1f, 1f, 0f, 0.7f);
            Gizmos.DrawSphere(transform.position, 0.5f);
        }

        // ==================================================================
        // 헬퍼
        // ==================================================================

        private string BuildLabelText(GameBlackboard bb)
        {
            var tags = bb.ActiveTerrainTags;
            string tagStr = (tags != null && tags.Count > 0)
                ? string.Join(", ", tags)
                : "(no tags)";

            return $"[Terrain Tags] {tagStr}\n" +
                   $"[Intensity]   {bb.GlobalIntensityScore:F2}\n" +
                   $"[Bounds]      {visualRadius:F0}m";
        }

#if UNITY_EDITOR
        private GUIStyle MakeLabelStyle(Color textColor, int fontSize, bool bold)
        {
            var style = new GUIStyle();
            style.normal.textColor = textColor;
            style.fontSize = fontSize;
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            style.padding = new RectOffset(6, 6, 4, 4);

            // 배경 텍스처 (어두운 반투명)
            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, labelBackgroundColor);
            bgTex.Apply();
            style.normal.background = bgTex;

            return style;
        }
#endif

        // ==================================================================
        // 런타임 API (다른 툴이 값을 조회할 때)
        // ==================================================================

        /// <summary>현재 관측 중인 태그 문자열 (Inspector 관찰용).</summary>
        public string LastSeenTags => lastSeenTagsDebug;

        /// <summary>현재 관측 중인 Intensity (Inspector 관찰용).</summary>
        public float LastSeenIntensity => lastSeenIntensityDebug;
    }
}
