// ══════════════════════════════════════════════════════════════════════════════
//  AICharacterEffectsManager.cs  (기존 파일 전체 교체)
//  계층  : [L4 Event/Coordination]
//  네임스페이스: TDA.Character.AI
//
//  변경 요약:
//  ┌──────────────────────────────────────────────────────────────────────┐
//  │  이전 (하드코딩)              →  이후 (SO 드리븐)                    │
//  │  Instantiate(hitsparkVFX)    →  base.PlayHitSparkVFX(eventType, ..) │
//  │                               →    → vfxRegistry.GetData(eventType) │
//  │                               →    → gpuSparkPrefab.EmitSparks(..)  │
//  │  OnGroggyEnter Instantiate  → 기존 방식 유지 (GPU 미전환)            │
//  └──────────────────────────────────────────────────────────────────────┘
//
//  아키텍처 규약:
//  • HitSpark VFX 는 부모(CharacterEffectsManager.PlayHitSparkVFX) 에 위임
//  • 그로기/처형/페이즈 전환 VFX 는 기존 Instantiate 방식 유지
//  • NGO 2.0: IsOwnedByServer 판별 없이 모든 클라이언트에서 로컬 VFX 실행
//    (AI NetworkObject 는 모든 클라이언트에 스폰되어 있으므로 각자 로컬 실행)
// ══════════════════════════════════════════════════════════════════════════════
using UnityEngine;
using TDA.Character;

namespace TDA.Character.AI
{
    public class AICharacterEffectsManager : CharacterEffectsManager
    {
        // ── AI 전용 VFX 프리팹 (기존 필드 완전 보존) ─────────────────────────
        [Header("AI 전용 — 그로기 VFX")]
        [Tooltip("포이즈가 파괴되어 그로기 상태 진입 시 스폰할 이펙트 프리팹")]
        [SerializeField] private GameObject groggyImpactVFX;

        [Tooltip("그로기 지속 중 캐릭터 위에 띄울 루프 이펙트 프리팹")]
        [SerializeField] private GameObject groggyLoopVFX;

        [Header("AI 전용 — 처형 VFX")]
        [Tooltip("처형 피격 완료 시 접촉점에 스폰할 이펙트")]
        [SerializeField] private GameObject executionFinishVFX;

        [Header("AI 전용 — 사망 VFX")]
        [Tooltip("사망 후 래그돌 진입 직후 스폰할 이펙트")]
        [SerializeField] private GameObject deathDissolveVFX;

        [Header("AI 전용 — 보스 페이즈 VFX")]
        [Tooltip("보스 페이즈 전환 시 캐릭터 중심에 스폰할 충격파 이펙트")]
        [SerializeField] private GameObject phaseTransitionVFX;

        [Header("그로기 루프 VFX 오프셋")]
        [Tooltip("캐릭터 머리 위 루프 VFX 의 높이 오프셋 (m)")]
        [SerializeField] private float groggyVFXHeightOffset = 1.8f;

        // ── 내부 상태 ─────────────────────────────────────────────────────────
        private AICharacterManager _ai;
        private GameObject _activeGroggyLoopVFX;
        private bool _deathVFXPlayed = false;

        // ══════════════════════════════════════════════════════════════════════
        //  생명주기
        // ══════════════════════════════════════════════════════════════════════

        protected override void Awake()
        {
            base.Awake();
            _ai = GetComponent<AICharacterManager>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (_ai?.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged += OnDeadChanged;
        }

        protected override void OnDisable()
        {
            if (_ai?.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged -= OnDeadChanged;

            CleanUpGroggyLoopVFX();
            base.OnDisable();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  AnimationEvent 수신 Override (기존 로직 완전 보존 + HitSpark 경로 추가)
        // ══════════════════════════════════════════════════════════════════════

        public override void OnAnimationEventReceived(AnimationEventType eventType)
        {
            // 부모 처리 (EffectTriggerMapping, Damage_Calculated → HitSpark SO 드리븐)
            base.OnAnimationEventReceived(eventType);

            // AI 전용 추가 처리 (기존 로직 완전 보존)
            switch (eventType)
            {
                case AnimationEventType.Groggy_Enter:
                    OnGroggyEnter();
                    break;

                case AnimationEventType.Groggy_Exit:
                    CleanUpGroggyLoopVFX();
                    break;

                case AnimationEventType.Execution_Finish:
                    OnExecutionFinish();
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PlayHitSparkVFX Override
        //  AI 는 피격 강도(포이즈 파괴 여부)에 따라 HitSpark 타입을 선택합니다.
        //  실제 스폰은 부모의 SO 드리븐 로직에 위임합니다.
        // ══════════════════════════════════════════════════════════════════════

        public override void PlayHitSparkVFX(
            AnimationEventType eventType,
            Vector3 contactPoint,
            Vector3 deflectDirection)
        {
            // Damage_Calculated 경로로 넘어온 Default 타입 →
            // AI 는 최소 HeavyHit 으로 격상 (AI 피격이 더 묵직하게 보이도록)
            AnimationEventType resolved = eventType;

            if (eventType == AnimationEventType.VFX_HitSpark_Default &&
                vfxRegistry != null &&
                vfxRegistry.GetData(AnimationEventType.VFX_HitSpark_Heavy) != null)
            {
                resolved = AnimationEventType.VFX_HitSpark_Heavy;
            }

            base.PlayHitSparkVFX(resolved, contactPoint, deflectDirection);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  AI 전용 이벤트 핸들러 (기존 로직 완전 보존)
        // ══════════════════════════════════════════════════════════════════════

        private void OnGroggyEnter()
        {
            Vector3 center = transform.position + Vector3.up * (groggyVFXHeightOffset * 0.5f);

            if (groggyImpactVFX != null)
                Instantiate(groggyImpactVFX, center, Quaternion.identity);

            if (groggyLoopVFX != null && _activeGroggyLoopVFX == null)
            {
                Vector3 loopPos = transform.position + Vector3.up * groggyVFXHeightOffset;
                _activeGroggyLoopVFX = Instantiate(groggyLoopVFX, loopPos, Quaternion.identity);
                _activeGroggyLoopVFX.transform.SetParent(transform);
            }
        }

        private void OnExecutionFinish()
        {
            CleanUpGroggyLoopVFX();

            if (executionFinishVFX != null)
            {
                Vector3 pos = lastContactPoint != Vector3.zero
                    ? lastContactPoint
                    : transform.position + Vector3.up * 1f;

                Instantiate(executionFinishVFX, pos, Quaternion.identity);
            }
        }

        private void OnDeadChanged(bool was, bool isNow)
        {
            if (!isNow || _deathVFXPlayed) return;
            _deathVFXPlayed = true;

            CleanUpGroggyLoopVFX();

            if (deathDissolveVFX != null)
                Instantiate(deathDissolveVFX, transform.position, transform.rotation);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Public 메서드 (기존 호출부 호환 유지)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>보스 페이즈 전환 VFX. AICharacterNetworkManager.OnPhaseChanged() 에서 호출.</summary>
        public void PlayPhaseTransitionVFX()
        {
            if (phaseTransitionVFX != null)
                Instantiate(phaseTransitionVFX, transform.position, Quaternion.identity);
        }

        /// <summary>그로기 루프 VFX 제거. GroggyState.ResetStateFlags() 에서 호출.</summary>
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
