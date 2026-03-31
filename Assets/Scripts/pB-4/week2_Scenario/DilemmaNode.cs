// =============================================================================
// DilemmaNode.cs  |  pB-4 Project — Week 2
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   시나리오 아크의 Climax 단계에서 등장하는 2지선다 분기 노드.
//   플레이어가 선택지 A 또는 B를 선택하면:
//     1. 결과 태그(Legacy Tag)가 블랙보드에 등록되어 이후 아크에 영향
//     2. 인격 피봇팅이 즉각 반영되어 NPC 성격이 변화
//     3. 다음 노드의 경로가 결정됨
//
//   예: "동료를 구출할 것인가(A), 유기할 것인가(B)?"
//   A 선택 → '생명의 은인' 태그 + 파티 전체 신뢰도 +30
//   B 선택 → '비겁자' 태그 + 파티 전체 신뢰도 -70 + 유기된 NPC Stability -0.5
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Scenario
{
    /// <summary>
    /// 딜레마의 개별 선택지 데이터.
    /// Inspector에서 각 선택지의 결과를 세밀하게 설정한다.
    /// </summary>
    [Serializable]
    public class DilemmaChoice
    {
        [Tooltip("선택지 표시 텍스트. UI에서 플레이어에게 보여주는 문구. " +
                 "예: '위험을 무릅쓰고 동료를 구출한다'")]
        [TextArea(2, 4)]
        public string displayText;

        [Tooltip("이 선택으로 인해 부여되는 Legacy Tag 목록. " +
                 "Week 4의 NarrativeBiddingEvaluator가 이 태그를 읽어 다음 아크 방향을 결정한다. " +
                 "예: 'Betrayer', 'Savior', 'Pragmatist'")]
        public List<string> resultTags = new List<string>();

        [Header("━━━ 인격 피봇팅 즉각 반영 ━━━━━━━━━━━━")]
        [Tooltip("이 선택 시 NPC 성격 5축에 적용할 변화량. " +
                 "순서: [Control, Stability, Openness, Agreeable, Directness]. " +
                 "예: 동료 구출 시 → [0, +0.1, 0, +0.15, 0] (안정성/우호성 상승). " +
                 "동료 유기 시 → [0, -0.2, 0, -0.3, +0.1] (안정성/우호성 하락, 직설성 상승). " +
                 "길이가 5가 아니면 무시된다.")]
        public float[] personalityPivotDelta = new float[5];

        [Tooltip("이 선택 후 연결되는 다음 PlotTile ID. " +
                 "ScenarioArcManager가 이 ID의 PlotTile을 다음에 활성화한다.")]
        public string nextTileId;

        [Tooltip("이 선택의 도덕적 성향 (-1.0=악, 0=중립, +1.0=선). " +
                 "Week 4의 IKarmaDirector에 전달되어 업보에 누적된다.")]
        [Range(-1f, 1f)]
        public float moralAlignment = 0f;
    }

    /// <summary>
    /// 2지선다 딜레마 노드. ScenarioArcManager의 Climax Phase에서 활성화된다.
    /// </summary>
    public class DilemmaNode : MonoBehaviour
    {
        [Header("━━━ 딜레마 정보 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 딜레마의 고유 ID. ScenarioArcSO의 climaxDilemma.dilemmaId와 매칭된다.")]
        public string dilemmaId;

        [Tooltip("딜레마 상황 설명 텍스트. UI에서 플레이어에게 보여주는 상황 묘사. " +
                 "예: '동료가 무너진 통로 아래에 깔려있다. 적이 접근하고 있다.'")]
        [TextArea(3, 6)]
        public string situationDescription;

        [Header("━━━ 선택지 A ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("첫 번째 선택지. 보통 '도덕적으로 올바른' 선택. " +
                 "예: 동료 구출, 자원 공유, 적 용서")]
        public DilemmaChoice choiceA;

        [Header("━━━ 선택지 B ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("두 번째 선택지. 보통 '실리적이거나 잔인한' 선택. " +
                 "예: 동료 유기, 자원 독점, 적 처형")]
        public DilemmaChoice choiceB;

        [Header("━━━ 상태 (Read Only) ━━━━━━━━━━━━━━━━")]
        [Tooltip("이 딜레마가 이미 결정되었는지 여부. true이면 다시 선택할 수 없다.")]
        [SerializeField] private bool isResolved = false;

        [Tooltip("결정된 선택지. 'A' 또는 'B'. 미결정이면 빈 문자열.")]
        [SerializeField] private string resolvedChoice = "";

        /// <summary>딜레마가 이미 결정되었는지 여부.</summary>
        public bool IsResolved => isResolved;
        /// <summary>결정된 선택지 ('A' 또는 'B').</summary>
        public string ResolvedChoice => resolvedChoice;

        // ==================================================================
        // 핵심 API: 선택지 결정
        // ==================================================================

        /// <summary>
        /// 플레이어가 선택지 A를 선택한다. 결과를 즉시 적용.
        /// UI 버튼의 OnClick에서 호출하거나, AI가 자동 선택 시 호출.
        /// </summary>
        public DilemmaChoice ResolveChoiceA()
        {
            return ResolveChoice("A", choiceA);
        }

        /// <summary>플레이어가 선택지 B를 선택한다.</summary>
        public DilemmaChoice ResolveChoiceB()
        {
            return ResolveChoice("B", choiceB);
        }

        private DilemmaChoice ResolveChoice(string label, DilemmaChoice choice)
        {
            if (isResolved)
            {
                Debug.LogWarning($"[DilemmaNode] '{dilemmaId}'\ub294 \uc774\ubbf8 \uacb0\uc815\ub428 ({resolvedChoice}). \uc7ac\uc120\ud0dd \ubd88\uac00.");
                return null;
            }

            isResolved = true;
            resolvedChoice = label;

            // 1. Legacy Tag 등록
            if (choice.resultTags != null)
            {
                foreach (var tag in choice.resultTags)
                    Debug.Log($"[DilemmaNode] Legacy Tag \ub4f1\ub85d: '{tag}' (\uc120\ud0dd={label})");
            }

            // 2. 인격 피봇팅 이벤트 발행
            if (choice.personalityPivotDelta != null && choice.personalityPivotDelta.Length == 5)
            {
                // EventBus를 통해 HumanoidAIBrain에 피봇팅 요청
                // Week 3에서 ArcProgressionManager가 이 이벤트를 중계
                Debug.Log($"[DilemmaNode] \uc778\uaca9 \ud53c\ubd07\ud305: [{string.Join(",", choice.personalityPivotDelta)}]");
            }

            // 3. 아크 Phase 전이 이벤트
            EventBus.RaiseArcPhaseChanged(dilemmaId, choice.moralAlignment > 0 ? 1 : -1);

            Debug.Log($"[DilemmaNode] \ub51c\ub808\ub9c8 '{dilemmaId}' \uacb0\uc815: \uc120\ud0dd={label}, " +
                      $"\ub3c4\ub355\uc131={choice.moralAlignment:F1}, \ub2e4\uc74c={choice.nextTileId}");

            return choice;
        }

        /// <summary>딜레마를 미결정 상태로 리셋 (테스트용).</summary>
        public void ResetDilemma()
        {
            isResolved = false;
            resolvedChoice = "";
        }
    }
}
