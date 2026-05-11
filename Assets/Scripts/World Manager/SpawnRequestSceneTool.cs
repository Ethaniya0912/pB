// =============================================================================
// SpawnRequestSceneTool.cs  |  pB-4 Wk3 Editor Tool (★ v2.6 namespace 단순화)
// Layer  : L4 Editor / Scene Tool
// Namespace: TDA.PB4.AI  (메인 assembly 단방향 의존 회피 위해 Editor 분리 제거)
//
// 역할:
//   WorldAISpawnManager의 ContextMenu에서 호출하는 씬 클릭 배치 도구.
//   파일 전체가 #if UNITY_EDITOR로 보호 → Player Build에 포함되지 않음.
//   일반 폴더(예: Scripts/World Manager/)에 두되, Editor 폴더 분리 안 함.
//
// 사용 흐름:
//   Hierarchy에서 WorldAISpawnManager 선택 →
//   인스펙터 ⋮ 메뉴 → "★ Wk3 Test/Begin Click-Spawn" →
//   Scene 뷰에서 마우스 이동 (녹색 disc 마커 표시) →
//   원하는 위치 좌클릭 → 스폰 + 모드 OFF
//
// 참조: Issues Report v7 §11.5 + A1~A16 작업 명세 A9, A13
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TDA.PB4.AI
{
    public static class SpawnRequestSceneTool
    {
        // ── 상태 ──────────────────────────────────────────────
        private static WorldAISpawnManager _target;
        private static bool _active = false;
        private static string _selectedFactionId = "Skeleton";
        private static int _selectedMemberCount = 3;

        // ── 색상 ──────────────────────────────────────────────
        private static readonly Color COL_VALID = new Color(0.3f, 1.0f, 0.3f, 0.8f);
        private static readonly Color COL_INVALID = new Color(1.0f, 0.4f, 0.4f, 0.8f);
        private static readonly Color COL_PREVIEW = new Color(0.5f, 0.8f, 1.0f, 0.4f);

        // ─────────────────────────────────────────────────────
        // Public API — WorldAISpawnManager.ContextMenu에서 호출
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// 클릭 배치 모드 시작. 씬뷰에서 1회 좌클릭 → SpawnRequest 발사 후 자동 종료.
        /// </summary>
        public static void BeginClickPlacement(WorldAISpawnManager target)
        {
            if (target == null)
            {
                Debug.LogWarning("[SpawnTool] target == null");
                return;
            }

            // 매핑 검증
            var mapping = target.factionPrefabMappings.Find(m => m.factionId == _selectedFactionId);
            if (mapping == null || mapping.prefab == null)
            {
                // 첫 매핑으로 fallback
                if (target.factionPrefabMappings.Count > 0 && target.factionPrefabMappings[0].prefab != null)
                {
                    _selectedFactionId = target.factionPrefabMappings[0].factionId;
                    _selectedMemberCount = target.factionPrefabMappings[0].defaultMemberCount;
                    Debug.Log($"[SpawnTool] '{_selectedFactionId}' 매핑으로 자동 설정 (기본 {_selectedMemberCount}명)");
                }
                else
                {
                    Debug.LogError("[SpawnTool] WorldAISpawnManager.factionPrefabMappings가 비어있습니다. " +
                                   "Inspector에서 prefab을 등록하세요.");
                    return;
                }
            }

            _target = target;
            _active = true;

            // SceneView GUI hook 등록 (중복 방지)
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;

            Debug.Log($"<color=#88FFAA>[SpawnTool]</color> 클릭 배치 모드 ON " +
                      $"(faction={_selectedFactionId}, count={_selectedMemberCount}). " +
                      $"씬뷰에서 좌클릭하세요. ESC=취소.");
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 배치 모드 강제 취소.
        /// </summary>
        public static void CancelPlacement()
        {
            if (!_active) return;
            _active = false;
            _target = null;
            SceneView.duringSceneGui -= OnSceneGUI;
            Debug.Log("<color=#FFAA88>[SpawnTool]</color> 배치 모드 취소");
            SceneView.RepaintAll();
        }

        // ─────────────────────────────────────────────────────
        // SceneView GUI — 마우스 클릭 + 시각 피드백
        // ─────────────────────────────────────────────────────
        private static void OnSceneGUI(SceneView view)
        {
            if (!_active || _target == null)
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                _active = false;
                return;
            }

            Event e = Event.current;

            // ── ESC 취소 ──────────────────────────────────────
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelPlacement();
                e.Use();
                return;
            }

            // ── 마우스 위치 → 월드 좌표 ──────────────────────
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, 1000f);

            // ── 시각적 피드백 (마커) ──────────────────────────
            if (hasHit)
            {
                bool onNavMesh = UnityEngine.AI.NavMesh.SamplePosition(
                    hit.point, out var navHit, 2f, UnityEngine.AI.NavMesh.AllAreas);

                Color markerColor = onNavMesh ? COL_VALID : COL_INVALID;
                Handles.color = markerColor;

                Vector3 markerPos = onNavMesh ? navHit.position : hit.point;
                Handles.DrawWireDisc(markerPos, Vector3.up, 1.5f);
                Handles.DrawWireDisc(markerPos, Vector3.up, 0.5f);

                // 멤버 수만큼 미리보기 disc
                Handles.color = COL_PREVIEW;
                for (int i = 1; i < _selectedMemberCount; i++)
                {
                    float angle = (i / (float)_selectedMemberCount) * Mathf.PI * 2f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2f;
                    Handles.DrawWireDisc(markerPos + offset, Vector3.up, 0.4f);
                }

                // 텍스트 라벨
                Handles.BeginGUI();
                Vector3 screenPos = HandleUtility.WorldToGUIPoint(markerPos);
                var labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = onNavMesh ? Color.green : Color.red },
                    fontSize = 12,
                };
                GUI.Label(new Rect(screenPos.x + 15, screenPos.y - 30, 300, 40),
                          $"{_selectedFactionId} ×{_selectedMemberCount}\n" +
                          (onNavMesh ? "✓ NavMesh OK" : "✗ NavMesh 외부"),
                          labelStyle);
                Handles.EndGUI();

                view.Repaint();
            }

            // ── 좌클릭 → 스폰 발사 ─────────────────────────────
            if (e.type == EventType.MouseDown && e.button == 0 && hasHit)
            {
                // SpawnRequest 생성
                var req = new SpawnRequest
                {
                    factionId = _selectedFactionId,
                    locationStrategy = SpawnLocationStrategy.Explicit,
                    explicitPosition = hit.point,
                    hasExplicitPosition = true,
                    memberCount = _selectedMemberCount,
                    reason = SpawnReason.EditorClick,
                    priority = 1.0f,
                };

                // 발사
                var response = _target.ExecuteSpawnRequest(req);
                Debug.Log($"<color=#88FFAA>[SpawnTool]</color> SpawnRequest 발사 at {hit.point:F1} → {response}");

                // 이벤트 소모 + 모드 OFF
                e.Use();
                CancelPlacement();
            }
        }

        // ─────────────────────────────────────────────────────
        // Selection 설정 API (외부에서 미리 매핑 변경 가능)
        // ─────────────────────────────────────────────────────
        public static void SetFaction(string factionId, int memberCount = 3)
        {
            _selectedFactionId = factionId;
            _selectedMemberCount = Mathf.Clamp(memberCount, 1, 12);
        }

        public static bool IsActive => _active;
        public static string CurrentFaction => _selectedFactionId;
        public static int CurrentMemberCount => _selectedMemberCount;
    }
}
#endif