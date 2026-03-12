using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events; // [신규 추가] ActionID 및 Funnel 아키텍처 연동을 위한 네임스페이스

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
        // 방향별 데미지 위치 체크 및 데미지 애니메이션 재생 (Funnel 라우팅)
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

        if (!playDamageAnimation) return;

        // 수동 애니메이션 선택 시 예외 처리 (레거시 문자열 해시 직접 재생)
        if (manuallySelectDamageAnimation)
        {
            character.characterAnimationManager.PlayTargetAnimation(Animator.StringToHash(damageAnimation), true);
            return; // 수동 재생 시 아래 방향별 로직 스킵
        }

        // [아키텍처 혁신: Funnel 라우팅] 
        // Hash List를 무작위로 뽑던 기존 방식을 버리고, 
        // 3방향(뒤, 좌, 우) 피격 시스템을 위한 ActionID로 명확히 라우팅합니다.
        ActionID staggerDirection = ActionID.Stagger_Backward; // 기본값 (정면/후면 피격 시 뒤로 밀림)

        // 공격자의 앵글을 계산하여 피격 방향 분기
        if (angleHitFrom >= 145 && angleHitFrom <= 180)
        {
            // 정면 피격 -> 뒤로 밀림
            staggerDirection = ActionID.Stagger_Backward;
        }
        else if (angleHitFrom <= -145 && angleHitFrom >= -180)
        {
            // 정면 피격 -> 뒤로 밀림
            staggerDirection = ActionID.Stagger_Backward;
        }
        else if (angleHitFrom >= -45 && angleHitFrom <= 45)
        {
            // 후면 피격 -> (현재 3방향 기획에 따라) 뒤로 밀리거나 공용 모션 사용
            staggerDirection = ActionID.Stagger_Backward;
        }
        else if (angleHitFrom >= -144 && angleHitFrom <= -45)
        {
            // 좌측 피격 -> 우측으로 밀림
            staggerDirection = ActionID.Stagger_Right;
        }
        else if (angleHitFrom >= 45 && angleHitFrom <= 144)
        {
            // 우측 피격 -> 좌측으로 밀림
            staggerDirection = ActionID.Stagger_Left;
        }

        // 포이즈가 깨졌다면 L4(Funnel)로 피격 신호 위임
        if (poiseIsBroken)
        {
            // ⭕ 시각적 실행은 L4 애니메이션 매니저의 onHit 깔때기로 '위임'합니다!
            // 이렇게 해야 현재 진행 중인 모든 공격을 파괴(Interrupt)하고 1순위로 밀려납니다.
            character.characterAnimationManager.PlayTargetHitReactionFunnel((int)staggerDirection);
        }
    }
}