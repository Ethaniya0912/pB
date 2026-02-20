using UnityEngine;

/// <summary>
/// [Base Class] 모든 캐릭터(플레이어, AI)의 시각적 피드백을 계산하는 기초 클래스입니다.
/// </summary>
public class CharacterVisualFeedbackManager : MonoBehaviour
{
    protected CharacterManager character;

    [Header("Base Feedback Settings")]
    public float maxReferenceSpeed = 10f;
    protected float currentSpeedFactor; // 0 ~ 1 정규화된 속도값

    protected virtual void Awake()
    {
        character = GetComponent<CharacterManager>();
    }

    protected virtual void Update()
    {
        CalculateBaseSpeedFactor();
    }

    protected virtual void CalculateBaseSpeedFactor()
    {
        if (character == null) return;

        // 리지드바디 또는 이동 컴포넌트에서 실제 물리 속도 추출
        float velocity = character.characterController != null ?
                         character.characterController.velocity.magnitude : 0f;

        currentSpeedFactor = Mathf.Clamp01(velocity / maxReferenceSpeed);
    }
}