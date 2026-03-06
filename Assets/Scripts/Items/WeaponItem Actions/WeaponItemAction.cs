using UnityEngine;
using TDA.Character.Player;

namespace TDA.Items.WeaponItemActions
{
    /// <summary>
    /// [L3 Domain] 각 무기 고유의 능력치와 액션 발동 메커니즘을 정의하는 핵심 데이터 캡슐(SO)입니다.
    /// </summary>
    public class WeaponItemAction : ScriptableObject
    {
        [Header("Action Network Sync")]
        [Tooltip("멀티플레이어 환경에서 이 액션을 식별하기 위한 고유 ID입니다. (WorldActionManager 참조용)")]
        // [에러 수정 CS1061] WorldActionManager와 PlayerCombatManager에서 요구하던 actionID 필드 추가
        public int actionID;

        [Header("Combat Mechanics")]
        [Tooltip("에임 어시스트 발동 시 투사되는 스피어캐스트(SphereCast)의 두께(반경)입니다. 넓을수록 타겟을 후하게 판정합니다.")]
        public float sphereCastRadius = 0.5f;

        [Tooltip("이 액션의 유효 타격 사거리입니다. (P3 모션 워핑 시 클리핑을 방지하는 거리 오프셋으로 사용됩니다)")]
        // PlayerCombatManager의 FindBestWarpTarget에서 사용되는 공격 사거리 변수도 함께 추가해 두었습니다.
        public float weaponAttackRange = 1.5f;

        /// <summary>
        /// 캐릭터가 해당 무기 액션을 실행하려 할 때 호출되는 베이스 가상 메서드입니다.
        /// </summary>
        public virtual void AttemptToPerformAction(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
        {
            // 베이스 클래스에서는 기본 로직만 유지하거나 비워둡니다.
            // LightAttackAction, HeavyAttackAction 등에서 이를 override 하여 사용합니다.
        }
    }
}