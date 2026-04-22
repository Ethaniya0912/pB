// =============================================================================
// UtilityMasterFormula.cs  |  pB-4 Project — Week 2 (Day 2 T2.3 통합본)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   HumanoidAI의 유틸리티 AI 핵심 수식을 구현한다.
//   Week 1의 단순 곱셈을 Master Formula로 교체:
//
//     U(A) = f(Need)^k  ×  Π(Modifier_i)  ×  TrustFactor
//
//   f(Need)  = 반응 곡선 (Power / Logit / Linear 중 선택)
//   k        = 성격 태그에 의해 조절되는 지수
//   Modifier = 성격/지형/상태에 의한 보정 계수
//   Trust    = 신뢰도 (Week 4에서 구현, 현재 1.0 고정)
//
// 사용법:
//   HumanoidAIBrain.UpdateDecision()에서 이 클래스의 ScoreAction()을 호출.
//   각 행동(Attack/Flee/Loot/Move/FollowCommand)에 대해 점수를 산출.
//
// [통합 이력]
//   - Day 1 T1.4: SetConfig/externalConfig 추가 (당시 partial class + 별도 Patch 파일)
//   - Day 2 T2.3: Awake 하드코딩 5개 Add 제거. SO 강제, fallback 없음.
//   - Day 2 T2.3 통합: UtilityMasterFormula_Week2Patch.cs를 이 파일에 흡수. partial 제거.
//     * 이유: T2.3에서 이미 원본을 크게 수정하므로 Patch 분리 명분 소멸.
//     * 결과: 솔루션 탐색기에 파일 1개. partial 키워드 불필요.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 반응 곡선 유형. 욕구(Need) 값을 비선형으로 변환하여
    /// NPC의 성격에 따라 '참다가 폭발'하거나 '서서히 반응'하는 행동을 모델링한다.
    /// </summary>
    public enum ResponseCurveType
    {
        /// <summary>
        /// f(x) = x^k. k>1이면 욕구가 커야 반응, k<1이면 작은 욕구에도 민감.
        /// 예: Glutton(k_hunger=2.5) → 배고픔이 0.8 이상이어야 강하게 반응.
        /// </summary>
        Power,

        /// <summary>
        /// f(x) = 1/(1+e^(-c*(x-0.5))). S자 곡선.
        /// 임계점(0.5) 근처에서 갑자기 반응이 폭발.
        /// 예: 겁쟁이 NPC가 공포 0.5까지 참다가 갑자기 도주.
        /// </summary>
        Logit,

        /// <summary>f(x) = x. 선형. 기본 반응.</summary>
        Linear
    }

    /// <summary>
    /// 개별 행동의 유틸리티 설정. 어떤 욕구를 사용하고, 어떤 곡선으로 변환하는지 정의.
    /// Inspector에서 행동별로 세밀하게 조절 가능.
    /// </summary>
    [Serializable]
    public class ActionUtilityConfig
    {
        [Tooltip("이 설정이 적용되는 행동 이름 (Attack/Flee/Loot/Move/FollowCommand)")]
        public string actionId;

        [Tooltip("이 행동의 기반 욕구. 예: Attack=aggression, Flee=fear, Loot=greed")]
        public string primaryNeedKey;

        [Tooltip("반응 곡선 유형. Power=점진적, Logit=임계점 폭발, Linear=선형")]
        public ResponseCurveType curveType = ResponseCurveType.Power;

        [Tooltip("Power Curve의 지수(k). 1.0=선형, 2.0=욕구 높아야 반응, 0.5=작은 욕구에도 민감")]
        [Range(0.1f, 5.0f)]
        public float curveExponent = 1.0f;

        [Tooltip("Logit Curve의 기울기(c). 높을수록 임계점에서 급격히 반응")]
        [Range(1f, 20f)]
        public float logitSteepness = 10f;

        [Tooltip("이 행동의 기본 가중치. 다른 행동과의 상대적 중요도")]
        [Range(0f, 2f)]
        public float baseWeight = 1.0f;

        [Tooltip("타겟이 없으면 이 행동의 점수를 0으로 강제할지 여부")]
        public bool requiresTarget = false;
    }

    /// <summary>
    /// 성격 태그에 의한 유틸리티 보정 규칙.
    /// 예: 'Brave' 태그가 있으면 fear 관련 Modifier에 0.5 곱셈 (공포 절반).
    /// </summary>
    [Serializable]
    public class PersonalityModifierRule
    {
        [Tooltip("이 규칙이 적용되는 성격 태그 이름 (PersonalityTagResolver가 부여한 태그)")]
        public string tagName;

        [Tooltip("보정 대상 행동. 비어있으면 모든 행동에 적용")]
        public string targetActionId;

        [Tooltip("보정 유형: Multiply=곱셈, AddToExponent=지수에 가산, OverrideWeight=가중치 교체")]
        public ModifierType modifierType = ModifierType.Multiply;

        [Tooltip("보정 값. Multiply: 0.5=절반, 2.0=2배. AddToExponent: +1.0=더 둔감, -0.5=더 민감")]
        public float value = 1.0f;
    }

    public enum ModifierType { Multiply, AddToExponent, OverrideWeight }

    /// <summary>
    /// Master Formula 엔진. HumanoidAIBrain이 참조하여 행동별 유틸리티 점수를 산출.
    /// MonoBehaviour로 HumanoidAIBrain과 같은 GameObject에 부착.
    /// </summary>
    public class UtilityMasterFormula : MonoBehaviour
    {
        // ==================================================================
        // Inspector 설정
        // ==================================================================
        [Header("━━━ Action Configs ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("각 행동(Attack/Flee/Loot/Move/FollowCommand)의 유틸리티 설정 목록. " +
                 "T2.3 적용 후: externalConfig 또는 Bootstrapper.defaultActionConfig가 " +
                 "SetConfig로 주입. 빈 상태로 Play하면 에러 출력.")]
        [SerializeField] private List<ActionUtilityConfig> actionConfigs = new List<ActionUtilityConfig>();

        [Header("━━━ Personality Modifier Rules ━━━━━━━━")]
        [Tooltip("성격 태그에 의한 유틸리티 보정 규칙 목록. " +
                 "예: 'Brave' 태그가 있으면 Flee 행동의 Modifier를 0.5로 곱하여 도주 확률 절반. " +
                 "'Glutton' 태그가 있으면 Loot 행동의 지수를 2.5로 설정하여 탐욕 극대화.")]
        [SerializeField] private List<PersonalityModifierRule> modifierRules = new List<PersonalityModifierRule>();

        [Header("━━━ Trust Factor (Week 4에서 활성화) ━━━")]
        [Tooltip("신뢰도 계수. 0.0~1.0. Week 4의 TrustMatrix가 이 값을 갱신. " +
                 "1.0=완전 신뢰(명령 복종 최대), 0.0=적대(독자 행동). " +
                 "현재 Week 2에서는 1.0 고정.")]
        [Range(0f, 1f)]
        public float trustFactor = 1.0f;

        [Header("━━━ Week 2 T1.4 외부 설정 (선택) ━━━━")]
        [Tooltip("UtilityActionConfigSO .asset. 지정되면 Awake 시 자동 로드. " +
                 "비어있으면 Bootstrapper가 SetConfig로 주입.")]
        [SerializeField] private UtilityActionConfigSO externalConfig;

        [Header("━━━ Debug ━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("계산 과정을 Console에 출력할지 여부. 성능에 영향을 주므로 테스트 시에만 켠다.")]
        public bool debugLog = false;

        // ==================================================================
        // 초기화 (T2.3 적용: 하드코딩 5개 Add 블록 완전 제거)
        // ==================================================================
        private void Awake()
        {
            // [Week 2 T2.3 개정] 5개 행동 하드코딩 제거.
            //
            // 우선순위:
            //   1순위: externalConfig (Inspector 할당)
            //   2순위: Bootstrapper가 나중에 SetConfig 호출 (Awake 이후)
            //   3순위: 둘 다 없으면 actionConfigs/modifierRules 빈 상태 → 에러

            if (externalConfig != null)
            {
                LoadFromConfig(externalConfig);
            }
            else if (actionConfigs.Count == 0)
            {
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

        // ==================================================================
        // 외부 설정 주입 API (Bootstrapper 및 수동 호출용)
        // ==================================================================

        /// <summary>외부 SO로부터 actions/modifiers 로드. Bootstrapper가 호출.</summary>
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

        /// <summary>SO → 내부 필드 복사. Awake와 SetConfig 양쪽에서 호출됨.</summary>
        private void LoadFromConfig(UtilityActionConfigSO config)
        {
            actionConfigs.Clear();
            actionConfigs.AddRange(config.actions);

            modifierRules.Clear();
            modifierRules.AddRange(config.modifiers);

            Debug.Log($"[UtilityMasterFormula] {name}: {config.actions.Count}개 action + " +
                      $"{config.modifiers.Count}개 modifier 로드됨 (from {config.name})");
        }

        /// <summary>현재 설정 요약 반환. Dashboard 등 디버그 용도.</summary>
        public string GetConfigSummary()
        {
            return $"[Formula] actions={actionConfigs.Count}, modifiers={modifierRules.Count}" +
                   (externalConfig != null ? $", SO={externalConfig.name}" : ", hardcoded");
        }

        // ==================================================================
        // 핵심 API: 행동별 유틸리티 점수 산출
        // ==================================================================

        /// <summary>
        /// 특정 행동의 유틸리티 점수를 산출한다.
        /// U(A) = f(Need)^k × Π(Modifier_i) × TrustFactor
        /// </summary>
        /// <param name="actionId">행동 이름 (Attack/Flee/Loot/Move/FollowCommand)</param>
        /// <param name="needs">욕구 딕셔너리. 키: aggression/fear/greed/hunger/obedience 등</param>
        /// <param name="activeTags">현재 활성화된 성격 태그 목록</param>
        /// <param name="hasTarget">공격 타겟이 존재하는지 여부</param>
        /// <returns>0.0~무한대 범위의 유틸리티 점수. Winner-takes-all로 최고 점수 행동 선택.</returns>
        public float ScoreAction(string actionId, Dictionary<string, float> needs,
            List<string> activeTags, bool hasTarget)
        {
            // 해당 행동의 설정 찾기
            ActionUtilityConfig config = null;
            foreach (var c in actionConfigs)
                if (c.actionId == actionId) { config = c; break; }

            if (config == null)
            {
                if (debugLog) Debug.LogWarning($"[UtilityFormula] 행동 '{actionId}'의 설정을 찾을 수 없습니다.");
                return 0f;
            }

            // 타겟 필요 행동인데 타겟이 없으면 0점
            if (config.requiresTarget && !hasTarget) return 0f;

            // Step 1: 기반 욕구 값 추출
            float needValue = 0f;
            if (needs != null && needs.ContainsKey(config.primaryNeedKey))
                needValue = Mathf.Clamp01(needs[config.primaryNeedKey]);

            // Step 2: 반응 곡선 적용 f(Need)
            float curveExponent = config.curveExponent;

            // 성격 태그에 의한 지수 보정
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

            // Step 3: Modifier 곱셈 Π(Modifier_i)
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
                        modifierProduct = rule.value; // 최후 Override가 이김
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
        /// 모든 행동의 점수를 한 번에 산출하여 딕셔너리로 반환.
        /// </summary>
        public Dictionary<string, float> ScoreAllActions(Dictionary<string, float> needs,
            List<string> activeTags, bool hasTarget)
        {
            var scores = new Dictionary<string, float>();
            foreach (var config in actionConfigs)
                scores[config.actionId] = ScoreAction(config.actionId, needs, activeTags, hasTarget);
            return scores;
        }

        // ==================================================================
        // 반응 곡선 함수
        // ==================================================================

        /// <summary>
        /// 욕구 값(0~1)을 반응 곡선으로 변환.
        /// Power: f(x) = x^k — 점진적 반응
        /// Logit: f(x) = 1/(1+e^(-c*(x-0.5))) — 임계점 폭발
        /// Linear: f(x) = x — 선형
        /// </summary>
        public static float ApplyResponseCurve(float x, ResponseCurveType type, float exponent, float logitSteepness)
        {
            x = Mathf.Clamp01(x);
            switch (type)
            {
                case ResponseCurveType.Power:
                    return Mathf.Pow(x, exponent);

                case ResponseCurveType.Logit:
                    // Sigmoid: 1/(1+e^(-c*(x-0.5)))
                    float c = Mathf.Max(1f, logitSteepness);
                    return 1f / (1f + Mathf.Exp(-c * (x - 0.5f)));

                case ResponseCurveType.Linear:
                default:
                    return x;
            }
        }
    }
}
