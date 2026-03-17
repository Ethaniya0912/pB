using UnityEngine;

namespace TDA.Core.Events
{
    /// <summary>
    /// [Data Layer] 애니메이터 파라미터 해시(Hash) 중앙 관리자
    /// 
    /// 문자열 하드코딩으로 인한 가비지 컬렉션(GC) 할당과 오타 버그(Silent Failure)를 
    /// 원천 차단하기 위해, 게임 실행 시 단 1회만 정적(Static)으로 해싱하여 메모리에 상주시키는 클래스입니다.
    /// </summary>
    public static class AnimatorParameterHash
    {
        // =========================================================================================
        // 🚨 주의: 괄호 안의 "문자열"이 유니티 애니메이터 창의 Parameters 이름과 대소문자, 공백까지 100% 일치해야 합니다!
        // =========================================================================================

        // [이동 및 로코모션 (Locomotion)]
        public static readonly int Horizontal = Animator.StringToHash("Horizontal");
        public static readonly int Vertical = Animator.StringToHash("Vertical");
        public static readonly int moveAmount = Animator.StringToHash("moveAmount");

        // [시선 및 턴 (IK & Turn)]
        public static readonly int turnAngle = Animator.StringToHash("turnAngle");

        // [액션 트리거 (Action & Combat)]
        public static readonly int onAction = Animator.StringToHash("onAction");
        public static readonly int ActionState = Animator.StringToHash("ActionState");

        // [차징 및 제스처 (Charging)]
        public static readonly int isCharging = Animator.StringToHash("isCharging");
        public static readonly int ChargeRatio = Animator.StringToHash("ChargeRatio");

        // [피격 및 리액션 (Hit & Reaction)]
        public static readonly int onHit = Animator.StringToHash("onHit");

        // [상태 판별 (State Flags)]
        public static readonly int isLockedOn = Animator.StringToHash("isLockedOn");
        public static readonly int isGrounded = Animator.StringToHash("isGrounded");
        public static readonly int verticalVelocity = Animator.StringToHash("verticalVelocity");

        // [🚨 추가된 누락 파라미터]
        public static readonly int isStrafing = Animator.StringToHash("isStrafing");
        public static readonly int isMoving = Animator.StringToHash("isMoving"); // 혹시 모를 레거시 호환용

        // [방어 시스템 (Defense)]
        public static readonly int isDefending = Animator.StringToHash("isDefending");
        public static readonly int guardStyle = Animator.StringToHash("guardStyle");
    }
}