#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-D 제안 8] PreviewRealtimeWatcher
    //
    // 역할:
    //   SO(CaveBiomeSettings / CaveBiomeData / RoomGeometryMatrix / Edge / Bias)를
    //   Inspector에서 수정하면 Preview Editor가 자동으로 Generate 호출.
    //
    // 구현 전략:
    //   1. AssetModificationProcessor.OnWillSaveAssets → SO 저장 감지
    //   2. EditorApplication.update로 debounce (150ms 지연 후 regenerate)
    //   3. GraphPreviewWindow가 활성 상태일 때만 작동
    //   4. 사용자 토글 (inputs.realtimeSOInspector) on/off
    //
    // 성능:
    //   - Debounce 150ms: 슬라이더 드래그 중엔 재생성 안 함 (마지막 저장 후 150ms)
    //   - Path 필터: CaveSystem SO assets만 trigger
    //   - 토글 OFF 시 완전히 비활성화 (이벤트 구독 X)
    // ══════════════════════════════════════════════════════════════════════

    public class PreviewRealtimeWatcher : AssetModificationProcessor
    {
        // 마지막 저장 감지 시각 (debounce 기준)
        private static double _lastChangeTime = -1.0;
        private static bool _pendingRegenerate = false;
        private const double DEBOUNCE_SECONDS = 0.15;

        // GraphPreviewWindow가 자체 등록하여 참조 보관
        private static System.Action _regenerateCallback;

        /// <summary>GraphPreviewWindow에서 호출 — realtime regenerate 콜백 등록.</summary>
        public static void RegisterCallback(System.Action callback)
        {
            _regenerateCallback = callback;
            // EditorApplication.update 구독 (한 번만)
            EditorApplication.update -= OnTick;
            EditorApplication.update += OnTick;
        }

        public static void UnregisterCallback()
        {
            _regenerateCallback = null;
            EditorApplication.update -= OnTick;
            _pendingRegenerate = false;
        }

        /// <summary>AssetDatabase가 asset 저장 직전 호출. 우리가 관심있는 SO 타입만 trigger.</summary>
        public static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null || paths.Length == 0) return paths;

            foreach (var path in paths)
            {
                if (IsRelevantAssetPath(path))
                {
                    _lastChangeTime = EditorApplication.timeSinceStartup;
                    _pendingRegenerate = true;
                    break;
                }
            }
            return paths;
        }

        /// <summary>
        /// CaveSystem에서 관심 있는 SO 경로인지 판단.
        /// 파일 내용을 열지 않고 type 확인만 하려면 경로/타입 based 필터 필요.
        /// </summary>
        private static bool IsRelevantAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.EndsWith(".asset")) return false;

            // Type 확인
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null) return false;

            return type == typeof(CaveBiomeSettings)
                || type == typeof(CaveBiomeData)
                || type == typeof(RoomGeometryMatrixSO)
                || type == typeof(EdgeConstraintSO)
                || type == typeof(CardinalBiomeBiasSO);
        }

        /// <summary>EditorApplication.update 콜백. Debounce 체크 후 regenerate.</summary>
        private static void OnTick()
        {
            if (!_pendingRegenerate) return;
            if (_regenerateCallback == null) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastChangeTime < DEBOUNCE_SECONDS) return;

            _pendingRegenerate = false;
            try
            {
                _regenerateCallback();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PreviewRealtimeWatcher] Regenerate callback failed: {e.Message}");
            }
        }
    }
}
#endif
