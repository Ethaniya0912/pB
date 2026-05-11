// =============================================================================
// DeprecatedInterfaces.cs  |  폐기 예정 인터페이스 모음
// Layer  : Deprecated
// Namespace: TDA.PB4.Interfaces.Environment (Wk0 유지)
//
// 역할:
//   Wk3 v4 / 지형 시스템 7항목 가이드 v1 에서 폐기 결정된 인터페이스 보존.
//   ★ 삭제 X — 점진 마이그레이션 단계 / 히스토리 보존.
//
// 수록 (모두 폐기 예정):
//   I
//   : Wk3 v4 폐기 결정 — TerrainContextAnalyzer 로 대체
//   IContextAnalyzer       : Wk3 v4 폐기 결정 — TerrainContextAnalyzer 로 대체
//   IBiomeSpawnResolver    : 데드코드 의심 (지형 가이드 7 항목 #2 검증 안건)
//
// 폐기 사유:
//   Wk3 v4 (2026-05-03) — IJunctionContextAnalyzer (CaveSystem, Tier 2) 우선 디렉션.
//   기존 ITerrainSampler / IContextAnalyzer 가 다루던 책임은:
//   - 좌표 / 형상 샘플링 → TerrainContextAnalyzer (CaveSystem)
//   - 태그 읽기 → IJunctionContextAnalyzer 인터페이스 (TerrainContextAnalyzer 구현)
//   - 통합 진입점 → WorldTerrainBridgeManager (Bridge)
//
// 마이그레이션 (점진):
//   P0: 신규 코드에서 사용 X (현재 ↘)
//   P1: 기존 사용처 파악 + 대체 (Wk5+)
//   P2: 마이그레이션 완료 후 본 파일 자체 제거 (Wk6+ 또는 그 이후)
//
// 이력:
//   2026-04-23 Wk0 — PB4Interfaces.cs Environment namespace 에서 정의
//   2026-05-03 Wk3 v4 — ITerrainSampler / IContextAnalyzer 폐기 결정
//   2026-05-11 정리 — DeprecatedInterfaces.cs 로 분리 (Deprecated/)
//                    파일명에서 PB4 접두사 제거
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Interfaces.Environment
{
    /// <summary>
    /// ★ 폐기 예정 — Wk3 v4 결정.
    /// 좌표 / 형상 샘플링은 CaveSystem.TerrainContextAnalyzer 로 대체.
    /// </summary>
    [System.Obsolete("Wk3 v4 폐기 — CaveSystem.TerrainContextAnalyzer 사용. WorldTerrainBridgeManager 경유.", false)]
    public interface ITerrainSampler
    {
        float SampleSlope(Vector3 worldPos);
        float SampleDensity(Vector3 worldPos);
        float SampleOcclusion(Vector3 worldPos);
        float SamplePathWidth(Vector3 worldPos);
    }

    /// <summary>
    /// ★ 폐기 예정 — Wk3 v4 결정.
    /// 태그 읽기는 IJunctionContextAnalyzer (CaveSystem.TerrainContextAnalyzer 구현) 로 대체.
    /// </summary>
    [System.Obsolete("Wk3 v4 폐기 — CaveSystem.IJunctionContextAnalyzer 사용. WorldTerrainBridgeManager 경유.", false)]
    public interface IContextAnalyzer
    {
        List<string> AnalyzeTerrain(Vector3 worldPos);
    }

    /// <summary>
    /// ★ 데드코드 의심 — 지형 시스템 7 항목 가이드 v1 #2 검증 안건.
    /// 사용처 확인 후 (1) 정식 활성화 또는 (2) 정식 제거 결정.
    /// SubBiomeProfileSO 와 책임 분리 / 통합 옵션은 가이드 §2 참조.
    /// </summary>
    [System.Obsolete("데드코드 의심 — 사용처 확인 안건 (지형 가이드 §2).", false)]
    public interface IBiomeSpawnResolver
    {
        float CalculateSpawnProbability(string factionId, int biomeType, Vector3 worldPos);
    }
}
