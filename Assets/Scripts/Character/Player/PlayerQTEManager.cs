using System.Collections.Generic;
using UnityEngine;
using TDA.Cameras;
using TDA.World;

namespace TDA.Character.Player
{
    /// <summary>
    /// [L3 Domain / Player Specialization]
    /// CharacterQTEManager를 상속받아 플레이어 전용 입력 감지와 UI 피드백을 추가합니다.
    ///
    /// [플레이어 특화 책임]
    /// 1. PlayerInputManager를 통해 QTE 전용 키 입력을 수신합니다.
    /// 2. 입력 창(InputWindow) 동안 다른 전투 입력을 차단합니다.
    /// 3. QTE 성공/실패를 ExecutionManager와 연동하여 처형 연출을 완성합니다.
    /// 4. 멀티플레이: NGO Owner 검증 후 ServerRpc로 결과를 동기화합니다.
    /// </summary>
    public class PlayerQTEManager : CharacterQTEManager
    {
        // ── 레퍼런스 ─────────────────────────────────────────────────────────
        private PlayerManager player;

        // ── 플레이어 입력 설정 ───────────────────────────────────────────────
        [Header("Player Input Settings")]
        [Tooltip("QTE 입력에 사용할 키코드 목록입니다. 단계별로 다른 키를 사용하려면 CameraQTEPhaseData의 inputKey를 활용합니다.")]
        [SerializeField] private KeyCode defaultQTEKey = KeyCode.F;

        [Tooltip("QTE 성공 시 입력 판정 오차 허용 범위입니다. 클수록 판정이 너그러워집니다.")]
        [SerializeField] private float inputLeniency = 0.1f;

        // ── 플레이어 QTE 상태 ────────────────────────────────────────────────
        [Header("Player QTE State (Read-Only)")]
        [Tooltip("현재 QTE 입력 창이 열려 있는 구간의 최적 타이밍 기준 시간입니다.")]
        public float perfectTimingCenter = 0f;

        // ── 내부 ─────────────────────────────────────────────────────────────
        private PlayerExecutionManager executionManager;

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
            // PlayerExecutionManager는 같은 GameObject에 부착됨
            executionManager = GetComponent<PlayerExecutionManager>();
        }

        protected override void Update()
        {
            base.Update(); // 시간 초과 감지 (CharacterQTEManager)

            // Owner인 플레이어에서만 입력 감지
            if (!isQTEActive || !waitingForInput) return;
            if (player == null || !player.IsOwner) return;

            HandleQTEInput();
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 입력 감지
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 매 프레임 QTE 입력을 감지합니다.
        /// PlayerInputManager 구현 후 해당 이벤트로 교체할 수 있습니다.
        /// </summary>
        private void HandleQTEInput()
        {
            // 현재 단계의 입력 키 결정
            KeyCode requiredKey = ResolveInputKeyForCurrentPhase();

            if (Input.GetKeyDown(requiredKey))
            {
                if (showDebugLogs)
                    Debug.Log($"<color=lime>[PlayerQTEManager]</color> QTE 입력 성공! " +
                              $"Phase {currentPhaseIndex} — {requiredKey} 입력됨 " +
                              $"(남은 시간: {inputWindowTimer:F2}s)");

                // 외부(PlayerInputManager)에서 호출해도 동일하게 처리됩니다.
                OnQTEInputReceived();
            }
        }

        /// <summary>
        /// PlayerInputManager 또는 다른 입력 시스템에서 직접 호출하는 공개 메서드입니다.
        /// </summary>
        public void OnQTEInputReceived()
        {
            if (!isQTEActive || !waitingForInput) return;
            if (player != null && !player.IsOwner) return;

            ResolveCurrentPhase(true);
        }

        /// <summary>
        /// 현재 단계의 요구 입력 키를 반환합니다.
        /// CameraQTEPhaseData에 inputKey 필드가 추가되면 단계별 분기 가능합니다.
        /// </summary>
        private KeyCode ResolveInputKeyForCurrentPhase()
        {
            // TODO: CameraQTEPhaseData.inputKey 필드 추가 후 단계별 키 분기
            // if (activePhases != null && currentPhaseIndex < activePhases.Count)
            //     return activePhases[currentPhaseIndex].inputKey;
            return defaultQTEKey;
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 훅 오버라이드
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnInputWindowOpened(CameraQTEPhaseData phase)
        {
            base.OnInputWindowOpened(phase);

            // 입력 창 중간 지점을 퍼펙트 타이밍 기준으로 설정
            perfectTimingCenter = phase.inputWindowDuration * 0.5f;

            // TODO: UI 매니저에 QTE 입력 창 표시 요청
            // player.playerUIManager?.ShowQTEPrompt(phase.inputWindowDuration, requiredKey);

            if (showDebugLogs)
                Debug.Log($"<color=cyan>[PlayerQTEManager]</color> Phase {currentPhaseIndex} " +
                          $"입력 창 오픈 — Phase {currentPhaseIndex} (입력창: {phase.inputWindowDuration}s)");
        }

        protected override void OnQTECompleted()
        {
            base.OnQTECompleted();

            // TODO: UI 매니저에 QTE 완료 표시 요청
            // player.playerUIManager?.HideQTEPrompt();

            // ExecutionManager에 처형 완료 신호 전달
            if (executionManager != null)
                executionManager.OnQTECompleted();
        }

        protected override void OnQTEFailed()
        {
            base.OnQTEFailed();

            // TODO: UI 매니저에 QTE 실패 표시 요청
            // player.playerUIManager?.ShowQTEFailFeedback();

            // ExecutionManager에 처형 중단 신호 전달
            if (executionManager != null)
                executionManager.OnQTEFailed();
        }

        protected override void CleanUpQTE(bool wasSuccess)
        {
            base.CleanUpQTE(wasSuccess);

            perfectTimingCenter = 0f;

            // QTE 중 차단했던 전투 입력 복원
            if (player != null)
            {
                player.isPerformingAction = false;
            }
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 편의 메서드
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 단계의 입력 시간 내 진행도를 0~1로 반환합니다. (UI 게이지 바 등에 활용)
        /// </summary>
        public float GetCurrentPhaseProgress()
        {
            if (!isQTEActive || !waitingForInput || activePhases == null) return 0f;

            float totalDuration = activePhases[currentPhaseIndex].inputWindowDuration;
            if (totalDuration <= 0f) return 0f;

            return 1f - Mathf.Clamp01(inputWindowTimer / totalDuration);
        }
        #endregion
    }
}
