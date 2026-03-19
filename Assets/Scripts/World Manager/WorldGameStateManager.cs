using System;
using System.Collections;
using UnityEngine;
using TDA.World;
using TDA.Cameras;
using TDA.Character.Player; // 로컬 플레이어 무장 검사를 위해 추가
using TDA.Items; // 아이템 데이터베이스(맨손 ID) 확인용

public class WorldGameStateManager : MonoBehaviour
{
    public static WorldGameStateManager Instance { get; private set; }

    [Header("State Info")]
    public GameState currentState = GameState.Normal;

    // 다른 매니저나 시스템이 상태 변화를 구독할 수 있게 이벤트 제공
    public delegate void StateChanged(GameState newState);
    public event StateChanged OnStatechanged;

    // =========================================================================================
    // 🚨 [허브 B: 정적 상태 라우팅 데이터] 하드코딩 탈피 및 SO 에셋 연동
    // =========================================================================================
    [Header("Camera Sequence Presets (SO Data-Driven)")]
    [Tooltip("인벤토리를 열 때 재생할 카메라 연출 (예: 캐릭터 우측 클로즈업) SO를 연결하세요.")]
    public CameraSequencePresetSO inventoryCameraSequence;

    [Tooltip("테이블 상호작용 시 재생할 카메라 연출 SO를 연결하세요.")]
    public CameraSequencePresetSO tableCameraSequence;

    [Tooltip("요리 시 재생할 카메라 연출 SO를 연결하세요.")]
    public CameraSequencePresetSO cookingCameraSequence;

    [Tooltip("일반 크기 적 락온 진입 시 재생할 전투 카메라 연출 SO를 연결하세요.")]
    public CameraSequencePresetSO lockOnCameraSequence;

    [Tooltip("보스 또는 거대 몬스터 락온 진입 시 재생할 로우 앵글 전투 카메라 연출 SO를 연결하세요.")]
    public CameraSequencePresetSO lockOnGiantCameraSequence;

    // -------------------------------------------------------------------------
    // [신규 추가] 논타겟팅(Free-Aim) 전투 뷰 및 탐험 뷰 분기
    // -------------------------------------------------------------------------
    [Tooltip("무기를 꺼내들고 락온하지 않은 논타겟(자유 시점) 난전 시 재생할 카메라 연출 SO를 연결하세요.")]
    public CameraSequencePresetSO freeAimCombatCameraSequence;

    [Tooltip("무기를 모두 집어넣은 맨손(Unarmed) 상태일 때 돌아갈 기본 탐험용 숄더뷰 리셋 시퀀스입니다.")]
    public CameraSequencePresetSO normalCameraSequence;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // 무기 장착 여부 검사용 로컬 플레이어 캐싱
    private PlayerManager localPlayerCached;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else Destroy(gameObject);

        DontDestroyOnLoad(gameObject);
    }

    public void ChangeState(GameState newState)
    {
        // newState 가 currentState랑 같으면 리턴, 아닐 시 같게 해주고 구독된 함수 활성화.
        if (currentState == newState) return;
        currentState = newState;
        OnStatechanged?.Invoke(currentState);
    }

    // =========================================================================================
    // [리팩토링] TriggerEventFocus (동적 타겟 포커싱)
    // 컷신이나 특수 이벤트 시 즉석에서 타겟과 함께 SO를 던져주어 카메라를 통제합니다.
    // =========================================================================================
    public void TriggerEventFocus(Transform target, CameraSequencePresetSO sequenceSO)
    {
        if (sequenceSO != null)
        {
            StartCoroutine(EventFocusRoutine(target, sequenceSO));
        }
        else
        {
            Debug.LogWarning("[WorldGameStateManager] 포커싱할 시퀀스 SO 데이터가 없습니다.");
        }
    }

    private IEnumerator EventFocusRoutine(Transform target, CameraSequencePresetSO sequenceSO)
    {
        GameState previousState = currentState;
        ChangeState(GameState.CinematicFocus);

        if (WorldCameraManager.Instance != null)
        {
            // 중앙 관제탑에 SO 데이터 기반 연출 재생 명령 (타겟이 있다면 내부적으로 활용 가능하도록 추후 확장 가능)
            WorldCameraManager.Instance.PlayCameraSequence(sequenceSO);
        }

        // 시퀀스 내부에 정의된 총 재생 시간(보간 시간 + 유지 시간)을 계산하여 자동으로 대기합니다.
        float totalDuration = 0f;
        if (sequenceSO.steps != null)
        {
            foreach (var step in sequenceSO.steps)
            {
                totalDuration += step.blendDuration + step.holdDuration;
            }
        }

        // 무한 대기(999s 등 UI 상태)가 아니라면 해당 컷신 시간만큼 대기 후 상태를 원상 복구합니다.
        if (totalDuration < 900f)
        {
            yield return new WaitForSeconds(totalDuration);
            ChangeState(previousState);

            // 상태 복구 시에도 무기 장착 여부를 검사하여 올바른 뷰로 리셋되도록 호출
            SetGamePlaySituation(previousState);
        }
    }

    // =========================================================================================
    // 🚨 [핵심 라우팅] 특정 상황으로 상태를 변경하고 카메라 포커싱을 동시에 요청합니다.
    // =========================================================================================
    public void SetGamePlaySituation(GameState newState, Transform target = null)
    {
        ChangeState(newState); // 상태 변경 이벤트 브로드캐스트

        if (WorldCameraManager.Instance == null) return;

        // 하드코딩된 물리 연산이나 값을 넘기지 않고, 각 상황에 맞는 '순수 SO 에셋'만 관제탑으로 전송합니다.
        switch (newState)
        {
            case GameState.LockOn:
                if (target != null)
                {
                    // 락온된 타겟의 콜라이더 크기나, 특수 보스 태그 등을 검사하여 판별
                    // 예: 타겟의 콜라이더 높이가 3m 이상이면 거대 몬스터로 간주
                    CharacterController targetController = target.GetComponent<CharacterController>();
                    bool isGiant = (targetController != null && targetController.height > 3.0f) || target.CompareTag("Boss");

                    if (isGiant && lockOnGiantCameraSequence != null)
                    {
                        WorldCameraManager.Instance.PlayCameraSequence(lockOnGiantCameraSequence);
                    }
                    else if (lockOnCameraSequence != null)
                    {
                        WorldCameraManager.Instance.PlayCameraSequence(lockOnCameraSequence);
                    }
                }
                else if (lockOnCameraSequence != null)
                {
                    // 타겟 참조가 소실된 안전망(Fallback) 처리
                    WorldCameraManager.Instance.PlayCameraSequence(lockOnCameraSequence);
                }
                break;

            case GameState.Inventory:
                if (inventoryCameraSequence != null)
                    WorldCameraManager.Instance.PlayCameraSequence(inventoryCameraSequence);
                break;

            case GameState.Table:
                if (tableCameraSequence != null)
                    WorldCameraManager.Instance.PlayCameraSequence(tableCameraSequence);
                break;

            case GameState.Cooking:
                if (cookingCameraSequence != null)
                    WorldCameraManager.Instance.PlayCameraSequence(cookingCameraSequence);
                break;

            case GameState.CinematicFocus:
                // 별도 외부 호출(TriggerEventFocus)에서 코루틴으로 전담하므로 스위치에서는 무시합니다.
                break;

            case GameState.Normal:
            default:
                // ====================================================================================
                // [신규 스마트 라우팅] 무기를 들고 있는지(논타겟 전투) vs 맨손인지(탐험) 판별
                // ====================================================================================
                bool isArmed = false;
                PlayerManager localPlayer = GetLocalPlayer();

                if (localPlayer != null && localPlayer.playerNetworkManager != null)
                {
                    int rightWepID = localPlayer.playerNetworkManager.currentRightHandWeaponID.Value;
                    int leftWepID = localPlayer.playerNetworkManager.currentLeftHandWeaponID.Value;

                    int unarmedID = -1;
                    if (WorldItemDatabase.Instance != null && WorldItemDatabase.Instance.unarmedWeapon != null)
                    {
                        unarmedID = WorldItemDatabase.Instance.unarmedWeapon.itemID;
                    }

                    // 양손 중 하나라도 맨손(unarmedID)이 아닌 진짜 무기/방패를 들고 있다면 '무장 상태'로 간주
                    if ((rightWepID != unarmedID && rightWepID != -1) ||
                        (leftWepID != unarmedID && leftWepID != -1))
                    {
                        isArmed = true;
                    }
                }

                if (isArmed && freeAimCombatCameraSequence != null)
                {
                    // 논타겟 전투 시점: 넓은 시야각, 마우스 자유 추적 등
                    WorldCameraManager.Instance.PlayCameraSequence(freeAimCombatCameraSequence);
                    if (showDebugLogs) Debug.Log("<color=cyan>[WorldGameState]</color> 무장 상태 확인됨. 논타겟 전투(FreeAim) 시점으로 복귀합니다.");
                }
                else if (normalCameraSequence != null)
                {
                    // 비무장 탐험 시점: 편안하고 좁은 숄더뷰
                    WorldCameraManager.Instance.PlayCameraSequence(normalCameraSequence);
                    if (showDebugLogs) Debug.Log("<color=gray>[WorldGameState]</color> 비무장 상태 확인됨. 기본 탐험(Normal) 시점으로 복귀합니다.");
                }
                else
                {
                    if (showDebugLogs) Debug.LogWarning("<color=red>[WorldGameState]</color> 🚨 Normal 카메라 시퀀스 SO가 할당되지 않아 뷰 복귀에 실패했습니다!");
                }
                break;
        }
    }

    /// <summary>
    /// 현재 씬에 존재하는 로컬 플레이어(IsOwner)를 찾아 반환합니다.
    /// 잦은 탐색 비용을 줄이기 위해 캐싱을 사용합니다.
    /// </summary>
    private PlayerManager GetLocalPlayer()
    {
        if (localPlayerCached != null) return localPlayerCached;

        PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (p.IsOwner)
            {
                localPlayerCached = p;
                return p;
            }
        }
        return null;
    }

    // 정책 : 이동허용여부, 공격 허용 여부, 상호작용 허용 여부
    public bool IsMovementAllowed() => currentState == GameState.Normal || currentState == GameState.LockOn || currentState == GameState.Chase || currentState == GameState.Inventory;
    public bool IsCombatAllowed() => currentState == GameState.Normal || currentState == GameState.LockOn;
    public bool IsInteractionAllowed() => currentState != GameState.CinematicFocus;
}