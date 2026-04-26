// =============================================================================
// SpeechDispatcher.cs  |  pB-4 Week 2 Day 4
// Layer  : L2 Router/Policy (검문소)
// Owner  : Person A
//
// 역할:
//   EventBus.OnSpeechTrigger 구독 → 검문 (쿨다운/거리/상태) 통과 시
//   Assembler.Assemble() → Renderer.Render() 파이프라인 호출.
//
// 4계층 원칙:
//   - [Rule 1] Domain을 직접 호출하지 않고 ISpeechAssembler 인터페이스로만.
//   - [Rule 3] OnEnable += / OnDisable -= 짝으로 생명주기 관리.
//   - [Rule 4] NetworkVariable 없음 — MonoBehaviour로 충분.
//
// [설계 결정 — Day 4 검토 후]
//   Dispatcher는 현재 네트워크 상태(NetworkVariable) 읽기/쓰기 없이
//   로컬 검문(쿨다운/거리)만 수행. NetworkBehaviour는 오버킬.
//   Week 7 멀티 발화 동기화 필요 시 NetworkBehaviour 승격 + Rpc 추가 예정.
//
// NGO 2.0 호환:
//   - 옵션: IsHost/IsServer 체크로 호스트 권한 발화만 허용 (멀티 대응).
//     HumanoidAIBrain.IsOwner 간접 참조로 단독 Play도 안전.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TDA.PB4.Core;           // EventBus
using TDA.PB4.Core.Events;    // [Day 4 수정] SpeechTriggerContext
using TDA.PB4.AI.Humanoid;     // HumanoidAIBrain

namespace TDA.PB4.AI.Speech
{
    /// <summary>L2 Router — SpeechTrigger 수신 → 검문 → Assembler/Renderer 배분.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpeechAssembler))]
    [RequireComponent(typeof(DialogueRenderer))]
    public class SpeechDispatcher : MonoBehaviour
    {
        [Header("━━━ 검문 파라미터 ━━━━━━━━━━━━━━━")]
        [Tooltip("같은 triggerId 중복 발화를 차단하는 쿨다운(초).")]
        [Range(0f, 30f)] public float cooldownSec = 3f;

        [Tooltip("플레이어가 이 거리 안에 있어야 발화. 0 = 거리 체크 안함.")]
        [Range(0f, 100f)] public float maxDistanceToPlayer = 15f;

        [Tooltip("[옵션 A] 플레이어가 씬에 없으면 발화 차단. true=엄격(권장), false=관대(레거시 호환).")]
        public bool requirePlayerPresence = true;

        [Tooltip("[디버그] 모든 검문 단계 통과/실패 로그를 자세히 출력. Production에서 false 권장.")]
        public bool verboseFilterLog = false;

        [Tooltip("발화 주체 식별자 일치 검사 (safety).")]
        public bool verifyCharacterId = true;

        [Tooltip("[멀티 대응] 서버(Host) 권한에서만 발화 허용. 단독 Play에서는 자동 true.")]
        public bool requireServerAuthority = true;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool debugLog = true;

        // 런타임 상태
        private Dictionary<string, float> lastSpokenTime = new Dictionary<string, float>();
        private ISpeechAssembler assembler;
        private DialogueRenderer renderer;
        private HumanoidAIBrain brain;
        private Transform playerTransform;
        private bool playerSearched = false;
        private float lastPlayerSearchTime = 0f;   // [FIX] 1회만 검색

        void Awake()
        {
            assembler = GetComponent<SpeechAssembler>();
            renderer = GetComponent<DialogueRenderer>();
            brain = GetComponent<HumanoidAIBrain>();
        }

        void OnEnable()
        {
            EventBus.OnSpeechTrigger += OnSpeechTrigger;  // [Rule 3]
            if (debugLog) Debug.Log($"[SpeechDispatcher] {name}: 구독 완료.", this);
        }

        void OnDisable()
        {
            EventBus.OnSpeechTrigger -= OnSpeechTrigger;  // [Rule 3] 짝
        }

        /// <summary>EventBus에서 발행된 SpeechTrigger 수신.</summary>
        private void OnSpeechTrigger(string triggerId, SpeechTriggerContext ctx)
        {
            // === 검문 1: 이 Dispatcher가 담당하는 NPC에 대한 이벤트인가? ===
            if (verifyCharacterId && ctx.characterId != name)
                return;  // 다른 NPC의 이벤트 무시

            // === 검문 2: 서버 권한 (멀티 대응) ===
            if (requireServerAuthority && !IsServerOrSinglePlayer())
            {
                if (debugLog) Debug.Log($"[SpeechDispatcher] {name}: 서버 권한 아님 — 발화 스킵.", this);
                return;
            }

            // === 검문 3: 내부 상태 ===
            if (!gameObject.activeInHierarchy)
                return;

            // === 검문 4: 쿨다운 ===
            if (IsOnCooldown(triggerId))
            {
                if (debugLog) Debug.Log($"[SpeechDispatcher] {name}: '{triggerId}' 쿨다운 중 — 발화 스킵.", this);
                return;
            }

            // === 검문 5: 플레이어 거리 / 존재 ===
            if (maxDistanceToPlayer > 0f && !IsPlayerInRange())
            {
                if (debugLog)
                {
                    string reason = (playerTransform == null)
                                  ? "플레이어가 씬에 없음 (requirePlayerPresence=true)"
                                  : $"플레이어 거리 초과 (max={maxDistanceToPlayer}m)";
                    Debug.Log(
                        $"<color=#FFB347>[SpeechDispatcher]</color> {name}: " +
                        $"trigger='{triggerId}' " +
                        $"context=[{ctx.previousAlignment}→{ctx.currentAlignment}, reason={ctx.reason}] " +
                        $"검문 실패 (플레이어) — {reason}. 발화 스킵.",
                        this);
                }
                return;
            }

            // === 검문 통과 → Domain(Assembler)에 조립 요청 ===
            string line = assembler.Assemble(triggerId, ctx);
            if (string.IsNullOrEmpty(line))
            {
                if (debugLog) Debug.Log($"[SpeechDispatcher] {name}: trigger='{triggerId}' " +
                      $"context=[{ctx.previousAlignment}→{ctx.currentAlignment}, reason={ctx.reason}] " +
                      $"Assembler가 빈 대사 반환 — 발화 스킵.", this);
                return;
            }

            SpeechTone tone = assembler.LastTone;

            // === L4 Renderer에 연출 위임 ===
            renderer.Render(line, tone);

            // 쿨다운 기록
            lastSpokenTime[triggerId] = Time.time;

            if (debugLog)
                Debug.Log($"<color=#90EE90>[SpeechDispatcher]</color> {name}: trigger='{triggerId}' " +
                          $"context=[{ctx.previousAlignment}→{ctx.currentAlignment}, reason={ctx.reason}] " +
                          $"발화 → \"{line}\" (tone={tone})", this);
        }

        // ================================================================
        // 디버그 도구 — Inspector ContextMenu에서 강제 발화/검문 우회
        // ================================================================

        [ContextMenu("DEBUG: 강제 발화 (현재 진영 기준)")]
        public void DispatcherDebugCurrentAlignment()
        {
            var alignmentCtrl = GetComponent<TDA.PB4.AI.NPCAlignmentController>();
            var current = alignmentCtrl != null
                        ? alignmentCtrl.CurrentAlignment
                        : TDA.PB4.Data.NPCAlignment.Neutral;

            string trigger = current switch
            {
                TDA.PB4.Data.NPCAlignment.Hostile => "hostile_default",
                TDA.PB4.Data.NPCAlignment.Friendly => "friendly_default",
                TDA.PB4.Data.NPCAlignment.Companion => "companion_default",
                _ => "neutral_default"
            };
            DebugForceSpeech(trigger);
        }

        [ContextMenu("DEBUG: friendly_default 강제 발화")]
        public void DispatcherDebugFriendly() => DebugForceSpeech("friendly_default");

        [ContextMenu("DEBUG: companion_default 강제 발화")]
        public void DispatcherDebugCompanion() => DebugForceSpeech("companion_default");

        [ContextMenu("DEBUG: hostile_default 강제 발화")]
        public void DispatcherDebugHostile() => DebugForceSpeech("hostile_default");

        /// <summary>
        /// 검문 우회 강제 발화. 디버그 전용. 쿨다운만 우회하고 다른 검문은 통과해야 함.
        /// 검문을 모두 우회하려면 ForceBypass=true 사용.
        /// </summary>
        public void DebugForceSpeech(string triggerId, bool bypassAllFilters = false)
        {
            Debug.Log($"<color=#FF8C00>[SpeechDispatcher.DEBUG]</color> {name}: 강제 발화 요청 trigger='{triggerId}' bypass={bypassAllFilters}", this);

            var alignmentCtrl = GetComponent<TDA.PB4.AI.NPCAlignmentController>();
            var current = alignmentCtrl != null
                        ? alignmentCtrl.CurrentAlignment
                        : TDA.PB4.Data.NPCAlignment.Neutral;

            var ctx = new TDA.PB4.Core.Events.SpeechTriggerContext
            {
                characterId = name,
                targetPlayerId = 0UL,
                currentAlignment = current,
                previousAlignment = current,
                reason = TDA.PB4.Interfaces.Narrative.AlignmentChangeReason.Manual
            };

            if (bypassAllFilters)
            {
                // 모든 검문 우회 — Assembler/Renderer만 직접 호출
                string line = assembler.Assemble(triggerId, ctx);
                if (!string.IsNullOrEmpty(line))
                {
                    renderer.Render(line, assembler.LastTone);
                    Debug.Log($"<color=#FF8C00>[DEBUG]</color> 강제 발화 완료 (모든 검문 우회).", this);
                }
                else
                {
                    Debug.LogWarning($"<color=#FF8C00>[DEBUG]</color> Assembler가 빈 대사 반환 — 강제 발화 실패.", this);
                }
            }
            else
            {
                // 정상 파이프라인 사용 (검문 적용)
                TDA.PB4.Core.EventBus.RaiseSpeechTrigger(triggerId, ctx);
            }
        }

        [ContextMenu("DEBUG: 모든 검문 우회 발화 (Friendly)")]
        public void DispatcherDebugBypass() => DebugForceSpeech("friendly_default", bypassAllFilters: true);

        [ContextMenu("DEBUG: 검문 상태 진단")]
        public void DispatcherDebugDiagnose()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<color=#FF8C00>[SpeechDispatcher.DEBUG]</color> {name}: 검문 상태 진단");

            // 검문 1: characterId
            sb.AppendLine($"  [1] characterId 검증: enabled={verifyCharacterId}, name='{name}'");

            // 검문 2: 서버 권한
            sb.AppendLine($"  [2] 서버 권한: required={requireServerAuthority}, isServerOrSingle={IsServerOrSinglePlayer()}");

            // 검문 3: GameObject 활성
            sb.AppendLine($"  [3] activeInHierarchy: {gameObject.activeInHierarchy}");

            // 검문 4: 쿨다운 상태
            sb.AppendLine($"  [4] 쿨다운 중인 trigger 목록 (cooldownSec={cooldownSec}s):");
            foreach (var kv in lastSpokenTime)
            {
                float remaining = cooldownSec - (Time.time - kv.Value);
                if (remaining > 0)
                    sb.AppendLine($"      - '{kv.Key}': 남은 {remaining:F1}s");
            }

            // 검문 5: 플레이어
            string playerStatus = playerTransform == null
                                ? "없음"
                                : $"있음 (거리 {Vector3.Distance(transform.position, playerTransform.position):F1}m / max {maxDistanceToPlayer}m)";
            sb.AppendLine($"  [5] 플레이어: {playerStatus}, requirePlayerPresence={requirePlayerPresence}");

            Debug.Log(sb.ToString(), this);
        }

        /// <summary>[FIX] Host/Server 권한 체크. NetworkManager 없으면 단독 Play로 판정 → true.</summary>
        private bool IsServerOrSinglePlayer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
                return true;  // 단독 Play 모드 → 항상 권한 있음
            return nm.IsServer;
        }

        private bool IsOnCooldown(string triggerId)
        {
            if (!lastSpokenTime.TryGetValue(triggerId, out float t)) return false;
            return (Time.time - t) < cooldownSec;
        }

        private bool IsPlayerInRange()
        {
            // 매 호출마다 재검색 (플레이어가 늦게 스폰되거나 죽고 다시 스폰될 수 있음)
            // 단, 1초 내 재호출 시 캐시 사용 (성능 보호)
            if (!playerSearched || Time.time - lastPlayerSearchTime > 1f)
            {
                playerTransform = FindPlayerTransform();
                playerSearched = true;
                lastPlayerSearchTime = Time.time;
            }

            if (playerTransform == null)
            {
                // [옵션 A] requirePlayerPresence=true이면 플레이어 없을 때 발화 차단
                return !requirePlayerPresence;
            }

            float sqrDist = (transform.position - playerTransform.position).sqrMagnitude;
            return sqrDist <= (maxDistanceToPlayer * maxDistanceToPlayer);
        }

        private Transform FindPlayerTransform()
        {
            try
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                return p != null ? p.transform : null;
            }
            catch (UnityException)
            {
                // "Player" 태그가 TagManager에 없으면 예외 발생 — 안전하게 null 반환
                return null;
            }
        }

        // === Test Lab / 수동 주입용 ===
        public void SetPlayerTransform(Transform t)
        {
            playerTransform = t;
            playerSearched = true;
        }

        /// <summary>쿨다운 리셋 (테스트 전용).</summary>
        public void ResetCooldowns() => lastSpokenTime.Clear();

        [ContextMenu("TEST: companion_default 강제 발화")]
        void DebugForceCompanion()
        {
            var ctx = new SpeechTriggerContext
            {
                characterId = name,
                targetPlayerId = 0UL,
                currentAlignment = TDA.PB4.Data.NPCAlignment.Companion,
                previousAlignment = TDA.PB4.Data.NPCAlignment.Friendly,
                reason = TDA.PB4.Interfaces.Narrative.AlignmentChangeReason.Manual
            };
            // 쿨다운 우회하여 테스트
            lastSpokenTime.Remove(SpeechTriggerIds.COMPANION_DEFAULT);
            OnSpeechTrigger(SpeechTriggerIds.COMPANION_DEFAULT, ctx);
        }
    }
}