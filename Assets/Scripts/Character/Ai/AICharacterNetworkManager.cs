// =============================================================================
// AICharacterNetworkManager.cs  |  TDA Project
// Layer  : L2 Router — AI 전용 네트워크 동기화 백본
//
// 역할:
//   CharacterNetworkManager 를 상속하여 AI(몬스터/NPC)에 특화된
//   NetworkVariable 과 RPC 를 추가합니다.
//
// 플레이어와의 차이:
//   - 스태미나 소모·보간 없음 (AI 는 스태미나 쿨다운 없이 공격)
//   - 워핑(Warping) 불필요 — AI 는 에임 어시스트 없이 NavMesh 이동
//   - 인벤토리 동기화 최소화 — 무기 ID 만 전파 (Player 급 장비 슬롯 불필요)
//   - Aggro(어그로) / Phase(페이즈) / SpawnChunk 좌표 서버 전파 추가
//
// 아키텍처 규약:
//   ① NetworkVariable 변경은 반드시 Owner(= 서버) 에서만 수행
//   ② Rpc 는 NGO 2.0 [Rpc(SendTo.Server)] / [Rpc(SendTo.ClientsAndHost)] 사용
//   ③ 모든 OnValueChanged 구독은 OnNetworkSpawn/OnNetworkDespawn 쌍으로 관리
//
// [v1.1 — 씬 배치 자동 Spawn 추가]
//   ★ 씬에 수동 배치된 AI 프리팹의 NetworkObject 자체-Spawn 로직 통합.
//     각 AI가 자기 NetworkObject 라이프사이클을 관리하는 분산 책임 패턴.
//     NetworkManager 상태 자동 감지 → 4가지 케이스 분기:
//       - Singleton == null               → 오프라인 모드, 무동작
//       - !IsListening                    → OnServerStarted 구독, 호스트 시작 시 자체 Spawn
//       - IsListening && IsServer         → 즉시 자체 Spawn
//       - IsListening && !IsServer (client)→ 무동작 (서버 권한, 클라는 자동 수신)
//   ★ 기존 OnNetworkSpawn 흐름과 충돌 없음:
//     자체 Spawn 호출 → NGO가 OnNetworkSpawn() 콜백 발화 → 기존 NetworkVariable 구독 정상 동작.
//   ★ 런타임 스폰된 AI(CaveSpawnerManager 경유)는 Start 시점에 IsSpawned=true 이므로 자동 스킵.
// =============================================================================
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L2 Router] AI(몬스터/NPC) 전용 네트워크 동기화 매니저.
    /// CharacterNetworkManager 의 공통 변수(HP·포이즈·위치·애니메이션)를 그대로 상속하며
    /// AI 전용 어그로·페이즈·스폰 정보를 추가합니다.
    /// </summary>
    public class AICharacterNetworkManager : CharacterNetworkManager
    {
        // =====================================================================
        // AI 전용 NetworkVariable — 서버 권위 (Server Write)
        // =====================================================================

        [Header("AI — Aggro (어그로)")]
        /// <summary>
        /// 현재 AI 가 어그로를 유지하고 있는 플레이어의 NetworkObjectId.
        /// 0 이면 타겟 없음.
        /// CharacterManager.currentTarget 과 이중 동기화하여
        /// 타 클라이언트도 AI 의 공격 대상을 알 수 있게 합니다.
        /// </summary>
        public NetworkVariable<ulong> aggroTargetNetworkObjectID = new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// 탐지 시야(FOV) 이내에 플레이어가 있는지 서버가 매 틱 갱신합니다.
        /// 클라이언트는 이 값으로 경계음(Alert SFX) 재생 여부를 판단합니다.
        /// </summary>
        public NetworkVariable<bool> isAlerted = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Header("AI — Phase (보스 페이즈)")]
        /// <summary>
        /// 보스 전용 페이즈 번호. 일반 몬스터는 항상 0.
        /// 페이즈 전환 시 클라이언트에서 연출(연기, 카메라 진동)을 발동합니다.
        /// </summary>
        public NetworkVariable<int> currentPhase = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Header("AI — Spawn Info")]
        /// <summary>
        /// AI 가 스폰된 Cave 청크 좌표.
        /// CaveSpawnerManager 가 스폰 직후 설정하며, 리스폰·재배치 시 참조합니다.
        /// </summary>
        public NetworkVariable<Vector3> spawnChunkCenter = new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // =====================================================================
        // [v1.1] 씬 배치 자동 Spawn 설정
        // =====================================================================

        [Header("━━━ 씬 배치 자동 Spawn (v1.1) ━━━━━━━")]
        [Tooltip("씬에 수동 배치된 AI 프리팹의 NetworkObject 자체-Spawn 활성화 여부. " +
                 "true(기본)이면 Start 시점에 NetworkManager 상태를 감지하여 필요 시 자체 Spawn 호출. " +
                 "런타임 스폰된 AI(CaveSpawnerManager 등)는 IsSpawned=true이므로 자동 스킵. " +
                 "프리팹 단위로 끄고 싶다면 인스턴스에서 false 설정.")]
        public bool autoSpawnIfPlacedInScene = true;

        [Tooltip("자동 Spawn 진단 로그 출력. " +
                 "Spawn 성공/실패/스킵 사유, OnServerStarted 구독 상태 등을 콘솔에 출력. " +
                 "Warning은 이 플래그 무관하게 항상 출력.")]
        public bool autoSpawnDebugLog = false;

        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager _ai;

        // [v1.1] 자동 Spawn 상태 추적 (중복 호출 방지)
        private bool _autoSpawnInvoked = false;
        private bool _autoSpawnSubscribedToServerStart = false;

        // =====================================================================
        // 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();
            _ai = GetComponent<AICharacterManager>();
        }

        // [v1.1] Start 시점에 자체-Spawn 시도.
        // Awake 보다 늦게 실행되므로 NetworkManager.Singleton 참조 가능성이 높다.
        private void Start()
        {
            TrySelfSpawnIfNeeded();
        }

        private void OnDestroy()
        {
            // [v1.1] OnServerStarted 구독 정리 (씬 전환/오브젝트 파괴 시 누수 방지)
            if (_autoSpawnSubscribedToServerStart && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= OnServerStartedSelfSpawn;
                _autoSpawnSubscribedToServerStart = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // 어그로 타겟이 바뀌면 aiCharacterCombatManager.currentTarget 갱신
            aggroTargetNetworkObjectID.OnValueChanged += OnAggroTargetChanged;

            // 경계 상태가 바뀌면 시각·청각 반응 처리
            isAlerted.OnValueChanged += OnIsAlertedChanged;

            // 보스 페이즈 전환 시 연출 트리거
            currentPhase.OnValueChanged += OnPhaseChanged;
        }

        public override void OnNetworkDespawn()
        {
            aggroTargetNetworkObjectID.OnValueChanged -= OnAggroTargetChanged;
            isAlerted.OnValueChanged -= OnIsAlertedChanged;
            currentPhase.OnValueChanged -= OnPhaseChanged;

            base.OnNetworkDespawn();
        }

        // =====================================================================
        // OnValueChanged 핸들러
        // =====================================================================

        /// <summary>
        /// 어그로 타겟 NetworkObjectId 변경 → 로컬 currentTarget 참조 갱신.
        /// CharacterNetworkManager.OnLockOnTargetIDChange 와 동일한 패턴.
        /// </summary>
        private void OnAggroTargetChanged(ulong oldId, ulong newId)
        {
            if (_ai == null || _ai.aiCharacterCombatManager == null) return;

            if (newId == 0)
            {
                _ai.aiCharacterCombatManager.currentTarget = null;
                return;
            }

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(newId, out NetworkObject netObj))
            {
                _ai.aiCharacterCombatManager.currentTarget =
                    netObj.GetComponent<CharacterManager>();
            }
        }

        /// <summary>
        /// 경계 상태 전환 — 경계음(Alert) 이벤트 발송.
        /// </summary>
        private void OnIsAlertedChanged(bool wasAlerted, bool nowAlerted)
        {
            if (_ai == null || _ai.characterEventManager == null) return;

            // 경계 진입 시 SFX 이벤트 발송 (CharacterSoundFxManager 가 수신)
            if (!wasAlerted && nowAlerted)
            {
                _ai.characterEventManager.NotifyAnimationEvent(
                    AnimationEventType.PlayVoice_Stagger, "AIAlert");
            }
        }

        /// <summary>
        /// 보스 페이즈 전환 — 클라이언트 연출 트리거.
        /// WorldCameraManager / CharacterEffectsManager 가 이벤트를 수신합니다.
        /// </summary>
        private void OnPhaseChanged(int oldPhase, int newPhase)
        {
            if (_ai == null || _ai.characterEventManager == null) return;

            // 페이즈 전환 이벤트 발송 (추후 AnimationEventType.BossPhaseTransition 추가 권장)
            Debug.Log($"<color=red>[AINetworkManager:{_ai.name}]</color> 페이즈 전환 {oldPhase} → {newPhase}");
        }

        // =====================================================================
        // AI 전용 공개 메서드 — 서버에서만 호출
        // =====================================================================

        /// <summary>
        /// 어그로 타겟을 설정합니다. IsServer 게이트 포함.
        /// AICharacterCombatManager 에서 currentTarget 변경 시 함께 호출합니다.
        /// </summary>
        public void SetAggroTarget(CharacterManager target)
        {
            if (!IsServer) return;
            aggroTargetNetworkObjectID.Value = target != null
                ? target.GetComponent<NetworkObject>().NetworkObjectId
                : 0;
        }

        /// <summary>
        /// 경계 상태를 설정합니다. AI FOV 탐지 루프에서 호출합니다.
        /// </summary>
        public void SetAlerted(bool alerted)
        {
            if (!IsServer) return;
            if (isAlerted.Value == alerted) return;
            isAlerted.Value = alerted;
        }

        /// <summary>
        /// 보스 페이즈를 전환합니다.
        /// </summary>
        public void SetPhase(int phase)
        {
            if (!IsServer) return;
            currentPhase.Value = phase;
        }

        // =====================================================================
        // 모션 워핑 RPC — AI 는 워핑 사용 안 함, 빈 override 로 무해화
        // =====================================================================
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public override void NotifyWarpAttackServerRpc(ulong targetId, int boneIndex)
        {
            // AI 는 모션 워핑을 사용하지 않으므로 의도적으로 비워둡니다.
        }

        [Rpc(SendTo.ClientsAndHost)]
        public override void NotifyWarpAttackClientRpc(ulong targetId, int boneIndex)
        {
            // AI 는 모션 워핑을 사용하지 않으므로 의도적으로 비워둡니다.
        }

        // =====================================================================
        // [v1.1] 씬 배치 자동 Spawn — 핵심 로직
        // =====================================================================

        /// <summary>
        /// NetworkManager 상태에 따라 자체 Spawn 분기 실행.
        /// Start() 에서 한 번 호출. 인스펙터에서 [Force Self Spawn Now] 컨텍스트 메뉴로도 호출 가능.
        ///
        /// 분기 매트릭스:
        ///   ┌───────────────────────────────────────┬────────────────────────────────┐
        ///   │ 상태                                   │ 동작                          │
        ///   ├───────────────────────────────────────┼────────────────────────────────┤
        ///   │ NetworkObject == null                  │ 경고 + 종료                   │
        ///   │ NetworkObject.IsSpawned == true        │ 이미 정상 → 스킵              │
        ///   │ NetworkManager.Singleton == null       │ 오프라인 모드 → 스킵          │
        ///   │ IsListening && IsServer                │ 즉시 Spawn                    │
        ///   │ IsListening && !IsServer (client)      │ 서버 권한 없음 → 스킵         │
        ///   │ !IsListening                           │ OnServerStarted 구독 → 대기   │
        ///   └───────────────────────────────────────┴────────────────────────────────┘
        /// </summary>
        [ContextMenu("Force Self Spawn Now")]
        public void TrySelfSpawnIfNeeded()
        {
            if (!autoSpawnIfPlacedInScene) return;
            if (_autoSpawnInvoked) return;

            var netObj = NetworkObject;
            if (netObj == null)
            {
                Debug.LogWarning(
                    $"[AINetSpawn:WARN:{name}] NetworkObject 컴포넌트 없음 — 자체 Spawn 불가.", this);
                return;
            }

            // NGO가 이미 정상 처리한 경우 → 스킵 (가장 흔한 정상 케이스)
            if (netObj.IsSpawned)
            {
                AutoSpawnLog("#90A4AE", "이미 IsSpawned=true → NGO가 정상 처리함, 스킵.");
                return;
            }

            // 오프라인 모드
            if (NetworkManager.Singleton == null)
            {
                AutoSpawnLog("#90A4AE",
                    "NetworkManager.Singleton == null → 오프라인 모드. " +
                    "Mob은 일반 GameObject로 동작 (CharacterManager.isNetworkActive 가드가 처리).");
                return;
            }

            var nm = NetworkManager.Singleton;

            // 호스트 활성 → 즉시 Spawn
            if (nm.IsListening && nm.IsServer)
            {
                AutoSpawnLog("#FFD54F",
                    "호스트 활성 (IsListening && IsServer) → 즉시 자체 Spawn 시도.");
                DoSpawn();
                return;
            }

            // 클라이언트 → 무동작 (서버가 Spawn하면 자동 수신)
            if (nm.IsListening && !nm.IsServer)
            {
                AutoSpawnLog("#90A4AE",
                    "클라이언트 모드 (IsListening && !IsServer) → 서버가 Spawn할 때까지 대기. " +
                    "서버 Spawn 후 자동 수신됨.");
                return;
            }

            // NetworkManager 존재하지만 미시작 → OnServerStarted 구독
            nm.OnServerStarted += OnServerStartedSelfSpawn;
            _autoSpawnSubscribedToServerStart = true;
            AutoSpawnLog("#FFD54F",
                "호스트 미시작 (NetworkManager 존재, !IsListening) → OnServerStarted 구독. " +
                "추후 StartHost 호출 시점에 자동 Spawn 예정.");
        }

        /// <summary>
        /// NetworkManager.OnServerStarted 콜백.
        /// 호스트가 시작된 시점에 자체 Spawn을 시도한다.
        /// </summary>
        private void OnServerStartedSelfSpawn()
        {
            // 1회성 콜백 — 즉시 구독 해제
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnServerStarted -= OnServerStartedSelfSpawn;
            _autoSpawnSubscribedToServerStart = false;

            AutoSpawnLog("#FFD54F", "OnServerStarted 수신 → 자체 Spawn 시도.");
            DoSpawn();
        }

        /// <summary>
        /// 실제 NetworkObject.Spawn() 호출. 모든 가드 검사를 다시 수행하여 안전성 보장.
        /// </summary>
        private void DoSpawn()
        {
            if (_autoSpawnInvoked) return;

            var netObj = NetworkObject;
            if (netObj == null) return;
            if (netObj.IsSpawned)
            {
                _autoSpawnInvoked = true;
                AutoSpawnLog("#90A4AE", "DoSpawn 시점에 이미 IsSpawned=true → 스킵.");
                return;
            }
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning(
                    $"[AINetSpawn:WARN:{name}] DoSpawn 호출됐지만 서버 권한 없음 → 무시.", this);
                return;
            }

            _autoSpawnInvoked = true;

            try
            {
                netObj.Spawn(true);  // destroyWithScene=true (씬 배치 객체에 적합)
                AutoSpawnLog("#FFD54F",
                    $"<b>✔ 자체 Spawn 성공</b> (NetworkObjectId={netObj.NetworkObjectId})");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    $"[AINetSpawn:WARN:{name}] NetworkObject.Spawn() 예외 → " +
                    $"{e.GetType().Name}: {e.Message}", this);
            }
        }

        /// <summary>
        /// 자동 Spawn 진단 로그. autoSpawnDebugLog=true일 때만 출력.
        /// </summary>
        private void AutoSpawnLog(string colorHex, string msg)
        {
            if (!autoSpawnDebugLog) return;
            Debug.Log($"<color={colorHex}>[AINetSpawn:{name}]</color> {msg}", this);
        }
    }
}