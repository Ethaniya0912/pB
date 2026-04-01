// =============================================================================
// JunctionLifecycleManager.cs  |  pB-4 Project — Week 6
// Layer  : L3 Domain (지형)
// Namespace: TDA.PB4.Terrain
//
// 역할:
//   HNGS 네러티브 정크션의 4단계 라이프사이클을 관리한다.
//   플레이어와의 거리에 따라 단계가 자동 전이된다.
//
//   4단계:
//     Dormant (초기): 정크션이 존재하지만 비활성. 아무 효과 없음.
//     Reservation (유저 100m 이내): 정크션 예약. 서사 패킷 준비.
//     Spawning (유저 30m 이내): 정크션 활성화. HNGS 방식으로 메쉬 미세조정 + 엔티티 스폰.
//     Fixation (사건 해결 후): WorldSaveData에 박제. 영구 고착.
//
// 연계:
//   - HNGSIntensityController: Spawning 단계에서 긴장도 가중
//   - DynamicFogController: Spawning 단계에서 안개 색상 오버라이드
//   - ProceduralSplineMesh: Spawning 단계에서 baseRadius 변조
// =============================================================================
using System;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Terrain
{
    public enum JunctionStage
    {
        /// <summary>비활성. 아무 효과 없음.</summary>
        Dormant,
        /// <summary>유저 100m 이내. 서사 패킷 준비.</summary>
        Reservation,
        /// <summary>유저 30m 이내. 메쉬 변조 + 엔티티 스폰.</summary>
        Spawning,
        /// <summary>사건 해결 후 영구 고착.</summary>
        Fixation
    }

    public class JunctionLifecycleManager : MonoBehaviour
    {
        [Header("━━━ 정크션 설정 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("정크션의 월드 위치. 이 위치를 기준으로 플레이어 거리를 측정.")]
        public Vector3 junctionPosition = Vector3.zero;

        [Tooltip("Dormant → Reservation 전이 거리. " +
                 "100 = 플레이어가 100m 이내에 진입하면 예약 단계.")]
        [Range(30f, 200f)]
        public float reservationDistance = 100f;

        [Tooltip("Reservation → Spawning 전이 거리. " +
                 "30 = 플레이어가 30m 이내에 진입하면 활성화.")]
        [Range(5f, 50f)]
        public float spawningDistance = 30f;

        [Header("━━━ Spawning 효과 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Spawning 단계에서 통로 너비 변조 계수 (0~1). " +
                 "0.5 = 통로가 원래의 50%로 수축. " +
                 "HNGSIntensityController의 shrink와 곱해져 복합 수축.")]
        [Range(0f, 1f)]
        public float widthFactor = 0.7f;

        [Tooltip("Spawning 단계에서 긴장도에 가산할 값. " +
                 "0.2 = 정크션 활성화 시 긴장도 +0.2.")]
        [Range(0f, 0.5f)]
        public float intensityBoost = 0.2f;

        [Header("━━━ 플레이어 참조 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("플레이어 Transform. 비어있으면 Camera.main.")]
        public Transform playerTransform;

        [Header("━━━ 현재 상태 (Read Only) ━━━━━━━━━━━━")]
        [SerializeField] private JunctionStage currentStage = JunctionStage.Dormant;
        [SerializeField] private float currentDistance;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        public JunctionStage CurrentStage => currentStage;
        public float CurrentDistance => currentDistance;

        private void Update()
        {
            if (currentStage == JunctionStage.Fixation) return;

            Vector3 playerPos = playerTransform != null ? playerTransform.position :
                               (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            currentDistance = Vector3.Distance(playerPos, junctionPosition);

            JunctionStage newStage = currentStage;
            if (currentDistance <= spawningDistance) newStage = JunctionStage.Spawning;
            else if (currentDistance <= reservationDistance) newStage = JunctionStage.Reservation;
            else newStage = JunctionStage.Dormant;

            if (newStage != currentStage)
            {
                JunctionStage prev = currentStage;
                currentStage = newStage;
                OnStageChanged(prev, newStage);
            }
        }

        private void OnStageChanged(JunctionStage prev, JunctionStage next)
        {
            if (debugLog)
                Debug.Log($"[Junction] {prev} → {next} (dist={currentDistance:F1}m)");

            if (next == JunctionStage.Spawning)
            {
                // 긴장도 가중
                var bb = GameBlackboard.Instance;
                if (bb != null)
                    bb.SetGlobalIntensity(Mathf.Clamp01(bb.GlobalIntensityScore + intensityBoost));
            }
        }

        /// <summary>사건 해결 후 Fixation 단계로 강제 전이. 월드 세이브에 기록.</summary>
        public void Fixate()
        {
            currentStage = JunctionStage.Fixation;
            if (debugLog) Debug.Log($"[Junction] Fixation! 영구 고착.");
        }
    }
}
