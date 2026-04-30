#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-D] PreviewAnimation — Editor Scene 반응성 시스템
    //
    // 구성:
    //   1. Easing  — easing function 카탈로그 (EaseOutCubic, EaseOutBack, etc.)
    //   2. AnimationState — 개별 애니메이션 진행 상태 추적
    //   3. AnimationCoordinator — EditorApplication.update에서 tick 일괄 처리
    //
    // 사용 패턴:
    //   var state = new AnimationState();
    //   AnimationCoordinator.Register(state);
    //   state.SetTarget(1.0f, 0.3f, Easing.EaseOutCubic);
    //   // 이후 state.currentValue 읽어서 렌더링
    //
    // 성능:
    //   - 애니메이션 없을 땐 SceneView.RepaintAll 호출 X (유휴 시 CPU 0%)
    //   - 애니메이션 5개 이하 시 ~1-2% CPU
    //   - 완료된 state는 자동으로 비활성 (Unregister는 수동)
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────
    // 1. Easing function 카탈로그
    // ──────────────────────────────────────────────────────────────
    public static class Easing
    {
        public static float Linear(float t) => t;

        public static float EaseOutCubic(float t)
        {
            float f = 1f - t;
            return 1f - f * f * f;
        }

        public static float EaseOutQuart(float t)
        {
            float f = 1f - t;
            return 1f - f * f * f * f;
        }

        public static float EaseInCubic(float t) => t * t * t;

        public static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);

        public static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        /// <summary>EaseOutBack — 살짝 overshoot ('pop' 효과)</summary>
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float f = t - 1f;
            return 1f + c3 * f * f * f + c1 * f * f;
        }

        /// <summary>EaseOutElastic — 튀는 느낌 (주의: 과도한 효과)</summary>
        public static float EaseOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c4 = (2f * Mathf.PI) / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 2. AnimationState — 개별 애니메이션 진행 상태
    // ──────────────────────────────────────────────────────────────
    public class AnimationState
    {
        public float currentValue;
        public float targetValue;
        public float startValue;
        public float startTime;
        public float duration;
        public System.Func<float, float> easing = Easing.EaseOutCubic;
        public float delay;  // 시작 전 지연 (stagger 효과용)

        public bool IsAnimating { get; private set; }

        public void SetTarget(float newTarget, float newDuration = 0.25f,
                              System.Func<float, float> easingFunc = null, float startDelay = 0f)
        {
            // 이미 같은 target이면 skip
            if (Mathf.Abs(newTarget - targetValue) < 0.001f && IsAnimating) return;
            // 같은 target이고 이미 끝나있으면 skip
            if (!IsAnimating && Mathf.Abs(newTarget - currentValue) < 0.001f) return;

            startValue = currentValue;
            targetValue = newTarget;
            startTime = (float)EditorApplication.timeSinceStartup + startDelay;
            duration = Mathf.Max(newDuration, 0.01f);
            delay = startDelay;
            if (easingFunc != null) easing = easingFunc;
            IsAnimating = true;
        }

        /// <summary>즉시 target으로 snap (애니메이션 없음).</summary>
        public void SnapTo(float value)
        {
            currentValue = value;
            targetValue = value;
            IsAnimating = false;
        }

        /// <summary>매 tick 호출. repaint 필요 여부 반환.</summary>
        public bool Tick()
        {
            if (!IsAnimating) return false;

            float now = (float)EditorApplication.timeSinceStartup;
            if (now < startTime) return true;  // delay 대기중

            float elapsed = now - startTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = easing(t);
            currentValue = Mathf.Lerp(startValue, targetValue, eased);

            if (t >= 1f)
            {
                currentValue = targetValue;
                IsAnimating = false;
            }
            return true;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 3. AnimationCoordinator — 전역 tick + SceneView repaint
    // ──────────────────────────────────────────────────────────────
    [InitializeOnLoad]
    public static class AnimationCoordinator
    {
        private static HashSet<AnimationState> _states = new HashSet<AnimationState>();
        private static bool _subscribed = false;

        static AnimationCoordinator()
        {
            EnsureSubscribed();
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            EditorApplication.update += OnEditorTick;
            _subscribed = true;
        }

        public static void Register(AnimationState state)
        {
            EnsureSubscribed();
            _states.Add(state);
        }

        public static void Unregister(AnimationState state)
        {
            _states.Remove(state);
        }

        public static void Clear() => _states.Clear();

        private static void OnEditorTick()
        {
            if (_states.Count == 0) return;

            bool anyAnimating = false;
            foreach (var state in _states)
            {
                if (state.Tick()) anyAnimating = true;
            }

            if (anyAnimating)
            {
                SceneView.RepaintAll();
            }
        }
    }
}
#endif
