using System;
using UnityEngine;

// [수정사항] 상위 계층(WorldGameStateManager)에서 전달할 정책 데이터 구조체(Preset) 정의
// 개별 매개변수로 전달하던 방식을 통합하여 추후 필터나 노이즈 강도 등이 추가되어도 
// 함수의 파라미터를 수정할 필요 없이 확장성을 보장합니다.
[Serializable]
public struct CameraStancePreset
{
    public float fov;
    public float lerpSpeed;
    public Vector3 customOffset;
}

/// <summary>
/// 해당 클라이언트의 로컬 월드에서 카메라 제어를 총괄하는 매니저.
/// 멀티플레이어 환경에서는 오직 '나의 플레이어 카메라'만 관리함.
/// </summary>
public class WorldCameraManager : MonoBehaviour
{
    public static WorldCameraManager Instance;

    [Header("Active Local Camera")]
    // 현재 이 클라이언트의 주인인 플레이어가 사용하는 카메라
    [SerializeField] private PlayerCamera localPlayerCamera;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // 매니저는 씬 전환 시에도 유지되어야 함
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 플레이어가 스폰될 때, 로컬 플레이어인 경우에만 이 함수를 호출하여 등록함.
    /// </summary>
    public void RegisterLocalCamera(PlayerCamera cam)
    {
        localPlayerCamera = cam;
        Debug.Log("<color=green>WorldCameraManager: Local Player Camera Registered.</color>");
    }

    #region External API (WorldGameStateManager, 이벤트 시스템에서 호출)

    // [수정사항] 파편화되어 있던 매개변수들을 CameraStancePreset 구조체 하나로 묶어서 받도록 변경
    // 특정 오브젝트 포커싱 (예: 플레이어 A가 가방을 열 때 호출)
    public void StartFocus(Transform target, CameraStancePreset preset)
    {
        if (localPlayerCamera != null)
            localPlayerCamera.SetContextualFocus(target, preset);
    }

    public void StopFocus()
    {
        if (localPlayerCamera != null)
            localPlayerCamera.ClearContextualFocus();
    }

    // 타격감: 내 화면만 흔들림 (다른 유저의 카메라는 영향받지 않음)
    public void ShakeCamera(float intensity, float duration)
    {
        if (localPlayerCamera != null)
            localPlayerCamera.Shake(intensity, duration);
    }

    // 추격전 효과 등 강도 조절
    public void SetBodycamWeight(float weight)
    {
        if (localPlayerCamera != null)
            localPlayerCamera.SetBodycamWeight(weight);
    }
    #endregion
}