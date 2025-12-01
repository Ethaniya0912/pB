using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DamageCollider : MonoBehaviour
{
    [Header("Collider")]
    protected Collider damageCollider;
    [Header("Damage")]
    public float physicalDamage = 0;
    public float elementalDamage = 0;

    [Header("Character Damaged")]
    protected List<CharacterManager> characterDamaged = new List<CharacterManager>();

    [Header("Contact Point")]
    private Vector3 contactPoint;

    private void OnTriggerEnter(Collider other)
    {
        // 콜라이더에 접촉된 other의 캐릭터 컴포넌트를 가져온후 damageTarget 에 복사.
        CharacterManager damageTarget = other.GetComponent<CharacterManager>();

        if (damageTarget != null)
        {
            contactPoint = other.gameObject.GetComponent<Collider>().ClosestPointOnBounds(transform.position);

            // 데미지가 팀킬인지 체크

            // 타겟이 블럭 중인지 체크

            // 타겟이 무적인지 체크

            // 데미지
            DamageTarget(damageTarget);
        }
    }

    protected virtual void DamageTarget(CharacterManager damageTarget)
    {
        // 단일 공격시전시 사지에 여러 데미지를 받게 끔 하고 싶지 않음.
        // 데미지를 적용하기 전, 리스트에 추가하기.

        // 캐릭터리스트에 상대가 추가되어있으면 그냥 리턴.
        if (characterDamaged.Contains(damageTarget))
            return;

        characterDamaged.Add(damageTarget);

        TakeDamageEffect damageEffect = Instantiate(WorldCharacterEffectsManager.Instance.takeDamageEffect);
        damageEffect.physicalDamage = physicalDamage;
        damageEffect.elementDamage = elementalDamage;
        damageEffect.contactPoint = contactPoint;

        damageTarget.characterEffectsManager.ProcessInstantEffects(damageEffect);
    }

    public  virtual void EnableDamageCollider()
    {
        damageCollider.enabled = true;
    }

    public virtual void DisableDamageCollider()
    {
        damageCollider.enabled = false;
        characterDamaged.Clear(); // 콜라이더를 리셋시 맞은 캐릭터 리셋, 다시 가격가능.
    }

}
