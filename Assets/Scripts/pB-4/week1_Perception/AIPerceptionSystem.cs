// =============================================================================
// AIPerceptionSystem.cs  |  pB×pC 통합 — Week 1 (pC 인지)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI.Perception
//
// 역할:
//   AI의 시각/청각/자취 인지를 통합 관리한다.
//   AwarenessState 4단계: Unaware → Suspicious → Alert → Combat
//
//   3분기 인지:
//     Case A(강제 조우): ScriptedEncounterTrigger가 직접 Combat 설정
//     Case B(자취 추적): FootprintTrail 발견 → Suspicious → 추적 → 시야 확인 → Combat
//     Case C(영속 추적): PersistentHuntDirector가 isPersistentHunt=true 설정 → 포기 불가
//
//   FactionDetectionSFXManager와 연동:
//     Suspicious 진입 시 OnFactionDetectedPlayer 이벤트 발행
//     → FactionDetectionSFXManager가 Pattern 1/2 SFX 재생
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Mob;        // MobAIBrain → factionData
using TDA.PB4.AI.Perception;  // SoundEventEmitter
using TDA.PB4.Core;           // EventBus
using TDA.PB4.Core;
using TDA.PB4.AI;

namespace TDA.PB4.AI.Perception
{
    /// <summary>AI 각성 상태 4단계.</summary>
    public enum AwarenessState
    {
        /// <summary>인지 없음. 평화 상태.</summary>
        Unaware = 0,
        /// <summary>의심. 자취/소리 감지. 조사 행동.</summary>
        Suspicious = 1,
        /// <summary>경계. 시야 확인. 전투 준비.</summary>
        Alert = 2,
        /// <summary>전투. 타겟 확정. 공격/추적.</summary>
        Combat = 3
    }

    public class AIPerceptionSystem : MonoBehaviour
    {
        // ==================================================================
        // 시각 인지 설정
        // ==================================================================
        [Header("━━━ 시각 인지 (Vision) ━━━━━━━━━━━━━━━")]
        [Tooltip("시각 인지 범위 (미터). 이 범위 내에서 시야각+벽차단 검사. " +
                 "20 = 20m. 기존 PatrolState.detectionRadius(15m)보다 넓게 설정 가능.")]
        [Range(5f, 50f)]
        public float visionRange = 20f;

        [Tooltip("시야각 (도). 전방 기준 좌우 절반씩. " +
                 "120 = 전방 120도 원뿔. 180 = 반구. 60 = 좁은 터널 시야.")]
        [Range(30f, 180f)]
        public float visionAngle = 120f;

        [Tooltip("시야 차단 레이어. 벽/지형만 포함. " +
                 "기존 PatrolState의 enviroLayers와 동일하게 설정.")]
        public LayerMask visionBlockLayer;

        [Tooltip("시각으로 감지할 대상 레이어. 플레이어 레이어 설정.")]
        public LayerMask targetLayer;

        // ==================================================================
        // 청각 인지 설정
        // ==================================================================
        [Header("━━━ 청각 인지 (Hearing) ━━━━━━━━━━━━━━")]
        [Tooltip("청각 인지 범위 (미터). SoundEvent의 volume×propagationRadius가 " +
                 "이 범위 이내이면 감지. 30 = 30m.")]
        [Range(5f, 60f)]
        public float hearingRange = 30f;

        [Tooltip("청각 민감도 (0~1). 소리 크기(volume)와 곱해져 감지 가능성 결정. " +
                 "1.0 = 모든 소리 감지. 0.3 = 큰 소리만 감지.")]
        [Range(0f, 1f)]
        public float hearingSensitivity = 0.5f;

        // ==================================================================
        // 자취 인지 설정
        // ==================================================================
        [Header("━━━ 자취 인지 (Trace) ━━━━━━━━━━━━━━━━")]
        [Tooltip("자취(FootprintTrail) 발견 범위 (미터). " +
                 "10 = 발자국 마커가 10m 이내에 있으면 발견.")]
        [Range(2f, 30f)]
        public float traceDetectionRange = 10f;

        // ==================================================================
        // 인지 상태
        // ==================================================================
        [Header("━━━ 현재 인지 상태 (Read Only) ━━━━━━━━")]
        [Tooltip("현재 각성 상태. Unaware(0)→Suspicious(1)→Alert(2)→Combat(3).")]
        [SerializeField] private AwarenessState awarenessLevel = AwarenessState.Unaware;

        [Tooltip("마지막으로 인지한 타겟 위치. 시야에서 사라지면 이 위치로 이동.")]
        [SerializeField] private Vector3 lastKnownPosition;

        [Tooltip("현재 추적 중인 타겟. Combat 상태에서만 유효.")]
        [SerializeField] private Transform currentPerceptionTarget;

        // ==================================================================
        // Case C: 영속 추적
        // ==================================================================
        [Header("━━━ 영속 추적 (Case C) ━━━━━━━━━━━━━━━")]
        [Tooltip("true이면 Suspicious→Unaware 감쇄가 비활성화됨. " +
                 "PersistentHuntDirector가 설정. 절대 포기하지 않음.")]
        public bool isPersistentHunt = false;

        [Tooltip("Case C: 자취를 잃은 후 나선형 수색 반경. " +
                 "20 = lastKnownPosition 주변 20m 나선형 수색.")]
        [Range(5f, 50f)]
        public float spiralSearchRadius = 20f;

        // ==================================================================
        // 감쇄 설정
        // ==================================================================
        [Header("━━━ 감쇄 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Suspicious 상태 유지 시간. 이 시간 내에 추가 자극이 없으면 Unaware로 복귀. " +
                 "isPersistentHunt=true이면 이 타이머는 작동하지 않음.")]
        [Range(3f, 30f)]
        public float suspicionDecayTime = 8f;

        [Tooltip("Alert→Suspicious 감쇄 시간. 타겟을 시야에서 놓친 후 이 시간 경과 시 Alert→Suspicious.")]
        [Range(3f, 20f)]
        public float alertDecayTime = 5f;

        // ==================================================================
        // 디버그
        // ==================================================================
        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Console에 인지 상태 변화를 출력.")]
        public bool debugLog = true;

        [Tooltip("Scene View에 시야 원뿔/청각 범위/자취 범위를 Gizmo로 표시.")]
        public bool showGizmos = true;

        // ==================================================================
        // 내부 상태
        // ==================================================================
        private float suspicionTimer;
        private float alertTimer;
        private float spiralAngle;
        private BaseAIBrain brain;

        // ==================================================================
        // 공개 프로퍼티
        // ==================================================================
        public AwarenessState CurrentAwareness => awarenessLevel;
        public Vector3 LastKnownPosition => lastKnownPosition;
        public Transform CurrentTarget => currentPerceptionTarget;

        private void Awake()
        {
            brain = GetComponent<BaseAIBrain>();
        }

        private void Update()
        {
            if (awarenessLevel == AwarenessState.Combat) return; // 전투 중에는 인지 불필요

            TickVision();
            TickTraceDetection();
            DecayAwareness();
        }

        // ==================================================================
        // 시각 인지: OverlapSphere + 시야각 + Linecast 벽차단
        // ==================================================================
        public void TickVision()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, targetLayer);
            foreach (var hit in hits)
            {
                Vector3 dirToTarget = (hit.transform.position - transform.position);
                dirToTarget.y = 0;
                float angle = Vector3.Angle(transform.forward, dirToTarget.normalized);

                if (angle > visionAngle * 0.5f) continue;

                // 벽 차단 검사
                Vector3 eyePos = transform.position + Vector3.up * 1.5f;
                Vector3 targetPos = hit.transform.position + Vector3.up * 1.0f;
                if (Physics.Linecast(eyePos, targetPos, visionBlockLayer)) continue;

                // 시야 확인 성공
                float distance = dirToTarget.magnitude;
                currentPerceptionTarget = hit.transform;
                lastKnownPosition = hit.transform.position;

                if (awarenessLevel < AwarenessState.Alert)
                    SetAwareness(AwarenessState.Alert, $"시야 확인 ({hit.name}, {distance:F1}m)");

                // Alert에서 일정 시간 경과 또는 근거리이면 Combat
                if (distance < visionRange * 0.5f || awarenessLevel == AwarenessState.Alert)
                {
                    SetAwareness(AwarenessState.Combat, $"전투 진입 ({hit.name}, {distance:F1}m)");
                    // pB-4 AI에 타겟 전달
                    if (brain != null)
                    {
                        var mob = brain as TDA.PB4.AI.Mob.MobAIBrain;
                        var hum = brain as TDA.PB4.AI.Humanoid.HumanoidAIBrain;
                        if (mob != null) mob.currentTarget = hit.transform;
                        if (hum != null) hum.currentTarget = hit.transform;
                    }

                    // 팩션 감지 SFX 이벤트 발행
                    RaiseFactionDetectionEvent(distance);
                }
                return;
            }

            // 시야에 타겟 없으면 lastKnownPosition 갱신
            if (awarenessLevel == AwarenessState.Alert && currentPerceptionTarget != null)
                lastKnownPosition = currentPerceptionTarget.position;
        }

        // ==================================================================
        // 청각 인지: EventBus.OnSoundEmitted 구독
        // ==================================================================
        public void OnSoundHeard(Vector3 soundPos, float volume)
        {
            float distance = Vector3.Distance(transform.position, soundPos);
            if (distance > hearingRange) return;

            float perception = volume * hearingSensitivity * (1f - distance / hearingRange);
            if (perception < 0.2f) return;

            lastKnownPosition = soundPos;

            if (awarenessLevel < AwarenessState.Suspicious)
                SetAwareness(AwarenessState.Suspicious, $"소리 감지 (vol={volume:F1}, dist={distance:F0}m, perc={perception:F2})");
        }

        // ==================================================================
        // 자취 인지: FootprintTrailSystem에서 주변 마커 검색
        // ==================================================================
        private void TickTraceDetection()
        {
            if (awarenessLevel >= AwarenessState.Alert) return;

            var trailSystem = FootprintTrailSystem.Instance;
            if (trailSystem == null) return;

            var nearest = trailSystem.GetNearestTrail(transform.position, traceDetectionRange);
            if (nearest == null) return;

            lastKnownPosition = nearest.Value;

            if (awarenessLevel < AwarenessState.Suspicious)
                SetAwareness(AwarenessState.Suspicious, $"자취 발견 ({Vector3.Distance(transform.position, nearest.Value):F1}m)");
        }

        // ==================================================================
        // 감쇄: 시간 경과 시 각성 레벨 감소
        // ==================================================================
        private void DecayAwareness()
        {
            if (isPersistentHunt) return; // Case C: 감쇄 비활성화

            if (awarenessLevel == AwarenessState.Suspicious)
            {
                suspicionTimer += Time.deltaTime;
                if (suspicionTimer >= suspicionDecayTime)
                {
                    SetAwareness(AwarenessState.Unaware, "의심 시간 초과 → Unaware");
                    suspicionTimer = 0f;
                }
            }
            else if (awarenessLevel == AwarenessState.Alert)
            {
                alertTimer += Time.deltaTime;
                if (alertTimer >= alertDecayTime)
                {
                    SetAwareness(AwarenessState.Suspicious, "경계 시간 초과 → Suspicious");
                    alertTimer = 0f;
                }
            }
        }

        // ==================================================================
        // 외부 API
        // ==================================================================

        /// <summary>외부에서 강제로 각성 상태를 설정 (Case A/C에서 사용).</summary>
        public void ForceSetAwareness(AwarenessState state, string reason = "외부 강제")
        {
            SetAwareness(state, reason);
        }

        /// <summary>외부에서 lastKnownPosition을 설정 (Case C에서 사용).</summary>
        public void SetLastKnownPosition(Vector3 pos)
        {
            lastKnownPosition = pos;
        }

        /// <summary>Case C: 나선형 수색 위치를 반환. lastKnownPosition 주변을 나선형으로 탐색.</summary>
        public Vector3 GetSpiralSearchPosition()
        {
            spiralAngle += Time.deltaTime * 30f; // 30도/초
            float radius = Mathf.Min(spiralAngle * 0.1f, spiralSearchRadius);
            float rad = spiralAngle * Mathf.Deg2Rad;
            return lastKnownPosition + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
        }

        // ==================================================================
        // 내부 헬퍼
        // ==================================================================
        private void SetAwareness(AwarenessState newState, string reason)
        {
            if (awarenessLevel == newState) return;
            var oldState = awarenessLevel;
            awarenessLevel = newState;
            suspicionTimer = 0f;
            alertTimer = 0f;

            if (debugLog)
                Debug.Log($"[Perception] {name}: {oldState}→{newState} ({reason})");
        }

        private void RaiseFactionDetectionEvent(float distance)
        {
            // ── [C-3 stub 완성] EventBus.OnFactionDetectedPlayer 실제 발행 ──
            // MobAIBrain.factionData 를 취득하여 이벤트와 함께 전달합니다.
            // FactionDetectionSFXManager 등이 이 이벤트를 구독하여 반응합니다.
            var mobBrain = GetComponent<MobAIBrain>();
            if (mobBrain != null)
            {
                // MobFactionDataSO factionData 는 private — GetFactionData() 래퍼 또는
                // MobAIBrain 에 public getter 를 추가하거나 아래처럼 Reflection 없이
                // EventBus 에 Transform 만 전달하는 오버로드를 사용합니다.
                EventBus.RaiseFactionDetectedPlayer(transform);

                if (debugLog)
                    Debug.Log($"[Perception] {name}: FactionDetected 이벤트 발행 (dist={distance:F1}m)");
            }
            else
            {
                // HumanoidAIBrain 또는 Brain 없는 AI
                EventBus.RaiseFactionDetectedPlayer(transform);

                if (debugLog)
                    Debug.Log($"[Perception] {name}: FactionDetected 이벤트 발행 (Brain 없음, dist={distance:F1}m)");
            }
        }

        // ==================================================================
        // Gizmo
        // ==================================================================
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            // 시야 범위 (노란색 원뿔)
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, visionRange);
            Vector3 fwd = transform.forward * visionRange;
            float halfAngle = visionAngle * 0.5f * Mathf.Deg2Rad;
            Vector3 leftRay = Quaternion.Euler(0, -visionAngle * 0.5f, 0) * fwd;
            Vector3 rightRay = Quaternion.Euler(0, visionAngle * 0.5f, 0) * fwd;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * 1.5f, leftRay);
            Gizmos.DrawRay(transform.position + Vector3.up * 1.5f, rightRay);

            // 청각 범위 (파란색)
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, hearingRange);

            // 자취 범위 (초록색)
            Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, traceDetectionRange);

            // lastKnownPosition (빨간 구체)
            if (awarenessLevel > AwarenessState.Unaware)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(lastKnownPosition, 0.5f);
                Gizmos.DrawLine(transform.position, lastKnownPosition);
            }

            // 나선형 수색 (Case C)
            if (isPersistentHunt && awarenessLevel == AwarenessState.Suspicious)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(lastKnownPosition, spiralSearchRadius);
            }
        }
#endif
    }
}