#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using TDA.Character;
using TDA.Character.AI;
using TDA.Character.Player;
using System.Reflection;

namespace TDA.EditorTools
{
    public enum SortOption
    {
        None,
        Distance,
        HP,
        Stamina,
        Team,
        Target_Exists,
        AI_State
    }

    /// <summary>
    /// 월드에 스폰된 몹, 플레이어, NPC의 상태를 실시간으로 모니터링하고 제어하는 통합 디버거 툴입니다.
    /// 상단 메뉴 TDA -> World Character Debugger 를 통해 열 수 있습니다.
    /// </summary>
    public class WorldCharacterDebuggerWindow : EditorWindow
    {
        private int currentTab = 0;
        private string[] tabs = { "👾 몹 (Mobs)", "👤 플레이어 (Players)", "🗣️ NPC" };
        private Vector2 scrollPosition;

        // 정렬 관련 변수
        private SortOption currentSortOption = SortOption.None;
        private bool isSortAscending = true;

        // AI 상태(State) 강제 변경을 위한 에셋 캐싱
        private List<AIState> allAIStates = new List<AIState>();
        private string[] allAIStateNames;

        // 데이터 추적용 딕셔너리
        private Dictionary<int, int> selectedStateIndices = new Dictionary<int, int>();
        private Dictionary<int, float> mockDamageDealtTracker = new Dictionary<int, float>();

        // [신규] 받은 데미지 추적용 딕셔너리 (HP 변화량 폴링)
        private Dictionary<int, float> lastHpTracker = new Dictionary<int, float>();
        private Dictionary<int, float> totalDamageTakenTracker = new Dictionary<int, float>();

        // UI 스타일
        private GUIStyle clippedStyle;
        private GUIStyle boldClippedStyle;
        private GUIStyle cardStyle;

        [MenuItem("TDA/World Character Debugger", false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<WorldCharacterDebuggerWindow>("월드 캐릭터 디버거");
            window.minSize = new Vector2(1000, 600);
            window.Show();
        }

        private void OnEnable()
        {
            LoadAllAIStates();
        }

        private void LoadAllAIStates()
        {
            allAIStates.Clear();
            string[] guids = AssetDatabase.FindAssets("t:AIState");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AIState state = AssetDatabase.LoadAssetAtPath<AIState>(path);
                if (state != null && !allAIStates.Contains(state))
                {
                    allAIStates.Add(state);
                }
            }
            allAIStateNames = allAIStates.Select(s => s.name).ToArray();
        }

        private void InitializeStyles()
        {
            if (clippedStyle == null)
            {
                clippedStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = false, clipping = TextClipping.Clip };
                boldClippedStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true, wordWrap = false, clipping = TextClipping.Clip };
                cardStyle = new GUIStyle("box") { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(5, 5, 5, 5) };
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("게임을 실행(Play Mode)해야 월드에 스폰된 캐릭터 목록을 볼 수 있습니다.", MessageType.Warning);
                return;
            }

            InitializeStyles();
            Repaint(); // 실시간 갱신

            DrawHeader();

            currentTab = GUILayout.Toolbar(currentTab, tabs, GUILayout.Height(30));

            DrawSortAndFilterUI();

            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            switch (currentTab)
            {
                case 0: DrawCharacterList<AICharacterManager>(false); break;
                case 1: DrawCharacterList<PlayerManager>(true); break;
                case 2: DrawCharacterList<CharacterManager>(false, true); break;
            }

            EditorGUILayout.EndScrollView();

            DrawBulkActions();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal("box");
            GUILayout.Label("🌐 월드 실시간 모니터링", EditorStyles.boldLabel);
            if (GUILayout.Button("🔄 AI State 목록 새로고침", GUILayout.Width(150)))
            {
                LoadAllAIStates();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSortAndFilterUI()
        {
            EditorGUILayout.BeginHorizontal("box");
            GUILayout.Label("🗂️ 정렬 기준:", EditorStyles.boldLabel, GUILayout.Width(80));

            currentSortOption = (SortOption)EditorGUILayout.EnumPopup(currentSortOption, GUILayout.Width(150));

            if (GUILayout.Button(isSortAscending ? "▲ 오름차순" : "▼ 내림차순", GUILayout.Width(100)))
            {
                isSortAscending = !isSortAscending;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCharacterList<T>(bool isPlayerTab, bool isNPCTab = false) where T : CharacterManager
        {
            T[] characters = FindObjectsByType<T>(FindObjectsSortMode.None);

            IEnumerable<T> filteredChars = characters;
            if (isNPCTab)
            {
                filteredChars = characters.Where(c => !(c is PlayerManager) && !(c is AICharacterManager));
            }

            int count = filteredChars.Count();
            if (count == 0)
            {
                EditorGUILayout.HelpBox($"현재 월드에 스폰된 {tabs[currentTab]}가 없습니다.", MessageType.Info);
                return;
            }

            IEnumerable<T> sortedChars = filteredChars;
            switch (currentSortOption)
            {
                case SortOption.Distance:
                    sortedChars = isSortAscending ? filteredChars.OrderBy(c => GetDistanceToNearestPlayer(c)) : filteredChars.OrderByDescending(c => GetDistanceToNearestPlayer(c));
                    break;
                case SortOption.HP:
                    sortedChars = isSortAscending ? filteredChars.OrderBy(c => c.characterNetworkManager.currentHealth.Value) : filteredChars.OrderByDescending(c => c.characterNetworkManager.currentHealth.Value);
                    break;
                case SortOption.Stamina:
                    sortedChars = isSortAscending ? filteredChars.OrderBy(c => c.characterNetworkManager.currentStamina.Value) : filteredChars.OrderByDescending(c => c.characterNetworkManager.currentStamina.Value);
                    break;
                case SortOption.Team:
                    sortedChars = isSortAscending ? filteredChars.OrderBy(c => c.characterGroup.ToString()) : filteredChars.OrderByDescending(c => c.characterGroup.ToString());
                    break;
                case SortOption.Target_Exists:
                    sortedChars = isSortAscending ? filteredChars.OrderBy(c => c.characterCombatManager?.currentTarget != null ? 1 : 0) : filteredChars.OrderByDescending(c => c.characterCombatManager?.currentTarget != null ? 1 : 0);
                    break;
                case SortOption.AI_State:
                    sortedChars = isSortAscending ? filteredChars.OrderBy(c => GetCurrentStateName(c)) : filteredChars.OrderByDescending(c => GetCurrentStateName(c));
                    break;
            }

            foreach (T c in sortedChars)
            {
                DrawCharacterCard(c, isPlayerTab);
            }
        }

        private void DrawCharacterCard(CharacterManager c, bool isPlayerTab)
        {
            if (c.characterNetworkManager == null) return;

            int instanceId = c.GetInstanceID();
            bool isDead = c.characterNetworkManager.isDead.Value;

            float curHP = c.characterNetworkManager.currentHealth.Value;
            float maxHP = c.characterNetworkManager.maxHealth.Value;
            float curSP = c.characterNetworkManager.currentStamina.Value;
            float maxSP = c.characterNetworkManager.maxStamina.Value;

            // =========================================================
            // [신규] 받은 데미지 계산 (매 프레임 폴링)
            // =========================================================
            if (!lastHpTracker.ContainsKey(instanceId))
            {
                lastHpTracker[instanceId] = curHP;
                totalDamageTakenTracker[instanceId] = 0f;
                mockDamageDealtTracker[instanceId] = 0f;
            }
            else
            {
                // 이전 프레임보다 HP가 깎였다면 데미지를 입은 것으로 간주하여 누적
                if (curHP < lastHpTracker[instanceId])
                {
                    totalDamageTakenTracker[instanceId] += (lastHpTracker[instanceId] - curHP);
                }
                lastHpTracker[instanceId] = curHP; // 현재 HP로 갱신
            }

            // 전체 카드 래퍼
            EditorGUILayout.BeginVertical(cardStyle);

            // ---------------------------------------------------------
            // ROW 1: 헤더 (이모지, 이름, 단축 버튼, 회복/이동)
            // ---------------------------------------------------------
            EditorGUILayout.BeginHorizontal();

            string emoji = GetStateEmoji(c);
            GUILayout.Label(emoji, new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter }, GUILayout.Width(25));
            GUILayout.Label($"<b>{c.gameObject.name}</b>", boldClippedStyle, GUILayout.Width(160));

            // [신규] 하이어라키 선택 버튼
            if (GUILayout.Button("🔍 선택", GUILayout.Width(60), GUILayout.Height(20)))
            {
                Selection.activeGameObject = c.gameObject;
                EditorGUIUtility.PingObject(c.gameObject); // 하이어라키에서 노란색으로 깜빡임
            }

            // [신규] 즉사 버튼
            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUILayout.Button("💀 즉사", GUILayout.Width(60), GUILayout.Height(20)))
            {
                c.characterNetworkManager.currentHealth.Value = 0;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace(); // 우측 정렬을 위한 빈 공간

            // 퀵 액션 버튼들
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("HP 회복", GUILayout.Width(65))) RecoverHP(c);

            GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button("SP 회복", GUILayout.Width(65))) RecoverSP(c);

            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("유저에 순간이동", GUILayout.Width(105))) TeleportToNearestPlayer(c);
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ---------------------------------------------------------
            // ROW 2: 기본 스탯 (HP, SP, 팀, 거리, 리젠)
            // ---------------------------------------------------------
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(30); // 이모지 간격 확보

            GUILayout.Label($"<color=#ff4757>HP: {curHP:F0}/{maxHP}</color>", clippedStyle, GUILayout.Width(100));
            GUILayout.Label($"<color=#2ed573>SP: {curSP:F0}/{maxSP}</color>", clippedStyle, GUILayout.Width(100));

            GUILayout.Label("팀:", clippedStyle, GUILayout.Width(20));
            EditorGUI.BeginChangeCheck();
            CharacterGroup newGroup = (CharacterGroup)EditorGUILayout.EnumPopup(c.characterGroup, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck()) c.characterGroup = newGroup;

            if (c.characterStatsManager != null)
            {
                GUILayout.Label("SP리젠:", clippedStyle, GUILayout.Width(50));
                c.characterStatsManager.staminaRegenerationAmount = (int)EditorGUILayout.FloatField(c.characterStatsManager.staminaRegenerationAmount, GUILayout.Width(40));
            }
            else
            {
                GUILayout.Space(94); // Layout Alignment
            }

            float dist = GetDistanceToNearestPlayer(c);
            string distText = dist < float.MaxValue ? $"{dist:F1}m" : "N/A";
            GUILayout.Label($"<color=#1e90ff>👤거리: {distText}</color>", clippedStyle, GUILayout.Width(90));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ---------------------------------------------------------
            // ROW 3: 전투 정보 (타겟, 애니메이션, 데미지)
            // ---------------------------------------------------------
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(30);

            string targetName = c.characterCombatManager?.currentTarget != null ? c.characterCombatManager.currentTarget.name : "None";
            GUILayout.Label($"🎯타겟: {targetName}", clippedStyle, GUILayout.Width(150));

            string animState = GetCurrentAnimationState(c);
            GUILayout.Label($"🎬Anim: {animState}", clippedStyle, GUILayout.Width(180));

            GUILayout.Label($"⚔️가한 Dmg: {mockDamageDealtTracker[instanceId]:F0}", clippedStyle, GUILayout.Width(110));
            GUILayout.Label($"🩸받은 Dmg: <color=#ff7675>{totalDamageTakenTracker[instanceId]:F0}</color>", clippedStyle, GUILayout.Width(110));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // ---------------------------------------------------------
            // ROW 4: AI State 제어 (몹 탭 전용)
            // ---------------------------------------------------------
            if (c is AICharacterManager aiManager && allAIStateNames != null && allAIStateNames.Length > 0)
            {
                GUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(30);

                if (!selectedStateIndices.ContainsKey(instanceId)) selectedStateIndices[instanceId] = 0;

                GUILayout.Label("🧠 현재 State:", clippedStyle, GUILayout.Width(80));

                string stateName = GetCurrentStateName(c);
                GUILayout.Label($"<color=orange>{stateName}</color>", clippedStyle, GUILayout.Width(150));

                selectedStateIndices[instanceId] = EditorGUILayout.Popup(selectedStateIndices[instanceId], allAIStateNames, GUILayout.Width(150));

                GUI.backgroundColor = new Color(1f, 0.8f, 0.4f);
                if (GUILayout.Button("상태 강제 덮어쓰기", GUILayout.Width(120)))
                {
                    AIState newState = allAIStates[selectedStateIndices[instanceId]];
                    FieldInfo stateField = typeof(AICharacterManager).GetField("currentState", BindingFlags.NonPublic | BindingFlags.Instance);

                    if (stateField != null && newState != null)
                    {
                        stateField.SetValue(aiManager, newState);
                        Debug.Log($"[디버거] {aiManager.name}의 상태를 {newState.name}(으)로 강제 변경했습니다.");
                    }
                }
                GUI.backgroundColor = Color.white;

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical(); // 전체 카드 종료
        }

        private void DrawBulkActions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("⚡ 일괄 명령 (현재 탭의 모든 캐릭터에게 적용)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("일괄 HP 회복", GUILayout.Height(30))) BulkRecoverHP();

            GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button("일괄 SP 회복", GUILayout.Height(30))) BulkRecoverSP();

            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("일괄 플레이어에게 소환", GUILayout.Height(30))) BulkTeleportToPlayer();

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ==========================================
        // Helper Methods
        // ==========================================

        private string GetStateEmoji(CharacterManager c)
        {
            if (c.characterNetworkManager != null && c.characterNetworkManager.isDead.Value) return "💀";
            if (c.characterDefenseManager != null && c.characterDefenseManager.isDefending) return "🛡️";
            if (c.isPerformingAction) return "⚔️";
            if (c.characterNetworkManager != null && c.characterNetworkManager.animatorMoveAmountMovement.Value > 0.5f) return "🏃";
            if (c.characterNetworkManager != null && c.characterNetworkManager.animatorMoveAmountMovement.Value > 0f) return "🚶";

            return "🧍"; // 기본 대기
        }

        private string GetCurrentStateName(CharacterManager c)
        {
            if (c is AICharacterManager aiManager)
            {
                FieldInfo stateField = typeof(AICharacterManager).GetField("currentState", BindingFlags.NonPublic | BindingFlags.Instance);
                AIState currentState = stateField?.GetValue(aiManager) as AIState;
                return currentState != null ? currentState.name : "None";
            }
            return "Player"; // 플레이어 탭일 경우
        }

        private string GetCurrentAnimationState(CharacterManager c)
        {
            if (c.animator == null) return "No Animator";

            string stateInfo = "";
            if (c.animator.layerCount > 1)
            {
                var actionState = c.animator.GetCurrentAnimatorClipInfo(1);
                if (actionState.Length > 0 && actionState[0].clip != null && !actionState[0].clip.name.Contains("Empty"))
                {
                    stateInfo = $"[A] {actionState[0].clip.name}";
                    return stateInfo;
                }
            }

            var baseState = c.animator.GetCurrentAnimatorClipInfo(0);
            if (baseState.Length > 0 && baseState[0].clip != null)
            {
                stateInfo = $"[B] {baseState[0].clip.name}";
            }
            else
            {
                stateInfo = "Transition / BlendTree";
            }

            return stateInfo;
        }

        private float GetDistanceToNearestPlayer(CharacterManager c)
        {
            PlayerManager nearest = GetNearestPlayer(c.transform.position, c as PlayerManager);
            if (nearest != null) return Vector3.Distance(c.transform.position, nearest.transform.position);
            return float.MaxValue;
        }

        private PlayerManager GetNearestPlayer(Vector3 pos, PlayerManager excludeSelf = null)
        {
            PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
            PlayerManager nearest = null;
            float minDist = float.MaxValue;

            foreach (var p in players)
            {
                if (p == excludeSelf) continue;
                float d = Vector3.Distance(pos, p.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = p;
                }
            }
            return nearest;
        }

        // ==========================================
        // Action Methods
        // ==========================================

        private void RecoverHP(CharacterManager c)
        {
            if (c.characterNetworkManager != null)
                c.characterNetworkManager.currentHealth.Value = c.characterNetworkManager.maxHealth.Value;
        }

        private void RecoverSP(CharacterManager c)
        {
            if (c.characterNetworkManager != null)
                c.characterNetworkManager.currentStamina.Value = c.characterNetworkManager.maxStamina.Value;
        }

        private void TeleportToNearestPlayer(CharacterManager c)
        {
            PlayerManager target = GetNearestPlayer(c.transform.position, c as PlayerManager);
            if (target != null)
            {
                Vector3 newPos = target.transform.position + target.transform.forward * 2.0f;

                var navAgent = c.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (navAgent != null) navAgent.enabled = false;

                c.transform.position = newPos;

                if (navAgent != null) navAgent.enabled = true;
            }
        }

        private void BulkRecoverHP()
        {
            var chars = GetCurrentTabCharacters();
            foreach (var c in chars) RecoverHP(c);
        }

        private void BulkRecoverSP()
        {
            var chars = GetCurrentTabCharacters();
            foreach (var c in chars) RecoverSP(c);
        }

        private void BulkTeleportToPlayer()
        {
            var chars = GetCurrentTabCharacters();
            foreach (var c in chars) TeleportToNearestPlayer(c);
        }

        private IEnumerable<CharacterManager> GetCurrentTabCharacters()
        {
            var all = FindObjectsByType<CharacterManager>(FindObjectsSortMode.None);
            if (currentTab == 0) return all.Where(c => c is AICharacterManager);
            if (currentTab == 1) return all.Where(c => c is PlayerManager);
            return all.Where(c => !(c is AICharacterManager) && !(c is PlayerManager));
        }
    }
}
#endif