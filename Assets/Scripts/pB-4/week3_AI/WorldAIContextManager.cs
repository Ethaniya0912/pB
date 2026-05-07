// ─────────────────────────────────────────────────────────────────────
// pB-4 — WorldAIContextManager (v6 ★ Phase 5 — FakeContextRegion 통합)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-05-04 v6 — 가짜 영역 마킹 시스템 (Phase 5 시각 검증 도구)
//   v5 → v6 변경 사항:
//     [추가] FakeContextRegion struct + FakeRegionShape + FakeOverrideMode enum
//     [추가] _fakeRegions 리스트 + 공용 API (Add/Remove/Clear/Get/Find)
//     [추가] Sample() 의 가짜 영역 적용 단계 (자연 비트 → 가짜 OR/Replace)
//     [추가] OnDrawGizmos — 활성 가짜 영역 시각화 (런타임 디버그용)
//     [보존] v5 의 모든 기능 (DontDestroyOnLoad / Inject / 16 필드 / Helper)
//
// 위치: Scripts/pB-4/week3_AI/WorldAIContextManager.cs
//
// ★ 사용법:
//   런타임 또는 Editor 에서 WorldAIContextManager.Instance.AddFakeRegion(region)
//   → 영역 안 NPC 가 다음 Sample 호출 시 가짜 비트 받음
//   → BlackboardUpdater 가 BB 갱신 → BaseAIBrain.UpdateFearFromTerrain
//   → fear 즉시 반응 → BG editor 의사결정 변경 → 행동 시각화
//
// ★ 신설 도구: FakeContextInjectorWindow (Window > pB-4 > Fake Context Injector)
//   마우스 클릭으로 영역 배치 + 60 비트 정밀 토글 + 프리셋
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TDA.PB4.Interfaces;
using TDA.PB4.Interfaces.Narrative;
using CaveSystem;

namespace TDA.PB4.AI.Context
{
    // ═════════════════════════════════════════════════════════════════
    // ★ Phase 5 — 가짜 영역 데이터 구조
    // ═════════════════════════════════════════════════════════════════

    /// <summary>가짜 영역의 형태.</summary>
    public enum FakeRegionShape
    {
        /// <summary>구체 — center + radius.</summary>
        Sphere,
        /// <summary>직선 통로 — center + direction + length + width (NarrowPath 같은).</summary>
        Line,
        /// <summary>박스 — center + size (방 / 구역).</summary>
        Box
    }

    /// <summary>가짜 비트의 적용 방식.</summary>
    public enum FakeOverrideMode
    {
        /// <summary>자연 비트 + 가짜 비트 (OR 누적). 영역 진입 시 자연 + 추가.</summary>
        OR,
        /// <summary>자연 비트 무시, 가짜 비트만 (덮어쓰기). 영역 안에서는 인공 환경.</summary>
        Replace
    }

    /// <summary>
    /// 가짜 환경 영역 — 마우스로 배치, NPC 가 진입하면 가짜 비트마스크 부여.
    /// FakeContextInjectorWindow EditorWindow 가 이 struct 인스턴스를 생성/편집.
    /// </summary>
    [System.Serializable]
    public struct FakeContextRegion
    {
        // ─── 식별 ─────────────────────────────────────────────────────
        public string id;            // 디버그 / 삭제용 (예: "AmbushZone_01")
        public bool active;          // 일시 비활성화 토글

        // ─── 형태 ─────────────────────────────────────────────────────
        public FakeRegionShape shape;
        public Vector3 center;       // 마우스 클릭 좌표

        // Sphere 용
        public float radius;

        // Box 용 (size = 전체 가로/세로/깊이)
        public Vector3 size;

        // Line 용 (direction = 정규화된 방향, length = m, width = m)
        public Vector3 direction;
        public float length;
        public float width;

        // ─── 가짜 비트마스크 (4 카테고리 60 비트) ────────────────────
        public uint identityTags;
        public uint stateTags;
        public uint eventTags;
        public uint potentialTags;

        // ─── 적용 방식 ────────────────────────────────────────────────
        public FakeOverrideMode overrideMode;

        // ─── 헬퍼 ─────────────────────────────────────────────────────

        /// <summary>월드 좌표가 본 영역 안에 있는지 검사.</summary>
        public bool ContainsPoint(Vector3 worldPos)
        {
            if (!active) return false;

            switch (shape)
            {
                case FakeRegionShape.Sphere:
                    return (worldPos - center).sqrMagnitude <= radius * radius;

                case FakeRegionShape.Box:
                    {
                        Vector3 d = worldPos - center;
                        Vector3 h = size * 0.5f;
                        return Mathf.Abs(d.x) <= h.x
                            && Mathf.Abs(d.y) <= h.y
                            && Mathf.Abs(d.z) <= h.z;
                    }

                case FakeRegionShape.Line:
                    {
                        Vector3 dir = direction.sqrMagnitude > 0.0001f
                            ? direction.normalized
                            : Vector3.forward;
                        Vector3 toPos = worldPos - center;
                        float along = Vector3.Dot(toPos, dir);
                        if (Mathf.Abs(along) > length * 0.5f) return false;

                        Vector3 perp = toPos - dir * along;
                        return perp.sqrMagnitude <= width * width * 0.25f;
                    }
            }

            return false;
        }

        /// <summary>편의 — Sphere 영역 빠른 생성.</summary>
        public static FakeContextRegion CreateSphere(string id, Vector3 center, float radius)
        {
            return new FakeContextRegion
            {
                id = id,
                active = true,
                shape = FakeRegionShape.Sphere,
                center = center,
                radius = Mathf.Max(0.5f, radius),
                size = Vector3.one * radius,           // Box 호환 기본값
                direction = Vector3.forward,
                length = radius * 2f,
                width = radius,
                overrideMode = FakeOverrideMode.OR,
            };
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // 본체 — WorldAIContextManager v6
    // ═════════════════════════════════════════════════════════════════

    public class WorldAIContextManager : MonoBehaviour, IContextProvider
    {
        public static WorldAIContextManager Instance { get; private set; }

        [Header("★ 라이프사이클")]
        [Tooltip("ON → 씬 전환 시 본 GameObject 보존 (메뉴 씬 → 본 씬 흐름). " +
                 "OFF → 씬 전용 (단독 Play 검증용)")]
        [SerializeField] private bool _persistAcrossScenes = true;

        [Tooltip("ON → 씬 전환 시 Inject 상태 무효화 (재 Inject 강제). " +
                 "OFF → 한 번 Inject 되면 영구 보존")]
        [SerializeField] private bool _resetInjectOnSceneChange = true;

        [Header("★ Phase 5 — 가짜 영역")]
        [Tooltip("ON → OnDrawGizmos 에 활성 가짜 영역 표시. OFF → 숨김.")]
        [SerializeField] private bool _drawFakeRegionGizmos = true;

        [Tooltip("ON → 가짜 영역 적용 시 verbose 로그 출력.")]
        [SerializeField] private bool _logFakeRegionHits = false;

        private const float DEFAULT_MORALE_NO_GROUP = 1.0f;
        private const bool DEFAULT_TOKEN_NO_GROUP = true;
        private const float EDGE_PROXIMITY_THRESHOLD = 5.0f;
        private const float DEFAULT_PATH_WIDTH = 10.0f;

        private IJunctionContextAnalyzer _terrain;
        private CaveNodeGraphBuilder _graph;
        private IIncidentRecorder _recorder;
        private IGroupAIInfo _group;

        private bool _isInjected = false;

        // ★ Phase 5 — 가짜 영역 리스트 (런타임 + Editor 공용)
        private readonly List<FakeContextRegion> _fakeRegions = new List<FakeContextRegion>();

        [Header("디버깅")]
        [SerializeField] private bool _verboseLogging = false;

        public bool IsInjected => _isInjected;

        /// <summary>★ Phase 5 — 현재 활성 가짜 영역 개수.</summary>
        public int FakeRegionCount => _fakeRegions.Count;

        // ─────────────────────────────────────────────────────────────
        // 라이프사이클
        // ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[WorldAIContextManager] 중복 인스턴스 감지 — 본 GameObject 제거");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (_persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
                Debug.Log("[WorldAIContextManager] DontDestroyOnLoad 적용 (메뉴 씬 → 본 씬 흐름)");
            }

            if (_resetInjectOnSceneChange)
            {
                SceneManager.sceneLoaded += OnSceneLoadedResetInject;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoadedResetInject;
        }

        private void OnSceneLoadedResetInject(Scene scene, LoadSceneMode mode)
        {
            if (!_resetInjectOnSceneChange) return;
            if (mode != LoadSceneMode.Single) return;

            if (_isInjected)
            {
                Debug.Log($"[WorldAIContextManager] sceneLoaded \"{scene.name}\" — " +
                          "기존 Inject 무효화 (TerrainContextAnalyzer destroy 가능성). " +
                          "AIContextBootstrapper 가 재 Inject 호출 대기.");
                _isInjected = false;
                _terrain = null;
                _graph = null;
                _recorder = null;
                _group = null;
            }
            // ★ Phase 5 — 씬 전환 시 가짜 영역도 클리어 (선택적)
            // 사용자 의도에 따라 보존하려면 본 라인 주석 처리
            _fakeRegions.Clear();
        }

        // ─────────────────────────────────────────────────────────────
        // Inject
        // ─────────────────────────────────────────────────────────────

        public void Inject(
            IJunctionContextAnalyzer terrain,
            CaveNodeGraphBuilder graph,
            IIncidentRecorder recorder,
            IGroupAIInfo group)
        {
            _terrain = terrain;
            _graph = graph;
            _recorder = recorder;
            _group = group;
            _isInjected = true;

            Debug.Log("[WorldAIContextManager] Inject 완료 (★ Tier 2): " +
                      $"terrain={terrain != null}, graph={graph != null}, " +
                      $"recorder={recorder != null}, group={group != null}");
        }

        // ─────────────────────────────────────────────────────────────
        // ★ Phase 5 — 가짜 영역 공용 API
        // ─────────────────────────────────────────────────────────────

        /// <summary>가짜 영역 추가. 같은 id 가 있으면 덮어쓰기.</summary>
        public void AddFakeRegion(FakeContextRegion region)
        {
            if (string.IsNullOrEmpty(region.id))
            {
                region.id = $"FakeRegion_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}";
            }

            int existingIdx = FindRegionIndex(region.id);
            if (existingIdx >= 0)
            {
                _fakeRegions[existingIdx] = region;
                if (_logFakeRegionHits)
                    Debug.Log($"[WorldAIContextManager] FakeRegion '{region.id}' 갱신 ({region.shape})");
            }
            else
            {
                _fakeRegions.Add(region);
                if (_logFakeRegionHits)
                    Debug.Log($"[WorldAIContextManager] FakeRegion '{region.id}' 추가 ({region.shape}, " +
                              $"I=0x{region.identityTags:X4} S=0x{region.stateTags:X4} " +
                              $"E=0x{region.eventTags:X4} P=0x{region.potentialTags:X4})");
            }
        }

        /// <summary>id 로 가짜 영역 제거. 없으면 false.</summary>
        public bool RemoveFakeRegion(string id)
        {
            int idx = FindRegionIndex(id);
            if (idx < 0) return false;
            _fakeRegions.RemoveAt(idx);
            if (_logFakeRegionHits)
                Debug.Log($"[WorldAIContextManager] FakeRegion '{id}' 제거");
            return true;
        }

        /// <summary>모든 가짜 영역 클리어.</summary>
        public void ClearAllFakeRegions()
        {
            int n = _fakeRegions.Count;
            _fakeRegions.Clear();
            if (_logFakeRegionHits)
                Debug.Log($"[WorldAIContextManager] FakeRegion 전체 클리어 ({n} 개)");
        }

        /// <summary>현재 등록된 가짜 영역의 읽기 전용 사본 반환 (Editor 용).</summary>
        public IReadOnlyList<FakeContextRegion> GetFakeRegions() => _fakeRegions;

        /// <summary>id 로 영역 찾기. 없으면 -1.</summary>
        private int FindRegionIndex(string id)
        {
            for (int i = 0; i < _fakeRegions.Count; i++)
            {
                if (_fakeRegions[i].id == id) return i;
            }
            return -1;
        }

        /// <summary>
        /// 좌표에 적용되는 모든 가짜 영역 + 자연 비트마스크를 합성한 결과 반환.
        /// EditorWindow 의 미리보기 / 디버그 용.
        /// </summary>
        public ContextTagSet ResolveTagsAt(Vector3 worldPos)
        {
            ContextTagSet natural = default;
            if (_isInjected && _terrain != null && _terrain.IsReady
                && _graph != null && _graph.nodesData != null && _graph.nodesData.Count > 0)
            {
                int nodeIdx = FindNearestNode(worldPos, out float dNode);
                int passageIdx = FindNearestPassage(worldPos, out float dEdge);
                bool useEdge = (passageIdx >= 0)
                            && (dEdge < dNode)
                            && (dEdge < EDGE_PROXIMITY_THRESHOLD);
                int idx = useEdge ? passageIdx : nodeIdx;
                var kind = useEdge ? ElementKind.Passage : ElementKind.Node;
                natural = _terrain.GetAllTags(idx, kind);
            }
            return ApplyFakeRegions(worldPos, natural);
        }

        // ─────────────────────────────────────────────────────────────
        // ★ Phase 5 — 가짜 영역 적용 핵심 로직
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 자연 비트 + 좌표를 받아 가짜 영역들을 적용한 최종 비트마스크 반환.
        /// Replace 모드는 마지막 매칭이 base, 그 후 OR 모드들이 누적.
        /// </summary>
        private ContextTagSet ApplyFakeRegions(Vector3 worldPos, ContextTagSet natural)
        {
            if (_fakeRegions.Count == 0) return natural;

            ContextTagSet result = natural;
            bool replaceFound = false;

            // 1차 스캔: Replace 모드 영역 찾기 (마지막이 우선)
            for (int i = 0; i < _fakeRegions.Count; i++)
            {
                var r = _fakeRegions[i];
                if (r.overrideMode != FakeOverrideMode.Replace) continue;
                if (!r.ContainsPoint(worldPos)) continue;

                result = new ContextTagSet
                {
                    identityTags = r.identityTags,
                    stateTags = r.stateTags,
                    eventTags = r.eventTags,
                    potentialTags = r.potentialTags,
                };
                replaceFound = true;
            }

            // 2차 스캔: OR 모드 영역 누적
            for (int i = 0; i < _fakeRegions.Count; i++)
            {
                var r = _fakeRegions[i];
                if (r.overrideMode != FakeOverrideMode.OR) continue;
                if (!r.ContainsPoint(worldPos)) continue;

                result.identityTags |= r.identityTags;
                result.stateTags |= r.stateTags;
                result.eventTags |= r.eventTags;
                result.potentialTags |= r.potentialTags;
            }

            if (_logFakeRegionHits && (replaceFound || result.identityTags != natural.identityTags
                || result.stateTags != natural.stateTags))
            {
                Debug.Log($"[WorldAIContextManager] FakeRegion HIT @ {worldPos}: " +
                          $"natural I=0x{natural.identityTags:X4} S=0x{natural.stateTags:X4} → " +
                          $"final I=0x{result.identityTags:X4} S=0x{result.stateTags:X4} " +
                          $"(replace={replaceFound})");
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Sample (★ v6 — 가짜 영역 적용 단계 추가)
        // ─────────────────────────────────────────────────────────────

        public ContextSnapshot Sample(Vector3 worldPos)
        {
            if (!_isInjected)
            {
                if (_verboseLogging)
                    Debug.LogWarning("[WorldAIContextManager] Inject 미완료 상태에서 Sample 호출");
                // ★ Inject 안 됐어도 가짜 영역만으로 결과 만들 수 있음
                return CreateSnapshotFromFakeOnly(worldPos);
            }
            if (_terrain == null || !_terrain.IsReady)
            {
                return CreateSnapshotFromFakeOnly(worldPos);
            }
            if (_graph == null || _graph.nodesData == null || _graph.nodesData.Count == 0)
            {
                return CreateSnapshotFromFakeOnly(worldPos);
            }

            int nodeIdx = FindNearestNode(worldPos, out float dNode);
            int passageIdx = FindNearestPassage(worldPos, out float dEdge);

            bool useEdge = (passageIdx >= 0)
                        && (dEdge < dNode)
                        && (dEdge < EDGE_PROXIMITY_THRESHOLD);
            int idx = useEdge ? passageIdx : nodeIdx;
            var kind = useEdge ? ElementKind.Passage : ElementKind.Node;

            // ★ v6 — 자연 비트 → 가짜 영역 적용
            var naturalTags = _terrain.GetAllTags(idx, kind);
            var finalTags = ApplyFakeRegions(worldPos, naturalTags);

            NodeRole nRole = useEdge
                ? NodeRole.Normal
                : _terrain.GetNodeRole(idx);
            PassageRole pRole = useEdge
                ? _terrain.GetPassageRole(idx)
                : PassageRole.ConnectorPath;

            float slope = 0f;
            float density = 0.5f;
            float occlusion = 0f;
            float pathWidth = DEFAULT_PATH_WIDTH;

            if (useEdge && _graph.lastPassageProfiles != null
                && idx < _graph.lastPassageProfiles.Count)
            {
                var profile = _terrain.GetPassageProfile(idx);
                slope = profile.ySlope;
                occlusion = profile.risk.blendRisk;
                pathWidth = profile.minLocalWidth > 0f
                    ? profile.minLocalWidth
                    : DEFAULT_PATH_WIDTH;
                density = 0.5f;
            }

            // ★ v6 — Compat tags 도 final (가짜 적용 후) 기준으로 다운샘플
            var compatTags = DownsampleTags(finalTags);

            return new ContextSnapshot
            {
                nearestNodeIdx = nodeIdx,
                nearestPassageIdx = passageIdx,
                nearestKind = kind,

                identityTags = finalTags.identityTags,
                stateTags = finalTags.stateTags,
                eventTags = finalTags.eventTags,
                potentialTags = finalTags.potentialTags,

                nodeRole = nRole,
                passageRole = pRole,

                slope = slope,
                density = density,
                occlusion = occlusion,
                pathWidth = pathWidth,

                groupMorale = _group != null
                    ? _group.GetMorale()
                    : DEFAULT_MORALE_NO_GROUP,
                attackTokenAvailable = _group != null
                    ? _group.HasAvailableToken()
                    : DEFAULT_TOKEN_NO_GROUP,

                terrainTags = compatTags,
            };
        }

        /// <summary>★ v6 — Inject 미완료 + 가짜 영역만 있을 때 사용.</summary>
        private ContextSnapshot CreateSnapshotFromFakeOnly(Vector3 worldPos)
        {
            if (_fakeRegions.Count == 0) return CreateEmptySnapshot();

            ContextTagSet naturalEmpty = default;
            var finalTags = ApplyFakeRegions(worldPos, naturalEmpty);
            var compatTags = DownsampleTags(finalTags);

            return new ContextSnapshot
            {
                nearestNodeIdx = -1,
                nearestPassageIdx = -1,
                nearestKind = ElementKind.Node,

                identityTags = finalTags.identityTags,
                stateTags = finalTags.stateTags,
                eventTags = finalTags.eventTags,
                potentialTags = finalTags.potentialTags,

                nodeRole = NodeRole.Normal,
                passageRole = PassageRole.ConnectorPath,

                slope = 0f,
                density = 0.5f,
                occlusion = 0f,
                pathWidth = DEFAULT_PATH_WIDTH,

                groupMorale = DEFAULT_MORALE_NO_GROUP,
                attackTokenAvailable = DEFAULT_TOKEN_NO_GROUP,

                terrainTags = compatTags,
            };
        }

        // ─────────────────────────────────────────────────────────────
        // 그래프 검색 (v5 동일)
        // ─────────────────────────────────────────────────────────────

        private int FindNearestNode(Vector3 pos, out float distance)
        {
            distance = float.MaxValue;
            if (_graph?.nodesData == null) return -1;

            int best = -1;
            float bestSqr = float.MaxValue;
            var nodes = _graph.nodesData;
            int count = nodes.Count;

            for (int i = 0; i < count; i++)
            {
                float sqr = (pos - nodes[i].position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = i; }
            }

            distance = best >= 0 ? Mathf.Sqrt(bestSqr) : float.MaxValue;
            return best;
        }

        private int FindNearestPassage(Vector3 pos, out float distance)
        {
            distance = float.MaxValue;
            if (_graph?.edgesData == null) return -1;

            int best = -1;
            float bestSqr = float.MaxValue;
            var edges = _graph.edgesData;
            int count = edges.Count;

            for (int i = 0; i < count; i++)
            {
                float sqr = SqrDistancePointToSegment(pos, edges[i].startPos, edges[i].endPos);
                if (sqr < bestSqr) { bestSqr = sqr; best = i; }
            }

            distance = best >= 0 ? Mathf.Sqrt(bestSqr) : float.MaxValue;
            return best;
        }

        private static float SqrDistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSqr = ab.sqrMagnitude;
            if (lenSqr < 0.0001f) return (p - a).sqrMagnitude;

            float t = Vector3.Dot(p - a, ab) / lenSqr;
            t = Mathf.Clamp01(t);

            Vector3 closest = a + t * ab;
            return (p - closest).sqrMagnitude;
        }

        private static List<string> DownsampleTags(ContextTagSet tags)
        {
            var result = new List<string>(5);

            if ((tags.stateTags & (uint)StateTag.STATE_NARROW) != 0) result.Add("Narrow");
            if ((tags.stateTags & (uint)StateTag.STATE_GRAND) != 0) result.Add("Open");
            if ((tags.stateTags & (uint)StateTag.STATE_OPEN) != 0) result.Add("Open");
            if ((tags.stateTags & (uint)StateTag.STATE_DENSE) != 0) result.Add("Dense");
            if ((tags.stateTags & (uint)StateTag.STATE_ISOLATED) != 0) result.Add("Sparse");
            if ((tags.stateTags & (uint)StateTag.STATE_ELEVATED) != 0) result.Add("HighGround");

            return result;
        }

        public void RecordIncident(string incidentId, float intensityScore, string moralAlignment)
        {
            if (_recorder == null) return;
            _recorder.RecordIncident(incidentId, intensityScore, moralAlignment);
        }

        private ContextSnapshot CreateEmptySnapshot()
        {
            return new ContextSnapshot
            {
                nearestNodeIdx = -1,
                nearestPassageIdx = -1,
                nearestKind = ElementKind.Node,

                identityTags = 0,
                stateTags = 0,
                eventTags = 0,
                potentialTags = 0,

                nodeRole = NodeRole.Normal,
                passageRole = PassageRole.ConnectorPath,

                slope = 0f,
                density = 0.5f,
                occlusion = 0f,
                pathWidth = DEFAULT_PATH_WIDTH,

                groupMorale = DEFAULT_MORALE_NO_GROUP,
                attackTokenAvailable = DEFAULT_TOKEN_NO_GROUP,

                terrainTags = new List<string>(),
            };
        }

        // ─────────────────────────────────────────────────────────────
        // ★ Phase 5 — 가짜 영역 Gizmo (런타임 디버그)
        // ─────────────────────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!_drawFakeRegionGizmos) return;
            if (_fakeRegions == null || _fakeRegions.Count == 0) return;

            for (int i = 0; i < _fakeRegions.Count; i++)
            {
                var r = _fakeRegions[i];
                if (!r.active) continue;

                // OR=시안 / Replace=마젠타
                Gizmos.color = r.overrideMode == FakeOverrideMode.Replace
                    ? new Color(1f, 0.3f, 0.8f, 0.35f)
                    : new Color(0.3f, 0.9f, 1f, 0.35f);

                switch (r.shape)
                {
                    case FakeRegionShape.Sphere:
                        Gizmos.DrawWireSphere(r.center, r.radius);
                        Gizmos.DrawSphere(r.center, r.radius * 0.1f);
                        break;

                    case FakeRegionShape.Box:
                        Gizmos.DrawWireCube(r.center, r.size);
                        Gizmos.DrawCube(r.center, r.size * 0.05f);
                        break;

                    case FakeRegionShape.Line:
                        {
                            Vector3 dir = r.direction.sqrMagnitude > 0.0001f
                                ? r.direction.normalized
                                : Vector3.forward;
                            Vector3 a = r.center - dir * (r.length * 0.5f);
                            Vector3 b = r.center + dir * (r.length * 0.5f);
                            Gizmos.DrawLine(a, b);

                            // 통로 폭 표시 (양옆)
                            Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized * (r.width * 0.5f);
                            Gizmos.DrawLine(a + perp, b + perp);
                            Gizmos.DrawLine(a - perp, b - perp);
                            Gizmos.DrawLine(a + perp, a - perp);
                            Gizmos.DrawLine(b + perp, b - perp);
                            break;
                        }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ContextMenu 디버그
        // ─────────────────────────────────────────────────────────────

        [ContextMenu("Log Current Snapshot")]
        public void LogCurrentSnapshotMenu()
        {
            if (!_isInjected)
            {
                Debug.LogWarning("[WorldAIContextManager] Inject 미완료");
                return;
            }

            var snap = Sample(transform.position);
            Vector3 pos = transform.position;
            string tagsStr = (snap.terrainTags != null && snap.terrainTags.Count > 0)
                ? string.Join(",", snap.terrainTags)
                : "(empty)";

            Debug.Log(
                $"[WorldAIContextManager Snapshot] @ ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})\n" +
                $"  Graph: nodeIdx={snap.nearestNodeIdx} edgeIdx={snap.nearestPassageIdx} kind={snap.nearestKind}\n" +
                $"  Tags: I=0x{snap.identityTags:X8} S=0x{snap.stateTags:X8} " +
                       $"E=0x{snap.eventTags:X8} P=0x{snap.potentialTags:X8}\n" +
                $"  Role: node={snap.nodeRole} passage={snap.passageRole}\n" +
                $"  Metric: slope={snap.slope:F3} density={snap.density:F3} " +
                         $"occ={snap.occlusion:F3} width={snap.pathWidth:F3}m\n" +
                $"  Compat: tags=[{tagsStr}]\n" +
                $"  Group: morale={snap.groupMorale:F3} token={snap.attackTokenAvailable}\n" +
                $"  ★ FakeRegions: {_fakeRegions.Count} active"
            );
        }

        [ContextMenu("★ Clear All Fake Regions")]
        public void ClearAllFakeRegionsMenu() => ClearAllFakeRegions();

        public void LogCurrentSnapshot(Vector3 worldPos)
        {
            if (!_verboseLogging) return;
            var snap = Sample(worldPos);
            Debug.Log($"[WorldAIContextManager] @ {worldPos}: " +
                      $"node={snap.nearestNodeIdx} edge={snap.nearestPassageIdx} " +
                      $"slope={snap.slope:F2} width={snap.pathWidth:F2}, " +
                      $"morale={snap.groupMorale:F2}, fakeRegions={_fakeRegions.Count}");
        }
    }
}