// ═══════════════════════════════════════════════════════════════════════════════
// Wk4-5 — TerrainSemanticMapper.cs (본격 구현 v1.1)
// ═══════════════════════════════════════════════════════════════════════════════
// Tier 2 ContextTagSet 60 bit → 5 인간 의미 boolean 매핑 헬퍼.
// 60 bit (Identity 15 + State 16 + Event 14 + Potential 15) 추상 정보 → 인간적 의미.
//
// ─── v1.0 → v1.1 변경 사항 (2026-05-12) ────────────────────────────────────────
// 
// [리네임]
//   - 파일명     : FuzzyTagMapper.cs   → TerrainSemanticMapper.cs
//   - 클래스명   : FuzzyTagMapper      → TerrainSemanticMapper
//   - 구조체명   : FuzzyTagFlags       → SemanticTagFlags
//   - #if 매크로 : (변경 X) PB4_TIER2_INTEGRATED
//
// [변경 이유]
//   v10 (이전 가설) — "퍼지 태그 5종" (NarrowPath / SpookyCave 등) 별도 정의 가설.
//                     "Fuzzy" = 흐릿한 / 정도(degree) 로 표현 — fuzzy logic 의 정신.
//
//   v11 (Tier 2 도착) — 지형팀이 4 카테고리 60 bit ContextTagSet 인도.
//                       v10 의 5 퍼지 태그 가설 무효화 → 60 bit 정확한 비트 매핑으로 대체.
//                       즉 실제 구현 = "Fuzzy" 가 아닌 "정확한 bit 검사".
//
//   문제 — "Fuzzy" 명칭이 v11 실제 동작과 불일치.
//          NPC 가 환경을 "인간적 의미" 로 인지한다는 의도는 유지되지만,
//          내부 동작 = fuzzy logic 의 정도 표현 X / 정확한 bit AND 검사 O.
//
//   해결 — "Semantic" (의미론적) 으로 리네임. 의도는 동일 유지:
//          "Tier 2 추상 비트마스크 → NPC 가 이해하는 인간적 의미 (좁은 통로 / 절벽길 등)".
//
// [영향 범위]
//   - 외부 호출자 = X (본 파일은 static 헬퍼 / 다른 Wk4 파일은 호출 X)
//   - 호출 예정자 = TraumaSystem.RegisterExploration(wasTraumatic) 의 wasTraumatic 입력
//                  → 외부에서 IsTraumaticTerrain() 결과 전달하는 형태 (느슨 결합)
//   - Wk4 의제 종합 v2 §2.5 의 명세 = 그대로 (이름만 정정)
//
// ─── 기존 정보 ────────────────────────────────────────────────────────────────
//
// 의존: ★ Tier 2 IJunctionContextAnalyzer (CaveSystem — 외부)
//        ★ ContextTagSet (Tier 2 자료 — 외부)
// 호출자: Wk4-3 TraumaSystem (IsTraumaticTerrain 진입 조건)
//          BlackboardUpdater 10Hz (5 의미 boolean BB 게시)
//          HumanoidAIBrain BT 노드 (Wk5+ 본격)
//
// 위치: Assets/Scripts/pB-4/week4_AI_Social/TerrainSemanticMapper.cs
// 버전: v1.1 (2026-05-12) — 리네임
// 기반: 기술개발 개론서 v11 §5.4.3 E / Wk4 의제 종합 v2 §2.5
// ═══════════════════════════════════════════════════════════════════════════════

using UnityEngine;

// ★ Tier 2 namespace 참조 (CaveSystem — 외부 인도분)
// 실제 사용 시: using TDA.CaveSystem; 또는 해당 namespace
// 본 파일은 ContextTagSet / StateTag / PotentialTag / IdentityTag enum 가정.
// using TDA.CaveSystem;

namespace TDA.PB4.AI.Social
{
    /// <summary>
    /// Tier 2 ContextTagSet 60 bit → 5 인간 의미 boolean 매핑 헬퍼.
    /// static 클래스 — 상태 보유 X / 순수 함수.
    /// </summary>
    /// <remarks>
    /// ★ 본 파일 = Tier 2 인도분 (IJunctionContextAnalyzer / ContextTagSet) 위에서 작동.
    /// Tier 2 미도착 시 컴파일 에러 — 본 파일 #if 가드로 fallback 활성.
    /// 
    /// ★ v1.1 (2026-05-12) — FuzzyTagMapper 에서 리네임.
    /// v10 "퍼지 태그" 가설 무효화 (Tier 2 도착) → "Semantic" 으로 명칭 정정.
    /// </remarks>
    public static class TerrainSemanticMapper
    {
        // ═══════════════════════════════════════════════════════════════════
        // 5 의미 매핑 (Tier 2 60 bit → 인간 의미)
        // ═══════════════════════════════════════════════════════════════════
        
        // ★ 본 메서드들은 Tier 2 ContextTagSet 의 실제 시그니처 도착 후 활성화.
        // 임시 자리 표시자 시그니처 (ContextTagSet 의 Has* 메서드 가정):
        //   - tags.HasState(StateTag.NARROW)
        //   - tags.HasPotential(PotentialTag.BOTTLENECK)
        //   - tags.HasIdentity(IdentityTag.HAZARD)
        //   - tags.HasEvent(EventTag.X)  (사용 X)
        
#if PB4_TIER2_INTEGRATED
        // Tier 2 통합 완료 시점 #define 활성화 — 본격 메서드
        
        /// <summary>좁은 통로 — STATE_NARROW + POTENTIAL_BOTTLENECK 매복 위험 인식.</summary>
        public static bool IsNarrowPath(ContextTagSet tags)
        {
            return tags.HasState(StateTag.NARROW)
                && tags.HasPotential(PotentialTag.BOTTLENECK);
        }
        
        /// <summary>절벽길 — STATE_NARROW + POTENTIAL_FALL_HAZARD.</summary>
        public static bool IsDifficultEscape(ContextTagSet tags)
        {
            return tags.HasState(StateTag.NARROW)
                && tags.HasPotential(PotentialTag.FALL_HAZARD);
        }
        
        /// <summary>고지대 — STATE_ELEVATED + POTENTIAL_VANTAGE_POINT 전술 우위.</summary>
        public static bool IsHighGround(ContextTagSet tags)
        {
            return tags.HasState(StateTag.ELEVATED)
                && tags.HasPotential(PotentialTag.VANTAGE_POINT);
        }
        
        /// <summary>폐쇄 동굴 — STATE_ENCLOSED + POTENTIAL_ECHO_CHAMBER.</summary>
        public static bool IsSpookyCave(ContextTagSet tags)
        {
            return tags.HasState(StateTag.ENCLOSED)
                && tags.HasPotential(PotentialTag.ECHO_CHAMBER);
        }
        
        /// <summary>위험 함정 — IDENTITY_HAZARD + POTENTIAL_ENVIRONMENTAL_TRAP.</summary>
        public static bool IsDeathTrap(ContextTagSet tags)
        {
            return tags.HasIdentity(IdentityTag.HAZARD)
                && tags.HasPotential(PotentialTag.ENVIRONMENTAL_TRAP);
        }
        
        /// <summary>
        /// 트라우마 진입 조건 — 5 의미 중 하나 true 시 트라우마 가능 지형.
        /// Wk4-3 TraumaSystem.RegisterExploration() 의 wasTraumatic 입력.
        /// </summary>
        public static bool IsTraumaticTerrain(ContextTagSet tags)
        {
            return IsNarrowPath(tags)
                || IsDifficultEscape(tags)
                || IsSpookyCave(tags)
                || IsDeathTrap(tags);
            // ★ IsHighGround 는 트라우마 진입 X (긍정적 지형)
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // 5 의미 종합 — BlackboardUpdater 10Hz 게시 용
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>5 의미 boolean 종합 — BB 게시 또는 디버그.</summary>
        public static SemanticTagFlags GetAllFlags(ContextTagSet tags)
        {
            return new SemanticTagFlags
            {
                isNarrowPath      = IsNarrowPath(tags),
                isDifficultEscape = IsDifficultEscape(tags),
                isHighGround      = IsHighGround(tags),
                isSpookyCave      = IsSpookyCave(tags),
                isDeathTrap       = IsDeathTrap(tags),
            };
        }
        
#else
        // Tier 2 미도착 시점 — 기본값 fallback (모두 false / 트라우마 진입 X)
        // 단, 컴파일은 통과 — 인터페이스만 노출
        
        /// <summary>좁은 통로 — Tier 2 미도착 시 false fallback.</summary>
        public static bool IsNarrowPath(object tags) => false;
        
        /// <summary>절벽길 — Tier 2 미도착 시 false fallback.</summary>
        public static bool IsDifficultEscape(object tags) => false;
        
        /// <summary>고지대 — Tier 2 미도착 시 false fallback.</summary>
        public static bool IsHighGround(object tags) => false;
        
        /// <summary>폐쇄 동굴 — Tier 2 미도착 시 false fallback.</summary>
        public static bool IsSpookyCave(object tags) => false;
        
        /// <summary>위험 함정 — Tier 2 미도착 시 false fallback.</summary>
        public static bool IsDeathTrap(object tags) => false;
        
        /// <summary>트라우마 진입 조건 — Tier 2 미도착 시 false (트라우마 진입 X).</summary>
        public static bool IsTraumaticTerrain(object tags) => false;
        
        /// <summary>5 의미 종합 — Tier 2 미도착 시 모두 false.</summary>
        public static SemanticTagFlags GetAllFlags(object tags) => default;
#endif
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // 5 의미 종합 구조체 — BlackboardUpdater 10Hz BB 게시
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// 5 의미 boolean 종합. BB 게시 / 디버그 / 통합 BT 입력.
    /// ★ v1.1 — FuzzyTagFlags 에서 리네임.
    /// </summary>
    [System.Serializable]
    public struct SemanticTagFlags
    {
        public bool isNarrowPath;
        public bool isDifficultEscape;
        public bool isHighGround;
        public bool isSpookyCave;
        public bool isDeathTrap;
        
        public bool IsTraumatic => isNarrowPath || isDifficultEscape || isSpookyCave || isDeathTrap;
        public bool IsPositive => isHighGround;
        
        public override string ToString()
        {
            return $"NP={isNarrowPath} DE={isDifficultEscape} HG={isHighGround} SC={isSpookyCave} DT={isDeathTrap}";
        }
    }
}
