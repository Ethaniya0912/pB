using UnityEngine;
using TDA.Items;

namespace TDA.Character
{
    /// <summary>
    /// [L3 Domain Layer / Base Class] 
    /// 캐릭터의 물리적 방어 연산, 내구도 관리, 패링 타이밍을 전담하는 부모 클래스입니다.
    /// 플레이어와 몬스터(AI) 모두 이 클래스의 공통 물리/수학 규칙을 따릅니다.
    /// </summary>
    public class CharacterDefenseManager : MonoBehaviour
    {
        protected CharacterManager character;

        [Header("Defense State & Stance")]
        [Tooltip("현재 방어 중인 4방향 자세입니다.")]
        public GuardDirection currentGuardDirection = GuardDirection.None;

        [Tooltip("현재 방패를 쥐는 자세 (몸 가까이 붙이기 vs 멀리 뻗기) 입니다.")]
        public ShieldStance currentShieldStance = ShieldStance.CloseGrip;
        public bool isDefending = false;

        [Header("Defense Data (Shield & Weapon)")]
        [Tooltip("현재 방어에 사용 중인 장비입니다. 방패가 없다면 들고 있는 무기(검 등)가 할당됩니다.")]
        public WeaponItem currentDefendingItem;
        [SerializeField] protected float currentShieldDurability;
        protected float guardStartTime;

        // =========================================================================================
        // [신규] 방어 자세(Guard Style) 애니메이션 보간 설정
        // =========================================================================================
        [Header("Animation Transition Settings")]
        [Tooltip("방어 자세(우클릭 ↔ Q키) 전환 시 애니메이션이 부드럽게 섞이는 기본 속도입니다.\n추후 Stats(민첩성 등)와 연동하여 최종 속도 산출 시 베이스 값으로 사용됩니다.")]
        public float baseGuardStyleTransitionSpeed = 8f;

        // 보간 처리를 위한 내부 변수
        protected float currentGuardStyleValue = 0f;
        protected float targetGuardStyleValue = 0f;

        [Header("Debug Settings")]
        [Tooltip("체크 시 방어 시스템의 각종 판정 결과와 내구도 차감 로그를 콘솔에 상세 출력합니다.")]
        public bool showDebugLogs = true;

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
        }

        protected virtual void Update()
        {
            // 매 프레임 애니메이션 보간을 처리합니다.
            HandleGuardStyleInterpolation();
        }

        /// <summary>
        /// 매 프레임 guardStyle 파라미터를 부드럽게 보간(Lerp)하여 애니메이터에 전달합니다.
        /// </summary>
        protected virtual void HandleGuardStyleInterpolation()
        {
            if (character == null || character.animator == null) return;

            // 현재 값을 타겟 값으로 baseGuardStyleTransitionSpeed에 맞춰 부드럽게 이동시킵니다.
            currentGuardStyleValue = Mathf.Lerp(currentGuardStyleValue, targetGuardStyleValue, Time.deltaTime * baseGuardStyleTransitionSpeed);

            // 애니메이터에 전달 (※ 애니메이터의 guardStyle 파라미터가 반드시 Float 타입이어야 합니다!)
            character.animator.SetFloat("guardStyle", currentGuardStyleValue);
        }

        /// <summary>
        /// 장비 매니저가 방패나 무기를 손에 장착할 때 호출하여 스펙(내구도)을 주입합니다.
        /// </summary>
        public virtual void SetDefendingItem(WeaponItem weaponSO)
        {
            currentDefendingItem = weaponSO;

            if (weaponSO is ShieldWeaponItemSO shieldSO)
            {
                currentShieldDurability = shieldSO.maxDurability;
                if (showDebugLogs) Debug.Log($"<color=cyan>[DefenseManager]</color> {gameObject.name}: '{shieldSO.itemName}' 방패 스펙 주입 (Max 내구도: {currentShieldDurability})");
            }
            else if (weaponSO != null)
            {
                // 방패가 아닌 일반 무기(검, 도끼 등)로 방어할 때의 임시 내구도 부여
                currentShieldDurability = 40f;
                if (showDebugLogs) Debug.Log($"<color=cyan>[DefenseManager]</color> {gameObject.name}: '{weaponSO.itemName}' 무기로 임시 방어 셋업 완료.");
            }
            else
            {
                if (showDebugLogs) Debug.Log($"<color=cyan>[DefenseManager]</color> {gameObject.name}: 방어구 장착 해제됨 (맨손).");
            }
        }

        /// <summary>
        /// 방어 입력 시 호출되어 가드 자세, 스탠스, 타이밍을 기록하고 애니메이터를 제어합니다.
        /// </summary>
        public virtual void StartDefense(GuardDirection direction, ShieldStance stance = ShieldStance.CloseGrip)
        {
            // 무기가 아예 없거나(맨손) 다른 액션 중이면 방어 불가
            if (currentDefendingItem == null || character.isPerformingAction) return;

            // 이미 똑같은 자세(Stance)와 방향으로 방어 중이라면, 아무것도 하지 않고 무시 (Input 노이즈 차단)
            if (isDefending && currentShieldStance == stance && currentGuardDirection == direction) return;

            currentGuardDirection = direction;
            currentShieldStance = stance;
            isDefending = true;
            guardStartTime = Time.time;

            character.animator.SetBool("isDefending", true);

            // [변경점] 즉각적으로 파라미터를 쏘지 않고, 보간을 위한 타겟값만 갱신합니다.
            targetGuardStyleValue = (stance == ShieldStance.CloseGrip) ? 0f : 1f;

            if (showDebugLogs)
            {
                string stanceName = stance == ShieldStance.CloseGrip ? "가까이(우클릭)" : "멀리(Q키)";
                Debug.Log($"<color=cyan>[DefenseManager]</color> 🛡️ 가드 발동! (자세: {stanceName}, 무기: {currentDefendingItem.itemName})");
            }
        }

        public virtual void StopDefense()
        {
            if (!isDefending) return;

            currentGuardDirection = GuardDirection.None;
            isDefending = false;

            character.animator.SetBool("isDefending", false);

            // 가드 해제 시 내부 보간 타겟도 기본값(가까이 잡기)으로 리셋해 둡니다.
            targetGuardStyleValue = 0f;

            if (showDebugLogs)
            {
                Debug.Log($"<color=gray>[DefenseManager]</color> 🛑 가드 해제 완료.");
            }
        }

        public virtual DefenseResult EvaluateDefense(HitEventData attackInfo)
        {
            // 1. 가드가 내려가 있거나 무기가 없으면 피격(Hit)
            if (!isDefending || currentGuardDirection == GuardDirection.None || currentDefendingItem == null)
                return DefenseResult.Hit;

            // =========================================================================================
            // [검 vs 방패 스탯 차등화 로직]
            // =========================================================================================
            float parryWindow = 0.1f; // 검으로 막을 때는 패링 프레임이 0.1초로 매우 빡빡함
            float effectiveDeflectionRes = 20f; // 검으로 막으면 대검류 공격에 무조건 튕겨남(Deflect)

            if (currentDefendingItem is ShieldWeaponItemSO shieldData)
            {
                // 방패면 방패 SO에 적힌 여유로운 스탯을 적용
                parryWindow = shieldData.parryWindowDuration;
                effectiveDeflectionRes = shieldData.deflectionResistance;
            }

            // 2. 패링 타이밍 검사
            bool isTimingPerfect = (Time.time - guardStartTime) <= parryWindow;

            if (isTimingPerfect && currentShieldStance == ShieldStance.CloseGrip)
            {
                if (showDebugLogs) Debug.Log($"<color=lime>[DefenseManager]</color> {gameObject.name}: 완벽한 타이밍 패링(Parried) 성공!");
                return DefenseResult.Parried;
            }

            // 3. 처냄(Deflect) 저항력 물리 판정
            if (currentShieldStance == ShieldStance.FarGrip)
            {
                effectiveDeflectionRes *= 0.5f;
            }

            if (attackInfo.impactForce > effectiveDeflectionRes)
            {
                BreakGuardAndTriggerEvent(DefenseResult.Deflected);
                return DefenseResult.Deflected;
            }

            // 4. 일반 방어 성공 (Blocked) 및 내구도 차감 연산
            currentShieldDurability -= (attackInfo.damage * 0.5f);

            if (showDebugLogs) Debug.Log($"<color=yellow>[DefenseManager]</color> {gameObject.name}: 방어 성공. 방어구 내구도 감소: -{attackInfo.damage * 0.5f} (남은 내구도: {currentShieldDurability})");

            // 5. 내구도 고갈 검사
            if (currentShieldDurability <= 0)
            {
                DestroyShield();
                BreakGuardAndTriggerEvent(DefenseResult.GuardBroken);
                return DefenseResult.GuardBroken;
            }

            return DefenseResult.Blocked;
        }

        protected virtual void BreakGuardAndTriggerEvent(DefenseResult breakType)
        {
            StopDefense();
            if (showDebugLogs) Debug.Log($"<color=orange>[DefenseManager]</color> {gameObject.name}: 가드 붕괴 발생! 타입: {breakType}");
        }

        protected virtual void DestroyShield()
        {
            if (showDebugLogs) Debug.Log($"<color=red>[DefenseManager]</color> {gameObject.name}: 방어구 내구도 0 도달. 가드가 파괴되었습니다!");
            // (무기가 영구 삭제되는 것은 막기 위해 currentDefendingItem = null 처리는 제외)
        }
    }
}