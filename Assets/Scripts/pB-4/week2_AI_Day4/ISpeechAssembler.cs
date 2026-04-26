// =============================================================================
// ISpeechAssembler.cs  |  pB-4 Week 2 Day 4
// Layer  : Interface (계층 경계)
// Owner  : Person A
//
// 역할:
//   발화 조립 계층의 공개 API.
//   Week 6 (LLM 통합) / Week 7 (3레이어 본작업)에서
//   구현체를 교체할 때 이 인터페이스는 불변 유지.
//
// 개론서 §100, §1539에서 ISpeechAssembler 인터페이스 명시됨.
// 4계층 원칙:
//   - Dispatcher(L2)는 이 인터페이스만 참조 → 구현체 교체 시 Dispatcher 수정 없음.
// =============================================================================
using TDA.PB4.Core.Events;  // [Day 4 수정] SpeechTriggerContext

namespace TDA.PB4.AI.Speech
{
    /// <summary>발화 조립 공개 API. Week 6/7 확장 경계.</summary>
    public interface ISpeechAssembler
    {
        /// <summary>triggerId + Context로 대사 한 줄을 조립.</summary>
        /// <returns>완성된 텍스트. 빈 문자열 = 발화 생략.</returns>
        string Assemble(string triggerId, SpeechTriggerContext ctx);

        /// <summary>직전 Assemble()이 결정한 Tone. Renderer가 참조.</summary>
        SpeechTone LastTone { get; }
    }
}
