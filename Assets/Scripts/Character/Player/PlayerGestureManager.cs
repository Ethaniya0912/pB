using UnityEngine;
using UnityEngine.InputSystem;

namespace TDA.Character.Player
{
    /// <summary>
    /// [L1 Input Layer] 마우스 드래그 궤적을 추적하여 제스처를 판별하는 매니저입니다.
    /// [P0-03 Phase 2] 파지 상태(Stance) 추적, 차징(Charging) 타이머, 엇박자 절기(Fumble) 시스템이 완벽하게 적용되었습니다.
    /// </summary>
    public class PlayerGestureManager : MonoBehaviour
    {
        private PlayerManager player;

        [Header("Weapon Stance (P0-03)")]
        [Tooltip("현재 무기를 쥐고 있는 방향입니다. 공격이 끝나면 이 상태가 갱신되며, 다음 공격 궤적의 물리적 논리를 검증합니다.")]
        public global::WeaponStance currentStance = global::WeaponStance.RightHold;

        [Header("Action ID Mapping")]
        [Tooltip("우에서 좌로 긋는 기본 베기 (ActionState)")]
        public int swipeLeftActionID = 1;
        [Tooltip("우에서 좌로 긋는 강공격/차징 (ActionState)")]
        public int swipeLeftHeavyActionID = 11;

        [Tooltip("좌에서 우로 긋는 기본 베기 (ActionState)")]
        public int swipeRightActionID = 2;
        [Tooltip("좌에서 우로 긋는 강공격/차징 (ActionState)")]
        public int swipeRightHeavyActionID = 12;

        [Tooltip("짧게 클릭했을 때 나가는 기본 평타 (ActionState)")]
        public int clickActionID = 1;

        [Tooltip("엇박자 조작 실패 시 강제 재생될 절기(Fumble) 모션의 ActionState ID")]
        public int fumbleActionID = 99;

        [Header("Charging System (P0-03)")]
        [Tooltip("강공격(차징)으로 인정되기 위해 마우스를 누르고 있어야 하는 최소 시간(초)")]
        [SerializeField] private float chargeThreshold = 0.35f;
        [Tooltip("차징 비율(ChargeRatio) 계산을 위한 최대 허용 시간(초)")]
        [SerializeField] private float maxChargeTime = 2.0f;

        private float clickStartTime = 0f;
        private bool isCharging = false;

        [Header("Penalty Settings")]
        [Tooltip("절기(Fumble) 발생 시 차감될 스태미나 페널티 양")]
        [SerializeField] private float fumbleStaminaPenalty = 25f;

        [Header("Gesture & Stance Settings")]
        [SerializeField] private float minimumDragDistance = 50f;
        [SerializeField] private float maxStanceHoldTime = 3.0f; // 더 이상 자동 해제에 사용되지 않지만, 다른 기믹용으로 유지
        private float currentStanceTimer = 0f;

        [Header("Input Queueing System (선입력)")]
        public bool enableInputQueueing = true;
        [SerializeField] private float inputQueueWindow = 1.0f;

        private int queuedActionID = 0;
        private float queueExpirationTime = 0f;

        [Header("Debug Visuals")]
        public bool show2DDragLine = true;
        public bool show3DWeaponTrajectory = true;

        private Vector2 dragStartPosition;
        private Vector2 dragCurrentPosition;
        private bool isDragging = false;

        private Vector3 debugStartArc;
        private Vector3 debugEndArc;
        private Vector3 debugChestCenter;
        private float debugGizmoTimer = 0f;

        public bool IsDragging => isDragging;

        private Texture2D lineTexture;

        private void Awake()
        {
            player = GetComponent<PlayerManager>();

            lineTexture = new Texture2D(1, 1);
            lineTexture.SetPixel(0, 0, Color.white);
            lineTexture.Apply();
        }

        private void Update()
        {
            HandleStanceState();
            HandleInputQueue();
            UpdateDebugGizmos();
            HandleRawMouseGesture();
        }

        /// <summary>
        /// 자세(Stance) 유지 중일 때, 이동 입력이나 강한 회전이 들어올 때만 강제로 자세를 푸는 로직입니다. (장기전 대비 유지 시간 무한)
        /// </summary>
        private void HandleStanceState()
        {
            if (player.animator == null) return;

            // 🚨 [Action_Ended 무한 루프 버그 픽스] 
            // 이미 Empty 상태로 넘어가고 있는(트랜지션 중) 찰나의 시간 동안 
            // CrossFade가 매 프레임 중복 호출되는 것을 원천 차단합니다.
            if (player.animator.IsInTransition(1)) return;

            // [치명적 버그 픽스] canCombo 변수에 의존하지 않고, 실제 애니메이터 1번 레이어(Action Override)의 상태를 직접 감시합니다.
            AnimatorStateInfo actionState = player.animator.GetCurrentAnimatorStateInfo(1);
            bool isHoldingStance = actionState.IsName("Stance_Hold_Left") || actionState.IsName("Stance_Hold_Right");

            if (isHoldingStance)
            {
                currentStanceTimer += Time.deltaTime;

                bool isMoving = player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f;

                // 마우스를 크게 돌려 제자리 턴(Turn)이 발생하려 할 때도 Stance 락을 풀어주어 무한 루프를 방지합니다.
                bool isTurning = Mathf.Abs(player.animator.GetFloat("turnAngle")) > 30f;

                // [장기전 대비 픽스] 시간 초과(currentStanceTimer >= maxStanceHoldTime) 조건을 제거했습니다. 
                // 이제 유저가 이동하거나 고개를 크게 돌리지 않는 한 영구적으로 파지 자세를 유지합니다.
                if (isMoving || isTurning)
                {
                    Debug.Log($"<color=gray>[Stance 종료]</color> {(isMoving ? "이동 입력" : "회전 감지")}. 무기를 내립니다.");

                    if (player.playerCombatManager != null)
                    {
                        player.playerCombatManager.canComboWithMainHandWeapon = false;
                        player.playerCombatManager.canComboWithOffHandWeapon = false;
                    }

                    // Action Override 레이어(1번)를 강제로 비워 Base Layer의 턴/이동 모션이 적용되게 만듭니다.
                    player.animator.CrossFade("Empty", 0.25f, 1);

                    player.isPerformingAction = false;
                    player.canMove = true;
                    player.canRotate = true;

                    currentStanceTimer = 0f;
                    queuedActionID = 0;

                    // 완전히 자세가 풀렸으므로 기본 우측 파지로 복구
                    currentStance = global::WeaponStance.RightHold;
                }
            }
            else
            {
                currentStanceTimer = 0f;
            }
        }

        private void HandleInputQueue()
        {
            if (!enableInputQueueing)
            {
                queuedActionID = 0;
                return;
            }

            if (queuedActionID != 0)
            {
                if (Time.time > queueExpirationTime)
                {
                    Debug.Log("<color=gray>[Gesture 큐잉 소멸]</color> 입력 기억 시간이 초과되어 초기화됩니다.");
                    queuedActionID = 0;
                    return;
                }

                bool canCombo = player.playerCombatManager != null &&
                               (player.playerCombatManager.canComboWithMainHandWeapon || player.playerCombatManager.canComboWithOffHandWeapon);

                if (!player.isPerformingAction || canCombo)
                {
                    Debug.Log($"<color=lime>[Gesture 큐잉 발동!]</color> 콤보 타이밍 도래! 예약된 액션(ActionID: {queuedActionID}) 실행!");
                    player.playerCombatManager.PerformDirectionalAttack(queuedActionID);
                    queuedActionID = 0;
                }
            }
        }

        private void HandleRawMouseGesture()
        {
            if (Mouse.current == null) return;

            if (player.playerInteractionManager != null && player.playerInteractionManager.currentlyHeldObject != null)
            {
                isDragging = false;
                return;
            }

            if (player.playerDefenseManager != null && player.playerDefenseManager.isDefending)
            {
                isDragging = false;
                return;
            }

            // 1. 마우스 누름 (제스처 및 차징 타이머 시작)
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                // [버그 픽스] 게임 뷰에서 커서가 중앙에 잠겨있을 때(CursorLockMode.Locked) 
                // Mouse.position이 갱신되지 않는 문제를 해결하기 위해 시작점을 화면 중앙으로 강제 세팅합니다.
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    dragStartPosition = new Vector2(Screen.width / 2f, Screen.height / 2f);
                }
                else
                {
                    dragStartPosition = Mouse.current.position.ReadValue();
                }

                dragCurrentPosition = dragStartPosition;

                isDragging = true;
                isCharging = false;
                clickStartTime = Time.time;

                Debug.Log($"<color=lime>[Gesture 시작]</color> 궤적 및 차지 타이머 추적을 시작합니다.");
            }

            // 2. 마우스 누르고 있는 중 (차징 스케일링)
            if (isDragging && Mouse.current.leftButton.isPressed)
            {
                // [버그 픽스] Mouse.position 대신 매 프레임의 마우스 이동량(Delta)을 누적합니다.
                // 이렇게 하면 커서가 중앙에 묶여 있어도 제스처 궤적을 완벽하게 그릴 수 있습니다!
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                dragCurrentPosition += mouseDelta;

                float holdDuration = Time.time - clickStartTime;

                // 차징 임계치 돌파 시
                if (!isCharging && holdDuration >= chargeThreshold)
                {
                    isCharging = true;
                    if (player.animator != null) player.animator.SetBool("isCharging", true);

                    if (player.characterEventManager != null)
                    {
                        player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Charge_Started);
                        Debug.Log("<color=magenta>[Charging]</color> 강공격 기 모으기 시작!");
                    }
                }

                // 차징 중 애니메이터에 비율(Ratio) 전달 (근육이 더 꼬이는 시각적 블렌드 트리용)
                if (isCharging && player.animator != null)
                {
                    float chargeRatio = Mathf.Clamp01((holdDuration - chargeThreshold) / (maxChargeTime - chargeThreshold));
                    player.animator.SetFloat("ChargeRatio", chargeRatio);
                }
            }

            // 3. 마우스 뗌 (판별 및 실행)
            if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;

                // 차징 상태 초기화
                if (player.animator != null)
                {
                    player.animator.SetBool("isCharging", false);
                    player.animator.SetFloat("ChargeRatio", 0f);
                }

                AnalyzeGesture(dragStartPosition, dragCurrentPosition, isCharging);
            }
        }

        /// <summary>
        /// [Phase 2 핵심] 궤적을 분석하고, 차징 여부와 파지 상태(WeaponStance)를 교차 검증합니다.
        /// </summary>
        private void AnalyzeGesture(Vector2 startPos, Vector2 endPos, bool isChargeAttack)
        {
            Vector2 dragVector = endPos - startPos;
            int targetActionID = clickActionID;
            string determinedDirection = isChargeAttack ? "차지 평타" : "기본 평타(짧은 클릭)";

            if (dragVector.magnitude >= minimumDragDistance)
            {
                float dragAngle = Mathf.Atan2(dragVector.y, dragVector.x) * Mathf.Rad2Deg;
                bool isDraggingRightToLeft = (dragVector.x < 0); // 우 -> 좌 (왼쪽으로 베기)
                bool isDraggingLeftToRight = (dragVector.x > 0); // 좌 -> 우 (오른쪽으로 베기)

                if (Mathf.Abs(dragAngle) < 45f || Mathf.Abs(dragAngle) > 135f)
                {
                    // =========================================================================
                    // 🚨 [엇박자 차단 가드] 파지 상태와 궤적의 엄격한 교차 검증 (Cross-Validation)
                    // =========================================================================
                    if (currentStance == global::WeaponStance.RightHold)
                    {
                        if (isDraggingLeftToRight)
                        {
                            ExecuteFumblePenalty("오른쪽에 무기가 있는데 우측으로 더 비틀어 베기를 시도함 (물리적 모순)");
                            return;
                        }

                        targetActionID = isChargeAttack ? swipeLeftHeavyActionID : swipeLeftActionID;
                        currentStance = global::WeaponStance.LeftHold;
                        determinedDirection = isChargeAttack ? "우->좌 강공격 (Left Hold 전환)" : "우->좌 베기 (Left Hold 전환)";
                    }
                    else if (currentStance == global::WeaponStance.LeftHold)
                    {
                        if (isDraggingRightToLeft)
                        {
                            ExecuteFumblePenalty("왼쪽에 무기가 있는데 좌측으로 더 비틀어 베기를 시도함 (물리적 모순)");
                            return;
                        }

                        targetActionID = isChargeAttack ? swipeRightHeavyActionID : swipeRightActionID;
                        currentStance = global::WeaponStance.RightHold;
                        determinedDirection = isChargeAttack ? "좌->우 강공격 (Right Hold 전환)" : "좌->우 베기 (Right Hold 전환)";
                    }
                    // =========================================================================
                }
                else
                {
                    // 상하 드래그는 아직 미구현
                    targetActionID = clickActionID;
                    determinedDirection = dragVector.y > 0 ? "상단 (미구현)" : "하단 (미구현)";
                }

                Debug.Log($"<color=cyan>[Gesture 분석 완료]</color> 판정: <color=yellow><b>{determinedDirection}</b></color> | 궤적: {dragAngle:F1}도 | 거리: {dragVector.magnitude:F1}px");
            }

            // 콤보 및 액션 실행 라우팅
            bool canCombo = player.playerCombatManager != null &&
                           (player.playerCombatManager.canComboWithMainHandWeapon || player.playerCombatManager.canComboWithOffHandWeapon);

            // 🚨 [버그 픽스 1 & 2] 즉시 실행 예외 조건 추가
            // 1. 제자리 턴(Turn) 중일 때 유저가 공격을 입력하면 즉시 턴을 캔슬하고 나갈 수 있도록 뚫어줍니다.
            bool isTurning = false;
            if (player.animator != null)
            {
                AnimatorStateInfo baseState = player.animator.GetCurrentAnimatorStateInfo(0);
                AnimatorStateInfo nextState = player.animator.GetNextAnimatorStateInfo(0); // [추가] 트랜지션 전환 중인 찰나의 상태도 감지

                if (baseState.IsName("Turn_Left_90") || baseState.IsName("Turn_Right_90") ||
                    baseState.IsName("Turn_Left_180") || baseState.IsName("Turn_Right_180") ||
                    nextState.IsName("Turn_Left_90") || nextState.IsName("Turn_Right_90") ||
                    nextState.IsName("Turn_Left_180") || nextState.IsName("Turn_Right_180"))
                {
                    isTurning = true;
                }
            }

            // 2. 강공격(isChargeAttack)으로 마우스를 뗀 순간이라면, 이미 차징(Wind-up) 중이었으므로 콤보 허용 여부와 무관하게 무조건 타격으로 넘어갑니다.
            if (!player.isPerformingAction || canCombo || isChargeAttack || isTurning)
            {
                Debug.Log($"<color=yellow>[Gesture 즉시 실행]</color> 애니메이션 실행 요청! (ActionID: {targetActionID})");
                player.playerCombatManager.PerformDirectionalAttack(targetActionID);
            }
            else
            {
                if (enableInputQueueing)
                {
                    queuedActionID = targetActionID;
                    queueExpirationTime = Time.time + inputQueueWindow;
                    Debug.Log($"<color=orange>[Gesture 큐잉됨(예약)]</color> 콤보 대기 중. {inputQueueWindow}초 동안 <color=white>'{determinedDirection}'</color>(ID:{targetActionID}) 보관.");
                }
                else
                {
                    Debug.Log($"<color=red>[Gesture 무시됨]</color> 인풋 큐잉 OFF & 콤보 타이밍 아님.");
                }
            }
        }

        /// <summary>
        /// [P0-03 핵심] 엇박자 입력 시 발생하는 치명적인 페널티 집행 로직
        /// </summary>
        private void ExecuteFumblePenalty(string debugMsg)
        {
            Debug.Log($"<color=red>[Fumble/절기]</color> 조작 실패! 사유: {debugMsg}");

            // 1. 자원 페널티: 스태미나 대폭 차감
            if (player.playerNetworkManager != null)
            {
                // [테스트 포인트] 네트워크 동기화가 걸린 스태미나 차감이 정상 작동하는지 확인
                player.playerNetworkManager.currentStamina.Value -= fumbleStaminaPenalty;
            }

            // 2. 취약 상태: 콤보 강제 해제
            if (player.playerCombatManager != null)
            {
                player.playerCombatManager.canComboWithMainHandWeapon = false;
                player.playerCombatManager.canComboWithOffHandWeapon = false;
            }

            // 3. 강제 애니메이션 재생 (ActionID: 99 - 근육이 꼬여 휘청거리는 모션)
            // Funnel을 태워서 applyRootMotion=true 상태로 밀어버림
            player.playerAnimationManager.PlayTargetActionFunnel(fumbleActionID, true, true, false, false);

            // 4. 극한의 시청각 피드백 방송
            if (player.characterEventManager != null)
            {
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlayVoice_Stagger_Pain);
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.CameraShake_Heavy_Fumble);
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.ScreenFX_Hit_Vignette);
            }

            // 5. 후속 입력 방지를 위한 상태 초기화
            queuedActionID = 0;
            isDragging = false;
            isCharging = false;
        }

        // =========================================================================================
        // [디버깅] 2D 라인 및 3D 화살표 기즈모 시각화
        // =========================================================================================

        private void UpdateDebugGizmos()
        {
            if (isDragging && dragStartPosition != dragCurrentPosition)
            {
                Vector2 dragVector = dragCurrentPosition - dragStartPosition;
                if (dragVector.magnitude >= minimumDragDistance)
                {
                    Calculate3DGizmoPoints(dragVector);
                    debugGizmoTimer = 1.5f;
                }
            }
            else if (debugGizmoTimer > 0)
            {
                debugGizmoTimer -= Time.deltaTime;
            }
        }

        private void Calculate3DGizmoPoints(Vector2 dragVector)
        {
            if (player == null) return;
            Vector3 playerForward = player.transform.forward;
            Vector3 playerRight = player.transform.right;
            Vector3 playerUp = player.transform.up;

            float weaponReach = 1.5f;
            debugChestCenter = player.transform.position + (playerUp * 1.2f);

            Vector2 normDrag = dragVector.normalized;
            Vector3 swipeDirection = (playerRight * normDrag.x) + (playerUp * normDrag.y);

            debugStartArc = debugChestCenter + (playerForward * weaponReach) - (swipeDirection * 1.0f);
            debugEndArc = debugChestCenter + (playerForward * weaponReach) + (swipeDirection * 1.0f);
        }

        private void OnGUI()
        {
            if (!isDragging || !show2DDragLine) return;

            Vector2 start = new Vector2(dragStartPosition.x, Screen.height - dragStartPosition.y);
            Vector2 end = new Vector2(dragCurrentPosition.x, Screen.height - dragCurrentPosition.y);

            // 차징 중이라면 선 색상을 붉은빛(충전됨)으로 변경
            Color lineColor = isCharging ? new Color(1f, 0.2f, 0.2f) : Color.red;
            float lineWidth = isCharging ? 5f : 3f;

            DrawGUILine(start, end, lineColor, lineWidth);
        }

        private void DrawGUILine(Vector2 pointA, Vector2 pointB, Color color, float width)
        {
            if (lineTexture == null) return;

            Matrix4x4 matrixBackup = GUI.matrix;
            Color colorBackup = GUI.color;

            GUI.color = color;
            float angle = Mathf.Atan2(pointB.y - pointA.y, pointB.x - pointA.x) * Mathf.Rad2Deg;
            float length = Vector2.Distance(pointA, pointB);

            GUIUtility.RotateAroundPivot(angle, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - width / 2, length, width), lineTexture);

            GUI.matrix = matrixBackup;
            GUI.color = colorBackup;
        }

        private void OnDrawGizmos()
        {
            if (!show3DWeaponTrajectory || player == null || debugGizmoTimer <= 0) return;

            float alpha = Mathf.Clamp01(debugGizmoTimer);
            Color baseColor = isCharging ? new Color(1f, 0f, 1f) : new Color(1f, 0.5f, 0f); // 차징 시 보라색

            Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f * alpha);
            Gizmos.DrawLine(debugStartArc, debugEndArc);

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.3f * alpha);
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                Vector3 pointOnArc = Vector3.Lerp(debugStartArc, debugEndArc, t);
                Gizmos.DrawLine(debugChestCenter, pointOnArc);
            }

            Gizmos.color = new Color(1f, 0f, 0f, alpha);

            Vector3 trajectoryDir = (debugEndArc - debugStartArc).normalized;
            Vector3 playerForward = player.transform.forward;
            Vector3 arrowCross = Vector3.Cross(trajectoryDir, playerForward).normalized;

            float arrowHeadLength = 0.4f;
            float arrowHeadWidth = 0.2f;

            Vector3 arrowWing1 = debugEndArc - (trajectoryDir * arrowHeadLength) + (arrowCross * arrowHeadWidth);
            Vector3 arrowWing2 = debugEndArc - (trajectoryDir * arrowHeadLength) - (arrowCross * arrowHeadWidth);

            Gizmos.DrawLine(debugEndArc, arrowWing1);
            Gizmos.DrawLine(debugEndArc, arrowWing2);
            Gizmos.DrawLine(arrowWing1, arrowWing2);

            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                Gizmos.DrawLine(debugEndArc, Vector3.Lerp(arrowWing1, arrowWing2, t));
            }
        }
    }
}