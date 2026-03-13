using UnityEngine;
using TDA.Items;

namespace TDA.Character.Player
{
    /// <summary>
    /// [L3 Domain Layer / Child Class] 
    /// CharacterDefenseManager의 물리적 방어 연산을 상속받아, 
    /// 플레이어 전용 자원인 '스태미나' 소모 로직과 카메라/사운드 피드백을 결합합니다.
    /// </summary>
    public class PlayerDefenseManager : CharacterDefenseManager
    {
        private PlayerManager player;

        [Header("Player Specific: Stamina Drain")]
        [Tooltip("방어 성공 시 들어온 데미지의 몇 %를 스태미나 피해로 변환할지 결정합니다. (기본값: 0.2 = 20%)")]
        [SerializeField] private float baseStaminaDrainRatio = 0.2f;

        [Tooltip("Q키(멀리 잡기/FarGrip) 방어 시 적용되는 스태미나 소모량 페널티 배수입니다. (기본값: 1.5배)")]
        [SerializeField] private float farGripStaminaPenaltyMultiplier = 1.5f;

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();
        }

        /// <summary>
        /// [오버라이드] 방어를 올릴 때 플레이어의 스태미나 상태를 먼저 검문합니다.
        /// </summary>
        public override void StartDefense(GuardDirection direction, ShieldStance stance = ShieldStance.CloseGrip)
        {
            // 스태미나가 바닥이면 가드를 아예 올리지 못함
            if (player.playerNetworkManager != null && player.playerNetworkManager.currentStamina.Value <= 0)
            {
                // 중복 경고 방지: 방어가 꺼져있을 때만 로그 출력
                if (!isDefending && showDebugLogs)
                    Debug.Log("<color=red>[PlayerDefense]</color> 스태미나가 완전히 고갈되어 가드를 올릴 수 없습니다!");
                return;
            }

            // [수정] 중복되던 PlayerDefenseManager만의 '가드 발동' 로그를 삭제하여, 
            // 부모(CharacterDefenseManager)의 깔끔한 로그 하나만 출력되도록 통합했습니다.
            base.StartDefense(direction, stance);
        }

        /// <summary>
        /// [오버라이드] 적의 공격을 막아냈을 때, 부모의 판정 결과에 맞춰 스태미나 감소를 적용합니다.
        /// </summary>
        public override DefenseResult EvaluateDefense(HitEventData attackInfo)
        {
            // 1. 부모(CharacterDefenseManager)에게 수학적 판정을 먼저 맡깁니다.
            DefenseResult result = base.EvaluateDefense(attackInfo);

            // 2. 완벽한 패링(Parried)일 경우 특수 사운드 및 피드백 처리
            if (result == DefenseResult.Parried)
            {
                if (player.characterEventManager != null)
                {
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlaySFX_Parry_Clang);
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Spawn_Spark_Clash);
                }
            }
            // 3. 일반 방어 성공(Blocked)일 때만 플레이어 전용 스태미나 페널티를 부과합니다.
            else if (result == DefenseResult.Blocked)
            {
                ApplyStaminaDrain(attackInfo.damage);

                // 방어 성공 시의 둔탁한 피드백 (사운드, 이펙트, 가벼운 카메라 흔들림)
                if (player.characterEventManager != null)
                {
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlaySFX_Guard_Thud);
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.Spawn_Spark_Clash);
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.CameraShake_Light);
                }
            }
            // 4. 가드가 튕겨졌을 때(Deflected)의 강력한 피드백
            else if (result == DefenseResult.Deflected)
            {
                if (player.characterEventManager != null)
                {
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlaySFX_Impact_Armor);
                    player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.CameraShake_Heavy);
                }
            }

            return result;
        }

        /// <summary>
        /// 스태미나 차감 전담 헬퍼 메서드
        /// </summary>
        private void ApplyStaminaDrain(float incomingDamage)
        {
            if (player.playerNetworkManager == null) return;

            // 인스펙터 변수와 현재 스탠스를 조합하여 최종 차감량 계산
            float staminaDrainMultiplier = (currentShieldStance == ShieldStance.FarGrip) ? farGripStaminaPenaltyMultiplier : 1.0f;
            float staminaDrain = incomingDamage * baseStaminaDrainRatio * staminaDrainMultiplier;

            player.playerNetworkManager.currentStamina.Value -= staminaDrain;

            if (showDebugLogs)
            {
                Debug.Log($"<color=yellow>[PlayerDefense]</color> 방어 스태미나 소모: -{staminaDrain:F1} (현재 스탠스: {currentShieldStance})");
            }

            // 공격을 막는 순간 스태미나가 0 이하로 떨어지면 플레이어 특유의 가드 붕괴(Guard Break) 발생
            if (player.playerNetworkManager.currentStamina.Value <= 0)
            {
                HandleGuardBreakByStamina();
            }
        }

        /// <summary>
        /// 스태미나가 바닥나서 가드가 뚫렸을 때의 처리 (방패 자체의 내구도는 무사한 일시적 무방비 상태)
        /// </summary>
        private void HandleGuardBreakByStamina()
        {
            if (showDebugLogs) Debug.Log("<color=orange>[PlayerDefense]</color> 스태미나 임계치 초과로 인한 가드 붕괴! (Stamina Out)");

            if (player.characterEventManager != null)
            {
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlaySFX_Impact_Armor);
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.CameraShake_Heavy);
            }

            // 향후 Guard_Break_Stun 모션(ActionID.Guard_Break_Stun 등)을 호출하는 로직 추가

            BreakGuardAndTriggerEvent(DefenseResult.GuardBroken);
        }

        /// <summary>
        /// [오버라이드] 내구도가 0이 되어 '실제로 방패가 박살 났을 때'의 처리 (영구적 파손)
        /// </summary>
        protected override void DestroyShield()
        {
            base.DestroyShield();
            if (showDebugLogs) Debug.Log("<color=red>[PlayerDefense - Breakage Logic]</color> 방패 완전 파손 프로세스 가동!");

            // 파손 시 특유의 폭발음과 파편 이펙트
            if (player.characterEventManager != null)
            {
                player.characterEventManager.NotifyAnimationEvent(global::AnimationEventType.PlaySFX_Impact_Bone); // 임시 굉음
            }

            // TODO: PlayerEquipmentManager를 호출하여 왼손 무기 슬롯(방패 모델) 강제 숨김/해제 처리
        }
    }
}