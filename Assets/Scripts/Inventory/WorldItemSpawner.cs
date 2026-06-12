using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 아이템 데이터를 월드 상의 물리 객체(Prefab)로 스폰하는 서버 전용 매니저입니다. (Dev A)
/// 일반 아이템 드롭과 아이템이 담긴 가방 스폰을 모두 지원합니다.
/// </summary>
public class WorldItemSpawner : NetworkBehaviour
{
    public static WorldItemSpawner Instance { get; private set; }

    [Header("Spawn Settings")]
    [SerializeField] private GameObject pickUpItemPrefab; // 기본 아이템 보관용 컨테이너 프리팹
    [SerializeField] private float dropForce = 2f;        // 아이템이 던져지는 힘

    private void Awake()
    {
        // [Step 1 / P2-8 최소 가드] 중복 인스턴스는 즉시 파괴 후 종료 —
        // 기존에는 return 누락으로 파괴 예정인 중복 인스턴스에도 DontDestroyOnLoad가
        // 적용되었다. 재호스팅/씬 전환 반복(SCN-02) 측정 오염 방지 (전면 정리는 Step 5).
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 서버에서 일반 아이템을 월드에 생성합니다. (물리 효과 포함)
    /// </summary>
    public void SpawnItemInWorld(int itemID, int stackCount, Vector3 spawnPosition, Vector3 direction)
    {
        if (!IsServer) return;

        // 1. 데이터베이스에서 아이템 정보 확인
        Item itemData = WorldItemDatabase.Instance.GetItemByID(itemID);
        if (itemData == null) return;

        // 2. 월드 아이템 프리팹 생성 (네트워크 오브젝트)
        GameObject pickupObject = Instantiate(pickUpItemPrefab, spawnPosition, Quaternion.identity);
        NetworkObject networkObj = pickupObject.GetComponent<NetworkObject>();
        networkObj.Spawn();

        // 3. 생성된 객체에 아이템 데이터 설정 (모든 클라이언트에 동기화될 변수)
        PickUpItemInteractable interactable = pickupObject.GetComponent<PickUpItemInteractable>();
        if (interactable != null)
        {
            interactable.SetItemData(itemID, stackCount);
        }

        // 4. 물리 효과 적용 (캐릭터 앞으로 살짝 던져짐)
        ApplyPhysicalForce(pickupObject, direction);
    }

    /// <summary>
    /// 아이템이 미리 담긴 가방을 특정 위치에 스폰합니다.
    /// </summary>
    public void SpawnFilledBackpack(int bagID, List<InventoryItem> itemsToInside, Vector3 position, Vector3 direction = default)
    {
        if (!IsServer) return;

        GameObject pickupObject = Instantiate(pickUpItemPrefab, position, Quaternion.identity);
        NetworkObject networkObj = pickupObject.GetComponent<NetworkObject>();
        networkObj.Spawn();

        PickUpItemInteractable interactable = pickupObject.GetComponent<PickUpItemInteractable>();
        if (interactable != null)
        {
            // 가방 데이터와 내부 아이템 목록을 함께 설정
            interactable.SetItemData(bagID, 1, itemsToInside);
        }

        // 물리 효과 적용 (방향이 제공된 경우)
        if (direction != default)
        {
            ApplyPhysicalForce(pickupObject, direction);
        }
    }

    /// <summary>
    /// 생성된 월드 아이템에 물리적 힘과 회전을 부여합니다.
    /// </summary>
    private void ApplyPhysicalForce(GameObject target, Vector3 direction)
    {
        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddForce(direction * dropForce, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * dropForce, ForceMode.Impulse);
        }
    }
}