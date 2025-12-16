using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WorldAIManager : MonoBehaviour
{
    public static WorldAIManager Instant {get; private set;}

    [Header("Debug")]
    [SerializeField] bool despawnCharacters = false;
    [SerializeField] bool respawnCharacters = false;

    [Header("Characters")]
    [SerializeField] GameObject[] aiCharacters;
    [SerializeField] List<GameObject> spawnedInCharacters;

    private void Awake()
    {
        if (Instant == null)
        {
            Instant = this;
        }
        else
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            // 씬에 있는 모든 ai 스폰.
            StartCoroutine(WaitForSceneToLoadThenSpawnCharacters());
        }
    }

    private void Update()
    {
        if (respawnCharacters)
        {
            respawnCharacters = false;
            SpawnAllCharacters();
        }
        if (despawnCharacters)
        {
            despawnCharacters = false;
            DespawnAllCharacters();
        }
    }

    // 로딩되기전 스폰되어 땅밑으로 떨어지는등 방지.
    private IEnumerator WaitForSceneToLoadThenSpawnCharacters()
    {
        while (!SceneManager.GetActiveScene().isLoaded)
        {
            yield return null;
        }
        SpawnAllCharacters();
    }

    private void SpawnAllCharacters()
    {
        foreach (var character in aiCharacters)
        {
            GameObject instantiatedCharacter = Instantiate(character);
            instantiatedCharacter.GetComponent<NetworkObject>().Spawn();
            spawnedInCharacters.Add(instantiatedCharacter);
        }
    }

    private void DespawnAllCharacters()
    {
        foreach (var character in spawnedInCharacters)
        {
            character.GetComponent<NetworkObject>().Despawn();
        }
    }

    private void DisableAllCharacters()
    {
        // 게임오브젝트를 비활성화하기 위해서, 네트워크에 비활성화상태를 싱크.
        // 만약 비활성화상태가 참일시, 클라이언트가 접속시 게임오브젝트를 비활성화
        // 먼 거리의 오브젝트를 메모리 절약을 위해 비활성화 할 수 있음.
        // 캐릭터가 지역마다 나뉠 수 있음 등(area_01,area_02).

    }
}
