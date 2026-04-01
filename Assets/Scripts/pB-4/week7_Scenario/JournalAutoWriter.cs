// =============================================================================
// JournalAutoWriter.cs  |  pB-4 Project — Week 7
// Layer  : L4 Event (연출)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   탈출 성공 시 자동으로 수첩에 자연어 문장을 기록한다.
//   가장 Stability가 낮은 NPC를 선정하여 그 NPC의 시점에서 기록.
//
//   기록 로직:
//     1. 탈출 성공(Exit) 이벤트 수신
//     2. 블랙보드 로그 스캔 (isAbandoned, isAllyDead 등)
//     3. Stability 가장 낮은 NPC 선정 → 화자(narrator)
//     4. 데이터시트 템플릿 + 성격축으로 자연어 문장 자동 생성
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.Scenario
{
    /// <summary>수첩 항목.</summary>
    [Serializable]
    public class JournalEntry
    {
        [Tooltip("기록 시점의 게임 시간")]
        public float timestamp;
        [Tooltip("화자(narrator) NPC 이름")]
        public string narratorName;
        [Tooltip("자동 생성된 자연어 문장")]
        [TextArea(2, 5)]
        public string content;
        [Tooltip("관련 사건 ID (IncidentRecorder 연동)")]
        public string relatedIncidentId;
    }

    public class JournalAutoWriter : MonoBehaviour
    {
        [Header("━━━ 수첩 내용 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("자동 기록된 수첩 항목 목록 (Read Only). " +
                 "Play 중 OnDungeonEscape() 호출 시 추가됨.")]
        [SerializeField] private List<JournalEntry> entries = new List<JournalEntry>();

        [Header("━━━ 문장 템플릿 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("탈출 성공 시 사용할 문장 템플릿 목록. " +
                 "{narrator}=화자 이름, {event}=사건 요약, {feeling}=감정 표현. " +
                 "비어있으면 기본 템플릿 자동 생성.")]
        [SerializeField] private List<string> templates = new List<string>();

        [Header("━━━ 감정 어휘 (성격축 연동) ━━━━━━━━━━")]
        [Tooltip("Stability 낮은 NPC가 사용하는 감정 표현. " +
                 "예: '손이 떨렸다', '식은땀이 흘렀다'")]
        [SerializeField] private List<string> lowStabilityFeelings = new List<string>();

        [Tooltip("Stability 높은 NPC가 사용하는 감정 표현. " +
                 "예: '담담했다', '아무렇지 않은 척했다'")]
        [SerializeField] private List<string> highStabilityFeelings = new List<string>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Awake()
        {
            if (templates.Count == 0)
            {
                templates.Add("{narrator}\ub294 \ub3d9\uad74\uc5d0\uc11c \ube60\uc838\ub098\uc654\ub2e4. {event} {feeling}");
                templates.Add("\uadf8\ub0a0 {narrator}\ub294 {event}\ub97c \ubcf4\uc558\ub2e4. {feeling}");
                templates.Add("{narrator}\uc758 \uae30\uc5b5 \uc18d\uc5d0 {event}\uc774 \uc0c8\uaca8\uc84c\ub2e4. {feeling}");
            }
            if (lowStabilityFeelings.Count == 0)
            {
                lowStabilityFeelings.AddRange(new[]{ "\uc190\uc774 \ub5a8\ub838\ub2e4", "\uc2dd\uc740\ub540\uc774 \ud758\ub800\ub2e4", "\ub2e4\ub9ac\uc5d0 \ud798\uc774 \ud480\ub838\ub2e4", "\ub208\ubb3c\uc774 \uba48\ucd94\uc9c0 \uc54a\uc558\ub2e4", "\uc228\uc774 \uba47\uc744 \uac83 \uac19\uc558\ub2e4" });
            }
            if (highStabilityFeelings.Count == 0)
            {
                highStabilityFeelings.AddRange(new[]{ "\ub2f4\ub2f4\ud588\ub2e4", "\uc544\ubb34\ub807\uc9c0 \uc54a\uc740 \ucc99\ud588\ub2e4", "\uc870\uc6a9\ud788 \uc785\uc744 \ub2e4\ubb3c\uc5c8\ub2e4", "\ub208\uc744 \uac10\uc558\ub2e4", "\uadf8\ub0e5 \uac78\uc5c8\ub2e4" });
            }
        }

        // ==================================================================
        // 핵심 API: 탈출 시 자동 기록
        // ==================================================================

        /// <summary>
        /// 던전 탈출 성공 시 호출. 자동으로 수첩 항목을 생성한다.
        /// </summary>
        /// <param name="eventSummary">사건 요약. 예: '동료를 버리고 도주했다'</param>
        /// <param name="relatedIncidentId">관련 IncidentRecorder ID</param>
        public JournalEntry OnDungeonEscape(string eventSummary = "", string relatedIncidentId = "")
        {
            // 1. Stability 가장 낮은 NPC 선정 (화자)
            var humanoids = FindObjectsByType<HumanoidAIBrain>(FindObjectsSortMode.None);
            HumanoidAIBrain narrator = null;
            float lowestStability = float.MaxValue;

            foreach (var h in humanoids)
            {
                if (h.Personality.stability < lowestStability)
                {
                    lowestStability = h.Personality.stability;
                    narrator = h;
                }
            }

            if (narrator == null)
            {
                if (debugLog) Debug.Log("[Journal] NPC \uc5c6\uc74c. \uae30\ub85d \uc2a4\ud0b5.");
                return null;
            }

            // 2. 감정 표현 선택 (성격축 연동)
            string feeling = lowestStability < 0.4f
                ? lowStabilityFeelings[UnityEngine.Random.Range(0, lowStabilityFeelings.Count)]
                : highStabilityFeelings[UnityEngine.Random.Range(0, highStabilityFeelings.Count)];

            // 3. 사건 요약이 비어있으면 블랙보드에서 추론
            if (string.IsNullOrEmpty(eventSummary))
            {
                var bb = GameBlackboard.Instance;
                if (bb != null && bb.ActiveTerrainTags != null && bb.ActiveTerrainTags.Count > 0)
                    eventSummary = $"\uc9c0\ud615 [{string.Join(",", bb.ActiveTerrainTags)}]\uc5d0\uc11c\uc758 \ud0d0\ud5d8";
                else
                    eventSummary = "\ub610 \ud55c \ubc88\uc758 \ud0d0\ud5d8";
            }

            // 4. 템플릿 기반 문장 생성
            string template = templates[UnityEngine.Random.Range(0, templates.Count)];
            string content = template
                .Replace("{narrator}", narrator.name)
                .Replace("{event}", eventSummary)
                .Replace("{feeling}", feeling);

            var entry = new JournalEntry
            {
                timestamp = Time.time,
                narratorName = narrator.name,
                content = content,
                relatedIncidentId = relatedIncidentId
            };

            entries.Add(entry);

            if (debugLog)
                Debug.Log($"[Journal] \uc218\ucca9 \uae30\ub85d: '{content}' (\ud654\uc790={narrator.name}, stability={lowestStability:F2})");

            return entry;
        }

        public IReadOnlyList<JournalEntry> Entries => entries;
    }
}
