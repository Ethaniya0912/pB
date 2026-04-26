// =============================================================================
// SpeechSampleAssetGenerator.cs  |  pB-4 Week 2 Day 4
// Layer  : Editor Tool
// Owner  : Person A
//
// 역할:
//   Day 4 샘플 SO 에셋 (8 Template + 5 Preset)를 메뉴 한 번 클릭으로 자동 생성.
//   SAMPLE_SO_GUIDE.md의 내용과 1:1 대응.
//
// 사용법:
//   Unity 메뉴 → Window/pB-4/Day 4/Generate Sample Speech SOs
//   → Assets/Data/SO/Speech 와 Assets/Data/SO/DialoguePreset에 에셋 자동 생성
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TDA.PB4.AI.Speech;
using TDA.PB4.Data;

namespace TDA.PB4.Tooling.Editor
{
    public static class SpeechSampleAssetGenerator
    {
        // [FIX] Profile별로 폴더 분리 — 한 NPC에는 한 폴더의 Template만 할당
        // 같은 triggerId의 Template 2개를 같은 NPC에 붙이면 Dictionary 덮어쓰기 버그.
        private const string SPEECH_DIR_DEFAULT  = "Assets/Data/SO/Speech/Default";
        private const string SPEECH_DIR_ALTRUIST = "Assets/Data/SO/Speech/Altruist";
        private const string PRESET_DIR          = "Assets/Data/SO/DialoguePreset";

        [MenuItem("Window/pB-4/Day 4/Generate Sample Speech SOs")]
        public static void GenerateAll()
        {
            EnsureDir(SPEECH_DIR_DEFAULT);
            EnsureDir(SPEECH_DIR_ALTRUIST);
            EnsureDir(PRESET_DIR);

            int tplCount = GenerateTemplates();
            int presetCount = GeneratePresets();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Day 4 샘플 SO 생성 완료",
                $"✅ {tplCount}개 SpeechTemplateSO 생성됨\n" +
                $"   ├─ Default 프로파일: {SPEECH_DIR_DEFAULT}\n" +
                $"   └─ Altruist 프로파일: {SPEECH_DIR_ALTRUIST}\n\n" +
                $"✅ {presetCount}개 DialoguePresetSO 생성됨\n   → {PRESET_DIR}\n\n" +
                "⚠️ 사용법: 한 NPC의 SpeechAssembler에는 ONE 폴더의 4개만 할당하세요.\n" +
                "   (같은 triggerId 중복 시 Dictionary 덮어쓰기 버그 발생)",
                "OK");
        }

        private static void EnsureDir(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        // ─────────────────────────────────────────────────────
        //   SpeechTemplate SO 8종 생성
        // ─────────────────────────────────────────────────────
        private static int GenerateTemplates()
        {
            int count = 0;

            // === 1. Hostile Default ===
            count += CreateTemplate(
                "SpeechTemplate_Hostile_Default",
                SpeechTriggerIds.HOSTILE_DEFAULT, "Default",
                new[] {
                    (NPCAlignment.Hostile,   "한 걸음만 더 다가오면 죽여버린다.", SpeechTone.Aggressive),
                    (NPCAlignment.Neutral,   "...경고했어. 멀어져.", SpeechTone.Cold),
                    (NPCAlignment.Friendly,  "이런... 왜 그런 걸로 다투지?", SpeechTone.Neutral),
                    (NPCAlignment.Companion, "우리 사이에 왜?", SpeechTone.Warm),
                });

            // === 2. Hostile Altruist ===
            count += CreateTemplate(
                "SpeechTemplate_Hostile_Altruist",
                SpeechTriggerIds.HOSTILE_DEFAULT, "Altruist",
                new[] {
                    (NPCAlignment.Hostile,   "제발... 다가오지 마세요. 다쳐요.", SpeechTone.Anxious),
                    (NPCAlignment.Neutral,   "조심하세요.", SpeechTone.Cold),
                    (NPCAlignment.Friendly,  "당신, 오늘 많이 힘드신가 봐요.", SpeechTone.Warm),
                    (NPCAlignment.Companion, "괜찮아요. 저는 당신 편이에요.", SpeechTone.Warm),
                });

            // === 3. Neutral Default ===
            count += CreateTemplate(
                "SpeechTemplate_Neutral_Default",
                SpeechTriggerIds.NEUTRAL_DEFAULT, "Default",
                new[] {
                    (NPCAlignment.Hostile,   "가까이 오지 마라.", SpeechTone.Cold),
                    (NPCAlignment.Neutral,   "...용건이 뭐요?", SpeechTone.Neutral),
                    (NPCAlignment.Friendly,  "어서 오세요.", SpeechTone.Warm),
                    (NPCAlignment.Companion, "당신이군요. 반가워요.", SpeechTone.Warm),
                });

            // === 4. Neutral Altruist ===
            count += CreateTemplate(
                "SpeechTemplate_Neutral_Altruist",
                SpeechTriggerIds.NEUTRAL_DEFAULT, "Altruist",
                new[] {
                    (NPCAlignment.Hostile,   "저에게 무슨 원한이 있으세요...?", SpeechTone.Anxious),
                    (NPCAlignment.Neutral,   "안녕하세요!", SpeechTone.Warm),
                    (NPCAlignment.Friendly,  "도와드릴 일 있으세요?", SpeechTone.Warm),
                    (NPCAlignment.Companion, "저 아세요? 반가워요, {playerName}님.", SpeechTone.Warm),
                });

            // === 5. Friendly Default ===
            count += CreateTemplate(
                "SpeechTemplate_Friendly_Default",
                SpeechTriggerIds.FRIENDLY_DEFAULT, "Default",
                new[] {
                    (NPCAlignment.Hostile,   "...예전엔 우리 친했는데.", SpeechTone.Cold),
                    (NPCAlignment.Neutral,   "당신을 아는 것 같기도 하고.", SpeechTone.Neutral),
                    (NPCAlignment.Friendly,  "당신 곁에 있는 게 편해요.", SpeechTone.Warm),
                    (NPCAlignment.Companion, "{playerName}! 잘 지냈어요?", SpeechTone.Warm),
                });

            // === 6. Friendly Altruist ===
            count += CreateTemplate(
                "SpeechTemplate_Friendly_Altruist",
                SpeechTriggerIds.FRIENDLY_DEFAULT, "Altruist",
                new[] {
                    (NPCAlignment.Hostile,   "왜 저를 이렇게 대하세요... 슬퍼요.", SpeechTone.Anxious),
                    (NPCAlignment.Neutral,   "안녕하세요! 좋은 하루 보내고 계세요?", SpeechTone.Warm),
                    (NPCAlignment.Friendly,  "오늘도 웃는 얼굴로 만나서 기뻐요.", SpeechTone.Warm),
                    (NPCAlignment.Companion, "당신이 있어서 이 세계가 더 따뜻해요.", SpeechTone.Warm),
                });

            // === 7. Companion Default ===
            count += CreateTemplate(
                "SpeechTemplate_Companion_Default",
                SpeechTriggerIds.COMPANION_DEFAULT, "Default",
                new[] {
                    (NPCAlignment.Hostile,   "...당신이 이럴 줄은 몰랐어요.", SpeechTone.Cold),
                    (NPCAlignment.Neutral,   "우리, 예전처럼 돌아갈 수 있을까요?", SpeechTone.Neutral),
                    (NPCAlignment.Friendly,  "당신은 좋은 사람이에요.", SpeechTone.Warm),
                    (NPCAlignment.Companion, "당신과 함께라면 어디든 가겠습니다.", SpeechTone.Warm),
                });

            // === 8. Companion Altruist ===
            count += CreateTemplate(
                "SpeechTemplate_Companion_Altruist",
                SpeechTriggerIds.COMPANION_DEFAULT, "Altruist",
                new[] {
                    (NPCAlignment.Hostile,   "저는 당신이 다시 돌아올 거라 믿어요...", SpeechTone.Anxious),
                    (NPCAlignment.Neutral,   "당신이 누군지 천천히 알아가고 싶어요.", SpeechTone.Warm),
                    (NPCAlignment.Friendly,  "당신과 함께 걷는 길이 행복해요.", SpeechTone.Warm),
                    (NPCAlignment.Companion, "언제든 부르세요, {playerName}. 달려갈게요.", SpeechTone.Warm),
                });

            return count;
        }

        private static int CreateTemplate(
            string assetName, string triggerId, string profileHint,
            (NPCAlignment align, string text, SpeechTone tone)[] variants)
        {
            // [FIX] profileHint에 따라 폴더 분기
            string dir = (profileHint == "Altruist") ? SPEECH_DIR_ALTRUIST : SPEECH_DIR_DEFAULT;
            string path = $"{dir}/{assetName}.asset";

            // 이미 존재하면 덮어쓸지 확인 안하고 그냥 업데이트
            var existing = AssetDatabase.LoadAssetAtPath<SpeechTemplateSO>(path);
            SpeechTemplateSO tpl;
            bool isNew = (existing == null);

            if (isNew)
            {
                tpl = ScriptableObject.CreateInstance<SpeechTemplateSO>();
            }
            else
            {
                tpl = existing;
                tpl.variants.Clear();
            }

            tpl.triggerId = triggerId;
            tpl.profileHint = profileHint;
            if (tpl.variants == null) tpl.variants = new List<TemplateVariant>();
            tpl.variants.Clear();

            foreach (var (align, text, tone) in variants)
            {
                tpl.variants.Add(new TemplateVariant
                {
                    alignment = align,
                    text = text,
                    tone = tone,
                    durationOverride = 0f
                });
            }

            if (isNew)
                AssetDatabase.CreateAsset(tpl, path);
            else
                EditorUtility.SetDirty(tpl);

            return 1;
        }

        // ─────────────────────────────────────────────────────
        //   DialoguePreset SO 5종 생성
        // ─────────────────────────────────────────────────────
        private static int GeneratePresets()
        {
            int count = 0;

            count += CreatePreset("DialoguePreset_Warm", SpeechTone.Warm,
                new Color32(255, 230, 180, 255), new Color32(60, 40, 20, 200),
                new Color32(255, 200, 100, 180),
                16f, 0.3f, 2.5f, 0.6f, 0f);

            count += CreatePreset("DialoguePreset_Cold", SpeechTone.Cold,
                new Color32(200, 220, 255, 255), new Color32(20, 30, 50, 220),
                new Color32(120, 140, 200, 150),
                16f, 0.4f, 2.0f, 0.8f, 0f);

            count += CreatePreset("DialoguePreset_Neutral", SpeechTone.Neutral,
                new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 180),
                new Color32(180, 180, 180, 150),
                16f, 0.2f, 2.5f, 0.5f, 0f);

            count += CreatePreset("DialoguePreset_Aggressive", SpeechTone.Aggressive,
                new Color32(255, 180, 180, 255), new Color32(60, 10, 10, 220),
                new Color32(220, 60, 60, 180),
                18f, 0.1f, 2.0f, 0.3f, 1.5f);

            count += CreatePreset("DialoguePreset_Anxious", SpeechTone.Anxious,
                new Color32(220, 200, 220, 255), new Color32(40, 20, 40, 200),
                new Color32(150, 100, 180, 150),
                15f, 0.5f, 3.0f, 0.7f, 2.0f);

            return count;
        }

        private static int CreatePreset(string assetName, SpeechTone tone,
            Color fontColor, Color bgColor, Color borderColor,
            float fontSize, float fadeIn, float display, float fadeOut, float shake)
        {
            string path = $"{PRESET_DIR}/{assetName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<DialoguePresetSO>(path);
            DialoguePresetSO preset;
            bool isNew = (existing == null);

            if (isNew)
                preset = ScriptableObject.CreateInstance<DialoguePresetSO>();
            else
                preset = existing;

            preset.tone = tone;
            preset.fontColor = fontColor;
            preset.backgroundColor = bgColor;
            preset.borderColor = borderColor;
            preset.fontSize = fontSize;
            preset.fadeInDuration = fadeIn;
            preset.displayDuration = display;
            preset.fadeOutDuration = fadeOut;
            preset.shakeAmplitude = shake;
            preset.bubbleWidth = 4f;

            if (isNew)
                AssetDatabase.CreateAsset(preset, path);
            else
                EditorUtility.SetDirty(preset);

            return 1;
        }
    }
}
#endif
