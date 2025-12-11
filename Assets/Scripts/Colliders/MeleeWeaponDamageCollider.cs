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
                ApplyAttackDamageModifiers(heavy_Attack_01_Modifier, damageEffect);
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

        //damageTarget.characterEffectsManager.ProcessInstantEffects(damageEffect);

        Debug.Log($"Attacker :  +  {characterCausingDamage.name}, IsOwner: { characterCausingDamage.IsOwner}");
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
