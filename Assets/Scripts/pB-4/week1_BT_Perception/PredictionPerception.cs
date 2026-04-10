// =============================================================================
// PredictionPerception.cs  |  pB-4 인지 모듈 — 예측
// 계층    : L3 Domain — AI 인지 전 레이어
// 네임스페이스: TDA.PB4.Perception
//
// 역할:
//   타겟의 현재 속도(velocity)를 기반으로 미래 위치를 예측합니다.
//   예측 위치 = 현재 위치 + velocity × predictionTime(1.5초)
//   NavMesh.SamplePosition으로 예측 위치를 NavMesh 위로 보정합니다.
//
// Blackboard 연동:
//   BehaviorGraphAgent.SetVariableValue("PredictedPosition", position)
//   StalkAction이 이 위치를 사용하여 타겟 이동 경로의 앞에서 기다립니다.
//
// 예측 품질:
//   타겟이 직선 이동 중 → 정확한 예측 (선행 차단 가능)
//   타겟이 방향 전환 중 → 부정확 (하지만 StalkAction의 fallback 있음)
//   타겟 정지 → 예측 위치 = 현재 위치 (변화 없음)
// =============================================================================
using UnityEngine;
using UnityEngine.AI;

namespace TDA.PB4.Perception
{
    /// <summary>
    /// 예측 인지 모듈. AI 프리팹에 부착합니다.
    /// 타겟의 미래 위치를 예측하여 Blackboard에 기록합니다.
    /// </summary>
    public class PredictionPerception : MonoBehaviour
    {
        // =====================================================================
        // Inspector 설정
        // =====================================================================

        [Header("━━━ 예측 설정 ━━━━━━━━━━━━━━━━━━━━")]

        /// <summary>예측 시간 (초). 이 시간 후의 위치를 예측합니다.</summary>
        [Tooltip("타겟이 현재 속도로 이 시간 동안 이동한 위치를 예측")]
        [SerializeField] private float predictionTime = 1.5f;

        /// <summary>예측 갱신 간격 (초). 매 프레임이 아닌 간격으로 갱신하여 성능 절약.</summary>
        [Tooltip("예측 재계산 간격. 0.2초면 초당 5회")]
        [SerializeField] private float updateInterval = 0.2f;

        /// <summary>NavMesh 보정 범위 (m). 예측 위치가 NavMesh 밖이면 이 범위에서 가장 가까운 위치.</summary>
        [Tooltip("예측 위치를 NavMesh 위로 보정하는 최대 범위")]
        [SerializeField] private float navMeshSampleRange = 5f;

        /// <summary>최소 속도. 이 속도 이하면 예측하지 않음 (정지 상태).</summary>
        [Tooltip("이 속도 이하면 타겟이 정지한 것으로 간주")]
        [SerializeField] private float minVelocityThreshold = 0.5f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool debugLog = false;
        [SerializeField] private bool drawGizmos = false;

        // =====================================================================
        // 내부 상태
        // =====================================================================

        /// <summary>예측된 미래 위치. 타겟 정지 시 Vector3.zero.</summary>
        private Vector3 _predictedPosition;

        /// <summary>타겟의 이전 프레임 위치 (속도 계산용).</summary>
        private Vector3 _previousTargetPosition;

        /// <summary>계산된 타겟 속도.</summary>
        private Vector3 _targetVelocity;

        /// <summary>갱신 타이머.</summary>
        private float _updateTimer;

        /// <summary>추적 중인 타겟.</summary>
        private Transform _target;

        private Unity.Behavior.BehaviorGraphAgent _btAgent;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================

        private void Awake()
        {
            _btAgent = GetComponent<Unity.Behavior.BehaviorGraphAgent>();
        }

        private void Update()
        {
            _updateTimer += Time.deltaTime;
            if (_updateTimer < updateInterval) return;
            _updateTimer = 0f;

            // ── 타겟 참조 갱신 ──
            // TODO: BehaviorGraphAgent에서 Target 변수를 읽는 방법 확인 필요
            // 현재는 외부에서 SetTarget()을 호출하는 방식
            if (_target == null)
            {
                if (_predictedPosition != Vector3.zero)
                {
                    _predictedPosition = Vector3.zero;
                    UpdateBlackboard(Vector3.zero);
                }
                return;
            }

            // ── 속도 계산 (위치 변화량 / 시간) ──
            Vector3 currentPos = _target.position;
            if (_previousTargetPosition != Vector3.zero)
            {
                _targetVelocity = (currentPos - _previousTargetPosition) / updateInterval;
            }
            _previousTargetPosition = currentPos;

            // ── 예측 ──
            if (_targetVelocity.magnitude < minVelocityThreshold)
            {
                // 타겟 정지 → 예측 불필요
                _predictedPosition = Vector3.zero;
                UpdateBlackboard(Vector3.zero);
                return;
            }

            // 미래 위치 = 현재 위치 + 속도 × 예측 시간
            Vector3 rawPrediction = currentPos + _targetVelocity * predictionTime;

            // NavMesh 보정: 예측 위치가 NavMesh 밖이면 가장 가까운 위치로
            if (NavMesh.SamplePosition(rawPrediction, out NavMeshHit hit, navMeshSampleRange, NavMesh.AllAreas))
            {
                _predictedPosition = hit.position;
            }
            else
            {
                // NavMesh 밖 → 현재 위치 사용 (예측 포기)
                _predictedPosition = currentPos;
            }

            UpdateBlackboard(_predictedPosition);

            if (debugLog)
                Debug.Log($"[PredictionPerception] {name}: vel={_targetVelocity.magnitude:F1}m/s " +
                          $"predicted={_predictedPosition} (in {predictionTime}s)");
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || _predictedPosition == Vector3.zero) return;

            // 예측 위치를 빨간 구로 표시
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_predictedPosition, 0.5f);

            // 현재 위치 → 예측 위치를 선으로 연결
            if (_target != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(_target.position, _predictedPosition);
            }
        }

        // =====================================================================
        // 유틸리티
        // =====================================================================

        private void UpdateBlackboard(Vector3 position)
        {
            if (_btAgent != null)
                _btAgent.SetVariableValue("PredictedPosition", position);
        }

        // =====================================================================
        // Public API
        // =====================================================================

        /// <summary>
        /// 추적 대상을 설정합니다.
        /// PB4DecisionAdapter 또는 AIPerceptionSystem에서 호출합니다.
        /// </summary>
        public void SetTarget(Transform target)
        {
            if (_target != target)
            {
                _target = target;
                _previousTargetPosition = target != null ? target.position : Vector3.zero;
                _targetVelocity = Vector3.zero;
            }
        }

        /// <summary>예측된 미래 위치. zero이면 예측 없음(타겟 정지 또는 없음).</summary>
        public Vector3 PredictedPosition => _predictedPosition;

        /// <summary>계산된 타겟 이동 속도 (m/s).</summary>
        public float TargetSpeed => _targetVelocity.magnitude;
    }
}
