using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.Core.Events;
using TDA.World;

namespace TDA.Character
{
    // =========================================================================
    // [Phase3] HitConfirmedData
    // 위치: namespace TDA.Character 안, class 바깥
    // MeleeWeaponDamageCollider 에서는 using TDA.Character; 로 접근합니다.
    // =========================================================================
    public struct HitConfirmedData
    {
        public CharacterManager victim;
        public Vector3 contactPoint;
        public float damageDealt;
        public bool wasPoiseBreak;
        public AttackType attackType;
        public DefenseResult defenseResult;
    }

    /// <summary>
    /// [L3 Domain] 플레이어와 AI 캐릭터가 공유하는 전투 물리 및 타겟팅 뼈대 클래스입니다.
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
        public NetworkVariable<bool> isAttacking = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<bool> canCombo = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
                eventManager.OnAnimationEventTriggered += OnAnimationEventReceived;
        }

        protected virtual void OnDisable()
        {
            CharacterEventManager eventManager = GetComponent<CharacterEventManager>();
            if (eventManager != null)
                eventManager.OnAnimationEventTriggered -= OnAnimationEventReceived;
        }

        public virtual void OnAnimationEventReceived(AnimationEventType eventType)
        {
            if (eventType == AnimationEventType.HitBoxEnable)
            {
                if (IsServer)
                {
                    isAttacking.Value = true;
                    foreach (var col in damageColliders) col.EnableDamageCollider();
                }
            }
            else if (eventType == AnimationEventType.HitBoxDisable)
            {
                if (IsServer)
                {
                    isAttacking.Value = false;
                    foreach (var col in damageColliders) col.DisableDamageCollider();
                }
            }
            else if (eventType == AnimationEventType.Action_Ended)
            {
                if (IsServer)
                {
                    isAttacking.Value = false;
                    canCombo.Value = false;
                    foreach (var col in damageColliders) col.DisableDamageCollider();
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
                    character.characterNetworkManager.currentTargetNetworkObjectID.Value =
                        newTarget.GetComponent<NetworkObject>().NetworkObjectId;
                }
                else
                {
                    currentTarget = null;
                    character.characterNetworkManager.currentTargetNetworkObjectID.Value = 0;
                }
            }
        }

        public virtual void PerformWeaponAction(ActionID actionID, global::AttackType attackType)
        {
            if (character.isPerformingAction) return;
            currentAttackType = attackType;
            lastAttackAnimationPerformedHash = (int)actionID;
            character.characterAnimationManager.PlayTargetActionFunnel((int)actionID, true, true);
        }

        // =========================================================================================
        // [Phase3] OnHitConfirmed
        // =========================================================================================
        public virtual void OnHitConfirmed(HitConfirmedData hitData)
        {
            if (!character.IsOwner) return;

            if (hitData.defenseResult == DefenseResult.Blocked ||
                hitData.defenseResult == DefenseResult.Parried)
                return;

            character.characterEventManager?.NotifyAnimationEvent(
                AnimationEventType.Hit_Confirmed, "OnHitConfirmed");

            bool isHeavyHit = hitData.wasPoiseBreak
                           || hitData.attackType == AttackType.HeavyAttack01
                           || hitData.attackType == AttackType.HeavyAttack02
                           || hitData.attackType == AttackType.ChargeAttack01
                           || hitData.attackType == AttackType.ChargeAttack02;

            if (isHeavyHit)
                character.characterEventManager?.NotifyAnimationEvent(
                    AnimationEventType.Hit_Confirmed_Heavy, "OnHitConfirmed.Heavy");

            if (WorldCameraManager.Instance != null)
            {
                WorldCameraManager.Instance.BroadcastCameraEvent(
                    AnimationEventType.Hit_Confirmed, "OnHitConfirmed");
                if (isHeavyHit)
                    WorldCameraManager.Instance.BroadcastCameraEvent(
                        AnimationEventType.Hit_Confirmed_Heavy, "OnHitConfirmed.Heavy");
            }
        }
    }
}