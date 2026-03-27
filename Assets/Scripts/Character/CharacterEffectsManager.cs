// ══════════════════════════════════════════════════════════════════════════════
//  CharacterEffectsManager.cs  (기존 파일 전체 교체)
//  계층  : [L4 Event/Coordination]
//  네임스페이스: TDA.Character
//
//  변경 요약:
//  ┌──────────────────────────────────────────────────────────────────────┐
//  │  이전 (하드코딩)                  →  이후 (SO 드리븐)                │
//  │  [SerializeField] hitsparkVFX     →  [SerializeField] vfxRegistry    │
//  │  PlayHitSparkVFX(pos, deflect)    →  PlayHitSparkVFX(eventType, ...) │
//  │    → Instantiate(hitsparkVFX)     →    → vfxRegistry.GetData(type)   │
//  │                                   →    → gpuSparkManager.EmitSparks()│
//  └──────────────────────────────────────────────────────────────────────┘
//
//  아키텍처 규약:
//  • CombatManager 에서 이 클래스를 직접 참조하거나 PlayHitSparkVFX 를
//    호출하는 코드는 허용되지 않습니다.
//  • 모든 HitSpark 트리거는 CharacterEventManager.NotifyAnimationEvent(
//    AnimationEventType.VFX_HitSpark_*) 경로를 거쳐야 합니다.
//  • bloodSplatterVFX 는 기존 Instantiate 방식을 유지합니다 (GPU 미전환).
// ══════════════════════════════════════════════════════════════════════════════
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
        // ── 캐릭터 참조 ──────────────────────────────────────────────────────
        protected CharacterManager character;
        protected CharacterEventManager eventManager;

        // ── SO 데이터 ────────────────────────────────────────────────────────
        [Header("VFX Data (SO-Driven)")]
        [Tooltip("AnimationEventType → HitSparkVFXData 매핑 레지스트리 (프로젝트 전역 공유 SO)")]
        [SerializeField] protected HitSparkVFXRegistry vfxRegistry;

        [Tooltip("HitSpark GPU 파티클 매니저 프리팹 (Instantiate 후 EmitSparks 호출)")]
        [SerializeField] protected ParrySparkGPUManager gpuSparkPrefab;

        // ── Blur Controller (옵션) ────────────────────────────────────────────
        // 인스펙터에서 연결하거나 Awake()에서 자동 탐색합니다.
        // AI 캐릭터처럼 blur가 필요 없는 경우 비워두면 자동으로 skip 됩니다.
        [Header("Motion Blur (옵션)")]
        [Tooltip("ObjectMotionBlurController — 비워두면 GetComponentInChildren 으로 자동 탐색")]
        [SerializeField] protected ObjectMotionBlurController blurController;

        // ── Blood VFX (기존 Instantiate 방식 유지) ───────────────────────────
        [Header("Blood VFX (기존 방식 유지)")]
        [SerializeField] protected GameObject bloodSplatterVFX;

        // ── 공격자 캐시 ──────────────────────────────────────────────────────
        [HideInInspector] public Vector3 lastContactPoint;
        [HideInInspector] public Vector3 lastAttackerPosition;

        // ── 이펙트 매핑 ──────────────────────────────────────────────────────
        [Header("Event Triggers (P4)")]
        [Tooltip("애니메이션 이벤트 신호에 맞춰 자율적으로 실행될 이펙트 매핑입니다.")]
        public List<EffectTriggerMapping> effectMappings = new List<EffectTriggerMapping>();

        [Header("Active Effects (Status)")]
        public List<InstantCharacterEffect> activeTimedEffects = new List<InstantCharacterEffect>();
        public List<InstantCharacterEffect> activeStaticEffects = new List<InstantCharacterEffect>();

        // ══════════════════════════════════════════════════════════════════════
        //  생명주기
        // ══════════════════════════════════════════════════════════════════════

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
            eventManager = GetComponent<CharacterEventManager>();

            // blurController 슬롯이 비어있으면 자식에서 자동 탐색
            if (blurController == null)
                blurController = GetComponentInChildren<ObjectMotionBlurController>(true);
        }

        // Rule 3 준수: OnEnable/OnDisable 에서 구독/해제 쌍 관리
        protected virtual void OnEnable()
        {
            if (eventManager != null)
                eventManager.OnAnimationEventTriggered += OnAnimationEventReceived;
        }

        protected virtual void OnDisable()
        {
            if (eventManager != null)
                eventManager.OnAnimationEventTriggered -= OnAnimationEventReceived;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  IAnimationEventListener 구현
        // ══════════════════════════════════════════════════════════════════════

        public virtual void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            // ── EffectTriggerMapping 처리 (기존 동작 완전 보존) ───────────────
            foreach (var mapping in effectMappings)
            {
                if (mapping.triggerType == eventType && mapping.effectSO != null)
                    ProcessInstantEffects(mapping.effectSO);
            }

            // ── Damage_Calculated: 피격 VFX 트리거 (기존 흐름 보존) ──────────
            if (eventType == global::AnimationEventType.Damage_Calculated)
            {
                HandleDamageCalculatedVFX();
                return;
            }

            // ── HitSpark 계열 VFX (700번대) — SO 드리븐 신규 처리 ──────────
            if (eventType.IsHitSparkEvent())
            {
                // Damage_Calculated 와 달리 애니메이터 타임라인에서
                // 직접 HitSpark 이벤트를 발행하는 경우 (별도 충돌 정보 없음)
                Vector3 fallbackContact = transform.position + Vector3.up * 1.2f;
                Vector3 fallbackDir = transform.forward;
                PlayHitSparkVFX(eventType, fallbackContact, fallbackDir);
            }

            // ── Motion Blur 상태 전환 ────────────────────────────────────────
            // blurController 가 없으면 (AI 등) 조용히 skip
            HandleBlurStateTransition(eventType);
        }

        // ════════════════════════════════════════════════════════════════════
        //  Motion Blur 상태 전환 헬퍼
        //
        //  두 가지 역할을 동시에 수행합니다.
        //
        //  [1] BlurState 전환 (하드코딩 — 공격/이동 상태 머신)
        //      HitBoxEnable → Attack, Action_Ended → Idle 등
        //      게임플레이 상태를 반영하는 BlurState 전환은 코드가 가장 명확합니다.
        //
        //  [2] ICameraEffectReceiver Pulse 트리거 (데이터 드리븐)
        //      Hit_Confirmed, Hit_From_Front 등 타격/피격 피드백 이벤트는
        //      BlurEventResponseSO 의 pulseDefinitions 에서 수치를 읽습니다.
        //      SO 에 등록되지 않은 이벤트는 Pulse 없이 무시됩니다.
        //
        //  하위 클래스(PlayerEffectsManager 등)에서 override 가능합니다.
        // ════════════════════════════════════════════════════════════════════
        protected virtual void HandleBlurStateTransition(global::AnimationEventType eventType)
        {
            if (blurController == null) return;

            // ── [1] BlurState 전환 (상태 머신) ───────────────────────────────
            switch (eventType)
            {
                case global::AnimationEventType.HitBoxEnable:
                    blurController.SetBlurState(ObjectMotionBlurController.BlurState.Attack);
                    break;

                case global::AnimationEventType.HitBoxDisable:
                case global::AnimationEventType.Action_Ended:
                    blurController.SetBlurState(ObjectMotionBlurController.BlurState.Idle);
                    break;

                case global::AnimationEventType.Charge_Started:
                    blurController.SetBlurState(ObjectMotionBlurController.BlurState.Aim);
                    break;

                case global::AnimationEventType.Charge_Ended:
                    blurController.SetBlurState(ObjectMotionBlurController.BlurState.HeavyAttack);
                    break;
            }

            // ── [2] ICameraEffectReceiver Pulse 트리거 (SO 드리븐) ────────────
            // BlurEventResponseSO 에 등록된 이벤트라면 Pulse 를 발동합니다.
            // 등록 안 된 이벤트는 ReceiveCameraEffectEvent 내부에서 조용히 무시됩니다.
            (blurController as TDA.Cameras.ICameraEffectReceiver)
                ?.ReceiveCameraEffectEvent(eventType);
        }

        // ════════════════════════════════════════════════════════════════════
        //  Blur 상태를 외부(로코모션 등)에서 직접 세팅하는 공개 API
        // ════════════════════════════════════════════════════════════════════
        public void SetBlurState(ObjectMotionBlurController.BlurState state)
        {
            if (blurController != null)
                blurController.SetBlurState(state);
        }

        public bool HasBlurController => blurController != null;

        // ══════════════════════════════════════════════════════════════════════
        //  피격 VFX 처리 (Damage_Calculated 경로)
        // ══════════════════════════════════════════════════════════════════════

        private void HandleDamageCalculatedVFX()
        {
            // 1. 접촉 지점 결정
            Vector3 contactPoint = (lastContactPoint != Vector3.zero)
                ? lastContactPoint
                : transform.position + new Vector3(0f, 1.2f, 0f);

            // 2. Blood VFX (기존 방식 유지)
            PlayBloodSplatterVFX(contactPoint);

            // 3. HitSpark — deflectDir 계산 (기존 로직 완전 보존)
            Vector3 deflectDir = (lastAttackerPosition != Vector3.zero)
                ? (transform.position - lastAttackerPosition).normalized
                : (contactPoint - transform.position).normalized;  // fallback

            // 4. 기본 HitSpark 이벤트 타입으로 SO 조회 후 GPU 방출
            PlayHitSparkVFX(
                global::AnimationEventType.VFX_HitSpark_Default,
                contactPoint,
                deflectDir);

            // 5. 캐시 초기화
            lastContactPoint = Vector3.zero;
            lastAttackerPosition = Vector3.zero;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Public VFX API
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// HitSpark VFX 를 SO 드리븐으로 재생합니다.
        ///
        /// 호출 경로:
        ///   [L3 CombatManager] → [L4 CharacterEventManager.NotifyAnimationEvent]
        ///   → [L4 CharacterEffectsManager.OnAnimationEventReceived]
        ///   → [이 메서드] → [ParrySparkGPUManager.EmitSparks]
        ///
        /// ⚠️ CombatManager 에서 직접 이 메서드를 호출하지 마세요.
        /// </summary>
        /// <param name="eventType">VFX_HitSpark_* 계열 AnimationEventType</param>
        /// <param name="contactPoint">타격 접촉점 (World Space)</param>
        /// <param name="deflectDirection">스파크 방출 방향 (World Space)</param>
        public virtual void PlayHitSparkVFX(
            global::AnimationEventType eventType,
            Vector3 contactPoint,
            Vector3 deflectDirection)
        {
            // SO 레지스트리에서 데이터 조회
            HitSparkVFXData data = vfxRegistry != null
                ? vfxRegistry.GetData(eventType)
                : null;

            // 레지스트리 미등록 시 WorldCharacterEffectsManager 폴백
            if (data == null)
            {
                // 기존 WorldCharacterEffectsManager 의 hitsparkVFX 폴백 유지
                if (WorldCharacterEffectsManager.Instance != null &&
                    WorldCharacterEffectsManager.Instance.hitsparkVFX != null)
                {
                    if (deflectDirection == Vector3.zero)
                        deflectDirection = transform.forward;
                    Quaternion rot = Quaternion.LookRotation(deflectDirection);
                    Instantiate(
                        WorldCharacterEffectsManager.Instance.hitsparkVFX,
                        contactPoint, rot);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                else
                {
                    Debug.LogWarning(
                        $"[CharacterEffectsManager] '{gameObject.name}': " +
                        $"eventType={eventType} 에 대한 HitSparkVFXData 가 없고 " +
                        $"WorldCharacterEffectsManager.hitsparkVFX 도 null 입니다. " +
                        $"HitSparkVFXRegistry 에 항목을 등록하세요.", this);
                }
#endif
                return;
            }

            // GPU 매니저 프리팹 없으면 경고 후 종료
            if (gpuSparkPrefab == null)
            {
                Debug.LogWarning(
                    $"[CharacterEffectsManager] gpuSparkPrefab 이 null 입니다. " +
                    $"인스펙터에서 ParrySparkGPUManager 프리팹을 연결하세요.", this);
                return;
            }

            // GPU 파티클 오브젝트 생성 후 EmitSparks 위임
            Vector3 burstDir = (deflectDirection == Vector3.zero)
                ? transform.forward
                : deflectDirection;

            ParrySparkGPUManager instance = Instantiate(gpuSparkPrefab, contactPoint, Quaternion.identity);
            instance.EmitSparks(data, contactPoint, burstDir);
        }

        /// <summary>
        /// Blood Splatter VFX 재생 (기존 방식 완전 보존).
        /// </summary>
        public void PlayBloodSplatterVFX(Vector3 contactPoint)
        {
            if (bloodSplatterVFX != null)
            {
                Instantiate(bloodSplatterVFX, contactPoint, Quaternion.identity);
            }
            else if (WorldCharacterEffectsManager.Instance != null &&
                     WorldCharacterEffectsManager.Instance.bloodSplatterVFX != null)
            {
                Instantiate(
                    WorldCharacterEffectsManager.Instance.bloodSplatterVFX,
                    contactPoint, Quaternion.identity);
            }
        }

        // ── 공격자 정보 캐싱 (TakeDamageEffect 에서 호출) ───────────────────

        /// <summary>
        /// TakeDamageEffect.ProcessEffect() 에서 호출하여 공격자 위치를 캐싱합니다.
        /// CharacterEventManager.NotifyAnimationEvent(Damage_Calculated) 발행 이전에
        /// 반드시 호출되어야 합니다.
        /// </summary>
        public void CacheAttackerInfo(Vector3 contactPoint, Vector3 attackerPosition)
        {
            lastContactPoint = contactPoint;
            lastAttackerPosition = attackerPosition;
        }

        // ── 이펙트 처리 ──────────────────────────────────────────────────────

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
    }
}