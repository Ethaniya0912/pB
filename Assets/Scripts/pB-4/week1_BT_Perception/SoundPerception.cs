// =============================================================================
// SoundPerception.cs  |  pB-4 인지 모듈 — 청각
// 계층    : L3 Domain — AI 인지 전 레이어
// 네임스페이스: TDA.PB4.Perception
//
// 역할:
//   hearingRange 내에서 발생한 소리 이벤트를 감지하고,
//   가장 최근 소리 위치를 Blackboard(LastHeardPosition)에 기록합니다.
//   StalkAction이 Target 없을 때 이 위치를 수색 목적지로 사용합니다.
//
// 소리 소스:
//   - SoundEventEmitter: 발소리/무기 충돌/문 열림 등
//   - EventBus.OnSoundEmitted 이벤트 구독
//
// Blackboard 연동:
//   BehaviorGraphAgent.SetVariableValue("LastHeardPosition", position)
//
// 감쇠 모델:
//   소리 감도 = 1 - (거리 / hearingRange)
//   벽 관통: Physics.Linecast로 장애물 수 확인 → 감도 × wallPenaltyPerHit
// =============================================================================
using UnityEngine;

namespace TDA.PB4.Perception
{
    /// <summary>
    /// 청각 인지 모듈. AI 프리팹에 부착합니다.
    /// hearingRange 내 소리를 감지하여 Blackboard.LastHeardPosition에 기록합니다.
    /// </summary>
    public class SoundPerception : MonoBehaviour
    {
        // =====================================================================
        // Inspector 설정
        // =====================================================================

        [Header("━━━ 청각 설정 ━━━━━━━━━━━━━━━━━━━━")]

        /// <summary>소리 감지 최대 범위 (m).</summary>
        [Tooltip("이 범위 밖의 소리는 감지하지 않습니다")]
        [SerializeField] private float hearingRange = 30f;

        /// <summary>벽 1개당 소리 감도 감소 비율 (0~1).</summary>
        [Tooltip("벽이 있으면 감도가 감소합니다. 0.3 = 벽 1개당 30% 감소")]
        [SerializeField] private float wallPenaltyPerHit = 0.3f;

        /// <summary>소리 감지 최소 감도 (이하면 무시).</summary>
        [Tooltip("이 값 이하의 감도는 감지하지 않습니다")]
        [SerializeField] private float minSensitivity = 0.1f;

        /// <summary>소리 기억 유지 시간 (초). 이 시간 후 위치 리셋.</summary>
        [Tooltip("소리를 들은 후 이 시간 동안 위치를 기억합니다")]
        [SerializeField] private float soundMemoryDuration = 10f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool debugLog = false;

        // =====================================================================
        // 내부 상태
        // =====================================================================

        /// <summary>가장 최근 감지한 소리 위치.</summary>
        private Vector3 _lastHeardPosition;

        /// <summary>마지막 소리 감지 시각.</summary>
        private float _lastHeardTime;

        /// <summary>BehaviorGraphAgent 캐시. Blackboard 갱신에 사용.</summary>
        private Unity.Behavior.BehaviorGraphAgent _btAgent;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================

        private void Awake()
        {
            _btAgent = GetComponent<Unity.Behavior.BehaviorGraphAgent>();
        }

        private void OnEnable()
        {
            // SoundEventEmitter.OnSoundEmitted: Action<Vector3, SoundType, float>
            // SoundType 파라미터를 받아 OnSoundEvent(Vector3, float) 래퍼로 전달
            TDA.PB4.AI.Perception.SoundEventEmitter.OnSoundEmitted += HandleSoundEmitted;
        }

        private void OnDisable()
        {
            TDA.PB4.AI.Perception.SoundEventEmitter.OnSoundEmitted -= HandleSoundEmitted;
        }

        /// <summary>
        /// SoundEventEmitter.OnSoundEmitted 래퍼.
        /// SoundType 에 따라 volume 보정 후 OnSoundEvent 로 전달합니다.
        /// volume × baseRadius = 실제 전파 반경(sourceRadius).
        /// </summary>
        private void HandleSoundEmitted(Vector3 position, TDA.PB4.AI.Perception.SoundType type, float volume)
        {
            // SoundType 별 기본 반경 (SoundEventEmitter.baseRadius 기본=30f 기준)
            float baseRadius = type switch
            {
                TDA.PB4.AI.Perception.SoundType.Footstep => 15f,
                TDA.PB4.AI.Perception.SoundType.Combat => 40f,
                TDA.PB4.AI.Perception.SoundType.Mining => 25f,
                TDA.PB4.AI.Perception.SoundType.Explosion => 60f,
                TDA.PB4.AI.Perception.SoundType.Dialogue => 8f,
                _ => 20f
            };
            OnSoundEvent(position, volume * baseRadius);
        }

        private void Update()
        {
            // 소리 기억 만료 확인
            if (_lastHeardPosition != Vector3.zero &&
                Time.time - _lastHeardTime > soundMemoryDuration)
            {
                _lastHeardPosition = Vector3.zero;
                UpdateBlackboard(Vector3.zero);

                if (debugLog)
                    Debug.Log($"[SoundPerception] {name}: 소리 기억 만료. 위치 리셋.");
            }
        }

        // =====================================================================
        // 소리 이벤트 처리
        // =====================================================================

        /// <summary>
        /// 소리 이벤트 수신 콜백.
        /// EventBus.OnSoundEmitted 또는 SoundEventChannel에서 호출됩니다.
        /// </summary>
        /// <param name="sourcePosition">소리 발생 위치</param>
        /// <param name="sourceRadius">소리 도달 범위 (소리 크기)</param>
        public void OnSoundEvent(Vector3 sourcePosition, float sourceRadius)
        {
            float distance = Vector3.Distance(transform.position, sourcePosition);

            // 범위 밖이면 무시
            if (distance > hearingRange || distance > sourceRadius)
                return;

            // ── 감도 계산 ──
            float sensitivity = 1f - (distance / hearingRange);

            // 벽 관통 감쇠: Linecast로 장애물 수 확인
            int wallCount = CountWallsBetween(transform.position, sourcePosition);
            sensitivity *= Mathf.Pow(1f - wallPenaltyPerHit, wallCount);

            // 최소 감도 미만이면 무시
            if (sensitivity < minSensitivity)
                return;

            // ── 위치 기록 ──
            _lastHeardPosition = sourcePosition;
            _lastHeardTime = Time.time;
            UpdateBlackboard(sourcePosition);

            if (debugLog)
                Debug.Log($"[SoundPerception] {name}: 소리 감지! 위치={sourcePosition}, " +
                          $"거리={distance:F1}m, 감도={sensitivity:F2}, 벽={wallCount}");
        }

        // =====================================================================
        // 유틸리티
        // =====================================================================

        /// <summary>두 지점 사이의 벽(장애물) 수를 Raycast로 셉니다.</summary>
        private int CountWallsBetween(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float maxDist = direction.magnitude;
            RaycastHit[] hits = Physics.RaycastAll(from + Vector3.up, direction.normalized, maxDist);

            int wallCount = 0;
            foreach (var hit in hits)
            {
                // AI 자신과 다른 AI는 무시. 벽/환경만 카운트.
                if (hit.collider.isTrigger) continue;
                if (hit.collider.gameObject == gameObject) continue;
                wallCount++;
            }
            return wallCount;
        }

        /// <summary>Blackboard의 LastHeardPosition을 갱신합니다.</summary>
        private void UpdateBlackboard(Vector3 position)
        {
            if (_btAgent != null)
                _btAgent.SetVariableValue("LastHeardPosition", position);
        }

        // =====================================================================
        // Public API
        // =====================================================================

        /// <summary>현재 기억 중인 소리 위치. Vector3.zero이면 기억 없음.</summary>
        public Vector3 LastHeardPosition => _lastHeardPosition;

        /// <summary>마지막 소리 감지 이후 경과 시간.</summary>
        public float TimeSinceLastHeard => _lastHeardPosition != Vector3.zero
            ? Time.time - _lastHeardTime : float.MaxValue;
    }
}