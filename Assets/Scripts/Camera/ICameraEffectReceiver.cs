// =============================================================================
// ICameraEffectReceiver.cs  |  TDA Project
// Layer  : L4 Interface — 카메라/블러 이펙트 오버레이 수신 규약
//
// 설계 의도:
//   3채널 이벤트(StanceSO 타임라인 / AnimationEventPoint / CombatHit)로부터
//   카메라·블러 효과를 수신하여 독립적으로 처리하는 컴포넌트의 공통 규약입니다.
//
// 구현 대상:
//   - P2_ObjectMotionBlurController  (블러 Pulse 처리)  [Phase4 구현 예정]
//   - (향후) PostProcessingManager, ScreenEffectManager 등
//
// 발행 경로:
//   WorldCameraManager.PlayOverlayEffect(data)
//     → foreach ICameraEffectReceiver → ReceiveOverlayEffect(data)
//
//   CharacterEventManager.NotifyAnimationEvent(eventType)
//     → 구독 중인 구현체 → OnAnimationEventReceived(eventType)
//       → ReceiveCameraEffectEvent(eventType) 내부 호출
//
// 계층 규약:
//   - 이 인터페이스 구현체는 L4 (View) 계층에 위치합니다.
//   - 이펙트 강도/시간은 BlurEventResponseSO 또는 CameraEffectOverlayData에서만 읽습니다.
//   - 하드코딩 금지. 이벤트 타입만 받고, 수치는 SO가 결정합니다.
// =============================================================================
using TDA.Core.Events;

namespace TDA.Cameras
{
    /// <summary>
    /// [Phase1 신규] 카메라/블러 이펙트 오버레이를 수신할 수 있는 컴포넌트의 공통 규약.
    ///
    /// P2_ObjectMotionBlurController 등 이펙트 처리 컴포넌트가 이 인터페이스를 구현하여
    /// WorldCameraManager 및 CharacterEventManager로부터 이펙트 요청을 수신합니다.
    /// </summary>
    public interface ICameraEffectReceiver
    {
        // =========================================================================================
        // 메서드 1: 이벤트 타입 기반 수신 (약결합)
        //
        // 발행자는 "무슨 일이 발생했는지"만 알립니다.
        // 수신자(블러 컨트롤러)가 BlurEventResponseSO를 통해
        // "얼마나 강하게, 얼마나 오래" 반응할지 자율적으로 결정합니다.
        //
        // 사용 시나리오:
        //   - Hit_Confirmed(88): 경공격 타격 확인 → 블러 경량 Pulse
        //   - Hit_Confirmed_Heavy(89): 포이즈 파괴 → 블러 강한 Pulse
        //   - Hit_From_Front(84) ~ Hit_From_Behind(87): 방향별 피격 → 방향별 블러
        // =========================================================================================

        /// <summary>
        /// 이벤트 타입에 따른 이펙트 반응 요청.
        /// 수신자는 eventType을 보고 BlurEventResponseSO에서 강도/시간을 자율적으로 조회합니다.
        /// </summary>
        /// <param name="eventType">
        /// CharacterEventManager를 통해 수신된 AnimationEventType.
        /// Hit_Confirmed, Hit_From_Front 등 카메라/블러 관련 이벤트를 처리합니다.
        /// </param>
        void ReceiveCameraEffectEvent(AnimationEventType eventType);

        // =========================================================================================
        // 메서드 2: 오버레이 데이터 직접 수신 (강결합, 정밀 제어)
        //
        // 발행자(WorldCameraManager.PlayOverlayEffect)가 구체적인 강도/시간을 SO에서 읽어
        // 직접 전달하는 방식입니다.
        //
        // 사용 시나리오:
        //   - StanceEventPoint.overlayData: Stance 구간 진입 시 즉시 적용
        //   - AnimationEventPoint.overlayData: 애니메이션 이벤트 포인트 발행 시 적용
        // =========================================================================================

        /// <summary>
        /// SO 데이터 기반 오버레이 직접 수신.
        /// 발행자가 이미 수치를 결정했으므로 수신자는 그대로 적용합니다.
        /// blurStrengthDelta, fovDelta 등 0값인 필드는 해당 효과 없음으로 처리하세요.
        /// </summary>
        /// <param name="overlayData">
        /// CameraEffectOverlayData 구조체.
        /// CameraStancePresetSO.StanceEventPoint 또는 AnimationEventPoint에서 제공됩니다.
        /// </param>
        void ReceiveOverlayEffect(CameraEffectOverlayData overlayData);
    }
}