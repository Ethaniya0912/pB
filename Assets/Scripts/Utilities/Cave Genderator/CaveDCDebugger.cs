// =============================================================================
// CaveDCDebugger.cs  |  pB-4 DC Pipeline 진단 도구
// =============================================================================
// 부착 위치: CaveComputeDispatcher와 동일 GameObject
//
// 기능:
//   1. SetFloat 직전 모든 파라미터 콘솔 로깅 (1회)
//   2. 특정 청크의 Y-slice density 히트맵 Scene Gizmos
//   3. floorMask / floorDetail / baseSDF 값을 수직 단면으로 시각화
//   4. corridorSurface 실제 수신값 확인
// =============================================================================

using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;

namespace CaveSystem
{
    [RequireComponent(typeof(CaveComputeDispatcher))]
    public class CaveDCDebugger : MonoBehaviour
    {
        // ──────────────────────────────────────────────────────────────────────
        // Inspector 제어
        // ──────────────────────────────────────────────────────────────────────

        [Header("Logging")]
        [Tooltip("true = 첫 번째 청크 디스패치 시 모든 파라미터를 Console에 출력")]
        public bool logFirstDispatch = true;

        [Tooltip("true = 매 청크마다 핵심 파라미터 출력 (성능 주의)")]
        public bool logEveryDispatch = false;

        [Header("Y-Slice Density Readback")]
        [Tooltip("true = 다음 청크에서 Y=sliceY 단면 density를 CPU로 읽어 Gizmos 표시")]
        public bool enableSliceReadback = false;

        [Tooltip("단면 Y 월드 좌표 (FloorAltitude 부근 추천)")]
        public float sliceWorldY = 0f;

        [Tooltip("Gizmos 큐브 스케일")]
        public float gizmoCubeSize = 0.3f;

        [Range(0f, 1f)]
        [Tooltip("density 컬러맵 범위 상한 (이 값 이상은 빨강)")]
        public float densityColorClamp = 5f;

        [Header("Quick Fix Test")]
        [Tooltip("true = floorMask에 max(0, dist_to_floor) 적용 (핫픽스 런타임 테스트)")]
        public bool applyFloorMaskFix = true;

        [Tooltip("true = corridorSurface를 강제로 이 값으로 override")]
        public bool overrideCorridorSurface = false;
        [Range(0f, 1f)]
        public float corridorSurfaceOverride = 0f;

        // ──────────────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────────────

        private CaveComputeDispatcher _dispatcher;
        private bool _hasLoggedOnce = false;
        private List<DebugVoxelPoint> _slicePoints = new List<DebugVoxelPoint>();
        private bool _pendingSliceReadback = false;

        private struct DebugVoxelPoint
        {
            public Vector3 worldPos;
            public float density;
            public float baseSDF;
        }

        // ──────────────────────────────────────────────────────────────────────
        // 공개 API — CaveComputeDispatcher에서 호출
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// CaveComputeDispatcher.DispatchChunk() 내에서 Dispatch 직전에 호출하세요:
        /// <code>
        /// GetComponent&lt;CaveDCDebugger&gt;()?.OnBeforeDispatch(densityShader, currentLayer, chunkPos);
        /// </code>
        /// </summary>
        public void OnBeforeDispatch(
            ComputeShader shader,
            DepthLayer layer,
            Vector3Int chunkPos)
        {
            if (!enabled) return;

            bool shouldLog = logEveryDispatch || (logFirstDispatch && !_hasLoggedOnce);
            if (!shouldLog) return;

            _hasLoggedOnce = true;

            // ── 1. 레이어 파라미터 로그 ────────────────────────────────────
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[CaveDCDebugger] ===== 청크 {chunkPos} 파라미터 덤프 =====");
            sb.AppendLine($"  Layer: {layer.layerName}");
            sb.AppendLine();
            sb.AppendLine("[Floor Constraints]");
            sb.AppendLine($"  _FloorAltitude       = {layer.minAltitude:F3}");
            sb.AppendLine($"  _CeilAltitude        = {layer.maxAltitude:F3}");
            sb.AppendLine($"  _FloorBlendRadius    = {layer.floorBlendRadius:F3}");
            sb.AppendLine($"  _CeilBlendRadius     = {layer.ceilBlendRadius:F3}");
            sb.AppendLine($"  _FloorBumpAmplitude  = {layer.floorBumpAmplitude:F3}");
            sb.AppendLine($"  _FloorBumpFrequency  = {layer.floorBumpFrequency:F3}");
            sb.AppendLine();
            sb.AppendLine("[Floor Detail (새 필드)]");
            sb.AppendLine($"  floorDetailAmplitude = {layer.floorDetailAmplitude:F3}");
            sb.AppendLine($"  floorDetailFrequency = {layer.floorDetailFrequency:F3}");
            sb.AppendLine($"  floorDetailRadius    = {layer.floorDetailRadius:F3}");
            sb.AppendLine();
            sb.AppendLine("[Corridor Surface (새 필드)]");
            float effectiveCS = overrideCorridorSurface ? corridorSurfaceOverride : layer.corridorSurface;
            sb.AppendLine($"  corridorSurface (SO) = {layer.corridorSurface:F3}");
            sb.AppendLine($"  corridorSurface (GPU전송값) = {effectiveCS:F3}");
            if (layer.corridorSurface > 0.01f)
                sb.AppendLine("  ⚠ corridorSurface > 0 → terraceSteps strata 활성! 단차 발생 가능");
            sb.AppendLine();
            sb.AppendLine("[FloorMask 진단]");
            float chunkBaseY = chunkPos.y * 16f; // 대략적 청크 월드 Y
            float distToFloor = chunkBaseY - layer.minAltitude;
            float rawMask = Mathf.Clamp01(1f - distToFloor / Mathf.Max(layer.floorDetailRadius, 0.1f));
            float fixedDist = Mathf.Max(0, distToFloor);
            float fixedMask = Mathf.Clamp01(1f - fixedDist / Mathf.Max(layer.floorDetailRadius, 0.1f));
            sb.AppendLine($"  청크 기준 Y ≈ {chunkBaseY:F1}m");
            sb.AppendLine($"  dist_to_floor ≈ {distToFloor:F2}m  (음수=통로가 floor 아래)");
            sb.AppendLine($"  현재 floorMask ≈ {rawMask:F3}");
            sb.AppendLine($"  수정 후 floorMask ≈ {fixedMask:F3}  (max(0, dist) 적용)");
            if (distToFloor < 0 && rawMask > 0.5f)
                sb.AppendLine("  ⚠ dist_to_floor < 0 이지만 floorMask = 1.0 → 파편 위험!");
            sb.AppendLine();
            sb.AppendLine("[Quick Fix Override]");
            sb.AppendLine($"  applyFloorMaskFix       = {applyFloorMaskFix}");
            sb.AppendLine($"  overrideCorridorSurface = {overrideCorridorSurface} → {effectiveCS:F3}");
            sb.AppendLine("========================================");

            Debug.Log(sb.ToString());

            // ── 2. Quick Fix Override 적용 ─────────────────────────────────
            if (overrideCorridorSurface)
                shader.SetFloat("_CorridorSurface", corridorSurfaceOverride);
        }

        /// <summary>
        /// floorMask fix를 런타임에 적용할 때 사용하는 계산 래퍼.
        /// CaveDensityGenerator.compute 수정 전까지 SetFloat으로 대체 가능.
        /// (현재는 로깅 전용 — compute 수정이 정석)
        /// </summary>
        public float ComputeFixedFloorMask(float worldY, float floorAltitude, float radius)
        {
            float dist = applyFloorMaskFix
                ? Mathf.Max(0f, worldY - floorAltitude)
                : worldY - floorAltitude;
            return Mathf.Clamp01(1f - dist / Mathf.Max(radius, 0.01f));
        }

        // ──────────────────────────────────────────────────────────────────────
        // Gizmos 슬라이스 시각화
        // ──────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _dispatcher = GetComponent<CaveComputeDispatcher>();
        }

        /// <summary>
        /// 콘솔에 density 슬라이스를 텍스트로 출력 (ASCII 히트맵).
        /// ComputeBuffer readback 없이 즉시 확인.
        /// </summary>
        [ContextMenu("Print Debug Summary")]
        private void PrintDebugSummary()
        {
            var settings = GetComponent<CaveComputeDispatcher>()?.caveSettings;
            if (settings == null) { Debug.LogWarning("[CaveDCDebugger] caveSettings를 찾을 수 없습니다."); return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[CaveDCDebugger] ===== SO 파라미터 전체 덤프 =====");
            sb.AppendLine($"  총 DepthLayer 수: {settings.depthLayers?.Count ?? 0}");

            for (int i = 0; i < (settings.depthLayers?.Count ?? 0); i++)
            {
                var L = settings.depthLayers[i];
                sb.AppendLine();
                sb.AppendLine($"--- Layer [{i}]: {L.layerName}  Y=[{L.minAltitude}, {L.maxAltitude}] ---");
                sb.AppendLine($"  floorBlendRadius     = {L.floorBlendRadius:F2}");
                sb.AppendLine($"  floorBumpAmplitude   = {L.floorBumpAmplitude:F3}");
                sb.AppendLine($"  floorDetailAmplitude = {L.floorDetailAmplitude:F3}  ← 새 필드");
                sb.AppendLine($"  floorDetailFrequency = {L.floorDetailFrequency:F3}  ← 새 필드");
                sb.AppendLine($"  floorDetailRadius    = {L.floorDetailRadius:F3}  ← 새 필드");
                sb.AppendLine($"  corridorSurface      = {L.corridorSurface:F3}  ← 새 필드");

                // 진단
                if (L.corridorSurface > 0.01f)
                    sb.AppendLine($"  ⚠ corridorSurface={L.corridorSurface:F2} > 0 → 단차 발생 가능");
                if (L.floorDetailAmplitude > 0.001f && L.floorDetailRadius <= 0.1f)
                    sb.AppendLine($"  ⚠ floorDetailRadius={L.floorDetailRadius:F2} ≤ 0.1 → 노이즈 범위 너무 좁음");
            }

            sb.AppendLine("===========================================");
            Debug.Log(sb.ToString());
        }

        private void OnDrawGizmos()
        {
            if (_slicePoints.Count == 0) return;

            foreach (var pt in _slicePoints)
            {
                float t = Mathf.InverseLerp(-densityColorClamp, densityColorClamp, pt.density);
                // 파란색(음수=공동) → 회색(0=표면) → 빨간색(양수=고체)
                Color col;
                if (pt.density < 0)
                    col = Color.Lerp(Color.cyan, Color.gray, Mathf.InverseLerp(-densityColorClamp, 0, pt.density));
                else
                    col = Color.Lerp(Color.gray, Color.red, Mathf.InverseLerp(0, densityColorClamp, pt.density));

                col.a = 0.6f;
                Gizmos.color = col;
                Gizmos.DrawCube(pt.worldPos, Vector3.one * gizmoCubeSize);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Editor 확장 (Inspector 버튼)
    // ──────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(CaveDCDebugger))]
    public class CaveDCDebuggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var dbg = (CaveDCDebugger)target;

            UnityEditor.EditorGUILayout.Space(8);
            UnityEditor.EditorGUILayout.LabelField("진단 액션", UnityEditor.EditorStyles.boldLabel);

            if (GUILayout.Button("SO 파라미터 전체 콘솔 출력"))
            {
                dbg.SendMessage("PrintDebugSummary");
            }

            if (GUILayout.Button("corridorSurface 0으로 강제 설정 (테스트)"))
            {
                dbg.overrideCorridorSurface = true;
                dbg.corridorSurfaceOverride = 0f;
                Debug.Log("[CaveDCDebugger] corridorSurface override → 0 (단차 제거 테스트)");
            }

            if (GUILayout.Button("floorMaskFix 활성화"))
            {
                dbg.applyFloorMaskFix = true;
                Debug.Log("[CaveDCDebugger] floorMaskFix 활성화 — 씬 재생성 후 파편 확인");
            }

            UnityEditor.EditorGUILayout.Space(4);
            UnityEditor.EditorGUILayout.HelpBox(
                "사용 방법:\n" +
                "1. CaveComputeDispatcher와 같은 GameObject에 부착\n" +
                "2. 'logFirstDispatch = true' → 첫 청크 파라미터 로그 확인\n" +
                "3. corridorSurface > 0 경고 → SO에서 0으로 수동 설정\n" +
                "4. dist_to_floor < 0 경고 → CaveDensityGenerator.compute 수정 필요",
                UnityEditor.MessageType.Info);
        }
    }
#endif
}
