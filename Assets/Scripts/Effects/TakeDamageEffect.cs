using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Character Effects/Instant Effects/Take Damage")]
public class TakeDamageEffect : InstantCharacterEffect
{
    [Header("Character Causing Damage")]
    public CharacterManager characterCausingDamage;

    [Header("Damage")]
    public float physicalDamage; // 4가지 섭타입(기본,둔기,베기,찌르기)
    public float elementDamage;

    [Header("Final Damage")]
    private int finalDamageDealt = 0; // 모든 데미지 합산.

    [Header("Poise")]
    public float poiseDamage = 0;
    public bool poiseIsBroken = false; // 캐릭터의 poise 가 무너지면 스턴, 데미지 애니메이션 재생

    [Header("Animation")]
    public bool playDamageAnimation = true;
    public bool manuallySelectDamageAnimation = false;
    public string damageAnimation;

    [Header("Sound FX")]
    public bool willPlayDamageSFX = true;
    public AudioClip elementalDamageSoundFX; // 엘레멘탈데미지가 존재시 일반SFX위에 덧씌움.

    [Header("Directional Damage Taken From")]
    public float angleHitFrom; // 어떤 데미지 애니메이션이 재생될지 정하기 ( 뒤로 휘청,왼/오)
    public Vector3 contactPoint; // 피 효과가 어디서 인스턴스될지 정함.

    public override void ProcessEffect(CharacterManager character)
    {
        base.ProcessEffect(character);

        if (character.characterNetworkManager.isDead.Value)
            return;

        // 무적상태확인

        // 데미지 계산
        CalculateDamage(character);
        // 방향별 데미지 위치 체크
        // 데미지 애니메이션 재생
        // 빌드업 체크(독, 출혈등)
        // 데미지 사운드 이펙트 재생
        // 데미지 vfx 재생(출혈)

        // 캐릭터가 ai 일 시, 데미지를 초래한 캐릭터가 존재시 타게팅.

    }

    private void CalculateDamage(CharacterManager character)
    {
        if (!character.IsOwner)
            return;

        if (characterCausingDamage != null)
        {
            // 데미지 모디파이어가 있는지 체크 후 베이스 데미지 조정(물리/엘레멘트 데미지 버프)
            // 피지컬 *= 모디파이어.
        }

        // 캐릭터의 플랫데미지를 체크한 이후, 데미지를 빼기.

        // 캐릭터 아머 흡수를 체크하고, 데미지 퍼센티지를 빼기.

        // 모든 데미지타입을 합산하고, 파이널 데미지를 적용.
        finalDamageDealt = Mathf.RoundToInt(physicalDamage + elementDamage);

        if (finalDamageDealt <= 0)
        {
            finalDamageDealt = 1;
        }

        character.characterNetworkManager.currentHealth.Value -= finalDamageDealt;
    }

}
