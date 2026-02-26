using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/* * [SteamClient 수정 버전]
 * 스팀 API의 초기화와 안전한 종료를 담당합니다.
 * 유니티 에디터 종료 시 스팀 상태가 유지되는 문제를 해결했습니다.
 */

public class SteamClient : MonoBehaviour
{
    /// <summary>
    /// The steam app id. The default value of 480 is a public app id for development.
    /// </summary>
    public uint steamAppId = 480;

    // 싱글톤 패턴을 적용하여 중복 초기화를 방지할 수도 있습니다.
    private static bool isInitialized = false;

    void Awake()
    {
        // 씬 전환 시 파괴되지 않도록 설정 (이미 존재한다면 파괴)
        if (isInitialized)
        {
            Destroy(gameObject);
            return;
        }

        try
        {
            Steamworks.SteamClient.Init(steamAppId);
            isInitialized = true;
            Debug.Log($"[Steam] Steam 초기화 성공 (AppID: {steamAppId})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Steam] Steam 초기화 실패: {e.Message}");
        }
    }

    void Update()
    {
        // 스팀 콜백은 매 프레임 호출되어야 이벤트를 수신할 수 있습니다.
        if (isInitialized)
        {
            Steamworks.SteamClient.RunCallbacks();
        }
    }

    // 유니티 에디터 플레이 모드 중지 또는 빌드된 게임 종료 시 호출됩니다.
    private void OnApplicationQuit()
    {
        ShutdownSteam();
    }

    // 오브젝트가 파괴될 때(씬 전환 등) 호출됩니다.
    private void OnDestroy()
    {
        // 싱글톤인 경우 OnApplicationQuit에서 처리하는 것이 안전하지만, 
        // 확실한 종료를 위해 체크합니다.
        ShutdownSteam();
    }

    private void ShutdownSteam()
    {
        if (isInitialized)
        {
            Steamworks.SteamClient.Shutdown();
            isInitialized = false;
            Debug.Log("[Steam] Steam API가 안전하게 종료되었습니다.");
        }
    }
}