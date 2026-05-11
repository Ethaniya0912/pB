// =============================================================================
// TerrainDomainInterfaces.cs  |  Terrain 도메인 내부 인터페이스
// Layer  : Terrain Domain Internal
// Namespace: TDA.PB4.Interfaces.Environment (Wk0 유지)
//
// 역할:
//   Terrain 도메인 내부 인터페이스. HNGS 시스템.
//
// 수록:
//   IHNGSController (사용 여부 확인 필요 — 일단 유지)
//
// ★ 폐기 예정 인터페이스 (ITerrainSampler / IContextAnalyzer / IBiomeSpawnResolver)
//   는 Deprecated/DeprecatedInterfaces.cs 참조.
//
// ★ 외부 도메인 (AI / Scenario) 이 Terrain 호출 시:
//   - Bridges/WorldTerrainBridgeManager.cs 의 메서드 사용 (Bridge 경유)
//   - 내부 통신: CaveSystem.IJunctionContextAnalyzer (TerrainContextAnalyzer 구현)
//
// 이력:
//   2026-04-23 Wk0 — PB4Interfaces.cs Environment namespace
//   2026-05-11 정리 — TerrainDomainInterfaces.cs 로 분리
//                    ITerrainSampler / IContextAnalyzer / IBiomeSpawnResolver
//                    → Deprecated 폴더로 이동
//                    파일명에서 PB4 접두사 제거
// =============================================================================

namespace TDA.PB4.Interfaces.Environment
{
    /// <summary>
    /// HNGS (Hierarchical Narrative Generation System) 컨트롤러 접근.
    /// 노드별 긴장도 조회 + 통로 너비 변조 요청.
    ///
    /// ★ 사용 여부 미확인 — 사용처 확인 후 (1) 유지 또는 (2) Deprecated 이동 결정.
    /// </summary>
    public interface IHNGSController
    {
        float GetIntensityAtNode(int nodeIndex);
        void RequestWidthModulation(int edgeIndex, float widthFactor);
    }
}
