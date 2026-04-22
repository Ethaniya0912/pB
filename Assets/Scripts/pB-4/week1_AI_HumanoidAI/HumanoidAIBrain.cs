// =============================================================================
// HumanoidAIBrain.cs  |  pB-4 Project — Week 1 → Week 2 Day 1 T1.5 (v2 개정)
// Layer  : L3 Domain (AI)
// Owner  : Person A
//
// 역할:
//   인간형 NPC(동료)의 의사결정 엔진.
//   PersonalityMatrix 5축 + Master Formula + 히스테리시스 상태 전환.
//
// [Week 2 Day 1 T1.5 개정 요약]
//   1) 단순 곱셈 UpdateDecision → Master Formula 파이프라인 8단계로 전환
//   2) InjectComponents 공개 메서드 추가 (Bootstrapper DI)
//   3) needsTagReEvaluation 명시 플래그 + MarkTagsDirty API
//   4) SelectNewState 히스테리시스 (stateSwitchHysteresis + minStateHoldTicks)
//   5) BuildNeedsDictionary로 6종 Need 계산 (Trust/Trauma 반영)
//   6) Week 1 로직을 FallbackScoresWeek1Logic으로 보존 (컴포넌트 미부착 시)
//   7) UpdateSituationVector — SitVectorEncoder로 BB 게시
//   8) archetype 필드 + ApplyArchetype 메서드 (T1.7 연결)
//
// [v2 코드리뷰 개정]
//   - [H3] aggression 공식 오류 수정: Clamp01(1 - fear*agreeable) → Clamp01((1-fear)*(1-agreeable))
//          의미상 "공격성 = 덜 무섭고 덜 우호적일수록 높다"로 교정.
//   - [H4] ApplyArchetype의 Reflection 기반 SetInitialTrust 호출 방식 명확화.
//          Day 1 시점에는 TrustMatrix에 SetInitialTrust 없을 수 있으므로 Reflection 유지,
//          단 주석으로 "Day 2 T2.4 이후 직접 호출로 대체" 명시.
//   - [H5] UpdateSituationVector의 try-catch 로그 스팸 방지 (hasLoggedSitError 플래그).
//   - [H6] decisionInterval Range(0.05, 2) → Range(0.1, 2) — 20Hz 성능 부담 방지.
//   - [H7] currentTarget을 [HideInInspector] 제거 → 디버그 관찰 가능.
//   - [H8] ExecuteBTNode의 TODO case를 명시적 "no-op. Day 4 T4.2에서 실구현" 주석 추가.
//   - [H10] GetScore를 protected static 으로 변경 (확장성 확보).
//
// PersonalityMatrix 5축:
//   control(자제력), stability(안정성), openness(개방성),
//   agreeable(우호성), directness(직설성) — 각 0.0~1.0
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.Data;
using TDA.PB4.Core;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI.Humanoid
{
    /// <summary>
    /// 성격 5축 구조체. 모든 인간형 NPC의 개성을 정의.
    /// Week 2 Master Formula에 직접 연동.
    /// </summary>
    [System.Serializable]
    public struct PersonalityMatrix
    {
        [Range(0f, 1f)] public float control;     // 자제력
        [Range(0f, 1f)] public float stability;   // 안정성
        [Range(0f, 1f)] public float openness;    // 개방성
        [Range(0f, 1f)] public float agreeable;   // 우호성
        [Range(0f, 1f)] public float directness;  // 직설성

        public float[] ToArray() => new float[] { control, stability, openness, agreeable, directness };

        public static PersonalityMatrix Random()
        {
            return new PersonalityMatrix
            {
                control = UnityEngine.Random.Range(0.1f, 0.9f),
                stability = UnityEngine.Random.Range(0.1f, 0.9f),
                openness = UnityEngine.Random.Range(0.1f, 0.9f),
                agreeable = UnityEngine.Random.Range(0.1f, 0.9f),
                directness = UnityEngine.Random.Range(0.1f, 0.9f),
            };
        }
    }

    public enum HumanoidBTState { Idle, Move, Loot, Attack, Flee, FollowCommand }

    public class HumanoidAIBrain : BaseAIBrain
    {
        // ==================================================================
        // 성격 데이터
        // ==================================================================
        [Header("Personality")]
        [SerializeField] private PersonalityMatrix personality;
        public PersonalityMatrix Personality => personality;

        // 성격 앵커 (Trauma/Pivot 시 비교용 원점)
        private PersonalityMatrix anchorPersonality;

        // ==================================================================
        // [T1.7] Archetype 슬롯
        // ==================================================================
        [Header("━━━ Archetype (T1.7 연결) ━━━━━━━━━")]

        [Tooltip("HumanoidArchetypeSO. 지정 시 Start에서 Personality/Trust/Karma 자동 적용.")]
        [SerializeField] private HumanoidArchetypeSO archetype;

        // ==================================================================
        // BT 상태
        // ==================================================================
        [Header("BT State")]
        [SerializeField] private HumanoidBTState currentState = HumanoidBTState.Idle;
        public HumanoidBTState CurrentState => currentState;

        // ==================================================================
        // [T1.5] Week 2 주입 컴포넌트 (Bootstrapper가 주입)
        // ==================================================================
        [Header("━━━ Week 2 주입 컴포넌트 (읽기 전용) ━━")]

        [SerializeField] private UtilityMasterFormula utilityFormula;
        [SerializeField] private PersonalityTagResolver tagResolver;
        [SerializeField] private SituationVectorEncoder sitEncoder;
        [SerializeField] private TrustMatrix trustMatrix;
        [SerializeField] private TraumaSystem traumaSystem;

        // ==================================================================
        // 유틸리티 점수 (Inspector 관찰용)
        // ==================================================================
        [Header("━━━ Utility Scores (실시간 디버그) ━━")]
        [SerializeField, Range(0f, 1f)] private float u_attack;
        [SerializeField, Range(0f, 1f)] private float u_flee;
        [SerializeField, Range(0f, 1f)] private float u_loot;
        [SerializeField, Range(0f, 1f)] private float u_move;
        [SerializeField, Range(0f, 1f)] private float u_followcommand;

        // [H7] [HideInInspector] 제거. u_* 필드들처럼 런타임 디버그 관찰 가능.
        [Header("━━━ Runtime Refs (디버그 관찰용) ━━━━━")]
        [Tooltip("현재 타겟. BT 조건 + Action이 참조.")]
        public Transform currentTarget;
        [Tooltip("이동 목적지. FleeAction/MoveAction이 사용.")]
        public Vector3 moveDestination;

        // ==================================================================
        // [T1.5] 상태 전환 파라미터 (히스테리시스)
        // ==================================================================
        [Header("━━━ 상태 전환 파라미터 ━━━━━━━━━━━━")]

        [Tooltip("Idle 상태 진입 임계. 모든 점수가 이 값보다 낮으면 Idle.")]
        [Range(0f, 0.5f)] public float idleThreshold = 0.1f;

        [Tooltip("상태 전환 히스테리시스 마진. 새 점수가 현재+이 값 이상이어야 전환.")]
        [Range(0f, 0.3f)] public float stateSwitchHysteresis = 0.08f;

        [Tooltip("동일 상태 최소 유지 틱 수. 너무 빠른 전환 방지.")]
        [Range(1, 10)] public int minStateHoldTicks = 2;

        private int currentStateHoldTicks;

        // ==================================================================
        // [T1.5] 태그 재평가 플래그
        // ==================================================================
        [Tooltip("태그 재평가 필요 플래그. Pivot 이벤트 또는 외부 트리거로 설정.")]
        [SerializeField] private bool needsTagReEvaluation = true;

        // ==================================================================
        // 결정 주기
        // ==================================================================
        [Header("━━━ 결정 주기 ━━━━━━━━━━━━━━━━━━━━━")]

        // [H6] 하한 0.05 → 0.1 로 변경. 20Hz 이상 호출 방지.
        [Tooltip("결정 주기 (초). Week 2 기본 0.3초 = 약 3 Hz.")]
        [Range(0.1f, 2f)] public float decisionInterval = 0.3f;

        private float decisionTimer = 0f;

        /// <summary>PB4DecisionAdapter가 외부에서 틱 제어 시 true. 이중 호출 방지.</summary>
        [HideInInspector] public bool externallyTicked = false;

        // [H5] UpdateSituationVector의 로그 스팸 방지용 플래그.
        // 첫 예외 발생 시 한 번만 로그 출력 후 이후 스킵.
        private bool hasLoggedSitError = false;

        // ==================================================================
        // Lifecycle
        // ==================================================================
        protected override void Awake()
        {
            base.Awake();
            anchorPersonality = personality;
        }

        private void Start()
        {
            // Archetype이 지정되어 있으면 적용.
            // Awake가 아닌 Start: Bootstrapper의 컴포넌트 주입 이후 실행 보장.
            ApplyArchetype();
        }

        private void Update()
        {
            // [Bug 2 수정] 외부 틱 제어 시 자체 호출 스킵
            if (externallyTicked) return;

            decisionTimer += Time.deltaTime;
            if (decisionTimer >= decisionInterval)
            {
                decisionTimer = 0f;
                UpdateDecision();
            }
        }

        // ==================================================================
        // [T1.4 / T1.5] Bootstrapper DI API
        // ==================================================================

        /// <summary>Bootstrapper가 주입하는 Week 2 컴포넌트 5종.</summary>
        public void InjectComponents(
            UtilityMasterFormula formula,
            PersonalityTagResolver resolver,
            SituationVectorEncoder encoder,
            TrustMatrix trust,
            TraumaSystem trauma)
        {
            this.utilityFormula = formula;
            this.tagResolver = resolver;
            this.sitEncoder = encoder;
            this.trustMatrix = trust;
            this.traumaSystem = trauma;

            needsTagReEvaluation = true;
            hasLoggedSitError = false;  // 재주입 시 로그 플래그 리셋

            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: Week 2 주입 완료. " +
                          $"Formula={formula != null}, Resolver={resolver != null}, " +
                          $"Encoder={encoder != null}, Trust={trust != null}, Trauma={trauma != null}");
        }

        /// <summary>태그 재평가 요청. Dilemma 해결/Pivot 발생 시 호출.</summary>
        public void MarkTagsDirty(string reason = "")
        {
            needsTagReEvaluation = true;
            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: Tags dirty ({reason})");
        }

        // ==================================================================
        // [T1.5] UpdateDecision — 8단계 Master Formula 파이프라인
        // ==================================================================

        /// <summary>Week 2 결정 파이프라인. decisionInterval마다 1회 실행.</summary>
        /// <remarks>
        /// 8 단계:
        ///   ① 지형 fear 업데이트 → ② 태그 재평가 (필요 시) → ③ Needs 딕셔너리 →
        ///   ④ ScoreAllActions → ⑤ 점수 보관 → ⑥ SelectNewState (히스테리시스) →
        ///   ⑦ SitVector 갱신 → ⑧ 다음 틱 대기.
        /// </remarks>
        public override void UpdateDecision()
        {
            // ① 지형 fear 업데이트
            UpdateFearFromTerrain();

            // ② 태그 재평가 (플래그 기반, 매 틱 X)
            if (needsTagReEvaluation && tagResolver != null)
            {
                tagResolver.ResolveTagsFromPersonality(personality);
                needsTagReEvaluation = false;

                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: Tag 재평가 → " +
                              $"[{string.Join(",", tagResolver.ActiveTags)}]");
            }

            // ③ Needs 딕셔너리 구성
            var needs = BuildNeedsDictionary();

            // ④ Utility 점수 산출
            Dictionary<string, float> scores;
            if (utilityFormula != null && tagResolver != null)
            {
                // UtilityMasterFormula.ScoreAllActions는 List<string>을 요구
                var activeTagsList = new List<string>(tagResolver.ActiveTags);
                scores = utilityFormula.ScoreAllActions(
                    needs,
                    activeTagsList,
                    currentTarget != null);
            }
            else
            {
                // Fallback: Week 1 로직 (컴포넌트 미부착 시)
                scores = FallbackScoresWeek1Logic(needs);
                if (verboseLogging)
                    Debug.LogWarning($"[HumanoidAIBrain] {name}: Week 1 fallback (Formula/Resolver 미부착)");
            }

            // ⑤ 점수 보관 (Inspector 디버그용)
            u_attack = GetScore(scores, "Attack");
            u_flee = GetScore(scores, "Flee");
            u_loot = GetScore(scores, "Loot");
            u_move = GetScore(scores, "Move");
            u_followcommand = GetScore(scores, "FollowCommand");

            // ⑥ 상태 선택 (히스테리시스)
            SelectNewState(scores);

            // ⑦ SitVector 갱신 (BB 게시)
            if (sitEncoder != null)
            {
                UpdateSituationVector(needs);
            }
        }

        // ==================================================================
        // [T1.5] Needs 딕셔너리 구성 (6종 욕구)
        // ==================================================================

        private Dictionary<string, float> BuildNeedsDictionary()
        {
            // Trust 반영
            float obedience = 0.5f;
            if (trustMatrix != null)
            {
                if (trustMatrix is ITrustProvider trustProv)
                    obedience = trustProv.GetNormalizedTrust();
                // else: Day 2 T2.4 이전에는 ITrustProvider 미구현. 0.5 fallback.
            }

            // Trauma 반영: fear에 multiplier 적용
            float adjustedFear = fear;
            if (traumaSystem != null && traumaSystem is ITraumaProvider traumaProv)
            {
                adjustedFear = Mathf.Clamp01(fear * traumaProv.GetFearMultiplier());
            }

            // [H3] aggression 공식 수정
            //   이전(버그): Clamp01(1 - adjustedFear * agreeable)
            //     → agreeable=0이면 fear 무관하게 aggression=1 (비우호적이면 무조건 공격적?)
            //     → agreeable=1 & fear=0이면 aggression=1 (우호적인데도 공격성 최대?)
            //     둘 다 논리 오류.
            //   수정: Clamp01((1 - adjustedFear) * (1 - agreeable))
            //     의미: "덜 무섭고 + 덜 우호적"일수록 공격성 증가 (두 축 모두 낮아야 함)
            //     fear=0 & agreeable=0 → 1.0 (가장 공격적) ✓
            //     fear=1 or agreeable=1 → 낮음 ✓
            float aggression = Mathf.Clamp01(
                (1f - adjustedFear) * (1f - personality.agreeable));

            return new Dictionary<string, float>()
            {
                ["fear"] = adjustedFear,
                ["hunger"] = hunger,
                ["greed"] = greed,
                ["fatigue"] = fatigue,
                ["aggression"] = aggression,
                ["obedience"] = obedience,
            };
        }

        /// <summary>Dictionary 안전 조회.</summary>
        /// <remarks>[H10] protected static 으로 변경 (자식 클래스 및 관련 유틸이 재사용).</remarks>
        protected static float GetScore(Dictionary<string, float> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out float v) ? v : 0f;
        }

        // ==================================================================
        // [T1.5] Week 1 fallback 로직 (Formula 미부착 시)
        // ==================================================================

        private Dictionary<string, float> FallbackScoresWeek1Logic(Dictionary<string, float> needs)
        {
            float fearMod = 1.0f - personality.stability;
            float aggressionMod = 1.0f - personality.agreeable;
            float greedMod = 1.0f - personality.control;
            float currentFear = needs["fear"];

            // [H12] Loot은 타겟 없을 때만 활성. Attack 중에는 보물 수집 안 함 (의도).
            return new Dictionary<string, float>
            {
                ["Attack"] = currentTarget != null
                    ? (1.0f - currentFear * fearMod) * aggressionMod * 0.8f
                    : 0f,
                ["Flee"] = currentFear * fearMod * 0.9f,
                ["Loot"] = needs["greed"] * greedMod * (currentTarget != null ? 0.0f : 0.7f),
                ["Move"] = (1.0f - needs["fatigue"]) * personality.openness * 0.3f,
                ["FollowCommand"] = needs["obedience"] * 0.7f,
            };
        }

        // ==================================================================
        // [T1.5] SelectNewState — 히스테리시스 적용
        // ==================================================================

        /// <summary>Winner-takes-all + 히스테리시스 적용 상태 전환.</summary>
        /// <remarks>
        /// 전환 조건:
        ///   1) 현재 상태 유지 틱이 minStateHoldTicks 이상
        ///   2) 새 승자 점수 >= 현재 상태 점수 + stateSwitchHysteresis
        ///   3) 모든 점수 < idleThreshold 인 경우는 Idle로 직접 전환
        /// </remarks>
        private void SelectNewState(Dictionary<string, float> scores)
        {
            // 승자 찾기 — 음수 점수 방어 포함
            string winnerAction = "Idle";
            float winnerScore = 0f;

            foreach (var kv in scores)
            {
                // [H1] 음수 점수 방어: 음수면 경쟁에 포함하지 않음
                if (kv.Value > winnerScore)
                {
                    winnerScore = kv.Value;
                    winnerAction = kv.Key;
                }
            }

            // 모든 점수 임계 이하 → Idle
            if (winnerScore < idleThreshold)
            {
                TransitionTo(HumanoidBTState.Idle);
                return;
            }

            var newState = MapActionIdToState(winnerAction);

            // 같은 상태면 유지 + 틱 카운터 증가
            if (newState == currentState)
            {
                currentStateHoldTicks++;
                return;
            }

            // 전환 조건 ① 최소 유지 틱 체크
            if (currentStateHoldTicks < minStateHoldTicks)
            {
                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: 전환 차단(minHold): " +
                              $"{currentState}→{newState} (holdTicks={currentStateHoldTicks})");
                return;
            }

            // 전환 조건 ② 히스테리시스 마진 체크
            float currentScore = GetScoreForCurrentState(scores);
            if (winnerScore < currentScore + stateSwitchHysteresis)
            {
                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: 전환 차단(히스테리시스): " +
                              $"{currentState}={currentScore:F2} vs {newState}={winnerScore:F2}");
                return;
            }

            TransitionTo(newState);
        }

        private HumanoidBTState MapActionIdToState(string actionId)
        {
            switch (actionId)
            {
                case "Attack": return HumanoidBTState.Attack;
                case "Flee": return HumanoidBTState.Flee;
                case "Loot": return HumanoidBTState.Loot;
                case "Move": return HumanoidBTState.Move;
                case "FollowCommand": return HumanoidBTState.FollowCommand;
                case "Idle": return HumanoidBTState.Idle;  // [H2] 명시적 Idle 매핑 추가
                default: return HumanoidBTState.Idle;
            }
        }

        private float GetScoreForCurrentState(Dictionary<string, float> scores)
        {
            switch (currentState)
            {
                case HumanoidBTState.Attack: return GetScore(scores, "Attack");
                case HumanoidBTState.Flee: return GetScore(scores, "Flee");
                case HumanoidBTState.Loot: return GetScore(scores, "Loot");
                case HumanoidBTState.Move: return GetScore(scores, "Move");
                case HumanoidBTState.FollowCommand: return GetScore(scores, "FollowCommand");
                default: return 0f;
            }
        }

        private void TransitionTo(HumanoidBTState newState)
        {
            if (newState == currentState) return;

            var oldState = currentState;
            currentState = newState;
            currentStateHoldTicks = 0;

            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: 상태 전환 {oldState} → {newState}");

            // 기존 BT 노드 실행 연결
            ExecuteBTNode(currentState.ToString());
        }

        // ==================================================================
        // [T1.5] SitVector 갱신
        // ==================================================================

        /// <summary>상황 벡터를 인코딩하여 Blackboard에 게시.</summary>
        /// <remarks>
        /// SitVectorEncoder가 Personality + Needs + Tags를 768-dim 벡터로 변환.
        /// Week 5 RAG 시스템이 사건 유사도 검색에 사용.
        ///
        /// 실제 SitVectorEncoder.EncodeAndPublish 시그니처는 Week 1 구현에 따라 다름.
        /// 컴파일 에러 발생 시 실제 API에 맞춰 이 메서드 내부 수정.
        ///
        /// [H5] hasLoggedSitError 플래그로 매 틱 로그 스팸 방지. 첫 예외만 로그.
        /// InjectComponents 재호출 시 플래그 리셋되어 재진단 가능.
        /// </remarks>
        private void UpdateSituationVector(Dictionary<string, float> needs)
        {
            if (sitEncoder == null || blackboard == null) return;

            try
            {
                // [수정] 원본 SituationVectorEncoder.EncodeAndPublish는 인자 없이 호출.
                // Encoder가 자체 brain 참조로 personality/needs/tags 직접 조회.
                sitEncoder.EncodeAndPublish();
            }
            catch (System.Exception e)
            {
                if (!hasLoggedSitError)
                {
                    hasLoggedSitError = true;
                    Debug.LogError($"[HumanoidAIBrain] {name}: UpdateSituationVector 예외: {e.Message} " +
                                   $"(이후 예외는 스팸 방지를 위해 로그 스킵.)");
                }
            }
        }

        // ==================================================================
        // [T1.7] Archetype 적용
        // ==================================================================

        /// <summary>Archetype 기반 Personality/Trust/Karma 초기화.</summary>
        /// <remarks>
        /// [H4] TrustMatrix.SetInitialTrust가 Day 2 T2.4에서 공식 메서드로 추가 예정.
        /// Day 1 시점에는 존재 여부 불확실 → Reflection으로 안전 호출.
        /// Day 2 이후에는 이 Reflection 블록을 직접 호출로 교체 권장.
        /// </remarks>
        private void ApplyArchetype()
        {
            if (archetype == null) return;

            if (!archetype.IsValid(out string reason))
            {
                Debug.LogWarning($"[HumanoidAIBrain] {name}: Archetype 유효성 실패 - {reason}");
                return;
            }

            // Personality 적용
            if (archetype.personality != null)
            {
                personality = archetype.personality.ToMatrix();
                anchorPersonality = personality;
                needsTagReEvaluation = true;

                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: Archetype '{archetype.displayName}' 적용 " +
                              $"({archetype.personality})");
            }

            // Trust 초기값 적용.
            // [H4] Day 1 시점에는 TrustMatrix에 SetInitialTrust가 없을 수 있어 Reflection 사용.
            //      Day 2 T2.4에서 정식 메서드 추가 후 아래로 교체 권장:
            //        if (trustMatrix != null)
            //        {
            //            (trustMatrix as ITrustProvider)?.AddDelta(
            //                archetype.startingTrust - 50f, "Archetype");
            //        }
            if (trustMatrix != null)
            {
                try
                {
                    var method = trustMatrix.GetType().GetMethod("SetInitialTrust");
                    if (method != null)
                    {
                        method.Invoke(trustMatrix, new object[] { archetype.startingTrust });
                    }
                    // else: Day 2 T2.4에서 API 통일.
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[HumanoidAIBrain] {name}: SetInitialTrust Reflection 실패: {e.Message}");
                }
            }

            // TODO Day 3 T3.1: KarmaDirector.Register(this, archetype.startingKarma)
            // TODO Day 3 T3.2: alignmentController.SetInitial(archetype.startingAlignment)
        }

        // ==================================================================
        // BT 노드 실행
        // ==================================================================

        /// <summary>결정된 목표에 따른 BT 노드 실행.</summary>
        /// <remarks>
        /// [H8] Day 1 시점에는 모든 case가 no-op. Day 4 T4.2에서 실구현 Action 노드와 연결.
        /// 현재는 상태 전환 시 로그 용도만 있으며 실제 GameObject 동작 없음.
        /// </remarks>
        public override void ExecuteBTNode(string goalId)
        {
            switch (goalId)
            {
                case "Attack":        /* no-op. Day 4 T4.2 AttackAction에서 연결 */ break;
                case "Flee":          /* no-op. Day 4 T4.2 FleeAction에서 연결 */ break;
                case "Loot":          /* no-op. Day 4 T4.2 LootAction에서 연결 */ break;
                case "Move":          /* no-op. Day 4 T4.2 MoveAction에서 연결 */ break;
                case "FollowCommand": /* no-op. Day 4 T4.2 FollowCommandAction에서 연결 */ break;
                case "Idle":          /* no-op. Day 4 T4.2 IdleAction에서 연결 */ break;
                default:              /* no-op. unknown goalId */ break;
            }
        }

        // ==================================================================
        // 외부 API (Week 2~7에서 확장)
        // ==================================================================

        /// <summary>성격 피봇팅. Dilemma/Pivot 발생 시 호출.</summary>
        public void PivotPersonality(float[] delta)
        {
            if (delta == null || delta.Length != 5) return;

            personality.control = Mathf.Clamp01(personality.control + delta[0]);
            personality.stability = Mathf.Clamp01(personality.stability + delta[1]);
            personality.openness = Mathf.Clamp01(personality.openness + delta[2]);
            personality.agreeable = Mathf.Clamp01(personality.agreeable + delta[3]);
            personality.directness = Mathf.Clamp01(personality.directness + delta[4]);

            // Pivot 후 태그 재평가 강제
            MarkTagsDirty("PivotPersonality");
        }

        public PersonalityMatrix GetAnchor() => anchorPersonality;

        /// <summary>디버그: 현재 런타임 상태 덤프.</summary>
        [ContextMenu("Dump Current State")]
        public void DumpCurrentState()
        {
            var tagsStr = tagResolver != null
                ? string.Join(",", tagResolver.ActiveTags)
                : "null";

            Debug.Log($"[HumanoidAIBrain] {name} State Dump:\n" +
                      $"  Personality: {(archetype != null ? archetype.displayName : "manual")} " +
                      $"(C={personality.control:F2}/S={personality.stability:F2}/" +
                      $"O={personality.openness:F2}/A={personality.agreeable:F2}/" +
                      $"D={personality.directness:F2})\n" +
                      $"  Needs: fear={fear:F2}, hunger={hunger:F2}, greed={greed:F2}, fatigue={fatigue:F2}\n" +
                      $"  Scores: Attack={u_attack:F2}, Flee={u_flee:F2}, Loot={u_loot:F2}, " +
                      $"Move={u_move:F2}, FollowCmd={u_followcommand:F2}\n" +
                      $"  State: {currentState} (heldTicks={currentStateHoldTicks})\n" +
                      $"  Target: {(currentTarget != null ? currentTarget.name : "null")}\n" +
                      $"  Tags: [{tagsStr}]\n" +
                      $"  IsStubFree: {IsStubFree()}");
        }
    }
}
