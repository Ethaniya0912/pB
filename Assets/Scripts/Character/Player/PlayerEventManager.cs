using System;
using TDA.Character;
using UnityEngine;


namespace TDA.Character.Player
{
    /// <summary>
    /// [P1 & P4] 플레이어 캐릭터 전용 애니메이션 이벤트 매니저입니다.
    /// CharacterEventManager를 상속받아 기본적인 Pub-Sub 기능을 수행하며,
    /// 플레이어에게만 필요한 특수 UI 알림이나 입력 잠금 관련 이벤트를 확장하기에 용이합니다.
    /// </summary>
    public class PlayerEventManager : CharacterEventManager
    {
        // 향후 플레이어 전용 이벤트 확장 예시
        // public event Action OnPlayerStaminaExhausted;

        public void Awake()
        {
#if UNITY_EDITOR
            Debug.Log($"<color=cyan>[PlayerEventManager]</color> '{gameObject.name}' 플레이어 이벤트 채널이 활성화되었습니다.");
#endif
        }


        // 플레이어에게만 해당하는 로직이 필요할 경우 여기에 작성합니다.
    }
}
