using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 가방의 위치 전이(등 <-> 바닥) 및 투명 셰이더 적용, 내부 아이템 생성을 관리합니다. 
/// 데이터 변경(NetworkList)을 감지하여 시각적 모델을 실시간으로 동기화하며,
/// EquipmentItem SO에 설정된 backpackModelScale 값을 기준으로 크기를 고정합니다. (최종 보완본)
/// </summary>
public class BagVisualController : MonoBehaviour
{
    [Header("Anchors")]
    [SerializeField] public Transform spineAnchor;      // 캐릭터 등에 고정될 위치 (Spine_Anchor_Slot)
    [SerializeField] public Transform internalGridRoot; // 내부 아이템 및 슬롯 생성 위치

    [Header("Visual Effects")]
    [SerializeField] private MeshRenderer bagMeshRenderer;
    [SerializeField] private Material opaqueMaterial;
    [SerializeField] private Material transparentMaterial;

    [Header("Slot UI Settings")]
    [SerializeField] private GameObject slotPrefab;      // 슬롯으로 사용할 반투명 큐브 프리팹
    [SerializeField] private Material slotDefaultMaterial; // [중요] Rendering Mode가 Transparent인 머티리얼이어야 합니다.
    [SerializeField] public float slotPadding = 0.08f;    // SO 데이터가 유효하지 않을 때 사용할 기본 간격
    [SerializeField] private float slotSizeMultiplier = 0.95f; // 슬롯 배경 크기 조절
    [SerializeField] private float itemModelScaleOffset = 0.85f; // 아이템 모델이 슬롯 내부에 들어가는 비율

    [Header("Open State Displacement")]
    [SerializeField] private float groundYOffset = 0.05f; // 바닥에 파묻히는 것을 방지하기 위한 높이 보정

    [Header("Colors")]
    public Color colorDefault = new Color(1, 1, 1, 0.2f);
    public Color colorValid = new Color(0, 1, 0, 0.5f);
    public Color colorInvalid = new Color(1, 0, 0, 0.5f);

    private CharacterInventoryManager inventory;
    private GameObject currentBagObject;
    private EquipmentItem currentBackpackData; // 현재 장착된 가방의 SO 데이터
    private GameObject[,] spawnedSlots; // 생성된 슬롯 객체들을 관리하는 배열
    private Vector3 originalLocalScale; // 가방 프리팹의 원래 스케일 저장용

    private void Awake()
    {
        inventory = GetComponentInParent<CharacterInventoryManager>();

        // [추가] 월드 상자 등의 경우 Awake 시점에 미리 렌더러와 그리드 루트를 찾아둡니다.
        if (bagMeshRenderer == null) bagMeshRenderer = GetComponentInChildren<MeshRenderer>();
        if (internalGridRoot == null) internalGridRoot = transform.Find("Internal_Grid_Root");
    }

    // 데이터 변경 시 자동으로 시각화 업데이트를 위한 이벤트 구독
    public void OnEnable()
    {
        if (inventory != null && inventory.GetInventoryItems() != null)
        {
            inventory.GetInventoryItems().OnListChanged += OnInventoryListChanged;
        }
    }

    public void OnDisable()
    {
        if (inventory != null && inventory.GetInventoryItems() != null)
        {
            inventory.GetInventoryItems().OnListChanged -= OnInventoryListChanged;
        }
    }

    private void OnInventoryListChanged(NetworkListEvent<InventoryItem> changeEvent)
    {
        // 인벤토리가 열려 있는 상태에서 데이터가 변하면 즉시 다시 그림
        var playerInv = inventory as PlayerInventoryManager;

        // 플레이어 본인의 인벤토리이거나, 현재 루팅 중인 상자인 경우 업데이트
        bool isMyInventory = playerInv != null && playerInv.isInventoryOpen;
        bool isLootingThis = playerInv != null && playerInv.currentLootContainer != null && playerInv.currentLootContainer.gameObject == gameObject;

        if (isMyInventory || isLootingThis)
        {
            Refresh3DInventory();
        }
    }

    /// <summary>
    /// PlayerEquipmentManager에서 새 가방이 생성될 때 호출하여 제어권을 넘겨받고 데이터를 초기화합니다.
    /// </summary>
    public void InitializeBagModel(GameObject model, Transform anchor, EquipmentItem data)
    {
        currentBagObject = model;
        spineAnchor = anchor;
        currentBackpackData = data;

        originalLocalScale = model.transform.localScale;

        // NGO 컴포넌트 제거 시 발생하는 좌표/스케일 리셋 방지를 위해 잠시 부모 해제
        model.transform.SetParent(null);

        // 모델 및 그 자식들로부터 모든 NetworkObject를 즉시 제거
        NetworkObject[] allNetworkObjects = model.GetComponentsInChildren<NetworkObject>(true);
        foreach (var no in allNetworkObjects)
        {
            DestroyImmediate(no);
        }

        // 2. 부모(Spine Anchor)로 이동 및 스케일 보정
        if (spineAnchor != null)
        {
            model.transform.SetParent(spineAnchor, true);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            Vector3 targetScale = (currentBackpackData != null) ? currentBackpackData.backpackModelScale : Vector3.one;

            model.transform.localScale = new Vector3(
                targetScale.x / spineAnchor.lossyScale.x,
                targetScale.y / spineAnchor.lossyScale.y,
                targetScale.z / spineAnchor.lossyScale.z
            );
        }

        bagMeshRenderer = model.GetComponentInChildren<MeshRenderer>();

        Transform foundRoot = model.transform.Find("Internal_Grid_Root");
        if (foundRoot != null) internalGridRoot = foundRoot;

        if (bagMeshRenderer != null && opaqueMaterial != null)
        {
            bagMeshRenderer.material = opaqueMaterial;
        }

        Debug.Log($"<color=white>[BagVisual] 모델 초기화 완료: {model.name}.</color>");
    }

    /// <summary>
    /// 가방/상자를 바닥 위치로 설정하거나 투명화 처리를 수행합니다.
    /// </summary>
    public void SetBagToWorldPivot(Transform pivot, bool isTransparent)
    {
        if (currentBagObject == null)
            currentBagObject = FindBagInSpine();

        // [보완] 만약 여전히 없다면, 상자(월드 오브젝트) 자신을 제어 대상으로 설정
        if (currentBagObject == null)
        {
            currentBagObject = gameObject;
            Debug.Log("[BagVisual] 장착 가방을 찾지 못해 월드 오브젝트 본체를 대상으로 설정합니다.");
        }

        // 1. 부모 해제 및 위치 설정 (상자는 위치를 옮기지 않으므로 분기 처리)
        if (spineAnchor != null && currentBagObject.transform.parent == spineAnchor)
        {
            currentBagObject.transform.SetParent(null);
            currentBagObject.transform.position = pivot.position + (Vector3.up * groundYOffset);

            // 2. 가방 데이터(SO)에 정의된 각도로 가방을 눕힙니다.
            if (currentBackpackData != null)
            {
                currentBagObject.transform.rotation = pivot.rotation * Quaternion.Euler(currentBackpackData.backpackLyingRotation);
                currentBagObject.transform.localScale = currentBackpackData.backpackModelScale;
            }
            else
            {
                currentBagObject.transform.rotation = pivot.rotation * Quaternion.Euler(90, 0, 0);
            }
        }

        // 3. 렌더러 확인 및 재질 적용
        if (bagMeshRenderer == null) bagMeshRenderer = currentBagObject.GetComponentInChildren<MeshRenderer>();
        if (bagMeshRenderer != null)
            bagMeshRenderer.material = isTransparent ? transparentMaterial : opaqueMaterial;

        // 가방/상자를 열 때 격자와 아이템을 새로고침합니다.
        if (isTransparent)
        {
            Refresh3DInventory();
        }
    }

    public void SetBagToSpine()
    {
        // 상자인 경우(spineAnchor 없음) 투명화만 해제하고 종료
        if (spineAnchor == null)
        {
            if (bagMeshRenderer != null) bagMeshRenderer.material = opaqueMaterial;
            ClearInternalModels();
            return;
        }

        if (currentBagObject == null)
            currentBagObject = FindBagInSpine();

        if (currentBagObject == null) return;

        // 등에 다시 멜 때도 부모 스케일 영향을 상쇄하여 SO의 스케일 유지
        currentBagObject.transform.SetParent(spineAnchor, true);
        currentBagObject.transform.localPosition = Vector3.zero;
        currentBagObject.transform.localRotation = Quaternion.identity;

        Vector3 targetScale = (currentBackpackData != null) ? currentBackpackData.backpackModelScale : Vector3.one;
        currentBagObject.transform.localScale = new Vector3(
            targetScale.x / spineAnchor.lossyScale.x,
            targetScale.y / spineAnchor.lossyScale.y,
            targetScale.z / spineAnchor.lossyScale.z
        );

        if (bagMeshRenderer != null) bagMeshRenderer.material = opaqueMaterial;

        if (internalGridRoot != null)
        {
            internalGridRoot.localPosition = Vector3.zero;
            internalGridRoot.localRotation = Quaternion.identity;
        }

        ClearInternalModels();
    }

    public void Refresh3DInventory()
    {
        ClearInternalModels();
        if (internalGridRoot == null || inventory == null)
        {
            Debug.LogWarning($"[BagVisual] {gameObject.name}: GridRoot({internalGridRoot != null}) 또는 Inventory({inventory != null}) 참조가 없습니다.");
            return;
        }

        int width = inventory.currentGridWidth.Value;
        int height = inventory.currentGridHeight.Value;
        float padding = (currentBackpackData != null && currentBackpackData.backpackSlotPadding > 0)
                        ? currentBackpackData.backpackSlotPadding : slotPadding;

        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning($"[BagVisual] {gameObject.name}: 격자 크기가 0입니다. (W:{width}, H:{height})");
            return;
        }

        // SO 커스텀 레이아웃 적용 (장착 가방인 경우에만)
        if (currentBackpackData != null)
        {
            internalGridRoot.localRotation = Quaternion.Euler(currentBackpackData.internalGridLocalRotation);
            internalGridRoot.localPosition = currentBackpackData.internalGridLocalOffset;
        }

        float totalGridWidth = (width - 1) * padding;
        float totalGridHeight = (height - 1) * padding;
        Vector3 centeringOffset = new Vector3(-totalGridWidth / 2f, totalGridHeight / 2f, 0);

        spawnedSlots = new GameObject[width, height];

        // 1. 배경 격자 슬롯 생성
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 slotPos = centeringOffset + new Vector3(x * padding, -y * padding, 0.002f);
                GameObject slot = (slotPrefab != null) ? Instantiate(slotPrefab, internalGridRoot) : GameObject.CreatePrimitive(PrimitiveType.Cube);

                slot.layer = LayerMask.NameToLayer("Bag");

                if (slotPrefab == null)
                {
                    slot.transform.SetParent(internalGridRoot);
                    if (slot.GetComponent<BoxCollider>()) slot.GetComponent<BoxCollider>().enabled = true;
                }

                slot.name = $"Slot_{x}_{y}";
                slot.transform.localPosition = slotPos;
                slot.transform.localRotation = Quaternion.identity;
                slot.transform.localScale = new Vector3(padding * slotSizeMultiplier, padding * slotSizeMultiplier, 0.005f);

                var renderer = slot.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    if (slotDefaultMaterial != null) renderer.material = slotDefaultMaterial;
                    renderer.material.color = colorDefault;
                }
                spawnedSlots[x, y] = slot;
            }
        }

        // 2. 실제 아이템 모델 생성
        var items = inventory.GetInventoryItems();
        foreach (var data in items)
        {
            Item itemSO = WorldItemDatabase.Instance.GetItemByID(data.itemID);
            if (itemSO != null && itemSO.itemModel != null)
            {
                Vector3 itemPos = centeringOffset + new Vector3(data.gridPositionX * padding, -data.gridPositionY * padding, -0.01f);

                GameObject model = Instantiate(itemSO.itemModel, internalGridRoot);
                model.transform.localPosition = itemPos;
                model.transform.localRotation = Quaternion.Euler(0, 0, data.rotationAngle);
                model.layer = LayerMask.NameToLayer("InventoryItem");

                AdjustModelScaleToSlot(model, itemSO, padding);

                Rigidbody rb = model.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                Collider col = model.GetComponent<Collider>();
                if (col != null) col.enabled = true;

                NetworkObject no = model.GetComponent<NetworkObject>();
                if (no != null) DestroyImmediate(no);

                var visual = model.AddComponent<InventoryItemVisual>();
                visual.itemData = data;
            }
        }

        Debug.Log($"<color=white>[BagVisual] {gameObject.name}: 격자 및 {items.Count}개의 아이템 시각화 완료.</color>");
    }

    private void AdjustModelScaleToSlot(GameObject model, Item itemSO, float padding)
    {
        Renderer modelRenderer = model.GetComponentInChildren<Renderer>();
        if (modelRenderer == null) return;
        Vector3 currentSize = modelRenderer.bounds.size;
        float targetWidth = padding * itemSO.itemSizeWidth * itemModelScaleOffset;
        float targetHeight = padding * itemSO.itemSizeHeight * itemModelScaleOffset;
        float finalScaleFactor = Mathf.Min(targetWidth / currentSize.x, targetHeight / currentSize.y);
        model.transform.localScale *= finalScaleFactor;
    }

    public void HighlightSlots(int startX, int startY, int w, int h, bool isValid)
    {
        if (spawnedSlots == null) return;
        ResetSlotColors();
        Color targetColor = isValid ? colorValid : colorInvalid;
        for (int y = startY; y < startY + h; y++)
        {
            for (int x = startX; x < startX + w; x++)
            {
                if (x >= 0 && x < spawnedSlots.GetLength(0) && y >= 0 && y < spawnedSlots.GetLength(1))
                {
                    var renderer = spawnedSlots[x, y].GetComponent<MeshRenderer>();
                    if (renderer != null) renderer.material.color = targetColor;
                }
            }
        }
    }

    public void ResetSlotColors()
    {
        if (spawnedSlots == null) return;
        foreach (var slot in spawnedSlots)
        {
            if (slot == null) continue;
            var renderer = slot.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.material.color = colorDefault;
        }
    }

    private void ClearInternalModels()
    {
        if (internalGridRoot == null) return;
        foreach (Transform child in internalGridRoot)
        {
            if (child != null) Destroy(child.gameObject);
        }
        spawnedSlots = null;
    }

    private GameObject FindBagInSpine()
    {
        if (spineAnchor != null)
        {
            foreach (Transform child in spineAnchor)
                if (child.name.StartsWith("Backpack_") || child.name.Contains("Bag")) return child.gameObject;
        }
        return null;
    }

    private Transform FindSpineAnchorInParent()
    {
        return FindChildRecursive(transform.root, "Backpack_Anchor_Slot");
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform result = FindChildRecursive(child, name);
            if (result != null) return result;
        }
        return null;
    }
}