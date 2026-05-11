// =============================================================================
// PresentationDomainInterfaces.cs  |  Presentation 도메인 내부 인터페이스
// Layer  : Presentation Domain Internal (UI / Dialogue)
// Namespace: TDA.PB4.Interfaces.Presentation (Wk0 유지)
//
// 역할:
//   UI / Dialogue 표현 계층 인터페이스.
//
// 수록:
//   ISpeechAssembler, IDialogueRenderer
//
// 이력:
//   2026-04-23 Wk0 — PB4Interfaces.cs Presentation namespace
//   2026-05-11 정리 — PresentationDomainInterfaces.cs 로 분리
//                    파일명에서 PB4 접두사 제거
// =============================================================================

namespace TDA.PB4.Interfaces.Presentation
{
    public interface ISpeechAssembler
    {
        string AssembleSpeech(float[] personalityVector, string contextTag);
    }

    public interface IDialogueRenderer
    {
        void ShowDialogue(string speakerName, string text, float duration);
        void HideDialogue();
    }
}
