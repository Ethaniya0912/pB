using System.Collections.Generic;
using UnityEngine;
using TDA.Cameras;
using TDA.World;
using Unity.Netcode;          // NetworkObject (L98, L102)
using TDA.Character.AI;       // AICharacterManager, AICharacterCombatManager (L223, L226)

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
            executionOpportunityTimer    = executionOpportunityDuration;

            if (showDebugLogs)
                Debug.Log($"<color=yellow>[PlayerExecutionManager]</color> " +
                          $"처형 기회 활성화 — {target.name} ({executionOpportunityDuration}s)");

            // TODO: UI에 처형 프롬프트 표시
            // player.playerUIManager?.ShowExecutionPrompt();
        }

        private void ClearExecutionOpportunity()
        {
            isExecutionOpportunityActive = false;
            executionOpportunityTimer    = 0f;

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

            // 처형 진입 카메라 시퀀스 재생
            if (executionEntrySequence != null && WorldCameraManager.Instance != null)
                WorldCameraManager.Instance.PlayCameraSequence(
                    executionEntrySequence, "PlayerExecution Entry");

            // QTE 단계 결정 및 시작
            List<CameraQTEPhaseData> phases = ResolveQTEPhasesForTarget(target);

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

            // 처형 중단 정리
            CleanUpExecution();

            // 피격자 해방 (상태 잠금 해제)
            if (executionPartner?.GetComponent<CharacterExecutionManager>() != null)
                executionPartner.GetComponent<CharacterExecutionManager>().ReleaseExecutionExternal();
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 헬퍼
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 대상 캐릭터의 종류에 따라 적절한 QTE 단계 리스트를 반환합니다.
        /// AICharacterManager에 characterTier 같은 분류 필드가 추가되면 더 정교한 분기 가능.
        /// </summary>
        private List<CameraQTEPhaseData> ResolveQTEPhasesForTarget(CharacterManager target)
        {
            if (target == null) return null;

            // AICharacterManager를 가져와 AI 레벨로 분기
            AICharacterManager aiMgr = target.GetComponent<AICharacterManager>();
            if (aiMgr == null) return humanoidQTEPhases; // 기본: 인간형

            AICharacterCombatManager aiCombat =
                target.GetComponent<AICharacterCombatManager>();

            if (aiCombat != null)
            {
                // 보스(combatLevel 8 이상) → 보스 QTE
                if (aiCombat.combatLevel >= 8 && bossQTEPhases.Count > 0)
                    return bossQTEPhases;

                // 강적(combatLevel 5~7) → 대형 몬스터 QTE
                if (aiCombat.combatLevel >= 5 && largeMonsterQTEPhases.Count > 0)
                    return largeMonsterQTEPhases;
            }

            // 기본: 인간형 QTE
            return humanoidQTEPhases;
        }
        #endregion
    }
}
