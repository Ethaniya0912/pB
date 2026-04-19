using UnityEngine;
using System;

namespace CaveSystem
{
    /// <summary>
    /// 개별 지대(Biome)의 기하학적 형태와 특성을 정의하는 독립적인 데이터 에셋입니다.
    /// 에디터에서 수정을 감지하여 실시간 핫리로드(Hot-Reload)를 지원합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCaveBiome", menuName = "Cave System/Biome Data")]
    public class CaveBiomeData : ScriptableObject
    {
        [Header("Biome Identity")]
        public string biomeName = "New Biome";

        [Header("Geometry Parameters (형태 제어)")]
        [Tooltip("0: 석회암/카르스트, 1: 주상절리, 2: 단층 압착/퇴적암")]
        public int noiseType = 0;

        [Tooltip("노이즈의 오밀조밀함. 값이 클수록 거칠고 좁게 파입니다.")]
        [Range(0.01f, 0.2f)] public float noiseFrequency = 0.05f;

        [Tooltip("Y축 압착률. 1.0 미만 시 지형이 위아래로 눌려 단층(Fault-line) 느낌을 줍니다.")]
        [Range(0.1f, 2.0f)] public float yCompression = 1.0f;

        [Tooltip("방과 통로가 융합되는 부드러움의 정도 (smin K값).")]
        [Range(0.1f, 10.0f)] public float sminStrength = 2.5f;

        [Tooltip("퇴적암 층리(Terrace) 단계. 0이면 사용하지 않으며, 높을수록 계단이 촘촘해집니다.")]
        [Range(0.0f, 10.0f)] public float terraceSteps = 0.0f;

        [Header("Floor Bumpiness (바닥 요철)")]
        [Tooltip("바닥 요철의 깊이 및 높이 진폭입니다.")]
        [Range(0.0f, 5.0f)] public float bumpAmplitude = 2.0f;

        [Tooltip("바닥 요철의 넓이 빈도입니다.")]
        [Range(0.01f, 0.2f)] public float bumpFrequency = 0.05f;

        [Header("Visual & Ecosystem")]
        public Material terrainMaterial;
        [Range(0f, 1f)] public float wetnessMultiplier = 0.0f;

        // 에디터 변경 감지 이벤트 (CaveComputeDispatcher가 구독)
        public static event Action OnBiomeModified;

        private void OnValidate()
        {
            // 플레이 모드 중에 수치를 변경하면 즉시 이벤트를 발생시켜 GPU 버퍼를 갱신합니다.
            if (Application.isPlaying)
            {
                OnBiomeModified?.Invoke();
            }
        }

        /// <summary>
        /// 참조 객체(Material 등)를 제외하고 GPU로 전송할 16바이트 정렬 순수 데이터만 패킹합니다.
        /// </summary>
        public BiomeParamData GetStructData()
        {
            return new BiomeParamData
            {
                noiseFrequency = this.noiseFrequency,
                yCompression = this.yCompression,
                sminStrength = this.sminStrength,
                terraceSteps = this.terraceSteps,
                bumpAmplitude = this.bumpAmplitude,
                bumpFrequency = this.bumpFrequency,
                noiseType = this.noiseType,
                padding = 0f // 32바이트 정렬 마감
            };
        }
    }
}