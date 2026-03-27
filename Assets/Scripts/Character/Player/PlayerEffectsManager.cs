// ══════════════════════════════════════════════════════════════════════════════
//  PlayerEffectsManager.cs  (기존 파일 전체 교체)
//  계층  : [L4 Event/Coordination]
//  네임스페이스: TDA.Character.Player
//
//  변경 요약:
//  ┌──────────────────────────────────────────────────────────────────────┐
//  │  이전 (하드코딩)                   →  이후 (위임)                    │
//  │  WorldCameraManager.ApplyCameraShake(1.0f, 5f)  → 제거              │
//  │  WorldCameraManager.ApplyCameraShake(0.3f, 2f)  → 제거              │
//  │  ※ 카메라 쉐이크는 PlayerEventManager 가 SO 기반으로 처리합니다.    │
//  │     PlayerEffectsManager 는 VFX(이펙트) 만 담당합니다. (SRP 준수)   │
//  └──────────────────────────────────────────────────────────────────────┘
//
//  아키텍처 규약:
//  • 카메라 연출은 PlayerEventManager 의 책임 — 이 파일에서 금지
//  • HitSpark VFX 는 부모(CharacterEffectsManager.PlayHitSparkVFX) 위임
//  • NGO 2.0: IsOwner 체크로 로컬 클라이언트만 VFX 실행
// ══════════════════════════════════════════════════════════════════════════════
using UnityEngine;
using TDA.Core.Events;
using TDA.World;

namespace TDA.Character.Player
{
    public class PlayerEffectsManager : CharacterEffectsManager
    {
        // ── Debug (기존 유지) ─────────────────────────────────────────────────
        [Header("Debug — Delete Later")]
        [SerializeField] private InstantCharacterEffect effectToTest;
        [SerializeField] private bool processEffect = false;

        // ══════════════════════════════════════════════════════════════════════
        //  IAnimationEventListener Override
        // ══════════════════════════════════════════════════════════════════════

        public override void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            // 공통 처리 (EffectTriggerMapping, Damage_Calculated, HitSpark 계열 + Blur 기본 전환)
            base.OnAnimationEventReceived(eventType);

            // ── NGO 2.0: 로컬 소유 클라이언트만 추가 처리 ──────────────────
            if (character != null && !character.IsOwner) return;

            // ── 카메라 쉐이크는 PlayerEventManager 가 SO 기반으로 처리하므로
            //    이 파일에서는 처리하지 않습니다. (SRP — 역할 분리)
        }

        // ════════════════════════════════════════════════════════════════════
        //  Motion Blur 상태 전환 — 플레이어 전용 override
        //  부모의 기본 전환(HitBoxEnable/Disable, Action_Ended 등) 위에
        //  플레이어 전용 로직(이동 중 Strafe)을 추가합니다.
        //  PlayerLocomotionManager.HandleAllMovement() 가 매 프레임 호출합니다.
        // ════════════════════════════════════════════════════════════════════
        public void UpdateLocomotionBlur(float moveAmount, bool isPerformingAction)
        {
            if (!HasBlurController) return;
            if (character != null && !character.IsOwner) return;

            // 공격/피격 중에는 로코모션이 blur 상태를 덮어쓰지 않음
            if (isPerformingAction) return;

            if (moveAmount > 0.1f)
                SetBlurState(ObjectMotionBlurController.BlurState.Strafe);
            else
                SetBlurState(ObjectMotionBlurController.BlurState.Idle);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PlayHitSparkVFX Override
        //  플레이어는 피격 VFX 타입을 공격 강도에 따라 세분화할 수 있습니다.
        //  기본 구현은 부모에 위임하고, 필요 시 eventType 을 override 합니다.
        // ══════════════════════════════════════════════════════════════════════

        public override void PlayHitSparkVFX(
            global::AnimationEventType eventType,
            Vector3 contactPoint,
            Vector3 deflectDirection)
        {
            // 플레이어는 IsOwner 체크 없이 모든 클라이언트에서 VFX 실행
            // (GPU 파티클은 서버 권한 없이 로컬에서만 재생됨)
            base.PlayHitSparkVFX(eventType, contactPoint, deflectDirection);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Debug Update (기존 유지)
        // ══════════════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!processEffect) return;
            processEffect = false;

            if (effectToTest is TakeStaminaDamageEffect staminaEffect)
            {
                var effect = Instantiate(staminaEffect);
                effect.staminaDamage = 55;
                ProcessInstantEffects(effect);
            }
            else if (effectToTest != null)
            {
                ProcessInstantEffects(Instantiate(effectToTest));
            }
        }
    }
}