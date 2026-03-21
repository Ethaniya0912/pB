using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;

namespace TDA.InternalEditorUtils
{
    /// <summary>
    /// [TDA] Quick Show in Inspector Utility
    /// 프로젝트 창의 에셋을 우클릭하여 전용 인스펙터 탭을 엽니다.
    /// 다중 선택을 지원하며, 선택된 모든 에셋이 하나의 윈도우 그룹에 네이티브 탭으로 병합됩니다.
    /// </summary>
    public static class QuickShowInInspectorEditor
    {
        // Assets 메뉴에 우클릭 항목 추가 (우선순위 0)
        [UnityEditor.MenuItem("Assets/[TDA] Show in inspector", false, 0)]
        public static void ShowInInspector()
        {
            // 1. 현재 프로젝트 창에서 선택된 모든 에셋들을 가져옵니다. (다중 선택 대응)
            UnityEngine.Object[] targets = UnityEditor.Selection.objects;

            if (targets == null || targets.Length == 0)
            {
                UnityEngine.Debug.LogWarning("[TDA] 인스펙터에 표시할 에셋이 선택되지 않았습니다.");
                return;
            }

            // 2. 선택된 모든 에셋을 순회하며 개별 탭으로 오픈
            foreach (UnityEngine.Object target in targets)
            {
                if (target == null) continue;
                TDAInspectorWindow.Open(target);
            }

            // 콘솔 피드백 출력
            UnityEngine.Debug.Log($"<color=cyan>[TDA]</color> 총 <b>{targets.Length}</b>개의 에셋을 독립 인스펙터 탭 그룹으로 생성 및 병합했습니다.");
        }

        /// <summary>
        /// 메뉴 활성화 조건 검사: 하나 이상의 에셋이 선택되어 있어야 합니다.
        /// </summary>
        [UnityEditor.MenuItem("Assets/[TDA] Show in inspector", true)]
        public static bool ShowInInspectorValidate()
        {
            return UnityEditor.Selection.objects != null && UnityEditor.Selection.objects.Length > 0;
        }
    }

    /// <summary>
    /// 단일 에셋의 인스펙터를 보여주는 커스텀 윈도우.
    /// ScriptableObject.CreateInstance를 사용하여 여러 인스턴스를 동시에 허용하며, 
    /// 리플렉션을 통해 기존 창에 네이티브 탭으로 강제 병합합니다.
    /// </summary>
    public class TDAInspectorWindow : UnityEditor.EditorWindow
    {
        [UnityEngine.SerializeField] private UnityEngine.Object _target;
        private UnityEditor.Editor _editor;
        private UnityEngine.Vector2 _scrollPos;

        /// <summary>
        /// 에셋별로 완전히 독립된 윈도우 인스턴스를 생성하고 기존 창 옆에 자동으로 도킹합니다.
        /// </summary>
        public static void Open(UnityEngine.Object target)
        {
            if (target == null) return;

            // 1. 중복 열기 방지: 이미 해당 에셋을 표시 중인 창이 있다면 포커스만 하고 종료
            TDAInspectorWindow[] windows = UnityEngine.Resources.FindObjectsOfTypeAll<TDAInspectorWindow>();
            TDAInspectorWindow anchorWindow = null;

            foreach (var win in windows)
            {
                if (win._target == target)
                {
                    win.Focus();
                    return;
                }
                // 병합의 기준점이 될 기존 TDA 창 하나를 저장 (가장 먼저 열린 창 우선)
                if (anchorWindow == null) anchorWindow = win;
            }

            // 2. 새 인스턴스 생성 (GetWindow 대신 CreateInstance를 써야 중복 생성이 가능함)
            TDAInspectorWindow newWindow = UnityEngine.ScriptableObject.CreateInstance<TDAInspectorWindow>();
            newWindow.Initialize(target);

            // 3. 자동 도킹 (Native Tab Docking) 로직
            bool docked = false;
            if (anchorWindow != null)
            {
                try
                {
                    // 리플렉션: EditorWindow 내부의 m_Parent 필드(HostView/DockArea)를 가져옵니다.
                    var parentField = typeof(UnityEditor.EditorWindow).GetField("m_Parent",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                    if (parentField != null)
                    {
                        var hostView = parentField.GetValue(anchorWindow);
                        if (hostView != null)
                        {
                            // HostView(또는 DockArea)의 AddTab 메서드를 찾아 새 윈도우를 추가합니다.
                            var addTabMethod = hostView.GetType().GetMethod("AddTab",
                                new System.Type[] { typeof(UnityEditor.EditorWindow) });

                            if (addTabMethod != null)
                            {
                                addTabMethod.Invoke(hostView, new object[] { newWindow });
                                docked = true;
                            }
                        }
                    }
                }
                catch
                {
                    // 리플렉션 실패 시 일반 Show로 진행
                }
            }

            // 기존 창이 없거나 도킹에 실패한 경우 독립 창으로 띄움
            if (!docked)
            {
                newWindow.Show();
            }
            else
            {
                newWindow.Focus();
            }
        }

        /// <summary>
        /// 윈도우의 타겟 에셋을 설정하고 탭 정보(이름, 아이콘)를 업데이트합니다.
        /// </summary>
        public void Initialize(UnityEngine.Object target)
        {
            _target = target;

            // 탭의 이름과 에셋 고유의 미니 아이콘을 유니티 네이티브 스타일로 설정
            this.titleContent = new UnityEngine.GUIContent(
                _target.name,
                UnityEditor.AssetPreview.GetMiniThumbnail(_target)
            );

            CreateEditorInstance();
        }

        private void CreateEditorInstance()
        {
            if (_editor != null)
            {
                DestroyImmediate(_editor);
            }

            if (_target != null)
            {
                // UnityEditor.Editor를 명시하여 네임스페이스 충돌 방지
                _editor = UnityEditor.Editor.CreateEditor(_target);
            }
        }

        private void OnEnable()
        {
            if (_target != null && _editor == null)
            {
                CreateEditorInstance();
            }
        }

        private void OnGUI()
        {
            if (_target == null)
            {
                UnityEditor.EditorGUILayout.HelpBox("에셋을 찾을 수 없습니다. 이 탭을 닫아주세요.", UnityEditor.MessageType.Warning);
                return;
            }

            _scrollPos = UnityEditor.EditorGUILayout.BeginScrollView(_scrollPos);

            if (_editor != null)
            {
                // 에디터 내용을 그림
                _editor.OnInspectorGUI();
            }
            else
            {
                CreateEditorInstance();
            }

            UnityEditor.EditorGUILayout.EndScrollView();
        }

        private void OnDestroy()
        {
            if (_editor != null)
            {
                DestroyImmediate(_editor);
            }
        }
    }
}