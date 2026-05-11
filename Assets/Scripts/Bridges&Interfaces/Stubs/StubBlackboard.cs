using System.Collections.Generic;
using TDA.PB4.Interfaces.Core;

namespace TDA.PB4.Stubs
{
    // ════════════════════════════════════════════════════════════════════
    // StubBlackboard — IBlackboard 의 Null Object Pattern fallback
    // ════════════════════════════════════════════════════════════════════
    //
    // 【용도】
    //   GameBlackboard.Instance 가 null 일 때 NullReferenceException 회피.
    //   호출자 (BaseAIBrain 등) 가 null 검사 없이 안전하게 호출 가능.
    //
    // 【본 클래스는 폐기 stub 이 아님】
    //   ITerrainSampler / IContextAnalyzer 같은 폐기 인터페이스의 임시 stub 과
    //   본질 다름 — Null Object Pattern 의 정상 구현체.
    //
    // 【사용 패턴】
    //   BaseAIBrain.cs:
    //     var gb = GameBlackboard.Instance;
    //     blackboard = (gb != null) ? new GameBlackboardAdapter(gb)
    //                                : new StubBlackboard();  // ★ fallback
    //
    //   IsStubFree() 메서드로 실제 GameBlackboard 연결 여부 검사 가능.
    //
    // 【반환값 — 안전한 default】
    //   GetActiveTerrainTags()  → 빈 List (null X)
    //   GetGlobalIntensity()    → 0f
    //   GetCharacterStat(key)   → 0f
    // ════════════════════════════════════════════════════════════════════
    
    public class StubBlackboard : IBlackboard
    {
        private static readonly IReadOnlyList<string> EmptyTagList = new List<string>();
        
        public IReadOnlyList<string> GetActiveTerrainTags() => EmptyTagList;
        
        public float GetGlobalIntensity() => 0f;
        
        public float GetCharacterStat(string key) => 0f;
        
        public override string ToString() => "StubBlackboard (Null Object — GameBlackboard 미존재)";
    }
}
