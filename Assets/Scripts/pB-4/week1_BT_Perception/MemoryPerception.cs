// =============================================================================
// MemoryPerception.cs  |  pB-4 인지 모듈 — 기억
// 계층    : L3 Domain — AI 인지 전 레이어
// 네임스페이스: TDA.PB4.Perception
//
// 역할:
//   타겟의 마지막 목격 위치를 기억합니다.
//   LOS(Line of Sight) 소실 후 memoryDuration(30초) 동안 유지.
//   시간 경과 시 기억이 소멸되고 Vector3.zero로 리셋됩니다.
//
// Blackboard 연동:
//   BehaviorGraphAgent.SetVariableValue("LastSeenPosition", position)
//   StalkAction이 Target LOS 소실 시 이 위치로 수색합니다.
//
// AIPerceptionSystem 연동:
//   AIPerceptionSystem이 AwarenessState.FullAlert일 때 → 위치 갱신
//   AwarenessState가 Unaware로 전환 시 → 기억 타이머 시작
// =============================================================================
using UnityEngine;

namespace TDA.PB4.Perception
{
    /// <summary>
    /// 기억 인지 모듈. AI 프리팹에 부착합니다.
    /// 타겟 최후 목격 위치를 기억하고, 시간 경과 후 소멸시킵니다.
    /// </summary>
    public class MemoryPerception : MonoBehaviour
    {
        // =====================================================================
        // Inspector 설정
        // =====================================================================

        [Header("━━━ 기억 설정 ━━━━━━━━━━━━━━━━━━━━")]

        /// <summary>기억 유지 시간 (초). 이 시간 후 마지막 목격 위치를 잊습니다.</summary>
        [Tooltip("타겟을 잃은 후 이 시간 동안 마지막 위치를 기억합니다")]
        [SerializeField] private float memoryDuration = 30f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool debugLog = false;

        // =====================================================================
        // 내부 상태
        // =====================================================================

        /// <summary>마지막 목격 위치. Vector3.zero이면 기억 없음.</summary>
        private Vector3 _lastSeenPosition;

        /// <summary>목격 소실 시각. 이 시점부터 memoryDuration 카운트.</summary>
        private float _lostSightTime;

        /// <summary>현재 타겟을 시야에 유지하고 있는지.</summary>
        private bool _hasLOS;

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
            // 시야 소실 상태에서 기억 만료 확인
            if (!_hasLOS && _lastSeenPosition != Vector3.zero)
            {
                if (Time.time - _lostSightTime > memoryDuration)
                {
                    _lastSeenPosition = Vector3.zero;
                    UpdateBlackboard(Vector3.zero);

                    if (debugLog)
                        Debug.Log($"[MemoryPerception] {name}: 기억 만료. {memoryDuration}초 경과.");
                }
            }
        }

        // =====================================================================
        // 외부 호출 API (AIPerceptionSystem에서 호출)
        // =====================================================================

        /// <summary>
        /// 타겟이 시야에 있을 때 매 프레임 호출합니다.
        /// 위치를 최신으로 갱신합니다.
        /// </summary>
        /// <param name="targetPosition">타겟의 현재 위치</param>
        public void UpdateSighting(Vector3 targetPosition)
        {
            _lastSeenPosition = targetPosition;
            _hasLOS = true;
            UpdateBlackboard(targetPosition);
        }

        /// <summary>
        /// 타겟이 시야에서 사라졌을 때 1회 호출합니다.
        /// 기억 타이머가 시작됩니다.
        /// </summary>
        public void OnLostSight()
        {
            if (!_hasLOS) return; // 이미 소실 상태

            _hasLOS = false;
            _lostSightTime = Time.time;

            if (debugLog)
                Debug.Log($"[MemoryPerception] {name}: 시야 소실. 마지막 위치={_lastSeenPosition}. " +
                          $"{memoryDuration}초 후 기억 만료.");
        }

        /// <summary>
        /// 기억을 강제로 소멸시킵니다.
        /// 기억 초기화가 필요한 특수 상황(사망/리스폰 등)에서 사용.
        /// </summary>
        public void ClearMemory()
        {
            _lastSeenPosition = Vector3.zero;
            _hasLOS = false;
            UpdateBlackboard(Vector3.zero);
        }

        // =====================================================================
        // 유틸리티
        // =====================================================================

        private void UpdateBlackboard(Vector3 position)
        {
            if (_btAgent != null)
                _btAgent.SetVariableValue("LastSeenPosition", position);
        }

        // =====================================================================
        // Public 읽기 전용
        // =====================================================================

        /// <summary>마지막 목격 위치. zero이면 기억 없음.</summary>
        public Vector3 LastSeenPosition => _lastSeenPosition;

        /// <summary>현재 타겟을 시야에 유지하고 있는지.</summary>
        public bool HasLineOfSight => _hasLOS;

        /// <summary>시야 소실 후 경과 시간. 시야 유지 중이면 0.</summary>
        public float TimeSinceLostSight => _hasLOS ? 0f : Time.time - _lostSightTime;

        /// <summary>기억이 유효한지 (소멸 전인지).</summary>
        public bool HasValidMemory => _lastSeenPosition != Vector3.zero;
    }
}
