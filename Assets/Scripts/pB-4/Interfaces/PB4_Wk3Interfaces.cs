// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — Wk3 신규 Forward Interfaces (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 0, v3)
// 위치: Scripts/pB-4/Interfaces/PB4_Wk3Interfaces.cs
//
// ★ 본 파일은 PB4Interfaces.cs (Wk0 동결)와 별개. 신규 3종만 박제.
//   기존 인터페이스 (ITerrainSampler 등)는 PB4Interfaces.cs 그대로 사용.
//
// 신규 3종:
//   ① IGroupAIInfo   (GroupAIManager forward, D3 PM 작성 예정)
//   ② IContextProvider (ContextManager 노출)
//   ③ ContextSnapshot struct (7 필드)
//
// ★ ContextSnapshot 7 필드 박제 박제:
//   IIncidentRecorder는 Wk0 동결 시그니처상 read 메서드 없음 (RecordIncident만).
//   따라서 recentIncidents 필드는 Wk3 시점에 미사용.
//   Wk5+ 시그니처 통일 협의 (협업제안서 v4 §4 [04/W3] BLOCKING) 완료 후
//   Wk5에서 ContextSnapshot v2 (8 필드)로 확장 박제.
//
// 코딩 표준 §3.4 L4 Coordination 계층.
// 골든룰 1 준수: 인터페이스 의존 (구체 클래스 직접 참조 X).
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Interfaces
{
    /// <summary>
    /// GroupAIManager의 읽기 전용 정보 인터페이스.
    /// D3 PM 작성될 GroupAIManager가 본 인터페이스 구현 예정.
    /// ContextManager가 본 인터페이스를 통해 그룹 상태 조회.
    /// </summary>
    public interface IGroupAIInfo
    {
        /// <summary>그룹 사기 (0~1). 1.0 = 만전, 0.0 = 패주.</summary>
        float GetMorale();

        /// <summary>공격 토큰 보유 가능 여부.</summary>
        bool HasAvailableToken();
    }

    /// <summary>
    /// ContextManager가 외부에 노출하는 인터페이스.
    /// BlackboardUpdater + 향후 다른 소비자가 본 인터페이스 통해 접근.
    /// </summary>
    public interface IContextProvider
    {
        /// <summary>지정 좌표의 컨텍스트 스냅샷 반환 (7 필드).</summary>
        ContextSnapshot Sample(Vector3 worldPos);
    }

    /// <summary>
    /// Context 시스템의 단일 스냅샷 (7 필드, Wk3 시점).
    /// ContextManager.Sample() 반환 + BlackboardUpdater 입력.
    /// ★ 자연 발현 박제: 모든 필드는 좌표 + 환경 기반 자연 계산 결과.
    ///
    /// Wk5+ recentIncidents 추가 박제 (시그니처 통일 협의 후).
    /// </summary>
    [System.Serializable]
    public struct ContextSnapshot
    {
        public float slope;          // 0~1 (지형 기울기)
        public float density;        // 0~1 (동굴벽 가까움)
        public float occlusion;      // 0~1 (시야 차폐도)
        public float pathWidth;      // m 단위 (경로 폭, 0.5~5.0)

        public List<string> terrainTags;   // 자연 발현 태그 5종 (Narrow/Open/Dense/Sparse/HighGround)

        public float groupMorale;          // 0~1 (그룹 사기, 그룹 미존재 시 1.0)
        public bool attackTokenAvailable;  // 공격 토큰 보유 (그룹 미존재 시 true)
    }
}
