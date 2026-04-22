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

        // =========================================================================================
        // [Fix-P0 신규] 디버그 스위치
        // Damage Collider 파이프라인 문제 추적 시 콘솔에 Enable/Disable 호출 내역과
        // IsServer / IsOwner / IsSpawned 값을 함께 찍어 단계별 병목을 식별하는 데 사용합니다.
        // 프로덕션에서는 반드시 false 로 두세요.
        // =========================================================================================
        [Header("Damage Collider Debug (Fix-P0)")]
        [Tooltip("켜면 HitBoxEnable/Disable 수신 시마다 IsServer/IsOwner/IsSpawned 상태를 로깅합니다. " +
                 "Enable/Disable 이 실제 어떤 Collider 에 전달되는지 추적할 때만 켜세요.")]
        [SerializeField] protected bool logDamageColliderPipeline = false;

        // =========================================================================================
        // [Fix-P0-F] 좀비 isPerformingAction 자동 해제 폴백 토글
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // 정석 해결: Animator Controller 의 Idle/Locomotion State 에 ResetCharacterStateBehaviour 를
        //           부착하고, 공격/피격/회피 State 에 각각 "Attack"/"Hit"/"Stagger"/"Roll" Tag 를 설정.
        //
        // 이 토글을 켜면:
        //   매 프레임 Animator 의 현재 Base Layer State Tag 를 조회해, Action 계열이 아닌 상태에서
        //   isPerformingAction = true 가 0.5초 이상 유지되면 좀비 상태로 판정하여 강제 리셋합니다.
        //
        // ⚠ 주의:
        //   State Tag 가 Animator 에 실제로 설정되어 있을 때만 켜세요.
        //   Tag 가 전혀 설정되어 있지 않으면 정상 공격 중에도 플래그를 해제해버려 오히려 버그를 유발합니다.
        //   설정 여부가 불확실하면 false 로 유지하고, 대신 Animator Controller 에서
        //   ResetCharacterStateBehaviour 를 Idle 계열 State 에 부착하세요.
        // =========================================================================================
        [Tooltip("Animator State 에 Attack/Hit/Stagger/Roll Tag 가 설정되어 있을 때만 켜세요. " +
                 "좀비 isPerformingAction 상태를 0.5초 후 자동 해제하는 임시 안전망입니다.")]
        public bool enableStaleActionFlagRecovery = false;

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

        // =========================================================================================
        // [Fix-P0 신규] 로컬 권위 판정 헬퍼
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // 기존 IsServer 게이트 하나만 쓰던 경우의 문제:
        //   · 네트워크 매니저가 미기동(IsListening == false) 상태의 단일 테스트 씬에서는
        //     모든 NetworkBehaviour의 IsServer == false → 히트박스 Enable 경로가 전부 차단.
        //   · 이 때문에 "에디터에서 Play 만 눌러 AI 공격을 확인" 하는 테스트 자체가 불가능.
        //
        // 수정 전략:
        //   · 네트워크가 활성화된 경우(멀티플레이): 기존 설계대로 IsServer 만 권위를 갖는다.
        //   · 네트워크가 비활성(싱글/에디터 테스트): 로컬이 곧 권위자. 모든 연산을 허용한다.
        //
        // 이 헬퍼는 NetworkManager.Singleton 이 없거나 IsListening == false 이면 true 를,
        // 네트워크 활성 상태에서는 IsServer 값을 그대로 반환합니다.
        // 이후 NetworkVariable 쓰기에는 별도 IsNetworkActive 플래그를 병행 체크합니다.
        // =========================================================================================
        protected bool HasLocalAuthority
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsListening) return true; // 네트워크 미활성 → 로컬 권위
                return IsServer;                                // 네트워크 활성 → 서버만 권위
            }
        }

        protected bool IsNetworkActive
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening;
            }
        }

        // =========================================================================================
        // [Fix-P0 신규] damageColliders 리스트 갱신 API
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // Awake 시점 1회 캐싱만으로는 장비 매니저가 Start() 에서 무기를 재인스턴시에이트할 때
        // 발생하는 리스트 스테일(stale) 문제를 해결할 수 없습니다.
        //
        // AICharacterEquipmentManager.LoadRightWeapon / LoadLeftWeapon 성공 후
        // 반드시 이 메서드를 호출해 새 무기의 DamageCollider 를 재등록하세요.
        // (PlayerEquipmentManager 에서도 동일 규약을 적용 가능합니다.)
        // =========================================================================================
        public virtual void RefreshDamageColliders()
        {
            // 안전하게 기존 리스트를 비우고 현재 자식 계층에서 다시 수집
            damageColliders.Clear();
            damageColliders.AddRange(GetComponentsInChildren<DamageCollider>(true));

#if UNITY_EDITOR
            if (logDamageColliderPipeline)
            {
                Debug.Log($"<color=lime>[CCM.Refresh]</color> {name} dmgColliders.Count = {damageColliders.Count}");
                for (int i = 0; i < damageColliders.Count; i++)
                {
                    var dc = damageColliders[i];
                    Debug.Log($"<color=lime>[CCM.Refresh]</color>   [{i}] {dc.gameObject.name}  " +
                              $"path={GetHierarchyPath(dc.transform)}  enabled={(dc != null ? dc.GetComponent<Collider>()?.enabled.ToString() : "null")}");
                }
            }
#endif
        }

#if UNITY_EDITOR
        // 디버그 용도의 경로 출력 헬퍼 — 프로덕션 코드 영향 없음
        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "<null>";
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
#endif

        public virtual void OnAnimationEventReceived(AnimationEventType eventType)
        {
            // =====================================================================================
            // [Fix-P0 수정] IsServer → HasLocalAuthority 로 게이트 완화
            // ─────────────────────────────────────────────────────────────────────────────────────
            // 기존 동작(멀티플레이 권위 규약)은 100% 보존됩니다:
            //   · 네트워크 활성 & 서버 → 예전과 동일하게 NetworkVariable 도 쓰고, 콜라이더도 토글.
            //   · 네트워크 활성 & 클라이언트 → NetworkVariable 쓰기는 서버만 가능하므로 스킵.
            //
            // 새로 지원되는 케이스:
            //   · 네트워크 비활성(단일/에디터 테스트) → NetworkVariable 접근은 스킵하되,
            //     콜라이더 토글은 로컬 권위로 정상 수행 → AI 공격이 실제로 데미지 콜라이더를 연다.
            //
            // NetworkVariable 쓰기는 IsNetworkActive && IsSpawned 인 상황에만 수행해
            // "스폰 전 프레임" 에러도 함께 방어합니다.
            // =====================================================================================

#if UNITY_EDITOR
            if (logDamageColliderPipeline &&
                (eventType == AnimationEventType.HitBoxEnable ||
                 eventType == AnimationEventType.HitBoxDisable ||
                 eventType == AnimationEventType.Action_Ended))
            {
                string spawned = (NetworkObject != null) ? NetworkObject.IsSpawned.ToString() : "no-NO";
                Debug.Log($"<color=cyan>[CCM.Event]</color> {name} evt={eventType}  " +
                          $"IsNetworkActive={IsNetworkActive}  IsServer={IsServer}  IsOwner={IsOwner}  IsSpawned={spawned}  " +
                          $"HasLocalAuthority={HasLocalAuthority}  dmgColliders.Count={damageColliders.Count}");
            }
#endif

            if (eventType == AnimationEventType.HitBoxEnable)
            {
                if (HasLocalAuthority)                                    // [Fix-P0] IsServer → HasLocalAuthority
                {
                    // NetworkVariable 쓰기는 실제 서버 스폰 상태에서만
                    if (IsNetworkActive && IsSpawned) isAttacking.Value = true;   // [Fix-P0]

                    // =============================================================================
                    // [Fix-P0 자가치유] HitBoxEnable 직전 damageColliders 리스트를 강제 재수집.
                    // ─────────────────────────────────────────────────────────────────────────────
                    // 무기가 런타임 Instantiate 되는 환경에서, Awake 시점 캐시가 stale 하거나
                    // 장비 매니저의 RefreshDamageColliders() 호출이 누락된 경우에도
                    // 공격 직전 시점에 항상 올바른 무기 Collider 가 리스트에 들어가도록 보장합니다.
                    //
                    // 비용: GetComponentsInChildren 은 공격 1회당 1번 수행되므로 프레임 부하 미미.
                    //       프로덕션 빌드에서도 안전망으로 남겨두어도 무방합니다.
                    // =============================================================================
                    RefreshDamageColliders();

                    foreach (var col in damageColliders)
                    {
                        if (col != null) col.EnableDamageCollider();
                    }
                }
            }
            else if (eventType == AnimationEventType.HitBoxDisable)
            {
                if (HasLocalAuthority)                                    // [Fix-P0]
                {
                    if (IsNetworkActive && IsSpawned) isAttacking.Value = false;  // [Fix-P0]
                    foreach (var col in damageColliders)
                    {
                        if (col != null) col.DisableDamageCollider();
                    }
                }
            }
            else if (eventType == AnimationEventType.Action_Ended)
            {
                if (HasLocalAuthority)                                    // [Fix-P0]
                {
                    if (IsNetworkActive && IsSpawned)                     // [Fix-P0]
                    {
                        isAttacking.Value = false;
                        canCombo.Value = false;
                    }
                    foreach (var col in damageColliders)
                    {
                        if (col != null) col.DisableDamageCollider();
                    }
                }
            }
        }

        // =========================================================================================
        // [Fix-P0-F 신규] 좀비 공격 상태 복구 헬퍼
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // 문제 상황:
        //   Animator Controller 의 Idle/Locomotion State 에 ResetCharacterStateBehaviour 가
        //   부착되지 않은 AI 의 경우, 공격 애니메이션이 끝난 뒤에도 Action_Ended 이벤트가 발행되지
        //   않아 character.isPerformingAction = true 및 ActionState = 1 파라미터가 남아 있게 됩니다.
        //   이 상태에서 BT 가 다음 공격을 시도해 PlayTargetActionFunnel(1) 을 호출해도
        //   Animator 의 ActionState 가 이미 1 이므로 전이 트리거가 소비되지 않아
        //   공격 애니메이션이 재생되지 않고, 결과적으로 HitBoxEnable 이벤트도 발행되지 않습니다.
        //   (로그 증거: 2차 공격 시 clip=Idle_Standard 인 채 isPerformingAction=true)
        //
        // 근본 해결:
        //   Goblin Animator 의 Idle_Standard 등 기본 State 에 ResetCharacterStateBehaviour 를 부착.
        //
        // 임시 폴백 (본 메서드):
        //   CharacterCombatManager 외부(예: StrikeAction)에서 공격 트리거 직전 호출하여
        //   좀비 플래그를 안전하게 리셋합니다. 공격이 실제로 진행 중일 때는 건드리지 않도록
        //   isAttacking NetworkVariable 및 Animator 의 현재 재생 State 이름을 함께 검사합니다.
        // =========================================================================================
        public virtual void TryClearStalePerformingActionState()
        {
            if (character == null || character.animator == null) return;

            // 현재 Base Layer 의 State 를 조회하여 Attack/Stagger 가 아닐 때만 플래그 리셋
            AnimatorStateInfo baseState = character.animator.GetCurrentAnimatorStateInfo(0);
            bool isInActionState =
                baseState.IsTag("Attack") || baseState.IsTag("Hit") ||
                baseState.IsTag("Stagger") || baseState.IsTag("Roll");

            // NetworkVariable 공격 상태도 확인 (네트워크 활성 시에만 유의미)
            bool netIsAttacking = IsNetworkActive && IsSpawned && isAttacking.Value;

            if (!isInActionState && !netIsAttacking && character.isPerformingAction)
            {
#if UNITY_EDITOR
                if (logDamageColliderPipeline)
                {
                    Debug.Log($"<color=magenta>[CCM.StaleReset]</color> {name}  " +
                              $"좀비 isPerformingAction=true 감지 → 강제 리셋  " +
                              $"baseState={baseState.shortNameHash}");
                }
#endif
                character.isPerformingAction = false;
                character.canMove = true;
                character.canRotate = true;
                character.animator.applyRootMotion = false;
                character.animator.SetInteger(AnimatorParameterHash.ActionState, 0);
            }
        }

        // =========================================================================================
        // [Fix-P0-F 신규] 자동 좀비 상태 감지기
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // Animator Controller 에 ResetCharacterStateBehaviour 가 누락된 상태 기계에서
        // isPerformingAction = true 가 영구 고착되는 것을 방지하기 위한 마지막 안전망입니다.
        //
        // 매 프레임 경량 검사:
        //   - Base Layer State 가 Action 계열 태그(Attack/Hit/Stagger/Roll) 가 아님
        //   - NetworkVariable isAttacking = false (또는 네트워크 비활성)
        //   - 그런데도 character.isPerformingAction = true
        //   - 이 상태가 0.5 초 이상 지속됨
        //   → 좀비 상태로 판정하고 강제 리셋.
        //
        // 이 코드는 Animator 의 정상 전이 중에는 1 프레임 만에 baseState 가 Action 태그로 바뀌므로
        // 오탐되지 않습니다. 애셋 수정 후에는 자연스럽게 발동되지 않습니다.
        // =========================================================================================
        private float _staleActionFlagTimer = 0f;
        private const float STALE_ACTION_TIMEOUT = 0.5f;

        // LateUpdate 를 사용하는 이유:
        //   AICharacterCombatManager 는 private void Update() 를 갖고 있어 부모의 Update() 를 숨깁니다.
        //   virtual/override 체인을 깨지 않고 부모 로직이 확실히 실행되도록 LateUpdate 에 배치합니다.
        //   (Unity 는 부모 private + 자식 private 이어도 각자 Update 를 호출하지만, 유지보수 혼선 방지)
        protected virtual void LateUpdate()
        {
            // [Fix-P0-F] 이 폴백은 명시적 opt-in 시에만 동작합니다.
            // 정식 해결은 Animator Controller 의 Idle State 에 ResetCharacterStateBehaviour 를 부착하는 것.
            if (!enableStaleActionFlagRecovery) return;

            if (character == null || character.animator == null) return;

            if (!character.isPerformingAction)
            {
                _staleActionFlagTimer = 0f;
                return;
            }

            // ─────────────────────────────────────────────────────────────────────
            // [Fix-P0-F 좀비 상태 판정]
            //   1) State Tag 가 Action 계열이면 명확히 정상 → 타이머 리셋
            //   2) Animator 가 전이 중이면 판단 유예 → 타이머 리셋
            //   3) 그 외는 isPerformingAction=true 인데 Idle 계열 State 에 있다는 의미
            //      → 타이머 가산 후 일정 시간 지속되면 좀비 판정
            //
            //   주의: ActionState 파라미터 != 0 체크는 일부러 제외합니다.
            //   본 버그의 정의가 "ActionState=1 이 남아있는 채로 Idle State 에 머무는" 상황이므로
            //   ActionState 파라미터만으로는 정상/좀비를 구분할 수 없습니다.
            //   대신 실제 Animator State Tag 만으로 판단합니다.
            // ─────────────────────────────────────────────────────────────────────
            AnimatorStateInfo baseState = character.animator.GetCurrentAnimatorStateInfo(0);

            bool hasActionTag =
                baseState.IsTag("Attack") || baseState.IsTag("Hit") ||
                baseState.IsTag("Stagger") || baseState.IsTag("Roll");

            bool animatorIsTransitioning = character.animator.IsInTransition(0);

            if (hasActionTag || animatorIsTransitioning)
            {
                // 정상 액션 수행 중 또는 전이 중 — 타이머 리셋
                _staleActionFlagTimer = 0f;
                return;
            }

            // Action Tag 가 없는 State 에 머무르는데 isPerformingAction=true → 좀비 상태 의심
            _staleActionFlagTimer += Time.deltaTime;

            if (_staleActionFlagTimer >= STALE_ACTION_TIMEOUT)
            {
                TryClearStalePerformingActionState();
                _staleActionFlagTimer = 0f;
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