using System.Collections;
using System.Collections.Generic;
using TDA.Character.AI;
using UnityEngine;
using UnityEngine.AI;

public class AICharacterManager : CharacterManager
{
    public AICharacterCombatManager aiCharacterCombatManager;

    [Header("Navmesh Agent")]
    public NavMeshAgent navMeshAgent;

    [Header("Locomotion Manager (Extension)")]
    // [신규 확장] 물리적 이동과 애니메이션 동기화를 담당할 중개자
    public AICharacterLocomotionManager aiCharacterLocomotionManager { get; private set; }

    [Header("State Machine")]
    [SerializeField] private AIState initialState; // 시작할 때 진입할 초기 상태 (인스펙터에서 IdleState 할당)
    [SerializeField] private AIState currentState; // 현재 진행 중인 상태

    protected override void Awake()
    {
        base.Awake();

        aiCharacterCombatManager = GetComponent<AICharacterCombatManager>();
        navMeshAgent = GetComponentInChildren<NavMeshAgent>();

        // [신규 확장] 로코모션 매니저 캐싱 및 동적 부착
        aiCharacterLocomotionManager = GetComponent<AICharacterLocomotionManager>();
        if (aiCharacterLocomotionManager == null)
        {
            aiCharacterLocomotionManager = gameObject.AddComponent<AICharacterLocomotionManager>();
        }

        // [신규 확장 핵심] 루트 모션 사용을 위해 NavMeshAgent의 강제 트랜스폼 제어권을 뺏어옵니다.
        if (navMeshAgent != null)
        {
            navMeshAgent.updatePosition = false;
            navMeshAgent.updateRotation = false;
        }

        // =========================================================================================
        // 🚨 [치명적 버그 수정] 스태미나 리젠 고장 원인 해결
        // 부모 클래스(CharacterManager)에서 characterStatsManager가 할당되지 않아 
        // Update() 문의 리젠 코드가 무시(Null)되고 있었습니다. 여기서 명시적으로 연결해 줍니다!
        // =========================================================================================
        if (characterStatsManager == null)
        {
            characterStatsManager = GetComponent<AICharacterStatsManager>();
        }

        // 개선됨: 더 이상 SO(ScriptableObject) 상태 에셋들을 Instantiate로 복사하지 않습니다.
        // 상태 클래스는 '로직'만 처리하고 '데이터(타겟, 시간 등)'는 AICharacterManager와
        // AICharacterCombatManager에서 들고 있으므로 메모리를 크게 절약할 수 있습니다.
    }

    protected override void Start()
    {
        base.Start();

        // 씬 시작 시 초기 상태 지정
        if (initialState != null)
        {
            currentState = initialState;
        }
    }

    protected override void Update()
    {
        base.Update();

        // 개선됨: AI의 시야 탐지, 회전, 애니메이션 등은 물리(FixedUpdate) 프레임보다
        // 렌더링(Update) 프레임에서 실행하는 것이 프레임 낭비 없이 훨씬 부드럽게 동작합니다.
        ProcessStateMachine();

        // =========================================================================================
        // [버그 수정: 스태미나 리젠 추가]
        // AI 몬스터가 뛸 때 소모한 스태미나가 다시 차오르도록 매 프레임 재생 로직을 호출합니다.
        // 이 코드가 있어야 PursueTargetState에서 멈춘 뒤 다시 달릴 수 있습니다.
        // =========================================================================================
        if (characterStatsManager != null)
        {
            characterStatsManager.RegenerateStamina();
        }

        // [신규 확장] 판단(상태 머신)과 스태미나 처리가 끝난 후, 최종적으로 루트모션 기반 이동을 실행합니다.
        if (aiCharacterLocomotionManager != null)
        {
            aiCharacterLocomotionManager.HandleAILocomotion();
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        // 물리 기반 이동이나 연산이 필요한 경우 여기에 작성합니다.
    }

    /// <summary>
    /// 현재 상태(currentState)의 Tick을 실행하고, 
    /// 반환된 결과에 따라 다음 상태로 전환합니다.
    /// </summary>
    private void ProcessStateMachine()
    {
        // 1. 현재 상태가 없다면 실행하지 않음
        if (currentState == null) return;

        // =========================================================================================
        // 🚨 [팀킬(동족상잔) 원천 차단 가드] 
        // 탐색 로직에서 피아 식별이 누락되어 같은 팀을 타겟으로 잡는 경우,
        // 상태 머신이 돌기 전에 중앙에서 강제로 타겟을 해제하여 춤추듯 빙글빙글 도는 버그를 막습니다.
        // =========================================================================================
        if (aiCharacterCombatManager != null && aiCharacterCombatManager.currentTarget != null)
        {
            // 내 타겟의 그룹과 내 그룹이 같다면? (아군이라면)
            if (aiCharacterCombatManager.currentTarget.characterGroup == this.characterGroup)
            {
                Debug.Log($"<color=orange>[피아 식별]</color> {gameObject.name}이(가) 아군({this.characterGroup})인 {aiCharacterCombatManager.currentTarget.name}을(를) 타겟으로 잡았습니다. 즉시 타겟을 해제합니다.");

                // 타겟을 강제로 풀어버립니다.
                aiCharacterCombatManager.SetTarget(null);

                // 타겟이 없어졌으므로 이번 프레임의 상태 머신 처리는 건너뜁니다. (다음 프레임에 Idle/Patrol 로직이 자연스레 타겟 재탐색)
                return;
            }
        }

        // 2. 현재 상태의 로직(Tick)을 실행하고 다음에 진행할 상태를 받아옴 (단일 매개변수 구조 유지)
        AIState nextState = currentState.Tick(this);

        // 3. 반환받은 상태가 기존 상태와 다르다면 상태를 교체(Transition)
        if (nextState != null && nextState != currentState)
        {
            // =========================================================================================
            // [디버깅 로그 필터링] 플레이어(카메라) 주변 15m 반경 내에 있는 몹의 상태 전환만 콘솔에 출력합니다.
            // =========================================================================================
            if (Camera.main != null)
            {
                float distToPlayer = Vector3.Distance(transform.position, Camera.main.transform.position);

                // 내 주변 15m 이내의 몹들만 상태 로그를 출력 (거리 수치는 입맛에 맞게 조절하세요)
                if (distToPlayer <= 15f)
                {
                    Debug.Log($"<color=magenta><b>[AI State Machine]</b></color> <color=yellow><b>[{gameObject.name}]</b></color> 상태 전환: <color=white>{currentState.name}</color> ➔ <color=cyan>{nextState.name}</color>");
                }
            }
            else
            {
                // 씬에 카메라가 없는 초기화 찰나 등에는 무조건 출력
                Debug.Log($"<color=magenta><b>[AI State Machine]</b></color> <color=yellow><b>[{gameObject.name}]</b></color> 상태 전환: <color=white>{currentState.name}</color> ➔ <color=cyan>{nextState.name}</color>");
            }

            currentState = nextState;
        }
    }
}