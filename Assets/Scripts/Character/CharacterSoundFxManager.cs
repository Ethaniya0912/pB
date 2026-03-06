using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character
{
    [Serializable]
    public struct SoundTriggerMapping
    {
        public global::AnimationEventType triggerType;
        public AudioClip audioClip;
        [Range(0f, 1f)] public float volume;
        public bool randomizePitch;
    }

    /// <summary>
    /// [L4 Event] 캐릭터의 모든 청각 피드백을 총괄하는 자율 오디오 스테이션.
    /// 중복 재생 쿨타임 필터를 내장하여 사운드 찢어짐을 방지합니다.
    /// </summary>
    public class CharacterSoundFxManager : MonoBehaviour, IAnimationEventListener
    {
        protected AudioSource audioSource;
        protected CharacterEventManager eventManager;

        [Header("Event Triggers (P4)")]
        public List<SoundTriggerMapping> soundMappings = new List<SoundTriggerMapping>();

        [Header("Anti-Clipping Filter")]
        public float globalMinInterval = 0.05f;

        protected Dictionary<global::AnimationEventType, float> lastPlayedTimes = new Dictionary<global::AnimationEventType, float>();

        protected virtual void Awake()
        {
            audioSource = GetComponent<AudioSource>();
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
            if (lastPlayedTimes.TryGetValue(eventType, out float lastTime))
            {
                if (Time.time < lastTime + globalMinInterval) return;
            }

            foreach (var mapping in soundMappings)
            {
                if (mapping.triggerType == eventType && mapping.audioClip != null)
                {
                    lastPlayedTimes[eventType] = Time.time;
                    PlaySoundFX(mapping.audioClip, mapping.volume > 0 ? mapping.volume : 1f, mapping.randomizePitch);
                    break;
                }
            }
        }

        public virtual void PlaySoundFX(AudioClip soundFX, float volume = 1, bool randomizePitch = true, float pitchRandom = 0.1f)
        {
            if (audioSource == null) return;
            audioSource.PlayOneShot(soundFX, volume);
            audioSource.pitch = 1;
            if (randomizePitch) audioSource.pitch += UnityEngine.Random.Range(-pitchRandom, pitchRandom);
        }

        public virtual void PlayRollSoundFX()
        {
            if (WorldSoundFXManager.Instance != null && WorldSoundFXManager.Instance.rollSFX != null)
            {
                if (audioSource != null) audioSource.PlayOneShot(WorldSoundFXManager.Instance.rollSFX);
            }
        }
    }
}