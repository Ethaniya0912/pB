using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstantCharacterEffect : ScriptableObject
{
    // 게임내 존재하는 모든 이펙트는 ID가 존재.
    // 네트워크에서 식별하기 편하게 하게 위함
    [Header("Effect ID")]
    public int instantEffectID;

    public virtual void ProcessEffect(CharacterManager character)
    {
        
    }

    private void CalculateStaminaDamage(CharacterManager character)
    {
        // 다른 플레이어의 이펙트와 모디파이어와 기본 스태미나 데미지를 비교
        // 값을 추가하거나 빼기 전 바꿈
        // sfx나 vfx 이펙트
    }
}
