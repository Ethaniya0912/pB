using System;
using UnityEngine;
using TDA.Core.Events;

#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
#endif

namespace TDA.Character
{
    /// <summary>
    /// [P4] 캐릭터 애니메이션 이벤트 중앙 통신 허브 (Broadcaster)
    /// 
    /// BaseActionBehaviour(감시자)로부터 Enum 기반의 이벤트를 수신하여,
    /// 이를 구독(Subscribe) 중인 하위 도메인 매니저(Combat, Effects, Sound)에게 맹목적으로 중계합니다.
    /// 로직을 처리하지 않는 순수 라우터(Router) 역할을 수행합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterEventManager : MonoBehaviour
    {
        #region [Events] 핵심 브로드캐스트 델리게이트
        /// <summary>
        /// [P4 개선] int 대신 명시적인 Enum을 전파하여 모든 수신자가 타입 안전성을 보장받습니다.
        /// </summary>
        public event Action<global::AnimationEventType> OnAnimationEventTriggered;
        #endregion

#if UNITY_EDITOR
        [Header("Console Debugging")]
        [Tooltip("체크 시 이벤트를 송출할 때마다 콘솔에 상세 로그를 출력합니다. (도배 주의)")]
        public bool showConsoleLogs = false;

        [Header("Gizmo Debugging")]
        [Tooltip("씬 뷰(Scene View)에서 캐릭터 머리 위에 최근 발생한 이벤트를 텍스트로 띄웁니다.")]
        public bool showEventGizmo = true;

        [Tooltip("기즈모 텍스트가 화면에 유지되는 시간(초)입니다.")]
        public float eventGizmoDuration = 1.0f;

        // 화면에 띄울 이벤트 기록을 담는 클래스
        private class EventLogEntry
        {
            public global::AnimationEventType eventType;
            public float expirationTime;
        }

        private List<EventLogEntry> recentEvents = new List<EventLogEntry>();
#endif

        #region [Logic] 이벤트 송출부
        /// <summary>
        /// 감시자(BaseActionBehaviour)가 델타 타임 트래킹 중 특정 프레임을 통과했을 때 호출합니다.
        /// </summary>
        public void NotifyAnimationEvent(global::AnimationEventType eventType)
        {
            // 1. 구독 중인 모든 리스너에게 이벤트 브로드캐스팅
            OnAnimationEventTriggered?.Invoke(eventType);

#if UNITY_EDITOR
            // 2. 에디터 환경 전용 디버그 로거 및 기즈모 추가 (블랙박스 방지)
            LogEventDispatch(eventType);

            if (showEventGizmo)
            {
                recentEvents.Add(new EventLogEntry
                {
                    eventType = eventType,
                    expirationTime = Time.time + eventGizmoDuration
                });
            }
#endif
        }
        #endregion

        #region [Memory Management] 생명주기 및 메모리 누수 방어 (Fail-safe)
        /// <summary>
        /// [치명적 데스싱크 및 메모리 누수 방어막]
        /// 객체가 파괴될 때 수신자들의 구독 해제 누락으로 인한 좀비 객체 호출을 원천 차단합니다.
        /// </summary>
        private void OnDestroy()
        {
            if (OnAnimationEventTriggered != null)
            {
#if UNITY_EDITOR
                int remainingSubscribers = OnAnimationEventTriggered.GetInvocationList().Length;
                if (remainingSubscribers > 0)
                {
                    Debug.LogWarning($"[CharacterEventManager] '{gameObject.name}' 파괴 시점까지 구독을 해제하지 않은 리스너가 {remainingSubscribers}개 존재합니다. 강제로 연결을 해제합니다.");
                }
#endif
                // 모든 델리게이트 구독망 강제 찢기
                OnAnimationEventTriggered = null;
            }
        }
        #endregion

#if UNITY_EDITOR
        #region [Debug] 디버깅 툴
        private void LogEventDispatch(global::AnimationEventType eventType)
        {
            if (OnAnimationEventTriggered == null)
            {
                // 수신자가 한 명도 없으면 체크박스 여부와 상관없이 경고를 띄웁니다! (매우 중요한 디버깅 기능)
                Debug.LogWarning($"[CharacterEventManager: {gameObject.name}] <color=red>이벤트({eventType})가 송출되었으나 수신 대기 중인 리스너가 0명입니다! 매니저들이 구독을 잊었는지 확인하세요.</color>");
                return;
            }

            if (showConsoleLogs)
            {
                int count = OnAnimationEventTriggered.GetInvocationList().Length;
                Debug.Log($"[Event] <color=cyan>{gameObject.name}</color> -> 송출: <color=yellow>{eventType}</color> (현재 수신 대기 중인 매니저: {count}명)");
            }
        }

        // 씬 뷰(Scene View)에 머리 위 텍스트를 그려주는 유니티 내장 콜백
        private void OnDrawGizmos()
        {
            if (!showEventGizmo || recentEvents.Count == 0) return;

            // 만료 시간이 지난 이벤트는 리스트에서 삭제
            recentEvents.RemoveAll(e => Time.time > e.expirationTime);

            if (recentEvents.Count == 0) return;

            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.cyan;
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;

            // 캐릭터 머리 위 좌표 (캐릭터 기준 높이 +2m 지점, 이벤트가 쌓일수록 위로 올라가게 설정)
            Vector3 headPosition = transform.position + Vector3.up * 2.0f;

            for (int i = 0; i < recentEvents.Count; i++)
            {
                // 최신 이벤트가 아래로 깔리도록 Y축 오프셋을 줍니다.
                Vector3 drawPosition = headPosition + (Vector3.up * (i * 0.3f));
                Handles.Label(drawPosition, $"[{recentEvents[i].eventType}]", style);
            }
        }
        #endregion
#endif
    }
}