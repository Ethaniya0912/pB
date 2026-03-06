using System;
using System.Collections.Generic;
using UnityEngine;


namespace TDA.Character.Animation
{
    /// <summary>
    /// [P1] 유니티 인스펙터의 Dictionary 직렬화 한계를 피하기 위한 커스텀 구조체입니다.
    /// 논리적인 애니메이션 상태 이름과 해당 상태에서 재생할 실제 파라미터(1차 SO)를 1:1로 묶어줍니다.
    /// </summary>
    [Serializable]
    public struct AnimationSetMapping
    {
        [Tooltip("Animator Controller 내부에 정의된 논리적인 State 이름 (예: LightAttack_1)")]
        public string logicalStateName;


        [Tooltip("이 상태가 실행될 때 적용할 1차 모션 파라미터 SO (클립, 이벤트 타이밍 등 포함)")]
        public AnimationEventParamsSO paramsSO;
    }


    /// <summary>
    /// [P1] 특정 캐릭터(또는 무기)가 사용하는 전체 모션 세트를 통합 관리하는 2차 데이터 레지스트리(SO)입니다.
    ///
    /// [아키텍처 설계 의도]
    /// 무기가 추가될 때마다 Animator에 상태(Node)를 거미줄처럼 계속 추가하는 스파게티 구조를 원천 차단합니다.
    /// 기본 뼈대만 있는 공통 Animator를 사용하고, 런타임에 AnimatorOverrideController를 통해
    /// 이 SO에 담긴 데이터셋으로 알맹이만 동적으로 교체(Swapping)하는 완벽한 유연성을 제공합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAnimationSet", menuName = "TDA/Animation/Animation Set")]
    public class CharacterAnimationSetSO : ScriptableObject
    {
        [Header("Base Animator Settings")]
        [Tooltip("이 세트를 덮어씌울 기본 뼈대 애니메이터 컨트롤러입니다. (런타임 오버라이드 생성용)")]
        public RuntimeAnimatorController baseController;


        [Header("Animation Mappings")]
        [Tooltip("이 캐릭터/무기가 사용할 모든 액션 목록입니다. Inspector에서 자유롭게 배열을 늘려 관리합니다.")]
        public List<AnimationSetMapping> animationMappings = new List<AnimationSetMapping>();


        /// <summary>
        /// 외부 시스템(CombatManager 등)이 런타임에 특정 상태의 1차 파라미터 데이터를 요청할 때 사용하는 헬퍼 메서드입니다.
        /// </summary>
        /// <param name="stateName">찾고자 하는 논리적 상태 이름 (예: "HeavyAttack")</param>
        /// <returns>매핑된 AnimationEventParamsSO 데이터. (없을 경우 null 반환 전 경고 출력)</returns>
        public AnimationEventParamsSO GetParamsForState(string stateName)
        {
            // List를 순회하여 일치하는 매핑을 찾습니다. (데이터가 수십 개 수준이므로 O(N) 탐색 속도는 무의미한 수준으로 빠름)
            foreach (var mapping in animationMappings)
            {
                if (mapping.logicalStateName == stateName)
                {
                    return mapping.paramsSO;
                }
            }


            // [안전망 (Fail-safe) 로직]
            // 매핑이 누락되어 null이 반환되면 NGO 멀티플레이 환경에서 즉시 널 참조 에러(NullReferenceException)가 터지고,
            // 워핑이나 데미지 코루틴이 정지하여 돌이킬 수 없는 데스싱크(Desync)를 유발합니다.
            // 이를 방지하기 위해 개발자가 즉시 문제를 인지할 수 있도록 명확한 경고 로그를 남깁니다.
            Debug.LogWarning($"[CharacterAnimationSetSO] <color=yellow>{name}</color> 에셋에 <color=red>{stateName}</color>에 대한 매핑이 누락되었습니다! 인스펙터를 확인하세요.");

            return null;
        }
    }
}
