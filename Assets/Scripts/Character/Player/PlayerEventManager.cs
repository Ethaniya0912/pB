using UnityEngine;
using TDA.Character;
using TDA.World;

namespace TDA.Character.Player
{
    /// <summary>
    /// [Player Specific Event Router] 플레이어 캐릭터에서 발생하는 Enum 이벤트를 수신하여
    /// 1인칭 바디캠 쉐이크, 풋스텝 사운드, 회전 락(Lock) 등 실질적인 게임 로직으로 토스(Toss)하는 브릿지입니다.
    /// </summary>
    public class PlayerEventManager : CharacterEventManager
    {
        private PlayerManager player;

        protected virtual void Awake()
        {
            player = GetComponent<PlayerManager>();

            // 자신이 상속받은 중앙 브로드캐스터에 리스너(Listener) 등록
            OnAnimationEventTriggered += HandleAnimationEvents;
        }

        protected virtual void OnDestroy()
        {
            // 메모리 누수 방지
            OnAnimationEventTriggered -= HandleAnimationEvents;
        }

        /// <summary>
        /// 수신된 Enum 이벤트를 분석하여 각 매니저에게 명령을 하달하는 스위치 타워입니다.
        /// </summary>
        private void HandleAnimationEvents(global::AnimationEventType eventType)
        {
            switch (eventType)
            {
                // =======================================================
                // [카메라 연출 연동] (PlayerCamera.cs의 Shake 함수 호출)
                // =======================================================
                case global::AnimationEventType.CameraShake_Light:
                    if (player.IsOwner && player.playerCamera != null)
                    {
                        // 가벼운 피격이나 착지 시 미세한 흔들림 (Intensity, Duration)
                        player.playerCamera.Shake(0.15f, 0.2f);
                    }
                    break;

                case global::AnimationEventType.CameraShake_Heavy:
                    if (player.IsOwner && player.playerCamera != null)
                    {
                        // 묵직한 강공격 임팩트나 강한 넉백 시의 화면 지진
                        player.playerCamera.Shake(0.4f, 0.35f);
                    }
                    break;

                case global::AnimationEventType.CameraShake_Roll:
                    if (player.IsOwner && player.playerCamera != null)
                    {
                        // 구르기 후 바닥을 짚을 때의 특유의 충격
                        player.playerCamera.Shake(0.2f, 0.15f);
                    }
                    break;

                // =======================================================
                // [로코모션 물리 락(Lock) 연동]
                // =======================================================
                case global::AnimationEventType.Lock_Rotation:
                    // 공격 시 특정 프레임(베기 직전)부터 플레이어의 마우스 억지 회전을 막아 스케이팅 차단
                    if (player.playerLocomotionManager != null)
                        player.playerLocomotionManager.isRotationLockedByEvent = true;
                    break;

                case global::AnimationEventType.Unlock_Rotation:
                    // 후딜레이 진입 시 다시 방향 회전을 허용
                    if (player.playerLocomotionManager != null)
                        player.playerLocomotionManager.isRotationLockedByEvent = false;
                    break;

                // =======================================================
                // [사운드/VFX 피드백 (Placeholder)]
                // =======================================================
                case global::AnimationEventType.PlayFootstep_L:
                case global::AnimationEventType.PlayFootstep_R:
                case global::AnimationEventType.PlayFootstep_Drag_L:
                case global::AnimationEventType.PlayFootstep_Drag_R:
                case global::AnimationEventType.PlayFootstep_Pivot:
                    // TODO: SoundManager를 호출하여 현재 바닥 재질에 맞는 풋스텝 재생
                    // 예: AudioManager.Instance.PlayFootstep(transform.position);
                    break;

                case global::AnimationEventType.PlaySFX_Swing_Light:
                case global::AnimationEventType.PlaySFX_Swing_Heavy:
                    // TODO: 무기 스윙 사운드 재생
                    break;
            }
        }
    }
}