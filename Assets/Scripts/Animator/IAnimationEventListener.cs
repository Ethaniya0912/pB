namespace TDA.Core.Events
{
    /// <summary>
    /// [P4] 애니메이션 이벤트 수신을 위한 Type-Safe 공통 규약 인터페이스입니다.
    /// 
    /// [아키텍처 설계 철학]
    /// C#의 다중 상속 금지 규칙으로 인해, NGO의 NetworkBehaviour를 상속받아야 하는 도메인 매니저들이 
    /// 이벤트를 안전하게 수신할 수 있도록 '구현(Implementation)' 방식으로 자격을 부여합니다.
    /// 과거의 int 해시 방식에서 벗어나, 명시적인 Enum을 사용하여 휴먼 에러를 0%로 차단합니다.
    /// </summary>
    public interface IAnimationEventListener
    {
        /// <summary>
        /// CharacterEventManager(중앙 방송국)로부터 애니메이션 이벤트 신호를 수신할 때 호출됩니다.
        /// 수신자는 switch(eventType) 패턴을 사용하여 자신에게 필요한 이벤트만 취사선택해야 합니다.
        /// </summary>
        /// <param name="eventType">Enums.cs에 정의된 규격화된 이벤트 타입</param>
        void OnAnimationEventReceived(global::AnimationEventType eventType);
    }
}