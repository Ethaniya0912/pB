// =============================================================================
// CharacterManager.cs  |  TDA Project
// Layer  : L2 Router — 플레이어·AI 공통 최상위 매니저
// 수정 이력:
//   [신규] characterExecutionManager 공개 필드 추가 (AI/Player 업캐스팅용)
//   [신규] isPoiseActive 플래그 추가 (강공격 중 포이즈 유지)
//   [신규] lockOnTransform 필드 추가 (카메라·Magnetic 락온 조준점)
//   [유지] 기존 모든 필드·메서드·주석 완전 보존
// =============================================================================
using System.Collections;
using System.Collections.Generic;
using SG;
using TDA.Character;
using Unity.Netcode;
using UnityEngine;

public class CharacterManager : NetworkBehaviour
{
    // =========================================================================
    // 컴포넌트 참조 (Awake에서 GetComponent 자동 할당)
    // =========================================================================
    [HideInInspector] public CharacterController characterController;
    [HideInInspector] public Animator animator;

    [HideInInspector] public CharacterNetworkManager characterNetworkManager;
    [HideInInspector] public CharacterEffectsManager characterEffectsManager;
    [HideInInspector] public CharacterAnimationManager characterAnimationManager;
    [HideInInspector] public CharacterCombatManager characterCombatManager;
    [HideInInspector] public CharacterSoundFxManager characterSoundFxManager;
    [HideInInspector] public CharacterLocomotionManager characterLocomotionManager;
    [HideInInspector] public CharacterInventoryManager characterInventoryManager;
    [HideInInspector] public CharacterIKController characterIKController;
    [HideInInspector] public CharacterStatsManager characterStatsManager;
    [HideInInspector] public CharacterEventManager characterEventManager;
    [HideInInspector] public CharacterDefenseManager characterDefenseManager;

    // ── [신규] 처형 매니저 공통 참조 (PlayerExecutionManager / AIExecutionManager 업캐스팅용) ──
    // PlayerManager.Awake() 또는 AICharacterManager.Awake()에서 할당합니다.
    [HideInInspector] public CharacterExecutionManager characterExecutionManager;
    // ───────────────────────────────────────────────────────────────────────────

    // =========================================================================
    // 캐릭터 그룹
    // =========================================================================
    [Header("Character Group")]
    public CharacterGroup characterGroup;

    // =========================================================================
    // 공통 상태 플래그
    // =========================================================================
    [Header("Flags")]
    public bool isPerformingAction = false;
    public bool applyRootMotion = false;
    public bool canRotate = true;
    public bool canMove = true;

    // ── [신규] 포이즈 유지 플래그 ─────────────────────────────────────────────
    // AttackState / PlayerCombatManager에서 강공격 시 true 설정.
    // TakeDamageEffect에서 true이면 경직(poiseIsBroken)을 무시합니다.
    // 공격 종료(ResetStateFlags / 모션 완료) 시 false로 초기화합니다.
    [HideInInspector] public bool isPoiseActive = false;
    // ───────────────────────────────────────────────────────────────────────────

    // ── [신규] Lock-On Transform ──────────────────────────────────────────────
    [Header("Lock On")]
    [Tooltip("카메라 락온 / MagneticSoftLock이 조준하는 Transform")]
    public Transform lockOnTransform;
    // ───────────────────────────────────────────────────────────────────────────

    // =========================================================================
    // Unity 생명주기
    // =========================================================================
    protected virtual void Awake()
    {
        DontDestroyOnLoad(this);

        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        characterNetworkManager = GetComponent<CharacterNetworkManager>();
        characterEffectsManager = GetComponent<CharacterEffectsManager>();
        characterAnimationManager = GetComponent<CharacterAnimationManager>();
        characterCombatManager = GetComponent<CharacterCombatManager>();
        characterSoundFxManager = GetComponent<CharacterSoundFxManager>();
        characterLocomotionManager = GetComponent<CharacterLocomotionManager>();
        characterInventoryManager = GetComponent<CharacterInventoryManager>();
        characterIKController = GetComponent<CharacterIKController>();
        characterStatsManager = GetComponent<CharacterStatsManager>();
        characterEventManager = GetComponent<CharacterEventManager>();
        characterDefenseManager = GetComponent<CharacterDefenseManager>();
        // characterExecutionManager는 서브클래스(PlayerManager / AICharacterManager)에서 할당
    }

    protected virtual void Start()
    {
        IgnoreMyOwnCollider();
    }

    public virtual void Update()
    {
        // 캐릭터가 내쪽에서 움직일 경우, 네트워크포지션에 내 포지션을 할당
        if (IsOwner)
        {
            characterNetworkManager.networkPosition.Value = transform.position;
            characterNetworkManager.networkRotation.Value = transform.rotation;
        }
        // 캐릭터가 상대방에서 움직일 경우, 네트워크 포지션에서 로컬 포지션으로 할당
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                characterNetworkManager.networkPosition.Value,
                ref characterNetworkManager.networkPositionVelocity,
                characterNetworkManager.networkPositionSmoothTime);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                characterNetworkManager.networkRotation.Value,
                characterNetworkManager.networkRotationSmoothTime);
        }
    }

    public virtual void FixedUpdate() { }

    public virtual void LateUpdate() { }

    // =========================================================================
    // 사망 처리
    // =========================================================================
    public virtual IEnumerator ProcessDeathEvent(bool manuallySelectDeathAnimation = false)
    {
        if (IsOwner)
        {
            characterNetworkManager.currentHealth.Value = 0;
            characterNetworkManager.isDead.Value = true;

            // 리셋해야 할 플래그를 리셋해주기.

            // 땅에서 죽은 게 아니라면, 다른 형식의 사망 애니메이션 재생.

            if (!manuallySelectDeathAnimation)
            {
                characterAnimationManager.PlayTargetAnimation(
                    Animator.StringToHash("Dead_01"), true);
            }
        }

        // 사망 SFX 재생

        yield return new WaitForSeconds(5);

        // 플레이어에게 룬 제공 (AI 캐릭터 사망 시)

        // 캐릭터 비활성화 disable
    }

    public virtual void ReviveCharacter() { }

    // =========================================================================
    // 자기 자신 콜라이더 무시 설정
    // =========================================================================
    protected virtual void IgnoreMyOwnCollider()
    {
        Collider characterControllerCollider = GetComponent<Collider>();
        Collider[] damageableCharacterColliders = GetComponentsInChildren<Collider>();
        List<Collider> ignoreColliders = new List<Collider>();

        // 모든 데미저블캐릭터콜라이더를 리스트에 때려박음
        foreach (var collider in damageableCharacterColliders)
        {
            ignoreColliders.Add(collider);
        }

        // 메인 캐릭터 컨트롤러도 별도로 리스트에 추가
        ignoreColliders.Add(characterControllerCollider);

        // 포이치문을 돌려, 리스트 콜라이더 내부에 있는 각 콜라이더끼리 서로 콜리션 무시.
        foreach (var collider in ignoreColliders)
        {
            foreach (var otherCollider in ignoreColliders)
            {
                Physics.IgnoreCollision(collider, otherCollider, true);
            }
        }
    }
}