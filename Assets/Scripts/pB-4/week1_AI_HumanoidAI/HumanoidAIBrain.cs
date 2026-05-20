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
//   - [NGO-1~6] NetworkVariable 동기화
//
// [★ v3.1 Personality 진단 강화 — 2026-05-04]
//   - [DIAG-1] DiagState enum + 상태 분류 (Healthy / EmptyTags / ZeroPersonality / TagResolverMissing)
//   - [DIAG-2] RunPersonalityDiagnostic — 상태 변화 시에만 색상 로그 (console 폭주 방지)
//   - [DIAG-3] ContextMenu "★ Diagnose Personality State (Force)" 추가
//   - [DIAG-4] IsPersonalityAllZero / IsPersonalityNearZero 헬퍼
//   - [DIAG-5] autoDiagnostic Inspector 토글 + UpdateDecision 자동 호출
//   - [DIAG-6] 색상 코드: 빨강(심각) / 노랑(경고) / 녹색(정상) / 시안(정보)
//
// [★ v3.1.1 — 2026-05-04 핫픽스]
//   - SetPersonality 메서드 추가 (Phase 5 v5 호환)
//     HumanoidBootstrapper.randomizePersonality 가 이미 v5 적용된 상태라 미스매치 발생.
//     personality + anchorPersonality 갱신 + needsTagReEvaluation = true + NetVar 동기화.
//
// [★ v3.1.2 — 2026-05-04 추적 로깅 강화]
//   - InjectComponents 진입 시 oldResolverID + newResolverID + new resolver tags 출력
//   - UpdateDecision ② 태그 재평가 시 Resolve CALL/DONE 전후 tags 변동 추적
//   - 색상 컨벤션: 자주 (Inject), 녹색 (Resolve 정상), 빨강 (의심 = 0 tags)
// =============================================================================
using System.Collections.Generic;
using Unity.Netcode;
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

    public partial class HumanoidAIBrain : BaseAIBrain
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
        [Range(1, 10)] public int minStateHoldTicks = 2;
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
        // ★ v3.1 진단 (DIAG-1, DIAG-5)
        // ==================================================================
        [Header("━━━ ★ v3.1 진단 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("ON → Personality 또는 ActiveTags 비정상 시 색상 로그 자동 출력.\n" +
                 "상태 변화 시에만 1 회 로그 (console 폭주 방지).")]
        [SerializeField] private bool autoDiagnostic = true;

        /// <summary>★ v3.1 진단 상태 분류.</summary>
        private enum DiagState
        {
            Unknown,
            Healthy,                 // 모든 것 정상
            EmptyTags,               // ActiveTags 가 빈 배열 ★ 가장 흔한 이슈
            ZeroPersonality,         // Personality 5축 모두 0 (NetworkVariable 동기화 race)
            TagResolverMissing       // tagResolver == null
        }

        private DiagState _lastDiagState = DiagState.Unknown;

        // ==================================================================
        // Lifecycle
        // ==================================================================
        protected override void Awake()
        {
            base.Awake();
            anchorPersonality = personality;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                netPersonality.Value = personality;
                netCurrentState.Value = (int)currentState;
            }

            netPersonality.OnValueChanged += OnNetPersonalityChanged;
            netCurrentState.OnValueChanged += OnNetCurrentStateChanged;

            if (!IsServer)
            {
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
            if (IsServer) return;
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
            if (!HasAuthority()) return;
            ApplyArchetype();
        }

        private void Update()
        {
            if (externallyTicked) return;
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
            // ★ v3.1.2 — Inject 진입 추적 (resolver ID + tags 상태)
            int oldResolverId = this.tagResolver != null ? this.tagResolver.GetInstanceID() : -1;
            int newResolverId = resolver != null ? resolver.GetInstanceID() : -1;
            int newResolverTags = resolver?.ActiveTags?.Count ?? -1;
            Debug.Log($"<color=#888888>[N:{GetInstanceID()}]</color> " +
                      $"<color=#CCAAFF><b>[Brain/Inject ENTER]</b></color> " +
                      $"<color=#FFCC44>{name}</color> | " +
                      $"oldResolverID={oldResolverId} → newResolverID={newResolverId} | " +
                      $"new resolver tags={newResolverTags}");

            this.utilityFormula = formula;
            this.tagResolver = resolver;
            this.sitEncoder = encoder;
            this.trustMatrix = trust;
            this.traumaSystem = trauma;

            needsTagReEvaluation = true;
            hasLoggedSitError = false;
            _lastDiagState = DiagState.Unknown;   // ★ v3.1 — 진단 상태 리셋

            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: Week 2 주입 완료. " +
                          $"Formula={formula != null}, Resolver={resolver != null}, " +
                          $"Encoder={encoder != null}, Trust={trust != null}, Trauma={trauma != null}");
        }

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
                // ★ v3.1.2 — Resolve 호출 직전 추적
                int beforeTags = tagResolver.ActiveTags?.Count ?? -1;
                Debug.Log($"<color=#888888>[N:{GetInstanceID()}|R:{tagResolver.GetInstanceID()}]</color> " +
                          $"<color=#88FF88><b>[Brain/Resolve CALL]</b></color> " +
                          $"<color=#FFCC44>{name}</color> | beforeTags={beforeTags}");

                tagResolver.ResolveTagsFromPersonality(personality);
                needsTagReEvaluation = false;

                // ★ v3.1.2 — Resolve 호출 직후 추적
                int afterTags = tagResolver.ActiveTags?.Count ?? -1;
                string afterColor = afterTags <= 0 ? "#FF8866" : "#88FF88";
                string afterStr = (tagResolver.ActiveTags != null && tagResolver.ActiveTags.Count > 0)
                    ? string.Join(",", tagResolver.ActiveTags)
                    : "[ ]";
                Debug.Log($"<color=#888888>[N:{GetInstanceID()}|R:{tagResolver.GetInstanceID()}]</color> " +
                          $"<color={afterColor}><b>[Brain/Resolve DONE]</b></color> " +
                          $"<color=#FFCC44>{name}</color> | tags={afterTags}={afterStr}");

                if (verboseLogging)
                    Debug.Log($"[HumanoidAIBrain] {name}: Tag 재평가 → " +
                              $"[{string.Join(",", tagResolver.ActiveTags)}]");
            }

            // ★ v3.1 — 자동 진단 (상태 변화 시만 1 회 로그)
            if (autoDiagnostic) RunPersonalityDiagnostic();

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
        // ★ v3.1 진단 — Personality / ActiveTags 자동 감지 (DIAG-2 ~ DIAG-6)
        // ==================================================================

        /// <summary>★ v3.1 — 5축 모두 0 (epsilon 미만) 인지 검사.</summary>
        public bool IsPersonalityAllZero()
        {
            const float E = 0.001f;
            return Mathf.Abs(personality.control) < E
                && Mathf.Abs(personality.stability) < E
                && Mathf.Abs(personality.openness) < E
                && Mathf.Abs(personality.agreeable) < E
                && Mathf.Abs(personality.directness) < E;
        }

        /// <summary>★ v3.1 — 현재 상태 분류.</summary>
        private DiagState ClassifyDiagState()
        {
            if (tagResolver == null) return DiagState.TagResolverMissing;
            if (IsPersonalityAllZero()) return DiagState.ZeroPersonality;
            if (tagResolver.ActiveTags == null || tagResolver.ActiveTags.Count == 0)
                return DiagState.EmptyTags;
            return DiagState.Healthy;
        }

        /// <summary>★ v3.1 — 상태 변화 시에만 1 회 색상 로그 출력 (console 폭주 방지).</summary>
        private void RunPersonalityDiagnostic()
        {
            DiagState current = ClassifyDiagState();
            if (current == _lastDiagState) return;   // 변화 없으면 스킵

            DiagState prev = _lastDiagState;
            _lastDiagState = current;

            string colorTag, statusTag, severity;
            switch (current)
            {
                case DiagState.Healthy:
                    colorTag = "#44FF66"; statusTag = "✓ HEALTHY"; severity = "Log"; break;
                case DiagState.EmptyTags:
                    colorTag = "#FFAA33"; statusTag = "⚠ EMPTY TAGS"; severity = "Warning"; break;
                case DiagState.ZeroPersonality:
                    colorTag = "#FF4444"; statusTag = "✗ ZERO PERSONALITY"; severity = "Error"; break;
                case DiagState.TagResolverMissing:
                    colorTag = "#FF4444"; statusTag = "✗ NO RESOLVER"; severity = "Error"; break;
                default:
                    colorTag = "#888888"; statusTag = "? UNKNOWN"; severity = "Log"; break;
            }

            string transition = (prev == DiagState.Unknown)
                ? $"<color=#888888>(initial)</color>"
                : $"<color=#888888>({prev}→{current})</color>";

            string body = BuildDiagnosticBody();
            string msg = $"<color={colorTag}><b>[HumanoidAIBrain {statusTag}]</b></color> {name} {transition}\n{body}";

            switch (severity)
            {
                case "Error": Debug.LogError(msg); break;
                case "Warning": Debug.LogWarning(msg); break;
                default: Debug.Log(msg); break;
            }
        }

        /// <summary>★ v3.1 — 진단 본문 생성 (색상 포함).</summary>
        private string BuildDiagnosticBody()
        {
            // Personality 색상 — 0 이면 빨강, 정상이면 시안
            const float E = 0.001f;
            string ColorVal(float v) =>
                Mathf.Abs(v) < E
                    ? $"<color=#FF4444>{v:F3}</color>"
                    : $"<color=#88CCFF>{v:F3}</color>";

            string personalityLine =
                $"C={ColorVal(personality.control)} " +
                $"S={ColorVal(personality.stability)} " +
                $"O={ColorVal(personality.openness)} " +
                $"A={ColorVal(personality.agreeable)} " +
                $"D={ColorVal(personality.directness)}";

            // ActiveTags 색상
            string tagsLine;
            if (tagResolver == null)
            {
                tagsLine = "<color=#FF4444>tagResolver = NULL</color>";
            }
            else if (tagResolver.ActiveTags == null || tagResolver.ActiveTags.Count == 0)
            {
                tagsLine = "<color=#FF4444>[ ] (★ 빈 배열 — Resolver 매핑 또는 임계치 문제)</color>";
            }
            else
            {
                tagsLine = $"<color=#44FF66>[{string.Join(", ", tagResolver.ActiveTags)}]</color>";
            }

            // Utility Score 색상 — 0 이면 빨강 톤
            string ColorScore(float s) =>
                s < 0.001f
                    ? $"<color=#FF6677>{s:F2}</color>"
                    : $"<color=#88FFAA>{s:F2}</color>";

            string scoresLine =
                $"Atk={ColorScore(u_attack)} " +
                $"Flee={ColorScore(u_flee)} " +
                $"Loot={ColorScore(u_loot)} " +
                $"Mov={ColorScore(u_move)} " +
                $"FCmd={ColorScore(u_followcommand)}";

            return
                $"  <b>personality:</b>  {personalityLine}\n" +
                $"  <b>ActiveTags:</b>   {tagsLine}\n" +
                $"  <b>fear:</b> <color=#FFCC44>{fear:F2}</color>  " +
                  $"<b>scores:</b> {scoresLine}\n" +
                $"  <b>state:</b> <color=#CCAAFF>{currentState}</color> (held={currentStateHoldTicks})";
        }

        /// <summary>
        /// ★ v3.1 — 강제 진단 + Tag 재평가. ContextMenu 우클릭으로 호출.
        /// 상태 변화 무관 항상 풀 정보 출력.
        /// </summary>
        [ContextMenu("★ Diagnose Personality State (Force)")]
        public void DiagnosePersonalityStateMenu()
        {
            DiagState s = ClassifyDiagState();
            string colorTag, statusTag;
            switch (s)
            {
                case DiagState.Healthy:
                    colorTag = "#44FF66"; statusTag = "✓ HEALTHY"; break;
                case DiagState.EmptyTags:
                    colorTag = "#FFAA33"; statusTag = "⚠ EMPTY TAGS"; break;
                case DiagState.ZeroPersonality:
                    colorTag = "#FF4444"; statusTag = "✗ ZERO PERSONALITY"; break;
                case DiagState.TagResolverMissing:
                    colorTag = "#FF4444"; statusTag = "✗ NO RESOLVER"; break;
                default:
                    colorTag = "#888888"; statusTag = "? UNKNOWN"; break;
            }

            string body = BuildDiagnosticBody();

            // anchor 정보
            const float E = 0.001f;
            string ColorVal(float v) =>
                Mathf.Abs(v) < E
                    ? $"<color=#AA8888>{v:F3}</color>"
                    : $"<color=#AABBDD>{v:F3}</color>";
            string anchorLine =
                $"C={ColorVal(anchorPersonality.control)} " +
                $"S={ColorVal(anchorPersonality.stability)} " +
                $"O={ColorVal(anchorPersonality.openness)} " +
                $"A={ColorVal(anchorPersonality.agreeable)} " +
                $"D={ColorVal(anchorPersonality.directness)}";

            // Network 정보
            string netLine = "<color=#888888>(N/A — IsServer=false 또는 NetworkManager 비활성)</color>";
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (IsServer)
                {
                    var v = netPersonality.Value;
                    netLine = $"<color=#AAFFAA>(Server) C={v.control:F3} S={v.stability:F3} " +
                              $"O={v.openness:F3} A={v.agreeable:F3} D={v.directness:F3}</color>";
                }
                else
                {
                    var v = netPersonality.Value;
                    netLine = $"<color=#AABBFF>(Client) C={v.control:F3} S={v.stability:F3} " +
                              $"O={v.openness:F3} A={v.agreeable:F3} D={v.directness:F3}</color>";
                }
            }

            string headerMsg =
                $"<color={colorTag}><b>[HumanoidAIBrain Diag] {statusTag}</b></color> {name} " +
                $"<color=#888888>(force)</color>\n" +
                $"{body}\n" +
                $"  <b>anchor:</b>       {anchorLine}\n" +
                $"  <b>netPersonality:</b> {netLine}\n" +
                $"  <b>utilityFormula:</b> {(utilityFormula != null ? "<color=#44FF66>✓</color>" : "<color=#FF4444>✗ NULL</color>")} " +
                  $"  <b>traumaSystem:</b> {(traumaSystem != null ? "<color=#44FF66>✓</color>" : "<color=#888888>null</color>")} " +
                  $"  <b>trustMatrix:</b> {(trustMatrix != null ? "<color=#44FF66>✓</color>" : "<color=#888888>null</color>")}";

            Debug.Log(headerMsg);

            // 강제 재평가 시도
            if (tagResolver != null)
            {
                tagResolver.ResolveTagsFromPersonality(personality);
                var tags = tagResolver.ActiveTags;
                string afterStr;
                if (tags == null || tags.Count == 0)
                {
                    afterStr = "<color=#FF4444>[ ] (★ 여전히 빈 배열 — Resolver 의 매핑 임계치 또는 SO 자체 문제)</color>";
                }
                else
                {
                    afterStr = $"<color=#44FF66>[{string.Join(", ", tags)}]</color>";
                }
                Debug.Log($"<color=#FFCC44>  → 강제 재평가 후 ActiveTags:</color> {afterStr}");
            }
            else
            {
                Debug.LogWarning("<color=#FF4444>  → tagResolver == null, 재평가 불가</color>");
            }
        }

        /// <summary>★ v3.1 — 진단 상태만 한 줄로 (Auto Diagnostic 안 켰을 때 사용).</summary>
        [ContextMenu("★ Quick Status (Single Line)")]
        public void QuickStatusMenu()
        {
            DiagState s = ClassifyDiagState();
            string color = s == DiagState.Healthy ? "#44FF66"
                : s == DiagState.EmptyTags ? "#FFAA33"
                : "#FF4444";
            string tagsCount = tagResolver != null
                ? (tagResolver.ActiveTags?.Count ?? 0).ToString()
                : "?";
            Debug.Log($"<color={color}><b>[Quick {s}]</b></color> {name} | " +
                      $"P=<color=#88CCFF>(C{personality.control:F2}/S{personality.stability:F2}/" +
                                       $"O{personality.openness:F2}/A{personality.agreeable:F2}/" +
                                       $"D{personality.directness:F2})</color> | " +
                      $"Tags=<color=#88CCFF>{tagsCount}</color> | " +
                      $"fear=<color=#FFCC44>{fear:F2}</color> | " +
                      $"State=<color=#CCAAFF>{currentState}</color>");
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

            // ★ W5-B7 Hybrid Acceptance — FollowCommand 결함 정정
            //   기존: obedience × 0.7 (단순 곱 / Master Formula 우회 / α/fearPenalty 부재)
            //   정정: AcceptanceBase × Utility — TrustMatrix.ComputeAcceptanceBase 자료 정합
            //   AcceptanceBase = (Trust/100)×0.6 + severity×0.4 − fear  (음수 가능 → Clamp01)
            //   Utility        = needs["obedience"] (Week 1 fallback 단순 영역)
            //   ComputeAcceptanceBase 영역 — ITrustProvider 인터페이스 측 부재 영역.
            //   TrustMatrix 클래스 직접 영역 정의 영역 (TrustMatrix.cs line 432).
            //   본 필드 영역 TrustMatrix 타입 영역 직접 호출 영역.
            //   trustMatrix 미연결 시 정정 전 동작 보존 (회귀 영역).
            float followCommandScore;
            if (trustMatrix != null)
            {
                const float severity = 0.5f;            // commandUrgency 자료 부재 영역 기본
                float acceptanceBase = trustMatrix.ComputeAcceptanceBase(severity, currentFear);
                float utility = needs["obedience"];
                followCommandScore = Mathf.Clamp01(acceptanceBase * utility);
            }
            else
            {
                followCommandScore = needs["obedience"] * 0.7f;  // fallback 회귀 영역
            }

            return new Dictionary<string, float>
            {
                ["Attack"] = currentTarget != null
                    ? (1.0f - currentFear * fearMod) * aggressionMod * 0.8f
                    : 0f,
                ["Flee"] = currentFear * fearMod * 0.9f,
                ["Loot"] = needs["greed"] * greedMod * (currentTarget != null ? 0.0f : 0.7f),
                ["Move"] = (1.0f - needs["fatigue"]) * personality.openness * 0.3f,
                ["FollowCommand"] = followCommandScore,
            };
        }

        // ==================================================================
        // SelectNewState — 히스테리시스
        // ==================================================================

        private void SelectNewState(Dictionary<string, float> scores)
        {
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

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                netCurrentState.Value = (int)newState;

            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: 상태 전환 {oldState} → {newState}");

            ExecuteBTNode(currentState.ToString());
        }

        // ==================================================================
        // Test-only: 강제 전환
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
                _lastDiagState = DiagState.Unknown;   // ★ v3.1 — Archetype 적용 시 진단 리셋

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
                case "Attack": break;
                case "Flee": break;
                case "Loot": break;
                case "Move": break;
                case "FollowCommand": break;
                case "Idle": break;
                default: break;
            }
        }

        // ==================================================================
        // 외부 API
        // ==================================================================

        public void PivotPersonality(float[] delta)
        {
            if (delta == null || delta.Length != 5) return;

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

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                netPersonality.Value = personality;

            MarkTagsDirty("PivotPersonality");
        }

        public PersonalityMatrix GetAnchor() => anchorPersonality;

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

        // ==================================================================
        // ★ Phase 5 v5 — SetPersonality (HumanoidBootstrapper.randomizePersonality 용)
        // ==================================================================

        /// <summary>
        /// ★ Phase 5 v5 — Personality 5축을 통째로 교체 + Tag 재평가 트리거.
        /// HumanoidBootstrapper.BootstrapOne 의 randomizePersonality ☑ 시 호출.
        /// 서버 권한 시 NetworkVariable 동기화.
        /// </summary>
        public void SetPersonality(PersonalityMatrix newPersonality)
        {
            personality = newPersonality;
            anchorPersonality = newPersonality;
            needsTagReEvaluation = true;
            _lastDiagState = DiagState.Unknown;   // ★ v3.1 — 진단 리셋

            // 서버 권한 시 NetworkVariable 동기화
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
                netPersonality.Value = personality;

            if (verboseLogging)
                Debug.Log($"[HumanoidAIBrain] {name}: ★ SetPersonality " +
                          $"(C={newPersonality.control:F2} S={newPersonality.stability:F2} " +
                          $"O={newPersonality.openness:F2} A={newPersonality.agreeable:F2} " +
                          $"D={newPersonality.directness:F2})");
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