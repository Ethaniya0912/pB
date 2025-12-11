using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AICharacterManager : CharacterManager
{
    public AICharacterCombatManager aiCharacterCombatManager;

    [Header("Current State")]
    [SerializeField] AIState currentState;

    [Header("Navmesh Agent")]
    public NavMeshAgent navMeshAgent;

    [Header("States")]
    public IdleState idle;
    public PursueTargetState pursueTarget;
    // 컴뱃스테이트
    // 공격

    protected override void Awake()
    {
        base.Awake();

        aiCharacterCombatManager = GetComponent<AICharacterCombatManager>();

        navMeshAgent = GetComponentInChildren<NavMeshAgent>();

        // SO의 카피를 써서 오리지날을 그대로 두기위함.
        idle = Instantiate(idle);
        pursueTarget = Instantiate(pursueTarget);

        currentState = idle;
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        ProcessStateMachine();
    }

    private void ProcessStateMachine()
    {
        AIState nextState = null;

        if (currentState != null)
        {
            nextState = currentState.Tick(this);
        }

        if (nextState != null)
        {
            currentState = nextState;
        }
    }

    // 옵션 2 : 옵션 1 간략화 버전.
    //private void ProcessStateMachine()
    //{
    //    AIState nextState = currentState?.Tick(this)
    //
    //    if (nextState != null)
    //    {
    //        currentState = nextState;
    //    }
    //}

}
