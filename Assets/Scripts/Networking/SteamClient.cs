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

    // [Step 1 / P1-1] Steam API 수명의 단독 소유 인스턴스 표시.
    // 기존에는 중복 인스턴스가 Destroy될 때도 OnDestroy → ShutdownSteam()이 호출되어
    // 살아있는 원본의 Steam API까지 통째로 꺼지는 결함이 있었다.
    private bool isOwner = false;

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
            isOwner = true;                  // [Step 1 / P1-1] 이 인스턴스만 종료 권한 보유
            DontDestroyOnLoad(gameObject);   // [Step 1 / P1-1] 주석의 의도를 실제 코드로 — 씬 전환 시 Steam API 유지
            Debug.Log($"[Steam] Steam 초기화 성공 (AppID: {steamAppId})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Steam] Steam 초기화 실패: {e.Message}");
        }
    }

    void Update()
    {
        // [Step 1 / P1-1] 콜백 펌핑의 유일한 지점 — RunCallbacks는 여기서만 호출한다.
        // (기존에는 SteamLobbyManager.Update와 이중 펌핑되어 프레임당 2회 실행되었음)
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
        // [Step 1 / P1-1] 소유 인스턴스만 종료 가능 — 중복 인스턴스의 파괴가
        // 살아있는 Steam API를 끌 수 없게 차단한다.
        if (!isOwner) return;
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