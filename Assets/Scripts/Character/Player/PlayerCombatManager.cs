using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.Character.Player;
using TDA.Items;
using TDA.Items.WeaponItemActions;
using TDA.Core.Events;
// 🚨 [Phase 1 고도화] 카메라 시스템 제어를 위한 네임스페이스 추가
using TDA.Cameras;
using TDA.World;

namespace TDA.Character
{
    /// <summary>
    /// [P1 & P3] 플레이어의 전투 로직과 모션 워핑(에임 어시스트)의 두뇌 역할을 담당하는 핵심 도메인 매니저입니다.
    ///
    /// [아키텍처 설계 철학]
    /// 1. 이벤트 체인(Event Chaining): 애니메이션 감시자의 1차 신호를 받아 데미지/무적 연산을 마친 뒤,
    ///    2차 이벤트를 발송하여 실행 순서의 무결성을 보장합니다.
    /// 2. 다중 상속 회피: NetworkBehaviour 상속을 유지하기 위해 IAnimationEventListener 인터페이스를 구현합니다.
    /// 3. 클리핑 방지 (P3): 에임 어시스트를 통해 적의 급소를 찾고, 무기 사거리 고려한 완벽한 안전 좌표를 도출합니다.
    /// 4. 제스처 궤적 연동 (P0-03): 마우스 드래그 궤적에 따른 방향성 공격과 콜라이더 정밀 제어를 수행합니다.
    /// </summary>
    public class PlayerCombatManager : CharacterCombatManager
    {
        PlayerManager player;

        public WeaponItem currentWeaponBeingUsed;

        [Header("Flags")]
        public bool canComboWithMainHandWeapon = false;
        public bool canComboWithOffHandWeapon = false;

        // =========================================================================================
        // [P1-2 신규] 카운터 기회 신호 시스템 (isCounterOpportunity)
        // AI 포이즈 파괴 시 TakeDamageEffect에서 호출되어 일정 시간 동안 카운터 기회를 활성화합니다.
        // isExecutionOpportunityActive와 OR 조건으로 연계 가능 (처형 진입 게이트)
        // =========================================================================================
        [Header("Counter Opportunity")]
        public bool isCounterOpportunity = false;
        [SerializeField] private float counterOpportunityDuration = 2.5f;
        private Coroutine counterOpportunityCoroutine;

        // =========================================================================================
        // 🚨 [Phase 1 고도화] 카메라 연동 데이터
        // =========================================================================================
        [Header("Camera & UI (Phase 1)")]
        [Tooltip("락온 시 카메라를 좌측 숄더뷰로 고정하기 위한 락온 전용 스탠스 SO")]
        public CameraStancePresetSO lockOnStanceSO;

        // [신규 추가] Seq SO — restorePreviousAngle 등 전체 시퀀스 설정을 담은 Tier2 에셋
        [Tooltip("락온 진입 시 재생할 카메라 시퀀스 SO (Seq_LockOn_Humanoid_SO를 여기에 연결)")]
        public CameraSequencePresetSO lockOnSequenceSO;

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
        // [P1 & P0-03] 이벤트 생명주기 관리 및 수신부 (Type-Safe Enum 기반 콜라이더 제어)
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
        /// [P4 & P0-03] IAnimationEventListener 인터페이스 구현부.
        /// 방송국에서 송출된 이벤트 Enum(타입 안정성)을 받아 능동적으로 전투 플래그 및 콜라이더를 제어합니다.
        /// </summary>
        public override void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            base.OnAnimationEventReceived(eventType);

            // [P0-03] Switch-Case 기반의 명확하고 확장성 높은 이벤트 처리
            switch (eventType)
            {
                // --- 콤보 제어 ---
                case global::AnimationEventType.ComboEnable:
                    EnableCombo();
                    break;

                case global::AnimationEventType.ComboDisable:
                case global::AnimationEventType.Action_Ended:
                    DisableCombo();
                    break;

                // --- 물리 판정 및 시각 효과(검기) 동기화 ---
                case global::AnimationEventType.HitBoxEnable:
                    if (player.playerEquipmentManager != null)
                    {
                        // 1프레임의 오차 없이 타격 판정 활성화
                        player.playerEquipmentManager.OpenDamageCollider();
                    }
                    if (player.characterEventManager != null)
                    {
                        // 판정이 열리는 동시에 시각적인 검기 이펙트(Trail) 출력
                        player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Trail_Enable_Smooth);
                    }
                    break;

                case global::AnimationEventType.HitBoxDisable:
                    if (player.playerEquipmentManager != null)
                    {
                        // 타격 프레임이 지나면 즉시 콜라이더 닫기
                        player.playerEquipmentManager.CloseDamageCollider();
                    }
                    if (player.characterEventManager != null)
                    {
                        // 검기 이펙트 끄기
                        player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Trail_Disable_Smooth);
                    }
                    break;

                // --- [P2-4 신규] 패링 윈도우 제어 ---
                // Parry_Window_Open 이벤트 수신 시 CharacterDefenseManager.isParryActive = true
                // Parry_Window_Close 이벤트 수신 시 CharacterDefenseManager.isParryActive = false
                // TakeDamageEffect.CheckCounterStagger()에서 이 플래그를 확인하여 역경직을 부여합니다.
                case global::AnimationEventType.Parry_Window_Open:
                    player.playerDefenseManager?.OnParryWindowEvent(true);
                    break;

                case global::AnimationEventType.Parry_Window_Close:
                    player.playerDefenseManager?.OnParryWindowEvent(false);
                    break;

                // --- [P2-2 신규] IFrame 무적 프레임 제어 ---
                // IFrameEnable 이벤트 수신 시 CharacterManager.isInvincible = true
                // IFrameDisable 이벤트 수신 시 CharacterManager.isInvincible = false
                case global::AnimationEventType.IFrameEnable:
                    player.isInvincible = true;
                    break;

                case global::AnimationEventType.IFrameDisable:
                    player.isInvincible = false;
                    break;

                // --- [P2-3 신규] ComboChain 큐 윈도우 제어 ---
                // ComboWindow_Open 이벤트 수신 시 큐잉된 Backstep 입력을 해소합니다.
                // ComboWindow_Close 이벤트 수신 시 isBackstepQueued 플래그를 초기화합니다.
                case global::AnimationEventType.ComboWindow_Open:
                    OnComboWindowOpened();
                    break;

                case global::AnimationEventType.ComboWindow_Close:
                    ClearBackstepQueue();
                    break;
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
        // [P2-3 신규] ComboChain 큐 시스템 (Recovery 큐잉)
        //
        // 기획: 전진 찌르기 후 S키(Dodge) 입력 큐잉 → Backstep 베리에이션 분기
        //
        // [아키텍처 규약]
        // - QueueBackstep() 호출은 PlayerManager(L2)가 OnDodgeInputReceived() 내에서 담당하고,
        //   실제 분기는 PlayerCombatManager(L3)가 처리합니다.
        //   PlayerManager에서 Backstep 애니메이션을 직접 호출하지 않습니다.
        // - isBackstepQueued 플래그는 ComboWindow_Close 또는 Action_Ended 이벤트 수신 시
        //   무조건 false로 초기화하여 큐 꼬임을 방지합니다.
        // - OnComboWindowOpened()는 PlayerCombatManager.OnAnimationEventReceived()의
        //   ComboWindow_Open 케이스에서 자동 호출됩니다.
        // =========================================================================================

        [Header("ComboChain Queue")]
        private bool isBackstepQueued = false; // S키 큐 상태 플래그

        /// <summary>
        /// [P2-3 신규] ComboWindow_Open 이벤트 수신 시 큐잉된 입력을 해소합니다.
        /// 큐가 없으면 기존 콤보 로직을 유지합니다.
        /// </summary>
        public void OnComboWindowOpened()
        {
            if (isBackstepQueued)
            {
                isBackstepQueued = false;

                // Backstep 베리에이션 실행 — Funnel로 위임
                // ActionID.Back_Step 값은 프로젝트 Enums에 맞게 수정하세요.
                player.playerAnimationManager.PlayTargetActionFunnel(
                    (int)ActionID.Back_Step, true, true);

                Debug.Log("<color=cyan>[PlayerCombatManager]</color> ComboChain: Backstep 베리에이션 실행!");
                return;
            }

            // 큐가 없으면 기존 콤보 진행 (추가 처리 없음)
            Debug.Log("<color=gray>[PlayerCombatManager]</color> ComboChain: 콤보 윈도우 오픈 (큐 없음).");
        }

        /// <summary>
        /// [P2-3 신규] ComboWindow_Close 또는 Action_Ended 이벤트 수신 시 호출됩니다.
        /// 큐 상태를 초기화하여 이전 입력이 다음 동작에 오염되지 않도록 합니다.
        /// </summary>
        public void ClearBackstepQueue()
        {
            if (isBackstepQueued)
            {
                isBackstepQueued = false;
                Debug.Log("<color=gray>[PlayerCombatManager]</color> ComboChain: Backstep 큐 만료 (윈도우 닫힘).");
            }
        }

        /// <summary>
        /// [P2-3 신규] PlayerManager.OnDodgeInputReceived()에서 ComboWindow가 열려 있을 때 호출합니다.
        /// canComboWithMainHandWeapon이 true일 때만 큐잉을 허용합니다.
        /// </summary>
        public void QueueBackstep()
        {
            if (canComboWithMainHandWeapon)
            {
                isBackstepQueued = true;
                Debug.Log("<color=yellow>[PlayerCombatManager]</color> ComboChain: Backstep 큐 등록.");
            }
        }

        // =========================================================================================
        // [P0-03 신규] 체술 기반 제스처 공격 라우터
        // =========================================================================================

        /// <summary>
        /// PlayerGestureManager에서 분석한 마우스 궤적(방향)에 따라 맞춤형 애니메이션을 실행합니다.
        /// </summary>
        /// <param name="actionID">1 = 우->좌 베기, 2 = 좌->우 베기 등 애니메이터로 넘길 Action State ID</param>
        public void PerformDirectionalAttack(int actionID)
        {
            // 1. 자원 및 상태 검문
            if (player.playerNetworkManager.isDead.Value) return;

            if (player.playerNetworkManager.currentStamina.Value <= 0)
            {
                Debug.Log("<color=red>[CombatManager Guard]</color> 스태미나가 부족하여 공격이 취소되었습니다.");
                return;
            }

            bool isTurning = false;
            bool isHoldingStance = false; // 파지 상태 추적 변수
            bool isEmptyState = false;    // [신규 추가] 엠프티(Empty) 상태 추적 변수

            if (player.animator != null)
            {
                // 🚨 [버그 완벽 수정] 턴 애니메이션이 1번 레이어(Action)로 격상됨에 따라 판독 주소를 동기화했습니다.
                // 더 이상 0번 레이어(Base)를 검사하지 않고, 1번 레이어에서 턴과 파지 상태를 모두 정밀 검사합니다.
                if (player.animator.layerCount > 1)
                {
                    AnimatorStateInfo actionState = player.animator.GetCurrentAnimatorStateInfo(1);
                    AnimatorStateInfo nextActionState = player.animator.GetNextAnimatorStateInfo(1);

                    // 턴 상태 확인
                    if (actionState.IsName("Turn_Left_90") || actionState.IsName("Turn_Right_90") ||
                        actionState.IsName("Turn_Left_180") || actionState.IsName("Turn_Right_180") ||
                        nextActionState.IsName("Turn_Left_90") || nextActionState.IsName("Turn_Right_90") ||
                        nextActionState.IsName("Turn_Left_180") || nextActionState.IsName("Turn_Right_180"))
                    {
                        isTurning = true;
                    }

                    // 1) 파지 상태 확인
                    if (actionState.IsName("Stance_Hold_Left") || actionState.IsName("Stance_Hold_Right"))
                    {
                        isHoldingStance = true;
                    }

                    // 2) [핵심 추가] Empty 상태 확인! 
                    // Empty 상태이거나, Empty로 복귀하는 찰나의 트랜지션 중이라면 플래그 꼬임을 무시하고 뚫어줍니다.
                    if (actionState.IsName("Empty State") || actionState.IsName("Empty"))
                    {
                        isEmptyState = true;
                    }
                }
            }

            bool isChargeRelease = (actionID == 11 || actionID == 12);

            // 🚨 [핵심 버그 뚫기] isHoldingStance 및 isEmptyState 예외 추가!
            // 파지(Hold) 상태로 멈춰 있거나, 아무것도 안 하는 완벽한 대기(Empty) 상태일 때는, 
            // isPerformingAction 플래그가 버그로 인해 true로 꼬여있더라도 강제로 가드를 뚫고 공격(약공격 포함)을 실행해야 합니다.
            if (player.isPerformingAction && !canComboWithMainHandWeapon && !isTurning && !isChargeRelease && !isHoldingStance && !isEmptyState)
            {
                // [디버깅 추가] 공격이 가드에 막혀 씹힐 때, 그 이유와 현재 상태들을 상세히 출력하여 디버깅을 돕습니다.
                Debug.Log($"<color=orange>[CombatManager Guard]</color> 공격 씹힘! (사유: 액션 중이며 예외 조건 불충족)\n" +
                          $"<color=gray>상세 상태 -> 콤보가능:{canComboWithMainHandWeapon}, 턴중:{isTurning}, 차징해방:{isChargeRelease}, 홀드중:{isHoldingStance}, 빈상태:{isEmptyState}</color>");
                return;
            }

            // 2. 공격 실행
            if (WorldGameStateManager.Instance.IsCombatAllowed())
            {
                // [신규] 차징 해방(강공격) 시점에 차징 종료 이벤트를 쏴서 사운드 페이드 아웃을 유도합니다.
                if (isChargeRelease && player.characterEventManager != null)
                {
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Charge_Ended);
                }

                // 무기 동기화
                if (player.playerInventoryManager.currentRightHandWeapon != null)
                {
                    player.playerNetworkManager.currentWeaponBeingUsed.Value = player.playerInventoryManager.currentRightHandWeapon.itemID;
                }

                player.playerNetworkManager.SetCharacterActionHand(true);

                // [임시 처리] 기존 스태미나 차감 시스템과의 호환성을 위해 AttackType을 강제 지정합니다.
                currentAttackType = global::AttackType.HeavyAttack01;

                // [핵심] Funnel을 통해 해당 방향의 ActionID를 애니메이터 파라미터에 꽂아 넣습니다.
                player.playerAnimationManager.PlayTargetActionFunnel(actionID, true, true, false, false);

                // 궤적 공격에 따른 스태미나 즉시 차감
                DrainStaminaBasedOnAttack();

                // 다음 애니메이션 프레임(ComboEnable 이벤트)이 오기 전까지 콤보 창을 굳게 닫아둡니다.
                DisableCombo();

                Debug.Log($"<color=yellow>[CombatManager]</color> 궤적 기반 공격 실행 완료! (ActionID: {actionID})");
            }
            else
            {
                Debug.Log("<color=orange>[CombatManager Guard]</color> 월드 상태가 전투를 허용하지 않아 공격이 취소되었습니다.");
            }
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

        // =========================================================================================
        // [P1-2 신규] 카운터 기회 활성화 메서드
        // =========================================================================================

        /// <summary>
        /// [P1-2 신규] TakeDamageEffect에서 AI 포이즈 파괴 확인 시 호출됩니다.
        /// 일정 시간 동안 isCounterOpportunity 플래그를 true로 유지하며,
        /// L4 이벤트 채널을 통해 카메라 연출·UI 알림 등을 연계할 수 있습니다.
        ///
        /// [아키텍처 규약]
        /// - ActivateCounterOpportunity()는 PlayerCombatManager(L3)에 위치하므로,
        ///   UI·카메라 연출은 반드시 L4 이벤트 발행(NotifyAnimationEvent)으로 처리해야 하며
        ///   직접 UI 컴포넌트를 호출하면 안 됩니다.
        /// - isCounterOpportunity 플래그는 PlayerExecutionManager.AttemptExecution()의
        ///   진입 조건 게이트로도 활용할 수 있습니다.
        /// </summary>
        public void ActivateCounterOpportunity()
        {
            isCounterOpportunity = true;

            if (counterOpportunityCoroutine != null)
                StopCoroutine(counterOpportunityCoroutine);

            counterOpportunityCoroutine = StartCoroutine(CounterOpportunityTimer());

            // [L4] UI 프롬프트, 카메라 연출 등 구독자가 자율 반응
            player.characterEventManager?
                .NotifyAnimationEvent(AnimationEventType.Groggy_Enter);
        }

        /// <summary>
        /// [P1-2 신규] counterOpportunityDuration 경과 후 isCounterOpportunity를 자동 해제합니다.
        /// </summary>
        private IEnumerator CounterOpportunityTimer()
        {
            yield return new WaitForSeconds(counterOpportunityDuration);
            isCounterOpportunity = false;
            counterOpportunityCoroutine = null;
        }

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

                // 🚨 [Phase 1 고도화] 락온 해제 시 관제탑에 설정된 기본 스탠스로 복귀
                if (WorldCameraManager.Instance != null && WorldCameraManager.Instance.defaultRestStance != null)
                {
                    WorldCameraManager.Instance.ChangeCameraStance(WorldCameraManager.Instance.defaultRestStance, "LockOn Disabled");
                    Debug.Log("<color=cyan>[PlayerCombatManager]</color> 락온 해제! <b>기본 스탠스</b>로 복귀합니다.");
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
                    SetTarget(player.playerCamera.nearestLockOnTarget);
                    player.playerNetworkManager.isLockedOn.Value = true;

                    if (WorldCameraManager.Instance != null)
                    {
                        // Sequence SO가 연결되어 있으면 PlayCameraSequence로 호출
                        // → restorePreviousAngle, damping, canBeInterruptedByInput 등 모두 작동
                        if (lockOnSequenceSO != null)
                        {
                            WorldCameraManager.Instance.PlayCameraSequence(lockOnSequenceSO, "LockOn Enabled");
                        }
                        // fallback: Sequence SO 미연결 시 기존 방식 유지
                        else if (lockOnStanceSO != null)
                        {
                            WorldCameraManager.Instance.ChangeCameraStance(lockOnStanceSO, "LockOn Enabled");
                        }
                    }
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

            // [버그 수정 완료] PlayerCamera.cs에서 하드코딩된 SetLockCameraHeight() 함수가 
            // 삭제되었으므로 이 부분의 호출을 깔끔하게 제거했습니다.
            // 락온 시 카메라 높이는 이제 WorldGameStateManager와 SO 데이터가 완벽히 통제합니다.
        }
    }
}