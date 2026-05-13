// =============================================================================
// TraumaSystem.cs  |  pB-4 Project — Week 4 (의제 Wk4-3 본격 구현)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC 가 위험한 지형 탐험 / 동료 사망 / 명령 부담 누적 시 트라우마 발전.
//   3 단계 진화 + Crossroads 분기 + 영구 변형.
//
// 3 단계 진화:
//   [0] None              : 정상 상태
//   [1] AcuteShock        : 1~3 탐험 — 일시적 fear 페널티 (적응 또는 누적)
//   [2] Crossroads        : 결정적 분기점 (극복 vs 악화)
//                            Trust / Karma / alliesAlive 조건 검사 후 분기
//   [3] PermanentScarring : 영구 변형 (Stability -0.5 / Agreeable -0.2)
//                            한 번 진입 = 회복 불가
//
// Trust × Trauma 결합:
//   Trust 90 + Acute → fear -0.45 (V-Wk4-04 검증 기준)
//   Trust 0 + Permanent → fear × 1.5 (자기 보존 본능 강화)
//   Cooperation + Crossroads → fear × 0.8 (조심스러운 명령 거부)
//
// [Wk4-3 본격 명세 — 2026-05-12]
//   - NetworkBehaviour 상속 (NGO 2.0 / TrustMatrix 패턴 정합)
//   - ITraumaProvider 구현 (HumanoidAIBrain.cs line 628 정합)
//   - namespace TDA.PB4.AI (사용자 측 본체 정합 — using TDA.PB4.AI 그대로)
//   - 3 단계 진화 자동 관리
//   - ComputeFearModifier (Trust × Trauma 결합 — Wk4-2 / Wk4-6 에서 호출)
//   - GetFearMultiplier (ITraumaProvider — 본체 line 628 정합)
//
// 기반: 기술개발 개론서 v11 §5.4.3 C / Wk4 의제 종합 v2 §2.3
//
// [v1.1 — 2026-05-12] ITraumaProvider 인터페이스 멤버 추가
//   - CurrentStageIndex (int property) — 본체 ITraumaProvider 정의 정합
//   - ApplyShock(float intensity, string reason) — 외부 쇼크 적용
//
// [v1.6 — 2026-05-12] CheckStageProgression 버그 해소
//   - Overcome 후 Crossroads 재진입 차단 버그 해결
//   - lastCrossroadsOutcome == Pending 조건 제거 (단순 단계 비교만)
//   - 발견: §6.5 Worsened 시뮬레이션 시 4회 탐험인데도 단계 안 변함
// =============================================================================
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.Interfaces;                    // [v1.2] DilemmaPath enum
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    /// <summary>트라우마 3 단계 진화.</summary>
    public enum TraumaStage
    {
        None = 0,                  // 정상 (탐험 0 회 또는 회복)
        AcuteShock = 1,            // 1~3 탐험 — 일시적 페널티
        Crossroads = 2,            // 분기 시점 (극복 vs 악화)
        PermanentScarring = 3      // 영구 변형 (Stability -0.5 / Agreeable -0.2)
    }

    /// <summary>Crossroads 분기 결과.</summary>
    public enum CrossroadsOutcome
    {
        Pending = 0,               // 아직 결정 안 됨
        Overcome = 1,               // 극복 (Trust / Karma / alliesAlive 충분)
        Worsened = 2                // 악화 (Permanent Scarring 진입)
    }

    /// <summary>
    /// 트라우마 기록 — Dashboard / Save&Load 용.
    /// [v1.3] 사용자 측 PB4DebugDashboard v04/v05 의 traumaID 접근 호환.
    /// [v1.4] PB4_Day3_TestLab line 589 / PB4DebugDashboard line 1142 — name / severity 추가.
    /// </summary>
    [System.Serializable]
    public class TraumaRecord
    {
        public string traumaID = "None";
        public string name = "None";              // [v1.4] 사람이 읽는 이름 / PB4_Day3_TestLab 사용
        public float severity = 0f;                // [v1.4] 강도 0~1 / PB4DebugDashboard 사용
        public TraumaStage stage = TraumaStage.None;
        public int explorationCountAtEntry = 0;
        public float timestamp = 0f;
    }

    public class TraumaSystem : NetworkBehaviour, ITraumaProvider
    {
        // ==================================================================
        // Inspector 필드 — 현재 트라우마 상태 (기본 플레이어)
        // ==================================================================
        [Header("━━━ 트라우마 현재 상태 ━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("현재 트라우마 단계 (Read Only — 자동 진화).")]
        [SerializeField] private TraumaStage currentStage = TraumaStage.None;

        [Tooltip("위험 지형 탐험 누적 횟수 (1~3 → AcuteShock / 4+ → Crossroads).")]
        [SerializeField, Range(0, 10)] private int explorationCount = 0;

        [Tooltip("동료 사망 목격 누적 횟수.")]
        [SerializeField, Range(0, 10)] private int alliesDiedCount = 0;

        [Tooltip("최근 Crossroads 결과 (Pending 일 시 분기 결정 전).")]
        [SerializeField] private CrossroadsOutcome lastCrossroadsOutcome = CrossroadsOutcome.Pending;

        // ==================================================================
        // 임계 / 가중치 (Inspector 튜닝)
        // ==================================================================
        [Header("━━━ 단계 진입 임계 ━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("AcuteShock 진입 — 탐험 횟수 임계 (디폴트 1).")]
        [Range(1, 5)] public int acuteShockThreshold = 1;

        [Tooltip("Crossroads 진입 — 탐험 횟수 임계 (디폴트 3).")]
        [Range(2, 10)] public int crossroadsThreshold = 3;

        [Header("━━━ fear 보정 가중치 ━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("AcuteShock 시 fear 곱셈 (디폴트 1.3 — 30% 증가).")]
        [Range(0.5f, 3f)] public float acuteShockMultiplier = 1.3f;

        [Tooltip("Crossroads 시 fear 곱셈 (디폴트 1.5).")]
        [Range(0.5f, 3f)] public float crossroadsMultiplier = 1.5f;

        [Tooltip("PermanentScarring 시 fear 곱셈 (디폴트 1.8).")]
        [Range(0.5f, 3f)] public float permanentMultiplier = 1.8f;

        [Header("━━━ Crossroads 분기 조건 ━━━━━━━━━━━━━━━━━━")]

        [Tooltip("Overcome 조건 — Trust 임계 (Cooperation 50 이상).")]
        [Range(0f, 100f)] public float overcomeTrustThreshold = 50f;

        [Tooltip("Overcome 조건 — Karma 임계 (선/중립).")]
        [Range(-100f, 100f)] public float overcomeKarmaThreshold = 0f;

        [Header("━━━ Permanent Scarring 영구 변형 ━━━━━━━━━━━━")]

        [Tooltip("Permanent 진입 시 Stability 감소량 (Wk4-3 명세 -0.5).")]
        [Range(0f, 1f)] public float permanentStabilityDelta = 0.5f;

        [Tooltip("Permanent 진입 시 Agreeable 감소량 (Wk4-3 명세 -0.2).")]
        [Range(0f, 1f)] public float permanentAgreeableDelta = 0.2f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        // ==================================================================
        // 이벤트
        // ==================================================================
        /// <summary>트라우마 단계 전이 이벤트. 인자: (oldStage, newStage).</summary>
        public event Action<TraumaStage, TraumaStage> OnStageChanged;

        /// <summary>Crossroads 진입 이벤트 (분기 결정 필요).</summary>
        public event Action OnCrossroadsEntered;

        /// <summary>Permanent Scarring 진입 이벤트 (영구 변형 적용 시점).</summary>
        public event Action<float, float> OnPermanentScarring;   // (stabilityDelta, agreeableDelta)

        /// <summary>
        /// [v1.4] 탐험 완료 알림 (RegisterExploration / RegisterAllyDeath / ApplyShock 시 발행).
        /// 호출자: PB4DebugDashboard line 1185 / PB4_Day3_TestLab line 882
        /// 
        /// [v1.5] event 키워드 제거 — 외부 invoke 허용 (CS0079 해소).
        ///        외부에서 traumaSystem.OnExplorationCompleted?.Invoke() 가능.
        ///        += / -= / = / Invoke 모두 가능.
        /// </summary>
        public Action OnExplorationCompleted;

        // ==================================================================
        // 핵심 메서드 — RegisterExploration (위험 지형 탐험 시 호출)
        // ==================================================================

        /// <summary>
        /// 위험 지형 탐험 1 회 등록. 단계 자동 진화 확인.
        /// 호출자: HumanoidAIBrain BT 노드 / TerrainSemanticMapper.IsTraumaticTerrain 결과
        /// </summary>
        /// <param name="wasTraumatic">진짜 트라우마 유발 지형이면 true (false 시 무시).</param>
        public void RegisterExploration(bool wasTraumatic)
        {
            if (!wasTraumatic) return;

            explorationCount++;
            CheckStageProgression();

            // [v1.4] 탐험 완료 이벤트 발행
            OnExplorationCompleted?.Invoke();

            if (debugLog)
                Debug.Log($"[Trauma] {name}: 탐험 등록 (총 {explorationCount}회) — 단계 {currentStage}", this);
        }

        /// <summary>
        /// 동료 사망 목격 등록. 단계 진입 가속.
        /// 호출자: AbandonRescueResolver / 동료 사망 이벤트 수신.
        /// </summary>
        public void RegisterAllyDeath(string allyId)
        {
            alliesDiedCount++;
            // 동료 사망 = 탐험 보다 더 강한 트리거 (1 회 = 2 탐험 효과)
            explorationCount += 2;
            CheckStageProgression();

            // [v1.4] 탐험 완료 이벤트 발행
            OnExplorationCompleted?.Invoke();

            if (debugLog)
                Debug.Log($"[Trauma] {name}: 동료 사망 목격 [{allyId}] (총 {alliesDiedCount}회 / 탐험 환산 {explorationCount})", this);
        }

        // ==================================================================
        // 단계 진화 자동 관리
        // ==================================================================

        private void CheckStageProgression()
        {
            TraumaStage oldStage = currentStage;
            TraumaStage newStage = currentStage;

            // Permanent 는 회복 불가 — 진입 시 고정
            if (currentStage == TraumaStage.PermanentScarring)
                return;

            // 단계 결정
            if (explorationCount >= crossroadsThreshold)
            {
                // [v1.6] Crossroads 재진입 허용 — lastCrossroadsOutcome 조건 제거
                //   이전: lastCrossroadsOutcome == Pending 만 진입 → Overcome 후 재진입 차단 버그
                //   변경: 단순 단계 비교만 — Overcome / Worsened 후에도 새 탐험 누적 시 재진입
                if (currentStage != TraumaStage.Crossroads)
                {
                    newStage = TraumaStage.Crossroads;
                    OnCrossroadsEntered?.Invoke();
                }
            }
            else if (explorationCount >= acuteShockThreshold)
            {
                newStage = TraumaStage.AcuteShock;
            }
            else
            {
                newStage = TraumaStage.None;
            }

            if (newStage != oldStage)
            {
                currentStage = newStage;

                // ★ v1.3 — _activeTrauma 자동 갱신
                _activeTrauma.stage = newStage;
                _activeTrauma.traumaID = newStage == TraumaStage.None
                    ? "None"
                    : $"{newStage}_{Time.time:F1}";
                _activeTrauma.name = newStage.ToString();   // [v1.4] 사람 읽는 이름
                _activeTrauma.explorationCountAtEntry = explorationCount;
                _activeTrauma.timestamp = Time.time;

                OnStageChanged?.Invoke(oldStage, newStage);

                if (debugLog)
                    Debug.Log($"[Trauma] {name}: ⚠ 단계 전이 {oldStage} → {newStage} (탐험 {explorationCount}회 / id {_activeTrauma.traumaID})", this);
            }
        }

        // ==================================================================
        // Crossroads 분기 결정 (시나리오 / 트리거)
        // ==================================================================

        /// <summary>
        /// Crossroads 분기 결정. 호출자가 (trustValue, karmaValue, alliesAlive) 입력.
        /// 조건 만족 시 Overcome / 미충족 시 Worsened (Permanent 진입).
        /// </summary>
        /// <param name="trustValue">현재 Trust (0~100).</param>
        /// <param name="karmaValue">현재 Karma (-100~100).</param>
        /// <param name="alliesAlive">살아있는 동료 수.</param>
        /// <returns>분기 결과.</returns>
        public CrossroadsOutcome ResolveCrossroads(float trustValue, float karmaValue, int alliesAlive)
        {
            if (currentStage != TraumaStage.Crossroads)
            {
                if (debugLog)
                    Debug.LogWarning($"[Trauma] {name}: ResolveCrossroads 호출됨 — but 현재 {currentStage} (Crossroads 아님)", this);
                return CrossroadsOutcome.Pending;
            }

            // Overcome 조건 검사
            bool trustOK = trustValue >= overcomeTrustThreshold;
            bool karmaOK = karmaValue >= overcomeKarmaThreshold;
            bool alliesOK = alliesAlive > 0;

            CrossroadsOutcome outcome;
            if (trustOK && karmaOK && alliesOK)
            {
                outcome = CrossroadsOutcome.Overcome;
                // 극복 = 단계 회복
                TraumaStage oldStage = currentStage;
                currentStage = TraumaStage.AcuteShock;   // Acute 로 후퇴 (완전 None 아님)
                explorationCount = acuteShockThreshold;
                OnStageChanged?.Invoke(oldStage, currentStage);
            }
            else
            {
                outcome = CrossroadsOutcome.Worsened;
                ApplyPermanentScarring();
            }

            lastCrossroadsOutcome = outcome;
            if (debugLog)
                Debug.Log($"[Trauma] {name}: Crossroads 결정 → {outcome} " +
                          $"(trust {trustValue:F0}/{(trustOK ? "OK" : "X")}, " +
                          $"karma {karmaValue:F0}/{(karmaOK ? "OK" : "X")}, " +
                          $"allies {alliesAlive}/{(alliesOK ? "OK" : "X")})", this);

            return outcome;
        }

        /// <summary>
        /// Permanent Scarring 적용 — 영구 변형 이벤트 발행.
        /// 외부 (HumanoidAIBrain) 가 personality 갱신 처리.
        /// </summary>
        private void ApplyPermanentScarring()
        {
            TraumaStage oldStage = currentStage;
            currentStage = TraumaStage.PermanentScarring;

            // ★ v1.3 — _activeTrauma 갱신
            _activeTrauma.stage = currentStage;
            _activeTrauma.traumaID = $"Permanent_{Time.time:F1}";
            _activeTrauma.explorationCountAtEntry = explorationCount;
            _activeTrauma.timestamp = Time.time;

            OnPermanentScarring?.Invoke(permanentStabilityDelta, permanentAgreeableDelta);
            OnStageChanged?.Invoke(oldStage, currentStage);

            if (debugLog)
                Debug.Log($"[Trauma] {name}: ⚠⚠ PERMANENT SCARRING " +
                          $"(Stability -{permanentStabilityDelta:F2} / Agreeable -{permanentAgreeableDelta:F2})", this);
        }

        // ==================================================================
        // ITraumaProvider 구현 — fear 보정 (본체 line 628 정합)
        // ==================================================================

        /// <summary>
        /// 현재 단계 기반 fear 곱셈 계수 반환.
        /// 본체 HumanoidAIBrain.cs line 628 사용: adjustedFear = fear * GetFearMultiplier()
        /// </summary>
        public float GetFearMultiplier()
        {
            return currentStage switch
            {
                TraumaStage.None => 1.0f,
                TraumaStage.AcuteShock => acuteShockMultiplier,
                TraumaStage.Crossroads => crossroadsMultiplier,
                TraumaStage.PermanentScarring => permanentMultiplier,
                _ => 1.0f
            };
        }

        /// <summary>
        /// 현재 단계 인덱스 (0~3). TraumaStage enum 값과 동일.
        /// [0] None / [1] AcuteShock / [2] Crossroads / [3] PermanentScarring
        /// </summary>
        public int CurrentStageIndex => (int)currentStage;

        /// <summary>
        /// 외부 쇼크 적용. 호출자가 직접 강도 / 이유 지정.
        /// 강도 변환: intensity 1.0 = 2 탐험 효과 (즉시 단계 진행 가능).
        /// 
        /// 호출자 예:
        ///   - AbandonRescueResolver — 동료 유기 후 자기 쇼크
        ///   - HumanoidAIBrain — 명령 거부 / 도덕 부담 누적
        ///   - 시나리오 트리거 — 특정 이벤트 시 강제 쇼크
        /// </summary>
        /// <param name="intensity">쇼크 강도 (0~1).</param>
        /// <param name="reason">이유 (디버그 / 추적).</param>
        public void ApplyShock(float intensity, string reason)
        {
            // 강도 → 탐험 환산 (1.0 = 2 회 / 0.5 = 1 회)
            int explorationDelta = Mathf.Max(1, Mathf.RoundToInt(intensity * 2f));
            explorationCount += explorationDelta;

            CheckStageProgression();

            // [v1.4] severity 저장 + 탐험 완료 이벤트 발행
            _activeTrauma.severity = intensity;
            _activeTrauma.name = reason ?? "Shock";
            OnExplorationCompleted?.Invoke();

            if (debugLog)
                Debug.Log($"[Trauma] {name}: ApplyShock " +
                          $"(intensity {intensity:F2} / reason {reason}) " +
                          $"→ +{explorationDelta} 탐험 (총 {explorationCount}) / 단계 {currentStage}", this);
        }

        // ==================================================================
        // Wk4 추가 — ComputeFearModifier (Trust × Trauma 결합)
        // ==================================================================

        /// <summary>
        /// Wk4 추가 — Trust × Trauma 결합 fear 보정.
        /// V-Wk4-04 검증: Trust 90 + Acute → fear -0.45 자연 발현.
        /// 
        /// 공식:
        ///   baseModifier = GetFearMultiplier()
        ///   trustBonus = (trustValue / 100) × 0.5    (Trust 100 → -0.5 보정)
        ///   adjustedFear = originalFear × baseModifier - trustBonus
        /// </summary>
        /// <param name="originalFear">원래 fear 값 (0~1).</param>
        /// <param name="trustValue">현재 Trust (0~100).</param>
        /// <returns>보정된 fear (0~1 clamp).</returns>
        public float ComputeFearModifier(float originalFear, float trustValue)
        {
            float baseModifier = GetFearMultiplier();
            float trustBonus = (trustValue / 100f) * 0.5f;   // Trust 100 → -0.5
            float adjusted = originalFear * baseModifier - trustBonus;
            return Mathf.Clamp01(adjusted);
        }

        // ==================================================================
        // 유틸리티 / 상태 조회
        // ==================================================================

        /// <summary>현재 단계.</summary>
        public TraumaStage CurrentStage => currentStage;

        /// <summary>탐험 누적 횟수.</summary>
        public int ExplorationCount => explorationCount;

        /// <summary>동료 사망 누적 횟수.</summary>
        public int AlliesDiedCount => alliesDiedCount;

        /// <summary>Crossroads 마지막 결과.</summary>
        public CrossroadsOutcome LastCrossroadsOutcome => lastCrossroadsOutcome;

        /// <summary>Permanent 진입 여부.</summary>
        public bool IsPermanentlyScarred => currentStage == TraumaStage.PermanentScarring;

        // ==================================================================
        // ★ v1.2 — 사용자 측 기존 API 호환 (Dashboard / DilemmaPivotResolver)
        // ==================================================================

        // ★ v1.3 — ActiveTrauma 필드 (TraumaRecord 객체, null 회피)
        private TraumaRecord _activeTrauma = new TraumaRecord();

        /// <summary>
        /// 현재 활성 트라우마 기록.
        /// 호출자: PB4DebugDashboard v04 line 85 — ActiveTrauma.traumaID 접근
        /// 
        /// 단계 변화 시 자동 갱신:
        ///   None      → traumaID = "None"
        ///   AcuteShock → traumaID = "Acute_<timestamp>"
        ///   Crossroads → traumaID = "Crossroads_<timestamp>"
        ///   Permanent  → traumaID = "Permanent_<timestamp>"
        /// </summary>
        public TraumaRecord ActiveTrauma => _activeTrauma;

        /// <summary>
        /// 마지막 trauma 이후 탐험 횟수. explorationCount alias.
        /// 호출자: PB4DebugDashboard v04 line 86 / v05 line 57
        /// </summary>
        public int ExplorationsSinceTrauma => explorationCount;

        /// <summary>
        /// DilemmaPivot 분기 결과로 Crossroads 결정.
        /// 호출자: DilemmaPivotResolver line 161
        /// 
        /// DilemmaPath → Crossroads 매핑:
        ///   StrongRefuse  → Overcome (자기 가치 확인 → 극복)
        ///   PartialRefuse → Pending (중간 — 결정 보류)
        ///   Comply        → Worsened (도덕 부담 누적 → Permanent)
        /// 
        /// ★ 본 시그니처가 사용자 측 line 161 의 실제 호출과 다를 경우
        ///   line 161 코드 공유 부탁드림 — 시그니처 정확화 진행.
        /// </summary>
        public CrossroadsOutcome ResolveCrossroadsByPivot(DilemmaPath path)
        {
            // Crossroads 단계 아니면 무효
            if (currentStage != TraumaStage.Crossroads)
            {
                if (debugLog)
                    Debug.LogWarning($"[Trauma] {name}: ResolveCrossroadsByPivot ({path}) " +
                                     $"— but 현재 {currentStage} (Crossroads 아님)", this);
                return CrossroadsOutcome.Pending;
            }

            CrossroadsOutcome outcome;
            switch (path)
            {
                case DilemmaPath.StrongRefuse:
                    // 강력 거부 = 자기 가치 확인 = 극복 (Acute 로 후퇴)
                    outcome = CrossroadsOutcome.Overcome;
                    {
                        TraumaStage oldStage = currentStage;
                        currentStage = TraumaStage.AcuteShock;
                        explorationCount = acuteShockThreshold;
                        OnStageChanged?.Invoke(oldStage, currentStage);
                    }
                    break;

                case DilemmaPath.Comply:
                    // 따름 = 도덕 부담 = 악화 (Permanent 진입)
                    outcome = CrossroadsOutcome.Worsened;
                    ApplyPermanentScarring();
                    break;

                case DilemmaPath.PartialRefuse:
                default:
                    // 중간 = 결정 보류 (그대로 Crossroads 유지)
                    outcome = CrossroadsOutcome.Pending;
                    break;
            }

            lastCrossroadsOutcome = outcome;

            if (debugLog)
                Debug.Log($"[Trauma] {name}: ResolveCrossroadsByPivot ({path}) " +
                          $"→ {outcome} / 단계 {currentStage}", this);

            return outcome;
        }

        /// <summary>
        /// ★ v1.3 — 사용자 측 과거 시그니처 호환 — bool 매개변수.
        /// 호출자: DilemmaPivotResolver line 161 — 단순 true/false 입력
        /// 
        /// bool 매핑:
        ///   true  → Overcome (극복 — AcuteShock 후퇴)
        ///   false → Worsened (악화 — PermanentScarring 진입)
        /// </summary>
        public CrossroadsOutcome ResolveCrossroadsByPivot(bool overcame)
        {
            if (currentStage != TraumaStage.Crossroads)
            {
                if (debugLog)
                    Debug.LogWarning($"[Trauma] {name}: ResolveCrossroadsByPivot ({overcame}) " +
                                     $"— but 현재 {currentStage} (Crossroads 아님)", this);
                return CrossroadsOutcome.Pending;
            }

            CrossroadsOutcome outcome;
            if (overcame)
            {
                outcome = CrossroadsOutcome.Overcome;
                TraumaStage oldStage = currentStage;
                currentStage = TraumaStage.AcuteShock;
                explorationCount = acuteShockThreshold;

                // _activeTrauma 갱신
                _activeTrauma.stage = currentStage;
                _activeTrauma.traumaID = $"AcuteShock_{Time.time:F1}";
                _activeTrauma.explorationCountAtEntry = explorationCount;
                _activeTrauma.timestamp = Time.time;

                OnStageChanged?.Invoke(oldStage, currentStage);
            }
            else
            {
                outcome = CrossroadsOutcome.Worsened;
                ApplyPermanentScarring();
            }

            lastCrossroadsOutcome = outcome;

            if (debugLog)
                Debug.Log($"[Trauma] {name}: ResolveCrossroadsByPivot ({overcame}) " +
                          $"→ {outcome} / 단계 {currentStage}", this);

            return outcome;
        }

        // ==================================================================
        // ContextMenu 디버그
        // ==================================================================

        [ContextMenu("[Wk4-3] Print Trauma State")]
        private void PrintState()
        {
            Debug.Log($"═══ Wk4-3 TraumaSystem State ═══\n" +
                      $"  NPC                  = {name}\n" +
                      $"  Current Stage        = {currentStage}\n" +
                      $"  Exploration Count    = {explorationCount}\n" +
                      $"  Allies Died Count    = {alliesDiedCount}\n" +
                      $"  Last Crossroads      = {lastCrossroadsOutcome}\n" +
                      $"  Fear Multiplier      = {GetFearMultiplier():F2}\n" +
                      $"  Test: fear=0.5 trust=60 → ComputeFearModifier = {ComputeFearModifier(0.5f, 60f):F2}", this);
        }

        [ContextMenu("[Wk4-3] Simulate: Trigger AcuteShock (1 exploration)")]
        private void DebugTriggerAcute()
        {
            RegisterExploration(true);
            Debug.Log($"[Wk4-3 Sim] AcuteShock 시뮬레이션 → 단계 {currentStage}", this);
        }

        [ContextMenu("[Wk4-3] Simulate: Trigger Crossroads (3 explorations)")]
        private void DebugTriggerCrossroads()
        {
            for (int i = 0; i < 3; i++)
                RegisterExploration(true);
            Debug.Log($"[Wk4-3 Sim] Crossroads 시뮬레이션 → 단계 {currentStage}", this);
        }

        [ContextMenu("[Wk4-3] Simulate: Resolve Crossroads - Overcome (trust 90 / karma 50 / allies 3)")]
        private void DebugResolveOvercome()
        {
            // 강제 Crossroads 진입
            if (currentStage != TraumaStage.Crossroads)
            {
                for (int i = 0; i < crossroadsThreshold; i++)
                    RegisterExploration(true);
            }
            var outcome = ResolveCrossroads(90f, 50f, 3);
            Debug.Log($"[Wk4-3 Sim] Overcome 시도 → 결과 {outcome} / 단계 {currentStage}", this);
        }

        [ContextMenu("[Wk4-3] Simulate: Resolve Crossroads - Worsened (trust 20 / karma -50 / allies 0)")]
        private void DebugResolveWorsened()
        {
            // 강제 Crossroads 진입
            if (currentStage != TraumaStage.Crossroads)
            {
                for (int i = 0; i < crossroadsThreshold; i++)
                    RegisterExploration(true);
            }
            var outcome = ResolveCrossroads(20f, -50f, 0);
            Debug.Log($"[Wk4-3 Sim] Worsened 시도 → 결과 {outcome} / 단계 {currentStage}", this);
        }

        [ContextMenu("[Wk4-3] V-Wk4-04 Test: Trust 90 + Acute → fear -0.45")]
        private void DebugVerifyTrustTraumaCombination()
        {
            // 단계 설정 — Acute
            if (currentStage != TraumaStage.AcuteShock)
            {
                explorationCount = acuteShockThreshold;
                currentStage = TraumaStage.AcuteShock;
            }

            // V-Wk4-04 시뮬레이션
            float originalFear = 0.50f;
            float trustValue = 90f;
            float adjusted = ComputeFearModifier(originalFear, trustValue);
            float fearChange = adjusted - originalFear;

            Debug.Log($"═══ V-Wk4-04 Test — Trust × Trauma 결합 ═══\n" +
                      $"  Original Fear        = {originalFear:F2}\n" +
                      $"  Trust                = {trustValue:F0}\n" +
                      $"  Stage                = {currentStage}\n" +
                      $"  Base Multiplier      = {GetFearMultiplier():F2}\n" +
                      $"  Trust Bonus          = {(trustValue / 100f * 0.5f):F2}\n" +
                      $"  Adjusted Fear        = {adjusted:F2}\n" +
                      $"  Fear Change          = {fearChange:F2} (★ V-Wk4-04 기준 -0.45)", this);
        }

        [ContextMenu("[Wk4-3] Reset Trauma State")]
        private void DebugReset()
        {
            currentStage = TraumaStage.None;
            explorationCount = 0;
            alliesDiedCount = 0;
            lastCrossroadsOutcome = CrossroadsOutcome.Pending;
            _activeTrauma = new TraumaRecord();   // ★ v1.3 — TraumaRecord 리셋
            Debug.Log($"[Trauma] {name}: 상태 리셋", this);
        }

        // ==================================================================
        // OnValidate — Inspector 변경 시 단계 자동 재평가
        // ==================================================================
        private void OnValidate()
        {
            // Edit 모드 — explorationCount 변경 시 단계 자동 재평가
            CheckStageProgression();
        }
    }
}