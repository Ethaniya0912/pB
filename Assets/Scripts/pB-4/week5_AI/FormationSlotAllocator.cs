// =============================================================================
// FormationSlotAllocator.cs  |  pB-4 Project — Week 5
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   전투 대형 슬롯에 NPC를 배치할 때 적합도 점수를 산출한다.
//   각 슬롯에 대해 NPC의 역할, 성격, 신뢰도를 종합 평가하여
//   가장 적합한 NPC를 배정한다.
//
//   슬롯 적합도 수식:
//     S_slot = RoleMatch + PersonalityWeight + TrustBonus
//
// 11 슬롯 구조 (대칭):
//                       Vanguard
//      FrontLeft     FrontCenter     FrontRight
//          FlankLeft               FlankRight
//                        Pivot
//      RearLeft       RearCenter      RearRight
//                      Skirmisher
//
// 연계:
//   - GroupAIManager: 에스컬레이션 레벨에 따라 대형 재배치 요청
//   - PersonalityTagResolver: 성격 태그로 PersonalityWeight 결정
//   - TrustMatrix: 신뢰도로 TrustBonus 결정
//   - HumanoidAIBrain: 배정된 슬롯 위치로 이동 명령
//   - FormationSO (신규): 대형 자산화 — 우선 참조, 없으면 Inspector slots fallback
//
// =============================================================================
// [★ 변경 이력 — 2026-05-15: W5-B4 Phase 2 — 11 슬롯 확장]
// =============================================================================
// 변경:
//   - FormationSlotType enum: 3 → 11 (Vanguard/Front×3/Flank×2/Pivot/Rear×3/Skirmisher)
//   - FormationSO 우선 참조 추가 (Inspector slots fallback 유지)
//   - EvaluateSlotFitness: 11 슬롯 측 case 정합
//   - GenerateDefaultSlots: 5 → 11 슬롯 측 기본 영역
//
// ★ 특이사항-A: enum 영역 breaking change
//   Unity 측 enum 영역 정수 인덱스 영역 저장 영역. 기존 3 슬롯 .asset / Prefab 측
//   slotType 자료 영역 무효 영역:
//     기존 Front (0) → 신규 Vanguard (0)   ★ 의미 다름
//     기존 Flank (1) → 신규 FrontLeft (1)  ★ 의미 다름
//     기존 Rear  (2) → 신규 FrontCenter (2) ★ 의미 다름
//   기존 .asset / Prefab 측 slots 자료 영역 수동 재설정 필수.
//   본 patch 측 GenerateDefaultSlots() 호출 시 11 슬롯 영역 자동 생성 영역.
//
// ★ 특이사항-B: HandleCover / HandleSplitFire 통합 부재
//   기획 측 GroupAIManager 측 HandleCover / HandleSplitFire 영역 — 슬롯 자료
//   본격 활용 영역. 본 turn 측 부재 — W5-B6 (Escalation) 또는 별 의제 측.
//
// ★ 특이사항-C: 신규 태그 측 mapping 부재
//   본 turn 측 11 슬롯 측 personality tag 매핑 영역 — 기존 7 tag 영역 (Brave/Stoic/
//   Impulsive/Explorer/Reckless/Cautious/Coward) 영역 정합. 신규 슬롯 측 (Pivot/
//   Skirmisher 등) 영역 — 기존 태그 영역 조합 영역 정합. 신규 태그 (Leader/Hunter/
//   Healer 등) 시 본 영역 측 case 영역 갱신 필요.
//
// ★ 특이사항-D: 탐욕 알고리즘 영역 한계
//   AllocateFormation 측 탐욕 알고리즘 영역 — 11 슬롯 측 본격 영역 영역 최적 영역
//   부재 (NP-hard 영역). 본 turn 측 — 기존 영역 유지. 본격 영역 — 헝가리안
//   알고리즘 영역 정합 영역 (Wk6+ 또는 별 의제).
//
// 신규 검증:
//   V-B4-01: 11 슬롯 enum 컴파일 통과 / Inspector 노출
//   V-B4-02: FormationSO 우선 참조 / Inspector slots fallback 동작
//   V-B4-03: GenerateDefaultSlots() 측 11 슬롯 자동 생성
//   V-B4-04: EvaluateSlotFitness 측 11 슬롯 case 모두 점수 산출
//   V-B4-05: 기존 5 slot (3 enum) 측 자료 영역 깨짐 영역 정합 (breaking change 인지)
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 대형 슬롯 유형 — 11 슬롯 (Phase 2 확장).
    /// ★ 특이사항-A: 기존 3 enum (Front/Flank/Rear) 영역 deprecated. 인덱스 다름.
    /// </summary>
    public enum FormationSlotType
    {
        /// <summary>[0] 최전방 단독 — 보스 어그로 / 탱커 1.</summary>
        Vanguard,

        /// <summary>[1] 전방 좌 — 근접 전투원.</summary>
        FrontLeft,
        /// <summary>[2] 전방 중 — 근접 전투원 (기존 Front 매핑 권장).</summary>
        FrontCenter,
        /// <summary>[3] 전방 우 — 근접 전투원.</summary>
        FrontRight,

        /// <summary>[4] 측면 좌 — 기습 / 우회 공격.</summary>
        FlankLeft,
        /// <summary>[5] 측면 우 — 기습 / 우회 공격.</summary>
        FlankRight,

        /// <summary>[6] 중앙 회전축 — 지휘관 / 안정 영역.</summary>
        Pivot,

        /// <summary>[7] 후방 좌 — 원거리 / 회피.</summary>
        RearLeft,
        /// <summary>[8] 후방 중 — 원거리 지원 / 힐러.</summary>
        RearCenter,
        /// <summary>[9] 후방 우 — 원거리 / 회피.</summary>
        RearRight,

        /// <summary>[10] 자유 기동 — 사냥꾼 / 외곽 정찰.</summary>
        Skirmisher
    }

    /// <summary>개별 슬롯의 월드 위치와 유형.</summary>
    [Serializable]
    public class FormationSlot
    {
        [Tooltip("슬롯 유형 (11 종)")]
        public FormationSlotType slotType = FormationSlotType.FrontCenter;

        [Tooltip("대형 기준점으로부터의 상대적 오프셋 위치.")]
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
        [Header("━━━ 대형 SO (우선 참조) ━━━━━━━━━━━━━━━")]
        [Tooltip("FormationSO 자산. 할당되면 본 SO 측 slots 영역 우선 참조.\n" +
                 "비어있으면 Inspector slots 또는 GenerateDefaultSlots() fallback.\n" +
                 "★ FormationSO 영역 우선 — 종족별 / 시나리오별 대형 차별화 영역.")]
        [SerializeField] private FormationSO formationSO;

        [Header("━━━ 대형 슬롯 정의 (Fallback) ━━━━━━━━")]
        [Tooltip("대형의 슬롯 목록. FormationSO 부재 시 본 영역 참조.\n" +
                 "비어있으면 Awake()에서 기본 11 슬롯 자동 생성.")]
        [SerializeField] private List<FormationSlot> slots = new List<FormationSlot>();

        [Header("━━━ 적합도 가중치 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("역할 일치 보너스. GroupRole이 슬롯과 맞을 때 가산되는 점수.")]
        [Range(0f, 5f)]
        public float roleMatchBonus = 2.0f;

        [Tooltip("성격 태그 일치 보너스.")]
        [Range(0f, 3f)]
        public float personalityBonus = 1.5f;

        [Tooltip("높은 신뢰도(80+) 보너스. 전방 슬롯에 적용.")]
        [Range(0f, 3f)]
        public float highTrustBonus = 1.0f;

        [Tooltip("낮은 신뢰도(<40) 후방 보너스. 불신 NPC 측 후방 자동 영역.")]
        [Range(0f, 5f)]
        public float lowTrustRearBonus = 3.0f;

        [Header("━━━ 대형 기준점 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("대형의 중심점 Transform. 보통 플레이어 또는 리더 NPC.")]
        public Transform formationCenter;

        [Header("━━━ 참가 NPC 목록 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("대형에 참가하는 NPC 목록. Inspector 드래그 또는 RegisterNPC() 런타임.")]
        [SerializeField] private List<HumanoidAIBrain> participants = new List<HumanoidAIBrain>();

        [Header("━━━ 마지막 배치 결과 (Read Only) ━━━━━━")]
        [SerializeField] private List<SlotFitnessResult> lastResults = new List<SlotFitnessResult>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        private void Awake()
        {
            // ★ FormationSO 우선 참조 — 본 SO 측 자료 영역 slots 정합
            if (formationSO != null && formationSO.slots != null && formationSO.slots.Count > 0)
            {
                slots.Clear();
                foreach (var slotData in formationSO.slots)
                {
                    slots.Add(new FormationSlot
                    {
                        slotType = slotData.slotType,
                        offset = slotData.offset
                    });
                }
                if (debugLog)
                    Debug.Log($"[Formation] FormationSO '{formationSO.formationId}' 측 {slots.Count} 슬롯 로드.");
            }
            else if (slots.Count == 0)
            {
                GenerateDefaultSlots();
            }
        }

        /// <summary>
        /// 11 슬롯 기본 대형 자동 생성 — 대칭 배치.
        /// 좌표 기준: +Z=전방, +X=우측. 단위 — 미터.
        /// </summary>
        private void GenerateDefaultSlots()
        {
            // 최전방 (Vanguard)
            slots.Add(new FormationSlot { slotType = FormationSlotType.Vanguard,    offset = new Vector3(0f, 0f, 4f) });

            // 전방 3 (좌/중/우)
            slots.Add(new FormationSlot { slotType = FormationSlotType.FrontLeft,   offset = new Vector3(-2f, 0f, 2f) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.FrontCenter, offset = new Vector3(0f, 0f, 2f) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.FrontRight,  offset = new Vector3(2f, 0f, 2f) });

            // 측면 2 (좌/우)
            slots.Add(new FormationSlot { slotType = FormationSlotType.FlankLeft,   offset = new Vector3(-3.5f, 0f, 0f) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.FlankRight,  offset = new Vector3(3.5f, 0f, 0f) });

            // 중앙 회전축 (Pivot)
            slots.Add(new FormationSlot { slotType = FormationSlotType.Pivot,       offset = new Vector3(0f, 0f, 0f) });

            // 후방 3 (좌/중/우)
            slots.Add(new FormationSlot { slotType = FormationSlotType.RearLeft,    offset = new Vector3(-2f, 0f, -2f) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.RearCenter,  offset = new Vector3(0f, 0f, -2f) });
            slots.Add(new FormationSlot { slotType = FormationSlotType.RearRight,   offset = new Vector3(2f, 0f, -2f) });

            // 자유 기동 (Skirmisher) — 외곽
            slots.Add(new FormationSlot { slotType = FormationSlotType.Skirmisher,  offset = new Vector3(0f, 0f, -4f) });
        }

        // ==================================================================
        // 핵심 API: 대형 배치
        // ==================================================================

        /// <summary>
        /// 모든 참가 NPC를 슬롯에 최적 배치한다.
        /// ★ 특이사항-D: 탐욕 알고리즘 — 최적 부재 (Wk6+ 헝가리안 권장).
        /// </summary>
        public void AllocateFormation()
        {
            lastResults.Clear();
            foreach (var s in slots) s.assignedNPC = null;

            var unassigned = new List<HumanoidAIBrain>(participants);

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

        /// <summary>
        /// 개별 NPC-슬롯 적합도 평가 — 11 슬롯 case.
        /// ★ 특이사항-C: 기존 7 태그 (Brave/Stoic/Impulsive/Explorer/Reckless/Cautious/Coward)
        ///    영역 조합 정합. 신규 태그 추가 시 본 영역 갱신 필요.
        /// </summary>
        public float EvaluateSlotFitness(HumanoidAIBrain npc, FormationSlotType slotType)
        {
            float score = 0f;
            var tags = npc.GetComponent<PersonalityTagResolver>()?.ActiveTags;
            var trust = npc.GetComponent<TrustMatrix>();
            float trustVal = trust != null ? trust.CurrentTrust : 50f;

            switch (slotType)
            {
                // ───── 최전방 (Vanguard) — 탱커 ─────
                case FormationSlotType.Vanguard:
                    if (HasTag(tags, "Brave")) score += personalityBonus * 1.5f;
                    if (HasTag(tags, "Stoic")) score += personalityBonus * 1.2f;
                    if (HasTag(tags, "Reckless")) score += personalityBonus * 0.8f;
                    if (trustVal >= 80f) score += highTrustBonus * 1.5f;
                    score += roleMatchBonus * 1.2f;
                    break;

                // ───── 전방 3 (Front Left/Center/Right) ─────
                case FormationSlotType.FrontLeft:
                case FormationSlotType.FrontCenter:
                case FormationSlotType.FrontRight:
                    if (HasTag(tags, "Brave")) score += personalityBonus;
                    if (HasTag(tags, "Stoic")) score += personalityBonus * 0.8f;
                    if (trustVal >= 80f) score += highTrustBonus;
                    score += roleMatchBonus;
                    break;

                // ───── 측면 2 (Flank Left/Right) — 기동 ─────
                case FormationSlotType.FlankLeft:
                case FormationSlotType.FlankRight:
                    if (HasTag(tags, "Impulsive")) score += personalityBonus * 1.3f;
                    if (HasTag(tags, "Explorer")) score += personalityBonus * 0.7f;
                    if (HasTag(tags, "Reckless")) score += personalityBonus;
                    break;

                // ───── 중앙 (Pivot) — 안정 ─────
                case FormationSlotType.Pivot:
                    if (HasTag(tags, "Stoic")) score += personalityBonus * 1.5f;
                    if (HasTag(tags, "Cautious")) score += personalityBonus * 0.8f;
                    if (trustVal >= 70f) score += highTrustBonus * 1.2f;
                    score += roleMatchBonus * 0.8f;
                    break;

                // ───── 후방 3 (Rear Left/Center/Right) ─────
                case FormationSlotType.RearLeft:
                case FormationSlotType.RearCenter:
                case FormationSlotType.RearRight:
                    if (HasTag(tags, "Cautious")) score += personalityBonus * 1.3f;
                    if (HasTag(tags, "Coward")) score += personalityBonus;
                    if (trustVal < 40f) score += lowTrustRearBonus;
                    break;

                // ───── 자유 기동 (Skirmisher) — 정찰 ─────
                case FormationSlotType.Skirmisher:
                    if (HasTag(tags, "Explorer")) score += personalityBonus * 1.5f;
                    if (HasTag(tags, "Impulsive")) score += personalityBonus;
                    if (HasTag(tags, "Reckless")) score += personalityBonus * 0.6f;
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

#if UNITY_EDITOR
        [ContextMenu("Print Formation State")]
        private void PrintState()
        {
            Debug.Log($"[Formation] SO='{(formationSO != null ? formationSO.formationId : "(null)")}' " +
                      $"slots={slots.Count} participants={participants.Count}");
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                string occ = s.assignedNPC != null ? s.assignedNPC.name : "(empty)";
                Debug.Log($"  [{i}] {s.slotType} offset={s.offset} \u2190 {occ} (fit={s.fitnessScore:F2})");
            }
        }

        [ContextMenu("Force Allocate")]
        private void ForceAllocate() => AllocateFormation();
#endif
    }
}
