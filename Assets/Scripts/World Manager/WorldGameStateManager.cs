using System;
using System.Collections;
using UnityEngine;
using TDA.World; // WorldCameraManager 참조를 위해 추가

public class WorldGameStateManager : MonoBehaviour
{
    public static WorldGameStateManager Instance { get; private set; }

    [Header("State Info")]
    public GameState currentState = GameState.Normal;

    // 다른 매니저나 시스템이 상태 변화를 구독할 수 있게 이벤트 제공
    public delegate void StateChanged(GameState newState);
    public event StateChanged OnStatechanged;

    // [수정] WorldCameraManager에 정의된 통합 구조체(CameraStancePreset)를 사용하기 위해 기존 구조체는 주석 처리합니다.
    /*
    // 상황별 카메라 설정 데이터 구조체
    [System.Serializable]
    public struct CameraPreset
    {
        public Vector3 offset;      // 거리/위치
        public float fov;           // 화각
        public float lerpSpeed;     // 전이속도
    }
    */

    [Header("Camera Presets")]
    // [수정] 기존 CameraPreset을 CameraStancePreset으로 교체하고, 내부 변수명도 customOffset으로 맞췄습니다.
    public CameraStancePreset inventoryPreset = new CameraStancePreset { customOffset = new Vector3(0f, 1.5f, -0.5f), fov = 40f, lerpSpeed = 5f };
    public CameraStancePreset tablePreset = new CameraStancePreset { customOffset = new Vector3(0f, 3.0f, -0.2f), fov = 30f, lerpSpeed = 4f };
    public CameraStancePreset cookingPreset = new CameraStancePreset { customOffset = new Vector3(0f, 2.0f, -0.8f), fov = 45f, lerpSpeed = 5f };
    public CameraStancePreset lockOnPreset = new CameraStancePreset { customOffset = new Vector3(0.5f, 1.8f, -4f), fov = 55f, lerpSpeed = 7f };

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

    // 특정 이벤트 오브젝트로 카메라를 돌리는 범용 펑션
    public void TriggerEventFocus(Transform target, float duration = 2.0f, float zoomFov = 40f)
    {
        StartCoroutine(EventFocusRoutine(target, duration, zoomFov));
    }

    private IEnumerator EventFocusRoutine(Transform target, float duration, float zoomFov)
    {
        GameState previousState = currentState;
        ChangeState(GameState.CinematicFocus);

        // WorldCameraManager를 통해 실제 카메라에게 명령 전달.
        if (WorldCameraManager.Instance != null)
        {
            // 이벤트 포커싱은 기본 위에서 내려다보는 오프셋(예: 0, 2.5, -1)을 임시로 사용
            // [수정] 4개의 파라미터를 던지던 것을 즉석에서 CameraStancePreset 구조체로 묶어서 전달하도록 수정 (CS1501 에러 해결)
            CameraStancePreset eventPreset = new CameraStancePreset { fov = zoomFov, lerpSpeed = 5f, customOffset = new Vector3(0, 2.5f, -1f) };
            WorldCameraManager.Instance.StartFocus(target, eventPreset);
            // WorldCameraManager.Instance.StartFocus(target, zoomFov, 5f, new Vector3(0, 2.5f, -1f));
        }

        yield return new WaitForSeconds(duration);

        if (WorldCameraManager.Instance != null)
        {
            WorldCameraManager.Instance.StopFocus();
        }

        ChangeState(previousState);
    }

    // 특정 상황으로 상태를 변경하고 카메라 포커싱을 동시에 요청.

    public void SetGamePlaySituation(GameState newState, Transform target = null)
    {
        currentState = newState;

        if (WorldCameraManager.Instance == null) return;

        switch (newState)
        {
            case GameState.LockOn:
                // [수정] 4개의 파라미터를 개별로 보내지 않고 lockOnPreset 구조체 자체를 파라미터로 전송 (CS1501 에러 해결)
                WorldCameraManager.Instance.StartFocus(target, lockOnPreset);
                // WorldCameraManager.Instance.StartFocus(target,lockOnPreset.fov,lockOnPreset.lerpSpeed,lockOnPreset.offset);
                break;
            case GameState.Inventory:
                // [수정] 4개의 파라미터를 개별로 보내지 않고 inventoryPreset 구조체 자체를 파라미터로 전송 (CS1501 에러 해결)
                WorldCameraManager.Instance.StartFocus(target, inventoryPreset);
                // WorldCameraManager.Instance.StartFocus(target, inventoryPreset.fov, inventoryPreset.lerpSpeed, inventoryPreset.offset);
                break;
            case GameState.Table:
                break;
            case GameState.Cooking:
                break;
            case GameState.CinematicFocus:
                break;
            default:
                WorldCameraManager.Instance.StopFocus();
                break;
        }
    }

    // 정책 : 이동허용여부, 공격 허용 여부, 상호작용 허용 여부
    public bool IsMovementAllowed() => currentState == GameState.Normal || currentState == GameState.LockOn || currentState == GameState.Chase || currentState == GameState.Inventory;
    public bool IsCombatAllowed() => currentState == GameState.Normal || currentState == GameState.LockOn;
    public bool IsInteractionAllowed() => currentState != GameState.CinematicFocus;

}