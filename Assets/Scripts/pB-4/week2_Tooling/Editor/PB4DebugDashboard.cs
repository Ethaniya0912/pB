// =============================================================================
// PB4DebugDashboard.cs  |  pB-4 Week 2 Day 2 T2.5 (v2 Futuristic Grid)
// 배치: Assets/Scripts/pB-4/week2_Tooling/Editor/PB4DebugDashboard.cs
//
// 역할: 매뉴얼 mockup 기준 3×2 그리드 + 퓨처리스틱 다크 테마.
//       상단 Target NPC bar + 6 패널 (Personality/Needs/ActiveTags/Trust/Trauma/BT+Log).
//       Utility 요약은 BT+Log 패널 내부에 통합.
//
// 디자인:
//   - 3×2 그리드 (Row 1: Personality | Needs | ActiveTags / Row 2: Trust | Trauma | BT+Log)
//   - 다크 네이비 배경 + 네온 시안/마젠타/앰버 액센트
//   - 카드 배경 + 얇은 테두리
//   - Tier/Stage 네온 배지
//   - Utility 막대 그라디언트 (DrawRect 여러 층)
//   - minSize: 1080×760 (3열이므로 너비 넉넉히 필요)
//
// API 호환:
//   - TrustMatrix.ChangeTrust(float, TrustChangeReason)
//   - TrustMatrix.CurrentTier → (int)
//   - TraumaSystem.CurrentStage → (int)
//   - TraumaSystem.ApplyShock / GetFearMultiplier / DebugReset → 리플렉션 (없어도 동작)
// =============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.Tooling.Editor
{
    public class PB4DebugDashboard : EditorWindow
    {
        // ==================================================================
        // 타겟 참조
        // ==================================================================
        private HumanoidAIBrain selectedBrain;
        private SerializedObject brainSO;
        private PersonalityTagResolver resolver;
        private TrustMatrix trustMatrix;
        private TraumaSystem traumaSystem;
        private UtilityMasterFormula formula;

        private SerializedProperty p_personality;
        private SerializedProperty p_control, p_stability, p_openness, p_agreeable, p_directness;
        private SerializedProperty p_fear, p_hunger, p_greed, p_fatigue;
        private SerializedProperty p_u_attack, p_u_flee, p_u_loot, p_u_move, p_u_followcommand;
        private SerializedProperty p_currentState;

        // ==================================================================
        // 상태
        // ==================================================================
        private bool autoRefresh = true;
        private float lastRefreshTime;
        private const float kRefreshInterval = 0.3f;

        private HumanoidAIBrain[] cachedBrains = new HumanoidAIBrain[0];
        private string[] cachedBrainNames = new string[] { "(Not playing)" };
        private int selectedIndex = -1;
        private float lastBrainCacheTime;
        private const float kBrainCacheInterval = 1.0f;

        private List<string> recentLogs = new List<string>();
        private const int kMaxLogLines = 5;

        // ==================================================================
        // 언어 설정 (EN / KR / BOTH)
        // ==================================================================
        private enum Lang { EN, KR, BOTH }
        private Lang currentLang = Lang.BOTH;

        /// <summary>언어별 문자열 반환. EN/KR/BOTH.</summary>
        private string L(string en, string kr)
        {
            switch (currentLang)
            {
                case Lang.EN: return en;
                case Lang.KR: return kr;
                case Lang.BOTH:
                default: return $"{en} ({kr})";
            }
        }

        /// <summary>짧은 공간용 — BOTH일 때도 English만 쓰고 KR은 괄호 없이 다음 줄.</summary>
        private string LShort(string en, string kr)
        {
            switch (currentLang)
            {
                case Lang.EN: return en;
                case Lang.KR: return kr;
                case Lang.BOTH:
                default: return en;  // 버튼 등 좁은 공간은 EN만
            }
        }

        // ==================================================================
        // 테마
        // ==================================================================
        private enum Theme { Dark, Light }
        private Theme currentTheme = Theme.Dark;

        // 색상 (현재 테마 기준, ApplyTheme이 값 채움)
        private Color C_BG;
        private Color C_CARD;
        private Color C_CARD_BORDER;
        private Color C_NEON_CYAN;
        private Color C_NEON_MAGENTA;
        private Color C_NEON_AMBER;
        private Color C_NEON_GREEN;
        private Color C_NEON_RED;
        private Color C_TEXT_BRIGHT;
        private Color C_TEXT_DIM;
        private Color C_SLIDER_BG;

        private void ApplyTheme()
        {
            if (currentTheme == Theme.Dark)
            {
                C_BG = new Color(0.043f, 0.059f, 0.102f);  // #0B0F1A
                C_CARD = new Color(0.075f, 0.098f, 0.149f);  // #131926
                C_CARD_BORDER = new Color(0.122f, 0.165f, 0.251f);  // #1F2A40
                C_NEON_CYAN = new Color(0.000f, 0.898f, 1.000f);  // #00E5FF
                C_NEON_MAGENTA = new Color(1.000f, 0.180f, 0.604f);  // #FF2E9A
                C_NEON_AMBER = new Color(1.000f, 0.702f, 0.000f);  // #FFB300
                C_NEON_GREEN = new Color(0.000f, 1.000f, 0.612f);  // #00FF9C
                C_NEON_RED = new Color(1.000f, 0.231f, 0.361f);  // #FF3B5C
                C_TEXT_BRIGHT = new Color(0.847f, 0.882f, 0.933f);  // #D8E1EE
                C_TEXT_DIM = new Color(0.420f, 0.478f, 0.580f);  // #6B7A94
                C_SLIDER_BG = new Color(0.098f, 0.122f, 0.176f);
            }
            else  // Light
            {
                C_BG = new Color(0.95f, 0.96f, 0.98f);     // 매우 밝은 블루그레이
                C_CARD = new Color(1.00f, 1.00f, 1.00f);     // 화이트
                C_CARD_BORDER = new Color(0.82f, 0.86f, 0.90f);     // 라이트 그레이
                C_NEON_CYAN = new Color(0.00f, 0.54f, 0.66f);     // 다크 시안
                C_NEON_MAGENTA = new Color(0.82f, 0.08f, 0.44f);     // 다크 핑크
                C_NEON_AMBER = new Color(0.78f, 0.46f, 0.00f);     // 다크 오렌지
                C_NEON_GREEN = new Color(0.00f, 0.60f, 0.35f);     // 다크 그린
                C_NEON_RED = new Color(0.82f, 0.18f, 0.26f);     // 다크 레드
                C_TEXT_BRIGHT = new Color(0.10f, 0.14f, 0.20f);     // 거의 블랙
                C_TEXT_DIM = new Color(0.42f, 0.48f, 0.56f);     // 미디엄 그레이
                C_SLIDER_BG = new Color(0.88f, 0.91f, 0.94f);     // 라이트 슬라이더 바
            }
        }


        // ==================================================================
        // 툴팁 문자열 (Tech + Gameplay 병기)
        // ==================================================================
        // ==================================================================
        // 툴팁 문자열 (첫 줄 = 제목, 나머지 = 본문)
        // ==================================================================
        private static class T
        {
            // ===== 카드 제목 7개 =====
            public const string Target =
                "Target NPC (대상 NPC)\n" +
                "Scene 내 HumanoidAIBrain 드롭다운. 관찰·조작할 NPC 선택.\n" +
                "다수 NPC 환경에서 특정 개체의 내면을 추적할 때 사용.";

            public const string Personality =
                "Personality (성격 5축)\n" +
                "PersonalityMatrix 5축: control/stability/openness/agreeable/directness (0-1).\n" +
                "NPC의 성격 프로필. 15개 태그 규칙의 입력값.\n" +
                "슬라이더 변경 시 PersonalityTagResolver가 즉시 재평가.";

            public const string Needs =
                "Needs (생리 욕구)\n" +
                "fear, hunger, greed, fatigue (0-1). UtilityMasterFormula 주 입력.\n" +
                "예: hunger↑ → u_loot 점수 상승 → Loot 행동 선택 가능성↑.";

            public const string ActiveTags =
                "Active Tags (발현 태그)\n" +
                "PersonalityTagResolver.ActiveTags. 15 TagRule을 Personality+Needs로 평가.\n" +
                "NPC의 '행동 성향' 리스트. 예: Coward (stability<0.3), Reckless (openness>0.7).\n" +
                "Utility 계산 시 각 태그가 modifier로 가중.";

            public const string Trust =
                "Trust (신뢰도)\n" +
                "trustMatrix.CurrentTrust (0-100) + CurrentTier (enum).\n" +
                "9종 TrustChangeReason으로 delta 변화. Tier 전이 시 EventBus 이벤트 발행.\n" +
                "NPC-Player 관계도. 명령 수용 여부, 희생 감수 정도 결정.\n" +
                "Tier 4단계: Hostility → Doubt → Cooperation → BlindTrust.";

            public const string Trauma =
                "Trauma (트라우마)\n" +
                "TraumaSystem. Stage 4단계: None/AcuteShock/Crossroads/PermScarring.\n" +
                "ApplyShock(severity, reason) → stability -0.5, agreeable -0.2.\n" +
                "심리적 충격과 회복. 쇼크 → 성격 변화 → 태그 변화 → 행동 전환.\n" +
                "회복(Crossroads)은 Trust + Exploration 누적으로 결정.";

            public const string BTStateLog =
                "BT State + Log (상태 + 로그)\n" +
                "HumanoidBTState: Idle/Move/Loot/Attack/Flee/FollowCommand.\n" +
                "Utility 점수 최대값 행동으로 전이 (minStateHoldTicks 후 변경 가능).\n" +
                "NPC가 지금 무엇을 하고 있는지 + 왜 그 결정을 했는지 추적.";

            // ===== Personality 슬라이더 5개 =====
            public const string Control =
                "Control (자제력)\n" +
                "personality.control. Range 0-1.\n" +
                "충동 억제 vs 즉각 반응.\n" +
                "High: 전술적·계산적 (Cautious 발현).\n" +
                "Low:  본능적·즉흥적 (Reckless, Greedy 가중치↑).";

            public const string Stability =
                "Stability (안정성)\n" +
                "personality.stability. Trauma의 주 타격 대상 (-0.5).\n" +
                "정서 안정성. 위기 대처 능력.\n" +
                "High: 냉정한 판단 (Stoic, Confident 발현).\n" +
                "Low:  공포 반응 강함 (Coward, Anxious 발현). fear 기본값 상승.";

            public const string Openness =
                "Openness (개방성)\n" +
                "personality.openness.\n" +
                "새로움·위험 감수도.\n" +
                "High: 탐험·도전 지향 (Reckless, Curious 발현).\n" +
                "Low:  안전·기존 행동 선호 (Cautious 발현).";

            public const string Agreeable =
                "Agreeable (우호성)\n" +
                "personality.agreeable. Trauma로 -0.2.\n" +
                "타인 협력 성향.\n" +
                "High: 이타적·팀워크 (Loyal, Altruist 발현).\n" +
                "Low:  이기적·독자 행동 (Selfish, Blunt 발현).";

            public const string Directness =
                "Directness (직설성)\n" +
                "personality.directness.\n" +
                "의사 표현·행동 직접성.\n" +
                "High: 직설적·공격적 (Aggressive, Blunt 발현).\n" +
                "Low:  우회적·외교적 (Diplomatic 발현).";

            // ===== Needs 4개 + obedience =====
            public const string Fear =
                "fear (공포)\n" +
                "BaseAIBrain.fear. Range 0-1, 기본값 0.3.\n" +
                "TraumaSystem이 fearMultiplier로 증폭 가능 (최대 ×1.5).\n" +
                "생존 위협 인지. u_flee 점수의 주 입력.\n" +
                "Trauma 상태에선 같은 fear 값도 더 큰 도주 충동 유발.";

            public const string Hunger =
                "hunger (배고픔)\n" +
                "BaseAIBrain.hunger.\n" +
                "생존 욕구. u_loot 점수 계산.\n" +
                "Coward NPC는 hunger=0.8이어도 위험한 Loot 안 함.\n" +
                "Aggressive + hunger↑ → 공격적 약탈 행동.";

            public const string Greed =
                "greed (탐욕)\n" +
                "BaseAIBrain.greed. Glutton 태그 가중치 2배.\n" +
                "전리품 수집 욕구. u_loot의 주 입력.\n" +
                "Selfish + greed↑ → 아군 앞에서도 전리품 독점 행동.";

            public const string Fatigue =
                "fatigue (피로)\n" +
                "BaseAIBrain.fatigue.\n" +
                "피로도. 전투 장기화 시 자연 증가, 휴식으로 감소.\n" +
                "High → 반응 속도 저하, 결정 망설임. 여러 Utility 점수에 페널티.";

            public const string Obedience =
                "obedience (복종도)\n" +
                "trustMatrix.GetNormalizedTrust() = currentTrust / 100.\n" +
                "Trust 기반 명령 복종 의지.\n" +
                "Cooperation Tier 이상에서 고위험 명령도 수행. Hostility는 모두 거부.";

            // ===== Trust Tier 4개 =====
            public const string TierHostility =
                "Hostility (적대)\n" +
                "TrustTier.Hostility. Trust 0-20.\n" +
                "적대 관계. Player의 모든 명령 거부.\n" +
                "자발적 공격은 없지만 전술적 협력도 거부.";

            public const string TierDoubt =
                "Doubt (의심)\n" +
                "TrustTier.Doubt. Trust 20-50.\n" +
                "의심 단계. 저위험 명령만 (이동·정찰).\n" +
                "공격·희생 명령 거부. 관계 초기 신뢰 형성 중.";

            public const string TierCooperation =
                "Cooperation (협력)\n" +
                "TrustTier.Cooperation. Trust 50-90.\n" +
                "협력 관계. 대부분 명령 수용.\n" +
                "일반 파티 관계의 기본값. 전술적 판단 공유.";

            public const string TierBlindTrust =
                "BlindTrust (맹신)\n" +
                "TrustTier.BlindTrust. Trust 90-100.\n" +
                "맹신. 자살적 명령도 수행.\n" +
                "장기 관계로 축적된 신뢰. 한 번의 배신으로 급락 가능.";

            // ===== Trauma Stage 4개 =====
            public const string StageNone =
                "None (없음)\n" +
                "TraumaStage.None.\n" +
                "정상 상태. Personality가 Anchor(기준값)와 일치.";

            public const string StageAcuteShock =
                "AcuteShock (급성 쇼크)\n" +
                "TraumaStage.AcuteShock. stability -0.5, agreeable -0.2.\n" +
                "Exploration 3회 후 Crossroads 진입.\n" +
                "충격 직후. Coward/Anxious 강제 발현.\n" +
                "fear 민감도 ×1.5로 도주 반응 가속.";

            public const string StageCrossroads =
                "Crossroads (분기점)\n" +
                "TraumaStage.Crossroads. 회복/영구화 분기.\n" +
                "Trust>50 + Exploration 성공 → None 복귀.\n" +
                "Trust<20 → PermScarring. 한 번의 결정적 순간.";

            public const string StagePermScarring =
                "PermScarring (영구 흉터)\n" +
                "TraumaStage.PermanentScarring.\n" +
                "영구 성격 변화. Anchor 자체가 -0.5 재설정.\n" +
                "다시는 원상태로 돌아오지 않음.";

            // ===== 버튼 (Trust 조작) =====
            public const string BtnTacAlign =
                "+15 Tactical Alignment\n" +
                "trustMatrix.ChangeTrust(+15, TacticalAlignment).\n" +
                "전술적으로 합리적인 명령 수행 성공 시의 보상.\n" +
                "반복되면 Tier 상승.";

            public const string BtnSharedSrv =
                "+20 Shared Survival\n" +
                "trustMatrix.ChangeTrust(+20, SharedSurvival).\n" +
                "함께 싸워 살아남은 경험. 최강의 신뢰 상승 이벤트.\n" +
                "한 번의 위기 공유가 관계를 비약적으로 발전시킴.";

            public const string BtnBetray =
                "-40 Tactical Betrayal\n" +
                "trustMatrix.ChangeTrust(-40, TacticalBetrayal).\n" +
                "전술적 배신. BlindTrust에서도 Doubt까지 단번에 낙하.\n" +
                "신뢰는 쌓기 어렵고 무너뜨리기 쉬움.";

            // ===== 버튼 (Tier 설정) =====
            public const string BtnSetHostile =
                "Set Hostility\nTrust=10. 바로 Hostility Tier로 설정.";
            public const string BtnSetDoubt =
                "Set Doubt\nTrust=30. 바로 Doubt Tier로 설정.";
            public const string BtnSetCoop =
                "Set Cooperation\nTrust=60. 바로 Cooperation Tier로 설정.";
            public const string BtnSetBlind =
                "Set BlindTrust\nTrust=95. 바로 BlindTrust Tier로 설정.";

            // ===== 버튼 (Personality 조작) =====
            public const string BtnResolve =
                "RESOLVE TAGS\n" +
                "PersonalityTagResolver.ResolveTagsFromPersonality() 수동 호출.\n" +
                "태그 재평가 강제 트리거. 슬라이더 변경 시 자동 재평가되지만\n" +
                "외부 조작(Trauma 등) 후 수동 확인이 필요할 때.";

            public const string BtnReset =
                "RESET to Anchor\n" +
                "HumanoidAIBrain.GetAnchor() 값으로 personality 복원.\n" +
                "Personality를 초기 기준점으로 되돌림.\n" +
                "⚠ Trauma 효과는 제거되지 않음 (별도 Reset 버튼 필요).";

            // ===== 버튼 (Trauma 조작) =====
            public const string BtnInflict =
                "Inflict 0.8\n" +
                "traumaSystem.ApplyShock(0.8, \"Dashboard_Manual\") + 리플렉션 폴백.\n" +
                "강도 0.8 트라우마 시연용. None → AcuteShock.\n" +
                "즉시 Stability -0.5, Agreeable -0.2 반영.";

            public const string BtnExplore =
                "Explore +1\n" +
                "traumaSystem.OnExplorationCompleted().\n" +
                "Exploration 카운터 +1. Crossroads 단계에서 회복 조건 누적.\n" +
                "Trust↑ + Exploration↑ → None 복귀 가능.";

            public const string BtnTraumaReset =
                "Trauma Reset\n" +
                "traumaSystem.DebugReset() via reflection.\n" +
                "트라우마 완전 초기화 (개발자 전용).\n" +
                "프로덕션 시나리오에선 Exploration + Trust로만 회복.";

            // ===== 배지 3개 =====
            public const string BadgeTier =
                "Trust Tier Badge\n" +
                "trustMatrix.CurrentTier (TrustTier enum). Trust 구간별 enum 반환.\n" +
                "현재 관계 단계를 한눈에 표시.\n" +
                "색상이 단계에 매핑 (적/주/녹/청).";

            public const string BadgeStage =
                "Trauma Stage Badge\n" +
                "traumaSystem.CurrentStage (TraumaStage enum).\n" +
                "현재 트라우마 상태. 단계별로 NPC 행동 양식 크게 달라짐.\n" +
                "AcuteShock 이상이면 경계 필요.";

            public const string BadgeState =
                "currentState\n" +
                "HumanoidAIBrain.currentState (HumanoidBTState enum).\n" +
                "Utility 최대값 행동으로 전이.\n" +
                "NPC가 실시간으로 무엇을 하는지 표시.\n" +
                "6종: 대기/이동/약탈/공격/도주/명령수행.";

            // ===== Utility 6개 =====
            public const string UAttack =
                "U_atk (Attack)\n" +
                "u_attack (private SerializeField). UtilityMasterFormula.ScoreAllActions 계산.\n" +
                "공격 행동 점수. fear↓ + Brave/Aggressive 태그로 상승.\n" +
                "타겟 존재 + Trust>Cooperation 시 Player 적 공격 가능.";

            public const string UFlee =
                "U_flee (Flee)\n" +
                "u_flee. fear × Coward_multiplier (최대 ×2).\n" +
                "도주 점수. 생존 본능.\n" +
                "u_attack보다 높으면 Flee 상태 진입.";

            public const string ULoot =
                "U_loot (Loot)\n" +
                "u_loot. (hunger+greed) × Glutton_multiplier.\n" +
                "전리품 수집 점수.\n" +
                "Greedy/Selfish 태그 NPC는 전투 중에도 Loot 선택 가능.";

            public const string UMove =
                "U_move (Move)\n" +
                "u_move. moveDestination 거리 기반.\n" +
                "기본 이동. 특별한 행동 없을 때의 fallback.";

            public const string UFollowCommand =
                "FollowCmd (Follow Command, 명령 수행)\n" +
                "u_followcommand. obedience × commandUrgency.\n" +
                "Player 명령 수행 점수. '명령을 따르는' 행동.\n" +
                "Cooperation Tier 이상에서 가장 높음.\n" +
                "BlindTrust는 모든 다른 u_*보다 우선될 수 있음.";

            public const string Winner =
                "Winner (선택된 행동)\n" +
                "5 Utility 중 최고 점수 행동. currentState 결정 근거.\n" +
                "NPC가 '하려고 하는' 행동.\n" +
                "u_flee가 Winner = 도망치려는 의도. 다만 minStateHoldTicks 후에만 실제 전이.";

            // ===== 기타 =====
            public const string RecentLog =
                "Recent Log\n" +
                "Dashboard 내부 ring buffer (5줄).\n" +
                "최근 조작·이벤트 요약. 디버깅 중 작업 흐름 추적용.";

            public const string LanguageToggle =
                "Language Toggle\n" +
                "언어 순환: EN → 한글 → EN/한글 (병기).\n" +
                "카드 제목·슬라이더 라벨·배지 텍스트에 적용.\n" +
                "툴팁 내용은 항상 영문+한글 혼합 (학습용).";

            public const string AutoRefresh =
                "Auto Refresh\n" +
                "EditorApplication.update로 0.3초마다 Repaint.\n" +
                "Play 중 실시간 값 반영. 끄면 수동 Repaint 필요.\n" +
                "마우스 이동 시 즉시 갱신 (부드러운 툴팁용).";

            public const string ThemeToggle =
                "Theme Toggle\n" +
                "Dark ↔ Light 테마 전환.\n" +
                "Dark: 다크 네이비 + 네온 액센트 (퓨처리스틱).\n" +
                "Light: 흰 배경 + 채도 낮춘 액센트 (일광 모드).";

            // ===== 발현 태그 15개 =====
            public static readonly System.Collections.Generic.Dictionary<string, string> TagDict
                = new System.Collections.Generic.Dictionary<string, string>
            {
                { "Coward", "Coward (겁쟁이)\nstability<0.3 발현. Flee Utility ×2, Attack ×0.5.\n공포에 쉽게 굴복. 위협 감지 시 도주 우선." },
                { "Brave", "Brave (용감)\nstability>0.7, control>0.5 발현.\nAttack 가중치↑, Flee 저항. 위협에도 맞서는 성향." },
                { "Stoic", "Stoic (금욕)\nstability>0.8, openness<0.4 발현.\n감정 기복 작음. 극단적 상황에서도 냉정 유지." },
                { "Reckless", "Reckless (무모)\nopenness>0.7, control<0.4 발현.\n위험 감수↑. 무모한 공격·탐험 행동 증가." },
                { "Cautious", "Cautious (신중)\nopenness<0.3, control>0.6 발현.\n안전 우선. 위험 요소 분석 후 행동." },
                { "Loyal", "Loyal (충성)\nagreeable>0.7, directness>0.4 발현.\n팀·주인에게 충실. FollowCommand Utility↑." },
                { "Selfish", "Selfish (이기)\nagreeable<0.3, greed>0.5 발현.\n개인 이익 우선. 팀 협력 저하, Loot Utility↑." },
                { "Blunt", "Blunt (무뚝뚝)\ndirectness>0.7, agreeable<0.5 발현.\n직설적 표현. 대화·협상에서 불리." },
                { "Diplomatic", "Diplomatic (외교적)\ndirectness<0.4, agreeable>0.5 발현.\n우회적·배려적 소통. 협상·중재에 유리." },
                { "Anxious", "Anxious (불안)\nstability<0.4 발현. fear 기본값 상승.\n상시 경계 상태. fear 민감도↑." },
                { "Confident", "Confident (자신감)\nstability>0.7, directness>0.5 발현.\n자기 결정 확신. Attack·FollowCommand 신뢰도↑." },
                { "Altruist", "Altruist (이타)\nagreeable>0.8 발현. 희생적 행동.\n아군 보호 우선. 자신 위험 감수하여 구출." },
                { "Curious", "Curious (호기심)\nopenness>0.7, control<0.5 발현.\n새로운 것 탐구. 탐험·상호작용 증가." },
                { "Aggressive", "Aggressive (공격적)\ndirectness>0.7, control<0.4 발현.\n즉각 공격 성향. Attack Utility×1.5." },
                { "Glutton", "Glutton (대식)\ngreed>0.7 발현. Loot Utility×2.\n식량·전리품에 집착. 하임리히 없이는 멈춤 어려움." },
            };
        }

        [MenuItem("Window/pB-4/Humanoid Dashboard")]
        public static void ShowWindow()
        {
            var window = GetWindow<PB4DebugDashboard>("pB-4 Humanoid Dashboard");
            window.minSize = new Vector2(1080, 760);
        }

        private void OnEnable()
        {
            ApplyTheme();
            wantsMouseMove = true;  // 마우스 이동 이벤트 수신 (부드러운 툴팁)
            EditorApplication.update += OnEditorUpdate;
        }
        private void OnDisable() { EditorApplication.update -= OnEditorUpdate; }

        private void OnEditorUpdate()
        {
            if (!autoRefresh || !Application.isPlaying) return;
            if (Time.realtimeSinceStartup - lastRefreshTime < kRefreshInterval) return;
            lastRefreshTime = Time.realtimeSinceStartup;
            Repaint();
        }

        // ==================================================================
        // 메인 OnGUI
        // ==================================================================
        private void OnGUI()
        {
            // 마우스 이동 시 즉시 Repaint (부드러운 툴팁 추적)
            if (Event.current.type == EventType.MouseMove)
                Repaint();

            // 전체 배경 덮기
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            DrawHeader();
            EditorGUILayout.Space(4);
            DrawTargetBar();
            EditorGUILayout.Space(6);

            if (!Application.isPlaying)
            {
                DrawCenteredMessage(L("[ SYSTEM OFFLINE ]  Enter Play mode to begin telemetry.",
                                      "[ 시스템 대기 ]  Play 모드에서 동작합니다."));
                return;
            }

            if (selectedBrain == null || brainSO == null)
            {
                DrawCenteredMessage(L("[ NO TARGET ]  Select a Humanoid NPC from the dropdown above.",
                                      "[ 대상 없음 ]  상단 드롭다운에서 NPC를 선택하세요."));
                return;
            }

            if (selectedBrain.gameObject == null)
            {
                ClearSelection();
                return;
            }

            brainSO.Update();

            // ===== 3×2 그리드 =====
            float w = (position.width - 32) / 3f;  // 좌우 여백 16 + 간격 2 × 2

            // Row 1
            EditorGUILayout.BeginHorizontal();
            DrawCard(w, "② " + L("Personality", "성격 (5축)"), DrawPersonalityPanel, 300, T.Personality);
            GUILayout.Space(2);
            DrawCard(w, "③ " + L("Needs", "생리 욕구"), DrawNeedsPanel, 300, T.Needs);
            GUILayout.Space(2);
            DrawCard(w, "④ " + L("Active Tags", "발현 태그"), DrawActiveTagsPanel, 300, T.ActiveTags);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Row 2
            EditorGUILayout.BeginHorizontal();
            DrawCard(w, "⑤ " + L("Trust", "신뢰도"), DrawTrustPanel, 330, T.Trust);
            GUILayout.Space(2);
            DrawCard(w, "⑥ " + L("Trauma", "트라우마"), DrawTraumaPanel, 330, T.Trauma);
            GUILayout.Space(2);
            DrawCard(w, "⑦ " + L("BT State + Log", "상태 + 로그"), DrawBTStatePanel, 330, T.BTStateLog);
            EditorGUILayout.EndHorizontal();

            bool changed = brainSO.ApplyModifiedProperties();
            if (changed && resolver != null)
            {
                resolver.ResolveTagsFromPersonality(selectedBrain.Personality);
                AddLog(L("Personality changed → tags re-evaluated", "Personality 변경 → 태그 재평가"));
            }

            // Trauma 자동 복귀 타이머 틱 (Dashboard 내부)
            TickTraumaDemoTimer();

            // 수동 툴팁 렌더링 (Unity IMGUI 기본 tooltip이 EditorWindow에서 동작 불안정)
            DrawTooltipOverlay();
        }

        // ==================================================================
        // 수동 툴팁 렌더러 (OnGUI 마지막에 호출)
        // 첫 줄 = 제목 (굵게), 나머지 = 본문
        // ==================================================================
        private void DrawTooltipOverlay()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (string.IsNullOrEmpty(GUI.tooltip)) return;

            // 첫 줄과 나머지 분리
            string title, body;
            int nl = GUI.tooltip.IndexOf('\n');
            if (nl > 0)
            {
                title = GUI.tooltip.Substring(0, nl).Trim();
                body = GUI.tooltip.Substring(nl + 1).Trim();
            }
            else
            {
                title = GUI.tooltip.Trim();
                body = "";
            }

            // 스타일 정의
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = C_NEON_CYAN },
                wordWrap = true
            };
            var bodyStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = C_TEXT_BRIGHT },
                wordWrap = true,
                richText = false
            };

            // 높이 계산
            const float maxWidth = 380f;
            const float innerPad = 10f;
            float innerWidth = maxWidth - innerPad * 2;

            float titleHeight = string.IsNullOrEmpty(title) ? 0 :
                titleStyle.CalcHeight(new GUIContent(title), innerWidth);
            float bodyHeight = string.IsNullOrEmpty(body) ? 0 :
                bodyStyle.CalcHeight(new GUIContent(body), innerWidth);

            float sepHeight = (titleHeight > 0 && bodyHeight > 0) ? 6f : 0f;
            float totalHeight = innerPad * 2 + titleHeight + sepHeight + bodyHeight;

            // 마우스 위치 + 큰 offset (원래 UI 가리지 않도록)
            Vector2 mouse = Event.current.mousePosition;
            float offsetX = 28;
            float offsetY = 28;

            float x, y;

            // 우측 공간 부족 시 마우스 왼쪽으로
            if (mouse.x + offsetX + maxWidth > position.width)
                x = mouse.x - maxWidth - offsetX;
            else
                x = mouse.x + offsetX;

            // 하단 공간 부족 시 마우스 위로
            if (mouse.y + offsetY + totalHeight > position.height)
                y = mouse.y - totalHeight - offsetY;
            else
                y = mouse.y + offsetY;

            // 안전 보정
            if (x < 4) x = 4;
            if (y < 4) y = 4;
            if (x + maxWidth > position.width) x = position.width - maxWidth - 4;
            if (y + totalHeight > position.height) y = position.height - totalHeight - 4;

            Rect container = new Rect(x, y, maxWidth, totalHeight);

            // 배경 + 네온 테두리
            var bg = currentTheme == Theme.Dark
                ? new Color(0.02f, 0.04f, 0.10f, 0.98f)
                : new Color(0.97f, 0.98f, 1.00f, 0.98f);
            var borderColor = new Color(C_NEON_CYAN.r, C_NEON_CYAN.g, C_NEON_CYAN.b, 0.65f);

            EditorGUI.DrawRect(new Rect(container.x - 2, container.y - 2, container.width + 4, container.height + 4),
                borderColor);
            EditorGUI.DrawRect(container, bg);

            // 제목 렌더링
            float cursorY = y + innerPad;
            if (titleHeight > 0)
            {
                Rect titleRect = new Rect(x + innerPad, cursorY, innerWidth, titleHeight);
                GUI.Label(titleRect, title, titleStyle);
                cursorY += titleHeight;

                // 제목 아래 네온 라인
                Rect lineRect = new Rect(x + innerPad, cursorY + 2, innerWidth, 1);
                EditorGUI.DrawRect(lineRect, borderColor);
                cursorY += sepHeight;
            }

            // 본문 렌더링
            if (bodyHeight > 0)
            {
                Rect bodyRect = new Rect(x + innerPad, cursorY, innerWidth, bodyHeight);
                GUI.Label(bodyRect, body, bodyStyle);
            }
        }

        // ==================================================================
        // Trauma 자동 복귀 시연용 타이머 (Dashboard 내부)
        // ==================================================================
        private float traumaDemoEndTime = -1f;
        private const float kTraumaDemoDurationSec = 15f;

        private void TickTraumaDemoTimer()
        {
            if (traumaDemoEndTime < 0f) return;
            if (Time.realtimeSinceStartup >= traumaDemoEndTime)
            {
                // 자동 Reset
                DemoReset();
                traumaDemoEndTime = -1f;
                AddLog(L($"Auto-reset after {kTraumaDemoDurationSec:F0}s",
                         $"시연 {kTraumaDemoDurationSec:F0}s 후 자동 초기화"));
            }
        }

        // ==================================================================
        // 공용 카드 래퍼
        // ==================================================================
        private void DrawCard(float width, string title, System.Action drawBody, float minHeight, string tooltip = "")
        {
            var rect = EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.MinHeight(minHeight));

            // 카드 배경
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, C_CARD);
                DrawNeonBorder(rect, C_CARD_BORDER, 1);
            }

            GUILayout.Space(6);

            // 카드 제목 (네온 시안 + 툴팁)
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = C_NEON_CYAN }
            };
            EditorGUILayout.LabelField(new GUIContent(title, tooltip), titleStyle);

            // 제목 아래 네온 라인
            var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(lineRect, new Color(C_NEON_CYAN.r, C_NEON_CYAN.g, C_NEON_CYAN.b, 0.4f));

            GUILayout.Space(4);

            drawBody?.Invoke();

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        // ==================================================================
        // 상단 헤더
        // ==================================================================
        private void DrawHeader()
        {
            var bgRect = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(bgRect, C_CARD);
                // 하단 네온 라인
                EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.yMax - 1, bgRect.width, 1), C_NEON_CYAN);
            }

            var leftRect = new Rect(bgRect.x + 12, bgRect.y + 8, 300, 20);
            var rightRect = new Rect(bgRect.xMax - 360, bgRect.y + 8, 350, 20);

            // 제목
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = C_NEON_CYAN }
            };
            GUI.Label(leftRect, "◆ pB-4 HUMANOID DASHBOARD v0.3", titleStyle);

            // Auto-refresh + Lang + Theme + Repaint 버튼 (4개)
            GUILayout.BeginArea(rightRect);
            EditorGUILayout.BeginHorizontal();

            GUI.color = autoRefresh ? C_NEON_GREEN : C_TEXT_DIM;
            autoRefresh = GUILayout.Toggle(autoRefresh,
                new GUIContent("■ Auto", T.AutoRefresh),
                EditorStyles.miniButton, GUILayout.Width(60));

            // 언어 토글 (EN → KR → BOTH → EN)
            GUI.color = C_NEON_MAGENTA;
            string langLabel = currentLang == Lang.EN ? "EN" : (currentLang == Lang.KR ? "한글" : "EN/한");
            if (GUILayout.Button(new GUIContent("⚙ " + langLabel, T.LanguageToggle),
                EditorStyles.miniButton, GUILayout.Width(70)))
            {
                currentLang = (Lang)(((int)currentLang + 1) % 3);
                AddLog($"Language → {langLabel}");
            }

            // 테마 토글 (Dark ↔ Light)
            GUI.color = C_NEON_AMBER;
            string themeLabel = currentTheme == Theme.Dark ? "🌙 Dark" : "☀ Light";
            if (GUILayout.Button(new GUIContent(themeLabel, T.ThemeToggle),
                EditorStyles.miniButton, GUILayout.Width(80)))
            {
                currentTheme = currentTheme == Theme.Dark ? Theme.Light : Theme.Dark;
                ApplyTheme();
                AddLog($"Theme → {themeLabel}");
            }

            GUI.color = C_TEXT_BRIGHT;
            if (GUILayout.Button(new GUIContent("⟲", "즉시 Repaint. Auto-refresh 꺼져 있을 때 수동 갱신."),
                EditorStyles.miniButton, GUILayout.Width(30)))
                Repaint();
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ==================================================================
        // Target Bar
        // ==================================================================
        private void DrawTargetBar()
        {
            var rect = EditorGUILayout.BeginVertical(GUILayout.MinHeight(50));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, C_CARD);
                DrawNeonBorder(rect, C_CARD_BORDER, 1);
            }

            GUILayout.Space(4);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = C_NEON_CYAN }
            };
            EditorGUILayout.LabelField(new GUIContent("① " + L("Target NPC", "대상 NPC"), T.Target), titleStyle);

            RefreshBrainCacheIfNeeded();

            EditorGUILayout.BeginHorizontal();

            int newIndex = EditorGUILayout.Popup(selectedIndex, cachedBrainNames, GUILayout.Height(20));
            if (newIndex != selectedIndex && newIndex >= 0 && newIndex < cachedBrains.Length)
            {
                selectedIndex = newIndex;
                SelectBrain(cachedBrains[newIndex]);
            }

            if (GUILayout.Button("↻", GUILayout.Width(28), GUILayout.Height(20)))
                RefreshBrainCacheIfNeeded(force: true);

            EditorGUILayout.EndHorizontal();

            if (selectedBrain != null)
            {
                var subStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = C_TEXT_DIM }
                };
                EditorGUILayout.LabelField(
                    $"▷ GameObject: {selectedBrain.gameObject.name}    ▷ InstanceID: {selectedBrain.GetInstanceID()}",
                    subStyle);
            }

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        // ==================================================================
        // 패널 ② — Personality
        // ==================================================================
        private void DrawPersonalityPanel()
        {
            DrawSlider(p_control, L("Control", "자제력"), T.Control);
            DrawSlider(p_stability, L("Stability", "안정성"), T.Stability);
            DrawSlider(p_openness, L("Openness", "개방성"), T.Openness);
            DrawSlider(p_agreeable, L("Agreeable", "우호성"), T.Agreeable);
            DrawSlider(p_directness, L("Directness", "직설성"), T.Directness);

            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (NeonButton("▶ " + LShort("RESOLVE", "태그 재평가"), C_NEON_CYAN, T.BtnResolve))
            {
                if (resolver != null && selectedBrain != null)
                {
                    resolver.ResolveTagsFromPersonality(selectedBrain.Personality);
                    AddLog(L("Tags resolved", "태그 수동 재평가"));
                }
            }
            if (NeonButton("⟲ " + LShort("RESET", "초기화"), C_NEON_AMBER, T.BtnReset))
            {
                if (selectedBrain != null)
                {
                    var anchor = selectedBrain.GetAnchor();
                    if (p_control != null) p_control.floatValue = anchor.control;
                    if (p_stability != null) p_stability.floatValue = anchor.stability;
                    if (p_openness != null) p_openness.floatValue = anchor.openness;
                    if (p_agreeable != null) p_agreeable.floatValue = anchor.agreeable;
                    if (p_directness != null) p_directness.floatValue = anchor.directness;
                    AddLog(L("Personality → Anchor", "Personality → 기준점 복귀"));
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawHint(L("▸ Slider drag → tags re-evaluated", "▸ 슬라이더 조작 → ActiveTags 자동 재평가"));
        }

        // ==================================================================
        // 패널 ③ — Needs
        // ==================================================================
        private void DrawNeedsPanel()
        {
            float fearMult = GetFearMultiplierSafe();
            string fearLabel = fearMult > 1.01f
                ? $"{L("fear", "공포")}  ×{fearMult:F2}"
                : L("fear", "공포");

            DrawSlider(p_fear, fearLabel, T.Fear, fearMult > 1.01f ? C_NEON_RED : C_NEON_CYAN);
            DrawSlider(p_hunger, L("hunger", "배고픔"), T.Hunger);
            DrawSlider(p_greed, L("greed", "탐욕"), T.Greed);
            DrawSlider(p_fatigue, L("fatigue", "피로"), T.Fatigue);

            GUILayout.Space(4);

            float trust = trustMatrix != null ? trustMatrix.GetNormalizedTrust() : 0.5f;

            var rect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            DrawGradientBar(rect, trust, C_NEON_MAGENTA, C_CARD);

            var overlayStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_TEXT_BRIGHT }
            };
            string obedLabel = L($"obedience (Trust): {trust:F2}", $"복종도 (Trust 반영): {trust:F2}");
            GUI.Label(rect, new GUIContent(obedLabel, T.Obedience), overlayStyle);
        }

        // ==================================================================
        // 패널 ④ — ActiveTags
        // ==================================================================
        private void DrawActiveTagsPanel()
        {
            if (resolver == null)
            {
                DrawWarning(L("PersonalityTagResolver not attached", "TagResolver 미부착"));
                return;
            }

            var tags = resolver.ActiveTags;
            if (tags == null || tags.Count == 0)
            {
                DrawHint(L("(no tags active)", "(발현된 태그 없음)"));
                return;
            }

            // Pill flow (wrap)
            float maxWidth = position.width / 3 - 32;
            float currentX = 0;
            EditorGUILayout.BeginHorizontal();
            foreach (var tag in tags)
            {
                float tagWidth = tag.Length * 7 + 24;
                if (currentX + tagWidth > maxWidth)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    currentX = 0;
                }
                DrawTagPill(tag);
                currentX += tagWidth + 4;
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            DrawHint(L($"▸ Tag count: {tags.Count}", $"▸ 태그 수: {tags.Count}개"));
        }

        private void DrawTagPill(string tagName)
        {
            var rect = GUILayoutUtility.GetRect(tagName.Length * 7 + 24, 22, GUILayout.ExpandWidth(false));

            if (Event.current.type == EventType.Repaint)
            {
                // Glow outline
                var glowColor = new Color(C_NEON_CYAN.r, C_NEON_CYAN.g, C_NEON_CYAN.b, 0.25f);
                EditorGUI.DrawRect(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), glowColor);
                // Fill
                EditorGUI.DrawRect(rect, new Color(0.0f, 0.6f, 0.7f, 0.35f));
                // Border
                DrawNeonBorder(rect, C_NEON_CYAN, 1);
            }

            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_NEON_CYAN }
            };

            // 태그별 툴팁 조회 (없으면 태그 이름만)
            string tagTooltip;
            if (!T.TagDict.TryGetValue(tagName, out tagTooltip))
                tagTooltip = $"{tagName}\n(태그 설명 미등록 - T.TagDict 확인)";

            GUI.Label(rect, new GUIContent(tagName, tagTooltip), labelStyle);
        }

        // ==================================================================
        // 패널 ⑤ — Trust
        // ==================================================================
        private void DrawTrustPanel()
        {
            if (trustMatrix == null)
            {
                DrawWarning(L("TrustMatrix not attached", "TrustMatrix 미부착"));
                return;
            }

            int tierIdx = (int)trustMatrix.CurrentTier;
            string tierName = TierName(tierIdx);
            Color tierColor = TierColor(tierIdx);

            // Tier 네온 배지
            string tierTooltip = GetTierTooltip(tierIdx);
            DrawNeonBadge($"◆ {L("TIER", "등급")}: {tierName} [{tierIdx}]", tierColor, 36, tierTooltip);

            GUILayout.Space(4);

            // 드래그 가능한 Trust 슬라이더 바
            var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            DrawGradientBar(rect, trustMatrix.CurrentTrust / 100f, tierColor, C_SLIDER_BG);

            // 테두리 (드래그 가능함을 시각화)
            if (Event.current.type == EventType.Repaint)
                DrawNeonBorder(rect, new Color(tierColor.r, tierColor.g, tierColor.b, 0.6f), 1);

            // 마우스 드래그 처리
            var ev = Event.current;
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && rect.Contains(ev.mousePosition))
            {
                float t = Mathf.Clamp01((ev.mousePosition.x - rect.x) / rect.width);
                float target = t * 100f;
                float delta = target - trustMatrix.CurrentTrust;
                if (Mathf.Abs(delta) > 0.1f)
                {
                    trustMatrix.ChangeTrust(delta, TrustChangeReason.Manual);
                }
                ev.Use();
                Repaint();
            }

            var valStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = C_TEXT_BRIGHT }
            };
            GUI.Label(rect,
                new GUIContent($"{trustMatrix.CurrentTrust:F1} / 100",
                    "Trust Slider (드래그 가능)\n" +
                    "막대를 좌우로 드래그하여 Trust 값 직접 조작.\n" +
                    "trustMatrix.ChangeTrust(delta, Manual). Range 0-100.\n" +
                    "Tier 색상 + CurrentTrust 수치 실시간 갱신."),
                valStyle);

            GUILayout.Space(6);
            DrawHint(L("▸ MANUAL DELTA:", "▸ 수동 조작:"));

            EditorGUILayout.BeginHorizontal();
            if (NeonButton("+15 " + LShort("Tac", "전술"), C_NEON_GREEN, T.BtnTacAlign))
                trustMatrix.ChangeTrust(15f, TrustChangeReason.TacticalAlignment);
            if (NeonButton("+20 " + LShort("Srv", "생존"), C_NEON_GREEN, T.BtnSharedSrv))
                trustMatrix.ChangeTrust(20f, TrustChangeReason.SharedSurvival);
            if (NeonButton("-40 " + LShort("Btr", "배신"), C_NEON_RED, T.BtnBetray))
                trustMatrix.ChangeTrust(-40f, TrustChangeReason.TacticalBetrayal);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            DrawHint(L("▸ SET TIER:", "▸ 등급 설정:"));

            EditorGUILayout.BeginHorizontal();
            if (NeonButton(LShort("Hostile", "적대"), TierColor(0), T.BtnSetHostile)) SetTrustTo(10f);
            if (NeonButton(LShort("Doubt", "의심"), TierColor(1), T.BtnSetDoubt)) SetTrustTo(30f);
            if (NeonButton(LShort("Coop", "협력"), TierColor(2), T.BtnSetCoop)) SetTrustTo(60f);
            if (NeonButton(LShort("Blind", "맹신"), TierColor(3), T.BtnSetBlind)) SetTrustTo(95f);
            EditorGUILayout.EndHorizontal();
        }

        private void SetTrustTo(float target)
        {
            if (trustMatrix == null) return;
            float delta = target - trustMatrix.CurrentTrust;
            trustMatrix.ChangeTrust(delta, TrustChangeReason.Manual);
        }

        // ==================================================================
        // 패널 ⑥ — Trauma
        // ==================================================================
        private void DrawTraumaPanel()
        {
            if (traumaSystem == null)
            {
                DrawWarning(L("TraumaSystem not attached", "TraumaSystem 미부착"));
                return;
            }

            int stageIdx = (int)traumaSystem.CurrentStage;
            string stageName = StageName(stageIdx);
            Color stageColor = StageColor(stageIdx);

            string stageTooltip = GetStageTooltip(stageIdx);
            DrawNeonBadge($"◆ {L("STAGE", "단계")}: {stageName} [{stageIdx}]", stageColor, 36, stageTooltip);

            GUILayout.Space(4);

            float fearMult = GetFearMultiplierSafe();
            DrawKeyValue(L("FearMultiplier:", "공포 배수:"), $"×{fearMult:F2}",
                fearMult > 1.01f ? C_NEON_RED : C_TEXT_BRIGHT,
                "FearMultiplier (공포 배수)\n" +
                "traumaSystem.GetFearMultiplier(). AcuteShock=1.5, None=1.0.\n" +
                "fear 증폭 계수. 같은 fear 값도 트라우마 상태에선 더 큰 도주 반응 유발.");
            DrawKeyValue(L("Explorations since:", "트라우마 이후 탐험:"), $"{traumaSystem.ExplorationsSinceTrauma}",
                null,
                "Explorations since trauma\n" +
                "traumaSystem.ExplorationsSinceTrauma (int).\n" +
                "트라우마 후 탐험 성공 횟수. Crossroads 회복 조건 (3회 이상).");
            if (traumaSystem.ActiveTrauma != null)
                DrawKeyValue(L("Active trauma:", "활성 트라우마:"),
                    $"{traumaSystem.ActiveTrauma.traumaID} (sev {traumaSystem.ActiveTrauma.severity:F2})",
                    null,
                    "Active trauma\n" +
                    "traumaSystem.ActiveTrauma (TraumaDataSO ref).\n" +
                    "현재 발현된 트라우마의 ID와 심각도.");

            GUILayout.Space(8);

            // 자동 복귀 카운트다운 표시 (시연 모드)
            if (traumaDemoEndTime > 0f)
            {
                float remain = traumaDemoEndTime - Time.realtimeSinceStartup;
                if (remain > 0f)
                {
                    var cdStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 11,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = C_NEON_AMBER }
                    };
                    var cdRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(cdRect, new Color(C_NEON_AMBER.r * 0.2f, C_NEON_AMBER.g * 0.2f, C_NEON_AMBER.b * 0.2f, 0.4f));
                        DrawNeonBorder(cdRect, C_NEON_AMBER, 1);
                    }
                    GUI.Label(cdRect,
                        new GUIContent(
                            L($"⏱ Auto-reset in {remain:F1}s", $"⏱ {remain:F1}초 후 자동 초기화"),
                            "Auto-reset Countdown\n" +
                            "Dashboard 내부 타이머. TraumaSystem 자동 복귀 로직이 없는 환경 시연용.\n" +
                            "매뉴얼 시연 C의 '15초 후 자동 복귀' 시각화. Cancel 버튼으로 중단 가능."),
                        cdStyle);
                }
            }

            DrawHint(L("▸ DEMO BUTTONS:", "▸ 시연 버튼:"));

            EditorGUILayout.BeginHorizontal();
            if (NeonButton(L("Inflict 0.8", "부여 0.8"), C_NEON_RED, T.BtnInflict))
                DemoInflict();
            if (NeonButton(L("Explore +1", "탐험 +1"), C_NEON_GREEN, T.BtnExplore))
            {
                traumaSystem.OnExplorationCompleted();
                AddLog(L("Exploration +1", "탐험 +1"));
            }
            if (NeonButton(L("Reset", "초기화"), C_NEON_AMBER, T.BtnTraumaReset))
                DemoReset();
            EditorGUILayout.EndHorizontal();

            // 시연용 "Cycle 15s" 버튼 (Inflict + 15초 후 자동 Reset)
            EditorGUILayout.BeginHorizontal();
            if (traumaDemoEndTime < 0f)
            {
                if (NeonButton(L("▶ Demo Cycle 15s", "▶ 15s 시연 사이클"), C_NEON_MAGENTA,
                    "Demo Cycle 15s\n" +
                    "Inflict 0.8 → Time.realtimeSinceStartup + 15s 후 DemoReset() 자동 호출.\n" +
                    "매뉴얼 시연 C의 정상 흐름 자동화. Inflict → 공포반응 → 자동회복까지 한 버튼."))
                {
                    DemoInflict();
                    traumaDemoEndTime = Time.realtimeSinceStartup + kTraumaDemoDurationSec;
                    AddLog(L($"Demo cycle started: auto-reset in {kTraumaDemoDurationSec:F0}s",
                             $"시연 사이클 시작: {kTraumaDemoDurationSec:F0}초 후 자동 초기화"));
                }
            }
            else
            {
                if (NeonButton(L("✕ Cancel auto-reset", "✕ 자동 초기화 취소"), C_NEON_AMBER,
                    "시연 사이클 중단. 자동 Reset 예약 해제. Trauma 상태는 유지."))
                {
                    traumaDemoEndTime = -1f;
                    AddLog(L("Demo cycle cancelled", "시연 사이클 취소"));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DemoInflict()
        {
            var applyShock = typeof(TraumaSystem).GetMethod("ApplyShock");
            if (applyShock != null)
            {
                applyShock.Invoke(traumaSystem, new object[] { 0.8f, "Dashboard_Manual" });
                AddLog(L("Trauma inflict 0.8", "트라우마 부여 0.8"));
                return;
            }
            var debugInflict = typeof(TraumaSystem).GetMethod("DebugInflict",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (debugInflict != null)
            {
                debugInflict.Invoke(traumaSystem, null);
                AddLog(L("Trauma inflict (DebugInflict)", "트라우마 부여 (DebugInflict 폴백)"));
                return;
            }
            AddLog(L("⚠ ApplyShock/DebugInflict both missing", "⚠ ApplyShock/DebugInflict 둘 다 없음"));
        }

        private void DemoReset()
        {
            var method = typeof(TraumaSystem).GetMethod("DebugReset",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(traumaSystem, null);
                AddLog(L("Trauma Reset", "트라우마 초기화"));
            }
            else
            {
                AddLog(L("⚠ DebugReset method missing", "⚠ DebugReset 메서드 없음"));
            }
        }

        // ==================================================================
        // 패널 ⑦ — BT State + Log (+ Utility 요약)
        // ==================================================================
        private void DrawBTStatePanel()
        {
            // 현재 State
            string stateName = "?";
            if (p_currentState != null && p_currentState.enumValueIndex >= 0 && p_currentState.enumValueIndex < p_currentState.enumDisplayNames.Length)
                stateName = p_currentState.enumDisplayNames[p_currentState.enumValueIndex];

            DrawNeonBadge($"currentState: {stateName}", C_NEON_AMBER, 36, T.BadgeState);

            GUILayout.Space(4);

            // Utility 요약 (한 줄 나열)
            float max = 0;
            string winner = "";
            CheckWinner(p_u_attack, "Atk", ref max, ref winner);
            CheckWinner(p_u_flee, "Flee", ref max, ref winner);
            CheckWinner(p_u_loot, "Loot", ref max, ref winner);
            CheckWinner(p_u_move, "Move", ref max, ref winner);
            CheckWinner(p_u_followcommand, "FollowCmd", ref max, ref winner);

            // Utility 5개 개별 라벨 (각각 tooltip)
            DrawUtilityInline("U_atk", GetProp(p_u_attack), T.UAttack, winner == "Atk");
            DrawUtilityInline("U_flee", GetProp(p_u_flee), T.UFlee, winner == "Flee");
            DrawUtilityInline("U_loot", GetProp(p_u_loot), T.ULoot, winner == "Loot");
            DrawUtilityInline("U_move", GetProp(p_u_move), T.UMove, winner == "Move");
            DrawUtilityInline("FollowCmd", GetProp(p_u_followcommand), T.UFollowCommand, winner == "FollowCmd");

            if (!string.IsNullOrEmpty(winner))
            {
                var winStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    normal = { textColor = C_NEON_AMBER }
                };
                EditorGUILayout.LabelField(
                    new GUIContent($"★ {L("Winner", "선택")}: {winner} ({max:F2})", T.Winner),
                    winStyle);
            }

            GUILayout.Space(6);

            // Divider
            var divRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(divRect, C_CARD_BORDER);

            GUILayout.Space(4);

            // 최근 로그
            DrawHint(L("▸ RECENT LOG:", "▸ 최근 로그:"), T.RecentLog);
            var logStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                wordWrap = true,
                normal = { textColor = C_NEON_GREEN }
            };
            string joined = recentLogs.Count > 0 ? string.Join("\n", recentLogs) : L("(empty)", "(로그 없음)");
            EditorGUILayout.LabelField(new GUIContent(joined, T.RecentLog), logStyle);
        }

        /// <summary>Utility 한 행 — 이름 + 값 + tooltip + winner 강조.</summary>
        private void DrawUtilityInline(string label, float value, string tooltip, bool isWinner)
        {
            EditorGUILayout.BeginHorizontal();

            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = isWinner ? C_NEON_AMBER : C_TEXT_BRIGHT }
            };
            GUILayout.Label(new GUIContent(label, tooltip), nameStyle, GUILayout.Width(55));

            // 막대 (Utility 값을 0-3 범위로 가정, 최대 3이면 가득)
            var barRect = GUILayoutUtility.GetRect(0, 12, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(barRect, C_SLIDER_BG);
                float t = Mathf.Clamp01(value / 3.0f);
                float w = barRect.width * t;
                if (w > 0)
                {
                    Color barColor = isWinner ? C_NEON_AMBER : C_NEON_CYAN;
                    var fill = new Color(barColor.r * 0.8f, barColor.g * 0.8f, barColor.b * 0.8f, 0.85f);
                    EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, w, barRect.height), fill);
                }
            }

            var valStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = isWinner ? C_NEON_AMBER : C_TEXT_BRIGHT }
            };
            GUILayout.Label(new GUIContent(value.ToString("F2"), tooltip), valStyle, GUILayout.Width(40));

            EditorGUILayout.EndHorizontal();
        }

        private float GetProp(SerializedProperty p) => p != null ? p.floatValue : 0f;

        private void CheckWinner(SerializedProperty p, string name, ref float max, ref string winner)
        {
            if (p == null) return;
            if (p.floatValue > max) { max = p.floatValue; winner = name; }
        }

        // ==================================================================
        // Drawing Helpers
        // ==================================================================
        private void DrawSlider(SerializedProperty p, string label, string tooltip = "", Color? labelColor = null)
        {
            if (p == null) return;

            EditorGUILayout.BeginHorizontal();

            var lblStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = labelColor ?? C_TEXT_BRIGHT },
                fontSize = 10
            };
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), lblStyle, GUILayout.Width(90));

            // 커스텀 색상 슬라이더
            var rect = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
            float v = Mathf.Clamp01(p.floatValue);
            DrawGradientBar(rect, v, C_NEON_CYAN, C_SLIDER_BG);

            // 드래그
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                p.floatValue = Mathf.Clamp01((Event.current.mousePosition.x - rect.x) / rect.width);
                GUI.changed = true;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDrag && rect.Contains(Event.current.mousePosition))
            {
                p.floatValue = Mathf.Clamp01((Event.current.mousePosition.x - rect.x) / rect.width);
                GUI.changed = true;
                Event.current.Use();
            }

            var valStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = C_NEON_CYAN }
            };
            GUILayout.Label(p.floatValue.ToString("F2"), valStyle, GUILayout.Width(40));

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>그라디언트 막대 — fg 색상에서 bg 색상으로</summary>
        private void DrawGradientBar(Rect rect, float fillRatio, Color fgColor, Color bgColor)
        {
            if (Event.current.type != EventType.Repaint) return;

            EditorGUI.DrawRect(rect, bgColor);

            float filled = rect.width * Mathf.Clamp01(fillRatio);
            if (filled <= 0) return;

            // Gradient: 밝게 → 중간 → 본 색
            int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments;
                float segX = rect.x + filled * t;
                float segW = filled / segments + 1;

                // 끝단 더 밝게
                Color c = Color.Lerp(fgColor, Color.white, t * 0.3f);
                c.a = 0.85f;
                EditorGUI.DrawRect(new Rect(segX, rect.y, segW, rect.height), c);
            }

            // 끝 하이라이트 (1px)
            EditorGUI.DrawRect(new Rect(rect.x + filled - 1, rect.y, 1, rect.height),
                new Color(1, 1, 1, 0.7f));
        }

        private void DrawNeonBadge(string text, Color color, float height, string tooltip = "")
        {
            var rect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                // Outer glow (진한 색 반투명)
                var glowColor = new Color(color.r, color.g, color.b, 0.15f);
                EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), glowColor);
                // Fill
                var fillColor = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.4f);
                EditorGUI.DrawRect(rect, fillColor);
                // Border
                DrawNeonBorder(rect, color, 2);
            }

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color }
            };
            GUI.Label(rect, new GUIContent(text, tooltip), style);
        }

        private void DrawNeonBorder(Rect rect, Color color, int thickness)
        {
            // 상
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            // 하
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            // 좌
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            // 우
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private bool NeonButton(string text, Color color, string tooltip = "")
        {
            var rect = GUILayoutUtility.GetRect(60, 22, GUILayout.ExpandWidth(true));

            bool clicked = false;
            var ev = Event.current;
            bool hover = rect.Contains(ev.mousePosition);

            if (ev.type == EventType.Repaint)
            {
                Color bgColor = hover ?
                    new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.8f) :
                    new Color(color.r * 0.15f, color.g * 0.15f, color.b * 0.15f, 0.6f);
                EditorGUI.DrawRect(rect, bgColor);
                DrawNeonBorder(rect, color, 1);
            }

            if (ev.type == EventType.MouseDown && hover)
            {
                clicked = true;
                ev.Use();
            }

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color }
            };
            GUI.Label(rect, new GUIContent(text, tooltip), style);

            return clicked;
        }

        private void DrawKeyValue(string key, string value, Color? valueColor = null, string tooltip = "")
        {
            EditorGUILayout.BeginHorizontal();
            var keyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = C_TEXT_DIM }
            };
            GUILayout.Label(new GUIContent(key, tooltip), keyStyle, GUILayout.Width(160));

            var valStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = valueColor ?? C_TEXT_BRIGHT }
            };
            GUILayout.Label(new GUIContent(value, tooltip), valStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHint(string text, string tooltip = "")
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = C_TEXT_DIM },
                fontSize = 9
            };
            EditorGUILayout.LabelField(new GUIContent(text, tooltip), style);
        }

        private void DrawWarning(string text)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = C_NEON_AMBER },
                fontSize = 10
            };
            EditorGUILayout.LabelField("⚠ " + text, style);
        }

        private void DrawCenteredMessage(string text)
        {
            GUILayout.FlexibleSpace();
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_NEON_MAGENTA }
            };
            EditorGUILayout.LabelField(text, style);
            GUILayout.FlexibleSpace();
        }

        // ==================================================================
        // Tier / Stage 색상
        // ==================================================================
        private string TierName(int idx)
        {
            switch (idx)
            {
                case 0: return L("Hostility", "적대");
                case 1: return L("Doubt", "의심");
                case 2: return L("Cooperation", "협력");
                case 3: return L("BlindTrust", "맹신");
                default: return "?";
            }
        }

        private string GetTierTooltip(int idx)
        {
            switch (idx)
            {
                case 0: return T.TierHostility;
                case 1: return T.TierDoubt;
                case 2: return T.TierCooperation;
                case 3: return T.TierBlindTrust;
                default: return T.BadgeTier;
            }
        }

        private string GetStageTooltip(int idx)
        {
            switch (idx)
            {
                case 0: return T.StageNone;
                case 1: return T.StageAcuteShock;
                case 2: return T.StageCrossroads;
                case 3: return T.StagePermScarring;
                default: return T.BadgeStage;
            }
        }

        private Color TierColor(int idx)
        {
            switch (idx)
            {
                case 0: return C_NEON_RED;
                case 1: return C_NEON_AMBER;
                case 2: return C_NEON_GREEN;
                case 3: return C_NEON_CYAN;
                default: return C_TEXT_DIM;
            }
        }

        private string StageName(int idx)
        {
            switch (idx)
            {
                case 0: return L("None", "없음");
                case 1: return L("AcuteShock", "급성 쇼크");
                case 2: return L("Crossroads", "분기점");
                case 3: return L("PermScarring", "영구 흉터");
                default: return "?";
            }
        }

        private Color StageColor(int idx)
        {
            switch (idx)
            {
                case 0: return C_TEXT_DIM;
                case 1: return C_NEON_AMBER;
                case 2: return C_NEON_MAGENTA;
                case 3: return C_NEON_RED;
                default: return C_TEXT_DIM;
            }
        }

        // ==================================================================
        // 선택 / 캐시
        // ==================================================================
        private void RefreshBrainCacheIfNeeded(bool force = false)
        {
            if (!force && Time.realtimeSinceStartup - lastBrainCacheTime < kBrainCacheInterval) return;
            lastBrainCacheTime = Time.realtimeSinceStartup;

            if (!Application.isPlaying)
            {
                cachedBrains = new HumanoidAIBrain[0];
                cachedBrainNames = new string[] { "(Not playing)" };
                return;
            }

            cachedBrains = FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            if (cachedBrains.Length == 0)
            {
                cachedBrainNames = new string[] { "(No NPC)" };
                return;
            }

            cachedBrainNames = new string[cachedBrains.Length];
            for (int i = 0; i < cachedBrains.Length; i++)
                cachedBrainNames[i] = cachedBrains[i].gameObject.name;

            if (selectedBrain != null)
            {
                selectedIndex = System.Array.IndexOf(cachedBrains, selectedBrain);
                if (selectedIndex < 0) ClearSelection();
            }
        }

        private void SelectBrain(HumanoidAIBrain brain)
        {
            selectedBrain = brain;
            brainSO = new SerializedObject(brain);

            var go = brain.gameObject;
            resolver = go.GetComponent<PersonalityTagResolver>();
            trustMatrix = go.GetComponent<TrustMatrix>();
            traumaSystem = go.GetComponent<TraumaSystem>();
            formula = go.GetComponent<UtilityMasterFormula>();

            p_personality = brainSO.FindProperty("personality");
            if (p_personality != null)
            {
                p_control = p_personality.FindPropertyRelative("control");
                p_stability = p_personality.FindPropertyRelative("stability");
                p_openness = p_personality.FindPropertyRelative("openness");
                p_agreeable = p_personality.FindPropertyRelative("agreeable");
                p_directness = p_personality.FindPropertyRelative("directness");
            }

            p_fear = brainSO.FindProperty("fear");
            p_hunger = brainSO.FindProperty("hunger");
            p_greed = brainSO.FindProperty("greed");
            p_fatigue = brainSO.FindProperty("fatigue");

            p_u_attack = brainSO.FindProperty("u_attack");
            p_u_flee = brainSO.FindProperty("u_flee");
            p_u_loot = brainSO.FindProperty("u_loot");
            p_u_move = brainSO.FindProperty("u_move");
            p_u_followcommand = brainSO.FindProperty("u_followcommand");

            p_currentState = brainSO.FindProperty("currentState");

            AddLog(L($"NPC selected: {brain.gameObject.name}", $"NPC 선택: {brain.gameObject.name}"));
        }

        private void ClearSelection()
        {
            selectedBrain = null;
            brainSO = null;
            resolver = null;
            trustMatrix = null;
            traumaSystem = null;
            formula = null;
            selectedIndex = -1;
        }

        // ==================================================================
        // Reflection Helpers
        // ==================================================================
        private float GetFearMultiplierSafe()
        {
            if (traumaSystem == null) return 1.0f;
            var method = typeof(TraumaSystem).GetMethod("GetFearMultiplier");
            if (method != null) return (float)method.Invoke(traumaSystem, null);
            return 1.0f;
        }

        // ==================================================================
        // 로그
        // ==================================================================
        private void AddLog(string msg)
        {
            recentLogs.Add($"[{System.DateTime.Now:HH:mm:ss}] {msg}");
            if (recentLogs.Count > kMaxLogLines)
                recentLogs.RemoveAt(0);
        }
    }
}