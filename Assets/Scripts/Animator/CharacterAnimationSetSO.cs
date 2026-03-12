using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.Character.Animation
{
    [Serializable]
    public struct AnimationSetMapping
    {
        public string logicalStateName;
        public AnimationEventParamsSO paramsSO;
    }

    /// <summary>
    /// [P1] 전체 모션 세트를 통합 관리하는 2차 데이터 레지스트리(SO)
    /// (반드시 파일 이름이 CharacterAnimationSetSO.cs 여야 합니다)
    /// </summary>
    [CreateAssetMenu(fileName = "NewAnimationSet", menuName = "TDA/Animation/Animation Set")]
    public class CharacterAnimationSetSO : ScriptableObject
    {
        [Header("Base Animator Settings")]
        public RuntimeAnimatorController baseController;

        [Header("Animation Mappings")]
        public List<AnimationSetMapping> animationMappings = new List<AnimationSetMapping>();

        private Dictionary<string, AnimationEventParamsSO> _stateDictionary;

        public void InitializeRuntimeDictionary()
        {
            _stateDictionary = new Dictionary<string, AnimationEventParamsSO>();

            foreach (var mapping in animationMappings)
            {
                if (string.IsNullOrEmpty(mapping.logicalStateName) || mapping.paramsSO == null) continue;

                if (!_stateDictionary.ContainsKey(mapping.logicalStateName))
                {
                    _stateDictionary.Add(mapping.logicalStateName, mapping.paramsSO);
                }
            }
        }

        public AnimationEventParamsSO GetParamsForState(string stateName)
        {
            if (_stateDictionary == null)
            {
                InitializeRuntimeDictionary();
            }

            if (_stateDictionary.TryGetValue(stateName, out var paramSO))
            {
                return paramSO;
            }

            Debug.LogWarning($"[CharacterAnimationSetSO] {name} 에셋에 {stateName} 매핑이 누락되었습니다!");
            return null;
        }
    }
}