using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterEffectsManager : MonoBehaviour
{
    CharacterManager character;
    // 인스턴트 이펙트(테이크 데미지, 힐)

    // 시간차 이펙트 (옥, 빌드 UPS)

    // 스테틱 이펙트 (장신구 버프 추가/제거 등)

    protected virtual void Awake()
    {
        character = GetComponent<CharacterManager>();
    }
        

    public virtual void ProcessInstantEffects(InstantCharacterEffect effect)
    {
        // 이펙트를 받기
        effect.ProcessEffect(character);
        // 처리 하기
    }
}
