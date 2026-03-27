// =============================================================================
// BlurEventResponseSO.cs  |  TDA Project
// Layer  : L4 Data — 블러 팀원 전용 이벤트별 Pulse 설정 SO
//
// 설계 의도:
//   P2_ObjectMotionBlurController가 AnimationEventType을 수신했을 때
//   "얼마나 강하게, 얼마나 오래" 블러 Pulse를 발생시킬지를 정의하는 데이터 컨테이너입니다.
//
// 하드코딩 금지 원칙:
//   블러 강도/시간을 스크립트에 직접 쓰지 않습니다.
//   이 SO에서만 수치를 관리합니다.
//
// 사용 방법:
//   1. Project 우클릭 → Create → TDA/Camera/Blur Event Response → BlurEventResponse_SO 생성
//   2. P2_ObjectMotionBlurController Inspector → blurEventResponse 필드에 연결
//   3. pulseDefinitions 리스트에 이벤트 타입과 강도/시간 설정
//
// 블러 팀원 설정 가이드:
//   strength  : SO 기본 블러값에 더해질 가산치 (0~1). 클수록 강한 블러.
//   duration  : 블러가 최대 strength에 머무는 시간(초). 타격감의 "무게" 결정.
//   decayTime : 블러가 기본값으로 복귀하는 감쇠 시간(초). 클수록 여운이 길어짐.
//   blendIn   : 블러가 최대치까지 올라가는 시간(초). 0이면 즉시 스냅.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.Cameras
{
    /// <summary>
    /// [Phase1 신규] 이벤트 타입별 블러 Pulse 반응 설정 SO.
    /// P2_ObjectMotionBlurController.blurEventResponse 필드에 연결합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "BlurEventResponse_SO", menuName = "TDA/Camera/Blur Event Response")]
    public class BlurEventResponseSO : ScriptableObject
    {
        // =========================================================================================
        // 블러 Pulse 정의 구조체
        // =========================================================================================

        [Serializable]
        public struct BlurPulseDefinition
        {
            [Tooltip("이 Pulse를 발동시킬 AnimationEventType.\n" +
                     "Hit_Confirmed(88), Hit_From_Front(84) 등에서 선택하세요.")]
            public AnimationEventType eventType;

            [Tooltip("이 이벤트 수신 시 기본 블러값에 더해질 순간 가산치(0~1).\n" +
                     "0이면 이 이벤트에 블러 반응 없음.\n" +
                     "예: 경공격 0.15, 강공격/포이즈파괴 0.5")]
            [Range(0f, 1f)] public float strength;

            [Tooltip("블러가 최대 strength에 도달하는 시간(초).\n" +
                     "0이면 즉시 스냅. (예: 0.03f = 타격 즉시 치고 오름)")]
            [Range(0f, 0.3f)] public float blendIn;

            [Tooltip("블러가 최대 strength에 머무는 유지 시간(초).\n" +
                     "타격의 '무게감'을 결정합니다. (예: 경공격 0.05f, 강공격 0.12f)")]
            [Range(0f, 0.5f)] public float duration;

            [Tooltip("블러가 기본값(SO 기본 blur)으로 복귀하는 감쇠 시간(초).\n" +
                     "클수록 타격의 여운이 길어집니다. (예: 0.2f ~ 0.4f)")]
            [Range(0f, 2f)] public float decayTime;
        }

        // =========================================================================================
        // 인스펙터 필드
        // =========================================================================================

        [Header("이벤트별 블러 Pulse 설정 (블러 팀원 설정 영역)")]
        [Tooltip("각 AnimationEventType에 대응하는 블러 Pulse 정의 목록입니다.\n" +
                 "등록되지 않은 이벤트 타입은 블러 반응 없음으로 처리됩니다.")]
        public List<BlurPulseDefinition> pulseDefinitions = new List<BlurPulseDefinition>
        {
            // ── 가해자 측 타격 확인 이벤트 ────────────────────────────────────────
            new BlurPulseDefinition
            {
                eventType  = AnimationEventType.Hit_Confirmed,
                strength   = 0.15f,   // 경공격: 약한 Pulse
                blendIn    = 0.02f,
                duration   = 0.05f,
                decayTime  = 0.2f
            },
            new BlurPulseDefinition
            {
                eventType  = AnimationEventType.Hit_Confirmed_Heavy,
                strength   = 0.50f,   // 포이즈 파괴: 강한 Pulse
                blendIn    = 0.02f,
                duration   = 0.10f,
                decayTime  = 0.35f
            },

            // ── 피격자 측 방향별 이벤트 ──────────────────────────────────────────
            new BlurPulseDefinition
            {
                eventType  = AnimationEventType.Hit_From_Front,
                strength   = 0.30f,   // 정면 피격: 중간 Pulse
                blendIn    = 0.03f,
                duration   = 0.08f,
                decayTime  = 0.25f
            },
            new BlurPulseDefinition
            {
                eventType  = AnimationEventType.Hit_From_Behind,
                strength   = 0.40f,   // 후면 피격: 강한 Pulse (뒤통수)
                blendIn    = 0.03f,
                duration   = 0.08f,
                decayTime  = 0.30f
            },
            new BlurPulseDefinition
            {
                eventType  = AnimationEventType.Hit_From_Left,
                strength   = 0.25f,   // 좌측 피격
                blendIn    = 0.03f,
                duration   = 0.07f,
                decayTime  = 0.25f
            },
            new BlurPulseDefinition
            {
                eventType  = AnimationEventType.Hit_From_Right,
                strength   = 0.25f,   // 우측 피격
                blendIn    = 0.03f,
                duration   = 0.07f,
                decayTime  = 0.25f
            },
        };

        // =========================================================================================
        // 런타임 조회 (Dictionary 캐싱)
        // =========================================================================================

        private Dictionary<AnimationEventType, BlurPulseDefinition> _lookup;

        /// <summary>
        /// eventType에 대응하는 BlurPulseDefinition을 반환합니다.
        /// 등록되지 않은 이벤트 타입은 strength = 0인 기본값을 반환합니다.
        /// </summary>
        public BlurPulseDefinition GetPulse(AnimationEventType eventType)
        {
            BuildLookupIfNeeded();
            return _lookup.TryGetValue(eventType, out var def) ? def : default;
        }

        /// <summary>
        /// 해당 이벤트 타입에 대한 블러 반응이 등록되어 있는지 확인합니다.
        /// </summary>
        public bool HasResponse(AnimationEventType eventType)
        {
            BuildLookupIfNeeded();
            return _lookup.ContainsKey(eventType) && _lookup[eventType].strength > 0f;
        }

        private void BuildLookupIfNeeded()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<AnimationEventType, BlurPulseDefinition>();
            foreach (var def in pulseDefinitions)
            {
                // 중복 키 방어: 마지막으로 등록된 값이 우선
                _lookup[def.eventType] = def;
            }
        }

        // SO가 수정될 때마다 캐시 무효화
        private void OnValidate()
        {
            _lookup = null; // 다음 GetPulse 호출 시 재빌드

#if UNITY_EDITOR
            // 중복 이벤트 타입 경고
            var seen = new HashSet<AnimationEventType>();
            foreach (var def in pulseDefinitions)
            {
                if (!seen.Add(def.eventType))
                {
                    Debug.LogWarning(
                        $"[BlurEventResponseSO] '{name}': " +
                        $"이벤트 타입 <color=yellow>{def.eventType}</color>이 중복 등록되어 있습니다. " +
                        $"마지막 항목이 적용됩니다.");
                }
            }
#endif
        }
    }
}