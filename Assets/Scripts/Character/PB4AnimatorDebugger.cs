using UnityEngine;
using UnityEngine.AI;
using TDA.Character.AI;
using Unity.Behavior;           // BehaviorGraphAgent, BlackboardVariable

namespace TDA.PB4.Diagnostics
{
    public class PB4AnimatorDebugger : MonoBehaviour
    {
        [Header("━━━ ① NavMeshAgent ━━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool agent_hasPath;
        [SerializeField] private string agent_pathStatus = "None";
        [SerializeField] private float agent_velocityMag;
        [SerializeField] private float agent_desiredVelocityMag;
        [SerializeField] private float agent_speed;
        [SerializeField] private float agent_remainingDistance;
        [SerializeField] private bool agent_updatePosition;
        [SerializeField] private float agent_nextPosOffset;

        // [Phase 5 FSM 탈락] ② 헤더 "FSM 상태" → "BT 상태 (Blackboard)"
        // currentState / usePB4BehaviorTree → BT Blackboard 기반으로 교체
        [Header("━━━ ② BT 상태 (Blackboard) ━━━━━━━━━━━")]
        [SerializeField] private string bt_utilityWinner = "None";
        [SerializeField] private float bt_fear;
        [SerializeField] private bool bt_hasTarget;
        [SerializeField] private string bt_targetName = "None";
        [SerializeField] private bool bt_isPerformingAction;

        [Header("━━━ ③ Animator ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private float anim_moveAmount;
        [SerializeField] private float anim_Horizontal;
        [SerializeField] private float anim_Vertical;
        [SerializeField] private bool anim_applyRootMotion;
        [SerializeField] private string anim_clipName = "None";

        [Header("━━━ ④ pB-4 AI ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private string pb4_state = "None";
        [SerializeField] private float pb4_fear;
        [SerializeField] private bool pb4_hasTarget;
        [SerializeField] private string pb4_targetName = "None";

        [Header("━━━ ⑤ 진단 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField][TextArea(3, 10)] private string diagnosis = "대기 중...";

        [Header("━━━ 설정 ━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        public float logInterval = 2f;
        public bool enableLog = true;

        private AICharacterManager _aiMgr;
        private NavMeshAgent _nav;
        private Animator _anim;
        private CharacterController _cc;
        private BehaviorGraphAgent _btAgent;   // [Phase 5]
        private float _timer;

        private static readonly int H_H = Animator.StringToHash("Horizontal");
        private static readonly int H_V = Animator.StringToHash("Vertical");
        private static readonly int H_M = Animator.StringToHash("moveAmount");

        private void Awake()
        {
            _aiMgr = GetComponent<AICharacterManager>();
            _nav = GetComponent<NavMeshAgent>();
            _anim = GetComponent<Animator>();
            _cc = GetComponent<CharacterController>();
            _btAgent = GetComponent<BehaviorGraphAgent>(); // [Phase 5]
        }

        private void Update()
        {
            if (_nav == null || !_nav.isActiveAndEnabled || !_nav.isOnNavMesh)
                return;
            if (!Application.isPlaying) return;

            // ① NavMeshAgent
            if (_nav != null)
            {
                agent_hasPath = _nav.hasPath;
                agent_pathStatus = _nav.pathStatus.ToString();
                agent_velocityMag = _nav.velocity.magnitude;
                agent_desiredVelocityMag = _nav.desiredVelocity.magnitude;
                agent_speed = _nav.speed;
                agent_remainingDistance = _nav.remainingDistance;
                agent_updatePosition = _nav.updatePosition;
                agent_nextPosOffset = Vector3.Distance(_nav.nextPosition, transform.position);
            }

            // ② BT 상태 (Blackboard) — [Phase 5] FSM currentState 대체
            if (_btAgent != null && _btAgent.BlackboardReference != null)
            {
                var bbRef = _btAgent.BlackboardReference;

                bbRef.GetVariable("UtilityWinner", out BlackboardVariable<string> wVar);
                bbRef.GetVariable("Fear", out BlackboardVariable<float> fVar);
                bbRef.GetVariable("HasTarget", out BlackboardVariable<bool> htVar);
                bbRef.GetVariable("Target", out BlackboardVariable<Transform> tVar);

                bt_utilityWinner = wVar?.Value ?? "---";
                bt_fear = fVar?.Value ?? 0f;
                bt_hasTarget = htVar?.Value ?? false;
                bt_targetName = tVar?.Value != null ? tVar.Value.name : "None";
                bt_isPerformingAction = _aiMgr != null && _aiMgr.isPerformingAction;
            }
            else if (_aiMgr != null)
            {
                // BehaviorGraphAgent 없을 때 CombatManager 기반 폴백
                bool ht = _aiMgr.aiCharacterCombatManager != null
                       && _aiMgr.aiCharacterCombatManager.currentTarget != null;
                bt_hasTarget = ht;
                bt_targetName = ht ? _aiMgr.aiCharacterCombatManager.currentTarget.name : "None";
                bt_isPerformingAction = _aiMgr.isPerformingAction;
            }

            // ③ Animator
            if (_anim != null)
            {
                anim_moveAmount = _anim.GetFloat(H_M);
                anim_Horizontal = _anim.GetFloat(H_H);
                anim_Vertical = _anim.GetFloat(H_V);
                anim_applyRootMotion = _anim.applyRootMotion;
                var si = _anim.GetCurrentAnimatorStateInfo(0);
                if (si.IsName("Idle_Standard")) anim_clipName = "Idle_Standard";
                else if (si.IsName("FreeMove_BlendTree")) anim_clipName = "FreeMove_BlendTree";
                else if (si.IsName("Strafe_BlendTree")) anim_clipName = "Strafe_BlendTree";
                else anim_clipName = $"Hash:{si.shortNameHash}";
            }

            // ④ pB-4 Brain (Brain 직접 참조 — BT 와 독립)
            var mob = GetComponent<TDA.PB4.AI.Mob.MobAIBrain>();
            if (mob != null)
            {
                pb4_state = mob.CurrentState.ToString();
                pb4_fear = mob.fear;
                pb4_hasTarget = mob.currentTarget != null;
                pb4_targetName = mob.currentTarget != null ? mob.currentTarget.name : "None";
            }
            else
            {
                var hum = GetComponent<TDA.PB4.AI.Humanoid.HumanoidAIBrain>();
                if (hum != null)
                {
                    pb4_state = hum.CurrentState.ToString();
                    pb4_fear = hum.fear;
                    pb4_hasTarget = hum.currentTarget != null;
                    pb4_targetName = hum.currentTarget != null ? hum.currentTarget.name : "None";
                }
            }

            // ⑤ 진단
            string d = "";

            if (!bt_hasTarget)
                d += "❌ BB.HasTarget=false → AI 가 타겟을 인식하지 못함 (AIPerceptionSystem 확인)\n";

            if (pb4_hasTarget && !bt_hasTarget)
                d += $"❌ SYNC 실패: pB4 타겟={pb4_targetName} → BB HasTarget=false\n" +
                     "   → PB4DecisionAdapter.SyncTarget() 동작 확인\n";

            if (_nav != null && !_nav.hasPath)
                d += "❌ hasPath=false → SetDestination 미호출 또는 ResetPath 이후 미갱신\n";

            if (_nav != null && agent_velocityMag < 0.01f && _nav.hasPath)
                d += $"❌ velocity=0 (경로 있음) desVel={agent_desiredVelocityMag:F2} nextOff={agent_nextPosOffset:F2}m\n";

            if (_anim != null && anim_moveAmount < 0.001f && agent_velocityMag > 0.1f)
                d += $"❌ moveAmount=0 (velocity={agent_velocityMag:F3}) → LocomotionManager 동기화 확인\n";

            if (bt_isPerformingAction)
                d += $"ℹ️ isPerformingAction=true (공격/피격 모션 중)\n";

            if (_anim != null)
                d += $"ℹ️ rootMotion={anim_applyRootMotion} clip={anim_clipName}\n";

            if (string.IsNullOrEmpty(d))
                d = $"✅ 정상: vel={agent_velocityMag:F2} → moveAmt={anim_moveAmount:F3} → {anim_clipName}";

            diagnosis = d;

            // Console 로그
            _timer += Time.deltaTime;
            if (enableLog && _timer >= logInterval)
            {
                _timer = 0f;
                Debug.Log(
                    $"<color=cyan>[PB4Debug] {name}</color>\n" +
                    $"  Nav: path={agent_hasPath} vel={agent_velocityMag:F2} desVel={agent_desiredVelocityMag:F2} " +
                    $"spd={agent_speed:F2} nxtOff={agent_nextPosOffset:F2} updPos={agent_updatePosition}\n" +
                    $"  BT: Winner={bt_utilityWinner} Fear={bt_fear:F2} HasTgt={bt_hasTarget} tgt={bt_targetName} performing={bt_isPerformingAction}\n" +
                    $"  Anim: move={anim_moveAmount:F3} H={anim_Horizontal:F3} V={anim_Vertical:F3} " +
                    $"root={anim_applyRootMotion} clip={anim_clipName}\n" +
                    $"  pB4: {pb4_state} fear={pb4_fear:F2} tgt={pb4_targetName}\n" +
                    $"  진단: {diagnosis.Replace("\n", " | ")}");
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || _nav == null) return;
            if (_nav.hasPath)
            {
                Gizmos.color = Color.cyan;
                var c = _nav.path.corners;
                for (int i = 0; i < c.Length - 1; i++) Gizmos.DrawLine(c[i], c[i + 1]);
            }
            Gizmos.color = Color.red;
            if (_nav.hasPath) Gizmos.DrawWireSphere(_nav.destination, 0.5f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_nav.nextPosition, 0.3f);
            if (agent_velocityMag > 0.1f)
            { Gizmos.color = Color.green; Gizmos.DrawRay(transform.position + Vector3.up, _nav.velocity); }
            if (agent_desiredVelocityMag > 0.1f)
            { Gizmos.color = Color.yellow; Gizmos.DrawRay(transform.position + Vector3.up * 1.2f, _nav.desiredVelocity); }
        }
#endif
    }
}