using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character
{
    [Serializable]
    public struct EffectTriggerMapping
    {
        [Tooltip("수신 대기할 이벤트 종류 (예: PlayVFX_BloodSplatter)")]
        public global::AnimationEventType triggerType;
        [Tooltip("발동시킬 이펙트 SO 데이터 (TakeDamageEffect 등)")]
        public InstantCharacterEffect effectSO;
    }

    public class CharacterEffectsManager : MonoBehaviour, IAnimationEventListener
    {
        protected CharacterManager character;
        protected CharacterEventManager eventManager;

        [Header("VFX Prefabs")]
        [SerializeField] GameObject bloodSplatterVFX;
        [SerializeField] GameObject hitsparkVFX;

        // ─────────────────────────────────────────────────────────────────────
        // [수정 1] lastAttackerPosition 활성화
        //   이전: 선언만 되어 있고 어디서도 읽지 않았음
        //         deflectDir 계산에 공격자 위치가 전혀 반영되지 않은 원인
        //   수정: TakeDamageEffect(또는 ProcessInstantEffects 경로)에서
        //         이 값을 세팅하고, OnAnimationEventReceived 에서 활용
        // ─────────────────────────────────────────────────────────────────────
        [HideInInspector] public Vector3 lastContactPoint;
        [HideInInspector] public Vector3 lastAttackerPosition;

        [Header("Event Triggers (P4)")]
        [Tooltip("애니메이션 이벤트 신호에 맞춰 자율적으로 실행될 이펙트 매핑입니다.")]
        public List<EffectTriggerMapping> effectMappings = new List<EffectTriggerMapping>();

        [Header("Active Effects (Status)")]
        [Tooltip("현재 캐릭터가 받고 있는 틱(Tick) 데미지, 버프, 상태 이상 효과 리스트입니다.")]
        public List<InstantCharacterEffect> activeTimedEffects = new List<InstantCharacterEffect>();
        public List<InstantCharacterEffect> activeStaticEffects = new List<InstantCharacterEffect>();

        protected virtual void Awake()
        {
            character  = GetComponent<CharacterManager>();
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

        public virtual void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            foreach (var mapping in effectMappings)
            {
                if (mapping.triggerType == eventType && mapping.effectSO != null)
                    ProcessInstantEffects(mapping.effectSO);
            }

            if (eventType == global::AnimationEventType.Damage_Calculated)
            {
                Vector3 cachedContactPoint = lastContactPoint;
                Vector3 dynamicContactPoint = (cachedContactPoint != Vector3.zero)
                    ? cachedContactPoint
                    : transform.position + new Vector3(0, 1.2f, 0);

                PlayBloodSplatterVFX(dynamicContactPoint);

                // ─────────────────────────────────────────────────────────────
                // [수정 2] deflectDir 계산 방식 변경 — 핵심 수정
                //   이전: (dynamicContactPoint - transform.position).normalized
                //         → 피격 캐릭터의 발(pivot)에서 접촉점을 향하는 방향
                //         → 공격자 위치나 타격 방향과 완전히 무관
                //         → 파티클이 항상 캐릭터 위쪽이나 측면으로 엉뚱하게 비산
                //
                //   수정: (transform.position - lastAttackerPosition).normalized
                //         → '공격자 → 피격자' 방향 = 충격이 진행된 방향
                //         → 파티클이 타격 방향의 연장선(반대편)으로 튀어나감
                //         → 금속 표면에 총알이 맞았을 때처럼 현실적인 비산
                //
                //   fallback: 공격자 위치 미확인 시
                //         접촉점에서 캐릭터 중심 방향의 반대(표면 법선 근사)로 대체
                // ─────────────────────────────────────────────────────────────
                Vector3 deflectDir;

                if (lastAttackerPosition != Vector3.zero)
                {
                    // 공격자 → 피격자 방향 = 충격 진행 방향 = 파티클이 뻗어나가는 방향
                    deflectDir = (transform.position - lastAttackerPosition).normalized;
                }
                else
                {
                    // fallback: 표면 법선 근사 (접촉점 → 캐릭터 중심 반대)
                    deflectDir = (dynamicContactPoint - transform.position).normalized;
                }

                PlayHitSparkVFX(dynamicContactPoint, deflectDir);

                // 캐시 초기화
                lastContactPoint     = Vector3.zero;
                lastAttackerPosition = Vector3.zero;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // [수정 3] CacheAttackerInfo() 공개 메서드 추가
        //   TakeDamageEffect.ProcessEffect() 또는 PlayDamageVFX() 내에서
        //   이 메서드를 호출해 공격자 위치를 캐싱합니다.
        //   호출 예시 (TakeDamageEffect.cs 내):
        //
        //     character.characterEffectsManager.lastContactPoint     = contactPoint;
        //     character.characterEffectsManager.lastAttackerPosition
        //         = (characterCausingDamage != null)
        //         ? characterCausingDamage.transform.position
        //         : contactPoint - character.transform.forward;
        //
        //   또는 아래 헬퍼 메서드를 직접 호출:
        //     character.characterEffectsManager.CacheAttackerInfo(contactPoint, attackerPos);
        // ─────────────────────────────────────────────────────────────────────
        public void CacheAttackerInfo(Vector3 contactPoint, Vector3 attackerPosition)
        {
            lastContactPoint     = contactPoint;
            lastAttackerPosition = attackerPosition;
        }

        public virtual void ProcessInstantEffects(InstantCharacterEffect effect)
        {
            if (effect != null) effect.ProcessEffect(character);
        }

        public virtual void ProcessTimedEffects(InstantCharacterEffect effect)
        {
            if (effect != null && !activeTimedEffects.Contains(effect))
                activeTimedEffects.Add(effect);
        }

        public virtual void ProcessStaticEffects(InstantCharacterEffect effect)
        {
            if (effect != null && !activeStaticEffects.Contains(effect))
                activeStaticEffects.Add(effect);
        }

        public void PlayBloodSplatterVFX(Vector3 contactPoint)
        {
            if (bloodSplatterVFX != null)
                Instantiate(bloodSplatterVFX, contactPoint, Quaternion.identity);
            else if (WorldCharacterEffectsManager.Instance != null && WorldCharacterEffectsManager.Instance.bloodSplatterVFX != null)
                Instantiate(WorldCharacterEffectsManager.Instance.bloodSplatterVFX, contactPoint, Quaternion.identity);
        }

        public void PlayHitSparkVFX(Vector3 contactPoint, Vector3 deflectDirection)
        {
            if (deflectDirection == Vector3.zero) deflectDirection = transform.forward;
            Quaternion rotation = Quaternion.LookRotation(deflectDirection);

            if (hitsparkVFX != null)
                Instantiate(hitsparkVFX, contactPoint, rotation);
            else if (WorldCharacterEffectsManager.Instance != null && WorldCharacterEffectsManager.Instance.hitsparkVFX != null)
                Instantiate(WorldCharacterEffectsManager.Instance.hitsparkVFX, contactPoint, rotation);
        }
    }
}
