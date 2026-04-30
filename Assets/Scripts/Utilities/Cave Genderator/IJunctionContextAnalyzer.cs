// ===============================================================================
// 파일: IJunctionContextAnalyzer.cs
// 역할: [Phase 17] ★ Junction System v4 — 외부 인식용 인터페이스
// 도입: Tier 2 — 지형 정보 시스템
//
// ★ 책임 분리 원칙:
//   - 지형 = 정보 제공 (Role + Tag + history)
//   - 외부 시스템 (ScenarioManager 등) = 시나리오/스폰/이벤트 결정
//   - 외부 시스템은 IJunctionContextAnalyzer 인터페이스에만 의존
//   - 구현체 (TerrainContextAnalyzer)는 향후 교체 가능
//
// ★ Phase 18 후 TagLookupTable + Application Log 활성
// ★ Phase 19 후 4 Axis Role (Passage/Region/Crossover Role) 활성
// ★ Phase 20 후 Tag 4 Category 활성
// ===============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace CaveSystem
{
    /// <summary>
    /// Junction System 안 element 의 종류
    /// </summary>
    public enum ElementKind
    {
        Node,      // 단일 노드
        Passage,   // 단일 edge
        Region,    // 여러 노드+통로 묶은 구역 (★ Phase 26)
        Crossover  // 교차/분기 영역 (★ Phase 25)
    }

    // ═════════════════════════════════════════════════════════════════
    // ★ Phase 19 — 4 Axis Role enum (★ 인프라 정의)
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// A1 Node — 노드 narrative 역할 (★ 기존 RoomType과 호환)
    /// </summary>
    public enum NodeRole
    {
        Candidate = 0,  // 미결정 (★ Phase 19 placeholder)
        Normal    = 0,  // 일반 통과 (alias)
        Spawn     = 1,
        Boss      = 2,
        Treasure  = 3,
        Hub       = 4,
        Nexus     = 5   // ★ Phase 19 신규 — critical path 교차
    }

    /// <summary>
    /// A2 Passage — 통로 narrative 역할 (★ Phase 19 신규)
    /// </summary>
    public enum PassageRole
    {
        ConnectorPath = 0,    // 일반 연결 (placeholder)
        ApproachPath,         // 주요 영역 진입로
        EscapeRoute,          // 비상 역방향 경로
        SacredCorridor,       // ★ 외부 결정 신성 진입
        HazardPath,           // 위험 통로
        TransitionPath,       // biome 전환
        ChallengePath,        // 좁은 길 도전
        VistaPath             // 전망 회랑
    }

    /// <summary>
    /// A3 Region — 지역 narrative 역할 (★ Phase 19 신규)
    /// </summary>
    public enum RegionRole
    {
        Default = 0,          // 일반
        BossArena,            // Boss + 주변
        SacredZone,           // ★ 외부 결정 신성
        HazardZone,           // 위험 cluster
        PuzzleZone,           // 탐색 퍼즐
        TransitZone,          // 이동 중심
        FrontierZone,         // ★ boundary 노드 cluster (F19)
        NomadZone             // ★ multi-biome chain (F15)
    }

    /// <summary>
    /// A4 Crossover — 분기/교차 narrative 역할 (★ Phase 19 신규)
    /// </summary>
    public enum CrossoverRole
    {
        Junction = 0,         // 일반 분기 (placeholder)
        DecisionPoint,        // 경로 선택 (★ Y-Fork F7)
        AmbushPoint,          // 매복 (★ Bottleneck F9+F14)
        Crossroad,            // 중심 교차로
        VerticalCross,        // 3D 교차 (Overpass)
        MajorJunction         // 대형 분기 hub (★ Crossover Hub F14)
    }

    // ═════════════════════════════════════════════════════════════════
    // ★ Phase 20 — Tag 4 Category bitmask (★ 풍부한 외부 context)
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// ① IDENTITY — Role 정보 외부 노출 (Read-only 미러)
    /// </summary>
    [System.Flags]
    public enum IdentityTag : uint
    {
        None              = 0,
        IDENTITY_SPAWN    = 1u << 0,
        IDENTITY_BOSS     = 1u << 1,
        IDENTITY_TREASURE = 1u << 2,
        IDENTITY_NEXUS    = 1u << 3,
        IDENTITY_HUB      = 1u << 4,
        IDENTITY_APPROACH = 1u << 5,
        IDENTITY_ESCAPE   = 1u << 6,
        IDENTITY_HAZARD   = 1u << 7,
        IDENTITY_BOSS_ARENA = 1u << 8,
        IDENTITY_FRONTIER = 1u << 9,    // ★ F19
        IDENTITY_DECISION = 1u << 10,   // ★ F7 Y-Fork
        IDENTITY_AMBUSH   = 1u << 11,   // ★ F9+F14 Bottleneck
        IDENTITY_MAJOR_JUNCTION = 1u << 12, // ★ F14
        IDENTITY_NOMAD    = 1u << 13    // ★ F15 multi-biome
    }

    /// <summary>
    /// ② STATE — 현재 지형 상태
    /// </summary>
    [System.Flags]
    public enum StateTag : uint
    {
        None              = 0,
        // Width 4 단계 (★ Phase 15 — 5m 정책)
        STATE_NARROW      = 1u << 0,    // 5~7m
        STATE_STANDARD    = 1u << 1,    // 7~10m
        STATE_WIDE        = 1u << 2,    // 10~15m
        STATE_GRAND       = 1u << 3,    // >15m
        // Orient
        STATE_VERTICAL    = 1u << 4,
        STATE_HORIZONTAL  = 1u << 5,
        // 위치
        STATE_DEEP        = 1u << 6,    // floorAlt + 5m 이내
        STATE_ELEVATED    = 1u << 7,    // sedimentLayerY 이상
        // boundary
        STATE_BOUNDARY    = 1u << 8,    // biomeA != biomeB
        STATE_MULTIBIOME  = 1u << 9,    // 3+ biome 통과
        // 인접
        STATE_DENSE       = 1u << 10,   // 인접 노드 다수
        STATE_ISOLATED    = 1u << 11,
        // 형태
        STATE_ENCLOSED    = 1u << 12,   // degree ≤ 2 + 작음
        STATE_OPEN        = 1u << 13,   // 큼 + 분기
        STATE_CURVED      = 1u << 14,   // curvAmp > 1.5
        // 위험
        STATE_CRITICAL_RISK = 1u << 15  // RiskLevel.Critical
    }

    /// <summary>
    /// ③ EVENT — ★ 어떤 일이 있었는가 (★ 추적 + 중복 방지)
    /// </summary>
    [System.Flags]
    public enum EventTag : uint
    {
        None              = 0,
        EVENT_REPOSITIONED = 1u << 0,        // ★ R1 (Phase 24) 노드 이동
        EVENT_DESPERATELY_PLACED = 1u << 1,  // ★ 옵션 B desperately quota
        EVENT_CHAMBER_LOCKED = 1u << 2,      // ★ N1 (Phase 14) chamber lock
        EVENT_RADIUS_REDUCED = 1u << 3,      // ★ R2 (Phase 23) 축소
        EVENT_DELTA_SPLIT  = 1u << 4,        // ★ δ Mid-node 분할
        EVENT_GUARD_ACTIVE = 1u << 5,        // ★ G1 보호 발생
        EVENT_G2_EXPANDED  = 1u << 6,        // ★ G2 확장 1m+
        EVENT_M5_OFFSET_LARGE = 1u << 7,     // ★ M5 offset > 1m
        EVENT_CROSSOVER_DETECTED = 1u << 8,  // 2D 교차 감지
        EVENT_ROLE_CHANGED = 1u << 9,        // Role 재할당
        EVENT_BIOME_QUOTA_FAIL = 1u << 10,   // Supervisor 검증 실패
        EVENT_PASS2_RESOLVED = 1u << 11,     // 3-Pass Resolve 발생
        EVENT_CLUSTER_DETECTED = 1u << 12,   // Region cluster 감지
        EVENT_SUB5M_VIOLATION = 1u << 13     // ★ 5m 정책 위반 (Phase 15)
    }

    /// <summary>
    /// ④ POTENTIAL — ★ 어떤 일이 일어나기 쉬운가 (★ 외부 추론용)
    /// </summary>
    [System.Flags]
    public enum PotentialTag : uint
    {
        None                       = 0,
        POTENTIAL_AMBUSH           = 1u << 0,   // Narrow + 분기
        POTENTIAL_HIDDEN_LOOT      = 1u << 1,   // Alcove
        POTENTIAL_FALL_HAZARD      = 1u << 2,   // Pit + Y 깊음
        POTENTIAL_RUSH_DOWN        = 1u << 3,   // Grand + Vertical
        POTENTIAL_BOTTLENECK       = 1u << 4,   // Narrow + 3+ edges
        POTENTIAL_VANTAGE_POINT    = 1u << 5,   // Plateau + Y 높음
        POTENTIAL_ECHO_CHAMBER     = 1u << 6,   // Hall + Wide + 폐쇄
        POTENTIAL_SACRED_VENUE     = 1u << 7,   // Cathedral + Boss 후보
        POTENTIAL_ENVIRONMENTAL_TRAP = 1u << 8, // Rocky + Pit
        POTENTIAL_RITUAL_SITE      = 1u << 9,   // Vault + 깊음
        POTENTIAL_CHASE_SCENE      = 1u << 10,  // Long Wide + Spawn 근처
        POTENTIAL_COVER_DENSE      = 1u << 11,  // Columnar + Wide
        POTENTIAL_SOUNDSCAPE_WIND  = 1u << 12,  // Vertical Zone + Shaft
        POTENTIAL_LOST_PLAYER      = 1u << 13,  // Labyrinth/Frontier
        POTENTIAL_MULTI_PATH_CHOICE = 1u << 14  // Y-Fork
    }

    /// <summary>
    /// ★ Tag 4 Category 통합 — Phase 20에서 활성
    /// </summary>
    public struct ContextTagSet
    {
        public uint identityTags;
        public uint stateTags;
        public uint eventTags;
        public uint potentialTags;

        public bool HasIdentity(IdentityTag tag) => (identityTags & (uint)tag) != 0;
        public bool HasState(StateTag tag) => (stateTags & (uint)tag) != 0;
        public bool HasEvent(EventTag tag) => (eventTags & (uint)tag) != 0;
        public bool HasPotential(PotentialTag tag) => (potentialTags & (uint)tag) != 0;
    }

    // ═════════════════════════════════════════════════════════════════
    // ★ Phase 18 — Tag Application Log 시스템
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ Tag 적용 추적 entry — "왜 이 tag가 부여됐는가"
    /// </summary>
    public struct TagApplication
    {
        public string ruleId;       // ★ "narrow_at_branch_ambush"
        public string category;     // "Identity" / "State" / "Event" / "Potential"
        public string reasoning;    // ★ 사람이 읽는 설명
        public string phase;        // ★ "Phase14_N1" / "Phase24_R1" 등
        public uint tagsApplied;    // 부여된 tag bits
    }

    /// <summary>
    /// ★ 한 element의 Tag 적용 history
    /// </summary>
    public class TagApplicationLog
    {
        public List<TagApplication> entries = new List<TagApplication>();

        public bool HasRule(string ruleId)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].ruleId == ruleId) return true;
            return false;
        }

        public List<TagApplication> GetByPhase(string phase)
        {
            var result = new List<TagApplication>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].phase == phase) result.Add(entries[i]);
            return result;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // ★ ScenarioContext — 외부 시스템에 제공할 시나리오 후보
    // ═════════════════════════════════════════════════════════════════

    public struct ScenarioContext
    {
        public string kind;         // ★ "BossBattle" / "SacredRitual" / "AmbushSpawn" 등
        public int priority;        // 우선순위 (높을수록 먼저)
        public string source;       // ★ "IDENTITY_BOSS + POTENTIAL_SACRED_VENUE" 같은 reasoning
        public List<string> adjustments; // 외부에서 추가 조정 가능
    }

    // ═════════════════════════════════════════════════════════════════
    // ★ IJunctionContextAnalyzer — 외부 의존 인터페이스
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★ Junction System v4 외부 인식 인터페이스
    ///
    ///   외부 시스템 (ScenarioManager / SpawnSystem / LightingManager 등)이
    ///   이 인터페이스에만 의존 → 구현체 (TerrainContextAnalyzer) 교체 가능.
    ///
    ///   Phase 17에서 인터페이스 정의 + 기본 구현 (Role + 기존 6 tag).
    ///   Phase 18~21에서 단계적으로 4 Category Tag + Application Log 활성.
    /// </summary>
    public interface IJunctionContextAnalyzer
    {
        // ─── ★ Tag 4 Category 직접 읽기 ───
        bool HasIdentity(int elementIdx, ElementKind kind, IdentityTag tag);
        bool HasState(int elementIdx, ElementKind kind, StateTag tag);
        bool HasEvent(int elementIdx, ElementKind kind, EventTag tag);
        bool HasPotential(int elementIdx, ElementKind kind, PotentialTag tag);

        ContextTagSet GetAllTags(int elementIdx, ElementKind kind);

        // ─── ★ Role 정보 직접 노출 ───
        NodeRole GetNodeRole(int nodeIdx);
        PassageRole GetPassageRole(int edgeIdx);
        RegionRole GetRegionRole(int regionIdx);
        CrossoverRole GetCrossoverRole(int crossoverIdx);

        // ★ Possible Role (외부 시스템이 후보 중 선택)
        NodeRole[] GetPossibleNodeRoles(int nodeIdx);
        PassageRole[] GetPossiblePassageRoles(int edgeIdx);
        RegionRole[] GetPossibleRegionRoles(int regionIdx);
        CrossoverRole[] GetPossibleCrossoverRoles(int crossoverIdx);

        // ─── ★ Tag 추적 (Phase 18) ───
        TagApplicationLog GetTagHistory(int elementIdx, ElementKind kind);

        // ─── ★ 시나리오 후보 enumerate ───
        List<ScenarioContext> EnumerateContexts(int elementIdx, ElementKind kind);

        // ─── ★ Spatial query ───
        int[] FindNodesByIdentity(IdentityTag tag);
        int[] FindEdgesByPotential(PotentialTag tag);
        int[] FindElementsByEvent(EventTag tag, ElementKind kind);

        // ─── ★ 정량 metric (Stage 3-E/F 노출) ───
        PassageMetric.RiskScore GetRiskScore(int edgeIdx);
        PassageMetric.PassageProfile GetPassageProfile(int edgeIdx);

        // ─── ★ 관리 ───
        bool IsReady { get; }
        void Rebuild();
    }
}
