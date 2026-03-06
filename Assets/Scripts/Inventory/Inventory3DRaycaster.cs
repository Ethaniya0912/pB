using UnityEngine;
using TDA.Character.Player;

/// <summary>
/// 3D 공간의 아이템 드래그 앤 드롭 및 가방 클릭/벗기기를 처리합니다. (Dev B 최종 보완본)
/// 가방 드래그 시 시각적 변화를 최소화하고 드롭 시점에 매니저의 지연 로직(애니메이션 후 스폰)을 호출합니다.
/// </summary>
public class Inventory3DRaycaster : MonoBehaviour
{
    [Header("Interaction Layers")]
    [SerializeField] private LayerMask itemLayer;      // 가방 내부 아이템 (InventoryItem)
    [SerializeField] private LayerMask characterLayer; // 캐릭터 본체 (Character)
    [SerializeField] private LayerMask bagLayer;       // 가방 격자/바닥 (Bag)
    [SerializeField] private LayerMask backBagLayer;   // 등에 멘 가방 (BackBag)

    [Header("References")]
    [SerializeField] private BagVisualController bagVisual;

    private PlayerManager player;
    private Camera cam;
    private InventoryItemVisual currentDragged;
    private GameObject preview;
    private bool isDragging = false;
    private bool isDraggingBackBag = false; // 가방 자체를 드래그 중인지 여부

    /// <summary>
    /// 현재 드래그 상호작용이 진행 중인지 반환합니다.
    /// </summary>
    public bool GetIsDragging() => isDragging;

    private void Awake()
    {
        // 최상위 부모에서 PlayerManager를 찾습니다.
        player = GetComponentInParent<PlayerManager>();
        if (player == null) player = GetComponent<PlayerManager>();

        if (player == null)
        {
            Debug.LogError("[Raycaster] PlayerManager를 찾을 수 없습니다! 상호작용이 불가능합니다.");
        }
    }

    private void Start()
    {
        if (cam == null) cam = Camera.main;
        if (bagVisual == null) bagVisual = GetComponentInChildren<BagVisualController>();
    }

    private void Update()
    {
        // 안전 점검: 플레이어 참조가 없거나 권한이 없으면 중단
        if (player == null || !player.IsOwner || cam == null) return;

        // 1. 인벤토리가 닫혀 있을 때: Alt를 이용한 가방 상호작용 (벗어던지기 준비)
        if (!player.playerInventoryManager.isInventoryOpen)
        {
            // TD : 남규할일 - Alt키 입력 감지 후 가방 드래그 시작

            //if (PlayerInputManager.Instance.alt_Input)
            if(false) // 임시로 비활성화
            {
                if (Input.GetMouseButtonDown(0)) StartBackBagDragging();
            }
            else
            {
                // Alt가 없을 때는 기존 클릭으로 인벤토리 오픈
                if (Input.GetMouseButtonDown(0)) DetectBackBagClick();
            }
        }
        else
        {
            // 2. 인벤토리가 열려 있을 때: 일반 아이템 드래그 (이동/버리기)
            if (Input.GetMouseButtonDown(0)) StartDragging();
        }

        if (isDragging) UpdateDragging();
        if (Input.GetMouseButtonUp(0) && isDragging) FinishDragging();
    }

    private void DetectBackBagClick()
    {
        if (player == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 3f, backBagLayer))
        {
            player.playerInventoryManager.ToggleInventory();
        }
    }

    /// <summary>
    /// 캐릭터의 등에 있는 가방을 드래그하기 시작합니다. (Alt 조작용)
    /// 요구사항: 드래그 도중에도 등에 장착된 상태를 유지하며 가상의 모델이 따라다니지 않습니다.
    /// </summary>
    private void StartBackBagDragging()
    {
        if (player == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 3f, backBagLayer))
        {
            isDragging = true;
            isDraggingBackBag = true;

            // [수정] 드래그 시작 시 가방을 숨기지 않고 그대로 둡니다. (요구사항 반영)
            // [수정] 마우스를 따라다니는 프리뷰 모델을 생성하지 않습니다.
            Debug.Log("<color=yellow>[Raycaster] 가방 드래그 시작 (비주얼 유지)</color>");
        }
    }

    /// <summary>
    /// 가방 내부의 일반 아이템 드래그를 시작합니다.
    /// </summary>
    private void StartDragging()
    {
        if (player == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 5f, itemLayer))
        {
            var visual = hit.collider.GetComponent<InventoryItemVisual>();
            if (visual != null)
            {
                isDragging = true;
                isDraggingBackBag = false;
                currentDragged = visual;
                currentDragged.gameObject.SetActive(false);

                // 일반 아이템은 마우스를 따라다녀야 하므로 프리뷰 생성
                preview = Instantiate(currentDragged.gameObject);
                preview.SetActive(true);
                preview.layer = LayerMask.NameToLayer("Ignore Raycast");
                foreach (Transform child in preview.transform) child.gameObject.layer = preview.layer;
                preview.transform.localScale = currentDragged.transform.localScale;
            }
        }
    }

    private void UpdateDragging()
    {
        // 가방 드래그 중일 때는 마우스 위치에 모델을 갱신하지 않음
        if (isDraggingBackBag || preview == null) return;

        Vector3 mousePos = Input.mousePosition;
        mousePos.z = 0.6f;
        preview.transform.position = cam.ScreenToWorldPoint(mousePos);

        // R키를 이용한 아이템 회전 로직
        if (Input.GetKeyDown(KeyCode.R))
        {
            preview.transform.Rotate(Vector3.forward, 90f);
            var data = currentDragged.itemData;
            data.rotationAngle = (data.rotationAngle + 90f) % 360f;
            currentDragged.itemData = data;
        }
    }

    /// <summary>
    /// 드래그가 끝났을 때 드롭 위치를 판정하여 액션을 실행합니다.
    /// </summary>
    private void FinishDragging()
    {
        if (player == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        isDragging = false;

        if (preview != null)
        {
            Destroy(preview);
            preview = null;
        }

        bool hitAnything = Physics.Raycast(ray, out RaycastHit hit, 10f);
        bool droppedOnCharacter = hitAnything && (((1 << hit.collider.gameObject.layer) & characterLayer) != 0);

        if (isDraggingBackBag)
        {
            if (!droppedOnCharacter)
            {
                // [수정] 캐릭터 외부 드롭 시 매니저의 애니메이션 지연 스폰 시퀀스 호출
                Debug.Log("<color=red>[Raycaster] 가방 버리기 판정. 시퀀스 호출.</color>");
                player.playerInventoryManager.UnEquipAndDropBackpack();
            }
            else
            {
                Debug.Log("[Raycaster] 가방 드롭 취소 (캐릭터 위)");
            }
            isDraggingBackBag = false;
        }
        else if (currentDragged != null)
        {
            bool actionTaken = false;
            if (droppedOnCharacter)
            {
                // 캐릭터에게 드롭 (장착)
                EquipmentSlot slot = hit.collider.name.Contains("Head") ? EquipmentSlot.Helmet : EquipmentSlot.RightHand;
                player.playerInventoryManager.OnItemDroppedOnCharacter(currentDragged.itemData, slot);
                actionTaken = true;
            }
            else if (hitAnything && (((1 << hit.collider.gameObject.layer) & bagLayer) != 0))
            {
                // 가방 내부 격자로 드롭 (이동)
                Transform gridRoot = bagVisual.internalGridRoot;
                Vector3 localPoint = gridRoot.InverseTransformPoint(hit.point);
                float padding = bagVisual.slotPadding;
                float totalW = (player.playerInventoryManager.currentGridWidth.Value - 1) * padding;
                float totalH = (player.playerInventoryManager.currentGridHeight.Value - 1) * padding;
                Vector3 centeringOffset = new Vector3(-totalW / 2f, totalH / 2f, 0);

                int targetX = Mathf.RoundToInt((localPoint.x - centeringOffset.x) / padding);
                int targetY = Mathf.RoundToInt((centeringOffset.y - localPoint.y) / padding);

                player.playerInventoryManager.RequestMoveItemServerRpc(currentDragged.itemData, targetX, targetY);
                actionTaken = true;
            }
            else
            {
                // 월드 바닥으로 드롭 (아이템 버리기)
                player.playerInventoryManager.OnItemDroppedInWorld(currentDragged.itemData);
                actionTaken = true;
            }

            // 액션이 수행되지 않았다면 원래 아이템 모델 복구
            if (!actionTaken) currentDragged.gameObject.SetActive(true);
            currentDragged = null;
        }
    }
}