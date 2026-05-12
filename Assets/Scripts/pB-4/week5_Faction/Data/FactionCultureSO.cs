// ============================================================================
// FactionCultureSO.cs
// Faction 5 축 문화 ScriptableObject
// 영역: 컨셉트 (정적 정의)
// 항목: P-10
// 11사안 v3 §6.2 박제 반영
// ============================================================================
//
// 5 축 의미:
//   - Brutality      : 잔인함 (사냥/약탈 선호 / 식생 소비량 ↑)
//   - Territoriality : 영토 집착도 (영토 확장 가중치 / 침입 반응 강도)
//   - Cohesion       : 응집력 (분할 저항도 / FleeAction 발동 임계)
//   - Diplomacy      : 외교 능력 (협상/우호 / 전쟁 vs 외교 선택)
//   - Cunning        : 교활함 (지능/계략 / 매복 행동 가중치)
//
// 종족별 권장 프리셋 (★ 임시 — 디렉션 합류 시 협의):
//   Goblin    : 0.7 / 0.4 / 0.2 / 0.1 / 0.3  (잔인 + 응집 약 + 외교 X)
//   Orc       : 0.6 / 0.7 / 0.6 / 0.3 / 0.5  (호전 + 영토 강 + 응집 강)
//   Bandit    : 0.5 / 0.3 / 0.3 / 0.4 / 0.6  (교활 + 영토 약)
//   Guild     : 0.3 / 0.6 / 0.7 / 0.8 / 0.8  (외교 + 교활 + 응집 강)
// ============================================================================

using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(
        menuName = "pB4/Faction/FactionCulture",
        fileName = "FactionCulture_NewFaction")]
    public class FactionCultureSO : ScriptableObject
    {
        [Header("━━━ Identity ━━━")]
        
        [Tooltip("FactionDefinitionSO 의 factionId 와 일치해야 함")]
        [SerializeField] private string _factionId = "";
        
        public string FactionId => _factionId;
        
        [Header("━━━ 5 축 문화 ━━━")]
        
        [Tooltip("잔인함 — 사냥/약탈 선호 + 식생 소비량")]
        [Range(0f, 1f)] [SerializeField] private float _brutality = 0.5f;
        
        [Tooltip("영토 집착도 — 영토 확장 가중치 + 침입 반응 강도")]
        [Range(0f, 1f)] [SerializeField] private float _territoriality = 0.5f;
        
        [Tooltip("응집력 — 분할 저항도 + Flee 임계")]
        [Range(0f, 1f)] [SerializeField] private float _cohesion = 0.5f;
        
        [Tooltip("외교 능력 — 협상/우호 + 전쟁 vs 외교 선택")]
        [Range(0f, 1f)] [SerializeField] private float _diplomacy = 0.5f;
        
        [Tooltip("교활함 — 지능/계략 + 매복 행동 가중치")]
        [Range(0f, 1f)] [SerializeField] private float _cunning = 0.5f;
        
        public float Brutality      => _brutality;
        public float Territoriality => _territoriality;
        public float Cohesion       => _cohesion;
        public float Diplomacy      => _diplomacy;
        public float Cunning        => _cunning;
        
        [Header("━━━ 영토 / 활동 반경 (기본값) ━━━")]
        
        [Tooltip("기본 영토 반경 (m). FactionTerritory.coreRadius 의 초기값.")]
        [SerializeField] private float _baseTerritoryRadius = 30f;
        
        [Tooltip("기본 활동 반경 (m). FactionActivityRange.radiusFromCore 의 초기값.")]
        [SerializeField] private float _baseActivityRadius = 60f;
        
        public float BaseTerritoryRadius => _baseTerritoryRadius;
        public float BaseActivityRadius  => _baseActivityRadius;
        
        [Header("━━━ SubBiome 선호 (Tier2 연계) ━━━")]
        
        [Tooltip("선호 SubBiome 명칭 배열. Tier2 SubBiomeProfileSO 와 연계. 예: \"DryKarst\", \"WetCavern\"")]
        [SerializeField] private string[] _preferredSubBiomes = new string[0];
        
        public string[] PreferredSubBiomes => _preferredSubBiomes;
        
        // ────────────────────────────────────────────────────────
        // (Wk5+ P-20) 마커 / trace 선호 — 협의 후 추가
        // ────────────────────────────────────────────────────────
        
        // (지형팀 협의 후 P-20 단계에서 추가)
        // [SerializeField] private TerritoryMarkerType[] _preferredMarkers;
        // [SerializeField] private ActivityTraceType[]   _preferredTraces;
    }
}
