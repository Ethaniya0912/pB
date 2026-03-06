using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using TDA.Core.Events;
using TDA.Character.Animation;
using TDA.AnimatorBehaviours;

namespace TDA.EditorScripts
{
    /// <summary>
    /// [Master Simulator V2] 
    /// 유니티 내장 프리뷰의 끊김 현상을 해결하기 위해, 에디터 전용 'AnimationMode' API를 사용하여
    /// Scene 뷰에 임시 더미를 소환하고 100% 완벽한 프레임으로 애니메이션을 시뮬레이션합니다.
    /// </summary>
    public class AnimationEventPreviewWindow : EditorWindow
    {
        // --- 타겟 데이터 ---
        private GameObject targetCharacterPrefab; // 씬 객체가 아닌 '프리팹'을 바로 받습니다.
        private GameObject previewDummy; // 씬에 자동 소환될 임시 더미

        private CharacterAnimationSetSO targetSetSO;
        private AnimationEventParamsSO targetSO;
        private int currentMappingIndex = -1;

        // --- 시뮬레이션 상태 ---
        private float previewTime = 0f; // 0.0 ~ 1.0 (Normalized)
        private bool isPlaying = false;
        private float playbackSpeed = 1.0f;
        private double lastUpdateTime;
        private bool comboReserved = false;

        // --- UI 및 렌더링 ---
        private Vector2 sidebarScroll;
        private Vector2 mainScroll;
        private List<AnimationEventPoint> workingEventPoints = new List<AnimationEventPoint>();
        private bool isDirty = false;

        [MenuItem("TDA/Animation/Pro Action Simulator V2")]
        public static void ShowWindow()
        {
            var window = GetWindow<AnimationEventPreviewWindow>("Pro Action Sim");
            window.minSize = new Vector2(1100, 700);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            DestroyPreviewDummy();
        }

        private void OnDestroy()
        {
            DestroyPreviewDummy();
        }

        // ==============================================================================
        // 완벽한 60FPS Scene View 샘플링 (AnimationMode API)
        // ==============================================================================

        private void OnEditorUpdate()
        {
            if (isPlaying && targetSO != null && targetSO.targetClip != null)
            {
                double currentTime = EditorApplication.timeSinceStartup;
                double deltaTime = currentTime - lastUpdateTime;
                lastUpdateTime = currentTime;

                // 실시간 재생 로직
                float deltaNormalized = (float)(deltaTime * playbackSpeed) / targetSO.targetClip.length;
                previewTime += deltaNormalized;

                // 콤보 체크 시뮬레이션
                CheckComboSimulation();

                if (previewTime >= 1f)
                {
                    if (targetSO.targetClip.isLooping)
                    {
                        previewTime -= 1f;
                    }
                    else
                    {
                        previewTime = 1f;
                        isPlaying = false;
                    }
                }

                SampleAnimationOnDummy();
                Repaint(); // 창 UI 갱신
            }
        }

        /// <summary>
        /// [핵심 개선] 유니티 에디터 공식 애니메이션 샘플링 API를 사용하여
        /// 씬에 생성된 더미 캐릭터의 뼈대를 프레임 단위로 완벽하고 부드럽게 조작합니다.
        /// </summary>
        private void SampleAnimationOnDummy()
        {
            if (previewDummy == null || targetSO == null || targetSO.targetClip == null) return;

            // 에디터 애니메이션 모드 활성화 (프리팹 원본 훼손 방지)
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            float actualTime = previewTime * targetSO.targetClip.length;

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(previewDummy, targetSO.targetClip, actualTime);
            AnimationMode.EndSampling();

            // Scene 뷰 강제 갱신 (가장 부드러운 렌더링을 위함)
            SceneView.RepaintAll();
        }

        private void CreatePreviewDummy()
        {
            if (targetCharacterPrefab == null)
            {
                EditorUtility.DisplayDialog("경고", "먼저 프리뷰 캐릭터 프리팹을 할당해주세요.", "확인");
                return;
            }

            DestroyPreviewDummy();

            // 씬의 원점에 임시 프리팹 소환
            previewDummy = Instantiate(targetCharacterPrefab, Vector3.zero, Quaternion.identity);
            previewDummy.name = "[TDA_Anim_Preview_Dummy_DoNotSave]";

            // 씬 저장 시 저장되지 않고, 플레이 모드 진입 시 사라지도록 설정
            previewDummy.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;

            // 시뮬레이션 중 방해되는 스크립트 강제 비활성화
            var monoBehaviours = previewDummy.GetComponentsInChildren<MonoBehaviour>();
            foreach (var mb in monoBehaviours)
            {
                mb.enabled = false;
            }

            // Scene 뷰 카메라를 더미로 자동 이동 및 포커싱
            Selection.activeGameObject = previewDummy;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }

            Debug.Log("<color=green>[Simulator]</color> 프리뷰 더미가 씬에 자동 소환되었습니다. Scene 창을 확인하세요!");
        }

        private void DestroyPreviewDummy()
        {
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            if (previewDummy != null)
            {
                DestroyImmediate(previewDummy);
                previewDummy = null;
            }
        }

        // ==============================================================================
        // 콤보 시뮬레이션 로직
        // ==============================================================================

        private void CheckComboSimulation()
        {
            if (!comboReserved) return;

            bool canComboNow = workingEventPoints.Any(p =>
                p.eventType == global::AnimationEventType.ComboEnable &&
                previewTime >= p.triggerTime &&
                previewTime <= 0.95f
            );

            if (canComboNow)
            {
                ExecuteComboTransition();
                comboReserved = false;
            }
        }

        private void ExecuteComboTransition()
        {
            if (targetSetSO == null) return;

            string currentStateName = targetSetSO.animationMappings[currentMappingIndex].logicalStateName;
            string nextStateName = "";

            if (currentStateName.Contains("_01")) nextStateName = currentStateName.Replace("_01", "_02");
            else if (currentStateName.Contains("_02")) nextStateName = currentStateName.Replace("_02", "_03");

            var nextMapping = targetSetSO.animationMappings.FindIndex(m => m.logicalStateName == nextStateName);
            if (nextMapping != -1)
            {
                SelectMapping(nextMapping);
                previewTime = 0f;
                isPlaying = true;
                lastUpdateTime = EditorApplication.timeSinceStartup;

                // 콤보 발동 시 유니티 내장 애니메이터 창에서도 해당 노드 자동 포커싱
                SyncWithUnityAnimator();
            }
        }

        // ==============================================================================
        // UI Drawing Methods (Professional Layout)
        // ==============================================================================

        private void OnGUI()
        {
            DrawProfessionalHeader();

            EditorGUILayout.BeginHorizontal();

            // 좌측 사이드바 (모션 매핑 리스트)
            DrawSidebar();

            // 우측 메인 뷰 (컨트롤 및 타임라인)
            EditorGUILayout.BeginVertical();
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);

            DrawDummyController();

            if (targetSO != null)
            {
                DrawSimulatorControls();
                DrawTimelineUI();
                DrawInputSimulation();

                EditorGUILayout.Space(20);
                DrawEventEditor();
            }
            else
            {
                DrawEmptyState();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawProfessionalHeader()
        {
            GUIStyle headerStyle = new GUIStyle("Window") { padding = new RectOffset(15, 15, 15, 15) };
            EditorGUILayout.BeginVertical(headerStyle);
            EditorGUILayout.BeginHorizontal();

            // 좌측 타이틀
            EditorGUILayout.BeginVertical();
            GUILayout.Label("⚔️ TDA PRO ACTION SIMULATOR", new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, normal = { textColor = Color.white } });
            GUILayout.Label("데이터 주도형 이벤트 & 콤보 타이밍 설계 도구", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // 우측 데이터 할당부
            EditorGUILayout.BeginVertical(GUILayout.Width(400));

            EditorGUIUtility.labelWidth = 140;
            EditorGUI.BeginChangeCheck();
            targetCharacterPrefab = (GameObject)EditorGUILayout.ObjectField("캐릭터 프리팹 (Project)", targetCharacterPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                DestroyPreviewDummy(); // 프리팹을 바꾸면 기존 더미 파괴
            }

            EditorGUILayout.Space(5);

            EditorGUI.BeginChangeCheck();
            targetSetSO = (CharacterAnimationSetSO)EditorGUILayout.ObjectField("타겟 무기/몹 셋업 (Set SO)", targetSetSO, typeof(CharacterAnimationSetSO), false);
            if (EditorGUI.EndChangeCheck())
            {
                currentMappingIndex = -1;
                targetSO = null;
            }
            EditorGUIUtility.labelWidth = 0;

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            Rect lineRect = EditorGUILayout.GetControlRect(false, 2);
            EditorGUI.DrawRect(lineRect, new Color(0.2f, 0.2f, 0.2f));
        }

        private void DrawDummyController()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal("box");

            GUILayout.Label("🎥 Scene View 프리뷰 컨트롤", EditorStyles.boldLabel, GUILayout.Width(200));

            if (previewDummy == null)
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                if (GUILayout.Button("🕹️ 프리뷰 더미 소환 및 카메라 고정", GUILayout.Height(30)))
                {
                    CreatePreviewDummy();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("🗑️ 프리뷰 더미 삭제", GUILayout.Height(30)))
                {
                    DestroyPreviewDummy();
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(10);
                if (GUILayout.Button("👁️ 카메라 다시 포커스", GUILayout.Height(30)))
                {
                    Selection.activeGameObject = previewDummy;
                    if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);
        }

        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(250), GUILayout.ExpandHeight(true));
            GUILayout.Label("📋 모션 리스트 (Anim Mappings)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (targetSetSO == null)
            {
                EditorGUILayout.HelpBox("우측 상단에 Character Animation Set SO를 할당해 주세요.", MessageType.Warning);
            }
            else
            {
                sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);
                for (int i = 0; i < targetSetSO.animationMappings.Count; i++)
                {
                    var mapping = targetSetSO.animationMappings[i];
                    GUI.backgroundColor = (currentMappingIndex == i) ? new Color(0.2f, 0.6f, 1f) : Color.white;

                    if (GUILayout.Button(mapping.logicalStateName, GUILayout.Height(35)))
                    {
                        SelectMapping(i);
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(5);
                if (GUILayout.Button("➕ 새 모션 매핑 추가", GUILayout.Height(30)))
                {
                    Undo.RecordObject(targetSetSO, "Add Mapping");
                    targetSetSO.animationMappings.Add(new AnimationSetMapping { logicalStateName = "New_Action_01" });
                    EditorUtility.SetDirty(targetSetSO);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void SelectMapping(int index)
        {
            if (isDirty)
            {
                if (EditorUtility.DisplayDialog("저장되지 않은 변경사항", "편집 중인 이벤트 데이터가 있습니다.\n저장하시겠습니까?", "Apply (저장)", "Discard (버리기)"))
                {
                    ApplyToSO();
                }
            }

            currentMappingIndex = index;
            targetSO = targetSetSO.animationMappings[index].paramsSO;
            LoadFromSO();

            // 탭 클릭 시 첫 프레임으로 씬 뷰 동기화
            previewTime = 0f;
            SampleAnimationOnDummy();
            SyncWithUnityAnimator();
        }

        private void DrawSimulatorControls()
        {
            EditorGUILayout.BeginVertical("helpBox");
            EditorGUILayout.BeginHorizontal(GUILayout.Height(40));

            // 재생 컨트롤
            GUI.backgroundColor = isPlaying ? new Color(1f, 0.6f, 0.2f) : new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button(isPlaying ? "⏸ PAUSE" : "▶ PLAY", GUILayout.Width(120), GUILayout.ExpandHeight(true)))
            {
                isPlaying = !isPlaying;
                lastUpdateTime = EditorApplication.timeSinceStartup;
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("⏹ RESET", GUILayout.Width(80), GUILayout.ExpandHeight(true)))
            {
                previewTime = 0;
                isPlaying = false;
                comboReserved = false;
                SampleAnimationOnDummy();
            }

            GUILayout.Space(30);

            // [개선] 배속 슬라이더 공간 압축
            EditorGUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("재생 배속 (Speed):", EditorStyles.boldLabel, GUILayout.Width(130));
            playbackSpeed = EditorGUILayout.Slider(playbackSpeed, 0.1f, 2.0f, GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // [개선] 유니티 내장 Animator 연동 버튼
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
            if (GUILayout.Button("🔍 Unity Animator 창 호출 (노드 보기)", GUILayout.Height(40), GUILayout.Width(250)))
            {
                SyncWithUnityAnimator();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawTimelineUI()
        {
            if (targetSO.targetClip == null) return;

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical("box");

            float totalFrames = targetSO.targetClip.length * targetSO.targetClip.frameRate;
            int currentFrame = Mathf.RoundToInt(previewTime * totalFrames);

            // 1. 슬라이더 & 프레임 정보
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            previewTime = EditorGUILayout.Slider("타임라인 (Timeline)", previewTime, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                SampleAnimationOnDummy(); // 슬라이더 드래그 시 즉각 동기화
            }
            GUILayout.Label($"{currentFrame} / {Mathf.CeilToInt(totalFrames)} F", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 2. 시각적 이벤트 마커 바
            Rect barRect = EditorGUILayout.GetControlRect(GUILayout.Height(30));
            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));

            // 워핑 구간 표시 (하늘색)
            float warpStartPx = barRect.x + (barRect.width * targetSO.warpStartTime);
            float warpEndPx = barRect.x + (barRect.width * targetSO.warpEndTime);
            EditorGUI.DrawRect(new Rect(warpStartPx, barRect.y, warpEndPx - warpStartPx, barRect.height), new Color(0f, 0.6f, 0.8f, 0.5f));

            // 이벤트 마커 그리기
            foreach (var point in workingEventPoints)
            {
                float xPos = barRect.x + (barRect.width * point.triggerTime);
                bool isActive = Mathf.Abs(previewTime - point.triggerTime) < 0.02f;

                Color markerColor = isActive ? Color.yellow : new Color(0.8f, 0.8f, 0.8f);
                EditorGUI.DrawRect(new Rect(xPos - 1, barRect.y, 3, barRect.height), markerColor);

                if (isActive)
                {
                    var labelStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.yellow }, fontSize = 12 };
                    EditorGUI.LabelField(new Rect(xPos - 20, barRect.y - 20, 150, 20), point.eventType.ToString(), labelStyle);
                }
            }

            // 붉은색 재생 헤드
            float headPos = barRect.x + (barRect.width * previewTime);
            EditorGUI.DrawRect(new Rect(headPos - 1, barRect.y - 5, 2, barRect.height + 10), Color.red);

            // 3. 상태 피드백 텍스트
            bool isWarping = previewTime >= targetSO.warpStartTime && previewTime <= targetSO.warpEndTime;
            string warpText = isWarping ? "<color=cyan>🔥 워핑 실행 구간 (Motion Warping ACTIVE)</color>" : "<color=grey>워핑 미작동</color>";

            string activeEventsText = "<color=grey>활성화된 이벤트 없음</color>";
            var activeEvents = workingEventPoints.Where(p => Mathf.Abs(previewTime - p.triggerTime) < 0.02f).ToList();
            if (activeEvents.Count > 0)
            {
                activeEventsText = "실행 중: " + string.Join(", ", activeEvents.Select(e => $"<color=yellow>[{e.eventType}]</color>"));
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(warpText, new GUIStyle(EditorStyles.label) { richText = true });
            GUILayout.FlexibleSpace();
            GUILayout.Label(activeEventsText, new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleRight });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawInputSimulation()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal("helpBox");
            GUILayout.Label("🎮 인풋 시뮬레이터 (Combo Test)", EditorStyles.boldLabel, GUILayout.Width(250));

            GUI.backgroundColor = comboReserved ? Color.cyan : Color.white;
            if (GUILayout.Button("🖱️ 마우스 좌클릭 (Light Attack)", GUILayout.Height(35), GUILayout.Width(200)))
            {
                comboReserved = true;
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("🖱️ 마우스 우클릭 (Heavy Attack)", GUILayout.Height(35), GUILayout.Width(200)))
            {
                comboReserved = true;
            }

            GUILayout.FlexibleSpace();
            if (comboReserved)
            {
                GUILayout.Label("<color=cyan>⚡ 콤보 입력 대기 중...</color>\n(ComboEnable 구간 진입 시 다음 탭으로 이동)", new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleRight });
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEventEditor()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("⚙️ EVENT POINTS 에디터 (1차 Data)", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            GUILayout.FlexibleSpace();

            if (isDirty) GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("💾 저장 (Apply)", GUILayout.Width(120), GUILayout.Height(30))) ApplyToSO();
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("🔄 되돌리기 (Reset)", GUILayout.Width(120), GUILayout.Height(30))) LoadFromSO();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(15);

            for (int i = 0; i < workingEventPoints.Count; i++)
            {
                EditorGUILayout.BeginHorizontal("helpBox");
                var point = workingEventPoints[i];

                EditorGUI.BeginChangeCheck();

                GUILayout.Label($"Event {i + 1}", EditorStyles.miniBoldLabel, GUILayout.Width(60));

                GUILayout.Label("발동 시점 (Time):", GUILayout.Width(110));
                float newTime = EditorGUILayout.Slider(point.triggerTime, 0f, 1f, GUILayout.ExpandWidth(true));

                GUILayout.Space(20);
                GUILayout.Label("이벤트 타입 (Type):", GUILayout.Width(120));
                global::AnimationEventType newType = (global::AnimationEventType)EditorGUILayout.EnumPopup(point.eventType, GUILayout.Width(250));

                if (EditorGUI.EndChangeCheck())
                {
                    point.triggerTime = newTime;
                    point.eventType = newType;
                    workingEventPoints[i] = point;
                    isDirty = true;
                }

                GUILayout.Space(10);
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("삭제", GUILayout.Width(60)))
                {
                    workingEventPoints.RemoveAt(i);
                    isDirty = true;
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space(10);
            GUI.backgroundColor = new Color(0.8f, 0.9f, 1f);
            if (GUILayout.Button("➕ 현재 타임라인 시점(재생 헤드 위치)에 새 이벤트 추가", GUILayout.Height(35)))
            {
                workingEventPoints.Add(new AnimationEventPoint { triggerTime = previewTime, eventType = global::AnimationEventType.ComboEnable });
                isDirty = true;

                // 시간순으로 즉시 정렬
                workingEventPoints = workingEventPoints.OrderBy(p => p.triggerTime).ToList();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 16 };
            GUILayout.Label("👈 왼쪽 사이드바에서 편집할 모션(액션)을 선택하세요.", style);
            if (targetSetSO == null)
            {
                GUILayout.Label("먼저 우측 상단에 Character Animation Set SO를 할당해야 합니다.", EditorStyles.centeredGreyMiniLabel);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        // ==============================================================================
        // Data & Logic Methods
        // ==============================================================================

        private void LoadFromSO()
        {
            if (targetSO == null) return;
            workingEventPoints = new List<AnimationEventPoint>(targetSO.eventPoints);
            isDirty = false;
        }

        private void ApplyToSO()
        {
            if (targetSO == null) return;
            Undo.RecordObject(targetSO, "Apply Animation Events");
            targetSO.eventPoints = new List<AnimationEventPoint>(workingEventPoints.OrderBy(p => p.triggerTime));
            EditorUtility.SetDirty(targetSO);
            isDirty = false;
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=green>[저장 완료]</color> {targetSO.name} 데이터가 성공적으로 원본 SO에 기록되었습니다.");
        }

        /// <summary>
        /// 유니티 내장 Animator 창을 강제로 띄우고, 
        /// 현재 우리가 시뮬레이터에서 선택한 논리적 상태(State) 노드를 파란색으로 하이라이트 해줍니다.
        /// </summary>
        private void SyncWithUnityAnimator()
        {
            if (targetSetSO == null || currentMappingIndex == -1 || targetSetSO.baseController == null)
            {
                Debug.LogWarning("[Simulator] Anim Set SO에 Base Controller가 등록되어 있지 않습니다.");
                return;
            }

            string logicalNameToFind = targetSetSO.animationMappings[currentMappingIndex].logicalStateName;

            // 유니티 내장 Animator 창 강제 오픈
            EditorApplication.ExecuteMenuItem("Window/Animation/Animator");

            AnimatorController controller = targetSetSO.baseController as AnimatorController;
            if (controller == null) return;

            foreach (var layer in controller.layers)
            {
                foreach (var state in layer.stateMachine.states)
                {
                    if (state.state.name == logicalNameToFind)
                    {
                        Selection.activeObject = state.state;
                        return;
                    }
                }
            }
            Debug.LogWarning($"[Animator Sync] Animator 내부에 '{logicalNameToFind}' State가 존재하지 않아 포커싱에 실패했습니다.");
        }
    }
}