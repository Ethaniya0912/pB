// ═══════════════════════════════════════════════════════════════════════════════
// ScenarioBridgeContracts.cs (v1.1)
// ═══════════════════════════════════════════════════════════════════════════════
// ★ 책임 파트 = 시나리오 (Scenario)
// ★ 현재 상태 = AI 파트가 임시 정의 — 시나리오팀 협의 전
//
// ─── v1.0 → v1.1 변경 사항 (2026-05-12) ────────────────────────────────────────
//
// [리네임 — Scenario 접두사 통일]
//   - 구조체명: CommandRequest    → ScenarioCommandRequest
//   - enum명  : CommandType       → ScenarioCommandType
//
// [변경 이유]
//   본체 AIBridgeContracts.cs 의 `CommandType` enum (분대 전술 명령 — FocusFire/SplitFire 등)
//   과 namespace TDA.PB4.Interfaces 안에서 이름 충돌 발견.
//
//   본체 enum = 전술 (Tactical) / Wk4 enum = 시나리오 (Narrative) — 의미 다른 두 영역.
//   본체는 그대로 유지 + Wk4 측을 "Scenario" 접두사로 통일 (일관성 + 본체 호환).
//
// [영향 범위]
//   - 본 파일 (ScenarioBridgeContracts.cs) — 구조체 / enum 이름 변경
//   - HumanoidAIBrain.Acceptance.cs — EvaluateAcceptance(ScenarioCommandRequest)
//   - HumanoidAIBrain.Dilemma.cs — 본문 내 참조 모두 변경
//   - 본체 AIBridgeContracts.cs — 변경 X (그대로 유지)
//
// ─── 임의 정의 히스토리 (★ 협의 필요) ──────────────────────────────────────────
//
// [2026-05-12 v1.0] — AI 파트가 임시 정의 (작성: Wk4 본격 진행 시점)
//   이유   : Wk4-2 AcceptanceScore + Wk4-6 DilemmaPivot 본격 구현 시
//            시나리오 → AI 명령 채널의 ScenarioCommandRequest 구조 필요.
//            그러나 시나리오팀 협의가 아직 진행 전.
//   해결   : AI 파트가 합리적 시그니처를 임의 채택 — 본 파일에 임시 정의.
//   향후   : 시나리오팀 협의 시점 도래 → 본 파일 책임을 시나리오 파트로 이전.
//            시그니처 변경 가능 (시나리오팀 의견 반영).
//
// [2026-05-12 v1.1] — Scenario 접두사 통일 (본체 CommandType 과 충돌 해소)
//
// [협의 의제] — 시나리오팀과 합의 시점에 확인 필요
//   1. ScenarioCommandRequest 필드 — severity / scenarioModifier / type / commandId 적절?
//   2. ScenarioCommandType enum — 6 종 (Standard / MoralChoice / DangerousOrder /
//      RescueRequest / AbandonRequest / Sacrifice) 충분?
//   3. 시그니처 동결 시점 — Wk5 디렉션 동결 시점에 함께? 또는 더 이른?
//   4. 본 파일을 시나리오 파트 namespace 로 이전? (현재 TDA.PB4.Interfaces 유지)
//   5. DilemmaPivotSO 와의 연동 시그니처 — Wk4-6 의제 와 함께
//
// ─── 기본 정보 ────────────────────────────────────────────────────────────────
//
// 위치     : Assets/Scripts/Bridges&Interfaces/Contracts/ScenarioBridgeContracts.cs
// namespace: TDA.PB4.Interfaces (Bridge v13 표준)
// 호출자   : AI 파트 (HumanoidAIBrain.Acceptance / HumanoidAIBrain.Dilemma)
// 공급자   : 시나리오 파트 (★ 협의 후) / 현재는 AI 파트 임시
// 버전     : v1.1 (2026-05-12) — Scenario 접두사 통일
// 기반     : 기술개발 개론서 v11 §5.4.3 A / Wk4 의제 종합 v2 §2.2 / §2.6
// 관련 문서: pB4_ScenarioTeamAgenda_v1.docx §2 (CommandRequest 시그니처 협의)
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace TDA.PB4.Interfaces
{
    // ═══════════════════════════════════════════════════════════════════
    // ScenarioCommandRequest — 시나리오 → AI 명령 요청
    // ═══════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// 시나리오 측이 NPC 에게 명령 시 전달하는 구조체.
    /// AI 파트의 EvaluateAcceptance / EvaluateDilemma 의 입력.
    /// </summary>
    /// <remarks>
    /// ★ 본 구조체 = 시나리오 파트 책임 / 현재 AI 파트가 임시 정의 (위 헤더 참조).
    /// ★ v1.1 — CommandRequest 에서 리네임 (본체 SquadCommand 와 의미 분리).
    /// </remarks>
    [Serializable]
    public struct ScenarioCommandRequest
    {
        /// <summary>명령의 심각도 (0~1). 위험 / 도덕적 부담 정도.</summary>
        [Range(0f, 1f)] public float severity;
        
        /// <summary>시나리오 측 보정 계수 (디렉터 의도 — 평소 1.0).</summary>
        public float scenarioModifier;
        
        /// <summary>명령 종류.</summary>
        public ScenarioCommandType type;
        
        /// <summary>명령 식별 ID (디버그 / 진단).</summary>
        public string commandId;
        
        public static ScenarioCommandRequest Default => new ScenarioCommandRequest
        {
            severity = 0.5f,
            scenarioModifier = 1.0f,
            type = ScenarioCommandType.Standard,
            commandId = ""
        };
    }
    
    /// <summary>
    /// 시나리오 명령 종류. 6 종 — AI 파트가 임의 채택 (시나리오팀 협의 시 변경 가능).
    /// ★ v1.1 — CommandType 에서 리네임 (본체 분대 명령 CommandType 와 충돌 해소).
    /// </summary>
    public enum ScenarioCommandType
    {
        Standard = 0,           // 표준 명령
        MoralChoice = 1,        // 도덕적 선택 (DilemmaPivot)
        DangerousOrder = 2,     // 위험한 명령 (절벽 진입 등)
        RescueRequest = 3,      // 동료 구출
        AbandonRequest = 4,     // 동료 유기 / 후퇴
        Sacrifice = 5           // 자살 임무 (Blind Trust 만)
    }
}
