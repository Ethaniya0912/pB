// =============================================================================
// PlayerManagerDebugger.cs  |  TDA Project
// Layer  : Editor Tool — Player 전용 디버거 컴포넌트
//
// 추가 패널:
//   Panel L — 파지(Grip) & 공격 상태 (isUsingRightHand/LeftHand, 스태미나 소모)
//   Panel M — 방어 / 패링 상태 (isDefending, guardDirection, parryWindow)
//   Panel N — 카메라 연동 (isLockedOn, currentTarget, FOV 플리커 감지)
//   Panel F — Null 체크 Player 전용 항목 추가
//
// [NEW] 파노라마/사이드HUD 기능은 CharacterManagerDebugger 베이스에서 상속
//       Player 전용 파노라마 기본 탭: 공통(0), 파지/공격(5), 방어/패링(6), 타겟(3)
// =============================================================================
using UnityEngine;
using TDA.Character;
using TDA.Character.Player;
using TDA.EditorTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.EditorTools
{
    /// <summary>
    /// [Editor Tool] PlayerManager 전용 온스크린 디버거.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerManagerDebugger : CharacterManagerDebugger
    {
        // =====================================================================
        // Inspector 추가 설정
        // =====================================================================
        [Header("Player Pause Triggers")]
        [Tooltip("isUsingRightHand 값이 바뀌는 순간 에디터를 일시정지합니다.")]
        public bool pauseOnGripChange = false;

        // =====================================================================
        // 내부 참조
        // =====================================================================
        private PlayerManager _player;

        // =====================================================================
        // 파지 추적
        // =====================================================================
        private bool _prevRightHand = true;
        private float _prevStamina = -1f;
        private float _lastStaminaDrain = 0f;

        // =====================================================================
        // 패링 윈도 추적
        // =====================================================================
        private bool _parryWindowOpen = false;
        private float _parryWindowOpenTime = 0f;

        // =====================================================================
        // FOV 플리커 감지 (섹션8 C-03)
        // =====================================================================
        private bool _samplingFOV = false;
        private float _fovSampleStart = 0f;
        private float _fovAtHitBox = 0f;
        private float _fovMin = 0f;
        private float _fovMax = 0f;
        private bool _flickerDetected = false;
        private float _flickerThreshold = 3f;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();
            CacheComponents();
        }

        protected override void CacheComponents()
        {
            base.CacheComponents();
            _player = GetComponent<PlayerManager>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            var evtMgr = GetComponent<CharacterEventManager>();
            if (evtMgr != null)
                evtMgr.OnAnimationEventTriggered += OnPlayerAnimEvent;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            var evtMgr = GetComponent<CharacterEventManager>();
            if (evtMgr != null)
                evtMgr.OnAnimationEventTriggered -= OnPlayerAnimEvent;
        }

        private void OnPlayerAnimEvent(AnimationEventType t)
        {
            if (t == AnimationEventType.Parry_Window_Open)
            {
                _parryWindowOpen = true;
                _parryWindowOpenTime = Time.time;
            }
            else if (t == AnimationEventType.Parry_Window_Close)
            {
                _parryWindowOpen = false;
            }
            else if (t == AnimationEventType.HitBoxEnable)
            {
                StartFOVSampling();
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!showDebugHUD || _player == null) return;
            TrackGripChange();
            TrackStaminaDrain();
            SampleFOV();
        }

        // =====================================================================
        // 파지 변경 감지
        // =====================================================================
        private void TrackGripChange()
        {
            if (_player.playerNetworkManager == null) return;
            if (!_player.IsSpawned) return;
            bool currRight = _player.playerNetworkManager.isUsingRightHand.Value;
            if (currRight != _prevRightHand)
            {
#if UNITY_EDITOR
                if (pauseOnGripChange) EditorApplication.isPaused = true;
#endif
                _prevRightHand = currRight;
            }
        }

        // =====================================================================
        // 스태미나 소모량 추적
        // =====================================================================
        private void TrackStaminaDrain()
        {
            if (_player.playerNetworkManager == null) return;
            if (!_player.IsSpawned) return;
            float curr = _player.playerNetworkManager.currentStamina.Value;
            if (_prevStamina > 0f && curr < _prevStamina)
                _lastStaminaDrain = _prevStamina - curr;
            _prevStamina = curr;
        }

        // =====================================================================
        // FOV 플리커 샘플링 (HitBoxEnable 후 0.6초간)
        // =====================================================================
        private void StartFOVSampling()
        {
            _samplingFOV = true;
            _fovSampleStart = Time.time;
            _fovAtHitBox = GetCurrentFOV();
            _fovMin = _fovAtHitBox;
            _fovMax = _fovAtHitBox;
            _flickerDetected = false;
        }

        private void SampleFOV()
        {
            if (!_samplingFOV) return;
            if (Time.time - _fovSampleStart > 0.6f)
            {
                _samplingFOV = false;
                if (_fovMax - _fovMin > _flickerThreshold)
                    _flickerDetected = true;
                return;
            }
            float fov = GetCurrentFOV();
            if (fov > 0f)
            {
                _fovMin = Mathf.Min(_fovMin, fov);
                _fovMax = Mathf.Max(_fovMax, fov);
            }
        }

        private float GetCurrentFOV()
        {
            if (_player.playerCamera == null) return 0f;
            Camera cam = _player.playerCamera.GetComponentInChildren<Camera>();
            return cam != null ? cam.fieldOfView : 0f;
        }

        // =====================================================================
        // Null 체크 — Player 전용 항목 추가
        // =====================================================================
        protected override void RefreshNullCheck()
        {
            base.RefreshNullCheck();
            if (_player == null) return;
            Add("playerNetworkManager", _player.playerNetworkManager == null, true, "isUsingRightHand/LeftHand 접근 불가");
            Add("playerLocomotionManager", _player.playerLocomotionManager == null, true, "이동·구르기·점프 불가");
            Add("playerCombatManager", _player.playerCombatManager == null, true, "모든 공격 실행 불가");
            Add("playerEquipmentManager", _player.playerEquipmentManager == null, false, "무기 모델 로드 없음");
            Add("playerCamera", _player.playerCamera == null, false, "카메라 시퀀스 SO 호출 없음");
            Add("playerDefenseManager", _player.playerDefenseManager == null, false, "방어/패링 발동 없음");
            Add("playerAnimationManager", _player.playerAnimationManager == null, true, "애니메이션 호출 불가");
            Add("playerStatsManager", _player.playerStatsManager == null, false, "HP/스태미나 계산 없음");
        }

        // =====================================================================
        // 탭 구성 / 라우팅
        // =====================================================================
#if UNITY_EDITOR
        protected override string[] TabLabels =>
            new[] { "공통", "이벤트", "애니", "타겟", "참조", "파지/공격", "방어/패링", "카메라" };

        protected override void DrawTab(int tab)
        {
            switch (tab)
            {
                case 0: DrawCommonPanel(); break;
                case 1: DrawEventHistoryPanel_Proxy(); break;
                case 2: DrawAnimHistoryPanel_Proxy(); break;
                case 3: DrawTargetPanel(); break;
                case 4: DrawNullCheckPanel(); break;
                case 5: DrawGripPanel(); break;
                case 6: DrawDefensePanel(); break;
                case 7: DrawCameraPanel(); break;
            }
        }

        // ── 사이드 HUD 탭 — Player 전용 확장 ──────────────────────────────────
        // Player는 사이드 HUD에 카메라 패널도 추가로 제공
        protected override string[] GetSideTabLabels() =>
            new[] { "공통", "스탯", "전투", "타겟의타겟" };

        // ── Panel L — 파지(Grip) & 공격 ───────────────────────────────────────
        private void DrawGripPanel()
        {
            SectionLabel("🗡 파지(Grip) & 공격 (Panel L)");
            if (_player.playerNetworkManager == null || !_player.IsSpawned)
            { SmallLabel("  (네트워크 미스폰)"); return; }

            var pnm = _player.playerNetworkManager;
            bool rightHand = pnm.isUsingRightHand.Value;
            bool leftHand = pnm.isUsingLeftHand.Value;

            if (rightHand)
                InfoLabel("  🗡 RIGHT GRIP (베기 계열)", new Color(0.4f, 0.7f, 1f));
            else if (leftHand)
                InfoLabel("  ◉ LEFT GRIP (찌르기 계열)", new Color(0.7f, 0.4f, 1f));
            else
                SmallLabel("  (파지 없음)");

            if (rightHand == leftHand)
                WarningLabel($"  ⚠ isRightHand={rightHand} == isLeftHand={leftHand} (동기화 이상!)");

            GUILayout.Space(4);
            BoolRow("isUsingRightHand", rightHand, false);
            BoolRow("isUsingLeftHand", leftHand, false);

            GUILayout.Space(4);
            SectionLabel("  무기 & 스태미나");
            SmallLabel($"  currentWeapon ID : {pnm.currentWeaponBeingUsed.Value}");
            SmallLabel($"  RightHand ID     : {pnm.currentRightHandWeaponID.Value}");
            SmallLabel($"  LeftHand  ID     : {pnm.currentLeftHandWeaponID.Value}");

            GUILayout.Space(4);
            StatBar("스태미나", pnm.currentStamina.Value, pnm.maxStamina.Value, new Color(0.9f, 0.75f, 0.1f));
            SmallLabel($"  마지막 공격 스태미나 소모: {_lastStaminaDrain:F1}");

            if (_player.playerCombatManager != null)
            {
                GUILayout.Space(4);
                BoolRow("canComboMainHand", _player.playerCombatManager.canComboWithMainHandWeapon, false);
                BoolRow("canComboOffHand", _player.playerCombatManager.canComboWithOffHandWeapon, false);
            }
        }

        // ── Panel M — 방어 / 패링 ────────────────────────────────────────────
        private void DrawDefensePanel()
        {
            SectionLabel("🛡 방어 / 패링 (Panel M)");
            var defMgr = _player.characterDefenseManager;
            if (defMgr == null)
            { WarningLabel("  CharacterDefenseManager 없음!"); return; }

            BoolRow("isDefending", defMgr.isDefending, false);
            SmallLabel($"  guardDirection : {defMgr.currentGuardDirection}");
            SmallLabel($"  shieldStance   : {defMgr.currentShieldStance}");

            GUILayout.Space(4);
            if (_parryWindowOpen)
            {
                float elapsed = Time.time - _parryWindowOpenTime;
                InfoLabel($"  ✦ PARRY WINDOW OPEN  ({elapsed:F2}s)", new Color(1f, 0.9f, 0.2f));
            }
            else
            {
                SmallLabel("  패링 윈도우: 닫힘");
            }

            GUILayout.Space(4);
            BoolRow("isHoldingCloseGrip", _player.isHoldingCloseGrip, false);
            BoolRow("isHoldingFarGrip", _player.isHoldingFarGrip, false);
        }

        // ── Panel N — 카메라 연동 ────────────────────────────────────────────
        private void DrawCameraPanel()
        {
            SectionLabel("📷 카메라 연동 (Panel N)");
            if (_player.playerNetworkManager != null && _player.IsSpawned)
                BoolRow("isLockedOn", _player.playerNetworkManager.isLockedOn.Value, false);

            var cm = _player.characterCombatManager;
            string targetName = cm?.currentTarget != null ? cm.currentTarget.name : "없음";
            SmallLabel($"  currentTarget : {targetName}");

            GUILayout.Space(4);
            if (_player.playerCamera != null)
            {
                float fov = GetCurrentFOV();
                SmallLabel($"  playerCamera : 참조 있음");
                SmallLabel($"  현재 Camera.fov : {fov:F1}°");

                if (_samplingFOV)
                {
                    float elapsed = Time.time - _fovSampleStart;
                    InfoLabel($"  📊 FOV 샘플링 중... ({elapsed:F2}s)", new Color(1f, 0.9f, 0.2f));
                    SmallLabel($"    min={_fovMin:F1}  max={_fovMax:F1}  range={_fovMax - _fovMin:F2}°");
                }
                else if (_flickerDetected)
                {
                    WarningLabel($"  ⚠ FOV 플리커 감지! (범위: {_fovMax - _fovMin:F1}°) — 섹션8 C-03 실패");
                }
                else if (_fovAtHitBox > 0f)
                {
                    InfoLabel($"  ✓ FOV 안정 (범위: {_fovMax - _fovMin:F1}°)", new Color(0.4f, 1f, 0.4f));
                }
            }
            else
            {
                WarningLabel("  playerCamera = null");
            }
        }
#endif
    }
}