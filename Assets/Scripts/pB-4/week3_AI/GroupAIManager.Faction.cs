// =============================================================================
// GroupAIManager.Faction.cs (★ E-07 — S2 신규)
// Partial — Faction State 변화 구독 + Group 측 반응
// Layer  : L2 Router (AI) — Faction 도메인 연계
// Namespace: TDA.PB4.AI
// =============================================================================
//
// 의의:
//   GroupAIManager 측 본 partial 측 본격 추가 — Faction 상태 변화 시 그룹 측 반응 진입점.
//   기존 main 파일 (2566 줄) + Debug.cs sibling 측 본격 0 줄 변경.
//
// 진입 흐름:
//   Start()    → _factionIdForSubscription 캐시 + Bridge.OnFactionStateChanged 구독
//   OnDestroy()→ 구독 해제 (이벤트 누수 방지)
//   HandleFactionStateChanged → 본 그룹 Faction 만 필터링 + 4 카테고리 분기
//
// 4 카테고리 핸들러 (현 시점 = 로그 출력 + 진입 자료 박제):
//   HandleMoodChanged      — 예: MOOD_PANIC 진입 시 도주 / MOOD_AGGRESSIVE 시 공세
//   HandleTacticalChanged  — 예: TACTICAL_DEFENSIVE 진입 시 진지 사수
//   HandleLifecycleChanged — 예: LIFECYCLE_DEFEATED 진입 시 그룹 해체 분기
//   HandleRelationChanged  — 예: RELATION_PLAYER_HOSTILE 진입 시 플레이어 대상 분기
//
// 본 파일 측 진입 자료 (TODO):
//   BT 신호 연계 / 도주 / 공세 / 해체 등 — 사용자 측 BT 구조에 따라 추가.
//   현 시점 = 로그 출력 + 변화 추적 (Tier 3 검수 자료).
//
// 의존:
//   - WorldFactionBridgeManager (E-04) — event Action<FactionStateChangeEventArgs>
//   - FactionStateChangeEventArgs (P-02 부속) — factionId + bitsBefore/bitsAfter + 편의 helper
//   - main 파일 GetFactionId() — 본 그룹 Faction ID 조회
//   - main 파일 Log(string colorHex, string category, string msg) — 로그 헬퍼 (★ 첫 인자 = hex string)
//   - main 파일 LogW(string) — warning 로그
// =============================================================================

using UnityEngine;
using TDA.PB4.Faction;
using TDA.PB4.Faction.Tags;

namespace TDA.PB4.AI
{
    public partial class GroupAIManager
    {
        // ──────────────────────────────────────────────────────────────────
        // ★ E-07 — Faction 색상 상수 (Log 측 hex string 정합)
        // ──────────────────────────────────────────────────────────────────
        // 본 상수 측 본격 GroupAIManager.Log(string colorHex, ...) 측 정합.
        // 기존 COL_TOKEN / COL_ROLE / COL_MORALE 등 정합 패턴.

        private const string COL_FACTION_SUB = "#8CD9D9";  // 시안 — 구독 시작
        private const string COL_FACTION_MOOD = "#D9A6A6";  // 소프트 레드 — Mood
        private const string COL_FACTION_TACTICAL = "#A6D9A6";  // 소프트 그린 — Tactical
        private const string COL_FACTION_LIFECYCLE = "#A6A6D9";  // 소프트 블루 — Lifecycle
        private const string COL_FACTION_RELATION = "#D9A6D9";  // 소프트 마젠타 — Relation

        // ──────────────────────────────────────────────────────────────────
        // ★ E-07 — Faction State 변화 구독 (S2 신규)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// 본 그룹이 속한 Faction ID 캐시 (구독 필터링용).
        /// Start() 시점에 GetFactionId() 결과를 캐시 — 매 이벤트 호출 시 재조회 회피.
        /// </summary>
        private string _factionIdForSubscription;

        /// <summary>
        /// E-07 — Start: Faction 상태 변화 구독.
        /// Awake/OnEnable 는 main 파일에서 정의됨 → 본 파일은 Start/OnDestroy 사용 (충돌 회피).
        /// 
        /// 본 시점 측 GetFactionId() 측 빈 값 측 가능성 (정적 GroupAIManager 또는 SetFactionDefinition 측 Start 이후 호출).
        /// → 본 구독 측 항상 수행 / factionId 측 첫 이벤트 시점 측 lazy 재해결.
        /// </summary>
        private void Start()
        {
            _factionIdForSubscription = ResolveFactionId();  // 빈 값 가능 — lazy resolve 측 정합

            // ★ HOTFIX6 — Debug.Log 직접 호출 (GroupAIManager.Log 측 debugLog 토글 측 silent 회피)
            Debug.Log(
                $"<color={COL_FACTION_SUB}>[GroupAI:E-07/Faction]</color> " +
                $"Start 진입 — gameObject='{gameObject.name}' / " +
                $"resolved='{_factionIdForSubscription}' / " +
                $"GetFactionId()='{GetFactionId()}' / " +
                $"policySO={(policySO != null ? policySO.name : "null")}",
                this);

            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null)
            {
                LogW("E-07/Faction: WorldFactionBridgeManager.Instance == null — Faction 이벤트 구독 실패.");
                return;
            }

            bridge.OnFactionStateChanged += HandleFactionStateChanged;

            if (!string.IsNullOrEmpty(_factionIdForSubscription))
            {
                Debug.Log(
                    $"<color={COL_FACTION_SUB}>[GroupAI:E-07/Faction]</color> " +
                    $"Faction 구독 시작 — factionId={_factionIdForSubscription}",
                    this);
            }
            // else: factionId 측 미설정 — HandleFactionStateChanged 측 lazy resolve 측 정합.
        }

        /// <summary>
        /// ★ HOTFIX7 — factionId 측 해결 (3 단계 우선순위) :
        ///   1. GetFactionId() = policySO.factionId — 정합 자료
        ///   2. GameObject 명 측 파싱 (Group_&lt;factionId&gt;_&lt;sequence&gt; 정합 패턴)
        ///   3. 빈 값 반환 (이벤트 측 무시)
        /// </summary>
        private string ResolveFactionId()
        {
            // 1. policySO.factionId 측 우선
            var fromPolicy = GetFactionId();
            if (!string.IsNullOrEmpty(fromPolicy)) return fromPolicy;

            // 2. GameObject 명 측 파싱 — "Group_<factionId>_<sequence>" 패턴
            var name = gameObject.name;
            const string prefix = "Group_";
            if (name.StartsWith(prefix))
            {
                var rest = name.Substring(prefix.Length);
                var underscoreIdx = rest.IndexOf('_');
                if (underscoreIdx > 0)
                    return rest.Substring(0, underscoreIdx);
            }

            return "";
        }

        /// <summary>
        /// E-07 — OnDestroy: 구독 해제 (이벤트 누수 방지).
        /// </summary>
        private void OnDestroy()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge != null)
                bridge.OnFactionStateChanged -= HandleFactionStateChanged;
        }

        /// <summary>
        /// Faction 상태 변화 핸들러 — 본 그룹 Faction 만 필터링하여 4 카테고리 분기.
        /// 이벤트 시그니처 = Action&lt;FactionStateChangeEventArgs&gt; — 단일 인자 (args.factionId 포함).
        /// 
        /// ★ Lazy resolve — _factionIdForSubscription 측 빈 값 측 첫 이벤트 시점 측 GetFactionId() 재호출.
        /// 본 패턴 측 Start 측 SetFactionDefinition 측 시점 정합 의존 측 회피.
        /// </summary>
        private void HandleFactionStateChanged(FactionStateChangeEventArgs args)
        {
            // ── Lazy resolve — factionId 측 미해결 시 본 시점 측 재시도 (HOTFIX7: 3 단계 fallback)
            if (string.IsNullOrEmpty(_factionIdForSubscription))
            {
                _factionIdForSubscription = ResolveFactionId();
                if (string.IsNullOrEmpty(_factionIdForSubscription))
                    return;  // 여전히 빈 값 — 본 GroupAIManager 측 정적 / 미할당 측 → 이벤트 무시

                // ★ HOTFIX6 — Debug.Log 직접 (debugLog 토글 측 무관)
                Debug.Log(
                    $"<color={COL_FACTION_SUB}>[GroupAI:E-07/Faction]</color> " +
                    $"Faction 구독 lazy resolve — factionId={_factionIdForSubscription} (gameObject='{gameObject.name}')",
                    this);
            }

            // 본 그룹의 Faction이 아니면 무시
            if (args.factionId != _factionIdForSubscription)
                return;

            // 4 카테고리 분기 (Helper 사용)
            if (args.MoodChanged) HandleMoodChanged(args);
            if (args.TacticalChanged) HandleTacticalChanged(args);
            if (args.LifecycleChanged) HandleLifecycleChanged(args);
            if (args.RelationChanged) HandleRelationChanged(args);
        }

        // ──────────────────────────────────────────────────────────────────
        // 4 카테고리 핸들러 (현 시점 = 로그 + 진입 자료 박제)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Mood 변화 핸들러 — 예: MOOD_PANIC → 도주 / MOOD_AGGRESSIVE → 공세.
        /// 본 시점 — 로그 출력 + BT 신호 연계 진입 자료 박제 (TODO).
        /// </summary>
        private void HandleMoodChanged(FactionStateChangeEventArgs args)
        {
            uint added = args.MoodBitsTurnedOn;
            uint removed = args.MoodBitsTurnedOff;

            Log(COL_FACTION_MOOD, "E-07/Mood",
                $"factionId={_factionIdForSubscription} " +
                $"added=0x{added:X8} removed=0x{removed:X8}");

            // TODO (E-07 진입 자료) — BT 신호 연계
            // if ((added & (uint)FactionMoodTag.MOOD_PANIC) != 0u)
            //     TriggerFleeBehavior();
            // if ((added & (uint)FactionMoodTag.MOOD_AGGRESSIVE) != 0u)
            //     RaiseAggression();
            // if ((removed & (uint)FactionMoodTag.MOOD_AGGRESSIVE) != 0u)
            //     RestoreNormalAggression();
        }

        /// <summary>
        /// Tactical 변화 핸들러 — 예: TACTICAL_DEFENSIVE → 진지 사수 / TACTICAL_RETREATING → 후퇴 신호.
        /// </summary>
        private void HandleTacticalChanged(FactionStateChangeEventArgs args)
        {
            uint added = args.TacticalBitsTurnedOn;
            uint removed = args.TacticalBitsTurnedOff;

            Log(COL_FACTION_TACTICAL, "E-07/Tactical",
                $"factionId={_factionIdForSubscription} " +
                $"added=0x{added:X8} removed=0x{removed:X8}");

            // TODO (E-07 진입 자료) — BT 신호 연계
        }

        /// <summary>
        /// Lifecycle 변화 핸들러 — 예: LIFECYCLE_DEFEATED → 그룹 해체 / LIFECYCLE_EMERGING → 신생 분기.
        /// </summary>
        private void HandleLifecycleChanged(FactionStateChangeEventArgs args)
        {
            uint added = args.LifecycleBitsTurnedOn;
            uint removed = args.LifecycleBitsTurnedOff;

            Log(COL_FACTION_LIFECYCLE, "E-07/Lifecycle",
                $"factionId={_factionIdForSubscription} " +
                $"added=0x{added:X8} removed=0x{removed:X8}");

            // TODO (E-07 진입 자료) — 그룹 해체 / 신생 분기
            // if ((added & (uint)FactionLifecycleTag.LIFECYCLE_DEFEATED) != 0u)
            //     BeginGroupDissolution();
            // if ((added & (uint)FactionLifecycleTag.LIFECYCLE_EXTINCT) != 0u)
            //     DespawnAllMembers();
        }

        /// <summary>
        /// Relation 변화 핸들러 — 예: RELATION_PLAYER_HOSTILE → 플레이어 대상 / RELATION_ALLIED → 우호 그룹 인식.
        /// </summary>
        private void HandleRelationChanged(FactionStateChangeEventArgs args)
        {
            uint added = args.RelationBitsTurnedOn;
            uint removed = args.RelationBitsTurnedOff;

            Log(COL_FACTION_RELATION, "E-07/Relation",
                $"factionId={_factionIdForSubscription} " +
                $"added=0x{added:X8} removed=0x{removed:X8}");

            // TODO (E-07 진입 자료) — 외부 관계 변화 신호
        }
    }
}