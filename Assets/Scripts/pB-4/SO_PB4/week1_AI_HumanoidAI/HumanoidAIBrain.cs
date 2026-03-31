// =============================================================================
// HumanoidAIBrain.cs  |  pB-4 Project — Week 1
// Layer  : L3 Domain (AI)
// Owner  : Person A
//
// 역할:
//   인간형 NPC(동료)의 의사결정 엔진 기초.
//   MobAIBrain과 달리 PersonalityMatrix 5축을 보유하며,
//   Week 2에서 Master Formula로 확장, Week 4에서 신뢰도/트라우마 추가,
//   Week 7에서 발화 시스템과 연동된다.
//   이번 주차에서는 5축 구조체 연결 + 기초 BT Root만 구축.
//
// PersonalityMatrix 5축:
//   control(자제력), stability(안정성), openness(개방성),
//   agreeable(우호성), directness(직설성) — 각 0.0~1.0
// =============================================================================
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.Data;
using TDA.PB4.Core;

namespace TDA.PB4.AI.Humanoid
{
    /// <summary>
    /// 성격 5축 구조체. 모든 인간형 NPC의 개성을 정의.
    /// Week 2에서 유틸리티 수식에 직접 연동.
    /// Week 7에서 발화 시스템의 단어 선택에 연동.
    /// </summary>
    [System.Serializable]
    public struct PersonalityMatrix
    {
        [Range(0f, 1f)] public float control;     // 자제력: 높으면 충동 억제, 낮으면 즉각 반응
        [Range(0f, 1f)] public float stability;   // 안정성: 높으면 트라우마 저항, 낮으면 재앙 확률 상승
        [Range(0f, 1f)] public float openness;    // 개방성: 높으면 새 경험 추구, 낮으면 안전 선호
        [Range(0f, 1f)] public float agreeable;   // 우호성: 높으면 협력적, 낮으면 이기적
        [Range(0f, 1f)] public float directness;  // 직설성: 높으면 단도직입, 낮으면 완곡 표현

        /// <summary>5축을 float[5] 배열로 변환 (유틸리티 수식 입력용)</summary>
        public float[] ToArray() => new float[] { control, stability, openness, agreeable, directness };

        /// <summary>랜덤 성격 생성 (테스트용)</summary>
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

        // 성격 앵커 (Week 4에서 트라우마 시스템이 비교하는 원점)
        private PersonalityMatrix anchorPersonality;

        // ==================================================================
        // BT 상태
        // ==================================================================
        [Header("BT State")]
        [SerializeField] private HumanoidBTState currentState = HumanoidBTState.Idle;
        public HumanoidBTState CurrentState => currentState;

        // ==================================================================
        // 유틸리티 점수 (Week 2에서 Master Formula로 확장)
        // ==================================================================
        [Header("Debug — Utility Scores")]
        [SerializeField] private float u_attack;
        [SerializeField] private float u_flee;
        [SerializeField] private float u_loot;
        [SerializeField] private float u_move;

        [HideInInspector] public Transform currentTarget;
        [HideInInspector] public Vector3 moveDestination;

        private float decisionTimer = 0f;
        private const float DECISION_INTERVAL = 0.3f; // HumanoidAI는 0.3초 간격

        // ==================================================================
        // Lifecycle
        // ==================================================================
        protected override void Awake()
        {
            base.Awake();
            anchorPersonality = personality; // 초기 성격을 앵커로 저장
        }

        private void Update()
        {
            decisionTimer += Time.deltaTime;
            if (decisionTimer >= DECISION_INTERVAL)
            {
                decisionTimer = 0f;
                UpdateFearFromTerrain();
                UpdateDecision();
            }
        }

        // ==================================================================
        // Week 1: 기초 유틸리티 (Week 2에서 Master Formula로 교체)
        // ==================================================================
        public override void UpdateDecision()
        {
            // 기초 수식: 성격 축이 유틸리티에 직접 영향
            float aggressionMod = 1.0f - personality.agreeable; // 비우호적일수록 공격적
            float fearMod = 1.0f - personality.stability;       // 불안정할수록 공포에 취약
            float greedMod = 1.0f - personality.control;        // 자제력 낮으면 탐욕적

            u_attack = (1.0f - fear * fearMod) * aggressionMod * 0.8f;
            u_flee   = fear * fearMod * 0.9f;
            u_loot   = greed * greedMod * (currentTarget != null ? 0.0f : 0.7f); // 전투 중엔 루팅 안 함
            u_move   = (1.0f - fatigue) * personality.openness * 0.3f;

            if (currentTarget == null) u_attack = 0f;

            // Winner-takes-all
            float maxU = Mathf.Max(u_attack, Mathf.Max(u_flee, Mathf.Max(u_loot, u_move)));

            HumanoidBTState newState;
            if (maxU == u_attack)      newState = HumanoidBTState.Attack;
            else if (maxU == u_flee)   newState = HumanoidBTState.Flee;
            else if (maxU == u_loot)   newState = HumanoidBTState.Loot;
            else if (maxU == u_move)   newState = HumanoidBTState.Move;
            else                       newState = HumanoidBTState.Idle;

            if (newState != currentState)
            {
                currentState = newState;
                ExecuteBTNode(currentState.ToString());
            }
        }

        // ==================================================================
        // BT 노드 실행
        // ==================================================================
        public override void ExecuteBTNode(string goalId)
        {
            switch (goalId)
            {
                case "Attack":  /* TODO: Week 3에서 GroupAI 토큰 연동 */ break;
                case "Flee":    /* TODO: Week 4에서 트라우마 시스템 연동 */ break;
                case "Loot":    /* 보물/아이템 탐색 */ break;
                case "Move":    /* NavMesh 기반 이동 */ break;
                case "FollowCommand": /* Week 4에서 신뢰도 기반 명령 복종 */ break;
                case "Idle":
                default:        break;
            }
        }

        // ==================================================================
        // 외부 API (Week 2~7에서 확장)
        // ==================================================================
        /// <summary>성격 피봇팅. Week 7에서 PersonalityPivotManager가 호출.</summary>
        public void PivotPersonality(float[] delta)
        {
            if (delta == null || delta.Length != 5) return;
            personality.control   = Mathf.Clamp01(personality.control + delta[0]);
            personality.stability = Mathf.Clamp01(personality.stability + delta[1]);
            personality.openness  = Mathf.Clamp01(personality.openness + delta[2]);
            personality.agreeable = Mathf.Clamp01(personality.agreeable + delta[3]);
            personality.directness = Mathf.Clamp01(personality.directness + delta[4]);
        }

        public PersonalityMatrix GetAnchor() => anchorPersonality;
    }
}
