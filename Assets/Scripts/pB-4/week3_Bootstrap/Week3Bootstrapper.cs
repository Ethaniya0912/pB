// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 4: Week3Bootstrapper (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 4, v3 — 3 namespace using 분기)
// 위치: Scripts/pB-4/week3_Bootstrap/Week3Bootstrapper.cs
//
// 목적: ContextManager의 4 의존성 주입 DI Container.
//       useRealImpls bool 토글 1줄로 Stub ↔ Real 전환.
//
// 코딩 표준:
//   §3.4 L4 Coordination 계층 (MonoBehaviour)
//   골든룰 1 준수: 인터페이스 의존 (구체 클래스는 Stubs/Real만)
//
// ★ D4 PM Real 전환 절차:
//   1. realTerrainHost / realScenarioHost GameObject Inspector 할당
//   2. useRealImpls 체크박스 ON
//   3. Play 진입 → Real 모드 박제
// ─────────────────────────────────────────────────────────────────────

using UnityEngine;
using TDA.PB4.Interfaces;              // IGroupAIInfo
using TDA.PB4.Interfaces.Environment;  // ITerrainSampler, IContextAnalyzer
using TDA.PB4.Interfaces.Narrative;    // IIncidentRecorder
using TDA.PB4.AI.Context;
using TDA.PB4.AI.Stubs;

namespace TDA.PB4.AI.Bootstrap
{
    /// <summary>
    /// Wk3 DI Container. Awake() 시 4 의존성 인스턴스화 + ContextManager.Inject() 호출.
    /// </summary>
    public class Week3Bootstrapper : MonoBehaviour
    {
        [Header("★ Stub vs Real 토글 (1줄 변경)")]
        [SerializeField] private bool useRealImpls = false;
        // false → Stub 5종 사용 (D2 3차 ~ D4 PM 전)
        // true  → Real 구현체 사용 (D4 PM Real 도착 후)

        [Header("Real 호스트 (D4 PM 도착 후 할당)")]
        [Tooltip("ITerrainSampler + IContextAnalyzer 구현체 GameObject")]
        [SerializeField] private GameObject realTerrainHost;
        [Tooltip("IIncidentRecorder 구현체 GameObject")]
        [SerializeField] private GameObject realScenarioHost;

        [Header("주입 대상 (Inspector 할당)")]
        [SerializeField] private ContextManager contextManager;

        [Header("디버깅")]
        [SerializeField] private bool _verboseLogging = true;

        private void Awake()
        {
            if (contextManager == null)
            {
                Debug.LogError("[Bootstrapper] ContextManager 미할당. " +
                               "Inspector에서 할당 필요.");
                return;
            }

            // ── 1. 지형 (지형팀 [01/W3] [02/W3]) ─────────────────────
            ITerrainSampler terrain;
            IContextAnalyzer analyzer;
            if (useRealImpls && realTerrainHost != null)
            {
                terrain = realTerrainHost.GetComponent<ITerrainSampler>();
                analyzer = realTerrainHost.GetComponent<IContextAnalyzer>();
                LogIfVerbose($"[Bootstrapper] Terrain Real: " +
                             $"sampler={terrain != null}, analyzer={analyzer != null}");
            }
            else
            {
                // 자연 발현 Stub (DEBT-21 교훈 적용)
                terrain = new TerrainSamplerStub();
                analyzer = new ContextAnalyzerStub(terrain);
                LogIfVerbose("[Bootstrapper] Terrain Stub: 자연 발현 모드");
            }

            // ── 2. 시나리오 (시나리오팀 [04/W3] BLOCKING) ───────────
            IIncidentRecorder recorder;
            if (useRealImpls && realScenarioHost != null)
            {
                recorder = realScenarioHost.GetComponent<IIncidentRecorder>();
                LogIfVerbose($"[Bootstrapper] Scenario Real: " +
                             $"recorder={recorder != null}");
            }
            else
            {
                recorder = new IncidentRecorderStub();
                LogIfVerbose("[Bootstrapper] Scenario Stub: In-memory 폴백");
            }

            // ── 3. GroupAIManager (D3 PM 작성 예정, 선택적) ────────
            // 현재 시점에 GroupAIManager 클래스가 존재하면 자동 검색,
            // 미존재 시 null (ContextManager가 안전하게 처리).
            IGroupAIInfo group = FindGroupAIInfo();

            // ── 4. ContextManager 주입 ─────────────────────────────
            contextManager.Inject(terrain, analyzer, recorder, group);

            LogIfVerbose($"[Bootstrapper] 주입 완료. useRealImpls={useRealImpls}");
        }

        /// <summary>
        /// IGroupAIInfo 구현체 자동 검색.
        /// D3 PM GroupAIManager.cs 작성 시 IGroupAIInfo 구현 → 자동 발견.
        /// 미존재 시 null 반환 (ContextManager가 기본값으로 폴백).
        /// </summary>
        private IGroupAIInfo FindGroupAIInfo()
        {
            // D3 PM 진입 전: GroupAIManager 미존재 → null
            // D3 PM 진입 후: GroupAIManager 존재 → 자동 연결
            var components = FindObjectsOfType<MonoBehaviour>();
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
    }
}
