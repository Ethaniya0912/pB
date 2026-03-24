#if UNITY_EDITOR
// ============================================================
// TDA Camera SO Wizard  v2.0
// 파일 경로: Assets/Scripts/Camera/Editor/TDA_CameraSOWizard.cs
//
// 신규 기능 (v2):
//   ① 프리셋 데이터 에디터 — 버튼·콤보박스·슬라이더 옵션 데이터를 직접 보고 수정
//   ② 커스텀 Q&A 빌더   — 파라미터 선택 + 질문 + 선택지를 직접 추가·편집·삭제
//   ③ 변경 전후 시각화  — 모든 파라미터(float 그래프 포함)의 변경 전/후 색상 구별
// ============================================================
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TDA.Cameras;

namespace TDA.Cameras.Editor
{
    // ══════════════════════════════════════════════════════════════
    // 데이터 모델
    // ══════════════════════════════════════════════════════════════

    public enum QAWidgetType { RadioButtons, Dropdown, Toggle, Slider, IntSlider }

    [Serializable]
    public class QAOption
    {
        public string label = "옵션";
        public string description = "";
    }

    [Serializable]
    public class QAParamMapping
    {
        public string fieldPath = "";
        public float floatValue = 0f;
        public bool boolValue = false;
        public int intValue = 0;
    }

    [Serializable]
    public class CustomQAItem
    {
        public string question = "새 질문";
        public QAWidgetType widgetType = QAWidgetType.RadioButtons;
        public List<QAOption> options = new List<QAOption> { new QAOption { label = "옵션 A" }, new QAOption { label = "옵션 B" } };
        public float sliderMin = 0f;
        public float sliderMax = 1f;
        public List<QAParamMapping> mappings = new List<QAParamMapping>();
        [NonSerialized] public int selectedIndex = 0;
        [NonSerialized] public float sliderValue = 0f;
        [NonSerialized] public bool toggleValue = false;
    }

    // ══════════════════════════════════════════════════════════════
    // 메인 에디터 창
    // ══════════════════════════════════════════════════════════════
    public class TDA_CameraSOWizard : EditorWindow
    {
        // ── 색 ────────────────────────────────────────────────────
        static readonly Color C_BG = new Color(0.11f, 0.11f, 0.13f);
        static readonly Color C_PANEL = new Color(0.16f, 0.16f, 0.19f);
        static readonly Color C_CARD = new Color(0.20f, 0.20f, 0.24f);
        static readonly Color C_BORDER = new Color(0.28f, 0.28f, 0.35f);
        static readonly Color C_ACCENT = new Color(0.28f, 0.62f, 1.00f);
        static readonly Color C_ACCENT2 = new Color(0.52f, 0.36f, 0.95f);
        static readonly Color C_OK = new Color(0.28f, 0.82f, 0.48f);
        static readonly Color C_WARN = new Color(1.00f, 0.70f, 0.18f);
        static readonly Color C_DANGER = new Color(0.92f, 0.32f, 0.32f);
        static readonly Color C_TH = new Color(0.94f, 0.94f, 0.99f);
        static readonly Color C_TN = new Color(0.72f, 0.72f, 0.78f);
        static readonly Color C_TD = new Color(0.48f, 0.48f, 0.54f);
        static readonly Color C_GBAR_OLD = new Color(0.38f, 0.38f, 0.52f);
        static readonly Color C_GBAR_NEW = new Color(0.28f, 0.82f, 0.48f);
        static readonly Color C_GBAR_HI = new Color(1.00f, 0.70f, 0.18f, 0.22f);

        // ── 탭 ────────────────────────────────────────────────────
        enum Tab { QA, PresetEditor, CustomQA }
        Tab activeTab = Tab.QA;

        // ── SO ────────────────────────────────────────────────────
        ScriptableObject targetSO;
        bool isStanceSO, isSeqSO;
        SerializedObject serializedSO;

        // ── Stance Q&A 기본 ───────────────────────────────────────
        enum UsageCtx { Explore, Combat_Human, Combat_Boss, Cutscene, UI, HitReaction, Custom }
        enum CamPos { Center, Left, Right, OverHead, LowAngle }
        enum TensionLv { Calm, Normal, Tense, Intense, Extreme }
        enum ShakeSt { None, Subtle, Medium, Heavy, Quake }

        UsageCtx qa_usage = UsageCtx.Combat_Human;
        CamPos qa_pos = CamPos.Right;
        TensionLv qa_tension = TensionLv.Normal;
        ShakeSt qa_shake = ShakeSt.None;
        bool qa_lockOn = false;
        bool qa_edge = false;
        bool qa_dynFrm = false;
        bool qa_handheld = true;
        float qa_dist = 2.5f;
        bool qa_multi = false;

        // ── Sequence Q&A 기본 ─────────────────────────────────────
        enum SeqPurpose { LightAtk, HeavyAtk, LockOn, Parry, GuardBreak, Hit, Execution, Custom }
        enum SeqInterrupt { Never, ByInput, ByDamage, Always }
        enum SeqRestore { None, Angle, Stance, Both }

        SeqPurpose qa_seqPurpose = SeqPurpose.LightAtk;
        SeqInterrupt qa_seqInterrupt = SeqInterrupt.ByDamage;
        SeqRestore qa_seqRestore = SeqRestore.Angle;
        int qa_seqSteps = 1;
        float qa_seqBlend = 0.08f;
        float qa_seqHold = 0.3f;
        float qa_seqDamp = 0.12f;

        // ── 계산된 미리보기 ───────────────────────────────────────
        float pv_fov, pv_zTilt, pv_baseYaw;
        Vector3 pv_offset;
        bool pv_dynFrm, pv_shake, pv_edge, pv_lockPenalty;
        float pv_shakeInt;
        bool pv_intInput, pv_intDmg, pv_restAngle, pv_restStance;
        float pv_restSpeed, pv_restBlend;

        // ── 커스텀 Q&A ────────────────────────────────────────────
        List<CustomQAItem> customQAs = new List<CustomQAItem>();
        int cqa_editing = -1;

        // ── 프리셋 에디터 foldout ─────────────────────────────────
        bool pe_lens = true, pe_dyn = true, pe_shake = true, pe_lock = true, pe_multi = true;
        bool pe_seqRule = true, pe_seqStep = true;

        // ── UI ────────────────────────────────────────────────────
        Vector2 scrollL, scrollR;
        bool pendingApply = false;
        string lastSave = "";
        bool stylesBuilt = false;

        // ── Styles ────────────────────────────────────────────────
        GUIStyle sH1, sH2, sH3, sBody, sHint, sBtnApply, sBtnTab, sBtnTabSel, sBtnSm, sPVL, sPVV, sBadge;

        // ═══════════════════════════════════════════════════════════
        #region 진입점
        // ═══════════════════════════════════════════════════════════

        [MenuItem("Assets/[TDA] Show in Camera SO Settings", false, 21)]
        static void OpenMenu()
        {
            var sel = Selection.activeObject as ScriptableObject;
            if (sel is CameraStancePresetSO || sel is CameraSequencePresetSO) OpenWith(sel);
            else EditorUtility.DisplayDialog("[TDA]", "CameraStancePresetSO 또는 CameraSequencePresetSO를 선택하세요.", "확인");
        }

        [MenuItem("Assets/[TDA] Show in Camera SO Settings", true)]
        static bool ValidMenu() =>
            Selection.activeObject is CameraStancePresetSO || Selection.activeObject is CameraSequencePresetSO;

        public static void OpenWith(ScriptableObject so)
        {
            var w = GetWindow<TDA_CameraSOWizard>(false, "📷 TDA Camera SO Settings");
            w.minSize = new Vector2(920, 700);
            w.Load(so);
            w.Show();
        }

        void Load(ScriptableObject so)
        {
            targetSO = so;
            isStanceSO = so is CameraStancePresetSO;
            isSeqSO = so is CameraSequencePresetSO;
            serializedSO = new SerializedObject(so);
            stylesBuilt = false;
            pendingApply = false;
            if (isStanceSO) InferStance((CameraStancePresetSO)so);
            else InferSeq((CameraSequencePresetSO)so);
            Recalc();
            Repaint();
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region OnGUI
        // ═══════════════════════════════════════════════════════════

        void OnGUI()
        {
            BuildStyles();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);
            if (targetSO == null) { DrawEmpty(); return; }
            if (serializedSO != null) serializedSO.Update();
            DrawTopBar();
            DrawTabs();
            DrawBody();
        }

        void DrawEmpty()
        {
            GUI.Label(new Rect(0, 0, position.width, position.height),
                "Project 창에서 CameraStanceSO 또는 CameraSequenceSO\n우클릭 → [TDA] Show in Camera SO Settings",
                new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawTopBar()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 50), C_PANEL);
            EditorGUI.DrawRect(new Rect(0, 49, position.width, 1), C_BORDER);

            string badge = isStanceSO ? "STANCE" : "SEQUENCE";
            Color bc = isStanceSO ? C_ACCENT : C_ACCENT2;
            Rect br = new Rect(10, 14, 72, 22);
            EditorGUI.DrawRect(br, bc * 0.75f);
            GUI.Label(br, badge, sBadge);

            GUI.Label(new Rect(90, 12, position.width - 260, 26), targetSO.name, sH1);

            GUI.backgroundColor = pendingApply ? C_OK : new Color(0.22f, 0.22f, 0.28f);
            if (GUI.Button(new Rect(position.width - 132, 11, 120, 28),
                           pendingApply ? "★ Apply" : "Apply", sBtnApply))
                DoApply();
            GUI.backgroundColor = Color.white;

            if (!string.IsNullOrEmpty(lastSave))
                GUI.Label(new Rect(position.width - 132, 39, 120, 12), lastSave, sHint);
        }

        void DrawTabs()
        {
            EditorGUI.DrawRect(new Rect(0, 50, position.width, 30), C_PANEL);
            EditorGUI.DrawRect(new Rect(0, 79, position.width, 1), C_BORDER);
            float tx = 10;
            DrawTabBtn(ref tx, "🎯 질의응답", Tab.QA);
            DrawTabBtn(ref tx, "🔧 프리셋 에디터", Tab.PresetEditor);
            DrawTabBtn(ref tx, "✏️ 커스텀 Q&A", Tab.CustomQA);
        }

        void DrawTabBtn(ref float x, string lbl, Tab t)
        {
            bool sel = activeTab == t;
            if (GUI.Button(new Rect(x, 54, 148, 24), lbl, sel ? sBtnTabSel : sBtnTab))
                activeTab = t;
            if (sel) EditorGUI.DrawRect(new Rect(x, 78, 148, 2), C_ACCENT);
            x += 152;
        }

        void DrawBody()
        {
            Rect body = new Rect(0, 80, position.width, position.height - 80);
            switch (activeTab)
            {
                case Tab.QA: DrawQATab(body); break;
                case Tab.PresetEditor: DrawPresetTab(body); break;
                case Tab.CustomQA: DrawCustomQATab(body); break;
            }
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region 탭 1 — 질의응답 + 미리보기
        // ═══════════════════════════════════════════════════════════

        void DrawQATab(Rect r)
        {
            float split = r.width * 0.52f;
            GUILayout.BeginArea(new Rect(r.x, r.y, split - 1, r.height));
            scrollL = GUILayout.BeginScrollView(scrollL, false, true);
            GUILayout.Space(10);
            DrawSecH("🎯  질의응답 설정", C_ACCENT);
            GUILayout.Space(5);
            EditorGUILayout.HelpBox("질문에 답하면 오른쪽 미리보기가 갱신됩니다. Apply로 SO에 반영하세요.", MessageType.Info);
            GUILayout.Space(8);

            bool ch = false;
            if (isStanceSO) ch = DrawStanceQA();
            else ch = DrawSeqQA();

            if (customQAs.Count > 0)
            {
                GUILayout.Space(10);
                DrawSecH("✏️  커스텀 질의응답", C_ACCENT2);
                GUILayout.Space(4);
                foreach (var qa in customQAs) ch |= RunCustomQA(qa);
            }

            if (ch) { Recalc(); pendingApply = true; Repaint(); }

            GUILayout.Space(24);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            EditorGUI.DrawRect(new Rect(r.x + split - 1, r.y, 1, r.height), C_BORDER);

            GUILayout.BeginArea(new Rect(r.x + split, r.y, r.width - split, r.height));
            scrollR = GUILayout.BeginScrollView(scrollR, false, true);
            GUILayout.Space(10);
            DrawSecH("📊  변경 전 / 후 비교", C_ACCENT2);
            GUILayout.Space(4);
            DrawChangeLegend();
            GUILayout.Space(6);
            if (isStanceSO) DrawStancePreview();
            else DrawSeqPreview();
            GUILayout.Space(24);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── Stance Q&A ───────────────────────────────────────────
        bool DrawStanceQA()
        {
            bool ch = false;
            QCard("Q1. 이 스탠스는 어떤 상황에서 사용됩니까?", () => {
                var n = Radio(qa_usage, new[] { "탐험/이동", "전투(인간형)", "전투(보스)", "컷씬/처형", "UI/인벤토리", "피격반응", "커스텀" });
                if (!n.Equals(qa_usage)) { qa_usage = n; ch = true; }
            });
            QCard("Q2. 카메라 위치는?", () => {
                var n = Radio(qa_pos, new[] { "정중앙", "좌측 숄더", "우측 숄더", "위에서", "낮은각도" });
                if (!n.Equals(qa_pos)) { qa_pos = n; ch = true; }
            });
            QCard("Q3. 긴장감 & 카메라 거리", () => {
                var n = Dropdown("긴장감", qa_tension, new[] { "🌿 평온", "😐 보통", "😤 긴장", "🔥 격렬", "💥 극한" });
                if (!n.Equals(qa_tension)) { qa_tension = n; ch = true; }
                float nd = Slider("카메라 거리(m)", qa_dist, 1f, 6f);
                if (Mathf.Abs(nd - qa_dist) > 0.001f) { qa_dist = nd; ch = true; }
            });
            QCard("Q4. 진입 시 흔들림?", () => {
                var n = Dropdown("흔들림", qa_shake, new[] { "없음", "미세", "보통", "강함", "지진" });
                if (!n.Equals(qa_shake)) { qa_shake = n; ch = true; }
            });
            QCard("Q5. 락온 모드 & 옵션?", () => {
                bool nl = Toggle("락온 모드", qa_lockOn); if (nl != qa_lockOn) { qa_lockOn = nl; ch = true; }
                if (qa_lockOn)
                {
                    bool ne = Toggle("  └ 엣지 패닝", qa_edge); if (ne != qa_edge) { qa_edge = ne; ch = true; }
                    bool nm = Toggle("  └ 다중 타겟 프레이밍", qa_multi); if (nm != qa_multi) { qa_multi = nm; ch = true; }
                }
            });
            QCard("Q6. 횡이동 다이내믹 프레이밍?", () => {
                bool nd = Toggle("다이내믹 프레이밍", qa_dynFrm); if (nd != qa_dynFrm) { qa_dynFrm = nd; ch = true; }
            });
            QCard("Q7. 핸드헬드(바디캠) 효과?", () => {
                bool nh = Toggle("핸드헬드 효과", qa_handheld); if (nh != qa_handheld) { qa_handheld = nh; ch = true; }
            });
            return ch;
        }

        // ── Sequence Q&A ─────────────────────────────────────────
        bool DrawSeqQA()
        {
            bool ch = false;
            QCard("Q1. 시퀀스 용도는?", () => {
                var n = Dropdown("용도", qa_seqPurpose, new[] { "약공격", "강공격", "락온 진입", "패링", "가드브레이크", "피격", "처형·QTE", "커스텀" });
                if (!n.Equals(qa_seqPurpose)) { qa_seqPurpose = n; ch = true; }
            });
            QCard("Q2. 스텝 수 & 타이밍", () => {
                int ns = Mathf.RoundToInt(Slider("스텝 수", qa_seqSteps, 1, 5)); if (ns != qa_seqSteps) { qa_seqSteps = ns; ch = true; }
                float nb = Slider("블렌드 시간(s)", qa_seqBlend, 0.02f, 1f); if (Mathf.Abs(nb - qa_seqBlend) > 0.001f) { qa_seqBlend = nb; ch = true; }
                float nh = Slider("유지 시간(s)", qa_seqHold, 0.05f, 10f); if (Mathf.Abs(nh - qa_seqHold) > 0.001f) { qa_seqHold = nh; ch = true; }
                float nd = Slider("액션 댐핑", qa_seqDamp, 0.05f, 0.5f); if (Mathf.Abs(nd - qa_seqDamp) > 0.001f) { qa_seqDamp = nd; ch = true; }
            });
            QCard("Q3. 중단 정책은?", () => {
                var n = Dropdown("중단 정책", qa_seqInterrupt, new[] { "절대 없음", "입력으로", "피격으로", "항상 가능" });
                if (!n.Equals(qa_seqInterrupt)) { qa_seqInterrupt = n; ch = true; }
            });
            QCard("Q4. 종료 후 복귀 정책은?", () => {
                var n = Dropdown("복귀 정책", qa_seqRestore, new[] { "복귀 없음", "이전 각도로", "기본 스탠스로", "각도+스탠스" });
                if (!n.Equals(qa_seqRestore)) { qa_seqRestore = n; ch = true; }
            });
            return ch;
        }

        // ── 커스텀 Q&A 런타임 ─────────────────────────────────────
        bool RunCustomQA(CustomQAItem qa)
        {
            bool ch = false;
            QCard(qa.question, () => {
                switch (qa.widgetType)
                {
                    case QAWidgetType.RadioButtons:
                        {
                            var lbls = new string[qa.options.Count];
                            for (int i = 0; i < lbls.Length; i++) lbls[i] = qa.options[i].label;
                            int ni = RadioInt(qa.selectedIndex, lbls);
                            if (ni != qa.selectedIndex) { qa.selectedIndex = ni; ch = true; }
                            break;
                        }
                    case QAWidgetType.Dropdown:
                        {
                            var lbls = new string[qa.options.Count];
                            for (int i = 0; i < lbls.Length; i++) lbls[i] = qa.options[i].label;
                            GUILayout.BeginHorizontal(); GUILayout.Space(16);
                            int ni = EditorGUILayout.Popup(qa.selectedIndex, lbls);
                            GUILayout.EndHorizontal();
                            if (ni != qa.selectedIndex) { qa.selectedIndex = ni; ch = true; }
                            break;
                        }
                    case QAWidgetType.Slider:
                    case QAWidgetType.IntSlider:
                        {
                            float nv = Slider(qa.question, qa.sliderValue, qa.sliderMin, qa.sliderMax);
                            if (qa.widgetType == QAWidgetType.IntSlider) nv = Mathf.Round(nv);
                            if (Mathf.Abs(nv - qa.sliderValue) > 0.0001f) { qa.sliderValue = nv; ch = true; }
                            break;
                        }
                    case QAWidgetType.Toggle:
                        {
                            bool nt = Toggle(qa.options.Count > 0 ? qa.options[0].label : "ON", qa.toggleValue);
                            if (nt != qa.toggleValue) { qa.toggleValue = nt; ch = true; }
                            break;
                        }
                }
            });
            return ch;
        }

        // ── 범례 ─────────────────────────────────────────────────
        void DrawChangeLegend()
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(8);
            Dot(C_GBAR_OLD); GUILayout.Label(" 현재값", sPVL, GUILayout.Width(46));
            Dot(C_GBAR_NEW); GUILayout.Label(" 변경 후", sPVL, GUILayout.Width(50));
            Dot(C_WARN); GUILayout.Label(" 변경 있음", sPVL);
            GUILayout.EndHorizontal();
        }
        void Dot(Color c) { Rect r = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12)); r.y += 2; EditorGUI.DrawRect(r, c); }

        // ── Stance 미리보기 ───────────────────────────────────────
        void DrawStancePreview()
        {
            var so = targetSO as CameraStancePresetSO; if (so == null) return;
            PVSection("📐 렌즈 & 구도", () => {
                PVFloat("FOV", pv_fov, so.fov, 10, 120);
                PVFloat("Z Tilt", pv_zTilt, so.zTilt, -45, 45);
                PVFloat("Offset X", pv_offset.x, so.baseOffset.x, -3, 3);
                PVFloat("Offset Y", pv_offset.y, so.baseOffset.y, 0, 4);
                PVFloat("Offset Z", pv_offset.z, so.baseOffset.z, -8, -0.3f);
                PVFloat("Base Yaw", pv_baseYaw, so.baseYawOffset, -45, 45);
            });
            PVSection("🏃 다이내믹 프레이밍", () => {
                PVBool("사용", pv_dynFrm, so.useDynamicFraming);
                if (pv_dynFrm)
                {
                    PVFloat("Left Offset", so.dynamicFraming.leftStrafeMaxOffset, so.dynamicFraming.leftStrafeMaxOffset, -3, 0);
                    PVFloat("Right Offset", so.dynamicFraming.rightStrafeMaxOffset, so.dynamicFraming.rightStrafeMaxOffset, 0, 3);
                }
            });
            PVSection("💥 흔들림 & 핸드헬드", () => {
                PVBool("Enable Shake", pv_shake, so.impactShake.enableShake);
                if (pv_shake) PVFloat("Intensity", pv_shakeInt, so.impactShake.shakeIntensity, 0, 4);
                PVBool("핸드헬드", qa_handheld, so.handheldEffect.enableHandheldEffect);
            });
            PVSection("🔒 락온 & 엣지패닝", () => {
                PVBool("Edge Panning", pv_edge, so.edgePanningData.useEdgePanning);
                PVBool("Escape Penalty", pv_lockPenalty, so.lockOnPenaltyData.enableTargetEscapePenalty);
            });
        }

        // ── Sequence 미리보기 ─────────────────────────────────────
        void DrawSeqPreview()
        {
            var so = targetSO as CameraSequencePresetSO; if (so == null) return;
            PVSection("🚦 중단 정책", () => {
                PVBool("Interrupt By Input", pv_intInput, so.canBeInterruptedByInput);
                PVBool("Interrupt By Damage", pv_intDmg, so.canBeInterruptedByDamage);
            });
            PVSection("↩️ 복귀 정책", () => {
                PVBool("Restore Angle", pv_restAngle, so.restorePreviousAngle);
                PVBool("Restore Stance", pv_restStance, so.restoreToDefaultStanceOnFinish);
                if (pv_restAngle) PVFloat("Restore Speed", pv_restSpeed, so.restoreAngleSpeed, 0.5f, 15);
                PVFloat("Restore Blend", pv_restBlend, so.restoreBlendDuration, 0.05f, 5);
            });
            PVSection($"🎬 스텝 ({qa_seqSteps}단계)", () => {
                if (so.steps != null && so.steps.Count > 0)
                {
                    PVFloat("Blend Duration", pv_restBlend, so.steps[0].blendDuration, 0.02f, 2);
                    PVFloat("Hold Duration", qa_seqHold, so.steps[0].holdDuration, 0.05f, 12);
                    PVFloat("Action Damp", qa_seqDamp, so.steps[0].actionPositionDamping, 0.05f, 0.5f);
                }
            });
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region 탭 2 — 프리셋 에디터
        // ═══════════════════════════════════════════════════════════

        void DrawPresetTab(Rect r)
        {
            if (serializedSO == null) return;

            GUILayout.BeginArea(r);
            scrollL = GUILayout.BeginScrollView(scrollL, false, true);
            GUILayout.Space(10);
            DrawSecH("🔧  SO 파라미터 직접 편집", C_ACCENT);
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "슬라이더 아래 바 그래프:  회색=저장된 현재값  /  초록=편집 중인 값  /  노란 배경=변경 있음\n" +
                "Apply를 눌러 SO에 저장하세요.",
                MessageType.Info);
            GUILayout.Space(8);

            if (isStanceSO) DrawPresetStance();
            else DrawPresetSeq();

            GUILayout.Space(24);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (serializedSO.ApplyModifiedProperties())
            { pendingApply = true; Repaint(); }
        }

        // ── Stance 프리셋 에디터 ──────────────────────────────────
        void DrawPresetStance()
        {
            var so = targetSO as CameraStancePresetSO; if (so == null) return;
            pe_lens = Foldout(pe_lens, "📐 렌즈 & 구도", () => {
                PropSlider("fov", so.fov, 10, 120, "°");
                PropSlider("zTilt", so.zTilt, -45, 45, "°");
                PropSlider("baseYawOffset", so.baseYawOffset, -45, 45, "°");
                PropVec3("baseOffset", so.baseOffset);
            });
            pe_dyn = Foldout(pe_dyn, "🏃 다이내믹 프레이밍", () => {
                PropBool("useDynamicFraming", so.useDynamicFraming, "다이내믹 프레이밍 사용");
                PropBool("inheritDynamicFraming", so.inheritDynamicFraming, "이전 프레이밍 계승");
                PropSlider("dynamicFraming.leftStrafeMaxOffset", so.dynamicFraming.leftStrafeMaxOffset, -3, 0, "m");
                PropSlider("dynamicFraming.rightStrafeMaxOffset", so.dynamicFraming.rightStrafeMaxOffset, 0, 3, "m");
                PropSlider("dynamicFraming.leftStrafeYaw", so.dynamicFraming.leftStrafeYaw, -45, 0, "°");
                PropSlider("dynamicFraming.rightStrafeYaw", so.dynamicFraming.rightStrafeYaw, 0, 45, "°");
                PropSlider("dynamicFraming.leftFramingDelay", so.dynamicFraming.leftFramingDelay, 0.05f, 2, "s");
                PropSlider("dynamicFraming.rightFramingDelay", so.dynamicFraming.rightFramingDelay, 0.05f, 2, "s");
                PropSlider("dynamicFraming.centerReturnDelay", so.dynamicFraming.centerReturnDelay, 0.05f, 3, "s");
                PropSlider("dynamicFraming.dynamicYawWeight", so.dynamicFraming.dynamicYawWeight, 0, 1, "");
            });
            pe_shake = Foldout(pe_shake, "💥 흔들림 & 핸드헬드", () => {
                PropBool("impactShake.enableShake", so.impactShake.enableShake, "진동 사용");
                PropSlider("impactShake.shakeIntensity", so.impactShake.shakeIntensity, 0, 5, "");
                PropSlider("impactShake.shakeDelay", so.impactShake.shakeDelay, 0, 1, "s");
                PropSlider("impactShake.maxDuration", so.impactShake.maxDuration, 0, 1, "s");
                PropBool("handheldEffect.enableHandheldEffect", so.handheldEffect.enableHandheldEffect, "핸드헬드 효과");
                PropSlider("handheldEffect.swayAmount", so.handheldEffect.swayAmount, 0, 3, "");
                PropSlider("handheldEffect.swaySpeed", so.handheldEffect.swaySpeed, 0, 5, "");
                PropSlider("handheldEffect.bobbingAmount", so.handheldEffect.bobbingAmount, 0, 5, "");
            });
            pe_lock = Foldout(pe_lock, "🔒 락온 & 엣지패닝 & 이탈 페널티", () => {
                PropBool("edgePanningData.useEdgePanning", so.edgePanningData.useEdgePanning, "엣지 패닝");
                PropSlider("edgePanningData.edgePanThreshold", so.edgePanningData.edgePanThreshold, 0.01f, 0.5f, "");
                PropSlider("edgePanningData.lockOnInputDeadzone", so.edgePanningData.lockOnInputDeadzone, 0, 1, "");
                PropBool("lockOnPenaltyData.enableTargetEscapePenalty", so.lockOnPenaltyData.enableTargetEscapePenalty, "이탈 페널티");
                PropSlider("lockOnPenaltyData.targetEscapeViewportThreshold", so.lockOnPenaltyData.targetEscapeViewportThreshold, 0, 0.5f, "");
                PropSlider("lockOnPenaltyData.softRecoverySpeed", so.lockOnPenaltyData.softRecoverySpeed, 0.1f, 15, "");
                PropSlider("lockOnPenaltyData.escapePersistTime", so.lockOnPenaltyData.escapePersistTime, 0, 2, "s");
                PropSlider("lockOnPenaltyData.pitchReturnSpeed", so.lockOnPenaltyData.pitchReturnSpeed, 0, 5, "");
            });
            pe_multi = Foldout(pe_multi, "👥 다중 타겟 프레이밍", () => {
                PropSlider("multiTargetFraming.focusDetectionRadius", so.multiTargetFraming.focusDetectionRadius, 1, 50, "m");
                PropSlider("multiTargetFraming.proximityEnemyWeight", so.multiTargetFraming.proximityEnemyWeight, 0, 1, "");
                PropSlider("multiTargetFraming.focusDampTime", so.multiTargetFraming.focusDampTime, 0.05f, 3, "s");
                PropSlider("multiTargetFraming.staticTargetWeightScale", so.multiTargetFraming.staticTargetWeightScale, 0, 1, "");
            });
        }

        // ── Sequence 프리셋 에디터 ────────────────────────────────
        void DrawPresetSeq()
        {
            var so = targetSO as CameraSequencePresetSO; if (so == null) return;
            pe_seqRule = Foldout(pe_seqRule, "🚦 시퀀스 규칙", () => {
                PropBool("canBeInterruptedByInput", so.canBeInterruptedByInput, "입력으로 중단");
                PropBool("canBeInterruptedByDamage", so.canBeInterruptedByDamage, "피격으로 중단");
                PropBool("restoreToDefaultStanceOnFinish", so.restoreToDefaultStanceOnFinish, "기본 스탠스 복귀");
                PropSlider("restoreBlendDuration", so.restoreBlendDuration, 0.05f, 5, "s");
                PropBool("restorePreviousAngle", so.restorePreviousAngle, "이전 각도 복귀");
                PropSlider("restoreAngleSpeed", so.restoreAngleSpeed, 0.5f, 15, "");
            });
            pe_seqStep = Foldout(pe_seqStep, "🎬 스텝 목록", () => {
                if (so.steps == null || so.steps.Count == 0) { GUILayout.Label("스텝 없음", sPVL); return; }
                for (int i = 0; i < so.steps.Count; i++)
                {
                    GUILayout.Label($"── Step {i + 1} ──", sH3);
                    PropSlider($"steps.{i}.blendDuration", so.steps[i].blendDuration, 0.02f, 2, "s");
                    PropSlider($"steps.{i}.holdDuration", so.steps[i].holdDuration, 0.05f, 12, "s");
                    PropSlider($"steps.{i}.trackingStartTime", so.steps[i].trackingStartTime, 0, 1, "");
                    PropSlider($"steps.{i}.trackingEndTime", so.steps[i].trackingEndTime, 0, 1, "");
                    PropSlider($"steps.{i}.actionPositionDamping", so.steps[i].actionPositionDamping, 0.05f, 0.5f, "");
                    PropSlider($"steps.{i}.actionRotationDamping", so.steps[i].actionRotationDamping, 0.05f, 0.5f, "");
                    GUILayout.Space(6);
                }
            });
        }

        // ── 프리셋 에디터 컴포넌트 ───────────────────────────────

        bool Foldout(bool open, string title, System.Action body)
        {
            GUILayout.Space(3);
            GUILayout.BeginHorizontal(); GUILayout.Space(8);
            open = EditorGUILayout.Foldout(open, title, true,
                new GUIStyle(EditorStyles.foldoutHeader)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = C_ACCENT }
                });
            GUILayout.EndHorizontal();
            if (open)
            {
                GUILayout.BeginHorizontal(); GUILayout.Space(18);
                GUILayout.BeginVertical(); body?.Invoke();
                GUILayout.EndVertical(); GUILayout.Space(4); GUILayout.EndHorizontal();
            }
            GUILayout.Space(2);
            return open;
        }

        void PropSlider(string path, float curSO, float min, float max, string unit)
        {
            SerializedProperty sp = FindProp(path); if (sp == null) return;
            float live = sp.propertyType == SerializedPropertyType.Float ? sp.floatValue : sp.intValue;
            bool diff = Mathf.Abs(live - curSO) > 0.001f;

            GUILayout.BeginHorizontal();
            if (diff) EditorGUI.DrawRect(GUILayoutUtility.GetRect(3, 18), C_WARN);
            GUILayout.Label(sp.displayName, sPVL, GUILayout.Width(170));

            // 미니 바 그래프
            Rect gr = GUILayoutUtility.GetRect(90, 18);
            MiniBar(gr, min, max, curSO, live, diff);

            float nv = GUILayout.HorizontalSlider(live, min, max, GUILayout.Width(100));
            GUILayout.Label($"{nv:F2}{unit}", sPVL, GUILayout.Width(48));
            GUILayout.EndHorizontal();

            if (Mathf.Abs(nv - live) > 0.0001f)
            {
                if (sp.propertyType == SerializedPropertyType.Float) sp.floatValue = nv;
                else sp.intValue = Mathf.RoundToInt(nv);
                pendingApply = true;
            }
        }

        void PropBool(string path, bool curSO, string label)
        {
            SerializedProperty sp = FindProp(path); if (sp == null) return;
            bool live = sp.boolValue;
            bool diff = live != curSO;

            GUILayout.BeginHorizontal();
            if (diff) EditorGUI.DrawRect(GUILayoutUtility.GetRect(3, 16), C_WARN);
            bool nv = EditorGUILayout.Toggle(live, GUILayout.Width(18));
            GUILayout.Label(label, sPVL);
            if (diff)
                GUILayout.Label($"  ({(curSO ? "ON" : "OFF")}→{(live ? "ON" : "OFF")})",
                    new GUIStyle(sPVL) { normal = { textColor = C_WARN } });
            GUILayout.EndHorizontal();

            if (nv != live) { sp.boolValue = nv; pendingApply = true; }
        }

        void PropVec3(string path, Vector3 curSO)
        {
            SerializedProperty sp = FindProp(path); if (sp == null) return;
            GUILayout.BeginHorizontal(); GUILayout.Space(4);
            GUILayout.Label(sp.displayName, sPVL, GUILayout.Width(170));
            Vector3 nv = EditorGUILayout.Vector3Field("", sp.vector3Value);
            GUILayout.EndHorizontal();
            if (nv != sp.vector3Value) { sp.vector3Value = nv; pendingApply = true; }
        }

        SerializedProperty FindProp(string dotPath)
        {
            if (serializedSO == null) return null;
            string[] parts = dotPath.Split('.');
            string conv = dotPath;
            if (parts.Length >= 2 && int.TryParse(parts[1], out int idx))
                conv = $"{parts[0]}.Array.data[{idx}]"
                     + (parts.Length > 2 ? "." + string.Join(".", parts, 2, parts.Length - 2) : "");
            return serializedSO.FindProperty(conv);
        }

        void MiniBar(Rect r, float min, float max, float oldV, float newV, bool diff)
        {
            if (Event.current.type != EventType.Repaint) return;
            float range = Mathf.Max(max - min, 0.0001f);
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.13f));
            float or = Mathf.Clamp01((oldV - min) / range);
            EditorGUI.DrawRect(new Rect(r.x, r.y + 1, r.width * or, r.height * 0.45f), C_GBAR_OLD);
            float nr = Mathf.Clamp01((newV - min) / range);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height * 0.53f, r.width * nr, r.height * 0.44f), diff ? C_GBAR_NEW : C_GBAR_OLD);
            if (diff) EditorGUI.DrawRect(r, C_GBAR_HI);
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region 탭 3 — 커스텀 Q&A 빌더
        // ═══════════════════════════════════════════════════════════

        void DrawCustomQATab(Rect r)
        {
            GUILayout.BeginArea(r);
            scrollL = GUILayout.BeginScrollView(scrollL, false, true);
            GUILayout.Space(10);
            DrawSecH("✏️  커스텀 질의응답 빌더", C_ACCENT2);
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "파라미터 경로·질문·선택지를 추가하면 [질의응답] 탭 하단에 표시됩니다.\n" +
                "SO 필드 경로 예: \"fov\" / \"impactShake.shakeIntensity\" / \"dynamicFraming.leftStrafeYaw\"",
                MessageType.Info);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal(); GUILayout.Space(8);
            if (GUILayout.Button("＋  새 질의응답 추가", sBtnSm, GUILayout.Width(180)))
            { customQAs.Add(new CustomQAItem()); cqa_editing = customQAs.Count - 1; }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            for (int i = 0; i < customQAs.Count; i++) DrawCQARow(i);

            if (cqa_editing >= 0 && cqa_editing < customQAs.Count)
                DrawCQAEdit(customQAs[cqa_editing]);

            GUILayout.Space(24);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawCQARow(int idx)
        {
            var qa = customQAs[idx];
            bool sel = cqa_editing == idx;

            GUILayout.BeginHorizontal(); GUILayout.Space(8);
            Color bg = sel ? new Color(0.20f, 0.32f, 0.54f, 0.7f) : C_CARD;
            Rect row = GUILayoutUtility.GetRect(1, 28);
            row.x = 8; row.width = position.width - 24;
            EditorGUI.DrawRect(row, bg);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            { cqa_editing = sel ? -1 : idx; Event.current.Use(); Repaint(); }

            GUI.Label(new Rect(row.x + 8, row.y + 6, row.width - 90, 16),
                $"[{idx + 1}]  {qa.question}  ({qa.widgetType})", sPVL);

            if (GUI.Button(new Rect(row.xMax - 26, row.y + 4, 22, 20), "✕",
                new GUIStyle(sBtnSm) { normal = { textColor = C_DANGER } }))
            { customQAs.RemoveAt(idx); if (cqa_editing == idx) cqa_editing = -1; Repaint(); return; }

            if (idx > 0 && GUI.Button(new Rect(row.xMax - 52, row.y + 4, 22, 20), "▲", sBtnSm))
            { var t = customQAs[idx]; customQAs[idx] = customQAs[idx - 1]; customQAs[idx - 1] = t; Repaint(); }
            if (idx < customQAs.Count - 1 && GUI.Button(new Rect(row.xMax - 78, row.y + 4, 22, 20), "▼", sBtnSm))
            { var t = customQAs[idx]; customQAs[idx] = customQAs[idx + 1]; customQAs[idx + 1] = t; Repaint(); }

            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        void DrawCQAEdit(CustomQAItem qa)
        {
            GUILayout.Space(10);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(1, 2), C_ACCENT2);
            GUILayout.Space(4);
            DrawSecH("편집 패널", C_ACCENT2);
            GUILayout.Space(4);

            // 질문
            GUILayout.BeginHorizontal(); GUILayout.Space(12);
            GUILayout.Label("질문", sPVL, GUILayout.Width(80));
            qa.question = EditorGUILayout.TextField(qa.question);
            GUILayout.Space(8); GUILayout.EndHorizontal();

            // 위젯 종류
            GUILayout.BeginHorizontal(); GUILayout.Space(12);
            GUILayout.Label("위젯 종류", sPVL, GUILayout.Width(80));
            qa.widgetType = (QAWidgetType)EditorGUILayout.EnumPopup(qa.widgetType);
            GUILayout.Space(8); GUILayout.EndHorizontal();

            if (qa.widgetType == QAWidgetType.Slider || qa.widgetType == QAWidgetType.IntSlider)
            {
                // 범위 + 매핑 필드
                GUILayout.BeginHorizontal(); GUILayout.Space(12);
                GUILayout.Label("Min", sPVL, GUILayout.Width(30));
                qa.sliderMin = EditorGUILayout.FloatField(qa.sliderMin, GUILayout.Width(55));
                GUILayout.Label("Max", sPVL, GUILayout.Width(30));
                qa.sliderMax = EditorGUILayout.FloatField(qa.sliderMax, GUILayout.Width(55));
                GUILayout.EndHorizontal();

                while (qa.mappings.Count < 1) qa.mappings.Add(new QAParamMapping());
                GUILayout.Space(4);
                GUILayout.BeginHorizontal(); GUILayout.Space(12);
                GUILayout.Label("SO 필드 경로", sPVL, GUILayout.Width(90));
                qa.mappings[0].fieldPath = EditorGUILayout.TextField(qa.mappings[0].fieldPath);
                GUILayout.Space(8); GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(); GUILayout.Space(12);
                EditorGUILayout.HelpBox("슬라이더 값이 해당 float 필드에 직접 적용됩니다.", MessageType.None);
                GUILayout.Space(8); GUILayout.EndHorizontal();
            }
            else
            {
                // 선택지 목록
                GUILayout.Space(5);
                GUILayout.BeginHorizontal(); GUILayout.Space(12);
                GUILayout.Label("선택지", sH3);
                if (GUILayout.Button("＋", sBtnSm, GUILayout.Width(30)))
                { qa.options.Add(new QAOption { label = $"옵션 {qa.options.Count + 1}" }); qa.mappings.Add(new QAParamMapping()); }
                GUILayout.EndHorizontal();

                for (int oi = 0; oi < qa.options.Count; oi++) DrawOptRow(qa, oi);
            }
        }

        void DrawOptRow(CustomQAItem qa, int oi)
        {
            while (qa.mappings.Count <= oi) qa.mappings.Add(new QAParamMapping());
            var opt = qa.options[oi];
            var map = qa.mappings[oi];

            GUILayout.BeginHorizontal(); GUILayout.Space(20);
            GUILayout.Label($"{oi + 1}.", sPVL, GUILayout.Width(16));
            opt.label = EditorGUILayout.TextField(opt.label, GUILayout.Width(88));
            GUILayout.Label("경로:", sPVL, GUILayout.Width(30));
            map.fieldPath = EditorGUILayout.TextField(map.fieldPath, GUILayout.Width(120));
            GUILayout.Label("값:", sPVL, GUILayout.Width(18));
            map.floatValue = EditorGUILayout.FloatField(map.floatValue, GUILayout.Width(48));
            if (GUILayout.Button("✕", sBtnSm, GUILayout.Width(20)))
            { qa.options.RemoveAt(oi); qa.mappings.RemoveAt(oi); Repaint(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(); GUILayout.Space(52);
            GUILayout.Label(
                string.IsNullOrEmpty(map.fieldPath)
                    ? "↑ 경로 입력 예: \"fov\" / \"impactShake.shakeIntensity\""
                    : $"→ {map.fieldPath} = {map.floatValue:F3}",
                new GUIStyle(sPVL) { normal = { textColor = string.IsNullOrEmpty(map.fieldPath) ? C_TD : C_GBAR_NEW } });
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region Recalc
        // ═══════════════════════════════════════════════════════════

        void Recalc()
        {
            if (isStanceSO) RecalcStance(); else RecalcSeq();
            foreach (var qa in customQAs) ApplyCQA(qa);
        }

        void RecalcStance()
        {
            float baseFov = qa_usage switch
            {
                UsageCtx.Explore => 70f,
                UsageCtx.Combat_Human => 62f,
                UsageCtx.Combat_Boss => 55f,
                UsageCtx.Cutscene => 45f,
                UsageCtx.UI => 50f,
                UsageCtx.HitReaction => 55f,
                _ => 60f
            };
            pv_fov = Mathf.Clamp(baseFov - (int)qa_tension * 3f, 25f, 90f);
            pv_zTilt = qa_usage == UsageCtx.HitReaction ? -3f : 0f;
            if (qa_tension == TensionLv.Extreme) pv_zTilt -= 2f;

            float ox = qa_pos == CamPos.Left ? -0.55f : qa_pos == CamPos.Right ? 0.55f : 0f;
            float oy = qa_pos == CamPos.OverHead ? 2.0f : qa_pos == CamPos.LowAngle ? 0.6f : 1.3f;
            float oz = qa_pos == CamPos.OverHead ? -qa_dist * 0.8f : -qa_dist;
            pv_offset = new Vector3(ox, oy, oz);
            pv_baseYaw = qa_lockOn ? (qa_pos == CamPos.Right ? -30f : qa_pos == CamPos.Left ? 30f : 0f) : 0f;
            pv_dynFrm = qa_dynFrm;
            pv_shake = qa_shake != ShakeSt.None;
            pv_shakeInt = (float)qa_shake * 0.5f;
            pv_edge = qa_edge;
            pv_lockPenalty = qa_lockOn;
        }

        void RecalcSeq()
        {
            pv_intInput = qa_seqInterrupt == SeqInterrupt.ByInput || qa_seqInterrupt == SeqInterrupt.Always;
            pv_intDmg = qa_seqInterrupt == SeqInterrupt.ByDamage || qa_seqInterrupt == SeqInterrupt.Always;
            pv_restAngle = qa_seqRestore == SeqRestore.Angle || qa_seqRestore == SeqRestore.Both;
            pv_restStance = qa_seqRestore == SeqRestore.Stance || qa_seqRestore == SeqRestore.Both;
            pv_restSpeed = qa_seqPurpose == SeqPurpose.Execution ? 2.0f : 3.0f;
            pv_restBlend = qa_seqBlend;
        }

        void ApplyCQA(CustomQAItem qa)
        {
            if (serializedSO == null) return;
            if (qa.widgetType == QAWidgetType.Slider || qa.widgetType == QAWidgetType.IntSlider)
            {
                if (qa.mappings.Count == 0 || string.IsNullOrEmpty(qa.mappings[0].fieldPath)) return;
                var sp = FindProp(qa.mappings[0].fieldPath); if (sp == null) return;
                float v = qa.widgetType == QAWidgetType.IntSlider ? Mathf.Round(qa.sliderValue) : qa.sliderValue;
                if (sp.propertyType == SerializedPropertyType.Float) sp.floatValue = v;
                else sp.intValue = Mathf.RoundToInt(v);
            }
            else if (qa.selectedIndex < qa.mappings.Count)
            {
                var map = qa.mappings[qa.selectedIndex];
                if (map == null || string.IsNullOrEmpty(map.fieldPath)) return;
                var sp = FindProp(map.fieldPath); if (sp == null) return;
                if (sp.propertyType == SerializedPropertyType.Float) sp.floatValue = map.floatValue;
                else if (sp.propertyType == SerializedPropertyType.Boolean) sp.boolValue = map.boolValue;
                else if (sp.propertyType == SerializedPropertyType.Integer) sp.intValue = map.intValue;
            }
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region Apply + Infer
        // ═══════════════════════════════════════════════════════════

        void DoApply()
        {
            if (targetSO == null) return;
            Undo.RecordObject(targetSO, "TDA Camera SO Wizard Apply");
            if (isStanceSO) ApplyStance((CameraStancePresetSO)targetSO);
            else ApplySeq((CameraSequencePresetSO)targetSO);
            if (serializedSO != null) serializedSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetSO);
            if (!Application.isPlaying) AssetDatabase.SaveAssets();
            pendingApply = false;
            lastSave = $"Saved {DateTime.Now:HH:mm:ss}";
            Debug.Log($"<color=lime>[TDA SO Wizard]</color> '{targetSO.name}' 저장 완료 ✓");
            Repaint();
        }

        void ApplyStance(CameraStancePresetSO so)
        {
            so.fov = pv_fov; so.zTilt = pv_zTilt; so.baseOffset = pv_offset; so.baseYawOffset = pv_baseYaw;
            so.useDynamicFraming = pv_dynFrm;
            var shk = so.impactShake; shk.enableShake = pv_shake; shk.shakeIntensity = pv_shakeInt;
            if (pv_shake && shk.maxDuration <= 0f) shk.maxDuration = 0.2f; so.impactShake = shk;
            var hh = so.handheldEffect; hh.enableHandheldEffect = qa_handheld;
            if (qa_handheld && hh.swayAmount < 0.01f) { hh.swayAmount = 0.3f; hh.swaySpeed = 1f; }
            so.handheldEffect = hh;
            var ep = so.edgePanningData; ep.useEdgePanning = pv_edge;
            if (pv_edge && ep.edgePanThreshold < 0.01f) ep.edgePanThreshold = 0.08f; so.edgePanningData = ep;
            var lp = so.lockOnPenaltyData; lp.enableTargetEscapePenalty = pv_lockPenalty;
            if (pv_lockPenalty && lp.targetEscapeViewportThreshold < 0.01f) lp.targetEscapeViewportThreshold = 0.08f;
            so.lockOnPenaltyData = lp;
        }

        void ApplySeq(CameraSequencePresetSO so)
        {
            so.canBeInterruptedByInput = pv_intInput; so.canBeInterruptedByDamage = pv_intDmg;
            so.restorePreviousAngle = pv_restAngle; so.restoreToDefaultStanceOnFinish = pv_restStance;
            so.restoreAngleSpeed = pv_restSpeed; so.restoreBlendDuration = pv_restBlend;
            if (so.steps != null)
                for (int i = 0; i < Mathf.Min(so.steps.Count, qa_seqSteps); i++)
                {
                    var s = so.steps[i]; s.blendDuration = qa_seqBlend; s.holdDuration = qa_seqHold;
                    s.actionPositionDamping = qa_seqDamp; s.actionRotationDamping = qa_seqDamp;
                    if (s.blendCurve == null || s.blendCurve.length == 0) s.blendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                    if (s.trackingWeightCurve == null || s.trackingWeightCurve.length == 0)
                    { s.trackingWeightCurve = AnimationCurve.Constant(0, 1, 1); s.trackingEndTime = 1f; }
                    so.steps[i] = s;
                }
        }

        void InferStance(CameraStancePresetSO so)
        {
            qa_dist = Mathf.Abs(so.baseOffset.z); qa_dynFrm = so.useDynamicFraming;
            qa_edge = so.edgePanningData.useEdgePanning; qa_handheld = so.handheldEffect.enableHandheldEffect;
            qa_lockOn = so.lockOnPenaltyData.enableTargetEscapePenalty || so.edgePanningData.useEdgePanning;
            qa_multi = so.focusTargets != null && so.focusTargets.Count > 0;
            qa_shake = so.impactShake.enableShake
                ? (ShakeSt)Mathf.Clamp(Mathf.RoundToInt(so.impactShake.shakeIntensity / 0.5f), 1, 4) : ShakeSt.None;
            qa_pos = so.baseOffset.x > 0.3f ? CamPos.Right : so.baseOffset.x < -0.3f ? CamPos.Left :
                   so.baseOffset.y > 1.8f ? CamPos.OverHead : so.baseOffset.y < 0.8f ? CamPos.LowAngle : CamPos.Center;
            qa_tension = so.fov >= 68 ? TensionLv.Calm : so.fov >= 58 ? TensionLv.Normal :
                       so.fov >= 48 ? TensionLv.Tense : so.fov >= 38 ? TensionLv.Intense : TensionLv.Extreme;
        }

        void InferSeq(CameraSequencePresetSO so)
        {
            qa_seqInterrupt = so.canBeInterruptedByInput && so.canBeInterruptedByDamage ? SeqInterrupt.Always :
                            so.canBeInterruptedByInput ? SeqInterrupt.ByInput :
                            so.canBeInterruptedByDamage ? SeqInterrupt.ByDamage : SeqInterrupt.Never;
            qa_seqRestore = so.restorePreviousAngle && so.restoreToDefaultStanceOnFinish ? SeqRestore.Both :
                          so.restorePreviousAngle ? SeqRestore.Angle :
                          so.restoreToDefaultStanceOnFinish ? SeqRestore.Stance : SeqRestore.None;
            if (so.steps != null && so.steps.Count > 0)
            {
                qa_seqSteps = so.steps.Count; qa_seqBlend = so.steps[0].blendDuration;
                qa_seqHold = so.steps[0].holdDuration; qa_seqDamp = so.steps[0].actionPositionDamping;
            }
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region 공통 Q&A 컴포넌트
        // ═══════════════════════════════════════════════════════════

        void QCard(string title, System.Action body)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(8);
            GUILayout.BeginVertical();
            GUILayout.Space(2); GUILayout.Label(title, sBody); GUILayout.Space(3);
            body?.Invoke(); GUILayout.Space(6);
            Rect ln = GUILayoutUtility.GetRect(1, 1); ln.x = 8; ln.width = position.width * 0.52f - 18;
            EditorGUI.DrawRect(ln, C_BORDER); GUILayout.Space(5);
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
        }

        T Radio<T>(T current, string[] labels) where T : Enum
        { return (T)(object)RadioInt((int)(object)current, labels); }

        int RadioInt(int current, string[] labels)
        {
            int result = current, cols = Mathf.Min(labels.Length, 3);
            GUILayout.BeginVertical();
            for (int i = 0; i < labels.Length; i += cols)
            {
                GUILayout.BeginHorizontal(); GUILayout.Space(16);
                for (int j = i; j < Mathf.Min(i + cols, labels.Length); j++)
                {
                    bool sel = j == current; Color c = sel ? C_ACCENT : C_CARD;
                    GUIStyle st = new GUIStyle(sBtnSm)
                    {
                        normal = { background = MkTex(c), textColor = sel ? Color.black : C_TN },
                        hover = { background = MkTex(c * 0.9f), textColor = sel ? Color.black : C_TN },
                        active = { background = MkTex(C_ACCENT * 1.1f), textColor = Color.black }
                    };
                    if (GUILayout.Button(labels[j], st, GUILayout.Height(24))) result = j;
                }
                GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.Space(2);
            }
            GUILayout.EndVertical();
            return result;
        }

        T Dropdown<T>(string lbl, T current, string[] labels) where T : Enum
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(16);
            GUILayout.Label(lbl, sPVL, GUILayout.Width(90));
            int n = EditorGUILayout.Popup((int)(object)current, labels);
            GUILayout.Space(8); GUILayout.EndHorizontal();
            return (T)(object)n;
        }

        bool Toggle(string lbl, bool val)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(16);
            bool n = EditorGUILayout.Toggle(val, GUILayout.Width(18));
            GUILayout.Label(lbl, sBody); GUILayout.EndHorizontal();
            return n;
        }

        float Slider(string lbl, float val, float min, float max)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(16);
            GUILayout.Label(lbl, sPVL, GUILayout.Width(140));
            float n = GUILayout.HorizontalSlider(val, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label($"{n:F2}", sPVL, GUILayout.Width(38));
            GUILayout.Space(8); GUILayout.EndHorizontal();
            return n;
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region 공통 미리보기 컴포넌트
        // ═══════════════════════════════════════════════════════════

        void PVSection(string title, System.Action body)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(6);
            GUILayout.BeginVertical("box");
            GUILayout.Label(title, sH2); GUILayout.Space(2);
            body?.Invoke(); GUILayout.Space(4);
            GUILayout.EndVertical(); GUILayout.Space(4);
            GUILayout.EndHorizontal(); GUILayout.Space(4);
        }

        void PVFloat(string lbl, float newV, float curV, float min, float max)
        {
            bool diff = Mathf.Abs(newV - curV) > 0.001f;
            GUILayout.BeginHorizontal(); GUILayout.Space(6);
            if (diff) EditorGUI.DrawRect(GUILayoutUtility.GetRect(3, 16), C_WARN);
            GUILayout.Label(lbl, sPVL, GUILayout.Width(112));
            Rect gr = GUILayoutUtility.GetRect(80, 18); MiniBar(gr, min, max, curV, newV, diff);
            if (diff)
                GUILayout.Label($"{curV:F2}→{newV:F2}",
                    new GUIStyle(sPVV) { normal = { textColor = C_WARN } }, GUILayout.Width(90));
            else GUILayout.Label($"{newV:F2}", sPVV, GUILayout.Width(48));
            GUILayout.EndHorizontal();
        }

        void PVBool(string lbl, bool newV, bool curV)
        {
            bool diff = newV != curV;
            GUILayout.BeginHorizontal(); GUILayout.Space(6);
            if (diff) EditorGUI.DrawRect(GUILayoutUtility.GetRect(3, 16), C_WARN);
            GUILayout.Label(lbl, sPVL, GUILayout.Width(150));
            GUIStyle st = new GUIStyle(sPVV);
            string txt;
            if (diff) { txt = $"{(curV ? "ON" : "OFF")}→{(newV ? "ON" : "OFF")}"; st.normal.textColor = C_WARN; }
            else { txt = newV ? "✓ ON" : "✗ OFF"; st.normal.textColor = newV ? C_OK : C_TD; }
            GUILayout.Label(txt, st);
            GUILayout.EndHorizontal();
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        #region 헬퍼
        // ═══════════════════════════════════════════════════════════

        void DrawSecH(string title, Color accent)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(8);
            Rect br = GUILayoutUtility.GetRect(3, 18); br.y -= 1; br.height += 2;
            EditorGUI.DrawRect(br, accent); GUILayout.Space(6);
            GUILayout.Label(title, sH2); GUILayout.EndHorizontal();
        }

        Texture2D MkTex(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var px = new Color[] { c, c, c, c }; t.SetPixels(px); t.Apply(); return t;
        }

        void BuildStyles()
        {
            if (stylesBuilt) return; stylesBuilt = true;
            sH1 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = C_TH } };
            sH2 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = C_ACCENT } };
            sH3 = new GUIStyle(EditorStyles.label) { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = C_ACCENT2 } };
            sBody = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, normal = { textColor = C_TN } };
            sHint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, alignment = TextAnchor.MiddleRight, normal = { textColor = C_TD } };
            sBtnApply = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            sBtnTab = new GUIStyle(GUI.skin.button) { fontSize = 10, normal = { textColor = C_TN } };
            sBtnTabSel = new GUIStyle(GUI.skin.button) { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = C_ACCENT } };
            sBtnSm = new GUIStyle(GUI.skin.button) { fontSize = 9, padding = new RectOffset(4, 4, 2, 2), margin = new RectOffset(0, 3, 0, 0) };
            sPVL = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, normal = { textColor = C_TD }, padding = new RectOffset(0, 4, 0, 0) };
            sPVV = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = C_TN } };
            sBadge = new GUIStyle(EditorStyles.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0, 0, 0, 0.9f) }
            };
        }
        #endregion
    }
}
#endif