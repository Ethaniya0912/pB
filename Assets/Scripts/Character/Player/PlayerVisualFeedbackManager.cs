using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TDA.Core.Events;

namespace TDA.Character.Player
{
    /// <summary>
    /// [Derived Class] 셰이더 연동 및 URP Post-Processing 볼륨을 실시간 조작하여
    /// 바디캠 피드백과 데미지 패닉 상태를 100% 렌더링합니다.
    /// </summary>
    public class PlayerVisualFeedbackManager : CharacterVisualFeedbackManager
    {
        private PlayerManager _player;
        private PlayerInputManager _inputManager;

        [Header("Step Response Settings")]
        [Range(0f, 1f)] public float sprintThreshold = 0.8f;
        [Range(0.1f, 15f)] public float accelerationResponse = 10f;
        [Range(0.1f, 10f)] public float recoveryResponse = 1.2f;

        [Header("Rhythm (Pulse)")]
        [Range(0.1f, 10f)] public float walkPulseFreq = 2.0f;
        [Range(0.1f, 20f)] public float sprintPulseFreq = 5.0f;
        [Range(0f, 0.5f)] public float pulseIntensity = 0.05f;

        [Header("Optimization & Anti-Ghosting")]
        [Range(0f, 0.1f)] public float deadzoneThreshold = 0.005f;
        public float idleCutoffSpeed = 2.0f;

        [Header("Performance Control")]
        public bool showDebugLogs = false;
        private float _smoothedSpeedFactor;

        [Header("Post-Processing Feedback (P4)")]
        public Volume postProcessVolume;
        private Vignette vignetteEffect;
        private LensDistortion lensDistortionEffect;
        private float defaultVignetteIntensity;
        private Coroutine damagePanicCoroutine;

        protected override void Awake()
        {
            base.Awake();
            _player = GetComponent<PlayerManager>();
            _inputManager = PlayerInputManager.Instance;

            if (postProcessVolume != null && postProcessVolume.profile != null)
            {
                if (postProcessVolume.profile.TryGet(out vignetteEffect))
                {
                    defaultVignetteIntensity = vignetteEffect.intensity.value;
                }
                postProcessVolume.profile.TryGet(out lensDistortionEffect);
            }
        }

        protected override void Update()
        {
            if (_player == null || !_player.IsOwner) return;

            base.Update();
            HandlePlayerVisualResponse();
        }

        public override void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            base.OnAnimationEventReceived(eventType);

            if (_player == null || !_player.IsOwner) return;

            if (eventType == global::AnimationEventType.Damage_Calculated)
            {
                if (damagePanicCoroutine != null) StopCoroutine(damagePanicCoroutine);
                if (gameObject.activeInHierarchy)
                {
                    damagePanicCoroutine = StartCoroutine(DamagePanicRoutine());
                }
            }
        }

        private System.Collections.IEnumerator DamagePanicRoutine()
        {
            if (vignetteEffect == null) yield break;

            float duration = 0.5f;
            float timer = 0f;
            float targetIntensity = 0.45f;
            Color targetColor = new Color(0.8f, 0f, 0f);
            Color defaultColor = Color.black;

            while (timer < 0.1f)
            {
                timer += Time.deltaTime;
                float t = timer / 0.1f;
                vignetteEffect.intensity.value = Mathf.Lerp(defaultVignetteIntensity, targetIntensity, t);
                vignetteEffect.color.value = Color.Lerp(defaultColor, targetColor, t);

                if (lensDistortionEffect != null) lensDistortionEffect.intensity.value = Mathf.Lerp(0, -0.3f, t);
                yield return null;
            }

            timer = 0f;

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = timer / duration;
                vignetteEffect.intensity.value = Mathf.Lerp(targetIntensity, defaultVignetteIntensity, t);
                vignetteEffect.color.value = Color.Lerp(targetColor, defaultColor, t);

                if (lensDistortionEffect != null) lensDistortionEffect.intensity.value = Mathf.Lerp(-0.3f, 0, t);
                yield return null;
            }

            vignetteEffect.intensity.value = defaultVignetteIntensity;
            vignetteEffect.color.value = defaultColor;
            if (lensDistortionEffect != null) lensDistortionEffect.intensity.value = 0f;
        }

        private void HandlePlayerVisualResponse()
        {
            if (_inputManager == null)
            {
                _inputManager = PlayerInputManager.Instance;
                if (_inputManager == null) return;
            }

            float inputAmount = _inputManager.moveAmount;
            float rawTarget = currentSpeedFactor * inputAmount;
            float targetFactor = (inputAmount > 0.6f) ? rawTarget : rawTarget * 0.3f;
            float lerpSpeed = targetFactor > _smoothedSpeedFactor ? accelerationResponse : recoveryResponse;

            if (inputAmount < 0.01f) lerpSpeed *= idleCutoffSpeed;

            _smoothedSpeedFactor = Mathf.Lerp(_smoothedSpeedFactor, targetFactor, Time.deltaTime * lerpSpeed);

            if (_smoothedSpeedFactor < deadzoneThreshold) _smoothedSpeedFactor = 0f;

            float pulse = 0f;
            if (_smoothedSpeedFactor > 0f)
            {
                bool isSprintingState = _smoothedSpeedFactor > sprintThreshold;
                float currentFreq = isSprintingState ? sprintPulseFreq : walkPulseFreq;
                pulse = Mathf.Sin(Time.time * currentFreq) * pulseIntensity * _smoothedSpeedFactor;
            }

            if (showDebugLogs)
            {
                string stateName = (inputAmount > 0.6f) ? "질주" : (inputAmount > 0.1f) ? "보행" : "정지";
                Debug.Log($"[PVFM {stateName}] 수치: {_smoothedSpeedFactor:F3} | 입력: {inputAmount:F1}");
            }

            Shader.SetGlobalFloat("_GlobalSpeedFactor", _smoothedSpeedFactor);
            Shader.SetGlobalFloat("_GlobalMovementPulse", pulse);
            Shader.SetGlobalFloat("_VFXMipBias", Mathf.Lerp(0f, -2.0f, _smoothedSpeedFactor));
        }
    }
}