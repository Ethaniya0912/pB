// ============================================================================
// BufferDiagnostic.cs
// 
// 사용법:
//   1. 이 파일을 Assets/Scripts 하위 아무 곳에 추가
//   2. 씬의 아무 GameObject (예: CaveManager) 에 컴포넌트로 부착
//   3. 플레이 모드 진입
//   4. F9 키 눌러 현재 상태 스냅샷 로그
//   5. 5분 후 다시 F9 눌러 비교
//   6. F10 키로 강제 GC + 스냅샷 (GC 후 진짜 살아있는 객체 확인)
//
// 출력:
//   - 활성 청크 수
//   - Mesh 객체 총 수
//   - Texture2D 객체 총 수
//   - NormalBaker pending bakes 수
//   - DCMeshBuilder pending physics bakes 수
//   - Stitch 큐 크기
//
// 누수 판정 기준:
//   - pending bakes > 50 → DisposePendingBake 호출 누락 의심
//   - Mesh 수 > activeChunks × 3 → ReturnToPool 이름 체크 실패 의심
//   - Texture2D 수 > activeChunks × 3 × 2 → NormalBaker 이전 텍스처 누수
//   - GC 전후 차이 > 50% → 관리되지 않는 참조 과다
// ============================================================================

using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace CaveSystem
{
    public class BufferDiagnostic : MonoBehaviour
    {
        [Tooltip("F9 스냅샷 키")]
        public KeyCode snapshotKey = KeyCode.F9;
        [Tooltip("F10 GC 강제 후 스냅샷 키")]
        public KeyCode gcSnapshotKey = KeyCode.F10;

        private int _lastMeshCount = -1;
        private int _lastTextureCount = -1;
        private int _lastPendingBakes = -1;
        private int _lastPendingPhysics = -1;

        private void Update()
        {
            if (Input.GetKeyDown(snapshotKey)) Snapshot(forceGC: false);
            if (Input.GetKeyDown(gcSnapshotKey)) Snapshot(forceGC: true);
        }

        private void Snapshot(bool forceGC)
        {
            if (forceGC)
            {
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                System.GC.Collect();
                // Unity Resources.UnloadUnusedAssets는 비동기여야 하지만 진단용이므로 skip
            }

            // ── Unity 객체 카운트 ──
            int meshCount = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int texture2DCount = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            int renderTextureCount = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;

            // ── CaveNormalBaker pending ──
            int pendingBakes = -1;
            var baker = CaveNormalBaker.Instance;
            if (baker != null)
            {
                var field = typeof(CaveNormalBaker).GetField(
                    "_pendingBakes",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var list = field?.GetValue(baker) as System.Collections.IList;
                pendingBakes = list?.Count ?? -1;
            }

            // ── DCMeshBuilder pending physics ──
            int pendingPhysics = -1;
            int stitchQueueSize = -1;
            var mb = DCMeshBuilder.Instance;
            if (mb != null)
            {
                var fieldPhy = typeof(DCMeshBuilder).GetField(
                    "_pendingPhysicsBakes",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var listPhy = fieldPhy?.GetValue(mb) as System.Collections.IList;
                pendingPhysics = listPhy?.Count ?? -1;

                var fieldStitch = typeof(DCMeshBuilder).GetField(
                    "_stitchQueue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var qStitch = fieldStitch?.GetValue(mb) as System.Collections.ICollection;
                stitchQueueSize = qStitch?.Count ?? -1;
            }

            // ── ChunkManager activeChunks ──
            int activeChunks = -1;
            int chunkPool = -1;
            int coarseObjects = -1;
            var cm = CaveManager.Instance?.chunkManager;
            if (cm != null)
            {
                var cmType = cm.GetType();
                var fieldActive = cmType.GetField(
                    "activeChunks",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var dict = fieldActive?.GetValue(cm) as System.Collections.IDictionary;
                activeChunks = dict?.Count ?? -1;

                var fieldPool = cmType.GetField(
                    "chunkPool",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var q = fieldPool?.GetValue(cm) as System.Collections.ICollection;
                chunkPool = q?.Count ?? -1;

                var fieldCoarse = cmType.GetField(
                    "coarseObjects",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var dictCoarse = fieldCoarse?.GetValue(cm) as System.Collections.IDictionary;
                coarseObjects = dictCoarse?.Count ?? -1;
            }

            // ── Δ (이전 스냅샷 대비) ──
            string delta(int cur, int prev)
            {
                if (prev < 0) return "";
                int d = cur - prev;
                if (d > 0) return $" (+{d})";
                if (d < 0) return $" ({d})";
                return " (=)";
            }

            // ── 로그 출력 ──
            string tag = forceGC ? "[Diag+GC]" : "[Diag]";
            Debug.Log($"{tag} ================================");
            Debug.Log($"{tag} ActiveChunks:     {activeChunks}");
            Debug.Log($"{tag} ChunkPool:        {chunkPool}");
            Debug.Log($"{tag} CoarseObjects:    {coarseObjects}");
            Debug.Log($"{tag} PendingBakes:     {pendingBakes}{delta(pendingBakes, _lastPendingBakes)}");
            Debug.Log($"{tag} PendingPhysics:   {pendingPhysics}{delta(pendingPhysics, _lastPendingPhysics)}");
            Debug.Log($"{tag} StitchQueue:      {stitchQueueSize}");
            Debug.Log($"{tag} Mesh objects:     {meshCount}{delta(meshCount, _lastMeshCount)}");
            Debug.Log($"{tag} Texture2D:        {texture2DCount}{delta(texture2DCount, _lastTextureCount)}");
            Debug.Log($"{tag} RenderTexture:    {renderTextureCount}");

            // ── 누수 경고 ──
            if (activeChunks > 0)
            {
                int expectedMesh = activeChunks * 3; // 대략 활성 × 2~3
                if (meshCount > expectedMesh * 2)
                    Debug.LogWarning($"{tag} ⚠ Mesh 과다: {meshCount} (예상 ≤{expectedMesh * 2}) — ReturnToPool 이름 체크 실패 의심");
                
                int expectedTex = activeChunks * 6;
                if (texture2DCount > expectedTex * 2)
                    Debug.LogWarning($"{tag} ⚠ Texture2D 과다: {texture2DCount} (예상 ≤{expectedTex * 2}) — NormalBaker 이전 텍스처 Destroy 누락");
            }
            if (pendingBakes > 30)
                Debug.LogWarning($"{tag} ⚠ NormalBaker pending 누적: {pendingBakes} — DisposePendingBake 호출 실패 의심");
            if (pendingPhysics > 30)
                Debug.LogWarning($"{tag} ⚠ Physics Bake pending 누적: {pendingPhysics}");
            if (stitchQueueSize > 30)
                Debug.LogWarning($"{tag} ⚠ Stitch Queue 적체: {stitchQueueSize}");

            _lastMeshCount = meshCount;
            _lastTextureCount = texture2DCount;
            _lastPendingBakes = pendingBakes;
            _lastPendingPhysics = pendingPhysics;
        }

        private void OnGUI()
        {
            // 화면 좌상단 즉시 확인용 미니 오버레이
            int pending = -1;
            int phys = -1;
            int activeC = -1;
            var baker = CaveNormalBaker.Instance;
            if (baker != null)
            {
                var field = typeof(CaveNormalBaker).GetField(
                    "_pendingBakes", BindingFlags.NonPublic | BindingFlags.Instance);
                var list = field?.GetValue(baker) as System.Collections.IList;
                pending = list?.Count ?? -1;
            }
            var mb = DCMeshBuilder.Instance;
            if (mb != null)
            {
                var field = typeof(DCMeshBuilder).GetField(
                    "_pendingPhysicsBakes", BindingFlags.NonPublic | BindingFlags.Instance);
                var list = field?.GetValue(mb) as System.Collections.IList;
                phys = list?.Count ?? -1;
            }
            var cm = CaveManager.Instance?.chunkManager;
            if (cm != null)
            {
                var field = cm.GetType().GetField(
                    "activeChunks", BindingFlags.NonPublic | BindingFlags.Instance);
                var dict = field?.GetValue(cm) as System.Collections.IDictionary;
                activeC = dict?.Count ?? -1;
            }
            GUI.Label(new Rect(10, 10, 300, 80),
                $"[Diag] Active: {activeC}  NBake:{pending}  PhyBake:{phys}\nF9=snapshot  F10=GC+snapshot");
        }
    }
}
