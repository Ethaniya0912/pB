// =============================================================================
// FactionAmbientSFXSO.cs  |  pB×pC 통합 — Week 1 (pC 오디오)
// Layer  : Data/SO
// Namespace: TDA.PB4.Audio
//
// 역할:
//   팩션별 원거리 감지 SFX(Pattern 1)의 데이터 정의.
//   고블린(찰랑거리는 갑옷+낄낄거림), 오크(묵직한 발걸음+중얼거림),
//   스켈레톤(뼈 덜그럭+금속 긁힘).
//   Tier별 레이어: Tier 높을수록 소리가 복합적.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Audio
{
    /// <summary>Tier별 SFX 레이어. Tier가 높을수록 더 많은 레이어가 활성화.</summary>
    [Serializable]
    public class TierSFXLayer
    {
        [Tooltip("이 레이어가 활성화되는 최소 Tier 레벨. " +
                 "1=Tier 1부터(발걸음), 2=Tier 2부터(+무기), 3=Tier 3부터(+음성).")]
        public int tierLevel = 1;

        [Tooltip("이 레이어의 AudioClip 목록. 랜덤 선택됨. " +
                 "예: Tier 1 = [footstep_01, footstep_02, footstep_03]")]
        public AudioClip[] clips;

        [Tooltip("이 레이어의 볼륨 배율. 0.5 = 50% 볼륨.")]
        [Range(0f, 1f)]
        public float layerVolume = 0.7f;
    }

    [CreateAssetMenu(menuName = "pB4/Audio/FactionAmbientSFX")]
    public class FactionAmbientSFXSO : ScriptableObject
    {
        [Header("━━━ 팩션 식별 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("팩션 고유 ID. FactionDetectionSFXManager에서 매칭에 사용. " +
                 "MobFactionDataSO의 factionId와 동일해야 함. 예: 'goblin', 'orc', 'skeleton'")]
        public string factionId;

        [Header("━━━ Tier별 레이어 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Tier별 SFX 레이어 목록. " +
                 "Tier 1: 발걸음만. Tier 2: 발걸음+무기. Tier 3: +간헐적 음성. " +
                 "Tier 4~5: +전투 북/드럼. 현재 팩션의 Tier 이하의 모든 레이어가 동시 재생.")]
        public List<TierSFXLayer> tierLayers = new List<TierSFXLayer>();

        [Header("━━━ 거리별 파라미터 ━━━━━━━━━━━━━━━━━")]
        [Tooltip("거리→볼륨 커브. X=정규화 거리(0=0m, 1=100m), Y=볼륨(0~1). " +
                 "가까울수록 볼륨 높음. 기본: 15m에서 0.8, 50m에서 0.3, 80m에서 0.05.")]
        public AnimationCurve distanceCurve = AnimationCurve.EaseInOut(0f, 0.8f, 1f, 0.05f);

        [Tooltip("거리→재생 간격 커브. X=정규화 거리, Y=간격(초). " +
                 "가까울수록 빈번. 기본: 15m에서 0.5초, 50m에서 2초, 80m에서 5초.")]
        public AnimationCurve intervalCurve = AnimationCurve.EaseInOut(0f, 0.5f, 1f, 5f);

        [Tooltip("거리→잔향 비율 커브. X=정규화 거리, Y=잔향(0~1). " +
                 "멀수록 잔향 많음(울림). 기본: 15m에서 0.2, 50m에서 0.7, 80m에서 0.99.")]
        public AnimationCurve reverbMix = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 0.99f);

        [Header("━━━ 클립 길이 범위 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("재생 클립 길이 범위 (초). X=최소, Y=최대. " +
                 "랜덤으로 이 범위 내에서 클립 재생 시간 결정.")]
        public Vector2 clipDurationRange = new Vector2(0.3f, 1.5f);
    }
}
