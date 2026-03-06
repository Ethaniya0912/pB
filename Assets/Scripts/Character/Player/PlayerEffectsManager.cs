using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;
using TDA.World;

namespace TDA.Character.Player
{
    public class PlayerEffectsManager : CharacterEffectsManager
    {
        [Header("Debug Delete Later")]
        [SerializeField] InstantCharacterEffect effectToTest;
        [SerializeField] bool processEffect = false;

        public override void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            base.OnAnimationEventReceived(eventType);

            if (character != null && !character.IsOwner) return;

            if (eventType == global::AnimationEventType.CameraShake_Heavy)
            {
                if (WorldCameraManager.Instance != null)
                {
                    WorldCameraManager.Instance.ApplyCameraShake(1.0f, 5f);
                }
            }
            else if (eventType == global::AnimationEventType.CameraShake_Light)
            {
                if (WorldCameraManager.Instance != null)
                {
                    WorldCameraManager.Instance.ApplyCameraShake(0.3f, 2f);
                }
            }
        }

        private void Update()
        {
            if (processEffect)
            {
                processEffect = false;
                if (effectToTest is TakeStaminaDamageEffect)
                {
                    TakeStaminaDamageEffect effect = Instantiate(effectToTest) as TakeStaminaDamageEffect;
                    effect.staminaDamage = 55;
                    ProcessInstantEffects(effect);
                }
                else if (effectToTest != null)
                {
                    InstantCharacterEffect effect = Instantiate(effectToTest);
                    ProcessInstantEffects(effect);
                }
            }
        }
    }
}