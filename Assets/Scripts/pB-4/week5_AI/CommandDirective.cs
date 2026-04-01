// =============================================================================
// CommandDirective.cs  |  pB-4 Project — Week 5
// Layer  : L2 Router (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   플레이어가 NPC에게 내리는 선언적 커맨드를 정의하고 실행한다.
//   각 커맨드는 심각도(severity)를 가지며, TrustMatrix의 AcceptanceScore로
//   NPC가 수락할지 거부할지 판정된다.
//
//   4종 커맨드:
//     Hold Formation (severity=0.2): 현재 위치 유지. 안전한 명령.
//     Scatter (severity=0.3): 흩어져서 개별 행동. 보통 위험.
//     Phalanx (severity=0.5): 밀집 대형. 꽤 위험 (적에게 노출).
//     Retreat (severity=0.1): 후퇴. 가장 안전한 명령.
//
// 연계:
//   - TrustMatrix.EvaluateCommand(): 명령 수락/거부 판정
//   - FormationSlotAllocator: Phalanx 커맨드 시 대형 재배치
//   - GroupAIManager: 커맨드에 따라 토큰/역할 재조정
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.AI
{
    public enum CommandType
    {
        /// <summary>현재 위치 유지. severity=0.2. 가장 안전.</summary>
        HoldFormation,
        /// <summary>흩어져서 개별 행동. severity=0.3.</summary>
        Scatter,
        /// <summary>밀집 대형으로 전환. severity=0.5. 적에게 노출 위험.</summary>
        Phalanx,
        /// <summary>후퇴 명령. severity=0.1. 매우 안전.</summary>
        Retreat
    }

    /// <summary>개별 NPC의 커맨드 응답 결과.</summary>
    [Serializable]
    public class CommandResponse
    {
        public string npcName;
        public CommandType command;
        public bool accepted;
        public float acceptanceScore;
        public string reason;
    }

    public class CommandDirective : MonoBehaviour
    {
        [Header("━━━ 커맨드별 심각도 ━━━━━━━━━━━━━━━━━")]
        [Tooltip("Hold Formation의 심각도. 낮을수록 안전한 명령. " +
                 "0.2 = 매우 안전. 대부분의 NPC가 수락.")]
        [Range(0f, 1f)] public float severityHold = 0.2f;

        [Tooltip("Scatter의 심각도. 0.3 = 약간 위험.")]
        [Range(0f, 1f)] public float severityScatter = 0.3f;

        [Tooltip("Phalanx의 심각도. 0.5 = 꽤 위험. " +
                 "Doubt 단계 NPC는 거부할 수 있음.")]
        [Range(0f, 1f)] public float severityPhalanx = 0.5f;

        [Tooltip("Retreat의 심각도. 0.1 = 가장 안전. " +
                 "Hostility NPC도 후퇴는 수락할 수 있음.")]
        [Range(0f, 1f)] public float severityRetreat = 0.1f;

        [Header("━━━ 대상 NPC 목록 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("커맨드 대상 NPC 목록. Inspector에서 드래그 할당.")]
        [SerializeField] private List<HumanoidAIBrain> targetNPCs = new List<HumanoidAIBrain>();

        [Header("━━━ 마지막 커맨드 결과 (Read Only) ━━━━")]
        [SerializeField] private List<CommandResponse> lastResponses = new List<CommandResponse>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        // ==================================================================
        // 핵심 API: 커맨드 실행
        // ==================================================================

        /// <summary>
        /// 모든 대상 NPC에게 커맨드를 내린다.
        /// 각 NPC의 TrustMatrix가 수락/거부를 판정.
        /// </summary>
        public List<CommandResponse> IssueCommand(CommandType command)
        {
            lastResponses.Clear();
            float severity = GetSeverity(command);

            foreach (var npc in targetNPCs)
            {
                if (npc == null) continue;
                var trust = npc.GetComponent<TrustMatrix>();
                float npcFear = npc.fear;

                bool accepted = true;
                float score = 1f;

                if (trust != null)
                {
                    accepted = trust.EvaluateCommand(severity, npcFear);
                    score = (trust.GetNormalizedTrust() * 0.6f) + ((1f - severity) * 0.4f) - npcFear;
                }

                var response = new CommandResponse
                {
                    npcName = npc.name,
                    command = command,
                    accepted = accepted,
                    acceptanceScore = score,
                    reason = accepted ? "Trust sufficient" : $"Trust too low (fear={npcFear:F2})"
                };
                lastResponses.Add(response);

                if (debugLog)
                    Debug.Log($"[Command] {command} \u2192 {npc.name}: {(accepted ? "\u2705\uc218\ub77d" : "\u274c\uac70\ubd80")} (score={score:F3})");
            }

            return lastResponses;
        }

        private float GetSeverity(CommandType cmd) => cmd switch
        {
            CommandType.HoldFormation => severityHold,
            CommandType.Scatter => severityScatter,
            CommandType.Phalanx => severityPhalanx,
            CommandType.Retreat => severityRetreat,
            _ => 0.5f
        };

        public void RegisterNPC(HumanoidAIBrain npc) { if (!targetNPCs.Contains(npc)) targetNPCs.Add(npc); }
        public IReadOnlyList<CommandResponse> LastResponses => lastResponses;
    }
}
