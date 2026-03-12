using UnityEngine;

namespace TDA.Core.Events
{
    /// <summary>
    /// [LData] 애니메이터 파라미터 문자열을 캐싱하는 전역 규약 정적(Static) 클래스입니다.
    /// 매 프레임 호출되는 Update 문 내부나 수많은 상태 전이 과정에서 
    /// 문자열(String) 할당으로 인한 가비지 컬렉터(GC) 스파이크를 원천 차단합니다.
    /// 기획된 애니메이터 컨트롤러의 파라미터명과 1:1로 정확히 일치해야 합니다.
    /// </summary>
    public static class AnimatorParameterHash
    {
        // =================================================================================
        // [이동 및 로코모션 (Locomotion)]
        // =================================================================================
        /// <summary>좌/우 게걸음 이동량 (Blend Tree 제어용)</summary>
        public static readonly int Horizontal = Animator.StringToHash("Horizontal");

        /// <summary>전/후 전진 이동량 (Blend Tree 제어용)</summary>
        public static readonly int Vertical = Animator.StringToHash("Vertical");

        /// <summary>절대값 기반 이동량 합산 (걷기/달리기 구간 스냅핑 및 속도 계산용)</summary>
        public static readonly int moveAmount = Animator.StringToHash("moveAmount");

        /// <summary>자유 이동 시의 전체 속도량 (0~1~2)</summary>
        public static readonly int Speed = Animator.StringToHash("Speed");

        /// <summary>수직 상승/하락 가속도 (공중 체공/낙하 애니메이션 제어용)</summary>
        public static readonly int verticalVelocity = Animator.StringToHash("verticalVelocity");

        public static readonly int isMoving = Animator.StringToHash("isMoving");
        public static readonly int isStrafing = Animator.StringToHash("isStrafing");
        public static readonly int isSprinting = Animator.StringToHash("isSprinting");

        public static readonly int isJumping = Animator.StringToHash("isJumping");
        public static readonly int isGrounded = Animator.StringToHash("isGrounded");
        public static readonly int isFalling = Animator.StringToHash("isFalling");

        // =================================================================================
        // [전투 및 상태 (Combat & States)]
        // =================================================================================
        public static readonly int isAttacking = Animator.StringToHash("isAttacking");
        public static readonly int isDefending = Animator.StringToHash("isDefending");
        public static readonly int isHit = Animator.StringToHash("isHit");
        public static readonly int isLockedOn = Animator.StringToHash("isLockedOn");
        public static readonly int isChargingAttack = Animator.StringToHash("isChargingAttack");

        // =================================================================================
        // [트리거 및 정수형 제어 (Triggers & Ints)]
        // =================================================================================
        /// <summary>공격의 방향이나 모션 종류를 세부적으로 가르는 식별자</summary>
        public static readonly int ActionState = Animator.StringToHash("ActionState");

        /// <summary>장착한 무기의 파지법 스왑용 식별자 (Q키 쉴드 스위칭 포함)</summary>
        public static readonly int WeaponType = Animator.StringToHash("WeaponType");

        /// <summary>신규 구조: 플레이어의 의지가 담긴 능동적 액션 트리거 (Funnel 연동)</summary>
        public static readonly int onAction = Animator.StringToHash("onAction");

        /// <summary>신규 구조: 강제력에 의해 우선순위가 덮어씌워지는 수동적 피격 트리거 (Funnel 연동)</summary>
        public static readonly int onHit = Animator.StringToHash("onHit");

        // 레거시 호환용 (추후 완전히 onAction, onHit으로 대체되면 삭제 가능)
        public static readonly int DoRoll = Animator.StringToHash("DoRoll");
        public static readonly int DoAttack = Animator.StringToHash("DoAttack");
        public static readonly int DoJump = Animator.StringToHash("DoJump");
        public static readonly int TakeDamage = Animator.StringToHash("TakeDamage");
    }
}