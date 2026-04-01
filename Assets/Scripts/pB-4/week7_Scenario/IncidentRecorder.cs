// =============================================================================
// IncidentRecorder.cs  |  pB-4 Project — Week 7
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   결정적 게임 사건(동료 유기, 보스 처치, 선택 분기 등)을 벡터 스냅샷으로
//   JSON 직렬화하여 기록한다. LightweightRAGEngine이 이 기록을 검색하여
//   과거 사건과의 유사도를 계산한다.
//
//   Incident 데이터:
//     Incident_ID, Intensity_Score, Moral_Alignment,
//     Terrain_Snapshot (활성 태그), Entity_Snapshot (NPC 상태),
//     Player_State, Summary_Prose, Dialogue_Anchor
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Scenario
{
    [Serializable]
    public class IncidentSnapshot
    {
        [Tooltip("사건 고유 ID. 예: 'incident_goblin_ambush_003'")]
        public string incidentId;

        [Tooltip("사건 강도 (0.0~1.0). 높을수록 극적인 사건. " +
                 "RAG 검색 시 유사도 가중치로 사용.")]
        public float intensityScore;

        [Tooltip("도덕적 성향 (-1.0~+1.0). 선/악 판정.")]
        public float moralAlignment;

        [Tooltip("사건 발생 시점의 활성 지형 태그 스냅샷")]
        public List<string> terrainTags = new List<string>();

        [Tooltip("사건 발생 시점의 전역 긴장도")]
        public float globalIntensity;

        [Tooltip("요약 문장. 수첩에 기록될 자연어 설명.")]
        public string summaryProse;

        [Tooltip("관련 대사 앵커. 재방문 시 회상 대사 트리거.")]
        public string dialogueAnchor;

        [Tooltip("사건 발생 시간 (게임 내 초)")]
        public float gameTimestamp;

        [Tooltip("768차원 상황 벡터 스냅샷 (코사인 유사도 검색용). " +
                 "null이면 기록 시점의 블랙보드 벡터를 자동 복사.")]
        public float[] situationVector;
    }

    public class IncidentRecorder : MonoBehaviour
    {
        [Header("━━━ 기록 저장소 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("기록된 사건 목록. Play 중 RecordIncident()로 추가됨. " +
                 "Inspector에서 기록 내용을 확인할 수 있다.")]
        [SerializeField] private List<IncidentSnapshot> records = new List<IncidentSnapshot>();

        [Header("━━━ 자동 기록 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("자동으로 기록할 최소 강도. 이 값 미만의 사건은 기록하지 않음. " +
                 "0.3 = 강도 30% 이상의 사건만 기록.")]
        [Range(0f, 1f)]
        public float autoRecordThreshold = 0.3f;

        [Tooltip("최대 기록 수. 이 수를 넘으면 가장 오래된 기록 삭제. " +
                 "100 = 최근 100건만 유지.")]
        [Range(10, 500)]
        public int maxRecords = 100;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        // ==================================================================
        // 핵심 API: 사건 기록
        // ==================================================================

        /// <summary>
        /// 결정적 사건을 기록한다. 강도가 autoRecordThreshold 미만이면 무시.
        /// </summary>
        public void RecordIncident(string id, float intensity, float moral, string summary, string dialogueAnchor = "")
        {
            if (intensity < autoRecordThreshold) return;

            var bb = GameBlackboard.Instance;
            var snapshot = new IncidentSnapshot
            {
                incidentId = id,
                intensityScore = intensity,
                moralAlignment = moral,
                terrainTags = bb != null && bb.ActiveTerrainTags != null ? new List<string>(bb.ActiveTerrainTags) : new List<string>(),
                globalIntensity = bb != null ? bb.GlobalIntensityScore : 0f,
                summaryProse = summary,
                dialogueAnchor = dialogueAnchor,
                gameTimestamp = Time.time,
                situationVector = bb != null ? (float[])bb.ActiveSituationVector.Clone() : null
            };

            records.Add(snapshot);

            // 용량 제한
            while (records.Count > maxRecords) records.RemoveAt(0);

            if (debugLog)
                Debug.Log($"[Incident] 기록: {id} (intensity={intensity:F2}, moral={moral:F2}) '{summary}'");
        }

        /// <summary>전체 기록을 JSON 문자열로 직렬화.</summary>
        public string SerializeToJSON()
        {
            return JsonUtility.ToJson(new IncidentRecordWrapper { incidents = records }, true);
        }

        /// <summary>JSON에서 기록을 역직렬화.</summary>
        public void DeserializeFromJSON(string json)
        {
            var wrapper = JsonUtility.FromJson<IncidentRecordWrapper>(json);
            if (wrapper != null && wrapper.incidents != null)
                records = wrapper.incidents;
        }

        public IReadOnlyList<IncidentSnapshot> Records => records;

        [Serializable]
        private class IncidentRecordWrapper { public List<IncidentSnapshot> incidents; }
    }
}
