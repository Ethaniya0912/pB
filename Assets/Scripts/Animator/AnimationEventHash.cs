using UnityEngine;

namespace TDA.Core.Events
{
    /// <summary>
    /// [P1] 애니메이션 통신 시스템의 데이터 식별자(Identifier) 규약 클래스입니다.
    /// Enum 대신 Animator.StringToHash를 사용하여 Git Merge Conflict를 원천 차단하고,
    /// 런타임 통신 시 무거운 문자열 대신 정수(int)를 사용하여 가비지 컬렉션(GC) 할당 없이 최고 수준의 퍼포먼스를 보장합니다.
    /// partial 클래스로 선언되어 향후 사운드/이펙트 등 파일 분할 확장이 용이합니다.
    /// </summary>
    public static partial class AnimationEventHash
    {
        // =================================================================================
        // [카테고리 1: 전투 및 시스템 흐름 제어 (Logic & Flow)]
        // =================================================================================

        /// <summary>다음 콤보 입력을 허용하는 창(Window) 개방</summary>
        public static readonly int ComboEnable = Animator.StringToHash("ComboEnable");

        /// <summary>콤보 입력 창 강제 종료</summary>
        public static readonly int ComboDisable = Animator.StringToHash("ComboDisable");

        /// <summary>무기 공격 판정 콜라이더 활성화</summary>
        public static readonly int HitBoxEnable = Animator.StringToHash("HitBoxEnable");

        /// <summary>무기 공격 판정 콜라이더 비활성화</summary>
        public static readonly int HitBoxDisable = Animator.StringToHash("HitBoxDisable");

        /// <summary>회피 무적 프레임(Invincibility Frame) 시작</summary>
        public static readonly int IFrameEnable = Animator.StringToHash("IFrameEnable");

        /// <summary>무적 프레임 종료</summary>
        public static readonly int IFrameDisable = Animator.StringToHash("IFrameDisable");

        /// <summary>[P1] 체인 로직에서 CombatManager가 데미지 연산을 끝낸 후 재발송하는 2차 이벤트</summary>
        public static readonly int Damage_Calculated = Animator.StringToHash("Damage_Calculated");

        // 🔥 [추가됨] 애니메이션 종료 시 플래그 초기화를 위한 해시
        public static readonly int Action_Ended = Animator.StringToHash("Action_Ended");

        // =================================================================================
        // [카테고리 2: 오디오 피드백 (Audio / SFX)]
        // =================================================================================

        /// <summary>가벼운 무기 스윙 사운드</summary>
        public static readonly int PlaySound_WeaponSwing_Light = Animator.StringToHash("PlaySound_WeaponSwing_Light");

        /// <summary>무거운 대검 류 스윙 사운드</summary>
        public static readonly int PlaySound_WeaponSwing_Heavy = Animator.StringToHash("PlaySound_WeaponSwing_Heavy");

        /// <summary>이동 시 발소리</summary>
        public static readonly int PlaySound_Footstep = Animator.StringToHash("PlaySound_Footstep");

        // =================================================================================
        // [카테고리 3: 시각적 피드백 (Visuals & Camera)]
        // =================================================================================

        /// <summary>적중 시 출혈 파티클 발생</summary>
        public static readonly int PlayVFX_BloodSplatter = Animator.StringToHash("PlayVFX_BloodSplatter");

        /// <summary>무기 궤적(잔상) 이펙트 켜기</summary>
        public static readonly int PlayVFX_WeaponTrail = Animator.StringToHash("PlayVFX_WeaponTrail");

        /// <summary>가벼운 피격 화면 진동</summary>
        public static readonly int CameraShake_Light = Animator.StringToHash("CameraShake_Light");

        /// <summary>강공격 시 화면 강제 진동 (마그네틱 카메라 연동)</summary>
        public static readonly int CameraShake_Heavy = Animator.StringToHash("CameraShake_Heavy");
    }
}