// =============================================================================
// CoreInterfaces.cs  |  Core 공유 서비스 인터페이스
// Layer  : Core (공유 계층)
// Namespace: TDA.PB4.Interfaces.Core
//
// 역할:
//   3개 파트 (AI / Terrain / Scenario) 가 공유하는 핵심 인터페이스.
//   Bridge 와 무관 — 직접 의존성 주입으로 사용.
//
// 이력:
//   2026-04-23 Wk0 — PB4Interfaces.cs 의 Core namespace 부분으로 정의
//   2026-05-11 정리 — CoreInterfaces.cs 로 분리 (Interfaces/Core/)
//                    파일명에서 PB4 접두사 제거
// =============================================================================
using System.Collections.Generic;

namespace TDA.PB4.Interfaces.Core
{
    /// <summary>중앙 블랙보드 접근 인터페이스.</summary>
    public interface IBlackboard
    {
        IReadOnlyList<string> GetActiveTerrainTags();
        float GetGlobalIntensity();
        float GetCharacterStat(string key);
    }

    /// <summary>사전 베이킹된 의미 원소 벡터 라이브러리 접근.</summary>
    public interface INarrativeVectorLib
    {
        float[] GetVector(string elementName);
        float CosineSimilarity(float[] a, float[] b);
    }

    /// <summary>Week 2 진척률 추적·보고 계약.</summary>
    public interface IProgressTracker
    {
        void ReportEvent(string checkId, bool passed, string detail);
        void ReportProgress(string milestoneId, float ratio);
        void EmitFinalReport();
    }
}
