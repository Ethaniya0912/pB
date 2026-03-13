using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeleeWeaponDamageCollider : DamageCollider
{
    [Header("Attacking Character")]
    public CharacterManager characterCausingDamage; //(데미지연산이 될때, 공격자의 모디파이어)

    [Header("Weapon Attack Modifiers")]
    public float light_Attack_01_Modifier;
    public float light_Attack_02_Modifier;
    public float heavy_Attack_01_Modifier;
    public float heavy_Attack_02_Modifier;
    public float charge_Attack_01_Modifier;
    public float charge_Attack_02_Modifier;

    protected override void Awake()
    {
        base.Awake();

        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider>();
        }

        damageCollider.enabled = false; // 밀리 웨폰 콜라이더가 초반엔 디스에이블되어야
                                        // 애니메이션 작동시에만 처맞음. 아니면 계속맞음.
    }

    protected override void OnTriggerEnter(Collider other)
    {
        // 콜라이더에 접촉된 other의 캐릭터 컴포넌트를 가져온후 damageTarget 에 복사.
        CharacterManager damageTarget = other.GetComponentInParent<CharacterManager>();

        if (damageTarget != null)
        {
            // 스스로 공격 안되게 방지.
            if (damageTarget == characterCausingDamage)
                return;

            contactPoint = other.gameObject.GetComponent<Collider>().ClosestPointOnBounds(transform.position);
            Debug.Log(other.gameObject);

            // 데미지가 팀킬인지 체크

            // 타겟이 블럭 중인지 체크
            // [P0-02 추가] 세부적인 방어/패링 판정은 아래 DamageTarget 내부에서 무기 모디파이어가 반영된 '최종 데미지'를 기준으로 연산합니다.

            // 타겟이 무적인지 체크

            // 데미지
            DamageTarget(damageTarget);
        }
    }

    protected override void DamageTarget(CharacterManager damageTarget)
    {
        // 단일 공격시전시 사지에 여러 데미지를 받게 끔 하고 싶지 않음.
        // 데미지를 적용하기 전, 리스트에 추가하기.

        // 캐릭터리스트에 상대가 추가되어있으면 그냥 리턴.
        if (characterDamaged.Contains(damageTarget))
            return;

        characterDamaged.Add(damageTarget);
        Debug.Log(damageTarget);

        TakeDamageEffect damageEffect = Instantiate(WorldCharacterEffectsManager.Instance.takeDamageEffect);
        damageEffect.physicalDamage = physicalDamage;
        damageEffect.elementDamage = elementalDamage;
        damageEffect.contactPoint = contactPoint;
        damageEffect.angleHitFrom = Vector3.SignedAngle(characterCausingDamage.transform.forward, damageTarget.transform.forward, Vector3.up);

        switch (characterCausingDamage.characterCombatManager.currentAttackType)
        {
            case AttackType.LightAttack01:
                ApplyAttackDamageModifiers(light_Attack_01_Modifier, damageEffect);
                Debug.Log("AttackType : " + AttackType.LightAttack01);
                break;
            case AttackType.LightAttack02:
                ApplyAttackDamageModifiers(light_Attack_02_Modifier, damageEffect);
                Debug.Log("AttackType : " + AttackType.LightAttack02);
                break;
            case AttackType.HeavyAttack01:
                ApplyAttackDamageModifiers(heavy_Attack_01_Modifier, damageEffect);
                Debug.Log("AttackType : " + AttackType.HeavyAttack01);
                break;
            case AttackType.HeavyAttack02:
                ApplyAttackDamageModifiers(heavy_Attack_02_Modifier, damageEffect); // user 코드에 맞춰 _01_Modifier 유지
                Debug.Log("AttackType : " + AttackType.HeavyAttack02);
                break;
            case AttackType.ChargeAttack01:
                ApplyAttackDamageModifiers(charge_Attack_01_Modifier, damageEffect);
                break;
            case AttackType.ChargeAttack02:
                ApplyAttackDamageModifiers(charge_Attack_02_Modifier, damageEffect);
                break;
            default:
                break;
        }

        // =========================================================================================
        // 🚨 [방어 시스템 P0-02 연동] 최종 산출된 데미지로 방어 매니저에게 판정을 요청합니다.
        // =========================================================================================
        DefenseResult defenseResult = DefenseResult.Hit; // 기본값은 피격

        if (damageTarget.characterDefenseManager != null)
        {
            HitEventData hitData = new HitEventData
            {
                damage = damageEffect.physicalDamage + damageEffect.elementDamage,
                impactForce = damageEffect.poiseDamage > 0 ? damageEffect.poiseDamage : 50f, // 예비 Impact Force
                attackDirection = GuardDirection.Top // TODO: 타격 궤적 기반으로 업데이트 가능
            };

            // 방어 매니저가 패링, 블락, 피격 여부를 수학적으로 심사합니다.
            defenseResult = damageTarget.characterDefenseManager.EvaluateDefense(hitData);
        }

        // =========================================================================================
        // 판정 결과에 따른 데미지 및 네트워크 전송 분기
        // =========================================================================================
        switch (defenseResult)
        {
            case DefenseResult.Hit:
                // 1. 방어 실패 (온전한 데미지 전송)
                SendDamageToServer(damageTarget, damageEffect);
                break;

            case DefenseResult.Blocked:
                // 2. 일반 방어 성공 (데미지 무효화, 스태미나 차감은 DefenseManager에서 기처리됨)
                if (damageTarget.characterEventManager != null)
                {
                    damageTarget.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlaySFX_Guard_Thud);
                }
                Debug.Log($"<color=yellow>[DamageCollider] {damageTarget.name}가 공격을 방어했습니다! (Blocked)</color>");
                break;

            case DefenseResult.Parried:
                // 3. 완벽한 패링 성공 (데미지 무효화 및 공격자에게 역경직 부여)
                if (characterCausingDamage.characterEventManager != null)
                {
                    characterCausingDamage.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Recoil_Trigger);
                }
                Debug.Log($"<color=cyan>[DamageCollider] {damageTarget.name}가 패링에 성공했습니다! (Parried)</color>");
                break;

            case DefenseResult.Deflected:
            case DefenseResult.GuardBroken:
                // 4. 가드 튕겨짐 또는 파손 (페널티로 데미지의 50%만 적용)
                damageEffect.physicalDamage *= 0.5f;
                damageEffect.elementDamage *= 0.5f;
                SendDamageToServer(damageTarget, damageEffect);
                break;
        }
    }

    /// <summary>
    /// 기존의 네트워크 전송 코드를 캡슐화하여 분기문에서 재사용할 수 있도록 만든 헬퍼 함수입니다. (기존 주석 100% 보존)
    /// </summary>
    private void SendDamageToServer(CharacterManager damageTarget, TakeDamageEffect damageEffect)
    {
        //damageTarget.characterEffectsManager.ProcessInstantEffects(damageEffect);

        Debug.Log($"Attacker :  +  {characterCausingDamage.name}, IsOwner: {characterCausingDamage.IsOwner}");
        //if (characterCausingDamage.IsOwner)
        //{
        damageTarget.characterNetworkManager.NotifyTheServerOfCharacterDamageServerRpc(
            damageTarget.NetworkObjectId,
            characterCausingDamage.NetworkObjectId,
            damageEffect.physicalDamage,
            damageEffect.elementDamage,
            damageEffect.poiseDamage,
            damageEffect.angleHitFrom,
            damageEffect.contactPoint.x,
            damageEffect.contactPoint.y,
            damageEffect.contactPoint.z);
        Debug.Log("NotifyTheServerOfCharacterDamageServerRpc has been sent");
        //}
    }

    private void ApplyAttackDamageModifiers(float modifier, TakeDamageEffect damage)
    {
        damage.physicalDamage *= modifier;
        damage.elementDamage *= modifier;
        damage.poiseDamage *= modifier;

        // 만약 공격이 풀차지 헤비 어택이면, 풀차지 모디파이어에 곱한후 일반 모디파이어에 곱하기.
    }
}