using SG;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 월드 상자나 시체 등 루팅 가능한 객체와의 상호작용을 관리합니다.
/// 플레이어 인벤토리 매니저와 연동하여 카메라 전환 및 아이템 시각화를 처리합니다.
/// </summary>
public class LootableInteractable : InteractableObject
{
    [Header("Looting Settings")]
    [Tooltip("카메라가 루팅 시 바라볼 지점입니다. 상자 앞이나 위쪽을 지정하세요.")]
    [SerializeField] private Transform cameraPivot;

    private WorldInventoryManager worldInventory;
    private BagVisualController visualController;

    protected virtual void Awake()
    {
        worldInventory = GetComponent<WorldInventoryManager>();
        visualController = GetComponentInChildren<BagVisualController>();

        // 상호작용 기본 텍스트 설정
        interactableName = "상자";
    }

    /// <summary>
    /// 플레이어가 E키로 상호작용했을 때 호출됩니다.
    /// </summary>
    public override void Interact(CharacterManager character)
    {
        PlayerManager player = character as PlayerManager;
        if (player == null || !player.IsOwner) return;

        Debug.Log($"<color=cyan>[Looting] 상자 열기 시도: {gameObject.name}</color>");

        // 1. 상태 등록 (이 로직이 있어야 플레이어가 이동할 때 인벤토리와 함께 닫힙니다.)
        OpenLootContainer(player);

        // 2. 카메라 시점 전환
        if (PlayerCamera.Instance != null)
        {
            // 인벤토리 카메라를 활성화합니다. 
            // TODO: PlayerCamera에서 cameraPivot 위치로 카메라를 이동시키는 로직이 연동되어야 합니다.
            PlayerCamera.Instance.ToggleInventoryCamera(true);
        }

        // 3. 비주얼 처리 (상자 투명화 및 내부 그리드 표시)
        if (visualController != null)
        {
            // 상자 모델 투명화 시퀀스 실행
            visualController.SetBagToWorldPivot(transform, true);

            // [수정] 에러 해결: 존재하지 않는 RefreshLootContainerVisuals 대신 Refresh3DInventory 호출
            // worldInventory.inventoryItems 직접 접근 대신 내부 로직 활용
            if (worldInventory != null)
            {
                visualController.Refresh3DInventory();
                Debug.Log($"[Looting] {worldInventory.GetInventoryItems().Count}개의 아이템을 시각화합니다.");
            }
        }
    }

    /// <summary>
    /// 플레이어 매니저에 루팅 상태를 등록합니다.
    /// </summary>
    private void OpenLootContainer(PlayerManager player)
    {
        // 인벤토리가 열린 상태로 간주하여 이동 시 HandleAutoCloseOnMovement가 동작하게 합니다.
        player.playerInventoryManager.isInventoryOpen = true;

        // [핵심] 플레이어 매니저에 현재 열린 상자를 등록합니다.
        // PlayerInventoryManager.cs에 'public LootableInteractable currentLootContainer;' 변수가 선언되어 있어야 합니다.
        player.playerInventoryManager.currentLootContainer = this;
    }

    /// <summary>
    /// 루팅을 종료할 때 호출됩니다. (움직여서 멀어지거나 다시 닫을 때)
    /// </summary>
    public void CloseLootContainer()
    {
        Debug.Log($"<color=orange>[Looting] 상자 닫기 시퀀스 실행: {gameObject.name}</color>");

        // 1. 카메라 복구
        if (PlayerCamera.Instance != null)
            PlayerCamera.Instance.ToggleInventoryCamera(false);

        // 2. 비주얼 복구
        if (visualController != null)
        {
            // 가방을 등에 메는 함수(SetBagToSpine)와 유사하게 상자의 비주얼을 불투명으로 돌리고 그리드를 지웁니다.
            // 상자의 경우 앵커가 없으므로 로직상 투명화만 해제합니다.
            visualController.SetBagToSpine();
        }
    }
}