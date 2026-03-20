using UnityEngine;
using UnityEditor;
using System.Reflection;
using TDA.Character.Player;
using TDA.World;
using TDA.Cameras;
using TDA.Core.Events;

namespace TDA.Character.Player.Editor
{
    /// <summary>
    /// [PlayerCamera 전용 커스텀 에디터]
    /// 런타임 중 프레임 단위의 애니메이션 진행도, 충돌체, 앵글을 모니터링하는 디버거 기능과,
    /// 1계층 스탠스 SO의 수치(특히 수직 조작감 등 신규 파라미터)를 실시간으로 튜닝하고 영구 저장하는 라이브 에디터를 제공합니다.
    /// </summary>
    [CustomEditor(typeof(PlayerCamera))]
    public class PlayerCameraEditor : UnityEditor.Editor
    {
        private bool showDebugPanel = true;
        private bool showLiveEditor = true;

        // =====================================================================
        // 라이브 에디팅 임시 변수 캐싱 (SO 덮어쓰기 및 동기화용)
        // =====================================================================
        private CameraStancePresetSO lastLiveEditSO;

        private float editFov;
        private Vector3 editBaseOffset;
        private float editZTilt;
        private float editBaseYawOffset;

        // 수직(상하) 조작 방식 변수 캐싱
        private CameraVerticalBehavior editBehaviorType;
        private float editElevationSpeed;
        private float editMaxElevationHeight;
        private float editMinElevationHeight;
        private float editFixedPitchAngle;
        private float editPitchForMaxHeight;
        private float editMaxDynamicHeight;
        private float editHeightSmoothTime;

        // 다이내믹 프레이밍 변수 캐싱 (+ 최신 상속 옵션 추가)
        private bool editUseDynamicFraming;
        private bool editInheritDynamicFraming;
        private float editLeftStrafe, editRightStrafe;
        private bool editHoldLeft, editHoldRight;
        private float editLeftDelay, editRightDelay, editCenterDelay;
        private float editLeftStrafeYaw, editRightStrafeYaw;
        private float editDynamicYawWeight;
        private float editForwardBackwardReturnTime;

        private bool editEnableHandheld;
        private float editSwayAmount, editSwaySpeed, editBobbingAmount;

        private bool editEnableShake;
        private float editShakeIntensity, editShakeDelay, editShakeDuration;

        // 🚨 [FOV 실시간 보간 렌더링용] 
        // 마우스를 움직이지 않아도 에디터 인스펙터가 스스로 매 프레임 갱신되도록 강제합니다.
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayerCamera pc = (PlayerCamera)target;

            if (!Application.isPlaying || pc.player == null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("▶ 게임을 실행(Play)하면 이곳에 상세한 카메라 애니메이션 프레임 모니터 및 SO 라이브 에디터가 표시됩니다.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(15);

            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = new Color(0.4f, 0.8f, 1f) } };
            GUIStyle subHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.9f, 0.7f, 0.4f) } };

            // =================================================================================
            // 📊 1. 실시간 상태 모니터 (Live Debug - 애니메이션 Frame & 앵글 추적)
            // =================================================================================
            showDebugPanel = EditorGUILayout.Foldout(showDebugPanel, "📊 실시간 상태 모니터 (Live Debug)", true, new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold });

            if (showDebugPanel)
            {
                EditorGUILayout.BeginVertical("box");

                // [월드 및 락온 상태]
                EditorGUILayout.LabelField("🌍 World & Player State", subHeaderStyle);
                DrawField("World State", WorldGameStateManager.Instance != null ? WorldGameStateManager.Instance.currentState.ToString() : "N/A");

                bool isLockedOn = pc.player.playerNetworkManager != null && pc.player.playerNetworkManager.isLockedOn.Value;
                string targetName = isLockedOn && pc.player.playerCombatManager.currentTarget != null ? pc.player.playerCombatManager.currentTarget.name : "None";
                DrawField("Lock-On Target", $"[{(isLockedOn ? "<color=lime>ON</color>" : "OFF")}] {targetName}", true);

                EditorGUILayout.Space(5);

                // [애니메이션 프레임 및 액션 상태]
                EditorGUILayout.LabelField("🏃 Animation & Action State", subHeaderStyle);
                int actionID = pc.player.animator != null ? pc.player.animator.GetInteger(AnimatorParameterHash.ActionState) : 0;
                DrawField("Action ID", actionID.ToString());
                DrawField("Is Performing Action", pc.player.isPerformingAction ? "<color=red>True</color>" : "False", true);

                string baseAnim = "N/A";
                string actionAnim = "N/A";
                string animProgress = "0.00%";

                if (pc.player.animator != null)
                {
                    if (pc.player.animator.layerCount > 0)
                    {
                        var bState = pc.player.animator.GetCurrentAnimatorClipInfo(0);
                        if (bState.Length > 0 && bState[0].clip != null) baseAnim = bState[0].clip.name;
                    }
                    if (pc.player.animator.layerCount > 1)
                    {
                        var aState = pc.player.animator.GetCurrentAnimatorClipInfo(1);
                        if (aState.Length > 0 && aState[0].clip != null) actionAnim = aState[0].clip.name;

                        // 액션 레이어(1)의 애니메이션 재생 프레임/진행도 실시간 트래킹 (멀미 방어 커브용)
                        var stateInfo = pc.player.animator.GetCurrentAnimatorStateInfo(1);
                        float normTime = stateInfo.normalizedTime % 1f;
                        animProgress = $"{(normTime * 100f):F1}% (Norm: {normTime:F3})";
                    }
                }

                DrawField("Base Layer Clip", baseAnim);
                DrawField("Action Layer Clip", actionAnim);
                DrawField("Action Progress (Frame)", $"<color=cyan>{animProgress}</color>", true);

                EditorGUILayout.Space(5);

                // [카메라 물리 수치 및 SO]
                EditorGUILayout.LabelField("🎥 Camera Transform & SO Data", subHeaderStyle);

                DrawField("최근 호출 원인 (Caller)", $"<color=lime>{pc.lastCallerReason}</color>", true);

                CameraSequencePresetSO currentSeqSO = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentSequenceSO : null;
                CameraStancePresetSO currentStanceSO = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentStanceSO : null;

                string currentSeqName = currentSeqSO != null ? currentSeqSO.name : "None (재생중 아님)";
                string currentStanceName = currentStanceSO != null ? currentStanceSO.name : "None (대기 스탠스 없음)";

                DrawField("Active Sequence SO", $"<color=cyan>{currentSeqName}</color>", true);
                DrawField("Active Stance SO", $"<color=cyan>{currentStanceName}</color>", true);

                // 🚨 [v3.0 고도화] 관제탑에서 현재 오버라이드 중인 액션 댐핑 수치 실시간 모니터링
                if (WorldCameraManager.Instance != null)
                {
                    float curPosDamp = WorldCameraManager.Instance.CurrentPositionDamping;
                    float curRotDamp = WorldCameraManager.Instance.CurrentRotationDamping;

                    // 기본값(0.1)보다 높으면 오버라이드 작동 중이므로 주황색으로 강조 표시
                    string dampColor = (curPosDamp > 0.1f || curRotDamp > 0.1f) ? "orange" : "white";
                    DrawField("Action Damping (Pos/Rot)", $"<color={dampColor}>{curPosDamp:F2} / {curRotDamp:F2}</color>", true);
                }

                EditorGUILayout.Space();
                DrawField("Z축 원본값 (SO Data)", $"{pc.debugRawZ:F2}");

                if (pc.bypassCollisionForDebug)
                {
                    DrawField("🚨 충돌/압착 원인", $"<color=lime>[Bypass 활성화] 충돌 완전 무시됨</color>", true);
                    DrawField("충돌 보정된 목표 Z", $"<color=lime>{pc.debugTargetZ:F2}</color>", true);
                }
                else if (pc.lastCollisionObjectName != "None")
                {
                    DrawField("🚨 충돌/압착 원인", $"<color=orange>{pc.lastCollisionObjectName}</color>", true);
                    DrawField("충돌 보정된 목표 Z", $"<color=orange>{pc.debugTargetZ:F2}</color>", true);
                }
                else
                {
                    DrawField("충돌 보정된 목표 Z", $"{pc.debugTargetZ:F2}");
                }

                DrawField("실제 렌더링 Z (Child Z)", $"<color=lime>{pc.debugActualZ:F2}</color>", true);

                // 실시간 렌더링 중인 FOV 추출
                float currentRealFov = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentFOV : (pc.cameraObject != null ? pc.cameraObject.fieldOfView : 0f);
                DrawField("Current FOV (Realtime)", $"<color=yellow>{currentRealFov:F2}</color>", true);

                float yaw = GetPrivateField<float>(pc, "leftAndRightLookAngle");
                float pitch = GetPrivateField<float>(pc, "upAndDownLookAngle");
                DrawField("Camera Angle (Pitch/Yaw)", $"{pitch:F1} / {yaw:F1}");

                EditorGUILayout.EndVertical();
            }

            // =================================================================================
            // 🛠️ 2. SO 라이브 에디터 (데이터 유실 방지 + 기획 튜닝용)
            // =================================================================================
            EditorGUILayout.Space(10);
            showLiveEditor = EditorGUILayout.Foldout(showLiveEditor, "🛠️ 카메라 라이브 에디터 (SO Data-Driven)", true, new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold });

            if (showLiveEditor)
            {
                WorldCameraManager wcm = WorldCameraManager.Instance;

                if (wcm != null && wcm.currentStanceSO != null)
                {
                    // 활성화된 스탠스 SO가 바뀌면 임시 변수들을 동기화
                    if (wcm.currentStanceSO != lastLiveEditSO)
                    {
                        lastLiveEditSO = wcm.currentStanceSO;
                        SyncFromSO(lastLiveEditSO);
                    }

                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField($"현재 실시간 렌더링 중인 SO: {lastLiveEditSO.name}", EditorStyles.boldLabel);
                    EditorGUILayout.Space(5);

                    // [렌즈 및 구도]
                    EditorGUILayout.LabelField("📷 렌즈 및 구도 (Lens & Framing)", EditorStyles.miniBoldLabel);

                    // 🚨 [FOV 실시간 보간 렌더링] 슬라이더 위쪽에 실제 변동 중인 렌즈의 FOV를 직관적으로 표시
                    float realFov = wcm.currentFOV;
                    EditorGUILayout.LabelField($"현재 실시간 보간 FOV: {realFov:F2}", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.yellow } });

                    editFov = EditorGUILayout.Slider("Target FOV (SO Data)", editFov, 10f, 120f);
                    editBaseOffset = EditorGUILayout.Vector3Field("Base Offset", editBaseOffset);
                    editZTilt = EditorGUILayout.Slider("Z Tilt", editZTilt, -45f, 45f);
                    editBaseYawOffset = EditorGUILayout.Slider("Base Yaw Offset (기본 좌우 각도)", editBaseYawOffset, -45f, 45f);
                    EditorGUILayout.Space(5);

                    // =====================================================================
                    // 수직 시점 조작 패널 (CameraVerticalBehavior)
                    // =====================================================================
                    EditorGUILayout.LabelField("↕️ 수직(상하) 시점 조작 방식 (Vertical Behavior)", EditorStyles.miniBoldLabel);

                    editBehaviorType = (CameraVerticalBehavior)EditorGUILayout.EnumPopup("Behavior Type", editBehaviorType);

                    // Enum 타입에 따라 필요한 파라미터만 조건부 렌더링 (인스펙터 공간 최적화)
                    if (editBehaviorType == CameraVerticalBehavior.ElevationOnly)
                    {
                        EditorGUI.indentLevel++;
                        editElevationSpeed = EditorGUILayout.FloatField(new GUIContent("Elevation Speed", "마우스 수직 조작 시 Y축 이동 속도"), editElevationSpeed);
                        editMaxElevationHeight = EditorGUILayout.FloatField(new GUIContent("Max Elevation Height", "최대 도달 Y축 오프셋"), editMaxElevationHeight);
                        editMinElevationHeight = EditorGUILayout.FloatField(new GUIContent("Min Elevation Height", "최소 도달 Y축 오프셋"), editMinElevationHeight);
                        editFixedPitchAngle = EditorGUILayout.FloatField(new GUIContent("Fixed Pitch Angle", "강제 고정될 렌즈 각도"), editFixedPitchAngle);
                        EditorGUI.indentLevel--;
                    }
                    else if (editBehaviorType == CameraVerticalBehavior.DynamicOverShoulder)
                    {
                        EditorGUI.indentLevel++;
                        editPitchForMaxHeight = EditorGUILayout.FloatField(new GUIContent("Pitch For Max Height", "이 각도(바닥)를 볼 때 최대 높이 도달"), editPitchForMaxHeight);
                        editMaxDynamicHeight = EditorGUILayout.FloatField(new GUIContent("Max Dynamic Height", "추가로 솟아오를 최대 Y축 오프셋"), editMaxDynamicHeight);
                        editHeightSmoothTime = EditorGUILayout.FloatField(new GUIContent("Height Smooth Time", "높이 변화의 완충 댐핑 시간"), editHeightSmoothTime);
                        EditorGUI.indentLevel--;
                    }
                    else if (editBehaviorType == CameraVerticalBehavior.ClassicPivot)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.HelpBox("가슴 중심점을 기준으로 팽이처럼 회전하는 클래식 뷰입니다. (추가 설정 없음)", MessageType.None);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(5);

                    // [다이내믹 프레이밍]
                    EditorGUILayout.LabelField("🏃 다이내믹 프레이밍 (Dynamic Framing)", EditorStyles.miniBoldLabel);

                    // 🚨 [최신 옵션 추가] 동적 프레이밍 활성화 및 상속 여부 UI 노출
                    editUseDynamicFraming = EditorGUILayout.Toggle("Use Dynamic Framing", editUseDynamicFraming);
                    editInheritDynamicFraming = EditorGUILayout.Toggle(new GUIContent("Inherit Framing (상속/동결)", "직전 스탠스의 프레이밍(A/D 꺾임)을 0으로 돌리지 않고 동결하여 유지합니다."), editInheritDynamicFraming);

                    EditorGUILayout.Space(3);

                    EditorGUILayout.LabelField("Position Offset (X축)", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    editLeftStrafe = EditorGUILayout.FloatField("Left Max Offset", editLeftStrafe);
                    editRightStrafe = EditorGUILayout.FloatField("Right Max Offset", editRightStrafe);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    editHoldLeft = EditorGUILayout.Toggle("Hold Left", editHoldLeft);
                    editHoldRight = EditorGUILayout.Toggle("Hold Right", editHoldRight);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Rotation Offset (Yaw 각도)", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    editLeftStrafeYaw = EditorGUILayout.FloatField("Left Yaw Angle", editLeftStrafeYaw);
                    editRightStrafeYaw = EditorGUILayout.FloatField("Right Yaw Angle", editRightStrafeYaw);
                    EditorGUILayout.EndHorizontal();

                    editDynamicYawWeight = EditorGUILayout.Slider("Yaw Weight (락온 개입률)", editDynamicYawWeight, 0f, 1f);

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Delay & Speed", EditorStyles.miniLabel);
                    editLeftDelay = EditorGUILayout.FloatField("Left Speed (Delay)", editLeftDelay);
                    editRightDelay = EditorGUILayout.FloatField("Right Speed (Delay)", editRightDelay);
                    editCenterDelay = EditorGUILayout.FloatField("Center Return Speed", editCenterDelay);

                    editForwardBackwardReturnTime = EditorGUILayout.FloatField("Return Time (전후진 롱테이크 복귀)", editForwardBackwardReturnTime);
                    EditorGUILayout.Space(5);

                    // [핸드헬드]
                    EditorGUILayout.LabelField("📳 핸드헬드 및 보빙 (Handheld)", EditorStyles.miniBoldLabel);
                    editEnableHandheld = EditorGUILayout.Toggle("Enable Handheld", editEnableHandheld);
                    if (editEnableHandheld)
                    {
                        editSwayAmount = EditorGUILayout.FloatField("Sway Amount", editSwayAmount);
                        editSwaySpeed = EditorGUILayout.FloatField("Sway Speed", editSwaySpeed);
                        editBobbingAmount = EditorGUILayout.FloatField("Bobbing Amount", editBobbingAmount);
                    }
                    EditorGUILayout.Space(5);

                    // [쉐이크]
                    EditorGUILayout.LabelField("💥 카메라 쉐이크 (Impact Shake)", EditorStyles.miniBoldLabel);
                    editEnableShake = EditorGUILayout.Toggle("Enable Shake", editEnableShake);
                    if (editEnableShake)
                    {
                        editShakeIntensity = EditorGUILayout.FloatField("Shake Intensity", editShakeIntensity);
                        editShakeDelay = EditorGUILayout.FloatField("Shake Delay", editShakeDelay);
                        editShakeDuration = EditorGUILayout.FloatField("Max Duration", editShakeDuration);

                        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                        if (GUILayout.Button("💥 설정된 값으로 쉐이크 테스트!", GUILayout.Height(25)))
                        {
                            wcm.ApplyCameraShake(editShakeIntensity, editShakeDuration);
                            Debug.Log($"<color=red>[Test]</color> 강도: {editShakeIntensity}, 시간: {editShakeDuration} 쉐이크 발동!");
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    EditorGUILayout.Space(10);

                    // 🚨 [가장 중요한 데이터 영구 저장 트리거 버튼]
                    GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                    if (GUILayout.Button("💾 변경사항 SO에 즉시 덮어쓰기 및 저장 (Apply to SO)", GUILayout.Height(35)))
                    {
                        // 1. 실행 취소(Ctrl+Z) 스택 기록 및 편집 권한 획득
                        Undo.RecordObject(lastLiveEditSO, "Live Edit Camera Stance");

                        // 2. 값 덮어쓰기
                        ApplyToSO(lastLiveEditSO);

                        // 3. WorldCameraManager를 통해 런타임 렌더링 즉시 강제 갱신
                        var m_ApplyStanceMethod = typeof(WorldCameraManager).GetMethod("ApplyStanceInstantly", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (m_ApplyStanceMethod != null)
                        {
                            m_ApplyStanceMethod.Invoke(wcm, new object[] { lastLiveEditSO });
                        }

                        // 4. Dirty 마킹 및 저장 (유실 방어)
                        EditorUtility.SetDirty(lastLiveEditSO);
                        AssetDatabase.SaveAssets();

                        Debug.Log($"<color=green>[Save Success]</color> <b>{lastLiveEditSO.name}</b> 에셋에 변경사항이 영구 저장되었습니다! Play 모드를 꺼도 절대 날아가지 않습니다.");
                    }
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.HelpBox("현재 활성화된 CameraStancePresetSO가 없습니다. WorldCameraManager가 SO를 렌더링하고 있는지 확인하세요.", MessageType.Warning);
                }
            }

            Repaint();
        }

        // =================================================================================
        // SO 동기화 유틸리티 함수들
        // =================================================================================
        private void SyncFromSO(CameraStancePresetSO so)
        {
            if (so == null) return;
            editFov = so.fov;
            editBaseOffset = so.baseOffset;
            editZTilt = so.zTilt;
            editBaseYawOffset = so.baseYawOffset;

            editBehaviorType = so.verticalBehavior.behaviorType;
            editElevationSpeed = so.verticalBehavior.elevationSpeed;
            editMaxElevationHeight = so.verticalBehavior.maxElevationHeight;
            editMinElevationHeight = so.verticalBehavior.minElevationHeight;
            editFixedPitchAngle = so.verticalBehavior.fixedPitchAngle;
            editPitchForMaxHeight = so.verticalBehavior.pitchForMaxHeight;
            editMaxDynamicHeight = so.verticalBehavior.maxDynamicHeight;
            editHeightSmoothTime = so.verticalBehavior.heightSmoothTime;

            // 🚨 [최신 데이터 동기화]
            editUseDynamicFraming = so.useDynamicFraming;
            editInheritDynamicFraming = so.inheritDynamicFraming;

            editLeftStrafe = so.dynamicFraming.leftStrafeMaxOffset;
            editRightStrafe = so.dynamicFraming.rightStrafeMaxOffset;
            editHoldLeft = so.dynamicFraming.holdLeftStrafe;
            editHoldRight = so.dynamicFraming.holdRightStrafe;
            editLeftDelay = so.dynamicFraming.leftFramingDelay;
            editRightDelay = so.dynamicFraming.rightFramingDelay;
            editCenterDelay = so.dynamicFraming.centerReturnDelay;

            editLeftStrafeYaw = so.dynamicFraming.leftStrafeYaw;
            editRightStrafeYaw = so.dynamicFraming.rightStrafeYaw;
            editDynamicYawWeight = so.dynamicFraming.dynamicYawWeight;
            editForwardBackwardReturnTime = so.dynamicFraming.forwardBackwardReturnTime;

            editEnableHandheld = so.handheldEffect.enableHandheldEffect;
            editSwayAmount = so.handheldEffect.swayAmount;
            editSwaySpeed = so.handheldEffect.swaySpeed;
            editBobbingAmount = so.handheldEffect.bobbingAmount;

            editEnableShake = so.impactShake.enableShake;
            editShakeIntensity = so.impactShake.shakeIntensity;
            editShakeDelay = so.impactShake.shakeDelay;
            editShakeDuration = so.impactShake.maxDuration;
        }

        private void ApplyToSO(CameraStancePresetSO so)
        {
            if (so == null) return;

            so.fov = editFov;
            so.baseOffset = editBaseOffset;
            so.zTilt = editZTilt;
            so.baseYawOffset = editBaseYawOffset;

            so.verticalBehavior.behaviorType = editBehaviorType;
            so.verticalBehavior.elevationSpeed = editElevationSpeed;
            so.verticalBehavior.maxElevationHeight = editMaxElevationHeight;
            so.verticalBehavior.minElevationHeight = editMinElevationHeight;
            so.verticalBehavior.fixedPitchAngle = editFixedPitchAngle;
            so.verticalBehavior.pitchForMaxHeight = editPitchForMaxHeight;
            so.verticalBehavior.maxDynamicHeight = editMaxDynamicHeight;
            so.verticalBehavior.heightSmoothTime = editHeightSmoothTime;

            // 🚨 [최신 데이터 적용]
            so.useDynamicFraming = editUseDynamicFraming;
            so.inheritDynamicFraming = editInheritDynamicFraming;

            so.dynamicFraming.leftStrafeMaxOffset = editLeftStrafe;
            so.dynamicFraming.rightStrafeMaxOffset = editRightStrafe;
            so.dynamicFraming.holdLeftStrafe = editHoldLeft;
            so.dynamicFraming.holdRightStrafe = editHoldRight;
            so.dynamicFraming.leftFramingDelay = editLeftDelay;
            so.dynamicFraming.rightFramingDelay = editRightDelay;
            so.dynamicFraming.centerReturnDelay = editCenterDelay;

            so.dynamicFraming.leftStrafeYaw = editLeftStrafeYaw;
            so.dynamicFraming.rightStrafeYaw = editRightStrafeYaw;
            so.dynamicFraming.dynamicYawWeight = editDynamicYawWeight;
            so.dynamicFraming.forwardBackwardReturnTime = editForwardBackwardReturnTime;

            so.handheldEffect.enableHandheldEffect = editEnableHandheld;
            so.handheldEffect.swayAmount = editSwayAmount;
            so.handheldEffect.swaySpeed = editSwaySpeed;
            so.handheldEffect.bobbingAmount = editBobbingAmount;

            so.impactShake.enableShake = editEnableShake;
            so.impactShake.shakeIntensity = editShakeIntensity;
            so.impactShake.shakeDelay = editShakeDelay;
            so.impactShake.maxDuration = editShakeDuration;
        }

        private void DrawField(string label, string value, bool richText = false)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(180));
            GUIStyle style = new GUIStyle(EditorStyles.label) { richText = richText, fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(value, style);
            EditorGUILayout.EndHorizontal();
        }

        private T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return (T)field.GetValue(target);
            return default;
        }
    }
}