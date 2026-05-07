// =============================================================================
// PersonalityTagResolver.cs  |  pB-4 Project — Week 2 Day 1 → v3 NGO 2.0 → v3.1 추적 로깅
// =============================================================================
//
// [★ v3.1 추적 로깅 — 2026-05-04]
//   - traceLogging 토글 추가 (debugLog 와 별개 — 디버그 폭주 방지)
//   - Trace() 헬퍼: ResolverID + NPC ID + tagsCount + tags내용 + ruleCount + Spawn 상태
//   - 6 시점 로깅:
//     1. OnNetworkSpawn 진입/종료
//     2. InitializeRulesSource
//     3. LoadRulesFromSO 진입/종료
//     4. SetRules 진입
//     5. ResolveTagsFromPersonality 진입 / 권한가드 / Clear / 종료
//     6. OnNetActiveTagsChanged 진입
//   - 색상 컨벤션:
//     #88FFFF 시안 — NetworkLifecycle (Spawn/Despawn)
//     #FFAA66 오렌지 — Rule Load
//     #88FF88 녹색 — Resolve (정상 발현)
//     #FF8866 빨강 — 의심 시점 (Clear/EmptyResult/AuthBlock)
//     #CCAAFF 자주 — Net 동기화
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    [System.Serializable]
    public class TagEmergenceRule
    {
        [Tooltip("태그 이름. UtilityMasterFormula의 PersonalityModifierRule.tagName과 일치해야 한다.")]
        public string tagName;

        [Header("━━━ 발현 조건 (5축 범위) ━━━━━━━━━")]
        [Range(-1f, 1f)] public float minControl = -1f;
        [Range(-1f, 2f)] public float maxControl = 2f;
        [Range(-1f, 1f)] public float minStability = -1f;
        [Range(-1f, 2f)] public float maxStability = 2f;
        [Range(-1f, 1f)] public float minOpenness = -1f;
        [Range(-1f, 2f)] public float maxOpenness = 2f;
        [Range(-1f, 1f)] public float minAgreeable = -1f;
        [Range(-1f, 2f)] public float maxAgreeable = 2f;
        [Range(-1f, 1f)] public float minDirectness = -1f;
        [Range(-1f, 2f)] public float maxDirectness = 2f;

        [Header("━━━ 발현 확률 ━━━━━━━━━━━━━━━━━━")]
        [Range(0f, 1f)] public float emergenceProbability = 0.7f;

        [Header("━━━ 유틸리티 보정 (빠른 설정) ━━━━━━")]
        public float fearMultiplier = 0f;
        public float greedKOverride = 0f;

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

    public class PersonalityTagResolver : NetworkBehaviour
    {
        [Header("━━━ 태그 발현 규칙 (15종) ━━━━━━━━━━━━")]
        [SerializeField] private List<TagEmergenceRule> rules = new List<TagEmergenceRule>();

        [Header("━━━ 현재 발현된 태그 (Read Only) ━━━━━━")]
        [SerializeField] private List<string> activeTags = new List<string>();

        private NetworkList<FixedString32Bytes> netActiveTags;

        public IReadOnlyList<string> ActiveTags => activeTags;

        [Header("━━━ Week 2 T1.4 외부 규칙 (선택) ━━━━")]
        [SerializeField] private PersonalityTagRuleSO externalRuleLibrary;

        [Header("━━━ Debug ━━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("기존: Resolve 시 각 rule 의 ✅/❌ 출력")]
        public bool debugLog = false;

        [Tooltip("★ v3.1 — 추적 로깅. 모든 진입/종료 시점에 ResolverID + tagsCount + 상태 출력. " +
                 "ActiveTags 가 어디서 0 으로 초기화되는지 캡처용.")]
        public bool traceLogging = true;

        private void Awake()
        {
            if (netActiveTags == null)
            {
                netActiveTags = new NetworkList<FixedString32Bytes>(
                    readPerm: NetworkVariableReadPermission.Everyone,
                    writePerm: NetworkVariableWritePermission.Server);
            }
            Trace("#88FFFF", "Awake", $"netActiveTags 초기화");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Trace("#88FFFF", "OnNetworkSpawn ENTER", $"IsServer={IsServer} IsClient={IsClient}");

            netActiveTags.OnListChanged += OnNetActiveTagsChanged;

            if (HasAuthority())
            {
                InitializeRulesSource();
            }

            Trace("#88FFFF", "OnNetworkSpawn EXIT", "OnListChanged 등록 완료");
        }

        private void InitializeRulesSource()
        {
            Trace("#FFAA66", "InitializeRulesSource ENTER",
                $"externalRuleLibrary={(externalRuleLibrary != null ? externalRuleLibrary.name : "NULL")}");

            if (externalRuleLibrary != null)
            {
                LoadRulesFromSO(externalRuleLibrary);
            }
            else if (rules.Count == 0)
            {
                if (HasAuthority())
                    StartCoroutine(VerifyRulesLoadedAfterFrame());
            }

            Trace("#FFAA66", "InitializeRulesSource EXIT", $"rules.Count={rules.Count}");
        }

        private void OnNetActiveTagsChanged(NetworkListEvent<FixedString32Bytes> e)
        {
            int beforeCount = activeTags.Count;
            activeTags.Clear();
            for (int i = 0; i < netActiveTags.Count; i++)
                activeTags.Add(netActiveTags[i].ToString());

            // 비어있을 때 의심 색상
            string color = activeTags.Count == 0 ? "#FF8866" : "#CCAAFF";
            Trace(color, "OnNetActiveTagsChanged",
                $"event={e.Type} netCount={netActiveTags.Count} (was activeTags={beforeCount} → {activeTags.Count})");

            if (debugLog)
                Debug.Log($"[TagResolver] {name}: NetworkList 변경 수신 → activeTags 동기화 ({activeTags.Count}개)");
        }

        public override void OnNetworkDespawn()
        {
            Trace("#88FFFF", "OnNetworkDespawn", "");
            if (netActiveTags != null)
            {
                netActiveTags.OnListChanged -= OnNetActiveTagsChanged;
            }
            base.OnNetworkDespawn();
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

        private bool HasAuthority()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return true;
            return IsServer;
        }

        public void SetRules(PersonalityTagRuleSO library)
        {
            Trace("#FFAA66", "SetRules ENTER",
                $"library={(library != null ? library.name : "NULL")}");

            if (library == null)
            {
                Debug.LogError($"[TagResolver] {name}: SetRules에 null 전달");
                return;
            }

            externalRuleLibrary = library;
            LoadRulesFromSO(library);

            Trace("#FFAA66", "SetRules EXIT", $"rules.Count={rules.Count}");
        }

        private void LoadRulesFromSO(PersonalityTagRuleSO library)
        {
            int beforeRuleCount = rules.Count;
            int beforeTagCount = activeTags.Count;

            rules.Clear();
            rules.AddRange(library.rules);

            // ★ 의심 — rules.Clear() 가 activeTags 영향?
            string color = (beforeTagCount > 0 && activeTags.Count == 0) ? "#FF8866" : "#FFAA66";
            Trace(color, "LoadRulesFromSO",
                $"from={library.name} rules:{beforeRuleCount}→{rules.Count} " +
                $"activeTags:{beforeTagCount}→{activeTags.Count}");

            Debug.Log($"[TagResolver] {name}: {library.rules.Count}개 규칙 로드됨 " +
                      $"(from {library.name})");
        }

        public string GetRulesSummary()
        {
            return $"[TagResolver] rules={rules.Count}" +
                   (externalRuleLibrary != null ? $", SO={externalRuleLibrary.name}" : ", hardcoded");
        }

        // ==================================================================
        // 핵심 API: 성격으로부터 태그 발현
        // ==================================================================

        public List<string> ResolveTagsFromPersonality(PersonalityMatrix personality)
        {
            int beforeCount = activeTags.Count;
            string callerInfo = $"P=(C{personality.control:F2}/S{personality.stability:F2}/" +
                                $"O{personality.openness:F2}/A{personality.agreeable:F2}/" +
                                $"D{personality.directness:F2})";

            Trace("#88FF88", "Resolve ENTER",
                $"{callerInfo} rules={rules.Count} beforeActive={beforeCount}");

            // [NGO-3] 권한 가드
            if (!HasAuthority())
            {
                Trace("#FF8866", "Resolve AUTH BLOCKED",
                    $"NetworkManager.Listening={NetworkManager.Singleton?.IsListening} " +
                    $"IsServer={IsServer} → 빈 배열 반환 (현재 activeTags 사본)");

                if (debugLog)
                    Debug.LogWarning($"[TagResolver] {name}: ResolveTagsFromPersonality는 서버 전용. " +
                                     $"클라이언트 호출 무시. (현재 activeTags 반환)");
                return new List<string>(activeTags);
            }

            // ★ 의심 시점 — Clear
            Trace("#FF8866", "Resolve CLEAR",
                $"activeTags.Clear() 직전 (count={activeTags.Count}) — 이 시점부터 빈 배열");
            activeTags.Clear();

            // [DEBT-24 v2] NetworkList 갱신 조건에 IsSpawned 추가.
            bool isNetworked = netActiveTags != null && IsSpawned;
            if (isNetworked)
            {
                Trace("#CCAAFF", "Resolve NetClear",
                    $"netActiveTags.Clear() 직전 (count={netActiveTags.Count})");
                netActiveTags.Clear();
            }

            int passedCount = 0;
            int firedCount = 0;

            foreach (var rule in rules)
            {
                if (!rule.CheckCondition(personality)) continue;
                passedCount++;

                float roll = UnityEngine.Random.Range(0f, 1f);
                if (roll <= rule.emergenceProbability)
                {
                    activeTags.Add(rule.tagName);
                    firedCount++;

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

            // ★ 종료 — 결과가 0 이면 빨강
            string color = activeTags.Count == 0 ? "#FF8866" : "#88FF88";
            Trace(color, "Resolve EXIT",
                $"rules={rules.Count} passed={passedCount} fired={firedCount} " +
                $"final={activeTags.Count}={(activeTags.Count > 0 ? string.Join(",", activeTags) : "[ ]")}");

            if (debugLog)
                Debug.Log($"[TagResolver] {name}: 최종 태그 [{string.Join(", ", activeTags)}] ({activeTags.Count}개)");

            return activeTags;
        }

        // ==================================================================
        // ★ v3.1 — 추적 로깅 헬퍼
        // ==================================================================

        private void Trace(string color, string callSite, string detail)
        {
            if (!traceLogging) return;

            int rid = GetInstanceID();
            int npcId = transform != null ? transform.GetInstanceID() : -1;
            int ruleCount = rules?.Count ?? 0;
            int tagsCount = activeTags?.Count ?? -1;
            string tagsStr = activeTags == null
                ? "<NULL>"
                : (activeTags.Count > 0 ? string.Join(",", activeTags) : "[ ]");
            string spawnStatus = IsSpawned ? "Spawn=T" : "Spawn=F";
            string netStatus = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                ? (IsServer ? "Auth=Srv" : "Auth=Cli")
                : "Auth=Solo";

            Debug.Log(
                $"<color=#888888>[R:{rid}|N:{npcId}]</color> " +
                $"<color={color}><b>[Resolver/{callSite}]</b></color> " +
                $"<color=#FFCC44>{name}</color> | " +
                $"tags={tagsCount}={tagsStr} | rules={ruleCount} | {spawnStatus} | {netStatus} | {detail}");
        }

        // ==================================================================
        // ★ v3.1 — 진단 ContextMenu
        // ==================================================================

        [ContextMenu("★ Trace Current State")]
        public void TraceCurrentStateMenu()
        {
            bool oldTrace = traceLogging;
            traceLogging = true;
            Trace("#FFCC44", "TraceCurrentState (Force)",
                $"externalSO={(externalRuleLibrary != null ? externalRuleLibrary.name : "NULL")}");
            traceLogging = oldTrace;
        }

        [ContextMenu("★ Force Resolve (Test)")]
        public void ForceResolveMenu()
        {
            // 테스트용 personality (Coward 매핑 보장)
            var testP = new PersonalityMatrix
            {
                control = 0.5f,
                stability = 0.2f,
                openness = 0.5f,
                agreeable = 0.5f,
                directness = 0.3f,
            };

            bool oldTrace = traceLogging;
            traceLogging = true;
            Trace("#FFCC44", "ForceResolve (Test)", "stability=0.2 → Coward 매칭 예상");
            ResolveTagsFromPersonality(testP);
            traceLogging = oldTrace;
        }
    }
}