using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;

#if UNITY_EDITOR
using UnityEditor; // Handles.Label 등 에디터 전용 기능을 위해 필요
#endif

namespace TDA.Character.AI
{
    /// <summary>
    /// AI 캐릭터의 전투 전술(탐지, 방어, 포위망)과 상태를 관리하는 매니저입니다.
    /// 부모 클래스(CharacterCombatManager)의 물리 규약을 100% 계승하며, 
    /// 애니메이션 이벤트로부터 FSM(상태 머신) 패턴 전이 신호를 수신합니다.
    /// </summary>
    public class AICharacterCombatManager : CharacterCombatManager
    {
        private AICharacterManager aiCharacter;

        [Header("Debug Settings")]
        [Tooltip("일반적인 전투 상태 변화 등의 로그를 표시합니다.")]
        public bool showDebugLogs = false; // 기본값 꺼짐

        [Tooltip("1초마다 전방 시야(FOV) 내의 적을 탐지하여 로그를 띄웁니다. (너무 많을 수 있으니 필요할 때만 켜세요)")]
        public bool showFOVScanLogs = false; // [추가] 전방 탐지 전용 플래그 (기본값 꺼짐)

        [Header("AI Intelligence & Combat Skill")]
        [Range(1, 10)] public int aiIntelligenceLevel = 5;
        [Range(1, 10)] public int combatLevel = 5;

        [Header("AI Pattern Flow (P1)")]
        public bool isPatternChainingPossible = false;

        [Header("Patrol & Wander Settings")]
        public float wanderRadius = 10f;
        public float wanderIntervalMin = 3f;
        public float wanderIntervalMax = 7f;
        [Range(0, 100)] public float groupUpChance = 40f;

        [Header("Patrol Waypoints (High Intelligence)")]
        public Transform[] patrolWaypoints;
        [HideInInspector] public int currentWaypointIndex = 0;

        [Header("Reinforcement Settings")]
        public float callForHelpRadius = 30f;
        public int intelligenceToCallHelp = 8;
        [HideInInspector] public bool hasCalledForHelp = false;

        [Header("State Execution Data (Runtime - Do Not Touch)")]
        [HideInInspector] public bool isWandering = false;
        [HideInInspector] public float nextWanderTime = 0f;
        [HideInInspector] public AICharacterManager targetAllyToCall = null;
        [HideInInspector] public float nextAttackTime = 0f;

        [Header("Detection Settings")]
        public float detectionRadius = 15f;
        public float minimumDetectionAngle = -50f;
        public float maximumDetectionAngle = 50f;

        [Header("Defense Tactics")]
        [Range(0, 100)] public float baseParryChance = 20f;
        [Range(0, 100)] public float baseBlockChance = 30f;
        [Range(0, 100)] public float baseEvadeChance = 20f;
        public float defensiveActionCooldown = 2f;
        private float nextDefensiveActionTime;

        [Header("Strafing Tactics (Circling)")]
        public float strafeSpeed = 2f;
        public float strafeDirectionChangeTime = 3f;
        private float nextStrafeChangeTime;
        private int strafeDirection = 1;

        // [디버깅] 로깅 쿨타임용 변수
        private float debugScanTimer = 0f;

        protected override void Awake()
        {
            base.Awake();
            aiCharacter = GetComponent<AICharacterManager>();
        }

        // =========================================================================================
        // [디버그] 전방 10m 탐지 및 로그 출력 (주기적 실행)
        // =========================================================================================
        private void Update()
        {
            // [수정] 전방 탐지 전용 플래그가 켜져 있을 때만 실행되도록 변경
            if (showFOVScanLogs)
            {
                debugScanTimer += Time.deltaTime;
                if (debugScanTimer >= 1f) // 1초에 한 번씩만 스캔하여 콘솔 부하 방지
                {
                    debugScanTimer = 0f;
                    LogCharactersInFront(10f); // 10미터 기준 탐지
                }
            }
        }

        private void LogCharactersInFront(float distance)
        {
            if (WorldUtilityManager.Instance == null) return;

            // 반경 10미터 안의 캐릭터 레이어 오브젝트들을 가져옵니다.
            Collider[] colliders = Physics.OverlapSphere(transform.position, distance, WorldUtilityManager.Instance.GetCharacterLayers());

            foreach (var collider in colliders)
            {
                CharacterManager otherCharacter = collider.GetComponent<CharacterManager>();

                // 자기 자신이 아니고, 죽지 않은 대상만 판별
                if (otherCharacter != null && otherCharacter != aiCharacter && !otherCharacter.characterNetworkManager.isDead.Value)
                {
                    Vector3 directionToTarget = otherCharacter.transform.position - transform.position;
                    directionToTarget.y = 0; // 평면 기준으로만 계산

                    // Vector3.SignedAngle을 사용하여 좌/우 각도(-180 ~ 180)를 정확히 판별합니다.
                    float viewableAngle = Vector3.SignedAngle(transform.forward, directionToTarget.normalized, Vector3.up);

                    // 지정된 시야각(minimumDetectionAngle ~ maximumDetectionAngle) 내에 들어왔는지 체크
                    if (viewableAngle >= minimumDetectionAngle && viewableAngle <= maximumDetectionAngle)
                    {
                        float dist = Vector3.Distance(transform.position, otherCharacter.transform.position);

                        // 탐지 로그는 일반적인 DebugLog의 showDebugLogs 조건과 별개로, showFOVScanLogs에 종속되도록 바로 출력
                        Debug.Log($"<color=#32CD32>[{gameObject.name}]</color> 👁️ 전방 {distance}m 시야 내 포착: <color=yellow>{otherCharacter.gameObject.name}</color> (거리: {dist:F1}m, 각도: {viewableAngle:F1}도)");
                    }
                }
            }
        }

        // =========================================================================================
        // [P4] AI 전용 애니메이션 이벤트 수신 (Enum 기반)
        // =========================================================================================
        public override void OnAnimationEventReceived(AnimationEventType eventType)
        {
            // 부모의 공용 히트박스 제어 실행
            base.OnAnimationEventReceived(eventType);

            // AI 특화 로직 처리 (FSM 패턴 체이닝)
            if (eventType == AnimationEventType.ComboEnable)
            {
                isPatternChainingPossible = true;
                DebugLog("[Combo] 콤보 허용 창(Window) 개방. 다음 패턴으로 전이 가능.");
            }
            else if (eventType == AnimationEventType.ComboDisable || eventType == AnimationEventType.Action_Ended)
            {
                isPatternChainingPossible = false;
            }
        }

        public void DebugLog(string message)
        {
            if (!showDebugLogs) return;
            Debug.Log($"<color=#32CD32>[{gameObject.name}]</color> {message}");
        }

        // =========================================================================================
        // 전투 전술 및 방어 로직
        // =========================================================================================
        public void HandleDefensiveActions()
        {
            if (currentTarget == null) return;
            if (aiCharacter.isPerformingAction) return;

            if (currentTarget.isPerformingAction)
            {
                if (Time.time < nextDefensiveActionTime) return;

                float levelModifier = combatLevel / 10f;
                float actualEvadeChance = baseEvadeChance * levelModifier;
                float actualParryChance = baseParryChance * levelModifier;
                float actualBlockChance = baseBlockChance * levelModifier;

                float randomDice = Random.Range(0f, 100f);

                if (randomDice < actualEvadeChance) PerformEvade();
                else if (randomDice < actualEvadeChance + actualParryChance) PerformParry();
                else if (randomDice < actualEvadeChance + actualParryChance + actualBlockChance) PerformBlock();

                float actualCooldown = defensiveActionCooldown * (1f + (1f - levelModifier));
                nextDefensiveActionTime = Time.time + actualCooldown;
            }
        }

        private void PerformEvade()
        {
            Vector3 attackerForward = currentTarget.transform.forward;
            float dotRight = Vector3.Dot(transform.right, attackerForward);
            string evadeAnimation = "Dodge_Back";

            float randomDirection = Random.value;
            if (randomDirection < 0.6f)
            {
                if (dotRight > 0) evadeAnimation = "Dodge_Right";
                else evadeAnimation = "Dodge_Left";
            }

            DebugLog($"방어 행동: 회피 ({evadeAnimation})");
            aiCharacter.characterAnimationManager.PlayTargetAnimation(Animator.StringToHash(evadeAnimation), true, true);
        }

        private void PerformParry()
        {
            DebugLog("방어 행동: 패링 (Parry_01)");
            aiCharacter.characterAnimationManager.PlayTargetAnimation(Animator.StringToHash("Parry_01"), true, true);
        }

        private void PerformBlock()
        {
            DebugLog("방어 행동: 방어 (Block_Start)");
            aiCharacter.characterAnimationManager.PlayTargetAnimation(Animator.StringToHash("Block_Start"), true, true);
        }

        public void HandleStrafingAroundTarget()
        {
            if (currentTarget == null) return;

            bool isOneOnOne = !HasAlliesTargetingSame();

            if (isOneOnOne)
            {
                if (Time.time > nextStrafeChangeTime)
                {
                    strafeDirection = Random.value > 0.5f ? 1 : -1;
                    nextStrafeChangeTime = Time.time + Random.Range(2f, strafeDirectionChangeTime);
                    DebugLog($"[1:1 대치] 지능 무관: {(strafeDirection == 1 ? "우측" : "좌측")}으로 맴돌기 수행 중");
                }
            }
            else if (aiIntelligenceLevel >= 7)
            {
                CalculateFlankingDirection();
            }
            else
            {
                if (Time.time > nextStrafeChangeTime)
                {
                    strafeDirection = Random.value > 0.5f ? 1 : -1;
                    nextStrafeChangeTime = Time.time + Random.Range(2f, strafeDirectionChangeTime);
                }
            }

            Vector3 targetDirection = currentTarget.transform.position - transform.position;
            targetDirection.y = 0;
            targetDirection.Normalize();

            Vector3 crossDirection = Vector3.Cross(targetDirection, Vector3.up).normalized;
            Vector3 strafeVector = crossDirection * strafeDirection;

            Vector3 targetDestination = transform.position + (strafeVector * strafeSpeed);
            if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                aiCharacter.navMeshAgent.SetDestination(targetDestination);
            }

            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(strafeDirection * 0.5f, 0, false);

            Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
        }

        private void CalculateFlankingDirection()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, 10f, WorldUtilityManager.Instance.GetCharacterLayers());
            AICharacterManager nearestAlly = null;
            float closestDistance = Mathf.Infinity;

            foreach (var collider in colliders)
            {
                AICharacterManager ally = collider.GetComponent<AICharacterManager>();
                if (ally != null && ally != aiCharacter && ally.characterGroup == aiCharacter.characterGroup)
                {
                    if (ally.aiCharacterCombatManager.currentTarget == currentTarget)
                    {
                        float distance = Vector3.Distance(transform.position, ally.transform.position);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            nearestAlly = ally;
                        }
                    }
                }
            }

            if (nearestAlly != null)
            {
                Vector3 targetDirection = (currentTarget.transform.position - transform.position).normalized;
                Vector3 rightDir = Vector3.Cross(targetDirection, Vector3.up).normalized;
                Vector3 dirToAlly = (nearestAlly.transform.position - transform.position).normalized;
                float dot = Vector3.Dot(rightDir, dirToAlly);

                int oldDirection = strafeDirection;
                strafeDirection = dot > 0 ? -1 : 1;

                if (oldDirection != strafeDirection)
                    DebugLog("전술적 행동: 아군과 겹치지 않게 포위망 폅니다.");
            }
        }

        public bool HasAlliesTargetingSame()
        {
            if (currentTarget == null) return false;
            Collider[] colliders = Physics.OverlapSphere(transform.position, 15f, WorldUtilityManager.Instance.GetCharacterLayers());
            foreach (var collider in colliders)
            {
                AICharacterManager ally = collider.GetComponent<AICharacterManager>();
                if (ally != null && ally != aiCharacter && ally.characterGroup == aiCharacter.characterGroup)
                {
                    if (ally.aiCharacterCombatManager.currentTarget == currentTarget) return true;
                }
            }
            return false;
        }

        public bool IsAllyAttackingTarget()
        {
            if (currentTarget == null) return false;
            Collider[] colliders = Physics.OverlapSphere(transform.position, 10f, WorldUtilityManager.Instance.GetCharacterLayers());
            foreach (var collider in colliders)
            {
                AICharacterManager ally = collider.GetComponent<AICharacterManager>();
                if (ally != null && ally != aiCharacter && ally.characterGroup == aiCharacter.characterGroup)
                {
                    if (ally.aiCharacterCombatManager.currentTarget == currentTarget && ally.isPerformingAction) return true;
                }
            }
            return false;
        }

        public AICharacterManager FindNearestPeacefulAlly()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, callForHelpRadius, WorldUtilityManager.Instance.GetCharacterLayers());
            AICharacterManager nearestAlly = null;
            float closestDistance = Mathf.Infinity;

            foreach (var collider in colliders)
            {
                AICharacterManager ally = collider.GetComponent<AICharacterManager>();
                if (ally != null && ally != aiCharacter && ally.characterGroup == aiCharacter.characterGroup)
                {
                    if (ally.aiCharacterCombatManager.currentTarget == null)
                    {
                        float distance = Vector3.Distance(transform.position, ally.transform.position);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            nearestAlly = ally;
                        }
                    }
                }
            }
            return nearestAlly;
        }

        public Vector3 GetWanderLocation()
        {
            Vector3 targetLocation = transform.position;

            if (Random.Range(0f, 100f) < groupUpChance)
            {
                AICharacterManager randomAlly = FindNearestPeacefulAlly();
                if (randomAlly != null) targetLocation = randomAlly.transform.position;
            }

            Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
            randomDirection += targetLocation;

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, 1)) return hit.position;

            return transform.position;
        }

        // =========================================================================================
        // [디버그] 씬 뷰 시각화 (기즈모 및 텍스트 렌더링)
        // =========================================================================================
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 1. 시야각(FOV) 부채꼴 기즈모 표시
            Gizmos.color = new Color(1f, 1f, 0f, 0.4f); // 반투명 노란색
            Vector3 headPosition = transform.position + Vector3.up * 1.5f;
            Vector3 forward = transform.forward;

            // 좌우 한계선 계산
            Vector3 leftRay = Quaternion.Euler(0, minimumDetectionAngle, 0) * forward;
            Vector3 rightRay = Quaternion.Euler(0, maximumDetectionAngle, 0) * forward;

            Gizmos.DrawRay(headPosition, leftRay * detectionRadius);
            Gizmos.DrawRay(headPosition, rightRay * detectionRadius);
            Gizmos.DrawRay(headPosition, forward * detectionRadius); // 중앙 기준선

            // 부채꼴 곡선 묘사
            int segments = 20;
            float angleStep = (maximumDetectionAngle - minimumDetectionAngle) / segments;
            Vector3 previousPoint = headPosition + leftRay * detectionRadius;

            for (int i = 1; i <= segments; i++)
            {
                float currentAngle = minimumDetectionAngle + (angleStep * i);
                Vector3 currentDirection = Quaternion.Euler(0, currentAngle, 0) * forward;
                Vector3 currentPoint = headPosition + currentDirection * detectionRadius;
                Gizmos.DrawLine(previousPoint, currentPoint);
                previousPoint = currentPoint;
            }

            // 2. 머리 위에 현재 FSM 상태 이름 텍스트로 띄우기
            if (aiCharacter != null)
            {
                GUIStyle style = new GUIStyle();
                style.normal.textColor = Color.red;
                style.fontSize = 14;
                style.fontStyle = FontStyle.Bold;
                style.alignment = TextAnchor.MiddleCenter;

                string stateName = "Unknown State";

                // AICharacterManager 내부에 있을 currentState 변수 값을 가져와 표시합니다.
                // 컴파일 에러를 방지하기 위해 Reflection을 사용하여 동적으로 값을 추출합니다.
                var stateField = typeof(AICharacterManager).GetField("currentState", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (stateField != null)
                {
                    var stateObj = stateField.GetValue(aiCharacter);
                    if (stateObj != null)
                    {
                        stateName = stateObj.GetType().Name; // 예: "PursueTargetState"
                    }
                }

                Handles.Label(headPosition + Vector3.up * 0.8f, $"[ {stateName} ]", style);
            }
        }
#endif
    }
}