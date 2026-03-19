#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.Cameras;
using TDA.World.EditorTools;

namespace TDA.World.EditorTools.CustomEditors
{
    /// <summary>
    /// [프리뷰어 생성 툴] 
    /// 지정된 스탠스(SO) 데이터를 씬에서 바로 확인할 수 있도록 가상의 오브젝트(캡슐/모델)를 소환하고, 
    /// 에디터 창을 닫으면 자동으로 씬에서 정리해 주는 통합 툴 윈도우입니다.
    /// </summary>
    public class CameraDirectingPreviewerWindow : EditorWindow
    {
        [Header("Settings")]
        [SerializeField] private CameraStancePresetSO targetStance;
        [SerializeField] private GameObject customModelPrefab;

        // 씬에 소환된 가상의 캐릭터를 추적하는 변수
        private GameObject spawnedDummy;

        [MenuItem("TDA/Camera/Camera Directing Previewer Tool")]
        public static void ShowWindow()
        {
            var window = GetWindow<CameraDirectingPreviewerWindow>("Camera Previewer");
            window.minSize = new Vector2(350, 250);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("🎥 카메라 연출 프리뷰어 툴", EditorStyles.boldLabel);
            GUILayout.Space(5);
            EditorGUILayout.HelpBox("버튼을 누르면 씬에 가상의 캐릭터(또는 캡슐)를 소환하고 카메라 기즈모를 활성화합니다.\n이 창을 닫으면 소환된 프리뷰 오브젝트는 자동으로 삭제됩니다.", MessageType.Info);
            GUILayout.Space(15);

            // SO 데이터 변경 감지: 할당된 SO가 바뀌면 이미 소환된 더미에도 즉각 반영되도록 처리
            EditorGUI.BeginChangeCheck();
            targetStance = (CameraStancePresetSO)EditorGUILayout.ObjectField("적용할 스탠스 (SO)", targetStance, typeof(CameraStancePresetSO), false);
            if (EditorGUI.EndChangeCheck() && spawnedDummy != null)
            {
                CameraDirectingPreviewer previewer = spawnedDummy.GetComponent<CameraDirectingPreviewer>();
                if (previewer != null) previewer.targetStance = targetStance;
                SceneView.RepaintAll(); // 즉시 기즈모 갱신
            }

            GUILayout.Space(5);
            customModelPrefab = (GameObject)EditorGUILayout.ObjectField("커스텀 모델 (선택)", customModelPrefab, typeof(GameObject), false);

            GUILayout.Space(20);

            // 소환 / 재생성 버튼
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button(spawnedDummy == null ? "🚀 프리뷰 캐릭터 소환" : "🔄 프리뷰 캐릭터 재생성", GUILayout.Height(35)))
            {
                SpawnDummy();
            }
            GUI.backgroundColor = Color.white;

            // 이미 소환된 더미가 있을 때만 수동 삭제 버튼 활성화
            if (spawnedDummy != null)
            {
                GUILayout.Space(5);
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("🗑️ 프리뷰 캐릭터 삭제", GUILayout.Height(25)))
                {
                    DestroyDummy();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        /// <summary>
        /// 씬에 프리뷰용 오브젝트(캡슐 또는 커스텀 모델)를 소환하고 스크립트를 붙입니다.
        /// </summary>
        private void SpawnDummy()
        {
            DestroyDummy(); // 기존에 소환된 것이 있다면 찌꺼기 방지를 위해 삭제

            if (customModelPrefab != null)
            {
                // 유니티 프리팹 연결성을 유지하며 안전하게 소환
                spawnedDummy = (GameObject)PrefabUtility.InstantiatePrefab(customModelPrefab);
                if (spawnedDummy == null) spawnedDummy = Instantiate(customModelPrefab); // Fallback

                spawnedDummy.name = "[Preview Dummy] " + customModelPrefab.name;
            }
            else
            {
                // 커스텀 모델이 없으면 기본 캡슐 생성
                spawnedDummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                spawnedDummy.name = "[Preview Dummy] Capsule";

                // 캡슐이 바닥(0,0,0)에 파묻히지 않게 위로 살짝 올려줍니다.
                spawnedDummy.transform.position = new Vector3(0, 1f, 0);
            }

            // 프리뷰어 컴포넌트 확인 및 부착
            CameraDirectingPreviewer previewer = spawnedDummy.GetComponent<CameraDirectingPreviewer>();
            if (previewer == null)
            {
                previewer = spawnedDummy.AddComponent<CameraDirectingPreviewer>();
            }

            // SO 데이터 주입
            previewer.targetStance = targetStance;

            // 씬 뷰(Scene View)에서 즉시 핸들을 조작할 수 있도록 소환된 오브젝트를 강제 선택(Focus)
            Selection.activeGameObject = spawnedDummy;

            // 씬 카메라가 소환된 오브젝트를 바라보도록 이동
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }

        private void DestroyDummy()
        {
            if (spawnedDummy != null)
            {
                DestroyImmediate(spawnedDummy);
            }
        }

        // ===================================================================
        // 🚨 생명주기 관리: 에디터 툴이 닫히거나 비활성화되면 쓰레기(Dummy)를 자동 청소합니다.
        // ===================================================================
        private void OnDisable()
        {
            DestroyDummy();
        }

        private void OnDestroy()
        {
            DestroyDummy();
        }
    }
}
#endif