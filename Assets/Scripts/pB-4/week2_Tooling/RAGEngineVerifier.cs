// =============================================================================
// RAGEngineVerifier.cs  |  pB-4 Project — Week 3 D2 (DEBT-20 검증)
// Layer  : L4 Tooling (검증 도구)
// Namespace: TDA.PB4.Tooling.RAG
//
// 역할:
//   LightweightRAGEngine의 CosineSimilarity 정확성 + Search() 동작 + 성능을
//   런타임에 일괄 검증한다. HumanoidVisualAutoVerifier와 동일 패턴.
//
// DEBT-20 (RAG 코사인 유사도 검색 미검증) 본질:
//   알고리즘 자체는 표준 코사인 공식이지만 실제 동작/성능 박제가 없었음.
//   이 Verifier는 다음 3종을 검증:
//     1. 단위 테스트   — CosineSimilarity 수학 정확성 (6 케이스)
//     2. 통합 테스트   — Search() 정렬/임계값/maxResults (5 케이스)
//     3. 성능 테스트   — 100/500 사건 벡터 검색 시간 (10ms 목표)
//
// 사용법:
//   1. 빈 GameObject에 본 컴포넌트 부착
//   2. Play 진입 → Console에 박제 결과 출력
//   3. PASS/FAIL 카운트 확인
//
// 의존성:
//   - LightweightRAGEngine (CosineSimilarity static 메서드)
//   - IncidentRecorder, IncidentSnapshot
// =============================================================================
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using TDA.PB4.Scenario;

namespace TDA.PB4.Tooling.RAG
{
    public class RAGEngineVerifier : MonoBehaviour
    {
        [Header("━━━ 실행 설정 ━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Awake 시 자동 실행")]
        public bool runOnAwake = true;

        [Tooltip("성능 테스트 사건 수")]
        [Range(10, 1000)] public int perfRecordCount = 100;

        [Tooltip("성능 테스트 반복 횟수 (평균 측정용)")]
        [Range(1, 100)] public int perfIterations = 10;

        [Tooltip("성능 목표 시간 (ms)")]
        [Range(1f, 100f)] public float perfTargetMs = 10f;

        [Header("━━━ 결과 박제 (Read Only) ━━━━━━━━━━")]
        [SerializeField] private int unitPass;
        [SerializeField] private int unitFail;
        [SerializeField] private int integrationPass;
        [SerializeField] private int integrationFail;
        [SerializeField] private float perfAvgMs;
        [SerializeField] private float perfMaxMs;
        [SerializeField] private bool perfPass;

        // 컬러 박제
        private const string CLR_PASS  = "<color=#7CFC00>";   // LawnGreen
        private const string CLR_FAIL  = "<color=#FF4500>";   // OrangeRed
        private const string CLR_INFO  = "<color=#7CD0FF>";   // SkyBlue
        private const string CLR_HEAD  = "<color=#FFD700>";   // Gold
        private const string CLR_END   = "</color>";

        private const float EPSILON = 0.0001f;

        // ════════════════════════════════════════════════════════════════════
        // Entry
        // ════════════════════════════════════════════════════════════════════
        private void Awake()
        {
            if (runOnAwake) RunAllTests();
        }

        [ContextMenu("Run All Tests")]
        public void RunAllTests()
        {
            Debug.Log($"{CLR_HEAD}═══════════════════════════════════════════════════{CLR_END}");
            Debug.Log($"{CLR_HEAD}[RAG Verifier] DEBT-20 검증 시작{CLR_END}");
            Debug.Log($"{CLR_HEAD}═══════════════════════════════════════════════════{CLR_END}");

            unitPass = unitFail = 0;
            integrationPass = integrationFail = 0;

            RunUnitTests();
            RunIntegrationTests();
            RunPerformanceTest();

            ReportSummary();
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. 단위 테스트 — CosineSimilarity
        // ════════════════════════════════════════════════════════════════════
        private void RunUnitTests()
        {
            Debug.Log($"{CLR_INFO}── 1. 단위 테스트 (CosineSimilarity) ──{CLR_END}");

            // U1: 정확 일치 → 1.0
            AssertNear("U1 정확 일치",
                LightweightRAGEngine.CosineSimilarity(new[] { 1f, 2f, 3f, 4f }, new[] { 1f, 2f, 3f, 4f }),
                1f, ref unitPass, ref unitFail);

            // U2: 정확 일치 (768차원) → 1.0
            var v768 = new float[768];
            for (int i = 0; i < 768; i++) v768[i] = (i % 7) * 0.1f;
            AssertNear("U2 정확 일치 (768차원)",
                LightweightRAGEngine.CosineSimilarity(v768, v768),
                1f, ref unitPass, ref unitFail);

            // U3: 직교 → 0.0
            AssertNear("U3 직교 (e1·e2)",
                LightweightRAGEngine.CosineSimilarity(new[] { 1f, 0f, 0f }, new[] { 0f, 1f, 0f }),
                0f, ref unitPass, ref unitFail);

            // U4: 반대 방향 → -1.0
            AssertNear("U4 반대 방향",
                LightweightRAGEngine.CosineSimilarity(new[] { 1f, 2f, 3f }, new[] { -1f, -2f, -3f }),
                -1f, ref unitPass, ref unitFail);

            // U5: 영벡터 (divide-by-zero) → 0
            AssertNear("U5 영벡터 안전",
                LightweightRAGEngine.CosineSimilarity(new[] { 0f, 0f, 0f }, new[] { 1f, 2f, 3f }),
                0f, ref unitPass, ref unitFail);

            // U6: null 입력 → 0
            AssertNear("U6 null 가드",
                LightweightRAGEngine.CosineSimilarity(null, new[] { 1f, 2f, 3f }),
                0f, ref unitPass, ref unitFail);

            // U7: 길이 불일치 (Min 처리)
            // a=[1,2], b=[1,2,99,99] → 길이 2까지만 사용 → 정확 일치
            AssertNear("U7 길이 불일치 (Min)",
                LightweightRAGEngine.CosineSimilarity(new[] { 1f, 2f }, new[] { 1f, 2f, 99f, 99f }),
                1f, ref unitPass, ref unitFail);

            // U8: 알려진 값 검증 — a=[1,1,1], b=[1,1,0]
            // dot=2, magA=√3, magB=√2 → cos = 2/√6 ≈ 0.8165
            AssertNear("U8 부분 일치 (계산값 박제)",
                LightweightRAGEngine.CosineSimilarity(new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 0f }),
                0.8165f, ref unitPass, ref unitFail, tol: 0.001f);
        }

        private static void AssertNear(string label, float actual, float expected,
            ref int pass, ref int fail, float tol = EPSILON)
        {
            bool ok = Mathf.Abs(actual - expected) <= tol;
            if (ok)
            {
                pass++;
                Debug.Log($"  {CLR_PASS}✅ {label}{CLR_END} (actual={actual:F4}, expected={expected:F4})");
            }
            else
            {
                fail++;
                Debug.LogError($"  ❌ {label} (actual={actual:F4}, expected={expected:F4}, tol={tol})");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. 통합 테스트 — Search() 동작
        // ════════════════════════════════════════════════════════════════════
        private void RunIntegrationTests()
        {
            Debug.Log($"{CLR_INFO}── 2. 통합 테스트 (Search 동작) ──{CLR_END}");

            // 임시 GameObject 생성 (테스트 후 파괴)
            var go = new GameObject("__RAG_TestRig__");
            go.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                // Recorder + Engine 인스턴스화 — NetworkBehaviour는 Spawn 안 시키므로
                // records 필드만 reflection으로 직접 주입
                var recorder = go.AddComponent<IncidentRecorder>();
                var engine = go.AddComponent<LightweightRAGEngine>();
                engine.debugLog = false;  // verifier에서 별도 출력하므로 차단

                // 테스트 사건 5건 삽입 (벡터 차원=10, 단순화)
                var snaps = new List<IncidentSnapshot>
                {
                    NewSnap("inc_01_perfect", new[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }, "고블린 매복 (북문)"),
                    NewSnap("inc_02_close",   new[] { 0.95f, 0.1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }, "오크 습격 (북동문)"),
                    NewSnap("inc_03_resonance", new[] { 0.7f, 0.5f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }, "도적 출현"),
                    NewSnap("inc_04_ambient", new[] { 0.3f, 0.5f, 0.7f, 0.3f, 0.1f, 0f, 0f, 0f, 0f, 0f }, "여행 중 휴식"),
                    NewSnap("inc_05_far",     new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f }, "도시 광장 행사"),
                };
                InjectRecords(recorder, snaps);
                InjectEngineRecorder(engine, recorder);

                // I1: 동일 벡터 입력 → DejaVu (1.0)
                var query1 = new[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
                var r1 = engine.Search(query1);
                AssertTrue("I1 동일 벡터 → 결과 5건 이하",
                    r1 != null && r1.Count >= 1, ref integrationPass, ref integrationFail);
                AssertTrue("I1b 1위가 inc_01_perfect (DejaVu)",
                    r1.Count > 0 && r1[0].incidentId == "inc_01_perfect" && r1[0].resonanceType == "DejaVu",
                    ref integrationPass, ref integrationFail);
                AssertNear("I1c 1위 sim ≈ 1.0",
                    r1.Count > 0 ? r1[0].cosineSimilarity : 0f, 1f, ref integrationPass, ref integrationFail);

                // I2: 정렬 박제 — 내림차순
                bool sorted = true;
                for (int i = 1; i < r1.Count; i++)
                {
                    if (r1[i].cosineSimilarity > r1[i - 1].cosineSimilarity) { sorted = false; break; }
                }
                AssertTrue("I2 결과 내림차순 정렬", sorted, ref integrationPass, ref integrationFail);

                // I3: 직교 벡터 → ambient 미만은 결과에 없어야 (Search 내부 필터)
                var query2 = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f };
                var r2 = engine.Search(query2);
                bool allOverThreshold = true;
                foreach (var r in r2)
                    if (r.cosineSimilarity < engine.ambientThreshold) { allOverThreshold = false; break; }
                AssertTrue("I3 직교 입력 → 모든 결과 ≥ ambientThreshold (0.3)",
                    allOverThreshold, ref integrationPass, ref integrationFail);

                // I4: maxResults 박제
                engine.maxResults = 2;
                var r3 = engine.Search(query1);
                AssertTrue($"I4 maxResults=2 (실제={r3.Count})",
                    r3.Count <= 2, ref integrationPass, ref integrationFail);
                engine.maxResults = 5;  // 원복

                // I5: resonanceType 분류 박제
                var query3 = new[] { 0.7f, 0.5f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
                var r4 = engine.Search(query3);
                bool typesValid = true;
                foreach (var r in r4)
                {
                    if (r.cosineSimilarity >= engine.dejaVuThreshold && r.resonanceType != "DejaVu") typesValid = false;
                    else if (r.cosineSimilarity >= engine.resonanceThreshold && r.cosineSimilarity < engine.dejaVuThreshold && r.resonanceType != "Resonance") typesValid = false;
                    else if (r.cosineSimilarity >= engine.ambientThreshold && r.cosineSimilarity < engine.resonanceThreshold && r.resonanceType != "Ambient") typesValid = false;
                }
                AssertTrue("I5 resonanceType 분류 정확",
                    typesValid, ref integrationPass, ref integrationFail);
            }
            finally
            {
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }

        private static IncidentSnapshot NewSnap(string id, float[] vec, string summary)
        {
            return new IncidentSnapshot
            {
                incidentId = id,
                situationVector = vec,
                summaryProse = summary,
                gameTimestamp = 0f,
            };
        }

        // IncidentRecorder.records는 private SerializeField → reflection으로 주입
        private static void InjectRecords(IncidentRecorder recorder, List<IncidentSnapshot> snaps)
        {
            var f = typeof(IncidentRecorder).GetField("records",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f?.SetValue(recorder, snaps);
        }

        private static void InjectEngineRecorder(LightweightRAGEngine engine, IncidentRecorder recorder)
        {
            var f = typeof(LightweightRAGEngine).GetField("recorder",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f?.SetValue(engine, recorder);
        }

        private static void AssertTrue(string label, bool condition, ref int pass, ref int fail)
        {
            if (condition)
            {
                pass++;
                Debug.Log($"  {CLR_PASS}✅ {label}{CLR_END}");
            }
            else
            {
                fail++;
                Debug.LogError($"  ❌ {label}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. 성능 테스트
        // ════════════════════════════════════════════════════════════════════
        private void RunPerformanceTest()
        {
            Debug.Log($"{CLR_INFO}── 3. 성능 테스트 ({perfRecordCount}건 × {perfIterations}회) ──{CLR_END}");

            var go = new GameObject("__RAG_PerfRig__");
            go.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                var recorder = go.AddComponent<IncidentRecorder>();
                var engine = go.AddComponent<LightweightRAGEngine>();
                engine.debugLog = false;

                // 768차원 무작위 벡터 perfRecordCount건 생성
                var snaps = new List<IncidentSnapshot>(perfRecordCount);
                var rng = new System.Random(42);
                for (int i = 0; i < perfRecordCount; i++)
                {
                    var v = new float[768];
                    for (int j = 0; j < 768; j++) v[j] = (float)(rng.NextDouble() * 2 - 1);
                    snaps.Add(NewSnap($"perf_{i:D4}", v, $"사건 #{i}"));
                }
                InjectRecords(recorder, snaps);
                InjectEngineRecorder(engine, recorder);

                // 쿼리 벡터
                var query = new float[768];
                for (int j = 0; j < 768; j++) query[j] = (float)(rng.NextDouble() * 2 - 1);

                // 워밍업 (JIT)
                engine.Search(query);

                // 측정
                var sw = new Stopwatch();
                float total = 0f, max = 0f;
                for (int it = 0; it < perfIterations; it++)
                {
                    sw.Restart();
                    engine.Search(query);
                    sw.Stop();
                    float ms = (float)sw.Elapsed.TotalMilliseconds;
                    total += ms;
                    if (ms > max) max = ms;
                }

                perfAvgMs = total / perfIterations;
                perfMaxMs = max;
                perfPass = perfAvgMs <= perfTargetMs;

                string clr = perfPass ? CLR_PASS : CLR_FAIL;
                string sym = perfPass ? "✅" : "❌";
                Debug.Log($"  {clr}{sym} avg={perfAvgMs:F2}ms · max={perfMaxMs:F2}ms (목표 {perfTargetMs:F0}ms){CLR_END}");
                Debug.Log($"  {CLR_INFO}records={perfRecordCount}, iterations={perfIterations}{CLR_END}");
            }
            finally
            {
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 종합 박제
        // ════════════════════════════════════════════════════════════════════
        private void ReportSummary()
        {
            int totalPass = unitPass + integrationPass;
            int totalFail = unitFail + integrationFail;
            int total = totalPass + totalFail;

            Debug.Log($"{CLR_HEAD}═══════════════════════════════════════════════════{CLR_END}");
            Debug.Log($"{CLR_HEAD}[RAG Verifier] DEBT-20 검증 종합{CLR_END}");
            Debug.Log($"{CLR_HEAD}═══════════════════════════════════════════════════{CLR_END}");
            Debug.Log($"  단위 테스트:   {unitPass}/{unitPass + unitFail} PASS");
            Debug.Log($"  통합 테스트:   {integrationPass}/{integrationPass + integrationFail} PASS");
            Debug.Log($"  성능:          avg={perfAvgMs:F2}ms / max={perfMaxMs:F2}ms (목표 {perfTargetMs:F0}ms) → {(perfPass ? "PASS" : "FAIL")}");
            Debug.Log($"  ───────────────────────────────────────────────");
            string finalSym = (totalFail == 0 && perfPass) ? "✅ ALL PASS" : "❌ FAIL";
            string finalClr = (totalFail == 0 && perfPass) ? CLR_PASS : CLR_FAIL;
            Debug.Log($"  {finalClr}최종: {finalSym} ({totalPass}/{total} 기능 + 성능 {(perfPass ? "OK" : "NG")}){CLR_END}");
            Debug.Log($"{CLR_HEAD}═══════════════════════════════════════════════════{CLR_END}");
        }
    }
}
