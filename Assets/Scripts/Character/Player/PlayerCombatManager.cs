using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.Character.Player;
using TDA.Items;
using TDA.Items.WeaponItemActions;
using TDA.Core.Events;

namespace TDA.Character
{
    /// <summary>
    /// [P1 & P3] 플레이어의 전투 로직과 모션 워핑(에임 어시스트)의 두뇌 역할을 담당하는 핵심 도메인 매니저입니다.
    ///
    /// [아키텍처 설계 철학]
    /// 1. 이벤트 체인(Event Chaining): 애니메이션 감시자의 1차 신호를 받아 데미지/무적 연산을 마친 뒤,
    ///    2차 이벤트를 발송하여 실행 순서의 무결성을 보장합니다.
    /// 2. 다중 상속 회피: NetworkBehaviour 상속을 유지하기 위해 IAnimationEventListener 인터페이스를 구현합니다.
    /// 3. 클리핑 방지 (P3): 에임 어시스트를 통해 적의 급소를 찾고, 무기 사거리를 고려한 완벽한 안전 좌표를 도출합니다.
    /// </summary>
    public class PlayerCombatManager : CharacterCombatManager
    {
        PlayerManager player;

        public WeaponItem currentWeaponBeingUsed;

        [Header("Flags")]
        public bool canComboWithMainHandWeapon = false;
        public bool canComboWithOffHandWeapon = false;

        [Header("Lock On Input")]
        [SerializeField] bool lockOn_Input;
        [SerializeField] bool lockOn_Left_Input;
        [SerializeField] bool lockOn_Right_Input;
        private Coroutine lockOnCoroutine;

        [Header("Motion Warping (P3)")]
        [Tooltip("에임 어시스트 스피어캐스트가 탐색할 적의 레이어 마스크입니다.")]
        [SerializeField] LayerMask aimAssistLayerMask = ~0; // 기본적으로 타겟 레이어 설정 필요

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();
        }

        // =========================================================================================
        // [P1] 이벤트 생명주기 관리 및 수신부
        // =========================================================================================

        protected override void OnEnable()
        {
            base.OnEnable();
            // 부모(CharacterCombatManager)에서 이미 이벤트 방송국을 구독하고 있으므로
            // 자식 클래스에서는 base.OnEnable()만 호출하여 이중 구독을 방지합니다.
        }

        protected override void OnDisable()
        {
            base.OnDisable();
        }

        /// <summary>
        /// [P4] IAnimationEventListener 인터페이스 구현부.
        /// 방송국에서 송출된 이벤트 Enum(타입 안정성)을 받아 능동적으로 전투 플래그를 제어합니다.
        /// </summary>
        public override void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            // [방어 로직] HitBoxEnable, HitBoxDisable 등 전투 판정 콜라이더 개폐는 
            // 전부 부모 클래스(CharacterCombatManager)에서 완벽히 처리하므로 base를 호출해 줍니다.
            base.OnAnimationEventReceived(eventType);

            // 자식 클래스인 여기서는 '플레이어 조작에만 관련된' 콤보 플래그만 통제합니다.
            if (eventType == global::AnimationEventType.ComboEnable)
            {
                EnableCombo();
            }
            else if (eventType == global::AnimationEventType.ComboDisable || eventType == global::AnimationEventType.Action_Ended)
            {
                DisableCombo();
            }
        }

        private void EnableCombo()
        {
            if (player.playerNetworkManager.isUsingRightHand.Value)
            {
                canComboWithMainHandWeapon = true;
            }
            else if (player.playerNetworkManager.isUsingLeftHand.Value)
            {
                // 향후 방패 치기나 쌍수 무기 연계를 위한 오프핸드 콤보 로직 보강
                canComboWithOffHandWeapon = true;
            }
        }

        private void DisableCombo()
        {
            canComboWithMainHandWeapon = false;
            canComboWithOffHandWeapon = false;
        }

        // =========================================================================================
        // [P3] 에임 어시스트 및 모션 워핑 연산 두뇌
        // =========================================================================================

        /// <summary>
        /// [P3] 공격 시작 시 호출되어 최적의 타겟(급소)을 찾고 클리핑 없는 워핑 목표 좌표를 수학적으로 산출합니다.
        /// </summary>
        /// <param name="warpPosition">산출된 클리핑 방지 목표 좌표</param>
        /// <param name="warpRotation">타겟을 완벽히 마주보는 목표 회전값</param>
        /// <param name="targetNetworkId">NGO 동기화를 위한 대상의 고유 ID</param>
        /// <param name="boneIndex">대상의 피격 신체 부위 인덱스</param>
        /// <returns>적합한 타겟을 찾았을 경우 true 반환</returns>
        public bool FindBestWarpTarget(out Vector3 warpPosition, out Quaternion warpRotation, out ulong targetNetworkId, out int boneIndex)
        {
            warpPosition = Vector3.zero;
            warpRotation = Quaternion.identity;
            targetNetworkId = 0;
            boneIndex = -1;

            if (Camera.main == null) return false;

            // 1. 에임 어시스트 스캔 (Viewport SphereCast)
            // 호러 게임의 어두운 시야를 고려하여 점(Ray)이 아닌 구체(Sphere)로 후한 판정을 투사합니다.
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float sphereCastRadius = 0.5f;
            float maxScanDistance = 15f;

            if (Physics.SphereCast(ray, sphereCastRadius, out RaycastHit hit, maxScanDistance, aimAssistLayerMask))
            {
                // 대상의 NetworkObject 추출
                NetworkObject targetNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                if (targetNetObj == null) return false;

                // 대상의 Animator 추출
                Animator targetAnimator = hit.collider.GetComponentInParent<Animator>();
                if (targetAnimator == null) return false;

                // 2. 급소(Bone) 정밀 추출
                // 몬스터의 렌더링 발밑(Root)이 아닌 가슴(Chest) 부위를 타격 목표로 설정하여 정밀도를 올립니다.
                Transform targetBone = targetAnimator.GetBoneTransform(HumanBodyBones.Chest);
                if (targetBone == null)
                {
                    targetBone = targetAnimator.transform; // 뼈대를 못 찾을 경우 Fallback
                }

                // 3. 클리핑(Clipping) 방지 수학 연산 (가장 핵심)
                // Bone 좌표로 그대로 텔레포트하면 적의 몸을 뚫어버리므로, 방향을 구한 뒤 무기 사거리만큼 뒤로 빼줍니다(Offset).
                Vector3 directionToTarget = (targetBone.position - transform.position).normalized;

                // 현재 무기의 고유 리치 (WeaponItemAction 확장 필드 적용)
                float attackRange = 1.5f;
                if (currentWeaponBeingUsed != null && currentWeaponBeingUsed.oh_RB_Action != null)
                {
                    attackRange = currentWeaponBeingUsed.oh_RB_Action.weaponAttackRange;
                }

                // 최종 워핑 좌표 = 타겟 뼈대 위치 - (방향 벡터 * 무기 사거리)
                warpPosition = targetBone.position - (directionToTarget * attackRange);
                // 최종 워핑 회전 = 대상을 완벽히 마주보는 역방향 회전
                warpRotation = Quaternion.LookRotation(directionToTarget);

                // 4. 결과 반환 및 메타데이터 추출 (이후 PlayerNetworkManager로 패스됨)
                targetNetworkId = targetNetObj.NetworkObjectId;
                boneIndex = (int)HumanBodyBones.Chest;

                return true;
            }

            return false;
        }

        // =========================================================================================
        // [기존 도메인 기능 완벽 보존]
        // =========================================================================================

        internal void OnRBInputReceived()
        {
            if (player.playerInventoryManager.currentRightHandWeapon != null)
            {
                PerformWeaponBasedAction
                (player.playerInventoryManager.currentRightHandWeapon.oh_RB_Action,
                player.playerInventoryManager.currentRightHandWeapon
                );
            }
        }

        internal void OnRTInputReceived()
        {
            if (player.playerInventoryManager.currentRightHandWeapon != null)
            {
                PerformWeaponBasedAction
                (player.playerInventoryManager.currentRightHandWeapon.oh_RT_Action,
                player.playerInventoryManager.currentRightHandWeapon
                );
            }
        }

        internal void OnLockOnInputReceived()
        {
            // [안전망] PlayerCamera가 할당되지 않아 발생하는 Silent Crash를 방어합니다.
            if (player.playerCamera == null)
            {
                player.playerCamera = FindFirstObjectByType<PlayerCamera>();
                if (player.playerCamera == null)
                {
                    Debug.Log("<color=red>[LockOn]</color> 락온 실패: 씬에 PlayerCamera 컴포넌트가 존재하지 않습니다!");
                    return;
                }
            }

            if (player.playerNetworkManager.isLockedOn.Value)
            {
                // 이미 락온 중이므로 언락(Unlock) 수행
                Debug.Log("<color=yellow>[LockOn]</color> 락온 해제 (Unlock)");
                player.playerCamera.ClearLockOnTargets();
                player.playerNetworkManager.isLockedOn.Value = false;
                SetTarget(null); // 타겟 명시적 초기화

                // 진행 중이던 탐색 코루틴 강제 중지
                if (lockOnCoroutine != null)
                {
                    StopCoroutine(lockOnCoroutine);
                    lockOnCoroutine = null;
                }
            }
            else
            {
                // 락온 시도
                Debug.Log("<color=yellow>[LockOn]</color> 주변 타겟 탐색 시작...");

                // [Try-Catch 에러 추적] 함수 내부에서 에러가 터져 코드가 멈추는 현상을 잡아냅니다.
                try
                {
                    player.playerCamera.HandleLocatingLockOnTargets();
                }
                catch (Exception e)
                {
                    Debug.Log($"<color=red>[LockOn Crash]</color> 타겟 탐색 중 치명적 에러 발생! \n원인: {e.Message}\n{e.StackTrace}");
                    return; // 에러가 나면 락온 성공/실패 판정을 건너뜁니다.
                }

                if (player.playerCamera.nearestLockOnTarget != null)
                {
                    Debug.Log($"<color=green>[LockOn]</color> 타겟 포착 성공: {player.playerCamera.nearestLockOnTarget.name}");
                    SetTarget(player.playerCamera.nearestLockOnTarget);
                    // 가장 가까운 타겟이 널이 아니면 현재 대상으로 락온
                    player.playerNetworkManager.isLockedOn.Value = true;
                }
                else
                {
                    Debug.Log("<color=orange>[LockOn]</color> 락온 실패: 조건을 만족하는 타겟이 주변에 없습니다.");
                }
            }
        }

        internal void OnLockOnSwitchTargetInputReceived(LockOnDirection direction)
        {
            if (player.playerCamera == null) return;

            try
            {
                if (direction == LockOnDirection.Left)
                {
                    // 이미 락온 됫다면 실행.
                    if (player.playerNetworkManager.isLockedOn.Value)
                    {
                        player.playerCamera.HandleLocatingLockOnTargets();
                        if (player.playerCamera.leftLockOnTarget != null)
                        {
                            SetTarget(player.playerCamera.leftLockOnTarget);
                        }
                    }
                }
                if (direction == LockOnDirection.Right)
                {
                    // 이미 락온 됫다면 실행.
                    if (player.playerNetworkManager.isLockedOn.Value)
                    {
                        player.playerCamera.HandleLocatingLockOnTargets();
                        if (player.playerCamera.rightLockOnTarget != null)
                        {
                            SetTarget(player.playerCamera.rightLockOnTarget);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log($"<color=red>[LockOn Switch Crash]</color> 타겟 변경 중 에러 발생: {e.Message}");
            }
        }

        public void PerformWeaponBasedAction(WeaponItemAction weaponAction, WeaponItem weaponPerformingAction)
        {
            if (!player.IsOwner || weaponAction == null)
                return;

            if (player.IsOwner)
            {
                // 액션 수행하기.
                weaponAction.AttemptToPerformAction(player, weaponPerformingAction);

                // 수행한 액션을 서버에 알리고, 그 후 서버가 다른 클라이언트에게 수행한 액션을 보여줌
                player.playerNetworkManager.NotifyTheServerOfWeaponActionServerRpc
                    (NetworkManager.Singleton.LocalClientId, weaponAction.actionID,
                    weaponPerformingAction.itemID);
            }
        }

        /// <summary>
        /// [버그 수정] 무기 공격 시 발생하는 스태미나 차감 공식입니다.
        /// </summary>
        public virtual void DrainStaminaBasedOnAttack()
        {
            if (!player.IsOwner)
            {
                Debug.LogWarning("[PlayerCombatManager] 스태미나 차감 시도 실패: 로컬 플레이어가 아닙니다.");
                return;
            }

            WeaponItem weaponToUse = currentWeaponBeingUsed;

            // [핵심 해결] 네트워크 변수(currentWeaponBeingUsed)의 동기화 1프레임 지연으로 인한 
            // Null 발생 및 스태미나 차감 무시(Skip) 버그를 원천 차단합니다.
            // 무기가 null일 경우 플레이어의 로컬 인벤토리에서 직접 끌어와서 즉시 계산합니다.
            if (weaponToUse == null)
            {
                if (player.playerNetworkManager.isUsingRightHand.Value)
                {
                    weaponToUse = player.playerInventoryManager.currentRightHandWeapon;
                }
                else if (player.playerNetworkManager.isUsingLeftHand.Value)
                {
                    weaponToUse = player.playerInventoryManager.currentLeftHandWeapon;
                }
            }

            if (weaponToUse == null)
            {
                Debug.LogWarning("[PlayerCombatManager] 스태미나 차감 시도 실패: 참조할 무기가 없습니다.");
                return;
            }

            float staminaDeducted = 0;

            // 공격 타입에 따른 스태미나 차감량 전수 연산
            switch (currentAttackType)
            {
                case global::AttackType.LightAttack01:
                    staminaDeducted = weaponToUse.baseStaminaCost * weaponToUse.lightAttackStaminaCostMultiplier;
                    Debug.Log("[PlayerCombatManager] Light Attack 01 실행, 스태미나 차감");
                    break;

                case global::AttackType.LightAttack02:
                    staminaDeducted = weaponToUse.baseStaminaCost * weaponToUse.lightAttackStaminaCostMultiplier;
                    Debug.Log("[PlayerCombatManager] Light Attack 02 실행, 스태미나 차감");
                    break;

                case global::AttackType.HeavyAttack01:
                    staminaDeducted = weaponToUse.baseStaminaCost * weaponToUse.heavyAttackStaminaCostMultiplier;
                    Debug.Log("[PlayerCombatManager] Heavy Attack 01 실행, 스태미나 차감");
                    break;

                case global::AttackType.HeavyAttack02:
                    staminaDeducted = weaponToUse.baseStaminaCost * weaponToUse.heavyAttackStaminaCostMultiplier;
                    Debug.Log("[PlayerCombatManager] Heavy Attack 02 실행, 스태미나 차감");
                    break;

                case global::AttackType.ChargeAttack01:
                    staminaDeducted = weaponToUse.baseStaminaCost * weaponToUse.heavyAttackStaminaCostMultiplier * 1.5f;
                    Debug.Log("[PlayerCombatManager] Charge Attack 01 실행, 스태미나 대폭 차감");
                    break;

                case global::AttackType.ChargeAttack02:
                    staminaDeducted = weaponToUse.baseStaminaCost * weaponToUse.heavyAttackStaminaCostMultiplier * 1.5f;
                    Debug.Log("[PlayerCombatManager] Charge Attack 02 실행, 스태미나 대폭 차감");
                    break;

                default:
                    Debug.LogWarning("[PlayerCombatManager] 스태미나 차감 실패: 정의되지 않은 공격 타입입니다.");
                    break;
            }

            player.playerNetworkManager.currentStamina.Value -= Mathf.RoundToInt(staminaDeducted);
        }

        public override void SetTarget(CharacterManager newTarget)
        {
            base.SetTarget(newTarget);

            // 로컬플레이어가 하고 잇다면
            if (player.IsOwner)
            {
                player.playerCamera.SetLockCameraHeight();
            }
        }
    }
}