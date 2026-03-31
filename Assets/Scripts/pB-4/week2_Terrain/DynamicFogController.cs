// =============================================================================
// DynamicFogController.cs  |  pB-4 Project — Week 2
// Layer  : L4 Event (시각 연출)
// Namespace: TDA.PB4.Terrain
//
// 역할:
//   GameBlackboard.globalIntensityScore를 실시간으로 읽어
//   Unity의 안개(Fog) 색상과 농도를 동적으로 변경한다.
//   긴장도가 높으면 안개가 붉어지고 짙어지며,
//   긴장도가 낮으면 안개가 옅어지고 회색으로 돌아온다.
//
//   이 컴포넌트가 L4 Event 계층인 이유:
//   안개 변경은 순수 '시각적 연출'이며 게임 로직(L3 Domain)에 속하지 않는다.
//   긴장도 '계산'은 HNGSIntensityController(L3)가 담당하고,
//   긴장도 '표현'은 이 컴포넌트(L4)가 담당하는 책임 분리.
//
//   지형 폐쇄도 태그에 따라:
//   - NarrowPath/SpookyCave → 안개가 붉은색으로 전환
//   - 태그 없음/WideOpen → 기본 회색 안개
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Terrain
{
    public class DynamicFogController : MonoBehaviour
    {
        [Header("━━━ 안개 색상 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("긴장도 0.0일 때의 안개 색상. 기본: 짙은 회색 (평화 상태)")]
        public Color calmFogColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        [Tooltip("긴장도 1.0일 때의 안개 색상. 기본: 핏빛 붉은색 (극한 위험)")]
        public Color tenseFogColor = new Color(0.35f, 0.05f, 0.05f, 1f);

        [Tooltip("지형 태그 'NarrowPath' 또는 'SpookyCave'가 활성화되었을 때의 안개 색상. " +
                 "좁은 통로에 진입하면 긴장도와 무관하게 이 색상으로 즉시 전환된다.")]
        public Color narrowPathFogColor = new Color(0.3f, 0.08f, 0.08f, 1f);

        [Header("━━━ 안개 농도 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("긴장도 0.0일 때의 안개 농도. 0.01=거의 투명")]
        [Range(0f, 0.1f)] public float calmFogDensity = 0.01f;

        [Tooltip("긴장도 1.0일 때의 안개 농도. 0.08=매우 짙음. " +
                 "Week 0 마일스톤: 긴장도 0.8 이상 시 안개 농도 변화 확인.")]
        [Range(0f, 0.1f)] public float tenseFogDensity = 0.06f;

        [Header("━━━ 전환 속도 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("안개 색상/농도가 목표값으로 전환되는 속도. " +
                 "1.0=즉시 전환, 0.1=10초에 걸쳐 서서히 전환. " +
                 "서서히 전환하면 '슬금슬금 공포가 밀려오는' 연출 가능.")]
        [Range(0.05f, 5f)] public float transitionSpeed = 1.5f;

        [Header("━━━ 태그 기반 오버라이드 ━━━━━━━━━━━━━")]
        [Tooltip("이 태그 중 하나라도 활성화되면 narrowPathFogColor로 전환. " +
                 "비어있으면 기본 태그(NarrowPath, SpookyCave, DeathTrap)가 사용된다.")]
        [SerializeField] private List<string> narrowOverrideTags = new List<string>();

        [Header("━━━ Debug (Read Only) ━━━━━━━━━━━━━━━")]
        [Tooltip("현재 목표 안개 색상")]
        [SerializeField] private Color targetColor;
        [Tooltip("현재 목표 안개 농도")]
        [SerializeField] private float targetDensity;
        [Tooltip("태그 오버라이드가 활성화되었는지 여부")]
        [SerializeField] private bool isNarrowOverrideActive;

        private void Awake()
        {
            // 기본 오버라이드 태그
            if (narrowOverrideTags.Count == 0)
                narrowOverrideTags.AddRange(new[] { "NarrowPath", "SpookyCave", "DeathTrap" });

            // Unity 안개 활성화
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
        }

        private void OnEnable()
        {
            EventBus.OnTerrainTagChanged += OnTerrainTagChanged;
        }

        private void OnDisable()
        {
            EventBus.OnTerrainTagChanged -= OnTerrainTagChanged;
        }

        private void Update()
        {
            var bb = GameBlackboard.Instance;
            float intensity = bb != null ? bb.GlobalIntensityScore : 0f;

            // 목표 색상/농도 결정
            if (isNarrowOverrideActive)
            {
                // 좁은 통로 태그 활성 시 → 태그 전용 색상 + 긴장도 농도
                targetColor = Color.Lerp(narrowPathFogColor, tenseFogColor, intensity);
                targetDensity = Mathf.Lerp(calmFogDensity * 2f, tenseFogDensity, intensity);
            }
            else
            {
                // 일반 상태 → 긴장도 기반 색상/농도
                targetColor = Color.Lerp(calmFogColor, tenseFogColor, intensity);
                targetDensity = Mathf.Lerp(calmFogDensity, tenseFogDensity, intensity);
            }

            // 부드러운 전환 (Lerp)
            float t = Time.deltaTime * transitionSpeed;
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, targetColor, t);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, targetDensity, t);
        }

        // ==================================================================
        // 이벤트 수신: 지형 태그 변경 시 오버라이드 재평가
        // ==================================================================
        private void OnTerrainTagChanged(IReadOnlyList<string> newTags)
        {
            isNarrowOverrideActive = false;
            if (newTags == null) return;

            foreach (var tag in newTags)
            {
                if (narrowOverrideTags.Contains(tag))
                {
                    isNarrowOverrideActive = true;
                    break;
                }
            }
        }
    }
}
