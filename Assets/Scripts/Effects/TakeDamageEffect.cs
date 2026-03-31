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
//
// 버그 수정 이력 (CodeReview v1.0):
//   [I-03 수정] isInvincible 체크 주석 해제 → IFrame 무적 정상 동작
//   [I-01 수정] ActivateCounterOpportunity() 호출 연결 → 카운터 기회 신호 전달
//   [I-02 수정] CheckCounterStagger() 구현 및 ProcessEffect() 호출 추가 → 패링 역경직 완성
//   [I-05 수정] PlayLightHitAnimation() ActionID → Hit_Light_* 전용 ID 사용
//   [I-07 수정] angleHitFrom 경계 데드존 해소 (-144 → -145f, 마지막 분기 else로 변경)
// =============================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;  // ActionID 및 Funnel 아키텍처 연동
using TDA.Character.AI; // AICharacterManager, AICharacterExecutionManager

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
        // [I-03 수정] 주석 해제 — IFrameEnable/Disable 이벤트와 CharacterManager.isInvincible 플래그가
        //             구현 완료되어 있으므로 실제 체크를 활성화합니다.
        if (character.isInvincible) return;

        // 데미지 계산
        CalculateDamage(character);

        // 방향별 데미지 위치 체크 및 데미지 애니메이션 재생 (Funnel 라우팅)
        PlayDirectionalBasedDamagedAnimation(character);

        // [I-02 수정] 패링 역경직 체크
        // character(피격자)의 패링 윈도우가 열려있으면 characterCausingDamage(공격자)에게 역경직 부여
        CheckCounterStagger(character);

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
        // ─── [DEBUG] 진입 확인 ────────────────────────────────────────────────
        Debug.Log($"[VFX-DEBUG 1] PlayDamageVFX 진입 — 캐릭터: {character.name}");

        // 1. 피 튀기는 이펙트
        character.characterEffectsManager.PlayBloodSplatterVFX(contactPoint);
        Debug.Log($"[VFX-DEBUG 2] PlayBloodSplatterVFX 호출 완료 — contactPoint: {contactPoint}");

        // 2. deflectDirection 계산
        Vector3 attackerPos = (characterCausingDamage != null)
            ? characterCausingDamage.transform.position
            : contactPoint - character.transform.forward;

        Vector3 deflectDirection = (character.transform.position - attackerPos).normalized;
        if (deflectDirection == Vector3.zero)
            deflectDirection = character.transform.forward;

        Debug.Log($"[VFX-DEBUG 3] deflectDir 계산 완료 — {deflectDirection}");

        // 3. CacheAttackerInfo
        character.characterEffectsManager.CacheAttackerInfo(contactPoint, attackerPos);
        Debug.Log($"[VFX-DEBUG 4] CacheAttackerInfo 완료");

        // 4. sparkType 결정
        AnimationEventType sparkType = poiseIsBroken
            ? AnimationEventType.VFX_HitSpark_Heavy
            : AnimationEventType.VFX_HitSpark_Default;

        Debug.Log($"[VFX-DEBUG 5] sparkType={sparkType} poiseIsBroken={poiseIsBroken}");

        // 5. EffectsManager 타입 확인
        Debug.Log($"[VFX-DEBUG 6] effectsManager 타입: {character.characterEffectsManager.GetType().Name}");

        // 6. PlayHitSparkVFX 호출
        character.characterEffectsManager.PlayHitSparkVFX(sparkType, contactPoint, deflectDirection);
        Debug.Log($"[VFX-DEBUG 7] PlayHitSparkVFX 호출 완료");
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
            {
                aiChar.OnPoiseBreak();

                // [I-01 수정] 공격자(플레이어)에게 카운터 기회 신호 전달
                // characterCausingDamage 가 PlayerCombatManager 를 가지고 있으면
                // ActivateCounterOpportunity() 를 호출하여 isCounterOpportunity 플래그를 활성화합니다.
                // PlayerManager.OnExecutionInputReceived() 의 처형 진입 게이트가 이 플래그를 참조합니다.
                if (characterCausingDamage != null)
                {
                    var attackerCombat = characterCausingDamage
                        .GetComponent<TDA.Character.PlayerCombatManager>();
                    attackerCombat?.ActivateCounterOpportunity();
                }
            }
        }
        else
        {
            // currentPoise가 이미 0 → 항상 경직
            poiseIsBroken = true;
        }

        // ⑤ 방향 계산 및 Funnel 라우팅
        // [아키텍처 혁신: Funnel 라우팅]
        // Hash List를 무작위로 뽑던 기존 방식을 버리고,
        // 4방향(앞, 뒤, 좌, 우) 피격 시스템을 위한 ActionID로 명확히 라우팅합니다.
        //
        // [방향별 이벤트 발행 추가]
        // ActionID 라우팅과 동시에 Hit_From_* AnimationEventType을 발행합니다.
        // 블러/VFX 팀원은 이 이벤트를 구독하여 어느 방향에서 맞았는지 판단합니다.
        // ActionID ↔ AnimationEventType 매핑:
        //   Stagger_Backward(100) ↔ Hit_From_Front(84)   : 정면 피격 → 뒤로 밀림
        //   Stagger_Left    (101) ↔ Hit_From_Right(85)   : 우측 피격 → 좌로 밀림
        //   Stagger_Right   (102) ↔ Hit_From_Left(86)    : 좌측 피격 → 우로 밀림
        //   Stagger_Forward (103) ↔ Hit_From_Behind(87)  : 후면 피격 → 앞으로 밀림

        ActionID staggerDirection = ActionID.Stagger_Backward;
        AnimationEventType hitDirectionEvent = AnimationEventType.Hit_From_Front;

        // 공격자의 앵글을 계산하여 피격 방향 분기
        if (angleHitFrom >= 145 && angleHitFrom <= 180)
        {
            // 정면 피격 → 뒤로 밀림
            staggerDirection = ActionID.Stagger_Backward;
            hitDirectionEvent = AnimationEventType.Hit_From_Front;
        }
        else if (angleHitFrom <= -145 && angleHitFrom >= -180)
        {
            // 정면 피격 → 뒤로 밀림
            staggerDirection = ActionID.Stagger_Backward;
            hitDirectionEvent = AnimationEventType.Hit_From_Front;
        }
        else if (angleHitFrom >= -45 && angleHitFrom <= 45)
        {
            // 후면 피격 → 앞으로 밀림
            staggerDirection = ActionID.Stagger_Forward;
            hitDirectionEvent = AnimationEventType.Hit_From_Behind;
        }
        // [I-07 수정] -144 → -145f 로 확장하여 -144.1~-144.9 데드존 해소
        else if (angleHitFrom >= -145f && angleHitFrom < -45f)
        {
            // 좌측 피격 → 우측으로 밀림
            staggerDirection = ActionID.Stagger_Right;
            hitDirectionEvent = AnimationEventType.Hit_From_Left;
        }
        // [I-07 수정] else if(45~144) → else 로 변경하여 144.1~144.9 데드존 흡수
        else
        {
            // 우측 피격 → 좌측으로 밀림 (45~144 및 경계 float 데드존 전부 포함)
            staggerDirection = ActionID.Stagger_Left;
            hitDirectionEvent = AnimationEventType.Hit_From_Right;
        }

        // 포이즈가 깨졌다면 L4(Funnel)로 피격 신호 위임
        if (poiseIsBroken)
        {
            // ⭕ 시각적 실행은 L4 애니메이션 매니저의 onHit 깔때기로 '위임'합니다!
            // 이렇게 해야 현재 진행 중인 모든 공격을 파괴(Interrupt)하고 1순위로 밀려납니다.
            character.characterAnimationManager.PlayTargetHitReactionFunnel((int)staggerDirection);

            // [신규] 방향 이벤트 발행 — 블러/VFX 팀원이 구독하여 방향별 연출 처리
            // CharacterVisualFeedbackManager, CharacterEffectsManager 등이 수신합니다.
            // 이벤트 발행은 Funnel 호출 직후 — 애니메이션 재생과 동프레임에 발생합니다.
            character.characterEventManager?.NotifyAnimationEvent(hitDirectionEvent);
        }
        else
        {
            // 포이즈 유지 → 경량 피격 모션 (Hit_* 계열)
            PlayLightHitAnimation(character);

            // [신규] 경량 피격에도 방향 이벤트 발행 (블러 강도는 구독자가 결정)
            character.characterEventManager?.NotifyAnimationEvent(hitDirectionEvent);
        }
    }

    // =========================================================================
    // PlayLightHitAnimation — 포이즈 유지 시 재생하는 경량 피격 모션
    // Hit_* ActionID를 사용하여 방향을 표현합니다. (Stagger 아닌 미세 피격)
    //
    // [I-05 수정] Attack_Right_01 / Attack_Left_01 임시 사용 → Hit_Light_* 전용 ActionID 로 교체
    //   기존: ActionID.Attack_Right_01 (공격 모션 ID, 임시)
    //   수정: ActionID.Hit_Light_Forward (경량 피격 전용 ID)
    //   Animator Controller: ActionState 파라미터에 Hit_Light_* State 연결 필요
    //
    // [I-07 수정] angleHitFrom 경계 데드존 해소 (-144f → -145f, 마지막 분기 else로 변경)
    // =========================================================================
    private void PlayLightHitAnimation(CharacterManager character)
    {
        ActionID hitDirection;

        if ((angleHitFrom >= 145f && angleHitFrom <= 180f) ||
            (angleHitFrom <= -145f && angleHitFrom >= -180f))
            hitDirection = ActionID.Hit_Light_Forward;   // [I-05] 정면 경량 피격 (기존: Attack_Right_01 임시)

        else if (angleHitFrom >= -45f && angleHitFrom <= 45f)
            hitDirection = ActionID.Hit_Light_Backward;  // [I-05] 후면 경량 피격 (기존: Attack_Left_01 임시)

        // [I-07] -144f → -145f 로 확장하여 float 경계 데드존 해소
        else if (angleHitFrom >= -145f && angleHitFrom < -45f)
            hitDirection = ActionID.Hit_Light_Left;      // [I-05] 좌측 경량 피격 (기존: Stagger_Right 임시)

        // [I-07] else if(45~144) → else 로 변경하여 144.1~144.9 데드존 흡수
        else
            hitDirection = ActionID.Hit_Light_Right;     // [I-05] 우측 경량 피격 (기존: Stagger_Left 임시)

        character.characterAnimationManager.PlayTargetHitReactionFunnel((int)hitDirection);
    }

    // =========================================================================
    // CheckCounterStagger [I-02 신규 구현]
    //
    // 역할:
    //   character(피격자)의 패링 윈도우(isParryActive)가 열려있으면
    //   패링 성공으로 판정하고 characterCausingDamage(공격자)에게 역경직을 부여합니다.
    //
    // 호출 시점:
    //   ProcessEffect() 에서 PlayDirectionalBasedDamagedAnimation() 직후 호출됩니다.
    //   poiseIsBroken 여부와 무관하게 독립적으로 실행됩니다.
    //
    // isParryActive 설정 경로:
    //   Parry_Window_Open(12) 이벤트 → PlayerCombatManager.OnAnimationEventReceived()
    //     → playerDefenseManager.OnParryWindowEvent(true)
    //       → CharacterDefenseManager.SetParryWindow(true)
    //         → isParryActive = true
    //
    // AI 패링도 동일한 경로를 따릅니다.
    //   AICharacterCombatManager.PerformParry() → StartDefense() + SetParryWindow(true)
    //
    // [아키텍처 규약]
    //   IsOwner 게이트는 character(피격자) 기준으로 수행합니다.
    //   역경직 부여 대상은 characterCausingDamage(공격자)입니다.
    // =========================================================================
    private void CheckCounterStagger(CharacterManager character)
    {
        if (!character.IsOwner) return;
        if (characterCausingDamage == null) return;

        // 피격자(character)의 패링 윈도우가 열려있는지 확인
        var defenderDefense = character.characterDefenseManager;
        if (defenderDefense == null || !defenderDefense.isParryActive) return;

        // 패링 성공 → 공격자(characterCausingDamage)에게 역경직 부여
        // L4 Funnel 경유 — Animator 직접 조작 금지
        characterCausingDamage.characterAnimationManager
            .PlayTargetHitReactionFunnel((int)ActionID.Stagger_Backward);

        // [L4] 역경직 이벤트 발행 — VFX/카메라 구독자가 자율 반응
        characterCausingDamage.characterEventManager?
            .NotifyAnimationEvent(AnimationEventType.Recoil_Trigger);

        Debug.Log($"[TakeDamageEffect] 패링 역경직 발동: {character.name} 패링 성공 → {characterCausingDamage.name} 역경직");
    }

    // =========================================================================
    // AI 처형 진행 중이면 경직을 무시합니다.
    // =========================================================================
    private bool ShouldIgnoreStaggerForExecution(CharacterManager character)
    {
        var execMgr = character.GetComponent<AICharacterExecutionManager>();
        if (execMgr == null) return false;
        return execMgr.ShouldIgnoreStaggerDuringExecution();
    }
}