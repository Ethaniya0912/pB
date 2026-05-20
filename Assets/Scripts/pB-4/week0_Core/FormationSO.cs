// =============================================================================
// FormationSO.cs  |  pB-4 Project — Week 5
// Layer  : L1 Data (Core)
// Namespace: TDA.PB4.Data
//
// 역할:
//   대형 (Formation) 자산화 SO. 11 슬롯 영역 자료 / 위치 / 회전 영역 정합.
//   FormationSlotAllocator 측 본 SO 우선 참조 — 없으면 Inspector slots fallback.
//
// 11 슬롯 구조 (대칭):
//                       Vanguard
//      FrontLeft     FrontCenter     FrontRight
//          FlankLeft               FlankRight
//                        Pivot
//      RearLeft       RearCenter      RearRight
//                      Skirmisher
//
// 4 기본 대형 권장:
//   Formation_Default     — 균형 대형 (11 슬롯 모두 활성)
//   Formation_Defensive   — 방어 대형 (Vanguard 제외 / Pivot/Rear 강화)
//   Formation_Aggressive  — 공격 대형 (Vanguard + Front 3 강화 / Rear 축소)
//   Formation_Skirmish    — 산개 대형 (Skirmisher 3 + Flank 2 + Pivot)
//
// ★ 특이사항-A: enum 변경 breaking change
//   FormationSlotType 영역 — 기존 3 (Front/Flank/Rear) → 신규 11 슬롯 영역.
//   기존 enum 값 영역 SerializedField 측 정수 영역 저장 영역 — Unity 측 enum 인덱스
//   변경 시 데이터 어긋남 영역. 본 turn 측 정합 영역:
//     [Obsolete] Front=0, Flank=1, Rear=2 영역 → 신규 enum 측 동일 인덱스 영역 매핑 영역
//     Front → FrontCenter (인덱스 0 → 2)  ★ 인덱스 불일치
//     Flank → FlankLeft   (인덱스 1 → 4)
//     Rear  → RearCenter  (인덱스 2 → 8)
//   기존 .asset 측 slots 자료 영역 enum 값 영역 수동 재설정 필수.
//
// ★ 특이사항-B: 회전 (rotation) 자료 미반영
//   기존 FormationSlot 영역 — offset (Vector3) 만 영역. 회전 자료 부재 영역.
//   본 SO 측 facingAngle (float, degrees) 영역 신규 영역 — 슬롯 측 NPC 바라보기 방향 영역.
//   본 자료 영역 — FormationSlotAllocator 측 본격 사용 영역 부재 영역 (W5-B6 또는 별 의제 측).
//
// ★ 특이사항-C: maxOccupants 자료
//   각 슬롯 측 1 NPC 영역 기본. 본 SO 측 maxOccupants 영역 추가 영역 —
//   슬롯 측 2명 이상 영역 영역 영역 영역 영역 영역 (예: Vanguard 영역 1 / FrontCenter 영역 2).
//   본 turn 측 — Allocator 측 maxOccupants 영역 본격 사용 영역 부재 영역 (1 영역 fallback 영역).
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI;

namespace TDA.PB4.Data
{
    /// <summary>
    /// 대형 슬롯 정의 (FormationSO 측 자료).
    /// FormationSlotAllocator 측 FormationSlot 영역 SO 자료 영역 정합.
    /// </summary>
    [Serializable]
    public class FormationSlotData
    {
        [Tooltip("슬롯 유형 (11 종)")]
        public FormationSlotType slotType = FormationSlotType.FrontCenter;

        [Tooltip("대형 기준점 (formationCenter) 측 상대 오프셋 위치.\n" +
                 "+Z = 전방, +X = 우측, +Y = 수직 (보통 0).")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("슬롯 측 NPC 바라보기 방향 (degrees). 0 = 대형 정면.\n" +
                 "★ 특이사항-B: 본 turn 측 Allocator 본격 사용 부재 — 표시용.")]
        [Range(-180f, 180f)]
        public float facingAngle = 0f;

        [Tooltip("슬롯 측 최대 NPC 수. 기본 1.\n" +
                 "★ 특이사항-C: 본 turn 측 Allocator 본격 사용 부재 — 1 영역 fallback.")]
        [Range(1, 4)]
        public int maxOccupants = 1;
    }

    [CreateAssetMenu(menuName = "pB4/Group/Formation", fileName = "Formation_New")]
    public class FormationSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("대형 식별 ID — 예: 'default' / 'defensive' / 'aggressive' / 'skirmish'")]
        public string formationId;

        [Tooltip("Inspector 표시명 — 예: '균형' / '방어' / '공격' / '산개'")]
        public string formationName;

        [TextArea(2, 4)]
        [Tooltip("대형 설명 — 기획 메모용. 런타임 사용 부재.")]
        public string description;

        [Header("Slot Definitions")]
        [Tooltip("슬롯 정의 목록. 비어있으면 Allocator 측 GenerateDefaultSlots() fallback.")]
        public List<FormationSlotData> slots = new List<FormationSlotData>();

#if UNITY_EDITOR
        [ContextMenu("Print Formation Layout")]
        private void PrintLayout()
        {
            Debug.Log($"[Formation:{formationId}] '{formationName}' — {slots.Count} slots");
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                Debug.Log($"  [{i}] {s.slotType} offset={s.offset} face={s.facingAngle:F0}° max={s.maxOccupants}");
            }
        }
#endif
    }
}
