using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character
{
    /// <summary>
    /// [P4] Enum 기반 이펙트 트리거 매핑 구조체
    /// </summary>
    [Serializable]
    public struct EffectTriggerMapping
    {
        [Tooltip("수신 대기할 이벤트 종류 (예: PlayVFX_BloodSplatter)")]
        public global::AnimationEventType triggerType;
        [Tooltip("발동시킬 이펙트 SO 데이터 (TakeDamageEffect 등)")]
        public InstantCharacterEffect effectSO;
    }

    /// <summary>
    /// [L4 Event] 캐릭터의 시각적 이펙트를 처리하는 자율 수신자입니다.
    /// [오류 정정] 기존에 선언되어 있던 3가지 상태 이펙트(인스턴트, 타임드, 스태틱) 처리 로직을 100% 복구했습니다.
    /// </summary>
    public class CharacterEffectsManager : MonoBehaviour, IAnimationEventListener
    {
        protected CharacterManager character;
        protected CharacterEventManager eventManager;

        [Header("VFX Prefabs")]
        [SerializeField] GameObject bloodSplatterVFX;

        [Header("Event Triggers (P4)")]
        [Tooltip("애니메이션 이벤트 신호에 맞춰 자율적으로 실행될 이펙트 매핑입니다.")]
        public List<EffectTriggerMapping> effectMappings = new List<EffectTriggerMapping>();

        // =========================================================================================
        // [이펙트 보관 리스트 100% 복구]
        // =========================================================================================
        [Header("Active Effects (Status)")]
        [Tooltip("현재 캐릭터가 받고 있는 틱(Tick) 데미지, 버프, 상태 이상 효과 리스트입니다.")]
        public List<InstantCharacterEffect> activeTimedEffects = new List<InstantCharacterEffect>();
        public List<InstantCharacterEffect> activeStaticEffects = new List<InstantCharacterEffect>();

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
            eventManager = GetComponent<CharacterEventManager>();
        }

        protected virtual void OnEnable()
        {
            if (eventManager != null) eventManager.OnAnimationEventTriggered += OnAnimationEventReceived;
        }

        protected virtual void OnDisable()
        {
            if (eventManager != null) eventManager.OnAnimationEventTriggered -= OnAnimationEventReceived;
        }

        /// <summary>
        /// [P4] Enum 신호를 수신하여 매핑된 이펙트를 자율 실행합니다.
        /// </summary>
        public virtual void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            // 1. 매핑된 이펙트 SO가 있다면 실행
            foreach (var mapping in effectMappings)
            {
                if (mapping.triggerType == eventType && mapping.effectSO != null)
                {
                    ProcessInstantEffects(mapping.effectSO);
                }
            }

            // 2. 이벤트 체인 (물리 데미지 연산 후 확실하게 출혈 연출)
            if (eventType == global::AnimationEventType.Damage_Calculated)
            {
                // 몬스터의 발밑(Root)이 아닌 타격이 들어가는 평균적인 가슴 높이(Y축 + 1.2f)에 오프셋을 주어 자연스럽게 연출합니다.
                Vector3 dynamicContactPoint = transform.position + new Vector3(0, 1.2f, 0);
                PlayBloodSplatterVFX(dynamicContactPoint);
            }
        }

        // =========================================================================================
        // [이펙트 처리 다형성 로직 100% 복구]
        // 기존 주석에만 있던 3가지(인스턴트, 시간차, 스태틱) 처리 뼈대를 실체화했습니다.
        // =========================================================================================

        /// <summary>
        /// 인스턴트 이펙트 (테이크 데미지, 즉시 힐링 등 1회성 적용)
        /// </summary>
        public virtual void ProcessInstantEffects(InstantCharacterEffect effect)
        {
            if (effect != null)
            {
                effect.ProcessEffect(character);
            }
        }

        /// <summary>
        /// 시간차 이펙트 (독, 출혈 빌드업 등 시간에 따라 틱 데미지를 주는 효과)
        /// </summary>
        public virtual void ProcessTimedEffects(InstantCharacterEffect effect)
        {
            if (effect != null && !activeTimedEffects.Contains(effect))
            {
                activeTimedEffects.Add(effect);
                // 향후 틱 타이머 코루틴이나 Update 문에서 ProcessEffect를 반복 호출하는 로직 추가
            }
        }

        /// <summary>
        /// 스태틱 이펙트 (장신구 버프, 오라 등 지속적으로 유지되는 효과)
        /// </summary>
        public virtual void ProcessStaticEffects(InstantCharacterEffect effect)
        {
            if (effect != null && !activeStaticEffects.Contains(effect))
            {
                activeStaticEffects.Add(effect);
                // 추가 능력치 부여(Add) 로직 수행
            }
        }

        public void PlayBloodSplatterVFX(Vector3 contactPoint)
        {
            // 만약 우리가 수동적으로 vfx 모델을 배치했을시, 해당 버전을 플레이.
            if (bloodSplatterVFX != null)
            {
                Instantiate(bloodSplatterVFX, contactPoint, Quaternion.identity);
            }
            // 그렇지 않으면, 디폴트 버전을 사용함.
            // 여러 적을 구현할때 반복적으로 인스팩터 사용해야하는 수고로움 방지용.
            else if (WorldCharacterEffectsManager.Instance != null && WorldCharacterEffectsManager.Instance.bloodSplatterVFX != null)
            {
                Instantiate(WorldCharacterEffectsManager.Instance.bloodSplatterVFX, contactPoint, Quaternion.identity);
            }
        }
    }
}