// =============================================================================
// SpeechAssembler.cs  |  pB-4 Week 2 Day 4
// Layer  : L3 Domain (발화 조립 공장)
// Owner  : Person A
//
// 역할:
//   triggerId + Context를 받아 SpeechTemplateSO에서 Variant를 선택하고
//   플레이스홀더를 치환하여 최종 텍스트를 반환.
//
// 4계층 원칙:
//   - [Rule 1] 다른 Domain(Karma/Trust) 직접 참조 금지 — Context만 받음.
//   - [Rule 2] 텍스트 하드코딩 금지 — SpeechTemplateSO 참조.
//   - [Rule 3] Unity 이벤트 아님 (직접 호출) → 생명주기 관리 불필요.
//
// 다국어 지원 (Day 4 후반):
//   - variant.GetText(activeLanguage) 호출로 언어별 대사 선택
//   - forceLanguage 필드로 디버그/테스트용 강제 언어 지정 가능
//   - 기본은 Application.systemLanguage (런타임 OS 언어)
//
// Week 7 확장 경로:
//   - Assemble() 내부만 3레이어 조합으로 교체 (인터페이스는 불변).
//   - AssembleLayered() 가상 메서드 추가 예정.
//
// Week 6 LLM 확장 경로:
//   - LLMSpeechAssembler : MonoBehaviour, ISpeechAssembler 교체 구현체.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core.Events;    // [Day 4 수정] SpeechTriggerContext
using TDA.PB4.Data;           // NPCAlignment

namespace TDA.PB4.AI.Speech
{
    /// <summary>Day 4 기초 구현. Variant 매칭 + 플레이스홀더 치환만.</summary>
    [DisallowMultipleComponent]
    public class SpeechAssembler : MonoBehaviour, ISpeechAssembler
    {
        [Header("━━━ 템플릿 목록 ━━━━━━━━━━━━━━━━")]
        [Tooltip("이 NPC가 사용할 SpeechTemplateSO들.")]
        [SerializeField] private List<SpeechTemplateSO> templates = new List<SpeechTemplateSO>();

        [Header("━━━ 다국어 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("강제 언어 지정. Unknown이면 시스템 언어 자동 감지(Application.systemLanguage). " +
                 "디버그/QA용으로 특정 언어 강제할 때 사용.")]
        [SerializeField] private SystemLanguage forceLanguage = SystemLanguage.Unknown;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool debugLog = true;

        // 빠른 조회용 캐시 (triggerId → SO)
        private Dictionary<string, SpeechTemplateSO> templateMap;

        public SpeechTone LastTone { get; private set; } = SpeechTone.Neutral;

        void Awake()
        {
            BuildTemplateMap();
        }

        void BuildTemplateMap()
        {
            templateMap = new Dictionary<string, SpeechTemplateSO>(
                templates != null ? templates.Count : 0);

            if (templates == null) return;

            foreach (var tpl in templates)
            {
                if (tpl == null || string.IsNullOrWhiteSpace(tpl.triggerId))
                    continue;

                // [FIX] 중복 triggerId 경고 — Profile별로 나누려면 NPC별로 Template 분리해야 함
                if (templateMap.ContainsKey(tpl.triggerId))
                {
                    Debug.LogWarning(
                        $"[SpeechAssembler] {name}: triggerId '{tpl.triggerId}' 중복! " +
                        $"'{templateMap[tpl.triggerId].name}'이 '{tpl.name}'으로 덮어써짐. " +
                        $"Profile별 분리가 의도라면 NPC별 Template 리스트를 분리하세요.",
                        this);
                }

                templateMap[tpl.triggerId] = tpl;
            }
        }

        /// <summary>공개 API. Dispatcher가 호출.</summary>
        public string Assemble(string triggerId, SpeechTriggerContext ctx)
        {
            if (templateMap == null)
                BuildTemplateMap();

            if (!templateMap.TryGetValue(triggerId, out var tpl))
            {
                if (debugLog)
                    Debug.LogWarning(
                        $"[SpeechAssembler] {name}: trigger='{triggerId}' 매칭 템플릿 없음. " +
                        $"context=[{ctx.previousAlignment}→{ctx.currentAlignment}, reason={ctx.reason}]",
                        this);
                LastTone = SpeechTone.Neutral;
                return string.Empty;
            }

            var variant = tpl.FindVariant(ctx.currentAlignment);
            if (variant == null)
            {
                LastTone = SpeechTone.Neutral;
                return string.Empty;
            }

            // 활성 언어로 텍스트 조회 (언어 매칭 실패 시 첫 항목 fallback)
            SystemLanguage activeLang = GetActiveLanguage();
            string rawText = variant.GetText(activeLang);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                if (debugLog)
                    Debug.LogWarning(
                        $"[SpeechAssembler] {name}: trigger='{triggerId}' " +
                        $"context=[{ctx.previousAlignment}→{ctx.currentAlignment}, reason={ctx.reason}] " +
                        $"Variant(align={variant.alignment}) — 언어 '{activeLang}' 및 fallback 모두 비어있음.",
                        this);
                LastTone = SpeechTone.Neutral;
                return string.Empty;
            }

            LastTone = variant.tone;

            // 플레이스홀더 치환
            string result = ResolvePlaceholders(rawText, ctx);

            if (debugLog)
            {
                // ctx로부터 풍부한 맥락 정보 표시
                string contextInfo = $"{ctx.previousAlignment}→{ctx.currentAlignment} (reason={ctx.reason})";
                string playerInfo = (ctx.targetPlayerId > 0UL) ? $", player={ctx.targetPlayerId}" : "";
                Debug.Log(
                    $"<color=#7CD0FF>[SpeechAssembler]</color> {name}: " +
                    $"trigger='{triggerId}' " +
                    $"context=[{contextInfo}{playerInfo}] " +
                    $"→ Variant(align={variant.alignment}, tone={variant.tone}, lang={activeLang}): " +
                    $"\"{result}\"",
                    this);
            }

            return result;
        }

        /// <summary>
        /// 활성 언어 결정.
        /// forceLanguage가 Unknown이 아니면 그 값, 그 외엔 Application.systemLanguage.
        /// </summary>
        private SystemLanguage GetActiveLanguage()
        {
            return (forceLanguage != SystemLanguage.Unknown)
                 ? forceLanguage
                 : Application.systemLanguage;
        }

        /// <summary>플레이스홀더 치환. {npcName}, {playerName} 지원.</summary>
        private string ResolvePlaceholders(string template, SpeechTriggerContext ctx)
        {
            string result = template;
            if (result.Contains("{npcName}"))
                result = result.Replace("{npcName}", ctx.characterId);

            if (result.Contains("{playerName}"))
                result = result.Replace("{playerName}", ResolvePlayerName(ctx.targetPlayerId));

            return result;
        }

        private string ResolvePlayerName(ulong playerId)
        {
            // Day 4 기초 구현: playerId 0 = "여행자" (기본 이름)
            // Week 7 확장: PlayerNameRegistry에서 조회 예정
            if (playerId == 0UL) return "여행자";
            return $"P{playerId}";
        }

        // [ContextMenu] Inspector에서 수동 테스트
        [ContextMenu("TEST: 현재 Alignment로 companion_default 조립")]
        void DebugTestCompanion()
        {
            var ctx = new SpeechTriggerContext
            {
                characterId = name,
                targetPlayerId = 0UL,
                currentAlignment = NPCAlignment.Companion,
                previousAlignment = NPCAlignment.Friendly,
                reason = TDA.PB4.Interfaces.Narrative.AlignmentChangeReason.Manual
            };
            string line = Assemble(SpeechTriggerIds.COMPANION_DEFAULT, ctx);
            Debug.Log($"[SpeechAssembler TEST] \"{line}\" (Tone={LastTone})");
        }

        // === Week 7 확장 훅 (현재는 미구현) ===
        // protected virtual string AssembleLayered(string triggerId, SpeechTriggerContext ctx)
        // {
        //     // Part A + Part B + Part C 조합
        //     // 5축 유클리드 매칭
        //     // 243버킷 유의어 사전
        //     // usage_count 반복 억제
        // }
    }
}