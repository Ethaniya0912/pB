using System.Collections.Generic;
using UnityEngine;
using TDA.Cameras;
using TDA.World;

namespace TDA.Character
{
    /// <summary>
    /// [L3 Domain / Base Class]
    /// QTE(Quick Time Event)의 공통 상태 관리, 단계 진행, 성공·실패 분기 로직을 담당합니다.
    /// Player와 AI 모두 이 클래스를 상속받아 각자의 입력 방식을 구현합니다.
    ///
    /// [아키텍처 철학]
    /// - 단계(Phase) 데이터는 CameraQTEPhaseData 리스트로 SO에서 공급됩니다.
    /// - 카메라 분기는 WorldCameraManager.StartQTE/ResolveQTEPhase에 위임합니다.
    /// - 직접 애니메이션을 건드리지 않고 Funnel(L4) 패턴으로 리액션을 발동합니다.
    /// - NGO 서버 권위: 입력 판정은 Owner에서, 결과 동기화는 ServerRpc로 처리합니다.
    /// </summary>
    public class CharacterQTEManager : MonoBehaviour
    {
        // ── 레퍼런스 ─────────────────────────────────────────────────────────
        protected CharacterManager character;

        // ── QTE 상태 ──────────────────────────────────────────────────────────
        [Header("QTE Runtime State (Read-Only)")]
        [Tooltip("현재 QTE가 진행 중인지 여부입니다.")]
        public bool isQTEActive = false;

        [Tooltip("현재 진행 중인 QTE의 단계 인덱스 (0-based)입니다.")]
        public int currentPhaseIndex = -1;

        [Tooltip("현재 QTE의 피대상 캐릭터(처형당하는 쪽)입니다.")]
        public CharacterManager qteTarget;

        // ── 내부 상태 ─────────────────────────────────────────────────────────
        protected List<CameraQTEPhaseData> activePhases;
        protected float inputWindowTimer    = 0f;
        protected bool  waitingForInput     = false;

        // ── 설정값 ────────────────────────────────────────────────────────────
        [Header("QTE Settings")]
        [Tooltip("QTE 실패 시 적용할 경직 ActionID입니다.")]
        [SerializeField] protected int qteFailStaggerActionID = (int)ActionID.Guard_Break_Stun;

        [Tooltip("디버그 로그 활성화 여부입니다.")]
        [SerializeField] public bool showDebugLogs = true;

        // ─────────────────────────────────────────────────────────────────────
        #region Unity Lifecycle
        // ─────────────────────────────────────────────────────────────────────
        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
        }

        protected virtual void Update()
        {
            if (!isQTEActive || !waitingForInput) return;

            // 입력 허용 시간 카운트다운
            inputWindowTimer -= Time.deltaTime;

            if (inputWindowTimer <= 0f)
            {
                // 시간 초과 → 실패 처리
                if (showDebugLogs)
                    Debug.Log($"<color=orange>[CharacterQTEManager]</color> {gameObject.name}: " +
                              $"Phase {currentPhaseIndex} 입력 시간 초과 → 실패");
                ResolveCurrentPhase(false);
            }
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Public API — QTE 진입 / 종료
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// QTE를 시작합니다. 단계 리스트와 대상 캐릭터를 받아 첫 번째 단계를 진행합니다.
        /// PlayerCombatManager 또는 AICharacterCombatManager에서 처형 조건 충족 시 호출합니다.
        /// </summary>
        public virtual void StartQTE(CharacterManager target, List<CameraQTEPhaseData> phases)
        {
            if (phases == null || phases.Count == 0)
            {
                Debug.LogWarning($"[CharacterQTEManager] {gameObject.name}: QTE 단계 데이터가 없습니다.");
                return;
            }

            activePhases      = phases;
            qteTarget         = target;
            currentPhaseIndex = 0;
            isQTEActive       = true;

            // 이동·회전 잠금
            LockCharacterMovement(true);

            // 카메라 시스템에 QTE 시작 알림
            if (WorldCameraManager.Instance != null)
                WorldCameraManager.Instance.StartQTE(activePhases);

            if (showDebugLogs)
                Debug.Log($"<color=cyan>[CharacterQTEManager]</color> {gameObject.name}: " +
                          $"QTE 시작 — {phases.Count}단계, 대상: {target.gameObject.name}");

            // 첫 번째 단계 입력 창 열기
            AdvanceToPhase(0);
        }

        /// <summary>
        /// 현재 단계를 강제로 종료합니다 (사망, 인터럽트 등 외부 요인).
        /// </summary>
        public virtual void ForceEndQTE()
        {
            if (!isQTEActive) return;

            if (showDebugLogs)
                Debug.Log($"<color=red>[CharacterQTEManager]</color> {gameObject.name}: QTE 강제 종료");

            CleanUpQTE(false);
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 단계 진행 내부 로직
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 특정 인덱스의 QTE 단계로 진입합니다.
        /// 입력 창 시간이 0이면 타이밍 없이 자동 진행합니다 (연출 전용 단계).
        /// </summary>
        protected virtual void AdvanceToPhase(int phaseIndex)
        {
            if (activePhases == null || phaseIndex >= activePhases.Count) return;

            currentPhaseIndex = phaseIndex;
            CameraQTEPhaseData phase = activePhases[phaseIndex];

            if (showDebugLogs)
                Debug.Log($"<color=cyan>[CharacterQTEManager]</color> Phase {phaseIndex} (입력창: {phase.inputWindowDuration}s)");

            if (phase.inputWindowDuration <= 0f)
            {
                // 입력 없는 연출 전용 단계 → 자동 성공 진행
                ResolveCurrentPhase(true);
            }
            else
            {
                // 입력 대기 시작
                inputWindowTimer = phase.inputWindowDuration;
                waitingForInput  = true;

                OnInputWindowOpened(phase);
            }
        }

        /// <summary>
        /// 현재 단계의 결과(성공/실패)를 처리합니다.
        /// 하위 클래스(PlayerQTEManager)에서 입력 감지 후 이 메서드를 호출합니다.
        /// </summary>
        protected virtual void ResolveCurrentPhase(bool success)
        {
            waitingForInput = false;

            // 카메라에 결과 전달
            if (WorldCameraManager.Instance != null)
                WorldCameraManager.Instance.ResolveQTEPhase(success);

            if (!success)
            {
                // 실패 → QTE 중단, 경직 적용
                OnQTEFailed();
                CleanUpQTE(false);
                return;
            }

            // 성공 처리
            bool isLastPhase = (currentPhaseIndex >= activePhases.Count - 1);

            if (isLastPhase)
            {
                // 마지막 단계 성공 → QTE 완전 완료
                OnQTECompleted();
                CleanUpQTE(true);
            }
            else
            {
                // 다음 단계 진행
                AdvanceToPhase(currentPhaseIndex + 1);
            }
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 하위 클래스 훅 (Virtual Events)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>입력 창이 열렸을 때 호출됩니다. 하위 클래스에서 UI/이펙트 등을 처리합니다.</summary>
        protected virtual void OnInputWindowOpened(CameraQTEPhaseData phase) { }

        /// <summary>QTE 전체 성공 시 호출됩니다.</summary>
        protected virtual void OnQTECompleted()
        {
            if (showDebugLogs)
                Debug.Log($"<color=lime>[CharacterQTEManager]</color> {gameObject.name}: QTE 완료!");
        }

        /// <summary>QTE 실패 시 호출됩니다. 기본: 경직 모션 재생.</summary>
        protected virtual void OnQTEFailed()
        {
            if (showDebugLogs)
                Debug.Log($"<color=red>[CharacterQTEManager]</color> {gameObject.name}: QTE 실패 — 경직 발생");

            // Funnel을 통해 경직 모션 재생
            if (character?.characterAnimationManager != null)
                character.characterAnimationManager.PlayTargetHitReactionFunnel(qteFailStaggerActionID);
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region 정리 및 유틸리티
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// QTE 종료 시 모든 상태를 초기화합니다.
        /// </summary>
        protected virtual void CleanUpQTE(bool wasSuccess)
        {
            isQTEActive       = false;
            currentPhaseIndex = -1;
            waitingForInput   = false;
            inputWindowTimer  = 0f;
            activePhases      = null;

            // 이동·회전 복원
            LockCharacterMovement(false);

            if (showDebugLogs)
                Debug.Log($"<color=gray>[CharacterQTEManager]</color> {gameObject.name}: " +
                          $"QTE 정리 완료 (결과: {(wasSuccess ? "성공" : "실패")})");
        }

        /// <summary>QTE 중 캐릭터 이동 및 회전을 잠그거나 복원합니다.</summary>
        protected virtual void LockCharacterMovement(bool shouldLock)
        {
            if (character == null) return;
            character.canMove   = !shouldLock;
            character.canRotate = !shouldLock;
        }
        #endregion
    }
}
