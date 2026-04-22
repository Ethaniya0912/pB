// =============================================================================
// FactionProfileSetupWizard.cs  |  pB-4 팩션 프로파일 자동 설정 마법사
// 위치    : Assets/Scripts/Editor/
// 메뉴    : pB-4 → Faction Profile Setup Wizard (Ctrl+Shift+F)
//
// 기능:
//   · 몹 종류(고블린/오크/스켈레톤) × 티어(T1~T3) 매트릭스로
//     FactionCombatProfileSO를 원클릭으로 생성·설정합니다.
//   · 기존 SO에도 프리셋 값을 덮어쓸 수 있습니다.
//   · 변경 전 미리보기(Before/After diff) 제공.
//   · Undo 지원.
// =============================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using TDA.PB4.Data;

namespace TDA.PB4.Editor
{
    public class FactionProfileSetupWizard : EditorWindow
    {
        // ── 프리셋 데이터 ─────────────────────────────────────────────────────
        public enum MobType  { Goblin, Orc, Skeleton }
        public enum TierType { T1, T2, T3 }

        [Serializable]
        private struct Preset
        {
            public float stalkSpeed, engageRange;
            public float attackRange, orbitRadius, strafeAngularSpeed;
            public float strikeTriggerTime, closeInMaxTime;
            public float feintIntervalMin, feintIntervalMax;
            public float guardBreakReactionDelay;
            public float poiseAttackIntervalMin, poiseAttackIntervalMax;
            public float fleeSprintSpeed, safeFleeDistance;
            public float panicChainRadius, panicChainMultiplier;
            public float stuckCheckInterval, stuckMoveThreshold;
            public int   stuckGiveUpCount;
            public float fleeDuelInitialFear, fleeDuelMaxInitialFear;
            public float fearBonusNarrowPath, fearBonusDeathTrap, fearBonusSpookyCave;
            public float spookyCaveSpeedMultiplier;
            public float narrowPathOrbitMultiplier, narrowPathStrikeTimeMultiplier;
            public float attackRotationSpeed, attackTimeout;
            public float tacticalStaminaRecoverThreshold, tacticalHealthRecoverThreshold;
            public float tacticalRetreatDist, tacticalRecoverDuration;
            public float tacticalEvasionChance, tacticalEvasionDist, tacticalEvasionDuration;
            public float tacticalFeintChance, tacticalFeintDist, tacticalFeintDuration;
        }

        // ── 프리셋 정의 (Inspector 순서와 동일, 매트릭스 기준값) ──────────────
        private static readonly Dictionary<(MobType, TierType), Preset> PRESETS =
            new Dictionary<(MobType, TierType), Preset>
        {
            // ── 고블린 ─────────────────────────────────────────────────────────
            [(MobType.Goblin, TierType.T1)] = new Preset {
                stalkSpeed=5.5f, engageRange=12f,
                attackRange=1.5f, orbitRadius=3f, strafeAngularSpeed=80f,
                strikeTriggerTime=0.5f, closeInMaxTime=2f,
                feintIntervalMin=1.5f, feintIntervalMax=3f,
                guardBreakReactionDelay=0.10f,
                poiseAttackIntervalMin=1.5f, poiseAttackIntervalMax=3.5f,
                fleeSprintSpeed=6f, safeFleeDistance=15f,
                panicChainRadius=5f, panicChainMultiplier=2f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0.35f, fleeDuelMaxInitialFear=0.40f,
                fearBonusNarrowPath=0.15f, fearBonusDeathTrap=0.25f, fearBonusSpookyCave=0.10f,
                spookyCaveSpeedMultiplier=1.3f, narrowPathOrbitMultiplier=0.4f, narrowPathStrikeTimeMultiplier=0.5f,
                attackRotationSpeed=10f, attackTimeout=2.0f,
                tacticalStaminaRecoverThreshold=0.25f, tacticalHealthRecoverThreshold=0.30f,
                tacticalRetreatDist=5f, tacticalRecoverDuration=3f,
                tacticalEvasionChance=0.30f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.5f,
                tacticalFeintChance=0.20f, tacticalFeintDist=1.5f, tacticalFeintDuration=0.4f,
            },
            [(MobType.Goblin, TierType.T2)] = new Preset {
                stalkSpeed=6.5f, engageRange=14f,
                attackRange=1.5f, orbitRadius=3f, strafeAngularSpeed=90f,
                strikeTriggerTime=0.4f, closeInMaxTime=1.8f,
                feintIntervalMin=1.2f, feintIntervalMax=2.5f,
                guardBreakReactionDelay=0.08f,
                poiseAttackIntervalMin=1.2f, poiseAttackIntervalMax=3.0f,
                fleeSprintSpeed=7f, safeFleeDistance=12f,
                panicChainRadius=6f, panicChainMultiplier=2.5f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0.30f, fleeDuelMaxInitialFear=0.35f,
                fearBonusNarrowPath=0.12f, fearBonusDeathTrap=0.20f, fearBonusSpookyCave=0.08f,
                spookyCaveSpeedMultiplier=1.4f, narrowPathOrbitMultiplier=0.4f, narrowPathStrikeTimeMultiplier=0.45f,
                attackRotationSpeed=11f, attackTimeout=1.8f,
                tacticalStaminaRecoverThreshold=0.20f, tacticalHealthRecoverThreshold=0.25f,
                tacticalRetreatDist=5f, tacticalRecoverDuration=2.5f,
                tacticalEvasionChance=0.25f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.4f,
                tacticalFeintChance=0.18f, tacticalFeintDist=1.5f, tacticalFeintDuration=0.35f,
            },
            [(MobType.Goblin, TierType.T3)] = new Preset {
                stalkSpeed=7.5f, engageRange=16f,
                attackRange=2.0f, orbitRadius=2.5f, strafeAngularSpeed=100f,
                strikeTriggerTime=0.3f, closeInMaxTime=1.5f,
                feintIntervalMin=1.0f, feintIntervalMax=2.0f,
                guardBreakReactionDelay=0.05f,
                poiseAttackIntervalMin=1.0f, poiseAttackIntervalMax=2.5f,
                fleeSprintSpeed=8f, safeFleeDistance=10f,
                panicChainRadius=7f, panicChainMultiplier=3.0f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=3,
                fleeDuelInitialFear=0.25f, fleeDuelMaxInitialFear=0.30f,
                fearBonusNarrowPath=0.10f, fearBonusDeathTrap=0.15f, fearBonusSpookyCave=0.06f,
                spookyCaveSpeedMultiplier=1.5f, narrowPathOrbitMultiplier=0.35f, narrowPathStrikeTimeMultiplier=0.4f,
                attackRotationSpeed=12f, attackTimeout=1.6f,
                tacticalStaminaRecoverThreshold=0.15f, tacticalHealthRecoverThreshold=0.20f,
                tacticalRetreatDist=4f, tacticalRecoverDuration=2f,
                tacticalEvasionChance=0.20f, tacticalEvasionDist=2.5f, tacticalEvasionDuration=0.4f,
                tacticalFeintChance=0.15f, tacticalFeintDist=2.0f, tacticalFeintDuration=0.3f,
            },

            // ── 오크 ───────────────────────────────────────────────────────────
            [(MobType.Orc, TierType.T1)] = new Preset {
                stalkSpeed=4.0f, engageRange=10f,
                attackRange=3.0f, orbitRadius=4f, strafeAngularSpeed=40f,
                strikeTriggerTime=2.0f, closeInMaxTime=3f,
                feintIntervalMin=2.0f, feintIntervalMax=5.0f,
                guardBreakReactionDelay=0.25f,
                poiseAttackIntervalMin=2.5f, poiseAttackIntervalMax=5.5f,
                fleeSprintSpeed=0f, safeFleeDistance=0f,
                panicChainRadius=3f, panicChainMultiplier=0.5f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0.20f, fleeDuelMaxInitialFear=0.28f,
                fearBonusNarrowPath=0.05f, fearBonusDeathTrap=0.08f, fearBonusSpookyCave=0.03f,
                spookyCaveSpeedMultiplier=1.0f, narrowPathOrbitMultiplier=0.4f, narrowPathStrikeTimeMultiplier=0.5f,
                attackRotationSpeed=8f, attackTimeout=3.0f,
                tacticalStaminaRecoverThreshold=0.30f, tacticalHealthRecoverThreshold=0.25f,
                tacticalRetreatDist=4f, tacticalRecoverDuration=3f,
                tacticalEvasionChance=0.10f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.5f,
                tacticalFeintChance=0.10f, tacticalFeintDist=1.5f, tacticalFeintDuration=0.4f,
            },
            [(MobType.Orc, TierType.T2)] = new Preset {
                stalkSpeed=4.5f, engageRange=10f,
                attackRange=3.0f, orbitRadius=4f, strafeAngularSpeed=45f,
                strikeTriggerTime=1.8f, closeInMaxTime=3f,
                feintIntervalMin=2.0f, feintIntervalMax=5.0f,
                guardBreakReactionDelay=0.20f,
                poiseAttackIntervalMin=2.2f, poiseAttackIntervalMax=5.0f,
                fleeSprintSpeed=0f, safeFleeDistance=0f,
                panicChainRadius=3f, panicChainMultiplier=0.5f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0.18f, fleeDuelMaxInitialFear=0.25f,
                fearBonusNarrowPath=0.04f, fearBonusDeathTrap=0.06f, fearBonusSpookyCave=0.02f,
                spookyCaveSpeedMultiplier=1.0f, narrowPathOrbitMultiplier=0.4f, narrowPathStrikeTimeMultiplier=0.45f,
                attackRotationSpeed=9f, attackTimeout=2.8f,
                tacticalStaminaRecoverThreshold=0.25f, tacticalHealthRecoverThreshold=0.20f,
                tacticalRetreatDist=4f, tacticalRecoverDuration=3f,
                tacticalEvasionChance=0.12f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.5f,
                tacticalFeintChance=0.12f, tacticalFeintDist=1.5f, tacticalFeintDuration=0.4f,
            },
            [(MobType.Orc, TierType.T3)] = new Preset {
                stalkSpeed=5.0f, engageRange=12f,
                attackRange=3.5f, orbitRadius=4f, strafeAngularSpeed=50f,
                strikeTriggerTime=1.5f, closeInMaxTime=2.5f,
                feintIntervalMin=1.8f, feintIntervalMax=4.5f,
                guardBreakReactionDelay=0.15f,
                poiseAttackIntervalMin=2.0f, poiseAttackIntervalMax=4.5f,
                fleeSprintSpeed=0f, safeFleeDistance=0f,
                panicChainRadius=4f, panicChainMultiplier=0.5f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0.15f, fleeDuelMaxInitialFear=0.22f,
                fearBonusNarrowPath=0.03f, fearBonusDeathTrap=0.05f, fearBonusSpookyCave=0.02f,
                spookyCaveSpeedMultiplier=1.1f, narrowPathOrbitMultiplier=0.35f, narrowPathStrikeTimeMultiplier=0.4f,
                attackRotationSpeed=10f, attackTimeout=2.5f,
                tacticalStaminaRecoverThreshold=0.20f, tacticalHealthRecoverThreshold=0.15f,
                tacticalRetreatDist=3f, tacticalRecoverDuration=2.5f,
                tacticalEvasionChance=0.15f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.4f,
                tacticalFeintChance=0.15f, tacticalFeintDist=2.0f, tacticalFeintDuration=0.35f,
            },

            // ── 스켈레톤 ────────────────────────────────────────────────────────
            [(MobType.Skeleton, TierType.T1)] = new Preset {
                stalkSpeed=2.5f, engageRange=15f,
                attackRange=2.5f, orbitRadius=5f, strafeAngularSpeed=20f,
                strikeTriggerTime=99f, closeInMaxTime=2.0f,
                feintIntervalMin=4.0f, feintIntervalMax=8.0f,
                guardBreakReactionDelay=0.05f,
                poiseAttackIntervalMin=3.0f, poiseAttackIntervalMax=6.0f,
                fleeSprintSpeed=0f, safeFleeDistance=0f,
                panicChainRadius=0f, panicChainMultiplier=0f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0f, fleeDuelMaxInitialFear=0f,
                fearBonusNarrowPath=0.08f, fearBonusDeathTrap=0.10f, fearBonusSpookyCave=0.05f,
                spookyCaveSpeedMultiplier=1.1f, narrowPathOrbitMultiplier=0.6f, narrowPathStrikeTimeMultiplier=0.7f,
                attackRotationSpeed=5f, attackTimeout=2.5f,
                tacticalStaminaRecoverThreshold=0.25f, tacticalHealthRecoverThreshold=0.30f,
                tacticalRetreatDist=5f, tacticalRecoverDuration=3f,
                tacticalEvasionChance=0.05f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.5f,
                tacticalFeintChance=0.05f, tacticalFeintDist=1.5f, tacticalFeintDuration=0.4f,
            },
            [(MobType.Skeleton, TierType.T2)] = new Preset {
                stalkSpeed=3.0f, engageRange=15f,
                attackRange=2.5f, orbitRadius=5f, strafeAngularSpeed=25f,
                strikeTriggerTime=99f, closeInMaxTime=2.0f,
                feintIntervalMin=4.0f, feintIntervalMax=7.0f,
                guardBreakReactionDelay=0.05f,
                poiseAttackIntervalMin=2.8f, poiseAttackIntervalMax=5.5f,
                fleeSprintSpeed=0f, safeFleeDistance=0f,
                panicChainRadius=0f, panicChainMultiplier=0f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0f, fleeDuelMaxInitialFear=0f,
                fearBonusNarrowPath=0.06f, fearBonusDeathTrap=0.08f, fearBonusSpookyCave=0.04f,
                spookyCaveSpeedMultiplier=1.2f, narrowPathOrbitMultiplier=0.55f, narrowPathStrikeTimeMultiplier=0.65f,
                attackRotationSpeed=6f, attackTimeout=2.3f,
                tacticalStaminaRecoverThreshold=0.20f, tacticalHealthRecoverThreshold=0.25f,
                tacticalRetreatDist=5f, tacticalRecoverDuration=2.5f,
                tacticalEvasionChance=0.08f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.5f,
                tacticalFeintChance=0.08f, tacticalFeintDist=1.5f, tacticalFeintDuration=0.4f,
            },
            [(MobType.Skeleton, TierType.T3)] = new Preset {
                stalkSpeed=3.5f, engageRange=18f,
                attackRange=3.0f, orbitRadius=6f, strafeAngularSpeed=30f,
                strikeTriggerTime=99f, closeInMaxTime=2.0f,
                feintIntervalMin=3.5f, feintIntervalMax=6.0f,
                guardBreakReactionDelay=0.03f,
                poiseAttackIntervalMin=2.5f, poiseAttackIntervalMax=5.0f,
                fleeSprintSpeed=0f, safeFleeDistance=0f,
                panicChainRadius=0f, panicChainMultiplier=0f,
                stuckCheckInterval=0.5f, stuckMoveThreshold=0.3f, stuckGiveUpCount=4,
                fleeDuelInitialFear=0f, fleeDuelMaxInitialFear=0f,
                fearBonusNarrowPath=0.05f, fearBonusDeathTrap=0.06f, fearBonusSpookyCave=0.03f,
                spookyCaveSpeedMultiplier=1.2f, narrowPathOrbitMultiplier=0.5f, narrowPathStrikeTimeMultiplier=0.6f,
                attackRotationSpeed=7f, attackTimeout=2.0f,
                tacticalStaminaRecoverThreshold=0.15f, tacticalHealthRecoverThreshold=0.20f,
                tacticalRetreatDist=4f, tacticalRecoverDuration=2f,
                tacticalEvasionChance=0.10f, tacticalEvasionDist=2f, tacticalEvasionDuration=0.5f,
                tacticalFeintChance=0.10f, tacticalFeintDist=2.0f, tacticalFeintDuration=0.4f,
            },
        };

        // ── UI 상태 ────────────────────────────────────────────────────────────
        private MobType  _selectedMob  = MobType.Goblin;
        private TierType _selectedTier = TierType.T1;
        private FactionCombatProfileSO _targetSO;
        private string   _savePath = "Assets/Data/A.I States";
        private Vector2  _scrollPos;
        private bool     _showDiff;
        private bool     _showAllPresets;
        private GUIStyle _headerStyle, _sectionStyle, _valueStyle, _changedStyle;
        private bool     _stylesReady;

        // 배치 생성 옵션
        private bool _batchAll;
        private bool[] _batchMob  = { true, true, true };   // G/O/S
        private bool[] _batchTier = { true, true, true };   // T1/T2/T3

        // 색상
        private static readonly Color COL_G  = new Color(0.35f, 0.75f, 0.42f, 1f);
        private static readonly Color COL_O  = new Color(0.90f, 0.60f, 0.15f, 1f);
        private static readonly Color COL_S  = new Color(0.30f, 0.58f, 0.88f, 1f);
        private static readonly Color COL_BG = new Color(0.17f, 0.17f, 0.17f, 1f);

        // ── 메뉴 등록 ──────────────────────────────────────────────────────────
        [MenuItem("pB-4/Faction Profile Setup Wizard  %#f")]
        public static void OpenWizard()
        {
            var win = GetWindow<FactionProfileSetupWizard>("⚙  Faction Profile Wizard");
            win.minSize = new Vector2(520, 680);
        }

        // ── GUI ────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            DrawMobTierSelector();
            DrawSavePath();
            EditorGUILayout.Space(6);
            DrawSingleCreate();
            EditorGUILayout.Space(8);
            DrawBatchCreate();
            EditorGUILayout.Space(8);
            DrawApplyToExisting();
            EditorGUILayout.Space(8);
            DrawPreview();

            EditorGUILayout.EndScrollView();
        }

        // ── 헤더 ──────────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            var rect = EditorGUILayout.GetControlRect(false, 54);
            EditorGUI.DrawRect(rect, COL_BG);
            GUI.Label(new Rect(rect.x+12, rect.y+8, rect.width, 28),
                "⚙  Faction Profile Setup Wizard", _headerStyle);
            GUI.Label(new Rect(rect.x+12, rect.y+30, rect.width, 16),
                "FactionCombatProfileSO 자동 생성·설정 |  pB-4 Data-Driven v2",
                new GUIStyle(EditorStyles.miniLabel) { normal={ textColor=new Color(0.6f,0.6f,0.6f) } });
            EditorGUILayout.Space(4);
        }

        // ── 몹/티어 선택 ──────────────────────────────────────────────────────
        private void DrawMobTierSelector()
        {
            EditorGUILayout.LabelField("몹 종류 × 티어 선택", _sectionStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMobButton(MobType.Goblin,   "🟢  고블린",  COL_G);
                DrawMobButton(MobType.Orc,      "🟡  오크",    COL_O);
                DrawMobButton(MobType.Skeleton, "🔵  스켈레톤",COL_S);
            }
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTierButton(TierType.T1, "T1  기본");
                DrawTierButton(TierType.T2, "T2  강화");
                DrawTierButton(TierType.T3, "T3  상위");
            }
            EditorGUILayout.Space(4);

            // 선택된 프리셋 요약
            var key = (_selectedMob, _selectedTier);
            if (PRESETS.TryGetValue(key, out var preset))
            {
                var mobColor = _selectedMob == MobType.Goblin ? COL_G :
                               _selectedMob == MobType.Orc    ? COL_O : COL_S;
                var style = new GUIStyle(EditorStyles.helpBox) { fontSize=11 };
                using (new EditorGUILayout.HorizontalScope())
                {
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(mobColor.r, mobColor.g, mobColor.b, 0.15f);
                    EditorGUILayout.BeginVertical(style);
                    GUI.backgroundColor = prev;

                    EditorGUILayout.LabelField(
                        $"선택: {_selectedMob} {_selectedTier}  " +
                        $"| Speed {preset.stalkSpeed}  Orbit {preset.orbitRadius}  " +
                        $"Strike {preset.strikeTriggerTime}s  Flee {(preset.fleeSprintSpeed > 0 ? preset.fleeSprintSpeed+"m/s" : "없음")}",
                        EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void DrawMobButton(MobType mob, string label, Color color)
        {
            var prev = GUI.backgroundColor;
            bool selected = _selectedMob == mob;
            GUI.backgroundColor = selected ? color : new Color(color.r, color.g, color.b, 0.25f);
            var style = new GUIStyle(GUI.skin.button)
            {
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                fontSize  = 13,
                fixedHeight = 32,
            };
            if (GUILayout.Button(label, style)) _selectedMob = mob;
            GUI.backgroundColor = prev;
        }

        private void DrawTierButton(TierType tier, string label)
        {
            var prev = GUI.backgroundColor;
            bool selected = _selectedTier == tier;
            GUI.backgroundColor = selected
                ? new Color(0.28f, 0.76f, 1f, 1f)
                : new Color(0.28f, 0.76f, 1f, 0.2f);
            var style = new GUIStyle(GUI.skin.button)
            {
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                fontSize  = 13,
                fixedHeight = 30,
            };
            if (GUILayout.Button(label, style)) _selectedTier = tier;
            GUI.backgroundColor = prev;
        }

        // ── 저장 경로 ─────────────────────────────────────────────────────────
        private void DrawSavePath()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                _savePath = EditorGUILayout.TextField("저장 폴더", _savePath);
                if (GUILayout.Button("📂", GUILayout.Width(28)))
                {
                    string chosen = EditorUtility.OpenFolderPanel("SO 저장 위치", _savePath, "");
                    if (!string.IsNullOrEmpty(chosen))
                    {
                        // Assets 상대경로로 변환
                        if (chosen.StartsWith(Application.dataPath))
                            _savePath = "Assets" + chosen.Substring(Application.dataPath.Length);
                    }
                }
            }
        }

        // ── 단일 생성 ─────────────────────────────────────────────────────────
        private void DrawSingleCreate()
        {
            EditorGUILayout.LabelField("━  단일 생성", _sectionStyle);

            var key = (_selectedMob, _selectedTier);
            if (!PRESETS.ContainsKey(key)) return;

            string filename = $"{_selectedMob}_T{(int)_selectedTier+1}_CombatProfile";

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"파일명: {filename}.asset", EditorStyles.miniLabel);
            }

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f, 1f);
            if (GUILayout.Button($"✦  {_selectedMob} {_selectedTier} 프로파일 생성", GUILayout.Height(36)))
                CreateSingle(_selectedMob, _selectedTier);
            GUI.backgroundColor = prev;
        }

        // ── 배치 생성 ─────────────────────────────────────────────────────────
        private void DrawBatchCreate()
        {
            EditorGUILayout.LabelField("━  배치 생성 (여러 팩션/티어 한번에)", _sectionStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("몹:", GUILayout.Width(28));
                _batchMob[0] = EditorGUILayout.ToggleLeft("고블린", _batchMob[0], GUILayout.Width(70));
                _batchMob[1] = EditorGUILayout.ToggleLeft("오크",   _batchMob[1], GUILayout.Width(50));
                _batchMob[2] = EditorGUILayout.ToggleLeft("스켈레톤",_batchMob[2], GUILayout.Width(75));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("티어:", GUILayout.Width(36));
                _batchTier[0] = EditorGUILayout.ToggleLeft("T1", _batchTier[0], GUILayout.Width(38));
                _batchTier[1] = EditorGUILayout.ToggleLeft("T2", _batchTier[1], GUILayout.Width(38));
                _batchTier[2] = EditorGUILayout.ToggleLeft("T3", _batchTier[2], GUILayout.Width(38));
            }

            // 생성 예정 파일 목록
            var planned = GetBatchList();
            if (planned.Count == 0)
            {
                EditorGUILayout.HelpBox("몹 또는 티어를 1개 이상 선택하세요.", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField(
                $"생성 예정 ({planned.Count}개): " + string.Join(", ", planned.ConvertAll(p => $"{p.Item1}_T{(int)p.Item2+1}")),
                EditorStyles.miniLabel);

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.28f, 0.76f, 1f, 1f);
            if (GUILayout.Button($"⚡  {planned.Count}개 한번에 생성·설정", GUILayout.Height(34)))
                CreateBatch(planned);
            GUI.backgroundColor = prev;
        }

        // ── 기존 SO에 적용 ────────────────────────────────────────────────────
        private void DrawApplyToExisting()
        {
            EditorGUILayout.LabelField("━  기존 SO에 덮어쓰기", _sectionStyle);

            _targetSO = (FactionCombatProfileSO)EditorGUILayout.ObjectField(
                "대상 SO", _targetSO, typeof(FactionCombatProfileSO), false);

            if (_targetSO == null)
            {
                EditorGUILayout.HelpBox("기존 SO를 드래그하거나 선택하세요.", MessageType.Info);
                return;
            }

            _showDiff = EditorGUILayout.Foldout(_showDiff, "변경 내용 미리보기 (Before → After)");
            if (_showDiff) DrawDiff(_targetSO, (_selectedMob, _selectedTier));

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.65f, 0.1f, 1f);
            if (GUILayout.Button($"⬇  {_selectedMob} {_selectedTier} 프리셋으로 덮어쓰기", GUILayout.Height(32)))
            {
                if (EditorUtility.DisplayDialog("덮어쓰기 확인",
                    $"{_targetSO.name}에 {_selectedMob} {_selectedTier} 프리셋을 적용합니다.\n" +
                    "기존 값이 모두 변경됩니다. 계속하시겠습니까?",
                    "적용", "취소"))
                    ApplyPreset(_targetSO, (_selectedMob, _selectedTier));
            }
            GUI.backgroundColor = prev;
        }

        // ── Before/After Diff ─────────────────────────────────────────────────
        private void DrawDiff(FactionCombatProfileSO so, (MobType, TierType) key)
        {
            if (!PRESETS.TryGetValue(key, out var preset)) return;

            var fields = GetSOFields();
            var presetValues = PresetToDict(preset);

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var f in fields)
                {
                    if (!presetValues.TryGetValue(f.Name, out var newVal)) continue;
                    var curVal = f.GetValue(so);
                    bool changed = !curVal.Equals(Convert.ChangeType(newVal, f.FieldType));
                    var style = changed ? _changedStyle : _valueStyle;
                    string arrow = changed ? $"  →  {newVal}" : "  (변화 없음)";
                    EditorGUILayout.LabelField($"  {f.Name}: {curVal}{arrow}", style);
                }
            }
        }

        // ── 프리셋 미리보기 ───────────────────────────────────────────────────
        private void DrawPreview()
        {
            _showAllPresets = EditorGUILayout.Foldout(_showAllPresets, "━  선택 프리셋 전체 값 보기");
            if (!_showAllPresets) return;

            var key = (_selectedMob, _selectedTier);
            if (!PRESETS.TryGetValue(key, out var preset)) return;

            var dict = PresetToDict(preset);
            var fields = GetSOFields();

            EditorGUI.indentLevel++;
            string curSec = "";
            foreach (var f in fields)
            {
                // 섹션 구분
                string sec = GetSection(f.Name);
                if (sec != curSec)
                {
                    curSec = sec;
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField($"── {sec}", EditorStyles.boldLabel);
                }

                if (dict.TryGetValue(f.Name, out var val))
                    EditorGUILayout.LabelField($"  {f.Name}", val.ToString(), _valueStyle);
            }
            EditorGUI.indentLevel--;
        }

        // ── 생성 로직 ─────────────────────────────────────────────────────────
        private void CreateSingle(MobType mob, TierType tier)
        {
            EnsureSaveDir();
            string path = $"{_savePath}/{mob}_T{(int)tier+1}_CombatProfile.asset";

            var so = AssetDatabase.LoadAssetAtPath<FactionCombatProfileSO>(path);
            bool isNew = so == null;
            if (isNew)
            {
                so = CreateInstance<FactionCombatProfileSO>();
                AssetDatabase.CreateAsset(so, path);
            }

            Undo.RecordObject(so, $"Faction Preset {mob} T{(int)tier+1}");
            ApplyPreset(so, (mob, tier));

            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(so);
            Debug.Log($"[FactionWizard] {(isNew ? "생성" : "업데이트")}: {path}");
        }

        private void CreateBatch(List<(MobType, TierType)> list)
        {
            EnsureSaveDir();
            int count = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (mob, tier) in list)
                {
                    string path = $"{_savePath}/{mob}_T{(int)tier+1}_CombatProfile.asset";
                    var so = AssetDatabase.LoadAssetAtPath<FactionCombatProfileSO>(path);
                    bool isNew = so == null;
                    if (isNew)
                    {
                        so = CreateInstance<FactionCombatProfileSO>();
                        AssetDatabase.CreateAsset(so, path);
                    }
                    Undo.RecordObject(so, "Faction Preset Batch");
                    ApplyPreset(so, (mob, tier));
                    EditorUtility.SetDirty(so);
                    count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("배치 생성 완료",
                $"{count}개의 FactionCombatProfileSO가 생성/갱신되었습니다.\n경로: {_savePath}", "확인");
        }

        private void ApplyPreset(FactionCombatProfileSO so, (MobType mob, TierType tier) key)
        {
            if (!PRESETS.TryGetValue(key, out var preset)) return;
            var dict = PresetToDict(preset);
            foreach (var f in GetSOFields())
            {
                if (!dict.TryGetValue(f.Name, out var val)) continue;
                try { f.SetValue(so, Convert.ChangeType(val, f.FieldType)); }
                catch { /* 타입 미스매치 무시 */ }
            }
        }

        // ── 유틸 ──────────────────────────────────────────────────────────────
        private List<(MobType, TierType)> GetBatchList()
        {
            var result = new List<(MobType, TierType)>();
            MobType[] mobs = { MobType.Goblin, MobType.Orc, MobType.Skeleton };
            TierType[] tiers = { TierType.T1, TierType.T2, TierType.T3 };
            for (int m = 0; m < 3; m++)
                for (int t = 0; t < 3; t++)
                    if (_batchMob[m] && _batchTier[t])
                        result.Add((mobs[m], tiers[t]));
            return result;
        }

        private void EnsureSaveDir()
        {
            if (!AssetDatabase.IsValidFolder(_savePath))
            {
                string[] parts = _savePath.Split('/');
                string cur = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = cur + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(cur, parts[i]);
                    cur = next;
                }
                AssetDatabase.Refresh();
            }
        }

        private static FieldInfo[] _cachedFields;
        private static FieldInfo[] GetSOFields()
        {
            if (_cachedFields != null) return _cachedFields;
            _cachedFields = typeof(FactionCombatProfileSO)
                .GetFields(BindingFlags.Public | BindingFlags.Instance);
            return _cachedFields;
        }

        private Dictionary<string, object> PresetToDict(Preset p)
        {
            var d = new Dictionary<string, object>
            {
                [nameof(FactionCombatProfileSO.stalkSpeed)]                      = p.stalkSpeed,
                [nameof(FactionCombatProfileSO.engageRange)]                     = p.engageRange,
                [nameof(FactionCombatProfileSO.attackRange)]                     = p.attackRange,
                [nameof(FactionCombatProfileSO.orbitRadius)]                     = p.orbitRadius,
                [nameof(FactionCombatProfileSO.strafeAngularSpeed)]              = p.strafeAngularSpeed,
                [nameof(FactionCombatProfileSO.strikeTriggerTime)]               = p.strikeTriggerTime,
                /*[nameof(FactionCombatProfileSO.closeInMaxTime)]                  = p.closeInMaxTime,
                [nameof(FactionCombatProfileSO.feintIntervalMin)]                = p.feintIntervalMin,
                [nameof(FactionCombatProfileSO.feintIntervalMax)]                = p.feintIntervalMax,
                [nameof(FactionCombatProfileSO.guardBreakReactionDelay)]         = p.guardBreakReactionDelay,
                [nameof(FactionCombatProfileSO.poiseAttackIntervalMin)]          = p.poiseAttackIntervalMin,
                [nameof(FactionCombatProfileSO.poiseAttackIntervalMax)]          = p.poiseAttackIntervalMax,
                [nameof(FactionCombatProfileSO.fleeSprintSpeed)]                 = p.fleeSprintSpeed,
                [nameof(FactionCombatProfileSO.safeFleeDistance)]                = p.safeFleeDistance,
                [nameof(FactionCombatProfileSO.panicChainRadius)]                = p.panicChainRadius,
                [nameof(FactionCombatProfileSO.panicChainMultiplier)]            = p.panicChainMultiplier,
                [nameof(FactionCombatProfileSO.stuckCheckInterval)]              = p.stuckCheckInterval,
                [nameof(FactionCombatProfileSO.stuckMoveThreshold)]              = p.stuckMoveThreshold,
                [nameof(FactionCombatProfileSO.stuckGiveUpCount)]                = p.stuckGiveUpCount,
                [nameof(FactionCombatProfileSO.fleeDuelInitialFear)]             = p.fleeDuelInitialFear,
                [nameof(FactionCombatProfileSO.fleeDuelMaxInitialFear)]          = p.fleeDuelMaxInitialFear,
                [nameof(FactionCombatProfileSO.fearBonusNarrowPath)]             = p.fearBonusNarrowPath,
                [nameof(FactionCombatProfileSO.fearBonusDeathTrap)]              = p.fearBonusDeathTrap,
                [nameof(FactionCombatProfileSO.fearBonusSpookyCave)]             = p.fearBonusSpookyCave,
                [nameof(FactionCombatProfileSO.spookyCaveSpeedMultiplier)]       = p.spookyCaveSpeedMultiplier,
                [nameof(FactionCombatProfileSO.narrowPathOrbitMultiplier)]       = p.narrowPathOrbitMultiplier,
                [nameof(FactionCombatProfileSO.narrowPathStrikeTimeMultiplier)]  = p.narrowPathStrikeTimeMultiplier,
                [nameof(FactionCombatProfileSO.attackRotationSpeed)]             = p.attackRotationSpeed,
                [nameof(FactionCombatProfileSO.attackTimeout)]                   = p.attackTimeout,
                [nameof(FactionCombatProfileSO.tacticalStaminaRecoverThreshold)] = p.tacticalStaminaRecoverThreshold,
                [nameof(FactionCombatProfileSO.tacticalHealthRecoverThreshold)]  = p.tacticalHealthRecoverThreshold,
                [nameof(FactionCombatProfileSO.tacticalRetreatDist)]             = p.tacticalRetreatDist,
                [nameof(FactionCombatProfileSO.tacticalRecoverDuration)]         = p.tacticalRecoverDuration,
                [nameof(FactionCombatProfileSO.tacticalEvasionChance)]           = p.tacticalEvasionChance,
                [nameof(FactionCombatProfileSO.tacticalEvasionDist)]             = p.tacticalEvasionDist,
                [nameof(FactionCombatProfileSO.tacticalEvasionDuration)]         = p.tacticalEvasionDuration,
                [nameof(FactionCombatProfileSO.tacticalFeintChance)]             = p.tacticalFeintChance,
                [nameof(FactionCombatProfileSO.tacticalFeintDist)]               = p.tacticalFeintDist,
                [nameof(FactionCombatProfileSO.tacticalFeintDuration)]           = p.tacticalFeintDuration,*/
            };
            return d;
        }

        private static string GetSection(string fieldName)
        {
            if (fieldName.StartsWith("stalk") || fieldName.StartsWith("engage")) return "Stalk";
            if (fieldName.StartsWith("attack") || fieldName.StartsWith("orbit") ||
                fieldName.StartsWith("strafe") || fieldName.StartsWith("strike") ||
                fieldName.StartsWith("closeIn") || fieldName.StartsWith("feint") ||
                fieldName.StartsWith("guard") || fieldName.StartsWith("poise")) return "CircleStrafe";
            if (fieldName.StartsWith("flee") || fieldName.StartsWith("panic") ||
                fieldName.StartsWith("safe")) return "Flee";
            if (fieldName.StartsWith("stuck")) return "FleeSwarm Stuck";
            if (fieldName.StartsWith("fleeDuel")) return "FleeDuel";
            if (fieldName.StartsWith("fearBonus")) return "지형 Fear 보정";
            if (fieldName.StartsWith("spooky") || fieldName.StartsWith("narrow")) return "지형 배율";
            if (fieldName.StartsWith("attackRotation") || fieldName.StartsWith("attackTimeout")) return "Strike";
            if (fieldName.StartsWith("tactical")) return "TacticalReposition";
            return "기타";
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 16,
                normal    = { textColor = new Color(0.28f, 0.76f, 1f) },
            };
            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal   = { textColor = new Color(0.7f, 0.85f, 1f) },
            };
            _valueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
            };
            _changedStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(1f, 0.85f, 0.2f) },
            };
        }
    }
}
#endif
