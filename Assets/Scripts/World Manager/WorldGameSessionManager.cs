using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using TDA.Character.Player; // PlayerManager 참조를 위해 추가

public class WorldGameSessionManager : MonoBehaviour
{
    public static WorldGameSessionManager Instance {get; private set;}

    [Header("Active Players In Session")]
    public List<PlayerManager> players = new List<PlayerManager>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void AddPlayerToActivePlayerList(PlayerManager player)
    {
        // 유저가 존재시 두번 추가 방지용
        if (!players.Contains(player))
        {
            players.Add(player);
        }

        // Null Slots 을 리스트에서 확인하고, null 슬롯을 지우기.
        for (int i = players.Count - 1; i > -1; i--)
        {
            // i번째에서 null 이라면, 해당은 지운다.
            if (players[i] == null)
            {
                players.RemoveAt(i);
            }
        }
    }

    public void RemovePlayerFromActivePlayerList(PlayerManager player)
    {
        // 유저가 존재시 제거.
        if (!players.Contains(player))
        {
            players.Remove(player);
        }

        // Null Slots 을 리스트에서 확인하고, null 슬롯을 지우기.
        for (int i = players.Count - 1; i > -1; i--)
        {
            // i번째에서 null 이라면, 해당은 지운다.
            if (players[i] == null)
            {
                players.RemoveAt(i);
            }
        }
    }
}
