// =============================================================================
// FactionDetectionSFXManager.cs  |  pB×pC 통합 — Week 1 (pC 오디오)
// Layer  : L4 Event/Coordination
// Namespace: TDA.PB4.Audio
//
// 역할:
//   팩션 그룹 단위의 감지 SFX를 관리한다. (1.20 전면 준수)
//   GroupAIManager의 OnFactionDetectedPlayer 이벤트를 구독하여
//   Pattern 1(원거리 환경 소음) 또는 Pattern 2(근거리 워밴드) 판별 및 재생.
//
//   Pattern 1 — 원거리: Steam Audio 잔향+거리별 4단계 감쇠
//   Pattern 2 — 근거리: 워밴드 SFX Tier 횟수 반복, 직접음 80%+
//
//   기존 AICharacterSoundFxManager(개별 AI)와 독립 동작. 교차 참조 없음.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Audio
{
    public class FactionDetectionSFXManager : MonoBehaviour
    {
        [Header("━━━ SFX 프로파일 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("팩션별 원거리 환경 SFX 프로파일 목록. " +
                 "각 SO에는 factionId, tierLayers, distanceCurve, intervalCurve, reverbMix 정의.")]
        [SerializeField] private List<FactionAmbientSFXSO> ambientProfiles = new List<FactionAmbientSFXSO>();

        [Tooltip("팩션별 근거리 워밴드 SFX 프로파일 목록. " +
                 "각 SO에는 factionId, warbandClip, tierRepeatInterval 정의.")]
        [SerializeField] private List<FactionWarbandSFXSO> warbandProfiles = new List<FactionWarbandSFXSO>();

        [Header("━━━ AudioSource 설정 ━━━━━━━━━━━━━━━━")]
        [Tooltip("전용 AudioSource. SteamAudioSource 컴포넌트도 부착되어야 함. " +
                 "비어있으면 자동 생성.")]
        [SerializeField] private AudioSource sfxAudioSource;

        [Header("━━━ 판정 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("프러스텀 판정 주기 (초). 0.2 = 초당 5회 카메라 시야 내 체크.")]
        [Range(0.1f, 1f)]
        public float frustumCheckInterval = 0.2f;

        [Tooltip("동일 팩션 워밴드(Pattern 2) 재생 쿨다운 (초). " +
                 "10 = 같은 팩션은 10초에 1회만 워밴드 재생.")]
        [Range(3f, 30f)]
        public float warbandCooldown = 10f;

        [Header("━━━ 현재 상태 (Read Only) ━━━━━━━━━━━━")]
        [SerializeField] private string activePattern = "None";
        [SerializeField] private string activeFactionId = "";
        [SerializeField] private float currentDistance;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private Dictionary<string, float> warbandCooldowns = new Dictionary<string, float>();
        private Camera mainCamera;
        private Coroutine ambientCoroutine;

        private void Awake()
        {
            if (sfxAudioSource == null)
            {
                sfxAudioSource = gameObject.AddComponent<AudioSource>();
                sfxAudioSource.spatialBlend = 1f; // 3D
                sfxAudioSource.playOnAwake = false;
            }
            mainCamera = Camera.main;
        }

        // ==================================================================
        // 이벤트 핸들러: 팩션이 유저를 감지했을 때
        // ==================================================================

        /// <summary>
        /// GroupAIManager 또는 AIPerceptionSystem에서 호출.
        /// 팩션이 플레이어를 감지했을 때 Pattern 1/2 판별 후 SFX 재생.
        /// </summary>
        public void OnFactionDetectedPlayer(string factionId, int tier, Vector3 groupPos, float distance)
        {
            activeFactionId = factionId;
            currentDistance = distance;

            // 프러스텀 판정: 카메라 시야 내인지 체크
            bool inFrustum = CheckFrustum(groupPos);

            if (inFrustum)
            {
                // Pattern 2: 근거리 워밴드
                PlayPattern2(factionId, tier, groupPos);
            }
            else
            {
                // Pattern 1: 원거리 환경 소음
                PlayPattern1(factionId, tier, groupPos, distance);
            }
        }

        // ==================================================================
        // Pattern 1: 원거리 환경 소음
        // ==================================================================
        private void PlayPattern1(string factionId, int tier, Vector3 position, float distance)
        {
            var profile = ambientProfiles.Find(p => p != null && p.factionId == factionId);
            if (profile == null) return;

            activePattern = $"P1 ({factionId}, {distance:F0}m)";

            // 거리별 볼륨/간격/잔향 계산
            float normalizedDist = Mathf.Clamp01(distance / 100f);
            float volume = profile.distanceCurve.Evaluate(normalizedDist);
            float interval = profile.intervalCurve.Evaluate(normalizedDist);
            float reverb = profile.reverbMix.Evaluate(normalizedDist);

            // Tier별 클립 선택
            AudioClip clip = null;
            foreach (var layer in profile.tierLayers)
            {
                if (layer.tierLevel <= tier && layer.clips != null && layer.clips.Length > 0)
                    clip = layer.clips[UnityEngine.Random.Range(0, layer.clips.Length)];
            }

            if (clip != null)
            {
                sfxAudioSource.transform.position = position;
                sfxAudioSource.volume = volume;
                sfxAudioSource.reverbZoneMix = reverb;
                sfxAudioSource.PlayOneShot(clip);
            }

            if (debugLog)
                Debug.Log($"[SFX] Pattern 1: {factionId} T{tier} dist={distance:F0}m vol={volume:F2} interval={interval:F1}s reverb={reverb:F2}");
        }

        // ==================================================================
        // Pattern 2: 근거리 워밴드
        // ==================================================================
        private void PlayPattern2(string factionId, int tier, Vector3 position)
        {
            // 쿨다운 체크
            if (warbandCooldowns.TryGetValue(factionId, out float lastTime))
                if (Time.time - lastTime < warbandCooldown) return;
            warbandCooldowns[factionId] = Time.time;

            var profile = warbandProfiles.Find(p => p != null && p.factionId == factionId);
            if (profile == null || profile.warbandClip == null) return;

            activePattern = $"P2 ({factionId}, T{tier}×{tier}회)";

            // 기존 Pattern 1 중단
            if (ambientCoroutine != null) StopCoroutine(ambientCoroutine);

            // Tier 횟수만큼 반복 재생
            StartCoroutine(PlayWarbandRepeated(profile, tier, position));

            if (debugLog)
                Debug.Log($"[SFX] Pattern 2: {factionId} T{tier} → 워밴드 {tier}회 반복 (직접음={profile.directSoundRatio:P0})");
        }

        private IEnumerator PlayWarbandRepeated(FactionWarbandSFXSO profile, int tier, Vector3 position)
        {
            sfxAudioSource.transform.position = position;
            sfxAudioSource.volume = 0.9f; // 명료하게
            sfxAudioSource.reverbZoneMix = 1f - profile.directSoundRatio; // 직접음 80%+

            for (int i = 0; i < tier; i++)
            {
                sfxAudioSource.PlayOneShot(profile.warbandClip);
                yield return new WaitForSeconds(profile.tierRepeatInterval);
            }
        }

        // ==================================================================
        // 프러스텀 판정
        // ==================================================================
        private bool CheckFrustum(Vector3 worldPosition)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return false;

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
            Bounds bounds = new Bounds(worldPosition, Vector3.one * 2f);
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }
    }
}
