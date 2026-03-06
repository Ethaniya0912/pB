using System;
using TDA.Character;
using UnityEngine;


namespace TDA.Character.AI
{
    /// <summary>
    /// [P1 & P4] AI(적/NPC) 캐릭터 전용 애니메이션 이벤트 매니저입니다.
    /// CharacterEventManager를 상속받아 기본적인 Pub-Sub 기능을 수행하며,
    /// AI의 상태 머신(FSM) 전환 신호나 특수 패턴 트리거 등을 확장하기에 용이합니다.
    /// </summary>
    public class AIEventManager : CharacterEventManager
    {
        // 향후 AI 전용 이벤트 확장 예시
        // public event Action OnAIPhaseChanged;


        public void Awake()
        {
#if UNITY_EDITOR
            Debug.Log($"<color=orange>[AIEventManager]</color> '{gameObject.name}' AI 이벤트 채널이 활성화되었습니다.");
#endif
        }


        // AI 행동 트리나 상태 머신에 특화된 중계 로직이 필요할 경우 여기에 작성합니다.
    }
}
