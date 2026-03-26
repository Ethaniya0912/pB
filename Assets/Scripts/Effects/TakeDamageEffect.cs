// =============================================================================
// TakeDamageEffect.cs  |  TDA Project
// Layer  : L3 Domain — 피격 효과
//          (데미지 계산 / 포이즈 비교 / 방향별 경직 애니메이션 / SFX / VFX)
//
// 통합 이력:
//   [기존] 기존 필드 구조 완전 보존 (characterCausingDamage, elementDamage,
//          finalDamageDealt, playDamageAnimation, manuallySelectDamageAnimation,
//          willPlayDamageSFX, elementalDamageSoundFX, contactPoint)
//   [기존] PlayDamageSFX / PlayDamageVFX / IgnoreMyOwnCollider 완전 보존
//   [기존] PlayBloodSplatterVFX + PlayHitSparkVFX + lastContactPoint/AttackerPosition 캐싱
//   [기존] 방향별 Stagger_Backward/Left/Right Funnel 라우팅 (기존 각도 범위 그대로)
//   [기존] finalDamageDealt int 처리 / IsOwner 게이트
//   [신규] poiseIsBroken 무조건 → currentPoise 비교 로직으로 교체
//   [신규] AI 처형 진행 중 경직 무시 (ShouldIgnoreStaggerForExecution)
//   [신규] poiseDamage 필드 추가
// =============================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;  // ActionID 및 Funnel 아키텍처 연동
using TDA.Character.AI; // AICharacterManager, AIExecutionManager

[CreateAssetMenu(menuName = "Character Effects/Instant Effects/Take Damage")]
public class TakeDamageEffect : InstantCharacterEffect
{
    [Header("Character Causing Damage")]
    public CharacterManager characterCausingDamage;

    [Header("Damage")]
    public float physicalDamage; // 4가지 서브타입(기본, 둔기, 베기, 찌르기)
    public float elementDamage;

    [Header("Final Damage")]
    private int finalDamageDealt = 0; // 모든 데미지 합산.

    [Header("Poise")]
    public float poiseDamage = 0;
    public bool poiseIsBroken = false; // 캐릭터의 poise가 무너지면 스턴, 데미지 애니메이션 재생

    [Header("Animation")]
    public bool playDamageAnimation = true;
    public bool manuallySelectDamageAnimation = false;
    public string damageAnimation;

    [Header("Sound FX")]
    public bool willPlayDamageSFX = true;
    public AudioClip elementalDamageSoundFX; // 엘레멘탈 데미지가 존재 시 일반 SFX 위에 덧씌움.

    [Header("Directional Damage Taken From")]
    public float angleHitFrom;   // 어떤 데미지 애니메이션이 재생될지 정하기 (뒤로 휘청, 왼/오)
    public Vector3 contactPoint;   // 피 효과가 어디서 인스턴스될지 정함.

    // =========================================================================
    public override void ProcessEffect(CharacterManager character)
    {
        base.ProcessEffect(character);

        if (character.characterNetworkManager.isDead.Value)
            return;

        // 무적 상태 확인 (향후 IFrame 플래그 체크 추가)
        // if (character.isInvincible) return;

        // 데미지 계산
        CalculateDamage(character);

        // 방향별 데미지 위치 체크 및 데미지 애니메이션 재생 (Funnel 라우팅)
        PlayDirectionalBasedDamagedAnimation(character);

        // 빌드업 체크 (독, 출혈 등 — 향후 구현)
        // CheckBuildUpEffects(character);

        // 데미지 사운드 이펙트 재생
        PlayDamageSFX(character);

        // 데미지 VFX 재생 (출혈)
        PlayDamageVFX(character);

        // 캐릭터가 AI일 시, 데미지를 초래한 캐릭터가 존재 시 타게팅.
        // if (character is AICharacterManager ai && characterCausingDamage != null)
        //     ai.aiCharacterCombatManager.currentTarget = characterCausingDamage;
    }

    // =========================================================================
    private void CalculateDamage(CharacterManager character)
    {
        if (!character.IsOwner)
            return;

        if (characterCausingDamage != null)
        {
            // 데미지 모디파이어가 있는지 체크 후 베이스 데미지 조정 (물리/엘레멘트 데미지 버프)
            // 피지컬 *= 모디파이어.
        }

        // 캐릭터의 플랫 데미지를 체크한 이후, 데미지를 빼기.

        // 캐릭터 아머 흡수를 체크하고, 데미지 퍼센티지를 빼기.

        // 모든 데미지 타입을 합산하고, 파이널 데미지를 적용.
        finalDamageDealt = Mathf.RoundToInt(physicalDamage + elementDamage);

        if (finalDamageDealt <= 0)
        {
            finalDamageDealt = 1;
        }

        Debug.Log("Final Damage Given: " + finalDamageDealt);
        character.characterNetworkManager.currentHealth.Value -= finalDamageDealt;
    }

    // =========================================================================
    private void PlayDamageVFX(CharacterManager character)
    {
        // 불 데미지를 가졌다면 불 파티클 재생
        // 라이트닝 데미지, 라이트닝 파티클 등등...

        // 1. 기존 피 튀기는 이펙트 (필요 시 유지)
        character.characterEffectsManager.PlayBloodSplatterVFX(contactPoint);

        // 2. [NEW] 타격 지점에서 바깥으로 튕겨나가는 정확한 방향 벡터 계산
        // 타격 지점(contactPoint)에서 캐릭터의 중심점(position)을 빼면 캐릭터 바깥을 향하는 벡터가 나옵니다.
        Vector3 deflectDirection = (contactPoint - character.transform.position).normalized;

        // 만약 타격점이 캐릭터 중앙과 완벽히 겹쳐 벡터가 0이 될 경우를 대비한 안전 장치
        if (deflectDirection == Vector3.zero)
        {
            deflectDirection = character.transform.forward;
        }

        // 3. 방향 데이터를 담아 스파크 이펙트 실행
        character.characterEffectsManager.PlayHitSparkVFX(contactPoint, deflectDirection);

        // [핵심] 나중에 애니메이션 이벤트에서 쓰기 위해 타격 지점과 공격자 위치를 저장(캐싱)해 둠
        character.characterEffectsManager.lastContactPoint = contactPoint;
        character.characterEffectsManager.lastAttackerPosition =
            (characterCausingDamage != null)
            ? characterCausingDamage.transform.position
            : contactPoint - character.transform.forward;

        // onanimationreceived때문에 위에 있는 코드가 레거시화된 느낌이 있음.
        // (참고) 애니메이션을 기다리지 않고 즉시 스파크를 터뜨리고 싶다면 여기서 바로 호출하셔도 됩니다.
        // Vector3 deflectDir = (contactPoint - character.transform.position).normalized;
        // character.characterEffectsManager.PlayHitSparkVFX(contactPoint, deflectDir);
    }

    // =========================================================================
    private void PlayDamageSFX(CharacterManager character)
    {
        if (!willPlayDamageSFX) return;

        AudioClip physicalDamageSFX = WorldSoundFXManager.Instance
            .ChooseRandomSFXFromArray(WorldSoundFXManager.Instance.physicalDamageSFX);

        character.characterSoundFxManager.PlaySoundFX(physicalDamageSFX);

        // 엘레멘탈 데미지가 있으면 엘레멘탈 SFX 덧씌움
        if (elementDamage > 0f && elementalDamageSoundFX != null)
            character.characterSoundFxManager.PlaySoundFX(elementalDamageSoundFX);
    }

    // =========================================================================
    // PlayDirectionalBasedDamagedAnimation
    //
    //  흐름:
    //    ① IsOwner 게이트
    //    ② 수동 애니메이션 선택 레거시 경로
    //    ③ AI 처형 진행 중이면 경직 완전 무시 [신규]
    //    ④ isPoiseActive = true 이면 경량 피격 모션 [신규]
    //    ⑤ 포이즈 비교 → poiseIsBroken 결정 + currentPoise 소모 [신규]
    //    ⑥ poiseIsBroken = true  → 방향 기반 Stagger_* Funnel
    //       poiseIsBroken = false → 방향 기반 Hit_*    Funnel (경량)
    //
    //  방향 판정 (angleHitFrom 기준 — 기존 코드 각도 범위 그대로 유지):
    //    정면 피격  :  145~180 또는 -145~-180  → Stagger_Backward
    //    후면 피격  : -45~45                   → Stagger_Backward
    //    좌측 피격  : -144~-45                 → Stagger_Right  (우로 밀림)
    //    우측 피격  :  45~144                  → Stagger_Left   (좌로 밀림)
    // =========================================================================
    private void PlayDirectionalBasedDamagedAnimation(CharacterManager character)
    {
        if (!character.IsOwner)
            return;

        // ① 수동 애니메이션 선택 시 예외 처리 (레거시 문자열 해시 직접 재생)
        if (manuallySelectDamageAnimation && !string.IsNullOrEmpty(damageAnimation))
        {
            character.characterAnimationManager.PlayTargetAnimation(
                Animator.StringToHash(damageAnimation), true);
            return; // 수동 재생 시 아래 방향별 로직 스킵
        }

        if (!playDamageAnimation) return;

        // ② AI 처형 진행 중 → 경직 완전 무시 [신규]
        if (ShouldIgnoreStaggerForExecution(character))
        {
            poiseIsBroken = false;
            return;
        }

        // ③ isPoiseActive = true → 강공격/차지 중 → 경량 피격 모션 [신규]
        if (character.isPoiseActive)
        {
            poiseIsBroken = false;
            PlayLightHitAnimation(character);
            return;
        }

        // ④ 포이즈 비교 (기존: poiseIsBroken = true 무조건 → 조건부로 교체) [신규]
        float currentPoise = character.characterNetworkManager.currentPoise.Value;

        if (currentPoise > 0f)
        {
            poiseIsBroken = (poiseDamage >= currentPoise);

            // 포이즈 소모
            character.characterNetworkManager.currentPoise.Value =
                Mathf.Max(0f, currentPoise - poiseDamage);

            // 포이즈 파괴 시 AI에게 OnPoiseBreak 알림
            if (poiseIsBroken && character is AICharacterManager aiChar)
                aiChar.OnPoiseBreak();
        }
        else
        {
            // currentPoise가 이미 0 → 항상 경직
            poiseIsBroken = true;
        }

        // ⑤ 방향 계산 및 Funnel 라우팅
        // [아키텍처 혁신: Funnel 라우팅]
        // Hash List를 무작위로 뽑던 기존 방식을 버리고,
        // 3방향(뒤, 좌, 우) 피격 시스템을 위한 ActionID로 명확히 라우팅합니다.
        ActionID staggerDirection = ActionID.Stagger_Backward; // 기본값 (정면/후면 피격 시 뒤로 밀림)

        // 공격자의 앵글을 계산하여 피격 방향 분기
        if (angleHitFrom >= 145 && angleHitFrom <= 180)
        {
            // 정면 피격 → 뒤로 밀림
            staggerDirection = ActionID.Stagger_Backward;
        }
        else if (angleHitFrom <= -145 && angleHitFrom >= -180)
        {
            // 정면 피격 → 뒤로 밀림
            staggerDirection = ActionID.Stagger_Backward;
        }
        else if (angleHitFrom >= -45 && angleHitFrom <= 45)
        {
            // 후면 피격 → (현재 3방향 기획에 따라) 뒤로 밀리거나 공용 모션 사용
            staggerDirection = ActionID.Stagger_Backward;
        }
        else if (angleHitFrom >= -144 && angleHitFrom <= -45)
        {
            // 좌측 피격 → 우측으로 밀림
            staggerDirection = ActionID.Stagger_Right;
        }
        else if (angleHitFrom >= 45 && angleHitFrom <= 144)
        {
            // 우측 피격 → 좌측으로 밀림
            staggerDirection = ActionID.Stagger_Left;
        }

        // 포이즈가 깨졌다면 L4(Funnel)로 피격 신호 위임
        if (poiseIsBroken)
        {
            // ⭕ 시각적 실행은 L4 애니메이션 매니저의 onHit 깔때기로 '위임'합니다!
            // 이렇게 해야 현재 진행 중인 모든 공격을 파괴(Interrupt)하고 1순위로 밀려납니다.
            character.characterAnimationManager.PlayTargetHitReactionFunnel((int)staggerDirection);
        }
        else
        {
            // 포이즈 유지 → 경량 피격 모션 (Hit_* 계열)
            PlayLightHitAnimation(character);
        }
    }

    // =========================================================================
    // PlayLightHitAnimation — 포이즈 유지 시 재생하는 경량 피격 모션
    // Hit_* ActionID를 사용하여 방향을 표현합니다. (Stagger 아닌 미세 피격)
    // =========================================================================
    private void PlayLightHitAnimation(CharacterManager character)
    {
        ActionID hitDirection;

        if ((angleHitFrom >= 145f && angleHitFrom <= 180f) ||
            (angleHitFrom <= -145f && angleHitFrom >= -180f))
            hitDirection = ActionID.Attack_Right_01;  // 임시: 전용 Hit_Forward 추가 전

        else if (angleHitFrom >= -45f && angleHitFrom <= 45f)
            hitDirection = ActionID.Attack_Left_01;   // 임시: 전용 Hit_Backward 추가 전

        else if (angleHitFrom >= -144f && angleHitFrom <= -45f)
            hitDirection = ActionID.Stagger_Right;    // 좌측 피격 → 우로 소폭 반응

        else
            hitDirection = ActionID.Stagger_Left;     // 우측 피격 → 좌로 소폭 반응

        character.characterAnimationManager.PlayTargetHitReactionFunnel((int)hitDirection);
    }

    // =========================================================================
    // ShouldIgnoreStaggerForExecution [신규]
    // AI 처형 진행 중이면 경직을 무시합니다.
    // =========================================================================
    private bool ShouldIgnoreStaggerForExecution(CharacterManager character)
    {
        var execMgr = character.GetComponent<AIExecutionManager>();
        if (execMgr == null) return false;
        return execMgr.ShouldIgnoreStaggerDuringExecution();
    }
}