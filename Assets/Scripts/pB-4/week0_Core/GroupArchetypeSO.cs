// =============================================================================
// GroupArchetypeSO.cs  |  pB-4 Project — Wk5 D1 (★ 명명 변경)
// Layer  : L1 Data (Core)
// Namespace: TDA.PB4.Data
//
// 역할:
//   그룹의 Collapse 진입 시 발현되는 양식 — 5축 좌표 + escalationModifier
//   + onCollapseSpeechId 를 보유. GroupAIManager 측 1 자산 부착.
//
//   ★ 그룹 단위 자산. NPC 개인 단위 변형 (DisasterSeedEvaluator) 와 별개.
//
// 5축 좌표 (CSOAD — PersonalityMatrix 정합):
//   C (Control)     자제력     0=충동/감정표출  1=절제/단답
//   S (Stability)   안정성     0=패닉/말더듬    1=평정심/논리
//   O (Openness)    개방성     0=권위/수직관계  1=호기심/수평관계
//   A (Agreeable)   우호성     0=이기적/기만적  1=이타적/정직
//   D (Directness)  직설성     0=신비로움/은유  1=명료함/사실
//
//   * 추가: EscalationModifier — Death Spiral Tier 전환 속도 보정
//     Range [0.5 ~ 2.0]  1.0=기본  >1=가속  <1=감속
//
// 4 권장 양식:
//   Despair    : 절망 / 자포자기  — C=0.2 S=0.2 O=0.3 A=0.3 D=0.5  Esc=0.8
//   Frenzy     : 광란 / 폭주     — C=0.1 S=0.1 O=0.7 A=0.2 D=0.9  Esc=1.5
//   ColdLogic  : 냉정 / 계산     — C=0.9 S=0.9 O=0.5 A=0.3 D=0.2  Esc=0.9
//   Paralysis  : 마비 / 정지     — C=0.5 S=0.5 O=0.2 A=0.5 D=0.2  Esc=0.7
//
// 연계:
//   - PersonalityMatrixSO       : 동일 5축 좌표계 — Leader 5축 ↔ archetype 거리 매핑
//   - PersonalityTagRuleSO      : Bucket (Low/Mid/High) 정합
//   - GroupAIManager.Escalation : escalationModifier 곱 보정 (W5-B6 + D-10)
//   - SpeechTemplateSO          : onCollapseSpeechId ↔ triggerId 매칭 (D-08)
//   - WorldAISpawnManager       : 5축 leader pick 자동 매핑 (D-08+)
//
// 명명 변경 이력 (Wk5 D1):
//   - DisasterArchetypeSO → GroupArchetypeSO       (그룹 단위 명시)
//   - disasterArchetype → groupArchetype           (필드명)
//   - speechTriggerIdOnEnter → onCollapseSpeechId  (의도 직관 — Collapse 진입 시 발화)
//   - Disaster_* → GroupArchetype_*                (자산명)
//   - Mad/Calc/Stasis/SelfAbandon → Frenzy/ColdLogic/Paralysis/Despair (4 자산 측 표준 영어)
//   - .cs.meta 측 GUID 보존 — 기존 .asset 측 m_Script reference 자동 유지
// =============================================================================
using System;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/AI/GroupArchetype", fileName = "GroupArchetype_New")]
    public class GroupArchetypeSO : ScriptableObject
    {
        // ───────── 식별자 ─────────
        [Header("Identity")]
        [Tooltip("양식 식별 ID — 예: 'frenzy' / 'cold_logic' / 'despair' / 'paralysis'")]
        public string archetypeId;

        [Tooltip("Inspector 표시명 — 예: '광란' / '냉정' / '절망' / '마비'")]
        public string archetypeName;

        // ───────── 발화 연계 (D-08 본격 활성) ─────────
        [Header("Speech Trigger (SpeechTemplateSO 연계)")]
        [Tooltip("그룹의 Collapse 진입 시 발화 트리거 ID. SpeechTemplateSO.triggerId 와 매칭.\n" +
                 "예: 'group_archetype_frenzy_collapse', 'group_archetype_paralysis_collapse'.\n" +
                 "D-08 측 GroupAIManager.HandleTierChangeForSpeech → EventBus.RaiseSpeechTrigger.")]
        public string onCollapseSpeechId;

        // ───────── 5축 좌표 (CSOAD 정합) ─────────
        [Header("5축 좌표 (CSOAD — PersonalityMatrixSO 정합)")]

        [Range(0f, 1f)]
        [Tooltip("자제력 (Control): 0=충동/감정표출, 1=절제/단답")]
        public float control = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("안정성 (Stability): 0=패닉/말더듬, 1=평정심/논리")]
        public float stability = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("개방성 (Openness): 0=권위/수직관계, 1=호기심/수평관계")]
        public float openness = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("우호성 (Agreeable): 0=이기적/기만적, 1=이타적/정직")]
        public float agreeable = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("직설성 (Directness): 0=신비로움/은유, 1=명료함/사실")]
        public float directness = 0.5f;

        // ───────── 에스컬레이션 보정 ─────────
        [Header("Escalation Modifier")]
        [Range(0.5f, 2f)]
        [Tooltip("Death Spiral Tier 전환 속도 보정 — 1.0=기본, >1=가속, <1=감속")]
        public float escalationModifier = 1.0f;

        // ───────── 유틸리티 메서드 ─────────

        /// <summary>
        /// 5축 좌표를 배열로 반환 (C, S, O, A, D 순서).
        /// PersonalityMatrixSO 측 동일 순서 정합.
        /// </summary>
        public float[] GetCSOADVector()
        {
            return new[] { control, stability, openness, agreeable, directness };
        }

        /// <summary>
        /// 5축 좌표를 외부 좌표 (PersonalityMatrix 측 값) 와 거리 계산 (Euclidean).
        /// Spawn 시점 측 leader 5축 ↔ archetype 5축 매핑 측 사용.
        /// </summary>
        public float ComputeDistanceTo(float c, float s, float o, float a, float d)
        {
            float dc = control - c;
            float ds = stability - s;
            float doo = openness - o;
            float da = agreeable - a;
            float dd = directness - d;
            return Mathf.Sqrt(dc * dc + ds * ds + doo * doo + da * da + dd * dd);
        }

#if UNITY_EDITOR
        [ContextMenu("Print CSOAD")]
        private void PrintCSOAD()
        {
            Debug.Log($"[GroupArchetype:{archetypeId}] " +
                      $"C={control:F2} S={stability:F2} O={openness:F2} A={agreeable:F2} D={directness:F2}  " +
                      $"Esc={escalationModifier:F2}  onCollapseSpeechId='{onCollapseSpeechId}'");
        }

        [ContextMenu("Validate Speech Trigger")]
        private void ValidateSpeechTrigger()
        {
            if (string.IsNullOrWhiteSpace(onCollapseSpeechId))
                Debug.LogWarning($"[GroupArchetype:{archetypeId}] onCollapseSpeechId 비어있음.", this);
            else
                Debug.Log($"[GroupArchetype:{archetypeId}] onCollapseSpeechId='{onCollapseSpeechId}' " +
                          $"(SpeechTemplateSO.triggerId 측 동일 값 필요)");
        }
#endif
    }
}
