using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TDA.Character.Player;
using TDA.Items;

namespace TDA.Diagnostics
{
    // ════════════════════════════════════════════════════════════════════
    // PlayerInputDiagnostics.cs  |  무기 전환 입력 진단 도구
    // ════════════════════════════════════════════════════════════════════
    //
    // 【용도】
    //   "Z/C 누르면 칼 뽑기" 가 됐다 안 됐다 하는 race condition 진단.
    //   PlayerManager / PlayerInputManager 코드 변경 X (비침투적).
    //
    // 【설치】
    //   1. 빈 GameObject 생성 — "[Diag] PlayerInput"
    //   2. 본 컴포넌트 추가 (어디서든 root)
    //   3. Play 모드 진입 → Z/C 누름
    //   4. Console + Inspector 에서 진단 결과 자동 표시
    //
    // 【진단 항목】
    //   ○ LocalPlayer 참조 (IsOwner = true 인 PlayerManager)
    //   ○ LocalPlayer 변경 감지 (씬 전환 후 새 Player 등록 여부)
    //   ○ playerNetworkManager.isDead.Value (차단 조건 1)
    //   ○ isPerformingAction (차단 조건 2)
    //   ○ playerEquipmentManager null (차단 조건 3 — NRE)
    //   ○ WorldItemDatabase.Instance null (의존성)
    //   ○ currentRightHandWeaponID / currentLeftHandWeaponID
    //   ○ Z/C 키 입력 카운트
    //
    // 【출력 위치】
    //   ○ Inspector — 실시간 상태 (★ Tracked State / Input Statistics)
    //   ○ Console — Z/C 누른 직후 자동 분석 + 차단 사유 명시
    //
    // 【ContextMenu】
    //   - Print Diagnostic Now (즉시 분석)
    //   - Force Refresh LocalPlayer (강제 재검색)
    // ════════════════════════════════════════════════════════════════════
    
    public class PlayerInputDiagnostics : MonoBehaviour
    {
        // ════════════════════════════════════════════
        // Settings
        // ════════════════════════════════════════════
        
        [Header("Diagnostic Settings")]
        [SerializeField, Tooltip("Z/C 키 누름 시 자동 진단 출력")]
        private bool _logOnKeyPress = true;
        
        [SerializeField, Tooltip("LocalPlayer 가 바뀔 때 로그")]
        private bool _logPlayerChanges = true;
        
        [SerializeField, Tooltip("매 1초마다 자동 진단 (디버깅 시만 ON)")]
        private bool _logEverySecond = false;
        
        // ════════════════════════════════════════════
        // Tracked State (Runtime, Inspector 표시)
        // ════════════════════════════════════════════
        
        [Header("★ Tracked State (Runtime, Read-only)")]
        [SerializeField] private string _localPlayerName = "(찾는 중...)";
        [SerializeField] private int _localPlayerInstanceID;
        [SerializeField] private bool _isOwner;
        [SerializeField] private bool _isSpawned;
        [SerializeField] private bool _isDead;
        [SerializeField] private bool _isPerformingAction;
        [SerializeField] private bool _hasNetworkManager;
        [SerializeField] private bool _hasEquipmentManager;
        [SerializeField] private bool _hasItemDatabase;
        [SerializeField] private int _currentRightWeaponID;
        [SerializeField] private int _currentLeftWeaponID;
        
        [Header("★ Input Statistics")]
        [SerializeField] private int _zKeyPressCount;
        [SerializeField] private int _cKeyPressCount;
        [SerializeField] private string _lastInputTime = "(아직 없음)";
        [SerializeField, TextArea(2, 5)] private string _lastBlockReason = "(분석 안 됨)";
        
        // ════════════════════════════════════════════
        // Internal
        // ════════════════════════════════════════════
        
        private PlayerManager _trackedPlayer;
        private float _nextPeriodicLogTime;
        
        // ════════════════════════════════════════════
        // Lifecycle
        // ════════════════════════════════════════════
        
        private void Awake()
        {
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            
            Debug.Log($"[InputDiag] 진단 도구 시작 — Z/C 키 누름 시 자동 분석.", this);
        }
        
        private void Update()
        {
            UpdateTrackedPlayer();
            UpdateState();
            DetectKeyPresses();
            
            if (_logEverySecond && Time.time >= _nextPeriodicLogTime)
            {
                _nextPeriodicLogTime = Time.time + 1f;
                PrintDiagnostic("Periodic");
            }
        }
        
        // ════════════════════════════════════════════
        // LocalPlayer 추적
        // ════════════════════════════════════════════
        
        private void UpdateTrackedPlayer()
        {
            var newPlayer = FindLocalPlayer();
            
            if (newPlayer != _trackedPlayer)
            {
                if (_logPlayerChanges)
                {
                    Debug.Log(
                        $"[InputDiag] LocalPlayer 변경: " +
                        $"{(_trackedPlayer != null ? $"{_trackedPlayer.gameObject.name} (id={_trackedPlayer.GetInstanceID()})" : "null")} → " +
                        $"{(newPlayer != null ? $"{newPlayer.gameObject.name} (id={newPlayer.GetInstanceID()})" : "null")}",
                        this);
                }
                _trackedPlayer = newPlayer;
            }
        }
        
        private PlayerManager FindLocalPlayer()
        {
            var all = FindObjectsByType<PlayerManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            
            foreach (var p in all)
            {
                if (p == null) continue;
                if (p.IsOwner) return p;
            }
            
            return null;
        }
        
        // ════════════════════════════════════════════
        // 상태 갱신
        // ════════════════════════════════════════════
        
        private void UpdateState()
        {
            if (_trackedPlayer == null)
            {
                _localPlayerName = "(LocalPlayer 없음)";
                _localPlayerInstanceID = 0;
                _isOwner = false;
                _isSpawned = false;
                _isDead = false;
                _isPerformingAction = false;
                _hasNetworkManager = false;
                _hasEquipmentManager = false;
            }
            else
            {
                _localPlayerName = _trackedPlayer.gameObject.name;
                _localPlayerInstanceID = _trackedPlayer.GetInstanceID();
                _isOwner = _trackedPlayer.IsOwner;
                _isSpawned = _trackedPlayer.IsSpawned;
                _isPerformingAction = _trackedPlayer.isPerformingAction;
                _hasNetworkManager = _trackedPlayer.playerNetworkManager != null;
                _hasEquipmentManager = _trackedPlayer.playerEquipmentManager != null;
                
                if (_hasNetworkManager)
                {
                    _isDead = _trackedPlayer.playerNetworkManager.isDead.Value;
                    _currentRightWeaponID = _trackedPlayer.playerNetworkManager.currentRightHandWeaponID.Value;
                    _currentLeftWeaponID = _trackedPlayer.playerNetworkManager.currentLeftHandWeaponID.Value;
                }
                else
                {
                    _isDead = false;
                    _currentRightWeaponID = -999;
                    _currentLeftWeaponID = -999;
                }
            }
            
            _hasItemDatabase = WorldItemDatabase.Instance != null;
        }
        
        // ════════════════════════════════════════════
        // Z/C 키 감지 (Input System)
        // ════════════════════════════════════════════
        
        private void DetectKeyPresses()
        {
            if (Keyboard.current == null) return;
            
            if (Keyboard.current.zKey.wasPressedThisFrame)
            {
                _zKeyPressCount++;
                _lastInputTime = $"{Time.time:F2}s (Z key)";
                if (_logOnKeyPress) PrintDiagnostic("Z key pressed");
            }
            
            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                _cKeyPressCount++;
                _lastInputTime = $"{Time.time:F2}s (C key)";
                if (_logOnKeyPress) PrintDiagnostic("C key pressed");
            }
        }
        
        // ════════════════════════════════════════════
        // 진단 출력
        // ════════════════════════════════════════════
        
        [ContextMenu("Print Diagnostic Now")]
        public void PrintDiagnosticNow() => PrintDiagnostic("Manual");
        
        private void PrintDiagnostic(string trigger)
        {
            Debug.Log($"════════ [InputDiag] {trigger} @ {Time.time:F2}s ════════", this);
            
            if (_trackedPlayer == null)
            {
                Debug.LogError(
                    $"  ★ LocalPlayer 없음!\n" +
                    $"     - PlayerManager 가 씬에 존재하지 않음\n" +
                    $"     - 또는 어떤 PlayerManager 도 IsOwner = true 가 아님\n" +
                    $"     - NGO 가 아직 Spawn 안 됨 가능성\n" +
                    $"  → PlayerInputManager.player 가 옛 Player 또는 null 가리키는 상태\n" +
                    $"     무기 전환 입력은 도달하지만 대상이 없어 NRE 또는 무반응", this);
                _lastBlockReason = "★ LocalPlayer null (PlayerInputManager.player 끊김 가능성)";
                return;
            }
            
            Debug.Log($"  LocalPlayer: {_trackedPlayer.gameObject.name} (instanceID={_trackedPlayer.GetInstanceID()})", _trackedPlayer);
            Debug.Log($"  IsOwner: {_trackedPlayer.IsOwner}");
            Debug.Log($"  IsSpawned: {_trackedPlayer.IsSpawned}");
            Debug.Log($"  isPerformingAction: {_trackedPlayer.isPerformingAction}");
            
            if (_trackedPlayer.playerNetworkManager == null)
            {
                Debug.LogError($"  ★ playerNetworkManager = null → NRE 위험", _trackedPlayer);
                _lastBlockReason = "★ playerNetworkManager null";
                return;
            }
            
            Debug.Log($"  isDead.Value: {_trackedPlayer.playerNetworkManager.isDead.Value}");
            Debug.Log($"  currentRightHandWeaponID: {_trackedPlayer.playerNetworkManager.currentRightHandWeaponID.Value}");
            Debug.Log($"  currentLeftHandWeaponID: {_trackedPlayer.playerNetworkManager.currentLeftHandWeaponID.Value}");
            Debug.Log($"  playerEquipmentManager: {(_trackedPlayer.playerEquipmentManager != null ? "OK" : "★ null")}");
            Debug.Log($"  WorldItemDatabase.Instance: {(WorldItemDatabase.Instance != null ? "OK" : "★ null")}");
            
            // 차단 조건 종합 분석
            var reasons = new List<string>();
            
            if (_trackedPlayer.playerNetworkManager.isDead.Value)
                reasons.Add("isDead.Value = true");
            
            if (_trackedPlayer.isPerformingAction)
                reasons.Add("isPerformingAction = true");
            
            if (_trackedPlayer.playerEquipmentManager == null)
                reasons.Add("playerEquipmentManager = null (NRE 위험)");
            
            if (WorldItemDatabase.Instance == null)
                reasons.Add("WorldItemDatabase.Instance = null");
            
            if (reasons.Count > 0)
            {
                _lastBlockReason = "★ 차단됨:\n  - " + string.Join("\n  - ", reasons);
                Debug.LogWarning($"  ★ OnSwitchWeaponInputReceived 차단/실패 사유:", _trackedPlayer);
                foreach (var r in reasons)
                    Debug.LogWarning($"    - {r}");
            }
            else
            {
                _lastBlockReason = "(차단 조건 없음 — 정상 진행 예상)";
                Debug.Log($"  ★ 차단 없음 — OnSwitchWeaponInputReceived 정상 진행 예상");
                Debug.Log($"  → 그래도 무기 전환 안 되면:");
                Debug.Log($"    - PlayerInputManager.player 가 본 Player 와 연결되어 있는지 확인");
                Debug.Log($"    - 또는 playerControls.PlayerAction 이 Enable 상태인지 확인");
                Debug.Log($"    - 또는 playerEquipmentManager.SwitchLeftWeapon/SwitchRightWeapon 내부 분석");
            }
            
            Debug.Log($"════════════════════════════════════════════════", this);
        }
        
        [ContextMenu("Force Refresh LocalPlayer")]
        public void ForceRefreshLocalPlayer()
        {
            _trackedPlayer = null;
            UpdateTrackedPlayer();
            Debug.Log(
                $"[InputDiag] 강제 재검색: " +
                $"{(_trackedPlayer != null ? $"{_trackedPlayer.gameObject.name} 발견" : "여전히 null")}",
                this);
        }
        
        [ContextMenu("Reset Statistics")]
        public void ResetStatistics()
        {
            _zKeyPressCount = 0;
            _cKeyPressCount = 0;
            _lastInputTime = "(아직 없음)";
            _lastBlockReason = "(분석 안 됨)";
            Debug.Log($"[InputDiag] 통계 초기화.", this);
        }
    }
}
