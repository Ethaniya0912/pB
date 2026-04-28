// =============================================================================
// PersonalityTagResolver.cs  |  pB-4 Project — Week 2 (Day 2 T2.3 통합본)
//                            |  v3 NGO 2.0 대응 — 2026-04-23
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   PersonalityMatrix 5축 값의 조합에서 성격 태그를 확률적으로 발현.
//   15종 태그 중 조건을 만족하는 태그가 확률적으로 부여.
//   발현된 태그는 UtilityMasterFormula의 PersonalityModifierRule과 연동.
//
// [통합 이력]
//   - Day 1 T1.4: SetRules/externalRuleLibrary 추가
//   - Day 2 T2.3: Awake에서 GenerateDefault15Rules 호출 제거. SO 강제.
//   - Day 2 T2.3 통합: PersonalityTagResolver_Week2Patch.cs 흡수. partial 제거.
//
// [v3 NGO 2.0 개정]
//   - [NGO-1] MonoBehaviour → NetworkBehaviour
//   - [NGO-2] Awake + OnNetworkSpawn 듀얼 초기화 (단독 Play + Network 호환)
//   - [NGO-3] ResolveTagsFromPersonality는 서버 권한 (HasAuthority 가드)
//   - [NGO-4] NetworkList<FixedString32Bytes> netActiveTags — 서버 결정 → 전 클라 자동 전파
//   - [NGO-5] 기존 List<string> activeTags 유지 (Inspector 표시 + 단독 Play + API 호환)
//   - [NGO-6] OnNetworkDespawn에서 NetworkList.Dispose
//
// [v4 DEBT-24 v1 — Week 3 D1]
//   - [DEBT-24] netActiveTags Awake에서 무조건 new로 변경.
//               이전: OnNetworkSpawn에서만 할당 → 단독 Play 시 null 유지 →
//                     ExitPlaymode → NetworkBehaviour.OnDestroy → InitializeVariables NRE.
//               이후: Awake에서 항상 NetworkList 인스턴스 보장. OnNetworkSpawn은 OnListChanged 등록만.
//               OnNetworkDespawn에서 Dispose/null 제거 (OnDestroy가 NGO 표준으로 처리).
//
// [v5 DEBT-24 v2 — Week 3 D1 후속]
//   - [DEBT-24 v2] ResolveTagsFromPersonality의 isNetworked 판정에 IsSpawned 추가.
//                  v1 부작용 박제: 단독 Play에서 매 polling마다 NetworkVariable dirty 워닝.
//                  원인: v1으로 netActiveTags가 항상 non-null → isNetworked = true →
//                        Clear/Add가 MarkNetworkBehaviourDirty 시도 →
//                        NetworkObject 미스폰이라 워닝 출력.
//                  해결: IsSpawned 시에만 NetworkList 갱신. 단독 Play는 activeTags만 갱신.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;                         // [NGO-4] FixedString32Bytes
using Unity.Netcode;                             // [NGO-1]
using UnityEngine;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 단일 성격 태그의 발현 조건을 정의하는 규칙.
    /// </summary>
    [Serializable]
    public class TagEmergenceRule
    {
        [Tooltip("태그 이름. UtilityMasterFormula의 PersonalityModifierRule.tagName과 일치해야 한다.")]
        public string tagName;

        [Header("━━━ 발현 조건 (5축 범위) ━━━━━━━━━")]
        [Tooltip("자제력(Control) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minControl = -1f;
        [Tooltip("자제력(Control) 최대값. 2=조건 없음")]
        [Range(-1f, 2f)] public float maxControl = 2f;

        [Tooltip("안정성(Stability) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minStability = -1f;
        [Tooltip("안정성(Stability) 최대값. 2=조건 없음")]
        [Range(-1f, 2f)] public float maxStability = 2f;

        [Tooltip("개방성(Openness) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minOpenness = -1f;
        [Range(-1f, 2f)] public float maxOpenness = 2f;

        [Tooltip("우호성(Agreeable) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minAgreeable = -1f;
        [Range(-1f, 2f)] public float maxAgreeable = 2f;

        [Tooltip("직설성(Directness) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minDirectness = -1f;
        [Range(-1f, 2f)] public float maxDirectness = 2f;

        [Header("━━━ 발현 확률 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("조건 만족 시 실제 태그 부여 확률. 0.7 = 70%.")]
        [Range(0f, 1f)] public float emergenceProbability = 0.7f;

        [Header("━━━ 유틸리티 보정 (빠른 설정) ━━━━━━")]
        [Tooltip("이 태그 부여 시 fear에 곱할 배수. 0=보정 없음.")]
        public float fearMultiplier = 0f;
        [Tooltip("탐욕 관련 유틸리티 지수 보정. 예: Glutton → greedK=2.5")]
        public float greedKOverride = 0f;

        /// <summary>주어진 성격 5축이 이 태그의 발현 조건을 만족하는지 검사.</summary>
        public bool CheckCondition(PersonalityMatrix p)
        {
            if (minControl > -0.5f && p.control < minControl) return false;
            if (maxControl < 1.5f && p.control > maxControl) return false;
            if (minStability > -0.5f && p.stability < minStability) return false;
            if (maxStability < 1.5f && p.stability > maxStability) return false;
            if (minOpenness > -0.5f && p.openness < minOpenness) return false;
            if (maxOpenness < 1.5f && p.openness > maxOpenness) return false;
            if (minAgreeable > -0.5f && p.agreeable < minAgreeable) return false;
            if (maxAgreeable < 1.5f && p.agreeable > maxAgreeable) return false;
            if (minDirectness > -0.5f && p.directness < minDirectness) return false;
            if (maxDirectness < 1.5f && p.directness > maxDirectness) return false;
            return true;
        }
    }

    /// <summary>
    /// 성격 5축으로부터 태그를 자동 발현시키는 해석기.
    /// [NGO-1] NetworkBehaviour. 서버가 태그 결정 → 전 클라 NetworkList 자동 동기화.
    /// </summary>
    public class PersonalityTagResolver : NetworkBehaviour             // [NGO-1]
    {
        [Header("━━━ 태그 발현 규칙 (15종) ━━━━━━━━━━━━")]
        [Tooltip("15종 성격 태그의 발현 조건 목록. externalRuleLibrary 또는 Bootstrapper가 주입.")]
        [SerializeField] private List<TagEmergenceRule> rules = new List<TagEmergenceRule>();

        [Header("━━━ 현재 발현된 태그 (Read Only) ━━━━━━")]
        [Tooltip("마지막 ResolveTagsFromPersonality 결과. [NGO-5] Inspector 표시용 + 단독 Play 저장소.")]
        [SerializeField] private List<string> activeTags = new List<string>();

        // [NGO-4] 실제 네트워크 동기화 저장소. 서버가 쓰기, 전 클라 읽기.
        private NetworkList<FixedString32Bytes> netActiveTags;

        /// <summary>외부에서 읽기 전용 접근. [NGO-5] 네트워크/단독 Play 양쪽 호환.</summary>
        public IReadOnlyList<string> ActiveTags => activeTags;

        [Header("━━━ Week 2 T1.4 외부 규칙 (선택) ━━━━")]
        [Tooltip("PersonalityTagRuleSO .asset. 지정되면 자동 로드. 비어있으면 Bootstrapper가 SetRules.")]
        [SerializeField] private PersonalityTagRuleSO externalRuleLibrary;

        [Header("━━━ Debug ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("태그 발현 과정을 Console에 출력.")]
        public bool debugLog = false;

        // ==================================================================
        // [v3 NGO-2] 이중 초기화 — 단독 Play + Network 동시 지원
        // [DEBT-24 v1] netActiveTags 무조건 초기화 — ExitPlaymode NRE 회피
        // ==================================================================

        /// <summary>단독 Play 경로. NetworkManager 비활성 시 기존 Day 1~2 동작.</summary>
        private void Awake()
        {
            // [DEBT-24 v1] netActiveTags 무조건 초기화.
            // 이전: NetworkManager null 시 netActiveTags가 null로 유지 → ExitPlaymode 시
            //       NetworkBehaviour.OnDestroy → InitializeVariables가 NetworkVariable null 검증 → throw.
            // 변경: 단독 Play에서도 NetworkList 인스턴스 보장. NetworkObject 미스폰이라
            //       실제 동기화는 안 일어나고, Dispose는 NGO가 OnDestroy에서 표준 처리.
            if (netActiveTags == null)
            {
                netActiveTags = new NetworkList<FixedString32Bytes>(
                    writePerm: NetworkVariableWritePermission.Server);
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                // 단독 Play: 네트워크 변수 없이 List 기반 동작
                InitializeRulesSource();
            }
            // 네트워크 Play: OnNetworkSpawn이 처리
        }

        /// <summary>네트워크 Play 경로. [NGO-2] NetworkObject 스폰 시 호출.</summary>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // [DEBT-24] netActiveTags는 Awake에서 이미 할당됨. OnListChanged 등록만.
            // [NGO-5] NetworkList 변경 시 activeTags(List<string>)에 동기화 — Inspector 표시 유지
            netActiveTags.OnListChanged += OnNetActiveTagsChanged;

            InitializeRulesSource();
        }

        /// <summary>[NGO-6] NetworkList 정리 — 핸들러 해제.</summary>
        /// <remarks>
        /// [DEBT-24] Dispose + null 할당 제거. 이유:
        ///   - OnNetworkDespawn 후에도 OnDestroy가 호출되며 NGO가 InitializeVariables로 null 검증
        ///   - netActiveTags = null 시 ExitPlaymode 시점에 throw (DEBT-24와 같은 결함 재발)
        ///   - NetworkList Dispose는 NetworkBehaviour.OnDestroy 표준 동작이 처리
        /// </remarks>
        public override void OnNetworkDespawn()
        {
            if (netActiveTags != null)
            {
                netActiveTags.OnListChanged -= OnNetActiveTagsChanged;
            }
            base.OnNetworkDespawn();
        }

        private void InitializeRulesSource()
        {
            if (externalRuleLibrary != null)
            {
                LoadRulesFromSO(externalRuleLibrary);
            }
            else if (rules.Count == 0)
            {
                // Bootstrapper가 SetRules 호출할 시간 대기
                if (HasAuthority())
                    StartCoroutine(VerifyRulesLoadedAfterFrame());
            }
        }

        /// <summary>[NGO-5] NetworkList → List<string> 동기화 (서버/클라 양쪽).</summary>
        private void OnNetActiveTagsChanged(NetworkListEvent<FixedString32Bytes> e)
        {
            activeTags.Clear();
            for (int i = 0; i < netActiveTags.Count; i++)
                activeTags.Add(netActiveTags[i].ToString());

            if (debugLog)
                Debug.Log($"[TagResolver] {name}: NetworkList 변경 수신 → activeTags 동기화 ({activeTags.Count}개)");
        }

        private IEnumerator VerifyRulesLoadedAfterFrame()
        {
            yield return null;
            if (rules.Count == 0)
            {
                Debug.LogError($"[TagResolver] {name}: rules 미로드. " +
                               $"Inspector의 externalRuleLibrary 또는 " +
                               $"HumanoidBootstrapper.defaultTagRules 할당 필요.");
            }
        }

        /// <summary>[v3 NGO 헬퍼] 결정 권한 체크 (NetworkManager 비활성 시 true).</summary>
        private bool HasAuthority()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return true;
            return IsServer;
        }

        // ==================================================================
        // 외부 규칙 주입 API (Bootstrapper 및 수동 호출용)
        // ==================================================================

        /// <summary>외부 SO로부터 rules 로드. Bootstrapper가 호출.</summary>
        public void SetRules(PersonalityTagRuleSO library)
        {
            if (library == null)
            {
                Debug.LogError($"[TagResolver] {name}: SetRules에 null 전달");
                return;
            }

            externalRuleLibrary = library;
            LoadRulesFromSO(library);
        }

        /// <summary>SO → 내부 필드 복사.</summary>
        private void LoadRulesFromSO(PersonalityTagRuleSO library)
        {
            rules.Clear();
            rules.AddRange(library.rules);

            Debug.Log($"[TagResolver] {name}: {library.rules.Count}개 규칙 로드됨 " +
                      $"(from {library.name})");
        }

        /// <summary>현재 규칙 요약 반환.</summary>
        public string GetRulesSummary()
        {
            return $"[TagResolver] rules={rules.Count}" +
                   (externalRuleLibrary != null ? $", SO={externalRuleLibrary.name}" : ", hardcoded");
        }

        // ==================================================================
        // 핵심 API: 성격으로부터 태그 발현
        // ==================================================================

        /// <summary>
        /// 주어진 PersonalityMatrix에서 태그를 확률적으로 발현.
        /// [NGO-3] 서버/단독 Play만 실행 가능. 클라에서 호출 시 현재 activeTags만 반환.
        /// </summary>
        public List<string> ResolveTagsFromPersonality(PersonalityMatrix personality)
        {
            // [NGO-3] 권한 가드
            if (!HasAuthority())
            {
                if (debugLog)
                    Debug.LogWarning($"[TagResolver] {name}: ResolveTagsFromPersonality는 서버 전용. " +
                                     $"클라이언트 호출 무시. (현재 activeTags 반환)");
                return new List<string>(activeTags);
            }

            // 기존 로직 (서버에서 결정)
            activeTags.Clear();

            // [DEBT-24 v2] NetworkList 갱신 조건에 IsSpawned 추가.
            // 이전 v1: Awake에서 무조건 new → 단독 Play에서도 isNetworked = true →
            //         NetworkList.Clear/Add가 MarkNetworkBehaviourDirty 시도 →
            //         NetworkObject 미스폰이라 'NetworkVariable is written to, but
            //         doesn't know its NetworkBehaviour yet' 워닝 매 호출마다 출력.
            // v2: IsSpawned 시에만 NetworkList 갱신. 단독 Play는 List<string> activeTags만 갱신.
            //     네트워크 Play 정상 동작 유지 (Spawn 후 OnNetworkSpawn에서 OnListChanged 등록 시점부터 IsSpawned == true).
            bool isNetworked = netActiveTags != null && IsSpawned;
            if (isNetworked) netActiveTags.Clear();

            foreach (var rule in rules)
            {
                if (!rule.CheckCondition(personality)) continue;

                float roll = UnityEngine.Random.Range(0f, 1f);
                if (roll <= rule.emergenceProbability)
                {
                    activeTags.Add(rule.tagName);

                    // [NGO-4] NetworkList에도 추가 → 전 클라 자동 전파
                    if (isNetworked)
                    {
                        if (rule.tagName.Length > 31)
                        {
                            Debug.LogWarning($"[TagResolver] 태그명 '{rule.tagName}' 31자 초과. 잘림.");
                        }
                        netActiveTags.Add(new FixedString32Bytes(rule.tagName));
                    }

                    if (debugLog)
                        Debug.Log($"[TagResolver] {name}: ✅ '{rule.tagName}' 발현 (roll={roll:F2} <= prob={rule.emergenceProbability:F2})");
                }
                else if (debugLog)
                {
                    Debug.Log($"[TagResolver] {name}: ❌ '{rule.tagName}' 미발현 (roll={roll:F2} > prob={rule.emergenceProbability:F2})");
                }
            }

            if (debugLog)
                Debug.Log($"[TagResolver] {name}: 최종 태그 [{string.Join(", ", activeTags)}] ({activeTags.Count}개)");

            return activeTags;
        }
    }
}