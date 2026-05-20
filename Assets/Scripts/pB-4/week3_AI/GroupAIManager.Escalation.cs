// =============================================================================
// GroupAIManager.Escalation.cs  |  pB-4 Project — Week 5 Phase 3 (W5-B6)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   GroupAIManager 측 partial 영역 — Death Spiral 4 Tier 영역 정합.
//   3 자료 (Time / Threat / Encircle) 측 에스컬레이션 점수 계산 +
//   Tier 전환 + Gatekeeper 3 영역.
//
// 4 Tier (None / Alert / Critical / Collapse):
//   None    — 정상 전투 영역 (max 0.3 미만)
//   Alert   — 긴장 영역 (0.3 ~ 0.5)
//   Critical— Death Spiral 영역 진입 (0.5 ~ 0.8)
//   Collapse— 붕괴 영역 / 강제 Flee 영역 (0.8+)
//
// 3 에스컬레이션 자료:
//   Time     — combatElapsedTime ≥ 임계값 영역 (default 60s / 120s / 240s)
//   Threat   — _knownTargets.Count / aliveMembers 비율 영역
//   Encircle — 그룹 중심 측 적 영역 각도 분포 영역 (2D X/Z 평면)
//
// Gatekeeper 3 (Tier 전환 차단/가속 조건):
//   1) Pivot 생존    → Tier 상승 1 단계 강하 (안정화)
//   2) Vanguard 무력 → Tier 상승 1 단계 가속
//   3) 최소 멤버 수  → aliveMembers < minMembers 시 즉시 Collapse
//
// EscalationModifier 통합 (Phase 1 W5-B5 연계):
//   GroupArchetypeSO.escalationModifier (0.5~2.0) 영역 곱 보정.
//   Mad (1.5) — 가속 영역 / Stasis (0.7) — 감속 영역.
//
// =============================================================================
// [★ 변경 이력 — 2026-05-15: W5-B6 Phase 3 신규]
// =============================================================================
// 신규:
//   - EscalationTier enum + 자료 + ContextMenu 진단
//   - GroupArchetypeSO 측 escalationModifier 곱 영역 적용
//   - Gatekeeper 3 영역 (Pivot / Vanguard / MinMembers)
//   - OnEscalationTierChanged event
//
// ★ 특이사항-A: partial 클래스 영역
//   본 파일 영역 partial class GroupAIManager 영역 영역 — 기존 GroupAIManager.cs
//   영역 직접 변경 영역 부재 영역. 동일 namespace (TDA.PB4.AI) + 동일 클래스명
//   영역 영역 — Unity 측 자동 정합 영역.
//
// ★ 특이사항-B: Tick 호출 영역 영역
//   본 partial 측 UpdateEscalation_Tick() 영역 자동 호출 영역 부재 영역.
//   사용자 측 기존 GroupAIManager.cs 측 Update() 또는 별 사이클 영역 측
//   본 메서드 영역 호출 영역 필수 영역. 본 patch 측 InvokeRepeating 영역
//   영역 영역 영역 시도 영역 — OnEnable 영역 영역 partial 영역 중복 영역.
//   안전한 영역 — Awake/OnEnable 영역 영역 partial 메서드 영역 분기 (예:
//   AwakeEscalation_PartialInit) 영역 영역 사용자 측 호출 영역 영역.
//   현 turn 측 — 사용자 측 GroupAIManager 측 Update() 영역 한 줄 추가 영역 필요:
//     private void Update() {
//         // ...기존 코드...
//         UpdateEscalation_Tick();   // ★ W5-B6 추가
//     }
//
// ★ 특이사항-C: GroupArchetype 영역 영역 Inspector 할당 영역 영역 영역
//   groupArchetype 필드 영역 Inspector 측 .asset 영역 영역 드래그 영역 영역.
//   부재 시 escalationModifier = 1.0 영역 (보정 부재) 영역 영역.
//
// ★ 특이사항-D: Gatekeeper Pivot/Vanguard 영역 자동 영역 부재 영역
//   gatekeeperPivotAlive / gatekeeperVanguardAlive 영역 — 본 turn 측
//   FormationSlotAllocator 영역 영역 자동 동기화 영역 부재 영역. Inspector 측
//   수동 영역 영역 또는 사용자 측 별 영역 영역 자료 영역 영역. 본격 자동화 영역
//   — 별 의제 W5-B6-X 영역 영역 (FormationSlotAllocator 측 슬롯 영역 NPC
//   생존 영역 영역 → 본 영역 영역 자동 갱신 영역).
//
// ★ 특이사항-E: Encircle 영역 영역 2D 평면 영역
//   ComputeEncircleEscalation 영역 — X/Z 평면 영역 영역 영역 (Y 영역 무시).
//   본 영역 — 본격 3D 게임 영역 측 영역 충분 영역 (보통 평지 전투 영역).
//   다층 영역 / 공중 적 영역 영역 — 별 의제 영역.
//
// 신규 검증:
//   V-B6-01: EscalationTier enum 컴파일 통과 / Inspector 노출
//   V-B6-02: 3 자료 영역 (Time/Threat/Encircle) 정상 계산
//   V-B6-03: Tier 전환 영역 OnEscalationTierChanged 이벤트 발행
//   V-B6-04: EscalationModifier 영역 곱 보정 영역 정합 (Mad 1.5 / Stasis 0.7)
//   V-B6-05: Gatekeeper 3 영역 동작 (Pivot 생존 / Vanguard 무력 / MinMembers)
//   V-B6-06: ContextMenu "Print Escalation State" 출력 정합
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Data;
using TDA.PB4.Core;                  // ★ D-08 — EventBus
using TDA.PB4.Core.Events;            // ★ D-08 — SpeechTriggerContext
using TDA.PB4.Interfaces.Narrative;   // ★ D-08 — AlignmentChangeReason

namespace TDA.PB4.AI
{
    public partial class GroupAIManager
    {
        // ───────── enum ─────────

        public enum EscalationTier
        {
            /// <summary>[0] 정상 전투 — max score < 0.3.</summary>
            None,
            /// <summary>[1] 긴장 — 0.3 ~ 0.5.</summary>
            Alert,
            /// <summary>[2] Death Spiral 진입 — 0.5 ~ 0.8.</summary>
            Critical,
            /// <summary>[3] 붕괴 / 강제 Flee — 0.8+.</summary>
            Collapse
        }

        // ───────── Inspector 자료 ─────────

        [Header("━━━ W5-B6 Escalation ━━━━━━━━━━━━━━━")]
        [Tooltip("에스컬레이션 활성화. false 시 본 partial 영역 모든 영역 차단.")]
        [SerializeField] private bool enableEscalation = true;

        [Tooltip("[표시 전용] 현재 Tier.")]
        [SerializeField] private EscalationTier currentEscalationTier = EscalationTier.None;

        [Tooltip("GroupArchetypeSO 자산 — escalationModifier 영역 곱 보정 영역.\n" +
                 "★ 특이사항-C: 부재 시 보정 부재 (×1.0).")]
        [SerializeField] private GroupArchetypeSO groupArchetype;

        // ★ D-08+ 신규 — 런타임 자동 할당용 setter/getter (WorldAISpawnManager 측 호출)
        public GroupArchetypeSO GroupArchetype => groupArchetype;

        public void SetGroupArchetype(GroupArchetypeSO archetype)
        {
            if (archetype == null) return;
            this.groupArchetype = archetype;
            if (debugLog)
                Debug.Log($"[GroupAI:Escalation] groupArchetype 자동 할당 — " +
                          $"'{archetype.archetypeName}' (mod=×{archetype.escalationModifier:F2})", this);
        }

        [Header("━━━ Time 임계값 (초) ━━━━━━━━━━━━━━━━")]
        [SerializeField] private float timeAlertThreshold = 60f;
        [SerializeField] private float timeCriticalThreshold = 120f;
        [SerializeField] private float timeCollapseThreshold = 240f;

        [Header("━━━ Threat 임계값 (적/멤버 비율) ━━━━")]
        [SerializeField] private float threatAlertThreshold = 1.5f;
        [SerializeField] private float threatCriticalThreshold = 2.5f;
        [SerializeField] private float threatCollapseThreshold = 4.0f;

        [Header("━━━ Encircle 임계값 (분포 각도°) ━━━")]
        [Range(0f, 360f)] [SerializeField] private float encircleAlertAngle = 180f;
        [Range(0f, 360f)] [SerializeField] private float encircleCriticalAngle = 270f;
        [Range(0f, 360f)] [SerializeField] private float encircleCollapseAngle = 320f;

        [Header("━━━ Gatekeeper 3 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Pivot 슬롯 NPC 생존 영역 → Tier 상승 1 단계 강하 (안정화).\n" +
                 "★ 특이사항-D: 본 turn 측 수동 영역. FormationSlotAllocator 자동 동기화 부재.")]
        [SerializeField] private bool gatekeeperPivotAlive = true;

        [Tooltip("Vanguard 슬롯 NPC 무력 영역 → Tier 상승 1 단계 가속.\n" +
                 "★ 특이사항-D: 본 turn 측 수동 영역.")]
        [SerializeField] private bool gatekeeperVanguardAlive = true;

        [Tooltip("최소 멤버 수 — 살아있는 멤버 이하 시 즉시 Collapse 강제.")]
        [Range(1, 10)]
        [SerializeField] private int gatekeeperMinMembers = 2;

        [Header("━━━ Read Only ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private float timeEscalationScore;
        [SerializeField] private float threatEscalationScore;
        [SerializeField] private float encircleEscalationScore;

        // ───────── 공개 API ─────────

        public EscalationTier CurrentEscalationTier => currentEscalationTier;
        public float TimeEscalationScore => timeEscalationScore;
        public float ThreatEscalationScore => threatEscalationScore;
        public float EncircleEscalationScore => encircleEscalationScore;

        /// <summary>Tier 전환 시 발행. (oldTier, newTier).</summary>
        public event Action<EscalationTier, EscalationTier> OnEscalationTierChanged;

        // ───────── Tick (★ 특이사항-B: 외부 호출 필수) ─────────

        /// <summary>
        /// 에스컬레이션 갱신 — 사용자 측 GroupAIManager.Update() 측 한 줄 호출 영역.
        /// ★ 특이사항-B: 본 partial 측 자동 호출 부재. 외부 영역 영역 호출 필수.
        /// </summary>
        public void UpdateEscalation_Tick()
        {
            if (!enableEscalation) return;

            // 3 자료 계산
            timeEscalationScore     = ComputeTimeEscalation();
            threatEscalationScore   = ComputeThreatEscalation();
            encircleEscalationScore = ComputeEncircleEscalation();

            // EscalationModifier 곱 보정 (Phase 1 연계)
            float mod = (groupArchetype != null) ? groupArchetype.escalationModifier : 1f;
            float timeBoosted     = Mathf.Clamp01(timeEscalationScore * mod);
            float threatBoosted   = Mathf.Clamp01(threatEscalationScore * mod);
            float encircleBoosted = Mathf.Clamp01(encircleEscalationScore * mod);

            // Tier 결정 (최댓값)
            float maxScore = Mathf.Max(timeBoosted, Mathf.Max(threatBoosted, encircleBoosted));
            EscalationTier proposed = DetermineTier(maxScore);

            // Gatekeeper 적용
            EscalationTier final = ApplyGatekeepers(proposed);

            // 전환 이벤트
            if (final != currentEscalationTier)
            {
                EscalationTier old = currentEscalationTier;
                currentEscalationTier = final;
                OnEscalationTierChanged?.Invoke(old, final);
                HandleTierChangeForSpeech(old, final);   // ★ D-08
            }
        }

        // ───────── 3 자료 계산 ─────────

        /// <summary>Time 자료 — combatElapsedTime 측 단계별 영역 (0~1).</summary>
        private float ComputeTimeEscalation()
        {
            if (combatElapsedTime < timeAlertThreshold) return 0f;
            if (combatElapsedTime < timeCriticalThreshold)
                return Mathf.Lerp(0.3f, 0.5f,
                    (combatElapsedTime - timeAlertThreshold) /
                    Mathf.Max(0.01f, timeCriticalThreshold - timeAlertThreshold));
            if (combatElapsedTime < timeCollapseThreshold)
                return Mathf.Lerp(0.5f, 0.8f,
                    (combatElapsedTime - timeCriticalThreshold) /
                    Mathf.Max(0.01f, timeCollapseThreshold - timeCriticalThreshold));
            return 1f;
        }

        /// <summary>Threat 자료 — _knownTargets / aliveMembers 비율 (0~1).</summary>
        private float ComputeThreatEscalation()
        {
            int enemies = (_knownTargets != null) ? _knownTargets.Count : 0;
            int aliveAllies = CountAliveMembers_Esc();
            if (aliveAllies == 0) return 1f;  // 멤버 全사 → 최대

            float ratio = (float)enemies / Mathf.Max(aliveAllies, 1);
            if (ratio < threatAlertThreshold) return 0f;
            if (ratio < threatCriticalThreshold) return 0.5f;
            if (ratio < threatCollapseThreshold) return 0.8f;
            return 1f;
        }

        /// <summary>
        /// Encircle 자료 — 그룹 중심 측 적 영역 각도 분포 (X/Z 평면 영역).
        /// ★ 특이사항-E: 2D 평면 영역. Y 영역 무시.
        /// </summary>
        private float ComputeEncircleEscalation()
        {
            if (_knownTargets == null || _knownTargets.Count < 2) return 0f;

            Vector3 groupCenter = ComputeGroupCenter_Esc();
            var angles = new List<float>();

            for (int i = 0; i < _knownTargets.Count; i++)
            {
                var slot = _knownTargets[i];
                if (slot == null || slot.target == null) continue;
                Vector3 dir = slot.target.position - groupCenter;
                float a = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                if (a < 0f) a += 360f;
                angles.Add(a);
            }

            if (angles.Count < 2) return 0f;
            angles.Sort();

            // 최대 빈 각도 (gap) 영역 측정 영역
            float maxGap = 360f - (angles[angles.Count - 1] - angles[0]);
            for (int i = 1; i < angles.Count; i++)
                maxGap = Mathf.Max(maxGap, angles[i] - angles[i - 1]);

            // 적 분포 각도 = 360 − maxGap
            float spreadAngle = 360f - maxGap;
            if (spreadAngle < encircleAlertAngle) return 0f;
            if (spreadAngle < encircleCriticalAngle) return 0.5f;
            if (spreadAngle < encircleCollapseAngle) return 0.8f;
            return 1f;
        }

        // ───────── 보조 ─────────

        private Vector3 ComputeGroupCenter_Esc()
        {
            if (members == null || members.Count == 0) return transform.position;
            Vector3 sum = Vector3.zero;
            int cnt = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && m.brain != null) { sum += m.brain.transform.position; cnt++; }
            }
            return cnt > 0 ? sum / cnt : transform.position;
        }

        private int CountAliveMembers_Esc()
        {
            if (members == null) return 0;
            int cnt = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && m.brain != null && m.brain.gameObject.activeInHierarchy) cnt++;
            }
            return cnt;
        }

        private EscalationTier DetermineTier(float maxScore)
        {
            if (maxScore >= 0.8f) return EscalationTier.Collapse;
            if (maxScore >= 0.5f) return EscalationTier.Critical;
            if (maxScore >= 0.3f) return EscalationTier.Alert;
            return EscalationTier.None;
        }

        private EscalationTier ApplyGatekeepers(EscalationTier proposed)
        {
            // ★ D-09 — FormationSlotAllocator 측 LastResults 측 자동 동기
            RefreshGatekeeperFlagsFromFormation();

            // Gatekeeper 3: 최소 멤버 수 — 즉시 Collapse 강제
            if (CountAliveMembers_Esc() < gatekeeperMinMembers)
                return EscalationTier.Collapse;

            // Gatekeeper 1: Pivot 생존 → Tier 1 단계 강하 (안정화)
            if (gatekeeperPivotAlive && proposed > EscalationTier.Alert)
                return (EscalationTier)((int)proposed - 1);

            // Gatekeeper 2: Vanguard 무력 → Tier 1 단계 가속
            if (!gatekeeperVanguardAlive && proposed < EscalationTier.Collapse)
                return (EscalationTier)((int)proposed + 1);

            return proposed;
        }

#if UNITY_EDITOR
        [ContextMenu("Escalation: Print State")]
        private void PrintEscalationState()
        {
            float mod = (groupArchetype != null) ? groupArchetype.escalationModifier : 1f;
            Debug.Log($"[Escalation] Tier={currentEscalationTier} (mod=×{mod:F2})\n" +
                      $"  Time      = {timeEscalationScore:F2} (elapsed={combatElapsedTime:F1}s)\n" +
                      $"  Threat    = {threatEscalationScore:F2} (enemies={(_knownTargets != null ? _knownTargets.Count : 0)} / alive={CountAliveMembers_Esc()})\n" +
                      $"  Encircle  = {encircleEscalationScore:F2}\n" +
                      $"  Gatekeeper: Pivot={gatekeeperPivotAlive} / Vanguard={gatekeeperVanguardAlive} / MinMembers={gatekeeperMinMembers}");
        }

        [ContextMenu("Escalation: Force Tick")]
        private void ForceEscalationTick() => UpdateEscalation_Tick();

        [ContextMenu("Escalation: Force Tier → Alert")]
        private void ForceAlert()
        {
            var old = currentEscalationTier;
            currentEscalationTier = EscalationTier.Alert;
            OnEscalationTierChanged?.Invoke(old, currentEscalationTier);
            HandleTierChangeForSpeech(old, currentEscalationTier);   // ★ D-08
        }

        [ContextMenu("Escalation: Force Tier → Critical")]
        private void ForceCritical()
        {
            var old = currentEscalationTier;
            currentEscalationTier = EscalationTier.Critical;
            OnEscalationTierChanged?.Invoke(old, currentEscalationTier);
            HandleTierChangeForSpeech(old, currentEscalationTier);   // ★ D-08
        }

        [ContextMenu("Escalation: Force Tier → Collapse")]
        private void ForceCollapse()
        {
            var old = currentEscalationTier;
            currentEscalationTier = EscalationTier.Collapse;
            OnEscalationTierChanged?.Invoke(old, currentEscalationTier);
            HandleTierChangeForSpeech(old, currentEscalationTier);   // ★ D-08
        }
#endif

        // =====================================================================
        // ★ D-09 (Wk5 D1) — Gatekeeper Pivot/Vanguard 자동 동기
        //
        // 흐름:
        //   ApplyGatekeepers 진입 직전 → FormationSlotAllocator.LastResults 순회 →
        //   Pivot/Vanguard 슬롯 할당 NPC 측 alive 측 산출 →
        //   gatekeeperPivotAlive / gatekeeperVanguardAlive 자동 갱신.
        //
        // 사용 패턴 (Pull):
        //   ApplyGatekeepers() 호출 측 직전 시점 측 매 갱신 — push event 측 race 회피.
        //
        // 가드:
        //   - FormationSlotAllocator 측 씬 측 없음  →  silent 종료 (수동 Inspector 값 보존)
        //   - LastResults 측 비어있음  →  silent 종료
        // =====================================================================
        private FormationSlotAllocator _cachedFormationAllocator;

        private void RefreshGatekeeperFlagsFromFormation()
        {
            if (_cachedFormationAllocator == null)
                _cachedFormationAllocator = FindAnyObjectByType<FormationSlotAllocator>();
            if (_cachedFormationAllocator == null) return;

            var results = _cachedFormationAllocator.LastResults;
            if (results == null || results.Count == 0) return;

            bool pivotAlive = false;
            bool vanguardAlive = false;

            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.slotType != FormationSlotType.Pivot &&
                    r.slotType != FormationSlotType.Vanguard) continue;

                bool isAlive = IsNPCAliveByName_Esc(r.npcName);
                if (!isAlive) continue;

                if (r.slotType == FormationSlotType.Pivot)    pivotAlive    = true;
                if (r.slotType == FormationSlotType.Vanguard) vanguardAlive = true;
            }

            gatekeeperPivotAlive    = pivotAlive;
            gatekeeperVanguardAlive = vanguardAlive;
        }

        private bool IsNPCAliveByName_Esc(string npcName)
        {
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && m.brain != null && m.brain.gameObject.name == npcName)
                    return m.brain.gameObject.activeInHierarchy;
            }
            return false;
        }

        // =====================================================================
        // ★ D-08 (Wk5 D1) — Disaster Speech 통합
        //
        // 흐름:
        //   Tier 전환 → Collapse 진입 시점 → groupArchetype.onCollapseSpeechId
        //   를 EventBus.RaiseSpeechTrigger 로 publish → SpeechDispatcher 가 구독 →
        //   검문 통과 시 SpeechAssembler.Assemble → DialogueRenderer.Render.
        //
        // 가드:
        //   - prev == Collapse  →  중복 진입 차단
        //   - next != Collapse  →  무관 Tier 진입 차단
        //   - groupArchetype == null  →  Inspector 미할당 차단
        //   - onCollapseSpeechId 비어있음  →  자산 결함 차단
        // =====================================================================
        private void HandleTierChangeForSpeech(EscalationTier prev, EscalationTier next)
        {
            if (next != EscalationTier.Collapse) return;
            if (prev == EscalationTier.Collapse) return;
            if (groupArchetype == null) return;
            if (string.IsNullOrEmpty(groupArchetype.onCollapseSpeechId)) return;

            // 첫 살아있는 멤버 측 발화 주체 선정
            GroupMemberState speakerMember = null;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && m.brain != null && m.brain.gameObject.activeInHierarchy)
                {
                    speakerMember = m;
                    break;
                }
            }
            if (speakerMember == null) return;

            var ctx = new SpeechTriggerContext
            {
                characterId       = speakerMember.brain.gameObject.name,
                targetPlayerId    = 0UL,
                currentAlignment  = NPCAlignment.Hostile,
                previousAlignment = NPCAlignment.Hostile,
                reason            = AlignmentChangeReason.Manual
            };

            EventBus.RaiseSpeechTrigger(groupArchetype.onCollapseSpeechId, ctx);

            if (debugLog)
            {
                Debug.Log(
                    $"[GroupAI:Escalation] D-08 Disaster Speech 트리거 — " +
                    $"archetype='{groupArchetype.archetypeName}' " +
                    $"trigger='{groupArchetype.onCollapseSpeechId}' " +
                    $"speaker={speakerMember.brain.gameObject.name}");
            }
        }
    }
}
