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

    // 디버그용 타이머
    private float debugTimer = 0f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
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

        // [트러블슈팅] 콜라이더 자체가 안 잡히는 경우 (레이어 세팅 문제)
        if (shouldLog && colliders.Length == 0)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog($"[탐지 실패] 반경 {radius}m 내에 '{characterLayer.value}' 레이어를 가진 오브젝트가 없습니다. (플레이어 레이어 확인 요망)");
        }

        foreach (var collider in colliders)
        {
            // [개선] 콜라이더가 자식 객체에 있을 경우를 대비해 GetComponentInParent 사용
            CharacterManager targetCharacter = collider.transform.GetComponentInParent<CharacterManager>();

            // [에러 수정] CharacterManager에는 isDead가 직접 존재하지 않고, CharacterNetworkManager 내부에 정의되어 있으므로 접근 경로를 수정했습니다.
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
                    aiCharacter.aiCharacterCombatManager.currentTarget = targetCharacter;
                    return SwitchState(aiCharacter, pursueTargetState);
                }
            }
        }

        // 타겟을 찾지 못했다면 현재 상태 유지
        return this;
    }
}