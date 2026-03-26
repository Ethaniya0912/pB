// =============================================================================
// AICharacterEffectsManager.cs  |  TDA Project
// Layer  : L4 Event — AI 전용 시각 이펙트 오케스트라
//
// 역할:
//   CharacterEffectsManager 를 상속하여 AI(몬스터/NPC)에 특화된
//   VFX 처리를 추가합니다.
//
// 플레이어와의 차이:
//   - 그로기(포이즈 파괴) 전용 VFX 스폰 (별개 파티클, 플레이어와 구분)
//   - 처형 피격 전용 연출 (카메라 흔들림 신호, 처형 광원 스폰)
//   - 페이즈 전환 VFX (보스 AI 전용 — 연기·충격파 이펙트)
//   - 사망 후 시체 비주얼 처리 (래그돌 전환 후 해골 잔해 스폰 등)
//   - 피격 방향별 혈흔 방향 보정 (플레이어 동일 로직 + AI 전용 파티클)
//
// 아키텍처 규약:
//   ① VFX 스폰은 이 클래스에서만 — CombatManager 에서 직접 Instantiate 금지
//   ② AnimationEventType Pub-Sub 방식 유지
//   ③ ProcessInstantEffects 는 부모 위임, AI 전용 이펙트만 Override
// =============================================================================
using UnityEngine;
using TDA.Character;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L4 Event] AI(몬스터/NPC) 전용 시각 이펙트 매니저.
    /// 그로기·처형·페이즈 전환 VFX 를 추가로 제공합니다.
    /// </summary>
    public class AICharacterEffectsManager : CharacterEffectsManager
    {
        // =====================================================================
        // AI 전용 VFX 프리팹
        // =====================================================================
        [Header("AI 전용 — 그로기 VFX")]
        [Tooltip("포이즈가 파괴되어 그로기 상태에 진입할 때 스폰할 이펙트 프리팹")]
        [SerializeField] private GameObject groggyImpactVFX;

        [Tooltip("그로기 지속 중 캐릭터 위에 띄울 루프 이펙트 프리팹 (예: 별·눈 돌기)")]
        [SerializeField] private GameObject groggyLoopVFX;

        [Header("AI 전용 — 처형 VFX")]
        [Tooltip("처형 피격 완료 시 접촉점에 스폰할 이펙트 (예: 강렬한 혈흔·충격파)")]
        [SerializeField] private GameObject executionFinishVFX;

        [Header("AI 전용 — 사망 VFX")]
        [Tooltip("사망 후 래그돌 진입 직후 스폰할 이펙트 (예: 먼지·붕괴 파티클)")]
        [SerializeField] private GameObject deathDissolveVFX;

        [Header("AI 전용 — 보스 페이즈 VFX")]
        [Tooltip("보스 페이즈 전환 시 캐릭터 중심에 스폰할 충격파 이펙트")]
        [SerializeField] private GameObject phaseTransitionVFX;

        [Header("그로기 루프 VFX 오프셋")]
        [Tooltip("캐릭터 머리 위 루프 VFX 의 높이 오프셋 (m)")]
        [SerializeField] private float groggyVFXHeightOffset = 1.8f;

        // =====================================================================
        // 내부 상태
        // =====================================================================
        private AICharacterManager _ai;
        private GameObject _activeGroggyLoopVFX;   // 그로기 루프 VFX 인스턴스
        private bool _deathVFXPlayed = false;

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

            if (_ai != null && _ai.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged += OnDeadChanged;
        }

        protected override void OnDisable()
        {
            if (_ai != null && _ai.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged -= OnDeadChanged;

            CleanUpGroggyLoopVFX();
            base.OnDisable();
        }

        // =====================================================================
        // AnimationEvent 수신 Override
        // =====================================================================
        public override void OnAnimationEventReceived(AnimationEventType eventType)
        {
            // 부모의 공통 처리(피격 VFX, 이펙트 매핑) 먼저 실행
            base.OnAnimationEventReceived(eventType);

            // AI 전용 추가 처리
            switch (eventType)
            {
                // 그로기 진입 (포이즈 파괴 = Guard_Break_Stun)
                case AnimationEventType.Groggy_Enter:
                    OnGroggyEnter();
                    break;

                // 처형 완료
                case AnimationEventType.Execution_Finish:
                    OnExecutionFinish();
                    break;
            }
        }

        // =====================================================================
        // AI 전용 이벤트 핸들러
        // =====================================================================

        /// <summary>
        /// 그로기 진입 시: 임팩트 VFX 스폰 + 루프 VFX 시작.
        /// GroggyState 진입과 동기화하여 호출됩니다.
        /// </summary>
        private void OnGroggyEnter()
        {
            Vector3 center = transform.position + Vector3.up * (groggyVFXHeightOffset * 0.5f);

            // 그로기 임팩트 (1회성)
            if (groggyImpactVFX != null)
                Instantiate(groggyImpactVFX, center, Quaternion.identity);

            // 그로기 루프 (지속 — GroggyState 종료 시 CleanUpGroggyLoopVFX 호출)
            if (groggyLoopVFX != null && _activeGroggyLoopVFX == null)
            {
                Vector3 loopPos = transform.position + Vector3.up * groggyVFXHeightOffset;
                _activeGroggyLoopVFX = Instantiate(groggyLoopVFX, loopPos, Quaternion.identity);
                _activeGroggyLoopVFX.transform.SetParent(transform); // 캐릭터와 함께 이동
            }
        }

        /// <summary>
        /// 처형 완료 시: 강렬한 VFX 스폰.
        /// </summary>
        private void OnExecutionFinish()
        {
            CleanUpGroggyLoopVFX(); // 처형 완료 시 그로기 루프 VFX 제거

            if (executionFinishVFX != null)
            {
                Vector3 pos = lastContactPoint != Vector3.zero
                    ? lastContactPoint
                    : transform.position + Vector3.up * 1f;

                Instantiate(executionFinishVFX, pos, Quaternion.identity);
            }
        }

        /// <summary>
        /// 사망 시: 해산(Dissolve) VFX 스폰. 1회만 실행.
        /// </summary>
        private void OnDeadChanged(bool was, bool isNow)
        {
            if (!isNow || _deathVFXPlayed) return;
            _deathVFXPlayed = true;

            CleanUpGroggyLoopVFX();

            if (deathDissolveVFX != null)
                Instantiate(deathDissolveVFX, transform.position, transform.rotation);
        }

        // =====================================================================
        // 공개 메서드 — AICharacterNetworkManager 의 OnPhaseChanged 에서 호출
        // =====================================================================

        /// <summary>
        /// 보스 페이즈 전환 VFX 를 스폰합니다.
        /// AICharacterNetworkManager.OnPhaseChanged() 에서 호출합니다.
        /// </summary>
        public void PlayPhaseTransitionVFX()
        {
            if (phaseTransitionVFX == null) return;
            Instantiate(phaseTransitionVFX, transform.position, Quaternion.identity);
        }

        /// <summary>
        /// 그로기 루프 VFX 를 명시적으로 제거합니다.
        /// GroggyState.ResetStateFlags() 에서 호출합니다.
        /// </summary>
        public void CleanUpGroggyLoopVFX()
        {
            if (_activeGroggyLoopVFX != null)
            {
                Destroy(_activeGroggyLoopVFX);
                _activeGroggyLoopVFX = null;
            }
        }
    }
}