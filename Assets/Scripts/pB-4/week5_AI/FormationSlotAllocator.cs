// =============================================================================
// FormationSlotAllocator.cs  |  pB-4 Project — Week 5
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   전투 대형 슬롯에 NPC를 배치할 때 적합도 점수를 산출한다.
//   각 슬롯(전방/측면/후방)에 대해 NPC의 역할, 성격, 신뢰도를 종합 평가하여
//   가장 적합한 NPC를 배정한다.
//
//   슬롯 적합도 수식:
//     S_slot = RoleMatch + PersonalityWeight + TrustBonus
//
//   전방 슬롯: Role:Tank=+2.0, Brave=+1.5, Trust>80=+1.0
//   측면 슬롯: Hasty/Impulsive=+2.0, Explorer=+1.0
//   후방 슬롯: Cautious=+2.0, Trust<40=+3.0 (불신하는 NPC는 후방으로)
//
// 연계:
//   - GroupAIManager: 에스컬레이션 레벨에 따라 대형 재배치 요청
//   - PersonalityTagResolver: 성격 태그로 PersonalityWeight 결정
//   - TrustMatrix: 신뢰도로 TrustBonus 결정
//   - HumanoidAIBrain: 배정된 슬롯 위치로 이동 명령
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 대형 슬롯 유형. 전방(탱크/근접)/측면(기습/기동)/후방(원거리/회피).
    /// </summary>
    public enum FormationSlotType
    {
        /// <summary>전방: 적과 가장 먼저 접촉. 탱크/근접 전투원 배치.</summary>
        Front,
        /// <summary>측면: 기습/우회 공격. 기동력 높은 NPC 배치.</summary>
        Flank,
        /// <summary>후방: 원거리 지원/회피. 신뢰도 낮은 NPC 자동 배치.</summary>
        Rear
    }

    /// <summary>개별 슬롯의 월드 위치와 유형.</summary>
    [Serializable]
    public class FormationSlot
    {
        [Tooltip("슬롯 유형 (Front/Flank/Rear)")]
        public FormationSlotType slotType = FormationSlotType.Front;

        [Tooltip("대형 기준점으로부터의 상대적 오프셋 위치. " +
                 "예: Front=(0,0,2), Flank=(3,0,0), Rear=(0,0,-2)")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("이 슬롯에 배정된 NPC (Read Only). Play 중 자동 할당.")]
        [HideInInspector] public HumanoidAIBrain assignedNPC;

        [Tooltip("이 슬롯의 적합도 점수 (Read Only).")]
        [HideInInspector] public float fitnessScore;
    }

    /// <summary>개별 NPC의 슬롯 적합도 평가 결과.</summary>
    [Serializable]
    public class SlotFitnessResult
    {
        public string npcName;
        public FormationSlotType slotType;
        public float roleMatch;
        public float personalityWeight;
        public float trustBonus;
        public float totalFitness;
    }

    public class FormationSlotAllocator : MonoBehaviour
    {
        [Header("━━━ 대형 슬롯 정의 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("대형의 슬롯 목록. 비어있으면 Awake()에서 기본 5슬롯(전방2/측면2/후방1) 자동 생성. " +
                 "수동으로 추가하여 커스텀 대형을 만들 수 있다.")]
        [SerializeField] private List<FormationSlot> slots = new List<FormationSlot>();

        [Header("━━━ 적합도 가중치 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("역할 일치 보너스. GroupRole이 슬롯과 맞을 때 가산되는 점수. " +
                 "2.0 = Striker가 Front 슬롯에 배치되면 +2.0점.")]
        [Range(0f, 5f)]
        public float roleMatchBonus = 2.0f;

        [Tooltip("성격 태그 일치 보너스. Brave가 Front에 배치되면 가산. " +
                 "1.5 = 용감한 NPC가 전방에 배치되면 +1.5점.")]
        [Range(0f, 3f)]
        public float personalityBonus = 1.5f;

        [Tooltip("높은 신뢰도(80+) 보너스. 전방 슬롯에 적용. " +
                 "1.0 = 신뢰도 80 이상 NPC가 전방에 배치되면 +1.0점.")]
        [Range(0f, 3f)]
        public float highTrustBonus = 1.0f;

        [Tooltip("낮은 신뢰도(<40) 후방 보너스. " +
                 "불신하는 NPC는 후방으로 자동 밀려남. " +
                 "3.0 = 불신 NPC의 후방 슬롯 적합도 +3.0.")]
        [Range(0f, 5f)]
        public float lowTrustRearBonus = 3.0f;

        [Header("━━━ 대형 기준점 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("대형의 중심점 Transform. 보통 플레이어 또는 리더 NPC. " +
                 "슬롯 위치는 이 Transform 기준 상대 좌표.")]
        public Transform formationCenter;

        [Header("━━━ 참가 NPC 목록 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("대형에 참가하는 NPC 목록. Inspector에서 드래그하거나 " +
                 "RegisterNPC()로 런타임 추가.")]
        [SerializeField] private List<HumanoidAIBrain> participants = new List<HumanoidAIBrain>();

        [Header("━━━ 마지막 배치 결과 (Read Only) ━━━━━━")]
        [SerializeField] private List<SlotFitnessResult> lastResults = new List<SlotFitnessResult>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        private void Awake()
        {
            if (slots.Count == 0) GenerateDefaultSlots();
        }

        private void GenerateDefaultSlots()
        {
            slots.Add(new FormationSlot { slotType = FormationSlotType.Front, offset = new Vector3(-1.5f, 0, 2f) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.Front, offset = new Vector3(1.5f, 0, 2f) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.Flank, offset = new Vector3(-3f, 0, 0) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.Flank, offset = new Vector3(3f, 0, 0) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.Rear, offset = new Vector3(0, 0, -2f) });
        }

        // ==================================================================
        // 핵심 API: 대형 배치
        // ==================================================================

        /// <summary>
        /// 모든 참가 NPC를 슬롯에 최적 배치한다.
        /// 각 NPC에 대해 모든 슬롯의 적합도를 계산하고, 탐욕 알고리즘으로 배정.
        /// </summary>
        public void AllocateFormation()
        {
            lastResults.Clear();
            foreach (var s in slots) s.assignedNPC = null;

            var unassigned = new List<HumanoidAIBrain>(participants);

            // 탐욕 알고리즘: 가장 높은 적합도 조합부터 배정
            for (int round = 0; round < slots.Count && unassigned.Count > 0; round++)
            {
                float bestScore = float.MinValue;
                int bestSlotIdx = -1;
                HumanoidAIBrain bestNPC = null;

                for (int si = 0; si < slots.Count; si++)
                {
                    if (slots[si].assignedNPC != null) continue;

                    foreach (var npc in unassigned)
                    {
                        float score = EvaluateSlotFitness(npc, slots[si].slotType);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSlotIdx = si;
                            bestNPC = npc;
                        }
                    }
                }

                if (bestSlotIdx >= 0 && bestNPC != null)
                {
                    slots[bestSlotIdx].assignedNPC = bestNPC;
                    slots[bestSlotIdx].fitnessScore = bestScore;
                    unassigned.Remove(bestNPC);

                    lastResults.Add(new SlotFitnessResult
                    {
                        npcName = bestNPC.name,
                        slotType = slots[bestSlotIdx].slotType,
                        totalFitness = bestScore
                    });

                    if (debugLog)
                        Debug.Log($"[Formation] {bestNPC.name} \u2192 {slots[bestSlotIdx].slotType} (fitness={bestScore:F2})");
                }
            }
        }

        /// <summary>개별 NPC-슬롯 적합도 평가.</summary>
        public float EvaluateSlotFitness(HumanoidAIBrain npc, FormationSlotType slotType)
        {
            float score = 0f;
            var tags = npc.GetComponent<PersonalityTagResolver>()?.ActiveTags;
            var trust = npc.GetComponent<TrustMatrix>();
            float trustVal = trust != null ? trust.CurrentTrust : 50f;

            switch (slotType)
            {
                case FormationSlotType.Front:
                    if (HasTag(tags, "Brave")) score += personalityBonus;
                    if (HasTag(tags, "Stoic")) score += personalityBonus * 0.8f;
                    if (trustVal >= 80f) score += highTrustBonus;
                    score += roleMatchBonus; // 기본 전방 보너스
                    break;

                case FormationSlotType.Flank:
                    if (HasTag(tags, "Impulsive")) score += personalityBonus * 1.3f;
                    if (HasTag(tags, "Explorer")) score += personalityBonus * 0.7f;
                    if (HasTag(tags, "Reckless")) score += personalityBonus;
                    break;

                case FormationSlotType.Rear:
                    if (HasTag(tags, "Cautious")) score += personalityBonus * 1.3f;
                    if (HasTag(tags, "Coward")) score += personalityBonus;
                    if (trustVal < 40f) score += lowTrustRearBonus;
                    break;
            }

            return score;
        }

        private bool HasTag(System.Collections.Generic.IReadOnlyList<string> tags, string tag)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++) if (tags[i] == tag) return true;
            return false;
        }

        public void RegisterNPC(HumanoidAIBrain npc) { if (!participants.Contains(npc)) participants.Add(npc); }
        public IReadOnlyList<FormationSlot> Slots => slots;
        public IReadOnlyList<SlotFitnessResult> LastResults => lastResults;
    }
}
