// =============================================================================
// TDAMasterInspector_Tabs.cs  |  TDA Project
// 경로 : Assets/Editor/TDAMasterInspector_Tabs.cs
//
// 역할 : 탭별 콘텐츠 렌더링
//   - 탭 ① 애니메이션 : AnimationSet / Mapping / EventPoint 타임라인
//   - 탭 ② 카메라     : SO 연결 매트릭스 / 런타임 상태 게이지
//   - 탭 ③ 이펙트     : Blur 펄스 차트 / SFX 배열 / VFX
//   - 탭 ④ 무기       : 스탯 바 / 액션 체인
//
// 공통 UI 헬퍼 → TDAMasterInspector_Utils.cs 참조
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TDA.Cameras;
using TDA.Character;
using TDA.Character.AI;
using TDA.Character.Animation;
using TDA.Character.Player;
using TDA.World;
using UnityEditor;
using UnityEngine;

namespace TDA.EditorTools
{
    public partial class TDAMasterInspector
    {
        // =====================================================================
        // 탭 ① — 애니메이션
        // =====================================================================
        private void DrawAnimationTab()
        {
            var c = SelectedChar;
            var animMgr = c.GetComponent<CharacterAnimationManager>();

            if (animMgr == null)
            {
                WarnBox("CharacterAnimationManager 컴포넌트가 없습니다.");
                return;
            }

            var animMgrSer = new SerializedObject(animMgr);
            animMgrSer.Update();

            // ── AnimationSet SO 슬롯 ──────────────────────────────────────
            if (BeginFoldout($"{c.name}/anim/setso", "📦 AnimationSet SO", true))
            {
                EditorGUI.indentLevel++;
                var setSOProp = animMgrSer.FindProperty("baseAnimationSet");
                if (setSOProp != null)
                {
                    SOFieldRow(animMgrSer, setSOProp, "AnimationSet SO");
                    if (animMgrSer.ApplyModifiedProperties()) MarkDirty(animMgr);
                }
                EditorGUI.indentLevel--;
            }
            EndFoldout();

            var animSetSO = animMgrSer.FindProperty("baseAnimationSet")
                                ?.objectReferenceValue as CharacterAnimationSetSO;

            if (animSetSO == null)
            {
                WarnBox("AnimationSet SO 미연결 — 위 슬롯에 에셋을 연결하세요.");
                return;
            }

            var animSetSer = new SerializedObject(animSetSO);
            animSetSer.Update();

            // ── 연결 상태 매트릭스 (시각화) ──────────────────────────────
            if (BeginFoldout($"{c.name}/anim/matrix", "📊 연결 상태 한눈에 보기", true))
            {
                DrawAnimConnectionMatrix(animSetSO);
            }
            EndFoldout();

            // ── Mapping 목록 ──────────────────────────────────────────────
            if (BeginFoldout($"{c.name}/anim/mappings",
                $"🎬 Animation Mappings  [{animSetSO.animationMappings.Count}]", true))
            {
                var mappingsProp = animSetSer.FindProperty("animationMappings");

                for (int i = 0; i < animSetSO.animationMappings.Count; i++)
                {
                    var mapping = animSetSO.animationMappings[i];
                    var paramsSO = mapping.paramsSO;
                    bool hasSO = paramsSO != null;
                    bool hasClip = hasSO && paramsSO.targetClip != null;
                    string statusIcon = (!hasSO || !hasClip) ? "⚠" : "✅";
                    string foldKey = $"{c.name}/anim/map/{i}_{mapping.logicalStateName}";

                    // 매핑 헤더 행 (폴드아웃)
                    if (BeginFoldoutColored(foldKey,
                        $"{statusIcon}  {mapping.logicalStateName}",
                        hasSO && hasClip,
                        indentLevel: 1))
                    {
                        var elemProp = mappingsProp.GetArrayElementAtIndex(i);
                        var soRefProp = elemProp.FindPropertyRelative("paramsSO");
                        EditorGUI.indentLevel += 2;

                        // paramsSO 슬롯
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.PropertyField(soRefProp,
                                new GUIContent("Params SO"), GUILayout.ExpandWidth(true));
                            if (soRefProp.objectReferenceValue != null &&
                                GUILayout.Button("↗", GUILayout.Width(26)))
                            {
                                EditorGUIUtility.PingObject(soRefProp.objectReferenceValue);
                                Selection.activeObject = soRefProp.objectReferenceValue;
                            }
                        }

                        if (hasSO)
                        {
                            var pSer = new SerializedObject(paramsSO);
                            pSer.Update();

                            // 핵심 SO 필드
                            SOFieldRow(pSer, pSer.FindProperty("targetClip"),
                                "Clip", warnIfNull: true);
                            SOFieldRow(pSer, pSer.FindProperty("cameraSequence"),
                                "Camera Seq", warnIfNull: !hasClip, warnMsg: "미연결");
                            SOFieldRow(pSer, pSer.FindProperty("lockOnStanceOverride"),
                                "LockOn Stance (선택)");

                            EditorGUILayout.Space(3);

                            // ── 이벤트 포인트 타임라인 (시각화) ──────────
                            string epKey = $"{foldKey}/events";
                            if (BeginFoldout(epKey,
                                $"⏱ 이벤트 포인트  [{paramsSO.eventPoints.Count}]", false,
                                indentOverride: 2))
                            {
                                if (paramsSO.eventPoints.Count == 0)
                                    WarnLabel("이벤트 포인트 없음");
                                else
                                    DrawEventTimeline(paramsSO.eventPoints);
                            }
                            EndFoldout();

                            // ── 액션 플래그 (뱃지형) ──────────────────────
                            string flagKey = $"{foldKey}/flags";
                            if (BeginFoldout(flagKey, "🚦 Action Flags", false, indentOverride: 2))
                            {
                                DrawActionFlagBadges(pSer);
                            }
                            EndFoldout();

                            if (pSer.ApplyModifiedProperties()) MarkDirty(paramsSO);
                        }

                        EditorGUI.indentLevel -= 2;
                    }
                    EndFoldout();
                }

                if (animSetSer.ApplyModifiedProperties()) MarkDirty(animSetSO);
            }
            EndFoldout();
        }

        // 연결 상태 매트릭스 그리드
        private void DrawAnimConnectionMatrix(CharacterAnimationSetSO animSetSO)
        {
            const int cellW = 120, cellH = 22, cols = 4;
            var list = animSetSO.animationMappings;

            EditorGUILayout.Space(4);
            Rect startRect = EditorGUILayout.GetControlRect(false,
                Mathf.Ceil((float)list.Count / cols) * (cellH + 2) + 4);

            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                bool ok = m.paramsSO != null && m.paramsSO.targetClip != null;
                int row = i / cols, col = i % cols;

                Rect cell = new Rect(
                    startRect.x + col * (cellW + 2),
                    startRect.y + row * (cellH + 2),
                    cellW, cellH);

                Color bg = ok ? new Color(0.15f, 0.38f, 0.2f, 0.85f)
                              : new Color(0.42f, 0.18f, 0.12f, 0.85f);
                EditorGUI.DrawRect(cell, bg);

                string label = m.logicalStateName.Length > 14
                    ? m.logicalStateName[..14] + "…" : m.logicalStateName;

                EditorGUI.LabelField(
                    new Rect(cell.x + 4, cell.y + 4, cell.width - 4, 14),
                    (ok ? "✅ " : "⚠ ") + label, EditorStyles.miniLabel);

                // 클릭 → Ping
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none) && m.paramsSO != null)
                {
                    EditorGUIUtility.PingObject(m.paramsSO);
                    Selection.activeObject = m.paramsSO;
                }
            }
            EditorGUILayout.Space(2);
        }

        // 이벤트 포인트 타임라인 시각화
        private void DrawEventTimeline(
            IList<AnimationEventPoint> eventPoints)
        {
            float totalDuration = eventPoints.Count > 0
                ? Mathf.Max(eventPoints.Max(e => e.triggerTime) * 1.15f, 1f) : 1f;

            Rect timelineRect = EditorGUILayout.GetControlRect(false, 32f);
            timelineRect.xMin += EditorGUI.indentLevel * 15;

            // 배경
            EditorGUI.DrawRect(timelineRect, new Color(0.08f, 0.08f, 0.12f, 0.9f));
            // 0~1 눈금 선
            for (int t = 0; t <= 4; t++)
            {
                float nx = timelineRect.x + (t / 4f) * timelineRect.width;
                EditorGUI.DrawRect(new Rect(nx, timelineRect.y, 1, timelineRect.height),
                    new Color(0.3f, 0.3f, 0.3f, 0.6f));
            }

            // 이벤트 마커
            foreach (var ep in eventPoints)
            {
                float nx = timelineRect.x +
                    (ep.triggerTime / totalDuration) * timelineRect.width;
                Color mc = ep.useOverlay
                    ? new Color(1f, 0.65f, 0.1f) : new Color(0.35f, 0.75f, 1f);
                EditorGUI.DrawRect(new Rect(nx - 1, timelineRect.y + 2, 3, timelineRect.height - 4), mc);

                // 툴팁 레이블
                EditorGUI.LabelField(
                    new Rect(nx - 14, timelineRect.yMax + 1, 32, 11),
                    $"{ep.triggerTime:F2}", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(14);

            // 이벤트 리스트
            foreach (var ep in eventPoints)
            {
                string line = $"  t={ep.triggerTime:F2}  {ep.eventType}";
                if (ep.useOverlay)
                    line += $"  [FOV{ep.overlayData.fovDelta:+0;-0}" +
                            $"  Blur:{ep.overlayData.blurStrengthDelta:F2}]";
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            }
        }

        // 액션 플래그 뱃지 행
        private void DrawActionFlagBadges(SerializedObject pSer)
        {
            string[] props = { "applyRootMotion", "isPerformingAction", "canMove", "canRotate" };
            string[] labels = { "RootMotion", "Performing", "CanMove", "CanRot" };

            EditorGUILayout.Space(2);
            Rect row = EditorGUILayout.GetControlRect(false, 24f);
            row.xMin += EditorGUI.indentLevel * 15;
            float bw = Mathf.Min(row.width / props.Length, 90);

            for (int i = 0; i < props.Length; i++)
            {
                var prop = pSer.FindProperty(props[i]);
                if (prop == null) continue;

                Rect cell = new Rect(row.x + i * (bw + 2), row.y, bw, 22);
                bool val = prop.boolValue;
                Color bg = val ? new Color(0.2f, 0.55f, 0.25f, 0.85f)
                               : new Color(0.3f, 0.3f, 0.3f, 0.55f);
                EditorGUI.DrawRect(cell, bg);
                EditorGUI.LabelField(
                    new Rect(cell.x + 3, cell.y + 4, cell.width - 6, 14),
                    (val ? "● " : "○ ") + labels[i], EditorStyles.miniLabel);

                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                    prop.boolValue = !prop.boolValue;
            }
            EditorGUILayout.Space(2);
        }

        // =====================================================================
        // 탭 ② — 카메라
        // =====================================================================
        private void DrawCameraTab()
        {
            var c = SelectedChar;

            // ── SO 연결 매트릭스 (한눈에 보기) ──────────────────────────
            if (BeginFoldout($"{c.name}/cam/matrix", "📊 카메라 SO 연결 현황", true))
                DrawCameraConnectionMatrix(c);
            EndFoldout();

            // ── PlayerEventManager ────────────────────────────────────────
            var pem = c.GetComponent<PlayerEventManager>();
            if (pem != null &&
                BeginFoldout($"{c.name}/cam/pem",
                    "📷 PlayerEventManager — 카메라 SO", true))
            {
                var ser = new SerializedObject(pem);
                ser.Update();

                DrawSOGroup(ser, new[]
                {
                    ("lightShakeStance",          "Light Shake Stance",      false),
                    ("heavyShakeStance",          "Heavy Shake Stance",       true),
                    ("rollShakeStance",           "Roll Shake Stance",        false),
                    ("fumblePenaltySequence",     "Fumble Penalty Seq",       false),
                    ("hitVignetteSequence",       "Hit Vignette Seq",         true),
                    ("footstepKickSequence",      "Footstep Kick Seq",        false),
                    ("hitConfirmedOverlaySO",     "Hit Confirmed SO",         false),
                    ("hitConfirmedHeavyOverlaySO","Hit Confirmed Heavy SO",   false),
                });

                if (ser.ApplyModifiedProperties()) MarkDirty(pem);
            }
            EndFoldout();

            // ── WorldCameraManager ────────────────────────────────────────
#if UNITY_2023_1_OR_NEWER
            var wcm = FindObjectsByType<WorldCameraManager>(FindObjectsSortMode.None)
                          .FirstOrDefault();
#else
            var wcm = FindObjectOfType<WorldCameraManager>();
#endif
            if (wcm != null &&
                BeginFoldout($"{c.name}/cam/wcm", "🌍 WorldCameraManager", true))
            {
                var ser = new SerializedObject(wcm);
                ser.Update();
                SOFieldRow(ser, ser.FindProperty("defaultRestStance"),
                    "Default Rest Stance", warnIfNull: true);
                SOFieldRow(ser, ser.FindProperty("localPlayerCamera"),
                    "Local Player Camera");

                if (ser.ApplyModifiedProperties()) MarkDirty(wcm);

                // 런타임 FOV 게이지
                if (Application.isPlaying)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("── 런타임 상태 ──",
                        EditorStyles.centeredGreyMiniLabel);
                    RuntimeSORow("현재 Seq", wcm.currentSequenceSO);
                    RuntimeSORow("현재 Stance", wcm.currentStanceSO);

                    DrawStatBar("FOV", wcm.currentFOV, 120f, new Color(0.3f, 0.7f, 1f));
                    DrawStatBar("Overlay FOV Δ",
                        Mathf.Abs(wcm.overlayFOVDelta), 30f,
                        new Color(1f, 0.65f, 0.2f));
                }
            }
            EndFoldout();

            // ── Lock-On ───────────────────────────────────────────────────
            var pcm = c.GetComponent<PlayerCombatManager>();
            if (pcm != null &&
                BeginFoldout($"{c.name}/cam/lockon", "🔒 Lock-On 카메라 SO", false))
            {
                var ser = new SerializedObject(pcm);
                ser.Update();
                SOFieldRow(ser, ser.FindProperty("lockOnSequenceSO"),
                    "LockOn Seq SO", warnIfNull: true);
                SOFieldRow(ser, ser.FindProperty("lockOnStanceSO"),
                    "LockOn Stance SO");
                if (ser.ApplyModifiedProperties()) MarkDirty(pcm);
            }
            EndFoldout();

            // ── 액션별 CameraSeq 연결 현황 ───────────────────────────────
            var animMgr = c.GetComponent<CharacterAnimationManager>();
            var animSer = animMgr != null ? new SerializedObject(animMgr) : null;
            var setSOObj = animSer?.FindProperty("baseAnimationSet")
                               ?.objectReferenceValue as CharacterAnimationSetSO;

            if (setSOObj != null &&
                BeginFoldout($"{c.name}/cam/actionseq",
                    "📋 액션별 CameraSeq 연결", false))
            {
                foreach (var m in setSOObj.animationMappings)
                {
                    if (m.paramsSO == null) continue;
                    bool hasSeq = m.paramsSO.cameraSequence != null;

                    Rect row = EditorGUILayout.GetControlRect(false, 20f);
                    EditorGUI.DrawRect(row, hasSeq
                        ? new Color(0.12f, 0.3f, 0.15f, 0.5f)
                        : new Color(0.35f, 0.15f, 0.1f, 0.5f));

                    EditorGUI.LabelField(
                        new Rect(row.x + 4, row.y + 3, 18, 14),
                        hasSeq ? "✅" : "⚠");
                    EditorGUI.LabelField(
                        new Rect(row.x + 22, row.y + 3, 160, 14),
                        m.logicalStateName, EditorStyles.miniLabel);

                    string seqLabel = hasSeq
                        ? m.paramsSO.cameraSequence.name : "— 미연결";
                    GUI.color = hasSeq ? Color.white : new Color(1f, 0.6f, 0.3f);
                    if (GUI.Button(
                        new Rect(row.x + 184, row.y + 2, row.width - 190, 16),
                        seqLabel, EditorStyles.miniLabel) &&
                        m.paramsSO.cameraSequence != null)
                    {
                        EditorGUIUtility.PingObject(m.paramsSO.cameraSequence);
                    }
                    GUI.color = Color.white;
                }
            }
            EndFoldout();
        }

        // 카메라 SO 연결 매트릭스
        private void DrawCameraConnectionMatrix(CharacterManager c)
        {
            var pem = c.GetComponent<PlayerEventManager>();
            var items = new List<(string name, bool connected)>();

            if (pem != null)
            {
                var s = new SerializedObject(pem);
                items.Add(("HeavyShake", s.FindProperty("heavyShakeStance")?.objectReferenceValue != null));
                items.Add(("HitVignette", s.FindProperty("hitVignetteSequence")?.objectReferenceValue != null));
                items.Add(("RollShake", s.FindProperty("rollShakeStance")?.objectReferenceValue != null));
                items.Add(("FumblePenalty", s.FindProperty("fumblePenaltySequence")?.objectReferenceValue != null));
                items.Add(("HitConfirmed", s.FindProperty("hitConfirmedOverlaySO")?.objectReferenceValue != null));
            }

            var pcm = c.GetComponent<PlayerCombatManager>();
            if (pcm != null)
            {
                var s = new SerializedObject(pcm);
                items.Add(("LockOn Seq", s.FindProperty("lockOnSequenceSO")?.objectReferenceValue != null));
                items.Add(("LockOn Stance", s.FindProperty("lockOnStanceSO")?.objectReferenceValue != null));
            }

            DrawConnectionMatrix(items, 4, 110);
        }

        // =====================================================================
        // 탭 ③ — 이펙트
        // =====================================================================
        private void DrawEffectsTab()
        {
            var c = SelectedChar;

            // ── ObjectMotionBlurController ────────────────────────────────
            var blurCtrl = FindBlurController(c);
            if (blurCtrl != null &&
                BeginFoldout($"{c.name}/fx/blur",
                    "💨 ObjectMotionBlurController", true))
            {
                var ser = new SerializedObject(blurCtrl);
                ser.Update();

                SOFieldRow(ser, ser.FindProperty("blurEventResponse"),
                    "Blur Event Response", warnIfNull: true);
                SOFieldRow(ser, ser.FindProperty("motionBlurVolume"),
                    "Motion Blur Volume");
                SOFieldRow(ser, ser.FindProperty("baseBlurIntensity"), "Base Intensity");
                SOFieldRow(ser, ser.FindProperty("maxBlurLengthOverride"), "Max Length");

                // 런타임 Blur 게이지
                if (Application.isPlaying)
                {
                    var intensityProp = ser.FindProperty("currentBlurIntensity")
                                    ?? ser.FindProperty("baseBlurIntensity");
                    if (intensityProp != null)
                        DrawStatBar("Current Blur",
                            intensityProp.floatValue, 1f,
                            new Color(0.5f, 0.3f, 0.9f));
                }

                // ── BlurEventResponseSO Pulse 차트 ─────────────────────
                var blurResProp = ser.FindProperty("blurEventResponse");
                var blurResSO = blurResProp?.objectReferenceValue as ScriptableObject;
                if (blurResSO != null)
                {
                    string pulseKey = $"{c.name}/fx/blur/pulses";
                    if (BeginFoldout(pulseKey, "📈 Pulse Definitions (차트)", false,
                        indentOverride: 1))
                    {
                        DrawBlurPulseChart(blurResSO);
                    }
                    EndFoldout();
                }

                if (ser.ApplyModifiedProperties()) MarkDirty(blurCtrl);
            }
            else if (blurCtrl == null)
            {
                WarnBox("ObjectMotionBlurController 없음.\n캐릭터 또는 카메라 오브젝트에 추가하세요.");
            }
            EndFoldout();

            // ── CharacterEffectsManager ────────────────────────────────────
            var effMgr = c.GetComponent<CharacterEffectsManager>();
            if (effMgr != null &&
                BeginFoldout($"{c.name}/fx/vfx", "✨ VFX / Effect Mappings", false))
            {
                var ser = new SerializedObject(effMgr);
                ser.Update();
                SOFieldRow(ser, ser.FindProperty("bloodSplatterVFX"),
                    "Blood Splatter VFX", warnIfNull: true);
                SOFieldRow(ser, ser.FindProperty("hitsparkVFX"),
                    "HitSpark VFX", warnIfNull: true);

                string emKey = $"{c.name}/fx/vfx/mappings";
                if (BeginFoldout(emKey,
                    $"Effect Mappings [{effMgr.effectMappings.Count}]", false,
                    indentOverride: 1))
                {
                    var mappings = ser.FindProperty("effectMappings");
                    if (mappings != null)
                        EditorGUILayout.PropertyField(mappings,
                            new GUIContent("Effect Mappings"), true);
                }
                EndFoldout();

                if (ser.ApplyModifiedProperties()) MarkDirty(effMgr);
            }
            EndFoldout();

            // ── WorldSoundFXManager ────────────────────────────────────────
#if UNITY_2023_1_OR_NEWER
            var sfxMgr = FindObjectsByType<WorldSoundFXManager>(FindObjectsSortMode.None)
                             .FirstOrDefault();
#else
            var sfxMgr = FindObjectOfType<WorldSoundFXManager>();
#endif
            if (sfxMgr != null &&
                BeginFoldout($"{c.name}/fx/sfx", "🔊 WorldSoundFXManager", false))
            {
                var ser = new SerializedObject(sfxMgr);
                ser.Update();

                var sfxArr = ser.FindProperty("physicalDamageSFX");
                if (sfxArr != null)
                {
                    // 배열 크기 게이지
                    DrawStatBar("Physical Damage SFX 수",
                        sfxArr.arraySize, 20f, new Color(0.3f, 0.8f, 0.5f));
                    if (sfxArr.arraySize == 0)
                        WarnLabel("physicalDamageSFX 배열 비어있음 → 피격음 없음");

                    string sfxKey = $"{c.name}/fx/sfx/arr";
                    if (BeginFoldout(sfxKey,
                        $"Physical Damage SFX [{sfxArr.arraySize}]", false,
                        indentOverride: 1))
                        EditorGUILayout.PropertyField(sfxArr,
                            new GUIContent("Physical Damage SFX"), true);
                    EndFoldout();
                }

                var rollSFX = ser.FindProperty("rollSFX");
                if (rollSFX != null) SOFieldRow(ser, rollSFX, "Roll SFX");
                if (ser.ApplyModifiedProperties()) MarkDirty(sfxMgr);
            }
            EndFoldout();
        }

        // Blur Pulse 차트 시각화
        private void DrawBlurPulseChart(ScriptableObject blurResSO)
        {
            var brSer = new SerializedObject(blurResSO);
            var pulses = brSer.FindProperty("pulseDefinitions");
            if (pulses == null || pulses.arraySize == 0)
            { WarnLabel("Pulse 정의 없음"); return; }

            const float barH = 18f, maxBarW = 200f;
            EditorGUILayout.Space(2);

            for (int i = 0; i < pulses.arraySize; i++)
            {
                var el = pulses.GetArrayElementAtIndex(i);
                var eProp = el.FindPropertyRelative("eventType");
                var sProp = el.FindPropertyRelative("strength");
                var dProp = el.FindPropertyRelative("decayTime");
                if (eProp == null) continue;

                float strength = sProp?.floatValue ?? 0f;
                float decay = dProp?.floatValue ?? 0f;
                string evtName = eProp.enumDisplayNames.Length > eProp.enumValueIndex
                    ? eProp.enumDisplayNames[eProp.enumValueIndex] : "?";

                Rect rowRect = EditorGUILayout.GetControlRect(false, barH + 2f);
                rowRect.xMin += 10;

                // 이벤트 이름
                EditorGUI.LabelField(
                    new Rect(rowRect.x, rowRect.y + 2, 160, barH),
                    evtName, EditorStyles.miniLabel);

                // Strength 바
                Rect barBg = new Rect(rowRect.x + 162, rowRect.y + 3, maxBarW, barH - 4);
                EditorGUI.DrawRect(barBg, new Color(0.15f, 0.15f, 0.15f));
                EditorGUI.DrawRect(
                    new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(strength), barBg.height),
                    Color.Lerp(new Color(0.2f, 0.6f, 1f), new Color(1f, 0.3f, 0.2f), strength));

                EditorGUI.LabelField(
                    new Rect(barBg.xMax + 4, rowRect.y + 2, 80, barH),
                    $"str:{strength:F2}  d:{decay:F2}s", EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(4);
        }

        // =====================================================================
        // 탭 ④ — 무기
        // =====================================================================
        private void DrawWeaponTab()
        {
            var c = SelectedChar;
            var invMgr = c.GetComponent<PlayerInventoryManager>();

            if (invMgr == null)
            {
                EditorGUILayout.HelpBox("PlayerInventoryManager 없음 (AI 캐릭터).",
                    MessageType.Info);

                var aiCom = c.GetComponent<AICharacterCombatManager>();
                if (aiCom != null &&
                    BeginFoldout($"{c.name}/wep/ai", "⚔ AI Combat Manager", true))
                {
                    var ser = new SerializedObject(aiCom);
                    ser.Update();
                    DrawAllProps(ser, aiCom);
                }
                EndFoldout();
                return;
            }

            var invSer = new SerializedObject(invMgr);
            invSer.Update();

            // ── 오른손 무기 ───────────────────────────────────────────────
            var rProp = invSer.FindProperty("currentRightHandWeapon");
            if (BeginFoldout($"{c.name}/wep/right", "⚔ 오른손 무기", true))
            {
                SOFieldRow(invSer, rProp, "Right Hand Weapon");
                DrawWeaponDetail(c.name, "right",
                    rProp?.objectReferenceValue as WeaponItem);
            }
            EndFoldout();

            // ── 왼손 무기 ─────────────────────────────────────────────────
            var lProp = invSer.FindProperty("currentLeftHandWeapon");
            if (BeginFoldout($"{c.name}/wep/left", "🛡 왼손 무기", true))
            {
                SOFieldRow(invSer, lProp, "Left Hand Weapon");
                DrawWeaponDetail(c.name, "left",
                    lProp?.objectReferenceValue as WeaponItem);
            }
            EndFoldout();

            if (invSer.ApplyModifiedProperties()) MarkDirty(invMgr);
        }

        private void DrawWeaponDetail(string charName, string slot, WeaponItem w)
        {
            if (w == null)
            {
                EditorGUILayout.LabelField("  — 장착 없음", EditorStyles.miniLabel);
                return;
            }

            var ser = new SerializedObject(w);
            ser.Update();

            // ── 스탯 바 (시각화) ──────────────────────────────────────────
            string statKey = $"{charName}/wep/{slot}/stats";
            if (BeginFoldout(statKey, "📊 스탯", true, indentOverride: 1))
            {
                var physProp = ser.FindProperty("physicalDamage");
                var poiseProp = ser.FindProperty("poiseDamage");
                var stamProp = ser.FindProperty("baseStaminaCost");

                if (physProp != null) DrawStatBar("Physical Dmg",
                    physProp.floatValue, 500f, new Color(1f, 0.45f, 0.2f));
                if (poiseProp != null) DrawStatBar("Poise Dmg",
                    poiseProp.floatValue, 200f, new Color(0.6f, 0.3f, 1f));
                if (stamProp != null) DrawStatBar("Stamina Cost",
                    stamProp.floatValue, 100f, new Color(0.25f, 0.75f, 0.45f));

                // 직접 편집 슬롯
                EditorGUILayout.Space(4);
                SOFieldRow(ser, physProp, "Physical Dmg");
                SOFieldRow(ser, poiseProp, "Poise Dmg");
                SOFieldRow(ser, stamProp, "Stamina Cost");
            }
            EndFoldout();

            // ── 액션 체인 ─────────────────────────────────────────────────
            string actionKey = $"{charName}/wep/{slot}/actions";
            if (BeginFoldout(actionKey, "🔗 액션 체인", false, indentOverride: 1))
            {
                DrawWeaponActionChain(ser, "oh_RB_Action", "RB Action");
                DrawWeaponActionChain(ser, "oh_RT_Action", "RT Action");
            }
            EndFoldout();

            if (ser.ApplyModifiedProperties()) MarkDirty(w);
        }

        private void DrawWeaponActionChain(SerializedObject wSer,
            string propName, string label)
        {
            var prop = wSer.FindProperty(propName);
            if (prop != null) SOFieldRow(wSer, prop, label);
        }

        // =====================================================================
        // 공통 SO 그룹 렌더 헬퍼
        // =====================================================================
        private void DrawSOGroup(SerializedObject ser,
            (string prop, string label, bool warn)[] fields)
        {
            foreach (var (prop, label, warn) in fields)
                SOFieldRow(ser, ser.FindProperty(prop), label, warnIfNull: warn);
        }

        // 런타임 SO 행
        private void RuntimeSORow(string label, UnityEngine.Object obj)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90));
                string n = obj != null ? obj.name : "None";
                EditorGUILayout.LabelField(n, EditorStyles.boldLabel);
                if (obj != null && GUILayout.Button("↗", GUILayout.Width(24)))
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }
        }

        // 전체 프로퍼티 표시 (AI Combat 등)
        private void DrawAllProps(SerializedObject ser, UnityEngine.Object target)
        {
            var it = ser.GetIterator();
            it.NextVisible(true);
            while (it.NextVisible(false))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(it, true, GUILayout.ExpandWidth(true));
                    if (it.propertyType == SerializedPropertyType.ObjectReference &&
                        it.objectReferenceValue != null)
                    {
                        if (GUILayout.Button("↗", GUILayout.Width(24)))
                        {
                            EditorGUIUtility.PingObject(it.objectReferenceValue);
                            Selection.activeObject = it.objectReferenceValue;
                        }
                    }
                }
            }
            if (ser.ApplyModifiedProperties()) MarkDirty(target);
        }
    }
}
#endif