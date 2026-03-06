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
        PlayDirectionalBasedDamagedAnimation(character);
        // 빌드업 체크(독, 출혈등)
        // 데미지 사운드 이펙트 재생
        PlayDamageSFX(character);
        // 데미지 vfx 재생(출혈)
        PlayDamageVFX(character);

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


        Debug.Log("Final Damage Given: " + finalDamageDealt);
        character.characterNetworkManager.currentHealth.Value -= finalDamageDealt;
    }

    private void PlayDamageVFX(CharacterManager character)
    {
        // 불 데미지를 가졌다면 불 파티클 재생
        // 라이트닝 데미지, 라이트닝 파티클 등등...

        character.characterEffectsManager.PlayBloodSplatterVFX(contactPoint);
    }

    private void PlayDamageSFX(CharacterManager character)
    {
        AudioClip physicalDamageSFX = WorldSoundFXManager.Instance.ChooseRandomSFXFromArray(WorldSoundFXManager.Instance.physicalDamageSFX);

        character.characterSoundFxManager.PlaySoundFX(physicalDamageSFX);

    }

    private void PlayDirectionalBasedDamagedAnimation(CharacterManager character)
    {
        if (!character.IsOwner)
            return;
        // TD : 포이즈가 부셔졌는지 계싼
        poiseIsBroken = true;

        // [에러 수정] 문자열 기반이 아닌 해시 기반(int) 로컬 변수 사용
        int damageAnimationHash = 0;

        // 공격자의 앵글을 계산
        if (angleHitFrom >= 145 && angleHitFrom <= 180)
        {
            // 정면 애니메이션 플레이
            damageAnimationHash = character.characterAnimationManager.GetRandomAnimationFromList(character.characterAnimationManager.forward_Medium_Damage_Hashes);
        }
        else if (angleHitFrom <= -145 && angleHitFrom >= -180)
        {
            // 정면 애니메이션 플레이
            damageAnimationHash = character.characterAnimationManager.GetRandomAnimationFromList(character.characterAnimationManager.forward_Medium_Damage_Hashes);
        }
        else if (angleHitFrom >= -45 && angleHitFrom <= 45)
        {
            // 후면 애니메이션 플레이
            damageAnimationHash = character.characterAnimationManager.GetRandomAnimationFromList(character.characterAnimationManager.backward_Medium_Damage_Hashes);
        }
        else if (angleHitFrom >= -144 && angleHitFrom <= -45)
        {
            // 좌측 애니메이션 플레이
            damageAnimationHash = character.characterAnimationManager.GetRandomAnimationFromList(character.characterAnimationManager.left_Medium_Damage_Hashes);
        }
        else if (angleHitFrom >= 45 && angleHitFrom <= 144)
        {
            // 우측 애니메이션 플레이
            damageAnimationHash = character.characterAnimationManager.GetRandomAnimationFromList(character.characterAnimationManager.right_Medium_Damage_Hashes);
        }

        // 수동 애니메이션 선택 시 예외 처리
        if (manuallySelectDamageAnimation)
        {
            damageAnimationHash = Animator.StringToHash(damageAnimation);
        }

        if (poiseIsBroken && damageAnimationHash != 0)
        {
            // [에러 수정] 바뀐 변수명(lastAnimationPlayedHash)과 데이터 타입(int) 반영
            character.characterAnimationManager.lastAnimationPlayedHash = damageAnimationHash;
            character.characterAnimationManager.PlayTargetAnimation(damageAnimationHash, true);
        }
    }
}