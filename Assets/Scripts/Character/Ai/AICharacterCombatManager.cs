// =============================================================================
// AICharacterCombatManager.cs  |  TDA Project
// Layer  : L3 Domain — AI 전투 행동 실행
// 수정 이력:
//   P0 ① HandleStrafingAroundTarget : UpdateAnimatorMovementParameters 완전 제거
//                                      → navMeshAgent.speed + SetDestination 전용
//   P0 ② PerformEvade               : 문자열 직접 조작 → ActionID + PlayTargetActionFunnel
//   P1 ③ PerformParry               : CharacterDefenseManager.StartDefense(CloseGrip) 연동
//   P1 ④ PerformBlock               : CharacterDefenseManager.StartDefense(FarGrip) 연동
//   Fix  : 누락되었던 AI 속성(Wander, Patrol, Detection, FOV 로그 등) 원본 복구
//   Fix  : CS1739(명명된 매개변수), CS7036(오버로드 누락), CS0117(Enum 누락) 에러 우회 적용
// =============================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
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
        // ─────────────────────────────────────────────────────────────────────
        // 내부 참조
        // ─────────────────────────────────────────────────────────────────────
        private AICharacterManager aiCharacter;

        [Header("Debug Settings")]
        [Tooltip("일반적인 전투 상태 변화 등의 로그를 표시합니다.")]
        public bool showDebugLogs = false; // 기본값 꺼짐

        [Tooltip("1초마다 전방 시야(FOV) 내의 적을 탐지하여 로그를 띄웁니다. (너무 많을 수 있으니 필요할 때만 켜세요)")]
        public bool showFOVScanLogs = false; // [추가] 전방 탐지 전용 플래그 (기본값 꺼짐)

        [Header("AI Intelligence & Combat Skill")]
        [Range(1, 10)] public int aiIntelligenceLevel = 5;
        [Tooltip("AI 전투 레벨 — 높을수록 방어/패링/위빙 확률 상승")]
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

        // ─────────────────────────────────────────────────────────────────────
        // 탐지 · 공격 범위 (수정본 추가)
        // ─────────────────────────────────────────────────────────────────────
        [Header("Detection & Attack Range")]
        [Tooltip("이 거리 이하에서 공격 시도")]
        public float maximumAttackRange = 2.0f;
        [Tooltip("이 거리 이하에선 공격 취소")]
        public float minimumAttackRange = 0.5f;
        [Tooltip("공격 발동 유효 각도")]
        public float viewableAngle = 180f;

        // ─────────────────────────────────────────────────────────────────────
        // 공격 쿨다운 (수정본 추가)
        // ─────────────────────────────────────────────────────────────────────
        [Header("Attack Cooldown")]
        public float minimumTimeBetweenAttacks = 1.5f;
        public float maximumTimeBetweenAttacks = 3.5f;
        private float currentAttackTimer = 0f;

        // ─────────────────────────────────────────────────────────────────────
        // 방어 행동 확률 (원본 유지)
        // ─────────────────────────────────────────────────────────────────────
        [Header("Defense Tactics")]
        [Range(0, 100)] public float baseParryChance = 20f;
        [Range(0, 100)] public float baseBlockChance = 30f;
        [Range(0, 100)] public float baseEvadeChance = 20f;
        public float defensiveActionCooldown = 2f;
        private float nextDefensiveActionTime;

        // ─────────────────────────────────────────────────────────────────────
        // ① 스트레이핑 (수정본 추가 및 원본 병합)
        // ─────────────────────────────────────────────────────────────────────
        [Header("Strafe Settings (P0 ①)")]
        [Tooltip("스트레이핑 이동 속도 — navMeshAgent.speed 에 직접 설정")]
        public float strafeSpeed = 2.0f;
        [Tooltip("방향 전환 최소 간격(초)")]
        public float strafeDirectionChangeMin = 2.0f;
        [Tooltip("방향 전환 최대 간격(초)")]
        public float strafeDirectionChangeMax = 5.0f;
        [Tooltip("타겟으로부터 유지할 선호 궤도 반경(m)")]
        public float strafeOrbitRadius = 3.5f;
        [Tooltip("방향 전환 시간(원본 호환용)")]
        public float strafeDirectionChangeTime = 3f;

        private float nextStrafeChangeTime;
        private float strafeDirectionTimer = 0f;
        private int strafeDirection = 1;

        // [디버깅] 로깅 쿨타임용 변수
        private float debugScanTimer = 0f;

        // =====================================================================
        // Awake
        // =====================================================================
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
                    float currentViewableAngle = Vector3.SignedAngle(transform.forward, directionToTarget.normalized, Vector3.up);

                    // 지정된 시야각(minimumDetectionAngle ~ maximumDetectionAngle) 내에 들어왔는지 체크
                    if (currentViewableAngle >= minimumDetectionAngle && currentViewableAngle <= maximumDetectionAngle)
                    {
                        float dist = Vector3.Distance(transform.position, otherCharacter.transform.position);

                        // 탐지 로그는 일반적인 DebugLog의 showDebugLogs 조건과 별개로, showFOVScanLogs에 종속되도록 바로 출력
                        Debug.Log($"<color=#32CD32>[{gameObject.name}]</color> 👁️ 전방 {distance}m 시야 내 포착: <color=yellow>{otherCharacter.gameObject.name}</color> (거리: {dist:F1}m, 각도: {currentViewableAngle:F1}도)");
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

        // =====================================================================
        // 공격 쿨다운 헬퍼 (수정본 편의기능)
        // =====================================================================
        public void HandleAttackTimer()
        {
            if (currentAttackTimer > 0f)
                currentAttackTimer -= Time.deltaTime;
        }

        public bool CanAttack() => currentAttackTimer <= 0f;

        public void SetAttackCooldown()
        {
            currentAttackTimer = Random.Range(minimumTimeBetweenAttacks, maximumTimeBetweenAttacks);
        }

        // =====================================================================
        // ① HandleStrafingAroundTarget
        //
        //  [제거] UpdateAnimatorMovementParameters(...) 직접 호출
        //  [추가] navMeshAgent.speed 설정 + navMeshAgent.SetDestination
        //
        //  Animator 파라미터(horizontalMovement 등)는
        //  AICharacterLocomotionManager.SyncAnimatorParameters()가
        //  navMeshAgent.velocity 를 읽어 매 프레임 자동으로 갱신합니다.
        // =====================================================================
        public void HandleStrafingAroundTarget()
        {
            if (currentTarget == null) return;
            if (!aiCharacter.IsServer) return;
            if (!aiCharacter.canMove) return;
            if (aiCharacter.isPerformingAction) return;

            bool isOneOnOne = !HasAlliesTargetingSame();

            if (!isOneOnOne && aiIntelligenceLevel >= 7)
            {
                CalculateFlankingDirection(); // 원본 포위망 로직 유지
            }
            else
            {
                // 방향 전환 타이머 (수정본)
                strafeDirectionTimer -= Time.deltaTime;
                if (strafeDirectionTimer <= 0f)
                {
                    strafeDirection = (Random.value > 0.5f) ? 1 : -1;
                    strafeDirectionTimer = Random.Range(strafeDirectionChangeMin, strafeDirectionChangeMax);
                    nextStrafeChangeTime = Time.time + Random.Range(2f, strafeDirectionChangeTime); // 원본 호환
                }
            }

            // NavMeshAgent 속도 설정 (Animator 직접 조작 완전 금지)
            aiCharacter.navMeshAgent.speed = strafeSpeed;

            // 타겟 주변 원호 궤적 목적지 계산
            Vector3 toTarget = (currentTarget.transform.position - aiCharacter.transform.position).normalized;
            Vector3 perpDir = Vector3.Cross(Vector3.up, toTarget) * strafeDirection;

            // 타겟 중심 선호 궤도 + 측면 이동
            Vector3 idealOrbitPos = currentTarget.transform.position - toTarget * strafeOrbitRadius;
            Vector3 strafeDest = idealOrbitPos + perpDir * (strafeSpeed * Time.deltaTime * 8f);

            // NavMesh 위로 보정 후 목적지 설정
            if (NavMesh.SamplePosition(strafeDest, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
                {
                    aiCharacter.navMeshAgent.SetDestination(hit.position);
                }
            }

            // 타겟 바라보기
            Quaternion targetRotation = Quaternion.LookRotation(toTarget);
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

        // =====================================================================
        // HandleDefensiveActions — combatLevel 기반 확률 선택
        // ※ 기본값(= 0f)을 주어 RetreatState에서 인자없이 호출할 때 나는 에러 해결
        // =====================================================================
        public void HandleDefensiveActions(float distanceFromAttacker = 0f)
        {
            if (currentTarget == null) return;
            if (!aiCharacter.IsServer) return;
            if (aiCharacter.isPerformingAction) return;

            if (currentTarget.isPerformingAction)
            {
                if (Time.time < nextDefensiveActionTime) return;

                float levelModifier = combatLevel / 10f;

                // 수정본: combatLevel 에 따른 방어 발동 확률
                float prob = combatLevel switch
                {
                    <= 1 => 0.10f,
                    2 => 0.25f,
                    3 => 0.40f,
                    4 => 0.55f,
                    _ => 0.70f
                };

                if (Random.value > prob) return;

                float roll = Random.value;
                if (roll < 0.30f && combatLevel >= 3) PerformParry();
                else if (roll < 0.60f && combatLevel >= 2) PerformEvade();
                else PerformBlock();

                float actualCooldown = defensiveActionCooldown * (1f + (1f - levelModifier));
                nextDefensiveActionTime = Time.time + actualCooldown;
            }
        }

        // =====================================================================
        // ② PerformEvade
        // PlayTargetActionFunnel 이 올바른 Animator 상태를 크로스페이드하고
        // IFrameEnable / Disable 애니메이션 이벤트를 자동으로 처리합니다.
        // =====================================================================
        private void PerformEvade()
        {
            if (currentTarget == null) return;

            // 공격자 진행 방향 기준 위빙 방향 결정
            Vector3 attackerForward = currentTarget.transform.forward;
            float dotRight = Vector3.Dot(aiCharacter.transform.right, attackerForward);

            int weaveActionID;
            if (Random.value < 0.6f)
            {
                // 공격자 오른쪽이면 AI 기준 왼쪽으로, 반대는 오른쪽으로 빠짐
                weaveActionID = (dotRight > 0f)
                    ? (int)ActionID.Roll_Forward   // 우측 위빙
                    : (int)ActionID.Back_Step;      // 좌측 위빙
            }
            else
            {
                weaveActionID = (int)ActionID.Back_Step;
            }

            // ② Funnel 호출. (isPerformingAction 파라미터 에러 회피를 위해 명명된 인수 대신 위치 매개변수 사용)
            aiCharacter.characterAnimationManager.PlayTargetActionFunnel(weaveActionID, true, false);

            DebugLog($"[PerformEvade] ActionID:{weaveActionID}");
        }

        // =====================================================================
        // ③ PerformParry
        // CharacterDefenseManager.StartDefense 로 패링 창 오픈 후 Funnel 호출
        // =====================================================================
        private void PerformParry()
        {
            // ③ 패링 타이밍 창 오픈 (CloseGrip = 패링 모드)
            if (aiCharacter.characterDefenseManager != null
                && aiCharacter.characterDefenseManager.currentDefendingItem != null)
            {
                aiCharacter.characterDefenseManager.StartDefense(
                    GuardDirection.Top,
                    ShieldStance.CloseGrip);
            }

            // 패링 모션 (위치 매개변수 사용)
            aiCharacter.characterAnimationManager.PlayTargetActionFunnel((int)ActionID.Parry_Counter, true, true);

            DebugLog("[PerformParry] 패링 타이밍 창 오픈 (CloseGrip)");
        }

        // =====================================================================
        // ④ PerformBlock
        // =====================================================================
        private void PerformBlock()
        {
            // ④ 방어 자세 시작 (FarGrip = 일반 방어)
            if (aiCharacter.characterDefenseManager != null
                && aiCharacter.characterDefenseManager.currentDefendingItem != null)
            {
                aiCharacter.characterDefenseManager.StartDefense(
                    GuardDirection.Top,
                    ShieldStance.FarGrip);
            }

            // [컴파일 에러 조치] ActionID에 Guard_01이 아직 등록되지 않아 발생하는 에러(CS0117)를 회피하기 위해, 
            // 원본에서 사용하던 안전한 방식인 Animator.StringToHash 를 사용하여 방어 애니메이션 재생을 보장합니다.
            aiCharacter.characterAnimationManager.PlayTargetAnimation(Animator.StringToHash("Block_Start"), false, true);

            DebugLog("[PerformBlock] 방어 자세 시작 (FarGrip)");
        }

        // =====================================================================
        // 타겟 설정 / 해제 (상위 FSM State 에서 사용)
        // =====================================================================
        public void ClearTarget()
        {
            currentTarget = null;
        }

        // =========================================================================================
        // [디버그] 씬 뷰 시각화 (기즈모 및 텍스트 렌더링) (원본 유지 기능)
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