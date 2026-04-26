// =============================================================================
// DialoguePresetSO.cs  |  pB-4 Week 2 Day 4
// Layer  : Data / SO (순수 데이터 — UI 스타일)
// Owner  : Person A
//
// 역할:
//   SpeechTone 별로 말풍선 UI 스타일(색상/지속시간/페이드)을 매핑.
//   DialogueRenderer가 Tone에 따라 Preset 선택.
//
// 4계층 원칙:
//   - [Rule 2] 하드코딩 금지 — 색상/시간 값은 SO에서.
//   - Designer가 Inspector에서 조율 가능.
// =============================================================================
using UnityEngine;

namespace TDA.PB4.AI.Speech
{
    /// <summary>Tone 별 말풍선 UI 스타일.</summary>
    [CreateAssetMenu(menuName = "pB-4/Speech/DialoguePreset", fileName = "DialoguePreset_")]
    public class DialoguePresetSO : ScriptableObject
    {
        [Header("━━━ 매칭 조건 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 Preset이 적용되는 Tone.")]
        public SpeechTone tone = SpeechTone.Neutral;

        [Header("━━━ 시각 스타일 ━━━━━━━━━━━━━━━━")]
        [Tooltip("텍스트 색상.")]
        public Color fontColor = Color.white;

        [Tooltip("말풍선 배경 색상 (알파 포함).")]
        public Color backgroundColor = new Color(0f, 0f, 0f, 0.75f);

        [Tooltip("테두리 색상.")]
        public Color borderColor = new Color(1f, 1f, 1f, 0.5f);

        [Tooltip("폰트 크기.")]
        [Range(8f, 48f)]
        public float fontSize = 16f;

        [Header("━━━ 타이밍 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("페이드 인 시간(초).")]
        [Range(0f, 2f)] public float fadeInDuration = 0.2f;

        [Tooltip("표시 지속 시간(초). 자동 계산이 비활성일 때 사용되는 고정값. " +
                 "TemplateVariant.durationOverride > 0 → 그 값 우선.")]
        [Range(0.5f, 10f)] public float displayDuration = 2.5f;

        [Tooltip("자동 계산 사용 여부. true면 텍스트 길이 기반으로 표시 시간 계산. " +
                 "false면 displayDuration 고정값 사용.")]
        public bool autoCalcDuration = true;

        [Tooltip("읽기 속도 — 글자당 초. 기본 0.12s/char (한글 평균 250자/분 = 0.24s/char ÷ 2 빨리). " +
                 "Tone별로 차등: Aggressive는 빠르게(0.08), Anxious는 느리게(0.15).")]
        [Range(0.04f, 0.3f)] public float secondsPerChar = 0.12f;

        [Tooltip("자동 계산된 시간에 곱하는 배율. 1.0 = 그대로, 1.5 = 50% 더 길게. " +
                 "Tone 별 미세조정용.")]
        [Range(0.5f, 3f)] public float durationScale = 1.0f;

        [Tooltip("자동 계산 시 최소 표시 시간(초). 짧은 대사도 너무 빨리 사라지지 않게.")]
        [Range(0.5f, 3f)] public float minDuration = 1.0f;

        [Tooltip("자동 계산 시 최대 표시 시간(초). 긴 대사도 너무 오래 머무르지 않게.")]
        [Range(2f, 15f)] public float maxDuration = 8.0f;

        [Tooltip("페이드 아웃 시간(초).")]
        [Range(0f, 2f)] public float fadeOutDuration = 0.5f;

        [Header("━━━ 연출 (선택) ━━━━━━━━━━━━━━━━")]
        [Tooltip("Tone 별 shake 강도. 0 = 없음. Anxious 톤에서 활용.")]
        [Range(0f, 2f)] public float shakeAmplitude = 0f;

        [Tooltip("표시 중 가로폭(워드 스페이스 단위).")]
        [Range(1f, 10f)] public float bubbleWidth = 4f;
    }
}