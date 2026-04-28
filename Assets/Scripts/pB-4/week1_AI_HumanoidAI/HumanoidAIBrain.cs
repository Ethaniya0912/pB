// =============================================================================
// HumanoidAIBrain.cs  |  pB-4 Project — Week 1 → Week 2 Day 1 T1.5 → v3 NGO 2.0
// Layer  : L3 Domain (AI)
// Owner  : Person A
//
// 역할:
//   인간형 NPC의 의사결정 엔진. PersonalityMatrix 5축 + Master Formula + 히스테리시스.
//
// [Week 2 Day 1 T1.5 개정]
//   1) Master Formula 파이프라인 8단계
//   2) InjectComponents DI
//   3) needsTagReEvaluation 플래그
//   4) 히스테리시스 + minStateHoldTicks
//   5) BuildNeedsDictionary (6종 욕구)
//   6) FallbackScoresWeek1Logic
//   7) UpdateSituationVector
//   8) Archetype 슬롯
//
// [v2 코드리뷰 개정]
//   - [H3] aggression 공식 수정
//   - [H4~H10] 세부 개선
//
// [v3 NGO 2.0 개정 — 2026-04-23]
//   - [NGO-1] BaseAIBrain이 NetworkBehaviour → HumanoidAIBrain도 자동 상속
//   - [NGO-2] Awake + OnNetworkSpawn 양쪽에서 anchorPersonality 설정 (단독 Play + Network)
//   - [NGO-3] Update() BT Tick은 서버/단독 Play만 실행
//   - [NGO-4] PivotPersonality → 서버 권한 (ServerRpc)
//   - [NGO-5] PersonalityMatrix에 INetworkSerializable 구현 (NetworkVariable 전송)
//   - [NGO-6] netPersonality, netCurrentState NetworkVariable로 전 클라 동기화
// =============================================================================
using System.Collections.Generic;
using Unity.Netcode;                              // [NGO-1]
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.Data;
using TDA.PB4.Core;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI.Humanoid
{
    /// <summary>
    /// 성격 5축 구조체. [NGO-5] INetworkSerializable 구현 추가.
    /// </summary>
    [System.Serializable]
    public struct PersonalityMatrix : INetworkSerializable, System.IEquatable<PersonalityMatrix>
    {
        [Range(0f, 1f)] public float control;
        [Range(0f, 1f)] public float stability;
        [Range(0f, 1f)] public float openness;
        [Range(0f, 1f)] public float agreeable;
        [Range(0f, 1f)] public float directness;

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

        // [NGO-5] NetworkVariable 전송용
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref control);
            s.SerializeValue(ref stability);
            s.SerializeValue(ref openness);
            s.SerializeValue(ref agreeable);
            s.SerializeValue(ref directness);
        }

        public bool Equals(PersonalityMatrix o) =>
            control == o.control && stability == o.stability && openness == o.openness &&
            agreeable == o.agreeable && directness == o.directness;
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

        private PersonalityMatrix anchorPersonality;

        // [NGO-6] 네트워크 동기화용 (서버 쓰기, 전 클라 읽기)
        private NetworkVariable<PersonalityMatrix> netPersonality = new NetworkVariable<PersonalityMatrix>(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

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

        // [NGO-6] 네트워크 동기화용 (int로 캐스팅)
        private NetworkVariable<int> netCurrentState = new NetworkVariable<int>(
            value: (int)HumanoidBTState.Idle,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // ==================================================================
        // [T1.5] Week 2 주입 컴포넌트
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

        [Header("━━━ Runtime Refs (디버그 관찰용) ━━━━━")]
        [Tooltip("현재 타겟. BT 조건 + Action이 참조.")]
        public Transform currentTarget;
        [Tooltip("이동 목적지.")]
        public Vector3 moveDestination;

        // ==================================================================
        // [T1.5] 상태 전환 파라미터
        // ==================================================================
        [Header("━━━ 상태 전환 파라미터 ━━━━━━━━━━━━")]
        [Range(0f, 0.5f)] public float idleThreshold = 0.1f;
        [Range(0f, 0.3f)] public float stateSwitchHysteresis = 0.08f;
        [Range(1, 10)]    public int minStateHoldTicks = 2;
        private int currentStateHoldTicks;

        [Tooltip("태그 재평가 필요 플래그.")]
        [SerializeField] private bool needsTagReEvaluation = true;

        // ==================================================================
        // 결정 주기
        // ==================================================================
        [Header("━━━ 결정 주기 ━━━━━━━━━━━━━━━━━━━━━")]
        [Range(0.1f, 2f)] public float decisionInterval = 0.3f;
        private float decisionTimer = 0f;

        [HideInInspector] public bool externallyTicked = false;
        private bool hasLoggedSitError = false;

        // ==================================================================
        // Lifecycle
        // ==================================================================
        protected override void Awake()
        {
            base.Awake();   // [NGO-2] BaseAIBrain.Awake가 단독 Play 시 InitializeBlackboard 수행
            anchorPersonality = personality;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();   // [NGO-2] BaseAIBrain.OnNetworkSpawn이 InitializeBlackboard 수행

            // 최초 스폰 시 personality 동기화 (서버)
            if (IsServer)
            {
                netPersonality.Value = personality;
                netCurrentState.Value = (int)currentState;
            }

            // 클라는 NetworkVariable 변화를 구독
            netPersonality.OnValueChanged += OnNetPersonalityChanged;
            netCurrentState.OnValueChanged += OnNetCurrentStateChanged;

            if (!IsServer)
            {
                // 클라는 초기값을 NetworkVariable에서 가져옴
                personality = netPersonality.Value;
                anchorPersonality = personality;
                currentState = (HumanoidBTState)netCurrentState.Value;
            }
        }

        public override void OnNetworkDespawn()
        {
            netPersonality.OnValueChanged -= OnNetPersonalityChanged;
            netCurrentState.OnValueChanged -= OnNetCurrentStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetPersonalityChanged(PersonalityMatrix prev, PersonalityMatrix next)
        {
            if (IsServer) return;   // 서버는 이미 변경됨
            personality = next;
            needsTagReEvaluation = true;
        }

        private void OnNetCurrentStateChanged(int prev, int next)
        {
            if (IsServer) return;
            currentState = (HumanoidBTState)next;
            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain/Client] {name}: 상태 수신 {(HumanoidBTState)prev} → {currentState}");
        }

        private void Start()
        {
            // [NGO-3] Archetype 적용은 서버/단독 Play만
            if (!HasAuthority()) return;

            ApplyArchetype();
        }

        private void Update()
        {
            if (externallyTicked) return;

            // [NGO-3] BT Tick은 서버/단독 Play만 실행
            if (!HasAuthority()) return;

            decisionTimer += Time.deltaTime;
            if (decisionTimer >= decisionInterval)
            {
                decisionTimer = 0f;
                UpdateDecision();
            }
        }

        // ==================================================================
        // Bootstrapper DI
        // ==================================================================

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
            hasLoggedSitError = false;

            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: Week 2 주입 완료. " +
                          $"Formula={formula != null}, Resolver={resolver != null}, " +
                          $"Encoder={encoder != null}, Trust={trust != null}, Trauma={trauma != null}");
        }

        /// <summary>태그 재평가 요청.</summary>
        public void MarkTagsDirty(string reason = "")
        {
            needsTagReEvaluation = true;
            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: Tags dirty ({reason})");
        }

        // ==================================================================
        // UpdateDecision — 8단계
        // ==================================================================

        public override void UpdateDecision()
        {
            // ① 지형 fear 업데이트
            UpdateFearFromTerrain();

            // ② 태그 재평가
            if (needsTagReEvaluation && tagResolver != null)
            {
                tagResolver.ResolveTagsFromPersonality(personality);
                needsTagReEvaluation = false;

                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: Tag 재평가 → " +
                              $"[{string.Join(",", tagResolver.ActiveTags)}]");
            }

            // ③ Needs 딕셔너리
            var needs = BuildNeedsDictionary();

            // ④ Utility 점수
            Dictionary<string, float> scores;
            if (utilityFormula != null && tagResolver != null)
            {
                var activeTagsList = new List<string>(tagResolver.ActiveTags);
                scores = utilityFormula.ScoreAllActions(
                    needs,
                    activeTagsList,
                    currentTarget != null);
            }
            else
            {
                scores = FallbackScoresWeek1Logic(needs);
                if (verboseLogging)
                    Debug.LogWarning($"[HumanoidAIBrain] {name}: Week 1 fallback");
            }

            // ⑤ 점수 보관
            u_attack = GetScore(scores, "Attack");
            u_flee = GetScore(scores, "Flee");
            u_loot = GetScore(scores, "Loot");
            u_move = GetScore(scores, "Move");
            u_followcommand = GetScore(scores, "FollowCommand");

            // ⑥ 상태 선택
            SelectNewState(scores);

            // ⑦ SitVector 갱신
            if (sitEncoder != null)
            {
                UpdateSituationVector(needs);
            }
        }

        // ==================================================================
        // Needs 딕셔너리
        // ==================================================================

        private Dictionary<string, float> BuildNeedsDictionary()
        {
            float obedience = 0.5f;
            if (trustMatrix != null && trustMatrix is ITrustProvider trustProv)
                obedience = trustProv.GetNormalizedTrust();

            float adjustedFear = fear;
            if (traumaSystem != null && traumaSystem is ITraumaProvider traumaProv)
                adjustedFear = Mathf.Clamp01(fear * traumaProv.GetFearMultiplier());

            // [H3] aggression = Clamp01((1 - fear) * (1 - agreeable))
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

        protected static float GetScore(Dictionary<string, float> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out float v) ? v : 0f;
        }

        // ==================================================================
        // Week 1 fallback
        // ==================================================================

        private Dictionary<string, float> FallbackScoresWeek1Logic(Dictionary<string, float> needs)
        {
            float fearMod = 1.0f - personality.stability;
            float aggressionMod = 1.0f - personality.agreeable;
            float greedMod = 1.0f - personality.control;
            float currentFear = needs["fear"];

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
        // SelectNewState — 히스테리시스
        // ==================================================================

        private void SelectNewState(Dictionary<string, float> scores)
        {
            // [DEBT-21 v1] holdTicks 시간 기반 누적으로 변경 — 데드락 해소.
            // 이전 결함: holdTicks++가 ❷ (newState==currentState) 분기 안에만 있어,
            //          winner가 다른 액션으로 바뀐 순간 영원히 0 유지 → ❸ 영원히 차단.
            //          v5.7 ForceStateForTesting이 currentState 직접 할당으로 가렸음.
            //          v5.14 자연 검증 박제로 입증: holdTicks=0 고정 → ❸ 우회로만 전이 성공.
            // 의미 변화: holdTicks = "현재 상태에 머문 누적 호출 수" (매 호출 ++)
            //          TransitionTo가 다른 상태로 전이 시 0 리셋 (line 479 그대로).
            currentStateHoldTicks++;

            string winnerAction = "Idle";
            float winnerScore = 0f;

            foreach (var kv in scores)
            {
                if (kv.Value > winnerScore)
                {
                    winnerScore = kv.Value;
                    winnerAction = kv.Key;
                }
            }

            if (winnerScore < idleThreshold)
            {
                TransitionTo(HumanoidBTState.Idle);
                return;
            }

            var newState = MapActionIdToState(winnerAction);

            if (newState == currentState)
            {
                // [DEBT-21] currentStateHoldTicks++ 제거 — 위로 옮김 (매 호출 무조건 증가)
                return;
            }

            if (currentStateHoldTicks < minStateHoldTicks)
            {
                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: 전환 차단(minHold): " +
                              $"{currentState}→{newState} (holdTicks={currentStateHoldTicks})");
                return;
            }

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
                case "Idle": return HumanoidBTState.Idle;
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

            // [NGO-6] 서버가 NetworkVariable 갱신 → 전 클라 자동 전파
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                netCurrentState.Value = (int)newState;

            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: 상태 전환 {oldState} → {newState}");

            ExecuteBTNode(currentState.ToString());
        }

        // ==================================================================
        // [v5.7 Day 5] Test-only: 모든 hysteresis/holdTicks 우회하고 강제 전환
        // utility 점수와 무관하게 즉시 currentState 변경 + ExecuteBTNode 트리거.
        // 시각 검증 도구(HumanoidVisualAutoVerifier)의 fallback 옵션.
        //
        // [v5.9] DirectFleeFallback (transform 직접 조작) 제거 — 가짜 검증 박제.
        //   진짜 자연 도주는 NavMeshAgent.enabled=true + NavMesh 베이크로 가능.
        //   (CaveManager.cs:420-437 패턴, Stage Setup이 자동 처리)
        // ==================================================================
        public void ForceStateForTesting(HumanoidBTState newState, string reason = "test")
        {
            var oldState = currentState;
            currentState = newState;
            currentStateHoldTicks = 0;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                netCurrentState.Value = (int)newState;

            Debug.Log($"[HumanoidAIBrain] {name}: ★ ForceStateForTesting {oldState} → {newState} (reason: {reason})");

            ExecuteBTNode(currentState.ToString());
        }

        // ==================================================================
        // SitVector
        // ==================================================================

        private void UpdateSituationVector(Dictionary<string, float> needs)
        {
            if (sitEncoder == null || blackboard == null) return;

            try
            {
                sitEncoder.EncodeAndPublish();
            }
            catch (System.Exception e)
            {
                if (!hasLoggedSitError)
                {
                    hasLoggedSitError = true;
                    Debug.LogError($"[HumanoidAIBrain] {name}: UpdateSituationVector 예외: {e.Message}");
                }
            }
        }

        // ==================================================================
        // Archetype
        // ==================================================================

        private void ApplyArchetype()
        {
            if (archetype == null) return;

            if (!archetype.IsValid(out string reason))
            {
                Debug.LogWarning($"[HumanoidAIBrain] {name}: Archetype 유효성 실패 - {reason}");
                return;
            }

            if (archetype.personality != null)
            {
                personality = archetype.personality.ToMatrix();
                anchorPersonality = personality;
                needsTagReEvaluation = true;

                // [NGO-6] 서버가 NetworkVariable 갱신
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                    netPersonality.Value = personality;

                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: Archetype '{archetype.displayName}' 적용");
            }

            if (trustMatrix != null)
            {
                try
                {
                    var method = trustMatrix.GetType().GetMethod("SetInitialTrust");
                    if (method != null)
                        method.Invoke(trustMatrix, new object[] { archetype.startingTrust });
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[HumanoidAIBrain] {name}: SetInitialTrust Reflection 실패: {e.Message}");
                }
            }
        }

        // ==================================================================
        // BT 노드 실행
        // ==================================================================

        public override void ExecuteBTNode(string goalId)
        {
            switch (goalId)
            {
                case "Attack":        break;
                case "Flee":          break;
                case "Loot":          break;
                case "Move":          break;
                case "FollowCommand": break;
                case "Idle":          break;
                default:              break;
            }
        }

        // ==================================================================
        // 외부 API
        // ==================================================================

        /// <summary>성격 피봇팅. [NGO-4] 서버 권한. 클라 호출 시 ServerRpc 위임.</summary>
        public void PivotPersonality(float[] delta)
        {
            if (delta == null || delta.Length != 5) return;

            // [NGO-4] 클라에서 호출 시 ServerRpc
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
            {
                PivotPersonalityServerRpc(delta[0], delta[1], delta[2], delta[3], delta[4]);
                return;
            }

            ApplyPivotInternal(delta);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void PivotPersonalityServerRpc(float d0, float d1, float d2, float d3, float d4)
        {
            ApplyPivotInternal(new float[] { d0, d1, d2, d3, d4 });
        }

        private void ApplyPivotInternal(float[] delta)
        {
            personality.control = Mathf.Clamp01(personality.control + delta[0]);
            personality.stability = Mathf.Clamp01(personality.stability + delta[1]);
            personality.openness = Mathf.Clamp01(personality.openness + delta[2]);
            personality.agreeable = Mathf.Clamp01(personality.agreeable + delta[3]);
            personality.directness = Mathf.Clamp01(personality.directness + delta[4]);

            // [NGO-6] NetworkVariable 갱신
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                netPersonality.Value = personality;

            MarkTagsDirty("PivotPersonality");
        }

        public PersonalityMatrix GetAnchor() => anchorPersonality;

        /// <summary>[Day 3] Anchor를 재설정 (DilemmaPivot에서 영구 변화).</summary>
        /// <remarks>
        /// 서버 권한. 클라 직접 호출 시 무시 + 경고 로그. 
        /// 정상 경로: DilemmaPivotResolver.ApplyChoice (이미 ServerRpc로 진입)
        /// </remarks>
        public void SetAnchor(PersonalityMatrix newAnchor)
        {
            if (!HasAuthority())
            {
                Debug.LogWarning($"[HumanoidAIBrain] {name}: SetAnchor 클라 직접 호출 무시. " +
                                 $"DilemmaPivotResolver.ApplyChoice 경유 필요.");
                return;
            }

            anchorPersonality = newAnchor;
            personality = newAnchor;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                netPersonality.Value = personality;

            MarkTagsDirty("SetAnchor");
        }

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
