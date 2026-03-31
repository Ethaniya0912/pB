// =============================================================================
// GameBlackboard.cs  |  pB-4 Project — Week 0
// Layer  : Core (공유 계층)
// Owner  : Person A
//
// 역할:
//   AI, 지형, 시나리오 3개 파트가 공유하는 중앙 데이터 버스.
//   Week 0에서 필드 타입과 접근자를 동결(Freeze)하며,
//   이후 주차에서 필드를 추가할 수는 있으나 기존 필드의 타입/이름을 변경할 수 없다.
//
// 아키텍처:
//   - 읽기: 모든 파트가 자유롭게 읽을 수 있음
//   - 쓰기: 각 파트는 자신의 관할 필드만 쓸 수 있음 (주석으로 관할 명시)
//   - 이벤트: 필드 변경 시 이벤트 버스를 통해 구독자에게 통지
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Core
{
    /// <summary>
    /// 3개 파트(AI/지형/시나리오)가 공유하는 중앙 블랙보드.
    /// Week 0 동결 — 필드 타입 및 접근자 변경 금지.
    /// </summary>
    public class GameBlackboard : MonoBehaviour
    {
        public static GameBlackboard Instance { get; private set; }

        // ==================================================================
        // [지형 파트 관할] 현재 활성 지형 태그
        // ==================================================================
        [Header("Terrain Tags (지형 파트 쓰기)")]
        [SerializeField] private List<string> _activeTerrainTags = new List<string>();
        public IReadOnlyList<string> ActiveTerrainTags => _activeTerrainTags;

        public void SetTerrainTags(List<string> tags)
        {
            _activeTerrainTags = tags ?? new List<string>();
            EventBus.RaiseTerrainTagChanged(_activeTerrainTags);
        }

        // ==================================================================
        // [시나리오 파트 관할] 현재 상황 벡터 (768차원, RAG 검색용)
        // ==================================================================
        [Header("Situation Vector (시나리오 파트 쓰기)")]
        [SerializeField] private float[] _activeSituationVector = new float[768];
        public float[] ActiveSituationVector => _activeSituationVector;

        public void SetSituationVector(float[] vector)
        {
            if (vector == null || vector.Length != 768)
            {
                Debug.LogError("[GameBlackboard] SituationVector는 768차원이어야 합니다.");
                return;
            }
            Array.Copy(vector, _activeSituationVector, 768);
        }

        // ==================================================================
        // [지형 파트 관할] 전역 긴장도 점수
        // ==================================================================
        [Header("Intensity (지형 파트 쓰기)")]
        [SerializeField] private float _globalIntensityScore;
        public float GlobalIntensityScore => _globalIntensityScore;

        public void SetGlobalIntensity(float score)
        {
            _globalIntensityScore = Mathf.Clamp01(score);
        }

        // ==================================================================
        // [AI 파트 관할] 현재 캐릭터 스탯
        // ==================================================================
        [Header("Character Stats (AI 파트 쓰기)")]
        private Dictionary<string, float> _currentCharacterStats = new Dictionary<string, float>();
        public IReadOnlyDictionary<string, float> CurrentCharacterStats => _currentCharacterStats;

        public void SetCharacterStat(string key, float value)
        {
            _currentCharacterStats[key] = value;
        }

        // ==================================================================
        // [시나리오 파트 관할] 활성 팩션 레지스트리
        // ==================================================================
        [Header("Faction Registry (시나리오 파트 쓰기)")]
        private Dictionary<string, FactionWorldState> _activeFactionRegistry 
            = new Dictionary<string, FactionWorldState>();
        public IReadOnlyDictionary<string, FactionWorldState> ActiveFactionRegistry 
            => _activeFactionRegistry;

        public void SetFactionState(string factionId, FactionWorldState state)
        {
            _activeFactionRegistry[factionId] = state;
            EventBus.RaiseFactionStateChanged(factionId, state);
        }

        // ==================================================================
        // Lifecycle
        // ==================================================================
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }
    }

    /// <summary>
    /// 팩션의 월드 상태 데이터. MetaScenarioGenerator가 초기 설정.
    /// </summary>
    [System.Serializable]
    public struct FactionWorldState
    {
        [Range(0f, 1f)] public float populationLevel;
        public bool isDecimated;
        public bool isDominant;
        public List<int> territoryNodes;
    }
}
