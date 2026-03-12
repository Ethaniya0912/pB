using System.Collections;
using System.Collections.Generic;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using TDA.Core.Events; // [신규 추가] ActionID 및 Funnel 연동을 위한 네임스페이스

namespace TDA.Character
{
    /// <summary>
    /// [L3 Domain] 캐릭터의 스탯(HP, 스태미나 등) 계산 및 관리를 전담하는 클래스입니다.
    /// 피격 데미지가 임계치를 넘어 그로기나 사망에 이를 때,
    /// 직접 애니메이터를 조작하지 않고 L4 Funnel 구조를 통해 리액션을 지시합니다.
    /// </summary>
    public class CharacterStatsManager : MonoBehaviour
    {
        CharacterManager character;

        [Header("Stamina Regeneration")]
        [SerializeField] private int staminaRegenerationAmount = 2;
        private float staminaRegenerationTimer = 0;
        private float staminaTickTimer = 0;
        [SerializeField] float staminaRegenerationDelay = 2;

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
        }

        protected virtual void Start()
        {

        }

        public int CalculateStaminaBasedOnEnduranceLevel(int endurance)
        {
            int stamina = 0;

            // 스태미나가 어떻게 계산될지 등식을 만듬.
            stamina = endurance * 10;

            return Mathf.RoundToInt(stamina);
        }

        public int CalculateHealthBasedOnVitalityLevel(int vitality)
        {
            int health = 0;

            // 스태미나가 어떻게 계산될지 등식을 만듬.
            health = vitality * 10;

            return Mathf.RoundToInt(health);
        }

        public virtual void RegenerateStamina()
        {
            if (!character.IsOwner)
                return;

            if (character.isPerformingAction)
                return;

            staminaRegenerationTimer += Time.deltaTime;

            if (staminaRegenerationTimer >= staminaRegenerationDelay)
            {
                if (character.characterNetworkManager.currentStamina.Value < character.characterNetworkManager.maxStamina.Value)
                {
                    staminaTickTimer += Time.deltaTime;

                    if (staminaTickTimer >= 0.1)
                    {
                        staminaTickTimer = 0;
                        character.characterNetworkManager.currentStamina.Value += staminaRegenerationAmount;
                    }
                }
            }
        }

        public virtual void ResetStaminaRegenTimer(float previousStaminaAmount, float currentStaminaAmount)
        {
            // 스태미나 타이머를 리셋하는건 이전 값이 새 값보다 클떄만.
            if (currentStaminaAmount < previousStaminaAmount)
            {
                staminaRegenerationTimer = 0;
            }
        }

        // =========================================================================================
        // [신규 아키텍처: Funnel 패턴] 피격 및 사망 리액션 위임
        // =========================================================================================

        /// <summary>
        /// [TO-BE 구조] 캐릭터가 피격당해 비틀거려야 할 때 호출됩니다.
        /// 직접 애니메이터의 TakeDamage 문자열을 부르지 않고, Funnel 메서드를 통해 인터럽트를 발생시킵니다.
        /// </summary>
        // 🚨 [에러 해결 완료] 삭제된 Stagger_Medium 대신, 가장 범용적인 'Stagger_Backward'를 기본값으로 변경합니다.
        public virtual void TriggerStaggerState(ActionID staggerActionID = ActionID.Stagger_Backward)
        {
            if (character == null || character.characterAnimationManager == null) return;

            // L4 깔때기를 통해 수동적 리액션(onHit) 발동 및 액션 상태 강제 초기화
            character.characterAnimationManager.PlayTargetHitReactionFunnel((int)staggerActionID);
        }

        /// <summary>
        /// [TO-BE 구조] 캐릭터의 전체 체력이 0이 되어 사망할 때 호출됩니다.
        /// 사망 모션을 Funnel로 전송하여 진행 중이던 모든 액션을 즉시 파괴합니다.
        /// </summary>
        public virtual void TriggerDeathState()
        {
            if (character == null || character.characterAnimationManager == null) return;

            // L4 깔때기를 통해 사망 상태 진입 (최고 우선순위)
            character.characterAnimationManager.PlayTargetHitReactionFunnel((int)ActionID.Dead_01);
        }
    }
}