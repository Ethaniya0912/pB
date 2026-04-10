// =============================================================================
// FactionWarbandSFXSO.cs  |  pB×pC 통합 — Week 1 (pC 오디오)
// Layer  : Data/SO
// Namespace: TDA.PB4.Audio
//
// 역할:
//   팩션별 근거리 감지 워밴드 SFX(Pattern 2) 데이터 정의.
//   고블린(찢어지는 뿔피리), 오크(전쟁 뿔나팔), 스켈레톤(뼈 경적).
//   Tier 횟수만큼 반복 재생: Tier 3이면 '삐이잇! 삐이잇! 삐이잇!' 3회.
// =============================================================================
using UnityEngine;

namespace TDA.PB4.Audio
{
    [CreateAssetMenu(menuName = "pB4/Audio/FactionWarbandSFX")]
    public class FactionWarbandSFXSO : ScriptableObject
    {
        [Header("━━━ 팩션 식별 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("팩션 고유 ID. FactionAmbientSFXSO와 동일한 ID.")]
        public string factionId;

        [Header("━━━ 워밴드 클립 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("1회 재생 단위 AudioClip. " +
                 "고블린: 뿔피리 한 마디. 오크: 뿔나팔 한 번. 스켈레톤: 뼈 경적. " +
                 "이 클립이 Tier 횟수만큼 반복됨.")]
        public AudioClip warbandClip;

        [Header("━━━ 반복 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("반복 간격 (초). 0.3 = 워밴드 사운드 간 0.3초 간격. " +
                 "Tier 3이면: 재생→0.3초→재생→0.3초→재생.")]
        [Range(0.1f, 1f)]
        public float tierRepeatInterval = 0.3f;

        [Header("━━━ 명료도 설정 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("직접음 비율 (0~1). 0.8 = 80%가 직접음, 20%가 잔향. " +
                 "Pattern 1(원거리)과 달리 Pattern 2는 명료하게 들려야 하므로 0.8+ 권장.")]
        [Range(0.5f, 1f)]
        public float directSoundRatio = 0.8f;

        [Tooltip("Pattern 1 즉시 중단 여부. true = Pattern 2 재생 시 Pattern 1을 즉시 멈춤. " +
                 "보통 true. 워밴드 사운드가 환경 소음을 덮어야 자연스러움.")]
        public bool priorityOverAmbient = true;
    }
}
