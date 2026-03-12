using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character
{
    /// <summary>
    /// [L3 Domain] 플레이어와 AI 캐릭터가 공유하는 전투 물리 및 타겟팅 뼈대 클래스입니다.
    /// [P4] 규약에 따라 IAnimationEventListener를 구현하여 이벤트 방송국으로부터 신호를 수신하고,
    /// 서버 권위형(Server Authority)으로 히트박스와 전투 상태를 통제합니다.
    /// [Funnel 연동] 실제 공격 애니메이션 실행은 직접 하지 않고 L4(AnimationManager)로 위임합니다.
    /// </summary>
    public class CharacterCombatManager : NetworkBehaviour, IAnimationEventListener
    {
        [Header("Core References")]
        protected CharacterManager character;

        [Header("Last Attack Animation Performed")]
        public string lastAttackAnimationPerformed;
        public int lastAttackAnimationPerformedHash;

        [Header("Attack Type")]
        public global::AttackType currentAttackType;

        [Header("Attack Target")]
        public CharacterManager currentTarget;

        [Header("Lock On Transform")]
        public Transform lockOnTransform;

        [Header("Combat Status (Networked)")]
        public NetworkVariable<bool> isAttacking = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> canCombo = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Header("Physics Execution")]
        protected List<DamageCollider> damageColliders = new List<DamageCollider>();

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
            damageColliders.AddRange(GetComponentsInChildren<DamageCollider>(true));
        }

        protected virtual void OnEnable()
        {
            CharacterEventManager eventManager = GetComponent<CharacterEventManager>();
            if (eventManager != null)
            {
                eventManager.OnAnimationEventTriggered += OnAnimationEventReceived;
            }
        }

        protected virtual void OnDisable()
        {
            CharacterEventManager eventManager = GetComponent<CharacterEventManager>();
            if (eventManager != null)
            {
                eventManager.OnAnimationEventTriggered -= OnAnimationEventReceived;
            }
        }

        /// <summary>
        /// [P4] 방송국(CharacterEventManager)에서 신호가 왔을 때 히트박스 개폐를 집행합니다.
        /// </summary>
        public virtual void OnAnimationEventReceived(AnimationEventType eventType)
        {
            if (eventType == AnimationEventType.HitBoxEnable)
            {
                // [NGO 규약] 물리 판정은 서버에서만 집행
                if (IsServer)
                {
                    isAttacking.Value = true;
                    foreach (var collider in damageColliders)
                    {
                        collider.EnableDamageCollider();
                    }
                }
            }
            else if (eventType == AnimationEventType.HitBoxDisable)
            {
                if (IsServer)
                {
                    isAttacking.Value = false;
                    foreach (var collider in damageColliders)
                    {
                        collider.DisableDamageCollider();
                    }
                }
            }
            else if (eventType == AnimationEventType.Action_Ended)
            {
                // 열려있던 모든 전투 상태 플래그를 정화(Cleanup)
                if (IsServer)
                {
                    isAttacking.Value = false;
                    canCombo.Value = false;
                    foreach (var collider in damageColliders)
                    {
                        collider.DisableDamageCollider();
                    }
                }
            }
        }

        public virtual void SetTarget(CharacterManager newTarget)
        {
            if (character.IsOwner)
            {
                if (newTarget != null)
                {
                    currentTarget = newTarget;
                    character.characterNetworkManager.currentTargetNetworkObjectID.Value = newTarget.GetComponent<NetworkObject>().NetworkObjectId;
                }
                else
                {
                    currentTarget = null;
                    character.characterNetworkManager.currentTargetNetworkObjectID.Value = 0;
                }
            }
        }

        // =========================================================================================
        // [신규 아키텍처: Funnel 패턴] 공격 로직 위임 (WeaponItemActionSO와 연동)
        // =========================================================================================
        /// <summary>
        /// [TO-BE 구조] WeaponItemActionSO에 정의된 ActionID를 매개변수로 받아,
        /// 직접 애니메이터를 찌르지 않고 L4 깔때기(PlayTargetActionFunnel)로 실행을 완벽히 위임합니다.
        /// </summary>
        public virtual void PerformWeaponAction(ActionID actionID, global::AttackType attackType)
        {
            if (character.isPerformingAction) return;

            // 상태 캐싱 (AI나 콤보 분기에서 사용)
            currentAttackType = attackType;
            lastAttackAnimationPerformedHash = (int)actionID;

            // ⭕ 시각적 실행은 L4 애니메이션 매니저에게 '위임'합니다!
            // 인자: (액션인덱스, isPerformingAction=true, applyRootMotion=true)
            character.characterAnimationManager.PlayTargetActionFunnel((int)actionID, true, true);
        }
    }
}