// =============================================================================
// SpeechAssembler.cs  |  pB-4 Project — Week 7
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 대사를 3단계 레이어로 조립한다.
//   [Part A: 감탄사] + [Part B: 핵심 정보] + [Part C: 어미]
//
//   기초 레이어:    5축 성격에 맞는 부품 선택 (243버킷 분류: 3^5)
//   질감 레이어:    유의어 레이어링 + 상태 기반 수식어
//   개성 레이어:    통사적 구조 변이 + 개별 ID 고유 버릇
//
//   반복 억제: (1.0 / (1.0 + usage_count)) 페널티
//   유클리드 거리 단어 매칭: 모든 단어에 5차원 벡터 → NPC 성격과 거리 계산
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.AI
{
    /// <summary>단일 단어/구문의 5차원 벡터와 사용 기록.</summary>
    [Serializable]
    public class WordEntry
    {
        [Tooltip("단어 또는 구문. 예: '젠장', '조심해', '뒤로!'")]
        public string word;

        [Tooltip("이 단어의 5축 성격 벡터 [C,S,O,A,D]. " +
                 "NPC 성격과 유클리드 거리가 가까울수록 선택 확률 높음. " +
                 "예: '젠장' = [0.1, 0.2, 0.5, 0.2, 0.9] (자제력↓ 직설↑)")]
        public float[] personalityVector = new float[5];

        [Tooltip("파트 유형. A=감탄사, B=핵심정보, C=어미")]
        public string partType = "B";

        [HideInInspector] public int usageCount = 0;
    }

    /// <summary>발화 결과 데이터.</summary>
    [Serializable]
    public class SpeechResult
    {
        public string partA;
        public string partB;
        public string partC;
        public string fullSentence;
        public string coreLayer;
        public string textureLayer;
        public string individualLayer;
    }

    public class SpeechAssembler : MonoBehaviour
    {
        [Header("━━━ 단어 사전 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("전체 단어/구문 사전. Part A(감탄사), Part B(핵심정보), Part C(어미)가 모두 포함. " +
                 "비어있으면 Awake()에서 기본 사전 30개가 자동 생성된다.")]
        [SerializeField] private List<WordEntry> dictionary = new List<WordEntry>();

        [Header("━━━ NPC 참조 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 NPC의 HumanoidAIBrain. 성격 5축을 읽어 단어를 매칭. " +
                 "같은 GameObject에 있으면 자동 탐색.")]
        [SerializeField] private HumanoidAIBrain brain;

        [Header("━━━ 질감 레이어 설정 ━━━━━━━━━━━━━━━━")]
        [Tooltip("상태 기반 수식어. fear>0.7이면 이 접두어가 붙는다. " +
                 "예: '떨리는 목소리로' → '[떨리는 목소리로] 뒤로!'")]
        public string fearModifier = "\ub5a8\ub9ac\ub294 \ubaa9\uc18c\ub9ac\ub85c";

        [Tooltip("greed>0.5이면 이 접두어. 예: '눈을 빛내며'")]
        public string greedModifier = "\ub208\uc744 \ube5b\ub0b4\uba70";

        [Header("━━━ 개성 레이어 설정 ━━━━━━━━━━━━━━━━")]
        [Tooltip("이 NPC 고유의 말버릇. 문장 끝에 추가됨. " +
                 "예: '...인 것 같아', '...라고!', '...흠'")]
        public string uniqueQuirk = "";

        [Tooltip("문장 구조 변이 확률 (0~1). " +
                 "0.3 = 30% 확률로 Part 순서가 B+A+C로 변이 (도치법). " +
                 "0.0 = 항상 A+B+C 순서.")]
        [Range(0f, 0.5f)]
        public float syntaxVariationChance = 0.2f;

        [Header("━━━ 반복 억제 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("반복 억제 강도. 높을수록 같은 단어 재사용 페널티 증가. " +
                 "1.0 = 1회 사용 시 선택 확률 50% 감소. " +
                 "0.0 = 억제 없음.")]
        [Range(0f, 3f)]
        public float repetitionPenaltyStrength = 1.0f;

        [Header("━━━ 마지막 발화 결과 (Read Only) ━━━━━━")]
        [SerializeField] private SpeechResult lastResult;

        [Header("━━━ 243버킷 (Read Only) ━━━━━━━━━━━━━")]
        [Tooltip("3^5=243개 성격 버킷 중 현재 NPC가 속한 버킷 인덱스")]
        [SerializeField] private int currentBucketIndex;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Awake()
        {
            if (brain == null) brain = GetComponent<HumanoidAIBrain>();
            if (dictionary.Count == 0) GenerateDefaultDictionary();
        }

        // ==================================================================
        // 핵심 API: 대사 생성
        // ==================================================================

        /// <summary>
        /// 현재 NPC의 성격과 상태에 맞는 대사를 3단계 레이어로 조립.
        /// </summary>
        /// <param name="situationContext">상황 맥락. 예: 'enemy_spotted', 'ally_down', 'loot_found'</param>
        public SpeechResult AssembleSpeech(string situationContext = "idle")
        {
            if (brain == null) return null;
            var personality = brain.Personality;

            // 243버킷 계산
            currentBucketIndex = Calculate243Bucket(personality);

            // === 기초 레이어: 성격 벡터 기반 단어 선택 ===
            string partA = SelectBestWord("A", personality);
            string partB = SelectBestWord("B", personality);
            string partC = SelectBestWord("C", personality);

            string coreResult = $"{partA} {partB}{partC}";

            // === 질감 레이어: 상태 기반 수식어 ===
            string texture = "";
            if (brain.fear > 0.7f) texture = $"[{fearModifier}] ";
            else if (brain.greed > 0.5f) texture = $"[{greedModifier}] ";

            string textureResult = texture + coreResult;

            // === 개성 레이어: 구조 변이 + 고유 버릇 ===
            string finalSentence = textureResult;

            // 도치법 변이
            if (UnityEngine.Random.Range(0f, 1f) < syntaxVariationChance)
                finalSentence = $"{texture}{partB}, {partA}{partC}";

            // 고유 말버릇 추가
            if (!string.IsNullOrEmpty(uniqueQuirk))
                finalSentence += $" {uniqueQuirk}";

            var result = new SpeechResult
            {
                partA = partA, partB = partB, partC = partC,
                fullSentence = finalSentence,
                coreLayer = coreResult,
                textureLayer = texture,
                individualLayer = uniqueQuirk
            };
            lastResult = result;

            if (debugLog)
                Debug.Log($"[Speech] bucket={currentBucketIndex}, core='{coreResult}', texture='{texture}', final='{finalSentence}'");

            return result;
        }

        // ==================================================================
        // 유클리드 거리 기반 단어 선택 + 반복 억제
        // ==================================================================
        private string SelectBestWord(string partType, PersonalityMatrix npcP)
        {
            float bestScore = float.MaxValue;
            WordEntry bestWord = null;
            float[] npc = { npcP.control, npcP.stability, npcP.openness, npcP.agreeable, npcP.directness };

            foreach (var entry in dictionary)
            {
                if (entry.partType != partType) continue;
                if (entry.personalityVector == null || entry.personalityVector.Length < 5) continue;

                // 유클리드 거리
                float dist = 0f;
                for (int i = 0; i < 5; i++)
                    dist += (npc[i] - entry.personalityVector[i]) * (npc[i] - entry.personalityVector[i]);
                dist = Mathf.Sqrt(dist);

                // 반복 억제 페널티
                float penalty = 1.0f / (1.0f + entry.usageCount * repetitionPenaltyStrength);
                float score = dist / penalty;

                if (score < bestScore) { bestScore = score; bestWord = entry; }
            }

            if (bestWord != null)
            {
                bestWord.usageCount++;
                return bestWord.word;
            }
            return "(...)";
        }

        // ==================================================================
        // 243버킷: 3^5 성격 분류
        // 각 축을 Low(0)/Mid(1)/High(2)로 삼분하여 0~242 인덱스
        // ==================================================================
        private int Calculate243Bucket(PersonalityMatrix p)
        {
            int c = Quantize3(p.control);
            int s = Quantize3(p.stability);
            int o = Quantize3(p.openness);
            int a = Quantize3(p.agreeable);
            int d = Quantize3(p.directness);
            return c * 81 + s * 27 + o * 9 + a * 3 + d;
        }

        private int Quantize3(float v) => v < 0.33f ? 0 : v < 0.66f ? 1 : 2;

        // ==================================================================
        // 기본 사전 생성
        // ==================================================================
        private void GenerateDefaultDictionary()
        {
            // Part A: 감탄사
            dictionary.Add(new WordEntry{word="\uc816\uc7a5!",partType="A",personalityVector=new[]{0.1f,0.2f,0.5f,0.2f,0.9f}});
            dictionary.Add(new WordEntry{word="\uc870\uc2ec!",partType="A",personalityVector=new[]{0.7f,0.6f,0.3f,0.7f,0.4f}});
            dictionary.Add(new WordEntry{word="\uc624!",partType="A",personalityVector=new[]{0.3f,0.5f,0.8f,0.5f,0.5f}});
            dictionary.Add(new WordEntry{word="...",partType="A",personalityVector=new[]{0.8f,0.8f,0.2f,0.3f,0.1f}});
            dictionary.Add(new WordEntry{word="\uc73c\uc544\uc545!",partType="A",personalityVector=new[]{0.1f,0.1f,0.3f,0.5f,0.3f}});
            dictionary.Add(new WordEntry{word="\ud750\uc74c...",partType="A",personalityVector=new[]{0.6f,0.7f,0.4f,0.4f,0.2f}});
            dictionary.Add(new WordEntry{word="\ubcf4\uc774\ub294\uac00?",partType="A",personalityVector=new[]{0.5f,0.5f,0.7f,0.6f,0.6f}});
            dictionary.Add(new WordEntry{word="\ub4e4\uc5b4!",partType="A",personalityVector=new[]{0.2f,0.4f,0.6f,0.3f,0.8f}});
            dictionary.Add(new WordEntry{word="\uc815\uc9c0!",partType="A",personalityVector=new[]{0.9f,0.7f,0.2f,0.4f,0.7f}});
            dictionary.Add(new WordEntry{word="\ud558...",partType="A",personalityVector=new[]{0.4f,0.3f,0.5f,0.6f,0.3f}});
            // Part B: 핵심 정보
            dictionary.Add(new WordEntry{word="\uc801\uc774 \ub2e4\uac00\uc628\ub2e4",partType="B",personalityVector=new[]{0.5f,0.5f,0.5f,0.5f,0.7f}});
            dictionary.Add(new WordEntry{word="\uae38\uc774 \ub9c9\ud600\uc788\ub2e4",partType="B",personalityVector=new[]{0.5f,0.4f,0.3f,0.5f,0.5f}});
            dictionary.Add(new WordEntry{word="\ubcf4\ubb3c\uc774 \uc788\ub2e4",partType="B",personalityVector=new[]{0.3f,0.5f,0.6f,0.4f,0.5f}});
            dictionary.Add(new WordEntry{word="\ub3d9\ub8cc\uac00 \uc4f0\ub7ec\uc84c\ub2e4",partType="B",personalityVector=new[]{0.5f,0.3f,0.4f,0.7f,0.5f}});
            dictionary.Add(new WordEntry{word="\uc5ec\uae30\uc11c \ubc84\ud2f0\uc790",partType="B",personalityVector=new[]{0.6f,0.6f,0.2f,0.5f,0.4f}});
            dictionary.Add(new WordEntry{word="\ub3c4\ub9dd\uce58\uc790",partType="B",personalityVector=new[]{0.2f,0.2f,0.3f,0.3f,0.3f}});
            dictionary.Add(new WordEntry{word="\ud568\uc815\uc774\ub2e4",partType="B",personalityVector=new[]{0.7f,0.5f,0.4f,0.4f,0.6f}});
            dictionary.Add(new WordEntry{word="\uc2e0\uacbd \uc4f0\uc9c0 \ub9c8",partType="B",personalityVector=new[]{0.8f,0.7f,0.3f,0.3f,0.8f}});
            dictionary.Add(new WordEntry{word="\ubb50\uac00 \uc788\ub2e4",partType="B",personalityVector=new[]{0.4f,0.4f,0.7f,0.5f,0.4f}});
            dictionary.Add(new WordEntry{word="\uc0c1\ud669\uc774 \uc548 \uc88b\ub2e4",partType="B",personalityVector=new[]{0.5f,0.3f,0.5f,0.5f,0.5f}});
            // Part C: 어미
            dictionary.Add(new WordEntry{word=".",partType="C",personalityVector=new[]{0.7f,0.7f,0.3f,0.5f,0.3f}});
            dictionary.Add(new WordEntry{word="!",partType="C",personalityVector=new[]{0.2f,0.3f,0.6f,0.4f,0.8f}});
            dictionary.Add(new WordEntry{word="...",partType="C",personalityVector=new[]{0.5f,0.4f,0.4f,0.5f,0.2f}});
            dictionary.Add(new WordEntry{word="\ub77c\uace0!",partType="C",personalityVector=new[]{0.1f,0.3f,0.5f,0.3f,0.9f}});
            dictionary.Add(new WordEntry{word="\uc778 \uac83 \uac19\uc544.",partType="C",personalityVector=new[]{0.6f,0.5f,0.5f,0.6f,0.2f}});
            dictionary.Add(new WordEntry{word=", \uc54c\uaca0\ub098?",partType="C",personalityVector=new[]{0.3f,0.5f,0.4f,0.4f,0.7f}});
            dictionary.Add(new WordEntry{word="(\uc911\uc5bc\uac70\ub9bc)",partType="C",personalityVector=new[]{0.8f,0.8f,0.2f,0.3f,0.1f}});
            dictionary.Add(new WordEntry{word="\ub2e4\uace0.",partType="C",personalityVector=new[]{0.5f,0.6f,0.3f,0.5f,0.5f}});
            dictionary.Add(new WordEntry{word="\ub77c\ub2c8\uae4c.",partType="C",personalityVector=new[]{0.4f,0.5f,0.4f,0.5f,0.6f}});
            dictionary.Add(new WordEntry{word="?!",partType="C",personalityVector=new[]{0.2f,0.2f,0.7f,0.4f,0.6f}});
        }

        public SpeechResult LastResult => lastResult;
        public int CurrentBucketIndex => currentBucketIndex;
    }
}
