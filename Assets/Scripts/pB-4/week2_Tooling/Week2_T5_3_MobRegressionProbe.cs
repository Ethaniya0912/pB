// =============================================================================
// Week2_T5_3_MobRegressionProbe.cs  |  pB-4 Week 2 Day 5 T5.3  v3
// 역할: Mob 회귀 테스트 자동 측정기. Play 진입 시 Mob* 인스턴스 모니터링,
//       FPS / 콘솔 에러·경고 / 상태 분포 / fear 분포 + 매니저 의존성을 수집.
//
// [v3 신규 — 의존성 자동 검증]
//   - Awake에서 World*Manager 싱글톤 존재 여부 검증
//   - 누락된 매니저는 result.missingDependencies에 박제
//   - Update 첫 프레임에 1회 추가 검증 (Awake 순서 의존성 회피)
//   - 자기 [T5.3 Probe] 로그는 errors/warnings 카운트 제외
//
// 측정 지표:
//   ① avgFps / minFps          — Mob FPS 60+ 통과 기준
//   ② errors / warnings         — Console 에러 0건, 경고 5건 이하 통과 기준
//   ③ activeBrainCount          — sanity (예상 15)
//   ④ stateTransitions          — BT 동작 활성도 (베이스라인 비교용 snapshot)
//   ⑤ fearAvg/Min/Max per faction — Mob 인지 작동 확인
//   ⑥ panicChainResult           — 강제 사망 후 인접 fear 증분 (수동 트리거)
//   ⑦ missingDependencies (v3)   — Manager 싱글톤 누락 인벤토리
// =============================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Mob;

namespace TDA.PB4.Tooling.MobRegression
{
    public enum ProbePhase
    {
        NotStarted,
        WarmingUp,
        Measuring,
        Finalized
    }

    [Serializable]
    public class MobRegressionResult
    {
        public bool sessionStarted;
        public bool sessionCompleted;
        public float durationSeconds;

        // FPS
        public float avgFps;
        public float minFps;
        public int frameSamples;

        // Console
        public int errors;
        public int warnings;
        public List<string> errorMessages = new List<string>();
        public List<string> warningMessages = new List<string>();

        // Brain 상태
        public int activeBrainCount;
        public int stateTransitions;
        public Dictionary<string, int> finalStateDistribution = new Dictionary<string, int>();
        public Dictionary<string, FactionFearStat> fearByFaction = new Dictionary<string, FactionFearStat>();

        // PanicChain
        public bool panicChainTriggered;
        public float panicChainFearBefore;
        public float panicChainFearAfter;
        public string panicChainNote;

        // [v3] 의존성 검증
        public List<string> missingDependencies = new List<string>();
        public List<string> presentDependencies = new List<string>();
        public bool stubModeActive;

        public string lastError;
    }

    [Serializable]
    public class FactionFearStat
    {
        public int count;
        public float min = 1f;
        public float max = 0f;
        public float sum;
        public float Avg => count > 0 ? sum / count : 0f;
    }

    public class Week2_T5_3_MobRegressionProbe : MonoBehaviour
    {
        public static Week2_T5_3_MobRegressionProbe Instance { get; private set; }
        public static MobRegressionResult LatestResult { get; private set; } = new MobRegressionResult();

        [Header("━━━ 측정 설정 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("측정 시작 후 자동 종료까지의 시간 (초). 0 = 무제한.")]
        public float autoStopAfterSeconds = 30f;

        [Tooltip("FPS 측정에서 처음 N초는 워밍업으로 제외.")]
        public float warmupSeconds = 2f;

        [Header("━━━ [v3] Stub Mode ━━━━━━━━━━━━━━━━")]
        [Tooltip("ON이면 측정 시작 전 Mob의 AICharacterEquipmentManager + AICharacterEquipmentDebugger를 " +
                 "일시 disable. WorldItemDatabase 매니저 없는 환경에서 36건 에러 방지.\n" +
                 "Runner의 Stub Mode 토글이 자동 설정. 측정 한정 임시 비활성화 — 씬 저장 X.")]
        public bool stubMode = false;

        [Tooltip("[v4] 검증할 매니저 싱글톤 타입 이름 목록. Runner가 채움.\n" +
                 "Session→State 정정 + WorldAIManager 추가.")]
        public List<string> dependencyTypeNames = new List<string>
        {
            "WorldItemDatabase",
            "WorldUtilityManager",
            "WorldGameStateManager",   // v4: Session → State
            "WorldSaveGameManager",
            "WorldAIManager",          // v4: 신규
        };

        // 내부 상태
        private MobRegressionResult result;
        private float startTime;
        private List<MobAIBrain> trackedBrains = new List<MobAIBrain>();
        private Dictionary<MobAIBrain, MobBTState> lastStates = new Dictionary<MobAIBrain, MobBTState>();
        private bool isWarmedUp;
        private float warmupEndTime;
        private ProbePhase phase = ProbePhase.NotStarted;
        private bool dependenciesRevalidated = false;

        // [v3] Stub Mode에서 비활성화한 컴포넌트 — 종료 시 복원
        private List<Behaviour> disabledComponents = new List<Behaviour>();

        // 외부 노출
        public ProbePhase Phase => phase;
        public float ElapsedSeconds => phase == ProbePhase.NotStarted ? 0f : Time.unscaledTime - startTime;
        public float RemainingSeconds
        {
            get
            {
                if (autoStopAfterSeconds <= 0f) return -1f;
                if (phase == ProbePhase.Finalized) return 0f;
                return Mathf.Max(0f, autoStopAfterSeconds - ElapsedSeconds);
            }
        }
        public float WarmupRemaining => isWarmedUp ? 0f : Mathf.Max(0f, warmupEndTime - Time.unscaledTime);
        public int TrackedBrainCount => trackedBrains.Count;

        // ==================================================================
        // Lifecycle
        // ==================================================================
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            try
            {
                result = new MobRegressionResult();
                LatestResult = result;
                result.sessionStarted = true;
                result.stubModeActive = stubMode;

                startTime = Time.unscaledTime;
                warmupEndTime = startTime + warmupSeconds;
                phase = ProbePhase.WarmingUp;

                Application.logMessageReceived += OnLogMessage;

                // [v3] Stub Mode — Equipment 컴포넌트 임시 disable
                if (stubMode) ApplyStubMode();

                // 측정 대상 Brain 스캔
                var brains = FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None);
                trackedBrains.AddRange(brains);
                result.activeBrainCount = trackedBrains.Count;

                foreach (var b in trackedBrains)
                    lastStates[b] = b.CurrentState;

                // [v3] 1차 의존성 검증 (Awake 시점)
                ValidateDependencies();

                Debug.Log($"[T5.3 Probe] 시작. 추적 Brain={trackedBrains.Count}, " +
                          $"warmup={warmupSeconds}s, autoStop={autoStopAfterSeconds}s, " +
                          $"stubMode={stubMode}. 의존성: {result.presentDependencies.Count}/{dependencyTypeNames.Count} 활성.");

                if (trackedBrains.Count == 0)
                    Debug.LogWarning("[T5.3 Probe] ⚠️ 씬에 MobAIBrain 0개. " +
                                     "Step 1의 5×3 자동 스폰 먼저 실행하세요.");
            }
            catch (Exception e)
            {
                if (result != null) result.lastError = "Awake 실패: " + e.Message;
                Debug.LogError($"[T5.3 Probe] Awake 예외: {e}");
            }
        }

        private void OnDestroy()
        {
            try { Application.logMessageReceived -= OnLogMessage; } catch { }
            FinalizeProbe();
            RestoreStubMode();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            try
            {
                float now = Time.unscaledTime;
                float elapsed = now - startTime;

                // 워밍업
                if (!isWarmedUp)
                {
                    if (now >= warmupEndTime)
                    {
                        isWarmedUp = true;
                        phase = ProbePhase.Measuring;

                        // [v3] 본 측정 진입 시 의존성 재검증 (Awake 순서 의존성 보정)
                        if (!dependenciesRevalidated)
                        {
                            dependenciesRevalidated = true;
                            ValidateDependencies();
                        }

                        Debug.Log($"[T5.3 Probe] 워밍업 종료. 본 측정 시작 (elapsed={elapsed:F2}s).");
                    }
                    else return;
                }

                // FPS
                float fps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                result.frameSamples++;
                result.avgFps = ((result.avgFps * (result.frameSamples - 1)) + fps) / result.frameSamples;
                if (fps < result.minFps || result.minFps == 0f) result.minFps = fps;

                // 상태 전이
                foreach (var b in trackedBrains)
                {
                    if (b == null) continue;
                    if (lastStates.TryGetValue(b, out var prev))
                    {
                        if (prev != b.CurrentState)
                        {
                            result.stateTransitions++;
                            lastStates[b] = b.CurrentState;
                        }
                    }
                    else lastStates[b] = b.CurrentState;
                }

                // 자동 종료
                if (autoStopAfterSeconds > 0 && elapsed >= autoStopAfterSeconds)
                {
                    FinalizeProbe();
                    Debug.Log($"[T5.3 Probe] 자동 종료. 결과 박제 완료 (elapsed={elapsed:F2}s).");
                    enabled = false;
                }
            }
            catch (Exception e)
            {
                if (result != null) result.lastError = "Update 예외: " + e.Message;
                Debug.LogError($"[T5.3 Probe] Update 예외: {e}");
            }
        }

        // ==================================================================
        // 종료 (멱등)
        // ==================================================================
        public void FinalizeProbe()
        {
            if (result == null || result.sessionCompleted) return;

            try
            {
                result.durationSeconds = Time.unscaledTime - startTime;
                result.finalStateDistribution.Clear();
                result.fearByFaction.Clear();

                foreach (var b in trackedBrains)
                {
                    if (b == null) continue;

                    string stateKey = b.CurrentState.ToString();
                    result.finalStateDistribution.TryGetValue(stateKey, out int cnt);
                    result.finalStateDistribution[stateKey] = cnt + 1;

                    string fid = ResolveFactionId(b);
                    if (!result.fearByFaction.TryGetValue(fid, out var stat))
                    {
                        stat = new FactionFearStat();
                        result.fearByFaction[fid] = stat;
                    }
                    stat.count++;
                    stat.sum += b.fear;
                    if (b.fear < stat.min) stat.min = b.fear;
                    if (b.fear > stat.max) stat.max = b.fear;
                }

                result.sessionCompleted = true;
                phase = ProbePhase.Finalized;
            }
            catch (Exception e)
            {
                result.lastError = "Finalize 예외: " + e.Message;
                Debug.LogError($"[T5.3 Probe] Finalize 예외: {e}");
            }
        }

        // ==================================================================
        // [v3] 의존성 검증
        // ==================================================================
        private void ValidateDependencies()
        {
            result.missingDependencies.Clear();
            result.presentDependencies.Clear();

            foreach (var typeName in dependencyTypeNames)
            {
                var t = FindTypeByName(typeName);
                if (t == null)
                {
                    result.missingDependencies.Add($"{typeName} (type 미발견)");
                    continue;
                }

                var instance = UnityEngine.Object.FindAnyObjectByType(t);
                if (instance == null)
                    result.missingDependencies.Add(typeName);
                else
                    result.presentDependencies.Add(typeName);
            }
        }

        private static Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        private static Type FindTypeByName(string name)
        {
            if (_typeCache.TryGetValue(name, out var cached)) return cached;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == name)
                        {
                            _typeCache[name] = t;
                            return t;
                        }
                    }
                }
                catch { /* ignore reflection errors */ }
            }
            _typeCache[name] = null;
            return null;
        }

        // ==================================================================
        // [v3] Stub Mode — Mob의 Equipment 컴포넌트 임시 disable
        // ==================================================================
        private void ApplyStubMode()
        {
            string[] componentNamesToDisable =
            {
                "AICharacterEquipmentManager",
                "AICharacterEquipmentDebugger",
            };

            int count = 0;
            foreach (var typeName in componentNamesToDisable)
            {
                var t = FindTypeByName(typeName);
                if (t == null) continue;

                var found = UnityEngine.Object.FindObjectsByType(t, FindObjectsSortMode.None);
                foreach (var obj in found)
                {
                    if (obj is Behaviour b && b.enabled)
                    {
                        b.enabled = false;
                        disabledComponents.Add(b);
                        count++;
                    }
                }
            }

            Debug.Log($"[T5.3 Probe] Stub Mode 적용: {count}개 Equipment 컴포넌트 일시 disable. " +
                      "회귀 테스트 종료 시 자동 복원됨.");
        }

        private void RestoreStubMode()
        {
            int restored = 0;
            foreach (var b in disabledComponents)
            {
                if (b != null) { b.enabled = true; restored++; }
            }
            disabledComponents.Clear();
            if (restored > 0)
                Debug.Log($"[T5.3 Probe] Stub Mode 복원: {restored}개 컴포넌트 enabled=true.");
        }

        // ==================================================================
        // PanicChain
        // ==================================================================
        [ContextMenu("Trigger PanicChain (force-kill 1 Skeleton)")]
        public void TriggerPanicChain()
        {
            if (result == null) return;

            MobAIBrain victim = null;
            MobAIBrain neighbor = null;

            foreach (var b in trackedBrains)
            {
                if (b == null) continue;
                if (ResolveFactionId(b) != "skeleton") continue;
                if (victim == null) { victim = b; continue; }
                float d = Vector3.Distance(b.transform.position, victim.transform.position);
                if (neighbor == null || d < Vector3.Distance(neighbor.transform.position, victim.transform.position))
                    neighbor = b;
            }

            if (victim == null || neighbor == null)
            {
                result.panicChainTriggered = true;
                result.panicChainNote = "Skeleton 2마리 미발견 — PanicChain 검증 불가";
                Debug.LogWarning("[T5.3 Probe] " + result.panicChainNote);
                return;
            }

            result.panicChainFearBefore = neighbor.fear;
            result.panicChainTriggered = true;

            Debug.Log($"[T5.3 Probe] PanicChain 트리거: {victim.name} 강제 사망. " +
                      $"이웃 {neighbor.name} fear before={neighbor.fear:F3}");

            UnityEngine.Object.Destroy(victim.gameObject);
            StartCoroutine(MeasurePanicChainAfter(neighbor, 1.5f));
        }

        private System.Collections.IEnumerator MeasurePanicChainAfter(MobAIBrain neighbor, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (neighbor != null)
            {
                result.panicChainFearAfter = neighbor.fear;
                float delta = result.panicChainFearAfter - result.panicChainFearBefore;
                result.panicChainNote = delta > 0.05f
                    ? $"PASS — fear +{delta:F3} (전파 확인)"
                    : $"FAIL — fear delta {delta:F3} (전파 부족)";
                Debug.Log($"[T5.3 Probe] {result.panicChainNote}");
            }
            else result.panicChainNote = "이웃 Brain도 소멸됨 — 회귀 결과 부정확";
        }

        // ==================================================================
        // 로그 콜백 (자기 로그 제외)
        // ==================================================================
        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!isWarmedUp || result == null) return;
            if (!string.IsNullOrEmpty(condition) && condition.StartsWith("[T5.3 Probe]")) return;

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                result.errors++;
                if (result.errorMessages.Count < 20)
                    result.errorMessages.Add(Truncate(condition, 200));
            }
            else if (type == LogType.Warning)
            {
                result.warnings++;
                if (result.warningMessages.Count < 20)
                    result.warningMessages.Add(Truncate(condition, 200));
            }
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        private static string ResolveFactionId(MobAIBrain b)
        {
            string n = (b.name ?? "").ToLowerInvariant();
            if (n.Contains("goblin")) return "goblin";
            if (n.Contains("orc")) return "orc";
            if (n.Contains("skeleton")) return "skeleton";
            return "unknown";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
#endif
