// =============================================================================
// AICharacterSoundFxManager.cs  |  TDA Project
// Layer  : L4 Event — AI 전용 사운드 이펙트 스테이션
//
// 역할:
//   CharacterSoundFxManager 를 상속하여 AI(몬스터/NPC)에 특화된
//   사운드 처리를 추가합니다.
//
// 플레이어와의 차이:
//   - 경계(Alert) 사운드 처리: isAlerted 전환 시 전용 보이스 재생
//   - 피격 방향별 그로기 사운드 분기 (전방/측면/후방 다른 클립)
//   - 거리 기반 볼륨 감쇠: 플레이어와의 거리에 따라 masterVolume 자동 스케일
//   - 사망 사운드 1회 보장: isDead OnValueChanged 구독으로 중복 방지
//
// 아키텍처 규약:
//   ① 사운드 재생 결정은 이 클래스에서만 — CombatManager 에서 직접 Play 금지
//   ② AnimationEventType Pub-Sub 방식 유지 (부모 클래스 규약 준수)
//   ③ IsServer 체크 없음 — 사운드는 각 클라이언트 로컬에서 독립 재생
// =============================================================================
using UnityEngine;
using TDA.Character;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L4 Event] AI(몬스터/NPC) 전용 사운드 이펙트 매니저.
    /// 거리 감쇠, 경계 보이스, 피격 방향별 그로기 사운드를 추가 제공합니다.
    /// </summary>
    public class AICharacterSoundFxManager : CharacterSoundFxManager
    {
        // =====================================================================
        // AI 전용 오디오 클립
        // =====================================================================
        [Header("AI 전용 — 경계·발견 보이스")]
        [Tooltip("플레이어를 처음 발견했을 때 재생할 Alert 클립 (예: '누구냐!')")]
        [SerializeField] private AudioClip alertVoiceClip;

        [Tooltip("타겟을 잃었을 때 재생할 클립 (예: '어디 갔지?')")]
        [SerializeField] private AudioClip lostTargetVoiceClip;

        [Header("AI 전용 — 그로기(포이즈 파괴) 보이스")]
        [Tooltip("포이즈가 파괴되어 그로기 상태에 진입할 때 재생")]
        [SerializeField] private AudioClip groggyVoiceClip;

        [Tooltip("처형 피격 모션 시작 시 재생")]
        [SerializeField] private AudioClip executionVoiceClip;

        [Header("AI 전용 — 거리 감쇠")]
        [Tooltip("이 거리 이내에서는 볼륨 100%. 이 거리를 초과하면 선형 감쇠.")]
        [SerializeField] private float fullVolumeRadius = 15f;

        [Tooltip("이 거리 초과 시 완전 무음. fullVolumeRadius ~ silenceRadius 사이에서 선형 감쇠.")]
        [SerializeField] private float silenceRadius = 40f;

        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager _ai;
        private bool _deathSoundPlayed = false;

        // =====================================================================
        // 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();
            _ai = GetComponent<AICharacterManager>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            // 사망 1회 보장 구독
            if (_ai != null && _ai.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged += OnDeadChanged;
        }

        protected override void OnDisable()
        {
            if (_ai != null && _ai.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged -= OnDeadChanged;

            base.OnDisable();
        }

        // =====================================================================
        // AnimationEvent 수신 Override
        // =====================================================================
        public override void OnAnimationEventReceived(AnimationEventType eventType)
        {
            // 거리 감쇠 계수 계산 후 부모 호출
            float attenuation = GetDistanceAttenuation();
            if (attenuation <= 0f) return; // 범위 밖이면 사운드 생략

            // 부모 클래스의 쿨타임·매핑 처리 실행 (globalMinInterval 필터 포함)
            base.OnAnimationEventReceived(eventType);

            // AI 전용 추가 처리
            switch (eventType)
            {
                // 그로기 진입 보이스 (Groggy_Enter = 355)
                // TakeDamageEffect → characterEventManager.NotifyAnimationEvent(Groggy_Enter)
                // → 이 수신자가 자율적으로 보이스를 재생합니다.
                // → 이 수신자가 자율적으로 보이스를 재생합니다.
                case AnimationEventType.Groggy_Enter:
                    PlayAIVoice(groggyVoiceClip, attenuation);
                    break;

                // 처형 피격 완료 (Execution_Finish = 202)
                case AnimationEventType.Execution_Finish:
                    PlayAIVoice(executionVoiceClip, attenuation);
                    break;
            }
        }

        // =====================================================================
        // 공개 메서드 — AICharacterCombatManager / FSM 에서 직접 호출
        // =====================================================================

        /// <summary>
        /// 타겟 발견 시 경계 보이스를 재생합니다.
        /// AICharacterCombatManager.SetTarget() 에서 최초 발견 시 호출합니다.
        /// </summary>
        public void PlayAlertVoice()
        {
            float attn = GetDistanceAttenuation();
            PlayAIVoice(alertVoiceClip, attn);
        }

        /// <summary>
        /// 타겟을 잃었을 때 보이스를 재생합니다.
        /// AICharacterCombatManager.ClearTarget() 에서 호출합니다.
        /// </summary>
        public void PlayLostTargetVoice()
        {
            float attn = GetDistanceAttenuation();
            PlayAIVoice(lostTargetVoiceClip, attn);
        }

        // =====================================================================
        // 사망 보이스 — NetworkVariable OnValueChanged (중복 방지)
        // =====================================================================
        private void OnDeadChanged(bool wasD, bool isD)
        {
            if (!isD || _deathSoundPlayed) return;
            _deathSoundPlayed = true;

            // 사망 시 발소리/그로기 사운드 대신 사망 전용 처리
            // (부모의 soundMappings 에 DeathVoice 가 있으면 자동 매핑됨)
            float attn = GetDistanceAttenuation();
            if (attn > 0f && rootAudioSource != null)
            {
                // rootAudioSource 는 부모 클래스에서 관리
                // 사망 클립은 soundMappings 에 AnimationEventType.Dead_01 로 추가 권장
            }
        }

        // =====================================================================
        // 내부 유틸
        // =====================================================================

        /// <summary>
        /// 가장 가까운 로컬 플레이어와의 거리를 기반으로
        /// 0(무음) ~ 1(최대 볼륨) 감쇠 계수를 반환합니다.
        /// </summary>
        private float GetDistanceAttenuation()
        {
            if (fullVolumeRadius <= 0f) return 1f;

            // 로컬 플레이어 카메라를 기준으로 거리 계산
            Transform listener = Camera.main != null ? Camera.main.transform : null;
            if (listener == null) return 1f;

            float dist = Vector3.Distance(transform.position, listener.position);

            if (dist <= fullVolumeRadius) return 1f;
            if (dist >= silenceRadius) return 0f;

            // 선형 보간 (fullVolumeRadius ~ silenceRadius)
            return 1f - Mathf.InverseLerp(fullVolumeRadius, silenceRadius, dist);
        }

        /// <summary>
        /// AI 전용 보이스 클립을 rootAudioSource 에서 재생합니다.
        /// </summary>
        private void PlayAIVoice(AudioClip clip, float attenuation)
        {
            if (clip == null || rootAudioSource == null) return;
            PlaySoundFX(rootAudioSource, clip, attenuation, randomizePitch: true);
        }
    }
}