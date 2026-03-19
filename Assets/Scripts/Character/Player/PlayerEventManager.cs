using UnityEngine;
using System.Collections;
using TDA.Character;
using TDA.World;
using TDA.Cameras; // [신규] 카메라 SO 데이터 참조를 위해 추가
// using UnityEngine.Rendering; // 향후 포스트 프로세싱 연동 시 주석 해제

namespace TDA.Character.Player
{
    /// <summary>
    /// [Player Specific Event Router] 플레이어 캐릭터에서 발생하는 Enum 이벤트를 수신하여
    /// 1인칭 바디캠 쉐이크, 풋스텝 사운드, 회전 락(Lock), 화면 비네팅 등 실질적인 게임 로직으로 토스(Toss)하는 브릿지입니다.
    /// </summary>
    public class PlayerEventManager : CharacterEventManager
    {
        private PlayerManager player;

        // =========================================================================================
        // 🚨 [데이터 주도 설계 완벽화] 하드코딩 플로트(Float)를 제거하고 SO 참조로 전면 개편!
        // 기획자가 Camera Stance / Sequence SO에 세팅해둔 값을 그대로 빼와서 사용합니다.
        // =========================================================================================
        [Header("Camera Directing Data (SO-Driven)")]
        [Tooltip("가벼운 피격이나 착지 시 쉐이크 수치를 가져올 1계층 스탠스 SO")]
        [SerializeField] private CameraStancePresetSO lightShakeStance;

        [Tooltip("묵직한 강공격 임팩트나 강한 넉백 시 쉐이크 수치를 가져올 1계층 스탠스 SO")]
        [SerializeField] private CameraStancePresetSO heavyShakeStance;

        [Tooltip("구르기 후 바닥을 짚을 때 쉐이크 수치를 가져올 1계층 스탠스 SO")]
        [SerializeField] private CameraStancePresetSO rollShakeStance;

        [Tooltip("절기(Fumble) 조작 실패 시, 쉐이크/비네트/틸트 등 전방위 멀미 연출을 덮어씌울 2계층 시퀀스 SO")]
        [SerializeField] private CameraSequencePresetSO fumblePenaltySequence;

        [Tooltip("일반 피격 시 화면 비네트(붉은 점멸) 효과를 덮어씌울 2계층 시퀀스 SO")]
        [SerializeField] private CameraSequencePresetSO hitVignetteSequence;

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
                // 🚨 [아키텍처 정상화] 카메라 직접 호출(player.playerCamera) 금지!
                // 모든 연출 요청은 오직 중앙 관제탑(WorldCameraManager)으로만 보냅니다.
                // =======================================================
                case global::AnimationEventType.CameraShake_Light:
                    // 로컬 플레이어의 화면만 흔들리도록 IsOwner 검사
                    if (player.IsOwner && WorldCameraManager.Instance != null && lightShakeStance != null)
                    {
                        // 기획자가 SO 인스펙터에 입력한 값을 그대로 빼와서 관제탑에 전달합니다.
                        if (lightShakeStance.impactShake.enableShake)
                        {
                            WorldCameraManager.Instance.ApplyCameraShake(lightShakeStance.impactShake.shakeIntensity, lightShakeStance.impactShake.maxDuration);
                        }
                    }
                    break;

                case global::AnimationEventType.CameraShake_Heavy:
                    if (player.IsOwner && WorldCameraManager.Instance != null && heavyShakeStance != null)
                    {
                        if (heavyShakeStance.impactShake.enableShake)
                        {
                            WorldCameraManager.Instance.ApplyCameraShake(heavyShakeStance.impactShake.shakeIntensity, heavyShakeStance.impactShake.maxDuration);
                        }
                    }
                    break;

                case global::AnimationEventType.CameraShake_Roll:
                    if (player.IsOwner && WorldCameraManager.Instance != null && rollShakeStance != null)
                    {
                        if (rollShakeStance.impactShake.enableShake)
                        {
                            WorldCameraManager.Instance.ApplyCameraShake(rollShakeStance.impactShake.shakeIntensity, rollShakeStance.impactShake.maxDuration);
                        }
                    }
                    break;

                // 🚨 [Phase 4] 절기(Fumble) 반동 제어 (2계층 시퀀스 통째로 실행!)
                case global::AnimationEventType.CameraShake_Heavy_Fumble:
                    if (player.IsOwner && WorldCameraManager.Instance != null && fumblePenaltySequence != null)
                    {
                        // 단순 Shake를 넘어, SO에 정의된 극단적인 FOV 왜곡, Z-Tilt(화면 비틀림), 강한 쉐이크를 한방에 터뜨립니다.
                        WorldCameraManager.Instance.PlayCameraSequence(fumblePenaltySequence);
                        Debug.Log("<color=red>[PlayerEventManager]</color> 절기(Fumble) 시퀀스 SO 재생: 멀미와 방향 감각 상실 유도!");
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
                // [사운드 피드백] 발소리 및 타격/피격음
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

                // 🚨 [Phase 4] 절기(Fumble) 사운드 에셋 연동
                case global::AnimationEventType.PlayVoice_Stagger_Pain:
                    // TODO: 오디오 매니저 연동 (근육 꼬이는 소리, 뼈 어긋나는 소리, 고통스러운 기침 등)
                    // 예: AudioManager.Instance.PlaySFX(SFX_Muscle_Tear);
                    Debug.Log("<color=red>[PlayerEventManager]</color> 뼈와 살이 뒤틀리는 사운드 재생 (절기 페널티)!");
                    break;

                // =======================================================
                // [시각 효과 / VFX 연동]
                // =======================================================
                // 🚨 [Phase 4] 화면 비네트(Vignette) 포스트 프로세싱 트리거
                case global::AnimationEventType.ScreenFX_Hit_Vignette:
                    if (player.IsOwner && WorldCameraManager.Instance != null && hitVignetteSequence != null)
                    {
                        // 기존 UI 팝업 방식이 아닌, Camera Sequence SO 내부의 비네팅(Vignette Intensity) 수치를 읽어 자연스럽게 화면 테두리를 조입니다!
                        WorldCameraManager.Instance.PlayCameraSequence(hitVignetteSequence);
                        Debug.Log("<color=red>[PlayerEventManager]</color> 화면 외곽 붉은색 점멸 (Vignette 시퀀스 SO) 트리거!");
                    }
                    break;
            }
        }
    }
}