// =============================================================================
// TDAMasterInspector_Utils.cs  |  TDA Project
// 경로 : Assets/Editor/TDAMasterInspector_Utils.cs
//
// 역할 : GUIStyle 캐시 / 폴드아웃 계층 시스템 / 시각화 헬퍼
//
// ── 폴드아웃 시스템 ────────────────────────────────────────────────────────
//   BeginFoldout(key, label, defaultOpen)  → bool (열려있으면 true)
//   EndFoldout()                           → 항상 호출 (스택 pop)
//   BeginFoldoutColored(...)               → 컬러 배경 + 상태 표시
//
// ── 시각화 헬퍼 ────────────────────────────────────────────────────────────
//   DrawStatBar(label, value, max, color)  → 수평 게이지 바
//   DrawConnectionMatrix(items, cols, cellW)→ OK/경고 그리드
//   SOFieldRow(...)                        → Ping 버튼 포함 프로퍼티 행
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TDA.EditorTools
{
    public partial class TDAMasterInspector
    {
        // =====================================================================
        // GUIStyle 캐시
        // =====================================================================
        internal GUIStyle styleBadgePlayer;
        internal GUIStyle styleBadgeAI;
        internal GUIStyle styleWarn;
        internal GUIStyle styleOK;
        internal GUIStyle styleSectionBg;
        internal GUIStyle styleFoldoutHeader;
        internal GUIStyle styleFoldoutHeaderWarn;
        private bool stylesReady;

        internal void InitStyles()
        {
            if (stylesReady) return;

            styleBadgePlayer = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.35f, 0.65f, 1f) },
                fontStyle = FontStyle.Bold
            };
            styleBadgeAI = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.42f, 0.42f) },
                fontStyle = FontStyle.Bold
            };
            styleWarn = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.8f, 0.1f) },
                fontStyle = FontStyle.Bold
            };
            styleOK = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.3f, 0.92f, 0.48f) },
                fontStyle = FontStyle.Bold
            };
            styleSectionBg = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.75f, 0.88f, 1f) }
            };

            // 폴드아웃 헤더 — 정상
            styleFoldoutHeader = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                normal = { textColor = new Color(0.82f, 0.92f, 1f) },
                onNormal = { textColor = new Color(0.82f, 0.92f, 1f) },
                focused = { textColor = new Color(0.82f, 0.92f, 1f) },
                onFocused = { textColor = new Color(0.82f, 0.92f, 1f) },
                active = { textColor = new Color(0.82f, 0.92f, 1f) },
                onActive = { textColor = new Color(0.82f, 0.92f, 1f) }
            };

            // 폴드아웃 헤더 — 경고 (SO 미연결 등)
            styleFoldoutHeaderWarn = new GUIStyle(styleFoldoutHeader)
            {
                normal = { textColor = new Color(1f, 0.72f, 0.1f) },
                onNormal = { textColor = new Color(1f, 0.72f, 0.1f) },
                focused = { textColor = new Color(1f, 0.72f, 0.1f) },
                onFocused = { textColor = new Color(1f, 0.72f, 0.1f) },
                active = { textColor = new Color(1f, 0.72f, 0.1f) },
                onActive = { textColor = new Color(1f, 0.72f, 0.1f) }
            };

            stylesReady = true;
        }

        // =====================================================================
        // 폴드아웃 계층 시스템
        // =====================================================================

        // 현재 열려있는 폴드아웃 스택 (BeginFoldout → EndFoldout 쌍 관리)
        private readonly Stack<bool> foldoutStack = new Stack<bool>();

        /// <summary>
        /// 표준 폴드아웃 헤더를 그립니다.
        /// 반환값이 true일 때 콘텐츠를 그리고, 반드시 EndFoldout() 을 호출하세요.
        /// </summary>
        internal bool BeginFoldout(string key, string label,
            bool defaultOpen = true, int indentOverride = 0)
        {
            if (!foldouts.ContainsKey(key))
                foldouts[key] = defaultOpen;

            // 배경 바
            Rect headerRect = EditorGUILayout.GetControlRect(false, 22f);
            int indent = indentOverride > 0 ? indentOverride : 0;
            headerRect.xMin += indent * 14;

            EditorGUI.DrawRect(headerRect,
                foldouts[key]
                    ? new Color(0.14f, 0.22f, 0.38f, 0.85f)
                    : new Color(0.12f, 0.12f, 0.16f, 0.75f));

            // 좌측 인디케이터 바
            EditorGUI.DrawRect(
                new Rect(headerRect.x, headerRect.y, 2, headerRect.height),
                new Color(0.35f, 0.55f, 1f, 0.7f));

            // 폴드아웃 화살표 + 라벨
            bool newState = EditorGUI.Foldout(
                new Rect(headerRect.x + 6, headerRect.y + 3,
                         headerRect.width - 10, 16),
                foldouts[key], "  " + label,
                true, styleFoldoutHeader);

            foldouts[key] = newState;
            foldoutStack.Push(newState);
            return newState;
        }

        /// <summary>
        /// 컬러 상태 표시가 포함된 폴드아웃 (OK = 초록, 경고 = 노랑)
        /// </summary>
        internal bool BeginFoldoutColored(string key, string label,
            bool isOk, int indentLevel = 0)
        {
            if (!foldouts.ContainsKey(key))
                foldouts[key] = false;

            Rect headerRect = EditorGUILayout.GetControlRect(false, 24f);
            headerRect.xMin += indentLevel * 14;

            Color bg = isOk
                ? new Color(0.12f, 0.24f, 0.14f, 0.75f)
                : new Color(0.28f, 0.14f, 0.10f, 0.75f);
            EditorGUI.DrawRect(headerRect, bg);

            // 상태 색 스트라이프
            EditorGUI.DrawRect(
                new Rect(headerRect.x, headerRect.y, 3, headerRect.height),
                isOk ? new Color(0.2f, 0.85f, 0.35f) : new Color(1f, 0.6f, 0.1f));

            bool newState = EditorGUI.Foldout(
                new Rect(headerRect.x + 8, headerRect.y + 4,
                         headerRect.width - 12, 16),
                foldouts[key], "  " + label, true,
                isOk ? styleFoldoutHeader : styleFoldoutHeaderWarn);

            foldouts[key] = newState;
            foldoutStack.Push(newState);
            return newState;
        }

        /// <summary>
        /// BeginFoldout 이후 반드시 호출합니다.
        /// </summary>
        internal void EndFoldout()
        {
            if (foldoutStack.Count > 0)
                foldoutStack.Pop();
            EditorGUILayout.Space(1);
        }

        // =====================================================================
        // 시각화 헬퍼
        // =====================================================================

        /// <summary>
        /// 수평 게이지 바 (라벨 + 색상 바 + 수치)
        /// </summary>
        internal void DrawStatBar(string label, float value, float maxValue, Color barColor)
        {
            if (maxValue <= 0f) maxValue = 1f;
            float ratio = Mathf.Clamp01(value / maxValue);

            Rect rowRect = EditorGUILayout.GetControlRect(false, 20f);
            rowRect.xMin += EditorGUI.indentLevel * 15 + 4;

            // 라벨
            const float labelW = 130f;
            EditorGUI.LabelField(
                new Rect(rowRect.x, rowRect.y + 2, labelW, 16),
                label, EditorStyles.miniLabel);

            // 바 영역
            Rect barBg = new Rect(rowRect.x + labelW, rowRect.y + 4,
                rowRect.width - labelW - 50f, 12f);
            EditorGUI.DrawRect(barBg, new Color(0.12f, 0.12f, 0.14f));
            EditorGUI.DrawRect(
                new Rect(barBg.x, barBg.y, barBg.width * ratio, barBg.height),
                barColor * new Color(1, 1, 1, 0.85f));

            // 수치 텍스트
            EditorGUI.LabelField(
                new Rect(barBg.xMax + 4, rowRect.y + 2, 46f, 16),
                value >= 10f ? $"{value:F0}" : $"{value:F2}",
                EditorStyles.miniLabel);
        }

        /// <summary>
        /// OK / 경고 상태 그리드 매트릭스
        /// </summary>
        internal void DrawConnectionMatrix(
            List<(string name, bool connected)> items,
            int cols, float cellW)
        {
            if (items == null || items.Count == 0) return;

            const float cellH = 22f;
            int rows = Mathf.CeilToInt((float)items.Count / cols);
            Rect area = EditorGUILayout.GetControlRect(false, rows * (cellH + 2) + 6);
            area.xMin += 4;

            for (int i = 0; i < items.Count; i++)
            {
                int r = i / cols, col = i % cols;
                var (name, ok) = items[i];

                Rect cell = new Rect(
                    area.x + col * (cellW + 2),
                    area.y + r * (cellH + 2),
                    cellW, cellH);

                EditorGUI.DrawRect(cell,
                    ok ? new Color(0.12f, 0.36f, 0.18f, 0.85f)
                       : new Color(0.40f, 0.16f, 0.10f, 0.85f));

                // 상태 인디케이터 왼쪽 바
                EditorGUI.DrawRect(
                    new Rect(cell.x, cell.y, 2, cell.height),
                    ok ? new Color(0.2f, 0.9f, 0.35f) : new Color(1f, 0.5f, 0.1f));

                string displayName = name.Length > (int)(cellW / 7.5f)
                    ? name[..(int)(cellW / 7.5f)] + "…" : name;

                EditorGUI.LabelField(
                    new Rect(cell.x + 5, cell.y + 4, cell.width - 6, 14),
                    (ok ? "✅ " : "⚠ ") + displayName,
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(2);
        }

        // =====================================================================
        // 공통 필드 / UI 헬퍼
        // =====================================================================

        /// <summary>
        /// SerializedProperty 한 행 — ObjectReference는 Ping 버튼 포함
        /// </summary>
        internal void SOFieldRow(SerializedObject ser, SerializedProperty prop,
            string label, bool warnIfNull = false, string warnMsg = "")
        {
            if (prop == null)
            {
                EditorGUILayout.LabelField(label, "— 필드 없음", EditorStyles.miniLabel);
                return;
            }

            bool isObjRef = prop.propertyType == SerializedPropertyType.ObjectReference;
            bool missing = warnIfNull && isObjRef && prop.objectReferenceValue == null;

            if (missing) GUI.color = new Color(1f, 0.75f, 0.25f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(prop,
                    new GUIContent(label), GUILayout.ExpandWidth(true));

                if (isObjRef && prop.objectReferenceValue != null)
                {
                    if (GUILayout.Button("↗", GUILayout.Width(24)))
                    {
                        EditorGUIUtility.PingObject(prop.objectReferenceValue);
                        Selection.activeObject = prop.objectReferenceValue;
                    }
                }
            }
            GUI.color = Color.white;

            if (missing && !string.IsNullOrEmpty(warnMsg))
                WarnLabel(warnMsg);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 경고 레이블 / 박스
        // ─────────────────────────────────────────────────────────────────────
        internal void WarnLabel(string msg)
        {
            GUI.color = new Color(1f, 0.78f, 0.1f);
            EditorGUILayout.LabelField("  ⚠ " + msg, EditorStyles.miniLabel);
            GUI.color = Color.white;
        }

        internal void WarnBox(string msg)
            => EditorGUILayout.HelpBox(msg, MessageType.Warning);

        // ─────────────────────────────────────────────────────────────────────
        // 섹션 헤더 (BeginFoldout 미사용 단순 구분선이 필요할 때)
        // ─────────────────────────────────────────────────────────────────────
        internal void SectionHeader(string title)
        {
            EditorGUILayout.Space(4);
            Rect r = EditorGUILayout.GetControlRect(false, 19f);
            EditorGUI.DrawRect(r, new Color(0.14f, 0.24f, 0.44f, 0.7f));
            EditorGUI.LabelField(r, "  " + title, styleSectionBg);
            EditorGUILayout.Space(2);
        }
    }
}
#endif