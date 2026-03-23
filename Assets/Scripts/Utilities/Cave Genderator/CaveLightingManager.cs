// ================================================================================
// 파일: CaveLightingManager.cs  (수정)
// 변경 내역:
//   · RenderSettings.ambientMode / ambientLight / ambientIntensity 를
//     씬 진입 시 동굴용(완전 암흑)으로 설정하고 씬 퇴장 시 복원
//   · RenderSettings.reflectionIntensity 도 동일하게 제어
//   · 원래 값 보존 → OnDestroy 에서 복원하여 다른 씬에 영향 없음
// ================================================================================

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace CaveSystem
{
    /// <summary>
    /// 동굴 내부의 커스텀 볼류메트릭 포그(Froxel)와 싱크홀 조명을 연동하고,
    /// 동굴 환경에 맞게 Ambient(SH)와 Reflection 간접광을 완전히 제거합니다.
    /// </summary>
    public class CaveLightingManager : MonoBehaviour
    {
        [Header("References")]
        public GameObject sinkholeLightPrefab;

        [Header("Optimization Settings")]
        public bool enableFarClipOptimization = true;
        public float optimalFarClipDistance = 50.0f;
        public float defaultFarClipDistance = 1000.0f;

        [Header("Lighting Settings")]
        public float lightIntensity = 5000f;

        [Header("Froxel Volumetric Fog Settings")]
        public Color caveFogColor = new Color(0.01f, 0.02f, 0.03f);
        public float caveFogDensity = 1.0f;

        // ── [신규] 동굴 Ambient 제어 ──────────────────────────
        [Header("Cave Ambient Settings")]
        [Tooltip("동굴 Ambient(SH) 완전 제거 여부. 동굴은 하늘빛이 없어야 하므로 기본 true.")]
        public bool killCaveAmbient = true;

        [Tooltip("동굴 Environment Reflection(Indirect Specular) 제거 여부.")]
        public bool killCaveReflection = true;

        [Tooltip("killCaveAmbient=false 일 때 사용할 Ambient 밝기 (0=완전 암흑).")]
        [Range(0f, 1f)]
        public float caveAmbientIntensity = 0f;

        // 원래 값 보존 (씬 전환 시 복원용)
        private AmbientMode originalAmbientMode;
        private Color originalAmbientLight;
        private float originalAmbientIntensity;
        private float originalReflectionIntensity;
        private bool ambientOverrideApplied = false;

        private List<GameObject> activeLights = new List<GameObject>();

        // ────────────────────────────────────────────────────
        private void Start()
        {
            InitializeVolumeOverrides();
        }

        private void InitializeVolumeOverrides()
        {
            // ── 1. Ambient / Reflection 원래 값 보존 후 동굴용으로 교체 ──
            if (killCaveAmbient || killCaveReflection)
            {
                originalAmbientMode = RenderSettings.ambientMode;
                originalAmbientLight = RenderSettings.ambientLight;
                originalAmbientIntensity = RenderSettings.ambientIntensity;
                originalReflectionIntensity = RenderSettings.reflectionIntensity;
                ambientOverrideApplied = true;

                if (killCaveAmbient)
                {
                    // Ambient Source를 단색(Flat)으로 변경 후 완전 검정 설정
                    // → SampleSH() = 0 → Indirect GI 기여 완전 제거
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = Color.black;
                    RenderSettings.ambientIntensity = caveAmbientIntensity;
                }

                if (killCaveReflection)
                {
                    // GlossyEnvironmentReflection 에 전달되는 강도를 0으로 설정
                    // → 하늘/Skybox 반사광 완전 차단
                    RenderSettings.reflectionIntensity = 0f;
                }
            }

            // ── 2. 카메라 Far Clip 최적화 ──────────────────────
            if (Camera.main != null)
            {
                if (enableFarClipOptimization)
                {
                    Camera.main.farClipPlane = optimalFarClipDistance;
                    Camera.main.backgroundColor = caveFogColor;
                    Camera.main.clearFlags = CameraClearFlags.SolidColor;
                    Shader.SetGlobalFloat("_MaxDist", optimalFarClipDistance);
                }
                else
                {
                    Camera.main.farClipPlane = defaultFarClipDistance;
                    Camera.main.clearFlags = CameraClearFlags.Skybox;
                    Shader.SetGlobalFloat("_MaxDist", defaultFarClipDistance);
                }
            }

            // ── 3. Froxel 포그 전역 변수 주입 ──────────────────
            Shader.SetGlobalFloat("_FogDensity", caveFogDensity);
            Shader.SetGlobalColor("_GlobalAmbientColor", caveFogColor);
        }

        /// <summary>
        /// 날씨/깊이 변화에 따라 포그 속성을 동적으로 갱신합니다.
        /// </summary>
        public void UpdateAtmosphere(Color targetFogColor, float targetDensity)
        {
            caveFogColor = Color.Lerp(caveFogColor, targetFogColor, Time.deltaTime);
            caveFogDensity = Mathf.Lerp(caveFogDensity, targetDensity, Time.deltaTime);

            Shader.SetGlobalColor("_GlobalAmbientColor", caveFogColor);
            Shader.SetGlobalFloat("_FogDensity", caveFogDensity);

            if (Camera.main != null && enableFarClipOptimization)
                Camera.main.backgroundColor = caveFogColor;
        }

        /// <summary>
        /// 싱크홀(천장 뚫린 곳) 좌표에 빛내림용 SpotLight를 배치합니다.
        /// </summary>
        public void SpawnSinkholeLights(List<Vector3> sinkholePositions)
        {
            foreach (Vector3 pos in sinkholePositions)
            {
                Vector3 spawnPos = pos + new Vector3(0, 20f, 0);
                GameObject lightObj = Instantiate(
                    sinkholeLightPrefab, spawnPos, Quaternion.Euler(90f, 0, 0), this.transform);

                Light spotLight = lightObj.GetComponent<Light>();
                if (spotLight != null)
                {
                    spotLight.type = LightType.Spot;
                    spotLight.intensity = lightIntensity;
                    spotLight.range = 50f;
                    spotLight.innerSpotAngle = 20f;
                    spotLight.spotAngle = 45f;
                    spotLight.shadows = LightShadows.Soft;
                }

                activeLights.Add(lightObj);
            }
        }

        private void OnDestroy()
        {
            // ── Ambient / Reflection 원래 값 복원 ──────────────
            // 씬 전환 시 다른 씬(지상 등)의 조명이 망가지지 않도록 복원
            if (ambientOverrideApplied)
            {
                RenderSettings.ambientMode = originalAmbientMode;
                RenderSettings.ambientLight = originalAmbientLight;
                RenderSettings.ambientIntensity = originalAmbientIntensity;
                RenderSettings.reflectionIntensity = originalReflectionIntensity;
            }

            // SpotLight 정리
            foreach (var light in activeLights)
                if (light != null) Destroy(light);
            activeLights.Clear();
        }
    }
}