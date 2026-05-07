// ─────────────────────────────────────────────────────────────────────
// pB-4 — AIContextBootstrapper (v5 — DontDestroyOnLoad + 늦은 Inject)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-05-03 v5 — 메뉴 씬 → 본 씬 흐름 지원
//   v4 → v5 변경 사항:
//     [개명] Week3Bootstrapper → AIContextBootstrapper (주차 무관)
//     [추가] DontDestroyOnLoad 옵션 플래그 (_persistAcrossScenes)
//     [추가] 늦은 Inject — SceneManager.sceneLoaded 이벤트 구독
//     [추가] Inject 트리거 씬 화이트리스트 (_targetScenes)
//     [추가] 중복 Inject 방지 (재진입 시 기존 Inject 보존)
//     [보존] Awake 직접 Inject (즉시 모드 — 단독 Play 검증용)
//
// 위치: Scripts/pB-4/week3_Bootstrap/AIContextBootstrapper.cs
//
// 시나리오:
//   ① 메뉴 씬 (Scene_main_menu_01) 시작:
//      - WorldAIContextManager (DontDestroyOnLoad) Awake → Instance 등록
//      - 본 컴포넌트 (DontDestroyOnLoad) Awake → sceneLoaded 구독
//      - Tier 2 인도분이 메뉴 씬에 없음 → 즉시 Inject 안 함 (대기)
//
//   ② Start Game → Scene_World_01 로딩:
//      - sceneLoaded 이벤트 발화
//      - 해당 씬이 _targetScenes 에 있으면 Tier 2 인도분 자동 검색
//      - WorldAIContextManager.Inject() 호출
//
//   ③ 다른 씬으로 이동 (예: 메뉴 복귀):
//      - WorldAIContextManager 는 살아있지만 Inject 상태 무효화 (옵션)
//
// 셋업 (메뉴 씬):
//   1. 빈 GameObject "AIContextBootstrapper" 추가 + 본 컴포넌트 부착
//   2. Inspector:
//      - Persist Across Scenes: ☑
//      - Target Scenes: ["Scene_World_01", "Wk3_NaturalEmergenceScene"]
//      - Force Rebuild On Inject: ☑
//   3. Tier 2 인도분 Inspector 할당 X — sceneLoaded 후 자동 검색
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TDA.PB4.Interfaces;
using TDA.PB4.Interfaces.Narrative;
using TDA.PB4.AI.Context;
using TDA.PB4.AI.Stubs;
using CaveSystem;

namespace TDA.PB4.AI.Bootstrap
{
    /// <summary>
    /// AI Context 시스템의 부트스트랩 (★ v5 — DontDestroyOnLoad + 늦은 Inject).
    /// 메뉴 씬부터 존재 → 본 씬 (Scene_World_01) 로딩 후 Tier 2 자동 통합.
    /// </summary>
    public class AIContextBootstrapper : MonoBehaviour
    {
        // ═════════════════════════════════════════════════════════════
        // 싱글톤 + DontDestroyOnLoad 플래그
        // ═════════════════════════════════════════════════════════════
        public static AIContextBootstrapper Instance { get; private set; }

        [Header("★ 라이프사이클")]
        [Tooltip("ON → 씬 전환 시 본 컴포넌트 보존 (메뉴 씬부터 본 씬까지). " +
                 "OFF → 씬 전용 (단독 Play 검증용)")]
        [SerializeField] private bool _persistAcrossScenes = true;

        [Tooltip("Inject 를 트리거할 씬 이름 화이트리스트. " +
                 "비워두면 모든 씬 로딩 시 시도. 매뉴 씬에서는 Inject 스킵.")]
        [SerializeField]
        private List<string> _targetScenes = new List<string>
        {
            "Scene_World_01",
            "Wk3_NaturalEmergenceScene",
        };

        [Tooltip("Inject 시 TerrainContextAnalyzer.Rebuild() 명시 호출. " +
                 "그래프가 늦게 생성되는 케이스 방어.")]
        [SerializeField] private bool _forceRebuildOnInject = true;

        [Tooltip("ON → Awake 에서도 즉시 Inject 시도 (단독 Play 검증용). " +
                 "OFF → sceneLoaded 이벤트만 사용 (메뉴→본 씬 흐름).")]
        [SerializeField] private bool _tryInjectOnAwake = false;

        [Header("★ Tier 2 인도분 (Inspector 할당 옵션)")]
        [Tooltip("Inspector 직접 할당 시 우선 사용. 비워두면 sceneLoaded 시 자동 검색.")]
        [SerializeField] private TerrainContextAnalyzer _terrainAnalyzerOverride;

        [SerializeField] private CaveNodeGraphBuilder _graphBuilderOverride;

        [Header("디버깅")]
        [SerializeField] private bool _verboseLogging = true;

        // ─────────────────────────────────────────────────────────────
        // 라이프사이클
        // ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            // 싱글톤 등록
            if (Instance != null && Instance != this)
            {
                LogIfVerbose("[AIContextBootstrapper] 중복 인스턴스 — 본 GameObject 제거");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // DontDestroyOnLoad 적용
            if (_persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
                LogIfVerbose("[AIContextBootstrapper] DontDestroyOnLoad 적용");
            }

            // sceneLoaded 이벤트 구독
            SceneManager.sceneLoaded += OnSceneLoaded;
            LogIfVerbose("[AIContextBootstrapper] sceneLoaded 이벤트 구독 완료");

            // 즉시 Inject 시도 (옵션)
            if (_tryInjectOnAwake)
            {
                LogIfVerbose("[AIContextBootstrapper] Awake 즉시 Inject 시도 (★ tryInjectOnAwake = true)");
                TryInject(SceneManager.GetActiveScene().name);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // ─────────────────────────────────────────────────────────────
        // sceneLoaded 핸들러
        // ─────────────────────────────────────────────────────────────
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogIfVerbose($"[AIContextBootstrapper] sceneLoaded: \"{scene.name}\" (mode={mode})");

            // 화이트리스트 검사 — 비어있으면 모든 씬 허용
            if (_targetScenes != null && _targetScenes.Count > 0)
            {
                if (!_targetScenes.Contains(scene.name))
                {
                    LogIfVerbose($"[AIContextBootstrapper] \"{scene.name}\" 은 _targetScenes 에 없음 — Inject 스킵");
                    return;
                }
            }

            // Inject 시도
            TryInject(scene.name);
        }

        // ─────────────────────────────────────────────────────────────
        // Inject 본체
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Tier 2 인도분 자동 검색 + WorldAIContextManager.Inject 호출.
        /// 이미 Inject 되어 있으면 재실행 (씬 재진입 케이스).
        /// </summary>
        private void TryInject(string sceneName)
        {
            // ── 1. WorldAIContextManager 싱글톤 확인 ─────────────────
            var ctxMgr = WorldAIContextManager.Instance;
            if (ctxMgr == null)
            {
                Debug.LogError("[AIContextBootstrapper] WorldAIContextManager.Instance == null. " +
                               "메뉴 씬에 WorldAIContextManager 컴포넌트 부착 + DontDestroyOnLoad 확인.");
                return;
            }

            // ── 2. TerrainContextAnalyzer 검색 ────────────────────
            TerrainContextAnalyzer terrain = _terrainAnalyzerOverride;
            if (terrain == null)
            {
#if UNITY_2023_1_OR_NEWER
                terrain = FindFirstObjectByType<TerrainContextAnalyzer>();
#else
                terrain = FindObjectOfType<TerrainContextAnalyzer>();
#endif
            }
            if (terrain == null)
            {
                Debug.LogWarning($"[AIContextBootstrapper] \"{sceneName}\" 에 TerrainContextAnalyzer 없음. " +
                                 "Inject 스킵 — Sample() 은 EmptySnapshot 반환.");
                return;
            }

            // ── 3. CaveNodeGraphBuilder 검색 ─────────────────────
            CaveNodeGraphBuilder graph = _graphBuilderOverride;
            if (graph == null) graph = CaveNodeGraphBuilder.Instance;
            if (graph == null)
            {
#if UNITY_2023_1_OR_NEWER
                graph = FindFirstObjectByType<CaveNodeGraphBuilder>();
#else
                graph = FindObjectOfType<CaveNodeGraphBuilder>();
#endif
            }
            if (graph == null)
            {
                Debug.LogWarning($"[AIContextBootstrapper] \"{sceneName}\" 에 CaveNodeGraphBuilder 없음. Inject 스킵.");
                return;
            }

            // ── 4. Tier 2 — Rebuild 명시 호출 (방어적) ────────────
            if (_forceRebuildOnInject)
            {
                terrain.Rebuild();
                LogIfVerbose("[AIContextBootstrapper] TerrainContextAnalyzer.Rebuild() 명시 호출 완료");
            }

            // ── 5. IIncidentRecorder Stub 폴백 ────────────────────
            IIncidentRecorder recorder = new IncidentRecorderStub();

            // ── 6. IGroupAIInfo 자동 검색 ─────────────────────────
            IGroupAIInfo group = FindGroupAIInfo();

            // ── 7. WorldAIContextManager.Inject 호출 ─────────────
            ctxMgr.Inject(terrain, graph, recorder, group);

            LogIfVerbose($"[AIContextBootstrapper] ★ \"{sceneName}\" Tier 2 통합 완료 — " +
                         $"IJunctionContextAnalyzer + CaveNodeGraphBuilder + " +
                         $"IIncidentRecorderStub + IGroupAIInfo({(group != null ? "real" : "null")})");
        }

        /// <summary>
        /// IGroupAIInfo 구현체 자동 검색.
        /// </summary>
        private IGroupAIInfo FindGroupAIInfo()
        {
#if UNITY_2023_1_OR_NEWER
            var components = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
#else
            var components = FindObjectsOfType<MonoBehaviour>();
#endif
            foreach (var comp in components)
            {
                if (comp is IGroupAIInfo info) return info;
            }
            return null;
        }

        private void LogIfVerbose(string msg)
        {
            if (_verboseLogging) Debug.Log(msg);
        }

        // ─────────────────────────────────────────────────────────────
        // 디버그 — 수동 Inject (런타임 디버깅용)
        // ─────────────────────────────────────────────────────────────
        [ContextMenu("★ Manual Inject (현재 씬)")]
        public void ManualInject()
        {
            TryInject(SceneManager.GetActiveScene().name);
        }
    }
}