using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace CaveSystem
{
    /// <summary>
    /// 동굴 내부의 커스텀 볼류메트릭 포그(Froxel)와 싱크홀 조명을 연동하여
    /// 완벽한 빛내림(God Rays) 효과와 가시거리 최적화를 제어하는 매니저입니다.
    /// </summary>
    public class CaveLightingManager : MonoBehaviour
    {
        [Header("References")]
        public GameObject sinkholeLightPrefab; // SpotLight와 먼지 파티클이 포함된 프리팹

        // [최적화 Opt 2: Far Clip 토글 및 Light 배치]
        [Header("Optimization Settings")]
        public bool enableFarClipOptimization = true;
        public float optimalFarClipDistance = 50.0f;
        public float defaultFarClipDistance = 1000.0f;

        [Header("Lighting Settings")]
        public float lightIntensity = 5000f;

        // 제공해주신 FroxelAtmosphereFeature 시스템과 직접 연동되는 설정
        [Header("Froxel Volumetric Fog Settings")]
        public Color caveFogColor = new Color(0.01f, 0.02f, 0.03f); // 심연의 흑암색
        public float caveFogDensity = 1.0f;

        private List<GameObject> activeLights = new List<GameObject>();

        private void Start()
        {
            InitializeVolumeOverrides();
        }

        /// <summary>
        /// URP 기본 Volume 대신 Froxel 글로벌 셰이더 변수를 제어하여
        /// 카메라 최적화와 포그 거리를 완벽하게 동기화합니다.
        /// </summary>
        private void InitializeVolumeOverrides()
        {
            if (Camera.main != null)
            {
                if (enableFarClipOptimization)
                {
                    // 1. 카메라 Far Clip Plane을 깎아내어 지오메트리 렌더링 강제 차단
                    Camera.main.farClipPlane = optimalFarClipDistance;
                    Camera.main.backgroundColor = caveFogColor;
                    Camera.main.clearFlags = CameraClearFlags.SolidColor;

                    // 2. Froxel 포그의 최대 렌더 거리를 Far Clip과 일치시켜 절단면을 숨김 (_MaxDistID 연동)
                    Shader.SetGlobalFloat("_MaxDist", optimalFarClipDistance);
                }
                else
                {
                    // 최적화 OFF 시 기본값 복구
                    Camera.main.farClipPlane = defaultFarClipDistance;
                    Camera.main.clearFlags = CameraClearFlags.Skybox;

                    Shader.SetGlobalFloat("_MaxDist", defaultFarClipDistance);
                }
            }

            // 3. Froxel 포그 전역 밀도 및 색상 주입 (_FogDensityID, _GlobalAmbientColorID 연동)
            Shader.SetGlobalFloat("_FogDensity", caveFogDensity);
            Shader.SetGlobalColor("_GlobalAmbientColor", caveFogColor);
        }

        /// <summary>
        /// Phase 1/6에서 파악된 싱크홀(천장 뚫린 곳) 좌표에 빛내림용 조명을 배치합니다.
        /// </summary>
        public void SpawnSinkholeLights(List<Vector3> sinkholePositions)
        {
            foreach (Vector3 pos in sinkholePositions)
            {
                // 싱크홀 Y축 기준 20m 위쪽에서 바닥을 향해 정확히 수직(Euler 90, 0, 0)으로 쏘는 조명 배치
                Vector3 spawnPos = pos + new Vector3(0, 20f, 0);

                GameObject lightObj = Instantiate(sinkholeLightPrefab, spawnPos, Quaternion.Euler(90f, 0, 0), this.transform);
                Light spotLight = lightObj.GetComponent<Light>();

                if (spotLight != null)
                {
                    spotLight.type = LightType.Spot;
                    // 빛내림이 Froxel 시스템과 강하게 반응할 수 있도록 설정
                    spotLight.intensity = lightIntensity;
                    spotLight.range = 50f;
                    spotLight.innerSpotAngle = 20f;
                    spotLight.spotAngle = 45f;
                    spotLight.shadows = LightShadows.Soft;

                    // 사용자의 Froxel 시스템은 _LightBuffer로 광원을 자동 수집하므로 별도 컴포넌트 불필요
                }

                activeLights.Add(lightObj);
            }
        }

        /// <summary>
        /// 날씨/시간이나 동굴의 깊이에 따라 포그 색상과 밀도를 동적으로 바꿀 때 사용하는 API
        /// </summary>
        public void UpdateAtmosphere(Color targetFogColor, float targetDensity)
        {
            caveFogColor = Color.Lerp(caveFogColor, targetFogColor, Time.deltaTime);
            caveFogDensity = Mathf.Lerp(caveFogDensity, targetDensity, Time.deltaTime);

            Shader.SetGlobalColor("_GlobalAmbientColor", caveFogColor);
            Shader.SetGlobalFloat("_FogDensity", caveFogDensity);

            if (Camera.main != null && enableFarClipOptimization)
            {
                Camera.main.backgroundColor = caveFogColor;
            }
        }

        private void OnDestroy()
        {
            foreach (var light in activeLights)
            {
                if (light != null) Destroy(light);
            }
            activeLights.Clear();
        }
    }
}