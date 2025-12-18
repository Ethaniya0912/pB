using UnityEngine;
using SG;

[CreateAssetMenu(menuName = "Character Actions/Weapon Actions/Slicing Action")]
public class SlicingWeaponItemAction : WeaponItemAction
{
    [Header("Slicing Settings")]
    [Tooltip("자르기 공격에 사용할 애니메이션 이름")]
    public string sliceAnimation = "Light_Attack_01"; // 예시 애니메이션

    public override void AttemptToPerformAction(PlayerManager playerPerformingAction, WeaponItem weaponItem)
    {
        // 1. 기본 액션 실행 (스태미나 소모 등)
        base.AttemptToPerformAction(playerPerformingAction, weaponItem);

        // 2. 무기 타입 체크 (Dev A의 SliceableObject 요구사항과 매칭)
        // 여기서는 간단히 무기 아이템이 존재하고 근접 무기인지 확인
        if (weaponItem == null) return;

        // 3. 애니메이션 실행
        // 애니메이션 이벤트에서 Collider를 켜는 방식은 기존 시스템을 따름
        // 수정: CharacterAnimationManager에 정의된 올바른 메서드 PlayTargetAnimation 사용
        playerPerformingAction.characterAnimationManager.PlayTargetAnimation(sliceAnimation, true);

        // *참고: 실제 Slicing 판정은 애니메이션 도중 켜지는 SlicingDamageCollider에서 발생합니다.
    }
}