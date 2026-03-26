using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEditor;
using System.Reflection;
using TDA.Character.Player;
using TDA.World;
using TDA.Cameras;
using TDA.Core.Events;

namespace TDA.Character.Player.Editor
{
    /// <summary>
    /// [PlayerCamera 전용 커스텀 에디터 - Pro Action Simulator V2.1]
    ///
    /// V2 → V2.1 개선 내용:
    /// ① Tracking Window 버그 수정: normTime이 시퀀스 내부 타이머 기준이어서 애니메이션과 단절된 문제 해결.
    ///    → BlendToStanceRoutine에서 애니메이터 normalizedTime을 직접 읽어 trackingWeight 계산하도록 패치 요청 주석 추가,
    ///       에디터에서 두 타임라인(Seq vs Anim)을 나란히 시각화하여 어긋남을 즉시 인지할 수 있게 함.
    ///
    /// ② 📽️ Pro Action Simulator V2 — 애니메이션 타임라인 + 카메라 연동 시각화
    ///    · 애니메이션 진행도(normalizedTime)를 가로 타임라인으로 실시간 렌더링
    ///    · eventPoints(HitBox, Combo, Lock 등) 각 이벤트를 컬러 마커로 표시
    ///    · warpStart~warpEnd 워프 구간을 반투명 오버레이로 표시
    ///    · Sequence의 trackingStart~trackingEnd를 같은 타임라인 위에 오버레이하여
    ///      "애니메이션 몇 % 시점에 트래킹이 켜지는지" 즉시 파악 가능
    ///    · CurrentTrackingWeight 실시간 게이지
    ///
    /// ③ 🎥 카메라 기즈모 Preview Panel
    ///    · Scene View에 카메라 피벗, 오프셋 위치, Frustum을 기즈모로 렌더링
    ///    · Tracking ON 구간(파랑) vs OFF 구간(빨강)으로 Follow 기즈모를 색상 구분
    ///    · 타겟 위치에 lockOn 기즈모 원 표시
    /// </summary>
    [CustomEditor(typeof(PlayerCamera))]
    public class PlayerCameraEditor : UnityEditor.Editor
    {
        // =====================================================================
        // 패널 토글
        // =====================================================================
        private bool showDebugPanel = true;
        private bool showSimulator = true;
        private bool showCameraGizmoPanel = true;
        private bool showLiveEditor = true;
        private bool showHistoryPanel = true;
        private bool showVisualDebugSection = true;

        // =====================================================================
        // 히스토리
        // =====================================================================
        private Vector2 historyScrollPos;
        private Vector2 internalHistoryScrollPos;
        private List<string> internalFunctionHistory = new List<string>();
        private string lastRecordedCallerReason = "";

        // =====================================================================
        // SO 라이브 에디터 임시 변수
        // =====================================================================
        private CameraStancePresetSO lastLiveEditSO;
        private float editFov;
        private Vector3 editBaseOffset;
        private float editZTilt;
        private float editBaseYawOffset;

        // [컴파일 에러 수정] 명시적 네임스페이스 지정
        private TDA.Cameras.CameraVerticalBehavior editBehaviorType;

        private float editElevationSpeed, editMaxElevationHeight, editMinElevationHeight;
        private float editFixedPitchAngle, editPitchForMaxHeight, editMaxDynamicHeight, editHeightSmoothTime;
        private bool editUseDynamicFraming, editInheritDynamicFraming;
        private float editLeftStrafe, editRightStrafe;
        private bool editHoldLeft, editHoldRight;
        private float editLeftDelay, editRightDelay, editCenterDelay;
        private float editLeftStrafeYaw, editRightStrafeYaw, editDynamicYawWeight, editForwardBackwardReturnTime;
        private bool editEnableHandheld;
        private float editSwayAmount, editSwaySpeed, editBobbingAmount;
        private bool editEnableShake;
        private float editShakeIntensity, editShakeDelay, editShakeDuration;

        // =====================================================================
        // 시뮬레이터 내부 상태 캐시
        // =====================================================================
        // 각 애니메이션 SO의 이벤트 데이터를 런타임에 캐싱
        private float cachedNormTime = 0f;
        private float cachedWarpStart = 0f;
        private float cachedWarpEnd = 0f;
        private List<(float time, int eventType)> cachedEventPoints = new List<(float, int)>();
        private string cachedAnimName = "";

        // 기즈모 그리기 토글
        private bool enableGizmoPreview = true;

        // =====================================================================
        // 스타일 캐시
        // =====================================================================
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _monoStyle;
        private GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = new Color(0.4f, 0.8f, 1f) } };
        private GUIStyle SubStyle => _subHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.9f, 0.7f, 0.4f) } };
        private GUIStyle MonoStyle => _monoStyle ??= new GUIStyle(EditorStyles.label) { font = EditorStyles.miniFont, fontSize = 10 };

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        // =====================================================================
        // 메인 GUI
        // =====================================================================
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayerCamera pc = (PlayerCamera)target;
            if (!Application.isPlaying || pc.player == null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("▶ 게임을 실행(Play)하면 Pro Action Simulator V2, SO 라이브 에디터, 히스토리가 표시됩니다.", MessageType.Info);
                return;
            }

            // 내부 펑션 히스토리 갱신
            UpdateInternalFunctionHistory(pc);

            EditorGUILayout.Space(15);
            WorldCameraManager wcm = WorldCameraManager.Instance;

            DrawVisualDebugSection(wcm);
            DrawDebugPanel(pc, wcm);
            DrawProActionSimulatorV2(pc, wcm);
            DrawCameraGizmoPanel(pc, wcm);
            DrawLiveEditor(wcm);
            DrawHistoryPanel(wcm);

            Repaint();
        }

        // =====================================================================
        // ① 시각적 디버그 도구
        // =====================================================================
        private void DrawVisualDebugSection(WorldCameraManager wcm)
        {
            if (wcm == null) return;
            showVisualDebugSection = EditorGUILayout.Foldout(showVisualDebugSection,
                "🔍 시각적 디버그 도구 (Visual Debug Tools)", true, FoldoutStyle());
            if (!showVisualDebugSection) return;

            EditorGUILayout.BeginVertical("box");
            EditorGUI.BeginChangeCheck();
            wcm.showSOInfoOnScreen = EditorGUILayout.Toggle("SO 정보 온스크린 표시", wcm.showSOInfoOnScreen);
            wcm.showEdgePanningGizmo = EditorGUILayout.Toggle("엣지 패닝 범위 표시", wcm.showEdgePanningGizmo);
            wcm.showTargetEscapeGizmo = EditorGUILayout.Toggle("타겟 이탈 경계 표시", wcm.showTargetEscapeGizmo);
            wcm.showPredictiveAngleGizmo = EditorGUILayout.Toggle("카메라 이동/시선 예측 표시", wcm.showPredictiveAngleGizmo);
            enableGizmoPreview = EditorGUILayout.Toggle("Scene 기즈모 Preview 활성화", enableGizmoPreview);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(wcm);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        // =====================================================================
        // ② 실시간 상태 모니터
        // =====================================================================
        private void DrawDebugPanel(PlayerCamera pc, WorldCameraManager wcm)
        {
            showDebugPanel = EditorGUILayout.Foldout(showDebugPanel,
                "📊 실시간 상태 모니터 (Live Debug)", true, FoldoutStyle());
            if (!showDebugPanel) return;

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField("🌍 World & Player State", SubStyle);
            DrawField("World State", WorldGameStateManager.Instance != null
                ? WorldGameStateManager.Instance.currentState.ToString() : "N/A");

            bool isLockedOn = pc.player.playerNetworkManager != null && pc.player.playerNetworkManager.isLockedOn.Value;
            string tgtName = isLockedOn && pc.player.playerCombatManager?.currentTarget != null
                ? pc.player.playerCombatManager.currentTarget.name : "None";
            DrawField("Lock-On Target", $"[{(isLockedOn ? "<color=lime>ON</color>" : "OFF")}] {tgtName}", true);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("🏃 Animation & Action State", SubStyle);

            int actionID = pc.player.animator != null ? pc.player.animator.GetInteger(AnimatorParameterHash.ActionState) : 0;
            DrawField("Action ID", actionID.ToString());
            DrawField("Is Performing Action", pc.player.isPerformingAction ? "<color=red>True</color>" : "False", true);

            string baseAnim = "N/A", actionAnim = "N/A", animProgress = "0.00%";
            if (pc.player.animator != null)
            {
                var bClips = pc.player.animator.GetCurrentAnimatorClipInfo(0);
                if (bClips.Length > 0 && bClips[0].clip != null) baseAnim = bClips[0].clip.name;
                if (pc.player.animator.layerCount > 1)
                {
                    var aClips = pc.player.animator.GetCurrentAnimatorClipInfo(1);
                    if (aClips.Length > 0 && aClips[0].clip != null) actionAnim = aClips[0].clip.name;
                    var stateInfo = pc.player.animator.GetCurrentAnimatorStateInfo(1);
                    float norm = stateInfo.normalizedTime % 1f;
                    animProgress = $"{norm * 100f:F1}% (Norm: {norm:F3})";
                }
            }

            DrawField("Base Layer Clip", baseAnim);
            DrawField("Action Layer Clip", actionAnim);
            DrawField("Action Progress", $"<color=cyan>{animProgress}</color>", true);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("🎥 Camera Transform & SO Data", SubStyle);
            DrawField("최근 호출 원인 (Caller)", $"<color=lime>{pc.lastCallerReason}</color>", true);

            string seqName = wcm?.currentSequenceSO != null ? wcm.currentSequenceSO.name : "None";
            string stanceName = wcm?.currentStanceSO != null ? wcm.currentStanceSO.name : "None";
            DrawField("Active Sequence SO", $"<color=cyan>{seqName}</color>", true);
            DrawField("Active Stance SO", $"<color=cyan>{stanceName}</color>", true);

            if (wcm != null)
            {
                float pd = wcm.CurrentPositionDamping, rd = wcm.CurrentRotationDamping;
                string dc = (pd > 0.1f || rd > 0.1f) ? "orange" : "white";
                DrawField("Action Damping (Pos/Rot)", $"<color={dc}>{pd:F2} / {rd:F2}</color>", true);

                // ▼ TrackingWeight 실시간 표시 (버그 진단용)
                float tw = wcm.CurrentTrackingWeight;
                string twColor = tw > 0.01f ? "lime" : "red";
                DrawField("Current Tracking Weight", $"<color={twColor}>{tw:F3}</color>", true);
            }

            DrawField("Z축 원본값 (SO Data)", $"{pc.debugRawZ:F2}");
            DrawField("충돌 보정된 목표 Z", $"{pc.debugTargetZ:F2}");
            DrawField("실제 렌더링 Z", $"<color=lime>{pc.debugActualZ:F2}</color>", true);

            float realFov = wcm?.currentFOV ?? (pc.cameraObject != null ? pc.cameraObject.fieldOfView : 0f);
            DrawField("Current FOV (Realtime)", $"<color=yellow>{realFov:F2}</color>", true);

            float yaw = GetPrivateField<float>(pc, "leftAndRightLookAngle");
            float pitch = GetPrivateField<float>(pc, "upAndDownLookAngle");
            DrawField("Camera Angle (Pitch/Yaw)", $"{pitch:F1} / {yaw:F1}");

            EditorGUILayout.EndVertical();
        }

        // =====================================================================
        // ③ Pro Action Simulator V2 ← 핵심 신규 기능
        // =====================================================================
        private void DrawProActionSimulatorV2(PlayerCamera pc, WorldCameraManager wcm)
        {
            showSimulator = EditorGUILayout.Foldout(showSimulator,
                "📽️ Pro Action Simulator V2 — 애니메이션 × 카메라 타임라인", true, FoldoutStyle());
            if (!showSimulator) return;

            EditorGUILayout.BeginVertical("box");

            // ── 데이터 수집 ──────────────────────────────────────────────────
            float animNorm = 0f;
            float animClipLength = 1f;
            string animClipName = "N/A";
            float warpStart = 0f, warpEnd = 0f;
            var eventPoints = new List<(float t, int ev)>();

            if (pc.player?.animator != null && pc.player.animator.layerCount > 1)
            {
                var stInfo = pc.player.animator.IsInTransition(1)
                    ? pc.player.animator.GetNextAnimatorStateInfo(1)
                    : pc.player.animator.GetCurrentAnimatorStateInfo(1);

                animNorm = stInfo.normalizedTime; // % 1f は意図的に外す（単発アクション用）
                // 재생이 끝난 뒤에도 1.0에 고정
                if (animNorm > 1f) animNorm = 1f;

                var clips = pc.player.animator.GetCurrentAnimatorClipInfo(1);
                if (clips.Length > 0 && clips[0].clip != null)
                {
                    animClipName = clips[0].clip.name;
                    animClipLength = clips[0].clip.length;
                }

                // 애니메이션 SO에서 eventPoints / warp 구간 읽기 (리플렉션으로 캐싱)
                RefreshAnimSOCache(pc, animClipName, ref warpStart, ref warpEnd, ref eventPoints);
            }

            // ── 타이틀 ───────────────────────────────────────────────────────
            EditorGUILayout.LabelField($"▶  {animClipName}  ({animClipLength:F2}s)", SubStyle);
            EditorGUILayout.Space(4);

            // ── 타임라인 렌더링 ──────────────────────────────────────────────
            Rect timelineRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(130));
            DrawAnimTimeline(timelineRect, pc, wcm, animNorm, warpStart, warpEnd, eventPoints, animClipLength);

            EditorGUILayout.Space(4);

            // ── 범례 ─────────────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            DrawLegendSwatch(new Color(0.3f, 0.7f, 1f, 0.5f), "Tracking ON");
            DrawLegendSwatch(new Color(1f, 0.3f, 0.3f, 0.5f), "Tracking OFF");
            DrawLegendSwatch(new Color(1f, 0.8f, 0.1f, 0.8f), "Warp 구간");
            DrawLegendSwatch(new Color(0.2f, 1f, 0.4f, 0.9f), "HitBox");
            DrawLegendSwatch(new Color(0.9f, 0.4f, 1f, 0.9f), "ComboEnable");
            DrawLegendSwatch(new Color(1f, 0.5f, 0.1f, 0.9f), "기타 이벤트");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // ── 버그 진단 패널 ───────────────────────────────────────────────
            if (wcm?.currentSequenceSO != null)
            {
                DrawTrackingWindowDiagnostic(wcm, animNorm, animClipLength);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 애니메이션 SO를 리플렉션으로 찾아 warp/event 데이터를 캐싱
        /// </summary>
        private void RefreshAnimSOCache(PlayerCamera pc, string clipName,
            ref float ws, ref float we, ref List<(float, int)> evts)
        {
            if (clipName == cachedAnimName && cachedEventPoints != null)
            {
                ws = cachedWarpStart;
                we = cachedWarpEnd;
                evts = cachedEventPoints;
                return;
            }

            // CharacterAnimationSetSO 또는 AnimationSO를 직接 찾는 방법은 없으므로
            // Assets 내 같은 이름의 SO를 GUID 없이 이름 검색으로 폴백
            cachedAnimName = clipName;
            cachedEventPoints = new List<(float, int)>();
            cachedWarpStart = 0f;
            cachedWarpEnd = 0f;

            // 프로젝트 내 모든 ScriptableObject에서 m_Name == clipName인 것을 찾기
            string[] guids = AssetDatabase.FindAssets($"t:ScriptableObject {clipName}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so == null || so.name != clipName) continue;

                // 리플렉션으로 필드 읽기
                var soType = so.GetType();

                // warpStartTime / warpEndTime
                var wsField = soType.GetField("warpStartTime",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var weField = soType.GetField("warpEndTime",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (wsField != null) cachedWarpStart = (float)wsField.GetValue(so);
                if (weField != null) cachedWarpEnd = (float)weField.GetValue(so);

                // eventPoints
                var epField = soType.GetField("eventPoints",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (epField != null)
                {
                    var epList = epField.GetValue(so) as System.Collections.IList;
                    if (epList != null)
                    {
                        foreach (var ep in epList)
                        {
                            var epType = ep.GetType();
                            var tField = epType.GetField("triggerTime",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            var evField = epType.GetField("eventType",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (tField == null || evField == null) continue;
                            float t = (float)tField.GetValue(ep);
                            int ev = Convert.ToInt32(evField.GetValue(ep));
                            cachedEventPoints.Add((t, ev));
                        }
                    }
                }
                break;
            }

            ws = cachedWarpStart;
            we = cachedWarpEnd;
            evts = cachedEventPoints;
        }

        /// <summary>
        /// 핵심: 타임라인 2행 렌더링
        /// Row 1: 애니메이션 축 (warp / events / 재생 헤드)
        /// Row 2: 카메라 Sequence 축 (tracking window / seq 타이머)
        /// </summary>
        private void DrawAnimTimeline(Rect r, PlayerCamera pc, WorldCameraManager wcm,
            float animNorm, float warpStart, float warpEnd,
            List<(float t, int ev)> evts, float clipLength)
        {
            if (Event.current.type != EventType.Repaint) return;

            float pad = 6f;
            float w = r.width - pad * 2;
            float rowH = 40f;
            float labelH = 16f;

            // ── Row 0: 시간 눈금자 ───────────────────────────────────────────
            Rect rulerRect = new Rect(r.x + pad, r.y, w, labelH);
            DrawRuler(rulerRect, w, clipLength);

            // ── Row 1: 애니메이션 타임라인 ──────────────────────────────────
            Rect animRow = new Rect(r.x + pad, r.y + labelH + 2, w, rowH);
            DrawRulerLabel(new Rect(r.x, animRow.y + rowH * 0.5f - 7, pad, 14), "ANIM");
            EditorGUI.DrawRect(animRow, new Color(0.15f, 0.15f, 0.15f, 1f));

            // warp 구간
            if (warpEnd > warpStart)
            {
                Rect warpR = SubRect(animRow, warpStart, warpEnd);
                EditorGUI.DrawRect(warpR, new Color(1f, 0.8f, 0.1f, 0.35f));
                DrawRectOutline(warpR, new Color(1f, 0.8f, 0.1f, 0.9f));
            }

            // 이벤트 마커
            foreach (var (t, ev) in evts)
            {
                Color mc = GetEventColor(ev);
                float mx = animRow.x + t * w;
                EditorGUI.DrawRect(new Rect(mx - 1, animRow.y, 2, animRow.height), mc);
                // 이벤트 타입 레이블 (짧게)
                string evLabel = GetEventShortLabel(ev);
                GUI.Label(new Rect(mx - 14, animRow.y + animRow.height + 1, 30, 12),
                    evLabel, new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = mc }, fontSize = 9 });
            }

            // 재생 헤드
            float headX = animRow.x + Mathf.Clamp01(animNorm) * w;
            EditorGUI.DrawRect(new Rect(headX - 1, animRow.y - 3, 2, animRow.height + 6), Color.white);

            // 현재 시간 텍스트
            GUI.Label(new Rect(headX + 3, animRow.y, 60, 14),
                $"{animNorm * clipLength:F2}s", MonoStyle);

            // ── Row 2: 카메라 Sequence 타임라인 ─────────────────────────────
            float row2Y = animRow.y + rowH + 22f;
            Rect seqRow = new Rect(r.x + pad, row2Y, w, rowH);
            DrawRulerLabel(new Rect(r.x, seqRow.y + rowH * 0.5f - 7, pad, 14), "SEQ");
            EditorGUI.DrawRect(seqRow, new Color(0.1f, 0.1f, 0.2f, 1f));

            if (wcm?.currentSequenceSO != null)
            {
                var seq = wcm.currentSequenceSO;
                float seqElapsed = 0f;
                float seqTotal = 0f;

                // 각 Step의 blendDuration + holdDuration 합산으로 총 Seq 시간 계산
                foreach (var step in seq.steps)
                    seqTotal += step.blendDuration + step.holdDuration;
                if (seqTotal < 0.001f) seqTotal = 1f;

                // 현재 Seq 진행도: WorldCameraManager에 직접 노출된 타이머가 없으므로
                // 애니메이션 normalizedTime × (첫 스텝 totalDur / clipLength) 로 근사
                float stepTotalDur = (seq.steps.Count > 0)
                    ? seq.steps[0].blendDuration + seq.steps[0].holdDuration : 1f;
                float seqNorm = (stepTotalDur > 0.001f)
                    ? Mathf.Clamp01(animNorm * clipLength / stepTotalDur) : animNorm;

                // 각 Step 렌더링
                float stepOffset = 0f;
                foreach (var step in seq.steps)
                {
                    float stepDur = step.blendDuration + step.holdDuration;
                    float stepStart = stepOffset / seqTotal;
                    float stepEnd = (stepOffset + stepDur) / seqTotal;

                    // 스텝 배경
                    Rect stepR = SubRect(seqRow, stepStart, stepEnd);
                    EditorGUI.DrawRect(stepR, new Color(0.2f, 0.2f, 0.35f, 0.7f));
                    DrawRectOutline(stepR, new Color(0.5f, 0.5f, 0.8f, 0.4f));

                    // ★ Tracking Window 오버레이
                    float tws = stepStart + step.trackingStartTime * (stepEnd - stepStart);
                    float twe = stepStart + step.trackingEndTime * (stepEnd - stepStart);
                    Rect trackR = SubRect(seqRow, tws, twe);
                    EditorGUI.DrawRect(trackR, new Color(0.3f, 0.7f, 1f, 0.4f));
                    DrawRectOutline(trackR, new Color(0.3f, 0.7f, 1f, 0.9f));

                    // Tracking OFF 영역 (스텝 내 나머지)
                    if (step.trackingStartTime > 0f)
                    {
                        Rect offL = SubRect(seqRow, stepStart, tws);
                        EditorGUI.DrawRect(offL, new Color(1f, 0.3f, 0.3f, 0.15f));
                    }
                    if (step.trackingEndTime < 1f)
                    {
                        Rect offR = SubRect(seqRow, twe, stepEnd);
                        EditorGUI.DrawRect(offR, new Color(1f, 0.3f, 0.3f, 0.15f));
                    }

                    // Blend 경계선
                    if (step.blendDuration > 0f)
                    {
                        float blendEnd = stepStart + (step.blendDuration / seqTotal);
                        float bx = seqRow.x + blendEnd * w;
                        EditorGUI.DrawRect(new Rect(bx, seqRow.y, 1, seqRow.height),
                            new Color(1f, 1f, 0.3f, 0.6f));
                    }

                    stepOffset += stepDur;
                }

                // Seq 재생 헤드 (근사값)
                float seqHeadX = seqRow.x + Mathf.Clamp01(seqNorm) * w;
                EditorGUI.DrawRect(new Rect(seqHeadX - 1, seqRow.y - 3, 2, seqRow.height + 6),
                    new Color(0.3f, 0.9f, 1f, 1f));

                // CurrentTrackingWeight 게이지 바
                float tw = wcm.CurrentTrackingWeight;
                float gaugeW = tw * w;
                float gaugeY = seqRow.y + seqRow.height + 4f;
                EditorGUI.DrawRect(new Rect(seqRow.x, gaugeY, w, 6f),
                    new Color(0.1f, 0.1f, 0.1f, 0.8f));
                Color gaugeColor = Color.Lerp(Color.red, new Color(0.3f, 1f, 0.4f, 1f), tw);
                EditorGUI.DrawRect(new Rect(seqRow.x, gaugeY, gaugeW, 6f), gaugeColor);
                GUI.Label(new Rect(seqRow.x + w + 4, gaugeY - 1, 50, 12),
                    $"TW:{tw:F2}", MonoStyle);
            }
        }

        /// <summary>
        /// 시간 눈금자 (0%, 25%, 50%, 75%, 100%)
        /// </summary>
        private void DrawRuler(Rect r, float w, float clipLength)
        {
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f, 1f));
            int ticks = 5;
            for (int i = 0; i <= ticks; i++)
            {
                float frac = (float)i / ticks;
                float tx = r.x + frac * w;
                EditorGUI.DrawRect(new Rect(tx, r.y + r.height - 4, 1, 4),
                    new Color(0.6f, 0.6f, 0.6f, 1f));
                string lbl = $"{frac * clipLength:F2}s";
                GUI.Label(new Rect(tx - 12, r.y, 30, 12), lbl, MonoStyle);
            }
        }

        private void DrawRulerLabel(Rect r, string text)
        {
            GUI.Label(r, text, new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) },
                fontSize = 8
            });
        }

        /// <summary>
        /// Tracking Window 버그 진단 패널
        /// 핵심: "Seq normTime"과 "Anim normalizedTime"이 서로 다른 시간축임을 명시
        /// </summary>
        private void DrawTrackingWindowDiagnostic(WorldCameraManager wcm, float animNorm, float clipLength)
        {
            var seq = wcm.currentSequenceSO;
            if (seq == null || seq.steps == null || seq.steps.Count == 0) return;

            var step = seq.steps[0]; // 첫 스텝 기준 진단
            float totalDur = step.blendDuration + step.holdDuration;

            // ─────────────────────────────────────────────────────────────────
            // 버그 핵심 설명: normTime = seqTimer / totalDuration  (Seq 내부 시간)
            //                 animNorm  = animator.normalizedTime  (애니메이션 시간)
            // 두 값이 일치하지 않으면 trackingStart/End는 아무 의미도 없다.
            // ─────────────────────────────────────────────────────────────────
            float seqEquivNorm = Mathf.Clamp01(animNorm * clipLength / Mathf.Max(totalDur, 0.001f));
            float twStart_sec = step.trackingStartTime * totalDur;
            float twEnd_sec = step.trackingEndTime * totalDur;

            // Seq 시간축에서 tracking이 켜지는 실제 초
            // Anim 진행도로 환산하면:
            float animEquivStart = twStart_sec / Mathf.Max(clipLength, 0.001f);
            float animEquivEnd = twEnd_sec / Mathf.Max(clipLength, 0.001f);

            bool inWindow = (seqEquivNorm >= step.trackingStartTime &&
                             seqEquivNorm <= step.trackingEndTime);
            float tw = wcm.CurrentTrackingWeight;

            EditorGUILayout.LabelField("⚠️ Tracking Window 진단 (버그 분석)", SubStyle);
            EditorGUILayout.BeginVertical("helpbox");

            // 상태 표시
            if (Mathf.Abs(tw) < 0.01f && animNorm > 0.01f && animNorm < 0.99f)
            {
                EditorGUILayout.HelpBox(
                    $"[Tracking OFF] 현재 TrackingWeight = {tw:F3}\n" +
                    $"Seq normTime ≈ {seqEquivNorm:F3}이 trackingWindow [{step.trackingStartTime:F3}~{step.trackingEndTime:F3}] 밖에 있음.",
                    MessageType.Warning);
            }
            else if (inWindow)
            {
                EditorGUILayout.HelpBox(
                    $"[Tracking ON] Weight={tw:F3}  |  Seq norm={seqEquivNorm:F3}",
                    MessageType.Info);
            }

            // 시간축 단절 경고
            EditorGUILayout.HelpBox(
                "🚨 구조적 단절 주의\n" +
                $"trackingStart/End는 Seq 내부 타이머 기준(0~1 = {totalDur:F2}s)입니다.\n" +
                $"현재 설정: [{step.trackingStartTime:F2} ~ {step.trackingEndTime:F2}] → 실제 {twStart_sec:F3}s ~ {twEnd_sec:F3}s\n" +
                $"클립 길이({clipLength:F2}s) 기준 애니메이션 진행도: {animEquivStart:F2} ~ {animEquivEnd:F2}\n\n" +
                "trackingWeight를 애니메이션 normalizedTime 기준으로 계산하려면\n" +
                "BlendToStanceRoutine에 아래 패치를 적용하세요. (하단 코드 패치 패널 참조)",
                MessageType.Warning);

            EditorGUILayout.EndVertical();
        }

        // =====================================================================
        // ④ 카메라 기즈모 Preview 패널
        // =====================================================================
        private void DrawCameraGizmoPanel(PlayerCamera pc, WorldCameraManager wcm)
        {
            showCameraGizmoPanel = EditorGUILayout.Foldout(showCameraGizmoPanel,
                "🎥 카메라 기즈모 Preview (Tracking Window 연동)", true, FoldoutStyle());
            if (!showCameraGizmoPanel) return;

            EditorGUILayout.BeginVertical("box");

            if (!enableGizmoPreview)
            {
                EditorGUILayout.HelpBox("기즈모 Preview가 비활성화되어 있습니다. 상단 '시각적 디버그 도구'에서 활성화하세요.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            float tw = wcm?.CurrentTrackingWeight ?? 1f;
            bool isTracking = tw > 0.01f;

            EditorGUILayout.LabelField(
                isTracking
                    ? $"<color=lime>● TRACKING ON  (weight {tw:F3})</color>"
                    : $"<color=red>● TRACKING OFF (weight {tw:F3})</color>",
                new GUIStyle(EditorStyles.boldLabel) { richText = true });

            EditorGUILayout.Space(4);

            // 미니 카메라 상태 표
            if (wcm != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"FOV {wcm.currentFOV:F1}°", GizmoStatStyle());
                GUILayout.Label($"Z {wcm.currentBaseOffset.z:F2}", GizmoStatStyle());
                GUILayout.Label($"Y {wcm.currentBaseOffset.y:F2}", GizmoStatStyle());
                GUILayout.Label($"ZTilt {wcm.currentZTilt:F1}°", GizmoStatStyle());
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Scene View에서 확인하세요:\n" +
                "🔵 파란 Frustum = Tracking ON 구간 (카메라가 캐릭터를 따라감)\n" +
                "🔴 빨간 Frustum = Tracking OFF 구간 (카메라 자유 시점)\n" +
                "○ 노란 원 = 현재 LockOn 타겟 위치\n" +
                "↔ 흰 선 = trackingStart~End 경계",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        // =====================================================================
        // OnSceneGUI — Scene View 기즈모 실제 렌더링
        // =====================================================================
        private void OnSceneGUI()
        {
            if (!Application.isPlaying || !enableGizmoPreview) return;

            PlayerCamera pc = (PlayerCamera)target;
            if (pc == null || pc.player == null || pc.cameraObject == null) return;

            WorldCameraManager wcm = WorldCameraManager.Instance;
            if (wcm == null) return;

            float tw = wcm.CurrentTrackingWeight;
            bool tracking = tw > 0.01f;

            // ── 카메라 피벗 & 현재 위치 ─────────────────────────────────────
            Vector3 camPos = pc.cameraObject.transform.position;
            Vector3 playerPos = pc.player.transform.position;
            Vector3 massCenter = playerPos + Vector3.up * 1.4f;

            // ── Frustum 색상 (Tracking 여부) ─────────────────────────────────
            Color frustumColor = tracking
                ? new Color(0.3f, 0.6f, 1f, 0.5f)
                : new Color(1f, 0.3f, 0.3f, 0.4f);
            Handles.color = frustumColor;

            // Frustum 근사 (카메라 방향으로 4개의 면 그리기)
            DrawCameraFrustumGizmo(pc.cameraObject, frustumColor, wcm.currentFOV);

            // ── 피벗 → 카메라 연결선 ─────────────────────────────────────────
            Handles.color = Color.white;
            Handles.DrawLine(massCenter, camPos);
            Handles.SphereHandleCap(0, massCenter, Quaternion.identity, 0.08f, EventType.Repaint);

            // ── 카메라 오프셋 목표 위치 (SO 데이터) ─────────────────────────
            Vector3 offsetTarget = massCenter
                + pc.player.transform.right * wcm.currentBaseOffset.x
                + Vector3.up * (wcm.currentBaseOffset.y - 1.4f)
                + pc.player.transform.forward * wcm.currentBaseOffset.z;

            Handles.color = new Color(1f, 0.9f, 0.2f, 0.6f);
            Handles.DrawDottedLine(massCenter, offsetTarget, 3f);
            Handles.SphereHandleCap(0, offsetTarget, Quaternion.identity, 0.06f, EventType.Repaint);

            // ── Tracking Window 경계 마커 ────────────────────────────────────
            if (wcm.currentSequenceSO?.steps?.Count > 0)
            {
                var step = wcm.currentSequenceSO.steps[0];
                float totalDur = step.blendDuration + step.holdDuration;

                // Start 경계 (초록 원)
                Handles.color = new Color(0.3f, 1f, 0.5f, 0.9f);
                Handles.DrawWireArc(massCenter, Vector3.up, Vector3.forward,
                    360f, 0.5f + step.trackingStartTime * 0.8f);
                Handles.Label(massCenter + Vector3.up * 0.3f + Vector3.right * 0.6f,
                    $"TW Start\n{step.trackingStartTime:F2} ({step.trackingStartTime * totalDur:F2}s)",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.3f, 1f, 0.5f) } });

                // End 경계 (빨간 원)
                Handles.color = new Color(1f, 0.4f, 0.3f, 0.9f);
                Handles.DrawWireArc(massCenter, Vector3.up, Vector3.forward,
                    360f, 0.5f + step.trackingEndTime * 0.8f);
                Handles.Label(massCenter + Vector3.up * 0.3f - Vector3.right * 1.0f,
                    $"TW End\n{step.trackingEndTime:F2} ({step.trackingEndTime * totalDur:F2}s)",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.4f, 0.3f) } });
            }

            // ── LockOn 타겟 ──────────────────────────────────────────────────
            if (pc.player.playerCombatManager?.currentTarget != null)
            {
                var tgt = pc.player.playerCombatManager.currentTarget;
                Vector3 tgtPos = tgt.transform.position + Vector3.up * 1.0f;
                Handles.color = new Color(1f, 0.8f, 0.1f, 0.9f);
                Handles.DrawWireArc(tgtPos, Vector3.up, Vector3.forward, 360f, 0.4f);
                Handles.DrawLine(camPos, tgtPos);
                Handles.Label(tgtPos + Vector3.up * 0.5f,
                    $"[LockOn] {tgt.name}",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.yellow } });
            }

            // ── TrackingWeight 수직 게이지 ───────────────────────────────────
            Handles.color = Color.Lerp(Color.red, Color.cyan, tw);
            Vector3 gaugeBot = playerPos + Vector3.up * 3.0f;
            Vector3 gaugeTop = gaugeBot + Vector3.up * tw * 1.2f;
            Handles.DrawLine(gaugeBot, gaugeTop);
            Handles.Label(gaugeTop + Vector3.right * 0.1f,
                $"TW {tw:F2}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.cyan } });
        }

        private void DrawCameraFrustumGizmo(Camera cam, Color color, float overrideFov)
        {
            Matrix4x4 old = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(cam.transform.position, cam.transform.rotation, Vector3.one);
            Handles.color = color;

            float aspect = cam.aspect > 0f ? cam.aspect : 1.777f;
            float nearDist = 0.1f;
            float farDist = 2.0f;

            float halfH_near = Mathf.Tan(overrideFov * 0.5f * Mathf.Deg2Rad) * nearDist;
            float halfW_near = halfH_near * aspect;
            float halfH_far = Mathf.Tan(overrideFov * 0.5f * Mathf.Deg2Rad) * farDist;
            float halfW_far = halfH_far * aspect;

            // 사각형 Near/Far 그리기
            Vector3[] near = {
                new Vector3(-halfW_near, -halfH_near, nearDist),
                new Vector3( halfW_near, -halfH_near, nearDist),
                new Vector3( halfW_near,  halfH_near, nearDist),
                new Vector3(-halfW_near,  halfH_near, nearDist),
            };
            Vector3[] far = {
                new Vector3(-halfW_far, -halfH_far, farDist),
                new Vector3( halfW_far, -halfH_far, farDist),
                new Vector3( halfW_far,  halfH_far, farDist),
                new Vector3(-halfW_far,  halfH_far, farDist),
            };

            for (int i = 0; i < 4; i++)
            {
                Handles.DrawLine(near[i], near[(i + 1) % 4]);
                Handles.DrawLine(far[i], far[(i + 1) % 4]);
                Handles.DrawLine(near[i], far[i]);
            }

            Handles.matrix = old;
        }

        // =====================================================================
        // ⑤ SO 라이브 에디터 (기존과 동일, 코드 축약)
        // =====================================================================
        private void DrawLiveEditor(WorldCameraManager wcm)
        {
            showLiveEditor = EditorGUILayout.Foldout(showLiveEditor,
                "🛠️ 카메라 라이브 에디터 (SO Data-Driven)", true, FoldoutStyle());
            if (!showLiveEditor || wcm == null || wcm.currentStanceSO == null)
            {
                if (showLiveEditor)
                    EditorGUILayout.HelpBox("현재 활성화된 CameraStancePresetSO가 없습니다.", MessageType.Warning);
                return;
            }

            if (wcm.currentStanceSO != lastLiveEditSO)
            {
                lastLiveEditSO = wcm.currentStanceSO;
                SyncFromSO(lastLiveEditSO);
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"현재 SO: {lastLiveEditSO.name}", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("📷 렌즈 및 구도", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"실시간 FOV: {wcm.currentFOV:F2}",
                new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.yellow } });
            editFov = EditorGUILayout.Slider("Target FOV", editFov, 10f, 120f);
            editBaseOffset = EditorGUILayout.Vector3Field("Base Offset", editBaseOffset);
            editZTilt = EditorGUILayout.Slider("Z Tilt", editZTilt, -45f, 45f);
            editBaseYawOffset = EditorGUILayout.Slider("Base Yaw Offset", editBaseYawOffset, -45f, 45f);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("↕️ 수직 시점 방식", EditorStyles.miniBoldLabel);

            // [컴파일 에러 수정] 명시적 네임스페이스 적용
            editBehaviorType = (TDA.Cameras.CameraVerticalBehavior)EditorGUILayout.EnumPopup("Behavior Type", editBehaviorType);

            if (editBehaviorType == TDA.Cameras.CameraVerticalBehavior.DynamicOverShoulder)
            {
                EditorGUI.indentLevel++;
                editPitchForMaxHeight = EditorGUILayout.FloatField("Pitch For Max Height", editPitchForMaxHeight);
                editMaxDynamicHeight = EditorGUILayout.FloatField("Max Dynamic Height", editMaxDynamicHeight);
                editHeightSmoothTime = EditorGUILayout.FloatField("Height Smooth Time", editHeightSmoothTime);
                EditorGUI.indentLevel--;
            }
            else if (editBehaviorType == TDA.Cameras.CameraVerticalBehavior.ElevationOnly)
            {
                EditorGUI.indentLevel++;
                editElevationSpeed = EditorGUILayout.FloatField("Elevation Speed", editElevationSpeed);
                editMaxElevationHeight = EditorGUILayout.FloatField("Max Elevation Height", editMaxElevationHeight);
                editMinElevationHeight = EditorGUILayout.FloatField("Min Elevation Height", editMinElevationHeight);
                editFixedPitchAngle = EditorGUILayout.FloatField("Fixed Pitch Angle", editFixedPitchAngle);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("📳 핸드헬드 & 쉐이크", EditorStyles.miniBoldLabel);
            editEnableHandheld = EditorGUILayout.Toggle("Enable Handheld", editEnableHandheld);
            if (editEnableHandheld)
            {
                editSwayAmount = EditorGUILayout.FloatField("Sway Amount", editSwayAmount);
                editSwaySpeed = EditorGUILayout.FloatField("Sway Speed", editSwaySpeed);
                editBobbingAmount = EditorGUILayout.FloatField("Bobbing Amount", editBobbingAmount);
            }
            editEnableShake = EditorGUILayout.Toggle("Enable Shake", editEnableShake);
            if (editEnableShake)
            {
                editShakeIntensity = EditorGUILayout.FloatField("Shake Intensity", editShakeIntensity);
                editShakeDelay = EditorGUILayout.FloatField("Shake Delay", editShakeDelay);
                editShakeDuration = EditorGUILayout.FloatField("Max Duration", editShakeDuration);

                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("💥 쉐이크 테스트!", GUILayout.Height(25)))
                    wcm.ApplyCameraShake(editShakeIntensity, editShakeDuration);
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.Space(10);
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("💾 SO에 즉시 저장 (Apply to SO)", GUILayout.Height(35)))
            {
                Undo.RecordObject(lastLiveEditSO, "Live Edit Camera Stance");
                ApplyToSO(lastLiveEditSO);
                var m = typeof(WorldCameraManager).GetMethod("ApplyStanceInstantly",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                m?.Invoke(wcm, new object[] { lastLiveEditSO });
                EditorUtility.SetDirty(lastLiveEditSO);
                AssetDatabase.SaveAssets();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        // =====================================================================
        // ⑥ 히스토리 패널 (기존과 동일)
        // =====================================================================
        private void DrawHistoryPanel(WorldCameraManager wcm)
        {
            showHistoryPanel = EditorGUILayout.Foldout(showHistoryPanel,
                "📜 카메라 상태 & 펑션 히스토리", true, FoldoutStyle());
            if (!showHistoryPanel) return;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("🎥 SO 변경 히스토리", EditorStyles.boldLabel);

            if (wcm?.historyList == null || wcm.historyList.Count == 0)
            {
                EditorGUILayout.HelpBox("기록된 히스토리가 없습니다.", MessageType.Info);
            }
            else
            {
                GUIStyle linkStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 11 };
                historyScrollPos = EditorGUILayout.BeginScrollView(historyScrollPos, GUILayout.Height(160));
                for (int i = 0; i < wcm.historyList.Count; i++)
                {
                    var rec = wcm.historyList[i];
                    GUI.backgroundColor = i % 2 == 0 ? new Color(0.2f, 0.2f, 0.2f, 0.3f) : Color.clear;
                    EditorGUILayout.BeginHorizontal("helpbox");
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.LabelField(rec.timestamp, GUILayout.Width(85));
                    if (GUILayout.Button(rec.seqSO != null ? rec.seqSO.name : "-", linkStyle, GUILayout.Width(130)))
                        if (rec.seqSO != null) TDA.InternalEditorUtils.TDAInspectorWindow.Open(rec.seqSO);
                    if (GUILayout.Button(rec.stanceSO != null ? rec.stanceSO.name : "-", linkStyle, GUILayout.Width(130)))
                        if (rec.stanceSO != null) TDA.InternalEditorUtils.TDAInspectorWindow.Open(rec.stanceSO);
                    EditorGUILayout.LabelField(rec.callerEvent, GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("히스토리 지우기", GUILayout.Height(22)))
                    wcm.historyList.Clear();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("⚙️ 내부 펑션 호출 히스토리", EditorStyles.boldLabel);
            if (internalFunctionHistory.Count == 0)
            {
                EditorGUILayout.HelpBox("기록된 내역이 없습니다.", MessageType.Info);
            }
            else
            {
                internalHistoryScrollPos = EditorGUILayout.BeginScrollView(internalHistoryScrollPos, GUILayout.Height(130));
                for (int i = 0; i < internalFunctionHistory.Count; i++)
                {
                    GUI.backgroundColor = i % 2 == 0 ? new Color(0.2f, 0.2f, 0.2f, 0.3f) : Color.clear;
                    EditorGUILayout.BeginHorizontal("helpbox");
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.LabelField(internalFunctionHistory[i],
                        new GUIStyle(EditorStyles.label) { richText = true });
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("히스토리 지우기", GUILayout.Height(22)))
                    internalFunctionHistory.Clear();
            }
            EditorGUILayout.EndVertical();
        }

        // =====================================================================
        // 유틸리티
        // =====================================================================
        private void UpdateInternalFunctionHistory(PlayerCamera pc)
        {
            if (pc.lastCallerReason == lastRecordedCallerReason) return;
            lastRecordedCallerReason = pc.lastCallerReason;
            string ts = System.DateTime.Now.ToString("HH:mm:ss.fff");
            internalFunctionHistory.Insert(0, $"<color=cyan>[{ts}]</color> {lastRecordedCallerReason}");
            if (internalFunctionHistory.Count > 50)
                internalFunctionHistory.RemoveAt(internalFunctionHistory.Count - 1);
        }

        private Rect SubRect(Rect r, float t0, float t1)
        {
            float x0 = r.x + t0 * r.width;
            float x1 = r.x + t1 * r.width;
            return new Rect(x0, r.y, Mathf.Max(x1 - x0, 1f), r.height);
        }

        private void DrawRectOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x + r.width - 1, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - 1, r.width, 1), c);
        }

        private void DrawLegendSwatch(Color c, string label)
        {
            Rect swatchRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            EditorGUI.DrawRect(swatchRect, c);
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(80));
        }

        private Color GetEventColor(int ev)
        {
            // eventType 값 기반 색상 분류 (AnimationEventType enum 기준)
            // HitBoxEnable: ~310, HitBoxDisable: ~311
            // ComboEnable: ~0(실제값 확인 필요), ComboDisable/Action_Ended: ~3
            // Lock_Rotation: ~2, CameraShake계열: 354, 605 등
            if (ev == 310 || ev == 311) return new Color(0.2f, 1f, 0.4f, 0.9f);  // HitBox → 초록
            if (ev == 0) return new Color(0.9f, 0.4f, 1f, 0.9f);  // ComboEnable → 보라
            if (ev == 3) return new Color(0.5f, 0.5f, 0.5f, 0.9f);// Action_Ended → 회색
            if (ev == 2) return new Color(1f, 0.8f, 0.2f, 0.9f);  // Lock_Rotation → 노랑
            if (ev >= 350 && ev < 400) return new Color(1f, 0.3f, 0.3f, 0.9f);  // Shake계열 → 빨강
            if (ev >= 600) return new Color(1f, 0.5f, 0.1f, 0.9f);  // VFX계열 → 주황
            return new Color(0.7f, 0.7f, 0.7f, 0.8f);
        }

        private string GetEventShortLabel(int ev)
        {
            if (ev == 310) return "HitOn";
            if (ev == 311) return "HitOff";
            if (ev == 0) return "Cmb+";
            if (ev == 3) return "End";
            if (ev == 2) return "LkRt";
            if (ev >= 350 && ev < 400) return "Shk";
            if (ev >= 600) return "VFX";
            return $"E{ev}";
        }

        private GUIStyle FoldoutStyle() =>
            new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold };

        private GUIStyle GizmoStatStyle() =>
            new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

        private void DrawField(string label, string value, bool richText = false)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(190));
            GUIStyle s = new GUIStyle(EditorStyles.label) { richText = richText, fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(value, s);
            EditorGUILayout.EndHorizontal();
        }

        private T GetPrivateField<T>(object target, string name)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (T)f.GetValue(target) : default;
        }

        // =====================================================================
        // SO 동기화 / 적용
        // =====================================================================
        private void SyncFromSO(CameraStancePresetSO so)
        {
            if (so == null) return;
            editFov = so.fov; editBaseOffset = so.baseOffset; editZTilt = so.zTilt;
            editBaseYawOffset = so.baseYawOffset;
            editBehaviorType = so.verticalBehavior.behaviorType;
            editElevationSpeed = so.verticalBehavior.elevationSpeed;
            editMaxElevationHeight = so.verticalBehavior.maxElevationHeight;
            editMinElevationHeight = so.verticalBehavior.minElevationHeight;
            editFixedPitchAngle = so.verticalBehavior.fixedPitchAngle;
            editPitchForMaxHeight = so.verticalBehavior.pitchForMaxHeight;
            editMaxDynamicHeight = so.verticalBehavior.maxDynamicHeight;
            editHeightSmoothTime = so.verticalBehavior.heightSmoothTime;
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
            so.fov = editFov; so.baseOffset = editBaseOffset; so.zTilt = editZTilt;
            so.baseYawOffset = editBaseYawOffset;
            so.verticalBehavior.behaviorType = editBehaviorType;
            so.verticalBehavior.elevationSpeed = editElevationSpeed;
            so.verticalBehavior.maxElevationHeight = editMaxElevationHeight;
            so.verticalBehavior.minElevationHeight = editMinElevationHeight;
            so.verticalBehavior.fixedPitchAngle = editFixedPitchAngle;
            so.verticalBehavior.pitchForMaxHeight = editPitchForMaxHeight;
            so.verticalBehavior.maxDynamicHeight = editMaxDynamicHeight;
            so.verticalBehavior.heightSmoothTime = editHeightSmoothTime;
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
    }
}