using UnityEngine;
using TDA.Character;
using TDA.Core.Events;
using TDA.Character.Player;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// [Phase 4 — WeaponBlurStateController v2]
/// TDA 커스텀 애니메이터 시스템 완전 통합 + 자동 의존성 탐색 + 디버그 시각화
///
/// ■ 자동 탐색 구조
///   Awake에서 CharacterManager → characterEventManager 경로로 자동 탐색
///   PlayerLocomotionManager 또는 CharacterController를 통해 속도 자동 감지
///   Rigidbody/CharacterController Inspector 슬롯은 선택사항 (자동 탐색이 실패할 경우 폴백)
///
/// ■ Windup 단계 감지 방법
///   이벤트 기반 (CharacterEventManager 구독):
///     Charge_Started(8)    → Charge_Windup 진입 감지
///     Lock_Rotation(14)    → Slash 예비동작 시작 감지
///   폴링 기반 (보조):
///     CharacterManager.canRotate == false + isPerformingAction == true
///     = 액션 진행 중 회전 불가 → 공격 계열 State 진행 중
///     Animator.GetCurrentAnimatorStateInfo().IsName() 으로 State 이름 직접 확인
///
/// ■ Inspector 디버그 패널
///   _dbgCurrentState  : 현재 BlurState 이름
///   _dbgShutterMult   : 현재 State의 ShutterMult 배율
///   _dbgAnimState     : 현재 Animator State 이름 (레이어 0)
///   _dbgIsWindup      : Charge Windup 진입 여부
///   _dbgSubscribed    : CharacterEventManager 구독 상태
/// </summary>
public class P4_WeaponBlurStateController : MonoBehaviour, IAnimationEventListener
{
    // ─────────────────────────────────────────────────────────────
    // Inspector — Dependencies
    // ─────────────────────────────────────────────────────────────
    [Header("Dependencies (자동 탐색 — 비워도 됩니다)")]
    [Tooltip("비워두면 같은 오브젝트에서 자동 탐색합니다.")]
    public ObjectMotionBlurController blurController;

    [Tooltip("비워두면 CharacterManager.characterEventManager를 통해 자동 탐색합니다.")]
    public CharacterEventManager characterEventManager;

    [Tooltip("비워두면 CharacterManager.characterController를 통해 자동 탐색합니다.\n" +
             "TDA는 Rigidbody를 직접 사용하지 않으므로 CharacterController가 기본입니다.")]
    public CharacterController characterControllerOverride;

    [Header("Attack Blur Boost")]
    [Tooltip("공격 중 추가 블러 배율. P2 ShutterMult에 곱해짐.\n" +
             "1.0=기본, 5.0=5배, 10.0=10배 강도.")]
    [Range(1f, 100f)] public float attackBlurMultiplier = 3.0f;  // 100배까지 가능

    [Tooltip("강공격 추가 배율 (attackBlurMultiplier에 추가로 곱해짐).")]
    [Range(1f, 3f)]  public float heavyBlurMultiplier   = 1.5f;
    // TIP: attackBlurMultiplier 10배 이상 사용 시
    //      P2의 maxBlurLength도 함께 올릴 것 (기본 0.25 → 0.5~1.0)
    //      stretchLen이 maxBlurLength로 클램프되므로 배율 효과가 제한됨

    [Tooltip("공격 진입 시 배율이 증가하는 보간 속도.\n" +
             "높을수록 즉각 반응, 낮을수록 서서히 증가.")]
    [Range(1f, 30f)] public float blurAttackRampUp   = 8f;

    [Tooltip("공격 종료 시 배율이 감소하는 보간 속도.\n" +
             "낮을수록 공격 후에도 블러가 오래 남음.")]
    [Range(0.5f, 15f)] public float blurAttackRampDown = 3f;

    [Header("Aim (Windup) Settings")]
    [Tooltip("true: Lock_Rotation(14) 수신 시 Aim 상태로 진입.\n" +
             "Slash 예비동작(t=0.0) 구간을 짧게 Aim으로 표현합니다.")]
    public bool enableSlashWindupAsAim = true;

    [Tooltip("Aim 최대 지속 시간(초). HitBoxEnable이 오지 않으면 자동 Idle 복귀.")]
    [Range(0f, 0.5f)] public float aimMaxDuration = 0.2f;

    [Header("Attack End Delay")]
    [Tooltip("HitBoxDisable 후 Idle 복귀까지의 딜레이(초). 공격 잔상 유지.")]
    [Range(0f, 0.5f)] public float attackEndDelay = 0.08f;

    [Header("Move Detection")]
    [Tooltip("이동으로 판단하는 속도 임계값 (m/s).")]
    [Range(0.1f, 5f)] public float strafeSpeedThreshold = 0.5f;

    // ─────────────────────────────────────────────────────────────
    // Inspector — Debug (ReadOnly)
    // ─────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────
    // 바디파트 분석 설정
    // ─────────────────────────────────────────────────────────────
    [Header("Body Part Analysis")]
    [Tooltip("true: 매 프레임 바디파트별 stretch 통계를 갱신합니다. (약간의 CPU 비용)")]
    // 기본값 false — Inspector에서 직접 켜야 활성화
    // 에디터 Play Mode에서도 활성화 시 GC Alloc 발생 (sharedMesh.boneWeights)
    public bool enableBodyPartAnalysis = false;

    [Tooltip("통계 갱신 간격(초). 0 = 매 프레임. 성능 우선 시 0.1~0.2 권장.")]
    [Range(0.5f, 10f)] public float analysisInterval = 3.0f; // 3초에 1회

    [Header("─── Debug (ReadOnly) ─────────────────")]
    [SerializeField] private string _dbgCurrentState        = "Idle";
    [SerializeField] private float  _dbgShutterMult         = 0.5f;
    [SerializeField] private string _dbgAnimState           = "—";
    [SerializeField] private float  _dbgPlayerSpeed         = 0f;
    [SerializeField] private bool   _dbgIsWindup            = false;
    [SerializeField] private bool   _dbgSubscribed          = false;
    [SerializeField] private bool   _dbgBlurCtrlFound       = false;
    [SerializeField] private bool   _dbgEventMgrFound       = false;
    [SerializeField] private bool   _dbgCCFound             = false;
    [Tooltip("마지막으로 수신된 AnimationEventType 이름")]
    [SerializeField] private string _dbgLastEventReceived   = "—";
    [Tooltip("마지막 이벤트 수신 이후 경과 시간 (초)")]
    [SerializeField] private float  _dbgLastEventTime       = -1f;
    [Tooltip("총 수신된 이벤트 누적 횟수")]
    [SerializeField] private int    _dbgTotalEventCount     = 0;
    [Tooltip("마지막 BlurState 전환을 유발한 이벤트 이름")]
    [SerializeField] private string _dbgLastTriggerEvent    = "—";
    [Tooltip("최근 수신된 이벤트 히스토리 (최대 6개, 위가 최신)")]
    [SerializeField] private string _dbgEventHistory_0 = "—";
    [SerializeField] private string _dbgEventHistory_1 = "—";
    [SerializeField] private string _dbgEventHistory_2 = "—";
    [SerializeField] private string _dbgEventHistory_3 = "—";
    [SerializeField] private string _dbgEventHistory_4 = "—";
    [SerializeField] private string _dbgEventHistory_5 = "—";

    // ── 현재 파라미터 기준 예상 stretch ─────────────────────────
    [Header("─── Predicted Stretch (Current Params) ──────────")]
    [Tooltip("현재 ShutterAngle / ShutterMult / maxBlurLength 기준 예상 최대 늘어남(cm)\n" +
             "실제 GVB speed 없이 ShutterMult 기준으로만 계산 (최대치 추산)")]
    [SerializeField] private string _dbgPredictedMax      = "—";
    [SerializeField] private string _dbgPredictedAttack   = "—";
    [SerializeField] private string _dbgPredictedHeavy    = "—";

    // ── 바디파트별 stretch 통계 ─────────────────────────────────
    [Header("─── Body Part Stretch Stats ───────────────────────")]
    [Tooltip("각 부위: 가중치 평균 | 예상 stretch(cm) | 색상의미")]
    [SerializeField] private string _dbgPart_HandTip    = "—";   // 손끝/손가락
    [SerializeField] private string _dbgPart_Hand       = "—";   // 손목/손
    [SerializeField] private string _dbgPart_Forearm    = "—";   // 팔뚝
    [SerializeField] private string _dbgPart_UpperArm   = "—";   // 위팔
    [SerializeField] private string _dbgPart_Shoulder   = "—";   // 어깨
    [SerializeField] private string _dbgPart_Foot       = "—";   // 발/발끝
    [SerializeField] private string _dbgPart_Shin       = "—";   // 정강이
    [SerializeField] private string _dbgPart_Thigh      = "—";   // 허벅지
    [SerializeField] private string _dbgPart_Head       = "—";   // 머리/목
    [SerializeField] private string _dbgPart_Spine      = "—";   // 척추/가슴
    [SerializeField] private string _dbgPart_Hips       = "—";   // 엉덩이/루트
    [SerializeField] private string _dbgPart_Overall    = "—";   // 전체 평균
    [Tooltip("maxBlurLength 대비 현재 최대 활용 비율 (빨강=클램프 초과, 흰색=100%)")]
    [SerializeField] private string _dbgMaxLengthUsage  = "—";

    // ShutterMult 배율표 (ObjectMotionBlurController.StateShutterMultiplier와 동기화)
    private static readonly float[] _shutterMultTable =
    {
        0.5f,   // Idle
        1.0f,   // Strafe
        0.67f,  // Aim
        1.5f,   // Attack
        2.0f,   // HeavyAttack
    };

    // ─────────────────────────────────────────────────────────────
    // 내부 참조
    // ─────────────────────────────────────────────────────────────
    private CharacterManager    _characterManager;
    private CharacterController _cc;
    private Animator            _animator;

    // ─────────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────────
    private float _idleScheduledTime  = -1f;
    private float _smoothedAttackMult = 1.0f;  // attackBlurMultiplier 보간값
    private float _aimStartTime      = -1f;
    private bool  _isInAim           = false;

    // ── 바디파트 분석용 캐시 ──────────────────────────────────────
    private AvatarAutoWeightBaker _weightBaker;
    private float[]               _cachedWeights;
    private SkinnedMeshRenderer   _cachedSMR;
    private float                 _nextAnalysisTime = 0f;
#if UNITY_EDITOR
    // 에디터 전용 캐시 — 런타임에는 할당 없음
    private BoneWeight[]          _cachedBoneWeights;   // sharedMesh.boneWeights 1회 캐시
    private Dictionary<string, float> _partSumCache;
    private Dictionary<string, int>   _partCountCache;
#endif

    // 바디파트 본 키워드 → 파트명 매핑
    private static readonly (string[] keywords, string partName)[] _bonePartMap =
    {
        (new[]{"finger","thumb","tip"},                   "HandTip"),
        (new[]{"hand"},                                   "Hand"),
        (new[]{"forearm","lowerarm","lower_arm"},          "Forearm"),
        (new[]{"upperarm","upper_arm","arm"},              "UpperArm"),
        (new[]{"shoulder","clavicle"},                    "Shoulder"),
        (new[]{"toe","foot","ankle"},                     "Foot"),
        (new[]{"shin","calf","lowerleg","lower_leg"},     "Shin"),
        (new[]{"thigh","upperleg","upper_leg"},           "Thigh"),
        (new[]{"head","neck"},                            "Head"),
        (new[]{"spine","chest"},                          "Spine"),
        (new[]{"hips","pelvis","root"},                   "Hips"),
    };
    private static readonly string[] _knownStateNames =
    {
        "Slash_Attack_Left", "Slash_Attack_Right",
        "Heavy_Attack_Left", "Heavy_Attack_Right",
        "Charge_Windup_Left", "Charge_Windup_Right", "Charge_Windup_Left ",
        "Roll_Forward", "Stagger_Medium", "Dead_01",
        "Idle_Standard", "FreeMove_BlendTree", "Strafe_BlendTree",
    };


    // ─────────────────────────────────────────────────────────────
    // Charge_Windup State 이름 목록 (Animator State 이름 폴링용)
    // Animator 컨트롤러의 State 이름과 일치해야 합니다.
    private static readonly string[] _chargeWindupStateNames =
    {
        "Charge_Windup_Left",
        "Charge_Windup_Right",
        "Charge_Windup_Left ",   // Humanoid.controller에 공백 있는 버전 포함
    };

    // ─────────────────────────────────────────────────────────────
    // 생명주기
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // Awake에서는 직접 GetComponent로만 탐색 (CharacterManager 캐시 미사용)
        // CharacterManager.Awake()가 아직 실행 전일 수 있어
        // characterEventManager 캐시가 null일 가능성이 있음
        if (blurController == null)
            blurController = GetComponent<ObjectMotionBlurController>();
        _dbgBlurCtrlFound = blurController != null;
    }

    private void Start()
    {
        // Start: 모든 Awake 완료 후 실행 → CharacterManager 캐시 확정됨
        // AutoResolve와 Subscribe를 Start에서 함께 실행
        AutoResolve();
        Subscribe();
    }

    private void OnEnable()
    {
        if (!_dbgSubscribed) Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    // ─────────────────────────────────────────────────────────────
    // 자동 의존성 탐색
    // ─────────────────────────────────────────────────────────────
    private void AutoResolve()
    {
        // ObjectMotionBlurController는 Awake에서 이미 탐색 완료

        // 2. CharacterManager (이 오브젝트 또는 부모에서 탐색)
        _characterManager = GetComponent<CharacterManager>();
        if (_characterManager == null)
            _characterManager = GetComponentInParent<CharacterManager>();

        // 3. CharacterEventManager
        //    TDA 아키텍처: CharacterManager.characterEventManager에 이미 캐싱되어 있음
        if (characterEventManager == null && _characterManager != null)
            characterEventManager = _characterManager.characterEventManager;
        if (characterEventManager == null)
            characterEventManager = GetComponent<CharacterEventManager>();
        if (characterEventManager == null)
            characterEventManager = GetComponentInChildren<CharacterEventManager>();
        _dbgEventMgrFound = characterEventManager != null;

        // 4. CharacterController
        //    TDA는 Rigidbody를 직접 사용하지 않음 → CharacterController가 정확한 속도 소스
        //    CharacterManager.characterController에 이미 캐싱되어 있음
        if (characterControllerOverride != null)
        {
            _cc = characterControllerOverride;
        }
        else if (_characterManager != null)
        {
            _cc = _characterManager.characterController;
        }
        if (_cc == null)
            _cc = GetComponent<CharacterController>();
        _dbgCCFound = _cc != null;

        // 5. Animator
        if (_characterManager != null)
            _animator = _characterManager.animator;
        if (_animator == null)
            _animator = GetComponent<Animator>();

        // 6. AvatarAutoWeightBaker & SMR (바디파트 분석용)
        _weightBaker = GetComponentInChildren<AvatarAutoWeightBaker>();
        if (_weightBaker == null)
            _weightBaker = GetComponent<AvatarAutoWeightBaker>();
        if (_weightBaker != null)
        {
            _cachedWeights = _weightBaker.GetCachedWeights();
            _cachedSMR     = _weightBaker.GetComponent<SkinnedMeshRenderer>();
            if (_cachedSMR == null)
                _cachedSMR = GetComponentInChildren<SkinnedMeshRenderer>();
        }

        // 6. 탐색 결과 경고
        if (!_dbgBlurCtrlFound)
            Debug.LogError("<color=red>[P4_BlurState]</color> ObjectMotionBlurController 자동 탐색 실패! Inspector에 직접 연결하세요.", this);
        if (!_dbgEventMgrFound)
            Debug.LogError("<color=red>[P4_BlurState]</color> CharacterEventManager 자동 탐색 실패! Inspector에 직접 연결하세요.", this);
        if (!_dbgCCFound)
            Debug.LogWarning("<color=yellow>[P4_BlurState]</color> CharacterController 자동 탐색 실패. 속도 감지 비활성화.", this);
        
        // 탐색 성공 요약 로그
        Debug.Log($"<color=cyan>[P4_BlurState]</color> AutoResolve 완료 | BlurCtrl:{_dbgBlurCtrlFound} | EventMgr:{_dbgEventMgrFound} | CC:{_dbgCCFound}", this);
    }

    private void Subscribe()
    {
        if (characterEventManager == null)
        {
            Debug.LogWarning("[P4] CharacterEventManager 없음 — 이벤트 구독 불가.", this);
            return;
        }
        characterEventManager.OnAnimationEventTriggered += OnAnimationEventReceived;
        _dbgSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (characterEventManager != null)
            characterEventManager.OnAnimationEventTriggered -= OnAnimationEventReceived;
        _dbgSubscribed = false;
    }

    // ─────────────────────────────────────────────────────────────
    // Update — 이동 감지 + 타이머 + Animator State 폴링 + 디버그 갱신
    // ─────────────────────────────────────────────────────────────
    private void Update()
    {
        // ── 속도 감지 ─────────────────────────────────────────────
        _dbgPlayerSpeed = GetSpeed();

        // ── Animator State 이름 갱신 (디버그) ───────────────────
        if (_animator != null)
        {
            var info = _animator.GetCurrentAnimatorStateInfo(0);
#if UNITY_EDITOR
            _dbgAnimState = GetStateName(info);
#endif
            // Charge_Windup 진입 감지 (폴링 보조 — Charge_Started 이벤트 없는 경우 대비)
            bool inWindup = IsChargeWindupState(info);
            if (inWindup && !_isInAim)
                EnterAim("Polling:Windup");
#if UNITY_EDITOR
            _dbgIsWindup = inWindup;
#endif
        }

        // ── 이동 기반 BlurState 전환 ─────────────────────────────
        if (blurController != null)
        {
            var cur = blurController.currentState;
            bool isActionActive = cur == ObjectMotionBlurController.BlurState.Attack
                               || cur == ObjectMotionBlurController.BlurState.HeavyAttack
                               || cur == ObjectMotionBlurController.BlurState.Aim;

            if (!isActionActive)
            {
                var next = _dbgPlayerSpeed > strafeSpeedThreshold
                    ? ObjectMotionBlurController.BlurState.Strafe
                    : ObjectMotionBlurController.BlurState.Idle;
                if (next != cur)   // 변화 시에만 호출 — 매 프레임 string alloc 방지
                    SetBlurState(next);
            }
        }

        // ── Idle 딜레이 타이머 ────────────────────────────────────
        if (_idleScheduledTime > 0f && Time.time >= _idleScheduledTime)
        {
            _idleScheduledTime = -1f;
            _isInAim           = false;
            SetBlurState(ObjectMotionBlurController.BlurState.Idle);
        }

        // ── Aim 타임아웃 ──────────────────────────────────────────
        if (_isInAim && _aimStartTime > 0f &&
            Time.time - _aimStartTime > aimMaxDuration)
        {
            _isInAim      = false;
            _aimStartTime = -1f;
            SetBlurState(ObjectMotionBlurController.BlurState.Idle);
        }

        // ── Attack Blur Multiplier 보간 + P2 주입 ─────────────────
        if (blurController != null)
        {
            var state = blurController.currentState;
            float targetMult;
            switch (state)
            {
                case ObjectMotionBlurController.BlurState.HeavyAttack:
                    targetMult = attackBlurMultiplier * heavyBlurMultiplier;
                    break;
                case ObjectMotionBlurController.BlurState.Attack:
                    targetMult = attackBlurMultiplier;
                    break;
                default:
                    targetMult = 1.0f;
                    break;
            }

            // 진입은 빠르게, 종료는 느리게 (rampUp/rampDown)
            float ramp = (targetMult > _smoothedAttackMult)
                       ? blurAttackRampUp
                       : blurAttackRampDown;
            _smoothedAttackMult = Mathf.Lerp(_smoothedAttackMult, targetMult,
                                             Time.deltaTime * ramp);

            // P2에 주입: blurIntensity를 _smoothedAttackMult로 오버라이드
            // blurController.blurIntensity는 기본값 유지
            // → external multiplier로 별도 적용
            blurController.SetExternalIntensityMult(_smoothedAttackMult);
        }

#if UNITY_EDITOR
        // 바디파트 분석: 에디터 전용 — 런타임 빌드에서 완전 제거
        if (enableBodyPartAnalysis && Time.time >= _nextAnalysisTime)
        {
            _nextAnalysisTime = Time.time + Mathf.Max(analysisInterval, 0f);
            UpdateBodyPartStats();
            UpdatePredictedStretch();
        }
#endif
    }

    // ─────────────────────────────────────────────────────────────
    // IAnimationEventListener — 이벤트 수신
    // ─────────────────────────────────────────────────────────────
    public void OnAnimationEventReceived(global::AnimationEventType eventType)
    {
#if UNITY_EDITOR
        // 에디터 전용 이벤트 기록 (string alloc → 런타임 GC 방지)
        string evName = eventType.ToString();
        _dbgLastEventReceived = evName;
        _dbgLastEventTime     = Time.time;
        _dbgTotalEventCount++;
        _dbgEventHistory_5 = _dbgEventHistory_4;
        _dbgEventHistory_4 = _dbgEventHistory_3;
        _dbgEventHistory_3 = _dbgEventHistory_2;
        _dbgEventHistory_2 = _dbgEventHistory_1;
        _dbgEventHistory_1 = _dbgEventHistory_0;
        _dbgEventHistory_0 = $"[{_dbgTotalEventCount}] {evName}";
#endif

        switch (eventType)
        {
            // ════════════════════════════════════════════
            // Aim 단계 (예비동작)
            // ════════════════════════════════════════════
            case global::AnimationEventType.Charge_Started:
                EnterAim("Charge_Started");
                break;

            case global::AnimationEventType.Lock_Rotation:
                if (enableSlashWindupAsAim)
                    EnterAim("Lock_Rotation");
                break;

            // ════════════════════════════════════════════
            // Attack 단계 (히트박스 활성)
            // ════════════════════════════════════════════
            case global::AnimationEventType.HitBoxEnable:
                _isInAim           = false;
                _aimStartTime      = -1f;
                _idleScheduledTime = -1f;
                SetBlurState(ObjectMotionBlurController.BlurState.Attack, "HitBoxEnable");
                break;

            case global::AnimationEventType.PlaySFX_Swing_Light:
                if (blurController != null &&
                    blurController.currentState != ObjectMotionBlurController.BlurState.Attack &&
                    blurController.currentState != ObjectMotionBlurController.BlurState.HeavyAttack)
                {
                    _idleScheduledTime = -1f;
                    SetBlurState(ObjectMotionBlurController.BlurState.Attack);
                }
                break;

            // ════════════════════════════════════════════
            // HeavyAttack 단계 (강공격 피크)
            // ════════════════════════════════════════════
            case global::AnimationEventType.PlaySFX_Swing_Heavy:
                _idleScheduledTime = -1f;
                SetBlurState(ObjectMotionBlurController.BlurState.HeavyAttack, "PlaySFX_Swing_Heavy");
                break;

            // ════════════════════════════════════════════
            // Idle 복귀
            // ════════════════════════════════════════════
            case global::AnimationEventType.HitBoxDisable:
                _dbgLastTriggerEvent = $"HitBoxDisable → Idle in {attackEndDelay}s";
                ScheduleIdle(attackEndDelay);
                break;

            case global::AnimationEventType.Unlock_Rotation:
                if (_isInAim)
                {
                    _isInAim      = false;
                    _aimStartTime = -1f;
                    SetBlurState(ObjectMotionBlurController.BlurState.Idle);
                }
                break;

            case global::AnimationEventType.Charge_Ended:
                _isInAim           = false;
                _aimStartTime      = -1f;
                _idleScheduledTime = -1f;
                SetBlurState(ObjectMotionBlurController.BlurState.Idle);
                break;

            case global::AnimationEventType.Action_Ended:
                _isInAim           = false;
                _aimStartTime      = -1f;
                _idleScheduledTime = -1f;
                SetBlurState(ObjectMotionBlurController.BlurState.Idle, "Action_Ended");
                break;

            // ════════════════════════════════════════════
            // 구르기 (Strafe)
            // ════════════════════════════════════════════
            case global::AnimationEventType.IFrameEnable:
                _idleScheduledTime = -1f;
                SetBlurState(ObjectMotionBlurController.BlurState.Strafe, "IFrameEnable");
                break;

            case global::AnimationEventType.IFrameDisable:
            case global::AnimationEventType.CameraShake_Roll:
                ScheduleIdle(0f);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────
    private void EnterAim(string triggerEvent = "Polling")
    {
        _isInAim           = true;
        _aimStartTime      = Time.time;
        _idleScheduledTime = -1f;
        SetBlurState(ObjectMotionBlurController.BlurState.Aim, triggerEvent);
    }

    private void ScheduleIdle(float delay)
    {
        if (delay <= 0f)
        {
            _idleScheduledTime = -1f;
            SetBlurState(ObjectMotionBlurController.BlurState.Idle);
        }
        else
        {
            _idleScheduledTime = Time.time + delay;
        }
    }

    private void SetBlurState(ObjectMotionBlurController.BlurState state,
                               string triggerEvent = "")
    {
        if (blurController == null) return;
        blurController.SetBlurState(state);
#if UNITY_EDITOR
        _dbgCurrentState     = state.ToString();
        _dbgShutterMult      = _shutterMultTable[(int)state];
        if (!string.IsNullOrEmpty(triggerEvent))
            _dbgLastTriggerEvent = triggerEvent;
#endif
    }


    // ─────────────────────────────────────────────────────────────
    // 바디파트 stretch 통계 분석
    // ─────────────────────────────────────────────────────────────

    private void UpdatePredictedStretch()
    {
        if (blurController == null) return;

        float shutterAngle = Shader.GetGlobalFloat("_ShutterAngle");
        float fps          = Shader.GetGlobalFloat("_TargetFPS");
        if (shutterAngle < 1f) shutterAngle = 180f;
        if (fps < 1f)          fps          = 60f;

        float expTime   = (shutterAngle / 360f) / fps;
        float intensity = blurController.blurIntensity;
        float maxLen    = blurController.maxBlurLength;
        float estSpeed  = 0.12f; // 팔 스윙 추정 속도 m/frame

        float multAttack = 2.0f;
        float multHeavy  = 3.0f;

        float rawA = estSpeed * fps * expTime * multAttack * intensity;
        float rawH = estSpeed * fps * expTime * multHeavy  * intensity;
        float stretchA = Mathf.Min(rawA, maxLen);
        float stretchH = Mathf.Min(rawH, maxLen);
        float stretchMax = Mathf.Max(stretchA, stretchH);

        _dbgPredictedMax    = $"{stretchMax*100f:F1}cm  [{StretchColor(stretchMax)}]"
                            + $"  SA={shutterAngle:F0}deg FPS={fps:F0}";
        _dbgPredictedAttack = $"{stretchA*100f:F1}cm  [{StretchColor(stretchA)}]"
                            + "  mult=2.0x" + (rawA > maxLen ? "  WARNING:CLAMPED maxLen+" : "");
        _dbgPredictedHeavy  = $"{stretchH*100f:F1}cm  [{StretchColor(stretchH)}]"
                            + "  mult=3.0x" + (rawH > maxLen ? "  WARNING:CLAMPED maxLen+" : "");
    }

    private void UpdateBodyPartStats()
    {
        if (_cachedWeights == null || _cachedWeights.Length == 0 ||
            _cachedSMR == null || blurController == null)
        {
            _dbgPart_Overall = "WeightBaker 없음 — AvatarAutoWeightBaker 연결 필요";
            return;
        }

        float shutterAngle = Shader.GetGlobalFloat("_ShutterAngle");
        float fps          = Shader.GetGlobalFloat("_TargetFPS");
        if (shutterAngle < 1f) shutterAngle = 180f;
        if (fps < 1f)          fps          = 60f;

        float expTime   = (shutterAngle / 360f) / fps;
        float intensity = blurController.blurIntensity;
        float maxLen    = blurController.maxBlurLength;
        float mult      = GetCurrentShutterMult();
        float estSpeed  = 0.12f;

        Transform[] bones = _cachedSMR.bones;
        // boneWeights 1회 캐시 — 이후 호출에서 재할당 없음
        if (_cachedBoneWeights == null || _cachedBoneWeights.Length == 0)
        {
            if (_cachedSMR.sharedMesh != null)
                _cachedBoneWeights = _cachedSMR.sharedMesh.boneWeights;
        }
        BoneWeight[] bwArr = _cachedBoneWeights;
        if (bones == null || bwArr == null) return;

        // Dictionary 재사용 (매 프레임 new 방지)
        if (_partSumCache == null)
        {
            _partSumCache   = new Dictionary<string, float>();
            _partCountCache = new Dictionary<string, int>();
        }
        var partSum   = _partSumCache;
        var partCount = _partCountCache;
        partSum.Clear(); partCount.Clear();
        string[] allParts = {"HandTip","Hand","Forearm","UpperArm","Shoulder",
                             "Foot","Shin","Thigh","Head","Spine","Hips","Other"};
        foreach (var p in allParts) { partSum[p] = 0f; partCount[p] = 0; }

        for (int v = 0; v < _cachedWeights.Length; v++)
        {
            BoneWeight bw = bwArr[v];
            float w = _cachedWeights[v];
            string boneName = (bw.boneIndex0 >= 0 && bw.boneIndex0 < bones.Length && bones[bw.boneIndex0] != null)
                            ? bones[bw.boneIndex0].name.ToLower() : "";
            string part = ClassifyBone(boneName);
            partSum[part]   += w;
            partCount[part] += 1;
        }

        void SetPart(ref string field, string key)
        {
            int   cnt = partCount.ContainsKey(key) ? partCount[key] : 0;
            float avg = (cnt > 0) ? partSum[key] / cnt : 0f;
            float raw = estSpeed * fps * expTime * mult * intensity * avg;
            float stretch = Mathf.Min(raw, maxLen);
            bool  clamped = raw > maxLen;
            field = FormatPartLine(avg, stretch, maxLen, clamped, cnt);
        }

        SetPart(ref _dbgPart_HandTip,  "HandTip");
        SetPart(ref _dbgPart_Hand,     "Hand");
        SetPart(ref _dbgPart_Forearm,  "Forearm");
        SetPart(ref _dbgPart_UpperArm, "UpperArm");
        SetPart(ref _dbgPart_Shoulder, "Shoulder");
        SetPart(ref _dbgPart_Foot,     "Foot");
        SetPart(ref _dbgPart_Shin,     "Shin");
        SetPart(ref _dbgPart_Thigh,    "Thigh");
        SetPart(ref _dbgPart_Head,     "Head");
        SetPart(ref _dbgPart_Spine,    "Spine");
        SetPart(ref _dbgPart_Hips,     "Hips");

        float totalAvg = 0f;
        foreach (var kv in partSum) totalAvg += kv.Value;
        float overallAvg     = _cachedWeights.Length > 0 ? totalAvg / _cachedWeights.Length : 0f;
        float overallStretch = Mathf.Min(estSpeed * fps * expTime * mult * intensity * overallAvg, maxLen);
        _dbgPart_Overall = FormatPartLine(overallAvg, overallStretch, maxLen, false, _cachedWeights.Length);

        float maxUsage = 0f;
        foreach (var kv in partSum)
        {
            int c = partCount[kv.Key];
            if (c == 0) continue;
            float a = kv.Value / c;
            float r = (estSpeed * fps * expTime * mult * intensity * a) / Mathf.Max(maxLen, 0.001f);
            if (r > maxUsage) maxUsage = r;
        }
        string clampWarn = (maxUsage > 1.0f) ? " WARNING:CLAMPED maxLen+ recommended" : "";
        _dbgMaxLengthUsage = $"{Mathf.Min(maxUsage,1f)*100f:F0}%  {MaxLenColor(maxUsage)}{clampWarn}";
    }

    private string ClassifyBone(string n)
    {
        foreach (var (kws, part) in _bonePartMap)
            foreach (var kw in kws)
                if (n.Contains(kw)) return part;
        return "Other";
    }

    private float GetCurrentShutterMult()
    {
        if (blurController == null) return 1f;
        switch (blurController.currentState)
        {
            case ObjectMotionBlurController.BlurState.Idle:        return 0f;
            case ObjectMotionBlurController.BlurState.Strafe:      return 0.8f;
            case ObjectMotionBlurController.BlurState.Aim:         return 0.5f;
            case ObjectMotionBlurController.BlurState.Attack:      return 2.0f;
            case ObjectMotionBlurController.BlurState.HeavyAttack: return 3.0f;
            default: return 1f;
        }
    }

    private static string StretchColor(float m)
    {
        float cm = m * 100f;
        if      (cm < 0.5f)  return "검정(0cm)";
        else if (cm < 2f)    return "파랑(2cm)";
        else if (cm < 8f)    return "파랑→녹색(2~8cm)";
        else if (cm < 16f)   return "녹색→노랑(8~16cm)";
        else if (cm < 25f)   return "노랑→흰색(16~25cm)";
        else                 return "흰색MAX(25cm+)";
    }

    private static string MaxLenColor(float ratio)
    {
        if      (ratio > 1.0f)  return "[빨강:상한초과]";
        else if (ratio > 0.75f) return "[노랑→흰색:75~100%]";
        else if (ratio > 0.5f)  return "[녹색→노랑:50~75%]";
        else if (ratio > 0.25f) return "[파랑→녹색:25~50%]";
        else                    return "[파랑이하:0~25%]";
    }

    private static string FormatPartLine(float avgW, float stretchM, float maxLen, bool clamped, int verts)
    {
        float cm    = stretchM * 100f;
        float ratio = stretchM / Mathf.Max(maxLen, 0.001f);
        return $"w={avgW:F2} | {cm:F1}cm | {ratio*100f:F0}% of maxLen | [{StretchColor(stretchM)}]"
             + (clamped ? " CLAMPED" : "") + $"  v:{verts}";
    }

    private float GetSpeed()

    {
        // 우선순위 1: Inspector 직접 지정 CC
        if (characterControllerOverride != null)
            return characterControllerOverride.velocity.magnitude;
        // 우선순위 2: CharacterManager.characterController (TDA 표준)
        if (_cc != null)
            return _cc.velocity.magnitude;
        return 0f;
    }

    /// <summary>
    /// Animator State 이름을 읽어 Charge_Windup 계열인지 확인합니다.
    /// Charge_Windup SO에 Charge_Started 이벤트가 없는 경우를 대비한 폴링 안전망입니다.
    /// </summary>
    private bool IsChargeWindupState(AnimatorStateInfo info)
    {
        foreach (var name in _chargeWindupStateNames)
            if (info.IsName(name)) return true;
        return false;
    }

    /// <summary>
    /// AnimatorStateInfo에서 사람이 읽을 수 있는 State 이름을 반환합니다.
    /// (Unity는 런타임에서 이름 직접 접근 불가 → IsName으로 역탐색)
    /// </summary>
    private string GetStateName(AnimatorStateInfo info)
    {
        // 공격/액션 State 우선 확인
        // static readonly: 매 프레임 새 배열 생성 방지 (GC Alloc 0)
        var knownStates = _knownStateNames;
        foreach (var n in knownStates)
            if (info.IsName(n)) return n;

        // 알 수 없는 State → hash 표시
        return $"Hash:{info.shortNameHash}";
    }
}
