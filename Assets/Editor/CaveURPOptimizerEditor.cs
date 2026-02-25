#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CaveSystem.Editor
{
    /// <summary>
    /// 동굴 렌더링 최적화 3단계: Depth Prepass 토글을 위한 에디터 스크립트입니다.
    /// Unity 6 / URP 17에서 설정 위치가 Renderer Data로 이동한 점을 반영하여 수정되었습니다.
    /// </summary>
    public static class CaveURPOptimizerEditor
    {
        [MenuItem("Cave System/Toggle URP Depth Prepass (Opt 3)")]
        public static void ToggleDepthPrepass()
        {
            // 1. 현재 적용된 URP 에셋 가져오기
            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null) urpAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;

            if (urpAsset == null)
            {
                Debug.LogError("[Cave System] 현재 활성화된 URP 에셋을 찾을 수 없습니다.");
                return;
            }

            // 2. URP 에셋에서 사용 중인 'Default Renderer Data' 찾기
            SerializedObject serializedUrpAsset = new SerializedObject(urpAsset);
            SerializedProperty rendererDataListProp = serializedUrpAsset.FindProperty("m_RendererDataList");
            SerializedProperty defaultRendererIndexProp = serializedUrpAsset.FindProperty("m_DefaultRendererIndex");

            if (rendererDataListProp == null || defaultRendererIndexProp == null || rendererDataListProp.arraySize == 0)
            {
                Debug.LogError("[Cave System] URP 에셋에서 Renderer Data 정보를 읽어올 수 없습니다.");
                return;
            }

            int defaultIndex = defaultRendererIndexProp.intValue;
            Object rendererDataAsset = rendererDataListProp.GetArrayElementAtIndex(defaultIndex).objectReferenceValue;

            if (rendererDataAsset == null)
            {
                Debug.LogError("[Cave System] 할당된 Renderer Data 에셋이 없습니다.");
                return;
            }

            // 3. Renderer Data 에셋의 프로퍼티 수정 (여기에 m_DepthPrimingMode가 있습니다)
            SerializedObject serializedRendererData = new SerializedObject(rendererDataAsset);
            SerializedProperty depthPrimingProp = serializedRendererData.FindProperty("m_DepthPrimingMode");

            if (depthPrimingProp == null)
            {
                Debug.LogError($"[Cave System] '{rendererDataAsset.name}' 에셋에서 'm_DepthPrimingMode' 속성을 찾을 수 없습니다. 해당 렌더러가 Depth Priming을 지원하지 않을 수 있습니다.");
                return;
            }

            // 4. 토글 처리 (0: Disabled, 1: Auto, 2: Forced)
            int currentMode = depthPrimingProp.intValue;
            int newMode = (currentMode == 2) ? 0 : 2; // Forced(2)면 Disabled(0)로, 아니면 Forced로.
            string modeName = (newMode == 2) ? "ON (Force Prepass)" : "OFF (Disabled)";

            depthPrimingProp.intValue = newMode;

            // 변경 사항 적용 및 저장
            serializedRendererData.ApplyModifiedProperties();

            // 파일 시스템에 변경사항 강제 기록
            EditorUtility.SetDirty(rendererDataAsset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Cave System] URP 최적화: <b>{rendererDataAsset.name}</b>의 Depth Prepass가 <b>{modeName}</b> 상태로 변경되었습니다.");
        }

        [MenuItem("Cave System/Toggle URP Depth Prepass (Opt 3)", true)]
        public static bool ValidateToggleDepthPrepass()
        {
            return (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset) ||
                   (QualitySettings.renderPipeline is UniversalRenderPipelineAsset);
        }
    }
}
#endif