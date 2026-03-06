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

    [Header("State Machine")]
    [SerializeField] private AIState initialState; // 시작할 때 진입할 초기 상태 (인스펙터에서 IdleState 할당)
    [SerializeField] private AIState currentState; // 현재 진행 중인 상태

    protected override void Awake()
    {
        base.Awake();

        aiCharacterCombatManager = GetComponent<AICharacterCombatManager>();
        navMeshAgent = GetComponentInChildren<NavMeshAgent>();

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

        // 2. 현재 상태의 로직(Tick)을 실행하고 다음에 진행할 상태를 받아옴
        AIState nextState = currentState.Tick(this);

        // 3. 반환받은 상태가 기존 상태와 다르다면 상태를 교체(Transition)
        if (nextState != null && nextState != currentState)
        {
            currentState = nextState;
        }
    }
}