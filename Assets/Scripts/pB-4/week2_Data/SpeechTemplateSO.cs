// =============================================================================
// SpeechTemplateSO.cs  |  pB-4 Week 2 Day 4
// Layer  : Data / SO (순수 데이터)
// Owner  : Person A
//
// 역할:
//   trigger id(예: "companion_default") 하나에 대해 여러 Variant를
//   보유하는 SO. SpeechAssembler가 런타임에 매칭하여 선택.
//
// 4계층 원칙:
//   - [Rule 2] 매직 넘버/하드코딩 금지 — 모든 텍스트는 SO에서 읽기.
//   - Data First — 코드 작성 전에 SO부터 정의 (가이드 Step 1).
//
// 다국어 지원 (Day 4 후반 추가):
//   - TemplateVariant.localizedTexts: List<LocalizedText> 사용
//   - SystemLanguage 기준 매칭, 없으면 첫 항목 fallback (보통 한국어)
//   - 기존 text 필드는 OnValidate에서 자동 마이그레이션 → localizedTexts[KO]
//
// Week 7 확장 예약:
//   - partA/partB/partC 필드 (Part A 감탄사 + Part B 핵심 + Part C 어미)
//   - personalityCenter 필드 (5축 유클리드 매칭용)
//   - agreeableThreshold 필드 (성격 기반 Variant 필터)
//   현재 Day 4는 alignment 필터만 사용.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Data;  // NPCAlignment

namespace TDA.PB4.AI.Speech
{
    /// <summary>발화 템플릿. 하나의 triggerId에 대해 여러 Alignment Variant.</summary>
    [CreateAssetMenu(menuName = "pB-4/Speech/Template", fileName = "SpeechTemplate_")]
    public class SpeechTemplateSO : ScriptableObject
    {
        [Tooltip("트리거 식별자. SpeechTriggerIds의 값과 일치해야 함. 예: companion_default")]
        public string triggerId;

        [Tooltip("NPC 성격 프로파일 분류 (선택 — Week 7에서 본격 사용). 지금은 정보용.")]
        public string profileHint = "Default";

        [Tooltip("Variant 목록. SpeechAssembler가 alignment가 일치하는 첫 항목을 선택.")]
        public List<TemplateVariant> variants = new List<TemplateVariant>();

        /// <summary>alignment 조건에 맞는 Variant를 반환. 없으면 첫 항목 fallback.</summary>
        public TemplateVariant FindVariant(NPCAlignment alignment)
        {
            if (variants == null || variants.Count == 0)
                return null;

            foreach (var v in variants)
            {
                if (v.alignment == alignment)
                    return v;
            }
            return variants[0];  // fallback
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(triggerId))
                Debug.LogWarning($"[SpeechTemplateSO] {name}: triggerId가 비어있음.", this);
            if (variants == null || variants.Count == 0)
                Debug.LogWarning($"[SpeechTemplateSO] {name}: variants가 비어있음.", this);

            // 자동 마이그레이션: 기존 v.text가 있고 localizedTexts가 비어있으면
            // localizedTexts[Korean]으로 자동 이주. 데이터 유실 0%.
            if (variants != null)
            {
                foreach (var v in variants)
                {
                    if (v == null) continue;

                    bool hasLegacyText = !string.IsNullOrWhiteSpace(v.text);
                    bool localizedEmpty = (v.localizedTexts == null || v.localizedTexts.Count == 0);

                    if (hasLegacyText && localizedEmpty)
                    {
                        v.localizedTexts = new List<LocalizedText>
                        {
                            new LocalizedText
                            {
                                language = SystemLanguage.Korean,
                                text = v.text
                            }
                        };
                        Debug.Log($"[SpeechTemplateSO] {name}: Variant({v.alignment}) " +
                                  $"기존 text → localizedTexts[Korean] 자동 마이그레이션.", this);
                    }
                }
            }
        }
    }

    /// <summary>언어별 대사 텍스트 1개.</summary>
    [Serializable]
    public class LocalizedText
    {
        [Tooltip("대상 언어. SystemLanguage enum 사용. " +
                 "한국어=Korean, 영어=English, 일본어=Japanese, 중국어=ChineseSimplified")]
        public SystemLanguage language = SystemLanguage.Korean;

        [Tooltip("대사 텍스트. 플레이스홀더 사용 가능: {npcName}, {playerName}")]
        [TextArea(2, 5)]
        public string text;
    }

    [Serializable]
    public class TemplateVariant
    {
        [Tooltip("이 Variant가 매칭되는 진영.")]
        public NPCAlignment alignment = NPCAlignment.Neutral;

        // [DEPRECATED] 단일 언어 시절 필드. OnValidate에서 자동으로 localizedTexts[KO]로 이주.
        // 데이터 유실 방지를 위해 필드는 유지하되 [HideInInspector] 처리.
        [HideInInspector]
        public string text;

        [Tooltip("언어별 대사 리스트. 시스템 언어와 일치하는 항목 우선, " +
                 "없으면 첫 항목 fallback. 새 SO 작성 시 [+]로 언어 추가.")]
        public List<LocalizedText> localizedTexts = new List<LocalizedText>();

        [Tooltip("이 대사의 톤. Renderer가 폰트/색상 결정에 사용.")]
        public SpeechTone tone = SpeechTone.Neutral;

        [Tooltip("(선택) 표시 시간 override. 0 = Preset 값 사용 (자동 계산 또는 displayDuration).")]
        [Range(0f, 10f)]
        public float durationOverride = 0f;

        /// <summary>
        /// 활성 언어에 맞는 텍스트 반환. 없으면 첫 항목 fallback (보통 한국어).
        /// 모든 언어가 비어있으면 string.Empty.
        /// </summary>
        public string GetText(SystemLanguage lang)
        {
            if (localizedTexts == null || localizedTexts.Count == 0)
                return string.Empty;

            // 정확한 언어 매칭
            foreach (var lt in localizedTexts)
            {
                if (lt.language == lang && !string.IsNullOrWhiteSpace(lt.text))
                    return lt.text;
            }

            // 폴백: 첫 비어있지 않은 항목 (보통 KO 원본)
            foreach (var lt in localizedTexts)
            {
                if (!string.IsNullOrWhiteSpace(lt.text))
                    return lt.text;
            }

            return string.Empty;
        }

        /// <summary>유효성 검사: 활성 언어에 텍스트가 있는가?</summary>
        public bool HasTextForLanguage(SystemLanguage lang)
        {
            if (localizedTexts == null) return false;
            foreach (var lt in localizedTexts)
                if (lt.language == lang && !string.IsNullOrWhiteSpace(lt.text))
                    return true;
            return false;
        }

        // Week 7 예약 필드 (현재 Day 4에서는 미사용):
        // public float agreeableThreshold = -1f;  // Week 7: Personality 매칭
        // public List<string> partA;   // Week 7: 감탄사 리스트
        // public List<string> partB;   // Week 7: 핵심 정보 리스트
        // public List<string> partC;   // Week 7: 어미 리스트
    }
}