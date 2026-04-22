using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Character; // [Phase3] HitConfirmedData

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

    // =========================================================================================
    // [P0-03 신규] 중복 타격 방지 리스트
    // 한 번의 무기 궤적(스윙) 동안 이미 데미지를 입힌 대상을 기억하여 다단 히트를 원천 차단합니다.
    // =========================================================================================
    protected List<CharacterManager> charactersDamagedThisSwing = new List<CharacterManager>();

    // =========================================================================================
    // [Fix-P0 신규] 디버그 토글
    // ─────────────────────────────────────────────────────────────────────────────────────────
    // · debugStartEnabled:
    //     Awake 시점에 콜라이더를 강제 Off 시키지 않고 인스펙터 상태를 그대로 유지합니다.
    //     파이프라인(애니메이션 이벤트 → CombatManager → 이 스크립트) 문제인지,
    //     콜라이더 자체의 물리 설정(Trigger/Layer/Matrix) 문제인지를 분리 검증할 때 사용합니다.
    //
    // · logColliderToggle:
    //     Enable/Disable 이 호출될 때마다 콘솔에 호출 시각과 스택 위치를 남깁니다.
    //     상위 CharacterCombatManager.logDamageColliderPipeline 과 조합해 전체 체인을 추적하세요.
    //
    // · logAwakeDiagnostic:
    //     Awake 시점 인스턴스 계층 경로 / damageCollider 타입을 출력합니다.
    //     "리스트에 엉뚱한 Collider 가 들어가 있는가?" 를 판별할 때 사용합니다.
    //
    // 프로덕션 빌드에서는 세 값 모두 false 로 두세요 (기본값).
    // 디버깅이 필요하면 런타임에 인스펙터에서 ON, 혹은 스크립트 상단에서 일괄 ON 하세요.
    // =========================================================================================
    [Header("Damage Collider Debug (Fix-P0)")]
    [Tooltip("켜면 Awake 시점 강제 Off 동작을 건너뛰어, 물리 자체 세팅을 검증할 수 있게 합니다.")]
    [SerializeField] private bool debugStartEnabled = false;
    [Tooltip("Enable/Disable 호출을 콘솔에 로그로 남깁니다. 디버깅 시에만 켜세요.")]
    [SerializeField] private bool logColliderToggle = false;
    [Tooltip("Awake 시점 인스턴스 정보(계층 경로, Collider 타입)를 로그로 남깁니다. 디버깅 시에만 켜세요.")]
    [SerializeField] private bool logAwakeDiagnostic = false;

    protected override void Awake()
    {
        base.Awake();

#if UNITY_EDITOR
        // [Fix-P0 진단] 인스턴스 계층/Collider 타입 식별 로그 — 디버깅 필요 시 인스펙터에서 ON.
        if (logAwakeDiagnostic)
        {
            Debug.Log($"<color=magenta>[MWD.Awake]</color> {gameObject.name}  " +
                      $"path={GetFullHierarchyPath()}  " +
                      $"damageCollider={(damageCollider != null ? damageCollider.GetType().Name : "NULL")}  " +
                      $"frame={Time.frameCount}");
        }
#endif

        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider>();
        }

        // =====================================================================================
        // [Fix-P0 수정] 기본 규약은 Awake 시점 강제 Off (원본 동작 100% 보존).
        // 단, debugStartEnabled == true 인 한해서만 인스펙터 상태를 유지합니다.
        // 이 분기는 파이프라인 고장과 물리 세팅 오류를 분리 검증하기 위한 "스위치" 입니다.
        // =====================================================================================
        if (!debugStartEnabled)
        {
            damageCollider.enabled = false; // 밀리 웨폰 콜라이더가 초반엔 디스에이블되어야
                                            // 애니메이션 작동시에만 처맞음. 아니면 계속맞음.
        }
    }

    // =========================================================================================
    // [P0-03] Type-Safe Enum 이벤트에 의해 호출되는 콜라이더 개폐 및 리셋 로직
    // =========================================================================================

    public override void EnableDamageCollider()
    {
        damageCollider.enabled = true;
        // 새로운 스윙 궤적이 시작될 때마다 기억 리셋 (중복 타격 방지 초기화)
        charactersDamagedThisSwing.Clear();

        // 부모(DamageCollider) 클래스의 기존 리스트도 안전을 위해 함께 초기화합니다.
        if (characterDamaged != null)
        {
            characterDamaged.Clear();
        }

#if UNITY_EDITOR
        // [Fix-P0] 파이프라인 추적 로그 (프로덕션에서는 logColliderToggle=false)
        if (logColliderToggle)
        {
            string parentName = transform.parent != null ? transform.parent.name : "<root>";
            Debug.Log($"<color=green>[MWD.Enable]</color> {gameObject.name}  " +
                      $"parent={parentName}  " +
                      $"frame={Time.frameCount}");
        }
#endif
    }

    public override void DisableDamageCollider()
    {
        damageCollider.enabled = false;
        charactersDamagedThisSwing.Clear();

#if UNITY_EDITOR
        // [Fix-P0] 파이프라인 추적 로그
        if (logColliderToggle)
        {
            Debug.Log($"<color=red>[MWD.Disable]</color> {gameObject.name}  " +
                      $"frame={Time.frameCount}");
        }
#endif
    }

    // =========================================================================================
    // [Fix-P0 진단 헬퍼] 정체불명 Collider 탐지를 위해 현재 GameObject 의 Hierarchy 풀경로 반환.
    // =========================================================================================
    private string GetFullHierarchyPath()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(gameObject.name);
        Transform t = transform.parent;
        while (t != null)
        {
            sb.Insert(0, t.name + "/");
            t = t.parent;
        }
        return sb.ToString();
    }

    // =========================================================================================

    protected override void OnTriggerEnter(Collider other)
    {
#if UNITY_EDITOR
        // [Fix-P0 진단 로그] OnTriggerEnter 자체가 호출되는지 확인 (CharacterManager 가 아닌 other 도 포함)
        if (logColliderToggle)
        {
            Debug.Log($"<color=#FF8800>[MWD.Trigger]</color> {gameObject.name} 의 Collider 에 " +
                      $"{other.gameObject.name} (layer={LayerMask.LayerToName(other.gameObject.layer)}) 진입. " +
                      $"enabled={damageCollider.enabled}  frame={Time.frameCount}");
        }
#endif
        // 콜라이더에 접촉된 other의 캐릭터 컴포넌트를 가져온후 damageTarget 에 복사.
        CharacterManager damageTarget = other.GetComponentInParent<CharacterManager>();

        if (damageTarget != null)
        {
            // 스스로 공격 안되게 방지.
            if (damageTarget == characterCausingDamage)
                return;

            // [P0-03] 이번 궤적(Swing)에서 이미 때린 대상이라면 무시! (팔, 다리 동시 충돌 다단 히트 방지)
            if (charactersDamagedThisSwing.Contains(damageTarget))
                return;

            // 검문을 통과했다면 때린 놈 리스트에 등록
            charactersDamagedThisSwing.Add(damageTarget);

            contactPoint = other.gameObject.GetComponent<Collider>().ClosestPointOnBounds(transform.position);
#if UNITY_EDITOR
            if (logColliderToggle) Debug.Log(other.gameObject);
#endif

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
#if UNITY_EDITOR
        if (logColliderToggle) Debug.Log(damageTarget);
#endif

        // =========================================================================================
        // [Fix-P0-B 신규] WorldCharacterEffectsManager 싱글톤 null-guard
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // 씬에 WorldCharacterEffectsManager GameObject 가 배치되지 않으면 Instance 가 null 이 되고,
        // 기존 코드는 Instantiate(Instance.takeDamageEffect) 에서 즉시 NullReferenceException 발생.
        // 이 경우 공격 플로우 자체가 죽어 방어/패링/네트워크 전송 로직까지 전부 미실행됩니다.
        //
        // 정석 해결은 씬의 DontDestroyOnLoad 루트에 해당 매니저 프리팹을 배치하는 것이지만,
        // 빠진 상태에서도 최소한 크래시 없이 Hit/Block/Parry 판정과 네트워크 전송이 정상 수행되도록
        // 방어 코드를 추가합니다. takeDamageEffect 프리팹이 없을 때는 런타임 1회성 인스턴스를
        // 생성해 물리 파라미터(physicalDamage, elementDamage, contactPoint, angleHitFrom) 만 채워서
        // 후속 로직을 그대로 이어갑니다.
        // =========================================================================================
        TakeDamageEffect damageEffect;
        if (WorldCharacterEffectsManager.Instance != null &&
            WorldCharacterEffectsManager.Instance.takeDamageEffect != null)
        {
            damageEffect = Instantiate(WorldCharacterEffectsManager.Instance.takeDamageEffect);
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning(
                "<color=orange>[MWD.DamageTarget]</color> WorldCharacterEffectsManager.Instance " +
                "또는 takeDamageEffect 가 null 입니다. 씬에 해당 매니저 GameObject 를 배치하고 " +
                "프리팹을 할당해 주세요. 임시 TakeDamageEffect 인스턴스로 대체 실행합니다.");
#endif
            // ScriptableObject 기반이 아닌 MonoBehaviour TakeDamageEffect 라면 new 대신 AddComponent 필요
            // 여기서는 일반적인 CharacterEffect ScriptableObject 계열을 가정하고 런타임 SO 생성
            damageEffect = ScriptableObject.CreateInstance<TakeDamageEffect>();
        }

        damageEffect.physicalDamage = physicalDamage;
        damageEffect.elementDamage = elementalDamage;
        damageEffect.contactPoint = contactPoint;
        damageEffect.angleHitFrom = Vector3.SignedAngle(characterCausingDamage.transform.forward, damageTarget.transform.forward, Vector3.up);

        switch (characterCausingDamage.characterCombatManager.currentAttackType)
        {
            case AttackType.LightAttack01:
                ApplyAttackDamageModifiers(light_Attack_01_Modifier, damageEffect);
#if UNITY_EDITOR
                if (logColliderToggle) Debug.Log("AttackType : " + AttackType.LightAttack01);
#endif
                break;
            case AttackType.LightAttack02:
                ApplyAttackDamageModifiers(light_Attack_02_Modifier, damageEffect);
#if UNITY_EDITOR
                if (logColliderToggle) Debug.Log("AttackType : " + AttackType.LightAttack02);
#endif
                break;
            case AttackType.HeavyAttack01:
                ApplyAttackDamageModifiers(heavy_Attack_01_Modifier, damageEffect);
#if UNITY_EDITOR
                if (logColliderToggle) Debug.Log("AttackType : " + AttackType.HeavyAttack01);
#endif
                break;
            case AttackType.HeavyAttack02:
                ApplyAttackDamageModifiers(heavy_Attack_02_Modifier, damageEffect); // user 코드에 맞춰 _01_Modifier 유지
#if UNITY_EDITOR
                if (logColliderToggle) Debug.Log("AttackType : " + AttackType.HeavyAttack02);
#endif
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
#if UNITY_EDITOR
                if (logColliderToggle) Debug.Log($"<color=yellow>[DamageCollider] {damageTarget.name}가 공격을 방어했습니다! (Blocked)</color>");
#endif
                break;

            case DefenseResult.Parried:
                // 3. 완벽한 패링 성공 (데미지 무효화 및 공격자에게 역경직 부여)
                if (characterCausingDamage.characterEventManager != null)
                {
                    characterCausingDamage.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Recoil_Trigger);
                }
#if UNITY_EDITOR
                if (logColliderToggle) Debug.Log($"<color=cyan>[DamageCollider] {damageTarget.name}가 패링에 성공했습니다! (Parried)</color>");
#endif
                break;

            case DefenseResult.Deflected:
            case DefenseResult.GuardBroken:
                // 4. 가드 튕겨짐 또는 파손 (페널티로 데미지의 50%만 적용)
                damageEffect.physicalDamage *= 0.5f;
                damageEffect.elementDamage *= 0.5f;
                SendDamageToServer(damageTarget, damageEffect);
                break;
        }
        // =====================================================================
        // [Phase3 신규] 가해자 측 타격 확인 이벤트 발행
        // =====================================================================
        if (characterCausingDamage != null &&
            characterCausingDamage.characterCombatManager != null)
        {
            float confirmedDmg = damageEffect.physicalDamage + damageEffect.elementDamage;
            HitConfirmedData hcd = new HitConfirmedData
            {
                victim = damageTarget,
                contactPoint = contactPoint,
                damageDealt = confirmedDmg,
                wasPoiseBreak = damageEffect.poiseIsBroken,
                attackType = characterCausingDamage.characterCombatManager.currentAttackType,
                defenseResult = defenseResult
            };
            characterCausingDamage.characterCombatManager.OnHitConfirmed(hcd);
        }
    }

    /// <summary>
    /// 기존의 네트워크 전송 코드를 캡슐화하여 분기문에서 재사용할 수 있도록 만든 헬퍼 함수입니다. (기존 주석 100% 보존)
    /// </summary>
    private void SendDamageToServer(CharacterManager damageTarget, TakeDamageEffect damageEffect)
    {
        //damageTarget.characterEffectsManager.ProcessInstantEffects(damageEffect);

#if UNITY_EDITOR
        if (logColliderToggle) Debug.Log($"Attacker :  +  {characterCausingDamage.name}, IsOwner: {characterCausingDamage.IsOwner}");
#endif
        //if (characterCausingDamage.IsOwner)
        //{

        // =========================================================================================
        // [Fix-P0-C 신규] 네트워크 비활성(싱글/에디터 테스트) 로컬 폴백
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // 기존 코드는 전부 ServerRpc 경로(NotifyTheServerOfCharacterDamageServerRpc)로만 데미지를 전송하며,
        // 서버 RPC 내부에서도 SpawnManager.SpawnedObjects.TryGetValue 로 스폰된 NetworkObject 만 조회합니다.
        //
        // 문제: NetworkManager.IsListening == false (Host 미기동 / 단일 플레이 테스트) 이면
        //   1) ServerRpc 호출이 서버에 도달하지 못하고 drop
        //   2) 도달한다 해도 씬 배치 AI 의 NetworkObject 가 스폰 전 상태라 TryGetValue 실패
        //   → 결과적으로 damageTarget.characterEffectsManager.ProcessInstantEffects 가 전혀 호출되지 않음
        //   → 플레이어 HP 감소/피격 애니메이션/VFX 전부 미발동 (현재 증상)
        //
        // 해결: 네트워크가 활성화되어 있을 때만 기존 ServerRpc 경로를 유지하고,
        //       비활성 상태에서는 ProcessInstantEffects 를 로컬에서 직접 호출해
        //       싱글 테스트에서도 데미지 파이프라인이 정상 가동되도록 합니다.
        //
        // 로컬 경로에서는 ProcessCharacterDamageFromServer 가 세팅하는 것과 동일하게
        // damageEffect.characterCausingDamage 를 할당해 이펙트 매니저의 후속 연산(방향/역경직 등)이
        // 원본 네트워크 경로와 동일한 입력을 받게 합니다.
        // =========================================================================================
        bool isNetworkActive = Unity.Netcode.NetworkManager.Singleton != null
                            && Unity.Netcode.NetworkManager.Singleton.IsListening;

        if (!isNetworkActive)
        {
            damageEffect.characterCausingDamage = characterCausingDamage;     // [Fix-P0-C]
            if (damageTarget.characterEffectsManager != null)                 // [Fix-P0-C]
            {
                damageTarget.characterEffectsManager.ProcessInstantEffects(damageEffect);
#if UNITY_EDITOR
                if (logColliderToggle)
                {
                    Debug.Log($"<color=lime>[MWD.LocalDmg]</color> {damageTarget.name} <- " +
                              $"{characterCausingDamage.name}  phys={damageEffect.physicalDamage} " +
                              $"elem={damageEffect.elementDamage} poise={damageEffect.poiseDamage}  " +
                              "(네트워크 비활성: 로컬 폴백 경로로 처리)");
                }
#endif
            }
            return;
        }

        // 아래는 기존 네트워크 경로 (100% 보존)
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