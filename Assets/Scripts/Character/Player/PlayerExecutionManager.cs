// =============================================================================
// PlayerExecutionManager.cs  |  TDA Project
// Layer  : L3 Domain — Player Specialization
//
// 수정 이력:
//   [추가] finisherDataList — FinisherData SO 목록 (인스펙터 연결)
//   [추가] currentFinisherData — 현재 처형에 사용할 SO 캐시
//   [추가] ResolveFinisherData() — 대상 티어 기반 SO 선택
//   [추가] DetermineTargetTier() — combatLevel → ExecutionTargetTier 변환
//          (기존 ResolveQTEPhasesForTarget() 의 하드코딩 분리)
//   [추가] ResolveQTEPhases() — DetermineTargetTier() 를 활용한 QTE 분기 교체
//   [추가] BeginExecution() — victim 워핑 + 물리 비활성화 + Animator 동시 재생
//   [추가] EndExecution() — 물리 복원 + 이동 플래그 복원
//          (base.EndExecution() 은 IsServer 게이트이므로
//           물리 복원을 먼저 수행하고 NetworkVariable 변경은 서버가 처리)
//   [추가] DisablePhysicsForExecution() / RestorePhysicsAfterExecution() 헬퍼
//   [추가] OnQTEFailed() 에서도 물리 복원 호출
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.Cameras;
using TDA.World;
using Unity.Netcode;          // NetworkObject (L98, L102)
using TDA.Character.AI;       // AICharacterManager, AICharacterCombatManager

namespace TDA.Character.Player
{
    /// <summary>
    /// [L3 Domain / Player Specialization]
    /// CharacterExecutionManager를 상속받아 플레이어 전용 처형 진입 조건,
    /// QTE 연동, 카메라 시퀀스 선택, 처형 후 보상 처리를 추가합니다.
    ///
    /// [플레이어 특화 책임]
    /// 1. 처형 입력 감지 및 AttemptExecution() 진입점 제공
    /// 2. 적의 종류(AI 레벨/체형)에 따라 QTE 단계 수를 다르게 설정
    /// 3. QTE 완료/실패를 PlayerQTEManager로부터 콜백으로 받아 처리
    /// 4. 처형 성공 시 카메라 피니시 시퀀스 재생
    /// </summary>
    public class PlayerExecutionManager : CharacterExecutionManager
    {
        // ── 레퍼런스 ─────────────────────────────────────────────────────────
        private PlayerManager player;
        private PlayerQTEManager playerQTEManager;

        // ── 처형 카메라 SO ───────────────────────────────────────────────────
        [Header("Execution Camera Sequences")]
        [Tooltip("처형 진입 시 재생할 카메라 시퀀스 SO입니다.")]
        public CameraSequencePresetSO executionEntrySequence;

        [Tooltip("처형 완료 피니시 시 재생할 카메라 시퀀스 SO입니다.")]
        public CameraSequencePresetSO executionFinishSequence;

        // ── QTE 단계 설정 ────────────────────────────────────────────────────
        [Header("QTE Phase Configurations")]
        [Tooltip("일반 적(체력 낮은 인간형) 처형 시 사용할 QTE 단계 목록입니다. (0개=QTE 없음, 즉결 처형)")]
        public List<CameraQTEPhaseData> humanoidQTEPhases = new List<CameraQTEPhaseData>();

        [Tooltip("대형 몬스터(트롤, 오거 등) 처형 시 사용할 QTE 단계 목록입니다.")]
        public List<CameraQTEPhaseData> largeMonsterQTEPhases = new List<CameraQTEPhaseData>();

        [Tooltip("보스 처형 시 사용할 QTE 단계 목록입니다.")]
        public List<CameraQTEPhaseData> bossQTEPhases = new List<CameraQTEPhaseData>();

        // ── [신규] FinisherData SO 목록 ─────────────────────────────────────
        [Header("Finisher Data (SO-Driven)")]
        [Tooltip("처형 종류별 FinisherData SO 목록.\n" +
                 "각 SO 의 targetTier 에 따라 BeginExecution() 시점에 자동 선택됩니다.\n" +
                 "같은 티어가 여러 개면 첫 번째가 선택됩니다.")]
        public List<FinisherData> finisherDataList = new List<FinisherData>();

        /// <summary>현재 처형에 사용 중인 FinisherData SO. BeginExecution() 에서 설정됩니다.</summary>
        private FinisherData currentFinisherData;

        // ── 처형 가능 상태 피드백 ────────────────────────────────────────────
        [Header("Execution Opportunity")]
        [Tooltip("처형이 가능한 상태인지 여부입니다. PlayerCombatManager가 참조합니다.")]
        public bool isExecutionOpportunityActive = false;

        [Tooltip("처형 기회가 남아있는 시간입니다. 0이 되면 기회 종료.")]
        public float executionOpportunityTimer = 0f;

        [SerializeField] private float executionOpportunityDuration = 3.0f;

        // ─────────────────────────────────────────────────────────────────────
        #region Unity Lifecycle
        // ─────────────────────────────────────────────────────────────────────
        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();
        }

        private void Start()
        {
            playerQTEManager = GetComponent<PlayerQTEManager>();
        }

        protected override void Update()
        {
            // 처형 기회 타이머 감산
            if (isExecutionOpportunityActive)
            {
                executionOpportunityTimer -= Time.deltaTime;
                if (executionOpportunityTimer <= 0f)
                    ClearExecutionOpportunity();
            }
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 처형 진입점 — 외부 호출
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 플레이어가 처형을 시도합니다.
        /// PlayerCombatManager에서 처형 입력 시 호출합니다.
        /// </summary>
        public void AttemptExecution(CharacterManager target)
        {
            if (!player.IsOwner) return;
            if (!CanAttemptExecution(target)) return;

            if (showDebugLogs)
                Debug.Log($"<color=lime>[PlayerExecutionManager]</color> " +
                          $"처형 시도: {target.name}");

            // 서버에 처형 시작 요청 → NotifyExecutionStartClientRpc로 양쪽 동기화
            NetworkObject targetNetObj = target.GetComponent<NetworkObject>();
            if (targetNetObj == null) return;

            RequestExecutionStartServerRpc(
                GetComponent<NetworkObject>().NetworkObjectId,
                targetNetObj.NetworkObjectId);
        }

        /// <summary>
        /// TakeDamageEffect에서 포이즈 파괴 후 호출합니다.
        /// 일정 시간 동안 처형 기회를 활성화합니다.
        /// </summary>
        public void ActivateExecutionOpportunity(CharacterManager target)
        {
            isExecutionOpportunityActive = true;
            executionOpportunityTimer = executionOpportunityDuration;

            if (showDebugLogs)
                Debug.Log($"<color=yellow>[PlayerExecutionManager]</color> " +
                          $"처형 기회 활성화 — {target.name} ({executionOpportunityDuration}s)");

            // TODO: UI에 처형 프롬프트 표시
            // player.playerUIManager?.ShowExecutionPrompt();
        }

        private void ClearExecutionOpportunity()
        {
            isExecutionOpportunityActive = false;
            executionOpportunityTimer = 0f;

            // TODO: UI 처형 프롬프트 숨기기
            // player.playerUIManager?.HideExecutionPrompt();
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 훅 오버라이드
        // ─────────────────────────────────────────────────────────────────────

        public override void BeginExecution(CharacterManager target, bool asAttacker)
        {
            base.BeginExecution(target, asAttacker);

            if (!asAttacker) return; // 공격자 측에서만 카메라·QTE 처리

            // ── [신규] FinisherData SO 선택 ──────────────────────────────────
            currentFinisherData = ResolveFinisherData(target);

            // ── [신규] 물리 충돌 비활성화 ────────────────────────────────────
            // CharacterController off + 두 캐릭터 충돌 무시
            DisablePhysicsForExecution(character, target);

            // ── [신규] victim 워핑 ───────────────────────────────────────────
            // FinisherData SO 의 victimOffset 을 attacker 로컬 → 월드 좌표 변환
            if (currentFinisherData != null)
            {
                Vector3 targetPos = character.transform.TransformPoint(
                                           currentFinisherData.victimOffset);
                Quaternion targetRot = character.transform.rotation
                                       * Quaternion.Euler(0f,
                                           currentFinisherData.victimYawOffset, 0f);

                target.transform.SetPositionAndRotation(targetPos, targetRot);

                if (showDebugLogs)
                    Debug.Log($"<color=cyan>[PlayerExecutionManager]</color> " +
                              $"Victim 워핑 완료 → {targetPos}");
            }
            else
            {
                Debug.LogWarning("[PlayerExecutionManager] FinisherData 가 없어 victim 워핑 건너뜀. " +
                                 "finisherDataList 에 SO 를 추가하세요.");
            }

            // ── [신규] 두 Animator 동시 재생 ────────────────────────────────
            // Apply Root Motion 이 두 캐릭터 모두 true 여야 합니다.
            if (currentFinisherData != null
                && currentFinisherData.attackerClip != null
                && currentFinisherData.victimClip != null)
            {
                // 두 Animator 를 0프레임에서 동시 시작
                character.animator.Play(currentFinisherData.attackerClip.name, 0, 0f);
                target.animator.Play(currentFinisherData.victimClip.name, 0, 0f);

                if (showDebugLogs)
                    Debug.Log($"<color=cyan>[PlayerExecutionManager]</color> " +
                              $"Animator 동시 재생 — " +
                              $"Attacker:{currentFinisherData.attackerClip.name} " +
                              $"Victim:{currentFinisherData.victimClip.name}");
            }

            // ── 처형 진입 카메라 시퀀스 재생 (기존 유지) ─────────────────────
            if (executionEntrySequence != null && WorldCameraManager.Instance != null)
                WorldCameraManager.Instance.PlayCameraSequence(
                    executionEntrySequence, "PlayerExecution Entry");

            // ── QTE 단계 결정 및 시작 (기존 유지, DetermineTargetTier 기반으로 교체) ──
            List<CameraQTEPhaseData> phases = ResolveQTEPhases(target);

            if (phases != null && phases.Count > 0 && playerQTEManager != null)
            {
                playerQTEManager.StartQTE(target, phases);
            }
            else
            {
                // QTE 없는 즉결 처형 → 바로 처형 완료 처리
                if (showDebugLogs)
                    Debug.Log($"<color=cyan>[PlayerExecutionManager]</color> " +
                              $"즉결 처형 (QTE 없음)");
            }
        }

        /// <summary>
        /// 처형 시퀀스를 종료합니다.
        ///
        /// [주의] base.EndExecution() 은 IsServer 게이트가 있어
        /// 클라이언트에서는 isBeingExecuted.Value 변경만 막힐 뿐입니다.
        /// 물리 복원은 모든 클라이언트에서 즉시 수행해야 하므로
        /// base 호출 전에 먼저 처리합니다.
        /// </summary>
        public override void EndExecution()
        {
            // ── [신규] 물리·플래그 복원은 모든 클라이언트에서 즉시 수행 ──────
            if (executionPartner != null)
                RestorePhysicsAfterExecution(character, executionPartner);

            base.EndExecution(); // if (!IsServer) return; → isBeingExecuted.Value = false

            if (showDebugLogs)
                Debug.Log($"<color=lime>[PlayerExecutionManager]</color> EndExecution 완료");
        }

        protected override void OnExecutionFinished()
        {
            base.OnExecutionFinished();

            // 처형 완료 피니시 카메라 시퀀스
            if (executionFinishSequence != null && WorldCameraManager.Instance != null)
                WorldCameraManager.Instance.PlayCameraSequence(
                    executionFinishSequence, "PlayerExecution Finish");

            // 처형 기회 정리
            ClearExecutionOpportunity();

            // TODO: 처형 완료 보상 (경험치, 스탯 회복 등)
            // player.playerStatsManager?.OnExecutionReward();
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region QTE 콜백 (PlayerQTEManager가 호출)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>PlayerQTEManager가 QTE 전체 완료 시 호출합니다.</summary>
        public void OnQTECompleted()
        {
            if (showDebugLogs)
                Debug.Log($"<color=lime>[PlayerExecutionManager]</color> QTE 완료 → 피니시 연출 진행");

            // Pair_Damage_Apply 이벤트는 이미 애니메이션이 처리 중이므로
            // 여기서는 추가 보상 처리 등을 수행합니다.
        }

        /// <summary>PlayerQTEManager가 QTE 실패 시 호출합니다.</summary>
        public void OnQTEFailed()
        {
            if (showDebugLogs)
                Debug.Log($"<color=red>[PlayerExecutionManager]</color> QTE 실패 → 처형 중단");

            // [신규] 물리 복원 후 중단
            if (executionPartner != null)
                RestorePhysicsAfterExecution(character, executionPartner);

            // 처형 중단 정리
            CleanUpExecution();

            // 피격자 해방 (상태 잠금 해제)
            if (executionPartner?.GetComponent<CharacterExecutionManager>() != null)
                executionPartner.GetComponent<CharacterExecutionManager>().ReleaseExecutionExternal();
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 헬퍼 — FinisherData SO 선택 (데이터 드리븐)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 대상 캐릭터의 ExecutionTargetTier 에 맞는 FinisherData SO 를 반환합니다.
        ///
        /// [개선] 기존 combatLevel >= 8 하드코딩 → DetermineTargetTier() 로 위임.
        /// SO 목록에 같은 티어가 없으면 Humanoid 티어를 Fallback 으로 반환합니다.
        /// </summary>
        private FinisherData ResolveFinisherData(CharacterManager target)
        {
            if (finisherDataList == null || finisherDataList.Count == 0)
            {
                Debug.LogWarning("[PlayerExecutionManager] finisherDataList 가 비어 있습니다.");
                return null;
            }

            ExecutionTargetTier tier = DetermineTargetTier(target);

            // 티어가 일치하는 첫 번째 SO 반환
            foreach (var data in finisherDataList)
            {
                if (data != null && data.targetTier == tier)
                    return data;
            }

            // Fallback: Humanoid 티어 반환
            foreach (var data in finisherDataList)
            {
                if (data != null && data.targetTier == ExecutionTargetTier.Humanoid)
                    return data;
            }

            return finisherDataList[0]; // 최후 Fallback
        }

        /// <summary>
        /// 대상 캐릭터의 combatLevel 을 읽어 ExecutionTargetTier 를 반환합니다.
        ///
        /// [개선] QTE 분기와 FinisherData 선택이 모두 이 메서드로 일원화됩니다.
        /// 향후 CharacterManager 에 tier 필드가 직접 추가되면 이 메서드만 수정합니다.
        /// </summary>
        private ExecutionTargetTier DetermineTargetTier(CharacterManager target)
        {
            AICharacterCombatManager aiCombat =
                target.GetComponent<AICharacterCombatManager>();

            if (aiCombat == null)
                return ExecutionTargetTier.Humanoid;

            if (aiCombat.combatLevel >= 8) return ExecutionTargetTier.Boss;
            if (aiCombat.combatLevel >= 5) return ExecutionTargetTier.LargeMonster;
            return ExecutionTargetTier.Humanoid;
        }

        /// <summary>
        /// 대상 티어에 따라 QTE 단계 목록을 반환합니다.
        ///
        /// [개선] 기존 ResolveQTEPhasesForTarget() 의 combatLevel 하드코딩을
        /// DetermineTargetTier() 로 위임하여 FinisherData 선택과 동일한 분기 로직을 공유합니다.
        /// AICharacterManager에 characterTier 같은 분류 필드가 추가되면 더 정교한 분기 가능.
        /// </summary>
        private List<CameraQTEPhaseData> ResolveQTEPhases(CharacterManager target)
        {
            if (target == null) return null;

            ExecutionTargetTier tier = DetermineTargetTier(target);

            switch (tier)
            {
                case ExecutionTargetTier.Boss:
                    return bossQTEPhases.Count > 0 ? bossQTEPhases : humanoidQTEPhases;
                case ExecutionTargetTier.LargeMonster:
                    return largeMonsterQTEPhases.Count > 0
                        ? largeMonsterQTEPhases : humanoidQTEPhases;
                default:
                    return humanoidQTEPhases;
            }
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 헬퍼 — 물리 비활성화 / 복원
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 처형 중 두 캐릭터가 겹쳐도 물리 엔진이 밀어내지 않도록 설정합니다.
        ///   - CharacterController 비활성화 (Root Motion 충돌 방지)
        ///   - 두 캐릭터 콜라이더 상호 무시
        ///
        /// [순서 중요] CharacterController.enabled = false 가
        /// animator.Play() 보다 먼저 호출되어야 Root Motion 이 자유롭게 적용됩니다.
        /// </summary>
        private void DisablePhysicsForExecution(
            CharacterManager attacker, CharacterManager victim)
        {
            // CharacterController 비활성화
            if (attacker.characterController != null)
                attacker.characterController.enabled = false;
            if (victim.characterController != null)
                victim.characterController.enabled = false;

            // 두 캐릭터의 루트 Collider 상호 충돌 무시
            Collider ac = attacker.GetComponent<Collider>();
            Collider vc = victim.GetComponent<Collider>();
            if (ac != null && vc != null)
                Physics.IgnoreCollision(ac, vc, true);

            if (showDebugLogs)
                Debug.Log("<color=cyan>[PlayerExecutionManager]</color> " +
                          "물리 비활성화 완료 (CC off + IgnoreCollision)");
        }

        /// <summary>
        /// 처형 종료 후 비활성화했던 물리 컴포넌트와 이동 플래그를 복원합니다.
        /// EndExecution() 및 OnQTEFailed() 양쪽에서 호출됩니다.
        /// </summary>
        private void RestorePhysicsAfterExecution(
            CharacterManager attacker, CharacterManager victim)
        {
            // CharacterController 복원
            if (attacker.characterController != null)
                attacker.characterController.enabled = true;
            if (victim.characterController != null)
                victim.characterController.enabled = true;

            // 충돌 무시 해제
            Collider ac = attacker.GetComponent<Collider>();
            Collider vc = victim.GetComponent<Collider>();
            if (ac != null && vc != null)
                Physics.IgnoreCollision(ac, vc, false);

            // 이동·회전 플래그 복원
            attacker.canMove = true;
            attacker.canRotate = true;
            attacker.isPerformingAction = false;

            victim.canMove = true;
            victim.canRotate = true;
            victim.isPerformingAction = false;

            if (showDebugLogs)
                Debug.Log("<color=lime>[PlayerExecutionManager]</color> " +
                          "물리 복원 완료 (CC on + 플래그 초기화)");
        }
        #endregion
    }
}