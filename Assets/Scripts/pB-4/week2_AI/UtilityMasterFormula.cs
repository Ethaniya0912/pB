// =============================================================================
// UtilityMasterFormula.cs  |  pB-4 Project — Week 2 Day 2 T2.3 통합본
//                          |  v3 NGO 2.0 대응 — 2026-04-23
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   HumanoidAI의 유틸리티 AI 핵심 수식 구현.
//   U(A) = f(Need)^k × Π(Modifier_i) × TrustFactor
//
// [통합 이력]
//   - Day 1 T1.4: SetConfig/externalConfig 추가
//   - Day 2 T2.3: Awake 하드코딩 5개 Add 제거. SO 강제.
//   - Day 2 T2.3 통합: Patch 파일 흡수. partial 제거.
//
// [v3 NGO 2.0 개정]
//   - [NGO-1] MonoBehaviour → NetworkBehaviour
//   - [NGO-2] Awake + OnNetworkSpawn 듀얼 초기화 (단독 Play + Network 호환)
//   - [NGO-3] ScoreAction/ScoreAllActions는 서버/단독 Play에서만 결정. 클라는 NetworkVariable 읽기
//   - [NGO-4] winnerIndex + UtilityScoresSnapshot를 NetworkVariable로 전파 (Dashboard용)
//   - [NGO-5] trustFactor는 그대로 public (Dashboard 드래그 호환, 클라 view-only)
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;                             // [NGO-1]
using UnityEngine;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    /// <summary>반응 곡선 유형.</summary>
    public enum ResponseCurveType
    {
        Power,
        Logit,
        Linear
    }

    /// <summary>개별 행동의 유틸리티 설정.</summary>
    [Serializable]
    public class ActionUtilityConfig
    {
        [Tooltip("이 설정이 적용되는 행동 이름 (Attack/Flee/Loot/Move/FollowCommand)")]
        public string actionId;

        [Tooltip("이 행동의 기반 욕구 키. 예: Attack=aggression, Flee=fear, Loot=greed")]
        public string primaryNeedKey;

        [Tooltip("반응 곡선 유형. Power=점진적, Logit=임계점 폭발, Linear=선형")]
        public ResponseCurveType curveType = ResponseCurveType.Power;

        [Tooltip("Power Curve 지수(k). 1.0=선형, 2.0=욕구 높아야 반응, 0.5=작은 욕구에도 민감")]
        [Range(0.1f, 5.0f)] public float curveExponent = 1.0f;

        [Tooltip("Logit Curve 기울기(c). 높을수록 임계점에서 급격히 반응")]
        [Range(1f, 20f)] public float logitSteepness = 10f;

        [Tooltip("이 행동의 기본 가중치")]
        [Range(0f, 2f)] public float baseWeight = 1.0f;

        [Tooltip("타겟이 없으면 이 행동의 점수를 0으로 강제")]
        public bool requiresTarget = false;
    }

    /// <summary>성격 태그에 의한 유틸리티 보정 규칙.</summary>
    [Serializable]
    public class PersonalityModifierRule
    {
        [Tooltip("이 규칙이 적용되는 성격 태그 이름")]
        public string tagName;

        [Tooltip("보정 대상 행동. 비어있으면 모든 행동에 적용")]
        public string targetActionId;

        [Tooltip("Multiply=곱셈, AddToExponent=지수에 가산, OverrideWeight=가중치 교체")]
        public ModifierType modifierType = ModifierType.Multiply;

        [Tooltip("보정 값. Multiply: 0.5=절반, 2.0=2배")]
        public float value = 1.0f;
    }

    public enum ModifierType { Multiply, AddToExponent, OverrideWeight }

    /// <summary>
    /// [NGO-4] 5개 행동 유틸리티 스냅샷. NetworkVariable로 전파하여 Dashboard 표시.
    /// </summary>
    [Serializable]
    public struct UtilityScoresSnapshot : INetworkSerializable, IEquatable<UtilityScoresSnapshot>
    {
        public float uAttack;
        public float uFlee;
        public float uLoot;
        public float uMove;
        public float uFollowCommand;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref uAttack);
            s.SerializeValue(ref uFlee);
            s.SerializeValue(ref uLoot);
            s.SerializeValue(ref uMove);
            s.SerializeValue(ref uFollowCommand);
        }

        public bool Equals(UtilityScoresSnapshot o) =>
            uAttack == o.uAttack && uFlee == o.uFlee && uLoot == o.uLoot &&
            uMove == o.uMove && uFollowCommand == o.uFollowCommand;
    }

    /// <summary>
    /// Master Formula 엔진. [NGO-1] NetworkBehaviour.
    /// HumanoidAIBrain과 같은 GameObject에 부착. 서버가 계산 → NetworkVariable로 클라 전파.
    /// </summary>
    public class UtilityMasterFormula : NetworkBehaviour              // [NGO-1]
    {
        // ==================================================================
        // Inspector 설정
        // ==================================================================
        [Header("━━━ Action Configs ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("각 행동의 유틸리티 설정 목록. externalConfig 또는 Bootstrapper가 주입.")]
        [SerializeField] private List<ActionUtilityConfig> actionConfigs = new List<ActionUtilityConfig>();

        [Header("━━━ Personality Modifier Rules ━━━━━━━━")]
        [Tooltip("성격 태그에 의한 유틸리티 보정 규칙 목록.")]
        [SerializeField] private List<PersonalityModifierRule> modifierRules = new List<PersonalityModifierRule>();

        [Header("━━━ Trust Factor ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("신뢰도 계수. 0.0~1.0. TrustMatrix가 갱신. 1.0=완전 신뢰, 0.0=적대.")]
        [Range(0f, 1f)] public float trustFactor = 1.0f;

        [Header("━━━ Week 2 T1.4 외부 설정 (선택) ━━━━")]
        [Tooltip("UtilityActionConfigSO .asset. 지정되면 자동 로드.")]
        [SerializeField] private UtilityActionConfigSO externalConfig;

        [Header("━━━ Debug ━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("계산 과정을 Console에 출력.")]
        public bool debugLog = false;

        // ==================================================================
        // [v3 NGO-4] 네트워크 동기화 상태
        // ==================================================================

        /// <summary>현재 winner 행동의 actionConfigs 인덱스. -1=없음. 서버가 갱신.</summary>
        private NetworkVariable<int> netWinnerIndex = new NetworkVariable<int>(
            value: -1,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>5개 행동의 최근 유틸리티 점수. Dashboard가 읽음.</summary>
        private NetworkVariable<UtilityScoresSnapshot> netScores = new NetworkVariable<UtilityScoresSnapshot>(
            value: default,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>클라이언트 접근용 winner 인덱스.</summary>
        public int CurrentWinnerIndex => netWinnerIndex.Value;

        /// <summary>클라이언트 접근용 유틸리티 스냅샷.</summary>
        public UtilityScoresSnapshot CurrentScores => netScores.Value;

        // ==================================================================
        // [v3 NGO-2] 이중 초기화
        // ==================================================================

        /// <summary>단독 Play 경로. NetworkManager 비활성 시 기존 Day 1~2 동작.</summary>
        private void Awake()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                InitializeConfigSource();
            }
        }

        /// <summary>네트워크 Play 경로.</summary>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            InitializeConfigSource();
        }

        private void InitializeConfigSource()
        {
            if (externalConfig != null)
            {
                LoadFromConfig(externalConfig);
            }
            else if (actionConfigs.Count == 0)
            {
                if (HasAuthority())
                    StartCoroutine(VerifyConfigLoadedAfterFrame());
            }
        }

        private IEnumerator VerifyConfigLoadedAfterFrame()
        {
            yield return null;
            if (actionConfigs.Count == 0)
            {
                Debug.LogError($"[UtilityMasterFormula] {name}: actions 미로드. " +
                               $"Inspector의 externalConfig 또는 " +
                               $"HumanoidBootstrapper.defaultActionConfig 할당 필요.");
            }
        }

        /// <summary>[v3 NGO 헬퍼] 결정 권한 체크.</summary>
        private bool HasAuthority()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return true;
            return IsServer;
        }

        // ==================================================================
        // 외부 설정 주입 API
        // ==================================================================

        /// <summary>외부 SO로부터 actions/modifiers 로드.</summary>
        public void SetConfig(UtilityActionConfigSO config)
        {
            if (config == null)
            {
                Debug.LogError($"[UtilityMasterFormula] {name}: SetConfig에 null 전달");
                return;
            }

            externalConfig = config;
            LoadFromConfig(config);
        }

        /// <summary>SO → 내부 필드 복사.</summary>
        private void LoadFromConfig(UtilityActionConfigSO config)
        {
            actionConfigs.Clear();
            actionConfigs.AddRange(config.actions);

            modifierRules.Clear();
            modifierRules.AddRange(config.modifiers);

            Debug.Log($"[UtilityMasterFormula] {name}: {config.actions.Count}개 action + " +
                      $"{config.modifiers.Count}개 modifier 로드됨 (from {config.name})");
        }

        /// <summary>현재 설정 요약 반환.</summary>
        public string GetConfigSummary()
        {
            return $"[Formula] actions={actionConfigs.Count}, modifiers={modifierRules.Count}" +
                   (externalConfig != null ? $", SO={externalConfig.name}" : ", hardcoded");
        }

        // ==================================================================
        // 핵심 API: 행동별 유틸리티 점수 산출
        // ==================================================================

        /// <summary>
        /// 특정 행동의 유틸리티 점수 산출.
        /// [NGO-3] 서버/단독 Play만 결정 가능. 클라는 NetworkVariable 읽기만.
        /// </summary>
        public float ScoreAction(string actionId, Dictionary<string, float> needs,
            List<string> activeTags, bool hasTarget)
        {
            ActionUtilityConfig config = null;
            foreach (var c in actionConfigs)
                if (c.actionId == actionId) { config = c; break; }

            if (config == null)
            {
                if (debugLog) Debug.LogWarning($"[UtilityFormula] 행동 '{actionId}' 설정 없음.");
                return 0f;
            }

            if (config.requiresTarget && !hasTarget) return 0f;

            // Step 1: 기반 욕구 값 추출
            float needValue = 0f;
            if (needs != null && needs.ContainsKey(config.primaryNeedKey))
                needValue = Mathf.Clamp01(needs[config.primaryNeedKey]);

            // Step 2: 반응 곡선
            float curveExponent = config.curveExponent;

            if (activeTags != null)
            {
                foreach (var rule in modifierRules)
                {
                    if (!activeTags.Contains(rule.tagName)) continue;
                    if (!string.IsNullOrEmpty(rule.targetActionId) && rule.targetActionId != actionId) continue;

                    if (rule.modifierType == ModifierType.AddToExponent)
                        curveExponent += rule.value;
                }
            }
            curveExponent = Mathf.Max(0.1f, curveExponent);

            float curvedNeed = ApplyResponseCurve(needValue, config.curveType, curveExponent, config.logitSteepness);

            // Step 3: Modifier 곱셈
            float modifierProduct = 1.0f;
            if (activeTags != null)
            {
                foreach (var rule in modifierRules)
                {
                    if (!activeTags.Contains(rule.tagName)) continue;
                    if (!string.IsNullOrEmpty(rule.targetActionId) && rule.targetActionId != actionId) continue;

                    if (rule.modifierType == ModifierType.Multiply)
                        modifierProduct *= rule.value;
                    else if (rule.modifierType == ModifierType.OverrideWeight)
                        modifierProduct = rule.value;
                }
            }

            // Step 4: 최종 공식
            float score = curvedNeed * modifierProduct * config.baseWeight * trustFactor;

            if (debugLog)
                Debug.Log($"[UtilityFormula] {actionId}: need={needValue:F2} → curve={curvedNeed:F2} " +
                          $"× mod={modifierProduct:F2} × weight={config.baseWeight:F2} × trust={trustFactor:F2} = {score:F3}");

            return score;
        }

        /// <summary>
        /// 모든 행동의 점수를 한 번에 산출.
        /// [NGO-3] 서버/단독 Play만 결정. 계산 후 NetworkVariable 갱신.
        /// </summary>
        public Dictionary<string, float> ScoreAllActions(Dictionary<string, float> needs,
            List<string> activeTags, bool hasTarget)
        {
            var scores = new Dictionary<string, float>();

            // [NGO-3] 권한 체크
            if (!HasAuthority())
            {
                // 클라는 마지막 동기화 값 반환
                var snap = netScores.Value;
                foreach (var config in actionConfigs)
                {
                    switch (config.actionId)
                    {
                        case "Attack":        scores[config.actionId] = snap.uAttack; break;
                        case "Flee":          scores[config.actionId] = snap.uFlee; break;
                        case "Loot":          scores[config.actionId] = snap.uLoot; break;
                        case "Move":          scores[config.actionId] = snap.uMove; break;
                        case "FollowCommand": scores[config.actionId] = snap.uFollowCommand; break;
                        default:              scores[config.actionId] = 0f; break;
                    }
                }
                return scores;
            }

            // 서버/단독 Play: 실제 계산
            foreach (var config in actionConfigs)
                scores[config.actionId] = ScoreAction(config.actionId, needs, activeTags, hasTarget);

            // [NGO-4] NetworkVariable 갱신 (단독 Play에서는 NetworkManager 비활성이므로 no-op)
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
            {
                int winnerIdx = -1;
                float winnerScore = float.MinValue;

                for (int i = 0; i < actionConfigs.Count; i++)
                {
                    var cfg = actionConfigs[i];
                    if (!scores.ContainsKey(cfg.actionId)) continue;
                    float s = scores[cfg.actionId];
                    if (s > winnerScore) { winnerScore = s; winnerIdx = i; }
                }

                netWinnerIndex.Value = winnerIdx;

                var snap = new UtilityScoresSnapshot();
                foreach (var cfg in actionConfigs)
                {
                    float s = scores.ContainsKey(cfg.actionId) ? scores[cfg.actionId] : 0f;
                    switch (cfg.actionId)
                    {
                        case "Attack":        snap.uAttack = s; break;
                        case "Flee":          snap.uFlee = s; break;
                        case "Loot":          snap.uLoot = s; break;
                        case "Move":          snap.uMove = s; break;
                        case "FollowCommand": snap.uFollowCommand = s; break;
                    }
                }
                netScores.Value = snap;
            }

            return scores;
        }

        // ==================================================================
        // 반응 곡선 함수
        // ==================================================================

        /// <summary>욕구 값(0~1)을 반응 곡선으로 변환.</summary>
        public static float ApplyResponseCurve(float x, ResponseCurveType type, float exponent, float logitSteepness)
        {
            x = Mathf.Clamp01(x);
            switch (type)
            {
                case ResponseCurveType.Power:
                    return Mathf.Pow(x, exponent);

                case ResponseCurveType.Logit:
                    float c = Mathf.Max(1f, logitSteepness);
                    return 1f / (1f + Mathf.Exp(-c * (x - 0.5f)));

                case ResponseCurveType.Linear:
                default:
                    return x;
            }
        }
    }
}
