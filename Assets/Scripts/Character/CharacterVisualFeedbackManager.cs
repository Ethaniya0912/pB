using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character
{
    /// <summary>
    /// [Base Class] 모든 캐릭터(플레이어, AI)의 시각적 피드백을 계산하는 기초 클래스입니다.
    /// 기존의 속도 기반 연산에 더하여, 물리 연산 후 피격 깜빡임(Flash) 로직을 수신합니다.
    /// </summary>
    public class CharacterVisualFeedbackManager : MonoBehaviour, IAnimationEventListener
    {
        protected CharacterManager character;
        protected CharacterEventManager eventManager;

        [Header("Base Feedback Settings")]
        public float maxReferenceSpeed = 10f;
        protected float currentSpeedFactor;

        [Header("Hit Flash Settings (P4)")]
        public float flashDuration = 0.2f;
        [ColorUsage(true, true)]
        public Color flashColor = Color.white;

        protected SkinnedMeshRenderer[] characterRenderers;

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
            eventManager = GetComponent<CharacterEventManager>();
            characterRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        }

        protected virtual void OnEnable()
        {
            if (eventManager != null) eventManager.OnAnimationEventTriggered += OnAnimationEventReceived;
        }

        protected virtual void OnDisable()
        {
            if (eventManager != null) eventManager.OnAnimationEventTriggered -= OnAnimationEventReceived;
        }

        protected virtual void Update()
        {
            CalculateBaseSpeedFactor();
        }

        /// <summary>
        /// [P4] 연산과 연출의 인과관계 보장 시스템
        /// CombatManager가 데미지 계산을 확정 지은 후 쏘아 올린 Damage_Calculated 이벤트에 반응합니다.
        /// </summary>
        public virtual void OnAnimationEventReceived(AnimationEventType eventType)
        {
            if (eventType == AnimationEventType.Damage_Calculated)
            {
                if (gameObject.activeInHierarchy)
                {
                    StartCoroutine(FlashCharacterRoutine());
                }
            }
        }

        protected virtual IEnumerator FlashCharacterRoutine()
        {
            if (characterRenderers == null || characterRenderers.Length == 0) yield break;

            float timer = 0f;
            float halfDuration = flashDuration / 2f;

            while (timer < halfDuration)
            {
                timer += Time.deltaTime;
                float t = timer / halfDuration;
                SetRenderersEmission(Color.Lerp(Color.black, flashColor, t));
                yield return null;
            }

            timer = 0f;

            while (timer < halfDuration)
            {
                timer += Time.deltaTime;
                float t = timer / halfDuration;
                SetRenderersEmission(Color.Lerp(flashColor, Color.black, t));
                yield return null;
            }

            SetRenderersEmission(Color.black);
        }

        protected void SetRenderersEmission(Color color)
        {
            foreach (var renderer in characterRenderers)
            {
                foreach (var mat in renderer.materials)
                {
                    mat.SetColor("_EmissionColor", color);
                    mat.EnableKeyword("_EMISSION");
                }
            }
        }

        protected virtual void CalculateBaseSpeedFactor()
        {
            if (character == null) return;
            float velocity = character.characterController != null ? character.characterController.velocity.magnitude : 0f;
            currentSpeedFactor = Mathf.Clamp01(velocity / maxReferenceSpeed);
        }
    }
}