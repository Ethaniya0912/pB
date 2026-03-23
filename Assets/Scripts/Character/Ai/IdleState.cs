using UnityEngine;
using TDA.Character.AI;

/// <summary>
/// 대기 상태 (Idle State)
/// 단일 책임: 주변에 플레이어(타겟)가 있는지 탐지하고, 발견 시 추적 상태로 전환합니다.
/// </summary>
[CreateAssetMenu(menuName = "AI/States/Idle")]
public class IdleState : AIState
{
    [Header("Next State")]
    public PursueTargetState pursueTargetState;

    // 디버그용 타이머
    private float debugTimer = 0f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        // =========================================================================================
        // [신규 확장] 루트 모션(Root Motion) 정합성을 위한 이동 강제 정지
        // AICharacterLocomotionManager가 애니메이터를 통해 이동을 제어하므로, 대기 상태 진입 시
        // NavMeshAgent에 남아있는 찌꺼기 경로를 완벽히 지워야 제자리에 멈추고 발 미끄러짐이 발생하지 않습니다.
        // =========================================================================================
        if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.hasPath)
        {
            aiCharacter.navMeshAgent.ResetPath();
        }

        /*
        // =========================================================================================
        // [레거시 코드 명시 - 수동 Transform 제어 로직 비활성화]
        // 
        // [과거 로직]
        // 과거에는 대기 상태(Idle)나 타겟을 응시할 때 아래와 같이 스크립트로 직접 캐릭터를 회전시켰을 수 있습니다.
        // Vector3 targetDir = targetPos - transform.position;
        // transform.rotation = Quaternion.LookRotation(targetDir);
        // 
        // [레거시가 된 이유 (Why Legacy?)]
        // 현재 이동 및 회전의 통제권은 전적으로 'AICharacterLocomotionManager'와 'Animator(Root Motion)'에 있습니다.
        // State 스크립트 내부에서 transform.rotation이나 position을 직접 강제로 조작하게 되면,
        // 애니메이터가 계산하는 회전/보폭 값과 충돌을 일으켜 캐릭터가 덜덜 떨리거나(Jittering)
        // 스케이트를 타듯 땅을 미끄러지는(Foot Sliding) 심각한 물리 버그가 발생합니다.
        // 따라서 물리적 개입 코드는 모두 제거/주석 처리하고, State는 오직 '판단'만 수행하도록 개편되었습니다.
        // =========================================================================================
        */

        // 2초마다 생존 신고 및 탐지 결과 로그 띄우기
        debugTimer += Time.deltaTime;
        bool shouldLog = false;

        if (debugTimer > 2.0f)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("👀 제자리 대기 중... 주변에 플레이어가 있는지 탐색하고 있습니다.");
            shouldLog = true;
            debugTimer = 0f;
        }

        float radius = aiCharacter.aiCharacterCombatManager.detectionRadius;
        float maxAngle = aiCharacter.aiCharacterCombatManager.maximumDetectionAngle;

        LayerMask characterLayer = WorldUtilityManager.Instance.GetCharacterLayers();

        // 1. 타겟 탐지 로직 (OverlapSphere 사용)
        Collider[] colliders = Physics.OverlapSphere(aiCharacter.transform.position, radius, characterLayer);

        for (int i = 0; i < colliders.Length; i++)
        {
            CharacterManager targetCharacter = colliders[i].transform.GetComponent<CharacterManager>();

            // 타겟이 본인이 아니며, 죽지 않은 유효한 대상일 때만 검사
            if (targetCharacter != null && targetCharacter != aiCharacter && !targetCharacter.characterNetworkManager.isDead.Value)
            {
                // [개선] 상하(Y축) 높이 차이 때문에 시야각에서 벗어나는 문제를 막기 위해 XZ 평면(바닥) 기준으로만 각도를 계산합니다.
                Vector3 aiPosXZ = new Vector3(aiCharacter.transform.position.x, 0, aiCharacter.transform.position.z);
                Vector3 targetPosXZ = new Vector3(targetCharacter.transform.position.x, 0, targetCharacter.transform.position.z);

                Vector3 targetDirection = targetPosXZ - aiPosXZ;
                Vector3 aiForwardXZ = new Vector3(aiCharacter.transform.forward.x, 0, aiCharacter.transform.forward.z).normalized;

                float viewableAngle = Vector3.Angle(targetDirection.normalized, aiForwardXZ);

                if (shouldLog)
                {
                    aiCharacter.aiCharacterCombatManager.DebugLog($"[탐지 분석] 타겟 '{targetCharacter.gameObject.name}' 포착. 거리: {Vector3.Distance(aiPosXZ, targetPosXZ):F1}m, 시야각: {viewableAngle:F1}도 (한계선: {maxAngle}도)");
                }

                // 타겟이 시야각(원뿔 형태) 내에 있는지 확인
                if (viewableAngle <= maxAngle)
                {
                    // 2. 타겟 설정 후 추적 상태로 전환
                    aiCharacter.aiCharacterCombatManager.DebugLog($"🎯 타겟 조준 완료! ({targetCharacter.gameObject.name}) 추적을 시작합니다.");
                    aiCharacter.aiCharacterCombatManager.SetTarget(targetCharacter);
                    return SwitchState(aiCharacter, pursueTargetState);
                }
            }
        }

        return this; // 타겟을 찾지 못하면 계속 대기 상태 유지
    }
}