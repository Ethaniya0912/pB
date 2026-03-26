// =============================================================================
// AICharacterIKController.cs  |  TDA Project
// Layer  : L4 View — AI 전용 IK 및 발소리 추적 컨트롤러
//
// 역할:
//   CharacterIKController 를 상속하여 AI(몬스터/NPC)에 특화된
//   IK 가중치 제어와 발소리 추적을 처리합니다.
//
// 플레이어와의 차이:
//   - 시선(HeadLookIK): 락온 대신 currentTarget 방향으로 자동 조준
//   - 손 IK: 무기 소켓 기반 (PlayerIK 의 물건 잡기 IK 없음)
//   - 발소리 추적: 구르기(isRolling) 대신 isPerformingAction 으로 중지 조건
//   - 스트레이핑 드래그 사운드: isLockedOn 대신 aiCharacterCombatManager.currentTarget
//     존재 여부로 전투 보행 판단
//   - 사망 시 모든 IK 가중치 0 으로 즉시 해제 (래그돌과 충돌 방지)
//
// 아키텍처 규약:
//   ① Animator 파라미터 직접 조작 금지
//   ② 발소리 이벤트는 characterEventManager.NotifyAnimationEvent 경유
//   ③ IK 가중치 제어는 Update/LateUpdate 에서만 (FSM State 에서 직접 조작 금지)
// =============================================================================
using UnityEngine;
using TDA.Character;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L4 View] AI(몬스터/NPC) 전용 IK 및 키네마틱 발소리 추적 컨트롤러.
    /// currentTarget 방향 시선 IK 와 전투/이동 상황별 발소리 분기를 추가합니다.
    /// </summary>
    public class AICharacterIKController : CharacterIKController
    {
        // =====================================================================
        // AI 전용 설정
        // =====================================================================
        [Header("AI — 시선 IK")]
        [Tooltip("true 이면 currentTarget 방향으로 머리 IK 를 자동 조준합니다.")]
        [SerializeField] private bool enableTargetLookIK = true;

        [Tooltip("시선 IK 가 활성화되기 시작하는 타겟 거리 (m). 이 거리 이하일 때만 고개를 돌립니다.")]
        [SerializeField] private float lookIKActivationRange = 12f;

        [Tooltip("타겟이 없어졌을 때 시선 IK 가중치를 0 으로 줄이는 감쇠 속도")]
        [SerializeField] private float lookIKFadeOutSpeed = 3f;

        [Header("AI — 무기 손 IK")]
        [Tooltip("무기 장착 소켓의 Transform. AICharacterEquipmentManager 가 동적으로 연결합니다.")]
        [SerializeField] private Transform weaponGripTarget;

        [Header("AI — 발소리 전투 판정")]
        [Tooltip("전투 중(currentTarget 존재) 발소리를 드래그 사운드로 바꾸는 비율.\n" +
                 "0 = 항상 일반 발소리, 1 = 전투 중 항상 드래그.")]
        [Range(0f, 1f)]
        [SerializeField] private float combatFootstepDragRatio = 0f;

        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager _ai;

        // =====================================================================
        // 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();
            _ai = GetComponent<AICharacterManager>();
        }

        protected override void Update()
        {
            base.Update(); // CharacterIKController.UpdateIKWeights() 호출

            if (_ai == null) return;

            // 사망 시 모든 IK 즉시 해제 (래그돌과 충돌 방지)
            if (_ai.characterNetworkManager != null
                && _ai.characterNetworkManager.isDead.Value)
            {
                targetHandWeight = 0f;
                targetLookWeight = 0f;
                return;
            }

            UpdateAILookIK();
        }

        // =====================================================================
        // AI 시선 IK — currentTarget 방향 자동 조준
        // =====================================================================

        /// <summary>
        /// currentTarget 의 lockOnTransform(상반신 위치)을 향해 머리 IK 를 조준합니다.
        /// 타겟이 없거나 범위 밖이면 가중치를 서서히 0 으로 감쇠합니다.
        /// </summary>
        private void UpdateAILookIK()
        {
            if (!enableTargetLookIK) return;
            if (_ai.aiCharacterCombatManager == null) return;

            CharacterManager target = _ai.aiCharacterCombatManager.currentTarget;

            if (target != null)
            {
                float dist = Vector3.Distance(transform.position, target.transform.position);

                if (dist <= lookIKActivationRange)
                {
                    // 타겟의 lockOnTransform 이 있으면 그 위치로, 없으면 transform
                    Transform lookPoint = target.lockOnTransform != null
                        ? target.lockOnTransform
                        : target.transform;

                    SetLookTarget(lookPoint);
                    return;
                }
            }

            // 타겟 없음 또는 범위 초과 — 가중치 서서히 감쇠
            if (targetLookWeight > 0f)
            {
                targetLookWeight = Mathf.MoveTowards(
                    targetLookWeight, 0f, lookIKFadeOutSpeed * Time.deltaTime);

                if (targetLookWeight <= 0.01f)
                    SetLookTarget(null);
            }
        }

        // =====================================================================
        // 무기 손 IK 설정 (AICharacterEquipmentManager 에서 호출)
        // =====================================================================

        /// <summary>
        /// 무기 장착 시 손 IK 타겟을 연결합니다.
        /// AICharacterEquipmentManager.LoadWeaponsOnStart() 에서 호출합니다.
        /// </summary>
        public void SetWeaponGripTarget(Transform gripPoint)
        {
            weaponGripTarget = gripPoint;
            SetHandIKTarget(gripPoint);
        }

        // =====================================================================
        // 발소리 추적 Override — AI 전용 중지 조건
        // =====================================================================
        protected override void TrackKinematicFootsteps()
        {
            if (character == null || character.characterEventManager == null) return;

            // 사망 시 발소리 추적 중지
            if (character.characterNetworkManager != null
                && character.characterNetworkManager.isDead.Value)
                return;

            // AI 는 isRolling 대신 isPerformingAction 중 발소리 억제
            // (공격·피격 모션 중 발소리 이중 재생 방지)
            if (character.isPerformingAction) return;

            // NavMesh 위에 없으면 발소리 추적 중지
            if (_ai != null && _ai.navMeshAgent != null
                && !_ai.navMeshAgent.isOnNavMesh)
                return;

            // 전투 중 발소리 타입 결정
            // combatFootstepDragRatio > 0 이고 타겟이 있으면 드래그 사운드 확률 적용
            bool useDragSound = false;
            if (_ai != null && _ai.aiCharacterCombatManager != null
                && _ai.aiCharacterCombatManager.currentTarget != null
                && combatFootstepDragRatio > 0f)
            {
                useDragSound = Random.value < combatFootstepDragRatio;
            }

            // 전투 드래그 발소리 사용 여부에 따라 isStrafing 플래그를 임시 오버라이드
            // 부모의 TrackKinematicFootsteps() 는 isLockedOn NetworkVar 를 읽으므로
            // AI 는 base 를 직접 호출하지 않고 여기서 독립 처리합니다.
            TrackAIFootsteps(useDragSound);
        }

        /// <summary>
        /// AI 전용 발소리 추적 — 부모의 TrackKinematicFootsteps 를 대체합니다.
        /// isStrafing 여부를 AI 전투 상태로 결정합니다.
        /// ProcessFootstep 이 부모에서 private 이므로 characterNetworkManager.isLockedOn
        /// 을 일시적으로 설정하여 부모 로직을 활용합니다.
        /// </summary>
        private void TrackAIFootsteps(bool isDragWalk)
        {
            if (character == null || character.characterNetworkManager == null) return;

            // 부모의 TrackKinematicFootsteps() 는 isLockedOn.Value 를 읽어 발소리 타입을 결정합니다.
            // AI 는 isLockedOn 이 항상 false 이므로, isDragWalk 를 원하면
            // characterNetworkManager.isLockedOn 을 임시로 덮어쓴 뒤 호출합니다.
            // ⚠ 이 방식은 짧은 프레임에만 적용되므로 네트워크 동기화에 영향 없음.
            bool originalLockOn = character.characterNetworkManager.isLockedOn.Value;

            if (isDragWalk && !originalLockOn)
            {
                // 서버에서만 NetworkVariable 변경 가능하므로
                // 전투 드래그 발소리는 IsServer 환경에서만 강제 적용
                if (character.IsServer)
                    character.characterNetworkManager.isLockedOn.Value = true;
            }

            // 부모의 발소리 추적 실행
            base.TrackKinematicFootsteps();

            // 복원
            if (isDragWalk && !originalLockOn && character.IsServer)
                character.characterNetworkManager.isLockedOn.Value = originalLockOn;
        }
    }
}