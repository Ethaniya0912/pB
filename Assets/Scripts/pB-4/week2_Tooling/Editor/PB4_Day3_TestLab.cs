// =============================================================================
// PB4_Day3_TestLab.cs  |  pB-4 Week 2 — Day 3 통합 테스트 랩
// 배치: Assets/Scripts/pB-4/week2_Tooling/Editor/PB4_Day3_TestLab.cs
//
// 역할:
//   Day 3의 모든 시스템을 순차적으로 테스트하는 단일 창.
//   왼쪽: Step 버튼 (Karma / Trust / Trauma / Pivot / Command)
//   오른쪽: 실시간 상태 + 로그 스트림
//
// 사용:
//   Window → pB-4 → Day 3 Test Lab
//   또는 HumanoidDashboard의 탭 버튼으로 진입
//
// 설계 원칙:
//   - Play 모드 전용. Edit 모드에서는 안내만 표시.
//   - 대상 NPC 자동 선택 (씬의 첫 번째 HumanoidAIBrain).
//   - 각 Step 버튼은 독립. 순서대로 눌러도 되고 개별로 테스트해도 됨.
//   - 버튼 위 마우스 호버 시 예상 결과 툴팁.
//   - 우측 상태 패널 0.5초마다 자동 Refresh.
// =============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;
using TDA.PB4.Director;
using TDA.PB4.Interfaces.Narrative;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.Tooling.Editor
{
    /// <summary>Day 3 통합 테스트 창. 좌측 Step 버튼 / 우측 실시간 상태+로그.</summary>
    public class PB4_Day3_TestLab : EditorWindow
    {
        // ===== 색상 테마 (Dashboard와 동일 네온 팔레트) =====
        private static readonly Color C_BG           = new Color(0.07f, 0.08f, 0.12f);
        private static readonly Color C_CARD         = new Color(0.11f, 0.13f, 0.18f);
        private static readonly Color C_CARD_LIGHT   = new Color(0.14f, 0.16f, 0.22f);
        private static readonly Color C_NEON_CYAN    = new Color(0.20f, 0.90f, 0.95f);
        private static readonly Color C_NEON_MAGENTA = new Color(0.95f, 0.40f, 0.80f);
        private static readonly Color C_NEON_GREEN   = new Color(0.40f, 0.95f, 0.50f);
        private static readonly Color C_NEON_AMBER   = new Color(0.98f, 0.75f, 0.20f);
        private static readonly Color C_NEON_RED     = new Color(0.95f, 0.35f, 0.35f);
        private static readonly Color C_TEXT_BRIGHT  = new Color(0.92f, 0.95f, 1.00f);
        private static readonly Color C_TEXT_DIM     = new Color(0.55f, 0.60f, 0.68f);
        private static readonly Color C_DIVIDER      = new Color(0.18f, 0.21f, 0.27f);

        // ===== 상태 =====
        private HumanoidAIBrain targetBrain;
        private TrustMatrix trust;
        private TraumaSystem trauma;
        private NPCAlignmentController alignment;
        private DilemmaPivotResolver pivotResolver;
        private PersonalityTagResolver tagResolver;
        private CommandAcceptanceResolver cmdResolver;

        private Vector2 scrollLog;
        private readonly List<LogEntry> logs = new List<LogEntry>(128);
        private const int MAX_LOGS = 200;

        private float lastRefreshTime;
        private const float REFRESH_INTERVAL = 0.5f;

        // 현재 진행 중인 Step
        private int currentStep = -1;

        // ===== NPC Profile 시스템 =====
        // 같은 시나리오라도 상대 NPC의 성격에 따라 Trust/Karma 반응이 다름.
        // 예: Altruist는 빠르게 친해지고, Aggressive는 좀처럼 마음 열지 않음.
        private class NPCProfile
        {
            public string name;
            public string icon;
            public string description;
            public float initialTrust;         // 프로파일 적용 시 Trust 초기값
            public float trustGainMult;        // Trust 증가 배율 (양수 delta)
            public float trustLossMult;        // Trust 감소 배율 (음수 delta, 절대값에 곱해짐)
            public float karmaReactionMult;    // Karma 변화가 이 NPC의 반응 속도에 미치는 배율
            public string expectedOutcome;     // 기대 결과 (UI 힌트)
        }

        private static readonly NPCProfile[] PROFILES = new[]
        {
            new NPCProfile
            {
                name = "Default",
                icon = "⚪",
                description = "표준 NPC. 평균적인 반응.",
                initialTrust = 50f, trustGainMult = 1.0f, trustLossMult = 1.0f, karmaReactionMult = 1.0f,
                expectedOutcome = "시나리오 기본값대로 전개"
            },
            new NPCProfile
            {
                name = "Altruist",
                icon = "💚",
                description = "이타적. 빠르게 친해지고 쉽게 용서함.",
                initialTrust = 60f, trustGainMult = 1.5f, trustLossMult = 0.7f, karmaReactionMult = 1.3f,
                expectedOutcome = "영웅의 길 → Companion 초고속 / 배신자도 쉽게 용서"
            },
            new NPCProfile
            {
                name = "Coward",
                icon = "😨",
                description = "겁쟁이. 경계가 길지만 한번 신뢰하면 강함.",
                initialTrust = 35f, trustGainMult = 0.8f, trustLossMult = 1.5f, karmaReactionMult = 1.1f,
                expectedOutcome = "조우 시 Doubt 깊음 / 이후 회복도 더딤"
            },
            new NPCProfile
            {
                name = "Aggressive",
                icon = "👿",
                description = "공격적. 초기부터 적대적 태도.",
                initialTrust = 20f, trustGainMult = 0.5f, trustLossMult = 2.0f, karmaReactionMult = 1.5f,
                expectedOutcome = "영웅의 길도 Friendly 도달 어려움 / 배신 시 즉각 Hostile"
            },
            new NPCProfile
            {
                name = "Stoic",
                icon = "🗿",
                description = "냉정한. 감정 변화가 느림. 안정적.",
                initialTrust = 50f, trustGainMult = 0.5f, trustLossMult = 0.5f, karmaReactionMult = 0.6f,
                expectedOutcome = "모든 시나리오가 약하게 표현 / 극단 상태 잘 안 감"
            },
            new NPCProfile
            {
                name = "Suspicious",
                icon = "🤨",
                description = "의심많음. 배신에 극도로 예민.",
                initialTrust = 40f, trustGainMult = 0.7f, trustLossMult = 1.8f, karmaReactionMult = 1.2f,
                expectedOutcome = "작은 배신도 Hostility 직행 / 회복 시간 오래"
            },
            new NPCProfile
            {
                name = "Idealist",
                icon = "✨",
                description = "이상주의자. 도덕적 행동에 강하게 반응.",
                initialTrust = 55f, trustGainMult = 1.3f, trustLossMult = 0.8f, karmaReactionMult = 2.0f,
                expectedOutcome = "Karma 변화에 가장 민감 / Saint 플레이어의 진정한 파트너"
            },
        };

        private int selectedProfileIdx = 0;
        private NPCProfile CurrentProfile => PROFILES[selectedProfileIdx];

        private class LogEntry
        {
            public string time;
            public string message;
            public Color color;
        }

        // ===== 메뉴 등록 =====
        [MenuItem("Window/pB-4/Day 3 Test Lab")]
        public static void Open()
        {
            var win = GetWindow<PB4_Day3_TestLab>("Day 3 Test Lab");
            win.minSize = new Vector2(900, 600);
            win.Show();
        }

        // ===== 라이프사이클 =====
        private void OnEnable()
        {
            AddLog("Day 3 Test Lab opened.", C_NEON_CYAN);
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;

            float t = Time.realtimeSinceStartup;
            if (t - lastRefreshTime >= REFRESH_INTERVAL)
            {
                lastRefreshTime = t;
                Repaint();
            }
        }

        // ===== OnGUI =====
        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            DrawHeader();

            if (!Application.isPlaying)
            {
                DrawCenteredMessage("[ SYSTEM OFFLINE ]\n\nPlay 모드에서 테스트 가능합니다.");
                return;
            }

            // Target NPC 자동 선택
            if (targetBrain == null)
                AutoSelectTarget();

            if (targetBrain == null)
            {
                DrawCenteredMessage("[ NO TARGET ]\n\n씬에 HumanoidAIBrain이 없습니다.\nBootstrap 후 다시 시도하세요.");
                return;
            }

            // [v3 NEW] Profile 선택 바
            DrawProfileBar();

            // ===== 절대 위치 2열 레이아웃 (IMGUI GetRect + ExpandHeight 버그 회피) =====
            float headerAndProfileHeight = 36 + 56 + 4;   // DrawHeader(36) + DrawProfileBar(56) + spacing
            float mainY = headerAndProfileHeight;
            float mainHeight = position.height - mainY - 6;
            float leftWidth = position.width * 0.52f - 6;
            float rightX = position.width * 0.52f + 2;
            float rightWidth = position.width - rightX - 6;
            float statusHeight = 260f;

            // 좌측: Step Panel (전체 높이)
            DrawStepPanelAt(new Rect(6, mainY, leftWidth, mainHeight));

            // 우측 상단: Status Panel (고정 260)
            DrawStatusPanelAt(new Rect(rightX, mainY, rightWidth, statusHeight));

            // 우측 하단: Log Panel (나머지 공간)
            float logY = mainY + statusHeight + 4;
            float logHeight = position.height - logY - 6;
            DrawLogPanelAt(new Rect(rightX, logY, rightWidth, logHeight));
        }

        // ===== 헤더 =====
        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, C_CARD);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), C_NEON_MAGENTA);
            }

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = C_NEON_MAGENTA }
            };
            GUI.Label(new Rect(rect.x + 12, rect.y + 10, 500, 20),
                      "◆ pB-4 DAY 3 INTEGRATION TEST LAB", titleStyle);

            // 대상 NPC 이름
            if (targetBrain != null)
            {
                var nameStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = C_NEON_CYAN },
                    alignment = TextAnchor.MiddleRight
                };
                GUI.Label(new Rect(rect.xMax - 320, rect.y + 10, 300, 20),
                          $"Target ▶ {targetBrain.name}", nameStyle);
            }
        }

        // [v3 NEW] Profile 선택 바
        private void DrawProfileBar()
        {
            var rect = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, C_CARD_LIGHT);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), C_DIVIDER);
            }

            // 1행: 레이블 + 드롭다운 + Apply 버튼
            var row1 = new Rect(rect.x + 10, rect.y + 6, rect.width - 20, 20);
            GUILayout.BeginArea(row1);
            EditorGUILayout.BeginHorizontal();

            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = C_NEON_AMBER },
                fontSize = 11
            };
            GUILayout.Label("◢ NPC PROFILE:", labelStyle, GUILayout.Width(120));

            // 프로필 드롭다운
            string[] profileOptions = new string[PROFILES.Length];
            for (int i = 0; i < PROFILES.Length; i++)
                profileOptions[i] = $"{PROFILES[i].icon}  {PROFILES[i].name}";

            int newIdx = EditorGUILayout.Popup(selectedProfileIdx, profileOptions, GUILayout.Width(180));
            if (newIdx != selectedProfileIdx)
            {
                selectedProfileIdx = newIdx;
                AddLog($"Profile 선택: {CurrentProfile.icon} {CurrentProfile.name} — {CurrentProfile.description}",
                       C_NEON_AMBER);
            }

            GUILayout.Space(8);

            // Apply 버튼
            GUI.color = C_NEON_GREEN;
            if (GUILayout.Button(new GUIContent("▶ Apply to Target",
                "선택된 Profile을 대상 NPC에 적용 — Trust 초기값 설정 + 반응 배율 활성화"),
                EditorStyles.miniButton, GUILayout.Width(130), GUILayout.Height(18)))
            {
                ApplyProfileToTarget();
            }
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();

            // 현재 배율 요약
            var multStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = C_TEXT_DIM } };
            GUILayout.Label(
                $"Trust×{CurrentProfile.trustGainMult:F1}↑/×{CurrentProfile.trustLossMult:F1}↓  " +
                $"Karma react×{CurrentProfile.karmaReactionMult:F1}",
                multStyle);

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();

            // 2행: 설명 + 기대 결과
            var row2 = new Rect(rect.x + 10, rect.y + 30, rect.width - 20, 20);
            GUILayout.BeginArea(row2);
            var descStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = C_TEXT_BRIGHT },
                wordWrap = false
            };
            GUILayout.Label($"   {CurrentProfile.description}   →  기대: {CurrentProfile.expectedOutcome}", descStyle);
            GUILayout.EndArea();
        }

        /// <summary>선택한 Profile을 대상 NPC에 적용. Trust 초기값 세팅 + 배율 활성화.</summary>
        private void ApplyProfileToTarget()
        {
            if (trust == null)
            {
                AddLog("⚠ TrustMatrix null. Apply 불가.", C_NEON_RED);
                return;
            }

            var p = CurrentProfile;
            float delta = p.initialTrust - trust.CurrentTrust;
            trust.ChangeTrust(delta, TrustChangeReason.Manual);

            AddLog($"════ Profile '{p.icon} {p.name}' 적용됨 ════", C_NEON_AMBER);
            AddLog($"  · Trust 초기값: {p.initialTrust:F0}", C_TEXT_BRIGHT);
            AddLog($"  · Trust 증가 배율: ×{p.trustGainMult:F2}", C_TEXT_BRIGHT);
            AddLog($"  · Trust 감소 배율: ×{p.trustLossMult:F2}", C_TEXT_BRIGHT);
            AddLog($"  · Karma 반응 배율: ×{p.karmaReactionMult:F2}", C_TEXT_BRIGHT);
            AddLog($"  · 기대 결과: {p.expectedOutcome}", C_NEON_CYAN);
        }

        // ===== Step 버튼 패널 =====
        private Vector2 scrollSteps;

        private void DrawStepPanelAt(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_CARD);
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20));

            DrawSectionLabel("◢ STEP SEQUENCE (순차 또는 개별 실행)");
            GUILayout.Space(6);

            scrollSteps = EditorGUILayout.BeginScrollView(scrollSteps);

            // ─── STEP 1: 초기 상태 확인 ─────────────────
            DrawStepHeader(1, "초기 상태 확인", "Karma=0, Trust=50, Alignment=Neutral 기대");
            if (DrawStepButton("① Dump 현재 상태", "현재 Karma/Trust/Alignment/Trauma 상태를 로그에 출력"))
            {
                currentStep = 1;
                DumpCurrentState();
            }
            GUILayout.Space(6);

            // ─── STEP 2: Karma 조작 ─────────────────────
            DrawStepHeader(2, "Karma 시스템 테스트", "값 변경 → Tier 전이 → Alignment 연쇄 반응");
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("② -A- Karma +20", "선행 (도움). Saint 방향.", 0.32f))
                { currentStep = 2; DoKarmaChange(+20f, KarmaChangeReason.RescuedCivilian); }
            if (DrawStepButton("② -B- Karma +60 (→ Saint)", "대대적 선행. Tier: Neutral → Saint", 0.32f))
                { currentStep = 2; DoKarmaChange(+60f, KarmaChangeReason.RescuedCivilian); }
            if (DrawStepButton("② -C- Reset to 0", "Karma 초기화", 0.32f))
                { currentStep = 2; DoKarmaReset(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("② -D- Karma -30", "악행. Outlaw 방향.", 0.32f))
                { currentStep = 2; DoKarmaChange(-30f, KarmaChangeReason.KilledCivilian); }
            if (DrawStepButton("② -E- Karma -100 (→ Demon)", "극악행. Tier: Neutral → Demon", 0.32f))
                { currentStep = 2; DoKarmaChange(-100f, KarmaChangeReason.KilledCivilian); }
            if (DrawStepButton("② -F- Add +10 반복", "자연 감소 시각화용. 0.05/5s 감소 확인.", 0.32f))
                { currentStep = 2; DoKarmaChange(+10f, KarmaChangeReason.Manual); }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── STEP 3: Trust 조작 ─────────────────────
            DrawStepHeader(3, "Trust 시스템 테스트", "Hostility → Doubt → Cooperation → BlindTrust");
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("③ -A- Trust +20", "TacticalAlignment (+15 기본)", 0.32f))
                { currentStep = 3; DoTrustChange(+20f, TrustChangeReason.TacticalAlignment); }
            if (DrawStepButton("③ -B- Trust +50 (→ BlindTrust)", "생존 위기 공유. 급격 상승.", 0.32f))
                { currentStep = 3; DoTrustChange(+50f, TrustChangeReason.SharedSurvival); }
            if (DrawStepButton("③ -C- Reset to 50", "Cooperation 기본값으로 복귀", 0.32f))
                { currentStep = 3; DoTrustReset(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("③ -D- Trust -40 (→ Doubt)", "TacticalBetrayal. 급락.", 0.32f))
                { currentStep = 3; DoTrustChange(-40f, TrustChangeReason.TacticalBetrayal); }
            if (DrawStepButton("③ -E- Trust -80 (→ Hostility)", "극심한 배신. 적대 진영 진입 가능", 0.32f))
                { currentStep = 3; DoTrustChange(-80f, TrustChangeReason.TacticalBetrayal); }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── STEP 4: Alignment 강제 관찰 ─────────────
            DrawStepHeader(4, "Alignment 전이 관찰", "Trust+Karma 조합으로 4 진영 중 결정");
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("④ -A- Force Hostile 조건", "Trust=5, Karma=-95 → 적대 강제 유도", 0.49f))
                { currentStep = 4; DoForceAlignmentConditions(5f, -95f); }
            if (DrawStepButton("④ -B- Force Companion 조건", "Trust=95, Karma=+95 → 동반자 조건", 0.49f))
                { currentStep = 4; DoForceAlignmentConditions(95f, +95f); }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── STEP 5: Trauma 테스트 ──────────────────
            DrawStepHeader(5, "Trauma 시스템 테스트", "4단계 Stage 전이: None → AcuteShock → Crossroads → PermanentScarring");
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("⑤ -A- Apply Shock 0.8", "높은 충격 → Stage None → AcuteShock", 0.32f))
                { currentStep = 5; DoApplyShock(0.8f); }
            if (DrawStepButton("⑤ -B- Apply Shock 0.3", "낮은 충격 → Fear만 증가, Stage 불변", 0.32f))
                { currentStep = 5; DoApplyShock(0.3f); }
            if (DrawStepButton("⑤ -C- Complete Exploration", "AcuteShock → Crossroads 진행", 0.32f))
                { currentStep = 5; DoCompleteExploration(); }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── STEP 6: Pivot (Day 3 꽃) ───────────────
            DrawStepHeader(6, "DilemmaPivot — 통합 연쇄 반응", "Personality 변경 + Alignment 강제 + Karma + Trauma 해결");
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("⑥ -A- Pivot: Mercy", "자비 선택 — Karma +15, Trauma 복구", 0.49f))
                { currentStep = 6; DoSimulatePivot(true); }
            if (DrawStepButton("⑥ -B- Pivot: Cruelty", "잔혹 선택 — Karma -25, Alignment 악화", 0.49f))
                { currentStep = 6; DoSimulatePivot(false); }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── STEP 7: Command Filter ─────────────────
            DrawStepHeader(7, "Command 수락 판정 테스트", "Trust × Severity × Loyalty 계산 확인");
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("⑦ -A- Test Trivial", "Trivial 명령. 거의 무조건 수락.", 0.32f))
                { currentStep = 7; DoTestCommand(CommandSeverity.Trivial); }
            if (DrawStepButton("⑦ -B- Test Severe", "위험 명령. Trust+Loyalty 부족 시 거부.", 0.32f))
                { currentStep = 7; DoTestCommand(CommandSeverity.Severe); }
            if (DrawStepButton("⑦ -C- Test Suicidal", "자기희생급. BlindTrust+Companion만 수락.", 0.32f))
                { currentStep = 7; DoTestCommand(CommandSeverity.Suicidal); }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── STEP 8: Speech 강제 발화 (Day 4 T4.1) ───
            DrawStepHeader(8, "Speech 강제 발화 (Day 4)", "EventBus → Dispatcher → Assembler → Renderer 파이프라인 테스트");
            EditorGUILayout.BeginHorizontal();
            if (DrawStepButton("⑧ -A- Force Hostile", "hostile_default 트리거 발행", 0.24f))
                { currentStep = 8; DoForceSpeech("hostile_default", TDA.PB4.Data.NPCAlignment.Hostile); }
            if (DrawStepButton("⑧ -B- Force Neutral", "neutral_default 트리거 발행", 0.24f))
                { currentStep = 8; DoForceSpeech("neutral_default", TDA.PB4.Data.NPCAlignment.Neutral); }
            if (DrawStepButton("⑧ -C- Force Friendly", "friendly_default 트리거 발행", 0.24f))
                { currentStep = 8; DoForceSpeech("friendly_default", TDA.PB4.Data.NPCAlignment.Friendly); }
            if (DrawStepButton("⑧ -D- Force Companion", "companion_default 트리거 발행", 0.24f))
                { currentStep = 8; DoForceSpeech("companion_default", TDA.PB4.Data.NPCAlignment.Companion); }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── STEP 9: 엔드-투-엔드 시나리오 3종 ───────────
            DrawStepHeader(9, "엔드-투-엔드 시나리오", "3가지 서사 중 선택 — 자동 재생");

            // 현재 진행 중이면 진행률 표시
            if (scenarioRunning)
            {
                var progressRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(progressRect, C_CARD_LIGHT);
                float progress = Mathf.Clamp01(scenarioStep / (float)Mathf.Max(1, currentScenario?.steps.Count ?? 1));
                var fillRect = new Rect(progressRect.x, progressRect.y, progressRect.width * progress, progressRect.height);
                EditorGUI.DrawRect(fillRect, C_NEON_MAGENTA);

                var progressStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = C_TEXT_BRIGHT },
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10
                };
                GUI.Label(progressRect,
                    $"▶ {currentScenario?.title ?? ""} — Step {scenarioStep}/{currentScenario?.steps.Count ?? 0}",
                    progressStyle);

                GUILayout.Space(4);
                if (DrawStepButton("■ 시나리오 중단", "진행 중인 시나리오를 즉시 중단"))
                    StopScenario();
            }
            else
            {
                // 4개 시나리오 버튼
                if (DrawStepButton("⑧-A 🦸 '영웅의 길' — 도덕적 성장 (6단계, 18초)",
                    "선행을 쌓아가며 Neutral → Saint → Companion까지 도달. Trust와 Karma 동시 상승."))
                { currentStep = 8; RunScenario(BuildHeroScenario()); }

                GUILayout.Space(2);
                if (DrawStepButton("⑧-B 💀 '배신자의 길' — 도덕적 타락 (6단계, 18초)",
                    "점진적 악행과 배신. Neutral → Outlaw → Demon + Trust 파탄. Hostile NPC 탄생."))
                { currentStep = 8; RunScenario(BuildBetrayerScenario()); }

                GUILayout.Space(2);
                if (DrawStepButton("⑧-C 🎭 '배신의 드라마' — 롤러코스터 (5단계, 15초)",
                    "선행 → 배신 → 트라우마 → 화해까지. 모든 시스템의 극적 상호작용 체험."))
                { currentStep = 8; RunScenario(BuildDramaScenario()); }

                GUILayout.Space(2);
                if (DrawStepButton("⑧-D 🌲 '숲에서의 조우' — 짧은 만남 (5단계, 12초)",
                    "우연히 만난 NPC. 경계 → 호의 → 공감의 미묘한 Trust 변화. 일상적 상호작용 시뮬레이션."))
                { currentStep = 8; RunScenario(BuildEncounterScenario()); }

                GUILayout.Space(2);
                if (DrawStepButton("⑧-E 🛡 '구원자' — 싸움에 개입 (6단계, 18초)",
                    "공격받던 NPC를 구함. Trauma + Trust + Karma 복합 반응. Profile별 회복 속도 차이 극명."))
                { currentStep = 8; RunScenario(BuildRescueScenario()); }
            }
            GUILayout.Space(8);

            // ─── 초기화 ─────────────────────────────────
            DrawSectionLabel("◢ RESET");
            if (DrawStepButton("⟲ 모든 값 초기화 (Karma=0, Trust=50, Trauma=None, Alignment=Neutral)",
                               "모든 Day 3 상태를 시작값으로 복원"))
            { currentStep = -1; DoFullReset(); }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ===== 상태 패널 =====
        private void DrawStatusPanelAt(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_CARD);
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16));

            EditorGUILayout.BeginHorizontal();
            DrawSectionLabel("◢ LIVE STATUS");
            GUILayout.FlexibleSpace();
            // [v3 NEW] 현재 Profile 배지
            var profileBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = C_NEON_AMBER },
                fontSize = 10,
                alignment = TextAnchor.MiddleRight
            };
            GUILayout.Label($"Profile: {CurrentProfile.icon} {CurrentProfile.name}", profileBadgeStyle);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Karma
            float karma = 0f;
            KarmaTier karmaTier = KarmaTier.Neutral;
            if (KarmaDirector.Instance != null)
            {
                karma = KarmaDirector.Instance.GetKarmaScore(0UL);
                karmaTier = KarmaDirector.Instance.GetTier(0UL);
            }
            DrawStatusRow("Karma", $"{karma:+0.0;-0.0;0}", TierColor(karmaTier), karmaTier.ToString());
            DrawProgressBar(-100f, 100f, karma, TierColor(karmaTier));

            GUILayout.Space(4);

            // Trust
            float trustVal = trust != null ? trust.CurrentTrust : 50f;
            TrustTier trustTier = trust != null ? trust.CurrentTier : TrustTier.Cooperation;
            DrawStatusRow("Trust", $"{trustVal:F1}", TrustTierColor(trustTier), trustTier.ToString());
            DrawProgressBar(0f, 100f, trustVal, TrustTierColor(trustTier));

            GUILayout.Space(4);

            // Alignment
            NPCAlignment curAlign = alignment != null ? alignment.CurrentAlignment : NPCAlignment.Neutral;
            DrawStatusRow("Alignment", curAlign.ToString(), AlignmentColor(curAlign), "");

            GUILayout.Space(4);

            // Trauma
            TraumaStage stage = trauma != null ? trauma.CurrentStage : TraumaStage.None;
            string traumaName = (trauma != null && trauma.ActiveTrauma != null) ? trauma.ActiveTrauma.name : "-";
            DrawStatusRow("Trauma", stage.ToString(), StageColor(stage), traumaName);

            GUILayout.Space(4);

            // Tags
            string tagsStr = tagResolver != null && tagResolver.ActiveTags.Count > 0
                ? string.Join(", ", tagResolver.ActiveTags)
                : "-";
            DrawStatusRow("Tags", $"{(tagResolver?.ActiveTags.Count ?? 0)}", C_TEXT_BRIGHT, tagsStr);

            GUILayout.EndArea();
        }

        private void DrawStatusRow(string label, string value, Color valueColor, string extra)
        {
            EditorGUILayout.BeginHorizontal();
            var labelStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = C_TEXT_DIM }, fontSize = 11 };
            GUILayout.Label(label, labelStyle, GUILayout.Width(75));

            var valStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = valueColor }, fontSize = 13 };
            GUILayout.Label(value, valStyle, GUILayout.Width(90));

            if (!string.IsNullOrEmpty(extra))
            {
                var exStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = C_TEXT_BRIGHT } };
                GUILayout.Label(extra, exStyle);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProgressBar(float min, float max, float val, Color color)
        {
            var rect = GUILayoutUtility.GetRect(0, 6, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, C_DIVIDER);

            float t = Mathf.InverseLerp(min, max, val);
            var fillRect = new Rect(rect.x, rect.y, rect.width * t, rect.height);
            EditorGUI.DrawRect(fillRect, color);
        }

        // ===== 로그 패널 =====
        private void DrawLogPanelAt(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_CARD);
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16));

            EditorGUILayout.BeginHorizontal();
            DrawSectionLabel("◢ LOG STREAM");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
                logs.Clear();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            scrollLog = EditorGUILayout.BeginScrollView(scrollLog, GUILayout.ExpandHeight(true));
            var logStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 10,
                richText = true
            };

            for (int i = logs.Count - 1; i >= 0; i--)
            {
                var entry = logs[i];
                GUI.color = entry.color;
                GUILayout.Label($"[{entry.time}] {entry.message}", logStyle);
            }
            GUI.color = Color.white;
            EditorGUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        // ===== 헬퍼: UI =====
        private void DrawSectionLabel(string text)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = C_NEON_CYAN },
                fontSize = 11
            };
            GUILayout.Label(text, style);
        }

        private void DrawStepHeader(int num, string title, string subtitle)
        {
            EditorGUILayout.BeginHorizontal();
            var numStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = (currentStep == num) ? C_NEON_GREEN : C_NEON_AMBER },
                fontSize = 12
            };
            GUILayout.Label($"STEP {num}", numStyle, GUILayout.Width(60));

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = C_TEXT_BRIGHT },
                fontSize = 11
            };
            GUILayout.Label(title, titleStyle);
            EditorGUILayout.EndHorizontal();

            var subStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = C_TEXT_DIM } };
            GUILayout.Label("   └ " + subtitle, subStyle);
        }

        private bool DrawStepButton(string label, string tooltip, float widthRatio = 1f)
        {
            var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 10 };
            float w = widthRatio < 1f ? position.width * 0.52f * widthRatio - 10 : 0;
            bool clicked;
            if (w > 0)
                clicked = GUILayout.Button(new GUIContent(label, tooltip), btnStyle, GUILayout.Height(22), GUILayout.Width(w));
            else
                clicked = GUILayout.Button(new GUIContent(label, tooltip), btnStyle, GUILayout.Height(22));
            return clicked;
        }

        private void DrawCenteredMessage(string message)
        {
            GUILayout.FlexibleSpace();
            var style = new GUIStyle(EditorStyles.largeLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_TEXT_DIM },
                fontSize = 13,
                wordWrap = true
            };
            GUILayout.Label(message, style);
            GUILayout.FlexibleSpace();
        }

        // ===== 헬퍼: Target 선택 =====
        private void AutoSelectTarget()
        {
            targetBrain = FindAnyObjectByType<HumanoidAIBrain>();
            if (targetBrain == null) return;

            var go = targetBrain.gameObject;
            trust = go.GetComponent<TrustMatrix>();
            trauma = go.GetComponent<TraumaSystem>();
            alignment = go.GetComponent<NPCAlignmentController>();
            pivotResolver = go.GetComponent<DilemmaPivotResolver>();
            tagResolver = go.GetComponent<PersonalityTagResolver>();
            cmdResolver = go.GetComponent<CommandAcceptanceResolver>();

            AddLog($"Target auto-selected: {targetBrain.name}", C_NEON_CYAN);
        }

        // ===== 헬퍼: 색상 매핑 =====
        private static Color TierColor(KarmaTier tier) => tier switch
        {
            KarmaTier.Saint => C_NEON_CYAN,
            KarmaTier.Neutral => C_TEXT_BRIGHT,
            KarmaTier.Outlaw => C_NEON_AMBER,
            KarmaTier.Demon => C_NEON_RED,
            _ => C_TEXT_DIM
        };

        private static Color TrustTierColor(TrustTier tier) => tier switch
        {
            TrustTier.Hostility => C_NEON_RED,
            TrustTier.Doubt => C_NEON_AMBER,
            TrustTier.Cooperation => C_NEON_GREEN,
            TrustTier.BlindTrust => C_NEON_CYAN,
            _ => C_TEXT_DIM
        };

        private static Color AlignmentColor(NPCAlignment a) => a switch
        {
            NPCAlignment.Hostile => C_NEON_RED,
            NPCAlignment.Neutral => C_TEXT_BRIGHT,
            NPCAlignment.Friendly => C_NEON_GREEN,
            NPCAlignment.Companion => C_NEON_CYAN,
            _ => C_TEXT_DIM
        };

        private static Color StageColor(TraumaStage s) => s switch
        {
            TraumaStage.None => C_TEXT_DIM,
            TraumaStage.AcuteShock => C_NEON_RED,
            TraumaStage.Crossroads => C_NEON_MAGENTA,
            TraumaStage.PermanentScarring => new Color(0.4f, 0.5f, 0.9f),
            _ => C_TEXT_DIM
        };

        // ===== 로그 헬퍼 =====
        private void AddLog(string msg, Color? color = null)
        {
            logs.Add(new LogEntry
            {
                time = System.DateTime.Now.ToString("HH:mm:ss"),
                message = msg,
                color = color ?? C_TEXT_BRIGHT
            });
            if (logs.Count > MAX_LOGS)
                logs.RemoveAt(0);
            Repaint();
        }

        // ==================================================================
        // Step 실행 메서드들
        // ==================================================================

        private void DumpCurrentState()
        {
            AddLog("════ 현재 상태 ════", C_NEON_CYAN);
            float k = KarmaDirector.Instance?.GetKarmaScore(0UL) ?? 0f;
            var kTier = KarmaDirector.Instance?.GetTier(0UL) ?? KarmaTier.Neutral;
            AddLog($"  Karma: {k:F1} ({kTier})", TierColor(kTier));
            AddLog($"  Trust: {trust?.CurrentTrust ?? 50f:F1} ({trust?.CurrentTier ?? TrustTier.Cooperation})",
                   TrustTierColor(trust?.CurrentTier ?? TrustTier.Cooperation));
            AddLog($"  Alignment: {alignment?.CurrentAlignment ?? NPCAlignment.Neutral}",
                   AlignmentColor(alignment?.CurrentAlignment ?? NPCAlignment.Neutral));
            AddLog($"  Trauma: {trauma?.CurrentStage ?? TraumaStage.None}",
                   StageColor(trauma?.CurrentStage ?? TraumaStage.None));
            AddLog($"  Active Tags: [{(tagResolver != null ? string.Join(", ", tagResolver.ActiveTags) : "")}]");
        }

        private void DoKarmaChange(float delta, KarmaChangeReason reason)
        {
            if (KarmaDirector.Instance == null)
            {
                AddLog("⚠ KarmaDirector.Instance null. 씬에 KarmaDirector 배치 필요.", C_NEON_RED);
                return;
            }
            KarmaDirector.Instance.ChangeKarma(0UL, delta, reason);
            AddLog($"Karma {delta:+0.0;-0.0;0} 적용됨 ({reason})", C_NEON_GREEN);
        }

        private void DoKarmaReset()
        {
            if (KarmaDirector.Instance == null) return;
            float cur = KarmaDirector.Instance.GetKarmaScore(0UL);
            KarmaDirector.Instance.ChangeKarma(0UL, -cur, KarmaChangeReason.Manual);
            AddLog("Karma → 0 초기화", C_NEON_GREEN);
        }

        private void DoTrustChange(float delta, TrustChangeReason reason)
        {
            if (trust == null) { AddLog("⚠ TrustMatrix null", C_NEON_RED); return; }

            // [v3 NEW] Profile 배율 적용
            float actualDelta;
            if (delta >= 0f)
                actualDelta = delta * CurrentProfile.trustGainMult;
            else
                actualDelta = delta * CurrentProfile.trustLossMult;

            trust.ChangeTrust(actualDelta, reason);

            string profileTag = selectedProfileIdx == 0 ? "" : $" [{CurrentProfile.icon} ×{(delta >= 0 ? CurrentProfile.trustGainMult : CurrentProfile.trustLossMult):F2}]";
            AddLog($"Trust {actualDelta:+0.0;-0.0;0} 적용 ({reason}){profileTag}",
                   actualDelta >= 0 ? C_NEON_GREEN : C_NEON_AMBER);
        }

        private void DoTrustReset()
        {
            if (trust == null) return;
            // [v3 NEW] Profile이 적용된 경우 해당 initialTrust로, 아니면 50
            float target = selectedProfileIdx == 0 ? 50f : CurrentProfile.initialTrust;
            float delta = target - trust.CurrentTrust;
            trust.ChangeTrust(delta, TrustChangeReason.Manual);
            AddLog($"Trust → {target:F0} ({CurrentProfile.icon} {CurrentProfile.name} 초기값)", C_NEON_GREEN);
        }

        private void DoForceAlignmentConditions(float targetTrust, float targetKarma)
        {
            if (trust == null || KarmaDirector.Instance == null) return;

            float tDelta = targetTrust - trust.CurrentTrust;
            float kDelta = targetKarma - KarmaDirector.Instance.GetKarmaScore(0UL);

            trust.ChangeTrust(tDelta, TrustChangeReason.TacticalAlignment);
            KarmaDirector.Instance.ChangeKarma(0UL, kDelta, KarmaChangeReason.Manual);

            AddLog($"Force 조건: Trust→{targetTrust}, Karma→{targetKarma}. Alignment 전이 대기 (히스테리시스 5s).",
                   C_NEON_AMBER);
        }

        private void DoApplyShock(float intensity)
        {
            if (trauma == null) { AddLog("⚠ TraumaSystem null", C_NEON_RED); return; }
            trauma.ApplyShock(intensity, "TestLab");
            AddLog($"ApplyShock intensity={intensity:F1}", C_NEON_GREEN);
        }

        private void DoCompleteExploration()
        {
            if (trauma == null) return;
            trauma.OnExplorationCompleted();
            AddLog("Exploration completed → Stage 진행 가능", C_NEON_GREEN);
        }

        private void DoSimulatePivot(bool mercy)
        {
            if (pivotResolver == null) { AddLog("⚠ DilemmaPivotResolver null", C_NEON_RED); return; }

            // 즉석에서 테스트용 Pivot SO 생성
            var testPivot = ScriptableObject.CreateInstance<DilemmaPivotSO>();
            testPivot.pivotId = mercy ? "test_mercy" : "test_cruelty";
            testPivot.triggerStage = PivotTriggerStage.Manual;
            testPivot.choices = new List<PivotChoice>
            {
                new PivotChoice
                {
                    choiceId = mercy ? "mercy" : "cruelty",
                    displayText = mercy ? "자비를 베푼다" : "잔혹하게 처리한다",
                    controlDelta = mercy ? -0.1f : +0.2f,
                    stabilityDelta = mercy ? +0.1f : -0.2f,
                    opennessDelta = mercy ? +0.2f : -0.3f,
                    agreeableDelta = mercy ? +0.3f : -0.4f,
                    directnessDelta = 0f,
                    forceAlignmentTransition = true,
                    forcedAlignment = mercy ? NPCAlignment.Friendly : NPCAlignment.Hostile,
                    karmaDelta = mercy ? +15f : -25f,
                    moralAlignment = mercy ? +0.8f : -0.8f,
                    resultTags = new List<string>(),
                    dialogueKey = mercy ? "pivot_mercy" : "pivot_cruelty"
                }
            };

            bool ok = pivotResolver.ApplyChoice(testPivot, testPivot.choices[0].choiceId, 0UL);
            if (ok)
                AddLog($"Pivot '{(mercy ? "Mercy" : "Cruelty")}' 연쇄 발동 → Anchor + Alignment + Karma + Trauma",
                       mercy ? C_NEON_GREEN : C_NEON_RED);
            else
                AddLog("Pivot 적용됨 (서버 ServerRpc 위임이었을 수 있음)", C_NEON_AMBER);
        }

        private void DoTestCommand(CommandSeverity severity)
        {
            if (cmdResolver == null) { AddLog("⚠ CommandAcceptanceResolver null", C_NEON_RED); return; }

            var req = new CommandRequest
            {
                commandId = FixedCommandId.From("test_cmd"),
                issuerPlayerId = 0UL,
                severity = severity,
                targetPosition = Vector3.zero,
                targetEntityId = 0UL
            };

            bool accepted = cmdResolver.TryAccept(req, out float acceptance, out string reason);
            string result = accepted ? "✓ 수락" : "✗ 거부";
            AddLog($"Command [{severity}]: {result} (acceptance={acceptance:F2}, {reason})",
                   accepted ? C_NEON_GREEN : C_NEON_RED);
        }

        /// <summary>
        /// [Day 4 T4.1] EventBus.RaiseSpeechTrigger 강제 발행.
        /// Alignment 전이를 기다리지 않고 즉시 발화 파이프라인 테스트.
        /// SpeechDispatcher → SpeechAssembler → DialogueRenderer 연쇄 작동 확인용.
        /// </summary>
        private void DoForceSpeech(string triggerId, TDA.PB4.Data.NPCAlignment alignment)
        {
            if (targetBrain == null) { AddLog("⚠ targetBrain null — 씬에 NPC 없음", C_NEON_RED); return; }

            // 현재 Alignment를 Context로 전달 (실제 Alignment 변경은 안 함)
            var ctx = new TDA.PB4.Core.Events.SpeechTriggerContext
            {
                characterId = targetBrain.name,
                targetPlayerId = 0UL,
                currentAlignment = alignment,
                previousAlignment = alignment,  // 전이 없음, 강제 발화만
                reason = TDA.PB4.Interfaces.Narrative.AlignmentChangeReason.Manual
            };

            AddLog($"🗣 Force Speech: '{triggerId}' (alignment={alignment})", C_NEON_MAGENTA);

            // [FIX] Test Lab 편의: 쿨다운 우회하여 연속 테스트 허용
            var dispatcher = targetBrain.GetComponent<TDA.PB4.AI.Speech.SpeechDispatcher>();
            if (dispatcher != null) dispatcher.ResetCooldowns();

            TDA.PB4.Core.EventBus.RaiseSpeechTrigger(triggerId, ctx);
        }

        private void DoFullReset()
        {
            DoKarmaReset();
            DoTrustReset();

            if (trauma != null)
            {
                // TraumaSystem의 ContextMenu 메서드 이름은 DebugReset
                trauma.SendMessage("DebugReset", SendMessageOptions.DontRequireReceiver);
            }

            AddLog("════ FULL RESET ════", C_NEON_CYAN);
        }

        // ==================================================================
        // 엔드-투-엔드 시나리오 엔진 (3종 지원)
        // ==================================================================
        private class ScenarioStep
        {
            public string label;
            public System.Action action;
            public Color logColor;
        }

        private class Scenario
        {
            public string title;
            public string ending;               // 종료 시 요약 메시지
            public Color endingColor;
            public float stepInterval = 3f;     // 각 step 간 간격 (초)
            public List<ScenarioStep> steps = new List<ScenarioStep>();
        }

        private double scenarioStartTime;
        private int scenarioStep;
        private bool scenarioRunning;
        private Scenario currentScenario;

        private void RunScenario(Scenario scn)
        {
            if (scenarioRunning)
            {
                AddLog("⚠ 다른 시나리오 진행 중. 먼저 중단하세요.", C_NEON_AMBER);
                return;
            }

            AddLog($"════ {scn.title} 시작 ({scn.steps.Count} steps) ════", C_NEON_MAGENTA);
            // [v3 NEW] 선택된 Profile 힌트
            if (selectedProfileIdx != 0)
            {
                AddLog($"   ▶ 상대 NPC: {CurrentProfile.icon} {CurrentProfile.name} — {CurrentProfile.description}",
                       C_NEON_AMBER);
                AddLog($"   ▶ 예상 결과: {CurrentProfile.expectedOutcome}", C_TEXT_DIM);
            }
            DoFullReset();   // DoTrustReset은 Profile의 initialTrust로 가므로 OK

            currentScenario = scn;
            scenarioStartTime = EditorApplication.timeSinceStartup;
            scenarioStep = 0;
            scenarioRunning = true;
            EditorApplication.update += TickScenario;
        }

        private void StopScenario()
        {
            scenarioRunning = false;
            EditorApplication.update -= TickScenario;
            AddLog("⏹ 시나리오 중단됨", C_NEON_AMBER);
            currentScenario = null;
        }

        private void TickScenario()
        {
            if (currentScenario == null) { StopScenario(); return; }

            double elapsed = EditorApplication.timeSinceStartup - scenarioStartTime;
            int targetStep = (int)(elapsed / currentScenario.stepInterval);

            if (targetStep <= scenarioStep) return;
            scenarioStep = targetStep;

            // 모든 step 완료 시 종료 처리
            if (scenarioStep > currentScenario.steps.Count)
            {
                AddLog($"════ {currentScenario.title} 종료 ════", C_NEON_MAGENTA);
                AddLog(currentScenario.ending, currentScenario.endingColor);
                // [v3 NEW] 프로파일별 결과 비교 힌트
                if (selectedProfileIdx != 0)
                {
                    AddLog($"   ※ 이번 회차 NPC: {CurrentProfile.icon} {CurrentProfile.name} " +
                           $"— Trust×{CurrentProfile.trustGainMult:F1}↑/×{CurrentProfile.trustLossMult:F1}↓",
                           C_NEON_AMBER);
                    AddLog($"   ※ 다른 Profile로 재실행하면 결과가 달라집니다!", C_TEXT_DIM);
                }
                AddLog(" ", Color.white);
                DumpCurrentState();
                scenarioRunning = false;
                EditorApplication.update -= TickScenario;
                currentScenario = null;
                Repaint();
                return;
            }

            // 현재 step 실행 (1-indexed)
            var step = currentScenario.steps[scenarioStep - 1];
            AddLog($"[{currentScenario.title} {scenarioStep}/{currentScenario.steps.Count}] {step.label}",
                   step.logColor);
            try { step.action?.Invoke(); }
            catch (System.Exception e) { AddLog($"⚠ Step 실행 오류: {e.Message}", C_NEON_RED); }

            Repaint();
        }

        // ─── 시나리오 A: 영웅의 길 ─────────────────────────
        private Scenario BuildHeroScenario()
        {
            var scn = new Scenario
            {
                title = "🦸 영웅의 길",
                stepInterval = 3f,
                ending = "✨ 플레이어는 진정한 영웅이 되었습니다. NPC는 자기희생도 수락하는 Companion으로 성장.",
                endingColor = C_NEON_CYAN,
            };
            scn.steps.Add(new ScenarioStep
            {
                label = "민간인 구조 (+30 RescuedCivilian)",
                logColor = C_NEON_GREEN,
                action = () => DoKarmaChange(+30f, KarmaChangeReason.RescuedCivilian)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "동료를 살림 (+20 RescueAction) → Trust 상승",
                logColor = C_NEON_GREEN,
                action = () => DoTrustChange(+40f, TrustChangeReason.RescueAction)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "추가 선행 (+30 RescuedCivilian) → Saint 진입",
                logColor = C_NEON_CYAN,
                action = () => DoKarmaChange(+30f, KarmaChangeReason.RescuedCivilian)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "생존 위기 공유 (+20 SharedSurvival) → BlindTrust",
                logColor = C_NEON_CYAN,
                action = () => DoTrustChange(+30f, TrustChangeReason.SharedSurvival)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "도덕적 선택 — Mercy (희생을 받아들임)",
                logColor = C_NEON_CYAN,
                action = () => DoSimulatePivot(true)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "Suicidal 명령 테스트 — Companion이면 수락",
                logColor = C_NEON_CYAN,
                action = () => DoTestCommand(CommandSeverity.Suicidal)
            });
            return scn;
        }

        // ─── 시나리오 B: 배신자의 길 ───────────────────────
        private Scenario BuildBetrayerScenario()
        {
            var scn = new Scenario
            {
                title = "💀 배신자의 길",
                stepInterval = 3f,
                ending = "💔 플레이어는 악명높은 배신자가 되었습니다. 동료였던 NPC가 이제 적이 되어 공격합니다.",
                endingColor = C_NEON_RED,
            };
            scn.steps.Add(new ScenarioStep
            {
                label = "동료를 위험에 버림 (-40 TacticalBetrayal)",
                logColor = C_NEON_AMBER,
                action = () => DoTrustChange(-40f, TrustChangeReason.TacticalBetrayal)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "민간인 살해 (-30 KilledCivilian)",
                logColor = C_NEON_AMBER,
                action = () => DoKarmaChange(-30f, KarmaChangeReason.KilledCivilian)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "완전한 배신 (-70 AbandonAction) → Hostility 진입",
                logColor = C_NEON_RED,
                action = () => DoTrustChange(-50f, TrustChangeReason.AbandonAction)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "추가 학살 (-30 KilledCivilian) → Outlaw",
                logColor = C_NEON_RED,
                action = () => DoKarmaChange(-30f, KarmaChangeReason.KilledCivilian)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "극악행 (-40 BetrayedAlly) → Demon Tier 근접",
                logColor = C_NEON_RED,
                action = () => DoKarmaChange(-40f, KarmaChangeReason.BetrayedAlly)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "도덕적 선택 — Cruelty (잔혹) → Hostile 강제 + Anchor 왜곡",
                logColor = C_NEON_RED,
                action = () => DoSimulatePivot(false)
            });
            return scn;
        }

        // ─── 시나리오 C: 배신의 드라마 (롤러코스터) ─────────
        private Scenario BuildDramaScenario()
        {
            var scn = new Scenario
            {
                title = "🎭 배신의 드라마",
                stepInterval = 3f,
                ending = "🌅 극적 롤러코스터 끝에 화해. 모든 Day 3 시스템이 유기적으로 연결됨을 확인.",
                endingColor = C_NEON_MAGENTA,
            };
            scn.steps.Add(new ScenarioStep
            {
                label = "대대적 선행 (+80) → Saint",
                logColor = C_NEON_CYAN,
                action = () => DoKarmaChange(+80f, KarmaChangeReason.RescuedCivilian)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "Trust 급상승 (+40 SharedSurvival)",
                logColor = C_NEON_GREEN,
                action = () => DoTrustChange(+40f, TrustChangeReason.SharedSurvival)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "급격한 배신 — Pivot Cruelty",
                logColor = C_NEON_RED,
                action = () => DoSimulatePivot(false)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "Trauma 발생 — Shock 0.8",
                logColor = C_NEON_AMBER,
                action = () => DoApplyShock(0.8f)
            });
            scn.steps.Add(new ScenarioStep
            {
                label = "화해 — Pivot Mercy → Trauma Crossroads 해결",
                logColor = C_NEON_MAGENTA,
                action = () => DoSimulatePivot(true)
            });
            return scn;
        }

        // ─── 시나리오 D: 숲에서의 조우 (잔잔한 일상) ──────
        //   설계 의도: Karma 변화는 없고 Trust만 미묘하게 움직이는 "일상적 상호작용".
        //            RPG에서 스쳐 지나가는 NPC와의 짧은 만남을 시뮬레이션.
        //            엔딩은 Cooperation 근처 — 결정적 관계가 되진 않지만 작은 신뢰 남김.
        private Scenario BuildEncounterScenario()
        {
            var scn = new Scenario
            {
                title = "🌲 숲에서의 조우",
                stepInterval = 2.5f,   // 짧게 — 12.5초짜리 vignette
                ending = "🍃 잠시 스쳐간 만남이었지만 작은 신뢰가 남았습니다. 낯선 이가 동료는 아니지만 적도 아닌 자가 되었습니다.",
                endingColor = C_NEON_GREEN,
            };

            scn.steps.Add(new ScenarioStep
            {
                label = "숲길에서 NPC와 마주침 — 초기 상태 관찰",
                logColor = C_NEON_CYAN,
                action = () => {
                    AddLog("   · 플레이어가 숲길을 걷다 낯선 NPC와 마주쳤다.", C_TEXT_DIM);
                    AddLog("   · 두 사람은 서로를 훑어본다. 무기를 꺼내지는 않았다.", C_TEXT_DIM);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "경계심 (Trust -20 IncompetenceExposure) → Doubt 진입",
                logColor = C_NEON_AMBER,
                action = () => {
                    AddLog("   · NPC는 무기에 손을 대고 거리를 유지한다.", C_TEXT_DIM);
                    DoTrustChange(-20f, TrustChangeReason.IncompetenceExposure);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "작은 호의 — 물과 식량 나누기 (Trust +10 ResourceSharing)",
                logColor = C_NEON_GREEN,
                action = () => {
                    AddLog("   · 플레이어가 먼저 물 한 모금을 건넨다. NPC가 조심스레 받는다.", C_TEXT_DIM);
                    DoTrustChange(+10f, TrustChangeReason.ResourceSharing);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "뜻밖의 위협 — 함께 맞섬 (Trust +20 SharedSurvival) → Cooperation 복귀",
                logColor = C_NEON_CYAN,
                action = () => {
                    AddLog("   · 덤불에서 들짐승이 튀어나왔다. 둘은 반사적으로 서로를 엄호한다.", C_TEXT_DIM);
                    DoTrustChange(+20f, TrustChangeReason.SharedSurvival);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "작별 전 짧은 부탁 — Trivial 명령 테스트",
                logColor = C_NEON_GREEN,
                action = () => {
                    AddLog("   · \"잠깐 여기 있어줘. 길을 확인하고 올게.\"", C_TEXT_DIM);
                    DoTestCommand(CommandSeverity.Trivial);
                }
            });

            return scn;
        }

        // ─── 시나리오 E: 구원자 (싸움 개입) ────────────────
        //   설계 의도: 플레이어가 공격받던 NPC를 구조하는 상황.
        //            Karma + Trust + Trauma 3개 축이 동시에 움직이는 복합 시나리오.
        //            Profile별 차이가 극명함 — Coward는 Trauma 깊게/Altruist는 즉시 BlindTrust.
        //            실질적으로 동료가 되는 관문 — Week 6까지 이어질 깊은 관계의 시작점.
        private Scenario BuildRescueScenario()
        {
            var scn = new Scenario
            {
                title = "🛡 구원자",
                stepInterval = 3f,
                ending = "🤝 피 묻은 손으로 잡은 첫 악수. 어떤 관계는 구원에서 시작됩니다. " +
                         "Companion 조건을 일시에 채운 가장 빠른 길.",
                endingColor = C_NEON_CYAN,
            };

            scn.steps.Add(new ScenarioStep
            {
                label = "비명소리 — 싸움 현장 목격",
                logColor = C_NEON_AMBER,
                action = () => {
                    AddLog("   · 숲길 저편에서 비명이 들린다.", C_TEXT_DIM);
                    AddLog("   · 한 남자가 도적 셋에게 둘러싸여 얻어맞고 있다.", C_TEXT_DIM);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "피해자의 충격 — Apply Shock 0.6 → AcuteShock",
                logColor = C_NEON_RED,
                action = () => {
                    AddLog("   · 남자는 이미 반쯤 의식을 잃은 상태. 공포가 얼굴에 새겨져 있다.", C_TEXT_DIM);
                    DoApplyShock(0.6f);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "플레이어 개입 — 도적 처치 (+20 KilledOutlaw)",
                logColor = C_NEON_GREEN,
                action = () => {
                    AddLog("   · 플레이어가 칼을 뽑고 뛰어든다. 도적들이 픽픽 쓰러진다.", C_TEXT_DIM);
                    DoKarmaChange(+20f, KarmaChangeReason.KilledOutlaw);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "구조됨 — 압도적 Trust 반응 (+40 RescueAction)",
                logColor = C_NEON_CYAN,
                action = () => {
                    AddLog("   · 남자가 눈을 뜨고 플레이어를 올려다본다.", C_TEXT_DIM);
                    AddLog("   · \"...당신이... 날 구했어...?\"", C_TEXT_DIM);
                    DoTrustChange(+40f, TrustChangeReason.RescueAction);
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "치유와 회복 — Complete Exploration (Trauma 진행)",
                logColor = C_NEON_GREEN,
                action = () => {
                    AddLog("   · 플레이어가 물을 건넨다. 남자가 조금씩 몸을 일으킨다.", C_TEXT_DIM);
                    DoTrustChange(+10f, TrustChangeReason.ResourceSharing);
                    DoCompleteExploration();   // Trauma Stage 진행
                }
            });

            scn.steps.Add(new ScenarioStep
            {
                label = "함께 걷기 — 연대감 (+20 SharedSurvival)",
                logColor = C_NEON_CYAN,
                action = () => {
                    AddLog("   · \"...어디로 가시오? 길이 위험하니 같이 갑시다.\"", C_TEXT_DIM);
                    DoTrustChange(+20f, TrustChangeReason.SharedSurvival);
                }
            });

            return scn;
        }
    }
}
