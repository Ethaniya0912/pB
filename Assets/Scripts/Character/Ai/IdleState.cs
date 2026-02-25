using UnityEngine;

/// <summary>
/// 대기 상태 (Idle State)
/// 단일 책임: 주변에 플레이어(타겟)가 있는지 탐지하고, 발견 시 추적 상태로 전환합니다.
/// </summary>
[CreateAssetMenu(menuName = "AI/States/Idle")]
public class IdleState : AIState
{
    [Header("Next State")]
    public PursueTargetState pursueTargetState;

    [Header("Detection Settings")]
    public LayerMask detectionLayer;
    public float detectionRadius = 15f;
    public float minimumDetectionAngle = -50f;
    public float maximumDetectionAngle = 50f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        // 1. 타겟 탐지 로직 (OverlapSphere 사용)
        Collider[] colliders = Physics.OverlapSphere(aiCharacter.transform.position, detectionRadius, detectionLayer);

        foreach (var collider in colliders)
        {
            CharacterManager targetCharacter = collider.transform.GetComponent<CharacterManager>();

            if (targetCharacter != null)
            {
                // 타겟을 향한 방향 벡터 계산
                Vector3 targetDirection = targetCharacter.transform.position - aiCharacter.transform.position;
                float viewableAngle = Vector3.Angle(targetDirection, aiCharacter.transform.forward);

                // 타겟이 시야각 내에 있는지 확인
                if (viewableAngle > minimumDetectionAngle && viewableAngle < maximumDetectionAngle)
                {
                    // 2. 타겟 설정 후 추적 상태로 전환
                    aiCharacter.aiCharacterCombatManager.currentTarget = targetCharacter;
                    return pursueTargetState;
                }
            }
        }

        // 타겟을 찾지 못했다면 현재 상태 유지
        return this;
    }
}