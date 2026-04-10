// =============================================================================
// SoundEventEmitter.cs  |  pB×pC 통합 — Week 1 (pC 인지)
// Layer  : L4 Event
// Namespace: TDA.PB4.AI.Perception
//
// 역할:
//   게임 내 소리 이벤트를 발행한다.
//   플레이어 발소리, 전투 소음, 채굴 소리 등이 발생하면
//   AIPerceptionSystem.OnSoundHeard()에 전달되어 AI 청각 인지를 작동시킨다.
//   FactionDetectionSFXManager와도 연동된다.
// =============================================================================
using System;
using UnityEngine;

namespace TDA.PB4.AI.Perception
{
    /// <summary>소리 유형. AI 반응이 유형별로 다름.</summary>
    public enum SoundType
    {
        /// <summary>발소리. volume=0.3. 걷기/달리기.</summary>
        Footstep,
        /// <summary>전투 소음. volume=0.8. 무기 충돌/비명.</summary>
        Combat,
        /// <summary>채굴/파괴. volume=0.6. 곡괭이/문 부수기.</summary>
        Mining,
        /// <summary>폭발. volume=1.0. 함정 작동/폭발물.</summary>
        Explosion,
        /// <summary>대화. volume=0.2. NPC 발화.</summary>
        Dialogue
    }

    public class SoundEventEmitter : MonoBehaviour
    {
        /// <summary>전역 이벤트. AI의 AIPerceptionSystem이 구독.</summary>
        public static event Action<Vector3, SoundType, float> OnSoundEmitted;

        [Header("━━━ 기본 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 오브젝트가 발생시키는 기본 소리 유형.")]
        public SoundType defaultSoundType = SoundType.Footstep;

        [Tooltip("기본 볼륨 (0~1). 발소리=0.3, 전투=0.8, 폭발=1.0.")]
        [Range(0f, 1f)]
        public float defaultVolume = 0.3f;

        [Tooltip("기본 전파 반경. volume × baseRadius = 실제 전파 거리.")]
        public float baseRadius = 30f;

        [Tooltip("자동 발행 간격 (초). 0=수동만. >0이면 자동으로 반복 발행. " +
                 "Footstep은 0.5초 간격 권장.")]
        [Range(0f, 5f)]
        public float autoEmitInterval = 0f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;
        public bool showGizmos = true;

        private float timer;

        private void Update()
        {
            if (autoEmitInterval <= 0f) return;
            timer += Time.deltaTime;
            if (timer >= autoEmitInterval)
            {
                timer = 0f;
                EmitSound(transform.position, defaultSoundType, defaultVolume);
            }
        }

        /// <summary>소리 이벤트 발행. AI들의 OnSoundHeard가 이 이벤트를 수신.</summary>
        public static void EmitSound(Vector3 position, SoundType type, float volume)
        {
            OnSoundEmitted?.Invoke(position, type, volume);
        }

        /// <summary>이 오브젝트의 기본 설정으로 소리 발행.</summary>
        [ContextMenu("Emit Sound Now")]
        public void EmitDefault()
        {
            EmitSound(transform.position, defaultSoundType, defaultVolume);
            if (debugLog) Debug.Log($"[SoundEmit] {name}: {defaultSoundType} vol={defaultVolume:F1}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, defaultVolume * baseRadius);
        }
#endif
    }
}
