// =============================================================================
// CharacterNetworkManager.cs  |  TDA Project
// Layer  : Data — NGO NetworkVariable 백본 라우터
// 수정 이력:
//   [신규] currentPoise / maxPoise NetworkVariable<float> 추가 (Owner Write)
//   [신규] NotifyTheServerOfCharacterDamageServerRpc에 poiseDamage 인자 추가
//   [신규] ProcessCharacterDamageFromServer — poiseDamage / contactPoint 반영
//   [유지] 기존 모든 NetworkVariable / RPC / 이벤트 / 주석 완전 보존
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using SG;
using Unity.Netcode;
using UnityEngine;
using TDA.Core.Events; // [P0-01 신규 추가] 애니메이터 파라미터 해시 통제용

namespace TDA.Character
{
    /// <summary>
    /// [L2 Router] 캐릭터의 전역 네트워크 상태를 동기화하는 백본(Backbone) 라우터 계층입니다.
    /// [P3] 모션 워핑을 위한 메타데이터 전파 RPC(가상 메서드)가 새롭게 포함되어 있으며,
    /// 게임 내 핵심 변수(체력, 위치, 플래그, 장비)를 서버 권위 혹은 오너 권위 하에 동기화합니다.
    /// </summary>
    public class CharacterNetworkManager : NetworkBehaviour
    {
        #region [References] 매니저 참조 및 이벤트
        protected CharacterManager character;
        protected CharacterIKController characterIKController;

        // [이벤트 스위치 보드] 상태 변화를 UI나 다른 로컬 매니저들에게 알리기 위한 공용 델리게이트
        public event Action OnCharacterStateChanged;
        #endregion

        #region [Network Variables] 기본 상태 및 물리 동기화
        [Header("Status")]
        public NetworkVariable<bool> isDead = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        public NetworkVariable<bool> isUsingRightHand = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        [Header("Position & Rotation")]
        public NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        public NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        // 지연 시간(Latency) 보간을 위한 SmoothDamp 설정
        public Vector3 networkPositionVelocity;
        public float networkPositionSmoothTime = 0.1f;
        public float networkRotationSmoothTime = 0.1f;
        #endregion

        #region [Network Variables] 애니메이터 및 전투 플래그 동기화
        [Header("Animator Blend Tree")]
        public NetworkVariable<float> animatorHorizontalMovement = new NetworkVariable<float>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<float> animatorVerticalMovement = new NetworkVariable<float>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<float> animatorMoveAmountMovement = new NetworkVariable<float>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Target (Metadata)")]
        // 내가 노리고 있는 상대의 고유 NetworkObjectId를 전파하여 시선 처리를 돕습니다.
        public NetworkVariable<ulong> currentTargetNetworkObjectID = new NetworkVariable<ulong>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Combat Flags")]
        public NetworkVariable<bool> isLockedOn = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> isSprinting = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> isJumping = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> isChargingAttack = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        #endregion

        #region [Network Variables] 자원 및 스탯 동기화 (Stats & Resources)
        [Header("Stats (Level & Attributes)")]
        public NetworkVariable<int> endurance = new NetworkVariable<int>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> vitality = new NetworkVariable<int>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Madness (Horror System)")]
        public NetworkVariable<float> currentMadness = new NetworkVariable<float>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<float> maxMadness = new NetworkVariable<float>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Resources (HP & Stamina)")]
        public NetworkVariable<float> currentStamina = new NetworkVariable<float>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> maxStamina = new NetworkVariable<int>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> maxHealth = new NetworkVariable<int>(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // ── [신규] 포이즈 (Poise) ──────────────────────────────────────────────
        [Header("Poise (Stagger System)")]
        /// <summary>
        /// [신규] 현재 포이즈 값.
        /// 0 이하가 되면 경직(Guard_Break_Stun)이 발생하고
        /// CombatStanceState.Tick()에서 GroggyState로 전환됩니다.
        /// Owner Write: 로컬에서 연산 후 서버에 동기화합니다.
        /// </summary>
        public NetworkVariable<float> currentPoise = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        /// <summary>
        /// [신규] 최대 포이즈 값.
        /// CharacterStatsManager.CalculatePoiseBasedOnStats()로 계산합니다.
        /// endurance 스탯 × 0.5 + 무기/방어구 보너스 합산.
        /// </summary>
        public NetworkVariable<float> maxPoise = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        // ───────────────────────────────────────────────────────────────────────
        #endregion

        #region [Network Variables] IK 및 상호작용 동기화
        [Header("IK Sync (Server Authority)")]
        // [최적화] 잡고 있는 물체의 Vector3를 계속 동기화하지 않고, 물체의 NetworkObjectId만 알려
        // 클라이언트 각자가 로컬에서 손 위치를 역운동학(IK)으로 연결하도록 설계하여 패킷을 절약합니다.
        public NetworkVariable<ulong> currentRightHandGrabbedObjectID = new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        #endregion

        #region [Lifecycle] 초기화 및 업데이트
        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
            characterIKController = GetComponent<CharacterIKController>();
        }

        // =========================================================================================
        // [P0-01 신규 추가] 위치 동기화 및 애니메이션(Strafe) 보간 로직 (Client-Side Prediction)
        // =========================================================================================
        protected virtual void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                // [내 캐릭터]
                // L3 도메인(LocomotionManager)에 의해 계산된 진짜 물리 위치를 매 프레임 서버로 전송 (Zero-latency)
                networkPosition.Value = transform.position;
                networkRotation.Value = transform.rotation;
            }
            else
            {
                // [타 캐릭터]
                // 서버로부터 수신된 위치로 로컬 트랜스폼을 보간(Interpolation)하여 끊김 현상(Snap) 방지
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    networkPosition.Value,
                    ref networkPositionVelocity,
                    networkPositionSmoothTime);

                // 회전 보간 속도 연산
                float rotSpeed = networkRotationSmoothTime > 0 ? (1f / networkRotationSmoothTime) : 15f;
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    networkRotation.Value,
                    Time.deltaTime * rotSpeed);

                // 타 클라이언트의 락온 상태를 기반으로 애니메이터의 isStrafing 파라미터를 갱신하여
                // 다른 플레이어가 내 화면에서 스케이트 타듯 움직이는 버그를 방지 (Hash 사용)
                if (character != null && character.animator != null)
                {
                    character.animator.SetBool(AnimatorParameterHash.isStrafing, isLockedOn.Value);
                }
            }
        }
        #endregion

        #region [P3] 모션 워핑 동기화 베이스 인터페이스 (Warping Portal)
        // =========================================================================================
        // [P3] 이 가상(Virtual) RPC 메서드들은 PlayerNetworkManager에서 오버라이드되어
        // 클라이언트 사이드 예측(Client-side Prediction)을 구동하는 '메타데이터 고속도로' 역할을 합니다.
        // =========================================================================================

        /// <summary>
        /// [P3] 클라이언트가 모션 워핑(에임 어시스트) 타겟 메타데이터를 서버에 알리는 관문입니다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public virtual void NotifyWarpAttackServerRpc(ulong targetId, int boneIndex)
        {
            if (IsServer)
            {
                NotifyWarpAttackClientRpc(targetId, boneIndex);
            }
        }

        /// <summary>
        /// [P3] 서버가 수신된 워핑 메타데이터를 세션 내 모든 유저에게 전파하는 관문입니다.
        /// 자식 클래스(PlayerNetworkManager)에서 오버라이드하여 핑 지연을 무시하는 로컬 예측 보간을 수행합니다.
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        public virtual void NotifyWarpAttackClientRpc(ulong targetId, int boneIndex)
        {
            // 베이스 클래스는 다형성(Polymorphism)을 위한 서명만 유지하며 아무 동작도 하지 않습니다.
        }
        #endregion

        #region [Logic] 상태 구독 및 이벤트 전파 (Health & Target Management)
        public void CheckHP(int oldHealth, int newHealth)
        {
            // 체력이 0 이하가 되면 로컬에서 사망 이벤트를 발동시킵니다.
            if (currentHealth.Value <= 0 && !isDead.Value)
            {
                StartCoroutine(character.ProcessDeathEvent());
            }

            // 오버힐링(최대 체력 초과) 방지 가드 로직
            if (character.IsOwner && currentHealth.Value > maxHealth.Value)
            {
                currentHealth.Value = maxHealth.Value;
            }

            // 상태 변화를 UI 등에 알립니다.
            OnCharacterStateChanged?.Invoke();
        }

        public void OnLockOnTargetIDChange(ulong oldId, ulong newId)
        {
            if (!IsOwner)
            {
                // 타 유저(프록시 객체)가 타겟을 바꿨을 때, 나도 동일한 대상을 바라보도록 로컬 참조를 연결해 줍니다.
                if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(newId, out NetworkObject netObj))
                {
                    character.characterCombatManager.currentTarget = netObj.GetComponent<CharacterManager>();
                }
            }
        }

        public void OnIsLockedOnChange(bool old, bool isLockedOn)
        {
            if (!isLockedOn)
            {
                character.characterCombatManager.currentTarget = null;
            }
            OnCharacterStateChanged?.Invoke();
        }

        public void OnIsChargingAttackChanged(bool oldStatus, bool newStatus)
        {
            character.animator.SetBool("isChargingAttack", isChargingAttack.Value);
        }
        #endregion

        #region [RPC] 애니메이션 강제 동기화 (Animation Synchronization)
        // 일반 행동(Action) 애니메이션
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void NotifyTheServerOfActionAnimationServerRpc(ulong clientID, int animationHash, bool applyRootMotion)
        {
            if (IsServer) PlayActionAnimationFromAllClientsClientRpc(clientID, animationHash, applyRootMotion);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void PlayActionAnimationFromAllClientsClientRpc(ulong clientID, int animationHash, bool applyRootMotion)
        {
            // 이 명령을 보낸 본인(Owner)은 이미 선제적으로 애니메이션을 틀었으므로 중복 재생을 방지합니다.
            if (clientID != NetworkManager.Singleton.LocalClientId)
            {
                PerformActionAnimationFromServer(animationHash, applyRootMotion);
            }
        }

        private void PerformActionAnimationFromServer(int animationHash, bool applyRootMotion)
        {
            character.applyRootMotion = applyRootMotion;
            character.animator.CrossFade(animationHash, 0.2f);
        }

        // 공격 전용 애니메이션 (피격 판정 등의 선행 분기가 필요할 수 있어 분리됨)
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void NotifyTheServerOfAttackActionAnimationServerRpc(ulong clientID, int animationHash, bool applyRootMotion)
        {
            if (IsServer) PlayAttackActionAnimationFromAllClientsClientRpc(clientID, animationHash, applyRootMotion);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void PlayAttackActionAnimationFromAllClientsClientRpc(ulong clientID, int animationHash, bool applyRootMotion)
        {
            if (clientID != NetworkManager.Singleton.LocalClientId)
            {
                PerformAttackActionAnimationFromServer(animationHash, applyRootMotion);
            }
        }

        private void PerformAttackActionAnimationFromServer(int animationHash, bool applyRootMotion)
        {
            character.applyRootMotion = applyRootMotion;
            character.animator.CrossFade(animationHash, 0.2f);
        }
        #endregion

        #region [RPC] 전투 및 데미지 동기화 (Combat & Damage Synchronization)
        // --- 데미지 처리 ---
        // [에러/경고 해결 CS0618] 최신 NGO(Unity 6) 규약에 맞춰 ServerRpc 속성 변경

        /// <summary>
        /// 무기 충돌 시 MeleeWeaponDamageCollider(L3)에서 서버로 피격 데이터를 전송합니다.
        /// [신규] poiseDamage 인자 추가 — 포이즈 시스템 연동.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void NotifyTheServerOfCharacterDamageServerRpc(
            ulong damageCharacterID,
            ulong characterCausingDamageID,
            float physicalDamage,
            float elementalDamage,
            float poiseDamage,          // [신규] 포이즈 데미지
            float angleHitFrom,
            float contactPointX,
            float contactPointY,
            float contactPointZ)
        {
            if (IsServer)
            {
                NotifyTheServerOfCharacterDamageClientRpc(
                    damageCharacterID, characterCausingDamageID,
                    physicalDamage, elementalDamage, poiseDamage,
                    angleHitFrom, contactPointX, contactPointY, contactPointZ);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void NotifyTheServerOfCharacterDamageClientRpc(
            ulong damageCharacterID,
            ulong characterCausingDamageID,
            float physicalDamage,
            float elementalDamage,
            float poiseDamage,
            float angleHitFrom,
            float contactPointX,
            float contactPointY,
            float contactPointZ)
        {
            ProcessCharacterDamageFromServer(
                damageCharacterID, characterCausingDamageID,
                physicalDamage, elementalDamage, poiseDamage,
                angleHitFrom, contactPointX, contactPointY, contactPointZ);
        }

        /// <summary>
        /// 수신된 데미지 데이터를 바탕으로 타격 방향, 깎이는 수치 등을 캡슐화한 뒤,
        /// 시각 연출을 총괄하는 CharacterEffectsManager에 다형성 이펙트를 위임 호출합니다.
        /// [신규] poiseDamage / contactPoint가 TakeDamageEffect에 전달됩니다.
        /// </summary>
        public void ProcessCharacterDamageFromServer(
            ulong damageCharacterID,
            ulong characterCausingDamageID,
            float physicalDamage,
            float elementalDamage,
            float poiseDamage,
            float angleHitFrom,
            float contactPointX,
            float contactPointY,
            float contactPointZ)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    damageCharacterID, out NetworkObject damagedObj) &&
                NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    characterCausingDamageID, out NetworkObject causerObj))
            {
                CharacterManager damagedCharacter = damagedObj.GetComponent<CharacterManager>();
                CharacterManager characterCausingDamage = causerObj.GetComponent<CharacterManager>();

                TakeDamageEffect damageEffect = Instantiate(
                    WorldCharacterEffectsManager.Instance.takeDamageEffect);

                damageEffect.physicalDamage = physicalDamage;
                damageEffect.elementDamage = elementalDamage;
                damageEffect.poiseDamage = poiseDamage;          // [신규]
                damageEffect.angleHitFrom = angleHitFrom;
                damageEffect.contactPoint = new Vector3(contactPointX, contactPointY, contactPointZ);
                damageEffect.characterCausingDamage = characterCausingDamage;

                damagedCharacter.characterEffectsManager.ProcessInstantEffects(damageEffect);
            }
        }
        #endregion

        #region [RPC] IK 및 상호작용 동기화 (IK Synchronization)
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // 물체(ID)를 잡거나 놓을 때 클라이언트 단의 IK 관절을 업데이트하는 이벤트 구독
            currentRightHandGrabbedObjectID.OnValueChanged += OnGrabbedObjectChanged;

            // [P0-01 신규 추가] 네트워크 스폰 시 타 클라이언트의 초기 위치 강제 동기화 (Snap)
            if (!IsOwner)
            {
                transform.position = networkPosition.Value;
                transform.rotation = networkRotation.Value;
            }
        }

        public override void OnNetworkDespawn()
        {
            currentRightHandGrabbedObjectID.OnValueChanged -= OnGrabbedObjectChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// 네트워크 변수로 ID가 전파되면, 각 클라이언트가 로컬 환경에서 해당 물체를 찾아 자신의 손(IK)을 부착합니다.
        /// </summary>
        private void OnGrabbedObjectChanged(ulong oldID, ulong newID)
        {
            // 0이면 물건을 놓았다는 의미입니다.
            if (newID == 0)
            {
                characterIKController?.SetHandIKTarget(null);
                return;
            }

            // ID로 스폰된 오브젝트를 탐색하여 손을 뻗을 피벗(GripPoint)을 찾습니다.
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(newID, out NetworkObject netObj))
            {
                InteractableItem grabbable = netObj.GetComponent<InteractableItem>();
                if (grabbable != null)
                {
                    characterIKController?.SetHandIKTarget(grabbable.gripPoint);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void NotifyServerOfGrabActionServerRpc(ulong objectID)
        {
            if (!IsServer) return;
            currentRightHandGrabbedObjectID.Value = objectID; // 서버에서 상태 갱신
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void NotifyServerOfReleaseActionServerRpc()
        {
            if (!IsServer) return;
            currentRightHandGrabbedObjectID.Value = 0;
        }
        #endregion
    }
}