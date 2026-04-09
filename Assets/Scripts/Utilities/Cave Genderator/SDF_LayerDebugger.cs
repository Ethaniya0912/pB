using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace CaveSystem
{
    /// <summary>
    /// SDF 밀도장 슬라이스 시각화 디버거 v3.
    ///
    /// v3 신규:
    ///   - Y-프로파일: 특정 (X,Z)에서 Y 방향 SDF/hFactor/noiseDamp 로그
    ///   - hFactor 히스토그램: smax 전이대 감쇠 진단
    ///   - 청크 캐시 브라우저: Inspector에서 청크 클릭→프리뷰 전환
    ///   - 씬뷰 Gizmo: 슬라이스/전이대/프로파일 위치 표시
    ///   - 색상: 시안=전이대 정상표면, 주황=전이대+shallow grad
    /// </summary>
    public class SDF_LayerDebugger : MonoBehaviour
    {
        [Header("슬라이스 설정")]
        public float sliceWorldY = -1.0f;
        public bool enableSliceCapture = false;

        [Header("시각화")]
        public float colorRange = 5.0f;
        public float surfaceEpsilon = 0.3f;
        public float gradientWarningThreshold = 0.3f;

        [Header("Y-프로파일")]
        [Tooltip("Y 방향 SDF 프로파일 스캔 활성화")]
        public bool enableYProfile = false;
        [Tooltip("프로파일 월드 XZ 좌표")]
        public Vector2 profileWorldXZ = Vector2.zero;
        [Tooltip("Y 스캔 간격 (복셀 단위)")]
        public int profileYStep = 1;

        [Header("씬뷰 Gizmo")]
        public bool showGizmo = true;
        [Range(0.1f, 0.9f)] public float gizmoAlpha = 0.5f;

        [Header("Floor 파라미터 (CDG 동기화)")]
        public float floorAltitude = -5f;
        public float floorBlendRadius = 4f;
        public float tunnelWidthScale = 0.5f;

        [Header("결과")]
        public Texture2D sliceTexture;
        public Vector3Int lastCapturedChunk;
        public int surfaceVoxelCount;
        public int shallowGradientCount;
        public float sdfMin, sdfMax;

        [Header("정밀 진단 v2")]
        public int thinShellCount;
        public int[] gradHistogram = new int[5];
        public int[] crossingDepthHistogram = new int[5];
        public float yDominantRatio;

        [Header("전이대 진단 v3")]
        public int[] hFactorHistogram = new int[4];
        public float transitionZoneRatio;

        [System.NonSerialized]
        public Dictionary<Vector3Int, SliceCacheEntry> sliceCache =
            new Dictionary<Vector3Int, SliceCacheEntry>();

        [System.Serializable]
        public class SliceCacheEntry
        {
            public Texture2D texture;
            public int surfaceCount, shallowCount;
            public float sdfMin, sdfMax;
            public Vector3 chunkBasePos;
            public int[] gHist, hHist;
            public float transRatio;
            public string summary;
        }

        [System.NonSerialized] public Vector3Int selectedChunk;
        private int _lastN;
        private float _lastVoxelSize;
        private Vector3 _lastChunkBasePos;

        // ── public API ──────────────────────────────────────────────

        public void CaptureSlice(
            ComputeBuffer voxelBuffer, int pointsPerAxis, float voxelSize,
            Vector3 chunkBasePos, Vector3Int chunkPos)
        {
            if (!enableSliceCapture || voxelBuffer == null) return;
            _lastN = pointsPerAxis;
            _lastVoxelSize = voxelSize;

            float localY = sliceWorldY - chunkBasePos.y;
            int sliceIdxY = Mathf.RoundToInt(localY / voxelSize);
            if (sliceIdxY < 0 || sliceIdxY >= pointsPerAxis) return;

            int N = pointsPerAxis;
            int capY = sliceIdxY;
            Vector3Int capChunk = chunkPos;
            Vector3 capBase = chunkBasePos;

            AsyncGPUReadback.Request(voxelBuffer, (req) =>
            {
                if (req.hasError) return;
                var vox = req.GetData<CaveVoxel>();
                BuildSliceTexture(vox, N, capY, voxelSize, capBase, capChunk);
                if (enableYProfile)
                    BuildYProfile(vox, N, voxelSize, capBase, capChunk);
            });
        }

        // ── Y-프로파일 ──────────────────────────────────────────────

        void BuildYProfile(
            Unity.Collections.NativeArray<CaveVoxel> vox,
            int N, float vs, Vector3 basePos, Vector3Int chunkPos)
        {
            int lx = Mathf.RoundToInt((profileWorldXZ.x - basePos.x) / vs);
            int lz = Mathf.RoundToInt((profileWorldXZ.y - basePos.z) / vs);
            if (lx < 0 || lx >= N || lz < 0 || lz >= N) return;

            float ab = Mathf.Max(floorBlendRadius, 0.1f) * tunnelWidthScale;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[SDF Y-Profile] 청크={chunkPos} localXZ=({lx},{lz}) worldXZ=({profileWorldXZ.x:F1},{profileWorldXZ.y:F1})");
            sb.AppendLine($"  {"worldY",7} {"SDF",8} {"hFact",7} {"nDamp",7} {"sign",4} 상태");

            float prev = 0f;
            for (int y = 0; y < N; y += profileYStep)
            {
                float sdf = vox[lx + y * N + lz * N * N].density;
                float wy = basePos.y + y * vs;
                float fc = floorAltitude - wy;
                float h = Mathf.Clamp01(0.5f + 0.5f * (fc - sdf) / Mathf.Max(ab, 0.001f));
                float nd = SS(0f, 0.15f, h) * SS(1f, 0.85f, h);
                bool sc = y > 0 && ((sdf > 0) != (prev > 0));
                string st = "";
                if (Mathf.Abs(sdf) < surfaceEpsilon) st += "★";
                if (sc) st += "⚡";
                if (h > 0.15f && h < 0.85f) st += "T";
                sb.AppendLine($"  {wy,7:F2} {sdf,8:F3} {h,7:F3} {nd,7:F3} {(sdf > 0 ? "+" : "-"),4} {st}");
                prev = sdf;
            }
            Debug.Log(sb.ToString());
        }

        static float SS(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ── 슬라이스 빌드 ───────────────────────────────────────────

        void BuildSliceTexture(
            Unity.Collections.NativeArray<CaveVoxel> vox,
            int N, int sliceY, float vs,
            Vector3 basePos, Vector3Int chunkPos)
        {
            if (sliceTexture == null || sliceTexture.width != N)
            {
                if (sliceTexture != null) Destroy(sliceTexture);
                sliceTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, name = $"SDF_Y{sliceWorldY:F1}" };
            }

            _lastChunkBasePos = basePos;
            surfaceVoxelCount = shallowGradientCount = thinShellCount = 0;
            sdfMin = float.MaxValue; sdfMax = float.MinValue;
            lastCapturedChunk = chunkPos;
            gradHistogram = new int[5];
            crossingDepthHistogram = new int[5];
            hFactorHistogram = new int[4];
            int yDomCnt = 0, transCnt = 0;

            float ab = Mathf.Max(floorBlendRadius, 0.1f) * tunnelWidthScale;
            float wy = basePos.y + sliceY * vs;
            Color[] px = new Color[N * N];

            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int idx = x + sliceY * N + z * N * N;
                float sdf = vox[idx].density;
                if (sdf < sdfMin) sdfMin = sdf;
                if (sdf > sdfMax) sdfMax = sdf;

                // hFactor
                float fc = floorAltitude - wy;
                float h = Mathf.Clamp01(0.5f + 0.5f * (fc - sdf) / Mathf.Max(ab, 0.001f));
                bool inT = h > 0.15f && h < 0.85f;

                // gradient
                float gLen = 0f; float agY = 0f; bool hg = false;
                if (x >= 2 && x < N-2 && z >= 2 && z < N-2 && sliceY >= 2 && sliceY < N-2)
                {
                    hg = true;
                    float gx = (vox[(x+1)+sliceY*N+z*N*N].density - vox[(x-1)+sliceY*N+z*N*N].density)*0.8f
                             + (vox[(x+2)+sliceY*N+z*N*N].density - vox[(x-2)+sliceY*N+z*N*N].density)*0.2f;
                    float gy = (vox[x+(sliceY+1)*N+z*N*N].density - vox[x+(sliceY-1)*N+z*N*N].density)*0.8f
                             + (vox[x+(sliceY+2)*N+z*N*N].density - vox[x+(sliceY-2)*N+z*N*N].density)*0.2f;
                    float gz = (vox[x+sliceY*N+(z+1)*N*N].density - vox[x+sliceY*N+(z-1)*N*N].density)*0.8f
                             + (vox[x+sliceY*N+(z+2)*N*N].density - vox[x+sliceY*N+(z-2)*N*N].density)*0.2f;
                    gLen = Mathf.Sqrt(gx*gx+gy*gy+gz*gz);
                    agY = Mathf.Abs(gy);
                }

                // thin shell
                bool ts = false;
                if (sliceY >= 2 && sliceY < N-2)
                {
                    bool sh = sdf > 0;
                    bool fa = (vox[x+(sliceY+1)*N+z*N*N].density > 0) != sh || (vox[x+(sliceY+2)*N+z*N*N].density > 0) != sh;
                    bool fb = (vox[x+(sliceY-1)*N+z*N*N].density > 0) != sh || (vox[x+(sliceY-2)*N+z*N*N].density > 0) != sh;
                    ts = fa && fb;
                }

                // crossing depth
                float cd = 0f;
                if (x > 0 && x < N-1 && z > 0 && z < N-1 && sliceY > 0 && sliceY < N-1)
                {
                    float mn = 0f, mp = 0f;
                    int[] ddx={1,-1,0,0,0,0}, ddy={0,0,1,-1,0,0}, ddz={0,0,0,0,1,-1};
                    for (int i = 0; i < 6; i++)
                    {
                        float ns = vox[(x+ddx[i])+(sliceY+ddy[i])*N+(z+ddz[i])*N*N].density;
                        if (ns < 0) mn = Mathf.Max(mn, -ns); else mp = Mathf.Max(mp, ns);
                    }
                    cd = Mathf.Min(mn, mp);
                }

                // stats
                float abs = Mathf.Abs(sdf);
                bool surf = abs < surfaceEpsilon;
                if (surf)
                {
                    surfaceVoxelCount++;
                    if (hg && gLen < gradientWarningThreshold && gLen > 0.001f) shallowGradientCount++;
                    if (ts) thinShellCount++;
                    if (hg && gLen > 0.001f && agY > gLen * 0.5f) yDomCnt++;
                    if (inT) transCnt++;
                    if (hg) { if(gLen<0.1f)gradHistogram[0]++;else if(gLen<0.3f)gradHistogram[1]++;else if(gLen<0.5f)gradHistogram[2]++;else if(gLen<1f)gradHistogram[3]++;else gradHistogram[4]++; }
                    if(cd<0.1f)crossingDepthHistogram[0]++;else if(cd<0.5f)crossingDepthHistogram[1]++;else if(cd<2f)crossingDepthHistogram[2]++;else if(cd<5f)crossingDepthHistogram[3]++;else crossingDepthHistogram[4]++;
                    if(h<0.15f)hFactorHistogram[0]++;else if(h<0.5f)hFactorHistogram[1]++;else if(h<0.85f)hFactorHistogram[2]++;else hFactorHistogram[3]++;
                }

                // color
                Color c;
                if (surf)
                {
                    if (ts) c = new Color(1,0,1,1);
                    else if (hg && gLen < gradientWarningThreshold && gLen > 0.001f)
                        c = inT ? new Color(1,0.6f,0,1) : Color.yellow;
                    else if (inT) c = Color.cyan;
                    else c = Color.green;
                }
                else if (sdf < 0) { float t = Mathf.Clamp01(abs/colorRange); c = new Color(0,0,0.3f+t*0.7f,1); }
                else { float t = Mathf.Clamp01(abs/colorRange); c = new Color(0.3f+t*0.7f,0,0,1); }

                px[z*N+x] = c;
            }

            sliceTexture.SetPixels(px);
            sliceTexture.Apply();

            yDominantRatio = surfaceVoxelCount > 0 ? (float)yDomCnt / surfaceVoxelCount : 0f;
            transitionZoneRatio = surfaceVoxelCount > 0 ? (float)transCnt / surfaceVoxelCount : 0f;

            sliceCache[chunkPos] = new SliceCacheEntry
            {
                texture = sliceTexture, surfaceCount = surfaceVoxelCount,
                shallowCount = shallowGradientCount, sdfMin = sdfMin, sdfMax = sdfMax,
                chunkBasePos = basePos, gHist = (int[])gradHistogram.Clone(),
                hHist = (int[])hFactorHistogram.Clone(), transRatio = transitionZoneRatio,
                summary = $"s={surfaceVoxelCount} sh={shallowGradientCount} tr={transitionZoneRatio:P0} [{sdfMin:F1},{sdfMax:F1}]"
            };

            Debug.Log($"[SDF v3] 청크={chunkPos}, Y={sliceWorldY:F1}, idx={sliceY}\n" +
                $"  표면:{surfaceVoxelCount} shallow:{shallowGradientCount} shell:{thinShellCount} Y지배:{yDominantRatio:P0}\n" +
                $"  gLen: [<0.1]={gradHistogram[0]} [0.1-0.3]={gradHistogram[1]} [0.3-0.5]={gradHistogram[2]} [0.5-1]={gradHistogram[3]} [1+]={gradHistogram[4]}\n" +
                $"  교차: [<0.1]={crossingDepthHistogram[0]} [0.1-0.5]={crossingDepthHistogram[1]} [0.5-2]={crossingDepthHistogram[2]} [2-5]={crossingDepthHistogram[3]} [5+]={crossingDepthHistogram[4]}\n" +
                $"  hFactor: [벽<0.15]={hFactorHistogram[0]} [전이lo]={hFactorHistogram[1]} [전이hi]={hFactorHistogram[2]} [바닥>0.85]={hFactorHistogram[3]}\n" +
                $"  전이대비율:{transitionZoneRatio:P0} SDF:[{sdfMin:F3},{sdfMax:F3}]");
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!showGizmo) return;
            // 슬라이스 평면
            Gizmos.color = new Color(0,1,0,0.12f);
            Gizmos.DrawCube(new Vector3(transform.position.x, sliceWorldY, transform.position.z), new Vector3(50,0.05f,50));
            // FloorAltitude
            Gizmos.color = new Color(1,0.5f,0,0.08f);
            Gizmos.DrawCube(new Vector3(transform.position.x, floorAltitude, transform.position.z), new Vector3(50,0.05f,50));
            // 전이대
            float br = Mathf.Max(floorBlendRadius,0.1f)*tunnelWidthScale;
            Gizmos.color = new Color(1,1,0,0.04f);
            Gizmos.DrawCube(new Vector3(transform.position.x, floorAltitude, transform.position.z), new Vector3(50,br*2,50));
            // Y-프로파일
            if (enableYProfile)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(new Vector3(profileWorldXZ.x, floorAltitude-10, profileWorldXZ.y),
                                new Vector3(profileWorldXZ.x, floorAltitude+15, profileWorldXZ.y));
                Gizmos.DrawWireSphere(new Vector3(profileWorldXZ.x, sliceWorldY, profileWorldXZ.y), 0.3f);
            }
        }
#endif
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(SDF_LayerDebugger))]
    public class SDF_LayerDebuggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var d = (SDF_LayerDebugger)target;

            UnityEditor.EditorGUILayout.Space(10);
            UnityEditor.EditorGUILayout.LabelField("슬라이스 프리뷰", UnityEditor.EditorStyles.boldLabel);

            if (d.sliceTexture != null)
            {
                var r = GUILayoutUtility.GetRect(256, 256);
                UnityEditor.EditorGUI.DrawPreviewTexture(r, d.sliceTexture);
                UnityEditor.EditorGUILayout.HelpBox(
                    "녹=정상표면 | 노랑=shallow | 주황=shallow+전이대\n시안=전이대정상 | 마젠타=쉘 | 파랑=공기 | 빨강=고체",
                    UnityEditor.MessageType.Info);

                // gLen
                if (d.gradHistogram?.Length == 5)
                    UnityEditor.EditorGUILayout.LabelField($"gLen: [<.1]={d.gradHistogram[0]} [.1-.3]={d.gradHistogram[1]} [.3-.5]={d.gradHistogram[2]} [.5-1]={d.gradHistogram[3]} [1+]={d.gradHistogram[4]}");
                // crossing
                if (d.crossingDepthHistogram?.Length == 5)
                    UnityEditor.EditorGUILayout.LabelField($"교차: [<.1]={d.crossingDepthHistogram[0]} [.1-.5]={d.crossingDepthHistogram[1]} [.5-2]={d.crossingDepthHistogram[2]} [2-5]={d.crossingDepthHistogram[3]} [5+]={d.crossingDepthHistogram[4]}");
                // hFactor
                if (d.hFactorHistogram?.Length == 4)
                {
                    UnityEditor.EditorGUILayout.LabelField($"hFactor: [벽<.15]={d.hFactorHistogram[0]} [전이lo]={d.hFactorHistogram[1]} [전이hi]={d.hFactorHistogram[2]} [바닥>.85]={d.hFactorHistogram[3]}");
                    if (d.transitionZoneRatio > 0.4f)
                        UnityEditor.EditorGUILayout.HelpBox($"⚠ 표면의 {d.transitionZoneRatio:P0}가 smax 전이대!", UnityEditor.MessageType.Warning);
                }
                UnityEditor.EditorGUILayout.LabelField($"Y지배:{d.yDominantRatio:P0} 전이대:{d.transitionZoneRatio:P0}");
                if (d.thinShellCount > 0)
                    UnityEditor.EditorGUILayout.HelpBox($"⚠ 쉘 {d.thinShellCount}개!", UnityEditor.MessageType.Warning);
            }
            else
            {
                UnityEditor.EditorGUILayout.HelpBox("캡처 대기 중. enableSliceCapture=true 후 Play.", UnityEditor.MessageType.Info);
            }

            // Y 프리셋
            UnityEditor.EditorGUILayout.Space(5);
            UnityEditor.EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Floor-2")) d.sliceWorldY = d.floorAltitude - 2;
            if (GUILayout.Button("Floor")) d.sliceWorldY = d.floorAltitude;
            if (GUILayout.Button("Floor+2")) d.sliceWorldY = d.floorAltitude + 2;
            if (GUILayout.Button("Floor+4")) d.sliceWorldY = d.floorAltitude + 4;
            UnityEditor.EditorGUILayout.EndHorizontal();

            // 청크 캐시 브라우저
            UnityEditor.EditorGUILayout.Space(10);
            UnityEditor.EditorGUILayout.LabelField("청크 캐시", UnityEditor.EditorStyles.boldLabel);
            if (d.sliceCache?.Count > 0)
            {
                UnityEditor.EditorGUILayout.LabelField($"{d.sliceCache.Count}개 캐시됨");
                foreach (var kv in d.sliceCache)
                {
                    bool sel = kv.Key == d.selectedChunk;
                    string lb = (sel ? "► " : "") + $"{kv.Key} {kv.Value.summary}";
                    if (GUILayout.Button(lb, sel ? UnityEditor.EditorStyles.boldLabel : UnityEditor.EditorStyles.miniButton))
                    {
                        d.selectedChunk = kv.Key;
                        d.sliceTexture = kv.Value.texture;
                        d.surfaceVoxelCount = kv.Value.surfaceCount;
                        d.shallowGradientCount = kv.Value.shallowCount;
                        d.sdfMin = kv.Value.sdfMin;
                        d.sdfMax = kv.Value.sdfMax;
                        d.gradHistogram = kv.Value.gHist;
                        d.hFactorHistogram = kv.Value.hHist;
                        d.transitionZoneRatio = kv.Value.transRatio;
                        UnityEditor.EditorUtility.SetDirty(d);
                    }
                }
            }

            // 플레이어 위치로 프로파일 설정
            UnityEditor.EditorGUILayout.Space(5);
            if (GUILayout.Button("SceneView 카메라 위치 → 프로파일 XZ"))
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null)
                {
                    var cp = sv.camera.transform.position;
                    d.profileWorldXZ = new Vector2(cp.x, cp.z);
                    d.enableYProfile = true;
                    UnityEditor.EditorUtility.SetDirty(d);
                }
            }
        }
    }
#endif
}
