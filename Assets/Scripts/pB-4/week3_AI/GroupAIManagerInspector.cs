// =============================================================================
// GroupAIManagerInspector.cs  |  pB-4 Wk3 CustomEditor (★ v3.4 신규)
// Layer  : L4 Editor / Inspector Tool
// Namespace: TDA.PB4.AI
//
// 역할:
//   GroupAIManager 인스펙터 하단에 실시간 모니터링 영역 추가.
//   - Group Status: Combat Elapsed, Morale, Escalation, Tokens
//   - Members 표: Name / Role / Target / Token / AtkCnt / LastAtk / BTState
//   - Known Targets 표: Target / LastSeen / Spotter / Aware Members
//
// Play Mode 시에만 박제. Auto Refresh=ON이면 매 frame 갱신 (60fps).
//
// 위치: GroupAIManager.cs와 같은 폴더 (Editor 분리 안 함).
// 어셈블리 단방향 의존 회피 위해 #if UNITY_EDITOR 보호.
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using TDA.PB4.AI.Mob;
using TDA.PB4.Data;
using UnityEditor;
using UnityEngine;

namespace TDA.PB4.AI
{
    [CustomEditor(typeof(GroupAIManager))]
    public class GroupAIManagerInspector : UnityEditor.Editor
    {
        private bool _autoRefresh = true;
        private bool _showMembers = true;
        private bool _showKnownTargets = true;
        private bool _showGroupStatus = true;
        private bool _showPolicyInfo = false;
        private bool _showEventHistory = true;
        private Vector2 _eventScrollPos;
        private bool _filterLifecycle = true;
        private bool _filterMembership = true;
        private bool _filterToken = true;
        private bool _filterRole = true;
        private bool _filterSense = true;
        private bool _filterCombat = true;

        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _tableHeaderStyle;
        private GUIStyle _tableRowStyle;
        private GUIStyle _tableRowAltStyle;

        public override bool RequiresConstantRepaint() => _autoRefresh && Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var grp = target as GroupAIManager;
            if (grp == null) return;

            EnsureStyles();

            // 기본 인스펙터 (편집 가능 필드)
            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "🟡 Live Monitoring은 Play Mode에서만 박제됩니다.",
                    MessageType.Info);
                return;
            }

            // === Live Monitoring 영역 ===
            EditorGUILayout.LabelField("━━━ 🟢 Live Monitoring ━━━", _sectionHeaderStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                _autoRefresh = EditorGUILayout.ToggleLeft(
                    "Auto Refresh (60fps)", _autoRefresh, GUILayout.Width(180));
                if (GUILayout.Button("Refresh Now", GUILayout.Width(100)))
                {
                    Repaint();
                }
            }
            EditorGUILayout.Space(4);

            _showGroupStatus = EditorGUILayout.Foldout(_showGroupStatus, "📊 Group Status", true);
            if (_showGroupStatus) DrawGroupStatus(grp);

            EditorGUILayout.Space(4);
            _showPolicyInfo = EditorGUILayout.Foldout(_showPolicyInfo, "📋 Policy SO Info", true);
            if (_showPolicyInfo) DrawPolicyInfo(grp);

            EditorGUILayout.Space(4);
            _showMembers = EditorGUILayout.Foldout(
                _showMembers, $"👥 Members ({grp.ActiveMemberCount} / {grp.MemberCount})", true);
            if (_showMembers) DrawMembers(grp);

            EditorGUILayout.Space(4);
            _showKnownTargets = EditorGUILayout.Foldout(
                _showKnownTargets, $"🎯 Known Targets ({grp.KnownTargets.Count})", true);
            if (_showKnownTargets) DrawKnownTargets(grp);

            EditorGUILayout.Space(4);
            _showEventHistory = EditorGUILayout.Foldout(
                _showEventHistory, $"📜 Event History ({grp.EventHistory.Count})", true);
            if (_showEventHistory) DrawEventHistory(grp);
        }

        private void EnsureStyles()
        {
            if (_sectionHeaderStyle == null)
            {
                _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    normal = { textColor = new Color(0.5f, 1.0f, 0.7f) },
                };
            }
            if (_tableHeaderStyle == null)
            {
                _tableHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 10,
                };
            }
            if (_tableRowStyle == null)
            {
                _tableRowStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 10,
                    padding = new RectOffset(2, 2, 1, 1),
                };
            }
            if (_tableRowAltStyle == null)
            {
                _tableRowAltStyle = new GUIStyle(_tableRowStyle);
                _tableRowAltStyle.normal.background = MakeBg(new Color(0.2f, 0.2f, 0.2f, 0.3f));
            }
        }

        private static Texture2D MakeBg(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        // ──────────────────────────────────────────────────────────────────────
        private void DrawGroupStatus(GroupAIManager grp)
        {
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Combat Elapsed (s)", grp.CombatElapsedTime);
                EditorGUILayout.IntField("Escalation Level", grp.CurrentEscalationLevel);
                EditorGUILayout.Slider("Morale", grp.CurrentMorale, 0f, 1f);
                EditorGUILayout.LabelField("Active / Max Tokens",
                    $"{grp.ActiveTokenCount} / {grp.MaxTokenCount}");
                EditorGUILayout.FloatField("Token Reassign in (s)", grp.TokenReassignTimeRemaining);
                EditorGUILayout.Slider("Encircle Progress", grp.encircleProgress, 0f, 1f);

                string implType = grp.PolicyImplementor != null
                    ? grp.PolicyImplementor.GetType().Name
                    : "(none)";
                EditorGUILayout.LabelField("Policy Type", implType);

                EditorGUILayout.LabelField("Active Members",
                    $"{grp.ActiveMemberCount} alive / {grp.MemberCount} total");

                EditorGUILayout.ObjectField("Last Token Winner", grp.LastTokenWinner,
                    typeof(MobAIBrain), true);

                // [v3.4 §5 L1] Faction Safe Zone 박제
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("─ Safe Zone (§5 L1) ─", EditorStyles.miniBoldLabel);
                if (grp.HasFactionSafeZone)
                {
                    EditorGUILayout.Vector3Field("Faction Safe Zone", grp.FactionSafeZone);
                }
                else
                {
                    EditorGUILayout.LabelField("Faction Safe Zone", "(미설정)");
                }
            }

            // Auto Focus 모드 (편집 가능)
            EditorGUILayout.Space(2);
            using (new EditorGUI.IndentLevelScope())
            {
                var newMode = (GroupAIManager.FocusCameraMode)EditorGUILayout.EnumPopup(
                    new GUIContent("📷 Focus Camera Mode",
                        "토큰 가장 최근 획득 멤버 기준 SceneView 카메라 자동 추적 모드.\n" +
                        "1: AI 정면 — AI 정면이 카메라 향함\n" +
                        "2: AI+타겟 탑뷰 — 두 캐릭터 중간 위에서 내려다봄\n" +
                        "3: AI 숄더 (거리 고정) — AI 등 너머 타겟. 타겟 멀어져도 AI↔카메라 거리 일정\n" +
                        "4: 그룹 탑뷰 — 그룹 전체를 위에서 내려다봄\n" +
                        "5: 그룹 숄더 → 타겟 — 그룹 중심에서 타겟 향함"),
                    grp.focusMode);
                if (newMode != grp.focusMode)
                {
                    grp.focusMode = newMode;
                    EditorUtility.SetDirty(grp);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        private void DrawPolicyInfo(GroupAIManager grp)
        {
            var so = grp.PolicySO;
            if (so == null)
            {
                EditorGUILayout.HelpBox("Policy SO 미할당.", MessageType.Warning);
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Asset", so, typeof(FactionGroupPolicySO), false);
                EditorGUILayout.LabelField("Asset Name", so.name);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("─ Core ─", EditorStyles.miniBoldLabel);
                EditorGUILayout.FloatField("Panic Chain Multiplier", so.panicChainMultiplier);
                EditorGUILayout.FloatField("Leader Influence Radius", so.leaderInfluenceRadius);
                EditorGUILayout.LabelField("Escalation Mode", so.escalationMode.ToString());
                EditorGUILayout.LabelField("Formation Template", so.formationTemplate.ToString());

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("─ Token Override ─", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Override Enabled", so.tokenOverrideEnabled.ToString());
                EditorGUILayout.FloatField("Override Threshold", so.tokenOverrideThreshold);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("─ Shared Target (§4 L1+L2) ─", EditorStyles.miniBoldLabel);
                EditorGUILayout.FloatField("Close Radius (m)", so.sharedTargetCloseRadius);
                EditorGUILayout.FloatField("Chain Max Radius (m)", so.sharedTargetChainMaxRadius);
                EditorGUILayout.FloatField("Timeout (s)", so.sharedTargetTimeout);
                EditorGUILayout.LabelField("Sight Obstacle Layers", LayerMaskToString(so.sharedTargetSightObstacleLayers));

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("─ Role Transitions ─", EditorStyles.miniBoldLabel);
                int ruleCount = so.roleTransitionRules != null ? so.roleTransitionRules.Count : 0;
                EditorGUILayout.IntField("Rule Count", ruleCount);
            }
        }

        private static string LayerMaskToString(LayerMask mask)
        {
            if (mask.value == 0) return "(none)";
            if (mask.value == ~0) return "Everything";
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                if ((mask.value & (1 << i)) != 0)
                {
                    string n = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
            }
            return names.Count == 0 ? mask.value.ToString() : string.Join(", ", names);
        }

        // ──────────────────────────────────────────────────────────────────────
        private void DrawMembers(GroupAIManager grp)
        {
            // 표 헤더
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Name", _tableHeaderStyle, GUILayout.Width(120));
                GUILayout.Label("Role", _tableHeaderStyle, GUILayout.Width(54));
                GUILayout.Label("BT State", _tableHeaderStyle, GUILayout.Width(60));
                GUILayout.Label("Target", _tableHeaderStyle, GUILayout.Width(105));
                GUILayout.Label("Dist", _tableHeaderStyle, GUILayout.Width(40));
                GUILayout.Label("Tk", _tableHeaderStyle, GUILayout.Width(20));
                GUILayout.Label("Atk#", _tableHeaderStyle, GUILayout.Width(34));
                GUILayout.Label("LastAtk", _tableHeaderStyle, GUILayout.Width(48));
                GUILayout.Label("Safe#", _tableHeaderStyle, GUILayout.Width(38));
            }

            if (grp.MemberCount == 0)
            {
                EditorGUILayout.LabelField("  (멤버 없음)");
                return;
            }

            int idx = 0;
            foreach (var m in grp.Members)
            {
                if (m == null || m.brain == null)
                {
                    EditorGUILayout.LabelField("  (null)");
                    idx++;
                    continue;
                }

                GUIStyle rowStyle = (idx % 2 == 0) ? _tableRowStyle : _tableRowAltStyle;
                using (new EditorGUILayout.HorizontalScope(rowStyle))
                {
                    if (GUILayout.Button(m.brain.name, EditorStyles.linkLabel, GUILayout.Width(120)))
                    {
                        EditorGUIUtility.PingObject(m.brain.gameObject);
                        Selection.activeGameObject = m.brain.gameObject;
                    }

                    GUILayout.Label(m.currentRole.ToString(), rowStyle, GUILayout.Width(54));

                    // BT State — PB4DecisionAdapter.LastPB4State
                    string btState = "—";
                    var adapter = m.brain.GetComponent<TDA.PB4.Bridge.PB4DecisionAdapter>();
                    if (adapter != null) btState = adapter.LastPB4State;
                    Color prevColor = GUI.color;
                    Color btColor = ColorForBTState(btState);
                    GUI.color = btColor;
                    GUILayout.Label(btState, rowStyle, GUILayout.Width(60));
                    GUI.color = prevColor;

                    string targetName = m.brain.currentTarget != null
                        ? m.brain.currentTarget.name
                        : "—";
                    if (m.brain.currentTarget == null) GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    GUILayout.Label(targetName, rowStyle, GUILayout.Width(105));
                    GUI.color = prevColor;

                    string distStr = "—";
                    if (m.brain.currentTarget != null)
                    {
                        float d = Vector3.Distance(m.brain.transform.position,
                            m.brain.currentTarget.position);
                        distStr = $"{d:F1}m";
                    }
                    GUILayout.Label(distStr, rowStyle, GUILayout.Width(40));

                    string tokenStr = m.hasAttackToken ? "✔" : "";
                    Color tokenColor = m.hasAttackToken
                        ? new Color(0.5f, 1.0f, 0.5f)
                        : Color.gray;
                    GUI.color = tokenColor;
                    GUILayout.Label(tokenStr, rowStyle, GUILayout.Width(20));
                    GUI.color = prevColor;

                    GUILayout.Label(m.attackCount.ToString(), rowStyle, GUILayout.Width(34));
                    GUILayout.Label($"{m.timeSinceLastAttack:F1}s", rowStyle, GUILayout.Width(48));

                    // [v3.4 §5 L1] Safe Zone count
                    int safeCount = m.brain.LastSafePositions != null ? m.brain.LastSafePositions.Count : 0;
                    GUILayout.Label(safeCount.ToString(), rowStyle, GUILayout.Width(38));
                }
                idx++;
            }
        }

        private static Color ColorForBTState(string state)
        {
            switch (state)
            {
                case "Attack": return new Color(1.0f, 0.5f, 0.5f);   // 빨강
                case "Flee": return new Color(1.0f, 0.85f, 0.3f);  // 노랑
                case "Patrol": return new Color(0.6f, 0.85f, 1.0f);  // 파랑
                case "Idle": return new Color(0.7f, 0.7f, 0.7f);   // 회색
                default: return Color.white;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        private void DrawKnownTargets(GroupAIManager grp)
        {
            // 표 헤더
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Target", _tableHeaderStyle, GUILayout.Width(130));
                GUILayout.Label("LastSeen", _tableHeaderStyle, GUILayout.Width(60));
                GUILayout.Label("Age", _tableHeaderStyle, GUILayout.Width(50));
                GUILayout.Label("Spotter", _tableHeaderStyle, GUILayout.Width(110));
                GUILayout.Label("Aware Members", _tableHeaderStyle, GUILayout.MinWidth(120));
            }

            if (grp.KnownTargets.Count == 0)
            {
                EditorGUILayout.LabelField("  (알려진 적 없음)");
                return;
            }

            int idx = 0;
            foreach (var slot in grp.KnownTargets)
            {
                if (slot == null) { idx++; continue; }

                GUIStyle rowStyle = (idx % 2 == 0) ? _tableRowStyle : _tableRowAltStyle;
                using (new EditorGUILayout.HorizontalScope(rowStyle))
                {
                    if (slot.target == null)
                    {
                        GUILayout.Label("(null target)", rowStyle, GUILayout.Width(130));
                    }
                    else
                    {
                        if (GUILayout.Button(slot.target.name, EditorStyles.linkLabel, GUILayout.Width(130)))
                        {
                            EditorGUIUtility.PingObject(slot.target.gameObject);
                            Selection.activeGameObject = slot.target.gameObject;
                        }
                    }

                    float lastSeenAge = Time.time - slot.lastSeenTime;
                    Color prevColor = GUI.color;
                    if (lastSeenAge > 5f) GUI.color = new Color(1.0f, 0.7f, 0.5f);
                    GUILayout.Label($"{lastSeenAge:F1}s", rowStyle, GUILayout.Width(60));
                    GUI.color = prevColor;

                    // First Spot Time (slot이 처음 만들어진 후 경과)
                    float firstSpotAge = Time.time - slot.firstSpotTime;
                    GUILayout.Label($"{firstSpotAge:F0}s", rowStyle, GUILayout.Width(50));

                    string spotter = slot.firstSpotter != null ? slot.firstSpotter.name : "—";
                    GUILayout.Label(spotter, rowStyle, GUILayout.Width(110));

                    string aware = "—";
                    if (slot.awareMembers != null && slot.awareMembers.Count > 0)
                    {
                        var names = new List<string>(slot.awareMembers.Count);
                        foreach (var am in slot.awareMembers)
                            names.Add(am != null ? am.name : "?");
                        aware = string.Join(", ", names);
                    }
                    GUILayout.Label(aware, rowStyle, GUILayout.MinWidth(120));
                }
                idx++;
            }
        }
        // ──────────────────────────────────────────────────────────────────────
        private void DrawEventHistory(GroupAIManager grp)
        {
            // 카테고리 필터 toolbar
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _filterLifecycle = GUILayout.Toggle(_filterLifecycle, "Lifecycle", EditorStyles.toolbarButton);
                _filterMembership = GUILayout.Toggle(_filterMembership, "Member", EditorStyles.toolbarButton);
                _filterToken = GUILayout.Toggle(_filterToken, "Token", EditorStyles.toolbarButton);
                _filterRole = GUILayout.Toggle(_filterRole, "Role", EditorStyles.toolbarButton);
                _filterSense = GUILayout.Toggle(_filterSense, "Sense", EditorStyles.toolbarButton);
                _filterCombat = GUILayout.Toggle(_filterCombat, "Combat", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    var so = new SerializedObject(grp);
                    grp.SendMessage("ClearEventHistory", SendMessageOptions.DontRequireReceiver);
                }
            }

            if (grp.EventHistory.Count == 0)
            {
                EditorGUILayout.LabelField("  (이벤트 없음)");
                return;
            }

            // 스크롤 영역 — 최대 200px (약 12행)
            using (var scroll = new EditorGUILayout.ScrollViewScope(_eventScrollPos,
                GUILayout.MaxHeight(200), GUILayout.MinHeight(60)))
            {
                _eventScrollPos = scroll.scrollPosition;

                int idx = 0;
                // 최근 이벤트가 위에 오도록 역순
                for (int i = grp.EventHistory.Count - 1; i >= 0; i--)
                {
                    var ev = grp.EventHistory[i];
                    if (!IsCategoryEnabled(ev.category)) continue;

                    GUIStyle rowStyle = (idx % 2 == 0) ? _tableRowStyle : _tableRowAltStyle;
                    using (new EditorGUILayout.HorizontalScope(rowStyle))
                    {
                        // 시간 (HH:MM:SS 형식 — 그룹 생성 후 경과)
                        string timeStr = FormatRelativeTime(ev.relativeTime);
                        Color prevColor = GUI.color;
                        GUI.color = new Color(0.7f, 0.7f, 0.7f);
                        GUILayout.Label(timeStr, rowStyle, GUILayout.Width(70));
                        GUI.color = prevColor;

                        // 카테고리 태그 (색상 강조)
                        GUI.color = ev.tagColor;
                        GUILayout.Label($"[{ev.category}]", rowStyle, GUILayout.Width(80));
                        GUI.color = prevColor;

                        // 메시지
                        GUILayout.Label(ev.message, rowStyle, GUILayout.MinWidth(200));
                    }
                    idx++;
                }
            }
        }

        private bool IsCategoryEnabled(string cat)
        {
            switch (cat)
            {
                case "Lifecycle": return _filterLifecycle;
                case "Membership": return _filterMembership;
                case "Token": return _filterToken;
                case "Role": return _filterRole;
                case "Sense": return _filterSense;
                case "Combat": return _filterCombat;
                default: return true;
            }
        }

        private static string FormatRelativeTime(float seconds)
        {
            int totalSec = Mathf.FloorToInt(seconds);
            int h = totalSec / 3600;
            int m = (totalSec % 3600) / 60;
            int s = totalSec % 60;
            return $"{h:D2}:{m:D2}:{s:D2}";
        }
    }
}
#endif