using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem
{
    [System.Serializable]
    public struct CaveAnalyticsData
    {
        public int totalTriangles;
        public int expectedOreCount;
        public float criticalPathLength;
        public float chokePointRatio;
    }

    /// <summary>
    /// [Part 5] 에디터 프리뷰 윈도우에서 튜닝이 완료된 지형(그래프 + 바이옴 설정)을 
    /// 스냅샷 형태로 영구 저장하여 다른 씬이나 이벤트에 재사용하기 위한 데이터 컨테이너입니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCavePreset", menuName = "Cave System/Cave Preset Data")]
    public class CavePresetData : ScriptableObject
    {
        [Header("Preset Info")]
        public string presetName = "New Dungeon Preset";
        public string description = "이 던전 프리셋에 대한 설명을 입력하세요.";

        [Header("Graph Blueprint (설계도)")]
        public List<NodeData> savedNodes = new List<NodeData>();
        public List<EdgeData> savedEdges = new List<EdgeData>();

        [Header("Biome & Shape Params (지형 특성)")]
        public BiomeParamData savedBiomeParams;
        public int debugStageOverride; // 뼈대만 저장했는지, 최종본인지 식별용

        [Header("Analytics (통계 정보)")]
        public CaveAnalyticsData analyticsInfo;
    }
}