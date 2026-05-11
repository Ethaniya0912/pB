// =============================================================================
// BaseAIBrain.cs  |  pB-4 Project — Week 0 → Week 3 Phase 4 (Tier 2 우선 디렉션)
// Layer  : L3 Domain (AI 공통 부모)
// Owner  : Person A
//
// 역할:
//   MobAIBrain과 HumanoidAIBrain의 공통 부모 추상 클래스.
//   생득 욕구 4종, 블랙보드 참조, 유틸리티 스코어러 참조, 그룹AI 참조를 포함.
//
// [Week 2 Day 1 T1.3 수정 — v2]
//   1) Awake에서 Stub 직접 생성 → null-coalesce 패턴 전환
//   2) InjectBlackboard(GameBlackboard) public 메서드 추가
//   3) IsStubFree() public 메서드 추가
//
// [v3 NGO 2.0 개정 — 2026-04-23]
//   - MonoBehaviour → NetworkBehaviour 상속
//   - Awake() → OnNetworkSpawn() 이관
//
// [★ v4 Phase 4 — Tier 2 우선 디렉션 마이그레이션 — 2026-05-04]
//   ★ 변경 사항:
//     [추가] UpdateFearFromTerrain v4 — Tier 2 비트마스크 17행 매핑 (Track B)
//     [추가] GetPersonalityFearMultiplier() 가상 메서드 (자식 override 진입점)
//     [폐기] Wk3 단순 태그 5종 트랙 (wk3Bonus) — Tier 2 가 같은 의미 더 풍부히 표현
//     [보존] Wk5+ 복합 태그 트랙 (legacyBonus) — FuzzyTagConverter 미래 작업 대비
//     [의존] WorldAIContextManager.Instance (Sample 직접 호출 — BG editor BB 우회)
//   ★ 흐름:
//     Track A: legacyBonus (Wk5+ 복합 태그 누적)  ← v3.5 보존
//     Track B: tier2Bonus (★ Phase 4 신규)        ← Personality 곱셈 적용
//   ★ Personality 곱셈은 가상 메서드로 분리:
//     - BaseAIBrain.GetPersonalityFearMultiplier() = 1.0 (default)
//     - HumanoidAIBrain.GetPersonalityFearMultiplier() = Lerp(0.5, 1.5, 1-stability)
// =============================================================================
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;  // [v3.4 §5 L1] NavMesh 보정용
using TDA.PB4.Interfaces;     // ★ v4 — ContextSnapshot (PB4_Wk3Interfaces.cs)
using TDA.PB4.Interfaces.Core;
using TDA.PB4.Interfaces.Intelligence;
using TDA.PB4.AI.Context;     // ★ v4 — WorldAIContextManager.Instance
using CaveSystem;              // ★ v4 — IdentityTag / StateTag / EventTag / PotentialTag

namespace TDA.PB4.AI
{
    public abstract class BaseAIBrain : NetworkBehaviour
    {
        // ==================================================================
        // 공통 생득 욕구 4종 (0.0 ~ 1.0)
        // ==================================================================
        [Header("Survival Drives (생득 욕구)")]
        [Range(0f, 1f)] public float fear = 0.3f;
        [Range(0f, 1f)] public float hunger = 0.2f;
        [Range(0f, 1f)] public float greed = 0.1f;
        [Range(0f, 1f)] public float fatigue = 0.1f;

        // ==================================================================
        // 인터페이스 참조
        // ==================================================================
        protected IBlackboard blackboard;
        protected IUtilityScorer utilityScorer;

        // 그룹 AI 참조
        [Header("Group AI")]
        [Tooltip("소속 그룹 AI 매니저. Inspector에서 연결하거나 런타임에 할당.")]
        public MonoBehaviour groupMindRef;

        // ==================================================================
        // [v3.4 §5 L1] Safe Zone Memory — 도주/후퇴 시 활용
        //   개인 메모리 (lastSafePositions): 다수 보유. 가까운 곳 우선 사용.
        //   spawn 시점, 평화 상태 일정 위치, Cave 태그(Stage 2+) 등에서 자동 기록.
        //   펙션 공유는 GroupAIManager.factionSafeZone 참조.
        // ==================================================================
        [Header("Safe Zone Memory (§5 L1)")]
        [Tooltip("[표시 전용] 개인이 기억하는 안전지대 위치 목록. 가까운 곳 우선 도주. " +
                 "spawn 시점 자동 기록 + 추후 Cave 태그 검색.")]
        [SerializeField] private List<Vector3> lastSafePositions = new List<Vector3>();

        [Tooltip("최대 기억 안전지대 수. 초과 시 가장 오래된 항목 제거.")]
        [SerializeField] private int maxRememberedSafeZones = 5;

        /// <summary>[v3.4 §5 L1] 개인 안전지대 메모리 (read-only).</summary>
        public IReadOnlyList<Vector3> LastSafePositions => lastSafePositions;

        /// <summary>
        /// [v3.4 §5 L1] 안전지대 위치 기록. ring buffer 형태로 maxRememberedSafeZones 캡.
        /// 중복 위치 (1m 이내)는 추가 안 함.
        ///
        /// [v3.4 §5 L1 보강] NavMesh 외부 위치는 가장 가까운 NavMesh 점으로 보정 후 저장.
        ///   spawn 위치가 NavMesh 가장자리에 걸치는 케이스 대응.
        ///   5m 내 NavMesh 없으면 기록 skip (안전지대 후보 자격 없음).
        /// </summary>
        public void RememberSafePosition(Vector3 pos)
        {
            // NavMesh 보정 — 가장 가까운 NavMesh 점으로 변환
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                if (Vector3.Distance(pos, hit.position) > 0.1f)
                {
                    Debug.Log($"[{name}] RememberSafePosition: NavMesh 보정 " +
                              $"({pos.x:F1},{pos.y:F1},{pos.z:F1}) → " +
                              $"({hit.position.x:F1},{hit.position.y:F1},{hit.position.z:F1})");
                }
                pos = hit.position;
            }
            else
            {
                Debug.LogWarning($"[{name}] RememberSafePosition: NavMesh 5m 내 없음 → 기록 skip ({pos})");
                return;
            }

            // 중복 검사 — 1m 이내 기존 항목 있으면 skip
            for (int i = 0; i < lastSafePositions.Count; i++)
            {
                if (Vector3.Distance(lastSafePositions[i], pos) < 1f) return;
            }
            lastSafePositions.Add(pos);
            if (lastSafePositions.Count > maxRememberedSafeZones)
                lastSafePositions.RemoveAt(0);
        }

        /// <summary>
        /// [v3.4 §5 L1] 자기 위치 기준 가장 가까운 안전지대 반환.
        /// 메모리 비어있으면 currentPos 그대로 반환 (도주 안 함과 같음).
        /// </summary>
        public Vector3 GetClosestSafePosition()
        {
            if (lastSafePositions.Count == 0) return transform.position;

            Vector3 my = transform.position;
            Vector3 best = lastSafePositions[0];
            float bestDist = Vector3.Distance(my, best);
            for (int i = 1; i < lastSafePositions.Count; i++)
            {
                float d = Vector3.Distance(my, lastSafePositions[i]);
                if (d < bestDist) { best = lastSafePositions[i]; bestDist = d; }
            }
            return best;
        }

        /// <summary>
        /// [v3.4 §5 L1] 개인 메모리 + 펙션 공유 SafeZone 통합 후 가까운 곳 반환.
        /// 펙션 공유는 GroupAIManager.factionSafeZone (있으면).
        ///
        /// [v3.4 §5 L1 보강] 펙션 SafeZone도 NavMesh 보정 후 후보 등록.
        /// </summary>
        public Vector3 GetClosestSafePositionWithFaction()
        {
            Vector3 my = transform.position;
            Vector3 best = my;
            float bestDist = float.MaxValue;
            bool found = false;

            // 개인 메모리는 RememberSafePosition에서 이미 NavMesh 보정됨
            foreach (var pos in lastSafePositions)
            {
                float d = Vector3.Distance(my, pos);
                if (d < bestDist) { best = pos; bestDist = d; found = true; }
            }

            // 펙션 공유 SafeZone — NavMesh 보정 후 후보 등록
            var grp = groupMindRef as GroupAIManager;
            if (grp != null && grp.HasFactionSafeZone)
            {
                Vector3 facPos = grp.FactionSafeZone;
                if (NavMesh.SamplePosition(facPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    facPos = hit.position;
                    float d = Vector3.Distance(my, facPos);
                    if (d < bestDist) { best = facPos; bestDist = d; found = true; }
                }
                // NavMesh 5m 내 없으면 펙션 SafeZone 후보 skip
            }

            return found ? best : my;
        }

        /// <summary>[v3.4 §5 L1] 안전지대 메모리 초기화 (디버그/테스트용).</summary>
        [ContextMenu("★ Wk3 Test/Clear Safe Zone Memory")]
        public void ClearSafeZoneMemory()
        {
            lastSafePositions.Clear();
            Debug.Log($"[{name}] 안전지대 메모리 초기화됨.");
        }

        // ==================================================================
        // [v3.4 §5 L1 Stage 2.1] Tactical Retreat — 전략적 후퇴 명령
        //   fear (본능적 공포)와 별개의 합리적 후퇴 신호.
        //   GroupAIManager.ReevaluateTacticalRetreat()에서 그룹 차원 결정.
        //   조건 충족 (HP < threshold 등) → orderedRetreat = true.
        //   PB4DecisionAdapter가 BB[UtilityWinner]="Flee" 강제 push.
        //   BT는 fear와 무관하게 FleeDuel 분기 진입 → InitiateRetreat.
        // ==================================================================
        [Header("Tactical Retreat (§5 L1 Stage 2.1)")]
        [Tooltip("[표시 전용] 그룹 차원 전략적 후퇴 명령. fear (본능)와 독립.")]
        [SerializeField] private bool orderedRetreat = false;

        /// <summary>[v3.4 §5 Stage 2.1] 그룹 차원 전략적 후퇴 명령 플래그.</summary>
        public bool OrderedRetreat
        {
            get => orderedRetreat;
            set => orderedRetreat = value;
        }

        // ==================================================================
        // Debug
        // ==================================================================
        [Header("Debug")]
        [Tooltip("상세 로그 출력 여부.")]
        [SerializeField] protected bool verboseLogging = false;

        // ★ v4 — Tier 2 fear 변동 디버그 로그 토글
        [Tooltip("ON → Tier 2 fear 변동 시 비트마스크 → fearDelta 분해 로그 출력 (디버깅용).")]
        [SerializeField] protected bool tier2FearVerbose = false;

        // ==================================================================
        // 추상 메서드
        // ==================================================================
        public abstract void UpdateDecision();
        public abstract void ExecuteBTNode(string goalId);

        // ==================================================================
        // ★ v4 — UpdateFearFromTerrain 2 트랙
        // ==================================================================

        /// <summary>
        /// 지형 정보를 fear 에 반영.
        ///
        /// ★ v4 (Phase 4) 트랙 구조:
        ///   - Track A — legacyBonus: Wk5+ 복합 태그 (NarrowPath/SpookyCave 등) 누적
        ///   - Track B — tier2Bonus:  ★ Tier 2 비트마스크 17행 매핑 + Personality 곱셈
        ///
        /// ★ Wk3 단순 태그 5종 트랙 (wk3Bonus) 폐기 — Tier 2 가 같은 의미 더 풍부히 표현.
        ///   v3.5 의 case "Narrow"/"Dense"/"HighGround"/"Open"/"Sparse" 모두 제거.
        /// </summary>
        protected virtual void UpdateFearFromTerrain()
        {
            // ── Track A: Wk5+ 복합 태그 (legacyBonus) — 보존 ─────
            // FuzzyTagConverter 가 미래에 활성화될 시 동작. 현재는 사실상 비어있음.
            float legacyBonus = 0f;
            if (blackboard != null)
            {
                var tags = blackboard.GetActiveTerrainTags();
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        switch (tag)
                        {
                            case "NarrowPath": legacyBonus += 0.10f; break;
                            case "SpookyCave": legacyBonus += 0.15f; break;
                            case "DeathTrap": legacyBonus += 0.25f; break;
                            case "DifficultEscape": legacyBonus += 0.05f; break;
                                // ★ Wk3 5종 (case "Narrow" / "Dense" 등) Phase 4 에서 폐기.
                        }
                    }
                }
            }

            // ── Track B: ★ Tier 2 비트마스크 17행 매핑 (Phase 4 신규) ──
            float tier2Bonus = 0f;
            bool hasTier2 = TryComputeTier2FearBonus(out tier2Bonus);

            // ── 적용 ─────────────────────────────────────────────
            // Track A: 누적 동작 (Wk5+ 한 시점 발현 가정)
            if (legacyBonus != 0f)
            {
                fear = Mathf.Clamp01(fear + legacyBonus);
            }

            // Track B: 목표값 도달 (Tier 2 매 틱 발현, BlackboardUpdater 10Hz)
            if (hasTier2)
            {
                const float FEAR_BASE = 0.20f;
                float tier2Target = Mathf.Clamp01(FEAR_BASE + tier2Bonus);
                if (fear < tier2Target)
                {
                    fear = tier2Target;
                }
            }
        }

        /// <summary>
        /// Tier 2 비트마스크에서 fearDelta 계산. Personality 곱셈 적용.
        /// 17행 매핑: fear ↑ 8행 + fear ↓ 4행 + 중립/이벤트 5행.
        /// </summary>
        /// <returns>Tier 2 컨텍스트 사용 가능 여부 (false 시 폴백 동작)</returns>
        private bool TryComputeTier2FearBonus(out float tier2Bonus)
        {
            tier2Bonus = 0f;

            var ctx = WorldAIContextManager.Instance;
            if (ctx == null || !ctx.IsInjected) return false;

            ContextSnapshot snap = ctx.Sample(transform.position);

            // ─── ① fear ↑ — 위험 인지 (8행) ───────────────────
            // STATE
            if ((snap.stateTags & (uint)StateTag.STATE_NARROW) != 0) tier2Bonus += 0.30f;
            if ((snap.stateTags & (uint)StateTag.STATE_CRITICAL_RISK) != 0) tier2Bonus += 0.40f;
            if ((snap.stateTags & (uint)StateTag.STATE_DENSE) != 0) tier2Bonus += 0.15f;
            if ((snap.stateTags & (uint)StateTag.STATE_ENCLOSED) != 0) tier2Bonus += 0.15f;
            // IDENTITY
            if ((snap.identityTags & (uint)IdentityTag.IDENTITY_AMBUSH) != 0) tier2Bonus += 0.40f;
            if ((snap.identityTags & (uint)IdentityTag.IDENTITY_HAZARD) != 0) tier2Bonus += 0.30f;
            // POTENTIAL
            if ((snap.potentialTags & (uint)PotentialTag.POTENTIAL_BOTTLENECK) != 0) tier2Bonus += 0.25f;
            if ((snap.potentialTags & (uint)PotentialTag.POTENTIAL_FALL_HAZARD) != 0) tier2Bonus += 0.20f;

            // ─── ② fear ↓ — 안전감 (4행) ──────────────────────
            // STATE
            if ((snap.stateTags & (uint)StateTag.STATE_GRAND) != 0) tier2Bonus -= 0.20f;
            if ((snap.stateTags & (uint)StateTag.STATE_OPEN) != 0) tier2Bonus -= 0.15f;
            // IDENTITY
            if ((snap.identityTags & (uint)IdentityTag.IDENTITY_HUB) != 0) tier2Bonus -= 0.10f;
            // POTENTIAL
            if ((snap.potentialTags & (uint)PotentialTag.POTENTIAL_VANTAGE_POINT) != 0) tier2Bonus -= 0.10f;

            // ─── ③ 중립 / 이벤트 (5행) ────────────────────────
            // STATE
            if ((snap.stateTags & (uint)StateTag.STATE_BOUNDARY) != 0) tier2Bonus += 0.10f;
            // EVENT
            if ((snap.eventTags & (uint)EventTag.EVENT_REPOSITIONED) != 0) tier2Bonus += 0.15f;
            if ((snap.eventTags & (uint)EventTag.EVENT_CHAMBER_LOCKED) != 0) tier2Bonus += 0.30f;
            // IDENTITY
            if ((snap.identityTags & (uint)IdentityTag.IDENTITY_BOSS_ARENA) != 0) tier2Bonus += 0.35f;
            if ((snap.identityTags & (uint)IdentityTag.IDENTITY_NEXUS) != 0) tier2Bonus -= 0.05f;

            // ─── ④ ★ Personality 곱셈 (가상 메서드 — 자식 override) ──
            float personalityMul = GetPersonalityFearMultiplier();
            tier2Bonus *= personalityMul;

            // ─── 디버그 로그 (옵션) ──────────────────────────
            if (tier2FearVerbose && tier2Bonus != 0f)
            {
                Debug.Log($"[Tier2 Fear] {name} @ node={snap.nearestNodeIdx} " +
                          $"I=0x{snap.identityTags:X4} S=0x{snap.stateTags:X4} " +
                          $"E=0x{snap.eventTags:X4} P=0x{snap.potentialTags:X4} " +
                          $"→ raw bonus + personalityMul({personalityMul:F2}) " +
                          $"= tier2Bonus={tier2Bonus:F3}");
            }

            return true;
        }

        /// <summary>
        /// ★ Phase 4 — Personality 영향 가상 메서드.
        /// BaseAIBrain default = 1.0 (Personality 무관 — Mob 등).
        /// HumanoidAIBrain override = stability 기반 Lerp 0.5~1.5.
        /// </summary>
        protected virtual float GetPersonalityFearMultiplier()
        {
            return 1.0f;
        }

        // ==================================================================
        // Awake / OnNetworkSpawn / Blackboard 초기화 (v3 NGO 보존)
        // ==================================================================
        protected virtual void Awake()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                InitializeBlackboard();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            InitializeBlackboard();
        }

        protected virtual void InitializeBlackboard()
        {
            if (blackboard != null)
            {
                if (verboseLogging)
                    Debug.Log($"[{GetType().Name}] {name}: Blackboard 이미 주입됨 ({blackboard.GetType().Name})");
                return;
            }

            var gb = TDA.PB4.Core.GameBlackboard.Instance;
            if (gb != null)
            {
                blackboard = new TDA.PB4.AI.GameBlackboardAdapter(gb);
                if (verboseLogging)
                    Debug.Log($"[{GetType().Name}] {name}: GameBlackboard → Adapter 경로");
                return;
            }

            blackboard = new TDA.PB4.Stubs.StubBlackboard();
            Debug.LogWarning($"[{GetType().Name}] {name}: GameBlackboard 부재 → Stub fallback. " +
                             $"실게임 씬에서 발생 시 GameBlackboard prefab 배치 확인 필요.");
        }

        // ==================================================================
        // DI 주입 API (v3 NGO 보존)
        // ==================================================================
        public void InjectBlackboard(TDA.PB4.Core.GameBlackboard gb)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
            {
                Debug.LogWarning($"[{GetType().Name}] {name}: InjectBlackboard는 서버 전용. 클라이언트 호출 무시.");
                return;
            }

            if (gb == null)
            {
                Debug.LogError($"[{GetType().Name}] {name}: InjectBlackboard에 null 전달");
                return;
            }
            blackboard = new TDA.PB4.AI.GameBlackboardAdapter(gb);
            if (verboseLogging)
                Debug.Log($"[{GetType().Name}] {name}: Blackboard 외부 주입 완료 (덮어쓰기)");
        }

        public bool IsStubFree()
        {
            return blackboard != null && !(blackboard is TDA.PB4.Stubs.StubBlackboard);
        }

        public string GetBlackboardDebugInfo()
        {
            if (blackboard == null) return "null";
            return $"{blackboard.GetType().Name}: {blackboard.ToString()}";
        }

        // ==================================================================
        // 서버 권한 체크
        // ==================================================================
        protected bool HasAuthority()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return true;
            return IsServer;
        }
    }
}