using System.Collections;
using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character.Player
{
    /// <summary>
    /// 이전에 누락되었던 플레이어 전용 호흡 사운드 시스템을 100% 복구 및 연동했습니다.
    /// </summary>
    public class PlayerSoundFxManager : CharacterSoundFxManager
    {
        [Header("State Audio")]
        [SerializeField] private AudioSource breathingAudioSource;
        [SerializeField] private AudioClip heavyBreathingClip;
        private Coroutine breathingFadeCoroutine;

        public override void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            base.OnAnimationEventReceived(eventType);

            if (eventType == global::AnimationEventType.PlaySFX_Stamina_Exhausted)
            {
                HandleBreathingAudio(true);
            }
            else if (eventType == global::AnimationEventType.PlaySFX_Stamina_Recovered)
            {
                HandleBreathingAudio(false);
            }
        }

        private void HandleBreathingAudio(bool isExhausted)
        {
            if (breathingAudioSource == null || heavyBreathingClip == null) return;
            if (breathingFadeCoroutine != null) StopCoroutine(breathingFadeCoroutine);

            if (gameObject.activeInHierarchy)
            {
                breathingFadeCoroutine = StartCoroutine(FadeBreathingAudioRoutine(isExhausted));
            }
        }

        private IEnumerator FadeBreathingAudioRoutine(bool fadeIn)
        {
            float duration = 0.8f;
            float timer = 0f;
            float startVolume = breathingAudioSource.volume;
            float targetVolume = fadeIn ? 1f : 0f;

            if (fadeIn && !breathingAudioSource.isPlaying)
            {
                breathingAudioSource.clip = heavyBreathingClip;
                breathingAudioSource.loop = true;
                breathingAudioSource.Play();
            }

            while (timer < duration)
            {
                timer += Time.deltaTime;
                breathingAudioSource.volume = Mathf.Lerp(startVolume, targetVolume, timer / duration);
                yield return null;
            }

            breathingAudioSource.volume = targetVolume;
            if (!fadeIn) breathingAudioSource.Stop();
        }
    }
}