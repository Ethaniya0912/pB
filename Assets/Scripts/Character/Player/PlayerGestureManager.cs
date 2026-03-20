using UnityEngine;
using UnityEngine.InputSystem;
using TDA.Core.Events; // 해시 시스템 연동을 위해 추가

namespace TDA.Character.Player
{
    /// <summary>
    /// [L1 Input Layer] 마우스 드래그 궤적을 추적하여 제스처를 판별하는 매니저입니다.
    /// [P0-03 Phase 2] 파지 상태(Stance) 추적, 차징(Charging) 타이머, 엇박자 절기(Fumble) 시스템이 완벽하게 적용되었습니다.
    /// 
    /// (사용자 요청에 따라 삭제/생략되었던 디버그 및 기즈모 로직, 상세 주석들을 100% 원상 복구 및 보존한 최종 마스터 버전입니다.)
    /// [버그 수정] 턴 애니메이션이 1번 레이어로 격상됨에 따라 턴 상태 판독 주소를 동기화하여 제스처 씹힘 현상을 완벽하게 해결했습니다.
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
        [Tooltip("제스처로 인정받기 위한 최소 마우스 드래그 픽셀 거리입니다.")]
        [SerializeField] private float minimumDragDistance = 50f;

        [Tooltip("파지 상태를 자동으로 강제 해제하기 전까지 기다리는 대기 시간. 현재는 강제 해제용으로 쓰지 않으나 특수 기믹(무게 디버프 등)을 위해 유지됩니다.")]
        [SerializeField] private float maxStanceHoldTime = 3.0f;
        private float currentStanceTimer = 0f;

        [Header("Input Queueing System (선입력)")]
        [Tooltip("콤보를 이어가기 위해 입력 선입력(큐잉)을 허용할지 여부입니다.")]
        public bool enableInputQueueing = true;
        [Tooltip("입력을 기억해둘 최대 허용 시간(초)입니다.")]
        [SerializeField] private float inputQueueWindow = 1.0f;

        private int queuedActionID = 0;
        private float queueExpirationTime = 0f;

        [Header("Debug Visuals (GUI & Gizmos)")]
        [Tooltip("화면에 마우스 드래그 궤적을 2D 라인으로 그립니다.")]
        public bool show2DDragLine = true;
        [Tooltip("씬 뷰(Scene)에 캐릭터가 베어낼 3D 궤적과 화살표를 시각화합니다.")]
        public bool show3DWeaponTrajectory = true;

        private Vector2 dragStartPosition;
        private Vector2 dragCurrentPosition;
        private bool isDragging = false;

        // 3D 렌더링용 연산 캐싱
        private Vector3 debugStartArc;
        private Vector3 debugEndArc;
        private Vector3 debugChestCenter;
        private float debugGizmoTimer = 0f;

        public bool IsDragging => isDragging;

        private Texture2D lineTexture;

        private void Awake()
        {
            player = GetComponent<PlayerManager>();

            // 2D 렌더링용 1x1 픽셀 텍스처 초기화 (메모리 릭 방지용 1회 생성)
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

        // =========================================================================================
        // 🚨 [파지 상태 판별 도우미]
        // 애니메이터 1번 레이어(Action)가 현재 파지 대기(Stance_Hold) 노드에 머물고 있는지 확인합니다.
        // =========================================================================================
        private bool IsHoldingStance()
        {
            if (player == null || player.animator == null) return false;
            AnimatorStateInfo actionState = player.animator.GetCurrentAnimatorStateInfo(1);
            return actionState.IsName("Stance_Hold_Left") || actionState.IsName("Stance_Hold_Right");
        }

        /// <summary>
        /// 파지(Stance) 유지 중일 때, 이동 입력이나 강한 회전이 들어오면 자세를 풀고 기본으로 복귀하는 로직입니다.
        /// </summary>
        private void HandleStanceState()
        {
            if (player.animator == null) return;

            // 이미 Empty 상태로 넘어가고 있는(트랜지션 중) 찰나의 시간 동안 
            // CrossFade가 매 프레임 중복 호출되는 것을 차단합니다.
            if (player.animator.IsInTransition(1)) return;

            if (IsHoldingStance())
            {
                currentStanceTimer += Time.deltaTime;

                // 물리적 이동 입력 감지
                bool isMoving = player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f;

                // 마우스를 크게 돌려 제자리 턴(Turn)이 발생하려 할 때 락 감지
                bool isTurning = Mathf.Abs(player.animator.GetFloat(AnimatorParameterHash.turnAngle)) > 30f;

                // 이동하거나 크게 고개를 돌리면 전투 자세(Hold)를 내리고 자연스러운 이동 상태로 풉니다.
                if (isMoving || isTurning)
                {
                    Debug.Log($"<color=gray>[Stance 종료]</color> {(isMoving ? "이동 입력" : "회전 감지")}. 무기를 내립니다.");

                    if (player.playerCombatManager != null)
                    {
                        player.playerCombatManager.canComboWithMainHandWeapon = false;
                        player.playerCombatManager.canComboWithOffHandWeapon = false;
                    }

                    // 상체(1번) 레이어를 강제로 비워서 Base Layer(하체)의 턴/이동이 온전히 적용되게 만듭니다.
                    player.animator.CrossFade("Empty", 0.25f, 1);

                    player.isPerformingAction = false;
                    player.canMove = true;
                    player.canRotate = true;

                    currentStanceTimer = 0f;
                    queuedActionID = 0;

                    // 완전히 자세가 풀렸으므로 무기를 오른쪽으로 쥔 기본 상태로 복구
                    currentStance = global::WeaponStance.RightHold;
                }

                // [보존된 로직] maxStanceHoldTime 변수는 인스펙터에 남겨두었으나, 
                // 장기전 롱테이크 전투를 위해 자동 해제 트리거 조건에서는 제거하여 
                // "이동하지 않는 한 영원히 무기를 치켜들고 대기" 하도록 설정했습니다.
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

                // 큐잉 발동 조건: 동작이 완전히 끝났거나, 콤보 타이밍이거나, 파지(Hold) 상태로 멈춰있을 때
                if (!player.isPerformingAction || canCombo || IsHoldingStance())
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

            // 상호작용 물체를 들고 있거나 방어 중일 때는 제스처 입력 무시
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

            // ---------------------------------------------------------
            // 1. 마우스 클릭 (제스처 시작 및 변수 초기화)
            // ---------------------------------------------------------
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                // 게임 화면에 커서가 잠긴 경우와 풀린 경우를 구분하여 드래그 시작점을 잡습니다.
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

            // ---------------------------------------------------------
            // 2. 마우스 유지 중 (궤적 업데이트 및 강공격 타이머 연산)
            // ---------------------------------------------------------
            if (isDragging && Mouse.current.leftButton.isPressed)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                dragCurrentPosition += mouseDelta;

                float holdDuration = Time.time - clickStartTime;

                // 지정된 시간(chargeThreshold) 이상 누르면 강공격(차징) 진입
                if (!isCharging && holdDuration >= chargeThreshold)
                {
                    isCharging = true;
                    // 해시 적용: 문자열 하드코딩 에러 원천 차단
                    if (player.animator != null) player.animator.SetBool(AnimatorParameterHash.isCharging, true);

                    if (player.characterEventManager != null)
                    {
                        // 오디오 매니저가 이 이벤트를 듣고 숨소리나 차징 사운드를 재생합니다.
                        player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Charge_Started);
                        Debug.Log("<color=magenta>[Charging]</color> 강공격 기 모으기 시작!");
                    }
                }

                // 차징 중이면 애니메이터 블렌드트리용 변수(ChargeRatio) 업데이트
                if (isCharging && player.animator != null)
                {
                    float chargeRatio = Mathf.Clamp01((holdDuration - chargeThreshold) / (maxChargeTime - chargeThreshold));
                    player.animator.SetFloat(AnimatorParameterHash.ChargeRatio, chargeRatio);
                }
            }

            // ---------------------------------------------------------
            // 3. 마우스 뗌 (판별 및 타격 액션 트리거!)
            // ---------------------------------------------------------
            if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;

                // 애니메이터의 차징 상태를 즉각 해제합니다.
                if (player.animator != null)
                {
                    player.animator.SetBool(AnimatorParameterHash.isCharging, false);
                    player.animator.SetFloat(AnimatorParameterHash.ChargeRatio, 0f);
                }

                // 🚨 [치명적 버그 수정] AnalyzeGesture를 호출하기 위해 isCharging 값을 전달하되,
                // 클래스 내부 멤버 변수(isCharging)도 즉시 false로 초기화해주어야 
                // 다음 궤적 판정에 영향을 주지 않습니다.
                bool wasChargedAttack = isCharging;
                isCharging = false;

                // 구해진 궤적 데이터와 강공격 여부를 분석 함수로 토스합니다.
                AnalyzeGesture(dragStartPosition, dragCurrentPosition, wasChargedAttack);
            }
        }

        /// <summary>
        /// [Phase 2 핵심] 마우스 궤적을 수학적으로 분석하고, 파지(Stance) 상태와 교차 검증하여 
        /// 정상적인 콤보인지, 조작 실수(절기)인지 판별한 후 액션을 실행합니다.
        /// </summary>
        private void AnalyzeGesture(Vector2 startPos, Vector2 endPos, bool isChargeAttack)
        {
            Vector2 dragVector = endPos - startPos;
            int targetActionID = clickActionID;
            string determinedDirection = isChargeAttack ? "차지 평타" : "기본 평타(짧은 클릭)";

            // 최소 거리 이상을 드래그 했을 때만 궤적으로 인정합니다.
            if (dragVector.magnitude >= minimumDragDistance)
            {
                float dragAngle = Mathf.Atan2(dragVector.y, dragVector.x) * Mathf.Rad2Deg;
                bool isDraggingRightToLeft = (dragVector.x < 0); // 마우스를 좌측으로 그음
                bool isDraggingLeftToRight = (dragVector.x > 0); // 마우스를 우측으로 그음

                // 현재는 좌/우 공격만 존재하므로 45~135도 이외의 영역을 수평 베기로 간주합니다.
                if (Mathf.Abs(dragAngle) < 45f || Mathf.Abs(dragAngle) > 135f)
                {
                    // =========================================================================
                    // 🚨 [엇박자 차단 가드] 파지 상태와 궤적의 엄격한 물리적 교차 검증
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
                    // 상하 드래그는 향후 기획에 맞추어 추가 개발
                    targetActionID = clickActionID;
                    determinedDirection = dragVector.y > 0 ? "상단 (미구현)" : "하단 (미구현)";
                }

                Debug.Log($"<color=cyan>[Gesture 분석 완료]</color> 판정: <color=yellow><b>{determinedDirection}</b></color> | 궤적: {dragAngle:F1}도 | 거리: {dragVector.magnitude:F1}px");
            }

            bool canCombo = player.playerCombatManager != null &&
                           (player.playerCombatManager.canComboWithMainHandWeapon || player.playerCombatManager.canComboWithOffHandWeapon);

            bool isTurning = false;
            if (player.animator != null && player.animator.layerCount > 1)
            {
                // 🚨 [버그 완벽 수정] 턴 애니메이션은 이제 0번이 아니라 1번(Action) 레이어에 있습니다!
                // Action Layer(1)의 상태를 추적해야 턴 중임을 정상적으로 깨닫고 턴을 캔슬하며 궤적 공격을 실행합니다.
                AnimatorStateInfo actionState = player.animator.GetCurrentAnimatorStateInfo(1);
                AnimatorStateInfo nextActionState = player.animator.GetNextAnimatorStateInfo(1);

                if (actionState.IsName("Turn_Left_90") || actionState.IsName("Turn_Right_90") ||
                    actionState.IsName("Turn_Left_180") || actionState.IsName("Turn_Right_180") ||
                    nextActionState.IsName("Turn_Left_90") || nextActionState.IsName("Turn_Right_90") ||
                    nextActionState.IsName("Turn_Left_180") || nextActionState.IsName("Turn_Right_180"))
                {
                    isTurning = true;
                }
            }

            // 조건 1: 아무것도 안 하고 있을 때 (isPerformingAction = false)
            // 조건 2: 콤보 타이밍일 때 (canCombo = true)
            // 조건 3: 차징 공격 해방 시 (isChargeAttack = true) - 무조건 타격으로 넘어감
            // 조건 4: 제자리 턴 중일 때 (isTurning = true) - 턴을 강제 캔슬하고 공격
            // 조건 5: 파지(Hold) 상태로 멈춰 있을 때 - 대기 중 즉시 격발
            if (!player.isPerformingAction || canCombo || isChargeAttack || isTurning || IsHoldingStance())
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
        /// [P0-03 핵심] 엇박자 입력 시 발생하는 치명적인 조작 실수 페널티 집행 로직
        /// </summary>
        private void ExecuteFumblePenalty(string debugMsg)
        {
            Debug.Log($"<color=red>[Fumble/절기]</color> 조작 실패! 사유: {debugMsg}");

            // 1. 자원 페널티: 스태미나 대폭 차감
            if (player.playerNetworkManager != null)
            {
                player.playerNetworkManager.currentStamina.Value -= fumbleStaminaPenalty;
            }

            // 2. 취약 상태 전환: 콤보 연결 강제 해제
            if (player.playerCombatManager != null)
            {
                player.playerCombatManager.canComboWithMainHandWeapon = false;
                player.playerCombatManager.canComboWithOffHandWeapon = false;
            }

            // 3. 강제 애니메이션 재생 (ActionID: 99 - 근육이 꼬여 휘청거리는 패널티 모션)
            player.playerAnimationManager.PlayTargetActionFunnel(fumbleActionID, true, true, false, false);

            // 4. 극한의 시청각 피드백 방송 (카메라, 사운드, 이펙트)
            if (player.characterEventManager != null)
            {
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlayVoice_Stagger_Pain);
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.CameraShake_Heavy_Fumble);
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.ScreenFX_Hit_Vignette);
            }

            // 5. 후속 입력 방지를 위한 큐잉 및 상태 초기화
            queuedActionID = 0;
            isDragging = false;
            isCharging = false;
        }

        // =========================================================================================
        // [디버깅 & 시각화] 2D 라인(GUI) 및 3D 화살표(Gizmo) 렌더링 로직 (보존됨)
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
            Color baseColor = isCharging ? new Color(1f, 0f, 1f) : new Color(1f, 0.5f, 0f);

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