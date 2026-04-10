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

    // ── [신규] 처형 매니저 공통 참조 (PlayerExecutionManager / AICharacterExecutionManager 업캐스팅용) ──
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

    // =========================================================================================
    // [P2-1 신규] IFrame 무적 프레임 플래그
    // 회피/구르기 모션 중 무적 구간을 표현하는 로컬 플래그입니다.
    //
    // [설계 근거]
    // - NetworkVariable이 아닌 로컬 플래그로 충분합니다.
    //   TakeDamageEffect.ProcessEffect()는 IsOwner 게이트를 선행하므로
    //   서버/클라이언트 중복 적용 걱정이 없습니다.
    // - IAnimationEventListener 구현체(PlayerCombatManager 등)에서
    //   IFrameEnable(=6) / IFrameDisable(=7) 이벤트 수신 시 이 값을 설정합니다.
    //   (PlayerCombatManager.OnAnimationEventReceived() 스위치에 케이스 추가 완료)
    // - 위빙 모션 시작 10~15% 구간에서 IFrameEnable 이벤트 발행,
    //   85~90% 구간에서 IFrameDisable 이벤트 발행을 Unity Animator 클립에 설정해야 합니다.
    // =========================================================================================
    [HideInInspector] public bool isInvincible = false;

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
        // [pB-4 수정] 오프라인/테스트 씬에서는 DontDestroyOnLoad를 적용하지 않습니다.
        // NetworkManager가 활성화된 멀티플레이 환경에서만 씬 전환 시 오브젝트를 유지합니다.
        // 테스트 씬에서 DontDestroyOnLoad를 적용하면 오브젝트가 DontDestroyOnLoad 씬으로
        // 이동하면서 position이 (0,0,0)으로 리셋되는 문제가 발생합니다.
        bool isNetworkActive = Unity.Netcode.NetworkManager.Singleton != null &&
                               Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (isNetworkActive)
        {
            DontDestroyOnLoad(this);
        }

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
        // [Bug 4] 네트워크 미초기화 환경 보호
        if (characterNetworkManager == null) return;

        // [Bug 5 — 0,0,0 스냅 수정]
        // 원인: networkPosition 초기값=Vector3.zero, AI는 IsOwner=false
        //       → else 분기에서 transform.position을 매 프레임 (0,0,0)으로 SmoothDamp
        // 수정: 실제 네트워크 세션에서만 위치 동기화 블록 실행
        bool isNetworkActive = Unity.Netcode.NetworkManager.Singleton != null &&
                               Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (!isNetworkActive) return;

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